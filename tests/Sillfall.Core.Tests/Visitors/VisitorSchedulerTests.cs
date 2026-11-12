using Sillfall.Core.Visitors;

namespace Sillfall.Core.Tests.Visitors;

public class VisitorSchedulerTests
{
    [Fact]
    public void Catalog_HasEightOwnCharactersInEnumOrder()
    {
        VisitorCatalog.All.Should().HaveCount(8);
        VisitorCatalog.All.Select(profile => profile.Kind).Should().Equal(Enum.GetValues<VisitorKind>());
        VisitorCatalog.All.Select(profile => profile.Name).Should().OnlyHaveUniqueItems();
        VisitorCatalog.All.Should().ContainSingle(profile => profile.Rarity == Rarity.Rare);
        VisitorCatalog.All.Select(profile => profile.Affinity).Should().OnlyHaveUniqueItems("every weather has its own visitor");
    }

    [Fact]
    public void Never_PlansNoVisit()
    {
        var scheduler = new VisitorScheduler();
        scheduler.SetFrequency(VisitorFrequency.Never, now: 100);

        scheduler.IsDue(1e12).Should().BeFalse();
    }

    [Theory]
    [InlineData(VisitorFrequency.Rarely)]
    [InlineData(VisitorFrequency.Sometimes)]
    [InlineData(VisitorFrequency.Often)]
    public void PlanNext_IsBetweenHalfAndOneAndAHalfIntervals(VisitorFrequency frequency)
    {
        var scheduler = new VisitorScheduler(seed: 4);
        double mean = VisitorScheduler.MeanInterval(frequency).TotalSeconds;
        scheduler.SetFrequency(frequency, now: 0);

        for (int i = 0; i < 200; i++)
        {
            scheduler.PlanNext(now: 1000);
            (scheduler.NextVisit - 1000).Should().BeInRange(mean * 0.5, mean * 1.5);
        }
    }

    [Fact]
    public void Often_IsMoreOftenThanRarely_AndAllowsTwoAtOnce()
    {
        VisitorScheduler.MeanInterval(VisitorFrequency.Often).Should().BeLessThan(VisitorScheduler.MeanInterval(VisitorFrequency.Sometimes));
        VisitorScheduler.MeanInterval(VisitorFrequency.Sometimes).Should().BeLessThan(VisitorScheduler.MeanInterval(VisitorFrequency.Rarely));
        VisitorScheduler.MaxAtOnce(VisitorFrequency.Often).Should().Be(2);
        VisitorScheduler.MaxAtOnce(VisitorFrequency.Sometimes).Should().Be(1);
    }

    [Fact]
    public void SetFrequency_Unchanged_KeepsThePlan()
    {
        var scheduler = new VisitorScheduler();
        scheduler.SetFrequency(VisitorFrequency.Sometimes, now: 0);
        double planned = scheduler.NextVisit;

        scheduler.SetFrequency(VisitorFrequency.Sometimes, now: 500);

        scheduler.NextVisit.Should().Be(planned);
    }

    [Fact]
    public void Choose_FavouriteWeather_BringsItsVisitorMoreOften()
    {
        double calm = Share(VisitorKind.Pip, new WeatherMood(0, 0, 0, 0, 0, 0, 0));
        double rainy = Share(VisitorKind.Pip, new WeatherMood(1, 0, 0, 0, 0, 0, 0));

        rainy.Should().BeGreaterThan(calm * 2.5, "rain makes Pip up to four times as likely");
    }

    [Fact]
    public void Choose_RareVisitor_IsRarest()
    {
        var mood = new WeatherMood(0, 0, 0, 0, 0, 0, 0);

        Share(VisitorKind.Lumen, mood).Should().BeLessThan(Share(VisitorKind.Tuck, mood));
        Share(VisitorKind.Tuck, mood).Should().BeLessThan(Share(VisitorKind.Drift, mood));
    }

    [Fact]
    public void Mood_Dominant_IsTheStrongestAboveTheThreshold()
    {
        new WeatherMood(0.3f, 0.6f, 0, 0, 0, 0, 0).Dominant.Should().Be(WeatherAffinity.Snow);
        new WeatherMood(0.1f, 0.15f, 0, 0, 0, 0, 0).Dominant.Should().Be(WeatherAffinity.Any);
        new WeatherMood(0, 0, 0, 0, 0, 0, 0).Suits(WeatherAffinity.Any).Should().BeTrue();
        new WeatherMood(0, 0, 0, 0.5f, 0, 0, 0).Suits(WeatherAffinity.Fog).Should().BeTrue();
    }

    private static double Share(VisitorKind kind, WeatherMood mood)
    {
        var scheduler = new VisitorScheduler(seed: 11);
        int hits = 0;
        const int Draws = 20_000;
        for (int i = 0; i < Draws; i++)
        {
            if (scheduler.Choose(mood) == kind)
            {
                hits++;
            }
        }

        return hits / (double)Draws;
    }
}
