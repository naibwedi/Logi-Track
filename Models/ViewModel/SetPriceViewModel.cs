using System.ComponentModel.DataAnnotations;

namespace logirack.Models.ViewModel;

public class SetPriceViewModel
{
    public int TripId { get; set; }
    public string CustomerName { get; set; }
    public string FromCity { get; set; }
    public string ToCity { get; set; }
    public double Distance { get; set; }
    public double Weight { get; set; }
    public double EstimatedPrice { get; set; }
    [Required]
    [Display(Name = "Final price (Nok)")]
    [Range(0,double.MaxValue,ErrorMessage = "Price mute be positive number")]
    [DataType(DataType.Currency)]
    public double AdminPrice { get; set; }
    [Required]
    [Display(Name= "Price Justification")]
    [StringLength(500, ErrorMessage = "Price mute be longer than 500 characters")]
    public string PriceJustification { get; set; }
}