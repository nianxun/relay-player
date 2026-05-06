using System.Net;
using System.Net.Http;
using Player.App.Models;
using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class UserFacingMessagesTests
{
    [TestMethod]
    public void BuildFriendlyErrorMessage_UnauthorizedSignIn_UsesLoginMessage()
    {
        var message = UserFacingMessages.BuildFriendlyErrorMessage(
            "sign in",
            new EmbyApiException(HttpStatusCode.Unauthorized, null, null, "Unauthorized"));

        Assert.AreEqual("登录失败。Emby 服务器拒绝了用户名、密码或 token。", message);
    }

    [TestMethod]
    public void BuildFriendlyErrorMessage_HttpRequest_UsesConnectionMessage()
    {
        var message = UserFacingMessages.BuildFriendlyErrorMessage(
            "load media",
            new HttpRequestException("network failed"));

        Assert.AreEqual("无法连接 Emby 服务器，请检查地址、网络和防火墙。", message);
    }

    [TestMethod]
    public void GetLoadingAndLoadedMessages_ReturnViewSpecificText()
    {
        Assert.AreEqual("正在加载搜索结果...", UserFacingMessages.GetLoadingMessage(BrowseViewKind.Search));
        Assert.AreEqual("没有可继续观看的内容。", UserFacingMessages.BuildLoadedMessage(BrowseViewKind.Resume, 0));
        Assert.AreEqual("已加载 3 个条目。", UserFacingMessages.BuildLoadedMessage(BrowseViewKind.Library, 3));
    }
}
