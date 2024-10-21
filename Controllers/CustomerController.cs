using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using logirack.Models;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using logirack.Data;
using logirack.Models.ViewModel;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

namespace logirack.Controllers
{
    [Authorize(Roles = "Customer")]
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

        [HttpGet]
        public IActionResult SubmitTrip()
        {
            return View();
        }
        

        /// <summary>
        /// Processes the order form submitted by the customer.
        /// </summary>
        [Authorize(Roles = "Customer")]
        [HttpPost]
        public async Task<IActionResult> SubmitTrip(SubmitTripViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || !user.IsApproved)
            {
                _logger.LogWarning("Unauthorized order submission attempt.");
                return RedirectToAction("Login", "Account");
            }

            if (ModelState.IsValid)
            {
                //Evan read this \|/ 
                //TODO we will do a view for this function so the admin can change his charges automatically from the view 
                // Combine PickupDate and PickupTime if needed
                //no need for combine uu can use DateTime directly on the ViewModel 
                //DateTime pickupDate =  model.PickupDate.Date + model.PickupTime;

                // Calculate the cost based on the provided distance, weight, and additional services
                double baseRate = 50;
                double distanceRate = model.Distance * 15; // Example: 15 nok per km
                double weightRate = model.Weight * 20; // Example: 20 Nok per kg
                double serviceCharge = 100;//

                double totalCost = baseRate + distanceRate + weightRate + serviceCharge;
                model.PriceEstimate = totalCost;

                _logger.LogInformation("Order submitted successfully by user {Email} with estimated price {Price}.", user.Email, totalCost);
                //you forgot to create a new trip entity based on the request for that uu Didn't see any trip on the table  see any 
                var trip = new Trip
                {
                    CustomerId = user.Id,
                    AdminId = "place for the admin ho will proceed",
                    FromCity = model.FromCity,
                    ToCity = model.ToCity,
                    FromAddress = model.FromAddress,
                    ToAddress = model.ToAddress,
                    FromZipCode = model.FromZipCode,
                    ToZipCode = model.ToAddress,
                    Weight = model.Weight,
                    Volume = model.Volume,
                    Distance = model.Distance,
                    Notes = model.Notes,
                    EstimatedPrice = model.PriceEstimate,
                    AdminPrice = 0, //wiil be set by Admin
                    Status = TripStatus.Requested,
                    PickupTime = model.PickupDate,
                    GoodsType = model.GoodType,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                //Save on db 
                _db.Trips.Add(trip);
                await _db.SaveChangesAsync();
                //Send email Notification to the customer 
                await _emailSender.SendEmailAsync(user.Email, "Thank you for your order!", $"Dear {user.FirstName} {user.LastName}, <br/>"
                    +"Your order from {model.FromCity} to {model.ToCity}, has been submitted successfully."
                    +"Estimated price is <strong>{model.PriceEstimate} NOK </strong><br/> an admin will review your order and set the final price <br/> Best regards LogiTrack team ") ;
                //Create the Order success View model to redirect to it 
                var successModl = new OrderSuccessViewModel
                {
                    CustomerName = user.FirstName + " " + user.LastName,
                    PickupLocation = trip.FromAddress,
                    DropofLocation = trip.ToAddress,
                    GoodsType = trip.GoodsType,
                    Weight = trip.Weight,
                    PickupDate = trip.PickupTime,
                    Distance = trip.Distance,
                    AdditionalService = trip.Notes,
                    PriceEstimate = trip.EstimatedPrice
                };
                // Redirect to the success view or display success message
                return View("OrderSuccess",successModl);
            }

            _logger.LogWarning("Order submission failed due to invalid model state for user {Email}.", user.Email);
            // If validation fails, return the form view with the model
            return View("CustomerForm", model);
        }

        /// <summary>
        /// Displays the order success page with order details.
        /// </summary>
        [Authorize(Roles = "Customer")]
        public IActionResult OrderSuccess(OrderSuccessViewModel model)
        {
            _logger.LogInformation("Order success page displayed for user with estimated price {Price}.", model.CustomerName ,model.PriceEstimate);
            return View(model);
        }
    }
}
