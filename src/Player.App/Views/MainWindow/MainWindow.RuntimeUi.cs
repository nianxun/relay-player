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
    /// 取消当前条目的详情/播放源预览请求，避免快速切换时旧请求回写到新选择。
    /// </summary>
    private void CancelSelectionPreview()
    {
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
    }

    /// <summary>
    /// 取消正在下载的 hero 图片，避免快速切换条目时旧图片覆盖新选择。
    /// </summary>
    private void CancelPosterLoad()
    {
        _posterCancellation?.Dispose();
        _posterCancellation = null;
    }

    /// <summary>
    /// 取消季/集列表加载请求，防止快速切换时旧季的数据回写。
    /// </summary>
    private void CancelEpisodeNavigation()
    {
        _episodeNavigationCancellation?.Dispose();
        _episodeNavigationCancellation = null;
    }

    /// <summary>
    /// 播放上报失败不阻塞播放，但需要在状态栏留下最新错误，便于定位 Emby 进度不同步。
    /// </summary>
    private void EmbyClient_PlaybackReportFailed(object? sender, PlaybackReportFailureEventArgs e)
    {
        _ = _logger.ErrorAsync(e.ReportName, e.Exception);
        Dispatcher.Invoke(() => SetStatus($"{e.ReportName}失败：{UserFacingMessages.BuildFriendlyErrorMessage(e.ReportName, e.Exception)}"));
    }

    /// <summary>
    /// 播放协调器的后台状态变化统一回到 UI 线程显示。
    /// </summary>
    private void PlaybackCoordinator_StatusChanged(object? sender, PlaybackStatusEventArgs e)
    {
        Dispatcher.Invoke(() => SetStatus(e.Message));
    }

}


