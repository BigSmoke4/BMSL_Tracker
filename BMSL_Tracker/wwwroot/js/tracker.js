/* ==========================================================================
 * BMSL Tracker — live map client
 * - Leaflet map + movement trails
 * - SignalR (cookie-authenticated) for real-time updates with auto-reconnect
 * - HTML5 Geolocation publishing, client-side throttling
 * All user-controlled strings are HTML-escaped before touching the DOM.
 * ========================================================================== */
(function () {
    "use strict";

    const mapEl = document.getElementById("map");
    if (!mapEl || typeof L === "undefined" || typeof signalR === "undefined") {
        return;
    }

    // ---- tuning -----------------------------------------------------------
    const UPDATE_INTERVAL_MS = 5000;      // minimum time between two sends
    const MIN_MOVE_METERS = 2;            // skip near-duplicate samples
    const ONLINE_WINDOW_MS = 10 * 60 * 1000;
    const TRAIL_MAX_POINTS = 30;
    const PRUNE_INTERVAL_MS = 30 * 1000;

    // ---- leaflet markers from vendored assets -----------------------------
    delete L.Icon.Default.prototype._getIconUrl;
    L.Icon.Default.mergeOptions({
        iconRetinaUrl: "/lib/leaflet/images/marker-icon-2x.png",
        iconUrl: "/lib/leaflet/images/marker-icon.png",
        shadowUrl: "/lib/leaflet/images/marker-shadow.png"
    });

    const map = L.map(mapEl).setView([0, 0], 2);
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        attribution: "&copy; OpenStreetMap contributors"
    }).addTo(map);

    // ---- state --------------------------------------------------------------
    const markers = new Map();
    const trails = new Map();
    const lastSeen = new Map();
    const colorCache = new Map();

    let myUserId = null;
    let lastSentAt = 0;
    let lastSentPoint = null;
    let watchId = null;
    let centeredOnMe = false;

    // ---- small helpers ------------------------------------------------------
    const $ = (id) => document.getElementById(id);

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function setStatus(text, variant) {
        const el = $("status");
        if (!el) return;
        el.textContent = text;
        el.className = "badge text-bg-" + (variant || "secondary");
    }

    function setOnlineCount() {
        const el = $("online-count");
        if (el) {
            el.textContent = markers.size + " online";
        }
    }

    function distanceMeters(a, b) {
        const R = 6371e3;
        const toRad = (d) => (d * Math.PI) / 180;
        const dLat = toRad(b[0] - a[0]);
        const dLng = toRad(b[1] - a[1]);
        const s =
            Math.sin(dLat / 2) ** 2 +
            Math.cos(toRad(a[0])) * Math.cos(toRad(b[0])) * Math.sin(dLng / 2) ** 2;
        return 2 * R * Math.atan2(Math.sqrt(s), Math.sqrt(1 - s));
    }

    function colorFor(userId) {
        if (userId === myUserId) return "#0d6efd";
        let cached = colorCache.get(userId);
        if (cached) return cached;
        let hash = 0;
        for (let i = 0; i < userId.length; i++) {
            hash = (hash * 31 + userId.charCodeAt(i)) | 0;
        }
        cached = "hsl(" + (Math.abs(hash) % 360) + ", 65%, 45%)";
        colorCache.set(userId, cached);
        return cached;
    }

    function popupHtml(userName, accuracy, timestamp) {
        const seen = timestamp ? new Date(timestamp).toLocaleString() : "—";
        const acc = typeof accuracy === "number" ? accuracy.toFixed(1) + " m" : "n/a";
        return (
            "<strong>" + escapeHtml(userName || "User") + "</strong><br />" +
            "Accuracy: " + escapeHtml(acc) + "<br />" +
            "Last seen: " + escapeHtml(seen)
        );
    }

    function showToast(message, variant) {
        const container = $("toast-container");
        if (!container || typeof bootstrap === "undefined") return;
        const el = document.createElement("div");
        el.className = "toast align-items-center text-bg-" + (variant || "dark") + " border-0";
        el.setAttribute("role", "alert");
        el.setAttribute("aria-live", "assertive");
        el.setAttribute("aria-atomic", "true");
        el.innerHTML =
            '<div class="d-flex"><div class="toast-body">' + escapeHtml(message) +
            '</div><button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button></div>';
        container.appendChild(el);
        const toast = new bootstrap.Toast(el, { delay: 6000 });
        toast.show();
        el.addEventListener("hidden.bs.toast", () => el.remove());
    }

    // ---- incoming locations -------------------------------------------------
    function upsertLocation(userId, userName, lat, lng, accuracy, timestamp) {
        const position = [lat, lng];
        lastSeen.set(userId, Date.now());

        let marker = markers.get(userId);
        if (!marker) {
            marker = L.marker(position).addTo(map);
            markers.set(userId, marker);
            trails.set(
                userId,
                L.polyline([], { color: colorFor(userId), weight: 4, opacity: 0.6 }).addTo(map)
            );
        }
        marker.setLatLng(position);
        marker.bindPopup(popupHtml(userName, accuracy, timestamp));
        if (marker.isPopupOpen()) {
            marker.getPopup().setContent(popupHtml(userName, accuracy, timestamp));
        }

        const trail = trails.get(userId);
        trail.addLatLng(position);
        if (trail.getLatLngs().length > TRAIL_MAX_POINTS) {
            trail.setLatLngs(trail.getLatLngs().slice(-TRAIL_MAX_POINTS));
        }

        setOnlineCount();

        if (userId === myUserId) {
            const button = $("center-me");
            if (button) button.disabled = false;
            if (!centeredOnMe) {
                map.setView(position, 16);
                centeredOnMe = true;
            }
        }
    }

    function pruneStaleUsers() {
        const cutoff = Date.now() - ONLINE_WINDOW_MS;
        for (const [userId, seenAt] of lastSeen.entries()) {
            if (seenAt < cutoff) {
                lastSeen.delete(userId);
                const marker = markers.get(userId);
                if (marker) map.removeLayer(marker);
                markers.delete(userId);
                const trail = trails.get(userId);
                if (trail) map.removeLayer(trail);
                trails.delete(userId);
            }
        }
        setOnlineCount();
    }

    // ---- signalr ------------------------------------------------------------
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/trackerHub")
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.on("YourId", (userId) => {
        myUserId = userId;
    });

    connection.on("ReceiveLocation", upsertLocation);

    connection.onreconnecting((err) => {
        setStatus("Reconnecting…", "warning");
        if (err) console.warn("SignalR reconnecting:", err);
    });

    connection.onreconnected(() => {
        setStatus("Connected", "success");
        startTracking();
    });

    connection.onclose(() => {
        setStatus("Disconnected", "danger");
        stopTracking();
        showToast("Live connection lost. Refresh the page to reconnect.", "danger");
    });

    function connect() {
        return connection
            .start()
            .then(() => {
                setStatus("Connected", "success");
                startTracking();
            })
            .catch((err) => {
                console.error("SignalR connection error:", err);
                setStatus("Connection failed — retrying…", "danger");
                setTimeout(connect, 10000);
            });
    }

    // ---- geolocation publishing ---------------------------------------------
    function requireSecureContext() {
        return !window.isSecureContext && window.location.hostname !== "localhost";
    }

    function startTracking() {
        if (watchId !== null) return; // already watching

        if (requireSecureContext()) {
            setStatus("HTTPS required for GPS", "danger");
            showToast("Geolocation requires a secure context (HTTPS).", "warning");
            return;
        }

        if (!("geolocation" in navigator)) {
            setStatus("Geolocation unsupported", "danger");
            return;
        }

        setStatus("Acquiring GPS…", "info");
        watchId = navigator.geolocation.watchPosition(
            onPosition,
            onGeolocationError,
            { enableHighAccuracy: true, maximumAge: 3000, timeout: 15000 }
        );
    }

    function stopTracking() {
        if (watchId !== null && "geolocation" in navigator) {
            navigator.geolocation.clearWatch(watchId);
        }
        watchId = null;
    }

    function onGeolocationError(error) {
        console.error("Geolocation error:", error);
        let msg = "Geolocation error";
        switch (error.code) {
            case error.PERMISSION_DENIED: msg = "Location permission denied"; break;
            case error.POSITION_UNAVAILABLE: msg = "Location unavailable"; break;
            case error.TIMEOUT: msg = "Location timeout"; break;
        }
        setStatus(msg, "danger");
        showToast(msg + ". Allow location access and refresh.", "warning");
    }

    function onPosition(position) {
        if (connection.state !== signalR.HubConnectionState.Connected) return;

        const now = Date.now();
        const point = [position.coords.latitude, position.coords.longitude];
        const elapsed = now - lastSentAt;
        const movedEnough =
            lastSentPoint === null || distanceMeters(lastSentPoint, point) >= MIN_MOVE_METERS;

        if (!movedEnough && elapsed < 3 * UPDATE_INTERVAL_MS) return; // idle: send rarely
        if (elapsed < UPDATE_INTERVAL_MS) return;                     // hard throttle

        lastSentAt = now;
        lastSentPoint = point;
        setStatus("Broadcasting position", "success");

        connection
            .invoke("SendLocation", position.coords.latitude, position.coords.longitude, position.coords.accuracy || 0)
            .catch((err) => {
                console.error("SendLocation failed:", err);
                setStatus("Transmission error", "warning");
            });
    }

    // ---- UI ------------------------------------------------------------------
    const centerBtn = $("center-me");
    if (centerBtn) {
        centerBtn.addEventListener("click", () => {
            const marker = myUserId ? markers.get(myUserId) : null;
            if (marker) {
                map.setView(marker.getLatLng(), 16);
                centeredOnMe = true;
            } else {
                showToast("Waiting for your first GPS fix…", "info");
            }
        });
    }

    setInterval(() => {
        const button = $("center-me");
        if (button) {
            button.disabled = !(myUserId && markers.has(myUserId));
        }
        pruneStaleUsers();
    }, PRUNE_INTERVAL_MS);

    connect();
})();
