using System.Net;
using System.Text.RegularExpressions;
using logirack.Data;
using logirack.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogiTrack.Tests;

public sealed class AccessControlAndIdorTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public AccessControlAndIdorTests(LogiTrackFactory factory)
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

    private async Task<(string customerAId, string customerBId, string driverId)> SeedUsersAsync()
    {
        const string customerAEmail = "idor-customerA@example.test";
        const string customerBEmail = "idor-customerB@example.test";
        const string driverEmail = "idor-driver@example.test";

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await EnsureUser(userManager, new Customer
        {
            Email = customerAEmail,
            UserName = customerAEmail,
            FirstName = "Alice",
            LastName = "Customer",
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
            DateOfBirth = new DateTime(1994, 8, 20),
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        await EnsureUser(userManager, new Driver
        {
            Email = driverEmail,
            UserName = driverEmail,
            DriverEmail = driverEmail,
            FirstName = "Dave",
            LastName = "Driver",
            DateOfBirth = new DateTime(1988, 3, 15),
            RoleType = "Driver",
            IsApproved = true,
            EmailConfirmed = true,
            IsAvailable = true,
            PricePerKm = 10
        }, "Driver");

        var userA = (await userManager.FindByEmailAsync(customerAEmail))!;
        var userB = (await userManager.FindByEmailAsync(customerBEmail))!;
        var driver = (await userManager.FindByEmailAsync(driverEmail))!;

        return (userA.Id, userB.Id, driver.Id);
    }

    private async Task<int> CreateTripForCustomerAsync(string customerId, TripStatus status = TripStatus.Requested, double adminPrice = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var trip = new Trip
        {
            CustomerId = customerId,
            FromCity = "Oslo",
            ToCity = "Drammen",
            FromAddress = "Storgata 10",
            ToAddress = "Bragernes Torg 1",
            FromZipCode = "0155",
            ToZipCode = "3017",
            Weight = 50,
            Volume = 1.5,
            Distance = 45,
            Notes = "Test delivery notes",
            GoodsType = "Electronics",
            PickupTime = DateTime.UtcNow.AddDays(1),
            EstimatedPrice = 1200,
            AdminPrice = adminPrice,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    [Fact]
    public async Task Customer_AccessingAnotherCustomers_OrderSuccess_ReturnsNotFound404_Never200Or302()
    {
        var (customerAId, _, _) = await SeedUsersAsync();
        var tripAId = await CreateTripForCustomerAsync(customerAId);

        // Bob (Customer B) attempts to view Alice's (Customer A) OrderSuccess page
        var bobClient = await LoginClient("idor-customerB@example.test");
        var response = await bobClient.GetAsync($"/Customer/OrderSuccess/{tripAId}");

        // IDOR Verification: Must return 404 Not Found, never 200 OK and never 302 Redirect to Dashboard
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Customer_AccessingAnotherCustomers_TripDetails_ReturnsNotFound404_Never200Or302()
    {
        var (customerAId, _, _) = await SeedUsersAsync();
        var tripAId = await CreateTripForCustomerAsync(customerAId);

        // Bob attempts to view Alice's TripDetails
        var bobClient = await LoginClient("idor-customerB@example.test");
        var response = await bobClient.GetAsync($"/Customer/TripDetails/{tripAId}");

        // Must return 404 Not Found, never 200 OK and never 302 Redirect
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Customer_AccessingOwnTrip_OrderSuccessAndTripDetails_Returns200OK()
    {
        var (customerAId, _, _) = await SeedUsersAsync();
        var tripAId = await CreateTripForCustomerAsync(customerAId);

        // Alice (Owner) accesses her own trip
        var aliceClient = await LoginClient("idor-customerA@example.test");

        var orderSuccessResp = await aliceClient.GetAsync($"/Customer/OrderSuccess/{tripAId}");
        Assert.Equal(HttpStatusCode.OK, orderSuccessResp.StatusCode);

        var tripDetailsResp = await aliceClient.GetAsync($"/Customer/TripDetails/{tripAId}");
        Assert.Equal(HttpStatusCode.OK, tripDetailsResp.StatusCode);
    }

    [Fact]
    public async Task Customer_AccessingNonExistentTrip_ReturnsNotFound404()
    {
        await SeedUsersAsync();
        var aliceClient = await LoginClient("idor-customerA@example.test");

        var orderSuccessResp = await aliceClient.GetAsync("/Customer/OrderSuccess/999999");
        Assert.Equal(HttpStatusCode.NotFound, orderSuccessResp.StatusCode);

        var tripDetailsResp = await aliceClient.GetAsync("/Customer/TripDetails/999999");
        Assert.Equal(HttpStatusCode.NotFound, tripDetailsResp.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public async Task Customer_AccessingInvalidTripId_ReturnsNotFound404(int invalidId)
    {
        await SeedUsersAsync();
        var aliceClient = await LoginClient("idor-customerA@example.test");

        var orderSuccessResp = await aliceClient.GetAsync($"/Customer/OrderSuccess/{invalidId}");
        Assert.Equal(HttpStatusCode.NotFound, orderSuccessResp.StatusCode);

        var tripDetailsResp = await aliceClient.GetAsync($"/Customer/TripDetails/{invalidId}");
        Assert.Equal(HttpStatusCode.NotFound, tripDetailsResp.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotRespondToPrice_OnAnotherCustomersTrip()
    {
        var (customerAId, _, _) = await SeedUsersAsync();
        // Alice has a trip in PriceSet status
        var tripAId = await CreateTripForCustomerAsync(customerAId, TripStatus.PriceSet, adminPrice: 2500);

        // Bob attempts to maliciously respond (approve) Alice's trip
        var bobClient = await LoginClient("idor-customerB@example.test");
        var token = await GetToken(bobClient, "/Customer/CreateTrip");

        var attackResponse = await bobClient.PostAsync("/Customer/RespondToPrice", Form(new()
        {
            ["id"] = tripAId.ToString(),
            ["approved"] = "true",
            ["__RequestVerificationToken"] = token
        }));

        // Must reject access: either 404 Not Found or 405 from pipeline handler, never 200 OK or 302 Redirect to MyTrips
        Assert.True(attackResponse.StatusCode == HttpStatusCode.NotFound || attackResponse.StatusCode == HttpStatusCode.MethodNotAllowed);
        Assert.NotEqual(HttpStatusCode.OK, attackResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, attackResponse.StatusCode);

        // Verify in database: Alice's trip status was NOT modified
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await db.Trips.FindAsync(tripAId);
        Assert.NotNull(trip);
        Assert.Equal(TripStatus.PriceSet, trip.Status);
    }

    [Fact]
    public async Task DriverRole_AccessingCustomerEndpoints_IsDeniedAccess()
    {
        var (customerAId, _, _) = await SeedUsersAsync();
        var tripAId = await CreateTripForCustomerAsync(customerAId);

        var driverClient = await LoginClient("idor-driver@example.test");

        var orderSuccessResp = await driverClient.GetAsync($"/Customer/OrderSuccess/{tripAId}");
        // Denied: returns 403 Forbidden or redirects to AccessDenied
        Assert.True(orderSuccessResp.StatusCode == HttpStatusCode.Forbidden ||
                    (orderSuccessResp.StatusCode == HttpStatusCode.Redirect &&
                     orderSuccessResp.Headers.Location?.OriginalString.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) == true));
        Assert.NotEqual(HttpStatusCode.OK, orderSuccessResp.StatusCode);

        var tripDetailsResp = await driverClient.GetAsync($"/Customer/TripDetails/{tripAId}");
        Assert.True(tripDetailsResp.StatusCode == HttpStatusCode.Forbidden ||
                    (tripDetailsResp.StatusCode == HttpStatusCode.Redirect &&
                     tripDetailsResp.Headers.Location?.OriginalString.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) == true));
        Assert.NotEqual(HttpStatusCode.OK, tripDetailsResp.StatusCode);
    }

    [Fact]
    public async Task CustomerRole_AccessingAdminAndDriverDashboards_IsDeniedAccess()
    {
        await SeedUsersAsync();
        var aliceClient = await LoginClient("idor-customerA@example.test");

        var adminResp = await aliceClient.GetAsync("/Admin/Dashboard");
        Assert.True(adminResp.StatusCode == HttpStatusCode.Forbidden ||
                    (adminResp.StatusCode == HttpStatusCode.Redirect &&
                     adminResp.Headers.Location?.OriginalString.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) == true));

        var driverResp = await aliceClient.GetAsync("/Driver/Dashboard");
        Assert.True(driverResp.StatusCode == HttpStatusCode.Forbidden ||
                    (driverResp.StatusCode == HttpStatusCode.Redirect &&
                     driverResp.Headers.Location?.OriginalString.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) == true));
    }

    [Fact]
    public async Task Customer_MyTrips_OnlyDisplaysOwnTrips()
    {
        var (customerAId, customerBId, _) = await SeedUsersAsync();
        var tripAId = await CreateTripForCustomerAsync(customerAId);
        var tripBId = await CreateTripForCustomerAsync(customerBId);

        // Bob requests MyTrips list
        var bobClient = await LoginClient("idor-customerB@example.test");
        var response = await bobClient.GetAsync("/Customer/MyTrips");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        // Bob's trip should be visible
        Assert.Contains($"/Customer/TripDetails/{tripBId}", html);
        // Alice's trip MUST NOT be visible in Bob's list
        Assert.DoesNotContain($"/Customer/TripDetails/{tripAId}", html);
    }

    [Fact]
    public async Task AnonymousUser_AccessingCustomerEndpoints_RedirectsToLogin302()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/Customer/OrderSuccess/1");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location?.OriginalString);

        var response2 = await client.GetAsync("/Customer/TripDetails/1");
        Assert.Equal(HttpStatusCode.Redirect, response2.StatusCode);
        Assert.Contains("/Identity/Account/Login", response2.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateTrip_SanitizesXssPayloads_BeforePersistingToDatabase()
    {
        await SeedUsersAsync();
        var aliceClient = await LoginClient("idor-customerA@example.test");
        var token = await GetToken(aliceClient, "/Customer/CreateTrip");

        const string maliciousNotes = "<script>alert('pwned')</script>Handle with care <iframe src='http://bad.org'></iframe>";
        const string maliciousFromAddress = "<script>steal()</script>Kongens gate 1";
        const string maliciousToAddress = "<img src=x onerror=alert(1)>Dronningens gate 5";
        const string maliciousGoodType = "<b>Fragile Glass</b>";

        var createResponse = await aliceClient.PostAsync("/Customer/CreateTrip", Form(new()
        {
            ["FromCity"] = "Oslo",
            ["ToCity"] = "Bergen",
            ["FromAddress"] = maliciousFromAddress,
            ["ToAddress"] = maliciousToAddress,
            ["FromZipCode"] = "0150",
            ["ToZipCode"] = "5003",
            ["Weight"] = "50",
            ["Volume"] = "1.5",
            ["Distance"] = "460",
            ["GoodType"] = maliciousGoodType,
            ["PickupDate"] = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-ddTHH:mm"),
            ["Notes"] = maliciousNotes,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await db.Trips
            .Where(t => t.Customer.Email == "idor-customerA@example.test")
            .OrderByDescending(t => t.Id)
            .FirstAsync();

        // Verify HTML tags were stripped and dangerous attributes neutralized
        Assert.DoesNotContain("<script", trip.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", trip.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", trip.FromAddress, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", trip.ToAddress, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", trip.ToAddress, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>", trip.GoodsType, StringComparison.OrdinalIgnoreCase);

        // Verify preserved sanitized textual content
        Assert.Contains("Handle with care", trip.Notes);
        Assert.Contains("Kongens gate 1", trip.FromAddress);
        Assert.Contains("Dronningens gate 5", trip.ToAddress);
        Assert.Contains("Fragile Glass", trip.GoodsType);
    }
}
