using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace logirack.Models;

public class ApplicationUser : IdentityUser
{
    //is the user approved by the admin or not 
    [Required]
    public bool IsApproved { get; set; } = false;
    [Required]
    [MaxLength(10)]
    public string RoleType { get; set; }=string.Empty;
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public DateTime ModifiedOn { get; set; } = DateTime.Now;
    
    [Required]
    [StringLength(20 , MinimumLength = 2, ErrorMessage = "First name must be between 2 and 20 characters.")]
    [RegularExpression(@"^[a-zA-Z]+$", ErrorMessage = "Use letters only please")]
    [Display(Name = "First Name")]
    public string FirstName { get; set; }
    [Required]
    [StringLength(20 , MinimumLength = 2, ErrorMessage = "Last name must be between 2 and 20 characters.")]
    [RegularExpression(@"^[a-zA-Z]+$", ErrorMessage = "Use letters only please")]
    [Display(Name = "Last Name")]
    public string LastName { get; set; }
    
}