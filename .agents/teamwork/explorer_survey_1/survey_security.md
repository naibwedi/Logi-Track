# Survey Report: Access Control, Concurrency & Security Hardening (R1)

**Target System:** Logi-Track Transport Management System  
**Investigator:** explorer_survey_1  
**Date:** 2026-10-27  
**Milestone:** M1 — Survey & Architectural Blueprint  

---

## 1. Executive Summary

This survey analyzes the security and concurrency architecture of Logi-Track with specific focus on **Requirement 1 (Access Control, Concurrency & Security Hardening)** and its interactions with **R2 (Live GPS Tracking)** and **R3 (Automated Invoicing)**.

### Core Discoveries:
1. **Access Control & IDOR Vulnerabilities**:
   - `CustomerController.OrderSuccess(int id)` (`CustomerController.cs:188`) returns `302 Redirect` to `Dashboard` instead of `404 Not Found` or `403 Forbidden` when accessing a trip not owned by the customer, violating acceptance criteria.
   - While `CustomerController.TripDetails` and `RespondToPrice` perform ownership checks returning `NotFound()`, future endpoints required by R2 (`/Track/{id}`) and R3 (`/Invoice/{id}`) currently do not exist and must be architected with strict ownership and role verification.
   - `DriverController` lacks an individual trip detail view (`/Driver/TripDetails/{id}`) or tracking view.
   - `DriverController` lacks the `[ApprovedOnly]` authorization filter used on `CustomerController`.
2. **Concurrency Control Deficiencies**:
   - Neither `Trip`, `DriverTrip`, nor `Driver` entities define any concurrency token (`[Timestamp]`, `byte[] RowVersion`, or `[ConcurrencyCheck]`).
   - `AdminController.ReviewTrip`, `AdminController.SetPrice`, and `CustomerController.RespondToPrice` rely solely on in-memory state checks on stale entities with no optimistic locking or conflict detection. Concurrent submissions produce "last write wins" overwrites or silent data state corruptions.
   - `AdminController.AssignTrip` exhibits a classic check-then-act race condition: driver availability and trip unassigned status are evaluated outside the database transaction, allowing concurrent admins to assign the same driver to multiple concurrent trips or assign multiple drivers to the same trip.
   - `DriverController.EndTrip` executes without database transactions or concurrency checks. Furthermore, it only accepts `TripStatus.Assigned`, completely locking out `TripStatus.InProgress`.
3. **CSRF & Input Sanitization**:
   - All current mutating POST actions explicitly define `[ValidateAntiForgeryToken]`. However, `Program.cs` lacks a global `AutoValidateAntiforgeryTokenAttribute` in its MVC pipeline.
   - User-supplied strings in `SubmitTripViewModel` (`FromAddress`, `ToAddress`, `FromCity`, `ToCity`, `Notes`, `GoodType`) are stored un-sanitized. `Notes` has no maximum length constraint, allowing unbounded payloads.
   - User-supplied strings are interpolated directly into SendGrid HTML notification emails (`EmailSender.cs:42` sets `htmlContent: htmlMessage`), opening an email HTML/script injection vector.

---

## 2. System Architecture & Trip Lifecycle

### 2.1 Technology Stack & Dual Database Setup
- **Framework:** ASP.NET Core 8.0 (MVC, Razor Pages, SignalR).
- **Data Store (`Program.cs:24-39`):**
  - **Development:** SQLite (`builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(sqliteConnection))`).
  - **Production:** PostgreSQL (`options.UseNpgsql(postgresConnection)`).
  - *Implication for Concurrency:* SQLite does not natively support SQL Server-style automatic database rowversion triggers for `byte[]` columns. A database-agnostic concurrency strategy using `[ConcurrencyCheck]` on a `Guid ConcurrencyToken` (or `SaveChanges` interceptor) is mandatory to guarantee portability across SQLite and PostgreSQL.

### 2.2 Identity Model & Roles
- **Base User:** `ApplicationUser : IdentityUser` (`Models/ApplicationUser.cs:11`).
- **Inheritance Hierarchy:** TPH (Table-Per-Hierarchy) mapping configured in `ApplicationDbContext.cs:49-54`:
  - Discriminator: `"RoleType"`.
  - Derived classes: `Customer`, `Driver`, `Admin`, `SuperAdmin`.
- **Approval System:** `IsApproved` boolean on `ApplicationUser`.
  - Filter: `Services/ApprovedOnlyAttribute.cs` verifies `user.IsApproved`.
  - Present on: `CustomerController` (`[Authorize(Roles = "Customer")]`, `[ApprovedOnly]`).
  - Missing on: `DriverController` (`[Authorize(Roles = "Driver")]` only).

### 2.3 Trip Lifecycle State Machine
Defined in `Models/Trip.cs:55-66`:
```csharp
public enum TripStatus
{
    Requested,          // 0: Customer submits request
    ApprovedByAdmin,    // 1: Admin reviews and approves request
    PriceSet,           // 2: Admin calculates and sets formal price
    ApprovedByCustomer, // 3: Customer accepts price quote
    Assigned,           // 4: Admin assigns driver to trip (DriverTrip created)
    InProgress,         // 5: Driver starts active route (CURRENTLY UNUSED IN ACTIONS)
    Completed,          // 6: Driver ends delivery
    CancelledByAdmin,   // 7: Admin rejects request
    CanceledByCustomer  // 8: Customer rejects price quote
}
```

#### Lifecycle Step Mapping:
| Step | Action Method | Controller / Lines | Roles | Pre-Conditions | Post-Conditions |
|---|---|---|---|---|---|
| **1. Create** | `CreateTrip(SubmitTripViewModel)` | `CustomerController:104-174` | Customer | Valid Model | Creates `Trip`, `Status = Requested` |
| **2. Review** | `ReviewTrip(int tId, bool isApproved)` | `AdminController:526-552` | Admin | `Status == Requested` | `Status = ApprovedByAdmin` or `CancelledByAdmin` |
| **3. Price** | `SetPrice(SetPriceViewModel)` | `AdminController:588-675` | Admin | `Status == ApprovedByAdmin` | Sets `AdminPrice`, `Status = PriceSet` |
| **4. Customer Price Response** | `RespondToPrice(int id, bool approved)` | `CustomerController:245-322` | Customer | `Status == PriceSet`, `CustomerId == user.Id` | `Status = ApprovedByCustomer` or `CanceledByCustomer` |
| **5. Driver Assignment** | `AssignTrip(AssignTripViewModel)` | `AdminController:712-804` | Admin | `Status == ApprovedByCustomer`, `Driver.IsAvailable` | Creates `DriverTrip`, sets `Status = Assigned`, `Driver.IsAvailable = false` |
| **Alternative Creation** | `AddNewTripByAdmin(CreateTripByAdminViewModel)` | `AdminController:844-994` | Admin | Valid Customer, Available Driver | Directly creates `Trip` + `DriverTrip`, sets `Status = Assigned` |
| **6. Active Transit** | *None* | *None* | Driver | *None* | `TripStatus.InProgress` is never set! |
| **7. Completion** | `EndTrip(int tripId)` | `DriverController:108-180` | Driver | `Status == Assigned`, `DriverId == user.Id` | `Status = Completed`, `Driver.IsAvailable = true` |
| **8. Invoicing / Payment** | `PreparePayments()` | `AdminController:1047-1093` | Admin | `DriverTrip.Status == Completed`, `PaymentPeriodId == null` | Creates `PaymentPeriod` and `Payment` |

#### Architectural Flaw in `InProgress` State:
- `TripStatus.InProgress` is defined in `Trip.cs:62` and queried in `AdminController.Dashboard:202` (`t.Status == TripStatus.Assigned || t.Status == TripStatus.InProgress`).
- However, `DriverController` contains no action to transition a trip from `Assigned` to `InProgress`.
- Furthermore, `DriverController.EndTrip` line 118:
  ```csharp
  if (driverTrip == null || driverTrip.Status != TripStatus.Assigned)
  {
      return NotFound("Trip not found or already completed.");
  }
  ```
  If a trip is in `InProgress`, `EndTrip` rejects it! This must be updated to accept `TripStatus.Assigned` OR `TripStatus.InProgress`.

---

## 3. Insecure Direct Object Reference (IDOR) Audit

### 3.1 Existing Actions Analysis
| Action | Controller | Location | User Role | ID Parameter | Current Auth / Ownership Guard | Security Status |
|---|---|---|---|---|---|---|
| `OrderSuccess` | `CustomerController` | Lines 184–195 | Customer | `int id` | `t.Id == id && t.CustomerId == user.Id` | ⚠️ **VULNERABLE**: Returns 302 Redirect to Dashboard instead of 404/403. |
| `TripDetails` | `CustomerController` | Lines 220–231 | Customer | `int id` | `t.Id == id && t.CustomerId == user.Id` | ✅ Secure (Returns 404 Not Found). |
| `RespondToPrice` | `CustomerController` | Lines 245–261 | Customer | `int id` | `t.Id == id && t.CustomerId == user.Id` | ✅ Secure (Returns 404 Not Found). |
| `EndTrip` | `DriverController` | Lines 108–121 | Driver | `int tripId` | `dt.TripId == tripId && dt.DriverId == driverId` | ✅ Secure (Returns 404 Not Found). |
| `TripDetails` | `AdminController` | Lines 489–517 | Admin | `int id` | `[Authorize(Roles = "Admin")]` | ✅ Secure (Admins have global oversight). |
| `ReviewTrip` | `AdminController` | Lines 526–552 | Admin | `int tId` | `[Authorize(Roles = "Admin")]` | ✅ Secure (Admins have global oversight). |
| `SetPrice` | `AdminController` | Lines 559–675 | Admin | `int id` / `TripId` | `[Authorize(Roles = "Admin")]` | ✅ Secure (Admins have global oversight). |
| `AssignTrip` | `AdminController` | Lines 681–804 | Admin | `int id` / `TripId` | `[Authorize(Roles = "Admin")]` | ✅ Secure (Admins have global oversight). |
| `JoinTrip` | `TripHub` | `TripHub.cs:22-41` | Authenticated | `int tripId` | Verifies `Admin` OR `trip.CustomerId == userId` OR `dt.DriverId == userId` | ✅ Secure (Throws `HubException`). |

### 3.2 Specific Finding: `CustomerController.OrderSuccess` IDOR Flaw
- **Location:** `Controllers/CustomerController.cs:188-193`
- **Observed Code:**
  ```csharp
  var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);
  if (trip == null)
  {
      return RedirectToAction("Dashboard");
  }
  ```
- **Vulnerability:** When a customer modifies the URL to view another user's trip (e.g. `/Customer/OrderSuccess/42`), the application issues HTTP 302 Found redirecting to `/Customer/Dashboard`.
- **Requirement Compliance:** Acceptance Criteria explicitly mandates:
  > *"Direct object reference (IDOR) attempts (e.g. requesting another user's trip details via URL manipulation) return 403 Forbidden or 404 Not Found."*
- **Remediation:** Change `return RedirectToAction("Dashboard");` to `return NotFound();` (or `Forbid()` if trip exists but belongs to another user).

### 3.3 Missing Trip Endpoints & Future IDOR Surfaces (R2 & R3)
1. **Live GPS Tracking (R2):**
   - When a tracking view (e.g., `/Customer/TrackTrip/{id}` or `/Driver/TrackTrip/{id}` or `/Admin/TrackTrip/{id}`) is implemented:
     - Customer route: Must verify `trip.CustomerId == user.Id`. If not, return `404` or `403`.
     - Driver route: Must verify `trip.DriverTrip != null && trip.DriverTrip.DriverId == user.Id`. If not, return `404` or `403`.
     - Hub method `SendLocationUpdate(tripId, lat, lon)`: Must strictly enforce that `Context.User` is the assigned Driver for that specific trip.
2. **Automated PDF Invoicing (R3):**
   - When invoice download action (e.g., `/Customer/DownloadInvoice/{tripId}`) is implemented:
     - Must verify `trip.CustomerId == user.Id` and `trip.Status == TripStatus.Completed`.
     - Non-owner requests must return `404` or `403`.
     - Admin route (`/Admin/DownloadTripInvoice/{tripId}`) must verify admin role and completion status.

---

## 4. Concurrency Management & Race Conditions

### 4.1 Absence of Concurrency Tokens
- In `Models/Trip.cs`, `Models/DriverTrip.cs`, and `Models/Driver.cs`:
  - There is no property marked with `[Timestamp]`.
  - There is no property marked with `[ConcurrencyCheck]`.
  - There is no `byte[] RowVersion` or `Guid ConcurrencyToken`.
- In `Migrations/20241212204809_InitialCreate.cs:275-300`:
  - `Trips` table schema does not include any row version or concurrency stamp column.

### 4.2 Race Conditions in Mutating Operations

#### 1. Concurrent Admin Review (`AdminController.ReviewTrip`)
- **Location:** `Controllers/AdminController.cs:526-552`
- **Race Condition:**
  - Admin A loads trip #10 (`Status == Requested`).
  - Admin B loads trip #10 (`Status == Requested`).
  - Admin A clicks "Approve" -> updates status to `ApprovedByAdmin`, sets `AdminId = AdminA`.
  - Admin B clicks "Reject" -> updates status to `CancelledByAdmin`, sets `AdminId = AdminB`.
  - Both updates succeed; the last write silently overwrites the previous decision. No concurrency conflict is raised.

#### 2. Concurrent Price Setting (`AdminController.SetPrice`)
- **Location:** `Controllers/AdminController.cs:588-675`
- **Race Condition:**
  - Admin A opens `SetPrice/10` and enters 4,000 NOK.
  - Admin B opens `SetPrice/10` and enters 4,500 NOK.
  - Admin A posts. Price is set to 4,000 NOK; status becomes `PriceSet`; email sent to customer.
  - Customer receives notification and approves 4,000 NOK (`Status = ApprovedByCustomer`).
  - Admin B posts stale form with 4,500 NOK.
  - Line 620 checks: `if (tripToUpdate.Status != TripStatus.ApprovedByAdmin) return BadRequest(...)`. Admin B's request is rejected with 400, but if both admins submitted before customer approval, Admin B would overwrite Admin A's price without warning.
  - `SetPriceViewModel` (`Models/ViewModel/SetPriceViewModel.cs`) does not contain any concurrency token to detect that the trip was modified between form render and submission.

#### 3. Concurrent Price Response (`CustomerController.RespondToPrice`)
- **Location:** `Controllers/CustomerController.cs:245-322`
- **Race Condition:**
  - If a customer double-clicks or triggers parallel requests to approve/reject, both execute against the database.
  - Without a concurrency token, parallel requests can cause duplicate notification emails and corrupt transaction states.

#### 4. Critical Race Condition: Driver Assignment (`AdminController.AssignTrip`)
- **Location:** `Controllers/AdminController.cs:720-796`
- **Flawed Code:**
  ```csharp
  // Line 725: Loaded outside transaction
  var trip = await _db.Trips.Include("DriverTrip").Include("Customer").FirstOrDefaultAsync(t => t.Id == model.TripId);
  ...
  // Line 745: Driver loaded outside transaction
  var driver = await _userManager.FindByIdAsync(model.DriverId) as Driver;
  ...
  // Line 753: Check executed in-memory
  if (!driver.IsAvailable) return View(model);
  ...
  // Line 767: Transaction only begins AFTER all checks have passed!
  using var transaction = await _db.Database.BeginTransactionAsync();
  try
  {
      var driverTrip = new DriverTrip { ... };
      trip.Status = TripStatus.Assigned;
      driver.IsAvailable = false;
      _db.DriverTrips.Add(driverTrip);
      await _db.SaveChangesAsync();
      await transaction.CommitAsync();
  ```
- **Vulnerability:**
  - Check-then-act concurrency flaw.
  - Suppose Admin 1 assigns Driver D to Trip 1, and Admin 2 assigns Driver D to Trip 2 concurrently.
  - Both threads execute lines 745–753 simultaneously. Both observe `driver.IsAvailable == true`.
  - Thread 1 begins transaction, writes `driver.IsAvailable = false`, commits.
  - Thread 2 begins transaction, writes `driver.IsAvailable = false`, commits.
  - **Result:** Driver D is double-assigned to two active trips simultaneously!
  - Likewise, if two admins assign different drivers to the same Trip: both see `trip.DriverTrip == null` before transaction start. While the 1-to-1 foreign key constraint on `DriverTrip.TripId` will eventually throw a raw database exception on SQLite/PostgreSQL, the application throws an unhandled 500 error instead of cleanly rejecting the stale update.

#### 5. Driver Completion Race (`DriverController.EndTrip`)
- **Location:** `Controllers/DriverController.cs:108-180`
- **Flawed Code:**
  - No database transaction.
  - No concurrency check.
  - Double submission by driver can trigger redundant email notifications and duplicate payment preparation eligibility.

---

## 5. Cross-Engine Concurrency Strategy (SQLite vs. PostgreSQL)

### 5.1 The Dual-Engine Constraint
- The application uses `Sqlite` in Development (`Program.cs:30`) and `Npgsql` (PostgreSQL) in Production (`Program.cs:37`).
- In SQL Server, `[Timestamp] byte[] RowVersion` automatically maps to a `rowversion` column with database-level automatic value generation.
- In SQLite, SQLite has no built-in `rowversion` type and does NOT automatically update `byte[]` timestamp columns on `UPDATE` statements unless a database trigger or client-side EF Core value generator is attached.
- In PostgreSQL, `xmin` is a system column, but mapping it in EF Core has subtle limitations across migrations.

### 5.2 Recommended Solution: `[ConcurrencyCheck]` with `Guid` Token
The cleanest, 100% portable, database-agnostic pattern that works identically in both SQLite and PostgreSQL without database-specific triggers:

1. **Add ConcurrencyToken to `Trip` and `DriverTrip`:**
   ```csharp
   [ConcurrencyCheck]
   public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
   ```
2. **Hook into `ApplicationDbContext.SaveChangesAsync`:**
   Override `SaveChangesAsync` in `ApplicationDbContext.cs`:
   ```csharp
   public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
   {
       var modifiedTrips = ChangeTracker.Entries<Trip>()
           .Where(e => e.State == EntityState.Modified);
       foreach (var entry in modifiedTrips)
       {
           entry.Entity.ConcurrencyToken = Guid.NewGuid();
       }

       var modifiedDriverTrips = ChangeTracker.Entries<DriverTrip>()
           .Where(e => e.State == EntityState.Modified);
       foreach (var entry in modifiedDriverTrips)
       {
           entry.Entity.ConcurrencyToken = Guid.NewGuid();
       }

       var modifiedDrivers = ChangeTracker.Entries<Driver>()
           .Where(e => e.State == EntityState.Modified);
       foreach (var entry in modifiedDrivers)
       {
           // ApplicationUser also has ConcurrencyStamp
           entry.Entity.ConcurrencyStamp = Guid.NewGuid().ToString();
       }

       return base.SaveChangesAsync(cancellationToken);
   }
   ```
3. **How EF Core Enforces It:**
   When an update occurs, EF Core generates:
   ```sql
   UPDATE "Trips"
   SET "Status" = @p0, "ConcurrencyToken" = @p1, "UpdatedAt" = @p2
   WHERE "Id" = @p3 AND "ConcurrencyToken" = @p4;
   ```
   If another transaction has committed an update in the interim, `@p4` does not match. Exactly `0` rows are updated, and EF Core immediately throws `DbUpdateConcurrencyException`.
4. **Handling in Controllers:**
   Controllers catch `DbUpdateConcurrencyException`:
   ```csharp
   catch (DbUpdateConcurrencyException)
   {
       ModelState.AddModelError(string.Empty, "This trip was modified by another user or process. Please reload and try again.");
       return Conflict("This trip was modified by another user. Please reload."); // Or return View with error
   }
   ```
5. **Form-Based Offline Concurrency Guard:**
   For forms like `SetPrice` and `AssignTrip`:
   - Include `ConcurrencyToken` in the ViewModel (`SetPriceViewModel`, `AssignTripViewModel`).
   - Render as a hidden input in the form: `<input type="hidden" asp-for="ConcurrencyToken" />`.
   - On POST, verify that the loaded entity's `ConcurrencyToken == model.ConcurrencyToken`. If not, immediately reject with a concurrency conflict before even attempting modification!

---

## 6. CSRF & Input Sanitization Audit

### 6.1 CSRF Audit
- **Controller-Level Scan:**
  All existing mutating POST actions across the entire application have `[ValidateAntiForgeryToken]`:
  - `CustomerController`: `CreateTrip` (line 103), `RespondToPrice` (line 244), `CustomerDelete` (line 325).
  - `DriverController`: `EndTrip` (line 107).
  - `AdminController`: `CreateDriver` (line 102), `EditDriver` (line 296), `DeleteDriver` (line 358), `ApproveUser` (line 399), `ReviewTrip` (line 525), `SetPrice` (line 587), `AssignTrip` (line 711), `AddNewTripByAdmin` (line 843), `PreparePayments` (line 1046), `MarkPaymentPaid` (line 1096).
  - `SuperAdminController`: `CreateAdmin` (line 56), `EditAdmin` (line 180), `DeleteAdmin` (line 238).
  - `HomeController`: `Contact` (line 37).
- **Hardening Recommendation:**
  In `Program.cs:75`, configure global validation:
  ```csharp
  builder.Services.AddControllersWithViews(options =>
  {
      options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
  });
  ```
  This guarantees that all present and future POST, PUT, DELETE actions are automatically protected against CSRF without relying on individual action attributes.

### 6.2 Input Sanitization & Data Validation Gaps
- **Model Constraints in `SubmitTripViewModel.cs`:**
  - `Notes` property (line 57): `public string Notes { get; set; } = string.Empty;`
    - Has **NO** `[StringLength]` validation attribute. An attacker can send multi-megabyte payloads.
  - `FromAddress`, `ToAddress`, `FromCity`, `ToCity`:
    - Have `[StringLength(100)]` but no trimming, no character whitelist/filtering, and no sanitization.
- **Email Injection Vector:**
  - In `CustomerController.cs:151-166`:
    ```csharp
    await _emailSender.SendEmailAsync(
        admin.Email!,
        "New Trip Request",
        $"A new trip request has been submitted.\n\n" +
        $"Trip Details:\n" +
        $"Trip ID: {trip.Id}\n" +
        $"Customer: {user.FirstName} {user.LastName}\n" +
        $"From: {trip.FromCity}\n" +
        $"To: {trip.ToCity}\n" + ...
    ```
  - In `EmailSender.cs:42`:
    ```csharp
    var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent: htmlMessage);
    ```
  - Notice that `htmlMessage` is sent as `htmlContent`!
  - If a user enters `<script>`, `<iframe>`, or phishing links in `FromCity` or `Notes`, it is sent directly as HTML content to administrators and drivers.
- **Sanitization Design:**
  - Create a dedicated sanitization service `IInputSanitizer` / `InputSanitizer.cs`:
    - Strips or encodes HTML tags.
    - Trims whitespace and strips dangerous control characters (`\0`, `\b`, CRLF where inappropriate).
    - Normalizes multi-line notes.
  - Update `SubmitTripViewModel` and `Trip` entity to enforce `[StringLength(1000)]` on `Notes`.
  - Apply sanitization before entity persistence and email template construction.

---

## 7. Concrete Proposed Implementation Design

### 7.1 Entity Concurrency Tokens (`Models/Trip.cs` and `Models/DriverTrip.cs`)
```csharp
// In Models/Trip.cs
[ConcurrencyCheck]
public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

// In Models/DriverTrip.cs
[ConcurrencyCheck]
public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
```

### 7.2 DbContext Concurrency Stamp Management (`Data/ApplicationDbContext.cs`)
```csharp
public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    foreach (var entry in ChangeTracker.Entries<Trip>())
    {
        if (entry.State == EntityState.Modified)
        {
            entry.Entity.ConcurrencyToken = Guid.NewGuid();
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
    }

    foreach (var entry in ChangeTracker.Entries<DriverTrip>())
    {
        if (entry.State == EntityState.Modified)
        {
            entry.Entity.ConcurrencyToken = Guid.NewGuid();
            entry.Entity.UpdateAt = DateTime.UtcNow;
        }
    }

    return base.SaveChangesAsync(cancellationToken);
}
```

### 7.3 IDOR Fix in `CustomerController.cs`
Replace lines 188–193:
```csharp
// Before:
var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);
if (trip == null)
{
    return RedirectToAction("Dashboard");
}

// After:
var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id);
if (trip == null || trip.CustomerId != user.Id)
{
    return NotFound();
}
```

### 7.4 Atomic Driver Assignment with Concurrency Guard (`AdminController.cs`)
```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> AssignTrip(AssignTripViewModel model)
{
    if (!ModelState.IsValid)
    {
        await LoadAvailableDrivers(model);
        return View(model);
    }

    await using var transaction = await _db.Database.BeginTransactionAsync();
    try
    {
        var trip = await _db.Trips
            .Include(t => t.DriverTrip)
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == model.TripId);

        if (trip == null)
            return NotFound("Trip not found");

        if (trip.Status != TripStatus.ApprovedByCustomer)
            return BadRequest("Only customer-approved trips can be assigned.");

        if (trip.DriverTrip != null)
        {
            ModelState.AddModelError(string.Empty, "This trip has already been assigned.");
            await LoadAvailableDrivers(model);
            return View(model);
        }

        var driver = await _db.Users.OfType<Driver>()
            .FirstOrDefaultAsync(d => d.Id == model.DriverId);

        if (driver == null || !driver.IsApproved || !driver.IsAvailable)
        {
            ModelState.AddModelError("DriverId", "The selected driver is no longer available.");
            await LoadAvailableDrivers(model);
            return View(model);
        }

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var driverTrip = new DriverTrip
        {
            TripId = trip.Id,
            DriverId = driver.Id,
            AssignedByAdminId = adminId,
            DriverPayment = driver.PricePerKm * trip.Distance,
            Status = TripStatus.Assigned,
            AssignmentDate = DateTime.UtcNow,
            UpdateAt = DateTime.UtcNow
        };

        trip.Status = TripStatus.Assigned;
        driver.IsAvailable = false;

        _db.DriverTrips.Add(driverTrip);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Success"] = $"Trip successfully assigned to {driver.UserName}";
        return RedirectToAction(nameof(TripRequests));
    }
    catch (DbUpdateConcurrencyException)
    {
        await transaction.RollbackAsync();
        ModelState.AddModelError(string.Empty, "Another administrator modified this trip or driver assignment. Please reload.");
        await LoadAvailableDrivers(model);
        return View(model);
    }
}
```

### 7.5 Input Sanitizer Service (`Services/InputSanitizer.cs`)
```csharp
namespace logirack.Services;

public static class InputSanitizer
{
    public static string Sanitize(string? input, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var trimmed = input.Trim();
        // Remove dangerous HTML/script injection characters
        var sanitized = System.Net.WebUtility.HtmlEncode(trimmed);
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength];
        }
        return sanitized;
    }
}
```

---

## 8. Summary Table of Files, Lines, and Required Changes

| File Path | Lines | Category | Issue | Proposed Remediation |
|---|---|---|---|---|
| `Program.cs` | 75 | CSRF | Missing global antiforgery validation | Add `AutoValidateAntiforgeryTokenAttribute` in `AddControllersWithViews`. |
| `Models/Trip.cs` | 46, 50 | Concurrency / Input | Missing `ConcurrencyToken`, unconstrained `Notes` | Add `[ConcurrencyCheck] public Guid ConcurrencyToken`, add `[StringLength(1000)]` to `Notes`. |
| `Models/DriverTrip.cs` | 31 | Concurrency | Missing `ConcurrencyToken` | Add `[ConcurrencyCheck] public Guid ConcurrencyToken`. |
| `Data/ApplicationDbContext.cs` | 178 | Concurrency | No auto-refresh of concurrency tokens | Override `SaveChangesAsync` to refresh tokens on entity updates. |
| `Controllers/CustomerController.cs` | 188–193 | IDOR | `OrderSuccess` returns 302 redirect for unauthorized trips | Return `NotFound()` on IDOR attempt. |
| `Controllers/CustomerController.cs` | 121–142 | Sanitization | Raw strings saved to DB and used in HTML emails | Sanitize `FromCity`, `ToCity`, `FromAddress`, `ToAddress`, `Notes` via `InputSanitizer`. |
| `Controllers/AdminController.cs` | 526–552 | Concurrency | `ReviewTrip` lacks concurrency exception handling | Catch `DbUpdateConcurrencyException` and return 409 Conflict / model error. |
| `Controllers/AdminController.cs` | 588–675 | Concurrency | `SetPrice` lacks concurrency exception handling | Catch `DbUpdateConcurrencyException` and report stale update. |
| `Controllers/AdminController.cs` | 720–796 | Concurrency | `AssignTrip` check-then-act race condition | Enclose checks in transaction, catch `DbUpdateConcurrencyException`. |
| `Controllers/DriverController.cs` | 16 | Access Control | Lacks `[ApprovedOnly]` attribute | Add `[ApprovedOnly]` to `DriverController`. |
| `Controllers/DriverController.cs` | 118 | State Machine | `EndTrip` rejects `InProgress` status | Allow `TripStatus.Assigned` and `TripStatus.InProgress`. |
| `Models/ViewModel/SubmitTripViewModel.cs` | 57 | Input Validation | `Notes` has no `[StringLength]` | Add `[StringLength(1000, ErrorMessage = "Notes cannot exceed 1000 characters.")]`. |
