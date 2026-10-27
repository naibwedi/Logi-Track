namespace logirack.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public enum VehicleStatus
{
    Active,
    Maintenance,
    Decommissioned
}

public class Vehicle
{
    public int Id { get; set; }

    [Required]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "Unit number is required.")]
    [Display(Name = "Unit Number")]
    public string UnitNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(17, MinimumLength = 17, ErrorMessage = "VIN must be exactly 17 characters.")]
    [RegularExpression(@"^[A-HJ-NPR-Z0-9]{17}$", ErrorMessage = "VIN must be a valid 17-character alphanumeric string (letters I, O, Q excluded).")]
    public string Vin { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [Display(Name = "License Plate")]
    public string LicensePlate { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    [Display(Name = "License State")]
    public string LicenseState { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Make { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Model { get; set; } = string.Empty;

    [Required]
    [Range(1990, 2035, ErrorMessage = "Year must be between 1990 and 2035.")]
    public int Year { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Registration Expiration Date")]
    public DateTime RegistrationExpirationDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Annual DOT Inspection Expiration")]
    public DateTime? AnnualDotInspectionExpiration { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "IRS Form 2290 Expiration")]
    public DateTime? Form2290ExpirationDate { get; set; }

    [StringLength(50)]
    [Display(Name = "PrePass Transponder ID")]
    public string? PrePassTransponderId { get; set; }

    [Required]
    public VehicleStatus Status { get; set; } = VehicleStatus.Active;

    public string? AssignedDriverId { get; set; }
    public Driver? AssignedDriver { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
    public ICollection<DriverTrip> DriverTrips { get; set; } = new List<DriverTrip>();
}
