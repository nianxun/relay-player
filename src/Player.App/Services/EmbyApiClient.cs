using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Player.App.Models;

namespace Player.App.Services;

public sealed class EmbyApiClient : IDisposable
{
    public const string ClientName = "Relay Player";
    public const string ClientVersion = "0.1.0";

    private const string ItemFields = "BasicSyncInfo,BackdropImageTags,DateCreated,Genres,ImageTags,MediaSources,Overview,ParentBackdropImageTags,ParentBackdropItemId,ParentId,ParentPrimaryImageItemId,ParentPrimaryImageTag,ParentThumbImageItemId,ParentThumbImageTag,ParentThumbItemId,Path,PrimaryImageAspectRatio,ProductionYear,RunTimeTicks,SeriesId,SeasonId,SeriesPrimaryImageTag,SeriesThumbImageTag,UserData";

    private readonly HttpClient _httpClient = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private Uri? _serverBaseUri;
    private string _userId = string.Empty;
    private string _accessToken = string.Empty;
    private string _deviceId = string.Empty;

    /// <summary>
    /// 播放上报失败时触发；播放本身不应被上报接口阻塞，但调用方需要有诊断线索。
    /// </summary>
    public event EventHandler<PlaybackReportFailureEventArgs>? PlaybackReportFailed;

    /// <summary>
    /// 释放用于 Emby API 调用的共享 HTTP 客户端。
    /// </summary>
    public void Dispose() => _httpClient.Dispose();

    /// <summary>
    /// 使用 Emby 用户名/密码接口认证用户，并用返回的 token 配置客户端。
    /// </summary>
    /// <param name="serverUrl">Emby 基础地址，可以带或不带协议头。</param>
    /// <param name="username">用户在界面输入的 Emby 用户名。</param>
    /// <param name="password"><c>/Users/AuthenticateByName</c> 要求提交的明文密码。</param>
    /// <param name="deviceId">Emby 用于识别这个 Windows 客户端的稳定设备标识。</param>
    /// <param name="cancellationToken">取消 HTTP 请求，同时保持界面可响应。</param>
    /// <returns>认证成功的用户和应保存到服务器档案中的访问 token。</returns>
    /// <exception cref="EmbyApiException">Emby 返回非成功状态码时抛出。</exception>
    /// <exception cref="InvalidOperationException">Emby 成功响应里缺少可用用户 ID 或 token 时抛出。</exception>
    public async Task<AuthenticationResult> AuthenticateAsync(
        string serverUrl,
        string username,
        string password,
        string deviceId,
        CancellationToken cancellationToken)
    {
        Configure(serverUrl, userId: "", accessToken: "", deviceId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "Users/AuthenticateByName")
        {
            Content = JsonContent.Create(new
            {
                Username = username,
                Pw = password
            })
        };

        PrepareRequest(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<AuthenticationResult>(_jsonOptions, cancellationToken)
                     ?? throw new InvalidOperationException("Emby returned an empty authentication response.");

        if (string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.User?.Id))
        {
            throw new InvalidOperationException("Emby did not return an access token and user id.");
        }

        Configure(serverUrl, result.User.Id, result.AccessToken, deviceId);
        return result;
    }

    /// <summary>
    /// 调用 Emby 官方密码接口更新当前用户密码。
    /// </summary>
    /// <remarks>
    /// 官方文档要求使用 <c>POST /Users/{Id}/Password</c>，并且“Requires authentication as user”，
    /// 所以这里必须建立在当前服务器会话已经可用的前提下，不能像登录接口那样匿名调用。
    /// </remarks>
    public async Task UpdatePasswordAsync(
        string userId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"Users/{Uri.EscapeDataString(userId)}/Password")
        {
            Content = JsonContent.Create(new
            {
                Id = userId,
                NewPw = newPassword,
                ResetPassword = false
            })
        };

        PrepareRequest(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// 为后续请求保存服务器地址和 Emby 授权信息。
    /// </summary>
    /// <param name="serverUrl">Emby 基础地址；缺少协议头时默认补 <c>http://</c>。</param>
    /// <param name="userId">当前用户 ID；认证前可以为空。</param>
    /// <param name="accessToken">Emby 返回并已保存的访问 token；登录前可以为空。</param>
    /// <param name="deviceId">写入 Emby 授权头的稳定设备标识。</param>
    public void Configure(string serverUrl, string userId, string accessToken, string deviceId)
    {
        var normalized = NormalizeServerUrl(serverUrl);
        _serverBaseUri = new Uri(normalized, UriKind.Absolute);
        _userId = userId;
        _accessToken = accessToken;
        _deviceId = deviceId;
    }

    /// <summary>
    /// 使用 Emby 专用 Latest 接口返回用户首页的最新条目。
    /// </summary>
    /// <param name="userId">已认证的 Emby 用户 ID。</param>
    /// <param name="limit">请求的最大条目数，会被限制在适合首页列表的范围内。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    /// <returns>适合主浏览面板展示的扁平条目列表。</returns>
    public async Task<IReadOnlyList<EmbyItem>> GetLatestItemsAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["Limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["Fields"] = ItemFields,
            ["IncludeItemTypes"] = "Movie,Series,Episode",
            ["GroupItems"] = "false",
            ["EnableUserData"] = "true",
            ["ImageTypeLimit"] = "1"
        };

        return await GetItemsFromJsonAsync($"Users/{Uri.EscapeDataString(userId)}/Items/Latest", query, cancellationToken);
    }

    /// <summary>
    /// 为已认证用户查询文件夹、搜索结果或递归媒体库视图。
    /// </summary>
    /// <param name="userId">已认证的 Emby 用户 ID。</param>
    /// <param name="parentId">要打开的父文件夹、剧集或季 ID；可为空。</param>
    /// <param name="searchTerm">搜索文本；为空时加载普通媒体库视图。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    /// <returns>Emby 返回的可播放条目和容器列表。</returns>
    public async Task<IReadOnlyList<EmbyItem>> GetItemsAsync(
        string userId,
        string? parentId,
        string searchTerm,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["Recursive"] = string.IsNullOrWhiteSpace(parentId) ? "true" : "false",
            ["IncludeItemTypes"] = "Movie,Series,Episode,Season,Folder",
            ["Fields"] = ItemFields,
            ["SortBy"] = string.IsNullOrWhiteSpace(searchTerm) ? "DateCreated" : "SortName",
            ["SortOrder"] = string.IsNullOrWhiteSpace(searchTerm) ? "Descending" : "Ascending",
            ["Limit"] = "80",
            ["EnableUserData"] = "true",
            ["ImageTypeLimit"] = "1"
        };

        if (!string.IsNullOrWhiteSpace(parentId))
        {
            query["ParentId"] = parentId;
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query["SearchTerm"] = searchTerm.Trim();
        }

        var response = await GetFromJsonAsync<EmbyItemsResponse>($"Users/{Uri.EscapeDataString(userId)}/Items", query, cancellationToken);
        return response.Items;
    }

    /// <summary>
    /// 返回当前用户最近播放过的条目。
    /// </summary>
    /// <param name="userId">已认证的 Emby 用户 ID。</param>
    /// <param name="limit">请求的最大条目数，会被限制在适合首页列表的范围内。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    /// <returns>按 <c>DatePlayed</c> 倒序排列的已播放条目。</returns>
    public async Task<IReadOnlyList<EmbyItem>> GetRecentlyPlayedAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["Limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["Fields"] = ItemFields,
            ["Filters"] = "IsPlayed",
            ["SortBy"] = "DatePlayed",
            ["SortOrder"] = "Descending",
            ["Recursive"] = "true",
            ["EnableUserData"] = "true",
            ["GroupItems"] = "false",
            ["ImageTypeLimit"] = "1"
        };

        var response = await GetFromJsonAsync<EmbyItemsResponse>($"Users/{Uri.EscapeDataString(userId)}/Items", query, cancellationToken);
        return response.Items;
    }

    /// <summary>
    /// 返回可以从用户保存位置继续播放的条目。
    /// </summary>
    /// <param name="userId">已认证的 Emby 用户 ID。</param>
    /// <param name="limit">请求的最大条目数，会被限制在适合首页列表的范围内。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    /// <returns>按最近播放时间优先排序的可续播条目。</returns>
    public async Task<IReadOnlyList<EmbyItem>> GetContinueWatchingAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["Limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["Fields"] = ItemFields,
            ["SortBy"] = "DatePlayed",
            ["SortOrder"] = "Descending",
            ["Recursive"] = "true",
            ["EnableUserData"] = "true",
            ["Filters"] = "IsResumable",
            ["GroupItems"] = "false",
            ["ImageTypeLimit"] = "1"
        };

        var response = await GetFromJsonAsync<EmbyItemsResponse>($"Users/{Uri.EscapeDataString(userId)}/Items", query, cancellationToken);
        return response.Items;
    }

    /// <summary>
    /// 查询某个剧集下的季列表，用于播放器详情区的“季”选择器。
    /// </summary>
    /// <param name="seriesId">Emby Series 条目的 ID。</param>
    /// <param name="userId">已认证的 Emby 用户 ID，用于返回用户播放状态。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    /// <returns>按季号排序的 Season 条目。</returns>
    public async Task<IReadOnlyList<EmbyItem>> GetSeasonsAsync(
        string seriesId,
        string userId,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["UserId"] = userId,
            ["Fields"] = ItemFields,
            ["EnableUserData"] = "true"
        };

        var response = await GetFromJsonAsync<EmbyItemsResponse>($"Shows/{Uri.EscapeDataString(seriesId)}/Seasons", query, cancellationToken);
        return response.Items
            .OrderBy(item => item.IndexNumber ?? int.MaxValue)
            .ThenBy(item => item.Name)
            .ToList();
    }

    /// <summary>
    /// 查询某个剧集或指定季下的集列表，用于在详情区直接切换集。
    /// </summary>
    /// <param name="seriesId">Emby Series 条目的 ID。</param>
    /// <param name="seasonId">可选的 Season 条目 ID；为空时返回整个剧集的集列表。</param>
    /// <param name="userId">已认证的 Emby 用户 ID，用于返回续播状态。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    /// <returns>按季号和集号排序的 Episode 条目。</returns>
    public async Task<IReadOnlyList<EmbyItem>> GetEpisodesAsync(
        string seriesId,
        string? seasonId,
        string userId,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["UserId"] = userId,
            ["SeasonId"] = seasonId,
            ["Fields"] = ItemFields,
            ["EnableUserData"] = "true"
        };

        var response = await GetFromJsonAsync<EmbyItemsResponse>($"Shows/{Uri.EscapeDataString(seriesId)}/Episodes", query, cancellationToken);
        return response.Items
            .OrderBy(item => item.ParentIndexNumber ?? int.MaxValue)
            .ThenBy(item => item.IndexNumber ?? int.MaxValue)
            .ThenBy(item => item.Name)
            .ToList();
    }

    /// <summary>
    /// 按 ID 读取单个媒体条目的完整展示字段，用于列表响应缺少图片继承信息时补齐详情。
    /// </summary>
    /// <param name="itemId">要补齐元数据的 Emby 条目 ID，可以是集、季或剧集。</param>
    /// <param name="userId">已认证的 Emby 用户 ID，用于返回用户相关字段和继承图信息。</param>
    /// <param name="cancellationToken">取消 HTTP 请求，避免快速切换条目时旧详情回写。</param>
    /// <returns>包含图片 tag 和父级继承字段的条目详情。</returns>
    public Task<EmbyItem> GetItemAsync(
        string itemId,
        string userId,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["Fields"] = ItemFields,
            ["EnableUserData"] = "true"
        };

        return GetFromJsonAsync<EmbyItem>($"Users/{Uri.EscapeDataString(userId)}/Items/{Uri.EscapeDataString(itemId)}", query, cancellationToken);
    }

    /// <summary>
    /// 请求某个条目的播放元数据和直连串流候选。
    /// </summary>
    /// <param name="itemId">要播放的 Emby 条目 ID。</param>
    /// <param name="userId">已认证的 Emby 用户 ID。</param>
    /// <param name="deviceId">Emby 播放会话使用的稳定客户端设备标识。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    /// <returns>可传给 <c>mpv.net</c> 的播放会话 ID 和媒体源。</returns>
    public async Task<PlaybackInfoResponse> GetPlaybackInfoAsync(
        string itemId,
        string userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["UserId"] = userId,
            ["StartTimeTicks"] = "0",
            ["IsPlayback"] = "true",
            ["AutoOpenLiveStream"] = "true",
            ["MaxStreamingBitrate"] = "140000000",
            ["DirectPlayProtocols"] = "File,Http",
            ["MediaSourceId"] = null
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri($"Items/{Uri.EscapeDataString(itemId)}/PlaybackInfo", query))
        {
            Content = JsonContent.Create(new
            {
                DeviceProfile = CreateMpvDeviceProfile(),
                UserId = userId,
                DeviceId = deviceId
            })
        };

        PrepareRequest(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<PlaybackInfoResponse>(_jsonOptions, cancellationToken)
               ?? new PlaybackInfoResponse();
    }

    /// <summary>
    /// 构建交给外部播放器的认证串流地址。
    /// </summary>
    /// <param name="itemId">要串流的 Emby 条目 ID。</param>
    /// <param name="mediaSource">播放信息返回并由用户选中的媒体源。</param>
    /// <param name="playSessionId">播放上报使用的 Emby 播放会话 ID。</param>
    /// <param name="userId">已认证的 Emby 用户 ID。</param>
    /// <param name="deviceId">Emby 播放会话使用的稳定客户端设备标识。</param>
    /// <param name="accessToken">追加为 <c>api_key</c> 的访问 token，因为 mpv.net 不能发送应用内请求头。</param>
    /// <param name="startPositionTicks">Emby ticks 表示的续播偏移。</param>
    /// <param name="videoStreamIndex">用户主动选择的视频流索引；为空时让 Emby/mpv 使用默认值。</param>
    /// <param name="audioStreamIndex">用户主动选择的音频流索引；为空时让 Emby/mpv 使用默认值。</param>
    /// <param name="subtitleStreamIndex">用户主动选择的字幕流索引；为空时让 Emby/mpv 使用默认值。</param>
    /// <param name="disableSubtitles">为 true 时通过 <c>SubtitleStreamIndex=-1</c> 主动关闭字幕。</param>
    /// <returns>mpv.net 可以直接打开的绝对 URL。</returns>
    public Uri BuildStreamUri(
        string itemId,
        MediaSource mediaSource,
        string playSessionId,
        string userId,
        string deviceId,
        string accessToken,
        long startPositionTicks,
        int? videoStreamIndex,
        int? audioStreamIndex,
        int? subtitleStreamIndex,
        bool disableSubtitles)
    {
        if (!string.IsNullOrWhiteSpace(mediaSource.DirectStreamUrl))
        {
            var directQuery = BuildStreamSelectionQuery(videoStreamIndex, audioStreamIndex, subtitleStreamIndex, disableSubtitles);
            return MakeAbsoluteUri(AppendQuery(AppendToken(mediaSource.DirectStreamUrl, accessToken), directQuery));
        }

        var container = NormalizeContainer(mediaSource.Container);
        var query = new Dictionary<string, string?>
        {
            ["MediaSourceId"] = mediaSource.Id,
            ["PlaySessionId"] = string.IsNullOrWhiteSpace(playSessionId) ? Guid.NewGuid().ToString("N") : playSessionId,
            ["UserId"] = userId,
            ["DeviceId"] = deviceId,
            ["api_key"] = accessToken,
            ["static"] = "true",
            ["VideoStreamIndex"] = videoStreamIndex?.ToString(),
            ["AudioStreamIndex"] = audioStreamIndex?.ToString(),
            ["SubtitleStreamIndex"] = disableSubtitles ? "-1" : subtitleStreamIndex?.ToString()
        };

        return MakeAbsoluteUri(BuildUri($"Videos/{Uri.EscapeDataString(itemId)}/stream.{container}", query));
    }

    /// <summary>
    /// 构建某个条目的主海报图片地址。
    /// </summary>
    /// <param name="itemId">需要加载主图片的条目 ID。</param>
    /// <param name="accessToken">图片请求中追加为 <c>api_key</c> 的访问 token。</param>
    /// <returns>适合 WPF 位图加载器使用的绝对图片 URL。</returns>
    public Uri BuildImageUri(string itemId, string accessToken)
    {
        return BuildImageUri(itemId, accessToken, "Primary", null, maxWidth: 420, maxHeight: null);
    }

    /// <summary>
    /// 构建指定图片类型的 URL，并按需带上 tag、尺寸限制和索引，避免请求 Emby 明确不存在的图片。
    /// </summary>
    /// <param name="itemId">图片所属条目 ID；继承图片时可以是父条目 ID。</param>
    /// <param name="accessToken">图片请求中追加为 <c>api_key</c> 的访问 token。</param>
    /// <param name="imageType">Emby 图片类型，例如 <c>Primary</c>、<c>Backdrop</c> 或 <c>Thumb</c>。</param>
    /// <param name="tag">Emby 返回的图片缓存 tag；为空时仍可构造 URL，但调用方应确保图片存在。</param>
    /// <param name="maxWidth">客户端希望的最大宽度。</param>
    /// <param name="maxHeight">客户端希望的最大高度。</param>
    /// <param name="index">多图类型的索引，例如第一张 Backdrop 为 0。</param>
    /// <returns>适合 WPF 图片控件加载的绝对 URL。</returns>
    public Uri BuildImageUri(
        string itemId,
        string accessToken,
        string imageType,
        string? tag,
        int? maxWidth,
        int? maxHeight,
        int? index = null)
    {
        var path = index is null
            ? $"Items/{Uri.EscapeDataString(itemId)}/Images/{Uri.EscapeDataString(imageType)}"
            : $"Items/{Uri.EscapeDataString(itemId)}/Images/{Uri.EscapeDataString(imageType)}/{index.Value}";
        var query = new Dictionary<string, string?>
        {
            ["maxWidth"] = maxWidth?.ToString(),
            ["maxHeight"] = maxHeight?.ToString(),
            ["tag"] = tag,
            ["quality"] = "85",
            ["api_key"] = accessToken
        };

        return MakeAbsoluteUri(BuildUri(path, query));
    }

    /// <summary>
    /// 构建某个条目的背景图地址，沿用 Emby 标准图片管线。
    /// </summary>
    /// <param name="itemId">需要加载第一张背景图的条目 ID。</param>
    /// <param name="accessToken">图片请求中追加为 <c>api_key</c> 的访问 token。</param>
    /// <returns>适合 WPF 位图加载器使用的绝对图片 URL。</returns>
    public Uri BuildBackdropUri(string itemId, string accessToken)
    {
        return BuildImageUri(itemId, accessToken, "Backdrop", null, maxWidth: 1200, maxHeight: null, index: 0);
    }

    /// <summary>
    /// 上报外部播放器已经开始播放。
    /// </summary>
    /// <param name="itemId">正在播放的 Emby 条目 ID。</param>
    /// <param name="playSessionId">播放信息返回或本次启动生成的播放会话 ID。</param>
    /// <param name="mediaSourceId">选中的媒体源 ID。</param>
    /// <param name="positionTicks">Emby ticks 表示的初始播放位置。</param>
    /// <param name="audioStreamIndex">用户选择的音频流索引。</param>
    /// <param name="subtitleStreamIndex">用户选择的字幕流索引；关闭字幕时传 -1。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    public Task ReportPlaybackStartAsync(
        string itemId,
        string playSessionId,
        string mediaSourceId,
        long positionTicks,
        int? audioStreamIndex,
        int? subtitleStreamIndex,
        CancellationToken cancellationToken)
    {
        return PostJsonIgnoreErrorsAsync(
            "Sessions/Playing",
            CreatePlaybackReport(itemId, playSessionId, mediaSourceId, positionTicks, audioStreamIndex, subtitleStreamIndex),
            "播放开始上报",
            cancellationToken);
    }

    /// <summary>
    /// 把 mpv.net 的周期性播放进度上报回 Emby。
    /// </summary>
    /// <param name="itemId">正在播放的 Emby 条目 ID。</param>
    /// <param name="playSessionId">本次启动使用的播放会话 ID。</param>
    /// <param name="mediaSourceId">选中的媒体源 ID。</param>
    /// <param name="positionTicks">Emby ticks 表示的当前播放位置。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    public Task ReportPlaybackProgressAsync(
        string itemId,
        string playSessionId,
        string mediaSourceId,
        long positionTicks,
        CancellationToken cancellationToken)
    {
        var body = CreatePlaybackReport(itemId, playSessionId, mediaSourceId, positionTicks, eventName: "TimeUpdate");
        return PostJsonIgnoreErrorsAsync("Sessions/Playing/Progress", body, "播放进度上报", cancellationToken);
    }

    /// <summary>
    /// 当 mpv.net 退出或 IPC 监听结束时，上报最终播放位置。
    /// </summary>
    /// <param name="itemId">正在播放的 Emby 条目 ID。</param>
    /// <param name="playSessionId">本次启动使用的播放会话 ID。</param>
    /// <param name="mediaSourceId">选中的媒体源 ID。</param>
    /// <param name="positionTicks">Emby ticks 表示的最终播放位置。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    public Task ReportPlaybackStoppedAsync(
        string itemId,
        string playSessionId,
        string mediaSourceId,
        long positionTicks,
        CancellationToken cancellationToken)
    {
        return PostJsonIgnoreErrorsAsync(
            "Sessions/Playing/Stopped",
            CreatePlaybackReport(itemId, playSessionId, mediaSourceId, positionTicks),
            "播放停止上报",
            cancellationToken);
    }

    /// <summary>
    /// 主动把条目标记为已播放，供界面按钮和集卡片状态使用。
    /// </summary>
    /// <param name="itemId">要标记的 Emby 条目 ID。</param>
    /// <param name="userId">已认证的 Emby 用户 ID。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    public Task MarkPlayedAsync(string itemId, string userId, CancellationToken cancellationToken)
    {
        var path = $"Users/{Uri.EscapeDataString(userId)}/PlayedItems/{Uri.EscapeDataString(itemId)}";
        return PostJsonAsync(path, cancellationToken);
    }

    /// <summary>
    /// 取消条目的“已播放”状态，和 MarkPlayedAsync 配对使用。
    /// </summary>
    /// <param name="itemId">要取消标记的 Emby 条目 ID。</param>
    /// <param name="userId">已认证的 Emby 用户 ID。</param>
    /// <param name="cancellationToken">取消 HTTP 请求。</param>
    public Task MarkUnplayedAsync(string itemId, string userId, CancellationToken cancellationToken)
    {
        var path = $"Users/{Uri.EscapeDataString(userId)}/PlayedItems/{Uri.EscapeDataString(itemId)}/Delete";
        return PostJsonAsync(path, cancellationToken);
    }

    private async Task<T> GetFromJsonAsync<T>(
        string path,
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, query));
        PrepareRequest(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
               ?? throw new InvalidOperationException("Emby returned an empty response.");
    }

    private async Task<IReadOnlyList<EmbyItem>> GetItemsFromJsonAsync(
        string path,
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, query));
        PrepareRequest(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return DeserializeItems(root);
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("Items", out var itemsElement) &&
            itemsElement.ValueKind == JsonValueKind.Array)
        {
            return DeserializeItems(itemsElement);
        }

        throw new InvalidOperationException("Emby returned an unexpected item list response.");
    }

    private async Task PostJsonIgnoreErrorsAsync(
        string path,
        object body,
        string reportName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body)
            };

            PrepareRequest(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 播放上报有助于同步服务器状态，但不应该阻塞 mpv.net 启动。
            PlaybackReportFailed?.Invoke(this, new PlaybackReportFailureEventArgs(reportName, ex));
        }
    }

    /// <summary>
    /// 发送需要明确成功/失败反馈的 JSON 请求，用于用户主动触发的状态修改。
    /// </summary>
    private async Task PostJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { })
        };

        PrepareRequest(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// 把相对地址转成绝对地址，并把当前认证信息写入本次请求。
    /// </summary>
    /// <remarks>
    /// <see cref="HttpClient.BaseAddress"/> 和 <see cref="HttpClient.DefaultRequestHeaders"/>
    /// 在客户端发出请求后不能再随意修改；登录流程会先无 token 认证再带 token 访问，
    /// 因此必须把这些差异放在每个 <see cref="HttpRequestMessage"/> 上。
    /// </remarks>
    private void PrepareRequest(HttpRequestMessage request)
    {
        request.RequestUri = MakeAbsoluteUri(request.RequestUri?.ToString() ?? string.Empty);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(
            "X-Emby-Authorization",
            BuildAuthorizationHeader(_userId, _deviceId));

        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            request.Headers.TryAddWithoutValidation("X-Emby-Token", _accessToken);
        }
    }

    private string BuildUri(string path, IReadOnlyDictionary<string, string?> query)
    {
        var parts = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");

        var queryString = string.Join("&", parts);
        return queryString.Length == 0 ? path : $"{path}?{queryString}";
    }

    private Uri MakeAbsoluteUri(string pathOrUri)
    {
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (_serverBaseUri is null)
        {
            throw new InvalidOperationException("Emby server URL has not been configured.");
        }

        if (pathOrUri.StartsWith("/", StringComparison.Ordinal))
        {
            return new Uri(_serverBaseUri.GetLeftPart(UriPartial.Authority) + pathOrUri);
        }

        return new Uri(_serverBaseUri, pathOrUri);
    }

    private string AppendToken(string url, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || url.Contains("api_key=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return $"{url}{(url.Contains('?') ? '&' : '?')}api_key={Uri.EscapeDataString(accessToken)}";
    }

    /// <summary>
    /// 给 Emby 直连地址追加用户选择的轨道参数，避免 DirectStreamUrl 分支绕过界面选择。
    /// </summary>
    private string AppendQuery(string url, IReadOnlyDictionary<string, string?> query)
    {
        var queryString = string.Join(
            "&",
            query
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

        if (string.IsNullOrWhiteSpace(queryString))
        {
            return url;
        }

        return $"{url}{(url.Contains('?') ? '&' : '?')}{queryString}";
    }

    /// <summary>
    /// 构造 Emby 串流接口认识的音视频/字幕流选择参数。
    /// </summary>
    private static IReadOnlyDictionary<string, string?> BuildStreamSelectionQuery(
        int? videoStreamIndex,
        int? audioStreamIndex,
        int? subtitleStreamIndex,
        bool disableSubtitles)
    {
        return new Dictionary<string, string?>
        {
            ["VideoStreamIndex"] = videoStreamIndex?.ToString(),
            ["AudioStreamIndex"] = audioStreamIndex?.ToString(),
            ["SubtitleStreamIndex"] = disableSubtitles ? "-1" : subtitleStreamIndex?.ToString()
        };
    }

    private static string NormalizeServerUrl(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new ArgumentException("Server URL is required.", nameof(serverUrl));
        }

        var normalized = serverUrl.Trim();
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "http://" + normalized;
        }

        return normalized.TrimEnd('/') + "/";
    }

    private static string NormalizeContainer(string? container)
    {
        if (string.IsNullOrWhiteSpace(container))
        {
            return "mkv";
        }

        var first = container.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(first) ? "mkv" : first.ToLowerInvariant();
    }

    private static string BuildAuthorizationHeader(string userId, string deviceId)
    {
        var userPart = string.IsNullOrWhiteSpace(userId) ? "" : $"UserId=\"{userId}\", ";
        return $"MediaBrowser {userPart}Client=\"{ClientName}\", Device=\"Windows\", DeviceId=\"{deviceId}\", Version=\"{ClientVersion}\"";
    }

    private static object CreateMpvDeviceProfile()
    {
        return new
        {
            Name = "mpv.net",
            MaxStreamingBitrate = 140000000,
            MaxStaticBitrate = 140000000,
            MusicStreamingTranscodingBitrate = 384000,
            DirectPlayProfiles = new[]
            {
                new { Type = "Video", Container = "mkv,mp4,m4v,mov,avi,wmv,ts,m2ts,webm,flv,mpg,mpeg" },
                new { Type = "Audio", Container = "mp3,flac,aac,m4a,ogg,wav,opus" }
            },
            TranscodingProfiles = new[]
            {
                new { Type = "Video", Container = "m3u8", Protocol = "hls", VideoCodec = "h264,hevc", AudioCodec = "aac,mp3,ac3,eac3", Context = "Streaming" }
            },
            ResponseProfiles = Array.Empty<object>(),
            ContainerProfiles = Array.Empty<object>(),
            CodecProfiles = Array.Empty<object>(),
            SubtitleProfiles = new[]
            {
                new { Format = "srt", Method = "External" },
                new { Format = "ass", Method = "External" },
                new { Format = "ssa", Method = "External" },
                new { Format = "sub", Method = "External" },
                new { Format = "vtt", Method = "External" }
            }
        };
    }

    private static object CreatePlaybackReport(
        string itemId,
        string playSessionId,
        string mediaSourceId,
        long positionTicks,
        int? audioStreamIndex = null,
        int? subtitleStreamIndex = null,
        string? eventName = null)
    {
        var report = new Dictionary<string, object?>
        {
            ["QueueableMediaTypes"] = new[] { "Video" },
            ["CanSeek"] = true,
            ["ItemId"] = itemId,
            ["MediaSourceId"] = mediaSourceId,
            ["IsPaused"] = false,
            ["IsMuted"] = false,
            ["PositionTicks"] = Math.Max(0, positionTicks),
            ["PlayMethod"] = "DirectStream",
            ["PlaySessionId"] = playSessionId,
            ["LiveStreamId"] = "",
            ["PlaylistIndex"] = 0,
            ["PlaylistLength"] = 1,
            ["SubtitleOffset"] = 0.0,
            ["PlaybackRate"] = 1.0
        };

        if (audioStreamIndex is not null)
        {
            report["AudioStreamIndex"] = audioStreamIndex;
        }

        if (subtitleStreamIndex is not null)
        {
            report["SubtitleStreamIndex"] = subtitleStreamIndex;
        }

        if (!string.IsNullOrWhiteSpace(eventName))
        {
            report["EventName"] = eventName;
        }

        return report;
    }

    private List<EmbyItem> DeserializeItems(JsonElement itemsElement)
    {
        return itemsElement.Deserialize<List<EmbyItem>>(_jsonOptions) ?? [];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new EmbyApiException(response.StatusCode, response.RequestMessage?.RequestUri, body, response.ReasonPhrase);
    }
}

/// <summary>
/// 描述一次播放上报失败，供界面状态栏或后续日志系统展示可追踪原因。
/// </summary>
public sealed class PlaybackReportFailureEventArgs : EventArgs
{
    public PlaybackReportFailureEventArgs(string reportName, Exception exception)
    {
        ReportName = reportName;
        Exception = exception;
    }

    public string ReportName { get; }

    public Exception Exception { get; }
}

/// <summary>
/// 表示带状态码、请求地址和响应正文的 Emby HTTP 失败，便于展示更清晰的诊断信息。
/// </summary>
public sealed class EmbyApiException : Exception
{
    public EmbyApiException(
        System.Net.HttpStatusCode statusCode,
        Uri? requestUri,
        string? responseBody,
        string? reasonPhrase)
        : base(BuildMessage(statusCode, reasonPhrase, responseBody))
    {
        StatusCode = statusCode;
        RequestUri = requestUri;
        ResponseBody = responseBody ?? "";
    }

    public System.Net.HttpStatusCode StatusCode { get; }

    public Uri? RequestUri { get; }

    public string ResponseBody { get; }

    private static string BuildMessage(System.Net.HttpStatusCode statusCode, string? reasonPhrase, string? responseBody)
    {
        var message = $"Emby request failed: {(int)statusCode} {reasonPhrase ?? "Unknown"}";
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            message += $" - {responseBody.Trim()}";
        }

        return message;
    }
}
