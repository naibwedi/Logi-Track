namespace logirack.Models;

public enum TripStatus
{
    Created,
    PriceSet,
    Accepted,
    Assigned,
    InProgress,
    Completed,
    CancelledByAdmin,
    CanceledByCustomer,
}