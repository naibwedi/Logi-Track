# Handoff Report: Access Control, Concurrency & Security Hardening (R1)

**Agent:** explorer_survey_1  
**Milestone:** M1 — Survey & Architectural Analysis  
**Date:** 2026-10-27  
**Full Technical Survey Report:** `C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\explorer_survey_1\survey_security.md`

---

## 1. Observation

1. **IDOR Response in `CustomerController.cs:188-193`:**
   Direct inspection of `Controllers/CustomerController.cs` reveals:
   ```csharp
   var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);
   if (trip == null)
   {
       return RedirectToAction("Dashboard");
   }
   ```
   When requesting an ID not owned by the authenticated customer, the response is an HTTP 302 Found redirect to `/Customer/Dashboard`. In contrast, the acceptance criteria states: *"Direct object reference (IDOR) attempts (e.g. requesting another user's trip details via URL manipulation) return 403 Forbidden or 404 Not Found."*

2. **Entity Concurrency Configuration in `Models/Trip.cs` and `Models/DriverTrip.cs`:**
   Inspection of `Models/Trip.cs` (lines 1–67) and `Models/DriverTrip.cs` (lines 1–32) reveals:
   - Neither entity defines a property with `[Timestamp]`, `[ConcurrencyCheck]`, or `byte[] RowVersion`.
   - Migration file `Migrations/20241212204809_InitialCreate.cs` lines 275–300 confirms the `Trips` table contains no rowversion, timestamp, or concurrency stamp columns.

3. **Trip State Machine Inconsistency in `Models/Trip.cs:62` and `Controllers/DriverController.cs:118-121`:**
   `TripStatus.InProgress` exists in `Models/Trip.cs:62` and is counted in `AdminController.cs:202`, but no controller action transitions a trip into `InProgress`. In `Controllers/DriverController.cs:118`:
   ```csharp
   if (driverTrip == null || driverTrip.Status != TripStatus.Assigned)
   {
       return NotFound("Trip not found or already completed.");
   }
   ```
   A trip in `InProgress` state will be rejected by `DriverController.EndTrip`.

4. **Check-Then-Act Race Condition in `Controllers/AdminController.cs:725-786`:**
   In `AdminController.AssignTrip`:
   - Line 725: `var trip = await _db.Trips...FirstOrDefaultAsync(t => t.Id == model.TripId);` (queried outside transaction)
   - Line 745: `var driver = await _userManager.FindByIdAsync(model.DriverId) as Driver;` (queried outside transaction)
   - Line 753: `if (!driver.IsAvailable) return View(model);` (checked in-memory outside transaction)
   - Line 767: `using var transaction = await _db.Database.BeginTransactionAsync();` (transaction begins only AFTER state validation)
   Two admins assigning the same driver concurrently both observe `driver.IsAvailable == true`, causing double-assignment.

5. **CSRF & Input Sanitization in `Program.cs:75`, `SubmitTripViewModel.cs:57`, and `CustomerController.cs:121-165`:**
   - `Program.cs:75` registers `builder.Services.AddControllersWithViews()` without `AutoValidateAntiforgeryTokenAttribute`.
   - `Models/ViewModel/SubmitTripViewModel.cs:57` defines `public string Notes { get; set; } = string.Empty;` with no length limit or character validation.
   - `CustomerController.cs:121-144` stores raw un-sanitized strings in `_db.Trips`.
   - `CustomerController.cs:151-165` concatenates raw user strings (`FromCity`, `ToCity`, etc.) into an email sent via `EmailSender.cs:42`, which delivers it as `htmlContent`, creating an email HTML/script injection vector.

---

## 2. Logic Chain

1. **IDOR Inadequacy:**
   - *Premise (from Observation 1):* Accessing `/Customer/OrderSuccess/{other_user_trip_id}` executes `RedirectToAction("Dashboard")`.
   - *Inference:* An automated test or security audit requesting another user's trip returns HTTP 302 instead of HTTP 404 or HTTP 403.
   - *Deduction:* `CustomerController.OrderSuccess` fails acceptance criterion 1 ("IDOR attempts return 403 Forbidden or 404 Not Found") unless refactored to return `NotFound()`.

2. **Concurrency & Race Conditions:**
   - *Premise (from Observation 2 & 4):* Entities lack `[ConcurrencyCheck]` / `[Timestamp]`, and `AssignTrip` performs availability checks before acquiring a transaction lock.
   - *Inference:* Multiple concurrent requests to `ReviewTrip`, `SetPrice`, `RespondToPrice`, and `AssignTrip` can read the same state and execute competing modifications.
   - *Deduction:* Without optimistic concurrency tokens (such as `[ConcurrencyCheck] Guid ConcurrencyToken`) checked during `SaveChangesAsync`, stale updates will silently overwrite database state without throwing `DbUpdateConcurrencyException`, violating acceptance criterion 2 ("stale trip updates are rejected without corrupting database state").

3. **State Machine Deadlock with R2:**
   - *Premise (from Observation 3):* R2 requires active trip GPS tracking during `InProgress` state, but `DriverController.EndTrip` strictly enforces `driverTrip.Status == TripStatus.Assigned`.
   - *Inference:* If a driver or simulation moves a trip to `InProgress`, the driver can never complete the trip via `EndTrip`.
   - *Deduction:* `DriverController.EndTrip` must be updated to permit completion when status is `Assigned` or `InProgress`.

4. **Input Sanitization & CSRF:**
   - *Premise (from Observation 5):* User notes and addresses are unbounded and un-sanitized, and email notifications use raw user input as HTML email content.
   - *Inference:* A malicious customer can inject HTML/script tags or excessively long payloads into trip requests that compromise email clients and display layouts.
   - *Deduction:* Adding `[StringLength(1000)]` on `Notes`, implementing an `InputSanitizer` utility, and adding global `AutoValidateAntiforgeryTokenAttribute` in `Program.cs` resolves these vulnerabilities.

---

## 3. Caveats

1. **Database Provider Parity:** The application runs SQLite in development and PostgreSQL in production. Standard SQL Server `[Timestamp] byte[] RowVersion` does not auto-generate in SQLite. The proposed `[ConcurrencyCheck] Guid ConcurrencyToken` pattern with `SaveChangesAsync` auto-regeneration was verified to be fully cross-compatible, but test suites run against SQLite while production targets PostgreSQL.
2. **SignalR Authorization for Live GPS (R2):** While `TripHub.JoinTrip` already contains participant authorization checking `trip.CustomerId == userId` or `trip.DriverTrip.DriverId == userId`, hub methods for broadcasting coordinates (`SendLocationUpdate`) have not yet been implemented and must be guarded in R2.
3. **Automated PDF Invoices (R3):** Customer-facing invoice download endpoints do not currently exist; their authorization guards must be built from scratch following the IDOR patterns established here.

---

## 4. Conclusion

The Logi-Track codebase possesses a sound foundation with role-based authentication and existing `[ValidateAntiForgeryToken]` annotations on existing POST actions. However, four critical vulnerabilities must be remediated to fulfill R1:
1. **Fix IDOR in `CustomerController.OrderSuccess`:** Change `RedirectToAction("Dashboard")` to `NotFound()`.
2. **Implement Optimistic Concurrency:** Add `[ConcurrencyCheck] public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();` on `Trip` and `DriverTrip`, auto-refresh the token on entity modification in `ApplicationDbContext.SaveChangesAsync`, and catch `DbUpdateConcurrencyException` in `AdminController.ReviewTrip`, `AdminController.SetPrice`, `AdminController.AssignTrip`, `CustomerController.RespondToPrice`, and `DriverController.EndTrip`.
3. **Transaction Hardening in `AdminController.AssignTrip`:** Enclose driver availability and assignment checks inside an explicit database transaction and verify availability with concurrency checks.
4. **Input Sanitization & Global CSRF Filter:** Add `AutoValidateAntiforgeryTokenAttribute` to `Program.cs`, add `[StringLength(1000)]` to `SubmitTripViewModel.Notes`, and sanitize address/notes strings via `InputSanitizer` before persistence and email dispatch.
5. **Trip Lifecycle Alignment:** Update `DriverController.EndTrip` to accept both `TripStatus.Assigned` and `TripStatus.InProgress`.

---

## 5. Verification Method

1. **Existing Baseline Test Suite:**
   Run: `dotnet test`
   Expected result: All 21 existing smoke and lifecycle tests in `tests/LogiTrack.Tests` pass with 0 failures.

2. **Verifying IDOR Fix:**
   - Create two customer users: `customerA` and `customerB`.
   - Submit a trip as `customerA` (obtaining `tripIdA`).
   - Authenticate client as `customerB` and perform `GET /Customer/OrderSuccess/{tripIdA}` and `GET /Customer/TripDetails/{tripIdA}`.
   - Invalidation condition: Response returns HTTP 200 OK or HTTP 302 Redirect.
   - Verification success: Response returns HTTP 404 Not Found (or HTTP 403 Forbidden).

3. **Verifying Optimistic Concurrency and Stale Update Rejection:**
   - Load trip #X in `AdminController.SetPrice` in two separate concurrent contexts or retrieve original `ConcurrencyToken`.
   - Context 1 updates price and commits.
   - Context 2 attempts to update price using stale `ConcurrencyToken` or stale entity state.
   - Invalidation condition: Context 2 succeeds without exception or corrupts database state.
   - Verification success: Context 2 throws `DbUpdateConcurrencyException` or returns HTTP 409 Conflict / model error, and database retains Context 1's state.

4. **Verifying Input Sanitization:**
   - Submit trip with HTML tags: `<script>alert('xss')</script>` in `Notes` and `FromAddress`.
   - Verify stored string in database is sanitized or HTML-encoded and does not execute script in rendered views or email bodies.
