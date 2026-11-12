using System.Net;
using System.Text;
using Sillfall.Core.Weather;
using Sillfall.Core.Weather.Providers;

namespace Sillfall.Core.Tests.Weather;

public sealed class MetNorwayProviderTests : IDisposable
{
    private static readonly GeoPoint s_berlin = new(52.520008, 13.404954);
    private static readonly DateTimeOffset s_expires = new(2026, 11, 12, 6, 53, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_modified = new(2026, 11, 12, 6, 21, 8, TimeSpan.Zero);

    private readonly FakeHandler _handler = new();
    private readonly MetNorwayProvider _provider;

    public MetNorwayProviderTests()
    {
        _provider = new MetNorwayProvider(_handler);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task Fetch_SendsTheRoundedLocationAndAnIdentifyingUserAgent()
    {
        _handler.Respond = _ => Ok();

        WeatherFetch fetch = await _provider.FetchAsync(s_berlin, null, TestContext.Current.CancellationToken);

        fetch.Status.Should().Be(WeatherFetchStatus.Updated);
        _handler.Requests.Should().ContainSingle();
        HttpRequestMessage request = _handler.Requests[0];
        request.RequestUri!.ToString().Should().Be("https://api.met.no/weatherapi/locationforecast/2.0/compact?lat=52.5&lon=13.4");
        request.Headers.UserAgent.ToString().Should().StartWith($"{AppInfo.Name}/").And.Contain("github.com");
        request.Headers.IfModifiedSince.Should().BeNull("there is nothing to compare with yet");
    }

    [Fact]
    public async Task Fetch_ReadsTheForecastAndTheCacheHeaders()
    {
        _handler.Respond = _ => Ok();

        WeatherFetch fetch = await _provider.FetchAsync(s_berlin, null, TestContext.Current.CancellationToken);

        fetch.Forecast!.Steps.Should().HaveCount(4);
        fetch.Expires.Should().Be(s_expires);
        fetch.LastModified.Should().Be(s_modified);
        fetch.Error.Should().BeNull();
    }

    [Fact]
    public async Task Fetch_AgainWhenUnchanged_AsksWithIfModifiedSinceAndKeepsTheForecast()
    {
        _handler.Respond = _ => Ok();
        WeatherFetch first = await _provider.FetchAsync(s_berlin, null, TestContext.Current.CancellationToken);
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified) { Content = Empty(s_expires.AddMinutes(30)) };

        WeatherFetch second = await _provider.FetchAsync(s_berlin, first, TestContext.Current.CancellationToken);

        _handler.Requests[1].Headers.IfModifiedSince.Should().Be(s_modified);
        second.Status.Should().Be(WeatherFetchStatus.NotModified);
        second.Forecast.Should().BeSameAs(first.Forecast);
        second.LastModified.Should().Be(s_modified);
        second.Expires.Should().Be(s_expires.AddMinutes(30));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Fetch_ServiceError_FailsWithAReason(HttpStatusCode status)
    {
        _handler.Respond = _ => new HttpResponseMessage(status);

        WeatherFetch fetch = await _provider.FetchAsync(s_berlin, null, TestContext.Current.CancellationToken);

        fetch.Status.Should().Be(WeatherFetchStatus.Failed);
        fetch.Forecast.Should().BeNull();
        fetch.Error.Should().Contain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Fetch_NoConnection_Fails()
    {
        _handler.Respond = _ => throw new HttpRequestException("offline");

        WeatherFetch fetch = await _provider.FetchAsync(s_berlin, null, TestContext.Current.CancellationToken);

        fetch.Status.Should().Be(WeatherFetchStatus.Failed);
        fetch.Error.Should().Contain("connection");
    }

    [Fact]
    public async Task Fetch_Timeout_Fails()
    {
        _handler.Respond = _ => throw new TaskCanceledException("timeout");

        WeatherFetch fetch = await _provider.FetchAsync(s_berlin, null, TestContext.Current.CancellationToken);

        fetch.Status.Should().Be(WeatherFetchStatus.Failed);
        fetch.Error.Should().Contain("in time");
    }

    [Fact]
    public async Task Fetch_GarbledAnswer_Fails()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>maintenance</html>") };

        WeatherFetch fetch = await _provider.FetchAsync(s_berlin, null, TestContext.Current.CancellationToken);

        fetch.Status.Should().Be(WeatherFetchStatus.Failed);
    }

    [Fact]
    public async Task Fetch_InvalidLocation_SendsNothing()
    {
        _handler.Respond = _ => Ok();

        WeatherFetch fetch = await _provider.FetchAsync(new GeoPoint(123, 0), null, TestContext.Current.CancellationToken);

        fetch.Status.Should().Be(WeatherFetchStatus.Failed);
        _handler.Requests.Should().BeEmpty();
    }

    private static HttpResponseMessage Ok()
    {
        var content = new StringContent(MetNorwaySample.Json, Encoding.UTF8, "application/json");
        content.Headers.Expires = s_expires;
        content.Headers.LastModified = s_modified;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static ByteArrayContent Empty(DateTimeOffset expires)
    {
        var content = new ByteArrayContent([]);
        content.Headers.Expires = expires;
        return content;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }
}
