# Logi-Track Survey Report: R3 (Automated PDF Invoicing) & R4 (Automated Test Coverage)

**Date**: 2026-10-27  
**Explorer**: explorer_survey_3  
**Working Directory**: `C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\explorer_survey_3`  
**Target Solution**: `C:\Users\naib\Documents\projects\Logi-Track\logirack.sln`  
**Authoritative Request**: `C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\ORIGINAL_REQUEST.md`

---

## Executive Summary

This survey examines the Logi-Track codebase regarding **R3 (Automated PDF Invoicing & Financial Summary)** and **R4 (Automated Test Coverage & Infrastructure)**.

Key findings:
1. **Invoicing & Billing**:
   - There is currently **no Invoice model or service** in the codebase.
   - `AdminController.Invoice(int id)` currently displays an HTML driver payment statement for `Payment` entities using a client-side `window.print()` button.
   - `AdminController.InvoiceGeneration()` only renders an HTML summary table of completed trips for driver payout preparation.
   - `CustomerController` has no invoice viewing or download capabilities.
   - **Zero PDF generation libraries** are installed in either `logirack.csproj` or `tests/LogiTrack.Tests/LogiTrack.Tests.csproj`.
2. **Fare Breakdown & Payment Status**:
   - `Trip` currently contains `EstimatedPrice`, `AdminPrice`, `Weight`, `Volume`, `Distance`, and `Status` (`TripStatus`), but lacks an explicit `PaymentStatus` attribute.
   - Price estimation is calculated in `CustomerController.CalculateTripPriceEstimate`: `BaseRate (50) + Distance * 15 NOK/km + Weight * 20 NOK/kg + ServiceCharge (100)`.
   - Invoices require an itemized fare breakdown structure and a clear payment status representation for completed trips.
3. **Existing Test Suite**:
   - `tests/LogiTrack.Tests` uses **xUnit 2.9.2** with `Microsoft.AspNetCore.Mvc.Testing 8.0.30`.
   - Current test coverage consists solely of **21 integration tests in `ApplicationSmokeTests.cs`**, using a temporary SQLite database per test fixture via `LogiTrackFactory`.
   - Test execution is clean (21 passed in ~6.0s) and builds with **0 warnings and 0 errors** under `--warnaserror`.
4. **Test Infrastructure & Gaps**:
   - Severe test gaps exist: **zero IDOR tests**, **zero concurrency / race condition tests**, **zero SignalR location broadcast authorization tests**, and **zero PDF byte stream / metadata tests**.
   - GitHub Actions CI (`.github/workflows/ci.yml`) builds and tests on `ubuntu-latest` (Linux), meaning any PDF library and test dependencies must work headlessly on Linux without extra OS-level configuration failures.

---

## 1. Billing & Invoicing Deep Dive

### 1.1 Existing Models, Services, and Controllers

| Component | Path | Current Functionality | R3 Gap |
|---|---|---|---|
| **Invoice Model** | `Models/` | **None exists**. | Missing DTO / domain representation for customer invoices with itemized fare lines. |
| **Payment Model** | `Models/Payment.cs` | Tracks driver payouts (`Amount`, `PaymentDate`, `Status`, `PaymentPeriodId`, `DriverId`). | Driver-facing payout record, not a customer trip invoice. |
| **DriverTrip Model** | `Models/DriverTrip.cs` | Tracks trip assignment to driver (`DriverPayment`, `Status`, `CompletionDate`, `AssignmentDate`). | Represents driver trip assignment and payment tracking. |
| **Trip Model** | `Models/Trip.cs` | Contains `EstimatedPrice`, `AdminPrice`, `Status`, dimensions, and customer relationship. | Lacks direct invoice link or itemized breakdown structure. |
| **Invoice Services** | `Services/` | **None exists**. (Only `ApprovedOnlyAttribute`, `EmailSender`, `PasswordService`). | Missing `IInvoiceService` / `PdfInvoiceService` for document creation. |
| **Admin Controller** | `Controllers/AdminController.cs` (lines 1109–1130) | `Invoice(int id)`: returns HTML view of `Payment` (driver compensation).<br>`InvoiceGeneration()`: returns HTML view of completed `DriverTrip` list. | No endpoint generates or streams PDF invoices for completed customer trips. |
| **Customer Controller** | `Controllers/CustomerController.cs` | `MyTrips()` and `TripDetails(int id)`. | No invoice viewing or download action. |
| **Customer Views** | `Views/Customer/MyTrips.cshtml`<br>`Views/Customer/TripDetails.cshtml` | Display trip tables and details. | No "Download Invoice" button for completed trips. |
| **Admin Views** | `Views/Admin/InvoiceGeneration.cshtml` | Displays completed trips in HTML table. | No download action or PDF export button. |

### 1.2 PDF Generation Library Analysis

Currently, neither project file references any PDF generator. The following libraries were evaluated for .NET 8 / Logi-Track:

#### Option A: QuestPDF (`QuestPDF` v2024.x / 2026.x)
- **NuGet**: `QuestPDF` (Downloads: 41M+)
- **API Style**: Fluent C# layout engine (declarative headers, footers, tables, item rows, styling).
- **Metadata**: First-class support via `container.DocumentMetadata(m => { m.Title(...); m.Author(...); m.Subject(...); })`.
- **Output**: Returns byte array directly via `document.GeneratePdf()` or writes to stream.
- **Licensing**: Requires setting `QuestPDF.Settings.License = LicenseType.Community;` in startup/service (free for individuals/companies < $1M revenue and OSS).
- **Runtime Dependencies**: Relies on SkiaSharp and HarfBuzzSharp native libraries. In Docker / Linux, requires font availability.

#### Option B: PDFsharp 6.x (`PDFsharp` / `PDFsharp-MigraDoc` v6.5.0 / 6.1.1)
- **NuGet**: `PDFsharp` or `PDFsharp-MigraDoc` (Downloads: 78M+)
- **API Style**: Managed C# library with MIT license. `MigraDoc` provides high-level document sections, tables, paragraphs, and styles.
- **Metadata**: Directly supported via `document.Info.Title = ...; document.Info.Author = "LogiTrack";`.
- **Output**: Writes to `MemoryStream` and byte array.
- **Licensing**: **MIT license** — 100% free, zero revenue restrictions, zero license configuration calls.
- **Runtime Dependencies**: Pure managed C# with cross-platform core; zero native C++ runtime quirks on Linux CI.
- **Test Validation**: Extremely easy to verify in tests using `PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly)` to assert `Title`, `Author`, `PageCount >= 1`.

#### Recommendation:
- Implement a clean interface: `IInvoiceService` with method `byte[] GenerateTripInvoicePdf(Trip trip)`.
- Use either **QuestPDF** (for modern declarative layout) or **PDFsharp / PDFsharp-MigraDoc** (for pure MIT licensing and zero-configuration cross-platform Linux CI compatibility).

---

## 2. Fare Breakdown & Payment Status Analysis

### 2.1 Pricing Logic in Existing Code
In `Controllers/CustomerController.cs` (lines 362–370):
```csharp
private double CalculateTripPriceEstimate(SubmitTripViewModel model)
{
    double baseRate = 50;
    double distanceRate = model.Distance * 15; // 15 NOK per km
    double weightRate = model.Weight * 20;     // 20 NOK per kg
    double serviceCharge = 100;
    return baseRate + distanceRate + weightRate + serviceCharge;
}
```
In `Controllers/AdminController.cs`:
- An Admin sets the final price via `SetPrice` (`tripToUpdate.AdminPrice = model.AdminPrice;`), which updates `Trip.Status = TripStatus.PriceSet`.
- The customer approves the price via `CustomerController.RespondToPrice` (`Trip.Status = TripStatus.ApprovedByCustomer`).
- The driver receives `DriverPayment = driver.PricePerKm * model.Distance`.

### 2.2 Itemized Fare Breakdown for Invoicing
To fulfill R3 ("itemized fare breakdown"), the invoice generator must present itemized charges.
Recommended invoice line-item structure:
1. **Base Freight Handling**: 50.00 NOK (or base allocation)
2. **Distance Transport**: `{Distance} km @ 15.00 NOK/km` = `{Distance * 15:F2} NOK`
3. **Cargo Weight Surcharge**: `{Weight} kg @ 20.00 NOK/kg` = `{Weight * 20:F2} NOK`
4. **Service & Dispatch Fee**: 100.00 NOK
5. **Admin Adjustment / Negotiated Rate Surcharge/Discount**: `AdminPrice - EstimatedPrice` (if applicable)
6. **Total Fare**: `{AdminPrice:C}` (or `EstimatedPrice:C` if AdminPrice is unset)
7. **VAT / Tax**: Norwegian MVA standard line (e.g. 25% VAT breakdown or VAT inclusive statement).

### 2.3 Payment Status Attribute on Completed Trips
- `Trip` currently does not have a `PaymentStatus` column in SQLite (`TripStatus` has values: `Requested`, `ApprovedByAdmin`, `PriceSet`, `ApprovedByCustomer`, `Assigned`, `InProgress`, `Completed`, `CancelledByAdmin`, `CanceledByCustomer`).
- The `PaymentStatus` enum (`Models/PaymentStatus.cs`) defines: `Pending`, `Processing`, `Paid`.
- For customer invoices:
  - Invoices are generated for **completed trips** (`trip.Status == TripStatus.Completed`).
  - Upon trip completion, the invoice status should state **"Paid"** / **"Settled"** (or dynamically reflect payment finalization).
  - A clean model / DTO (e.g., `TripInvoiceDetails`) should populate `PaymentStatus = "Paid"` when `trip.Status == TripStatus.Completed`.

---

## 3. Test Suite & Infrastructure Survey

### 3.1 Project Configuration
- **File**: `tests/LogiTrack.Tests/LogiTrack.Tests.csproj`
- **Framework**: `net8.0`
- **Nullable**: `enable`
- **ImplicitUsings**: `enable`
- **Packages**:
  - `Microsoft.AspNetCore.Mvc.Testing` (8.0.30)
  - `Microsoft.NET.Test.Sdk` (17.11.1)
  - `xunit` (2.9.2)
  - `xunit.runner.visualstudio` (2.8.2)
- **Missing Test Dependencies**:
  - `Microsoft.AspNetCore.SignalR.Client` (needed for end-to-end SignalR authorization and broadcast tests)
  - `PDFsharp` or PDF stream validator (for PDF structure and metadata verification in tests)

### 3.2 Existing Test Fixture & Database Strategy
- **Fixture Class**: `LogiTrackFactory : WebApplicationFactory<Program>` (currently embedded inside `ApplicationSmokeTests.cs` lines 233–260).
- **Environment**: Sets `builder.UseEnvironment("Development")`.
- **Database**:
  - Uses an isolated per-run SQLite database file:  
    `_databasePath = Path.Combine(Path.GetTempPath(), $"logitrack-tests-{Guid.NewGuid():N}.db");`
  - When the web host boots, `Program.cs` executes:
    ```csharp
    await db.Database.MigrateAsync();
    await SeedData.Initialize(services);
    ```
    This automatically creates all tables, seeds standard roles (`SuperAdmin`, `Admin`, `Customer`, `Driver`), and creates the bootstrap SuperAdmin user.
  - Connection pooling on SQLite is transient; temp files are cleaned up by the OS.
- **Helpers**:
  - `EnsureUser`: ensures required test users exist with role and confirmed email.
  - `LoginClient`: authenticates via `/Identity/Account/Login` with CSRF token extraction and returns cookie-authenticated `HttpClient`.
  - `GetToken`: parses `__RequestVerificationToken` from response HTML via Regex.

### 3.3 Current Test Execution Baseline
- **Command**: `dotnet test --verbosity normal`
- **Result**: **21 passed, 0 failed, 0 skipped** (Execution time: ~6.0s).
- **Build Warning Verification**: `dotnet build --warnaserror` succeeds with **0 warnings and 0 errors**.

---

## 4. Exact Test Gaps Analysis (Against R4 & Acceptance Criteria)

| Acceptance Criteria Category | Specific Acceptance Criterion | Current Status | Detailed Gap & Test Requirements |
|---|---|---|---|
| **Security & State Integrity** | Direct object reference (IDOR) attempts return 403 Forbidden or 404 Not Found. | **0 tests** | - Customer B attempting `GET /Customer/TripDetails/{CustomerA_TripId}` must return 403 or 404.<br>- Customer B attempting `POST /Customer/RespondToPrice` on Customer A's trip must return 403 or 404.<br>- Customer B attempting `GET /Customer/DownloadInvoice/{CustomerA_TripId}` must return 403 or 404.<br>- Driver B attempting `POST /Driver/EndTrip` on Driver A's trip must return 403 or 404.<br>- Unauthenticated or non-admin attempting admin endpoints must be challenged/forbidden. |
| **Security & State Integrity** | Concurrent state modification tests verify stale trip updates are rejected without corrupting DB state. | **0 tests** | - `Trip` currently lacks optimistic concurrency tokens (`RowVersion` or `ConcurrencyStamp`).<br>- Two simultaneous admin reviews/approvals or price updates on the same trip must reject stale updates without duplicate or conflicting records.<br>- Concurrent driver assignment to the same trip must assign only once and prevent double-booking. |
| **Map & Real-Time Tracking** | SignalR connection tests confirm location broadcast events are delivered only to users authorized for that specific trip. | **1 basic test** (`SignalR_hub_rejects_anonymous_connections`) | - Test authorized `JoinTrip(tripId)`: Customer of the trip can join.<br>- Test authorized `JoinTrip(tripId)`: Assigned driver can join.<br>- Test authorized `JoinTrip(tripId)`: Admin/SuperAdmin can join.<br>- Test unauthorized `JoinTrip(tripId)`: Unrelated Customer or Driver must be rejected with `HubException`.<br>- Test location update broadcasts: only clients connected to `trip_{tripId}` receive location broadcast payloads; clients in other groups do not receive them. |
| **Invoicing & Builds** | Requesting invoice download for completed trip returns valid `application/pdf` byte stream with non-zero length and expected metadata. | **0 tests** | - Test customer downloading own completed trip invoice: HTTP 200, Content-Type `application/pdf`, non-zero length, filename header.<br>- Test admin downloading any completed trip invoice: HTTP 200, Content-Type `application/pdf`, non-zero length.<br>- Test PDF binary validity: starts with `%PDF-` header and contains `%%EOF`.<br>- Test metadata: Title containing Trip ID, Author "LogiTrack", Creation date.<br>- Test invoice download rejection: trips in status `Requested`, `ApprovedByAdmin`, `PriceSet`, `Assigned`, or `InProgress` must reject invoice download (e.g. 400 Bad Request or 404). |
| **Invoicing & Builds** | `dotnet build` succeeds with 0 warnings and 0 errors. | **Verified** | Currently 0 warnings and 0 errors. Must be maintained with `<Nullable>enable</Nullable>`. |
| **Invoicing & Builds** | `dotnet test` executes all existing and newly added unit/integration tests with 100% passing results. | **Verified** | Existing 21 tests pass. Newly added test classes must pass 100%. |

---

## 5. Architectural & Implementation Recommendations

### 5.1 Service & Controller Architecture for Invoicing (R3)

```
logirack/
├── Services/
│   ├── IInvoiceService.cs            # Interface: GenerateTripInvoicePdf(Trip trip)
│   └── PdfInvoiceService.cs          # QuestPDF or PDFsharp implementation
├── Models/
│   └── ViewModel/
│       └── TripInvoiceDto.cs         # Structured model for invoice details & fare lines
├── Controllers/
│   ├── CustomerController.cs         # Add: DownloadInvoice(int id) [Authorize(Roles="Customer")]
│   └── AdminController.cs            # Add: DownloadInvoice(int id) [Authorize(Roles="Admin,SuperAdmin")]
```

#### Invoice Download Endpoints:
1. `GET /Customer/DownloadInvoice/{id}`:
   - Validates user is the trip customer (`trip.CustomerId == user.Id`). If not, returns `Forbid()` / `NotFound()`.
   - Validates trip is completed (`trip.Status == TripStatus.Completed`). If not, returns `BadRequest("Invoice is only available for completed trips.")`.
   - Calls `_invoiceService.GenerateTripInvoicePdf(trip)`.
   - Returns `File(pdfBytes, "application/pdf", $"Invoice-{id}.pdf")`.
2. `GET /Admin/DownloadInvoice/{id}`:
   - Validates trip exists and is completed (`trip.Status == TripStatus.Completed`).
   - Generates and returns PDF file stream.
3. Views:
   - `Views/Customer/MyTrips.cshtml`: Add "Invoice" button in Actions column when `trip.Status == TripStatus.Completed`.
   - `Views/Customer/TripDetails.cshtml`: Add "Download Invoice (PDF)" button when `trip.Status == TripStatus.Completed`.
   - `Views/Admin/InvoiceGeneration.cshtml`: Add "Download Invoice" link for each completed trip row.

### 5.2 Test Project Organization (R4)

```
tests/LogiTrack.Tests/
├── Fixtures/
│   └── LogiTrackFactory.cs           # Factored out WebApplicationFactory & DB initialization
├── Helpers/
│   └── TestAuthExtensions.cs         # Shared helpers for user creation, logins, CSRF tokens
├── ApplicationSmokeTests.cs          # Retained existing 21 tests (unbroken regression suite)
├── AccessControlAndIdorTests.cs      # IDOR: cross-customer & cross-driver unauthorized access
├── ConcurrencyTests.cs               # Stale trip update and concurrent action tests
├── SignalRTrackingHubTests.cs        # SignalR authentication, group join auth, broadcast isolation
└── InvoiceGenerationTests.cs         # PDF byte stream, non-zero length, metadata, completed constraint
```

### 5.3 NuGet Dependencies to Add
- To `logirack.csproj`:
  - `QuestPDF` (or `PDFsharp-MigraDoc`)
- To `tests/LogiTrack.Tests/LogiTrack.Tests.csproj`:
  - `Microsoft.AspNetCore.SignalR.Client` (Version 8.0.x)
  - `PDFsharp` (Version 6.x) for deep PDF stream and metadata inspection in test assertions.

---

## Conclusion
The Logi-Track project is currently in a very clean build state (0 warnings, 0 errors, net8.0 nullable enabled), but lacks the invoice PDF generation capabilities (R3) and the comprehensive security, concurrency, SignalR, and invoice test coverage mandated by R4. The existing `WebApplicationFactory` SQLite test fixture provides an excellent, fast foundation on which to build the required test suites.
