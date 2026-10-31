using System.Security.Claims;
using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using logirack.Hubs;
using logirack.Services;
using Microsoft.AspNetCore.SignalR;

namespace logirack.Controllers;


/// <summary>
/// Controller for Admin actions, including creating and managing Driver users.
/// </summary>
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<AdminController> _logger;
    private readonly IEmailSender _emailSender;
    private readonly PasswordService _passwordService;
    private readonly IHubContext<TripHub> _hubContext;
    private readonly ICoordinateService _coordinateService;
    private readonly IInvoiceService _invoiceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    public AdminController(ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<AdminController> logger,
        IEmailSender emailSender,
        IHubContext<TripHub> hubContext,
        PasswordService passwordService,
        ICoordinateService coordinateService,
        IInvoiceService invoiceService)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
        _hubContext = hubContext;
        _emailSender = emailSender;
        _passwordService = passwordService;
        _coordinateService = coordinateService;
        _invoiceService = invoiceService;
    }
    /// <summary>
    /// Search for drivers based on specified criteria
    /// </summary>
    /// <param name="searchString">The search term to filter drivers</param>
    /// <param name="searchCriteria">The field to search by (FirstName, LastName, Email, PhoneNumber)</param>
    /// <returns>A list of matching drivers</returns>
    /// <response code="200">Returns the list of matching drivers</response>
    /// <response code="400">If the search criteria is invalid</response>
    [HttpGet]
public IActionResult SearchDrivers(string searchString, string searchCriteria)
{
    var drivers = from d in _db.Drivers // Changed _context to _db
        select d;

    if (!String.IsNullOrEmpty(searchString))
    {
        switch (searchCriteria)
        {
            case "FirstName":
                drivers = drivers.Where(d => d.FirstName.Contains(searchString));
                break;
            case "LastName":
                drivers = drivers.Where(d => d.LastName.Contains(searchString));
                break;
            case "Email":
                drivers = drivers.Where(d => d.Email != null && d.Email.Contains(searchString));
                break;
            case "PhoneNumber":
                drivers = drivers.Where(d => d.PhoneNumber != null && d.PhoneNumber.Contains(searchString));
                break;
        }
    }

    return PartialView("_DriverManagmentPartial", drivers.ToList());
}

    
    /// <summary>
    /// Creates a new driver account
    /// </summary>
    /// <returns>View for creating a new driver</returns>
    [HttpGet]
    public IActionResult CreateDriver()
    {
        return View();
    }

    /// <summary>
    /// Processes the creation of a new Driver
    /// </summary>
    /// <param name="model">Driver creation data</param>
    /// <returns>Redirect to driver management on success, or view with errors</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDriver(CreateDriverViewModel model)
    {
        if (ModelState.IsValid)
        {
            // Check for email
            var euser = await _userManager.FindByEmailAsync(model.DriverEmail);
            if (euser != null)
            {
                ModelState.AddModelError(nameof(CreateDriverViewModel.DriverEmail), "Email Already Exists");
                return View(model);
            }
            // Check for phone number
            var ePhone = await _userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber);
            if (ePhone != null)
            {
                ModelState.AddModelError(nameof(CreateDriverViewModel.PhoneNumber), "Phone Number Already Exists");
                return View(model);
            }

            var newDriver = new Driver
            {
                UserName = model.DriverEmail,
                Email = model.DriverEmail,
                EmailConfirmed = true,
                IsApproved = true,
                CreatedOn = DateTime.Now,
                ModifiedOn = DateTime.Now,
                PhoneNumber = model.PhoneNumber,
                RoleType = "Driver",
                FirstName = model.FirstName,
                LastName = model.LastName,
                PricePerKm = model.PricePerKm,
                PaymentFreq = model.PaymentFreq,
                IsAvailable = true,
                DateOfBirth = model.DateOfBirth,
                CdlNumber = model.CdlNumber,
                CdlState = model.CdlState?.ToUpperInvariant(),
                CdlExpirationDate = model.CdlExpirationDate,
                MedicalCardExpirationDate = model.MedicalCardExpirationDate,
            };
            var result = await _userManager.CreateAsync(newDriver, model.Password);
            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new driver");
                await _userManager.AddToRoleAsync(newDriver, "Driver");

                // Send password to driver's email
                try
                {
                    await _emailSender.SendEmailAsync(model.DriverEmail, "Driver Account Created",
                        "Your driver account has been created. Use the initial password provided securely by your administrator and change it after signing in.");
                    _logger.LogInformation("Email successfully sent to {Email}", model.DriverEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email to {Email}", model.DriverEmail);
                }

                TempData["Success"] = "Driver Created";
                return RedirectToAction(nameof(DriverManagment));
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                    _logger.LogError("Error creating a driver: {Error}", error.Description);
                }
            }
        }
        return View(model);
    }
    

    /// <summary>
    /// Displays a list of all Driver users
    /// </summary>
    /// <returns>View with list of all drivers</returns>
    [HttpGet]
    public async Task<IActionResult> DriverManagment()
    {
        var allDrivers = await _userManager.GetUsersInRoleAsync("Driver");
        var drivers = allDrivers.OfType<Driver>().ToList();
        return View(drivers);
    } 
    /// <summary>
    /// Displays the admin dashboard
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        try
        {
           
            var activeTrips = await _db.Trips.CountAsync(t => 
                t.Status == TripStatus.Requested ||
                t.Status == TripStatus.ApprovedByAdmin ||
                t.Status == TripStatus.PriceSet ||
                t.Status == TripStatus.ApprovedByCustomer);

            
            var assignedTrips = await _db.Trips.CountAsync(t => 
                t.Status == TripStatus.Assigned || 
                t.Status == TripStatus.InProgress);

            var completedTrips = await _db.Trips.CountAsync(t => 
                t.Status == TripStatus.Completed);

            var availableDriverCount = await _db.Users
                .OfType<Driver>()
                .CountAsync(d => d.IsAvailable);

            var availableDriversList = await _db.Users
                .OfType<Driver>()
                .Where(d => d.IsAvailable)
                .ToListAsync();

            var customers = await _db.Users
                .OfType<Customer>()
                .ToListAsync();

            var totalRevenue = await _db.Trips
                .Where(t => t.Status != TripStatus.CancelledByAdmin && 
                           t.Status != TripStatus.CanceledByCustomer)
                .SumAsync(t => t.AdminPrice);

            var recentActivities = await _db.RecentActivities
                .OrderByDescending(a => a.Timestamp)
                .Take(10)
                .ToListAsync();

            var pendingApprovals = await _db.Users.CountAsync(u => !u.IsApproved);
            var allDriversForCompliance = await _db.Users.OfType<Driver>().ToListAsync();
            var allVehiclesForCompliance = await _db.Vehicles.ToListAsync();
            var complianceAlerts = ComplianceService.GetAlerts(allDriversForCompliance, allVehiclesForCompliance, DateTime.UtcNow).ToList();
            var blacklistedBrokersCount = await _db.Brokers.CountAsync(b => b.IsBlacklisted);

            var dashboardStats = new DashboardStats
            {
                ActiveTrips = activeTrips,
                AssignedTrips = assignedTrips,
                CompletedTrips = completedTrips,
                AvailableDrivers = availableDriverCount,
                PendingApprovals = pendingApprovals,
                TotalRevenue = totalRevenue,
                RecentActivities = recentActivities,
                AvailableDriversList = availableDriversList,
                Customers = customers,
                ComplianceAlerts = complianceAlerts,
                BlacklistedBrokersCount = blacklistedBrokersCount
            };

            return View(dashboardStats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the admin dashboard.");
            TempData["Error"] = "Dashboard data could not be loaded. Please try again.";
            return View(new DashboardStats
            {
                RecentActivities = new List<RecentActivity>(),
                AvailableDriversList = new List<Driver>(),
                Customers = new List<Customer>()
            });
        }
    }
    /// <summary>
    /// Gets driver details for editing
    /// </summary>
    /// <param name="id">Driver ID to edit</param>
    [HttpGet]
    public async Task<IActionResult> EditDriver(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest();
        }

        var driver = await _userManager.FindByIdAsync(id) as Driver;
        if (driver == null)
        {
            return NotFound();
        }

        var model = new EditDriverViewModel
        {
            id = driver.Id,
            FirstName = driver.FirstName,
            LastName = driver.LastName,
            Email = driver.Email ?? string.Empty,
            PhoneNumber = driver.PhoneNumber ?? string.Empty,
            PricePerKm = driver.PricePerKm,
            PaymentFreq = driver.PaymentFreq,
            IsAvailable = driver.IsAvailable,
            CdlNumber = driver.CdlNumber,
            CdlState = driver.CdlState,
            CdlExpirationDate = driver.CdlExpirationDate,
            MedicalCardExpirationDate = driver.MedicalCardExpirationDate,
        };
        return View(model);
    }

    /// <summary>
    /// Updates driver information
    /// </summary>
    /// <param name="model">Updated driver information</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDriver(EditDriverViewModel model)
    {
        if (ModelState.IsValid)
        {
            var driver = await _userManager.FindByIdAsync(model.id) as Driver;
            if (driver == null)
            {
                return NotFound();
            }
            // Checking email uniqueness
            if (!string.Equals(driver.Email, model.Email, StringComparison.OrdinalIgnoreCase))
            {
                var euser = await _userManager.FindByEmailAsync(model.Email);
                if (euser != null && euser.Id != model.id)
                {
                    ModelState.AddModelError(nameof(EditDriverViewModel.Email), "Email Already Exists");
                    return View(model);
                }
                driver.Email = model.Email;
                driver.UserName = model.Email;
                driver.EmailConfirmed = true;
            }
            // Checking phone number
            if (!string.Equals(driver.PhoneNumber, model.PhoneNumber, StringComparison.OrdinalIgnoreCase))
            {
                var ephone = await _userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber && x.Id != model.id);
                if (ephone != null)
                {
                    ModelState.AddModelError(nameof(EditDriverViewModel.PhoneNumber), "PhoneNumber Already Exists");
                    return View(model);
                }
                driver.PhoneNumber = model.PhoneNumber;
            }
            driver.FirstName = model.FirstName;
            driver.LastName = model.LastName;
            driver.PricePerKm = model.PricePerKm;
            driver.PaymentFreq = model.PaymentFreq;
            driver.IsAvailable = model.IsAvailable;
            driver.CdlNumber = model.CdlNumber;
            driver.CdlState = model.CdlState?.ToUpperInvariant();
            driver.CdlExpirationDate = model.CdlExpirationDate;
            driver.MedicalCardExpirationDate = model.MedicalCardExpirationDate;
            driver.ModifiedOn = DateTime.Now;
            var result = await _userManager.UpdateAsync(driver);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(DriverManagment));
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                    _logger.LogError("Error updating driver: {Error}", error.Description);
                }
            }
        }
        return View(model);
    }

    /// <summary>
    /// Deletes a driver account
    /// </summary>
    /// <param name="id">ID of driver to delete</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDriver(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest();
        }
        var driver = await _userManager.FindByIdAsync(id);
        if (driver == null)
        {
            return NotFound();
        }
        var result = await _userManager.DeleteAsync(driver);
        if (result.Succeeded)
        {
            _logger.LogInformation("Driver {Email} deleted", driver.Email);
        }
        else
        {
            _logger.LogError("Error deleting driver {Email}", driver.Email);
        }
        return RedirectToAction(nameof(DriverManagment));
    }
    
    /// <summary>
    /// Gets list of users pending approval
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PendingApprovals()
    {
        var pendingUsers=await _userManager.Users.Where(x => x.IsApproved == false ).ToListAsync();
        return View(pendingUsers);
    }
    
    
    /// <summary>
    /// Approves or rejects a user registration
    /// </summary>
    /// <param name="userId">User ID to approve/reject</param>
    /// <param name="approve">True to approve, false to reject</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveUser(string userId, bool approve)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return BadRequest("User ID cannot be null or empty.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        // Oppdater godkjenningsstatusen basert på admin-valget
        if (approve)
        {
            user.IsApproved = true;
            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                try
                {
                    await _emailSender.SendEmailAsync(
                        user.Email!,
                        "Your Account Has Been Approved",
                        $"Dear {user.FirstName} {user.LastName},\n\n" +
                        $"Your account has been approved. You can now log in and access all features.\n\n" +
                        $"Best regards,\n" +
                        $"Logirack Team"
                    );
                    _logger.LogInformation("Approval email sent to user");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send approval email");
                }

                TempData["Success"] = "User has been approved.";
            }
        }
        else
        {
            // Hvis brukeren ikke er godkjent, kan du velge å slette brukeren eller sette en annen status
            await _userManager.DeleteAsync(user);
            TempData["Success"] = "User has been rejected and deleted.";
            _logger.LogInformation("User {Email} rejected by admin and deleted.", user.Email);
        }
        return RedirectToAction(nameof(PendingApprovals));
    }

    
    /// <summary>
    /// Displays a list of trip requests filtered by status.
    /// </summary>
    /// <param name="sFilter">The status to filter trips by. Defaults to "Requested".</param>
    /// <returns>The TripRequests view with a list of filtered trips.</returns>
    [HttpGet]
    public async Task<IActionResult> TripRequests(string sFilter = "Requested")
    {
        if (Enum.TryParse<TripStatus>(sFilter, out var status))
        {
            var trips = await _db.Trips.Include(t => t.Customer)
                .Where(t => t.Status == status)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TripRequestViewModel
                {
                    TripId = t.Id,
                    CustomerName = $"{t.Customer.FirstName} {t.Customer.LastName}",
                    FromCity = t.FromCity,
                    ToCity = t.ToCity,
                    Weight = t.Weight,
                    Distance = t.Distance,
                    EstimatedPrice = t.EstimatedPrice,
                    Status = t.Status,
                    CreatedAt = t.CreatedAt,
                    PickUpAt = t.PickupTime
                }).ToListAsync();
            ViewBag.CurrentFilter = sFilter;
            return View(trips);
        }
        return BadRequest("Invalid filter check db relations");
    }

    /// <summary>
    /// Displays detailed information about a specific trip.
    /// </summary>
    /// <param name="id">The ID of the trip to display details for.</param>
    /// <returns>The TripDetails view with the trip's detailed information.</returns>
    [HttpGet]
    public async Task<IActionResult> TripDetails(int id)
    {
        var trip = await _db.Trips
            .Include(t => t.Customer)
            .Include(t => t.DriverTrip)
            .Include(t => t.Broker)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (trip == null)
            return NotFound();

        var startCoords = _coordinateService.GetCoordinatesForCity(trip.FromCity);
        var destCoords = _coordinateService.GetCoordinatesForCity(trip.ToCity);
        double currentLat = startCoords.Latitude;
        double currentLng = startCoords.Longitude;

        if (trip.DriverTrip != null)
        {
            var driverLocation = await _db.Locations.FirstOrDefaultAsync(l => l.DriverID == trip.DriverTrip.DriverId);
            if (driverLocation != null)
            {
                currentLat = driverLocation.Latitude;
                currentLng = driverLocation.Longitude;
            }
        }

        var viewModel = new TripDetailsViewModel
        {
            TripId = trip.Id,
            CustomerName = $"{trip.Customer.FirstName} {trip.Customer.LastName}",
            CustomerEmail = trip.Customer.Email ?? string.Empty,
            CustomerPhone = trip.Customer.PhoneNumber ?? string.Empty,
            FromCity = trip.FromCity,
            ToCity = trip.ToCity,
            FromAddress = trip.FromAddress,
            ToAddress = trip.ToAddress,
            FromZipCode = trip.FromZipCode,
            ToZipCode = trip.ToZipCode,
            Weight = trip.Weight,
            Volume = trip.Volume,
            Distance = trip.Distance,
            GoodTypes = trip.GoodsType,
            Notes = trip.Notes,
            EstimatedPrice = trip.EstimatedPrice,
            AdminPrice = trip.AdminPrice,
            Status = trip.Status,
            CreatedOn = trip.CreatedAt,
            PickupDate = trip.PickupTime,
            StartLatitude = startCoords.Latitude,
            StartLongitude = startCoords.Longitude,
            DestinationLatitude = destCoords.Latitude,
            DestinationLongitude = destCoords.Longitude,
            CurrentLatitude = currentLat,
            CurrentLongitude = currentLng,
            BrokerId = trip.BrokerId,
            BrokerName = trip.Broker?.CompanyName,
            BrokerMcNumber = trip.Broker?.DisplayMc,
            IsBrokerBlacklisted = trip.Broker?.IsBlacklisted ?? false,
            BrokerBlacklistReason = trip.Broker?.BlacklistReason,
            InvoiceNumber = trip.InvoiceNumber,
            BrokerLoadNumber = trip.BrokerLoadNumber,
            RateConfirmationAmount = trip.RateConfirmationAmount,
            DetentionFee = trip.DetentionFee,
            LumperFee = trip.LumperFee,
            OtherAccessorials = trip.OtherAccessorials,
            Deductions = trip.Deductions,
            DeductionReason = trip.DeductionReason,
            TonuAmount = trip.TonuAmount,
            Settlement = SettlementCalculator.ForTrip(trip)
        };
        return View(viewModel);
    }

    /// <summary>
    /// Processes the approval or rejection of a trip request.
    /// </summary>
    /// <param name="tId">Trip ID</param>
    /// <param name="isApproved">Approval status</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewTrip(int tId, bool isApproved)
    {
        var trip = await _db.Trips.FindAsync(tId);
        if (trip == null)
            return NotFound();
        if (trip.Status != TripStatus.Requested)
            return BadRequest("Only requested trips can be reviewed.");
        
        var admin = await _userManager.GetUserAsync(User) as Admin;
        if (admin == null)
            return Forbid();
        trip.Status = isApproved ? TripStatus.ApprovedByAdmin : TripStatus.CancelledByAdmin;
        trip.AdminId=admin.Id;
        trip.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict while reviewing trip {TripId}", trip.Id);
            return Conflict("The trip was modified by another process. Please refresh and try again.");
        }
        var tripReviewInfo = new
        {
            TripId = trip.Id,
            TripStatus = trip.Status.ToString(),
            ReviewedBy = $"{admin.FirstName} {admin.LastName}",
            ReviewDate = trip.UpdatedAt
        };
        await _hubContext.Clients.Group("Admins").SendAsync("TripReviewed", tripReviewInfo);
        TempData["Success"] =isApproved ? "Trip Approved" : "Trip Rejected";
        return RedirectToAction(nameof(TripRequests));

    }

    /// <summary>
    /// Gets trip price setting page
    /// </summary>
    /// <param name="id">Trip ID</param>
    [HttpGet]
    public async Task<IActionResult> SetPrice(int id)
    {
        
        var trip = await _db.Trips
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (trip==null)
        {
            return NotFound();
        }

        var viewModel = new SetPriceViewModel
        {
            TripId = trip.Id,
            CustomerName = trip.Customer.FirstName + " " + trip.Customer.LastName,
            FromCity = trip.FromCity,
            ToCity = trip.ToCity,
            Distance = trip.Distance,
            Weight = trip.Weight,
            EstimatedPrice = trip.EstimatedPrice
        };
        return View(viewModel);
    }
    /// <summary>
    /// Sets the price for a trip
    /// </summary>
    /// <param name="model">Price setting data</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrice(SetPriceViewModel model)
    {
        if (!ModelState.IsValid)
        {
            foreach (var state in ModelState.Values)
            {
                foreach (var error in state.Errors)
                {
                    _logger.LogError(error.ErrorMessage);
                }
            }
            
            var trip = await _db.Trips.Include(t => t.Customer).FirstOrDefaultAsync(t => t.Id == model.TripId);

            if (trip != null)
            {
                model.CustomerName = $"{trip.Customer.FirstName} {trip.Customer.LastName}";
                model.FromCity = trip.FromCity;
                model.ToCity = trip.ToCity;
                model.Distance = trip.Distance;
                model.Weight = trip.Weight;
                model.EstimatedPrice = trip.EstimatedPrice;
            } 
            return View(model);
        }

        var tripToUpdate = await _db.Trips.Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == model.TripId);
        if (tripToUpdate == null)
        {
            return NotFound();
        }
        if (tripToUpdate.Status != TripStatus.ApprovedByAdmin)
        {
            return BadRequest("Only admin-approved trips can be priced.");
        }

        tripToUpdate.AdminPrice = model.AdminPrice;
        tripToUpdate.Status = TripStatus.PriceSet;
        tripToUpdate.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
            try
            {
                await _emailSender.SendEmailAsync(
                    tripToUpdate.Customer.Email!,
                    "Trip Price Set - Action Required",
                    $"Dear {tripToUpdate.Customer.FirstName} {tripToUpdate.Customer.LastName},\n\n" +
                    $"The price for your trip has been set and requires your approval.\n\n" +
                    $"Trip Details:\n" +
                    $"Trip ID: {tripToUpdate.Id}\n" +
                    $"From: {tripToUpdate.FromCity}\n" +
                    $"To: {tripToUpdate.ToCity}\n" +
                    $"Set Price: {tripToUpdate.AdminPrice:C}\n" +
                    $"Original Estimate: {tripToUpdate.EstimatedPrice:C}\n\n" +
                    $"Please log in to your account to approve or reject this price.\n\n" +
                    $"Best regards,\n" +
                    $"Logirack Team"
                );
                _logger.LogInformation("Price set notification email sent to customer {Email}", 
                    tripToUpdate.Customer.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send price set notification to cutomer");
            }
            var priceSetInfo = new
            {
                TripId = tripToUpdate.Id,
                AdminPrice = tripToUpdate.AdminPrice,
                Status = tripToUpdate.Status.ToString(),
                UpdatedAt = tripToUpdate.UpdatedAt
            };

            await _hubContext.Clients.Group("Admins").SendAsync("TripPriceSet", priceSetInfo);

            TempData["Success"] = "Price set successfully and customer has been notified.";
            return RedirectToAction(nameof(TripDetails), new { id = model.TripId });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict while setting price for trip {TripId}", model.TripId);
            ModelState.AddModelError(string.Empty, "The trip was modified by another process. Please refresh and try again.");
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting price for trip {TripId}", model.TripId);
            ModelState.AddModelError("", "An error occurred while saving changes.");
            return View(model);
        }
    }
    /// <summary>
    /// Gets driver assignment page for a trip
    /// </summary>
    /// <param name="id">Trip ID</param>
    [HttpGet]
    public async Task<IActionResult> AssignTrip(int id)
    {
        _logger.LogInformation($"AssignTrip GET called with id: {id}");
        var trip = await _db.Trips.Include(t=>t.Customer).FirstOrDefaultAsync(t=>t.Id==id);
        if (trip==null)
        {
            _logger.LogWarning($"Trip not found with id: {id}");
            return NotFound();
        }
        var availableDrivers = await _userManager.GetUsersInRoleAsync("Driver");
        var drivers = availableDrivers.OfType<Driver>().Where(d=>d.IsAvailable).ToList();
        _logger.LogInformation($"Found {drivers.Count} available drivers");

        var viewM = new AssignTripViewModel
        {
            TripId = trip.Id,
            FromCity = trip.FromCity,
            ToCity = trip.ToCity,
            Weight = trip.Weight,
            Distance = trip.Distance,
            AvailableDrivers = drivers,
            AvailableBrokers = await _db.Brokers.OrderBy(b => b.CompanyName).ToListAsync(),
            BrokerId = trip.BrokerId,
            InvoiceNumber = trip.InvoiceNumber,
            BrokerLoadNumber = trip.BrokerLoadNumber,
            RateConfirmationAmount = trip.RateConfirmationAmount ?? (trip.AdminPrice > 0 ? (decimal)trip.AdminPrice : (decimal)trip.EstimatedPrice),
            DetentionFee = trip.DetentionFee,
            LumperFee = trip.LumperFee,
            OtherAccessorials = trip.OtherAccessorials,
            Deductions = trip.Deductions,
            DeductionReason = trip.DeductionReason,
            AvailableVehicles = await _db.Vehicles
                .Where(v => v.Status == VehicleStatus.Active)
                .OrderBy(v => v.UnitNumber)
                .ToListAsync(),
        };
        return View(viewM);
    }

    /// <summary>
    /// Assigns a driver to a trip
    /// </summary>
    /// <param name="model">Trip assignment data</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignTrip(AssignTripViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await LoadAvailableDrivers(model);
            return View(model);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var trip = await _db.Trips
                .Include(t => t.DriverTrip)
            .Include(t => t.Broker)
                .Include(t => t.Customer)
                .FirstOrDefaultAsync(t => t.Id == model.TripId);

            if (trip == null)
            {
                await transaction.RollbackAsync();
                _logger.LogError($"Trip not found. TripId: {model.TripId}");
                return NotFound("Trip not found");
            }

            if (trip.Status != TripStatus.ApprovedByCustomer)
            {
                await transaction.RollbackAsync();
                return BadRequest("Only customer-approved trips can be assigned.");
            }

            if (trip.DriverTrip != null)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", "This trip is already assigned to a driver");
                await LoadAvailableDrivers(model);
                return View(model);
            }

            var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == model.DriverId);
            if (driver == null)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("DriverId", "Selected driver not found");
                await LoadAvailableDrivers(model);
                return View(model);
            }

            if (!driver.IsAvailable)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("DriverId", "Selected driver is not available");
                await LoadAvailableDrivers(model);
                return View(model);
            }

            if (!driver.IsApproved)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("DriverId", "Selected driver is not approved");
                await LoadAvailableDrivers(model);
                return View(model);
            }

            Broker? selectedBroker = null;
            if (model.BrokerId.HasValue && model.BrokerId.Value > 0)
            {
                selectedBroker = await _db.Brokers.FirstOrDefaultAsync(b => b.Id == model.BrokerId.Value);
                if (selectedBroker != null && selectedBroker.IsBlacklisted)
                {
                    TempData["Warning"] = $"WARNING: Broker {selectedBroker.CompanyName} ({selectedBroker.DisplayMc}) is on the Bad Broker / Do-Not-Load list! Reason: {selectedBroker.BlacklistReason ?? "Unspecified"}";
                }
            }

            if (driver.MedicalCardExpirationDate.HasValue && ComplianceService.IsMedicalCardExpired(driver, DateTime.UtcNow))
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("DriverId", "Selected driver's DOT Medical Card has expired (49 CFR 391.41) and cannot be dispatched.");
                await LoadAvailableDrivers(model);
                return View(model);
            }

            Vehicle? selectedVehicle = null;
            if (model.VehicleId.HasValue && model.VehicleId.Value > 0)
            {
                selectedVehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == model.VehicleId.Value);
                if (selectedVehicle == null)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("VehicleId", "Selected vehicle not found.");
                    await LoadAvailableDrivers(model);
                    return View(model);
                }

                if (selectedVehicle.Status != VehicleStatus.Active)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("VehicleId", $"Selected vehicle unit is not Active (current status: {selectedVehicle.Status}).");
                    await LoadAvailableDrivers(model);
                    return View(model);
                }

                if (selectedVehicle.RegistrationExpirationDate < DateTime.UtcNow)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("VehicleId", "Selected vehicle registration has expired.");
                    await LoadAvailableDrivers(model);
                    return View(model);
                }
            }
            else
            {
                // Backwards compatibility fallback for legacy test invocations:
                // Auto-bind to the driver's assigned tractor or the first active vehicle.
                if (driver.VehicleId.HasValue)
                {
                    selectedVehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == driver.VehicleId.Value && v.Status == VehicleStatus.Active);
                }

                if (selectedVehicle == null)
                {
                    selectedVehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Status == VehicleStatus.Active && v.RegistrationExpirationDate >= DateTime.UtcNow);
                }
            }

            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                DriverId = driver.Id,
                AssignedByAdminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                DriverPayment = driver.PricePerKm * trip.Distance,
                Status = TripStatus.Assigned,
                VehicleId = selectedVehicle?.Id,
                AssignmentDate = DateTime.UtcNow,
                UpdateAt = DateTime.UtcNow
            };

            trip.Status = TripStatus.Assigned;
            trip.VehicleId = selectedVehicle?.Id;
            if (selectedBroker != null)
            {
                trip.BrokerId = selectedBroker.Id;
            }
            if (!string.IsNullOrWhiteSpace(model.InvoiceNumber))
            {
                trip.InvoiceNumber = InputSanitizer.StripDangerousTags(model.InvoiceNumber.Trim());
            }
            if (!string.IsNullOrWhiteSpace(model.BrokerLoadNumber))
            {
                trip.BrokerLoadNumber = InputSanitizer.StripDangerousTags(model.BrokerLoadNumber.Trim());
            }
            if (model.RateConfirmationAmount.HasValue && model.RateConfirmationAmount.Value > 0)
            {
                trip.RateConfirmationAmount = model.RateConfirmationAmount.Value;
                trip.DetentionFee = model.DetentionFee;
                trip.LumperFee = model.LumperFee;
                trip.OtherAccessorials = model.OtherAccessorials;
                trip.Deductions = model.Deductions;
                trip.DeductionReason = InputSanitizer.StripDangerousTags(model.DeductionReason);
                var settlement = SettlementCalculator.ForTrip(trip);
                driverTrip.DriverPayment = (double)settlement.DriverNetPay;
            }
            trip.UpdatedAt = DateTime.UtcNow;
            driver.IsAvailable = false;

            _db.DriverTrips.Add(driverTrip);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = $"Trip successfully assigned to {driver.UserName}";
            return RedirectToAction(nameof(TripRequests));
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Concurrency conflict during driver assignment for trip {TripId}", model.TripId);
            ModelState.AddModelError("", "The trip or driver was modified by another process. Please refresh and try again.");
            await LoadAvailableDrivers(model);
            return View(model);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error assigning trip");
            ModelState.AddModelError("", "An error occurred while assigning the trip");
            await LoadAvailableDrivers(model);
            return View(model);
        }
    }
    private async Task LoadAvailableDrivers(AssignTripViewModel model)
    {
        var allDrivers = await _userManager.GetUsersInRoleAsync("Driver");
    
        var availableDrivers = new List<Driver>();
        foreach (var user in allDrivers)
        {
            if (user is Driver driver && driver.IsAvailable)
            {
                availableDrivers.Add(driver);
            }
        }
        model.AvailableDrivers = availableDrivers;
        model.AvailableBrokers = await _db.Brokers.OrderBy(b => b.CompanyName).ToListAsync();
        model.AvailableVehicles = await _db.Vehicles
            .Where(v => v.Status == VehicleStatus.Active)
            .OrderBy(v => v.UnitNumber)
            .ToListAsync();
    }
    /// <summary>
    /// Gets page for creating a new trip by admin
    /// </summary>
    /// <param name="id">Reference ID</param>
    [HttpGet]
    public async Task<IActionResult> AddNewTripByAdmin(int id)
    {
        _logger.LogInformation($"CreateTrip GET called with id: {id}");
        var customer = await _db.Users.OfType<Customer>().ToListAsync();
        var availableDrivers = await _db.Users.OfType<Driver>().Where(d=>d.IsAvailable).ToListAsync();
        var model = new CreateTripByAdminViewModel
        {
            PickUpTime = DateTime.Now.AddHours(1),
            Customers = customer,
            Drivers = availableDrivers,
        };
        return View(model);
    }

    /// <summary>
    /// Creates a new trip
    /// </summary>
    /// <param name="model">Trip creation data</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNewTripByAdmin(CreateTripByAdminViewModel model)
    {
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("ModelState is invalid");
            model.Customers = await _db.Users
                .OfType<Customer>()
                .ToListAsync();
            
            model.Drivers = await _db.Users
                .OfType<Driver>()
                .Where(d => d.IsAvailable)
                .ToListAsync();

            return View(model);
        }

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var customer = await _db.Users
                .OfType<Customer>()
                .FirstOrDefaultAsync(c => c.Id == model.CustomerId);
            
            if (customer == null)
            {
                ModelState.AddModelError("CustomerId", "Invalid customer selected.");
                return View(model);
            }

            var driver = await _db.Users.OfType<Driver>()
                .FirstOrDefaultAsync(d => d.Id == model.DriverId && d.IsApproved && d.IsAvailable);
            if (driver == null)
            {
                ModelState.AddModelError("DriverId", "Select an approved, available driver.");
                model.Customers = await _db.Users.OfType<Customer>().ToListAsync();
                model.Drivers = await _db.Users.OfType<Driver>().Where(d => d.IsApproved && d.IsAvailable).ToListAsync();
                return View(model);
            }

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(adminId))
            {
                var admin = await _db.Users
                    .OfType<Admin>()
                    .FirstOrDefaultAsync(a => a.Id == adminId);
                
                if (admin == null)
                {
                    _logger.LogWarning($"Admin with ID {adminId} not found in database");
                    ModelState.AddModelError("", "Current admin user not found in database.");
                    return View(model);
                }
            }

            //C trip
            var trip = new Trip
            {
                CustomerId = model.CustomerId,
                FromCity = model.FromCity,
                Status = TripStatus.Assigned,
                ToCity = model.ToCity,
                FromAddress = model.FromAddress,
                FromZipCode = model.FromZipCode,
                ToAddress = model.ToAddress,
                ToZipCode = model.ToZipCode,
                GoodsType = model.GoodsType,
                Distance = model.Distance,
                Weight = model.weight,
                PickupTime = model.PickUpTime,
                VehicleId = driver.VehicleId,
                CreatedAt = DateTime.Now,
                AdminPrice = model.Price,
                AdminId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            };
            _db.Trips.Add(trip);
            await _db.SaveChangesAsync();
            _logger.LogInformation($"Successfully created Trip with ID: {trip.Id}");
            //C dt
            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                Trip = trip,
                DriverId = model.DriverId,
                AssignedByAdminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                DriverPayment = driver.PricePerKm * model.Distance,
                Status = TripStatus.Assigned,
                VehicleId = driver.VehicleId,
                AssignmentDate = DateTime.Now,
                UpdateAt = DateTime.Now
            };
            _db.DriverTrips.Add(driverTrip);
            await _db.SaveChangesAsync();
            _logger.LogInformation($"Successfully created DriverTrip with ID: {driverTrip.Id}");
            
            driver.IsAvailable = false;
            _db.Users.Update(driver);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            try
            {
                await _emailSender.SendEmailAsync(
                    driver.Email!,
                    "New Trip Assignment",
                    $"Dear {driver.FirstName} {driver.LastName},\n\n" +
                    $"You have been assigned a new trip.\n\n" +
                    $"Trip Details:\n" +
                    $"Trip ID: {trip.Id}\n" +
                    $"From: {trip.FromCity}\n" +
                    $"To: {trip.ToCity}\n" +
                    $"Pickup Time: {trip.PickupTime}\n" +
                    $"Distance: {trip.Distance}km\n" +
                    $"Weight: {trip.Weight}kg\n" +
                    $"Your Payment: {driverTrip.DriverPayment:C}\n\n" +
                    $"Please check your dashboard for more details.\n\n" +
                    $"Best regards,\n" +
                    $"Logirack Team"
                );
                _logger.LogInformation("Trip assignment email sent to driver {Email}", driver.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send trip assignment email to driver {Email}", driver.Email);
            }
            var tripCreationInfo = new
            {
                TripId = trip.Id,
                TripStatus = trip.Status.ToString(),
                DriverId = driver.Id,
                DriverName = $"{driver.FirstName} {driver.LastName}",
                CreatedBy = User.Identity?.Name ?? "Unknown admin",
                CreationDate = trip.CreatedAt
            };

            await _hubContext.Clients.Group("Admins").SendAsync("TripCreated", tripCreationInfo);
            
            return RedirectToAction(nameof(Dashboard));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating and assigning an admin-created trip");
            ModelState.AddModelError("", "An error occurred while saving changes.");
            model.Customers = await _db.Users
                .OfType<Customer>()
                .ToListAsync();
            
            model.Drivers = await _db.Users
                .OfType<Driver>()
                .Where(d => d.IsAvailable)
                .ToListAsync();
            return View(model);
        }
    }
    
    /// <summary>
    /// Gets trip history log
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> TripLog()
    {
        var trips = await _db.DriverTrips
            .Include(dt => dt.Trip).ThenInclude(trip => trip.Customer)
            .Include(dt => dt.Driver)
            .Select(dt => new TripDetailsViewModel
            {
                TripId = dt.Trip.Id,
                CustomerName = $"{dt.Trip.Customer.FirstName} {dt.Trip.Customer.LastName}", // Assuming Trip has a Customer relationship
                CustomerEmail = dt.Trip.Customer.Email ?? string.Empty,
                CustomerPhone = dt.Trip.Customer.PhoneNumber ?? string.Empty,
                FromCity = dt.Trip.FromCity,
                ToCity = dt.Trip.ToCity,
                FromAddress = dt.Trip.FromAddress,
                ToAddress = dt.Trip.ToAddress,
                FromZipCode = dt.Trip.FromZipCode,
                ToZipCode = dt.Trip.ToZipCode,
                Weight = dt.Trip.Weight,
                Volume = dt.Trip.Volume,
                Distance = dt.Trip.Distance,
                GoodTypes = dt.Trip.GoodsType,
                Notes = dt.Trip.Notes,
                AdminPrice = dt.Trip.AdminPrice,
                EstimatedPrice = dt.Trip.EstimatedPrice,
                Status = dt.Trip.Status,
                CreatedOn = dt.Trip.CreatedAt,
                PickupDate = dt.Trip.PickupTime
            })
            .ToListAsync();

        return View(trips);
    }

    [HttpGet]
    public async Task<IActionResult> PaymentManagement()
    {
        var payments = await _db.Payments
            .AsNoTracking()
            .Include(payment => payment.Driver)
            .Include(payment => payment.PaymentPeriod)
            .OrderByDescending(payment => payment.PaymentDate)
            .ToListAsync();
        return View(payments);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreparePayments()
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminId)) return Challenge();

        var completed = await _db.DriverTrips
            .Include(item => item.Driver)
            .Where(item => item.Status == TripStatus.Completed && item.PaymentPeriodId == null)
            .ToListAsync();

        if (completed.Count == 0)
        {
            TempData["PaymentMessage"] = "There are no new completed trips to prepare for payment.";
            return RedirectToAction(nameof(PaymentManagement));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        foreach (var group in completed.GroupBy(item => item.DriverId))
        {
            var period = new PaymentPeriod
            {
                StartDate = group.Min(item => item.CompletionDate ?? item.UpdateAt).Date,
                EndDate = group.Max(item => item.CompletionDate ?? item.UpdateAt).Date,
                PaymentFreq = group.First().Driver.PaymentFreq,
                Total = group.Sum(item => item.DriverPayment),
                Status = PaymentStatus.Pending
            };
            _db.PaymentPeriods.Add(period);
            await _db.SaveChangesAsync();

            foreach (var item in group) item.PaymentPeriodId = period.Id;
            _db.Payments.Add(new Payment
            {
                PaymentPeriodId = period.Id,
                DriverId = group.Key,
                Amount = period.Total,
                PaymentDate = DateTime.UtcNow,
                Status = PaymentStatus.Pending,
                ProcByAdminID = adminId
            });
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["PaymentMessage"] = $"Prepared {completed.Count} completed trip(s) for payment.";
        return RedirectToAction(nameof(PaymentManagement));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPaymentPaid(int id)
    {
        var payment = await _db.Payments.Include(item => item.PaymentPeriod).SingleOrDefaultAsync(item => item.Id == id);
        if (payment == null) return NotFound();
        payment.Status = PaymentStatus.Paid;
        payment.PaymentDate = DateTime.UtcNow;
        payment.PaymentPeriod.Status = PaymentStatus.Paid;
        await _db.SaveChangesAsync();
        TempData["PaymentMessage"] = $"Payment #{payment.Id} marked as paid.";
        return RedirectToAction(nameof(PaymentManagement));
    }

    [HttpGet]
    public async Task<IActionResult> Invoice(int id)
    {
        var payment = await _db.Payments.AsNoTracking()
            .Include(item => item.Driver)
            .Include(item => item.PaymentPeriod)
            .SingleOrDefaultAsync(item => item.Id == id);
        return payment == null ? NotFound() : View(payment);
    }

    [HttpGet]
    public async Task<IActionResult> InvoiceGeneration()
    {
        var completedTrips = await _db.DriverTrips
            .AsNoTracking()
            .Include(driverTrip => driverTrip.Driver)
            .Include(driverTrip => driverTrip.Trip)
            .Where(driverTrip => driverTrip.Status == TripStatus.Completed)
            .OrderByDescending(driverTrip => driverTrip.CompletionDate ?? driverTrip.UpdateAt)
            .ToListAsync();
        return View(completedTrips);
    }

    /// <summary>
    /// Downloads the generated PDF invoice for any completed trip.
    /// Admin authorization required.
    /// </summary>
    /// <param name="id">The trip ID</param>
    /// <returns>PDF file stream</returns>
    [HttpGet]
    public async Task<IActionResult> DownloadInvoice(int id)
    {
        var trip = await _db.Trips
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (trip == null)
        {
            return NotFound();
        }

        if (trip.Status != TripStatus.Completed)
        {
            return BadRequest("Invoice is only available for completed trips.");
        }

        var customer = trip.Customer;
        if (customer == null)
        {
            var appUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == trip.CustomerId);
            customer = appUser as Customer ?? new Customer
            {
                Id = trip.CustomerId,
                UserName = appUser?.UserName,
                Email = appUser?.Email,
                PhoneNumber = appUser?.PhoneNumber,
                FirstName = appUser?.FirstName ?? "Customer",
                LastName = appUser?.LastName ?? "",
                CompanyName = appUser?.CompanyName ?? string.Empty
            };
        }

        var pdfBytes = _invoiceService.GenerateInvoicePdf(trip, customer);
        return File(pdfBytes, "application/pdf", $"Invoice_Trip_{trip.Id}.pdf");
    }

    // ==========================================
    // Fleet & Equipment Management (M1)
    // ==========================================

    /// <summary>
    /// Displays list of all fleet vehicle units.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> VehicleManagement()
    {
        var vehicles = await _db.Vehicles
            .Include(v => v.AssignedDriver)
            .OrderBy(v => v.UnitNumber)
            .ToListAsync();

        var drivers = (await _userManager.GetUsersInRoleAsync("Driver"))
            .OfType<Driver>()
            .OrderBy(d => d.FirstName)
            .ThenBy(d => d.LastName)
            .ToList();

        ViewBag.AvailableDrivers = drivers;
        return View("VehicleManagement", vehicles);
    }

    /// <summary>
    /// Alias for VehicleManagement
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Vehicles()
    {
        return await VehicleManagement();
    }

    /// <summary>
    /// Displays form to create a new vehicle.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CreateVehicle()
    {
        var drivers = (await _userManager.GetUsersInRoleAsync("Driver"))
            .OfType<Driver>()
            .OrderBy(d => d.FirstName)
            .ThenBy(d => d.LastName)
            .ToList();

        ViewBag.Drivers = drivers;
        var model = new Vehicle
        {
            Year = DateTime.UtcNow.Year,
            RegistrationExpirationDate = DateTime.UtcNow.AddYears(1),
            AnnualDotInspectionExpiration = DateTime.UtcNow.AddYears(1),
            Form2290ExpirationDate = DateTime.UtcNow.AddYears(1),
            Status = VehicleStatus.Active
        };
        return View(model);
    }

    /// <summary>
    /// Creates a new vehicle unit.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateVehicle(Vehicle vehicle)
    {
        if (await _db.Vehicles.AnyAsync(v => v.UnitNumber == vehicle.UnitNumber))
        {
            ModelState.AddModelError("UnitNumber", "A vehicle with this Unit Number already exists.");
        }

        if (await _db.Vehicles.AnyAsync(v => v.Vin == vehicle.Vin))
        {
            ModelState.AddModelError("Vin", "A vehicle with this VIN already exists.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Drivers = (await _userManager.GetUsersInRoleAsync("Driver"))
                .OfType<Driver>()
                .OrderBy(d => d.FirstName)
                .ThenBy(d => d.LastName)
                .ToList();
            return View(vehicle);
        }

        vehicle.CreatedAt = DateTime.UtcNow;
        vehicle.UpdatedAt = DateTime.UtcNow;

        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(vehicle.AssignedDriverId))
        {
            var otherVehicles = await _db.Vehicles
                .Where(v => v.Id != vehicle.Id && v.AssignedDriverId == vehicle.AssignedDriverId)
                .ToListAsync();
            foreach (var ov in otherVehicles)
            {
                ov.AssignedDriverId = null;
            }

            var driver = await _db.Drivers.FindAsync(vehicle.AssignedDriverId);
            if (driver != null)
            {
                driver.VehicleId = vehicle.Id;
            }
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = $"Vehicle Unit {vehicle.UnitNumber} created successfully.";
        return RedirectToAction(nameof(VehicleManagement));
    }

    /// <summary>
    /// Displays form to edit an existing vehicle.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> EditVehicle(int id)
    {
        var vehicle = await _db.Vehicles
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle == null)
        {
            return NotFound();
        }

        ViewBag.Drivers = (await _userManager.GetUsersInRoleAsync("Driver"))
            .OfType<Driver>()
            .OrderBy(d => d.FirstName)
            .ThenBy(d => d.LastName)
            .ToList();

        return View(vehicle);
    }

    /// <summary>
    /// Updates an existing vehicle unit.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditVehicle(int id, Vehicle vehicle)
    {
        if (id != vehicle.Id)
        {
            return BadRequest();
        }

        if (await _db.Vehicles.AnyAsync(v => v.Id != id && v.UnitNumber == vehicle.UnitNumber))
        {
            ModelState.AddModelError("UnitNumber", "Unit Number is already in use by another vehicle.");
        }

        if (await _db.Vehicles.AnyAsync(v => v.Id != id && v.Vin == vehicle.Vin))
        {
            ModelState.AddModelError("Vin", "VIN is already in use by another vehicle.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Drivers = (await _userManager.GetUsersInRoleAsync("Driver"))
                .OfType<Driver>()
                .OrderBy(d => d.FirstName)
                .ThenBy(d => d.LastName)
                .ToList();
            return View(vehicle);
        }

        var existing = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (existing == null)
        {
            return NotFound();
        }

        var previousDriverId = existing.AssignedDriverId;

        existing.UnitNumber = vehicle.UnitNumber;
        existing.Vin = vehicle.Vin;
        existing.LicensePlate = vehicle.LicensePlate;
        existing.LicenseState = vehicle.LicenseState;
        existing.Make = vehicle.Make;
        existing.Model = vehicle.Model;
        existing.Year = vehicle.Year;
        existing.RegistrationExpirationDate = vehicle.RegistrationExpirationDate;
        existing.AnnualDotInspectionExpiration = vehicle.AnnualDotInspectionExpiration;
        existing.Form2290ExpirationDate = vehicle.Form2290ExpirationDate;
        existing.PrePassTransponderId = vehicle.PrePassTransponderId;
        existing.Status = vehicle.Status;
        existing.AssignedDriverId = vehicle.AssignedDriverId;
        existing.UpdatedAt = DateTime.UtcNow;

        if (previousDriverId != vehicle.AssignedDriverId)
        {
            if (!string.IsNullOrEmpty(previousDriverId))
            {
                var prevDriver = await _db.Drivers.FindAsync(previousDriverId);
                if (prevDriver != null && prevDriver.VehicleId == id)
                {
                    prevDriver.VehicleId = null;
                }
            }

            if (!string.IsNullOrEmpty(vehicle.AssignedDriverId))
            {
                var otherVehicles = await _db.Vehicles
                    .Where(v => v.Id != id && v.AssignedDriverId == vehicle.AssignedDriverId)
                    .ToListAsync();
                foreach (var ov in otherVehicles)
                {
                    ov.AssignedDriverId = null;
                }

                var newDriver = await _db.Drivers.FindAsync(vehicle.AssignedDriverId);
                if (newDriver != null)
                {
                    newDriver.VehicleId = id;
                }
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Vehicle Unit {existing.UnitNumber} updated successfully.";
        return RedirectToAction(nameof(VehicleManagement));
    }

    /// <summary>
    /// Changes the operational status of a vehicle.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetVehicleStatus(int id, VehicleStatus status)
    {
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle == null)
        {
            return NotFound();
        }

        vehicle.Status = status;
        vehicle.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Vehicle Unit {vehicle.UnitNumber} status changed to {status}.";
        return RedirectToAction(nameof(VehicleManagement));
    }

    /// <summary>
    /// Assigns a driver to a vehicle unit.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignDriverToVehicle(int vehicleId, string? driverId)
    {
        var vehicle = await _db.Vehicles.FindAsync(vehicleId);
        if (vehicle == null)
        {
            return NotFound();
        }

        if (!string.IsNullOrEmpty(vehicle.AssignedDriverId))
        {
            var oldDriver = await _db.Drivers.FindAsync(vehicle.AssignedDriverId);
            if (oldDriver != null && oldDriver.VehicleId == vehicleId)
            {
                oldDriver.VehicleId = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(driverId))
        {
            var driver = await _db.Drivers.FindAsync(driverId);
            if (driver == null)
            {
                return NotFound("Driver not found.");
            }

            var prevVehicles = await _db.Vehicles
                .Where(v => v.Id != vehicleId && v.AssignedDriverId == driverId)
                .ToListAsync();
            foreach (var pv in prevVehicles)
            {
                pv.AssignedDriverId = null;
            }

            vehicle.AssignedDriverId = driverId;
            driver.VehicleId = vehicleId;
        }
        else
        {
            vehicle.AssignedDriverId = null;
        }

        vehicle.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Driver assignment for Vehicle Unit {vehicle.UnitNumber} updated.";
        return RedirectToAction(nameof(VehicleManagement));
    }

    /// <summary>
    /// Unassigns any assigned driver from a vehicle unit.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignVehicleDriver(int vehicleId)
    {
        return await AssignDriverToVehicle(vehicleId, null);
    }

    // ==========================================
    // Broker CRM & Bad Broker Protection (R2)
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> BrokerManagement()
    {
        var brokers = await _db.Brokers
            .OrderByDescending(b => b.IsBlacklisted)
            .ThenBy(b => b.CompanyName)
            .ToListAsync();
        return View("Brokers", brokers);
    }

    [HttpGet]
    public async Task<IActionResult> Brokers()
    {
        return await BrokerManagement();
    }

    [HttpGet]
    public IActionResult CreateBroker()
    {
        return View(new Broker());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBroker(Broker broker)
    {
        broker.McNumber = Broker.NormalizeMc(broker.McNumber);
        broker.CompanyName = InputSanitizer.StripDangerousTags(broker.CompanyName)?.Trim() ?? string.Empty;
        broker.AgentName = InputSanitizer.StripDangerousTags(broker.AgentName)?.Trim();
        broker.BlacklistReason = InputSanitizer.StripDangerousTags(broker.BlacklistReason)?.Trim();
        broker.Notes = InputSanitizer.StripDangerousTags(broker.Notes)?.Trim();

        ModelState.Remove(nameof(Broker.McNumber));
        if (string.IsNullOrWhiteSpace(broker.McNumber) || !System.Text.RegularExpressions.Regex.IsMatch(broker.McNumber, @"^\d{1,8}$"))
        {
            ModelState.AddModelError(nameof(Broker.McNumber), "MC Number must contain 1 to 8 digits.");
        }
        else if (await _db.Brokers.AnyAsync(b => b.McNumber == broker.McNumber))
        {
            ModelState.AddModelError(nameof(Broker.McNumber), $"A broker with MC#{broker.McNumber} already exists.");
        }

        if (broker.IsBlacklisted && string.IsNullOrWhiteSpace(broker.BlacklistReason))
        {
            ModelState.AddModelError(nameof(Broker.BlacklistReason), "Please provide a reason when blacklisting a broker.");
        }

        if (!ModelState.IsValid)
        {
            return View(broker);
        }

        if (broker.IsBlacklisted)
        {
            broker.BlacklistedAt = DateTime.UtcNow;
        }

        broker.CreatedAt = DateTime.UtcNow;
        broker.UpdatedAt = DateTime.UtcNow;

        _db.Brokers.Add(broker);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Broker {broker.CompanyName} ({broker.DisplayMc}) saved.";
        return RedirectToAction(nameof(Brokers));
    }

    [HttpGet]
    public async Task<IActionResult> EditBroker(int id)
    {
        var broker = await _db.Brokers.FirstOrDefaultAsync(b => b.Id == id);
        if (broker == null)
        {
            return NotFound();
        }
        return View(broker);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBroker(int id, Broker broker)
    {
        if (id != broker.Id)
        {
            return BadRequest();
        }

        var existing = await _db.Brokers.FirstOrDefaultAsync(b => b.Id == id);
        if (existing == null)
        {
            return NotFound();
        }

        var normalizedMc = Broker.NormalizeMc(broker.McNumber);
        ModelState.Remove(nameof(Broker.McNumber));
        if (string.IsNullOrWhiteSpace(normalizedMc) || !System.Text.RegularExpressions.Regex.IsMatch(normalizedMc, @"^\d{1,8}$"))
        {
            ModelState.AddModelError(nameof(Broker.McNumber), "MC Number must contain 1 to 8 digits.");
        }
        else if (await _db.Brokers.AnyAsync(b => b.Id != id && b.McNumber == normalizedMc))
        {
            ModelState.AddModelError(nameof(Broker.McNumber), $"MC#{normalizedMc} is already in use by another broker.");
        }

        if (broker.IsBlacklisted && string.IsNullOrWhiteSpace(broker.BlacklistReason))
        {
            ModelState.AddModelError(nameof(Broker.BlacklistReason), "Please provide a reason when blacklisting a broker.");
        }

        if (!ModelState.IsValid)
        {
            return View(broker);
        }

        existing.CompanyName = InputSanitizer.StripDangerousTags(broker.CompanyName)?.Trim() ?? string.Empty;
        existing.McNumber = normalizedMc;
        existing.AgentName = InputSanitizer.StripDangerousTags(broker.AgentName)?.Trim();
        existing.Phone = broker.Phone;
        existing.PhoneExtension = broker.PhoneExtension;
        existing.Email = broker.Email;
        existing.IsBlacklisted = broker.IsBlacklisted;
        existing.BlacklistReason = InputSanitizer.StripDangerousTags(broker.BlacklistReason)?.Trim();
        existing.BlacklistedAt = broker.IsBlacklisted ? (existing.BlacklistedAt ?? DateTime.UtcNow) : null;
        existing.Notes = InputSanitizer.StripDangerousTags(broker.Notes)?.Trim();
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Broker {existing.CompanyName} ({existing.DisplayMc}) updated.";
        return RedirectToAction(nameof(Brokers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBrokerBlacklist(int id, bool isBlacklisted, string? reason)
    {
        var broker = await _db.Brokers.FirstOrDefaultAsync(b => b.Id == id);
        if (broker == null)
        {
            return NotFound();
        }

        broker.IsBlacklisted = isBlacklisted;
        broker.BlacklistReason = isBlacklisted ? (InputSanitizer.StripDangerousTags(reason)?.Trim() ?? "Flagged on Bad Broker list") : null;
        broker.BlacklistedAt = isBlacklisted ? DateTime.UtcNow : null;
        broker.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData[isBlacklisted ? "Warning" : "Success"] = isBlacklisted
            ? $"Broker {broker.CompanyName} ({broker.DisplayMc}) flagged on Bad Broker list."
            : $"Broker {broker.CompanyName} ({broker.DisplayMc}) removed from Bad Broker list.";
        return RedirectToAction(nameof(Brokers));
    }

    // ==========================================
    // Load Accounting, Settlements & TONU (R3)
    // ==========================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTripSettlement(
        int tripId,
        int? brokerId,
        string? invoiceNumber,
        string? brokerLoadNumber,
        decimal rateConfirmationAmount,
        decimal detentionFee = 0m,
        decimal lumperFee = 0m,
        decimal otherAccessorials = 0m,
        decimal deductions = 0m,
        string? deductionReason = null)
    {
        var trip = await _db.Trips
            .Include(t => t.DriverTrip)
            .FirstOrDefaultAsync(t => t.Id == tripId);

        if (trip == null)
        {
            return NotFound();
        }

        if (rateConfirmationAmount < 0 || detentionFee < 0 || lumperFee < 0 || otherAccessorials < 0 || deductions < 0)
        {
            return BadRequest("Settlement amounts cannot be negative.");
        }

        trip.BrokerId = brokerId;
        trip.InvoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? null : InputSanitizer.StripDangerousTags(invoiceNumber.Trim());
        trip.BrokerLoadNumber = string.IsNullOrWhiteSpace(brokerLoadNumber) ? null : InputSanitizer.StripDangerousTags(brokerLoadNumber.Trim());
        trip.RateConfirmationAmount = rateConfirmationAmount;
        trip.DetentionFee = detentionFee;
        trip.LumperFee = lumperFee;
        trip.OtherAccessorials = otherAccessorials;
        trip.Deductions = deductions;
        trip.DeductionReason = InputSanitizer.StripDangerousTags(deductionReason)?.Trim();
        trip.UpdatedAt = DateTime.UtcNow;

        var breakdown = SettlementCalculator.ForTrip(trip);
        if (trip.DriverTrip != null)
        {
            trip.DriverTrip.DriverPayment = (double)breakdown.DriverNetPay;
            trip.DriverTrip.UpdateAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Settlement updated. Gross: ${breakdown.GrossRate:F2} | 12% Dispatch: ${breakdown.DispatchFee:F2} | 88% Driver Net: ${breakdown.DriverNetPay:F2}";
        return RedirectToAction(nameof(TripDetails), new { id = tripId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkTripTonu(int tripId, decimal tonuAmount = 150m, decimal deductions = 0m)
    {
        var trip = await _db.Trips
            .Include(t => t.DriverTrip)
                .ThenInclude(dt => dt!.Driver)
            .FirstOrDefaultAsync(t => t.Id == tripId);

        if (trip == null)
        {
            return NotFound();
        }

        SettlementBreakdown breakdown;
        try
        {
            breakdown = SettlementCalculator.CalculateTonu(tonuAmount, deductions);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ex.Message);
        }

        trip.Status = TripStatus.Tonu;
        trip.TonuAmount = tonuAmount;
        trip.Deductions = deductions;
        trip.UpdatedAt = DateTime.UtcNow;

        if (trip.DriverTrip != null)
        {
            trip.DriverTrip.Status = TripStatus.Tonu;
            trip.DriverTrip.DriverPayment = (double)breakdown.DriverNetPay;
            trip.DriverTrip.UpdateAt = DateTime.UtcNow;
            if (trip.DriverTrip.Driver != null)
            {
                trip.DriverTrip.Driver.IsAvailable = true;
            }
        }

        await _db.SaveChangesAsync();
        TempData["Warning"] = $"Trip #{trip.Id} marked as TONU (${tonuAmount:F2}). Driver Net Payout: ${breakdown.DriverNetPay:F2}.";
        return RedirectToAction(nameof(TripDetails), new { id = tripId });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadSettlement(int id)
    {
        var trip = await _db.Trips
            .Include(t => t.Broker)
            .Include(t => t.Vehicle)
            .Include(t => t.DriverTrip)
                .ThenInclude(dt => dt!.Driver)
            .Include(t => t.DriverTrip)
                .ThenInclude(dt => dt!.Vehicle)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (trip == null)
        {
            return NotFound();
        }

        var driver = trip.DriverTrip?.Driver ?? new Driver
        {
            FirstName = "Unassigned",
            LastName = "Driver",
            Email = "dispatch@ruftalogistics.com"
        };

        var breakdown = SettlementCalculator.ForTrip(trip);
        var vehicle = trip.DriverTrip?.Vehicle ?? trip.Vehicle;
        var pdfBytes = _invoiceService.GenerateSettlementStatementPdf(trip, driver, breakdown, vehicle, trip.Broker);
        return File(pdfBytes, "application/pdf", $"Settlement_Trip_{trip.Id}.pdf");
    }
}
