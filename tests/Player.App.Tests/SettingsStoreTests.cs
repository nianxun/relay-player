using System.Text.Json;
using Player.App.Models;
using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public async Task SaveAsync_WritesProtectedTokensWithoutPlainText()
    {
        using var workspace = new TemporarySettingsWorkspace();
        var store = workspace.CreateStore();
        var settings = new AppSettings
        {
            ServerUrl = "http://server",
            Username = "demo",
            UserId = "user-1",
            AccessToken = "main-token",
            ServerProfiles =
            [
                new ServerProfile
                {
                    Id = "profile-1",
                    ServerUrl = "http://server",
                    Username = "demo",
                    UserId = "user-1",
                    AccessToken = "profile-token"
                }
            ]
        };

        await store.SaveAsync(settings);
        var json = await File.ReadAllTextAsync(workspace.SettingsPath);

        Assert.DoesNotContain("main-token", json);
        Assert.DoesNotContain("profile-token", json);
        Assert.Contains("ProtectedAccessToken", json);
    }

    [TestMethod]
    public async Task LoadAsync_ReadsLegacyPlainTokenThenSaveMigratesToProtectedToken()
    {
        using var workspace = new TemporarySettingsWorkspace();
        var legacySettings = new AppSettings
        {
            ServerUrl = "http://server",
            Username = "demo",
            UserId = "user-1",
            AccessToken = "legacy-main-token",
            ServerProfiles =
            [
                new ServerProfile
                {
                    Id = "profile-1",
                    ServerUrl = "http://server",
                    Username = "demo",
                    UserId = "user-1",
                    AccessToken = "legacy-profile-token"
                }
            ]
        };
        await workspace.WriteSettingsAsync(legacySettings);

        var store = workspace.CreateStore();
        var loaded = await store.LoadAsync();
        await store.SaveAsync(loaded);
        var migratedJson = await File.ReadAllTextAsync(workspace.SettingsPath);

        Assert.AreEqual("legacy-main-token", loaded.AccessToken);
        Assert.AreEqual("legacy-profile-token", loaded.ServerProfiles[0].AccessToken);
        Assert.DoesNotContain("legacy-main-token", migratedJson);
        Assert.DoesNotContain("legacy-profile-token", migratedJson);
    }

    [TestMethod]
    public async Task LoadAsync_BacksUpCorruptSettingsAndReturnsDefaultSettings()
    {
        using var workspace = new TemporarySettingsWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.SettingsPath)!);
        await File.WriteAllTextAsync(workspace.SettingsPath, "{ broken json");

        var store = workspace.CreateStore();
        var loaded = await store.LoadAsync();
        var backups = Directory.GetFiles(Path.GetDirectoryName(workspace.SettingsPath)!, "settings.json.corrupt.*");

        Assert.AreEqual(string.Empty, loaded.ServerUrl);
        Assert.HasCount(1, backups);
        Assert.IsFalse(File.Exists(workspace.SettingsPath));
    }

    private sealed class TemporarySettingsWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "RelayPlayerTests", Guid.NewGuid().ToString("N"));

        public string SettingsPath => Path.Combine(_root, "current", "settings.json");

        public string LegacySettingsPath => Path.Combine(_root, "legacy", "settings.json");

        public SettingsStore CreateStore()
        {
            return new SettingsStore(SettingsPath, LegacySettingsPath);
        }

        public async Task WriteSettingsAsync(AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            await using var stream = File.Create(SettingsPath);
            await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true });
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
