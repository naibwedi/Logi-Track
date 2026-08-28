using System.ComponentModel.DataAnnotations;

namespace logirack.Models.ViewModel;
/// <summary>
/// ViewModel for customers to submit a new trip order.
/// </summary>
public class SubmitTripViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "From city")]
    public string FromCity { get; set; }=string.Empty;
    
    [Required]
    [StringLength(100)]
    [Display(Name = "To city")]
    public string ToCity { get; set; }=string.Empty;
    
    [Required]
    [StringLength(100)]
    [Display(Name = "From Address")]
    public string FromAddress { get; set; }=string.Empty;
    
    [Required]
    [StringLength(100)]
    [Display(Name = "Goods  types")]
    public string GoodType { get; set; }=string.Empty;
    
    [Required]
    [StringLength(100)]
    [Display(Name = "To Address")]
    public string ToAddress { get; set; }=string.Empty;
    
    [Required]
    [StringLength(10 ,MinimumLength = 4 , ErrorMessage ="You must provide at least 4 characters long")]
    [Display(Name = "From Zip Code")]
    public string FromZipCode { get; set; }=string.Empty;

    [Required]
    [StringLength(10 ,MinimumLength = 4 , ErrorMessage ="You must provide at least 4 characters long")]
    [Display(Name = "To Zip Code")]
    public string ToZipCode { get; set; }=string.Empty;
    [Required]
    [Range(10, double.MaxValue, ErrorMessage ="Weight must be greater than 10kg")]
    [Display(Name = "Weight in Kg")]
    public double Weight { get; set; }

    [Required]
    [Range(0.1, double.MaxValue, ErrorMessage ="Distance must be greater than 0,1km")]
    [Display(Name = "Distance in Km")]
    public double Distance { get; set; }
    
    [Range(0.1, double.MaxValue, ErrorMessage ="Distance must be greater than 0,1km")]
    [Display(Name = "Volum in m^3")]
    public double Volume { get; set; }
    [Display(Name = "Aditional Notes")]
    public string Notes { get; set; }=string.Empty;
    [Required]
    [DataType(DataType.DateTime)]
    public DateTime PickupDate { get; set; }
    
    public double PriceEstimate { get; set; }

}