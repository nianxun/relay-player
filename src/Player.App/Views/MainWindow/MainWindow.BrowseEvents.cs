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
    /// 选中列表条目时，根据当前页面状态决定是进入详情还是直接切换到该条目的播放上下文。
    /// </summary>
    private async void ItemsListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressItemSelectionChanged)
        {
            return;
        }

        if (ItemsListView.SelectedItem is not EmbyItem item)
        {
            return;
        }

        if (_currentState.Kind == BrowseViewKind.Resume)
        {
            await OpenResumeItemDetailAsync(item);
            return;
        }

        await SelectItemAsync(item);
    }

    /// <summary>
    /// 切换季时刷新对应剧集列表；此处只负责把 UI 选择传给加载流程，不做额外状态推断。
    /// </summary>
    private async void SeasonComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isSwitchingServerProfile)
        {
            return;
        }

        if (SeasonComboBox.SelectedItem is not EmbyItem season)
        {
            return;
        }

        await LoadEpisodesForSeasonAsync(season);
    }

    /// <summary>
    /// 切换集时直接把当前详情切到目标剧集，避免用户在同一季里反复手动确认。
    /// </summary>
    private async void EpisodeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isSwitchingServerProfile)
        {
            return;
        }

        if (EpisodeComboBox.SelectedItem is not EmbyItem episode)
        {
            return;
        }

        await SelectEpisodeAsync(episode);
    }

    /// <summary>
    /// 媒体源切换后刷新本版本的资源大小和可选音视频/字幕轨道。
    /// </summary>
    private void MediaSourceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isApplyingMediaSourceSelection)
        {
            return;
        }

        UpdateMediaSourceDetails(MediaSourceComboBox.SelectedItem as MediaSource);
    }

    /// <summary>
    /// 双击内容区域时，目录进入下一层，媒体条目则直接播放。
    /// </summary>
    private async void ItemsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsListView.SelectedItem is not EmbyItem item)
        {
            return;
        }

        if (EmbyItemKind.IsContainer(item))
        {
            await OpenContainerAsync(item);
            return;
        }

        await PlaySelectedAsync();
    }

    /// <summary>
    /// 播放按钮只负责触发当前条目的播放流程，不做额外选择逻辑。
    /// </summary>
    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        await PlaySelectedAsync();
    }

    /// <summary>
    /// 主动切换已播放状态时，统一走 Emby 的标记接口并同步到当前已加载条目。
    /// </summary>
    private async void TogglePlayedButton_Click(object sender, RoutedEventArgs e)
    {
        var item = _selectedItem;
        if (item is null || string.IsNullOrWhiteSpace(item.Id))
        {
            SetStatus("请先选择一个条目。");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.UserId))
        {
            SetStatus("请先登录。");
            return;
        }

        await RunGuardedAsync(
            "update played state",
            async cancellationToken =>
            {
                var shouldMarkPlayed = item.UserData?.Played != true;
                if (shouldMarkPlayed)
                {
                    await _embyClient.MarkPlayedAsync(item.Id, _settings.UserId, cancellationToken);
                }
                else
                {
                    await _embyClient.MarkUnplayedAsync(item.Id, _settings.UserId, cancellationToken);
                }

                ApplyPlayedState(item.Id, shouldMarkPlayed);
                RefreshPlayedUiState(item);
                SetStatus(shouldMarkPlayed ? "已标记为已播放。" : "已取消已播放标记。");
            },
            invalidateSessionOnAuthFailure: false);
    }
}
