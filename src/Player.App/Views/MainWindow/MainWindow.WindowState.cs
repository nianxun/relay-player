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
    /// 根据窗口状态更新最大化按钮图标，避免自绘标题栏和真实窗口状态脱节。
    /// </summary>
    private void ToggleWindowMaximized()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        UpdateWindowStateButton();
    }

    /// <summary>
    /// 根据当前窗口状态切换最大化/还原图标。
    /// </summary>
    private void UpdateWindowStateButton()
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }
}


