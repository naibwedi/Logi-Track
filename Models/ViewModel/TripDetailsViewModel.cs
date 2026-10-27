namespace logirack.Models.ViewModel;

public class TripDetailsViewModel
{
    public int TripId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string FromCity { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public string FromZipCode { get; set; } = string.Empty;
    public string ToZipCode { get; set; } = string.Empty;
    public double Weight { get; set; }
    public double Volume { get; set; }
    public double Distance { get; set; }
    public string GoodTypes { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    
    public double AdminPrice { get; set; }
    public double EstimatedPrice { get; set; }
    public TripStatus Status { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime PickupDate { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public double DestinationLatitude { get; set; }
    public double DestinationLongitude { get; set; }
    public double CurrentLatitude { get; set; }
    public double CurrentLongitude { get; set; }
}
