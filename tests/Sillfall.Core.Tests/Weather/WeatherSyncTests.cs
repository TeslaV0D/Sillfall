using Sillfall.Core.Weather;
using Sillfall.Core.Weather.Providers;

namespace Sillfall.Core.Tests.Weather;

public class WeatherSyncTests
{
    private static readonly GeoPoint s_berlin = new(52.52, 13.41);
    private static readonly DateTimeOffset s_now = MetNorwaySample.FirstStep.AddMinutes(10);

    [Fact]
    public async Task Refresh_Success_IsLive_AndFollowsExpires()
    {
        var provider = new FakeProvider { Next = _ => Updated(expires: s_now.AddMinutes(25)) };
        var sync = new WeatherSync(provider);

        WeatherSyncState state = await sync.RefreshAsync(s_berlin, s_now, TestContext.Current.CancellationToken);

        state.Should().Be(WeatherSyncState.Live);
        sync.Forecast.Should().NotBeNull();
        sync.LastSuccess.Should().Be(s_now);
        sync.NextAttempt.Should().Be(s_now.AddMinutes(25));
        sync.IsDue(s_now.AddMinutes(24)).Should().BeFalse();
        sync.IsDue(s_now.AddMinutes(25)).Should().BeTrue();
    }

    [Theory]
    [InlineData(2, 15)]
    [InlineData(-5, 15)]
    [InlineData(300, 60)]
    public async Task Refresh_ExpiresOutOfRange_IsClamped(int expiresInMinutes, int nextInMinutes)
    {
        var sync = new WeatherSync(new FakeProvider { Next = _ => Updated(expires: s_now.AddMinutes(expiresInMinutes)) });

        await sync.RefreshAsync(s_berlin, s_now, TestContext.Current.CancellationToken);

        sync.NextAttempt.Should().Be(s_now.AddMinutes(nextInMinutes));
    }

    [Fact]
    public async Task Refresh_WithoutExpires_WaitsHalfAnHour()
    {
        var sync = new WeatherSync(new FakeProvider { Next = _ => Updated(expires: null) });

        await sync.RefreshAsync(s_berlin, s_now, TestContext.Current.CancellationToken);

        sync.NextAttempt.Should().Be(s_now + WeatherSync.DefaultInterval);
    }

    [Fact]
    public async Task Refresh_Failures_BackOffAndKeepTheLastForecast()
    {
        var provider = new FakeProvider { Next = _ => Updated(expires: s_now.AddMinutes(30)) };
        var sync = new WeatherSync(provider);
        await sync.RefreshAsync(s_berlin, s_now, TestContext.Current.CancellationToken);
        WeatherForecast forecast = sync.Forecast!;
        provider.Next = _ => WeatherFetch.Failure("No connection to the weather service.");

        var waits = new List<double>();
        DateTimeOffset time = s_now;
        for (int i = 0; i < 7; i++)
        {
            await sync.RefreshAsync(s_berlin, time, TestContext.Current.CancellationToken);
            waits.Add((sync.NextAttempt - time).TotalMinutes);
            time = sync.NextAttempt;
        }

        waits.Should().Equal(1, 2, 4, 8, 16, 30, 30);
        sync.State.Should().Be(WeatherSyncState.Offline);
        sync.Forecast.Should().BeSameAs(forecast, "offline the effects follow the last known forecast");
        sync.LastError.Should().Contain("connection");
        sync.Failures.Should().Be(7);
    }

    [Fact]
    public async Task Refresh_AfterAFailure_SuccessIsLiveAgain()
    {
        var provider = new FakeProvider { Next = _ => WeatherFetch.Failure("offline") };
        var sync = new WeatherSync(provider);
        (await sync.RefreshAsync(s_berlin, s_now, TestContext.Current.CancellationToken)).Should().Be(WeatherSyncState.Waiting);

        provider.Next = _ => Updated(expires: null);
        (await sync.RefreshAsync(s_berlin, s_now.AddMinutes(1), TestContext.Current.CancellationToken)).Should().Be(WeatherSyncState.Live);

        sync.Failures.Should().Be(0);
        sync.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_PassesThePreviousAnswer_SoUnchangedForecastsAreNotSentAgain()
    {
        var provider = new FakeProvider { Next = _ => Updated(expires: null) };
        var sync = new WeatherSync(provider);
        await sync.RefreshAsync(s_berlin, s_now, TestContext.Current.CancellationToken);
        provider.Next = previous => previous! with { Status = WeatherFetchStatus.NotModified };

        await sync.RefreshAsync(s_berlin, s_now.AddMinutes(30), TestContext.Current.CancellationToken);

        provider.Previous[1].Should().NotBeNull();
        sync.State.Should().Be(WeatherSyncState.Live);
    }

    [Fact]
    public async Task Refresh_AnotherPlace_StartsOver_AndSendsOnlyRoundedLocations()
    {
        var provider = new FakeProvider { Next = _ => Updated(expires: null) };
        var sync = new WeatherSync(provider);
        await sync.RefreshAsync(s_berlin, s_now, TestContext.Current.CancellationToken);
        await sync.RefreshAsync(new GeoPoint(48.137, 11.576), s_now.AddMinutes(1), TestContext.Current.CancellationToken);

        provider.Previous[1].Should().BeNull("Munich has no previous answer");
        provider.Locations.Should().Equal(new GeoPoint(52.5, 13.4), new GeoPoint(48.1, 11.6));
    }

    [Fact]
    public async Task Targets_MapTheStepOfTheGivenTime()
    {
        var sync = new WeatherSync(new FakeProvider { Next = _ => Updated(expires: null) });
        sync.Targets(s_now, invert: false).Should().BeNull("there is no forecast yet");
        await sync.RefreshAsync(s_berlin, s_now, TestContext.Current.CancellationToken);

        WeatherTargets first = sync.Targets(s_now, invert: false)!;
        WeatherTargets second = sync.Targets(s_now.AddHours(1), invert: false)!;

        first.Rain.Should().BeGreaterThan(0f, "light rain in the first step");
        second.Snow.Should().BeGreaterThan(0f, "snow showers in the second");
        second.SnowStays.Should().BeTrue("-1.5 °C");
    }

    private static WeatherFetch Updated(DateTimeOffset? expires) =>
        new(WeatherFetchStatus.Updated, MetNorwayParser.Parse(MetNorwaySample.Json), expires, s_now, null);

    private sealed class FakeProvider : IWeatherProvider
    {
        public Func<WeatherFetch?, WeatherFetch> Next { get; set; } = _ => WeatherFetch.Failure("not set");

        public List<GeoPoint> Locations { get; } = [];

        public List<WeatherFetch?> Previous { get; } = [];

        public string Attribution => "test";

        public Task<WeatherFetch> FetchAsync(GeoPoint location, WeatherFetch? previous, CancellationToken cancellationToken)
        {
            Locations.Add(location);
            Previous.Add(previous);
            return Task.FromResult(Next(previous));
        }
    }
}
