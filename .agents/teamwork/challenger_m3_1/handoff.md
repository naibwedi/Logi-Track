# Handoff Report: Challenger M3.1 (Invoicing Download Endpoints & Access Control)

## 1. Observation

### Verification of Code Under Test
- **CustomerController Invoicing Endpoint**:
  - File: `Controllers/CustomerController.cs`, lines 261–296:
    ```csharp
    [HttpGet]
    public async Task<IActionResult> DownloadInvoice(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var trip = await _db.Trips
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id);

        if (trip == null)
        {
            _logger.LogWarning("Customer {UserId} attempted unauthorized or non-existent invoice download for trip {TripId}", user.Id, id);
            return NotFound();
        }

        if (trip.Status != TripStatus.Completed)
        {
            _logger.LogWarning("Invoice download rejected for trip {TripId}: status is {Status}, not Completed", trip.Id, trip.Status);
            return BadRequest("Invoice is only available for completed trips.");
        }
        ...
        var pdfBytes = _invoiceService.GenerateInvoicePdf(trip, customer);
        return File(pdfBytes, "application/pdf", $"Invoice_Trip_{trip.Id}.pdf");
    }
    ```
  - Class-level attributes:
    - Line 16: `[Authorize(Roles = "Customer")]`
    - Line 17: `[ApprovedOnly]`

- **AdminController Invoicing Endpoint**:
  - File: `Controllers/AdminController.cs`, lines 1193–1229:
    ```csharp
    [HttpGet]
    public async Task<IActionResult> DownloadInvoice(int id)
    {
        var trip = await _db.Trips
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (trip == null)
        {
            return NotFound();
        }

        if (trip.Status != TripStatus.Completed)
        {
            return BadRequest("Invoice is only available for completed trips.");
        }
        ...
        var pdfBytes = _invoiceService.GenerateInvoicePdf(trip, customer);
        return File(pdfBytes, "application/pdf", $"Invoice_Trip_{trip.Id}.pdf");
    }
    ```
  - Class-level attribute: `[Authorize(Roles = "Admin")]`.

### Empirical Test Execution and Results
- Created adversarial stress test harness: `tests/LogiTrack.Tests/ChallengerInvoiceAccessControlStressTests.cs` (37 stress test executions):
  1. `CustomerBeta_AttemptingToDownload_CustomerAlphaInvoice_ReturnsNotFound_IdorBlocked`: PASSED (asserted `404 NotFound` on cross-customer trip ID manipulation).
  2. `CustomerAlpha_AttemptingToDownload_CustomerBetaInvoice_ReturnsNotFound_IdorBlocked`: PASSED (asserted `404 NotFound`).
  3. `CustomerAlpha_DownloadingOwnCompletedInvoice_SucceedsWithValidPdf`: PASSED (asserted `200 OK`, `application/pdf`, filename `Invoice_Trip_{id}.pdf`, magic `%PDF-`, size > 1 KB).
  4. `Customer_DownloadingInvoice_ForNonCompletedTrip_ReturnsBadRequest`: PASSED across all 8 non-completed states:
     - `TripStatus.Requested` -> 400 BadRequest
     - `TripStatus.ApprovedByAdmin` -> 400 BadRequest
     - `TripStatus.PriceSet` -> 400 BadRequest
     - `TripStatus.ApprovedByCustomer` -> 400 BadRequest
     - `TripStatus.Assigned` -> 400 BadRequest
     - `TripStatus.InProgress` -> 400 BadRequest
     - `TripStatus.CancelledByAdmin` -> 400 BadRequest
     - `TripStatus.CanceledByCustomer` -> 400 BadRequest
  5. `Admin_DownloadingInvoice_ForNonCompletedTrip_ReturnsBadRequest`: PASSED across all 8 non-completed states:
     - `TripStatus.Requested` -> 400 BadRequest
     - `TripStatus.ApprovedByAdmin` -> 400 BadRequest
     - `TripStatus.PriceSet` -> 400 BadRequest
     - `TripStatus.ApprovedByCustomer` -> 400 BadRequest
     - `TripStatus.Assigned` -> 400 BadRequest
     - `TripStatus.InProgress` -> 400 BadRequest
     - `TripStatus.CancelledByAdmin` -> 400 BadRequest
     - `TripStatus.CanceledByCustomer` -> 400 BadRequest
  6. `Customer_DownloadingInvoice_ForNonExistentTrip_ReturnsNotFound`: PASSED for boundary IDs `999999`, `0`, `-1`, and `2147483647` (all returned `404 NotFound`).
  7. `Admin_DownloadingInvoice_ForNonExistentTrip_ReturnsNotFound`: PASSED for boundary IDs `999999`, `0`, `-1`, and `2147483647` (all returned `404 NotFound`).
  8. `AnonymousRequest_ToCustomerInvoice_RedirectsToLogin`: PASSED (`302 Redirect` to `/Identity/Account/Login`).
  9. `AnonymousRequest_ToAdminInvoice_RedirectsToLogin`: PASSED (`302 Redirect` to `/Identity/Account/Login`).
  10. `Driver_CannotAccess_CustomerDownloadInvoice`: PASSED (`403 Forbidden` / AccessDenied redirect).
  11. `Driver_CannotAccess_AdminDownloadInvoice`: PASSED (`403 Forbidden` / AccessDenied redirect).
  12. `Customer_CannotAccess_AdminDownloadInvoice`: PASSED (`403 Forbidden` / AccessDenied redirect).
  13. `PdfInvoiceService_HandlesUnicodeAndSpecialCharacters_WithoutCrash`: PASSED (tested Norwegian characters æ, ø, å in addresses and goods types).
  14. `PdfInvoiceService_HandlesNullCustomerOptionalFields_WithoutNullReferenceException`: PASSED.
  15. `UnapprovedCustomer_CannotDownloadInvoice_RedirectsToApprovalPending`: PASSED.
  16. `Admin_CanDownloadInvoice_ForCompletedTrip_WithMinimalCustomerProfile`: PASSED.
  17. `Customer_PostRequest_ToDownloadInvoiceEndpoint_IsRejected`: PASSED.

- **Stress Test Command Output**:
  ```pwsh
  dotnet test --filter "FullyQualifiedName~ChallengerInvoiceAccessControlStressTests"
  ```
  Result:
  ```
  Starting test execution, please wait...
  A total of 1 test files matched the specified pattern.

  Passed!  - Failed:     0, Passed:    37, Skipped:     0, Total:    37, Duration: 3 s - LogiTrack.Tests.dll (net8.0)
  ```

- **Full Test Suite Command Output**:
  ```pwsh
  dotnet test
  ```
  Result:
  ```
  Passed!  - Failed:     0, Passed:   275, Skipped:     0, Total:   275, Duration: 6 s - LogiTrack.Tests.dll (net8.0)
  ```

- **Compilation Check Output**:
  ```pwsh
  dotnet build --warnaserror
  ```
  Result:
  ```
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:02.32
  ```

---

## 2. Logic Chain

1. **IDOR Rejection (Objective 1)**:
   - In `CustomerController.DownloadInvoice(int id)`, the database query explicitly filters by both trip ID and customer ID:
     `_db.Trips.Include(t => t.Customer).FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == user.Id)`
   - When Customer B (an authenticated customer) submits a request for a completed trip owned by Customer A (`/Customer/DownloadInvoice/{tripAId}`), the query evaluates to null and the controller executes `return NotFound()`.
   - Empirically confirmed by `CustomerBeta_AttemptingToDownload_CustomerAlphaInvoice_ReturnsNotFound_IdorBlocked` and `CustomerAlpha_AttemptingToDownload_CustomerBetaInvoice_ReturnsNotFound_IdorBlocked`: both returned HTTP 404 NotFound. No trip data or invoice bytes are leaked.

2. **Non-Completed Trip Rejection (Objective 2)**:
   - Both `CustomerController.DownloadInvoice` and `AdminController.DownloadInvoice` enforce `trip.Status == TripStatus.Completed`. If `trip.Status != TripStatus.Completed`, the controller immediately executes `return BadRequest("Invoice is only available for completed trips.")`.
   - Empirically confirmed across all 8 non-completed statuses (`Requested`, `ApprovedByAdmin`, `PriceSet`, `ApprovedByCustomer`, `Assigned`, `InProgress`, `CancelledByAdmin`, `CanceledByCustomer`) for both Customer and Admin endpoints. All 16 status test cases returned HTTP 400 BadRequest.

3. **Non-Existent Trip IDs (Objective 3)**:
   - When a requested trip ID does not exist in the database (tested with boundary values `999999`, `0`, `-1`, and `int.MaxValue`), both `CustomerController.DownloadInvoice` and `AdminController.DownloadInvoice` return HTTP 404 NotFound.
   - Empirically confirmed across all boundary IDs for both endpoints.

4. **Unauthenticated / Anonymous Access Challenge (Objective 4)**:
   - `CustomerController` is guarded by `[Authorize(Roles = "Customer")]` and `[ApprovedOnly]`, and `AdminController` is guarded by `[Authorize(Roles = "Admin")]`.
   - Requests made without an authentication cookie receive HTTP 302 redirecting to `/Identity/Account/Login?ReturnUrl=...`.
   - Empirically confirmed by `AnonymousRequest_ToCustomerInvoice_RedirectsToLogin` and `AnonymousRequest_ToAdminInvoice_RedirectsToLogin`.

5. **Cross-Role and Policy Boundaries**:
   - Drivers cannot access `/Customer/DownloadInvoice` or `/Admin/DownloadInvoice`.
   - Customers cannot access `/Admin/DownloadInvoice`.
   - Unapproved customers are intercepted by `[ApprovedOnly]` and redirected to `ApprovalPending`.
   - All tests pass with zero errors, zero warnings under `--warnaserror`, and 100% test pass rate across 275 tests.

---

## 3. Caveats
No caveats. All four core verification objectives and additional boundary conditions were empirically verified via direct HTTP client requests through `WebApplicationFactory` and unit tests.

---

## 4. Conclusion
**Verdict: APPROVE**

Milestone 3's Invoicing Download Endpoints and Access Control meet all security and functional requirements:
- IDOR attempts between distinct customer accounts strictly yield HTTP 404 NotFound.
- Attempts to download invoices for non-completed trips across the entire lifecycle status matrix strictly yield HTTP 400 BadRequest for both customers and administrators.
- Requests with non-existent or out-of-bound trip IDs strictly return HTTP 404 NotFound.
- Anonymous requests are challenged and redirected to login.
- Cross-role boundaries and unapproved account states are strictly enforced.

---

## 5. Verification Method

To independently reproduce the empirical findings:

1. **Run the Milestone 3 Challenger Stress Test Suite**:
   ```pwsh
   dotnet test --filter "FullyQualifiedName~ChallengerInvoiceAccessControlStressTests"
   ```
   Expected output: `Passed! - Failed: 0, Passed: 37, Skipped: 0, Total: 37`.

2. **Run the Full Test Suite**:
   ```pwsh
   dotnet test
   ```
   Expected output: `Passed! - Failed: 0, Passed: 275, Skipped: 0, Total: 275`.

3. **Run Zero-Warning Compilation Check**:
   ```pwsh
   dotnet build --warnaserror
   ```
   Expected output: `Build succeeded. 0 Warning(s), 0 Error(s)`.

4. **Inspect Source Files**:
   - `tests/LogiTrack.Tests/ChallengerInvoiceAccessControlStressTests.cs`
   - `Controllers/CustomerController.cs` (lines 261–296)
   - `Controllers/AdminController.cs` (lines 1193–1229)
