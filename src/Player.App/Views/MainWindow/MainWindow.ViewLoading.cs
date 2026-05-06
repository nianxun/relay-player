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
    /// 加载列表视图，并同步选择状态、标题和视图状态记录。
    /// </summary>
    private async Task LoadViewAsync(BrowseState state, bool pushCurrent)
    {
        if (!HasSavedSession())
        {
            SetStatus("请先登录 Emby。");
            return;
        }

        if (pushCurrent)
        {
            _navigationStack.Push(_currentState);
        }

        _currentState = state;
        // 页面切换的显隐状态必须在网络请求前完成；否则返回继续观看时，
        // 旧详情面板会和入口列表短暂同时显示，形成肉眼可见的双页面闪烁。
        ApplyViewShellState(state.Kind);
        ConfigureClientFromSettings();

        CancelSelectionPreview();
        _loadCancellation = new CancellationLease();
        var loadToken = _loadCancellation.StartNew();

        await RunGuardedAsync(
            "load media",
            async cancellationToken =>
            {
                SetStatus(UserFacingMessages.GetLoadingMessage(state.Kind));
                ListTitleTextBlock.Text = state.Title;
                ItemsHeaderTextBlock.Text = state.Title;
                UpdateBackButtonVisibility();
                UpdateViewButtons(state.Kind);

                var items = await LoadItemsForStateAsync(state, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                AttachArtworkUris(items);

                // 新列表准备好后再替换集合；这里不再包一层 DeferRefresh，
                // 否则在同一批更新里同步改选中项时，WPF 会抛出 Current 位置相关异常。
                _items.Clear();
                foreach (var item in items)
                {
                    _items.Add(item);
                }

                if (_items.Count == 0)
                {
                    ClearSelectedItem();
                    SetStatus(UserFacingMessages.BuildLoadedMessage(state.Kind, items.Count));
                    return;
                }

                if (state.Kind == BrowseViewKind.Resume)
                {
                    // “继续观看”是入口列表，不再自动选中第一集；自动加载详情会造成上方空面板和额外网络请求。
                    _suppressItemSelectionChanged = true;
                    try
                    {
                        ItemsListView.SelectedItem = null;
                    }
                    finally
                    {
                        _suppressItemSelectionChanged = false;
                    }

                    ClearSelectedItem();
                    SetStatus(UserFacingMessages.BuildLoadedMessage(state.Kind, items.Count));
                    return;
                }

                // 目录和搜索页仍需要一个明确的详情上下文；从继续观看跳转过来时优先保持用户点击的条目。
                var initialItem = !string.IsNullOrWhiteSpace(state.SelectedItemId)
                    ? _items.FirstOrDefault(item => string.Equals(item.Id, state.SelectedItemId, StringComparison.Ordinal))
                    : null;
                initialItem ??= _items.FirstOrDefault(item => !EmbyItemKind.IsContainer(item)) ?? _items[0];

                _suppressItemSelectionChanged = true;
                try
                {
                    ItemsListView.SelectedItem = initialItem;
                }
                finally
                {
                    _suppressItemSelectionChanged = false;
                }

                ItemsListView.ScrollIntoView(initialItem);
                await SelectItemAsync(initialItem);
                SetStatus(UserFacingMessages.BuildLoadedMessage(state.Kind, items.Count));
            },
            loadToken,
            showBusyOverlay: false);
    }

    /// <summary>
    /// 根据当前导航状态分发到对应的 Emby 查询。
    /// </summary>
    /// <remarks>
    /// “继续观看”直接映射到 Emby 的可续播过滤器；目录和搜索仍保留，便于从条目继续深入。
    /// </remarks>
    private async Task<IReadOnlyList<EmbyItem>> LoadItemsForStateAsync(BrowseState state, CancellationToken cancellationToken)
    {
        return state.Kind switch
        {
            BrowseViewKind.Library when string.IsNullOrWhiteSpace(state.ParentId) =>
                await _embyClient.GetLatestItemsAsync(_settings.UserId, HomeListLimit, cancellationToken),
            BrowseViewKind.Library =>
                await _embyClient.GetItemsAsync(_settings.UserId, state.ParentId, string.Empty, cancellationToken),
            BrowseViewKind.Search =>
                await _embyClient.GetItemsAsync(_settings.UserId, null, state.SearchTerm ?? string.Empty, cancellationToken),
            BrowseViewKind.Resume =>
                await _embyClient.GetContinueWatchingAsync(_settings.UserId, HomeListLimit, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported view state: {state.Kind}")
        };
    }

    /// <summary>
    /// 为列表卡片预先补齐缩略图地址，避免在 XAML 中拼接带 token 的 Emby 图片 URL。
    /// </summary>
    /// <remarks>
    /// Emby 明确要求客户端只请求条目声明存在的图片；继续观看里的剧集经常没有自己的 Backdrop，
    /// 所以这里按 Thumb、Primary、继承 Backdrop 的顺序找真实可用图片，避免卡片长期显示空占位。
    /// </remarks>
    private void AttachArtworkUris(IEnumerable<EmbyItem> items)
    {
        _artworkResolver.AttachThumbnailUris(items, _settings.AccessToken);
    }
}
