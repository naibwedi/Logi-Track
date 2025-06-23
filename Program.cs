using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using logirack.Data;
using logirack.Hubs;
using logirack.Models;
using SendGrid;
using logirack.Services;
using Microsoft.OpenApi.Models;


var builder = WebApplication.CreateBuilder(args);


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        var postgresConnection = builder.Configuration.GetConnectionString("PostgreSQLConnection");
        options.UseNpgsql(postgresConnection);
    }
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();
// ----------------swager---------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Logirack API",
        Version = "v1",
        Description = "API documentation for Logirack application",
        Contact = new OpenApiContact
        {
            Name = "Your Name",
            Email = "your.email@example.com"
        }
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
// Configure Identity
// builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
//     .AddRoles<IdentityRole>()
//     .AddEntityFrameworkStores<ApplicationDbContext>();
//for dev only --------------------------------------------------------------
builder.Services.AddDefaultIdentity<ApplicationUser>(options => 
    {
        if (builder.Environment.IsDevelopment())
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.SignIn.RequireConfirmedEmail = false;
        }
        else
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.SignIn.RequireConfirmedEmail = true;
        }
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
builder.Services.AddSingleton<ISendGridClient>(new SendGridClient(sendGridApiKey));

// Register EmailSender as the implementation of IEmailSender
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    // Add these two lines
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Logirack API V1");
    });
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
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        // Ensure roles exist
        string[] roleNames = { "SuperAdmin", "Admin", "Customer" };
        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        await SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
