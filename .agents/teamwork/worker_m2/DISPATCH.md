## 2026-10-27T19:55:20Z
You are worker_m2.
Your working directory: C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\worker_m2
Project root: C:\Users\naib\Documents\projects\Logi-Track
Authoritative request file: C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\ORIGINAL_REQUEST.md
Survey & Technical Design report: C:\Users\naib\Documents\projects\Logi-Track\.agents\teamwork\explorer_survey_2\survey_tracking.md
Project specification: C:\Users\naib\Documents\projects\Logi-Track\PROJECT.md

Objective:
Implement Milestone 2: R2 Live Driver GPS & Map Tracking Simulation.
Specific tasks:
1. Coordinate Service:
   - Create `Services/ICoordinateService.cs` and `Services/CityCoordinateService.cs`:
     - Map Norwegian cities (Oslo: 59.9139, 10.7522; Bergen: 60.3913, 5.3221; Trondheim: 63.4305, 10.3951; Stavanger: 58.9700, 5.7331; Kristiansand: 58.1599, 8.0182; Drammen: 59.7441, 10.2045; Fredrikstad: 59.2181, 10.9298; Tromsø: 69.6492, 18.9553; Sandnes: 58.8524, 5.7352; Ålesund: 62.4722, 6.1495, etc.).
     - Implement deterministic fallback for any arbitrary city name (hashing string into Norway bounds: lat [58.0, 69.0], lng [5.0, 25.0]).
     - Register `ICoordinateService` as Singleton in `Program.cs`.
2. SignalR Hub Location Broadcasting:
   - In `Hubs/TripHub.cs`:
     - Implement `public async Task SendLocationUpdate(int tripId, double latitude, double longitude)`:
       - Validate caller authentication and retrieve `userId`.
       - Query trip including `DriverTrip`:
         - Validate caller authorization: Must be Admin/SuperAdmin OR assigned Driver (`trip.DriverTrip != null && trip.DriverTrip.DriverId == userId`). If customer or unassigned driver, throw `new HubException("You are not authorized to update location for this trip.")`.
         - Validate trip status: Must be `TripStatus.Assigned` or `TripStatus.InProgress`. If completed or cancelled, throw `new HubException("Location updates are only allowed for active trips.")`.
         - Validate coordinate bounds: `latitude >= -90 && latitude <= 90` and `longitude >= -180 && longitude <= 180`. Throw `HubException` if invalid.
         - If `trip.Status == TripStatus.Assigned`, update `trip.Status = TripStatus.InProgress` and `trip.DriverTrip.Status = TripStatus.InProgress`.
         - Update driver's `CurrentLocation` in the database if available.
         - Save changes to database.
         - Broadcast: `await Clients.Group($"trip_{tripId}").SendAsync("ReceiveLocationUpdate", tripId, latitude, longitude);`.
3. Client-side Leaflet Assets & Scripts:
   - Add Leaflet 1.9.4 CSS and JS into `wwwroot/lib/leaflet/leaflet.css` and `wwwroot/lib/leaflet/leaflet.js` (fully functional, standalone).
   - Create `wwwroot/js/tripTracking.js`:
     - Initialize Leaflet map targeting container `trip-map`.
     - Render OpenStreetMap tiles (with graceful offline fallback).
     - Place Pickup marker (with city/address popup) and Destination marker (with city/address popup).
     - Place Vehicle marker at initial coordinates.
     - Connect to `/tripHub`, join group `trip_{tripId}`, listen for `ReceiveLocationUpdate`, update vehicle marker coordinates smoothly.
     - Ensure zero JavaScript console errors on load or updates.
   - Create `wwwroot/js/driverSimulation.js`:
     - For driver active trips: compute linear waypoints between start and destination coordinates.
     - Provide interactive UI controls: "Start GPS Simulation", "Pause Simulation", "Reset", progress bar, current lat/lng display.
     - Timer emits `connection.invoke("SendLocationUpdate", tripId, lat, lng)` every 1-2 seconds.
4. Update Views:
   - `Views/Customer/TripDetails.cshtml`: for active/completed trips, inject coordinates from `ICoordinateService` and render map container + `tripTracking.js`.
   - `Views/Driver/CurrentTrips.cshtml`: render interactive map and simulation controls for assigned/in-progress trips.
   - `Views/Admin/TripDetails.cshtml`: render interactive map for active trips.
   - Ensure `_Layout.cshtml` or specific views load Leaflet CSS/JS properly without breaking pages or throwing JS console errors.
