using Sillfall.Core.Visitors;

namespace Sillfall.Core.Tests.Visitors;

public sealed class VisitorAlbumTests : IDisposable
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly WeatherMood s_calm = new(0, 0, 0, 0, 0, 0, 0);
    private static readonly WeatherMood s_rain = new(0.8f, 0, 0, 0, 0, 0, 0);
    private static readonly int[] s_comboPoints = [10, 15, 20, 25, 30];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sillfall-album-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(VisitorKind.Drift, 10)]
    [InlineData(VisitorKind.Tuck, 25)]
    [InlineData(VisitorKind.Lumen, 50)]
    public void Collect_GivesPointsByRarity(VisitorKind kind, int points)
    {
        (VisitorAlbum album, CollectReward reward) = new VisitorAlbum().Collect(kind, s_calm, s_now);

        reward.Points.Should().Be(points);
        album.Points.Should().Be(points);
        album.CountOf(kind).Should().Be(1);
    }

    [Fact]
    public void Collect_InFavouriteWeather_DoublesThePoints()
    {
        CollectReward reward = new VisitorAlbum().Collect(VisitorKind.Pip, s_rain, s_now).Reward;

        reward.FavouriteWeather.Should().BeTrue();
        reward.Points.Should().Be(20);
        reward.NewAchievements.Should().Contain(Achievement.InTheirElement);
    }

    [Fact]
    public void Collect_WithinAMinute_BuildsACombo_ThatEndsAfterwards()
    {
        var album = new VisitorAlbum();
        var rewards = new List<CollectReward>();
        DateTimeOffset time = s_now;
        for (int i = 0; i < 5; i++)
        {
            (album, CollectReward reward) = album.Collect(VisitorKind.Nim, s_calm, time);
            rewards.Add(reward);
            time += TimeSpan.FromSeconds(30);
        }

        rewards.Select(reward => reward.Combo).Should().Equal(1, 2, 3, 4, 5);
        rewards.Select(reward => reward.Points).Should().Equal(s_comboPoints, "the factor stops at 3");
        album.BestCombo.Should().Be(5);
        album.Has(Achievement.ThreeInARow).Should().BeTrue();

        CollectReward later = album.Collect(VisitorKind.Nim, s_calm, time + TimeSpan.FromMinutes(5)).Reward;
        later.Combo.Should().Be(1);
    }

    [Fact]
    public void Achievements_AreReachedOnce()
    {
        var album = new VisitorAlbum();
        IReadOnlyList<Achievement> first = [];
        DateTimeOffset time = s_now;
        foreach (VisitorKind kind in Enum.GetValues<VisitorKind>())
        {
            (album, CollectReward reward) = album.Collect(kind, s_calm, time);
            if (kind == VisitorKind.Pip)
            {
                first = reward.NewAchievements;
            }

            time += TimeSpan.FromMinutes(10);
        }

        first.Should().Contain(Achievement.FirstGuest);
        album.Has(Achievement.FullHouse).Should().BeTrue();
        album.Has(Achievement.RareFind).Should().BeTrue();
        album.Has(Achievement.Hundred).Should().BeTrue("8 visitors make 175 points");
        album.Achievements.Should().OnlyHaveUniqueItems();
        album.Collect(VisitorKind.Pip, s_calm, time).Reward.NewAchievements.Should().NotContain(Achievement.FirstGuest);
    }

    [Fact]
    public void EveryWeather_NeedsFiveKindsOfWeather()
    {
        var album = new VisitorAlbum();
        WeatherMood[] moods =
        [
            new(0.8f, 0, 0, 0, 0, 0, 0),
            new(0, 0.8f, 0, 0, 0, 0, 0),
            new(0, 0, 0.8f, 0, 0, 0, 0),
            new(0, 0, 0, 0.8f, 0, 0, 0),
        ];
        foreach (WeatherMood mood in moods)
        {
            album = album.Collect(VisitorKind.Nim, mood, s_now).Album;
        }

        album.Has(Achievement.EveryWeather).Should().BeFalse();
        album.Collect(VisitorKind.Nim, new WeatherMood(0, 0, 0, 0, 0.8f, 0, 0), s_now).Album.Has(Achievement.EveryWeather).Should().BeTrue();
    }

    [Fact]
    public void Collect_DoesNotChangeTheOldAlbum()
    {
        var album = new VisitorAlbum();
        album = album.Collect(VisitorKind.Pip, s_calm, s_now).Album;

        album.Collect(VisitorKind.Pip, s_calm, s_now);

        album.CountOf(VisitorKind.Pip).Should().Be(1);
    }

    [Fact]
    public void Store_SaveThenLoad_GivesTheSameAlbum()
    {
        var store = new AlbumStore(Path.Combine(_directory, "collection.json"));
        VisitorAlbum album = new VisitorAlbum().Collect(VisitorKind.Tuck, s_rain, s_now).Album.Collect(VisitorKind.Pip, s_rain, s_now).Album;

        store.Save(album);
        VisitorAlbum loaded = store.Load();

        loaded.Should().BeEquivalentTo(album);
        File.ReadAllText(store.Path).Should().Contain("\"Tuck\"").And.Contain("\"FirstGuest\"").And.NotContain("totalCollected");
    }

    [Fact]
    public void Store_DamagedFile_GivesAnEmptyAlbumAndAReason()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "collection.json");
        File.WriteAllText(path, "{ not json");
        var store = new AlbumStore(path);

        store.Load().TotalCollected.Should().Be(0);
        store.LastError.Should().NotBeNull();
    }

    [Fact]
    public void Sanitized_RepairsHandEditedValues()
    {
        var album = new VisitorAlbum
        {
            Points = -5,
            Visitors =
            [
                new VisitorTally { Kind = VisitorKind.Pip, Count = 2, FirstCollected = s_now },
                new VisitorTally { Kind = VisitorKind.Pip, Count = 1, FirstCollected = s_now.AddDays(-1) },
                new VisitorTally { Kind = (VisitorKind)99, Count = 1 },
                new VisitorTally { Kind = VisitorKind.Sol, Count = 0 },
            ],
            Achievements = [Achievement.FirstGuest, Achievement.FirstGuest, (Achievement)42],
        };

        VisitorAlbum clean = album.Sanitized();

        clean.Points.Should().Be(0);
        clean.Visitors.Should().ContainSingle();
        clean.CountOf(VisitorKind.Pip).Should().Be(3);
        clean.Visitors[0].FirstCollected.Should().Be(s_now.AddDays(-1));
        clean.Achievements.Should().Equal(Achievement.FirstGuest);
    }
}
