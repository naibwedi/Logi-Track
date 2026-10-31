using logirack.Services;
using Xunit;

namespace LogiTrack.Tests;

public sealed class CoordinateServiceTests
{
    private readonly CityCoordinateService _service = new();

    [Theory]
    [InlineData("Oslo", 59.9139, 10.7522)]
    [InlineData("Bergen", 60.3913, 5.3221)]
    [InlineData("Trondheim", 63.4305, 10.3951)]
    [InlineData("Stavanger", 58.9700, 5.7331)]
    [InlineData("Kristiansand", 58.1599, 8.0182)]
    [InlineData("Drammen", 59.7441, 10.2045)]
    [InlineData("Fredrikstad", 59.2181, 10.9298)]
    [InlineData("Tromsø", 69.6492, 18.9553)]
    [InlineData("Sandnes", 58.8524, 5.7352)]
    [InlineData("Ålesund", 62.4722, 6.1495)]
    [InlineData("Haugesund", 59.4138, 5.2680)]
    [InlineData("Bodø", 67.2804, 14.4049)]
    [InlineData("Arendal", 58.4615, 8.7725)]
    [InlineData("Larvik", 59.0533, 10.0274)]
    [InlineData("Lillehammer", 61.1153, 10.4662)]
    [InlineData("Hamar", 60.7945, 11.0680)]
    public void KnownCities_ReturnExpectedCoordinates(string city, double expectedLat, double expectedLng)
    {
        var coords = _service.GetCoordinatesForCity(city);
        Assert.Equal(expectedLat, coords.Latitude, 4);
        Assert.Equal(expectedLng, coords.Longitude, 4);
    }

    [Theory]
    [InlineData("oslo", 59.9139, 10.7522)]
    [InlineData("OSLO", 59.9139, 10.7522)]
    [InlineData("  Bergen  ", 60.3913, 5.3221)]
    [InlineData("tRoNdHeIm", 63.4305, 10.3951)]
    public void CityLookup_IsCaseAndWhitespaceInsensitive(string cityInput, double expectedLat, double expectedLng)
    {
        var coords = _service.GetCoordinatesForCity(cityInput);
        Assert.Equal(expectedLat, coords.Latitude, 4);
        Assert.Equal(expectedLng, coords.Longitude, 4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrEmptyInput_ReturnsDefaultOsloCoordinates(string? input)
    {
        var coords = _service.GetCoordinatesForCity(input);
        Assert.Equal(59.9139, coords.Latitude, 4);
        Assert.Equal(10.7522, coords.Longitude, 4);
    }

    [Theory]
    [InlineData("RandomVillageNorway1")]
    [InlineData("UnknownNordicOutpost")]
    [InlineData("SvalbardOutpostX")]
    [InlineData("ImaginaryTown999")]
    [InlineData("TestFjordLocation")]
    public void UnknownCity_DeterministicFallback_ReturnsCoordinatesWithinNorwayBounds(string arbitraryCity)
    {
        var coords = _service.GetCoordinatesForCity(arbitraryCity);

        // Norway bounds: lat [58.0, 69.0], lng [5.0, 25.0]
        Assert.InRange(coords.Latitude, 58.0, 69.0);
        Assert.InRange(coords.Longitude, 5.0, 25.0);
    }

    [Fact]
    public void UnknownCity_DeterministicFallback_ProducesConsistentResults()
    {
        const string cityName = "ConsistentFantasyCity";
        var coords1 = _service.GetCoordinatesForCity(cityName);
        var coords2 = _service.GetCoordinatesForCity(cityName);
        var coords3 = _service.GetCoordinatesForCity(cityName.ToLowerInvariant());

        Assert.Equal(coords1.Latitude, coords2.Latitude);
        Assert.Equal(coords1.Longitude, coords2.Longitude);
        Assert.Equal(coords1.Latitude, coords3.Latitude);
        Assert.Equal(coords1.Longitude, coords3.Longitude);
    }

    [Fact]
    public async Task GetCoordinatesForCityAsync_ReturnsSameAsSync()
    {
        var syncCoords = _service.GetCoordinatesForCity("Trondheim");
        var asyncCoords = await _service.GetCoordinatesForCityAsync("Trondheim");

        Assert.Equal(syncCoords.Latitude, asyncCoords.Latitude);
        Assert.Equal(syncCoords.Longitude, asyncCoords.Longitude);
    }
}
