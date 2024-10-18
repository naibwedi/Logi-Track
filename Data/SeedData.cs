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
    /// Initializes the database with default roles and a SuperAdmin user.
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

        string[] roleNames = { "Admin","SuperAdmin","Driver","Costumer" };
        IdentityResult roleResult;

        foreach (var roleName in roleNames)
        {
            var roleExist = await roleManager.RoleExistsAsync(roleName);
            if (!roleExist)
            {
                roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!roleResult.Succeeded)
                {
                    
                }
            }
        }
        
        //Seed SuperAdmin
        var superAdminEmail = "super@uia.no";
        var superAdminUser = await userManager.FindByEmailAsync(superAdminEmail);

        if (superAdminUser == null)
        {
            var superAdnin = new SuperAdmin
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
            var result = await userManager.CreateAsync(superAdnin, "P@ssw0rd12");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(superAdnin, "SuperAdmin");
                logger.LogInformation("SuperAdmin user created successfully.");
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    logger.LogError($"Error CreateSuperAdmin: {error.Description}");
                }
            }
            
        }
        else
        {
            logger.LogInformation("SuperAdmin already exists.");
        }
        
        // admin 
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
                    logger.LogError($"Error CreateAdmin: {error.Description}");
                }
            }
        }
        else
        {
            logger.LogInformation("Admin already exists.");
        }
        

    }
    
}