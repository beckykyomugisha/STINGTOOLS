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

**Drift 2 is not isolated to `TaggedElement`.** A sweep of the other
mobile types found the same camelCase-name disease on two more
high-traffic types (the rest — `BimIssue`, `IssueAttachment`, `Project`,
`UserProfile`, `LoginResponse`, `NotificationPreferences` — are clean):

| TS type | TS field (`types/api.ts`) | server emits | evidence |
|---|---|---|---|
| `ComplianceSnapshot` | `compliancePercent` | `tagPercent` | `api.ts:42` vs `ComplianceSnapshot.cs:26` — **dashboard compliance % is `undefined` on mobile** |
| `Transmittal` | `transmittalNumber` | `transmittalCode` | `api.ts:241` vs `Transmittal.cs:11` |
| `Transmittal` | `issuedBy` | `createdBy` | `api.ts:243` vs `Transmittal.cs:21` |
| `Transmittal` | `issuedTo` | `recipient` | `api.ts:244` vs `Transmittal.cs:12` — **transmittal list shows blank sender/recipient** |

Minor / defensive (not breaking): `DocumentRecord.updatedAt` is nullable
server-side (handle `null`); `Meeting.type` is an optional legacy fallback
the server never emits (`meetingType` only) — drop the fallback or alias
it. Fix these the same way as `TaggedElement` (rename or adapter); the
ComplianceSnapshot + Transmittal ones are user-visible bugs.

**Drift 2 — execution + corrections (session 6, on `upbeat-noether-tg4pn`).**
Prompt 2 was executed and confirmed Drift 2 **live, not latent**: the
scanner / element-search screen rendered blank DISC/SYS/PROD/SEQ/tag and an
empty TAG7 because every renamed token deserialised to `undefined`. Three
corrections to this audit:
- **Consumers live in `Planscape/app/` (Expo Router screens), not
  `Planscape/src/`.** `Planscape/src` is the data/types layer; the actual
  `TaggedElement` consumers are `app/(tabs)/scanner.tsx` and
  `app/ifc/index.tsx` (2 files). The rename (approach A) surfaced them via
  `tsc`, so no runtime adapter was needed. Earlier "grep `Planscape/src` →
  zero consumers" was a false negative.
- The `lvl`/`level` collision was concrete: the scanner's "LVL" cell read
  `element.level` (level *name*) instead of the level *code* — fixed to
  `element.lvl`.
- A **compile-time conformance test** was added
  (`src/api/__typetests__/taggedElement.typetest.ts`) — captured server
  JSON checked assignable, `@ts-expect-error` guards block the verbose
  aliases from returning. This is the right micro-pattern; Prompt 10
  generalises it.

**Mobile baseline blocker (sibling to Prompt 5):** `Planscape/app/` carries
**111 pre-existing `tsc` errors** (WIP tree, unrelated to these types). Like
the non-building `Planscape.API`, the mobile app does not typecheck clean,
so "`tsc --noEmit` passes" can never be a CI gate — and Prompt 10's drift
gate can't assert it — until that baseline is fixed. The session-6 fix
proved its change is **byte-identical** to the 111-error baseline (zero new
errors), which is the correct discipline, but the baseline itself needs its
own clean-up task.

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
writing the same projection. (Done — session 7 added
`Planscape.Server/docs/element-ingest-paths.md` + cross-referencing
XML-docs.)

**The real coexistence invariant** (verified `PlanscapeDbContext.cs:605-609`):
the two paths are **not** wire-compatible and never were — what holds them
together is (1) both mappers writing the **shared `TaggedElement` columns**,
and (2) a **filtered-unique key-space**:
`(ProjectId, RevitElementId) WHERE RevitElementId > 0` for Revit, and
`(ProjectId, UniqueId)` for non-Revit. The `:609` comment is also the
**server-side confirmation of Drift 4**: *"for Revit `UniqueId` is the
Revit `Element.UniqueId`; for non-Revit hosts `UniqueId` carries the IFC
GlobalId."* The same column means two different things by host — which is
exactly why a cross-host join on it fails. (Note: Prompt 7 re-keys the
`ExternalElementMapping.IfcGlobalId`, a *different* key from this
`TaggedElement` index, so it does not disturb this invariant.)

---

## Drift 4 — Cross-host identity key: Revit UniqueId ≠ true IFC GlobalId (CRITICAL)

This is the most consequential finding across the whole audit: the marquee
cross-host feature — *"select/raise in one host, resolve on the others"* —
is a **guaranteed no-op between Revit and Bonsai/ArchiCAD**, even though
every layer builds clean and is internally consistent. The defect lives
entirely in the *contract between* layers.

- `ExternalElementMapping` and `GET /ifc/mappings?ifcGuid=` are keyed on
  **IfcGlobalId**.
- The Revit sync path stuffs `Element.UniqueId` (Revit's **45-char**
  UniqueId) into that column. Confirmed in
  `StingTools/BIMManager/PlanscapeServerClient.cs:1040`:
  *"keys on IfcGlobalId (Revit UniqueId in our case)"*.
- Bonsai / ArchiCAD send the **true 22-char IFC GlobalId**. The IFC GUID is
  *derived* from the Revit UniqueId but is **not equal** to it — different
  namespaces for the same physical element.
- The BCC cross-host resolution (consumer work, screenshot summary) looks
  up `Element.UniqueId` via `/ifc/mappings?ifcGuid=…` → matches only
  Revit-origin rows (federated docs); **never matches a Bonsai/ArchiCAD
  row**. The headline use case ("issue raised in Blender → highlight in
  Revit") cannot fire.

**The plugin already computes the correct key.**
`StingTools/Commands/Interop/StabilizeIfcGuidsCommand.cs` persists Revit's
`IfcGloballyUniqueId` into the shared param `IFC_GLOBAL_ID_TXT` (feeding
`ElementGlobalIdRegistry` / `IfcAlignmentValidator`,
`GLOBALID_DRIFT`). The **true IFC GlobalId is the only key every host can
produce** — Bonsai/ArchiCAD have no access to Revit's UniqueId, so the
common key *must* be the IFC GlobalId.

**Fix direction (Prompt 7):** Revit must key cross-host identity on
`IFC_GLOBAL_ID_TXT`, not `Element.UniqueId`, for both the mapping upsert
and the BCC `/ifc/mappings` lookup. Fall back gracefully — and prompt the
user to run *Stabilize IFC GUIDs* — when the param is empty (Revit
re-generates IfcGUIDs across sessions until stabilised; that's exactly why
`StabilizeIfcGuidsCommand` exists). Note this interacts with the
TagSync `UniqueId`-as-key choice (Drift 3 / cross-host server work): the
mapping column and the lookup must agree on the IFC GlobalId, not the
Revit UniqueId.

**DONE (Prompt 7, session 14, commit `8486cf056`) — the keystone fix.**
`IfcGlobalId` added to `TagElementPayload` (plugin) + `TagElementDto`
(server); the plugin populates it from `IFC_GLOBAL_ID_TXT`;
`TagSyncController` now keys `ExternalElementMapping` on `dto.IfcGlobalId`,
not `UniqueId` (which keeps its correct role as the host-side id). The BCC
read path (`ResolveCrossHostSummary`) reads `IFC_GLOBAL_ID_TXT` for the
`/ifc/mappings` lookup; un-stabilised elements are skipped with a "run
Stabilize IFC GUIDs first" hint instead of a wrong-key query; the BCC
tooltip names Stabilize as the prerequisite. No EF migration
(`ExternalElementMapping.IfcGlobalId` already existed; `IfcGlobalId` is a
wire field). Builds clean. **Correctness is "equal by construction"** —
both hosts write the IFC file's GlobalId (Revit via
`IFC_GLOBAL_ID_TXT = IfcGloballyUniqueId = export GlobalId`, Bonsai via
`el.GlobalId`) — **with one operational dependency that is currently NOT
server-monitored** (corrected, session 15): `IFC_GLOBAL_ID_TXT` is a
*snapshot* at stabilize time and equals the exported file's GlobalId only
while the model stays stable through export. **R1 refined (code-verified):**
the pin is the **`IfcGUID` instance parameter**, not an `IFCExportOptions`
setting — Revit populates it on an element's first IFC export and honours
it as `IfcRoot.GlobalId` thereafter, and `StabilizeIfcGuidsCommand` reads
`IFC_GLOBAL_ID_TXT` **from that same param** (`ReadRevitIfcGuid` lines
163-185; it skips elements whose `IfcGUID` is empty = never exported). So
the cross-host key and the exported GlobalId share **one source** and are
equal by construction **once an element has been exported at least once**;
the residual risk narrows to the `IfcGUID` param being regenerated between
the stabilize snapshot and the ingested export (hence the export → stabilize
→ export-again ordering, R2). The server `GLOBALID_DRIFT` detector is still
a **deferred no-op**
(`IfcAlignmentValidator.cs:306` — Gap 9 needs a
`FederatedElement.ProjectModelId` column that doesn't exist). The earlier
"monitored" claim was wrong. The **only active guards** are now Prompt 12's
client-side precheck (counts Missing + Stale before push/export) and
Prompt 11's live round-trip (the only thing that actually confirms
snapshot == export). **Recommended follow-up** (flagged, not done):
add an explicit `TaggedElement.IfcGlobalId` column (migration + backfill +
dual-write both ingest paths + index) so the `UniqueId` overload is
retired; not required for cross-host resolution, which routes through
`ExternalElementMapping`.

---

## Drift 5 — StingBridge (ArchiCAD / IFC watcher) bypasses the cross-host contract

There is a **second, divergent Python client** at
`StingBridge/planscape/client.py` — separate from
`stingtools-core/python/stingtools_core/planscape/client.py` (Drift 1).
The ArchiCAD/IFC-watcher bridge:

- Posts to the **legacy** `/api/tagsync/sync` (line 90), not `/ifc/data`.
- **Fabricates a fake `revitElementId`** from an MD5 of the IFC GUID:
  `revit_id = int(md5(guid)[:15], 16) & 0x7FFF…` (`client.py:181`), then
  sends it as `"revitElementId"` (`:184`). ArchiCAD elements therefore
  masquerade as Revit elements with synthetic ids.
- Never sets `Host`, never sends `HostElementId`, **never populates
  `ExternalElementMapping`.** ArchiCAD data lands in `TaggedElement` with a
  bogus Revit id and is unreachable by cross-host resolution.

This is Drift 4's disease in a worse form (a *fabricated* Revit key rather
than a real-but-wrong one). Two problems to fix: (a) the bridge should
ingest via `/ifc/data` with `Host="archicad"`, the true IFC GlobalId, and
the ArchiCAD element GUID as `HostElementId`; (b) **there should not be two
Python clients** — `StingBridge` and `stingtools-core` should share one
`PlanscapeClient`, or one should be retired.

**DONE (Prompt 9, session 11) — with a correction to this audit.** There are
**two** StingBridge sync paths, not one: `watch/ifc_watcher.py` (IFC export
→ `el.GlobalId` is the only id, so `ifcGlobalId = hostElementId = GlobalId`)
and `sync/engine.py` (live ArchiCAD JSON API → holds the ArchiCAD element
GUID as `hostElementId` but no IFC GlobalId, so it **derives** the GlobalId
via `ifcopenshell.guid.compress(GUID)`, raw-GUID fallback). Both now POST
`/ifc/data` with `Host="archicad"`, no fabricated Revit id. `_element_to_wire`
is single-sourced from `stingtools-core` (import + `sys.path` fallback), with
a test asserting it's the *same* function, not a clone; legacy
`/tagsync/sync` kept as a deprecated shim (safe transition). Deferred (with
rationale): the full client merge — the two clients use different HTTP stacks
and the bridge has 3 methods core lacks; only the *wire contract* mattered
and it's unified. **Open assumption:** the engine's `compress(GUID)` must
equal what ArchiCAD's IFC export writes — confirm against a live ArchiCAD
IFC round-trip before relying on engine-path cross-host matching.

---

## Drift 6 — Revit client ↔ server endpoint mismatches (404 / 405 class)

Inventory of `PlanscapeServerClient.*.cs` calls vs server routes found four
paths that will fail at runtime (most of the ~60 calls are clean):

| Client method @ line | Calls | Server has | Failure |
|---|---|---|---|
| `SendTransmittalAsync` @ `:904` | `POST .../transmittals/{id}/send` | `[HttpPut("{txId}/send")]` `TransmittalsController.cs:358` | **405** verb mismatch |
| `PushWarningsAsync` @ `:955` | `POST .../warnings` | only `[HttpPost("report")]` `WarningsController.cs:33` | **404** wrong path |
| `PushBoqSnapshotAsync` @ `:1001` | `POST .../boq/snapshot` | no `snapshot` route in `BoqController` | **404** no route |
| `FullSyncAsync` @ `:382` | `POST /api/tagsync/fullsync` | no route | **404** (but `[Obsolete]`) |
| `listIfcElements()` (mobile) @ `endpoints.ts:270` | `GET .../tagged-elements` | no route in any controller | **404** latent (feature unwired) |

Field-name drift on the Revit client is otherwise **clean** — bodies
serialize camelCase via `[JsonProperty]`, matching the server DTOs. These
are pure path/verb fixes (rename the path / change POST→PUT / add the
server route), not contract redesigns. The `FullSyncAsync` one is already
`[Obsolete]` — confirm it has no live caller, then delete it rather than
re-add the route.

**DONE (Prompt 8, session 12).** All four resolved + the inventory
re-verified independently (~15 other literals confirmed clean): transmittal
`send` switched to PUT (new `PutJsonAsync` helper); warnings repointed to
`/warnings/report` (no caller, body shape documented); `boq/snapshot` —
investigation showed the route was **intended but never added** (`BoqSnapshot`
entity + DbSet exist; `IfcBoqSeedJob.cs:166` cites `BoqController.PushSnapshot`),
so the server `[HttpPost("snapshot")]` route was added (persists + broadcasts,
no migration — entity pre-existed); `FullSyncAsync` confirmed obsolete +
zero callers → deleted. Both plugin and server build clean.

---

## BCC cross-host consumer — review notes

A fourth session (commit `cedcf3aba`, on `claude/upbeat-noether-tg4pn` —
**not on this branch**) built the Revit/BCC consumer of the cross-host
server work: a `PlanscapeServerClient.CrossHost.cs` partial
(`GetHealthcareDashboardAsync` / `GetPenetrationsDashboardAsync` /
`GetIfcMappingsAsync`), a Healthcare "LIVE DASHBOARD" panel, a new
always-on Penetrations tab, and cross-host resolution appended to the
selection dialog. Verified what's checkable on this branch:

**Sound:**
- Dashboard endpoints + shapes are real: `HealthcareController.cs:25`
  (`pressure.totalLast7d`, `mgas.rag`, `antiLigature`) and
  `PenetrationsController.cs:120` (`byStatus`, `byHost`/`HostType`) —
  the `JObject` rendering targets actual camelCase keys.
- Uses `?ifcGuid=` (not `?ifc_guid=`) — correct, matches the authority
  flagged in Drift 1.
- `dotnet build StingTools.csproj — 0 errors` is legitimate and is **not**
  contradicted by the `Planscape.API` breakage: the Revit plugin and the
  API are separate build targets.

**Concerns:**
- **Untestable end-to-end until the API builds.** Every endpoint this
  panel calls lives in the non-building `Planscape.API` (see cross-host
  review note 1 / Prompt 5). The plugin compiles; the round trip can't be
  exercised yet.
- **Hardcoded `JObject` keys are the same fragility class as Drift 2.**
  `JObject.Value("totalLast7d")` returns null silently if the server shape
  drifts → blank chips, no error. A shared contract or a smoke test
  against a captured response would catch it.
- **Drift 4 makes the cross-host resolution itself a no-op** for the
  Revit↔Blender case — the panel is correct, the key it queries with is
  not.

---

## Execution status / branch state (UPDATED — consolidation has occurred)

**`claude/upbeat-noether-tg4pn` is now the integration branch.** As of
merge `50f10555f` (*"Merge branch 'claude/magical-mayer-hLnIk' … into
claude/upbeat-noether-tg4pn"*) it carries **both** all the implementation
work **and** a copy of this audit:

- Implementation (all verified present there): Bonsai client fix
  (`ff2c46564`, Drift 1) + DTO-driven test + `?ifcGuid` comment fix; the
  full Bonsai integration set (sync op, `host_element_id`, `prefs.py`,
  COORD panel); cross-host server work; BCC consumer (`cedcf3aba`);
  validation-warnings + raise-issue + ArchiCAD event log; the Prompt 2
  mobile `TaggedElement` fix; the Prompt 3 ingest-paths docs (`d056e2c5e`).
- This audit + prompts (merged in via `50f10555f`).

**`claude/magical-mayer-hLnIk` (this branch) is now behind and impl-less.**
`ff2c46564` is still unreachable here; `client.py` still has no
`_element_to_wire`; no bonsai impl files. It holds **only** these audit
docs. The session-5→8 review updates (Drifts 4–6, Prompts 5–10, the
corrections) were committed here **after** `50f10555f`, so they are **not
yet on the integration branch** — re-merge `magical-mayer → upbeat-noether`
to capture them, or author future audit edits directly on
`upbeat-noether`.

**Consequence for the remaining prompts:** run them against
`upbeat-noether-tg4pn` (where the code is). Prompts 1–4 there are
"already done" no-ops; the open work is Prompt 5 (API build), the mobile
`tsc` baseline, Prompt 7 (Revit IFC-GlobalId re-key), and Prompts 6/8/9/10.
Do **not** run them against this stale branch.

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

**Status (executed on `upbeat-noether-tg4pn`):** Prompts 1–9 are DONE.
**All six drifts are fixed.** Remaining is *prevention + hygiene*, not
correctness: Prompt 10 (systemic codegen — see decision below), two
baseline blockers (mobile `tsc`, test project), the EF-migration backlog,
the `TaggedElement.IfcGlobalId` follow-up, and one live multi-host
round-trip to confirm the derivation assumptions.

**Cross-host key is now the true IFC GlobalId on ALL hosts** (Prompt 7,
session 14 fixed the last holdout): Bonsai (`el.GlobalId`),
ArchiCAD/IFC-watcher (`el.GlobalId`), ArchiCAD/engine
(`ifcopenshell.guid.compress(GUID)`), Revit (`IFC_GLOBAL_ID_TXT =
IfcGloballyUniqueId = export GlobalId`). The cross-host identity feature is
now logically complete: consistent key everywhere + hardened mapping
(Prompt 6: observability + reconciliation) + de-duplicated clients
(Prompt 9).

## Parallel batch (session 15) — Prompts 10/13, 11, 12, 14 + corrections

Five sessions run in parallel (one per project tree, as advised). Status +
the corrections they surfaced:

- **Prompt 10/13 (server guardrail, Option 1) — DONE, committed `0a9e95de5`.**
  New `contract-drift.yml` CI gate (server-build + the Python conformance
  tests; parses the C# DTO *source*, no running server needed) — **proven
  live**: renaming `IfcElementDto.FullTag`/`IfcGlobalId` turns CI red,
  revert turns it green. `TaggedElementDto` + `[ProducesResponseType]` on
  `tagsync/elements/search`, healthcare/penetrations dashboards, compliance,
  transmittals list (all wire-identical — only EF nav artifacts dropped);
  `/ifc/data` + `/ifc/mappings` get explicit response types. `ValidationErrorDto`
  `{code,message,severity}` declared + a tolerant `TryParse` now exercised at
  the IFC-ingest write site (surfaces one summary warning on non-conforming
  blobs). Gate is deliberately **server-only** (no mobile `tsc` / `dotnet
  test` job — both red on the baselines). *Duplicate RESOLVED (Prompt 15,
  commit `8610ce5bc`): the second agent's fuller guardrail was confirmed a
  clean superset of `0a9e95de5` (`git merge-base --is-ancestor` holds;
  additive-only diffs, no contested hunks), committed as 9 explicitly-staged
  files with the two cross-host WIP files left untracked; gate re-proven
  red-on-rename / green-on-revert; whole-solution build 0 errors. Not yet
  pushed.*
- **Prompt 12 (Stabilize prereq) — #1 DONE (`5bd85fe2d`), #2 not deliverable.**
  Non-blocking "Run Stabilize IFC GUIDs first" precheck fires at the two
  user-initiated entry points (IFC export, BCC Sync Now) only when Missing
  or Stale > 0 (reuses the existing `StingStaleMarker`; opt-in
  `promptStabilise` param so auto/scheduled callers don't nag). **#2 (surface
  server `GLOBALID_DRIFT`) is impossible today** — the validator is a
  deferred no-op (see Drift 4 correction above); #1 is the available
  equivalent.
- **Prompt 11 (live round-trip) — harness + runbook DONE, live run pending lab.**
  `tools/tests/cross_host_round_trip.py` (3 guarded tests, skip-clean in CI,
  self-validated against a synthetic IFC) + `docs/CROSS_HOST_ROUND_TRIP_RUNBOOK.md`.
  **Corrected my step 4:** independently modelling the "same" element in
  Revit and ArchiCAD yields *two different* GlobalIds (different native
  GUIDs); `/ifc/mappings` joins rows that **share one** GlobalId, so the
  runnable acceptance is per-lineage (`{revit,blender}` on G_R,
  `{archicad,blender}` on G_A), not a tri-host unification — or use a single
  authoritative IFC. Also surfaced **R1** (in-repo exporters don't pin the
  GUID source — the real risk to Prompt 7's equality), **R2** (Stabilize
  skips never-exported elements → order export→stabilize→export-again), and
  **R4** (new latent bug, off-path).
- **Prompt 14 (hygiene) — Task 2 DONE (`367932207`), 1/3/4 deferred with facts.**
  Test project now builds; `dotnet test` runs 380 / 253 pass (was: didn't
  compile). Fixing it **unmasked two latent EF-model bugs that would crash
  the production DbContext**: `PhotoAlbumPhoto` + `PhotoNdaAcceptance` are
  composite-key join entities with no key config — now configured. **Corrected
  the EF-migration premise** (Task 3): the Photo DbSets + cross-host columns
  are **already migrated**; the only real issues are a *stale model snapshot*
  and the `ArchiCADEventLogPersistence` migration (which lives in the
  uncommitted ArchiCAD WIP). Task 1 (mobile `tsc`) deferred —
  *but the ~18 it called "structural WIP / fabrication" are actually 2
  typo'd import paths to existing files (corrected): `@/stores/auth` →
  `@/stores/authStore` (8 files), `@/src/components/MemberPicker` →
  `@/components/MemberPicker` (doubled `src/`, 1 file) — trivially fixable,
  clears ~18 of 111; the other ~93 are the unenumerated remainder.* Task 4 skipped
  (optional, highest risk).

**New latent bug (Prompt 11 R4):** `IfcGuidEncoder.FromGuid`
(`StingTools/IfcResults/IfcGuidEncoder.cs`) packs bytes `1/6/7/4/8/16`,
which is **not** canonical IFC compression (`1/3/3/3/3/3-2/4/4/4/4/4`)
despite a docstring claiming parity. Used only by DIALux + Clash export
(which match their own output), **never the cross-host key** — so it's
off-path and was not patched (per "don't fix blindly"), but it's a real
mislabelled encoder to fix separately.

**New production bug found + fixed (Prompt 14):** `PhotoAlbumPhoto` /
`PhotoNdaAcceptance` had no EF key configuration — EF validates the model on
every provider, so this would have **crashed the production DbContext at
runtime**, not just the tests. Surfaced only because the test project was
made to build. Fixed in `367932207`.

**One consolidated live round-trip validates the whole story.** Two
host-side derivations are "equal by construction" but unconfirmed against
a real export: Revit's `IFC_GLOBAL_ID_TXT == exported IFC GlobalId`
(Prompt 7) and ArchiCAD engine's `compress(GUID) == exported IFC GlobalId`
(Prompt 9). A single test — model in Revit + ArchiCAD → export IFC →
ingest in Bonsai → confirm `/ifc/mappings?ifcGuid=` resolves all rows —
confirms both. This is the only unverified link left in cross-host
correctness.

**Prompt 10 decision (session 13, recommended):** **Option 1 — scoped
guardrail.** Verified blast radius is ~5x the prompt's estimate (~227
anonymous `Ok(new {…})` across 96 of 119 controllers; only 55
`ProducesResponseType`); full codegen (Option 2) is multi-week and its CI
gate is blocked by the two baselines anyway, while most protective value is
already shipped (the Prompt-1 Python DTO-drift test, the Prompt-2 mobile
conformance test, the Prompt-5 cross-host doc). Option 1 = response DTOs
for the ~7 client-consumed endpoints where drift actually occurred
(tagsync search, healthcare/penetrations dashboards, compliance,
transmittals, `/ifc/*`; meetings is lowest-priority — essentially clean) +
a real `ValidationErrors` DTO + a **server-build + conformance-test** CI
gate (NOT a mobile-`tsc`/test-suite gate — those can't go green until the
baselines are cleared). Days, not weeks. Defer Option 2 unless drift
recurs despite the guardrail.

0. ~~**Prompt 5 — fix the pre-existing `Planscape.API` build breakage.**~~
   **DONE (session 9).** The audit's "6 errors" was an *underestimate* —
   CS0101/CS0111 duplicate-type errors halt semantic analysis and **masked
   a true set of 51 errors**; fixing the dups unmasked the rest. Real scope
   was broader than the prompt listed (IssuesController CS0128,
   IssueAudioNotesController CS0103, SeedData, 5 missing Photo DbSets).
   Driven to **0 errors** across 8 files + DbContext (de-dup + dead-ref
   repair, 21 ins / 157 del); Photo DbSets were **restored not deleted**
   (entities exist + controllers routed = half-added — correct call); dup
   types renamed identifier-only (no wire change). The "trust the compiler,
   not the prompt" instruction paid off literally. **EF migration for the
   restored Photo DbSets is still a pending runtime step.**
1. ~~**Drift 1 (Bonsai)**~~ — **DONE** (`ff2c46564` + test/comment residue).
2. **Drift 4 (Prompt 7) — Revit cross-host key.** CRITICAL and still open:
   until Revit keys on the true IFC GlobalId (`IFC_GLOBAL_ID_TXT`) instead
   of `Element.UniqueId`, the entire cross-host identity investment is a
   no-op for Revit↔Blender. Highest *feature* value of everything here.
3. ~~**Drift 2 (Mobile)**~~ — **DONE (session 6)**, approach A (rename +
   2 consumers in `Planscape/app`). ComplianceSnapshot/Transmittal field
   drifts (extended Drift 2) **still open**.
4. ~~**Drift 3**~~ — **DONE (session 7)** — `element-ingest-paths.md` +
   XML-docs; real invariant captured.
5. ~~**Cross-host hardening (Prompt 6)**~~ — **DONE (session 10).**
   Fire-and-forget **kept** and gap-closed (deliberate, documented):
   `CrossHostMappingAudit.RecordUpsertFailureAsync` → `AuditLog` + ILogger
   on failure; `MappingReconciliationJob` (Hangfire hourly) backfills
   Revit-keyed mappings from committed `TaggedElement` rows; `MappingHosts`
   guard so a mis-attributed host fails the audit rather than poisoning the
   table; authority documented (`ExternalElementMapping` = GlobalId↔host;
   `*IfcGlobalId` columns = "this record is about element X"; `by-ifc`
   endpoint made resilient to one surface being populated without the other).

**Baseline blockers (gate any CI conformance/test gate, incl. Prompt 10):**
- `Planscape.API` build — **FIXED (Prompt 5).** EF migrations are now
  generatable (the API builds), so the pending migrations (restored Photo
  DbSets, `ArchiCADEventLogPersistence`, cross-host columns) can be
  produced — do this next on the schema side.
- **Mobile `tsc`** — `Planscape/app/` still carries ~111 pre-existing
  errors; not yet cleared.
- **Test project** — `Planscape.Server/tests/Planscape.Tests` has ~7
  pre-existing errors (duplicate `WebApplicationFactory` definitions +
  Program accessibility), surfaced by session 10. The test project does
  **not build**, so there is no runnable test suite / test CI gate until
  this is cleared. Sibling to the other two; agent-reported (no .NET build
  in this audit env), structure corroborated.

**Systemic option (Prompt 10):** Drifts 1/2/4/5/6 share one root cause —
each client hand-writes the wire shape. Generating the client types from
the server's OpenAPI (with explicit response DTOs) + a CI drift gate stops
recurrence. Justified only if drift keeps happening; otherwise the manual
prompts suffice. Its CI gate needs all three baseline blockers green.

**Two facts from the session-5 review:**
- `dotnet ef migrations add` builds the **startup (API) project**, which
  does not compile (Prompt 5). So the pending migrations
  (`20260601000000_CrossHostIdentityFields`, `ArchiCADEventLogPersistence`)
  **cannot be generated until Prompt 5 lands** — it's a hard prerequisite
  for schema work, not just testing.
- `TaggedElement.ValidationErrors` is an **unschematized JSON string** whose
  shape already diverges by host (Bonsai now writes severity-tagged
  objects; Revit writes plain strings). Harmless today (no typed consumer)
  but a Drift-in-waiting — Prompt 10 step 5 declares a shape for it.
- The Bonsai raise-issue operator correctly anchors `modelElementGuid` on
  the **true IFC GlobalId**, which makes Drift 4 (Revit keys on
  `Element.UniqueId`) the definitive blocker for Blender→Revit issue
  resolution — Prompt 7 is what makes raise-issue work cross-host.

**Before any of the above: consolidate branches** (see Execution status).
Drift 1 is already fixed on `claude/upbeat-noether-tg4pn`; running its
prompt here would redo it. Land that branch (or move this audit onto it)
first so prompts act on the real current state.

Authority is **server camelCase JSON**; align both client edges to it.
Do **not** touch the `TaggedElement` entity / DB columns or the shipped
migration.

### Verification touchpoints (server-side, authoritative)

- `Planscape.Server/src/Planscape.Core/DTOs/IfcIngestDtos.cs` — IFC wire shape
- `Planscape.Server/src/Planscape.Core/Entities/TaggedElement.cs` — projection columns
- `Planscape.Server/src/Planscape.API/Controllers/IfcController.cs:46,201` — bind sites
- `Planscape.Server/src/Planscape.API/Controllers/TagSyncController.cs:283` — mobile search return
- `shared/ifc/psets/Pset_StingTags.xml` — canonical property names
