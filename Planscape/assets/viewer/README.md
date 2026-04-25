# Planscape 3D viewer assets

`viewer.html` is the self-contained three.js viewer loaded inside a `<WebView>`
by `src/components/ModelViewer.tsx`. It expects three.js + GLTFLoader +
OrbitControls to live **next to** this README.

## One-time setup (CI or local)

Download the three.js UMD bundle and the two loaders that match. We pin r160
to keep behaviour deterministic across devices:

```bash
cd Planscape/assets/viewer

# 1. Three core (UMD, minified)
curl -fsSL -o three.min.js         https://unpkg.com/three@0.160.0/build/three.min.js

# 2. GLTFLoader (examples bundle, UMD shim)
curl -fsSL -o GLTFLoader.js        https://unpkg.com/three@0.160.0/examples/js/loaders/GLTFLoader.js

# 3. OrbitControls (examples bundle, UMD shim)
curl -fsSL -o OrbitControls.js     https://unpkg.com/three@0.160.0/examples/js/controls/OrbitControls.js
```

(Alternative — if the team already mirrors three.js internally, set
`VIEWER_ASSETS_URL` in CI and copy from there.)

Commit the three files alongside `viewer.html`. They're ~700 KB total gzipped —
tiny compared to a typical model. Shipping them as static assets keeps the
viewer fully offline on site.

## File layout

```
Planscape/assets/viewer/
├── viewer.html            ← main page loaded by WebView
├── three.min.js           ← three.js r160 UMD (fetch via curl above)
├── GLTFLoader.js          ← examples/js/loaders
├── OrbitControls.js       ← examples/js/controls
└── README.md              ← this file
```

`viewer.html` gracefully degrades — if any of the three script files are
missing, the viewer shows a friendly error instead of a blank screen.

## Bridge protocol (RN ↔ WebView)

`ModelViewer.tsx` posts JSON strings to the viewer; the viewer posts JSON
strings back via `window.ReactNativeWebView.postMessage`.

### RN → viewer commands

| `type`            | `payload`                                                     |
|-------------------|---------------------------------------------------------------|
| `load`            | `{ url: string }`  load a glTF/GLB from the given URL        |
| `elementMap`      | `{ map: { [guid]: { tag, name, category, discipline } } }`   |
| `setTool`         | `{ tool: "pick" \| "measure" \| "section" \| "pin" }`        |
| `fit`             | —                                                             |
| `addPin`          | `{ id, x, y, z, priority }`                                   |
| `setPins`         | `Pin[]`                                                       |
| `clearPins`       | —                                                             |
| `clearMeasure`    | —                                                             |
| `clearHighlight`  | —                                                             |
| `setDiscipline`   | `{ discipline: "M" \| "E" \| ..., visible: boolean }`         |
| `setBackground`   | `{ color: number }`                                           |

### Viewer → RN events

| `type`          | `payload`                                                       |
|-----------------|-----------------------------------------------------------------|
| `ready`         | `{ platform: "webview" }`                                       |
| `loaded`        | `{ elementCount, bounds: [minX..maxZ] }`                        |
| `pick`          | `{ guid, point: [x,y,z], name, meta }`                          |
| `placeIssue`    | `{ guid, meta, point: [x,y,z] }`  (long-press or pin tool)      |
| `pinTap`        | `{ issueId, priority }`                                         |
| `measure`       | `{ distance, points: [[x,y,z],[x,y,z]] }`                       |
| `toolChanged`   | `{ tool }`                                                      |

## Updating the viewer

Edit `viewer.html` directly — no build step. Increment the version
comment at the top if you want to bust the WebView cache on-device.
