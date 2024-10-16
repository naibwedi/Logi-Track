using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    public IActionResult Dashboard()
    {
        return View();
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



}