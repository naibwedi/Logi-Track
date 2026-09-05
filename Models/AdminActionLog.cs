namespace logirack.Models;

public class AdminActionLog
{
    public int Id { get; set; }
    public string AdminId { get; set; } = string.Empty;
    public Admin Admin { get; set; } = null!;
    public string ASction { get; set; } = string.Empty;
    public string DeTails { get; set; } = string.Empty;
    public DateTime ADate { get; set; }
}
