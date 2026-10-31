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

public sealed class InvoiceGenerationTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public InvoiceGenerationTests(LogiTrackFactory factory)
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

    private async Task<(string customerAId, string customerBId, string adminId)> SeedInvoiceUsersAsync()
    {
        const string customerAEmail = "invoice-customerA@example.test";
        const string customerBEmail = "invoice-customerB@example.test";
        const string adminEmail = "invoice-admin@example.test";

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await EnsureUser(userManager, new Customer
        {
            Email = customerAEmail,
            UserName = customerAEmail,
            FirstName = "Alice",
            LastName = "Customer",
            CompanyName = "Nordic Freight AS",
            PhoneNumber = "+4798765432",
            DateOfBirth = new DateTime(1992, 5, 10),
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        await EnsureUser(userManager, new Customer
        {
            Email = customerBEmail,
            UserName = customerBEmail,
            FirstName = "Bob",
            LastName = "Customer",
            CompanyName = "Fjord Cargo",
            PhoneNumber = "+4791234567",
            DateOfBirth = new DateTime(1994, 8, 20),
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        await EnsureUser(userManager, new Admin
        {
            Email = adminEmail,
            UserName = adminEmail,
            FirstName = "Admin",
            LastName = "Manager",
            CompanyName = "Logi-Track HQ",
            DateOfBirth = new DateTime(1985, 3, 15),
            RoleType = "Admin",
            IsApproved = true,
            EmailConfirmed = true
        }, "Admin");

        var userA = (await userManager.FindByEmailAsync(customerAEmail))!;
        var userB = (await userManager.FindByEmailAsync(customerBEmail))!;
        var admin = (await userManager.FindByEmailAsync(adminEmail))!;

        return (userA.Id, userB.Id, admin.Id);
    }

    private async Task<int> CreateTripAsync(string customerId, TripStatus status, double estimatedPrice = 1200, double adminPrice = 1250)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var trip = new Trip
        {
            CustomerId = customerId,
            FromCity = "Oslo",
            ToCity = "Bergen",
            FromAddress = "Storgata 1",
            ToAddress = "Bryggen 15",
            FromZipCode = "0155",
            ToZipCode = "5003",
            Weight = 45.5,
            Volume = 2.4,
            Distance = 460,
            Notes = "Fragile electronics pallet",
            GoodsType = "Electronics",
            PickupTime = DateTime.UtcNow.AddDays(2),
            EstimatedPrice = estimatedPrice,
            AdminPrice = adminPrice,
            Status = status,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            UpdatedAt = DateTime.UtcNow
        };

        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    // -------------------------------------------------------------
    // 1. Pure Unit / Service Tests for PdfInvoiceService
    // -------------------------------------------------------------

    [Fact]
    public void GenerateInvoicePdf_ReturnsNonEmptyStreamStartingWithPdfMagicBytes()
    {
        var service = new PdfInvoiceService();
        var trip = new Trip
        {
            Id = 42,
            CustomerId = "cust-1",
            FromCity = "Oslo",
            ToCity = "Bergen",
            FromAddress = "Storgata 1",
            ToAddress = "Bryggen 15",
            FromZipCode = "0155",
            ToZipCode = "5003",
            Weight = 50,
            Volume = 1.2,
            Distance = 460,
            GoodsType = "Electronics",
            PickupTime = DateTime.UtcNow,
            EstimatedPrice = 1200,
            AdminPrice = 1250,
            Status = TripStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        var customer = new Customer
        {
            Id = "cust-1",
            FirstName = "Alice",
            LastName = "Customer",
            Email = "alice@example.test",
            PhoneNumber = "+4798765432",
            CompanyName = "Alice Imports"
        };

        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        Assert.NotNull(pdfBytes);
        Assert.NotEmpty(pdfBytes);
        Assert.True(pdfBytes.Length > 1000, "Generated PDF bytes should have substantial size");

        // Verify PDF Magic Bytes (%PDF-)
        var header = System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void GenerateInvoicePdf_PopulatesExpectedDocumentMetadata()
    {
        var service = new PdfInvoiceService();
        var trip = new Trip
        {
            Id = 1234,
            CustomerId = "cust-2",
            FromCity = "Trondheim",
            ToCity = "Stavanger",
            FromAddress = "Torget 5",
            ToAddress = "Havnegata 12",
            FromZipCode = "7011",
            ToZipCode = "4005",
            Weight = 80,
            Volume = 3.5,
            Distance = 750,
            GoodsType = "Machinery Parts",
            PickupTime = DateTime.UtcNow,
            EstimatedPrice = 3500,
            AdminPrice = 3600,
            Status = TripStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };

        var customer = new Customer
        {
            Id = "cust-2",
            FirstName = "Bob",
            LastName = "Smith",
            Email = "bob@example.test",
            PhoneNumber = "+4791234567"
        };

        var pdfBytes = service.GenerateInvoicePdf(trip, customer);

        using var ms = new MemoryStream(pdfBytes);
        var pdfDoc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);

        Assert.Equal("Invoice INV-001234", pdfDoc.Info.Title);
        Assert.Equal("Logi-Track", pdfDoc.Info.Author);
        Assert.Equal("Trip #1234 Shipment Invoice", pdfDoc.Info.Subject);
        Assert.False(string.IsNullOrWhiteSpace(pdfDoc.Info.Producer), "Producer metadata should be populated");
        Assert.True(pdfDoc.PageCount >= 1, "Invoice must contain at least 1 page");
    }

    [Fact]
    public void GenerateInvoicePdf_ThrowsArgumentNullException_ForNullArguments()
    {
        var service = new PdfInvoiceService();
        var trip = new Trip { Id = 1 };
        var customer = new Customer { Id = "c1" };

        Assert.Throws<ArgumentNullException>(() => service.GenerateInvoicePdf(null!, customer));
        Assert.Throws<ArgumentNullException>(() => service.GenerateInvoicePdf(trip, null!));
    }

    // -------------------------------------------------------------
    // 2. Integration Tests: Customer Endpoint
    // -------------------------------------------------------------

    [Fact]
    public async Task Customer_CanDownloadInvoice_ForOwnCompletedTrip()
    {
        var (customerAId, _, _) = await SeedInvoiceUsersAsync();
        var tripId = await CreateTripAsync(customerAId, TripStatus.Completed);

        var client = await LoginClient("invoice-customerA@example.test");
        var response = await client.GetAsync($"/Customer/DownloadInvoice/{tripId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        // Content-Disposition check
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal($"Invoice_Trip_{tripId}.pdf", disposition.FileName?.Trim('"'));

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length > 1000);
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public async Task Customer_CannotDownloadInvoice_ForAnotherCustomersTrip_IdorProtection()
    {
        var (customerAId, _, _) = await SeedInvoiceUsersAsync();
        var tripAId = await CreateTripAsync(customerAId, TripStatus.Completed);

        // Customer B attempts to download Customer A's invoice
        var bobClient = await LoginClient("invoice-customerB@example.test");
        var response = await bobClient.GetAsync($"/Customer/DownloadInvoice/{tripAId}");

        // IDOR protection: Must return 404 NotFound
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
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
    public async Task Customer_CannotDownloadInvoice_ForNonCompletedTrip(TripStatus nonCompletedStatus)
    {
        var (customerAId, _, _) = await SeedInvoiceUsersAsync();
        var tripId = await CreateTripAsync(customerAId, nonCompletedStatus);

        var client = await LoginClient("invoice-customerA@example.test");
        var response = await client.GetAsync($"/Customer/DownloadInvoice/{tripId}");

        // Must reject download for non-completed trip (400 BadRequest)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------
    // 3. Integration Tests: Admin Endpoint
    // -------------------------------------------------------------

    [Fact]
    public async Task Admin_CanDownloadInvoice_ForAnyCompletedTrip()
    {
        var (customerAId, _, _) = await SeedInvoiceUsersAsync();
        var tripId = await CreateTripAsync(customerAId, TripStatus.Completed);

        var adminClient = await LoginClient("invoice-admin@example.test");
        var response = await adminClient.GetAsync($"/Admin/DownloadInvoice/{tripId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal($"Invoice_Trip_{tripId}.pdf", disposition.FileName?.Trim('"'));

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length > 1000);
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public async Task Admin_CannotDownloadInvoice_ForNonCompletedTrip()
    {
        var (customerAId, _, _) = await SeedInvoiceUsersAsync();
        var tripId = await CreateTripAsync(customerAId, TripStatus.InProgress);

        var adminClient = await LoginClient("invoice-admin@example.test");
        var response = await adminClient.GetAsync($"/Admin/DownloadInvoice/{tripId}");

        // Must reject download for non-completed trip (400 BadRequest)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DownloadInvoice_ReturnsNotFound_ForNonExistentTrip()
    {
        var adminClient = await LoginClient("invoice-admin@example.test");
        var response = await adminClient.GetAsync("/Admin/DownloadInvoice/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var customerClient = await LoginClient("invoice-customerA@example.test");
        var customerResponse = await customerClient.GetAsync("/Customer/DownloadInvoice/999999");
        Assert.Equal(HttpStatusCode.NotFound, customerResponse.StatusCode);
    }

    [Fact]
    public async Task AnonymousUser_CannotDownloadInvoice_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var customerResponse = await client.GetAsync("/Customer/DownloadInvoice/1");
        Assert.Equal(HttpStatusCode.Redirect, customerResponse.StatusCode);
        Assert.Contains("/Identity/Account/Login", customerResponse.Headers.Location?.ToString());

        var adminResponse = await client.GetAsync("/Admin/DownloadInvoice/1");
        Assert.Equal(HttpStatusCode.Redirect, adminResponse.StatusCode);
        Assert.Contains("/Identity/Account/Login", adminResponse.Headers.Location?.ToString());
    }
}
