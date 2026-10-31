# BRIEFING — 2026-10-27T20:57:00Z

## Mission
Investigate and produce comprehensive architectural and technical specifications for Automated Load Accounting & Driver Settlements (R3) and Trip Dispatch Lifecycle Integration for the Logi-Track TMS transformation.

## 🔒 My Identity
- Archetype: explorer
- Roles: investigation, synthesis
- Working directory: C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\explorer_tms_2
- Original parent: 96e0bcea-7c2d-416b-9b35-3e1615879d11
- Milestone: TMS Transformation Phase 1 - Architecture & Design (R3 & Trip Lifecycle)

## 🔒 Key Constraints
- Read-only investigation — do NOT modify any project source code.
- Write only to working directory: C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\explorer_tms_2
- Produce 5-component handoff report (handoff.md).

## Current Parent
- Conversation ID: 96e0bcea-7c2d-416b-9b35-3e1615879d11
- Updated: not yet

## Investigation State
- **Explored paths**:
  - `Models/Trip.cs`, `Models/DriverTrip.cs`, `Models/TripStatus.cs`, `Models/Payment.cs`, `Models/PaymentPeriod.cs`, `Models/Driver.cs`
  - `Controllers/AdminController.cs`, `Controllers/DriverController.cs`, `Controllers/CustomerController.cs`
  - `Services/IInvoiceService.cs`, `Services/PdfInvoiceService.cs`
  - `Data/ApplicationDbContext.cs`, `Data/SeedData.cs`, `Migrations/`
  - `tests/LogiTrack.Tests/` (ran baseline tests: 280/280 passing)
  - `Views/Admin/AssignTrip.cshtml`, `AddNewTripByAdmin.cshtml`, `PaymentManagement.cshtml`, `Invoice.cshtml`
  - `Views/Driver/PaymentTracking.cshtml`
- **Key findings**:
  1. `Trip` & `DriverTrip` models:
     - Require dual load identification: `InternalInvoiceNumber` (e.g. `IN001`) and `BrokerLoadNumber` (e.g. `23044790`).
     - Accessorials: `DetentionAmount`, `LumperAmount`, `TonuAmount`, `DeductionsAmount`, `DeductionReason`.
     - `TripStatus` must include `TONU`.
     - `DriverTrip` must link to `VehicleId` (Tractor Unit).
  2. Load Accounting Math:
     - Gross Rate = Rate Confirmation (or TONU amount) + Added Accessorials (Detention + Lumper).
     - Factoring Fee = 1.5% of Gross Rate.
     - Dispatch Fee = 12.0% of Gross Rate.
     - Driver Net Pay = Gross Rate - Dispatch Fee - Deductions = (88% net base) - Deductions.
     - TONU standard payout is $150–$200; driver net pay is $150–$200 * 0.88.
     - Strict `decimal` arithmetic prevents IEEE-754 rounding drift.
  3. Driver Settlement Statement Voucher:
     - Dedicated `ISettlementService` & `PdfSettlementService` using `PdfSharpCore` recommended to cleanly separate carrier-to-driver compensation statements from customer billing invoices.
     - PDF & HTML vouchers formatted with Rufta Logistics branding, driver CDL, unit #, and itemized load table.
     - Endpoints for download by Admin and Driver (with IDOR protection).
  4. Trip Dispatch Lifecycle Integration:
     - `AssignTrip` must require selecting both active Driver and active Tractor Unit (`VehicleId`).
     - Displays broker MC# and warns if broker is blacklisted.
     - Validates Driver DOT Medical Card expiration.
     - Transition to TONU frees driver/tractor, sets payout, updates driver payment to 88%.
     - `PreparePayments` updated to include both Completed and TONU trips.
- **Unexplored areas**: None within scope.

## Key Decisions Made
- Recommended separate `ISettlementService` rather than overloading `IInvoiceService`.
- Model extension proposals use non-destructive additive schema with proper decimal arithmetic.
- Full 5-component handoff report completed in `handoff.md`.

## Artifact Index
- DISPATCH.md — Dispatch log
- BRIEFING.md — Working memory and context
- progress.md — Liveness heartbeat
- handoff.md — Final 5-component handoff report
