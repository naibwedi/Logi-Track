namespace logirack.Models;
/// <summary>
/// Represents the status of a trip.
/// </summary>
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