using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize(Roles = "Driver")]
public class DriverController : Controller
{
    public IActionResult Dashboard()
    {
        return View();
    }
}