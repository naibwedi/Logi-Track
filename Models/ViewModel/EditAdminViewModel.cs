using System.ComponentModel.DataAnnotations;

namespace logirack.Models.ViewModel
{
    /// <summary>
    /// ViewModel for editing an existing Admin user.
    /// </summary>
    public class EditAdminViewModel
    {
        [Required]
        public string Id { get; set; } // The unique identifier for the admin being edited

        [Required]
        [EmailAddress]
        [StringLength(30, MinimumLength = 3)]
        [Display(Name = "Admin Email")]
        public string AdminEmail { get; set; }

        [Required]
        [StringLength(20, MinimumLength = 2, ErrorMessage = "First Name must be between 2 and 20 characters.")]
        [RegularExpression(@"^[a-zA-Z]+$", ErrorMessage = "Use letters only please")]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Required]
        [StringLength(20, MinimumLength = 2, ErrorMessage = "Last Name must be between 2 and 20 characters.")]
        [RegularExpression(@"^[a-zA-Z]+$", ErrorMessage = "Use letters only please")]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        [Required]
        [StringLength(15, MinimumLength = 3, ErrorMessage = "Phone Number must be between 3 and 15 characters.")]
        [DataType(DataType.PhoneNumber)]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Date of Birth")]
        public DateTime DateOfBirth { get; set; }
    }
}