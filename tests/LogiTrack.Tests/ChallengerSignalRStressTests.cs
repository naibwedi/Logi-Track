using System.Security.Claims;
using logirack.Data;
using logirack.Hubs;
using logirack.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogiTrack.Tests;

public sealed class ChallengerSignalRStressTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public ChallengerSignalRStressTests(LogiTrackFactory factory)
    {
        _factory = factory;
    }

    private sealed class TestHubCallerContext(ClaimsPrincipal? user, string connectionId = "conn-challenger-1") : HubCallerContext
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

        public int TotalMessagesSent => DefaultProxy.Messages.Count + GroupProxies.Values.Sum(p => p.Messages.Count);

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
        string connectionId = "conn-challenger-1")
    {
        var hub = new TripHub(db);
        var groups = new TestGroupManager();
        var clients = new TestHubCallerClients();

        hub.Context = new TestHubCallerContext(user, connectionId);
        hub.Groups = groups;
        hub.Clients = clients;

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

    private async Task<(string customerAId, string customerBId, string driverAId, string driverBId, string adminId)> SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var customerA = await EnsureUser(users, new Customer
        {
            UserName = "challenger_cust_a@test.local",
            Email = "challenger_cust_a@test.local",
            FirstName = "CustA",
            LastName = "Challenger",
            CompanyName = "Challenger Corp A",
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        var customerB = await EnsureUser(users, new Customer
        {
            UserName = "challenger_cust_b@test.local",
            Email = "challenger_cust_b@test.local",
            FirstName = "CustB",
            LastName = "Challenger",
            CompanyName = "Challenger Corp B",
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        var driverA = await EnsureUser(users, new Driver
        {
            UserName = "challenger_driver_a@test.local",
            Email = "challenger_driver_a@test.local",
            FirstName = "DriverA",
            LastName = "Challenger",
            DriverEmail = "challenger_driver_a@test.local",
            RoleType = "Driver",
            IsAvailable = true,
            PricePerKm = 10,
            PaymentFreq = PaymentFreq.Monthly,
            IsApproved = true,
            EmailConfirmed = true
        }, "Driver");

        var driverB = await EnsureUser(users, new Driver
        {
            UserName = "challenger_driver_b@test.local",
            Email = "challenger_driver_b@test.local",
            FirstName = "DriverB",
            LastName = "Challenger",
            DriverEmail = "challenger_driver_b@test.local",
            RoleType = "Driver",
            IsAvailable = true,
            PricePerKm = 10,
            PaymentFreq = PaymentFreq.Monthly,
            IsApproved = true,
            EmailConfirmed = true
        }, "Driver");

        var admin = await EnsureUser(users, new Admin
        {
            UserName = "challenger_admin@test.local",
            Email = "challenger_admin@test.local",
            FirstName = "Admin",
            LastName = "Challenger",
            RoleType = "Admin",
            IsApproved = true,
            EmailConfirmed = true
        }, "Admin");

        return (customerA.Id, customerB.Id, driverA.Id, driverB.Id, admin.Id);
    }

    private static async Task<T> EnsureUser<T>(UserManager<ApplicationUser> users, T user, string role) where T : ApplicationUser
    {
        var existing = await users.FindByEmailAsync(user.Email!);
        if (existing is not null) return (T)existing;

        var result = await users.CreateAsync(user, DefaultPassword);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        result = await users.AddToRoleAsync(user, role);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private static async Task<Trip> CreateTripAsync(ApplicationDbContext db, string customerId, string? driverId, string adminId, TripStatus status = TripStatus.Assigned)
    {
        var trip = new Trip
        {
            CustomerId = customerId,
            AdminId = adminId,
            FromCity = "Oslo",
            ToCity = "Trondheim",
            FromAddress = "Karl Johans gate 1",
            ToAddress = "Torvet 1",
            FromZipCode = "0154",
            ToZipCode = "7013",
            Weight = 100,
            Volume = 4,
            Distance = 490,
            GoodsType = "Equipment",
            PickupTime = DateTime.UtcNow.AddDays(2),
            EstimatedPrice = 8000,
            AdminPrice = 8500,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(driverId))
        {
            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                DriverId = driverId,
                AssignedByAdminId = adminId,
                Status = status,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };
            db.DriverTrips.Add(driverTrip);
            await db.SaveChangesAsync();
        }

        return trip;
    }

    // -------------------------------------------------------------------------------------------------
    // CHALLENGE 1: Customer Calling SendLocationUpdate Rejection & Zero Broadcast Verification
    // -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(TripStatus.Assigned)]
    [InlineData(TripStatus.InProgress)]
    [InlineData(TripStatus.Completed)]
    public async Task Challenge1_CustomerOwnerCallingSendLocationUpdate_ThrowsHubException_AndZeroBroadcast(TripStatus status)
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, status);

        var customer = CreatePrincipal(customerAId, "Customer");
        var (hub, _, clients) = CreateHub(db, customer);

        // Attempt location update as the customer who owns the trip
        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 59.9139, 10.7522));
        Assert.Equal("You are not authorized to update location for this trip.", ex.Message);

        // Verify zero broadcast messages dispatched across any group or client
        Assert.Equal(0, clients.TotalMessagesSent);

        // Verify database Location was NOT created or modified for customer
        var customerLocation = await db.Locations.FirstOrDefaultAsync(l => l.DriverID == customerAId);
        Assert.Null(customerLocation);

        // Verify trip status was NOT modified
        var refreshedTrip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == trip.Id);
        Assert.Equal(status, refreshedTrip.Status);
    }

    [Fact]
    public async Task Challenge1_StrangerCustomerCallingSendLocationUpdate_ThrowsHubException_AndZeroBroadcast()
    {
        var (customerAId, customerBId, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, TripStatus.InProgress);

        // Customer B tries to update Customer A's trip
        var customerB = CreatePrincipal(customerBId, "Customer");
        var (hub, _, clients) = CreateHub(db, customerB);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 59.9139, 10.7522));
        Assert.Equal("You are not authorized to update location for this trip.", ex.Message);

        Assert.Equal(0, clients.TotalMessagesSent);
    }

    // -------------------------------------------------------------------------------------------------
    // CHALLENGE 2: Unassigned Driver Calling SendLocationUpdate Rejection & Zero Broadcast Verification
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Challenge2_UnassignedDriverCallingSendLocationUpdate_ThrowsHubException_AndZeroBroadcast()
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var unassignedDriver = await EnsureUser(users, new Driver
        {
            UserName = $"challenger_unassigned_{Guid.NewGuid():N}@test.local",
            Email = $"challenger_unassigned_{Guid.NewGuid():N}@test.local",
            FirstName = "Unassigned",
            LastName = "Driver",
            DriverEmail = "unassigned@test.local",
            RoleType = "Driver",
            IsAvailable = true,
            PricePerKm = 10,
            PaymentFreq = PaymentFreq.Monthly,
            IsApproved = true,
            EmailConfirmed = true
        }, "Driver");

        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, TripStatus.InProgress);

        // Unassigned driver attempts to update Trip assigned to Driver A
        var unassignedPrincipal = CreatePrincipal(unassignedDriver.Id, "Driver");
        var (hub, _, clients) = CreateHub(db, unassignedPrincipal);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 60.3913, 5.3221));
        Assert.Equal("You are not authorized to update location for this trip.", ex.Message);

        // Verify zero broadcast
        Assert.Equal(0, clients.TotalMessagesSent);

        // Verify unassigned driver's location was NOT created or modified in db
        var driverLoc = await db.Locations.FirstOrDefaultAsync(l => l.DriverID == unassignedDriver.Id);
        Assert.Null(driverLoc);
    }

    [Fact]
    public async Task Challenge2_DriverCallingSendLocationUpdate_OnTripWithoutAssignedDriver_ThrowsHubException()
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Create trip with NO driver
        var trip = await CreateTripAsync(db, customerAId, null, adminId, TripStatus.Requested);

        var driverA = CreatePrincipal(driverAId, "Driver");
        var (hub, _, clients) = CreateHub(db, driverA);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 60.3913, 5.3221));
        Assert.Equal("You are not authorized to update location for this trip.", ex.Message);

        Assert.Equal(0, clients.TotalMessagesSent);
    }

    // -------------------------------------------------------------------------------------------------
    // CHALLENGE 3: Location Updates on Non-Active Trips (All Inactive Statuses)
    // -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(TripStatus.Completed)]
    [InlineData(TripStatus.CancelledByAdmin)]
    [InlineData(TripStatus.CanceledByCustomer)]
    [InlineData(TripStatus.Requested)]
    [InlineData(TripStatus.ApprovedByAdmin)]
    [InlineData(TripStatus.PriceSet)]
    [InlineData(TripStatus.ApprovedByCustomer)]
    public async Task Challenge3_AssignedDriver_CallingOnInactiveTrip_ThrowsHubException_AndZeroBroadcast(TripStatus inactiveStatus)
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, inactiveStatus);

        var driverA = CreatePrincipal(driverAId, "Driver");
        var (hub, _, clients) = CreateHub(db, driverA);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 59.9139, 10.7522));
        Assert.Equal("Location updates are only allowed for active trips.", ex.Message);

        // Verify no message was broadcast
        Assert.Equal(0, clients.TotalMessagesSent);

        // Verify status remained unchanged
        var refreshedTrip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == trip.Id);
        Assert.Equal(inactiveStatus, refreshedTrip.Status);
    }

    [Theory]
    [InlineData(TripStatus.Completed)]
    [InlineData(TripStatus.CancelledByAdmin)]
    [InlineData(TripStatus.CanceledByCustomer)]
    [InlineData(TripStatus.Requested)]
    public async Task Challenge3_Admin_CallingOnInactiveTrip_ThrowsHubException_AndZeroBroadcast(TripStatus inactiveStatus)
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, inactiveStatus);

        var admin = CreatePrincipal(adminId, "Admin");
        var (hub, _, clients) = CreateHub(db, admin);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 59.9139, 10.7522));
        Assert.Equal("Location updates are only allowed for active trips.", ex.Message);

        Assert.Equal(0, clients.TotalMessagesSent);
    }

    // -------------------------------------------------------------------------------------------------
    // CHALLENGE 4: Out-of-bounds Coordinates (Lat 95, Lng 200, NaN, Infinity)
    // -------------------------------------------------------------------------------------------------

    [Theory]
    // Specific requirements from dispatch: lat 95, lng 200, NaN, Infinity
    [InlineData(95.0, 10.0)]
    [InlineData(-95.0, 10.0)]
    [InlineData(60.0, 200.0)]
    [InlineData(60.0, -200.0)]
    [InlineData(double.NaN, 10.0)]
    [InlineData(60.0, double.NaN)]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.PositiveInfinity, 10.0)]
    [InlineData(double.NegativeInfinity, 10.0)]
    [InlineData(60.0, double.PositiveInfinity)]
    [InlineData(60.0, double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, double.NegativeInfinity)]
    // Exact boundary over-bounds
    [InlineData(90.000001, 0.0)]
    [InlineData(-90.000001, 0.0)]
    [InlineData(0.0, 180.000001)]
    [InlineData(0.0, -180.000001)]
    // Massive overflow/underflow
    [InlineData(1000.0, 0.0)]
    [InlineData(0.0, -1000.0)]
    public async Task Challenge4_OutOfBoundsCoordinates_ThrowsInvalidCoordinates_AndZeroBroadcast(double lat, double lng)
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, TripStatus.InProgress);

        var driverA = CreatePrincipal(driverAId, "Driver");
        var (hub, _, clients) = CreateHub(db, driverA);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, lat, lng));
        Assert.Equal("Invalid coordinates.", ex.Message);

        // Ensure zero broadcast occurred
        Assert.Equal(0, clients.TotalMessagesSent);
    }

    [Theory]
    // Exact boundary valid coordinates
    [InlineData(90.0, 180.0)]
    [InlineData(-90.0, -180.0)]
    [InlineData(90.0, -180.0)]
    [InlineData(-90.0, 180.0)]
    [InlineData(0.0, 0.0)]
    public async Task Challenge4_ExactBoundaryValidCoordinates_SucceedsAndBroadcasts(double lat, double lng)
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, TripStatus.InProgress);

        var driverA = CreatePrincipal(driverAId, "Driver");
        var (hub, _, clients) = CreateHub(db, driverA);

        await hub.SendLocationUpdate(trip.Id, lat, lng);

        var tripGroup = clients.GroupProxies[$"trip_{trip.Id}"];
        Assert.Single(tripGroup.Messages);
        Assert.Equal("ReceiveLocationUpdate", tripGroup.Messages[0].method);
        Assert.Equal(lat, tripGroup.Messages[0].args[1]);
        Assert.Equal(lng, tripGroup.Messages[0].args[2]);
    }

    // -------------------------------------------------------------------------------------------------
    // CHALLENGE 5: Group Delivery Strictly Isolated to Specified Trip Group (Zero Cross-Trip Leakage)
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Challenge5_MultiTripGroupIsolation_StrictlyScopedWithoutLeakage()
    {
        var (customerAId, customerBId, driverAId, driverBId, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Create Trip 1 (Customer A, Driver A) and Trip 2 (Customer B, Driver B)
        var trip1 = await CreateTripAsync(db, customerAId, driverAId, adminId, TripStatus.InProgress);
        var trip2 = await CreateTripAsync(db, customerBId, driverBId, adminId, TripStatus.InProgress);

        Assert.NotEqual(trip1.Id, trip2.Id);

        var driverA = CreatePrincipal(driverAId, "Driver");
        var driverB = CreatePrincipal(driverBId, "Driver");

        // 1. Customer A attempts to join Trip 2 group (IDOR attempt via SignalR JoinTrip)
        var customerA = CreatePrincipal(customerAId, "Customer");
        var (customerAHub, _, _) = CreateHub(db, customerA, "conn-cust-a");
        var joinEx = await Assert.ThrowsAsync<HubException>(() => customerAHub.JoinTrip(trip2.Id));
        Assert.Equal("You are not allowed to subscribe to this trip.", joinEx.Message);

        // 2. Driver A sends location update for Trip 1
        var (hubA, _, clientsA) = CreateHub(db, driverA, "conn-driver-a");
        const double trip1Lat = 59.9139;
        const double trip1Lng = 10.7522;

        await hubA.SendLocationUpdate(trip1.Id, trip1Lat, trip1Lng);

        // Verify Trip 1 group received the message
        Assert.True(clientsA.GroupProxies.ContainsKey($"trip_{trip1.Id}"));
        var trip1Group = clientsA.GroupProxies[$"trip_{trip1.Id}"];
        Assert.Single(trip1Group.Messages);
        Assert.Equal("ReceiveLocationUpdate", trip1Group.Messages[0].method);
        Assert.Equal(trip1.Id, trip1Group.Messages[0].args[0]);

        // Verify Trip 2 group proxy received ZERO messages (was never targeted or invoked)
        Assert.False(clientsA.GroupProxies.ContainsKey($"trip_{trip2.Id}"),
            "SignalR hub must never dispatch Trip 1 updates to Trip 2 group!");

        // Verify DefaultProxy (All, Caller, Others) received ZERO messages
        Assert.Empty(clientsA.DefaultProxy.Messages);

        // 3. Driver B sends location update for Trip 2
        var (hubB, _, clientsB) = CreateHub(db, driverB, "conn-driver-b");
        const double trip2Lat = 63.4305;
        const double trip2Lng = 10.3951;

        await hubB.SendLocationUpdate(trip2.Id, trip2Lat, trip2Lng);

        // Verify Trip 2 group received the message
        Assert.True(clientsB.GroupProxies.ContainsKey($"trip_{trip2.Id}"));
        var trip2Group = clientsB.GroupProxies[$"trip_{trip2.Id}"];
        Assert.Single(trip2Group.Messages);
        Assert.Equal("ReceiveLocationUpdate", trip2Group.Messages[0].method);
        Assert.Equal(trip2.Id, trip2Group.Messages[0].args[0]);

        // Verify Trip 1 group proxy was not invoked during Trip 2 update
        Assert.False(clientsB.GroupProxies.ContainsKey($"trip_{trip1.Id}"),
            "SignalR hub must never dispatch Trip 2 updates to Trip 1 group!");
    }

    [Fact]
    public async Task Challenge5_InterleavedBurstUpdates_MaintainsCompleteGroupIsolation()
    {
        var (customerAId, customerBId, driverAId, driverBId, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var trip1 = await CreateTripAsync(db, customerAId, driverAId, adminId, TripStatus.InProgress);
        var trip2 = await CreateTripAsync(db, customerBId, driverBId, adminId, TripStatus.InProgress);

        var driverA = CreatePrincipal(driverAId, "Driver");
        var driverB = CreatePrincipal(driverBId, "Driver");

        // Shared clients container to simulate shared hub dispatcher environment
        var sharedClients = new TestHubCallerClients();

        var hubA = new TripHub(db)
        {
            Context = new TestHubCallerContext(driverA, "conn-driver-a"),
            Clients = sharedClients,
            Groups = new TestGroupManager()
        };

        var hubB = new TripHub(db)
        {
            Context = new TestHubCallerContext(driverB, "conn-driver-b"),
            Clients = sharedClients,
            Groups = new TestGroupManager()
        };

        const int iterations = 25;
        for (int i = 0; i < iterations; i++)
        {
            // Interleaved updates
            await hubA.SendLocationUpdate(trip1.Id, 59.0 + (i * 0.01), 10.0 + (i * 0.01));
            await hubB.SendLocationUpdate(trip2.Id, 63.0 + (i * 0.01), 10.0 + (i * 0.01));
        }

        // Assert strictly isolated group delivery
        var trip1Group = sharedClients.GroupProxies[$"trip_{trip1.Id}"];
        var trip2Group = sharedClients.GroupProxies[$"trip_{trip2.Id}"];
        var adminsGroup = sharedClients.GroupProxies["Admins"];

        Assert.Equal(iterations, trip1Group.Messages.Count);
        Assert.Equal(iterations, trip2Group.Messages.Count);
        Assert.Equal(iterations * 2, adminsGroup.Messages.Count);

        // Verify every message in trip_1 group has tripId == trip1.Id
        Assert.All(trip1Group.Messages, msg =>
        {
            Assert.Equal("ReceiveLocationUpdate", msg.method);
            Assert.Equal(trip1.Id, msg.args[0]);
        });

        // Verify every message in trip_2 group has tripId == trip2.Id
        Assert.All(trip2Group.Messages, msg =>
        {
            Assert.Equal("ReceiveLocationUpdate", msg.method);
            Assert.Equal(trip2.Id, msg.args[0]);
        });

        // Verify default broadcast proxy received 0 messages
        Assert.Empty(sharedClients.DefaultProxy.Messages);
    }
}
