namespace logirack.Models;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents a trip or request within the LogiTruck system.
/// </summary>

public class Trip
{
    public int Id { get; set; }
    [Required]
    public string CustomerId { get; set; } = string.Empty;
    public Customer Customer { get; set; } = null!;
    //[Required]
    public string? AdminId { get; set; }
    public Admin? Admin { get; set; }
    [Required]
    [StringLength(100)]
    public string FromCity { get; set; } = string.Empty;
    [Required]
    [StringLength(100)]
    public string ToCity { get; set; } = string.Empty;
    [Required]
    [StringLength(100)]
    public string FromAddress { get; set; } = string.Empty;
    [Required]
    [StringLength(100)]
    public string ToAddress { get; set; } = string.Empty;
    [Required]
    public string FromZipCode { get; set; } = string.Empty;
    [Required]
    public string ToZipCode { get; set; } = string.Empty;
    [Required]
    public double Weight { get; set; }
    [Required]
    public double Volume { get; set; }
    [Required]
    public double Distance { get; set; }
    public string Notes { get; set; }=String.Empty;
    [Required]
    public string GoodsType { get; set; } = string.Empty;
    [DataType(DataType.DateTime)]
    public DateTime PickupTime { get; set; }
    public double EstimatedPrice { get; set; }
    public double AdminPrice { get; set; }
    public TripStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DriverTrip? DriverTrip { get; set; }
    public IEnumerable<RecentActivity>? RecentActivities { get; set; }
}
/// <summary>
/// Represents the status of a trip.
/// </summary>
public enum TripStatus
{
    Requested,
    ApprovedByAdmin,
    PriceSet,
    ApprovedByCustomer,
    Assigned,
    InProgress,
    Completed,
    CancelledByAdmin,
    CanceledByCustomer,
}
