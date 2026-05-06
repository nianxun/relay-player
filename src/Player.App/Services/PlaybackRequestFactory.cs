using Player.App.Models;

namespace Player.App.Services;

/// <summary>
/// 根据当前媒体、播放信息和用户选择生成稳定的播放请求快照。
/// </summary>
public static class PlaybackRequestFactory
{
    /// <summary>
    /// 构造用户主动点击播放时的请求；会尊重续播位置、媒体源、视频轨、音频轨和字幕选择。
    /// </summary>
    public static PlaybackRequest CreateManualRequest(
        EmbyItem item,
        MediaSource mediaSource,
        PlaybackInfoResponse? playbackInfo,
        bool resumeFromUserData,
        MediaTrackOption? videoTrack,
        MediaTrackOption? audioTrack,
        MediaTrackOption? subtitleTrack)
    {
        var playSessionId = playbackInfo?.PlaySessionId ?? Guid.NewGuid().ToString("N");
        var startTicks = resumeFromUserData
            ? item.UserData?.PlaybackPositionTicks ?? 0
            : 0;

        return new PlaybackRequest(
            item,
            mediaSource,
            playSessionId,
            startTicks,
            GetExplicitTrackIndex(videoTrack),
            GetExplicitTrackIndex(audioTrack),
            GetExplicitTrackIndex(subtitleTrack),
            subtitleTrack?.IsNone == true,
            UseSelectedMpvTracks: true);
    }

    /// <summary>
    /// 只有用户选择了具体轨道时才返回索引；自动选择和关闭项都不应作为普通索引传给 Emby。
    /// </summary>
    internal static int? GetExplicitTrackIndex(MediaTrackOption? option)
    {
        return option is { IsAuto: false, IsNone: false } ? option.Index : null;
    }
}
