namespace logirack.Models;

public class Location
{
    public int ID { get; set; }
    public string DriverID { get; set; }
    public Driver Driver { get; set; }
    public string City { get; set; }
    public string Street { get; set; }
    public string Region { get; set; }
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public DateTime UpdatedAt { get; set; }
}