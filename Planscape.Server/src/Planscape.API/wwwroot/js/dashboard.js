// C1 — Planscape office dashboard. Vanilla JS; no build step.
//
// - localStorage stores the JWT access/refresh pair
// - fetch() + Bearer auth against the same /api/* endpoints mobile uses
// - Views are rendered into #main by swapping innerHTML — simple, good enough
//   for a read-only coordinator view.
(function () {
  "use strict";

  // Deployed-artifact marker for serve path #3 (vanilla web /app). dashboard.js is
  // volume-mounted (wwwroot/js) → reflects on restart/refresh, no docker rebuild.
  console.log("[dashboard] STING_DASH_BUILD p1-invite");

  // Phase 169 — runtime config. The Mapbox token must be replaced with a
  // real public token from mapbox.com (free account, no credit card
  // required). When left as the placeholder, the dashboard renders a
  // graceful fallback panel instead of crashing.
  const CONFIG = {
    apiBase: "/api",
    mapboxToken: "PLANSCAPE_MAPBOX_TOKEN",
  };

  const TOKEN_KEY   = "planscape_token";
  const REFRESH_KEY = "planscape_refresh";
  const USER_KEY    = "planscape_user";
  // coordination-viewer.js reads this to send the X-Tenant header on the
  // dashboard→viewer hop (coordination-viewer.js:301). The JWT already carries
  // tenant_id, so seed it from the token rather than waiting for the user to
  // fill in Settings (belt-and-braces — the server resolves tenant from the
  // JWT regardless, but the explicit header avoids any middleware that prefers
  // it).
  const TENANT_KEY  = "planscape_tenant";

  // Decode the `tenant_id` claim from a JWT (base64url middle segment) and
  // persist it as planscape_tenant. No verification — purely to populate the
  // client-side header; the server still validates the signed token.
  function seedTenantFromToken(token) {
    try {
      if (!token) return;
      const part = token.split(".")[1];
      if (!part) return;
      const json = atob(part.replace(/-/g, "+").replace(/_/g, "/"));
      const claims = JSON.parse(json);
      const tenant = claims.tenant_id || claims.tid || "";
      if (tenant) localStorage.setItem(TENANT_KEY, tenant);
    } catch (_) { /* malformed token — leave tenant unset */ }
  }

  const state = {
    projects: [],
    projectId: null,
    view: "overview",
    // Phase 169 — overview-screen UI state
    mapViewActive: false,
    mapInstance: null,
    overviewFilter: "all",     // all | active | archived | onhold
    overviewSearch: "",
    overviewSort: "lastActive", // lastActive | nameAsc | complianceAsc | complianceDesc | created
  };

  // ── Auth + fetch ──────────────────────────────────────────────────────

  function getToken()  { return localStorage.getItem(TOKEN_KEY); }
  function getRefresh() { return localStorage.getItem(REFRESH_KEY); }
  function setTokens(t, r) {
    localStorage.setItem(TOKEN_KEY, t);
    localStorage.setItem(REFRESH_KEY, r);
    seedTenantFromToken(t);
  }
  function clearTokens() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
    localStorage.removeItem(TENANT_KEY);
  }

  async function api(path, options = {}) {
    const headers = Object.assign({ "Content-Type": "application/json" }, options.headers || {});
    const tk = getToken();
    if (tk) headers.Authorization = "Bearer " + tk;

    let res = await fetch(path, Object.assign({}, options, { headers }));
    if (res.status === 401) {
      const r = await tryRefresh();
      if (r) {
        headers.Authorization = "Bearer " + getToken();
        res = await fetch(path, Object.assign({}, options, { headers }));
      } else {
        showLogin();
        const e = new Error("unauthenticated");
        e.unauthenticated = true;
        throw e;
      }
    }
    if (!res.ok) throw new Error("HTTP " + res.status + " " + await res.text());
    const txt = await res.text();
    return txt ? JSON.parse(txt) : null;
  }
  async function tryRefresh() {
    const rt = getRefresh();
    if (!rt) return false;
    const res = await fetch("/api/auth/refresh", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: rt }),
    });
    if (!res.ok) return false;
    const body = await res.json();
    setTokens(body.accessToken, body.refreshToken);
    return true;
  }

  // ── Login overlay ─────────────────────────────────────────────────────

  function showLogin() {
    document.getElementById("loginOverlay").classList.remove("hidden");
    const chip = document.getElementById("userChip");
    if (chip) chip.textContent = "Not signed in";
  }
  function hideLogin() {
    document.getElementById("loginOverlay").classList.add("hidden");
  }
  document.getElementById("loginForm").addEventListener("submit", async (ev) => {
    ev.preventDefault();
    const errEl = document.getElementById("loginError");
    errEl.textContent = "";
    try {
      const email    = document.getElementById("loginEmail").value.trim();
      const password = document.getElementById("loginPassword").value;
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });
      if (!res.ok) throw new Error(await res.text());
      const body = await res.json();
      setTokens(body.accessToken, body.refreshToken);
      localStorage.setItem(USER_KEY, body.userName || email);
      hideLogin();
      await boot();
    } catch (e) {
      errEl.textContent = "Sign-in failed.";
    }
  });
  document.getElementById("signOutBtn").addEventListener("click", () => {
    clearTokens();
    location.reload();
  });

  // ── Public runtime config ────────────────────────────────────────────

  async function loadPublicConfig() {
    try {
      const r = await fetch("/api/public-config", { cache: "no-store" });
      if (!r.ok) return;
      const cfg = await r.json();
      if (cfg && typeof cfg === "object") {
        if (cfg.mapboxToken) CONFIG.mapboxToken = cfg.mapboxToken;
      }
    } catch {
      // Non-fatal — fall back to compiled-in CONFIG defaults.
    }
  }

  // ── Realtime hub (SignalR) ────────────────────────────────────────────
  // P1-C — the dashboard previously had NO realtime client, so server
  // broadcasts (NotificationHub) were invisible until a manual reload.
  // `Hub` is a thin, reusable client: it lazy-loads @microsoft/signalr from
  // the same pinned CDN the viewer shim uses, connects to /hubs/notifications
  // with the dashboard's JWT (same-origin; the hub is NOT under /api), and
  // re-joins the active project's group. Subscribe to any server event with
  // Hub.on(event, handler). WarningsReported is the first consumer; add
  // IssueCreated / TransmittalUpdated / ComplianceChanged the same way
  // (they are pre-registered below, so a one-line Hub.on() is all it takes).
  // STING_DASH_SIGNALR_VENDORED — load SignalR from our OWN origin, not a third-party
  // CDN. Edge/Firefox Tracking Prevention blocks cdn.jsdelivr.net in InPrivate ("blocked
  // access to storage") and it fails offline; vendored locally (pinned 8.0.7), mirroring
  // the viewer + livekit-client.
  const SIGNALR_CDN = "/vendor/signalr.min.js";
  const Hub = (function () {
    const subs = new Map();
    let conn = null;
    let joinedProject = null;
    // Keep in lockstep with what NotificationHub emits to project groups.
    // Audited against Planscape.API/Controllers/ SendAsync sites — see
    // docs/PHASE_Z_AUDITS.md §Z-7. Server raises 17 distinct events; we
    // subscribe to the 13 that have a UI surface in this dashboard. The
    // 4 unsubscribed (LpsRecordPushed, SitePhoto*, PhotoAlbumChanged,
    // Deliverable*) target views not present in the SPA today — wire
    // their LIVE_VIEW_EVENTS entries the day those views are added.
    const KNOWN_EVENTS = [
      "WarningsReported",
      "IssueCreated", "IssueUpdated", "CommentAdded",
      "TransmittalUpdated",
      "ComplianceChanged", "ComplianceUpdated", "TagsUpdated",
      "DocumentUpdated", "ApprovalDecided",
      "MeetingCreated", "MeetingUpdated",
      // W2 — live-meeting start (per-user "Notification" with data.type=meeting_live) +
      // scheduled-meeting surface. Parsed via MeetingsCore.parseNotificationEvent.
      "Notification", "MeetingScheduled",
      "WorkflowRunCompleted",
      "ModelUpdated",
      // NotificationHub.JoinProject replies with these to the caller + the project
      // group; register them so SignalR doesn't warn "No client method with the name
      // 'joinedproject'/'presencechanged'". Subscribers can listen via Hub.on(); the
      // default registration just routes them through emit() (no-op when unobserved).
      "JoinedProject", "PresenceChanged",
    ];

    function emit(event, payload) {
      const list = subs.get(event); if (!list) return;
      for (const fn of list) { try { fn(payload); } catch (e) { console.warn("[hub] handler", event, e); } }
    }
    function on(event, handler) {
      if (typeof handler !== "function") return;
      let list = subs.get(event); if (!list) { list = []; subs.set(event, list); }
      list.push(handler);
    }
    function loadLib() {
      return new Promise((resolve, reject) => {
        if (window.signalR) return resolve(window.signalR);
        const tag = document.createElement("script");
        tag.src = SIGNALR_CDN; tag.async = true;
        tag.onload  = () => window.signalR ? resolve(window.signalR) : reject(new Error("signalR global missing"));
        tag.onerror = () => reject(new Error("SignalR CDN unreachable"));
        document.head.appendChild(tag);
      });
    }
    async function join(projectId) {
      if (!conn || !projectId || joinedProject === projectId) return;
      try {
        if (joinedProject) { try { await conn.invoke("LeaveProject", joinedProject); } catch (_) {} }
        await conn.invoke("JoinProject", projectId);
        joinedProject = projectId;
      } catch (e) { console.warn("[hub] JoinProject", e); }
    }
    async function start() {
      if (conn) { await join(state.projectId); return; }
      let SR;
      try { SR = await loadLib(); }
      catch (e) { console.warn("[hub] realtime disabled —", e.message); return; }
      conn = new SR.HubConnectionBuilder()
        .withUrl("/hubs/notifications", { accessTokenFactory: () => getToken() || "" })
        .withAutomaticReconnect([0, 1000, 5000, 15000, 30000])
        .configureLogging(SR.LogLevel.Warning)
        .build();
      KNOWN_EVENTS.forEach(name => conn.on(name, payload => emit(name, payload)));
      conn.onreconnected(() => {
        const pid = joinedProject; joinedProject = null;
        if (pid) join(pid);
      });
      conn.onclose(err => { if (err) console.warn("[hub] connection closed", err); });
      try { await conn.start(); await join(state.projectId); }
      catch (e) { console.warn("[hub] start failed", e); }
    }
    return { start, join, on };
  })();

  // W2 — live-meeting "Join" banner (top, dismissible). The server only sends the
  // live-start Notification to OTHER project members (starter excluded, membership-filtered),
  // so just surfacing it here is correct.
  function showJoinBanner(text, joinUrl) {
    var ex = document.getElementById("liveJoinBanner"); if (ex) ex.remove();
    var b = document.createElement("div");
    b.id = "liveJoinBanner";
    b.style.cssText = "position:fixed;top:0;left:0;right:0;z-index:10001;background:#1976d2;color:#fff;padding:10px 16px;display:flex;align-items:center;gap:12px;box-shadow:0 2px 8px rgba(0,0,0,.3);font-size:14px";
    b.innerHTML = '<span style="flex:1">🎥 ' + esc(text) + "</span>" +
      (joinUrl ? '<button id="ljbJoin" style="background:#fff;color:#1976d2;border:none;border-radius:5px;padding:6px 14px;font-weight:600;cursor:pointer">Join</button>' : "") +
      '<button id="ljbX" style="background:transparent;color:#fff;border:none;font-size:18px;cursor:pointer">✕</button>';
    document.body.appendChild(b);
    var x = document.getElementById("ljbX"); if (x) x.onclick = function () { b.remove(); };
    var j = document.getElementById("ljbJoin"); if (j) j.onclick = function () { window.open(joinUrl, "_blank"); b.remove(); };
    setTimeout(function () { try { b.remove(); } catch (e) {} }, 30000);
  }
  // W2 — live-start: "{user} started a meeting — Join" with a ?meeting= deep-link into the viewer.
  Hub.on("Notification", function (payload) {
    var MCx = (typeof window !== "undefined") && window.MeetingsCore; if (!MCx) return;
    var ev = MCx.parseNotificationEvent("Notification", payload); if (!ev || ev.type !== "live-start") return;
    var data = (payload && payload.data) || payload || {};
    var pid = data.projectId || state.projectId;
    var id = ev.sessionId || ev.meetingId;
    var url = id ? MCx.meetingJoinUrl(pid, id) : null;
    showJoinBanner((payload && (payload.title || payload.message)) || "A meeting started", url);
  });
  // W2 — scheduled meeting: light surface + refresh the list if it's open.
  Hub.on("MeetingScheduled", function (payload) {
    var MCx = (typeof window !== "undefined") && window.MeetingsCore; if (!MCx) return;
    var ev = MCx.parseNotificationEvent("MeetingScheduled", payload); if (!ev) return;
    toast("📅 Meeting scheduled: " + (ev.body || ""));
    if (state.section === "meetings") { var main = document.getElementById("main"); if (main) renderMeetings(main); }
  });

  // Update / create the live count badge on the Warnings nav link.
  function setWarnBadge(total, delta) {
    const link = document.querySelector('.nav-link[data-view="warnings"]');
    if (!link) return;
    let badge = link.querySelector(".nav-badge");
    if (!badge) { badge = document.createElement("span"); badge.className = "nav-badge"; link.appendChild(badge); }
    badge.textContent = String(total);
    badge.classList.toggle("up",   (delta || 0) > 0);
    badge.classList.toggle("down", (delta || 0) < 0);
  }

  // First realtime consumer: live warning counts. Filters to the active
  // project, updates the sidebar badge, toasts the delta, and refreshes the
  // Warnings list if it's the view on screen.
  Hub.on("WarningsReported", (payload) => {
    if (!payload) return;
    const samePid = String(payload.projectId).toLowerCase() === String(state.projectId).toLowerCase();
    if (!samePid) return;
    const delta = payload.delta || 0;
    setWarnBadge(payload.totalWarnings, delta);
    const verb = delta > 0 ? `+${delta}` : (delta < 0 ? String(delta) : "no change");
    try { showToast(`Warnings: ${payload.totalWarnings} (${verb})`); } catch (_) {}
    if (state.view === "warnings") render();
  });

  // ── P1-D — live refresh for the other dashboard views ─────────────────
  // Builds on the P1-C Hub. Each dashboard list view maps to the
  // NotificationHub events that invalidate its data; we re-render only when
  // that view is on screen. The connection is already scoped to one project
  // group so we receive only the active project's events — the projectId
  // check is a belt-and-suspenders guard for events that carry one.
  //
  // Names are the server's ACTUAL raise-site events (verified by grepping
  // SendAsync across Controllers + SignalR/): the workflow event is
  // WorkflowRunCompleted (not "WorkflowStateUpdate"), and there is NO server
  // clash event — the clash kernel runs in-Revit and there is no /hubs/clash —
  // so "ClashNotification" is intentionally absent (matches the audit).
  function matchesActiveProject(payload) {
    if (!payload || payload.projectId == null) return true;
    return String(payload.projectId).toLowerCase() === String(state.projectId).toLowerCase();
  }
  const LIVE_VIEW_EVENTS = {
    issues:       ["IssueCreated", "IssueUpdated", "CommentAdded"],
    documents:    ["DocumentUpdated", "ApprovalDecided"],
    transmittals: ["TransmittalUpdated"],
    meetings:     ["MeetingCreated", "MeetingUpdated"],
    workflows:    ["WorkflowRunCompleted"],
    models:       ["ModelUpdated"],
    overview:     ["ComplianceChanged", "ComplianceUpdated", "TagsUpdated"],
  };
  Object.keys(LIVE_VIEW_EVENTS).forEach(view => {
    LIVE_VIEW_EVENTS[view].forEach(ev => Hub.on(ev, payload => {
      if (!matchesActiveProject(payload)) return;
      if (state.view === view) render();
    }));
  });

  // ── Boot + router ─────────────────────────────────────────────────────

  // Wire the persistent chrome (chip, nav links) exactly once. This MUST run
  // before any network call so the sidebar buttons are always live — even
  // when the API is unreachable. Previously these handlers were attached
  // only *after* a successful /api/projects fetch, so a server that was down
  // (or returned 5xx) left every nav link dead and the chip frozen on
  // "Signing in…".
  // Views that can appear in the URL hash. project-dashboard is reachable by
  // deep-link only (no nav button).
  const KNOWN_VIEWS = new Set([
    "overview", "issues", "documents", "transmittals", "meetings", "recordings", "workflows",
    "warnings", "models", "photos", "schedule", "cost", "tenant-keywords",
    "tenant-bim-manager-roles", "project-dashboard",
  ]);

  // Parse a hash like "#models?project=GUID" into { view, project }.
  function parseHash() {
    const h = (location.hash || "").replace(/^#/, "");
    const [viewPart, queryPart] = h.split("?");
    const params = new URLSearchParams(queryPart || "");
    return { view: viewPart || "", project: params.get("project") };
  }

  // Sync state + active nav link from the URL hash. Called on boot and on
  // every hashchange so deep-links (incl. the Revit BCC "Open Web Dashboard"
  // button) land on the right view/project instead of always Overview.
  function applyHashRoute() {
    const { view, project } = parseHash();
    if (project && state.projects.some(p => String(p.id) === String(project))) {
      state.projectId = project;
      const picker = document.getElementById("projectPicker");
      if (picker) picker.value = project;
    }
    if (view && KNOWN_VIEWS.has(view)) {
      state.view = view;
      document.querySelectorAll(".nav-link").forEach(l => {
        l.classList.toggle("active", l.dataset.view === view);
      });
    }
  }

  let chromeWired = false;
  function wireChrome() {
    if (chromeWired) return;
    chromeWired = true;
    const chip = document.getElementById("userChip");
    if (chip) chip.textContent = localStorage.getItem(USER_KEY) || "Signed in";
    document.querySelectorAll(".nav-link").forEach(link => {
      link.addEventListener("click", (ev) => {
        ev.preventDefault();
        document.querySelectorAll(".nav-link").forEach(l => l.classList.remove("active"));
        link.classList.add("active");
        state.view = link.dataset.view;
        render();
      });
    });
    window.addEventListener("hashchange", () => { applyHashRoute(); render(); });
  }

  // Shown when the API can't be reached (server down, wrong port, CORS,
  // 5xx). Keeps the chrome alive and gives the user a way back in instead
  // of a silent freeze.
  function renderServerUnreachable(err) {
    const chip = document.getElementById("userChip");
    if (chip) chip.textContent = "Offline";
    const main = document.getElementById("main");
    if (!main) return;
    main.innerHTML = `
      <div class="greeting-strip">
        <div>
          <h2>Can't reach the Planscape server</h2>
          <div class="summary">The dashboard loaded, but the API at
            <code>${esc(location.origin)}/api</code> didn't respond. Make sure the
            Planscape server is running (e.g. <code>docker compose up -d</code> in
            <code>Planscape.Server/docker</code>), then retry.</div>
          <div class="summary" style="margin-top:6px;opacity:.7">${esc(String(err && err.message || err || ""))}</div>
        </div>
        <div class="actions">
          <button id="btnRetryBoot" class="btn-primary">↻ Retry</button>
          <button id="btnReLogin" class="ghost">Sign in again</button>
        </div>
      </div>`;
    const retry = document.getElementById("btnRetryBoot");
    if (retry) retry.onclick = () => { state.projectId = null; boot().catch(() => {}); };
    const relog = document.getElementById("btnReLogin");
    if (relog) relog.onclick = () => { clearTokens(); showLogin(); };
  }

  async function boot() {
    wireChrome();
    // Phase 169 — pull public runtime config (Mapbox token) before any
    // view tries to render the map. Non-fatal on its own.
    await loadPublicConfig();
    try {
      state.projects = await api("/api/projects");
    } catch (e) {
      // 401 path already invoked showLogin() inside api(); anything else is a
      // server-reachability problem — show it instead of freezing.
      if (!(e && e.unauthenticated)) renderServerUnreachable(e);
      return;
    }
    const picker = document.getElementById("projectPicker");
    picker.innerHTML = state.projects
      .map(p => `<option value="${p.id}">${esc(p.name)} (${esc(p.code)})</option>`)
      .join("");
    state.projectId = state.projects[0]?.id || null;
    picker.onchange = () => { state.projectId = picker.value; Hub.join(state.projectId); render(); };

    // Honour any deep-link in the URL hash (view + project) before first paint.
    applyHashRoute();

    // P1-C — open the realtime connection once we have a project context.
    // Fire-and-forget: rendering must not wait on the SignalR CDN load.
    Hub.start();

    render();
  }

  async function render() {
    const main = document.getElementById("main");
    if (!state.projectId) {
      // P0-5 — empty-projects state. A freshly-logged-in user (or any tenant
      // whose projects list is empty) used to land on a bare "No projects
      // available." string with no path forward. Render a friendly panel
      // with a "+ New project" button that reuses the existing modal flow.
      const userName = (localStorage.getItem(USER_KEY) || "").split("@")[0] || "there";
      main.innerHTML = `
        <div class="greeting-strip">
          <div>
            <h2>Welcome, ${esc(userName)}.</h2>
            <div class="summary">No projects yet — create one to start tracking issues, documents, transmittals, and the rest of the coordination workflow.</div>
          </div>
          <div class="actions">
            <button id="btnEmptyNewProject" class="btn-primary">＋ New Project</button>
          </div>
        </div>
        <div id="modal-mount"></div>`;
      const btn = document.getElementById("btnEmptyNewProject");
      if (btn) btn.onclick = openNewProjectModal;
      return;
    }
    syncSidebarScope();
    main.innerHTML = `<div class="empty">Loading…</div>`;
    try {
      switch (state.view) {
        case "overview":          await renderOverview(main); break;
        case "project-dashboard": await renderProjectDashboard(main); break;
        case "issues":       await renderList(main, `Issues`, `/api/projects/${state.projectId}/issues`, issueColumns); break;
        case "documents":    await renderList(main, `Documents`, `/api/projects/${state.projectId}/documents`, docColumns); break;
        case "transmittals": await renderList(main, `Transmittals`, `/api/projects/${state.projectId}/transmittals`, tmxColumns); break;
        case "meetings":     await renderMeetings(main); break;
        case "recordings":   await renderRecordings(main); break;
        case "workflows":    await renderList(main, `Workflow runs`, `/api/projects/${state.projectId}/workflows/history`, workflowColumns); break;
        case "warnings":     await renderList(main, `Warnings`, `/api/projects/${state.projectId}/warnings/trend`, warningColumns); break;
        case "models":       await renderModels(main); break;
        case "photos":       await renderPhotos(main); break;
        case "schedule":     await renderList(main, `Schedule`, `/api/projects/${state.projectId}/schedule`, scheduleColumns); break;
        case "cost":         await renderCost(main); break;
        // Phase 152 — admin: tenant keyword extensions for the
        // deliverable state machine. Doesn't depend on a project.
        case "tenant-keywords": await renderTenantKeywords(main); break;
        // Phase 155 — admin: tenant-scoped BIM-Manager role override.
        case "tenant-bim-manager-roles": await renderTenantBimManagerRoles(main); break;
      }
    } catch (e) {
      main.innerHTML = `<div class="empty">Could not load: ${esc(String(e))}</div>`;
    }
  }

  // ── Renderers ─────────────────────────────────────────────────────────

  // Phase 169 — ACC-style overview with project cards + Mapbox map view.
  async function renderOverview(main) {
    // Always work from the freshly-fetched projects list so pin toggles
    // and filter changes don't drift from the server state.
    state.projects = await api("/api/projects");
    const projects = state.projects;

    // Aggregate stats for the greeting summary line.
    const active = projects.filter(p => statusKey(p) === "active");
    let openIssues = 0;
    let overdue = 0;
    try {
      // Best-effort summary; if the dashboard endpoint fails for one
      // project the others still render. Cheap because we only call it
      // for active projects.
      const dashboards = await Promise.all(active.map(p =>
        api(`/api/projects/${p.id}/dashboard`).catch(() => null)));
      for (const d of dashboards) {
        if (!d) continue;
        openIssues += d.openIssues || 0;
        overdue   += d.overdueIssues || 0;
      }
    } catch { /* non-fatal */ }

    const userName = (localStorage.getItem(USER_KEY) || "").split("@")[0] || "there";
    const hour = new Date().getHours();
    const greeting = hour < 12 ? "Good morning"
                  : hour < 18 ? "Good afternoon"
                              : "Good evening";

    main.innerHTML = `
      <div class="greeting-strip">
        <div>
          <h2>${esc(greeting)}, ${esc(userName)}.</h2>
          <div class="summary">You have ${active.length} active project${active.length === 1 ? "" : "s"} · ${openIssues} open issue${openIssues === 1 ? "" : "s"} · ${overdue} overdue today.</div>
        </div>
        <div class="actions">
          <button id="btnNewProject" class="btn-primary">＋ New Project</button>
          <button id="btnToggleMap" class="btn-ghost-light">${state.mapViewActive ? "☰ Card view" : "🗺 Map view"}</button>
        </div>
      </div>
      <div id="overview-body"></div>
      <div id="modal-mount"></div>
    `;

    document.getElementById("btnNewProject").onclick = openNewProjectModal;
    document.getElementById("btnToggleMap").onclick = () => {
      state.mapViewActive = !state.mapViewActive;
      // Tear down any existing map before swapping views, otherwise
      // Mapbox leaks the WebGL context.
      if (!state.mapViewActive && state.mapInstance) {
        try { state.mapInstance.remove(); } catch {}
        state.mapInstance = null;
      }
      renderOverview(main);
    };

    renderOverviewBody();
  }

  function renderOverviewBody() {
    const body = document.getElementById("overview-body");
    if (!body) return;
    const projects = state.projects || [];
    const pinned = projects.filter(p => p.isPinned);

    let html = "";

    if (pinned.length > 0) {
      html += `<div class="section-eyebrow">📌 Pinned</div>`;
      html += `<div class="project-card-grid pinned-row">${pinned.map(projectCardHtml).join("")}</div>`;
    }

    const filtered = applyOverviewFilters(projects);

    html += `
      <div class="section-heading-row">
        <h3>All Projects (${filtered.length})</h3>
        <div class="controls-row" style="margin: 0">
          <div class="left">
            <input id="searchInput" type="text" class="search-input" placeholder="Search projects…" value="${esc(state.overviewSearch)}" />
            ${filterPillHtml("all", "All")}
            ${filterPillHtml("active", "Active")}
            ${filterPillHtml("archived", "Completed")}
            ${filterPillHtml("onhold", "On Hold")}
          </div>
          <div class="right">
            <select id="sortSelect" class="sort-select">
              <option value="lastActive"      ${state.overviewSort === "lastActive"      ? "selected" : ""}>Sort: Last Active</option>
              <option value="nameAsc"         ${state.overviewSort === "nameAsc"         ? "selected" : ""}>Sort: Name A-Z</option>
              <option value="complianceAsc"   ${state.overviewSort === "complianceAsc"   ? "selected" : ""}>Sort: Compliance ↑</option>
              <option value="complianceDesc"  ${state.overviewSort === "complianceDesc"  ? "selected" : ""}>Sort: Compliance ↓</option>
              <option value="created"         ${state.overviewSort === "created"         ? "selected" : ""}>Sort: Created Date</option>
            </select>
          </div>
        </div>
      </div>
    `;

    if (state.mapViewActive) {
      html += `<div id="projects-map"></div>`;
    } else {
      html += `<div class="project-card-grid">${
        filtered.length === 0
          ? `<div class="empty">No projects match your filters.</div>`
          : filtered.map(projectCardHtml).join("")
      }</div>`;
    }

    body.innerHTML = html;

    // Wire up control inputs (re-rendered on every change)
    const searchEl = document.getElementById("searchInput");
    if (searchEl) {
      searchEl.addEventListener("input", (e) => {
        state.overviewSearch = e.target.value;
        renderOverviewBody();
        // restore caret + focus after innerHTML swap
        const fresh = document.getElementById("searchInput");
        if (fresh) { fresh.focus(); fresh.setSelectionRange(state.overviewSearch.length, state.overviewSearch.length); }
      });
    }
    document.querySelectorAll(".filter-pill").forEach(pill => {
      pill.onclick = () => {
        state.overviewFilter = pill.dataset.filter;
        renderOverviewBody();
      };
    });
    const sortEl = document.getElementById("sortSelect");
    if (sortEl) sortEl.onchange = () => { state.overviewSort = sortEl.value; renderOverviewBody(); };

    // Wire up card buttons + map
    wireProjectCardEvents();
    if (state.mapViewActive) initProjectsMap(filtered);
  }

  function filterPillHtml(filterKey, label) {
    const active = state.overviewFilter === filterKey ? " active" : "";
    return `<button class="filter-pill${active}" data-filter="${filterKey}">${esc(label)}</button>`;
  }

  function applyOverviewFilters(projects) {
    const q = state.overviewSearch.trim().toLowerCase();
    let out = projects.filter(p => {
      if (state.overviewFilter !== "all" && statusKey(p) !== state.overviewFilter) return false;
      if (!q) return true;
      const hay = [(p.name || ""), (p.code || ""), (p.city || ""), (p.country || "")].join(" ").toLowerCase();
      return hay.includes(q);
    });
    switch (state.overviewSort) {
      case "nameAsc":
        out.sort((a, b) => (a.name || "").localeCompare(b.name || "")); break;
      case "complianceAsc":
        out.sort((a, b) => (a.compliancePercent || 0) - (b.compliancePercent || 0)); break;
      case "complianceDesc":
        out.sort((a, b) => (b.compliancePercent || 0) - (a.compliancePercent || 0)); break;
      case "created":
        out.sort((a, b) => new Date(b.createdAt || 0) - new Date(a.createdAt || 0)); break;
      default:
        // lastActive — already sorted by server (pinned first, then lastSyncAt)
        out.sort((a, b) => new Date(b.lastSyncAt || 0) - new Date(a.lastSyncAt || 0));
    }
    return out;
  }

  function statusKey(p) {
    // The API serialises ProjectStatus as either an enum int or a string
    // depending on JsonSerializer config, so accept both.
    const raw = p.status;
    if (typeof raw === "number") return raw === 1 ? "archived" : raw === 2 ? "handed_over" : "active";
    const s = String(raw || "active").toLowerCase();
    if (s === "active") return "active";
    if (s === "archived" || s === "handed_over" || s === "completed") return "archived";
    if (s === "on_hold" || s === "onhold")    return "onhold";
    return "active";
  }

  function statusLabel(key) {
    return key === "archived" ? "✓ Completed"
         : key === "onhold"   ? "⏸ On Hold"
                              : "● Active";
  }

  function complianceColor(pct) {
    if (pct >= 80) return "green";
    if (pct >= 50) return "amber";
    return "red";
  }

  function disciplinesFor(p) {
    // No structured discipline field on the project DTO, so derive a
    // plausible mix from phase/status. Cards must show 1-4 chips.
    const status = statusKey(p);
    if (status === "archived") return ["A", "S", "M", "E"];
    const phase = String(p.phase || "").toLowerCase();
    if (phase.includes("design"))   return ["A", "S", "M"];
    if (phase.includes("execut"))   return ["M", "E", "P", "FP"];
    if (phase.includes("handover")) return ["A", "M", "E"];
    return ["A", "M", "E"];
  }

  function timeAgo(iso) {
    if (!iso) return "—";
    const t = new Date(iso).getTime();
    if (!t) return "—";
    const diff = Date.now() - t;
    const m = Math.floor(diff / 60000);
    if (m < 1) return "just now";
    if (m < 60) return `${m} min ago`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h} hour${h === 1 ? "" : "s"} ago`;
    const d = Math.floor(h / 24);
    if (d < 30) return `${d} day${d === 1 ? "" : "s"} ago`;
    const mo = Math.floor(d / 30);
    return `${mo} month${mo === 1 ? "" : "s"} ago`;
  }

  function projectCardHtml(p) {
    const sk = statusKey(p);
    const pct = Math.round(p.compliancePercent || 0);
    const colour = complianceColor(pct);
    const loc = p.city ? `${esc(p.city)}${p.country ? ", " + esc(p.country) : ""}` : "Location not set";
    const discs = disciplinesFor(p);

    return `
      <div class="project-card" data-project-id="${esc(p.id)}">
        <div class="card-cover">
          <span class="code-chip">${esc(p.code || "—")}</span>
          <span class="status-chip ${sk}">${statusLabel(sk)}</span>
          <span class="location">📍 ${loc}</span>
          <button class="pin-btn ${p.isPinned ? "pinned" : ""}" data-pin-id="${esc(p.id)}" title="${p.isPinned ? "Unpin" : "Pin"}">
            ${p.isPinned ? "★" : "☆"}
          </button>
        </div>
        <div class="card-body">
          <p class="project-name" title="${esc(p.name || "")}">${esc(p.name || "Untitled")}</p>
          <div class="disc-chips">
            ${discs.map(d => `<span class="disc-chip ${d}">${d}</span>`).join("")}
          </div>
          <div class="compliance-block">
            <div class="compliance-row"><span>Compliance</span><span>${pct}%</span></div>
            <div class="compliance-bar"><div class="compliance-bar-fill ${colour}" data-target-width="${pct}"></div></div>
          </div>
          <div class="card-stats">
            <span>⚠ ${p.warningCount || 0} issues</span>
            <span>📄 – docs</span>
            <span>👥 ${p.memberCount ?? 0} members</span>
          </div>
          <div class="card-role">You: BIM Coordinator</div>
          <div class="card-last-active">Last sync: ${timeAgo(p.lastSyncAt)}</div>
        </div>
        <div class="card-footer">
          <button data-card-action="project-dashboard" data-project-id="${esc(p.id)}">Dashboard</button>
          <button data-card-action="issues"            data-project-id="${esc(p.id)}">Issues</button>
          <button data-card-action="documents"         data-project-id="${esc(p.id)}">Documents</button>
        </div>
      </div>
    `;
  }

  function wireProjectCardEvents() {
    // Animate compliance bars on render
    requestAnimationFrame(() => {
      document.querySelectorAll(".compliance-bar-fill").forEach(el => {
        const w = el.dataset.targetWidth || "0";
        el.style.width = `${w}%`;
      });
    });

    // Card body click → navigate to the per-project dashboard
    document.querySelectorAll(".project-card").forEach(card => {
      card.onclick = (e) => {
        // Ignore clicks that originated on internal action buttons or pin
        if (e.target.closest(".pin-btn")) return;
        if (e.target.closest("[data-card-action]")) return;
        navigateToProject(card.dataset.projectId, "project-dashboard");
      };
    });

    // Pin button
    document.querySelectorAll(".pin-btn").forEach(btn => {
      btn.onclick = async (e) => {
        e.stopPropagation();
        const id = btn.dataset.pinId;
        try {
          await api(`/api/projects/${id}/pin`, { method: "PATCH" });
          state.projects = await api("/api/projects");
          renderOverviewBody();
        } catch (err) {
          showToast("Could not toggle pin");
        }
      };
    });

    // Footer action buttons
    document.querySelectorAll("[data-card-action]").forEach(btn => {
      btn.onclick = (e) => {
        e.stopPropagation();
        navigateToProject(btn.dataset.projectId, btn.dataset.cardAction);
      };
    });
  }

  function navigateToProject(projectId, view) {
    state.projectId = projectId;
    state.view = view;
    const picker = document.getElementById("projectPicker");
    if (picker) picker.value = projectId;
    document.querySelectorAll(".nav-link").forEach(l => {
      l.classList.toggle("active", l.dataset.view === view);
    });
    render();
  }

  // Show the project-scoped sidebar section only when the user is inside
  // a project (anything other than the all-projects Overview and the
  // tenant-admin views). The label updates to the active project name so
  // it's clear whose Issues / Documents / etc. you're navigating.
  const PROJECT_SCOPED_VIEWS = new Set([
    "project-dashboard", "issues", "documents", "transmittals",
    "meetings", "recordings", "workflows", "warnings", "models", "schedule", "cost",
  ]);
  function syncSidebarScope() {
    const inProject = PROJECT_SCOPED_VIEWS.has(state.view);
    document.body.classList.toggle("in-project", inProject);
    const label = document.getElementById("navProjectLabel");
    if (label) {
      const p = (state.projects || []).find(p => p.id === state.projectId);
      label.textContent = p ? (p.code || p.name || "Project") : "Project";
    }
  }

  // ── Project dashboard ────────────────────────────────────────────────
  // Single-project landing page. Reached by clicking a project card or
  // the Dashboard footer button. The all-projects "Overview" stays clean
  // and high-level; the per-project information that used to live on
  // the cards is shown in full here against /api/projects/{id}/dashboard.

  async function renderProjectDashboard(main) {
    if (!state.projectId) {
      main.innerHTML = `<div class="empty">No project selected.</div>`;
      return;
    }
    const id = state.projectId;
    let d;
    try {
      d = await api(`/api/projects/${id}/dashboard`);
    } catch (e) {
      main.innerHTML = `<div class="empty">Could not load project dashboard: ${esc(String(e))}</div>`;
      return;
    }

    const summary = (state.projects || []).find(p => p.id === id) || {};
    const sk = statusKey(summary);
    const pct = Math.round(d.compliancePercent || 0);
    const colour = complianceColor(pct);
    const cpct = Math.round(d.containerCompliancePercent || 0);
    const ccolour = complianceColor(cpct);
    const totalEls = d.totalElements || 0;
    const taggedEls = d.taggedElements || 0;
    const untaggedEls = Math.max(0, totalEls - taggedEls);
    const loc = summary.city
      ? `${esc(summary.city)}${summary.country ? ", " + esc(summary.country) : ""}`
      : "Location not set";

    main.innerHTML = `
      <div class="pd-header">
        <button class="pd-back" id="pdBack">← All projects</button>
        <div class="pd-title-row">
          <div>
            <div class="pd-eyebrow">
              <span class="code-chip">${esc(d.code || summary.code || "—")}</span>
              <span class="status-chip ${sk}">${statusLabel(sk)}</span>
              <span class="pd-loc">📍 ${loc}</span>
              <span class="pd-phase">Phase: ${esc(d.phase || summary.phase || "—")}</span>
            </div>
            <h1 class="pd-name">${esc(d.name || summary.name || "Project")}</h1>
          </div>
          <div class="pd-meta">
            <div>Last sync: <strong>${timeAgo(summary.lastSyncAt)}</strong></div>
            <div>Members: <strong>${summary.memberCount ?? 0}</strong></div>
            <button class="pd-more-btn" id="pdMoreBtn" title="Project actions">⋯</button>
            <div class="pd-more-menu" id="pdMoreMenu" hidden>
              <button data-pd-action="archive">🗄 Archive project</button>
            </div>
          </div>
        </div>
      </div>
      <div id="modal-mount"></div>

      <div class="kpi-grid">
        <div class="kpi ${colour}">
          <div class="value">${pct}%</div>
          <div class="label">Tag compliance</div>
          <div class="kpi-bar"><div class="kpi-bar-fill ${colour}" style="width:${pct}%"></div></div>
        </div>
        <div class="kpi ${ccolour}">
          <div class="value">${cpct}%</div>
          <div class="label">Container compliance</div>
          <div class="kpi-bar"><div class="kpi-bar-fill ${ccolour}" style="width:${cpct}%"></div></div>
        </div>
        <div class="kpi"><div class="value">${taggedEls.toLocaleString()}</div><div class="label">Tagged elements</div><div class="kpi-sub">of ${totalEls.toLocaleString()} · ${untaggedEls.toLocaleString()} untagged</div></div>
        <div class="kpi ${d.openIssues ? 'amber' : 'green'}"><div class="value">${d.openIssues || 0}</div><div class="label">Open issues</div></div>
        <div class="kpi ${d.overdueIssues ? 'red' : ''}"><div class="value">${d.overdueIssues || 0}</div><div class="label">Overdue</div></div>
        <div class="kpi ${d.criticalIssues ? 'red' : ''}"><div class="value">${d.criticalIssues || 0}</div><div class="label">Critical</div></div>
        <div class="kpi"><div class="value">${d.documents || 0}</div><div class="label">Documents</div></div>
        <div class="kpi ${d.warningCount ? 'amber' : ''}"><div class="value">${d.warningCount || 0}</div><div class="label">Warnings</div></div>
      </div>

      <div class="pd-grid">
        <div class="card">
          <div class="card-head">
            <h3>Compliance trend (30 days)</h3>
            <a href="#" class="pd-link" data-jump="workflows">Workflow history →</a>
          </div>
          ${trendSparklineHtml(d.complianceTrend || [])}
        </div>

        <div class="card">
          <div class="card-head">
            <h3>Recent issues</h3>
            <a href="#" class="pd-link" data-jump="issues">All issues →</a>
          </div>
          ${recentIssuesHtml(d.recentIssues || [])}
        </div>

        <div class="card">
          <div class="card-head">
            <h3>Recent workflow runs</h3>
            <a href="#" class="pd-link" data-jump="workflows">All runs →</a>
          </div>
          ${recentWorkflowsHtml(d.recentWorkflows || [])}
        </div>

        <div class="card">
          <div class="card-head">
            <h3>Quick links</h3>
          </div>
          <div class="pd-quicklinks">
            <button data-jump="documents">📄 Documents (${d.documents || 0})</button>
            <button data-jump="issues">⚠ Issues (${d.openIssues || 0})</button>
            <button data-jump="transmittals">📤 Transmittals</button>
            <button data-jump="meetings">📅 Meetings</button>
            <button data-jump="recordings">🎬 Recordings</button>
            <button data-jump="warnings">🚧 Warnings (${d.warningCount || 0})</button>
            <button data-jump="models">🧊 3D models</button>
            <button data-jump="schedule">📊 Schedule</button>
            <button data-jump="cost">💰 Cost</button>
          </div>
        </div>
      </div>
    `;

    document.getElementById("pdBack").onclick = () => {
      state.view = "overview";
      document.querySelectorAll(".nav-link").forEach(l => {
        l.classList.toggle("active", l.dataset.view === "overview");
      });
      render();
    };

    document.querySelectorAll("[data-jump]").forEach(b => {
      b.onclick = (e) => {
        e.preventDefault();
        navigateToProject(id, b.dataset.jump);
      };
    });

    // ⋯ menu — opens a small popover with destructive actions
    // gated behind a confirm modal that requires the project code to be
    // retyped. Hidden behind the menu so a stray click can't archive.
    const moreBtn = document.getElementById("pdMoreBtn");
    const moreMenu = document.getElementById("pdMoreMenu");
    if (moreBtn && moreMenu) {
      moreBtn.onclick = (e) => {
        e.stopPropagation();
        moreMenu.hidden = !moreMenu.hidden;
      };
      document.addEventListener("click", () => { moreMenu.hidden = true; }, { once: true });
      moreMenu.querySelectorAll("[data-pd-action]").forEach(b => {
        b.onclick = (e) => {
          e.stopPropagation();
          moreMenu.hidden = true;
          if (b.dataset.pdAction === "archive") openArchiveModal(d);
        };
      });
    }
  }

  function openArchiveModal(project) {
    const mount = document.getElementById("modal-mount");
    if (!mount) return;
    const code = project.code || "";
    mount.innerHTML = `
      <div class="modal-overlay" id="archiveOverlay">
        <div class="modal-box">
          <h2>Archive project</h2>
          <p style="color:var(--muted);font-size:13px;margin:0 0 12px">
            This will move <strong>${esc(project.name || "this project")}</strong>
            to the Completed list. Members keep access; nothing is deleted,
            but the project stops counting toward active-project totals.
          </p>
          <p style="color:var(--muted);font-size:13px;margin:0 0 8px">
            To confirm, type the project code <code style="background:var(--slate-100);padding:1px 6px;border-radius:4px">${esc(code)}</code> below.
          </p>
          <div class="field">
            <input id="archiveConfirm" type="text" placeholder="${esc(code)}" autocomplete="off" />
          </div>
          <div class="actions">
            <button type="button" class="btn-cancel" id="archiveCancel">Cancel</button>
            <button type="button" class="btn-primary" id="archiveGo" disabled style="background:var(--danger)">Archive project</button>
          </div>
        </div>
      </div>
    `;
    const overlay = document.getElementById("archiveOverlay");
    const close = () => { mount.innerHTML = ""; };
    document.getElementById("archiveCancel").onclick = close;
    overlay.onclick = (e) => { if (e.target === overlay) close(); };

    const input = document.getElementById("archiveConfirm");
    const go    = document.getElementById("archiveGo");
    input.addEventListener("input", () => {
      go.disabled = input.value.trim().toUpperCase() !== code.toUpperCase();
    });
    input.focus();

    go.onclick = async () => {
      go.disabled = true;
      try {
        await api(`/api/projects/${project.id}?confirmCode=${encodeURIComponent(code)}`, {
          method: "DELETE",
        });
        close();
        showToast("Project archived");
        state.projects = await api("/api/projects");
        state.view = "overview";
        document.querySelectorAll(".nav-link").forEach(l => {
          l.classList.toggle("active", l.dataset.view === "overview");
        });
        render();
      } catch (e) {
        go.disabled = false;
        showToast("Could not archive — check your permissions and try again.");
      }
    };
  }

  function recentIssuesHtml(rows) {
    if (!rows.length) return `<div class="empty">No issues yet.</div>`;
    return `<table class="pd-table"><thead><tr>
        <th>Code</th><th>Title</th><th>Priority</th><th>Status</th><th>Age</th>
      </tr></thead><tbody>
      ${rows.map(i => `
        <tr class="${i.isOverdue ? 'pd-overdue' : ''}">
          <td>${esc(i.issueCode || "")}</td>
          <td>${esc(i.title || "")}</td>
          <td>${chip(i.priority)}</td>
          <td>${chip(i.status)}</td>
          <td>${i.daysOpen ?? 0}d${i.isOverdue ? ' · <span class="pd-overdue-tag">overdue</span>' : ''}</td>
        </tr>`).join("")}
      </tbody></table>`;
  }

  function recentWorkflowsHtml(rows) {
    if (!rows.length) return `<div class="empty">No workflow runs yet.</div>`;
    return `<table class="pd-table"><thead><tr>
        <th>Preset</th><th>✓</th><th>✗</th><th>Before → After</th><th>When</th>
      </tr></thead><tbody>
      ${rows.slice(0, 6).map(w => {
        const before = w.complianceBefore != null ? Math.round(w.complianceBefore) + "%" : "—";
        const after  = w.complianceAfter  != null ? Math.round(w.complianceAfter)  + "%" : "—";
        return `<tr>
          <td>${esc(w.preset || "")}</td>
          <td style="color:var(--green)">${w.stepsPassed ?? 0}</td>
          <td style="color:var(--red)">${w.stepsFailed ?? 0}</td>
          <td>${before} → <strong>${after}</strong></td>
          <td>${fmtDate(w.executedAt)}</td>
        </tr>`;
      }).join("")}
      </tbody></table>`;
  }

  function trendSparklineHtml(points) {
    if (!points || points.length < 2) {
      return `<div class="empty">Not enough data for a trend yet — sync your project to start tracking.</div>`;
    }
    const w = 600, h = 120, pad = 8;
    const xs = points.map((_, i) => pad + (i * (w - 2 * pad)) / Math.max(1, points.length - 1));
    const tagY = points.map(p => h - pad - ((p.tagPercent || 0) / 100) * (h - 2 * pad));
    const conY = points.map(p => h - pad - ((p.containerPercent || 0) / 100) * (h - 2 * pad));
    const tagPath = xs.map((x, i) => `${i === 0 ? "M" : "L"}${x.toFixed(1)},${tagY[i].toFixed(1)}`).join(" ");
    const conPath = xs.map((x, i) => `${i === 0 ? "M" : "L"}${x.toFixed(1)},${conY[i].toFixed(1)}`).join(" ");
    const last = points[points.length - 1];
    const first = points[0];
    const delta = ((last.tagPercent || 0) - (first.tagPercent || 0));
    const deltaTxt = (delta >= 0 ? "+" : "") + delta.toFixed(1) + "%";
    const deltaCol = delta >= 0 ? "var(--green)" : "var(--red)";
    return `
      <div class="pd-trend">
        <div class="pd-trend-summary">
          <span><span class="pd-dot" style="background:#3B82F6"></span> Tag ${Math.round(last.tagPercent || 0)}%</span>
          <span><span class="pd-dot" style="background:#22C55E"></span> Container ${Math.round(last.containerPercent || 0)}%</span>
          <span style="color:${deltaCol};font-weight:600">Δ ${deltaTxt}</span>
        </div>
        <svg viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" class="pd-trend-svg">
          <path d="${tagPath}" fill="none" stroke="#3B82F6" stroke-width="2"/>
          <path d="${conPath}" fill="none" stroke="#22C55E" stroke-width="2" stroke-dasharray="4 3"/>
        </svg>
      </div>`;
  }

  // ── New Project modal + toast ────────────────────────────────────────

  function openNewProjectModal() {
    const mount = document.getElementById("modal-mount");
    if (!mount) return;
    mount.innerHTML = `
      <div class="modal-overlay" id="modalOverlay">
        <div class="modal-box">
          <h2>New Project</h2>
          <form id="newProjectForm">
            <div class="field">
              <label>Project Name *</label>
              <input id="np_name" type="text" required />
            </div>
            <div class="field">
              <label>Project Code *</label>
              <input id="np_code" type="text" class="uppercase" required placeholder="e.g. NHW-2026" />
            </div>
            <div class="field">
              <label>Phase</label>
              <select id="np_phase">
                <option>Design</option>
                <option>Execution</option>
                <option>Handover</option>
              </select>
            </div>
            <div class="field">
              <label>City</label>
              <input id="np_city" type="text" />
            </div>
            <div class="field">
              <label>Country</label>
              <input id="np_country" type="text" />
            </div>
            <div class="actions">
              <button type="button" class="btn-cancel" id="np_cancel">Cancel</button>
              <button type="submit" class="btn-primary">Create Project</button>
            </div>
          </form>
        </div>
      </div>
    `;
    const overlay = document.getElementById("modalOverlay");
    const close = () => { mount.innerHTML = ""; };
    document.getElementById("np_cancel").onclick = close;
    overlay.onclick = (e) => { if (e.target === overlay) close(); };
    document.getElementById("newProjectForm").onsubmit = async (ev) => {
      ev.preventDefault();
      const name    = document.getElementById("np_name").value.trim();
      const code    = document.getElementById("np_code").value.trim().toUpperCase();
      const phase   = document.getElementById("np_phase").value;
      const city    = document.getElementById("np_city").value.trim();
      const country = document.getElementById("np_country").value.trim();
      try {
        await api("/api/projects", {
          method: "POST",
          body: JSON.stringify({ name, code, phase, city, country }),
        });
        close();
        state.projects = await api("/api/projects");
        const main = document.getElementById("main");
        if (state.view === "overview") renderOverview(main);
        showToast("Project created ✓");
      } catch (e) {
        showToast("Could not create project");
      }
    };
  }

  function showToast(msg) {
    const el = document.createElement("div");
    el.className = "toast";
    el.textContent = msg;
    document.body.appendChild(el);
    setTimeout(() => el.remove(), 3200);
  }

  // ── Mapbox project map ───────────────────────────────────────────────

  function initProjectsMap(projects) {
    const container = document.getElementById("projects-map");
    if (!container) return;

    if (!window.mapboxgl) {
      container.outerHTML = `<div class="map-fallback">
        <h4>Map could not load</h4>
        <p>Mapbox GL JS failed to load. Check your network or CSP.</p>
      </div>`;
      return;
    }
    if (!CONFIG.mapboxToken || CONFIG.mapboxToken === "PLANSCAPE_MAPBOX_TOKEN") {
      container.outerHTML = `<div class="map-fallback">
        <h4>Map view requires a Mapbox token</h4>
        <p>Replace <code>PLANSCAPE_MAPBOX_TOKEN</code> in <code>js/dashboard.js</code> with a real token from <a href="https://account.mapbox.com/access-tokens/" target="_blank" rel="noopener">mapbox.com</a> (free tier — no credit card required).</p>
      </div>`;
      return;
    }

    mapboxgl.accessToken = CONFIG.mapboxToken;
    const map = new mapboxgl.Map({
      container: "projects-map",
      style: "mapbox://styles/mapbox/dark-v11",
      center: [20, 5],
      zoom: 3.2,
    });
    state.mapInstance = map;
    map.addControl(new mapboxgl.NavigationControl(), "top-right");

    const located = projects.filter(p => p.latitude != null && p.longitude != null);
    if (located.length === 0) return;

    const bounds = new mapboxgl.LngLatBounds();

    for (const p of located) {
      const sk = statusKey(p);
      const el = document.createElement("div");
      el.className = `map-marker ${sk}`;

      const pct = Math.round(p.compliancePercent || 0);
      const colour = complianceColor(pct);
      const barColour = colour === "green" ? "#22C55E" : colour === "amber" ? "#F59E0B" : "#EF4444";
      const popupHtml = `
        <div class="popup-body">
          <div class="top-row">
            <span class="pop-status ${sk}">${statusLabel(sk)}</span>
            <span class="pop-code">${esc(p.code || "")}</span>
          </div>
          <h4>${esc(p.name || "")}</h4>
          <div class="pop-loc">📍 ${esc(p.city || "")}${p.country ? ", " + esc(p.country) : ""}</div>
          <hr>
          <div>Compliance <strong style="float:right">${pct}%</strong></div>
          <div class="pop-bar"><div class="pop-bar-fill" style="width:${pct}%;background:${barColour}"></div></div>
          <div class="pop-stats">
            <span>Open Issues ${p.warningCount || 0}</span>
            <span>Phase: ${esc(p.phase || "—")}</span>
            <span>Team: ${p.memberCount ?? 0} members</span>
            <span>Last sync: ${timeAgo(p.lastSyncAt)}</span>
          </div>
          <hr>
          <button class="pop-open-btn" data-pop-open-id="${esc(p.id)}">Open Project →</button>
        </div>
      `;

      const popup = new mapboxgl.Popup({ offset: 24, closeButton: true })
        .setHTML(popupHtml);

      // After Mapbox injects the popup HTML, wire up the open button.
      popup.on("open", () => {
        const btn = document.querySelector(`[data-pop-open-id="${p.id}"]`);
        if (btn) btn.onclick = () => {
          popup.remove();
          navigateToProject(p.id, "project-dashboard");
        };
      });

      new mapboxgl.Marker({ element: el })
        .setLngLat([p.longitude, p.latitude])
        .setPopup(popup)
        .addTo(map);

      bounds.extend([p.longitude, p.latitude]);
    }

    if (located.length > 1) {
      map.fitBounds(bounds, { padding: 60, maxZoom: 6, duration: 0 });
    } else {
      map.setCenter([located[0].longitude, located[0].latitude]);
      map.setZoom(5);
    }
  }

  // ── New Project modal + toast ────────────────────────────────────────

  function openNewProjectModal() {
    const mount = document.getElementById("modal-mount");
    if (!mount) return;
    mount.innerHTML = `
      <div class="modal-overlay" id="modalOverlay">
        <div class="modal-box">
          <h2>New Project</h2>
          <form id="newProjectForm">
            <div class="field">
              <label>Project Name *</label>
              <input id="np_name" type="text" required />
            </div>
            <div class="field">
              <label>Project Code *</label>
              <input id="np_code" type="text" class="uppercase" required placeholder="e.g. NHW-2026" />
            </div>
            <div class="field">
              <label>Phase</label>
              <select id="np_phase">
                <option>Design</option>
                <option>Execution</option>
                <option>Handover</option>
              </select>
            </div>
            <div class="field">
              <label>City</label>
              <input id="np_city" type="text" />
            </div>
            <div class="field">
              <label>Country</label>
              <input id="np_country" type="text" />
            </div>
            <div class="actions">
              <button type="button" class="btn-cancel" id="np_cancel">Cancel</button>
              <button type="submit" class="btn-primary">Create Project</button>
            </div>
          </form>
        </div>
      </div>
    `;
    const overlay = document.getElementById("modalOverlay");
    const close = () => { mount.innerHTML = ""; };
    document.getElementById("np_cancel").onclick = close;
    overlay.onclick = (e) => { if (e.target === overlay) close(); };
    document.getElementById("newProjectForm").onsubmit = async (ev) => {
      ev.preventDefault();
      const name    = document.getElementById("np_name").value.trim();
      const code    = document.getElementById("np_code").value.trim().toUpperCase();
      const phase   = document.getElementById("np_phase").value;
      const city    = document.getElementById("np_city").value.trim();
      const country = document.getElementById("np_country").value.trim();
      try {
        await api("/api/projects", {
          method: "POST",
          body: JSON.stringify({ name, code, phase, city, country }),
        });
        close();
        state.projects = await api("/api/projects");
        const main = document.getElementById("main");
        if (state.view === "overview") renderOverview(main);
        showToast("Project created ✓");
      } catch (e) {
        showToast("Could not create project");
      }
    };
  }

  function showToast(msg) {
    const el = document.createElement("div");
    el.className = "toast";
    el.textContent = msg;
    document.body.appendChild(el);
    setTimeout(() => el.remove(), 3200);
  }

  async function renderList(main, title, path, columns) {
    const rows = await api(path);
    const arr = Array.isArray(rows) ? rows : (rows?.items || []);
    main.innerHTML = `<h1>${esc(title)}</h1><div class="card">${tableHtml(arr, columns)}</div>`;
  }

  async function renderModels(main) {
    const rows = (await api(`/api/projects/${state.projectId}/models`)) || [];
    main.innerHTML = `
      <h1>3D models</h1>
      ${rows.length === 0 ? `<div class="empty">No models published yet. Use the Revit plugin's "Publish Model to Planscape" command.</div>` : ""}
      <div class="kpi-grid">
        ${rows.map(m => `
          <div class="card" style="margin-bottom:0" data-model-id="${m.id}">
            <div style="display:flex;align-items:center;gap:10px;margin-bottom:8px">
              <span style="font-size:24px">${m.format === "Glb" || m.format === "Gltf" ? "🧊" : "📦"}</span>
              <strong>${esc(m.name)}</strong>
            </div>
            <div style="font-size:12px;color:#666">${esc(m.format)} · ${(m.fileSizeBytes/1024/1024).toFixed(1)} MB${m.discipline ? " · " + esc(m.discipline) : ""}</div>
            <div style="margin-top:10px;display:flex;gap:8px;flex-wrap:wrap">
              <button class="ghost" style="color:var(--primary);border-color:var(--primary)"
                data-action="open" data-id="${m.id}">Open in viewer</button>
              <button class="ghost" style="color:var(--danger);border-color:var(--danger)"
                data-action="delete" data-id="${m.id}" data-name="${esc(m.name)}">🗑 Delete</button>
            </div>
          </div>`).join("")}
      </div>
      <div id="modal-mount"></div>`;

    main.querySelectorAll('[data-action="open"]').forEach(b => {
      b.onclick = () => window.open(`/viewer.html?project=${state.projectId}&model=${b.dataset.id}`, "_blank");
    });
    main.querySelectorAll('[data-action="delete"]').forEach(b => {
      b.onclick = () => openDeleteModelModal(main, { id: b.dataset.id, name: b.dataset.name });
    });
  }

  // Site photos — gallery over the existing /photos API. Photo bytes are
  // auth-gated, so a bare <img src> can't carry the Bearer token; each tile
  // lazy-fetches the file as a blob on scroll-into-view and swaps in an
  // object URL (the same pattern the coordination viewer uses for models).
  const PHOTO_REASONS = ["Reference", "Progress", "Defect", "Safety", "QA", "Handover"];

  async function renderPhotos(main) {
    main.innerHTML = `
      <h1>Site photos</h1>
      <div class="photo-toolbar">
        <label>Album
          <select id="photoAlbum"><option value="">All photos</option></select>
        </label>
        <button id="photoUploadBtn" class="btn-primary">＋ Upload photo</button>
      </div>
      <div id="photoUploadMount"></div>
      <div id="photoGrid"><div class="empty">Loading…</div></div>`;

    // Album dropdown (non-blocking).
    api(`/api/projects/${state.projectId}/photo-albums`).then(albums => {
      const list = Array.isArray(albums) ? albums : (albums && albums.items) || [];
      const sel = document.getElementById("photoAlbum");
      if (!sel) return;
      list.forEach(a => {
        const o = document.createElement("option");
        o.value = a.id;
        o.textContent = `${a.name}${a.photoCount != null ? " (" + a.photoCount + ")" : ""}`;
        sel.appendChild(o);
      });
      sel.onchange = () => loadPhotoGrid(sel.value);
    }).catch(() => {});

    document.getElementById("photoUploadBtn").onclick = () => openCaptureForm();
    await loadPhotoGrid("");
  }

  // Render the grid for "all photos" (albumId empty) or a single album.
  async function loadPhotoGrid(albumId) {
    const grid = document.getElementById("photoGrid");
    if (!grid) return;
    grid.innerHTML = `<div class="empty">Loading…</div>`;
    try {
      let items;
      if (albumId) {
        const a = await api(`/api/projects/${state.projectId}/photo-albums/${albumId}`);
        items = (a && a.photos) || [];
      } else {
        const data = await api(`/api/projects/${state.projectId}/photos?pageSize=60`);
        items = (data && data.items) || [];
      }
      grid.innerHTML = items.length === 0
        ? `<div class="empty">No photos here yet. Capture from the mobile app or use “Upload photo”.</div>`
        : `<div class="photo-grid">${items.map(photoCard).join("")}</div>`;
      lazyLoadPhotos(grid);
    } catch (e) {
      grid.innerHTML = `<div class="empty">Could not load photos: ${esc(String(e))}</div>`;
    }
  }

  // Capture-from-web: multipart POST to /photos/capture (the same endpoint
  // the mobile app uses). Best-effort browser geolocation tags lat/long.
  function openCaptureForm() {
    const mount = document.getElementById("photoUploadMount");
    if (!mount) return;
    mount.innerHTML = `
      <div class="card photo-capture">
        <div class="field"><label>Photo</label><input type="file" id="capFile" accept="image/*" capture="environment" /></div>
        <div class="field"><label>Caption</label><input type="text" id="capCaption" placeholder="What does this show?" /></div>
        <div class="capture-row">
          <label>Reason<select id="capReason">${PHOTO_REASONS.map(r => `<option>${r}</option>`).join("")}</select></label>
          <label>Level<input type="text" id="capLevel" placeholder="L02" /></label>
          <label>Zone<input type="text" id="capZone" placeholder="Z01" /></label>
        </div>
        <label class="capture-gps"><input type="checkbox" id="capGps" checked /> Tag GPS location</label>
        <div class="actions">
          <button class="btn-cancel" id="capCancel">Cancel</button>
          <button class="btn-primary" id="capSubmit">Upload</button>
        </div>
        <p id="capStatus" class="error"></p>
      </div>`;
    document.getElementById("capCancel").onclick = () => { mount.innerHTML = ""; };
    document.getElementById("capSubmit").onclick = submitCapture;
  }

  async function submitCapture() {
    const fileEl = document.getElementById("capFile");
    const status = document.getElementById("capStatus");
    const btn = document.getElementById("capSubmit");
    status.textContent = "";
    const file = fileEl && fileEl.files && fileEl.files[0];
    if (!file) { status.textContent = "Pick an image first."; return; }
    btn.disabled = true; btn.textContent = "Uploading…";
    try {
      const fd = new FormData();
      fd.append("File", file, file.name);
      fd.append("Reason", document.getElementById("capReason").value || "Reference");
      const cap = document.getElementById("capCaption").value.trim(); if (cap) fd.append("Caption", cap);
      const lvl = document.getElementById("capLevel").value.trim();   if (lvl) fd.append("LevelCode", lvl);
      const zone = document.getElementById("capZone").value.trim();   if (zone) fd.append("ZoneCode", zone);
      fd.append("Source", "web");
      if (document.getElementById("capGps").checked) {
        const pos = await getGeo().catch(() => null);
        if (pos) { fd.append("Latitude", String(pos.lat)); fd.append("Longitude", String(pos.lng)); if (pos.acc) fd.append("AccuracyM", String(pos.acc)); }
      }
      const res = await fetch(`/api/projects/${state.projectId}/photos/capture`, {
        method: "POST",
        headers: { "Authorization": "Bearer " + getToken() }, // no Content-Type — browser sets the multipart boundary
        body: fd,
      });
      if (!res.ok) throw new Error("HTTP " + res.status + " " + await res.text());
      document.getElementById("photoUploadMount").innerHTML = "";
      const sel = document.getElementById("photoAlbum");
      await loadPhotoGrid(sel ? sel.value : "");
    } catch (e) {
      btn.disabled = false; btn.textContent = "Upload";
      status.textContent = "Upload failed — check permissions and try again.";
    }
  }

  function getGeo() {
    return new Promise((resolve, reject) => {
      if (!navigator.geolocation) return reject();
      navigator.geolocation.getCurrentPosition(
        p => resolve({ lat: p.coords.latitude, lng: p.coords.longitude, acc: p.coords.accuracy }),
        reject, { enableHighAccuracy: true, timeout: 8000 });
    });
  }

  function photoCard(p) {
    const when = fmtDate(p.capturedAt);
    const where = [p.levelCode, p.zoneCode].filter(Boolean).join(" · ");
    const gps = (p.latitude != null && p.longitude != null)
      ? `<a href="https://maps.google.com/?q=${p.latitude},${p.longitude}" target="_blank" rel="noopener">📍 ${Number(p.latitude).toFixed(5)}, ${Number(p.longitude).toFixed(5)}</a>`
      : "";
    const aud = p.audience ? `<span class="badge">${esc(p.audience)}</span>` : "";
    return `
      <div class="card photo-card">
        <div class="photo-thumb" data-photo-id="${esc(p.id)}"><div class="photo-spinner">Loading…</div></div>
        <div class="photo-meta">
          <div class="photo-cap">${esc(p.caption || "(no caption)")}</div>
          <div class="photo-sub">${esc(when)}${where ? " · " + esc(where) : ""} ${aud}</div>
          ${gps ? `<div class="photo-sub">${gps}</div>` : ""}
        </div>
      </div>`;
  }

  function lazyLoadPhotos(main) {
    const tiles = Array.from(main.querySelectorAll(".photo-thumb[data-photo-id]"));
    if (tiles.length === 0) return;
    const load = async (tile) => {
      const id = tile.getAttribute("data-photo-id");
      if (!id) return;
      tile.removeAttribute("data-photo-id"); // handled once
      try {
        const res = await fetch(`/api/projects/${state.projectId}/photos/${id}/file`,
          { headers: { Authorization: "Bearer " + getToken() } });
        if (!res.ok) throw new Error("HTTP " + res.status);
        const obj = URL.createObjectURL(await res.blob());
        const img = document.createElement("img");
        img.src = obj; img.alt = ""; img.onclick = () => window.open(obj, "_blank");
        tile.innerHTML = ""; tile.appendChild(img);
      } catch (e) {
        tile.innerHTML = `<div class="photo-err">⚠ unavailable</div>`;
      }
    };
    if ("IntersectionObserver" in window) {
      const io = new IntersectionObserver((entries) => {
        entries.forEach(en => { if (en.isIntersecting) { io.unobserve(en.target); load(en.target); } });
      }, { rootMargin: "200px" });
      tiles.forEach(t => io.observe(t));
    } else {
      tiles.forEach(load);
    }
  }

  // Wrong-model rescue: the Revit plugin SHA-256-dedups uploads, so a
  // user who published the wrong file is locked out of republishing the
  // *correct* file with the same geometry until the existing entry is
  // gone. Mirrors the Archive-project confirm flow — retype the file
  // name so a slip of the mouse can't wipe a real publish.
  function openDeleteModelModal(main, model) {
    const mount = main.querySelector("#modal-mount") || document.getElementById("modal-mount");
    if (!mount) return;
    const name = model.name || "this model";
    mount.innerHTML = `
      <div class="modal-overlay" id="delModelOverlay">
        <div class="modal-box">
          <h2>Delete 3D model</h2>
          <p style="color:var(--muted);font-size:13px;margin:0 0 12px">
            This will remove <strong>${esc(name)}</strong> from this project.
            The Revit plugin's SHA-256 dedup will then accept a republish
            of the same geometry — useful when the wrong file was
            published. The bytes are soft-deleted on the server and can
            be recovered by an admin.
          </p>
          <p style="color:var(--muted);font-size:13px;margin:0 0 8px">
            To confirm, type the model name <code style="background:var(--slate-100);padding:1px 6px;border-radius:4px">${esc(name)}</code> below.
          </p>
          <div class="field">
            <input id="delModelConfirm" type="text" placeholder="${esc(name)}" autocomplete="off" />
          </div>
          <div class="actions">
            <button type="button" class="btn-cancel" id="delModelCancel">Cancel</button>
            <button type="button" class="btn-primary" id="delModelGo" disabled style="background:var(--danger)">Delete model</button>
          </div>
        </div>
      </div>`;
    const overlay = document.getElementById("delModelOverlay");
    const close = () => { mount.innerHTML = ""; };
    document.getElementById("delModelCancel").onclick = close;
    overlay.onclick = (e) => { if (e.target === overlay) close(); };

    const input = document.getElementById("delModelConfirm");
    const go    = document.getElementById("delModelGo");
    input.addEventListener("input", () => {
      go.disabled = input.value.trim() !== name;
    });
    input.focus();

    go.onclick = async () => {
      go.disabled = true;
      try {
        await api(`/api/projects/${state.projectId}/models/${model.id}`, { method: "DELETE" });
        close();
        showToast("Model deleted ✓");
        renderModels(main);
      } catch (e) {
        go.disabled = false;
        showToast("Could not delete — check your permissions and try again.");
      }
    };
  }

  async function renderCost(main) {
    const rows = await api(`/api/projects/${state.projectId}/cost/items`);
    const summary = await api(`/api/projects/${state.projectId}/cost/summary`);
    main.innerHTML = `
      <h1>Cost</h1>
      <div class="kpi-grid">
        ${(summary || []).map(s => `
          <div class="kpi"><div class="value">${fmt(s.total, s.currency || "GBP")}</div>
            <div class="label">${esc(s.kind)} · ${esc(s.discipline || "All")}</div></div>`).join("")}
      </div>
      <div class="card">${tableHtml(rows, costColumns)}</div>`;
  }

  // ── Column specs ──────────────────────────────────────────────────────

  const issueColumns = [
    { k: "issueCode",  label: "Code" },
    { k: "title",      label: "Title" },
    { k: "type",       label: "Type" },
    { k: "priority",   label: "Priority", render: (v) => chip(v) },
    { k: "status",     label: "Status",   render: (v) => chip(v) },
    { k: "assignee",   label: "Assignee" },
    { k: "createdAt",  label: "Created",  render: (v) => fmtDate(v) },
  ];
  const docColumns = [
    { k: "fileName",      label: "File" },
    { k: "documentType",  label: "Type" },
    { k: "discipline",    label: "Disc" },
    { k: "cdeStatus",     label: "CDE" },
    { k: "suitabilityCode", label: "Suit." },
    { k: "revision",      label: "Rev" },
    { k: "uploadedAt",    label: "Uploaded", render: (v) => fmtDate(v) },
  ];
  const tmxColumns = [
    { k: "transmittalCode", label: "Code" },
    { k: "title",           label: "Title" },
    { k: "recipients",      label: "To" },
    { k: "status",          label: "Status" },
    { k: "sentAt",          label: "Sent", render: (v) => fmtDate(v) },
  ];
  const meetingColumns = [
    { k: "title",         label: "Title" },
    { k: "type",          label: "Type" },
    { k: "scheduledAt",   label: "When", render: (v) => fmtDate(v) },
    { k: "durationMinutes", label: "Duration (m)" },
    { k: "location",      label: "Location" },
  ];

  // ── Recordings (web /app — mirrors the mobile Expo recordings UI) ──────────
  function fmtDur(s) { if (!s || s <= 0) return "—"; const m = Math.floor(s / 60), ss = Math.round(s % 60); return m + ":" + String(ss).padStart(2, "0"); }
  function fmtSize(b) { if (!b || b <= 0) return "—"; return b >= 1048576 ? (b / 1048576).toFixed(1) + " MB" : (b / 1024).toFixed(0) + " KB"; }
  function fmtDateTime(v) { if (!v) return ""; try { return new Date(v).toLocaleString(); } catch { return v; } }
  function isAudio(k) { return k === "audio-only" || k === "audio"; }

  // In-browser player: HTML5 <video> (mp4) / <audio> (audio-only egress) over the
  // short-lived presigned URL (self-authenticating — no Bearer needed). Body-level
  // overlay so it never clobbers an open recordings modal.
  function openRecordingPlayer(rec) {
    const audio = isAudio(rec.kind);
    const url = rec.downloadUrl || "";
    const ov = document.createElement("div");
    ov.className = "modal-overlay";
    ov.style.zIndex = "9999";
    ov.innerHTML =
      '<div class="modal-box" style="max-width:760px">' +
        '<h2 style="margin-bottom:10px">' + (audio ? "🎙 Audio recording" : "🎥 Recording") + "</h2>" +
        (audio
          ? '<audio src="' + esc(url) + '" controls autoplay style="width:100%"></audio>'
          : '<video src="' + esc(url) + '" controls autoplay style="width:100%;max-height:60vh;background:#000;border-radius:8px"></video>') +
        '<div class="actions" style="margin-top:12px">' +
          '<a class="ghost" href="' + esc(url) + '" target="_blank" rel="noopener" style="text-decoration:none">⬇ Download</a>' +
          '<button type="button" class="btn-cancel" id="recPlayClose">Close</button>' +
        "</div>" +
      "</div>";
    document.body.appendChild(ov);
    const close = () => { try { ov.remove(); } catch (e) {} };
    ov.querySelector("#recPlayClose").onclick = close;
    ov.onclick = (e) => { if (e.target === ov) close(); };
  }

  // A recording row's action cell: ▶ Play (COMPLETE + url only) + ⬇ Download; else status.
  function recActionsHtml(r) {
    if (r.status === "COMPLETE" && r.downloadUrl) {
      return '<button class="ghost rec-play" data-url="' + esc(r.downloadUrl) + '" data-kind="' + esc(r.kind) + '" ' +
        'style="color:var(--primary);border-color:var(--primary)">▶ Play</button> ' +
        '<a class="ghost" href="' + esc(r.downloadUrl) + '" target="_blank" rel="noopener" style="text-decoration:none">⬇ Download</a>';
    }
    return '<span style="color:var(--muted);font-size:12px">' +
      (r.status === "ACTIVE" || r.status === "STARTING" ? "recording…" : esc(r.status)) + "</span>";
  }
  function wireRecPlay(scope) {
    scope.querySelectorAll(".rec-play").forEach((b) => {
      b.onclick = () => openRecordingPlayer({ downloadUrl: b.dataset.url, kind: b.dataset.kind });
    });
  }
  async function fetchProjectRecordings() {
    // W3 — route recordings through the shared meetings-core (single source); fall back to
    // a direct call only if the module failed to load.
    try {
      var core = mcApi();
      if (core) return await core.listRecordings(state.projectId);
      const w = await api(`/api/projects/${state.projectId}/recordings`); return (w && w.recordings) || [];
    } catch (e) { return []; }
  }

  // Per-meeting recordings modal (the meeting-detail "Recordings" block).
  function openMeetingRecordings(main, meeting, recs) {
    const mount = main.querySelector("#modal-mount") || document.getElementById("modal-mount");
    if (!mount) return;
    const rows = recs.length
      ? '<table><tbody>' + recs.map((r) =>
          '<tr><td style="font-size:13px">' + (isAudio(r.kind) ? "🎙" : "🎥") + " " + fmtDateTime(r.startedAt) +
          " · " + fmtDur(r.durationSeconds) + " · " + fmtSize(r.fileSizeBytes) + " · " + esc(r.status) +
          '</td><td style="text-align:right;white-space:nowrap">' + recActionsHtml(r) + "</td></tr>").join("") + "</tbody></table>"
      : '<div class="empty">No recordings for this meeting.</div>';
    mount.innerHTML =
      '<div class="modal-overlay" id="recMeetOverlay"><div class="modal-box" style="max-width:580px">' +
        "<h2>" + esc((meeting && meeting.title) || "Meeting") + " — Recordings</h2>" + rows +
        '<div class="actions"><button type="button" class="btn-cancel" id="recMeetClose">Close</button></div>' +
      "</div></div>";
    document.getElementById("recMeetClose").onclick = () => { mount.innerHTML = ""; };
    const ov = document.getElementById("recMeetOverlay");
    ov.onclick = (e) => { if (e.target === ov) mount.innerHTML = ""; };
    wireRecPlay(mount);
  }

  // Meetings view — table with a ▶ REC badge on recorded rows; click a row to see its
  // recordings. (Replaces the generic renderList for meetings.)
  async function renderMeetings(main) {
    const [mRaw, recs] = await Promise.all([
      api(`/api/projects/${state.projectId}/meetings`),
      fetchProjectRecordings(),
    ]);
    const meetings = Array.isArray(mRaw) ? mRaw : (mRaw?.items || []);
    const byMeeting = {};
    recs.forEach((r) => { if (r.meetingId) (byMeeting[r.meetingId] = byMeeting[r.meetingId] || []).push(r); });
    const body = meetings.map((m) => {
      const badge = byMeeting[m.id]
        ? ' <span class="chip" style="background:rgba(25,118,210,0.15);color:#1976d2">▶ REC</span>' : "";
      return '<tr data-meeting-id="' + esc(m.id) + '" style="cursor:pointer">' +
        "<td>" + esc(m.title || "(untitled)") + badge + "</td>" +
        "<td>" + esc(m.type || m.meetingType || "") + "</td>" +
        "<td>" + fmtDate(m.scheduledAt) + "</td>" +
        "<td>" + (m.durationMinutes == null ? "" : esc(String(m.durationMinutes))) + "</td>" +
        "<td>" + esc(m.location || "") + "</td>" +
        '<td style="text-align:right"><button class="ghost m-join-list" data-id="' + esc(m.id) + '" style="color:var(--primary);border-color:var(--primary)">🎥 Join</button></td></tr>';
    }).join("");
    var canNew = mCan("schedule", null);
    main.innerHTML =
      '<div style="display:flex;align-items:center;gap:10px;margin-bottom:4px"><h1 style="margin:0;flex:1">Meetings</h1>' +
      (canNew ? '<button class="btn-primary" id="mNew">+ New meeting</button>' : "") + "</div>" +
      '<p style="color:var(--muted);font-size:13px;margin:-2px 0 12px">Click a meeting to open it (overview · agenda · actions · attendees · minutes · recordings).</p>' +
      '<div class="card">' + (meetings.length
        ? "<table><thead><tr><th>Title</th><th>Type</th><th>When</th><th>Duration (m)</th><th>Location</th><th></th></tr></thead><tbody>" + body + "</tbody></table>"
        : '<div class="empty">No meetings yet.</div>') + "</div>" +
      '<div id="modal-mount"></div>';
    main.querySelectorAll("tr[data-meeting-id]").forEach((tr) => {
      tr.onclick = () => { renderMeetingDetail(main, tr.dataset.meetingId); };
    });
    main.querySelectorAll(".m-join-list").forEach((b) => {
      b.onclick = (e) => { e.stopPropagation(); window.open(MC().meetingJoinUrl(state.projectId, b.dataset.id), "_blank"); };
    });
    var nb = document.getElementById("mNew");
    if (nb) nb.onclick = async function () {
      var core = mcApi(); if (!core) return;
      var v = await formModal("New meeting", [
        { key: "title", label: "Title" },
        { key: "meetingType", label: "Type", type: "select", options: MTYPES, value: "MEETING" },
        { key: "scheduledAt", label: "When", type: "datetime-local" },
        { key: "durationMinutes", label: "Duration (min)", type: "number", value: 60 },
        { key: "location", label: "Location" },
      ]);
      if (!v || !v.title) return;
      try {
        await core.createMeeting(state.projectId, { title: v.title, meetingType: v.meetingType, scheduledAt: v.scheduledAt ? new Date(v.scheduledAt).toISOString() : new Date().toISOString(), durationMinutes: v.durationMinutes ? parseInt(v.durationMinutes, 10) : 60, location: v.location });
        toast("Meeting created"); renderMeetings(main);
      } catch (e) { toast("Create failed: " + e); }
    };
  }

  // Project Recordings archive — ALL recordings newest-first (label/date/duration/size/
  // status/Play/Download + AD-HOC chip). Covers scheduled-meeting + ad-hoc sessions.
  async function renderRecordings(main) {
    const recs = await fetchProjectRecordings();
    const body = recs.map((r) =>
      "<tr><td>" + (isAudio(r.kind) ? "🎙" : "🎥") + " " + esc(r.label || "") +
        (r.adHoc ? ' <span class="chip" style="background:rgba(230,81,0,0.15);color:#e65100">AD-HOC</span>' : "") + "</td>" +
      "<td>" + fmtDateTime(r.startedAt) + "</td>" +
      "<td>" + fmtDur(r.durationSeconds) + "</td>" +
      "<td>" + fmtSize(r.fileSizeBytes) + "</td>" +
      "<td>" + esc(r.status) + "</td>" +
      '<td style="text-align:right;white-space:nowrap">' + recActionsHtml(r) + "</td></tr>").join("");
    main.innerHTML = "<h1>Recordings</h1>" +
      '<p style="color:var(--muted);font-size:13px;margin:-4px 0 12px">All meeting &amp; ad-hoc session recordings in this project (newest first).</p>' +
      (recs.length === 0
        ? '<div class="empty">No recordings yet. Record a live meeting and it will appear here.</div>'
        : '<div class="card"><table><thead><tr><th>Recording</th><th>When</th><th>Duration</th><th>Size</th><th>Status</th><th></th></tr></thead><tbody>' + body + "</tbody></table></div>") +
      '<div id="modal-mount"></div>';
    wireRecPlay(main);
  }

  // ── W0/W1 — meetings-core client + role helpers (web consumes the shared module) ──
  function MC() { return (typeof window !== "undefined" && window.MeetingsCore) || null; }
  function mcApi() { return MC() ? MC().create((p, o) => api(p, o)) : null; }
  function toast(msg) {
    var t = document.createElement("div");
    t.textContent = msg;
    t.style.cssText = "position:fixed;bottom:20px;left:50%;transform:translateX(-50%);background:#1a1d24;color:#fff;padding:10px 16px;border-radius:6px;z-index:10000;font-size:13px;box-shadow:0 4px 16px rgba(0,0,0,.4)";
    document.body.appendChild(t); setTimeout(() => { try { t.remove(); } catch (e) {} }, 3000);
  }
  function b64urlJson(s) { try { return JSON.parse(atob(s.replace(/-/g, "+").replace(/_/g, "/"))); } catch (e) { return {}; } }
  function jwtClaims() { var t = getToken(); if (!t) return {}; var p = t.split("."); return p.length >= 2 ? b64urlJson(p[1]) : {}; }
  function currentUserId() { var c = jwtClaims(); return c.sub || c.nameid || c.user_id || c.uid || c["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"] || null; }
  function claimRole() { var c = jwtClaims(); return c.iso_role || c.role || c["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] || ""; }
  // Map an arbitrary role string to a CAPS key the shared matrix understands.
  function mapRole(r) {
    r = (r || "").toString().toLowerCase();
    if (/host|manager|coordinator|\bbc\b|bim/.test(r)) return "host";
    if (/chair/.test(r)) return "chair";
    if (/secretary|minute/.test(r)) return "secretary";
    if (/client/.test(r)) return "client";
    if (/lead|author|attendee|discipline/.test(r)) return "attendee";
    return r;
  }
  // Effective meeting role: creator → host; else the user's attendee role; else JWT role;
  // unknown → "attendee" (safe non-destructive default — server still enforces).
  function meetingRole(meeting) {
    var uid = currentUserId();
    if (meeting && uid && (meeting.createdBy === uid || meeting.hostUserId === uid)) return "host";
    var r = "";
    if (meeting && meeting.attendees && uid) {
      var me = meeting.attendees.find(function (a) { return a.userId === uid; });
      if (me && me.role) r = me.role;
    }
    if (!r) r = claimRole();
    var k = mapRole(r);
    return (MC() && MC().CAPS[k]) ? k : "attendee";
  }
  function mCan(cap, meeting) { return MC() ? MC().can(meetingRole(meeting), cap) : false; }

  // Generic form modal → resolves to a values object (or null on cancel).
  function formModal(title, fields) {
    return new Promise((resolve) => {
      var mount = document.getElementById("modal-mount") || document.body;
      var wrap = document.createElement("div");
      wrap.className = "modal-overlay"; wrap.style.zIndex = "9998";
      var inner = fields.map(function (f) {
        var id = "fm_" + f.key;
        if (f.type === "select") {
          // WS1c — options may be plain strings OR { value, label } objects (member dropdowns).
          var opts = (f.options || []).map(function (o) {
            var ov = (o && typeof o === "object") ? o.value : o;
            var ol = (o && typeof o === "object") ? o.label : o;
            return '<option value="' + esc(ov) + '"' + (String(ov) === String(f.value) ? " selected" : "") + ">" + esc(ol) + "</option>";
          }).join("");
          return '<div class="field"><label>' + esc(f.label) + '</label><select id="' + id + '">' + opts + "</select></div>";
        }
        if (f.type === "textarea") return '<div class="field"><label>' + esc(f.label) + '</label><textarea id="' + id + '" rows="5">' + esc(f.value || "") + "</textarea></div>";
        return '<div class="field"><label>' + esc(f.label) + '</label><input id="' + id + '" type="' + (f.type || "text") + '" value="' + esc(f.value == null ? "" : String(f.value)) + '" /></div>';
      }).join("");
      wrap.innerHTML = '<div class="modal-box" style="max-width:520px"><h2>' + esc(title) + "</h2>" + inner +
        '<div class="actions"><button type="button" class="btn-cancel" id="fmCancel">Cancel</button>' +
        '<button type="button" class="btn-primary" id="fmSave">Save</button></div></div>';
      mount.appendChild(wrap);
      var close = function (v) { try { wrap.remove(); } catch (e) {} resolve(v); };
      wrap.querySelector("#fmCancel").onclick = function () { close(null); };
      wrap.onclick = function (e) { if (e.target === wrap) close(null); };
      wrap.querySelector("#fmSave").onclick = function () {
        var out = {}; fields.forEach(function (f) { out[f.key] = (document.getElementById("fm_" + f.key) || {}).value; });
        close(out);
      };
    });
  }

  // P1 — member picker for "Invite to meeting". Fetches the project roster, pre-checks
  // nobody, lets the host multi-select, and returns { userIds, message, sendEmail }.
  // Members already on the meeting are flagged so the host doesn't re-invite blindly.
  async function inviteMembersModal(pid, existingAttendees) {
    var members = [];
    try { members = await api("/api/projects/" + pid + "/members"); } catch (e) { toast("Could not load members: " + e); return null; }
    if (!Array.isArray(members) || !members.length) { toast("No project members to invite."); return null; }
    var onMeeting = {}; (existingAttendees || []).forEach(function (a) { if (a.userId) onMeeting[a.userId] = true; });
    return new Promise(function (resolve) {
      var mount = document.getElementById("modal-mount") || document.body;
      var wrap = document.createElement("div"); wrap.className = "modal-overlay"; wrap.style.zIndex = "9998";
      var rows = members.map(function (m) {
        var label = esc(m.displayName || m.email || m.userId);
        var sub = (m.projectRole ? esc(m.projectRole) : "") + (onMeeting[m.userId] ? " · already invited" : "");
        return '<label style="display:flex;align-items:center;gap:8px;padding:5px 0;border-bottom:1px solid var(--slate-100,#eee);font-size:13px">' +
          '<input type="checkbox" class="invm" value="' + esc(m.userId) + '" />' +
          '<span style="flex:1">' + label + (sub ? ' <span style="color:var(--muted)">— ' + sub + "</span>" : "") + "</span></label>";
      }).join("");
      wrap.innerHTML = '<div class="modal-box" style="max-width:520px"><h2>Invite to meeting</h2>' +
        '<div style="display:flex;gap:8px;margin-bottom:6px"><button type="button" class="ghost" id="invAll">Select all</button><button type="button" class="ghost" id="invNone">Clear</button></div>' +
        '<div style="max-height:300px;overflow:auto;margin-bottom:8px">' + rows + "</div>" +
        '<div class="field"><label>Note (optional)</label><input id="invMsg" type="text" placeholder="e.g. Please join the coordination review" /></div>' +
        '<label style="display:flex;align-items:center;gap:8px;font-size:13px;margin:6px 0"><input type="checkbox" id="invEmail" /> Also send email</label>' +
        '<div class="actions"><button type="button" class="btn-cancel" id="invCancel">Cancel</button>' +
        '<button type="button" class="btn-primary" id="invSend">Send invites</button></div></div>';
      mount.appendChild(wrap);
      var close = function (v) { try { wrap.remove(); } catch (e) {} resolve(v); };
      wrap.querySelector("#invCancel").onclick = function () { close(null); };
      wrap.onclick = function (e) { if (e.target === wrap) close(null); };
      wrap.querySelector("#invAll").onclick = function () { wrap.querySelectorAll(".invm").forEach(function (c) { c.checked = true; }); };
      wrap.querySelector("#invNone").onclick = function () { wrap.querySelectorAll(".invm").forEach(function (c) { c.checked = false; }); };
      wrap.querySelector("#invSend").onclick = function () {
        var ids = []; wrap.querySelectorAll(".invm").forEach(function (c) { if (c.checked) ids.push(c.value); });
        if (!ids.length) { toast("Pick at least one member."); return; }
        close({ userIds: ids, message: (wrap.querySelector("#invMsg") || {}).value || "", sendEmail: !!(wrap.querySelector("#invEmail") || {}).checked });
      };
    });
  }

  // WS1c — shared project-member dropdown source. Returns the raw members plus a formModal
  // select option list ({value:userId,label:name}). `includeManual` prepends a "type manually"
  // sentinel (value ""); `taken` flags members already on the meeting.
  async function projectMemberOptions(pid, includeManual, taken) {
    var members = [];
    try { members = await api("/api/projects/" + pid + "/members"); } catch (e) { members = []; }
    if (!Array.isArray(members)) members = [];
    var opts = includeManual ? [{ value: "", label: "— enter manually below —" }] : [];
    members.forEach(function (mm) {
      opts.push({ value: mm.userId, label: (mm.displayName || mm.email || mm.userId) + (taken && taken[mm.userId] ? " · already added" : "") });
    });
    return { members: members, options: opts };
  }

  var MTYPES = ["MEETING", "COORDINATION", "DESIGN_REVIEW", "PROGRESS", "CLIENT", "SITE", "HANDOVER"];
  function toLocalInput(iso) { if (!iso) return ""; try { var d = new Date(iso); var z = new Date(d.getTime() - d.getTimezoneOffset() * 60000); return z.toISOString().slice(0, 16); } catch (e) { return ""; } }

  // W1 — full role-aware meeting detail (Overview / Agenda / Actions / Attendees / Minutes /
  // Recordings). Everything routes through meetings-core; controls gated by mCan(); server enforces.
  async function renderMeetingDetail(main, meetingId) {
    var core = mcApi();
    if (!core) { main.innerHTML = '<div class="empty">Meeting module not loaded.</div>'; return; }
    var pid = state.projectId;
    var m, recsByMeeting = {};
    try {
      m = await core.getMeeting(pid, meetingId);
      try { recsByMeeting = (await core.recordingsByMeeting(pid)).byMeeting; } catch (e) {}
    } catch (e) { main.innerHTML = '<div class="empty">Could not load meeting: ' + esc(String(e)) + "</div>"; return; }
    var recs = recsByMeeting[meetingId] || [];
    var role = meetingRole(m);
    var canEdit = mCan("editAgenda", m), canMinutes = mCan("editMinutes", m), canAttend = mCan("manageAttendees", m), canAssign = mCan("assignActions", m);
    var agenda = m.agendaItems || m.agenda || [];
    var actions = m.actionItems || m.actions || [];
    var attendees = m.attendees || [];

    main.innerHTML =
      '<div style="display:flex;align-items:center;gap:10px;margin-bottom:8px">' +
        '<button class="ghost" id="mdBack">← Meetings</button>' +
        "<h1 style=\"margin:0\">" + esc(m.title || "(untitled)") + "</h1>" +
        '<span class="chip">' + esc(m.status || "") + '</span>' +
        (recs.length ? ' <span class="chip" style="background:rgba(25,118,210,0.15);color:#1976d2">▶ REC</span>' : "") +
        '<span style="margin-left:auto;color:var(--muted);font-size:12px">your role: ' + esc(role) + "</span>" +
      "</div>" +
      '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:12px">' +
        (mCan("join", m) ? '<button class="btn-primary" id="mdJoin">🎥 Join live</button>' : "") +
        (canAttend ? '<button class="ghost" id="mdInvite">✉ Invite to meeting</button>' : "") +
        (mCan("schedule", m) ? '<button class="ghost" id="mdEdit">✏ Edit meeting</button>' : "") +
      "</div>" +
      '<div class="card"><h3 style="margin-top:0">Overview</h3>' +
        '<div style="font-size:13px;color:var(--muted)">' + esc(m.meetingType || m.type || "MEETING") + " · " +
        (m.scheduledAt ? new Date(m.scheduledAt).toLocaleString() : "Unscheduled") +
        (m.durationMinutes ? " · " + m.durationMinutes + " min" : "") + (m.location ? " · " + esc(m.location) : "") + "</div></div>" +
      sectionCard("Agenda", canEdit ? '<button class="ghost" id="mdAddAgenda">+ Item</button>' : "",
        agenda.length ? agenda.map(function (i, ix) {
          return '<div class="md-row" style="display:flex;gap:8px;align-items:center;padding:4px 0;border-bottom:1px solid var(--slate-100,#eee)">' +
            '<span style="flex:1;font-size:13px"><strong>' + (ix + 1) + ".</strong> " + esc(i.title || "") +
            (i.status ? ' <span class="chip">' + esc(i.status) + "</span>" : "") +
            (i.decision ? '<br><span style="color:var(--muted);font-size:12px">Decision: ' + esc(i.decision) + "</span>" : "") + "</span>" +
            (canEdit ? '<button class="ghost md-ed-agenda" data-id="' + esc(i.id) + '">edit</button><button class="ghost md-del-agenda" data-id="' + esc(i.id) + '">✕</button>' : "") + "</div>";
        }).join("") : '<div class="empty">No agenda items.</div>') +
      sectionCard("Action items", "",
        actions.length ? actions.map(function (a) {
          var mine = a.assigneeUserId && a.assigneeUserId === currentUserId();
          var canRow = canAssign || mine;
          return '<div class="md-row" style="display:flex;gap:8px;align-items:center;padding:4px 0;border-bottom:1px solid var(--slate-100,#eee)">' +
            '<span style="flex:1;font-size:13px">' + esc(a.description || "") +
            (a.assignee ? ' <span style="color:var(--muted)">→ ' + esc(a.assignee) + "</span>" : "") +
            (a.priority ? ' <span class="chip">' + esc(a.priority) + "</span>" : "") +
            ' <span class="chip">' + esc(a.status || "OPEN") + "</span></span>" +
            (canRow ? '<button class="ghost md-ed-action" data-id="' + esc(a.id) + '">edit</button>' : "") + "</div>";
        }).join("") : '<div class="empty">No action items.</div>',
        canAssign ? '<button class="ghost" id="mdAddAction">+ Action</button>' : "") +
      sectionCard("Attendees", canAttend ? '<button class="ghost" id="mdAddAttendee">+ Attendee</button>' : "",
        attendees.length ? attendees.map(function (a) {
          return '<div class="md-row" style="display:flex;gap:8px;align-items:center;padding:4px 0;border-bottom:1px solid var(--slate-100,#eee)">' +
            '<span style="flex:1;font-size:13px">' + esc(a.name || a.email || "") +
            (a.role ? ' <span class="chip">' + esc(a.role) + "</span>" : "") +
            (a.attendanceStatus ? ' <span style="color:var(--muted)">' + esc(a.attendanceStatus) + "</span>" : "") + "</span>" +
            (canAttend ? '<button class="ghost md-ed-attendee" data-id="' + esc(a.id) + '">edit</button><button class="ghost md-del-attendee" data-id="' + esc(a.id) + '">✕</button>' : "") + "</div>";
        }).join("") : '<div class="empty">No attendees.</div>') +
      '<div class="card"><div style="display:flex;align-items:center;gap:8px"><h3 style="margin:0;flex:1">Minutes</h3>' +
        (canMinutes ? '<button class="ghost" id="mdGenDoc">📄 Generate doc</button>' : "") + "</div>" +
        (canMinutes
          ? '<textarea id="mdMinutes" rows="6" style="width:100%;margin-top:8px">' + esc(m.minutes || "") + '</textarea><div class="actions"><button class="btn-primary" id="mdSaveMinutes">Save minutes</button></div>'
          : '<div style="white-space:pre-wrap;font-size:13px;margin-top:8px">' + esc(m.minutes || "—") + "</div>") + "</div>" +
      sectionCard("Recordings", "",
        recs.length ? '<table><tbody>' + recs.map(function (r) {
          return "<tr><td style=\"font-size:13px\">" + (isAudio(r.kind) ? "🎙" : "🎥") + " " + fmtDateTime(r.startedAt) + " · " + fmtDur(r.durationSeconds) + " · " + fmtSize(r.fileSizeBytes) + " · " + esc(r.status) +
            "</td><td style=\"text-align:right;white-space:nowrap\">" + recActionsHtml(r) + "</td></tr>";
        }).join("") + "</tbody></table>" : '<div class="empty">No recordings for this meeting.</div>') +
      '<div id="modal-mount"></div>';

    var reload = function () { renderMeetingDetail(main, meetingId); };
    document.getElementById("mdBack").onclick = function () { state.section = "meetings"; render(); };
    var jb = document.getElementById("mdJoin"); if (jb) jb.onclick = function () { window.open(MC().meetingJoinUrl(pid, meetingId), "_blank"); };
    // P1 — invite project members to THIS meeting (push → tap to join). Distinct
    // from the project-member invite (which adds someone to the project).
    var ib = document.getElementById("mdInvite"); if (ib) ib.onclick = async function () {
      var picked = await inviteMembersModal(pid, attendees);
      if (!picked) return;
      try {
        var res = await core.invite(pid, meetingId, { userIds: picked.userIds, message: picked.message, sendEmail: picked.sendEmail });
        var note = "Invited " + (res.count || picked.userIds.length) + " member(s)";
        if (!res.pushConfigured) note += " · push off (in-app/email only)";
        if (picked.sendEmail) note += res.emailConfigured ? " · email sent" : " · email not configured";
        toast(note); reload();
      } catch (e) { toast("Invite failed: " + e); }
    };
    var eb = document.getElementById("mdEdit"); if (eb) eb.onclick = async function () {
      var v = await formModal("Edit meeting", [
        { key: "title", label: "Title", value: m.title },
        { key: "meetingType", label: "Type", type: "select", options: MTYPES, value: m.meetingType || m.type || "MEETING" },
        { key: "scheduledAt", label: "When", type: "datetime-local", value: toLocalInput(m.scheduledAt) },
        { key: "durationMinutes", label: "Duration (min)", type: "number", value: m.durationMinutes },
        { key: "location", label: "Location", value: m.location },
      ]);
      if (!v) return;
      try { await core.updateMeeting(pid, meetingId, { title: v.title, meetingType: v.meetingType, scheduledAt: v.scheduledAt ? new Date(v.scheduledAt).toISOString() : m.scheduledAt, durationMinutes: v.durationMinutes ? parseInt(v.durationMinutes, 10) : null, location: v.location }); toast("Saved"); reload(); }
      catch (e) { toast("Save failed: " + e); }
    };
    var aa = document.getElementById("mdAddAgenda"); if (aa) aa.onclick = async function () {
      var v = await formModal("Add agenda item", [{ key: "title", label: "Title" }, { key: "description", label: "Description", type: "textarea" }, { key: "durationMinutes", label: "Duration (min)", type: "number" }]);
      if (!v || !v.title) return;
      try { await core.addAgendaItem(pid, meetingId, { title: v.title, description: v.description, durationMinutes: v.durationMinutes ? parseInt(v.durationMinutes, 10) : null }); reload(); } catch (e) { toast("Failed: " + e); }
    };
    main.querySelectorAll(".md-ed-agenda").forEach(function (b) { b.onclick = async function () {
      var it = agenda.find(function (x) { return x.id === b.dataset.id; }) || {};
      var v = await formModal("Edit agenda item", [{ key: "title", label: "Title", value: it.title }, { key: "status", label: "Status", type: "select", options: ["", "OPEN", "DISCUSSED", "CLOSED"], value: it.status || "" }, { key: "outcome", label: "Outcome", type: "textarea", value: it.outcome }, { key: "decision", label: "Decision", type: "textarea", value: it.decision }]);
      if (!v) return; try { await core.updateAgendaItem(pid, meetingId, b.dataset.id, v); reload(); } catch (e) { toast("Failed: " + e); }
    }; });
    main.querySelectorAll(".md-del-agenda").forEach(function (b) { b.onclick = async function () { if (!confirm("Delete this agenda item?")) return; try { await core.deleteAgendaItem(pid, meetingId, b.dataset.id); reload(); } catch (e) { toast("Failed: " + e); } }; });
    var ac = document.getElementById("mdAddAction"); if (ac) ac.onclick = async function () {
      // WS1c — assignee is a known set (project members) → dropdown + manual fallback.
      var amem = await projectMemberOptions(pid, true, null);
      var v = await formModal("Add action", [{ key: "description", label: "Description" }, { key: "assignee", label: "Assignee (member)", type: "select", options: amem.options, value: "" }, { key: "assigneeManual", label: "Assignee (if external)", value: "" }, { key: "dueDate", label: "Due", type: "date" }, { key: "priority", label: "Priority", type: "select", options: ["LOW", "MEDIUM", "HIGH"], value: "MEDIUM" }]);
      if (!v || !v.description) return;
      var body = { description: v.description, dueDate: v.dueDate || null, priority: v.priority };
      if (v.assignee) { var am = amem.members.find(function (x) { return String(x.userId) === String(v.assignee); }) || {}; body.assignee = am.displayName || am.email || ""; body.assigneeUserId = v.assignee; }
      else if (v.assigneeManual) body.assignee = v.assigneeManual;
      try { await core.addAction(pid, meetingId, body); reload(); } catch (e) { toast("Failed: " + e); }
    };
    main.querySelectorAll(".md-ed-action").forEach(function (b) { b.onclick = async function () {
      var it = actions.find(function (x) { return x.id === b.dataset.id; }) || {};
      var v = await formModal("Edit action", [{ key: "description", label: "Description", value: it.description }, { key: "assignee", label: "Assignee", value: it.assignee }, { key: "dueDate", label: "Due", type: "date", value: (it.dueDate || "").slice(0, 10) }, { key: "priority", label: "Priority", type: "select", options: ["LOW", "MEDIUM", "HIGH"], value: it.priority || "MEDIUM" }, { key: "status", label: "Status", type: "select", options: ["OPEN", "IN_PROGRESS", "CLOSED"], value: it.status || "OPEN" }]);
      if (!v) return; try { await core.updateAction(pid, meetingId, b.dataset.id, v); reload(); } catch (e) { toast("Failed: " + e); }
    }; });
    var ad = document.getElementById("mdAddAttendee"); if (ad) ad.onclick = async function () {
      // WS1c — Name is a dropdown of project members (sourced from the Project Members tab);
      // role/attendance are dropdowns; manual entry stays as a fallback for external guests.
      var taken = {}; attendees.forEach(function (a) { if (a.userId) taken[a.userId] = true; });
      var mem = await projectMemberOptions(pid, true, taken);
      var v = await formModal("Add attendee", [
        { key: "member", label: "Project member", type: "select", options: mem.options, value: "" },
        { key: "name", label: "Name (external guest)", value: "" },
        { key: "email", label: "Email (external guest)", value: "" },
        { key: "role", label: "Role", type: "select", options: ["Attendee", "Chair", "Secretary", "Client", "Discipline-lead"], value: "Attendee" },
      ]);
      if (!v) return;
      var body = { role: v.role };
      if (v.member) { var mm = mem.members.find(function (x) { return String(x.userId) === String(v.member); }) || {}; body.userId = v.member; body.name = mm.displayName || mm.email || ""; body.email = mm.email || ""; }
      else { body.name = v.name; body.email = v.email; }
      if (!body.userId && !body.name && !body.email) { toast("Pick a member or enter a name/email."); return; }
      try { await core.addAttendee(pid, meetingId, body); reload(); } catch (e) { toast("Failed: " + e); }
    };
    main.querySelectorAll(".md-ed-attendee").forEach(function (b) { b.onclick = async function () {
      var it = attendees.find(function (x) { return x.id === b.dataset.id; }) || {};
      var v = await formModal("Edit attendee", [{ key: "role", label: "Role", type: "select", options: ["Attendee", "Chair", "Secretary", "Client", "Discipline-lead"], value: it.role || "Attendee" }, { key: "attendanceStatus", label: "Attendance", type: "select", options: ["INVITED", "ACCEPTED", "DECLINED", "ATTENDED", "ABSENT"], value: it.attendanceStatus || "INVITED" }]);
      if (!v) return; try { await core.updateAttendee(pid, meetingId, b.dataset.id, v); reload(); } catch (e) { toast("Failed: " + e); }
    }; });
    main.querySelectorAll(".md-del-attendee").forEach(function (b) { b.onclick = async function () { if (!confirm("Remove this attendee?")) return; try { await core.deleteAttendee(pid, meetingId, b.dataset.id); reload(); } catch (e) { toast("Failed: " + e); } }; });
    var sm = document.getElementById("mdSaveMinutes"); if (sm) sm.onclick = async function () { try { await core.logMinutes(pid, meetingId, document.getElementById("mdMinutes").value, m.status); toast("Minutes saved"); } catch (e) { toast("Failed: " + e); } };
    var gd = document.getElementById("mdGenDoc"); if (gd) gd.onclick = async function () { try { await core.generateMinutesDoc(pid, meetingId); toast("Minutes doc generated → Documents"); } catch (e) { toast("Failed: " + e); } };
    wireRecPlay(main);
  }
  function sectionCard(title, headerBtn, bodyHtml, headerBtn2) {
    return '<div class="card"><div style="display:flex;align-items:center;gap:8px;margin-bottom:6px"><h3 style="margin:0;flex:1">' + esc(title) + "</h3>" + (headerBtn || "") + (headerBtn2 || "") + "</div>" + bodyHtml + "</div>";
  }

  const workflowColumns = [
    { k: "preset",          label: "Preset" },
    { k: "stepsPassed",     label: "✓", render: v => `<span style="color:var(--green)">${v}</span>` },
    { k: "stepsFailed",     label: "✗", render: v => `<span style="color:var(--red)">${v}</span>` },
    { k: "stepsSkipped",    label: "→" },
    { k: "complianceBefore",label: "Before", render: v => v?.toFixed(0) + "%" },
    { k: "complianceAfter", label: "After",  render: v => v?.toFixed(0) + "%" },
    { k: "executedAt",      label: "When",   render: v => fmtDate(v) },
  ];
  const warningColumns = [
    { k: "severity",     label: "Sev", render: (v) => chip(v) },
    { k: "category",     label: "Cat" },
    { k: "description",  label: "Description" },
    { k: "elementCount", label: "Elts" },
    { k: "discipline",   label: "Disc" },
    { k: "firstSeen",    label: "First seen", render: (v) => fmtDate(v) },
  ];
  const scheduleColumns = [
    { k: "code",            label: "Code" },
    { k: "name",            label: "Name" },
    { k: "ribaStage",       label: "Stage" },
    { k: "discipline",      label: "Disc" },
    { k: "plannedStart",    label: "Start",  render: v => fmtDate(v) },
    { k: "plannedFinish",   label: "Finish", render: v => fmtDate(v) },
    { k: "percentComplete", label: "%", render: v => (v ?? 0).toFixed(0) + "%" },
  ];
  const costColumns = [
    { k: "code",        label: "Code" },
    { k: "description", label: "Description" },
    { k: "discipline",  label: "Disc" },
    { k: "unit",        label: "Unit" },
    { k: "quantity",    label: "Qty" },
    { k: "unitRate",    label: "Rate",  render: (v, r) => fmt(v, r.currency) },
    { k: "lineTotal",   label: "Total", render: (v, r) => fmt(v, r.currency) },
    { k: "kind",        label: "Kind" },
  ];

  // ── Helpers ───────────────────────────────────────────────────────────

  function tableHtml(rows, columns) {
    if (!rows || rows.length === 0)
      return `<div class="empty">Nothing to show yet.</div>`;
    const head = columns.map(c => `<th>${esc(c.label)}</th>`).join("");
    const body = rows.map(r =>
      "<tr>" + columns.map(c => {
        const v = r[c.k];
        return `<td>${c.render ? c.render(v, r) : (v == null ? "" : esc(String(v)))}</td>`;
      }).join("") + "</tr>"
    ).join("");
    return `<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
  }
  function issuesTableHtml(rows) { return tableHtml(rows, issueColumns); }
  function chip(v) {
    if (!v) return "";
    const cls = String(v).toLowerCase();
    return `<span class="chip ${cls}">${esc(v)}</span>`;
  }
  function esc(s) {
    return String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  // ── Phase 152: Tenant keyword extensions editor ───────────────────────
  // Admin / BIM Manager surface. The server caches the result via Redis +
  // a striped LRU; PUT body is validated server-side so a malformed JSON
  // returns 400 with a structured error instead of being silently
  // ignored at request time.
  async function renderTenantKeywords(main) {
    // Phase 154 — fetch the canonical bucket list from the server in
    // parallel with the tenant's current JSON so the JS validator
    // doesn't drift if a 7th bucket lands server-side.
    await loadRoleBucketsOnce();
    let current;
    try {
      current = await api(`/api/admin/tenant-keywords`);
    } catch (e) {
      main.innerHTML = `<div class="empty">Could not load tenant keywords: ${esc(String(e))}</div>`;
      return;
    }
    const sample = `{
  "working":   ["PARKED", "WAITING_ON_X"],
  "terminal":  ["FROZEN", "DECOMMISSIONED"],
  "rejecting": ["BLOCKED_BY_QA"]
}`;
    const initial = current.json && current.json.length > 0
      ? safeFormatJson(current.json)
      : sample;

    main.innerHTML = `
      <h1>Tenant keyword extensions</h1>
      <p class="hint">
        Tenant-scoped vocabulary for the deliverable state-machine role
        inferer. Sits between platform defaults and per-project keywords;
        project keywords still win. Recognised role buckets:
        <code>initial</code> · <code>working</code> · <code>submitting</code> ·
        <code>accepting</code> · <code>rejecting</code> · <code>terminal</code>.
        Other keys are ignored.
      </p>
      <div class="card" style="max-width:760px">
        <div class="row" style="justify-content:space-between;align-items:center;margin-bottom:8px">
          <strong>Current configuration</strong>
          <span class="chip ${current.hasExtensions ? 'green' : 'grey'}">
            ${current.hasExtensions ? 'Active' : 'None set'}
          </span>
        </div>
        <textarea id="tkJson" rows="14" spellcheck="false"
                  style="width:100%;font-family:ui-monospace,monospace;font-size:13px"
        >${esc(initial)}</textarea>
        <!-- Phase 153 — inline schema-aware validator output. Updates
             on every keystroke so a typo in a bucket name is flagged
             before Save instead of round-tripping to the server. -->
        <div id="tkValidate" class="hint" style="margin-top:6px;min-height:18px"></div>
        <div class="row" style="gap:8px;margin-top:8px;align-items:center">
          <button id="tkSave" class="primary">Save</button>
          <button id="tkClear" class="ghost">Clear extensions</button>
          <button id="tkReset" class="ghost">Reset editor</button>
          <span id="tkStatus" class="hint" style="margin-left:auto"></span>
        </div>
      </div>
    `;

    // Phase 153 — wire the inline validator. Runs synchronously, no
    // server round-trip. Disables the Save button on hard errors so
    // the user can't push known-bad JSON. Server still validates
    // (defence in depth) — this is purely a UX improvement.
    const $json = document.getElementById("tkJson");
    const $validate = document.getElementById("tkValidate");
    const $save = document.getElementById("tkSave");

    function validate() {
      const text = ($json.value || "").trim();
      if (text.length === 0) {
        $validate.textContent = "(empty — Save will clear all extensions)";
        $validate.className = "hint";
        $save.disabled = false;
        return true;
      }
      const result = validateTenantKeywordsJson(text);
      $validate.textContent = result.message;
      $validate.className = result.ok ? "hint ok" : "hint error";
      $save.disabled = !result.ok;
      return result.ok;
    }
    $json.addEventListener("input", validate);
    validate(); // initial pass

    document.getElementById("tkSave").onclick = async () => {
      const body = document.getElementById("tkJson").value || "";
      const status = document.getElementById("tkStatus");
      status.textContent = "Saving…";
      try {
        const res = await api(`/api/admin/tenant-keywords`, {
          method: "PUT",
          body: JSON.stringify({ json: body }),
        });
        status.textContent = `Saved · ${res.buckets || 0} bucket(s) · ${res.entries || 0} keyword(s)`;
        status.className = "hint ok";
      } catch (e) {
        status.textContent = `Save failed — ${esc(String(e))}`;
        status.className = "hint error";
      }
    };

    document.getElementById("tkClear").onclick = async () => {
      if (!confirm("Clear all tenant keyword extensions? Projects fall back to platform + built-in vocabulary.")) return;
      const status = document.getElementById("tkStatus");
      status.textContent = "Clearing…";
      try {
        await api(`/api/admin/tenant-keywords`, {
          method: "PUT",
          body: JSON.stringify({ json: null }),
        });
        document.getElementById("tkJson").value = sample;
        status.textContent = "Cleared.";
        status.className = "hint ok";
      } catch (e) {
        status.textContent = `Clear failed — ${esc(String(e))}`;
        status.className = "hint error";
      }
    };

    document.getElementById("tkReset").onclick = () => {
      document.getElementById("tkJson").value = initial;
      const s = document.getElementById("tkStatus");
      s.textContent = "Editor reset.";
      s.className = "hint";
    };
  }

  // Phase 153 — pure-Compute validator mirroring the server's
  // ParseForValidation rules. Returns { ok, message } so the editor
  // can disable Save on hard errors and surface a one-line hint.
  //
  // Phase 154 — the bucket list now comes from
  // /api/state-machine/role-buckets (single source of truth). We
  // fetch it lazily on first call and cache the resolved Set on the
  // module. Until the fetch lands, fall back to the historical
  // hardcoded six so the editor isn't blocked by a slow Redis blip
  // on startup. If the server later returns a different list (e.g.
  // a 7th bucket added), subsequent validations honour it without
  // a JS rebuild.
  let TK_VALID_BUCKETS = new Set([
    "initial", "working", "submitting", "accepting", "rejecting", "terminal",
  ]);
  let TK_BUCKETS_LOADED = false;
  async function loadRoleBucketsOnce() {
    if (TK_BUCKETS_LOADED) return;
    try {
      const res = await api(`/api/state-machine/role-buckets`);
      if (Array.isArray(res?.buckets) && res.buckets.length > 0) {
        TK_VALID_BUCKETS = new Set(res.buckets.map(b => String(b).toLowerCase()));
      }
    } catch { /* fall back to hardcoded set; non-fatal */ }
    TK_BUCKETS_LOADED = true;
  }
  function validateTenantKeywordsJson(text) {
    let parsed;
    try { parsed = JSON.parse(text); }
    catch (e) { return { ok: false, message: `JSON syntax error — ${esc(String(e.message || e))}` }; }
    if (parsed === null || Array.isArray(parsed) || typeof parsed !== "object") {
      return { ok: false, message: "Body must be a JSON object: { \"working\": [\"PARKED\"], … }" };
    }
    const bucketNames = Object.keys(parsed);
    if (bucketNames.length === 0) {
      return { ok: false, message: "No buckets defined." };
    }
    const unknown = bucketNames.filter(k => !TK_VALID_BUCKETS.has(String(k).toLowerCase()));
    if (unknown.length > 0) {
      return {
        ok: false,
        message: `Unknown bucket name(s): ${unknown.map(esc).join(", ")}. ` +
                 `Valid: ${[...TK_VALID_BUCKETS].join(", ")}.`,
      };
    }
    let totalEntries = 0;
    for (const [bucket, value] of Object.entries(parsed)) {
      if (!Array.isArray(value)) {
        return { ok: false, message: `"${esc(bucket)}" must be a JSON array of strings.` };
      }
      const nonStrings = value.filter(v => typeof v !== "string");
      if (nonStrings.length > 0) {
        return {
          ok: false,
          message: `"${esc(bucket)}" contains non-string entries; only quoted strings are accepted.`,
        };
      }
      const empties = value.filter(v => !v || !v.trim());
      if (empties.length > 0) {
        return {
          ok: false,
          message: `"${esc(bucket)}" contains empty / whitespace strings; remove them or fill them in.`,
        };
      }
      totalEntries += value.length;
    }
    if (totalEntries === 0) {
      return { ok: false, message: "No keywords across any bucket — Save would clear extensions." };
    }
    return {
      ok: true,
      message: `Looks good — ${bucketNames.length} bucket${bucketNames.length === 1 ? "" : "s"}, ${totalEntries} keyword${totalEntries === 1 ? "" : "s"}.`,
    };
  }

  // ── Phase 155: Tenant BIM-Manager role override editor ───────────────
  // Same auth gate as tenant-keywords. The override is a JSON array of
  // single-letter ISO 19650 codes; null clears so projects fall back
  // to the deployment-global appsettings list (default ["K"]).
  async function renderTenantBimManagerRoles(main) {
    let current;
    try {
      current = await api(`/api/admin/tenant-bim-manager-roles`);
    } catch (e) {
      main.innerHTML = `<div class="empty">Could not load BIM-Manager roles: ${esc(String(e))}</div>`;
      return;
    }
    const sample = `["K", "C"]`;
    const initial = current.json && current.json.length > 0
      ? safeFormatJson(current.json)
      : sample;

    main.innerHTML = `
      <h1>Tenant BIM-Manager roles</h1>
      <p class="hint">
        Tenant-scoped override of the ISO 19650 role codes that grant
        BIM-Manager permissions on the tenant-keywords editor (and any
        other endpoint behind the <code>BimManagerOrAdmin</code>
        authorisation policy). Null/empty falls back to the
        deployment-wide list (default <code>["K"]</code>). Single-letter
        codes only — e.g. <code>K</code> (BIM Manager), <code>C</code>
        (Coordinator), <code>M</code> (Mechanical), <code>S</code>
        (Structural).
      </p>
      <div class="card" style="max-width:760px">
        <div class="row" style="justify-content:space-between;align-items:center;margin-bottom:8px">
          <strong>Current override</strong>
          <span class="chip ${current.hasOverride ? 'green' : 'grey'}">
            ${current.hasOverride ? 'Active' : 'Falls back to deployment'}
          </span>
        </div>
        <textarea id="bmrJson" rows="6" spellcheck="false"
                  style="width:100%;font-family:ui-monospace,monospace;font-size:13px"
        >${esc(initial)}</textarea>
        <div id="bmrValidate" class="hint" style="margin-top:6px;min-height:18px"></div>
        <div class="row" style="gap:8px;margin-top:8px;align-items:center">
          <button id="bmrSave" class="primary">Save</button>
          <button id="bmrClear" class="ghost">Clear override</button>
          <button id="bmrReset" class="ghost">Reset editor</button>
          <span id="bmrStatus" class="hint" style="margin-left:auto"></span>
        </div>
      </div>
    `;

    const $json = document.getElementById("bmrJson");
    const $validate = document.getElementById("bmrValidate");
    const $save = document.getElementById("bmrSave");

    function validate() {
      const text = ($json.value || "").trim();
      if (text.length === 0) {
        $validate.textContent = "(empty — Save will clear the override)";
        $validate.className = "hint";
        $save.disabled = false;
        return true;
      }
      const result = validateBimManagerRolesJson(text);
      $validate.textContent = result.message;
      $validate.className = result.ok ? "hint ok" : "hint error";
      $save.disabled = !result.ok;
      return result.ok;
    }
    $json.addEventListener("input", validate);
    validate();

    document.getElementById("bmrSave").onclick = async () => {
      const body = $json.value || "";
      const status = document.getElementById("bmrStatus");
      status.textContent = "Saving…";
      try {
        const res = await api(`/api/admin/tenant-bim-manager-roles`, {
          method: "PUT",
          body: JSON.stringify({ json: body }),
        });
        const roles = (res.roles || []).map(esc).join(", ");
        status.textContent = `Saved · roles: ${roles}`;
        status.className = "hint ok";
      } catch (e) {
        status.textContent = `Save failed — ${esc(String(e))}`;
        status.className = "hint error";
      }
    };

    document.getElementById("bmrClear").onclick = async () => {
      if (!confirm("Clear the tenant BIM-Manager role override? Tenant falls back to deployment defaults.")) return;
      const status = document.getElementById("bmrStatus");
      status.textContent = "Clearing…";
      try {
        await api(`/api/admin/tenant-bim-manager-roles`, {
          method: "PUT",
          body: JSON.stringify({ json: null }),
        });
        $json.value = sample;
        status.textContent = "Cleared.";
        status.className = "hint ok";
      } catch (e) {
        status.textContent = `Clear failed — ${esc(String(e))}`;
        status.className = "hint error";
      }
    };

    document.getElementById("bmrReset").onclick = () => {
      $json.value = initial;
      const s = document.getElementById("bmrStatus");
      s.textContent = "Editor reset.";
      s.className = "hint";
    };
  }

  // Phase 155 — pure-Compute validator for the BIM-Manager role
  // override JSON. Mirrors the server's
  // DbTenantBimManagerRoleResolver.Parse rules: array of non-empty
  // strings; trims + uppercases on the server side.
  function validateBimManagerRolesJson(text) {
    let parsed;
    try { parsed = JSON.parse(text); }
    catch (e) { return { ok: false, message: `JSON syntax error — ${esc(String(e.message || e))}` }; }
    if (!Array.isArray(parsed)) {
      return { ok: false, message: `Body must be a JSON array of strings: ["K", "C"]` };
    }
    if (parsed.length === 0) {
      return { ok: false, message: "Empty array — Save would clear the override." };
    }
    const nonStrings = parsed.filter(v => typeof v !== "string");
    if (nonStrings.length > 0) {
      return { ok: false, message: "Array contains non-string entries; only quoted strings are accepted." };
    }
    const empties = parsed.filter(v => !v || !v.trim());
    if (empties.length > 0) {
      return { ok: false, message: "Array contains empty / whitespace strings; remove them." };
    }
    const tooLong = parsed.find(v => v.trim().length > 4);
    if (tooLong) {
      return { ok: false, message: `"${esc(tooLong)}" looks too long for an ISO 19650 single-letter code.` };
    }
    const unique = new Set(parsed.map(v => v.trim().toUpperCase()));
    return {
      ok: true,
      message: `Looks good — ${unique.size} role${unique.size === 1 ? "" : "s"} (${[...unique].join(", ")}).`,
    };
  }

  function safeFormatJson(s) {
    try { return JSON.stringify(JSON.parse(s), null, 2); }
    catch { return s; }
  }
  function fmtDate(v) { if (!v) return ""; try { return new Date(v).toLocaleDateString(); } catch { return v; } }
  function fmt(v, ccy) {
    if (v == null) return "";
    try { return new Intl.NumberFormat("en-GB", { style: "currency", currency: ccy || "GBP" }).format(v); }
    catch { return "£" + Number(v).toFixed(2); }
  }

  // ── Start ─────────────────────────────────────────────────────────────

  // Mirror boot()'s own 401-vs-unreachable split (see :370-377). A bare
  // `.catch(() => showLogin())` bounced the user to the login screen on ANY
  // uncaught boot rejection — including transient render/config errors — even
  // though their session was valid. The api() helper tags genuine auth
  // failures with `.unauthenticated`; everything else is a server-reachability
  // problem and should surface the retry panel, not a spurious logout.
  if (!getToken()) { showLogin(); }
  else {
    // Re-seed the tenant header for a session restored from a prior visit
    // (setTokens only ran on the original login).
    seedTenantFromToken(getToken());
    boot().catch(e => (e && e.unauthenticated) ? showLogin() : renderServerUnreachable(e));
  }
})();
