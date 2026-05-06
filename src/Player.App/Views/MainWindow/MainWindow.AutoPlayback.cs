using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Player.App.Models;
using Player.App.Services;

namespace Player.App;

public partial class MainWindow
{
    /// <summary>
    /// 为同一 mpv 进程中的下一集准备 URL、标题和 Emby 上报上下文。
    /// </summary>
    private async Task<PlaybackAutoPlayItem?> PrepareNextAutoPlaybackItemAsync(
        EmbyItem currentEpisode,
        Guid chainId,
        CancellationToken cancellationToken)
    {
        if (!EmbyItemKind.IsEpisode(currentEpisode) ||
            string.IsNullOrWhiteSpace(_settings.UserId) ||
            !_playbackCoordinator.IsActiveChain(chainId) ||
            cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            var nextEpisode = await ResolveNextEpisodeAsync(currentEpisode, cancellationToken);
            if (nextEpisode is null || !_playbackCoordinator.IsActiveChain(chainId))
            {
                SetStatus("连播结束：没有下一集。");
                return null;
            }

            var playbackInfo = await _embyClient.GetPlaybackInfoAsync(
                nextEpisode.Id,
                _settings.UserId,
                _settings.DeviceId,
                cancellationToken);

            var mediaSource = playbackInfo.MediaSources.FirstOrDefault();
            if (mediaSource is null)
            {
                SetStatus($"连播停止：{nextEpisode.DisplayTitle} 没有可播放媒体源。");
                return null;
            }

            var disableSubtitles = await Dispatcher.InvokeAsync(() =>
                SubtitleTrackComboBox.SelectedItem is MediaTrackOption { IsNone: true });

            var request = new PlaybackRequest(
                nextEpisode,
                mediaSource,
                playbackInfo.PlaySessionId ?? Guid.NewGuid().ToString("N"),
                StartTicks: 0,
                VideoStreamIndex: null,
                AudioStreamIndex: null,
                SubtitleStreamIndex: null,
                DisableSubtitles: disableSubtitles,
                UseSelectedMpvTracks: false);

            await _embyClient.ReportPlaybackStartAsync(
                request.Item.Id,
                request.PlaySessionId,
                request.MediaSource.Id,
                request.StartTicks,
                request.AudioStreamIndex,
                request.DisableSubtitles ? -1 : request.SubtitleStreamIndex,
                cancellationToken);

            return new PlaybackAutoPlayItem(request, playbackInfo);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _ = _logger.ErrorAsync("连播启动下一集失败。", ex);
            SetStatus("连播停止：启动下一集失败。");
            return null;
        }
    }

    /// <summary>
    /// 连播切到下一集时同步下拉框选中项，让界面标题、集选择器和实际播放条目保持一致。
    /// </summary>
    private void SelectEpisodeInPicker(EmbyItem episode)
    {
        var matchingEpisode = _episodes.FirstOrDefault(item => string.Equals(item.Id, episode.Id, StringComparison.Ordinal));
        if (matchingEpisode is null)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            EpisodeComboBox.SelectedItem = matchingEpisode;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// 连播切到下一集后同步详情区、媒体源和选集控件；真正播放切换由 PlaybackCoordinator 负责。
    /// </summary>
    private void ApplyAutoPlaybackItem(PlaybackAutoPlayItem playback)
    {
        Dispatcher.Invoke(() =>
        {
            var nextEpisode = playback.Request.Item;
            var playbackInfo = playback.PlaybackInfo;
            _selectedItem = nextEpisode;
            _selectedPlaybackInfo = playbackInfo;
            SelectedTitleTextBlock.Text = nextEpisode.DisplayTitle;
            SelectedMetaTextBlock.Text = nextEpisode.MetaLine;
            SelectedSourceTextBlock.Text = "";
            SelectedOverviewTextBlock.Text = string.IsNullOrWhiteSpace(nextEpisode.Overview)
                ? "暂无简介。"
                : nextEpisode.Overview.Trim();
            MediaSourceComboBox.ItemsSource = playbackInfo.MediaSources;
            ApplyInitialMediaSourceSelection(playbackInfo.MediaSources);
            SelectEpisodeInPicker(nextEpisode);
            BindPosterImage(nextEpisode);
            RefreshPlayedUiState(nextEpisode);
        });
    }

    /// <summary>
    /// 查找整部剧中的下一集；先按季号/集号严格比较，再用列表位置兜底。
    /// </summary>
    private async Task<EmbyItem?> ResolveNextEpisodeAsync(EmbyItem currentEpisode, CancellationToken cancellationToken)
    {
        var episodes = await LoadSeriesEpisodesAsync(currentEpisode, cancellationToken);
        return _episodeSelectionCoordinator.SelectNextEpisode(currentEpisode, episodes);
    }

    /// <summary>
    /// 从当前集反查所属剧集，加载整部剧的集列表，让本季最后一集可以继续到下一季。
    /// </summary>
    private async Task<IReadOnlyList<EmbyItem>> LoadSeriesEpisodesAsync(EmbyItem episode, CancellationToken cancellationToken)
    {
        var seriesId = _episodeSelectionCoordinator.ResolveSeriesId(episode);
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            return [];
        }

        var episodes = await _embyClient.GetEpisodesAsync(seriesId, seasonId: null, _settings.UserId, cancellationToken);
        AttachArtworkUris(episodes);
        return episodes;
    }
}
