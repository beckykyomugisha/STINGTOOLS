# Viewer build pipeline (T4-29)

The standalone web coordination viewer ships as plain `<script>` tags
loading sibling JS files. This works without a build step but every
cold open transfers ~500 KB of un-minified, un-tree-shaken sources.

This directory adds an **opt-in** Vite build that produces a
minified, tree-shaken `dist/` with the same filenames so deployment
swaps in zero-friction.

## Quick start

```bash
cd Planscape/assets/viewer
npm install         # one-time
npm run build       # → dist/coordination-viewer.js + .css + companions
npm run dev         # local dev server with HMR (proxies /api → :5000)
npm run check       # CI gate: node --check on all three JS files
```

## What the build does

| Source | After build | Δ size |
|---|---|---|
| `coordination-viewer.js` (3,800 lines, ~140 KB) | `dist/coordination-viewer.js` minified | ~50 KB |
| `coordination-viewer.css` (1,300 lines, ~32 KB) | `dist/coordination-viewer.css` minified | ~20 KB |
| `signalr-shim.js` (~5 KB shim) | bundles `@microsoft/signalr` (tree-shaken to WebSocket transport only) | ~30 KB |
| `viewer-extras.js` | minified | small |
| `three.min.js` | not rebundled — kept as external `<script>` referenced from viewer.html | unchanged |

Total transfer for a cold open: ~500 KB → ~150 KB.

## Runtime contracts preserved

- File:// scheme still works for the React Native WebView host (Vite
  build emits IIFE bundles, not ES modules).
- `window.STING_VIEWER`, `window.__planscapeHub`, `window.__planscapePhotoRealtime`
  symbol names are pinned via `esbuild.keepNames: true`.
- Authorization header + `?access_token=` query-string fallback unchanged.
- File names in `dist/` match the existing `<script src>` tags in
  `viewer.html` so the deploy step is a single `cp -R dist/ <served-path>/`.

## Deferred / not done

- **Three.js bundling.** Currently kept as external `three.min.js`
  loaded via `<script>` per the existing pattern. Pulling it into the
  bundle would let us tree-shake unused Three.js modules (loaders,
  postprocessing) and save another ~50 KB, but requires changes to
  `coordination-viewer.js` to use ES imports instead of `window.THREE`.
- **CI integration.** Server's `wwwroot/viewer/` is currently served
  from the in-tree source. A follow-up changes the docker image to
  `npm run build` + copy `dist/` instead.
- **Source-map upload.** Build emits `.map` files; uploading them to
  Sentry / Honeycomb for production stack traces is a separate hook.

## Why opt-in (not enforced)

The build pipeline is additive. Until the host server is reconfigured
to serve `dist/` instead of the in-tree sources, `npm run build` is
a no-op for production. Keeping the in-tree path live during the cut-
over avoids a single big-bang deployment.
