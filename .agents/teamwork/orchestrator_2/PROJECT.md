# Master Project: Logi-Track TMS Overhaul for Rufta Logistics LLC

## Architecture
Logi-Track is an ASP.NET Core 8 MVC web application with Entity Framework Core, ASP.NET Core Identity (Admin, Driver, Customer), Leaflet GPS mapping, SignalR telemetry, and PDF generation.
This overhaul transforms Logi-Track into a complete US motor carrier transport management system (TMS) for Rufta Logistics LLC.

### Key Architectural Pillars:
1. **Fleet & Equipment Management (M1)**: `Vehicle` entity tracking Unit Number (e.g. 4604, 1717, 6328), 17-char VIN, License Plate/State, Make, Model, Year, Registration Expiration, Annual DOT Inspection, IRS Form 2290 Expiration, PrePass Transponder ID, Status (`Active`, `Maintenance`, `Decommissioned`), and Assigned Driver. Admin Fleet Management views and tractor dispatch assignment.
2. **Broker CRM & Bad Broker Protection (M2)**: `Broker` entity tracking Legal Name, FMCSA MC Number (e.g. `MC#322572`), primary agent, direct phone/ext, dispatch email, `IsBlacklisted`, and `BlacklistReason`. Real-time visual alert warnings on dispatch boards whenever a blacklisted broker is selected.
3. **Automated 12%/88% Load Accounting & Driver Settlements (M3)**: Dual load IDs (`InternalInvoiceNumber` e.g. `IN001` and `BrokerLoadNumber` e.g. `23044790`), accessorials (`DetentionAmount`, `LumperAmount`, `TonuAmount`, `DeductionsAmount`), `TripStatus.TONU` transition ($150-$200 payout), `SettlementCalculator` (1.5% factoring fee, 12.0% dispatch fee, 88.0% net base driver pay), and `ISettlementService` / `PdfSettlementService` generating settlement statement vouchers.
4. **DOT Safety & Regulatory Expiration Tracking (M4)**: Driver CDL number, state, and DOT Medical Examination Card expiration (49 CFR 391.41). Hard dispatch validation gate preventing dispatch of drivers with expired medical cards. Automated dashboard warning banners for Medical cards, annual vehicle DOT inspections, quarterly IFTA tax deadlines, and IRS Form 2290 renewals.
5. **Comprehensive Automated Test Coverage & Quality Verification (M5)**: Complete test coverage in `tests/LogiTrack.Tests`, elimination of test parallelization race conditions via `xunit.runner.json`, strict `--warnaserror` build with 0 warnings/errors, and 100% test pass rate.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Vehicle Entity & EF Migration | Model `Vehicle` with UnitNumber, VIN, Plate, State, Make, Model, Year, Expirations, Status, EF Core migration | M1 | Survey (explorer_tms_1) & R1 |
| 2 | Fleet Equipment Management UI | Admin views to list, create, edit, deactivate vehicles, and assign/unassign tractors to drivers | M1 | Survey (explorer_tms_1) & R1 |
| 3 | Tractor Unit Dispatch Integration | Require selecting an active Tractor Unit during trip dispatch, linking `VehicleId` on Trip and DriverTrip | M1 | Survey (explorer_tms_1, explorer_tms_2) & R1 |
| 4 | Fleet Seed Data | Seed Rufta Logistics fleet units (4604, 1717, 6328) with realistic VINs, plates, and statuses | M1 | Survey (explorer_tms_1) & R1 |
| 5 | Broker CRM Entity & Directory | Model `Broker` with MC#, legal name, agent, phone, email, blacklist flag, and reason | M2 | Survey (explorer_tms_1) & R2 |
| 6 | Broker Management UI | Admin views to list, create, edit brokers and toggle blacklist status with issue reason | M2 | Survey (explorer_tms_1) & R2 |
| 7 | Bad Broker Visual Alert on Dispatch | Prominent visual alert warning on dispatch board and trip review when blacklisted broker selected | M2 | Survey (explorer_tms_1, explorer_tms_2) & R2 |
| 8 | Broker Seed Data | Seed freight brokers (C.H. Robinson, TQL, and blacklisted Apex Freight Direct) | M2 | Survey (explorer_tms_1) & R2 |
| 9 | Dual Load Identification | Persist `InternalInvoiceNumber` (IN001) and `BrokerLoadNumber` (23044790) on Trip | M3 | Survey (explorer_tms_2) & R3 |
| 10 | Accessorial Charges & TONU Status | Support Detention, Lumper, Deductions, and `TripStatus.TONU` with standard $150-$200 payout | M3 | Survey (explorer_tms_2) & R3 |
| 11 | Settlement Calculation Engine | `SettlementCalculator` with Gross Rate, 1.5% factoring, 12% dispatch fee, and 88% net driver pay with decimal precision | M3 | Survey (explorer_tms_2) & R3 |
| 12 | Driver Settlement Statement Vouchers | Dedicated `ISettlementService` / `PdfSettlementService` generating downloadable settlement vouchers for Admin and Driver | M3 | Survey (explorer_tms_2) & R3 |
| 13 | Payment Period TONU Aggregation | Update `PreparePayments` to include completed and TONU trips in driver compensation settlement batches | M3 | Survey (explorer_tms_2) & R3 |
| 14 | Driver CDL & Medical Card Persistence | Add `CdlNumber`, `CdlIssuingState`, and `DotMedicalCardExpirationDate` (49 CFR 391.41) to `Driver` | M4 | Survey (explorer_tms_3) & R4 |
| 15 | Driver Management Compliance UI | Update driver create/edit views and driver list with CDL details and Medical Card status badges | M4 | Survey (explorer_tms_3) & R4 |
| 16 | Dispatch Medical Compliance Gate | Reject/warn dispatchers attempting to assign a driver with expired DOT Medical Card | M4 | Survey (explorer_tms_3) & R4 |
| 17 | Automated Dashboard Warning Banners | Compute and display warning banners for Med cards, vehicle DOT inspections, quarterly IFTA, and Form 2290 | M4 | Survey (explorer_tms_3) & R4 |
| 18 | Test Parallelization Stabilization | Add `xunit.runner.json` to eliminate SQLite/PdfSharp collection concurrency races | M5 | Survey (explorer_tms_3) & R5 |
| 19 | Comprehensive TMS Unit & Integration Tests | Author complete test suites covering Fleet, Broker, Settlements, and DOT Compliance | M5 | Survey (explorer_tms_3) & R5 |
| 20 | Build & Quality Verification Gate | Verify `dotnet build --warnaserror` (0 warnings/errors) and `dotnet test` (100% pass) | M5 | Survey (explorer_tms_3) & Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Fleet & Equipment Management | Features 1-4: `Vehicle` model, DbContext/Migrations, Admin Fleet CRUD, driver tractor assignment, dispatch tractor selection, seed fleet | none | PLANNED |
| M2 | Broker CRM & Bad Broker Protection | Features 5-8: `Broker` model, Admin Broker CRUD, Bad Broker blacklist warnings on dispatch board, broker seed data | M1 | PLANNED |
| M3 | Load Accounting & Driver Settlements | Features 9-13: Dual load IDs, accessorials, `TripStatus.TONU`, `SettlementCalculator` (12%/88%), `ISettlementService` settlement voucher PDF & downloads | M1, M2 | PLANNED |
| M4 | DOT Safety & Regulatory Compliance | Features 14-17: Driver CDL & Medical card 49 CFR 391.41, dispatch medical gate, 4-part dashboard warning banners | M1 | PLANNED |
| M5 | Comprehensive Test Suite & Quality Verification | Features 18-20: `xunit.runner.json`, complete tests for R1-R4, 0 warnings/errors build, 100% pass, integrity audit | M1, M2, M3, M4 | PLANNED |

## Interface Contracts
### Fleet & Equipment Contracts
- `Vehicle`:
  - `Id`, `UnitNumber`, `Vin` (17 chars), `LicensePlate`, `LicenseState`, `Make`, `Model`, `Year`, `RegistrationExpirationDate`, `AnnualDotInspectionExpiration`, `Form2290ExpirationDate`, `PrePassTransponderId`, `Status` (`Active`, `Maintenance`, `Decommissioned`), `AssignedDriverId`.
- `Trip` and `DriverTrip`:
  - `int? VehicleId`, `Vehicle? Vehicle`
- `Driver`:
  - `int? AssignedVehicleId`, `Vehicle? AssignedVehicle`

### Broker CRM Contracts
- `Broker`:
  - `Id`, `CompanyName`, `McNumber` (e.g. `MC#322572`), `PrimaryAgentName`, `PhoneNumber`, `DispatchEmail`, `IsBlacklisted`, `BlacklistReason`, `Notes`.
- `Trip`:
  - `int? BrokerId`, `Broker? Broker`

### Accounting & Settlement Contracts
- `Trip`:
  - `string InternalInvoiceNumber`, `string? BrokerLoadNumber`
  - `double DetentionAmount`, `double LumperAmount`, `double TonuAmount`, `double DeductionsAmount`, `string? DeductionReason`
- `TripStatus`:
  - Added enum value: `TONU`
- `SettlementCalculator`:
  - `SettlementCalculationResult Calculate(decimal rateConfirmation, decimal detention, decimal lumper, decimal tonu, decimal deductions, bool isTonu)`
  - Constants: `FactoringFeeRate = 0.015m`, `DispatchFeeRate = 0.120m`, `DriverBasePayRate = 0.880m`
- `ISettlementService`:
  - `SettlementCalculationResult CalculateTripSettlement(Trip trip)`
  - `SettlementCalculationResult CalculatePaymentSettlement(Payment payment)`
  - `byte[] GenerateSettlementPdf(Payment payment)`
  - `byte[] GenerateTripSettlementPdf(DriverTrip driverTrip)`

### DOT Safety & Compliance Contracts
- `Driver`:
  - `string CdlNumber`, `string CdlIssuingState`, `DateTime? DotMedicalCardExpirationDate`
- `AdminController.AssignTrip`:
  - Validates `driver.DotMedicalCardExpirationDate.HasValue && driver.DotMedicalCardExpirationDate.Value.Date < DateTime.UtcNow.Date` -> rejects/warns under 49 CFR 391.41.
- `DashboardStats`:
  - `List<ComplianceAlert> ComplianceAlerts` with alerts for Medical Cards, Vehicle DOT Inspections, Quarterly IFTA, and IRS Form 2290.

## Code Layout
- `Models/`:
  - `Vehicle.cs`: Fleet power unit model.
  - `Broker.cs`: Freight broker CRM model.
  - `Driver.cs`: CDL and Medical Card compliance extensions.
  - `Trip.cs`: Dual load IDs, accessorials, VehicleId, BrokerId, TONU status.
  - `DriverTrip.cs`: VehicleId.
  - `ComplianceAlert.cs`: Strongly-typed compliance notifications.
- `Services/`:
  - `SettlementCalculator.cs`: Pure math engine for 12%/88% settlements.
  - `ISettlementService.cs` & `PdfSettlementService.cs`: Settlement statement PDF generation.
- `Controllers/`:
  - `AdminController.cs`: Vehicle CRUD, Broker CRUD, dispatch validation, TONU action, settlement downloads, compliance dashboard banners.
  - `DriverController.cs`: Driver settlement download action with IDOR check.
- `Views/`:
  - `Admin/Vehicles/` or `Admin/VehicleManagement.cshtml`, `CreateVehicle.cshtml`, `EditVehicle.cshtml`
  - `Admin/Brokers/` or `Admin/BrokerManagement.cshtml`, `CreateBroker.cshtml`, `EditBroker.cshtml`
  - `Admin/AssignTrip.cshtml` & `AddNewTripByAdmin.cshtml`: Tractor selection, Bad broker warning, medical card check.
  - `Admin/Dashboard.cshtml`: Compliance warning banners.
- `Data/`:
  - `ApplicationDbContext.cs`: DbSets for Vehicles and Brokers, relationships and configurations.
  - `SeedData.cs`: Initial fleet and broker seed data.
  - `Migrations/`: `AddFleetAndBrokerEntities` migration.
- `tests/LogiTrack.Tests/`:
  - `xunit.runner.json`: Disable parallelization for deterministic test runs.
  - `VehicleManagementTests.cs`, `BrokerManagementTests.cs`, `SettlementCalculationTests.cs`, `DriverComplianceTests.cs`, `TripDispatchLifecycleTests.cs`.
