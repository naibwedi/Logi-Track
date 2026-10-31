using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Identity.UI.Services;
using logirack.Services;

namespace logirack.Controllers;


// <summary>
/// Controller for managing driver operations and trip handling
/// </summary>
[Authorize(Roles = "Driver")]
public class DriverController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DriverController> _logger;
    private readonly IEmailSender _emailSender;
    private readonly ICoordinateService _coordinateService;
    private readonly IInvoiceService _invoiceService;
    public DriverController(ApplicationDbContext db, ILogger<DriverController> logger, IEmailSender emailSender, ICoordinateService coordinateService, IInvoiceService? invoiceService = null)
    {
        _db = db;
        _logger = logger;
        _emailSender = emailSender;
        _coordinateService = coordinateService;
        _invoiceService = invoiceService ?? new PdfInvoiceService();
    }
    /// <summary>
    /// Displays the driver dashboard
    /// </summary>
    /// <returns>The dashboard view</returns>
    [HttpGet]
    public IActionResult Dashboard()
    {
        return View();
    }


    /// <summary>
    /// Retrieves the complete trip history for the current driver
    /// </summary>
    /// <returns>List of all trips assigned to the driver</returns>
    /// <response code="200">Returns the list of trips</response>
    [HttpGet]
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
    [HttpGet]
    public async Task<IActionResult> CurrentTrips()
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var driverTrips = await _db.DriverTrips
            .Include(dt => dt.Trip)
            .Where(dt => dt.DriverId == driverId && (dt.Status == TripStatus.Assigned || dt.Status == TripStatus.InProgress))
            .ToListAsync();

        var driverLocation = await _db.Locations.FirstOrDefaultAsync(l => l.DriverID == driverId);

        var currentTrips = driverTrips.Select(dt =>
        {
            var startCoords = _coordinateService.GetCoordinatesForCity(dt.Trip.FromCity);
            var destCoords = _coordinateService.GetCoordinatesForCity(dt.Trip.ToCity);
            var currentLat = driverLocation != null ? driverLocation.Latitude : startCoords.Latitude;
            var currentLng = driverLocation != null ? driverLocation.Longitude : startCoords.Longitude;

            return new TripDetailsViewModel
            {
                TripId = dt.Trip.Id,
                FromCity = dt.Trip.FromCity,
                ToCity = dt.Trip.ToCity,
                FromAddress = dt.Trip.FromAddress,
                ToAddress = dt.Trip.ToAddress,
                FromZipCode = dt.Trip.FromZipCode,
                ToZipCode = dt.Trip.ToZipCode,
                Distance = dt.Trip.Distance,
                Weight = dt.Trip.Weight,
                Volume = dt.Trip.Volume,
                GoodTypes = dt.Trip.GoodsType,
                Notes = dt.Trip.Notes,
                PickupDate = dt.Trip.PickupTime,
                Status = dt.Trip.Status,
                AdminPrice = dt.Trip.AdminPrice,
                EstimatedPrice = dt.Trip.EstimatedPrice,
                StartLatitude = startCoords.Latitude,
                StartLongitude = startCoords.Longitude,
                DestinationLatitude = destCoords.Latitude,
                DestinationLongitude = destCoords.Longitude,
                CurrentLatitude = currentLat,
                CurrentLongitude = currentLng
            };
        }).ToList();

        return View(currentTrips);
    }


    /// <summary>
    /// Marks a trip as completed
    /// </summary>
    /// <param name="tripId">ID of the trip to complete</param>
    /// <returns>Redirects to current trips view</returns>
    /// <response code="200">If trip is successfully marked as completed</response>
    /// <response code="404">If trip is not found or not in correct state</response>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndTrip(int tripId)
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var driverTrip = await _db.DriverTrips
            .Include(dt => dt.Trip)
            .Include(dt => dt.Trip.Admin)
            .Include(dt => dt.Trip.Customer)
            .Include(dt => dt.Driver)
            .FirstOrDefaultAsync(dt => dt.TripId == tripId && dt.DriverId == driverId);

        if (driverTrip == null || (driverTrip.Status != TripStatus.Assigned && driverTrip.Status != TripStatus.InProgress))
        {
            return NotFound("Trip not found or already completed.");
        }

        driverTrip.Status = TripStatus.Completed;
        driverTrip.Trip.Status = TripStatus.Completed;
        driverTrip.UpdateAt = DateTime.UtcNow;
        driverTrip.Trip.UpdatedAt = DateTime.UtcNow;
        driverTrip.CompletionDate = DateTime.UtcNow;
        driverTrip.Driver.IsAvailable = true; 

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict while completing trip {TripId}", tripId);
            return Conflict("The trip was modified by another process. Please refresh and try again.");
        }
        if (driverTrip.Trip.Admin != null)
        {
            try
            {
                await _emailSender.SendEmailAsync(
                    driverTrip.Trip.Admin.Email!,
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
                driverTrip.Trip.Customer.Email!,
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

    [HttpGet]
    public async Task<IActionResult> PaymentTracking()
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var payments = await _db.Payments.AsNoTracking()
            .Where(payment => payment.DriverId == driverId)
            .OrderByDescending(payment => payment.PaymentDate)
            .ToListAsync();
        return View(payments);
    }

    [HttpGet]
    public IActionResult DriverTripManagement() => RedirectToAction(nameof(CurrentTrips));

    /// <summary>
    /// Downloads the Driver Settlement Statement PDF voucher for a trip assigned to the current driver.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DownloadSettlement(int id)
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(driverId))
        {
            return Challenge();
        }

        var driverTrip = await _db.DriverTrips
            .Include(dt => dt.Trip)
                .ThenInclude(t => t.Broker)
            .Include(dt => dt.Trip)
                .ThenInclude(t => t.Vehicle)
            .Include(dt => dt.Driver)
            .Include(dt => dt.Vehicle)
            .FirstOrDefaultAsync(dt => dt.TripId == id);

        if (driverTrip == null || driverTrip.DriverId != driverId)
        {
            return NotFound();
        }

        var trip = driverTrip.Trip;
        var breakdown = SettlementCalculator.ForTrip(trip);
        var vehicle = driverTrip.Vehicle ?? trip.Vehicle;
        var pdfBytes = _invoiceService.GenerateSettlementStatementPdf(trip, driverTrip.Driver, breakdown, vehicle, trip.Broker);
        return File(pdfBytes, "application/pdf", $"Settlement_Trip_{trip.Id}.pdf");
    }
}
