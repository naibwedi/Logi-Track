using logirack.Data;
using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace logirack.Controllers;


/// <summary>
/// Controller for SuperAdmin actions, including creating and managing Admin users.
/// </summary>

[Authorize (Roles = "SuperAdmin")]
public class SuperAdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager; 
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<SuperAdminController> _logger;
    /// <summary>
    /// Initializes a new instance of the <see cref="SuperAdminController"/> class.
    /// </summary>
    public SuperAdminController(
        ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager, ILogger<SuperAdminController> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }
    
    /// <summary>
    /// Displays the form to create a new Admin.
    /// </summary>
    [HttpGet]
    public IActionResult CreateAdmin()
    {
        return View();
    }
    
    /// <summary>
    /// Processes the creation of a new Admin.
    /// </summary>
    /// <param name="model">The data submitted from the form.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAdmin(CreateAdminViewModel model)
    {
        if (ModelState.IsValid)
        {
            //check if the email is already taken 
            var eUser = await _userManager.FindByEmailAsync(model.AdminEmail);
            if (eUser != null)
            {
                _logger.LogInformation($"User {eUser.Email} has already been added.");
                ModelState.AddModelError(nameof(CreateAdminViewModel.AdminEmail), "This email already exists.");
                return View(model);
            }
            //check if the phoneNumber is already taken
            var pUser = await _userManager.Users.FirstOrDefaultAsync(u=>u.PhoneNumber==model.PhoneNumber);
            if (pUser != null)
            {
                _logger.LogInformation($"User with phone number {pUser.PhoneNumber} has already been added.");
                ModelState.AddModelError(nameof(CreateAdminViewModel.PhoneNumber), "This phone number already exists.");
                return View(model);
            }
            var newadmin = new Admin
            {
                UserName = model.AdminEmail,
                Email = model.AdminEmail,
                EmailConfirmed = true, 
                IsApproved = true,
                CreatedOn = DateTime.Now,
                ModifiedOn = DateTime.Now,
                PhoneNumber = model.PhoneNumber,
                RoleType = "Admin",
                FirstName = model.FirstName,
                LastName = model.LastName
                
            };
            
            var result = await _userManager.CreateAsync(newadmin, model.AdminPassword);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(newadmin, "Admin");
                _logger.LogInformation("User created a new account {Email}.", model.AdminEmail);
                /*i need to add super admin to action log admin
                //for the Action log optional 
                var actionlog = new AdminActionLog
                {
                    AdminId = newadmin.Id,
                    ASction = "CreateAdmin",
                    DeTails = $"Created Admin with Email {model.AdminEmail}",
                    ADate = DateTime.Now
                };
                _db.AdminActionLogs.Add(actionlog);
                await _db.SaveChangesAsync();
                */
                TempData["Success"] = "Admin created successfully";
                return RedirectToAction(nameof(AdminList));


            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                    _logger.LogWarning("Error creating an admin : {1}", error.Description);
                }
            }
        }
        return View(model);
    }
    
    /// <summary>
    /// Displays a list of all Admin users.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> AdminList()
    {
        var allUsersInAdminRole = await _userManager.GetUsersInRoleAsync("Admin");
        var admins = allUsersInAdminRole.OfType<Admin>().ToList();
        return View(admins);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAdmin(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest();
        }
        var admin = await _userManager.FindByIdAsync(id);
        if (admin==null)
        {
            return NotFound();
        }
        var result = await _userManager.DeleteAsync(admin);
        if (result.Succeeded)
        {
            _logger.LogInformation("Admin {Email} deleted.", admin.Email);
            TempData["Success"] = "Admin deleted successfully";
        }
        else
        {
            _logger.LogWarning("Error deleting admin with {Email} not deleted.", admin.Email);
            TempData["Error"] = "Admin not deleted";
        }
        return RedirectToAction(nameof(AdminList));
    }
    
    

}