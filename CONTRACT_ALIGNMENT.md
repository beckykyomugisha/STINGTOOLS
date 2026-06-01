# Contract Alignment Audit — server / mobile / bonsai / BCC

**Scope:** the tag/element data contract shared by the four surfaces that
exchange STING 8-segment tags + IFC-GlobalId-keyed element records:
Planscape Server (authority), Planscape mobile (Expo/TS), the Bonsai
add-on (Python), and the Revit BCC (`PlanscapeServerClient`).

**Status:** review only — no code changed. Authored on
`claude/magical-mayer-hLnIk`.

**TL;DR:** one logical record (the 8-segment tag + element identity) is
spelled **four different ways** across the surfaces. The server-internal
bridge is consistent, but both *client edges* (Bonsai→server,
server→mobile) drop fields silently. One of these (Bonsai) was fixed on
`claude/upbeat-noether-tg4pn` (commit `ff2c46564`) but that fix is **not
in this branch**.

---

## The four vocabularies

| Surface | source file | discipline | system | full tag | guid / host id | notes |
|---|---|---|---|---|---|---|
| IFC substrate | `shared/ifc/psets/Pset_StingTags.xml` | `Discipline` | `System` | `FullTag` | — | verbose — **canonical** |
| Server IFC DTO | `Planscape.Server/src/Planscape.Core/DTOs/IfcIngestDtos.cs` | `Discipline` | `System` | `FullTag` | `IfcGlobalId`, `HostElementId` | mirrors substrate ✅ |
| Server entity / TagSync | `Planscape.Server/src/Planscape.Core/Entities/TaggedElement.cs` | `Disc` | `Sys` | `Tag1` | `UniqueId`, `RevitElementId` | **abbreviated** (DB columns) |
| Mobile TS | `Planscape/src/types/api.ts:161` | `discipline` | `systemType` | `assTag1` | `uniqueId` | **3rd form** — `productCode`, `sequenceNumber`, `tag7Summary`, `revision` |
| Bonsai Python | `stingtools-core/python/stingtools_core/planscape/client.py:86` | `discipline` | `system` | `tag` | `ifc_guid`, `host_element_id` | **snake_case** + `compliance_pct` |

The substrate, the IFC DTO, and the server's internal verbose→abbreviated
bridge (`IfcController.cs:149` `t.Disc = el.Discipline`) are mutually
consistent. The two **client edges** are not.

Server JSON casing: ASP.NET Core default **camelCase** — no
`JsonNamingPolicy` override found in `Program.cs`. Binding is
case-insensitive but **not snake_case-aware**. This makes server
camelCase the de-facto authority (it is the wire format and is already
baked into a shipped DB schema + migration `20260519000000_IfcIngestSubstrate`).

---

## Drift 1 — Bonsai → `POST /api/projects/{id}/ifc/data` (HARD BROKEN)

**File:** `stingtools-core/python/stingtools_core/planscape/client.py:103-108`

The client sends snake_case keys:

```python
body = { "host": ..., "host_document_guid": ..., "elements": [...] }
# each element: ifc_guid, host_element_id, tag, compliance_pct, discipline, ...
```

The server binds `IfcIngestRequest` / `IfcElementDto` as camelCase
(`hostDocumentGuid`, `ifcGlobalId`, `hostElementId`, `fullTag`, …). Result:

- `host` and `elements` bind; **everything else arrives null**.
- Every element fails the `string.IsNullOrWhiteSpace(el.IfcGlobalId)`
  guard (`IfcController.cs:90`) → skipped with warning
  `"skipped element with empty IfcGlobalId"`. **The whole ingest is a no-op.**
- `compliance_pct` (float) has **no server field** — server models
  compliance as three bools (`isComplete` / `isFullyResolved` / `isStale`).
  It must be dropped or translated, not sent.

**Also:** `GET /api/projects/{id}/ifc/mappings?ifc_guid=...`
(`IfcController.cs:201`) binds `[FromQuery] string? ifcGuid`. The
`?ifc_guid=` query in the docstring/comment **will not bind** → the GUID
filter is silently ignored and the endpoint returns the full first page.

**Fix direction:** add an `_element_to_wire` snake→exact-DTO-camel map in
`client.py` (camelCase pass-through), switch the request body to
camelCase, drop/translate `compliance_pct`, and change the query param to
`ifcGuid`. *This is the fix already implemented on `ff2c46564`
(`claude/upbeat-noether-tg4pn`) — it just needs to be ported here.*

---

## Drift 2 — Server → Mobile (`GET /api/tagsync/elements/search`)

**Server:** `TagSyncController.cs:283-318` returns `Ok(elements)` — the
**raw** `TaggedElement` entity list, serialized camelCase:
`disc, loc, zone, lvl, sys, func, prod, seq, tag1, tag7, rev, level, …`

**Mobile:** `Planscape/src/types/api.ts:161` types the response as:

```ts
export interface TaggedElement {
  assTag1, discipline, location, zone, level, systemType,
  function, productCode, sequenceNumber, status, revision,
  categoryName, familyName, typeName, roomName, gridRef, tag7Summary, syncedAt
}
```

Field-by-field mismatch:

| Mobile expects | Server emits | match |
|---|---|---|
| `assTag1` | `tag1` | ✗ |
| `discipline` | `disc` | ✗ |
| `location` | `loc` | ✗ |
| `systemType` | `sys` | ✗ |
| `function` | `func` | ✗ |
| `productCode` | `prod` | ✗ |
| `sequenceNumber` | `seq` | ✗ |
| `revision` | `rev` | ✗ |
| `tag7Summary` | `tag7` | ✗ |
| `level` | `lvl` **and** `level` | ✗ collision |
| `zone`, `status`, `categoryName`, `familyName`, `typeName`, `roomName`, `gridRef`, `syncedAt` | same | ✅ |

Every tag-token field lands `undefined` on the phone → element search
shows blank discipline / system / tag. The `level` case is worse than
blank: server has **two** fields, `lvl` (level *code*) and `level`
(level *name*); mobile's `level` silently binds to the name.

Confirmed no server-side projection emits the mobile names — grep for
`assTag1` / `systemType` / `productCode` / `tag7Summary` in
`Planscape.Server/src` hits only `Planscape.MIM` Asset entities, never
`TaggedElement`.

**Fix direction:** either rename the mobile `TaggedElement` interface to
the server serialization (`disc/loc/lvl/sys/func/prod/seq/tag1/tag7/rev`),
or add one `mapTaggedElement()` adapter at the fetch boundary in
`endpoints.ts` (the search call is at `endpoints.ts:257` and `:273`).
Resolve `lvl` vs `level` explicitly (expose both as e.g. `levelCode` +
`levelName`).

---

## Drift 3 — Two ingest paths, divergent keys (design seam, not a bug)

| Path | caller | DTO | key | vocabulary |
|---|---|---|---|---|
| `POST /api/tagsync/sync` | Revit / BCC `PlanscapeServerClient` | `TagSyncRequest` | `RevitElementId` | abbreviated |
| `POST /api/projects/{id}/ifc/data` | Bonsai Python client | `IfcIngestRequest` | `IfcGlobalId` | verbose |

Both upsert the **same** `TaggedElement` table. This is intentional
(Revit has a stable `ElementId`; non-Revit hosts only have the IFC
GlobalId, carried in `TaggedElement.UniqueId` with `RevitElementId = 0` —
`IfcController.cs:165-166`). It is not wrong, but it is the *reason* the
abbreviated/verbose split exists and leaks into Drift 2.

**BCC/Revit needs no change** — it is already aligned to TagSync. Worth a
one-line doc note that `/tagsync/sync` and `/ifc/data` are siblings
writing the same projection.

---

## Mobile gap (not a drift) — no IFC-mapping consumer

The mobile app references neither `/ifc/data` nor `/ifc/mappings`
(grep for `ifc/`, `ifcGlobalId`, `ExternalElementMapping` in
`Planscape/src` returns nothing). Cross-host element resolution
(issue raised in Bonsai → highlight in Revit) exists server-side
(`IfcController.Resolve`, `IIdentityResolverService`) but has no mobile
client. Future work, not part of this alignment.

---

## Server cross-host identity — review notes

A separate session (screenshot summary, not on this branch) extracted an
`IfcIngestService`, made TagSync carry `Host`+`HostDocumentGuid` with a
fire-and-forget mapping upsert, added optional `IfcGlobalId` to the
ArchiCAD event + nullable `RoomIfcGlobalId`/`ElementIfcGlobalId` columns
on healthcare/penetration entities, a `healthcare/by-ifc/{ifcGlobalId}`
GET, and a hand-authored migration `20260601000000_CrossHostIdentityFields`.
The work is sound; four notes (verified against this branch where possible):

**1. BLOCKER — `Planscape.API` does not compile, and it is *pre-existing*,
unrelated to the cross-host work (CONFIRMED on this branch's HEAD).** Real
CS0101 duplicate-type collisions in namespace `Planscape.API.Controllers`:
- `AddMemberRequest` — `DistributionGroupsController.cs:229` **and**
  `ProjectMembersController.cs:552` (both top-level `public record`).
- `ReorderRequest` — `IssueCustomFieldsController.cs:216`
  (`ReorderItem[] Items`) **and** `PhotoAlbumsController.cs:465`
  (`Guid[] Order`).
- Plus the `Photo*` DbSet references (`SitePhotosExtController`,
  `PhotoAclGate`, `SeedData`).

A non-building API means the server can't deploy → the **entire** IFC
ingest path is dead in prod, however clean the new controllers are. Three
sessions have now stacked work on top of a non-compiling project. **Fix
this first, as its own tiny PR** (Prompt 5), before any feature work
merges on top. "Touched controllers emit zero diagnostics" is true but a
full build can't confirm it until the collisions are gone.

**2. This work closes the Drift 3 gap — good direction.** TagSync now
carries `Host`+`HostDocumentGuid` and upserts `ExternalElementMapping`, so
the Revit/BCC path finally populates the cross-host mapping table (was
`/ifc/data`-only). Two cautions:
- There are now **two** ways to tie an entity to an IFC GlobalId — the
  `ExternalElementMapping` index **and** the new `RoomIfcGlobalId` /
  `ElementIfcGlobalId` columns on domain entities. Both are legitimate
  (cross-host index vs. direct domain FK) but document which is
  authoritative and ensure they can't silently diverge.
- `Host` defaults to `"revit"`. Confirm Blender/ArchiCAD callers actually
  *set* it — a forgotten field tags every cross-host element `revit` and
  poisons the mapping the feature exists for.

**3. Fire-and-forget is the weak point.** "Never fails the sync, own DB
scope, best-effort" is good UX for the *sync* — but the mapping **is the
deliverable**. Best-effort + lossy means a process restart or transient DB
error silently drops an identity row with nothing to reconcile it. At
minimum log failures to `AuditLog`; better, add a backfill/reconciliation
pass, or fold the upsert into the same transaction if the latency is
tolerable. Don't let the one write the feature is named after be the one
allowed to vanish.

**4. Migration hygiene.** Hand-authored migration matches the repo
convention (good). Ensure the model-snapshot edit is exact — a drifted
snapshot makes the next `ef migrations add` emit a confusing phantom diff.

This work does **not** touch Drift 1 (Bonsai snake_case) or Drift 2
(mobile field names) — don't assume the cross-host commit fixed them.

---

## Recommended order of operations

0. **Prompt 5 — fix the pre-existing `Planscape.API` build breakage.**
   Highest leverage, lowest risk; unblocks every other server change.
1. **Drift 1 (Bonsai)** — highest impact, smallest change, already
   solved on `ff2c46564`. Port the `_element_to_wire` map + query-param
   fix into this branch's `client.py`.
2. **Drift 2 (Mobile)** — one interface rename or one adapter function;
   resolve the `lvl`/`level` collision while there.
3. **Drift 3** — documentation only (largely subsumed by the cross-host
   work, which made TagSync populate the mapping table).
4. **Cross-host hardening** (Prompt 6) — `AuditLog` + reconciliation for
   the fire-and-forget mapping upsert; document mapping-table vs.
   `*IfcGlobalId`-column authority.

Authority is **server camelCase JSON**; align both client edges to it.
Do **not** touch the `TaggedElement` entity / DB columns or the shipped
migration.

### Verification touchpoints (server-side, authoritative)

- `Planscape.Server/src/Planscape.Core/DTOs/IfcIngestDtos.cs` — IFC wire shape
- `Planscape.Server/src/Planscape.Core/Entities/TaggedElement.cs` — projection columns
- `Planscape.Server/src/Planscape.API/Controllers/IfcController.cs:46,201` — bind sites
- `Planscape.Server/src/Planscape.API/Controllers/TagSyncController.cs:283` — mobile search return
- `shared/ifc/psets/Pset_StingTags.xml` — canonical property names
