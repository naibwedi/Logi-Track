﻿using System.Security.Claims;
using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using logirack.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace logirack.Services;


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

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    public AdminController(ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<AdminController> logger,
        IEmailSender emailSender,IHubContext<TripHub> hubContext,
        PasswordService passwordService)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
        _hubContext = hubContext;
        _emailSender = emailSender;
        _passwordService = passwordService;
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
                drivers = drivers.Where(d => d.Email.Contains(searchString));
                break;
            case "PhoneNumber":
                drivers = drivers.Where(d => d.PhoneNumber.Contains(searchString));
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

            // Generate a random password
            var password = _passwordService.GeneratePassword();

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
            };
            var result = await _userManager.CreateAsync(newDriver, password);
            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new driver");
                await _userManager.AddToRoleAsync(newDriver, "Driver");

                // Send password to driver's email
                try
                {
                    await _emailSender.SendEmailAsync(model.DriverEmail, "Driver Account Created",
                        $"Your account has been created. Your password is: {password}");
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
                Customers = customers
            };

            return View(dashboardStats);
        }
        catch (Exception ex)
        {
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
            Email = driver.Email,
            PhoneNumber = driver.PhoneNumber,
            PricePerKm = driver.PricePerKm,
            PaymentFreq = driver.PaymentFreq,
            IsAvailable = driver.IsAvailable,
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
                        user.Email,
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
        var trip = await _db.Trips.Include(t => t.Customer).FirstOrDefaultAsync(t=>t.Id == id);
        if(trip == null)
            return NotFound();
        var viewModel = new TripDetailsViewModel
        {
            TripId = trip.Id,
            CustomerName = $"{trip.Customer.FirstName} {trip.Customer.LastName}",
            CustomerEmail = trip.Customer.Email,
            CustomerPhone = trip.Customer.PhoneNumber,
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
            Status = trip.Status,
            CreatedOn = trip.CreatedAt,
            PickupDate = trip.PickupTime
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
        
        var admin = await _userManager.GetUserAsync(User) as Admin;
        if (admin == null)
            return Forbid();
        trip.Status = isApproved ? TripStatus.ApprovedByAdmin : TripStatus.CancelledByAdmin;
        trip.AdminId=admin.Id;
        trip.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
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

            if (trip == null)
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

        var tripToUpdate = await _db.Trips.FindAsync(model.TripId);
        if (tripToUpdate == null)
        {
            return NotFound();
        }

        tripToUpdate.AdminPrice = model.AdminPrice;
        tripToUpdate.Status = TripStatus.PriceSet;
        tripToUpdate.UpdatedAt = DateTime.Now;

        try
        {
            await _db.SaveChangesAsync();
            try
            {
                await _emailSender.SendEmailAsync(
                    tripToUpdate.Customer.Email,
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

        try
        {
            var trip = await _db.Trips
                .Include("DriverTrip")    
                .Include("Customer")      
                .FirstOrDefaultAsync(t => t.Id == model.TripId);

            if (trip == null)
            {
                _logger.LogError($"Trip not found. TripId: {model.TripId}");
                return NotFound("Trip not found");
            }

            if (trip.DriverTrip != null)
            {
                ModelState.AddModelError("", "This trip is already assigned to a driver");
                await LoadAvailableDrivers(model);
                return View(model);
            }

            var driver = await _userManager.FindByIdAsync(model.DriverId) as Driver;
            if (driver == null)
            {
                ModelState.AddModelError("DriverId", "Selected driver not found");
                await LoadAvailableDrivers(model);
                return View(model);
            }

            if (!driver.IsAvailable)
            {
                ModelState.AddModelError("DriverId", "Selected driver is not available");
                await LoadAvailableDrivers(model);
                return View(model);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var driverTrip = new DriverTrip
                {
                    TripId = trip.Id,
                    DriverId = driver.Id,
                    AssignedByAdminId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                    DriverPayment = driver.PricePerKm * trip.Distance,
                    Status = TripStatus.Assigned,
                    AssignmentDate = DateTime.Now,
                    UpdateAt = DateTime.Now
                };

                trip.Status = TripStatus.Assigned;
                driver.IsAvailable = false;

                _db.DriverTrips.Add(driverTrip);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] = $"Trip successfully assigned to {driver.UserName}";
                return RedirectToAction(nameof(TripRequests));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
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
            var customer = await _db.Users
                .OfType<Customer>()
                .FirstOrDefaultAsync(c => c.Id == model.CustomerId);
            
            if (customer == null)
            {
                ModelState.AddModelError("CustomerId", "Invalid customer selected.");
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
                CreatedAt = DateTime.Now,
                AdminPrice = model.Price,
                AdminId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            };
            _db.Trips.Add(trip);
            await _db.SaveChangesAsync();
            _logger.LogInformation($"Successfully created Trip with ID: {trip.Id}");
            //C dt
            var driver = await _db.Users.OfType<Driver>().FirstOrDefaultAsync(d => d.Id == model.DriverId);
            if (driver == null)
            {
                throw new Exception($"Trip with ID {model.DriverId} not found");
            }

            var driverTrip = new DriverTrip
            {
                TripId = trip.Id,
                Trip = trip,
                DriverId = model.DriverId,
                AssignedByAdminId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                DriverPayment = driver.PricePerKm * model.Distance,
                Status = TripStatus.Assigned,
                AssignmentDate = DateTime.Now,
                UpdateAt = DateTime.Now
            };
            _db.DriverTrips.Add(driverTrip);
            await _db.SaveChangesAsync();
            _logger.LogInformation($"Successfully created DriverTrip with ID: {driverTrip.Id}");
            
            driver.IsAvailable = false;
            _db.Users.Update(driver);
            await _db.SaveChangesAsync();
            try
            {
                await _emailSender.SendEmailAsync(
                    driver.Email,
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
                CreatedBy = User.Identity.Name,
                CreationDate = trip.CreatedAt
            };

            await _hubContext.Clients.Group("Admins").SendAsync("TripCreated", tripCreationInfo);
            
            return RedirectToAction(nameof(Dashboard));
        }
        catch (Exception ex)
        {
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
            .Include(dt => dt.Trip)
            .Include(dt => dt.Driver)
            .Select(dt => new TripDetailsViewModel
            {
                TripId = dt.Trip.Id,
                CustomerName = $"{dt.Trip.Customer.FirstName} {dt.Trip.Customer.LastName}", // Assuming Trip has a Customer relationship
                CustomerEmail = dt.Trip.Customer.Email,
                CustomerPhone = dt.Trip.Customer.PhoneNumber,
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

   


}
