namespace logirack.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// A freight broker that tenders loads to the carrier (e.g. TQL, C.H. Robinson, Landstar).
/// </summary>
public class Broker
{
    public int Id { get; set; }

    [Required]
    [StringLength(150)]
    [Display(Name = "Company Legal Name")]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>FMCSA Motor Carrier number, stored normalized as digits only (e.g. "322572").</summary>
    [Required]
    [StringLength(12)]
    [RegularExpression(@"^\d{1,8}$", ErrorMessage = "MC number must be 1-8 digits (the 'MC#' prefix is optional when typing).")]
    [Display(Name = "MC Number")]
    public string McNumber { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Primary Agent")]
    public string? AgentName { get; set; }

    [StringLength(30)]
    [Phone]
    public string? Phone { get; set; }

    [StringLength(10)]
    [Display(Name = "Ext.")]
    public string? PhoneExtension { get; set; }

    [StringLength(150)]
    [EmailAddress]
    [Display(Name = "Dispatch Email")]
    public string? Email { get; set; }

    [Display(Name = "Bad Broker (Do Not Dispatch)")]
    public bool IsBlacklisted { get; set; }

    [StringLength(500)]
    [Display(Name = "Blacklist Reason")]
    public string? BlacklistReason { get; set; }

    public DateTime? BlacklistedAt { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Trip> Trips { get; set; } = new List<Trip>();

    public string DisplayMc => $"MC#{McNumber}";

    /// <summary>Strips an "MC", "MC#", "MC-" prefix and whitespace, leaving digits.</summary>
    public static string NormalizeMc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return new string(raw.Where(char.IsDigit).ToArray());
    }
}
