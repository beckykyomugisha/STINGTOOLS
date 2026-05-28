# PBR Texture Providers

The PBR texture pipeline pulls high-quality PBR material packs from external
libraries and stamps them onto Revit materials through the Prism (Autodesk
Standard Surface) appearance schema. Four providers ship out of the box:

| Slot | Provider           | License            | Cost     | Integration       |
|------|--------------------|--------------------|----------|-------------------|
| 1    | Poly Haven         | CC0                | Free     | Inline browser    |
| 2    | ambientCG          | CC0                | Free     | Inline browser    |
| 3    | Architextures Pro  | royalty-free       | $55/yr   | URL launch        |
| 4    | User-supplied folder | varies           | varies   | Auto-detect drop  |

Corporate baseline lives in `StingTools/Data/STING_TEXTURE_PROVIDERS.json`.
Projects can layer additions/overrides via
`<project>/_BIM_COORD/texture_providers.json` — entries match by id (project
wins), suffix-rule lists merge (additions append). Use `Pbr_ReloadProviders`
after editing on disk.

## How a pack flows from cloud to Revit

```
                     ┌──────────────────────────────┐
   Inspector "Browse │ MaterialHubProviderBrowserDialog │
   library…" button  └────────────┬─────────────────┘
                                  │ ListAssetsAsync
                                  ▼
                  ┌──────────────────────────────┐
                  │ IPbrProviderClient.ListAssetsAsync │  (Poly Haven REST,
                  └────────────┬─────────────────┘   ambientCG JSON,
                               │                     user-folder enum)
                  Thumbnails ◀─┘
                               │
                User picks ─── │
                               ▼
                  ┌──────────────────────────────┐
                  │ DownloadPackAsync             │
                  │   • Poly Haven: per-map URLs  │
                  │   • ambientCG: zip + extract  │
                  │   • user-folder: in-place     │
                  └────────────┬─────────────────┘
                               ▼
                  ┌──────────────────────────────┐
                  │ TexturePackIngester.LoadOrIngest │  ← suffix detection
                  │   writes manifest.json         │
                  └────────────┬─────────────────┘
                               ▼ TexturePackManifest
                  ┌──────────────────────────────┐
                  │ GenericToPrismConverter        │  (optional, dialog-gated)
                  │   InPlace | DuplicateMaterial  │
                  └────────────┬─────────────────┘
                               ▼
                  ┌──────────────────────────────┐
                  │ PbrTextureApplier.Apply        │
                  │   AppearanceAssetEditScope     │
                  │   10 Prism slots               │
                  └──────────────────────────────┘
```

## Slot map

| Slot          | Prism property         | Generic fallback     |
|---------------|------------------------|----------------------|
| Base color    | `advanced_base_color`  | `generic_diffuse`    |
| Normal        | `advanced_normal`      | `generic_bump_map`   |
| Roughness     | `advanced_roughness`   | (lost — warn)        |
| Metalness     | `advanced_metalness`   | (lost — warn)        |
| AO            | (combined into base in render) | (lost — warn) |
| Bump          | `advanced_bump`        | `generic_bump_map` (collapsed) |
| Displacement  | `advanced_displacement` (raytrace only) | (lost — warn) |
| Opacity       | `advanced_cutout`      | `generic_cutout`     |
| Emission      | `advanced_emission`    | `generic_self_illum_color` |
| Anisotropy    | `advanced_anisotropy`  | (lost — warn)        |

## Caveats

1. **Realistic view approximates PBR.** Bump + displacement + AO + true
   metalness only show their full effect in **raytraced rendering**.
2. **Displacement is render-only.** Enabling it adds 30–120 s per render
   frame; default is OFF.
3. **Network reach.** Provider clients need outbound HTTPS to
   `api.polyhaven.com` / `cdn.polyhaven.com` / `ambientcg.com` /
   `acg-media.struffelproductions.com`. Behind a proxy, the user must
   configure `HTTPS_PROXY` before opening the browser.
4. **Generic → Prism conversion is opt-in.** The dialog warns about the
   trade-off (in-place mutates the asset, possibly affecting other
   materials that share it; duplicate creates a separate material).
5. **Bulk apply uses name match.** `Pbr_BulkApply` walks every pack
   folder under `_BIM_COORD/textures/` and applies to the material whose
   name matches the pack folder. Use the same folder name as the Revit
   material for a clean 1:1 mapping.
