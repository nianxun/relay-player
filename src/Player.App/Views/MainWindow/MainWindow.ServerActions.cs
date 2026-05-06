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
    /// 切换到指定服务器档案；必要时会重新加载默认首页，确保继续观看区域和会话状态同步。
    /// </summary>
    private async Task ActivateServerProfileAsync(ServerProfile profile, bool loadInitialView)
    {
        _isSwitchingServerProfile = true;

        try
        {
            profile.LastUsedUtc = DateTimeOffset.UtcNow;
            SortServerProfiles(profile.Id);
            ApplyServerProfileToForm(profile);
            SelectServerProfile(profile);
            await SaveSettingsAsync();
            ConfigureClientFromSettings();
            UpdateAuthStateUi(profile);

            if (loadInitialView)
            {
                ResetBrowseState(clearItems: true);
                await LoadViewAsync(CreateDefaultBrowseState(), pushCurrent: false);
            }
        }
        finally
        {
            _isSwitchingServerProfile = false;
        }
    }

    /// <summary>
    /// 把选中的服务器档案同步到登录表单和当前设置对象。
    /// </summary>
    private void ApplyServerProfileToForm(ServerProfile profile)
    {
        _serverProfileManager.ApplyProfileToSettings(_settings, profile);
        RefreshServerListSelection(profile);
    }

    /// <summary>
    /// 选择下拉框条目，同时避免递归触发快速切换处理逻辑。
    /// </summary>
    private void SelectServerProfile(ServerProfile profile)
    {
        _isInitializing = true;
        try
        {
            ServerListBox.SelectedItem = profile;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// 处理服务器列表的一键切换；即使点击的是当前服务器，也按用户预期重新刷新继续观看。
    /// </summary>
    private async Task SwitchServerProfileAsync(ServerProfile profile)
    {
        profile.LastUsedUtc = DateTimeOffset.UtcNow;
        SortServerProfiles(profile.Id);
        ApplyServerProfileToForm(profile);

        if (!profile.HasSavedSession)
        {
            _settings.LastServerProfileId = profile.Id;
            await SaveSettingsAsync();
            ResetBrowseState(clearItems: true);
            UpdateAuthStateUi(profile);
            return;
        }

        await ActivateServerProfileAsync(profile, loadInitialView: true);
    }

    /// <summary>
    /// 登录成功后创建或更新保存的服务器档案。
    /// </summary>
    private ServerProfile UpsertServerProfileFromLogin(string serverUrl, string username, AuthenticationResult result)
    {
        var profile = _serverProfileManager.UpsertFromLogin(
            _serverProfiles,
            serverUrl,
            username,
            result,
            DateTimeOffset.UtcNow);
        SortServerProfiles(profile.Id);
        ApplyServerProfileToForm(profile);
        SelectServerProfile(profile);
        return profile;
    }

    /// <summary>
    /// 统一处理添加、编辑和修改密码后的认证、保存、切换和刷新。
    /// </summary>
    private async Task AuthenticateAndActivateAsync(string serverUrl, string username, string password, string status)
    {
        await RunGuardedAsync(
            "sign in",
            async cancellationToken =>
            {
                SetStatus(status);
                var result = await _embyClient.AuthenticateAsync(serverUrl, username, password, _settings.DeviceId, cancellationToken);
                var profile = UpsertServerProfileFromLogin(serverUrl, username, result);

                _navigationStack.Clear();
                await ActivateServerProfileAsync(profile, loadInitialView: true);
                SetStatus($"已连接：{profile.DisplayName}。");
            },
            invalidateSessionOnAuthFailure: false);
    }
}
