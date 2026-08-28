
using logirack.Models;
using Microsoft.AspNetCore.SignalR;

namespace logirack.Hubs
{
    public class TripHub : Hub
    {
        // Trip Status 
        public async Task UpdateTripStatus(int tripId, TripStatus newStatus, string message)
        {
            await Clients.Groups($"trip_{tripId}").SendAsync("TripStatusUpdated", new
            {
                TripId = tripId,
                Status = newStatus,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        // Admin Dashboard 
        public async Task UpdateAdminDashboard(DashboardStats stats)
        {
            await Clients.Group("Admins").SendAsync("DashboardStatsUpdated", stats);
        }

        // Price 
        public async Task NotifyPriceSet(int tripId, decimal price)
        {
            await Clients.Groups($"trip_{tripId}").SendAsync("PriceUpdated", new
            {
                TripId = tripId,
                Price = price,
                Timestamp = DateTime.Now
            });
        }
        // Driver
        public async Task NotifyDriverAssigned(int tripId, string driverName)
        {
            await Clients.Groups($"trip_{tripId}").SendAsync("DriverAssigned", new
            {
                TripId = tripId,
                DriverName = driverName,
                Timestamp = DateTime.Now
            });
        }
    }
}