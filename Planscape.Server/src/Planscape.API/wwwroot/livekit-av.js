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

  // STEP-0 SERVED marker — bumped per slice so a marker grep on the *served*
  // bundle proves the running container has this exact change.
  var STING_MEETING_BUILD = "N4-layout";
  try { console.log("[livekit] STING_MEETING_BUILD " + STING_MEETING_BUILD); } catch (e) {}

  var params = new URLSearchParams(location.search);
  var sessionId = params.get("meeting") || params.get("session") || "";
  var projectId = params.get("project") || "";
  if (!sessionId || !projectId) return;           // not a meeting — stay inert

  // P3 — vendored locally (offline; no third-party CDN / SRI exposure). Pinned to
  // livekit-client 2.7.5 UMD; refresh vendor/livekit-client.umd.min.js to bump.
  var LK_CDN = "./vendor/livekit-client.umd.min.js";
  var TOKEN_KEY = "planscape_token";
  var apiBase = (params.get("api")
    || (typeof localStorage !== "undefined" && localStorage.getItem("planscape_api_base"))
    || "").replace(/\/$/, "");
  var token = (function () { try { return localStorage.getItem(TOKEN_KEY) || ""; } catch (e) { return ""; } })();

  var state = {
    room: null,
    LK: null,
    joined: false,        // M1 — gesture-gated: true only after the user clicks Join
    tokenInfo: null,      // M1 — fetched at boot, consumed by join()
    micOn: true,
    camOn: true,
    screenOn: false,
    isPresenter: false,
    surface: "model",     // model | document | screen (WS3d)
    tiles: new Map(),     // participant.sid → { wrap, video, label }
    // M3 — media-plane conferencing
    lowBw: false,         // audio-only (no camera video) for 3G / field
    viewMode: "gallery",  // gallery | speaker
    pinnedSid: null,      // a pinned participant (overrides active-speaker focus)
    activeSid: null,      // current active speaker (speaker-view focus)
  };

  // M2 — collaborative document markup. Strokes are stored with NORMALISED
  // coords (0..1 over the doc pane) so they line up across clients of any size.
  // The wire is MeetingHub.BroadcastDocMarkup (co-presence), never LiveKit.
  var MARKUP = {
    strokes: [],          // [{ id, tool, color, w, pts:[[x,y]…], text, by }]
    tool: "pen",          // pen | arrow | text | rect | highlight
    color: "#e8413a",
    draw: false,          // local drawing mode (canvas captures pointer events)
    granted: false,       // non-presenter allowed to draw (host granted)
    cur: null,            // in-progress stroke
  };
  function markupAllowed() { return state.isPresenter || MARKUP.granted; }

  // WS3d — apply the active surface broadcast by the presenter (via MeetingHub).
  window.addEventListener("sting:surfaceChanged", function (e) {
    var d = e.detail || {}; applySurface(d.surface, d.documentId);
  });
  // Presenter auto-follows their own screen-share: starting a share switches the
  // shared surface to 'screen' for everyone; stopping returns to 'model'.
  window.addEventListener("sting:screenShareStarted", function () { if (state.isPresenter) setSurface("screen"); });
  window.addEventListener("sting:screenShareStopped", function () { if (state.isPresenter && state.surface === "screen") setSurface("model"); });
  // M2 — a markup op (add stroke / clear / grant) arrived from a participant.
  window.addEventListener("sting:docMarkupChanged", function (e) { onRemoteMarkup(e.detail || {}); });
  // M3 — host moderation: self-mute on "mute all", leave on "removed".
  window.addEventListener("sting:selfMute", function () {
    if (state.room && state.micOn) { state.micOn = false; state.room.localParticipant.setMicrophoneEnabled(false).catch(noop); paintBtn("lkMic", false, "🎤", "🔇"); }
  });
  window.addEventListener("sting:removed", function () { if (state.joined) leave(); });
  // N4 — meeting layout mode changed (meeting-sync.js owns the control); reposition
  // the A/V bar to match: sidebar → dock bottom-right; pip/theater → bottom-centre.
  window.addEventListener("sting:meetLayout", function (e) {
    var mode = (e.detail && e.detail.mode) || "pip";
    var bar = document.getElementById("lkBar"); if (!bar) return;
    if (mode === "sidebar") { bar.style.left = "auto"; bar.style.right = "12px"; bar.style.transform = "none"; }
    else { bar.style.left = "50%"; bar.style.right = "auto"; bar.style.transform = "translateX(-50%)"; }
  });

  // ── boot: load livekit-client, fetch a token, show the JOIN lobby ─────────
  // M1 — A/V is gesture-gated: we build the in-meeting pill + a "Join A/V"
  // button but do NOT connect or touch the camera/mic until the user clicks
  // Join. Browsers require a user gesture for getUserMedia anyway, and an
  // unprompted camera light on page-load is hostile. Co-presence (meeting-
  // sync.js) still auto-joins — it needs no devices.
  loadScript(LK_CDN, function (ok) {
    if (!ok || !window.LivekitClient) { console.warn("[livekit] client lib failed to load"); buildShell(); setLobby("unavailable"); return; }
    state.LK = window.LivekitClient;
    fetchToken().then(function (info) {
      buildShell();
      if (!info) { setLobby("unavailable"); return; }   // 501 (unconfigured) / error
      state.tokenInfo = info;
      state.isPresenter = !!info.isPresenter;            // gates the screen-share button
      setLobby("ready");
      if (params.get("autojoin") === "1") join();        // opt-in auto-join for embeds/tests
    });
  });

  // M1 — explicit Join: connect the media plane + request devices ON the click
  // gesture (so the permission prompt is expected, not a surprise).
  function join() {
    if (state.joined || !state.tokenInfo) return;
    state.joined = true;
    setLobby("connecting");
    showLiveControls();
    connect(state.tokenInfo);
  }

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
      .on(LK.RoomEvent.TrackSubscribed, function (track, pub, participant) { attachTrack(track, participant); updateTileBadgeFor(participant); emitAvState(); })
      .on(LK.RoomEvent.TrackUnsubscribed, function (track, pub, participant) {
        try { track.detach().forEach(function (el) { el.remove(); }); } catch (e) {}
        if (isScreenShare(track)) clearScreen();
        // N1 — a remote turned their camera off (track gone): drop the video,
        // reveal the initials placeholder, keep the tile + name in the strip.
        if (participant && track && track.kind === "video" && !isScreenShare(track)) {
          var t = state.tiles.get(participant.sid);
          if (t) { t.video = null; showPlaceholder(t, true); updateTileBadge(t, participant); }
        }
        renderTiles(); emitAvState();
      })
      .on(LK.RoomEvent.LocalTrackUnpublished, function (pub) { if (pub && isScreenShare(pub.track)) { state.screenOn = false; clearScreen(); } })
      .on(LK.RoomEvent.ParticipantConnected, function () { renderTiles(); emitAvState(); })
      .on(LK.RoomEvent.ParticipantDisconnected, function (p) { dropTile(p.sid); emitAvState(); })
      .on(LK.RoomEvent.ActiveSpeakersChanged, function (speakers) { highlightSpeakers(speakers); emitAvState(); })
      // N1 — mic/camera mute toggles change a tile's badge + placeholder; reflect it live.
      .on(LK.RoomEvent.TrackMuted, function (pub, participant) { updateTileBadgeFor(participant); emitAvState(); })
      .on(LK.RoomEvent.TrackUnmuted, function (pub, participant) { updateTileBadgeFor(participant); emitAvState(); })
      .on(LK.RoomEvent.LocalTrackPublished, function (pub) { if (pub.track) attachTrack(pub.track, room.localParticipant); })
      .on(LK.RoomEvent.Disconnected, function () { onRoomDisconnected(); });

    room.connect(info.url, info.token)
      .then(function () { setStatus("live"); setLobby("live"); renderTiles(); emitAvState(); return enableDevices(); })
      .catch(function (e) {
        console.warn("[livekit] connect failed", e);
        setStatus("offline"); setLobby("error");
        state.joined = false; showLobbyControls();
      });

    window.addEventListener("beforeunload", function () { try { room.disconnect(); } catch (e) {} });

    // Expose a tiny API (used by screen-share + active-surface milestones).
    window.STING_LIVEKIT = {
      room: room,
      toggleMic: toggleMic,
      toggleCam: toggleCam,
      toggleScreen: toggleScreen,
      leave: leave,
      _av: {},
      avState: function () { return (window.STING_LIVEKIT && window.STING_LIVEKIT._av) || {}; },
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
    // M3 — low-bandwidth: drop remote CAMERA video (keep audio + screen-share).
    if (state.lowBw && !participant.isLocal) { try { var pub = track.sid && participant.getTrackPublication && participant.getTrackPublication(track.source); if (pub && pub.setSubscribed) pub.setSubscribed(false); } catch (e) {} return; }
    var tile = ensureTile(participant);
    // Clear any previous video element, attach the new track.
    if (tile.video) { try { tile.video.remove(); } catch (e) {} }
    var v = track.attach();
    v.style.cssText = "width:100%;height:100%;object-fit:cover;border-radius:6px;background:#000";
    v.muted = participant.isLocal === true;     // never echo our own mic
    tile.wrap.insertBefore(v, tile.label);
    tile.video = v;
    showPlaceholder(tile, false);               // N1 — live video hides the initials placeholder
    updateTileBadge(tile, participant);
  }

  function ensureTile(participant) {
    var sid = participant.sid;
    if (state.tiles.has(sid)) return state.tiles.get(sid);
    var wrap = el("div", {
      style: "position:relative;width:132px;height:99px;border-radius:6px;overflow:hidden;" +
        "background:#11141a;border:2px solid transparent;flex:0 0 auto"
    });
    // N1 — initials placeholder shown whenever there's no live camera video
    // (participant present but camera off / not yet published). Hidden by
    // attachTrack the moment a video track lands.
    var nm = (participant.name) || (participant.isLocal ? "You" : (participant.identity || "Guest"));
    var ph = el("div", {
      style: "position:absolute;inset:0;display:flex;align-items:center;justify-content:center;" +
        "font:600 26px -apple-system,Segoe UI,sans-serif;color:#9fb0c8;background:#11141a"
    }, initials(nm));
    wrap.appendChild(ph);
    // N1 — per-tile mic/camera state badge (top-left).
    var badge = el("div", {
      style: "position:absolute;left:4px;top:4px;display:flex;gap:3px;font-size:11px;line-height:1;" +
        "background:rgba(0,0,0,0.5);padding:2px 4px;border-radius:4px;pointer-events:none"
    });
    wrap.appendChild(badge);
    var label = el("div", {
      style: "position:absolute;left:4px;bottom:4px;right:4px;font:11px -apple-system,Segoe UI,sans-serif;" +
        "color:#fff;background:rgba(0,0,0,0.55);padding:1px 5px;border-radius:4px;" +
        "white-space:nowrap;overflow:hidden;text-overflow:ellipsis"
    }, nm);
    wrap.appendChild(label);
    // M3 — click a tile to pin it (speaker focus); click again to unpin.
    wrap.style.cursor = "pointer";
    wrap.addEventListener("click", function () {
      state.pinnedSid = (state.pinnedSid === sid) ? null : sid;
      applyTileLayout();
    });
    var strip = document.getElementById("lkStrip");
    if (strip) strip.appendChild(wrap);
    var tile = { wrap: wrap, video: null, label: label, ph: ph, badge: badge, sid: sid };
    state.tiles.set(sid, tile);
    updateTileBadge(tile, participant);
    applyTileLayout();
    return tile;
  }

  // N1 — initials / placeholder / per-tile badge + project-wide A/V state.
  function initials(name) {
    var parts = String(name || "?").trim().split(/[\s@._-]+/).filter(Boolean);
    var s = (parts[0] || "?").charAt(0) + (parts.length > 1 ? parts[parts.length - 1].charAt(0) : "");
    return s.toUpperCase() || "?";
  }
  function showPlaceholder(tile, on) { if (tile && tile.ph) tile.ph.style.display = on ? "flex" : "none"; }
  function avFor(p) {
    var cam = false, mic = false;
    try { cam = !!p.isCameraEnabled; } catch (e) {}
    try { mic = !!p.isMicrophoneEnabled; } catch (e) {}
    return { cam: cam, mic: mic };
  }
  function updateTileBadge(tile, p) {
    if (!tile || !tile.badge || !p) return;
    var a = avFor(p);
    tile.badge.innerHTML = "";
    tile.badge.appendChild(el("span", {}, a.mic ? "🎤" : "🔇"));
    tile.badge.appendChild(el("span", {}, a.cam ? "📹" : "🚫"));
    showPlaceholder(tile, !(a.cam && tile.video));   // hide placeholder only when a live camera is shown
  }
  function participantBySid(sid) {
    var room = state.room; if (!room) return null;
    if (room.localParticipant && room.localParticipant.sid === sid) return room.localParticipant;
    var found = null; room.remoteParticipants.forEach(function (p) { if (p.sid === sid) found = p; });
    return found;
  }
  function updateTileBadgeFor(p) { if (!p) return; var t = state.tiles.get(p.sid); if (t) updateTileBadge(t, p); }
  // N1 — broadcast the room's A/V state (keyed by participant identity = userId)
  // so meeting-sync.js can show camera/mic/in-call status on the co-presence roster.
  function emitAvState() {
    var room = state.room, map = {};
    if (room) {
      var add = function (p) {
        if (!p) return; var a = avFor(p);
        map[String(p.identity || p.sid)] = {
          name: p.name || p.identity || "", cam: a.cam, mic: a.mic, present: true,
          isLocal: !!p.isLocal, speaking: p.sid === state.activeSid
        };
      };
      add(room.localParticipant);
      room.remoteParticipants.forEach(add);
    }
    if (window.STING_LIVEKIT) window.STING_LIVEKIT._av = map;
    try { window.dispatchEvent(new CustomEvent("sting:avState", { detail: map })); } catch (e) {}
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
    // N1 — keep every tile's mic/cam badge + placeholder in sync with live state.
    state.tiles.forEach(function (t, sid) { var p = participantBySid(sid); if (p) updateTileBadge(t, p); });
  }
  function highlightSpeakers(speakers) {
    var active = {};
    speakers.forEach(function (p) { active[p.sid] = true; });
    if (speakers && speakers.length) state.activeSid = speakers[0].sid;   // speaker-view focus
    state.tiles.forEach(function (t, sid) {
      t.wrap.style.borderColor = active[sid] ? "#37c272" : "transparent";
    });
    if (state.viewMode === "speaker" && !state.pinnedSid) applyTileLayout();
  }

  // ── M3 — speaker / gallery layout + pin ────────────────────────────────────
  function setTileSize(tile, big) {
    tile.wrap.style.width = big ? "320px" : "132px";
    tile.wrap.style.height = big ? "200px" : "99px";
    tile.wrap.style.boxShadow = big ? "0 0 0 2px rgba(59,130,246,0.8)" : "none";
  }
  function applyTileLayout() {
    var focus = state.pinnedSid || (state.viewMode === "speaker" ? state.activeSid : null);
    state.tiles.forEach(function (t, sid) { setTileSize(t, sid === focus); });
  }
  function toggleView() {
    state.viewMode = (state.viewMode === "gallery") ? "speaker" : "gallery";
    var b = document.getElementById("lkView");
    if (b) { b.textContent = state.viewMode === "speaker" ? "▭" : "▦"; b.title = state.viewMode === "speaker" ? "Speaker view (click for gallery)" : "Gallery view (click for speaker)"; }
    if (state.viewMode === "gallery") state.pinnedSid = null;
    applyTileLayout();
  }

  // ── M3 — device picker (camera / mic / speaker) ────────────────────────────
  function toggleDevicePicker() {
    var pop = document.getElementById("lkDevPop");
    if (pop) { pop.remove(); return; }
    pop = el("div", { id: "lkDevPop", style:
      "position:absolute;bottom:54px;left:50%;transform:translateX(-50%);z-index:16;" +
      "background:rgba(12,14,18,0.97);color:#fff;border-radius:10px;padding:10px 12px;pointer-events:auto;" +
      "font:12px -apple-system,Segoe UI,sans-serif;display:flex;flex-direction:column;gap:8px;min-width:230px" });
    pop.appendChild(el("div", { style: "font-weight:600;margin-bottom:2px" }, "Devices"));
    buildDeviceSelect(pop, "videoinput", "Camera");
    buildDeviceSelect(pop, "audioinput", "Microphone");
    buildDeviceSelect(pop, "audiooutput", "Speaker");
    var live = document.getElementById("lkLive"); if (live) live.appendChild(pop);
  }
  function buildDeviceSelect(pop, kind, label) {
    var row = el("div", { style: "display:flex;flex-direction:column;gap:3px" });
    row.appendChild(el("label", { style: "opacity:0.8" }, label));
    var sel = el("select", { style: "background:#1b1f27;color:#fff;border:1px solid #333;border-radius:6px;padding:4px" });
    row.appendChild(sel); pop.appendChild(row);
    if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) { sel.appendChild(el("option", {}, "Not available")); return; }
    navigator.mediaDevices.enumerateDevices().then(function (devs) {
      var n = 0;
      devs.forEach(function (d) {
        if (d.kind !== kind) return;
        n++;
        var o = document.createElement("option"); o.value = d.deviceId; o.textContent = d.label || (label + " " + n); sel.appendChild(o);
      });
      if (!n) sel.appendChild(el("option", {}, "No " + label.toLowerCase()));
    }).catch(noop);
    sel.addEventListener("change", function () {
      if (!state.room || !state.room.switchActiveDevice) return;
      try { state.room.switchActiveDevice(kind, sel.value); } catch (e) { console.warn("[livekit] switchActiveDevice", e); }
    });
  }

  // ── N3 — present a document (discoverable picker + drag-drop / upload) ───────
  // Presenter picks an existing project doc OR drops / uploads a local file into the
  // shared DOCUMENT surface. The file is persisted to the project document store first
  // (so every participant can fetch it by id), then the surface is broadcast via
  // MeetingHub SurfaceChanged → all clients render the same doc and M2 markup syncs on it.
  function openDocPicker() {
    if (!state.isPresenter) { toast("Only the presenter can share a document"); return; }
    var pop = document.getElementById("lkDocPick");
    if (pop) { pop.remove(); return; }
    pop = el("div", { id: "lkDocPick", style:
      "position:absolute;bottom:54px;left:50%;transform:translateX(-50%);z-index:16;" +
      "background:rgba(12,14,18,0.97);color:#fff;border-radius:10px;padding:12px;pointer-events:auto;" +
      "font:12px -apple-system,Segoe UI,sans-serif;display:flex;flex-direction:column;gap:8px;width:300px;max-width:92vw" });
    pop.appendChild(el("div", { style: "font-weight:600" }, "Present a document"));

    var search = el("input", { placeholder: "Search project documents…", style:
      "background:#1b1f27;color:#fff;border:1px solid #333;border-radius:6px;padding:5px 8px" });
    pop.appendChild(search);

    var list = el("div", { style: "max-height:220px;overflow-y:auto;display:flex;flex-direction:column;gap:2px" });
    list.appendChild(el("div", { style: "opacity:0.7;padding:6px" }, "Loading…"));
    pop.appendChild(list);

    // drag-drop / click-to-upload zone for a LOCAL file.
    var drop = el("div", { id: "lkDocDrop", style:
      "border:1.5px dashed rgba(255,255,255,0.35);border-radius:8px;padding:10px;text-align:center;cursor:pointer;opacity:0.92" },
      "⬇ Drop a file here, or click to upload");
    var fileInput = el("input", { type: "file" }); fileInput.style.display = "none";
    drop.appendChild(fileInput);
    drop.addEventListener("click", function () { fileInput.click(); });
    fileInput.addEventListener("change", function () { if (fileInput.files && fileInput.files[0]) uploadAndShare(fileInput.files[0]); });
    ["dragenter", "dragover"].forEach(function (ev) { drop.addEventListener(ev, function (e) { e.preventDefault(); e.stopPropagation(); drop.style.background = "rgba(55,194,114,0.18)"; }); });
    ["dragleave"].forEach(function (ev) { drop.addEventListener(ev, function (e) { e.preventDefault(); e.stopPropagation(); drop.style.background = ""; }); });
    drop.addEventListener("drop", function (e) {
      e.preventDefault(); e.stopPropagation(); drop.style.background = "";
      var f = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
      if (f) uploadAndShare(f);
    });
    pop.appendChild(drop);

    var live = document.getElementById("lkLive"); if (live) live.appendChild(pop);

    function render(items, q) {
      list.innerHTML = "";
      var filtered = items.filter(function (d) { return !q || (((d.fileName || "") + " " + (d.documentType || "")).toLowerCase().indexOf(q) >= 0); });
      if (!filtered.length) { list.appendChild(el("div", { style: "opacity:0.6;padding:6px" }, items.length ? "No match" : "No documents in this project")); return; }
      filtered.slice(0, 200).forEach(function (d) {
        var row = el("button", { title: d.fileName || d.id, style:
          "text-align:left;border:none;border-radius:6px;cursor:pointer;padding:6px 8px;background:rgba(255,255,255,0.08);color:#fff;display:flex;flex-direction:column;gap:1px" });
        row.appendChild(el("div", { style: "white-space:nowrap;overflow:hidden;text-overflow:ellipsis" }, d.fileName || d.id));
        var sub = [d.documentType, d.discipline, d.revision].filter(Boolean).join(" · ");
        if (sub) row.appendChild(el("div", { style: "font-size:10px;opacity:0.6" }, sub));
        row.addEventListener("click", function () { setSurface("document", d.id); pop.remove(); toast("Sharing " + (d.fileName || "document")); });
        list.appendChild(row);
      });
    }
    var allDocs = [];
    fetch(apiBase + "/api/projects/" + projectId + "/documents?pageSize=200", { headers: token ? { "Authorization": "Bearer " + token } : {} })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) { allDocs = (d && d.items) || []; render(allDocs, ""); })
      .catch(function () { list.innerHTML = ""; list.appendChild(el("div", { style: "opacity:0.6;padding:6px" }, "Could not load documents")); });
    search.addEventListener("input", function () { render(allDocs, search.value.trim().toLowerCase()); });
  }
  function uploadAndShare(file) {
    if (!file) return;
    if (!state.isPresenter) { toast("Only the presenter can share a document"); return; }
    toast("Uploading " + file.name + "…");
    var fd = new FormData();
    fd.append("file", file, file.name);
    fetch(apiBase + "/api/projects/" + projectId + "/documents/upload", {
      method: "POST",
      headers: token ? { "Authorization": "Bearer " + token } : {},   // NO Content-Type — the browser sets the multipart boundary
      body: fd,
    }).then(function (r) { return r.ok ? r.json() : null; })
      .then(function (doc) {
        if (!doc || !doc.id) { toast("Upload failed"); return; }
        var pick = document.getElementById("lkDocPick"); if (pick) pick.remove();
        setSurface("document", doc.id);
        toast("Shared " + (doc.fileName || file.name));
      }).catch(function (e) { toast("Upload failed"); console.warn("[livekit] upload", e); });
  }

  // ── M3 — low-bandwidth (audio-only) mode ───────────────────────────────────
  function toggleLowBw() {
    state.lowBw = !state.lowBw;
    var b = document.getElementById("lkLowBw");
    if (b) b.style.background = state.lowBw ? "rgba(244,180,0,0.9)" : "rgba(255,255,255,0.14)";
    if (!state.room) return;
    if (state.lowBw) {
      // Stop publishing our camera + unsubscribe remote camera video (TrackUnsubscribed
      // detaches the tile videos). Audio + screen-share stay.
      state.camOn = false; state.room.localParticipant.setCameraEnabled(false).catch(noop); paintBtn("lkCam", false, "📹", "🚫");
      state.room.remoteParticipants.forEach(function (p) {
        p.trackPublications.forEach(function (pub) {
          if (pub.kind === "video" && pub.source !== state.LK.Track.Source.ScreenShare && pub.setSubscribed) { try { pub.setSubscribed(false); } catch (e) {} }
        });
      });
      toast("Low-bandwidth: audio only");
    } else {
      state.room.remoteParticipants.forEach(function (p) {
        p.trackPublications.forEach(function (pub) {
          if (pub.kind === "video" && pub.setSubscribed) { try { pub.setSubscribed(true); } catch (e) {} }
        });
      });
      toast("Video restored");
    }
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
    if (screen) screen.style.display = (surface === "screen") ? "flex" : "none";
    if (surface === "document") {
      // Ensure the pane exists BEFORE toggling its display — on the first switch
      // the element doesn't exist yet, so the old "set display on a null lookup"
      // left the doc surface permanently hidden.
      var pane = ensureDocPane();
      pane.style.display = "flex";
      showDocPane(docId);
      sizeMarkupCanvas(); renderMarkup();
    } else {
      var doc = document.getElementById("lkDoc");
      if (doc) doc.style.display = "none";
    }
    refreshMarkupUI();   // M2 — show/hide the markup toolbar with the doc surface
    paintSurfaceSwitch(surface);
  }
  function showDocPane(docId) {
    var pane = ensureDocPane();
    if (!docId) { pane._frame.removeAttribute("src"); pane._msg.textContent = "No document selected"; pane._msg.style.display = "flex"; return; }
    if (pane._docId === docId && pane._frame.getAttribute("src")) return;   // already showing
    pane._docId = docId; pane._msg.style.display = "none";
    // Fetch the doc file with the auth header, show it via a blob URL (PDF/image).
    // N3 — the route is /download (there is no /file route — the old URL 404'd, so the
    // shared-document surface never rendered).
    var url = apiBase + "/api/projects/" + projectId + "/documents/" + docId + "/download";
    fetch(url, { headers: token ? { "Authorization": "Bearer " + token } : {} })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.blob(); })
      .then(function (b) { if (pane._url) URL.revokeObjectURL(pane._url); pane._url = URL.createObjectURL(b); pane._frame.src = pane._url; })
      .catch(function (e) { pane._msg.textContent = "Could not load document"; pane._msg.style.display = "flex"; console.warn("[livekit] doc", e); });
  }
  function ensureDocPane() {
    var pane = document.getElementById("lkDoc");
    if (pane) return pane;
    pane = el("div", { id: "lkDoc", style:
      "position:absolute;inset:0 0 120px 0;z-index:13;display:none;flex-direction:column;gap:6px;background:#15171c;padding:8px" });
    // M2 — markup toolbar sits above a relative "stage" that stacks the document
    // iframe and the markup canvas; the canvas overlays the doc 1:1.
    var bar = buildMarkupToolbar();
    var stage = el("div", { style: "position:relative;flex:1;min-height:0" });
    // P3 — sandbox the shared-document iframe. The src is only ever an auth-fetched
    // blob URL of the document response; allow-same-origin lets the blob render, nothing
    // else (no scripts, forms, top-navigation, or popups).
    var frame = el("iframe", { sandbox: "allow-same-origin", style: "position:absolute;inset:0;width:100%;height:100%;border:none;border-radius:6px;background:#fff" });
    var cvs = el("canvas", { id: "lkMarkupCanvas", style: "position:absolute;inset:0;pointer-events:none;touch-action:none" });
    var msg = el("div", { style: "position:absolute;inset:0;display:none;align-items:center;justify-content:center;color:#9aa3b2;font:13px sans-serif" }, "");
    stage.appendChild(frame); stage.appendChild(cvs); stage.appendChild(msg);
    pane.appendChild(bar); pane.appendChild(stage);
    pane._frame = frame; pane._msg = msg; pane._cvs = cvs; pane._stage = stage;
    document.body.appendChild(pane);
    wireMarkupPointer(cvs);
    window.addEventListener("resize", function () { if (state.surface === "document") { sizeMarkupCanvas(); renderMarkup(); } });
    return pane;
  }

  // ── M2 — collaborative document markup (canvas overlay, MeetingHub wire) ────
  function sizeMarkupCanvas() {
    var pane = document.getElementById("lkDoc");
    if (!pane || !pane._cvs || !pane._stage) return;
    var r = pane._stage.getBoundingClientRect();
    var w = Math.max(1, Math.round(r.width)), h = Math.max(1, Math.round(r.height));
    if (pane._cvs.width !== w) pane._cvs.width = w;
    if (pane._cvs.height !== h) pane._cvs.height = h;
  }
  function renderMarkup() {
    var pane = document.getElementById("lkDoc");
    if (!pane || !pane._cvs) return;
    var ctx = pane._cvs.getContext("2d"); if (!ctx) return;
    var W = pane._cvs.width, H = pane._cvs.height;
    ctx.clearRect(0, 0, W, H);
    MARKUP.strokes.forEach(function (s) { drawStroke(ctx, s, W, H); });
    if (MARKUP.cur) drawStroke(ctx, MARKUP.cur, W, H);
  }
  function drawStroke(ctx, s, W, H) {
    var pts = (s.pts || []).map(function (p) { return [p[0] * W, p[1] * H]; });
    ctx.save();
    ctx.lineCap = "round"; ctx.lineJoin = "round";
    ctx.strokeStyle = s.color || "#e8413a"; ctx.fillStyle = s.color || "#e8413a";
    ctx.lineWidth = s.w || 3;
    if (s.tool === "highlight") { ctx.globalAlpha = 0.35; ctx.lineWidth = (s.w || 3) * 5; }
    if (s.tool === "pen" || s.tool === "highlight") {
      if (pts.length) { ctx.beginPath(); ctx.moveTo(pts[0][0], pts[0][1]); for (var i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]); ctx.stroke(); }
    } else if (s.tool === "rect" && pts.length >= 2) {
      ctx.strokeRect(Math.min(pts[0][0], pts[1][0]), Math.min(pts[0][1], pts[1][1]),
        Math.abs(pts[1][0] - pts[0][0]), Math.abs(pts[1][1] - pts[0][1]));
    } else if (s.tool === "arrow" && pts.length >= 2) {
      var a = pts[0], b = pts[1];
      ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke();
      var ang = Math.atan2(b[1] - a[1], b[0] - a[0]), hl = Math.max(10, (s.w || 3) * 3);
      ctx.beginPath(); ctx.moveTo(b[0], b[1]);
      ctx.lineTo(b[0] - hl * Math.cos(ang - 0.4), b[1] - hl * Math.sin(ang - 0.4));
      ctx.lineTo(b[0] - hl * Math.cos(ang + 0.4), b[1] - hl * Math.sin(ang + 0.4));
      ctx.closePath(); ctx.fill();
    } else if (s.tool === "text" && pts.length) {
      var fs = Math.max(12, (s.w || 3) * 5);
      ctx.font = "600 " + fs + "px -apple-system,Segoe UI,sans-serif"; ctx.textBaseline = "top";
      ctx.fillText(s.text || "", pts[0][0], pts[0][1]);
    }
    ctx.restore();
  }
  function wireMarkupPointer(cvs) {
    function norm(e) {
      var r = cvs.getBoundingClientRect();
      return [clamp01((e.clientX - r.left) / (r.width || 1)), clamp01((e.clientY - r.top) / (r.height || 1))];
    }
    cvs.addEventListener("pointerdown", function (e) {
      if (!MARKUP.draw || !markupAllowed()) return;
      e.preventDefault();
      var p = norm(e);
      if (MARKUP.tool === "text") {
        var t = prompt("Text annotation:"); if (!t) return;
        commitStroke(mkStroke([p], t)); return;
      }
      MARKUP.cur = mkStroke([p]);
      try { cvs.setPointerCapture(e.pointerId); } catch (x) {}
    });
    cvs.addEventListener("pointermove", function (e) {
      if (!MARKUP.cur) return;
      var p = norm(e);
      if (MARKUP.tool === "pen" || MARKUP.tool === "highlight") MARKUP.cur.pts.push(p);
      else MARKUP.cur.pts[1] = p;       // arrow / rect — second point tracks the cursor
      renderMarkup();
    });
    function finish() {
      if (!MARKUP.cur) return;
      var s = MARKUP.cur; MARKUP.cur = null;
      if (s.pts.length < 2 && s.tool !== "text") { renderMarkup(); return; }  // drop a dot/degenerate drag
      commitStroke(s);
    }
    cvs.addEventListener("pointerup", finish);
    cvs.addEventListener("pointercancel", finish);
  }
  function mkStroke(pts, text) {
    return { id: rid(), tool: MARKUP.tool, color: MARKUP.color, w: 3, pts: pts.slice(), text: text || "", by: myName() };
  }
  function commitStroke(s) {
    MARKUP.strokes.push(s); renderMarkup();
    try { window.STING_MEETING && window.STING_MEETING.broadcastDocMarkup({ op: "add", stroke: s }); } catch (e) {}
  }
  function onRemoteMarkup(m) {
    if (!m || !m.op) return;
    if (m.op === "add" && m.stroke) { MARKUP.strokes.push(m.stroke); renderMarkup(); }
    else if (m.op === "clear") { MARKUP.strokes = []; MARKUP.cur = null; renderMarkup(); }
    else if (m.op === "grant") { MARKUP.granted = !!m.on; refreshMarkupUI(); toast(m.on ? "Host enabled markup for everyone" : "Markup restricted to presenter"); }
  }
  function clearMarkup() {
    MARKUP.strokes = []; MARKUP.cur = null; renderMarkup();
    try { window.STING_MEETING && window.STING_MEETING.broadcastDocMarkup({ op: "clear" }); } catch (e) {}
  }
  function setDrawMode(on) {
    MARKUP.draw = !!on && markupAllowed();
    var pane = document.getElementById("lkDoc");
    if (pane && pane._cvs) pane._cvs.style.pointerEvents = MARKUP.draw ? "auto" : "none";
    var b = document.getElementById("lkMkDraw");
    if (b) { b.textContent = MARKUP.draw ? "✏️ Drawing" : "✏️ Markup"; b.style.background = MARKUP.draw ? "rgba(55,194,114,0.9)" : "rgba(255,255,255,0.14)"; }
  }
  function toggleGrant() {
    MARKUP.granted = !MARKUP.granted;       // presenter mirror of what others receive
    try { window.STING_MEETING && window.STING_MEETING.broadcastDocMarkup({ op: "grant", on: MARKUP.granted }); } catch (e) {}
    var b = document.getElementById("lkMkGrant"); if (b) b.style.background = MARKUP.granted ? "rgba(55,194,114,0.9)" : "rgba(255,255,255,0.14)";
    toast(MARKUP.granted ? "Markup enabled for everyone" : "Markup restricted to presenter");
  }
  function buildMarkupToolbar() {
    var bar = el("div", { id: "lkMkBar", style:
      "display:none;flex-wrap:wrap;align-items:center;gap:5px;pointer-events:auto;" +
      "background:rgba(0,0,0,0.5);border-radius:8px;padding:5px 7px" });
    [["pen", "✏︎", "Pen"], ["highlight", "🖍", "Highlighter"], ["arrow", "➟", "Arrow"], ["rect", "▭", "Rectangle"], ["text", "T", "Text"]].forEach(function (t) {
      bar.appendChild(mkBtn("lkMkTool_" + t[0], t[1], t[2], function () { MARKUP.tool = t[0]; paintTool(); setDrawMode(true); }));
    });
    ["#e8413a", "#f4b400", "#37c272", "#3b82f6", "#ffffff", "#111111"].forEach(function (c) {
      var sw = el("button", { title: "Colour " + c, style:
        "width:18px;height:18px;border-radius:50%;border:2px solid rgba(255,255,255,0.5);cursor:pointer;background:" + c });
      sw.addEventListener("click", function () { MARKUP.color = c; });
      bar.appendChild(sw);
    });
    bar.appendChild(mkBtn("lkMkDraw", "✏️ Markup", "Toggle drawing mode (off = scroll the document)", function () { setDrawMode(!MARKUP.draw); }));
    bar.appendChild(mkBtn("lkMkClear", "🗑 Clear", "Clear all markup", clearMarkup));
    bar.appendChild(mkBtn("lkMkSnap", "📸 Snapshot", "Save markup as a meeting snapshot", saveMarkupSnapshot));
    bar.appendChild(mkBtn("lkMkIssue", "⚑ Issue", "Save markup as an issue", saveMarkupIssue));
    bar.appendChild(mkBtn("lkMkGrant", "👥 Grant", "Allow everyone to draw (host only)", toggleGrant));
    return bar;
  }
  function mkBtn(id, glyph, title, fn) {
    var b = el("button", { id: id, title: title || "", style:
      "border:none;border-radius:6px;cursor:pointer;font:12px -apple-system,Segoe UI,sans-serif;" +
      "padding:4px 8px;background:rgba(255,255,255,0.14);color:#fff" }, glyph);
    b.addEventListener("click", fn); return b;
  }
  function paintTool() {
    ["pen", "highlight", "arrow", "rect", "text"].forEach(function (t) {
      var b = document.getElementById("lkMkTool_" + t);
      if (b) b.style.background = (t === MARKUP.tool) ? "rgba(59,130,246,0.85)" : "rgba(255,255,255,0.14)";
    });
  }
  function refreshMarkupUI() {
    var bar = document.getElementById("lkMkBar"); if (!bar) return;
    var show = (state.surface === "document") && markupAllowed();
    bar.style.display = show ? "flex" : "none";
    var g = document.getElementById("lkMkGrant"); if (g) g.style.display = state.isPresenter ? "inline-block" : "none";
    if (!show) setDrawMode(false);    // leaving the doc surface / not allowed → stop capturing pointer
    paintTool();
  }
  // ── markup persistence (durable — separate REST, not the hub) ──────────────
  function saveMarkupSnapshot() {
    var pane = document.getElementById("lkDoc");
    var label = prompt("Snapshot label:", "Markup");
    if (label === null) return;
    var body = { Label: label || "Markup", StateJson: JSON.stringify({
      surface: "document", documentId: (pane && pane._docId) || null, strokes: MARKUP.strokes }) };
    fetch(apiBase + "/api/projects/" + projectId + "/meeting-sessions/" + sessionId + "/snapshots",
      { method: "POST", headers: jsonHeaders(), body: JSON.stringify(body) })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function () { toast("Snapshot saved"); })
      .catch(function (e) { toast("Snapshot failed"); console.warn("[markup] snapshot", e); });
  }
  function saveMarkupIssue() {
    var title = prompt("Issue title:", "Markup from meeting"); if (!title) return;
    var pane = document.getElementById("lkDoc");
    var ip = apiBase + "/api/projects/" + projectId + "/issues";
    var desc = "Created from live-meeting markup on the shared document surface."
      + (pane && pane._docId ? "\nDocument: " + pane._docId : "") + "\nSession: " + sessionId;
    rasterizeMarkup(pane).then(function (blob) {
      return fetch(ip, { method: "POST", headers: jsonHeaders(),
        body: JSON.stringify({ Type: "OBS", Title: title, Description: desc, Priority: "MEDIUM" }) })
        .then(function (r) { if (!r.ok) throw new Error("issue " + r.status); return r.json(); })
        .then(function (issue) {
          if (!blob || !issue || !issue.id) return issue;
          var fd = new FormData();
          fd.append("file", blob, "markup-" + (issue.issueCode || "issue") + ".png");
          return fetch(ip + "/" + issue.id + "/attachments",
            { method: "POST", headers: token ? { "Authorization": "Bearer " + token } : {}, body: fd })
            .then(function () { return issue; }).catch(function () { return issue; });
        });
    }).then(function (issue) { toast("Issue " + ((issue && issue.issueCode) || "") + " created"); })
      .catch(function (e) { toast("Issue save failed"); console.warn("[markup] issue", e); });
  }
  // Markup is rasterised over a white background (the cross-origin sandboxed
  // iframe's pixels can't be read into a canvas); the document id is carried in
  // the issue description so reviewers can re-open the doc beneath the markup.
  function rasterizeMarkup(pane) {
    return new Promise(function (resolve) {
      var src = pane && pane._cvs;
      var W = (src && src.width) || 800, H = (src && src.height) || 600;
      var out = document.createElement("canvas"); out.width = W; out.height = H;
      var ctx = out.getContext("2d");
      ctx.fillStyle = "#ffffff"; ctx.fillRect(0, 0, W, H);
      MARKUP.strokes.forEach(function (s) { drawStroke(ctx, s, W, H); });
      if (out.toBlob) out.toBlob(function (b) { resolve(b); }, "image/png"); else resolve(null);
    });
  }
  function jsonHeaders() { return token ? { "Authorization": "Bearer " + token, "Content-Type": "application/json" } : { "Content-Type": "application/json" }; }
  function rid() { rid._n = (rid._n || 0) + 1; return "s" + rid._n + "_" + ((state.room && state.room.localParticipant && state.room.localParticipant.sid) || "x"); }
  function myName() { try { return (localStorage.getItem("planscape_user") || "Guest").split("@")[0]; } catch (e) { return "Guest"; } }
  function clamp01(v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
  function paintSurfaceSwitch(surface) {
    ["model", "document", "screen"].forEach(function (s) {
      var b = document.getElementById("lkSurf_" + s); if (!b) return;
      b.style.background = (s === surface) ? "rgba(59,130,246,0.85)" : "rgba(255,255,255,0.14)";
    });
  }

  // ── controls ───────────────────────────────────────────────────────────────
  // M1 — enable mic + camera independently so denying one doesn't block the
  // other; the buttons reflect the REAL device state (a denied/absent device
  // shows its control struck-through "off" rather than lying that it's live).
  function enableDevices() {
    var lp = state.room && state.room.localParticipant;
    if (!lp) return Promise.resolve();
    var mic = lp.setMicrophoneEnabled(true)
      .then(function () { state.micOn = true; paintBtn("lkMic", true, "🎤", "🔇"); })
      .catch(function (e) { state.micOn = false; paintBtn("lkMic", false, "🎤", "🔇"); toast("Microphone unavailable"); console.warn("[livekit] mic denied/unavailable", e); });
    var cam = lp.setCameraEnabled(true)
      .then(function () { state.camOn = true; paintBtn("lkCam", true, "📹", "🚫"); })
      .catch(function (e) { state.camOn = false; paintBtn("lkCam", false, "📹", "🚫"); toast("Camera unavailable"); console.warn("[livekit] camera denied/unavailable", e); });
    return Promise.all([mic, cam]).then(function () { updateTileBadgeFor(lp); emitAvState(); });
  }
  function toggleMic() {
    if (!state.room) return;
    state.micOn = !state.micOn;
    state.room.localParticipant.setMicrophoneEnabled(state.micOn).catch(noop);
    paintBtn("lkMic", state.micOn, "🎤", "🔇");
    updateTileBadgeFor(state.room.localParticipant); emitAvState();
  }
  function toggleCam() {
    if (!state.room) return;
    state.camOn = !state.camOn;
    state.room.localParticipant.setCameraEnabled(state.camOn).catch(noop);
    paintBtn("lkCam", state.camOn, "📹", "🚫");
    updateTileBadgeFor(state.room.localParticipant); emitAvState();
  }
  // M1 — Leave returns to the lobby (Join button) rather than destroying the
  // whole bar, so a user can re-join the same session without reloading.
  function leave() {
    if (state.room) { try { state.room.disconnect(); } catch (e) {} state.room = null; }
    state.joined = false; state.screenOn = false;
    clearTiles(); clearScreen();
    showLobbyControls(); setStatus("idle"); setLobby("left");
    emitAvState();   // N1 — room is null now → empty map clears "in call" on the roster
  }
  // Unexpected media drop (network / SFU restart). Co-presence (meeting-sync.js)
  // keeps its own auto-reconnect; here we fall back to the Join lobby.
  function onRoomDisconnected() {
    if (!state.joined) return;            // already left intentionally
    state.joined = false; state.room = null; state.screenOn = false;
    clearTiles(); clearScreen();
    showLobbyControls(); setStatus("offline"); setLobby("left");
    emitAvState();   // N1 — clear "in call" status on the roster
  }

  // ── UI shell ────────────────────────────────────────────────────────────────
  // M1 — one persistent bar with three rows: an "in a meeting" pill (always),
  // the participant tile strip (live only), and a control row that swaps between
  // a LOBBY group (Join A/V button) and a LIVE group (mic/cam/screen/surface/leave).
  function buildShell() {
    if (document.getElementById("lkBar")) return;
    var bar = el("div", { id: "lkBar", style:
      "position:absolute;bottom:12px;left:50%;transform:translateX(-50%);z-index:14;" +
      "display:flex;flex-direction:column;gap:8px;align-items:center;pointer-events:none" });

    // Row 1 — the "in a meeting" pill (status indicator, always visible).
    var pill = el("div", { id: "lkPill", style:
      "display:flex;align-items:center;gap:7px;pointer-events:auto;cursor:default;" +
      "background:rgba(0,0,0,0.66);color:#fff;padding:5px 12px;border-radius:18px;" +
      "font:12px -apple-system,Segoe UI,Roboto,sans-serif;backdrop-filter:blur(4px)" });
    pill.appendChild(el("span", { id: "lkDot", style:
      "width:9px;height:9px;border-radius:50%;background:#e8a13a;flex:0 0 auto" }));
    pill.appendChild(el("span", { id: "lkPillTxt" }, "In a meeting"));
    bar.appendChild(pill);

    // Row 2 — participant tiles (populated once live).
    var strip = el("div", { id: "lkStrip", style:
      "display:none;gap:8px;max-width:90vw;overflow-x:auto;pointer-events:auto;" +
      "padding:4px;background:rgba(0,0,0,0.35);border-radius:8px;backdrop-filter:blur(4px)" });
    bar.appendChild(strip);

    // Row 3a — LOBBY controls (pre-join).
    var lobby = el("div", { id: "lkLobby", style:
      "display:flex;gap:8px;pointer-events:auto;background:rgba(0,0,0,0.6);" +
      "padding:6px 10px;border-radius:24px;backdrop-filter:blur(4px)" });
    var joinBtn = el("button", { id: "lkJoin", title: "Join the meeting's audio / video", style:
      "border:none;border-radius:20px;cursor:pointer;font:600 14px -apple-system,Segoe UI,sans-serif;" +
      "padding:8px 18px;background:#37c272;color:#06140c" }, "▶  Join A/V");
    joinBtn.addEventListener("click", join);
    lobby.appendChild(joinBtn);
    bar.appendChild(lobby);

    // Row 3b — LIVE controls (post-join), hidden until Join.
    var live = el("div", { id: "lkLive", style:
      "display:none;gap:8px;pointer-events:auto;background:rgba(0,0,0,0.6);" +
      "padding:6px 10px;border-radius:24px;backdrop-filter:blur(4px)" });
    live.appendChild(ctrlBtn("lkMic", "🎤", toggleMic, "Mute / unmute mic"));
    live.appendChild(ctrlBtn("lkCam", "📹", toggleCam, "Camera on / off"));
    // M3 — device picker (cam/mic/speaker), low-bandwidth (audio-only), view toggle.
    live.appendChild(ctrlBtn("lkDev", "⚙", toggleDevicePicker, "Choose camera / mic / speaker"));
    live.appendChild(ctrlBtn("lkLowBw", "📶", toggleLowBw, "Low-bandwidth (audio-only)"));
    live.appendChild(ctrlBtn("lkView", "▦", toggleView, "Gallery / speaker view"));
    // WS3c — screen-share is presenter/host only (also enforced by the LiveKit grant).
    if (state.isPresenter) live.appendChild(ctrlBtn("lkScreen2", "🖥", toggleScreen, "Share / stop sharing screen"));
    // WS3d — presenter switches the shared surface everyone sees. 'screen' engages
    // automatically with the screen-share toggle above.
    if (state.isPresenter) {
      live.appendChild(ctrlBtn("lkSurf_model", "🧊", function () { setSurface("model"); }, "Show the 3D model"));
      live.appendChild(ctrlBtn("lkSurf_document", "📄", openDocPicker, "Present a document (pick a project doc · drag-drop · upload)"));
      // hidden anchor so paintSurfaceSwitch can highlight the 'screen' surface too
      live.appendChild(el("span", { id: "lkSurf_screen", style: "display:none" }));
    }
    live.appendChild(ctrlBtn("lkLeave", "✖", leave, "Leave A/V", "#d05050"));
    bar.appendChild(live);

    document.body.appendChild(bar);
  }
  function showLiveControls() {
    var lobby = document.getElementById("lkLobby"); if (lobby) lobby.style.display = "none";
    var live = document.getElementById("lkLive"); if (live) live.style.display = "flex";
    var strip = document.getElementById("lkStrip"); if (strip) strip.style.display = "flex";
  }
  function showLobbyControls() {
    var live = document.getElementById("lkLive"); if (live) live.style.display = "none";
    var strip = document.getElementById("lkStrip"); if (strip) strip.style.display = "none";
    var lobby = document.getElementById("lkLobby"); if (lobby) lobby.style.display = "flex";
  }
  function clearTiles() {
    state.tiles.forEach(function (t) { try { t.wrap.remove(); } catch (e) {} });
    state.tiles.clear();
    var strip = document.getElementById("lkStrip"); if (strip) strip.innerHTML = "";
  }
  // M1 — drive the pill's dot colour + label from the lobby/live state machine.
  var LOBBY_TEXT = {
    ready: "Ready to join",
    connecting: "Connecting…",
    live: "● Live",
    left: "Left — Join to rejoin",
    offline: "Reconnecting…",
    unavailable: "A/V unavailable",
    error: "Couldn't join — retry",
  };
  function setLobby(s) {
    var txt = document.getElementById("lkPillTxt");
    if (txt) txt.textContent = LOBBY_TEXT[s] || "In a meeting";
    var dot = document.getElementById("lkDot");
    if (dot) dot.style.background =
      s === "live" ? "#37c272" :
      (s === "connecting" ? "#e8a13a" :
      ((s === "error" || s === "unavailable" || s === "offline") ? "#d05050" : "#9aa3b2"));
    var join = document.getElementById("lkJoin");
    if (join) join.disabled = (s === "unavailable" || s === "connecting");
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
