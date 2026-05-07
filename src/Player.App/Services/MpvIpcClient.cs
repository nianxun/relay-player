using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Player.App.Services;

public sealed class MpvIpcClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 监听 mpv IPC 事件，定期上报播放进度，并返回本次播放是否自然播完。
    /// </summary>
    public async Task<MpvPlaybackResult> MonitorAsync(
        MpvLaunch launch,
        TimeSpan initialPosition,
        Func<TimeSpan, Task> reportProgressAsync,
        Func<TimeSpan, Task> reportStoppedAsync,
        CancellationToken cancellationToken)
    {
        var lastPosition = initialPosition;
        var endedNaturally = false;

        try
        {
            using var pipe = await ConnectAsync(launch.IpcPipeName, cancellationToken);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };

            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            await SendAsync(writer, new
            {
                command = new object[] { "observe_property", 1, "time-pos" }
            }, cancellationToken);

            var lastReport = DateTimeOffset.UtcNow;

            while (!cancellationToken.IsCancellationRequested && !launch.Process.HasExited)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (TryReadTimePosition(line, out var position))
                {
                    lastPosition = position;

                    if (DateTimeOffset.UtcNow - lastReport >= TimeSpan.FromSeconds(10))
                    {
                        await reportProgressAsync(lastPosition);
                        lastReport = DateTimeOffset.UtcNow;
                    }
                }

                if (TryReadEndFileReason(line, out var reason))
                {
                    endedNaturally = string.Equals(reason, "eof", StringComparison.OrdinalIgnoreCase);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await WaitForExitAsync(launch.Process, cancellationToken);
        }
        finally
        {
            await reportStoppedAsync(lastPosition);
        }

        return new MpvPlaybackResult(lastPosition, endedNaturally);
    }

    /// <summary>
    /// 在同一个 mpv IPC 会话中托管连播；每次自然结束后用 loadfile 切到下一集。
    /// </summary>
    public async Task MonitorAutoPlayAsync(
        MpvLaunch launch,
        MpvIpcPlaybackItem initialItem,
        CancellationToken cancellationToken)
    {
        var currentItem = initialItem;
        var lastPosition = initialItem.InitialPosition;
        var stoppedReported = false;

        try
        {
            using var pipe = await ConnectAsync(launch.IpcPipeName, cancellationToken);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };

            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            await SendAsync(writer, new
            {
                command = new object[] { "observe_property", 1, "time-pos" }
            }, cancellationToken);

            var lastReport = DateTimeOffset.UtcNow;

            while (!cancellationToken.IsCancellationRequested && !launch.Process.HasExited)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (TryReadTimePosition(line, out var position))
                {
                    lastPosition = position;

                    if (DateTimeOffset.UtcNow - lastReport >= TimeSpan.FromSeconds(10))
                    {
                        await currentItem.ReportProgressAsync(lastPosition);
                        lastReport = DateTimeOffset.UtcNow;
                    }
                }

                if (!TryReadEndFileReason(line, out var reason))
                {
                    continue;
                }

                await currentItem.ReportStoppedAsync(lastPosition);
                stoppedReported = true;

                if (!string.Equals(reason, "eof", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var nextItem = await currentItem.ResolveNextAsync();
                if (nextItem is null)
                {
                    await SendAsync(writer, new { command = new object[] { "quit" } }, cancellationToken);
                    break;
                }

                currentItem = nextItem;
                lastPosition = nextItem.InitialPosition;
                lastReport = DateTimeOffset.UtcNow;
                stoppedReported = false;

                await ApplyDisplayTitleAsync(writer, nextItem.DisplayTitle, cancellationToken);
                await SendAsync(writer, new
                {
                    command = BuildLoadFileCommand(nextItem.StreamUri, nextItem.InitialPosition)
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await WaitForExitAsync(launch.Process, cancellationToken);
        }
        finally
        {
            if (!stoppedReported)
            {
                await currentItem.ReportStoppedAsync(lastPosition);
            }
        }
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(500, cancellationToken);
                return pipe;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                lastError = ex;
                await pipe.DisposeAsync();
                await Task.Delay(250, cancellationToken);
            }
        }

        throw new IOException($"Could not connect to mpv IPC pipe '{pipeName}'.", lastError);
    }

    private static Task SendAsync(StreamWriter writer, object command, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(command, JsonOptions);
        return writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    /// <summary>
    /// 构造连播切集命令，并把起播位置作为 mpv 的 per-file option 传入。
    /// </summary>
    /// <remarks>
    /// 首集启动时可能携带 <c>--start</c> 续播参数；mpv 的命令行选项会影响后续文件。
    /// 因此下一集必须显式传 <c>start=0</c>，否则 mpv 会沿用上一集的历史进度。
    /// mpv 0.38 起 <c>loadfile</c> 的第四个参数是插入索引，使用 options 时需要把索引设为 -1。
    /// </remarks>
    internal static object[] BuildLoadFileCommand(Uri streamUri, TimeSpan initialPosition)
    {
        return
        [
            "loadfile",
            streamUri.ToString(),
            "replace",
            -1,
            $"start={FormatLoadFileStartOption(initialPosition)}"
        ];
    }

    /// <summary>
    /// 切换文件前更新 mpv 的标题相关选项，避免新一集继续显示上一集标题。
    /// </summary>
    private static async Task ApplyDisplayTitleAsync(
        StreamWriter writer,
        string displayTitle,
        CancellationToken cancellationToken)
    {
        var title = NormalizeIpcTitle(displayTitle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        await SendAsync(writer, new
        {
            command = new object[] { "set_property", "options/title", title }
        }, cancellationToken);

        await SendAsync(writer, new
        {
            command = new object[] { "set_property", "options/force-media-title", title }
        }, cancellationToken);
    }

    /// <summary>
    /// IPC 标题同样需要去掉控制字符，避免 JSON 命令写入后影响 mpv 解析。
    /// </summary>
    private static string NormalizeIpcTitle(string displayTitle)
    {
        if (string.IsNullOrWhiteSpace(displayTitle))
        {
            return string.Empty;
        }

        return new string(displayTitle.Where(character => !char.IsControl(character)).ToArray()).Trim();
    }

    /// <summary>
    /// 将 TimeSpan 转成 mpv loadfile options 接受的秒数字符串；零值显式保留为 0。
    /// </summary>
    private static string FormatLoadFileStartOption(TimeSpan position)
    {
        if (position <= TimeSpan.Zero)
        {
            return "0";
        }

        return position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool TryReadTimePosition(string line, out TimeSpan position)
    {
        position = TimeSpan.Zero;

        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("name", out var name) ||
                !string.Equals(name.GetString(), "time-pos", StringComparison.Ordinal))
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return false;
            }

            position = TimeSpan.FromSeconds(data.GetDouble());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 读取 mpv 的 end-file 事件；只有 reason=eof 才表示自然播完，关闭窗口或停止播放不能触发连播。
    /// </summary>
    private static bool TryReadEndFileReason(string line, out string reason)
    {
        reason = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("event", out var eventName) ||
                !string.Equals(eventName.GetString(), "end-file", StringComparison.Ordinal))
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("reason", out var reasonElement))
            {
                reason = reasonElement.GetString() ?? string.Empty;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        return process.HasExited ? Task.CompletedTask : process.WaitForExitAsync(cancellationToken);
    }
}

public sealed record MpvPlaybackResult(TimeSpan LastPosition, bool EndedNaturally);

public sealed record MpvIpcPlaybackItem(
    Uri StreamUri,
    string DisplayTitle,
    TimeSpan InitialPosition,
    Func<TimeSpan, Task> ReportProgressAsync,
    Func<TimeSpan, Task> ReportStoppedAsync,
    Func<Task<MpvIpcPlaybackItem?>> ResolveNextAsync);
