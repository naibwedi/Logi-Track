/**
 * driverSimulation.js - Driver GPS Telemetry Simulator
 */
(function () {
    "use strict";

    function initSimulation() {
        const simContainer = document.getElementById("driver-simulation-panel");
        if (!simContainer) return;

        const btnStart = document.getElementById("btn-start-simulation");
        const btnPause = document.getElementById("btn-pause-simulation");
        const btnReset = document.getElementById("btn-reset-simulation");
        const progressBar = document.getElementById("sim-progress-bar");
        const progressText = document.getElementById("sim-progress-text");
        const latVal = document.getElementById("sim-lat-val");
        const lngVal = document.getElementById("sim-lng-val");
        const statusBadge = document.getElementById("sim-status-badge");

        const tripId = parseInt(simContainer.getAttribute("data-trip-id"), 10);
        const startLat = parseFloat(simContainer.getAttribute("data-start-lat")) || 59.9139;
        const startLng = parseFloat(simContainer.getAttribute("data-start-lng")) || 10.7522;
        const destLat = parseFloat(simContainer.getAttribute("data-dest-lat")) || 60.3913;
        const destLng = parseFloat(simContainer.getAttribute("data-dest-lng")) || 5.3221;

        // Generate 35 intermediate waypoints with gentle curve
        const numSteps = 35;
        const waypoints = [];
        for (let i = 0; i <= numSteps; i++) {
            const ratio = i / numSteps;
            const lat = startLat + (destLat - startLat) * ratio;
            const lng = startLng + (destLng - startLng) * ratio;
            const curve = Math.sin(ratio * Math.PI) * 0.015;
            waypoints.push({
                lat: parseFloat((lat + curve).toFixed(4)),
                lng: parseFloat((lng + curve).toFixed(4)),
                percent: Math.round(ratio * 100)
            });
        }

        let currentIndex = 0;
        let timer = null;

        function updateUI(point, index) {
            if (latVal) latVal.textContent = point.lat.toFixed(4);
            if (lngVal) lngVal.textContent = point.lng.toFixed(4);
            if (progressBar) {
                progressBar.style.width = point.percent + "%";
                progressBar.setAttribute("aria-valuenow", point.percent);
            }
            if (progressText) {
                progressText.textContent = point.percent + "% (" + (index + 1) + "/" + waypoints.length + " steps)";
            }
            if (window.tripMap && typeof window.tripMap.updatePosition === "function") {
                window.tripMap.updatePosition(point.lat, point.lng);
            }
        }

        async function sendUpdate(point) {
            const conn = window.signalRConnection;
            if (!conn || conn.state !== signalR.HubConnectionState.Connected) {
                console.warn("SignalR connection not ready to emit GPS update.");
                return;
            }

            try {
                await conn.invoke("SendLocationUpdate", tripId, point.lat, point.lng);
            } catch (err) {
                console.error("Error sending location update:", err);
            }
        }

        async function stepSimulation() {
            if (currentIndex >= waypoints.length) {
                pauseSimulation();
                if (statusBadge) {
                    statusBadge.textContent = "Trip Destination Reached";
                    statusBadge.className = "badge bg-success";
                }
                return;
            }

            const currentPoint = waypoints[currentIndex];
            updateUI(currentPoint, currentIndex);
            await sendUpdate(currentPoint);
            currentIndex++;

            if (currentIndex >= waypoints.length) {
                pauseSimulation();
                if (statusBadge) {
                    statusBadge.textContent = "Trip Destination Reached";
                    statusBadge.className = "badge bg-success";
                }
            }
        }

        function startSimulation() {
            if (timer) return;

            if (currentIndex >= waypoints.length) {
                currentIndex = 0;
            }

            if (btnStart) btnStart.disabled = true;
            if (btnPause) btnPause.disabled = false;
            if (statusBadge) {
                statusBadge.textContent = "Simulating GPS Updates...";
                statusBadge.className = "badge bg-primary";
            }

            // Immediately emit first step
            stepSimulation();

            timer = setInterval(stepSimulation, 1500);
        }

        function pauseSimulation() {
            if (timer) {
                clearInterval(timer);
                timer = null;
            }
            if (btnStart) btnStart.disabled = false;
            if (btnPause) btnPause.disabled = true;
            if (statusBadge && currentIndex < waypoints.length) {
                statusBadge.textContent = "Simulation Paused";
                statusBadge.className = "badge bg-warning text-dark";
            }
        }

        function resetSimulation() {
            pauseSimulation();
            currentIndex = 0;
            const startPt = waypoints[0];
            updateUI(startPt, 0);
            sendUpdate(startPt);
            if (statusBadge) {
                statusBadge.textContent = "Ready to simulate";
                statusBadge.className = "badge bg-secondary";
            }
        }

        if (btnStart) btnStart.addEventListener("click", startSimulation);
        if (btnPause) btnPause.addEventListener("click", pauseSimulation);
        if (btnReset) btnReset.addEventListener("click", resetSimulation);

        // Initial UI display
        updateUI(waypoints[0], 0);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initSimulation);
    } else {
        initSimulation();
    }
})();
