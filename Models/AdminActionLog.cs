namespace logirack.Models;

public class AdminActionLog
{
    public int Id { get; set; }
    public string AdminId { get; set; }
    public Admin Admin { get; set; }
    public string ASction { get; set; }
    public string DeTails { get; set; }
    public DateTime ADate { get; set; }
}