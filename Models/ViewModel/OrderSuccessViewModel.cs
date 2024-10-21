namespace logirack.Models.ViewModel;
using System.ComponentModel.DataAnnotations;


public class OrderSuccessViewModel
{
    public string CustomerName { get; set; }

    
    [Required]
    [StringLength(100)]
    [Display(Name = "To city")]
    public string ToCity { get; set; }
    
    [Required]
    [StringLength(100)]
    [Display(Name = "From Address")]
    public string PickupLocation { get; set; }
    
    [Required]
    [StringLength(100)]
    [Display(Name = "Goods  types")]
    public string GoodsType { get; set; }
    
    [Required]
    [StringLength(100)]
    [Display(Name = "To Address")]
    public string DropofLocation { get; set; }
    
    [Required]
    [Range(10, double.MaxValue, ErrorMessage ="Weight must be greater than 10kg")]
    [Display(Name = "Weight in Kg")]
    public double Weight { get; set; }

    [Required]
    [Range(0.1, double.MaxValue, ErrorMessage ="Distance must be greater than 0,1km")]
    [Display(Name = "Distance in Km")]
    public double Distance { get; set; }
    
    [Display(Name = "Aditional Service")]
    public string AdditionalService { get; set; }
    [Required]
    [DataType(DataType.DateTime)]
    public DateTime PickupDate { get; set; }
    
    public double PriceEstimate { get; set; }

}