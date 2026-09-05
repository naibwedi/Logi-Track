namespace logirack.Models;

public class Address
{
    public int Id { get; set; }
    public string DriverId { get; set; } = string.Empty;
    public Driver Driver { get; set; } = null!;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CodePostal { get; set; } = string.Empty;
}
