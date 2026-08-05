// S5.2 — converter sidecar HTTP server.
//
// POST /chunk
//   { sourceUrl: string, projectId: uuid, tenantId: uuid, disciplineHint?: string }
//   → walks the GLB, splits by node-name discipline prefix, writes
//     each chunk back to /api/scene-nodes (tenant-scoped storage),
//     returns the manifest the SceneNode rows are minted from.
//
// POST /health
//   → 200 OK
//
// The endpoint is internal (called by the API only) so auth is via a
// shared bearer token in env (CONVERTER_TOKEN). Outbound calls to the
// API carry the API_BEARER token so writes hit the platform tenant.

import express from 'express';
import { NodeIO } from '@gltf-transform/core';
import { KHRDracoMeshCompression } from '@gltf-transform/extensions';
import { draco, prune, dedup, weld } from '@gltf-transform/functions';
import draco3d from 'draco3dgltf';
import fetch from 'node-fetch';
import crypto from 'crypto';

const PORT = process.env.PORT || 7700;
const API_BASE = process.env.API_BASE || 'http://api:8080';
const API_BEARER = process.env.API_BEARER || '';
const CONVERTER_TOKEN = process.env.CONVERTER_TOKEN || '';

const app = express();
app.use(express.json({ limit: '10mb' }));

app.get('/health', (_req, res) => res.json({ ok: true }));

app.post('/chunk', async (req, res) => {
  if (CONVERTER_TOKEN && req.headers['x-converter-token'] !== CONVERTER_TOKEN) {
    return res.status(401).json({ error: 'unauthorised' });
  }
  const { sourceUrl, projectId, tenantId, sourceModelId } = req.body;
  if (!sourceUrl || !projectId || !tenantId || !sourceModelId) {
    return res.status(400).json({ error: 'sourceUrl, projectId, tenantId, sourceModelId required' });
  }
  try {
    const manifest = await chunkAndUpload({ sourceUrl, projectId, tenantId, sourceModelId });
    res.json(manifest);
  } catch (err) {
    console.error('chunk failed', err);
    res.status(500).json({ error: String(err) });
  }
});

async function chunkAndUpload({ sourceUrl, projectId, tenantId, sourceModelId }) {
  // 1. Pull the source GLB.
  const buf = Buffer.from(await (await fetch(sourceUrl)).arrayBuffer());
  const io = new NodeIO()
    .registerExtensions([KHRDracoMeshCompression])
    .registerDependencies({
      'draco3d.decoder': await draco3d.createDecoderModule(),
      'draco3d.encoder': await draco3d.createEncoderModule(),
    });
  const doc = await io.readBinary(buf);

  // 2. Group nodes by discipline. Convention: glTF node names emitted by
  //    the Revit plugin start with the discipline code in square brackets:
  //    "[M] Air Handling Unit 01"; if absent we put the node in "ANY".
  const groups = new Map();
  for (const node of doc.getRoot().listNodes()) {
    const m = /^\[(\w{1,3})\]/.exec(node.getName() || '');
    const disc = m ? m[1].toUpperCase() : 'ANY';
    if (!groups.has(disc)) groups.set(disc, []);
    groups.get(disc).push(node);
  }

  const manifest = { sceneNodes: [] };

  // 3. Per-group: clone the doc, drop everything not in the group, optimise + Draco,
  //    upload to /api/scene-nodes, append to manifest.
  for (const [disc, keepNodes] of groups) {
    const sub = doc.clone();
    const keepSet = new Set(keepNodes.map((n) => n.getName()));
    for (const n of [...sub.getRoot().listNodes()]) {
      if (!keepSet.has(n.getName())) n.dispose();
    }
    await sub.transform(prune(), dedup(), weld(), draco({ method: 'edgebreaker' }));
    const chunkBytes = Buffer.from(await io.writeBinary(sub));
    const hash = crypto.createHash('sha256').update(chunkBytes).digest('hex');

    // Compute AABB from the remaining accessors.
    const aabb = computeAabb(sub);

    // Upload via the API (which tenant-scopes the storage path).
    const fd = new FormData();
    fd.append('file', new Blob([chunkBytes], { type: 'model/gltf-binary' }), `${disc}.glb`);
    fd.append('projectId', projectId);
    fd.append('tenantId', tenantId);
    fd.append('sourceModelId', sourceModelId);
    fd.append('discipline', disc);
    fd.append('hash', hash);
    fd.append('vertexCount', String(countVertices(sub)));
    fd.append('compression', 'draco');
    fd.append('aabb', JSON.stringify(aabb));

    const resp = await fetch(`${API_BASE}/api/scene-nodes/ingest`, {
      method: 'POST',
      headers: API_BEARER ? { Authorization: `Bearer ${API_BEARER}` } : {},
      body: fd,
    });
    if (!resp.ok) throw new Error(`ingest failed for ${disc}: ${await resp.text()}`);

    const row = await resp.json();
    manifest.sceneNodes.push({ discipline: disc, id: row.id, hash, sizeBytes: chunkBytes.length });
  }

  return manifest;
}

function computeAabb(doc) {
  let min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
  for (const acc of doc.getRoot().listAccessors()) {
    if (acc.getType() !== 'VEC3') continue;
    const aMin = acc.getMin([0, 0, 0]);
    const aMax = acc.getMax([0, 0, 0]);
    for (let i = 0; i < 3; i++) {
      if (aMin[i] < min[i]) min[i] = aMin[i];
      if (aMax[i] > max[i]) max[i] = aMax[i];
    }
  }
  return { minX: min[0], minY: min[1], minZ: min[2], maxX: max[0], maxY: max[1], maxZ: max[2] };
}

function countVertices(doc) {
  let n = 0;
  for (const acc of doc.getRoot().listAccessors()) {
    if (acc.getType() === 'VEC3') n += acc.getCount();
  }
  return n;
}

app.listen(PORT, () => console.log(`converter-sidecar listening on :${PORT}`));
