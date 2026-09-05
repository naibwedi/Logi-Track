using System.ComponentModel.DataAnnotations;

namespace logirack.Models.ViewModel;

public class CreateTripByAdminViewModel
{
    [Required(ErrorMessage = "Please select a customer")]
    public string CustomerId { get; set; } = string.Empty;
    [Required(ErrorMessage = "Please select a driver")]
    public string DriverId { get; set; } = string.Empty;
    [Required(ErrorMessage = "From city is required")]
    public string FromCity { get; set; } = string.Empty;
    [Required(ErrorMessage = "To city is required")]
    public string ToCity { get; set; } = string.Empty;
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
    [Required, StringLength(100)] public string FromAddress { get; set; } = string.Empty;
    [Required, StringLength(10, MinimumLength = 4)] public string FromZipCode { get; set; } = string.Empty;
    [Required, StringLength(100)] public string ToAddress { get; set; } = string.Empty;
    [Required, StringLength(10, MinimumLength = 4)] public string ToZipCode { get; set; } = string.Empty;
    [Required, StringLength(100)] public string GoodsType { get; set; } = string.Empty;
    [Required]  
    [Range(100, double.MaxValue, ErrorMessage = "Price must be greater than 100nok")]
    public double Price { get; set; }
}
