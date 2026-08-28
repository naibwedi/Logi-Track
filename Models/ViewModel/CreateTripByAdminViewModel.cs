using System.ComponentModel.DataAnnotations;

namespace logirack.Models.ViewModel;

public class CreateTripByAdminViewModel
{
    [Required(ErrorMessage = "Please select a customer")]
    public string CustomerId { get; set; }
    [Required(ErrorMessage = "Please select a driver")]
    public string DriverId { get; set; }
    [Required(ErrorMessage = "From city is required")]
    public string FromCity { get; set; }
    [Required(ErrorMessage = "To city is required")]
    public string ToCity { get; set; }
    [Required(ErrorMessage = "Distance is required")]
    [Range(1, double.MaxValue, ErrorMessage = "Distance must be greater than 0")]
    public double Distance { get; set; }
    [Required(ErrorMessage = "Weight is required")]
    [Range(0.1, double.MaxValue, ErrorMessage = "Weight must be greater than 0")]
    public double weight { get; set; }
    [Required(ErrorMessage = "Pickup time is required")]
    public DateTime PickUpTime { get; set; }
    
    public List<Customer> Customers { get; set; } = new List<Customer>();
    public List<Driver> Drivers { get; set; } = new List<Driver>();
    public string FromAddress { get; set; }
    public string FromZipCode { get; set; }
    public string ToAddress { get; set; }
    public string ToZipCode { get; set; }
    public string GoodsType { get; set; }
    [Required]  
    [Range(100, double.MaxValue, ErrorMessage = "Price must be greater than 100nok")]
    public double Price { get; set; }
}