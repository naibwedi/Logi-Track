/**
 * Leaflet & Driver Simulation Math Empirical Stress Test Harness
 */
const fs = require('fs');
const path = require('path');
const assert = require('assert');

console.log("=== EMPIRICAL STRESS TEST: SIMULATION MATH & LEAFLET SCRIPTS ===");

// -------------------------------------------------------------
// Part 1: Simulation Math Stress Testing
// -------------------------------------------------------------
console.log("\n[1/3] Testing Simulation Math & Waypoint Generation...");

function calculateWaypoints(startLat, startLng, destLat, destLng, numSteps = 35) {
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
    return waypoints;
}

const testCoordinatePairs = [
    { name: "Oslo to Bergen", start: [59.9139, 10.7522], dest: [60.3913, 5.3221] },
    { name: "Same City (Oslo to Oslo)", start: [59.9139, 10.7522], dest: [59.9139, 10.7522] },
    { name: "Kristiansand to Alta (South to North Extreme)", start: [58.1599, 8.0182], dest: [69.9689, 23.2717] },
    { name: "Alta to Kristiansand (Reverse Extreme)", start: [69.9689, 23.2717], dest: [58.1599, 8.0182] },
    { name: "Norway Bounding Box Min to Max", start: [58.0, 5.0], dest: [69.0, 25.0] },
    { name: "Near Zero Distance (0.0001 deg)", start: [59.9139, 10.7522], dest: [59.9140, 10.7523] }
];

for (const pair of testCoordinatePairs) {
    const pts = calculateWaypoints(pair.start[0], pair.start[1], pair.dest[0], pair.dest[1]);
    
    assert.strictEqual(pts.length, 36, `${pair.name}: Must produce exactly 36 points (0 to 35)`);
    
    // First point matches start
    assert.strictEqual(pts[0].lat, pair.start[0], `${pair.name}: First lat must match start`);
    assert.strictEqual(pts[0].lng, pair.start[1], `${pair.name}: First lng must match start`);
    assert.strictEqual(pts[0].percent, 0, `${pair.name}: First percent must be 0`);
    
    // Last point matches destination
    assert.strictEqual(pts[35].lat, pair.dest[0], `${pair.name}: Last lat must match dest`);
    assert.strictEqual(pts[35].lng, pair.dest[1], `${pair.name}: Last lng must match dest`);
    assert.strictEqual(pts[35].percent, 100, `${pair.name}: Last percent must be 100`);

    // Intermediate points check
    for (let i = 0; i < pts.length; i++) {
        const pt = pts[i];
        assert(!isNaN(pt.lat) && isFinite(pt.lat), `Point ${i} lat must be finite number`);
        assert(!isNaN(pt.lng) && isFinite(pt.lng), `Point ${i} lng must be finite number`);
        assert(pt.percent >= 0 && pt.percent <= 100, `Point ${i} percent must be in [0, 100]`);
        
        // Check curve max offset is <= 0.015
        const ratio = i / 35;
        const linearLat = pair.start[0] + (pair.dest[0] - pair.start[0]) * ratio;
        const diff = Math.abs(pt.lat - linearLat);
        assert(diff <= 0.0155, `Curve offset must be bounded: diff=${diff}`);
    }
}
console.log("  -> Simulation math verified across all test coordinate pairs: PASS");

// -------------------------------------------------------------
// Part 2: DOM & Leaflet Runtime Environment Mock
// -------------------------------------------------------------
console.log("\n[2/3] Testing tripTracking.js in Simulated DOM...");

class MockElement {
    constructor(id = "", attributes = {}) {
        this.id = id;
        this.attributes = { ...attributes };
        this.style = {};
        this.className = "";
        this.textContent = "";
        this.eventListeners = {};
        this.disabled = false;
    }
    getAttribute(name) {
        return this.attributes[name] !== undefined ? String(this.attributes[name]) : null;
    }
    setAttribute(name, value) {
        this.attributes[name] = String(value);
    }
    addEventListener(event, fn) {
        this.eventListeners[event] = this.eventListeners[event] || [];
        this.eventListeners[event].push(fn);
    }
    trigger(event, data) {
        if (this.eventListeners[event]) {
            for (const fn of this.eventListeners[event]) {
                fn(data);
            }
        }
    }
}

function createMockEnvironment(mapAttributes = {}, panelAttributes = {}) {
    const elements = new Map();

    const tripMap = new MockElement("trip-map", {
        "data-trip-id": "42",
        "data-from-city": "Oslo",
        "data-to-city": "Bergen",
        "data-from-address": "Karl Johans gate 1",
        "data-to-address": "Bryggen 10",
        "data-start-lat": "59.9139",
        "data-start-lng": "10.7522",
        "data-dest-lat": "60.3913",
        "data-dest-lng": "5.3221",
        "data-current-lat": "59.9139",
        "data-current-lng": "10.7522",
        ...mapAttributes
    });
    elements.set("trip-map", tripMap);

    // Other tracking elements
    elements.set("current-lat-val", new MockElement("current-lat-val"));
    elements.set("current-lng-val", new MockElement("current-lng-val"));
    elements.set("tracking-status-badge", new MockElement("tracking-status-badge"));

    // Driver simulation panel elements
    const simPanel = new MockElement("driver-simulation-panel", {
        "data-trip-id": "42",
        "data-start-lat": "59.9139",
        "data-start-lng": "10.7522",
        "data-dest-lat": "60.3913",
        "data-dest-lng": "5.3221",
        ...panelAttributes
    });
    elements.set("driver-simulation-panel", simPanel);
    elements.set("btn-start-simulation", new MockElement("btn-start-simulation"));
    elements.set("btn-pause-simulation", new MockElement("btn-pause-simulation"));
    elements.set("btn-reset-simulation", new MockElement("btn-reset-simulation"));
    elements.set("sim-progress-bar", new MockElement("sim-progress-bar"));
    elements.set("sim-progress-text", new MockElement("sim-progress-text"));
    elements.set("sim-lat-val", new MockElement("sim-lat-val"));
    elements.set("sim-lng-val", new MockElement("sim-lng-val"));
    elements.set("sim-status-badge", new MockElement("sim-status-badge"));

    const docListeners = {};
    const mockDocument = {
        readyState: "complete",
        getElementById: (id) => elements.get(id) || null,
        querySelector: (selector) => {
            if (selector === ".vehicle-coords-popup") {
                return new MockElement("", {});
            }
            return null;
        },
        querySelectorAll: () => [],
        addEventListener: (event, fn) => {
            docListeners[event] = docListeners[event] || [];
            docListeners[event].push(fn);
        }
    };

    // Mock Leaflet
    const mockMarkers = [];
    let fittedBounds = null;
    const mockL = {
        Icon: { Default: { imagePath: "" } },
        divIcon: (opts) => ({ type: "divIcon", opts }),
        tileLayer: () => ({
            on: () => {},
            addTo: () => {}
        }),
        marker: (coords, opts) => {
            const m = {
                coords,
                opts,
                popups: [],
                bindPopup: function(html) { this.popups.push(html); return this; },
                addTo: function(map) { mockMarkers.push(this); return this; },
                setLatLng: function(newCoords) { this.coords = newCoords; return this; }
            };
            return m;
        },
        polyline: (coords, opts) => ({
            coords,
            opts,
            addTo: () => {}
        }),
        map: (el) => ({
            setView: function() { return this; },
            fitBounds: function(b) { fittedBounds = b; return this; }
        })
    };

    // Mock SignalR
    const signalRHandlers = {};
    const invokedMethods = [];
    const mockSignalRConnection = {
        state: "Connected",
        on: (event, fn) => {
            signalRHandlers[event] = signalRHandlers[event] || [];
            signalRHandlers[event].push(fn);
        },
        off: (event) => {
            delete signalRHandlers[event];
        },
        invoke: async (method, ...args) => {
            invokedMethods.push({ method, args });
            return Promise.resolve();
        },
        start: async () => Promise.resolve()
    };

    const mockSignalR = {
        HubConnectionState: { Connected: "Connected", Disconnected: "Disconnected" },
        HubConnectionBuilder: function() {
            return {
                withUrl: function() { return this; },
                withAutomaticReconnect: function() { return this; },
                build: () => mockSignalRConnection
            };
        }
    };

    return {
        elements,
        mockDocument,
        mockL,
        mockSignalR,
        mockSignalRConnection,
        signalRHandlers,
        invokedMethods,
        mockMarkers
    };
}

// Test executing tripTracking.js
const tripTrackingCode = fs.readFileSync(path.join(__dirname, "../wwwroot/js/tripTracking.js"), "utf8");

{
    const env = createMockEnvironment();
    const sandbox = {
        document: env.mockDocument,
        window: {
            signalRConnection: env.mockSignalRConnection
        },
        L: env.mockL,
        signalR: env.mockSignalR,
        console: console,
        parseInt: parseInt,
        parseFloat: parseFloat
    };

    const runTripTracking = new Function(
        "document", "window", "L", "signalR", "console", "parseInt", "parseFloat",
        tripTrackingCode
    );

    runTripTracking(
        sandbox.document, sandbox.window, sandbox.L, sandbox.signalR,
        sandbox.console, sandbox.parseInt, sandbox.parseFloat
    );

    // Verify map initialized
    assert(sandbox.window.tripMap, "window.tripMap must be exposed");
    assert.strictEqual(sandbox.window.tripMap.tripId, 42);
    assert.strictEqual(sandbox.window.tripMap.startLat, 59.9139);
    assert.strictEqual(sandbox.window.tripMap.destLat, 60.3913);

    // Verify markers placed
    assert.strictEqual(env.mockMarkers.length, 3, "Must create 3 markers: pickup, dest, vehicle");

    // Verify SignalR listener registered
    assert(env.signalRHandlers["ReceiveLocationUpdate"], "ReceiveLocationUpdate must be registered");

    // Simulate SignalR incoming location update
    const handler = env.signalRHandlers["ReceiveLocationUpdate"][0];
    handler(42, 60.1234, 7.5678);

    assert.strictEqual(env.elements.get("current-lat-val").textContent, "60.1234");
    assert.strictEqual(env.elements.get("current-lng-val").textContent, "7.5678");
    assert.strictEqual(env.elements.get("tracking-status-badge").textContent, "Live Tracking Active");
    console.log("  -> tripTracking.js correctly sets up map, markers, and updates coordinates on SignalR: PASS");
}

// -------------------------------------------------------------
// Part 3: Test driverSimulation.js Controls and SignalR Publishing
// -------------------------------------------------------------
console.log("\n[3/3] Testing driverSimulation.js Simulation Controls & Telemetry...");

const driverSimCode = fs.readFileSync(path.join(__dirname, "../wwwroot/js/driverSimulation.js"), "utf8");

{
    const env = createMockEnvironment();
    const sandbox = {
        document: env.mockDocument,
        window: {
            signalRConnection: env.mockSignalRConnection,
            tripMap: {
                updatePosition: (lat, lng) => {
                    env.elements.get("current-lat-val").textContent = lat.toFixed(4);
                    env.elements.get("current-lng-val").textContent = lng.toFixed(4);
                }
            }
        },
        L: env.mockL,
        signalR: env.mockSignalR,
        console: console,
        parseInt: parseInt,
        parseFloat: parseFloat,
        setInterval: (fn, ms) => {
            return { fn, ms, id: 1 };
        },
        clearInterval: () => {}
    };

    const runDriverSim = new Function(
        "document", "window", "L", "signalR", "console", "parseInt", "parseFloat", "setInterval", "clearInterval",
        driverSimCode
    );

    runDriverSim(
        sandbox.document, sandbox.window, sandbox.L, sandbox.signalR,
        sandbox.console, sandbox.parseInt, sandbox.parseFloat,
        sandbox.setInterval, sandbox.clearInterval
    );

    // Verify initial display set to start coordinates
    assert.strictEqual(env.elements.get("sim-lat-val").textContent, "59.9139");
    assert.strictEqual(env.elements.get("sim-lng-val").textContent, "10.7522");
    assert.strictEqual(env.elements.get("sim-progress-bar").style.width, "0%");

    // Trigger Start Simulation
    const btnStart = env.elements.get("btn-start-simulation");
    assert(btnStart.eventListeners["click"], "Start button must have click listener");
    btnStart.trigger("click");

    assert.strictEqual(btnStart.disabled, true, "Start button disabled during active simulation");
    assert.strictEqual(env.elements.get("btn-pause-simulation").disabled, false, "Pause button enabled");
    assert.strictEqual(env.elements.get("sim-status-badge").textContent, "Simulating GPS Updates...");

    // Check that first waypoint SendLocationUpdate was dispatched
    const locationDispatches = env.invokedMethods.filter(m => m.method === "SendLocationUpdate");
    assert(locationDispatches.length >= 1, "Must invoke SendLocationUpdate on start");
    assert.strictEqual(locationDispatches[0].args[0], 42, "Trip ID must match");
    assert.strictEqual(locationDispatches[0].args[1], 59.9139, "First lat must match start");
    assert.strictEqual(locationDispatches[0].args[2], 10.7522, "First lng must match start");

    // Trigger Pause Simulation
    const btnPause = env.elements.get("btn-pause-simulation");
    btnPause.trigger("click");
    assert.strictEqual(btnStart.disabled, false, "Start button re-enabled on pause");
    assert.strictEqual(btnPause.disabled, true, "Pause button disabled on pause");
    assert.strictEqual(env.elements.get("sim-status-badge").textContent, "Simulation Paused");

    // Trigger Reset Simulation
    const btnReset = env.elements.get("btn-reset-simulation");
    btnReset.trigger("click");
    assert.strictEqual(env.elements.get("sim-progress-bar").style.width, "0%");
    assert.strictEqual(env.elements.get("sim-status-badge").textContent, "Ready to simulate");

    console.log("  -> driverSimulation.js correctly initializes, emits GPS waypoints, handles Start/Pause/Reset: PASS");
}

// -------------------------------------------------------------
// Part 4: Edge Cases & Resiliency Testing
// -------------------------------------------------------------
console.log("\n[4/4] Testing Edge Cases, Graceful Degradation & Missing Elements...");

// Edge case 1: trip-map element does not exist on page (e.g. non-active trip view)
{
    const emptyDoc = {
        readyState: "complete",
        getElementById: () => null,
        addEventListener: () => {}
    };
    let warned = false;
    const runTripTracking = new Function("document", "window", "L", "signalR", "console", tripTrackingCode);
    assert.doesNotThrow(() => {
        runTripTracking(emptyDoc, {}, {}, {}, { warn: () => { warned = true; } });
    }, "tripTracking must exit quietly without error when #trip-map is absent");
    console.log("  -> Missing #trip-map handled cleanly: PASS");
}

// Edge case 2: Leaflet L is undefined (CDN/offline failure)
{
    const env = createMockEnvironment();
    let warned = false;
    const runTripTracking = new Function("document", "window", "L", "signalR", "console", "parseInt", "parseFloat", tripTrackingCode);
    assert.doesNotThrow(() => {
        runTripTracking(env.mockDocument, {}, undefined, env.mockSignalR, { warn: () => { warned = true; } }, parseInt, parseFloat);
    }, "tripTracking must warn and exit cleanly when Leaflet L is undefined");
    assert(warned, "Should emit a warning when Leaflet is not loaded");
    console.log("  -> Undefined Leaflet library handled gracefully with warning: PASS");
}

// Edge case 3: driver-simulation-panel does not exist on page (e.g. customer view)
{
    const emptyDoc = {
        readyState: "complete",
        getElementById: () => null,
        addEventListener: () => {}
    };
    const runDriverSim = new Function("document", "window", "L", "signalR", "console", driverSimCode);
    assert.doesNotThrow(() => {
        runDriverSim(emptyDoc, {}, {}, {}, console);
    }, "driverSimulation must exit quietly without error when panel is absent");
    console.log("  -> Missing #driver-simulation-panel handled cleanly: PASS");
}

// Edge case 4: Non-numeric / NaN data attributes in trip-map
{
    const env = createMockEnvironment({
        "data-start-lat": "invalid_lat",
        "data-start-lng": "invalid_lng",
        "data-dest-lat": "invalid_lat",
        "data-dest-lng": "invalid_lng"
    });
    const sandbox = {
        document: env.mockDocument,
        window: { signalRConnection: env.mockSignalRConnection },
        L: env.mockL,
        signalR: env.mockSignalR,
        console: console,
        parseInt: parseInt,
        parseFloat: parseFloat
    };
    const runTripTracking = new Function("document", "window", "L", "signalR", "console", "parseInt", "parseFloat", tripTrackingCode);
    assert.doesNotThrow(() => {
        runTripTracking(sandbox.document, sandbox.window, sandbox.L, sandbox.signalR, sandbox.console, sandbox.parseInt, sandbox.parseFloat);
    }, "tripTracking must fallback to Oslo defaults when coordinates are invalid");
    assert.strictEqual(sandbox.window.tripMap.startLat, 59.9139, "Should fallback to Oslo 59.9139");
    assert.strictEqual(sandbox.window.tripMap.startLng, 10.7522, "Should fallback to Oslo 10.7522");
    console.log("  -> Invalid coordinate data attributes fallback to defaults: PASS");
}

console.log("\n=== ALL EMPIRICAL JAVASCRIPT & SIMULATION STRESS TESTS PASSED ===");
