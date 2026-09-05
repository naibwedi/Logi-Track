using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace logirack.Models.ViewModel;

public class AssignTripViewModel
{
    public int TripId { get; set; }
    public string FromCity { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public double Distance { get; set; }
    public double Weight { get; set; }
    
    [Required(ErrorMessage = "please select a driver")]
    public string DriverId { get; set; } = string.Empty;

    public List<Driver> AvailableDrivers { get; set; } = new List<Driver>();

}
