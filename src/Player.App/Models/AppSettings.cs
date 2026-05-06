namespace Player.App.Models;

/// <summary>
/// 保存到用户配置目录的应用设置。
/// </summary>
public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string ProtectedAccessToken { get; set; } = "";
    public string UserId { get; set; } = "";
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string MpvNetPath { get; set; } = "";
    public string LastServerProfileId { get; set; } = "";
    public List<ServerProfile> ServerProfiles { get; set; } = [];
}
