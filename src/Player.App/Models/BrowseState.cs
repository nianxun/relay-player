namespace Player.App.Models;

/// <summary>
/// 描述主浏览区域的当前页面状态；窗口用它统一处理返回栈、标题和列表加载参数。
/// </summary>
internal sealed record BrowseState(
    BrowseViewKind Kind,
    string Title,
    string? ParentId = null,
    string? SearchTerm = null,
    string? SelectedItemId = null);

/// <summary>
/// 主窗口支持的浏览页面类型；详情页是从列表进入的临时状态，不会直接重新加载列表。
/// </summary>
internal enum BrowseViewKind
{
    Library,
    Search,
    Resume,
    Detail
}
