using Player.App.Models;
using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class EpisodeSelectionCoordinatorTests
{
    [TestMethod]
    public void SelectSeasonForEpisode_PrefersSeasonId()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var episode = new EmbyItem
        {
            Type = "Episode",
            SeasonId = "season-2",
            ParentIndexNumber = 1
        };
        var seasons = new[]
        {
            new EmbyItem { Id = "season-1", Type = "Season", IndexNumber = 1 },
            new EmbyItem { Id = "season-2", Type = "Season", IndexNumber = 2 }
        };

        var selected = coordinator.SelectSeasonForEpisode(episode, seasons);

        Assert.AreSame(seasons[1], selected);
    }

    [TestMethod]
    public void SelectEpisodeForSeason_PrefersCurrentEpisodeThenResumeEpisode()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var current = new EmbyItem { Id = "episode-2", Type = "Episode" };
        var episodes = new[]
        {
            new EmbyItem { Id = "episode-1", Type = "Episode" },
            new EmbyItem
            {
                Id = "episode-2",
                Type = "Episode",
                UserData = new EmbyUserData { PlaybackPositionTicks = 100 }
            }
        };

        var selected = coordinator.SelectEpisodeForSeason(current, episodes);

        Assert.AreSame(episodes[1], selected);
    }

    [TestMethod]
    public void SelectEpisodeForSeason_UsesResumeEpisodeWhenCurrentIsMissing()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var episodes = new[]
        {
            new EmbyItem { Id = "episode-1", Type = "Episode" },
            new EmbyItem
            {
                Id = "episode-2",
                Type = "Episode",
                UserData = new EmbyUserData { PlaybackPositionTicks = 100 }
            }
        };

        var selected = coordinator.SelectEpisodeForSeason(null, episodes);

        Assert.AreSame(episodes[1], selected);
    }

    [TestMethod]
    public void SelectInitialSeason_PrefersSeasonItem()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var fallback = new EmbyItem { Id = "folder-1", Type = "Folder" };
        var season = new EmbyItem { Id = "season-1", Type = "Season" };

        var selected = coordinator.SelectInitialSeason([fallback, season]);

        Assert.AreSame(season, selected);
    }

    [TestMethod]
    public void ResolveSeriesIdForEpisodeLoad_ActiveSeriesTakesPriority()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var current = new EmbyItem { Type = "Episode", SeriesId = "series-from-current" };

        var seriesId = coordinator.ResolveSeriesIdForEpisodeLoad("active-series", current);

        Assert.AreEqual("active-series", seriesId);
    }

    [TestMethod]
    public void ResolveSeasonRequestId_NonSeason_ReturnsNull()
    {
        var coordinator = new EpisodeSelectionCoordinator();

        var seasonId = coordinator.ResolveSeasonRequestId(new EmbyItem { Id = "folder-1", Type = "Folder" });

        Assert.IsNull(seasonId);
    }

    [TestMethod]
    public void FindEpisodeById_ReturnsMatchingEpisode()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var expected = new EmbyItem { Id = "episode-2", Type = "Episode" };
        var episodes = new[]
        {
            new EmbyItem { Id = "episode-1", Type = "Episode" },
            expected
        };

        var selected = coordinator.FindEpisodeById(episodes, new EmbyItem { Id = "episode-2", Type = "Episode" });

        Assert.AreSame(expected, selected);
    }

    [TestMethod]
    public void SelectNextEpisode_CurrentSeasonLastEpisode_ContinuesToNextSeason()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var current = new EmbyItem
        {
            Id = "s1e2",
            Type = "Episode",
            ParentIndexNumber = 1,
            IndexNumber = 2
        };
        var episodes = new[]
        {
            new EmbyItem { Id = "s2e1", Type = "Episode", ParentIndexNumber = 2, IndexNumber = 1, Name = "下一季第一集" },
            new EmbyItem { Id = "s1e1", Type = "Episode", ParentIndexNumber = 1, IndexNumber = 1, Name = "第一集" },
            current
        };

        var selected = coordinator.SelectNextEpisode(current, episodes);

        Assert.AreSame(episodes[0], selected);
    }

    [TestMethod]
    public void SelectNextEpisode_MissingEpisodeNumber_FallsBackToListPosition()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var current = new EmbyItem { Id = "episode-1", Type = "Episode", Name = "A" };
        var expectedNext = new EmbyItem { Id = "episode-2", Type = "Episode", Name = "B" };

        var selected = coordinator.SelectNextEpisode(current, [current, expectedNext]);

        Assert.AreSame(expectedNext, selected);
    }

    [TestMethod]
    public void SelectNextEpisode_LastEpisode_ReturnsNull()
    {
        var coordinator = new EpisodeSelectionCoordinator();
        var current = new EmbyItem
        {
            Id = "s1e2",
            Type = "Episode",
            ParentIndexNumber = 1,
            IndexNumber = 2
        };
        var episodes = new[]
        {
            new EmbyItem { Id = "s1e1", Type = "Episode", ParentIndexNumber = 1, IndexNumber = 1 },
            current
        };

        var selected = coordinator.SelectNextEpisode(current, episodes);

        Assert.IsNull(selected);
    }
}
