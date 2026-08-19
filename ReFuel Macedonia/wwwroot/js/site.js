(function (window, document) {
    "use strict";

    var ReFuel = {};

    ReFuel.country = { lat: 41.6086, lng: 21.7453, zoom: 8 };

    ReFuel.tileLayer = function () {
        return L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        });
    };

    ReFuel.money = function (value) {
        if (value === null || value === undefined) return "-";
        return Number(value).toFixed(2) + " ден";
    };

    ReFuel.createStationMap = function (elementId, options) {
        options = options || {};

        var map = L.map(elementId, { scrollWheelZoom: true }).setView(
            [options.lat || ReFuel.country.lat, options.lng || ReFuel.country.lng],
            options.zoom || ReFuel.country.zoom);

        ReFuel.tileLayer().addTo(map);

        var cluster = (typeof L.markerClusterGroup === "function")
            ? L.markerClusterGroup({ maxClusterRadius: 55, disableClusteringAtZoom: 14 })
            : L.layerGroup();

        map.addLayer(cluster);

        map.refuelCluster = cluster;
        return map;
    };

    ReFuel.stationMarker = function (station, detailUrlPrefix) {
        var color = station.color || "#0F766E";
        var price = station.price ? ReFuel.money(station.price) : "";

        var icon = L.divIcon({
            className: "refuel-marker",
            html: '<div style="background:' + color + ';color:#fff;border-radius:8px;padding:2px 6px;' +
                  'font:700 11px/1.3 Segoe UI,sans-serif;white-space:nowrap;box-shadow:0 1px 4px rgba(0,0,0,.35);' +
                  'border:2px solid #fff">' + (price || "⛽") + "</div>",
            iconSize: [10, 10],
            iconAnchor: [20, 12]
        });

        var marker = L.marker([station.lat, station.lng], { icon: icon });

        var html = '<div class="popup-title">' + ReFuel.escape(station.name) + "</div>";
        if (station.brand) html += '<div class="muted small">' + ReFuel.escape(station.brand) + "</div>";
        if (station.price) html += '<div class="popup-price">' + ReFuel.money(station.price) + "</div>";
        html += '<div class="small muted">' + ReFuel.escape(station.city || "") + "</div>";
        if (station.open24h) html += '<span class="chip">24/7</span> ';
        if (station.lpg) html += '<span class="chip">LPG</span> ';
        if (station.verified) html += '<span class="chip">✓ OSM</span>';
        html += '<div style="margin-top:.5rem"><a href="' + (detailUrlPrefix || "/Stations/Details/") + station.id + '">→</a></div>';

        marker.bindPopup(html);
        return marker;
    };

    ReFuel.escape = function (value) {
        if (value === null || value === undefined) return "";
        return String(value)
            .replace(/&/g, "&amp;").replace(/</g, "&lt;")
            .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
    };

    ReFuel.loadStations = function (map, url, detailUrlPrefix, onDone) {
        fetch(url, { headers: { "Accept": "application/json" } })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                map.refuelCluster.clearLayers();
                data.forEach(function (station) {
                    if (!station.lat || !station.lng) return;
                    map.refuelCluster.addLayer(ReFuel.stationMarker(station, detailUrlPrefix));
                });
                if (typeof onDone === "function") onDone(data);
            })
            .catch(function (error) { console.error("ReFuel: station load failed", error); });
    };

    ReFuel.locate = function (callback) {
        if (!navigator.geolocation) { callback(null); return; }
        navigator.geolocation.getCurrentPosition(
            function (position) { callback({ lat: position.coords.latitude, lng: position.coords.longitude }); },
            function () { callback(null); },
            { enableHighAccuracy: false, timeout: 8000, maximumAge: 300000 });
    };

    /* Reads a CSS custom property from <html>, so charts follow the theme. */
    ReFuel.cssVar = function (name, fallback) {
        try {
            var v = getComputedStyle(document.documentElement).getPropertyValue(name);
            return (v && v.trim()) || fallback;
        } catch (e) { return fallback; }
    };

    ReFuel.lineChart = function (canvasId, series, options) {
        var canvas = document.getElementById(canvasId);
        if (!canvas || typeof Chart === "undefined") return null;

        options = options || {};

        var labels = [];
        series.forEach(function (s) {
            (s.points || []).forEach(function (p) {
                if (labels.indexOf(p.date) === -1) labels.push(p.date);
            });
        });

        var datasets = series.map(function (s) {
            var map = {};
            (s.points || []).forEach(function (p) { map[p.date] = p.price; });

            return {
                label: s.label,
                data: labels.map(function (l) {
                    return Object.prototype.hasOwnProperty.call(map, l) ? map[l] : null;
                }),
                borderColor: s.color,
                backgroundColor: s.color + "22",
                borderWidth: 2,
                borderDash: s.dashed ? [6, 4] : undefined,
                tension: .25,
                spanGaps: true,
                pointRadius: labels.length > 40 ? 0 : 2,
                pointHoverRadius: 4,
                fill: !!s.fill
            };
        });

        return new Chart(canvas.getContext("2d"), {
            type: "line",
            data: { labels: labels, datasets: datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: { display: series.length > 1, labels: { boxWidth: 12, font: { size: 11 } } },
                    tooltip: {
                        callbacks: {
                            label: function (ctx) { return ctx.dataset.label + ": " + ReFuel.money(ctx.parsed.y); }
                        }
                    }
                },
                scales: {
                    x: { type: "category", ticks: { maxTicksLimit: 10, font: { size: 10 } }, grid: { display: false } },
                    y: {
                        beginAtZero: false,
                        ticks: { font: { size: 10 }, callback: function (v) { return v + " ден"; } },
                        grid: { color: ReFuel.cssVar("--line-2", "#EBEFF4") }
                    }
                }
            }
        });
    };

    ReFuel.barChart = function (canvasId, labels, values, colors, label) {
        var canvas = document.getElementById(canvasId);
        if (!canvas || typeof Chart === "undefined") return null;

        return new Chart(canvas.getContext("2d"), {
            type: "bar",
            data: {
                labels: labels,
                datasets: [{ label: label || "", data: values, backgroundColor: colors, borderRadius: 6 }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                indexAxis: "y",
                plugins: { legend: { display: false } },
                scales: {
                    x: { beginAtZero: false, ticks: { font: { size: 10 } }, grid: { color: ReFuel.cssVar("--line-2", "#EBEFF4") } },
                    y: { ticks: { font: { size: 10 } }, grid: { display: false } }
                }
            }
        });
    };

    ReFuel.initAddressPicker = function (mapId, latInput, lngInput) {
        var latField = document.getElementById(latInput);
        var lngField = document.getElementById(lngInput);
        var box = document.getElementById(mapId);
        if (!latField || !lngField || !box) return;

        // No pin until the customer places one: an empty field must stay empty, not silently
        // default to the centre of Skopje.
        var lat = parseFloat(latField.value);
        var lng = parseFloat(lngField.value);
        var hasPoint = isFinite(lat) && isFinite(lng) && lat !== 0 && lng !== 0;

        var map = L.map(mapId).setView(
            hasPoint ? [lat, lng] : [ReFuel.country.lat, ReFuel.country.lng],
            hasPoint ? 13 : ReFuel.country.zoom);
        ReFuel.tileLayer().addTo(map);

        var marker = null;
        var hint = document.getElementById("pick-hint");

        function sync(position) {
            latField.value = position.lat.toFixed(6);
            lngField.value = position.lng.toFixed(6);
            box.classList.remove("map-unpicked");
            if (hint) hint.classList.add("picked");
        }

        function place(position) {
            if (!marker) {
                marker = L.marker(position, { draggable: true }).addTo(map);
                marker.on("dragend", function () { sync(marker.getLatLng()); });
            } else {
                marker.setLatLng(position);
            }
            sync(marker.getLatLng());
        }

        if (hasPoint) place([lat, lng]);

        map.on("click", function (e) { place(e.latlng); });

        var locateButton = document.getElementById("btn-locate");
        if (locateButton) {
            locateButton.addEventListener("click", function (e) {
                e.preventDefault();
                ReFuel.locate(function (position) {
                    if (!position) { alert(locateButton.getAttribute("data-nogeo") || "Location unavailable"); return; }
                    map.setView([position.lat, position.lng], 15);
                    place([position.lat, position.lng]);
                });
            });
        }
    };

    /* Signature pad: a plain canvas, pointer events (finger, pen, mouse), sharp on high-DPI
       screens, exported as a PNG data URL into a hidden input on submit. */
    ReFuel.initSignaturePad = function (canvasId, inputId, clearId, formId) {
        var canvas = document.getElementById(canvasId);
        var input = document.getElementById(inputId);
        if (!canvas || !input) return null;

        var ctx = canvas.getContext("2d");
        var drawing = false, dirty = false, last = null;

        function resize() {
            var ratio = Math.max(1, window.devicePixelRatio || 1);
            var width = canvas.clientWidth || 600, height = canvas.clientHeight || 220;
            var snapshot = dirty ? canvas.toDataURL("image/png") : null;
            canvas.width = Math.round(width * ratio);
            canvas.height = Math.round(height * ratio);
            ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
            ctx.lineWidth = 2.2;
            ctx.lineCap = "round";
            ctx.lineJoin = "round";
            ctx.strokeStyle = "#0B0D12";
            if (snapshot) {
                var img = new Image();
                img.onload = function () { ctx.drawImage(img, 0, 0, width, height); };
                img.src = snapshot;
            }
        }

        function point(e) {
            var rect = canvas.getBoundingClientRect();
            return { x: e.clientX - rect.left, y: e.clientY - rect.top };
        }

        canvas.addEventListener("pointerdown", function (e) {
            e.preventDefault();
            canvas.setPointerCapture(e.pointerId);
            drawing = true; last = point(e);
            ctx.beginPath(); ctx.moveTo(last.x, last.y); ctx.lineTo(last.x + 0.1, last.y + 0.1); ctx.stroke();
            dirty = true; canvas.classList.add("signed");
        });
        canvas.addEventListener("pointermove", function (e) {
            if (!drawing) return;
            e.preventDefault();
            var p = point(e);
            ctx.beginPath(); ctx.moveTo(last.x, last.y); ctx.lineTo(p.x, p.y); ctx.stroke();
            last = p;
        });
        function stop(e) { if (drawing) { drawing = false; try { canvas.releasePointerCapture(e.pointerId); } catch (err) { } } }
        canvas.addEventListener("pointerup", stop);
        canvas.addEventListener("pointercancel", stop);
        canvas.addEventListener("pointerleave", stop);

        var clearButton = clearId ? document.getElementById(clearId) : null;
        if (clearButton) {
            clearButton.addEventListener("click", function (e) {
                e.preventDefault();
                ctx.save(); ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.clearRect(0, 0, canvas.width, canvas.height); ctx.restore();
                dirty = false; input.value = ""; canvas.classList.remove("signed");
            });
        }

        var form = formId ? document.getElementById(formId) : canvas.closest("form");
        if (form) {
            form.addEventListener("submit", function (e) {
                if (!dirty) {
                    e.preventDefault();
                    canvas.classList.add("missing");
                    var msg = canvas.getAttribute("data-required");
                    if (msg) alert(msg);
                    return;
                }
                input.value = canvas.toDataURL("image/png");
            });
        }

        window.addEventListener("resize", resize);
        resize();
        return { isSigned: function () { return dirty; } };
    };

    /* QR code into an element, when the qrcodejs library is on the page. Falls back to the
       plain link text so a printed document is still verifiable by typing the URL. */
    ReFuel.qr = function (elementId, text, size) {
        var el = document.getElementById(elementId);
        if (!el) return;
        if (typeof QRCode === "undefined") { el.textContent = text; return; }
        el.innerHTML = "";
        new QRCode(el, { text: text, width: size || 120, height: size || 120, correctLevel: QRCode.CorrectLevel.M });
    };

    /* Delivery zones as translucent circles (landing page coverage map). */
    ReFuel.drawZones = function (map, zones) {
        if (!map || !zones || typeof L === "undefined") return;
        zones.forEach(function (z) {
            if (!z || !z.lat || !z.lng) return;
            L.circle([z.lat, z.lng], {
                radius: (z.radiusKm || 25) * 1000,
                color: z.color || "#FF6A00",
                weight: 1.5,
                opacity: .7,
                fillColor: z.color || "#FF6A00",
                fillOpacity: .08,
                interactive: false
            }).addTo(map);
        });
    };

    window.ReFuel = ReFuel;
})(window, document);

(function () {
    var nav = document.getElementById('main-nav');
    var toggle = document.querySelector('[data-nav-toggle]');
    var groups = Array.prototype.slice.call(document.querySelectorAll('.nav-group'));

    function closeGroups(except) {
        groups.forEach(function (group) {
            if (group === except) return;
            group.classList.remove('open');
            var trigger = group.querySelector('.nav-trigger');
            if (trigger) trigger.setAttribute('aria-expanded', 'false');
        });
    }

    groups.forEach(function (group) {
        var trigger = group.querySelector('.nav-trigger');
        if (!trigger) return;

        trigger.addEventListener('click', function (event) {
            event.preventDefault();
            var open = group.classList.toggle('open');
            trigger.setAttribute('aria-expanded', open ? 'true' : 'false');
            closeGroups(group);
        });
    });

    if (toggle && nav) {
        toggle.addEventListener('click', function () {
            var open = nav.classList.toggle('open');
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            if (!open) closeGroups(null);
        });
    }

    document.addEventListener('click', function (event) {
        var inside = event.target.closest ? event.target.closest('.nav-group') : null;
        var onToggle = event.target.closest ? event.target.closest('[data-nav-toggle]') : null;

        if (!inside && !onToggle) closeGroups(null);
        if (onToggle) return;

        var insideNav = event.target.closest ? event.target.closest('#main-nav') : null;

        if (!insideNav && nav && nav.classList.contains('open')) {
            nav.classList.remove('open');
            if (toggle) toggle.setAttribute('aria-expanded', 'false');
        }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') return;
        closeGroups(null);
        if (nav) nav.classList.remove('open');
        if (toggle) toggle.setAttribute('aria-expanded', 'false');
    });
})();

/* ---------------------------------------------------------- site chrome
   Header shadow once the page scrolls, auto-hiding toasts for TempData
   messages and the one-time cookie notice (essential cookies only). */
(function () {
    var header = document.getElementById('site-header');
    if (header) {
        var onScroll = function () {
            header.classList.toggle('scrolled', (window.scrollY || window.pageYOffset || 0) > 8);
        };
        window.addEventListener('scroll', onScroll, { passive: true });
        onScroll();
    }

    function dismiss(toast) {
        if (!toast || toast.classList.contains('hide')) return;
        toast.classList.add('hide');
        window.setTimeout(function () {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, 260);
    }

    Array.prototype.slice.call(document.querySelectorAll('[data-toast]')).forEach(function (toast) {
        var close = toast.querySelector('[data-toast-close]');
        if (close) close.addEventListener('click', function () { dismiss(toast); });
        var timer = window.setTimeout(function () { dismiss(toast); }, 7000);
        toast.addEventListener('mouseenter', function () { window.clearTimeout(timer); });
    });

    /* light / dark theme ------------------------------------------------ */
    function applyChartTheme() {
        if (typeof Chart === 'undefined') return;
        var cs = getComputedStyle(document.documentElement);
        Chart.defaults.color = (cs.getPropertyValue('--ink-3') || '#4F586A').trim();
        Chart.defaults.borderColor = (cs.getPropertyValue('--line-2') || '#EBEFF4').trim();
        if (Chart.instances) {
            Object.keys(Chart.instances).forEach(function (k) {
                var c = Chart.instances[k];
                if (c && typeof c.update === 'function') c.update('none');
            });
        }
    }
    applyChartTheme();

    Array.prototype.slice.call(document.querySelectorAll('[data-theme-toggle]')).forEach(function (btn) {
        btn.addEventListener('click', function () {
            var root = document.documentElement;
            var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
            root.setAttribute('data-theme', next);
            document.cookie = 'rf_theme=' + next + '; Max-Age=' + (60 * 60 * 24 * 365) + '; Path=/; SameSite=Lax';
            applyChartTheme();
        });
    });

    var bar = document.getElementById('cookie-bar');
    if (bar) {
        var accepted = /(^|;\s*)rf_consent=1(;|$)/.test(document.cookie || '');
        if (!accepted) bar.hidden = false;
        var accept = bar.querySelector('[data-cookie-accept]');
        if (accept) {
            accept.addEventListener('click', function () {
                document.cookie = 'rf_consent=1; Max-Age=' + (60 * 60 * 24 * 365) + '; Path=/; SameSite=Lax';
                bar.hidden = true;
            });
        }
    }
})();

/* ---------------------------------------------------------------- PWA
   Registers the service worker and turns the browser's install event into a
   button in the header, so the app can be installed as a desktop window. */
(function () {
    var secure = location.protocol === 'https:' ||
                 location.hostname === 'localhost' ||
                 location.hostname === '127.0.0.1';

    if ('serviceWorker' in navigator && secure) {
        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/sw.js').catch(function () { });
        });
    }

    var deferred = null;
    var button = document.getElementById('install-app');
    if (!button) return;

    window.addEventListener('beforeinstallprompt', function (event) {
        event.preventDefault();
        deferred = event;
        button.hidden = false;
    });

    button.addEventListener('click', function () {
        if (!deferred) return;
        deferred.prompt();
        deferred.userChoice.then(function () {
            deferred = null;
            button.hidden = true;
        });
    });

    window.addEventListener('appinstalled', function () {
        deferred = null;
        button.hidden = true;
    });
})();
