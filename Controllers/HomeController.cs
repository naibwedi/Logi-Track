using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using logirack.Models;

namespace logirack.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Services() => View();

    [HttpGet]
    public IActionResult Contact() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Contact(string email, string message)
    {
        if (string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            ModelState.AddModelError("email", "Enter a valid email address.");
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length < 10)
            ModelState.AddModelError("message", "Message must contain at least 10 characters.");

        if (!ModelState.IsValid)
        {
            ViewData["Email"] = email;
            ViewData["Message"] = message;
            return View();
        }

        _logger.LogInformation("Contact request received from {Email}: {MessageLength} characters", email, message.Length);
        TempData["ContactSuccess"] = "Thanks. Your message has been received.";
        return RedirectToAction(nameof(Contact));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
