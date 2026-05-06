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
    /// 打开独立的登录弹窗，收集服务器地址、用户名和密码。
    /// </summary>
    /// <remarks>
    /// 同一个弹窗服务于添加、编辑和修改密码；修改密码时会锁定地址和用户名，防止误改档案身份。
    /// </remarks>
    private bool TryShowLoginDialog(
        out string serverUrl,
        out string username,
        out string password,
        string title,
        string subtitle,
        string submitText,
        ServerProfile? initialProfile = null,
        bool lockServerAndUsername = false)
    {
        var selected = initialProfile ?? GetSelectedProfile();
        var initialServerUrl = !string.IsNullOrWhiteSpace(selected?.ServerUrl)
            ? selected.ServerUrl
            : _settings.ServerUrl;
        var initialUsername = !string.IsNullOrWhiteSpace(selected?.Username)
            ? selected.Username
            : _settings.Username;

        var dialog = new LoginDialog(
            initialServerUrl,
            initialUsername,
            title,
            subtitle,
            submitText,
            lockServerAndUsername)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            serverUrl = string.Empty;
            username = string.Empty;
            password = string.Empty;
            return false;
        }

        serverUrl = dialog.ServerUrl;
        username = dialog.Username;
        password = dialog.Password;
        return true;
    }

    /// <summary>
    /// 让服务器列表重绘当前档案的显示文本和选中态。
    /// </summary>
    private void RefreshServerListSelection(ServerProfile? profile)
    {
        if (profile is not null && ServerListBox.SelectedItem != profile)
        {
            ServerListBox.SelectedItem = profile;
        }

        ServerListBox.Items.Refresh();
    }

    /// <summary>
    /// 从模板内部命中的视觉元素向上查找指定父控件，供服务器列表点击和右键菜单定位条目。
    /// </summary>
    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void ClearSelectedItem()
    {
        _selectedItem = null;
        _selectedPlaybackInfo = null;
        ItemsListView.SelectedItem = null;
        BindingOperations.ClearBinding(PosterImage, Image.SourceProperty);
        PosterImage.Source = null;
        SelectedTitleTextBlock.Text = "未选择媒体";
        SelectedMetaTextBlock.Text = "";
        SelectedSourceTextBlock.Text = "";
        SelectedOverviewTextBlock.Text = "暂无媒体详情。";
        TogglePlayedButton.ToolTip = "标记已播放";
        TogglePlayedButton.Tag = "False";
        TogglePlayedButton.IsEnabled = false;
        MediaSourceComboBox.ItemsSource = null;
        ClearTrackSelectors();
        ClearEpisodeSelectionUi();
    }

    /// <summary>
    /// 当当前服务器或认证状态变化时，把列表导航重置到默认视图。
    /// </summary>
    private void ResetBrowseState(bool clearItems)
    {
        CancelSelectionPreview();
        CancelEpisodeNavigation();
        _navigationStack.Clear();
        _currentState = CreateDefaultBrowseState();
        UpdateBackButtonVisibility();
        ListTitleTextBlock.Text = _currentState.Title;
        ItemsHeaderTextBlock.Text = _currentState.Title;
        UpdateViewButtons(_currentState.Kind);
        SetDetailPanelVisibility(false);
        SetItemsListVisibility(true);

        if (clearItems)
        {
            _items.Clear();
        }

        ClearSelectedItem();
    }

}


