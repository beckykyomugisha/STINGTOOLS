// MODEL-VIEWER extras — federation, walkthrough, oblique sections,
// section box, multi-plane clipping, per-model toggle, exploded view,
// markup canvas, area + volume measure, auto-LOD.
// Loaded *after* viewer.html's main IIFE so it can hook STING_VIEWER.
(() => {
  'use strict';
  const ext = {};
  window.STING_VIEWER_EXTRAS = ext;

  function host() { return window.STING_VIEWER; }

  // ── Federation: load multiple GLBs into one scene ─────────────────────
  // Track each source's sub-group for per-model visibility + explode.
  const federationModels = []; // [{ label, discipline, group }]

  ext.loadFederation = function (sources) {
    const h = host();
    if (!h || typeof THREE === 'undefined' || !THREE.GLTFLoader) return;
    if (h.federationGroup) h.scene.remove(h.federationGroup);
    federationModels.length = 0;
    const group = new THREE.Group();
    h.federationGroup = group;
    h.scene.add(group);

    const loader = new THREE.GLTFLoader();
    if (h.dracoLoader && loader.setDRACOLoader) loader.setDRACOLoader(h.dracoLoader);
    if (h.meshoptDecoder && loader.setMeshoptDecoder) loader.setMeshoptDecoder(h.meshoptDecoder);

    let remaining = sources.length;
    const merged = new THREE.Box3();
    sources.forEach((src) => {
      loader.load(src.url, (gltf) => {
        gltf.scene.userData.federationLabel = src.label || '';
        gltf.scene.userData.federationDiscipline = src.discipline || '';
        h.assignElementIds(gltf.scene);
        group.add(gltf.scene);
        federationModels.push({ label: src.label || '', discipline: src.discipline || '', group: gltf.scene });
        merged.expandByObject(gltf.scene);
        if (--remaining === 0) {
          h.modelRoot = group;
          h.modelBounds = merged;
          // fitCamera() now sets camera.up based on dominant vertical axis,
          // so no separate up-axis sync is needed here.
          h.fitCamera();
          h.bridge.send('loaded', {
            elementCount: countMeshes(group),
            bounds: bbToArray(merged),
            federation: sources.map((s) => ({ label: s.label, discipline: s.discipline })),
          });
        }
      }, undefined, (err) => {
        if (--remaining === 0) host().bridge.send('federationError', { url: src.url, error: String(err) });
      });
    });
  };

  // ── Per-model visibility toggle ────────────────────────────────────────
  ext.setModelVisible = function (label, visible) {
    const entry = federationModels.find(m => m.label === label || m.label === String(label));
    if (entry) entry.group.visible = visible;
  };

  ext.getFederationModels = function () {
    return federationModels.map(m => ({
      label: m.label, discipline: m.discipline,
      visible: m.group.visible,
      opacity: m._opacity ?? 1,
    }));
  };

  // ── Per-model opacity (federation overlay fade) ───────────────────────
  // Sets opacity for every mesh in the labelled sub-group. Stash the
  // original material once so we can restore opaque rendering cleanly.
  ext.setModelOpacity = function (label, opacity) {
    const entry = federationModels.find(m => m.label === label || m.label === String(label));
    if (!entry) return;
    const a = Math.max(0, Math.min(1, opacity));
    entry._opacity = a;
    entry.group.traverse(o => {
      if (!o.isMesh || !o.material) return;
      // Materials may be shared across meshes; clone on first dim so we
      // don't bleed transparency into other federated models.
      if (!o.userData._opacityClone) {
        o.material = o.material.clone();
        o.userData._opacityClone = true;
      }
      o.material.transparent = a < 1;
      o.material.opacity = a;
      o.material.depthWrite = a >= 0.99;
      o.material.needsUpdate = true;
    });
  };

  // ── Solo / isolate one model (hide all others) ────────────────────────
  // Pass null/undefined to clear and re-show everything.
  ext.setModelSolo = function (label) {
    if (label == null) {
      federationModels.forEach(m => { m.group.visible = true; });
      return;
    }
    federationModels.forEach(m => {
      m.group.visible = (m.label === label || m.label === String(label));
    });
  };

  // ── Isolate by element guid set ────────────────────────────────────────
  // Hide every mesh whose userData.elementGuid is NOT in the given set.
  // Used by coordination-viewer's tree multi-select and clash-pair focus.
  let _isolatedSet = null;
  ext.setIsolatedGuids = function (guids) {
    const h = host(); if (!h || !h.modelRoot) return;
    _isolatedSet = (guids && guids.length) ? new Set(guids) : null;
    h.modelRoot.traverse(o => {
      if (!o.isMesh) return;
      if (_isolatedSet == null) {
        // Restore original visibility flag stored on first isolation.
        if (o.userData._origVisible != null) {
          o.visible = o.userData._origVisible;
          delete o.userData._origVisible;
        }
        return;
      }
      if (o.userData._origVisible == null) o.userData._origVisible = o.visible;
      o.visible = _isolatedSet.has(o.userData.elementGuid);
    });
  };
  ext.clearIsolation = function () { ext.setIsolatedGuids(null); };

  // ── Multi-plane section management ───────────────────────────────────
  // Each entry: { plane: THREE.Plane, axis, offset (0..1), id }
  let sectionPlaneEntries = [];
  let sectionPlaneIdCounter = 0;

  function syncClippingPlanes() {
    const h = host(); if (!h) return;
    h.renderer.clippingPlanes = sectionPlaneEntries.map(e => e.plane);
  }

  function buildPlane(axis, offset) {
    const h = host(); if (!h) return null;
    const normals = { x: [1,0,0], y: [0,1,0], z: [0,0,1], free: [0,1,0] };
    const n = new THREE.Vector3(...(normals[axis] || normals.y)).normalize();
    const sz = h.modelBounds.getSize(new THREE.Vector3());
    const c  = h.modelBounds.getCenter(new THREE.Vector3());
    const span = Math.abs(n.x)*sz.x + Math.abs(n.y)*sz.y + Math.abs(n.z)*sz.z;
    const o = (offset == null ? 0.5 : offset) - 0.5;
    const constant = -n.dot(c) + o * span;
    return new THREE.Plane(n, constant);
  }

  // Backwards-compatible single-plane API (replaces all current planes with one).
  ext.setSectionPlane = function ({ normal, offset, enabled }) {
    const h = host(); if (!h) return;
    if (!enabled) { sectionPlaneEntries = []; h.renderer.clippingPlanes = []; return; }
    const n = (normal && normal.length === 3)
      ? new THREE.Vector3(normal[0], normal[1], normal[2]).normalize()
      : new THREE.Vector3(0, -1, 0);
    if (n.lengthSq() < 1e-9) n.set(0, -1, 0);
    const sz = h.modelBounds.getSize(new THREE.Vector3());
    const c  = h.modelBounds.getCenter(new THREE.Vector3());
    const span = Math.max(sz.x, sz.y, sz.z);
    const o = (offset == null ? 0.5 : offset) - 0.5;
    const constant = -n.dot(c) + o * span;
    const plane = new THREE.Plane(n, constant);
    sectionPlaneEntries = [{ plane, axis: 'free', offset: offset ?? 0.5, id: ++sectionPlaneIdCounter }];
    h.renderer.clippingPlanes = [plane];
  };

  // Add a new section plane (returns id for later removal/update).
  ext.addSectionPlaneAxis = function (axis, offset) {
    const plane = buildPlane(axis, offset ?? 0.5);
    if (!plane) return -1;
    const id = ++sectionPlaneIdCounter;
    sectionPlaneEntries.push({ plane, axis, offset: offset ?? 0.5, id });
    syncClippingPlanes();
    host()?.bridge.send('sectionPlanesChanged', { count: sectionPlaneEntries.length, planes: sectionPlaneEntries.map(e => ({ id: e.id, axis: e.axis, offset: e.offset })) });
    return id;
  };

  // Update an existing plane's offset (0..1 across bounds).
  ext.updateSectionPlane = function (id, offset) {
    const entry = sectionPlaneEntries.find(e => e.id === id);
    if (!entry) return;
    const newPlane = buildPlane(entry.axis, offset);
    if (!newPlane) return;
    entry.plane.copy(newPlane);
    entry.offset = offset;
    syncClippingPlanes();
  };

  // Remove a single plane by id.
  ext.removeSectionPlane = function (id) {
    sectionPlaneEntries = sectionPlaneEntries.filter(e => e.id !== id);
    syncClippingPlanes();
  };

  // Clear all section planes.
  ext.clearSectionPlanes = function () {
    sectionPlaneEntries = [];
    const h = host(); if (h) h.renderer.clippingPlanes = [];
  };

  ext.getSectionPlanes = function () {
    return sectionPlaneEntries.map(e => ({ id: e.id, axis: e.axis, offset: e.offset }));
  };

  // ── Section box: 6-plane AABB clip ───────────────────────────────────
  // Box is defined by [minX, maxX, minY, maxY, minZ, maxZ] offsets (model units).
  // After setSectionBox(), call updateSectionBoxFace(faceIdx, offset) to slide faces.
  // Face indices: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
  const BOX_NORMALS = [
    new THREE.Vector3( 1, 0, 0), new THREE.Vector3(-1, 0, 0),
    new THREE.Vector3( 0, 1, 0), new THREE.Vector3( 0,-1, 0),
    new THREE.Vector3( 0, 0, 1), new THREE.Vector3( 0, 0,-1),
  ];
  let sectionBoxPlaneIds = []; // ids of the 6 box planes in sectionPlaneEntries

  ext.setSectionBox = function ({ inset } = {}) {
    const h = host(); if (!h) return;
    // Remove any previous box planes.
    sectionBoxPlaneIds.forEach(id => { sectionPlaneEntries = sectionPlaneEntries.filter(e => e.id !== id); });
    sectionBoxPlaneIds = [];

    const i = inset ?? 0;
    const min = h.modelBounds.min, max = h.modelBounds.max;
    const sx = (max.x - min.x) * i, sy = (max.y - min.y) * i, sz = (max.z - min.z) * i;
    const constants = [
      -(min.x + sx),   max.x - sx,
      -(min.y + sy),   max.y - sy,
      -(min.z + sz),   max.z - sz,
    ];

    constants.forEach((c, faceIdx) => {
      const plane = new THREE.Plane(BOX_NORMALS[faceIdx].clone(), c);
      const id = ++sectionPlaneIdCounter;
      sectionPlaneEntries.push({ plane, axis: `box${faceIdx}`, offset: 0, id });
      sectionBoxPlaneIds.push(id);
    });

    syncClippingPlanes();
    h.bridge.send('sectionBoxSet', { faces: sectionBoxPlaneIds });
  };

  // Slide a section box face. offset in model-space units (millimetres for Revit models).
  ext.updateSectionBoxFace = function (faceIdx, offsetMm) {
    if (faceIdx < 0 || faceIdx >= sectionBoxPlaneIds.length) return;
    const id = sectionBoxPlaneIds[faceIdx];
    const entry = sectionPlaneEntries.find(e => e.id === id);
    if (!entry) return;
    entry.plane.constant = offsetMm;
    syncClippingPlanes();
  };

  ext.clearSectionBox = function () {
    sectionBoxPlaneIds.forEach(id => { sectionPlaneEntries = sectionPlaneEntries.filter(e => e.id !== id); });
    sectionBoxPlaneIds = [];
    syncClippingPlanes();
  };

  // ── Exploded view ─────────────────────────────────────────────────────
  // factor=0: collapsed (original positions). factor=1: fully exploded.
  ext.setExplodeFactor = function (factor) {
    const h = host(); if (!h || !h.federationGroup) return;
    const center = h.modelBounds.getCenter(new THREE.Vector3());
    const scale  = h.modelBounds.getSize(new THREE.Vector3()).length() * 0.18 * factor;
    h.federationGroup.children.forEach(subGroup => {
      if (factor === 0) { subGroup.position.set(0, 0, 0); return; }
      const bb = new THREE.Box3().setFromObject(subGroup);
      if (bb.isEmpty()) { subGroup.position.set(0, 0, 0); return; }
      const subCenter = bb.getCenter(new THREE.Vector3());
      const dir = subCenter.clone().sub(center);
      const len = dir.length();
      if (len < 1e-6) { subGroup.position.set(0, 0, 0); return; }
      subGroup.position.copy(dir.normalize().multiplyScalar(scale));
    });
    h.bridge.send('explodeFactor', { factor });
  };

  // ── Markup (2D canvas overlay) ────────────────────────────────────────
  let markupCanvas = null;
  let markupCtx = null;
  let markupMode = null;
  let markupItems = [];
  let _drawPath = null;
  let _drawStart = null;

  function ensureMarkupCanvas() {
    if (markupCanvas) return;
    const hostEl = document.getElementById('viewerCanvas') || document.body;
    markupCanvas = document.createElement('canvas');
    markupCanvas.style.cssText = 'position:absolute;inset:0;z-index:50;pointer-events:none;';
    markupCanvas.width  = hostEl.clientWidth  || window.innerWidth;
    markupCanvas.height = hostEl.clientHeight || window.innerHeight;
    hostEl.appendChild(markupCanvas);
    markupCtx = markupCanvas.getContext('2d');
    new ResizeObserver(() => {
      markupCanvas.width  = hostEl.clientWidth;
      markupCanvas.height = hostEl.clientHeight;
      _redrawMarkup();
    }).observe(hostEl);
  }

  ext.startMarkup = function (mode) {
    ensureMarkupCanvas();
    // Detach old listeners before switching mode.
    if (markupCanvas) {
      markupCanvas.removeEventListener('pointerdown', _onMkDown);
      markupCanvas.removeEventListener('pointermove', _onMkMove);
      markupCanvas.removeEventListener('pointerup',   _onMkUp);
    }
    markupMode = mode || null;
    if (!markupCanvas) return;
    if (markupMode) {
      markupCanvas.style.pointerEvents = 'all';
      markupCanvas.style.cursor = markupMode === 'text' ? 'text' : 'crosshair';
      markupCanvas.addEventListener('pointerdown', _onMkDown);
      markupCanvas.addEventListener('pointermove', _onMkMove);
      markupCanvas.addEventListener('pointerup',   _onMkUp);
    } else {
      markupCanvas.style.pointerEvents = 'none';
      markupCanvas.style.cursor = 'default';
    }
  };

  ext.clearMarkup = function () {
    markupItems = [];
    if (markupCtx) markupCtx.clearRect(0, 0, markupCanvas.width, markupCanvas.height);
    host()?.bridge.send('markupCleared', {});
  };

  ext.getMarkupDataUrl = function () {
    if (!markupCanvas) return null;
    return markupCanvas.toDataURL('image/png');
  };

  function _onMkDown(e) {
    _drawStart = { x: e.offsetX, y: e.offsetY };
    if (markupMode === 'draw') _drawPath = [{ ..._drawStart }];
    markupCanvas.setPointerCapture(e.pointerId);
  }
  function _onMkMove(e) {
    if (!_drawStart) return;
    _redrawMarkup();
    if (markupMode === 'draw' && _drawPath) {
      _drawPath.push({ x: e.offsetX, y: e.offsetY });
      _renderPath(_drawPath, '#F97316', 2.5);
    } else if (markupMode === 'arrow') {
      _renderArrow(_drawStart, { x: e.offsetX, y: e.offsetY }, true);
    } else if (markupMode === 'rect') {
      _renderRect(_drawStart, { x: e.offsetX, y: e.offsetY }, true);
    }
  }
  function _onMkUp(e) {
    if (!_drawStart) return;
    const end = { x: e.offsetX, y: e.offsetY };
    if (markupMode === 'draw' && _drawPath && _drawPath.length > 2) {
      markupItems.push({ type: 'draw', path: [..._drawPath], color: '#F97316' });
    } else if (markupMode === 'arrow') {
      markupItems.push({ type: 'arrow', start: { ..._drawStart }, end });
    } else if (markupMode === 'rect') {
      markupItems.push({ type: 'rect', start: { ..._drawStart }, end });
    } else if (markupMode === 'text') {
      const text = window.prompt('Annotation text:');
      if (text && text.trim()) markupItems.push({ type: 'text', x: end.x, y: end.y, text: text.trim() });
    }
    _drawStart = null; _drawPath = null;
    _redrawMarkup();
    host()?.bridge.send('markupUpdated', { count: markupItems.length });
  }

  function _redrawMarkup() {
    if (!markupCtx) return;
    markupCtx.clearRect(0, 0, markupCanvas.width, markupCanvas.height);
    markupItems.forEach(item => {
      if (item.type === 'draw')  _renderPath(item.path, item.color || '#F97316', 2.5);
      else if (item.type === 'arrow') _renderArrow(item.start, item.end, false);
      else if (item.type === 'rect')  _renderRect(item.start, item.end, false);
      else if (item.type === 'text')  _renderText(item.text, item.x, item.y);
    });
  }
  function _renderPath(pts, color, width) {
    if (!markupCtx || pts.length < 2) return;
    markupCtx.beginPath();
    markupCtx.moveTo(pts[0].x, pts[0].y);
    pts.slice(1).forEach(p => markupCtx.lineTo(p.x, p.y));
    markupCtx.strokeStyle = color; markupCtx.lineWidth = width;
    markupCtx.lineJoin = 'round'; markupCtx.lineCap = 'round';
    markupCtx.stroke();
  }
  function _renderArrow(s, e, preview) {
    if (!markupCtx) return;
    markupCtx.beginPath(); markupCtx.moveTo(s.x, s.y); markupCtx.lineTo(e.x, e.y);
    markupCtx.strokeStyle = preview ? 'rgba(249,115,22,0.7)' : '#F97316';
    markupCtx.lineWidth = 2.5; markupCtx.stroke();
    const angle = Math.atan2(e.y - s.y, e.x - s.x);
    const h = 14;
    markupCtx.beginPath();
    markupCtx.moveTo(e.x, e.y);
    markupCtx.lineTo(e.x - h * Math.cos(angle - 0.4), e.y - h * Math.sin(angle - 0.4));
    markupCtx.moveTo(e.x, e.y);
    markupCtx.lineTo(e.x - h * Math.cos(angle + 0.4), e.y - h * Math.sin(angle + 0.4));
    markupCtx.stroke();
  }
  function _renderRect(s, e, preview) {
    if (!markupCtx) return;
    markupCtx.strokeStyle = preview ? 'rgba(249,115,22,0.7)' : '#F97316';
    markupCtx.lineWidth = 2; markupCtx.setLineDash([]);
    markupCtx.strokeRect(s.x, s.y, e.x - s.x, e.y - s.y);
  }
  function _renderText(text, x, y) {
    if (!markupCtx) return;
    markupCtx.font = 'bold 14px Inter, sans-serif';
    markupCtx.strokeStyle = 'rgba(0,0,0,0.7)'; markupCtx.lineWidth = 3;
    markupCtx.strokeText(text, x, y);
    markupCtx.fillStyle = '#F97316'; markupCtx.fillText(text, x, y);
  }

  // ── Walkthrough mode (first-person) ───────────────────────────────────
  // Desktop: WASD + mouse-look. Mobile: virtual joystick rendered as a
  // translucent disc anchored bottom-left; one-finger drag steers.
  let walkActive = false;
  let walkVelocity = new THREE.Vector3();
  let walkInput = { fwd: 0, right: 0, lookX: 0, lookY: 0 };
  let walkClock = null;
  let walkJoystick = null;

  ext.setWalkthrough = function (enabled) {
    const h = host(); if (!h) return;
    walkActive = !!enabled;
    h.controls.enabled = !walkActive;
    if (walkActive) {
      walkClock = new THREE.Clock();
      attachWalkInput(h);
      // Auto-detect the up axis from bounds shape.
      const sz = h.modelBounds.getSize(new THREE.Vector3());
      const upAxis = pickUpAxis(sz);
      h.camera.up.set(upAxis.x, upAxis.y, upAxis.z);
      const eye = eyeFromBounds(h.modelBounds);
      const c = h.modelBounds.getCenter(new THREE.Vector3());
      const floor = upAxis.x ? h.modelBounds.min.x
                  : upAxis.y ? h.modelBounds.min.y
                             : h.modelBounds.min.z;
      const pos = h.camera.position.clone();
      if (upAxis.x) pos.x = floor + eye;
      else if (upAxis.y) pos.y = floor + eye;
      else pos.z = floor + eye;
      h.camera.position.copy(pos);
      const lookAt = pos.clone();
      if (upAxis.x) lookAt.y += 1; else lookAt.x += 1;
      h.camera.lookAt(lookAt);
      walkUp = upAxis;
      // Expose the active up-axis so coordination-viewer's scroll handler
      // can project forward movement onto the floor plane, matching WASD.
      window.__walkUp = upAxis.toArray();
      h.bridge.send('walkthrough', { active: true, upAxis: upAxis.toArray() });
    } else {
      detachWalkInput();
      walkVelocity.set(0, 0, 0);
      window.__walkUp = null;
      h.bridge.send('walkthrough', { active: false });
    }
  };

  let walkUp = new THREE.Vector3(0, 1, 0);

  function pickUpAxis(sz) {
    const ax = Math.abs(sz.x), ay = Math.abs(sz.y), az = Math.abs(sz.z);
    if (az <= ax && az <= ay) return new THREE.Vector3(0, 0, 1);
    if (ay <= ax && ay <= az) return new THREE.Vector3(0, 1, 0);
    return new THREE.Vector3(1, 0, 0);
  }

  function eyeFromBounds(b) {
    const sz = b.getSize(new THREE.Vector3());
    // Default human eye height in metres (~1.7 m). Coordinators can override
    // via the viewer Settings popover; we read from localStorage so it persists.
    let metres = 1.7;
    try {
      const stored = parseFloat(window.localStorage?.getItem('planscape_eye_height_m'));
      if (!isNaN(stored) && stored > 0.5 && stored < 3) metres = stored;
    } catch (_) {}
    // Heuristic: floor span > 100 → assume mm units.
    return Math.max(sz.x, sz.z) > 100 ? metres * 1000 : metres;
  }

  function attachWalkInput(h) {
    document.addEventListener('keydown', onKey);
    document.addEventListener('keyup',   onKeyUp);
    h.renderer.domElement.addEventListener('pointermove', onPtrMove);
    walkJoystick = createJoystick();
    document.body.appendChild(walkJoystick.el);
    if (!h._walkAnim) {
      h._walkAnim = true;
      const tick = () => {
        if (!walkActive) { h._walkAnim = false; return; }
        stepWalk(h);
        requestAnimationFrame(tick);
      };
      requestAnimationFrame(tick);
    }
  }
  function detachWalkInput() {
    document.removeEventListener('keydown', onKey);
    document.removeEventListener('keyup',   onKeyUp);
    if (walkJoystick) { walkJoystick.el.remove(); walkJoystick = null; }
  }

  function onKey(e) {
    if (e.key === 'w' || e.key === 'W' || e.key === 'ArrowUp')    walkInput.fwd  =  1;
    if (e.key === 's' || e.key === 'S' || e.key === 'ArrowDown')  walkInput.fwd  = -1;
    if (e.key === 'a' || e.key === 'A' || e.key === 'ArrowLeft')  walkInput.right = -1;
    if (e.key === 'd' || e.key === 'D' || e.key === 'ArrowRight') walkInput.right =  1;
    if (e.key === 'Escape') ext.setWalkthrough(false);
  }
  function onKeyUp(e) {
    if ('wWsS'.includes(e.key) || e.key === 'ArrowUp' || e.key === 'ArrowDown') walkInput.fwd = 0;
    if ('aAdD'.includes(e.key) || e.key === 'ArrowLeft' || e.key === 'ArrowRight') walkInput.right = 0;
  }
  let lastPtr = null;
  function onPtrMove(e) {
    if (!walkActive) return;
    if (e.buttons !== 1 && e.pointerType !== 'touch') { lastPtr = null; return; }
    if (lastPtr) {
      walkInput.lookX += (e.clientX - lastPtr.x) * 0.003;
      walkInput.lookY += (e.clientY - lastPtr.y) * 0.003;
    }
    lastPtr = { x: e.clientX, y: e.clientY };
  }
  function stepWalk(h) {
    const dt = walkClock.getDelta();
    // Base speed scales with model size; speed-wheel multiplier lets
    // coordinators move at human pace (1.0×) or fast-fly (4×–8×).
    const mul = (typeof window.__walkSpeedMul === 'number' && window.__walkSpeedMul > 0)
      ? window.__walkSpeedMul : 1.0;
    const speed = Math.max(h.modelBounds.getSize(new THREE.Vector3()).length() * 0.05, 1) * mul;
    const fwd = new THREE.Vector3();
    h.camera.getWorldDirection(fwd);
    // Project fwd onto the plane perpendicular to walkUp so movement
    // stays horizontal regardless of current camera pitch.
    const dot = fwd.dot(walkUp);
    fwd.addScaledVector(walkUp, -dot).normalize();
    const right = new THREE.Vector3().crossVectors(fwd, walkUp).normalize();
    walkVelocity.set(0, 0, 0)
      .addScaledVector(fwd, walkInput.fwd * speed)
      .addScaledVector(right, walkInput.right * speed);
    h.camera.position.addScaledVector(walkVelocity, dt);
    if (walkInput.lookX || walkInput.lookY) {
      const eu = new THREE.Euler().setFromQuaternion(h.camera.quaternion, 'YXZ');
      eu.y -= walkInput.lookX;
      eu.x -= walkInput.lookY;
      eu.x = Math.max(-1.4, Math.min(1.4, eu.x));
      h.camera.quaternion.setFromEuler(eu);
      walkInput.lookX = 0; walkInput.lookY = 0;
    }
  }

  function createJoystick() {
    const el = document.createElement('div');
    el.style.cssText = 'position:absolute;left:24px;bottom:90px;width:120px;height:120px;border-radius:60px;background:rgba(255,255,255,0.12);border:1px solid rgba(255,255,255,0.3);touch-action:none;';
    const knob = document.createElement('div');
    knob.style.cssText = 'position:absolute;left:40px;top:40px;width:40px;height:40px;border-radius:20px;background:rgba(232,145,45,0.9);';
    el.appendChild(knob);
    let active = false;
    el.addEventListener('pointerdown', (e) => { active = true; el.setPointerCapture(e.pointerId); });
    el.addEventListener('pointerup',   () => { active = false; knob.style.left = '40px'; knob.style.top = '40px'; walkInput.fwd = 0; walkInput.right = 0; });
    el.addEventListener('pointermove', (e) => {
      if (!active) return;
      const r = el.getBoundingClientRect();
      const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
      const dx = Math.max(-50, Math.min(50, e.clientX - cx));
      const dy = Math.max(-50, Math.min(50, e.clientY - cy));
      knob.style.left = (40 + dx) + 'px';
      knob.style.top  = (40 + dy) + 'px';
      walkInput.right = dx / 50;
      walkInput.fwd   = -dy / 50;
    });
    return { el };
  }

  // ── Area measurement (polygon) ────────────────────────────────────────
  let areaPoints = [];
  let areaGroup = null;
  ext.startArea = function () {
    const h = host(); if (!h) return;
    cancelArea();
    areaGroup = new THREE.Group();
    h.scene.add(areaGroup);
    areaPoints = [];
  };
  ext.addAreaPoint = function (point) {
    const h = host(); if (!h || !areaGroup) return;
    const p = new THREE.Vector3(point[0], point[1], point[2]);
    areaPoints.push(p);
    addMarker(areaGroup, p, 0x66bb6a);
    if (areaPoints.length >= 2) drawSegment(areaGroup,
      areaPoints[areaPoints.length - 2], areaPoints[areaPoints.length - 1], 0x66bb6a);
  };
  ext.finishArea = function () {
    const h = host(); if (!h || !areaGroup || areaPoints.length < 3) return;
    drawSegment(areaGroup, areaPoints[areaPoints.length - 1], areaPoints[0], 0x66bb6a);
    const a = polygonArea3D(areaPoints);
    h.bridge.send('measureArea', { area: a, points: areaPoints.map(v => v.toArray()) });
    areaPoints = [];
  };
  function cancelArea() {
    const h = host(); if (h && areaGroup) h.scene.remove(areaGroup);
    areaGroup = null; areaPoints = [];
  }
  function polygonArea3D(pts) {
    if (pts.length < 3) return 0;
    let total = new THREE.Vector3();
    for (let i = 0; i < pts.length; i++) {
      const a = pts[i], b = pts[(i + 1) % pts.length];
      total.add(new THREE.Vector3().crossVectors(a, b));
    }
    return total.length() * 0.5;
  }

  // ── Volume measurement (selected element AABB) ────────────────────────
  ext.measureSelectionVolume = function () {
    const h = host(); if (!h || !h.highlightedMesh) return;
    const bb = new THREE.Box3().setFromObject(h.highlightedMesh);
    const sz = bb.getSize(new THREE.Vector3());
    const v = sz.x * sz.y * sz.z;
    h.bridge.send('measureVolume', {
      volume: v,
      size: [sz.x, sz.y, sz.z],
      bounds: [bb.min.x, bb.min.y, bb.min.z, bb.max.x, bb.max.y, bb.max.z],
    });
  };

  function addMarker(group, p, color) {
    const sz = (host()?.modelBounds.getSize(new THREE.Vector3()).length() || 1) * 0.004;
    const sph = new THREE.Mesh(
      new THREE.SphereGeometry(sz, 12, 12),
      new THREE.MeshBasicMaterial({ color }));
    sph.position.copy(p); group.add(sph);
  }
  function drawSegment(group, a, b, color) {
    const g = new THREE.BufferGeometry().setFromPoints([a, b]);
    const m = new THREE.LineBasicMaterial({ color });
    group.add(new THREE.Line(g, m));
  }

  // ── Auto-LOD: drop pixel ratio + meshes when render fps sags ──────────
  // Driven by rAF (the actual render loop), NOT setInterval.
  let frameCount = 0;
  let windowStart = performance.now();
  let lodLevel = 0;
  let rafAttached = false;

  function attachRafSampler() {
    if (rafAttached) return;
    rafAttached = true;
    function step() {
      frameCount++;
      const now = performance.now();
      const elapsed = now - windowStart;
      // 1-second windows.
      if (elapsed >= 1000) {
        const avg = frameCount * 1000 / elapsed;
        frameCount = 0;
        windowStart = now;
        evaluateLod(avg);
      }
      requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  }

  function evaluateLod(avg) {
    const h = host(); if (!h) return;
    if (avg < 24 && lodLevel === 0) {
      h.renderer.setPixelRatio(1);
      lodLevel = 1;
      h.bridge.send('lodChanged', { level: 1, avgFps: avg });
    } else if (avg < 18 && lodLevel === 1) {
      let n = 0;
      h.modelRoot && h.modelRoot.traverse((o) => { if (o.isMesh && (n++ % 3 === 2)) o.visible = false; });
      lodLevel = 2;
      h.bridge.send('lodChanged', { level: 2, avgFps: avg });
    } else if (avg > 45 && lodLevel > 0) {
      h.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
      h.modelRoot && h.modelRoot.traverse((o) => { if (o.isMesh) o.visible = true; });
      lodLevel = 0;
      h.bridge.send('lodChanged', { level: 0, avgFps: avg });
    }
  }

  // Kick the sampler once viewer.html has populated STING_VIEWER. The
  // `tickFps` name is kept for backwards compat with the host page's bootstrap.
  ext.tickFps = function () { attachRafSampler(); };

  function countMeshes(o) { let n = 0; o.traverse(x => { if (x.isMesh) n++; }); return n; }
  function bbToArray(b) { return [b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z]; }

  // ── Camera presets — orthogonal & iso views ───────────────────────────
  // preset ∈ { top, bottom, front, back, left, right, iso, home }.
  // Frames the model bounds with a fresh camera position + lookAt.
  ext.setCameraPreset = function (preset) {
    const h = host(); if (!h || h.modelBounds.isEmpty()) return;
    const c = h.modelBounds.getCenter(new THREE.Vector3());
    const sz = h.modelBounds.getSize(new THREE.Vector3());
    const d = Math.max(sz.x, sz.y, sz.z) * 1.8 || 10;
    const presets = {
      top:    [0,  d, 0],
      bottom: [0, -d, 0],
      front:  [0, 0,  d],
      back:   [0, 0, -d],
      left:   [-d, 0, 0],
      right:  [ d, 0, 0],
      iso:    [d * 0.85, d * 0.85, d * 0.85],
      home:   [d * 0.85, d * 0.85, d * 0.85],
    };
    const off = presets[preset] || presets.iso;
    h.camera.position.set(c.x + off[0], c.y + off[1], c.z + off[2]);
    h.controls.target.copy(c);
    // Orthogonal presets need a clean Y-up unless we'd be aligned with it
    // (top/bottom) — then flip up to +Z so the camera doesn't gimbal-lock.
    h.camera.up.set(0, 1, 0);
    if (preset === 'top' || preset === 'bottom') h.camera.up.set(0, 0, 1);
    h.controls.update();
    h.bridge.send('cameraPreset', { preset });
  };

  // ── Camera bookmark slots — quick recall (1..4) ───────────────────────
  const cameraBookmarks = {};
  ext.saveCameraBookmark = function (slot) {
    const h = host(); if (!h) return;
    cameraBookmarks[slot] = {
      pos: h.camera.position.toArray(),
      target: h.controls.target.toArray(),
      up: h.camera.up.toArray(),
    };
    h.bridge.send('bookmarkSaved', { slot, slots: Object.keys(cameraBookmarks).map(Number) });
  };
  ext.restoreCameraBookmark = function (slot) {
    const h = host(); if (!h) return;
    const b = cameraBookmarks[slot]; if (!b) return;
    h.camera.position.fromArray(b.pos);
    h.controls.target.fromArray(b.target);
    h.camera.up.fromArray(b.up);
    h.controls.update();
    h.bridge.send('bookmarkRestored', { slot });
  };

  // ── Render mode — shaded / wireframe / x-ray / ghost ──────────────────
  // Centralised so the View menu, coordination layer, and saved-view
  // restore path all converge on one implementation.
  let _renderMode = 'shaded';
  ext.setRenderMode = function (mode) {
    const h = host(); if (!h || !h.modelRoot) return;
    _renderMode = mode;
    h.modelRoot.traverse(o => {
      if (!o.isMesh || !o.material) return;
      if (!o.userData._renderClone) {
        o.material = o.material.clone();
        o.userData._renderClone = true;
        o.userData._origOpacity = o.material.opacity ?? 1;
        o.userData._origTransparent = o.material.transparent ?? false;
      }
      const m = o.material;
      m.wireframe = (mode === 'wireframe');
      if (mode === 'xray') {
        m.transparent = true;
        m.opacity = 0.25;
        m.depthWrite = false;
      } else if (mode === 'ghost') {
        m.transparent = true;
        m.opacity = 0.55;
        m.depthWrite = true;
      } else {
        m.transparent = o.userData._origTransparent;
        m.opacity = o.userData._origOpacity ?? 1;
        m.depthWrite = true;
      }
      m.needsUpdate = true;
    });
    h.bridge.send('renderMode', { mode });
  };
  ext.getRenderMode = function () { return _renderMode; };

  // ── Edge silhouette overlay (wireframe-on-shaded) ─────────────────────
  // Adds thin edge lines on top of shaded geometry — dramatically improves
  // depth perception on small mobile screens. Toggleable to spare GPU on
  // large models.
  let _edgeGroup = null;
  ext.setEdgeOverlay = function (enabled) {
    const h = host(); if (!h || !h.modelRoot) return;
    if (_edgeGroup) { h.scene.remove(_edgeGroup); _edgeGroup = null; }
    if (!enabled) return;
    _edgeGroup = new THREE.Group();
    _edgeGroup.userData.skipRaycast = true;
    const mat = new THREE.LineBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.35 });
    h.modelRoot.traverse(o => {
      if (!o.isMesh || !o.visible) return;
      try {
        const edges = new THREE.EdgesGeometry(o.geometry, 30);
        const line = new THREE.LineSegments(edges, mat);
        line.applyMatrix4(o.matrixWorld);
        _edgeGroup.add(line);
      } catch (_) {}
    });
    h.scene.add(_edgeGroup);
  };

  // ── Section caps — fill cut surfaces for visual closure ───────────────
  // Renders a translucent quad on each section plane sized to model bounds.
  // Pure visualisation — does NOT participate in clipping itself.
  let _sectionCapsGroup = null;
  ext.setSectionCaps = function (enabled) {
    const h = host(); if (!h) return;
    if (_sectionCapsGroup) { h.scene.remove(_sectionCapsGroup); _sectionCapsGroup = null; }
    if (!enabled || sectionPlaneEntries.length === 0) return;
    _sectionCapsGroup = new THREE.Group();
    const size = h.modelBounds.getSize(new THREE.Vector3());
    const cap = Math.max(size.x, size.y, size.z) * 1.2;
    const center = h.modelBounds.getCenter(new THREE.Vector3());
    sectionPlaneEntries.forEach(e => {
      const geom = new THREE.PlaneGeometry(cap, cap);
      const mat = new THREE.MeshBasicMaterial({
        color: 0xE8912D, side: THREE.DoubleSide,
        transparent: true, opacity: 0.18,
        depthWrite: false,
      });
      const mesh = new THREE.Mesh(geom, mat);
      // Orient the plane geometry so its normal matches the clipping plane.
      const n = e.plane.normal;
      mesh.lookAt(n.x, n.y, n.z);
      // Position: project model centre onto the plane.
      const cToP = e.plane.distanceToPoint(center);
      mesh.position.copy(center).addScaledVector(n, -cToP);
      mesh.userData.skipRaycast = true;
      _sectionCapsGroup.add(mesh);
    });
    h.scene.add(_sectionCapsGroup);
  };
  ext.refreshSectionCaps = function () {
    // Re-render caps if they're enabled and plane set changed.
    if (_sectionCapsGroup) ext.setSectionCaps(true);
  };

  // ── Coordinate readout — broadcast XYZ under cursor on pointermove ────
  // Off by default (expensive raycast on every move). Coordinator UI
  // turns it on while the Coordinates chip is visible.
  let _coordEnabled = false;
  let _coordListener = null;
  ext.setCoordReadout = function (enabled) {
    const h = host(); if (!h) return;
    if (enabled && !_coordEnabled) {
      _coordEnabled = true;
      const rc = new THREE.Raycaster();
      const v2 = new THREE.Vector2();
      let lastEmit = 0;
      _coordListener = (e) => {
        const now = performance.now();
        if (now - lastEmit < 50) return;        // throttle to 20 Hz
        lastEmit = now;
        const rect = h.renderer.domElement.getBoundingClientRect();
        v2.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
        v2.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
        rc.setFromCamera(v2, h.camera);
        const targets = h.modelRoot ? [h.modelRoot] : [];
        const hits = rc.intersectObjects(targets, true);
        if (!hits.length) { h.bridge.send('coord', { hit: false }); return; }
        h.bridge.send('coord', { hit: true, point: hits[0].point.toArray() });
      };
      h.renderer.domElement.addEventListener('pointermove', _coordListener);
    } else if (!enabled && _coordEnabled) {
      _coordEnabled = false;
      if (_coordListener) h.renderer.domElement.removeEventListener('pointermove', _coordListener);
      _coordListener = null;
      h.bridge.send('coord', { hit: false, off: true });
    }
  };

  // ── Cumulative path measurement ───────────────────────────────────────
  // Sequence of picks, sum of segment lengths. finish() emits the total +
  // every leg so the host can render a polyline + breakdown.
  let _pathPoints = [];
  let _pathGroup = null;
  ext.startCumulativeMeasure = function () {
    const h = host(); if (!h) return;
    if (_pathGroup) h.scene.remove(_pathGroup);
    _pathGroup = new THREE.Group();
    h.scene.add(_pathGroup);
    _pathPoints = [];
  };
  ext.addCumulativePoint = function (point) {
    const h = host(); if (!h || !_pathGroup) return;
    const p = new THREE.Vector3(point[0], point[1], point[2]);
    _pathPoints.push(p);
    addMarker(_pathGroup, p, 0x0078D4);
    if (_pathPoints.length >= 2) {
      const a = _pathPoints[_pathPoints.length - 2];
      drawSegment(_pathGroup, a, p, 0x0078D4);
      const seg = a.distanceTo(p);
      const total = _pathPoints.reduce((acc, _, i) => i ? acc + _pathPoints[i - 1].distanceTo(_pathPoints[i]) : 0, 0);
      h.bridge.send('measurePath', { segment: seg, total, points: _pathPoints.map(v => v.toArray()) });
    }
  };
  ext.finishCumulativeMeasure = function () {
    const h = host(); if (!h) return;
    const total = _pathPoints.reduce((acc, _, i) => i ? acc + _pathPoints[i - 1].distanceTo(_pathPoints[i]) : 0, 0);
    h.bridge.send('measurePathFinal', { total, points: _pathPoints.map(v => v.toArray()) });
    _pathPoints = [];
  };
  ext.clearCumulativeMeasure = function () {
    const h = host(); if (!h) return;
    if (_pathGroup) { h.scene.remove(_pathGroup); _pathGroup = null; }
    _pathPoints = [];
  };

  // ── Full view-state snapshot ──────────────────────────────────────────
  // Captures everything that makes a saved view *visually* reproducible:
  // camera, render mode, exploded state, section planes/box, per-model
  // visibility + opacity, isolated guid set, edge overlay, caps.
  ext.captureViewState = function () {
    const h = host(); if (!h) return null;
    return {
      version: 1,
      camera: {
        pos: h.camera.position.toArray(),
        target: h.controls.target.toArray(),
        up: h.camera.up.toArray(),
        fov: h.camera.fov,
      },
      renderMode: _renderMode,
      sectionPlanes: sectionPlaneEntries.map(e => ({ id: e.id, axis: e.axis, offset: e.offset })),
      sectionBoxFaces: sectionBoxPlaneIds.slice(),
      federation: federationModels.map(m => ({
        label: m.label,
        visible: m.group.visible,
        opacity: m._opacity ?? 1,
      })),
      isolated: _isolatedSet ? Array.from(_isolatedSet) : null,
      edgeOverlay: !!_edgeGroup,
      sectionCaps: !!_sectionCapsGroup,
    };
  };

  // ── Clearance / minimum-distance between two meshes ───────────────────
  // Strategy:
  //  1. AABB-distance as cheap lower bound (free, exact for separated boxes).
  //  2. If AABBs intersect → vertex-to-bounding-box probe pass for both
  //     directions (sampled) so we still return a meaningful 'penetration'
  //     reading.
  // Returns { distance, pointA, pointB, intersect, aGuid, bGuid }.
  ext.computeClearance = function (meshA, meshB) {
    const h = host(); if (!h || !meshA || !meshB) return null;
    const boxA = new THREE.Box3().setFromObject(meshA);
    const boxB = new THREE.Box3().setFromObject(meshB);
    if (boxA.isEmpty() || boxB.isEmpty()) return null;

    // Closest point on each AABB to the other AABB's center.
    const centerA = boxA.getCenter(new THREE.Vector3());
    const centerB = boxB.getCenter(new THREE.Vector3());
    const pA = boxA.clampPoint(centerB, new THREE.Vector3());
    const pB = boxB.clampPoint(centerA, new THREE.Vector3());

    const intersect = boxA.intersectsBox(boxB);
    let distance;
    if (intersect) {
      // Boxes overlap — sample geometry to estimate penetration depth.
      // Returns NEGATIVE distance so the host can flag clashes.
      distance = -sampleOverlapDepth(boxA, boxB);
    } else {
      distance = pA.distanceTo(pB);
    }

    // Draw a temporary line so the user can see the measurement in-scene.
    drawClearanceLine(pA, pB, intersect);

    return {
      distance,
      pointA: pA.toArray(),
      pointB: pB.toArray(),
      intersect,
      aGuid: meshA.userData && meshA.userData.elementGuid,
      bGuid: meshB.userData && meshB.userData.elementGuid,
      aName: meshA.name,
      bName: meshB.name,
    };
  };

  function sampleOverlapDepth(a, b) {
    // Overlap region's smallest dimension is a fair penetration estimate
    // for AABB-overlapping clashes.
    const ix = Math.min(a.max.x, b.max.x) - Math.max(a.min.x, b.min.x);
    const iy = Math.min(a.max.y, b.max.y) - Math.max(a.min.y, b.min.y);
    const iz = Math.min(a.max.z, b.max.z) - Math.max(a.min.z, b.min.z);
    return Math.max(0, Math.min(ix, Math.min(iy, iz)));
  }

  let _clearanceGroup = null;
  function drawClearanceLine(pA, pB, intersect) {
    const h = host(); if (!h) return;
    if (_clearanceGroup) h.scene.remove(_clearanceGroup);
    _clearanceGroup = new THREE.Group();
    const color = intersect ? 0xEF4444 : 0x10B981;
    const g = new THREE.BufferGeometry().setFromPoints([pA, pB]);
    _clearanceGroup.add(new THREE.Line(g, new THREE.LineBasicMaterial({ color, depthTest: false })));
    addMarker(_clearanceGroup, pA, color);
    addMarker(_clearanceGroup, pB, color);
    h.scene.add(_clearanceGroup);
  }
  ext.clearClearance = function () {
    const h = host(); if (h && _clearanceGroup) h.scene.remove(_clearanceGroup);
    _clearanceGroup = null;
  };

  // ── 360° photo sphere — inside-out equirect texture ───────────────────
  // Loads an equirectangular image and renders it on the inside of a sphere
  // anchored at the given world position (defaults to model centre). The
  // user pans inside the sphere by orbiting the camera. Call clearPhotoSphere
  // to remove it.
  let _photoSphere = null;
  let _photoSphereSaved = null;
  ext.setPhotoSphere = function (url, opts) {
    const h = host(); if (!h || !url) return;
    ext.clearPhotoSphere();
    const radius = (opts && opts.radius)
      ?? Math.max(1, h.modelBounds.getSize(new THREE.Vector3()).length() * 0.05);
    const center = (opts && opts.position)
      ? new THREE.Vector3(opts.position[0], opts.position[1], opts.position[2])
      : h.modelBounds.getCenter(new THREE.Vector3());

    const loader = new THREE.TextureLoader();
    loader.crossOrigin = 'anonymous';
    loader.load(url, (tex) => {
      tex.colorSpace = THREE.SRGBColorSpace || THREE.sRGBEncoding;
      const geo = new THREE.SphereGeometry(radius, 64, 32);
      geo.scale(-1, 1, 1); // flip inside-out so we view the texture from inside
      const mat = new THREE.MeshBasicMaterial({ map: tex, side: THREE.FrontSide });
      _photoSphere = new THREE.Mesh(geo, mat);
      _photoSphere.userData.skipRaycast = true;
      _photoSphere.position.copy(center);
      h.scene.add(_photoSphere);

      // Save the previous camera state so clearPhotoSphere can restore it.
      _photoSphereSaved = {
        pos: h.camera.position.toArray(),
        target: h.controls.target.toArray(),
      };
      h.camera.position.copy(center);
      h.controls.target.set(center.x + 0.1, center.y, center.z);
      h.controls.update();
      h.bridge.send('photoSphere', { active: true, url, position: center.toArray(), radius });
    }, undefined, (err) => {
      h.bridge.send('photoSphereError', { url, error: String(err) });
    });
  };
  ext.clearPhotoSphere = function () {
    const h = host(); if (!h) return;
    if (_photoSphere) {
      h.scene.remove(_photoSphere);
      try { _photoSphere.geometry.dispose(); _photoSphere.material.map?.dispose(); _photoSphere.material.dispose(); } catch (_) {}
      _photoSphere = null;
    }
    if (_photoSphereSaved) {
      h.camera.position.fromArray(_photoSphereSaved.pos);
      h.controls.target.fromArray(_photoSphereSaved.target);
      h.controls.update();
      _photoSphereSaved = null;
    }
    h.bridge.send('photoSphere', { active: false });
  };

  // ── Live multiplayer cursor — render other users' camera frustums ─────
  // The coordination layer broadcasts our camera state on a throttle; when
  // it receives others' state it calls setPresenceCursor(userId, payload).
  // Cursors fade after `staleMs` so a disconnected collaborator's frustum
  // doesn't sit in the scene forever.
  const _presenceCursors = new Map(); // userId → { mesh, lastSeen }
  const PRESENCE_STALE_MS = 30000;
  function makePresenceMesh(color) {
    // A small camera frustum sprite + a label-pin.
    const grp = new THREE.Group();
    const cone = new THREE.Mesh(
      new THREE.ConeGeometry(0.5, 1.2, 8),
      new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.55, depthTest: false }),
    );
    cone.rotation.x = Math.PI;
    grp.add(cone);
    grp.userData.skipRaycast = true;
    return grp;
  }
  ext.setPresenceCursor = function (userId, payload) {
    const h = host(); if (!h || !userId) return;
    let entry = _presenceCursors.get(userId);
    if (!entry) {
      // Hash user ID to a stable hue so each collaborator keeps the same colour.
      let hash = 0;
      for (let i = 0; i < userId.length; i++) hash = ((hash << 5) - hash + userId.charCodeAt(i)) | 0;
      const hue = ((hash % 360) + 360) % 360;
      const col = new THREE.Color().setHSL(hue / 360, 0.7, 0.55);
      const mesh = makePresenceMesh(col.getHex());
      h.scene.add(mesh);
      entry = { mesh, color: col.getHex(), label: payload?.name || userId };
      _presenceCursors.set(userId, entry);
    }
    if (payload?.pos && payload?.target) {
      const pos = new THREE.Vector3(...payload.pos);
      const tgt = new THREE.Vector3(...payload.target);
      const sz  = h.modelBounds.getSize(new THREE.Vector3()).length() || 1;
      const scale = sz * 0.015;
      entry.mesh.position.copy(pos);
      entry.mesh.lookAt(tgt);
      // Rotate cone so its narrow tip points along the camera view direction.
      entry.mesh.rotateX(Math.PI / 2);
      entry.mesh.scale.set(scale, scale, scale);
    }
    entry.lastSeen = performance.now();
  };
  ext.removePresenceCursor = function (userId) {
    const h = host(); if (!h) return;
    const entry = _presenceCursors.get(userId);
    if (!entry) return;
    h.scene.remove(entry.mesh);
    _presenceCursors.delete(userId);
  };
  ext.clearPresenceCursors = function () {
    const h = host(); if (!h) return;
    _presenceCursors.forEach(entry => h.scene.remove(entry.mesh));
    _presenceCursors.clear();
  };
  // Periodic stale sweep — runs alongside the FPS sampler.
  setInterval(() => {
    const h = host(); if (!h) return;
    const now = performance.now();
    for (const [uid, e] of _presenceCursors.entries()) {
      if (now - e.lastSeen > PRESENCE_STALE_MS) {
        h.scene.remove(e.mesh);
        _presenceCursors.delete(uid);
      }
    }
  }, 5000);

  // Capture our own camera state for broadcasting (host throttles).
  ext.getCameraState = function () {
    const h = host(); if (!h) return null;
    return { pos: h.camera.position.toArray(), target: h.controls.target.toArray() };
  };

  ext.restoreViewState = function (s) {
    const h = host(); if (!h || !s) return;
    // Camera first — section planes use bounds, not camera, so order is safe.
    if (s.camera) {
      h.camera.position.fromArray(s.camera.pos);
      h.controls.target.fromArray(s.camera.target);
      h.camera.up.fromArray(s.camera.up);
      if (s.camera.fov) { h.camera.fov = s.camera.fov; h.camera.updateProjectionMatrix(); }
      h.controls.update();
    }
    // Sections: replace whole set so id reuse stays consistent.
    sectionPlaneEntries = [];
    sectionBoxPlaneIds = [];
    (s.sectionPlanes || []).forEach(p => {
      if (typeof p.axis === 'string' && p.axis.startsWith('box')) return; // re-added below
      ext.addSectionPlaneAxis(p.axis, p.offset);
    });
    syncClippingPlanes();
    // Federation visibility + opacity.
    (s.federation || []).forEach(f => {
      ext.setModelVisible(f.label, f.visible !== false);
      if (f.opacity != null) ext.setModelOpacity(f.label, f.opacity);
    });
    // Isolated set.
    ext.setIsolatedGuids(s.isolated || null);
    // Render mode last so material clones happen after federation changes.
    if (s.renderMode && s.renderMode !== _renderMode) ext.setRenderMode(s.renderMode);
    ext.setEdgeOverlay(!!s.edgeOverlay);
    ext.setSectionCaps(!!s.sectionCaps);
    h.bridge.send('viewStateRestored', { keys: Object.keys(s) });
  };
})();
