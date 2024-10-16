namespace logirack.Models;

public class Driver : ApplicationUser
{
    public decimal PricePerKm { get; set; }
    public PaymentFreq PaymentFreq { get; set; }
    public bool IsAvailable { get; set; }
    public List<DriverTrip> DriverTrips { get; set; }
    public List<PaymentPeriod> PaymentPeriods { get; set; }
    public Address PermanentAddress { get; set; } 
    public Location CurrentLocation { get; set; }
    
}