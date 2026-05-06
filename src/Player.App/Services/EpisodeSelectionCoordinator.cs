using Player.App.Models;

namespace Player.App.Services;

/// <summary>
/// 提供选季/选集所需的纯逻辑，避免 UI 代码里散落季号、集号和续播优先级判断。
/// </summary>
public sealed class EpisodeSelectionCoordinator
{
    /// <summary>
    /// 从剧集或剧集条目中解析所属 Series ID。
    /// </summary>
    public string? ResolveSeriesId(EmbyItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SeriesId))
        {
            return item.SeriesId;
        }

        return EmbyItemKind.IsSeries(item) ? item.Id : null;
    }

    /// <summary>
    /// 解析某集所属季 ID；Emby 有时放在 SeasonId，有时只能从 ParentId 兜底。
    /// </summary>
    public string? ResolveSeasonForEpisode(EmbyItem episode)
    {
        if (!string.IsNullOrWhiteSpace(episode.SeasonId))
        {
            return episode.SeasonId;
        }

        return !string.IsNullOrWhiteSpace(episode.ParentId) ? episode.ParentId : null;
    }

    /// <summary>
    /// 从当前集和季列表中选择应该显示的季，优先精确 ID，其次匹配季号。
    /// </summary>
    public EmbyItem? SelectSeasonForEpisode(EmbyItem episode, IReadOnlyList<EmbyItem> seasons)
    {
        if (seasons.Count == 0)
        {
            return null;
        }

        var selectedSeason = ResolveSeasonForEpisode(episode) is { } seasonId
            ? seasons.FirstOrDefault(season => string.Equals(season.Id, seasonId, StringComparison.Ordinal))
            : null;

        selectedSeason ??= episode.ParentIndexNumber is { } seasonIndex
            ? seasons.FirstOrDefault(season => season.IndexNumber == seasonIndex)
            : null;

        return selectedSeason ?? seasons[0];
    }

    /// <summary>
    /// 打开剧集根节点时选择初始季；优先选择真正的 Season 条目，异常数据下回退第一项。
    /// </summary>
    public EmbyItem? SelectInitialSeason(IReadOnlyList<EmbyItem> seasons)
    {
        if (seasons.Count == 0)
        {
            return null;
        }

        return seasons.FirstOrDefault(EmbyItemKind.IsSeason) ?? seasons[0];
    }

    /// <summary>
    /// 季切换后选择默认集：当前集优先，其次续播集，最后第一集。
    /// </summary>
    public EmbyItem? SelectEpisodeForSeason(EmbyItem? current, IReadOnlyList<EmbyItem> episodes)
    {
        if (episodes.Count == 0)
        {
            return null;
        }

        return episodes.FirstOrDefault(episode =>
                   current is not null &&
                   EmbyItemKind.IsEpisode(current) &&
                   string.Equals(episode.Id, current.Id, StringComparison.Ordinal)) ??
               episodes.FirstOrDefault(item => item.UserData?.PlaybackPositionTicks is { } position && position > 0) ??
               episodes[0];
    }

    /// <summary>
    /// 从整部剧的集列表里选择当前集之后的下一集；先按季号/集号比较，缺少编号时再用列表位置兜底。
    /// </summary>
    /// <remarks>
    /// Emby 的集列表可能跨季返回，也可能存在缺少季号或集号的特殊条目。
    /// 这里把连播顺序集中在服务里，避免主窗口和后续播放逻辑各自实现一套排序规则。
    /// </remarks>
    public EmbyItem? SelectNextEpisode(EmbyItem currentEpisode, IReadOnlyList<EmbyItem> episodes)
    {
        var orderedEpisodes = episodes
            .Where(EmbyItemKind.IsEpisode)
            .OrderBy(item => item.ParentIndexNumber ?? int.MaxValue)
            .ThenBy(item => item.IndexNumber ?? int.MaxValue)
            .ThenBy(item => item.Name)
            .ToList();

        var currentFromSeries = orderedEpisodes.FirstOrDefault(item => string.Equals(item.Id, currentEpisode.Id, StringComparison.Ordinal));
        var currentSeasonNumber = currentFromSeries?.ParentIndexNumber ?? currentEpisode.ParentIndexNumber;
        var currentEpisodeNumber = currentFromSeries?.IndexNumber ?? currentEpisode.IndexNumber;

        if (currentSeasonNumber is not null || currentEpisodeNumber is not null)
        {
            var nextByNumber = orderedEpisodes.FirstOrDefault(candidate =>
                !string.Equals(candidate.Id, currentEpisode.Id, StringComparison.Ordinal) &&
                IsAfterEpisode(candidate, currentSeasonNumber, currentEpisodeNumber));

            if (nextByNumber is not null)
            {
                return nextByNumber;
            }
        }

        if (currentFromSeries is null)
        {
            return null;
        }

        var currentIndex = orderedEpisodes.IndexOf(currentFromSeries);
        return currentIndex >= 0 && currentIndex + 1 < orderedEpisodes.Count
            ? orderedEpisodes[currentIndex + 1]
            : null;
    }

    /// <summary>
    /// 判断当前条目是否仍属于正在加载的剧集上下文，避免旧请求回写新页面。
    /// </summary>
    public bool IsSameSeriesContext(EmbyItem item, string seriesId)
    {
        return EmbyItemKind.IsSeries(item) && string.Equals(item.Id, seriesId, StringComparison.Ordinal) ||
               string.Equals(item.SeriesId, seriesId, StringComparison.Ordinal);
    }

    /// <summary>
    /// 选季加载集列表时解析剧集 ID；已激活的剧集上下文优先，其次从当前选中条目反查。
    /// </summary>
    public string? ResolveSeriesIdForEpisodeLoad(string? activeSeriesId, EmbyItem? current)
    {
        if (!string.IsNullOrWhiteSpace(activeSeriesId))
        {
            return activeSeriesId;
        }

        return current is null ? null : ResolveSeriesId(current);
    }

    /// <summary>
    /// 生成 Emby Episodes 接口需要的 SeasonId；非季条目不能作为季过滤条件。
    /// </summary>
    public string? ResolveSeasonRequestId(EmbyItem season)
    {
        return EmbyItemKind.IsSeason(season) ? season.Id : null;
    }

    /// <summary>
    /// 在已加载集列表中按 ID 找到当前详情对应的那一集，用于同步下拉框选中项。
    /// </summary>
    public EmbyItem? FindEpisodeById(IReadOnlyList<EmbyItem> episodes, EmbyItem episode)
    {
        return episodes.FirstOrDefault(item => string.Equals(item.Id, episode.Id, StringComparison.Ordinal));
    }

    /// <summary>
    /// 判断候选集是否在当前集之后；季号优先，季号相同时比较集号。
    /// </summary>
    private static bool IsAfterEpisode(EmbyItem candidate, int? currentSeasonNumber, int? currentEpisodeNumber)
    {
        var candidateSeasonNumber = candidate.ParentIndexNumber;
        var candidateEpisodeNumber = candidate.IndexNumber;

        if (currentSeasonNumber is not null && candidateSeasonNumber is not null &&
            candidateSeasonNumber.Value != currentSeasonNumber.Value)
        {
            return candidateSeasonNumber.Value > currentSeasonNumber.Value;
        }

        if (currentEpisodeNumber is not null && candidateEpisodeNumber is not null)
        {
            return candidateEpisodeNumber.Value > currentEpisodeNumber.Value;
        }

        return false;
    }
}
