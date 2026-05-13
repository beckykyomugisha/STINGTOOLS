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

  // ── Oblique section plane (arbitrary normal + offset) ─────────────────
  ext.setSectionPlane = function ({ normal, offset, enabled }) {
    const h = host(); if (!h) return;
    if (!enabled) { h.renderer.clippingPlanes = []; return; }
    const n = (normal && normal.length === 3)
      ? new THREE.Vector3(normal[0], normal[1], normal[2]).normalize()
      : new THREE.Vector3(0, -1, 0);
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
      // Position camera at floor + eye height along the chosen up axis.
      const floor = upAxis.x ? h.modelBounds.min.x
                  : upAxis.y ? h.modelBounds.min.y
                             : h.modelBounds.min.z;
      const pos = c.clone();
      if (upAxis.x) pos.x = floor + eye;
      else if (upAxis.y) pos.y = floor + eye;
      else pos.z = floor + eye;
      h.camera.position.copy(pos);
      // Look towards a point one metre forward along an axis perpendicular
      // to the up axis so the view starts roughly horizontal.
      const lookAt = pos.clone();
      if (upAxis.x) lookAt.y += 1; else lookAt.x += 1;
      h.camera.lookAt(lookAt);
      walkUp = upAxis;
      h.bridge.send('walkthrough', { active: true, upAxis: upAxis.toArray() });
    } else {
      detachWalkInput();
      walkVelocity.set(0, 0, 0);
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
      // Rotate around the model's up axis (yaw) first, then around the
      // camera's local right axis (pitch). This keeps look-left/right and
      // look-up/down correct for both Y-up and Z-up models.
      const yawQ = new THREE.Quaternion().setFromAxisAngle(walkUp, -walkInput.lookX);
      h.camera.quaternion.premultiply(yawQ);
      const right = new THREE.Vector3(1, 0, 0).applyQuaternion(h.camera.quaternion);
      const pitchQ = new THREE.Quaternion().setFromAxisAngle(right, -walkInput.lookY);
      h.camera.quaternion.premultiply(pitchQ);
      // Clamp pitch so the camera can't flip upside-down.
      const up2 = walkUp.clone().applyQuaternion(h.camera.quaternion);
      if (up2.dot(walkUp) < 0.05) {
        h.camera.quaternion.premultiply(new THREE.Quaternion().setFromAxisAngle(right, walkInput.lookY));
      }
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
  function bbToArray(b) { return [b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z]; }
})();
