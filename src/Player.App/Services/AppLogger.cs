using System.IO;
using System.Text;

namespace Player.App.Services;

/// <summary>
/// 轻量文件日志服务，用于记录播放上报、IPC 和外部进程这类状态栏难以保留的诊断信息。
/// </summary>
public sealed class AppLogger
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int MaxArchiveCount = 3;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RelayPlayer",
        "logs",
        "relay-player.log");

    /// <summary>
    /// 写入普通诊断信息；日志失败不向外抛出，避免影响播放器主流程。
    /// </summary>
    public Task InfoAsync(string message)
    {
        return WriteAsync("INFO", message, null);
    }

    /// <summary>
    /// 写入异常诊断信息，保留异常类型、消息和堆栈，方便复现后回查。
    /// </summary>
    public Task ErrorAsync(string message, Exception exception)
    {
        return WriteAsync("ERROR", message, exception);
    }

    /// <summary>
    /// 串行追加日志，避免多个后台播放回调同时写文件导致内容交错。
    /// </summary>
    private async Task WriteAsync(string level, string message, Exception? exception)
    {
        try
        {
            await _writeLock.WaitAsync();
            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfNeeded();

                var builder = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                    .Append(" [")
                    .Append(level)
                    .Append("] ")
                    .AppendLine(message);

                if (exception is not null)
                {
                    builder.AppendLine(exception.ToString());
                }

                await File.AppendAllTextAsync(LogPath, builder.ToString(), Encoding.UTF8);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch
        {
            // 日志系统只提供诊断能力，不能因为磁盘或权限问题打断播放。
        }
    }

    /// <summary>
    /// 日志超过上限时保留最近 3 份归档，避免长期播放后日志无限增大。
    /// </summary>
    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
        {
            return;
        }

        for (var index = MaxArchiveCount; index >= 1; index--)
        {
            var source = index == 1 ? LogPath : BuildArchivePath(index - 1);
            var target = BuildArchivePath(index);
            if (!File.Exists(source))
            {
                continue;
            }

            File.Move(source, target, overwrite: true);
        }
    }

    private string BuildArchivePath(int index)
    {
        return $"{LogPath}.{index}";
    }
}
