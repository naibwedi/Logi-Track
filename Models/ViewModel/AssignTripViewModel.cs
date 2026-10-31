using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace logirack.Models.ViewModel;

public class AssignTripViewModel
{
    public int TripId { get; set; }
    public string FromCity { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public double Distance { get; set; }
    public double Weight { get; set; }
    
    [Required(ErrorMessage = "please select a driver")]
    public string DriverId { get; set; } = string.Empty;

    public List<Driver> AvailableDrivers { get; set; } = new List<Driver>();

    [Display(Name = "Tractor Unit")]
    public int? VehicleId { get; set; }

    public List<Vehicle> AvailableVehicles { get; set; } = new List<Vehicle>();

    [Display(Name = "Freight Broker")]
    public int? BrokerId { get; set; }

    public List<Broker> AvailableBrokers { get; set; } = new List<Broker>();

    [StringLength(30)]
    [Display(Name = "Internal Invoice # (e.g. IN001)")]
    public string? InvoiceNumber { get; set; }

    [StringLength(50)]
    [Display(Name = "Broker Load #")]
    public string? BrokerLoadNumber { get; set; }

    [Range(0, 1000000)]
    [Display(Name = "Rate Confirmation Amount ($)")]
    public decimal? RateConfirmationAmount { get; set; }

    [Range(0, 100000)]
    [Display(Name = "Detention Fee ($)")]
    public decimal DetentionFee { get; set; }

    [Range(0, 100000)]
    [Display(Name = "Lumper Fee ($)")]
    public decimal LumperFee { get; set; }

    [Range(0, 100000)]
    [Display(Name = "Other Accessorials ($)")]
    public decimal OtherAccessorials { get; set; }

    [Range(0, 100000)]
    [Display(Name = "Driver Deductions ($)")]
    public decimal Deductions { get; set; }

    [StringLength(250)]
    [Display(Name = "Deduction Reason")]
    public string? DeductionReason { get; set; }
}
