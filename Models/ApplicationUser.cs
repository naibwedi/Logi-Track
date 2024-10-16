using Microsoft.AspNetCore.Identity;
using Microsoft.Build.Framework;

namespace logirack.Models;

public class ApplicationUser : IdentityUser
{
    //is the user approved by the admin or not 
    [Required]
    public bool IsApproved { get; set; } = false;
    public string RoleType { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public DateTime ModifiedOn { get; set; } = DateTime.Now;
    
}