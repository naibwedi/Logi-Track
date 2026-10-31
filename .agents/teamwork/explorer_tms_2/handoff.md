# TMS Transformation Exploration Report: Automated Load Accounting, Driver Settlements (R3) & Trip Dispatch Lifecycle

**Agent**: `explorer_tms_2`  
**Date**: 2026-10-27  
**Scope**: 
1. Automated Load Accounting & Driver Settlements (R3): Dual load ID, accessorials (TONU, detention, lumpers, deductions), settlement math (1.5% factoring, 12.0% dispatch, 88.0% net base), settlement vouchers (PDF/HTML), `ISettlementService`.
2. Trip Dispatch Lifecycle Integration: Admin trip creation/dispatch with Driver and Tractor Unit selection, broker details & blacklist alerts, TONU and Completed state transitions.

---

## 1. Observation

### 1.1 Existing Model Structure & Accounting Limitations
- **`Models/Trip.cs` (lines 8–54)**:
  - `Trip` currently contains basic logistics attributes: `FromCity`, `ToCity`, `FromAddress`, `ToAddress`, `FromZipCode`, `ToZipCode`, `Weight`, `Volume`, `Distance`, `GoodsType`, `PickupTime`, `EstimatedPrice`, `AdminPrice`, `Status`, `ConcurrencyToken`.
  - There are **no fields** for dual load identification (`InternalInvoiceNumber`, `BrokerLoadNumber`), freight broker relationship (`BrokerId`, `Broker`), tractor unit assignment (`VehicleId`, `Vehicle`), or accessorial charges (`DetentionAmount`, `LumperAmount`, `TonuAmount`, `DeductionsAmount`, `DeductionReason`).
- **`Models/Trip.cs` (lines 58–69) `TripStatus` Enum**:
  ```csharp
  public enum TripStatus
  {
      Requested,
      ApprovedByAdmin,
      PriceSet,
      ApprovedByCustomer,
      Assigned,
      InProgress,
      Completed,
      CancelledByAdmin,
      CanceledByCustomer,
  }
  ```
  - `TripStatus` has **no `TONU` status**. A truck order cancelled by a broker after dispatch currently has no dedicated status, meaning compensation cannot be properly calculated for cancelled loads.
- **`Models/DriverTrip.cs` (lines 5–33)**:
  - Line 22: `public double DriverPayment { get; set; }`
  - Lines 826 and 983 in `Controllers/AdminController.cs`:
    `DriverPayment = driver.PricePerKm * trip.Distance;`
    Driver payment was previously computed using a simplistic courier per-kilometer rate (`PricePerKm * Distance`), completely disconnected from commercial freight broker rate confirmations, 12% dispatch fee splits, accessorials, and claim deductions.
  - `DriverTrip` has no link to `Vehicle` (Tractor Unit).
- **`Data/ApplicationDbContext.cs` (line 7)**:
  - Comment: `//// TOREAD dont use decimal on models use double for SQLite compatibility start`
  - While EF Core 8.0.30 supports `decimal` (mapped to `TEXT` or `NUMERIC`), SQLite floating-point storage can risk precision drift unless explicit `decimal` types and rounding rules are maintained in application code and service calculations.

### 1.2 Current Dispatch & Trip Lifecycle Flow
- **`Controllers/AdminController.cs`**:
  - `AssignTrip(int id)` (lines 728–751) and `AssignTrip(AssignTripViewModel model)` (lines 759–859):
    - Line 738: Loads only available drivers (`_userManager.GetUsersInRoleAsync("Driver")`).
    - Lines 821–830: Creates `DriverTrip` selecting only `DriverId`. **Does not select or validate a Tractor Unit (`Vehicle`)**.
    - Does not display Broker details, MC#, or Bad Broker blacklist warnings.
    - Does not validate driver DOT Medical Examination Card expiration dates.
  - `AddNewTripByAdmin` (lines 879–1049):
    - Admin creates trip selecting only Customer and Driver.
    - Sets status directly to `TripStatus.Assigned`.
    - No tractor unit selection, no broker selection, no broker load number.
  - Line 1109 in `PreparePayments()`:
    ```csharp
    var completed = await _db.DriverTrips
        .Include(item => item.Driver)
        .Where(item => item.Status == TripStatus.Completed && item.PaymentPeriodId == null)
        .ToListAsync();
    ```
    - Only queries `TripStatus.Completed`. Even if a load had a TONU payout, it would be excluded from driver payment batch preparation.

### 1.3 Invoice & Settlement Services
- **`Services/IInvoiceService.cs` (lines 5–8)**:
  ```csharp
  public interface IInvoiceService
  {
      byte[] GenerateInvoicePdf(Trip trip, Customer customer);
  }
  ```
  - Strictly customer-facing freight billing invoice implemented by `PdfInvoiceService` using `PdfSharpCore`.
  - No interface or service exists for **Driver Settlement Statements** (carrier-to-driver compensation vouchers).
- **`Controllers/AdminController.cs` (lines 1165–1172)**:
  - `Invoice(int id)` renders `Views/Admin/Invoice.cshtml`, which is a generic HTML summary of a `Payment` entity with no itemized load breakdown, no dual load IDs, no accessorial itemization, and no download link.
- **`Controllers/DriverController.cs` (lines 219–226)**:
  - `PaymentTracking()` renders `Views/Driver/PaymentTracking.cshtml` showing only `PaymentDate`, `Amount`, and `Status`. Drivers have no mechanism to view or download itemized settlement statement vouchers.

### 1.4 Test Suite & Build Verification
- Running `dotnet test --nologo` on commit baseline:
  - Output: `Passed! - Failed: 0, Passed: 280, Skipped: 0, Total: 280, Duration: 10 s`
  - All 280 existing unit and integration tests pass.
  - Project references: `logirack.csproj` has `net8.0`, `PdfSharpCore 1.3.67`, `Microsoft.EntityFrameworkCore.Sqlite 8.0.30`.

---

## 2. Logic Chain

### 2.1 Dual Load Identification Architecture
1. **Premise**: In US freight operations for Rufta Logistics LLC, motor carriers receive load tenders from third-party logistics (3PL) brokers (e.g. C.H. Robinson, TQL, Landstar) with a Broker Load / Reference Number (e.g. `23044790`). Simultaneously, Rufta Logistics generates its own sequential Internal Invoice Number (e.g. `IN001`) for invoicing the broker and factoring company.
2. **Inference**:
   - `Trip` must persist both identifiers:
     - `InternalInvoiceNumber` (`string`, max length 50, required or auto-generated as `$"IN{trip.Id:D3}"`).
     - `BrokerLoadNumber` (`string?`, max length 50, optional/broker-specific).
   - Both identifiers must appear on:
     - Dispatch boards and trip listings (`TripRequests`, `TripLog`, `CurrentTrips`).
     - Customer/Broker Invoice PDF.
     - Driver Settlement Statement Voucher PDF and HTML view.

### 2.2 Accessorial Charges & Financial Settlement Math
1. **Premise**: The TMS contract requires:
   - `Gross Rate = Rate Confirmation + Added Accessorials`
   - `Factoring Fee = 1.5% of Gross Rate`
   - `Dispatch Fee = 12.0% of Gross Rate`
   - `Driver Net Pay = Gross Rate - Dispatch Fee - Deductions (88% net base)`
   - Accessorials: TONU ($150–$200 standard), Detention fees, Lumper payments, claim deductions.
2. **Step-by-step Math Model**:
   - **Base Rate Confirmation**: Agrees with broker tender rate (mapped to `Trip.AdminPrice`).
   - **Accessorials**:
     - `DetentionAmount`: Driver detention waiting at shipper/receiver past 2 free hours.
     - `LumperAmount`: Third-party unloading fee reimbursement.
     - `TonuAmount`: Payout when broker cancels after dispatch ($150.00 – $200.00).
     - `Added Accessorials = DetentionAmount + LumperAmount` (for standard loads) or `TonuAmount` (for TONU loads).
   - **Gross Rate**:
     - Normal trip: `GrossRate = AdminPrice + DetentionAmount + LumperAmount`
     - TONU trip: `GrossRate = TonuAmount + DetentionAmount + LumperAmount` (or flat `TonuAmount` if standard $150–$200 payout).
   - **Factoring Fee**:
     - `FactoringFee = GrossRate * 0.015m` (carrier financing expense).
   - **Dispatch Fee**:
     - `DispatchFee = GrossRate * 0.120m` (Rufta Logistics dispatch brokerage cut).
   - **Driver Net Pay**:
     - `DriverNetPay = GrossRate - DispatchFee - DeductionsAmount`
     - Note: `GrossRate - DispatchFee` = `GrossRate * (1.0m - 0.120m) = GrossRate * 0.880m` (88% net base).
     - Then minus any claim deductions: `DriverNetPay = (GrossRate * 0.880m) - DeductionsAmount`.
3. **Preventing Rounding Drift**:
   - Binary floating-point (`double`) produces IEEE-754 precision artifacts (e.g. `0.1 + 0.2 != 0.3`).
   - All financial settlement calculations must be executed using C# `decimal` with explicit rounding `Math.Round(value, 2, MidpointRounding.AwayFromZero)`.
   - On models, `double` or `decimal` can be stored; when using `decimal`, EF Core 8 with SQLite can store as `TEXT`/`NUMERIC` or use a converter `.HasConversion<double>()` to ensure 100% SQLite query safety without losing precision in memory.

### 2.3 Trip Status Lifecycle & TONU State Transitions
1. **Premise**: A motor carrier dispatch lifecycle includes the critical real-world condition: "Truck Order Not Used" (TONU).
2. **State Machine Expansion**:
   - Add `TONU` to `TripStatus`.
   - Valid transitions:
     - `Assigned` -> `InProgress` -> `Completed`
     - `Assigned` -> `TONU`
     - `InProgress` -> `TONU`
3. **Transition to TONU**:
   - When an Admin marks a trip as TONU (`MarkTripTonu`):
     - `trip.Status = TripStatus.TONU;`
     - `trip.TonuAmount = tonuAmount;` (standard $150.00 or $200.00)
     - `driverTrip.Status = TripStatus.TONU;`
     - `driverTrip.CompletionDate = DateTime.UtcNow;`
     - `driverTrip.DriverPayment = Math.Round(tonuAmount * 0.88, 2);`
     - Release driver: `driver.IsAvailable = true;`
     - Release tractor unit: `vehicle.Status = VehicleStatus.Active;`
     - Concurrency token updated.
   - `PreparePayments()` query must include both completed and TONU trips:
     `Where(item => (item.Status == TripStatus.Completed || item.Status == TripStatus.TONU) && item.PaymentPeriodId == null)`

### 2.4 Dispatch Selection: Both Driver & Tractor Unit Required
1. **Premise**: R1 and TMS dispatch requirements state:
   - "Trip dispatch interfaces require selecting both an active Driver and an active Vehicle Unit."
   - "Attempting to dispatch a driver with an expired DOT Medical Card produces a validation warning."
   - "Attempting to dispatch a load with a blacklisted broker displays a prominent warning with the blacklist reason."
2. **Integration into `AssignTrip`**:
   - `AssignTripViewModel` must include:
     - `[Required(ErrorMessage = "Please select a driver")] string DriverId`
     - `[Required(ErrorMessage = "Please select a tractor unit")] int VehicleId`
     - `List<Driver> AvailableDrivers`
     - `List<Vehicle> AvailableVehicles` (vehicles with `Status == VehicleStatus.Active`)
     - Broker details & Bad Broker blacklist warning flag
   - In `AssignTrip(AssignTripViewModel model)` POST:
     - Verify both Driver and Vehicle exist and are available/active.
     - Validate driver medical card expiration date: if expired, log warning and set validation alert.
     - Assign `driverTrip.VehicleId = vehicle.Id`.
     - Compute initial `driverTrip.DriverPayment = (trip.AdminPrice + trip.DetentionAmount + trip.LumperAmount) * 0.88 - trip.DeductionsAmount`.

### 2.5 Driver Settlement Statement Voucher Service (`ISettlementService`)
1. **Premise**: Existing `IInvoiceService` generates customer invoices for cargo transport billing. Driver settlement statements serve a completely different business purpose: driver payroll/settlement vouchers itemizing gross load revenue, 12% dispatch fee deductions, accessorials, factoring fees, and net payout.
2. **Inference**:
   - Do NOT overload `IInvoiceService`.
   - Introduce a dedicated `ISettlementService` and `PdfSettlementService` in `Services/`:
     ```csharp
     public interface ISettlementService
     {
         SettlementCalculationResult CalculateTripSettlement(Trip trip);
         SettlementCalculationResult CalculatePaymentSettlement(Payment payment);
         byte[] GenerateSettlementPdf(Payment payment);
         byte[] GenerateTripSettlementPdf(DriverTrip driverTrip);
     }
     ```
   - Use `PdfSharpCore` (consistent with `PdfInvoiceService`).
   - Format includes:
     - Rufta Logistics LLC carrier branding
     - Driver name, CDL #, issuing state, assigned Tractor unit #
     - Pay period dates and statement voucher #
     - Itemized load table: Internal Invoice #, Broker Load #, Broker Name, Route, Completion Date, Base Rate, Accessorials (Detention/Lumper/TONU), Gross Rate, 12% Dispatch Fee, Deductions, Driver Net Pay
     - Carrier factoring fee (1.5%) reference
     - Direct deposit / ACH settlement summary
   - Both Admins and Drivers can download PDF vouchers:
     - Admin: `/Admin/DownloadSettlement/{paymentId}` and `/Admin/DownloadTripSettlement/{tripId}`
     - Driver: `/Driver/DownloadSettlement/{paymentId}` and `/Driver/DownloadTripSettlement/{tripId}` (with strict ownership check)

---

## 3. Caveats

1. **Entity Sharing with Peer Agents**:
   - `Vehicle` (Tractor Unit) is primarily investigated by `explorer_tms_1` (R1). Our design relies on `Vehicle.Id`, `Vehicle.UnitNumber`, `Vehicle.Status == VehicleStatus.Active`.
   - `Broker` is primarily investigated by `explorer_tms_1` (R2). Our design relies on `Broker.Id`, `Broker.LegalName`, `Broker.McNumber`, `Broker.IsBlacklisted`, `Broker.BlacklistReason`.
   - `Driver.CdlNumber`, `Driver.CdlState`, and `Driver.MedicalCardExpiration` are investigated by `explorer_tms_3` (R4). Our dispatch validation references these properties.
2. **Database Engine Differences**:
   - In Development and Tests, SQLite is used; in Production, PostgreSQL (`Npgsql`) is used.
   - For SQLite compatibility, financial properties on models (`DetentionAmount`, `LumperAmount`, `TonuAmount`, `DeductionsAmount`) can be stored as `double` on entities and converted to `decimal` during calculations, or stored as `decimal` with EF Core's built-in mapping. The settlement service calculation engine itself strictly uses `decimal` to ensure 0 rounding drift.
3. **No Code Modification**:
   - As an explorer agent, no project source code was modified during this turn. All proposals are documented in this report for the implementer/worker agents.

---

## 4. Conclusion & Technical Design

### 4.1 Proposed Model Additions

#### A. Add `TONU` to `TripStatus` (`Models/Trip.cs`)
```csharp
public enum TripStatus
{
    Requested,
    ApprovedByAdmin,
    PriceSet,
    ApprovedByCustomer,
    Assigned,
    InProgress,
    Completed,
    CancelledByAdmin,
    CanceledByCustomer,
    TONU // Truck Order Not Used ($150-$200 payout)
}
```

#### B. Properties Added to `Trip` (`Models/Trip.cs`)
```csharp
// Dual Load Identification
[StringLength(50)]
public string InternalInvoiceNumber { get; set; } = string.Empty;

[StringLength(50)]
public string? BrokerLoadNumber { get; set; }

// Broker CRM Link (R2)
public int? BrokerId { get; set; }
public Broker? Broker { get; set; }

// Accessorial Charges & Deductions (R3)
public double DetentionAmount { get; set; } = 0.0;
public double LumperAmount { get; set; } = 0.0;
public double TonuAmount { get; set; } = 0.0; // Standard $150.00 - $200.00 when TONU
public double DeductionsAmount { get; set; } = 0.0;

[StringLength(500)]
public string? DeductionReason { get; set; }

// Equipment Assignment (R1)
public int? VehicleId { get; set; }
public Vehicle? Vehicle { get; set; }
```

#### C. Properties Added to `DriverTrip` (`Models/DriverTrip.cs`)
```csharp
// Tractor Unit Assigned for Trip
public int? VehicleId { get; set; }
public Vehicle? Vehicle { get; set; }
```

---

### 4.2 Settlement Calculation Engine (`Services/SettlementCalculator.cs`)

```csharp
namespace logirack.Services;

public sealed class SettlementCalculationResult
{
    public decimal RateConfirmation { get; init; }
    public decimal Detention { get; init; }
    public decimal Lumper { get; init; }
    public decimal Tonu { get; init; }
    public decimal TotalAccessorials => Detention + Lumper + Tonu;
    public decimal GrossRate { get; init; }
    public decimal FactoringFee { get; init; } // 1.5% of GrossRate
    public decimal DispatchFee { get; init; }  // 12.0% of GrossRate
    public decimal Deductions { get; init; }
    public decimal DriverNetPay { get; init; } // GrossRate - DispatchFee - Deductions = (88% net base) - Deductions
    public decimal CarrierNetMargin => DispatchFee - FactoringFee;
}

public static class SettlementCalculator
{
    public const decimal FactoringFeeRate = 0.015m; // 1.5%
    public const decimal DispatchFeeRate = 0.120m;  // 12.0%
    public const decimal DriverBasePayRate = 0.880m; // 88.0%

    public static SettlementCalculationResult Calculate(
        decimal rateConfirmation,
        decimal detention = 0m,
        decimal lumper = 0m,
        decimal tonu = 0m,
        decimal deductions = 0m,
        bool isTonu = false)
    {
        decimal grossRate;
        if (isTonu)
        {
            // For TONU loads, gross freight revenue is the agreed TONU payout + any detention/lumper
            grossRate = Math.Round(tonu + detention + lumper, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            grossRate = Math.Round(rateConfirmation + detention + lumper, 2, MidpointRounding.AwayFromZero);
        }

        decimal factoringFee = Math.Round(grossRate * FactoringFeeRate, 2, MidpointRounding.AwayFromZero);
        decimal dispatchFee = Math.Round(grossRate * DispatchFeeRate, 2, MidpointRounding.AwayFromZero);
        decimal driverNetPay = Math.Round(grossRate - dispatchFee - deductions, 2, MidpointRounding.AwayFromZero);

        return new SettlementCalculationResult
        {
            RateConfirmation = rateConfirmation,
            Detention = detention,
            Lumper = lumper,
            Tonu = tonu,
            GrossRate = grossRate,
            FactoringFee = factoringFee,
            DispatchFee = dispatchFee,
            Deductions = deductions,
            DriverNetPay = driverNetPay
        };
    }
}
```

---

### 4.3 Dedicated Settlement Service Specification (`Services/ISettlementService.cs`)

```csharp
namespace logirack.Services;

using logirack.Models;

public interface ISettlementService
{
    SettlementCalculationResult CalculateTripSettlement(Trip trip);
    SettlementCalculationResult CalculatePaymentSettlement(Payment payment);
    byte[] GenerateSettlementPdf(Payment payment);
    byte[] GenerateTripSettlementPdf(DriverTrip driverTrip);
}
```

#### PDF Settlement Voucher Layout Design (`PdfSettlementService.cs`)
Using `PdfSharpCore`:
1. **Document Meta**: Title `"Rufta Logistics - Driver Settlement Voucher #SET-{payment.Id:D5}"`.
2. **Header Banner**: Navy background (`#182B49`), Rufta Logistics LLC branding, US DOT / MC credentials, Pay Period start/end dates, voucher issuance date.
3. **Driver & Tractor Panel**:
   - Driver Name, Email, Phone
   - CDL Number & State (from Driver)
   - Assigned Tractor Unit Number, VIN, Plate (from Vehicle)
4. **Itemized Trip Table**:
   - Headers: `Internal Inv #`, `Broker Load #`, `Route / Details`, `Status`, `Gross Rate`, `12% Dispatch Fee`, `Accessorials`, `Deductions`, `Driver Net Pay`.
   - Rows: Display each trip with dual load IDs (`IN001` / `23044790`), origin to destination, delivery timestamp, itemized accessorials (Detention, Lumper, TONU), 12% deduction, and net pay.
5. **Period Totals Summary**:
   - Total Gross Revenue
   - Total Dispatch Fees (12%)
   - Total Claim Deductions
   - Net Driver Payable (88% net base)
   - Factoring Fee summary (1.5% reference)
6. **Footer**:
   - "Rufta Logistics LLC • Direct Deposit ACH Settlement Statement • Inquiries: dispatch@ruftalogistics.test"

---

### 4.4 Controller & Lifecycle Action Specifications

#### A. `AssignTrip` Enhancement (`Controllers/AdminController.cs`)
```csharp
// In AssignTrip (GET):
var availableDrivers = await _db.Drivers
    .Where(d => d.IsAvailable && d.IsApproved)
    .ToListAsync();

var availableVehicles = await _db.Vehicles
    .Where(v => v.Status == VehicleStatus.Active)
    .ToListAsync();

// Pass availableVehicles and availableDrivers to AssignTripViewModel.
// If trip.Broker != null && trip.Broker.IsBlacklisted: pass warning message to ViewBag / ViewModel.

// In AssignTrip (POST):
// 1. Verify ModelState.IsValid (DriverId and VehicleId required).
// 2. Validate Vehicle is Active.
// 3. Validate Driver MedicalCardExpiration > DateTime.Today (produce validation warning if expired).
// 4. Calculate DriverPayment using SettlementCalculator (88% base).
// 5. Create DriverTrip with TripId, DriverId, VehicleId, DriverPayment, Status = TripStatus.Assigned.
// 6. Set driver.IsAvailable = false.
// 7. Commit in transaction with concurrency guard.
```

#### B. `MarkTripTonu` Action (`Controllers/AdminController.cs`)
```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> MarkTripTonu(int tripId, double tonuAmount = 150.0, string? reason = null)
{
    var trip = await _db.Trips
        .Include(t => t.DriverTrip)
        .ThenInclude(dt => dt.Driver)
        .FirstOrDefaultAsync(t => t.Id == tripId);

    if (trip == null) return NotFound();
    if (trip.Status != TripStatus.Assigned && trip.Status != TripStatus.InProgress)
        return BadRequest("Only assigned or in-progress trips can be transitioned to TONU.");

    trip.Status = TripStatus.TONU;
    trip.TonuAmount = tonuAmount;
    if (!string.IsNullOrWhiteSpace(reason))
    {
        trip.Notes = string.IsNullOrWhiteSpace(trip.Notes) ? $"TONU: {reason}" : $"{trip.Notes} | TONU: {reason}";
    }

    if (trip.DriverTrip != null)
    {
        trip.DriverTrip.Status = TripStatus.TONU;
        trip.DriverTrip.CompletionDate = DateTime.UtcNow;
        // 88% driver net payout for TONU
        trip.DriverTrip.DriverPayment = Math.Round(tonuAmount * 0.88, 2);

        if (trip.DriverTrip.Driver != null)
        {
            trip.DriverTrip.Driver.IsAvailable = true;
        }
    }

    if (trip.VehicleId.HasValue)
    {
        var vehicle = await _db.Vehicles.FindAsync(trip.VehicleId.Value);
        if (vehicle != null && vehicle.Status == VehicleStatus.Active)
        {
            // Vehicle is freed up
        }
    }

    await _db.SaveChangesAsync();
    TempData["Success"] = $"Trip #{trip.Id} transitioned to TONU with ${tonuAmount:F2} payout.";
    return RedirectToAction(nameof(TripDetails), new { id = tripId });
}
```

#### C. `PreparePayments` Update (`Controllers/AdminController.cs`)
```csharp
// Update line 1109 to include TONU:
var completed = await _db.DriverTrips
    .Include(item => item.Driver)
    .Include(item => item.Trip)
    .Where(item => (item.Status == TripStatus.Completed || item.Status == TripStatus.TONU) 
                   && item.PaymentPeriodId == null)
    .ToListAsync();
```

#### D. Download Settlement Endpoints (`AdminController.cs` & `DriverController.cs`)
```csharp
// AdminController:
[HttpGet]
public async Task<IActionResult> DownloadSettlement(int id)
{
    var payment = await _db.Payments
        .Include(p => p.Driver)
        .Include(p => p.PaymentPeriod)
            .ThenInclude(pp => pp.DriverTrips)
                .ThenInclude(dt => dt.Trip)
                    .ThenInclude(t => t.Broker)
        .FirstOrDefaultAsync(p => p.Id == id);

    if (payment == null) return NotFound();
    var pdfBytes = _settlementService.GenerateSettlementPdf(payment);
    return File(pdfBytes, "application/pdf", $"Settlement_Voucher_{payment.Id}.pdf");
}

// DriverController (with IDOR protection):
[HttpGet]
public async Task<IActionResult> DownloadSettlement(int id)
{
    var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    var payment = await _db.Payments
        .Include(p => p.Driver)
        .Include(p => p.PaymentPeriod)
            .ThenInclude(pp => pp.DriverTrips)
                .ThenInclude(dt => dt.Trip)
                    .ThenInclude(t => t.Broker)
        .FirstOrDefaultAsync(p => p.Id == id && p.DriverId == driverId);

    if (payment == null) return NotFound();
    var pdfBytes = _settlementService.GenerateSettlementPdf(payment);
    return File(pdfBytes, "application/pdf", $"Settlement_Voucher_{payment.Id}.pdf");
}
```

---

## 5. Verification Method

### 5.1 Automated Test Execution
Run the full test suite in `tests/LogiTrack.Tests`:
```pwsh
dotnet test --nologo
```
**Expected Result**: All existing 280 tests continue to pass.

### 5.2 Planned Unit & Integration Test Suites
1. **Settlement Math Unit Tests (`SettlementCalculatorTests.cs`)**:
   - `GrossRate` = $2,000.00 -> `FactoringFee` = $30.00, `DispatchFee` = $240.00, `DriverNetPay` = $1,760.00 (88%).
   - `GrossRate` = $2,225.00 ($2,000 base + $150 detention + $75 lumper) -> `FactoringFee` = $33.38, `DispatchFee` = $267.00, `DriverNetPay` = $1,958.00.
   - Deductions = $50.00 -> `DriverNetPay` = $1,908.00.
   - TONU: `TonuAmount` = $150.00 -> `GrossRate` = $150.00, `FactoringFee` = $2.25, `DispatchFee` = $18.00, `DriverNetPay` = $132.00 (88%).
   - TONU: `TonuAmount` = $200.00 -> `GrossRate` = $200.00, `FactoringFee` = $3.00, `DispatchFee` = $24.00, `DriverNetPay` = $176.00 (88%).
   - Precision test over 1,000 random decimal transactions verifying 0 rounding drift.
2. **Dispatch Lifecycle Integration Tests (`TripDispatchLifecycleTests.cs`)**:
   - Dispatch without Tractor Unit fails model validation.
   - Dispatch with blacklisted broker triggers alert.
   - Dispatch with expired driver medical card triggers validation warning.
   - Transition to TONU sets status, TONU payout, calculates 88% driver compensation, and frees driver/tractor.
   - `PreparePayments` aggregates completed and TONU trips into driver settlement period.
3. **Settlement PDF Tests (`SettlementPdfTests.cs`)**:
   - Generating settlement PDF produces valid `%PDF-` document with non-zero byte length.
   - Driver unauthorized IDOR access to another driver's settlement PDF returns 404/403.

### 5.3 Invalidation Conditions
- Any floating-point binary inaccuracies resulting in cents drift between `Gross Rate - Dispatch Fee` and `88% Gross Rate`.
- Exclusion of TONU trips from driver settlement batches.
- Allowing trip dispatch without an active Tractor Unit.
