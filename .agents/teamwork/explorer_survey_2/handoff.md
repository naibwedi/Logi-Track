# Handoff Report: R2 Live Driver GPS & Map Tracking Simulation Survey

**Agent**: explorer_survey_2  
**Recipient**: orchestrator_1  
**Timestamp**: 2026-10-27T19:25:00Z  
**Type**: Hard (Survey Task Complete)  

---

## 1. Observation

1. **SignalR Configuration in `Program.cs`**:
   - `Program.cs` lines 88 and 152:
     ```csharp
     88: builder.Services.AddSignalR();
     ...
     152: app.MapHub<TripHub>("/tripHub");
     ```
   - `Program.cs` line 133:
     ```csharp
     133: context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
     ```
     `geolocation=()` explicitly disables browser hardware geolocation, confirming that driver GPS coordinates must be simulated in code rather than queried from hardware sensors.

2. **Existing SignalR Hub (`Hubs/TripHub.cs`)**:
   - `Hubs/TripHub.cs` lines 9–45:
     ```csharp
     9:  [Authorize]
     10: public sealed class TripHub(ApplicationDbContext db) : Hub
     11: {
     12:     public override async Task OnConnectedAsync()
     13:     {
     14:         if (Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("SuperAdmin") == true)
     15:         {
     16:             await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
     17:         }
     18:         await base.OnConnectedAsync();
     19:     }
     20: 
     21:     public async Task JoinTrip(int tripId)
     22:     {
     ...
     30:         var isAdmin = Context.User!.IsInRole("Admin") || Context.User.IsInRole("SuperAdmin");
     31:         var isParticipant = isAdmin || await db.Trips.AnyAsync(trip =>
     32:             trip.Id == tripId &&
     33:             (trip.CustomerId == userId || (trip.DriverTrip != null && trip.DriverTrip.DriverId == userId)));
     34: 
     35:         if (!isParticipant)
     36:         {
     37:             throw new HubException("You are not allowed to subscribe to this trip.");
     38:         }
     39: 
     40:         await Groups.AddToGroupAsync(Context.ConnectionId, $"trip_{tripId}");
     41:     }
     ...
     43:     public Task LeaveTrip(int tripId) =>
     44:         Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trip_{tripId}");
     45: }
     ```
   - **Observation**: `TripHub` includes group subscription and IDOR verification in `JoinTrip`, but **contains zero methods for updating or broadcasting GPS coordinates** (no `SendLocationUpdate` or equivalent).

3. **Static Assets & Client Scripts**:
   - `Views/Shared/_Layout.cshtml` lines 141–151:
     SignalR client JS is loaded via CDN (`cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js`), and `wwwroot/js/signalR.js` is loaded for authenticated users.
   - `wwwroot/js/signalR.js` lines 4–8:
     ```javascript
     if (typeof signalR !== "undefined") {
     const connection = new signalR.HubConnectionBuilder()
         .withUrl("/tripHub")
         .withAutomaticReconnect() 
         .build();
     ```
     `const connection` is enclosed within a block-scoped `if` statement, preventing external scripts from accessing the connection instance.
   - Searching for `leaflet` or `map` across `wwwroot` and `Views` yields zero mapping libraries.

4. **Trip & Route Modeling**:
   - `Models/Trip.cs`: Contains string fields `FromCity`, `ToCity`, `FromAddress`, `ToAddress`, `FromZipCode`, `ToZipCode`, and numeric `Distance`. No latitude/longitude properties exist on `Trip`.
   - `Models/Location.cs`: Contains `Latitude` and `Longitude` linked 1-to-1 with `Driver.CurrentLocation`, but is currently unused in active trips.
   - City names entered in the app and test suite include "Oslo", "Bergen", etc. (`ApplicationSmokeTests.cs` line 68).

5. **Existing Trip Views**:
   - Customer Trip Details (`Views/Customer/TripDetails.cshtml`, lines 37–40): Displays only text info; active trips state: `<p>No further actions are required at this time.</p>`.
   - Driver Current Trips (`Views/Driver/CurrentTrips.cshtml`, lines 28–51): Renders a table of assigned trips with "End Trip" buttons. No map or GPS controls exist.
   - Admin Trip Details (`Views/Admin/TripDetails.cshtml`, lines 71–114): Displays trip details and approval buttons. No map exists.

6. **Current Test Status**:
   - Executing `dotnet test` runs 21 tests in `tests/LogiTrack.Tests/LogiTrack.Tests.dll`, all passing in 2 seconds.
   - Existing SignalR smoke test (`ApplicationSmokeTests.cs` lines 180–185) verifies that anonymous negotiate requests to `/tripHub` are rejected with 401 or redirect.

---

## 2. Logic Chain

1. From Observation 1, `builder.Services.AddSignalR()` and `app.MapHub<TripHub>("/tripHub")` are already active, meaning SignalR transport infrastructure is operational.
2. From Observation 1 (`Permissions-Policy: geolocation=()`), browser hardware geolocation is disabled by HTTP header policy; thus, simulating GPS movement purely in software is the intended and required path.
3. From Observation 2, `TripHub` currently provides subscription via `JoinTrip` and `LeaveTrip`, but completely lacks a method to accept GPS coordinates from drivers. Therefore, `TripHub` must be extended with a `SendLocationUpdate(int tripId, double latitude, double longitude)` method.
4. From Observation 2 and the security requirement (R1/R2), accepting location updates requires strict authorization: the caller must be authenticated, must be the assigned driver for that trip (or Admin), the trip must be active (`TripStatus.Assigned` or `TripStatus.InProgress`), coordinates must be numerically valid, and customers must be rejected if attempting to emit coordinates. Broadcast must be directed strictly to `trip_{tripId}` to guarantee multi-tenant privacy.
5. From Observation 3, no map library exists in the codebase. Leaflet 1.9.4 paired with OpenStreetMap is the optimal choice because it requires no API keys, has zero usage fees, and is lightweight. Providing local copies in `wwwroot/lib/leaflet` ensures that tests and builds run reliably without network dependency.
6. From Observation 3, `wwwroot/js/signalR.js` does not expose `connection` globally because of `const` block-scoping. Exposing `window.signalRConnection = connection` or creating a dedicated `tripTracking.js` script solves inter-script communication cleanly.
7. From Observation 4, trips only store city names, not raw coordinates. Instead of requiring database schema migrations (which risk breaking existing tests or SQLite migrations), an `ICoordinateService` can resolve Norwegian city names (Oslo, Bergen, Trondheim, etc.) to exact coordinates, with a deterministic mathematical fallback within Norway for any arbitrary or test string. Start and destination coordinates can then be provided to the views via ViewModel properties (`TripDetailsViewModel`).
8. From Observation 5, active trip views for Customers (`Customer/TripDetails.cshtml`), Drivers (`Driver/CurrentTrips.cshtml`), and Admins (`Admin/TripDetails.cshtml`) should embed the map container and tracking script. The Driver view specifically requires GPS simulation controls ("Start Simulation", "Pause Simulation", "Reset Simulation") with a linear interpolation algorithm emitting coordinate updates every 1–2 seconds.

---

## 3. Caveats

- **Network Access for Map Tiles**: While OpenStreetMap tile rendering requires internet access in browser sessions, Leaflet map initialization itself does not fail if tile requests time out. For headless automated tests (`dotnet test`), tests should mock or directly invoke SignalR hub methods and verify DOM/model properties without requiring headless browser tile loading.
- **Database Schema Changes**: Modifying `Trip` to add database columns would require an EF Core migration. The recommended design avoids altering database columns on `Trip` by utilizing `ICoordinateService` and passing coordinates via ViewModels and `Location`, preserving full backward database compatibility.
- **Trip Status State Machine**: In the current codebase, `AssignTrip` sets status to `TripStatus.Assigned`, and `EndTrip` completes from `Assigned`. The simulation and authorization rules support both `TripStatus.Assigned` and `TripStatus.InProgress`.

---

## 4. Conclusion

The existing Logi-Track backend is well-positioned for R2:
1. Extend `TripHub` with `SendLocationUpdate(int tripId, double latitude, double longitude)` enforcing assigned driver / admin role authorization, active trip status, coordinate boundary validation, driver `Location` persistence, and isolated broadcasting to `Clients.Group($"trip_{tripId}")`.
2. Introduce `ICoordinateService` / `CityCoordinateService` to resolve city names into coordinates deterministically without database migrations.
3. Integrate Leaflet 1.9.4 and a dedicated `wwwroot/js/tripTracking.js` module.
4. Update `Views/Customer/TripDetails.cshtml`, `Views/Driver/CurrentTrips.cshtml` (with driver simulation controls), and `Views/Admin/TripDetails.cshtml` to render interactive maps for active trips.
5. Create comprehensive tests in `tests/LogiTrack.Tests/TripHubAuthorizationTests.cs` verifying IDOR resistance, customer update rejection, unassigned driver rejection, and isolated group delivery.

Detailed technical design and code blueprints are fully documented in `survey_tracking.md`.

---

## 5. Verification Method

To verify the findings and current codebase state:
1. **Build Verification**:
   ```powershell
   dotnet build
   ```
   *Expected Result*: Build succeeded with 0 warnings and 0 errors.

2. **Existing Test Suite**:
   ```powershell
   dotnet test
   ```
   *Expected Result*: 21 passed tests.

3. **Inspect Key Surveyed Files**:
   - `Program.cs` (lines 88, 133, 152)
   - `Hubs/TripHub.cs` (lines 1–46)
   - `Views/Shared/_Layout.cshtml` (lines 141–155)
   - `wwwroot/js/signalR.js` (lines 1–123)
   - `Models/Trip.cs` (lines 1–67)
   - `survey_tracking.md` in current directory for complete technical design.

4. **Invalidation Conditions**:
   - If `TripHub` already contained coordinate broadcasting methods (confirmed absent).
   - If `Models/Trip` already had lat/long coordinate properties (confirmed absent).
   - If Leaflet was already installed in `wwwroot/lib` (confirmed absent).
