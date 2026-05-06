using Player.App.Models;
using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class ArtworkResolverTests
{
    [TestMethod]
    public void AttachThumbnailUris_ItemHasThumbTag_UsesOwnThumbImage()
    {
        using var client = CreateClient();
        var resolver = new ArtworkResolver(client);
        var item = new EmbyItem
        {
            Id = "item-1",
            Type = "Episode",
            ImageTags = new Dictionary<string, string>
            {
                ["Thumb"] = "thumb-tag",
                ["Primary"] = "primary-tag"
            }
        };

        resolver.AttachThumbnailUris([item], "token-1");

        var uri = item.ThumbnailUri?.AbsoluteUri ?? "";
        StringAssert.Contains(uri, "/Items/item-1/Images/Thumb");
        StringAssert.Contains(uri, "tag=thumb-tag");
        StringAssert.Contains(uri, "api_key=token-1");
    }

    [TestMethod]
    public void AttachThumbnailUris_EpisodeHasParentThumb_UsesParentThumbImage()
    {
        using var client = CreateClient();
        var resolver = new ArtworkResolver(client);
        var item = new EmbyItem
        {
            Id = "episode-1",
            Type = "Episode",
            ParentThumbImageItemId = "season-1",
            ParentThumbImageTag = "season-thumb-tag"
        };

        resolver.AttachThumbnailUris([item], "token-1");

        var uri = item.ThumbnailUri?.AbsoluteUri ?? "";
        StringAssert.Contains(uri, "/Items/season-1/Images/Thumb");
        StringAssert.Contains(uri, "tag=season-thumb-tag");
    }

    [TestMethod]
    public void AttachThumbnailUris_EpisodeOnlyHasSeriesId_UsesSeriesFallbackImage()
    {
        using var client = CreateClient();
        var resolver = new ArtworkResolver(client);
        var item = new EmbyItem
        {
            Id = "episode-1",
            Type = "Episode",
            SeriesId = "series-1"
        };

        resolver.AttachThumbnailUris([item], "token-1");

        var uri = item.ThumbnailUri?.AbsoluteUri ?? "";
        StringAssert.Contains(uri, "/Items/series-1/Images/Thumb");
        StringAssert.Contains(uri, "api_key=token-1");
    }

    /// <summary>
    /// 图片解析测试只构造 URL，不发起网络请求；这里配置基础地址即可让 EmbyApiClient 生成绝对地址。
    /// </summary>
    private static EmbyApiClient CreateClient()
    {
        var client = new EmbyApiClient();
        client.Configure("http://emby.local:8096", "user-1", "token-1", "device-1");
        return client;
    }
}
