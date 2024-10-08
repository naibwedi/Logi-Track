using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using logirack.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Setter opp roller og brukere før applikasjonen starter
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    await SeedRolesAndDefaultUsers(userManager, roleManager);
}

app.Run();

// Oppretter rollene Admin og Driver
async Task SeedRolesAndDefaultUsers(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
{
    // Definere alle rollene vi trenger
    var roles = new[] { "Admin", "Driver" };
    
    // Sjekker om rollen eksisterer
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // Admin bruker
    var adminEmail = "admin@gmail.com";
    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        var admin = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(admin, "Password1.");

        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }

    // Driver bruker
    var driverEmail = "driver@gmail.com";
    var driverUser = await userManager.FindByEmailAsync(driverEmail);
    if (driverUser == null)
    {
        var driver = new IdentityUser
        {
            UserName = driverEmail,
            Email = driverEmail,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(driver, "Password1.");

        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(driver, "Driver");
        }
    }
}