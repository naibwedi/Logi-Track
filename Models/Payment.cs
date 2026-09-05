namespace logirack.Models;
using System.ComponentModel.DataAnnotations;


public class Payment
{
    public int Id { get; set; }
    [Required]
    public int PaymentPeriodId { get; set; }
    public PaymentPeriod PaymentPeriod { get; set; } = null!;
    [Required]
    public string DriverId { get; set; } = string.Empty;
    public Driver Driver { get; set; } = null!;
    
    [Required]
    [DataType(DataType.Currency)]
    public double Amount { get; set; }
    [Required]
    public DateTime PaymentDate { get; set; }
    
    public PaymentStatus Status { get; set; }
    [Required]
    public string ProcByAdminID { get; set; } = string.Empty;
    public Admin ProcByAdmin { get; set; } = null!;
}
