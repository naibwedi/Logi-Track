# Project: Logi-Track Production-Grade Overhaul

## Architecture
Logi-Track is an ASP.NET Core 8 MVC web application with Entity Framework Core (SQLite in dev/test, PostgreSQL in prod), ASP.NET Core Identity for authentication and role-based authorization (Admin, Driver, Customer), and SignalR for real-time telemetry.

The overhaul introduces four core architectural pillars:
1. **Access Control & Concurrency Guard**: Strict role/ownership checks preventing IDOR on all customer/driver/admin endpoints; portable optimistic concurrency via `[ConcurrencyCheck] Guid ConcurrencyToken` refreshed in `ApplicationDbContext.SaveChangesAsync`; transaction-guarded state transitions; input sanitization and global antiforgery token validation.
2. **Real-Time GPS Telemetry & Interactive Mapping**: Leaflet 1.9.4 + OpenStreetMap interactive mapping embedded into active trip views; `TripHub` SignalR hub extended with `SendLocationUpdate(tripId, lat, lng)` enforcing assigned driver / admin role authorization, active trip validation (`Assigned` / `InProgress`), coordinate bounds validation, and isolated broadcast to group `trip_{tripId}`; an `ICoordinateService` providing deterministic city coordinate mapping (Oslo, Bergen, Trondheim, etc.) without schema alterations.
3. **Automated PDF Invoicing Service**: Pure managed C# PDF generation service (`IInvoiceService` / `PdfInvoiceService` using PDFsharp/MigraDoc or QuestPDF) generating itemized financial breakdown invoices for completed trips with PDF metadata, downloadable via `GET /Customer/DownloadInvoice/{id}` and `GET /Admin/DownloadInvoice/{id}`.
4. **Comprehensive Automated Test Coverage**: Expansion of `tests/LogiTrack.Tests` covering IDOR boundaries, concurrent state modifications, SignalR hub connection authorization and location broadcast scoping, and PDF invoice generation stream/metadata assertions with 0 build warnings/errors and 100% pass rate.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | IDOR Prevention in Trip Retrieval | Return 404/403 for unauthorized trip viewing, order success, and details | M1 | Survey (explorer_survey_1) & R1 |
| 2 | Optimistic Concurrency on Trip & DriverTrip | Add `[ConcurrencyCheck] Guid ConcurrencyToken` to prevent silent overwrites | M1 | Survey (explorer_survey_1) & R1 |
| 3 | Transaction-Guarded State Transitions | Prevent race conditions during admin reviews, pricing, driver assignment, end trip | M1 | Survey (explorer_survey_1) & R1 |
| 4 | Trip State Machine Harmonization | Enable `TripStatus.InProgress` and allow `EndTrip` from `Assigned` or `InProgress` | M1 | Survey (explorer_survey_1) & R1 |
| 5 | Input Sanitization & Global CSRF Guard | Sanitize addresses/notes, enforce string length limits, register global Antiforgery filter | M1 | Survey (explorer_survey_1) & R1 |
| 6 | Interactive Map with Route & Live Marker | Render Leaflet map on active trip views for customer, driver, and admin | M2 | Survey (explorer_survey_2) & R2 |
| 7 | Deterministic Coordinate Service | Map city names to coordinates (`ICoordinateService`) without database schema migrations | M2 | Survey (explorer_survey_2) & R2 |
| 8 | SignalR Hub Location Broadcasting | Implement `SendLocationUpdate` on `TripHub` with strict driver/admin authorization & bounds check | M2 | Survey (explorer_survey_2) & R2 |
| 9 | Driver Live GPS Simulation UI | Controls on Driver `CurrentTrips` to simulate movement with linear waypoint interpolation | M2 | Survey (explorer_survey_2) & R2 |
| 10 | Real-Time Group Broadcast Isolation | Deliver GPS updates strictly to authorized group `trip_{tripId}` | M2 | Survey (explorer_survey_2) & R2 |
| 11 | Server-Side PDF Invoicing Service | Implement `IInvoiceService` generating itemized fare breakdown PDF with metadata | M3 | Survey (explorer_survey_3) & R3 |
| 12 | Customer & Admin Invoice Downloads | Add `DownloadInvoice(id)` in `CustomerController` and `AdminController` with ownership checks | M3 | Survey (explorer_survey_3) & R3 |
| 13 | UI Invoice Download Links | Add download buttons to `Customer/MyTrips`, `Customer/TripDetails`, and `Admin/InvoiceGeneration` | M3 | Survey (explorer_survey_3) & R3 |
| 14 | Automated IDOR & Security Tests | Expand `tests/LogiTrack.Tests` with tests verifying 403/404 on cross-user trip access | M4 | Survey (explorer_survey_3) & R4 |
| 15 | Automated Concurrency Tests | Verify stale updates fail with concurrency exception without DB corruption | M4 | Survey (explorer_survey_3) & R4 |
| 16 | Automated SignalR Authorization Tests | Verify `JoinTrip` and `SendLocationUpdate` authorization and group isolation | M4 | Survey (explorer_survey_3) & R4 |
| 17 | Automated PDF Invoicing Tests | Verify application/pdf byte stream, non-zero length, metadata, and status validation | M4 | Survey (explorer_survey_3) & R4 |
| 18 | Build & Test Quality Gate | 0 build errors, 0 build warnings (`--warnaserror`), 100% tests passing in CI | M4 | Survey (explorer_survey_3) & Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Access Control, Concurrency & Security Hardening | Features 1-5: IDOR fix in `CustomerController.OrderSuccess`, `ConcurrencyToken` on `Trip`/`DriverTrip`, concurrency exception handling in controllers, atomic transaction in `AssignTrip`, input sanitization, global antiforgery attribute, and `TripStatus.InProgress` support | none | DONE |
| M2 | Live Driver GPS & Map Tracking Simulation | Features 6-10: Leaflet 1.9.4 integration, `ICoordinateService`, `TripHub.SendLocationUpdate` with authorization and group broadcast, driver GPS simulation controls, and view updates | M1 | DONE |
| M3 | Automated PDF Invoicing & Financial Summary | Features 11-13: PDF library reference, `IInvoiceService` / `PdfInvoiceService` with itemized breakdown and metadata, `DownloadInvoice` actions in Customer/Admin controllers with authorization, and UI download buttons | M1 | DONE |
| M4 | Automated Test Coverage & Quality Verification | Features 14-18: Add `AccessControlAndIdorTests`, `ConcurrencyTests`, `SignalRTrackingHubTests`, `InvoiceGenerationTests` in `tests/LogiTrack.Tests`, verify 0 warnings/errors, 100% passing tests | M1, M2, M3 | DONE |
| M5 | Adversarial Hardening & Forensic Integrity Audit | White-box adversarial testing, comprehensive integrity forensic audit, and victory verification | M4 | IN_PROGRESS |

## Interface Contracts
### Security & Concurrency Contracts
- `Trip` and `DriverTrip`:
  - `[ConcurrencyCheck] public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();`
- `ApplicationDbContext.SaveChangesAsync`:
  - Automatically updates `ConcurrencyToken = Guid.NewGuid()` for modified `Trip` and `DriverTrip` entries before `base.SaveChangesAsync()`.
- Controllers catching `DbUpdateConcurrencyException`:
  - `AdminController.SetPrice`, `AdminController.ReviewTrip`, `AdminController.AssignTrip`
  - `CustomerController.RespondToPrice`
  - `DriverController.EndTrip`
  - Returns `Conflict("The trip was modified by another process. Please refresh and try again.")` or adds ModelState error and reloads view.

### Live GPS & Tracking Contracts
- `ICoordinateService`:
  - `Task<Coordinates> GetCoordinatesForCityAsync(string city);`
  - `Coordinates GetCoordinatesForCity(string city);`
  - Records: `public record Coordinates(double Latitude, double Longitude);`
- `TripHub`:
  - `public async Task JoinTrip(int tripId)`: checks participant/admin authorization, joins group `trip_{tripId}`.
  - `public async Task LeaveTrip(int tripId)`: leaves group `trip_{tripId}`.
  - `public async Task SendLocationUpdate(int tripId, double latitude, double longitude)`: verifies caller is assigned driver (or admin), verifies trip status is `Assigned` or `InProgress`, validates latitude [-90, 90] and longitude [-180, 180], transitions trip status to `InProgress` if currently `Assigned`, updates driver current location, and broadcasts `ReceiveLocationUpdate(tripId, latitude, longitude)` to group `trip_{tripId}`.

### PDF Invoicing Contracts
- `IInvoiceService`:
  - `byte[] GenerateInvoicePdf(Trip trip, Customer customer);`
- Invoicing Endpoints:
  - `CustomerController.DownloadInvoice(int id)`: returns `File(pdfBytes, "application/pdf", $"Invoice_Trip_{trip.Id}.pdf")`. Returns 404/403 if trip does not exist, is not completed, or belongs to another customer.
  - `AdminController.DownloadInvoice(int id)`: returns `File(pdfBytes, "application/pdf", $"Invoice_Trip_{trip.Id}.pdf")`. Returns 404 if trip does not exist or is not completed.

## Code Layout
- `Controllers/`:
  - `AdminController.cs`: concurrency handling on `SetPrice`, `ReviewTrip`, `AssignTrip`; `DownloadInvoice(int id)`.
  - `CustomerController.cs`: IDOR fix on `OrderSuccess`; concurrency handling on `RespondToPrice`; `DownloadInvoice(int id)`; input sanitization on `SubmitTrip`.
  - `DriverController.cs`: allow `EndTrip` on `Assigned` and `InProgress`.
- `Models/`:
  - `Trip.cs`: add `[ConcurrencyCheck] public Guid ConcurrencyToken`.
  - `DriverTrip.cs`: add `[ConcurrencyCheck] public Guid ConcurrencyToken`.
  - `Location.cs`: coordinates for driver.
- `Services/`:
  - `ICoordinateService.cs` & `CityCoordinateService.cs`: city to lat/lng resolution.
  - `IInvoiceService.cs` & `PdfInvoiceService.cs`: PDF generation with itemized fare breakdown and metadata.
  - `InputSanitizer.cs`: user input sanitization utility.
- `Hubs/`:
  - `TripHub.cs`: `SendLocationUpdate` method with strict authorization and group broadcast.
- `Views/`:
  - `Customer/TripDetails.cshtml`, `Customer/MyTrips.cshtml`: map container, invoice download buttons.
  - `Driver/CurrentTrips.cshtml`: map container, live GPS simulation controls.
  - `Admin/TripDetails.cshtml`, `Admin/InvoiceGeneration.cshtml`: map container, invoice download links.
- `wwwroot/`:
  - `lib/leaflet/`: Leaflet 1.9.4 CSS and JS.
  - `js/tripTracking.js`: client-side map rendering and SignalR location receiver.
  - `js/driverSimulation.js`: client-side GPS simulation with waypoint interpolation and SignalR publisher.
- `tests/LogiTrack.Tests/`:
  - `ApplicationSmokeTests.cs`: existing 21 tests.
  - `AccessControlAndIdorTests.cs`: IDOR and access control coverage.
  - `ConcurrencyTests.cs`: optimistic concurrency and stale update rejection tests.
  - `SignalRTrackingHubTests.cs`: SignalR authorization and broadcast scoping tests.
  - `InvoiceGenerationTests.cs`: PDF byte stream, metadata, and status validation tests.
