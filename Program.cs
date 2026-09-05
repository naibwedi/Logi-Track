using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using logirack.Data;
using logirack.Hubs;
using logirack.Models;
using SendGrid;
using logirack.Services;
using Microsoft.AspNetCore.DataProtection;


var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        var sqliteConnection = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        options.UseSqlite(sqliteConnection);
    }
    else
    {
        var postgresConnection = builder.Configuration.GetConnectionString("PostgreSQLConnection")
            ?? throw new InvalidOperationException("Connection string 'PostgreSQLConnection' not found.");
        options.UseNpgsql(postgresConnection);
    }
});


builder.Services.AddDatabaseDeveloperPageExceptionFilter();
var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".keys");
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
// Configure Identity
// builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
//     .AddRoles<IdentityRole>()
//     .AddEntityFrameworkStores<ApplicationDbContext>();
//for dev only --------------------------------------------------------------
builder.Services.AddDefaultIdentity<ApplicationUser>(options => 
    {
        if (builder.Environment.IsDevelopment())
        {
            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedEmail = false;
        }
        else
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.SignIn.RequireConfirmedEmail = true;
        }
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
// Add controllers and Razor pages
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
// Register custom services
builder.Services.AddScoped<PasswordService>();


// Manually configure SendGrid client
var sendGridApiKey = builder.Configuration["SendGrid:ApiKey"];
builder.Services.AddSingleton<ISendGridClient>(new SendGridClient(
    string.IsNullOrWhiteSpace(sendGridApiKey) ? "not-configured" : sendGridApiKey));

// Register EmailSender as the implementation of IEmailSender
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Map routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
//  SignalR

app.MapHub<TripHub>("/tripHub");
// Set up roles and SuperAdmin user before the application starts
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, "Database migration or identity role setup failed.");
        throw;
    }
}

app.Run();

public partial class Program;
