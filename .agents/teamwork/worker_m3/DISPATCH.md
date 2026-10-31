## 2026-10-27T20:17:04Z
You are worker_m3.
Your working directory: C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\worker_m3
Project root: C:\Users\naib\Documents\projects\Logi-Track
Authoritative request file: C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\ORIGINAL_REQUEST.md
Survey & Technical Design report: C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\explorer_survey_3\survey_invoicing_tests.md
Project specification: C:\Users\naib\Documents\projects\Logi-Track\PROJECT.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Objective:
Implement Milestone 3: R3 Automated PDF Invoicing & Financial Summary.
Specific tasks:
1. PDF Library Reference:
   - Add a pure managed .NET 8 compatible PDF library (e.g. QuestPDF or PdfSharpCore / PDFsharp) to logirack.csproj and tests/LogiTrack.Tests/LogiTrack.Tests.csproj if needed.
   - If using QuestPDF: Configure `QuestPDF.Settings.License = LicenseType.Community;` in Program.cs.
2. Invoicing Service:
   - Create `Services/IInvoiceService.cs` and `Services/PdfInvoiceService.cs`:
     - Method signature: `byte[] GenerateInvoicePdf(Trip trip, Customer customer);`
     - Generate clean, formatted PDF invoices containing:
       - Header: Logi-Track brand, Invoice ID (`INV-{trip.Id:D6}`), Issue Date, Trip Creation Date.
       - Customer Information: Customer full name, email, phone number.
       - Shipment & Route details: From Address/City/Zip, To Address/City/Zip, Goods Type, Distance (km), Weight (kg), Volume (m³).
       - Itemized fare breakdown:
         - Base Rate: 50.00 NOK
         - Distance Transport Fee: Distance * 15.00 NOK
         - Cargo Weight Surcharge: Weight * 20.00 NOK
         - Service Handling Fee: 100.00 NOK
         - Total Final Price: `trip.AdminPrice ?? trip.EstimatedPrice` NOK
       - Payment Status: "PAID" / "SETTLED" for completed trips.
       - PDF document metadata: Title, Author, Subject, Producer.
     - Register `IInvoiceService` in Program.cs.
3. Download Endpoints:
   - In `Controllers/CustomerController.cs`:
     - Add `[HttpGet] public async Task<IActionResult> DownloadInvoice(int id)`:
       - Validate caller authentication and customer role.
       - Query trip including Customer: ensure `trip.Id == id && trip.CustomerId == user.Id`. If not found, return `NotFound()`.
       - Enforce completed status: `trip.Status == TripStatus.Completed`. If not completed, return `BadRequest` or `NotFound()`.
       - Generate PDF via `_invoiceService.GenerateInvoicePdf(trip, trip.Customer ?? user)`.
       - Return `File(pdfBytes, "application/pdf", $"Invoice_Trip_{trip.Id}.pdf")`.
   - In `Controllers/AdminController.cs`:
     - Add `[HttpGet] public async Task<IActionResult> DownloadInvoice(int id)`:
       - Validate admin authorization.
       - Query trip including Customer. If not found, return `NotFound()`.
       - Enforce completed status: `trip.Status == TripStatus.Completed`. If not completed, return `BadRequest` or `NotFound()`.
       - Generate PDF via `_invoiceService.GenerateInvoicePdf(trip, trip.Customer!)`.
       - Return `File(pdfBytes, "application/pdf", $"Invoice_Trip_{trip.Id}.pdf")`.
4. Views Integration:
   - In `Views/Customer/MyTrips.cshtml`: Add "Download Invoice" button/link for completed trips.
   - In `Views/Customer/TripDetails.cshtml`: Add "Download Invoice" button/link for completed trips.
   - In `Views/Admin/InvoiceGeneration.cshtml`: Add "Download PDF Invoice" button/link for completed trips.
5. Automated Tests:
   - Create `tests/LogiTrack.Tests/InvoiceGenerationTests.cs`:
     - Test that `GenerateInvoicePdf` returns a non-empty byte array starting with `%PDF-`.
     - Test PDF metadata is populated.
     - Test customer can download invoice for own completed trip (`application/pdf`, non-zero length).
     - Test IDOR protection: customer cannot download invoice for another customer's trip (returns 404).
     - Test invoice download is rejected for non-completed trips (e.g. Requested, Assigned, InProgress).
     - Test admin can download invoice for any completed trip.
     - Test admin download rejected for non-completed trip.
6. Verification:
   - Run `dotnet build --warnaserror` (0 warnings, 0 errors).
   - Run `dotnet test` (all tests passing).
   - Write handoff report to `C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\worker_m3\handoff.md`.
   - Send message to orchestrator_1 when finished.
