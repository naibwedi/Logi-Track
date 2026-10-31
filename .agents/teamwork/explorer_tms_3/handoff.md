# Exploration & Technical Specification Report: DOT Safety & Regulatory Expiration Tracking (R4) & Test Architecture / Build Setup (R5)

**Author:** `explorer_tms_3`  
**Date:** 2026-10-27  
**Working Directory:** `C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\explorer_tms_3`  
**Target Solution:** Logi-Track Motor Carrier TMS Overhaul (Rufta Logistics LLC)  

---

## 1. Observation

### 1.1 Driver Entity & Profile Models
1. **`Models/Driver.cs` (lines 11–25)**:
   ```csharp
   public class Driver : ApplicationUser
   {
       [Required]
       [Range(0, double.MaxValue, ErrorMessage = "Please enter a valid price.")]
       public double PricePerKm { get; set; }
       [Required]
       public PaymentFreq PaymentFreq { get; set; }
       public string DriverEmail { get; set; } = string.Empty;
       public bool IsAvailable { get; set; }
       public List<DriverTrip> DriverTrips { get; set; } = [];
       public List<PaymentPeriod> PaymentPeriods { get; set; } = [];
       public Address PermanentAddress { get; set; } = null!;
       public Location CurrentLocation { get; set; } = null!;
       public IEnumerable<Payment>? Payments { get; set; }
   }
   ```
   - **Direct Observation**: The `Driver` entity contains **zero** commercial driving regulatory fields. There is no Commercial Driver License number (`CdlNumber`), no license issuing state (`CdlIssuingState`), and no DOT Medical Examination Card expiration date (`DotMedicalCardExpirationDate` under 49 CFR 391.41).

2. **`Models/ViewModel/CreateDriverViewModel.cs` (lines 7–63)** & **`Models/ViewModel/EditDriverViewModel.cs` (lines 7–40)**:
   - `CreateDriverViewModel` collects: `DriverEmail`, `FirstName`, `LastName`, `PricePerKm`, `PhoneNumber`, `PaymentFreq`, `DateOfBirth`, `Password`, `ConfirmPassword`.
   - `EditDriverViewModel` collects: `id`, `FirstName`, `LastName`, `Email`, `PhoneNumber`, `PricePerKm`, `PaymentFreq`, `IsAvailable`.
   - Neither ViewModel contains inputs for CDL Number, Issuing State, or Medical Card Expiration Date.

3. **`Controllers/AdminController.cs`**:
   - `CreateDriver` (POST, lines 110–177): Instantiates `new Driver` mapping only basic Identity attributes and `PricePerKm`, `PaymentFreq`, `DateOfBirth`.
   - `EditDriver` (GET, lines 271–296; POST lines 304–358): Reads and modifies only basic profile fields.
   - `_DriverManagmentPartial.cshtml` (lines 3–14): Renders a table with headers: First Name, Last Name, Email, Phone Number, Price per Km, Payment Frequency, Actions. No compliance or license status indicators exist.

4. **`Areas/Identity/Pages/Account/Register.cshtml.cs` (lines 106–130)**:
   - Self-registration is strictly hardcoded to create the `Customer` role (`await _userManager.AddToRoleAsync(user, "Customer");`).
   - Drivers are created and maintained solely by Admins via `AdminController`.

---

### 1.2 Dispatch Assignment & Compliance Gatekeeping
1. **`Controllers/AdminController.cs` — `AssignTrip` (lines 728–859)**:
   - Line 738: Queries available drivers via `_userManager.GetUsersInRoleAsync("Driver")` filtered by `d.IsAvailable`.
   - Lines 805–819: Validates only that:
     ```csharp
     if (!driver.IsAvailable) { ... }
     if (!driver.IsApproved) { ... }
     ```
   - **Direct Observation**: There is **no validation** on whether the driver's DOT Medical Card is expired or missing. An admin can assign an active load to any approved driver even if their medical qualification expired years ago.
2. **`Views/Admin/AssignTrip.cshtml` (lines 23–35)**:
   - The driver dropdown (`<select asp-for="DriverId">`) displays only Name, Email, and Price/km.
   - It does not present CDL information, medical card expiration status, or alert warnings when an expired driver is selected.

---

### 1.3 Dashboard Metrics & Warning Banners
1. **`Models/DashbordStats.cs` (lines 6–18)**:
   ```csharp
   public class DashboardStats
   {
       public int ActiveTrips { get; set; }
       public int AvailableDrivers { get; set; }
       public int PendingApprovals { get; set; }
       public double  TotalRevenue { get; set; }
       public int CompletedTrips { get; set; }
       public List<RecentActivity> RecentActivities { get; set; } = [];
       public List<Driver> AvailableDriversList { get; set; } = [];
       public List<Customer> Customers { get; set; } = [];
       public int AssignedTrips { get; set; }  
   }
   ```
2. **`Controllers/AdminController.cs` — `Dashboard()` (lines 195–253)**:
   - Aggregates trip counts, revenue, and active drivers.
   - Completely lacks any calculation or notification system for regulatory compliance expirations.
3. **`Views/Admin/Dashboard.cshtml` (lines 9–35)**:
   - Displays 4 status cards: Active Trips, Assigned Trips, Completed Trips, Available Drivers.
   - **Direct Observation**: Zero alert banners exist for:
     * Driver DOT Medical Cards (49 CFR 391.41)
     * Annual Vehicle DOT Inspections (49 CFR 396.17)
     * Quarterly IFTA Tax Deadlines
     * IRS Form 2290 (HVUT) Annual Renewals

---

### 1.4 Regulatory Framework & Deadline Specifications
1. **DOT Medical Examination Card (49 CFR 391.41 & 391.43)**:
   - Commercial drivers must carry a valid Medical Examiner's Certificate (Form MCSA-5876).
   - Validity is at most 24 months. Operating a CMV with an expired medical card is an out-of-service violation.
2. **Annual Vehicle DOT Inspection (49 CFR 396.17 & Appendix G)**:
   - All commercial motor vehicles must pass a comprehensive periodic inspection at least once every 12 months.
3. **Quarterly IFTA (International Fuel Tax Agreement) Deadlines**:
   - Returns and tax payments are statutory deadlines due on the last day of the month following each calendar quarter:
     * **Q1 (Jan 1 – Mar 31):** Due **April 30**
     * **Q2 (Apr 1 – Jun 30):** Due **July 31**
     * **Q3 (Jul 1 – Sep 30):** Due **October 31**
     * **Q4 (Oct 1 – Dec 31):** Due **January 31** (subsequent year)
   - *Current System Context*: For simulated operations in late October (e.g. October 27, 2026), the Q3 deadline is **October 31, 2026** (4 days away).
4. **IRS Form 2290 Heavy Highway Vehicle Use Tax (HVUT)**:
   - Annual tax on CMVs with taxable gross weight >= 55,000 lbs.
   - The tax period runs from July 1 through June 30.
   - The statutory filing deadline for annual renewal is **August 31** of each year.

---

### 1.5 Existing Test Architecture & Build Configuration
1. **Project Configuration (`logirack.csproj` & `tests/LogiTrack.Tests/LogiTrack.Tests.csproj`)**:
   - Both target `net8.0` with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
   - Tool execution:
     ```powershell
     dotnet build --warnaserror
     ```
     Result:
     ```
     Build succeeded.
         0 Warning(s)
         0 Error(s)
     ```
   - All code is compiled with nullable reference annotations; any nullable mismatch immediately triggers a build error under `--warnaserror`.

2. **Test Frameworks & Libraries**:
   - `xunit` version `2.9.2`.
   - `xunit.runner.visualstudio` version `2.8.2`.
   - `Microsoft.AspNetCore.Mvc.Testing` version `8.0.30`.
   - `PdfSharpCore` version `1.3.67`.
   - **Moq is NOT used**: Test doubles are written as pure C# classes (`TestHubCallerContext`, `TestGroupManager`, `TestClientProxy`, `TestHubCallerClients`, `TestTempDataProvider`).
   - **Database Provider in Tests**: SQLite. In `LogiTrackFactory` (`ApplicationSmokeTests.cs` lines 233–251), each test class fixture creates an isolated temporary file:
     `_databasePath = Path.Combine(Path.GetTempPath(), $"logitrack-tests-{Guid.NewGuid():N}.db");`

3. **Test Execution Behavior & Parallelization Finding**:
   - Initial standard test run (`dotnet test`):
     ```
     Failed! - Failed: 3, Passed: 277, Skipped: 0, Total: 280, Duration: 8 s
     ```
     Failures occurred in `FullWorkflowEndToEndTests.cs`:
     - `E2E_Lifecycle_CustomerSubmit_AdminPrice_CustomerAccept_AssignDriver_SimulateGps_EndTrip_DownloadInvoices`: `Assert.Contains("PAID IN FULL", pageText)` failed.
     - `E2E_FinancialSummaryAndItemizedPdfInvoiceVerification`: `Assert.Contains("Service Handling & Dispatch Fee", pageText)` failed.
     - `E2E_LiveDriverSimulationAndMultiTenantIsolation_GroupScoping`: `SQLite Error 19: 'FOREIGN KEY constraint failed'`.
   - Isolated execution of each failed test:
     ```powershell
     dotnet test --filter "FullyQualifiedName~FullWorkflowEndToEndTests"
     ```
     Result: **5 Passed, 0 Failed**.
   - Test execution with parallelization disabled:
     ```powershell
     dotnet test --no-build -- RunConfiguration.DisableParallelization=true
     ```
     Result: **Passed! - Failed: 0, Passed: 280, Skipped: 0, Total: 280, Duration: 54 s**.
   - **Direct Observation**: All 280 existing tests are functionally valid. The intermittent failures in full runs are caused by **xUnit parallel test collection interference** (concurrent `WebApplicationFactory` SQLite file contention and `PdfSharpCore` static font cache threading races).

---

## 2. Logic Chain

### 2.1 Driver Regulatory Modeling (R4)
1. **Premise**: 49 CFR 391.41 mandates that commercial motor carrier drivers possess a valid Commercial Driver's License and an unexpired Medical Examiner's Certificate before operating a CMV.
2. **Inference**:
   - Add the following properties to `Models/Driver.cs`:
     ```csharp
     [StringLength(50)]
     [Display(Name = "CDL Number")]
     public string CdlNumber { get; set; } = string.Empty;

     [StringLength(2)]
     [RegularExpression(@"^[A-Z]{2}$", ErrorMessage = "State must be 2 uppercase letters.")]
     [Display(Name = "CDL Issuing State")]
     public string CdlIssuingState { get; set; } = string.Empty;

     [DataType(DataType.Date)]
     [Display(Name = "DOT Medical Card Expiration Date")]
     public DateTime? DotMedicalCardExpirationDate { get; set; }
     ```
   - Making `DotMedicalCardExpirationDate` nullable (`DateTime?`) ensures full backward compatibility with existing tests that create `Driver` records without this parameter.
3. **Inference for ViewModels**:
   - Update `CreateDriverViewModel` and `EditDriverViewModel` to require `CdlNumber`, `CdlIssuingState`, and `DotMedicalCardExpirationDate`.
   - Update `Views/Admin/CreateDriver.cshtml` and `Views/Admin/EditDriver.cshtml` with corresponding input fields.
   - Update `Views/Admin/_DriverManagmentPartial.cshtml` with CDL and Medical Card status badges:
     * `Valid` (Green): `> 30 days`
     * `Expiring Soon` (Yellow): `<= 30 days && >= today`
     * `Expired` (Red): `< today`

---

### 2.2 Dispatch Compliance Gatekeeping (R4)
1. **Premise**: Acceptance criteria specifies: *"Attempting to dispatch a driver with an expired DOT Medical Card produces a validation warning."*
2. **Inference**:
   - In `AdminController.AssignTrip(AssignTripViewModel model)`:
     ```csharp
     var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == model.DriverId);
     if (driver == null) { ... }

     // 49 CFR 391.41 Regulatory Compliance Gate
     if (driver.DotMedicalCardExpirationDate.HasValue && driver.DotMedicalCardExpirationDate.Value.Date < DateTime.UtcNow.Date)
     {
         await transaction.RollbackAsync();
         ModelState.AddModelError("DriverId", $"Cannot assign driver: DOT Medical Examination Card expired on {driver.DotMedicalCardExpirationDate.Value:yyyy-MM-dd} (49 CFR 391.41 compliance violation).");
         await LoadAvailableDrivers(model);
         return View(model);
     }
     ```
   - Apply identical validation in `AdminController.AddNewTripByAdmin` (POST) when an admin directly creates and assigns a trip.
   - In `Views/Admin/AssignTrip.cshtml`:
     - Include `data-med-card-expired="@(driver.DotMedicalCardExpirationDate.HasValue && driver.DotMedicalCardExpirationDate.Value.Date < DateTime.UtcNow.Date)"` on each `<option>`.
     - In the dropdown text, append `[EXPIRED MED CARD]` if expired.
     - Add dynamic JavaScript warning box displaying an immediate alert when an expired driver is selected.

---

### 2.3 Automated Dashboard Warning Banners Architecture (R4)
1. **Premise**: Acceptance criteria and R4 require dashboard warning banners for:
   * Upcoming/expired Medical cards
   * Annual vehicle DOT inspections
   * Quarterly IFTA tax deadlines
   * IRS Form 2290 renewals
2. **Inference**:
   - Define a strongly typed `ComplianceAlert` model in `Models/ComplianceAlert.cs` (or inside `Models/DashbordStats.cs`):
     ```csharp
     public class ComplianceAlert
     {
         public string AlertType { get; set; } = string.Empty; // "MedicalCard", "DotInspection", "IFTA", "Form2290"
         public string Severity { get; set; } = "warning";     // "danger", "warning", "info"
         public string Title { get; set; } = string.Empty;
         public string Message { get; set; } = string.Empty;
         public DateTime? ExpirationDate { get; set; }
         public int DaysRemaining { get; set; }
         public string? ActionUrl { get; set; }
     }
     ```
   - Extend `DashboardStats`:
     ```csharp
     public List<ComplianceAlert> ComplianceAlerts { get; set; } = [];
     ```
   - **Compliance Banner Calculation Logic** in `AdminController.Dashboard()`:
     1. **Medical Cards**: Query `_db.Users.OfType<Driver>()`.
        - Expired (`< today`): Add `Severity = "danger"`, `"CRITICAL: Driver {Name} has an expired DOT Medical Card ({Date:yyyy-MM-dd}). Dispatches prohibited."`
        - Expiring Soon (`<= today.AddDays(30)`): Add `Severity = "warning"`, `"WARNING: Driver {Name} DOT Medical Card expires in {Days} days."`
     2. **Annual Vehicle Inspections (49 CFR 396.17)**: Query `_db.Vehicles`.
        - Expired (`< today`): Add `Severity = "danger"`, `"CRITICAL: Vehicle Unit #{UnitNumber} Annual DOT Inspection expired on {Date:yyyy-MM-dd}."`
        - Expiring Soon (`<= today.AddDays(30)`): Add `Severity = "warning"`, `"WARNING: Vehicle Unit #{UnitNumber} Annual DOT Inspection due in {Days} days."`
     3. **Quarterly IFTA Tax Deadline**:
        - Compute statutory quarter deadline based on `DateTime.UtcNow`:
          * Jan 1 – Apr 30 -> April 30 (Q1)
          * May 1 – Jul 31 -> July 31 (Q2)
          * Aug 1 – Oct 31 -> October 31 (Q3)
          * Nov 1 – Dec 31 -> January 31 of next year (Q4)
        - If `daysRemaining <= 30`:
          * Overdue (`daysRemaining < 0`): `Severity = "danger"`, `"PAST DUE: IFTA Q{Quarter} fuel tax filing was due on {Deadline:MMMM dd, yyyy}."`
          * Upcoming (`daysRemaining >= 0`): `Severity = "warning"`, `"UPCOMING TAX DEADLINE: IFTA Q{Quarter} fuel tax filing is due on {Deadline:MMMM dd, yyyy} ({Days} days remaining)."`
     4. **IRS Form 2290 Renewals**:
        - Statutory annual renewal is August 31. If `today.Month == 7 || today.Month == 8`, generate a fleet-wide reminder banner.
        - Per-vehicle check: If `v.Form2290ExpirationDate < today`, generate a critical expiration alert for Unit #{UnitNumber}.
   - In `Views/Admin/Dashboard.cshtml`:
     Render `<div class="compliance-alerts">` right below the header, styling each banner as a Bootstrap alert (`alert-danger` / `alert-warning`) with icon and action link.

---

### 2.4 Test Suite Stabilization & Architecture (R5)
1. **Premise**: `dotnet test` must achieve 100% passing results reliably.
2. **Inference**:
   - To permanently eliminate parallelization collisions without modifying individual tests, introduce `xunit.runner.json` in `tests/LogiTrack.Tests/`:
     ```json
     {
       "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
       "parallelizeAssembly": false,
       "parallelizeTestCollections": false
     }
     ```
     With `<None Update="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />` in `LogiTrack.Tests.csproj`.
   - Alternatively, add an assembly attribute:
     ```csharp
     [assembly: CollectionBehavior(DisableTestParallelization = true)]
     ```
   - This ensures all 280 baseline tests plus all newly authored tests run deterministically and pass with zero race conditions.

---

### 2.5 Planned Test Suite Coverage for R1, R2, R3, R4
To satisfy acceptance criteria, author the following tests under `tests/LogiTrack.Tests/`:

| Module / Requirement | Test File | Test Method & Description |
|---|---|---|
| **R1. Fleet & Equipment** | `VehicleManagementTests.cs` | `CreateVehicle_WithValidAttributes_Succeeds`<br>`CreateVehicle_WithInvalidVin_FailsValidation`<br>`AssignVehicleToDriver_UpdatesDriverAndVehicle`<br>`DecommissionVehicle_PreventsDispatch` |
| **R1. Trip Dispatch** | `VehicleDispatchTests.cs` | `AssignTrip_RequiresActiveDriverAndActiveVehicle`<br>`AssignTrip_WithVehicleInMaintenance_FailsValidation`<br>`AssignTrip_WithAlreadyAssignedVehicle_FailsValidation` |
| **R2. Broker CRM** | `BrokerManagementTests.cs` | `CreateBroker_WithValidMCNumber_Succeeds`<br>`LookupBroker_ByMCNumber_ReturnsBrokerDetails`<br>`BlacklistBroker_PersistsReasonAndFlag` |
| **R2. Bad Broker Protection** | `BrokerDispatchWarningTests.cs` | `AssignTrip_WithBlacklistedBroker_DisplaysProminentWarning`<br>`ReviewTrip_DisplaysBrokerMCAndContactDetails` |
| **R3. Financial Settlements** | `SettlementCalculationTests.cs` | `Settlement_GrossRate_CalculatesRatePlusAccessorials`<br>`Settlement_Math_ComputesFactoringFeeAndDispatchFeeExact`<br>`Settlement_DriverNetPay_Calculates88PercentMinusDeductions`<br>`Settlement_TONUTransition_SetsAgreedAmountAndRecalculatesNetPay` |
| **R3. Settlement Statements** | `SettlementStatementVoucherTests.cs` | `GenerateSettlementStatementPdf_ContainsDualLoadIdAndBreakdown`<br>`Driver_CanDownload_SettlementVoucher`<br>`Admin_CanView_SettlementVoucher` |
| **R4. Safety & Compliance** | `DriverComplianceTests.cs` | `Driver_PersistsCdlAndMedicalCardExpiration`<br>`AssignTrip_WithExpiredMedicalCard_ProducesValidationWarning`<br>`AssignTrip_WithValidMedicalCard_SucceedsWithoutWarning` |
| **R4. Dashboard Banners** | `RegulatoryDashboardBannerTests.cs` | `Dashboard_DisplaysDangerBanner_WhenDriverMedicalCardExpired`<br>`Dashboard_DisplaysWarningBanner_WhenMedicalCardExpiringIn30Days`<br>`Dashboard_DisplaysBanner_WhenVehicleDotInspectionExpired`<br>`Dashboard_DisplaysBanner_ForUpcomingIftaQuarterlyDeadline`<br>`Dashboard_DisplaysBanner_ForIRSForm2290Renewal` |

---

## 3. Synthesis Across Explorer Analyses

### 3.1 Catalog of Inputs
1. **`explorer_tms_1` (Fleet & Equipment R1, Broker CRM R2, EF Migrations)**:
   - Designed `Vehicle` entity (`UnitNumber`, `Vin`, `LicensePlate`, `LicenseState`, `Make`, `Model`, `Year`, `RegistrationExpirationDate`, `AnnualDotInspectionExpiration`, `PrePassTransponderId`, `Status`, `AssignedDriverId`).
   - Designed `Broker` entity (`LegalName`, `McNumber`, `PrimaryAgentName`, `Phone`, `Email`, `IsBlacklisted`, `BlacklistReason`).
   - Verified that `dotnet ef` is functional and recommended a new EF Core migration (`AddFleetAndBrokerEntities`).
2. **`explorer_tms_2` (Automated Load Accounting & Settlements R3, Dispatch Lifecycle)**:
   - Added `InternalInvoiceNumber` (`IN001`) and `BrokerLoadNumber` (`23044790`) to `Trip`.
   - Added accessorials (`TonuAmount`, `DetentionAmount`, `LumperAmount`, `DeductionsAmount`, `DeductionReason`).
   - Added `TripStatus.TONU`.
   - Designed `ISettlementService` and `PdfSettlementService` for 12%/88% load settlement statements.
   - Integrated dual Driver + Vehicle selection into `AssignTrip`.
3. **`explorer_tms_3` (This Report: DOT Safety R4, Test Suite Architecture R5)**:
   - Added `CdlNumber`, `CdlIssuingState`, and `DotMedicalCardExpirationDate` to `Driver`.
   - Added `Form2290ExpirationDate` to `Vehicle`.
   - Formulated the 4-part automated compliance banner engine for `DashboardStats` and `Views/Admin/Dashboard.cshtml`.
   - Formulated the dispatch compliance gate in `AssignTrip` (49 CFR 391.41).
   - Solved the test parallelization collision and defined the full 16-test suite covering R1–R4.

### 3.2 Consensus
- **Data Types for Currency**: All agents agree that `double` on EF Core entities maintains SQLite compatibility, while monetary calculations in services (`ISettlementService`, `IInvoiceService`) should use `decimal` precision and round to 2 decimal places.
- **Trip Dispatch Integration**: All agents agree that `AssignTripViewModel` and `AdminController.AssignTrip` must validate both Driver and Tractor Unit, check Broker Blacklist status, and check Driver Medical Card expiration.
- **Migration Strategy**: A single comprehensive migration can incorporate `Vehicle`, `Broker`, `Driver` CDL/Med Card extensions, and `Trip` accounting fields.

### 3.3 Resolved Conflicts
- **Vehicle Expiration Fields**: `explorer_tms_1` included `AnnualDotInspectionExpiration`. `explorer_tms_3` extends this by also recommending `Form2290ExpirationDate` on `Vehicle` so that both DOT inspections and IRS Form 2290 per-vehicle expirations are tracked in the database and surfaced to dashboard banners.
- **Driver Medical Card Nullability**: To avoid breaking existing tests that create `Driver` instances without medical dates, `DotMedicalCardExpirationDate` is typed as `DateTime?`. The compliance engine treats `null` as unrecorded (or warning) while past dates strictly trigger the dispatch block.

### 3.4 Dissenting Views
- None. All specifications align across R1, R2, R3, R4, and R5.

### 3.5 Gaps Identified & Addressed
- **xUnit Test Parallelization**: Standard `dotnet test` suffered from 3 intermittent failures due to xUnit parallel test collection execution. Solved by specifying sequential test collection execution (`parallelizeTestCollections: false`), bringing the pass rate to 100% (280/280).

---

## 4. Caveats

1. **Simulated Server Clocks**: The quarterly IFTA and IRS Form 2290 banner calculations depend on `DateTime.UtcNow`. In unit tests for banners, tests should pass a reference `DateTime` or use a clock abstraction (e.g. `TimeProvider` or reference date parameter) so test assertions remain invariant over time.
2. **State Code Formatting**: While US CDLs use 2-letter postal abbreviations (e.g. `TX`, `IL`), Canadian provinces (e.g. `ON`, `AB`) also use 2 letters. A 2-letter uppercase constraint is appropriate and flexible.
3. **No Direct Code Modifications**: As a read-only explorer agent, no production or test files have been modified. All proposed changes are documented as implementation specifications for subsequent development stages.

---

## 5. Conclusion

1. **R4 (DOT Safety & Regulatory Expiration Tracking)** is fully mapped:
   - `Driver` entity and ViewModels will persist `CdlNumber`, `CdlIssuingState`, and `DotMedicalCardExpirationDate`.
   - `AdminController.AssignTrip` will enforce a hard validation gate rejecting drivers with expired medical cards under 49 CFR 391.41.
   - `AdminController.Dashboard()` will calculate and display dynamic warning banners for:
     1. Expired/upcoming driver DOT Medical Cards (49 CFR 391.41)
     2. Expired/upcoming annual vehicle DOT inspections (49 CFR 396.17)
     3. Approaching quarterly IFTA fuel tax deadlines (April 30, July 31, October 31, January 31)
     4. IRS Form 2290 (HVUT) annual renewals (August 31)
2. **R5 (Test Architecture & Build Setup)** is fully validated:
   - Solution compiles cleanly with **0 warnings and 0 errors** under `dotnet build --warnaserror`.
   - Solution passes **280 of 280 tests (100%)** when test collections run sequentially.
   - A complete 16-test suite covering R1, R2, R3, and R4 is specified using existing pure C# test doubles and isolated SQLite databases.

---

## 6. Verification Method

To independently verify all findings in this report:

1. **Verify Build Compliance**:
   ```powershell
   dotnet build --warnaserror
   ```
   *Expected Result*: Build succeeded with 0 Warning(s) and 0 Error(s).

2. **Verify Sequential Test Suite Baseline (100% Pass Rate)**:
   ```powershell
   dotnet test --no-build -- RunConfiguration.DisableParallelization=true
   ```
   *Expected Result*: `Passed! - Failed: 0, Passed: 280, Skipped: 0, Total: 280`.

3. **Inspect Driver Model & ViewModels**:
   - Inspect `C:\Users\naib\Documents\projects\Logi-Track\Models\Driver.cs` (lines 11–25)
   - Inspect `C:\Users\naib\Documents\projects\Logi-Track\Models\ViewModel\CreateDriverViewModel.cs`
   - Inspect `C:\Users\naib\Documents\projects\Logi-Track\Models\ViewModel\EditDriverViewModel.cs`

4. **Inspect Dispatch Gate & Dashboard Logic**:
   - Inspect `C:\Users\naib\Documents\projects\Logi-Track\Controllers\AdminController.cs`:
     * `Dashboard()` (lines 195–265)
     * `AssignTrip` GET (lines 728–751) and POST (lines 759–859)
   - Inspect `C:\Users\naib\Documents\projects\Logi-Track\Views\Admin\Dashboard.cshtml`
   - Inspect `C:\Users\naib\Documents\projects\Logi-Track\Views\Admin\AssignTrip.cshtml`
