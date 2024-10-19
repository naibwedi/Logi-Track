using System.ComponentModel.DataAnnotations;

namespace logirack.Models.ViewModel;

/// <summary>
/// ViewModel for creating a new Admin user.
/// </summary>
public class CreateAdminViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(30, MinimumLength = 3)]
    [Display(Name = "Admin Email")]
    public string AdminEmail { get; set; }
    
    [Required]
    [StringLength(100,ErrorMessage = "The password must at least {2} Characters long.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Admin Password")]
    public string AdminPassword { get; set; }
    
    [DataType(DataType.Password)]
    [StringLength(100)]
    [Display(Name = "Confirm Password")]
    [Compare("AdminPassword", ErrorMessage = "The password and confirmation password do not match.")]
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
    [StringLength(15,MinimumLength = 3 , ErrorMessage = "Phone Number must be between 3 and 15 characters.")]
    [DataType(DataType.PhoneNumber)]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; }
    
    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
 
    public DateTime DateOfBirth { get; set; }
    
    
}