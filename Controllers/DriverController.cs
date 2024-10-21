using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

[Authorize(Roles = "Driver")]
public class DriverController : Controller
{
    private readonly PasswordService _passwordService;
    private readonly ILogger<DriverController> _logger;

    // Constructor to inject PasswordService
    public DriverController(PasswordService passwordService, ILogger<DriverController> logger)
    {
        _passwordService = passwordService;
        _logger = logger;
    }

    public IActionResult Dashboard()
    {
        return View();
    }

    // Example action to demonstrate usage of PasswordService
    public IActionResult GenerateDriverPassword()
    {
        // Use PasswordService to generate a password
        string generatedPassword = _passwordService.GeneratePassword();
        _logger.LogInformation("Generated password for driver: {Password}", generatedPassword);

        ViewBag.GeneratedPassword = generatedPassword; // Pass the generated password to the view
        return View();
    }
}