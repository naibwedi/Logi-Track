using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
namespace logirack.Data;

using logirack.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

/// <summary>
/// Provides methods to seed the database with initial data.
/// </summary>
public class SeedData
{
    /// <summary>
    /// Initializes the database with default roles and users (SuperAdmin, Admin, Customer).
    /// </summary>
    /// <param name="serviceProvider">The service provider to get required services.</param>
    public static async Task Initialize(IServiceProvider serviceProvider)
    {
        using var db = new ApplicationDbContext(
            serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()
        );

        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var logger = serviceProvider.GetRequiredService<ILogger<SeedData>>();

        // Define roles
        string[] roleNames = { "Admin", "SuperAdmin", "Driver", "Customer" }; // Corrected "Customer" role spelling
        IdentityResult roleResult;

        foreach (var roleName in roleNames)
        {
            var roleExist = await roleManager.RoleExistsAsync(roleName);
            if (!roleExist)
            {
                roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!roleResult.Succeeded)
                {
                    logger.LogError($"Error creating role {roleName}");
                }
            }
        }

        // Seed SuperAdmin
        var superAdminEmail = "super@uia.no";
        var superAdminUser = await userManager.FindByEmailAsync(superAdminEmail);

        if (superAdminUser == null)
        {
            var superAdmin = new SuperAdmin
            {
                UserName = superAdminEmail,
                Email = superAdminEmail,
                EmailConfirmed = true,
                IsApproved = true,
                CreatedOn = DateTime.Now,
                ModifiedOn = DateTime.Now,
                PhoneNumber = "1234567890",
                RoleType = "SuperAdmin",
                FirstName = "Super",
                LastName = "Admin"
            };
            var result = await userManager.CreateAsync(superAdmin, "P@ssw0rd12");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(superAdmin, "SuperAdmin");
                logger.LogInformation("SuperAdmin user created successfully.");
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    logger.LogError($"Error creating SuperAdmin: {error.Description}");
                }
            }
        }
        else
        {
            logger.LogInformation("SuperAdmin already exists.");
        }

        // Seed Admin
        var adminEmail = "admin@uia.no";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
            var admin = new Admin
            {
                UserName = adminEmail,
                Email = adminEmail,
                IsApproved = true,
                EmailConfirmed = true,
                PhoneNumber = "0987654321",
                FirstName = "Admin",
                LastName = "User",
                RoleType = "Admin",
            };
            var result = await userManager.CreateAsync(admin, "Admin@12345");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
                logger.LogInformation("Admin user created successfully.");
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    logger.LogError($"Error creating Admin: {error.Description}");
                }
            }
        }
        else
        {
            logger.LogInformation("Admin already exists.");
        }

        // Seed Customer
        var customerEmail = "customer@uia.no";
        var customerUser = await userManager.FindByEmailAsync(customerEmail);

        if (customerUser == null)
        {
            var customer = new Customer
            {
                UserName = customerEmail,
                Email = customerEmail,
                EmailConfirmed = true,
                IsApproved = true,
                PhoneNumber = "1231231234",
                FirstName = "Customer",
                LastName = "User",
                RoleType = "Customer",
                CreatedOn = DateTime.Now,
                ModifiedOn = DateTime.Now
            };
            var result = await userManager.CreateAsync(customer, "Customer@12345");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(customer, "Customer");
                logger.LogInformation("Customer user created successfully.");
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    logger.LogError($"Error creating Customer: {error.Description}");
                }
            }
        }
        else
        {
            logger.LogInformation("Customer already exists.");
        }

        // Seed Driver
        var driverEmail = "driver@uia.no";
        var driverUser = await userManager.FindByEmailAsync(driverEmail);

        if (driverUser == null)
        {
            var driver = new Driver
            {
                UserName = driverEmail,
                Email = driverEmail,
                EmailConfirmed = true,
                IsApproved = true,
                PhoneNumber = "3213213210",
                FirstName = "Driver",
                LastName = "User",
                RoleType = "Driver",
                CreatedOn = DateTime.Now,
                ModifiedOn = DateTime.Now
            };
            var result = await userManager.CreateAsync(driver, "Driver@12345");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(driver, "Driver");
                logger.LogInformation("Driver user created successfully.");
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    logger.LogError($"Error creating Driver: {error.Description}");
                }
            }
        }
        else
        {
            logger.LogInformation("Driver already exists.");
        }
    }
}
