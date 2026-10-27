using System.Security.Cryptography;
using System.Text;

namespace logirack.Services;

public class CityCoordinateService : ICoordinateService
{
    private static readonly Coordinates DefaultCoordinates = new(59.9139, 10.7522); // Oslo

    private static readonly Dictionary<string, Coordinates> KnownCities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Oslo"] = new Coordinates(59.9139, 10.7522),
        ["Bergen"] = new Coordinates(60.3913, 5.3221),
        ["Trondheim"] = new Coordinates(63.4305, 10.3951),
        ["Stavanger"] = new Coordinates(58.9700, 5.7331),
        ["Kristiansand"] = new Coordinates(58.1599, 8.0182),
        ["Drammen"] = new Coordinates(59.7441, 10.2045),
        ["Fredrikstad"] = new Coordinates(59.2181, 10.9298),
        ["Tromsø"] = new Coordinates(69.6492, 18.9553),
        ["Tromso"] = new Coordinates(69.6492, 18.9553),
        ["Sandnes"] = new Coordinates(58.8524, 5.7352),
        ["Ålesund"] = new Coordinates(62.4722, 6.1495),
        ["Alesund"] = new Coordinates(62.4722, 6.1495),
        ["Haugesund"] = new Coordinates(59.4138, 5.2680),
        ["Bodø"] = new Coordinates(67.2804, 14.4049),
        ["Bodo"] = new Coordinates(67.2804, 14.4049),
        ["Arendal"] = new Coordinates(58.4615, 8.7725),
        ["Larvik"] = new Coordinates(59.0533, 10.0274),
        ["Lillehammer"] = new Coordinates(61.1153, 10.4662),
        ["Hamar"] = new Coordinates(60.7945, 11.0680),
        ["Halden"] = new Coordinates(59.1248, 11.3875),
        ["Moss"] = new Coordinates(59.4340, 10.6577),
        ["Porsgrunn"] = new Coordinates(59.1405, 9.6561),
        ["Skien"] = new Coordinates(59.2096, 9.6089),
        ["Molde"] = new Coordinates(62.7372, 7.1607),
        ["Harstad"] = new Coordinates(68.7986, 16.5419),
        ["Alta"] = new Coordinates(69.9689, 23.2717),

        // Major US Freight & Logistics Hubs
        ["New York"] = new Coordinates(40.7128, -74.0060),
        ["New York, NY"] = new Coordinates(40.7128, -74.0060),
        ["NYC"] = new Coordinates(40.7128, -74.0060),
        ["Los Angeles"] = new Coordinates(34.0522, -118.2437),
        ["Los Angeles, CA"] = new Coordinates(34.0522, -118.2437),
        ["LA"] = new Coordinates(34.0522, -118.2437),
        ["Chicago"] = new Coordinates(41.8781, -87.6298),
        ["Chicago, IL"] = new Coordinates(41.8781, -87.6298),
        ["Houston"] = new Coordinates(29.7604, -95.3698),
        ["Houston, TX"] = new Coordinates(29.7604, -95.3698),
        ["Phoenix"] = new Coordinates(33.4484, -112.0740),
        ["Phoenix, AZ"] = new Coordinates(33.4484, -112.0740),
        ["Philadelphia"] = new Coordinates(39.9526, -75.1652),
        ["Philadelphia, PA"] = new Coordinates(39.9526, -75.1652),
        ["San Antonio"] = new Coordinates(29.4241, -98.4936),
        ["San Antonio, TX"] = new Coordinates(29.4241, -98.4936),
        ["San Diego"] = new Coordinates(32.7157, -117.1611),
        ["San Diego, CA"] = new Coordinates(32.7157, -117.1611),
        ["Dallas"] = new Coordinates(32.7767, -96.7970),
        ["Dallas, TX"] = new Coordinates(32.7767, -96.7970),
        ["Austin"] = new Coordinates(30.2672, -97.7431),
        ["Austin, TX"] = new Coordinates(30.2672, -97.7431),
        ["Fort Worth"] = new Coordinates(32.7555, -97.3308),
        ["Fort Worth, TX"] = new Coordinates(32.7555, -97.3308),
        ["Jacksonville"] = new Coordinates(30.3322, -81.6557),
        ["Jacksonville, FL"] = new Coordinates(30.3322, -81.6557),
        ["Columbus"] = new Coordinates(39.9612, -82.9988),
        ["Columbus, OH"] = new Coordinates(39.9612, -82.9988),
        ["Charlotte"] = new Coordinates(35.2271, -80.8431),
        ["Charlotte, NC"] = new Coordinates(35.2271, -80.8431),
        ["Indianapolis"] = new Coordinates(39.7684, -86.1581),
        ["Indianapolis, IN"] = new Coordinates(39.7684, -86.1581),
        ["San Francisco"] = new Coordinates(37.7749, -122.4194),
        ["San Francisco, CA"] = new Coordinates(37.7749, -122.4194),
        ["Seattle"] = new Coordinates(47.6062, -122.3321),
        ["Seattle, WA"] = new Coordinates(47.6062, -122.3321),
        ["Denver"] = new Coordinates(39.7392, -104.9903),
        ["Denver, CO"] = new Coordinates(39.7392, -104.9903),
        ["Washington"] = new Coordinates(38.9072, -77.0369),
        ["Washington, DC"] = new Coordinates(38.9072, -77.0369),
        ["Boston"] = new Coordinates(42.3601, -71.0589),
        ["Boston, MA"] = new Coordinates(42.3601, -71.0589),
        ["Nashville"] = new Coordinates(36.1627, -86.7816),
        ["Nashville, TN"] = new Coordinates(36.1627, -86.7816),
        ["Detroit"] = new Coordinates(42.3314, -83.0458),
        ["Detroit, MI"] = new Coordinates(42.3314, -83.0458),
        ["Oklahoma City"] = new Coordinates(35.4676, -97.5164),
        ["Oklahoma City, OK"] = new Coordinates(35.4676, -97.5164),
        ["Portland"] = new Coordinates(45.5152, -122.6784),
        ["Portland, OR"] = new Coordinates(45.5152, -122.6784),
        ["Las Vegas"] = new Coordinates(36.1699, -115.1398),
        ["Las Vegas, NV"] = new Coordinates(36.1699, -115.1398),
        ["Memphis"] = new Coordinates(35.1495, -90.0490),
        ["Memphis, TN"] = new Coordinates(35.1495, -90.0490),
        ["Louisville"] = new Coordinates(38.2527, -85.7585),
        ["Louisville, KY"] = new Coordinates(38.2527, -85.7585),
        ["Baltimore"] = new Coordinates(39.2904, -76.6122),
        ["Baltimore, MD"] = new Coordinates(39.2904, -76.6122),
        ["Milwaukee"] = new Coordinates(43.0389, -87.9065),
        ["Milwaukee, WI"] = new Coordinates(43.0389, -87.9065),
        ["Albuquerque"] = new Coordinates(35.0844, -106.6504),
        ["Albuquerque, NM"] = new Coordinates(35.0844, -106.6504),
        ["Kansas City"] = new Coordinates(39.0997, -94.5786),
        ["Kansas City, MO"] = new Coordinates(39.0997, -94.5786),
        ["Atlanta"] = new Coordinates(33.7490, -84.3880),
        ["Atlanta, GA"] = new Coordinates(33.7490, -84.3880),
        ["Miami"] = new Coordinates(25.7617, -80.1918),
        ["Miami, FL"] = new Coordinates(25.7617, -80.1918),
        ["Minneapolis"] = new Coordinates(44.9778, -93.2650),
        ["Minneapolis, MN"] = new Coordinates(44.9778, -93.2650),
        ["Tampa"] = new Coordinates(27.9506, -82.4572),
        ["Tampa, FL"] = new Coordinates(27.9506, -82.4572),
        ["New Orleans"] = new Coordinates(29.9511, -90.0715),
        ["New Orleans, LA"] = new Coordinates(29.9511, -90.0715),
        ["Salt Lake City"] = new Coordinates(40.7608, -111.8910),
        ["Salt Lake City, UT"] = new Coordinates(40.7608, -111.8910),
        ["Pittsburgh"] = new Coordinates(40.4406, -79.9959),
        ["Pittsburgh, PA"] = new Coordinates(40.4406, -79.9959),
        ["Cincinnati"] = new Coordinates(39.1031, -84.5120),
        ["Cincinnati, OH"] = new Coordinates(39.1031, -84.5120),
        ["St. Louis"] = new Coordinates(38.6270, -90.1994),
        ["St. Louis, MO"] = new Coordinates(38.6270, -90.1994),
        ["Orlando"] = new Coordinates(28.5383, -81.3792),
        ["Orlando, FL"] = new Coordinates(28.5383, -81.3792)
    };

    public Coordinates GetCoordinatesForCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return DefaultCoordinates;
        }

        var trimmed = city.Trim();
        if (KnownCities.TryGetValue(trimmed, out var coords))
        {
            return coords;
        }

        // Deterministic fallback using SHA-256 hash mapped strictly into Norway bounds:
        // Latitude: [58.0, 69.0], Longitude: [5.0, 25.0]
        var normalized = trimmed.ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash1 = BitConverter.ToUInt32(hash, 0);
        var hash2 = BitConverter.ToUInt32(hash, 4);

        var lat = 58.0 + (hash1 % 110000) / 10000.0;
        var lng = 5.0 + (hash2 % 200000) / 10000.0;

        return new Coordinates(Math.Round(lat, 4), Math.Round(lng, 4));
    }

    public Task<Coordinates> GetCoordinatesForCityAsync(string? city)
    {
        return Task.FromResult(GetCoordinatesForCity(city));
    }
}
