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
    [StringLength(200)]
    public string FromAddress { get; set; } = string.Empty;
    [Required]
    [StringLength(200)]
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
    [StringLength(1000)]
    public string Notes { get; set; }=String.Empty;
    [Required]
    public string GoodsType { get; set; } = string.Empty;
    [DataType(DataType.DateTime)]
    public DateTime PickupTime { get; set; }
    public double EstimatedPrice { get; set; }
    public double AdminPrice { get; set; }
    public TripStatus Status { get; set; }
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? BrokerId { get; set; }
    public Broker? Broker { get; set; }
    /// <summary>Broker's load / PO / reference number from the rate confirmation (e.g. "23044790").</summary>
    [StringLength(50)]
    public string? BrokerLoadNumber { get; set; }
    /// <summary>Carrier's internal invoice number (e.g. "IN001").</summary>
    [StringLength(20)]
    public string? InvoiceNumber { get; set; }
    // Load accounting (USD). Stored as decimal; aggregate in memory (SQLite cannot SUM decimals).
    [Range(0, 1_000_000)]
    public decimal? RateConfirmationAmount { get; set; }
    [Range(0, 100_000)]
    public decimal DetentionFee { get; set; }
    [Range(0, 100_000)]
    public decimal LumperFee { get; set; }
    [Range(0, 100_000)]
    public decimal OtherAccessorials { get; set; }
    /// <summary>Driver deductions (claims, trailer misuse, broken seal, late fees) as a positive amount.</summary>
    [Range(0, 100_000)]
    public decimal Deductions { get; set; }
    [StringLength(500)]
    public string? DeductionReason { get; set; }
    /// <summary>Truck Order Not Used payout (standard $150-$200) when the load is cancelled after dispatch.</summary>
    [Range(0, 1_000)]
    public decimal? TonuAmount { get; set; }
    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
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
    /// <summary>Truck Order Not Used: carrier dispatched, shipper/broker cancelled; TONU payout applies.</summary>
    Tonu,
}
