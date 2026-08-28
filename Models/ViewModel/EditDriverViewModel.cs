using System.ComponentModel.DataAnnotations;



namespace logirack.Models.ViewModel;

public class EditDriverViewModel
{
    public string id { get; set; }
    
    [Required]
    [Display(Name = "First Name")]
    public string FirstName { get; set; }
    
    [Required]
    [Display(Name = "Last Name")]
    public string LastName { get; set; }
    
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; }=string.Empty;
    
    [Required]
    [Phone]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; }=string.Empty;
    
    [Required]
    [Display(Name = "Price per Km")]
    [Range(0, double.MaxValue, ErrorMessage = "Please enter a valid price.")]
    public double PricePerKm { get; set; }
    
    [Display(Name = "Payment Frequency")]
    public PaymentFreq PaymentFreq { get; set; }
    
    [Display(Name ="is Available")]
    public bool IsAvailable { get; set; }
    
}