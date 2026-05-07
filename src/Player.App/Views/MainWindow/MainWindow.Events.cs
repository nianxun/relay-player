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
    /// 加载持久化设置、填充服务器历史，并尝试恢复最后一次可用会话。
    /// </summary>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;

        _settings = await _settingsStore.LoadAsync();
        _serverProfileManager.Normalize(_settings);
        BindSettingsToUi();

        MpvPathTextBox.Text = _mpvLauncher.ResolveExecutable(_settings.MpvNetPath);
        if (!string.Equals(_settings.MpvNetPath, MpvPathTextBox.Text, StringComparison.Ordinal))
        {
            _settings.MpvNetPath = MpvPathTextBox.Text;
            await SaveSettingsAsync();
        }

        UpdateBackButtonVisibility();
        UpdateViewButtons(_currentState.Kind);
        SetStatus("就绪");

        _isInitializing = false;

        if (TryGetSelectedProfile(out var profile) && profile is not null && profile.HasSavedSession)
        {
            SetStatus($"正在恢复 {profile.DisplayName} 的会话...");
            await ActivateServerProfileAsync(profile, loadInitialView: true);
            return;
        }

        UpdateAuthStateUi(null);
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _loadCancellation?.Dispose();
        _loadCancellation = null;

        _playbackCoordinator.CancelMonitoring();
        CancelSelectionPreview();
        CancelEpisodeNavigation();
        CancelPosterLoad();
        _playbackCoordinator.StatusChanged -= PlaybackCoordinator_StatusChanged;
        _playbackCoordinator.PlaybackStopped -= PlaybackCoordinator_PlaybackStopped;
        _embyClient.PlaybackReportFailed -= EmbyClient_PlaybackReportFailed;
    }

    /// <summary>
    /// 窗口状态可能由双击、任务栏或系统快捷键改变，标题栏图标需要统一刷新。
    /// </summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateWindowStateButton();
    }

    private async void ContinueWatchingButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToAsync(new BrowseState(BrowseViewKind.Resume, "继续观看"));
    }

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SearchAsync();
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_navigationStack.Count == 0)
        {
            return;
        }

        var previousState = _navigationStack.Pop();
        await LoadViewAsync(previousState, pushCurrent: false);
    }

    private async void BrowseMpvButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select mpv.net executable",
            Filter = "mpv.net executable|mpvnet.exe;mpv.net.exe|Executable files|*.exe|All files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            MpvPathTextBox.Text = dialog.FileName;
            _settings.MpvNetPath = dialog.FileName;
            await SaveSettingsAsync();
            SetStatus("已保存 mpv.net 路径。");
        }
    }

    /// <summary>
    /// 自绘标题栏接管拖拽和双击最大化，避免系统默认白色标题栏破坏深色界面。
    /// </summary>
    private void TitleBarDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowMaximized();
            return;
        }

        DragMove();
    }

    /// <summary>
    /// 最小化按钮只改变窗口状态，不触发布局重建。
    /// </summary>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 最大化/还原按钮保持自绘标题栏按钮状态和窗口状态一致。
    /// </summary>
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowMaximized();
    }

    /// <summary>
    /// 关闭按钮沿用窗口关闭流程，确保取消令牌和外部资源正常释放。
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 缩略图加载失败时隐藏 Image 控件，继续展示深色占位和文字信息。
    /// </summary>
    private void ThumbnailImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image image)
        {
            image.Visibility = Visibility.Collapsed;

            if (image.DataContext is EmbyItem item)
            {
                item.ThumbnailUri = null;
            }
        }
    }
}


