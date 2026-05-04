// C1 — Planscape office dashboard. Vanilla JS; no build step.
//
// - localStorage stores the JWT access/refresh pair
// - fetch() + Bearer auth against the same /api/* endpoints mobile uses
// - Views are rendered into #main by swapping innerHTML — simple, good enough
//   for a read-only coordinator view.
(function () {
  "use strict";

  // Phase 169 — runtime config. The Mapbox token is fetched once at boot
  // from /api/public-config, which the server populates from
  // appsettings.json (Maps:MapboxToken) or the MAPBOX_TOKEN env var.
  // When the server returns an empty token we leave the placeholder in
  // place and the map view renders a graceful fallback panel.
  const CONFIG = {
    apiBase: "/api",
    mapboxToken: "PLANSCAPE_MAPBOX_TOKEN",
  };
  async function loadPublicConfig() {
    try {
      const res = await fetch("/api/public-config");
      if (!res.ok) return;
      const body = await res.json();
      if (body && typeof body.mapboxToken === "string" && body.mapboxToken.length > 0) {
        CONFIG.mapboxToken = body.mapboxToken;
      }
    } catch { /* non-fatal — fallback panel will render */ }
  }

  const TOKEN_KEY   = "planscape_token";
  const REFRESH_KEY = "planscape_refresh";
  const USER_KEY    = "planscape_user";

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
  }
  function clearTokens() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
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
        throw new Error("unauthenticated");
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

  // ── Boot + router ─────────────────────────────────────────────────────

  async function boot() {
    document.getElementById("userChip").textContent = localStorage.getItem(USER_KEY) || "";
    // Phase 169 — pull public runtime config (Mapbox token) before any
    // view tries to render the map.
    await loadPublicConfig();
    try {
      state.projects = await api("/api/projects");
    } catch { return; /* showLogin already invoked */ }
    const picker = document.getElementById("projectPicker");
    picker.innerHTML = state.projects
      .map(p => `<option value="${p.id}">${esc(p.name)} (${esc(p.code)})</option>`)
      .join("");
    state.projectId = state.projects[0]?.id || null;
    picker.addEventListener("change", () => { state.projectId = picker.value; render(); });

    document.querySelectorAll(".nav-link").forEach(link => {
      link.addEventListener("click", (ev) => {
        ev.preventDefault();
        document.querySelectorAll(".nav-link").forEach(l => l.classList.remove("active"));
        link.classList.add("active");
        state.view = link.dataset.view;
        render();
      });
    });
    render();
  }

  async function render() {
    const main = document.getElementById("main");
    if (!state.projectId) { main.innerHTML = `<div class="empty">No projects available.</div>`; return; }
    main.innerHTML = `<div class="empty">Loading…</div>`;
    try {
      switch (state.view) {
        case "overview":     await renderOverview(main); break;
        case "issues":       await renderList(main, `Issues`, `/api/projects/${state.projectId}/issues`, issueColumns); break;
        case "documents":    await renderList(main, `Documents`, `/api/projects/${state.projectId}/documents`, docColumns); break;
        case "transmittals": await renderList(main, `Transmittals`, `/api/projects/${state.projectId}/transmittals`, tmxColumns); break;
        case "meetings":     await renderList(main, `Meetings`, `/api/projects/${state.projectId}/meetings`, meetingColumns); break;
        case "workflows":    await renderList(main, `Workflow runs`, `/api/projects/${state.projectId}/workflows`, workflowColumns); break;
        case "warnings":     await renderList(main, `Warnings`, `/api/projects/${state.projectId}/warnings`, warningColumns); break;
        case "models":       await renderModels(main); break;
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

  // Phase 169 — deterministic per-project cover gradient palette. A real
  // CoverImageUrl wins; otherwise we hash the project code into one of six
  // palettes so every card has a distinct visual without an external image.
  const COVER_PALETTES = [
    ["#1A1F5E", "#2D3480"], // navy
    ["#7C2D12", "#EA580C"], // burnt orange
    ["#064E3B", "#10B981"], // emerald
    ["#4C1D95", "#8B5CF6"], // violet
    ["#831843", "#EC4899"], // magenta
    ["#0C4A6E", "#0EA5E9"], // sky
  ];
  function coverStyle(p) {
    if (p.coverImageUrl) {
      return `background:#1A1F5E url('${esc(p.coverImageUrl)}') center/cover no-repeat;`;
    }
    const seed = (p.code || p.id || p.name || "").toString();
    let h = 0;
    for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) | 0;
    const [a, b] = COVER_PALETTES[Math.abs(h) % COVER_PALETTES.length];
    return `background:linear-gradient(135deg, ${a}, ${b});`;
  }

  function projectCardHtml(p) {
    const sk = statusKey(p);
    const pct = Math.round(p.compliancePercent || 0);
    const colour = complianceColor(pct);
    const loc = p.city ? `${esc(p.city)}${p.country ? ", " + esc(p.country) : ""}` : "Location not set";
    const discs = disciplinesFor(p);

    return `
      <div class="project-card" data-project-id="${esc(p.id)}">
        <div class="card-cover" style="${coverStyle(p)}">
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
          <button data-card-action="overview"  data-project-id="${esc(p.id)}">Dashboard</button>
          <button data-card-action="issues"    data-project-id="${esc(p.id)}">Issues</button>
          <button data-card-action="documents" data-project-id="${esc(p.id)}">Documents</button>
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

    // Card body click → navigate to project overview
    document.querySelectorAll(".project-card").forEach(card => {
      card.onclick = (e) => {
        // Ignore clicks that originated on internal action buttons or pin
        if (e.target.closest(".pin-btn")) return;
        if (e.target.closest("[data-card-action]")) return;
        navigateToProject(card.dataset.projectId, "overview");
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
          navigateToProject(p.id, "overview");
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
    const rows = await api(`/api/projects/${state.projectId}/models`);
    main.innerHTML = `
      <h1>3D models</h1>
      ${rows.length === 0 ? `<div class="empty">No models published yet. Use the Revit plugin's "Publish Model to Planscape" command.</div>` : ""}
      <div class="kpi-grid">
        ${rows.map(m => `
          <div class="card" style="margin-bottom:0">
            <div style="display:flex;align-items:center;gap:10px;margin-bottom:8px">
              <span style="font-size:24px">${m.format === "Glb" || m.format === "Gltf" ? "🧊" : "📦"}</span>
              <strong>${esc(m.name)}</strong>
            </div>
            <div style="font-size:12px;color:#666">${esc(m.format)} · ${(m.fileSizeBytes/1024/1024).toFixed(1)} MB${m.discipline ? " · " + esc(m.discipline) : ""}</div>
            <button class="ghost" style="margin-top:10px;color:var(--primary);border-color:var(--primary)"
              onclick="window.open('/viewer.html?project=${state.projectId}&model=${m.id}','_blank')">
              Open in viewer
            </button>
          </div>`).join("")}
      </div>`;
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

  if (!getToken()) { showLogin(); } else { boot().catch(() => showLogin()); }
})();
