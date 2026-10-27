using System.ComponentModel.DataAnnotations;

namespace logirack.Models;

public class DriverTrip
{
    public int Id { get; set; }
    [Required]
    public int TripId { get; set; }
    [Required]
    public Trip Trip { get; set; } = null!;
    [Required]
    public string DriverId { get; set; } = string.Empty;
    [Required]
    public Driver Driver { get; set; } = null!;
    [Required]
    public string AssignedByAdminId { get; set; } = string.Empty;
    [Required]

    public Admin Admin { get; set; } = null!;
    
    public double  DriverPayment { get; set; }
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    //[Required] remove comment whene create a payment managmenet 
    public int? PaymentPeriodId { get; set; }
    //[Required]
    public PaymentPeriod? PaymentPeriod { get; set; }
    public TripStatus Status { get; set; }
    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime? CompletionDate { get; set; }
    public DateTime AssignmentDate { get; set; }
    public DateTime UpdateAt { get; set; }
}
