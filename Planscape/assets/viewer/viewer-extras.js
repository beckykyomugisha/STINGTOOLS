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
      const eyeHeight = 1.7; // metres — assume mm units, see eyeFromBounds
      const startY = h.modelBounds.min.y + eyeFromBounds(h.modelBounds);
      const c = h.modelBounds.getCenter(new THREE.Vector3());
      h.camera.position.set(c.x, startY, c.z);
      h.camera.lookAt(c.x + 1, startY, c.z);
      h.bridge.send('walkthrough', { active: true });
    } else {
      detachWalkInput();
      walkVelocity.set(0, 0, 0);
      h.bridge.send('walkthrough', { active: false });
    }
  };

  function eyeFromBounds(b) {
    const sz = b.getSize(new THREE.Vector3());
    // 1.7 metres in mm-units; if the scene looks tiny (units in metres) fall
    // back to 1.7 absolute. Heuristic: floor span > 100 → mm.
    return Math.max(sz.x, sz.z) > 100 ? 1700 : 1.7;
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
    const speed = Math.max(h.modelBounds.getSize(new THREE.Vector3()).length() * 0.05, 1);
    const fwd = new THREE.Vector3();
    h.camera.getWorldDirection(fwd); fwd.y = 0; fwd.normalize();
    const right = new THREE.Vector3().crossVectors(fwd, new THREE.Vector3(0, 1, 0)).normalize();
    walkVelocity.set(0, 0, 0)
      .addScaledVector(fwd, walkInput.fwd * speed)
      .addScaledVector(right, walkInput.right * speed);
    h.camera.position.addScaledVector(walkVelocity, dt);
    if (walkInput.lookX || walkInput.lookY) {
      const e = new THREE.Euler().setFromQuaternion(h.camera.quaternion, 'YXZ');
      e.y -= walkInput.lookX;
      e.x -= walkInput.lookY;
      e.x = Math.max(-1.4, Math.min(1.4, e.x));
      h.camera.quaternion.setFromEuler(e);
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

  // ── Auto-LOD: drop pixel ratio + antialias when fps sags ──────────────
  let fpsSamples = []; let lastFrame = performance.now(); let lodLevel = 0;
  ext.tickFps = function () {
    const now = performance.now();
    const dt = now - lastFrame; lastFrame = now;
    if (dt <= 0 || dt > 1000) return;
    const fps = 1000 / dt;
    fpsSamples.push(fps);
    if (fpsSamples.length > 60) fpsSamples.shift();
    if (fpsSamples.length === 60) {
      const avg = fpsSamples.reduce((s, x) => s + x, 0) / 60;
      const h = host(); if (!h) return;
      if (avg < 24 && lodLevel === 0) {
        h.renderer.setPixelRatio(1);
        lodLevel = 1;
        h.bridge.send('lodChanged', { level: 1, avgFps: avg });
      } else if (avg < 18 && lodLevel === 1) {
        // Hide every Nth mesh to ease load.
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
      fpsSamples = [];
    }
  };

  function countMeshes(o) { let n = 0; o.traverse(x => { if (x.isMesh) n++; }); return n; }
  function bbToArray(b) { return [b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z]; }
})();
