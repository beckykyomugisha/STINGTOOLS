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

## Tree-shaken three.js bundle

`build.mjs` lists the exact Three.js classes the viewer uses (24 core
+ 4 addons) instead of `export * from 'three'`. esbuild treats the
re-export entry point as a normal ESM source and drops every
unreferenced export from `dist/three.min.js`. Refresh the list when
new `THREE.<symbol>` lookups appear in `viewer.html` /
`viewer-extras.js` / `coordination-viewer.js`:

```bash
grep -oh 'THREE\.[A-Z][A-Za-z0-9]*' viewer.html viewer-extras.js \
  coordination-viewer.js | sort -u
```

The runtime contract is unchanged: `dist/three.min.js` still defines
`window.THREE` with the same class shape, so neither `viewer.html`
nor `viewer-extras.js` needed code changes.

## Docker / CI integration

The `Planscape.Server/docker/Dockerfile` now has a dedicated
`viewer` stage (Node 20-alpine) that runs `npm ci && npm run build`
and stages the resulting `dist/` over `wwwroot/` before the dotnet
publish step. The MSBuild `SyncCoordinationViewer` target is gated on
`PlanscapeServeDist=true` so the in-tree sources don't overwrite the
minified bundle inside the container.

Local dev is unchanged — `PlanscapeServeDist` is unset by default, so
`dotnet build` still syncs source → wwwroot every build. Pass
`--build-arg PLANSCAPE_SERVE_DIST=false` to `docker build` if you
need to ship raw sources in the image (e.g. to attach a debugger
against unminified line numbers).

## Deferred / not done

- **Source-map upload.** Build emits `.map` files; uploading them to
  Sentry / Honeycomb for production stack traces is a separate hook.

## Why opt-in (not enforced)

The build pipeline is additive. Until the host server is reconfigured
to serve `dist/` instead of the in-tree sources, `npm run build` is
a no-op for production. Keeping the in-tree path live during the cut-
over avoids a single big-bang deployment.
