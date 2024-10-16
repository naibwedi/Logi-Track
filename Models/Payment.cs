namespace logirack.Models;

public class Payment
{
    public int Id { get; set; }
    public int PaymentPeriodId { get; set; }
    public PaymentPeriod PaymentPeriod { get; set; }
    public double Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public PaymentStatus Status { get; set; }
    public String ProcByAdminID { get; set; }
    public Admin ProcByAdmin { get; set; }
}