# Forensic Audit Report: Milestone 1

**Work Product:** Milestone 1 (Access Control, Concurrency & Security Hardening)  
**Auditor:** auditor_m1_1  
**Integrity Mode:** Development Mode (per `ORIGINAL_REQUEST.md`)  
**Verdict:** **`CLEAN`**

---

### Phase Results

| # | Check Name | Status | Details |
|---|------------|--------|---------|
| 1 | Hardcoded test values / mock bypasses | **PASS** | Grep audit across `Controllers/`, `Services/`, `Models/`, `Data/` found zero mock bypasses or hardcoded test values. |
| 2 | Facade implementations / dummy returns | **PASS** | All new components (`InputSanitizer`, `ApplicationDbContext.UpdateConcurrencyTokens`, controller actions) contain real logic, zero dummy/constant returns. |
| 3 | Pre-populated verification artifacts | **PASS** | File system search for `*.log`, `*result*`, `*output*` returned 0 pre-existing result files. |
| 4 | Concurrency token authenticity | **PASS** | `[ConcurrencyCheck] Guid ConcurrencyToken` is mapped in EF Core, refreshed in `SaveChangesAsync`, and proven to throw `DbUpdateConcurrencyException` on concurrent stale updates. |
| 5 | Input sanitizer authenticity | **PASS** | `InputSanitizer.Sanitize` strips tags (`<script>`, `<iframe>`, `<img>`), filters control characters, encodes HTML entities, and enforces bounds; proven not to be a no-op or echo. |
| 6 | Transaction guards authenticity | **PASS** | `AdminController.AssignTrip` establishes an explicit transaction, commits on success, and executes `await transaction.RollbackAsync()` on all invalid states and exceptions. |
| 7 | Test suite integrity & execution | **PASS** | 59/59 tests executed and passed (0 failed, 0 skipped, 0 ignored). |

---

## 1. Observation

### 1.1 Direct Source Code Observations
- **`Models/Trip.cs` (lines 48-49) & `Models/DriverTrip.cs` (lines 28-29)**:
  ```csharp
  [ConcurrencyCheck]
  public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
  ```
- **`Data/ApplicationDbContext.cs` (lines 180-211)**:
  `SaveChangesAsync` and `SaveChanges` override invokes `UpdateConcurrencyTokens()`.
  ```csharp
  private void UpdateConcurrencyTokens()
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
  }
  ```
- **`Services/InputSanitizer.cs` (lines 10-48)**:
  Pure C# implementation using `HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);` and `WebUtility.HtmlEncode(stripped)`.
  - Filters `char.IsControl(c)` while allowing `\r`, `\n`, `\t`.
  - Strips HTML markup.
  - HTML-encodes residual characters.
  - Truncates to `maxLength`.
- **`Controllers/AdminController.cs` (lines 734-802)**:
  `AssignTrip`:
  - Starts transaction: `await using var transaction = await _db.Database.BeginTransactionAsync();`
  - Rejection branches (`trip == null`, `trip.Status != TripStatus.ApprovedByCustomer`, `trip.DriverTrip != null`, `driver == null`, `!driver.IsAvailable`, `!driver.IsApproved`) each call `await transaction.RollbackAsync();`.
  - Concurrency exception handler catches `DbUpdateConcurrencyException`, executes `await transaction.RollbackAsync();`, and returns error view.
  - General exception handler catches `Exception`, executes `await transaction.RollbackAsync();`, and returns error view.
  - Commits only after `await _db.SaveChangesAsync()` via `await transaction.CommitAsync();`.
- **`Controllers/CustomerController.cs` (lines 191-202)**:
  `OrderSuccess(int id)`:
  ```csharp
  var user = await _userManager.GetUserAsync(User);
  if (user is null) return Challenge();
  var trip = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);
  if (trip == null)
  {
      return NotFound();
  }
  return View(trip);
  ```
  Returns `NotFound()` (404) directly if trip does not exist or does not belong to the authenticated user.
- **`Program.cs` (lines 75-78)**:
  ```csharp
  builder.Services.AddControllersWithViews(options =>
  {
      options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
  });
  ```

### 1.2 Static Analysis & Grep Searches
- Searching for `IntegrationTest` in production code (`Controllers/`, `Services/`, `Models/`, `Data/`):
  - Result: 0 matches (confined exclusively to `tests/LogiTrack.Tests/ApplicationSmokeTests.cs` and `tests/LogiTrack.Tests/ConcurrencyTests.cs`).
- Searching for `Skip` in `tests/LogiTrack.Tests/`:
  - Result: 0 matches.
- Searching for pre-populated result artifacts (`find . -name '*.log' -o -name '*result*' -o -name '*output*'`):
  - Result: 0 matches.

### 1.3 Empirical Execution Results
- **Independent Test Harness (`ForensicVerifier`)**:
  Executed against compiled assembly with SQLite in-memory provider:
  ```
  ==================================================
  FORENSIC INTEGRITY EMPIRICAL VERIFICATION HARNESS
  ==================================================

  [CHECK 1] Testing InputSanitizer Authenticity & Rigor...
  [PASS] Strip script tag and encode inner content
  [PASS] Strip standalone script tag with src
  [PASS] Strip img onerror tag
  [PASS] Strip non-printable control characters except newline
  [PASS] Enforce max length truncation
  [PASS] Null input returns empty string
  [PASS] Whitespace returns empty string
        Residual encoding output: 'A &amp; B &gt; C &lt; D'
  [PASS] Residual markup characters properly sanitized/encoded

  [CHECK 2] Testing ConcurrencyToken Authenticity in EF Core...
        Original Trip ConcurrencyToken: 9b2388b3-6224-453f-8b82-0898f307a19d
  [PASS] Initial ConcurrencyToken is non-empty GUID
        Updated Trip ConcurrencyToken:  a6854460-ceae-4b20-bee8-952b780cd287
  [PASS] ConcurrencyToken was automatically updated in SaveChangesAsync

  [CHECK 2.2] Testing Stale Update Conflict Rejection on Trip...
        Client A updated trip successfully.
        Caught expected DbUpdateConcurrencyException: The database operation was expected to affect 1 row(s), but actually affected 0 row(s); data may have been modified or deleted since entities were loaded. See https://go.microsoft.com/fwlink/?LinkId=527962 for information on understanding and handling optimistic concurrency exceptions.
  [PASS] Concurrent stale update on Trip triggered DbUpdateConcurrencyException

  [CHECK 2.3] Testing Stale Update Conflict Rejection on DriverTrip...
        Client A updated DriverTrip to InProgress.
        Caught expected DriverTrip DbUpdateConcurrencyException: The database operation was expected to affect 1 row(s), but actually affected 0 row(s); data may have been modified or deleted since entities were loaded. See https://go.microsoft.com/fwlink/?LinkId=527962 for information on understanding and handling optimistic concurrency exceptions.
  [PASS] Concurrent stale update on DriverTrip triggered DbUpdateConcurrencyException

  [CHECK 3] Testing Transaction Rollback Integrity...
  [PASS] Transaction RollbackAsync strictly prevented database persistence

  ==================================================
  ALL FORENSIC CHECKS PASSED EMPIRICALLY (0 failures)
  ```
- **Project Build**:
  `dotnet build --warnaserror` -> `0 Warning(s), 0 Error(s)`.
- **Project Test Execution**:
  `dotnet test` -> `Passed! - Failed: 0, Passed: 59, Skipped: 0, Total: 59, Duration: 5 s`.

---

## 2. Logic Chain

1. **Absence of Deception**:
   - Comprehensive grep searches and structural diff reviews show no hardcoded test passwords, emails, mock bypasses, or dummy returns in production code.
2. **Authenticity of Concurrency Control**:
   - EF Core metadata confirms `ConcurrencyToken` is configured as a concurrency token (`b.Property<Guid>("ConcurrencyToken").IsConcurrencyToken()`).
   - When modified, `UpdateConcurrencyTokens()` assigns a new GUID to `CurrentValue`, retaining the snapshotted token in `OriginalValue`.
   - The generated SQL appends `AND "ConcurrencyToken" = @originalToken`.
   - In our empirical harness, simulating concurrent contexts proved that a stale update matches 0 rows and triggers a genuine `DbUpdateConcurrencyException`, protecting database state.
3. **Authenticity of Sanitization**:
   - `InputSanitizer.Sanitize` applies multi-stage processing: control character elimination, regular-expression tag stripping, and HTML entity encoding.
   - Attack vectors (`<script>`, `<img onerror=...>`, nested tags, unclosed tags) were empirically neutralized. The utility is genuine and functional.
4. **Authenticity of Transaction Isolation**:
   - In `AdminController.AssignTrip`, `BeginTransactionAsync()` is called before state checks (`IsAvailable`, `Status`), and every premature exit executes `RollbackAsync()`.
   - Our empirical test confirmed that `RollbackAsync()` leaves 0 uncommitted entities in the database.
5. **Authenticity of Access Control (IDOR)**:
   - `CustomerController.OrderSuccess` checks `t.Id == id && t.CustomerId == user.Id`. Unauthorized queries return `NotFound()` (404) rather than 302 redirect or 200 OK.
6. **Integrity Mode Conformance**:
   - Under Development Mode (and evaluated across all modes), there are no prohibited patterns (no hardcoded outputs, no facade implementations, no fabricated logs).

---

## 3. Caveats

- **Scope Boundary**: This audit specifically covered Milestone 1 deliverables. Downstream deliverables (SignalR GPS telemetry in M2, PDF generation in M3, and test suite extensions in M4) will be subject to subsequent milestone audits.
- No caveats remain regarding Milestone 1 deliverables.

---

## 4. Conclusion

The Milestone 1 work product satisfies all integrity criteria. No shortcuts, mock bypasses, dummy implementations, or fabricated outputs exist.
Binary Verdict: **`CLEAN`**.

---

## 5. Verification Method

To independently reproduce and verify this audit verdict:

1. **Verify Clean Build with Zero Warnings**:
   ```powershell
   dotnet build --warnaserror
   ```
   *Expected outcome*: Build succeeded with 0 warnings and 0 errors.

2. **Execute Complete Test Suite**:
   ```powershell
   dotnet test
   ```
   *Expected outcome*: 59 passed, 0 failed, 0 skipped.

3. **Inspect Concurrency Token and Transaction Code**:
   - Inspect `Data/ApplicationDbContext.cs` lines 180-211 to confirm token regeneration.
   - Inspect `Controllers/AdminController.cs` lines 734-802 to confirm `BeginTransactionAsync`, `CommitAsync`, and `RollbackAsync` branches.
   - Inspect `Controllers/CustomerController.cs` lines 191-202 to confirm 404 return on IDOR attempts.
