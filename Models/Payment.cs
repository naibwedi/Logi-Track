namespace logirack.Models;
using System.ComponentModel.DataAnnotations;


public class Payment
{
    public int Id { get; set; }
    [Required]
    public int PaymentPeriodId { get; set; }
    public PaymentPeriod PaymentPeriod { get; set; }
    [Required]
    public string DriverId { get; set; }
    public Driver Driver { get; set; }
    
    [Required]
    [DataType(DataType.Currency)]
    public double Amount { get; set; }
    [Required]
    public DateTime PaymentDate { get; set; }
    
    public PaymentStatus Status { get; set; }
    [Required]
    public String ProcByAdminID { get; set; }
    public Admin ProcByAdmin { get; set; }
}