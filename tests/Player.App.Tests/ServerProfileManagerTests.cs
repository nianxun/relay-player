using Player.App.Models;
using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class ServerProfileManagerTests
{
    [TestMethod]
    public void Normalize_LegacySession_IsMigratedToServerProfile()
    {
        var manager = new ServerProfileManager();
        var settings = new AppSettings
        {
            ServerUrl = "http://127.0.0.1:8096",
            Username = "demo",
            UserId = "user-1",
            AccessToken = "token-1"
        };

        manager.Normalize(settings);

        Assert.HasCount(1, settings.ServerProfiles);
        Assert.AreEqual("user-1", settings.ServerProfiles[0].UserId);
        Assert.AreEqual(settings.ServerProfiles[0].Id, settings.LastServerProfileId);
    }

    [TestMethod]
    public void UpsertFromLogin_ReusesExistingProfileForSameServerAndUser()
    {
        var manager = new ServerProfileManager();
        var profiles = new List<ServerProfile>
        {
            new()
            {
                Id = "profile-1",
                ServerUrl = "http://example.com:8096",
                Username = "old",
                UserId = "user-1",
                AccessToken = "old-token",
                LastUsedUtc = DateTimeOffset.MinValue
            }
        };

        var result = new AuthenticationResult
        {
            AccessToken = "new-token",
            User = new EmbyUser
            {
                Id = "user-1",
                Name = "new-name"
            }
        };

        var profile = manager.UpsertFromLogin(
            profiles,
            "example.com:8096",
            "fallback-name",
            result,
            new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero));

        Assert.AreSame(profiles[0], profile);
        Assert.AreEqual("new-name", profile.Username);
        Assert.AreEqual("new-token", profile.AccessToken);
        Assert.HasCount(1, profiles);
    }

    [TestMethod]
    public void ResolveStartupProfile_PrefersLastSelectedThenSavedSession()
    {
        var manager = new ServerProfileManager();
        var selected = new ServerProfile
        {
            Id = "selected",
            ServerUrl = "http://one",
            UserId = "u1",
            AccessToken = "t1",
            LastUsedUtc = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var fallback = new ServerProfile
        {
            Id = "fallback",
            ServerUrl = "http://two",
            UserId = "u2",
            AccessToken = "t2",
            LastUsedUtc = DateTimeOffset.UtcNow
        };

        var chosen = manager.ResolveStartupProfile([selected, fallback], "selected");

        Assert.AreSame(selected, chosen);
    }

    [TestMethod]
    public void ApplyProfileToSettings_ProfileHasSession_CopiesConnectionAndToken()
    {
        var manager = new ServerProfileManager();
        var settings = new AppSettings();
        var profile = new ServerProfile
        {
            Id = "profile-1",
            ServerUrl = "http://server",
            Username = "demo",
            UserId = "user-1",
            AccessToken = "token-1"
        };

        manager.ApplyProfileToSettings(settings, profile);

        Assert.AreEqual("http://server", settings.ServerUrl);
        Assert.AreEqual("demo", settings.Username);
        Assert.AreEqual("user-1", settings.UserId);
        Assert.AreEqual("token-1", settings.AccessToken);
        Assert.AreEqual("profile-1", settings.LastServerProfileId);
    }

    [TestMethod]
    public void ApplyProfileToSettings_ProfileWithoutSession_ClearsRuntimeToken()
    {
        var manager = new ServerProfileManager();
        var settings = new AppSettings
        {
            UserId = "old-user",
            AccessToken = "old-token"
        };
        var profile = new ServerProfile
        {
            Id = "profile-1",
            ServerUrl = "http://server",
            Username = "demo"
        };

        manager.ApplyProfileToSettings(settings, profile);

        Assert.AreEqual("http://server", settings.ServerUrl);
        Assert.AreEqual("demo", settings.Username);
        Assert.AreEqual(string.Empty, settings.UserId);
        Assert.AreEqual(string.Empty, settings.AccessToken);
    }

    [TestMethod]
    public void InvalidateSession_ClearsProfileAndSettingsToken()
    {
        var manager = new ServerProfileManager();
        var settings = new AppSettings
        {
            UserId = "user-1",
            AccessToken = "token-1",
            LastServerProfileId = "profile-1"
        };
        var profile = new ServerProfile
        {
            Id = "profile-1",
            UserId = "user-1",
            AccessToken = "token-1"
        };
        var now = new DateTimeOffset(2026, 5, 6, 13, 0, 0, TimeSpan.Zero);

        manager.InvalidateSession(settings, profile, now);

        Assert.AreEqual(string.Empty, profile.UserId);
        Assert.AreEqual(string.Empty, profile.AccessToken);
        Assert.AreEqual(now, profile.LastUsedUtc);
        Assert.AreEqual(string.Empty, settings.UserId);
        Assert.AreEqual(string.Empty, settings.AccessToken);
        Assert.AreEqual("profile-1", settings.LastServerProfileId);
    }

    [TestMethod]
    public void DeleteProfile_CurrentProfile_ClearsSettingsAndReturnsMostRecentFallback()
    {
        var manager = new ServerProfileManager();
        var removed = new ServerProfile
        {
            Id = "removed",
            ServerUrl = "http://removed",
            Username = "demo",
            UserId = "user-1",
            AccessToken = "token-1",
            LastUsedUtc = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var fallback = new ServerProfile
        {
            Id = "fallback",
            ServerUrl = "http://fallback",
            Username = "demo",
            UserId = "user-2",
            AccessToken = "token-2",
            LastUsedUtc = DateTimeOffset.UtcNow
        };
        var profiles = new List<ServerProfile> { removed, fallback };
        var settings = new AppSettings
        {
            ServerUrl = "http://removed",
            Username = "demo",
            UserId = "user-1",
            AccessToken = "token-1",
            LastServerProfileId = "removed"
        };

        var result = manager.DeleteProfile(profiles, settings, removed);

        Assert.AreSame(fallback, result);
        CollectionAssert.DoesNotContain(profiles, removed);
        Assert.AreEqual(string.Empty, settings.ServerUrl);
        Assert.AreEqual(string.Empty, settings.Username);
        Assert.AreEqual(string.Empty, settings.UserId);
        Assert.AreEqual(string.Empty, settings.AccessToken);
        Assert.AreEqual(string.Empty, settings.LastServerProfileId);
    }
}
