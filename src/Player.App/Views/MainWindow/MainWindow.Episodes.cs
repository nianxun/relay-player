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
    /// 打开剧集后，按 Emby 返回的 season/episode 层级填充右侧选择器。
    /// </summary>
    private async Task LoadSeriesSelectionAsync(EmbyItem series, CancellationToken cancellationToken)
    {
        CancelEpisodeNavigation();
        _activeSeriesId = series.Id;
        _episodeNavigationCancellation = new CancellationLease();
        var linkedToken = _episodeNavigationCancellation.StartLinked(cancellationToken);

        try
        {
            SetStatus("正在加载季和集...");
            var seasons = await _embyClient.GetSeasonsAsync(series.Id, _settings.UserId, linkedToken);

            if (linkedToken.IsCancellationRequested || !ReferenceEquals(_selectedItem, series))
            {
                return;
            }

            _seasons.Clear();
            foreach (var season in seasons)
            {
                _seasons.Add(season);
            }

            SeasonComboBox.Visibility = _seasons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EpisodePickerPanel.Visibility = Visibility.Visible;

            if (_seasons.Count == 0)
            {
                SetStatus("没有找到可用的季。");
                return;
            }

            var firstSeason = _episodeSelectionCoordinator.SelectInitialSeason(_seasons) ?? _seasons[0];
            _isInitializing = true;
            try
            {
                SeasonComboBox.SelectedItem = firstSeason;
            }
            finally
            {
                _isInitializing = false;
            }

            await LoadEpisodesForSeasonAsync(
                firstSeason,
                selectEpisodeAfterLoad: true,
                cancellationToken: linkedToken,
                cancelExistingRequest: false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            CancelEpisodeNavigation();
        }
    }

    /// <summary>
    /// 选中季后刷新该季的集列表，避免用户只能停留在当前条目。
    /// </summary>
    private async Task LoadEpisodesForSeasonAsync(EmbyItem season)
    {
        await LoadEpisodesForSeasonAsync(
            season,
            selectEpisodeAfterLoad: true,
            cancellationToken: CancellationToken.None,
            cancelExistingRequest: true);
    }

    /// <summary>
    /// 选中季后刷新该季的集列表，调用方可决定是否立即切换到某一集。
    /// </summary>
    private async Task LoadEpisodesForSeasonAsync(
        EmbyItem season,
        bool selectEpisodeAfterLoad,
        CancellationToken cancellationToken,
        bool cancelExistingRequest)
    {
        var current = _selectedItem;
        var seriesId = _episodeSelectionCoordinator.ResolveSeriesIdForEpisodeLoad(_activeSeriesId, current);
        if (current is null || string.IsNullOrWhiteSpace(seriesId))
        {
            return;
        }

        var seasonId = _episodeSelectionCoordinator.ResolveSeasonRequestId(season);

        if (cancelExistingRequest)
        {
            CancelEpisodeNavigation();
        }

        var navigationCancellation = new CancellationLease();
        if (cancelExistingRequest)
        {
            _episodeNavigationCancellation = navigationCancellation;
        }
        var linkedToken = navigationCancellation.StartLinked(cancellationToken);

        try
        {
            SetStatus("正在加载剧集...");
            var episodes = await _embyClient.GetEpisodesAsync(seriesId, seasonId, _settings.UserId, linkedToken);

            if (navigationCancellation.IsCancellationRequested ||
                _selectedItem is null ||
                !_episodeSelectionCoordinator.IsSameSeriesContext(_selectedItem, seriesId))
            {
                return;
            }

            _episodes.Clear();
            AttachArtworkUris(episodes);
            foreach (var episode in episodes)
            {
                _episodes.Add(episode);
            }

            EpisodeComboBox.Visibility = _episodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_episodes.Count == 0)
            {
                SetStatus("当前季没有可播放的集。");
                return;
            }

            var selectedEpisode = _episodeSelectionCoordinator.SelectEpisodeForSeason(current, _episodes) ?? _episodes[0];

            _isInitializing = true;
            try
            {
                EpisodeComboBox.SelectedItem = selectedEpisode;
            }
            finally
            {
                _isInitializing = false;
            }

            if (selectEpisodeAfterLoad)
            {
                await SelectEpisodeAsync(selectedEpisode);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (cancelExistingRequest && ReferenceEquals(_episodeNavigationCancellation, navigationCancellation))
            {
                CancelEpisodeNavigation();
            }
            else
            {
                navigationCancellation.Dispose();
            }
        }
    }

    /// <summary>
    /// 当用户在集选择器里切换条目时，直接把右侧详情和媒体源切过去。
    /// </summary>
    private async Task SelectEpisodeAsync(EmbyItem episode)
    {
        if (!EmbyItemKind.IsEpisode(episode))
        {
            return;
        }

        _isApplyingEpisodeSelection = true;
        try
        {
            await SelectItemAsync(episode);
            RefreshPlayedUiState(episode);
        }
        finally
        {
            _isApplyingEpisodeSelection = false;
        }
    }
}


