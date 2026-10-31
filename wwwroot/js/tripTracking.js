/**
 * tripTracking.js - Interactive Leaflet Map & SignalR Real-Time Tracking
 */
(function () {
    "use strict";

    function initMap() {
        const mapEl = document.getElementById("trip-map");
        if (!mapEl) return;

        if (typeof L === "undefined") {
            console.warn("Leaflet library not loaded. Map tracking is unavailable.");
            return;
        }

        // Configure default Leaflet asset path
        L.Icon.Default.imagePath = "/lib/leaflet/images/";

        const tripId = parseInt(mapEl.getAttribute("data-trip-id"), 10);
        const fromCity = mapEl.getAttribute("data-from-city") || "Pickup";
        const toCity = mapEl.getAttribute("data-to-city") || "Destination";
        const fromAddress = mapEl.getAttribute("data-from-address") || "";
        const toAddress = mapEl.getAttribute("data-to-address") || "";

        const startLat = parseFloat(mapEl.getAttribute("data-start-lat")) || 59.9139;
        const startLng = parseFloat(mapEl.getAttribute("data-start-lng")) || 10.7522;
        const destLat = parseFloat(mapEl.getAttribute("data-dest-lat")) || 60.3913;
        const destLng = parseFloat(mapEl.getAttribute("data-dest-lng")) || 5.3221;
        const currentLat = parseFloat(mapEl.getAttribute("data-current-lat")) || startLat;
        const currentLng = parseFloat(mapEl.getAttribute("data-current-lng")) || startLng;

        // Custom styled icons
        const pickupIcon = L.divIcon({
            className: "custom-map-icon",
            html: '<div style="background-color:#198754; color:white; border-radius:50%; width:32px; height:32px; display:flex; align-items:center; justify-content:center; box-shadow:0 2px 6px rgba(0,0,0,0.3); font-weight:bold; font-size:14px; border:2px solid white;" title="Pickup">A</div>',
            iconSize: [32, 32],
            iconAnchor: [16, 16],
            popupAnchor: [0, -18]
        });

        const destIcon = L.divIcon({
            className: "custom-map-icon",
            html: '<div style="background-color:#dc3545; color:white; border-radius:50%; width:32px; height:32px; display:flex; align-items:center; justify-content:center; box-shadow:0 2px 6px rgba(0,0,0,0.3); font-weight:bold; font-size:14px; border:2px solid white;" title="Destination">B</div>',
            iconSize: [32, 32],
            iconAnchor: [16, 16],
            popupAnchor: [0, -18]
        });

        const vehicleIcon = L.divIcon({
            className: "custom-map-icon",
            html: '<div style="background-color:#0d6efd; color:white; border-radius:50%; width:36px; height:36px; display:flex; align-items:center; justify-content:center; box-shadow:0 2px 8px rgba(13,110,253,0.5); font-size:18px; border:2px solid white;" title="Driver">🚚</div>',
            iconSize: [36, 36],
            iconAnchor: [18, 18],
            popupAnchor: [0, -20]
        });

        const map = L.map(mapEl).setView([currentLat, currentLng], 7);

        // OpenStreetMap tile layer with error suppression for offline environments
        const tileLayer = L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
            maxZoom: 19,
            attribution: "&copy; OpenStreetMap contributors"
        });
        tileLayer.on("tileerror", function () {
            // Graceful offline fallback
        });
        tileLayer.addTo(map);

        // Place markers
        const pickupMarker = L.marker([startLat, startLng], { icon: pickupIcon }).addTo(map);
        pickupMarker.bindPopup("<strong>Pickup: " + fromCity + "</strong>" + (fromAddress ? "<br>" + fromAddress : ""));

        const destMarker = L.marker([destLat, destLng], { icon: destIcon }).addTo(map);
        destMarker.bindPopup("<strong>Destination: " + toCity + "</strong>" + (toAddress ? "<br>" + toAddress : ""));

        const vehicleMarker = L.marker([currentLat, currentLng], { icon: vehicleIcon }).addTo(map);
        vehicleMarker.bindPopup("<strong>Live Vehicle Location</strong><br><span class='vehicle-coords-popup'>" + currentLat.toFixed(4) + ", " + currentLng.toFixed(4) + "</span>");

        // Route polyline connecting start and destination
        const routeLine = L.polyline([[startLat, startLng], [destLat, destLng]], {
            color: "#0d6efd",
            weight: 4,
            opacity: 0.7,
            dashArray: "8, 8"
        }).addTo(map);

        // Fit map bounds to encompass the full trip
        map.fitBounds([[startLat, startLng], [destLat, destLng]], { padding: [50, 50] });

        // Store globally for driver simulation script to access
        window.tripMap = {
            map: map,
            vehicleMarker: vehicleMarker,
            routeLine: routeLine,
            tripId: tripId,
            startLat: startLat,
            startLng: startLng,
            destLat: destLat,
            destLng: destLng,
            updatePosition: updatePosition
        };

        function updatePosition(lat, lng) {
            if (typeof lat !== "number" || typeof lng !== "number") return;
            vehicleMarker.setLatLng([lat, lng]);

            const popupEl = document.querySelector(".vehicle-coords-popup");
            if (popupEl) {
                popupEl.textContent = lat.toFixed(4) + ", " + lng.toFixed(4);
            }

            const latDisplay = document.getElementById("current-lat-val");
            const lngDisplay = document.getElementById("current-lng-val");
            if (latDisplay) latDisplay.textContent = lat.toFixed(4);
            if (lngDisplay) lngDisplay.textContent = lng.toFixed(4);

            const statusBadge = document.getElementById("tracking-status-badge");
            if (statusBadge) {
                statusBadge.textContent = "Live Tracking Active";
                statusBadge.className = "badge bg-success";
            }
        }

        // Connect to SignalR TripHub
        setupSignalR(tripId, updatePosition);
    }

    async function setupSignalR(tripId, updateCallback) {
        if (typeof signalR === "undefined") return;

        let conn = window.signalRConnection;
        if (!conn) {
            conn = new signalR.HubConnectionBuilder()
                .withUrl("/tripHub")
                .withAutomaticReconnect()
                .build();
            window.signalRConnection = conn;
        }

        // Register location receiver
        conn.off("ReceiveLocationUpdate");
        conn.on("ReceiveLocationUpdate", function (tId, lat, lng) {
            if (parseInt(tId, 10) === tripId) {
                updateCallback(lat, lng);
            }
        });

        conn.off("LocationUpdated");
        conn.on("LocationUpdated", function (data) {
            if (data && parseInt(data.tripId, 10) === tripId) {
                updateCallback(data.latitude, data.longitude);
            }
        });

        // Ensure connection is started
        if (conn.state === signalR.HubConnectionState.Disconnected) {
            try {
                await conn.start();
            } catch (err) {
                console.warn("SignalR connection start error:", err);
            }
        }

        // Join trip group
        try {
            if (conn.state === signalR.HubConnectionState.Connected) {
                await conn.invoke("JoinTrip", tripId);
            }
        } catch (err) {
            console.warn("Could not join trip group:", err);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initMap);
    } else {
        initMap();
    }
})();
