using System;
using System.Collections.Generic;

namespace logirack.Models
{
    public class DashboardStats
    {
        public int ActiveTrips { get; set; }
        public int AvailableDrivers { get; set; }
        public int PendingApprovals { get; set; }
        public double  TotalRevenue { get; set; }
        public int CompletedTrips { get; set; }
        public List<RecentActivity> RecentActivities { get; set; }
        public List<Driver> AvailableDriversList { get; set; }
        public List<Customer> Customers { get; set; }
    }
    
    public class RecentActivity
    {
        public int Id { get; set; }
        public int TripId { get; set; }
        public string ActivityType { get; set; } // e.g., "Driver", "Trip", "User"
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public Trip Trip { get; set; }

    }
}