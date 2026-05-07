using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class MpvIpcClientTests
{
    [TestMethod]
    public void BuildLoadFileCommand_ModernStyle_ZeroInitialPosition_OverridesPreviousStartOption()
    {
        var command = MpvIpcClient.BuildLoadFileCommand(
            new Uri("http://127.0.0.1/video.mkv"),
            TimeSpan.Zero,
            supportsInsertIndex: true);

        CollectionAssert.AreEqual(
            new object[] { "loadfile", "http://127.0.0.1/video.mkv", "replace", -1, "start=0" },
            command);
    }

    [TestMethod]
    public void BuildLoadFileCommand_LegacyStyle_ZeroInitialPosition_OmitsInsertIndex()
    {
        var command = MpvIpcClient.BuildLoadFileCommand(
            new Uri("http://127.0.0.1/video.mkv"),
            TimeSpan.Zero,
            supportsInsertIndex: false);

        CollectionAssert.AreEqual(
            new object[] { "loadfile", "http://127.0.0.1/video.mkv", "replace", "start=0" },
            command);
    }

    [TestMethod]
    public void BuildLoadFileCommand_ModernStyle_PositiveInitialPosition_UsesInvariantSecondValue()
    {
        var command = MpvIpcClient.BuildLoadFileCommand(
            new Uri("http://127.0.0.1/video.mkv"),
            TimeSpan.FromMilliseconds(12345),
            supportsInsertIndex: true);

        Assert.AreEqual("start=12.345", command[4]);
    }

    [TestMethod]
    public void SupportsLoadFileInsertIndex_Mpv037_ReturnsFalse()
    {
        Assert.IsFalse(MpvIpcClient.SupportsLoadFileInsertIndex("mpv 0.37.0-152-gbd5d8e41"));
    }

    [TestMethod]
    public void SupportsLoadFileInsertIndex_Mpv038_ReturnsTrue()
    {
        Assert.IsTrue(MpvIpcClient.SupportsLoadFileInsertIndex("mpv 0.38.0"));
    }
}
