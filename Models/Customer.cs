namespace logirack.Models;

public class Customer : ApplicationUser
{
    public List<Trip> Trips { get; set; }
    public bool isDeleted { get; set; } 
    public DateTime? DeletedOn { get; set; } 
    public string DisplayName { get; set; }

    public Customer()
    {
        Trips = new List<Trip>();
        isDeleted=false;
        DeletedOn = null;
    }

}