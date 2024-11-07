using System.Security.Claims;
using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    public AdminController(ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<AdminController> logger,
        IEmailSender emailSender,
        PasswordService passwordService)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
        _emailSender = emailSender;
        _passwordService = passwordService;
    }
/// <summary>
/// creating functionality for searching drivers based on filters
/// </summary>
/// <param name="searchString"></param>
/// <param name="searchCriteria"></param>
/// <returns></returns>
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

    
    [HttpGet]
    public IActionResult CreateDriver()
    {
        return View();
    }

    /// <summary>
    /// Processes  the creation of a new Driver.
    /// </summary>
    /// <param name="model">The data submitted from the form.</param>
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
    /// Displays a list of all Driver users.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DriverManagment()
    {
        var allDrivers = await _userManager.GetUsersInRoleAsync("Driver");
        var drivers = allDrivers.OfType<Driver>().ToList();
        return View(drivers);
    }

    public IActionResult Dashboard()
    {
        return View();
    }

    /// <summary>
    /// Edits a Driver user.
    /// </summary>
    /// <param name="id">The ID of the Driver to edit.</param>
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
    /// Deletes a Driver user.
    /// </summary>
    /// <param name="id">The ID of the Driver to delete.</param>
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
    
    [HttpGet]
    public async Task<IActionResult> PendingApprovals()
    {
        var pendingUsers=await _userManager.Users.Where(x => x.IsApproved == false ).ToListAsync();
        return View(pendingUsers);
    }
    
    
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
            TempData["Success"] = "User has been approved.";
            _logger.LogInformation("User {Email} approved by admin.", user.Email);
        }
        else
        {
            // Hvis brukeren ikke er godkjent, kan du velge å slette brukeren eller sette en annen status
            await _userManager.DeleteAsync(user);
            TempData["Success"] = "User has been rejected and deleted.";
            _logger.LogInformation("User {Email} rejected by admin and deleted.", user.Email);
        }

        await _db.SaveChangesAsync();
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
    /// <param name="tId">The ID of the trip to review.</param>
    /// <param name="isApproved">Indicates whether the trip is approved (true) or rejected (false).</param>
    /// <returns>Redirects to the TripRequests view after processing.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewTrip(int tId, bool isApproved)
    {
        var trip = await _db.Trips.FindAsync(tId);
        if (trip == null)
            return NotFound();
        
        //for getting curent admin like assignment 4 
        var admin = await _userManager.GetUserAsync(User) as Admin;
        if (admin == null)
            return Forbid();
        trip.Status = isApproved ? TripStatus.ApprovedByAdmin : TripStatus.CancelledByAdmin;
        trip.AdminId=admin.Id;
        trip.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        //notification to customer about trip status 
        TempData["Success"] =isApproved ? "Trip Approved" : "Trip Rejected";
        return RedirectToAction(nameof(TripRequests));

    }

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
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", "An error occurred while saving changes.");
            return View(model);
        }

        return RedirectToAction(nameof(TripDetails), new { id = model.TripId });
    }

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignTrip(AssignTripViewModel model)
    {
        _logger.LogInformation($"AssignTrip POST called with TripId: {model.TripId}, DriverId: {model.DriverId}");
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("ModelState is invalid");
            foreach (var modelState in ModelState.Values)
            {
                foreach (var error in modelState.Errors)
                {
                    _logger.LogWarning($"Validation error: {error.ErrorMessage}");
                }
            }
        }

        try
        {

            var trip = await _db.Trips.FindAsync(model.TripId);
            var driver = await _userManager.FindByIdAsync(model.DriverId) as Driver;

            if (trip == null || driver == null)
            {
                _logger.LogError($"Trip or Driver not found. TripId: {model.TripId}, DriverId: {model.DriverId}");
                return NotFound();
            }

            if (!driver.IsAvailable)
            {
                ModelState.AddModelError("DriverId", "this driver is not available");
                return View(model);
            }

            _logger.LogInformation($"Creating DriverTrip for Trip: {trip.Id}, Driver: {driver.Email}");

            var driverTrip = new DriverTrip
            {
                TripId = model.TripId,
                Trip = trip,
                DriverId = driver.Id,
                Driver = driver,
                AssignedByAdminId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                DriverPayment = driver.PricePerKm * trip.Distance,
                Status = TripStatus.Assigned,
                AssignmentDate = DateTime.Now,
                UpdateAt = DateTime.Now
            };
            driver.IsAvailable = false;
            await _userManager.UpdateAsync(driver);
            _db.DriverTrips.Add(driverTrip);
            await _db.SaveChangesAsync();
            _logger.LogInformation($"Successfully created DriverTrip with ID: {driverTrip.Id}");
            TempData["Success"] = $"Trip Assigned To User {driver.UserName}";
            return RedirectToAction(nameof(TripRequests));
        }
        catch(Exception ex)
        {
            _logger.LogError($"Error assigning trip: {ex.Message}");
            var availableDrivers = await _userManager.GetUsersInRoleAsync("Driver");
            model.AvailableDrivers=availableDrivers.OfType<Driver>().Where(d => d.IsAvailable).ToList();
            return View(model);
        }
        
    }

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
            //C trip
            var trip = new Trip
            {
                CustomerId = model.CustomerId,
                FromCity = model.FromCity,
                ToCity = model.ToCity,
                FromAddress = model.FromAddress,
                FromZipCode = model.FromZipCode,
                ToAddress = model.ToAddress,
                ToZipCode = model.ToZipCode,
                GoodsType = model.GoodsType,
                Distance = model.Distance,
                Weight = model.weight,
                PickupTime = model.PickUpTime,
                Status = TripStatus.PriceSet,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
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
            _logger.LogInformation($"Successfully change  Drive status of driver : {driverTrip.Id}");
            
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

}
