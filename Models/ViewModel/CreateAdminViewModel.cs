using System.ComponentModel.DataAnnotations;

namespace logirack.Models.ViewModel;


public class CreateAdminViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(30, MinimumLength = 3)]
    [Display(Name = "Admin Email")]
    public string AdminEmail { get; set; }
    
    [Required]
    [StringLength(100,ErrorMessage = "The password must at least {2} Characters long.", MinimumLength = 5)]
    [DataType(DataType.Password)]
    [Display(Name = "Admin Password")]
    public string AdminPassword { get; set; }
    
    [DataType(DataType.Password)]
    [StringLength(100)]
    [Display(Name = "Confirm Password")]
    [Compare("AdminPassword", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; }
    
    [Required]
    [StringLength(30, MinimumLength = 3)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; }
    
    [Required]
    [StringLength(30, MinimumLength = 3)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; }
    
    [Required]
    [StringLength(30, MinimumLength = 3)]
    [DataType(DataType.PhoneNumber)]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; }
    
    
}