// Phase 178 — SignalR client shim for the standalone web viewer.
//
// Before this file existed, the viewer relied on a periodic refresh and
// exposed a no-op `window.__planscapePhotoRealtime` hook for hosts to
// call manually. With this shim loaded, `window.__planscapeHub` exposes
// a real SignalR-backed transport so the viewer auto-receives:
//
//   - IssueCreated / IssueUpdated  (NotificationHub)
//   - CommentAdded                  (NotificationHub)
//   - SitePhotoCaptured             (NotificationHub — Phase 178)
//   - SitePhotoApproved             (NotificationHub — Phase 178)
//
// Loaded BEFORE coordination-viewer.js so its setupPhotoRealtime() can
// auto-bind on init. Falls back gracefully when the CDN is unreachable
// (the viewer keeps working with the periodic-refresh hook).
//
// P3 — @microsoft/signalr is VENDORED locally (vendor/signalr.min.js, pinned to
// 8.0.7) instead of a third-party CDN: offline-capable + no SRI / CDN-tamper
// exposure. Refresh the vendored file to bump the version.
(function () {
  'use strict';
  if (typeof window === 'undefined') return;
  // The viewer is also hosted inside a React Native WebView for the
  // mobile app (see Planscape/src/components/ModelViewer.tsx). Mobile
  // owns its own realtime client (RealtimeClient), so skip the shim
  // there to avoid double subscriptions.
  if (window.ReactNativeWebView) return;

  // Don't double-init if a host already mounted one.
  if (window.__planscapeHub) return;

  const SIGNALR_CDN_URL = './vendor/signalr.min.js';

  // Read the same query / storage state the main viewer uses so we
  // start the connection only when there's a project + token. Mirrors
  // coordination-viewer.js bootstrap order.
  const params = new URLSearchParams(location.search);
  const projectId = params.get('project');
  const apiBase = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_api_base'))
                 || (params.get('api') || '');
  const token = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_token'))
              || params.get('token') || '';
  const tenantId = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_tenant')) || '';

  // No-project sessions (empty-state CTA) skip the connection entirely.
  if (!projectId) return;

  // Inject the script tag. We could use ES module dynamic import, but
  // a UMD <script> avoids needing type=module on the host page and
  // works in older WebViews that mount the file without a server.
  const tag = document.createElement('script');
  tag.src = SIGNALR_CDN_URL;
  tag.async = true;
  tag.onload = init;
  tag.onerror = () => {
    // CDN unreachable / blocked. Leave the no-op realtime hook in place;
    // coordination-viewer.js falls back to manual refresh.
    console.warn('[planscape-hub] SignalR CDN unreachable — falling back to periodic refresh');
  };
  document.head.appendChild(tag);

  function init() {
    if (!window.signalR) {
      console.warn('[planscape-hub] @microsoft/signalr loaded but global is missing');
      return;
    }
    const SR = window.signalR;
    const hubUrl = (apiBase || '') + '/hubs/notifications';

    // V3 — long-session auth. The captured `token` goes stale after the JWT TTL, so a
    // (re)negotiate after that 401s and SignalR retries with the SAME dead token →
    // negotiate storm. Read the CURRENT token from localStorage each call, and when it's
    // expired/near-expiry exchange the refresh token for a fresh JWT before handing it to
    // SignalR. A clean single reconnect with a live token instead of a storm.
    function currentToken() { try { return localStorage.getItem('planscape_token') || token; } catch (_) { return token; } }
    function isExpiringSoon(t) {
      try {
        const p = JSON.parse(atob(String(t).split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
        return !p.exp || (p.exp * 1000 - Date.now() < 60000);   // <60s left (or no exp)
      } catch (_) { return false; }
    }
    let _refreshing = null;
    function refreshAccessToken() {
      if (_refreshing) return _refreshing;                       // coalesce concurrent refreshes
      const rt = (function () { try { return localStorage.getItem('planscape_refresh'); } catch (_) { return null; } })();
      if (!rt) return Promise.resolve(null);
      _refreshing = fetch((apiBase || '') + '/api/auth/refresh', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: rt }),
      }).then(r => r.ok ? r.json() : null).then(b => {
        if (b && b.accessToken) { try { localStorage.setItem('planscape_token', b.accessToken); if (b.refreshToken) localStorage.setItem('planscape_refresh', b.refreshToken); } catch (_) {} return b.accessToken; }
        return null;
      }).catch(() => null).then(v => { _refreshing = null; return v; });
      return _refreshing;
    }
    async function tokenFactory() {
      let t = currentToken();
      if (isExpiringSoon(t)) { const fresh = await refreshAccessToken(); if (fresh) t = fresh; }
      return t || '';
    }

    const conn = new SR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        // The server's JwtBearerEvents.OnMessageReceived handler also
        // accepts `?access_token=` for SignalR; long-poll-only fall-back
        // works the same way. accessTokenFactory beats the query path
        // because it's the cleanest WebSocket/SSE story.
        accessTokenFactory: tokenFactory,   // V3 — dynamic, refresh-aware (STING_VIZ_SIGNALR_REFRESH)
        // Forward the tenant header so the server's tenant-resolution
        // middleware sees it on the upgrade handshake.
        headers: tenantId ? { 'X-Tenant': tenantId } : undefined,
        // Skip negotiate cuts a round-trip; safe because we know
        // WebSockets is the chosen transport on a modern browser.
        skipNegotiation: false,
        transport: SR.HttpTransportType.WebSockets | SR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 1000, 5000, 15000, 30000])
      .configureLogging(SR.LogLevel.Warning)
      .build();

    // Maintain a small per-event subscriber list so coordination-viewer.js
    // (and any other module that wants to listen) can bind via .on()
    // without caring about the underlying SignalR connection.
    const subs = new Map();
    function emit(event, payload) {
      const list = subs.get(event); if (!list) return;
      for (const fn of list) {
        try { fn(payload); } catch (e) { console.warn('[planscape-hub] handler', event, e); }
      }
    }

    // Server-pushed events — keep this list in lockstep with what
    // NotificationHub emits. Each handler relays to all subscribers.
    [
      'IssueCreated', 'IssueUpdated',
      'CommentAdded',
      'SitePhotoCaptured', 'SitePhotoApproved',
      // Server emits the in-app notification under the event name Notification
      // (NotificationService.cs:62,246 SendAsync). The previous literal here
      // did not match that name, so viewer in-app notifications never fired.
      // Keep this list in lockstep with what NotificationHub emits.
      'Notification',
      // A0 — NotificationHub.JoinProject replies with these to the caller + project
      // group; register them so SignalR stops warning "No client method with the name
      // 'joinedproject'/'presencechanged'". (Dashboard side already done.)
      'JoinedProject', 'PresenceChanged',
    ].forEach(name => conn.on(name, payload => emit(name, payload)));

    conn.onreconnected(() => {
      console.info('[planscape-hub] reconnected — re-joining project', projectId);
      conn.invoke('JoinProject', projectId).catch(err =>
        console.warn('[planscape-hub] JoinProject after reconnect', err));
    });
    conn.onclose(err => {
      if (err) console.warn('[planscape-hub] connection closed', err);
    });

    conn.start()
      .then(() => conn.invoke('JoinProject', projectId))
      .catch(err => console.warn('[planscape-hub] start failed', err));

    window.__planscapeHub = {
      raw: conn,
      on(event, handler) {
        if (typeof handler !== 'function') return;
        let list = subs.get(event); if (!list) { list = []; subs.set(event, list); }
        list.push(handler);
      },
      off(event, handler) {
        const list = subs.get(event); if (!list) return;
        const i = list.indexOf(handler); if (i >= 0) list.splice(i, 1);
      },
      stop() { try { conn.stop(); } catch (_) {} },
      isConnected() {
        return conn && conn.state === SR.HubConnectionState.Connected;
      },
    };

    // Once the hub is mounted, give coordination-viewer.js a chance to
    // re-bind any subscriptions that registered before the shim landed
    // (race when the CDN is slow). The viewer's setupPhotoRealtime()
    // checks for window.__planscapeHub on startup — calling it again is
    // a no-op if already bound.
    if (typeof window.__planscapeRebindHub === 'function') {
      try { window.__planscapeRebindHub(); } catch (_) {}
    }
  }
})();
