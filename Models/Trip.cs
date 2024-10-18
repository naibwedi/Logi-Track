namespace logirack.Models;


public class Trip
{
    public int Id { get; set; }
    public string CustomerId { get; set; }
    public Customer Customer { get; set; }
    public string AdminId { get; set; }
    public Admin Admin { get; set; }
    public string FromCity { get; set; }
    public string ToCity { get; set; }
    public string FromAddress { get; set; }
    public string ToAddress { get; set; }
    public string FromZipCode { get; set; }
    public string ToZipCode { get; set; }
    public double Weight { get; set; }
    public double Volume { get; set; }
    public double Distance { get; set; }
    public string Notes { get; set; }
    public double AdminPrice { get; set; }
    public bool IsPrAcceptedBeyCustomer  { get; set; }
    public bool IsAcceptedBeyAdmin { get; set; }
    public TripStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DriverTrip DriverTrip { get; set; }
    
}
