using Microsoft.AspNetCore.Identity;

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
