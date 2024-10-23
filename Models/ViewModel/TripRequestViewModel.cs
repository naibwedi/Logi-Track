namespace logirack.Models.ViewModel;

public class TripRequestViewModel
{
    public int TripId { get; set; }
    public string CustomerName { get; set; }
    public string FromCity { get; set; }
    public string ToCity { get; set; }
    public double Weight { get; set; }
    public double Distance { get; set; }
    public double EstimatedPrice { get; set; }
    public TripStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime PickUpAt { get; set; }
}