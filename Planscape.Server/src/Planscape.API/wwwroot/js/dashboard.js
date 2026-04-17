// C1 — Planscape office dashboard. Vanilla JS; no build step.
//
// - localStorage stores the JWT access/refresh pair
// - fetch() + Bearer auth against the same /api/* endpoints mobile uses
// - Views are rendered into #main by swapping innerHTML — simple, good enough
//   for a read-only coordinator view.
(function () {
  "use strict";

  const TOKEN_KEY   = "planscape_token";
  const REFRESH_KEY = "planscape_refresh";
  const USER_KEY    = "planscape_user";

  const state = {
    projects: [],
    projectId: null,
    view: "overview",
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
      }
    } catch (e) {
      main.innerHTML = `<div class="empty">Could not load: ${esc(String(e))}</div>`;
    }
  }

  // ── Renderers ─────────────────────────────────────────────────────────

  async function renderOverview(main) {
    const dash = await api(`/api/projects/${state.projectId}/dashboard`);
    const c = dash.compliance || {};
    const rag = (c.ragStatus || "RED").toLowerCase();
    main.innerHTML = `
      <h1>${esc(dash.project?.name || "")}</h1>
      <div class="kpi-grid">
        <div class="kpi ${rag}"><div class="value">${(c.tagPercent || 0).toFixed(0)}%</div><div class="label">Tag compliance</div></div>
        <div class="kpi"><div class="value">${c.totalElements ?? 0}</div><div class="label">Elements</div></div>
        <div class="kpi ${c.warningCount > 10 ? "amber" : ""}"><div class="value">${c.warningCount ?? 0}</div><div class="label">Warnings</div></div>
        <div class="kpi"><div class="value">${dash.openIssues ?? 0}</div><div class="label">Open issues</div></div>
      </div>
      <div class="card">
        <h3>Recent issues</h3>
        ${issuesTableHtml(dash.recentIssues || [])}
      </div>`;
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
  function fmtDate(v) { if (!v) return ""; try { return new Date(v).toLocaleDateString(); } catch { return v; } }
  function fmt(v, ccy) {
    if (v == null) return "";
    try { return new Intl.NumberFormat("en-GB", { style: "currency", currency: ccy || "GBP" }).format(v); }
    catch { return "£" + Number(v).toFixed(2); }
  }

  // ── Start ─────────────────────────────────────────────────────────────

  if (!getToken()) { showLogin(); } else { boot().catch(() => showLogin()); }
})();
