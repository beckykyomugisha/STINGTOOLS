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

  // STEP-0 SERVED marker — bumped per slice that touches this file.
  var STING_MEETINGSYNC_BUILD = "N1-presence";
  try { console.log("[meeting] STING_MEETINGSYNC_BUILD " + STING_MEETINGSYNC_BUILD); } catch (e) {}

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
    participants: new Map(), // connectionId → { displayName, userId, hand }
    // M3 — conferencing
    myUserId: "",            // decoded from the JWT (for host comparison)
    myConnId: "",            // our own SignalR connection id (set after start)
    hostUserId: "",          // session host's user id (from room state / RoomChanged)
    myHand: false,
    // M4 — AEC functions
    lastPickGuid: "",        // last picked element (for issue link + viewpoint)
    meetingId: "",           // linked formal Meeting (agenda/actions/minutes)
    modelId: "",             // session model (informational)
    clash: { list: [], idx: -1, on: false },
    // N1 — live A/V state from livekit-av.js, keyed by participant identity (= userId).
    av: {},
  };
  state.myUserId = decodeUserId(token);

  // N1 — the media plane (livekit-av.js) publishes camera/mic/in-call state; merge it
  // into the co-presence roster so it shows WHO IS ONLINE + their live A/V status.
  window.addEventListener("sting:avState", function (e) { state.av = (e && e.detail) || {}; renderPresence(); });

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

  // V3 — long-session auth (same as signalr-shim): current token from localStorage,
  // refreshed via /api/auth/refresh when the JWT is near expiry, so a long meeting
  // doesn't 401 + storm negotiate. STING_VIZ_SIGNALR_REFRESH.
  function currentToken() { try { return localStorage.getItem(TOKEN_KEY) || token; } catch (e) { return token; } }
  function isExpiringSoon(t) {
    try { var p = JSON.parse(atob(String(t).split(".")[1].replace(/-/g, "+").replace(/_/g, "/"))); return !p.exp || (p.exp * 1000 - Date.now() < 60000); } catch (e) { return false; }
  }
  var _refreshing = null;
  function refreshAccessToken() {
    if (_refreshing) return _refreshing;
    var rt = (function () { try { return localStorage.getItem("planscape_refresh"); } catch (e) { return null; } })();
    if (!rt) return Promise.resolve(null);
    _refreshing = fetch(apiBase + "/api/auth/refresh", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ refreshToken: rt }) })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (b) { if (b && b.accessToken) { try { localStorage.setItem(TOKEN_KEY, b.accessToken); if (b.refreshToken) localStorage.setItem("planscape_refresh", b.refreshToken); } catch (e) {} return b.accessToken; } return null; })
      .catch(function () { return null; })
      .then(function (v) { _refreshing = null; return v; });
    return _refreshing;
  }
  function tokenFactory() {
    var t = currentToken();
    if (!isExpiringSoon(t)) return t || "";
    return refreshAccessToken().then(function (fresh) { return fresh || t || ""; });
  }
  function connect() {
    var SR = window.signalR;
    var hubUrl = apiBase + "/hubs/meeting";
    var conn = new SR.HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: tokenFactory })
      .withAutomaticReconnect()
      .build();
    state.conn = conn;

    conn.on("ParticipantJoined", function (p) {
      if (p && p.connectionId) state.participants.set(p.connectionId, { displayName: p.displayName || "Guest", userId: (p.userId || ""), hand: false });
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
      // C5 — a full visualize snapshot (scheme + modes + custom colours + transparency).
      if (profile && profile.source === "appearance") {
        try { window.dispatchEvent(new CustomEvent("sting:remoteAppearance", { detail: profile })); } catch (e) {}
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
    conn.on("RoomChanged", function (st) {
      if (st && st.hostUserId) { state.hostUserId = String(st.hostUserId); renderPresence(); }
      if (st && st.meetingId) { state.meetingId = String(st.meetingId); renderAecMeeting(); }
      if (st && st.modelId) state.modelId = String(st.modelId);
      window.dispatchEvent(new CustomEvent("sting:roomChanged", { detail: st }));
    });
    // WS3d — presenter switched the active surface (model | document | screen);
    // livekit-av.js applies it so every client shows the same pane.
    conn.on("SurfaceChanged", function (s) { window.dispatchEvent(new CustomEvent("sting:surfaceChanged", { detail: s })); });
    // M2 — a markup op on the shared DOCUMENT surface (add stroke / clear / grant);
    // livekit-av.js renders it on the markup canvas so everyone sees it live.
    conn.on("DocMarkupChanged", function (m) { window.dispatchEvent(new CustomEvent("sting:docMarkupChanged", { detail: m })); });

    // ── M3 — conferencing essentials (chat / reactions / hand / moderation) ──
    conn.on("ChatReceived", function (m) { addChatLine((m && m.from) || "Guest", (m && m.text) || "", false); });
    conn.on("ReactionReceived", function (r) { floatReaction((r && r.emoji) || "👍", (r && r.from) || ""); });
    conn.on("HandChanged", function (h) {
      if (!h || !h.connectionId) return;
      var p = state.participants.get(h.connectionId);
      if (p) { p.hand = !!h.raised; renderPresence(); }
      if (h.raised) toast(((p && p.displayName) || "Someone") + " raised their hand ✋");
    });
    conn.on("Moderation", function (m) {
      if (!m || !m.action) return;
      if (m.action === "mute-all") { window.dispatchEvent(new CustomEvent("sting:selfMute")); toast("Host muted everyone 🔇"); }
      else if (m.action === "remove") {
        if (m.connectionId && m.connectionId === state.myConnId) {
          toast("You were removed from the meeting");
          window.dispatchEvent(new CustomEvent("sting:removed"));
          try { conn.invoke("LeaveSession", sessionId); } catch (e) {}
          try { conn.stop(); } catch (e) {}
        } else if (m.connectionId) {
          state.participants.delete(m.connectionId); renderPresence();
        }
      }
    });

    conn.onreconnected(function () { state.myConnId = conn.connectionId || state.myConnId; conn.invoke("JoinSession", sessionId, displayName).catch(noop); });

    conn.start()
      .then(function () { state.myConnId = conn.connectionId || ""; return conn.invoke("JoinSession", sessionId, displayName); })
      .then(function () { wireCameraBroadcast(); wireSelectionAndSection(); buildConferenceUI(); buildAecUI(); fetchRoomState(); setStatus("live"); })
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
            if (state.conn && (type === "pick" || type === "pinTap")) {
              var guid = payload && (payload.guid || (payload.meta && payload.meta.guid));
              if (guid) {
                state.lastPickGuid = guid;   // M4 — remember selection for issue/viewpoint
                if (!state.applyingRemoteHi) state.conn.invoke("BroadcastHighlight", sessionId, [guid]).catch(noop);
              }
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
      '<div id="meetingCounts" style="font-size:10px;opacity:0.78;margin-bottom:6px"></div>' +
      '<label style="display:flex;align-items:center;gap:6px;cursor:pointer;margin-bottom:6px">' +
        '<input type="checkbox" id="meetingFollow" checked> Follow presenter' +
      '</label>' +
      '<div id="meetingParticipants" style="display:flex;flex-wrap:wrap;gap:4px"></div>';
    (document.getElementById("viewer-canvas") ? document.body : document.body).appendChild(panel);
    var f = document.getElementById("meetingFollow");
    if (f) f.addEventListener("change", function () { state.following = f.checked; });
    renderPresence();
  }

  // M3 — roster with roles (★ host · "(you)") + ✋ hand + host controls.
  function renderPresence() {
    var host = document.getElementById("meetingParticipants");
    if (!host) return;
    host.innerHTML = "";
    var meHost = isHost() ? " ★" : "";
    host.appendChild(rosterChip(displayName + " (you)" + meHost + (state.myHand ? " ✋" : "") + avSuffix(state.myUserId), "#3a7de8"));
    state.participants.forEach(function (p, cid) {
      var badge = (p.userId && String(p.userId) === state.hostUserId) ? " ★" : "";
      var wrap = document.createElement("span");
      wrap.style.cssText = "display:inline-flex;align-items:center;gap:3px";
      wrap.appendChild(rosterChip(p.displayName + badge + (p.hand ? " ✋" : "") + avSuffix(String(p.userId)), colorFor(p.displayName)));
      if (isHost()) {  // host controls: make-host (★) + remove (✖)
        wrap.appendChild(miniBtn("★", "Make host", function () { makeHost(p.userId); }));
        wrap.appendChild(miniBtn("✖", "Remove", function () { removeParticipant(cid); }));
      }
      host.appendChild(wrap);
    });
    // N1 — "N online · M in call" summary.
    var counts = document.getElementById("meetingCounts");
    if (counts) counts.textContent = (state.participants.size + 1) + " online · " + inCallCount() + " in call";
    refreshHostControls();
  }
  // N1 — A/V status suffix for a roster chip, from the live media-plane state.
  //   in call: 📹 (cam on) + 🎤/🔇 (mic) + 🔊 (active speaker);  online only: 🕓 (away from A/V).
  function avSuffix(uid) {
    var a = uid && state.av ? state.av[uid] : null;
    if (!a || !a.present) return " 🕓";
    return (a.cam ? " 📹" : "") + (a.mic ? " 🎤" : " 🔇") + (a.speaking ? " 🔊" : "");
  }
  function inCallCount() {
    var n = 0; for (var k in (state.av || {})) { if (state.av[k] && state.av[k].present) n++; }
    return n;
  }
  function rosterChip(text, bg) {
    var s = document.createElement("span");
    s.textContent = text; s.style.cssText = chipCss(bg);
    return s;
  }
  function miniBtn(g, title, fn) {
    var b = document.createElement("button");
    b.textContent = g; b.title = title;
    b.style.cssText = "border:none;border-radius:4px;cursor:pointer;font-size:10px;padding:1px 4px;background:rgba(255,255,255,0.18);color:#fff";
    b.addEventListener("click", fn); return b;
  }
  function isHost() { return !!state.myUserId && state.myUserId === state.hostUserId; }
  function decodeUserId(t) {
    try { var p = JSON.parse(atob(String(t).split(".")[1].replace(/-/g, "+").replace(/_/g, "/"))); return String(p.sub || p.user_id || ""); }
    catch (e) { return ""; }
  }
  function fetchRoomState() {
    if (!projectId) return;
    var t = currentToken();
    fetch(apiBase + "/api/projects/" + projectId + "/meeting-sessions/" + sessionId, { headers: t ? { "Authorization": "Bearer " + t } : {} })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (s) {
        if (!s) return;
        if (s.hostUserId) state.hostUserId = String(s.hostUserId);
        if (s.meetingId) state.meetingId = String(s.meetingId);
        if (s.modelId) state.modelId = String(s.modelId);
        renderPresence(); renderAecMeeting();
      })
      .catch(noop);
  }
  function jsonHeadersMS() { var t = currentToken(); return t ? { "Authorization": "Bearer " + t, "Content-Type": "application/json" } : { "Content-Type": "application/json" }; }

  // ── M3 — chat / reactions / raise-hand / moderation senders + UI ───────────
  function sendChat(text) {
    text = (text || "").trim(); if (!text) return;
    addChatLine(displayName, text, true);
    if (state.conn) state.conn.invoke("BroadcastChat", sessionId, { from: displayName, text: text, ts: Date.now() }).catch(noop);
  }
  function react(emoji) {
    floatReaction(emoji);
    if (state.conn) state.conn.invoke("BroadcastReaction", sessionId, { emoji: emoji, from: displayName }).catch(noop);
  }
  function raiseHand() {
    state.myHand = !state.myHand;
    if (state.conn) state.conn.invoke("BroadcastHand", sessionId, state.myHand).catch(noop);
    var b = document.getElementById("meetHand"); if (b) b.style.background = state.myHand ? "rgba(244,180,0,0.9)" : "rgba(255,255,255,0.14)";
    renderPresence();
  }
  function muteAll() { if (state.conn) state.conn.invoke("MuteAll", sessionId).catch(noop); toast("Asked everyone to mute"); }
  function makeHost(userId) {
    if (!userId) return;
    fetch(apiBase + "/api/projects/" + projectId + "/meeting-sessions/" + sessionId + "/host",
      { method: "POST", headers: jsonHeadersMS(), body: JSON.stringify({ userId: String(userId) }) })
      .then(function (r) { if (r.ok) toast("Host changed"); else toast("Make-host failed"); }).catch(noop);
  }
  function removeParticipant(cid) { if (state.conn && cid) state.conn.invoke("RemoveParticipant", sessionId, cid).catch(noop); }

  function buildConferenceUI() {
    if (document.getElementById("meetTools")) return;
    var panel = document.getElementById("meetingPanel"); if (!panel) return;
    var tools = document.createElement("div");
    tools.id = "meetTools";
    tools.style.cssText = "display:flex;flex-wrap:wrap;gap:4px;margin-top:6px";
    tools.appendChild(toolBtn("meetHand", "✋", "Raise / lower hand", raiseHand));
    ["👍", "👏", "❤️", "😂"].forEach(function (e) { tools.appendChild(toolBtn(null, e, "Send reaction", function () { react(e); })); });
    tools.appendChild(toolBtn("meetChatBtn", "💬", "Toggle chat", toggleChat));
    var mute = toolBtn("meetMuteAll", "🔇", "Mute everyone (host)", muteAll);
    mute.setAttribute("data-host", "1");
    tools.appendChild(mute);
    panel.appendChild(tools);

    var chat = document.createElement("div");
    chat.id = "meetChat";
    chat.style.cssText = "display:none;flex-direction:column;gap:4px;margin-top:6px";
    var log = document.createElement("div");
    log.id = "meetChatLog";
    log.style.cssText = "max-height:150px;overflow-y:auto;display:flex;flex-direction:column;gap:3px;font-size:11px";
    var inputRow = document.createElement("div");
    inputRow.style.cssText = "display:flex;gap:4px";
    var input = document.createElement("input");
    input.id = "meetChatInput"; input.placeholder = "Message…";
    input.style.cssText = "flex:1;min-width:0;border:none;border-radius:4px;padding:3px 6px;font-size:11px";
    input.addEventListener("keydown", function (e) { if (e.key === "Enter") { sendChat(input.value); input.value = ""; } });
    inputRow.appendChild(input);
    inputRow.appendChild(toolBtn(null, "➤", "Send", function () { sendChat(input.value); input.value = ""; }));
    chat.appendChild(log); chat.appendChild(inputRow);
    panel.appendChild(chat);
    refreshHostControls();
  }
  function toolBtn(id, glyph, title, fn) {
    var b = document.createElement("button");
    if (id) b.id = id; b.textContent = glyph; b.title = title;
    b.style.cssText = "border:none;border-radius:6px;cursor:pointer;font-size:13px;padding:3px 7px;background:rgba(255,255,255,0.14);color:#fff";
    b.addEventListener("click", fn); return b;
  }
  function toggleChat() {
    var c = document.getElementById("meetChat"); if (!c) return;
    var open = c.style.display === "none";
    c.style.display = open ? "flex" : "none";
    if (open) { var b = document.getElementById("meetChatBtn"); if (b) b.style.background = "rgba(255,255,255,0.14)"; var i = document.getElementById("meetChatInput"); if (i) i.focus(); }
  }
  function addChatLine(from, text, mine) {
    var log = document.getElementById("meetChatLog"); if (!log) return;
    var line = document.createElement("div");
    var who = document.createElement("strong");
    who.textContent = (mine ? "You" : from) + ": "; who.style.color = mine ? "#7fb2ff" : "#9fe0b0";
    line.appendChild(who); line.appendChild(document.createTextNode(text));
    log.appendChild(line); log.scrollTop = log.scrollHeight;
    var c = document.getElementById("meetChat");
    if (c && c.style.display === "none" && !mine) { var b = document.getElementById("meetChatBtn"); if (b) b.style.background = "rgba(55,194,114,0.85)"; }
  }
  var _rx = 17;
  function floatReaction(emoji) {
    _rx = (_rx + 41) % 100;
    var f = document.createElement("div");
    f.textContent = emoji;
    f.style.cssText = "position:absolute;bottom:120px;left:" + (38 + (_rx % 28)) + "%;z-index:14;font-size:30px;" +
      "pointer-events:none;transition:transform 2s linear,opacity 2s linear;opacity:1";
    document.body.appendChild(f);
    requestAnimationFrame(function () { f.style.transform = "translateY(-170px)"; f.style.opacity = "0"; });
    setTimeout(function () { f.remove(); }, 2100);
  }
  function refreshHostControls() {
    var show = isHost();
    var list = document.querySelectorAll('#meetTools [data-host]');
    for (var i = 0; i < list.length; i++) list[i].style.display = show ? "inline-block" : "none";
  }

  // ── M4 — AEC functions (issue / clash review / meeting link / viewpoint) ────
  function buildAecUI() {
    if (document.getElementById("meetAec")) return;
    var panel = document.getElementById("meetingPanel"); if (!panel) return;
    var row = document.createElement("div");
    row.id = "meetAec";
    row.style.cssText = "display:flex;flex-wrap:wrap;gap:4px;margin-top:6px;border-top:1px solid rgba(255,255,255,0.15);padding-top:6px";
    row.appendChild(toolBtn(null, "⚑", "Raise an issue (links current selection + viewpoint)", newIssue));
    row.appendChild(toolBtn("meetClashBtn", "⧉", "Clash review (step + camera-follow)", toggleClashReview));
    row.appendChild(toolBtn(null, "📸", "Capture viewpoint snapshot", function () { captureViewpoint(""); }));
    row.appendChild(toolBtn("meetMtgBtn", "📋", "Link / minutes (formal meeting)", meetingMenu));
    panel.appendChild(row);

    var cp = document.createElement("div");
    cp.id = "meetClashPanel";
    cp.style.cssText = "display:none;flex-direction:column;gap:4px;margin-top:6px;font-size:11px";
    var info = document.createElement("div"); info.id = "meetClashInfo"; info.textContent = "Clash review";
    var nav = document.createElement("div"); nav.style.cssText = "display:flex;gap:4px";
    nav.appendChild(toolBtn(null, "◀", "Previous clash", function () { stepClash(-1); }));
    nav.appendChild(toolBtn(null, "▶", "Next clash", function () { stepClash(1); }));
    nav.appendChild(toolBtn(null, "⚑→", "Promote this clash to an issue", promoteClash));
    cp.appendChild(info); cp.appendChild(nav);
    panel.appendChild(cp);
    renderAecMeeting();
  }
  function renderAecMeeting() {
    var b = document.getElementById("meetMtgBtn"); if (!b) return;
    b.style.background = state.meetingId ? "rgba(55,194,114,0.85)" : "rgba(255,255,255,0.14)";
    b.title = state.meetingId ? "Meeting linked — add action / generate minutes" : "Link / create a formal meeting record";
  }
  // markup/discussion → Issue, linking the current model selection + a viewpoint.
  function newIssue() {
    var title = prompt("Issue title:", "Issue from meeting"); if (!title) return;
    var assignee = prompt("Assignee email (optional):", "");
    var body = { Type: "OBS", Title: title, Priority: "MEDIUM",
      Description: "Raised in live meeting " + sessionId + (state.lastPickGuid ? "\nElement: " + state.lastPickGuid : "") };
    if (assignee && assignee.trim()) body.AssigneeEmail = assignee.trim();
    if (state.lastPickGuid) body.ModelElementGuid = state.lastPickGuid;   // no ModelId — avoids ProjectModel validation
    fetch(apiBase + "/api/projects/" + projectId + "/issues", { method: "POST", headers: jsonHeadersMS(), body: JSON.stringify(body) })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (issue) {
        if (issue && (issue.issueCode || issue.id)) { toast("Issue " + (issue.issueCode || "") + " created"); captureViewpoint("Issue " + (issue.issueCode || "")); }
        else toast("Issue create failed");
      }).catch(function () { toast("Issue create failed"); });
  }
  // Viewpoint = camera + highlighted element → a replayable MeetingSnapshot.
  function captureViewpoint(label) {
    var cam = null; try { cam = serializeCamera(); } catch (e) {}
    var body = { Label: label || ("Viewpoint " + new Date().toLocaleTimeString()),
      StateJson: JSON.stringify({ surface: "model", camera: cam, highlights: state.lastPickGuid ? [state.lastPickGuid] : [] }) };
    fetch(apiBase + "/api/projects/" + projectId + "/meeting-sessions/" + sessionId + "/snapshots", { method: "POST", headers: jsonHeadersMS(), body: JSON.stringify(body) })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (s) { if (s) toast("Viewpoint saved"); else toast("Viewpoint failed"); }).catch(noop);
  }
  // Clash review — fetch project clashes, step through them; selectAndZoom moves
  // the presenter's camera which the existing camera-follow mirrors to followers.
  function toggleClashReview() {
    state.clash.on = !state.clash.on;
    var cp = document.getElementById("meetClashPanel");
    var b = document.getElementById("meetClashBtn");
    if (b) b.style.background = state.clash.on ? "rgba(59,130,246,0.85)" : "rgba(255,255,255,0.14)";
    if (cp) cp.style.display = state.clash.on ? "flex" : "none";
    if (state.clash.on && !state.clash.list.length) loadClashes();
  }
  function loadClashes() {
    fetch(apiBase + "/api/projects/" + projectId + "/clashes?pageSize=100", { headers: jsonHeadersMS() })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) {
        state.clash.list = (d && d.items) || [];
        state.clash.idx = state.clash.list.length ? 0 : -1;
        renderClash();
        if (state.clash.idx >= 0) focusClash(state.clash.list[0]);
      }).catch(function () { toast("Could not load clashes"); });
  }
  function stepClash(delta) {
    if (!state.clash.list.length) return;
    state.clash.idx = (state.clash.idx + delta + state.clash.list.length) % state.clash.list.length;
    renderClash(); focusClash(state.clash.list[state.clash.idx]);
  }
  function focusClash(c) {
    var guid = c && (c.elementAGuid || c.ElementAGuid);
    if (!guid) return;
    state.lastPickGuid = guid;
    postCmd({ type: "selectAndZoom", payload: { guid: guid } });
    if (state.conn) state.conn.invoke("BroadcastHighlight", sessionId, [guid]).catch(noop);
  }
  function promoteClash() {
    var c = state.clash.list[state.clash.idx]; if (!c) return;
    var cid = c.id || c.Id; if (!cid) return;
    fetch(apiBase + "/api/projects/" + projectId + "/clashes/" + cid + "/promote-to-issue", { method: "POST", headers: jsonHeadersMS() })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (x) { toast(x ? ("Clash → " + (x.issueCode || "issue")) : "Promote failed (already an issue?)"); })
      .catch(function () { toast("Promote failed"); });
  }
  function renderClash() {
    var info = document.getElementById("meetClashInfo"); if (!info) return;
    if (!state.clash.list.length) { info.textContent = "No clashes found"; return; }
    var c = state.clash.list[state.clash.idx] || {};
    info.textContent = "Clash " + (state.clash.idx + 1) + "/" + state.clash.list.length +
      " — " + (c.disciplineA || "?") + "↔" + (c.disciplineB || "?") + " (" + (c.severity || "") + ")";
  }
  // Formal-meeting link → action items + minutes.
  function meetingMenu() {
    if (!state.meetingId) { linkOrCreateMeeting(); return; }
    var t = prompt("Add a decision/action (leave blank to GENERATE MINUTES):", "");
    if (t === null) return;
    if (t.trim() === "") generateMinutes(); else addDecision(t.trim());
  }
  function linkOrCreateMeeting() {
    var title = prompt("Create a formal meeting record — title:", "Coordination meeting"); if (!title) return;
    fetch(apiBase + "/api/projects/" + projectId + "/meetings", { method: "POST", headers: jsonHeadersMS(),
      body: JSON.stringify({ Title: title, MeetingType: "BIM Coordination", ScheduledAt: new Date().toISOString() }) })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (m) {
        var mid = m && (m.id || m.Id); if (!mid) { toast("Create meeting failed"); return; }
        return fetch(apiBase + "/api/projects/" + projectId + "/meeting-sessions/" + sessionId + "/link-meeting",
          { method: "POST", headers: jsonHeadersMS(), body: JSON.stringify({ meetingId: mid }) })
          .then(function (r) { if (r.ok) { state.meetingId = String(mid); toast("Meeting created + linked"); renderAecMeeting(); } else toast("Link failed"); });
      }).catch(function () { toast("Meeting link failed"); });
  }
  function addDecision(text) {
    if (!state.meetingId) { toast("Link a meeting first"); return; }
    fetch(apiBase + "/api/projects/" + projectId + "/meetings/" + state.meetingId + "/actions",
      { method: "POST", headers: jsonHeadersMS(), body: JSON.stringify({ Description: text }) })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (x) { toast(x ? "Action added" : "Add action failed"); }).catch(function () { toast("Add action failed"); });
  }
  function generateMinutes() {
    if (!state.meetingId) { toast("Link a meeting first"); return; }
    fetch(apiBase + "/api/projects/" + projectId + "/meetings/" + state.meetingId + "/export/minutes", { method: "POST", headers: jsonHeadersMS() })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (x) { toast(x ? "Minutes generated" : "Minutes failed"); }).catch(function () { toast("Minutes failed"); });
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
    // M2 — push one markup op (add stroke / clear / grant) to the other
    // participants over MeetingHub. livekit-av.js calls this from the markup canvas.
    broadcastDocMarkup: function (markup) { if (state.conn) state.conn.invoke("BroadcastDocMarkup", sessionId, markup || {}).catch(noop); },
    // M3 — conferencing
    sendChat: sendChat,
    react: react,
    raiseHand: raiseHand,
    muteAll: muteAll,
    // M4 — AEC
    newIssue: newIssue,
    captureViewpoint: captureViewpoint,
    toggleClashReview: toggleClashReview,
    get isHost() { return isHost(); },
    get connected() { return !!state.conn; },
    sessionId: sessionId,
  };
})();
