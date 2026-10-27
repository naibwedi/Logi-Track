namespace logirack.Services;

using logirack.Models;

public interface IInvoiceService
{
    byte[] GenerateInvoicePdf(Trip trip, Customer customer);
}
