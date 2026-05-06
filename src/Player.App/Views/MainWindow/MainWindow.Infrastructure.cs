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
    private void TryApplyWindowIcon()
    {
        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/RelayPlayer.png", UriKind.Absolute));
        }
        catch
        {
        }
    }

    /// <summary>
    /// 包装界面操作，以便统一显示友好错误信息并保持窗口响应。
    /// </summary>
    private async Task RunGuardedAsync(
        string operationName,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default,
        bool invalidateSessionOnAuthFailure = true,
        bool showBusyOverlay = true)
    {
        try
        {
            if (showBusyOverlay)
            {
                SetBusy(true);
            }
            await action(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消。");
        }
        catch (EmbyApiException ex)
        {
            if (invalidateSessionOnAuthFailure &&
                ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // 保存的 token 可能在服务器端被吊销，清空后下一次操作会要求重新登录。
                InvalidateCurrentSession();
                await SaveSettingsAsync();
            }

            var message = UserFacingMessages.BuildFriendlyErrorMessage(operationName, ex);
            SetStatus(message);
            MessageBox.Show(this, message, AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (HttpRequestException ex)
        {
            var message = UserFacingMessages.BuildFriendlyErrorMessage(operationName, ex);
            SetStatus(message);
            MessageBox.Show(this, message, AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            var message = UserFacingMessages.BuildFriendlyErrorMessage(operationName, ex);
            SetStatus(message);
            MessageBox.Show(this, message, AppDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (showBusyOverlay)
            {
                SetBusy(false);
            }
        }
    }

    /// <summary>
    /// 将服务器档案设为当前档案，持久化选择，并按需加载默认视图。
    /// </summary>
}


