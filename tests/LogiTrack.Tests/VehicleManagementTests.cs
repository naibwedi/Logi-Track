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

public sealed class VehicleManagementTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string AdminPassword = "IntegrationTest!2026";
    private const string AdminEmail = "fleet-admin@ruftalogistics.test";
    private const string DriverEmail = "fleet-driver@ruftalogistics.test";
    private const string CustomerEmail = "fleet-customer@ruftalogistics.test";

    public VehicleManagementTests(LogiTrackFactory factory)
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
                FirstName = "Fleet",
                LastName = "Admin",
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
                FirstName = "Amanuel",
                LastName = "Tekle",
                DateOfBirth = new DateTime(1992, 5, 10),
                RoleType = "Driver",
                IsApproved = true,
                EmailConfirmed = true,
                IsAvailable = true,
                PricePerKm = 15.0
            };
            var result = await users.CreateAsync(driver, AdminPassword);
            Assert.True(result.Succeeded);
            await users.AddToRoleAsync(driver, "Driver");
        }

        if (await users.FindByEmailAsync(CustomerEmail) == null)
        {
            var customer = new Customer
            {
                Email = CustomerEmail,
                UserName = CustomerEmail,
                FirstName = "Shipper",
                LastName = "Client",
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

    [Fact]
    public async Task Fleet_SeedData_InitializesRuftaUnits_4604_1717_6328()
    {
        await SeedUsersAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var unit4604 = await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == "4604");
        Assert.NotNull(unit4604);
        Assert.Equal("1FUJGLDR8NLHA4604", unit4604.Vin);
        Assert.Equal("Freightliner", unit4604.Make);
        Assert.Equal("Cascadia", unit4604.Model);
        Assert.Equal(VehicleStatus.Active, unit4604.Status);
        Assert.Equal("IL-4604TR", unit4604.LicensePlate);

        var unit1717 = await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == "1717");
        Assert.NotNull(unit1717);
        Assert.Equal("4V4NC9EH5PN171701", unit1717.Vin);
        Assert.Equal("Volvo", unit1717.Make);
        Assert.Equal(VehicleStatus.Active, unit1717.Status);

        var unit6328 = await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == "6328");
        Assert.NotNull(unit6328);
        Assert.Equal("1XKYDP9X5MJ632801", unit6328.Vin);
        Assert.Equal("Kenworth", unit6328.Make);
        Assert.Equal(VehicleStatus.Maintenance, unit6328.Status);
    }

    [Fact]
    public async Task Admin_CanAccess_VehicleManagement_Page()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        var response = await admin.GetAsync("/Admin/VehicleManagement");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Fleet & Equipment Management", html);
        Assert.Contains("4604", html);
        Assert.Contains("1717", html);
        Assert.Contains("6328", html);
        Assert.Contains("Cascadia", html);
        Assert.Contains("Active", html);
        Assert.Contains("Maintenance", html);

        // Also verify /Admin/Vehicles route alias
        var aliasResp = await admin.GetAsync("/Admin/Vehicles");
        Assert.Equal(HttpStatusCode.OK, aliasResp.StatusCode);
    }

    [Fact]
    public async Task Admin_CreateVehicle_WithValidDetails_PersistsToDatabase()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        var token = await GetToken(admin, "/Admin/CreateVehicle");
        var createResponse = await admin.PostAsync("/Admin/CreateVehicle", Form(new()
        {
            ["UnitNumber"] = "8899",
            ["Vin"] = "1FUJGLDR8NLHA8899",
            ["Make"] = "Peterbilt",
            ["Model"] = "579",
            ["Year"] = "2024",
            ["LicensePlate"] = "IL-8899TR",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["AnnualDotInspectionExpiration"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["Form2290ExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["PrePassTransponderId"] = "PP-889901",
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == "8899");
        Assert.NotNull(created);
        Assert.Equal("1FUJGLDR8NLHA8899", created.Vin);
        Assert.Equal("Peterbilt", created.Make);
        Assert.Equal(VehicleStatus.Active, created.Status);
    }

    [Fact]
    public async Task Admin_CreateVehicle_WithInvalidVin_RejectsValidation()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        var token = await GetToken(admin, "/Admin/CreateVehicle");
        var createResponse = await admin.PostAsync("/Admin/CreateVehicle", Form(new()
        {
            ["UnitNumber"] = "7777",
            ["Vin"] = "INVALID_VIN_SHORT", // Not 17 valid alphanumeric chars
            ["Make"] = "Freightliner",
            ["Model"] = "Cascadia",
            ["Year"] = "2023",
            ["LicensePlate"] = "IL-7777TR",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var html = await createResponse.Content.ReadAsStringAsync();
        Assert.Contains("VIN must be", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == "7777"));
    }

    [Fact]
    public async Task Admin_CreateVehicle_DuplicateUnitNumber_Rejects()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        var token = await GetToken(admin, "/Admin/CreateVehicle");
        var createResponse = await admin.PostAsync("/Admin/CreateVehicle", Form(new()
        {
            ["UnitNumber"] = "4604", // already seeded
            ["Vin"] = "4V4NC9EH5PN999999",
            ["Make"] = "Freightliner",
            ["Model"] = "Cascadia",
            ["Year"] = "2023",
            ["LicensePlate"] = "IL-DUPTR",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var html = await createResponse.Content.ReadAsStringAsync();
        Assert.Contains("A vehicle with this Unit Number already exists", html);
    }

    [Fact]
    public async Task Admin_EditVehicle_UpdatesProperties_Successfully()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        int vehicleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var testVehicle = new Vehicle
            {
                UnitNumber = "5555",
                Vin = "1FUJGLDR8NLHA5555",
                Make = "Peterbilt",
                Model = "579 Standard",
                Year = 2022,
                LicensePlate = "IL-5555TR",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddMonths(12),
                PrePassTransponderId = "PP-555501",
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(testVehicle);
            await db.SaveChangesAsync();
            vehicleId = testVehicle.Id;
        }

        var token = await GetToken(admin, $"/Admin/EditVehicle/{vehicleId}");
        var editResponse = await admin.PostAsync($"/Admin/EditVehicle/{vehicleId}", Form(new()
        {
            ["Id"] = vehicleId.ToString(),
            ["UnitNumber"] = "5555",
            ["Vin"] = "1FUJGLDR8NLHA5555",
            ["Make"] = "Peterbilt",
            ["Model"] = "579 Ultraloft",
            ["Year"] = "2022",
            ["LicensePlate"] = "IL-5555MOD",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddMonths(12).ToString("yyyy-MM-dd"),
            ["PrePassTransponderId"] = "PP-5555MOD",
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, editResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updated = await db.Vehicles.FindAsync(vehicleId);
            Assert.NotNull(updated);
            Assert.Equal("579 Ultraloft", updated.Model);
            Assert.Equal("IL-5555MOD", updated.LicensePlate);
            Assert.Equal("PP-5555MOD", updated.PrePassTransponderId);
        }
    }

    [Fact]
    public async Task Admin_SetVehicleStatus_TogglesStatus_Successfully()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        int vehicleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var testVehicle = new Vehicle
            {
                UnitNumber = "5556",
                Vin = "1FUJGLDR8NLHA5556",
                Make = "Volvo",
                Model = "VNL",
                Year = 2023,
                LicensePlate = "IL-5556TR",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddMonths(12),
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(testVehicle);
            await db.SaveChangesAsync();
            vehicleId = testVehicle.Id;
        }

        var token = await GetToken(admin, "/Admin/VehicleManagement");
        var toggleResponse = await admin.PostAsync("/Admin/SetVehicleStatus", Form(new()
        {
            ["id"] = vehicleId.ToString(),
            ["status"] = "Maintenance",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, toggleResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vehicle = await db.Vehicles.FindAsync(vehicleId);
            Assert.NotNull(vehicle);
            Assert.Equal(VehicleStatus.Maintenance, vehicle.Status);
        }
    }

    [Fact]
    public async Task Admin_AssignDriverToVehicle_And_Unassign_Works()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        int vehicleId;
        string driverId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            vehicleId = (await db.Vehicles.FirstAsync(v => v.UnitNumber == "4604")).Id;
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            driverId = (await users.FindByEmailAsync(DriverEmail))!.Id;
        }

        var token = await GetToken(admin, "/Admin/VehicleManagement");
        var assignResponse = await admin.PostAsync("/Admin/AssignDriverToVehicle", Form(new()
        {
            ["vehicleId"] = vehicleId.ToString(),
            ["driverId"] = driverId,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, assignResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vehicle = await db.Vehicles.FindAsync(vehicleId);
            var driver = await db.Drivers.FindAsync(driverId);

            Assert.Equal(driverId, vehicle!.AssignedDriverId);
            Assert.Equal(vehicleId, driver!.VehicleId);
            Assert.Equal(vehicleId, driver.AssignedVehicleId);
        }

        // Now unassign
        token = await GetToken(admin, "/Admin/VehicleManagement");
        var unassignResponse = await admin.PostAsync("/Admin/UnassignVehicleDriver", Form(new()
        {
            ["vehicleId"] = vehicleId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, unassignResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vehicle = await db.Vehicles.FindAsync(vehicleId);
            var driver = await db.Drivers.FindAsync(driverId);

            Assert.Null(vehicle!.AssignedDriverId);
            Assert.Null(driver!.VehicleId);
        }
    }

    [Fact]
    public async Task Admin_AssignTrip_WithActiveVehicle_PersistsVehicleOnTripAndDriverTrip()
    {
        await SeedUsersAsync();

        int tripId;
        int activeVehicleId;
        string driverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var customer = await users.FindByEmailAsync(CustomerEmail);
            var driver = await users.FindByEmailAsync(DriverEmail);
            driverId = driver!.Id;

            // Make sure driver is available
            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;

            var activeVehicle = await db.Vehicles.FirstAsync(v => v.Status == VehicleStatus.Active);
            activeVehicleId = activeVehicle.Id;

            var trip = new Trip
            {
                CustomerId = customer!.Id,
                FromCity = "Chicago",
                ToCity = "Indianapolis",
                FromAddress = "100 S Wacker Dr",
                ToAddress = "1 Monument Cir",
                FromZipCode = "60606",
                ToZipCode = "46204",
                Weight = 18000,
                Volume = 50,
                Distance = 295,
                GoodsType = "Commercial Freight",
                PickupTime = DateTime.UtcNow.AddDays(1),
                EstimatedPrice = 1200,
                AdminPrice = 1250,
                Status = TripStatus.ApprovedByCustomer,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        var assignResp = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = activeVehicleId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, assignResp.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);

            Assert.NotNull(trip);
            Assert.Equal(TripStatus.Assigned, trip.Status);
            Assert.Equal(activeVehicleId, trip.VehicleId);
            Assert.NotNull(trip.DriverTrip);
            Assert.Equal(activeVehicleId, trip.DriverTrip.VehicleId);
        }
    }

    [Fact]
    public async Task Admin_AssignTrip_WithMaintenanceVehicle_IsRejected()
    {
        await SeedUsersAsync();

        int tripId;
        int maintenanceVehicleId;
        string driverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var customer = await users.FindByEmailAsync(CustomerEmail);
            var driver = await users.FindByEmailAsync(DriverEmail);
            driverId = driver!.Id;

            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;

            var maintenanceVehicle = await db.Vehicles.FirstAsync(v => v.Status == VehicleStatus.Maintenance);
            maintenanceVehicleId = maintenanceVehicle.Id;

            var trip = new Trip
            {
                CustomerId = customer!.Id,
                FromCity = "Chicago",
                ToCity = "Detroit",
                FromAddress = "200 Michigan Ave",
                ToAddress = "500 Woodward Ave",
                FromZipCode = "60601",
                ToZipCode = "48226",
                Weight = 12000,
                Volume = 40,
                Distance = 455,
                GoodsType = "Auto Parts",
                PickupTime = DateTime.UtcNow.AddDays(1),
                EstimatedPrice = 1500,
                AdminPrice = 1600,
                Status = TripStatus.ApprovedByCustomer,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        var assignResp = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = maintenanceVehicleId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);
        var html = await assignResp.Content.ReadAsStringAsync();
        Assert.Contains("not Active", html);

        // Verify trip was NOT assigned
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.FindAsync(tripId);
            Assert.Equal(TripStatus.ApprovedByCustomer, trip!.Status);
        }
    }

    [Fact]
    public async Task Admin_AssignTrip_WithExpiredRegistration_IsRejected()
    {
        await SeedUsersAsync();

        int tripId;
        int expiredVehicleId;
        string driverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var customer = await users.FindByEmailAsync(CustomerEmail);
            var driver = await users.FindByEmailAsync(DriverEmail);
            driverId = driver!.Id;

            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;

            var expiredVehicle = new Vehicle
            {
                UnitNumber = "9901",
                Vin = "1FUJGLDR8NLHA9901",
                Make = "Volvo",
                Model = "VNL",
                Year = 2020,
                LicensePlate = "IL-9901TR",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddDays(-5), // Expired
                Status = VehicleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(expiredVehicle);

            var trip = new Trip
            {
                CustomerId = customer!.Id,
                FromCity = "Chicago",
                ToCity = "Milwaukee",
                FromAddress = "300 State St",
                ToAddress = "400 Water St",
                FromZipCode = "60610",
                ToZipCode = "53202",
                Weight = 10000,
                Volume = 30,
                Distance = 145,
                GoodsType = "Consumer Goods",
                PickupTime = DateTime.UtcNow.AddDays(1),
                EstimatedPrice = 800,
                AdminPrice = 850,
                Status = TripStatus.ApprovedByCustomer,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
            expiredVehicleId = expiredVehicle.Id;
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        var assignResp = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = expiredVehicleId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);
        var html = await assignResp.Content.ReadAsStringAsync();
        Assert.Contains("registration has expired", html);
    }

    [Fact]
    public async Task Admin_AssignTrip_LegacyInvocation_WithoutVehicleId_AutoBindsActiveVehicle()
    {
        await SeedUsersAsync();

        int tripId;
        string driverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var customer = await users.FindByEmailAsync(CustomerEmail);
            var driver = await users.FindByEmailAsync(DriverEmail);
            driverId = driver!.Id;

            var driverEntity = await db.Drivers.FindAsync(driverId);
            driverEntity!.IsAvailable = true;

            var trip = new Trip
            {
                CustomerId = customer!.Id,
                FromCity = "Rockford",
                ToCity = "Peoria",
                FromAddress = "10 Main St",
                ToAddress = "20 Elm St",
                FromZipCode = "61101",
                ToZipCode = "61602",
                Weight = 15000,
                Volume = 35,
                Distance = 210,
                GoodsType = "Manufacturing Parts",
                PickupTime = DateTime.UtcNow.AddDays(1),
                EstimatedPrice = 1100,
                AdminPrice = 1150,
                Status = TripStatus.ApprovedByCustomer,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        // POST without VehicleId to simulate legacy test call
        var assignResp = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, assignResp.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);

            Assert.NotNull(trip);
            Assert.Equal(TripStatus.Assigned, trip.Status);
            // Verify that an active vehicle was auto-bound
            Assert.NotNull(trip.VehicleId);
            var assignedVehicle = await db.Vehicles.FindAsync(trip.VehicleId);
            Assert.NotNull(assignedVehicle);
            Assert.Equal(VehicleStatus.Active, assignedVehicle.Status);
            Assert.NotNull(trip.DriverTrip);
            Assert.Equal(trip.VehicleId, trip.DriverTrip.VehicleId);
        }
    }
}
