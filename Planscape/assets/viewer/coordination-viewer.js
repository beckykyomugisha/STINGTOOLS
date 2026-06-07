/* coordination-viewer.js — Corporate-grade BIM coordination layer.
   Sits on top of the existing Three.js viewer (window.STING_VIEWER) and
   adds: panel UI wiring, API client, clash + issue management, level
   selector, minimap, saved views, session history, navigation modes,
   keyboard shortcuts, and the issue creation modal.

   Loaded after the original viewer bootstraps so STING_VIEWER is ready. */

(function () {
  'use strict';

  const USE_MOCK_CLASHES = true;   // server endpoint may not exist yet

  // ── Boot guard — wait for STING_VIEWER to be ready ────────────────────
  // C3: bail with a visible error card after 30s so dependency failures
  // don't leave the user staring at an infinite spinner.
  function whenReady(cb) {
    const start = Date.now();
    (function poll() {
      if (window.STING_VIEWER && window.STING_VIEWER.scene) return cb();
      if (Date.now() - start > 30000) return showBootError();
      setTimeout(poll, 50);
    })();
  }
  function showBootError() {
    const div = document.createElement('div');
    div.style.cssText = 'position:fixed;inset:0;z-index:9999;display:flex;align-items:center;justify-content:center;background:#111318;color:#E8EAF0;font-family:Inter,sans-serif;padding:32px;text-align:center';
    div.innerHTML = `
      <div style="max-width:480px">
        <div style="font-size:32px;color:#EF4444;margin-bottom:12px">⚠</div>
        <h2 style="margin:0 0 8px;font-size:16px">Viewer failed to start</h2>
        <p style="color:#8892A4;font-size:13px;line-height:1.5">
          Three.js or the GLTF loader could not be loaded from
          <code style="background:#1C1F26;padding:2px 6px;border-radius:3px">assets/viewer/</code>.
          Check the network tab and that <code>three.min.js</code>,
          <code>GLTFLoader.js</code>, and <code>OrbitControls.js</code> are present.
        </p>
        <button onclick="location.reload()"
          style="margin-top:14px;padding:8px 16px;background:#0078D4;color:#fff;border:0;border-radius:6px;cursor:pointer">Retry</button>
      </div>`;
    document.body.appendChild(div);
  }
  whenReady(initCoordination);

  function initCoordination() {
    const V = window.STING_VIEWER;
    const THREE_ = window.THREE;
    // FEDERATION — the group holding EVERY loaded model root. The viz layer (resolver,
    // index, picking, aggregation) traverses this so appearance/isolate/colour span all
    // loaded models, not just the active one. Falls back to the single root pre-pivot.
    function vizGroup() { return (V && V.modelGroup) || (V && V.modelRoot) || null; }
    // V1 — keep only PICKABLE intersections: a mesh whose self + ancestors are visible AND
    // whose appearance state isn't ghost / x-ray / transparent. Mirrors the engine's
    // isPickableMesh so a click passes through ghosted/x-rayed elements to the solid behind.
    function pickableHits(hits) {
      return (hits || []).filter(h => {
        const o = h.object; if (!o) return false;
        for (let p = o; p; p = p.parent) { if (p.visible === false) return false; }
        const vm = o.userData && o.userData._vizMode;
        if (vm === 'ghost') return false;
        if (typeof vm === 'string' && (vm.indexOf('trans:') === 0 || vm === 'rmode:xray' || vm === 'rmode:ghost')) return false;
        return true;
      });
    }
    const params = new URLSearchParams(location.search);
    const projectId = params.get('project') || '';
    const modelId   = params.get('model')   || '';
    // U10 — resolve the API base from (in order): explicit window override
    // for embedders, user-saved Settings popover value (LAN/staging/on-prem),
    // build-time injected EXPO_PUBLIC_API_BASE, the URL ?api= param for
    // share-links from another origin, and finally the localhost fallback
    // so a fresh git-clone still works on dev. The Settings menu writes
    // `planscape_api_base` and reloads.
    const storedApi = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_api_base')) || '';
    // Same-origin fallback: when the viewer is SERVED by the API (the normal
    // case — including over a Cloudflare tunnel), call the API at the serving
    // origin so a remote guest never hits localhost. Only fall back to the
    // localhost literal for file:// opens (origin === 'null'/empty).
    const sameOrigin = (typeof window !== 'undefined' && window.location
                        && /^https?:/.test(window.location.origin || '')) ? window.location.origin : '';
    const apiBase   = window.__PLANSCAPE_API__
                   || storedApi
                   || params.get('api')
                   || sameOrigin
                   || 'http://localhost:5000';
    const token     = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_token')) || '';
    // Apply persisted theme on first paint so re-loads don't flash white.
    try {
      const t = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_theme')) || 'dark';
      document.documentElement.dataset.theme = t;
    } catch (_) {}

    // C2: when the viewer is loaded as a bundled asset inside the React
    // Native WebView (Asset.fromModule → file://) the host app supplies all
    // data via the existing bridge — fetching from http://localhost:5000
    // just spams CORS errors and falls back to mock data even when real
    // data is on its way through the bridge. Disable the network client
    // entirely in that case.
    const isFileScheme  = location.protocol === 'file:';
    const isWebView     = !!window.ReactNativeWebView;
    const apiEnabled    = !isFileScheme && !isWebView;
    // U8 — embedders (e.g., a parent dashboard rendering the viewer in an
    // iframe) can pass ?embed=1 to suppress the auto-redirect on 401 and
    // handle re-auth themselves.
    const embedMode     = params.get('embed') === '1';

    // ── State ───────────────────────────────────────────────────────────
    const state = {
      projectName: '',
      modelName: '',
      models: [],
      issues: [],
      clashes: [],
      elementMap: {},
      meshMeta: new Map(),     // mesh.uuid → meta (M0 resolver — verified at load)
      guidMeshes: new Map(),   // guid → mesh[] (multi-mesh elements)
      members: [{ id: 'me', name: 'You', initials: 'YO' },
                { id: 'sd', name: 'Sting Davis', initials: 'SD' },
                { id: 'se', name: 'Sentongo E.', initials: 'SE' }],
      activeDisciplines: new Set(),   // empty = all visible
      selectedElementGuid: null,      // PRIMARY (last-clicked) — kept for
                                      // backward-compat with downstream
                                      // single-element call sites.
      selectedElementGuids: new Set(),// FULL multi-selection set; supports
                                      // ctrl/cmd-click toggle, shift-click
                                      // range, and tree-multi-select.
      selectedElementMesh: null,
      selMeshes: new Set(),           // PART A — meshes currently carrying a
                                      // selection-highlight overlay (≠ appearance).
      selectedClashId: null,
      selectedIssueId: null,
      activeLevels: new Set(),
      levelBands: [],
      activeNav: 'orbit',
      activeTool: 'orbit',   // exclusive tool: orbit | pick | measure | markup | section
      issuesFilter: 'all',
      // X1 — clash filters are now two independent axes.
      clashStatusFilter: 'any',  // any | NEW | OPEN | RESOLVED
      clashTypeFilter:   'any',  // any | HARD | SOFT
      currentUser: null,
      bottomTab: 'clashes',
      rightTab: 'properties',
      savedViews: [],
      history: [],
      issuePins: new Map(),    // issueId → mesh
      clashPins: new Map(),    // clashId → mesh
      clashMarkersVisible: true,  // View menu toggle (default on) — clash wire-box markers
      issueMarkersVisible: true,  // View menu toggle (default on) — issue sphere markers
      glassMode: false,           // STOPGAP heuristic — glass categories semi-transparent
      photoPins: new Map(),    // Slice 4b — photoId → mesh
      photos: [],              // Slice 4b — list of SitePhotoDto rows
      photoFilters: { reason: 'any', audience: 'any' },
      photoCaptureSeed: {},
      photoReviewSelected: new Set(),
      elementMaterials: new Map(), // mesh.uuid → original material (for ghost / highlight)
      ghostMode: false,
      // ── Visualize (discipline-aware appearance) ──────────────────────────
      ghostStyle: { tint: 0x888888, opacity: 0.12 }, // user-tunable ghost look
      vizDiscMode: new Map(),   // DISCIPLINE → 'show' | 'ghost' | 'hide'
      vizCatMode:  new Map(),   // CATEGORY   → 'show' | 'ghost' | 'hide'
      vizDiscSel:  new Set(),   // P2 — disciplines ticked for multi-isolate
      vizCatSel:   new Set(),   // P2 — categories ticked for multi-isolate
      vizPreset:   null,        // active discipline appearance preset name
      // WS2 — disciplines/categories kept SOLID (original shaded material) when a
      // global x-ray / ghost render mode is on. The rest go x-ray/ghost; hide still
      // wins. Persisted alongside the viz maps.
      vizKeepSolidDisc: new Set(),
      vizKeepSolidCat:  new Set(),
      vizColour:   null,        // M1/M4 colour descriptor (token | preset | param) or null
      vizPalette:  'STING',     // M4 active palette set name
      vizCustomColours: new Map(), // B1 value/discipline → custom hex (overrides palette)
      vizTransp:   new Map(),   // C1 disc/cat → opacity 0..1 (continuous transparency)
      vizIsolation: null,       // C2 { mode:'isolate'|'hideOthers'|'hideSel', guids:Set } (transient)
      vizSearchQuery: '',       // C6 search text (kept across panel re-renders)
      vizSearchField: '*',      // C6 search field ('*' = any value, else token/param)
      federatedLoaded: false,   // FEDERATION — guard so we co-load the project once
      federationLoading: false, // V4 — true while sibling models stream in (suppress heavy work)
      modelVisible: new Map(),  // FEDERATION — modelId → shown? (checkbox state)
      colourMats:  new Map(),   // hex → shared coloured material (no per-mesh leak)
      transMats:   new Map(),   // opacity% → shared transparent material (C1)
      renderMode: 'shaded',
      applyingRemoteViz: false, // guard so a broadcast render-mode doesn't echo
      clashSection: { active: false, saved: null, onFocus: false }, // clip-plane section box
      apiBase, projectId, modelId, token
    };
    window.__COORD = state;  // debug handle

    // ── API helpers ─────────────────────────────────────────────────────
    let authChallenged = false;   // toast 401 once per session
    const API_TIMEOUT_MS = 12000;
    async function api(path, opts = {}) {
      if (!apiEnabled) return null;        // C2 — short-circuit on file:// / RN
      const headers = Object.assign({ 'Content-Type': 'application/json' }, opts.headers || {});
      if (token) headers['Authorization'] = `Bearer ${token}`;
      // T1 — multi-tenant: forward the user's selected tenant (if any).
      const tenantId = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_tenant')) || '';
      if (tenantId) { headers['X-Tenant'] = tenantId; state.tenantId = tenantId; }
      // B11 — abort the request after 12s so a hung backend doesn't leave
      // spinners stuck forever. Each call gets its own controller.
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), API_TIMEOUT_MS);
      try {
        const res = await fetch(`${apiBase}${path}`,
          Object.assign({ signal: controller.signal }, opts, { headers }));
        if (res.status === 401 && !authChallenged) {
          authChallenged = true;
          // U8 — toast immediately, then redirect to the dashboard's login
          // overlay (/index.html shows it automatically when there's no
          // token in localStorage). Stash the viewer URL in sessionStorage
          // so a future dashboard-side `?next=` handler can pick it up.
          // Embedders pass ?embed=1 to keep the viewer mounted and
          // re-auth themselves.
          toast('Sign-in expired — redirecting to login…', 'error');
          if (typeof localStorage !== 'undefined') {
            try { localStorage.removeItem('planscape_token'); } catch (_) {}
          }
          if (!embedMode) {
            const next = location.pathname + location.search;
            try { sessionStorage.setItem('planscape_post_login_next', next); } catch (_) {}
            setTimeout(() => { location.href = `${apiBase}/index.html`; }, 1500);
          }
        }
        if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
        const ct = res.headers.get('content-type') || '';
        return ct.includes('application/json') ? res.json() : res.text();
      } catch (err) {
        const aborted = err && err.name === 'AbortError';
        console.warn('[coord] api', path, aborted ? 'timeout' : err.message);
        return null;
      } finally {
        clearTimeout(timer);
      }
    }

    // ── DOM helpers ─────────────────────────────────────────────────────
    const $  = (sel, root = document) => root.querySelector(sel);
    const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
    function el(tag, attrs = {}, children = []) {
      const e = document.createElement(tag);
      for (const k in attrs) {
        if (k === 'class') e.className = attrs[k];
        else if (k === 'html') e.innerHTML = attrs[k];
        else if (k.startsWith('on') && typeof attrs[k] === 'function') e.addEventListener(k.slice(2), attrs[k]);
        else if (attrs[k] != null) e.setAttribute(k, attrs[k]);
      }
      (Array.isArray(children) ? children : [children]).forEach(c => {
        if (c == null) return;
        e.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
      });
      return e;
    }
    function escapeHtml(s) { return String(s == null ? '' : s).replace(/[&<>"]/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;' })[c]); }

    function toast(msg, kind = '') {
      const tray = $('#toasts');
      const t = el('div', { class: `toast ${kind}` }, msg);
      tray.appendChild(t);
      setTimeout(() => { t.style.opacity = '0'; setTimeout(() => t.remove(), 300); }, 3500);
    }

    function logHistory(label) {
      const now = new Date();
      state.history.unshift({ time: now, label, snapshot: captureViewState() });
      if (state.history.length > 50) state.history.pop();
      renderHistory();
    }

    // ── Initial render of static UI bits ───────────────────────────────
    // E1 — FAULT-ISOLATED init (STING_VIZ_E1_INIT). Previously these ran as a bare
    // sequence: the FIRST one to throw (e.g. an unguarded $('#x').addEventListener on
    // a missing element, or V.controls not ready) silently killed EVERY setup after
    // it — which is why the nav-mode buttons + bottom-ribbon toggles all went dead at
    // once. Each is now wrapped so one failure can't cascade, and the culprit is logged.
    function _si(name, fn) { try { fn(); } catch (e) { console.warn('[viewer init] "' + name + '" threw (others still run):', e); } }
    _si('header', setupHeader);
    _si('panelToggles', setupPanelToggles);
    _si('tabs', setupTabs);
    _si('bottomPanel', setupBottomPanel);
    _si('viewportOverlays', setupViewportOverlays);
    _si('keyboardShortcuts', setupKeyboardShortcuts);
    _si('keyNav', setupKeyNav);
    _si('modalHandlers', setupModalHandlers);
    _si('navControls', setupNavControls);
    _si('sectionCard', setupSectionCard);
    _si('help', setupHelp);
    _si('heartbeat', setupHeartbeat);
    _si('selectionToolbar', setupSelectionToolbar);
    _si('rowContextMenu', setupRowContextMenu);
    _si('canvasContextMenu', setupCanvasContextMenu);
    _si('viewCube', setupViewCube);
    _si('panelHandles', setupPanelHandles);
    _si('photoCaptureModal', setupPhotoCaptureModal);
    _si('photoReviewModal', setupPhotoReviewModal);
    _si('photoFab', setupPhotoFab);
    _si('photoRealtime', setupPhotoRealtime);
    console.log('[viewer] STING_VIZ_E1_INITGUARD nav+ribbon delegated, fault-isolated init');
    console.log('[viewer] STING_VIZ_BUILD viz-syspalette');
    renderProperties(null);
    renderHistory();
    updateBadges();
    updateRightTabCounts();              // X2 — initial empty counts

    // ── Bootstrap data ─────────────────────────────────────────────────
    bootstrap().catch(e => console.error('[coord] bootstrap', e));

    async function bootstrap() {
      // L2 / user chip — learn who's logged in so "Mine" filter and avatar
      // initials are correct in production.
      if (apiEnabled) {
        const me = await api('/api/auth/me');
        if (me && (me.id || me.userId)) {
          state.currentUser = me;
          const id = me.id || me.userId;
          const name = me.displayName || me.name || me.email || 'You';
          const initials = (name || 'YO').split(/[\s@]+/).filter(Boolean).map(s => s[0]).slice(0, 2).join('').toUpperCase();
          $('#userChip').textContent = initials || 'YO';
          $('#userChip').title = name;
          // Replace the placeholder "me" member with the real one.
          state.members = [{ id, name, initials }, ...state.members.filter(m => m.id !== 'me')];
        }
      }
      if (apiEnabled && !projectId) {
        // U5 — no project / model in URL: show empty-state CTA on first paint.
        showEmptyStateCTA();
        return;     // nothing else to bootstrap
      }

      const project = projectId ? await api(`/api/projects/${projectId}`) : null;
      if (project && project.name) {
        state.projectName = project.name;
        $('#breadcrumbProject').textContent = project.name;
      }

      // List models
      const modelsRes = projectId ? await api(`/api/projects/${projectId}/models`) : null;
      state.models = Array.isArray(modelsRes) ? modelsRes : (modelsRes?.items || []);
      const activeModel = state.models.find(m => m.id === modelId) || state.models[0];
      if (activeModel) {
        state.modelName = activeModel.name || activeModel.fileName || activeModel.id;
        $('#breadcrumbModel').textContent = state.modelName;
      }
      renderModels();

      // Element map
      if (projectId && modelId) {
        const map = await api(`/api/projects/${projectId}/models/${modelId}/element-map`);
        if (map) {
          state.elementMap = map;
          if (V && V.scene) {
            // forward to original viewer command so it can populate userData links
            handleHostCommand({ type: 'elementMap', payload: { map } });
          }
        }
      }
      buildModelTree();
      buildDisciplineChips();
      buildLevelStrip();
      if (state.rightTab === 'visualize') renderVisualizePanel();

      // Load model GLB
      // B1 — security: never put the JWT in the query string. Fetch the
      // GLB as a blob with a normal Authorization header, then hand
      // GLTFLoader a blob URL. This keeps the token out of browser
      // history, server access logs, and the Referer header.
      // R3 — wrap in the same AbortController + 401-redirect contract
      // the api() helper uses, with a longer 60s timeout because GLBs
      // can be 50-200 MB.
      // E-fix (0%-loading bug): the GLB is now STREAMED so the percentage
      // reflects real bytes downloaded. The old `await res.blob()` blocked
      // here for the whole download with no progress, then handed GLTFLoader
      // a blob: URL whose onProgress reports Content-Length 0 — so "0%" never
      // moved and a stalled/failed download just left a frozen overlay.
      // Failures are now surfaced ON the boot overlay (not a transient toast)
      // with a Retry affordance. The boot overlay lives in viewer.html; this
      // script runs in the same document so it drives #bootLoader directly.

      function bootLoaderEl() { return document.getElementById('bootLoader'); }
      function setBootProgress(pct, label) {
        const elp = document.getElementById('loadingProgress');
        if (elp) elp.textContent = (label != null) ? label : (pct != null ? pct + '%' : '');
      }
      function setBootMessage(msg) {
        const bl = bootLoaderEl();
        const m = bl && bl.querySelector('.msg');
        if (m) m.textContent = msg;
      }
      function resetBootLoader() {
        const bl = bootLoaderEl();
        if (!bl) return;
        bl.style.display = '';
        const sp = bl.querySelector('.spinner'); if (sp) sp.style.display = '';
        const retry = bl.querySelector('#bootRetryBtn'); if (retry) retry.style.display = 'none';
        setBootMessage('Loading model');
        setBootProgress(0, null);
      }
      function showBootError(msg, canRetry) {
        const bl = bootLoaderEl();
        toast(msg, 'error');                       // keep the toast too
        if (!bl) return;
        bl.style.display = '';                      // E.3 — keep the overlay up
        const sp = bl.querySelector('.spinner'); if (sp) sp.style.display = 'none';
        setBootMessage(msg);
        setBootProgress(null, '');
        let retry = bl.querySelector('#bootRetryBtn');
        if (canRetry) {
          if (!retry) {
            retry = document.createElement('button');
            retry.id = 'bootRetryBtn';
            retry.textContent = 'Retry';
            retry.style.cssText = 'margin-top:14px;padding:7px 20px;cursor:pointer;border-radius:6px;border:1px solid #2a6fd0;background:#1d6fd0;color:#fff;font:inherit;';
            retry.addEventListener('click', () => { resetBootLoader(); loadModelGlb(); });
            bl.appendChild(retry);
          }
          retry.style.display = '';
        } else if (retry) {
          retry.style.display = 'none';
        }
        // Terminal (non-retryable) failure: unblock meeting co-presence so the
        // session still connects even though this user's model didn't load.
        if (!canRetry) {
          try { window.STING_VIEWER && window.STING_VIEWER.markModelReady && window.STING_VIEWER.markModelReady(); } catch (_) {}
        }
      }

      // Stream the response body so the overlay shows real download progress.
      async function streamGlbWithProgress(res) {
        const total = Number(res.headers.get('Content-Length') || 0);
        // Older WebViews without a streaming body reader fall back to blob().
        if (!res.body || typeof res.body.getReader !== 'function') {
          setBootMessage('Downloading model');
          return await res.blob();
        }
        const reader = res.body.getReader();
        const chunks = [];
        let received = 0;
        setBootMessage('Downloading model');
        for (;;) {
          const { done, value } = await reader.read();
          if (done) break;
          chunks.push(value);
          received += value.length;
          if (total > 0) setBootProgress(Math.round(received / total * 100), null);
          else setBootProgress(null, (received / 1048576).toFixed(1) + ' MB');
        }
        setBootMessage('Preparing model');
        const type = res.headers.get('Content-Type') || 'model/gltf-binary';
        return new Blob(chunks, { type });
      }

      async function loadModelGlb() {
        const fileUrl = `${apiBase}/api/projects/${projectId}/models/${modelId}/file`;
        const headers = {};
        if (token) headers['Authorization'] = `Bearer ${token}`;
        const tenantId = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_tenant')) || state.tenantId;
        if (tenantId) headers['X-Tenant'] = tenantId;
        // E.5 — a missing token is the "Not signed in" case: say so on the
        // overlay instead of letting it hang or 401-loop.
        if (!token && !embedMode) {
          showBootError('Not signed in — open this model from the dashboard.', false);
          return;
        }
        const ctl = new AbortController();
        const t = setTimeout(() => ctl.abort(), 60000);
        try {
          const res = await fetch(fileUrl, { headers, cache: 'no-store', signal: ctl.signal });
          if (res.status === 401) {
            if (!authChallenged) {
              authChallenged = true;
              showBootError('Sign-in expired — redirecting to login…', false);
              try { localStorage.removeItem('planscape_token'); } catch (_) {}
              if (!embedMode) {
                // Same target as the api() helper above: dashboard's login
                // overlay at /index.html (the bare /login path is a SPA hash).
                const next = location.pathname + location.search;
                try { sessionStorage.setItem('planscape_post_login_next', next); } catch (_) {}
                setTimeout(() => { location.href = `${apiBase}/index.html`; }, 1500);
              }
            }
            return;
          }
          if (res.status === 404) {
            // ModelsController returns 404 {error:"storage_missing"} for a
            // fileless / stale model row (see audit C). Don't hang on it.
            showBootError('Model file not found — it may need re-publishing from the authoring tool.', false);
            return;
          }
          if (!res.ok) { showBootError(`Failed to load model (${res.status} ${res.statusText}).`, true); return; }
          const blob = await streamGlbWithProgress(res);
          const blobUrl = URL.createObjectURL(blob);
          if (state.lastBlobUrl) { try { URL.revokeObjectURL(state.lastBlobUrl); } catch (_) {} }
          state.lastBlobUrl = blobUrl;
          // BLK-2 — fetch the model's federation transform (best-effort) so the
          // engine places it in shared world space. Absent / identity → no-op.
          let transform = null;
          try { transform = await api(`/api/projects/${projectId}/models/${modelId}/transform`); } catch (_) {}
          handleHostCommand({ type: 'load', payload: { url: blobUrl, transform, modelId } });
        } catch (err) {
          const aborted = err && err.name === 'AbortError';
          console.warn('[coord] GLB fetch failed', aborted ? 'timeout' : err.message);
          showBootError(aborted
            ? 'Model download timed out after 60s.'
            : 'Failed to download model file — check your connection.', true);
        } finally {
          clearTimeout(t);
        }
      }

      if (projectId && modelId) {
        await loadModelGlb();
      } else {
        // No model to load on this view — unblock meeting co-presence (BLK-5)
        // so a model-less coordination session still connects.
        try { window.STING_VIEWER && window.STING_VIEWER.markModelReady && window.STING_VIEWER.markModelReady(); } catch (_) {}
      }

      // Project members — populates assignee + watcher pickers with the
      // real org/project roster instead of the hardcoded "Sting Davis /
      // Sentongo E." demo seed. Falls back silently to the seed list when
      // the endpoint is unavailable (offline, permission denied, etc.).
      await loadProjectMembers();

      // Issues + clashes + site photos (Slice 4b)
      await loadIssues();
      await loadClashes();
      await loadSitePhotos();
    }

    async function loadProjectMembers() {
      if (!projectId) return;
      const data = await api(`/api/projects/${projectId}/members`);
      const list = Array.isArray(data) ? data : (data?.items || data?.members || []);
      if (!list.length) return;     // keep demo seed when API empty/unauth
      const me = state.currentUser;
      const meId = me && (me.id || me.userId);
      const mapped = list.map(m => {
        const id   = m.userId || m.UserId || m.id || m.Id;
        const name = m.displayName || m.DisplayName || m.email || m.Email || 'User';
        const email = m.email || m.Email || '';
        const role = m.iso19650Role || m.Iso19650Role || m.projectRole || m.ProjectRole || '';
        const initials = (name || 'U').split(/[\s@]+/).filter(Boolean).map(s => s[0]).slice(0, 2).join('').toUpperCase();
        return { id, name, email, role, initials };
      });
      // Keep "me" pinned at top of the list for ergonomics.
      const sorted = mapped.sort((a, b) => {
        if (a.id === meId) return -1;
        if (b.id === meId) return 1;
        return (a.name || '').localeCompare(b.name || '');
      });
      state.members = sorted;
    }

    // Forward a command to the original viewer's handleCommand by dispatching
    // a 'message' event. This is the same path RN uses.
    function handleHostCommand(cmd) {
      try {
        const ev = new MessageEvent('message', { data: JSON.stringify(cmd) });
        window.dispatchEvent(ev);
      } catch (e) { console.warn('host cmd', e); }
    }

    // ── Header ──────────────────────────────────────────────────────────
    function setupHeader() {
      $('#btnToggleLeft').addEventListener('click', () => {
        document.querySelector('.app-shell').classList.toggle('left-collapsed');
        savePanelState(); onResize(); updatePanelHandles();
      });
      $('#btnToggleRight').addEventListener('click', () => {
        document.querySelector('.app-shell').classList.toggle('right-collapsed');
        savePanelState(); onResize(); updatePanelHandles();
      });

      // Dropdown menus
      bindMenu('#btnMeasure', '#menuMeasure', {
        '#mPP':    () => { setActiveTool('measure'); toast('Measure: pick two points'); },
        '#mArea':  () => { handleHostCommand({ type: 'startArea' }); toast('Area: tap points, double-click to close'); },
        '#mAngle': () => { startAngleTool(); },
        '#mClear': () => { handleHostCommand({ type: 'clearMeasure' }); state.angleTool = false; state.anglePoints = []; }
      });
      bindMenu('#btnSection', '#menuSection', {
        '#sX':    () => openSectionPlane('x'),
        '#sY':    () => openSectionPlane('y'),
        '#sZ':    () => openSectionPlane('z'),
        '#sFree': () => openSectionPlane('free'),
        '#sBox':  () => openSectionPlane('box'),
        '#sClr':  () => clearSection()
      });
      bindMenu('#btnView', '#menuView', {
        '#vShaded':    () => setRenderMode('shaded'),
        '#vWire':      () => setRenderMode('wire'),
        '#vXray':      () => setRenderMode('xray'),
        '#vGhost':     () => setRenderMode('ghost'),
        '#vRealistic': () => setRenderMode('realistic'),
        '#vEdges':     () => toggleEdgeOverlay(),
        '#vCaps':      () => toggleSectionCaps(),
        '#vClashMarkers': () => toggleClashMarkers(),
        '#vIssueMarkers': () => toggleIssueMarkers(),
        '#vGlass':        () => toggleGlass(),
        '#vCoords':    () => toggleCoordReadout(),
        '#vExplode':   () => toggleExplodedView(),
        '#vTop':       () => setCameraPreset('top'),
        '#vFront':     () => setCameraPreset('front'),
        '#vSide':      () => setCameraPreset('right'),
        '#vIso':       () => setCameraPreset('iso'),
        '#vBmSave':    () => saveBookmark(1),
        '#vBmRestore': () => restoreBookmark(1),
      });
      // B — labelled toolbar toggles for clash / issue markers (in addition to the View ▾
      // items); reflect the on/off state on the button. Default on (markers visible).
      $('#tbClashMarkers')?.addEventListener('click', () => toggleClashMarkers());
      $('#tbIssueMarkers')?.addEventListener('click', () => toggleIssueMarkers());
      paintMarkerBtn('tbClashMarkers', state.clashMarkersVisible);
      paintMarkerBtn('tbIssueMarkers', state.issueMarkersVisible);
      bindMenu('#btnIssues', '#menuIssues', {
        '#iCreate': () => openIssueModal(),
        '#iMine':   () => { state.issuesFilter = 'mine'; switchBottomTab('issues'); renderIssues(); },
        '#iAll':    () => { state.issuesFilter = 'all'; switchBottomTab('issues'); renderIssues(); }
      });
      bindMenu('#btnMarkup', '#menuMarkup', {
        '#mkScreenshot': () => takeScreenshot(),
        '#mkShare':      () => shareCurrentView(),
        '#mkText':    () => { startMarkupTool('text');    toast('Markup: click a surface to place text'); },
        '#mkArrow':   () => { startMarkupTool('arrow');   toast('Markup: drag from tail to head'); },
        '#mkDraw':    () => { startMarkupTool('draw');    toast('Markup: drag to draw freehand'); },
        '#mkCloud':   () => { startMarkupTool('cloud');   toast('Markup: drag a box for a revision cloud'); },
        '#mkDim':     () => { startMarkupTool('dim');     toast('Markup: click two points for a dimension'); },
        '#mkCallout': () => { startMarkupTool('callout'); toast('Markup: click a point, then a label spot'); },
        '#mkClear':   () => { handleHostCommand({ type: 'clearMarkup' }); toast('Markups cleared'); },
      });
      bindMenu('#btnMeet', '#menuMeet', {
        '#meetStart': () => startMeeting(),
        '#meetJoin':  () => joinMeeting(),
        '#meetCopy':  () => copyMeetingLink(),
      });

      $('#btnClashes').addEventListener('click', () => switchBottomTab('clashes'));
      $('#btnIssueBadge').addEventListener('click', () => switchBottomTab('issues'));
      $('#btnHelp').addEventListener('click', () => $('.help-overlay').classList.add('open'));
      $('#btnSettings').addEventListener('click', (e) => { e.stopPropagation(); openSettingsMenu(); });
      $('#btnNotifs').addEventListener('click', () => toast(`${state.issues.filter(i => i.status !== 'RESOLVED').length} open issues`));

      // ── Navigation: brand and breadcrumb make the viewer feel like part
      // of the wider Planscape app instead of a leaf page. They link to
      // the parent shell (the static planscape-site / API "/app" route)
      // when one is reachable, and otherwise fall back gracefully.
      // C — explicit "← Back" that always works: native history.back() when there's a
      // prior entry (no forced refresh — the dashboard restores from bfcache), else fall
      // back to the project dashboard / projects home. Opening the viewer is a normal
      // navigation (location.href), so a history entry exists and browser Back works too.
      const backBtn = $('#btnBack');
      if (backBtn) backBtn.addEventListener('click', (e) => {
        e.preventDefault();
        if (window.ReactNativeWebView) { window.ReactNativeWebView.postMessage(JSON.stringify({ type: 'navigateBack' })); return; }
        if (window.history.length > 1) { window.history.back(); return; }
        location.href = projectId ? (apiBase ? `${apiBase}/app/projects/${projectId}` : `/app/projects/${projectId}`)
                                  : (apiBase ? `${apiBase}/app/projects` : '/app/projects');
      });
      const brand = $('#brandHome');
      if (brand) brand.addEventListener('click', (e) => {
        e.preventDefault();
        // Prefer the API host's /app/projects route, else the current
        // origin's /app/projects, else just /index.html.
        const target = (apiBase ? `${apiBase}/app/projects` : '/app/projects');
        if (window.ReactNativeWebView) {
          // Inside the mobile WebView, post a "navigate home" message and
          // let the React Native host pop the navigation stack.
          window.ReactNativeWebView.postMessage(JSON.stringify({ type: 'navigateHome' }));
        } else {
          location.href = target;
        }
      });
      const crumb = $('#breadcrumbProject');
      if (crumb) crumb.addEventListener('click', (e) => {
        e.preventDefault();
        if (!projectId) return;
        const target = (apiBase ? `${apiBase}/app/projects/${projectId}` : `/app/projects/${projectId}`);
        if (window.ReactNativeWebView) {
          window.ReactNativeWebView.postMessage(JSON.stringify({ type: 'navigateProject', projectId }));
        } else {
          location.href = target;
        }
      });

      setupSettingsMenu();
    }

    // ── Settings popover ─────────────────────────────────────────────────
    // Replaces the previous "TODO" no-op. Persists API base + tenant +
    // theme to localStorage; reload makes them effective on next bootstrap.
    function setupSettingsMenu() {
      const menu = $('#settingsMenu');
      if (!menu) return;
      // Hydrate inputs from current config + storage.
      const api = ($('#settingApiBase')); if (api) api.value = apiBase || '';
      const tenant = ($('#settingTenant'));
      if (tenant) tenant.value = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_tenant')) || '';
      const theme = ($('#settingTheme'));
      if (theme) theme.value = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_theme')) || 'dark';
      // Eye height + walk speed hydrate from the same keys viewer-extras +
      // setupNavControls read at runtime.
      const eyeH = $('#settingEyeHeight');
      if (eyeH) {
        const stored = parseFloat(localStorage.getItem('planscape_eye_height_m'));
        eyeH.value = !isNaN(stored) && stored > 0 ? stored : '';
      }
      const ws = $('#settingWalkSpeed');
      if (ws) {
        const stored = parseFloat(localStorage.getItem('planscape_walk_speed'));
        ws.value = !isNaN(stored) && stored > 0 ? stored : '';
      }

      $('#settingsCancel')?.addEventListener('click', () => menu.classList.remove('open'));
      $('#settingsSave')?.addEventListener('click', () => {
        try {
          const apiVal    = $('#settingApiBase')?.value.trim() || '';
          const tenantVal = $('#settingTenant')?.value.trim() || '';
          const themeVal  = $('#settingTheme')?.value || 'dark';
          const eyeVal    = parseFloat($('#settingEyeHeight')?.value || '');
          const wsVal     = parseFloat($('#settingWalkSpeed')?.value || '');
          if (apiVal)    localStorage.setItem('planscape_api_base', apiVal);
          else           localStorage.removeItem('planscape_api_base');
          if (tenantVal) localStorage.setItem('planscape_tenant', tenantVal);
          else           localStorage.removeItem('planscape_tenant');
          localStorage.setItem('planscape_theme', themeVal);
          if (!isNaN(eyeVal) && eyeVal >= 0.6 && eyeVal <= 2.4) {
            localStorage.setItem('planscape_eye_height_m', String(eyeVal));
          } else if ($('#settingEyeHeight')?.value === '') {
            localStorage.removeItem('planscape_eye_height_m');
          }
          if (!isNaN(wsVal) && wsVal > 0 && wsVal <= 8) {
            localStorage.setItem('planscape_walk_speed', String(wsVal));
            window.__walkSpeedMul = wsVal;
          }
          document.documentElement.dataset.theme = themeVal;
          toast('Settings saved — reloading…', 'success');
          setTimeout(() => location.reload(), 600);
        } catch (e) {
          toast('Could not save settings: ' + (e.message || e), 'error');
        }
      });
      // Close on outside click.
      document.addEventListener('click', (ev) => {
        if (!menu.classList.contains('open')) return;
        if (ev.target.closest('#settingsMenu') || ev.target.closest('#btnSettings')) return;
        menu.classList.remove('open');
      });
    }
    function openSettingsMenu() {
      const menu = $('#settingsMenu');
      if (!menu) return;
      menu.classList.toggle('open');
    }

    function bindMenu(triggerSel, menuSel, items) {
      const trigger = $(triggerSel);
      const menu    = $(menuSel);
      if (!trigger || !menu) return;
      trigger.addEventListener('click', (e) => {
        e.stopPropagation();
        $$('.menu.open').forEach(m => { if (m !== menu) m.classList.remove('open'); });
        menu.classList.toggle('open');
        const r = trigger.getBoundingClientRect();
        menu.style.left = r.left + 'px';
        menu.style.top  = r.bottom + 4 + 'px';
      });
      for (const sel in items) {
        const it = $(sel, menu);
        if (it) it.addEventListener('click', () => { menu.classList.remove('open'); items[sel](); });
      }
    }

    // ── Live meetings entry point ─────────────────────────────────────────────
    // The realtime co-presence client (meeting-sync.js) is URL-driven: it only
    // activates with ?meeting=<sessionId>. These controls CREATE a session via
    // the existing API, copy a shareable join link, and reload into it so the
    // client connects (camera-follow + presence + overlay co-presence).
    function meetingJoinUrl(sessionId) {
      const u = new URL(location.href);
      u.searchParams.set('meeting', sessionId);
      if (projectId) u.searchParams.set('project', projectId);
      if (modelId) u.searchParams.set('model', modelId);
      return u.toString();
    }
    function copyToClipboard(text) {
      try {
        if (navigator.clipboard && navigator.clipboard.writeText) { navigator.clipboard.writeText(text); return; }
      } catch (_) {}
      try {
        const ta = document.createElement('textarea');
        ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select();
        document.execCommand('copy'); ta.remove();
      } catch (_) {}
    }
    async function startMeeting() {
      if (!projectId) return toast('No project — cannot start a meeting', 'warn');
      const displayName = (() => { try { return (localStorage.getItem('planscape_user') || 'Host').split('@')[0]; } catch (_) { return 'Host'; } })();
      const resp = await api(`/api/projects/${projectId}/meeting-sessions`, {
        method: 'POST',
        body: JSON.stringify({ modelId: modelId || null, displayName, surface: 'web' }),
      });
      const id = resp && (resp.id || resp.Id);
      if (!id) return toast('Could not start meeting (check sign-in)', 'error');
      const link = meetingJoinUrl(id);
      copyToClipboard(link);
      toast('Meeting started — join link copied. Opening session…');
      logHistory && logHistory('Started a live meeting');
      // Reload this tab INTO the meeting so meeting-sync.js activates.
      setTimeout(() => { location.href = link; }, 700);
    }
    function joinMeeting() {
      const id = (prompt('Paste the meeting session ID to join:') || '').trim();
      if (!id) return;
      location.href = meetingJoinUrl(id);
    }
    function copyMeetingLink() {
      const cur = new URLSearchParams(location.search).get('meeting');
      if (!cur) return toast('Not in a meeting yet — Start one first', 'warn');
      copyToClipboard(meetingJoinUrl(cur));
      toast('Join link copied to clipboard');
    }
    document.addEventListener('click', () => $$('.menu.open').forEach(m => m.classList.remove('open')));

    // ── Panel section toggles ───────────────────────────────────────────
    function setupPanelToggles() {
      $$('.panel-section-header').forEach(h => {
        h.addEventListener('click', (e) => {
          if (e.target.closest('.add-btn')) return;
          h.parentElement.classList.toggle('closed');
          h.parentElement.classList.toggle('open');
        });
      });
    }

    // ── Models + discipline chips ──────────────────────────────────────
    function buildDisciplineChips() {
      const wrap = $('#discChips');
      wrap.innerHTML = '';
      // R10 — without an element-map there's nothing to filter against,
      // so the chips would be visually present but functionally inert.
      // Hide them entirely until we have real metas and reveal once the
      // map arrives (re-called by bootstrap and the boot observer).
      const haveMap = state.elementMap && Object.keys(state.elementMap).length > 0;
      wrap.style.display = haveMap ? '' : 'none';
      if (!haveMap) return;
      const disciplines = new Set();
      Object.values(state.elementMap).forEach(m => {
        if (m && m.discipline) disciplines.add(String(m.discipline).toUpperCase().slice(0, 4));
      });
      Array.from(disciplines).sort().forEach(d => {
        const chip = el('button', { class: 'disc-chip', 'data-disc': d }, d);
        chip.addEventListener('click', (e) => {
          if (e.shiftKey) {
            chip.classList.toggle('active');
          } else {
            const wasActive = chip.classList.contains('active');
            $$('.disc-chip').forEach(c => c.classList.remove('active'));
            if (!wasActive) chip.classList.add('active');
          }
          const active = $$('.disc-chip.active').map(c => c.dataset.disc);
          applyDisciplineFilter(active);
        });
        wrap.appendChild(chip);
      });
    }

    function applyDisciplineFilter(active) {
      state.activeDisciplines = new Set(active);
      const filterAll = active.length === 0;
      if (!V.modelRoot || !state.elementMap) return;
      vizGroup().traverse(obj => {
        if (!obj.isMesh) return;
        const meta = metaForMesh(obj);
        const disc = meta?.discipline ? String(meta.discipline).toUpperCase().slice(0, 4) : null;
        if (filterAll) {
          obj.visible = true;
          restoreOriginalMaterial(obj);
        } else if (disc && active.includes(disc)) {
          obj.visible = true;
          restoreOriginalMaterial(obj);
        } else {
          ghostMaterial(obj);
        }
      });
    }

    // L5 — track materials we've cloned/replaced so we can dispose them.
    // Each entry is { original, replacement } so we restore the original
    // and free the replacement's GPU resources on clear.
    function rememberOriginal(mesh) {
      if (!state.elementMaterials.has(mesh.uuid)) {
        // A1 — the original is ALWAYS the load-time true original, never the
        // mesh's current (possibly already-swapped) material.
        if (!mesh.userData._trueOrig) mesh.userData._trueOrig = mesh.material;
        state.elementMaterials.set(mesh.uuid, { original: mesh.userData._trueOrig, replacement: null });
      }
    }
    // Materials the appearance engine SHARES across many meshes must never be
    // disposed per-mesh: the single ghost material (M2) + the per-hex colour
    // materials (M1). Disposing one would blank every other mesh using it.
    function isSharedMat(m) { return !!(m && m.userData && (m.userData.stingGhost || m.userData.stingColour)); }
    function setReplacement(mesh, mat) {
      rememberOriginal(mesh);
      const slot = state.elementMaterials.get(mesh.uuid);
      // Dispose any previous replacement (avoids GPU leak when the same mesh gets
      // ghosted then highlighted then ghosted again) — but NOT shared materials.
      if (slot.replacement && slot.replacement !== mat && !isSharedMat(slot.replacement) && typeof slot.replacement.dispose === 'function') {
        try { slot.replacement.dispose(); } catch (_) {}
      }
      slot.replacement = mat;
      mesh.material = mat;
    }
    // M2 — ONE shared ghost material for the whole scene. Tint/opacity edits mutate
    // it in place (instant, no traverse, no per-mesh leak).
    let ghostSharedMat = null;
    function getGhostMaterial() {
      if (!ghostSharedMat) {
        const gs = state.ghostStyle || { tint: 0x888888, opacity: 0.12 };
        ghostSharedMat = new THREE_.MeshStandardMaterial({
          color: gs.tint, transparent: true, opacity: gs.opacity,
          depthWrite: false, side: THREE_.DoubleSide,
        });
        ghostSharedMat.userData = { stingGhost: true };
      }
      return ghostSharedMat;
    }
    function ghostMaterial(mesh) { setReplacement(mesh, getGhostMaterial()); }
    function restoreOriginalMaterial(mesh) {
      const slot = state.elementMaterials.get(mesh.uuid);
      mesh.material = mesh.userData._trueOrig || (slot && slot.original) || mesh.material;
      if (slot && slot.replacement && !isSharedMat(slot.replacement) && typeof slot.replacement.dispose === 'function') {
        try { slot.replacement.dispose(); } catch (_) {}
      }
      state.elementMaterials.delete(mesh.uuid);
    }
    // ════════════════════════════════════════════════════════════════════════
    // PART A — SELECTION LAYER (an OVERLAY on the appearance layer, never clobbers
    // it). The highlight is a CLONE of the mesh's current appearance-resolved
    // material + emissive; tracked in its own set; never routed through
    // state.elementMaterials. STING_VIZ_LAYERED_A — served-artifact marker.
    // ════════════════════════════════════════════════════════════════════════
    // Render-mode "lens" materials (View menu) — shared + cached so they compose
    // through applyAppearance without per-mesh leaks.
    const _rmodeMats = {};
    function renderModeMaterial(m) {
      if (_rmodeMats[m]) return _rmodeMats[m];
      let mat;
      if (m === 'wire')       mat = new THREE_.MeshBasicMaterial({ color: 0x60A5FA, wireframe: true });
      else if (m === 'xray')  mat = new THREE_.MeshStandardMaterial({ color: 0xFFFFFF, transparent: true, opacity: 0.25, depthWrite: false });
      else if (m === 'ghost') mat = new THREE_.MeshStandardMaterial({ color: 0x888888, transparent: true, opacity: 0.35, depthWrite: false });
      else return null;
      mat.userData = { stingColour: true };   // shared — isSharedMat protects it
      _rmodeMats[m] = mat;
      return mat;
    }
    // The mesh's current BASE appearance material (what it shows when NOT selected):
    // its appearance replacement (ghost/colour/render-mode) or the true original.
    function appearanceMaterialOf(o) {
      const slot = state.elementMaterials.get(o.uuid);
      if (slot && slot.replacement) return slot.replacement;
      return o.userData._trueOrig || o.material;
    }
    // Add / refresh the selection overlay on a mesh: clone its appearance material
    // and add emissive. Re-clones only when the underlying appearance changed.
    function addSelHighlight(o) {
      if (!o) return;
      const base = appearanceMaterialOf(o);
      let clone = o.userData._selClone;
      if (!clone || o.userData._selSrc !== base) {
        if (clone && !isSharedMat(clone) && typeof clone.dispose === 'function') { try { clone.dispose(); } catch (_) {} }
        clone = base.clone();
        if (clone.emissive) { clone.emissive.setHex(0xF97316); clone.emissiveIntensity = 0.5; }
        clone.userData = { stingSel: true };
        o.userData._selClone = clone;
        o.userData._selSrc = base;
      }
      o.material = clone;
      state.selMeshes.add(o);
    }
    function removeSelHighlight(o) {
      if (!o) return;
      o.material = appearanceMaterialOf(o);          // back to the appearance material
      const clone = o.userData._selClone;
      if (clone && !isSharedMat(clone) && typeof clone.dispose === 'function') { try { clone.dispose(); } catch (_) {} }
      o.userData._selClone = null; o.userData._selSrc = null;
      state.selMeshes.delete(o);
    }
    // Re-overlay the selection on the CURRENT appearance: drop highlights from
    // de-selected meshes, (re)add to selected ones. Cheap — touches only the
    // selection set, not the whole scene. Called by selection changes AND at the
    // end of applyAppearance so a viz change re-clones on the new base material.
    function reapplySelection() {
      const want = state.selectedElementGuids;
      Array.from(state.selMeshes).forEach(o => {
        if (!want.has(o.userData.elementGuid)) removeSelHighlight(o);
      });
      want.forEach(g => {
        const meshes = (state.guidMeshes && state.guidMeshes.get(g)) || [findMeshByGuid(g)].filter(Boolean);
        meshes.forEach(m => addSelHighlight(m));
      });
    }

    // A2 — full reset to the true original (used by clash/issue FOCUS, which then
    // applies its own materials). Clears BOTH the selection overlay AND the
    // appearance replacements, and resets the dirty-flags so the next
    // applyAppearance re-evaluates every mesh. (Selection alone uses
    // reapplySelection, which never wipes the appearance — that was the
    // "click resets to shaded" bug.)
    function clearAllHighlights() {
      Array.from(state.selMeshes).forEach(o => removeSelHighlight(o));
      state.selMeshes.clear();
      if (!V.modelRoot) { state.elementMaterials.clear(); return; }
      const ids = Array.from(state.elementMaterials.keys());
      vizGroup().traverse(o => {
        if (!o.isMesh) return;
        o.userData._vizMode = undefined;
        o.userData._paintKey = undefined;
        if (state.elementMaterials.has(o.uuid)) restoreOriginalMaterial(o);
      });
      ids.forEach(id => state.elementMaterials.delete(id));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Visualize — discipline-aware appearance. Reuses the existing material
    // plumbing (ghostMaterial / restoreOriginalMaterial / setReplacement) and
    // the engine overlay (V.applyOverlay / V.clearOverlay) — nothing new on
    // the render side.
    // ════════════════════════════════════════════════════════════════════════
    const VIZ_PALETTE = ['#3B82F6','#22C55E','#F59E0B','#A855F7','#EC4899','#14B8A6',
                         '#F97316','#EF4444','#84CC16','#06B6D4','#8B5CF6','#F43F5E',
                         '#10B981','#EAB308','#6366F1','#D946EF','#0EA5E9','#65A30D'];
    const NOVAL = '<No Value>';
    // M4 — named palette sets. Categorical schemes cycle the list; numeric/gradient
    // schemes interpolate across it. RAG + Viridis (colourblind-safe) + Spectral + Mono.
    const PALETTE_SETS = {
      'STING':      ['#3498db','#e67e22','#f1c40f','#2ecc71','#9b59b6','#e74c3c','#1abc9c','#95a5a6','#e84393','#00b894','#fdcb6e','#6c5ce7'],
      'RAG':        ['#2ecc71','#f1c40f','#e74c3c'],
      'Spectral':   ['#9e0142','#d53e4f','#f46d43','#fdae61','#fee08b','#e6f598','#abdda4','#66c2a5','#3288bd','#5e4fa2'],
      'Viridis':    ['#440154','#482878','#3e4a89','#31688e','#26828e','#1f9e89','#35b779','#6ece58','#b5de2b','#fde725'],
      'Monochrome': ['#2b2b2b','#444','#5f5f5f','#7a7a7a','#969696','#b3b3b3','#d0d0d0','#ededed'],
    };
    function hexToRgb(h) { const n = parseInt(String(h).replace('#', ''), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }
    function rgbToHex(r, g, b) { return '#' + [r, g, b].map(x => ('0' + Math.round(x).toString(16)).slice(-2)).join(''); }
    function lerpHex(a, b, f) { const pa = hexToRgb(a), pb = hexToRgb(b); return rgbToHex(pa[0] + (pb[0] - pa[0]) * f, pa[1] + (pb[1] - pa[1]) * f, pa[2] + (pb[2] - pa[2]) * f); }
    function lerpRamp(stops, t) {
      if (!stops || !stops.length) return '#888';
      if (t <= 0) return stops[0];
      if (t >= 1) return stops[stops.length - 1];
      const seg = t * (stops.length - 1), i = Math.floor(seg);
      return lerpHex(stops[i], stops[i + 1], seg - i);
    }
    function rampColour(col, v) {
      const t = (col.max > col.min) ? Math.max(0, Math.min(1, (v - col.min) / (col.max - col.min))) : 0;
      return lerpRamp(col.ramp || PALETTE_SETS.Viridis, t);
    }
    // BUILD 5 — per-discipline appearance presets (GLB textures are limited, so
    // these give a clean discipline read). _other = everything not listed.
    const DISC_PRESETS = {
      'Discipline':   { A:'#3498db', M:'#e67e22', E:'#f1c40f', P:'#2ecc71', S:'#9b59b6', FP:'#e74c3c', LV:'#1abc9c', G:'#95a5a6', _other:'#5b6472' },
      'MEP palette':  { M:'#e67e22', E:'#f1c40f', P:'#2ecc71', FP:'#e74c3c', LV:'#1abc9c', _other:'#3a3f4a' },
      'Struct palette':{ S:'#9b59b6', _other:'#3a3f4a' },
      'Arch palette': { A:'#3498db', _other:'#3a3f4a' },
    };

    function tokenValue(meta, token) {
      if (!meta) return '';
      switch (token) {
        case 'DISC': return meta.discipline || meta.disc || meta.DISC || '';
        case 'LOC':  return meta.loc || meta.location || meta.LOC || '';
        case 'ZONE': return meta.zone || meta.ZONE || '';
        case 'SYS':  return meta.system || meta.sys || meta.SYS || '';
        case 'LVL':  return meta.level || meta.lvl || meta.LVL || '';
        case 'FUNC': return meta.func || meta.function || meta.FUNC || '';
        case 'PROD': return meta.prod || meta.product || meta.PROD || '';
        case 'SEQ':  return meta.seq || meta.sequence || meta.SEQ || '';
        case 'TAG':  return meta.tag || meta.assTag || meta.ASS_TAG_1 || meta.tag1 || '';
        case 'CAT':  return meta.category || '';
        default: return '';
      }
    }
    function discKey(meta) { return String(tokenValue(meta, 'DISC') || '').toUpperCase().slice(0, 4); }
    // M4 — distinct scalar meta keys (for "colour by parameter"). Skips objects +
    // the keys already exposed as dedicated colour-by-tag buttons.
    function paramKeys() {
      const skip = new Set(['name', 'tag', 'STING_TAG', 'elementId', 'discipline', 'category', 'system', 'level', 'func', 'prod']);
      const keys = new Set();
      Object.values(state.elementMap || {}).forEach(m => {
        if (!m || typeof m !== 'object') return;
        Object.keys(m).forEach(k => { if (!skip.has(k) && m[k] != null && typeof m[k] !== 'object') keys.add(k); });
      });
      return Array.from(keys).sort();
    }
    function distinctTokens(token) {
      const s = new Set();
      if (state.elementMap) Object.values(state.elementMap).forEach(m => {
        let v = String(tokenValue(m, token) || '').trim();
        if (!v) return;
        if (token === 'DISC') v = v.toUpperCase().slice(0, 4);
        s.add(v);
      });
      return Array.from(s).sort();
    }

    // BUILD 1 — apply the per-discipline + per-category show/ghost/hide modes.
    // Most-restrictive wins (hide > ghost > show).
    // PART A — the SINGLE appearance resolver. One traverse, dirty-flagged. Each
    // mesh gets exactly one base state: hidden | ghost | colour:hex | rmode:lens |
    // original. The render mode (View menu) is a GLOBAL lens that COMPOSES here
    // rather than via its own traversal/store. Selection is re-overlaid at the end.
    function applyVizModes() {
      if (!V.modelRoot) return;
      if (V.activeOverlaySource && V.clearOverlay) { V.clearOverlay(); }   // retire any legacy overlay store
      // 'realistic' is a renderer-global (env+tonemap), NOT a per-mesh lens → treat as
      // 'shaded' here so meshes keep their real (base) materials for the IBL to light.
      const rmode = (state.renderMode && state.renderMode !== 'shaded' && state.renderMode !== 'realistic') ? state.renderMode : null;
      const iso = state.vizIsolation;   // C2 selection-driven isolation (or null)
      vizGroup().traverse(o => {
        if (!o.isMesh) return;
        const meta = metaForMesh(o);
        const disc = discOf(meta), cat = catKey(meta);
        const dm = state.vizDiscMode.get(disc);
        const cm = state.vizCatMode.get(cat);
        // Precedence: hide > transparency(slider) > ghost(button) > colour > show.
        let mode;
        // C1 — a per-discipline/category transparency slider (< 100%) overrides ghost.
        const tv = state.vizTransp.has(disc) ? state.vizTransp.get(disc)
                 : (state.vizTransp.has(cat) ? state.vizTransp.get(cat) : null);
        if (dm === 'hide' || cm === 'hide') mode = 'hide';
        else if (tv != null && tv < 1) mode = 'trans:' + Math.round(tv * 100);
        else if (dm === 'ghost' || cm === 'ghost') mode = 'ghost';
        else if (state.vizColour) {
          const col = state.vizColour;
          const v = colourValueOf(col, meta, o.userData.elementGuid);
          const key = colourKey(col, v);
          if (col.hidden && col.hidden.has(key)) mode = 'hide';                 // legend shift-click
          else if (col.isolate != null && key !== col.isolate) mode = 'ghost';  // legend isolate → ghost rest
          else {
            // P3a — when colouring by DISCIPLINE (preset) or by discipline+variants, a
            // custom CATEGORY colour OVERRIDES the discipline/variant colour (was: discipline
            // won). Other schemes (system/param/status) keep their own resolution.
            let c;
            if ((col.kind === 'preset' || col.kind === 'discVariants') && state.vizCustomColours.has(cat)) c = state.vizCustomColours.get(cat);
            else c = colourForValue(col, v);
            mode = c ? ('colour:' + c) : 'show';
          }
        } else {
          // B1 — a per-discipline/category custom colour applies directly even with
          // no active colour scheme (the scheme path already honours it via colourForValue).
          const custom = state.vizCustomColours.get(disc) || state.vizCustomColours.get(cat);
          mode = custom ? ('colour:' + custom) : 'show';
        }
        // A4 — global render-mode lens applies UNDER colour/ghost/hide: only meshes
        // that resolved to plain 'show' (and aren't kept-solid) take the lens.
        if (mode === 'show' && rmode) {
          const keepSolid = state.vizKeepSolidDisc.has(disc) || state.vizKeepSolidCat.has(cat);
          if (!keepSolid) mode = 'rmode:' + rmode;
        }
        // C2 — selection-driven isolation composes ON TOP: in-set elements keep their
        // resolved appearance (colour/transparency/show); out-of-set get ghosted (isolate)
        // or hidden (hide-others); hide-selection hides the in-set ones.
        if (iso) {
          const inSet = iso.guids.has(o.userData.elementGuid);
          if (iso.mode === 'isolate' && !inSet && mode !== 'hide') mode = 'ghost';
          else if (iso.mode === 'hideOthers' && !inSet) mode = 'hide';
          else if (iso.mode === 'hideSel' && inSet) mode = 'hide';
        }
        applyMeshState(o, mode);
      });
      reapplySelection();      // A3 — re-overlay the selection on the new appearance
      maybeApplyGlass();       // STOPGAP — heuristic glass transparency (after materials settle)
      broadcastAppearance();
      saveVizState();          // B3 — persist appearance inputs per model (guarded)
    }
    // applyAppearance — canonical name; applyVizModes kept for existing callers.
    const applyAppearance = applyVizModes;

    // ── Glass transparency STOPGAP (HEURISTIC) ─────────────────────────────────
    // The exporter currently writes opaque materials (no alpha), so glazing reads
    // solid. Until Phase 2 (generic_transparency → alphaMode=BLEND) ships, optionally
    // make glass-ish CATEGORIES semi-transparent. This is a labelled heuristic, NOT
    // real model data. Decoupled from the appearance engine: it clones the material
    // the engine just assigned (per glass mesh, reversible), so it never fights the
    // layered model / colour-by / ghost / selection.
    const GLASS_KEYS = ['window', 'curtain panel', 'curtain wall', 'glazing', 'storefront', 'glass'];
    function isGlassMesh(o) {
      try {
        const cat = String((metaForMesh(o) || {}).category || '').toLowerCase();
        if (!cat || cat.includes('mullion')) return false;   // mullions are frames, not glass
        return GLASS_KEYS.some(k => cat.includes(k));
      } catch (_) { return false; }
    }
    function maybeApplyGlass() {
      const on = !!state.glassMode || state.renderMode === 'realistic';
      const grp = vizGroup(); if (!grp) return;
      grp.traverse(o => {
        if (!o.isMesh) return;
        const isClone = o.material && o.material.userData && o.material.userData._glassClone;
        if (on && isGlassMesh(o) && o.userData._vizMode !== 'hide') {
          const base = isClone ? o.userData._glassSrc : o.material;
          if (!base) return;
          if (isClone) { try { o.material.dispose(); } catch (_) {} }   // replace stale clone
          const c = base.clone();
          c.transparent = true; c.opacity = 0.3; c.depthWrite = false;
          c.userData = Object.assign({}, c.userData, { _glassClone: true });
          o.userData._glassSrc = base;
          o.material = c;
        } else if (o.userData && o.userData._glassSrc) {
          if (isClone) { try { o.material.dispose(); } catch (_) {} }   // restore engine material
          o.material = o.userData._glassSrc;
          o.userData._glassSrc = null;
        }
      });
    }
    function toggleGlass() {
      state.glassMode = !state.glassMode;
      applyAppearance();   // re-runs materials → maybeApplyGlass applies/restores
      toast(state.glassMode ? 'Glass: heuristic ON (not real data — export from Revit for true alpha)' : 'Glass: off');
    }

    // Set a single mesh to exactly one appearance state. Dirty-flagged + idempotent
    // (skips redundant material swaps). hide → invisible; ghost → shared ghost mat;
    // colour:<hex> → shared coloured mat; show → original.
    function applyMeshState(o, mode) {
      if (o.userData._vizMode === mode) return;     // idempotent
      o.userData._vizMode = mode;
      o.userData._paintKey = undefined;             // appearance changed → selection re-clones
      if (mode === 'hide') { o.visible = false; restoreOriginalMaterial(o); return; }
      o.visible = true;
      if (mode === 'ghost') { ghostMaterial(o); return; }
      if (mode.indexOf('colour:') === 0) { setReplacement(o, colourMaterial(mode.slice(7))); return; }
      if (mode.indexOf('trans:') === 0)  { setReplacement(o, transMaterial(parseInt(mode.slice(6), 10))); return; }   // C1
      if (mode.indexOf('rmode:') === 0)  { setReplacement(o, renderModeMaterial(mode.slice(6))); return; }
      restoreOriginalMaterial(o);                   // show → true original
    }
    // Shared coloured material per hex (cached — no per-mesh leak).
    function colourMaterial(hex) {
      let m = state.colourMats.get(hex);
      if (!m) {
        const n = parseInt(String(hex).replace('#', ''), 16);
        m = new THREE_.MeshStandardMaterial({ color: isFinite(n) ? n : 0x888888, metalness: 0.0, roughness: 0.85 });
        m.userData = { stingColour: true };
        state.colourMats.set(hex, m);
      }
      return m;
    }
    // C1 — shared transparent material per opacity% (cached). A continuous version of
    // ghost: the group's elements render at the slider's opacity over a neutral tint.
    function transMaterial(pct) {
      pct = Math.max(0, Math.min(100, Math.round(pct)));
      let m = state.transMats.get(pct);
      if (!m) {
        // V1 — endpoint render-order: when nearly opaque, write depth (and drop the
        // transparent flag at the very top) so the high end renders cleanly as a solid
        // instead of a transparency-sorted ghost that can flip/vanish behind geometry.
        // Opacity value mapping itself is unchanged (linear pct/100).
        m = new THREE_.MeshStandardMaterial({ color: 0x9aa3b2,
          transparent: pct < 99,
          opacity: Math.max(0.02, pct / 100),
          depthWrite: pct >= 90,
          side: THREE_.DoubleSide });
        m.userData = { stingColour: true };   // shared — never disposed per-mesh
        state.transMats.set(pct, m);
      }
      return m;
    }
    // The raw value a mesh contributes to the active colour scheme: a number for a
    // numeric/gradient scheme, a discipline for a preset, else the (normalised) token.
    // Part B — standard MEP system colours (BS 1710 / CIBSE / ASME), keyed by STING SYS code.
    const SYS_PALETTE = {
      DCW: '#1565c0', CWS: '#1565c0', DHW: '#e53935', HWS: '#e53935', DHWR: '#ec407a',
      SAN: '#2e7d32', SOIL: '#2e7d32', WASTE: '#558b2f', FW: '#558b2f',
      VEN: '#9ccc65', VENT: '#9ccc65', RWD: '#00897b', SW: '#00897b', STORM: '#00897b',
      GAS: '#fbc02d', FP: '#d32f2f', FLS: '#d32f2f',
      SUP: '#1565c0', SA: '#1565c0', RET: '#fb8c00', RA: '#fb8c00', EA: '#8d6e63',
      HVAC: '#1565c0', CHW: '#00bcd4', LHW: '#ff7043', HHW: '#ff7043', LTHW: '#ff7043',
      COM: '#7e57c2', ICT: '#7e57c2', LV: '#7e57c2', NCL: '#26a69a',
      _other: '#5b6472',
    };
    // raw classification (sysClass) → SYS code, for richer matching when the token is absent.
    const SYS_CLASS_MAP = [
      [/cold\s*water|domestic\s*cold|\bdcw\b/i, 'DCW'], [/hot\s*water\s*return|recirc/i, 'DHWR'],
      [/hot\s*water|domestic\s*hot|\bdhw\b/i, 'DHW'], [/sanitary|soil/i, 'SAN'], [/waste/i, 'WASTE'],
      [/vent/i, 'VEN'], [/storm|rain/i, 'RWD'], [/\bgas\b|natural\s*gas/i, 'GAS'],
      [/fire\s*protect|sprinkler|\bfire\b/i, 'FP'], [/supply\s*air/i, 'SUP'], [/return\s*air/i, 'RET'],
      [/exhaust/i, 'EA'], [/chilled/i, 'CHW'], [/hydronic|heating\s*water|\bhhw\b|\blhw\b/i, 'LHW'],
    ];
    // The SYS key for a mesh's meta: prefer the SYS token, else derive from raw classification.
    function sysKeyOf(meta) {
      let s = String(tokenValue(meta, 'SYS') || '').trim().toUpperCase();
      if (s) return s;
      const cls = String((meta && meta.sysClass) || '').trim();
      if (cls) { for (const pair of SYS_CLASS_MAP) if (pair[0].test(cls)) return pair[1]; }
      return '';
    }
    function colourValueOf(col, meta, guid) {
      // C4 — clash/issue status schemes resolve by element GUID via a precomputed map.
      if (col.byGuid) return (guid && col.byGuid.get(guid)) || col.def;
      // Part B — System palette keys on the SYS code (token or derived from sysClass).
      if (col.kind === 'sysPalette') return sysKeyOf(meta);
      if (col.kind === 'preset') return discOf(meta);
      // P3b — discipline + category variants: value keys on the (disc|cat) pair.
      if (col.kind === 'discVariants') return (discOf(meta) || '_other') + '|' + (catKey(meta) || '');
      if (col.numeric) { const raw = tokenValue(meta, col.token); const n = parseFloat(raw); return isFinite(n) ? n : null; }
      let v = String(tokenValue(meta, col.token) || '').trim();
      if (col.token === 'DISC') v = discOf(meta);
      return v;
    }
    // Stable legend KEY for a value (numeric → '<No Value>' or the raw number string;
    // categorical → the value or '<No Value>').
    function colourKey(col, v) {
      if (col.numeric) return (v == null) ? NOVAL : String(v);
      return (v === '' || v == null) ? NOVAL : v;
    }
    // Value → hex for the active scheme. Numeric uses the min→max ramp; categorical
    // uses the per-value palette; missing values get the distinct <No Value> colour.
    function colourForValue(col, v) {
      // B1 — a user-assigned custom colour for this value/discipline overrides the
      // palette (checked first, for both categorical + preset schemes).
      if (state.vizCustomColours && (v || v === 0) && state.vizCustomColours.has(v)) return state.vizCustomColours.get(v);
      if (col.kind === 'preset') return col.map[v] || col.map._other || null;
      // Part B — System palette: custom-per-system wins (checked above), else the precomputed
      // standard/variant colour for that SYS key.
      if (col.kind === 'sysPalette') return col.valueColors.get(v) || col.noValue || null;
      // P3b — variant value is "disc|cat"; a custom CATEGORY colour wins, else the
      // precomputed variant shade for that pair.
      if (col.kind === 'discVariants') {
        const cat = String(v).split('|')[1] || '';
        if (cat && state.vizCustomColours.has(cat)) return state.vizCustomColours.get(cat);
        return col.valueColors.get(v) || col.noValue || null;
      }
      if (col.numeric) return (v == null) ? col.noValue : rampColour(col, v);
      if (v === '' || v == null) return col.noValue || null;
      return col.valueColors ? (col.valueColors.get(v) || col.noValue || null) : null;
    }
    // Derive a discipline even when the DISC token is absent (as-built models):
    // map the Revit category to a discipline code (DiscMap-style).
    function discOf(meta) {
      // BUG 1(a) — the REAL DISC token wins when present (exporter-authored truth)…
      const d = discKey(meta);
      // …EXCEPT A2 SAFETY-NET: the old exporter stamped some categories with the WRONG
      // DISC (Lighting Fixtures → P, Toposolid → S, etc.). For those KNOWN-misclassified
      // categories the category-derived discipline OVERRIDES a stale stamp, so existing
      // exports read right WITHOUT a re-publish. Every other category trusts the stamp.
      const c0 = catKey(meta).toLowerCase();
      if (c0) { const ov = categoryOverrideDisc(c0); if (ov && ov !== d) return ov; }
      if (d) return d;
      // Derived fallback for metadata-poor / as-built models. ORDER MATTERS (first match
      // wins): Electrical BEFORE Plumbing so "Lighting Fixtures" / "Electrical Fixtures"
      // never fall under the old bare-"fixture" plumbing rule; Fire-protection before
      // Plumbing (sprinklers ≠ plumbing); Plumbing made SPECIFIC (never bare "fixture").
      const c = catKey(meta).toLowerCase();
      if (!c) return '';
      const RULES = [
        // Mechanical / HVAC
        [/duct|air\s*terminal|diffuser|grille|hvac|\bvav\b|\bahu\b|\bfcu\b|mechanical|\bfan\b|damper|air\s*handl|chiller|\bboiler\b|cooling\s*tower/, 'M'],
        // Electrical (incl. lighting + comms/data + fire-alarm) — BEFORE plumbing.
        [/electric|lighting|luminaire|light\s*fixture|\bconduit|cable\s*tray|\bcable\b|\bwire\b|\bdata\b|fire\s*alarm|communicat|security\s*device|nurse\s*call|telephon|\bswitch\b|socket|receptacle|panelboard|distribution\s*board|busway|bus\s*duct/, 'E'],
        // Fire protection — BEFORE plumbing (sprinklers / standpipes / hydrants).
        [/sprinkler|fire\s*protect|fire\s*supp|fire\s*pump|standpipe|hydrant/, 'FP'],
        // Plumbing / public health — SPECIFIC; never bare "fixture".
        [/plumb|sanitary|water\s*closet|\bwc\b|lavatory|urinal|\bbasin\b|\bsink\b|cistern|\bsoil\b|\bwaste\b|drainage|\bpipe|\bvalve\b|\btap\b|cold\s*water|hot\s*water|rainwater|\bgully\b/, 'P'],
        // Structural
        [/column|\bbeam\b|brace|footing|foundation|framing|structural|rebar|truss|slab\s*edge|\bpile\b/, 'S'],
        // Architectural (building-element catch-all)
        [/wall|floor|ceiling|roof|door|window|stair|railing|handrail|furniture|casework|\broom\b|curtain|generic\s*model|topograph|planting|\bsite\b|\bmass\b|parking|\bramp\b/, 'A'],
      ];
      for (const [re, disc] of RULES) if (re.test(c)) return disc;
      return '';
    }
    // A2 — strong category→discipline ONLY for the categories the exporter historically
    // mis-stamped: lighting/electrical devices → E, toposolid/site → A. Returns '' for
    // anything else (so the stamped DISC is trusted). These overrides are always correct
    // (a Lighting Fixture is electrical no matter what a bad stamp said).
    function categoryOverrideDisc(c) {
      if (/lighting|luminaire|light\s*fixture|electric|\bconduit\b|cable\s*tray|\bcable\b|\bdata\b|fire\s*alarm|\bswitch\b|socket|receptacle|panelboard|distribution\s*board|busway|bus\s*duct/.test(c)) return 'E';
      if (/toposolid|topograph|\bsite\b|\bpad\b|grading|sub-?region/.test(c)) return 'A';
      return '';
    }
    // Distinct disciplines across the model (derived where the token is absent).
    function distinctDisc() {
      const s = new Set();
      if (state.elementMap) Object.values(state.elementMap).forEach(m => { const d = discOf(m); if (d) s.add(d); });
      return Array.from(s).sort();
    }
    // Broadcast the live appearance state to a meeting (echo-guarded), reusing the
    // overlay channel established in WS2.
    // C5 — mirror the FULL visualize state to meeting followers via the WS2 overlay
    // channel (echo-guarded). The whole appearance (modes + scheme + custom colours +
    // transparency + render mode) travels as one snapshot; followers re-derive colours
    // deterministically. restoringViz/applyingRemoteViz prevent self-echo + reload churn.
    // Item 3 — COALESCED near-real-time broadcast: leading-edge immediate, then at most
    // one send per 100 ms carrying the LATEST snapshot (intermediate states dropped, the
    // final always sent). Effectively live during a slider drag without flooding SignalR.
    // Echo-guarded by applyingRemoteViz/restoringViz so a received snapshot can't loop.
    let _bcastTimer = null, _bcastAt = 0;
    const BCAST_THROTTLE_MS = 100;
    function _doBroadcastAppearance() {
      _bcastTimer = null; _bcastAt = Date.now();
      if (state.applyingRemoteViz || restoringViz) return;
      const m = (typeof window !== 'undefined') && window.STING_MEETING;
      if (m && typeof m.broadcastOverlay === 'function') {
        try { m.broadcastOverlay({ source: 'appearance', viz: serializeViz() }); } catch (_) {}
      }
    }
    function broadcastAppearance() {
      if (state.applyingRemoteViz || restoringViz || state.federationLoading) return;   // V4 — no broadcast while streaming models
      const since = Date.now() - _bcastAt;
      if (since >= BCAST_THROTTLE_MS) _doBroadcastAppearance();                 // leading — live
      else if (!_bcastTimer) _bcastTimer = setTimeout(_doBroadcastAppearance, BCAST_THROTTLE_MS - since);  // trailing — latest
    }
    // One-click "shade only X, ghost the rest" — BUG 2: idempotent TOGGLE (clicking the
    // already-isolated row clears back to show-all).
    function shadeOnlyDiscipline(disc) {
      const all = distinctDisc();
      const alreadyOnly = state.vizDiscMode.get(disc) === 'show' && all.filter(d => d !== disc).every(d => state.vizDiscMode.get(d) === 'ghost');
      state.vizDiscMode.clear();
      if (!alreadyOnly) all.forEach(d => state.vizDiscMode.set(d, d === disc ? 'show' : 'ghost'));
      applyAppearance();
      renderVisualizePanel();
      toast(alreadyOnly ? 'Show all' : `Shading ${disc}, ghosting the rest`);
    }
    function shadeOnlyCategory(cat) {
      const all = distinctTokens('CAT');
      const alreadyOnly = state.vizCatMode.get(cat) === 'show' && all.filter(c => c !== cat).every(c => state.vizCatMode.get(c) === 'ghost');
      state.vizCatMode.clear();
      if (!alreadyOnly) all.forEach(c => state.vizCatMode.set(c, c === cat ? 'show' : 'ghost'));
      applyAppearance();
      renderVisualizePanel();
      toast(alreadyOnly ? 'Show all' : `Shading ${cat}, ghosting the rest`);
    }
    // P2 — multi-select isolate: shade every TICKED discipline/category, ghost the rest
    // (empty selection → show all). Drives the same per-disc/-cat mode maps as the single
    // quick-isolate, so it composes with the layered model identically.
    function shadeOnlySet(kind) {
      const isDisc = kind === 'disc';
      const all = isDisc ? distinctDisc() : distinctTokens('CAT');
      const sel = isDisc ? state.vizDiscSel : state.vizCatSel;
      const modeMap = isDisc ? state.vizDiscMode : state.vizCatMode;
      modeMap.clear();
      if (sel.size) all.forEach(v => modeMap.set(v, sel.has(v) ? 'show' : 'ghost'));
      applyAppearance();
      renderVisualizePanel();
      toast(sel.size ? `Shading ${Array.from(sel).join(', ')} — ghosting the rest` : 'Show all (none ticked)');
    }
    // V3 — one-click discipline combo: set the multi-isolate selection to the given codes
    // (intersected with what's present) and shade-only-those. Empty/All → show everything.
    function comboPreset(codes) {
      state.vizDiscSel = new Set(codes || []);
      shadeOnlySet('disc');
    }

    // Colour every element by ANY STING token (categorical) — sets a colour
    // descriptor and drives the ONE appearance engine (no separate overlay path).
    function activePalette() { return PALETTE_SETS[state.vizPalette] || VIZ_PALETTE; }
    // A5 — assign palette colours DETERMINISTICALLY from the SORTED distinct values,
    // so a given value always maps to the same hex across re-renders / selections /
    // reloads. Cached on state.vizColour.valueColors; never reassigned on interaction.
    function buildValueColors(sortedValues, pal) {
      const m = new Map();
      sortedValues.forEach((v, i) => m.set(v, pal[i % pal.length]));
      return m;
    }
    function colourByToken(token) {
      if (!V.modelRoot || !state.elementMap) return toast('No model / element map', 'warn');
      // BUG 2 — idempotent TOGGLE: clicking the active scheme again clears it (→ base).
      if (!restoringViz && state.vizColour && state.vizColour.kind === 'token' && state.vizColour.token === token) {
        clearColour(); renderVisualizePanel(); toast('Colour cleared'); return;
      }
      const distinct = (token === 'DISC') ? distinctDisc() : distinctTokens(token);   // SORTED
      if (!distinct.length) return toast(`No ${token} values on this model`, 'warn');
      const values = buildValueColors(distinct, activePalette());
      const counts = new Map();
      Object.values(state.elementMap).forEach(m => {
        let v = String(tokenValue(m, token) || '').trim();
        if (token === 'DISC') v = discOf(m);
        if (v) counts.set(v, (counts.get(v) || 0) + 1);
      });
      state.vizColour = {
        kind: 'token', token, valueColors: values, counts, noValue: '#3a3f4a',
        isolate: null, hidden: new Set(),
        legend: distinct.map(v => ({ label: v, color: values.get(v), count: counts.get(v) || 0 })),
      };
      state.vizPreset = null;
      applyAppearance();
      renderVisualizePanel();
      if (!restoringViz) toast(`Coloured by ${token} — ${values.size} value${values.size === 1 ? '' : 's'}`);
    }

    // M4 — colour by ANY meta parameter. ≥80% numeric values ⇒ a min→max gradient
    // (with a value-range + unit legend); otherwise a categorical palette. Missing
    // values get the distinct <No Value> colour for QA.
    function colourByParam(key) {
      if (!V.modelRoot || !state.elementMap) return toast('No model / element map', 'warn');
      if (!restoringViz && state.vizColour && state.vizColour.kind === 'param' && state.vizColour.token === key) {
        clearColour(); renderVisualizePanel(); toast('Colour cleared'); return;   // BUG 2 toggle
      }
      const raws = [];
      Object.values(state.elementMap).forEach(m => { const r = m[key]; if (r != null && typeof r !== 'object') raws.push(r); });
      if (!raws.length) return toast(`No "${key}" values`, 'warn');
      const nums = raws.map(x => parseFloat(x)).filter(n => isFinite(n));
      const numeric = nums.length >= raws.length * 0.8;
      if (numeric) {
        // C4 — reduce, NOT Math.min(...nums): a 12k-element spread overflows the call stack.
        let min = Infinity, max = -Infinity;
        for (let i = 0; i < nums.length; i++) { const n = nums[i]; if (n < min) min = n; if (n > max) max = n; }
        const ramp = (state.vizPalette === 'STING') ? PALETTE_SETS.Viridis : activePalette();
        const unit = paramUnit(key);
        state.vizColour = {
          kind: 'param', token: key, numeric: true, min, max, ramp, unit, noValue: '#3a3f4a',
          isolate: null, hidden: new Set(),
          legend: [0, 0.25, 0.5, 0.75, 1].map(t => ({
            label: fmtNum(min + (max - min) * t) + (unit ? ' ' + unit : ''),
            color: lerpRamp(ramp, t),
          })),
        };
      } else {
        const distinct = Array.from(new Set(raws.map(r => String(r).trim()).filter(Boolean))).sort();   // SORTED
        if (!distinct.length) return toast(`No "${key}" values`, 'warn');
        const values = buildValueColors(distinct, activePalette());   // A5 deterministic
        const counts = new Map();
        Object.values(state.elementMap).forEach(m => { const v = String(m[key] != null ? m[key] : '').trim(); if (v) counts.set(v, (counts.get(v) || 0) + 1); });
        state.vizColour = { kind: 'param', token: key, valueColors: values, counts, noValue: '#3a3f4a', isolate: null, hidden: new Set(),
          legend: distinct.map(v => ({ label: v, color: values.get(v), count: counts.get(v) || 0 })) };
      }
      state.vizPreset = null;
      applyAppearance();
      renderVisualizePanel();
      if (!restoringViz) toast(`Coloured by ${key}`);
    }
    // C4 — colour by CLASH status. Each clashing element gets its worst clash status;
    // the rest are "Clear" (muted). Resolves by guid via col.byGuid (colourValueOf).
    function colourByClashStatus() {
      if (!V.modelRoot) return toast('No model', 'warn');
      if (!restoringViz && state.vizColour && state.vizColour.kind === 'clash') { clearColour(); renderVisualizePanel(); toast('Colour cleared'); return; }
      const COLOURS = { NEW: '#e74c3c', OPEN: '#f39c12', RESOLVED: '#2ecc71', Clear: '#3a3f4a' };
      const RANK = { Clear: 0, RESOLVED: 1, OPEN: 2, NEW: 3 };
      const byGuid = new Map();
      (state.clashes || []).forEach(c => {
        const st = c.status || 'NEW';
        [c.elementA && c.elementA.guid, c.elementB && c.elementB.guid].forEach(g => {
          if (!g) return;
          const cur = byGuid.get(g);
          if (!cur || (RANK[st] || 0) > (RANK[cur] || 0)) byGuid.set(g, st);
        });
      });
      if (!byGuid.size) return toast('No clashes to colour by', 'warn');
      const counts = { Clear: 0 }; byGuid.forEach(s => counts[s] = (counts[s] || 0) + 1);
      const present = ['NEW', 'OPEN', 'RESOLVED', 'Clear'].filter(s => s === 'Clear' || counts[s]);
      state.vizColour = {
        kind: 'clash', byGuid, def: 'Clear', valueColors: new Map(present.map(s => [s, COLOURS[s]])),
        counts, noValue: COLOURS.Clear, isolate: null, hidden: new Set(),
        legend: present.map(s => ({ label: s, color: COLOURS[s], count: counts[s] || 0 })),
      };
      state.vizPreset = null; applyAppearance(); renderVisualizePanel();
      if (!restoringViz) toast(`Coloured by clash status — ${byGuid.size} clashing element${byGuid.size === 1 ? '' : 's'}`);
    }
    // C4 — colour by ISSUE status (Open / Resolved / No issue), via elementGuids[].
    function colourByIssueStatus() {
      if (!V.modelRoot) return toast('No model', 'warn');
      if (!restoringViz && state.vizColour && state.vizColour.kind === 'issue') { clearColour(); renderVisualizePanel(); toast('Colour cleared'); return; }
      const COLOURS = { Open: '#f39c12', Resolved: '#2ecc71', 'No issue': '#3a3f4a' };
      const byGuid = new Map();
      (state.issues || []).forEach(i => {
        const st = (i.status === 'RESOLVED') ? 'Resolved' : 'Open';
        (Array.isArray(i.elementGuids) ? i.elementGuids : []).forEach(g => {
          if (!g) return;
          if (byGuid.get(g) !== 'Open') byGuid.set(g, st);   // Open wins over Resolved
        });
      });
      if (!byGuid.size) return toast('No issues linked to elements', 'warn');
      const counts = { 'No issue': 0 }; byGuid.forEach(s => counts[s] = (counts[s] || 0) + 1);
      const present = ['Open', 'Resolved', 'No issue'].filter(s => s === 'No issue' || counts[s]);
      state.vizColour = {
        kind: 'issue', byGuid, def: 'No issue', valueColors: new Map(present.map(s => [s, COLOURS[s]])),
        counts, noValue: COLOURS['No issue'], isolate: null, hidden: new Set(),
        legend: present.map(s => ({ label: s, color: COLOURS[s], count: counts[s] || 0 })),
      };
      state.vizPreset = null; applyAppearance(); renderVisualizePanel();
      if (!restoringViz) toast(`Coloured by issue status — ${byGuid.size} flagged element${byGuid.size === 1 ? '' : 's'}`);
    }
    // C6 — search/filter → act. Find element guids whose chosen field (or ANY scalar
    // value when field='*') contains the query, then isolate / hide / colour / select
    // them — all through the layered model.
    const SEARCH_TOKENS = ['DISC', 'SYS', 'LVL', 'FUNC', 'PROD', 'CAT'];
    function searchElementGuids(query, field) {
      const q = String(query || '').trim().toLowerCase();
      const out = new Set();
      if (!q || !state.elementMap) return out;
      Object.entries(state.elementMap).forEach(([guid, m]) => {
        if (!m || typeof m !== 'object') return;
        let hit = false;
        if (field && field !== '*') {
          // BUG 3 — use the SAME normalisation the resolver + colour-by use, so search
          // matches the displayed values: DISC → discOf (derived), CAT → catKey.
          let v;
          if (field === 'DISC') v = discOf(m);
          else if (field === 'CAT') v = catKey(m);
          else v = SEARCH_TOKENS.includes(field) ? tokenValue(m, field) : m[field];
          hit = String(v == null ? '' : v).toLowerCase().includes(q);
        } else {
          for (const k in m) { const v = m[k]; if (v != null && typeof v !== 'object' && String(v).toLowerCase().includes(q)) { hit = true; break; } }
        }
        if (hit) out.add(guid);
      });
      return out;
    }
    function colourBySearch(matched) {
      const C = { Match: '#f39c12', Other: '#3a3f4a' };
      state.vizColour = {
        kind: 'search', byGuid: new Map(Array.from(matched).map(g => [g, 'Match'])), def: 'Other',
        valueColors: new Map([['Match', C.Match], ['Other', C.Other]]), counts: { Match: matched.size, Other: 0 },
        noValue: C.Other, isolate: null, hidden: new Set(),
        legend: [{ label: 'Match', color: C.Match, count: matched.size }, { label: 'Other', color: C.Other, count: 0 }],
      };
      state.vizPreset = null; applyAppearance(); renderVisualizePanel();
    }
    function searchAct(action) {
      const matched = searchElementGuids(state.vizSearchQuery, state.vizSearchField);
      if (!matched.size) return toast('No matches', 'warn');
      if (action === 'isolate')      { state.vizIsolation = { mode: 'isolate', guids: matched }; applyAppearance(); }
      else if (action === 'hide')    { state.vizIsolation = { mode: 'hideSel', guids: matched }; applyAppearance(); }
      else if (action === 'colour')  { colourBySearch(matched); }
      else if (action === 'select')  {
        state.selectedElementGuids = new Set(matched);
        state.selectedElementGuid = matched.values().next().value;
        reapplySelection(); renderProperties(state.selectedElementGuid);
        renderSelectionToolbar(); updateRightTabCounts();
      }
      toast(`${matched.size} match${matched.size === 1 ? '' : 'es'} — ${action}`);
    }
    function fmtNum(n) { return Math.abs(n) >= 1000 ? Math.round(n).toLocaleString() : (Math.round(n * 100) / 100).toString(); }
    function paramUnit(key) {
      const k = String(key).toLowerCase();
      if (/_mm$|width|height|depth|length|diameter|thickness/.test(k)) return 'mm';
      if (/area/.test(k)) return 'm²';
      if (/volume/.test(k)) return 'm³';
      if (/cost|price|rate/.test(k)) return (state.currency || '');
      if (/flow|lps/.test(k)) return 'L/s';
      if (/pressure|pa$/.test(k)) return 'Pa';
      if (/power|wattage|kw/.test(k)) return 'kW';
      if (/voltage/.test(k)) return 'V';
      return '';
    }

    // Discipline appearance preset (solid colour per discipline) — same engine.
    // P3b — derive a distinguishable shade of a discipline base colour for the i-th of n
    // categories: spread lightness across roughly ±28% (mix toward white/black) so the
    // categories read apart while still clearly belonging to that discipline.
    function shadeVariant(baseHex, i, n) {
      if (n <= 1) return baseHex;
      const f = (i / (n - 1) - 0.5) * 0.56;   // -0.28 … +0.28
      return f >= 0 ? lerpHex(baseHex, '#ffffff', f) : lerpHex(baseHex, '#1a1a1a', -f);
    }
    // P3b — "Discipline + category variants": each discipline keeps its base colour;
    // categories within it get auto variant shades (legend grouped by discipline). Custom
    // category colours still win (handled in colourForValue). Idempotent toggle.
    function applyDisciplineVariants() {
      if (!V.modelRoot) return;
      if (!restoringViz && state.vizPreset === '__variants') { clearColour(); renderVisualizePanel(); toast('Variants cleared'); return; }
      const base = DISC_PRESETS['Discipline'];
      const catsByDisc = {};
      Object.values(state.elementMap || {}).forEach(m => {
        const d = discOf(m) || '_other', c = catKey(m) || '';
        (catsByDisc[d] = catsByDisc[d] || new Set()).add(c);
      });
      const valueColors = new Map(), legend = [];
      Object.keys(catsByDisc).sort().forEach(d => {
        const bcol = base[d] || base._other || '#5b6472';
        const cats = Array.from(catsByDisc[d]).sort();
        cats.forEach((c, i) => {
          const hex = shadeVariant(bcol, i, cats.length);
          valueColors.set(d + '|' + c, hex);
          legend.push({ label: d + ' · ' + (c || '—'), color: hex });
        });
      });
      state.vizPreset = '__variants';
      state.vizColour = { kind: 'discVariants', valueColors, isolate: null, hidden: new Set(), noValue: '#3a3f4a', legend };
      applyAppearance();
      if (!restoringViz) toast('Discipline + category variants');
    }
    // Part B — System palette: standard per-classification colours for known SYS codes,
    // variant shades for unknown, custom-per-system still wins; legend shows system + count.
    // "No SYS values" toast ONLY when genuinely empty (after a re-publish carries the data).
    function applySystemPalette() {
      if (!V.modelRoot) return;
      if (!restoringViz && state.vizPreset === '__syspalette') { clearColour(); renderVisualizePanel(); toast('System palette cleared'); return; }
      const counts = {};
      Object.values(state.elementMap || {}).forEach(m => { const k = sysKeyOf(m); if (k) counts[k] = (counts[k] || 0) + 1; });
      const keys = Object.keys(counts).sort();
      if (!keys.length) { toast('No SYS values — re-publish from Revit (MEP systems) to populate', 'warn'); return; }
      const valueColors = new Map(), legend = [];
      keys.forEach((k, i) => {
        const hex = SYS_PALETTE[k] || shadeVariant(SYS_PALETTE._other, i, keys.length);
        valueColors.set(k, hex);
        legend.push({ label: k + ' (' + counts[k] + ')', color: hex });
      });
      state.vizPreset = '__syspalette';
      state.vizColour = { kind: 'sysPalette', valueColors, isolate: null, hidden: new Set(), noValue: '#3a3f4a', legend };
      applyAppearance();
      if (!restoringViz) toast('System palette');
    }
    function applyDisciplinePreset(name) {
      const preset = DISC_PRESETS[name];
      if (!preset || !V.modelRoot) return;
      if (!restoringViz && state.vizPreset === name) { clearColour(); renderVisualizePanel(); toast('Preset cleared'); return; }   // BUG 2 toggle
      const used = new Set();
      Object.values(state.elementMap || {}).forEach(m => { const d = discOf(m); if (preset[d]) used.add(d); });
      state.vizPreset = name;
      state.vizColour = {
        kind: 'preset', map: preset, presetName: name, isolate: null, hidden: new Set(),
        legend: Array.from(used).sort().map(d => ({ label: d, color: preset[d] }))
          .concat(preset._other ? [{ label: 'Other', color: preset._other }] : []),
      };
      applyAppearance();
      if (!restoringViz) toast(`Applied ${name}`);
    }
    function clearColour() {
      state.vizColour = null; state.vizPreset = null;
      applyAppearance();
    }

    // BUILD 4 (section) — a clip-plane section box around an arbitrary AABB.
    // Snapshots the renderer's current clippingPlanes so it never clobbers the
    // section tool; clearClashSection restores them.
    function unionBoxForGuids(guids) {
      const box = new THREE_.Box3(); let any = false;
      guids.filter(Boolean).forEach(g => {
        const m = findMeshByGuid(g);
        if (m) { box.union(new THREE_.Box3().setFromObject(m)); any = true; }
      });
      return any ? box : null;
    }
    function applyClashSection(box) {
      if (!box || !V.renderer) return;
      const pad = (box.getSize(new THREE_.Vector3()).length() * 0.08) || 0.5;
      const min = box.min.clone().addScalar(-pad), max = box.max.clone().addScalar(pad);
      const planes = [
        new THREE_.Plane(new THREE_.Vector3( 1, 0, 0), -min.x),
        new THREE_.Plane(new THREE_.Vector3(-1, 0, 0),  max.x),
        new THREE_.Plane(new THREE_.Vector3( 0, 1, 0), -min.y),
        new THREE_.Plane(new THREE_.Vector3( 0,-1, 0),  max.y),
        new THREE_.Plane(new THREE_.Vector3( 0, 0, 1), -min.z),
        new THREE_.Plane(new THREE_.Vector3( 0, 0,-1),  max.z),
      ];
      if (state.clashSection.saved === null) state.clashSection.saved = V.renderer.clippingPlanes;
      V.renderer.clippingPlanes = planes;
      state.clashSection.active = true;
    }
    function clearClashSection() {
      if (!V.renderer) return;
      if (state.clashSection.saved !== null) {
        V.renderer.clippingPlanes = state.clashSection.saved;
        state.clashSection.saved = null;
      } else {
        V.renderer.clippingPlanes = [];
      }
      state.clashSection.active = false;
    }

    // Deterministic reset to the TRUE original — clears every appearance input
    // (disc/cat modes, colour scheme, custom colours, keep-solid, render mode) AND
    // the selection, then restores each mesh to its load-time original material.
    function resetVisualization() {
      state.vizDiscMode.clear(); state.vizCatMode.clear();
      state.vizKeepSolidDisc.clear(); state.vizKeepSolidCat.clear();
      state.vizPreset = null; state.vizColour = null;
      state.vizCustomColours.clear();
      state.vizTransp.clear();
      state.vizIsolation = null;
      state.renderMode = 'shaded';
      try { if (V.setRealistic) V.setRealistic(false); } catch (_) {}   // Clear returns to base look
      state.selectedElementGuid = null; state.selectedElementGuids.clear();
      clearClashSection();
      if (V.clearOverlay) V.clearOverlay();
      clearAllHighlights();            // selection + appearance → true original, flags reset
      showAllElements();
      try { localStorage.removeItem(vizStateKey()); } catch (_) {}   // B3 — wipe persisted state
      renderVisualizePanel();
      toast('Visualization reset');
    }

    // ── B3 — per-MODEL visualize persistence ──────────────────────────────────
    // Saves the appearance INPUTS (not per-mesh state) keyed by model id; colours are
    // re-derived deterministically on restore, so nothing drifts. Selection is NOT
    // persisted (it's transient). restoringViz suppresses re-save churn during load.
    let restoringViz = false;
    // FEDERATION — viz now spans EVERY loaded model in the project, so persist the
    // appearance per PROJECT (not per model id) — otherwise the federation state would
    // fragment across the individual models. (Supersédes the D-fix per-model key, which
    // was correct only while the viewer was single-model.)
    function vizStateKey() { return 'planscape_viz_proj_' + (projectId || modelId || 'default'); }
    // C5 — one serializer for the appearance inputs, reused by per-model persistence (B3)
    // AND named visualize presets in Saved Views. Colours are stored as a descriptor and
    // re-derived deterministically (A5), so a snapshot reproduces identical colours.
    function serializeViz() {
      const c = state.vizColour;
      return {
        disc: Array.from(state.vizDiscMode.entries()),
        cat:  Array.from(state.vizCatMode.entries()),
        custom: Array.from(state.vizCustomColours.entries()),
        transp: Array.from(state.vizTransp.entries()),
        keepDisc: Array.from(state.vizKeepSolidDisc),
        keepCat:  Array.from(state.vizKeepSolidCat),
        palette: state.vizPalette,
        renderMode: state.renderMode,
        colour: c ? { kind: c.kind, token: c.token, presetName: c.presetName, numeric: !!c.numeric } : null,
        // E3 — the section box (fraction-based, model-relative) rides along.
        section: (typeof window !== 'undefined' && window.STING_VIEWER_EXTRAS && window.STING_VIEWER_EXTRAS.getSectionBox)
          ? window.STING_VIEWER_EXTRAS.getSectionBox() : null,
      };
    }
    function applyVizSnapshot(d) {
      if (!d) return;
      restoringViz = true;
      try {
        state.vizDiscMode = new Map(d.disc || []);
        state.vizCatMode  = new Map(d.cat || []);
        state.vizCustomColours = new Map(d.custom || []);
        state.vizTransp   = new Map(d.transp || []);
        state.vizKeepSolidDisc = new Set(d.keepDisc || []);
        state.vizKeepSolidCat  = new Set(d.keepCat || []);
        state.vizIsolation = null;
        if (d.palette) state.vizPalette = d.palette;
        state.renderMode = d.renderMode || 'shaded';
        state.vizColour = null;
        if (d.colour) {
          if (d.colour.kind === 'preset' && d.colour.presetName) applyDisciplinePreset(d.colour.presetName);
          else if (d.colour.kind === 'discVariants') applyDisciplineVariants();   // P3b — rebuild variants
          else if (d.colour.kind === 'sysPalette') applySystemPalette();          // Part B — rebuild system palette
          else if (d.colour.kind === 'clash') colourByClashStatus();
          else if (d.colour.kind === 'issue') colourByIssueStatus();
          else if (d.colour.numeric && d.colour.token) colourByParam(d.colour.token);
          else if (d.colour.kind === 'token' && d.colour.token) colourByToken(d.colour.token);
          else if (d.colour.kind === 'param' && d.colour.token) colourByParam(d.colour.token);
        }
        // E3 — restore the section box (or clear if the snapshot had none).
        const xx = (typeof window !== 'undefined') && window.STING_VIEWER_EXTRAS;
        if (xx && xx.applySectionState) xx.applySectionState(d.section || { active: false });
      } catch (_) {}
      restoringViz = false;
      applyAppearance();
      renderVisualizePanel();
    }
    function saveVizState() {
      if (state.applyingRemoteViz || restoringViz) return;
      try { localStorage.setItem(vizStateKey(), JSON.stringify(serializeViz())); } catch (_) {}
    }
    function loadVizState() {
      try { const raw = localStorage.getItem(vizStateKey()); if (raw) applyVizSnapshot(JSON.parse(raw)); } catch (_) {}
    }
    // V6 — SAVED VIEWS: named Show+Colour combos persisted per project (localStorage),
    // one-click recall. Reuses serializeViz()/applyVizSnapshot(); recall broadcasts to a meeting.
    function savedViewsKey() { return vizStateKey() + ':saved'; }
    function loadSavedViews() { try { return JSON.parse(localStorage.getItem(savedViewsKey()) || '[]') || []; } catch (_) { return []; } }
    function writeSavedViews(arr) { try { localStorage.setItem(savedViewsKey(), JSON.stringify(arr)); } catch (_) {} }
    function saveNamedView(name) {
      name = (name || '').trim(); if (!name) return;
      const arr = loadSavedViews().filter(v => v.name !== name);   // replace same-name
      arr.unshift({ name: name, snap: serializeViz(), at: Date.now() });
      writeSavedViews(arr.slice(0, 50));
      renderVisualizePanel();
      toast('Saved view: ' + name);
    }
    function recallNamedView(name) {
      const v = loadSavedViews().find(x => x.name === name); if (!v) return;
      applyVizSnapshot(v.snap);          // applies + renders (restoringViz guards self-echo)
      try { broadcastAppearance(); } catch (_) {}   // then mirror the recalled state to a meeting
      toast('Recalled: ' + name);
    }
    function deleteNamedView(name) { writeSavedViews(loadSavedViews().filter(v => v.name !== name)); renderVisualizePanel(); }

    // ── Visualize panel UI ───────────────────────────────────────────────────
    // B1 — a discipline/category row: optional colour picker (custom colour for that
    // group, overrides the palette) · label (click = isolate) · show / ghost / hide /
    // isolate buttons. customKey is the value (disc code / category) for the colour map.
    function vizModeRow(label, count, getMode, setMode, onIsolate, customKey, selSet, selKey) {
      const row = el('div', { class: 'viz-row',
        style: 'display:flex;align-items:center;gap:5px;padding:3px 0' });
      // P2 — multi-select tick for "Shade selected, ghost rest".
      if (selSet) {
        const ck = el('input', { type: 'checkbox', title: 'Tick for multi-isolate',
          style: 'width:14px;height:14px;flex:0 0 auto;cursor:pointer;accent-color:#3B82F6' });
        ck.checked = selSet.has(selKey);
        ck.addEventListener('change', () => { if (ck.checked) selSet.add(selKey); else selSet.delete(selKey); });
        row.appendChild(ck);
      }
      if (customKey != null) {
        const cp = el('input', { type: 'color', value: state.vizCustomColours.get(customKey) || '#888888',
          title: 'Custom colour for ' + label,
          style: 'width:18px;height:18px;padding:0;border:none;background:none;cursor:pointer;flex:0 0 auto' });
        cp.addEventListener('input', () => { state.vizCustomColours.set(customKey, cp.value); applyAppearance(); });
        row.appendChild(cp);
      }
      const lbl = el('span', {
        title: onIsolate ? 'Click to isolate (shade only, ghost rest)' : '',
        style: 'flex:1;font-size:12px;color:var(--text,#e6e6e6);white-space:nowrap;overflow:hidden;text-overflow:ellipsis'
          + (onIsolate ? ';cursor:pointer' : '')
      }, count != null ? `${label} (${count})` : label);
      if (onIsolate) lbl.addEventListener('click', () => onIsolate());
      row.appendChild(lbl);
      const group = el('div', { style: 'display:flex;gap:2px' });
      const mkBtn = (m, glyph, title, fn) => {
        const cur = getMode() || 'show';
        const active = (m === '_iso') ? false : (cur === m);
        const b = el('button', {
          class: 'viz-mode-btn', title: title || m,
          style: 'width:24px;height:22px;border-radius:4px;cursor:pointer;font-size:12px;'
            + 'border:1px solid ' + (active ? '#3B82F6' : 'rgba(255,255,255,0.15)') + ';'
            + 'background:' + (active ? 'rgba(59,130,246,0.25)' : 'rgba(255,255,255,0.04)') + ';'
            + 'color:#e6e6e6'
        }, glyph);
        b.addEventListener('click', fn);
        group.appendChild(b);
      };
      [['show', '◐', 'Show'], ['ghost', '○', 'Ghost'], ['hide', '∅', 'Hide']].forEach(([m, glyph, title]) =>
        mkBtn(m, glyph, title, () => { setMode(m); applyAppearance(); renderVisualizePanel(); }));
      if (onIsolate) mkBtn('_iso', '◎', 'Isolate (shade only, ghost the rest)', () => onIsolate());
      row.appendChild(group);
      // C1 — continuous transparency slider (100% = opaque/no override; < 100% renders
      // the group at that opacity, overriding the binary ghost).
      if (customKey != null) {
        const cur = state.vizTransp.has(customKey) ? Math.round(state.vizTransp.get(customKey) * 100) : 100;
        const sl = el('input', { type: 'range', min: '0', max: '100', step: '5', value: String(cur),
          title: 'Opacity ' + cur + '%', class: 'viz-transp',
          style: 'width:46px;flex:0 0 auto;cursor:pointer;accent-color:#3B82F6' });
        sl.addEventListener('input', () => {
          const v = parseInt(sl.value, 10);
          sl.title = 'Opacity ' + v + '%';
          if (v >= 100) state.vizTransp.delete(customKey); else state.vizTransp.set(customKey, v / 100);
          applyAppearance();
        });
        row.appendChild(sl);
      }
      return row;
    }

    // Colour legend (swatch · label · count) with M4 interactivity:
    //   click       → isolate this value (ghost the rest)   [toggle]
    //   shift-click → hide this value                        [toggle]
    //   hover       → highlight matching meshes
    // Numeric/gradient legends are read-only stops (no per-value isolation).
    function renderVizLegend(col) {
      const box = el('div', { class: 'viz-legend', style: 'margin-top:6px;display:flex;flex-direction:column;gap:2px;max-height:200px;overflow:auto' });
      const interactive = !col.numeric;
      col.legend.forEach(it => {
        const isIso = col.isolate === it.label;
        const isHid = col.hidden && col.hidden.has(it.label);
        const row = el('div', { class: 'viz-legend-row', 'data-value': it.label, style:
          'display:flex;align-items:center;gap:6px;font-size:11px;padding:2px 3px;border-radius:3px;' +
          (interactive ? 'cursor:pointer;' : '') +
          'color:' + (isHid ? '#6b7280' : '#cfd6e4') + ';' +
          (isIso ? 'background:rgba(59,130,246,0.25);' : '') +
          (isHid ? 'text-decoration:line-through;opacity:0.6;' : '') }, [
          el('span', { style: `width:12px;height:12px;border-radius:2px;background:${it.color};flex:0 0 auto` }),
          el('span', { style: 'flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis' }, String(it.label)),
          el('span', { style: 'color:#9aa3b2' }, it.count != null ? String(it.count) : ''),
        ]);
        if (interactive) {
          row.addEventListener('click', (e) => {
            if (e.shiftKey) {                              // shift-click → toggle hide
              if (col.hidden.has(it.label)) col.hidden.delete(it.label); else col.hidden.add(it.label);
              col.isolate = null;
            } else {                                       // click → toggle isolate
              col.isolate = (col.isolate === it.label) ? null : it.label;
              col.hidden.clear();
            }
            applyAppearance(); renderVisualizePanel();
          });
          row.addEventListener('mouseenter', () => highlightByColourValue(col, it.label));
          row.addEventListener('mouseleave', () => clearHoverHighlight());
        }
        box.appendChild(row);
      });
      return box;
    }
    // Transient hover highlight — emissive boost on the (shared) materials of meshes
    // matching a legend value. Track MATERIALS (not meshes) so a shared material is
    // touched once; restore to black (these engine materials default to no emissive).
    let _hoverMats = [];
    function clearHoverHighlight() {
      // C3 — restore the material's PRIOR emissive (captured below), not a forced
      // black: a true-original GLTF material may have its own emissive, and selected
      // meshes carry the orange selection emissive — neither must be stomped.
      _hoverMats.forEach(({ mat, hex }) => { if (mat && mat.emissive) mat.emissive.setHex(hex); });
      _hoverMats = [];
    }
    function highlightByColourValue(col, label) {
      clearHoverHighlight();
      if (!V.modelRoot) return;
      const seen = new Set();
      vizGroup().traverse(o => {
        if (!o.isMesh || !o.visible || !o.material || !o.material.emissive) return;
        const key = colourKey(col, colourValueOf(col, metaForMesh(o), o.userData.elementGuid));
        if (key === label && !seen.has(o.material.uuid)) {
          seen.add(o.material.uuid);
          _hoverMats.push({ mat: o.material, hex: o.material.emissive.getHex() });
          o.material.emissive.setHex(0x2244aa);
        }
      });
    }

    function renderVisualizePanel() {
      const pane = $('#pane-visualize');
      if (!pane) return;
      pane.innerHTML = '';
      const haveMap = state.elementMap && Object.keys(state.elementMap).length > 0;
      if (!haveMap) {
        pane.appendChild(el('div', { class: 'empty-state' },
          'Load a tagged model to use Visualize'));
        return;
      }
      const wrap = el('div', { style: 'padding:10px;display:flex;flex-direction:column;gap:14px;overflow:auto' });
      const sectionTitle = (t) => el('div', {
        style: 'font-size:11px;font-weight:600;letter-spacing:0.04em;text-transform:uppercase;color:var(--text-muted,#9aa3b2);margin-bottom:2px'
      }, t);

      // ── Quick actions ──
      const quick = el('div', {});
      quick.appendChild(sectionTitle('Quick'));
      const discs = distinctDisc();   // derived where the DISC token is absent
      const sel = el('select', { style: 'flex:1;background:#1a1d24;color:#e6e6e6;border:1px solid rgba(255,255,255,0.15);border-radius:4px;padding:4px' });
      sel.appendChild(el('option', { value: '' }, 'Pick discipline…'));
      discs.forEach(d => sel.appendChild(el('option', { value: d }, d)));
      const qRow = el('div', { style: 'display:flex;gap:6px;align-items:center' }, [
        sel,
        el('button', { class: 'btn sm', style: 'white-space:nowrap', onclick: () => sel.value && shadeOnlyDiscipline(sel.value) }, 'Shade only, ghost rest')
      ]);
      quick.appendChild(qRow);
      quick.appendChild(el('div', { style: 'display:flex;gap:6px;margin-top:6px' }, [
        el('button', { class: 'btn sm subtle', style: 'flex:1', onclick: () => { state.vizDiscMode.forEach((_, k) => state.vizDiscMode.set(k, 'show')); state.vizCatMode.forEach((_, k) => state.vizCatMode.set(k, 'show')); state.vizColour = null; if (V.modelRoot) vizGroup().traverse(o => { if (o.isMesh) o.userData._vizMode = undefined; }); applyAppearance(); renderVisualizePanel(); } }, 'Show all'),
        el('button', { class: 'btn sm subtle', style: 'flex:1', onclick: () => resetVisualization() }, 'Reset')
      ]));
      wrap.appendChild(quick);

      // V4 — two clearly-labelled axes (pure visual grouping; the engine is unchanged):
      // (1) SHOW / FILTER (combo presets + multi-isolate disc/cat → only these shaded) and
      // (2) COLOUR BY (tag / variants / custom / param) — applied within the shown set.
      const SECT = 'border:1px solid rgba(255,255,255,0.10);border-radius:8px;padding:8px;display:flex;flex-direction:column;gap:10px';
      const showSection = el('div', { style: SECT });
      showSection.appendChild(sectionTitle('① SHOW / FILTER'));
      showSection.appendChild(el('div', { style: 'font-size:11px;color:#9aa3b2;margin-top:-4px' }, 'Pick a combo or tick disciplines/categories → only those shaded, the rest ghosted.'));
      const colourSection = el('div', { style: SECT });
      colourSection.appendChild(sectionTitle('② COLOUR BY'));
      colourSection.appendChild(el('div', { style: 'font-size:11px;color:#9aa3b2;margin-top:-4px' }, 'Colours the SHOWN set (ghosted elements stay ghosted). e.g. Show MEP + Colour by Category.'));
      wrap.appendChild(showSection);
      wrap.appendChild(colourSection);

      // ── BUILD 2 — Ghost appearance ──
      const ghostBox = el('div', {});
      ghostBox.appendChild(sectionTitle('Ghost appearance'));
      const tintHex = '#' + ('000000' + (state.ghostStyle.tint >>> 0).toString(16)).slice(-6);
      const tint = el('input', { type: 'color', value: tintHex, style: 'width:34px;height:24px;padding:0;border:none;background:none;cursor:pointer' });
      tint.addEventListener('input', () => { state.ghostStyle.tint = parseInt(tint.value.slice(1), 16); reapplyGhosts(); });
      // V1 — ghost is INTENTIONALLY never fully solid (it's the "faded rest"); the old 60%
      // cap made max read as "should be solid". Widen the usable range to 90% + relabel +
      // a note so max ≠ "expected solid" (for solid, use ① SHOW / FILTER). Linear value→state.
      const op = el('input', { type: 'range', min: '2', max: '90', value: String(Math.round(state.ghostStyle.opacity * 100)),
        title: 'Ghost fade — how visible the ghosted "rest" is (never fully solid; use SHOW/FILTER for solid)', style: 'flex:1' });
      const opVal = el('span', { style: 'font-size:11px;color:#9aa3b2;width:34px;text-align:right' }, Math.round(state.ghostStyle.opacity * 100) + '%');
      op.addEventListener('input', () => { state.ghostStyle.opacity = (+op.value) / 100; opVal.textContent = op.value + '%'; reapplyGhosts(); });
      ghostBox.appendChild(el('div', { style: 'display:flex;gap:8px;align-items:center;margin-top:4px' }, [
        el('span', { style: 'font-size:12px;color:#e6e6e6' }, 'Tint'), tint,
        el('span', { style: 'font-size:12px;color:#e6e6e6;margin-left:8px' }, 'Ghost fade'), op, opVal
      ]));
      ghostBox.appendChild(el('div', { style: 'font-size:11px;color:#9aa3b2;margin-top:2px' },
        'Ghost stays translucent by design — to make elements solid, use ① SHOW / FILTER (shade them).'));
      wrap.appendChild(ghostBox);

      // ── BUILD 3 — Colour by token ──
      const colBox = el('div', {});
      colBox.appendChild(sectionTitle('Colour by tag'));
      // BUG 2 — buttons reflect the ACTIVE scheme (pressed) so state is visible + the
      // toggle is obvious. cKind/cTok read the single state model (state.vizColour).
      const cKind = state.vizColour && state.vizColour.kind, cTok = state.vizColour && state.vizColour.token;
      const pressed = (on) => on ? 'border-color:#3B82F6;background:rgba(59,130,246,0.30);color:#fff' : '';
      const tokens = [['DISC', 'Discipline'], ['CAT', 'Category'], ['SYS', 'System'], ['LVL', 'Level'], ['FUNC', 'Function'], ['PROD', 'Product']];
      const tokRow = el('div', { style: 'display:flex;flex-wrap:wrap;gap:4px;margin-top:4px' });
      tokens.forEach(([t, label]) => tokRow.appendChild(
        el('button', { class: 'btn sm', style: pressed(cKind === 'token' && cTok === t), onclick: () => colourByToken(t) }, label)));
      // Part B — System palette: standard MEP-system colours (vs the generic 'System' token
      // button which palette-assigns). Shown pressed when active.
      tokRow.appendChild(el('button', {
        class: 'btn sm', style: (state.vizPreset === '__syspalette' ? 'border-color:#3B82F6;background:rgba(59,130,246,0.30);color:#fff' : ''),
        title: 'Standard MEP system colours (DCW/DHW/SAN/VEN/GAS/FP/…)',
        onclick: () => { applySystemPalette(); renderVisualizePanel(); } }, '🛠 System palette'));
      tokRow.appendChild(el('button', { class: 'btn sm subtle', onclick: () => { clearColour(); renderVisualizePanel(); toast('Colour cleared'); } }, 'Clear colour'));
      colBox.appendChild(tokRow);
      // C4 — colour by coordination status (clash / issue).
      colBox.appendChild(el('div', { style: 'display:flex;flex-wrap:wrap;gap:4px;margin-top:4px' }, [
        el('button', { class: 'btn sm', style: pressed(cKind === 'clash'), title: 'Colour by clash status (worst per element)', onclick: () => colourByClashStatus() }, 'Clash status'),
        el('button', { class: 'btn sm', style: pressed(cKind === 'issue'), title: 'Colour by issue status (open / resolved)', onclick: () => colourByIssueStatus() }, 'Issue status'),
      ]));
      // M4 — palette set + colour-by-ANY-parameter (categorical or numeric gradient).
      const palSel = el('select', { style: 'background:#1a1d24;color:#e6e6e6;border:1px solid rgba(255,255,255,0.15);border-radius:4px;padding:3px;font-size:11px;flex:1' });
      Object.keys(PALETTE_SETS).forEach(p => { const o = el('option', { value: p }, p); if (p === state.vizPalette) o.selected = true; palSel.appendChild(o); });
      palSel.addEventListener('change', () => { state.vizPalette = palSel.value; if (state.vizColour) { if (state.vizColour.kind === 'param') colourByParam(state.vizColour.token); else if (state.vizColour.kind === 'token') colourByToken(state.vizColour.token); } });
      const paramSel = el('select', { style: 'background:#1a1d24;color:#e6e6e6;border:1px solid rgba(255,255,255,0.15);border-radius:4px;padding:3px;font-size:11px;flex:1' });
      paramSel.appendChild(el('option', { value: '' }, 'Colour by parameter…'));
      paramKeys().forEach(k => paramSel.appendChild(el('option', { value: k }, k)));
      paramSel.addEventListener('change', () => { if (paramSel.value) colourByParam(paramSel.value); });
      colBox.appendChild(el('div', { style: 'display:flex;gap:4px;margin-top:5px' }, [
        el('span', { style: 'font-size:11px;color:#9aa3b2;align-self:center' }, 'Palette'), palSel, paramSel,
      ]));
      // Legend — rendered by the M1 engine (was the overlay's). M4 makes the
      // swatches interactive (click = isolate, shift-click = hide, hover = highlight).
      if (state.vizColour && state.vizColour.legend && state.vizColour.legend.length) {
        colBox.appendChild(renderVizLegend(state.vizColour));
      }
      colourSection.appendChild(colBox);

      // ── C6 — Search → act ──
      const searchBox = el('div', {});
      searchBox.appendChild(sectionTitle('Search → act'));
      const sInput = el('input', { type: 'text', value: state.vizSearchQuery, placeholder: 'Find by value…',
        style: 'flex:1;background:#1a1d24;color:#e6e6e6;border:1px solid rgba(255,255,255,0.15);border-radius:4px;padding:4px;font-size:11px' });
      sInput.addEventListener('input', () => { state.vizSearchQuery = sInput.value; });
      sInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') searchAct('isolate'); });
      const sField = el('select', { style: 'background:#1a1d24;color:#e6e6e6;border:1px solid rgba(255,255,255,0.15);border-radius:4px;padding:3px;font-size:11px;flex:0 0 auto' });
      sField.appendChild(el('option', { value: '*' }, 'Any'));
      SEARCH_TOKENS.concat(paramKeys()).forEach(f => { const o = el('option', { value: f }, f); if (f === state.vizSearchField) o.selected = true; sField.appendChild(o); });
      sField.addEventListener('change', () => { state.vizSearchField = sField.value; });
      searchBox.appendChild(el('div', { style: 'display:flex;gap:4px;margin-top:4px' }, [sInput, sField]));
      searchBox.appendChild(el('div', { style: 'display:flex;flex-wrap:wrap;gap:4px;margin-top:5px' }, [
        el('button', { class: 'btn sm', title: 'Isolate matches (ghost the rest)', onclick: () => searchAct('isolate') }, 'Isolate'),
        el('button', { class: 'btn sm', title: 'Hide matches', onclick: () => searchAct('hide') }, 'Hide'),
        el('button', { class: 'btn sm', title: 'Colour matches', onclick: () => searchAct('colour') }, 'Colour'),
        el('button', { class: 'btn sm', title: 'Select matches', onclick: () => searchAct('select') }, 'Select'),
      ]));
      wrap.appendChild(searchBox);

      // ── BUILD 5 — Discipline presets ──
      const presetBox = el('div', {});
      presetBox.appendChild(sectionTitle('Discipline presets'));
      const pRow = el('div', { style: 'display:flex;flex-wrap:wrap;gap:4px;margin-top:4px' });
      Object.keys(DISC_PRESETS).forEach(name => pRow.appendChild(
        el('button', { class: 'btn sm', style: (state.vizPreset === name ? 'border-color:#3B82F6;background:rgba(59,130,246,0.30);color:#fff' : ''), onclick: () => { applyDisciplinePreset(name); renderVisualizePanel(); } }, name)));
      // P3b — discipline base colour + per-category variant shades.
      pRow.appendChild(el('button', { class: 'btn sm',
        style: (state.vizPreset === '__variants' ? 'border-color:#3B82F6;background:rgba(59,130,246,0.30);color:#fff' : ''),
        onclick: () => { applyDisciplineVariants(); renderVisualizePanel(); } }, 'Disc + variants'));
      presetBox.appendChild(pRow);
      colourSection.appendChild(presetBox);

      // V6 — SAVED VIEWS: compose a Show+Colour combo once, recall instantly (per project).
      const savedBox = el('div', {});
      savedBox.appendChild(sectionTitle('Saved views'));
      const savedRow = el('div', { style: 'display:flex;flex-wrap:wrap;gap:4px;margin-bottom:4px' });
      loadSavedViews().forEach(v => {
        const b = el('button', { class: 'btn sm', title: 'Recall: ' + v.name, onclick: () => recallNamedView(v.name) }, v.name);
        const x = el('span', { style: 'cursor:pointer;margin-left:4px;opacity:0.6', title: 'Delete',
          onclick: (e) => { e.stopPropagation(); deleteNamedView(v.name); } }, '✕');
        b.appendChild(x); savedRow.appendChild(b);
      });
      if (!loadSavedViews().length) savedRow.appendChild(el('div', { style: 'font-size:11px;color:#9aa3b2' }, 'No saved views yet — compose a Show + Colour combo, then Save.'));
      savedBox.appendChild(savedRow);
      savedBox.appendChild(el('button', { class: 'btn sm', style: 'width:100%', onclick: () => {
        const name = (typeof window !== 'undefined' && window.prompt) ? window.prompt('Save current view as:', '') : '';
        if (name) saveNamedView(name);
      } }, '💾 Save current view'));
      wrap.appendChild(savedBox);

      // ── BUILD 1 — By discipline ──
      const byDisc = el('div', {});
      byDisc.appendChild(sectionTitle('By discipline'));
      const discCounts = {};
      Object.values(state.elementMap).forEach(m => { const d = discOf(m); if (d) discCounts[d] = (discCounts[d] || 0) + 1; });
      // V3 — combo presets: one-click "show these, ghost rest" for common discipline sets.
      if (discs.length) {
        const present = new Set(discs);
        const comboRow = el('div', { style: 'display:flex;flex-wrap:wrap;gap:4px;margin-bottom:6px' });
        comboRow.appendChild(el('button', { class: 'btn sm', onclick: () => comboPreset(discs.slice()) }, 'All'));
        [['MEP', ['M', 'E', 'P']], ['M&E', ['M', 'E']], ['M&P', ['M', 'P']], ['E&P', ['E', 'P']]].forEach(combo => {
          const avail = combo[1].filter(c => present.has(c));
          if (avail.length >= 2) comboRow.appendChild(el('button', { class: 'btn sm', onclick: () => comboPreset(avail) }, combo[0]));
        });
        discs.forEach(d => comboRow.appendChild(el('button', { class: 'btn sm subtle', onclick: () => comboPreset([d]) }, d)));
        byDisc.appendChild(comboRow);
      }
      if (!discs.length) byDisc.appendChild(el('div', { style: 'font-size:11px;color:#9aa3b2' }, 'No discipline data'));
      discs.forEach(d => byDisc.appendChild(vizModeRow(d, discCounts[d],
        () => state.vizDiscMode.get(d),
        (m) => state.vizDiscMode.set(d, m),
        () => shadeOnlyDiscipline(d),        // label / ◎ click isolates
        d,                                    // B1 — custom-colour key
        state.vizDiscSel, d)));               // P2 — multi-isolate tick
      if (discs.length) {
        const dActions = el('div', { style: 'display:flex;gap:4px;margin-top:4px' });
        dActions.appendChild(el('button', { class: 'btn sm', style: 'flex:1;white-space:nowrap',
          onclick: () => shadeOnlySet('disc') }, '◎ Shade ticked, ghost rest'));
        dActions.appendChild(el('button', { class: 'btn sm subtle',
          onclick: () => { state.vizDiscSel.clear(); renderVisualizePanel(); } }, 'Clear ticks'));
        byDisc.appendChild(dActions);
      }
      showSection.appendChild(byDisc);

      // ── BUILD 1 — By category ──
      const cats = distinctTokens('CAT');
      if (cats.length) {
        const byCat = el('div', {});
        byCat.appendChild(sectionTitle('By category'));
        const catCounts = {};
        Object.values(state.elementMap).forEach(m => { const c = catKey(m); if (c) catCounts[c] = (catCounts[c] || 0) + 1; });
        cats.forEach(c => byCat.appendChild(vizModeRow(c, catCounts[c],
          () => state.vizCatMode.get(c),
          (m) => state.vizCatMode.set(c, m),
          () => shadeOnlyCategory(c),         // label / ◎ click isolates
          c,                                   // B1 — custom-colour key
          state.vizCatSel, c)));               // P2 — multi-isolate tick
        const cActions = el('div', { style: 'display:flex;gap:4px;margin-top:4px' });
        cActions.appendChild(el('button', { class: 'btn sm', style: 'flex:1;white-space:nowrap',
          onclick: () => shadeOnlySet('cat') }, '◎ Shade ticked, ghost rest'));
        cActions.appendChild(el('button', { class: 'btn sm subtle',
          onclick: () => { state.vizCatSel.clear(); renderVisualizePanel(); } }, 'Clear ticks'));
        byCat.appendChild(cActions);
        showSection.appendChild(byCat);
      }

      // ── WS2 — Keep solid under x-ray / ghost ──
      const keepBox = el('div', {});
      keepBox.appendChild(sectionTitle('Keep solid under x-ray / ghost'));
      keepBox.appendChild(el('div', { style: 'font-size:11px;color:#9aa3b2;margin-bottom:4px' },
        'Selected disciplines/categories stay solid while View → X-ray / Ghost fades the rest.'));
      const mkKeepChip = (label, isOn, toggle) =>
        el('button', { class: 'btn sm' + (isOn ? '' : ' subtle'),
          onclick: () => { toggle(); applyKeepSolidLive(); renderVisualizePanel(); } }, label);
      const kdRow = el('div', { style: 'display:flex;flex-wrap:wrap;gap:4px;margin-bottom:6px' });
      discs.forEach(d => kdRow.appendChild(mkKeepChip(d, state.vizKeepSolidDisc.has(d),
        () => { state.vizKeepSolidDisc.has(d) ? state.vizKeepSolidDisc.delete(d) : state.vizKeepSolidDisc.add(d); })));
      keepBox.appendChild(kdRow);
      if (cats.length) {
        const kcRow = el('div', { style: 'display:flex;flex-wrap:wrap;gap:4px' });
        cats.forEach(c => kcRow.appendChild(mkKeepChip(c, state.vizKeepSolidCat.has(c),
          () => { state.vizKeepSolidCat.has(c) ? state.vizKeepSolidCat.delete(c) : state.vizKeepSolidCat.add(c); })));
        keepBox.appendChild(kcRow);
      }
      keepBox.appendChild(el('div', { style: 'display:flex;gap:6px;margin-top:6px' }, [
        el('button', { class: 'btn sm subtle', onclick: () => {
          state.vizKeepSolidDisc.clear(); state.vizKeepSolidCat.clear(); applyKeepSolidLive(); renderVisualizePanel();
        } }, 'Clear keep-solid'),
        el('button', { class: 'btn sm subtle', onclick: () => { clearRenderMode(); renderVisualizePanel(); } }, 'Reset view (shaded)'),
      ]));
      wrap.appendChild(keepBox);

      // ── Clash focus options ──
      const clashBox = el('div', {});
      clashBox.appendChild(sectionTitle('Clash focus'));
      const cb = el('input', { type: 'checkbox' });
      cb.checked = !!state.clashSection.onFocus;
      cb.addEventListener('change', () => { state.clashSection.onFocus = cb.checked; if (!cb.checked) clearClashSection(); });
      const lbl = el('label', { style: 'display:flex;gap:6px;align-items:center;font-size:12px;color:#e6e6e6;margin-top:4px;cursor:pointer' }, [cb, 'Section box around the clash pair']);
      clashBox.appendChild(lbl);
      clashBox.appendChild(el('div', { style: 'font-size:11px;color:#9aa3b2;margin-top:2px' },
        'Click a clash (or its View button) to isolate the pair: A red, B orange, everything else ghosted, auto-zoomed.'));
      wrap.appendChild(clashBox);

      pane.appendChild(wrap);
    }

    // WS2 — if a global x-ray/ghost/wire mode is active, re-apply it so a change
    // to the keep-solid exclusion set takes effect immediately.
    function applyKeepSolidLive() {
      if (state.renderMode && state.renderMode !== 'shaded') setRenderMode(state.renderMode);
    }

    // Re-apply ghost styling to whatever is currently ghosted (tint/opacity live update).
    // M2 — live tint/opacity: mutate the ONE shared ghost material. Instant; no
    // traverse, no rebuild. Every ghosted mesh already points at this material.
    function reapplyGhosts() {
      const gs = state.ghostStyle;
      const mat = getGhostMaterial();
      mat.color.set(gs.tint);
      mat.opacity = gs.opacity;
      mat.transparent = true;
      mat.needsUpdate = true;
    }

    function renderModels() {
      const list = $('#modelsList');
      list.innerHTML = '';
      if (!state.models.length) {
        list.appendChild(el('div', { class: 'empty-state' }, 'No models uploaded'));
        return;
      }
      state.models.forEach(m => {
        const initials = (m.uploadedBy || 'XX').split(/\s+/).map(s => s[0]).slice(0, 2).join('').toUpperCase();
        const colour = stringHashColour(m.uploadedBy || m.id);
        const row = el('div', { class: 'model-row' }, [
          el('input', { type: 'checkbox', checked: 'checked', 'data-id': m.id }),
          el('span', { class: 'label', title: m.name || m.fileName }, m.name || m.fileName || 'model'),
          el('span', { class: 'ver' }, m.version ? `v${m.version}` : ''),
          el('span', { class: 'avatar', style: `background:${colour}`, title: m.uploadedBy || '' }, initials),
          el('span', { class: 'timestamp' }, relativeTime(m.uploadedAt))
        ]);
        const cb = $('input[type=checkbox]', row);
        if (state.modelVisible.has(m.id)) cb.checked = state.modelVisible.get(m.id);
        cb.addEventListener('change', () => {
          // FEDERATION — toggle THIS model root by id (three.js gates its meshes via
          // the root's visibility), then re-apply the active appearance across the
          // federation so a newly-shown model picks up the current scheme.
          state.modelVisible.set(m.id, cb.checked);
          if (V.setModelVisibleById && V.setModelVisibleById(m.id, cb.checked)) {
            applyAppearance();
          } else {
            const extras = window.STING_VIEWER_EXTRAS;
            const label = m.name || m.fileName || '';
            if (extras && extras.setModelVisible && label) extras.setModelVisible(label, cb.checked);
            else if (V.modelRoot) vizGroup().traverse(obj => { if (obj.isMesh) obj.visible = cb.checked; });
          }
        });
        list.appendChild(row);
      });
    }

    function stringHashColour(s) {
      const palette = ['#3B82F6', '#22C55E', '#F59E0B', '#A855F7', '#EC4899', '#14B8A6', '#F97316'];
      let h = 0; for (let i = 0; i < (s || '').length; i++) h = (h * 31 + s.charCodeAt(i)) & 0xffff;
      return palette[h % palette.length];
    }
    function relativeTime(iso) {
      if (!iso) return '';
      const d = new Date(iso);
      if (isNaN(d.getTime())) return '';
      const min = Math.round((Date.now() - d.getTime()) / 60000);
      if (min < 1) return 'just now';
      if (min < 60) return `${min}m ago`;
      const hr = Math.round(min / 60);
      if (hr < 24) return `${hr}h ago`;
      const day = Math.round(hr / 24);
      return `${day}d ago`;
    }

    // ── Model tree ─────────────────────────────────────────────────────
    function buildModelTree() {
      const root = $('#modelTree');
      root.innerHTML = '';
      const map = state.elementMap || {};
      const guids = Object.keys(map);
      $('#treeCount').textContent = guids.length ? `${guids.length} elements` : '';
      if (!guids.length) {
        root.appendChild(el('div', { class: 'empty-state' }, 'No element map'));
        return;
      }
      // Group: Level → Discipline → System → Element
      const tree = {};
      guids.forEach(g => {
        const meta = map[g] || {};
        const lvl  = meta.level || 'Unassigned';
        const disc = meta.discipline || 'Other';
        const sys  = meta.system || meta.category || 'General';
        tree[lvl] = tree[lvl] || {};
        tree[lvl][disc] = tree[lvl][disc] || {};
        tree[lvl][disc][sys] = tree[lvl][disc][sys] || [];
        tree[lvl][disc][sys].push({ guid: g, name: meta.name || meta.tag || g.slice(0, 8) });
      });

      function buildNode(label, children, count, leaf, payload) {
        const node = el('div', { class: 'tree-node closed' });
        const row = el('div', { class: 'tree-row' });
        row.appendChild(el('span', { class: 'chev' }, leaf ? '' : '▶'));
        row.appendChild(el('span', { class: 'name' }, label));
        if (count != null) row.appendChild(el('span', { class: 'count' }, String(count)));
        node.appendChild(row);
        if (!leaf) {
          const kids = el('div', { class: 'tree-children' });
          children.forEach(c => kids.appendChild(c));
          node.appendChild(kids);
          row.addEventListener('click', () => {
            node.classList.toggle('open'); node.classList.toggle('closed');
          });
        } else {
          // Ctrl/Cmd-click toggles the leaf in the multi-selection set.
          // Plain click replaces. Shift-click (range select) is best-effort:
          // we walk the visible tree-row siblings between this row and the
          // most recent primary and add them all.
          row.addEventListener('click', (ev) => {
            if (ev.ctrlKey || ev.metaKey) {
              selectElementByGuid(payload.guid, 'toggle');
            } else if (ev.shiftKey && state.selectedElementGuid) {
              // Range select within the same parent group of leaves.
              const parent = node.parentElement;
              if (parent) {
                const siblings = $$('.tree-node', parent).filter(n => n.querySelector('.chev')?.textContent === '');
                const rows = siblings.map(s => s);
                const startIdx = rows.indexOf(node);
                // Find the prior primary's row to anchor the range.
                let anchorIdx = -1;
                rows.forEach((r, i) => {
                  if (r._guid === state.selectedElementGuid) anchorIdx = i;
                });
                if (anchorIdx >= 0) {
                  const [a, b] = anchorIdx < startIdx ? [anchorIdx, startIdx] : [startIdx, anchorIdx];
                  for (let i = a; i <= b; i++) {
                    if (rows[i]._guid) selectElementByGuid(rows[i]._guid, 'add');
                  }
                  return;
                }
              }
              selectElementByGuid(payload.guid, 'add');
            } else {
              selectElementByGuid(payload.guid, 'replace');
            }
          });
          node._guid = payload.guid;        // for shift-range lookup
        }
        return node;
      }
      Object.keys(tree).sort().forEach(lvl => {
        const lvlChildren = [];
        let lvlCount = 0;
        Object.keys(tree[lvl]).sort().forEach(disc => {
          const discChildren = [];
          let discCount = 0;
          Object.keys(tree[lvl][disc]).sort().forEach(sys => {
            const items = tree[lvl][disc][sys];
            // U3 — render a deterministic page (200) and surface a "+ N more"
            // affordance when there's overflow, instead of silently truncating.
            const PAGE = 200;
            const visible = items.slice(0, PAGE);
            const sysKids = visible.map(it => buildNode(it.name, [], null, true, it));
            if (items.length > PAGE) {
              const more = el('div', { class: 'tree-row', style: 'color:var(--accent);cursor:pointer;font-style:italic' },
                `+ ${items.length - PAGE} more — load all`);
              more.addEventListener('click', (ev) => {
                ev.stopPropagation();
                const parent = more.parentElement;
                items.slice(PAGE).forEach(it => parent.appendChild(buildNode(it.name, [], null, true, it)));
                more.remove();
              });
              sysKids.push(more);
            }
            discChildren.push(buildNode(sys, sysKids, items.length));
            discCount += items.length;
          });
          lvlChildren.push(buildNode(disc, discChildren, discCount));
          lvlCount += discCount;
        });
        root.appendChild(buildNode(lvl, lvlChildren, lvlCount));
      });

      // R9 — when the search query is cleared, restore the default
      // collapsed state instead of leaving every branch expanded.
      $('#treeSearch').addEventListener('input', (e) => {
        const q = e.target.value.trim().toLowerCase();
        $$('.tree-node', root).forEach(n => {
          const txt = n.textContent.toLowerCase();
          const match = !q || txt.includes(q);
          n.style.display = match ? '' : 'none';
          if (q) {
            // expand matches so users can see hits in context
            if (match) { n.classList.remove('closed'); n.classList.add('open'); }
          } else {
            // search cleared — collapse non-leaf branches back to default
            if (n.querySelector('.tree-children')) {
              n.classList.add('closed'); n.classList.remove('open');
            }
          }
        });
      });
    }

    // ── Levels strip ───────────────────────────────────────────────────
    function buildLevelStrip() {
      const strip = $('#levelStrip');
      strip.innerHTML = '';
      const set = new Set();
      Object.values(state.elementMap || {}).forEach(m => { if (m && m.level) set.add(m.level); });
      const arr = Array.from(set).sort((a, b) => {
        // try natural sort
        return String(a).localeCompare(String(b), undefined, { numeric: true });
      });
      const fallback = ['B1','GF','L01','L02','L03','L04','RF'];
      const levels = arr.length ? arr : fallback;
      strip.appendChild(el('button', { class: 'nav-arrow' }, '◀'));
      levels.forEach(lvl => {
        const pill = el('button', { class: 'level-pill', 'data-lvl': lvl }, lvl);
        pill.addEventListener('click', (e) => {
          if (e.shiftKey) pill.classList.toggle('active');
          else {
            const isActive = pill.classList.contains('active');
            $$('.level-pill').forEach(p => p.classList.remove('active'));
            if (!isActive) pill.classList.add('active');
          }
          applyLevelFilter();
        });
        strip.appendChild(pill);
      });
      strip.appendChild(el('button', { class: 'nav-arrow' }, '▶'));

      // Compute Y bands from model bounds — fall back to even slices.
      computeLevelBands(levels);
    }

    function computeLevelBands(levels) {
      const b = V.modelBounds;
      if (!b || b.isEmpty()) return;
      const min = b.min.y, max = b.max.y;
      // R8 — prefer real elevations from the element-map when available.
      // We accept any of these per-element fields (in metres):
      //   levelElevation | levelElevationM | levelTopM | levelBaseM
      // For each unique level, take the median of all reported
      // elevations as the level's base height; band tops are the next
      // level's base (or modelBounds.max for the topmost).
      const levelHeights = new Map();
      Object.values(state.elementMap || {}).forEach(m => {
        if (!m || !m.level) return;
        const h = m.levelElevation ?? m.levelElevationM ?? m.levelBaseM ?? m.levelTopM;
        if (h == null || isNaN(+h)) return;
        if (!levelHeights.has(m.level)) levelHeights.set(m.level, []);
        levelHeights.get(m.level).push(+h);
      });
      const haveElevations = levelHeights.size >= Math.max(2, levels.length - 1);
      if (haveElevations) {
        const sorted = levels.map(lvl => {
          const samples = (levelHeights.get(lvl) || []).slice().sort((a, b) => a - b);
          const base = samples.length ? samples[Math.floor(samples.length / 2)] : null;
          return { level: lvl, base };
        }).filter(x => x.base != null).sort((a, b) => a.base - b.base);
        state.levelBands = sorted.map((row, i) => ({
          level: row.level,
          min: row.base,
          max: i + 1 < sorted.length ? sorted[i + 1].base : max
        }));
        if (state.levelBands.length) return;
      }
      // Fallback: equal slices when no elevation data is supplied.
      const step = (max - min) / Math.max(1, levels.length);
      state.levelBands = levels.map((l, i) => ({
        level: l, min: min + i * step, max: min + (i + 1) * step
      }));
    }

    // L7 — cache mesh centroid-Y per mesh.uuid so level filtering is
    // instant on large models (4k+ elements). The cache is invalidated
    // when the model is replaced (handled in the boot observer below).
    const centroidYCache = new Map();
    function getCentroidY(mesh) {
      const cached = centroidYCache.get(mesh.uuid);
      if (cached != null) return cached;
      const bb = new THREE_.Box3().setFromObject(mesh);
      const y = (bb.min.y + bb.max.y) / 2;
      centroidYCache.set(mesh.uuid, y);
      return y;
    }
    function invalidateCentroidCache() { centroidYCache.clear(); }

    function applyLevelFilter() {
      const active = $$('.level-pill.active').map(p => p.dataset.lvl);
      if (!active.length) {
        V.renderer.clippingPlanes = [];
        if (V.modelRoot) vizGroup().traverse(o => { if (o.isMesh) o.visible = true; });
        return;
      }
      const wanted = state.levelBands.filter(b => active.includes(b.level));
      if (!V.modelRoot || !wanted.length) return;
      vizGroup().traverse(obj => {
        if (!obj.isMesh) return;
        const cy = getCentroidY(obj);
        obj.visible = wanted.some(w => cy >= w.min - 0.01 && cy <= w.max + 0.01);
      });
    }

    // ── Saved views ────────────────────────────────────────────────────
    $('#btnAddView').addEventListener('click', async () => {
      const name = await promptInline({
        title: 'Save current view',
        label: 'Name',
        placeholder: 'e.g. Plant room — main entry',
        defaultValue: `View ${state.savedViews.length + 1}`,
        minLength: 1, maxLength: 80,
        okLabel: 'Save view',
      });
      if (!name) return;
      state.savedViews.push({ id: 'v' + Date.now(), name, snapshot: captureViewState() });
      renderSavedViews();
      logHistory(`Saved view "${name}"`);
    });
    $('#btnPresent').addEventListener('click', () => presentMode());

    function captureViewState() {
      const cam = V.camera;
      return {
        camPos: cam.position.toArray(),
        camTarget: V.controls.target.toArray(),
        disciplines: Array.from(state.activeDisciplines),
        levels: $$('.level-pill.active').map(p => p.dataset.lvl),
        viz: serializeViz(),   // C5 — full visualize state (scheme + modes + custom colours)
      };
    }
    function restoreViewState(s) {
      if (!s) return;
      V.camera.position.fromArray(s.camPos);
      V.controls.target.fromArray(s.camTarget);
      V.controls.update();
      // B5 — restore the full snapshot, not just camera. Disciplines +
      // levels are re-applied via the same chip / pill click flow so
      // visibility + ghost states match exactly.
      if (Array.isArray(s.disciplines)) {
        $$('.disc-chip').forEach(c => c.classList.toggle('active', s.disciplines.includes(c.dataset.disc)));
        applyDisciplineFilter(s.disciplines);
      }
      if (Array.isArray(s.levels)) {
        $$('.level-pill').forEach(p => p.classList.toggle('active', s.levels.includes(p.dataset.lvl)));
        applyLevelFilter();
      }
      // C5 — restore the full visualize appearance, then mirror it to a live meeting.
      if (s.viz) { applyVizSnapshot(s.viz); broadcastAppearance(); }
    }
    function renderSavedViews() {
      const list = $('#savedViewsList');
      list.innerHTML = '';
      state.savedViews.forEach(v => {
        const row = el('div', { class: 'saved-view' }, [
          el('span', { class: 'star' }, '★'),
          el('span', { class: 'label' }, v.name)
        ]);
        row.addEventListener('click', () => { restoreViewState(v.snapshot); logHistory(`Opened "${v.name}"`); });
        list.appendChild(row);
      });
    }
    let presentTimer = null;
    function presentMode() {
      if (!state.savedViews.length) return toast('Save some views first', 'warn');
      if (presentTimer) {
        clearInterval(presentTimer); presentTimer = null;
        toast('Present mode stopped'); return;
      }
      let i = 0;
      restoreViewState(state.savedViews[0].snapshot);
      presentTimer = setInterval(() => {
        i = (i + 1) % state.savedViews.length;
        restoreViewState(state.savedViews[i].snapshot);
      }, 5000);
      toast('Present mode: 5s cycle. Click again to stop.');
    }

    // ── Session history ────────────────────────────────────────────────
    function renderHistory() {
      const list = $('#sessionHistory');
      list.innerHTML = '';
      if (!state.history.length) {
        list.appendChild(el('div', { class: 'empty-state' }, 'No actions yet'));
        return;
      }
      state.history.slice(0, 20).forEach(h => {
        const t = h.time;
        const lbl = `${t.getHours().toString().padStart(2,'0')}:${t.getMinutes().toString().padStart(2,'0')}  ${h.label}`;
        const row = el('div', { class: 'saved-view' }, [el('span', { class: 'label' }, lbl)]);
        row.addEventListener('click', () => restoreViewState(h.snapshot));
        list.appendChild(row);
      });
    }

    // ── Right-panel tabs ───────────────────────────────────────────────
    function setupTabs() {
      // R2 — DELEGATE on the stable .tab-bar (was: one-time addEventListener per .tab — the
      // E1 anti-pattern that loses handlers if a tab node is ever re-rendered). One listener
      // resolves the clicked tab via closest(); switching to Visualize re-renders (re-binds)
      // its panel. Durable fix for the whole "dead-until-refresh" class: re-rendered panes
      // re-bind on render, and the tab-bar + bottom panel are delegated on stable parents.
      const bar = $('.tab-bar'); if (!bar) return;
      bar.addEventListener('click', (ev) => {
        const t = ev.target.closest('.tab');
        if (!t || !bar.contains(t)) return;
        $$('.tab-bar .tab').forEach(x => x.classList.remove('active'));
        $$('.tab-pane').forEach(x => x.classList.remove('active'));
        t.classList.add('active');
        const pane = $('#pane-' + t.dataset.tab);
        if (pane) pane.classList.add('active');
        state.rightTab = t.dataset.tab;
        if (t.dataset.tab === 'visualize') renderVisualizePanel();
        if (t.dataset.tab === 'clashes') renderRightClashes();
        if (t.dataset.tab === 'issues')  renderRightIssues();
        if (t.dataset.tab === 'photos')  { loadSitePhotos(); renderPhotos(); }
        if (t.dataset.tab === 'comments') renderComments();
        if (t.dataset.tab === 'activity') renderActivityTimeline();
      });
    }

    // ── Activity timeline (issue audit trail) ──────────────────────────
    async function renderActivityTimeline() {
      const pane = $('#pane-activity');
      if (!pane) return;
      const issueId = state.selectedIssueId;
      if (!issueId) {
        pane.innerHTML = '<div class="empty-state"><span class="glyph">🕓</span>Select an issue to see its activity</div>';
        return;
      }
      const issue = state.issues.find(i => i.id === issueId);
      pane.innerHTML = `
        <div class="prop-section-label">${escapeHtml(issue?.code || issueId)} activity</div>
        <div class="activity-list" id="activityList">
          <div class="inline-loader"><span class="dot-spin"></span>Loading…</div>
        </div>`;
      const data = await api(`/api/projects/${projectId}/issues/${issueId}/activity`);
      const entries = Array.isArray(data) ? data : (data?.items || []);
      const list = $('#activityList');
      if (!list) return;
      if (!entries.length) {
        list.innerHTML = '<div class="empty-state">No activity yet</div>';
        return;
      }
      list.innerHTML = '';
      entries.forEach(e => {
        list.appendChild(renderActivityCard(e));
      });
    }

    /** T3-14 — Build a rich activity card. The same JSON shape (action +
     *  details + userName + timestamp) feeds the BCC desktop and the mobile
     *  issue-detail screen, so the renderer here is the canonical reference
     *  for the visual layout. */
    function renderActivityCard(entry) {
      const when    = entry.timestamp || entry.Timestamp || entry.createdAt || '';
      const action  = entry.action || entry.Action || '';
      const userN   = entry.userName || entry.UserName || entry.author || 'System';
      const dRaw    = entry.detailsJson || entry.DetailsJson || entry.details || '';
      const details = (typeof dRaw === 'string') ? safeParse(dRaw) : (dRaw || {});

      const card = el('div', { class: 'activity-card' });
      card.appendChild(el('div', { class: 'avatar', style: `background:${avatarColor(userN)}` }, initials(userN)));

      const body = el('div', { class: 'body' });
      const head = el('div', { class: 'head' });
      head.appendChild(el('span', { class: 'who'  }, userN));
      head.appendChild(el('span', { class: 'verb' }, ' ' + verbForAction(action, details)));
      head.appendChild(el('span', { class: 'when', title: when }, relativeTime(when)));
      body.appendChild(head);

      const inline = inlineDetail(action, details);
      if (inline) body.appendChild(el('div', { class: 'detail' }, inline));

      // Contextual chip — priority badge / status pill / file thumbnail.
      const chip = chipForActivity(action, details);
      if (chip) body.appendChild(chip);

      card.appendChild(body);
      return card;
    }

    function safeParse(s) {
      if (!s) return {};
      try { return JSON.parse(s); } catch (_) { return {}; }
    }
    function initials(name) {
      const parts = String(name || '?').trim().split(/\s+/).slice(0, 2);
      return parts.map(p => p[0]?.toUpperCase() || '').join('') || '?';
    }
    function avatarColor(name) {
      // Deterministic hue from the user-name so the same person always
      // gets the same swatch across cards.
      let h = 0; for (const ch of String(name || '')) h = (h * 31 + ch.charCodeAt(0)) | 0;
      return `hsl(${Math.abs(h) % 360} 55% 38%)`;
    }
    function relativeTime(iso) {
      if (!iso) return '';
      const d = new Date(iso); if (isNaN(d.getTime())) return iso;
      const s = Math.round((Date.now() - d.getTime()) / 1000);
      if (s < 60)        return s + 's ago';
      if (s < 3600)      return Math.round(s / 60) + 'm ago';
      if (s < 86400)     return Math.round(s / 3600) + 'h ago';
      if (s < 86400 * 7) return Math.round(s / 86400) + 'd ago';
      return d.toLocaleDateString();
    }
    function verbForAction(action, details) {
      const a = String(action || '').toUpperCase();
      if (a === 'CREATE')          return 'created the issue';
      if (a === 'COMMENT')         return 'commented';
      if (a === 'ATTACH' || a === 'ATTACHMENT_ADD') return 'attached a file';
      if (a === 'ATTACHMENT_DELETE') return 'removed an attachment';
      if (a === 'STATUS')          return 'changed status';
      if (a === 'PRIORITY')        return 'changed priority';
      if (a === 'ASSIGN')          return 'changed assignee';
      if (a === 'RESOLVE')         return 'marked resolved';
      if (a === 'CLOSE')           return 'closed the issue';
      if (a === 'REOPEN')          return 're-opened the issue';
      if (a === 'UPDATE')          return 'updated the issue';
      return action || 'updated';
    }
    function inlineDetail(action, details) {
      // Render only field-level diffs as inline text; the chip below carries
      // the visual badge (priority pill, status pill, thumbnail).
      if (!details || typeof details !== 'object') return '';
      const parts = [];
      for (const k of Object.keys(details)) {
        if (k === 'priority' || k === 'status' || k === 'thumbnailUrl' || k === 'fileName') continue;
        const v = details[k];
        if (v && typeof v === 'object' && 'from' in v && 'to' in v) parts.push(`${k}: ${v.from} → ${v.to}`);
        else if (k === 'body' || k === 'comment') parts.push(String(v));
        else if (typeof v === 'string' && v.length < 200) parts.push(`${k}: ${v}`);
      }
      return parts.join(' · ');
    }
    function chipForActivity(action, details) {
      if (!details || typeof details !== 'object') return null;
      // Attachment thumbnail — render an inline preview if the server
      // surfaced a thumbnailUrl. Falls back to a filename chip.
      if (details.thumbnailUrl || details.fileName) {
        if (details.thumbnailUrl) {
          const img = el('img', { class: 'thumb', src: details.thumbnailUrl, alt: details.fileName || 'attachment' });
          return img;
        }
        return el('span', { class: 'chip' }, '📎 ' + (details.fileName || 'attachment'));
      }
      // Priority chip on PRIORITY change events.
      const pri = details.priority?.to || details.priority;
      if (typeof pri === 'string') return el('span', { class: 'chip priority-' + pri.toUpperCase() }, pri.toUpperCase());
      // Status chip on STATUS change events.
      const st  = details.status?.to || details.status;
      if (typeof st === 'string') return el('span', { class: 'chip status-' + st.toUpperCase() }, st.toUpperCase());
      return null;
    }

    function formatActivity(action, detailsJson) {
      if (!action) return '';
      let detail = '';
      if (detailsJson) {
        try {
          const obj = JSON.parse(detailsJson);
          const parts = [];
          for (const k in obj) {
            const v = obj[k];
            if (v && typeof v === 'object' && 'from' in v && 'to' in v) {
              parts.push(`${k}: ${v.from} → ${v.to}`);
            } else if (v && typeof v === 'object' && 'changed' in v) {
              parts.push(`${k} updated`);
            } else {
              parts.push(`${k}: ${v}`);
            }
          }
          detail = parts.length ? ' — ' + parts.join(', ') : '';
        } catch (_) { /* leave detail blank */ }
      }
      return `${action}${detail}`;
    }

    // ── Comments tab (U2) ──────────────────────────────────────────────
    // When an issue is selected, load its thread; otherwise show a hint.
    let commentsCache = new Map();        // issueId → comment[]
    // R4 — keep the pending comment screenshot in module scope, NOT on
    // the textarea's dataset. Setting a 500 KB base64 string as a DOM
    // attribute thrashes layout / mutation observers; a closure variable
    // costs nothing.
    let pendingAttachment = null;
    async function renderComments() {
      const pane = $('#pane-comments');
      const issueId = state.selectedIssueId;
      if (!issueId) {
        pane.innerHTML = `
          <div class="empty-state">
            <span class="glyph">💬</span>
            Select an issue from the bottom tray to view and reply to its thread.
          </div>`;
        return;
      }
      const issue = state.issues.find(i => i.id === issueId);
      pane.innerHTML = `
        <div class="prop-section-label">${escapeHtml(issue?.code || issueId)} comments</div>
        <div class="comments-list" id="commentsList">
          <div class="inline-loader"><span class="dot-spin"></span>Loading…</div>
        </div>
        <div class="comment-compose">
          <textarea id="commentInput" placeholder="Reply to this issue… (use @ to mention)"></textarea>
          <div class="row">
            <button class="btn ghost sm" id="commentAttach">📷 Attach view</button>
            <button class="btn sm" id="commentSubmit">Post</button>
          </div>
        </div>`;
      $('#commentSubmit').addEventListener('click', () => postComment(issueId));
      $('#commentAttach').addEventListener('click', () => {
        pendingAttachment = downscaleScreenshot(V.renderer.domElement, 1280, 0.85);
        const ta = $('#commentInput');
        ta.value = (ta.value ? ta.value + '\n\n' : '') + '[screenshot attached]';
      });

      const listEl = $('#commentsList');
      let items = commentsCache.get(issueId);
      if (!items) {
        const data = await api(`/api/projects/${projectId}/issues/${issueId}/comments`);
        items = Array.isArray(data) ? data : (data?.items || []);
        commentsCache.set(issueId, items);
      }
      if (!items.length) {
        listEl.innerHTML = '<div class="empty-state" style="padding:14px"><span style="opacity:.6">No replies yet</span></div>';
        return;
      }
      listEl.innerHTML = '';
      items.forEach(c => {
        const who = escapeHtml(c.authorName || c.author || 'Unknown');
        const when = c.createdAt ? new Date(c.createdAt).toLocaleString() : '';
        const item = el('div', { class: 'comment-item' });
        item.innerHTML = `
          <div class="meta"><span class="who">${who}</span><span>${escapeHtml(when)}</span></div>
          <div class="body">${escapeHtml(c.body || c.text || '')}</div>`;
        listEl.appendChild(item);
      });
    }

    async function postComment(issueId) {
      const ta = $('#commentInput');
      const body = (ta?.value || '').trim();
      if (!body) return;
      const payload = { body, attachment: pendingAttachment };
      pendingAttachment = null;          // R4 — drop after read
      const result = await api(`/api/projects/${projectId}/issues/${issueId}/comments`, {
        method: 'POST', body: JSON.stringify(payload)
      });
      const created = result || {
        id: 'local-' + Date.now(),
        body,
        authorName: state.currentUser?.displayName || 'You',
        createdAt: new Date().toISOString()
      };
      const list = commentsCache.get(issueId) || [];
      list.push(created);
      commentsCache.set(issueId, list);
      renderComments();
      updateRightTabCounts();            // X2
      // X9 (light) — auto-scroll the freshly rendered list to the bottom
      // so the new message is visible without manual scrolling.
      const listEl = $('#commentsList');
      if (listEl) listEl.scrollTop = listEl.scrollHeight;
    }

    // ── Properties tab ─────────────────────────────────────────────────
    // Compute the intersection of property keys across multiple elements,
    // keeping only entries where the value is identical for every element.
    // Returns { name, count, kvs: [[k, v], ...] } so the UI can render
    // a single "common properties" view for batched edits / inspection.
    function computeCommonProperties(guids) {
      const arr = guids.map(g => state.elementMap[g] || {});
      if (!arr.length) return { name: '—', count: 0, kvs: [] };
      const flatten = (m) => {
        const out = {};
        Object.entries(m || {}).forEach(([k, v]) => {
          if (v == null) return;
          if (typeof v === 'object') {
            Object.entries(v).forEach(([kk, vv]) => { if (vv != null && typeof vv !== 'object') out[`${k}.${kk}`] = vv; });
          } else {
            out[k] = v;
          }
        });
        return out;
      };
      const flats = arr.map(flatten);
      const keys = Object.keys(flats[0] || {});
      const common = [];
      keys.forEach(k => {
        const v0 = flats[0][k];
        if (flats.every(f => f[k] === v0)) common.push([k, v0]);
      });
      // Surface common category / discipline at the top so the user
      // immediately sees what they've grabbed.
      const categories = new Set(arr.map(m => m.category || ''));
      const disciplines = new Set(arr.map(m => m.discipline || ''));
      return {
        count: arr.length,
        kvs: common,
        categorySummary: categories.size === 1 ? [...categories][0] : `${categories.size} categories`,
        disciplineSummary: disciplines.size === 1 ? [...disciplines][0] : `${disciplines.size} disciplines`
      };
    }

    // M3 — element cost. Reads meta.cost / estimatedCost / totalCost / any CST_* key.
    // Returns { value, currency } or null. Never fabricates — absent ⇒ null ⇒ "—".
    function findCost(meta) {
      if (!meta) return null;
      let v = null;
      if (meta.cost != null) v = meta.cost;
      else if (meta.estimatedCost != null) v = meta.estimatedCost;
      else if (meta.totalCost != null) v = meta.totalCost;
      else { for (const k in meta) { if (/^CST_.*|.*cost$/i.test(k) && meta[k] != null && typeof meta[k] !== 'object') { v = meta[k]; break; } } }
      if (v == null) return null;
      return { value: v, currency: meta.costCurrency || meta.currency || state.currency || 'USD' };
    }
    function formatCurrency(c) {
      if (!c || c.value == null) return '—';
      const n = typeof c.value === 'number' ? c.value : parseFloat(String(c.value).replace(/[^0-9.\-]/g, ''));
      if (!isFinite(n)) return String(c.value);
      try { return new Intl.NumberFormat(undefined, { style: 'currency', currency: c.currency, maximumFractionDigits: 0 }).format(n); }
      catch (_) { return `${c.currency} ${n.toLocaleString()}`; }
    }

    // N6 — humanise an exporter key into a section heading + a plain-object test.
    // Exporter element-map shapes vary (psets / classification / type+instance
    // param bundles), so the panel renders nested groups GENERICALLY rather than
    // assuming fixed field names.
    function n6Humanize(k) {
      const map = {
        psets: 'Property sets', propertySets: 'Property sets', properties: 'Instance properties',
        parameters: 'Instance parameters', classification: 'Classification', classifications: 'Classification',
        typeParams: 'Type parameters', typeParameters: 'Type parameters', typeProperties: 'Type parameters',
        instanceParams: 'Instance parameters', instanceParameters: 'Instance parameters',
        relationships: 'Relationships', relations: 'Relationships', quantities: 'Quantities'
      };
      if (map[k]) return map[k];
      return String(k).replace(/[_\-]+/g, ' ').replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/^\w/, c => c.toUpperCase());
    }
    function n6IsObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }

    function renderProperties(guid) {
      const pane = $('#pane-properties');
      // Multi-select branch — show common-properties summary instead of
      // a single element card, with the multi-aware action stack.
      const sel = state.selectedElementGuids;
      if (sel && sel.size > 1) {
        const guids = [...sel];
        const c = computeCommonProperties(guids);
        const RESERVED = new Set(['name','category','tag','STING_TAG']);
        const rows = c.kvs.filter(([k]) => !RESERVED.has(k));
        pane.innerHTML = `
          <div class="prop-section-label">Selection</div>
          <div class="prop-title">${c.count} elements</div>
          <div class="prop-row"><span class="k">Categories</span><span class="v">${escapeHtml(c.categorySummary || '—')}</span></div>
          <div class="prop-row"><span class="k">Disciplines</span><span class="v">${escapeHtml(c.disciplineSummary || '—')}</span></div>
          <div class="prop-section-label">Common properties (${rows.length})</div>
          ${rows.length
            ? rows.map(([k, v]) => `<div class="prop-row"><span class="k">${escapeHtml(k)}</span><span class="v">${escapeHtml(String(v))}</span></div>`).join('')
            : '<div class="prop-row" style="opacity:0.6">No properties shared by every element</div>'}
          <div class="action-stack">
            <button class="btn full" id="actMultiCreateIssue">🚩 Create issue from selection</button>
            <button class="btn ghost full" id="actMultiFit">🎯 Fit to selection</button>
            <button class="btn ghost full" id="actMultiIsolate">◎ Isolate</button>
            <button class="btn subtle full" id="actMultiHide">⊘ Hide</button>
            <button class="btn subtle full" id="actMultiClear">✕ Clear selection</button>
          </div>`;
        $('#actMultiCreateIssue', pane)?.addEventListener('click', () => {
          openIssueModal({
            guid: state.selectedElementGuid,
            meta: state.elementMap[state.selectedElementGuid] || {},
            multi: guids
          });
        });
        $('#actMultiFit', pane)?.addEventListener('click', () => fitToSelection());
        $('#actMultiIsolate', pane)?.addEventListener('click', () => isolateSelection());
        $('#actMultiHide', pane)?.addEventListener('click', () => hideSelection());
        $('#actMultiClear', pane)?.addEventListener('click', () => selectElementByGuid(null));
        return;
      }
      if (!guid) {
        const issuesOpen = state.issues.filter(i => i.status !== 'RESOLVED').length;
        const overdue = state.issues.filter(i => i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED')).length;
        const tagged = Object.values(state.elementMap || {}).filter(m => m && m.tag).length;
        const total = Object.keys(state.elementMap || {}).length;
        const pct = total ? Math.round(100 * tagged / total) : 0;
        pane.innerHTML = `
          <div class="prop-title">Model overview</div>
          <div class="kpi-grid">
            <div class="kpi"><div class="v">${total.toLocaleString()}</div><div class="k">Elements</div></div>
            <div class="kpi ok"><div class="v">${pct}%</div><div class="k">Tagged</div></div>
            <div class="kpi ${state.clashes.length ? 'crit' : ''}"><div class="v">${state.clashes.length}</div><div class="k">Clashes</div></div>
            <div class="kpi ${overdue ? 'crit' : (issuesOpen ? 'warn' : 'ok')}"><div class="v">${issuesOpen}</div><div class="k">Open issues</div></div>
          </div>
          <div class="prop-section-label">Coordination status</div>
          <div class="prop-row"><span class="k">New clashes</span><span class="v">${state.clashes.filter(c => c.status === 'NEW').length}</span></div>
          <div class="prop-row"><span class="k">Resolved</span><span class="v">${state.clashes.filter(c => c.status === 'RESOLVED').length}</span></div>
          <div class="prop-row"><span class="k">Overdue issues</span><span class="v">${overdue}</span></div>
          <div class="prop-row"><span class="k">Compliance</span><span class="v">${pct}%</span></div>
          <div class="prop-section-label">Last updated</div>
          <div class="prop-row"><span class="k">Model</span><span class="v">${escapeHtml(state.modelName || '—')}</span></div>
        `;
        return;
      }
      const meta = state.elementMap[guid] || {};
      const tag = meta.tag || meta.STING_TAG || '';
      // Polish — accept either nested {dimensions:{}, performance:{}} or
      // flat keys (width_mm, flow_lps, etc.) coming back from
      // ModelsController.GetElementMap. Identity / dimension / performance
      // groups are inferred by suffix if the response is flat.
      const idCard = [
        ['Discipline', meta.discipline],
        ['System',     meta.system],
        ['Status',     meta.status],
        ['Level',      meta.level],
        ['Family',     meta.family],
        ['Type',       meta.type],
        ['Mark',       meta.mark]
      ].filter(([, v]) => v != null && v !== '');
      const RESERVED = new Set(['name','category','tag','STING_TAG','discipline','system','status','level','family','type','mark','dimensions','performance',
        // N6 — these scalars/arrays now have dedicated sections (Quantities / Cost /
        // Materials / Relationships); keep them out of the generic "Properties" bucket.
        'materials','cost','costCurrency','area','volume','length','quantity','count',
        'host','hostName','hostId','hostGuid','assembly','room','space','ifcType','ifcGuid','globalId','guid','revitId','elementId']);
      const isDimKey  = k => /(_mm|_m|width|height|depth|length|diameter|thickness|area)$/i.test(k);
      const isPerfKey = k => /(flow|pressure|capacity|voltage|current|power|temperature|cooling|heating|wattage|kw|lps|cfm|pa)/i.test(k);
      let dims = Object.entries(meta.dimensions  || {}).filter(([, v]) => v != null);
      let perfs = Object.entries(meta.performance || {}).filter(([, v]) => v != null);
      // R11 — accumulate any leftover scalar keys into a generic
      // "Properties" bucket so sparse element-map responses still show
      // useful data instead of dropping it.
      const claimedDims  = new Set(dims.map(([k]) => k));
      const claimedPerfs = new Set(perfs.map(([k]) => k));
      const others = [];
      Object.entries(meta).forEach(([k, v]) => {
        if (RESERVED.has(k) || v == null || typeof v === 'object') return;
        if (!dims.length  && isDimKey(k))  { dims.push([k, v]);  claimedDims.add(k); return; }
        if (!perfs.length && isPerfKey(k)) { perfs.push([k, v]); claimedPerfs.add(k); return; }
        if (!claimedDims.has(k) && !claimedPerfs.has(k)) others.push([k, v]);
      });

      const cost = findCost(meta);
      // Generic identity (category is shown in the title) — include category too for completeness.
      const fullId = [['Category', meta.category]].concat(idCard).filter(([, v]) => v != null && v !== '');
      const rowHtml = (k, v) => `<div class="prop-row" data-search="${escapeHtml((k + ' ' + v).toLowerCase())}"><span class="k">${escapeHtml(k)}</span><span class="v">${escapeHtml(v)}</span></div>`;
      // E4 — Materials (per-material name + area/volume) + Quantities (area/volume/length/
      // count), rendered only when the exporter supplied them; absent fields just don't show.
      const mats = Array.isArray(meta.materials) ? meta.materials : [];
      const matsHtml = mats.length ? '<div class="prop-section-label">Materials</div>' + mats.map(m => {
        const nm = m.name || m.material || '—';
        const ex = [m.area != null ? fmtNum(m.area) + ' m²' : '', m.volume != null ? fmtNum(m.volume) + ' m³' : ''].filter(Boolean).join(' · ');
        return rowHtml(nm, ex);
      }).join('') : '';
      const qty = [];
      if (meta.area != null)   qty.push(['Area', fmtNum(meta.area) + ' m²']);
      if (meta.volume != null) qty.push(['Volume', fmtNum(meta.volume) + ' m³']);
      if (meta.length != null) qty.push(['Length', fmtNum(meta.length) + ' m']);
      if (meta.quantity != null || meta.count != null) qty.push(['Quantity', String(meta.quantity != null ? meta.quantity : meta.count)]);
      const qtyHtml = qty.length ? '<div class="prop-section-label">Quantities</div>' + qty.map(([k, v]) => rowHtml(k, v)).join('') : '';
      // N6 — Relationships (host / spatial / IFC identity) from whatever scalar
      // relationship fields the exporter supplied; absent fields just don't show.
      const relRows = [
        ['Host', meta.host || meta.hostName],
        ['Host id', meta.hostId || meta.hostGuid],
        ['Assembly', meta.assembly],
        ['Room / space', meta.room || meta.space],
        ['IFC type', meta.ifcType],
        ['IFC GUID', meta.ifcGuid || meta.globalId],
        ['Revit id', meta.revitId || meta.elementId],
      ].filter(([, v]) => v != null && v !== '');
      const relHtml = relRows.length
        ? '<div class="prop-section-label">Relationships</div>' + relRows.map(([k, v]) => rowHtml(k, String(v))).join('')
        : '';
      // N6 — nested groups the flat renderer used to DROP (typeof object → skipped):
      // IFC property sets, classification, type/instance parameter bundles. Rendered
      // generically so ANY exporter shape surfaces — a "group of objects" (e.g. psets)
      // gets a sub-head per member; a flat object becomes a key/value section.
      const GROUP_SKIP = new Set(['dimensions', 'performance']);   // already have dedicated sections
      const n6SubHead = t => '<div style="font-size:11px;font-weight:600;opacity:0.7;margin:6px 0 2px">' + escapeHtml(t) + '</div>';
      const n6Scalars = obj => Object.entries(obj)
        .filter(([, v]) => v != null && typeof v !== 'object')
        .map(([k, v]) => rowHtml(k, String(v))).join('');
      const groupsHtml = Object.entries(meta)
        .filter(([k, v]) => n6IsObj(v) && !GROUP_SKIP.has(k))
        .map(([k, v]) => {
          const entries = Object.entries(v).filter(([, vv]) => vv != null);
          if (!entries.length) return '';
          const hasSubObjs = entries.some(([, vv]) => n6IsObj(vv));
          const inner = hasSubObjs
            ? entries.map(([sk, sv]) => n6IsObj(sv) ? (n6SubHead(sk) + n6Scalars(sv)) : rowHtml(sk, String(sv))).join('')
            : n6Scalars(v);
          return inner ? ('<div class="prop-section-label">' + escapeHtml(n6Humanize(k)) + '</div>' + inner) : '';
        }).filter(Boolean).join('');
      // Item 3 — "no data" hints: when a standard group is ABSENT from the element-map,
      // show a muted hint so it's clear it's a DATA GAP (export from Revit), not a viewer
      // bug. Present fields are already rendered above (Identity / Dimensions / Performance /
      // Properties / Relationships / psets via the generic N6 path).
      const dataHint = (label, what) =>
        '<div class="prop-section-label">' + label + '</div>' +
        '<div class="prop-row" style="opacity:0.5"><span class="v">— ' + escapeHtml(what) + '</span></div>';
      const matsBlock = matsHtml || dataHint('Materials', 'no material data — re-export from Revit with PLANSCAPE_EXPORT_TEXTURES to populate');
      const qtyBlock  = qtyHtml  || dataHint('Quantities', 'no area/volume/quantity — re-export from Revit with quantities to populate');
      const psetBlock = groupsHtml ? '' : dataHint('Property sets', 'no IFC psets / classification — re-export from Revit to populate');
      // P1 — full 8-token ISO 19650 tag. Present tokens show their value; absent ones
      // (commonly LOC/ZONE/SEQ until a re-publish that includes the ASS_* params) show the
      // "re-export" hint. DISC falls back to the derived discipline (disc-safetynet).
      const ISO_TOKENS = [['DISC', 'Discipline'], ['LOC', 'Location'], ['ZONE', 'Zone'], ['LVL', 'Level'], ['SYS', 'System'], ['FUNC', 'Function'], ['PROD', 'Product'], ['SEQ', 'Sequence']];
      const isoVals = ISO_TOKENS.map(([t, label]) => {
        let v = String(tokenValue(meta, t) || '').trim();
        if (t === 'DISC' && !v) v = discOf(meta) || '';
        return [t, label, v];
      });
      const isoAssembled = String(tokenValue(meta, 'TAG') || '').trim() ||
        (isoVals.every(([, , v]) => v) ? isoVals.map(([, , v]) => v).join('-') : '');
      const isoBlock = '<div class="prop-section-label">ISO 19650 Tag</div>' +
        (isoAssembled ? `<div class="prop-row"><span class="v mono">${escapeHtml(isoAssembled)}</span><span class="copy" data-copy="${escapeHtml(isoAssembled)}" title="Copy">📋</span></div>` : '') +
        isoVals.map(([t, label, v]) => v
          ? rowHtml(label + ' (' + t + ')', v)
          : `<div class="prop-row" style="opacity:0.5"><span class="k">${label} (${t})</span><span class="v">— re-export from Revit</span></div>`).join('');
      pane.innerHTML = `
        <div class="prop-section-label">Element</div>
        <div class="prop-title">${escapeHtml(meta.name || meta.category || 'Element')}</div>
        ${tag ? `<div class="prop-section-label">STING Tag</div>
          <div class="prop-row"><span class="v mono">${escapeHtml(tag)}</span>
            <span class="copy" data-copy="${escapeHtml(tag)}" title="Copy">📋</span></div>` : ''}
        <div class="prop-section-label">Cost</div>
        <div class="prop-row" data-search="cost"><span class="k">Estimated cost</span><span class="v" style="${cost ? 'color:#37c272;font-weight:600' : 'opacity:0.6'}" title="${cost ? '' : 'No cost data — set CST_UNIT_RATE_NR on the type, or attach a cost sidecar'}">${escapeHtml(cost ? formatCurrency(cost) : '— no cost data')}</span></div>
        <input id="propFilter" placeholder="Filter properties…" style="width:100%;margin:8px 0 4px;background:#1a1d24;color:#e6e6e6;border:1px solid rgba(255,255,255,0.15);border-radius:4px;padding:5px 8px;font-size:12px" />
        <div id="propRows" style="max-height:42vh;overflow:auto">
          ${fullId.length ? '<div class="prop-section-label">Identity</div>' + fullId.map(([k, v]) => rowHtml(k, v)).join('') : ''}
          ${isoBlock}
          ${matsBlock}
          ${qtyBlock}
          ${dims.length ? '<div class="prop-section-label">Dimensions</div>' + dims.map(([k, v]) => rowHtml(k, v)).join('') : ''}
          ${perfs.length ? '<div class="prop-section-label">Performance</div>' + perfs.map(([k, v]) => rowHtml(k, v)).join('') : ''}
          ${others.length ? '<div class="prop-section-label">Properties</div>' + others.map(([k, v]) => rowHtml(k, v)).join('') : ''}
          ${relHtml}
          ${groupsHtml}
          ${psetBlock}
        </div>
        <div class="prop-section-label">Actions</div>
        <div class="action-grid" style="display:grid;grid-template-columns:1fr 1fr;gap:4px;margin-top:4px">
          <button class="btn sm" id="actIsolate">◎ Isolate</button>
          <button class="btn sm" id="actHide">⊘ Hide</button>
          <button class="btn sm" id="actHideOthers">⊝ Hide others</button>
          <button class="btn sm" id="actShowAll">⊙ Show all</button>
          <button class="btn sm" id="actFit">⌖ Fit</button>
          <button class="btn sm" id="actZoom">🔎 Zoom</button>
          <button class="btn sm" id="actPivot">⊕ Set pivot</button>
          <button class="btn sm" id="actSection">⊟ Section box</button>
          <button class="btn sm" id="actMeasure">📏 Measure</button>
          <button class="btn sm" id="actColourLike">🎨 Colour like</button>
        </div>
        <div class="action-stack" style="margin-top:6px">
          <button class="btn full" id="actCreateIssue">🚩 Create issue</button>
          <button class="btn ghost full" id="actFindClashes">🔍 Find clashes for this</button>
          <button class="btn subtle full" id="actCopyTag">📋 Copy STING tag</button>
          <button class="btn subtle full" id="actLinkSheet">📌 Link to sheet</button>
        </div>
      `;
      // E4 — actions, all composing with the layered model. Each ensures THIS element
      // is the selection so the selection-driven ops act on it.
      const ensureSel = () => { if (!state.selectedElementGuids.has(guid)) { state.selectedElementGuids = new Set([guid]); state.selectedElementGuid = guid; reapplySelection(); } };
      const centreOf = () => { const m = findMeshByGuid(guid); if (!m) return null; return new THREE_.Box3().setFromObject(m).getCenter(new THREE_.Vector3()); };
      $('#actIsolate', pane)?.addEventListener('click', () => { ensureSel(); isolateSelection(); });
      $('#actHide', pane)?.addEventListener('click', () => { ensureSel(); hideSelection(); });
      $('#actHideOthers', pane)?.addEventListener('click', () => { ensureSel(); hideOthers(); });
      $('#actShowAll', pane)?.addEventListener('click', () => showAllElements());
      $('#actFit', pane)?.addEventListener('click', () => { ensureSel(); fitToSelection(); });
      $('#actZoom', pane)?.addEventListener('click', () => { const m = findMeshByGuid(guid); if (m && V.fitCamera) V.fitCamera(new THREE_.Box3().setFromObject(m)); });
      $('#actPivot', pane)?.addEventListener('click', () => { const c = centreOf(); if (c) { V.controls.target.copy(c); V.controls.update(); toast('Orbit pivot set'); } });
      $('#actSection', pane)?.addEventListener('click', () => { ensureSel(); sectionBoxFromSelection(); });
      $('#actMeasure', pane)?.addEventListener('click', () => { const x = window.STING_VIEWER_EXTRAS; const c = centreOf(); if (x && x.startMeasureFrom && c) x.startMeasureFrom(c); else setActiveTool('measure'); });
      $('#actColourLike', pane)?.addEventListener('click', () => {
        const catv = String(tokenValue(meta, 'CAT') || meta.category || '').trim();
        if (!catv) return toast('No category to match', 'warn');
        const matched = new Set(Object.entries(state.elementMap || {}).filter(([, m]) => String((m && (m.category)) || '').trim() === catv).map(([g]) => g));
        colourBySearch(matched); toast(`Coloured ${matched.size} like "${catv}"`);
      });
      $('#actCreateIssue', pane)?.addEventListener('click', () => openIssueModal({ guid, meta }));
      $('#actFindClashes', pane)?.addEventListener('click', () => {
        switchBottomTab('clashes'); state.selectedElementGuid = guid; renderClashes();
      });
      $('#actCopyTag', pane)?.addEventListener('click', () => copyText(tag));
      $('#actLinkSheet', pane)?.addEventListener('click', () => openSheetLinkPicker({ guid, meta }));
      $$('.copy', pane).forEach(c => c.addEventListener('click', () => copyText(c.dataset.copy)));
      // M3 — live property filter (no re-render).
      const pf = $('#propFilter', pane);
      if (pf) pf.addEventListener('input', () => {
        const q = pf.value.trim().toLowerCase();
        $$('#propRows .prop-row', pane).forEach(r => {
          r.style.display = (!q || (r.dataset.search || '').includes(q)) ? '' : 'none';
        });
      });
    }

    // U7 — clipboard.writeText is unavailable in non-secure contexts
    // (file:// inside RN WebView, http:// in older browsers). Fall back
    // to the historic textarea + execCommand("copy") trick so the Copy
    // STING Tag / Share view link buttons keep working there too.
    // ── Inline prompt (replacement for window.prompt) ────────────────
    // window.prompt is blocked or styled inconsistently across browsers
    // (mobile WebKit hides it entirely; Chrome's looks like a 1996 Java
    // applet). This helper renders a small in-app modal that fits the
    // viewer's design language, supports a multi-line textarea + min/max
    // length validation, and resolves with the entered string or null.
    //
    // opts: { title, label?, placeholder?, defaultValue?, multiline?,
    //         minLength?, maxLength?, okLabel?, cancelLabel? }
    // Returns: Promise<string | null>
    function promptInline(opts) {
      const {
        title, label = '', placeholder = '', defaultValue = '',
        multiline = false, minLength = 0, maxLength = 2000,
        okLabel = 'OK', cancelLabel = 'Cancel',
      } = opts || {};
      return new Promise((resolve) => {
        const back = el('div', { class: 'modal-backdrop open inline-prompt-bd' });
        const card = el('div', { class: 'modal inline-prompt' });
        const head = el('div', { class: 'head' }, [
          el('h2', {}, title || 'Enter value'),
          el('button', { class: 'close', title: 'Cancel' }, '✕')
        ]);
        const body = el('div', { class: 'body' });
        if (label) body.appendChild(el('label', {}, label));
        const input = multiline
          ? el('textarea', { rows: '3', placeholder })
          : el('input', { type: 'text', placeholder });
        input.value = defaultValue || '';
        body.appendChild(input);
        const counter = el('div', { class: 'inline-prompt-counter' }, '');
        body.appendChild(counter);
        const foot = el('div', { class: 'foot' }, [
          el('button', { class: 'btn subtle' }, cancelLabel),
          el('button', { class: 'btn' }, okLabel)
        ]);
        card.appendChild(head); card.appendChild(body); card.appendChild(foot);
        back.appendChild(card);
        document.body.appendChild(back);

        const okBtn = $('.btn:not(.subtle)', foot);
        const cancelBtn = $('.btn.subtle', foot);
        const closeBtn = $('.close', head);

        function paintCounter() {
          const len = (input.value || '').trim().length;
          counter.textContent = `${len} / ${maxLength}${minLength > 0 ? ` (min ${minLength})` : ''}`;
          counter.classList.toggle('warn', minLength > 0 && len < minLength);
          okBtn.disabled = (minLength > 0 && len < minLength) || len > maxLength;
        }
        paintCounter();
        input.addEventListener('input', paintCounter);
        // Submit on Enter (single-line) / Ctrl-Enter (multiline).
        input.addEventListener('keydown', (e) => {
          if (e.key === 'Enter' && (!multiline || e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            if (!okBtn.disabled) okBtn.click();
          }
        });

        let done = false;
        function close(value) {
          if (done) return;
          done = true;
          back.remove();
          resolve(value);
        }
        okBtn.addEventListener('click', () => close(input.value));
        cancelBtn.addEventListener('click', () => close(null));
        closeBtn.addEventListener('click', () => close(null));
        back.addEventListener('keydown', (e) => {
          if (e.key === 'Escape') { e.preventDefault(); close(null); }
        });
        back.addEventListener('click', (e) => {
          if (e.target === back) close(null);
        });
        setTimeout(() => input.focus(), 30);
      });
    }

    // Phase 186A — "📌 Link to sheet" picker. The viewer has no element→sheet
    // index of its own, so we filter the project document register down to
    // documentType=SH (ISO 19650 Sheet) optionally narrowed by the element's
    // discipline, and let the user pick. Open in a new tab via the existing
    // download endpoint (which honours the same JWT + tenant headers as the
    // viewer). Pre-auth / file:// hosts get a graceful toast.
    async function openSheetLinkPicker({ guid, meta }) {
      if (!apiEnabled || !projectId) {
        toast('Sign in to a project to link sheets', 'warn');
        return;
      }
      const disc = (meta && (meta.discipline || meta.DISC)) || '';
      const params = new URLSearchParams({ documentType: 'SH', pageSize: '100' });
      if (disc) params.set('discipline', disc);
      let docs = [];
      try {
        const data = await api(`/api/projects/${projectId}/documents?${params.toString()}`);
        docs = (data && Array.isArray(data.items)) ? data.items : [];
      } catch (err) {
        toast('Could not load sheets: ' + (err.message || err), 'error');
        return;
      }
      // Fallback — if discipline filter returned 0, retry without it so the
      // user always sees the sheets available rather than an empty list.
      if (docs.length === 0 && disc) {
        try {
          const data2 = await api(`/api/projects/${projectId}/documents?documentType=SH&pageSize=100`);
          docs = (data2 && Array.isArray(data2.items)) ? data2.items : [];
        } catch (_) { /* swallow */ }
      }
      const back = el('div', { class: 'modal-backdrop open' });
      const card = el('div', { class: 'modal', style: 'max-width:540px;width:90%' });
      const head = el('div', { class: 'head' }, [
        el('h2', {}, '📌 Link to sheet'),
        el('button', { class: 'close', title: 'Close' }, '✕')
      ]);
      const body = el('div', { class: 'body' });
      const tag = (meta && (meta.tag || meta.STING_TAG)) || '';
      body.appendChild(el('div', { class: 'prop-section-label' },
        tag ? `Sheets matching ${disc ? disc + ' · ' : ''}${tag}` : 'Project sheets'));
      const search = el('input', { type: 'text', placeholder: 'Filter by name or number…', style: 'width:100%;margin-bottom:8px' });
      body.appendChild(search);
      const list = el('div', { class: 'sheet-link-list', style: 'max-height:360px;overflow-y:auto;border:1px solid var(--border, #2a3038);border-radius:6px' });
      body.appendChild(list);

      function paint(filter = '') {
        list.innerHTML = '';
        const f = filter.trim().toLowerCase();
        const filtered = docs.filter(d => {
          if (!f) return true;
          return (d.fileName || '').toLowerCase().includes(f) ||
                 (d.description || '').toLowerCase().includes(f) ||
                 (d.revision || '').toLowerCase().includes(f);
        });
        if (filtered.length === 0) {
          list.appendChild(el('div', { style: 'padding:16px;color:var(--muted,#888);text-align:center' },
            docs.length === 0 ? 'No sheets in this project yet.' : 'No matches.'));
          return;
        }
        filtered.forEach(d => {
          const row = el('div', { class: 'sheet-link-row',
            style: 'padding:8px 10px;border-bottom:1px solid var(--border,#2a3038);cursor:pointer;display:flex;justify-content:space-between;gap:8px;align-items:center' });
          const left = el('div', { style: 'flex:1;min-width:0' }, [
            el('div', { style: 'font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis' },
              d.fileName || '(unnamed)'),
            el('div', { style: 'font-size:11px;color:var(--muted,#888);margin-top:2px' },
              [d.discipline, d.revision && `Rev ${d.revision}`, d.cdeStatus].filter(Boolean).join(' · '))
          ]);
          const open = el('button', { class: 'btn subtle', style: 'flex:0 0 auto' }, 'Open');
          row.appendChild(left); row.appendChild(open);
          row.addEventListener('click', () => openSheet(d));
          open.addEventListener('click', (e) => { e.stopPropagation(); openSheet(d); });
          list.appendChild(row);
        });
      }
      function openSheet(d) {
        const url = `${apiBase}/api/projects/${projectId}/documents/${d.id}/download`
                  + (token ? `?access_token=${encodeURIComponent(token)}` : '');
        try { window.open(url, '_blank', 'noopener'); }
        catch (_) { toast('Could not open sheet', 'error'); }
        close();
      }
      search.addEventListener('input', () => paint(search.value));
      paint('');

      const foot = el('div', { class: 'foot' }, [
        el('button', { class: 'btn subtle' }, 'Close')
      ]);
      card.appendChild(head); card.appendChild(body); card.appendChild(foot);
      back.appendChild(card);
      document.body.appendChild(back);
      let done = false;
      function close() { if (done) return; done = true; back.remove(); }
      $('.close', head).addEventListener('click', close);
      $('.btn.subtle', foot).addEventListener('click', close);
      back.addEventListener('click', (e) => { if (e.target === back) close(); });
      setTimeout(() => search.focus(), 30);
    }

    function copyText(t) {
      if (!t) return;
      const okMsg = 'Copied: ' + t;
      const failMsg = 'Copy failed';
      const useExecFallback = () => {
        try {
          const ta = document.createElement('textarea');
          ta.value = t;
          ta.setAttribute('readonly', '');
          ta.style.cssText = 'position:fixed;left:-9999px;top:-9999px;opacity:0';
          document.body.appendChild(ta);
          ta.focus(); ta.select();
          const ok = document.execCommand('copy');
          document.body.removeChild(ta);
          toast(ok ? okMsg : failMsg, ok ? 'success' : 'error');
        } catch (_) { toast(failMsg, 'error'); }
      };
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(t).then(() => toast(okMsg, 'success'), useExecFallback);
      } else {
        useExecFallback();
      }
    }

    // ── Clashes ────────────────────────────────────────────────────────
    async function loadClashes() {
      // U4 — show inline loader while the request is in flight.
      const body = $('#clashesBody');
      if (body) body.innerHTML = '<div class="inline-loader"><span class="dot-spin"></span>Loading clashes…</div>';
      let data = null;
      if (!USE_MOCK_CLASHES && projectId) {
        data = await api(`/api/projects/${projectId}/clashes`);
      }
      state.clashes = (Array.isArray(data) ? data : (data?.items || null)) || mockClashes();
      placeClashPins();
      renderClashes();
      updateBadges();
    }

    function mockClashes() {
      // Synthesise from element map so positions render somewhere visible.
      const guids = Object.keys(state.elementMap || {});
      if (!guids.length) {
        return [
          { id: 'CLH-1', type: 'HARD', elementA: { guid: 'a', name: 'AHU-001' }, elementB: { guid: 'b', name: 'Beam-044' }, overlap_mm: 145, status: 'NEW', discPair: 'MECH/STR' },
          { id: 'CLH-2', type: 'HARD', elementA: { guid: 'c', name: 'Duct-022' }, elementB: { guid: 'd', name: 'Col-018' }, overlap_mm: 88, status: 'NEW', discPair: 'MECH/STR' },
          { id: 'CLH-3', type: 'SOFT', elementA: { guid: 'e', name: 'Pipe-009' }, elementB: { guid: 'f', name: 'Duct-033' }, overlap_mm: 42, status: 'OPEN', discPair: 'PLMB/MECH', assignedTo: 'Sentongo E.' },
          { id: 'CLH-4', type: 'HARD', elementA: { guid: 'g', name: 'AHU-003' }, elementB: { guid: 'h', name: 'Beam-081' }, overlap_mm: 201, status: 'RESOLVED', discPair: 'MECH/STR', assignedTo: 'Sting Davis' }
        ];
      }
      const pick = () => guids[Math.floor(Math.random() * guids.length)];
      const pickPair = () => {
        // R6 — never clash an element with itself; retry up to a bounded
        // number of times before giving up (real models have far more
        // than 2 elements so this almost always succeeds first try).
        let a = pick(), b = pick(), guard = 6;
        while (a === b && guard-- > 0) b = pick();
        return [a, b];
      };
      const out = [];
      for (let i = 1; i <= 12; i++) {
        const [a, b] = pickPair();
        if (a === b) continue;
        const ma = state.elementMap[a] || {}, mb = state.elementMap[b] || {};
        out.push({
          id: `CLH-${String(i).padStart(3, '0')}`,
          type: i % 3 === 0 ? 'SOFT' : 'HARD',
          elementA: { guid: a, name: ma.name || a.slice(0, 8) },
          elementB: { guid: b, name: mb.name || b.slice(0, 8) },
          overlap_mm: Math.round(20 + Math.random() * 200),
          status: i % 6 === 0 ? 'RESOLVED' : (i % 4 === 0 ? 'OPEN' : 'NEW'),
          discPair: `${(ma.discipline || 'MECH').slice(0, 4)}/${(mb.discipline || 'STR').slice(0, 4)}`
        });
      }
      return out;
    }

    function placeClashPins() {
      // R13 — push into the engine's pinGroup + pinMeta so the engine's
      // existing raycaster handles pin clicks (single raycast per click,
      // pinTap event delivered through V.bridge).
      const host = V.pinGroup || V.scene;
      state.clashPins.forEach((m, id) => {
        host.remove(m);
        if (V.pinMeta) V.pinMeta.delete(m.uuid);
      });
      state.clashPins.clear();
      if (!V.modelRoot) return;
      const size = V.modelBounds.isEmpty() ? 1 : V.modelBounds.getSize(new THREE_.Vector3()).length() * 0.012;
      state.clashes.forEach(c => {
        if (c.status === 'RESOLVED') return;
        const pos = clashCentroid(c);
        if (!pos) return;
        const colour = c.type === 'HARD' ? 0xEF4444 : 0xF59E0B;
        const geom = new THREE_.BoxGeometry(size, size, size);
        const edges = new THREE_.EdgesGeometry(geom);
        const mat = new THREE_.LineBasicMaterial({ color: colour, depthTest: false });
        const wire = new THREE_.LineSegments(edges, mat);
        wire.renderOrder = 998;
        wire.position.copy(pos);
        wire.userData.clashId = c.id;
        wire.visible = state.clashMarkersVisible;   // honour the View-menu toggle across rebuilds
        host.add(wire);
        if (V.pinMeta) V.pinMeta.set(wire.uuid, { __coord: 'clash', clashId: c.id });
        state.clashPins.set(c.id, wire);
      });
    }

    function clashCentroid(c) {
      const meshA = findMeshByGuid(c.elementA?.guid);
      const meshB = findMeshByGuid(c.elementB?.guid);
      if (meshA && meshB) {
        const a = new THREE_.Box3().setFromObject(meshA);
        const b = new THREE_.Box3().setFromObject(meshB);
        const u = a.union(b);
        return u.getCenter(new THREE_.Vector3());
      }
      // fallback: random within bounds
      if (V.modelBounds.isEmpty()) return null;
      const c1 = V.modelBounds.getCenter(new THREE_.Vector3());
      return c1;
    }

    // B7 — build a GUID→mesh index once per model load instead of doing
    // a full traverse on every clash / pin / focus call. Reduces the
    // 12-clash × 4000-mesh placement cost from ~96k traversals to ~24
    // hash lookups.
    const guidIndex = new Map();
    // M0 — VERIFIED mesh→meta resolver. Builds three maps in one traverse:
    //   guidIndex      guid → first mesh   (legacy single-mesh lookups)
    //   guidMeshes     guid → mesh[]       (multi-mesh Revit elements)
    //   meshMeta       mesh.uuid → meta    (the appearance/properties hot path)
    // Resolves a mesh's guid from its own userData, then ancestors, then name,
    // accepting only a guid that exists in elementMap. Logs the hit-rate so a
    // bad export (meshes with no resolvable guid) is visible immediately.
    // FEDERATION — co-load every OTHER project model into the same scene so the viz
    // layer spans the whole federation (3×MBALWA + 2×Tendo …). Each model's GLB is
    // added via the engine's addModel (shared recenter → relative positions) and its
    // element-map merged into state.elementMap (guids are globally unique Revit ids).
    // Best-effort + sequential so a slow/missing model can't block the rest.
    async function loadFederatedModels() {
      if (state.federatedLoaded) return;
      state.federatedLoaded = true;
      state.modelVisible.set(modelId, true);
      const others = (state.models || []).filter(m => m && m.id && m.id !== modelId);
      if (!others.length) return;
      const headers = {};
      if (token) headers['Authorization'] = `Bearer ${token}`;
      const tenantId = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_tenant')) || state.tenantId;
      if (tenantId) headers['X-Tenant'] = tenantId;
      // V4 — STREAM the models in ONE AT A TIME: wait for each model's GLTF parse to
      // land before fetching the next, with a short breather so the render loop keeps
      // delivering frames (the primary stays orbitable, siblings pop in). This replaces
      // the old "fire all addModel then poll" which let 5 parses pile onto one frame and
      // crashed FPS to ~0.2. `federationLoading` suppresses the per-change broadcast while
      // streaming. Heavy re-index/re-apply runs ONCE at the end, not per model.
      state.federationLoading = true;
      const hasRoot = (id) => (V.modelRoots || []).some(r => r.userData && r.userData.stingModelId === id);
      const waitForRoot = (id, ms) => new Promise(res => {
        if (hasRoot(id)) return res();
        let t = 0; const iv = setInterval(() => { t += 100; if (hasRoot(id) || t >= ms) { clearInterval(iv); res(); } }, 100);
      });
      const breather = (ms) => new Promise(r => setTimeout(r, ms));
      for (const m of others) {
        try {
          const res = await fetch(`${apiBase}/api/projects/${projectId}/models/${m.id}/file`, { headers, cache: 'no-store' });
          if (!res.ok) continue;
          const blobUrl = URL.createObjectURL(await res.blob());
          let transform = null;
          try { transform = await api(`/api/projects/${projectId}/models/${m.id}/transform`); } catch (_) {}
          try { const map = await api(`/api/projects/${projectId}/models/${m.id}/element-map`); if (map && typeof map === 'object') Object.assign(state.elementMap, map); } catch (_) {}
          handleHostCommand({ type: 'addModel', payload: { url: blobUrl, transform, modelId: m.id } });
          state.modelVisible.set(m.id, true);
          await waitForRoot(m.id, 20000);   // let THIS model's parse finish before the next
          await breather(250);              // yield frames to the render loop
        } catch (e) { console.warn('[fed] model load failed', m.id, e); }
      }
      state.federationLoading = false;
      rebuildGuidIndex();
      applyAppearance();
      renderVisualizePanel();
      if (typeof renderModels === 'function') renderModels();
      console.log(`[fed] STING_VIZ_FEDERATION ${(V.modelRoots || []).length} model roots co-rendered`);
    }

    function rebuildGuidIndex() {
      guidIndex.clear();
      state.meshMeta = new Map();
      state.guidMeshes = new Map();
      if (!V.modelRoot) return;
      const map = state.elementMap || {};
      let total = 0, hit = 0;
      vizGroup().traverse(obj => {
        if (!obj.isMesh) return;
        total++;
        // PART A / A1 — THE single true-original store. Captured ONCE at load,
        // before any appearance/render swap, so the appearance + selection +
        // render-mode layers all restore from the same source and never fight.
        if (!obj.userData._trueOrig) obj.userData._trueOrig = obj.material;
        const ud = obj.userData || {};
        let guid = ud.elementGuid || ud.uniqueId || (ud.extras && ud.extras.uniqueId);
        if (!guid || !map[guid]) {
          // walk ancestors for a guid/uniqueId present in the element map
          let p = obj.parent;
          while (p) {
            const pu = p.userData || {}, pe = pu.extras || {};
            const pg = pu.elementGuid || pu.guid || pu.uniqueId || pe.guid || pe.uniqueId;
            if (pg && map[pg]) { guid = pg; break; }
            p = p.parent;
          }
        }
        if ((!guid || !map[guid]) && obj.name && map[obj.name]) guid = obj.name;   // name fallback
        if (guid) {
          obj.userData.elementGuid = guid;                 // normalise back onto the mesh
          if (!guidIndex.has(guid)) guidIndex.set(guid, obj);
          if (!state.guidMeshes.has(guid)) state.guidMeshes.set(guid, []);
          state.guidMeshes.get(guid).push(obj);
        }
        const meta = guid ? map[guid] : null;
        if (meta) { state.meshMeta.set(obj.uuid, meta); hit++; }
      });
      const pct = total ? Math.round(hit / total * 100) : 0;
      console.log(`[viz] mesh→meta resolver: ${hit}/${total} meshes resolved (${pct}%)`);
      console.log('[viz] STING_VIZ_LAYERED_AB rows+deselect+persist');   // STEP-0 served-artifact marker
      if (total && pct < 50) console.warn('[viz] LOW resolver hit-rate — check the exporter writes per-mesh guids matching the element map');
    }
    // Fast meta lookup for a mesh (resolver map first, then a guid fallback).
    function metaForMesh(o) {
      if (!o) return null;
      const m = state.meshMeta && state.meshMeta.get(o.uuid);
      if (m) return m;
      const g = o.userData && o.userData.elementGuid;
      return (g && state.elementMap) ? (state.elementMap[g] || null) : null;
    }
    // Normalised category key — trimmed, used identically on the UI-build AND
    // lookup sides so a toggle key always equals its lookup key.
    function catKey(meta) { return String(tokenValue(meta, 'CAT') || '').trim(); }
    function findMeshByGuid(guid) {
      if (!guid) return null;
      const cached = guidIndex.get(guid);
      if (cached) return cached;
      // Lazy fallback for the rare case where the index was built before
      // a glTF added meshes (e.g., federation deferred-load).
      if (!V.modelRoot) return null;
      let found = null;
      vizGroup().traverse(obj => {
        if (!found && obj.isMesh && obj.userData.elementGuid === guid) found = obj;
      });
      if (found) guidIndex.set(guid, found);
      return found;
    }

    function renderClashes() {
      // Bottom-panel table
      const body = $('#clashesBody');
      // X1 — combine the two independent axes; "any" means don't filter
      // on that axis. Coordinators can now pick e.g. "Hard New".
      const sf = state.clashStatusFilter, tf = state.clashTypeFilter;
      let rows = state.clashes;
      if (sf !== 'any') rows = rows.filter(c => c.status === sf);
      if (tf !== 'any') rows = rows.filter(c => c.type === tf);

      body.innerHTML = rows.length ? '' : '<div class="empty-state">No clashes match the filter</div>';
      if (rows.length) {
        const table = el('table', { class: 'dtable' });
        table.innerHTML = `<thead><tr>
          <th>#</th><th>Type</th><th>Element A</th><th>Element B</th>
          <th>Disc</th><th>Overlap</th><th>Assigned</th><th>Status</th><th></th>
        </tr></thead><tbody></tbody>`;
        const tbody = $('tbody', table);
        rows.forEach(c => {
          const tr = el('tr', { 'data-id': c.id, 'data-kind': 'clash' });
          tr._clash = c;       // setupRowContextMenu reads this
          tr.innerHTML = `
            <td>${escapeHtml(c.id)}</td>
            <td><span class="tag ${c.type === 'HARD' ? 'hard' : 'soft'}">${c.type}</span></td>
            <td>${escapeHtml(c.elementA?.name || '')}</td>
            <td>${escapeHtml(c.elementB?.name || '')}</td>
            <td>${escapeHtml(c.discPair || '')}</td>
            <td>${c.type === 'HARD' ? c.overlap_mm + 'mm' : c.overlap_mm + 'mm clr'}</td>
            <td>${escapeHtml(c.assignedTo || '—')}</td>
            <td><span class="tag ${c.status.toLowerCase()}">${c.status}</span></td>
            <td><div class="row-actions">
              <button class="btn sm subtle" data-act="view">View</button>
              <button class="btn sm" data-act="issue">→ Issue</button>
            </div></td>
          `;
          tr.addEventListener('click', (e) => {
            if (e.target.closest('button')) return;
            focusClash(c);
          });
          tr.addEventListener('dblclick', (e) => {
            if (e.target.closest('button')) return;
            focusClash(c);                         // focus mode: isolate (ghost rest) + zoom
          });
          // B3 — stopPropagation so clicking "→ Issue" doesn't ALSO fire
          // the row's focusClash and fly the camera away from the modal.
          $('button[data-act=view]', tr).addEventListener('click', (e) => { e.stopPropagation(); focusClash(c); });
          $('button[data-act=issue]', tr).addEventListener('click', (e) => { e.stopPropagation(); openIssueModal({ clash: c }); });
          tbody.appendChild(tr);
        });
        body.appendChild(table);
      }
      // R12 — uniformly total counts on the per-status pills, plus a
      // separate "Showing N of M" indicator that respects both axes.
      const showing = $('#clashesShowing');
      if (showing) showing.textContent = `Showing ${rows.length} of ${state.clashes.length}`;
      $('#clashesTotal').textContent = state.clashes.length;
      $('#clashesNew').textContent = state.clashes.filter(c => c.status === 'NEW').length;
      $('#clashesOpen').textContent = state.clashes.filter(c => c.status === 'OPEN').length;
      $('#clashesResolved').textContent = state.clashes.filter(c => c.status === 'RESOLVED').length;
      renderRightClashes();
      updateRightTabCounts();
    }

    function renderRightClashes() {
      const pane = $('#pane-clashes');
      const guid = state.selectedElementGuid;
      const subset = guid ? state.clashes.filter(c => c.elementA?.guid === guid || c.elementB?.guid === guid) : state.clashes.slice(0, 20);
      if (!subset.length) {
        pane.innerHTML = '<div class="empty-state"><span class="glyph">⚠</span>No clashes</div>';
        return;
      }
      pane.innerHTML = '';
      pane.appendChild(el('div', { class: 'prop-section-label' }, guid ? `Clashes (${subset.length})` : 'Recent clashes'));
      subset.forEach(c => {
        const card = el('div', {
          class: `coord-card ${c.type.toLowerCase()}`,
          'data-kind': 'clash',
          title: 'Click to zoom · Double-click to isolate · Right-click for options',
        });
        card._clash = c;   // delegated context menu in setupRowContextMenu reads this
        card.innerHTML = `
          <div class="head"><span class="tag ${c.type === 'HARD' ? 'hard' : 'soft'}">${c.type}</span>
            <span style="color:var(--text-muted);font-size:11px">${c.overlap_mm}mm ${c.type === 'HARD' ? 'overlap' : 'clearance'}</span></div>
          <div class="body">${escapeHtml(c.elementA?.name)}  ✕  ${escapeHtml(c.elementB?.name)}</div>
          <div class="meta">${escapeHtml(c.discPair || '')} · ${escapeHtml(c.status)}</div>
          <div class="actions">
            <button class="btn sm subtle" data-act="view">View in 3D</button>
            <button class="btn sm" data-act="issue">→ Create issue</button>
          </div>
        `;
        // R5 — the card's CSS sets cursor:pointer; honour that by wiring
        // the body to focusClash. Buttons stop propagation so they keep
        // their distinct actions.
        card.addEventListener('click', (e) => {
          if (e.target.closest('button')) return;
          focusClash(c);
        });
        // Double-click → clash-focus mode (mirrors bottom-panel dblclick).
        card.addEventListener('dblclick', (e) => {
          if (e.target.closest('button')) return;
          focusClash(c);
        });
        $('button[data-act=view]', card).addEventListener('click', (e) => { e.stopPropagation(); focusClash(c); });
        $('button[data-act=issue]', card).addEventListener('click', (e) => { e.stopPropagation(); openIssueModal({ clash: c }); });
        pane.appendChild(card);
      });
    }

    // BUILD 4 — clash-focus mode. Isolate the two clashing elements by GHOSTING
    // everything else (reusing ghostMaterial), colour A red / B orange, auto-zoom
    // to the pair, and optionally drop a section box around them.
    function focusClash(c) {
      state.selectedClashId = c.id;
      clearClashSection();
      if (V.activeOverlaySource && V.clearOverlay) { V.clearOverlay(); state.vizPreset = null; }
      clearAllHighlights();              // L6
      const a = findMeshByGuid(c.elementA?.guid);
      const b = findMeshByGuid(c.elementB?.guid);
      const keep = new Set([a, b].filter(Boolean).map(m => m.uuid));
      if (V.modelRoot && keep.size) {
        vizGroup().traverse(o => {
          if (!o.isMesh) return;
          o.visible = true;
          if (!keep.has(o.uuid)) ghostMaterial(o);   // ghost the context
        });
      }
      // A red, B orange — solid + emissive so the pair pops against the ghost.
      if (a) setReplacement(a, new THREE_.MeshStandardMaterial({ color: 0xEF4444, emissive: 0xEF4444, emissiveIntensity: 0.45 }));
      if (b) setReplacement(b, new THREE_.MeshStandardMaterial({ color: 0xF97316, emissive: 0xF97316, emissiveIntensity: 0.45 }));
      // Auto-zoom to the pair (fall back to a fly-to centroid).
      const box = unionBoxForGuids([c.elementA?.guid, c.elementB?.guid]);
      if (box && V.fitCamera) { try { V.fitCamera(box); } catch (_) { const p = clashCentroid(c); if (p) flyTo(p); } }
      else { const p = clashCentroid(c); if (p) flyTo(p); }
      // Optional section box around the pair.
      if (state.clashSection.onFocus && box) applyClashSection(box);
      logHistory(`Inspected ${c.id}`);
    }

    function emissive(mesh, hex) {
      setReplacement(mesh, new THREE_.MeshStandardMaterial({
        color: hex, emissive: hex, emissiveIntensity: 0.45
      }));
    }

    // L4 — only one flyTo animation may run at a time. Each new fly bumps
    // the token; the previous animation sees the bump and bails on its
    // next frame.
    let flyToken = 0;
    // flyTo a POINT, preserving the current view direction and landing at a
    // FRAMING distance (FOV-aware) — not a fixed offset. `radius` (optional) is the
    // region to frame; default ~8% of the model sphere so a point-only call (photo,
    // issue fallback, minimap) lands at a sensible zoom regardless of current zoom.
    function flyTo(pos, radius) {
      const cam = V.camera;
      let r = radius;
      if (r == null) {
        // STING_VIEWER_FITFIX — radius from box size (Sphere is tree-shaken from the bundle).
        const ms = (V.modelBounds && !V.modelBounds.isEmpty())
          ? 0.5 * V.modelBounds.getSize(new THREE_.Vector3()).length() : 0;
        r = ms > 0 ? ms * 0.08 : V.controls.target.distanceTo(cam.position) * 0.3;
      }
      const vFov = (cam.fov || 50) * Math.PI / 180;
      const hFov = 2 * Math.atan(Math.tan(vFov / 2) * (cam.aspect || 1));
      const dist = (r / Math.sin(Math.min(vFov, hFov) / 2)) * 1.1;
      let dir = new THREE_.Vector3().subVectors(cam.position, V.controls.target);
      if (dir.lengthSq() < 1e-9) dir.set(0, 0.55, 0.85);
      dir.normalize();
      const myToken = ++flyToken;
      const start = cam.position.clone();
      const targetCam = pos.clone().addScaledVector(dir, dist);
      const startTgt = V.controls.target.clone();
      const t0 = performance.now();
      const dur = 600;
      function step() {
        if (myToken !== flyToken) return;        // superseded
        const t = Math.min(1, (performance.now() - t0) / dur);
        const e = 0.5 - 0.5 * Math.cos(t * Math.PI);
        V.camera.position.lerpVectors(start, targetCam, e);
        V.controls.target.lerpVectors(startTgt, pos, e);
        V.controls.update();
        if (t < 1) requestAnimationFrame(step);
      }
      requestAnimationFrame(step);
    }

    // ── Selection-aware helpers (used by toolbar + multi-select pane) ──
    // All operate on `state.selectedElementGuids` so behaviour is the same
    // whether the user grabbed one element by clicking the canvas or 30
    // by ctrl/shift-clicking through the model tree.
    function selectedMeshes() {
      const out = [];
      state.selectedElementGuids.forEach(g => {
        const m = findMeshByGuid(g); if (m) out.push(m);
      });
      return out;
    }

    // Item 2 — Fit/Home frame only the VISIBLE geometry; no-op + toast when nothing
    // is shown (e.g. everything hidden / all models toggled off).
    function fitVisibleOrToast() {
      const vb = (V.visibleModelBounds && V.visibleModelBounds());
      if (vb && !vb.isEmpty()) { if (V.fitCamera) V.fitCamera(); return; }
      toast('Nothing visible to frame', 'warn');
    }
    function fitToSelection() {
      const meshes = selectedMeshes();
      if (!meshes.length) return toast('Nothing selected', 'warn');
      const bb = new THREE_.Box3();
      meshes.forEach(m => bb.expandByObject(m));
      if (bb.isEmpty()) return;
      // Frame the selection bbox edge-to-edge (FOV-aware), preserving view dir.
      if (V.fitCamera) { try { V.fitCamera(bb); return; } catch (_) {} }
      flyTo(bb.getCenter(new THREE_.Vector3()));
    }

    // C2 — selection-driven visibility, routed through the appearance layer so it
    // COMPOSES with colour / ghost / transparency (was a raw o.visible traversal that
    // a subsequent applyAppearance would clobber).
    function isolateSelection() {
      const set = state.selectedElementGuids;
      if (!set.size) return toast('Nothing selected', 'warn');
      state.vizIsolation = { mode: 'isolate', guids: new Set(set) };
      applyAppearance();
      toast(`Isolated ${set.size} element${set.size === 1 ? '' : 's'} (rest ghosted)`);
    }
    function hideSelection() {
      const set = state.selectedElementGuids;
      if (!set.size) return toast('Nothing selected', 'warn');
      state.vizIsolation = { mode: 'hideSel', guids: new Set(set) };
      applyAppearance();
      toast(`Hid ${set.size} element${set.size === 1 ? '' : 's'}`);
    }
    function showAllElements() {
      // Show-all clears selection isolation AND any per-group hide so nothing stays
      // hidden; ghost/colour/transparency are left intact.
      state.vizIsolation = null;
      state.vizDiscMode.forEach((m, k) => { if (m === 'hide') state.vizDiscMode.delete(k); });
      state.vizCatMode.forEach((m, k) => { if (m === 'hide') state.vizCatMode.delete(k); });
      applyAppearance();
      renderVisualizePanel();
      toast('All elements visible');
    }

    function renderSelectionToolbar() {
      const tb = $('#selectionToolbar');
      if (!tb) return;
      const n = state.selectedElementGuids.size;
      if (!n) { tb.style.display = 'none'; return; }
      tb.style.display = 'flex';
      const cnt = $('#selCount');
      if (cnt) cnt.textContent = `${n} selected`;
    }

    // Isolate just the two meshes involved in a clash (used by dblclick
    // on a clash row). Falls back to plain focus when meshes can't be
    // resolved (e.g. element-map response missed them).
    function isolateClashPair(c) {
      if (!V.modelRoot) return;
      const aGuid = c.elementA?.guid;
      const bGuid = c.elementB?.guid;
      const set = new Set([aGuid, bGuid].filter(Boolean));
      if (!set.size) return;
      vizGroup().traverse(o => {
        if (!o.isMesh) return;
        o.visible = set.has(o.userData.elementGuid);
      });
      toast(`Isolated clash pair (${set.size})`);
    }

    // ── Right-click + double-click context menu for clash + issue rows ──
    // Industry references: BIMcollab Zoom + Solibri Office both expose a
    // right-click "Zoom · Isolate · Hide · Mark resolved · Copy ID" menu
    // on coordination rows. We use the same mental model so coordinators
    // moving from those tools find it without training.
    let activeRowMenu = null;
    function setupRowContextMenu() {
      // Re-usable popover element — kept around at the document root so
      // it's not affected by the panel's own resize / scroll containers.
      const menu = el('div', { class: 'row-menu', id: 'rowMenu' });
      document.body.appendChild(menu);

      // Delegate so dynamically-rendered rows (re-render on filter change)
      // don't need their own listeners.
      $('#bottomPanel')?.addEventListener('contextmenu', (e) => {
        const tr = e.target.closest('tr[data-kind]');
        if (!tr) return;
        e.preventDefault();
        const kind = tr.dataset.kind;
        if (kind === 'clash' && tr._clash) openClashRowMenu(menu, tr._clash, e.clientX, e.clientY);
        else if (kind === 'issue' && tr._issue) openIssueRowMenu(menu, tr._issue, e.clientX, e.clientY);
      });
      // Slice 4b — photo cards live in the right rail, not the bottom panel.
      // Mirror the row-menu behaviour for `.photo-card[data-kind=photo]`.
      $('#pane-photos')?.addEventListener('contextmenu', (e) => {
        const card = e.target.closest('.photo-card[data-kind="photo"]');
        if (!card || !card._photo) return;
        e.preventDefault();
        openPhotoRowMenu(menu, card._photo, e.clientX, e.clientY);
      });
      // Right-panel issue cards — same menu as bottom-panel rows.
      $('#pane-issues')?.addEventListener('contextmenu', (e) => {
        const card = e.target.closest('.coord-card[data-kind="issue"]');
        if (!card || !card._issue) return;
        e.preventDefault();
        openIssueRowMenu(menu, card._issue, e.clientX, e.clientY);
      });
      // Right-panel clash cards — same menu as bottom-panel rows.
      $('#pane-clashes')?.addEventListener('contextmenu', (e) => {
        const card = e.target.closest('.coord-card[data-kind="clash"]');
        if (!card || !card._clash) return;
        e.preventDefault();
        openClashRowMenu(menu, card._clash, e.clientX, e.clientY);
      });
      // Click anywhere else to dismiss.
      document.addEventListener('click', (e) => {
        if (activeRowMenu && !e.target.closest('.row-menu')) {
          activeRowMenu.classList.remove('open');
          activeRowMenu = null;
        }
      });
      document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && activeRowMenu) {
          activeRowMenu.classList.remove('open');
          activeRowMenu = null;
        }
      });
    }

    function showRowMenuAt(menu, items, x, y) {
      menu.innerHTML = '';
      items.forEach(it => {
        if (it === '-') {
          menu.appendChild(el('div', { class: 'sep' }));
          return;
        }
        const row = el('div', { class: 'item' + (it.danger ? ' danger' : '') }, [
          el('span', { class: 'glyph' }, it.glyph || ''),
          el('span', {}, it.label),
          it.hot ? el('span', { class: 'hot' }, it.hot) : null
        ]);
        row.addEventListener('click', () => {
          menu.classList.remove('open');
          activeRowMenu = null;
          try { it.run(); } catch (err) { console.warn('[row-menu]', err); }
        });
        menu.appendChild(row);
      });
      // Position with a viewport-edge guard so the menu never clips off-screen.
      const W = window.innerWidth, H = window.innerHeight;
      const w = 220, h = items.length * 32;
      menu.style.left = Math.min(x, W - w - 8) + 'px';
      menu.style.top  = Math.min(y, H - h - 8) + 'px';
      menu.classList.add('open');
      activeRowMenu = menu;
    }

    // ── 3D viewport right-click context menu ──────────────────────────────────
    // Reuses the #rowMenu framework. A right CLICK (movement < 4px) opens the menu;
    // a right DRAG still PANS (OrbitControls RIGHT=PAN) — we only suppress the
    // browser's native menu. Raycasts the element under the cursor.
    function setupCanvasContextMenu() {
      const dom = V && V.renderer && V.renderer.domElement;
      if (!dom) return;
      let menu = document.getElementById('rowMenu');
      if (!menu) { menu = el('div', { class: 'row-menu', id: 'rowMenu' }); document.body.appendChild(menu); }
      let rcDown = null;
      dom.addEventListener('pointerdown', (e) => { if (e.button === 2) rcDown = { x: e.clientX, y: e.clientY }; });
      dom.addEventListener('contextmenu', (e) => {
        e.preventDefault();                  // always suppress the browser menu
        const moved = rcDown ? Math.hypot(e.clientX - rcDown.x, e.clientY - rcDown.y) : 0;
        rcDown = null;
        if (moved > 4) return;               // that was a right-drag PAN
        openCanvasContextMenu(menu, e.clientX, e.clientY);
      });
    }

    function openCanvasContextMenu(menu, x, y) {
      const dom = V.renderer.domElement;
      const r = dom.getBoundingClientRect();
      const ptr = new THREE_.Vector2(((x - r.left) / r.width) * 2 - 1, -((y - r.top) / r.height) * 2 + 1);
      const ray = new THREE_.Raycaster();
      ray.setFromCamera(ptr, V.camera);
      const hits = V.modelRoot ? pickableHits(ray.intersectObject(vizGroup(), true)) : [];   // V1 — skip ghost/x-ray
      if (hits.length && hits[0].object && hits[0].object.isMesh) {
        const mesh = hits[0].object;
        const guid = mesh.userData && mesh.userData.elementGuid;
        const bb = new THREE_.Box3().setFromObject(mesh);
        const centre = bb.getCenter(new THREE_.Vector3());
        // Select the right-clicked element (unless it's already in the selection)
        // so Isolate / Hide / Properties / Create issue act on it.
        if (guid && !state.selectedElementGuids.has(guid)) selectElementByGuid(guid, 'replace');
        const meta = guid ? (state.elementMap[guid] || {}) : {};
        const tag = meta.tag || meta.STING_TAG || meta.tag1 || '';
        showRowMenuAt(menu, [
          { glyph: '◎', label: 'Isolate',              run: () => isolateSelection() },
          { glyph: '⊘', label: 'Hide',                 run: () => hideSelection() },
          { glyph: '⊝', label: 'Hide others',          run: () => hideOthers() },
          { glyph: '⊙', label: 'Show all',             run: () => showAllElements() },
          '-',
          { glyph: '⌖', label: 'Fit element',          run: () => { if (V.fitCamera) V.fitCamera(bb); } },
          { glyph: '⊕', label: 'Set pivot here',       run: () => { V.controls.target.copy(centre); V.controls.update(); toast('Orbit pivot set'); } },
          { glyph: '⊟', label: 'Section box from selection', run: () => sectionBoxFromSelection() },
          '-',
          { glyph: 'ℹ', label: 'Properties',           run: () => { $('.tab-bar .tab[data-tab=properties]')?.click(); renderProperties(state.selectedElementGuid); } },
          { glyph: '🚩', label: 'Create issue',         run: () => openIssueModal({ guid, meta }) },
          tag ? { glyph: '🏷', label: 'Copy STING tag', run: () => { copyToClipboard(String(tag)); toast('Tag copied'); } } : null,
          '-',
          { glyph: '✕', label: 'Deselect',             run: () => selectElementByGuid(null) },   // B2
        ].filter(Boolean), x, y);
      } else {
        showRowMenuAt(menu, [
          { glyph: '✕', label: 'Deselect / clear selection', run: () => selectElementByGuid(null) },  // B2
          { glyph: '⊙', label: 'Show all',  run: () => showAllElements() },
          { glyph: '⌖', label: 'Fit model', run: () => { if (V.fitCamera) V.fitCamera(); } },
          '-',
          { glyph: '◳', label: 'Exit markup / section', run: () => setActiveTool('orbit') },
        ], x, y);
      }
    }

    // ── ViewCube / orientation gizmo + perspective↔ortho toggle (Commit 5) ──
    function setupViewCube() {
      const cube = $('#viewCube'); if (!cube) return;
      cube.querySelectorAll('.vc-btn[data-snap]').forEach(b => {
        b.addEventListener('click', () => {
          const v = b.dataset.snap;
          if (v === 'home') { if (V.fitCamera) V.fitCamera(); return; }
          if (v === 'eye') { const x = window.STING_VIEWER_EXTRAS; if (x && x.humanEyeView) { x.humanEyeView(); toast('Human eye view'); } return; }
          if (V.snapView) V.snapView(v);
        });
      });
      const ortho = $('#orthoToggle');
      if (ortho) {
        ortho.addEventListener('click', () => {
          const toOrtho = !(V.isOrtho);
          if (V.setCameraMode) V.setCameraMode(toOrtho ? 'ortho' : 'persp');
          ortho.textContent = (V.isOrtho ? 'Ortho' : 'Persp');
          ortho.classList.toggle('active', !!V.isOrtho);
          toast(V.isOrtho ? 'Orthographic projection' : 'Perspective projection');
        });
      }
    }

    // ── Collapsible panels ("Xpand") — edge handles + localStorage persistence ──
    const PANEL_KEY = 'planscape_panels';
    function savePanelState() {
      const shell = document.querySelector('.app-shell'); if (!shell) return;
      try {
        const rs = document.documentElement.style;
        localStorage.setItem(PANEL_KEY, JSON.stringify({
          l: shell.classList.contains('left-collapsed'),
          r: shell.classList.contains('right-collapsed'),
          b: !!$('#bottomPanel')?.classList.contains('collapsed'),
          lw: rs.getPropertyValue('--panel-left-width').trim() || undefined,   // V2 — persist width
          rw: rs.getPropertyValue('--panel-right-width').trim() || undefined,
        }));
      } catch (e) {}
    }
    function updatePanelHandles() {
      const shell = document.querySelector('.app-shell'); if (!shell) return;
      const lh = $('#railHandleLeft'), rh = $('#railHandleRight');
      if (lh) lh.textContent = shell.classList.contains('left-collapsed') ? '›' : '‹';
      if (rh) rh.textContent = shell.classList.contains('right-collapsed') ? '‹' : '›';
    }
    function setupPanelHandles() {
      const shell = document.querySelector('.app-shell');
      const wrap = $('.viewport-wrap');
      if (!shell || !wrap) return;
      // Restore persisted collapse state + widths on load.
      try {
        const s = JSON.parse(localStorage.getItem(PANEL_KEY) || '{}');
        if (s.lw) document.documentElement.style.setProperty('--panel-left-width', s.lw);    // V2
        if (s.rw) document.documentElement.style.setProperty('--panel-right-width', s.rw);
        if (s.l) shell.classList.add('left-collapsed');
        if (s.r) shell.classList.add('right-collapsed');
        if (s.b) $('#bottomPanel')?.classList.add('collapsed');
        if (s.l || s.r || s.b || s.lw || s.rw) onResize();
      } catch (e) {}
      // V2 — each rail handle: DRAG to resize the panel's width live (clamped), CLICK (no
      // drag) to collapse/expand. Width persists; the canvas + camera resize live via the
      // ortho-aware sizeRenderer so 3D framing stays correct as the viewport reclaims space.
      const mk = (id, cls, side) => {
        const varName = side === 'left' ? '--panel-left-width' : '--panel-right-width';
        const h = el('div', { id, class: 'rail-handle rail-' + side, title: 'Drag to resize · click to collapse/expand' });
        let down = null, moved = false;
        h.addEventListener('pointerdown', (e) => {
          down = { x: e.clientX }; moved = false;
          try { h.setPointerCapture(e.pointerId); } catch (_) {}
          shell.classList.add('resizing');
          e.preventDefault();
        });
        h.addEventListener('pointermove', (e) => {
          if (!down) return;
          if (!moved && Math.abs(e.clientX - down.x) > 3) moved = true;
          if (!moved) return;
          let w = (side === 'left') ? e.clientX : (window.innerWidth - e.clientX);
          w = Math.max(180, Math.min(560, w));
          shell.classList.remove(cls);                 // a resize implies expanded
          document.documentElement.style.setProperty(varName, w + 'px');
          if (V.sizeRenderer) V.sizeRenderer();         // live canvas + camera
        });
        const end = (e) => {
          if (!down) return;
          try { h.releasePointerCapture(e.pointerId); } catch (_) {}
          shell.classList.remove('resizing');
          if (!moved) { shell.classList.toggle(cls); onResize(); }   // click → collapse toggle
          down = null;
          savePanelState(); updatePanelHandles();
        };
        h.addEventListener('pointerup', end);
        h.addEventListener('pointercancel', end);
        wrap.appendChild(h);
        return h;
      };
      mk('railHandleLeft',  'left-collapsed',  'left');
      mk('railHandleRight', 'right-collapsed', 'right');
      updatePanelHandles();
    }

    // Hide every mesh that is NOT in the current selection (C2 — through the layer).
    function hideOthers() {
      const keep = state.selectedElementGuids;
      if (!keep.size) return toast('Nothing selected', 'warn');
      state.vizIsolation = { mode: 'hideOthers', guids: new Set(keep) };
      applyAppearance();
      toast(`Hid all but ${keep.size}`);
    }

    // Section box around the current selection's bbox (no-op clip until Commit 4
    // wires setSectionBox in viewer-extras; the menu item is then fully live).
    function sectionBoxFromSelection() {
      const meshes = selectedMeshes();
      if (!meshes.length) return toast('Select an element first', 'warn');
      const bb = new THREE_.Box3();
      meshes.forEach(m => bb.expandByObject(m));
      if (bb.isEmpty()) return;
      // Pad slightly so the selection isn't flush with the cut.
      const pad = bb.getSize(new THREE_.Vector3()).length() * 0.06 || 0.5;
      bb.min.addScalar(-pad); bb.max.addScalar(pad);
      setActiveTool('section');
      const x = window.STING_VIEWER_EXTRAS;
      if (x && x.setSectionBox) {
        x.setSectionBox({ min: [bb.min.x, bb.min.y, bb.min.z], max: [bb.max.x, bb.max.y, bb.max.z], enabled: true });
        if (x.onSectionChange) x.onSectionChange(syncSectionBoxSliders);
      }
      $('#sectionCard').style.display = 'block';
      renderSectionBoxPanel();
      toast('Section box from selection');
    }

    function openClashRowMenu(menu, c, x, y) {
      const aGuid = c.elementA?.guid, bGuid = c.elementB?.guid;
      showRowMenuAt(menu, [
        { glyph: '🎯', label: 'Zoom to clash',     run: () => focusClash(c) },
        { glyph: '◎',  label: 'Isolate pair',      run: () => isolateClashPair(c) },
        { glyph: '⊘',  label: 'Hide both',         run: () => {
            if (!V.modelRoot) return;
            vizGroup().traverse(o => { if (o.isMesh && (o.userData.elementGuid === aGuid || o.userData.elementGuid === bGuid)) o.visible = false; });
            toast('Hid clash pair');
        }},
        { glyph: '⊙',  label: 'Show all',          run: () => showAllElements() },
        '-',
        { glyph: '🚩', label: 'Create issue from clash', run: () => openIssueModal({ clash: c }) },
        { glyph: '✓',  label: 'Mark resolved',     run: () => {
            c.status = 'RESOLVED';
            renderClashes(); placeClashPins();
            toast(`${c.id} marked resolved`, 'success');
        }},
        '-',
        { glyph: '📋', label: 'Copy clash ID',     run: () => copyText(c.id) },
        { glyph: '📤', label: 'Copy element pair', run: () => copyText(`${c.elementA?.name || ''}  ✕  ${c.elementB?.name || ''}`) },
      ], x, y);
    }

    function openIssueRowMenu(menu, i, x, y) {
      const isResolved = i.status === 'RESOLVED' || i.status === 'CLOSED';
      showRowMenuAt(menu, [
        { glyph: '🎯', label: 'Zoom to issue',         run: () => focusIssue(i) },
        { glyph: '◎',  label: 'Isolate linked elements', run: () => {
            if (!V.modelRoot || !Array.isArray(i.elementGuids)) return;
            const set = new Set(i.elementGuids);
            vizGroup().traverse(o => {
              if (!o.isMesh) return;
              o.visible = set.has(o.userData.elementGuid);
            });
            toast(`Isolated ${set.size} linked element${set.size === 1 ? '' : 's'}`);
        }},
        { glyph: '⊙',  label: 'Show all',              run: () => showAllElements() },
        '-',
        { glyph: '💬', label: 'Open comments',         run: () => {
            focusIssue(i);
            const tab = $$('.tab-bar .tab').find(t => t.dataset.tab === 'comments');
            if (tab) tab.click();
        }},
        { glyph: '🕓', label: 'View activity',         run: () => {
            focusIssue(i);
            const tab = $$('.tab-bar .tab').find(t => t.dataset.tab === 'activity');
            if (tab) tab.click();
        }},
        '-',
        ...(isResolved ? [
          { glyph: '↺', label: 'Re-open issue',        run: () => updateIssue(i.id, { status: 'OPEN' }) },
        ] : [
          { glyph: '▶', label: 'Mark in-progress',     run: () => updateIssue(i.id, { status: 'IN_PROGRESS' }) },
          { glyph: '✓', label: 'Mark resolved',        run: () => updateIssue(i.id, { status: 'RESOLVED' }) },
          { glyph: '🔒', label: 'Close issue',          run: () => updateIssue(i.id, { status: 'CLOSED' }) },
        ]),
        '-',
        { glyph: '📋', label: 'Copy issue ID',         run: () => copyText(i.code || i.id) },
        { glyph: '🔗', label: 'Copy permalink',         run: () => {
            const url = `${location.origin}${location.pathname}?project=${projectId}&model=${modelId}&issue=${i.id}`;
            copyText(url);
        }},
      ], x, y);
    }

    function openPhotoRowMenu(menu, p, x, y) {
      const reviewer = isPhotoApprover();
      const items = [
        { glyph: '🎯', label: 'Zoom to photo', run: () => focusPhoto(p) },
      ];
      if (p.anchorElementGuid) {
        items.push({ glyph: '◎', label: 'Zoom to anchored element', run: () => {
          const m = findMeshByGuid(p.anchorElementGuid);
          if (m) {
            const bb = new THREE_.Box3().setFromObject(m);
            flyTo(bb.getCenter(new THREE_.Vector3()));
            emissive(m, 0xF97316);
          } else {
            toast('Anchored element not in current model', 'warn');
          }
        }});
      }
      items.push('-');
      items.push({ glyph: '✏', label: 'Edit caption', run: async () => {
        const cap = await promptInline({
          title: 'Edit caption',
          label: 'What does this photo show?',
          placeholder: 'e.g. Riser sleeves cast on Level 02',
          defaultValue: p.caption || '',
          multiline: true, minLength: 0, maxLength: 2000,
          okLabel: 'Save caption',
        });
        if (cap == null) return;
        // No dedicated patch endpoint yet; an approve(caption) on a
        // PendingReview photo doubles as caption-set. For other audiences
        // we surface a notice — the canonical edit path lives on the
        // server slice 4a (not yet wired into this viewer slice).
        if (p.audience === 'PendingReview') {
          if (cap.trim().length < 3) { toast('Caption ≥ 3 chars to approve', 'warn'); return; }
          const r = await approveSitePhoto(p.id, cap.trim());
          if (r) { Object.assign(p, r); renderPhotos(); }
        } else {
          toast('Caption editing for non-pending photos lands in slice 5', 'warn');
        }
      }});
      if (reviewer) {
        items.push('-');
        if (p.audience === 'PendingReview' || p.audience === 'Internal') {
          items.push({ glyph: '✓', label: 'Approve', run: async () => {
            let cap = p.caption || '';
            if (cap.trim().length < 3) {
              cap = await promptInline({
                title: 'Approve photo',
                label: 'Approval caption (visible to client)',
                placeholder: 'Describe what the client should see',
                defaultValue: cap,
                multiline: true, minLength: 3, maxLength: 2000,
                okLabel: 'Approve & publish',
              });
            }
            if (!cap || cap.trim().length < 3) return;
            const r = await approveSitePhoto(p.id, cap);
            if (r) { Object.assign(p, r); renderPhotos(); }
          }});
        }
        if (p.audience === 'PendingReview' || p.audience === 'Approved') {
          items.push({ glyph: '✗', label: 'Reject', run: async () => {
            const reason = await promptInline({
              title: 'Reject photo',
              label: 'Reason (shown to the capturer)',
              placeholder: 'e.g. off-topic / poor quality / privacy',
              defaultValue: '',
              multiline: true, minLength: 0, maxLength: 500,
              okLabel: 'Reject',
            });
            if (reason === null) return;
            const r = await rejectSitePhoto(p.id, reason);
            if (r) { Object.assign(p, r); renderPhotos(); }
          }});
        }
        if (p.audience === 'ClientPortal') {
          items.push({ glyph: '↶', label: 'Withdraw from portal', run: async () => {
            if (!confirm('Withdraw this photo from the client portal?')) return;
            const r = await withdrawSitePhoto(p.id);
            if (r) { Object.assign(p, r); renderPhotos(); }
          }});
        }
      }
      items.push('-');
      items.push({ glyph: '📋', label: 'Copy photo ID', run: () => copyText(p.id) });
      items.push({ glyph: '🔗', label: 'Copy permalink', run: () => {
        const url = `${location.origin}${location.pathname}?project=${projectId}&model=${modelId}&photo=${p.id}`;
        copyText(url);
      }});
      showRowMenuAt(menu, items, x, y);
    }

    function setupSelectionToolbar() {
      const tb = $('#selectionToolbar');
      if (!tb) return;
      $('#selFit', tb)?.addEventListener('click', () => fitToSelection());
      $('#selIsolate', tb)?.addEventListener('click', () => isolateSelection());
      $('#selHide', tb)?.addEventListener('click', () => hideSelection());
      $('#selShowAll', tb)?.addEventListener('click', () => showAllElements());
      $('#selGhost', tb)?.addEventListener('click', () => {
        state.ghostMode = !state.ghostMode;
        setRenderMode(state.ghostMode ? 'ghost' : 'shaded');
      });
      $('#selIssue', tb)?.addEventListener('click', () => {
        const guids = [...state.selectedElementGuids];
        const primary = state.selectedElementGuid || guids[0];
        openIssueModal({
          guid: primary,
          meta: state.elementMap[primary] || {},
          multi: guids
        });
      });
      $('#selClose', tb)?.addEventListener('click', () => selectElementByGuid(null));
    }

    // ── Issues ─────────────────────────────────────────────────────────
    async function loadIssues() {
      if (!projectId) { state.issues = []; renderIssues(); return; }
      // U4 — show inline loader while the request is in flight.
      const body = $('#issuesBody');
      if (body) body.innerHTML = '<div class="inline-loader"><span class="dot-spin"></span>Loading issues…</div>';
      const data = await api(`/api/projects/${projectId}/issues`);
      state.issues = Array.isArray(data) ? data : (data?.items || []);
      placeIssuePins();
      renderIssues();
      updateBadges();
    }

    function placeIssuePins() {
      // R13 — same pinGroup + pinMeta path as placeClashPins.
      const host = V.pinGroup || V.scene;
      state.issuePins.forEach(m => {
        host.remove(m);
        if (V.pinMeta) V.pinMeta.delete(m.uuid);
      });
      state.issuePins.clear();
      const size = V.modelBounds.isEmpty() ? 0.4 : V.modelBounds.getSize(new THREE_.Vector3()).length() * 0.008;
      const PRIORITY = { CRITICAL: 0xEF4444, HIGH: 0xF97316, MEDIUM: 0xF59E0B, LOW: 0x60A5FA, RESOLVED: 0x22C55E };
      state.issues.forEach(i => {
        if (!i.position) return;
        const colour = i.status === 'RESOLVED' ? PRIORITY.RESOLVED : (PRIORITY[i.priority] || PRIORITY.MEDIUM);
        const sphere = new THREE_.Mesh(
          new THREE_.SphereGeometry(size, 18, 18),
          new THREE_.MeshStandardMaterial({ color: colour, emissive: colour, emissiveIntensity: 0.6, depthTest: false })
        );
        sphere.position.set(i.position.x, i.position.y, i.position.z);
        sphere.userData.issueId = i.id;
        sphere.renderOrder = 999;
        sphere.visible = state.issueMarkersVisible;   // honour the View-menu toggle across rebuilds
        host.add(sphere);
        if (V.pinMeta) V.pinMeta.set(sphere.uuid, { __coord: 'issue', issueId: i.id, priority: i.priority });
        state.issuePins.set(i.id, sphere);
      });
    }

    // T3-18 — Bulk multi-selection set for the issues grid. Mirrors the
    // 3D viewer's selectedElementGuids set: ctrl/cmd-click toggles, shift-
    // click extends a contiguous range, plain click clears + selects one.
    // Lives outside renderIssues so the set survives re-renders triggered
    // by filter changes; we prune ids that no longer exist on each render.
    state.bulkIssueIds = state.bulkIssueIds || new Set();
    state.bulkLastIssueId = state.bulkLastIssueId || null;

    function renderIssues() {
      const body = $('#issuesBody');
      let rows = state.issues;
      // L2 — match against the real signed-in user id, with 'me' as fallback
      // for offline / pre-auth state so the placeholder demo data still works.
      const myId = state.currentUser?.id || state.currentUser?.userId || 'me';
      if (state.issuesFilter === 'mine') rows = rows.filter(i => i.assigneeId === myId || i.assigneeId === 'me');
      else if (state.issuesFilter === 'overdue') rows = rows.filter(i => i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED'));

      // Prune stale ids before re-rendering — happens when an issue is
      // resolved+filtered out under "Mine" / "Overdue" but the bulk set
      // still references it.
      const visibleIds = new Set(rows.map(r => r.id));
      for (const id of [...state.bulkIssueIds]) if (!visibleIds.has(id)) state.bulkIssueIds.delete(id);

      body.innerHTML = rows.length ? '' : '<div class="empty-state">No issues</div>';
      if (rows.length) {
        const table = el('table', { class: 'dtable' });
        table.innerHTML = `<thead><tr>
          <th style="width:24px"><input type="checkbox" class="bulk-check" id="bulkCheckAll" title="Select all visible"></th>
          <th>ID</th><th>Title</th><th>Priority</th><th>Assignee</th>
          <th>Due</th><th>Status</th><th>SLA</th>
        </tr></thead><tbody></tbody>`;
        const tbody = $('tbody', table);
        rows.forEach(i => {
          const tr = el('tr', { 'data-id': i.id, 'data-kind': 'issue' });
          tr._issue = i;       // setupRowContextMenu reads this
          const priority = i.priority || 'MEDIUM';
          const overdue = i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED');
          const checked = state.bulkIssueIds.has(i.id) ? 'checked' : '';
          if (checked) tr.classList.add('row-selected');
          tr.innerHTML = `
            <td><input type="checkbox" class="bulk-check" data-row-check="${i.id}" ${checked}></td>
            <td>${escapeHtml(i.code || i.id?.slice(0, 8))}</td>
            <td>${escapeHtml(i.title || '')}</td>
            <td><span class="tag ${priority}">${priority}</span></td>
            <td>${escapeHtml(i.assigneeName || '—')}</td>
            <td>${i.dueDate ? new Date(i.dueDate).toLocaleDateString() : '—'}</td>
            <td><span class="tag ${(i.status || 'NEW').toLowerCase()}">${i.status || 'NEW'}</span></td>
            <td>${overdue ? '<span class="tag overdue">OVERDUE</span>' : '—'}</td>
          `;

          // Checkbox cell — toggle without bubbling to focusIssue. Shift-
          // click on the checkbox extends the range from bulkLastIssueId.
          const cb = tr.querySelector('input.bulk-check[data-row-check]');
          cb.addEventListener('click', (ev) => {
            ev.stopPropagation();
            if (ev.shiftKey && state.bulkLastIssueId) toggleIssueRange(rows, state.bulkLastIssueId, i.id, true);
            else toggleIssueBulk(i.id, cb.checked);
            state.bulkLastIssueId = i.id;
            renderBulkFooter();
            // Reflect row selection class without a full re-render.
            tr.classList.toggle('row-selected', state.bulkIssueIds.has(i.id));
          });

          tr.addEventListener('click', (e) => {
            // T3-18 — replicate the viewer's multi-select gesture set on
            // the row body itself so power users don't have to aim at the
            // 14-pixel checkbox.
            if (e.target.closest('input.bulk-check')) return;
            if (e.metaKey || e.ctrlKey) {
              toggleIssueBulk(i.id, !state.bulkIssueIds.has(i.id));
              state.bulkLastIssueId = i.id;
              renderIssues();
              return;
            }
            if (e.shiftKey && state.bulkLastIssueId) {
              toggleIssueRange(rows, state.bulkLastIssueId, i.id, true);
              state.bulkLastIssueId = i.id;
              renderIssues();
              return;
            }
            focusIssue(i);
          });
          tr.addEventListener('dblclick', () => {
            focusIssue(i);
            // Also pop the right-rail comments tab so the user can
            // start replying immediately.
            const tab = $$('.tab-bar .tab').find(t => t.dataset.tab === 'comments');
            if (tab) tab.click();
          });
          tbody.appendChild(tr);
        });
        body.appendChild(table);

        // Header checkbox — toggles every visible row in/out of the set.
        const headerCb = $('#bulkCheckAll', body);
        if (headerCb) {
          headerCb.checked = rows.every(r => state.bulkIssueIds.has(r.id)) && rows.length > 0;
          headerCb.addEventListener('change', () => {
            if (headerCb.checked) rows.forEach(r => state.bulkIssueIds.add(r.id));
            else rows.forEach(r => state.bulkIssueIds.delete(r.id));
            renderIssues();
          });
        }

        renderBulkFooter();
      } else {
        // No rows; ensure footer is wiped.
        renderBulkFooter();
      }
      $('#issuesTotal').textContent = state.issues.length;
      // R1 — match the same myId logic the filter uses, otherwise the
      // "Mine" badge always shows 0 in production where assigneeId is a
      // real UUID, never the placeholder "me".
      $('#issuesMine').textContent = state.issues.filter(i => i.assigneeId === myId || i.assigneeId === 'me').length;
      $('#issuesOverdue').textContent = state.issues.filter(i => i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED')).length;
      renderRightIssues();
      updateRightTabCounts();
    }

    // T3-18 — toggle a single id in/out of the bulk set.
    function toggleIssueBulk(id, on) {
      if (on) state.bulkIssueIds.add(id); else state.bulkIssueIds.delete(id);
    }
    // Range-select between two visible row ids, mirroring the viewer's
    // shift-click extend behaviour. `add` controls add-or-replace.
    function toggleIssueRange(rows, fromId, toId, add) {
      const ids = rows.map(r => r.id);
      const a = ids.indexOf(fromId), b = ids.indexOf(toId);
      if (a < 0 || b < 0) { state.bulkIssueIds.add(toId); return; }
      const lo = Math.min(a, b), hi = Math.max(a, b);
      if (!add) state.bulkIssueIds.clear();
      for (let k = lo; k <= hi; k++) state.bulkIssueIds.add(ids[k]);
    }
    /** Sticky bulk footer with N selected · Reassign · Bulk Resolve · Export CSV. */
    function renderBulkFooter() {
      const host = $('#issuesBody'); if (!host) return;
      let footer = host.querySelector('.bulk-footer');
      const n = state.bulkIssueIds.size;
      if (n === 0) { if (footer) footer.remove(); return; }
      if (!footer) {
        footer = el('div', { class: 'bulk-footer' });
        host.appendChild(footer);
      }
      footer.innerHTML = '';
      footer.appendChild(el('span', { class: 'count' }, `${n} selected`));
      footer.appendChild(el('span', { class: 'grow' }));
      const btnReassign = el('button', { class: 'btn sm subtle' }, 'Reassign');
      btnReassign.addEventListener('click', () => bulkReassign());
      const btnResolve  = el('button', { class: 'btn sm' }, 'Bulk Resolve');
      btnResolve.addEventListener('click', () => bulkResolve());
      const btnExport   = el('button', { class: 'btn sm subtle' }, 'Export CSV');
      btnExport.addEventListener('click', () => bulkExportCsv());
      const btnClear    = el('button', { class: 'btn sm subtle' }, 'Clear');
      btnClear.addEventListener('click', () => { state.bulkIssueIds.clear(); renderIssues(); });
      footer.appendChild(btnReassign);
      footer.appendChild(btnResolve);
      footer.appendChild(btnExport);
      footer.appendChild(btnClear);
    }
    /** T3-18 — bulk resolve. Calls updateIssue per row but caps concurrency
     *  at 10 to spare the server's audit-log writers (each PUT triggers a
     *  SignalR broadcast and an AuditLog INSERT). */
    async function bulkResolve() {
      const ids = [...state.bulkIssueIds];
      if (!ids.length) return;
      if (!confirm(`Mark ${ids.length} issue${ids.length === 1 ? '' : 's'} as RESOLVED?`)) return;
      const CAP = 10;
      let done = 0, failed = 0;
      for (let i = 0; i < ids.length; i += CAP) {
        const chunk = ids.slice(i, i + CAP);
        await Promise.all(chunk.map(async (id) => {
          try {
            await api(`/api/projects/${projectId}/issues/${id}`, {
              method: 'PUT', body: JSON.stringify({ status: 'RESOLVED' })
            });
            const idx = state.issues.findIndex(x => x.id === id);
            if (idx >= 0) state.issues[idx].status = 'RESOLVED';
            done++;
          } catch (_) { failed++; }
        }));
      }
      state.bulkIssueIds.clear();
      renderIssues(); placeIssuePins?.(); updateBadges?.();
      toast(`Resolved ${done}${failed ? ` · ${failed} failed` : ''}`, failed ? 'warn' : 'success');
    }
    async function bulkReassign() {
      const ids = [...state.bulkIssueIds];
      if (!ids.length) return;
      const who = prompt(`Reassign ${ids.length} issue(s) to (display name or email):`);
      if (!who) return;
      const CAP = 10;
      let done = 0, failed = 0;
      for (let i = 0; i < ids.length; i += CAP) {
        const chunk = ids.slice(i, i + CAP);
        await Promise.all(chunk.map(async (id) => {
          try {
            await api(`/api/projects/${projectId}/issues/${id}`, {
              method: 'PUT', body: JSON.stringify({ assignee: who })
            });
            const idx = state.issues.findIndex(x => x.id === id);
            if (idx >= 0) state.issues[idx].assigneeName = who;
            done++;
          } catch (_) { failed++; }
        }));
      }
      state.bulkIssueIds.clear();
      renderIssues();
      toast(`Reassigned ${done}${failed ? ` · ${failed} failed` : ''}`, failed ? 'warn' : 'success');
    }
    function bulkExportCsv() {
      const ids = [...state.bulkIssueIds];
      const rows = state.issues.filter(i => ids.includes(i.id));
      if (!rows.length) return;
      const cols = ['code', 'title', 'priority', 'status', 'assigneeName', 'dueDate', 'createdAt'];
      const csv  = [cols.join(',')].concat(rows.map(r => cols.map(c => csvCell(r[c])).join(','))).join('\n');
      const blob = new Blob([csv], { type: 'text/csv' });
      const url  = URL.createObjectURL(blob);
      const a    = el('a', { href: url, download: `issues-${new Date().toISOString().slice(0, 10)}.csv` });
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 5000);
    }
    function csvCell(v) {
      if (v == null) return '';
      const s = String(v).replace(/"/g, '""');
      return /[",\n]/.test(s) ? `"${s}"` : s;
    }

    function renderRightIssues() {
      const pane = $('#pane-issues');
      const guid = state.selectedElementGuid;
      const subset = guid
        ? state.issues.filter(i => Array.isArray(i.elementGuids) && i.elementGuids.includes(guid))
        : state.issues.slice(0, 12);
      if (!subset.length) {
        pane.innerHTML = '<div class="empty-state"><span class="glyph">🚩</span>No issues</div>';
        return;
      }
      pane.innerHTML = '';
      pane.appendChild(el('div', { class: 'prop-section-label' }, guid ? `Linked issues (${subset.length})` : 'Recent issues'));
      subset.forEach(i => {
        const overdue = i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED');
        const card = el('div', {
          class: `coord-card priority-${i.priority || 'MEDIUM'} ${i.status === 'RESOLVED' ? 'resolved' : ''}`,
          'data-kind': 'issue',
          title: 'Click to zoom · Double-click to open comments · Right-click for options',
        });
        card._issue = i;   // delegated context menu in setupRowContextMenu reads this
        card.innerHTML = `
          <div class="head">
            <span class="tag ${(i.status || 'NEW').toLowerCase()}">${i.status || 'NEW'}</span>
            <span class="tag ${i.priority || 'MEDIUM'}">${i.priority || 'MED'}</span>
            <span style="margin-left:auto;color:var(--text-muted);font-size:11px">${escapeHtml(i.code || '')}</span>
          </div>
          <div class="body">${escapeHtml(i.title || 'Untitled')}</div>
          <div class="meta">Assigned: ${escapeHtml(i.assigneeName || '—')}${i.dueDate ? ' · Due ' + new Date(i.dueDate).toLocaleDateString() : ''}${overdue ? ' · OVERDUE' : ''}</div>
          ${i.description ? `<div class="meta" style="margin-top:6px">${escapeHtml(String(i.description).slice(0, 200))}</div>` : ''}
          <div class="actions">
            <button class="btn sm subtle" data-act="view">View</button>
            ${i.status !== 'RESOLVED' ? '<button class="btn sm" data-act="resolve">Resolve</button>' : ''}
          </div>
        `;
        // R5 — same treatment as the clash cards: card body focuses the
        // issue, buttons keep their explicit actions.
        card.addEventListener('click', (e) => {
          if (e.target.closest('button')) return;
          focusIssue(i);
        });
        // Double-click → zoom + open comments (mirrors bottom-panel dblclick).
        card.addEventListener('dblclick', (e) => {
          if (e.target.closest('button')) return;
          focusIssue(i);
          const tab = $$('.tab-bar .tab').find(t => t.dataset.tab === 'comments');
          if (tab) tab.click();
        });
        $('button[data-act=view]', card)?.addEventListener('click', (e) => { e.stopPropagation(); focusIssue(i); });
        $('button[data-act=resolve]', card)?.addEventListener('click', (e) => { e.stopPropagation(); updateIssue(i.id, { status: 'RESOLVED' }); });
        pane.appendChild(card);
      });
    }

    function focusIssue(i) {
      state.selectedIssueId = i.id;
      clearAllHighlights();              // L6
      // Frame the issue's LINKED element(s) — build their bbox and fitCamera it
      // (mirrors focusClash). Only fall back to a point fly-to when the issue has
      // a bare GPS/model point and no resolvable element.
      const bb = new THREE_.Box3();
      let any = false;
      if (Array.isArray(i.elementGuids)) {
        i.elementGuids.forEach(g => {
          const m = findMeshByGuid(g);
          if (m) { emissive(m, 0xF97316); bb.expandByObject(m); any = true; }
        });
      }
      if (any && !bb.isEmpty() && V.fitCamera) {
        try { V.fitCamera(bb); }
        catch (_) { if (i.position) flyTo(new THREE_.Vector3(i.position.x, i.position.y, i.position.z)); }
      } else if (i.position) {
        flyTo(new THREE_.Vector3(i.position.x, i.position.y, i.position.z));
      }
      // switch right panel
      const tab = $('.tab-bar .tab[data-tab=issues]'); tab?.click();
      // U2 — if Comments tab is active, refresh thread for the new issue.
      if (state.rightTab === 'comments') renderComments();
      updateRightTabCounts();            // X2
      logHistory(`Inspected ${i.code || i.id}`);
    }

    async function updateIssue(id, patch) {
      const res = await api(`/api/projects/${projectId}/issues/${id}`, {
        method: 'PUT', body: JSON.stringify(patch)
      });
      if (res || res === '') {
        const idx = state.issues.findIndex(i => i.id === id);
        if (idx >= 0) state.issues[idx] = Object.assign({}, state.issues[idx], patch);
        renderIssues(); placeIssuePins(); updateBadges();
        toast('Issue updated', 'success');
      } else {
        toast('Update failed', 'error');
      }
    }

    // ── Issue creation modal ───────────────────────────────────────────
    // Pending attachments collected before submit so the user can stage
    // photos / PDFs / drawings without uploading until the issue exists.
    let pendingIssueAttachments = [];
    function openIssueModal(seed = {}) {
      const modal = $('#issueModal');
      modal.classList.add('open');
      $('#imTitle').value = '';
      $('#imDesc').value  = '';
      const initialEl = $('#imInitialComment'); if (initialEl) initialEl.value = '';
      $('#imScreenshot').innerHTML = '';
      $('#imScreenshot').dataset.b64 = '';
      pendingIssueAttachments = [];
      const attachListEl = $('#imAttachList'); if (attachListEl) attachListEl.innerHTML = '';
      // priority + type + status defaults
      $$('#imPriority .choice').forEach(c => c.classList.toggle('active', c.dataset.v === 'HIGH'));
      $$('#imType .choice').forEach(c => c.classList.toggle('active', c.dataset.v === 'RFI'));
      $$('#imStatus .choice').forEach(c => c.classList.toggle('active', c.dataset.v === 'OPEN'));
      const discEl = $('#imDiscipline'); if (discEl) discEl.value = seed.discipline || '';
      // linked elements
      const link = $('#imLinked'); link.innerHTML = '';
      const linked = [];
      if (seed.guid) {
        linked.push({ guid: seed.guid, name: seed.meta?.name || seed.meta?.tag || seed.guid.slice(0, 8) });
      }
      if (seed.clash) {
        const a = seed.clash.elementA, b = seed.clash.elementB;
        if (a) linked.push({ guid: a.guid, name: a.name });
        if (b) linked.push({ guid: b.guid, name: b.name });
        $('#imTitle').value = `${seed.clash.discPair} clash — ${a?.name} vs ${b?.name}`;
        // For clash issues, pre-select CLASH as the type and pre-fill
        // discipline from the dominant side of the pair (e.g. "M-S" → M).
        $$('#imType .choice').forEach(c => c.classList.toggle('active', c.dataset.v === 'CLASH'));
        if (discEl && seed.clash.discPair) discEl.value = (seed.clash.discPair.split('-')[0] || '').toUpperCase();
      }
      modal.dataset.linked = JSON.stringify(linked);
      renderLinkedElements(linked);

      // assignee + watcher pickers — populated from project members API
      // (see loadProjectMembers in bootstrap), with the demo-seed members
      // as fallback so first-time / offline runs aren't empty.
      const assigneeSel = $('#imAssignee');
      assigneeSel.innerHTML = '<option value="">— Unassigned —</option>' +
        state.members.map(m => `<option value="${m.id}">${escapeHtml(m.name)}${m.role ? ' · ' + escapeHtml(m.role) : ''}</option>`).join('');

      const watchSel = $('#imWatchersSelect');
      if (watchSel) {
        watchSel.innerHTML = '<option value="">— Add a watcher —</option>' +
          state.members.map(m => `<option value="${m.id}">${escapeHtml(m.name)}${m.role ? ' · ' + escapeHtml(m.role) : ''}</option>`).join('');
      }
      modal.dataset.watchers = '[]';
      const chips = $('#imWatcherChips'); if (chips) chips.innerHTML = '';

      // B12 — focus the title field so the user can start typing
      // immediately, and remember the previously focused element so we
      // can restore it on close.
      modal.dataset.open = '1';
      modal.dataset.returnFocus = '';
      const prev = document.activeElement;
      if (prev && prev !== document.body) modal.dataset.returnFocusId = prev.id || '';
      setTimeout(() => $('#imTitle')?.focus(), 30);
    }

    function renderWatcherChips() {
      const chips = $('#imWatcherChips');
      if (!chips) return;
      const ids = JSON.parse($('#issueModal').dataset.watchers || '[]');
      chips.innerHTML = '';
      ids.forEach((id, i) => {
        const m = state.members.find(x => x.id === id);
        if (!m) return;
        const chip = el('span', { class: 'watcher-chip' }, [
          el('span', { class: 'initials' }, m.initials || ''),
          el('span', { class: 'name' }, ' ' + m.name + ' '),
          el('span', { class: 'x', title: 'Remove' }, '✕')
        ]);
        $('.x', chip).addEventListener('click', () => {
          const arr = ids.slice(); arr.splice(i, 1);
          $('#issueModal').dataset.watchers = JSON.stringify(arr);
          renderWatcherChips();
        });
        chips.appendChild(chip);
      });
    }

    function renderAttachmentList() {
      const list = $('#imAttachList');
      if (!list) return;
      list.innerHTML = '';
      pendingIssueAttachments.forEach((f, i) => {
        const row = el('div', { class: 'attachment-row' }, [
          el('span', { class: 'name' }, f.name),
          el('span', { class: 'size' }, formatBytes(f.size)),
          el('span', { class: 'x', title: 'Remove' }, '✕')
        ]);
        $('.x', row).addEventListener('click', () => {
          pendingIssueAttachments.splice(i, 1);
          renderAttachmentList();
        });
        list.appendChild(row);
      });
    }

    function formatBytes(n) {
      if (!n) return '0 B';
      const u = ['B','KB','MB','GB'];
      let i = 0; let v = n;
      while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
      return v.toFixed(v < 10 && i > 0 ? 1 : 0) + ' ' + u[i];
    }

    function renderLinkedElements(arr) {
      const link = $('#imLinked'); link.innerHTML = '';
      arr.forEach((it, i) => {
        const row = el('div', { class: 'linked-element' }, [
          el('span', { class: 'dot' }),
          el('span', {}, `${it.name} (${it.guid.slice(0, 8)})`),
          el('span', { class: 'x', title: 'Remove' }, '✕')
        ]);
        $('.x', row).addEventListener('click', () => {
          arr.splice(i, 1);
          $('#issueModal').dataset.linked = JSON.stringify(arr);
          renderLinkedElements(arr);
        });
        link.appendChild(row);
      });
    }

    function setupModalHandlers() {
      const modal = $('#issueModal');
      const closeModal = () => { modal.classList.remove('open'); modal.dataset.open = ''; };
      $('#imClose').addEventListener('click', closeModal);
      $('#imCancel').addEventListener('click', closeModal);
      modal.addEventListener('click', (e) => {
        if (e.target.id === 'issueModal') closeModal();
      });
      // B12 — focus trap + Esc close. The global Esc handler intentionally
      // skips when the modal is open so dismissing the modal doesn't also
      // wipe the user's selection / highlights.
      modal.addEventListener('keydown', (e) => {
        if (!modal.classList.contains('open')) return;
        if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); closeModal(); return; }
        if (e.key !== 'Tab') return;
        const focusables = $$('input, textarea, select, button, [tabindex]:not([tabindex="-1"])', modal)
          .filter(el => !el.disabled && el.offsetParent !== null);
        if (!focusables.length) return;
        const first = focusables[0], last = focusables[focusables.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      });
      $$('#imPriority .choice').forEach(c => c.addEventListener('click', () => {
        $$('#imPriority .choice').forEach(x => x.classList.remove('active'));
        c.classList.add('active');
      }));
      $$('#imType .choice').forEach(c => c.addEventListener('click', () => {
        $$('#imType .choice').forEach(x => x.classList.remove('active'));
        c.classList.add('active');
      }));
      $$('#imStatus .choice').forEach(c => c.addEventListener('click', () => {
        $$('#imStatus .choice').forEach(x => x.classList.remove('active'));
        c.classList.add('active');
      }));
      const watchSel = $('#imWatchersSelect');
      if (watchSel) {
        watchSel.addEventListener('change', () => {
          const id = watchSel.value;
          watchSel.value = '';
          if (!id) return;
          const ids = JSON.parse(modal.dataset.watchers || '[]');
          if (ids.includes(id)) return;
          ids.push(id);
          modal.dataset.watchers = JSON.stringify(ids);
          renderWatcherChips();
        });
      }
      const attachBtn = $('#imAttachBtn');
      const attachInput = $('#imAttachInput');
      const dropZone = $('#imAttachDrop');
      // Centralise the "stage these files for upload" loop so click-to-browse
      // and drag-drop hit the exact same path (validation, oversize toast,
      // empty-input guard).
      function stageFiles(files) {
        if (!files) return;
        for (const f of files) {
          if (f.size > 50 * 1024 * 1024) {
            toast(`${f.name} exceeds 50 MB — skipped`, 'warn');
            continue;
          }
          pendingIssueAttachments.push(f);
        }
        renderAttachmentList();
      }
      if (attachBtn && attachInput) {
        attachBtn.addEventListener('click', (ev) => {
          ev.stopPropagation();           // don't double-fire via dropzone
          attachInput.click();
        });
        attachInput.addEventListener('change', () => {
          stageFiles(attachInput.files);
          attachInput.value = '';        // allow same file to be re-picked
        });
      }
      // Drag-and-drop dropzone — matches mobile expo-image-picker ergonomics
      // for desktop users dragging photos out of Finder / Explorer / Slack.
      if (dropZone && attachInput) {
        dropZone.addEventListener('click', () => attachInput.click());
        dropZone.addEventListener('keydown', (ev) => {
          if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); attachInput.click(); }
        });
        ['dragenter', 'dragover'].forEach(evt => {
          dropZone.addEventListener(evt, (ev) => {
            ev.preventDefault(); ev.stopPropagation();
            dropZone.classList.add('dragover');
          });
        });
        ['dragleave', 'drop'].forEach(evt => {
          dropZone.addEventListener(evt, (ev) => {
            ev.preventDefault(); ev.stopPropagation();
            dropZone.classList.remove('dragover');
          });
        });
        dropZone.addEventListener('drop', (ev) => {
          const files = ev.dataTransfer?.files;
          stageFiles(files);
        });
        // Suppress browser default of navigating to the dropped file when
        // the user misses the drop zone — common with folder drags.
        ['dragover', 'drop'].forEach(evt => {
          modal.addEventListener(evt, (ev) => { ev.preventDefault(); });
        });
      }
      $('#imScreenshotBtn').addEventListener('click', () => {
        // B14 — downscale to <= 1280px wide JPEG before posting. A raw 4K
        // PNG can hit 8-12 MB base64; the issues endpoint and Postgres
        // bytea column don't enjoy that. JPEG q=0.85 keeps screenshots
        // legible while staying well under 500 KB.
        const b64 = downscaleScreenshot(V.renderer.domElement, 1280, 0.85);
        const wrap = $('#imScreenshot');
        wrap.innerHTML = `<img src="${b64}" alt="screenshot"/>`;
        wrap.dataset.b64 = b64;
      });
      $('#imSubmit').addEventListener('click', submitIssue);
    }

    async function submitIssue() {
      const modal = $('#issueModal');
      const linked = JSON.parse(modal.dataset.linked || '[]');
      const watcherIds = JSON.parse(modal.dataset.watchers || '[]');
      const priority = $('#imPriority .choice.active')?.dataset.v || 'MEDIUM';
      const type     = $('#imType .choice.active')?.dataset.v || 'RFI';
      const status   = $('#imStatus .choice.active')?.dataset.v || 'OPEN';
      const discipline = $('#imDiscipline')?.value || null;
      const initialComment = ($('#imInitialComment')?.value || '').trim();
      const payload = {
        title: $('#imTitle').value.trim(),
        priority, type, status, discipline,
        elementGuids: linked.map(l => l.guid),
        // Match server CreateIssueRequest field names where possible so the
        // payload is forward-compatible with the typed DTO. The server
        // accepts both legacy `linkedElementIds` (string) and the camelCase
        // form below; viewer prefers explicit GUID list for clarity.
        linkedElementIds: linked.length ? JSON.stringify(linked.map(l => l.guid)) : null,
        assigneeUserId: $('#imAssignee').value || null,
        watcherUserIds: watcherIds,
        dueDate: $('#imDue').value || null,
        description: $('#imDesc').value,
        screenshotBase64: $('#imScreenshot').dataset.b64 || null,
        source: 'web-viewer',
        // 3D anchor — stamps where in the model the issue was raised so
        // pins re-render on next load.
        position: lastClickPoint ? { x: lastClickPoint.x, y: lastClickPoint.y, z: lastClickPoint.z } : undefined,
        modelId: state.modelId || modelId || null,
        modelElementGuid: linked[0]?.guid || null,
        modelX: lastClickPoint?.x ?? null,
        modelY: lastClickPoint?.y ?? null,
        modelZ: lastClickPoint?.z ?? null,
      };
      if (!payload.title) return toast('Title required', 'warn');

      let result;
      if (projectId) {
        result = await api(`/api/projects/${projectId}/issues`, {
          method: 'POST', body: JSON.stringify(payload)
        });
      }
      const created = result || Object.assign({
        id: 'local-' + Date.now(),
        code: 'ISS-LOCAL-' + (state.issues.length + 1),
        status: status,
        slaBreached: false
      }, payload);

      // Upload any attachments + post the initial comment now the issue
      // exists. Both are best-effort — failures don't unwind the issue.
      if (projectId && created.id && !String(created.id).startsWith('local-')) {
        for (const f of pendingIssueAttachments) {
          try {
            const fd = new FormData();
            fd.append('file', f, f.name);
            const resp = await fetch(`${apiBase}/api/projects/${projectId}/issues/${created.id}/attachments`, {
              method: 'POST',
              headers: token ? { 'Authorization': 'Bearer ' + token } : {},
              body: fd
            });
            if (!resp.ok) toast(`Attachment ${f.name} failed (${resp.status})`, 'warn');
          } catch (e) {
            console.warn('[coord] attachment upload failed', f.name, e);
          }
        }
        if (initialComment) {
          await api(`/api/projects/${projectId}/issues/${created.id}/comments`, {
            method: 'POST',
            body: JSON.stringify({ body: initialComment, source: 'web-viewer' })
          });
        }
      }
      pendingIssueAttachments = [];

      state.issues.unshift(created);
      placeIssuePins(); renderIssues(); updateBadges();
      modal.classList.remove('open');
      toast(`Issue ${created.code || created.id} created`, 'success');
      logHistory(`Created ${created.code || 'issue'}`);
    }

    // ── Site photos (Slice 4b) ─────────────────────────────────────────
    // Six-Reason taxonomy from server: Progress / Issue / Defect / Safety /
    // AsBuilt / Reference. Photos can be filtered by reason + audience and
    // optionally restricted to the currently-selected element. PM/Admin/Owner
    // get the bulk-approve reviewer pane.
    const PHOTO_REASONS = ['Progress','Issue','Defect','Safety','AsBuilt','Reference'];
    const PHOTO_AUDIENCES = ['Internal','PendingReview','Approved','ClientPortal','Withdrawn'];
    const PHOTO_PIN_COLOUR = 0xFBBF24;       // gold (matches design lock)
    const PHOTO_BLOB_CACHE = new Map();      // photoId → object URL (revoked on cleanup)

    // (state.photos / photoFilters / photoPins / photoCaptureSeed /
    // photoReviewSelected initialised inside the main `state` object above
    // so updateRightTabCounts can read them on the first paint before this
    // block has run.)
    let pendingPhotoFile = null;             // staged file in capture modal
    let pendingPhotoObjectUrl = null;        // preview url to revoke on close
    let pcStream = null;                     // active getUserMedia webcam stream
    function stopWebcam() {
      if (pcStream) { try { pcStream.getTracks().forEach(t => t.stop()); } catch (_) {} pcStream = null; }
    }

    // ── API helpers — match the existing loadIssues / loadClashes pattern ──
    async function loadSitePhotos(filters = {}) {
      if (!projectId) { state.photos = []; renderPhotos(); return state.photos; }
      const qs = new URLSearchParams();
      const merged = Object.assign({}, state.photoFilters, filters);
      if (merged.reason && merged.reason !== 'any') qs.set('reason', merged.reason);
      if (merged.audience && merged.audience !== 'any') qs.set('audience', merged.audience);
      if (state.selectedElementGuid) qs.set('anchorElementGuid', state.selectedElementGuid);
      qs.set('pageSize', '200');
      const path = `/api/projects/${projectId}/photos${qs.toString() ? '?' + qs.toString() : ''}`;
      const data = await api(path);
      const items = Array.isArray(data) ? data : (data?.items || []);
      state.photos = items;
      placePhotoPins();
      if (state.rightTab === 'photos') renderPhotos();
      updateRightTabCounts();
      return items;
    }

    async function captureSitePhoto(formData) {
      // Multipart POST — DO NOT set Content-Type; the browser fills the
      // multipart boundary correctly when omitted. We bypass api() because
      // it stamps application/json on every call.
      if (!projectId) { toast('No active project — cannot capture', 'error'); return null; }
      try {
        const headers = {};
        if (token) headers['Authorization'] = 'Bearer ' + token;
        const tenantId = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_tenant')) || state.tenantId;
        if (tenantId) headers['X-Tenant'] = tenantId;
        const ctl = new AbortController();
        const timer = setTimeout(() => ctl.abort(), 60000);  // 60s — phone-camera JPEG can be 5-15 MB
        const res = await fetch(`${apiBase}/api/projects/${projectId}/photos/capture`, {
          method: 'POST', headers, body: formData, signal: ctl.signal
        });
        clearTimeout(timer);
        if (!res.ok) {
          let msg = `${res.status} ${res.statusText}`;
          try { const err = await res.json(); if (err?.error) msg = err.error + (err.allowed ? ` (allowed: ${err.allowed.join(', ')})` : ''); } catch (_) {}
          toast(`Capture failed — ${msg}`, 'error');
          return null;
        }
        return await res.json();
      } catch (e) {
        const aborted = e && e.name === 'AbortError';
        toast(aborted ? 'Capture timed out — retry?' : 'Capture failed — ' + (e.message || e), 'error');
        return null;
      }
    }

    async function approveSitePhoto(id, caption) {
      const r = await api(`/api/projects/${projectId}/photos/${id}/approve`, {
        method: 'POST', body: JSON.stringify({ caption: caption || null })
      });
      if (r) toast('Photo approved', 'success');
      return r;
    }
    async function rejectSitePhoto(id, reason) {
      const r = await api(`/api/projects/${projectId}/photos/${id}/reject`, {
        method: 'POST', body: JSON.stringify({ reason: reason || null })
      });
      if (r) toast('Photo rejected', 'success');
      return r;
    }
    async function withdrawSitePhoto(id) {
      const r = await api(`/api/projects/${projectId}/photos/${id}/withdraw`, {
        method: 'POST', body: JSON.stringify({})
      });
      if (r) toast('Photo withdrawn', 'success');
      return r;
    }
    async function bulkApproveSitePhotos(ids, caption) {
      if (!ids || !ids.length) return null;
      if (!caption || caption.trim().length < 3) {
        toast('Approval caption must be ≥ 3 chars', 'warn');
        return null;
      }
      const r = await api(`/api/projects/${projectId}/photos/bulk-approve`, {
        method: 'POST', body: JSON.stringify({ photoIds: ids, caption: caption.trim() })
      });
      if (r) toast(`Approved ${ids.length} photo${ids.length === 1 ? '' : 's'}`, 'success');
      return r;
    }

    // Approver gate mirrors server: Admin / Owner / PM (project role).
    function isPhotoApprover() {
      const u = state.currentUser || {};
      const role = (u.role || u.Role || '').toString();
      if (role === 'Admin' || role === 'Owner') return true;
      // ProjectMember role check — populated by loadProjectMembers().
      const myId = u.id || u.userId;
      if (!myId) return false;
      const me = state.members.find(m => m.id === myId);
      return !!me && (me.role === 'PM' || me.role === 'pm');
    }

    // ── 3D pin handling — mirrors placeIssuePins / placeClashPins ─────
    function placePhotoPins() {
      const host = V.pinGroup || V.scene;
      state.photoPins.forEach(m => {
        host.remove(m);
        if (V.pinMeta) V.pinMeta.delete(m.uuid);
      });
      state.photoPins.clear();
      if (!V.modelBounds || V.modelBounds.isEmpty()) return;
      const size = V.modelBounds.getSize(new THREE_.Vector3()).length() * 0.0072;
      state.photos.forEach(p => {
        if (p.modelX == null || p.modelY == null || p.modelZ == null) return;
        const sphere = new THREE_.Mesh(
          new THREE_.SphereGeometry(size, 16, 16),
          new THREE_.MeshStandardMaterial({
            color: PHOTO_PIN_COLOUR, emissive: PHOTO_PIN_COLOUR,
            emissiveIntensity: 0.55, depthTest: false
          })
        );
        sphere.position.set(p.modelX, p.modelY, p.modelZ);
        sphere.userData.photoId = p.id;
        sphere.renderOrder = 999;
        host.add(sphere);
        if (V.pinMeta) V.pinMeta.set(sphere.uuid, { __coord: 'photo', photoId: p.id, reason: p.reason });
        state.photoPins.set(p.id, sphere);
      });
    }

    function focusPhoto(p) {
      if (!p) return;
      // Switch to Photos tab + scroll the matching card into view.
      const tab = $$('.tab-bar .tab').find(t => t.dataset.tab === 'photos');
      if (tab) tab.click();
      if (p.modelX != null && p.modelY != null && p.modelZ != null) {
        flyTo(new THREE_.Vector3(p.modelX, p.modelY, p.modelZ));
      } else if (p.anchorElementGuid) {
        const m = findMeshByGuid(p.anchorElementGuid);
        if (m) {
          const bb = new THREE_.Box3().setFromObject(m);
          flyTo(bb.getCenter(new THREE_.Vector3()));
        }
      }
      setTimeout(() => {
        const card = $(`.photo-card[data-id="${p.id}"]`);
        if (card) {
          card.scrollIntoView({ block: 'center', behavior: 'smooth' });
          card.classList.add('focused');
          setTimeout(() => card.classList.remove('focused'), 1600);
        }
      }, 50);
      logHistory(`Inspected photo · ${p.reason || ''}`);
    }

    // Build a thumbnail src — fetches the protected file once via api()
    // (Authorization header), turns it into a blob URL, caches it. The
    // alternative (token in query string) was rejected per B1.
    function ensurePhotoThumbSrc(photoId, imgEl) {
      if (!imgEl) return;
      const cached = PHOTO_BLOB_CACHE.get(photoId);
      if (cached) { imgEl.src = cached; return; }
      // Lazy-load on first paint so a 50-photo gallery doesn't fetch
      // every original up-front.
      const fetcher = async () => {
        try {
          const headers = {};
          if (token) headers['Authorization'] = 'Bearer ' + token;
          const tenantId = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_tenant')) || state.tenantId;
          if (tenantId) headers['X-Tenant'] = tenantId;
          const res = await fetch(`${apiBase}/api/projects/${projectId}/photos/${photoId}/file`, { headers });
          if (!res.ok) throw new Error(`${res.status}`);
          const blob = await res.blob();
          const url = URL.createObjectURL(blob);
          PHOTO_BLOB_CACHE.set(photoId, url);
          imgEl.src = url;
        } catch (e) {
          imgEl.style.display = 'none';
          const ph = imgEl.nextElementSibling;
          if (ph && ph.classList?.contains('thumb-fallback')) ph.style.display = 'flex';
        }
      };
      // IntersectionObserver — only kick the fetch when the thumb scrolls
      // into view. Falls back to immediate fetch if IO is unavailable.
      if (typeof IntersectionObserver === 'function') {
        const io = new IntersectionObserver((entries) => {
          if (entries.some(e => e.isIntersecting)) {
            io.disconnect();
            fetcher();
          }
        });
        io.observe(imgEl);
      } else {
        fetcher();
      }
    }

    // ── Photos right-rail tab ─────────────────────────────────────────
    function renderPhotos() {
      const pane = $('#pane-photos');
      if (!pane) return;
      const filterEl = state.selectedElementGuid;
      const elementMeta = filterEl ? (state.elementMap[filterEl] || {}) : null;
      const pendingCount = state.photos.filter(p => p.audience === 'PendingReview').length;
      const reviewerVisible = isPhotoApprover();
      const totalLabel = filterEl ? `${state.photos.length} for selected element` : `${state.photos.length} in project`;

      pane.innerHTML = `
        <div class="prop-section-label">Site photos</div>
        ${filterEl ? `<div class="photo-filter-context">
          Filtered to <strong>${escapeHtml(elementMeta?.name || elementMeta?.tag || filterEl.slice(0, 8))}</strong>
          <button class="btn ghost xs" id="photoClearAnchor" title="Show all project photos">✕ Clear</button>
        </div>` : ''}
        <div class="photo-toolbar">
          <span class="hint">${escapeHtml(totalLabel)}</span>
          <div class="right">
            <button class="btn sm subtle" id="photoRefresh" title="Refresh from server">↻</button>
            <button class="btn sm" id="photoQuickCapture">📷 Capture</button>
          </div>
        </div>
        ${reviewerVisible ? `<button class="btn ghost full review-bar" id="photoOpenReview">
          🛡 Review queue <span class="count">${pendingCount}</span>
        </button>` : ''}
        <div class="photo-filters">
          <div class="row">
            <span class="lbl">Reason</span>
            <button class="filter-btn ${state.photoFilters.reason === 'any' ? 'active' : ''}" data-reason="any">Any</button>
            ${PHOTO_REASONS.map(r => `<button class="filter-btn reason-chip rc-${r.toLowerCase()} ${state.photoFilters.reason === r ? 'active' : ''}" data-reason="${r}">${escapeHtml(r)}</button>`).join('')}
          </div>
          <div class="row">
            <span class="lbl">Audience</span>
            <select class="filter-select" id="photoAudienceFilter">
              <option value="any" ${state.photoFilters.audience === 'any' ? 'selected' : ''}>Any audience</option>
              ${PHOTO_AUDIENCES.map(a => `<option value="${a}" ${state.photoFilters.audience === a ? 'selected' : ''}>${escapeHtml(a)}</option>`).join('')}
            </select>
          </div>
        </div>
        <div class="photo-list" id="photoList">
          ${state.photos.length === 0
            ? '<div class="empty-state"><span class="glyph">📷</span>No site photos match these filters</div>'
            : state.photos.map(p => renderPhotoCard(p, reviewerVisible)).join('')}
        </div>
      `;

      // Wire top toolbar
      $('#photoRefresh', pane)?.addEventListener('click', () => loadSitePhotos());
      $('#photoQuickCapture', pane)?.addEventListener('click', () => openPhotoCaptureModal());
      $('#photoOpenReview', pane)?.addEventListener('click', () => openPhotoReviewModal());
      $('#photoClearAnchor', pane)?.addEventListener('click', () => {
        selectElementByGuid(null);
        loadSitePhotos();
      });

      // Wire reason chips
      $$('.photo-filters .filter-btn', pane).forEach(btn => {
        btn.addEventListener('click', () => {
          state.photoFilters.reason = btn.dataset.reason;
          loadSitePhotos();
        });
      });
      $('#photoAudienceFilter', pane)?.addEventListener('change', (e) => {
        state.photoFilters.audience = e.target.value;
        loadSitePhotos();
      });

      // Wire each card
      $$('.photo-card', pane).forEach(card => {
        const id = card.dataset.id;
        const p = state.photos.find(x => x.id === id);
        if (!p) return;
        card._photo = p;            // setupRowContextMenu reads this
        const img = $('img', card);
        if (img) ensurePhotoThumbSrc(id, img);
        card.addEventListener('click', (e) => {
          if (e.target.closest('button')) return;
          focusPhoto(p);
        });
        card.addEventListener('dblclick', () => focusPhoto(p));
        $('button[data-act=approve]', card)?.addEventListener('click', async (e) => {
          e.stopPropagation();
          let cap = p.caption || '';
          if (cap.trim().length < 3) {
            cap = await promptInline({
              title: 'Approve photo',
              label: 'Approval caption (visible to client)',
              placeholder: 'Describe what the client should see',
              defaultValue: cap,
              multiline: true, minLength: 3, maxLength: 2000,
              okLabel: 'Approve & publish',
            });
          }
          if (!cap || cap.trim().length < 3) return;
          const updated = await approveSitePhoto(id, cap);
          if (updated) { Object.assign(p, updated); renderPhotos(); }
        });
        $('button[data-act=reject]', card)?.addEventListener('click', async (e) => {
          e.stopPropagation();
          const reason = await promptInline({
            title: 'Reject photo',
            label: 'Reason (shown to the capturer)',
            placeholder: 'e.g. off-topic / poor quality / privacy',
            defaultValue: '',
            multiline: true, minLength: 0, maxLength: 500,
            okLabel: 'Reject',
          });
          if (reason === null) return;
          const updated = await rejectSitePhoto(id, reason);
          if (updated) { Object.assign(p, updated); renderPhotos(); }
        });
        $('button[data-act=withdraw]', card)?.addEventListener('click', async (e) => {
          e.stopPropagation();
          if (!confirm('Withdraw this photo from the client portal?')) return;
          const updated = await withdrawSitePhoto(id);
          if (updated) { Object.assign(p, updated); renderPhotos(); }
        });
      });
    }

    function renderPhotoCard(p, reviewerVisible) {
      const reason = p.reason || 'Reference';
      const audience = p.audience || 'Internal';
      const captured = p.capturedAt ? new Date(p.capturedAt).toLocaleString() : '';
      const lvlZone = [p.levelCode, p.zoneCode].filter(Boolean).join(' · ') || '';
      const caption = p.caption || '';
      const canApprove = reviewerVisible && audience === 'PendingReview';
      const canReject  = reviewerVisible && (audience === 'PendingReview' || audience === 'Approved');
      const canWithdraw = reviewerVisible && audience === 'ClientPortal';
      return `
        <div class="photo-card" data-id="${escapeHtml(p.id)}" data-kind="photo">
          <div class="thumb">
            <img alt="${escapeHtml(caption || 'Site photo')}" loading="lazy" />
            <div class="thumb-fallback" style="display:none">📷</div>
            <span class="reason-chip rc-${reason.toLowerCase()}">${escapeHtml(reason)}</span>
            <span class="audience-chip aud-${audience.toLowerCase()}">${escapeHtml(audience)}</span>
          </div>
          <div class="meta">
            <div class="cap">${caption ? escapeHtml(caption) : '<em class="muted">No caption</em>'}</div>
            <div class="sub">${escapeHtml(captured)}${lvlZone ? ' · ' + escapeHtml(lvlZone) : ''}</div>
            ${p.anchorElementGuid ? `<div class="sub anchor">⚓ ${escapeHtml((state.elementMap[p.anchorElementGuid]?.name) || p.anchorElementGuid.slice(0, 8))}</div>` : ''}
          </div>
          ${(canApprove || canReject || canWithdraw) ? `<div class="actions">
            ${canApprove ? '<button class="btn xs" data-act="approve">✓ Approve</button>' : ''}
            ${canReject  ? '<button class="btn xs subtle" data-act="reject">✗ Reject</button>' : ''}
            ${canWithdraw ? '<button class="btn xs subtle" data-act="withdraw">↶ Withdraw</button>' : ''}
          </div>` : ''}
        </div>
      `;
    }

    // ── Capture modal ─────────────────────────────────────────────────
    function inferDefaultReason() {
      // Auto-pre-select per the brief: element selected → AsBuilt; clash open
      // → Defect; otherwise Reference.
      if (state.selectedClashId) return 'Defect';
      if (state.selectedElementGuid) return 'AsBuilt';
      return 'Reference';
    }

    function openPhotoCaptureModal(seed = {}) {
      const modal = $('#photoCaptureModal');
      if (!modal) return;
      pendingPhotoFile = null;
      if (pendingPhotoObjectUrl) { try { URL.revokeObjectURL(pendingPhotoObjectUrl); } catch (_) {} pendingPhotoObjectUrl = null; }
      $('#pcCaption').value = '';
      $('#pcLevel').value   = seed.levelCode || '';
      $('#pcZone').value    = seed.zoneCode  || '';
      $('#pcPreview').innerHTML = '';
      const reason = seed.reason || inferDefaultReason();
      $$('#pcReason .choice').forEach(c => c.classList.toggle('active', c.dataset.v === reason));
      // Anchor pills — show what we've captured automatically.
      const elGuid = seed.guid || state.selectedElementGuid;
      const elMeta = elGuid ? (state.elementMap[elGuid] || {}) : null;
      $('#pcAnchorElement').textContent = 'Element: ' + (elMeta?.name || elMeta?.tag || (elGuid ? elGuid.slice(0, 8) : '—'));
      $('#pcAnchorElement').classList.toggle('on', !!elGuid);
      const haveXyz = !!lastClickPoint;
      $('#pcAnchorPoint').textContent = haveXyz
        ? `3D: ${lastClickPoint.x.toFixed(2)}, ${lastClickPoint.y.toFixed(2)}, ${lastClickPoint.z.toFixed(2)}`
        : '3D point: —';
      $('#pcAnchorPoint').classList.toggle('on', haveXyz);
      $('#pcAnchorModel').textContent = state.modelName ? 'Model: ' + state.modelName : 'Model: —';
      $('#pcAnchorModel').classList.toggle('on', !!modelId);
      state.photoCaptureSeed = { guid: elGuid, reason };
      modal.classList.add('open');
    }
    function closePhotoCaptureModal() {
      const modal = $('#photoCaptureModal');
      if (!modal) return;
      stopWebcam();   // release the camera if a live preview was open
      modal.classList.remove('open');
      pendingPhotoFile = null;
      if (pendingPhotoObjectUrl) { try { URL.revokeObjectURL(pendingPhotoObjectUrl); } catch (_) {} pendingPhotoObjectUrl = null; }
    }

    function setupPhotoCaptureModal() {
      const modal = $('#photoCaptureModal');
      if (!modal) return;
      $('#pcClose').addEventListener('click', closePhotoCaptureModal);
      $('#pcCancel').addEventListener('click', closePhotoCaptureModal);
      modal.addEventListener('click', (e) => { if (e.target.id === 'photoCaptureModal') closePhotoCaptureModal(); });
      modal.addEventListener('keydown', (e) => {
        if (!modal.classList.contains('open')) return;
        if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); closePhotoCaptureModal(); }
      });

      // Reason chip toggle
      $$('#pcReason .choice').forEach(c => c.addEventListener('click', () => {
        $$('#pcReason .choice').forEach(x => x.classList.remove('active'));
        c.classList.add('active');
      }));

      // File picker + drag-drop (same pattern as imAttachDrop)
      const drop = $('#pcDrop');
      const input = $('#pcFileInput');
      const pickBtn = $('#pcPickBtn');
      function stagePhoto(file) {
        if (!file) return;
        if (file.size > 25 * 1024 * 1024) { toast(`${file.name} > 25 MB — skipped`, 'warn'); return; }
        if (!file.type || !file.type.startsWith('image/')) { toast('Only image files accepted', 'warn'); return; }
        pendingPhotoFile = file;
        if (pendingPhotoObjectUrl) { try { URL.revokeObjectURL(pendingPhotoObjectUrl); } catch (_) {} }
        pendingPhotoObjectUrl = URL.createObjectURL(file);
        $('#pcPreview').innerHTML = `<img src="${pendingPhotoObjectUrl}" alt="preview" />`;
      }
      pickBtn?.addEventListener('click', (ev) => { ev.stopPropagation(); input.click(); });
      input.addEventListener('change', () => { stagePhoto(input.files?.[0]); input.value = ''; });

      // Desktop webcam capture (getUserMedia). Falls back to the file picker
      // when there's no camera or permission is denied. Mobile keeps its
      // native camera via the file input's capture="environment".
      const webcamBtn = $('#pcWebcamBtn');
      function snapFromVideo(video) {
        const w = video.videoWidth || 1280, h = video.videoHeight || 720;
        const canvas = document.createElement('canvas');
        canvas.width = w; canvas.height = h;
        canvas.getContext('2d').drawImage(video, 0, 0, w, h);
        canvas.toBlob((blob) => {
          stopWebcam();
          if (!blob) { toast('Could not capture frame', 'error'); $('#pcPreview').innerHTML = ''; return; }
          const file = new File([blob], 'webcam-' + Date.now() + '.jpg', { type: 'image/jpeg' });
          stagePhoto(file);          // same path as a picked file
        }, 'image/jpeg', 0.92);
      }
      async function startWebcamCapture() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
          toast('Webcam not available — opening file picker', 'warn');
          return input.click();
        }
        try {
          stopWebcam();
          pcStream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false });
        } catch (err) {
          console.warn('[photo] getUserMedia denied/failed', err);
          toast('Camera blocked or unavailable — use Pick file', 'warn');
          return input.click();   // graceful fallback
        }
        const preview = $('#pcPreview');
        preview.innerHTML = '';
        const video = document.createElement('video');
        video.autoplay = true; video.playsInline = true; video.muted = true;
        video.style.cssText = 'width:100%;border-radius:6px;background:#000';
        video.srcObject = pcStream;
        const bar = el('div', { style: 'display:flex;gap:8px;margin-top:6px' }, [
          el('button', { class: 'btn sm', type: 'button', onclick: () => snapFromVideo(video) }, '📸 Snap'),
          el('button', { class: 'btn sm subtle', type: 'button', onclick: () => { stopWebcam(); preview.innerHTML = ''; } }, 'Cancel'),
        ]);
        preview.appendChild(video); preview.appendChild(bar);
        try { await video.play(); } catch (_) {}
      }
      webcamBtn?.addEventListener('click', (ev) => { ev.stopPropagation(); startWebcamCapture(); });
      drop.addEventListener('click', () => input.click());
      drop.addEventListener('keydown', (ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); input.click(); } });
      ['dragenter','dragover'].forEach(evt => drop.addEventListener(evt, (ev) => { ev.preventDefault(); ev.stopPropagation(); drop.classList.add('dragover'); }));
      ['dragleave','drop'].forEach(evt => drop.addEventListener(evt, (ev) => { ev.preventDefault(); ev.stopPropagation(); drop.classList.remove('dragover'); }));
      drop.addEventListener('drop', (ev) => stagePhoto(ev.dataTransfer?.files?.[0]));
      ['dragover','drop'].forEach(evt => modal.addEventListener(evt, (ev) => { ev.preventDefault(); }));

      $('#pcSubmit').addEventListener('click', submitPhotoCapture);
    }

    async function submitPhotoCapture() {
      if (!pendingPhotoFile) { toast('Pick or capture a photo first', 'warn'); return; }
      const reason = $('#pcReason .choice.active')?.dataset.v || 'Reference';
      const caption = ($('#pcCaption').value || '').trim();
      const level   = ($('#pcLevel').value || '').trim();
      const zone    = ($('#pcZone').value || '').trim();
      const elGuid  = state.photoCaptureSeed?.guid || state.selectedElementGuid;

      const fd = new FormData();
      fd.append('file', pendingPhotoFile, pendingPhotoFile.name || 'capture.jpg');
      fd.append('reason', reason);
      if (caption) fd.append('caption', caption);
      if (level)   fd.append('levelCode', level);
      if (zone)    fd.append('zoneCode', zone);
      if (elGuid)  fd.append('anchorElementGuid', elGuid);
      if (modelId) fd.append('modelId', modelId);
      if (lastClickPoint) {
        fd.append('modelX', String(lastClickPoint.x));
        fd.append('modelY', String(lastClickPoint.y));
        fd.append('modelZ', String(lastClickPoint.z));
      }
      fd.append('source', 'web-viewer');
      fd.append('capturedAt', new Date().toISOString());

      // Disable button while in flight so the user can't double-submit on
      // a slow connection.
      const btn = $('#pcSubmit'); if (btn) { btn.disabled = true; btn.textContent = 'Uploading…'; }
      const created = await captureSitePhoto(fd);
      if (btn) { btn.disabled = false; btn.textContent = 'Capture →'; }
      if (!created) return;
      // Add to the front of the in-memory list, repaint pin + tab.
      state.photos.unshift(created);
      placePhotoPins();
      renderPhotos();
      updateRightTabCounts();
      closePhotoCaptureModal();
      toast(`Photo captured · ${created.reason}${created.audience === 'PendingReview' ? ' · Pending review' : ''}`, 'success');
      logHistory(`Captured ${created.reason} photo`);
    }

    // ── Reviewer mini-pane (PM/Admin/Owner bulk approve) ──────────────
    function openPhotoReviewModal() {
      const modal = $('#photoReviewModal');
      if (!modal) return;
      if (!isPhotoApprover()) { toast('Only PM / Admin / Owner can review photos', 'warn'); return; }
      state.photoReviewSelected.clear();
      $('#prBulkCaption').value = '';
      modal.classList.add('open');
      refreshPhotoReviewList();
    }
    function closePhotoReviewModal() {
      const modal = $('#photoReviewModal');
      if (!modal) return;
      modal.classList.remove('open');
      state.photoReviewSelected.clear();
    }

    async function refreshPhotoReviewList() {
      const list = $('#prList');
      const lbl = $('#prCountLabel');
      if (!list || !lbl) return;
      list.innerHTML = '<div class="inline-loader"><span class="dot-spin"></span>Loading pending photos…</div>';
      const data = await api(`/api/projects/${projectId}/photos?audience=PendingReview&pageSize=200`);
      const items = Array.isArray(data) ? data : (data?.items || []);
      lbl.textContent = items.length ? `${items.length} pending photo${items.length === 1 ? '' : 's'}` : 'Queue empty';
      if (!items.length) {
        list.innerHTML = '<div class="empty-state"><span class="glyph">✓</span>Nothing pending review</div>';
        return;
      }
      list.innerHTML = items.map(p => `
        <label class="review-row" data-id="${escapeHtml(p.id)}">
          <input type="checkbox" data-id="${escapeHtml(p.id)}" ${state.photoReviewSelected.has(p.id) ? 'checked' : ''} />
          <div class="thumb"><img alt="" loading="lazy" /><div class="thumb-fallback" style="display:none">📷</div></div>
          <div class="meta">
            <div class="cap">${p.caption ? escapeHtml(p.caption) : '<em class="muted">No caption</em>'}</div>
            <div class="sub">
              <span class="reason-chip rc-${(p.reason || 'reference').toLowerCase()}">${escapeHtml(p.reason || 'Reference')}</span>
              <span class="hint">${escapeHtml(p.capturedAt ? new Date(p.capturedAt).toLocaleString() : '')}</span>
              ${p.levelCode ? `<span class="hint">· ${escapeHtml(p.levelCode)}</span>` : ''}
              ${p.zoneCode  ? `<span class="hint">· ${escapeHtml(p.zoneCode)}</span>` : ''}
            </div>
          </div>
          <button class="btn xs subtle" data-act="reject" data-id="${escapeHtml(p.id)}" title="Reject">✗</button>
        </label>
      `).join('');
      // Lazy thumbs
      $$('.review-row .thumb img', list).forEach((img, i) => ensurePhotoThumbSrc(items[i].id, img));
      // Checkbox bind
      $$('input[type=checkbox]', list).forEach(cb => {
        cb.addEventListener('change', () => {
          if (cb.checked) state.photoReviewSelected.add(cb.dataset.id);
          else state.photoReviewSelected.delete(cb.dataset.id);
        });
      });
      // Per-row reject
      $$('button[data-act=reject]', list).forEach(btn => {
        btn.addEventListener('click', async (e) => {
          e.preventDefault(); e.stopPropagation();
          const reason = await promptInline({
            title: 'Reject photo',
            label: 'Reason (shown to the capturer)',
            placeholder: 'e.g. off-topic / poor quality / privacy',
            defaultValue: '',
            multiline: true, minLength: 0, maxLength: 500,
            okLabel: 'Reject',
          });
          if (reason === null) return;
          await rejectSitePhoto(btn.dataset.id, reason);
          refreshPhotoReviewList();
          loadSitePhotos();
        });
      });
    }

    function setupPhotoReviewModal() {
      const modal = $('#photoReviewModal');
      if (!modal) return;
      $('#prClose').addEventListener('click', closePhotoReviewModal);
      $('#prBulkCancel').addEventListener('click', closePhotoReviewModal);
      modal.addEventListener('click', (e) => { if (e.target.id === 'photoReviewModal') closePhotoReviewModal(); });
      modal.addEventListener('keydown', (e) => {
        if (!modal.classList.contains('open')) return;
        if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); closePhotoReviewModal(); }
      });
      $('#prRefresh').addEventListener('click', refreshPhotoReviewList);
      $('#prSelectAll').addEventListener('click', () => {
        $$('#prList input[type=checkbox]').forEach(cb => {
          cb.checked = true;
          state.photoReviewSelected.add(cb.dataset.id);
        });
      });
      $('#prSelectNone').addEventListener('click', () => {
        state.photoReviewSelected.clear();
        $$('#prList input[type=checkbox]').forEach(cb => cb.checked = false);
      });
      $('#prBulkApprove').addEventListener('click', async () => {
        const ids = Array.from(state.photoReviewSelected);
        if (!ids.length) { toast('Select at least one photo', 'warn'); return; }
        const cap = ($('#prBulkCaption').value || '').trim();
        if (cap.length < 3) { toast('Shared caption must be ≥ 3 chars', 'warn'); return; }
        const r = await bulkApproveSitePhotos(ids, cap);
        if (r) {
          state.photoReviewSelected.clear();
          await refreshPhotoReviewList();
          await loadSitePhotos();
        }
      });
    }

    // ── Capture FAB wiring ────────────────────────────────────────────
    function setupPhotoFab() {
      const fab = $('#photoFab');
      if (!fab) return;
      fab.addEventListener('click', () => openPhotoCaptureModal());
      // Hide until model is loaded so we don't tease the user with a button
      // they can't anchor properly. Re-enabled by the boot observer below.
      fab.style.display = 'none';
    }

    // SignalR-style live update hook. The signalr-shim.js loaded from
    // viewer.html mounts window.__planscapeHub before this runs; this
    // function binds the hub events to local state refreshers. When the
    // shim is missing (CDN unreachable / running inside RN WebView /
    // file:// scheme), the periodic loaders keep working as a fallback.
    function setupPhotoRealtime() {
      if (!projectId) return;
      const refreshPhotos = () => loadSitePhotos();
      const refreshIssues = (payload) => {
        // Only refetch when the event is for the active project.
        if (payload && payload.projectId && payload.projectId !== projectId) return;
        loadIssues();
      };
      const refreshComments = (payload) => {
        if (payload && payload.projectId && payload.projectId !== projectId) return;
        // Comments thread is loaded on demand when the right-rail tab
        // is opened; only repaint if the user is currently looking at
        // the affected issue's comments.
        if (state.rightTab === 'comments' && state.selectedIssueId &&
            payload && payload.issueId === state.selectedIssueId) {
          renderComments();
        }
      };

      // Public hook so external host harnesses (or test suites) can
      // simulate a refresh without a real hub event.
      window.__planscapePhotoRealtime = {
        onSitePhotoCaptured: refreshPhotos,
        onSitePhotoApproved: refreshPhotos,
        refresh: refreshPhotos,
      };

      // Bind to the shim if it's already mounted, OR register a
      // rebind callback that the shim will call once the CDN script
      // arrives. The "bound" tracker lives on window so a second
      // setupPhotoRealtime() invocation (page reload, hot-reload)
      // doesn't re-register every handler against the same hub
      // singleton — closure-scoped tracking would reset to empty on
      // each call and produce duplicates.
      window.__planscapeHubBound = window.__planscapeHubBound || new WeakSet();
      function bindHub() {
        const hub = window.__planscapeHub;
        if (!hub || typeof hub.on !== 'function') return;
        if (window.__planscapeHubBound.has(hub)) return;
        window.__planscapeHubBound.add(hub);
        hub.on('SitePhotoCaptured', refreshPhotos);
        hub.on('SitePhotoApproved', refreshPhotos);
        hub.on('IssueCreated', refreshIssues);
        hub.on('IssueUpdated', refreshIssues);
        hub.on('CommentAdded',  refreshComments);
      }
      bindHub();
      window.__planscapeRebindHub = bindHub;
    }

    // ── Bottom panel ───────────────────────────────────────────────────
    function setupBottomPanel() {
      const bottom = $('#bottomPanel');
      // R1 — the tray must NEVER exceed the current viewport (else the top grab-handle +
      // collapse button scroll off-screen and there's no way back without a refresh), nor
      // shrink below a usable minimum. Clamp on every window resize + on load, and persist a
      // SANE height that's re-clamped to the current viewport when restored.
      const BP_MIN = 80;
      const bpMaxH = () => Math.max(120, Math.floor(window.innerHeight * 0.85));
      function clampBottomPanel() {
        const bp = $('#bottomPanel'); if (!bp || bp.classList.contains('collapsed')) return;
        const cur = bp.getBoundingClientRect().height;
        const clamped = Math.min(bpMaxH(), Math.max(BP_MIN, cur));
        if (Math.abs(clamped - cur) > 1) {
          bp.style.height = clamped + 'px';
          document.documentElement.style.setProperty('--bottom-panel-height', clamped + 'px');
          onResize();
        }
      }
      try {
        const savedH = parseInt(localStorage.getItem('planscape_bottom_h') || '', 10);
        if (savedH && bottom && !bottom.classList.contains('collapsed')) {
          const c = Math.min(bpMaxH(), Math.max(BP_MIN, savedH));
          bottom.style.height = c + 'px';
          document.documentElement.style.setProperty('--bottom-panel-height', c + 'px');
        }
      } catch (_) {}
      window.addEventListener('resize', clampBottomPanel);
      setTimeout(clampBottomPanel, 0);
      // E1 — DELEGATED click handling on the stable #bottomPanel so the tab buttons
      // (CLASHES/ISSUES/TIMELINE) + the collapse toggle can't lose their bindings.
      if (bottom) bottom.addEventListener('click', (ev) => {
        const tab = ev.target.closest('.btab');
        if (tab && bottom.contains(tab)) { switchBottomTab(tab.dataset.tab); return; }
        const col = ev.target.closest('#bottomCollapse');
        if (col) {
          bottom.classList.toggle('collapsed');
          bottom.classList.remove('expanded');
          $('.viewport-wrap')?.classList.toggle('bp-collapsed', bottom.classList.contains('collapsed'));
          savePanelState(); onResize();
        }
      });

      // ── Expand button (max state — toggles 60vh) ─────────────────────
      const expandBtn = $('#bottomExpand');
      if (expandBtn) {
        expandBtn.addEventListener('click', () => {
          const bp = $('#bottomPanel');
          const wrap = $('.viewport-wrap');
          bp.classList.remove('collapsed');
          wrap?.classList.remove('bp-collapsed');
          bp.classList.toggle('expanded');
          // Clear any inline height the resize-drag set so the .expanded
          // CSS class wins; toggling expanded back off restores the
          // CSS-variable-driven default height.
          if (bp.classList.contains('expanded')) {
            bp.style.removeProperty('height');
          }
          // Mirror .expanded onto the viewport-wrap so the floating
          // overlays (level strip, nav controls, coord readout, minimap)
          // shift up to clear the 60vh tray instead of being covered.
          // Without this they end up trapped underneath because their
          // `bottom: calc(var(--bottom-panel-height) + …)` was computed
          // against the default 220 px var.
          wrap?.classList.toggle('bp-expanded', bp.classList.contains('expanded'));
          expandBtn.textContent = bp.classList.contains('expanded') ? '⤓' : '⛶';
          expandBtn.title = bp.classList.contains('expanded')
            ? 'Restore default height'
            : 'Expand to 60% of viewport';
          onResize();
        });
      }

      // ── Drag-to-resize on the top edge ───────────────────────────────
      // We change the CSS variable in :root so the floating overlays
      // (level strip, nav controls, coord readout, minimap) keep their
      // bottom: calc(var(--bottom-panel-height) + …) offsets aligned.
      const resizeHandle = $('#bottomResize');
      if (resizeHandle) {
        let dragStartY = 0;
        let dragStartHeight = 0;
        const minH = 80;
        const maxH = () => Math.max(120, window.innerHeight * 0.85);
        const onMove = (ev) => {
          const dy = dragStartY - ev.clientY;       // up = bigger panel
          const next = Math.min(maxH(), Math.max(minH, dragStartHeight + dy));
          const bp = $('#bottomPanel');
          bp.style.height = next + 'px';
          // Push the same value into the root var so floating overlays
          // recompute their bottom: calc(...).
          document.documentElement.style.setProperty('--bottom-panel-height', next + 'px');
        };
        const onUp = () => {
          const bp = $('#bottomPanel');
          bp.classList.remove('resizing');
          document.removeEventListener('pointermove', onMove);
          document.removeEventListener('pointerup', onUp);
          clampBottomPanel();                         // R1 — never leave an off-screen height
          try { localStorage.setItem('planscape_bottom_h', String(parseInt(bp.style.height, 10) || '')); } catch (_) {}
          onResize();                                 // canvas re-fit
        };
        resizeHandle.addEventListener('pointerdown', (ev) => {
          const bp = $('#bottomPanel');
          if (bp.classList.contains('collapsed')) return;
          // Drag breaks the .expanded preset — once you've manually
          // resized, you've "taken over" the height, same as a normal
          // window drag in any IDE. Drop the mirror class on the
          // viewport-wrap too so the floating overlays return to their
          // default --bottom-panel-height-driven offsets.
          bp.classList.remove('expanded');
          $('.viewport-wrap')?.classList.remove('bp-expanded');
          bp.classList.add('resizing');
          dragStartY = ev.clientY;
          dragStartHeight = bp.getBoundingClientRect().height;
          document.addEventListener('pointermove', onMove);
          document.addEventListener('pointerup', onUp, { once: true });
          ev.preventDefault();
        });
        // Double-click the grip to reset to the CSS-default height.
        resizeHandle.addEventListener('dblclick', () => {
          $('#bottomPanel').style.removeProperty('height');
          document.documentElement.style.removeProperty('--bottom-panel-height');
          try { localStorage.removeItem('planscape_bottom_h'); } catch (_) {}   // R1 — reset persisted height
          onResize();
        });
      }
      $('#btnRunDetect').addEventListener('click', () => {
        toast('Running clash detection… (mock)', 'warn');
        setTimeout(() => { state.clashes = mockClashes(); placeClashPins(); renderClashes(); toast('Clash detection complete', 'success'); }, 1200);
      });
      $('#btnExportCsv').addEventListener('click', exportClashesCsv);
      $('#btnExportIssues').addEventListener('click', exportIssuesCsv);
      $('#btnNewIssue').addEventListener('click', () => openIssueModal());

      // X1 — bind status + type axes independently.
      // R2 — DELEGATE the filter bars on their stable containers (was one-time per-button
      // addEventListener — the E1 anti-pattern). One listener per bar reads the clicked
      // .filter-btn via closest(), so the filters survive any re-render.
      const delegateFilters = (containerId, apply) => {
        const c = $('#' + containerId); if (!c) return;
        c.addEventListener('click', (ev) => {
          const b = ev.target.closest('.filter-btn'); if (!b || !c.contains(b)) return;
          $$('#' + containerId + ' .filter-btn').forEach(x => x.classList.remove('active'));
          b.classList.add('active');
          apply(b);
        });
      };
      delegateFilters('clashStatusFilters', b => { state.clashStatusFilter = b.dataset.status; renderClashes(); });
      delegateFilters('clashTypeFilters', b => { state.clashTypeFilter = b.dataset.type; renderClashes(); });
      delegateFilters('issueFilters', b => { state.issuesFilter = b.dataset.f; renderIssues(); });
    }
    function switchBottomTab(name) {
      state.bottomTab = name;
      $$('.btab').forEach(t => t.classList.toggle('active', t.dataset.tab === name));
      $$('.bottom-pane').forEach(p => p.classList.toggle('active', p.id === 'bp-' + name));
      $$('#bottomPanel .filter-row').forEach(r => r.style.display = (r.id === 'fr-' + name) ? '' : 'none');
      if (name === 'timeline') renderTimeline();
      // B4 — keep the .viewport-wrap.bp-collapsed class in sync with the
      // actual bottom-panel state, so the level strip / nav controls /
      // coord readout / minimap don't end up floating mid-air.
      $('#bottomPanel').classList.remove('collapsed');
      $('.viewport-wrap')?.classList.remove('bp-collapsed');
      onResize();
    }

    function exportClashesCsv() {
      const rows = [['ID','Type','Element A','Element B','Disc','Overlap mm','Assigned','Status']];
      state.clashes.forEach(c => rows.push([c.id, c.type, c.elementA?.name, c.elementB?.name, c.discPair, c.overlap_mm, c.assignedTo || '', c.status]));
      downloadCsv('clashes.csv', rows);
    }
    function exportIssuesCsv() {
      const rows = [['ID','Title','Priority','Status','Assignee','Due']];
      state.issues.forEach(i => rows.push([i.code || i.id, i.title, i.priority, i.status, i.assigneeName || '', i.dueDate || '']));
      downloadCsv('issues.csv', rows);
    }
    function downloadCsv(name, rows) {
      // B10 — UTF-8 BOM so Excel renders mm/° and accented assignee names
      // correctly instead of garbling them as Windows-1252.
      // B9 — revoke the object URL after the synthetic click so the blob
      // isn't pinned in memory until the tab closes.
      const csv = '﻿' + rows.map(r => r.map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(',')).join('\n');
      const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a'); a.href = url; a.download = name; a.click();
      setTimeout(() => { try { URL.revokeObjectURL(url); } catch (_) {} }, 0);
    }

    function renderTimeline() {
      const c = $('#timelineCanvas');
      if (!c) return;
      const ctx = c.getContext('2d');
      const w = c.width = c.clientWidth;
      const h = c.height = c.clientHeight;
      ctx.fillStyle = '#1C1F26'; ctx.fillRect(0, 0, w, h);
      // mock data
      const sessions = 10;
      const clashTrend  = Array.from({ length: sessions }, (_, i) => Math.max(0, 60 - i * 5 + Math.random() * 8));
      const issueTrend  = Array.from({ length: sessions }, (_, i) => Math.max(0, 18 - i * 1.4 + Math.random() * 3));
      drawSpark(ctx, clashTrend, w, h, '#EF4444', 0);
      drawSpark(ctx, issueTrend, w, h, '#F59E0B', 1);
      ctx.fillStyle = '#8892A4'; ctx.font = '11px Inter';
      ctx.fillText('Clashes (red) · Issues (amber)  — last 10 sessions', 10, 16);
    }
    function drawSpark(ctx, data, w, h, colour) {
      const max = Math.max(...data, 1);
      ctx.beginPath();
      data.forEach((v, i) => {
        const x = 20 + (w - 40) * (i / (data.length - 1));
        const y = h - 16 - (h - 40) * (v / max);
        if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
      });
      ctx.strokeStyle = colour; ctx.lineWidth = 2; ctx.stroke();
    }

    // ── Viewport overlays (coords + minimap + level + nav + section) ───
    let lastClickPoint = null;
    function setupViewportOverlays() {
      const dom = V.renderer.domElement;
      const readout = $('#coordReadout');
      const ray = new THREE_.Raycaster();
      const ptr = new THREE_.Vector2();
      const tooltip = $('#pinTooltip');
      dom.addEventListener('pointermove', (e) => {
        if (!V.modelRoot) return;
        const r = dom.getBoundingClientRect();
        ptr.x = ((e.clientX - r.left) / r.width) * 2 - 1;
        ptr.y = -((e.clientY - r.top) / r.height) * 2 + 1;
        ray.setFromCamera(ptr, V.camera);
        const hits = ray.intersectObject(vizGroup(), true);
        if (hits.length) {
          const p = hits[0].point;
          readout.innerHTML = `<span class="x">X</span> ${p.x.toFixed(2)}m  <span class="y">Y</span> ${p.y.toFixed(2)}m  <span class="z">Z</span> ${p.z.toFixed(2)}m`;
        }
        // U1 — hover tooltip on issue / clash pins.
        const pinTargets = [];
        state.issuePins.forEach(m => pinTargets.push(m));
        state.clashPins.forEach(m => pinTargets.push(m));
        state.photoPins.forEach(m => pinTargets.push(m));
        const pinHits = pinTargets.length ? ray.intersectObjects(pinTargets, false) : [];
        if (pinHits.length && tooltip) {
          const u = pinHits[0].object.userData;
          let html = '';
          if (u.issueId) {
            const issue = state.issues.find(x => x.id === u.issueId);
            if (issue) html = `<div class="ttitle">${escapeHtml(issue.title || 'Issue')}</div>
              <div class="tmeta">${escapeHtml(issue.code || u.issueId)} · ${escapeHtml(issue.priority || 'MED')} · ${escapeHtml(issue.status || 'NEW')}</div>`;
          } else if (u.clashId) {
            const cl = state.clashes.find(x => x.id === u.clashId);
            if (cl) html = `<div class="ttitle">${escapeHtml(cl.elementA?.name)} ✕ ${escapeHtml(cl.elementB?.name)}</div>
              <div class="tmeta">${escapeHtml(cl.id)} · ${cl.type} · ${cl.overlap_mm}mm · ${escapeHtml(cl.status)}</div>`;
          } else if (u.photoId) {
            const p = state.photos.find(x => x.id === u.photoId);
            if (p) html = `<div class="ttitle">${escapeHtml(p.caption || 'Site photo')}</div>
              <div class="tmeta">${escapeHtml(p.reason || '')} · ${escapeHtml(p.audience || '')} · ${escapeHtml(p.capturedAt ? new Date(p.capturedAt).toLocaleDateString() : '')}</div>`;
          }
          if (html) {
            tooltip.innerHTML = html;
            tooltip.style.display = 'block';
            tooltip.style.left = (e.clientX - r.left + 14) + 'px';
            tooltip.style.top  = (e.clientY - r.top  + 14) + 'px';
          } else {
            tooltip.style.display = 'none';
          }
        } else if (tooltip) {
          tooltip.style.display = 'none';
        }
      });
      dom.addEventListener('pointerleave', () => { if (tooltip) tooltip.style.display = 'none'; });
      // L3 — capture the press location at pointer-down, only commit it
      // to lastClickPoint on pointer-up if the pointer barely moved
      // (otherwise the user was orbiting / panning).
      let pressPx = null;
      dom.addEventListener('pointerdown', (e) => {
        pressPx = { x: e.clientX, y: e.clientY };
      });
      dom.addEventListener('pointerup', (e) => {
        if (!pressPx) return;
        const moved = Math.hypot(e.clientX - pressPx.x, e.clientY - pressPx.y);
        pressPx = null;
        if (moved > 6) return;                  // drag, not a click
        if (!V.modelRoot) return;
        const r = dom.getBoundingClientRect();
        ptr.x = ((e.clientX - r.left) / r.width) * 2 - 1;
        ptr.y = -((e.clientY - r.top) / r.height) * 2 + 1;
        ray.setFromCamera(ptr, V.camera);
        // R14 — if the click landed on a pin, do NOT update lastClickPoint
        // from the model surface behind it. Pin hits go through the
        // engine's pinTap event instead.
        if (V.pinGroup) {
          const pinHits = ray.intersectObject(V.pinGroup, true);
          if (pinHits.length) return;
        }
        const hits = ray.intersectObject(vizGroup(), true);
        if (hits.length) {
          lastClickPoint = hits[0].point.clone();
          // Pivot mode — a single click sets the orbit centre (controls.target)
          // WITHOUT zooming (ACC-style). Subsequent wheel-zoom + drag-rotate happen
          // around that point. (Was checking the stale 'focus' mode name; the nav
          // button now sets 'pivot'.) Plain pick-mode keeps selecting elements.
          if (state.activeNav === 'pivot') {
            V.controls.target.copy(lastClickPoint);
            V.controls.update();
            toast('Orbit pivot set — click to move it', 'success');
          }
        }
      });

      // ACC-style: PLAIN double-click ZOOMS-TO-FIT the clicked element (and, via
      // fitCamera, sets the orbit pivot to its centre). A double-click that misses
      // the model is a no-op. (Single click only selects — see selectElementByGuid.)
      dom.addEventListener('dblclick', (e) => {
        if (!V.modelRoot) return;
        const r = dom.getBoundingClientRect();
        ptr.x = ((e.clientX - r.left) / r.width) * 2 - 1;
        ptr.y = -((e.clientY - r.top) / r.height) * 2 + 1;
        ray.setFromCamera(ptr, V.camera);
        const hits = ray.intersectObject(vizGroup(), true);
        if (!hits.length) return;
        const m = hits[0].object;
        if (m && m.isMesh) {
          const bb = new THREE_.Box3().setFromObject(m);
          try { window.STING_VIEWER && window.STING_VIEWER.fitCamera && window.STING_VIEWER.fitCamera(bb); } catch (_) {}
          return;
        }
        // Non-mesh hit — fall back to setting the pivot at the hit point.
        V.controls.target.copy(hits[0].point);
        V.controls.update();
      });
      // R13 — drop the standalone pin-click raycaster. The engine already
      // raycasts pinGroup on every click and emits 'pinTap' via the
      // bridge for any uuid in pinMeta. We listen for it below.

      window.addEventListener('resize', onResize);
      setupMinimap();
      // Hook the original 'pick' events through to our properties panel.
      const origSend = V.bridge.send;
      V.bridge.send = function (type, payload) {
        // Angle measurement: intercept picks and collect 3 points.
        if (type === 'pick' && payload && state.angleTool) {
          if (payload.point) {
            state.anglePoints = state.anglePoints || [];
            state.anglePoints.push(payload.point);
            const n = state.anglePoints.length;
            if (n === 1) toast('Angle: now click first arm point');
            else if (n === 2) toast('Angle: now click second arm point');
            else if (n >= 3) {
              const [v, a, b] = state.anglePoints.map(p => ({ x: p[0], y: p[1], z: p[2] }));
              const ax = a.x-v.x, ay = a.y-v.y, az = a.z-v.z;
              const bx = b.x-v.x, by = b.y-v.y, bz = b.z-v.z;
              const dot = ax*bx + ay*by + az*bz;
              const lenA = Math.sqrt(ax*ax+ay*ay+az*az), lenB = Math.sqrt(bx*bx+by*by+bz*bz);
              const angle = lenA > 0 && lenB > 0
                ? Math.acos(Math.max(-1, Math.min(1, dot/(lenA*lenB)))) * 180 / Math.PI
                : 0;
              toast(`Angle: ${angle.toFixed(2)}°`, 'info');
              state.angleTool = false; state.anglePoints = [];
            }
          }
          return origSend.call(V.bridge, type, payload);
        }
        // Empty-space click (engine raycast hit nothing) — clear the whole
        // selection so the floating toolbar + multi-property pane disappear.
        if (type === 'deselect') {
          selectElementByGuid(null);
          return origSend.call(V.bridge, type, payload);
        }
        if (type === 'pick' && payload && payload.guid) {
          // Route canvas-picks through the multi-select-aware selector so
          // clearing happens correctly and the floating selection toolbar
          // / multi-property pane stay in sync. Ctrl/Cmd-pick toggles into
          // the existing set; Shift-pick adds; plain pick replaces.
          const ev = payload.event || {};
          const mode = (ev.ctrlKey || ev.metaKey) ? 'toggle'
                     : ev.shiftKey               ? 'add'
                     : 'replace';
          selectElementByGuid(payload.guid, mode);
        }
        // Live XYZ readout — push values into the coord chip if it's visible.
        if (type === 'coord' && payload) {
          const chip = document.getElementById('coordChip');
          if (chip && chip.style.display !== 'none') {
            if (payload.hit && payload.point) {
              const [x, y, z] = payload.point;
              chip.textContent = `X ${x.toFixed(0)}  Y ${y.toFixed(0)}  Z ${z.toFixed(0)}`;
            } else if (payload.off !== true) {
              chip.textContent = 'XYZ —';
            }
          }
        }
        // R13 — engine emits pinTap for any pin in pinMeta. Coord pins
        // tagged with __coord = 'issue' / 'clash' route into our focus
        // handlers; legacy pins (priority-only payload) keep working
        // for any external embedders.
        if (type === 'pinTap' && payload) {
          if (payload.__coord === 'issue' && payload.issueId) {
            const i = state.issues.find(x => x.id === payload.issueId);
            if (i) focusIssue(i);
          } else if (payload.__coord === 'clash' && payload.clashId) {
            const c = state.clashes.find(x => x.id === payload.clashId);
            if (c) focusClash(c);
          } else if (payload.__coord === 'photo' && payload.photoId) {
            const p = state.photos.find(x => x.id === payload.photoId);
            if (p) focusPhoto(p);
          }
        }
        return origSend.call(V.bridge, type, payload);
      };
    }

    // Multi-select aware selector. mode=
    //   "replace" → standard click; clear set, set primary to guid
    //   "toggle"  → Ctrl/Cmd-click; add or remove guid from set
    //   "add"     → Shift-click / programmatic; ensure guid in set
    function selectElementByGuid(guid, mode = 'replace') {
      if (!guid) {
        state.selectedElementGuid = null;
        state.selectedElementGuids.clear();
        reapplySelection();          // A3 — drop highlights ONLY; appearance stays put
        renderProperties(null);
        updateRightTabCounts();
        renderSelectionToolbar();
        return;
      }
      if (mode === 'toggle') {
        if (state.selectedElementGuids.has(guid)) {
          state.selectedElementGuids.delete(guid);
          if (state.selectedElementGuid === guid) {
            // Promote any remaining selection to primary, else clear.
            const it = state.selectedElementGuids.values().next();
            state.selectedElementGuid = it.done ? null : it.value;
          }
        } else {
          state.selectedElementGuids.add(guid);
          state.selectedElementGuid = guid;
        }
      } else if (mode === 'add') {
        state.selectedElementGuids.add(guid);
        state.selectedElementGuid = guid;
      } else {
        state.selectedElementGuid = guid;
        state.selectedElementGuids.clear();
        state.selectedElementGuids.add(guid);
      }
      // A3 — selection is an OVERLAY on the appearance layer: clone the current
      // appearance material + emissive, never wipe ghost/colour. Selecting does NOT
      // touch any _vizMode or the render mode. (Was clearAllHighlights + emissive,
      // which reset every mesh to shaded — the "click resets to shaded" bug.)
      reapplySelection();
      // ACC-style interaction — a single click SELECTS ONLY; the camera does NOT
      // move. Double-click frames the element (see the dblclick handler).
      renderProperties(state.selectedElementGuid);
      updateRightTabCounts();
      renderSelectionToolbar();
      // Slice 4b — if Photos tab is active, refetch with the anchor filter
      // applied so the gallery narrows to photos for the selected element.
      if (state.rightTab === 'photos') loadSitePhotos();
    }

    function setupMinimap() {
      const wrap = $('#minimap');
      const canvas = $('#minimapCanvas');
      if (!wrap) return;
      // M6 — drop the second WebGL context (couldn't share the scene's GPU buffers →
      // rendered empty). The minimap is now a scissored inset of the ONE main renderer,
      // drawn behind this transparent frame.
      if (canvas) canvas.style.display = 'none';
      wrap.style.background = 'transparent';
      const cam = new THREE_.OrthographicCamera(-1, 1, 1, -1, 0.1, 1e6);
      cam.up.set(0, 0, -1);   // top-down looking down rendered -Y; +Z is "down" on the map

      $('#minimapToggle')?.addEventListener('click', () => wrap.classList.toggle('collapsed'));

      // "You are here" marker (HTML overlay on the transparent frame).
      const marker = el('div', { style:
        'position:absolute;width:9px;height:9px;border-radius:50%;background:#3b82f6;' +
        'border:1.5px solid #fff;transform:translate(-50%,-50%);pointer-events:none;z-index:3' });
      wrap.appendChild(marker);

      let curPad = 1, curCentre = new THREE_.Vector3();
      // Render the inset every frame, after the main scene, via the shared renderer.
      V.onAfterRender = () => {
        if (!V.modelRoot || V.modelBounds.isEmpty() || wrap.classList.contains('collapsed')) { marker.style.display = 'none'; return; }
        const c = V.modelBounds.getCenter(new THREE_.Vector3());
        const s = V.modelBounds.getSize(new THREE_.Vector3());
        const pad = Math.max(s.x, s.z) * 0.55 || 1;
        curPad = pad; curCentre = c;
        cam.left = -pad; cam.right = pad; cam.top = pad; cam.bottom = -pad;
        cam.position.set(c.x, V.modelBounds.max.y + s.y + 10, c.z);   // above, looking down -Y
        cam.lookAt(c);
        cam.near = 0.1; cam.far = s.y * 4 + 200;
        cam.updateProjectionMatrix();
        const host = V.renderer.domElement.getBoundingClientRect();
        const mr = wrap.getBoundingClientRect();
        V.renderInset(cam, mr.left - host.left, mr.top - host.top, mr.width, mr.height);
        // place the marker at the main camera's ground position within the map
        const camPos = V.camera.position;
        const mx = (camPos.x - c.x) / pad;            // -1..1 → world X (screen-right)
        const my = -((camPos.z - c.z) / pad);         // world Z maps to screen via -Z up
        marker.style.display = '';
        marker.style.left = ((mx + 1) / 2 * mr.width) + 'px';
        marker.style.top  = ((1 - my) / 2 * mr.height) + 'px';
      };

      // Map a minimap pixel to a horizontal world point (inverse of the marker map).
      function mapToWorld(clientX, clientY) {
        const mr = wrap.getBoundingClientRect();
        const nx = (clientX - mr.left) / mr.width * 2 - 1;
        const ny = -((clientY - mr.top) / mr.height * 2 - 1);
        return new THREE_.Vector3(curCentre.x + nx * curPad, V.controls.target.y, curCentre.z - ny * curPad);
      }

      // M6 — drag leak fix: capture pointer events on the frame + stopPropagation so a
      // minimap drag never reaches the main OrbitControls. Drag = pan/recenter the main
      // view; a click (< 4px) = flyTo.
      let dragging = false, downX = 0, downY = 0, moved = 0;
      wrap.addEventListener('pointerdown', (e) => {
        if (e.target === $('#minimapToggle')) return;       // let the collapse button work
        dragging = true; downX = e.clientX; downY = e.clientY; moved = 0;
        try { wrap.setPointerCapture(e.pointerId); } catch (_) {}
        e.stopPropagation(); e.preventDefault();
      });
      wrap.addEventListener('pointermove', (e) => {
        if (!dragging) return;
        e.stopPropagation();
        moved += Math.hypot(e.clientX - downX, e.clientY - downY);
        downX = e.clientX; downY = e.clientY;
        const w = mapToWorld(e.clientX, e.clientY);
        const delta = w.clone().sub(V.controls.target); delta.y = 0;   // horizontal recenter
        V.camera.position.add(delta); V.controls.target.add(delta); V.controls.update();
      });
      const endDrag = (e) => {
        if (!dragging) return;
        dragging = false; e.stopPropagation();
        try { wrap.releasePointerCapture(e.pointerId); } catch (_) {}
        if (moved < 4) flyTo(mapToWorld(e.clientX, e.clientY));   // treat as a click
      };
      wrap.addEventListener('pointerup', endDrag);
      wrap.addEventListener('pointercancel', endDrag);
      wrap.addEventListener('contextmenu', (e) => e.preventDefault());
      wrap.addEventListener('wheel', (e) => e.stopPropagation());   // don't zoom the main view
    }

    function setupNavControls() {
      // Capture OrbitControls' default mouse-button bindings so Pan ↔ Orbit
      // toggling can restore them.
      const defaultButtons = (V.controls && V.controls.mouseButtons)
        ? Object.assign({}, V.controls.mouseButtons)
        : null;
      // Brief visual flash for one-shot nav buttons (home / level / fit).
      function flashNavBtn(btn) {
        btn.classList.add('flash');
        setTimeout(() => btn.classList.remove('flash'), 300);
      }
      const navEl = $('#navControls');
      // E1 — DELEGATED click handler on the stable #navControls container (one
      // listener) so the nav-mode buttons can never lose their binding on a re-render.
      if (!navEl) return;
      navEl.addEventListener('click', (ev) => {
        const b = ev.target.closest('.nav-btn');
        if (!b || !navEl.contains(b)) return;
        const m = b.dataset.mode;
        // One-shot actions: fire and return without changing active mode.
        if (m === 'fit') {
          if (state.selectedElementGuids.size) fitToSelection();
          else fitVisibleOrToast();              // Item 2 — frame only what's visible
          flashNavBtn(b);
          return;
        }
        if (m === 'home') {
          fitVisibleOrToast();                   // Item 2 — frame only what's visible
          flashNavBtn(b);
          return;
        }
        if (m === 'level') {
          // Make the current view horizontal — zero pitch, keep heading.
          if (V.levelCamera) V.levelCamera();
          flashNavBtn(b);
          return;
        }
        // E2 — raise / lower the eye (altitude only; heading + pitch fixed). Moves
        // camera.position AND controls.target by the same delta along the rendered up
        // axis (+Y), model-scaled, via the camera GETTER inside ext.elevateCamera.
        if (m === 'elevUp' || m === 'elevDown') {
          const x = window.STING_VIEWER_EXTRAS;
          if (x && x.elevateCamera) x.elevateCamera(m === 'elevUp' ? 1 : -1);
          flashNavBtn(b);
          return;
        }
        $$('.nav-btn').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        state.activeNav = m;
        // Selecting a navigation mode exits any active exclusive tool
        // (markup / measure / section) — restoring pick + OrbitControls.
        if (state.activeTool !== 'orbit' && state.activeTool !== 'pick') setActiveTool('orbit');
        // Walk mode delegates to viewer-extras' first-person controls.
        handleHostCommand({ type: 'setWalkthrough', payload: { enabled: m === 'walk' } });
        // Toggle navControls.walking class so the speed-wheel highlights.
        if (navEl) navEl.classList.toggle('walking', m === 'walk');
        // Pan mode rebinds left mouse to PAN; Orbit restores defaults.
        if (V.controls && V.controls.mouseButtons && THREE_.MOUSE) {
          if (m === 'pan') {
            V.controls.mouseButtons.LEFT  = THREE_.MOUSE.PAN;
            V.controls.mouseButtons.RIGHT = THREE_.MOUSE.ROTATE;
          } else if (defaultButtons) {
            V.controls.mouseButtons.LEFT   = defaultButtons.LEFT;
            V.controls.mouseButtons.MIDDLE = defaultButtons.MIDDLE;
            V.controls.mouseButtons.RIGHT  = defaultButtons.RIGHT;
          }
        }
        // Pivot mode — orbit around a point you click (handled in the pointerdown
        // handler above). If something's already selected, seed the pivot at its
        // centre immediately (no zoom). Then click anywhere to move the pivot.
        if (m === 'pivot') {
          const sel = findMeshByGuid(state.selectedElementGuid);
          if (sel) {
            const bb = new THREE_.Box3().setFromObject(sel);
            V.controls.target.copy(bb.getCenter(new THREE_.Vector3()));
            V.controls.update();
          }
          toast('Pivot mode — click to set the orbit centre', 'info');
        }
      });

      // ── Walk-speed widget ─────────────────────────────────────────────
      // Three input paths into the same multiplier:
      //   1. +/- buttons in the nav-controls speed-wheel widget
      //   2. Mouse-wheel scroll while walk mode is active
      //   3. Settings menu "Default walk speed" persisted in localStorage
      // viewer-extras.js reads window.__walkSpeedMul on every step.
      if (typeof window.__walkSpeedMul !== 'number') window.__walkSpeedMul = 1.0;
      try {
        const persisted = parseFloat(localStorage.getItem('planscape_walk_speed'));
        if (!isNaN(persisted) && persisted > 0) window.__walkSpeedMul = persisted;
      } catch (_) {}
      function paintSpeed(animate) {
        const v = $('#walkSpeedVal');
        if (!v) return;
        v.textContent = window.__walkSpeedMul.toFixed(2).replace(/\.?0+$/, '') + '×';
        if (animate) {
          v.classList.remove('bumped');
          // Force reflow so the animation restarts even on rapid bumps.
          void v.offsetWidth;
          v.classList.add('bumped');
          v.addEventListener('animationend', () => v.classList.remove('bumped'), { once: true });
        }
      }
      function bumpSpeed(delta) {
        const prev = window.__walkSpeedMul;
        const next = Math.min(8, Math.max(0.1, window.__walkSpeedMul + delta));
        window.__walkSpeedMul = Math.round(next * 100) / 100;
        if (window.__walkSpeedMul === prev) return; // already at limit, no paint
        try { localStorage.setItem('planscape_walk_speed', String(window.__walkSpeedMul)); } catch (_) {}
        paintSpeed(true);
      }
      $('#walkSpeedDown')?.addEventListener('click', (e) => { e.stopPropagation(); bumpSpeed(-0.25); });
      $('#walkSpeedUp')?.addEventListener('click',   (e) => { e.stopPropagation(); bumpSpeed(+0.25); });
      paintSpeed();

      // Scroll wheel during walk mode:
      //   • Plain scroll  → move the camera forward/backward along the
      //     current look direction (one-step nudge sized to model scale).
      //   • Shift+scroll  → adjust walk speed (the previous behaviour,
      //     now behind a modifier so plain scroll can navigate freely).
      // Side-panel content keeps native scroll in both cases.
      window.addEventListener('wheel', (ev) => {
        if (state.activeNav !== 'walk') return;
        const tgt = ev.target;
        if (tgt && /INPUT|TEXTAREA|SELECT/.test(tgt.tagName)) return;
        // Scrollable side panes keep native behaviour.
        if (tgt && tgt.closest && (
            tgt.closest('.left-panel') ||
            tgt.closest('.right-panel') ||
            tgt.closest('.bottom-panel'))) return;

        if (ev.shiftKey) {
          // Shift+scroll → adjust speed (fine: 10% per notch).
          const sign = ev.deltaY < 0 ? +1 : -1;
          bumpSpeed(sign * 0.1);
        } else {
          // Plain scroll → step the camera forward/backward, projected
          // onto the floor plane so movement stays horizontal even when
          // the camera is pitched up or down (matches WASD behaviour).
          const V = window.STING_VIEWER;
          if (V && V.camera && V.modelBounds) {
            const sign = ev.deltaY < 0 ? 1 : -1;
            const step = V.modelBounds.getSize(new THREE_.Vector3()).length() * 0.02
                         * (window.__walkSpeedMul || 1.0);
            const dir = new THREE_.Vector3();
            V.camera.getWorldDirection(dir);
            // Project onto floor plane using the active walk-up axis.
            const upArr = window.__walkUp;
            if (upArr) {
              const up = new THREE_.Vector3(upArr[0], upArr[1], upArr[2]);
              dir.addScaledVector(up, -dir.dot(up));
              if (dir.lengthSq() > 1e-6) dir.normalize();
            }
            V.camera.position.addScaledVector(dir, sign * step);
          }
        }
        ev.preventDefault();
      }, { passive: false });
    }

    // ── One mutually-exclusive tool state: orbit | pick | measure | markup | section.
    // The engine's pick raycaster only fires in 'pick'/'orbit'; markup/measure/section
    // own the pointer. Switching tools (or Exit) cleanly tears down the previous one
    // and restores OrbitControls + pick.
    function setActiveTool(t) {
      const prev = state.activeTool;
      state.activeTool = t;                       // set FIRST so teardown sees the new state
      if (prev === 'markup' && t !== 'markup') {
        const x = window.STING_VIEWER_EXTRAS;
        if (x && x.stopMarkup) { try { x.stopMarkup(); } catch (_) {} }   // restores rotate + detaches markup input
        showMarkupBar(false);
      }
      if (prev === 'section' && t !== 'section') {
        if (typeof exitSectionTool === 'function') { try { exitSectionTool(); } catch (_) {} }  // Commit 4
      }
      // Gate the engine pick raycaster.
      const engineTool = (t === 'measure' || t === 'markup' || t === 'section') ? t : 'pick';
      handleHostCommand({ type: 'setTool', payload: { tool: engineTool } });
    }

    // Enter markup with a specific tool — sets the exclusive tool state, then starts
    // the markup gesture in the engine and shows the markup toolbar.
    function startMarkupTool(mode) {
      setActiveTool('markup');
      handleHostCommand({ type: 'startMarkup', payload: { mode } });
      showMarkupBar(true);
    }

    // Floating markup toolbar: Undo · Clear all · Exit. (Escape also exits, handled
    // in viewer-extras which fires sting:markupStopped → we tidy the host state.)
    function showMarkupBar(show) {
      let bar = document.getElementById('markupBar');
      if (!show) { if (bar) bar.remove(); return; }
      if (bar) return;
      bar = el('div', { id: 'markupBar', style:
        'position:absolute;top:64px;left:50%;transform:translateX(-50%);z-index:14;display:flex;gap:6px;' +
        'background:rgba(0,0,0,0.62);padding:6px 10px;border-radius:8px;backdrop-filter:blur(4px)' }, [
        el('span', { style: 'color:#ffcc33;font:12px sans-serif;align-self:center;margin-right:4px' }, '✏ Markup'),
        el('button', { class: 'btn sm subtle', onclick: () => { const x = window.STING_VIEWER_EXTRAS; if (x && x.undoMarkup) x.undoMarkup(); } }, '↩ Undo'),
        el('button', { class: 'btn sm subtle', onclick: () => { handleHostCommand({ type: 'clearMarkup' }); toast('Markups cleared'); } }, '🗑 Clear all'),
        el('button', { class: 'btn sm', style: 'background:rgba(208,80,80,0.85);color:#fff', onclick: () => setActiveTool('orbit') }, '✕ Exit'),
      ]);
      document.body.appendChild(bar);
    }
    // Escape (or any internal markup exit) in viewer-extras → tidy the host tool state.
    if (typeof window !== 'undefined') {
      window.addEventListener('sting:markupStopped', () => {
        if (state.activeTool === 'markup') {
          state.activeTool = 'orbit';
          handleHostCommand({ type: 'setTool', payload: { tool: 'pick' } });
          showMarkupBar(false);
        }
      });
    }

    // A4 — render mode is now a GLOBAL appearance state. setRenderMode just records
    // it and lets applyAppearance compose it with the per-discipline/category/colour
    // resolution (keep-solid + hide honoured there) and re-overlay the selection.
    function setRenderMode(mode) {
      if (!V.modelRoot) return;
      state.renderMode = mode;
      // Realistic is a RENDERER-global (env + tonemap), not a per-mesh lens. Toggle it
      // here; applyAppearance treats 'realistic' like 'shaded' for per-mesh resolution
      // so colour-by / ghost / hide still override on top.
      try { if (V.setRealistic) V.setRealistic(mode === 'realistic'); } catch (_) {}
      applyAppearance();
      broadcastVizRenderMode(mode);   // mirror to meeting followers (no-op when solo)
      // E — set expectations: Realistic lights the model, but full surface detail needs
      // textures (re-publish with PLANSCAPE_EXPORT_TEXTURES). Colour-only materials stay matte.
      if (mode === 'realistic') toast('Realistic — lit view. Full detail needs textures (re-publish with PLANSCAPE_EXPORT_TEXTURES).');
      else toast('View: ' + mode);
    }
    function clearRenderMode() {
      if (!V.modelRoot) { state.renderMode = 'shaded'; return; }
      state.renderMode = 'shaded';
      try { if (V.setRealistic) V.setRealistic(false); } catch (_) {}
      applyAppearance();
    }

    // WS2 — mirror the global render mode + keep-solid exclusion to meeting
    // followers through the existing overlay channel (a renderMode-tagged profile;
    // meeting-sync routes it to sting:remoteRenderMode instead of applyOverlay).
    function broadcastVizRenderMode(mode) {
      if (state.applyingRemoteViz) return;
      const m = (typeof window !== 'undefined') && window.STING_MEETING;
      if (!m || typeof m.broadcastOverlay !== 'function') return;
      try {
        m.broadcastOverlay({
          source: 'renderMode',
          renderMode: mode,
          keepSolidDisc: Array.from(state.vizKeepSolidDisc),
          keepSolidCat:  Array.from(state.vizKeepSolidCat),
        });
      } catch (_) {}
    }
    // Apply a render mode + exclusion received from the meeting presenter.
    function applyRemoteVizRenderMode(p) {
      if (!p) return;
      state.applyingRemoteViz = true;
      try {
        state.vizKeepSolidDisc = new Set(p.keepSolidDisc || []);
        state.vizKeepSolidCat  = new Set(p.keepSolidCat || []);
        setRenderMode(p.renderMode || 'shaded');
        if (state.rightTab === 'visualize') renderVisualizePanel();
      } catch (_) {}
      state.applyingRemoteViz = false;
    }
    // C5 — apply a FULL appearance snapshot received from the meeting presenter.
    function applyRemoteVizSnapshot(viz) {
      if (!viz) return;
      state.applyingRemoteViz = true;
      try { applyVizSnapshot(viz); } catch (_) {}
      state.applyingRemoteViz = false;
    }
    if (typeof window !== 'undefined') {
      window.addEventListener('sting:remoteRenderMode', (e) => applyRemoteVizRenderMode(e.detail));
      window.addEventListener('sting:remoteAppearance', (e) => applyRemoteVizSnapshot(e.detail && e.detail.viz));
    }

    // 3D Explode — works on ANY model. Toggles an Explode control panel (factor
    // slider 0..N + grouping: Radial | By level | By discipline). Factor 0 =
    // collapsed. Visual only — coexists with section + selection.
    function toggleExplodedView() {
      const x = window.STING_VIEWER_EXTRAS;
      if (!x || !x.setExplodeFactor) { toast('Explode unavailable', 'warn'); return; }
      const existing = $('#explodePanel');
      if (existing) { existing.remove(); x.setExplodeFactor(0); state.explodeFactor = 0; toast('Explode off'); return; }
      const panel = el('div', { id: 'explodePanel', style:
        'position:absolute;top:120px;right:12px;z-index:13;width:200px;background:rgba(20,22,28,0.92);' +
        'padding:10px;border-radius:8px;backdrop-filter:blur(4px)' });
      panel.appendChild(el('div', { style: 'display:flex;justify-content:space-between;align-items:center;font:600 11px sans-serif;color:#cfd6e4;margin-bottom:6px' }, [
        el('span', {}, '💥 EXPLODE'),
        el('button', { style: 'background:none;border:none;color:#9aa3b2;cursor:pointer;font-size:14px', onclick: () => toggleExplodedView() }, '✕'),
      ]));
      panel.appendChild(el('div', { style: 'font:11px sans-serif;color:#9aa3b2;margin-bottom:2px' }, 'Factor'));
      const fl = el('input', { type: 'range', min: '0', max: '100', value: String(Math.round((state.explodeFactor || 0) / 2 * 100)), style: 'width:100%;accent-color:var(--accent)' });
      fl.addEventListener('input', () => { state.explodeFactor = parseInt(fl.value, 10) / 100 * 2; x.setExplodeFactor(state.explodeFactor); });
      panel.appendChild(fl);
      const modes = el('div', { style: 'display:flex;gap:4px;margin-top:8px' });
      [['radial', 'Radial'], ['level', 'By level'], ['discipline', 'By disc.']].forEach(([m, lbl], i) => {
        const b = el('button', { class: 'btn sm subtle' + (i === 0 ? ' active' : ''), onclick: () => {
          modes.querySelectorAll('button').forEach(o => o.classList.remove('active')); b.classList.add('active');
          if (x.setExplodeMode) x.setExplodeMode(m);
        } }, lbl);
        modes.appendChild(b);
      });
      panel.appendChild(modes);
      $('.viewport-wrap')?.appendChild(panel);
      if (state.explodeFactor > 0) x.setExplodeFactor(state.explodeFactor);
      toast('Explode — drag the factor (0 = collapsed)');
    }

    // Edge-silhouette overlay — wireframe-on-shaded for depth perception.
    function toggleEdgeOverlay() {
      const extras = window.STING_VIEWER_EXTRAS;
      if (!extras || !extras.setEdgeOverlay) return;
      state.edgeOverlay = !state.edgeOverlay;
      extras.setEdgeOverlay(state.edgeOverlay);
      toast(state.edgeOverlay ? 'Edge overlay ON' : 'Edge overlay OFF');
    }

    // Clash / issue marker visibility — independent View-menu toggles (default on).
    // Flip the existing pin meshes' .visible (place*Pins re-applies the flag on rebuild).
    // The wire-box clash markers + sphere issue markers stay visually distinct from the
    // orbit-pivot indicator; this only touches the marker groups, never the pivot.
    // B — reflect marker on/off on the labelled toolbar button (dim + strike when off).
    function paintMarkerBtn(id, on) {
      const b = $('#' + id); if (!b) return;
      b.style.opacity = on ? '1' : '0.45';
      b.style.textDecoration = on ? 'none' : 'line-through';
    }
    function toggleClashMarkers() {
      state.clashMarkersVisible = !state.clashMarkersVisible;
      state.clashPins.forEach(m => { m.visible = state.clashMarkersVisible; });
      paintMarkerBtn('tbClashMarkers', state.clashMarkersVisible);
      toast(state.clashMarkersVisible ? 'Clash markers ON' : 'Clash markers OFF');
    }
    function toggleIssueMarkers() {
      state.issueMarkersVisible = !state.issueMarkersVisible;
      state.issuePins.forEach(m => { m.visible = state.issueMarkersVisible; });
      paintMarkerBtn('tbIssueMarkers', state.issueMarkersVisible);
      toast(state.issueMarkersVisible ? 'Issue markers ON' : 'Issue markers OFF');
    }

    // Section caps — translucent fill at every clipping plane.
    function toggleSectionCaps() {
      const extras = window.STING_VIEWER_EXTRAS;
      if (!extras || !extras.setSectionCaps) return;
      state.sectionCaps = !state.sectionCaps;
      extras.setSectionCaps(state.sectionCaps);
      toast(state.sectionCaps ? 'Section caps ON' : 'Section caps OFF');
    }

    // Live XYZ readout — coordinator hovers cursor; coords stream into a chip.
    function toggleCoordReadout() {
      const extras = window.STING_VIEWER_EXTRAS;
      if (!extras || !extras.setCoordReadout) return;
      state.coordReadout = !state.coordReadout;
      extras.setCoordReadout(state.coordReadout);
      let chip = document.getElementById('coordChip');
      if (!chip) {
        chip = document.createElement('div');
        chip.id = 'coordChip';
        chip.style.cssText = 'position:absolute;right:12px;top:48px;background:rgba(15,18,24,0.85);border:1px solid #2a3140;border-radius:6px;padding:6px 10px;font:11px ui-monospace,monospace;color:#cdd6e3;z-index:60;pointer-events:none;display:none;';
        chip.textContent = 'XYZ —';
        (document.getElementById('viewerCanvas') || document.body).appendChild(chip);
      }
      chip.style.display = state.coordReadout ? 'block' : 'none';
      toast(state.coordReadout ? 'Coordinates readout ON' : 'Coordinates readout OFF');
    }

    // Camera presets — orthogonal + iso through the extras layer so the
    // walk-mode up-axis fix-up runs consistently.
    function setCameraPreset(preset) {
      const extras = window.STING_VIEWER_EXTRAS;
      if (!extras || !extras.setCameraPreset) return;
      extras.setCameraPreset(preset);
      toast('View: ' + preset);
    }

    // Camera bookmark slots — captured in extras, persisted only in-session.
    function saveBookmark(slot) {
      const extras = window.STING_VIEWER_EXTRAS;
      if (!extras || !extras.saveCameraBookmark) return;
      extras.saveCameraBookmark(slot);
      toast(`Bookmark ${slot} saved — View → Restore to recall`);
    }
    function restoreBookmark(slot) {
      const extras = window.STING_VIEWER_EXTRAS;
      if (!extras || !extras.restoreCameraBookmark) return;
      extras.restoreCameraBookmark(slot);
      toast(`Bookmark ${slot} restored`);
    }

    function setupSectionCard() {
      // E5 — optional-chain every binding so one missing element can't abort the rest
      // of the section card's wiring (the E1 fragility class, contained per-control).
      $('#sectionClose')?.addEventListener('click', () => { const c = $('#sectionCard'); if (c) c.style.display = 'none'; });
      $('#sectionAddX')?.addEventListener('click', () => addSectionPlane('x'));
      $('#sectionAddY')?.addEventListener('click', () => addSectionPlane('y'));
      $('#sectionAddZ')?.addEventListener('click', () => addSectionPlane('z'));
      $('#sectionClear')?.addEventListener('click', () => clearSection());
    }

    function openSectionPlane(axis) {
      $('#sectionCard').style.display = 'block';
      if (axis === 'box') return enterSectionBox();
      addSectionPlane(axis);
    }
    // Section BOX — 6-plane AABB clip with sliders + an optional draggable gizmo.
    function enterSectionBox() {
      setActiveTool('section');
      const x = window.STING_VIEWER_EXTRAS;
      if (x && x.setSectionBox) {
        x.setSectionBox({});                       // default to whole-model bounds
        if (x.onSectionChange) x.onSectionChange(syncSectionBoxSliders);  // gizmo → sliders
      }
      $('#sectionCard').style.display = 'block';
      renderSectionBoxPanel();
      toast('Section box — drag the sliders, or enable the gizmo to drag faces');
    }
    function addSectionPlane(axis) {
      handleHostCommand({ type: 'addSectionPlaneAxis', payload: { axis, offset: 0.5 } });
      renderSectionCard();
    }
    function clearSection() {
      exitSectionTool();
      handleHostCommand({ type: 'clearSectionPlanes' });
      $('#sectionCard').style.display = 'none';
      state.sectionPlanes = [];
    }
    // Tear down the section box (clip + caps + gizmo) — also called by
    // setActiveTool when switching away from 'section'.
    function exitSectionTool() {
      const x = window.STING_VIEWER_EXTRAS;
      if (x && x.clearSectionBox) try { x.clearSectionBox(); } catch (_) {}
      $('#sectionCard').style.display = 'none';
    }
    function renderSectionBoxPanel() {
      const body = $('#sectionCard .body');
      if (!body) return;
      body.innerHTML = '';
      const x = window.STING_VIEWER_EXTRAS;
      const box = (x && x.getSectionBox) ? x.getSectionBox() : { active: false };
      const fr = box.fractions || { minX: 0, maxX: 1, minY: 0, maxY: 1, minZ: 0, maxZ: 1 };
      [['X', 'x', 'min', 'minX'], ['X', 'x', 'max', 'maxX'],
       ['Y', 'y', 'min', 'minY'], ['Y', 'y', 'max', 'maxY'],
       ['Z', 'z', 'min', 'minZ'], ['Z', 'z', 'max', 'maxZ']].forEach(([lbl, axis, end, key]) => {
        const row = el('div', { style: 'display:flex;align-items:center;gap:6px;font-size:11px;color:var(--text-muted);margin-top:3px' });
        row.appendChild(el('span', { style: 'min-width:30px' }, (end === 'min' ? '−' : '+') + lbl));
        const s = el('input', { type: 'range', min: '0', max: '100', value: String(Math.round((fr[key] || 0) * 100)),
          'data-sbkey': key, style: 'flex:1;accent-color:var(--accent)' });
        s.addEventListener('input', () => { if (x && x.setSectionBoxFace) x.setSectionBoxFace(axis, end, parseInt(s.value, 10) / 100); });
        row.appendChild(s);
        body.appendChild(row);
      });
      const mkChk = (label, checked, fn) => {
        const c = el('input', { type: 'checkbox' }); c.checked = !!checked;
        c.addEventListener('change', () => fn(c));
        return el('label', { style: 'display:flex;gap:4px;align-items:center;font-size:11px;color:var(--text-muted)' }, [c, label]);
      };
      const toolRow = el('div', { style: 'display:flex;flex-wrap:wrap;gap:8px;margin-top:8px' }, [
        mkChk('Caps', box.caps, (c) => { if (x && x.setSectionCaps) x.setSectionCaps(c.checked); }),
        mkChk('Flip (show outside)', box.invert, (c) => { if (x && x.setSectionInvert) x.setSectionInvert(c.checked); }),   // E3
        mkChk('Gizmo', false, (c) => {
          if (!x) return;
          if (c.checked) { if (!x.attachSectionGizmo || !x.attachSectionGizmo()) { c.checked = false; toast('Drag-gizmo unavailable — use sliders', 'warn'); } }
          else if (x.detachSectionGizmo) x.detachSectionGizmo();
        }),
      ]);
      body.appendChild(toolRow);
      body.appendChild(el('div', { style: 'display:flex;gap:4px;margin-top:6px' }, [
        el('button', { class: 'btn sm subtle', onclick: () => sectionBoxFromSelection() }, 'From selection'),
        el('button', { class: 'btn sm danger', onclick: () => clearSection() }, 'Clear'),
      ]));
    }
    // Engine → UI: when the gizmo drags a face, push the new fractions to the sliders.
    function syncSectionBoxSliders(info) {
      if (!info || !info.fractions) return;
      $$('#sectionCard input[data-sbkey]').forEach(s => {
        const k = s.dataset.sbkey;
        if (info.fractions[k] != null) s.value = String(Math.round(info.fractions[k] * 100));
      });
    }

    // Render the section card plane list with per-plane offset sliders.
    function renderSectionCard() {
      const body = $('#sectionCard .body');
      if (!body) return;
      const existing = body.querySelector('.plane-list');
      if (existing) existing.remove();
      const planes = (window.STING_VIEWER_EXTRAS?.getSectionPlanes?.() || []);
      if (!planes.length) return;
      const list = document.createElement('div');
      list.className = 'plane-list';
      list.style.cssText = 'margin-top:8px;display:flex;flex-direction:column;gap:4px;';
      planes.forEach(p => {
        const row = document.createElement('div');
        row.style.cssText = 'display:flex;align-items:center;gap:6px;font-size:11px;color:var(--text-muted)';
        row.innerHTML = `<span style="min-width:36px;text-transform:uppercase">${p.axis}</span>
          <input type="range" min="0" max="100" value="${Math.round((p.offset||0.5)*100)}"
            style="flex:1;accent-color:var(--accent)" data-id="${p.id}" />
          <span class="pct">${Math.round((p.offset||0.5)*100)}%</span>
          <button style="background:none;border:none;color:var(--danger);cursor:pointer;font-size:14px" data-remove="${p.id}">✕</button>`;
        const slider = row.querySelector('input[type=range]');
        slider.addEventListener('input', () => {
          const off = parseInt(slider.value, 10) / 100;
          row.querySelector('.pct').textContent = slider.value + '%';
          handleHostCommand({ type: 'updateSectionPlane', payload: { id: p.id, offset: off } });
        });
        row.querySelector(`[data-remove="${p.id}"]`).addEventListener('click', () => {
          handleHostCommand({ type: 'removeSectionPlane', payload: { id: p.id } });
          renderSectionCard();
        });
        list.appendChild(row);
      });
      body.appendChild(list);
    }

    // ── Angle measurement ──────────────────────────────────────────────
    function startAngleTool() {
      state.angleTool = true;
      state.anglePoints = [];
      setActiveTool('pick');
      toast('Angle: click vertex, then first arm point, then second arm point', 'info');
    }

    // ── Keyboard shortcuts ─────────────────────────────────────────────
    // Arrow-key + PageUp/Down navigation (ACC parity).
    //   Arrows           → pan in screen-space (continuous while held)
    //   Shift + arrows   → orbit around the current pivot
    //   PageUp / PageDn  → dolly toward / away from the pivot
    //   Home             → zoom to fit (alias of Space)
    // Speed scales with the camera-to-pivot distance, so the same hold
    // feels right on a 1 m room and a 1 km master-plan.
    function setupKeyNav() {
      const held = new Set();
      let shiftDown = false;
      let ctrlDown = false;     // M5 — Ctrl+Arrow raises/lowers the eye (elevation)
      const NAV_KEYS = new Set([
        'ArrowLeft','ArrowRight','ArrowUp','ArrowDown','PageUp','PageDown'
      ]);
      window.addEventListener('keydown', (e) => {
        if (/INPUT|TEXTAREA|SELECT/.test(e.target.tagName)) return;
        if ($('#issueModal')?.classList.contains('open')) return;
        if (e.key === 'Shift') { shiftDown = true; return; }
        if (e.key === 'Control' || e.key === 'Meta') { ctrlDown = true; return; }
        if (e.key === 'Home')  { handleHostCommand({ type: 'fit' }); e.preventDefault(); return; }
        if (NAV_KEYS.has(e.key)) {
          held.add(e.key);
          // Ctrl+Arrow is our elevation gesture — don't let the browser hijack it.
          e.preventDefault();          // stop the page scrolling under us
        }
      });
      window.addEventListener('keyup', (e) => {
        if (e.key === 'Shift') { shiftDown = false; return; }
        if (e.key === 'Control' || e.key === 'Meta') { ctrlDown = false; return; }
        held.delete(e.key);
      });
      // Clear held keys if the user alt-tabs away — otherwise the camera
      // drifts forever in the direction last pressed.
      window.addEventListener('blur', () => { held.clear(); shiftDown = false; ctrlDown = false; });

      function tick() {
        if (held.size) {
          const cam = V.camera, target = V.controls.target;
          const dist = cam.position.distanceTo(target) || 1;
          const panStep   = dist * 0.018;     // pan amount per frame
          const orbitStep = 0.025;             // radians per frame
          const dollyStep = dist * 0.025;     // dolly amount per frame

          // Screen-space basis. forward/right/up are recomputed every
          // frame because the camera may have rotated since last call.
          const forward = new THREE_.Vector3();
          cam.getWorldDirection(forward);
          const right = new THREE_.Vector3().crossVectors(forward, cam.up).normalize();
          const up    = new THREE_.Vector3().crossVectors(right, forward).normalize();

          const elevStep = dist * 0.02;        // M5 — eye-elevation per frame
          let panX = 0, panY = 0, dolly = 0, orbX = 0, orbY = 0, elevY = 0;
          if (held.has('ArrowLeft'))  { if (shiftDown) orbX -= orbitStep; else panX -= panStep; }
          if (held.has('ArrowRight')) { if (shiftDown) orbX += orbitStep; else panX += panStep; }
          // Ctrl+Up/Down → raise/lower the eye along rendered +Y (heading + pitch kept).
          if (held.has('ArrowUp'))    { if (ctrlDown) elevY += elevStep; else if (shiftDown) orbY -= orbitStep; else panY += panStep; }
          if (held.has('ArrowDown'))  { if (ctrlDown) elevY -= elevStep; else if (shiftDown) orbY += orbitStep; else panY -= panStep; }
          if (held.has('PageUp'))     dolly -= dollyStep;
          if (held.has('PageDown'))   dolly += dollyStep;

          if (elevY) { cam.position.y += elevY; target.y += elevY; }
          if (panX || panY || dolly) {
            const offset = new THREE_.Vector3()
              .addScaledVector(right, panX)
              .addScaledVector(up,    panY)
              .addScaledVector(forward, -dolly);
            cam.position.add(offset);
            target.add(offset);
          }
          if (orbX || orbY) {
            // Orbit camera position around the pivot, leaving target fixed.
            const o = cam.position.clone().sub(target);
            const sph = new THREE_.Spherical().setFromVector3(o);
            sph.theta -= orbX;
            sph.phi   -= orbY;
            // Match controls.maxPolarAngle (π·0.95) so the camera never
            // rolls under the model when held-arrow orbiting.
            sph.phi = Math.max(0.05, Math.min(Math.PI * 0.95, sph.phi));
            o.setFromSpherical(sph);
            cam.position.copy(target).add(o);
          }
          V.controls.update();
        }
        requestAnimationFrame(tick);
      }
      requestAnimationFrame(tick);
    }

    function setupKeyboardShortcuts() {
      document.addEventListener('keydown', (e) => {
        if (/INPUT|TEXTAREA|SELECT/.test(e.target.tagName)) return;
        const k = e.key;
        // B12 — when the modal is open, its own keydown handler owns Esc
        // (and Tab) so dismissing it doesn't also wipe the user's
        // selection / highlights.
        if ($('#issueModal')?.classList.contains('open')) return;
        if ($('#photoCaptureModal')?.classList.contains('open')) return;
        if ($('#photoReviewModal')?.classList.contains('open')) return;
        // p = toggle PIVOT nav mode (ACC/Navisworks parity) — then a single click
        // sets the orbit centre without zooming. Shift+P keeps the photo-capture
        // shortcut (it used to own plain P).
        if (k === 'p') {
          const piv = $('.nav-btn[data-mode=pivot]');
          if (piv) { piv.click(); e.preventDefault(); return; }
        }
        if (k === 'P') {   // Shift+P — open the photo capture modal
          if (state.modelName || state.elementMap) {
            openPhotoCaptureModal();
            e.preventDefault();
            return;
          }
        }
        if (k === 'Escape') {
          // R7 — help overlay swallows Esc so closing it doesn't ALSO
          // wipe the user's selection / highlights.
          const help = $('.help-overlay');
          if (help && help.classList.contains('open')) {
            help.classList.remove('open');
            return;
          }
          // Row-menu (clash/issue right-click popover) also swallows Esc
          // for the same reason — closing the menu shouldn't also clear
          // the selection it was opened against.
          const rowMenu = $('#rowMenu');
          if (rowMenu && rowMenu.classList.contains('open')) {
            return;     // setupRowContextMenu's own keydown handler closes it
          }
          // B2 — Esc clears the SELECTION only; the visualize appearance stays put.
          handleHostCommand({ type: 'clearHighlight' });
          state.selectedElementGuid = null;
          state.selectedElementGuids.clear();
          state.selectedIssueId = null;
          reapplySelection();            // drop selection overlays, keep appearance
          renderProperties(null);
          updateRightTabCounts();        // X2
          renderSelectionToolbar();
        } else if (k === 'f' || k === 'F') {
          // F = fit selection (multi-aware). With nothing selected, fit the
          // whole model edge-to-edge. Matches the "Fit" button in nav-controls.
          if (state.selectedElementGuids.size) fitToSelection();
          else if (V.fitCamera) V.fitCamera();
        } else if (k === 'h' || k === 'H') {
          // H = hide selected. Multi-aware: hides every mesh in the
          // selection set, not just the primary.
          if (state.selectedElementGuids.size) hideSelection();
        } else if (k === 'i' || k === 'I') {
          // I = isolate selection (multi-aware). Plain `I` is reserved for
          // isolate; Shift+I (handled below) creates a new issue.
          if (e.shiftKey) {} else if (state.selectedElementGuids.size) isolateSelection();
        } else if (k === 'a' || k === 'A') {
          showAllElements();
        } else if (k === 'g' || k === 'G') {
          state.ghostMode = !state.ghostMode;
          setRenderMode(state.ghostMode ? 'ghost' : 'shaded');
        } else if (k === 'w' || k === 'W') {
          $('.nav-btn[data-mode=walk]')?.click();
        } else if (k === 'o' || k === 'O') {
          $('.nav-btn[data-mode=orbit]')?.click();
        } else if (k === 'm' || k === 'M') {
          setActiveTool('measure');
        } else if (k === 'e' || k === 'E') {
          // E = human eye view (drop to eye height, look horizontal, keep heading).
          const x = window.STING_VIEWER_EXTRAS;
          if (x && x.humanEyeView) { x.humanEyeView(); toast('Human eye view'); }
        } else if (k === ' ') {
          handleHostCommand({ type: 'fit' });
          e.preventDefault();
        } else if ((e.ctrlKey || e.metaKey) && (k === 's' || k === 'S')) {
          e.preventDefault(); $('#btnAddView').click();
        } else if (e.shiftKey && (k === 'I' || k === 'i')) {
          // Shift+I — create a new issue. The bare 'I' shortcut is already
          // taken (isolate selected element), so this is the new-issue
          // variant. Pre-seeds the linked element if one is selected.
          e.preventDefault();
          const sel = state.selectedElementGuid;
          openIssueModal(sel ? { guid: sel, meta: state.elementMap?.[sel] || {} } : {});
        } else if (k >= '1' && k <= '7') {
          const pills = $$('.level-pill'); const p = pills[parseInt(k, 10) - 1]; if (p) p.click();
        }
      });
    }

    function setupHelp() {
      $('.help-overlay .close-help').addEventListener('click', () => $('.help-overlay').classList.remove('open'));
    }

    // ── Misc helpers ───────────────────────────────────────────────────
    // U5 — gentle nudge when the viewer is opened without query params.
    function showEmptyStateCTA() {
      if (document.getElementById('noProjectCta')) return;
      const wrap = $('.viewport-wrap'); if (!wrap) return;
      const cta = el('div', { class: 'no-project-cta', id: 'noProjectCta' });
      cta.innerHTML = `
        <div class="card">
          <h2>Pick a project to get started</h2>
          <p>The coordination viewer needs a project and model id in the URL.
             Open a project from the dashboard, or append
             <code>?project=&lt;id&gt;&amp;model=&lt;id&gt;</code> to this URL.</p>
          <button class="btn" id="ctaBackToProjects">Open projects list</button>
        </div>`;
      wrap.appendChild(cta);
      $('#ctaBackToProjects', cta).addEventListener('click', () => {
        location.href = (apiBase || '') + '/projects';
      });
      // Hide the boot loader behind it so it doesn't double-spin.
      const bl = $('#bootLoader'); if (bl) bl.style.display = 'none';
    }

    // B14 — shared screenshot helper. Source canvas is the live WebGL
    // canvas; we draw it into a 2D canvas at the target width and emit
    // JPEG. Returns a data URL.
    function downscaleScreenshot(srcCanvas, maxWidth, quality) {
      try {
        const sw = srcCanvas.width, sh = srcCanvas.height;
        if (!sw || !sh) return srcCanvas.toDataURL('image/png');
        const scale = Math.min(1, maxWidth / sw);
        const tw = Math.round(sw * scale), th = Math.round(sh * scale);
        const c = document.createElement('canvas');
        c.width = tw; c.height = th;
        c.getContext('2d').drawImage(srcCanvas, 0, 0, tw, th);
        return c.toDataURL('image/jpeg', quality || 0.85);
      } catch (_) {
        return srcCanvas.toDataURL('image/png');
      }
    }

    function takeScreenshot() {
      const b64 = downscaleScreenshot(V.renderer.domElement, 2560, 0.92);
      const a = document.createElement('a');
      a.href = b64;
      a.download = `${state.modelName || 'view'}-${Date.now()}.jpg`;
      a.click();
      toast('Screenshot saved', 'success');
    }
    function shareCurrentView() {
      // B2 — route through copyText so non-secure contexts (file://, old
      // WebView) get the textarea + execCommand fallback instead of a
      // silent no-op + a misleading "copied" toast.
      const url = `${location.origin}${location.pathname}?project=${projectId}&model=${modelId}`;
      copyText(url);
    }

    // ── Connectivity heartbeat (U6) ────────────────────────────────────
    // Lightweight: ping /health every 15s and toggle the session pill.
    // SignalR proper would need the full @microsoft/signalr browser bundle;
    // this gives the coordinator a real "Live / Offline" signal without
    // pulling in 100KB of dependencies. Browser online/offline events
    // short-circuit the next ping.
    function setupHeartbeat() {
      const pill = $('#sessionPill');
      if (!pill) return;
      function setOnline(ok) {
        pill.classList.toggle('offline', !ok);
        pill.lastChild.nodeValue = ok ? 'Live' : 'Offline';
        pill.title = ok ? 'Server reachable' : 'Server unreachable — retrying…';
      }
      // If the API is disabled (file://, RN WebView), the host owns
      // connectivity — show neutral state instead of polling.
      if (!apiEnabled) {
        pill.classList.toggle('offline', false);
        pill.lastChild.nodeValue = isWebView ? 'Bridged' : 'Local';
        pill.title = isWebView ? 'Connected via React Native bridge' : 'Standalone mode';
        return;
      }
      let alive = true;
      async function ping() {
        try {
          const res = await fetch(`${apiBase}/health`, { method: 'GET', cache: 'no-store' });
          setOnline(res.ok);
          alive = res.ok;
        } catch (_) {
          setOnline(false);
          alive = false;
        }
      }
      ping();                                          // immediate
      // R2 — keep the timer handle on state so the beforeunload cleanup
      // (which clears state.heartbeatTimer) actually does something.
      state.heartbeatTimer = setInterval(ping, 15000);  // every 15s
      window.addEventListener('online',  ping);
      window.addEventListener('offline', () => setOnline(false));
      document.addEventListener('visibilitychange', () => { if (!document.hidden) ping(); });
    }

    // X2 — right-panel tab counts. Numbers reflect what's relevant to
    // the selected element when one is selected, else the totals.
    function updateRightTabCounts() {
      const guid = state.selectedElementGuid;
      const cl = guid
        ? state.clashes.filter(c => c.elementA?.guid === guid || c.elementB?.guid === guid)
        : state.clashes;
      const is = guid
        ? state.issues.filter(i => Array.isArray(i.elementGuids) && i.elementGuids.includes(guid))
        : state.issues.filter(i => i.status !== 'RESOLVED');
      // R15 — prefer the cached length once loaded; otherwise fall back
      // to the issue's commentCount field from the API so the badge shows
      // useful info before the user opens the Comments tab.
      let cmnt = 0;
      if (state.selectedIssueId) {
        const cached = commentsCache.get(state.selectedIssueId);
        if (cached) cmnt = cached.length;
        else {
          const issue = state.issues.find(i => i.id === state.selectedIssueId);
          cmnt = (issue && (issue.commentCount ?? issue.comments_count ?? issue.commentsCount)) || 0;
        }
      }
      const set = (id, n) => {
        const e = $('#' + id); if (!e) return;
        e.textContent = n ? `(${n})` : '';
      };
      set('rightTabClashesCount',  cl.length);
      set('rightTabIssuesCount',   is.length);
      set('rightTabCommentsCount', cmnt);
      // Slice 4b — photos count is anchor-aware: when an element is
      // selected we already filter the request to that anchor server-side
      // so state.photos.length is the correct number to display.
      // (state.photos is initialised inside the photo block; guard for the
      // very first updateRightTabCounts() call at boot which fires before
      // that mutation has run.)
      const ph = Array.isArray(state.photos) ? state.photos : [];
      set('rightTabPhotosCount', ph.length);
    }

    function updateBadges() {
      const newClashes = state.clashes.filter(c => c.status === 'NEW').length;
      const totalClashes = state.clashes.length;
      $('#clashBadge').textContent = totalClashes;
      $('#clashBadge').className = 'badge' + (newClashes ? '' : ' muted');
      const openIssues = state.issues.filter(i => i.status !== 'RESOLVED').length;
      $('#issueBadge').textContent = openIssues;
      $('#issueBadge').className = 'badge warn' + (openIssues ? '' : ' muted');
      const overdue = state.issues.filter(i => i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED')).length;
      $('#notifBadge').textContent = overdue;
      $('#notifBadge').className = overdue ? 'badge' : 'badge muted';
    }

    function onResize() {
      // Let the 0.18s panel transition settle, then resize. Prefer the engine's
      // ortho-aware sizeRenderer so the orthographic frustum stays correct too.
      setTimeout(() => {
        if (V.sizeRenderer) { V.sizeRenderer(); return; }
        const wrap = $('.viewport-wrap');
        if (!wrap || !V.renderer) return;
        const w = wrap.clientWidth, h = wrap.clientHeight;
        if (!V.camera.isOrthographicCamera) V.camera.aspect = Math.max(0.001, w / Math.max(1, h));
        V.camera.updateProjectionMatrix();
        V.renderer.setSize(w, h, false);
      }, 200);
    }

    // ── Hide the boot loader once the scene has a model ───────────────
    // L1 — only re-run the level / tree / chip builds once, after the
    // first model load completes; bootstrap() already populated them
    // from the element-map, but bounds-based bands need real geometry.
    let bootRan = false;
    const bootObserver = setInterval(() => {
      if (bootRan) return;
      if (V.modelRoot && !V.modelBounds.isEmpty()) {
        bootRan = true;
        $('#bootLoader')?.style.setProperty('display', 'none');
        invalidateCentroidCache();   // L7 — fresh model, fresh cache
        rebuildGuidIndex();          // B7 — GUID→mesh map for fast lookups
        loadVizState();              // B3 — restore this PROJECT's saved visualize state
        // V4 — DEFER federation co-load so the PRIMARY model is interactive first; start
        // on idle (fallback timer) rather than racing the primary's first frames.
        (window.requestIdleCallback || ((fn) => setTimeout(fn, 1500)))(() => loadFederatedModels(), { timeout: 3000 });
        placeIssuePins();
        placeClashPins();
        placePhotoPins();             // Slice 4b — photo pins after model bounds known
        const fab = $('#photoFab'); if (fab) fab.style.display = '';
        buildLevelStrip();           // re-run with real bounds for Y bands
        // Auto-fit on first model load — Revit survey-coord models can
        // have huge XY offsets (135 km in test data) which leave the
        // default camera (10,10,10) outside the model entirely. fit
        // recomputes camera near/far from bounds in the same call.
        handleHostCommand({ type: 'fit' });
        clearInterval(bootObserver);
        // B1 — GLTFLoader has consumed the blob URL, free it now to avoid
        // pinning the original GLB bytes in memory for the rest of the
        // session.
        if (state.lastBlobUrl) {
          try { URL.revokeObjectURL(state.lastBlobUrl); } catch (_) {}
          state.lastBlobUrl = null;
        }
      }
    }, 400);

    // B15 — clean up timers and blob URLs on unload.
    window.addEventListener('beforeunload', () => {
      if (state.lastBlobUrl) try { URL.revokeObjectURL(state.lastBlobUrl); } catch (_) {}
      if (presentTimer) clearInterval(presentTimer);
      if (state.heartbeatTimer) clearInterval(state.heartbeatTimer);
      // Slice 4b — release photo thumbnail blob URLs + the staged
      // capture preview if the user closes mid-capture.
      try { PHOTO_BLOB_CACHE.forEach(u => URL.revokeObjectURL(u)); PHOTO_BLOB_CACHE.clear(); } catch (_) {}
      if (pendingPhotoObjectUrl) try { URL.revokeObjectURL(pendingPhotoObjectUrl); } catch (_) {}
    });

    // First paint
    setTimeout(onResize, 100);
  }
})();
