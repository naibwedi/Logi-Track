using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using logirack.Models;

namespace logirack.Data;

public static class SeedData
{
    private static readonly string[] Roles = ["SuperAdmin", "Admin", "Customer", "Driver"];

    public static async Task Initialize(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SeedData));

        foreach (var role in Roles)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Could not create role '{role}': {errors}");
            }

            logger.LogInformation("Created identity role {Role}", role);
        }

        var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Vehicles.AnyAsync())
        {
            var vehicles = new List<Vehicle>
            {
                new Vehicle
                {
                    UnitNumber = "4604",
                    Vin = "1FUJGLDR8NLHA4604",
                    LicensePlate = "IL-4604TR",
                    LicenseState = "IL",
                    Make = "Freightliner",
                    Model = "Cascadia",
                    Year = 2022,
                    RegistrationExpirationDate = DateTime.UtcNow.AddMonths(9),
                    AnnualDotInspectionExpiration = DateTime.UtcNow.AddMonths(8),
                    Form2290ExpirationDate = DateTime.UtcNow.AddMonths(10),
                    PrePassTransponderId = "PP-460401",
                    Status = VehicleStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new Vehicle
                {
                    UnitNumber = "1717",
                    Vin = "4V4NC9EH5PN171701",
                    LicensePlate = "IL-1717TR",
                    LicenseState = "IL",
                    Make = "Volvo",
                    Model = "VNL 860",
                    Year = 2023,
                    RegistrationExpirationDate = DateTime.UtcNow.AddMonths(11),
                    AnnualDotInspectionExpiration = DateTime.UtcNow.AddMonths(10),
                    Form2290ExpirationDate = DateTime.UtcNow.AddMonths(11),
                    PrePassTransponderId = "PP-171702",
                    Status = VehicleStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new Vehicle
                {
                    UnitNumber = "6328",
                    Vin = "1XKYDP9X5MJ632801",
                    LicensePlate = "IL-6328TR",
                    LicenseState = "IL",
                    Make = "Kenworth",
                    Model = "T680",
                    Year = 2021,
                    RegistrationExpirationDate = DateTime.UtcNow.AddMonths(2),
                    AnnualDotInspectionExpiration = DateTime.UtcNow.AddMonths(1),
                    Form2290ExpirationDate = DateTime.UtcNow.AddMonths(3),
                    PrePassTransponderId = "PP-632803",
                    Status = VehicleStatus.Maintenance,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            };

            await db.Vehicles.AddRangeAsync(vehicles);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded Rufta Logistics LLC initial fleet units (4604, 1717, 6328).");
        }

        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var userManager = serviceProvider.GetRequiredService<UserManager<logirack.Models.ApplicationUser>>();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new logirack.Models.SuperAdmin
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsApproved = true,
            RoleType = "SuperAdmin",
            FirstName = "System",
            LastName = "Administrator",
            CreatedOn = DateTime.UtcNow,
            ModifiedOn = DateTime.UtcNow
        };
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Could not create bootstrap SuperAdmin: {errors}");
        }

        await userManager.AddToRoleAsync(user, "SuperAdmin");
        logger.LogWarning("Bootstrap SuperAdmin {Email} was created. Remove BootstrapAdmin configuration now.", email);
    }
}
