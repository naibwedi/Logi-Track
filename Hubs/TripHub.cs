using System.Security.Claims;
using logirack.Data;
using logirack.Models;
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

    public async Task SendLocationUpdate(int tripId, double latitude, double longitude)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("Authentication is required.");
        }

        if (double.IsNaN(latitude) || double.IsInfinity(latitude) ||
            double.IsNaN(longitude) || double.IsInfinity(longitude) ||
            latitude < -90.0 || latitude > 90.0 ||
            longitude < -180.0 || longitude > 180.0)
        {
            throw new HubException("Invalid coordinates.");
        }

        var trip = await db.Trips
            .Include(t => t.DriverTrip)
            .FirstOrDefaultAsync(t => t.Id == tripId);

        if (trip == null)
        {
            throw new HubException("Trip not found.");
        }

        var isAdmin = Context.User!.IsInRole("Admin") || Context.User.IsInRole("SuperAdmin");
        var isAssignedDriver = trip.DriverTrip != null && trip.DriverTrip.DriverId == userId;

        if (!isAdmin && !isAssignedDriver)
        {
            throw new HubException("You are not authorized to update location for this trip.");
        }

        if (trip.Status != TripStatus.Assigned && trip.Status != TripStatus.InProgress)
        {
            throw new HubException("Location updates are only allowed for active trips.");
        }

        if (trip.Status == TripStatus.Assigned)
        {
            trip.Status = TripStatus.InProgress;
            trip.UpdatedAt = DateTime.UtcNow;
            if (trip.DriverTrip != null)
            {
                trip.DriverTrip.Status = TripStatus.InProgress;
                trip.DriverTrip.UpdateAt = DateTime.UtcNow;
            }
        }

        var targetDriverId = isAssignedDriver ? userId : trip.DriverTrip?.DriverId;
        if (!string.IsNullOrEmpty(targetDriverId))
        {
            var driverLocation = await db.Locations.FirstOrDefaultAsync(l => l.DriverID == targetDriverId);
            if (driverLocation == null)
            {
                driverLocation = new Location
                {
                    DriverID = targetDriverId,
                    Latitude = latitude,
                    Longitude = longitude,
                    City = trip.ToCity,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Locations.Add(driverLocation);
            }
            else
            {
                driverLocation.Latitude = latitude;
                driverLocation.Longitude = longitude;
                driverLocation.UpdatedAt = DateTime.UtcNow;
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Handle race condition when concurrent GPS updates arrive simultaneously
        }

        await Clients.Group($"trip_{tripId}").SendAsync("ReceiveLocationUpdate", tripId, latitude, longitude);
        await Clients.Group("Admins").SendAsync("ReceiveLocationUpdate", tripId, latitude, longitude);
    }
}
