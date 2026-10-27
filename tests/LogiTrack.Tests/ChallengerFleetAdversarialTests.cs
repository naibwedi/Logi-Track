using System.ComponentModel.DataAnnotations;
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

/// <summary>
/// Adversarial verification suite for Milestone M1 (Fleet & Equipment Management).
/// Rigorously tests ISO-3779 VIN validation, vehicle lifecycle status gates on dispatch (Maintenance, Decommissioned),
/// uniqueness invariants for Unit Number and VIN, and database relational integrity.
/// </summary>
public sealed class ChallengerFleetAdversarialTests : IClassFixture<LogiTrackFactory>
{
    private readonly LogiTrackFactory _factory;
    private const string AdminPassword = "IntegrationTest!2026";
    private const string AdminEmail = "fleet-admin@ruftalogistics.test";
    private const string DriverEmail = "fleet-driver@ruftalogistics.test";
    private const string CustomerEmail = "fleet-customer@ruftalogistics.test";

    public ChallengerFleetAdversarialTests(LogiTrackFactory factory)
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

    #region 1. VIN Validation Tests (ISO-3779 & Length Constraints)

    [Theory]
    [InlineData("1FUJGLDR8NLHA460")] // 16 characters (too short)
    [InlineData("1FU")]              // 3 characters (too short)
    [InlineData("")]                 // Empty string
    public void Vehicle_Vin_Under17Characters_FailsValidation(string invalidVin)
    {
        var vehicle = new Vehicle
        {
            UnitNumber = "TEST01",
            Vin = invalidVin,
            Make = "Freightliner",
            Model = "Cascadia",
            Year = 2023,
            LicensePlate = "IL-TEST01",
            LicenseState = "IL",
            RegistrationExpirationDate = DateTime.UtcNow.AddYears(1)
        };

        var context = new ValidationContext(vehicle);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(vehicle, context, results, validateAllProperties: true);

        Assert.False(isValid, $"VIN '{invalidVin}' (length {invalidVin.Length}) should have failed validation.");
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Vehicle.Vin)));
    }

    [Theory]
    [InlineData("1FUJGLDR8NLHA46041")] // 18 characters (too long)
    [InlineData("1FUJGLDR8NLHA460412345")] // 22 characters (too long)
    public void Vehicle_Vin_Over17Characters_FailsValidation(string invalidVin)
    {
        var vehicle = new Vehicle
        {
            UnitNumber = "TEST02",
            Vin = invalidVin,
            Make = "Freightliner",
            Model = "Cascadia",
            Year = 2023,
            LicensePlate = "IL-TEST02",
            LicenseState = "IL",
            RegistrationExpirationDate = DateTime.UtcNow.AddYears(1)
        };

        var context = new ValidationContext(vehicle);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(vehicle, context, results, validateAllProperties: true);

        Assert.False(isValid, $"VIN '{invalidVin}' (length {invalidVin.Length}) should have failed validation.");
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Vehicle.Vin)));
    }

    [Theory]
    [InlineData("1FUJGLDR8NLHAI604")] // Contains 'I' (forbidden by ISO-3779)
    [InlineData("IFUJGLDR8NLHA4604")] // Starts with 'I'
    [InlineData("1FUJGLDR8NLHA460I")] // Ends with 'I'
    [InlineData("1FUJGLDR8NLHAO604")] // Contains 'O' (forbidden by ISO-3779)
    [InlineData("OFUJGLDR8NLHA4604")] // Starts with 'O'
    [InlineData("1FUJGLDR8NLHA460O")] // Ends with 'O'
    [InlineData("1FUJGLDR8NLHAQ604")] // Contains 'Q' (forbidden by ISO-3779)
    [InlineData("QFUJGLDR8NLHA4604")] // Starts with 'Q'
    [InlineData("1FUJGLDR8NLHA460Q")] // Ends with 'Q'
    [InlineData("1FUJGLDR8NLHAIOQ4")] // Contains all three 'I', 'O', 'Q'
    public void Vehicle_Vin_WithForbiddenChars_I_O_Q_FailsValidation(string invalidVin)
    {
        var vehicle = new Vehicle
        {
            UnitNumber = "TEST03",
            Vin = invalidVin,
            Make = "Freightliner",
            Model = "Cascadia",
            Year = 2023,
            LicensePlate = "IL-TEST03",
            LicenseState = "IL",
            RegistrationExpirationDate = DateTime.UtcNow.AddYears(1)
        };

        var context = new ValidationContext(vehicle);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(vehicle, context, results, validateAllProperties: true);

        Assert.False(isValid, $"VIN '{invalidVin}' containing forbidden characters (I, O, Q) should have failed validation.");
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Vehicle.Vin)));
    }

    [Theory]
    [InlineData("1FUJGLDR8NLHA-604")] // Hyphen
    [InlineData("1FUJGLDR8NLHA 604")] // Space
    [InlineData("1FUJGLDR8NLHA_604")] // Underscore
    [InlineData("1FUJGLDR8NLHA!604")] // Exclamation mark
    public void Vehicle_Vin_WithSpecialCharacters_FailsValidation(string invalidVin)
    {
        var vehicle = new Vehicle
        {
            UnitNumber = "TEST04",
            Vin = invalidVin,
            Make = "Volvo",
            Model = "VNL",
            Year = 2023,
            LicensePlate = "IL-TEST04",
            LicenseState = "IL",
            RegistrationExpirationDate = DateTime.UtcNow.AddYears(1)
        };

        var context = new ValidationContext(vehicle);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(vehicle, context, results, validateAllProperties: true);

        Assert.False(isValid, $"VIN '{invalidVin}' containing special characters should have failed validation.");
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Vehicle.Vin)));
    }

    [Theory]
    [InlineData("1FUJGLDR8NLHA4604")] // Freightliner Cascadia
    [InlineData("4V4NC9EH5PN171701")] // Volvo VNL 860
    [InlineData("1XKYDP9X5MJ632801")] // Kenworth T680
    [InlineData("2NP2LJ0X8MM123456")] // Peterbilt 579
    [InlineData("1M2AX19C7PM654321")] // Mack Anthem
    [InlineData("3AKJHHDR5LS987654")] // Western Star 5700XE
    public void Vehicle_Vin_Valid17CharIso3779_PassesValidation(string validVin)
    {
        var vehicle = new Vehicle
        {
            UnitNumber = "VALID01",
            Vin = validVin,
            Make = "Freightliner",
            Model = "Cascadia",
            Year = 2023,
            LicensePlate = "IL-VALID1",
            LicenseState = "IL",
            RegistrationExpirationDate = DateTime.UtcNow.AddYears(1)
        };

        var context = new ValidationContext(vehicle);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(vehicle, context, results, validateAllProperties: true);

        Assert.True(isValid, $"Valid 17-char VIN '{validVin}' should have passed validation but produced errors: " +
                             string.Join("; ", results.Select(r => r.ErrorMessage)));
    }

    [Fact]
    public async Task Admin_CreateVehicle_With16CharVin_IsRejectedByController()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        var token = await GetToken(admin, "/Admin/CreateVehicle");
        var postResponse = await admin.PostAsync("/Admin/CreateVehicle", Form(new()
        {
            ["UnitNumber"] = "CHALLENGE16",
            ["Vin"] = "1FUJGLDR8NLHA460", // 16 chars
            ["Make"] = "Freightliner",
            ["Model"] = "Cascadia",
            ["Year"] = "2023",
            ["LicensePlate"] = "IL-CH16",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode); // Returns view with validation error
        var html = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("VIN must be", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == "CHALLENGE16"));
    }

    [Fact]
    public async Task Admin_CreateVehicle_With18CharVin_IsRejectedByController()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        var token = await GetToken(admin, "/Admin/CreateVehicle");
        var postResponse = await admin.PostAsync("/Admin/CreateVehicle", Form(new()
        {
            ["UnitNumber"] = "CHALLENGE18",
            ["Vin"] = "1FUJGLDR8NLHA46041", // 18 chars
            ["Make"] = "Freightliner",
            ["Model"] = "Cascadia",
            ["Year"] = "2023",
            ["LicensePlate"] = "IL-CH18",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var html = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("VIN must be", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == "CHALLENGE18"));
    }

    [Theory]
    [InlineData("1FUJGLDR8NLHAI604", "CHALLENGE_I")] // Contains 'I'
    [InlineData("1FUJGLDR8NLHAO604", "CHALLENGE_O")] // Contains 'O'
    [InlineData("1FUJGLDR8NLHAQ604", "CHALLENGE_Q")] // Contains 'Q'
    public async Task Admin_CreateVehicle_WithForbiddenChars_IsRejectedByController(string forbiddenVin, string unitNumber)
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        var token = await GetToken(admin, "/Admin/CreateVehicle");
        var postResponse = await admin.PostAsync("/Admin/CreateVehicle", Form(new()
        {
            ["UnitNumber"] = unitNumber,
            ["Vin"] = forbiddenVin,
            ["Make"] = "Freightliner",
            ["Model"] = "Cascadia",
            ["Year"] = "2023",
            ["LicensePlate"] = $"IL-{unitNumber}",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var html = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("VIN must be", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == unitNumber));
    }

    #endregion

    #region 2. Equipment Status Constraints & AssignTrip Tests

    [Fact]
    public async Task AssignTrip_WithMaintenanceVehicle_IsPrevented_AndTripRemainsUnassigned()
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

            // Ensure we have a Maintenance vehicle
            var maintenanceVehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Status == VehicleStatus.Maintenance);
            if (maintenanceVehicle == null)
            {
                maintenanceVehicle = new Vehicle
                {
                    UnitNumber = "MAINT-99",
                    Vin = "1FUJGLDR8NLHAM999",
                    Make = "Freightliner",
                    Model = "Cascadia",
                    Year = 2021,
                    LicensePlate = "IL-MNT99",
                    LicenseState = "IL",
                    RegistrationExpirationDate = DateTime.UtcNow.AddMonths(6),
                    Status = VehicleStatus.Maintenance,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Vehicles.Add(maintenanceVehicle);
                await db.SaveChangesAsync();
            }
            maintenanceVehicleId = maintenanceVehicle.Id;

            var trip = new Trip
            {
                CustomerId = customer!.Id,
                FromCity = "Peoria",
                ToCity = "Chicago",
                FromAddress = "100 Riverfront Dr",
                ToAddress = "200 Michigan Ave",
                FromZipCode = "61602",
                ToZipCode = "60601",
                Weight = 25000,
                Volume = 60,
                Distance = 270,
                GoodsType = "Heavy Equipment",
                PickupTime = DateTime.UtcNow.AddDays(2),
                EstimatedPrice = 1800,
                AdminPrice = 1900,
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

        // Assert: HTTP OK (not redirected), form returned with error
        Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);
        var html = await assignResp.Content.ReadAsStringAsync();
        Assert.Contains("not Active", html);
        Assert.Contains("Maintenance", html);

        // Verify database state: Trip must still be ApprovedByCustomer, DriverTrip must not exist
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);
            Assert.NotNull(trip);
            Assert.Equal(TripStatus.ApprovedByCustomer, trip.Status);
            Assert.Null(trip.VehicleId);
            Assert.Null(trip.DriverTrip);

            // Driver must still be available
            var driver = await db.Drivers.FindAsync(driverId);
            Assert.True(driver!.IsAvailable);
        }
    }

    [Fact]
    public async Task AssignTrip_WithDecommissionedVehicle_IsPrevented_AndTripRemainsUnassigned()
    {
        await SeedUsersAsync();

        int tripId;
        int decommissionedVehicleId;
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

            // Create a Decommissioned vehicle
            var decommissionedVehicle = new Vehicle
            {
                UnitNumber = "DECOM-01",
                Vin = "1FUJGLDR8NLHADEC01",
                Make = "Volvo",
                Model = "VNL 670",
                Year = 2015,
                LicensePlate = "IL-DEC01",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddMonths(1),
                Status = VehicleStatus.Decommissioned,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Vehicles.Add(decommissionedVehicle);

            var trip = new Trip
            {
                CustomerId = customer!.Id,
                FromCity = "Aurora",
                ToCity = "Rockford",
                FromAddress = "50 Galena Blvd",
                ToAddress = "10 State St",
                FromZipCode = "60506",
                ToZipCode = "61101",
                Weight = 14000,
                Volume = 35,
                Distance = 110,
                GoodsType = "Palletized Freight",
                PickupTime = DateTime.UtcNow.AddDays(1),
                EstimatedPrice = 750,
                AdminPrice = 800,
                Status = TripStatus.ApprovedByCustomer,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
            decommissionedVehicleId = decommissionedVehicle.Id;
        }

        var admin = await LoginClient(AdminEmail, AdminPassword);
        var token = await GetToken(admin, $"/Admin/AssignTrip/{tripId}");

        var assignResp = await admin.PostAsync("/Admin/AssignTrip", Form(new()
        {
            ["TripId"] = tripId.ToString(),
            ["DriverId"] = driverId,
            ["VehicleId"] = decommissionedVehicleId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        // Assert: HTTP OK, validation error returned
        Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);
        var html = await assignResp.Content.ReadAsStringAsync();
        Assert.Contains("not Active", html);
        Assert.Contains("Decommissioned", html);

        // Verify database state: Trip was not assigned
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);
            Assert.NotNull(trip);
            Assert.Equal(TripStatus.ApprovedByCustomer, trip.Status);
            Assert.Null(trip.VehicleId);
            Assert.Null(trip.DriverTrip);
        }
    }

    #endregion

    #region 3. Uniqueness Constraints (Unit Number & VIN)

    [Fact]
    public async Task Admin_CreateVehicle_DuplicateUnitNumber_ReturnsValidationError()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        // Unit 4604 is already seeded in the database
        var token = await GetToken(admin, "/Admin/CreateVehicle");
        var response = await admin.PostAsync("/Admin/CreateVehicle", Form(new()
        {
            ["UnitNumber"] = "4604", // duplicate
            ["Vin"] = "1FUJGLDR8NLHA9999", // unique VIN
            ["Make"] = "Kenworth",
            ["Model"] = "T680",
            ["Year"] = "2023",
            ["LicensePlate"] = "IL-DUP01",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("A vehicle with this Unit Number already exists", html);

        // Ensure no extra vehicle was created with that duplicate unit
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var matchingUnits = await db.Vehicles.Where(v => v.UnitNumber == "4604").ToListAsync();
        Assert.Single(matchingUnits);
    }

    [Fact]
    public async Task Admin_CreateVehicle_DuplicateVin_ReturnsValidationError()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        // VIN 1FUJGLDR8NLHA4604 is already seeded for unit 4604
        var token = await GetToken(admin, "/Admin/CreateVehicle");
        var response = await admin.PostAsync("/Admin/CreateVehicle", Form(new()
        {
            ["UnitNumber"] = "9988", // unique unit number
            ["Vin"] = "1FUJGLDR8NLHA4604", // duplicate VIN
            ["Make"] = "Kenworth",
            ["Model"] = "T680",
            ["Year"] = "2023",
            ["LicensePlate"] = "IL-DUPVIN",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("A vehicle with this VIN already exists", html);

        // Ensure vehicle 9988 was not persisted
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await db.Vehicles.FirstOrDefaultAsync(v => v.UnitNumber == "9988"));
    }

    [Fact]
    public async Task Database_UniqueIndex_EnforcesUnitNumberUniqueness_ThrowsDbUpdateException()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Find an existing vehicle unit
        var existing = await db.Vehicles.FirstAsync();

        var duplicateUnitVehicle = new Vehicle
        {
            UnitNumber = existing.UnitNumber, // duplicate
            Vin = "1FUJGLDR8NLHADB01",      // distinct
            Make = "TestMake",
            Model = "TestModel",
            Year = 2022,
            LicensePlate = "IL-DBDUP1",
            LicenseState = "IL",
            RegistrationExpirationDate = DateTime.UtcNow.AddYears(1),
            Status = VehicleStatus.Active
        };

        db.Vehicles.Add(duplicateUnitVehicle);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_UniqueIndex_EnforcesVinUniqueness_ThrowsDbUpdateException()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await db.Vehicles.FirstAsync();

        var duplicateVinVehicle = new Vehicle
        {
            UnitNumber = "DB-UNIQUE-99",     // distinct
            Vin = existing.Vin,              // duplicate
            Make = "TestMake",
            Model = "TestModel",
            Year = 2022,
            LicensePlate = "IL-DBDUP2",
            LicenseState = "IL",
            RegistrationExpirationDate = DateTime.UtcNow.AddYears(1),
            Status = VehicleStatus.Active
        };

        db.Vehicles.Add(duplicateVinVehicle);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    [Fact]
    public async Task Admin_EditVehicle_DuplicateUnitOrVinFromOtherVehicle_IsRejected()
    {
        await SeedUsersAsync();
        var admin = await LoginClient(AdminEmail, AdminPassword);

        int vehicleIdToEdit;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vehicleToEdit = new Vehicle
            {
                UnitNumber = "7701",
                Vin = "1FUJGLDR8NLHA7701",
                Make = "Peterbilt",
                Model = "579",
                Year = 2022,
                LicensePlate = "IL-7701TR",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddMonths(12),
                Status = VehicleStatus.Active
            };
            db.Vehicles.Add(vehicleToEdit);
            await db.SaveChangesAsync();
            vehicleIdToEdit = vehicleToEdit.Id;
        }

        var token = await GetToken(admin, $"/Admin/EditVehicle/{vehicleIdToEdit}");

        // Attempt 1: Edit to UnitNumber "4604" which belongs to unit 4604
        var editResp1 = await admin.PostAsync($"/Admin/EditVehicle/{vehicleIdToEdit}", Form(new()
        {
            ["Id"] = vehicleIdToEdit.ToString(),
            ["UnitNumber"] = "4604", // already used by another vehicle
            ["Vin"] = "1FUJGLDR8NLHA7701",
            ["Make"] = "Peterbilt",
            ["Model"] = "579",
            ["Year"] = "2022",
            ["LicensePlate"] = "IL-7701TR",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddMonths(12).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, editResp1.StatusCode);
        var html1 = await editResp1.Content.ReadAsStringAsync();
        Assert.Contains("Unit Number is already in use by another vehicle", html1);

        // Attempt 2: Edit to VIN "4V4NC9EH5PN171701" which belongs to unit 1717
        token = await GetToken(admin, $"/Admin/EditVehicle/{vehicleIdToEdit}");
        var editResp2 = await admin.PostAsync($"/Admin/EditVehicle/{vehicleIdToEdit}", Form(new()
        {
            ["Id"] = vehicleIdToEdit.ToString(),
            ["UnitNumber"] = "7701",
            ["Vin"] = "4V4NC9EH5PN171701", // already used by unit 1717
            ["Make"] = "Peterbilt",
            ["Model"] = "579",
            ["Year"] = "2022",
            ["LicensePlate"] = "IL-7701TR",
            ["LicenseState"] = "IL",
            ["RegistrationExpirationDate"] = DateTime.UtcNow.AddMonths(12).ToString("yyyy-MM-dd"),
            ["Status"] = "Active",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, editResp2.StatusCode);
        var html2 = await editResp2.Content.ReadAsStringAsync();
        Assert.Contains("VIN is already in use by another vehicle", html2);
    }

    #endregion

    #region 4. Relational Integrity & Driver Association Tests

    [Fact]
    public async Task Vehicle_SetNullOnDelete_PreservesTripsAndDriverIntegrity()
    {
        int tripId;
        int vehicleId;
        string driverId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var customer = await users.FindByEmailAsync(CustomerEmail);
            var driver = await users.FindByEmailAsync(DriverEmail);
            driverId = driver!.Id;

            var testVehicle = new Vehicle
            {
                UnitNumber = "DEL-01",
                Vin = "1FUJGLDR8NLHADEL0",
                Make = "Freightliner",
                Model = "Cascadia",
                Year = 2022,
                LicensePlate = "IL-DEL01",
                LicenseState = "IL",
                RegistrationExpirationDate = DateTime.UtcNow.AddMonths(12),
                Status = VehicleStatus.Active,
                AssignedDriverId = driverId
            };
            db.Vehicles.Add(testVehicle);
            await db.SaveChangesAsync();
            vehicleId = testVehicle.Id;

            var trip = new Trip
            {
                CustomerId = customer!.Id,
                FromCity = "Chicago",
                ToCity = "Joliet",
                FromAddress = "100 River Rd",
                ToAddress = "200 Plainfield Rd",
                FromZipCode = "60601",
                ToZipCode = "60435",
                Weight = 10000,
                Volume = 25,
                Distance = 75,
                GoodsType = "Local Goods",
                PickupTime = DateTime.UtcNow.AddDays(1),
                EstimatedPrice = 500,
                AdminPrice = 550,
                Status = TripStatus.Assigned,
                VehicleId = vehicleId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Trips.Add(trip);

            var driverTrip = new DriverTrip
            {
                Trip = trip,
                DriverId = driverId,
                AssignedByAdminId = driverId,
                DriverPayment = 400,
                Status = TripStatus.Assigned,
                VehicleId = vehicleId,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };
            db.DriverTrips.Add(driverTrip);
            await db.SaveChangesAsync();
            tripId = trip.Id;
        }

        // Delete the vehicle entity
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var v = await db.Vehicles.FindAsync(vehicleId);
            Assert.NotNull(v);
            db.Vehicles.Remove(v);
            await db.SaveChangesAsync();
        }

        // Verify that the trip and driverTrip records still exist with VehicleId set to null
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var trip = await db.Trips.Include(t => t.DriverTrip).FirstOrDefaultAsync(t => t.Id == tripId);
            Assert.NotNull(trip);
            Assert.Null(trip.VehicleId);
            Assert.NotNull(trip.DriverTrip);
            Assert.Null(trip.DriverTrip.VehicleId);

            // Driver must still exist
            var driver = await db.Drivers.FindAsync(driverId);
            Assert.NotNull(driver);
        }
    }

    #endregion
}
