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
    /// 根据后台加载状态统一禁用可触发请求的控件，避免并发操作互相覆盖 UI。
    /// </summary>
    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        LoginButton.IsEnabled = !isBusy;
        ContinueWatchingButton.IsEnabled = !isBusy;
        SearchTextBox.IsEnabled = !isBusy;
        ServerListBox.IsEnabled = !isBusy;
        ItemsListView.IsEnabled = !isBusy;
        SeasonComboBox.IsEnabled = !isBusy;
        EpisodeComboBox.IsEnabled = !isBusy;
        MediaSourceComboBox.IsEnabled = !isBusy;
        VideoTrackComboBox.IsEnabled = !isBusy;
        AudioTrackComboBox.IsEnabled = !isBusy;
        SubtitleTrackComboBox.IsEnabled = !isBusy;
        ResumeCheckBox.IsEnabled = !isBusy;
        AutoPlayCheckBox.IsEnabled = !isBusy;
        PlayButton.IsEnabled = !isBusy;
        TogglePlayedButton.IsEnabled = !isBusy && _selectedItem is not null && !EmbyItemKind.IsContainer(_selectedItem);
        BrowseMpvButton.IsEnabled = !isBusy;
        Cursor = isBusy ? Cursors.Wait : Cursors.Arrow;
    }

    /// <summary>
    /// 设置底部状态栏文本，所有后台流程都通过这里给用户明确反馈。
    /// </summary>
    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    /// <summary>
    /// 仅在存在返回目标时显示返回按钮，避免主视图出现无效操作。
    /// </summary>
    private void UpdateBackButtonVisibility()
    {
        BackButton.Visibility = _navigationStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 详情面板按页面状态整块折叠；使用 Collapsed 能从布局流中移除它，彻底消除入口页的预留空白。
    /// </summary>
    private void SetDetailPanelVisibility(bool isVisible)
    {
        DetailPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ItemsHeaderPanel.Margin = isVisible
            ? new Thickness(0, 18, 0, 10)
            : new Thickness(0, 0, 0, 10);
    }

    /// <summary>
    /// 根据目标页面类型一次性切换主要区域，保证异步加载期间不会出现旧页和新页叠在一起。
    /// </summary>
    private void ApplyViewShellState(BrowseViewKind kind)
    {
        var isDetailOnly = kind == BrowseViewKind.Detail;
        SetDetailPanelVisibility(kind != BrowseViewKind.Resume);
        SetItemsListVisibility(!isDetailOnly);
    }

    /// <summary>
    /// 继续观看条目进入详情后隐藏入口列表；搜索和目录页仍保留列表，方便连续切换条目。
    /// </summary>
    private void SetItemsListVisibility(bool isVisible)
    {
        var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ItemsHeaderPanel.Visibility = visibility;
        ItemsListView.Visibility = visibility;
    }

    /// <summary>
    /// 刷新导航按钮的选中态，目前只保留“继续观看”入口。
    /// </summary>
    private void UpdateViewButtons(BrowseViewKind kind)
    {
        ApplyViewButtonStyle(ContinueWatchingButton, kind == BrowseViewKind.Resume);
    }

    /// <summary>
    /// 按约定把按钮选中态写到 Tag，交给 XAML 样式统一处理视觉状态。
    /// </summary>
    private static void ApplyViewButtonStyle(System.Windows.Controls.Button button, bool isActive)
    {
        button.Tag = isActive ? "True" : "False";
        button.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimaryBrush");
    }
}
