using System.Security.Claims;
using logirack.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace logirack.Hubs;

[Authorize]
public sealed class TripHub(ApplicationDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("SuperAdmin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinTrip(int tripId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("Authentication is required.");
        }

        var isAdmin = Context.User!.IsInRole("Admin") || Context.User.IsInRole("SuperAdmin");
        var isParticipant = isAdmin || await db.Trips.AnyAsync(trip =>
            trip.Id == tripId &&
            (trip.CustomerId == userId || (trip.DriverTrip != null && trip.DriverTrip.DriverId == userId)));

        if (!isParticipant)
        {
            throw new HubException("You are not allowed to subscribe to this trip.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"trip_{tripId}");
    }

    public Task LeaveTrip(int tripId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trip_{tripId}");
}
