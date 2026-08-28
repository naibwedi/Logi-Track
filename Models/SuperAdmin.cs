namespace logirack.Models;

public class SuperAdmin : ApplicationUser
{
    public List<AdminActionLog> ActionLogs { get; set; }

}