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
    const apiBase   = window.__PLANSCAPE_API__
                   || storedApi
                   || params.get('api')
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
      selectedClashId: null,
      selectedIssueId: null,
      activeLevels: new Set(),
      levelBands: [],
      activeNav: 'orbit',
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
      photoPins: new Map(),    // Slice 4b — photoId → mesh
      photos: [],              // Slice 4b — list of SitePhotoDto rows
      photoFilters: { reason: 'any', audience: 'any' },
      photoCaptureSeed: {},
      photoReviewSelected: new Set(),
      elementMaterials: new Map(), // mesh.uuid → original material (for ghost / highlight)
      ghostMode: false,
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
    setupHeader();
    setupPanelToggles();
    setupTabs();
    setupBottomPanel();
    setupViewportOverlays();
    setupKeyboardShortcuts();
    setupKeyNav();
    setupModalHandlers();
    setupNavControls();
    setupSectionCard();
    setupHelp();
    setupHeartbeat();
    setupSelectionToolbar();
    setupRowContextMenu();
    setupPhotoCaptureModal();
    setupPhotoReviewModal();
    setupPhotoFab();
    setupPhotoRealtime();
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
          handleHostCommand({ type: 'load', payload: { url: blobUrl, transform } });
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
        onResize();
      });
      $('#btnToggleRight').addEventListener('click', () => {
        document.querySelector('.app-shell').classList.toggle('right-collapsed');
        onResize();
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
        '#vEdges':     () => toggleEdgeOverlay(),
        '#vCaps':      () => toggleSectionCaps(),
        '#vCoords':    () => toggleCoordReadout(),
        '#vExplode':   () => toggleExplodedView(),
        '#vTop':       () => setCameraPreset('top'),
        '#vFront':     () => setCameraPreset('front'),
        '#vSide':      () => setCameraPreset('right'),
        '#vIso':       () => setCameraPreset('iso'),
        '#vBmSave':    () => saveBookmark(1),
        '#vBmRestore': () => restoreBookmark(1),
      });
      bindMenu('#btnIssues', '#menuIssues', {
        '#iCreate': () => openIssueModal(),
        '#iMine':   () => { state.issuesFilter = 'mine'; switchBottomTab('issues'); renderIssues(); },
        '#iAll':    () => { state.issuesFilter = 'all'; switchBottomTab('issues'); renderIssues(); }
      });
      bindMenu('#btnMarkup', '#menuMarkup', {
        '#mkScreenshot': () => takeScreenshot(),
        '#mkShare':      () => shareCurrentView(),
        '#mkText':  () => { handleHostCommand({ type: 'startMarkup', payload: { mode: 'text'  } }); toast('Markup: click to place text'); },
        '#mkArrow': () => { handleHostCommand({ type: 'startMarkup', payload: { mode: 'arrow' } }); toast('Markup: drag to draw arrow'); },
        '#mkDraw':  () => { handleHostCommand({ type: 'startMarkup', payload: { mode: 'draw'  } }); toast('Markup: drag to draw freehand'); }
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
      V.modelRoot.traverse(obj => {
        if (!obj.isMesh) return;
        const meta = state.elementMap[obj.userData.elementGuid];
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
        state.elementMaterials.set(mesh.uuid, { original: mesh.material, replacement: null });
      }
    }
    function setReplacement(mesh, mat) {
      rememberOriginal(mesh);
      const slot = state.elementMaterials.get(mesh.uuid);
      // Dispose any previous replacement (avoids GPU leak when the same
      // mesh gets ghosted then highlighted then ghosted again).
      if (slot.replacement && typeof slot.replacement.dispose === 'function') {
        try { slot.replacement.dispose(); } catch (_) {}
      }
      slot.replacement = mat;
      mesh.material = mat;
    }
    function ghostMaterial(mesh) {
      setReplacement(mesh, new THREE_.MeshStandardMaterial({
        color: 0x888888, transparent: true, opacity: 0.12,
        depthWrite: false, side: THREE_.DoubleSide
      }));
    }
    function restoreOriginalMaterial(mesh) {
      const slot = state.elementMaterials.get(mesh.uuid);
      if (!slot) return;
      mesh.material = slot.original;
      if (slot.replacement && typeof slot.replacement.dispose === 'function') {
        try { slot.replacement.dispose(); } catch (_) {}
      }
      state.elementMaterials.delete(mesh.uuid);
    }
    // L6 — wipe every active highlight / ghost across the scene. Called
    // whenever the user focuses a different clash or issue.
    function clearAllHighlights() {
      if (!V.modelRoot) { state.elementMaterials.clear(); return; }
      const ids = Array.from(state.elementMaterials.keys());
      V.modelRoot.traverse(o => {
        if (o.isMesh && state.elementMaterials.has(o.uuid)) restoreOriginalMaterial(o);
      });
      // Anything left (e.g., disposed mesh) — drop it.
      ids.forEach(id => state.elementMaterials.delete(id));
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
        cb.addEventListener('change', () => {
          // Use per-model visibility from extras if available; fall back to full-scene toggle.
          const label = m.name || m.fileName || '';
          const extras = window.STING_VIEWER_EXTRAS;
          if (extras && extras.setModelVisible && label) {
            extras.setModelVisible(label, cb.checked);
          } else if (V.modelRoot) {
            V.modelRoot.traverse(obj => { if (obj.isMesh) obj.visible = cb.checked; });
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
        if (V.modelRoot) V.modelRoot.traverse(o => { if (o.isMesh) o.visible = true; });
        return;
      }
      const wanted = state.levelBands.filter(b => active.includes(b.level));
      if (!V.modelRoot || !wanted.length) return;
      V.modelRoot.traverse(obj => {
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
        levels: $$('.level-pill.active').map(p => p.dataset.lvl)
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
      $$('.tab-bar .tab').forEach(t => {
        t.addEventListener('click', () => {
          $$('.tab-bar .tab').forEach(x => x.classList.remove('active'));
          $$('.tab-pane').forEach(x => x.classList.remove('active'));
          t.classList.add('active');
          const pane = $('#pane-' + t.dataset.tab);
          if (pane) pane.classList.add('active');
          state.rightTab = t.dataset.tab;
          if (t.dataset.tab === 'clashes') renderRightClashes();
          if (t.dataset.tab === 'issues')  renderRightIssues();
          if (t.dataset.tab === 'photos')  { loadSitePhotos(); renderPhotos(); }
          if (t.dataset.tab === 'comments') renderComments();
          if (t.dataset.tab === 'activity') renderActivityTimeline();
        });
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
      const RESERVED = new Set(['name','category','tag','STING_TAG','discipline','system','status','level','family','type','mark','dimensions','performance']);
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

      pane.innerHTML = `
        <div class="prop-section-label">Element</div>
        <div class="prop-title">${escapeHtml(meta.name || meta.category || 'Element')}</div>
        ${tag ? `<div class="prop-section-label">STING Tag</div>
          <div class="prop-row"><span class="v mono">${escapeHtml(tag)}</span>
            <span class="copy" data-copy="${escapeHtml(tag)}" title="Copy">📋</span></div>` : ''}
        ${idCard.length ? '<div class="prop-section-label">Identity</div>' +
          idCard.map(([k, v]) => `<div class="prop-row"><span class="k">${k}</span><span class="v">${escapeHtml(v)}</span></div>`).join('') : ''}
        ${dims.length ? '<div class="prop-section-label">Dimensions</div>' +
          dims.map(([k, v]) => `<div class="prop-row"><span class="k">${k}</span><span class="v">${escapeHtml(v)}</span></div>`).join('') : ''}
        ${perfs.length ? '<div class="prop-section-label">Performance</div>' +
          perfs.map(([k, v]) => `<div class="prop-row"><span class="k">${k}</span><span class="v">${escapeHtml(v)}</span></div>`).join('') : ''}
        ${others.length ? '<div class="prop-section-label">Properties</div>' +
          others.map(([k, v]) => `<div class="prop-row"><span class="k">${escapeHtml(k)}</span><span class="v">${escapeHtml(v)}</span></div>`).join('') : ''}
        <div class="action-stack">
          <button class="btn full" id="actCreateIssue">🚩 Create issue</button>
          <button class="btn ghost full" id="actFindClashes">🔍 Find clashes for this</button>
          <button class="btn subtle full" id="actCopyTag">📋 Copy STING tag</button>
          <button class="btn subtle full" id="actLinkSheet">📌 Link to sheet</button>
        </div>
      `;
      $('#actCreateIssue', pane)?.addEventListener('click', () => openIssueModal({ guid, meta }));
      $('#actFindClashes', pane)?.addEventListener('click', () => {
        switchBottomTab('clashes'); state.selectedElementGuid = guid; renderClashes();
      });
      $('#actCopyTag', pane)?.addEventListener('click', () => copyText(tag));
      $('#actLinkSheet', pane)?.addEventListener('click', () => openSheetLinkPicker({ guid, meta }));
      $$('.copy', pane).forEach(c => c.addEventListener('click', () => copyText(c.dataset.copy)));
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
    function rebuildGuidIndex() {
      guidIndex.clear();
      if (!V.modelRoot) return;
      V.modelRoot.traverse(obj => {
        if (obj.isMesh && obj.userData.elementGuid) {
          guidIndex.set(obj.userData.elementGuid, obj);
        }
      });
    }
    function findMeshByGuid(guid) {
      if (!guid) return null;
      const cached = guidIndex.get(guid);
      if (cached) return cached;
      // Lazy fallback for the rare case where the index was built before
      // a glTF added meshes (e.g., federation deferred-load).
      if (!V.modelRoot) return null;
      let found = null;
      V.modelRoot.traverse(obj => {
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
            focusClash(c);                         // zoom + isolate the pair
            isolateClashPair(c);
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
        // Double-click → zoom + isolate the clashing pair (mirrors bottom-panel dblclick).
        card.addEventListener('dblclick', (e) => {
          if (e.target.closest('button')) return;
          focusClash(c);
          isolateClashPair(c);
        });
        $('button[data-act=view]', card).addEventListener('click', (e) => { e.stopPropagation(); focusClash(c); });
        $('button[data-act=issue]', card).addEventListener('click', (e) => { e.stopPropagation(); openIssueModal({ clash: c }); });
        pane.appendChild(card);
      });
    }

    function focusClash(c) {
      state.selectedClashId = c.id;
      clearAllHighlights();              // L6
      const pos = clashCentroid(c);
      if (pos) flyTo(pos);
      const a = findMeshByGuid(c.elementA?.guid);
      const b = findMeshByGuid(c.elementB?.guid);
      if (a) emissive(a, 0xEF4444);
      if (b) emissive(b, 0x60A5FA);
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
    function flyTo(pos) {
      const myToken = ++flyToken;
      const start = V.camera.position.clone();
      const targetCam = pos.clone().add(new THREE_.Vector3(8, 6, 8));
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

    function fitToSelection() {
      const meshes = selectedMeshes();
      if (!meshes.length) return toast('Nothing selected', 'warn');
      const bb = new THREE_.Box3();
      meshes.forEach(m => bb.expandByObject(m));
      if (bb.isEmpty()) return;
      const c = bb.getCenter(new THREE_.Vector3());
      flyTo(c);
    }

    function isolateSelection() {
      if (!V.modelRoot) return;
      const set = state.selectedElementGuids;
      if (!set.size) return toast('Nothing selected', 'warn');
      V.modelRoot.traverse(o => {
        if (!o.isMesh) return;
        o.visible = set.has(o.userData.elementGuid);
      });
      toast(`Isolated ${set.size} element${set.size === 1 ? '' : 's'}`);
    }

    function hideSelection() {
      if (!V.modelRoot) return;
      const set = state.selectedElementGuids;
      if (!set.size) return toast('Nothing selected', 'warn');
      V.modelRoot.traverse(o => {
        if (!o.isMesh) return;
        if (set.has(o.userData.elementGuid)) o.visible = false;
      });
      toast(`Hid ${set.size} element${set.size === 1 ? '' : 's'}`);
    }

    function showAllElements() {
      if (!V.modelRoot) return;
      V.modelRoot.traverse(o => { if (o.isMesh) o.visible = true; });
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
      V.modelRoot.traverse(o => {
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

    function openClashRowMenu(menu, c, x, y) {
      const aGuid = c.elementA?.guid, bGuid = c.elementB?.guid;
      showRowMenuAt(menu, [
        { glyph: '🎯', label: 'Zoom to clash',     run: () => focusClash(c) },
        { glyph: '◎',  label: 'Isolate pair',      run: () => isolateClashPair(c) },
        { glyph: '⊘',  label: 'Hide both',         run: () => {
            if (!V.modelRoot) return;
            V.modelRoot.traverse(o => { if (o.isMesh && (o.userData.elementGuid === aGuid || o.userData.elementGuid === bGuid)) o.visible = false; });
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
            V.modelRoot.traverse(o => {
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
      if (i.position) flyTo(new THREE_.Vector3(i.position.x, i.position.y, i.position.z));
      if (Array.isArray(i.elementGuids)) {
        i.elementGuids.forEach(g => {
          const m = findMeshByGuid(g); if (m) emissive(m, 0xF97316);
        });
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
      $$('.btab').forEach(t => {
        t.addEventListener('click', () => switchBottomTab(t.dataset.tab));
      });
      $('#bottomCollapse').addEventListener('click', () => {
        const bp = $('#bottomPanel');
        bp.classList.toggle('collapsed');
        bp.classList.remove('expanded');
        $('.viewport-wrap')?.classList.toggle('bp-collapsed', bp.classList.contains('collapsed'));
        onResize();
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
          $('#bottomPanel').classList.remove('resizing');
          document.removeEventListener('pointermove', onMove);
          document.removeEventListener('pointerup', onUp);
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
      $$('#clashStatusFilters .filter-btn').forEach(b => b.addEventListener('click', () => {
        $$('#clashStatusFilters .filter-btn').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        state.clashStatusFilter = b.dataset.status;
        renderClashes();
      }));
      $$('#clashTypeFilters .filter-btn').forEach(b => b.addEventListener('click', () => {
        $$('#clashTypeFilters .filter-btn').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        state.clashTypeFilter = b.dataset.type;
        renderClashes();
      }));
      $$('#issueFilters .filter-btn').forEach(b => b.addEventListener('click', () => {
        $$('#issueFilters .filter-btn').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        state.issuesFilter = b.dataset.f;
        renderIssues();
      }));
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
        const hits = ray.intersectObject(V.modelRoot, true);
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
        const hits = ray.intersectObject(V.modelRoot, true);
        if (hits.length) {
          lastClickPoint = hits[0].point.clone();
          // Focus / Pivot mode — clicking the model sets the orbit pivot
          // (ACC-style). Subsequent wheel-zoom + drag-rotate happen
          // around that point. Plain pick-mode handlers keep their job
          // (element selection, properties tab, etc.).
          if (state.activeNav === 'focus') {
            V.controls.target.copy(lastClickPoint);
            V.controls.update();
            toast('Orbit pivot set', 'success');
          }
        }
      });

      // Double-click anywhere on the model — set orbit pivot regardless
      // of the active nav mode. Mirrors the Navisworks "F" / ACC dbl-tap
      // pattern. Frame-fit on dblclick of an element bounding box is
      // routed through the engine's existing fit() command for
      // consistency with the keyboard 'F' shortcut.
      dom.addEventListener('dblclick', (e) => {
        if (!V.modelRoot) return;
        const r = dom.getBoundingClientRect();
        ptr.x = ((e.clientX - r.left) / r.width) * 2 - 1;
        ptr.y = -((e.clientY - r.top) / r.height) * 2 + 1;
        ray.setFromCamera(ptr, V.camera);
        const hits = ray.intersectObject(V.modelRoot, true);
        if (!hits.length) return;
        // Shift+dblclick → fit camera to the clicked element's bounding
        // box (instead of just setting pivot). Useful for jumping to
        // small components inside a huge model.
        if (e.shiftKey) {
          const m = hits[0].object;
          if (m && m.isMesh) {
            const bb = new THREE_.Box3().setFromObject(m);
            // Re-use the engine's fitCamera by calling it through the
            // bridge with a synthetic 'fit' command — the engine reads
            // modelBounds, so we temporarily widen it. Cleaner: call
            // fitCamera(bb) directly via the exposed STING_VIEWER if
            // available.
            try { (window.STING_VIEWER && window.STING_VIEWER.fitCamera ? window.STING_VIEWER.fitCamera : null)?.(bb); }
            catch (_) {}
            return;
          }
        }
        V.controls.target.copy(hits[0].point);
        V.controls.update();
        toast('Orbit pivot set', 'success');
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
        clearAllHighlights();
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
      // Re-paint highlights from scratch so removed elements lose their
      // emissive material.
      clearAllHighlights();
      let lastCentre = null;
      state.selectedElementGuids.forEach(g => {
        const m = findMeshByGuid(g);
        if (m) {
          emissive(m, 0xF97316);
          // R-R12 — only fly to the union centre when selection size
          // changes via tree (mode != toggle). For toggle (incremental),
          // skip the camera move so the user keeps spatial context.
          if (g === state.selectedElementGuid) {
            const bb = new THREE_.Box3().setFromObject(m);
            lastCentre = bb.getCenter(new THREE_.Vector3());
          }
        }
      });
      if (mode !== 'toggle' && lastCentre) flyTo(lastCentre);
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
      if (!canvas) return;
      const renderer = new THREE_.WebGLRenderer({ canvas, antialias: false });
      renderer.setSize(canvas.clientWidth, canvas.clientHeight);
      const cam = new THREE_.OrthographicCamera(-1, 1, 1, -1, 0.1, 1000);
      cam.up.set(0, 0, 1);

      $('#minimapToggle').addEventListener('click', () => wrap.classList.toggle('collapsed'));

      function updateMinimap() {
        if (!V.modelRoot || V.modelBounds.isEmpty()) return;
        const c = V.modelBounds.getCenter(new THREE_.Vector3());
        const s = V.modelBounds.getSize(new THREE_.Vector3());
        const pad = Math.max(s.x, s.z) * 0.55;
        cam.left = -pad; cam.right = pad; cam.top = pad; cam.bottom = -pad;
        cam.position.set(c.x, c.y + s.y * 4 + 10, c.z);
        cam.lookAt(c);
        cam.updateProjectionMatrix();
        try { renderer.render(V.scene, cam); } catch (_) {}
      }
      setInterval(updateMinimap, 250);

      canvas.addEventListener('click', (e) => {
        const r = canvas.getBoundingClientRect();
        const nx = (e.clientX - r.left) / r.width * 2 - 1;
        const ny = -((e.clientY - r.top) / r.height * 2 - 1);
        const c = V.modelBounds.getCenter(new THREE_.Vector3());
        const s = V.modelBounds.getSize(new THREE_.Vector3());
        const tgt = new THREE_.Vector3(c.x + nx * s.x * 0.5, V.controls.target.y, c.z - ny * s.z * 0.5);
        flyTo(tgt);
      });
    }

    function setupNavControls() {
      // Capture OrbitControls' default mouse-button bindings so Pan ↔ Orbit
      // toggling can restore them.
      const defaultButtons = V.controls.mouseButtons
        ? Object.assign({}, V.controls.mouseButtons)
        : null;
      // Brief visual flash for one-shot nav buttons (home / level / fit).
      function flashNavBtn(btn) {
        btn.classList.add('flash');
        setTimeout(() => btn.classList.remove('flash'), 300);
      }
      const navEl = $('#navControls');
      $$('.nav-btn').forEach(b => b.addEventListener('click', () => {
        const m = b.dataset.mode;
        // One-shot actions: fire and return without changing active mode.
        if (m === 'fit') {
          if (state.selectedElementGuids.size) fitToSelection();
          else if (V.modelBounds && !V.modelBounds.isEmpty()) {
            flyTo(V.modelBounds.getCenter(new THREE_.Vector3()));
          }
          flashNavBtn(b);
          return;
        }
        if (m === 'home') {
          // Reset to the default opening camera position (fitCamera).
          if (V.fitCamera) V.fitCamera();
          flashNavBtn(b);
          return;
        }
        if (m === 'level') {
          // Make the current view horizontal — zero pitch, keep heading.
          if (V.levelCamera) V.levelCamera();
          flashNavBtn(b);
          return;
        }
        $$('.nav-btn').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        state.activeNav = m;
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
        // Pivot = orbit camera around the selected element (or its centre).
        // Was previously labelled "focus"; we keep the underlying behaviour
        // and just expose a clearer label.
        if (m === 'pivot' && state.selectedElementGuid) {
          selectElementByGuid(state.selectedElementGuid, 'replace');
        }
      }));

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

    function setActiveTool(t) {
      handleHostCommand({ type: 'setTool', payload: { tool: t } });
    }

    function setRenderMode(mode) {
      if (!V.modelRoot) return;
      // B8 — dispose the previous replacement before swapping in a new
      // one, otherwise toggling shaded → wire → xray → ghost a few times
      // leaks N MeshStandardMaterial allocations on the GPU per cycle.
      V.modelRoot.traverse(o => {
        if (!o.isMesh) return;
        if (!o.userData._origMat) o.userData._origMat = o.material;
        const orig = o.userData._origMat;
        const prev = o.material;
        let next;
        if (mode === 'shaded') next = orig;
        else if (mode === 'wire')  next = new THREE_.MeshBasicMaterial({ color: 0x60A5FA, wireframe: true });
        else if (mode === 'xray')  next = new THREE_.MeshStandardMaterial({ color: 0xFFFFFF, transparent: true, opacity: 0.25, depthWrite: false });
        else if (mode === 'ghost') next = new THREE_.MeshStandardMaterial({ color: 0x888888, transparent: true, opacity: 0.35, depthWrite: false });
        else next = orig;
        if (prev && prev !== orig && prev !== next && typeof prev.dispose === 'function') {
          try { prev.dispose(); } catch (_) {}
        }
        o.material = next;
      });
      state.renderMode = mode;
      toast('View: ' + mode);
    }

    // Exploded view — requires a federated model. Toggles between 0 and 1.
    function toggleExplodedView() {
      const extras = window.STING_VIEWER_EXTRAS;
      if (!extras || !extras.setExplodeFactor) { toast('Explode requires a federated model', 'warn'); return; }
      state.explodeFactor = state.explodeFactor > 0 ? 0 : 1;
      extras.setExplodeFactor(state.explodeFactor);
      toast(state.explodeFactor > 0 ? 'Exploded view — click View → Explode to collapse' : 'Exploded view: collapsed');
    }

    // Edge-silhouette overlay — wireframe-on-shaded for depth perception.
    function toggleEdgeOverlay() {
      const extras = window.STING_VIEWER_EXTRAS;
      if (!extras || !extras.setEdgeOverlay) return;
      state.edgeOverlay = !state.edgeOverlay;
      extras.setEdgeOverlay(state.edgeOverlay);
      toast(state.edgeOverlay ? 'Edge overlay ON' : 'Edge overlay OFF');
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
      $('#sectionClose').addEventListener('click', () => $('#sectionCard').style.display = 'none');
      $('#sectionAddX').addEventListener('click', () => addSectionPlane('x'));
      $('#sectionAddY').addEventListener('click', () => addSectionPlane('y'));
      $('#sectionAddZ').addEventListener('click', () => addSectionPlane('z'));
      $('#sectionClear').addEventListener('click', () => clearSection());
    }

    function openSectionPlane(axis) {
      $('#sectionCard').style.display = 'block';
      addSectionPlane(axis);
    }
    function addSectionPlane(axis) {
      if (axis === 'box') {
        // Section box: 6-plane AABB clip. Slight inset so edges are visible.
        handleHostCommand({ type: 'setSectionBox', payload: { inset: 0 } });
        toast('Section box active — drag faces in the Section card to adjust');
        renderSectionCard();
        return;
      }
      handleHostCommand({ type: 'addSectionPlaneAxis', payload: { axis, offset: 0.5 } });
      renderSectionCard();
    }
    function clearSection() {
      handleHostCommand({ type: 'clearSectionPlanes' });
      handleHostCommand({ type: 'clearSectionBox' });
      $('#sectionCard').style.display = 'none';
      state.sectionPlanes = [];
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
      const NAV_KEYS = new Set([
        'ArrowLeft','ArrowRight','ArrowUp','ArrowDown','PageUp','PageDown'
      ]);
      window.addEventListener('keydown', (e) => {
        if (/INPUT|TEXTAREA|SELECT/.test(e.target.tagName)) return;
        if ($('#issueModal')?.classList.contains('open')) return;
        if (e.key === 'Shift') { shiftDown = true; return; }
        if (e.key === 'Home')  { handleHostCommand({ type: 'fit' }); e.preventDefault(); return; }
        if (NAV_KEYS.has(e.key)) {
          held.add(e.key);
          e.preventDefault();          // stop the page scrolling under us
        }
      });
      window.addEventListener('keyup', (e) => {
        if (e.key === 'Shift') { shiftDown = false; return; }
        held.delete(e.key);
      });
      // Clear held keys if the user alt-tabs away — otherwise the camera
      // drifts forever in the direction last pressed.
      window.addEventListener('blur', () => { held.clear(); shiftDown = false; });

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

          let panX = 0, panY = 0, dolly = 0, orbX = 0, orbY = 0;
          if (held.has('ArrowLeft'))  { if (shiftDown) orbX -= orbitStep; else panX -= panStep; }
          if (held.has('ArrowRight')) { if (shiftDown) orbX += orbitStep; else panX += panStep; }
          if (held.has('ArrowUp'))    { if (shiftDown) orbY -= orbitStep; else panY += panStep; }
          if (held.has('ArrowDown'))  { if (shiftDown) orbY += orbitStep; else panY -= panStep; }
          if (held.has('PageUp'))     dolly -= dollyStep;
          if (held.has('PageDown'))   dolly += dollyStep;

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
        // P = open the photo capture modal — fast keyboard shortcut for
        // coordinators who want to grab a screenshot/upload without
        // reaching for the FAB.
        if (k === 'p' || k === 'P') {
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
          handleHostCommand({ type: 'clearHighlight' });
          clearAllHighlights();          // L6
          state.selectedElementGuid = null;
          state.selectedElementGuids.clear();
          state.selectedIssueId = null;
          renderProperties(null);
          updateRightTabCounts();        // X2
          renderSelectionToolbar();
        } else if (k === 'f' || k === 'F') {
          // F = fit selection (multi-aware). With nothing selected, fit the
          // whole model. Matches the new "Fit" button in nav-controls.
          if (state.selectedElementGuids.size) fitToSelection();
          else if (V.modelBounds && !V.modelBounds.isEmpty()) {
            flyTo(V.modelBounds.getCenter(new THREE_.Vector3()));
          }
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
      // give the layout a tick to settle, then nudge the renderer
      setTimeout(() => {
        const wrap = $('.viewport-wrap');
        if (!wrap || !V.renderer) return;
        const w = wrap.clientWidth, h = wrap.clientHeight;
        V.camera.aspect = Math.max(0.001, w / Math.max(1, h));
        V.camera.updateProjectionMatrix();
        V.renderer.setSize(w, h, false);
      }, 16);
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
