# Exporting real Revit material textures to glTF (Phase 2, hardened)

`StingTools/BIMManager/RevitGltfExporter.cs` can embed real Revit material **textures**
(diffuse bitmaps + PBR factors + UVs) into the published GLB, so brick / tile / wood
surfaces carry their image in the web viewer instead of a flat colour. It is **off by
default** (lean coordination / low-bandwidth exports are unchanged) and **runtime-unverified
from CI** — it must be verified on a Revit machine by re-publishing a textured model and
reading `StingTools.log`.

## Enable it

```
setx PLANSCAPE_EXPORT_TEXTURES 1     # (or RevitGltfExporter.ExportTextures = true)
```
…then **restart Revit** (so the env var is picked up) and **re-publish** a model. With the
flag off, the exporter writes flat `baseColorFactor` exactly as before.

## What it does

- **Appearance → bitmap** (per material): `Material.AppearanceAssetId` →
  `AppearanceAssetElement.GetRenderingAsset()`, then a **recursive walk** of the connected
  asset graph for a `UnifiedBitmap` — preferring diffuse/colour/albedo-named branches
  (`generic_diffuse`, `diffuse`, `color_map`, `surface_albedo`, `base_color`, `albedo`),
  falling back to any bitmap. Also reads `generic_glossiness`→roughness,
  `generic_is_metal`→metalness, `generic_transparency`→alpha (`alphaMode=BLEND`), and a
  bump/normal map when present.
- **Path resolution**: absolute path used if it exists; otherwise the **filename is searched**
  in the Revit material/texture library dirs (`%CommonProgramFiles(x86)%\Autodesk Shared\
  Materials\Textures`, …) plus the project folder, plus any dirs in the `ADSK_MATERIAL_LIBRARY`
  or `PLANSCAPE_TEXTURE_DIRS` env vars (`;`-separated). Results cached per filename.
- **glTF graph**: `TEXCOORD_0` from `PolymeshTopology` UVs; GLB-embedded `images[]`
  (PNG/JPEG, **downscaled to ≤2048** via System.Drawing), `samplers[]` (REPEAT), `textures[]`;
  `pbrMetallicRoughness.baseColorTexture` (+ optional `normalTexture`); real-world
  scale/offset/rotation via **KHR_texture_transform** (scale = 1/realWorldScale). **Deduped**
  by resolved path (images) + appearance hash (materials).
- **Viewer**: glTF baseColorTexture is sRGB; the viewer's **Realistic** mode enables
  `ColorManagement` so it decodes correctly (default mode keeps the legacy linear look).
  Pair texture export with Realistic mode for lighting.

## Reading the diagnostics (this is how you verify, since it can't run in CI)

Every export with the flag on writes **one `[tex]` line per material** + a SUMMARY to
`StingTools.log`:

```
[tex] 'Brick, Common': appearanceAsset=yes diffuseBitmapProp=found rawPath='1\Mats\Masonry\Brick\…\brick.png' resolved='C:\Program Files (x86)\Common Files\Autodesk Shared\Materials\Textures\…\brick.png' embedded=yes reason=ok
[tex] 'Paint - White':  appearanceAsset=yes diffuseBitmapProp=none  rawPath='' resolved='MISSING' embedded=no reason=no-bitmap
[tex] 'Glass':          appearanceAsset=yes diffuseBitmapProp=found rawPath='…\glass.png' resolved='MISSING' embedded=no reason=path-missing searchedDirs='C:\…;C:\…'
[tex] SUMMARY materials=42 withBitmap=18 embedded=16 skipped-noBitmap=24 skipped-pathMissing=2 uvless-textured=0 images=16
```

- `appearanceAsset=no` → the material has no rendering asset (nothing to resolve).
- `diffuseBitmapProp=none` / `reason=no-bitmap` → colour-only material (paint) — **correct** to
  stay flat.
- `reason=path-missing` + `searchedDirs=…` → the bitmap exists in the appearance but its file
  wasn't found → install/point at the material library (set `PLANSCAPE_TEXTURE_DIRS`).
- `uvless-textured=N` → N meshes had a textured material but no UVs (texture can't map; skipped).

## Verify on the Revit machine

1. `setx PLANSCAPE_EXPORT_TEXTURES 1` → restart Revit.
2. Re-publish a **textured** model (brick / tile / wood).
3. Read the `[tex]` lines + SUMMARY in `StingTools.log`.
4. Load the model in the web viewer (ideally with **Realistic** on): brick/tile/wood surfaces
   carry their image at the correct scale; paint / colour-only materials stay flat (correct);
   the GLB stays a reasonable size (downscale + dedupe).

## Caveats

- Runtime-unverified from CI — the code compiles clean against the Revit 2025 API but the
  appearance-asset schema is version-sensitive; verify on 2025/2026/2027.
- Texture files resolve only where the material library is installed (or via the env-var dirs).
- UVs exist only for textured materials; untextured/un-UV'd meshes are skipped (logged).
- Cost / area / volume (E4) emission is unaffected by the texture toggle.

## MEP system capture (SYS) — same single Publish pass
Each element's MEP system is resolved at export (no reliance on the often-empty
`ASS_SYSTEM_TYPE_TXT` token): `MEPCurve.MEPSystem` for pipes/ducts/conduit/tray; fittings/
fixtures/accessories walk `MEPModel.ConnectorManager.Connectors → Connector.MEPSystem`.
Element-level `RBS_SYSTEM_CLASSIFICATION_PARAM` (raw class, e.g. Domestic Cold Water / Sanitary
/ Vent / Supply Air) + `RBS_SYSTEM_NAME_PARAM` (instance name) are read first. The element-map
gains three fields per element: **`system`** (STING SYS code via `TagConfig.GetMepSystemAwareSysCode`),
**`sysClass`** (raw classification), **`sysName`** (instance name). Non-MEP elements → empty SYS.
A `[sys] SUMMARY` line (mirror of `[tex]`) logs coverage to StingTools.log:
`[sys] SUMMARY resolved=… unresolved=… | DCW=… DHW=… SAN=… VEN=…` + unresolved-by-category.
ZONE/LOC/SEQ are also emitted on tagged entries (P1). One Publish pass now carries: textures
(when PLANSCAPE_EXPORT_TEXTURES=1) + full 8-token tag + discipline + SYS/sysClass/sysName.
Verify on re-publish: a plumbing model's `[sys] SUMMARY` shows resolved>0; the viewer's
colour-by-System no longer says "No SYS values".

