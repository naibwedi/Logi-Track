using System;

namespace logirack.Models
{
    public class OrderModel
    {
        public string CustomerName { get; set; }
        public string ContactInfo { get; set; }
        public string PickupLocation { get; set; }
        public string DropoffLocation { get; set; }
        public string GoodsType { get; set; }
        public double Weight { get; set; }
        public DateTime PickupDate { get; set; } // For the date
        public TimeSpan PickupTime { get; set; } // For the time
        public double Distance { get; set; }
        public string AdditionalServices { get; set; }
        public double PriceEstimate { get; set; }
    }
}