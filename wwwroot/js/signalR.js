"use strict";

//SignalR connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/tripHub") // URL to the SignalR Hub
    .withAutomaticReconnect() 
    .build();

//the connection
connection.start()
    .then(() => console.log("Connected to SignalR Hub"))
    .catch(err => console.error("Error connecting to SignalR Hub:", err));

//trip status updates
connection.on("TripStatusUpdated", (data) => {
    console.log("TripStatusUpdated received:", data);
    updateTripStatus(data);
});

//price updates
connection.on("PriceUpdated", (data) => {
    console.log("PriceUpdated received:", data);
    updateTripPrice(data);
});

//driver assignments
connection.on("DriverAssigned", (data) => {
    console.log("DriverAssigned received:", data);
    updateTripDriver(data);
});

//admin dashboard updates
connection.on("DashboardStatsUpdated", (stats) => {
    console.log("DashboardStatsUpdated received:", stats);
    updateDashboardStats(stats);
});

function updateTripStatus(data) {
    const statusElement = document.getElementById(`trip-status-${data.TripId}`);
    if (statusElement) {
        statusElement.textContent = data.Status;
        addHighlight(statusElement);
        showToast("Trip Status Updated", `Trip ID ${data.TripId} status changed to ${data.Status}.`, "success");
    }
}

function updateTripPrice(data) {
    const priceElement = document.getElementById(`trip-price-${data.TripId}`);
    if (priceElement) {
        priceElement.textContent = `$${data.Price.toFixed(2)}`;
        addHighlight(priceElement);
        showToast("Price Updated", `Trip ID ${data.TripId} price set to $${data.Price.toFixed(2)}.`, "info");
    }
}

function updateTripDriver(data) {
    const driverElement = document.getElementById(`trip-driver-${data.TripId}`);
    if (driverElement) {
        driverElement.textContent = data.DriverName;
        addHighlight(driverElement);
        showToast("Driver Assigned", `Trip ID ${data.TripId} assigned to ${data.DriverName}.`, "primary");
    }
}

function updateDashboardStats(stats) {
    const activeTrips = document.getElementById('active-trips');
    const availableDrivers = document.getElementById('available-drivers');
    const pendingApprovals = document.getElementById('pending-approvals');
    const totalRevenue = document.getElementById('total-revenue');

    if (activeTrips) activeTrips.textContent = stats.ActiveTrips;
    if (availableDrivers) availableDrivers.textContent = stats.AvailableDrivers;
    if (pendingApprovals) pendingApprovals.textContent = stats.PendingApprovals;
    if (totalRevenue) totalRevenue.textContent = `$${stats.TotalRevenue.toFixed(2)}`;

    if (activeTrips) addHighlight(activeTrips);
    if (availableDrivers) addHighlight(availableDrivers);
    if (pendingApprovals) addHighlight(pendingApprovals);
    if (totalRevenue) addHighlight(totalRevenue);

    showToast("Dashboard Updated", "Financial metrics have been updated.", "warning");
}

function addHighlight(element) {
    element.classList.add('highlight-update');
    setTimeout(() => {
        element.classList.remove('highlight-update');
    }, 2000); 
}

function showToast(title, message, type = 'primary') {
    const toastContainer = document.querySelector('.toast-container');
    if (!toastContainer) {
        console.error("Toast container not found. Ensure you have a div with class 'toast-container' in your layout.");
        return;
    }

    const toastEl = document.createElement('div');
    toastEl.className = `toast align-items-center text-bg-${type} border-0`;
    toastEl.role = 'alert';
    toastEl.ariaLive = 'assertive';
    toastEl.ariaAtomic = 'true';
    toastEl.innerHTML = `
        <div class="d-flex">
            <div class="toast-body">
                <strong>${title}:</strong> ${message}
            </div>
            <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
        </div>
    `;

    toastContainer.appendChild(toastEl);
    const bsToast = new bootstrap.Toast(toastEl, { delay: 5000 }); // Auto-hide after 5 seconds
    bsToast.show();
    toastEl.addEventListener('hidden.bs.toast', () => {
        toastEl.remove();
    });
}
