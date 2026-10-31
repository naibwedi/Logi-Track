# Survey & Technical Design: R2 Live Driver GPS & Map Tracking Simulation

**Date**: 2026-10-27  
**Explorer**: explorer_survey_2  
**Target Milestone**: R2 — Live Driver GPS & Map Tracking Simulation  
**Project Root**: `C:\Users\naib\Documents\projects\Logi-Track`  

---

## 1. Executive Summary & Acceptance Criteria Alignment

The objective of Requirement R2 is to provide real-time location visualization and updates during active trips:
1. **Interactive Map**: Render an interactive map for active trips displaying route points (pickup location, destination, and current vehicle position) without JavaScript console errors.
2. **SignalR Broadcast**: Allow drivers on active trips to emit simulated GPS coordinate updates via SignalR, broadcasting updates strictly to authorized viewing customers and admins in real time.
3. **Strict Authorization**: Ensure SignalR events are isolated so only participants (assigned driver, customer, and admins) can subscribe to or publish updates for a given trip.

### Current State Summary
- **SignalR Backend**: Already registered in `Program.cs` (`builder.Services.AddSignalR()`, `app.MapHub<TripHub>("/tripHub")`), and a base hub `Hubs/TripHub.cs` exists with `[Authorize]`, `JoinTrip`, and `LeaveTrip`. However, `TripHub` currently **lacks any location broadcasting method** (e.g., `SendLocationUpdate`).
- **Static Assets**: `_Layout.cshtml` includes the SignalR JavaScript client library via CDN (`microsoft-signalr/8.0.7`), and `wwwroot/js/signalR.js` handles status and price notifications. However, `signalR.js` wraps the connection in a block-scoped `const` inside an `if` block, making it inaccessible to other scripts.
- **Mapping Libraries**: No map library (e.g., Leaflet or OpenStreetMap) is currently present in `wwwroot/lib` or views.
- **Data Modeling**: `Models/Trip.cs` stores city names (`FromCity`, `ToCity`), addresses, and distance as free-text strings/numbers, but contains no latitude/longitude coordinates. `Models/Location.cs` exists for storing a driver's coordinates, but is not integrated into active trip tracking.
- **Active Trip Views**: `Views/Customer/TripDetails.cshtml`, `Views/Driver/CurrentTrips.cshtml`, and `Views/Admin/TripDetails.cshtml` currently lack map containers, coordinate data bindings, and simulation controls.

---

## 2. Frontend Architecture & Static Assets Survey

### 2.1 Existing Layout & Script Loading
**File**: `Views/Shared/_Layout.cshtml`
- Lines 141–142 load SignalR client:
  ```html
  <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js"
          crossorigin="anonymous" referrerpolicy="no-referrer"></script>
  ```
- Lines 148–151 load `signalR.js`:
  ```html
  @if (User.Identity?.IsAuthenticated == true)
  {
      <script src="~/js/signalR.js" asp-append-version="true"></script>
  }
  ```
- Lines 15 and 155 render optional section hooks:
  ```html
  @await RenderSectionAsync("Styles", required: false)
  ...
  @await RenderSectionAsync("Scripts", required: false)
  ```

### 2.2 Existing `signalR.js` Analysis
**File**: `wwwroot/js/signalR.js`
- Lines 4–8:
  ```javascript
  if (typeof signalR !== "undefined") {
  const connection = new signalR.HubConnectionBuilder()
      .withUrl("/tripHub") // URL to the SignalR Hub
      .withAutomaticReconnect() 
      .build();
  ```
- **Key Observation**: `const connection` is declared inside block scope `{ ... }`. Therefore, `window.connection` is `undefined`. Any separate tracking script on a specific page cannot access `connection` unless `connection` is attached to `window.signalRConnection` or exported.
- **Recommendation**: In `signalR.js`, expose `window.signalRConnection = connection;` or have the dedicated `tripTracking.js` script use `window.signalRConnection` if available, or establish its own connection if not present.

### 2.3 Map Library Evaluation & Selection
- **Selected Library**: **Leaflet (v1.9.4)** paired with OpenStreetMap tile layer (`https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png`).
- **Rationale**:
  - Open source, lightweight (~40KB gzip), zero API key requirements.
  - No billing or quota limits compared to Google Maps / Mapbox.
  - Native support for custom markers, popups, polylines, and smooth marker coordinate transitions.
- **Asset Placement**:
  - Recommend placing local copies in `wwwroot/lib/leaflet/leaflet.js` and `wwwroot/lib/leaflet/leaflet.css` (with images in `wwwroot/lib/leaflet/images/`), while also supporting CDN fallback:
    - CSS: `https://unpkg.com/leaflet@1.9.4/dist/leaflet.css`
    - JS: `https://unpkg.com/leaflet@1.9.4/dist/leaflet.js`
  - This ensures that builds and tests function identically in offline and online environments.

### 2.4 Browser Permissions-Policy Note
**File**: `Program.cs` (line 133):
```csharp
context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
```
- `geolocation=()` blocks the browser's hardware `navigator.geolocation` API.
- This confirms that location updates **must be simulated** rather than relying on browser geolocation.

---

## 3. Existing Views & Active Trip UI Design

### 3.1 Customer Tracking View
**File**: `Views/Customer/TripDetails.cshtml`
- **Current Code** (lines 37–40):
  ```html
  else if (Model.Status == TripStatus.ApprovedByCustomer || Model.Status == TripStatus.Assigned || Model.Status == TripStatus.InProgress || Model.Status == TripStatus.Completed)
  {
      <p>No further actions are required at this time.</p>
  }
  ```
- **Proposed Enhancement**:
  When `Model.Status == TripStatus.Assigned || Model.Status == TripStatus.InProgress`, render:
  1. A live delivery status card with real-time connection status indicator.
  2. An interactive map container:
     ```html
     <div id="trip-map" style="height: 420px; width: 100%; border-radius: 8px;"
          data-trip-id="@Model.Id"
          data-from-city="@Model.FromCity"
          data-to-city="@Model.ToCity"
          data-start-lat="@Model.StartLatitude"
          data-start-lng="@Model.StartLongitude"
          data-dest-lat="@Model.DestinationLatitude"
          data-dest-lng="@Model.DestinationLongitude"
          data-current-lat="@Model.CurrentLatitude"
          data-current-lng="@Model.CurrentLongitude"
          data-is-driver="false">
     </div>
     ```
  3. Load `tripTracking.js` via `@section Scripts`.
  4. Real-time coordinate and status indicator: "Driver is on the way (Lat: ..., Lng: ...)".

### 3.2 Driver Active Trip View & GPS Simulator
**File**: `Views/Driver/CurrentTrips.cshtml`
- **Current Code** (lines 28–50):
  Renders an HTML table listing assigned trips with an "End Trip" button for each.
- **Proposed Enhancement**:
  For active/assigned trips, provide an expandable card or dedicated tracking pane:
  1. Map container displaying the route from pickup to destination.
  2. Driver Simulation Controls:
     - **Start Simulation** button: starts a timer emitting simulated coordinates along the route every 1–2 seconds.
     - **Pause Simulation** button: pauses the simulation timer.
     - **Reset Simulation** button: resets the vehicle back to pickup.
     - **Progress Bar & Coordinate Readout**: Displays % completed and current GPS coordinates.
  3. `data-is-driver="true"` attribute enabling the simulation engine.

### 3.3 Admin Trip View
**File**: `Views/Admin/TripDetails.cshtml`
- **Current Code** (lines 71–114):
  Displays trip information and approval/pricing/assignment action buttons.
- **Proposed Enhancement**:
  When `Model.Status == TripStatus.Assigned || Model.Status == TripStatus.InProgress`, render the interactive map showing the active route and live driver marker, subscribed to the trip group via SignalR.

---

## 4. Backend Route & Coordinate Modeling Survey

### 4.1 Existing Models Analysis
- `Models/Trip.cs`:
  - Contains `FromCity`, `ToCity`, `FromAddress`, `ToAddress`, `FromZipCode`, `ToZipCode`, `Distance`.
  - Does **not** contain coordinates.
- `Models/Location.cs`:
  - Contains `ID`, `DriverID`, `Driver`, `City`, `Street`, `Region`, `Longitude`, `Latitude`, `UpdatedAt`.
  - Mapped 1-to-1 to `Driver.CurrentLocation` in `ApplicationDbContext.cs`.
- `Models/ViewModel/TripDetailsViewModel.cs`:
  - Contains trip summary fields used across Admin and Driver views.

### 4.2 City Coordinate Resolution Strategy
Because customers enter city names (e.g., "Oslo", "Bergen", "Trondheim"), the backend should provide a robust coordinate resolver service:
`ICoordinateService` / `CityCoordinateService`:
1. **Predefined Dictionary of Major Norwegian Cities**:
   - Oslo: `(59.9139, 10.7522)`
   - Bergen: `(60.3913, 5.3221)`
   - Trondheim: `(63.4305, 10.3951)`
   - Stavanger: `(58.9700, 5.7331)`
   - Kristiansand: `(58.1599, 8.0182)`
   - Drammen: `(59.7441, 10.2045)`
   - Fredrikstad: `(59.2205, 10.9347)`
   - Tromsø: `(69.6492, 18.9553)`
   - Sandnes: `(58.8524, 5.7352)`
   - Ålesund: `(62.4722, 6.1495)`
   - Haugesund: `(59.4138, 5.2680)`
   - Bodø: `(67.2804, 14.4049)`
   - Arendal: `(58.4615, 8.7725)`
   - Larvik: `(59.0533, 10.0274)`
   - Lillehammer: `(61.1153, 10.4662)`
   - Hamar: `(60.7945, 11.0680)`
2. **Deterministic Fallback Algorithm**:
   For any unrecognized city name or test input, compute a deterministic hash offset constrained strictly within Norwegian boundaries:
   ```csharp
   public (double Latitude, double Longitude) GetCoordinates(string? cityName)
   {
       if (string.IsNullOrWhiteSpace(cityName)) return (59.9139, 10.7522); // Default Oslo
       var normalized = cityName.Trim().ToLowerInvariant();
       if (_knownCities.TryGetValue(normalized, out var coords)) return coords;

       // Deterministic fallback inside southern/central Norway
       uint hash = (uint)normalized.GetHashCode();
       double lat = 59.0 + (hash % 300) / 100.0;     // 59.00 to 62.00 N
       double lng = 6.0 + ((hash >> 8) % 500) / 100.0; // 6.00 to 11.00 E
       return (lat, lng);
   }
   ```
   **Benefit**: Guarantees that any trip ever created will always have valid, non-null, distinct start and end coordinates without making external geocoding HTTP requests that could fail or timeout during tests.

3. **ViewModel Integration**:
   Add coordinate properties to `TripDetailsViewModel`:
   ```csharp
   public double StartLatitude { get; set; }
   public double StartLongitude { get; set; }
   public double DestinationLatitude { get; set; }
   public double DestinationLongitude { get; set; }
   public double CurrentLatitude { get; set; }
   public double CurrentLongitude { get; set; }
   ```

---

## 5. SignalR Backend Survey & Hub Technical Design

### 5.1 Existing `TripHub` Analysis
**File**: `Hubs/TripHub.cs`
```csharp
[Authorize]
public sealed class TripHub(ApplicationDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("SuperAdmin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinTrip(int tripId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("Authentication is required.");
        }

        var isAdmin = Context.User!.IsInRole("Admin") || Context.User.IsInRole("SuperAdmin");
        var isParticipant = isAdmin || await db.Trips.AnyAsync(trip =>
            trip.Id == tripId &&
            (trip.CustomerId == userId || (trip.DriverTrip != null && trip.DriverTrip.DriverId == userId)));

        if (!isParticipant)
        {
            throw new HubException("You are not allowed to subscribe to this trip.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"trip_{tripId}");
    }

    public Task LeaveTrip(int tripId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trip_{tripId}");
}
```

### 5.2 Proposed Hub Method: `SendLocationUpdate`
Add a dedicated method to `TripHub` for publishing GPS updates:
```csharp
public async Task SendLocationUpdate(int tripId, double latitude, double longitude)
{
    var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(userId))
    {
        throw new HubException("Authentication is required.");
    }

    // Validate coordinate range
    if (double.IsNaN(latitude) || double.IsNaN(longitude) ||
        latitude < -90.0 || latitude > 90.0 ||
        longitude < -180.0 || longitude > 180.0)
    {
        throw new HubException("Invalid GPS coordinates.");
    }

    // Strict Authorization: Only assigned driver or admin can update location
    var isAdmin = Context.User!.IsInRole("Admin") || Context.User.IsInRole("SuperAdmin");
    
    var trip = await db.Trips
        .Include(t => t.DriverTrip)
        .FirstOrDefaultAsync(t => t.Id == tripId);

    if (trip == null)
    {
        throw new HubException("Trip not found.");
    }

    // Must be in active state (Assigned or InProgress)
    if (trip.Status != TripStatus.Assigned && trip.Status != TripStatus.InProgress)
    {
        throw new HubException("Location updates are only permitted for active trips.");
    }

    var isAssignedDriver = trip.DriverTrip != null && trip.DriverTrip.DriverId == userId;

    if (!isAssignedDriver && !isAdmin)
    {
        throw new HubException("You are not authorized to publish location updates for this trip.");
    }

    // Update driver's CurrentLocation record if driver
    if (isAssignedDriver)
    {
        var driverLocation = await db.Locations.FirstOrDefaultAsync(l => l.DriverID == userId);
        if (driverLocation == null)
        {
            driverLocation = new Location
            {
                DriverID = userId,
                Latitude = latitude,
                Longitude = longitude,
                City = trip.ToCity,
                UpdatedAt = DateTime.UtcNow
            };
            db.Locations.Add(driverLocation);
        }
        else
        {
            driverLocation.Latitude = latitude;
            driverLocation.Longitude = longitude;
            driverLocation.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    var updatePayload = new
    {
        tripId = tripId,
        latitude = latitude,
        longitude = longitude,
        timestamp = DateTime.UtcNow
    };

    // Broadcast strictly to authorized trip subscribers
    await Clients.Group($"trip_{tripId}").SendAsync("LocationUpdated", updatePayload);

    // Also broadcast to Admins group for admin dashboard overview
    await Clients.Group("Admins").SendAsync("LocationUpdated", updatePayload);
}
```

---

## 6. Authorization & Security Architecture

### 6.1 Threat Model & Security Controls

| Threat / Vulnerability | Vector | Prevention Mechanism |
|---|---|---|
| **Anonymous Snooping** | Unauthenticated user connects to `/tripHub` | `[Authorize]` attribute on `TripHub` class triggers 401 challenge on negotiate. |
| **Trip Snooping (IDOR)** | Customer A calls `JoinTrip(tripB)` | Hub verifies `trip.CustomerId == userId`. Non-participants get `HubException("You are not allowed to subscribe to this trip.")`. |
| **Driver Snooping (IDOR)** | Driver X calls `JoinTrip(tripY)` | Hub verifies `trip.DriverTrip.DriverId == userId`. Non-assigned drivers are rejected. |
| **Spoofed GPS Updates** | Customer or random user calls `SendLocationUpdate` | Method validates caller is the assigned driver on that specific active trip (or Admin). Customers are strictly forbidden. |
| **Stale State GPS Emission** | Driver continues emitting GPS after trip completion | Method verifies `trip.Status == TripStatus.Assigned || trip.Status == TripStatus.InProgress`. Rejects completed/cancelled trips. |
| **Malformed Coordinate Injection** | Emitting `NaN`, `Infinity`, or out-of-range floats | Numerical range checks `[-90, 90]` and `[-180, 180]` enforced before broadcasting. |

---

## 7. Driver GPS Simulation Implementation Design

### 7.1 Waypoint Generation Algorithm
In client script `wwwroot/js/tripTracking.js`:
```javascript
function generateWaypoints(startLat, startLng, destLat, destLng, numSteps = 30) {
    const waypoints = [];
    for (let i = 0; i <= numSteps; i++) {
        const ratio = i / numSteps;
        // Linear interpolation with a gentle realistic curve offset
        const lat = startLat + (destLat - startLat) * ratio;
        const lng = startLng + (destLng - startLng) * ratio;
        // Add subtle arc offset to look like a realistic road curve
        const curveOffset = Math.sin(ratio * Math.PI) * 0.02;
        waypoints.push({
            lat: Number((lat + curveOffset).toFixed(6)),
            lng: Number((lng + curveOffset).toFixed(6))
        });
    }
    return waypoints;
}
```

### 7.2 Simulation Controller Lifecycle
```javascript
class DriverGpsSimulator {
    constructor(tripId, waypoints, hubConnection, onTick) {
        this.tripId = tripId;
        this.waypoints = waypoints;
        this.connection = hubConnection;
        this.onTick = onTick;
        this.currentIndex = 0;
        this.intervalId = null;
    }

    start(intervalMs = 1500) {
        if (this.intervalId) return;
        this.intervalId = setInterval(async () => {
            if (this.currentIndex >= this.waypoints.length) {
                this.pause();
                return;
            }
            const pt = this.waypoints[this.currentIndex];
            try {
                await this.connection.invoke("SendLocationUpdate", this.tripId, pt.lat, pt.lng);
                if (this.onTick) this.onTick(pt, this.currentIndex, this.waypoints.length);
                this.currentIndex++;
            } catch (err) {
                console.error("Failed to emit simulated GPS update:", err);
            }
        }, intervalMs);
    }

    pause() {
        if (this.intervalId) {
            clearInterval(this.intervalId);
            this.intervalId = null;
        }
    }

    reset() {
        this.pause();
        this.currentIndex = 0;
        const pt = this.waypoints[0];
        this.connection.invoke("SendLocationUpdate", this.tripId, pt.lat, pt.lng).catch(console.error);
        if (this.onTick) this.onTick(pt, 0, this.waypoints.length);
    }
}
```

### 7.3 Interactive Map UI Rendering
1. **Container**:
   ```html
   <div id="trip-map" style="height: 420px; width: 100%; border-radius: 8px; border: 1px solid #dee2e6;"></div>
   ```
2. **Leaflet Initialization**:
   - Guard against missing Leaflet: `if (typeof L === 'undefined') return;`
   - Guard against re-initialization: If `map` already exists on container, call `map.remove()`.
   - Add OSM tile layer:
     ```javascript
     L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
         maxZoom: 19,
         attribution: '&copy; OpenStreetMap contributors'
     }).addTo(map);
     ```
   - Place Pickup Marker (`startMarker`) with green badge icon or popup.
   - Place Destination Marker (`destMarker`) with red badge icon or popup.
   - Place Live Vehicle Marker (`vehicleMarker`) with truck icon.
   - Draw dashed route line: `L.polyline([[startLat, startLng], [destLat, destLng]], { color: '#0d6efd', weight: 4, dashArray: '6, 8' }).addTo(map)`.
   - Call `map.fitBounds([ [startLat, startLng], [destLat, destLng] ], { padding: [40, 40] })`.

3. **Real-time Map Updates**:
   When `connection.on("LocationUpdated", data)` fires:
   - Check `data.tripId === currentTripId`.
   - Call `vehicleMarker.setLatLng([data.latitude, data.longitude])`.
   - Update UI readout: "Current location: Lat X, Lng Y (Updated just now)".

---

## 8. Testing & Verification Strategy

### 8.1 Automated Test Suite Expansion (`tests/LogiTrack.Tests`)
To satisfy Acceptance Criterion:
*"SignalR connection tests confirm location broadcast events are delivered only to users authorized for that specific trip."*

Create `tests/LogiTrack.Tests/TripHubAuthorizationTests.cs` covering:

1. **Anonymous Rejection**:
   - Anonymous negotiate request to `/tripHub` returns 401 Unauthorized or redirect to login.
2. **`JoinTrip` Access Control**:
   - Customer A can join group for Customer A's trip.
   - Customer A joining Customer B's trip throws `HubException` ("You are not allowed to subscribe to this trip.").
   - Assigned driver can join group for their assigned trip.
   - Unassigned driver joining another driver's trip throws `HubException`.
   - Admin can join any trip group.
3. **`SendLocationUpdate` Access Control**:
   - Assigned driver calling `SendLocationUpdate` on their active trip succeeds.
   - Customer calling `SendLocationUpdate` throws `HubException` ("You are not authorized to publish location updates for this trip.").
   - Unassigned driver calling `SendLocationUpdate` throws `HubException`.
   - Driver calling `SendLocationUpdate` on a completed trip throws `HubException` ("Location updates are only permitted for active trips.").
   - Driver emitting coordinates with latitude > 90 throws `HubException` ("Invalid GPS coordinates.").
4. **Isolated Event Delivery Verification**:
   - Connect two test clients: Client 1 (Customer A on Trip 1) and Client 2 (Customer B on Trip 2).
   - Driver on Trip 1 emits GPS update.
   - Verify Client 1 receives the `LocationUpdated` event with Trip 1 ID.
   - Verify Client 2 does **not** receive the event.

---

## 9. File Modification Inventory for Implementation

| File Path | Action | Description |
|---|---|---|
| `Hubs/TripHub.cs` | Modify | Add `SendLocationUpdate` method with driver/admin auth checks, active status validation, coordinate bounds check, DB location update, and isolated group broadcast. |
| `Services/CoordinateService.cs` | Add | Add `ICoordinateService` and implementation mapping cities to coordinates with deterministic fallback. |
| `Program.cs` | Modify | Register `ICoordinateService` in DI (`builder.Services.AddSingleton<ICoordinateService, CoordinateService>();`). |
| `Models/ViewModel/TripDetailsViewModel.cs` | Modify | Add coordinate properties (`StartLatitude`, `StartLongitude`, `DestinationLatitude`, `DestinationLongitude`, `CurrentLatitude`, `CurrentLongitude`). |
| `Controllers/CustomerController.cs` | Modify | Populate coordinate properties in `TripDetails` GET action using `ICoordinateService`. |
| `Controllers/DriverController.cs` | Modify | Populate coordinate properties in `CurrentTrips` GET action using `ICoordinateService`. |
| `Controllers/AdminController.cs` | Modify | Populate coordinate properties in `TripDetails` GET action using `ICoordinateService`. |
| `Views/Shared/_Layout.cshtml` | Modify | Add Leaflet CSS/JS or ensure sections load them cleanly without console errors. |
| `wwwroot/js/signalR.js` | Modify | Expose `window.signalRConnection = connection;` so tracking scripts can access the active hub connection. |
| `wwwroot/js/tripTracking.js` | Add | Client script handling Leaflet map initialization, SignalR subscription, real-time marker updates, and driver GPS simulation controls. |
| `Views/Customer/TripDetails.cshtml` | Modify | Add map container, script inclusion, and live status UI when trip is assigned/in-progress. |
| `Views/Driver/CurrentTrips.cshtml` | Modify | Add tracking map and GPS simulation controls (Start, Pause, Reset, progress bar) for active trips. |
| `Views/Admin/TripDetails.cshtml` | Modify | Add map container for assigned/in-progress trips. |
| `tests/LogiTrack.Tests/LogiTrack.Tests.csproj` | Modify | Add `Microsoft.AspNetCore.SignalR.Client` package reference. |
| `tests/LogiTrack.Tests/TripHubAuthorizationTests.cs` | Add | Automated integration/unit tests for SignalR hub authorization, IDOR protection, coordinate validation, and isolated event delivery. |
