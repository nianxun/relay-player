using Player.App.Models;

namespace Player.App.Services;

/// <summary>
/// 处理服务器档案的迁移、排序、查找和登录后的更新逻辑。
/// </summary>
public sealed class ServerProfileManager
{
    /// <summary>
    /// 标准化设置对象里的服务器历史，补齐缺失 ID，并把旧版单服务器设置迁移成档案列表。
    /// </summary>
    public void Normalize(AppSettings settings)
    {
        settings.ServerProfiles ??= [];

        if (settings.ServerProfiles.Count == 0 && HasLegacySession(settings))
        {
            settings.ServerProfiles.Add(new ServerProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                ServerUrl = settings.ServerUrl,
                Username = settings.Username,
                UserId = settings.UserId,
                AccessToken = settings.AccessToken,
                ProtectedAccessToken = settings.ProtectedAccessToken,
                LastUsedUtc = DateTimeOffset.UtcNow
            });
            settings.LastServerProfileId = settings.ServerProfiles[0].Id;
        }

        foreach (var profile in settings.ServerProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
            }
        }

        if (string.IsNullOrWhiteSpace(settings.LastServerProfileId) && settings.ServerProfiles.Count > 0)
        {
            settings.LastServerProfileId = settings.ServerProfiles
                .OrderByDescending(profile => profile.LastUsedUtc)
                .First()
                .Id;
        }
    }

    /// <summary>
    /// 登录成功后，按服务器地址和用户 ID 更新已有档案或新增档案。
    /// </summary>
    public ServerProfile UpsertFromLogin(
        IList<ServerProfile> profiles,
        string serverUrl,
        string username,
        AuthenticationResult result,
        DateTimeOffset now)
    {
        var normalizedServer = NormalizeServerUrlForComparison(serverUrl);
        var userId = result.User!.Id;

        var profile = profiles.FirstOrDefault(entry =>
            string.Equals(NormalizeServerUrlForComparison(entry.ServerUrl), normalizedServer, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.UserId, userId, StringComparison.Ordinal));

        if (profile is null)
        {
            profile = new ServerProfile
            {
                Id = Guid.NewGuid().ToString("N")
            };
            profiles.Add(profile);
        }

        profile.ServerUrl = serverUrl.Trim().TrimEnd('/');
        profile.Username = string.IsNullOrWhiteSpace(result.User.Name) ? username : result.User.Name;
        profile.UserId = userId;
        profile.AccessToken = result.AccessToken;
        profile.LastUsedUtc = now;
        return profile;
    }

    /// <summary>
    /// 按最近使用时间排序服务器历史。
    /// </summary>
    public IReadOnlyList<ServerProfile> SortProfiles(IEnumerable<ServerProfile> profiles)
    {
        return profiles
            .OrderByDescending(profile => profile.LastUsedUtc)
            .ThenBy(profile => profile.Username)
            .ThenBy(profile => profile.ServerUrl)
            .ToList();
    }

    /// <summary>
    /// 从当前服务器历史中选择最适合自动恢复的档案。
    /// </summary>
    public ServerProfile? ResolveStartupProfile(IReadOnlyList<ServerProfile> profiles, string lastServerProfileId)
    {
        return profiles.FirstOrDefault(profile => string.Equals(profile.Id, lastServerProfileId, StringComparison.Ordinal)) ??
               profiles.FirstOrDefault(profile => profile.HasSavedSession) ??
               profiles.FirstOrDefault();
    }

    /// <summary>
    /// 把服务器档案写入当前设置；有保存会话时同步 token，否则只保留服务器和用户名。
    /// </summary>
    /// <remarks>
    /// 主窗口、添加服务器、编辑服务器和修改密码都会走这个同步逻辑。
    /// 集中到这里可以避免只更新档案却忘了更新运行时设置，导致后续 API 请求仍指向旧服务器。
    /// </remarks>
    public void ApplyProfileToSettings(AppSettings settings, ServerProfile profile)
    {
        settings.ServerUrl = profile.ServerUrl;
        settings.Username = profile.Username;
        settings.LastServerProfileId = profile.Id;

        if (profile.HasSavedSession)
        {
            settings.UserId = profile.UserId;
            settings.AccessToken = profile.AccessToken;
        }
        else
        {
            settings.UserId = string.Empty;
            settings.AccessToken = string.Empty;
        }
    }

    /// <summary>
    /// 清空当前会话 token，但保留服务器档案本身，便于用户重新输入密码。
    /// </summary>
    public void InvalidateSession(AppSettings settings, ServerProfile? profile, DateTimeOffset now)
    {
        if (profile is not null)
        {
            profile.AccessToken = string.Empty;
            profile.UserId = string.Empty;
            profile.LastUsedUtc = now;
        }

        settings.AccessToken = string.Empty;
        settings.UserId = string.Empty;
        settings.LastServerProfileId = profile?.Id ?? settings.LastServerProfileId;
    }

    /// <summary>
    /// 删除服务器档案并返回删除后应选中的最近使用档案。
    /// </summary>
    /// <remarks>
    /// 如果删除的是当前设置指向的服务器，会同时清空当前运行时会话，避免 UI 仍显示旧服务器已连接。
    /// </remarks>
    public ServerProfile? DeleteProfile(
        IList<ServerProfile> profiles,
        AppSettings settings,
        ServerProfile profile)
    {
        profiles.Remove(profile);
        if (string.Equals(settings.LastServerProfileId, profile.Id, StringComparison.Ordinal))
        {
            settings.LastServerProfileId = string.Empty;
        }

        if (MatchesCurrentSettings(settings, profile))
        {
            settings.ServerUrl = string.Empty;
            settings.Username = string.Empty;
            settings.UserId = string.Empty;
            settings.AccessToken = string.Empty;
        }

        return profiles
            .OrderByDescending(entry => entry.LastUsedUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// 旧版设置里只保存单个服务器会话时，作为迁移来源。
    /// </summary>
    public static bool HasLegacySession(AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.ServerUrl) &&
               !string.IsNullOrWhiteSpace(settings.UserId) &&
               !string.IsNullOrWhiteSpace(settings.AccessToken);
    }

    /// <summary>
    /// 把服务器地址标准化后再比较，避免因为是否带协议或尾部斜杠导致重复档案。
    /// </summary>
    public static string NormalizeServerUrlForComparison(string serverUrl)
    {
        var normalized = serverUrl.Trim();
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        return normalized.TrimEnd('/');
    }

    /// <summary>
    /// 判断档案是否就是当前设置中的服务器身份；用户名和地址都匹配才认为需要清空当前会话。
    /// </summary>
    private static bool MatchesCurrentSettings(AppSettings settings, ServerProfile profile)
    {
        return string.Equals(settings.ServerUrl, profile.ServerUrl, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(settings.Username, profile.Username, StringComparison.OrdinalIgnoreCase);
    }
}
