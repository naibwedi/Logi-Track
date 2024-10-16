namespace logirack.Models;

public class Customer : ApplicationUser
{
    public List<Trip> Trips { get; set; }
}