using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace logirack.Models.ViewModel;

    // ViewModels/InvoiceGenerationViewModel.cs
    public class InvoiceGenerationViewModel
    {
        // Driver Information
        public string SelectedDriverId { get; set; } // Driver ID
        public List<SelectListItem> DriverList { get; set; } = new List<SelectListItem>();

        // Date Range
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Trip Details for the Invoice
        public List<TripDetail> TripDetails { get; set; } = new List<TripDetail>();
        
        // Deductions if any
        public List<DeductionDetail> Deductions { get; set; } = new List<DeductionDetail>();

        // Summary
        public decimal TotalAmount { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal FinalAmount { get; set; }

        // Nested classes representing trips & deductions
        public class TripDetail
        {
            public DateTime Date { get; set; }
            public string TripInfo { get; set; }
            public decimal Amount { get; set; }
        }

        public class DeductionDetail
        {
            public DateTime Date { get; set; }
            public string DeductionName { get; set; }
            public decimal Amount { get; set; }
        }
    }