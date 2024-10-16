using System.ComponentModel.DataAnnotations;

namespace logirack.Models;

public class Admin : ApplicationUser
{
    public List<Trip> ManagedTrips { get; set; } 
    public List<AdminActionLog> ActionLogs  { get; set; }
    public List<DriverTrip> AssignedDriverT { get; set; }
    public List<Payment> ProcessedPayments { get; set; }
    
}