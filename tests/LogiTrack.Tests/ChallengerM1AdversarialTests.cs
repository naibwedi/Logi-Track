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

public sealed class ChallengerM1AdversarialTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string AdminPassword = "IntegrationTest!2026";
    private const string AdminEmail = "challenger-admin@ruftalogistics.test";
    private const string DriverAEmail = "driver-a@ruftalogistics.test";
    private const string DriverBEmail = "driver-b@ruftalogistics.test";
    private const string CustomerEmail = "challenger-customer@ruftalogistics.test";

    public ChallengerM1AdversarialTests(LogiTrackFactory factory)
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

    private async Task SeedChallengerDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.FindByEmailAsync(AdminEmail) == null)
        {
            var admin = new Admin
            {
                Email = AdminEmail,
                UserName = AdminEmail,
                FirstName = "Challenger",
                LastName = "Admin",
                DateOfBirth = new DateTime(1980, 1, 1),
                RoleType = "Admin",
                IsApproved = true,
                EmailConfirmed = true
            };
            var result = await users.CreateAsync(admin, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(admin, "Admin");
        }

        if (await users.FindByEmailAsync(DriverAEmail) == null)
        {
            var driverA = new Driver
            {
                Email = DriverAEmail,
                UserName = DriverAEmail,
                DriverEmail = DriverAEmail,
                FirstName = "Driver",
                LastName = "Alpha",
                DateOfBirth = new DateTime(1990, 2, 2),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 12.0
            };
            var result = await users.CreateAsync(driverA, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(driverA, "Driver");
        }

        if (await users.FindByEmailAsync(DriverBEmail) == null)
        {
            var driverB = new Driver
            {
                Email = DriverBEmail,
                UserName = DriverBEmail,
                DriverEmail = DriverBEmail,
                FirstName = "Driver",
                LastName = "Bravo",
                DateOfBirth = new DateTime(1991, 3, 3),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 14.0
            };
            var result = await users.CreateAsync(driverB, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(driverB, "Driver");
        }

        if (await users.FindByEmailAsync(CustomerEmail) == null)
        {
            var customer = new Customer
            {
                Email = CustomerEmail,
                UserName = CustomerEmail,
                FirstName = "Challenger",
                LastName = "Customer",
                DateOfBirth = new DateTime(1987, 4, 4),
                RoleType = "Customer",
                IsApproved = true,
                EmailConfirmed = true
            };
            var result = await users.CreateAsync(customer, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(customer, "Customer");
        }
    }

    private async Task<int> CreateApprovedTripAsync(string customerEmail)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var customer = await users.FindByEmailAsync(customerEmail);
        Assert.NotNull(customer);

        var trip = new Trip
        {
            CustomerId = customer.Id,
            FromCity = "Dallas",
            ToCity = "Houston",
            FromAddress = "100 Main St",
            ToAddress = "200 Travis St",
            FromZipCode = "75201",
            ToZipCode = "77002",
            Weight = 25000,
            Volume = 45,
            Distance = 240,
            GoodsType = "Industrial Machinery",
            PickupTime = DateTime.UtcNow.AddDays(2),
            EstimatedPrice = 1400,
            AdminPrice = 1450,
            Status = TripStatus.ApprovedByCustomer,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    // =========================================================================
    // 1. REGISTRATION EXPIRATION ADVERSARIAL TESTS
    // =========================================================================

    [Fact]
    public async Task RegistrationExpiration_PastDate_FailsValidation_AndLeavesTripUnassigned()
    {
        await SeedChallengerDataAsync();

        int vehicleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vehicle = new Vehicle
            {
                UnitNumber = "ADV-EXP-01",
                Vin = "1FUJGLDR8NLHA9101",
                Make = "Freightliner",
                Model = "Cascadia",
                Year = 2021,
                LicensePlate = "TX-9101TR",
                LicenseState = "TX",
                RegistrationExpirationDate = DateTime.UtcNow.AddDays(-10), // Expired 10 days ago
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleId = vehicle.Id;
        }

        int tripId = await CreateApprovedTripAsync(CustomerEmail);

        string driverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var driver = await users.FindByEmailAsync(DriverAEmail);
            driverId = driver!.Id;
            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;
            await db.SaveChangesAsync();
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        var response = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = vehicleId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        // Validation rejection returns the view (HTTP 200 OK) with validation message
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Selected vehicle registration has expired", html);

        // Verify the database state: Trip must remain ApprovedByCustomer, no DriverTrip created
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);
            Assert.NotNull(trip);
            Assert.Equal(TripStatus.ApprovedByCustomer, trip.Status);
            Assert.Null(trip.DriverTrip);
            Assert.Null(trip.VehicleId);

            var driver = await db.Drivers.FindAsync(driverId);
            Assert.True(driver!.IsAvailable); // Driver availability must NOT be consumed
        }
    }

    [Fact]
    public async Task RegistrationExpiration_ExpiredByMinutes_FailsValidation()
    {
        await SeedChallengerDataAsync();

        int vehicleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vehicle = new Vehicle
            {
                UnitNumber = "ADV-EXP-MIN",
                Vin = "1FUJGLDR8NLHA9102",
                Make = "Volvo",
                Model = "VNL 860",
                Year = 2022,
                LicensePlate = "TX-9102TR",
                LicenseState = "TX",
                RegistrationExpirationDate = DateTime.UtcNow.AddMinutes(-5), // Expired 5 minutes ago
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleId = vehicle.Id;
        }

        int tripId = await CreateApprovedTripAsync(CustomerEmail);

        string driverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var driver = await users.FindByEmailAsync(DriverAEmail);
            driverId = driver!.Id;
            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;
            await db.SaveChangesAsync();
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        var response = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = vehicleId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Selected vehicle registration has expired", html);
    }

    [Fact]
    public async Task RegistrationExpiration_DecommissionedStatus_FailsValidation()
    {
        await SeedChallengerDataAsync();

        int vehicleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vehicle = new Vehicle
            {
                UnitNumber = "ADV-DEC-01",
                Vin = "1FUJGLDR8NLHA9103",
                Make = "Kenworth",
                Model = "T680",
                Year = 2019,
                LicensePlate = "TX-9103TR",
                LicenseState = "TX",
                RegistrationExpirationDate = DateTime.UtcNow.AddYears(1), // Valid date
                Status = VehicleStatus.Decommissioned, // But Decommissioned
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleId = vehicle.Id;
        }

        int tripId = await CreateApprovedTripAsync(CustomerEmail);

        string driverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var driver = await users.FindByEmailAsync(DriverAEmail);
            driverId = driver!.Id;
            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;
            await db.SaveChangesAsync();
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        var response = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = vehicleId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("not Active", html);
    }

    // =========================================================================
    // 2. DRIVER & TRACTOR REASSIGNMENT & UNASSIGNMENT ADVERSARIAL TESTS
    // =========================================================================

    [Fact]
    public async Task DriverReassignment_DriverMovesFromVehicle1ToVehicle2_CleansUpPreviousAssignmentWithoutOrphans()
    {
        await SeedChallengerDataAsync();

        int vehicle1Id;
        int vehicle2Id;
        string driverAId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var driverA = await users.FindByEmailAsync(DriverAEmail);
            driverAId = driverA!.Id;

            var v1 = new Vehicle
            {
                UnitNumber = "REASSIGN-V1",
                Vin = "1FUJGLDR8NLHA9201",
                Make = "Freightliner",
                Model = "Cascadia",
                Year = 2022,
                LicensePlate = "IL-9201TR",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddYears(1),
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var v2 = new Vehicle
            {
                UnitNumber = "REASSIGN-V2",
                Vin = "1FUJGLDR8NLHA9202",
                Make = "Volvo",
                Model = "VNL",
                Year = 2023,
                LicensePlate = "IL-9202TR",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddYears(1),
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.AddRange(v1, v2);
            await db.SaveChangesAsync();
            vehicle1Id = v1.Id;
            vehicle2Id = v2.Id;
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);

        // Step 1: Assign Driver A to Vehicle 1
        var token1 = await GetToken(admin, "/Admin/VehicleManagement");
        var assignV1Resp = await admin.PostAsync("/Admin/AssignDriverToVehicle", Form(new()
        {
            ["vehicleId"] = vehicle1Id.ToString(),
            ["driverId"] = driverAId,
            ["__RequestVerificationToken"] = token1
        }));
        Assert.Equal(HttpStatusCode.Redirect, assignV1Resp.StatusCode);

        // Verify Step 1 state
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var v1 = await db.Vehicles.FindAsync(vehicle1Id);
            var driver = await db.Drivers.FindAsync(driverAId);

            Assert.Equal(driverAId, v1!.AssignedDriverId);
            Assert.Equal(vehicle1Id, driver!.VehicleId);
            Assert.Equal(vehicle1Id, driver.AssignedVehicleId);
        }

        // Step 2: Now assign Driver A to Vehicle 2
        var token2 = await GetToken(admin, "/Admin/VehicleManagement");
        var assignV2Resp = await admin.PostAsync("/Admin/AssignDriverToVehicle", Form(new()
        {
            ["vehicleId"] = vehicle2Id.ToString(),
            ["driverId"] = driverAId,
            ["__RequestVerificationToken"] = token2
        }));
        Assert.Equal(HttpStatusCode.Redirect, assignV2Resp.StatusCode);

        // Verify Step 2 state:
        // Vehicle 1 MUST be cleaned up (AssignedDriverId == null). No orphaned pointer!
        // Vehicle 2 MUST have AssignedDriverId == DriverAId.
        // Driver A MUST point to Vehicle 2.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var v1 = await db.Vehicles.FindAsync(vehicle1Id);
            var v2 = await db.Vehicles.FindAsync(vehicle2Id);
            var driver = await db.Drivers.FindAsync(driverAId);

            Assert.Null(v1!.AssignedDriverId); // Cleaned up!
            Assert.Equal(driverAId, v2!.AssignedDriverId);
            Assert.Equal(vehicle2Id, driver!.VehicleId);
            Assert.Equal(vehicle2Id, driver.AssignedVehicleId);
        }
    }

    [Fact]
    public async Task VehicleDriverReassignment_VehicleReplacedWithNewDriver_CleansUpPreviousDriver()
    {
        await SeedChallengerDataAsync();

        int vehicleId;
        string driverAId;
        string driverBId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            driverAId = (await users.FindByEmailAsync(DriverAEmail))!.Id;
            driverBId = (await users.FindByEmailAsync(DriverBEmail))!.Id;

            var v = new Vehicle
            {
                UnitNumber = "REASSIGN-V3",
                Vin = "1FUJGLDR8NLHA9203",
                Make = "Peterbilt",
                Model = "579",
                Year = 2024,
                LicensePlate = "IL-9203TR",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddYears(1),
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(v);
            await db.SaveChangesAsync();
            vehicleId = v.Id;
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);

        // Step 1: Assign Driver A to Vehicle
        var token1 = await GetToken(admin, "/Admin/VehicleManagement");
        await admin.PostAsync("/Admin/AssignDriverToVehicle", Form(new()
        {
            ["vehicleId"] = vehicleId.ToString(),
            ["driverId"] = driverAId,
            ["__RequestVerificationToken"] = token1
        }));

        // Step 2: Now assign Driver B to the SAME Vehicle
        var token2 = await GetToken(admin, "/Admin/VehicleManagement");
        var assignBResp = await admin.PostAsync("/Admin/AssignDriverToVehicle", Form(new()
        {
            ["vehicleId"] = vehicleId.ToString(),
            ["driverId"] = driverBId,
            ["__RequestVerificationToken"] = token2
        }));
        Assert.Equal(HttpStatusCode.Redirect, assignBResp.StatusCode);

        // Verify:
        // Driver A must now have VehicleId == null (not orphaned pointing to vehicle)
        // Vehicle must have AssignedDriverId == Driver B
        // Driver B must have VehicleId == Vehicle
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var v = await db.Vehicles.FindAsync(vehicleId);
            var driverA = await db.Drivers.FindAsync(driverAId);
            var driverB = await db.Drivers.FindAsync(driverBId);

            Assert.Null(driverA!.VehicleId);
            Assert.Null(driverA.AssignedVehicleId);
            Assert.Equal(driverBId, v!.AssignedDriverId);
            Assert.Equal(vehicleId, driverB!.VehicleId);
            Assert.Equal(vehicleId, driverB.AssignedVehicleId);
        }
    }

    [Fact]
    public async Task UnassignDriver_FreesVehicleAndDriver_Cleanly()
    {
        await SeedChallengerDataAsync();

        int vehicleId;
        string driverAId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            driverAId = (await users.FindByEmailAsync(DriverAEmail))!.Id;

            var v = new Vehicle
            {
                UnitNumber = "FREE-V1",
                Vin = "1FUJGLDR8NLHA9204",
                Make = "Kenworth",
                Model = "W990",
                Year = 2023,
                LicensePlate = "IL-9204TR",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddYears(1),
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(v);
            await db.SaveChangesAsync();
            vehicleId = v.Id;
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);

        // Assign first
        var token1 = await GetToken(admin, "/Admin/VehicleManagement");
        await admin.PostAsync("/Admin/AssignDriverToVehicle", Form(new()
        {
            ["vehicleId"] = vehicleId.ToString(),
            ["driverId"] = driverAId,
            ["__RequestVerificationToken"] = token1
        }));

        // Now Unassign via UnassignVehicleDriver
        var token2 = await GetToken(admin, "/Admin/VehicleManagement");
        var unassignResp = await admin.PostAsync("/Admin/UnassignVehicleDriver", Form(new()
        {
            ["vehicleId"] = vehicleId.ToString(),
            ["__RequestVerificationToken"] = token2
        }));
        Assert.Equal(HttpStatusCode.Redirect, unassignResp.StatusCode);

        // Verify both are completely freed
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var v = await db.Vehicles.FindAsync(vehicleId);
            var driverA = await db.Drivers.FindAsync(driverAId);

            Assert.Null(v!.AssignedDriverId);
            Assert.Null(driverA!.VehicleId);
            Assert.Null(driverA.AssignedVehicleId);
        }
    }

    // =========================================================================
    // 3. BACKWARDS COMPATIBILITY FALLBACK ADVERSARIAL TESTS
    // =========================================================================

    [Fact]
    public async Task BackwardsCompatibility_OmittedVehicleId_AutoBindsActiveVehicle_Succeeds()
    {
        await SeedChallengerDataAsync();

        int tripId = await CreateApprovedTripAsync(CustomerEmail);
        string driverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            driverId = (await users.FindByEmailAsync(DriverAEmail))!.Id;

            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;
            driverEntity.VehicleId = null; // Driver has no pre-assigned vehicle
            await db.SaveChangesAsync();
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        // POST omitting VehicleId completely (legacy caller payload)
        var response = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);

            Assert.NotNull(trip);
            Assert.Equal(TripStatus.Assigned, trip.Status);
            Assert.NotNull(trip.VehicleId);
            Assert.True(trip.VehicleId > 0);

            var boundVehicle = await db.Vehicles.FindAsync(trip.VehicleId);
            Assert.NotNull(boundVehicle);
            Assert.Equal(VehicleStatus.Active, boundVehicle.Status);
            Assert.True(boundVehicle.RegistrationExpirationDate >= DateTime.UtcNow);

            Assert.NotNull(trip.DriverTrip);
            Assert.Equal(trip.VehicleId, trip.DriverTrip.VehicleId);
        }
    }

    [Fact]
    public async Task BackwardsCompatibility_ExplicitZeroVehicleId_AutoBindsActiveVehicle_Succeeds()
    {
        await SeedChallengerDataAsync();

        int tripId = await CreateApprovedTripAsync(CustomerEmail);
        string driverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            driverId = (await users.FindByEmailAsync(DriverAEmail))!.Id;

            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;
            driverEntity.VehicleId = null;
            await db.SaveChangesAsync();
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        // POST with VehicleId = "0" (e.g. unselected integer bind)
        var response = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = "0",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);

            Assert.NotNull(trip);
            Assert.Equal(TripStatus.Assigned, trip.Status);
            Assert.NotNull(trip.VehicleId);
            Assert.True(trip.VehicleId > 0);

            var boundVehicle = await db.Vehicles.FindAsync(trip.VehicleId);
            Assert.NotNull(boundVehicle);
            Assert.Equal(VehicleStatus.Active, boundVehicle.Status);
            Assert.NotNull(trip.DriverTrip);
            Assert.Equal(trip.VehicleId, trip.DriverTrip.VehicleId);
        }
    }

    [Fact]
    public async Task BackwardsCompatibility_EmptyStringVehicleId_AutoBindsActiveVehicle_Succeeds()
    {
        await SeedChallengerDataAsync();

        int tripId = await CreateApprovedTripAsync(CustomerEmail);
        string driverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            driverId = (await users.FindByEmailAsync(DriverAEmail))!.Id;

            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;
            await db.SaveChangesAsync();
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        // POST with VehicleId = "" (empty string)
        var response = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = "",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);

            Assert.NotNull(trip);
            Assert.Equal(TripStatus.Assigned, trip.Status);
            Assert.NotNull(trip.VehicleId);
            Assert.True(trip.VehicleId > 0);
        }
    }
}
