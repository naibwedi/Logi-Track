using System.Collections.Concurrent;
using System.Diagnostics;
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
using Microsoft.Extensions.Logging;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace LogiTrack.Tests;

/// <summary>
/// Tier 5 Adversarial Chaos, High-Frequency Concurrency, and Stress Testing Suite for Logi-Track.
/// Stress-tests:
/// 1. Concurrency collision: High-frequency simultaneous competing updates across admin pricing,
///    driver assignment, and driver ending trips (clean 409 Conflict/BadRequest/Redirect without 500 crash or DB corruption).
/// 2. Rapid SignalR connection cycling: Rapid join, leave, broadcast, and invalid coordinate flood.
/// 3. Rapid concurrent invoice PDF generation under multi-threaded load (50+ parallel threads).
/// 4. Absences of deadlocks, thread-safety violations, memory leaks, and database corruption.
/// </summary>
public sealed class ChallengerChaosConcurrencyStressTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public ChallengerChaosConcurrencyStressTests(LogiTrackFactory factory)
    {
        _factory = factory;
    }

    #region Test Doubles & Helpers

    private sealed class ChaosHubCallerContext(ClaimsPrincipal? user, string connectionId = "conn-chaos-1") : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        public override ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items { get; } = new ConcurrentDictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class ChaosGroupManager : IGroupManager
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

        public int AddedCount
        {
            get
            {
                lock (_lock) return AddedGroups.Count;
            }
        }

        public int RemovedCount
        {
            get
            {
                lock (_lock) return RemovedGroups.Count;
            }
        }
    }

    private sealed class ChaosClientProxy : IClientProxy
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
                lock (_lock) return Messages.Count;
            }
        }

        public List<(string method, object?[] args)> GetSnapshot()
        {
            lock (_lock) return [.. Messages];
        }
    }

    private sealed class ChaosHubCallerClients : IHubCallerClients
    {
        private readonly ConcurrentDictionary<string, ChaosClientProxy> _groupProxies = new();
        public ChaosClientProxy DefaultProxy { get; } = new();

        public IClientProxy All => DefaultProxy;
        public IClientProxy Caller => DefaultProxy;
        public IClientProxy Others => DefaultProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => DefaultProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => DefaultProxy;

        public IClientProxy Group(string groupName) =>
            _groupProxies.GetOrAdd(groupName, _ => new ChaosClientProxy());

        public ChaosClientProxy GetGroupProxy(string groupName) =>
            _groupProxies.GetOrAdd(groupName, _ => new ChaosClientProxy());

        public IClientProxy Client(string connectionId) => DefaultProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => DefaultProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => DefaultProxy;
        public IClientProxy OthersInGroup(string groupName) => DefaultProxy;
        public IClientProxy User(string userId) => DefaultProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => DefaultProxy;
    }

    private static (TripHub hub, ChaosGroupManager groups, ChaosHubCallerClients clients) CreateChaosHub(
        ApplicationDbContext db,
        ClaimsPrincipal? user,
        string connectionId = "conn-chaos-1")
    {
        var groups = new ChaosGroupManager();
        var clients = new ChaosHubCallerClients();
        var hub = new TripHub(db)
        {
            Context = new ChaosHubCallerContext(user, connectionId),
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
            FromAddress = "Chaos Road 1",
            ToAddress = "Chaos Way 99",
            FromZipCode = "0150",
            ToZipCode = "5003",
            Weight = 450,
            Volume = 4.5,
            Distance = 460,
            Notes = "Chaos concurrency stress payload",
            GoodsType = "Critical Stress Cargo",
            Status = status,
            EstimatedPrice = 8500,
            AdminPrice = status >= TripStatus.PriceSet ? 9200 : 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    #endregion

    #region 1. Concurrency Collision Stress Tests

    [Fact]
    public async Task ConcurrencyChaos_SimultaneousCompetingUpdatesOnSameTrip_ReturnsCleanResponsesWithoutCrashingOrCorruptingDb()
    {
        string customerId;
        string adminId;
        string driverId;
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "chaos-compete-cust@example.test",
                UserName = "chaos-compete-cust@example.test",
                FirstName = "Chaos",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            customerId = cust.Id;

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "chaos-compete-admin@example.test",
                UserName = "chaos-compete-admin@example.test",
                FirstName = "Chaos",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = admin.Id;

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = "chaos-compete-driver@example.test",
                UserName = "chaos-compete-driver@example.test",
                DriverEmail = "chaos-compete-driver@example.test",
                FirstName = "Chaos",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = false,
                PricePerKm = 12
            }, "Driver");
            driverId = driver.Id;

            var trip = CreateTripEntity(customerId, TripStatus.Requested);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        // We launch 12 concurrent tasks competing on the same trip:
        // - 4 Admins attempting ReviewTrip (approving / rejecting)
        // - 4 Admins attempting SetPrice
        // - 2 Customers attempting RespondToPrice
        // - 2 Drivers attempting EndTrip
        var tasks = new List<Task<IActionResult>>();
        var cleanStatusCodes = new ConcurrentBag<int>();

        for (int i = 0; i < 4; i++)
        {
            bool approve = (i % 2 == 0);
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var controller = ActivatorUtilities.CreateInstance<AdminController>(scope.ServiceProvider);
                var ctx = new DefaultHttpContext { User = CreatePrincipal(adminId, "Admin") };
                controller.ControllerContext = new ControllerContext { HttpContext = ctx };
                controller.TempData = new TempDataDictionary(ctx, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
                return await controller.ReviewTrip(tripId, approve);
            }));
        }

        for (int i = 0; i < 4; i++)
        {
            double price = 5000 + (i * 500);
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var controller = ActivatorUtilities.CreateInstance<AdminController>(scope.ServiceProvider);
                var ctx = new DefaultHttpContext { User = CreatePrincipal(adminId, "Admin") };
                controller.ControllerContext = new ControllerContext { HttpContext = ctx };
                controller.TempData = new TempDataDictionary(ctx, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
                var model = new SetPriceViewModel { TripId = tripId, AdminPrice = price };
                return await controller.SetPrice(model);
            }));
        }

        for (int i = 0; i < 2; i++)
        {
            bool accept = (i == 0);
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var controller = ActivatorUtilities.CreateInstance<CustomerController>(scope.ServiceProvider);
                var ctx = new DefaultHttpContext { User = CreatePrincipal(customerId, "Customer") };
                controller.ControllerContext = new ControllerContext { HttpContext = ctx };
                controller.TempData = new TempDataDictionary(ctx, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
                return await controller.RespondToPrice(tripId, accept);
            }));
        }

        for (int i = 0; i < 2; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var controller = ActivatorUtilities.CreateInstance<DriverController>(scope.ServiceProvider);
                var ctx = new DefaultHttpContext { User = CreatePrincipal(driverId, "Driver") };
                controller.ControllerContext = new ControllerContext { HttpContext = ctx };
                controller.TempData = new TempDataDictionary(ctx, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
                return await controller.EndTrip(tripId);
            }));
        }

        var results = await Task.WhenAll(tasks);

        // Analyze every response:
        // Every response MUST be one of:
        // - RedirectToActionResult (success)
        // - ViewResult (validation error or captured conflict re-render)
        // - ConflictObjectResult (409 Conflict)
        // - BadRequestObjectResult (400 Bad Request)
        // - NotFoundResult / NotFoundObjectResult (404)
        // NO UNHANDLED SERVER 500 OR CRASHES!
        foreach (var actionResult in results)
        {
            Assert.NotNull(actionResult);
            if (actionResult is ConflictObjectResult conflict)
            {
                cleanStatusCodes.Add(conflict.StatusCode ?? 409);
                Assert.Equal(409, conflict.StatusCode);
            }
            else if (actionResult is BadRequestObjectResult badRequest)
            {
                cleanStatusCodes.Add(badRequest.StatusCode ?? 400);
            }
            else if (actionResult is RedirectToActionResult redirect)
            {
                cleanStatusCodes.Add(302);
                Assert.NotNull(redirect.ActionName);
            }
            else if (actionResult is ViewResult view)
            {
                cleanStatusCodes.Add(200);
            }
            else if (actionResult is NotFoundResult or NotFoundObjectResult)
            {
                cleanStatusCodes.Add(404);
            }
            else
            {
                Assert.Fail($"Unexpected result type under concurrency collision: {actionResult.GetType().Name}");
            }
        }

        // Verify Database Integrity:
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var finalTrip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(finalTrip);
            Assert.NotEqual(Guid.Empty, finalTrip.ConcurrencyToken);
            Assert.True(Enum.IsDefined(typeof(TripStatus), finalTrip.Status));
        }
    }

    [Fact]
    public async Task ConcurrencyChaos_MassiveDriverDoubleBookingCollision_GuaranteesMutualExclusion()
    {
        string adminId;
        string driverId;
        var tripIds = new List<int>();

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "chaos-doublebook-admin@example.test",
                UserName = "chaos-doublebook-admin@example.test",
                FirstName = "Chaos",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = admin.Id;

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = "chaos-single-driver@example.test",
                UserName = "chaos-single-driver@example.test",
                DriverEmail = "chaos-single-driver@example.test",
                FirstName = "SingleBooking",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 14
            }, "Driver");
            driverId = driver.Id;

            // Ensure driver is strictly available initially
            driver.IsAvailable = true;
            await db.SaveChangesAsync();

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "chaos-doublebook-cust@example.test",
                UserName = "chaos-doublebook-cust@example.test",
                FirstName = "DoubleBook",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            for (int i = 0; i < 8; i++)
            {
                var trip = CreateTripEntity(cust.Id, TripStatus.ApprovedByCustomer);
                db.Trips.Add(trip);
                await db.SaveChangesAsync();
                tripIds.Add(trip.Id);
            }
        }

        // 8 simultaneous admin operations attempting to assign the single driver to 8 different trips
        var assignTasks = tripIds.Select(tId => Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var controller = ActivatorUtilities.CreateInstance<AdminController>(scope.ServiceProvider);
            var ctx = new DefaultHttpContext { User = CreatePrincipal(adminId, "Admin") };
            controller.ControllerContext = new ControllerContext { HttpContext = ctx };
            controller.TempData = new TempDataDictionary(ctx, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
            var model = new AssignTripViewModel { TripId = tId, DriverId = driverId };
            return await controller.AssignTrip(model);
        })).ToList();

        var results = await Task.WhenAll(assignTasks);

        // Verification:
        // Exactly ONE assignment must succeed with RedirectToAction("TripRequests")
        int successCount = results.Count(r => r is RedirectToActionResult redirect && redirect.ActionName == "TripRequests");
        Assert.Equal(1, successCount);

        // All other 7 must fail cleanly with ViewResult (re-rendering view with validation message)
        int failedCount = results.Count(r => r is ViewResult);
        Assert.Equal(7, failedCount);

        // Verify Database: exactly ONE DriverTrip record exists for this driver!
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignedTrips = await db.DriverTrips.Where(dt => dt.DriverId == driverId).ToListAsync();
            Assert.Single(assignedTrips);

            var driverInDb = await db.Drivers.FindAsync(driverId);
            Assert.NotNull(driverInDb);
            Assert.False(driverInDb.IsAvailable, "Driver must be marked unavailable after being assigned.");
        }
    }

    [Fact]
    public async Task ConcurrencyChaos_MultipleAdminsAssigningDifferentDriversToSameTrip_OnlyOneSucceeds()
    {
        string adminId;
        int tripId;
        var driverIds = new List<string>();

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "chaos-sametrip-admin@example.test",
                UserName = "chaos-sametrip-admin@example.test",
                FirstName = "SameTrip",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = admin.Id;

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "chaos-sametrip-cust@example.test",
                UserName = "chaos-sametrip-cust@example.test",
                FirstName = "SameTrip",
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

            for (int i = 0; i < 6; i++)
            {
                var driver = await EnsureUser(userManager, new Driver
                {
                    Email = $"chaos-diff-driver-{i}@example.test",
                    UserName = $"chaos-diff-driver-{i}@example.test",
                    DriverEmail = $"chaos-diff-driver-{i}@example.test",
                    FirstName = $"Driver{i}",
                    LastName = "Chaos",
                    DateOfBirth = new DateTime(1988, 1, 1),
                    RoleType = "Driver",
                    IsApproved = true,
                    EmailConfirmed = true,
                    IsAvailable = true,
                    PricePerKm = 10 + i
                }, "Driver");
                driver.IsAvailable = true;
                await db.SaveChangesAsync();
                driverIds.Add(driver.Id);
            }
        }

        // 6 simultaneous tasks attempting to assign 6 different drivers to the SAME trip
        var tasks = driverIds.Select(dId => Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var controller = ActivatorUtilities.CreateInstance<AdminController>(scope.ServiceProvider);
            var ctx = new DefaultHttpContext { User = CreatePrincipal(adminId, "Admin") };
            controller.ControllerContext = new ControllerContext { HttpContext = ctx };
            controller.TempData = new TempDataDictionary(ctx, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
            var model = new AssignTripViewModel { TripId = tripId, DriverId = dId };
            return await controller.AssignTrip(model);
        })).ToList();

        var results = await Task.WhenAll(tasks);

        // Exactly one admin assignment must succeed
        int successCount = results.Count(r => r is RedirectToActionResult redirect && redirect.ActionName == "TripRequests");
        Assert.Equal(1, successCount);

        // Database Verification:
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var driverTrips = await db.DriverTrips.Where(dt => dt.TripId == tripId).ToListAsync();
            Assert.Single(driverTrips);

            var tripInDb = await db.Trips.FindAsync(tripId);
            Assert.NotNull(tripInDb);
            Assert.Equal(TripStatus.Assigned, tripInDb.Status);

            // Exactly 1 driver marked unavailable; the other 5 drivers must remain available!
            var winningDriverId = driverTrips[0].DriverId;
            var availableDriversCount = await db.Drivers
                .Where(d => driverIds.Contains(d.Id) && d.IsAvailable)
                .CountAsync();
            Assert.Equal(5, availableDriversCount);

            var winningDriver = await db.Drivers.FindAsync(winningDriverId);
            Assert.NotNull(winningDriver);
            Assert.False(winningDriver.IsAvailable);
        }
    }

    #endregion

    #region 2. Rapid SignalR Connection Cycling & Telemetry Chaos

    [Fact]
    public async Task SignalRChaos_RapidConnectionCycling_FiftyClientsJoiningAndLeaving_NoDeadlockOrLeak()
    {
        var tripIds = new List<int>();
        var customerIds = new List<string>();

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            for (int i = 0; i < 5; i++)
            {
                var cust = await EnsureUser(userManager, new Customer
                {
                    Email = $"chaos-signalr-cycle-cust-{i}@example.test",
                    UserName = $"chaos-signalr-cycle-cust-{i}@example.test",
                    FirstName = $"CycleCust{i}",
                    LastName = "Customer",
                    DateOfBirth = new DateTime(1990, 1, 1),
                    RoleType = "Customer",
                    IsApproved = true,
                    EmailConfirmed = true
                }, "Customer");
                customerIds.Add(cust.Id);

                var trip = CreateTripEntity(cust.Id, TripStatus.Assigned);
                db.Trips.Add(trip);
                await db.SaveChangesAsync();
                tripIds.Add(trip.Id);
            }
        }

        // 50 concurrent simulated client connections rapidly joining and leaving groups (total 250 operations)
        var sharedGroupManager = new ChaosGroupManager();
        var sharedClients = new ChaosHubCallerClients();

        var clientTasks = Enumerable.Range(0, 50).Select(clientIndex => Task.Run(async () =>
        {
            int custIdx = clientIndex % customerIds.Count;
            string cId = customerIds[custIdx];
            int tId = tripIds[custIdx];
            string connId = $"chaos-conn-{clientIndex}";

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var hub = new TripHub(db)
            {
                Context = new ChaosHubCallerContext(CreatePrincipal(cId, "Customer"), connId),
                Groups = sharedGroupManager,
                Clients = sharedClients
            };

            // Rapid 5 cycles of Join and Leave
            for (int cycle = 0; cycle < 5; cycle++)
            {
                await hub.JoinTrip(tId);
                await hub.LeaveTrip(tId);
            }
        })).ToList();

        await Task.WhenAll(clientTasks);

        // Assert no deadlocks and accurate tracking:
        // 50 clients * 5 cycles = 250 joins and 250 leaves
        Assert.Equal(250, sharedGroupManager.AddedCount);
        Assert.Equal(250, sharedGroupManager.RemovedCount);
    }

    [Fact]
    public async Task SignalRChaos_InvalidCoordinateFlood_RejectsAllAdversarialPayloadsWithoutCrashing()
    {
        string driverId;
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "chaos-coord-admin@example.test",
                UserName = "chaos-coord-admin@example.test",
                FirstName = "Coord",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = "chaos-coord-driver@example.test",
                UserName = "chaos-coord-driver@example.test",
                DriverEmail = "chaos-coord-driver@example.test",
                FirstName = "Coord",
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
                Email = "chaos-coord-cust@example.test",
                UserName = "chaos-coord-cust@example.test",
                FirstName = "Coord",
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

        // Adversarial coordinate vectors to stress-test bounds validation
        var invalidCoordinates = new (double lat, double lng)[]
        {
            (90.0001, 10.0),
            (-90.0001, 10.0),
            (10.0, 180.0001),
            (10.0, -180.0001),
            (double.NaN, 10.0),
            (10.0, double.NaN),
            (double.PositiveInfinity, 10.0),
            (10.0, double.PositiveInfinity),
            (double.NegativeInfinity, 10.0),
            (10.0, double.NegativeInfinity),
            (1000.0, 2000.0),
            (-999.0, -999.0),
            (double.MaxValue, 0),
            (0, double.MinValue)
        };

        var hubGroupManager = new ChaosGroupManager();
        var hubClients = new ChaosHubCallerClients();

        // 1. Flood with invalid coordinates in parallel: ALL must throw HubException("Invalid coordinates.")
        var floodTasks = Enumerable.Range(0, 50).Select(index => Task.Run(async () =>
        {
            var (lat, lng) = invalidCoordinates[index % invalidCoordinates.Length];
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var hub = new TripHub(db)
            {
                Context = new ChaosHubCallerContext(CreatePrincipal(driverId, "Driver"), $"chaos-flood-{index}"),
                Groups = hubGroupManager,
                Clients = hubClients
            };

            var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(tripId, lat, lng));
            Assert.Equal("Invalid coordinates.", ex.Message);
        })).ToList();

        await Task.WhenAll(floodTasks);

        // 2. Legitimate boundary coordinates MUST succeed cleanly
        var validBoundaryCoordinates = new (double lat, double lng)[]
        {
            (90.0, 180.0),
            (-90.0, -180.0),
            (0.0, 0.0),
            (59.9139, 10.7522), // Oslo
            (60.3913, 5.3221)   // Bergen
        };

        foreach (var (vLat, vLng) in validBoundaryCoordinates)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var hub = new TripHub(db)
            {
                Context = new ChaosHubCallerContext(CreatePrincipal(driverId, "Driver"), "chaos-valid-bound"),
                Groups = hubGroupManager,
                Clients = hubClients
            };

            await hub.SendLocationUpdate(tripId, vLat, vLng);
        }

        // Verify group broadcast received valid messages
        var tripGroup = hubClients.GetGroupProxy($"trip_{tripId}");
        Assert.Equal(validBoundaryCoordinates.Length, tripGroup.MessageCount);

        var adminGroup = hubClients.GetGroupProxy("Admins");
        Assert.Equal(validBoundaryCoordinates.Length, adminGroup.MessageCount);
    }

    [Fact]
    public async Task SignalRChaos_UnauthorizedCrossTenantFlood_StrictlyDeniedWithHubException()
    {
        int tripId;
        string rogueCustomerId;
        string rogueDriverId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "chaos-unauth-admin@example.test",
                UserName = "chaos-unauth-admin@example.test",
                FirstName = "Unauth",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");

            var legitDriver = await EnsureUser(userManager, new Driver
            {
                Email = "chaos-legit-driver@example.test",
                UserName = "chaos-legit-driver@example.test",
                DriverEmail = "chaos-legit-driver@example.test",
                FirstName = "Legit",
                LastName = "Driver",
                DateOfBirth = new DateTime(1988, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = false,
                PricePerKm = 10
            }, "Driver");

            var legitCust = await EnsureUser(userManager, new Customer
            {
                Email = "chaos-legit-cust@example.test",
                UserName = "chaos-legit-cust@example.test",
                FirstName = "Legit",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");

            var rogueCust = await EnsureUser(userManager, new Customer
            {
                Email = "chaos-rogue-cust@example.test",
                UserName = "chaos-rogue-cust@example.test",
                FirstName = "Rogue",
                LastName = "Customer",
                DateOfBirth = new DateTime(1992, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            rogueCustomerId = rogueCust.Id;

            var rogueDriver = await EnsureUser(userManager, new Driver
            {
                Email = "chaos-rogue-driver@example.test",
                UserName = "chaos-rogue-driver@example.test",
                DriverEmail = "chaos-rogue-driver@example.test",
                FirstName = "Rogue",
                LastName = "Driver",
                DateOfBirth = new DateTime(1989, 1, 1),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 10
            }, "Driver");
            rogueDriverId = rogueDriver.Id;

            var trip = CreateTripEntity(legitCust.Id, TripStatus.Assigned);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                DriverId = legitDriver.Id,
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

        var hubGroupManager = new ChaosGroupManager();
        var hubClients = new ChaosHubCallerClients();

        // 20 concurrent unauthorized callers attempting JoinTrip or SendLocationUpdate
        var unauthTasks = Enumerable.Range(0, 20).Select(index => Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (index % 3 == 0)
            {
                // Unauthenticated user
                var hub = new TripHub(db)
                {
                    Context = new ChaosHubCallerContext(null, $"unauth-{index}"),
                    Groups = hubGroupManager,
                    Clients = hubClients
                };
                var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinTrip(tripId));
                Assert.Equal("Authentication is required.", ex.Message);
            }
            else if (index % 3 == 1)
            {
                // Rogue customer trying to join another customer's trip
                var hub = new TripHub(db)
                {
                    Context = new ChaosHubCallerContext(CreatePrincipal(rogueCustomerId, "Customer"), $"rogue-cust-{index}"),
                    Groups = hubGroupManager,
                    Clients = hubClients
                };
                var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinTrip(tripId));
                Assert.Equal("You are not allowed to subscribe to this trip.", ex.Message);
            }
            else
            {
                // Rogue driver trying to send GPS update for a trip they are not assigned to
                var hub = new TripHub(db)
                {
                    Context = new ChaosHubCallerContext(CreatePrincipal(rogueDriverId, "Driver"), $"rogue-driver-{index}"),
                    Groups = hubGroupManager,
                    Clients = hubClients
                };
                var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(tripId, 59.91, 10.75));
                Assert.Equal("You are not authorized to update location for this trip.", ex.Message);
            }
        })).ToList();

        await Task.WhenAll(unauthTasks);

        // Ensure ZERO messages were broadcast to the trip group or admins
        var tripGroup = hubClients.GetGroupProxy($"trip_{tripId}");
        Assert.Equal(0, tripGroup.MessageCount);
    }

    #endregion

    #region 3. Rapid Concurrent Invoice PDF Generation Under Multi-Threaded Load

    [Fact]
    public async Task PdfChaos_FiftyConcurrentThreads_AllProduceValidReadablePdfDocumentsWithoutCorruption()
    {
        var service = new PdfInvoiceService();
        var generatedPdfs = new ConcurrentBag<byte[]>();

        var testCases = Enumerable.Range(1, 50).Select(i =>
        {
            var customer = new Customer
            {
                Id = $"pdf-chaos-cust-{i}",
                FirstName = (i % 3 == 0) ? "Jørn-Håkon" : (i % 3 == 1) ? "Renée & Ærlig" : $"Customer{i}",
                LastName = (i % 2 == 0) ? "Østbø-Ås" : $"LastName{i}",
                Email = $"cust{i}@example.test"
            };

            var trip = new Trip
            {
                Id = 1000 + i,
                CustomerId = customer.Id,
                FromCity = (i % 2 == 0) ? "Oslo" : "Tromsø",
                ToCity = (i % 2 == 0) ? "Trondheim" : "Kristiansand",
                FromAddress = $"Start St {i}",
                ToAddress = $"End Blvd {i}",
                FromZipCode = "0150",
                ToZipCode = "7010",
                Distance = (i == 1) ? 0 : (i == 2) ? 10000 : (i * 25.5),
                Weight = (i == 1) ? 0 : (i * 50.0),
                Volume = i * 1.5,
                EstimatedPrice = (i == 1) ? 0 : (i * 1500),
                AdminPrice = (i == 1) ? 0 : (i * 1800),
                Status = TripStatus.Completed,
                CreatedAt = DateTime.UtcNow.AddDays(-i),
                UpdatedAt = DateTime.UtcNow
            };

            return (trip, customer);
        }).ToList();

        // Launch 50 threads generating PDFs in parallel
        var tasks = testCases.Select(tc => Task.Run(() =>
        {
            var bytes = service.GenerateInvoicePdf(tc.trip, tc.customer);
            generatedPdfs.Add(bytes);

            // Assert immediate integrity:
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 1000, $"PDF too small: {bytes.Length} bytes");

            // Magic bytes '%PDF-'
            Assert.Equal(0x25, bytes[0]);
            Assert.Equal(0x50, bytes[1]);
            Assert.Equal(0x44, bytes[2]);
            Assert.Equal(0x46, bytes[3]);
            Assert.Equal(0x2D, bytes[4]);

            // Parse document structure with PdfReader to confirm 0 corruption
            using var stream = new MemoryStream(bytes);
            using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Assert.Equal(1, doc.PageCount);
            Assert.Equal($"Invoice INV-{tc.trip.Id:D6}", doc.Info.Title);
            Assert.Equal("Logi-Track", doc.Info.Author);
        })).ToList();

        await Task.WhenAll(tasks);

        Assert.Equal(50, generatedPdfs.Count);
    }

    [Fact]
    public async Task PdfChaos_MemoryStabilityAndResourceCleanup_LoopUnderConcurrency()
    {
        var service = new PdfInvoiceService();
        var customer = new Customer
        {
            Id = "mem-leak-cust",
            FirstName = "Memory",
            LastName = "StressTester",
            Email = "mem@example.test"
        };
        var trip = new Trip
        {
            Id = 9999,
            CustomerId = customer.Id,
            FromCity = "Oslo",
            ToCity = "Bergen",
            FromAddress = "Memory Lane 1",
            ToAddress = "Byte Boulevard 2",
            Distance = 460,
            Weight = 500,
            Volume = 10,
            EstimatedPrice = 12000,
            AdminPrice = 13500,
            Status = TripStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Warm up
        _ = service.GenerateInvoicePdf(trip, customer);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long initialMemory = GC.GetTotalMemory(true);

        // Generate 100 PDFs across 10 parallel tasks
        var loopTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                var bytes = service.GenerateInvoicePdf(trip, customer);
                Assert.NotNull(bytes);
                Assert.True(bytes.Length > 1000);
            }
        })).ToList();

        await Task.WhenAll(loopTasks);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long finalMemory = GC.GetTotalMemory(true);
        long growthBytes = finalMemory - initialMemory;

        // Ensure memory growth is reasonable (< 30 MB delta after 100 generations)
        Assert.True(growthBytes < 30 * 1024 * 1024, $"Potential memory leak detected: {growthBytes / 1024 / 1024} MB growth");
    }

    [Fact]
    public async Task PdfChaos_ConcurrentControllerDownloadEndpoints_UnderLoad()
    {
        string customerId;
        string adminId;
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "chaos-download-admin@example.test",
                UserName = "chaos-download-admin@example.test",
                FirstName = "Download",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = admin.Id;

            var cust = await EnsureUser(userManager, new Customer
            {
                Email = "chaos-download-cust@example.test",
                UserName = "chaos-download-cust@example.test",
                FirstName = "Download",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            customerId = cust.Id;

            var trip = CreateTripEntity(cust.Id, TripStatus.Completed);
            trip.AdminPrice = 8500;
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        // 20 concurrent requests hitting Customer and Admin DownloadInvoice endpoints
        var downloadTasks = Enumerable.Range(0, 20).Select(index => Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();

            if (index % 2 == 0)
            {
                var controller = ActivatorUtilities.CreateInstance<CustomerController>(scope.ServiceProvider);
                var ctx = new DefaultHttpContext { User = CreatePrincipal(customerId, "Customer") };
                controller.ControllerContext = new ControllerContext { HttpContext = ctx };

                var result = await controller.DownloadInvoice(tripId);
                var fileResult = Assert.IsType<FileContentResult>(result);
                Assert.Equal("application/pdf", fileResult.ContentType);
                Assert.Equal($"Invoice_Trip_{tripId}.pdf", fileResult.FileDownloadName);
                Assert.True(fileResult.FileContents.Length > 1000);
            }
            else
            {
                var controller = ActivatorUtilities.CreateInstance<AdminController>(scope.ServiceProvider);
                var ctx = new DefaultHttpContext { User = CreatePrincipal(adminId, "Admin") };
                controller.ControllerContext = new ControllerContext { HttpContext = ctx };

                var result = await controller.DownloadInvoice(tripId);
                var fileResult = Assert.IsType<FileContentResult>(result);
                Assert.Equal("application/pdf", fileResult.ContentType);
                Assert.Equal($"Invoice_Trip_{tripId}.pdf", fileResult.FileDownloadName);
                Assert.True(fileResult.FileContents.Length > 1000);
            }
        })).ToList();

        await Task.WhenAll(downloadTasks);
    }

    [Fact]
    public async Task ConcurrencyChaos_SimultaneousDriverEndTrip_AndAdminReview_PreventsInvalidStateOrCorruption()
    {
        string driverId;
        string adminId;
        string customerId;
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "chaos-endrace-admin@example.test",
                UserName = "chaos-endrace-admin@example.test",
                FirstName = "EndRace",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = admin.Id;

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = "chaos-endrace-driver@example.test",
                UserName = "chaos-endrace-driver@example.test",
                DriverEmail = "chaos-endrace-driver@example.test",
                FirstName = "EndRace",
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
                Email = "chaos-endrace-cust@example.test",
                UserName = "chaos-endrace-cust@example.test",
                FirstName = "EndRace",
                LastName = "Customer",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            }, "Customer");
            customerId = cust.Id;

            var trip = CreateTripEntity(cust.Id, TripStatus.InProgress);
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

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
            db.DriverTrips.Add(driverTrip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        // Concurrently run 4 Driver EndTrip calls and 4 Admin ReviewTrip calls
        var tasks = new List<Task<IActionResult>>();

        for (int i = 0; i < 4; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var controller = ActivatorUtilities.CreateInstance<DriverController>(scope.ServiceProvider);
                var ctx = new DefaultHttpContext { User = CreatePrincipal(driverId, "Driver") };
                controller.ControllerContext = new ControllerContext { HttpContext = ctx };
                controller.TempData = new TempDataDictionary(ctx, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
                return await controller.EndTrip(tripId);
            }));
        }

        for (int i = 0; i < 4; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var controller = ActivatorUtilities.CreateInstance<AdminController>(scope.ServiceProvider);
                var ctx = new DefaultHttpContext { User = CreatePrincipal(adminId, "Admin") };
                controller.ControllerContext = new ControllerContext { HttpContext = ctx };
                controller.TempData = new TempDataDictionary(ctx, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
                return await controller.ReviewTrip(tripId, false);
            }));
        }

        var results = await Task.WhenAll(tasks);

        // Assert all completed cleanly without unhandled 500
        foreach (var r in results)
        {
            Assert.True(
                r is RedirectToActionResult ||
                r is ConflictObjectResult ||
                r is BadRequestObjectResult ||
                r is ViewResult ||
                r is NotFoundResult,
                $"Unexpected result: {r.GetType().Name}");
        }

        // Verify Database: Trip completed and driver is available
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tripInDb = await db.Trips.FindAsync(tripId);
            Assert.NotNull(tripInDb);
            Assert.Equal(TripStatus.Completed, tripInDb.Status);

            var driverInDb = await db.Drivers.FindAsync(driverId);
            Assert.NotNull(driverInDb);
            Assert.True(driverInDb.IsAvailable, "Driver must be marked available upon trip completion.");
        }
    }

    [Fact]
    public async Task SignalRChaos_ConcurrentFirstGpsEmit_AssignedToInProgress_RaceResilience()
    {
        string driverId;
        string adminId;
        int tripId;

        using (var setupScope = _factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var admin = await EnsureUser(userManager, new Admin
            {
                Email = "chaos-gpsrace-admin@example.test",
                UserName = "chaos-gpsrace-admin@example.test",
                FirstName = "GpsRace",
                LastName = "Admin",
                DateOfBirth = new DateTime(1985, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            }, "Admin");
            adminId = admin.Id;

            var driver = await EnsureUser(userManager, new Driver
            {
                Email = "chaos-gpsrace-driver@example.test",
                UserName = "chaos-gpsrace-driver@example.test",
                DriverEmail = "chaos-gpsrace-driver@example.test",
                FirstName = "GpsRace",
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
                Email = "chaos-gpsrace-cust@example.test",
                UserName = "chaos-gpsrace-cust@example.test",
                FirstName = "GpsRace",
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

        var hubGroupManager = new ChaosGroupManager();
        var hubClients = new ChaosHubCallerClients();

        // 8 concurrent GPS location update emits on newly assigned trip
        var emitTasks = Enumerable.Range(0, 8).Select(idx => Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var hub = new TripHub(db)
            {
                Context = new ChaosHubCallerContext(CreatePrincipal(driverId, "Driver"), $"chaos-gpsrace-{idx}"),
                Groups = hubGroupManager,
                Clients = hubClients
            };

            double lat = 59.91 + (idx * 0.001);
            double lng = 10.75 + (idx * 0.001);
            await hub.SendLocationUpdate(tripId, lat, lng);
        })).ToList();

        await Task.WhenAll(emitTasks);

        // Verification:
        // Status transitioned to InProgress
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tripInDb = await db.Trips.Include(t => t.DriverTrip).FirstAsync(t => t.Id == tripId);
            Assert.Equal(TripStatus.InProgress, tripInDb.Status);
            Assert.NotNull(tripInDb.DriverTrip);
            Assert.Equal(TripStatus.InProgress, tripInDb.DriverTrip.Status);

            var loc = await db.Locations.FirstOrDefaultAsync(l => l.DriverID == driverId);
            Assert.NotNull(loc);
            Assert.InRange(loc.Latitude, 59.90, 60.00);
            Assert.InRange(loc.Longitude, 10.70, 10.80);
        }

        var tripGroup = hubClients.GetGroupProxy($"trip_{tripId}");
        Assert.Equal(8, tripGroup.MessageCount);
    }

    #endregion
}
