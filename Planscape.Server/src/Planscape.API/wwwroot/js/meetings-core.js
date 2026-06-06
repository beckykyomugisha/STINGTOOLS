// meetings-core.js — the ONE meeting logic + API module shared by both front-ends
// (vanilla web /app dashboard.js via window.MeetingsCore, and the Expo app via import).
// Framework-agnostic: NO DOM, NO React. All network goes through an injected `fetchJson`
// so each surface supplies its own authed transport (web localStorage JWT / mobile token).
//
// SERVED marker (path #3, volume-mounted js): grep this file for STING_MEETINGS_CORE_BUILD.
//
// Usage:
//   const api = MeetingsCore.create(fetchJson);   // fetchJson(path, {method, body}) -> parsed JSON | null
//   await api.listMeetings(projectId);
// Pure helpers + the role matrix live on MeetingsCore directly (no fetch needed).
(function (root, factory) {
  if (typeof module === "object" && module.exports) module.exports = factory();   // CJS / Metro
  else root.MeetingsCore = factory();                                            // browser global
}(typeof self !== "undefined" ? self : this, function () {
  "use strict";

  var BUILD = "STING_MEETINGS_CORE_BUILD w0-core";

  // ── Role → capability matrix (W5 — single source of truth for BOTH surfaces). ──
  // Server enforces; UIs show only allowed controls. Role strings match the server's
  // ProjectRole / meeting role conventions (case-insensitive lookup via roleCaps()).
  var CAPS = {
    host:           ["schedule","start","record","present","editAgenda","editMinutes","assignActions","manageAttendees","completeOwnActions","join","view","watchRecordings","readMinutes"],
    bimcoordinator: ["schedule","start","record","present","editAgenda","editMinutes","assignActions","manageAttendees","completeOwnActions","join","view","watchRecordings","readMinutes"],
    manager:        ["schedule","start","record","present","editAgenda","editMinutes","assignActions","manageAttendees","completeOwnActions","join","view","watchRecordings","readMinutes"],
    chair:          ["editAgenda","editMinutes","assignActions","completeOwnActions","join","view","watchRecordings","readMinutes"],
    secretary:      ["editAgenda","editMinutes","assignActions","completeOwnActions","join","view","watchRecordings","readMinutes"],
    attendee:       ["completeOwnActions","join","view","watchRecordings","readMinutes"],
    "discipline-lead": ["completeOwnActions","join","view","watchRecordings","readMinutes"],
    client:         ["join","view","watchRecordings","readMinutes"],
    viewer:         ["view","watchRecordings","readMinutes"],
    field:          ["completeOwnActions","join","view","readMinutes"],
  };
  function normRole(r) { return String(r || "viewer").toLowerCase().replace(/\s+/g, "-").replace(/_/g, "-"); }
  function roleCaps(role) { return CAPS[normRole(role)] || CAPS.viewer; }
  function can(role, capability) { return roleCaps(role).indexOf(capability) !== -1; }

  // ── Pure formatting / predicate helpers (used by both surfaces' UIs). ──
  function fmtDuration(s) { if (!s || s <= 0) return "—"; var m = Math.floor(s / 60), ss = Math.round(s % 60); return m + ":" + String(ss).padStart(2, "0"); }
  function fmtSize(b) { if (!b || b <= 0) return "—"; return b >= 1048576 ? (b / 1048576).toFixed(1) + " MB" : (b / 1024).toFixed(0) + " KB"; }
  function isAudioKind(k) { return k === "audio-only" || k === "audio"; }
  function recordingIsPlayable(r) { return !!r && r.status === "COMPLETE" && !!r.downloadUrl; }
  // The viewer A/V deep-link for a meeting (A/V already works in the viewer).
  function meetingJoinUrl(projectId, meetingId) { return "/viewer.html?project=" + encodeURIComponent(projectId) + "&meeting=" + encodeURIComponent(meetingId); }

  // ── Notification-event normaliser (W2). Maps the SignalR/NotificationHub events the
  // server emits into one shape: { type, meetingId, title, body, deepLink }. Returns null
  // for events that aren't meeting-related. `event` = hub method name, `payload` = its arg.
  function parseNotificationEvent(event, payload) {
    payload = payload || {};
    var data = payload.data || payload;
    var mid = data.meetingId || data.MeetingId || payload.meetingId || null;
    switch (event) {
      case "Notification":
        if (data.type === "meeting_live" || data.Type === "meeting_live") {
          var sid = data.meetingSessionId || data.MeetingSessionId || null;
          return { type: "live-start", meetingId: mid, sessionId: sid,
                   title: payload.title || "Meeting started",
                   body: payload.message || payload.body || "Join the live meeting",
                   deepLink: data.deepLink || (mid ? ("?meeting=" + mid) : null) };
        }
        return null;
      case "MeetingScheduled":
        return { type: "scheduled", meetingId: payload.id || payload.Id || mid,
                 title: "Meeting scheduled", body: payload.title || payload.Title || "", deepLink: null };
      case "MeetingCreated":
      case "MeetingUpdated":
        return { type: event === "MeetingCreated" ? "created" : "updated",
                 meetingId: payload.id || payload.Id || mid, title: event, body: payload.title || payload.Title || "", deepLink: null };
      default:
        return null;
    }
  }
  // Meeting-related hub event names, for the client to subscribe to.
  var MEETING_EVENTS = ["Notification", "MeetingScheduled", "MeetingCreated", "MeetingUpdated"];

  // ── API factory. fetchJson(path, opts?) MUST return parsed JSON (or null for 204). ──
  function create(fetchJson) {
    if (typeof fetchJson !== "function") throw new Error("MeetingsCore.create(fetchJson): fetchJson is required");
    function get(p) { return fetchJson(p); }
    function send(p, method, body) { return fetchJson(p, { method: method, body: body == null ? undefined : JSON.stringify(body) }); }
    var base = function (pid) { return "/api/projects/" + pid + "/meetings"; };

    return {
      // Meetings
      listMeetings: function (pid, status) { return get(base(pid) + (status ? "?status=" + encodeURIComponent(status) : "")).then(function (r) { return Array.isArray(r) ? r : (r && r.items) || []; }); },
      getMeeting: function (pid, mid) { return get(base(pid) + "/" + mid); },
      createMeeting: function (pid, body) { return send(base(pid), "POST", body); },
      updateMeeting: function (pid, mid, body) { return send(base(pid) + "/" + mid, "PUT", body); },

      // Agenda
      addAgendaItem: function (pid, mid, body) { return send(base(pid) + "/" + mid + "/agenda", "POST", body); },
      updateAgendaItem: function (pid, mid, itemId, body) { return send(base(pid) + "/" + mid + "/agenda/" + itemId, "PUT", body); },
      deleteAgendaItem: function (pid, mid, itemId) { return send(base(pid) + "/" + mid + "/agenda/" + itemId, "DELETE"); },

      // Action items
      addAction: function (pid, mid, body) { return send(base(pid) + "/" + mid + "/actions", "POST", body); },
      updateAction: function (pid, mid, actionId, body) { return send(base(pid) + "/" + mid + "/actions/" + actionId, "PUT", body); },
      listOpenActions: function (pid) { return get(base(pid) + "/actions/open").then(function (r) { return Array.isArray(r) ? r : (r && r.items) || []; }); },

      // Attendees
      listAttendees: function (pid, mid) { return get(base(pid) + "/" + mid + "/attendees").then(function (r) { return Array.isArray(r) ? r : (r && r.items) || []; }); },
      addAttendee: function (pid, mid, body) { return send(base(pid) + "/" + mid + "/attendees", "POST", body); },
      updateAttendee: function (pid, mid, attendeeId, body) { return send(base(pid) + "/" + mid + "/attendees/" + attendeeId, "PUT", body); },
      deleteAttendee: function (pid, mid, attendeeId) { return send(base(pid) + "/" + mid + "/attendees/" + attendeeId, "DELETE"); },

      // Minutes
      logMinutes: function (pid, mid, minutes, status) { return send(base(pid) + "/" + mid + "/minutes", "POST", { minutes: minutes, status: status }); },
      generateMinutesDoc: function (pid, mid) { return send(base(pid) + "/" + mid + "/export/minutes", "POST"); },
      minutesIcsUrl: function (pid, mid) { return base(pid) + "/" + mid + "/export/ics"; },

      // Live session + artifacts
      startLiveSession: function (pid, mid, body) { return send(base(pid) + "/" + mid + "/live-session", "POST", body || {}); },
      getLiveArtifacts: function (pid, mid) { return get(base(pid) + "/" + mid + "/live-artifacts"); },

      // Recordings (project archive — includes presigned downloadUrl per row)
      listRecordings: function (pid) { return get("/api/projects/" + pid + "/recordings").then(function (r) { return (r && r.recordings) || []; }); },
      // recordings grouped by meetingId (for list badges + per-meeting detail)
      recordingsByMeeting: function (pid) {
        return this.listRecordings(pid).then(function (recs) {
          var by = {}; recs.forEach(function (r) { if (r.meetingId) (by[r.meetingId] = by[r.meetingId] || []).push(r); });
          return { all: recs, byMeeting: by };
        });
      },
    };
  }

  return {
    BUILD: BUILD,
    create: create,
    // pure helpers / matrix
    can: can, roleCaps: roleCaps, CAPS: CAPS,
    fmtDuration: fmtDuration, fmtSize: fmtSize, isAudioKind: isAudioKind,
    recordingIsPlayable: recordingIsPlayable, meetingJoinUrl: meetingJoinUrl,
    parseNotificationEvent: parseNotificationEvent, MEETING_EVENTS: MEETING_EVENTS,
  };
}));
