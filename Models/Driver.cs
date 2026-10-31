using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
    public string DriverEmail { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    [StringLength(30)]
    [Display(Name = "CDL Number")]
    public string? CdlNumber { get; set; }
    [StringLength(2)]
    [Display(Name = "CDL State")]
    public string? CdlState { get; set; }
    [DataType(DataType.Date)]
    [Display(Name = "CDL Expiration")]
    public DateTime? CdlExpirationDate { get; set; }
    /// <summary>DOT medical examiner's certificate expiration (49 CFR 391.41). Expired = may not be dispatched.</summary>
    [DataType(DataType.Date)]
    [Display(Name = "DOT Medical Card Expiration")]
    public DateTime? MedicalCardExpirationDate { get; set; }

    [NotMapped]
    public int? AssignedVehicleId
    {
        get => VehicleId;
        set => VehicleId = value;
    }

    [NotMapped]
    public Vehicle? AssignedVehicle
    {
        get => Vehicle;
        set => Vehicle = value;
    }

    public List<DriverTrip> DriverTrips { get; set; } = [];
    public List<PaymentPeriod> PaymentPeriods { get; set; } = [];
    public Address PermanentAddress { get; set; } = null!;
    public Location CurrentLocation { get; set; } = null!;
    public IEnumerable<Payment>? Payments { get; set; }
}
