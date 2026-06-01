# Element ingest: two sibling endpoints, one projection

`Planscape.Server` has **two** element-ingest endpoints. They are intentional
siblings — both upsert the **same `TaggedElement` table** — but each speaks a
different host vocabulary and keys on a different identifier. This split is
deliberate (Revit has a stable `ElementId`; non-Revit hosts only have the IFC
`GlobalId`); this note exists so the split, and the vocabulary divergence it
causes, is no longer undocumented.

| | `POST /api/tagsync/sync` | `POST /api/projects/{id}/ifc/data` |
|---|---|---|
| Controller | `TagSyncController.SyncElements` | `IfcController.IngestData` |
| Caller | Revit plugin + BCC (`StingTools.BIMManager.PlanscapeServerClient.SyncElementsAsync`) | Bonsai Python client (`stingtools_core.planscape.PlanscapeClient.ingest_ifc_data`) — and any future non-Revit host |
| Request DTO | `TagSyncRequest` → `TagElementDto` (**abbreviated**) | `IfcIngestRequest` → `IfcElementDto` (**verbose**) |
| Upsert key | `(ProjectId, RevitElementId)` | `(ProjectId, UniqueId)` where `UniqueId` **carries the IfcGlobalId** and `RevitElementId = 0` |
| Also writes | — | `ExternalElementMapping` (cross-host identity rows) via `IIfcIngestService` |
| Project id | in the **body** (`TagSyncRequest.ProjectId`) | in the **route** |

> **`TagElementDto.Tag7` is a single string**, not `Tag7A..F`. Only `Tag1` and
> `Tag7` exist on the TagSync DTO.

## They are NOT wire-compatible — and don't need to be

The two DTOs use **different field names for the same eight tag segments**, so
you cannot post one body to the other endpoint:

| `TaggedElement` column | `TagElementDto` (tagsync) | `IfcElementDto` (ifc/data) |
|---|---|---|
| `Disc` | `Disc` | `Discipline` |
| `Loc`  | `Loc`  | `Location` |
| `Zone` | `Zone` | `Zone` |
| `Lvl`  | `Lvl`  | `Level` |
| `Sys`  | `Sys`  | `System` |
| `Func` | `Func` | `Function` |
| `Prod` | `Prod` | `Product` |
| `Seq`  | `Seq`  | `Sequence` |
| `Tag1` | `Tag1` | `FullTag` |

Each controller has its **own mapper** (`TagSyncController.MapDtoToEntity` /
the inline upsert in `IfcController` → `IIfcIngestService`) that translates its
vocabulary onto the shared `TaggedElement` columns. The abbreviated/verbose
vocabulary split traces entirely to this — it is two front doors, one table.

### Shared core (both mappers populate)

`Disc`, `Loc`, `Zone`, `Lvl`, `Sys`, `Func`, `Prod`, `Seq`, `Tag1`, `UniqueId`,
`CategoryName`, `FamilyName`, `Status`, `Rev`, `IsComplete`, `IsFullyResolved`.

### Endpoint-only columns

- **tagsync only:** `RevitElementId` (real value, the key), `Tag7`, `SyncedAt`,
  `SyncedBy`.
- **ifc/data only:** `RevitElementId = 0`, `TypeName`, `RoomName`, `Level`
  (from `LevelName`), `IsStale`, `ValidationErrors`, `LastModifiedUtc`; plus it
  populates `ExternalElementMapping`.

## The real invariant (what "must stay compatible" actually means)

Not field names — those diverge by design. Two things must hold:

1. **Both mappers keep targeting the same `TaggedElement` columns for the
   shared core.** If you add a new tag-bearing column to `TaggedElement`, add
   it to *both* mappers (or document why one host can't supply it) so a row's
   meaning doesn't depend on which door it came through.
2. **The key-space contract is preserved.** `TaggedElement` carries two
   *filtered* unique indexes so the hosts never collide
   (`PlanscapeDbContext` / migration `IfcIngestSubstrate`):
   - `(ProjectId, RevitElementId)` unique **where `RevitElementId > 0`** — Revit/tagsync,
   - `(ProjectId, UniqueId)` unique **where `UniqueId <> ''`** — non-Revit/ifc-data.
   Revit rows set `RevitElementId > 0`; non-Revit rows set `RevitElementId = 0`
   and a non-empty `UniqueId`. Changing either endpoint's keying would break
   this coexistence.

## When to use which

- **Revit / BCC:** `POST /api/tagsync/sync`. It is already aligned to this path
  and needs no change. It does call `GET /api/projects/{id}/ifc/mappings`
  (read-only cross-host lookup) but **never** `POST /ifc/data`.
- **Bonsai / ArchiCAD / Tekla / headless:** `POST /api/projects/{id}/ifc/data`.
  Use it whenever the host has no stable Revit `ElementId` and the IFC
  `GlobalId` is the canonical key — it also seeds the cross-host
  `ExternalElementMapping` table.

No behavioural change accompanies this note; it is documentation only.
