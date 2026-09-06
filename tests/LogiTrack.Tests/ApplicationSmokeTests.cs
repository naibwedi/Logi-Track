using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using logirack.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using logirack.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LogiTrack.Tests;

public sealed class ApplicationSmokeTests : IClassFixture<LogiTrackFactory>
{
    private readonly HttpClient _client;
    private readonly LogiTrackFactory _factory;

    public ApplicationSmokeTests(LogiTrackFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Approved_roles_can_sign_in_and_open_their_workspaces()
    {
        const string password = "IntegrationTest!2026";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await EnsureUser(users, new Admin { Email = "admin@example.test", UserName = "admin@example.test", FirstName = "Test", LastName = "Admin", DateOfBirth = new DateTime(1990, 1, 1), RoleType = "Admin", IsApproved = true, EmailConfirmed = true }, "Admin", password);
            await EnsureUser(users, new Driver { Email = "driver@example.test", UserName = "driver@example.test", DriverEmail = "driver@example.test", FirstName = "Test", LastName = "Driver", DateOfBirth = new DateTime(1990, 1, 1), RoleType = "Driver", IsApproved = true, EmailConfirmed = true, IsAvailable = true, PricePerKm = 10 }, "Driver", password);
            await EnsureUser(users, new Customer { Email = "customer@example.test", UserName = "customer@example.test", FirstName = "Test", LastName = "Customer", DateOfBirth = new DateTime(1990, 1, 1), RoleType = "Customer", IsApproved = true, EmailConfirmed = true }, "Customer", password);
        }

        await AssertWorkspace("admin@example.test", password, "/Admin/Dashboard", "/Admin/DriverManagment", "/Admin/TripRequests", "/Admin/TripLog", "/Admin/PaymentManagement", "/Admin/InvoiceGeneration");
        await AssertWorkspace("driver@example.test", password, "/Driver/Dashboard", "/Driver/CurrentTrips", "/Driver/TripLog", "/Driver/PaymentTracking");
        await AssertWorkspace("customer@example.test", password, "/Customer/Dashboard", "/Customer/CreateTrip", "/Customer/MyTrips");
    }

    [Fact]
    public async Task Complete_trip_and_payment_lifecycle_succeeds()
    {
        const string password = "IntegrationTest!2026";
        const string adminEmail = "lifecycle-admin@example.test";
        const string driverEmail = "lifecycle-driver@example.test";
        const string customerEmail = "lifecycle-customer@example.test";
        string driverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await EnsureUser(users, new Admin { Email = adminEmail, UserName = adminEmail, FirstName = "Flow", LastName = "Admin", DateOfBirth = new DateTime(1990, 1, 1), RoleType = "Admin", IsApproved = true, EmailConfirmed = true }, "Admin", password);
            await EnsureUser(users, new Driver { Email = driverEmail, UserName = driverEmail, DriverEmail = driverEmail, FirstName = "Flow", LastName = "Driver", DateOfBirth = new DateTime(1990, 1, 1), RoleType = "Driver", IsApproved = true, EmailConfirmed = true, IsAvailable = true, PricePerKm = 12 }, "Driver", password);
            await EnsureUser(users, new Customer { Email = customerEmail, UserName = customerEmail, FirstName = "Flow", LastName = "Customer", DateOfBirth = new DateTime(1990, 1, 1), RoleType = "Customer", IsApproved = true, EmailConfirmed = true }, "Customer", password);
            driverId = (await users.FindByEmailAsync(driverEmail))!.Id;
        }

        var customer = await LoginClient(customerEmail, password);
        var createToken = await GetToken(customer, "/Customer/CreateTrip");
        var create = await customer.PostAsync("/Customer/CreateTrip", Form(new()
        {
            ["FromCity"]="Oslo", ["ToCity"]="Bergen", ["FromAddress"]="Testveien 1", ["ToAddress"]="Testgata 2",
            ["FromZipCode"]="0150", ["ToZipCode"]="5003", ["Weight"]="100", ["Volume"]="2", ["Distance"]="460",
            ["GoodType"]="Test goods", ["PickupDate"]=DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm"), ["Notes"]="Automated lifecycle", ["__RequestVerificationToken"]=createToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        int tripId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            tripId = await db.Trips.Where(t => t.Customer.Email == customerEmail).OrderByDescending(t => t.Id).Select(t => t.Id).FirstAsync();
        }

        var admin = await LoginClient(adminEmail, password);
        var token = await GetToken(admin, $"/Admin/TripDetails/{tripId}");
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Admin/ReviewTrip", Form(new() { ["tId"]=tripId.ToString(), ["isApproved"]="true", ["__RequestVerificationToken"]=token }))).StatusCode);
        token = await GetToken(admin, $"/Admin/SetPrice/{tripId}");
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Admin/SetPrice", Form(new() { ["TripId"]=tripId.ToString(), ["AdminPrice"]="7500", ["__RequestVerificationToken"]=token }))).StatusCode);

        token = await GetToken(customer, $"/Customer/TripDetails/{tripId}");
        Assert.Equal(HttpStatusCode.Redirect, (await customer.PostAsync("/Customer/RespondToPrice", Form(new() { ["id"]=tripId.ToString(), ["approved"]="true", ["__RequestVerificationToken"]=token }))).StatusCode);

        token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Admin/AssignTrip", Form(new() { ["TripId"]=tripId.ToString(), ["DriverId"]=driverId, ["__RequestVerificationToken"]=token }))).StatusCode);

        var driver = await LoginClient(driverEmail, password);
        token = await GetToken(driver, "/Driver/CurrentTrips");
        Assert.Equal(HttpStatusCode.Redirect, (await driver.PostAsync("/Driver/EndTrip", Form(new() { ["tripId"]=tripId.ToString(), ["__RequestVerificationToken"]=token }))).StatusCode);

        token = await GetToken(admin, "/Admin/PaymentManagement");
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Admin/PreparePayments", Form(new() { ["__RequestVerificationToken"]=token }))).StatusCode);

        using var finalScope = _factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(TripStatus.Completed, (await finalDb.Trips.FindAsync(tripId))!.Status);
        Assert.True(await finalDb.Payments.AnyAsync(p => p.DriverId == driverId && p.Amount == 5520));
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
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var token = await GetToken(client, "/Identity/Account/Login");
        var login = await client.PostAsync("/Identity/Account/Login", Form(new() { ["Input.Email"] = email, ["Input.Password"] = password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private async Task AssertWorkspace(string email, string password, params string[] paths)
    {
        var client = await LoginClient(email, password);
        foreach (var path in paths)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    private static async Task EnsureUser(UserManager<ApplicationUser> users, ApplicationUser user, string role, string password)
    {
        if (await users.FindByEmailAsync(user.Email!) is not null) return;
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        result = await users.AddToRoleAsync(user, role);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    [Fact]
    public async Task Home_page_is_available()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_database_connectivity()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_routes_render_a_branded_not_found_page()
    {
        var response = await _client.GetAsync("/this-route-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/Admin/Dashboard")]
    [InlineData("/Driver/Dashboard")]
    [InlineData("/Customer/Dashboard")]
    [InlineData("/SuperAdmin/Dashboard")]
    [InlineData("/Admin/PaymentManagement")]
    [InlineData("/Admin/InvoiceGeneration")]
    [InlineData("/Admin/Invoice/1")]
    [InlineData("/Driver/PaymentTracking")]
    public async Task Protected_pages_challenge_anonymous_users(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SignalR_hub_rejects_anonymous_connections()
    {
        var response = await _client.PostAsync("/tripHub/negotiate?negotiateVersion=1", null);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });
    }

    [Theory]
    [InlineData("/Home/Services")]
    [InlineData("/Home/Contact")]
    [InlineData("/Home/Privacy")]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    public async Task Public_pages_are_available(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Home_page_does_not_advertise_unimplemented_telematics()
    {
        var html = await _client.GetStringAsync("/");
        Assert.DoesNotContain("Real-Time GPS Tracking", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Route Optimization", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Driver Safety Monitoring", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Contact_form_validates_and_accepts_a_message()
    {
        var page = await _client.GetStringAsync("/Home/Contact");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        var invalid = await _client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "not-an-email", ["message"] = "short", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Enter a valid email address", await invalid.Content.ReadAsStringAsync());

        page = await _client.GetStringAsync("/Home/Contact");
        token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var valid = await _client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "customer@example.test", ["message"] = "Please contact me about a delivery.", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Equal("/Home/Contact", valid.Headers.Location?.OriginalString);
    }
}

public sealed class LogiTrackFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"logitrack-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_databasePath}",
                ["SendGrid:ApiKey"] = string.Empty,
                ["SendGrid:FromEmail"] = string.Empty,
                ["BootstrapAdmin:Email"] = "superadmin@example.test",
                ["BootstrapAdmin:Password"] = "IntegrationTest!2026"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // SQLite may keep the pooled connection briefly; the OS temp directory
        // safely cleans up the per-run database after the process exits.
    }
}
