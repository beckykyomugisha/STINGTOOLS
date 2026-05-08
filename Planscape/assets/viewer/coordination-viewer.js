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
      clashesFilter: 'all',
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
    async function api(path, opts = {}) {
      if (!apiEnabled) return null;        // C2 — short-circuit on file:// / RN
      const headers = Object.assign({ 'Content-Type': 'application/json' }, opts.headers || {});
      if (token) headers['Authorization'] = `Bearer ${token}`;
      try {
        const res = await fetch(`${apiBase}${path}`, Object.assign({}, opts, { headers }));
        if (res.status === 401 && !authChallenged) {
          authChallenged = true;
          toast('Sign-in expired — please log in again', 'error');
          // Don't auto-redirect; viewer can be embedded.
        }
        if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
        const ct = res.headers.get('content-type') || '';
        return ct.includes('application/json') ? res.json() : res.text();
      } catch (err) {
        console.warn('[coord] api', path, err.message);
        return null;
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
    setupModalHandlers();
    setupNavControls();
    setupSectionCard();
    setupHelp();
    renderProperties(null);
    renderHistory();
    updateBadges();

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
      if (projectId && modelId) {
        const fileUrl = `${apiBase}/api/projects/${projectId}/models/${modelId}/file${token ? `?access_token=${encodeURIComponent(token)}` : ''}`;
        handleHostCommand({ type: 'load', payload: { url: fileUrl } });
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
      const disciplines = new Set(['ARCH','STR','MECH','ELEC','PLMB','FIRE']);
      Object.values(state.elementMap || {}).forEach(m => {
        if (m && m.discipline) disciplines.add(String(m.discipline).toUpperCase().slice(0, 4));
      });
      const wrap = $('#discChips');
      wrap.innerHTML = '';
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
            const sysKids = items.slice(0, 200).map(it => buildNode(it.name, [], null, true, it));
            discChildren.push(buildNode(sys, sysKids, items.length));
            discCount += items.length;
          });
          lvlChildren.push(buildNode(disc, discChildren, discCount));
          lvlCount += discCount;
        });
        root.appendChild(buildNode(lvl, lvlChildren, lvlCount));
      });

      $('#treeSearch').addEventListener('input', (e) => {
        const q = e.target.value.trim().toLowerCase();
        $$('.tree-node', root).forEach(n => {
          const txt = n.textContent.toLowerCase();
          const match = !q || txt.includes(q);
          n.style.display = match ? '' : 'none';
          if (q && match) { n.classList.remove('closed'); n.classList.add('open'); }
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
        });
      });
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
      const dim = meta.dimensions || {};
      const perf = meta.performance || {};
      const idCard = [
        ['Discipline', meta.discipline],
        ['System',     meta.system],
        ['Status',     meta.status],
        ['Level',      meta.level],
        ['Family',     meta.family],
        ['Type',       meta.type],
        ['Mark',       meta.mark]
      ].filter(([, v]) => v != null && v !== '');
      const dims = Object.entries(dim).filter(([, v]) => v != null);
      const perfs = Object.entries(perf).filter(([, v]) => v != null);

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

    function copyText(t) {
      if (!t) return;
      navigator.clipboard?.writeText(t).then(
        () => toast('Copied: ' + t, 'success'),
        () => toast('Copy failed', 'error')
      );
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
      const out = [];
      for (let i = 1; i <= 12; i++) {
        const a = pick(), b = pick();
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
      // Clear existing
      state.clashPins.forEach(m => V.scene.remove(m));
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
        V.scene.add(wire);
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

    function findMeshByGuid(guid) {
      if (!guid || !V.modelRoot) return null;
      let found = null;
      V.modelRoot.traverse(obj => {
        if (!found && obj.isMesh && obj.userData.elementGuid === guid) found = obj;
      });
      return found;
    }

    function renderClashes() {
      // Bottom-panel table
      const body = $('#clashesBody');
      const filter = state.clashesFilter;
      let rows = state.clashes;
      if (filter === 'new')      rows = rows.filter(c => c.status === 'NEW');
      else if (filter === 'open') rows = rows.filter(c => c.status === 'OPEN');
      else if (filter === 'resolved') rows = rows.filter(c => c.status === 'RESOLVED');
      else if (filter === 'hard') rows = rows.filter(c => c.type === 'HARD');
      else if (filter === 'soft') rows = rows.filter(c => c.type === 'SOFT');

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
          $('button[data-act=view]', tr).addEventListener('click', () => focusClash(c));
          $('button[data-act=issue]', tr).addEventListener('click', () => openIssueModal({ clash: c }));
          tbody.appendChild(tr);
        });
        body.appendChild(table);
      }
      $('#clashesFilterCount').textContent = rows.length;
      $('#clashesTotal').textContent = state.clashes.length;
      $('#clashesNew').textContent = state.clashes.filter(c => c.status === 'NEW').length;
      $('#clashesOpen').textContent = state.clashes.filter(c => c.status === 'OPEN').length;
      $('#clashesResolved').textContent = state.clashes.filter(c => c.status === 'RESOLVED').length;
      renderRightClashes();
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
        $('button[data-act=view]', card).addEventListener('click', () => focusClash(c));
        $('button[data-act=issue]', card).addEventListener('click', () => openIssueModal({ clash: c }));
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
      state.issuePins.forEach(m => V.scene.remove(m));
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
        V.scene.add(sphere);
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
      $('#issuesMine').textContent = state.issues.filter(i => i.assigneeId === 'me').length;
      $('#issuesOverdue').textContent = state.issues.filter(i => i.slaBreached || (i.dueDate && new Date(i.dueDate) < new Date() && i.status !== 'RESOLVED')).length;
      renderRightIssues();
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
        $('button[data-act=view]', card)?.addEventListener('click', () => focusIssue(i));
        $('button[data-act=resolve]', card)?.addEventListener('click', () => updateIssue(i.id, { status: 'RESOLVED' }));
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
      $('#imClose').addEventListener('click', () => $('#issueModal').classList.remove('open'));
      $('#imCancel').addEventListener('click', () => $('#issueModal').classList.remove('open'));
      $('#issueModal').addEventListener('click', (e) => {
        if (e.target.id === 'issueModal') $('#issueModal').classList.remove('open');
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
        const b64 = V.renderer.domElement.toDataURL('image/png');
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

      $$('#clashFilters .filter-btn').forEach(b => b.addEventListener('click', () => {
        $$('#clashFilters .filter-btn').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        state.clashesFilter = b.dataset.f;
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
      $('#bottomPanel').classList.remove('collapsed');
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
      const blob = new Blob([rows.map(r => r.map(x => `"${String(x ?? '').replace(/"/g, '""')}"`).join(',')).join('\n')], { type: 'text/csv' });
      const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = name; a.click();
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
        const hits = ray.intersectObject(V.modelRoot, true);
        if (hits.length) lastClickPoint = hits[0].point.clone();
      });
      // Click pin → handle
      dom.addEventListener('click', (e) => {
        const r = dom.getBoundingClientRect();
        ptr.x = ((e.clientX - r.left) / r.width) * 2 - 1;
        ptr.y = -((e.clientY - r.top) / r.height) * 2 + 1;
        ray.setFromCamera(ptr, V.camera);
        const targets = [];
        state.issuePins.forEach(m => targets.push(m));
        state.clashPins.forEach(m => targets.push(m));
        const hits = ray.intersectObjects(targets, false);
        if (hits.length) {
          const u = hits[0].object.userData;
          if (u.issueId) {
            const i = state.issues.find(x => x.id === u.issueId);
            if (i) focusIssue(i);
          } else if (u.clashId) {
            const c = state.clashes.find(x => x.id === u.clashId);
            if (c) focusClash(c);
          }
        }
      });

      window.addEventListener('resize', onResize);
      setupMinimap();
      // Hook the original 'pick' events through to our properties panel.
      const origSend = V.bridge.send;
      V.bridge.send = function (type, payload) {
        if (type === 'pick' && payload && payload.guid) {
          state.selectedElementGuid = payload.guid;
          renderProperties(payload.guid);
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
      $$('.nav-btn').forEach(b => b.addEventListener('click', () => {
        $$('.nav-btn').forEach(x => x.classList.remove('active'));
        b.classList.add('active');
        const m = b.dataset.mode;
        state.activeNav = m;
        if (m === 'walk') {
          handleHostCommand({ type: 'setWalkthrough', payload: { enabled: true } });
        } else {
          handleHostCommand({ type: 'setWalkthrough', payload: { enabled: false } });
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
      V.modelRoot.traverse(o => {
        if (!o.isMesh) return;
        if (!o.userData._origMat) o.userData._origMat = o.material;
        const orig = o.userData._origMat;
        if (mode === 'shaded') {
          o.material = orig;
        } else if (mode === 'wire') {
          o.material = new THREE_.MeshBasicMaterial({ color: 0x60A5FA, wireframe: true });
        } else if (mode === 'xray') {
          o.material = new THREE_.MeshStandardMaterial({ color: 0xFFFFFF, transparent: true, opacity: 0.25, depthWrite: false });
        } else if (mode === 'ghost') {
          o.material = new THREE_.MeshStandardMaterial({ color: 0x888888, transparent: true, opacity: 0.35, depthWrite: false });
        }
      });
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
    function setupKeyboardShortcuts() {
      document.addEventListener('keydown', (e) => {
        if (/INPUT|TEXTAREA|SELECT/.test(e.target.tagName)) return;
        const k = e.key;
        if (k === 'Escape') {
          $('#issueModal').classList.remove('open');
          $('.help-overlay').classList.remove('open');
          handleHostCommand({ type: 'clearHighlight' });
          clearAllHighlights();          // L6
          state.selectedElementGuid = null; renderProperties(null);
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

    function takeScreenshot() {
      const b64 = V.renderer.domElement.toDataURL('image/png');
      const a = document.createElement('a'); a.href = b64; a.download = `${state.modelName || 'view'}-${Date.now()}.png`; a.click();
      toast('Screenshot saved', 'success');
    }
    function shareCurrentView() {
      const url = `${location.origin}${location.pathname}?project=${projectId}&model=${modelId}`;
      navigator.clipboard?.writeText(url);
      toast('View URL copied to clipboard', 'success');
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
        placeIssuePins();
        placeClashPins();
        buildLevelStrip();           // re-run with real bounds for Y bands
        clearInterval(bootObserver);
      }
    }, 400);

    // First paint
    setTimeout(onResize, 100);
  }
})();
