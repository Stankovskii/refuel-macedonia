/*  ReFuel service worker — deliberately does nothing.
 *
 *  Chrome only fires `beforeinstallprompt` (the "install as an app" offer) for a
 *  page that has a registered service worker with a fetch handler. That is the
 *  only reason this file exists.
 *
 *  It caches nothing and serves nothing from cache: every request goes straight
 *  to the network, exactly as if no service worker were installed. This means it
 *  can never show a stale page, never hide a server error behind an offline
 *  screen, and never has to be cleared between runs during development.
 *
 *  On activation it also deletes any cache left behind by earlier versions of
 *  this file, so upgrading cleans up after itself.
 */

self.addEventListener('install', function () {
    self.skipWaiting();
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys()
            .then(function (keys) {
                return Promise.all(keys.map(function (key) { return caches.delete(key); }));
            })
            .catch(function () { /* nothing cached, nothing to clean */ })
            .then(function () { return self.clients.claim(); })
    );
});

/*  Present, but a pure pass-through. Required for installability. */
self.addEventListener('fetch', function (event) {
    return;
});
