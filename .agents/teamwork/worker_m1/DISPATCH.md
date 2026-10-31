## 2026-10-27T21:02:40Z
You are worker_m1, an implementation worker for the Logi-Track TMS overhaul.

Your working directory is:
C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\worker_m1

The project root is:
C:\Users\naib\Documents\projects\Logi-Track

Read the authoritative user request at:
C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\ORIGINAL_REQUEST.md
specifically section '## Follow-up — 2026-10-27T20:46:20Z' -> '### R1. Fleet & Equipment Management'.

Also read the architecture specifications in:
C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\orchestrator_2\PROJECT.md
and the detailed technical design in:
C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\explorer_tms_1\handoff.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Your task for Milestone M1 (Fleet & Equipment Management):
1. Create `Models/Vehicle.cs`:
   - Properties: UnitNumber (e.g. "4604", "1717", "6328"), 17-char VIN with regex validation (`^[A-HJ-NPR-Z0-9]{17}$`), LicensePlate, LicenseState, Make, Model, Year, RegistrationExpirationDate, AnnualDotInspectionExpiration, Form2290ExpirationDate, PrePassTransponderId, Status (`VehicleStatus` enum: `Active`, `Maintenance`, `Decommissioned`), AssignedDriverId, Timestamps, navigation properties.
2. Extend `Models/Trip.cs`, `Models/DriverTrip.cs`, and `Models/Driver.cs` with `VehicleId` and `Vehicle` navigation properties.
3. Update `Data/ApplicationDbContext.cs`:
   - Add `DbSet<Vehicle> Vehicles { get; set; }`.
   - In `OnModelCreating`, configure `Vehicle` entity: unique indexes on `UnitNumber` and `Vin`, relationships to Driver, Trip, and DriverTrip (SetNull on delete).
4. Generate EF Core migration using `dotnet ef migrations add AddFleetAndEquipmentManagement` and verify it applies cleanly.
5. In `Data/SeedData.cs`, seed the initial Rufta Logistics LLC fleet units (Units 4604, 1717, 6328) with realistic VINs, plates, statuses, and expiration dates if `Vehicles` table is empty.
6. In `Controllers/AdminController.cs`:
   - Implement Fleet Management actions: `VehicleManagement` (or `Vehicles`), `CreateVehicle` [GET/POST], `EditVehicle` [GET/POST], `SetVehicleStatus` [POST], `AssignDriverToVehicle` [POST], `UnassignVehicleDriver` [POST].
   - Update `AssignTrip` [GET]: Populate `AvailableVehicles` with vehicles where `Status == VehicleStatus.Active`.
   - Update `AssignTrip` [POST]: Require an active vehicle. (Backwards compatibility note: if `VehicleId` is null/zero in legacy test invocations, auto-bind to the driver's assigned tractor or the first active vehicle to prevent breaking existing tests; but in normal/UI flow, validate and enforce active vehicle).
7. Create Admin Fleet Views:
   - `Views/Admin/VehicleManagement.cshtml` (or `Views/Admin/Vehicles/Index.cshtml`) with status badges (Active/Maintenance/Decommissioned) and expiration warnings.
   - `CreateVehicle.cshtml` and `EditVehicle.cshtml`.
   - Update `Views/Admin/AssignTrip.cshtml` to include a Tractor Unit dropdown.
   - Update `Views/Shared/_Layout.cshtml` to add a "Fleet Management" link in the Admin navigation bar.
8. Verification:
   - Run `dotnet build --warnaserror` (MUST pass with 0 warnings, 0 errors).
   - Run `dotnet test` (ensure all tests pass).
   - Document commands executed and exact outputs.

Write your report to `C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\worker_m1\handoff.md`.
When finished, send a message to orchestrator with summary and report path.
