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
    const apiBase   = window.__PLANSCAPE_API__ || 'http://localhost:5000';
    const token     = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_token')) || '';

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
      selectedElementGuid: null,
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
          // U8 — toast immediately, then redirect to / (the office dashboard's
          // login overlay) after a short grace window. Embedders pass
          // ?embed=1 to keep the viewer mounted and re-auth themselves.
          toast('Sign-in expired — redirecting to login…', 'error');
          if (typeof localStorage !== 'undefined') {
            try { localStorage.removeItem('planscape_token'); } catch (_) {}
          }
          if (!embedMode) {
            const next = encodeURIComponent(location.pathname + location.search);
            setTimeout(() => { location.href = `${apiBase}/?next=${next}`; }, 1500);
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
      if (projectId && modelId) {
        const fileUrl = `${apiBase}/api/projects/${projectId}/models/${modelId}/file`;
        const headers = {};
        if (token) headers['Authorization'] = `Bearer ${token}`;
        const tenantId = (typeof localStorage !== 'undefined' && localStorage.getItem('planscape_tenant')) || state.tenantId;
        if (tenantId) headers['X-Tenant'] = tenantId;
        const ctl = new AbortController();
        const t = setTimeout(() => ctl.abort(), 60000);
        try {
          const res = await fetch(fileUrl, { headers, cache: 'no-store', signal: ctl.signal });
          if (res.status === 401 && !authChallenged) {
            authChallenged = true;
            toast('Sign-in expired — redirecting to login…', 'error');
            try { localStorage.removeItem('planscape_token'); } catch (_) {}
            if (!embedMode) {
              const next = encodeURIComponent(location.pathname + location.search);
              setTimeout(() => { location.href = `${apiBase}/?next=${next}`; }, 1500);
            }
            return;
          }
          if (!res.ok) {
            // The server returns 404 + JSON {error:"storage_missing"} when
            // the ProjectModel row exists but its GLB has been wiped from
            // disk (typical cause: container rebuild without the
            // persistent storage volume mount). Surface that as an
            // actionable CTA instead of the generic "Failed to load
            // model file" toast that gives coordinators no recovery
            // path.
            let body = null;
            try { body = await res.clone().json(); } catch (_) {}
            if (res.status === 404 && body && body.error === 'storage_missing') {
              const msg =
                "Model bytes missing on the server — please republish from Revit " +
                "(BIM tab → Publish Model → 'Replace existing' or 'Auto').";
              toast(msg, 'error');
              console.warn('[coord] storage_missing for model', modelId, body);
              $('#bootLoader')?.style.setProperty('display', 'none');
              return;
            }
            throw new Error(`${res.status} ${res.statusText}`);
          }
          const blob = await res.blob();
          const blobUrl = URL.createObjectURL(blob);
          state.lastBlobUrl = blobUrl;
          handleHostCommand({ type: 'load', payload: { url: blobUrl } });
        } catch (err) {
          const aborted = err && err.name === 'AbortError';
          console.warn('[coord] GLB fetch failed', aborted ? 'timeout' : err.message);
          toast(aborted ? 'Model download timed out — retry?' : 'Failed to load model file', 'error');
          $('#bootLoader')?.style.setProperty('display', 'none');
        } finally {
          clearTimeout(t);
        }
      }

      // Issues + clashes
      await loadIssues();
      await loadClashes();
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
        '#mAngle': () => { setActiveTool('measure'); toast('Angle: pick three points'); },
        '#mClear': () => { handleHostCommand({ type: 'clearMeasure' }); }
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
        '#vGhost':     () => setRenderMode('ghost')
      });
      bindMenu('#btnIssues', '#menuIssues', {
        '#iCreate': () => openIssueModal(),
        '#iMine':   () => { state.issuesFilter = 'mine'; switchBottomTab('issues'); renderIssues(); },
        '#iAll':    () => { state.issuesFilter = 'all'; switchBottomTab('issues'); renderIssues(); }
      });
      bindMenu('#btnMarkup', '#menuMarkup', {
        '#mkScreenshot': () => takeScreenshot(),
        '#mkShare':      () => shareCurrentView(),
        '#mkText':       () => toast('Markup: text — coming next', 'warn'),
        '#mkArrow':      () => toast('Markup: arrow — coming next', 'warn'),
        '#mkDraw':       () => toast('Markup: freehand — coming next', 'warn')
      });

      $('#btnClashes').addEventListener('click', () => switchBottomTab('clashes'));
      $('#btnIssueBadge').addEventListener('click', () => switchBottomTab('issues'));
      $('#btnHelp').addEventListener('click', () => $('.help-overlay').classList.add('open'));
      $('#btnSettings').addEventListener('click', () => toast('Settings — TODO'));
      $('#btnNotifs').addEventListener('click', () => toast(`${state.issues.filter(i => i.status !== 'RESOLVED').length} open issues`));
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
          // No multi-model federation in the basic flow — ghost / show all matching IDs
          if (V.modelRoot) {
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
          row.addEventListener('click', () => selectElementByGuid(payload.guid));
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
    $('#btnAddView').addEventListener('click', () => {
      const name = prompt('Saved view name', `View ${state.savedViews.length + 1}`);
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
          if (t.dataset.tab === 'comments') renderComments();
        });
      });
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
    function renderProperties(guid) {
      const pane = $('#pane-properties');
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
      $('#actLinkSheet', pane)?.addEventListener('click', () => toast('Sheet link — TODO', 'warn'));
      $$('.copy', pane).forEach(c => c.addEventListener('click', () => copyText(c.dataset.copy)));
    }

    // U7 — clipboard.writeText is unavailable in non-secure contexts
    // (file:// inside RN WebView, http:// in older browsers). Fall back
    // to the historic textarea + execCommand("copy") trick so the Copy
    // STING Tag / Share view link buttons keep working there too.
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
          const tr = el('tr', { 'data-id': c.id });
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
        const card = el('div', { class: `coord-card ${c.type.toLowerCase()}` });
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

    function renderIssues() {
      const body = $('#issuesBody');
      let rows = state.issues;
      // L2 — match against the real signed-in user id, with 'me' as fallback
      // for offline / pre-auth state so the placeholder demo data still works.
      const myId = state.currentUser?.id || state.currentUser?.userId || 'me';
      if (state.issuesFilter === 'mine') rows = rows.filter(i => i.assigneeId === myId || i.assigneeId === 'me');
      else if (state.issuesFilter === 'overdue') rows = rows.filter(i => i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED'));
      body.innerHTML = rows.length ? '' : '<div class="empty-state">No issues</div>';
      if (rows.length) {
        const table = el('table', { class: 'dtable' });
        table.innerHTML = `<thead><tr>
          <th>ID</th><th>Title</th><th>Priority</th><th>Assignee</th>
          <th>Due</th><th>Status</th><th>SLA</th>
        </tr></thead><tbody></tbody>`;
        const tbody = $('tbody', table);
        rows.forEach(i => {
          const tr = el('tr', { 'data-id': i.id });
          const priority = i.priority || 'MEDIUM';
          const overdue = i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED');
          tr.innerHTML = `
            <td>${escapeHtml(i.code || i.id?.slice(0, 8))}</td>
            <td>${escapeHtml(i.title || '')}</td>
            <td><span class="tag ${priority}">${priority}</span></td>
            <td>${escapeHtml(i.assigneeName || '—')}</td>
            <td>${i.dueDate ? new Date(i.dueDate).toLocaleDateString() : '—'}</td>
            <td><span class="tag ${(i.status || 'NEW').toLowerCase()}">${i.status || 'NEW'}</span></td>
            <td>${overdue ? '<span class="tag overdue">OVERDUE</span>' : '—'}</td>
          `;
          tr.addEventListener('click', () => focusIssue(i));
          tbody.appendChild(tr);
        });
        body.appendChild(table);
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
        const card = el('div', { class: `coord-card priority-${i.priority || 'MEDIUM'} ${i.status === 'RESOLVED' ? 'resolved' : ''}` });
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
    function openIssueModal(seed = {}) {
      const modal = $('#issueModal');
      modal.classList.add('open');
      $('#imTitle').value = '';
      $('#imDesc').value  = '';
      $('#imScreenshot').innerHTML = '';
      $('#imScreenshot').dataset.b64 = '';
      // priority default
      $$('#imPriority .choice').forEach(c => c.classList.toggle('active', c.dataset.v === 'HIGH'));
      $$('#imType .choice').forEach(c => c.classList.toggle('active', c.dataset.v === 'RFI'));
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
      }
      modal.dataset.linked = JSON.stringify(linked);
      renderLinkedElements(linked);

      // member dropdown
      const sel = $('#imAssignee');
      sel.innerHTML = '<option value="">— Unassigned —</option>' +
        state.members.map(m => `<option value="${m.id}">${escapeHtml(m.name)}</option>`).join('');

      // B12 — focus the title field so the user can start typing
      // immediately, and remember the previously focused element so we
      // can restore it on close.
      modal.dataset.open = '1';
      modal.dataset.returnFocus = '';
      const prev = document.activeElement;
      if (prev && prev !== document.body) modal.dataset.returnFocusId = prev.id || '';
      setTimeout(() => $('#imTitle')?.focus(), 30);
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
      const linked = JSON.parse($('#issueModal').dataset.linked || '[]');
      const priority = $('#imPriority .choice.active')?.dataset.v || 'MEDIUM';
      const type     = $('#imType .choice.active')?.dataset.v || 'RFI';
      const payload = {
        title: $('#imTitle').value.trim(),
        priority, type,
        elementGuids: linked.map(l => l.guid),
        assigneeId: $('#imAssignee').value || null,
        dueDate: $('#imDue').value || null,
        description: $('#imDesc').value,
        screenshotBase64: $('#imScreenshot').dataset.b64 || null,
        position: lastClickPoint ? { x: lastClickPoint.x, y: lastClickPoint.y, z: lastClickPoint.z } : undefined
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
        status: 'NEW',
        slaBreached: false
      }, payload);
      state.issues.unshift(created);
      placeIssuePins(); renderIssues(); updateBadges();
      $('#issueModal').classList.remove('open');
      toast(`Issue ${created.code || created.id} created`, 'success');
      logHistory(`Created ${created.code || 'issue'}`);
    }

    // ── Bottom panel ───────────────────────────────────────────────────
    function setupBottomPanel() {
      $$('.btab').forEach(t => {
        t.addEventListener('click', () => switchBottomTab(t.dataset.tab));
      });
      $('#bottomCollapse').addEventListener('click', () => {
        const bp = $('#bottomPanel');
        bp.classList.toggle('collapsed');
        $('.viewport-wrap')?.classList.toggle('bp-collapsed', bp.classList.contains('collapsed'));
        onResize();
      });
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
        if (type === 'pick' && payload && payload.guid) {
          state.selectedElementGuid = payload.guid;
          renderProperties(payload.guid);
          updateRightTabCounts();          // X2
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
          }
        }
        return origSend.call(V.bridge, type, payload);
      };
    }

    function selectElementByGuid(guid) {
      state.selectedElementGuid = guid;
      clearAllHighlights();              // L6
      const m = findMeshByGuid(guid);
      if (m) {
        const bb = new THREE_.Box3().setFromObject(m);
        flyTo(bb.getCenter(new THREE_.Vector3()));
        emissive(m, 0xF97316);
      }
      renderProperties(guid);
      updateRightTabCounts();            // X2
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
      // Fit View — action button, NOT a mode. Don't change active state.
      $('#btnFitView')?.addEventListener('click', () => {
        handleHostCommand({ type: 'fit' });
        toast('Zoomed to fit', 'success');
      });
      $$('.nav-btn[data-mode]').forEach(b => b.addEventListener('click', () => {
        $$('.nav-btn[data-mode]').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        const m = b.dataset.mode;
        state.activeNav = m;
        // Walk mode delegates to viewer-extras' first-person controls.
        handleHostCommand({ type: 'setWalkthrough', payload: { enabled: m === 'walk' } });
        // Polish — Pan mode rebinds left mouse to PAN so coordinators can
        // shove the model around with one finger / left-drag without
        // remembering the right-click pan modifier. Orbit restores defaults.
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
        if (m === 'focus' && state.selectedElementGuid) {
          selectElementByGuid(state.selectedElementGuid);
        }
      }));
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
      const normals = { x: [1, 0, 0], y: [0, 1, 0], z: [0, 0, 1], free: [0, 1, 0] };
      const n = normals[axis] || normals.y;
      handleHostCommand({ type: 'setSectionPlane', payload: { enabled: true, normal: n, offset: 0.5 } });
    }
    function clearSection() {
      handleHostCommand({ type: 'setSectionPlane', payload: { enabled: false } });
      $('#sectionCard').style.display = 'none';
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
        if (k === 'Escape') {
          // R7 — help overlay swallows Esc so closing it doesn't ALSO
          // wipe the user's selection / highlights.
          const help = $('.help-overlay');
          if (help && help.classList.contains('open')) {
            help.classList.remove('open');
            return;
          }
          handleHostCommand({ type: 'clearHighlight' });
          clearAllHighlights();          // L6
          state.selectedElementGuid = null;
          state.selectedIssueId = null;
          renderProperties(null);
          updateRightTabCounts();        // X2
        } else if (k === 'f' || k === 'F') {
          if (state.selectedElementGuid) selectElementByGuid(state.selectedElementGuid);
        } else if (k === 'h' || k === 'H') {
          const m = findMeshByGuid(state.selectedElementGuid); if (m) m.visible = !m.visible;
        } else if (k === 'i' || k === 'I') {
          if (!V.modelRoot) return;
          const g = state.selectedElementGuid; if (!g) return;
          V.modelRoot.traverse(o => { if (o.isMesh) o.visible = (o.userData.elementGuid === g); });
        } else if (k === 'a' || k === 'A') {
          if (!V.modelRoot) return;
          V.modelRoot.traverse(o => { if (o.isMesh) o.visible = true; });
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
    });

    // First paint
    setTimeout(onResize, 100);
  }
})();
