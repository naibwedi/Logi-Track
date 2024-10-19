using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using logirack.Models;
using Microsoft.AspNetCore.Authorization;

[Authorize]
public class CustomerController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CustomerController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    // This action shows the pending approval message
    [HttpGet]
    public IActionResult ApprovalPending()
    {
        return View();
    }

    // Dashboard that requires approval to access
    [Authorize(Roles = "Customer")]
    public IActionResult Dashboard()
    {
        var user = _userManager.GetUserAsync(User).Result;

        if (!user.IsApproved)
        {
            return RedirectToAction("ApprovalPending");
        }

        return View();
    }
}