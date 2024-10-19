using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Host;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// init a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    public AdminController(ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<AdminController> logger)
    {
        _db=db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult CreateDriver()
    {
        return View();
    }

    
    /// <summary>
    /// Processes the creation of a new Driver.
    /// </summary>
    /// <param name="model">The data submitted from the form.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDriver(CreateDriverViewModel model)
    {
        if (ModelState.IsValid)
        {
            //check for email 
            var euser=await _userManager.FindByEmailAsync(model.DriverEmail);
            if (euser!=null)
            {
                ModelState.AddModelError(nameof(CreateDriverViewModel.DriverEmail), "Email Already Exists");
                return View(model);
            }
            //check for phone number 
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
                DateOfBirth= model.DateOfBirth,
            };
            var result = await _userManager.CreateAsync(newDriver, model.DriverPassword);
            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new driver");
                await _userManager.AddToRoleAsync(newDriver, "Driver");
                TempData["Success"] = "Driver Created";
                return RedirectToAction(nameof(DriverList));
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                    _logger.LogError("Error creating a driver {1}",error.Description);
                }
            }
        }
        return View(model);
    }

    /// <summary>
    /// Displays a list of all Driver users.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DriverList()
    {
        var allDrievers = await _userManager.GetUsersInRoleAsync("Driver");
        var drivers = allDrievers.OfType<Driver>().ToList();
        return View(drivers);
    }
    
    public IActionResult Dashboard()
    {
        return View();
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
        if (driver==null)
        {
            return NotFound();
        }
        var result = await _userManager.DeleteAsync(driver);
        if (result.Succeeded)
        {
            _logger.LogInformation("Driver {1} deleted",driver.Email);
        }
        else
        {
            _logger.LogError("Error deleting driver {1} ",driver.Email);
        }
        return RedirectToAction(nameof(DriverList));
    }

    [HttpGet]
    public async Task<IActionResult> EditDriver(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest();
        }

        var driver = await _userManager.FindByIdAsync(id) as Driver;
        if (driver== null)
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
            if (driver==null)
            {
                return NotFound();
            }
            //cheking email uniqueness 
            if (!string.Equals(driver.Email, model.Email,StringComparison.OrdinalIgnoreCase))
            {
                var euser = await _userManager.FindByEmailAsync(model.Email);
                if (euser!=null && euser.Id!=model.id)
                {
                    ModelState.AddModelError(nameof(EditDriverViewModel.Email), "Email Already Exists");
                    return View(model);
                }
                driver.Email = model.Email;
                driver.UserName = model.Email;
                driver.EmailConfirmed=true;
            }
            //checking phonenumber 
            if (!string.Equals(driver.PhoneNumber, model.PhoneNumber, StringComparison.OrdinalIgnoreCase))
            {
                var ephone= await _userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber && x.Id!=model.id);
                if (ephone!=null)
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
                return RedirectToAction(nameof(DriverList));
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                    _logger.LogError("Error updating driver {1} ",error.Description);
                }
            }
        }
        return View(model);
    }
    public IActionResult ManageDrivers()
    {
        return View();
    }

    public IActionResult AddNewTrip()
    {
        return View();
    }


    public IActionResult InvoiceGeneration()
    {
        return View();
    }

    public IActionResult PaymentManagement()
    {
        return View();
    }
    
    // List all customers pending approval
    [HttpGet]
    public IActionResult PendingApprovals()
    {
        var unapprovedUsers = _userManager.Users.Where(u => !u.IsApproved).ToList();
        return View(unapprovedUsers);
    }

    // Approve a customer
    [HttpPost]
    public async Task<IActionResult> ApproveUser(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.IsApproved = true;
            await _userManager.UpdateAsync(user);
        }

        return RedirectToAction("PendingApprovals");
    }
}