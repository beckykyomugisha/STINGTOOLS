# Contract Alignment — Implementation Prompts

Self-contained task prompts for the findings in
[`CONTRACT_ALIGNMENT.md`](CONTRACT_ALIGNMENT.md). Each is independent and
hand-off-ready: copy one into a fresh session as the task.

**Shared context (applies to every prompt below):**

- Authority is the **server camelCase JSON** (ASP.NET Core default; no
  `JsonNamingPolicy` override). Align clients TO it. Do **not** change the
  `TaggedElement` entity, its DB columns, or any shipped EF migration.
- Server is verified by inspection only — there is no .NET/Revit build in
  this sandbox. Python and TS changes can be unit/type-checked.
- Branch per repo convention; commit with a clear message; note any
  "not build-verified" caveat in the commit body.

---

## Prompt 1 — Bonsai IFC ingest client (Drift 1, HARD BROKEN, highest priority)

> The Bonsai Python client at
> `stingtools-core/python/stingtools_core/planscape/client.py` sends the
> IFC ingest payload in **snake_case**, but the server
> (`Planscape.Server/src/Planscape.Core/DTOs/IfcIngestDtos.cs`, bound by
> `IfcController.cs:46`) deserializes **camelCase** and is not
> snake_case-aware. Result: only `host` and `elements` bind, every element
> fails the empty-`IfcGlobalId` guard (`IfcController.cs:90`), and the
> whole ingest is a silent no-op.
>
> Fix `ingest_ifc_data` (and the transport as needed) so the wire payload
> matches the C# DTO exactly:
>
> 1. Add an explicit `_element_to_wire(element: dict) -> dict` field map
>    translating the Pythonic snake_case keys to the exact `IfcElementDto`
>    member names (camelCase): `ifc_guid→ifcGlobalId`,
>    `host_element_id→hostElementId`, `host_display_label→hostDisplayLabel`,
>    `tag→fullTag`, `discipline→discipline`, `location→location`,
>    `zone→zone`, `level→level`, `system→system`, `function→function`,
>    `product→product`, `sequence→sequence`, `ifc_class→ifcClass`,
>    `category_name→categoryName`, `family_name→familyName`,
>    `type_name→typeName`, `status→status`, `rev→rev`,
>    `room_name→roomName`, `level_name→levelName`,
>    `is_complete→isComplete`, `is_fully_resolved→isFullyResolved`,
>    `is_stale→isStale`, `validation_errors→validationErrors`,
>    `last_modified_utc→lastModifiedUtc`. Pass through any key that is
>    already a valid camelCase DTO member.
> 2. `compliance_pct` has **no server field** — the server models
>    compliance as `isComplete`/`isFullyResolved`/`isStale` bools. Drop it,
>    or derive the bools from it if the caller only supplies the float.
>    Do not send `compliance_pct` on the wire.
> 3. Switch the request body to camelCase: `host`, `hostDocumentGuid`,
>    `pluginVersion`, `userName`, `elements`.
> 4. Fix the `GET /ifc/mappings` query param: the controller binds
>    `[FromQuery] string? ifcGuid` (`IfcController.cs:201`), so any caller
>    must use `?ifcGuid=`, not `?ifc_guid=`. Update the docstring and any
>    call site accordingly.
>
> Reference the already-shipped fix on `claude/upbeat-noether-tg4pn`
> (commit `ff2c46564`) — the `_element_to_wire` approach there is the
> intended pattern; port it, don't reinvent.
>
> **Acceptance:** a unit test that builds a representative element dict,
> runs it through `_element_to_wire`, and asserts every produced key is a
> valid `IfcElementDto` member (cross-check against `IfcIngestDtos.cs`);
> assert `compliance_pct` is absent and the bool triplet is present.
> `py_compile` clean. Existing smoke tests still pass.

---

## Prompt 2 — Mobile `TaggedElement` type alignment (Drift 2, latent)

> The Planscape mobile app fetches `TaggedElement[]` from
> `GET /api/tagsync/elements/search` (`Planscape/src/api/endpoints.ts:257`
> and `:273`). The server returns the **raw** `TaggedElement` entity
> (`TagSyncController.cs:283-318`, `return Ok(elements)`), serialized
> camelCase from `Planscape.Server/src/Planscape.Core/Entities/TaggedElement.cs`:
> `disc, loc, zone, lvl, sys, func, prod, seq, tag1, tag7, status, rev,
> categoryName, familyName, typeName, roomName, gridRef, level, syncedAt,
> isComplete, isFullyResolved, isStale`.
>
> The mobile interface `Planscape/src/types/api.ts:161` instead declares
> `assTag1, discipline, location, level, systemType, function,
> productCode, sequenceNumber, revision, tag7Summary, …`. None of the
> token fields match → every tag/token renders `undefined` on the phone.
> There is also a collision: the server emits **both** `lvl` (level code)
> and `level` (level name); the mobile `level` silently binds the name.
>
> Pick ONE approach and apply it consistently:
>
> - **(A) Rename the interface** to the server serialization
>   (`disc/loc/lvl/sys/func/prod/seq/tag1/tag7/rev`) and fix every consumer
>   of `TaggedElement` in the app. Expose the two level fields distinctly,
>   e.g. `lvl` (code) and `levelName`.
> - **(B) Add a `mapTaggedElement(raw): TaggedElement` adapter** at the
>   fetch boundary in `endpoints.ts` that maps server camelCase → the
>   existing verbose interface, keeping `level` as `levelName` and adding
>   `levelCode` from `lvl`. Leave the interface and all consumers unchanged.
>
> Prefer (B) if `TaggedElement` is referenced widely in the UI (smaller
> blast radius); prefer (A) if it's referenced in only a couple of places.
> Grep first and state which you chose and why.
>
> **Acceptance:** `tsc --noEmit` clean. The element-search screen shows
> populated discipline/system/tag fields (or, if no live server, a unit
> test of the mapper/interface against a captured server JSON sample). The
> `lvl` vs `level` ambiguity is resolved explicitly in code, not left to
> chance.

---

## Prompt 3 — Document the dual ingest paths (Drift 3, docs only)

> `Planscape.Server` has two element-ingest endpoints that write the same
> `TaggedElement` projection:
>
> - `POST /api/tagsync/sync` — used by Revit/BCC `PlanscapeServerClient`;
>   keyed on `RevitElementId`; abbreviated DTO (`TagSyncRequest`).
> - `POST /api/projects/{id}/ifc/data` — used by the Bonsai Python client;
>   keyed on `IfcGlobalId` (carried in `TaggedElement.UniqueId`, with
>   `RevitElementId = 0`); verbose DTO (`IfcIngestRequest`).
>
> This is intentional (Revit has a stable ElementId; non-Revit hosts only
> have the IFC GlobalId), but it is undocumented and is the root of the
> abbreviated/verbose vocabulary split. Add a short doc note — in the
> XML-doc summary of both controllers and/or
> `Planscape.Server/docs/` — stating that the two endpoints are siblings
> writing the same `TaggedElement` table, when to use which, and that
> they must stay field-compatible. **No behavioural change. No new
> endpoint. BCC/Revit needs no code change** — it is already aligned to
> TagSync.

---

## Prompt 4 (optional) — Port the full `ff2c46564` Bonsai integration set

> Cherry-pick / re-apply the full alignment set from
> `claude/upbeat-noether-tg4pn` (commit `ff2c46564`) onto this branch, not
> just the client fix from Prompt 1. That commit also added:
>
> - a **sync operator** (`ops/sync_planscape.py` + `sting/sync_planscape`)
>   — collects active-IFC `Pset_StingTags` elements, runs `SpatialChecker`
>   + `validate_tag`, attaches errors as JSON, calls
>   `client.ingest_ifc_data(host="blender")`, reports server counts;
> - `BonsaiBridge.host_element_id()` in `stingtools-bonsai/core/bonsai.py`
>   — resolves the Blender object name via `tool.Ifc.get_object` with
>   IFC-attribute fallbacks;
> - `prefs.py` — `AddonPreferences` (server URL / API token / project id)
>   keyed off `__package__`, registered first;
> - wiring into `ops/__init__.py` and a `COORD → Planscape` panel section
>   (button enabled only when configured).
>
> **Caution:** `prefs.py` on that branch originally had
> `from __future__ import annotations`, which PEP-563-stringifies the
> `bpy.props.StringProperty(...)` annotations and **silently breaks
> property registration** in Blender. That import was removed there;
> ensure it stays removed when porting. Verify property registration in a
> live Blender session (the original work could not — it was integration-
> tested against ifcopenshell 0.8.5 only, no live Blender).
>
> Do Prompt 1 first (or as part of this); this prompt supersedes it if you
> take the whole set.
