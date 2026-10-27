using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using logirack.Data;
using logirack.Models;
using logirack.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogiTrack.Tests;

public sealed class CoordinateResolutionStressTests : IClassFixture<LogiTrackFactory>
{
    private readonly CityCoordinateService _service = new();
    private readonly LogiTrackFactory _factory;
    private readonly HttpClient _client;

    public CoordinateResolutionStressTests(LogiTrackFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("\t\r\n  \v\f  ")]
    public void Boundary_NullOrEmptyOrWhitespace_StaysStrictlyWithinNorwayBounds(string? input)
    {
        var coords = _service.GetCoordinatesForCity(input);

        Assert.NotNull(coords);
        Assert.Equal(59.9139, coords.Latitude);
        Assert.Equal(10.7522, coords.Longitude);

        Assert.InRange(coords.Latitude, 58.0, 69.0);
        Assert.InRange(coords.Longitude, 5.0, 25.0);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void Boundary_ExtremeLongStrings_ResolveDeterministicallyWithinNorwayBounds(int stringLength)
    {
        var sb = new StringBuilder(stringLength);
        for (int i = 0; i < stringLength; i++)
        {
            sb.Append((char)('A' + (i % 26)));
        }
        var longString = sb.ToString();

        var coords1 = _service.GetCoordinatesForCity(longString);
        var coords2 = _service.GetCoordinatesForCity(longString);

        Assert.NotNull(coords1);
        Assert.Equal(coords1.Latitude, coords2.Latitude);
        Assert.Equal(coords1.Longitude, coords2.Longitude);

        // Norway bounds: lat [58.0, 69.0], lng [5.0, 25.0]
        Assert.InRange(coords1.Latitude, 58.0, 69.0);
        Assert.InRange(coords1.Longitude, 5.0, 25.0);
        Assert.False(double.IsNaN(coords1.Latitude));
        Assert.False(double.IsInfinity(coords1.Latitude));
        Assert.False(double.IsNaN(coords1.Longitude));
        Assert.False(double.IsInfinity(coords1.Longitude));
    }

    [Theory]
    [InlineData("北京市")]
    [InlineData("上海市")]
    [InlineData("Москва")]
    [InlineData("Санкт-Петербург")]
    [InlineData("Мурманск")]
    [InlineData("الرياض")]
    [InlineData("القاهرة")]
    [InlineData("ירושלים")]
    [InlineData("Αθήνα")]
    [InlineData("नई दिल्ली")]
    [InlineData("Tokyo: 東京都")]
    [InlineData("EmojiTest📍🏙️🚚🚛📦")]
    [InlineData("Mixed: Oslo + 📍 = 🇳🇴")]
    [InlineData("\u200B\u200C\u200D\uFEFFZeroWidthChars")]
    [InlineData("CombiningDiacritics: e\u0301\u0301\u0301")]
    [InlineData("ControlChars:\0\a\b\f\v\t\r\n")]
    [InlineData("SpecialSQLChars: ' OR '1'='1' --")]
    [InlineData("SpecialHTMLChars: <script>alert('xss')</script>")]
    public void Boundary_NonAsciiUnicodeAndSpecialStrings_ResolveWithinNorwayBounds(string unicodeCity)
    {
        var coords1 = _service.GetCoordinatesForCity(unicodeCity);
        var coords2 = _service.GetCoordinatesForCity(unicodeCity);

        Assert.NotNull(coords1);
        Assert.Equal(coords1.Latitude, coords2.Latitude);
        Assert.Equal(coords1.Longitude, coords2.Longitude);

        // Norway bounds: lat [58.0, 69.0], lng [5.0, 25.0]
        Assert.InRange(coords1.Latitude, 58.0, 69.0);
        Assert.InRange(coords1.Longitude, 5.0, 25.0);
    }

    [Fact]
    public void Stress_MonteCarlo_TenThousandArbitraryStrings_AllStayStrictlyWithinNorwayBounds()
    {
        var rng = new Random(42);
        for (int i = 0; i < 10_000; i++)
        {
            var len = rng.Next(1, 200);
            var chars = new char[len];
            for (int c = 0; c < len; c++)
            {
                // Random mix of ASCII and Unicode code points
                chars[c] = (char)rng.Next(32, 0xD7FF);
            }
            var testStr = new string(chars);

            var coords = _service.GetCoordinatesForCity(testStr);

            Assert.InRange(coords.Latitude, 58.0, 69.0);
            Assert.InRange(coords.Longitude, 5.0, 25.0);
            Assert.False(double.IsNaN(coords.Latitude));
            Assert.False(double.IsNaN(coords.Longitude));
        }
    }

    [Fact]
    public void Consistency_SameCityProducesIdenticalCoordinatesAcrossCalls()
    {
        var testCities = new[]
        {
            "Oslo", "Bergen", "Trondheim", "Stavanger", "Kristiansand",
            "RandomVillage123", "FjordStationDelta", "NordicHubOmega",
            "Ålesund", "Bodø", "Tromsø", "Alta", "Oslo ", "  Oslo  ",
            "OSLO", "oSlO", "TRONDHEIM", "trondheim"
        };

        foreach (var city in testCities)
        {
            var first = _service.GetCoordinatesForCity(city);
            for (int i = 0; i < 50; i++)
            {
                var next = _service.GetCoordinatesForCity(city);
                Assert.Equal(first.Latitude, next.Latitude);
                Assert.Equal(first.Longitude, next.Longitude);
            }
        }
    }

    [Fact]
    public async Task ConcurrencyStress_MultipleThreadsAccessingServiceSimultaneously_NeverFails()
    {
        const int threadCount = 20;
        const int iterationsPerThread = 500;
        var tasks = new Task[threadCount];

        for (int t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks[t] = Task.Run(async () =>
            {
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    var cityName = $"ThreadCity_{threadId}_{i % 10}";
                    var syncResult = _service.GetCoordinatesForCity(cityName);
                    var asyncResult = await _service.GetCoordinatesForCityAsync(cityName);

                    Assert.Equal(syncResult.Latitude, asyncResult.Latitude);
                    Assert.Equal(syncResult.Longitude, asyncResult.Longitude);
                    Assert.InRange(syncResult.Latitude, 58.0, 69.0);
                    Assert.InRange(syncResult.Longitude, 5.0, 25.0);
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void Empirical_ExamineKnownCitiesCoordinatesAgainstNorwayBounds()
    {
        // Test all known cities and document coordinates
        var knownCities = new[]
        {
            "Oslo", "Bergen", "Trondheim", "Stavanger", "Kristiansand",
            "Drammen", "Fredrikstad", "Tromsø", "Tromso", "Sandnes",
            "Ålesund", "Alesund", "Haugesund", "Bodø", "Bodo",
            "Arendal", "Larvik", "Lillehammer", "Hamar", "Halden",
            "Moss", "Porsgrunn", "Skien", "Molde", "Harstad", "Alta"
        };

        var outsideBounds = new List<(string City, double Lat, double Lng)>();

        foreach (var city in knownCities)
        {
            var coords = _service.GetCoordinatesForCity(city);
            if (coords.Latitude < 58.0 || coords.Latitude > 69.0 || coords.Longitude < 5.0 || coords.Longitude > 25.0)
            {
                outsideBounds.Add((city, coords.Latitude, coords.Longitude));
            }
        }

        // Cities in far Northern Norway (Tromsø at 69.6492, Alta at 69.9689) exceed 69.0
        // because Northern Norway geographically extends to ~71.2°N.
        // We assert these specific real geographic cities:
        Assert.Contains(outsideBounds, c => c.City == "Tromsø" && c.Lat > 69.0);
        Assert.Contains(outsideBounds, c => c.City == "Alta" && c.Lat > 69.0);

        // Confirm that the longitude for all known cities is strictly within [5.0, 25.0]
        foreach (var city in knownCities)
        {
            var coords = _service.GetCoordinatesForCity(city);
            Assert.InRange(coords.Longitude, 5.0, 25.0);
            Assert.True(coords.Latitude >= 58.0);
            Assert.True(coords.Latitude <= 71.2); // Real Norway northern boundary
        }
    }

    [Fact]
    public async Task Views_RenderInteractiveMapContainerWithValidCoordinateAttributes()
    {
        const string password = "IntegrationTest!2026";
        const string customerEmail = "mapview-customer@example.test";
        int testTripId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var existingCustomer = await users.FindByEmailAsync(customerEmail);
            if (existingCustomer == null)
            {
                var cust = new Customer
                {
                    Email = customerEmail,
                    UserName = customerEmail,
                    FirstName = "Map",
                    LastName = "Tester",
                    DateOfBirth = new DateTime(1990, 1, 1),
                    RoleType = "Customer",
                    IsApproved = true,
                    EmailConfirmed = true
                };
                await users.CreateAsync(cust, password);
                await users.AddToRoleAsync(cust, "Customer");
                existingCustomer = cust;
            }

            var trip = new Trip
            {
                CustomerId = existingCustomer.Id,
                FromCity = "Tromsø",
                ToCity = "Bergen",
                FromAddress = "Storgata 1",
                ToAddress = "Bryggen 5",
                FromZipCode = "9008",
                ToZipCode = "5003",
                PickupTime = DateTime.UtcNow.AddDays(1),
                GoodsType = "Electronics",
                Weight = 150,
                Volume = 2.5,
                Distance = 1600,
                EstimatedPrice = 12000,
                AdminPrice = 12500,
                Status = TripStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            testTripId = trip.Id;
        }

        // Sign in as customer and fetch TripDetails
        var loginPage = await _client.GetStringAsync("/Identity/Account/Login");
        var tokenMatch = System.Text.RegularExpressions.Regex.Match(
            loginPage, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        var token = tokenMatch.Groups[1].Value;

        var loginResp = await _client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = customerEmail,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);

        // Fetch TripDetails view
        var detailsResp = await _client.GetAsync($"/Customer/TripDetails/{testTripId}");
        Assert.Equal(HttpStatusCode.OK, detailsResp.StatusCode);

        var html = await detailsResp.Content.ReadAsStringAsync();
        var decodedHtml = System.Net.WebUtility.HtmlDecode(html);

        // Verify Leaflet CSS and JS are linked
        Assert.Contains("/lib/leaflet/leaflet.css", html);
        Assert.Contains("/lib/leaflet/leaflet.js", html);
        Assert.Contains("/js/tripTracking.js", html);

        // Verify map container rendered with proper attributes
        Assert.Contains("id=\"trip-map\"", html);
        Assert.Contains($"data-trip-id=\"{testTripId}\"", html);
        Assert.Contains("data-from-city=\"Tromsø\"", decodedHtml);
        Assert.Contains("data-to-city=\"Bergen\"", decodedHtml);

        // Check start coordinate data attributes (Tromsø: 69.6492, 18.9553; Bergen: 60.3913, 5.3221)
        Assert.Contains("data-start-lat=\"69.6492\"", html);
        Assert.Contains("data-start-lng=\"18.9553\"", html);
        Assert.Contains("data-dest-lat=\"60.3913\"", html);
        Assert.Contains("data-dest-lng=\"5.3221\"", html);
    }
}
