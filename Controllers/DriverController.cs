using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace logirack.Controllers;


// <summary>
/// Controller for managing driver operations and trip handling
/// </summary>
[Authorize(Roles = "Driver")]
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DriverController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DriverController> _logger;
    private readonly IEmailSender _emailSender;
    public DriverController(ApplicationDbContext db, ILogger<DriverController> logger,IEmailSender emailSender)
    {
        _db = db;
        _logger = logger;
        _emailSender = emailSender;

    }
    /// <summary>
    /// Displays the driver dashboard
    /// </summary>
    /// <returns>The dashboard view</returns>
    [HttpGet("dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Dashboard()
    {
        return View();
    }


    /// <summary>
    /// Retrieves the complete trip history for the current driver
    /// </summary>
    /// <returns>List of all trips assigned to the driver</returns>
    /// <response code="200">Returns the list of trips</response>
    [HttpGet("trips/log")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TripLog()
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var trips = await _db.DriverTrips
            .Include(dt => dt.Trip)
            .Where(dt => dt.DriverId == driverId)
            .Select(dt => new TripDetailsViewModel
            {
                TripId = dt.Trip.Id,
                FromCity = dt.Trip.FromCity,
                ToCity = dt.Trip.ToCity,
                Distance = dt.Trip.Distance,
                Weight = dt.Trip.Weight,
                PickupDate = dt.Trip.PickupTime,
                Status = dt.Trip.Status,
                AdminPrice = dt.Trip.AdminPrice
            })
            .ToListAsync();

        return View(trips);
    }

    /// <summary>
    /// Retrieves current active trips for the driver
    /// </summary>
    /// <returns>List of currently assigned trips</returns>
    /// <response code="200">Returns the list of current trips</response>
    [HttpGet("trips/current")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CurrentTrips()
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var currentTrips = await _db.DriverTrips
            .Include(dt => dt.Trip)
            .Where(dt => dt.DriverId == driverId && dt.Status == TripStatus.Assigned) // Show only assigned trips
            .Select(dt => new TripDetailsViewModel
            {
                TripId = dt.Trip.Id,
                FromCity = dt.Trip.FromCity,
                ToCity = dt.Trip.ToCity,
                Distance = dt.Trip.Distance,
                Weight = dt.Trip.Weight,
                PickupDate = dt.Trip.PickupTime,
                Status = dt.Trip.Status,
                AdminPrice = dt.Trip.AdminPrice
            })
            .ToListAsync();

        return View(currentTrips);
    }


    /// <summary>
    /// Marks a trip as completed
    /// </summary>
    /// <param name="tripId">ID of the trip to complete</param>
    /// <returns>Redirects to current trips view</returns>
    /// <response code="200">If trip is successfully marked as completed</response>
    /// <response code="404">If trip is not found or not in correct state</response>
    [HttpPost("trips/{tripId}/complete")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndTrip(int tripId)
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var driverTrip = await _db.DriverTrips
            .Include(dt => dt.Trip)
            .Include(dt => dt.Trip.Admin)
            .Include(dt => dt.Trip.Customer)
            .Include(dt => dt.Driver)
            .FirstOrDefaultAsync(dt => dt.TripId == tripId && dt.DriverId == driverId);

        if (driverTrip == null || driverTrip.Status != TripStatus.Assigned)
        {
            return NotFound("Trip not found or already completed.");
        }

        driverTrip.Status = TripStatus.Completed;
        driverTrip.Trip.Status = TripStatus.Completed;
        driverTrip.UpdateAt = DateTime.Now;
        driverTrip.Driver.IsAvailable = true; 

        await _db.SaveChangesAsync();
        if (driverTrip.Trip.Admin != null)
        {
            try
            {
                await _emailSender.SendEmailAsync(
                    driverTrip.Trip.Admin.Email,
                    "Trip Completed Notification",
                    $"Trip ID: {tripId} has been completed by driver {driverTrip.Driver.FirstName} {driverTrip.Driver.LastName}.\n\n" +
                    $"Trip Details:\n" +
                    $"From: {driverTrip.Trip.FromCity}\n" +
                    $"To: {driverTrip.Trip.ToCity}\n" +
                    $"Customer: {driverTrip.Trip.Customer.FirstName} {driverTrip.Trip.Customer.LastName}\n" +
                    $"Completion Time: {DateTime.Now}\n" +
                    $"Final Price: {driverTrip.Trip.AdminPrice:C}"
                );
                _logger.LogInformation("Trip completion email sent to admin");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send trip completion email to admin ");
            }
        }

        if (driverTrip.Trip.Customer != null) 
        { 
            try 
            {
                await _emailSender.SendEmailAsync(
                driverTrip.Trip.Customer.Email,
                "Your Trip Has Been Completed",
                $"Dear {driverTrip.Trip.Customer.FirstName},\n\n" +
                $"Your trip has been completed successfully.\n\n" +
                $"Trip Details:\n" +
                $"Trip ID: {tripId}\n" +
                $"From: {driverTrip.Trip.FromCity}\n" +
                $"To: {driverTrip.Trip.ToCity}\n" +
                $"Driver: {driverTrip.Driver.FirstName} {driverTrip.Driver.LastName}\n" +
                $"Completion Time: {DateTime.Now}\n" +
                $"Final Price: {driverTrip.Trip.AdminPrice:C}\n\n" +
                $"Thank you for using our service!"
                );
                _logger.LogInformation("Trip completion email sent to customer");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send trip completion email to customer");
            } 
        } 
        TempData["Success"] = "Trip has been marked as completed.";
        return RedirectToAction(nameof(CurrentTrips));
    }
}