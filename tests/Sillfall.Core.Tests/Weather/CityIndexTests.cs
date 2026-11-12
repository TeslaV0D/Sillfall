using Sillfall.Core.Weather;
using Sillfall.Core.Weather.Cities;

namespace Sillfall.Core.Tests.Weather;

public class CityIndexTests
{
    private static readonly Lazy<CityIndex> s_bundled = new(CityIndex.LoadBundled);

    private static CityIndex Bundled => s_bundled.Value;

    [Fact]
    public void LoadBundled_HasTheWholeList()
    {
        Bundled.Count.Should().BeGreaterThan(30_000);
    }

    [Theory]
    [InlineData("Berlin", "Berlin", "DE")]
    [InlineData("berlin", "Berlin", "DE")]
    [InlineData("München", "Munich", "DE")]
    [InlineData("Muenchen", "Munich", "DE")]
    [InlineData("munchen", "Munich", "DE")]
    [InlineData("Köln", "Köln", "DE")]
    [InlineData("koeln", "Köln", "DE")]
    [InlineData("Wien", "Vienna", "AT")]
    [InlineData("Sao Paulo", "São Paulo", "BR")]
    [InlineData("  new   york  ", "New York City", "US")]
    [InlineData("Vienna, US", "Vienna", "US")]
    public void Search_FindsTheCityByAnyOfItsNames(string query, string name, string country)
    {
        City city = Bundled.Search(query)[0];

        city.Name.Should().Be(name);
        city.Country.Should().Be(country);
    }

    [Fact]
    public void Search_StartOfAName_ListsBigCitiesFirst()
    {
        IReadOnlyList<City> cities = Bundled.Search("Frankf", limit: 5);

        cities.Should().HaveCountGreaterThan(1);
        cities[0].Name.Should().Be("Frankfurt am Main");
        cities.Select(city => city.Population).Should().BeInDescendingOrder();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xqzwv")]
    public void Search_NothingSensible_FindsNothing(string query)
    {
        Bundled.Search(query).Should().BeEmpty();
    }

    [Fact]
    public void Search_RespectsTheLimit()
    {
        Bundled.Search("san", limit: 3).Should().HaveCount(3);
    }

    [Fact]
    public void Nearest_NamesTheCityOfALocation()
    {
        Bundled.Nearest(new GeoPoint(52.52, 13.40))!.Name.Should().Be("Berlin");
        Bundled.Nearest(new GeoPoint(-33.87, 151.21))!.Name.Should().Be("Sydney");
    }

    [Theory]
    [InlineData("Straße", "strasse")]
    [InlineData("  São   Paulo ", "sao paulo")]
    [InlineData("ZÜRICH", "zurich")]
    [InlineData("Łódź", "lodz")]
    [InlineData("a|b", "a b")]
    public void Normalize_DropsAccentsCaseAndSpaces(string text, string expected)
    {
        CityIndex.Normalize(text).Should().Be(expected);
    }

    [Fact]
    public void Parse_SkipsBrokenLines()
    {
        const string List = "Alpha\tAA\t1.00\t2.00\t50000\t|alpha|\nbroken line\nBeta\tBB\tnorth\t2.00\t1\t|beta|\nGamma\tCC\t3.00\t4.00\t20000\t\n";

        CityIndex index = CityIndex.Parse(new StringReader(List));

        index.Count.Should().Be(2);
        index.Search("gamma").Should().ContainSingle().Which.Location.Should().Be(new GeoPoint(3, 4));
    }
}
