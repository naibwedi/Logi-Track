# Master TMS Transformation Plan — Logi-Track

## Objective
Transform Logi-Track into a complete US motor carrier transport management system (TMS) for Rufta Logistics LLC, adding Fleet & Equipment Management, a Broker CRM with MC# lookups and Bad Broker protections, automated 12%/88% load settlement accounting (TONU, detention, lumpers, factoring), and DOT safety compliance tracking.

## Standards
- Strict compilation: `dotnet build` with 0 warnings and 0 errors under `--warnaserror`.
- Test verification: `dotnet test` with 100% passing rate.
- Zero tolerance on hardcoding or facade implementations (Forensic Auditor verification).

## Phase 0: Discovery & Codebase Survey
- Spawn 3 Explorers in parallel:
  - `explorer_tms_1`: Models, DbContext, Migrations, Fleet/Vehicle & Broker entities.
  - `explorer_tms_2`: Trip dispatch lifecycle, Accessorials, Financial math (12%/88%), Settlement generation.
  - `explorer_tms_3`: Driver compliance (CDL, DOT Med card 49 CFR 391.41, alerts), existing tests, build settings.
- Synthesize findings into updated `PROJECT.md` Feature Inventory & Architecture.

## Phase 1: Milestone M1 — Fleet & Equipment Management
- Entity: `Vehicle` (UnitNumber, VIN, Plate, State, Make, Model, Year, RegistrationExpiration, PrePassTransponderId, Status: Active/Maintenance/Decommissioned, AssignedDriverId).
- CRUD views & controller logic for Admins.
- Dispatch validation: active vehicle + active driver selection on trip creation/dispatch.
- Explorer → Worker → Reviewers (2) → Challengers (2) → Auditor → Gate.

## Phase 2: Milestone M2 — Broker CRM & Bad Broker Protection
- Entity: `Broker` (LegalName, MCNumber e.g. MC#322572, ContactAgent, Phone, Email, IsBlacklisted, BlacklistReason).
- Broker management CRUD views & controller.
- Dispatch board integration: Broker selection with MC# lookup and prominent visual warning banner if blacklisted.
- Explorer → Worker → Reviewers (2) → Challengers (2) → Auditor → Gate.

## Phase 3: Milestone M3 — Load Accounting & Driver Settlements
- Trip fields: InternalInvoiceNumber (e.g. IN001), BrokerReferenceNumber (e.g. 23044790).
- Accessorial charges & TONU status: TONU ($150-$200), Detention, Lumper, Deductions.
- Math engine:
  - Gross Rate = Rate Confirmation + Accessorials
  - Factoring Fee = 1.5% of Gross Rate
  - Dispatch Fee = 12.0% of Gross Rate
  - Driver Net Pay = Gross Rate - Dispatch Fee - Deductions (88% net base)
- Settlement statement voucher generation (view & download).
- Explorer → Worker → Reviewers (2) → Challengers (2) → Auditor → Gate.

## Phase 4: Milestone M4 — DOT Safety & Regulatory Compliance
- Driver model extensions: CDL number, CDL state, Medical Card expiration date.
- Dashboard warning banners: Medical cards, annual vehicle DOT inspections, quarterly IFTA tax deadlines, IRS Form 2290 renewals.
- Dispatcher validation warning when assigning driver with expired DOT Medical card.
- Explorer → Worker → Reviewers (2) → Challengers (2) → Auditor → Gate.

## Phase 5: Milestone M5 — Comprehensive Test Suite & Quality Verification
- Expand `tests/LogiTrack.Tests`:
  - Vehicle assignment & status constraints.
  - Broker MC lookup & blacklist alerts.
  - Financial settlement math with decimal precision & TONU payout.
  - DOT medical card expiration & regulatory compliance alerts.
- Verify `dotnet build --warnaserror` (0 warnings, 0 errors).
- Verify `dotnet test` (100% pass rate).
- Full Forensic Integrity Audit.
- Victory claim to Sentinel.
