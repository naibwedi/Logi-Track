using System.ComponentModel.DataAnnotations;
using Microsoft.Build.Framework;

namespace logirack.Models.ViewModel;

public class EditDriverViewModel
{
    public string id { get; set; }
    
    [System.ComponentModel.DataAnnotations.Required]
    [Display(Name = "First Name")]
    public string FirstName { get; set; }
    
    [System.ComponentModel.DataAnnotations.Required]
    [Display(Name = "Last Name")]
    public string LastName { get; set; }
    
    [System.ComponentModel.DataAnnotations.Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; }
    
    [System.ComponentModel.DataAnnotations.Required]
    [Phone]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; }
    
    [System.ComponentModel.DataAnnotations.Required]
    [Display(Name = "Price per Km")]
    [Range(0, double.MaxValue, ErrorMessage = "Please enter a valid price.")]
    public double PricePerKm { get; set; }
    
    [Display(Name = "Payment Frequency")]
    public PaymentFreq PaymentFreq { get; set; }
    
    [Display(Name ="is Available")]
    public bool IsAvailable { get; set; }
    
}