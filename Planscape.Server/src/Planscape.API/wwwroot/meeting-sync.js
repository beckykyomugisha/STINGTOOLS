/* meeting-sync.js — live synced 3D meetings client.
 *
 * The server half has always existed (MeetingHub at /hubs/meeting +
 * MeetingRoomController), but no client ever connected to it, so the
 * realtime co-presence feature was dark. This is that client.
 *
 * Activation is OPT-IN via the URL: it only does anything when the viewer
 * is opened with `?meeting=<sessionId>` (a MeetingSession GUID). Without
 * it the script is a complete no-op — zero risk to the standalone viewer.
 *
 *   /viewer.html?project=<pid>&model=<mid>&meeting=<sessionId>
 *
 * What it syncs (v1): live camera ("follow the presenter") + participant
 * presence + K3 overlay pushes. Section/highlight broadcasts are received
 * and exposed as hooks for a fast-follow; camera-follow is the 80% feature.
 *
 * Contract (see MeetingHub.cs):
 *   invoke: JoinSession(sessionId, name) · LeaveSession(sessionId)
 *           BroadcastCamera(sessionId, camera) · BroadcastOverlay(...) · …
 *   on:     ParticipantJoined/Left · CameraMoved({camera}) · OverlayChanged(profile)
 *           SectionChanged({section}) · HighlightChanged({guids}) · RoomChanged(state)
 */
(function () {
  "use strict";

  var params = new URLSearchParams(location.search);
  var sessionId = params.get("meeting") || params.get("session") || "";
  var projectId = params.get("project") || "";
  if (!sessionId) return; // not a meeting — stay inert.

  var TOKEN_KEY = "planscape_token";
  var USER_KEY  = "planscape_user";
  var apiBase   = (params.get("api") || "").replace(/\/$/, ""); // same-origin when blank
  var token     = (function () { try { return localStorage.getItem(TOKEN_KEY) || ""; } catch (e) { return ""; } })();
  var displayName = (function () { try { return (localStorage.getItem(USER_KEY) || "Guest").split("@")[0]; } catch (e) { return "Guest"; } })();

  var state = {
    conn: null,
    following: true,         // followers track the presenter's camera
    applyingRemote: false,   // camera feedback-loop guard
    applyingRemoteHi: false, // highlight feedback-loop guard
    applyingRemoteSec: false,// section feedback-loop guard
    lastSentAt: 0,
    participants: new Map(), // connectionId → { displayName }
  };

  // ── wait for the viewer API, the SignalR lib (shim loads it async), AND the
  //    model to be on screen. BLK-5: joining before the model loads makes us
  //    broadcast/receive camera + highlight + section against an empty scene
  //    (the "stuck at 0%, live but blank" symptom). modelReady is set by the
  //    engine on load success OR failure, and by the coordination layer when
  //    there is no model to load — so this can't hang on a model-less view.
  function ready(cb, tries) {
    tries = tries || 0;
    var V = window.STING_VIEWER;
    var coreReady = V && V.camera && V.controls && window.signalR;
    var modelReady = V && V.modelReady === true;
    if (coreReady && modelReady) return cb();
    if (tries > 900) {
      // ~90s hard fallback: if the engine is up but the model still hasn't
      // resolved (very large/slow download), join degraded rather than never.
      if (coreReady) { console.warn("[meeting] model not ready after 90s — joining degraded"); return cb(); }
      console.warn("[meeting] STING_VIEWER / signalR never became ready");
      return;
    }
    setTimeout(function () { ready(cb, tries + 1); }, 100);
  }

  ready(start);

  function start() {
    buildPresenceUI();
    // Best-effort REST join so the hub's OwnsSessionAsync guard passes
    // (adds the participant row). Non-fatal if it 403s — the hub will
    // simply not add us to the group and we degrade to view-only.
    restJoin().finally(connect);
  }

  function restJoin() {
    if (!projectId) return Promise.resolve();
    var url = apiBase + "/api/projects/" + projectId + "/meeting-sessions/" + sessionId + "/join";
    return fetch(url, {
      method: "POST",
      headers: token ? { "Authorization": "Bearer " + token, "Content-Type": "application/json" } : { "Content-Type": "application/json" },
      body: JSON.stringify({ displayName: displayName }),
    }).catch(function () { /* non-fatal */ });
  }

  function connect() {
    var SR = window.signalR;
    var hubUrl = apiBase + "/hubs/meeting";
    var conn = new SR.HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: function () { return token; } })
      .withAutomaticReconnect()
      .build();
    state.conn = conn;

    conn.on("ParticipantJoined", function (p) {
      if (p && p.connectionId) state.participants.set(p.connectionId, { displayName: p.displayName || "Guest" });
      renderPresence();
      toast((p && p.displayName ? p.displayName : "Someone") + " joined");
    });
    conn.on("ParticipantLeft", function (p) {
      if (p && p.connectionId) state.participants.delete(p.connectionId);
      renderPresence();
    });
    conn.on("CameraMoved", function (msg) {
      if (!state.following) return;
      applyRemoteCamera(msg && msg.camera);
    });
    conn.on("OverlayChanged", function (profile) {
      // WS2 — a renderMode-tagged profile is the global x-ray/ghost + keep-solid
      // exclusion, not a colour overlay; route it to the coordination layer.
      if (profile && profile.source === "renderMode") {
        try { window.dispatchEvent(new CustomEvent("sting:remoteRenderMode", { detail: profile })); } catch (e) {}
        return;
      }
      try { window.STING_VIEWER.applyOverlay && window.STING_VIEWER.applyOverlay(profile); } catch (e) {}
    });
    conn.on("SectionChanged", function (msg) {
      applyRemoteSection(msg && msg.section);
      window.dispatchEvent(new CustomEvent("sting:remoteSection", { detail: msg && msg.section }));
    });
    conn.on("HighlightChanged", function (msg) {
      applyRemoteHighlight(msg && msg.guids);
      window.dispatchEvent(new CustomEvent("sting:remoteHighlight", { detail: msg && msg.guids }));
    });
    conn.on("RoomChanged", function (st) { window.dispatchEvent(new CustomEvent("sting:roomChanged", { detail: st })); });

    conn.onreconnected(function () { conn.invoke("JoinSession", sessionId, displayName).catch(noop); });

    conn.start()
      .then(function () { return conn.invoke("JoinSession", sessionId, displayName); })
      .then(function () { wireCameraBroadcast(); wireSelectionAndSection(); setStatus("live"); })
      .catch(function (e) { console.warn("[meeting] connect failed", e); setStatus("offline"); });

    window.addEventListener("beforeunload", function () {
      try { conn.invoke("LeaveSession", sessionId); } catch (e) {}
    });
  }

  // ── outbound: broadcast our camera when the user finishes a move ───────
  function wireCameraBroadcast() {
    var controls = window.STING_VIEWER.controls;
    if (!controls || !controls.addEventListener) return;
    var onMove = throttle(function () {
      if (state.applyingRemote) return;          // don't echo a remote move
      if (!state.conn) return;
      var cam = serializeCamera();
      if (!cam) return;
      state.conn.invoke("BroadcastCamera", sessionId, cam).catch(noop);
    }, 100);
    // OrbitControls fires 'end' on interaction finish, 'change' continuously.
    controls.addEventListener("end", onMove);
  }

  function serializeCamera() {
    var V = window.STING_VIEWER;
    var c = V.camera, ctl = V.controls;
    if (!c || !ctl) return null;
    return {
      p: c.position.toArray(),
      t: ctl.target.toArray(),
      q: c.quaternion ? c.quaternion.toArray() : null,
    };
  }

  function applyRemoteCamera(cam) {
    if (!cam) return;
    var V = window.STING_VIEWER;
    var c = V.camera, ctl = V.controls;
    if (!c || !ctl) return;
    state.applyingRemote = true;
    try {
      if (cam.p) c.position.fromArray(cam.p);
      if (cam.q && c.quaternion) c.quaternion.fromArray(cam.q);
      if (cam.t) ctl.target.fromArray(cam.t);
      ctl.update && ctl.update();
    } catch (e) { /* ignore */ }
    // Release the guard after the controls 'end'/'change' has flushed.
    setTimeout(function () { state.applyingRemote = false; }, 0);
  }

  // ── outbound: selection (highlight) + section plane ───────────────────
  // Non-invasive: we wrap the viewer's existing outbound event bus
  // (bridge.send) and the one section setter, rather than editing the
  // 4k-line coordination viewer. coordination-viewer.js also wraps
  // bridge.send; we chain on top of whatever's there.
  function wireSelectionAndSection() {
    var V = window.STING_VIEWER;
    try {
      if (V && V.bridge && typeof V.bridge.send === "function" && !V.bridge.__meetingWrapped) {
        var orig = V.bridge.send.bind(V.bridge);
        V.bridge.send = function (type, payload) {
          try {
            if (!state.applyingRemoteHi && state.conn && (type === "pick" || type === "pinTap")) {
              var guid = payload && (payload.guid || (payload.meta && payload.meta.guid));
              if (guid) state.conn.invoke("BroadcastHighlight", sessionId, [guid]).catch(noop);
            }
          } catch (e) {}
          return orig(type, payload);
        };
        V.bridge.__meetingWrapped = true;
      }
    } catch (e) {}
    try {
      var ext = window.STING_VIEWER_EXTRAS;
      if (ext && typeof ext.setSectionPlane === "function" && !ext.__meetingWrapped) {
        var origSec = ext.setSectionPlane.bind(ext);
        ext.setSectionPlane = function (plane) {
          var r = origSec(plane);
          try {
            if (!state.applyingRemoteSec && state.conn)
              state.conn.invoke("BroadcastSection", sessionId, { plane: plane }).catch(noop);
          } catch (e) {}
          return r;
        };
        ext.__meetingWrapped = true;
      }
    } catch (e) {}
  }

  function applyRemoteHighlight(guids) {
    state.applyingRemoteHi = true;
    try {
      if (guids && guids.length) postCmd({ type: "selectAndZoom", payload: { guid: guids[0] } });
      else postCmd({ type: "clearHighlight" });
    } catch (e) {}
    setTimeout(function () { state.applyingRemoteHi = false; }, 60);
  }

  function applyRemoteSection(section) {
    var ext = window.STING_VIEWER_EXTRAS;
    if (!ext || typeof ext.setSectionPlane !== "function") return;
    var plane = section && (section.plane || section);
    if (!plane) return;
    state.applyingRemoteSec = true;
    try { ext.setSectionPlane(plane); } catch (e) {}
    setTimeout(function () { state.applyingRemoteSec = false; }, 0);
  }

  // Drive the viewer engine via its window 'message' command channel
  // (viewer.html: window.addEventListener('message', e => handleCommand(JSON.parse(e.data)))).
  function postCmd(cmd) { try { window.postMessage(JSON.stringify(cmd), "*"); } catch (e) {} }

  // ── presence UI (small corner panel) ──────────────────────────────────
  function buildPresenceUI() {
    if (document.getElementById("meetingPanel")) return;
    var panel = document.createElement("div");
    panel.id = "meetingPanel";
    panel.style.cssText = [
      "position:absolute", "top:8px", "right:184px", "z-index:12",
      "background:rgba(0,0,0,0.66)", "color:#fff", "padding:8px 10px",
      "border-radius:6px", "font:12px -apple-system,Segoe UI,Roboto,sans-serif",
      "max-width:220px", "backdrop-filter:blur(4px)",
    ].join(";");
    panel.innerHTML =
      '<div style="display:flex;align-items:center;gap:6px;margin-bottom:6px">' +
        '<span id="meetingDot" style="width:8px;height:8px;border-radius:50%;background:#e8a13a"></span>' +
        '<strong>Live meeting</strong>' +
      '</div>' +
      '<label style="display:flex;align-items:center;gap:6px;cursor:pointer;margin-bottom:6px">' +
        '<input type="checkbox" id="meetingFollow" checked> Follow presenter' +
      '</label>' +
      '<div id="meetingParticipants" style="display:flex;flex-wrap:wrap;gap:4px"></div>';
    (document.getElementById("viewer-canvas") ? document.body : document.body).appendChild(panel);
    var f = document.getElementById("meetingFollow");
    if (f) f.addEventListener("change", function () { state.following = f.checked; });
    renderPresence();
  }

  function renderPresence() {
    var host = document.getElementById("meetingParticipants");
    if (!host) return;
    host.innerHTML = "";
    var me = document.createElement("span");
    me.textContent = displayName + " (you)";
    me.style.cssText = chipCss("#3a7de8");
    host.appendChild(me);
    state.participants.forEach(function (p) {
      var s = document.createElement("span");
      s.textContent = p.displayName;
      s.style.cssText = chipCss(colorFor(p.displayName));
      host.appendChild(s);
    });
  }

  function setStatus(s) {
    var dot = document.getElementById("meetingDot");
    if (dot) dot.style.background = s === "live" ? "#37c272" : (s === "offline" ? "#d05050" : "#e8a13a");
  }

  // ── small helpers ──────────────────────────────────────────────────────
  function chipCss(bg) {
    return "background:" + bg + ";padding:2px 7px;border-radius:10px;font-size:11px;white-space:nowrap";
  }
  function colorFor(name) {
    var h = 0; for (var i = 0; i < (name || "").length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0;
    return "hsl(" + (h % 360) + ",55%,42%)";
  }
  function throttle(fn, ms) {
    var last = 0, timer = null;
    return function () {
      var now = Date.now(), wait = ms - (now - last);
      if (wait <= 0) { last = now; fn(); }
      else if (!timer) { timer = setTimeout(function () { last = Date.now(); timer = null; fn(); }, wait); }
    };
  }
  function toast(msg) {
    var t = document.createElement("div");
    t.textContent = msg;
    t.style.cssText = "position:absolute;bottom:64px;left:50%;transform:translateX(-50%);z-index:13;" +
      "background:rgba(0,0,0,0.8);color:#fff;padding:6px 12px;border-radius:4px;font:12px sans-serif";
    document.body.appendChild(t);
    setTimeout(function () { t.remove(); }, 2500);
  }
  function noop() {}

  // Expose a tiny API for tests / future wiring.
  window.STING_MEETING = {
    get following() { return state.following; },
    set following(v) { state.following = !!v; var f = document.getElementById("meetingFollow"); if (f) f.checked = !!v; },
    serializeCamera: serializeCamera,
    applyRemoteCamera: applyRemoteCamera,
    applyRemoteHighlight: applyRemoteHighlight,
    applyRemoteSection: applyRemoteSection,
    broadcastHighlight: function (guids) { if (state.conn) state.conn.invoke("BroadcastHighlight", sessionId, guids || []).catch(noop); },
    broadcastSection: function (section) { if (state.conn) state.conn.invoke("BroadcastSection", sessionId, section || {}).catch(noop); },
    // WS2 — push an overlay profile (colour overlay OR a renderMode-tagged
    // x-ray/ghost + keep-solid exclusion) to the group via the existing channel.
    broadcastOverlay: function (profile) { if (state.conn) state.conn.invoke("BroadcastOverlay", sessionId, profile || {}).catch(noop); },
    sessionId: sessionId,
  };
})();
