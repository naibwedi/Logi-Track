using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace logirack.Models.ViewModel;

public class AssignTripViewModel
{
    public int TripId { get; set; }
    public string FromCity { get; set; }
    public string ToCity { get; set; }
    public double Distance { get; set; }
    public double Weight { get; set; }
    
    [Required(ErrorMessage = "please select a driver")]
    public string DriverId { get; set; }
    
    public List<Driver> AvailableDrivers { get; set; }
    
}