using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using logirack.Models;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using logirack.Data;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace logirack.Controllers
{
    [Authorize(Roles = "Customer")]
    [ApprovedOnly]
    public class CustomerController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<CustomerController> _logger;
        private readonly IEmailSender _emailSender;//for admin to get email when there's new request from a customer 

        public CustomerController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, 
            ILogger<CustomerController> logger  , IEmailSender emailSender,ApplicationDbContext db)
        {
            _db = db;
            _emailSender = emailSender;
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
        }
        // Home page for the customer
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
        // This action shows the pending approval message
        [HttpGet]
        public IActionResult ApprovalPending()
        {
            return View();
        }

        // Dashboard that requires approval to access
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Dashboard()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("User not found.");
                return RedirectToAction("Login", "Account");
            }

            if (!user.IsApproved)
            {
                _logger.LogInformation("User {Email} is not approved.", user.Email);
                return RedirectToAction("ApprovalPending");
            }

            if (!await _userManager.IsInRoleAsync(user, "Customer"))
            {
                _logger.LogWarning("User {Email} is not assigned the 'Customer' role.", user.Email);
                return Forbid();
            }

            _logger.LogInformation("User {Email} accessed the dashboard.", user.Email);
            return View();
        }

        /// <summary>
        /// Displays the order form for a registered and approved customer.
        /// </summary>
        /*
        [Authorize(Roles = "Customer")]
        [HttpGet("/Customer/CustomerForm")]
        public async Task<IActionResult> CustomerForm()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("User not found.");
                return RedirectToAction("Login", "Account");
            }

            if (!user.IsApproved)
            {
                _logger.LogInformation("User {Email} is not approved.", user.Email);
                TempData["RequireRegistration"] = true;
                return RedirectToAction("Register", "Account");
            }

            if (!await _userManager.IsInRoleAsync(user, "Customer"))
            {
                _logger.LogWarning("User {Email} is not assigned the 'Customer' role.", user.Email);
                return Forbid();
            }

            _logger.LogInformation("User {Email} accessed the order form.", user.Email);
            return View(); // This returns the CustomerForm.cshtml view
        }
        */
        //Displays the trip request form 
        [HttpGet]
        public IActionResult CreateTrip()
        {
            return View();
        }
        //Process teh trip request submission 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTrip(SubmitTripViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var user = await _userManager.GetUserAsync(User);
            //Calculate estimated price 
            double estimatedPrice = CalculateTripPriceEstimate(model);
            _logger.LogInformation("Order submitted successfully by user {Email} with estimated price {Price}.", user.Email, estimatedPrice);
            //you forgot to create a new trip entity based on the request for that uu Didn't see any trip on the table  see any 
            var trip = new Trip
            {
                CustomerId = user.Id,
                AdminId = null,
                FromCity = model.FromCity,
                ToCity = model.ToCity,
                FromAddress = model.FromAddress,
                ToAddress = model.ToAddress,
                FromZipCode = model.FromZipCode,
                ToZipCode = model.ToZipCode,
                Weight = model.Weight,
                Volume = model.Volume,
                Distance = model.Distance,
                Notes = model.Notes,
                GoodsType = model.GoodType,
                PickupTime = model.PickupDate,
                EstimatedPrice = estimatedPrice,
                AdminPrice = 0, //wiil be set by Admin
                Status = TripStatus.Requested,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            //Save on db 
            _db.Trips.Add(trip);
            await _db.SaveChangesAsync();
            return RedirectToAction("OrderSuccess", new { id = trip.Id });
        }

        //Displays the order success page 
        [HttpGet]
        public async Task<IActionResult> OrderSuccess(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var trip = await _db.Trips.FirstOrDefaultAsync(t=>t.Id ==id&&t.CustomerId == user.Id);
            if (trip == null)
            {
                return RedirectToAction("Dashboard");
            }
            return View(trip);
        }
        //Displays the customers trips 
        [HttpGet]
        public async Task<IActionResult> MyTrips()
        {
            var user = await _userManager.GetUserAsync(User);
            var trips = await _db.Trips.Where(t => t.CustomerId == user.Id).ToListAsync();
            return View(trips);
        }
        // Display trip details 
        [HttpGet]
        [Route("/Customer/TripDetails/{id}")]
        public async Task<IActionResult> TripDetails(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);
            if (trip == null)
            {
                return NotFound();
            }
            return View(trip);
        }
        //Handels Customer's response to admin's price 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondToPrice(int id, bool approved)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user==null)
            {
                _logger.LogWarning("User not found.");
                return RedirectToAction("Login", "Account");
            }
            var trip= await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);
            if (trip == null)
            {
                _logger.LogWarning("Trip not found.");
                return NotFound();
            }

            if (trip.Status != TripStatus.PriceSet)
            {
                _logger.LogWarning("Trip price is not set.");
                return BadRequest("cannot respond to this trip at this time");
            }

            if (approved)
            {
                trip.Status = TripStatus.ApprovedByCustomer;
            }
            else
            {
                trip.Status = TripStatus.CanceledByCustomer;
            }
            trip.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            return RedirectToAction("MyTrips");
        }

        //Helper method to calculate estimated price 
        private double CalculateTripPriceEstimate(SubmitTripViewModel model)
        {
            double baseRate = 50;
            double distanceRate = model.Distance * 15; // Example: 15 nok per km
            double weightRate = model.Weight * 20; // Example: 20 Nok per kg
            double serviceCharge = 100;//

            return baseRate + distanceRate + weightRate + serviceCharge;
        }
        /// <summary>
        /// Displays the order success page with order details
        /// </summary>
     
    }
}
