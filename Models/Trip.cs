namespace logirack.Models;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents a trip or request within the LogiTruck system.
/// </summary>

public class Trip
{
    public int Id { get; set; }
    [Required]
    public string CustomerId { get; set; }
    public Customer Customer { get; set; }
    [Required]
    public string AdminId { get; set; }
    public Admin Admin { get; set; }
    [Required]
    [StringLength(100)]
    public string FromCity { get; set; }
    [Required]
    [StringLength(100)]
    public string ToCity { get; set; }
    [Required]
    [StringLength(100)]
    public string FromAddress { get; set; }
    [Required]
    [StringLength(100)]
    public string ToAddress { get; set; }
    [Required]
    public string FromZipCode { get; set; }
    [Required]
    public string ToZipCode { get; set; }
    [Required]
    public double Weight { get; set; }
    [Required]
    public double Volume { get; set; }
    [Required]
    public double Distance { get; set; }
    public string Notes { get; set; }=String.Empty;
    [Required]
    public string GoodsType { get; set; }
    public DateTime PickupTime { get; set; }
    public double EstimatedPrice { get; set; }
    public double AdminPrice { get; set; }
    public bool IsPrAcceptedBeyCustomer  { get; set; }
    public bool IsAcceptedBeyAdmin { get; set; }
    public TripStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DriverTrip DriverTrip { get; set; }
    
}
/// <summary>
/// Represents the status of a trip.
/// </summary>
public enum TripStatus
{
    Requested,
    PriceSet,
    isApproved,
    Assigned,
    InProgress,
    Completed,
    CancelledByAdmin,
    CanceledByCustomer,
}