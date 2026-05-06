using Player.App.Models;
using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class PlaybackRequestFactoryTests
{
    [TestMethod]
    public void CreateManualRequest_ResumeEnabled_UsesUserDataPositionAndSelectedTracks()
    {
        var item = new EmbyItem
        {
            Id = "item-1",
            UserData = new EmbyUserData { PlaybackPositionTicks = 1234 }
        };
        var mediaSource = new MediaSource { Id = "source-1" };
        var playbackInfo = new PlaybackInfoResponse { PlaySessionId = "session-1" };
        var videoTrack = new MediaTrackOption { Index = 10, DisplayName = "video" };
        var audioTrack = new MediaTrackOption { Index = 11, DisplayName = "audio" };
        var subtitleTrack = new MediaTrackOption { Index = 12, DisplayName = "subtitle" };

        var request = PlaybackRequestFactory.CreateManualRequest(
            item,
            mediaSource,
            playbackInfo,
            resumeFromUserData: true,
            videoTrack,
            audioTrack,
            subtitleTrack);

        Assert.AreSame(item, request.Item);
        Assert.AreSame(mediaSource, request.MediaSource);
        Assert.AreEqual("session-1", request.PlaySessionId);
        Assert.AreEqual(1234, request.StartTicks);
        Assert.AreEqual(10, request.VideoStreamIndex);
        Assert.AreEqual(11, request.AudioStreamIndex);
        Assert.AreEqual(12, request.SubtitleStreamIndex);
        Assert.IsFalse(request.DisableSubtitles);
        Assert.IsTrue(request.UseSelectedMpvTracks);
    }

    [TestMethod]
    public void CreateManualRequest_SubtitleNone_DisablesSubtitlesWithoutSubtitleIndex()
    {
        var request = PlaybackRequestFactory.CreateManualRequest(
            new EmbyItem { Id = "item-1" },
            new MediaSource { Id = "source-1" },
            new PlaybackInfoResponse { PlaySessionId = "session-1" },
            resumeFromUserData: false,
            videoTrack: MediaTrackOption.Auto("视频自动"),
            audioTrack: null,
            subtitleTrack: MediaTrackOption.None("关闭字幕"));

        Assert.AreEqual(0, request.StartTicks);
        Assert.IsNull(request.VideoStreamIndex);
        Assert.IsNull(request.AudioStreamIndex);
        Assert.IsNull(request.SubtitleStreamIndex);
        Assert.IsTrue(request.DisableSubtitles);
    }

    [TestMethod]
    public void GetExplicitTrackIndex_AutoAndNone_ReturnNull()
    {
        Assert.IsNull(PlaybackRequestFactory.GetExplicitTrackIndex(MediaTrackOption.Auto("自动")));
        Assert.IsNull(PlaybackRequestFactory.GetExplicitTrackIndex(MediaTrackOption.None("关闭")));
        Assert.AreEqual(7, PlaybackRequestFactory.GetExplicitTrackIndex(new MediaTrackOption { Index = 7 }));
    }
}
