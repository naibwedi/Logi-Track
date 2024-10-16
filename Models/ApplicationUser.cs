﻿using Microsoft.AspNetCore.Identity;

namespace logirack.Models;

public class ApplicationUser : IdentityUser
{
    //is the user approved by the admin or not 
    public bool IsApproved { get; set; } = false;
    public string RoleType { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public DateTime ModifiedOn { get; set; } = DateTime.Now;
    
    public Customer Customer { get; set; }
    public Driver Driver { get; set; }
    public Admin Admin { get; set; }
    public SuperAdmin SuperAdmin { get; set; }
    
}