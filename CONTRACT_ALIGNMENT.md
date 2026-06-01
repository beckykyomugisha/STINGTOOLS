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
writing the same projection.

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

## Execution status / branch divergence (READ BEFORE RUNNING PROMPTS)

The fixes for these drifts are **accumulating on a different branch than
this audit.** As of this writing:

- `claude/upbeat-noether-tg4pn` (Windows env, `C:\Dev\STINGTOOLS`) has:
  the Bonsai client fix (`ff2c46564`, Drift 1) + its DTO-driven test +
  the `?ifcGuid` comment fix; the cross-host server work; and the BCC
  consumer work (`cedcf3aba`). **`ff2c46564` is not even reachable from
  this clone.**
- `claude/magical-mayer-hLnIk` (this branch) has: only these audit docs.
  Drift 1 is genuinely **not done here** — `client.py` still sends raw
  snake_case, no `_element_to_wire`, no test, stale `IfcController.cs:195`
  comment.

**Consequence:** prompts run against `upbeat-noether-tg4pn` keep returning
"already done"; the same prompts against this branch are still real work.
**Consolidate the two branches (or move this audit to where the code is)
before executing more prompts** — otherwise the docs keep describing a
world the sibling branch has already changed.

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
2. **Drift 4 (Prompt 7) — Revit cross-host key.** CRITICAL: until Revit
   keys on the true IFC GlobalId (`IFC_GLOBAL_ID_TXT`) instead of
   `Element.UniqueId`, the entire cross-host identity investment is a
   no-op for Revit↔Blender. Highest *feature* value of everything here.
3. **Drift 2 (Mobile)** — one interface rename or one adapter function;
   resolve the `lvl`/`level` collision while there.
4. **Drift 3** — documentation only (largely subsumed by the cross-host
   work, which made TagSync populate the mapping table).
5. **Cross-host hardening** (Prompt 6) — `AuditLog` + reconciliation for
   the fire-and-forget mapping upsert; document mapping-table vs.
   `*IfcGlobalId`-column authority.

**Systemic option (Prompt 10):** Drifts 1/2/4/5/6 share one root cause —
each client hand-writes the wire shape. Generating the client types from
the server's OpenAPI (with explicit response DTOs) + a CI drift gate stops
recurrence. Justified only if drift keeps happening; otherwise the manual
prompts suffice. Prompt 5 is its prerequisite (needs a building API).

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
