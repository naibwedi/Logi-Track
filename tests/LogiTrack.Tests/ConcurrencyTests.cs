using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using logirack.Controllers;
using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogiTrack.Tests;

public sealed class ConcurrencyTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public ConcurrencyTests(LogiTrackFactory factory)
    {
        _factory = factory;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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

    private static Trip CreateTripEntity(string customerId, TripStatus status = TripStatus.Requested)
    {
        return new Trip
        {
            CustomerId = customerId,
            FromCity = "Oslo",
            ToCity = "Bergen",
            FromAddress = "Storgata 1",
            ToAddress = "Bryggen 2",
            FromZipCode = "0150",
            ToZipCode = "5003",
            Weight = 50,
            Volume = 2,
            Distance = 460,
            Notes = "Initial concurrency notes",
            GoodsType = "General Cargo",
            Status = status,
            EstimatedPrice = 5000,
            AdminPrice = status >= TripStatus.PriceSet ? 6000 : 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // =========================================================================
    // 1. ConcurrencyToken Auto-Update & Integrity Verification
    // =========================================================================

    [Fact]
    public async Task SavingModifiedTrip_AutomaticallyUpdatesConcurrencyToken()
    {
        string customerId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "token-customer@example.test",
                UserName = "token-customer@example.test",
                FirstName = "Token",
                LastName = "Customer",
                DateOfBirth = new DateTime(1992, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            customerId = cust.Id;
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var trip = CreateTripEntity(customerId);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var initialToken = trip.ConcurrencyToken;
        var initialUpdatedAt = trip.UpdatedAt;
        Assert.NotEqual(Guid.Empty, initialToken);

        // Modify entity and save
        trip.Notes = "Updated Notes by user 1";
        await db.SaveChangesAsync();

        var updatedToken = trip.ConcurrencyToken;
        var updatedUpdatedAt = trip.UpdatedAt;

        Assert.NotEqual(Guid.Empty, updatedToken);
        Assert.NotEqual(initialToken, updatedToken);
        Assert.True(updatedUpdatedAt >= initialUpdatedAt);
    }

    [Fact]
    public async Task SavingModifiedDriverTrip_AutomaticallyUpdatesConcurrencyToken()
    {
        int driverTripId;
        Guid initialToken;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "dt-admin@example.test",
                UserName = "dt-admin@example.test",
                FirstName = "DT",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = "dt-driver@example.test",
                UserName = "dt-driver@example.test",
                DriverEmail = "dt-driver@example.test",
                FirstName = "DT",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 10
            }, "Driver");

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "dt-customer@example.test",
                UserName = "dt-customer@example.test",
                FirstName = "DT",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.ApprovedByCustomer);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                DriverId = driver.Id,
                AssignedByAdminId = admin.Id,
                DriverPayment = 4600,
                Status = TripStatus.Assigned,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };
            db.DriverTrips.Add(driverTrip);
            await db.SaveChangesAsync();

            driverTripId = driverTrip.Id;
            initialToken = driverTrip.ConcurrencyToken;
            Assert.NotEqual(Guid.Empty, initialToken);
        }

        using (var updateScope = _factory.Services.CreateScope())
        {
            var db = updateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dt = await db.DriverTrips.FindAsync(driverTripId);
            Assert.NotNull(dt);

            dt.Status = TripStatus.InProgress;
            await db.SaveChangesAsync();

            Assert.NotEqual(initialToken, dt.ConcurrencyToken);
        }
    }

    // =========================================================================
    // 2. DbUpdateConcurrencyException on Stale Entities (Trip & DriverTrip)
    // =========================================================================

    [Fact]
    public async Task ConcurrentTripUpdates_TriggerDbUpdateConcurrencyException_AndPreventCorruption()
    {
        int tripId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "race-cust@example.test",
                UserName = "race-cust@example.test",
                FirstName = "Race",
                LastName = "Customer",
                DateOfBirth = new DateTime(1991, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.Requested);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tripInScope1 = await db1.Trips.FindAsync(tripId);
        var tripInScope2 = await db2.Trips.FindAsync(tripId);

        Assert.NotNull(tripInScope1);
        Assert.NotNull(tripInScope2);
        Assert.Equal(tripInScope1.ConcurrencyToken, tripInScope2.ConcurrencyToken);

        // User 1 updates status to ApprovedByAdmin and commits
        tripInScope1.Status = TripStatus.ApprovedByAdmin;
        tripInScope1.Notes = "Approved by Admin 1";
        await db1.SaveChangesAsync();

        // User 2 (with stale original ConcurrencyToken) attempts to overwrite with CancelledByAdmin
        tripInScope2.Status = TripStatus.CancelledByAdmin;
        tripInScope2.Notes = "Cancelled by Admin 2";

        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await db2.SaveChangesAsync();
        });

        Assert.NotNull(exception);

        // Verify database reflects User 1's commit and was NOT corrupted by User 2's stale update
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedTrip = await verifyDb.Trips.FindAsync(tripId);
        Assert.NotNull(persistedTrip);
        Assert.Equal(TripStatus.ApprovedByAdmin, persistedTrip.Status);
        Assert.Equal("Approved by Admin 1", persistedTrip.Notes);
    }

    [Fact]
    public async Task ConcurrentDriverTripUpdates_TriggerDbUpdateConcurrencyException_AndPreventCorruption()
    {
        int driverTripId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "dt-race-admin@example.test",
                UserName = "dt-race-admin@example.test",
                FirstName = "DTRace",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = "dt-race-driver@example.test",
                UserName = "dt-race-driver@example.test",
                DriverEmail = "dt-race-driver@example.test",
                FirstName = "DTRace",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = false,
                PricePerKm = 10
            }, "Driver");

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "dt-race-cust@example.test",
                UserName = "dt-race-cust@example.test",
                FirstName = "DTRace",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.Assigned);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                DriverId = driver.Id,
                AssignedByAdminId = admin.Id,
                DriverPayment = 4600,
                Status = TripStatus.Assigned,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };
            db.DriverTrips.Add(driverTrip);
            await db.SaveChangesAsync();
            driverTripId = driverTrip.Id;
        }

        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dt1 = await db1.DriverTrips.FindAsync(driverTripId);
        var dt2 = await db2.DriverTrips.FindAsync(driverTripId);

        Assert.NotNull(dt1);
        Assert.NotNull(dt2);
        Assert.Equal(dt1.ConcurrencyToken, dt2.ConcurrencyToken);

        // Driver starts trip (transitions to InProgress)
        dt1.Status = TripStatus.InProgress;
        await db1.SaveChangesAsync();

        // Stale process attempts to complete trip
        dt2.Status = TripStatus.Completed;

        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await db2.SaveChangesAsync();
        });

        Assert.NotNull(exception);

        // Verify persisted state reflects InProgress, not Completed
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedDt = await verifyDb.DriverTrips.FindAsync(driverTripId);
        Assert.NotNull(persistedDt);
        Assert.Equal(TripStatus.InProgress, persistedDt.Status);
    }

    // =========================================================================
    // 3. Controller Exception Handling & Conflict Rejection
    // =========================================================================

    [Fact]
    public async Task AdminController_ReviewTrip_ConcurrencyConflict_ReturnsConflict409()
    {
        int tripId;
        string adminId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "review-concurrency-admin@example.test",
                UserName = "review-concurrency-admin@example.test",
                FirstName = "Review",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = admin.Id;

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "review-concurrency-cust@example.test",
                UserName = "review-concurrency-cust@example.test",
                FirstName = "Review",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.Requested);
            setupDb.Trips.Add(trip);
            await setupDb.SaveChangesAsync();
            tripId = trip.Id;
        }

        using var scope = _factory.Services.CreateScope();
        var controller = ActivatorUtilities.CreateInstance<AdminController>(scope.ServiceProvider);
        var testDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tempDataProvider = scope.ServiceProvider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminId),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider);

        // Load trip in controller's context
        var tripToReview = await testDb.Trips.FindAsync(tripId);
        Assert.NotNull(tripToReview);

        // Concurrently modify the trip in the database
        using (var bgScope = _factory.Services.CreateScope())
        {
            var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bgTrip = await bgDb.Trips.FindAsync(tripId);
            bgTrip!.Notes = "Concurrently updated";
            await bgDb.SaveChangesAsync();
        }

        // Controller reviews trip with stale token -> catches DbUpdateConcurrencyException -> Conflict 409
        var result = await controller.ReviewTrip(tripId, true);
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.Conflict, conflictResult.StatusCode);
        Assert.Contains("modified by another process", conflictResult.Value?.ToString());
    }

    [Fact]
    public async Task CustomerController_RespondToPrice_ConcurrencyConflict_ReturnsConflict409()
    {
        int tripId;
        string custId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "respond-concurrency-cust@example.test",
                UserName = "respond-concurrency-cust@example.test",
                FirstName = "Respond",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            custId = cust.Id;

            var trip = CreateTripEntity(cust.Id, TripStatus.PriceSet);
            setupDb.Trips.Add(trip);
            await setupDb.SaveChangesAsync();
            tripId = trip.Id;
        }

        using var scope = _factory.Services.CreateScope();
        var controller = ActivatorUtilities.CreateInstance<CustomerController>(scope.ServiceProvider);
        var testDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tempDataProvider = scope.ServiceProvider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, custId),
            new Claim(ClaimTypes.Role, "Customer")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider);

        // Load trip
        var tripToRespond = await testDb.Trips.FindAsync(tripId);
        Assert.NotNull(tripToRespond);

        // Concurrently modify trip in background
        using (var bgScope = _factory.Services.CreateScope())
        {
            var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bgTrip = await bgDb.Trips.FindAsync(tripId);
            bgTrip!.Notes = "Concurrently updated";
            await bgDb.SaveChangesAsync();
        }

        // Controller responds to price with stale token -> catches DbUpdateConcurrencyException -> Conflict 409
        var result = await controller.RespondToPrice(tripId, true);
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.Conflict, conflictResult.StatusCode);
        Assert.Contains("modified by another process", conflictResult.Value?.ToString());
    }

    [Fact]
    public async Task DriverController_EndTrip_ConcurrencyConflict_ReturnsConflict409()
    {
        int tripId;
        string driverId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "end-concurrency-admin@example.test",
                UserName = "end-concurrency-admin@example.test",
                FirstName = "End",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = "end-concurrency-driver@example.test",
                UserName = "end-concurrency-driver@example.test",
                DriverEmail = "end-concurrency-driver@example.test",
                FirstName = "End",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = false,
                PricePerKm = 10
            }, "Driver");
            driverId = driver.Id;

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "end-concurrency-cust@example.test",
                UserName = "end-concurrency-cust@example.test",
                FirstName = "End",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.InProgress);
            setupDb.Trips.Add(trip);
            await setupDb.SaveChangesAsync();

            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                DriverId = driver.Id,
                AssignedByAdminId = admin.Id,
                DriverPayment = 4600,
                Status = TripStatus.InProgress,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };
            setupDb.DriverTrips.Add(driverTrip);
            await setupDb.SaveChangesAsync();
            tripId = trip.Id;
        }

        using var scope = _factory.Services.CreateScope();
        var controller = ActivatorUtilities.CreateInstance<DriverController>(scope.ServiceProvider);
        var testDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tempDataProvider = scope.ServiceProvider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, driverId),
            new Claim(ClaimTypes.Role, "Driver")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider);

        // Load driver trip
        var dt = await testDb.DriverTrips.FirstOrDefaultAsync(d => d.TripId == tripId && d.DriverId == driverId);
        Assert.NotNull(dt);

        // Background modification
        using (var bgScope = _factory.Services.CreateScope())
        {
            var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bgDt = await bgDb.DriverTrips.FirstOrDefaultAsync(d => d.TripId == tripId && d.DriverId == driverId);
            bgDt!.UpdateAt = DateTime.UtcNow.AddMinutes(1);
            await bgDb.SaveChangesAsync();
        }

        // DriverController.EndTrip -> catches DbUpdateConcurrencyException -> Conflict 409
        var result = await controller.EndTrip(tripId);
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.Conflict, conflictResult.StatusCode);
        Assert.Contains("modified by another process", conflictResult.Value?.ToString());
    }

    // =========================================================================
    // 4. Stress-Test: Race Conditions on AdminController.AssignTrip
    // =========================================================================

    [Fact]
    public async Task AdminController_AssignTrip_ConcurrentRequests_SameDriver_PreventsDoubleAssignment()
    {
        const string adminEmail = "race-assign-admin@example.test";
        const string driverEmail = "race-assign-driver@example.test";
        string driverId;
        int trip1Id;
        int trip2Id;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await EnsureUser(userManager, new Admin
            {
                Email = adminEmail,
                UserName = adminEmail,
                FirstName = "AssignRace",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = driverEmail,
                UserName = driverEmail,
                DriverEmail = driverEmail,
                FirstName = "SingleAssignment",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 15
            }, "Driver");
            driverId = driver.Id;

            // Ensure driver is marked available
            driver.IsAvailable = true;
            await db.SaveChangesAsync();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "race-assign-cust@example.test",
                UserName = "race-assign-cust@example.test",
                FirstName = "AssignRace",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip1 = CreateTripEntity(cust.Id, TripStatus.ApprovedByCustomer);
            var trip2 = CreateTripEntity(cust.Id, TripStatus.ApprovedByCustomer);
            db.Trips.AddRange(trip1, trip2);
            await db.SaveChangesAsync();

            trip1Id = trip1.Id;
            trip2Id = trip2.Id;
        }

        // Two admin clients attempting to assign the same driver to trip1 and trip2 simultaneously
        var admin1 = await LoginClient(adminEmail);
        var admin2 = await LoginClient(adminEmail);

        var token1 = await GetToken(admin1, $"/Admin/AssignTrip/{trip1Id}");
        var token2 = await GetToken(admin2, $"/Admin/AssignTrip/{trip2Id}");

        var task1 = admin1.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = trip1Id.ToString(),
            ["DriverId"] = driverId,
            ["__RequestVerificationToken"] = token1
        }));

        var task2 = admin2.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = trip2Id.ToString(),
            ["DriverId"] = driverId,
            ["__RequestVerificationToken"] = token2
        }));

        var responses = await Task.WhenAll(task1, task2);

        // Verification:
        // One request must succeed (Redirect 302).
        // The other request MUST NOT create a second assignment.
        int successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Redirect);
        Assert.True(successCount <= 1, $"CRITICAL RACE CONDITION: Driver was assigned multiple times! Success count: {successCount}");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var driverTrips = await verifyDb.DriverTrips.Where(dt => dt.DriverId == driverId).ToListAsync();
        Assert.True(driverTrips.Count <= 1, $"Database corruption: Driver {driverId} has {driverTrips.Count} assignments, expected at most 1!");

        var driverEntity = await verifyDb.Drivers.FindAsync(driverId);
        Assert.NotNull(driverEntity);
        if (driverTrips.Count == 1)
        {
            Assert.False(driverEntity.IsAvailable, "Driver must be marked unavailable after assignment.");
        }
    }

    [Fact]
    public async Task AdminController_AssignTrip_ConcurrentRequests_SameTrip_PreventsDoubleAssignment()
    {
        const string adminEmail = "race-trip-admin@example.test";
        string driver1Id;
        string driver2Id;
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await EnsureUser(userManager, new Admin
            {
                Email = adminEmail,
                UserName = adminEmail,
                FirstName = "TripRace",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var driver1 = await EnsureUser(userManager, new Driver
            {
                Email = "race-driver1@example.test",
                UserName = "race-driver1@example.test",
                DriverEmail = "race-driver1@example.test",
                FirstName = "Driver",
                LastName = "One",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 10
            }, "Driver");
            driver1Id = driver1.Id;

            var driver2 = await EnsureUser(userManager, new Driver
            {
                Email = "race-driver2@example.test",
                UserName = "race-driver2@example.test",
                DriverEmail = "race-driver2@example.test",
                FirstName = "Driver",
                LastName = "Two",
                DateOfBirth = new DateTime(1989, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 12
            }, "Driver");
            driver2Id = driver2.Id;

            driver1.IsAvailable = true;
            driver2.IsAvailable = true;
            await db.SaveChangesAsync();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "race-trip-cust@example.test",
                UserName = "race-trip-cust@example.test",
                FirstName = "TripRace",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.ApprovedByCustomer);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var admin1 = await LoginClient(adminEmail);
        var admin2 = await LoginClient(adminEmail);

        var token1 = await GetToken(admin1, $"/Admin/AssignTrip/{tripId}");
        var token2 = await GetToken(admin2, $"/Admin/AssignTrip/{tripId}");

        // Admin 1 tries to assign Driver 1, Admin 2 tries to assign Driver 2 to the same trip
        var task1 = admin1.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driver1Id,
            ["__RequestVerificationToken"] = token1
        }));

        var task2 = admin2.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driver2Id,
            ["__RequestVerificationToken"] = token2
        }));

        var responses = await Task.WhenAll(task1, task2);

        int successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Redirect);
        Assert.True(successCount <= 1, $"CRITICAL RACE CONDITION: Trip {tripId} was assigned to multiple drivers! Success count: {successCount}");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var driverTripsForTrip = await verifyDb.DriverTrips.Where(dt => dt.TripId == tripId).ToListAsync();
        Assert.Single(driverTripsForTrip);
    }

    [Fact]
    public async Task AdminController_SetPrice_ConcurrencyConflict_AddsModelErrorAndReturnsView()
    {
        int tripId;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "setprice-concurrency-cust@example.test",
                UserName = "setprice-concurrency-cust@example.test",
                FirstName = "SetPrice",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.ApprovedByAdmin);
            setupDb.Trips.Add(trip);
            await setupDb.SaveChangesAsync();
            tripId = trip.Id;
        }

        using var scope = _factory.Services.CreateScope();
        var controller = ActivatorUtilities.CreateInstance<AdminController>(scope.ServiceProvider);
        var testDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tempDataProvider = scope.ServiceProvider.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin-id"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, tempDataProvider);

        var tripToPrice = await testDb.Trips.Include(t => t.Customer).FirstOrDefaultAsync(t => t.Id == tripId);
        Assert.NotNull(tripToPrice);

        // Background update changes ConcurrencyToken in DB
        using (var bgScope = _factory.Services.CreateScope())
        {
            var bgDb = bgScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bgTrip = await bgDb.Trips.FindAsync(tripId);
            bgTrip!.Notes = "Concurrently modified while pricing";
            await bgDb.SaveChangesAsync();
        }

        var model = new SetPriceViewModel
        {
            TripId = tripId,
            AdminPrice = 8500
        };

        // When SetPrice executes with stale ConcurrencyToken, it catches DbUpdateConcurrencyException
        var result = await controller.SetPrice(model);
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(string.Empty));
        Assert.Contains("modified by another process", controller.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task AdminController_AssignTrip_HighConcurrency_5Trips_AssignSameDriver_OnlyOneSucceeds()
    {
        const string adminEmail = "highconcurrency-admin@example.test";
        const string driverEmail = "highconcurrency-driver@example.test";
        string driverId;
        var tripIds = new List<int>();

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await EnsureUser(userManager, new Admin
            {
                Email = adminEmail,
                UserName = adminEmail,
                FirstName = "HighRace",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = driverEmail,
                UserName = driverEmail,
                DriverEmail = driverEmail,
                FirstName = "HighRace",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 10
            }, "Driver");
            driverId = driver.Id;
            driver.IsAvailable = true;
            await db.SaveChangesAsync();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "highconcurrency-cust@example.test",
                UserName = "highconcurrency-cust@example.test",
                FirstName = "HighRace",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            for (int i = 0; i < 5; i++)
            {
                var trip = CreateTripEntity(cust.Id, TripStatus.ApprovedByCustomer);
                db.Trips.Add(trip);
                await db.SaveChangesAsync();
                tripIds.Add(trip.Id);
            }
        }

        var admin = await LoginClient(adminEmail);

        // Fetch tokens for all 5 assignment views
        var tokens = new List<string>();
        foreach (var tripId in tripIds)
        {
            tokens.Add(await GetToken(admin, $"/Admin/AssignTrip/{tripId}"));
        }

        // Fire 5 concurrent POST requests trying to assign the same driver
        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 5; i++)
        {
            var tripId = tripIds[i];
            var token = tokens[i];
            tasks.Add(admin.PostAsync("/Admin/AssignTrip", Form(new()
            {
                ["TripId"] = tripId.ToString(),
                ["DriverId"] = driverId,
                ["__RequestVerificationToken"] = token
            })));
        }

        var responses = await Task.WhenAll(tasks);

        int redirects = responses.Count(r => r.StatusCode == HttpStatusCode.Redirect);
        Assert.True(redirects <= 1, $"RACE CONDITION DETECTED: {redirects} assignments succeeded for driver {driverId}!");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var driverTrips = await verifyDb.DriverTrips.Where(dt => dt.DriverId == driverId).ToListAsync();
        Assert.True(driverTrips.Count <= 1, $"Database corruption: Driver {driverId} assigned {driverTrips.Count} times!");

        var driverInDb = await verifyDb.Drivers.FindAsync(driverId);
        Assert.NotNull(driverInDb);
        if (driverTrips.Count == 1)
        {
            Assert.False(driverInDb.IsAvailable);
        }
    }

    [Fact]
    public async Task CustomerController_RespondToPrice_ConcurrentRequests_OnlyOneWins()
    {
        const string customerEmail = "concurrent-respond-cust@example.test";
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
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.PriceSet);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var client = await LoginClient(customerEmail);
        var token = await GetToken(client, $"/Customer/TripDetails/{tripId}");

        // Dispatch concurrent Approve and Reject requests
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

        // At least one must be rejected (either 400 BadRequest or 409 Conflict)
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.BadRequest);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finalTrip = await verifyDb.Trips.FindAsync(tripId);
        Assert.NotNull(finalTrip);

        // The state must be cleanly either ApprovedByCustomer OR CanceledByCustomer, never corrupted
        Assert.True(finalTrip.Status == TripStatus.ApprovedByCustomer || finalTrip.Status == TripStatus.CanceledByCustomer);
    }

    [Fact]
    public async Task DriverController_EndTrip_ConcurrentRequests_OnlyOneWins()
    {
        const string driverEmail = "concurrent-end-driver@example.test";
        string driverId;
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "concurrent-end-admin@example.test",
                UserName = "concurrent-end-admin@example.test",
                FirstName = "End",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = driverEmail,
                UserName = driverEmail,
                DriverEmail = driverEmail,
                FirstName = "End",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = false,
                PricePerKm = 10
            }, "Driver");
            driverId = driver.Id;

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "concurrent-end-cust@example.test",
                UserName = "concurrent-end-cust@example.test",
                FirstName = "End",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var trip = CreateTripEntity(cust.Id, TripStatus.Assigned);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                DriverId = driver.Id,
                AssignedByAdminId = admin.Id,
                DriverPayment = 4600,
                Status = TripStatus.Assigned,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };
            db.DriverTrips.Add(driverTrip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var client = await LoginClient(driverEmail);
        var token = await GetToken(client, "/Driver/CurrentTrips");

        // Send two concurrent EndTrip requests
        var task1 = client.PostAsync("/Driver/EndTrip", Form(new()
        {
            ["tripId"] = tripId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        var task2 = client.PostAsync("/Driver/EndTrip", Form(new()
        {
            ["tripId"] = tripId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        var responses = await Task.WhenAll(task1, task2);

        // At least one request is rejected with 404 Not Found (already completed) or 409 Conflict
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.NotFound || r.StatusCode == HttpStatusCode.Conflict);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tripInDb = await verifyDb.Trips.FindAsync(tripId);
        var dtInDb = await verifyDb.DriverTrips.FirstOrDefaultAsync(d => d.TripId == tripId);
        var driverInDb = await verifyDb.Drivers.FindAsync(driverId);

        Assert.Equal(TripStatus.Completed, tripInDb!.Status);
        Assert.Equal(TripStatus.Completed, dtInDb!.Status);
        Assert.True(driverInDb!.IsAvailable);
    }
}
