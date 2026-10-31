namespace logirack.Services;

using logirack.Models;

public interface IInvoiceService
{
    byte[] GenerateInvoicePdf(Trip trip, Customer customer);
    byte[] GenerateSettlementStatementPdf(Trip trip, Driver driver, SettlementBreakdown breakdown, Vehicle? vehicle = null, Broker? broker = null);
}
