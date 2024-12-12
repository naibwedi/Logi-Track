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
        private readonly ILogger<CustomerController> _logger;
        private readonly IEmailSender _emailSender; 

        public CustomerController(UserManager<ApplicationUser> userManager, 
            ILogger<CustomerController> logger, IEmailSender emailSender, ApplicationDbContext db)
        {
            _db = db;
            _emailSender = emailSender;
            _userManager = userManager;
            _logger = logger;
        }

        // Home page for the customer
        /// <summary>
        /// Displays the landing page
        /// </summary>
        /// <returns>The index view</returns>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        // This action shows the pending approval message
        /// <summary>
        /// Shows pending approval message for unapproved customers
        /// </summary>
        /// <returns>The approval pending view</returns>
        [HttpGet]
        public IActionResult ApprovalPending()
        {
            return View();
        }

        /// <summary>
        /// Displays the customer dashboard
        /// </summary>
        /// <returns>The dashboard view or redirects based on user status</returns>
        /// <response code="200">Returns the dashboard view</response>
        /// <response code="302">Redirects to login if user not found</response>
        /// <response code="403">If user is not authorized</response>
        [HttpGet]
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
        /// Displays the trip creation form
        /// </summary>
        /// <returns>The trip creation view</returns>
        [HttpGet]
        public IActionResult CreateTrip()
        {
            return View();
        }

        /// <summary>
        /// Processes a new trip request
        /// </summary>
        /// <param name="model">The trip request details</param>
        /// <returns>Redirects to success page on completion</returns>
        /// <response code="200">Returns success view with created trip</response>
        /// <response code="400">If the model is invalid</response>
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
            _logger.LogInformation("Order submitted successfully by user {Email} with estimated price {Price}.",
                user.Email, estimatedPrice);
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
            try
            {
                var admins = await _userManager.GetUsersInRoleAsync("Admin");
                foreach (var admin in admins)
                {
                    await _emailSender.SendEmailAsync(
                        admin.Email,
                        "New Trip Request",
                        $"A new trip request has been submitted.\n\n" +
                        $"Trip Details:\n" +
                        $"Trip ID: {trip.Id}\n" +
                        $"Customer: {user.FirstName} {user.LastName}\n" +
                        $"From: {trip.FromCity}\n" +
                        $"To: {trip.ToCity}\n" +
                        $"Weight: {trip.Weight}kg\n" +
                        $"Distance: {trip.Distance}km\n" +
                        $"Pickup Time: {trip.PickupTime}\n" +
                        $"Estimated Price: {trip.EstimatedPrice:C}\n" +
                        $"\nPlease review this request in the admin dashboard."
                    );
                }
                _logger.LogInformation("New trip notification sent for trip {TripId}", trip.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send new trip notification emails ");
            }
            return RedirectToAction("OrderSuccess", new { id = trip.Id });
        }

        /// <summary>
        /// Displays the order success page
        /// </summary>
        /// <param name="id">The ID of the created trip</param>
        /// <returns>Success view with trip details</returns>
        /// <response code="200">Returns the success view</response>
        /// <response code="404">If trip is not found</response>
        [HttpGet]
        public async Task<IActionResult> OrderSuccess(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);
            if (trip == null)
            {
                return RedirectToAction("Dashboard");
            }

            return View(trip);
        }

        /// <summary>
        /// Retrieves all trips for the current customer
        /// </summary>
        /// <returns>View with list of customer's trips</returns>
        /// <response code="200">Returns list of trips</response>
        [HttpGet]
        public async Task<IActionResult> MyTrips()
        {
            var user = await _userManager.GetUserAsync(User);
            var trips = await _db.Trips.Where(t => t.CustomerId == user.Id).ToListAsync();
            return View(trips);
        }

        /// <summary>
        /// Gets detailed information about a specific trip
        /// </summary>
        /// <param name="id">The trip ID</param>
        /// <returns>Detailed view of the trip</returns>
        /// <response code="200">Returns trip details</response>
        /// <response code="404">If trip is not found</response>
        [HttpGet]
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
        /// <summary>
        /// Handles customer's response to admin's price setting
        /// </summary>
        /// <param name="id">The trip ID</param>
        /// <param name="approved">Whether the price is approved</param>
        /// <returns>Redirects to trips list</returns>
        /// <response code="200">If response is processed successfully</response>
        /// <response code="400">If trip is not in correct state</response>
        /// <response code="404">If trip is not found</response>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondToPrice(int id, bool approved)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("User not found.");
                return RedirectToAction("Login", "Account");
            }

            var trip = await _db.Trips
                .Include(t => t.Admin)
                .FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);
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
                if (trip.Admin != null)
                {
                    try 
                    {
                        await _emailSender.SendEmailAsync(
                            trip.Admin.Email,
                            "Trip Price Approved",
                            $"Customer {user.FirstName} {user.LastName} has approved the price for trip ID: {trip.Id}.\n\n" +
                            $"Trip Details:\n" +
                            $"From: {trip.FromCity}\n" +
                            $"To: {trip.ToCity}\n" +
                            $"Price: {trip.AdminPrice}\n" +
                            $"Status: Approved by Customer"
                        );
                        _logger.LogInformation("Price approval email sent for trip {TripId}", trip.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send approval email for trip {TripId}", trip.Id);
                    }
                }
            }
            else
            {
                trip.Status = TripStatus.CanceledByCustomer;
                if (trip.Admin != null)
                {
                    try 
                    {
                        await _emailSender.SendEmailAsync(
                            trip.Admin.Email,
                            "Trip Price Rejected",
                            $"Customer {user.FirstName} {user.LastName} has rejected the price for trip ID: {trip.Id}.\n\n" +
                            $"Trip Details:\n" +
                            $"From: {trip.FromCity}\n" +
                            $"To: {trip.ToCity}\n" +
                            $"Price: {trip.AdminPrice}\n" +
                            $"Status: Rejected by Customer"
                        );
                        _logger.LogInformation("Price rejection email sent for trip {TripId}", trip.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "faild to send rejection email for trip {TripId}", trip.Id);
                    }
                }
            }

            trip.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            return RedirectToAction("MyTrips");
        }

        private double CalculateTripPriceEstimate(SubmitTripViewModel model)
        {
            double baseRate = 50;
            double distanceRate = model.Distance * 15; // Example: 15 nok per km
            double weightRate = model.Weight * 20; // Example: 20 Nok per kg
            double serviceCharge = 100; //

            return baseRate + distanceRate + weightRate + serviceCharge;
        }
    }
}
