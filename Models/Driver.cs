using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace logirack.Models;
/// <summary>
/// Represents a driver in the LogiTruck system.
/// Inherits from ApplicationUser.
/// </summary>

public class Driver : ApplicationUser
{
    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Please enter a valid price.")]
    public double PricePerKm { get; set; }
    [Required]
    public PaymentFreq PaymentFreq { get; set; }
    public bool IsAvailable { get; set; }
    public List<DriverTrip> DriverTrips { get; set; }
    public List<PaymentPeriod> PaymentPeriods { get; set; }
    public Address PermanentAddress { get; set; } 
    public Location CurrentLocation { get; set; }
    
}