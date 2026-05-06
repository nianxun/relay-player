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
    /// 使用 Emby 账号密码登录，保存服务器会话，并打开默认媒体视图。
    /// </summary>
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await AddServerProfileAsync();
    }

    /// <summary>
    /// 列表项选择变化时，自动切换服务器并刷新当前视图。
    /// </summary>
    private async void ServerListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isSwitchingServerProfile)
        {
            return;
        }

        if (ServerListBox.SelectedItem is not ServerProfile profile)
        {
            return;
        }

        await SwitchServerProfileAsync(profile);
    }

    /// <summary>
    /// 左键单击服务器即执行切换和刷新；已选中的服务器再次点击也会刷新当前视图。
    /// </summary>
    private async void ServerListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isInitializing || _isSwitchingServerProfile)
        {
            return;
        }

        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not ServerProfile profile)
        {
            return;
        }

        if (ReferenceEquals(ServerListBox.SelectedItem, profile))
        {
            e.Handled = true;
            await SwitchServerProfileAsync(profile);
        }
    }

    /// <summary>
    /// 右键菜单打开前先把鼠标所在服务器设为当前项，避免编辑或删除误作用到旧选择。
    /// </summary>
    private void ServerListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }

    /// <summary>
    /// 服务器菜单里的编辑入口，复用统一登录弹窗收集新凭据。
    /// </summary>
    private async void EditServerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedProfile() is not { } profile)
        {
            SetStatus("请先选择服务器。");
            return;
        }

        await EditServerProfileAsync(profile);
    }

    /// <summary>
    /// 服务器菜单里的修改密码入口，走独立窗体避免误操作。
    /// </summary>
    private async void ChangeServerPasswordMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedProfile() is not { } profile)
        {
            SetStatus("请先选择服务器。");
            return;
        }

        await ChangeServerPasswordAsync(profile);
    }

    /// <summary>
    /// 删除服务器前再次确认，避免在服务器历史里误删正在使用的会话。
    /// </summary>
    private async void DeleteServerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedProfile() is not { } profile)
        {
            SetStatus("请先选择服务器。");
            return;
        }

        await DeleteServerProfileAsync(profile);
    }
}
