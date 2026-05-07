using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class MpvIpcClientTests
{
    [TestMethod]
    public void BuildLoadFileCommand_ZeroInitialPosition_OverridesPreviousStartOption()
    {
        var command = MpvIpcClient.BuildLoadFileCommand(
            new Uri("http://127.0.0.1/video.mkv"),
            TimeSpan.Zero);

        CollectionAssert.AreEqual(
            new object[] { "loadfile", "http://127.0.0.1/video.mkv", "replace", -1, "start=0" },
            command);
    }

    [TestMethod]
    public void BuildLoadFileCommand_PositiveInitialPosition_UsesInvariantSecondValue()
    {
        var command = MpvIpcClient.BuildLoadFileCommand(
            new Uri("http://127.0.0.1/video.mkv"),
            TimeSpan.FromMilliseconds(12345));

        Assert.AreEqual("start=12.345", command[4]);
    }
}
