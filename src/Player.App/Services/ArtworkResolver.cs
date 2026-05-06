using Player.App.Models;

namespace Player.App.Services;

/// <summary>
/// 负责把 Emby 条目的图片字段解析成可请求的图片 URL。
/// </summary>
public sealed class ArtworkResolver
{
    private readonly EmbyApiClient _embyClient;

    public ArtworkResolver(EmbyApiClient embyClient)
    {
        _embyClient = embyClient;
    }

    /// <summary>
    /// 为列表卡片批量补齐缩略图地址，避免 XAML 中拼接带 token 的 Emby 图片 URL。
    /// </summary>
    public void AttachThumbnailUris(IEnumerable<EmbyItem> items, string accessToken)
    {
        foreach (var item in items)
        {
            item.ThumbnailUri = ResolveThumbnailUri(item, accessToken);
        }
    }

    /// <summary>
    /// 生成详情大图候选地址；列表卡片已经能显示的缩略图优先复用，避免同一集卡片有图但详情无图。
    /// </summary>
    public async Task<IReadOnlyList<Uri>> ResolvePosterCandidatesAsync(
        EmbyItem item,
        string userId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Uri>();
        if (item.ThumbnailUri is not null)
        {
            candidates.Add(item.ThumbnailUri);
        }

        var artworkItem = await ResolveArtworkItemAsync(item, userId, cancellationToken);
        candidates.AddRange(ResolveArtworkUris(artworkItem, accessToken));
        return candidates
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// 横向卡片优先使用 Thumb 或 Primary；如果是剧集条目，则退回剧集/季继承的背景图。
    /// </summary>
    private Uri? ResolveThumbnailUri(EmbyItem item, string accessToken)
    {
        return EnumerateImageCandidates(item, accessToken, maxWidth: 430, maxHeight: 242).FirstOrDefault();
    }

    /// <summary>
    /// 当列表条目没有携带图片 tag 时，回源读取单条目详情以补齐当前集的继承预览图。
    /// </summary>
    private async Task<EmbyItem> ResolveArtworkItemAsync(
        EmbyItem item,
        string userId,
        CancellationToken cancellationToken)
    {
        if (HasUsableArtwork(item) ||
            string.IsNullOrWhiteSpace(item.Id) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return item;
        }

        try
        {
            return await _embyClient.GetItemAsync(item.Id, userId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 图片补全失败不影响播放；继续使用列表已有字段尝试兜底 URL。
            return item;
        }
    }

    /// <summary>
    /// 同步解析一组图片 URL，不发起网络请求；真正下载由调用方完成。
    /// </summary>
    private IEnumerable<Uri> ResolveArtworkUris(EmbyItem item, string accessToken)
    {
        foreach (var uri in EnumerateImageCandidates(item, accessToken, maxWidth: 1100, maxHeight: null))
        {
            yield return uri;
        }

        // 最后的无 tag 兜底用于兼容部分服务端未返回 ImageTags 但实际能提供主图的情况。
        yield return _embyClient.BuildImageUri(item.Id, accessToken);
    }

    /// <summary>
    /// 按成熟客户端常见优先级生成图片候选，优先宽幅图，再回退主图和父级图。
    /// </summary>
    private IEnumerable<Uri> EnumerateImageCandidates(
        EmbyItem item,
        string accessToken,
        int maxWidth,
        int? maxHeight)
    {
        if (TryGetImageTag(item.ImageTags, "Thumb", out var thumbTag))
        {
            yield return _embyClient.BuildImageUri(item.Id, accessToken, "Thumb", thumbTag, maxWidth, maxHeight);
        }

        if (item.BackdropImageTags is { Count: > 0 } backdropTags)
        {
            yield return _embyClient.BuildImageUri(item.Id, accessToken, "Backdrop", backdropTags[0], maxWidth, maxHeight, index: 0);
        }

        if (TryGetImageTag(item.ImageTags, "Primary", out var primaryTag))
        {
            yield return _embyClient.BuildImageUri(item.Id, accessToken, "Primary", primaryTag, maxWidth, maxHeight);
        }

        // 剧集和季条目经常没有自己的封面，真正稳定可用的图片往往挂在父级或系列级条目上。
        var parentThumbItemId = string.IsNullOrWhiteSpace(item.ParentThumbImageItemId)
            ? item.ParentThumbItemId
            : item.ParentThumbImageItemId;

        if (!string.IsNullOrWhiteSpace(parentThumbItemId) &&
            !string.IsNullOrWhiteSpace(item.ParentThumbImageTag))
        {
            yield return _embyClient.BuildImageUri(parentThumbItemId, accessToken, "Thumb", item.ParentThumbImageTag, maxWidth, maxHeight);
        }

        if (!string.IsNullOrWhiteSpace(item.ParentBackdropItemId) &&
            item.ParentBackdropImageTags is { Count: > 0 } parentBackdropTags)
        {
            yield return _embyClient.BuildImageUri(item.ParentBackdropItemId, accessToken, "Backdrop", parentBackdropTags[0], maxWidth, maxHeight, index: 0);
        }

        if (!string.IsNullOrWhiteSpace(item.ParentPrimaryImageItemId) &&
            !string.IsNullOrWhiteSpace(item.ParentPrimaryImageTag))
        {
            yield return _embyClient.BuildImageUri(item.ParentPrimaryImageItemId, accessToken, "Primary", item.ParentPrimaryImageTag, maxWidth, maxHeight);
        }

        if (!string.IsNullOrWhiteSpace(item.SeasonId))
        {
            yield return _embyClient.BuildImageUri(item.SeasonId, accessToken, "Thumb", null, maxWidth, maxHeight);
            yield return _embyClient.BuildImageUri(item.SeasonId, accessToken, "Primary", null, maxWidth, maxHeight);
        }

        if (!string.IsNullOrWhiteSpace(item.SeriesId))
        {
            yield return _embyClient.BuildImageUri(item.SeriesId, accessToken, "Thumb", null, maxWidth, maxHeight);
            yield return _embyClient.BuildImageUri(item.SeriesId, accessToken, "Primary", null, maxWidth, maxHeight);
        }

        if (TryResolveInheritedImage(item.SeriesId, item.SeriesThumbImageTag, accessToken, "Thumb", maxWidth, maxHeight, out var seriesThumbUri))
        {
            yield return seriesThumbUri;
        }

        if (TryResolveInheritedImage(item.SeriesId, item.SeriesPrimaryImageTag, accessToken, "Primary", maxWidth, maxHeight, out var seriesPrimaryUri))
        {
            yield return seriesPrimaryUri;
        }
    }

    /// <summary>
    /// 尝试构造继承自父级或系列级条目的图片地址；只在已有 tag 时返回，避免发出明显无效的请求。
    /// </summary>
    private bool TryResolveInheritedImage(
        string? itemId,
        string? tag,
        string accessToken,
        string imageType,
        int maxWidth,
        int? maxHeight,
        out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        uri = _embyClient.BuildImageUri(itemId, accessToken, imageType, tag, maxWidth, maxHeight);
        return true;
    }

    /// <summary>
    /// 判断条目是否已有足够图片线索；没有线索时再回源补齐，避免每次选择都多发一个详情请求。
    /// </summary>
    private static bool HasUsableArtwork(EmbyItem item)
    {
        return HasImageTag(item.ImageTags, "Thumb") ||
               HasImageTag(item.ImageTags, "Primary") ||
               item.BackdropImageTags is { Count: > 0 } ||
               !string.IsNullOrWhiteSpace(item.ParentThumbImageTag) ||
               !string.IsNullOrWhiteSpace(item.ParentPrimaryImageTag) ||
               item.ParentBackdropImageTags is { Count: > 0 } ||
               !string.IsNullOrWhiteSpace(item.SeriesThumbImageTag) ||
               !string.IsNullOrWhiteSpace(item.SeriesPrimaryImageTag);
    }

    /// <summary>
    /// 快速判断某个图片类型是否存在 tag，供回源策略避免重复遍历。
    /// </summary>
    private static bool HasImageTag(Dictionary<string, string>? imageTags, string imageType)
    {
        return TryGetImageTag(imageTags, imageType, out _);
    }

    /// <summary>
    /// Emby 的 ImageTags 是区分图片类型的字典；忽略大小写能兼容不同服务端序列化习惯。
    /// </summary>
    private static bool TryGetImageTag(Dictionary<string, string>? imageTags, string imageType, out string tag)
    {
        tag = string.Empty;
        if (imageTags is null)
        {
            return false;
        }

        foreach (var pair in imageTags)
        {
            if (pair.Key.Equals(imageType, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                tag = pair.Value;
                return true;
            }
        }

        return false;
    }
}
