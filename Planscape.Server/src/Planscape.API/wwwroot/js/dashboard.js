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
  console.log("[dashboard] STING_DASH_BUILD recordings-web");

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
    try { const w = await api(`/api/projects/${state.projectId}/recordings`); return (w && w.recordings) || []; }
    catch (e) { return []; }
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
        "<td>" + esc(m.location || "") + "</td></tr>";
    }).join("");
    main.innerHTML = "<h1>Meetings</h1>" +
      '<p style="color:var(--muted);font-size:13px;margin:-4px 0 12px">Click a meeting to see its recordings.</p>' +
      '<div class="card">' + (meetings.length
        ? "<table><thead><tr><th>Title</th><th>Type</th><th>When</th><th>Duration (m)</th><th>Location</th></tr></thead><tbody>" + body + "</tbody></table>"
        : '<div class="empty">No meetings yet.</div>') + "</div>" +
      '<div id="modal-mount"></div>';
    main.querySelectorAll("tr[data-meeting-id]").forEach((tr) => {
      tr.onclick = () => {
        const m = meetings.find((x) => x.id === tr.dataset.meetingId);
        openMeetingRecordings(main, m, byMeeting[tr.dataset.meetingId] || []);
      };
    });
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
