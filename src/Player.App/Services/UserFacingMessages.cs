using System.Net;
using System.Net.Http;
using Player.App.Models;

namespace Player.App.Services;

/// <summary>
/// 集中生成主窗口状态栏和弹窗使用的用户可见文案。
/// </summary>
public static class UserFacingMessages
{
    /// <summary>
    /// 根据页面类型返回加载中的状态栏文案。
    /// </summary>
    internal static string GetLoadingMessage(BrowseViewKind kind)
    {
        return kind switch
        {
            BrowseViewKind.Library => "正在加载目录内容...",
            BrowseViewKind.Search => "正在加载搜索结果...",
            BrowseViewKind.Resume => "正在加载继续观看...",
            _ => "正在加载内容..."
        };
    }

    /// <summary>
    /// 根据页面类型和数量返回加载结束后的状态栏文案。
    /// </summary>
    internal static string BuildLoadedMessage(BrowseViewKind kind, int count)
    {
        return count == 0
            ? kind switch
            {
                BrowseViewKind.Resume => "没有可继续观看的内容。",
                BrowseViewKind.Search => "没有找到匹配内容。",
                _ => "没有找到内容。"
            }
            : $"已加载 {count} 个条目。";
    }

    /// <summary>
    /// 把底层异常转换成用户能理解的错误提示，同时保留操作名称用于区分登录和普通请求。
    /// </summary>
    public static string BuildFriendlyErrorMessage(string operationName, Exception ex)
    {
        return ex switch
        {
            EmbyApiException api when api.StatusCode == HttpStatusCode.Unauthorized =>
                operationName == "sign in"
                    ? "登录失败。Emby 服务器拒绝了用户名、密码或 token。"
                    : "Emby 会话已过期，请重新登录。",
            EmbyApiException api when api.StatusCode == HttpStatusCode.Forbidden =>
                operationName == "sign in"
                    ? "登录失败。Emby 服务器拒绝了这组凭据。"
                    : "Emby 服务器拒绝了这个请求，请检查用户权限。",
            EmbyApiException api when api.StatusCode == HttpStatusCode.NotFound =>
                "Emby 服务器返回 404，请检查服务器地址和基础路径。",
            EmbyApiException api when (int)api.StatusCode >= 500 =>
                $"Emby 服务器错误 {(int)api.StatusCode}，请稍后重试。",
            HttpRequestException =>
                "无法连接 Emby 服务器，请检查地址、网络和防火墙。",
            InvalidOperationException invalid => invalid.Message,
            _ => ex.Message
        };
    }
}
