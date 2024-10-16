﻿using Microsoft.Build.Framework;

namespace logirack.Models;

public class Driver : ApplicationUser
{
    public double PricePerKm { get; set; }
    [Required]
    public PaymentFreq PaymentFreq { get; set; }
    public bool IsAvailable { get; set; }
    public List<DriverTrip> DriverTrips { get; set; }
    public List<PaymentPeriod> PaymentPeriods { get; set; }
    public Address PermanentAddress { get; set; } 
    public Location CurrentLocation { get; set; }
    
}