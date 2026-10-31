# Original User Request

## Initial Request — 2026-10-27T19:10:49Z

Perform a production-grade overhaul of Logi-Track to resolve security/workflow gaps, introduce live driver GPS tracking simulation on interactive maps, and implement automated PDF invoicing.

Working directory: C:\Users\naib\Documents\projects\Logi-Track
Integrity mode: development

## Requirements

### R1. Access Control, Concurrency & Security Hardening
Harden the trip management lifecycle against common vulnerabilities:
- Enforce strict ownership and role verification on all trip viewing and modification actions so customers and drivers can never access or modify trips belonging to others.
- Prevent race conditions during concurrent admin reviews, pricing submissions, or driver assignments using optimistic concurrency or transaction guards.
- Verify CSRF protection on all mutating POST/PUT actions and sanitize user-provided addresses and notes.

### R2. Live Driver GPS & Map Tracking Simulation
Provide real-time location visualization and updates during active trips:
- Render an interactive map for active trips displaying route points (pickup location, destination, and current vehicle position).
- Allow drivers on active trips to emit simulated GPS coordinate updates via SignalR, broadcasting updates to authorized viewing customers and admins in real time.

### R3. Automated PDF Invoicing & Financial Summary
Provide downloadable invoices and payment records upon trip completion:
- Generate clean, formatted PDF invoices for completed trips detailing trip ID, customer information, timestamps, itemized fare breakdown, and payment status.
- Allow customers to download their trip invoice from their trip history and admins from the billing overview.

### R4. Automated Test Coverage
Expand the existing test suite in tests/LogiTrack.Tests to cover the new security constraints, live tracking hub authorization, and PDF invoice generation.

## Acceptance Criteria

### Security & State Integrity
- [ ] Direct object reference (IDOR) attempts (e.g. requesting another user's trip details via URL manipulation) return 403 Forbidden or 404 Not Found.
- [ ] Concurrent state modification tests verify that stale trip updates are rejected without corrupting database state.

### Map & Real-Time Tracking
- [ ] Active trips load an interactive map showing start, destination, and live position markers without JavaScript console errors.
- [ ] SignalR connection tests confirm location broadcast events are delivered only to users authorized for that specific trip.

### Invoicing & Builds
- [ ] Requesting an invoice download for any completed trip returns a valid application/pdf byte stream with non-zero length and expected metadata.
- [ ] dotnet build succeeds with 0 warnings and 0 errors.
- [ ] dotnet test executes all existing and newly added unit/integration tests with 100% passing results.


## Follow-up — 2026-10-27T20:46:20Z

Transform Logi-Track into a complete US motor carrier transport management system (TMS) for Rufta Logistics LLC, adding Fleet & Equipment Management, a Broker CRM with MC# lookups and Bad Broker protections, automated 12%/88% load settlement accounting (TONU, detention, lumpers, factoring), and DOT safety compliance tracking.

Working directory: C:\Users\naib\Documents\projects\Logi-Track
Integrity mode: development

## Requirements

### R1. Fleet & Equipment Management
Implement full power unit and equipment tracking:
- Model and persist commercial vehicles (`Vehicle`) with Unit Number (e.g. 4604, 1717, 6328), 17-character VIN, License Plate, License State, Make (Freightliner, Volvo, Kenworth), Model, Year, Registration Expiration Date, and PrePass Transponder ID.
- Enable assigning and unassigning tractors to drivers, tracking vehicle statuses (`Active`, `Maintenance`, `Decommissioned`), and selecting both Driver and Tractor Unit during trip dispatch.

### R2. Broker CRM & "Bad Broker" Protection
Implement an industry-standard freight broker directory:
- Model freight brokers (`Broker`) storing Company Legal Name, FMCSA MC Number (e.g. `MC#322572`), primary agent name, direct phone/extension, and dispatch email.
- Include a Bad Broker Blacklist flag with issue notes (e.g., payment default, claim disputes).
- Provide visual alert warnings on the dispatch board whenever a dispatcher selects a blacklisted broker.

### R3. Automated Load Accounting & Driver Settlements
Automate freight financial accounting matching motor carrier operations:
- Support dual load identification: Internal Invoice Number (e.g. `IN001`) and Broker Load / Reference Number (e.g. `23044790`).
- Implement accessorial charges: Truck Order Not Used (TONU with standard $150-$200 payout), Detention fees, Lumper payments, and claim deductions.
- Automate load settlement calculations:
  - `Gross Rate = Rate Confirmation + Added Accessorials`
  - `Factoring Fee = 1.5%`
  - `Dispatch Fee = 12.0%` of Gross Rate
  - `Driver Net Pay = Gross Rate - Dispatch Fee - Deductions`
- Generate printable and downloadable Driver Settlement Statement vouchers for completed trip periods.

### R4. DOT Safety & Regulatory Expiration Tracking
Implement regulatory compliance safeguards:
- Track driver Commercial Driver License (CDL) numbers, license issuing state, and DOT Medical Examination Card expiration dates (49 CFR 391.41).
- Provide automated dashboard warning banners for upcoming expirations (Medical cards, annual vehicle DOT inspections, quarterly IFTA tax deadlines, and IRS Form 2290 renewals).
- Warn dispatchers when attempting to assign a driver whose DOT Medical Card has expired.

### R5. Comprehensive Test Suite
Add unit and integration tests to `tests/LogiTrack.Tests` covering vehicle assignment constraints, broker MC lookup and blacklist alerts, financial settlement math, and medical card expiration validation.

## Acceptance Criteria

### Fleet & Equipment
- [ ] Admins can create, view, edit, and deactivate vehicle units with complete VIN, plate, and registration expiration details.
- [ ] Trip dispatch interfaces require selecting both an active Driver and an active Vehicle Unit.

### Broker Directory & Safety
- [ ] Creating or reviewing a trip displays the broker's MC number and contact details.
- [ ] Attempting to dispatch a load with a blacklisted broker displays a prominent warning with the blacklist reason.

### Financial Settlement Math
- [ ] Settlement calculation tests verify that `Gross Rate`, `Factoring Fee (1.5%)`, `Dispatch Fee (12%)`, and `Driver Net Pay (88% - deductions)` compute with decimal precision without rounding drift.
- [ ] Transitioning a trip to `TONU` sets the agreed TONU payout amount and updates driver compensation accordingly.
- [ ] Drivers and Admins can view and download formatted settlement breakdown statements.

### Compliance & Quality
- [ ] Attempting to dispatch a driver with an expired DOT Medical Card produces a validation warning.
- [ ] `dotnet build` succeeds with 0 warnings and 0 errors under `--warnaserror`.
- [ ] `dotnet test` executes all existing and newly authored test suites with 100% passing results.
