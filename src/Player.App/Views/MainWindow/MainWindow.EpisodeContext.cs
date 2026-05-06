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
    /// 在不阻塞当前详情和播放信息的前提下，后台补齐当前集所属的季/集选择器。
    /// </summary>
    private async Task LoadEpisodeSelectionContextInBackgroundAsync(EmbyItem episode)
    {
        try
        {
            await LoadEpisodeSelectionContextAsync(episode, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_selectedItem, episode))
            {
                SetStatus(UserFacingMessages.BuildFriendlyErrorMessage("load episode context", ex));
            }
        }
    }

    /// <summary>
    /// 选中继续观看里的某一集时，反向加载它所属剧集的季/集上下文。
    /// </summary>
    private async Task LoadEpisodeSelectionContextAsync(EmbyItem episode, CancellationToken cancellationToken)
    {
        var seriesId = _episodeSelectionCoordinator.ResolveSeriesId(episode);
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            return;
        }

        CancelEpisodeNavigation();
        _activeSeriesId = seriesId;
        var navigationCancellation = new CancellationLease();
        _episodeNavigationCancellation = navigationCancellation;
        var linkedToken = navigationCancellation.StartLinked(cancellationToken);

        try
        {
            SetStatus("正在加载季和集...");
            var seasons = await _embyClient.GetSeasonsAsync(seriesId, _settings.UserId, linkedToken);

            if (linkedToken.IsCancellationRequested || !ReferenceEquals(_selectedItem, episode))
            {
                return;
            }

            _seasons.Clear();
            foreach (var season in seasons)
            {
                _seasons.Add(season);
            }

            if (_seasons.Count == 0)
            {
                return;
            }

            EpisodePickerPanel.Visibility = Visibility.Visible;
            SeasonComboBox.Visibility = Visibility.Visible;
            EpisodeComboBox.Visibility = Visibility.Visible;

            var selectedSeason = _episodeSelectionCoordinator.SelectSeasonForEpisode(episode, _seasons) ?? _seasons[0];

            _isInitializing = true;
            try
            {
                SeasonComboBox.SelectedItem = selectedSeason;
            }
            finally
            {
                _isInitializing = false;
            }

            await LoadEpisodesForSeasonAsync(
                selectedSeason,
                selectEpisodeAfterLoad: false,
                cancellationToken: linkedToken,
                cancelExistingRequest: false);

            var matchingEpisode = _episodeSelectionCoordinator.FindEpisodeById(_episodes, episode);
            if (matchingEpisode is not null)
            {
                _isInitializing = true;
                try
                {
                    EpisodeComboBox.SelectedItem = matchingEpisode;
                }
                finally
                {
                    _isInitializing = false;
                }
                RefreshPlayedUiState(matchingEpisode);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_episodeNavigationCancellation, navigationCancellation))
            {
                CancelEpisodeNavigation();
            }
            else
            {
                navigationCancellation.Dispose();
            }
        }
    }
}
