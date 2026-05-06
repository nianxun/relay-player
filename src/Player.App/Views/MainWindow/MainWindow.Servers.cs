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
    /// 把旧版单服务器设置迁移到档案列表，并修复缺失的档案标识。
    /// </summary>
    private void NormalizeSettings()
    {
        _serverProfileManager.Normalize(_settings);
    }

    /// <summary>
    /// 填充服务器列表，并选择最适合自动恢复的档案。
    /// </summary>
    private void BindSettingsToUi()
    {
        _serverProfiles.Clear();
        foreach (var profile in _serverProfileManager.SortProfiles(_settings.ServerProfiles))
        {
            _serverProfiles.Add(profile);
        }

        var selected = _serverProfileManager.ResolveStartupProfile(_serverProfiles.ToList(), _settings.LastServerProfileId);

        if (selected is not null)
        {
            _isInitializing = true;
            ServerListBox.SelectedItem = selected;
            _isInitializing = false;
            ApplyServerProfileToForm(selected);
        }
        else
        {
            RefreshServerListSelection(null);
        }
    }

    private void UpdateAuthStateUi(ServerProfile? profile)
    {
        if (profile is null)
        {
            SetStatus("点击“添加服务器”连接 Emby 服务器。");
            RefreshServerListSelection(null);
            return;
        }

        if (!profile.HasSavedSession)
        {
            SetStatus($"已选择 {profile.DisplayName}。请输入密码登录。");
            RefreshServerListSelection(profile);
            return;
        }

        SetStatus($"已连接到 {profile.DisplayName}。");
        RefreshServerListSelection(profile);
    }

    /// <summary>
    /// 持久化当前表单设置和已排序的服务器历史。
    /// </summary>
    private async Task SaveSettingsAsync()
    {
        _settings.ServerProfiles = _serverProfiles.ToList();
        await _settingsStore.SaveAsync(_settings);
    }

    /// <summary>
    /// 按最近使用时间排序服务器历史，同时保留当前可见选择。
    /// </summary>
    private void SortServerProfiles(string? selectedProfileId = null)
    {
        var selected = selectedProfileId is null
            ? TryGetSelectedProfile(out var current) ? current : null
            : _serverProfiles.FirstOrDefault(profile => string.Equals(profile.Id, selectedProfileId, StringComparison.Ordinal));

        var ordered = _serverProfileManager.SortProfiles(_serverProfiles);

        _serverProfiles.Clear();
        foreach (var profile in ordered)
        {
            _serverProfiles.Add(profile);
        }

        if (selected is not null)
        {
            _isInitializing = true;
            ServerListBox.SelectedItem = selected;
            _isInitializing = false;
        }
    }

    /// <summary>
    /// 当 Emby 拒绝保存的会话时清空当前 token，但不删除服务器历史条目。
    /// </summary>
    private void InvalidateCurrentSession()
    {
        var profile = GetSelectedProfile();
        if (profile is not null)
        {
            _serverProfileManager.InvalidateSession(_settings, profile, DateTimeOffset.UtcNow);
            SortServerProfiles(profile.Id);
            RefreshServerListSelection(profile);
        }
        else
        {
            _serverProfileManager.InvalidateSession(_settings, profile, DateTimeOffset.UtcNow);
        }

        ResetBrowseState(clearItems: true);
        RefreshServerListSelection(profile);
    }

    private ServerProfile? GetSelectedProfile()
    {
        return ServerListBox.SelectedItem as ServerProfile;
    }

    private bool TryGetSelectedProfile(out ServerProfile? profile)
    {
        profile = GetSelectedProfile();
        return profile is not null;
    }

    private bool HasSavedSession()
    {
        return !string.IsNullOrWhiteSpace(_settings.ServerUrl) &&
               !string.IsNullOrWhiteSpace(_settings.UserId) &&
               !string.IsNullOrWhiteSpace(_settings.AccessToken);
    }

    private void ConfigureClientFromSettings()
    {
        _embyClient.Configure(_settings.ServerUrl, _settings.UserId, _settings.AccessToken, _settings.DeviceId);
    }
}


