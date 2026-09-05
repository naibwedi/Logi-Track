namespace logirack.Models;

public class Location
{
    public int ID { get; set; }
    public string DriverID { get; set; } = string.Empty;
    public Driver Driver { get; set; } = null!;
    public string City { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public DateTime UpdatedAt { get; set; }
}
