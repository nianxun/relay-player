using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Player.App.Models;

namespace Player.App.Services;

/// <summary>
/// 读取和写入应用持久化设置及服务器档案。
/// </summary>
public sealed class SettingsStore
{
    private const string CurrentSettingsFolderName = "RelayPlayer";
    private const string LegacySettingsFolderName = "EmbyMpvPlayer";
    private const string CorruptSettingsExtension = ".corrupt";
    private readonly TokenProtector _tokenProtector = new();

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                CurrentSettingsFolderName,
                "settings.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LegacySettingsFolderName,
                "settings.json"))
    {
    }

    /// <summary>
    /// 允许测试用例指定隔离路径，避免读写用户真实配置目录。
    /// </summary>
    internal SettingsStore(string settingsPath, string legacySettingsPath)
    {
        SettingsPath = settingsPath;
        LegacySettingsPath = legacySettingsPath;
    }

    public string SettingsPath { get; }

    private string LegacySettingsPath { get; }

    /// <summary>
    /// 读取当前配置；如果当前配置损坏，会备份坏文件并回退到旧版路径或默认设置。
    /// </summary>
    public async Task<AppSettings> LoadAsync()
    {
        foreach (var sourcePath in EnumerateExistingSettingsPaths())
        {
            try
            {
                await using var stream = File.OpenRead(sourcePath);
                var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions)
                               ?? new AppSettings();
                UnprotectTokens(settings);
                return settings;
            }
            catch (JsonException)
            {
                BackupCorruptSettings(sourcePath);
            }
            catch (IOException)
            {
                BackupCorruptSettings(sourcePath);
            }
            catch (UnauthorizedAccessException)
            {
                BackupCorruptSettings(sourcePath);
            }
        }

        return new AppSettings();
    }

    /// <summary>
    /// 先写入同目录临时文件，再覆盖目标文件，避免保存中断时留下半截 JSON。
    /// </summary>
    public async Task SaveAsync(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = SettingsPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, CreateProtectedCopy(settings), _jsonOptions);
        }

        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    /// <summary>
    /// 当前路径优先，旧版路径只作为迁移读取来源，不再继续写回旧目录。
    /// </summary>
    private IEnumerable<string> EnumerateExistingSettingsPaths()
    {
        if (File.Exists(SettingsPath))
        {
            yield return SettingsPath;
        }

        if (File.Exists(LegacySettingsPath))
        {
            yield return LegacySettingsPath;
        }
    }

    /// <summary>
    /// 坏配置不能直接删除，备份后用户仍可手动找回服务器地址或 token。
    /// </summary>
    private static void BackupCorruptSettings(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var backupPath = sourcePath + CorruptSettingsExtension + "." + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        File.Move(sourcePath, backupPath, overwrite: true);
    }

    /// <summary>
    /// 读取配置后把密文 token 解到内存字段；无法解密时清空会话，保留服务器档案本身。
    /// </summary>
    private void UnprotectTokens(AppSettings settings)
    {
        settings.AccessToken = UnprotectToken(settings.ProtectedAccessToken, settings.AccessToken);

        foreach (var profile in settings.ServerProfiles ?? [])
        {
            profile.AccessToken = UnprotectToken(profile.ProtectedAccessToken, profile.AccessToken);
        }
    }

    /// <summary>
    /// 保存前创建只用于序列化的副本，避免把 DPAPI 密文回写到运行时明文字段。
    /// </summary>
    private AppSettings CreateProtectedCopy(AppSettings settings)
    {
        return new AppSettings
        {
            ServerUrl = settings.ServerUrl,
            Username = settings.Username,
            AccessToken = string.Empty,
            ProtectedAccessToken = ProtectToken(settings.AccessToken),
            UserId = settings.UserId,
            DeviceId = settings.DeviceId,
            MpvNetPath = settings.MpvNetPath,
            LastServerProfileId = settings.LastServerProfileId,
            ServerProfiles = settings.ServerProfiles
                .Select(profile => new ServerProfile
                {
                    Id = profile.Id,
                    ServerUrl = profile.ServerUrl,
                    Username = profile.Username,
                    UserId = profile.UserId,
                    AccessToken = string.Empty,
                    ProtectedAccessToken = ProtectToken(profile.AccessToken),
                    LastUsedUtc = profile.LastUsedUtc
                })
                .ToList()
        };
    }

    /// <summary>
    /// 新字段优先；旧明文字段仅用于迁移，下一次保存时不会再落盘。
    /// </summary>
    private string UnprotectToken(string protectedToken, string legacyPlainToken)
    {
        var storedValue = string.IsNullOrWhiteSpace(protectedToken) ? legacyPlainToken : protectedToken;
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return string.Empty;
        }

        try
        {
            return _tokenProtector.Unprotect(storedValue);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 只有存在 token 时才生成密文，避免配置里出现无意义的 DPAPI payload。
    /// </summary>
    private string ProtectToken(string token)
    {
        return string.IsNullOrWhiteSpace(token) ? string.Empty : _tokenProtector.Protect(token);
    }
}
