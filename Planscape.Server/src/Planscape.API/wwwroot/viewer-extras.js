// MODEL-VIEWER extras — federation, walkthrough, oblique sections,
// area + volume measure, auto-LOD. Loaded *after* viewer.html's main IIFE
// so it can hook the global STING_VIEWER object the host page exposes.
(() => {
  'use strict';
  const ext = {};
  window.STING_VIEWER_EXTRAS = ext;

  function host() { return window.STING_VIEWER; }

  // ── Federation: load multiple GLBs into one scene ─────────────────────
  ext.loadFederation = function (sources) {
    const h = host();
    if (!h || typeof THREE === 'undefined' || !THREE.GLTFLoader) return;
    if (h.federationGroup) h.scene.remove(h.federationGroup);
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
        merged.expandByObject(gltf.scene);
        if (--remaining === 0) {
          // ORBIT FIX — rotate the whole federated group Z-up → Y-up to match the
          // single-model pivot, so OrbitControls runs at its native (0,1,0).
          if (h.upQuaternion) {
            group.quaternion.copy(h.upQuaternion);
            group.updateMatrixWorld(true);
            merged.setFromObject(group);   // recompute bounds in the rotated (Y-up) space
          }
          h.modelRoot = group;
          h.modelBounds = merged;
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

  // ── Oblique section plane (arbitrary normal + offset) ─────────────────
  ext.setSectionPlane = function ({ normal, offset, enabled }) {
    const h = host(); if (!h) return;
    if (!enabled) { h.renderer.clippingPlanes = []; return; }
    // ORBIT FIX — section normals arrive in TRUE world (Z-up) building axes; the
    // rendered scene is Y-up, so rotate the normal into scene space (e.g. a Revit
    // 'Z'/vertical cut stays vertical on screen). modelBounds below is already in
    // rendered space, so the offset/constant math stays consistent.
    let na = (normal && normal.length === 3) ? [normal[0], normal[1], normal[2]] : [0, 0, -1];
    if (typeof h.worldDirToScene === 'function') na = h.worldDirToScene(na);
    const n = new THREE.Vector3(na[0], na[1], na[2]).normalize();
    if (n.lengthSq() < 1e-9) n.set(0, -1, 0);
    const sz = h.modelBounds.getSize(new THREE.Vector3());
    const c  = h.modelBounds.getCenter(new THREE.Vector3());
    // offset is a 0..1 fraction across the bounds extent along the normal.
    const span = Math.max(sz.x, sz.y, sz.z);
    const o = (offset == null ? 0.5 : offset) - 0.5;
    const constant = -n.dot(c) + o * span;
    const plane = new THREE.Plane(n, constant);
    h.renderer.clippingPlanes = [plane];
  };

  // ── Walkthrough mode (first-person) ───────────────────────────────────
  // Desktop: WASD + mouse-look via PointerLockControls (created lazily).
  // Mobile: virtual joystick rendered as a translucent disc anchored to the
  // bottom-left; one-finger drag steers, pinch handled by orbit (disabled in
  // walk mode, free-look uses touchstart→touchmove deltas).
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
      // Auto-detect the up axis from bounds shape. Buildings have the
      // smallest extent on their vertical axis (height < width × depth).
      // Revit GLBs are Z-up; web glTF is Y-up. The walkthrough picks the
      // axis with the smallest span as "up" and aligns camera.up to it.
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

  // M5 — "Human eye" view: drop the camera to eye height (reusing eyeFromBounds +
  // the localStorage override) and look horizontally, KEEPING the current heading.
  // Works in orbit mode — does NOT engage walkthrough. Y-up aware; up stays (0,1,0).
  ext.humanEyeView = function () {
    const h = host(); if (!h || !h.modelBounds || h.modelBounds.isEmpty()) return;
    const cam = h.camera;
    const eye = eyeFromBounds(h.modelBounds);
    const floor = h.modelBounds.min.y;
    const fwd = new THREE.Vector3(); cam.getWorldDirection(fwd);
    fwd.y = 0; if (fwd.lengthSq() < 1e-6) fwd.set(0, 0, -1); fwd.normalize();
    cam.up.set(0, 1, 0);
    cam.position.y = floor + eye;
    const span = h.modelBounds.getSize(new THREE.Vector3()).length();
    const tgt = cam.position.clone().addScaledVector(fwd, Math.max(2, span * 0.05));
    tgt.y = cam.position.y;                 // horizontal gaze
    h.controls.target.copy(tgt);
    h.controls.update();
  };

  // M5 — raise/lower the eye along rendered +Y, keeping heading + pitch (move BOTH
  // camera.position and controls.target by the same delta). Step scales with model size.
  ext.elevateCamera = function (dir) {
    const h = host(); if (!h || !h.modelBounds || h.modelBounds.isEmpty()) return;
    const step = (h.modelBounds.getSize(new THREE.Vector3()).y || 10) * 0.04 * (dir < 0 ? -1 : 1);
    h.camera.position.y += step;
    h.controls.target.y += step;
    h.controls.update();
  };

  function pickUpAxis(sz) {
    // ORBIT FIX — the model is now rendered Y-up (the host rotates Z-up → Y-up),
    // so first-person walk is always level against world-Y. No more bbox guess.
    return new THREE.Vector3(0, 1, 0);
  }

  function eyeFromBounds(b) {
    const sz = b.getSize(new THREE.Vector3());
    // Default human eye height in metres (~1.7 m above floor for an
    // average adult; 1.6–1.8 m is the standard range used by IFC + ISO
    // 19650 walkthrough specs). Coordinators can override via the viewer
    // Settings popover; we read the persisted value from localStorage so
    // the override survives reloads.
    let metres = 1.7;
    try {
      const stored = parseFloat(window.localStorage?.getItem('planscape_eye_height_m'));
      if (!isNaN(stored) && stored > 0.5 && stored < 3) metres = stored;
    } catch (_) {}
    // If the scene looks tiny (units in metres) fall back to the metre
    // value directly. Heuristic: floor span > 100 → assume mm units.
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
    // Base speed scales with model size; the speed-wheel multiplier
    // (driven by the +/- buttons + scroll wheel in the coordination
    // viewer's nav-controls bar) lets coordinators move at human pace
    // (1.0×) or fast-fly across a hospital wing (4×–8×).
    const mul = (typeof window.__walkSpeedMul === 'number' && window.__walkSpeedMul > 0)
      ? window.__walkSpeedMul : 1.0;
    const speed = Math.max(h.modelBounds.getSize(new THREE.Vector3()).length() * 0.05, 1) * mul;
    const fwd = new THREE.Vector3();
    h.camera.getWorldDirection(fwd);
    // Project fwd onto the plane perpendicular to walkUp so movement stays
    // horizontal regardless of where the camera is currently pitched.
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
    // Area points are drawn in the recentred + Y-up-rotated render space; report
    // them back in TRUE world (Z-up) coords via the host bridge so the host stays
    // in survey coordinates. The area value itself is rotation/translation-invariant.
    const toW = (typeof h.toWorld === 'function')
      ? h.toWorld
      : (a3) => { const o = h.modelOffset || { x: 0, y: 0, z: 0 }; return [a3[0] + o.x, a3[1] + o.y, a3[2] + o.z]; };
    h.bridge.send('measureArea', {
      area: a,
      points: areaPoints.map(v => toW([v.x, v.y, v.z]))
    });
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
    const sph = new THREE.Mesh(
      new THREE.SphereGeometry(host().modelBounds.getSize(new THREE.Vector3()).length() * 0.004, 12, 12),
      new THREE.MeshBasicMaterial({ color }));
    sph.position.copy(p); group.add(sph);
  }
  function drawSegment(group, a, b, color) {
    const g = new THREE.BufferGeometry().setFromPoints([a, b]);
    const m = new THREE.LineBasicMaterial({ color });
    group.add(new THREE.Line(g, m));
  }

  // ── Auto-LOD: drop pixel ratio + meshes when render fps sags ──────────
  // Driven by rAF (the actual render loop), NOT setInterval — sampling the
  // setInterval cadence reports the timer's own delay, not GPU/draw cost.
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
  // `tickFps` name is kept for backwards compat with the host page's
  // bootstrap; calling it now just attaches the rAF loop on first call.
  ext.tickFps = function () { attachRafSampler(); };

  function countMeshes(o) { let n = 0; o.traverse(x => { if (x.isMesh) n++; }); return n; }
  // ORBIT FIX — merged bounds are in the rendered (Y-up) space; map the 8 corners
  // through the host's toWorld so the reported bounds stay true-world (Z-up).
  function bbToArray(b) {
    const h = host();
    if (!h || typeof h.toWorld !== 'function') return [b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z];
    let nx = Infinity, ny = Infinity, nz = Infinity, Mx = -Infinity, My = -Infinity, Mz = -Infinity;
    for (let i = 0; i < 8; i++) {
      const w = h.toWorld([(i & 1) ? b.max.x : b.min.x, (i & 2) ? b.max.y : b.min.y, (i & 4) ? b.max.z : b.min.z]);
      nx = Math.min(nx, w[0]); ny = Math.min(ny, w[1]); nz = Math.min(nz, w[2]);
      Mx = Math.max(Mx, w[0]); My = Math.max(My, w[1]); Mz = Math.max(Mz, w[2]);
    }
    return [nx, ny, nz, Mx, My, Mz];
  }

  // ════════════════════════════════════════════════════════════════════════
  // 3D MARKUP — text · arrow · freehand draw · revision cloud · dimension ·
  // callout. (Previously startMarkup/clearMarkup were referenced by the host
  // but never defined, so the whole markup menu was a no-op.)
  //
  // While a markup tool is active we disable OrbitControls rotate so the
  // left button is free for drawing; pan (right-drag) + zoom (wheel) still
  // work. Esc, "Clear markups", or finishing exits and restores rotate.
  // ════════════════════════════════════════════════════════════════════════
  let markupGroup = null;
  let markupMode = null;          // text|arrow|draw|cloud|dim|callout|null
  let markupGesture = null;       // active drag/click gesture
  let markupClicks = [];          // accumulated points for multi-click tools
  let markupPreview = null;       // transient preview object during a drag
  let markupStamps = [];          // children.length after each completed op (for Undo)
  let rotatePrev = null;          // saved controls.enableRotate
  const MARKUP_COLOR = 0xffcc33;
  const markupRay = new THREE.Raycaster();

  function ensureMarkupGroup(h) {
    if (!markupGroup) { markupGroup = new THREE.Group(); markupGroup.name = 'sting-markup'; h.scene.add(markupGroup); }
    return markupGroup;
  }
  function disposeObj(o) {
    o.traverse && o.traverse(c => {
      if (c.geometry && c.geometry.dispose) c.geometry.dispose();
      if (c.material) {
        const m = c.material; if (m.map && m.map.dispose) m.map.dispose();
        if (m.dispose) m.dispose();
      }
    });
  }
  function markupSpan(h) {
    return (h.modelBounds && !h.modelBounds.isEmpty())
      ? h.modelBounds.getSize(new THREE.Vector3()).length() : 10;
  }

  // screen → 3D: raycast the model; fall back to a plane through the orbit
  // target facing the camera so markup still lands on empty space.
  function markupPoint(h, ev) {
    const dom = h.renderer.domElement;
    const rect = dom.getBoundingClientRect();
    const ndc = new THREE.Vector2(
      ((ev.clientX - rect.left) / rect.width) * 2 - 1,
      -((ev.clientY - rect.top) / rect.height) * 2 + 1
    );
    markupRay.setFromCamera(ndc, h.camera);
    if (h.modelRoot) {
      const hits = markupRay.intersectObject(h.modelRoot, true);
      if (hits && hits.length) return hits[0].point.clone();
    }
    const n = new THREE.Vector3(); h.camera.getWorldDirection(n);
    const plane = new THREE.Plane().setFromNormalAndCoplanarPoint(n, h.controls.target);
    const out = new THREE.Vector3();
    return markupRay.ray.intersectPlane(plane, out) ? out : h.controls.target.clone();
  }

  function roundRect(ctx, x, y, w, hh, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + hh, r);
    ctx.arcTo(x + w, y + hh, x, y + hh, r);
    ctx.arcTo(x, y + hh, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
  }
  function makeLabel(h, text, color) {
    const fontSize = 30, pad = 10;
    const c = document.createElement('canvas');
    let ctx = c.getContext('2d');
    ctx.font = fontSize + 'px sans-serif';
    const w = Math.max(24, Math.ceil(ctx.measureText(text).width)) + pad * 2;
    const hh = fontSize + pad * 2;
    c.width = w; c.height = hh;
    ctx = c.getContext('2d');
    ctx.font = fontSize + 'px sans-serif';
    ctx.fillStyle = 'rgba(18,18,26,0.86)'; roundRect(ctx, 0, 0, w, hh, 8); ctx.fill();
    ctx.strokeStyle = color || '#ffcc33'; ctx.lineWidth = 2; roundRect(ctx, 1, 1, w - 2, hh - 2, 7); ctx.stroke();
    ctx.fillStyle = color || '#ffcc33'; ctx.textBaseline = 'middle';
    ctx.fillText(text, pad, hh / 2 + 1);
    const tex = new THREE.CanvasTexture(c); tex.minFilter = THREE.LinearFilter;
    const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false, transparent: true }));
    const s = markupSpan(h) * 0.022;
    sprite.scale.set(s * (w / hh), s, 1);
    sprite.renderOrder = 999;
    return sprite;
  }

  function placeText(h, p, text) {
    const sprite = makeLabel(h, text, '#ffe08a');
    sprite.position.copy(p);
    markupGroup.add(sprite);
  }
  function placeArrow(h, a, b) {
    const g = ensureMarkupGroup(h);
    const mat = new THREE.LineBasicMaterial({ color: MARKUP_COLOR });
    g.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([a, b]), mat));
    // arrowhead cone at b, pointing a→b
    const dir = new THREE.Vector3().subVectors(b, a); const len = dir.length() || 1; dir.normalize();
    const headLen = Math.min(len * 0.25, markupSpan(h) * 0.03);
    const cone = new THREE.Mesh(
      new THREE.ConeGeometry(headLen * 0.5, headLen, 12),
      new THREE.MeshBasicMaterial({ color: MARKUP_COLOR }));
    cone.position.copy(b);
    cone.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
    cone.translateY(-headLen / 2);
    g.add(cone);
  }
  function placePolyline(h, pts, color, closed) {
    if (pts.length < 2) return;
    const arr = closed ? pts.concat([pts[0]]) : pts;
    const g = ensureMarkupGroup(h);
    g.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(arr),
      new THREE.LineBasicMaterial({ color: color || MARKUP_COLOR })));
  }
  function placeDimension(h, a, b) {
    const g = ensureMarkupGroup(h);
    g.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([a, b]),
      new THREE.LineBasicMaterial({ color: 0x4f9dff })));
    addMarker(g, a, 0x4f9dff); addMarker(g, b, 0x4f9dff);
    const mid = new THREE.Vector3().addVectors(a, b).multiplyScalar(0.5);
    const label = makeLabel(h, a.distanceTo(b).toFixed(2) + ' m', '#7fbfff');
    label.position.copy(mid);
    g.add(label);
    if (host() && host().bridge) host().bridge.send('markupDimension', { distance: a.distanceTo(b) });
  }
  function placeCallout(h, anchor, labelPos, text) {
    const g = ensureMarkupGroup(h);
    g.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([anchor, labelPos]),
      new THREE.LineBasicMaterial({ color: MARKUP_COLOR })));
    addMarker(g, anchor, MARKUP_COLOR);
    const label = makeLabel(h, text, '#ffcc33');
    label.position.copy(labelPos);
    g.add(label);
  }
  // Revision cloud: a scalloped loop of arcs around the drag rectangle,
  // built on the plane through the two drag points facing the camera.
  function placeCloud(h, a, b) {
    const g = ensureMarkupGroup(h);
    const n = new THREE.Vector3(); h.camera.getWorldDirection(n); n.normalize();
    let u = new THREE.Vector3(1, 0, 0).cross(n); if (u.lengthSq() < 1e-6) u = new THREE.Vector3(0, 1, 0).cross(n);
    u.normalize();
    const v = new THREE.Vector3().crossVectors(n, u).normalize();
    // a,b projected onto (u,v) about a
    const d = new THREE.Vector3().subVectors(b, a);
    const w = d.dot(u), hgt = d.dot(v);
    const corners = [a.clone(),
      a.clone().addScaledVector(u, w),
      a.clone().addScaledVector(u, w).addScaledVector(v, hgt),
      a.clone().addScaledVector(v, hgt)];
    const bump = Math.max(Math.hypot(w, hgt) * 0.06, markupSpan(h) * 0.01);
    const pts = [];
    for (let e = 0; e < 4; e++) {
      const p0 = corners[e], p1 = corners[(e + 1) % 4];
      const edge = new THREE.Vector3().subVectors(p1, p0); const elen = edge.length();
      const segs = Math.max(2, Math.round(elen / (bump * 1.6)));
      const outward = new THREE.Vector3().crossVectors(edge.clone().normalize(), n).normalize();
      for (let i = 0; i < segs; i++) {
        const t0 = i / segs, tm = (i + 0.5) / segs;
        pts.push(p0.clone().addScaledVector(edge, t0));
        pts.push(p0.clone().addScaledVector(edge, tm).addScaledVector(outward, bump));
      }
    }
    placePolyline(h, pts, 0xff5a5a, true);
  }

  // ── pointer wiring ──
  let _markupHandlers = null;
  function attachMarkupInput(h) {
    detachMarkupInput();
    const dom = h.renderer.domElement;
    const onDown = (ev) => {
      if (ev.button !== 0 || !markupMode) return;
      markupGesture = { start: markupPoint(h, ev), screen: { x: ev.clientX, y: ev.clientY }, points: [] };
      if (markupMode === 'draw') markupGesture.points.push(markupGesture.start);
    };
    const onMove = (ev) => {
      if (!markupGesture) return;
      const p = markupPoint(h, ev);
      if (markupMode === 'draw') { markupGesture.points.push(p); refreshPreview(h, markupGesture.points, false, 0xffcc33); }
      else if (markupMode === 'arrow' || markupMode === 'dim' || markupMode === 'cloud') {
        refreshPreview(h, [markupGesture.start, p], false, markupMode === 'dim' ? 0x4f9dff : 0xffcc33);
      }
    };
    const onUp = (ev) => {
      if (!markupGesture) return;
      const end = markupPoint(h, ev);
      const moved = Math.hypot(ev.clientX - markupGesture.screen.x, ev.clientY - markupGesture.screen.y);
      clearPreview(h);
      finishGesture(h, markupGesture.start, end, moved);
      markupGesture = null;
    };
    dom.addEventListener('pointerdown', onDown);
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    _markupHandlers = { dom, onDown, onMove, onUp };
  }
  function detachMarkupInput() {
    if (!_markupHandlers) return;
    const { dom, onDown, onMove, onUp } = _markupHandlers;
    dom.removeEventListener('pointerdown', onDown);
    window.removeEventListener('pointermove', onMove);
    window.removeEventListener('pointerup', onUp);
    _markupHandlers = null;
  }
  function refreshPreview(h, pts, closed, color) {
    clearPreview(h);
    if (pts.length < 2) return;
    markupPreview = new THREE.Line(
      new THREE.BufferGeometry().setFromPoints(closed ? pts.concat([pts[0]]) : pts),
      new THREE.LineBasicMaterial({ color: color || 0xffffff }));
    ensureMarkupGroup(h).add(markupPreview);
  }
  function clearPreview(h) {
    if (markupPreview && markupGroup) { markupGroup.remove(markupPreview); disposeObj(markupPreview); }
    markupPreview = null;
  }

  const DRAG_THRESHOLD = 5; // px
  function finishGesture(h, start, end, moved) {
    const before = markupGroup ? markupGroup.children.length : 0;
    const dragged = moved > DRAG_THRESHOLD;
    switch (markupMode) {
      case 'text': {
        if (dragged) return;
        const t = (window.prompt('Markup text:') || '').trim();
        if (t) placeText(h, start, t);
        break;
      }
      case 'arrow':
        if (dragged) placeArrow(h, start, end);
        break;
      case 'draw':
        if (markupGesture && markupGesture.points.length >= 2) placePolyline(h, markupGesture.points, 0xffcc33, false);
        break;
      case 'cloud':
        if (dragged) placeCloud(h, start, end);
        break;
      case 'dim':
        // two clicks
        markupClicks.push(start);
        if (markupClicks.length === 2) { placeDimension(h, markupClicks[0], markupClicks[1]); markupClicks = []; }
        break;
      case 'callout':
        markupClicks.push(start);
        if (markupClicks.length === 2) {
          const t = (window.prompt('Callout text:') || '').trim() || 'Note';
          placeCallout(h, markupClicks[0], markupClicks[1], t);
          markupClicks = [];
        }
        break;
    }
    // Record a stamp when this gesture actually placed object(s), so Undo can
    // remove the whole last markup op (not just one of its child meshes).
    if (markupGroup && markupGroup.children.length > before) markupStamps.push(markupGroup.children.length);
  }

  function markupKeydown(ev) { if (ev.key === 'Escape' && markupMode) ext.stopMarkup(); }

  ext.startMarkup = function (mode) {
    const h = host(); if (!h) return;
    ensureMarkupGroup(h);
    markupMode = mode || 'text';
    markupClicks = [];
    if (rotatePrev === null && h.controls) { rotatePrev = h.controls.enableRotate; h.controls.enableRotate = false; }
    attachMarkupInput(h);
    window.addEventListener('keydown', markupKeydown);
    if (h.bridge) h.bridge.send('markupModeChanged', { mode: markupMode });
  };
  ext.stopMarkup = function () {
    const h = host();
    detachMarkupInput();
    clearPreview(h);
    window.removeEventListener('keydown', markupKeydown);
    if (h && h.controls && rotatePrev !== null) { h.controls.enableRotate = rotatePrev; rotatePrev = null; }
    markupMode = null; markupGesture = null; markupClicks = [];
    if (h && h.bridge) h.bridge.send('markupModeChanged', { mode: null });
    // Tell the coordination layer markup exited (Escape / internal) so it can
    // restore the exclusive tool state + pick + hide the markup toolbar.
    try { window.dispatchEvent(new CustomEvent('sting:markupStopped')); } catch (e) {}
  };
  // Undo the last completed markup op (drops the whole op, not one child mesh).
  ext.undoMarkup = function () {
    if (!markupGroup || !markupGroup.children.length) return;
    markupStamps.pop();
    const target = markupStamps.length ? markupStamps[markupStamps.length - 1] : 0;
    while (markupGroup.children.length > target) {
      const obj = markupGroup.children[markupGroup.children.length - 1];
      markupGroup.remove(obj); disposeObj(obj);
    }
  };
  ext.clearMarkup = function () {
    const h = host();
    ext.stopMarkup();
    if (markupGroup && h) { h.scene.remove(markupGroup); disposeObj(markupGroup); }
    markupGroup = null; markupStamps = [];
  };

  // ════════════════════════════════════════════════════════════════════════
  // SECTION BOX — a 6-plane AABB clip (±X/±Y/±Z) over renderer.clippingPlanes,
  // modelled on coordination-viewer's clash-box. Driven by sliders (fractions
  // across the model bounds) AND a draggable per-face gizmo (TransformControls,
  // see below). Cut faces are filled with cap quads so solids don't read hollow.
  // ════════════════════════════════════════════════════════════════════════
  const sb = {
    active: false,
    min: null, max: null,         // THREE.Vector3 in world (rendered) coords
    caps: false,
    capGroup: null,
    savedClip: null,              // previous renderer.clippingPlanes (e.g. clash box)
    onChange: null,               // host sync callback (sliders ↔ gizmo)
  };
  function sbModelBounds() {
    const h = host();
    if (h && h.modelBounds && !h.modelBounds.isEmpty()) return h.modelBounds;
    return new THREE.Box3(new THREE.Vector3(-1, -1, -1), new THREE.Vector3(1, 1, 1));
  }
  function sbPlanes() {
    return [
      new THREE.Plane(new THREE.Vector3( 1, 0, 0), -sb.min.x),
      new THREE.Plane(new THREE.Vector3(-1, 0, 0),  sb.max.x),
      new THREE.Plane(new THREE.Vector3( 0, 1, 0), -sb.min.y),
      new THREE.Plane(new THREE.Vector3( 0,-1, 0),  sb.max.y),
      new THREE.Plane(new THREE.Vector3( 0, 0, 1), -sb.min.z),
      new THREE.Plane(new THREE.Vector3( 0, 0,-1),  sb.max.z),
    ];
  }
  function sbApply() {
    const h = host(); if (!h || !h.renderer || !sb.min) return;
    const planes = sbPlanes();
    if (sb.savedClip === null) sb.savedClip = h.renderer.clippingPlanes;
    h.renderer.clippingPlanes = planes;
    h.renderer.localClippingEnabled = true;
    sb.active = true;
    rebuildCaps(planes);
    if (sb.gizmo) refreshHandles();
    if (typeof sb.onChange === 'function') { try { sb.onChange(ext.getSectionBox()); } catch (e) {} }
  }
  // Per-plane cap quad: a coplanar quad at each cut, clipped by the OTHER 5
  // planes, so the cut reads as a filled surface instead of a hollow shell.
  function rebuildCaps(planes) {
    const h = host(); if (!h) return;
    if (sb.capGroup) { h.scene.remove(sb.capGroup); disposeObj(sb.capGroup); sb.capGroup = null; }
    if (!sb.caps || !sb.min) return;
    sb.capGroup = new THREE.Group(); sb.capGroup.name = 'sting-section-caps';
    const size = new THREE.Vector3().subVectors(sb.max, sb.min);
    const c = new THREE.Vector3().addVectors(sb.min, sb.max).multiplyScalar(0.5);
    const pad = size.length() * 0.001;
    const faces = [
      { n: new THREE.Vector3( 1, 0, 0), pos: new THREE.Vector3(sb.max.x, c.y, c.z), w: size.z, hh: size.y },
      { n: new THREE.Vector3(-1, 0, 0), pos: new THREE.Vector3(sb.min.x, c.y, c.z), w: size.z, hh: size.y },
      { n: new THREE.Vector3( 0, 1, 0), pos: new THREE.Vector3(c.x, sb.max.y, c.z), w: size.x, hh: size.z },
      { n: new THREE.Vector3( 0,-1, 0), pos: new THREE.Vector3(c.x, sb.min.y, c.z), w: size.x, hh: size.z },
      { n: new THREE.Vector3( 0, 0, 1), pos: new THREE.Vector3(c.x, c.y, sb.max.z), w: size.x, hh: size.y },
      { n: new THREE.Vector3( 0, 0,-1), pos: new THREE.Vector3(c.x, c.y, sb.min.z), w: size.x, hh: size.y },
    ];
    faces.forEach((f, i) => {
      const geo = new THREE.PlaneGeometry(Math.abs(f.w) + pad, Math.abs(f.hh) + pad);
      // Cap clipped by the OTHER 5 planes so it only fills inside the box.
      const others = planes.filter((_, j) => j !== i);
      const mat = new THREE.MeshBasicMaterial({
        color: 0xbfd4ff, side: THREE.DoubleSide, transparent: true, opacity: 0.85,
        clippingPlanes: others, clipShadows: false,
      });
      const q = new THREE.Mesh(geo, mat);
      q.position.copy(f.pos);
      q.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), f.n.clone().normalize());
      q.renderOrder = 2;
      sb.capGroup.add(q);
    });
    h.scene.add(sb.capGroup);
  }

  ext.setSectionBox = function (p) {
    p = p || {};
    const mb = sbModelBounds();
    sb.min = (p.min && p.min.length === 3) ? new THREE.Vector3(p.min[0], p.min[1], p.min[2]) : mb.min.clone();
    sb.max = (p.max && p.max.length === 3) ? new THREE.Vector3(p.max[0], p.max[1], p.max[2]) : mb.max.clone();
    if (p.enabled === false) { ext.clearSectionBox(); return; }
    sbApply();
  };
  // Move one face to a 0..1 fraction across the model bounds (slider/gizmo path).
  ext.setSectionBoxFace = function (axis, end, frac) {
    if (!sb.min) ext.setSectionBox({});
    const mb = sbModelBounds();
    const lo = mb.min[axis], hi = mb.max[axis];
    let v = lo + (hi - lo) * Math.max(0, Math.min(1, frac));
    const minEps = (hi - lo) * 0.01 || 1e-3;
    if (end === 'min') { v = Math.min(v, sb.max[axis] - minEps); sb.min[axis] = v; }
    else               { v = Math.max(v, sb.min[axis] + minEps); sb.max[axis] = v; }
    sbApply();
  };
  // Set a face by ABSOLUTE world value (gizmo drag), clamped so faces can't cross.
  function setSectionBoxFaceWorld(axis, end, world) {
    const mb = sbModelBounds();
    const minEps = (mb.max[axis] - mb.min[axis]) * 0.01 || 1e-3;
    if (end === 'min') sb.min[axis] = Math.min(world, sb.max[axis] - minEps);
    else               sb.max[axis] = Math.max(world, sb.min[axis] + minEps);
    sbApply();
  }
  ext.getSectionBox = function () {
    if (!sb.active || !sb.min) return { active: false };
    const mb = sbModelBounds();
    const frac = (axis, v) => { const lo = mb.min[axis], hi = mb.max[axis]; return hi > lo ? (v - lo) / (hi - lo) : 0; };
    return {
      active: true, caps: sb.caps,
      fractions: {
        minX: frac('x', sb.min.x), maxX: frac('x', sb.max.x),
        minY: frac('y', sb.min.y), maxY: frac('y', sb.max.y),
        minZ: frac('z', sb.min.z), maxZ: frac('z', sb.max.z),
      },
    };
  };
  ext.setSectionCaps = function (on) { sb.caps = !!on; rebuildCaps(sbPlanes()); };
  ext.onSectionChange = function (cb) { sb.onChange = cb; };
  ext.clearSectionBox = function () {
    const h = host();
    detachSectionGizmo();
    if (sb.capGroup && h) { h.scene.remove(sb.capGroup); disposeObj(sb.capGroup); }
    sb.capGroup = null;
    if (h && h.renderer) h.renderer.clippingPlanes = (sb.savedClip !== null) ? sb.savedClip : [];
    sb.savedClip = null; sb.active = false; sb.min = sb.max = null;
    if (typeof sb.onChange === 'function') { try { sb.onChange({ active: false }); } catch (e) {} }
  };
  // The host routes both clearSectionPlanes + clearSectionBox here.
  ext.clearSectionPlanes = function () { ext.clearSectionBox(); };

  // ── Draggable per-face gizmo (TransformControls) ──────────────────────────
  // Six tiny handle meshes at the face centres. A single re-attachable
  // TransformControls (translate, axis-locked to the grabbed face's axis) resizes
  // ONLY that face. dragging-changed disables OrbitControls so the drag is clean.
  let TC = null;
  function getTC() {
    if (TC !== null) return TC;
    TC = (typeof THREE !== 'undefined' && THREE.TransformControls) ? THREE.TransformControls : false;
    return TC;
  }
  function makeHandle(axis, end) {
    const m = new THREE.Mesh(
      new THREE.SphereGeometry(1, 16, 16),
      new THREE.MeshBasicMaterial({ color: 0x3b82f6, depthTest: false }));
    m.renderOrder = 999;
    m.userData.sbFace = { axis, end };
    return m;
  }
  function refreshHandles() {
    if (!sb.gizmo || !sb.min) return;
    const c = new THREE.Vector3().addVectors(sb.min, sb.max).multiplyScalar(0.5);
    const span = new THREE.Vector3().subVectors(sb.max, sb.min).length();
    const r = Math.max(span * 0.012, 1e-3);
    const place = (h, axis, end) => {
      h.position.set(c.x, c.y, c.z);
      h.position[axis] = (end === 'min') ? sb.min[axis] : sb.max[axis];
      h.scale.setScalar(r);
    };
    place(sb.handles.minX, 'x', 'min'); place(sb.handles.maxX, 'x', 'max');
    place(sb.handles.minY, 'y', 'min'); place(sb.handles.maxY, 'y', 'max');
    place(sb.handles.minZ, 'z', 'min'); place(sb.handles.maxZ, 'z', 'max');
  }
  ext.attachSectionGizmo = function () {
    const h = host(); if (!h || !sb.active) return false;
    const tc = getTC(); if (!tc) return false;          // TransformControls unavailable → sliders only
    if (sb.gizmo) return true;
    sb.handleGroup = new THREE.Group(); sb.handleGroup.name = 'sting-section-handles';
    sb.handles = {
      minX: makeHandle('x', 'min'), maxX: makeHandle('x', 'max'),
      minY: makeHandle('y', 'min'), maxY: makeHandle('y', 'max'),
      minZ: makeHandle('z', 'min'), maxZ: makeHandle('z', 'max'),
    };
    Object.values(sb.handles).forEach(m => sb.handleGroup.add(m));
    h.scene.add(sb.handleGroup);
    const gizmo = new tc(h.camera, h.renderer.domElement);
    gizmo.setMode('translate');
    sb.gizmo = gizmo;
    // r169 — add the gizmo's HELPER object (not the controls) to the scene.
    sb.gizmoHelper = (typeof gizmo.getHelper === 'function') ? gizmo.getHelper() : gizmo;
    h.scene.add(sb.gizmoHelper);
    gizmo.addEventListener('dragging-changed', (e) => { h.controls.enabled = !e.value; });
    gizmo.addEventListener('objectChange', () => {
      const o = gizmo.object; if (!o || !o.userData.sbFace) return;
      const { axis, end } = o.userData.sbFace;
      setSectionBoxFaceWorld(axis, end, o.position[axis]);  // also re-places handles via sbApply→refreshHandles
    });
    // Pointerdown on a handle attaches the gizmo to it, axis-locked to its face.
    sb._onHandleDown = (ev) => {
      if (ev.button !== 0) return;
      const rect = h.renderer.domElement.getBoundingClientRect();
      const ndc = new THREE.Vector2(
        ((ev.clientX - rect.left) / rect.width) * 2 - 1,
        -((ev.clientY - rect.top) / rect.height) * 2 + 1);
      const ray = new THREE.Raycaster(); ray.setFromCamera(ndc, h.camera);
      const hit = ray.intersectObjects(Object.values(sb.handles), false)[0];
      if (!hit) return;
      const f = hit.object.userData.sbFace;
      gizmo.attach(hit.object);
      gizmo.showX = f.axis === 'x'; gizmo.showY = f.axis === 'y'; gizmo.showZ = f.axis === 'z';
    };
    h.renderer.domElement.addEventListener('pointerdown', sb._onHandleDown);
    refreshHandles();
    return true;
  };
  function detachSectionGizmo() {
    const h = host();
    if (!sb.gizmo) return;
    try { sb.gizmo.detach(); } catch (e) {}
    if (h) {
      if (sb._onHandleDown) h.renderer.domElement.removeEventListener('pointerdown', sb._onHandleDown);
      if (sb.gizmoHelper) h.scene.remove(sb.gizmoHelper);
      if (sb.handleGroup) { h.scene.remove(sb.handleGroup); disposeObj(sb.handleGroup); }
      if (h.controls) h.controls.enabled = true;
    }
    if (sb.gizmo.dispose) { try { sb.gizmo.dispose(); } catch (e) {} }
    sb.gizmo = null; sb.gizmoHelper = null; sb.handleGroup = null; sb.handles = null; sb._onHandleDown = null;
  }
  ext.detachSectionGizmo = detachSectionGizmo;
  ext.setSectionGizmoMode = function (mode) {     // 'translate' (move box) | 'rotate' (oblique)
    if (sb.gizmo) sb.gizmo.setMode(mode === 'rotate' ? 'rotate' : 'translate');
  };
  ext.setSectionSnap = function (step) {
    if (sb.gizmo && sb.gizmo.setTranslationSnap) sb.gizmo.setTranslationSnap(step || null);
  };

  // ════════════════════════════════════════════════════════════════════════
  // 3D EXPLODE — displace every element from the model centre by a factor (0..N).
  // Works on ANY model (not just federated). Visual only (positions, no geometry
  // edits) so it coexists with section + selection. Grouping: radial | level |
  // discipline. World-space displacement is converted to each mesh's PARENT-local
  // space so the Z-up→Y-up pivot rotation is respected.
  // ════════════════════════════════════════════════════════════════════════
  const expl = { factor: 0, mode: 'radial', orig: new Map() };
  function explHashAngle(key) {
    let h = 0; const s = String(key);
    for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0;
    return (Math.abs(h) % 360) * Math.PI / 180;
  }
  ext.setExplodeMode = function (mode) {
    expl.mode = (mode === 'level' || mode === 'discipline') ? mode : 'radial';
    if (expl.factor > 0) ext.setExplodeFactor(expl.factor);   // re-apply with the new grouping
  };
  ext.setExplodeFactor = function (factor) {
    const h = host(); if (!h || !h.modelRoot) return;
    expl.factor = factor;
    const mb = h.modelBounds;
    const centre = (mb && !mb.isEmpty()) ? mb.getCenter(new THREE.Vector3()) : new THREE.Vector3();
    const span = (mb && !mb.isEmpty()) ? mb.getSize(new THREE.Vector3()).length() : 1;
    const tmpBox = new THREE.Box3();
    const tmpQ = new THREE.Quaternion();
    h.modelRoot.traverse(o => {
      if (!o.isMesh) return;
      if (!expl.orig.has(o.uuid)) expl.orig.set(o.uuid, o.position.clone());
      const orig = expl.orig.get(o.uuid);
      if (factor <= 0) { o.position.copy(orig); return; }
      tmpBox.setFromObject(o);
      if (tmpBox.isEmpty()) { o.position.copy(orig); return; }
      const ec = tmpBox.getCenter(new THREE.Vector3());   // world centre of the element
      let dir;
      if (expl.mode === 'level') {
        dir = new THREE.Vector3(0, Math.sign(ec.y - centre.y) || 1, 0);
      } else if (expl.mode === 'discipline') {
        const key = o.userData.discipline || o.userData.category || o.userData.modelId || o.uuid;
        const a = explHashAngle(key);
        dir = new THREE.Vector3(Math.cos(a), 0, Math.sin(a));
      } else {
        dir = new THREE.Vector3().subVectors(ec, centre);
        if (dir.lengthSq() < 1e-9) dir.set(0, 1, 0);
        dir.normalize();
      }
      const disp = dir.multiplyScalar(factor * span * 0.25);   // world-space displacement
      if (o.parent) o.parent.getWorldQuaternion(tmpQ); else tmpQ.identity();
      disp.applyQuaternion(tmpQ.invert());                     // → parent-local space
      o.position.copy(orig).add(disp);
    });
  };
  ext.clearExplode = function () { ext.setExplodeFactor(0); };
  ext.getExplode = function () { return { factor: expl.factor, mode: expl.mode }; };
})();
