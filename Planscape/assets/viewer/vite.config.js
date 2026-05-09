// T4-29 — Viewer build pipeline.
//
// The existing viewer ships as plain <script src> tags in viewer.html
// referencing co-located JS files. That works (no build step needed),
// but every page load downloads the un-minified, un-tree-shaken
// sources — three.min.js (already minified) plus our 3,800-line
// coordination-viewer.js + 1,300-line CSS + 200-line shim. Total
// ~500 KB transfer for a cold open.
//
// This Vite config produces a `dist/` bundle that:
//   - Minifies coordination-viewer.js + signalr-shim.js + viewer-extras.js
//     with ESBuild (~3× smaller, ~6× faster transfer on 3G).
//   - Tree-shakes @microsoft/signalr to drop the WebSocket-fallback
//     transports we don't use.
//   - Emits source maps (kept for debugging; gated by build:prod).
//   - Preserves the same output filenames so viewer.html doesn't need
//     to change — `dist/coordination-viewer.js` replaces the in-tree
//     source at deploy time.
//
// Runtime contract preserved:
//   - file:// scheme keeps working (RN WebView's bundled host path)
//   - Authorization header pattern unchanged
//   - SignalR shim still attaches to window.__planscapeHub
//
// Dev workflow:
//   $ npm install        # one-time
//   $ npm run dev        # vite dev server with HMR for local iterations
//   $ npm run build      # produces dist/ for deployment
//   $ npm run check      # node --check on all three JS files (CI gate)
//
// CI integration deferred: the existing pipeline serves files in-tree.
// Cutting over to dist/ requires a small change to whatever serves
// `Planscape.Server/src/Planscape.API/wwwroot/viewer/`.
import { defineConfig } from 'vite';
import { resolve } from 'node:path';

export default defineConfig({
  // Base path matches the runtime fetch path the API server uses to
  // serve viewer assets (existing pattern). Change to '/viewer/' if
  // the host moves the bundle behind a route prefix.
  base: './',

  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: true,
    target: 'es2020',           // covers Chrome 80+, Safari 13+, Edge 80+;
                                // RN WebView is at-least Chromium 80
    minify: 'esbuild',
    cssMinify: true,
    cssCodeSplit: false,        // single CSS bundle keeps viewer.html simple
    rollupOptions: {
      // Multi-entry build: each script tag in viewer.html maps to one
      // entry. ES modules so tree-shaking works against @microsoft/signalr.
      input: {
        'coordination-viewer': resolve(__dirname, 'coordination-viewer.js'),
        'signalr-shim':        resolve(__dirname, 'signalr-shim.js'),
        'viewer-extras':       resolve(__dirname, 'viewer-extras.js'),
      },
      output: {
        // Drop the hash so existing <script src="./coordination-viewer.js">
        // tags in viewer.html keep resolving without a templating step.
        // Cache-busting moves to the host server's ETag / Cache-Control.
        entryFileNames: '[name].js',
        chunkFileNames: 'chunks/[name]-[hash].js',
        assetFileNames: '[name][extname]',
        // Lock format to IIFE so the bundles still run in <script>
        // (not <script type="module">), preserving file:// loading
        // inside the React Native WebView host.
        format: 'iife',
        inlineDynamicImports: false,
        // Avoid name collisions when two entries import the same
        // module — IIFE bundles each get their own scope.
        manualChunks: undefined,
      },
      // The viewer's bundled three.min.js is loaded as a sibling
      // <script> tag and exposes window.THREE. Mark it external so
      // we don't double-bundle it.
      external: ['three', /^three\//],
    },
    chunkSizeWarningLimit: 1000,
  },

  // Dev server — only used by `npm run dev` for local iteration.
  // Production deployment uses the static dist/ output.
  server: {
    port: 5174,
    strictPort: false,
    open: '/viewer.html',
    // Permit the existing relative API base if the dev server is
    // hosting both viewer + API behind a shared origin.
    proxy: {
      '/api':   { target: 'http://localhost:5000', changeOrigin: true },
      '/hubs':  { target: 'http://localhost:5000', ws: true,    changeOrigin: true },
      '/bcf':   { target: 'http://localhost:5000', changeOrigin: true },
    },
  },

  // ESBuild transform — kept conservative. Aggressive minification
  // can rename the engine bridge symbols (`window.STING_VIEWER`,
  // `window.__planscapeHub`) which break runtime contracts. The
  // reserved list pins those.
  esbuild: {
    legalComments: 'none',
    keepNames: true,
    minify: true,
    target: 'es2020',
  },
});
