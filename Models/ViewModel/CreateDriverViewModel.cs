namespace logirack.Models.ViewModel;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// ViewModel for creating a new Driver.
/// </summary>
public class CreateDriverViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(30, MinimumLength = 3)]
    [Display(Name = "Driver Email")]
    public string DriverEmail { get; set; }
    
    [Required]
    [StringLength(100,ErrorMessage = "The password must at least {2} Characters long.", MinimumLength = 5)]
    [DataType(DataType.Password)]
    [Display(Name = "Driver Password")]
    public string DriverPassword { get; set; }
    
    [StringLength(100)]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("DriverPassword", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; }
    
    [Required]
    [StringLength(20 , MinimumLength = 2, ErrorMessage = "First Name must be between 2 and 20 characters.")]
    [RegularExpression(@"^[a-zA-Z]+$", ErrorMessage = "Use letters only please")]
    [Display(Name = "First Name")]
    public string FirstName { get; set; }
    
    [Required]
    [StringLength(20 , MinimumLength = 2, ErrorMessage = "Last name must be between 2 and 20 characters.")]
    [RegularExpression(@"^[a-zA-Z]+$", ErrorMessage = "Use letters only please")]
    [Display(Name = "Last Name")]
    public string LastName { get; set; }
    
    [Required]
    [Range(0.1, 1000, ErrorMessage = "Price per kn must be between 0 and 1000 Nok.")]
    [Display(Name = "Price per kn on Nok")]
    public double PricePerKm { get; set; }

    [Required]
    [StringLength(15,MinimumLength = 3 , ErrorMessage = "Phone Number must be between 3 and 15 characters.")]
    [DataType(DataType.PhoneNumber)]
    [Display(Name = "Phone Number")]

    public string PhoneNumber { get; set; }
    
    [Required]
    [Display(Name = "Payment Frequency")]
    public PaymentFreq PaymentFreq { get; set; }
    
    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
 
    public DateTime DateOfBirth { get; set; }
    
}