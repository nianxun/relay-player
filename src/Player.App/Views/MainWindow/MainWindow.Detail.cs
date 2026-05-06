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
    /// 更新主详情区；如果是可播放条目，则加载 mpv.net 需要的媒体源列表。
    /// </summary>
    private async Task SelectItemAsync(EmbyItem item)
    {
        CancelSelectionPreview();
        var selectionCancellation = new CancellationLease();
        _selectionCancellation = selectionCancellation;
        var cancellationToken = selectionCancellation.StartNew();

        _selectedItem = item;
        _selectedPlaybackInfo = null;
        SelectedTitleTextBlock.Text = item.DisplayTitle;
        SelectedMetaTextBlock.Text = item.MetaLine;
        SelectedSourceTextBlock.Text = "";
        SelectedOverviewTextBlock.Text = string.IsNullOrWhiteSpace(item.Overview)
            ? "暂无简介。"
            : item.Overview.Trim();
        MediaSourceComboBox.ItemsSource = null;
        ClearTrackSelectors();
        RefreshPlayedUiState(item);
        if (!_isApplyingEpisodeSelection)
        {
            ClearEpisodeSelectionUi();
        }

        BindPosterImage(item);

        if (HasSavedSession())
        {
            // 海报只是视觉增强，后台加载完成后再替换；图片字段不足时会后台回源补齐，不阻塞详情切换。
            _ = LoadPosterImageAsync(item, selectionCancellation.Token);
        }

        if (EmbyItemKind.IsSeries(item))
        {
            await LoadSeriesSelectionAsync(item, selectionCancellation.Token);
            return;
        }

        if (EmbyItemKind.IsContainer(item))
        {
            SetStatus("双击可打开这个文件夹或剧集。");
            return;
        }

        if (EmbyItemKind.IsEpisode(item) && !_isApplyingEpisodeSelection)
        {
            // 首次进入剧集详情时，季/集下拉只是辅助导航；放到后台补齐，
            // 避免它的两次网络请求挡住当前集的预览图和播放信息。
            _ = LoadEpisodeSelectionContextInBackgroundAsync(item);
        }

        try
        {
            // 预览详情只负责更新右侧卡片和播放源，不要把整条列表锁住，
            // 这样用户在季/集之间快速切换时不会被上一条目的网络请求卡住。
            SetStatus("正在加载播放信息...");
            var playbackInfo = await _embyClient.GetPlaybackInfoAsync(
                item.Id,
                _settings.UserId,
                _settings.DeviceId,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_selectedItem, item))
            {
                return;
            }

            _selectedPlaybackInfo = playbackInfo;
            MediaSourceComboBox.ItemsSource = _selectedPlaybackInfo.MediaSources;
            ApplyInitialMediaSourceSelection(_selectedPlaybackInfo.MediaSources);
            // 媒体源为空通常意味着服务器策略、权限或媒体元数据不支持直接播放。
            SetStatus(_selectedPlaybackInfo.MediaSources.Count == 0
                ? "没有找到可播放的媒体源。"
                : "可以播放。");
            RefreshPlayedUiState(item);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_selectedItem, item))
            {
                SetStatus("已取消。");
            }
        }
        catch (EmbyApiException ex)
        {
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_selectedItem, item))
            {
                return;
            }

            if (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // 播放信息同样依赖有效会话，失效后要清掉 token，避免后续继续点选时重复报错。
                InvalidateCurrentSession();
                await SaveSettingsAsync();
            }

            var message = UserFacingMessages.BuildFriendlyErrorMessage("load playback info", ex);
            SetStatus(message);
            MessageBox.Show(this, message, AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (HttpRequestException ex)
        {
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_selectedItem, item))
            {
                return;
            }

            var message = UserFacingMessages.BuildFriendlyErrorMessage("load playback info", ex);
            SetStatus(message);
            MessageBox.Show(this, message, AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(_selectedItem, item))
            {
                return;
            }

            var message = UserFacingMessages.BuildFriendlyErrorMessage("load playback info", ex);
            SetStatus(message);
            MessageBox.Show(this, message, AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, selectionCancellation))
            {
                CancelSelectionPreview();
            }
        }
    }
}


