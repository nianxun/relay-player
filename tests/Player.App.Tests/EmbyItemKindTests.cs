using Player.App.Models;
using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class EmbyItemKindTests
{
    [TestMethod]
    public void IsContainer_FolderSeriesSeason_ReturnsTrue()
    {
        Assert.IsTrue(EmbyItemKind.IsContainer(new EmbyItem { Type = "Folder" }));
        Assert.IsTrue(EmbyItemKind.IsContainer(new EmbyItem { Type = "Series" }));
        Assert.IsTrue(EmbyItemKind.IsContainer(new EmbyItem { Type = "Season" }));
    }

    [TestMethod]
    public void IsContainer_Episode_ReturnsFalse()
    {
        Assert.IsFalse(EmbyItemKind.IsContainer(new EmbyItem { Type = "Episode" }));
    }

    [TestMethod]
    public void SpecificTypeChecks_IgnoreCase()
    {
        Assert.IsTrue(EmbyItemKind.IsSeries(new EmbyItem { Type = "series" }));
        Assert.IsTrue(EmbyItemKind.IsSeason(new EmbyItem { Type = "season" }));
        Assert.IsTrue(EmbyItemKind.IsEpisode(new EmbyItem { Type = "episode" }));
    }
}
