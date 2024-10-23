namespace logirack.Models.ViewModel;

public class TripDetailsViewModel
{
    public int TripId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string CustomerPhone { get; set; }
    public string FromCity { get; set; }
    public string ToCity { get; set; }
    public string FromAddress { get; set; }
    public string ToAddress { get; set; }
    public string FromZipCode { get; set; }
    public string ToZipCode { get; set; }
    public double Weight { get; set; }
    public double Volume { get; set; }
    public double Distance { get; set; }
    public string GoodTypes { get; set; }
    public string Notes { get; set; }
    public double EstimatedPrice { get; set; }
    public TripStatus Status { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime PickupDate { get; set; }
}