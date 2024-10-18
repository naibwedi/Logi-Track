namespace logirack.Models;

public class DriverTrip
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public Trip Trip { get; set; }
    public string DriverId { get; set; }
    public Driver Driver { get; set; }
    public string AssignedByAdminId { get; set; }
    public Admin Admin { get; set; }
    public double  DriverPayment { get; set; }
    public int? PaymentPeriodId { get; set; }
    public PaymentPeriod PaymentPeriod { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletionDate { get; set; }
    public DateTime AssignmentDate { get; set; }
    public DateTime UpdateAt { get; set; }
}