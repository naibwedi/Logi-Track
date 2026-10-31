using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using logirack.Controllers;
using logirack.Data;
using logirack.Hubs;
using logirack.Models;
using logirack.Models.ViewModel;
using logirack.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace LogiTrack.Tests;

/// <summary>
/// Adversarial edge-case and boundary resilience challenge test suite for Logi-Track Milestone 4.
/// Probes boundary lifecycle values (zero-cost freight, multi-million high-cost freight, negative validation rejection),
/// transaction rollback isolation under concurrent price conflicts and driver assignment races, and
/// high-concurrency multi-tenant SignalR group broadcast isolation.
/// </summary>
public sealed class ChallengerE2EResilienceTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public ChallengerE2EResilienceTests(LogiTrackFactory factory)
    {
        _factory = factory;
    }

    #region Thread-Safe SignalR Hub Test Harness Doubles

    private sealed class ThreadSafeHubCallerContext(ClaimsPrincipal? user, string connectionId = "conn-challenger-resilience") : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        public override ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items { get; } = new ConcurrentDictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class ThreadSafeGroupManager : IGroupManager
    {
        private readonly object _lock = new();
        public List<(string connectionId, string groupName)> AddedGroups { get; } = [];
        public List<(string connectionId, string groupName)> RemovedGroups { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                AddedGroups.Add((connectionId, groupName));
            }
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                RemovedGroups.Add((connectionId, groupName));
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ThreadSafeClientProxy : IClientProxy
    {
        private readonly object _lock = new();
        public List<(string method, object?[] args)> Messages { get; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Messages.Add((method, args));
            }
            return Task.CompletedTask;
        }

        public int MessageCount
        {
            get
            {
                lock (_lock)
                {
                    return Messages.Count;
                }
            }
        }

        public List<(string method, object?[] args)> GetSnapshot()
        {
            lock (_lock)
            {
                return [.. Messages];
            }
        }
    }

    private sealed class ThreadSafeHubCallerClients : IHubCallerClients
    {
        private readonly ConcurrentDictionary<string, ThreadSafeClientProxy> _groupProxies = new();
        public ThreadSafeClientProxy DefaultProxy { get; } = new();

        public IClientProxy All => DefaultProxy;
        public IClientProxy Caller => DefaultProxy;
        public IClientProxy Others => DefaultProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => DefaultProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => DefaultProxy;

        public IClientProxy Group(string groupName) =>
            _groupProxies.GetOrAdd(groupName, _ => new ThreadSafeClientProxy());

        public ThreadSafeClientProxy GetGroupProxy(string groupName) =>
            _groupProxies.GetOrAdd(groupName, _ => new ThreadSafeClientProxy());

        public IClientProxy Client(string connectionId) => DefaultProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => DefaultProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => DefaultProxy;
        public IClientProxy OthersInGroup(string groupName) => DefaultProxy;
        public IClientProxy User(string userId) => DefaultProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => DefaultProxy;
    }

    private static (TripHub hub, ThreadSafeGroupManager groups, ThreadSafeHubCallerClients clients) CreateThreadSafeHub(
        ApplicationDbContext db,
        ClaimsPrincipal? user,
        string connectionId = "conn-resilience-1")
    {
        var groups = new ThreadSafeGroupManager();
        var clients = new ThreadSafeHubCallerClients();
        var hub = new TripHub(db)
        {
            Context = new ThreadSafeHubCallerContext(user, connectionId),
            Groups = groups,
            Clients = clients
        };
        return (hub, groups, clients);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    #endregion

    #region HTTP & User Helpers

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    private static async Task<string> GetToken(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Failed to extract __RequestVerificationToken from {path}");
        return match.Groups[1].Value;
    }

    private async Task<HttpClient> LoginClient(string email, string password = DefaultPassword)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        var token = await GetToken(client, "/Identity/Account/Login");
        var login = await client.PostAsync("/Identity/Account/Login", Form(new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static async Task<T> EnsureUser<T>(UserManager<ApplicationUser> users, T user, string role, string password = DefaultPassword) where T : ApplicationUser
    {
        var existing = await users.FindByEmailAsync(user.Email!);
        if (existing is not null) return (T)existing;

        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        result = await users.AddToRoleAsync(user, role);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    #endregion

    #region PDF Text Decoding Helpers

    private static string ExtractTextFromPage(PdfPage page)
    {
        var fontMaps = new Dictionary<string, Dictionary<int, string>>();

        var fontsDict = page.Resources?.Elements.GetDictionary("/Font");
        if (fontsDict != null)
        {
            foreach (var key in fontsDict.Elements.Keys)
            {
                var font = fontsDict.Elements.GetDictionary(key);
                if (font == null) continue;

                var toUnicode = font.Elements["/ToUnicode"];
                if (toUnicode is PdfReference tRef && tRef.Value is PdfDictionary tDict && tDict.Stream != null)
                {
                    tDict.Stream.TryUnfilter();
                    var cmapText = Encoding.UTF8.GetString(tDict.Stream.Value);
                    fontMaps[key] = ParseCMap(cmapText);
                }
            }
        }

        var contentSb = new StringBuilder();
        for (int i = 0; i < page.Contents.Elements.Count; i++)
        {
            var item = page.Contents.Elements[i];
            PdfDictionary? dict = null;
            if (item is PdfReference pref && pref.Value is PdfDictionary d1) dict = d1;
            else if (item is PdfDictionary d2) dict = d2;

            if (dict?.Stream != null)
            {
                dict.Stream.TryUnfilter();
                contentSb.Append(Encoding.UTF8.GetString(dict.Stream.Value));
                contentSb.Append('\n');
            }
        }

        string rawContent = contentSb.ToString();
        var decoded = new StringBuilder();
        string currentFont = "";

        var tokens = Regex.Matches(rawContent, @"(/F\w+)\s+[\d\.]+\s+Tf|<([0-9A-Fa-f\r\n\s]+)>\s*Tj|\[([^\]]+)\]\s*TJ", RegexOptions.Singleline);
        foreach (Match m in tokens)
        {
            if (m.Groups[1].Success)
            {
                currentFont = m.Groups[1].Value;
            }
            else if (m.Groups[2].Success)
            {
                var cleanHex = Regex.Replace(m.Groups[2].Value, @"\s+", "");
                decoded.AppendLine(DecodeHexString(cleanHex, currentFont, fontMaps));
            }
            else if (m.Groups[3].Success)
            {
                var tjContent = m.Groups[3].Value;
                var tjHexMatches = Regex.Matches(tjContent, @"<([0-9A-Fa-f\r\n\s]+)>");
                var tjSb = new StringBuilder();
                foreach (Match hm in tjHexMatches)
                {
                    var cleanHex = Regex.Replace(hm.Groups[1].Value, @"\s+", "");
                    tjSb.Append(DecodeHexString(cleanHex, currentFont, fontMaps));
                }
                if (tjSb.Length > 0)
                {
                    decoded.AppendLine(tjSb.ToString());
                }
            }
        }

        return decoded.ToString();
    }

    private static string DecodeHexString(string hex, string fontName, Dictionary<string, Dictionary<int, string>> fontMaps)
    {
        fontMaps.TryGetValue(fontName, out var map);
        var sb = new StringBuilder();
        for (int i = 0; i + 4 <= hex.Length; i += 4)
        {
            int cid = int.Parse(hex.Substring(i, 4), NumberStyles.HexNumber);
            if (map != null && map.TryGetValue(cid, out var ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append((char)cid);
            }
        }
        return sb.ToString();
    }

    private static Dictionary<int, string> ParseCMap(string cmapText)
    {
        var map = new Dictionary<int, string>();

        var bfrangeMatches = Regex.Matches(cmapText, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>");
        foreach (Match m in bfrangeMatches)
        {
            int startCid = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
            int endCid = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
            int startUni = int.Parse(m.Groups[3].Value, NumberStyles.HexNumber);

            for (int cid = startCid; cid <= endCid; cid++)
            {
                map[cid] = ((char)(startUni + (cid - startCid))).ToString();
            }
        }

        var bfcharMatches = Regex.Matches(cmapText, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>");
        foreach (Match m in bfcharMatches)
        {
            int cid = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
            int uni = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
            if (!map.ContainsKey(cid))
            {
                map[cid] = ((char)uni).ToString();
            }
        }

        return map;
    }

    #endregion

    // =========================================================================
    // Challenge 1: Zero-Cost and Micro-Cost Freight E2E Lifecycle & PDF Invoicing
    // =========================================================================
    [Fact]
    public async Task Challenge_E2E_ZeroCostFreight_LifecycleAndPdfGeneration_Resilient()
    {
        const string customerEmail = "resilience-zero-cust@example.test";
        const string adminEmail = "resilience-zero-admin@example.test";
        const string driverEmail = "resilience-zero-driver@example.test";

        string customerId, adminId, driverId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = customerEmail,
                UserName = customerEmail,
                FirstName = "ZeroCost",
                LastName = "Customer",
                CompanyName = "Complimentary Shipping Corp",
                PhoneNumber = "+4799001122",
                DateOfBirth = new DateTime(1992, 3, 10),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            customerId = cust.Id;

            var adm = await EnsureUser(userManager, new Admin
            {
                Email = adminEmail,
                UserName = adminEmail,
                FirstName = "Admin",
                LastName = "ZeroQuoter",
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = adm.Id;

            var drv = await EnsureUser(userManager, new Driver
            {
                Email = driverEmail,
                UserName = driverEmail,
                DriverEmail = driverEmail,
                FirstName = "Driver",
                LastName = "ZeroRun",
                DateOfBirth = new DateTime(1985, 7, 20),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 10.0
            }, "Driver");
            driverId = drv.Id;
        }

        var customerClient = await LoginClient(customerEmail);
        var adminClient = await LoginClient(adminEmail);
        var driverClient = await LoginClient(driverEmail);

        // Step 1: Customer submits trip
        var createToken = await GetToken(customerClient, "/Customer/CreateTrip");
        var createTripResp = await customerClient.PostAsync("/Customer/CreateTrip", Form(new()
        {
            ["FromCity"] = "Oslo",
            ["ToCity"] = "Drammen",
            ["FromAddress"] = "Hafslundsgate 1",
            ["ToAddress"] = "Strømsø Torg 2",
            ["FromZipCode"] = "0150",
            ["ToZipCode"] = "3044",
            ["Weight"] = "25.0",
            ["Volume"] = "1.0",
            ["Distance"] = "45.0",
            ["GoodType"] = "Marketing Samples",
            ["PickupDate"] = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm"),
            ["Notes"] = "Promotional free delivery route test",
            ["__RequestVerificationToken"] = createToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, createTripResp.StatusCode);
        var locationHeader = createTripResp.Headers.Location?.ToString() ?? string.Empty;
        var tripMatch = Regex.Match(locationHeader, @"OrderSuccess(?:/|\?id=)(\d+)");
        Assert.True(tripMatch.Success);
        int tripId = int.Parse(tripMatch.Groups[1].Value);

        // Step 2: Admin reviews trip -> ApprovedByAdmin
        var reviewToken = await GetToken(adminClient, $"/Admin/TripDetails/{tripId}");
        var reviewResp = await adminClient.PostAsync("/Admin/ReviewTrip", Form(new()
        {
            ["tId"] = tripId.ToString(),
            ["isApproved"] = "true",
            ["__RequestVerificationToken"] = reviewToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, reviewResp.StatusCode);

        // Step 3: Admin sets price to 0.00 NOK (Zero-Cost Freight)
        var setPriceToken = await GetToken(adminClient, $"/Admin/SetPrice/{tripId}");
        var setPriceResp = await adminClient.PostAsync("/Admin/SetPrice", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["AdminPrice"] = "0.00",
            ["__RequestVerificationToken"] = setPriceToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, setPriceResp.StatusCode);

        // Verify in DB that AdminPrice is exactly 0.00 and Status is PriceSet
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(trip);
            Assert.Equal(0.00, trip.AdminPrice);
            Assert.Equal(TripStatus.PriceSet, trip.Status);
        }

        // Step 4: Customer accepts 0.00 NOK price
        var respondToken = await GetToken(customerClient, $"/Customer/TripDetails/{tripId}");
        var respondResp = await customerClient.PostAsync("/Customer/RespondToPrice", Form(new()
        {
            ["id"] = tripId.ToString(),
            ["approved"] = "true",
            ["__RequestVerificationToken"] = respondToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, respondResp.StatusCode);

        // Step 5: Admin assigns driver
        var assignToken = await GetToken(adminClient, $"/Admin/AssignTrip/{tripId}");
        var assignResp = await adminClient.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["__RequestVerificationToken"] = assignToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, assignResp.StatusCode);

        // Step 6: Driver sends GPS location update via SignalR -> InProgress
        using (var hubScope = _factory.Services.CreateScope())
        {
            var db = hubScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var driverPrincipal = CreatePrincipal(driverId, "Driver");
            var (hub, _, clients) = CreateThreadSafeHub(db, driverPrincipal, "conn-zero-drv");

            await hub.JoinTrip(tripId);
            await hub.SendLocationUpdate(tripId, 59.8000, 10.5000);

            var groupProxy = clients.GetGroupProxy($"trip_{tripId}");
            Assert.Contains(groupProxy.GetSnapshot(), m => m.method == "ReceiveLocationUpdate");
        }

        // Step 7: Driver ends trip -> Completed
        var endToken = await GetToken(driverClient, "/Driver/CurrentTrips");
        var endResp = await driverClient.PostAsync("/Driver/EndTrip", Form(new()
        {
            ["tripId"] = tripId.ToString(),
            ["__RequestVerificationToken"] = endToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, endResp.StatusCode);

        // Step 8: Download Invoice and verify PDF integrity for Zero-Cost Freight
        var pdfResp = await customerClient.GetAsync($"/Customer/DownloadInvoice/{tripId}");
        Assert.Equal(HttpStatusCode.OK, pdfResp.StatusCode);
        Assert.Equal("application/pdf", pdfResp.Content.Headers.ContentType?.MediaType);

        var pdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
        Assert.True(pdfBytes.Length > 1024);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, 5));

        using (var doc = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.ReadOnly))
        {
            Assert.Equal($"Invoice INV-{tripId:D6}", doc.Info.Title);
            Assert.True(doc.PageCount >= 1);
            var pageText = ExtractTextFromPage(doc.Pages[0]);
            Assert.Contains("PAID IN FULL", pageText);
            Assert.Contains("ITEMIZED FARE BREAKDOWN", pageText);
        }
    }

    // =========================================================================
    // Challenge 2: Multi-Million High-Cost Freight E2E Lifecycle & PDF Formatting
    // =========================================================================
    [Fact]
    public async Task Challenge_E2E_HighCostFreight_LifecycleAndPdfGeneration_Resilient()
    {
        const string customerEmail = "resilience-high-cust@example.test";
        const string adminEmail = "resilience-high-admin@example.test";
        const string driverEmail = "resilience-high-driver@example.test";

        string customerId, adminId, driverId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = customerEmail,
                UserName = customerEmail,
                FirstName = "HighRoller",
                LastName = "Customer",
                CompanyName = "Offshore Industrial AS",
                PhoneNumber = "+4799887766",
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            customerId = cust.Id;

            var adm = await EnsureUser(userManager, new Admin
            {
                Email = adminEmail,
                UserName = adminEmail,
                FirstName = "Admin",
                LastName = "HighValuer",
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = adm.Id;

            var drv = await EnsureUser(userManager, new Driver
            {
                Email = driverEmail,
                UserName = driverEmail,
                DriverEmail = driverEmail,
                FirstName = "Driver",
                LastName = "HeavyHaul",
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 25.0
            }, "Driver");
            driverId = drv.Id;
        }

        var customerClient = await LoginClient(customerEmail);
        var adminClient = await LoginClient(adminEmail);
        var driverClient = await LoginClient(driverEmail);

        // Step 1: Customer submits large cargo trip (50,000 kg, 2,500 km)
        var createToken = await GetToken(customerClient, "/Customer/CreateTrip");
        var createTripResp = await customerClient.PostAsync("/Customer/CreateTrip", Form(new()
        {
            ["FromCity"] = "Stavanger",
            ["ToCity"] = "Tromsø",
            ["FromAddress"] = "Risavika Havnering 14",
            ["ToAddress"] = "Breivika Sjøvei 8",
            ["FromZipCode"] = "4056",
            ["ToZipCode"] = "9019",
            ["Weight"] = "50000.0",
            ["Volume"] = "120.0",
            ["Distance"] = "2500.0",
            ["GoodType"] = "Subsea Drilling Turbine",
            ["PickupDate"] = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-ddTHH:mm"),
            ["Notes"] = "Multi-million NOK high-consequence heavy freight load",
            ["__RequestVerificationToken"] = createToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, createTripResp.StatusCode);
        var locationHeader = createTripResp.Headers.Location?.ToString() ?? string.Empty;
        var tripMatch = Regex.Match(locationHeader, @"OrderSuccess(?:/|\?id=)(\d+)");
        Assert.True(tripMatch.Success);
        int tripId = int.Parse(tripMatch.Groups[1].Value);

        // Step 2: Admin reviews trip -> ApprovedByAdmin
        var reviewToken = await GetToken(adminClient, $"/Admin/TripDetails/{tripId}");
        var reviewResp = await adminClient.PostAsync("/Admin/ReviewTrip", Form(new()
        {
            ["tId"] = tripId.ToString(),
            ["isApproved"] = "true",
            ["__RequestVerificationToken"] = reviewToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, reviewResp.StatusCode);

        // Step 3: Admin sets multi-million NOK price: 15,750,000.50 NOK
        const double highPrice = 15750000.50;
        var setPriceToken = await GetToken(adminClient, $"/Admin/SetPrice/{tripId}");
        var setPriceResp = await adminClient.PostAsync("/Admin/SetPrice", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["AdminPrice"] = highPrice.ToString("F2", CultureInfo.InvariantCulture),
            ["__RequestVerificationToken"] = setPriceToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, setPriceResp.StatusCode);

        // Verify in DB that AdminPrice is exactly 15,750,000.50
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(trip);
            Assert.Equal(highPrice, trip.AdminPrice);
            Assert.Equal(TripStatus.PriceSet, trip.Status);
        }

        // Step 4: Customer accepts high price
        var respondToken = await GetToken(customerClient, $"/Customer/TripDetails/{tripId}");
        var respondResp = await customerClient.PostAsync("/Customer/RespondToPrice", Form(new()
        {
            ["id"] = tripId.ToString(),
            ["approved"] = "true",
            ["__RequestVerificationToken"] = respondToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, respondResp.StatusCode);

        // Step 5: Admin assigns driver
        var assignToken = await GetToken(adminClient, $"/Admin/AssignTrip/{tripId}");
        var assignResp = await adminClient.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["__RequestVerificationToken"] = assignToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, assignResp.StatusCode);

        // Step 6: Driver sends GPS location update -> InProgress
        using (var hubScope = _factory.Services.CreateScope())
        {
            var db = hubScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var driverPrincipal = CreatePrincipal(driverId, "Driver");
            var (hub, _, clients) = CreateThreadSafeHub(db, driverPrincipal, "conn-high-drv");

            await hub.JoinTrip(tripId);
            await hub.SendLocationUpdate(tripId, 69.6492, 18.9553);

            var groupProxy = clients.GetGroupProxy($"trip_{tripId}");
            Assert.Contains(groupProxy.GetSnapshot(), m => m.method == "ReceiveLocationUpdate");
        }

        // Step 7: Driver ends trip -> Completed
        var endToken = await GetToken(driverClient, "/Driver/CurrentTrips");
        var endResp = await driverClient.PostAsync("/Driver/EndTrip", Form(new()
        {
            ["tripId"] = tripId.ToString(),
            ["__RequestVerificationToken"] = endToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, endResp.StatusCode);

        // Step 8: Download Invoice and verify multi-million NOK formatting in PDF
        var pdfResp = await customerClient.GetAsync($"/Customer/DownloadInvoice/{tripId}");
        Assert.Equal(HttpStatusCode.OK, pdfResp.StatusCode);

        var pdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
        Assert.True(pdfBytes.Length > 1024);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, 5));

        using (var doc = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.ReadOnly))
        {
            Assert.Equal($"Invoice INV-{tripId:D6}", doc.Info.Title);
            Assert.True(doc.PageCount >= 1);
            var pageText = ExtractTextFromPage(doc.Pages[0]);

            Assert.Contains("PAID IN FULL", pageText);
            Assert.Contains("15750000.50 NOK", pageText);
            Assert.Contains("Admin Rate Adjustment", pageText);
        }
    }

    // =========================================================================
    // Challenge 3: Negative Price & Input Boundary Defense
    // =========================================================================
    [Fact]
    public async Task Challenge_NegativePriceAndMetrics_RejectedByValidation()
    {
        const string customerEmail = "resilience-neg-cust@example.test";
        const string adminEmail = "resilience-neg-admin@example.test";
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = customerEmail,
                UserName = customerEmail,
                FirstName = "Negative",
                LastName = "Tester",
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var adm = await EnsureUser(userManager, new Admin
            {
                Email = adminEmail,
                UserName = adminEmail,
                FirstName = "Admin",
                LastName = "NegativeGuard",
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var trip = new Trip
            {
                CustomerId = cust.Id,
                AdminId = adm.Id,
                FromCity = "Oslo",
                ToCity = "Bergen",
                FromAddress = "Storgata 1",
                ToAddress = "Bryggen 1",
                FromZipCode = "0155",
                ToZipCode = "5003",
                Distance = 460,
                Weight = 50,
                Volume = 2,
                GoodsType = "Standard",
                Status = TripStatus.ApprovedByAdmin,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var adminClient = await LoginClient(adminEmail);
        var customerClient = await LoginClient(customerEmail);

        // Attempt 1: Admin attempts to set negative price (-500.00 NOK)
        var setPriceToken = await GetToken(adminClient, $"/Admin/SetPrice/{tripId}");
        var negPriceResp = await adminClient.PostAsync("/Admin/SetPrice", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["AdminPrice"] = "-500.00",
            ["__RequestVerificationToken"] = setPriceToken
        }));

        // In MVC, validation failure re-renders view with 200 OK (NOT redirect)
        Assert.Equal(HttpStatusCode.OK, negPriceResp.StatusCode);
        var negPriceHtml = await negPriceResp.Content.ReadAsStringAsync();
        Assert.Contains("positive number", negPriceHtml);

        // Verify Trip in DB was NOT updated
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(trip);
            Assert.Equal(TripStatus.ApprovedByAdmin, trip.Status);
            Assert.NotEqual(-500.00, trip.AdminPrice);
        }

        // Attempt 2: Customer attempts to create trip with negative weight and distance
        var createToken = await GetToken(customerClient, "/Customer/CreateTrip");
        var negTripResp = await customerClient.PostAsync("/Customer/CreateTrip", Form(new()
        {
            ["FromCity"] = "Oslo",
            ["ToCity"] = "Bergen",
            ["FromAddress"] = "A 1",
            ["ToAddress"] = "B 2",
            ["FromZipCode"] = "0150",
            ["ToZipCode"] = "5000",
            ["Weight"] = "-50.0",
            ["Distance"] = "-100.0",
            ["Volume"] = "-1.0",
            ["GoodType"] = "Invalid",
            ["PickupDate"] = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm"),
            ["__RequestVerificationToken"] = createToken
        }));

        Assert.Equal(HttpStatusCode.OK, negTripResp.StatusCode);
        var negTripHtml = await negTripResp.Content.ReadAsStringAsync();
        Assert.Contains("greater than", negTripHtml);
    }

    // =========================================================================
    // Challenge 4: Transaction Rollback Cleanliness Under Concurrent Conflicts
    // =========================================================================
    [Fact]
    public async Task Challenge_ConcurrentDriverAssignment_TransactionRollbackCleanliness_NoOrphanedLocks()
    {
        const string adminEmail = "resilience-trans-admin@example.test";
        string driverId, adminId;
        int trip1Id, trip2Id;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "resilience-trans-cust@example.test",
                UserName = "resilience-trans-cust@example.test",
                FirstName = "TxCustomer",
                LastName = "Test",
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var adm = await EnsureUser(userManager, new Admin
            {
                Email = adminEmail,
                UserName = adminEmail,
                FirstName = "TxAdmin",
                LastName = "Test",
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = adm.Id;

            // Single available driver
            var drv = await EnsureUser(userManager, new Driver
            {
                Email = "resilience-trans-drv@example.test",
                UserName = "resilience-trans-drv@example.test",
                DriverEmail = "resilience-trans-drv@example.test",
                FirstName = "SoleDriver",
                LastName = "Contended",
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 10.0
            }, "Driver");
            driverId = drv.Id;

            var t1 = new Trip
            {
                CustomerId = cust.Id,
                FromCity = "Oslo",
                ToCity = "Bergen",
                FromAddress = "A 1",
                ToAddress = "B 1",
                FromZipCode = "0150",
                ToZipCode = "5000",
                Distance = 460,
                Weight = 20,
                Volume = 1,
                Status = TripStatus.ApprovedByCustomer,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var t2 = new Trip
            {
                CustomerId = cust.Id,
                FromCity = "Oslo",
                ToCity = "Trondheim",
                FromAddress = "A 2",
                ToAddress = "B 2",
                FromZipCode = "0150",
                ToZipCode = "7000",
                Distance = 490,
                Weight = 30,
                Volume = 1.5,
                Status = TripStatus.ApprovedByCustomer,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Trips.AddRange(t1, t2);
            await db.SaveChangesAsync();
            trip1Id = t1.Id;
            trip2Id = t2.Id;
        }

        // Simulate two concurrent AdminController calls trying to assign the same driver to different trips
        Task<IActionResult> assignTask1;
        Task<IActionResult> assignTask2;

        using (var scope1 = _factory.Services.CreateScope())
        using (var scope2 = _factory.Services.CreateScope())
        {
            var controller1 = ActivatorUtilities.CreateInstance<AdminController>(scope1.ServiceProvider);
            var controller2 = ActivatorUtilities.CreateInstance<AdminController>(scope2.ServiceProvider);

            var httpContext1 = new DefaultHttpContext();
            httpContext1.User = CreatePrincipal(adminId, "Admin");
            controller1.ControllerContext = new ControllerContext { HttpContext = httpContext1 };
            controller1.TempData = new TempDataDictionary(httpContext1, scope1.ServiceProvider.GetRequiredService<ITempDataProvider>());

            var httpContext2 = new DefaultHttpContext();
            httpContext2.User = CreatePrincipal(adminId, "Admin");
            controller2.ControllerContext = new ControllerContext { HttpContext = httpContext2 };
            controller2.TempData = new TempDataDictionary(httpContext2, scope2.ServiceProvider.GetRequiredService<ITempDataProvider>());

            var model1 = new AssignTripViewModel { TripId = trip1Id, DriverId = driverId };
            var model2 = new AssignTripViewModel { TripId = trip2Id, DriverId = driverId };

            // Run concurrently
            assignTask1 = controller1.AssignTrip(model1);
            assignTask2 = controller2.AssignTrip(model2);

            var results = await Task.WhenAll(assignTask1, assignTask2);

            // Exactly ONE assignment must succeed with RedirectToAction("TripRequests")
            int redirectCount = results.Count(r => r is RedirectToActionResult redirect && redirect.ActionName == "TripRequests");
            Assert.Equal(1, redirectCount);

            // The losing assignment returned ViewResult with ModelState error (due to rollback on !driver.IsAvailable)
            int viewResultCount = results.Count(r => r is ViewResult);
            Assert.Equal(1, viewResultCount);
        }

        // Verify Database State: Driver assigned to exactly ONE trip, Driver.IsAvailable is false, and other trip is untouched
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var trip1 = await db.Trips.Include(t => t.DriverTrip).FirstAsync(t => t.Id == trip1Id);
            var trip2 = await db.Trips.Include(t => t.DriverTrip).FirstAsync(t => t.Id == trip2Id);
            var driver = await db.Drivers.FindAsync(driverId);

            Assert.NotNull(driver);
            Assert.False(driver.IsAvailable);

            bool trip1Assigned = trip1.Status == TripStatus.Assigned && trip1.DriverTrip != null;
            bool trip2Assigned = trip2.Status == TripStatus.Assigned && trip2.DriverTrip != null;

            // Mutual exclusion: trip1Assigned XOR trip2Assigned
            Assert.True(trip1Assigned ^ trip2Assigned, "Driver must be assigned to exactly one trip!");

            // Verify the other trip is cleanly rolled back and still ApprovedByCustomer without orphaned DriverTrip
            if (trip1Assigned)
            {
                Assert.Equal(TripStatus.ApprovedByCustomer, trip2.Status);
                Assert.Null(trip2.DriverTrip);
            }
            else
            {
                Assert.Equal(TripStatus.ApprovedByCustomer, trip1.Status);
                Assert.Null(trip1.DriverTrip);
            }

            // Verify Database Connection is healthy and subsequent queries / transactions execute with ZERO orphaned locks
            await using var subsequentTx = await db.Database.BeginTransactionAsync();
            var checkTrips = await db.Trips.CountAsync();
            Assert.True(checkTrips >= 2);
            await subsequentTx.CommitAsync();
        }
    }

    // =========================================================================
    // Challenge 5: Highly Concurrent Multi-Trip SignalR Isolation Under Heavy Load
    // =========================================================================
    [Fact]
    public async Task Challenge_MultiTripSignalR_ConcurrentBroadcast_StrictGroupIsolation()
    {
        const int tripCount = 5;
        const int updatesPerTrip = 20;

        var tripIds = new List<int>();
        var driverIds = new List<string>();
        string adminId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var adm = await EnsureUser(userManager, new Admin
            {
                Email = "sigr-resilience-admin@example.test",
                UserName = "sigr-resilience-admin@example.test",
                FirstName = "SigR",
                LastName = "Admin",
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = adm.Id;

            for (int i = 0; i < tripCount; i++)
            {
                var cust = await EnsureUser(userManager, new Customer
                {
                    Email = $"sigr-cust-{i}@example.test",
                    UserName = $"sigr-cust-{i}@example.test",
                    FirstName = $"Cust{i}",
                    LastName = "MultiTenant",
                    RoleType = "Customer",
                    IsApproved = true,
                    EmailConfirmed = true
                }, "Customer");

                var drv = await EnsureUser(userManager, new Driver
                {
                    Email = $"sigr-drv-{i}@example.test",
                    UserName = $"sigr-drv-{i}@example.test",
                    DriverEmail = $"sigr-drv-{i}@example.test",
                    FirstName = $"Driver{i}",
                    LastName = "MultiTenant",
                    RoleType = "Driver",
                    IsApproved = true,
                    EmailConfirmed = true,
                    IsAvailable = false,
                    PricePerKm = 10.0
                }, "Driver");

                driverIds.Add(drv.Id);

                var trip = new Trip
                {
                    CustomerId = cust.Id,
                    FromCity = "Oslo",
                    ToCity = "Bergen",
                    FromAddress = $"Start {i}",
                    ToAddress = $"End {i}",
                    Distance = 450 + i * 10,
                    Weight = 20,
                    Volume = 1,
                    Status = TripStatus.Assigned,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Trips.Add(trip);
                await db.SaveChangesAsync();

                var dt = new DriverTrip
                {
                    TripId = trip.Id,
                    DriverId = drv.Id,
                    AssignedByAdminId = adm.Id,
                    Status = TripStatus.Assigned,
                    AssignmentDate = DateTime.UtcNow,
                    UpdateAt = DateTime.UtcNow
                };
                db.DriverTrips.Add(dt);
                await db.SaveChangesAsync();

                tripIds.Add(trip.Id);
            }
        }

        // Shared thread-safe clients proxy across all hub instances to observe message delivery
        var sharedClients = new ThreadSafeHubCallerClients();
        var sharedGroups = new ThreadSafeGroupManager();

        // 1. All trips emit updates concurrently across multiple tasks
        var broadcastTasks = new List<Task>();

        for (int i = 0; i < tripCount; i++)
        {
            int tripIndex = i;
            int currentTripId = tripIds[tripIndex];
            string currentDriverId = driverIds[tripIndex];

            broadcastTasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var principal = CreatePrincipal(currentDriverId, "Driver");

                var hub = new TripHub(db)
                {
                    Context = new ThreadSafeHubCallerContext(principal, $"conn-driver-{tripIndex}"),
                    Groups = sharedGroups,
                    Clients = sharedClients
                };

                await hub.JoinTrip(currentTripId);

                for (int step = 0; step < updatesPerTrip; step++)
                {
                    double lat = 60.0 + (step * 0.01);
                    double lng = 10.0 + (step * 0.01);
                    await hub.SendLocationUpdate(currentTripId, lat, lng);
                }
            }));
        }

        await Task.WhenAll(broadcastTasks);

        // 2. Strict Group Scoping Verification:
        // Each trip group must receive EXACTLY updatesPerTrip messages, all matching that tripId
        for (int i = 0; i < tripCount; i++)
        {
            int currentTripId = tripIds[i];
            var groupProxy = sharedClients.GetGroupProxy($"trip_{currentTripId}");
            var messages = groupProxy.GetSnapshot();

            Assert.Equal(updatesPerTrip, messages.Count);
            foreach (var m in messages)
            {
                Assert.Equal("ReceiveLocationUpdate", m.method);
                Assert.Equal(currentTripId, (int)m.args[0]!);
            }
        }

        // 3. Admin Group Verification:
        // Admins group must receive all messages across all trips
        var adminProxy = sharedClients.GetGroupProxy("Admins");
        var adminMessages = adminProxy.GetSnapshot();
        Assert.Equal(tripCount * updatesPerTrip, adminMessages.Count);

        // 4. Concurrently verify rogue cross-tenant injection is rejected
        using (var rogueScope = _factory.Services.CreateScope())
        {
            var db = rogueScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rogueDriverPrincipal = CreatePrincipal(driverIds[0], "Driver"); // Driver 0

            var rogueHub = new TripHub(db)
            {
                Context = new ThreadSafeHubCallerContext(rogueDriverPrincipal, "conn-rogue"),
                Groups = sharedGroups,
                Clients = sharedClients
            };

            // Attempting to broadcast to Trip 1 (which belongs to Driver 1) must be rejected
            var rogueEx = await Assert.ThrowsAsync<HubException>(() =>
                rogueHub.SendLocationUpdate(tripIds[1], 60.5, 10.5));
            Assert.Equal("You are not authorized to update location for this trip.", rogueEx.Message);

            // Group 1 message count must NOT have increased
            var group1Proxy = sharedClients.GetGroupProxy($"trip_{tripIds[1]}");
            Assert.Equal(updatesPerTrip, group1Proxy.MessageCount);
        }
    }
}
