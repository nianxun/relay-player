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
    /// 清空季/集选择器和对应数据源，避免切换条目后残留旧季旧集。
    /// </summary>
    private void ClearEpisodeSelectionUi()
    {
        _isInitializing = true;
        try
        {
            _seasons.Clear();
            _episodes.Clear();
            SeasonComboBox.SelectedItem = null;
            EpisodeComboBox.SelectedItem = null;
            SeasonComboBox.Visibility = Visibility.Collapsed;
            EpisodeComboBox.Visibility = Visibility.Collapsed;
            EpisodePickerPanel.Visibility = Visibility.Collapsed;
            _activeSeriesId = null;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// 根据当前选中条目的播放状态，刷新“已播放”按钮图标和视觉反馈。
    /// </summary>
    private void RefreshPlayedUiState(EmbyItem item)
    {
        var isPlayed = item.UserData?.Played == true;
        TogglePlayedButton.IsEnabled = !_isBusy && !EmbyItemKind.IsContainer(item);
        TogglePlayedButton.Tag = isPlayed ? "True" : "False";
        TogglePlayedButton.ToolTip = isPlayed ? "取消已播放" : "标记已播放";
    }

    /// <summary>
    /// 把用户主动修改的播放状态同步到所有当前已加载的同 ID 条目。
    /// </summary>
    /// <remarks>
    /// Emby 的列表、季/集下拉和当前详情可能分别持有不同的 <see cref="EmbyItem"/> 实例；
    /// 只改一个实例会造成“卡片未刷新、下拉徽记未刷新”的假状态。
    /// </remarks>
    private void ApplyPlayedState(string itemId, bool isPlayed)
    {
        foreach (var matchingItem in EnumerateLoadedItems(itemId))
        {
            matchingItem.UserData ??= new EmbyUserData();
            matchingItem.UserData.Played = isPlayed;
            if (isPlayed)
            {
                matchingItem.UserData.PlaybackPositionTicks = 0;
            }
        }
    }

    /// <summary>
    /// 把播放结束后从 Emby 重新读取到的状态同步到当前所有已加载的同 ID 条目。
    /// </summary>
    /// <remarks>
    /// 停止上报由 Emby 决定最终续播位置和是否已播放，客户端不能只用 mpv 的本地秒数推断；
    /// 这里用服务器返回的快照覆盖本地状态，避免详情页和继续观看列表显示旧进度。
    /// </remarks>
    private void ApplyPlaybackStateSnapshot(EmbyItem snapshot)
    {
        foreach (var matchingItem in EnumerateLoadedItems(snapshot.Id))
        {
            matchingItem.UserData = snapshot.UserData;
            matchingItem.DatePlayed = snapshot.DatePlayed;
            matchingItem.RunTimeTicks = snapshot.RunTimeTicks;
            matchingItem.ThumbnailUri = snapshot.ThumbnailUri ?? matchingItem.ThumbnailUri;
        }
    }

    /// <summary>
    /// 枚举当前界面可能展示的同一 Emby 条目，按引用去重后供状态同步使用。
    /// </summary>
    private IEnumerable<EmbyItem> EnumerateLoadedItems(string itemId)
    {
        var seen = new HashSet<EmbyItem>(ReferenceEqualityComparer.Instance);
        foreach (var item in _items.Concat(_episodes).Concat(_seasons))
        {
            if (string.Equals(item.Id, itemId, StringComparison.Ordinal) && seen.Add(item))
            {
                yield return item;
            }
        }

        if (_selectedItem is not null &&
            string.Equals(_selectedItem.Id, itemId, StringComparison.Ordinal) &&
            seen.Add(_selectedItem))
        {
            yield return _selectedItem;
        }
    }
}
