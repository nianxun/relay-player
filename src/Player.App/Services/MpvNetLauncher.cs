using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Player.App.Services;

public sealed class MpvNetLauncher
{
    public string ResolveExecutable(string configuredPath)
    {
        var normalizedConfiguredPath = NormalizeExecutablePath(configuredPath);
        if (!string.IsNullOrWhiteSpace(normalizedConfiguredPath) && File.Exists(normalizedConfiguredPath))
        {
            return normalizedConfiguredPath;
        }

        var candidates = GetCommonCandidates()
            .Concat(GetPathCandidates())
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return normalizedConfiguredPath;
    }

    /// <summary>
    /// 启动 mpv.net，并把界面选择的视频、音频、字幕轨道转换成 mpv 参数。
    /// </summary>
    /// <param name="executablePath">mpv.net 可执行文件路径。</param>
    /// <param name="streamUri">Emby 认证后的播放地址。</param>
    /// <param name="displayTitle">在 mpv.net 窗口和播放状态中显示的媒体标题。</param>
    /// <param name="startPositionTicks">需要从 Emby 续播的位置。</param>
    /// <param name="videoTrackId">可选的视频流索引；为空时由 mpv 自动选择。</param>
    /// <param name="audioTrackId">可选的音频流索引；为空时由 mpv 自动选择。</param>
    /// <param name="subtitleTrackId">可选的字幕流索引；为空时由 mpv 自动选择。</param>
    /// <param name="disableSubtitles">为 true 时强制关闭字幕，优先级高于字幕流索引。</param>
    /// <returns>包含进程和 IPC 管道名的启动信息。</returns>
    public MpvLaunch Play(
        string executablePath,
        Uri streamUri,
        string displayTitle,
        long startPositionTicks,
        int? videoTrackId,
        int? audioTrackId,
        int? subtitleTrackId,
        bool disableSubtitles,
        bool keepIdleForPlaylist)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Please set the mpv.net executable path.");
        }

        var pipeName = $"emby-mpv-player-{Guid.NewGuid():N}";

        var info = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };

        info.ArgumentList.Add(streamUri.ToString());
        info.ArgumentList.Add("--force-window=yes");
        info.ArgumentList.Add("--keep-open=no");
        info.ArgumentList.Add($"--input-ipc-server=\\\\.\\pipe\\{pipeName}");
        AddDisplayTitleArguments(info, displayTitle);
        if (keepIdleForPlaylist)
        {
            info.ArgumentList.Add("--idle=yes");
        }

        if (startPositionTicks > 0)
        {
            info.ArgumentList.Add($"--start={FormatMpvStartTime(TimeSpan.FromTicks(startPositionTicks))}");
        }

        AddTrackSelectionArguments(info, videoTrackId, audioTrackId, subtitleTrackId, disableSubtitles);

        try
        {
            var process = Process.Start(info)
                          ?? throw new InvalidOperationException("mpv.net did not return a process handle.");

            return new MpvLaunch(process, pipeName);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Failed to start mpv.net from '{executablePath}'.", ex);
        }
    }

    /// <summary>
    /// 给网络串流显式指定标题，避免 mpv.net 直接把带 token 的 URL 当作文件名显示成乱码。
    /// </summary>
    /// <remarks>
    /// <c>--title</c> 控制窗口标题，<c>--force-media-title</c> 控制媒体属性标题；
    /// 两个都传是为了兼容 mpv 和 mpv.net 对标题显示位置的差异。
    /// </remarks>
    private static void AddDisplayTitleArguments(ProcessStartInfo info, string displayTitle)
    {
        var normalizedTitle = NormalizeDisplayTitle(displayTitle);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return;
        }

        info.ArgumentList.Add($"--title={normalizedTitle}");
        info.ArgumentList.Add($"--force-media-title={normalizedTitle}");
    }

    /// <summary>
    /// 将 Emby 媒体流索引映射到 mpv 的轨道选择参数。
    /// </summary>
    /// <remarks>
    /// mpv 的 <c>--vid</c>、<c>--aid</c>、<c>--sid</c> 支持具体 ID、auto 或 no；
    /// 这里不传空值，让 mpv 保留自身默认选择，只处理用户在界面上的明确选择。
    /// </remarks>
    private static void AddTrackSelectionArguments(
        ProcessStartInfo info,
        int? videoTrackId,
        int? audioTrackId,
        int? subtitleTrackId,
        bool disableSubtitles)
    {
        if (videoTrackId is { } vid)
        {
            info.ArgumentList.Add($"--vid={vid}");
        }

        if (audioTrackId is { } aid)
        {
            info.ArgumentList.Add($"--aid={aid}");
        }

        if (disableSubtitles)
        {
            info.ArgumentList.Add("--sid=no");
        }
        else if (subtitleTrackId is { } sid)
        {
            info.ArgumentList.Add($"--sid={sid}");
        }
    }

    /// <summary>
    /// 清理标题中的换行和控制字符，避免它们进入 mpv.net 命令行后破坏窗口标题。
    /// </summary>
    private static string NormalizeDisplayTitle(string displayTitle)
    {
        if (string.IsNullOrWhiteSpace(displayTitle))
        {
            return "";
        }

        var normalized = new string(displayTitle
            .Where(character => !char.IsControl(character))
            .ToArray());

        return normalized.Trim();
    }

    private static string NormalizeExecutablePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return "";
        }

        var trimmed = configuredPath.Trim().Trim('"');
        return Environment.ExpandEnvironmentVariables(trimmed);
    }

    private static IEnumerable<string> GetCommonCandidates()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "mpv.net", "mpvnet.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "mpv.net", "mpv.net.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "mpv.net", "mpvnet.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "mpv.net", "mpv.net.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "mpv.net", "mpvnet.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "mpv.net", "mpv.net.exe");
    }

    private static IEnumerable<string> GetPathCandidates()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = Environment.ExpandEnvironmentVariables(segment.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            yield return Path.Combine(directory, "mpvnet.exe");
            yield return Path.Combine(directory, "mpv.net.exe");
        }
    }

    private static string FormatMpvStartTime(TimeSpan position)
    {
        return position.TotalHours >= 1
            ? position.ToString(@"h\:mm\:ss")
            : position.ToString(@"mm\:ss");
    }
}

public sealed record MpvLaunch(Process Process, string IpcPipeName);
