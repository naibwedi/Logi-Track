using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using logirack.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace logirack.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IEmailSender _emailSender;
    private readonly string _contactRecipient;
    private readonly bool _emailConfigured;

    public HomeController(ILogger<HomeController> logger, IEmailSender emailSender, IConfiguration configuration)
    {
        _logger = logger;
        _emailSender = emailSender;
        _contactRecipient = configuration["Contact:Recipient"] ?? string.Empty;
        _emailConfigured = !string.IsNullOrWhiteSpace(configuration["SendGrid:ApiKey"])
            && !string.IsNullOrWhiteSpace(configuration["SendGrid:FromEmail"])
            && !string.IsNullOrWhiteSpace(_contactRecipient);
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
    [EnableRateLimiting("contact")]
    public async Task<IActionResult> Contact(string email, string message)
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

        if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment() && !_emailConfigured)
        {
            ModelState.AddModelError(string.Empty, "Contact email is temporarily unavailable. Please try again later.");
            ViewData["Email"] = email;
            ViewData["Message"] = message;
            return View();
        }

        try
        {
            if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            {
                await _emailSender.SendEmailAsync(
                    _contactRecipient,
                    $"LogiTrack contact request from {email}",
                    $"Reply-to email: {System.Net.WebUtility.HtmlEncode(email)}<br><br>{System.Net.WebUtility.HtmlEncode(message).Replace("\n", "<br>")}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not deliver contact request from {Email}", email);
            ModelState.AddModelError(string.Empty, "We could not deliver your message. Please try again later.");
            ViewData["Email"] = email;
            ViewData["Message"] = message;
            return View();
        }

        _logger.LogInformation("Contact request accepted from {Email}: {MessageLength} characters", email, message.Length);
        TempData["ContactSuccess"] = "Thanks. Your message has been received.";
        return RedirectToAction(nameof(Contact));
    }

    [AcceptVerbs("GET", "POST")]
    public IActionResult RequestError(int code)
    {
        Response.StatusCode = code;
        ViewData["StatusCode"] = code;
        return View("StatusCode");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
