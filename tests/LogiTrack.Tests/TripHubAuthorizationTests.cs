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

public sealed class TripHubAuthorizationTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string DefaultPassword = "IntegrationTest!2026";

    public TripHubAuthorizationTests(LogiTrackFactory factory)
    {
        _factory = factory;
    }

    private sealed class TestHubCallerContext(ClaimsPrincipal? user, string connectionId = "conn-test-1") : HubCallerContext
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
        string connectionId = "conn-1")
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
            UserName = "hub_customer_a@test.local",
            Email = "hub_customer_a@test.local",
            FirstName = "CustomerA",
            LastName = "User",
            CompanyName = "Customer Corp A",
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        var customerB = await EnsureUser(users, new Customer
        {
            UserName = "hub_customer_b@test.local",
            Email = "hub_customer_b@test.local",
            FirstName = "CustomerB",
            LastName = "User",
            CompanyName = "Customer Corp B",
            RoleType = "Customer",
            IsApproved = true,
            EmailConfirmed = true
        }, "Customer");

        var driverA = await EnsureUser(users, new Driver
        {
            UserName = "hub_driver_a@test.local",
            Email = "hub_driver_a@test.local",
            FirstName = "DriverA",
            LastName = "User",
            DriverEmail = "hub_driver_a@test.local",
            RoleType = "Driver",
            IsAvailable = true,
            PricePerKm = 10,
            PaymentFreq = PaymentFreq.Monthly,
            IsApproved = true,
            EmailConfirmed = true
        }, "Driver");

        var driverB = await EnsureUser(users, new Driver
        {
            UserName = "hub_driver_b@test.local",
            Email = "hub_driver_b@test.local",
            FirstName = "DriverB",
            LastName = "User",
            DriverEmail = "hub_driver_b@test.local",
            RoleType = "Driver",
            IsAvailable = true,
            PricePerKm = 10,
            PaymentFreq = PaymentFreq.Monthly,
            IsApproved = true,
            EmailConfirmed = true
        }, "Driver");

        var admin = await EnsureUser(users, new Admin
        {
            UserName = "hub_admin@test.local",
            Email = "hub_admin@test.local",
            FirstName = "Admin",
            LastName = "User",
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
            ToCity = "Bergen",
            FromAddress = "Storgata 1",
            ToAddress = "Bryggen 2",
            FromZipCode = "0150",
            ToZipCode = "5003",
            Weight = 50,
            Volume = 2,
            Distance = 460,
            GoodsType = "Electronics",
            PickupTime = DateTime.UtcNow.AddDays(1),
            EstimatedPrice = 5000,
            AdminPrice = 5500,
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

    [Fact]
    public async Task JoinTrip_AnonymousCaller_ThrowsAuthenticationRequired()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (hub, _, _) = CreateHub(db, null);
        var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinTrip(1));
        Assert.Equal("Authentication is required.", ex.Message);
    }

    [Fact]
    public async Task JoinTrip_UnauthorizedCustomer_ThrowsNotAllowedException()
    {
        var (customerAId, customerBId, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId);

        // Customer B tries to join Customer A's trip
        var userB = CreatePrincipal(customerBId, "Customer");
        var (hub, _, _) = CreateHub(db, userB);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinTrip(trip.Id));
        Assert.Equal("You are not allowed to subscribe to this trip.", ex.Message);
    }

    [Fact]
    public async Task JoinTrip_AssignedCustomer_SuccessfullySubscribesToGroup()
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId);

        var userA = CreatePrincipal(customerAId, "Customer");
        var (hub, groups, _) = CreateHub(db, userA, "conn-customer-a");

        await hub.JoinTrip(trip.Id);

        Assert.Contains(groups.AddedGroups, g => g.connectionId == "conn-customer-a" && g.groupName == $"trip_{trip.Id}");
    }

    [Fact]
    public async Task JoinTrip_AssignedDriver_SuccessfullySubscribesToGroup()
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId);

        var driver = CreatePrincipal(driverAId, "Driver");
        var (hub, groups, _) = CreateHub(db, driver, "conn-driver-a");

        await hub.JoinTrip(trip.Id);

        Assert.Contains(groups.AddedGroups, g => g.connectionId == "conn-driver-a" && g.groupName == $"trip_{trip.Id}");
    }

    [Fact]
    public async Task JoinTrip_UnassignedDriver_ThrowsNotAllowedException()
    {
        var (customerAId, _, driverAId, driverBId, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId);

        // Driver B attempts to subscribe to Driver A's trip
        var driverB = CreatePrincipal(driverBId, "Driver");
        var (hub, _, _) = CreateHub(db, driverB);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinTrip(trip.Id));
        Assert.Equal("You are not allowed to subscribe to this trip.", ex.Message);
    }

    [Fact]
    public async Task JoinTrip_Admin_SuccessfullySubscribesToAnyTrip()
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId);

        var adminUser = CreatePrincipal(adminId, "Admin");
        var (hub, groups, _) = CreateHub(db, adminUser, "conn-admin-1");

        await hub.JoinTrip(trip.Id);

        Assert.Contains(groups.AddedGroups, g => g.connectionId == "conn-admin-1" && g.groupName == $"trip_{trip.Id}");
    }

    [Fact]
    public async Task SendLocationUpdate_AnonymousCaller_ThrowsAuthenticationRequired()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (hub, _, _) = CreateHub(db, null);
        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(1, 59.9139, 10.7522));
        Assert.Equal("Authentication is required.", ex.Message);
    }

    [Fact]
    public async Task SendLocationUpdate_NonExistentTrip_ThrowsTripNotFound()
    {
        var (_, _, driverAId, _, _) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var driver = CreatePrincipal(driverAId, "Driver");
        var (hub, _, _) = CreateHub(db, driver);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(999999, 59.9139, 10.7522));
        Assert.Equal("Trip not found.", ex.Message);
    }

    [Theory]
    [InlineData(91.0, 10.0)]
    [InlineData(-91.0, 10.0)]
    [InlineData(60.0, 181.0)]
    [InlineData(60.0, -181.0)]
    [InlineData(double.NaN, 10.0)]
    [InlineData(60.0, double.NaN)]
    [InlineData(double.PositiveInfinity, 10.0)]
    public async Task SendLocationUpdate_InvalidCoordinates_ThrowsInvalidCoordinates(double lat, double lng)
    {
        var (_, _, driverAId, _, _) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var driver = CreatePrincipal(driverAId, "Driver");
        var (hub, _, _) = CreateHub(db, driver);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(1, lat, lng));
        Assert.Equal("Invalid coordinates.", ex.Message);
    }

    [Fact]
    public async Task SendLocationUpdate_CustomerCaller_ThrowsNotAuthorized()
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId);

        var customer = CreatePrincipal(customerAId, "Customer");
        var (hub, _, _) = CreateHub(db, customer);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 59.9139, 10.7522));
        Assert.Equal("You are not authorized to update location for this trip.", ex.Message);
    }

    [Fact]
    public async Task SendLocationUpdate_UnassignedDriverCaller_ThrowsNotAuthorized()
    {
        var (customerAId, _, driverAId, driverBId, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId);

        var driverB = CreatePrincipal(driverBId, "Driver");
        var (hub, _, _) = CreateHub(db, driverB);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 59.9139, 10.7522));
        Assert.Equal("You are not authorized to update location for this trip.", ex.Message);
    }

    [Theory]
    [InlineData(TripStatus.Completed)]
    [InlineData(TripStatus.CancelledByAdmin)]
    [InlineData(TripStatus.CanceledByCustomer)]
    [InlineData(TripStatus.Requested)]
    [InlineData(TripStatus.PriceSet)]
    public async Task SendLocationUpdate_InactiveTrip_ThrowsActiveTripsOnly(TripStatus inactiveStatus)
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, inactiveStatus);

        var driverA = CreatePrincipal(driverAId, "Driver");
        var (hub, _, _) = CreateHub(db, driverA);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.SendLocationUpdate(trip.Id, 59.9139, 10.7522));
        Assert.Equal("Location updates are only allowed for active trips.", ex.Message);
    }

    [Fact]
    public async Task SendLocationUpdate_AssignedDriver_TransitionsToInProgress_UpdatesDatabase_AndBroadcasts()
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, TripStatus.Assigned);

        var driverA = CreatePrincipal(driverAId, "Driver");
        var (hub, _, clients) = CreateHub(db, driverA);

        const double testLat = 60.1234;
        const double testLng = 10.5678;

        await hub.SendLocationUpdate(trip.Id, testLat, testLng);

        // 1. Verify trip and driverTrip status transitioned to InProgress
        var refreshedTrip = await db.Trips.Include(t => t.DriverTrip).FirstAsync(t => t.Id == trip.Id);
        Assert.Equal(TripStatus.InProgress, refreshedTrip.Status);
        Assert.NotNull(refreshedTrip.DriverTrip);
        Assert.Equal(TripStatus.InProgress, refreshedTrip.DriverTrip.Status);

        // 2. Verify driver Location entity in database was created or updated
        var location = await db.Locations.FirstOrDefaultAsync(l => l.DriverID == driverAId);
        Assert.NotNull(location);
        Assert.Equal(testLat, location.Latitude);
        Assert.Equal(testLng, location.Longitude);

        // 3. Verify broadcast to trip group
        var tripGroup = clients.GroupProxies[$"trip_{trip.Id}"];
        Assert.Single(tripGroup.Messages);
        var (method, args) = tripGroup.Messages[0];
        Assert.Equal("ReceiveLocationUpdate", method);
        Assert.Equal(trip.Id, args[0]);
        Assert.Equal(testLat, args[1]);
        Assert.Equal(testLng, args[2]);

        // 4. Verify broadcast to Admins group
        var adminsGroup = clients.GroupProxies["Admins"];
        Assert.Single(adminsGroup.Messages);
        Assert.Equal("ReceiveLocationUpdate", adminsGroup.Messages[0].method);
    }

    [Fact]
    public async Task SendLocationUpdate_Admin_CanBroadcastForActiveTrip()
    {
        var (customerAId, _, driverAId, _, adminId) = await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trip = await CreateTripAsync(db, customerAId, driverAId, adminId, TripStatus.InProgress);

        var adminUser = CreatePrincipal(adminId, "Admin");
        var (hub, _, clients) = CreateHub(db, adminUser);

        const double testLat = 61.5555;
        const double testLng = 8.4444;

        await hub.SendLocationUpdate(trip.Id, testLat, testLng);

        var tripGroup = clients.GroupProxies[$"trip_{trip.Id}"];
        Assert.Single(tripGroup.Messages);
        Assert.Equal("ReceiveLocationUpdate", tripGroup.Messages[0].method);
        Assert.Equal(trip.Id, tripGroup.Messages[0].args[0]);
        Assert.Equal(testLat, tripGroup.Messages[0].args[1]);
        Assert.Equal(testLng, tripGroup.Messages[0].args[2]);
    }
}
