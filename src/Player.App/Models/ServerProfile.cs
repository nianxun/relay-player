using System.Text.Json.Serialization;

namespace Player.App.Models;

/// <summary>
/// 保存的 Emby 服务器会话状态，用于服务器历史、快速切换和自动恢复。
/// </summary>
public sealed class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string UserId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string ProtectedAccessToken { get; set; } = "";
    public DateTimeOffset LastUsedUtc { get; set; } = DateTimeOffset.MinValue;

    [JsonIgnore]
    public bool HasSavedSession => !string.IsNullOrWhiteSpace(ServerUrl) &&
                                   !string.IsNullOrWhiteSpace(UserId) &&
                                   !string.IsNullOrWhiteSpace(AccessToken);

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                return ServerUrl;
            }

            if (string.IsNullOrWhiteSpace(ServerUrl))
            {
                return Username;
            }

            return $"{Username} @ {ServerUrl}";
        }
    }

    /// <summary>
    /// 让服务器列表在选中状态下继续显示档案名称，而不是模型类型名。
    /// </summary>
    public override string ToString() => DisplayName;
}
