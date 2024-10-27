using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Threading.Tasks;
using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
namespace logirack.Services;


[Authorize(Roles = "Driver")]
public class DriverController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PasswordService _passwordService;
    private readonly ILogger<DriverController> _logger;

    // Constructor to inject PasswordService
    public DriverController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, PasswordService passwordService, ILogger<DriverController> logger)
    {
        _db = db;
        _passwordService = passwordService;
        _logger = logger;
        _userManager = userManager;
    }

    public IActionResult Dashboard()
    {
        return View();
    }
    
    
    // Action to show the driver their assigned trips
    [HttpGet]
    public async Task<IActionResult> TripLog()
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var trips = await _db.DriverTrips
            .Include(dt => dt.Trip)
            .Where(dt => dt.DriverId == driverId && dt.Status == TripStatus.Assigned)
            .Select(dt => new TripDetailsViewModel
            {
                TripId = dt.Trip.Id,
                FromCity = dt.Trip.FromCity,
                ToCity = dt.Trip.ToCity,
                Distance = dt.Trip.Distance,
                Weight = dt.Trip.Weight,
                PickupDate = dt.Trip.PickupTime,
                Status = dt.Trip.Status
            })
            .ToListAsync();

        return View(trips);
    }

    // Action to allow the driver to mark a trip as completed
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndTrip(int tripId)
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var driverTrip = await _db.DriverTrips
            .Include(dt => dt.Trip)
            .Include(dt => dt.Driver)
            .FirstOrDefaultAsync(dt => dt.TripId == tripId && dt.DriverId == driverId);

        if (driverTrip == null || driverTrip.Status != TripStatus.Assigned)
        {
            return NotFound("Trip not found or already completed.");
        }

        driverTrip.Status = TripStatus.Completed;
        driverTrip.UpdateAt = DateTime.Now;
        driverTrip.Driver.IsAvailable = true;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Trip has been marked as completed.";
        return RedirectToAction(nameof(TripLog));
    }
    

    // Example action to demonstrate usage of PasswordService
    public IActionResult GenerateDriverPassword()
    {
        // Use PasswordService to generate a password
        string generatedPassword = _passwordService.GeneratePassword();
        _logger.LogInformation("Generated password for driver: {Password}", generatedPassword);

        ViewBag.GeneratedPassword = generatedPassword; // Pass the generated password to the view
        return View();
    }
}