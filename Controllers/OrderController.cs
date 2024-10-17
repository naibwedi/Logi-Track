using Microsoft.AspNetCore.Mvc;
using logirack.Models;
using System;

namespace logirack.Controllers
{
    public class OrderController : Controller
    {
        [HttpGet("/Customer/CustomerForm")]
        public IActionResult CustomerForm()
        {
            return View(); // This returns the CustomerForm.cshtml view
        }

        [HttpPost]
        public IActionResult SubmitOrder(OrderModel model)
        {
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

                // Redirect to the success view or display success message
                return View("OrderSuccess", model);
            }

            // If validation fails, return the form view with the model
            return View("CustomerForm", model);
        }

        public IActionResult OrderSuccess(OrderModel model)
        {
            return View(model);
        }
    }
}