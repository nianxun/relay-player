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
    /// 添加新的服务器档案，成功认证后立即切换并刷新继续观看。
    /// </summary>
    private async Task AddServerProfileAsync()
    {
        if (!TryShowLoginDialog(
                out var serverUrl,
                out var username,
                out var password,
                title: "添加 Emby 服务器",
                subtitle: "输入服务器地址和账户",
                submitText: "添加"))
        {
            return;
        }

        await AuthenticateAndActivateAsync(serverUrl, username, password, "正在添加服务器...");
    }

    /// <summary>
    /// 编辑服务器会重新认证，因为服务器地址或用户名变化后旧 token 不再可信。
    /// </summary>
    private async Task EditServerProfileAsync(ServerProfile profile)
    {
        if (!TryShowLoginDialog(
                out var serverUrl,
                out var username,
                out var password,
                title: "编辑服务器",
                subtitle: "修改地址或账户后会重新登录",
                submitText: "保存",
                initialProfile: profile))
        {
            return;
        }

        await AuthenticateAndActivateAsync(serverUrl, username, password, "正在更新服务器...");
    }

    /// <summary>
    /// 按 Emby 官方密码接口修改服务器端密码，并用新密码重新认证刷新本地 token。
    /// </summary>
    private async Task ChangeServerPasswordAsync(ServerProfile profile)
    {
        if (!profile.HasSavedSession)
        {
            SetStatus("当前服务器没有可用会话，请先登录后再修改密码。");
            return;
        }

        var dialog = new ChangePasswordDialog(profile.Username)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunGuardedAsync(
            "change password",
            async cancellationToken =>
            {
                SetStatus("正在修改密码...");
                _embyClient.Configure(profile.ServerUrl, profile.UserId, profile.AccessToken, _settings.DeviceId);
                await _embyClient.UpdatePasswordAsync(profile.UserId, dialog.NewPassword, cancellationToken);

                // 修改密码后立即用新密码重新认证，确保本地保存的 token 与服务器最新凭据一致。
                var result = await _embyClient.AuthenticateAsync(
                    profile.ServerUrl,
                    profile.Username,
                    dialog.NewPassword,
                    _settings.DeviceId,
                    cancellationToken);

                profile.AccessToken = result.AccessToken;
                profile.UserId = result.User!.Id;
                profile.LastUsedUtc = DateTimeOffset.UtcNow;

                SortServerProfiles(profile.Id);
                ApplyServerProfileToForm(profile);
                await SaveSettingsAsync();
                UpdateAuthStateUi(profile);
                SetStatus("密码已修改并刷新登录状态。");
            },
            invalidateSessionOnAuthFailure: false);
    }

    /// <summary>
    /// 删除服务器档案并清理当前会话；如果还有其他服务器，则自动选择最近一个。
    /// </summary>
    private async Task DeleteServerProfileAsync(ServerProfile profile)
    {
        var confirm = MessageBox.Show(
            this,
            $"删除服务器“{profile.DisplayName}”？",
            "删除服务器",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        var wasCurrentProfile =
            string.Equals(_settings.ServerUrl, profile.ServerUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_settings.Username, profile.Username, StringComparison.OrdinalIgnoreCase);
        var fallback = _serverProfileManager.DeleteProfile(_serverProfiles, _settings, profile);
        if (wasCurrentProfile)
        {
            ResetBrowseState(clearItems: true);
        }

        if (fallback is not null)
        {
            SelectServerProfile(fallback);
            ApplyServerProfileToForm(fallback);
        }
        else
        {
            ServerListBox.SelectedItem = null;
        }

        await SaveSettingsAsync();
        UpdateAuthStateUi(fallback);
        SetStatus(fallback is null ? "已删除服务器。" : $"已删除服务器，当前选择 {fallback.DisplayName}。");
    }
}
