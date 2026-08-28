using System.ComponentModel.DataAnnotations;

namespace logirack.Models;

public class PaymentPeriod
{
    public int Id { get; set; }
    //public string DriverID { get; set; }
    //public Driver Driver { get; set; }
    [Required]
    public DateTime StartDate { get; set; }
    [Required]
    public DateTime EndDate { get; set; }
    public PaymentFreq PaymentFreq { get; set; }
    public double Total { get; set; }
    public PaymentStatus Status { get; set; }
    public List<DriverTrip> DriverTrips { get; set; }
    public List<Payment> Payments { get; set; }
    
}
