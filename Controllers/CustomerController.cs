using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using logirack.Models;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace logirack.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CustomerController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<CustomerController> _logger;

        public CustomerController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ILogger<CustomerController> logger)
        {
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

        /// <summary>
        /// Processes the order form submitted by the customer.
        /// </summary>
        [Authorize(Roles = "Customer")]
        [HttpPost]
        public async Task<IActionResult> SubmitOrder(OrderModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || !user.IsApproved)
            {
                _logger.LogWarning("Unauthorized order submission attempt.");
                return RedirectToAction("Login", "Account");
            }

            if (ModelState.IsValid)
            {
                // Combine PickupDate and PickupTime if needed
                DateTime fullPickupDateTime = model.PickupDate.Add(model.PickupTime);

                // Calculate the cost based on the provided distance, weight, and additional services
                double baseRate = 50;
                double distanceRate = model.Distance * 1.5; // Example: $1.5 per km
                double weightRate = model.Weight * 2; // Example: $2 per kg
                double serviceCharge = (model.AdditionalServices == "expedited") ? 30 :
                    (model.AdditionalServices == "insurance") ? 20 : 0;

                double totalCost = baseRate + distanceRate + weightRate + serviceCharge;
                model.PriceEstimate = totalCost;

                _logger.LogInformation("Order submitted successfully by user {Email} with estimated price {Price}.", user.Email, totalCost);

                // Redirect to the success view or display success message
                return View("OrderSuccess", model);
            }

            _logger.LogWarning("Order submission failed due to invalid model state for user {Email}.", user.Email);
            // If validation fails, return the form view with the model
            return View("CustomerForm", model);
        }

        /// <summary>
        /// Displays the order success page with order details.
        /// </summary>
        [Authorize(Roles = "Customer")]
        public IActionResult OrderSuccess(OrderModel model)
        {
            _logger.LogInformation("Order success page displayed for user with estimated price {Price}.", model.PriceEstimate);
            return View(model);
        }
    }
}
