using System.ComponentModel.DataAnnotations;

namespace logirack.Models;

public class DriverTrip
{
    public int Id { get; set; }
    [Required]
    public int TripId { get; set; }
    [Required]
    public Trip Trip { get; set; }
    [Required]
    public string DriverId { get; set; }
    [Required]
    public Driver Driver { get; set; }
    [Required]
    public string AssignedByAdminId { get; set; }
    [Required]

    public Admin Admin { get; set; }
    
    public double  DriverPayment { get; set; }
    //[Required] remove comment whene create a payment managmenet 
    public int? PaymentPeriodId { get; set; }
    //[Required]
    public PaymentPeriod? PaymentPeriod { get; set; }
    public TripStatus Status { get; set; }
    public DateTime? CompletionDate { get; set; }
    public DateTime AssignmentDate { get; set; }
    public DateTime UpdateAt { get; set; }
}