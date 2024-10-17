namespace logirack.Data;
using logirack.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

public class SeedData
{
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
            var result = await userManager.CreateAsync(superAdnin, "P@ssw0rd123");
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


    }
    
}