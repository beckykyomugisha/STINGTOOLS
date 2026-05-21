# Project-Overlay Examples

Three of the corporate-baseline enums in `shared/ifc/enums/` are
**project-scoped templates** — the values shipped at corporate level are
placeholders that every project replaces with its actual codes:

- `StingLocationCodes` — buildings on the site
- `StingZoneCodes` — fire / acoustic / clinical / HVAC zones
- `StingLevelCodes` — storeys / floor levels

This folder shows what a populated project overlay looks like.

## Where project overlays live

For project `<project.rvt>`, the overlay files are stored at:

```
<project_directory>/_BIM_COORD/enums/
    StingLocationCodes.xml
    StingLevelCodes.xml
    StingZoneCodes.xml
```

The `_BIM_COORD/` directory is the same one used by Phase 184c
drawing-types overlay, Phase 183 LiveProfileSync snapshot, and the
template-engine `manifest.json`. The files share the **same XML schema**
as the corporate baseline (`_schema.xsd`), but with `<Scope>` set to
`project_template` and `<Origin>` flipped to `project`.

## Resolver behaviour

When `stingtools-core` loads an enum for a given project:

1. **Read** the corporate baseline from `shared/ifc/enums/<Name>.xml`.
2. **If** the corporate file has `<Scope>project_template</Scope>` AND
   the project's overlay exists at `<project>/_BIM_COORD/enums/<Name>.xml`,
   **replace** the corporate `<Values>` block with the project's.
3. **Reserved codes** (`XX`, `*`, `SITE`, `EXT`, `GF`, `MZ`, `RF`, `PR`,
   `ZZ`) are NOT replaceable — projects cannot redefine sentinels.
4. **Validate** that the project's overlay values do not collide with
   reserved codes. Mismatch raises a fatal load error.
5. **Compute** a SHA-256 over the merged values list — this becomes the
   project's effective lock. Stored in
   `<project>/_BIM_COORD/.sting_overlay_locks.json` so that downstream
   drift detection works.

Corporate-locked enums (`<Scope>corporate</Scope>`) never accept overlay
values — projects cannot extend `StingDisciplineCodes` or
`StingSystemCodes` via overlay. To add to a corporate-locked enum,
file a schema-release request.

## Files in this folder

| File | Purpose |
|---|---|
| `StingLocationCodes.example.xml` | Sample project's building list (NHS-trust-style 5-building campus) |
| `StingLevelCodes.example.xml` | Sample project's storey list (5-storey acute hospital) |
| `StingZoneCodes.example.xml` | Sample project's clinical-zone list (8 ward + theatre zones) |

These are **examples, not templates** — they show real-world structure
but should not be copied verbatim into a project. Each project starts
from the corporate baseline and fills in its own values.

## Validation

Run from the repo root:

```
python3 tools/enums/validate_overlay.py path/to/project/_BIM_COORD/enums/
```

(Tool to be added next to `compute_checksums.py` — checks reserved-code
collisions, schema conformance, and overlay-merge resolvability.)
