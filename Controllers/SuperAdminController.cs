using logirack.Models;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace logirack.Controllers;

/// <summary>
/// Controller for SuperAdmin actions, including creating and managing Admin users.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SuperAdminController> _logger;
    private readonly IEmailSender _emailSender; 

    /// <summary>
    /// Initializes a new instance of the <see cref="SuperAdminController"/> class.
    /// </summary>
    public SuperAdminController(UserManager<ApplicationUser> userManager, ILogger<SuperAdminController> logger, IEmailSender emailSender)
    {
        _emailSender = emailSender;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Dashboard()
    {
        return View("Dashboard");
    }

    /// <summary>
    /// Gets the form to create a new Admin
    /// </summary>
    /// <returns>The create admin view</returns>
    /// <response code="200">Returns the create form view</response>
    [HttpGet]
    public IActionResult CreateAdmin()
    {
        return View();
    }

    /// <summary>
    /// Creates a new Admin user
    /// </summary>
    /// <param name="model">The admin creation data</param>
    /// <returns>Redirects to admin list on success</returns>
    /// <response code="200">If admin is created successfully</response>
    /// <response code="400">If the model is invalid or email/phone exists</response>
    [HttpPost]
    public async Task<IActionResult> CreateAdmin(CreateAdminViewModel model)
    {
        if (ModelState.IsValid)
        {
            // Check if the email is already taken
            var eUser = await _userManager.FindByEmailAsync(model.AdminEmail);
            if (eUser != null)
            {
                _logger.LogInformation($"User {eUser.Email} has already been added.");
                ModelState.AddModelError(nameof(CreateAdminViewModel.AdminEmail), "This email already exists.");
                return View(model);
            }
            // Check if the phone number is already taken
            var pUser = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == model.PhoneNumber);
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
                LastName = model.LastName,
                DateOfBirth = model.DateOfBirth
            };

            var result = await _userManager.CreateAsync(newadmin, model.AdminPassword);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(newadmin, "Admin");
                _logger.LogInformation("User created a new account {Email}.", model.AdminEmail);
                try
                {
                    await _emailSender.SendEmailAsync(
                        model.AdminEmail,
                        "Welcome to Logirack Admin Team",
                        $"Dear {model.FirstName} {model.LastName},\n\n" +
                        $"Welcome to the Logirack admin team! Your admin account has been created successfully.\n\n" +
                        $"Your login credentials:\n" +
                        $"Email: {model.AdminEmail}\n" +
                        $"Password: {model.AdminPassword}\n\n" +
                        $"Please login at: [Your Login URL]\n\n" +
                        $"For security reasons, we recommend changing your password after your first login.\n\n" +
                        $"Account Details:\n" +
                        $"Role: Admin\n" +
                        $"Phone: {model.PhoneNumber}\n" +
                        $"Created on: {DateTime.Now}\n\n" +
                        $"If you have any questions or need assistance, please contact the SuperAdmin.\n\n" +
                        $"Best regards,\n" +
                        $"Logirack Team"
                    );
                    _logger.LogInformation("Welcome email sent to new admin {Email}", model.AdminEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send welcome email to new admin {Email}", model.AdminEmail);
                    // Continue execution even if email fails
                }

                TempData["Success"] = "Admin created successfully";
                return RedirectToAction(nameof(AdminList));
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                    _logger.LogWarning("Error creating an admin: {1}", error.Description);
                }
            }
        }
        return View(model);
    }

    /// <summary>
    /// Gets an admin's details for editing
    /// </summary>
    /// <param name="id">The ID of the admin to edit</param>
    /// <returns>The edit admin view</returns>
    /// <response code="200">Returns the edit form view</response>
    /// <response code="404">If admin is not found</response>
    /// <response code="400">If id is null or empty</response>
    [HttpGet]
    public async Task<IActionResult> EditAdmin(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest();
        }

        var admin = await _userManager.FindByIdAsync(id);
        if (admin == null)
        {
            return NotFound();
        }

        var model = new EditAdminViewModel
        {
            Id = admin.Id,
            FirstName = admin.FirstName,
            LastName = admin.LastName,
            AdminEmail = admin.Email,
            PhoneNumber = admin.PhoneNumber,
            DateOfBirth = admin.DateOfBirth
        };

        return View(model);
    }

    /// <summary>
    /// Updates an existing admin's information
    /// </summary>
    /// <param name="model">The updated admin data</param>
    /// <returns>Redirects to admin list on success</returns>
    /// <response code="200">If admin is updated successfully</response>
    /// <response code="404">If admin is not found</response>
    /// <response code="400">If the model is invalid</response>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAdmin(EditAdminViewModel model)
    {
        if (ModelState.IsValid)
        {
            var admin = await _userManager.FindByIdAsync(model.Id);
            if (admin == null)
            {
                return NotFound();
            }

            admin.FirstName = model.FirstName;
            admin.LastName = model.LastName;
            admin.PhoneNumber = model.PhoneNumber;
            admin.DateOfBirth = model.DateOfBirth;

            var result = await _userManager.UpdateAsync(admin);
            if (result.Succeeded)
            {
                _logger.LogInformation("Admin details updated successfully for {Email}.", admin.Email);
                TempData["Success"] = "Admin details updated successfully.";
                return RedirectToAction(nameof(AdminList));
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                    _logger.LogWarning("Error updating admin: {1}", error.Description);
                }
            }
        }

        return View(model);
    }

    /// <summary>
    /// Gets a list of all admin users
    /// </summary>
    /// <returns>View with list of all admins</returns>
    /// <response code="200">Returns the list of admins</response>
    [HttpGet]
    public async Task<IActionResult> AdminList()
    {
        var allUsersInAdminRole = await _userManager.GetUsersInRoleAsync("Admin");
        var admins = allUsersInAdminRole.OfType<Admin>().ToList();
        return View(admins);
    }

    /// <summary>
    /// Deletes an admin user
    /// </summary>
    /// <param name="id">The ID of the admin to delete</param>
    /// <returns>Redirects to admin list</returns>
    /// <response code="200">If admin is deleted successfully</response>
    /// <response code="404">If admin is not found</response>
    /// <response code="400">If id is null or empty</response>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAdmin(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest();
        }
        var admin = await _userManager.FindByIdAsync(id);
        if (admin == null)
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
