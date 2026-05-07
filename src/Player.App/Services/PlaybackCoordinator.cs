using Player.App.Models;

namespace Player.App.Services;

/// <summary>
/// 协调 mpv.net 启动、IPC 监听、Emby 播放上报和连播生命周期。
/// </summary>
public sealed class PlaybackCoordinator
{
    private readonly EmbyApiClient _embyClient;
    private readonly MpvNetLauncher _mpvLauncher;
    private readonly MpvIpcClient _mpvIpcClient;
    private readonly AppLogger _logger;
    private CancellationTokenSource? _playbackCancellation;
    private Guid _activePlaybackChainId = Guid.Empty;

    public PlaybackCoordinator(
        EmbyApiClient embyClient,
        MpvNetLauncher mpvLauncher,
        MpvIpcClient mpvIpcClient,
        AppLogger logger)
    {
        _embyClient = embyClient;
        _mpvLauncher = mpvLauncher;
        _mpvIpcClient = mpvIpcClient;
        _logger = logger;
    }

    /// <summary>
    /// 播放链路产生状态变化时触发，由窗口决定如何显示到状态栏。
    /// </summary>
    public event EventHandler<PlaybackStatusEventArgs>? StatusChanged;

    /// <summary>
    /// mpv.net 停止播放某个条目并完成停止上报后触发，用于界面刷新 Emby 最新播放状态。
    /// </summary>
    public event EventHandler<PlaybackStoppedEventArgs>? PlaybackStopped;

    /// <summary>
    /// 启动一次新的播放链，自动取消旧的 IPC 监听，确保进度只回传给当前条目。
    /// </summary>
    public async Task StartAsync(
        PlaybackRequest request,
        PlaybackSessionContext context,
        bool allowAutoPlayNext,
        Func<EmbyItem, Guid, CancellationToken, Task<PlaybackAutoPlayItem?>> resolveNextAsync,
        Action<PlaybackAutoPlayItem> applyAutoPlayItem,
        CancellationToken cancellationToken)
    {
        var (chainId, playbackToken) = ResetPlaybackMonitoring();

        var streamUri = _embyClient.BuildStreamUri(
            request.Item.Id,
            request.MediaSource,
            request.PlaySessionId,
            context.UserId,
            context.DeviceId,
            context.AccessToken,
            request.StartTicks,
            request.VideoStreamIndex,
            request.AudioStreamIndex,
            request.SubtitleStreamIndex,
            request.DisableSubtitles);

        await _embyClient.ReportPlaybackStartAsync(
            request.Item.Id,
            request.PlaySessionId,
            request.MediaSource.Id,
            request.StartTicks,
            request.AudioStreamIndex,
            request.DisableSubtitles ? -1 : request.SubtitleStreamIndex,
            cancellationToken);

        var launch = _mpvLauncher.Play(
            context.MpvNetPath,
            streamUri,
            request.Item.DisplayTitle,
            request.StartTicks,
            videoTrackId: request.UseSelectedMpvTracks ? request.VideoStreamIndex : null,
            audioTrackId: request.UseSelectedMpvTracks ? request.AudioStreamIndex : null,
            subtitleTrackId: request.UseSelectedMpvTracks ? request.SubtitleStreamIndex : null,
            request.DisableSubtitles,
            keepIdleForPlaylist: allowAutoPlayNext);

        await _logger.InfoAsync($"已启动 mpv.net：{request.Item.DisplayTitle}");
        PublishStatus($"已启动 mpv.net：{request.Item.DisplayTitle}");

        // 启动后监听必须在后台运行，否则主窗口会被 mpv 生命周期阻塞。
        _ = allowAutoPlayNext
            ? MonitorAutoPlaybackAsync(launch, request, context, chainId, resolveNextAsync, applyAutoPlayItem, playbackToken)
            : MonitorPlaybackAsync(launch, request, chainId, playbackToken);
    }

    /// <summary>
    /// 取消当前播放监听；不会强制关闭 mpv.net，只停止应用侧 IPC 和 Emby 上报链路。
    /// </summary>
    public void CancelMonitoring()
    {
        _activePlaybackChainId = Guid.NewGuid();
        _playbackCancellation?.Cancel();
        _playbackCancellation?.Dispose();
        _playbackCancellation = null;
    }

    /// <summary>
    /// 判断连播回调是否仍属于当前播放链，防止旧后台任务在新播放后继续生效。
    /// </summary>
    public bool IsActiveChain(Guid chainId)
    {
        return chainId == _activePlaybackChainId &&
               _playbackCancellation?.IsCancellationRequested != true;
    }

    /// <summary>
    /// 使用同一个 mpv.net 实例进行连播，避免 mpv.net 单实例转发导致新管道无法监听进度。
    /// </summary>
    private async Task MonitorAutoPlaybackAsync(
        MpvLaunch launch,
        PlaybackRequest request,
        PlaybackSessionContext context,
        Guid chainId,
        Func<EmbyItem, Guid, CancellationToken, Task<PlaybackAutoPlayItem?>> resolveNextAsync,
        Action<PlaybackAutoPlayItem> applyAutoPlayItem,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mpvIpcClient.MonitorAutoPlayAsync(
                launch,
                CreateIpcPlaybackItem(request, context, chainId, resolveNextAsync, applyAutoPlayItem, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _logger.InfoAsync("播放监听已取消。");
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("播放监听异常停止。", ex);
            PublishStatus($"播放监听已停止：{ex.Message}");
        }
    }

    /// <summary>
    /// 监听单集播放的 IPC 事件，负责周期性进度和最终停止上报。
    /// </summary>
    private async Task MonitorPlaybackAsync(
        MpvLaunch launch,
        PlaybackRequest request,
        Guid chainId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mpvIpcClient.MonitorAsync(
                launch,
                TimeSpan.FromTicks(request.StartTicks),
                position => _embyClient.ReportPlaybackProgressAsync(
                    request.Item.Id,
                    request.PlaySessionId,
                    request.MediaSource.Id,
                    NormalizePlaybackPositionTicks(position, request.StartTicks),
                    cancellationToken),
                async position =>
                {
                    var stoppedTicks = NormalizePlaybackPositionTicks(position, request.StartTicks);
                    await _embyClient.ReportPlaybackStoppedAsync(
                        request.Item.Id,
                        request.PlaySessionId,
                        request.MediaSource.Id,
                        stoppedTicks,
                        cancellationToken);
                    PublishPlaybackStopped(request.Item, stoppedTicks);
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _logger.InfoAsync("播放监听已取消。");
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("播放监听异常停止。", ex);
            PublishStatus($"播放监听已停止：{ex.Message}");
        }
    }

    /// <summary>
    /// 把播放请求转成 IPC 连播条目，进度上报和下一集解析都绑定到当前请求快照。
    /// </summary>
    private MpvIpcPlaybackItem CreateIpcPlaybackItem(
        PlaybackRequest request,
        PlaybackSessionContext context,
        Guid chainId,
        Func<EmbyItem, Guid, CancellationToken, Task<PlaybackAutoPlayItem?>> resolveNextAsync,
        Action<PlaybackAutoPlayItem> applyAutoPlayItem,
        CancellationToken cancellationToken)
    {
        var streamUri = _embyClient.BuildStreamUri(
            request.Item.Id,
            request.MediaSource,
            request.PlaySessionId,
            context.UserId,
            context.DeviceId,
            context.AccessToken,
            request.StartTicks,
            request.VideoStreamIndex,
            request.AudioStreamIndex,
            request.SubtitleStreamIndex,
            request.DisableSubtitles);

        return new MpvIpcPlaybackItem(
            streamUri,
            request.Item.DisplayTitle,
            TimeSpan.FromTicks(request.StartTicks),
            position => _embyClient.ReportPlaybackProgressAsync(
                request.Item.Id,
                request.PlaySessionId,
                request.MediaSource.Id,
                NormalizePlaybackPositionTicks(position, request.StartTicks),
                cancellationToken),
            async position =>
            {
                var stoppedTicks = NormalizePlaybackPositionTicks(position, request.StartTicks);
                await _embyClient.ReportPlaybackStoppedAsync(
                    request.Item.Id,
                    request.PlaySessionId,
                    request.MediaSource.Id,
                    stoppedTicks,
                    cancellationToken);
                PublishPlaybackStopped(request.Item, stoppedTicks);
            },
            async () =>
            {
                var nextPlayback = await resolveNextAsync(request.Item, chainId, cancellationToken);
                if (nextPlayback is null)
                {
                    return null;
                }

                applyAutoPlayItem(nextPlayback);
                PublishStatus($"连播下一集：{nextPlayback.Request.Item.DisplayTitle}");
                return CreateIpcPlaybackItem(
                    nextPlayback.Request,
                    context,
                    chainId,
                    resolveNextAsync,
                    applyAutoPlayItem,
                    cancellationToken);
            });
    }

    /// <summary>
    /// 新播放开始前重置播放链 ID 和取消令牌，让旧后台监听自然退出。
    /// </summary>
    private (Guid ChainId, CancellationToken Token) ResetPlaybackMonitoring()
    {
        CancelMonitoring();
        _playbackCancellation = new CancellationTokenSource();
        _activePlaybackChainId = Guid.NewGuid();
        return (_activePlaybackChainId, _playbackCancellation.Token);
    }

    private void PublishStatus(string message)
    {
        StatusChanged?.Invoke(this, new PlaybackStatusEventArgs(message));
    }

    private void PublishPlaybackStopped(EmbyItem item, long stoppedTicks)
    {
        PlaybackStopped?.Invoke(this, new PlaybackStoppedEventArgs(item.Id, item.DisplayTitle, stoppedTicks));
    }

    private static long NormalizePlaybackPositionTicks(TimeSpan mpvPosition, long requestedStartTicks)
    {
        return Math.Max(mpvPosition.Ticks, requestedStartTicks);
    }
}

/// <summary>
/// 启动 mpv.net 时不会变化的会话参数。
/// </summary>
public sealed record PlaybackSessionContext(
    string UserId,
    string DeviceId,
    string AccessToken,
    string MpvNetPath);

/// <summary>
/// 一次 mpv.net 播放所需的稳定快照；连播下一集时不会依赖仍在变化的界面下拉框状态。
/// </summary>
public sealed record PlaybackRequest(
    EmbyItem Item,
    MediaSource MediaSource,
    string PlaySessionId,
    long StartTicks,
    int? VideoStreamIndex,
    int? AudioStreamIndex,
    int? SubtitleStreamIndex,
    bool DisableSubtitles,
    bool UseSelectedMpvTracks);

/// <summary>
/// 连播下一集准备完成后的请求和原始播放信息，用于同时更新 mpv 和主窗口详情区。
/// </summary>
public sealed record PlaybackAutoPlayItem(
    PlaybackRequest Request,
    PlaybackInfoResponse PlaybackInfo);

/// <summary>
/// 播放协调器向界面发送的简短状态消息。
/// </summary>
public sealed class PlaybackStatusEventArgs : EventArgs
{
    public PlaybackStatusEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}

/// <summary>
/// 表示某个 Emby 条目的 mpv.net 播放已经停止，并且客户端已尝试把最终进度上报给 Emby。
/// </summary>
public sealed class PlaybackStoppedEventArgs : EventArgs
{
    public PlaybackStoppedEventArgs(string itemId, string displayTitle, long positionTicks)
    {
        ItemId = itemId;
        DisplayTitle = displayTitle;
        PositionTicks = positionTicks;
    }

    public string ItemId { get; }

    public string DisplayTitle { get; }

    public long PositionTicks { get; }
}
