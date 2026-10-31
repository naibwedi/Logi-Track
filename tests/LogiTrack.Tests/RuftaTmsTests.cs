using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using logirack.Data;
using logirack.Models;
using logirack.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogiTrack.Tests;

public sealed class RuftaTmsTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string AdminPassword = "IntegrationTest!2026";
    private const string AdminEmail = "rufta-admin@ruftalogistics.test";
    private const string DriverEmail = "rufta-driver@ruftalogistics.test";
    private const string OtherDriverEmail = "rufta-driver2@ruftalogistics.test";
    private const string CustomerEmail = "rufta-customer@ruftalogistics.test";

    public RuftaTmsTests(LogiTrackFactory factory)
    {
        _factory = factory;
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    private static async Task<string> GetToken(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }

    private async Task<HttpClient> LoginClient(string email, string password)
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

    private async Task SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.FindByEmailAsync(AdminEmail) == null)
        {
            var admin = new Admin
            {
                Email = AdminEmail,
                UserName = AdminEmail,
                FirstName = "Rufta",
                LastName = "Dispatcher",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            };
            var result = await users.CreateAsync(admin, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(admin, "Admin");
        }

        if (await users.FindByEmailAsync(DriverEmail) == null)
        {
            var driver = new Driver
            {
                Email = DriverEmail,
                UserName = DriverEmail,
                DriverEmail = DriverEmail,
                FirstName = "Robel",
                LastName = "Haile",
                DateOfBirth = new DateTime(1990, 4, 12),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 15.0,
                CdlNumber = "R420-8819-2201",
                CdlState = "IL",
                CdlExpirationDate = DateTime.UtcNow.AddYears(2),
                MedicalCardExpirationDate = DateTime.UtcNow.AddYears(1)
            };
            var result = await users.CreateAsync(driver, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(driver, "Driver");
        }

        if (await users.FindByEmailAsync(OtherDriverEmail) == null)
        {
            var driver2 = new Driver
            {
                Email = OtherDriverEmail,
                UserName = OtherDriverEmail,
                DriverEmail = OtherDriverEmail,
                FirstName = "Dawit",
                LastName = "Gebre",
                DateOfBirth = new DateTime(1991, 8, 20),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 15.0
            };
            var result = await users.CreateAsync(driver2, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(driver2, "Driver");
        }

        if (await users.FindByEmailAsync(CustomerEmail) == null)
        {
            var customer = new Customer
            {
                Email = CustomerEmail,
                UserName = CustomerEmail,
                FirstName = "Midwest",
                LastName = "Shipper",
                DateOfBirth = new DateTime(1988, 3, 15),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            };
            var result = await users.CreateAsync(customer, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(customer, "Customer");
        }
    }

    // ==========================================
    // 1. SettlementCalculator Unit Tests (R3)
    // ==========================================

    [Fact]
    public void SettlementCalculator_StandardLoad_CalculatesExact12And88Split()
    {
        // $2,500 Rate Con + $150 Detention + $50 Lumper = $2,700 Gross
        var result = SettlementCalculator.Calculate(
            rateConfirmation: 2500m,
            detention: 150m,
            lumper: 50m,
            otherAccessorials: 0m,
            deductions: 100m);

        Assert.Equal(2700.00m, result.GrossRate);
        Assert.Equal(200.00m, result.AccessorialsTotal);
        Assert.Equal(40.50m, result.FactoringFee);       // 1.5% of 2700
        Assert.Equal(324.00m, result.DispatchFee);       // 12% of 2700
        Assert.Equal(2376.00m, result.DriverGrossShare); // 88% of 2700
        Assert.Equal(100.00m, result.Deductions);
        Assert.Equal(2276.00m, result.DriverNetPay);     // 2376 - 100
        Assert.False(result.IsTonu);
    }

    [Theory]
    [InlineData(150.00)]
    [InlineData(175.00)]
    [InlineData(200.00)]
    public void SettlementCalculator_TonuWithinBounds_CalculatesCorrectSplit(decimal tonuAmount)
    {
        var breakdown = SettlementCalculator.CalculateTonu(tonuAmount, deductions: 0m);
        Assert.True(breakdown.IsTonu);
        Assert.Equal(tonuAmount, breakdown.GrossRate);
        Assert.Equal(Math.Round(tonuAmount * 0.015m, 2, MidpointRounding.AwayFromZero), breakdown.FactoringFee);
        Assert.Equal(Math.Round(tonuAmount * 0.12m, 2, MidpointRounding.AwayFromZero), breakdown.DispatchFee);
        Assert.Equal(tonuAmount - Math.Round(tonuAmount * 0.12m, 2, MidpointRounding.AwayFromZero), breakdown.DriverNetPay);
    }

    [Theory]
    [InlineData(149.99)]
    [InlineData(200.01)]
    [InlineData(0)]
    public void SettlementCalculator_TonuOutOfRange_ThrowsArgumentOutOfRangeException(decimal invalidTonu)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SettlementCalculator.CalculateTonu(invalidTonu));
    }

    // ==========================================
    // 2. Broker Normalization & CRM Tests (R2)
    // ==========================================

    [Theory]
    [InlineData("MC#322572", "322572")]
    [InlineData("mc-135797", "135797")]
    [InlineData("  590179 ", "590179")]
    public void Broker_NormalizeMc_StripsPrefixAndFormatting(string raw, string expected)
    {
        Assert.Equal(expected, Broker.NormalizeMc(raw));
        var broker = new Broker { McNumber = expected };
        Assert.Equal($"MC#{expected}", broker.DisplayMc);
    }

    [Fact]
    public async Task BrokerCrm_CreateAndToggleBlacklist_PersistsCorrectly()
    {
        await SeedUsersAsync();
        var client = await LoginClient(AdminEmail, AdminPassword);

        var createToken = await GetToken(client, "/Admin/CreateBroker");
        var createResp = await client.PostAsync("/Admin/CreateBroker", Form(new()
        {
            ["CompanyName"] = "C.H. Robinson Worldwide",
            ["McNumber"] = "MC#128725",
            ["AgentName"] = "Sarah Jenkins",
            ["Phone"] = "(800) 323-7587",
            ["PhoneExtension"] = "4412",
            ["Email"] = "sarah.j@chrobinson.test",
            ["IsBlacklisted"] = "false",
            ["__RequestVerificationToken"] = createToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, createResp.StatusCode);

        int brokerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var broker = await db.Brokers.FirstOrDefaultAsync(b => b.McNumber == "128725");
            Assert.NotNull(broker);
            Assert.Equal("C.H. Robinson Worldwide", broker!.CompanyName);
            Assert.False(broker.IsBlacklisted);
            brokerId = broker.Id;
        }

        // Flag as Bad Broker
        var listToken = await GetToken(client, "/Admin/Brokers");
        var flagResp = await client.PostAsync("/Admin/ToggleBrokerBlacklist", Form(new()
        {
            ["id"] = brokerId.ToString(),
            ["isBlacklisted"] = "true",
            ["reason"] = "Refused detention payout on Chicago load",
            ["__RequestVerificationToken"] = listToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, flagResp.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updated = await db.Brokers.FindAsync(brokerId);
            Assert.NotNull(updated);
            Assert.True(updated!.IsBlacklisted);
            Assert.Contains("Refused detention payout", updated.BlacklistReason);
            Assert.NotNull(updated.BlacklistedAt);
        }
    }

    // ==========================================
    // 3. DOT Safety & Compliance Guard Tests (R4)
    // ==========================================

    [Fact]
    public void ComplianceService_DetectsExpiredAndExpiringItemsAndIftaDates()
    {
        var today = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        // Q1 IFTA deadline is April 30
        Assert.Equal(new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc), ComplianceService.NextIftaDueDate(today));

        var expiredDriver = new Driver
        {
            Id = "d1",
            FirstName = "Expired",
            LastName = "MedDriver",
            MedicalCardExpirationDate = today.AddDays(-3),
            CdlExpirationDate = today.AddDays(10)
        };

        Assert.True(ComplianceService.IsMedicalCardExpired(expiredDriver, today));
        Assert.False(ComplianceService.IsCdlExpired(expiredDriver, today));

        var alerts = ComplianceService.GetAlerts(new[] { expiredDriver }, Array.Empty<Vehicle>(), today);
        Assert.Contains(alerts, a => a.Category == "DOT Medical Card" && a.Severity == ComplianceSeverity.Expired);
        Assert.Contains(alerts, a => a.Category == "CDL" && a.Severity == ComplianceSeverity.Warning);
        Assert.Contains(alerts, a => a.Category == "IFTA Quarterly Return");
    }

    [Fact]
    public async Task AssignTrip_DriverWithExpiredMedicalCard_IsBlockedWith49CfrError()
    {
        await SeedUsersAsync();
        int tripId;
        string expiredDriverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var customer = await db.Customers.FirstAsync(c => c.Email == CustomerEmail);

            var expiredDriver = new Driver
            {
                Email = "expired-med@ruftalogistics.test",
                UserName = "expired-med@ruftalogistics.test",
                DriverEmail = "expired-med@ruftalogistics.test",
                FirstName = "MedExpired",
                LastName = "Driver",
                DateOfBirth = new DateTime(1985, 5, 5),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 12.0,
                MedicalCardExpirationDate = DateTime.UtcNow.AddDays(-5)
            };
            await users.CreateAsync(expiredDriver, AdminPassword);
            await users.AddToRoleAsync(expiredDriver, "Driver");
            expiredDriverId = expiredDriver.Id;

            var trip = new Trip
            {
                CustomerId = customer.Id,
                FromCity = "Chicago",
                ToCity = "Atlanta",
                FromAddress = "100 Loop",
                ToAddress = "200 Peachtree",
                FromZipCode = "60601",
                ToZipCode = "30301",
                GoodsType = "Dry Van",
                Distance = 1150,
                Weight = 18000,
                Volume = 40,
                Status = TripStatus.ApprovedByCustomer,
                PickupTime = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var client = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(client, $"/Admin/AssignTrip/{tripId}");
        var resp = await client.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = expiredDriverId,
            ["FromCity"] = "Chicago",
            ["ToCity"] = "Atlanta",
            ["Distance"] = "1150",
            ["Weight"] = "18000",
            ["__RequestVerificationToken"] = token
        }));

        // Returns 200 View with validation error instead of redirecting
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("DOT Medical Card has expired", body);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstAsync(t => t.Id == tripId);
            Assert.Equal(TripStatus.ApprovedByCustomer, trip.Status);
            Assert.Null(trip.DriverTrip);
        }
    }

    // ==========================================
    // 4. End-to-End Settlement, TONU & PDF Voucher Tests
    // ==========================================

    [Fact]
    public async Task SettlementAndTonuWorkflow_UpdatesAccountingAndGeneratesPdfVoucher()
    {
        await SeedUsersAsync();
        int tripId;
        int brokerId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var customer = await db.Customers.FirstAsync(c => c.Email == CustomerEmail);
            var driver = await db.Drivers.FirstAsync(d => d.Email == DriverEmail);
            var admin = await db.Admins.FirstAsync(a => a.Email == AdminEmail);
            var broker = await db.Brokers.FirstAsync(b => b.McNumber == "322572");
            brokerId = broker.Id;

            var trip = new Trip
            {
                CustomerId = customer.Id,
                FromCity = "Chicago",
                ToCity = "Dallas",
                FromAddress = "100 Terminal Way",
                ToAddress = "500 Commerce Blvd",
                FromZipCode = "60601",
                ToZipCode = "75201",
                GoodsType = "Reefer",
                Distance = 1480,
                Weight = 19000,
                Volume = 50,
                Status = TripStatus.Assigned,
                AdminPrice = 3000,
                PickupTime = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            db.DriverTrips.Add(new DriverTrip
            {
                TripId = trip.Id,
                DriverId = driver.Id,
                AssignedByAdminId = admin.Id,
                Status = TripStatus.Assigned,
                DriverPayment = 2000,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var adminClient = await LoginClient(AdminEmail, AdminPassword);
        var detailsToken = await GetToken(client: adminClient, $"/Admin/TripDetails/{tripId}");

        // 1. Update Trip Settlement ($3,000 Rate Con + $200 Detention - $50 Deduction)
        var updateResp = await adminClient.PostAsync("/Admin/UpdateTripSettlement", Form(new()
        {
            ["tripId"] = tripId.ToString(),
            ["brokerId"] = brokerId.ToString(),
            ["invoiceNumber"] = $"IN-{tripId:D4}",
            ["brokerLoadNumber"] = "23044790",
            ["rateConfirmationAmount"] = "3000.00",
            ["detentionFee"] = "200.00",
            ["lumperFee"] = "0.00",
            ["otherAccessorials"] = "0.00",
            ["deductions"] = "50.00",
            ["deductionReason"] = "Cash Advance",
            ["__RequestVerificationToken"] = detailsToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, updateResp.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dt = await db.DriverTrips.Include(x => x.Trip).FirstAsync(x => x.TripId == tripId);
            // Gross = 3200, 12% Dispatch = 384, 88% Share = 2816, minus 50 = 2766.00
            Assert.Equal(2766.00, dt.DriverPayment, 2);
            Assert.Equal("23044790", dt.Trip.BrokerLoadNumber);
        }

        // 2. Download Settlement PDF as Admin
        var adminPdfResp = await adminClient.GetAsync($"/Admin/DownloadSettlement/{tripId}");
        Assert.Equal(HttpStatusCode.OK, adminPdfResp.StatusCode);
        Assert.Equal("application/pdf", adminPdfResp.Content.Headers.ContentType?.MediaType);
        var pdfBytes = await adminPdfResp.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, 5));

        // 3. Download Settlement PDF as Assigned Driver (Authorized) vs Other Driver (IDOR Blocked)
        var driverClient = await LoginClient(DriverEmail, AdminPassword);
        var driverPdfResp = await driverClient.GetAsync($"/Driver/DownloadSettlement/{tripId}");
        Assert.Equal(HttpStatusCode.OK, driverPdfResp.StatusCode);

        var otherDriverClient = await LoginClient(OtherDriverEmail, AdminPassword);
        var idorResp = await otherDriverClient.GetAsync($"/Driver/DownloadSettlement/{tripId}");
        Assert.Equal(HttpStatusCode.NotFound, idorResp.StatusCode);

        // 4. Mark Trip as TONU ($150)
        var tonuToken = await GetToken(adminClient, $"/Admin/TripDetails/{tripId}");
        var tonuResp = await adminClient.PostAsync("/Admin/MarkTripTonu", Form(new()
        {
            ["tripId"] = tripId.ToString(),
            ["tonuAmount"] = "150.00",
            ["deductions"] = "0.00",
            ["__RequestVerificationToken"] = tonuToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, tonuResp.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstAsync(t => t.Id == tripId);
            Assert.Equal(TripStatus.Tonu, trip.Status);
            Assert.Equal(150.00m, trip.TonuAmount);
            // 88% of $150 = $132.00
            Assert.Equal(132.00, trip.DriverTrip!.DriverPayment, 2);
        }
    }
}
