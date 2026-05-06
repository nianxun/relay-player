using Player.App.Models;

namespace Player.App.Services;

/// <summary>
/// 集中判断 Emby 条目类型，避免窗口、导航和播放逻辑各自用字符串比较。
/// </summary>
public static class EmbyItemKind
{
    /// <summary>
    /// 判断条目是否是只能继续进入的容器，而不是可直接交给 mpv.net 播放的媒体。
    /// </summary>
    public static bool IsContainer(EmbyItem item)
    {
        return item.Type.Equals("Folder", StringComparison.OrdinalIgnoreCase) ||
               item.Type.Equals("Series", StringComparison.OrdinalIgnoreCase) ||
               item.Type.Equals("Season", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断条目是否是剧集根节点。
    /// </summary>
    public static bool IsSeries(EmbyItem item)
    {
        return item.Type.Equals("Series", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断条目是否是季节点。
    /// </summary>
    public static bool IsSeason(EmbyItem item)
    {
        return item.Type.Equals("Season", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断条目是否是具体集，可直接播放并参与连播。
    /// </summary>
    public static bool IsEpisode(EmbyItem item)
    {
        return item.Type.Equals("Episode", StringComparison.OrdinalIgnoreCase);
    }
}
