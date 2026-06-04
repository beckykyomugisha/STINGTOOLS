/* livekit-av.js — WS3 web A/V for live meetings (LiveKit).
 *
 * The SignalR MeetingHub (meeting-sync.js) owns model co-presence; LiveKit owns
 * the media plane (camera / mic / screen-share). This file is the LiveKit half.
 *
 * Activation is OPT-IN via the URL, exactly like meeting-sync.js: it only runs
 * when the viewer is opened with ?meeting=<sessionId>. Without it the script is a
 * complete no-op. Inside a React Native WebView it stays inert (mobile gets native
 * LiveKit via @livekit/react-native — see Planscape/app/meetings).
 *
 *   /viewer.html?project=<pid>&model=<mid>&meeting=<sessionId>
 *
 * Pattern mirrors signalr-shim.js: load the livekit-client UMD bundle from a pinned
 * CDN at runtime (the viewer's esbuild build is for the three/coordination bundles;
 * third-party browser libs are CDN-pinned, no npm step needed here).
 *
 * Milestone (b): join → publish camera + mic → participant tile strip with
 * active-speaker highlight + mic/camera/leave toggles. Screen-share (c) and
 * active-surface switching (d) extend this file.
 */
(function () {
  "use strict";
  if (typeof window === "undefined") return;
  if (window.ReactNativeWebView) return;          // mobile owns its own LiveKit
  if (window.__STING_LIVEKIT__) return;           // no double-init
  window.__STING_LIVEKIT__ = true;

  var params = new URLSearchParams(location.search);
  var sessionId = params.get("meeting") || params.get("session") || "";
  var projectId = params.get("project") || "";
  if (!sessionId || !projectId) return;           // not a meeting — stay inert

  var LK_CDN = "https://cdn.jsdelivr.net/npm/livekit-client@2.7.5/dist/livekit-client.umd.min.js";
  var TOKEN_KEY = "planscape_token";
  var apiBase = (params.get("api")
    || (typeof localStorage !== "undefined" && localStorage.getItem("planscape_api_base"))
    || "").replace(/\/$/, "");
  var token = (function () { try { return localStorage.getItem(TOKEN_KEY) || ""; } catch (e) { return ""; } })();

  var state = {
    room: null,
    LK: null,
    micOn: true,
    camOn: true,
    screenOn: false,
    isPresenter: false,
    surface: "model",     // model | document | screen (WS3d)
    tiles: new Map(),     // participant.sid → { wrap, video, label }
  };

  // WS3d — apply the active surface broadcast by the presenter (via MeetingHub).
  window.addEventListener("sting:surfaceChanged", function (e) {
    var d = e.detail || {}; applySurface(d.surface, d.documentId);
  });
  // Presenter auto-follows their own screen-share: starting a share switches the
  // shared surface to 'screen' for everyone; stopping returns to 'model'.
  window.addEventListener("sting:screenShareStarted", function () { if (state.isPresenter) setSurface("screen"); });
  window.addEventListener("sting:screenShareStopped", function () { if (state.isPresenter && state.surface === "screen") setSurface("model"); });

  // ── boot: load livekit-client, fetch a token, connect ─────────────────────
  loadScript(LK_CDN, function (ok) {
    if (!ok || !window.LivekitClient) { console.warn("[livekit] client lib failed to load"); return; }
    state.LK = window.LivekitClient;
    fetchToken().then(function (info) {
      if (!info) return;                          // 501 (unconfigured) / error — silent
      state.isPresenter = !!info.isPresenter;     // needed by buildUI (screen button gate)
      buildUI();
      connect(info);
    });
  });

  function fetchToken() {
    var url = apiBase + "/api/projects/" + projectId + "/meeting-sessions/" + sessionId + "/livekit-token";
    return fetch(url, {
      method: "POST",
      headers: token
        ? { "Authorization": "Bearer " + token, "Content-Type": "application/json" }
        : { "Content-Type": "application/json" },
      body: JSON.stringify({}),
    }).then(function (r) {
      if (!r.ok) { if (r.status !== 501) console.warn("[livekit] token", r.status); return null; }
      return r.json();
    }).catch(function (e) { console.warn("[livekit] token fetch failed", e); return null; });
  }

  function connect(info) {
    var LK = state.LK;
    state.isPresenter = !!info.isPresenter;
    var room = new LK.Room({ adaptiveStream: true, dynacast: true });
    state.room = room;

    room
      .on(LK.RoomEvent.TrackSubscribed, function (track, pub, participant) { attachTrack(track, participant); })
      .on(LK.RoomEvent.TrackUnsubscribed, function (track) {
        try { track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
        if (isScreenShare(track)) clearScreen();
        renderTiles();
      })
      .on(LK.RoomEvent.LocalTrackUnpublished, function (pub) { if (pub && isScreenShare(pub.track)) { state.screenOn = false; clearScreen(); } })
      .on(LK.RoomEvent.ParticipantConnected, function () { renderTiles(); })
      .on(LK.RoomEvent.ParticipantDisconnected, function (p) { dropTile(p.sid); })
      .on(LK.RoomEvent.ActiveSpeakersChanged, function (speakers) { highlightSpeakers(speakers); })
      .on(LK.RoomEvent.LocalTrackPublished, function (pub) { if (pub.track) attachTrack(pub.track, room.localParticipant); })
      .on(LK.RoomEvent.Disconnected, function () { teardownUI(); });

    room.connect(info.url, info.token)
      .then(function () { return room.localParticipant.setMicrophoneEnabled(true); })
      .then(function () { return room.localParticipant.setCameraEnabled(true); })
      .then(function () { setStatus("live"); renderTiles(); })
      .catch(function (e) { console.warn("[livekit] connect failed", e); setStatus("offline"); });

    window.addEventListener("beforeunload", function () { try { room.disconnect(); } catch (e) {} });

    // Expose a tiny API (used by screen-share + active-surface milestones).
    window.STING_LIVEKIT = {
      room: room,
      toggleMic: toggleMic,
      toggleCam: toggleCam,
      toggleScreen: toggleScreen,
      get isPresenter() { return state.isPresenter; },
      get screenOn() { return state.screenOn; },
    };
  }

  // ── media tiles ───────────────────────────────────────────────────────────
  function isScreenShare(track) {
    try { return track && track.source === state.LK.Track.Source.ScreenShare; } catch (e) { return false; }
  }

  function attachTrack(track, participant) {
    if (!track) return;
    if (track.kind === "audio") {
      // Audio: attach a hidden element so it plays; not shown in the strip.
      var a = track.attach(); a.style.display = "none"; document.body.appendChild(a); return;
    }
    if (track.kind !== "video") return;
    // WS3c — a screen-share track goes to the big central pane, not a small tile.
    if (isScreenShare(track)) { attachScreen(track, participant); return; }
    var tile = ensureTile(participant);
    // Clear any previous video element, attach the new track.
    if (tile.video) { try { tile.video.remove(); } catch (e) {} }
    var v = track.attach();
    v.style.cssText = "width:100%;height:100%;object-fit:cover;border-radius:6px;background:#000";
    v.muted = participant.isLocal === true;     // never echo our own mic
    tile.wrap.insertBefore(v, tile.label);
    tile.video = v;
  }

  function ensureTile(participant) {
    var sid = participant.sid;
    if (state.tiles.has(sid)) return state.tiles.get(sid);
    var wrap = el("div", {
      style: "position:relative;width:132px;height:99px;border-radius:6px;overflow:hidden;" +
        "background:#11141a;border:2px solid transparent;flex:0 0 auto"
    });
    var label = el("div", {
      style: "position:absolute;left:4px;bottom:4px;right:4px;font:11px -apple-system,Segoe UI,sans-serif;" +
        "color:#fff;background:rgba(0,0,0,0.55);padding:1px 5px;border-radius:4px;" +
        "white-space:nowrap;overflow:hidden;text-overflow:ellipsis"
    }, (participant.identity && participant.name) || (participant.isLocal ? "You" : (participant.identity || "Guest")));
    wrap.appendChild(label);
    var strip = document.getElementById("lkStrip");
    if (strip) strip.appendChild(wrap);
    var tile = { wrap: wrap, video: null, label: label, sid: sid };
    state.tiles.set(sid, tile);
    return tile;
  }
  function dropTile(sid) {
    var t = state.tiles.get(sid);
    if (t) { try { t.wrap.remove(); } catch (e) {} state.tiles.delete(sid); }
  }
  function renderTiles() {
    var room = state.room; if (!room) return;
    ensureTile(room.localParticipant);
    room.remoteParticipants.forEach(function (p) {
      ensureTile(p);
      p.trackPublications.forEach(function (pub) { if (pub.track && pub.isSubscribed) attachTrack(pub.track, p); });
    });
  }
  function highlightSpeakers(speakers) {
    var active = {};
    speakers.forEach(function (p) { active[p.sid] = true; });
    state.tiles.forEach(function (t, sid) {
      t.wrap.style.borderColor = active[sid] ? "#37c272" : "transparent";
    });
  }

  // ── WS3c — screen-share: a presenter's screen renders in a big central pane ──
  function attachScreen(track, participant) {
    var pane = ensureScreenPane();
    if (pane._video) { try { pane._video.remove(); } catch (e) {} }
    var v = track.attach();
    v.style.cssText = "max-width:100%;max-height:100%;object-fit:contain;background:#000";
    pane.insertBefore(v, pane.firstChild);
    pane._video = v;
    // Visibility is driven by the active surface (applySurface), not by the track
    // arriving — so all clients show the screen at the same moment the presenter
    // switches the surface. If we're already on the screen surface, reveal it now.
    if (state.surface === "screen") pane.style.display = "flex";
    var who = (participant && (participant.name || participant.identity)) || "Presenter";
    var lbl = pane.querySelector(".lk-screen-label"); if (lbl) lbl.textContent = "🖥 " + who + " is sharing";
    // WS3d hook — when a screen-share starts, surface should follow (presenter
    // drives this through MeetingHub; followers just show the pane).
    try { window.dispatchEvent(new CustomEvent("sting:screenShareStarted", { detail: { identity: participant && participant.identity } })); } catch (e) {}
  }
  function ensureScreenPane() {
    var pane = document.getElementById("lkScreen");
    if (pane) return pane;
    pane = el("div", { id: "lkScreen", style:
      "position:absolute;inset:0 0 120px 0;z-index:13;display:none;align-items:center;justify-content:center;" +
      "background:rgba(8,10,14,0.92);padding:24px" });
    var label = el("div", { class: "lk-screen-label", style:
      "position:absolute;top:10px;left:12px;font:12px -apple-system,Segoe UI,sans-serif;color:#cfe3ff;" +
      "background:rgba(0,0,0,0.5);padding:2px 8px;border-radius:4px" }, "🖥 sharing");
    pane.appendChild(label);
    document.body.appendChild(pane);
    return pane;
  }
  function clearScreen() {
    var pane = document.getElementById("lkScreen");
    if (pane) { if (pane._video) { try { pane._video.remove(); } catch (e) {} pane._video = null; } pane.style.display = "none"; }
    try { window.dispatchEvent(new CustomEvent("sting:screenShareStopped")); } catch (e) {}
  }
  function toggleScreen() {
    if (!state.room) return;
    if (!state.isPresenter) { return; }   // grant gating is also enforced server-side
    state.screenOn = !state.screenOn;
    state.room.localParticipant.setScreenShareEnabled(state.screenOn)
      .then(function () { paintBtn("lkScreen2", state.screenOn, "🖥", "🖥"); var b = document.getElementById("lkScreen2"); if (b) b.style.background = state.screenOn ? "rgba(55,194,114,0.85)" : "rgba(255,255,255,0.14)"; })
      .catch(function (e) { state.screenOn = !state.screenOn; console.warn("[livekit] screen-share", e); });
  }

  // ── WS3d — active surface (model | document | screen) ──────────────────────
  // Presenter switches; the server persists + broadcasts SurfaceChanged; every
  // client (incl. presenter) applies it so all panes stay in lock-step.
  function setSurface(surface, docId) {
    if (!state.isPresenter) return;            // only the presenter drives the surface
    var url = apiBase + "/api/projects/" + projectId + "/meeting-sessions/" + sessionId + "/surface";
    fetch(url, {
      method: "POST",
      headers: token ? { "Authorization": "Bearer " + token, "Content-Type": "application/json" } : { "Content-Type": "application/json" },
      body: JSON.stringify({ surface: surface, documentId: docId || null }),
    }).catch(function (e) { console.warn("[livekit] surface", e); });
  }
  function applySurface(surface, docId) {
    surface = surface || "model";
    state.surface = surface;
    var screen = document.getElementById("lkScreen");
    var doc = document.getElementById("lkDoc");
    if (screen) screen.style.display = (surface === "screen") ? "flex" : "none";
    if (doc)    doc.style.display    = (surface === "document") ? "flex" : "none";
    if (surface === "document") showDocPane(docId);
    paintSurfaceSwitch(surface);
  }
  function showDocPane(docId) {
    var pane = ensureDocPane();
    if (!docId) { pane._frame.removeAttribute("src"); pane._msg.textContent = "No document selected"; pane._msg.style.display = "block"; return; }
    if (pane._docId === docId && pane._frame.getAttribute("src")) return;   // already showing
    pane._docId = docId; pane._msg.style.display = "none";
    // Fetch the doc file with the auth header, show it via a blob URL (PDF/image).
    var url = apiBase + "/api/projects/" + projectId + "/documents/" + docId + "/file";
    fetch(url, { headers: token ? { "Authorization": "Bearer " + token } : {} })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.blob(); })
      .then(function (b) { if (pane._url) URL.revokeObjectURL(pane._url); pane._url = URL.createObjectURL(b); pane._frame.src = pane._url; })
      .catch(function (e) { pane._msg.textContent = "Could not load document"; pane._msg.style.display = "block"; console.warn("[livekit] doc", e); });
  }
  function ensureDocPane() {
    var pane = document.getElementById("lkDoc");
    if (pane) return pane;
    pane = el("div", { id: "lkDoc", style:
      "position:absolute;inset:0 0 120px 0;z-index:13;display:none;flex-direction:column;background:#15171c;padding:8px" });
    var frame = el("iframe", { style: "flex:1;width:100%;border:none;border-radius:6px;background:#fff" });
    var msg = el("div", { style: "color:#9aa3b2;font:13px sans-serif;text-align:center;padding:20px;display:none" }, "");
    pane.appendChild(msg); pane.appendChild(frame);
    pane._frame = frame; pane._msg = msg;
    document.body.appendChild(pane);
    return pane;
  }
  function paintSurfaceSwitch(surface) {
    ["model", "document", "screen"].forEach(function (s) {
      var b = document.getElementById("lkSurf_" + s); if (!b) return;
      b.style.background = (s === surface) ? "rgba(59,130,246,0.85)" : "rgba(255,255,255,0.14)";
    });
  }

  // ── controls ───────────────────────────────────────────────────────────────
  function toggleMic() {
    if (!state.room) return;
    state.micOn = !state.micOn;
    state.room.localParticipant.setMicrophoneEnabled(state.micOn).catch(noop);
    paintBtn("lkMic", state.micOn, "🎤", "🔇");
  }
  function toggleCam() {
    if (!state.room) return;
    state.camOn = !state.camOn;
    state.room.localParticipant.setCameraEnabled(state.camOn).catch(noop);
    paintBtn("lkCam", state.camOn, "📹", "🚫");
  }
  function leave() {
    if (state.room) { try { state.room.disconnect(); } catch (e) {} }
    teardownUI();
  }

  // ── UI shell ────────────────────────────────────────────────────────────────
  function buildUI() {
    if (document.getElementById("lkBar")) return;
    var bar = el("div", { id: "lkBar", style:
      "position:absolute;bottom:12px;left:50%;transform:translateX(-50%);z-index:14;" +
      "display:flex;flex-direction:column;gap:8px;align-items:center;pointer-events:none" });
    var strip = el("div", { id: "lkStrip", style:
      "display:flex;gap:8px;max-width:90vw;overflow-x:auto;pointer-events:auto;" +
      "padding:4px;background:rgba(0,0,0,0.35);border-radius:8px;backdrop-filter:blur(4px)" });
    var ctrls = el("div", { style:
      "display:flex;gap:8px;pointer-events:auto;background:rgba(0,0,0,0.6);" +
      "padding:6px 10px;border-radius:24px;backdrop-filter:blur(4px)" });
    ctrls.appendChild(ctrlBtn("lkMic", "🎤", toggleMic, "Mute / unmute mic"));
    ctrls.appendChild(ctrlBtn("lkCam", "📹", toggleCam, "Camera on / off"));
    // WS3c — screen-share is presenter/host only (also enforced by the LiveKit grant).
    if (state.isPresenter) ctrls.appendChild(ctrlBtn("lkScreen2", "🖥", toggleScreen, "Share / stop sharing screen"));
    // WS3d — presenter switches the shared surface everyone sees. 'screen' engages
    // automatically with the screen-share toggle above.
    if (state.isPresenter) {
      ctrls.appendChild(ctrlBtn("lkSurf_model", "🧊", function () { setSurface("model"); }, "Show the 3D model"));
      ctrls.appendChild(ctrlBtn("lkSurf_document", "📄", function () {
        var d = prompt("Document id to share with the room:"); if (d && d.trim()) setSurface("document", d.trim());
      }, "Share a document"));
      // hidden anchor so paintSurfaceSwitch can highlight the 'screen' surface too
      ctrls.appendChild(el("span", { id: "lkSurf_screen", style: "display:none" }));
    }
    var dot = el("span", { id: "lkDot", style:
      "width:8px;height:8px;border-radius:50%;background:#e8a13a;align-self:center;margin:0 2px" });
    ctrls.appendChild(dot);
    ctrls.appendChild(ctrlBtn("lkLeave", "✖", leave, "Leave A/V", "#d05050"));
    bar.appendChild(strip);
    bar.appendChild(ctrls);
    document.body.appendChild(bar);
  }
  function teardownUI() {
    var bar = document.getElementById("lkBar"); if (bar) bar.remove();
    state.tiles.clear();
  }
  function ctrlBtn(id, glyph, fn, title, bg) {
    var b = el("button", { id: id, title: title || "", style:
      "width:38px;height:38px;border:none;border-radius:50%;cursor:pointer;font-size:16px;" +
      "background:" + (bg || "rgba(255,255,255,0.14)") + ";color:#fff" }, glyph);
    b.addEventListener("click", fn);
    return b;
  }
  function paintBtn(id, on, onGlyph, offGlyph) {
    var b = document.getElementById(id); if (!b) return;
    b.textContent = on ? onGlyph : offGlyph;
    b.style.background = on ? "rgba(255,255,255,0.14)" : "rgba(208,80,80,0.85)";
  }
  function setStatus(s) {
    var dot = document.getElementById("lkDot");
    if (dot) dot.style.background = s === "live" ? "#37c272" : (s === "offline" ? "#d05050" : "#e8a13a");
  }

  // ── helpers ──────────────────────────────────────────────────────────────
  function el(tag, attrs, text) {
    var e = document.createElement(tag);
    if (attrs) for (var k in attrs) { if (k === "style") e.style.cssText = attrs[k]; else e.setAttribute(k, attrs[k]); }
    if (text != null) e.textContent = text;
    return e;
  }
  function loadScript(src, cb) {
    var s = document.createElement("script");
    s.src = src; s.async = true;
    s.onload = function () { cb(true); };
    s.onerror = function () { cb(false); };
    document.head.appendChild(s);
  }
  function noop() {}
})();
