using System.Net;
using System.Text.RegularExpressions;
using logirack.Data;
using logirack.Models;
using logirack.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace LogiTrack.Tests;

public sealed class ChallengerInvoiceAccessControlStressTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public ChallengerInvoiceAccessControlStressTests(LogiTrackFactory factory)
    {
        _factory = factory;
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

    private static async Task EnsureUser(UserManager<ApplicationUser> users, ApplicationUser user, string role, string password = DefaultPassword)
    {
        if (await users.FindByEmailAsync(user.Email!) is not null) return;
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        result = await users.AddToRoleAsync(user, role);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private async Task<(string customerAlphaId, string customerBetaId, string adminId, string driverId)> SeedChallengerUsersAsync()
    {
        const string alphaEmail = "challenger-alpha@example.test";
        const string betaEmail = "challenger-beta@example.test";
        const string adminEmail = "challenger-admin@example.test";
        const string driverEmail = "challenger-driver@example.test";

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await EnsureUser(userManager, new Customer
        {
            Email = alphaEmail,
            UserName = alphaEmail,
            FirstName = "Alpha",
            LastName = "Customer",
            CompanyName = "Alpha Logistics AS",
            PhoneNumber = "+4791000001",
            DateOfBirth = new DateTime(1990, 1, 1),
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        await EnsureUser(userManager, new Customer
        {
            Email = betaEmail,
            UserName = betaEmail,
            FirstName = "Beta",
            LastName = "Customer",
            CompanyName = "Beta Freighting",
            PhoneNumber = "+4791000002",
            DateOfBirth = new DateTime(1991, 2, 2),
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        await EnsureUser(userManager, new Admin
        {
            Email = adminEmail,
            UserName = adminEmail,
            FirstName = "Challenger",
            LastName = "Admin",
            CompanyName = "Logi-Track Internal Audit",
            DateOfBirth = new DateTime(1985, 3, 3),
            RoleType = "Admin",
            IsApproved = true,
            EmailConfirmed = true
        }, "Admin");

        await EnsureUser(userManager, new Driver
        {
            Email = driverEmail,
            UserName = driverEmail,
            DriverEmail = driverEmail,
            FirstName = "Challenger",
            LastName = "Driver",
            PhoneNumber = "+4791000003",
            DateOfBirth = new DateTime(1988, 4, 4),
            RoleType = "Driver",
            IsApproved = true,
            EmailConfirmed = true,
            IsAvailable = true,
            PricePerKm = 15.0
        }, "Driver");

        var alpha = (await userManager.FindByEmailAsync(alphaEmail))!;
        var beta = (await userManager.FindByEmailAsync(betaEmail))!;
        var admin = (await userManager.FindByEmailAsync(adminEmail))!;
        var driver = (await userManager.FindByEmailAsync(driverEmail))!;

        return (alpha.Id, beta.Id, admin.Id, driver.Id);
    }

    private async Task<int> CreateTripAsync(string customerId, TripStatus status, double estimatedPrice = 1500, double adminPrice = 1600)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var trip = new Trip
        {
            CustomerId = customerId,
            FromCity = "Oslo",
            ToCity = "Bergen",
            FromAddress = "Dronning Eufemias gate 8",
            ToAddress = "Strandgaten 20",
            FromZipCode = "0191",
            ToZipCode = "5004",
            Weight = 65.0,
            Volume = 3.2,
            Distance = 465,
            Notes = "Adversarial Challenge Trip",
            GoodsType = "Industrial Spare Parts",
            PickupTime = DateTime.UtcNow.AddDays(1),
            EstimatedPrice = estimatedPrice,
            AdminPrice = adminPrice,
            Status = status,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow
        };

        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    // =========================================================================
    // 1. IDOR VERIFICATION: Cross-Customer Download Protection
    // =========================================================================

    [Fact]
    public async Task CustomerBeta_AttemptingToDownload_CustomerAlphaInvoice_ReturnsNotFound_IdorBlocked()
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var alphaTripId = await CreateTripAsync(alphaId, TripStatus.Completed);

        var betaClient = await LoginClient("challenger-beta@example.test");
        var response = await betaClient.GetAsync($"/Customer/DownloadInvoice/{alphaTripId}");

        // Must strictly return 404 NotFound
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CustomerAlpha_AttemptingToDownload_CustomerBetaInvoice_ReturnsNotFound_IdorBlocked()
    {
        var (_, betaId, _, _) = await SeedChallengerUsersAsync();
        var betaTripId = await CreateTripAsync(betaId, TripStatus.Completed);

        var alphaClient = await LoginClient("challenger-alpha@example.test");
        var response = await alphaClient.GetAsync($"/Customer/DownloadInvoice/{betaTripId}");

        // Must strictly return 404 NotFound
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CustomerAlpha_DownloadingOwnCompletedInvoice_SucceedsWithValidPdf()
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var alphaTripId = await CreateTripAsync(alphaId, TripStatus.Completed);

        var alphaClient = await LoginClient("challenger-alpha@example.test");
        var response = await alphaClient.GetAsync($"/Customer/DownloadInvoice/{alphaTripId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"Invoice_Trip_{alphaTripId}.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        var magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", magic);
    }

    // =========================================================================
    // 2. NON-COMPLETED TRIP STATUS MATRIX: Customer & Admin Endpoints
    // =========================================================================

    [Theory]
    [InlineData(TripStatus.Requested)]
    [InlineData(TripStatus.ApprovedByAdmin)]
    [InlineData(TripStatus.PriceSet)]
    [InlineData(TripStatus.ApprovedByCustomer)]
    [InlineData(TripStatus.Assigned)]
    [InlineData(TripStatus.InProgress)]
    [InlineData(TripStatus.CancelledByAdmin)]
    [InlineData(TripStatus.CanceledByCustomer)]
    public async Task Customer_DownloadingInvoice_ForNonCompletedTrip_ReturnsBadRequest(TripStatus nonCompletedStatus)
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var tripId = await CreateTripAsync(alphaId, nonCompletedStatus);

        var alphaClient = await LoginClient("challenger-alpha@example.test");
        var response = await alphaClient.GetAsync($"/Customer/DownloadInvoice/{tripId}");

        // Must reject download for non-completed trip with 400 BadRequest
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(TripStatus.Requested)]
    [InlineData(TripStatus.ApprovedByAdmin)]
    [InlineData(TripStatus.PriceSet)]
    [InlineData(TripStatus.ApprovedByCustomer)]
    [InlineData(TripStatus.Assigned)]
    [InlineData(TripStatus.InProgress)]
    [InlineData(TripStatus.CancelledByAdmin)]
    [InlineData(TripStatus.CanceledByCustomer)]
    public async Task Admin_DownloadingInvoice_ForNonCompletedTrip_ReturnsBadRequest(TripStatus nonCompletedStatus)
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var tripId = await CreateTripAsync(alphaId, nonCompletedStatus);

        var adminClient = await LoginClient("challenger-admin@example.test");
        var response = await adminClient.GetAsync($"/Admin/DownloadInvoice/{tripId}");

        // Admin must also be rejected with 400 BadRequest for non-completed trips
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =========================================================================
    // 3. NON-EXISTENT TRIP IDS: Boundary & Fuzzing
    // =========================================================================

    [Theory]
    [InlineData(999999)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Customer_DownloadingInvoice_ForNonExistentTrip_ReturnsNotFound(int invalidTripId)
    {
        await SeedChallengerUsersAsync();
        var alphaClient = await LoginClient("challenger-alpha@example.test");
        var response = await alphaClient.GetAsync($"/Customer/DownloadInvoice/{invalidTripId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(999999)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Admin_DownloadingInvoice_ForNonExistentTrip_ReturnsNotFound(int invalidTripId)
    {
        await SeedChallengerUsersAsync();
        var adminClient = await LoginClient("challenger-admin@example.test");
        var response = await adminClient.GetAsync($"/Admin/DownloadInvoice/{invalidTripId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // 4. UNAUTHENTICATED ANONYMOUS REQUESTS: Auth Challenge / Redirect
    // =========================================================================

    [Fact]
    public async Task AnonymousRequest_ToCustomerInvoice_RedirectsToLogin()
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var tripId = await CreateTripAsync(alphaId, TripStatus.Completed);

        var anonymousClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var response = await anonymousClient.GetAsync($"/Customer/DownloadInvoice/{tripId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/Identity/Account/Login", location);
    }

    [Fact]
    public async Task AnonymousRequest_ToAdminInvoice_RedirectsToLogin()
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var tripId = await CreateTripAsync(alphaId, TripStatus.Completed);

        var anonymousClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var response = await anonymousClient.GetAsync($"/Admin/DownloadInvoice/{tripId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/Identity/Account/Login", location);
    }

    // =========================================================================
    // 5. CROSS-ROLE BOUNDARY DEFENSE: Drivers & Customers in Unauthorized Roles
    // =========================================================================

    [Fact]
    public async Task Driver_CannotAccess_CustomerDownloadInvoice()
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var tripId = await CreateTripAsync(alphaId, TripStatus.Completed);

        var driverClient = await LoginClient("challenger-driver@example.test");
        var response = await driverClient.GetAsync($"/Customer/DownloadInvoice/{tripId}");

        // Role restriction: Driver is not in "Customer" role -> 403 Forbidden or redirect to AccessDenied
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Redirect && (response.Headers.Location?.ToString().Contains("AccessDenied") ?? false)),
            $"Expected 403 or AccessDenied redirect, got {response.StatusCode} to {response.Headers.Location}");
    }

    [Fact]
    public async Task Driver_CannotAccess_AdminDownloadInvoice()
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var tripId = await CreateTripAsync(alphaId, TripStatus.Completed);

        var driverClient = await LoginClient("challenger-driver@example.test");
        var response = await driverClient.GetAsync($"/Admin/DownloadInvoice/{tripId}");

        // Role restriction: Driver is not in "Admin" role -> 403 Forbidden or redirect to AccessDenied
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Redirect && (response.Headers.Location?.ToString().Contains("AccessDenied") ?? false)),
            $"Expected 403 or AccessDenied redirect, got {response.StatusCode} to {response.Headers.Location}");
    }

    [Fact]
    public async Task Customer_CannotAccess_AdminDownloadInvoice()
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var tripId = await CreateTripAsync(alphaId, TripStatus.Completed);

        var alphaClient = await LoginClient("challenger-alpha@example.test");
        var response = await alphaClient.GetAsync($"/Admin/DownloadInvoice/{tripId}");

        // Customer is not in "Admin" role -> 403 Forbidden or redirect to AccessDenied
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Redirect && (response.Headers.Location?.ToString().Contains("AccessDenied") ?? false)),
            $"Expected 403 or AccessDenied redirect, got {response.StatusCode} to {response.Headers.Location}");
    }

    // =========================================================================
    // 6. STRESS TEST: PDF Engine Edge Cases, Unicode & Structural Integrity
    // =========================================================================

    [Fact]
    public void PdfInvoiceService_HandlesUnicodeAndSpecialCharacters_WithoutCrash()
    {
        var service = new PdfInvoiceService();
        var trip = new Trip
        {
            Id = 98765,
            CustomerId = "cust-unicode",
            FromCity = "Tromsø",
            ToCity = "Ålesund",
            FromAddress = "Storgata 100, Nærøysund",
            ToAddress = "Bærum Verk 4",
            FromZipCode = "9008",
            ToZipCode = "6002",
            Weight = 120.75,
            Volume = 4.85,
            Distance = 1120.4,
            GoodsType = "Fersk Laks & Sjømat (Cold Cargo 0-2°C)",
            PickupTime = DateTime.UtcNow,
            EstimatedPrice = 18500.0,
            AdminPrice = 17900.0, // Discounted from estimate
            Status = TripStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };

        var customer = new Customer
        {
            Id = "cust-unicode",
            FirstName = "Bjørn",
            LastName = "Håkonsson",
            Email = "bjorn.hakon@havbruk.test",
            PhoneNumber = "+47 98 76 54 32",
            CompanyName = "Nordic Arctic Fisheries & Co. AS"
        };

        var bytes = service.GenerateInvoicePdf(trip, customer);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1000);

        // Verify PDF Magic Bytes
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", header);

        // Verify PDF Parser can inspect the output document
        using var ms = new MemoryStream(bytes);
        var pdf = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        Assert.Equal("Invoice INV-098765", pdf.Info.Title);
        Assert.Equal("Logi-Track", pdf.Info.Author);
        Assert.Equal("Trip #98765 Shipment Invoice", pdf.Info.Subject);
        Assert.True(pdf.PageCount >= 1);
    }

    [Fact]
    public void PdfInvoiceService_HandlesNullCustomerOptionalFields_WithoutNullReferenceException()
    {
        var service = new PdfInvoiceService();
        var trip = new Trip
        {
            Id = 1,
            CustomerId = "cust-sparse",
            FromCity = "Oslo",
            ToCity = "Drammen",
            FromAddress = "Karl Johans gate",
            ToAddress = "Bragernes Torg",
            FromZipCode = "0154",
            ToZipCode = "3017",
            Weight = 5.0,
            Volume = 0.1,
            Distance = 42.0,
            GoodsType = "Documents",
            PickupTime = DateTime.UtcNow,
            EstimatedPrice = 850.0,
            AdminPrice = 0.0, // AdminPrice not set (fallback to estimated)
            Status = TripStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };

        // Customer with null company, empty phone, empty display name
        var customer = new Customer
        {
            Id = "cust-sparse",
            FirstName = "Solo",
            LastName = "User",
            Email = "solo@example.test",
            PhoneNumber = null,
            CompanyName = null!
        };

        var bytes = service.GenerateInvoicePdf(trip, customer);
        using var ms = new MemoryStream(bytes);
        var pdf = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        Assert.True(pdf.PageCount >= 1);
    }

    // =========================================================================
    // 7. SECURITY & EDGE CASES: Unapproved Users, Detached Customers, Method Rejection
    // =========================================================================

    [Fact]
    public async Task UnapprovedCustomer_CannotDownloadInvoice_RedirectsToApprovalPending()
    {
        const string unapprovedEmail = "unapproved-customer@example.test";
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await EnsureUser(userManager, new Customer
            {
                Email = unapprovedEmail,
                UserName = unapprovedEmail,
                FirstName = "Pending",
                LastName = "Customer",
                CompanyName = "Pending AS",
                PhoneNumber = "+4799000000",
                DateOfBirth = new DateTime(1995, 5, 5),
                RoleType = "Customer",
                IsApproved = false,
                EmailConfirmed = true
            }, "Customer");
        }

        var client = await LoginClient(unapprovedEmail);
        var response = await client.GetAsync("/Customer/DownloadInvoice/1");

        // ApprovedOnly attribute should intercept and redirect to ApprovalPending
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("ApprovalPending", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Admin_CanDownloadInvoice_ForCompletedTrip_WithMinimalCustomerProfile()
    {
        const string minimalEmail = "minimal-customer@example.test";
        string minimalCustomerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await EnsureUser(userManager, new Customer
            {
                Email = minimalEmail,
                UserName = minimalEmail,
                FirstName = "Minimal",
                LastName = "User",
                CompanyName = "",
                PhoneNumber = "",
                DateOfBirth = new DateTime(1996, 6, 6),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            var user = (await userManager.FindByEmailAsync(minimalEmail))!;
            minimalCustomerId = user.Id;
        }

        var tripId = await CreateTripAsync(minimalCustomerId, TripStatus.Completed);

        var adminClient = await LoginClient("challenger-admin@example.test");
        var response = await adminClient.GetAsync($"/Admin/DownloadInvoice/{tripId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task Customer_PostRequest_ToDownloadInvoiceEndpoint_IsRejected()
    {
        var (alphaId, _, _, _) = await SeedChallengerUsersAsync();
        var tripId = await CreateTripAsync(alphaId, TripStatus.Completed);

        var alphaClient = await LoginClient("challenger-alpha@example.test");
        var response = await alphaClient.PostAsync($"/Customer/DownloadInvoice/{tripId}", new StringContent(string.Empty));

        // Action is [HttpGet] only; POST should return 400, 404, or 405 (not 200 OK)
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}

