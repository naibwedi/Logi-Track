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
/// Tier 4 End-to-End Multi-Tier Integration Test Scenarios for Logi-Track Overhaul.
/// Validates the full trip lifecycle, live GPS SignalR telemetry, map UI rendering,
/// PDF invoice generation, and security penetration defenses (IDOR, Concurrency 409,
/// Hub authorization, and invoice isolation) per TEST_INFRA.md and ORIGINAL_REQUEST.md.
/// </summary>
public sealed class FullWorkflowEndToEndTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public FullWorkflowEndToEndTests(LogiTrackFactory factory)
    {
        _factory = factory;
    }

    #region SignalR Hub Test Harness Doubles

    private sealed class TestHubCallerContext(ClaimsPrincipal? user, string connectionId = "conn-e2e-1") : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        public override ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class TestGroupManager : IGroupManager
    {
        public List<(string connectionId, string groupName)> AddedGroups { get; } = [];
        public List<(string connectionId, string groupName)> RemovedGroups { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddedGroups.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            RemovedGroups.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class TestClientProxy : IClientProxy
    {
        public List<(string method, object?[] args)> Messages { get; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Messages.Add((method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class TestHubCallerClients : IHubCallerClients
    {
        public Dictionary<string, TestClientProxy> GroupProxies { get; } = [];
        public TestClientProxy DefaultProxy { get; } = new();

        public IClientProxy All => DefaultProxy;
        public IClientProxy Caller => DefaultProxy;
        public IClientProxy Others => DefaultProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => DefaultProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => DefaultProxy;

        public IClientProxy Group(string groupName)
        {
            if (!GroupProxies.TryGetValue(groupName, out var proxy))
            {
                proxy = new TestClientProxy();
                GroupProxies[groupName] = proxy;
            }
            return proxy;
        }

        public IClientProxy Client(string connectionId) => DefaultProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => DefaultProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => DefaultProxy;
        public IClientProxy OthersInGroup(string groupName) => DefaultProxy;
        public IClientProxy User(string userId) => DefaultProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => DefaultProxy;
    }

    private static (TripHub hub, TestGroupManager groups, TestHubCallerClients clients) CreateHub(
        ApplicationDbContext db,
        ClaimsPrincipal? user,
        string connectionId = "conn-e2e-1")
    {
        var hub = new TripHub(db)
        {
            Context = new TestHubCallerContext(user, connectionId),
            Groups = new TestGroupManager(),
            Clients = new TestHubCallerClients()
        };
        return (hub, (TestGroupManager)hub.Groups, (TestHubCallerClients)hub.Clients);
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

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

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

    #region PDF Parsing & Text Extraction Helpers

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
    // Tier 4 Scenario 1: Comprehensive E2E Trip Lifecycle
    // =========================================================================
    [Fact]
    public async Task E2E_Lifecycle_CustomerSubmit_AdminPrice_CustomerAccept_AssignDriver_SimulateGps_EndTrip_DownloadInvoices()
    {
        // -------------------------------------------------------------
        // Setup Users: Customer, Admin, Driver
        // -------------------------------------------------------------
        const string customerEmail = "e2e-lifecycle-cust@example.test";
        const string adminEmail = "e2e-lifecycle-admin@example.test";
        const string driverEmail = "e2e-lifecycle-driver@example.test";

        string customerId, adminId, driverId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = customerEmail,
                UserName = customerEmail,
                FirstName = "Lars",
                LastName = "Bakke",
                CompanyName = "Bakke Logistics AS",
                PhoneNumber = "+4798112233",
                DateOfBirth = new DateTime(1990, 4, 12),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            customerId = cust.Id;

            var adm = await EnsureUser(userManager, new Admin
            {
                Email = adminEmail,
                UserName = adminEmail,
                FirstName = "Hanne",
                LastName = "Sørensen",
                CompanyName = "Logi-Track Operations HQ",
                DateOfBirth = new DateTime(1982, 11, 25),
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
                FirstName = "Svein",
                LastName = "Haugen",
                DateOfBirth = new DateTime(1987, 8, 14),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 12.50,
                PaymentFreq = PaymentFreq.Monthly
            }, "Driver");
            driverId = drv.Id;
        }

        // -------------------------------------------------------------
        // Step 1: Customer submits trip request with potentially unsafe notes
        // -------------------------------------------------------------
        var customerClient = await LoginClient(customerEmail);
        var createToken = await GetToken(customerClient, "/Customer/CreateTrip");

        const string rawNotes = "<script>alert('pwn')</script>Fragile pharmaceutical cargo with strict temperature control.";
        var createTripResp = await customerClient.PostAsync("/Customer/CreateTrip", Form(new()
        {
            ["FromCity"] = "Oslo",
            ["ToCity"] = "Bergen",
            ["FromAddress"] = "Dronning Eufemias gate 8",
            ["ToAddress"] = "Torgallmenningen 1",
            ["FromZipCode"] = "0191",
            ["ToZipCode"] = "5014",
            ["Weight"] = "65.0",
            ["Volume"] = "2.8",
            ["Distance"] = "460.0",
            ["GoodType"] = "Pharmaceuticals",
            ["PickupDate"] = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm"),
            ["Notes"] = rawNotes,
            ["__RequestVerificationToken"] = createToken
        }));

        Assert.Equal(HttpStatusCode.Redirect, createTripResp.StatusCode);
        var locationHeader = createTripResp.Headers.Location?.ToString() ?? string.Empty;
        var tripMatch = Regex.Match(locationHeader, @"OrderSuccess(?:/|\?id=)(\d+)");
        Assert.True(tripMatch.Success, $"Could not extract trip ID from redirect: {locationHeader}");
        int tripId = int.Parse(tripMatch.Groups[1].Value);

        // Verify in Database: Notes sanitized, status Requested, ConcurrencyToken valid
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(trip);
            Assert.Equal(TripStatus.Requested, trip.Status);
            Assert.Equal(customerId, trip.CustomerId);
            Assert.DoesNotContain("<script>", trip.Notes);
            Assert.Contains("Fragile pharmaceutical cargo", trip.Notes);
            Assert.NotEqual(Guid.Empty, trip.ConcurrencyToken);
        }

        // -------------------------------------------------------------
        // Step 2: Admin reviews trip -> ApprovedByAdmin
        // -------------------------------------------------------------
        var adminClient = await LoginClient(adminEmail);
        var adminReviewToken = await GetToken(adminClient, $"/Admin/TripDetails/{tripId}");

        var reviewResp = await adminClient.PostAsync("/Admin/ReviewTrip", Form(new()
        {
            ["tId"] = tripId.ToString(),
            ["isApproved"] = "true",
            ["__RequestVerificationToken"] = adminReviewToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, reviewResp.StatusCode);

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(trip);
            Assert.Equal(TripStatus.ApprovedByAdmin, trip.Status);
            Assert.Equal(adminId, trip.AdminId);
        }

        // -------------------------------------------------------------
        // Step 3: Admin sets final price -> PriceSet
        // -------------------------------------------------------------
        var setPriceToken = await GetToken(adminClient, $"/Admin/SetPrice/{tripId}");
        const double quotedPrice = 7500.00;

        var setPriceResp = await adminClient.PostAsync("/Admin/SetPrice", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["AdminPrice"] = quotedPrice.ToString("F2", CultureInfo.InvariantCulture),
            ["__RequestVerificationToken"] = setPriceToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, setPriceResp.StatusCode);

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(trip);
            Assert.Equal(TripStatus.PriceSet, trip.Status);
            Assert.Equal(quotedPrice, trip.AdminPrice);
        }

        // -------------------------------------------------------------
        // Step 4: Customer accepts quoted price -> ApprovedByCustomer
        // -------------------------------------------------------------
        var customerRespondToken = await GetToken(customerClient, $"/Customer/TripDetails/{tripId}");

        var respondResp = await customerClient.PostAsync("/Customer/RespondToPrice", Form(new()
        {
            ["id"] = tripId.ToString(),
            ["approved"] = "true",
            ["__RequestVerificationToken"] = customerRespondToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, respondResp.StatusCode);

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(trip);
            Assert.Equal(TripStatus.ApprovedByCustomer, trip.Status);
        }

        // -------------------------------------------------------------
        // Step 5: Admin assigns available driver -> Assigned
        // -------------------------------------------------------------
        var assignToken = await GetToken(adminClient, $"/Admin/AssignTrip/{tripId}");

        var assignResp = await adminClient.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["__RequestVerificationToken"] = assignToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, assignResp.StatusCode);

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);
            var driver = await db.Drivers.FindAsync(driverId);

            Assert.NotNull(trip);
            Assert.Equal(TripStatus.Assigned, trip.Status);
            Assert.NotNull(trip.DriverTrip);
            Assert.Equal(driverId, trip.DriverTrip.DriverId);
            Assert.Equal(TripStatus.Assigned, trip.DriverTrip.Status);
            Assert.NotNull(driver);
            Assert.False(driver.IsAvailable, "Driver availability must be locked upon assignment");
        }

        // -------------------------------------------------------------
        // Step 6: Verify Map Rendering on Active Trip Views
        // -------------------------------------------------------------
        var custDetailsResp = await customerClient.GetAsync($"/Customer/TripDetails/{tripId}");
        Assert.Equal(HttpStatusCode.OK, custDetailsResp.StatusCode);
        var custHtml = await custDetailsResp.Content.ReadAsStringAsync();

        Assert.Contains("id=\"trip-map\"", custHtml);
        Assert.Contains($"data-trip-id=\"{tripId}\"", custHtml);
        Assert.Contains("/lib/leaflet/leaflet.js", custHtml);
        Assert.Contains("/lib/leaflet/leaflet.css", custHtml);
        Assert.Contains("/js/tripTracking.js", custHtml);

        var driverClient = await LoginClient(driverEmail);
        var driverTripsResp = await driverClient.GetAsync("/Driver/CurrentTrips");
        Assert.Equal(HttpStatusCode.OK, driverTripsResp.StatusCode);
        var driverHtml = await driverTripsResp.Content.ReadAsStringAsync();

        Assert.Contains("id=\"trip-map\"", driverHtml);
        Assert.Contains("id=\"driver-simulation-panel\"", driverHtml);
        Assert.Contains("/js/driverSimulation.js", driverHtml);

        // -------------------------------------------------------------
        // Step 7: Driver connects via SignalR and emits GPS coordinates -> InProgress
        // -------------------------------------------------------------
        using (var hubScope = _factory.Services.CreateScope())
        {
            var db = hubScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var driverPrincipal = CreatePrincipal(driverId, "Driver");

            var (hub, groups, clients) = CreateHub(db, driverPrincipal, "conn-driver-e2e");

            // Driver joins trip group
            await hub.JoinTrip(tripId);
            Assert.Contains(groups.AddedGroups, g => g.groupName == $"trip_{tripId}");

            // Driver emits simulated GPS coordinates
            const double testLat = 60.1234;
            const double testLng = 10.5678;
            await hub.SendLocationUpdate(tripId, testLat, testLng);

            // Group broadcast validation
            var tripGroup = clients.Group($"trip_{tripId}");
            var testProxy = Assert.IsType<TestClientProxy>(tripGroup);
            Assert.Contains(testProxy.Messages, m =>
                m.method == "ReceiveLocationUpdate" &&
                (int)m.args[0]! == tripId &&
                Math.Abs((double)m.args[1]! - testLat) < 0.0001 &&
                Math.Abs((double)m.args[2]! - testLng) < 0.0001);
        }

        // Verify status automatically transitioned to InProgress and Location is updated in DB
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);
            var loc = await db.Locations.FirstOrDefaultAsync(l => l.DriverID == driverId);

            Assert.NotNull(trip);
            Assert.Equal(TripStatus.InProgress, trip.Status);
            Assert.NotNull(trip.DriverTrip);
            Assert.Equal(TripStatus.InProgress, trip.DriverTrip.Status);
            Assert.NotNull(loc);
            Assert.Equal(60.1234, loc.Latitude, 4);
            Assert.Equal(10.5678, loc.Longitude, 4);
        }

        // -------------------------------------------------------------
        // Step 8: Driver ends trip -> Completed & Driver made available
        // -------------------------------------------------------------
        var endTripToken = await GetToken(driverClient, "/Driver/CurrentTrips");
        var endTripResp = await driverClient.PostAsync("/Driver/EndTrip", Form(new()
        {
            ["tripId"] = tripId.ToString(),
            ["__RequestVerificationToken"] = endTripToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, endTripResp.StatusCode);

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);
            var driver = await db.Drivers.FindAsync(driverId);

            Assert.NotNull(trip);
            Assert.Equal(TripStatus.Completed, trip.Status);
            Assert.NotNull(trip.DriverTrip);
            Assert.Equal(TripStatus.Completed, trip.DriverTrip.Status);
            Assert.NotNull(trip.DriverTrip.CompletionDate);
            Assert.NotNull(driver);
            Assert.True(driver.IsAvailable, "Driver availability must be restored upon trip completion");
        }

        // -------------------------------------------------------------
        // Step 9: Customer & Admin download PDF invoices with "PAID IN FULL"
        // -------------------------------------------------------------
        // Customer download
        var custPdfResp = await customerClient.GetAsync($"/Customer/DownloadInvoice/{tripId}");
        Assert.Equal(HttpStatusCode.OK, custPdfResp.StatusCode);
        Assert.Equal("application/pdf", custPdfResp.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"Invoice_Trip_{tripId}.pdf", custPdfResp.Content.Headers.ContentDisposition?.FileName);

        var custPdfBytes = await custPdfResp.Content.ReadAsByteArrayAsync();
        Assert.True(custPdfBytes.Length > 1024, "Invoice PDF byte stream must be substantial (> 1KB)");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(custPdfBytes, 0, 5));

        using (var doc = PdfReader.Open(new MemoryStream(custPdfBytes), PdfDocumentOpenMode.ReadOnly))
        {
            Assert.Equal($"Invoice INV-{tripId:D6}", doc.Info.Title);
            Assert.Equal("Logi-Track", doc.Info.Author);
            Assert.Equal($"Trip #{tripId} Shipment Invoice", doc.Info.Subject);
            Assert.True(doc.PageCount >= 1);

            var pageText = ExtractTextFromPage(doc.Pages[0]);
            Assert.Contains("PAID IN FULL", pageText);
            Assert.Contains("Lars Bakke", pageText);
            Assert.Contains("ITEMIZED FARE BREAKDOWN", pageText);
        }

        // Admin download
        var adminPdfResp = await adminClient.GetAsync($"/Admin/DownloadInvoice/{tripId}");
        Assert.Equal(HttpStatusCode.OK, adminPdfResp.StatusCode);
        Assert.Equal("application/pdf", adminPdfResp.Content.Headers.ContentType?.MediaType);

        var adminPdfBytes = await adminPdfResp.Content.ReadAsByteArrayAsync();
        Assert.True(adminPdfBytes.Length > 1024);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(adminPdfBytes, 0, 5));

        using (var doc = PdfReader.Open(new MemoryStream(adminPdfBytes), PdfDocumentOpenMode.ReadOnly))
        {
            Assert.Equal($"Invoice INV-{tripId:D6}", doc.Info.Title);
            var pageText = ExtractTextFromPage(doc.Pages[0]);
            Assert.Contains("PAID IN FULL", pageText);
        }
    }

    // =========================================================================
    // Tier 4 Scenario 2: E2E Security Penetration Sequential Attack Chain
    // =========================================================================
    [Fact]
    public async Task E2E_SecurityPenetrationSequence_AllFiveDefensesHold()
    {
        // -------------------------------------------------------------
        // Setup: Victim Customer, Attacker Customer, Attacker Driver
        // -------------------------------------------------------------
        const string victimCustomerEmail = "pen-victim-cust@example.test";
        const string attackerCustomerEmail = "pen-attacker-cust@example.test";
        const string attackerDriverEmail = "pen-attacker-driver@example.test";

        string victimCustomerId, attackerCustomerId, attackerDriverId;
        int victimTripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var victim = await EnsureUser(userManager, new Customer
            {
                Email = victimCustomerEmail,
                UserName = victimCustomerEmail,
                FirstName = "Victim",
                LastName = "Customer",
                CompanyName = "Target Logistics",
                DateOfBirth = new DateTime(1991, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            victimCustomerId = victim.Id;

            var attackerCust = await EnsureUser(userManager, new Customer
            {
                Email = attackerCustomerEmail,
                UserName = attackerCustomerEmail,
                FirstName = "Attacker",
                LastName = "Customer",
                DateOfBirth = new DateTime(1993, 2, 2),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            attackerCustomerId = attackerCust.Id;

            var attackerDrv = await EnsureUser(userManager, new Driver
            {
                Email = attackerDriverEmail,
                UserName = attackerDriverEmail,
                DriverEmail = attackerDriverEmail,
                FirstName = "Rogue",
                LastName = "Driver",
                DateOfBirth = new DateTime(1989, 3, 3),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 10
            }, "Driver");
            attackerDriverId = attackerDrv.Id;

            var trip = new Trip
            {
                CustomerId = victimCustomerId,
                FromCity = "Oslo",
                ToCity = "Trondheim",
                FromAddress = "Storgata 1",
                ToAddress = "Olav Tryggvasons gate 2",
                FromZipCode = "0155",
                ToZipCode = "7011",
                Distance = 490,
                Weight = 40,
                Volume = 1.5,
                GoodsType = "Electronics",
                Notes = "Victim sensitive shipment",
                EstimatedPrice = 6000,
                AdminPrice = 6200,
                Status = TripStatus.PriceSet,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            victimTripId = trip.Id;
        }

        var attackerClient = await LoginClient(attackerCustomerEmail);

        // -------------------------------------------------------------
        // Probe 1: IDOR Trip Details Tampering -> Returns 404 Not Found
        // -------------------------------------------------------------
        var idorDetailsResp = await attackerClient.GetAsync($"/Customer/TripDetails/{victimTripId}");
        Assert.Equal(HttpStatusCode.NotFound, idorDetailsResp.StatusCode);

        var idorSuccessResp = await attackerClient.GetAsync($"/Customer/OrderSuccess/{victimTripId}");
        Assert.Equal(HttpStatusCode.NotFound, idorSuccessResp.StatusCode);

        // -------------------------------------------------------------
        // Probe 2: Stale Concurrent Price Update -> Returns 409 Conflict
        // -------------------------------------------------------------
        using (var controllerScope = _factory.Services.CreateScope())
        {
            var controller = ActivatorUtilities.CreateInstance<CustomerController>(controllerScope.ServiceProvider);
            var testDb = controllerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tempDataProvider = controllerScope.ServiceProvider.GetRequiredService<ITempDataProvider>();

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, victimCustomerId),
                new Claim(ClaimTypes.Role, "Customer")
            }, "TestAuth"));

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, tempDataProvider);

            // Read trip in current context
            var tripToRespond = await testDb.Trips.FindAsync(victimTripId);
            Assert.NotNull(tripToRespond);

            // Concurrently modify the trip in a background scope, bumping ConcurrencyToken
            using (var bgScope = _factory.Services.CreateScope())
            {
                var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var bgTrip = await bgDb.Trips.FindAsync(victimTripId);
                Assert.NotNull(bgTrip);
                bgTrip.Notes = "Concurrently modified by background process";
                await bgDb.SaveChangesAsync();
            }

            // Stale update attempt catches DbUpdateConcurrencyException and yields 409 Conflict
            var conflictResult = await controller.RespondToPrice(victimTripId, true);
            var objResult = Assert.IsType<ConflictObjectResult>(conflictResult);
            Assert.Equal((int)HttpStatusCode.Conflict, objResult.StatusCode);
            Assert.Contains("modified by another process", objResult.Value?.ToString());
        }

        // -------------------------------------------------------------
        // Probe 3: Unauthorized SignalR Subscribe -> Throws HubException
        // -------------------------------------------------------------
        using (var hubScope = _factory.Services.CreateScope())
        {
            var db = hubScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Attacker Customer attempt
            var attackerPrincipal = CreatePrincipal(attackerCustomerId, "Customer");
            var (hubAttacker, _, _) = CreateHub(db, attackerPrincipal, "conn-attacker-cust");

            var exCust = await Assert.ThrowsAsync<HubException>(() => hubAttacker.JoinTrip(victimTripId));
            Assert.Equal("You are not allowed to subscribe to this trip.", exCust.Message);

            // Rogue Driver attempt (not assigned to this trip)
            var rogueDriverPrincipal = CreatePrincipal(attackerDriverId, "Driver");
            var (hubDriver, _, _) = CreateHub(db, rogueDriverPrincipal, "conn-attacker-drv");

            var exDrv = await Assert.ThrowsAsync<HubException>(() => hubDriver.JoinTrip(victimTripId));
            Assert.Equal("You are not allowed to subscribe to this trip.", exDrv.Message);
        }

        // -------------------------------------------------------------
        // Probe 4: Unauthorized Location Broadcast -> Throws HubException
        // -------------------------------------------------------------
        using (var hubScope = _factory.Services.CreateScope())
        {
            var db = hubScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Attacker Customer attempt to broadcast GPS coordinates
            var attackerPrincipal = CreatePrincipal(attackerCustomerId, "Customer");
            var (hubAttacker, _, _) = CreateHub(db, attackerPrincipal, "conn-attacker-cust-bcast");

            var exCust = await Assert.ThrowsAsync<HubException>(() =>
                hubAttacker.SendLocationUpdate(victimTripId, 59.9139, 10.7522));
            Assert.Equal("You are not authorized to update location for this trip.", exCust.Message);

            // Rogue Driver attempt to broadcast GPS coordinates
            var rogueDriverPrincipal = CreatePrincipal(attackerDriverId, "Driver");
            var (hubDriver, _, _) = CreateHub(db, rogueDriverPrincipal, "conn-attacker-drv-bcast");

            var exDrv = await Assert.ThrowsAsync<HubException>(() =>
                hubDriver.SendLocationUpdate(victimTripId, 59.9139, 10.7522));
            Assert.Equal("You are not authorized to update location for this trip.", exDrv.Message);
        }

        // -------------------------------------------------------------
        // Probe 5: Unauthorized Invoice Download -> Returns 404 Not Found
        // -------------------------------------------------------------
        // Complete the victim's trip in database
        using (var completeScope = _factory.Services.CreateScope())
        {
            var db = completeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(victimTripId);
            Assert.NotNull(trip);
            trip.Status = TripStatus.Completed;
            await db.SaveChangesAsync();
        }

        // Attacker attempts to download victim's invoice
        var unauthorizedInvoiceResp = await attackerClient.GetAsync($"/Customer/DownloadInvoice/{victimTripId}");
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedInvoiceResp.StatusCode);

        // Rogue driver attempting to download via customer endpoint -> Forbidden or Redirect
        var rogueDriverClient = await LoginClient(attackerDriverEmail);
        var driverInvoiceResp = await rogueDriverClient.GetAsync($"/Customer/DownloadInvoice/{victimTripId}");
        Assert.True(
            driverInvoiceResp.StatusCode == HttpStatusCode.Forbidden ||
            driverInvoiceResp.StatusCode == HttpStatusCode.Redirect,
            $"Expected Forbidden or Redirect for Driver accessing Customer invoice, got: {driverInvoiceResp.StatusCode}");
    }

    // =========================================================================
    // Tier 4 Scenario 3: Live Driver Simulation & Multi-Tenant Isolation
    // =========================================================================
    [Fact]
    public async Task E2E_LiveDriverSimulationAndMultiTenantIsolation_GroupScoping()
    {
        int tripAlphaId, tripBetaId;
        string driverAlphaId, driverBetaId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var custAlpha = await EnsureUser(userManager, new Customer
            {
                Email = "tenant-alpha-cust@example.test",
                UserName = "tenant-alpha-cust@example.test",
                FirstName = "Alpha",
                LastName = "Customer",
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var custBeta = await EnsureUser(userManager, new Customer
            {
                Email = "tenant-beta-cust@example.test",
                UserName = "tenant-beta-cust@example.test",
                FirstName = "Beta",
                LastName = "Customer",
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var drvAlpha = await EnsureUser(userManager, new Driver
            {
                Email = "tenant-alpha-drv@example.test",
                UserName = "tenant-alpha-drv@example.test",
                DriverEmail = "tenant-alpha-drv@example.test",
                FirstName = "Alpha",
                LastName = "Driver",
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = false,
                PricePerKm = 10
            }, "Driver");
            driverAlphaId = drvAlpha.Id;

            var drvBeta = await EnsureUser(userManager, new Driver
            {
                Email = "tenant-beta-drv@example.test",
                UserName = "tenant-beta-drv@example.test",
                DriverEmail = "tenant-beta-drv@example.test",
                FirstName = "Beta",
                LastName = "Driver",
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = false,
                PricePerKm = 10
            }, "Driver");
            driverBetaId = drvBeta.Id;

            var adm = await EnsureUser(userManager, new Admin
            {
                Email = "tenant-admin@example.test",
                UserName = "tenant-admin@example.test",
                FirstName = "Tenant",
                LastName = "Admin",
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var tripAlpha = new Trip
            {
                CustomerId = custAlpha.Id,
                FromCity = "Oslo",
                ToCity = "Bergen",
                FromAddress = "Alpha St 1",
                ToAddress = "Alpha Dest 2",
                Distance = 460,
                Weight = 20,
                Volume = 1,
                Status = TripStatus.Assigned,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(tripAlpha);
            await db.SaveChangesAsync();
            tripAlphaId = tripAlpha.Id;

            var dtAlpha = new DriverTrip
            {
                TripId = tripAlphaId,
                DriverId = driverAlphaId,
                AssignedByAdminId = adm.Id,
                DriverPayment = 4600,
                Status = TripStatus.Assigned,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };
            db.DriverTrips.Add(dtAlpha);

            var tripBeta = new Trip
            {
                CustomerId = custBeta.Id,
                FromCity = "Trondheim",
                ToCity = "Stavanger",
                FromAddress = "Beta St 1",
                ToAddress = "Beta Dest 2",
                Distance = 600,
                Weight = 30,
                Volume = 1.2,
                Status = TripStatus.Assigned,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(tripBeta);
            await db.SaveChangesAsync();
            tripBetaId = tripBeta.Id;

            var dtBeta = new DriverTrip
            {
                TripId = tripBetaId,
                DriverId = driverBetaId,
                AssignedByAdminId = adm.Id,
                DriverPayment = 6000,
                Status = TripStatus.Assigned,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };
            db.DriverTrips.Add(dtBeta);
            await db.SaveChangesAsync();
        }

        using (var hubScope = _factory.Services.CreateScope())
        {
            var db = hubScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var drvAlphaPrincipal = CreatePrincipal(driverAlphaId, "Driver");

            var (hubAlpha, groupsAlpha, clientsAlpha) = CreateHub(db, drvAlphaPrincipal, "conn-drv-alpha");

            await hubAlpha.JoinTrip(tripAlphaId);

            // Driver Alpha simulates sequential GPS coordinates along route
            var waypoints = new[]
            {
                (59.9139, 10.7522),
                (60.1500, 8.2000),
                (60.3913, 5.3221)
            };

            foreach (var (lat, lng) in waypoints)
            {
                await hubAlpha.SendLocationUpdate(tripAlphaId, lat, lng);
            }

            // Verify Group Alpha received all 3 location updates
            var alphaGroupProxy = (TestClientProxy)clientsAlpha.Group($"trip_{tripAlphaId}");
            Assert.Equal(3, alphaGroupProxy.Messages.Count(m => m.method == "ReceiveLocationUpdate"));

            // Verify Group Beta received ZERO updates (strict isolation)
            var betaGroupProxy = (TestClientProxy)clientsAlpha.Group($"trip_{tripBetaId}");
            Assert.Empty(betaGroupProxy.Messages);

            // Verify Driver Alpha attempting to broadcast to Trip Beta is rejected
            var crossTenantEx = await Assert.ThrowsAsync<HubException>(() =>
                hubAlpha.SendLocationUpdate(tripBetaId, 60.0, 10.0));
            Assert.Equal("You are not authorized to update location for this trip.", crossTenantEx.Message);
        }
    }

    // =========================================================================
    // Tier 4 Scenario 4: Concurrent Price Conflict Resolution (HTTP Level)
    // =========================================================================
    [Fact]
    public async Task E2E_ConcurrentAdminPriceConflictResolution_StaleUpdateRejected_StateIntact()
    {
        const string customerEmail = "concurrent-e2e-cust@example.test";
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = customerEmail,
                UserName = customerEmail,
                FirstName = "Concurrent",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 5, 5),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = new Trip
            {
                CustomerId = cust.Id,
                FromCity = "Oslo",
                ToCity = "Kristiansand",
                FromAddress = "Storgata 10",
                ToAddress = "Markens gate 5",
                FromZipCode = "0155",
                ToZipCode = "4610",
                Distance = 320,
                Weight = 25,
                Volume = 1.0,
                GoodsType = "Commercial",
                EstimatedPrice = 4500,
                AdminPrice = 4800,
                Status = TripStatus.PriceSet,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var client = await LoginClient(customerEmail);
        var token = await GetToken(client, $"/Customer/TripDetails/{tripId}");

        // Dispatch concurrent conflicting Approve and Reject requests simultaneously
        var taskApprove = client.PostAsync("/Customer/RespondToPrice", Form(new()
        {
            ["id"] = tripId.ToString(),
            ["approved"] = "true",
            ["__RequestVerificationToken"] = token
        }));

        var taskReject = client.PostAsync("/Customer/RespondToPrice", Form(new()
        {
            ["id"] = tripId.ToString(),
            ["approved"] = "false",
            ["__RequestVerificationToken"] = token
        }));

        var responses = await Task.WhenAll(taskApprove, taskReject);

        // At least one request must be rejected (409 Conflict or 400 BadRequest)
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.BadRequest);

        // Database state must be cleanly either ApprovedByCustomer OR CanceledByCustomer, never corrupt
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tripInDb = await db.Trips.FindAsync(tripId);
            Assert.NotNull(tripInDb);
            Assert.True(
                tripInDb.Status == TripStatus.ApprovedByCustomer || tripInDb.Status == TripStatus.CanceledByCustomer,
                $"Expected status to be ApprovedByCustomer or CanceledByCustomer, but found {tripInDb.Status}");
        }
    }

    // =========================================================================
    // Tier 4 Scenario 5: Financial Summary & Itemized Breakdown Verification
    // =========================================================================
    [Fact]
    public void E2E_FinancialSummaryAndItemizedPdfInvoiceVerification()
    {
        var trip = new Trip
        {
            Id = 88001,
            CustomerId = "cust-fin-1",
            FromCity = "Oslo",
            ToCity = "Bergen",
            FromAddress = "Karenslyst Allé 2",
            ToAddress = "C. Sundts gate 10",
            FromZipCode = "0278",
            ToZipCode = "5004",
            Distance = 460.0,
            Weight = 50.0,
            Volume = 2.5,
            GoodsType = "Industrial Machinery",
            PickupTime = DateTime.UtcNow,
            EstimatedPrice = 8050.00,
            AdminPrice = 8200.00,
            Status = TripStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow
        };

        var customer = new Customer
        {
            Id = "cust-fin-1",
            FirstName = "Kari",
            LastName = "Nordmann",
            DisplayName = "Kari Nordmann",
            Email = "kari.nordmann@example.test",
            PhoneNumber = "+4740112233",
            CompanyName = "Fjord Industrial AS"
        };

        var service = new PdfInvoiceService();
        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 1024);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, 5));

        using var doc = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.ReadOnly);
        Assert.Equal("Invoice INV-088001", doc.Info.Title);
        Assert.Equal("Logi-Track", doc.Info.Author);
        Assert.Equal("Trip #88001 Shipment Invoice", doc.Info.Subject);
        Assert.Equal("Logi-Track Invoicing Service", doc.Info.Creator);

        var pageText = ExtractTextFromPage(doc.Pages[0]);

        // Validate Key Structural & Financial Elements
        Assert.Contains("LOGI-TRACK", pageText);
        Assert.Contains("INV-088001", pageText);
        Assert.Contains("Kari Nordmann", pageText);
        Assert.Contains("Fjord Industrial AS", pageText);
        Assert.Contains("ITEMIZED FARE BREAKDOWN", pageText);
        Assert.Contains("Base Freight Handling", pageText);
        Assert.Contains("Distance Transport Fee", pageText);
        Assert.Contains("Cargo Weight Surcharge", pageText);
        Assert.Contains("Service Handling & Dispatch Fee", pageText);
        Assert.Contains("TOTAL FINAL PRICE", pageText);
        Assert.Contains("PAYMENT STATUS: PAID IN FULL", pageText);
        Assert.Contains("MVA 25%", pageText);
    }
}
