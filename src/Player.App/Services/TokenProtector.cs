using System.Security.Cryptography;
using System.Text;

namespace Player.App.Services;

/// <summary>
/// 使用 Windows DPAPI 保护落盘 token；密文只能由当前 Windows 用户解开。
/// </summary>
public sealed class TokenProtector
{
    private const string ProtectedPrefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RelayPlayer.AccessToken.v1");

    /// <summary>
    /// 把明文 token 转成带前缀的 DPAPI 密文，便于配置文件里区分新旧格式。
    /// </summary>
    public string Protect(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        if (IsProtected(token))
        {
            return token;
        }

        var plainBytes = Encoding.UTF8.GetBytes(token);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
    }

    /// <summary>
    /// 解开 DPAPI 密文；旧版明文 token 会原样返回，供读取后自动迁移。
    /// </summary>
    public string Unprotect(string tokenOrProtectedToken)
    {
        if (string.IsNullOrWhiteSpace(tokenOrProtectedToken))
        {
            return string.Empty;
        }

        if (!IsProtected(tokenOrProtectedToken))
        {
            return tokenOrProtectedToken;
        }

        var payload = tokenOrProtectedToken[ProtectedPrefix.Length..];
        var protectedBytes = Convert.FromBase64String(payload);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// 判断配置值是否已经是新格式，避免重复加密。
    /// </summary>
    public bool IsProtected(string value)
    {
        return value.StartsWith(ProtectedPrefix, StringComparison.Ordinal);
    }
}
