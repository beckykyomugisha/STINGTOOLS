# Contract Alignment — Implementation Prompts

Self-contained task prompts for the findings in
[`CONTRACT_ALIGNMENT.md`](CONTRACT_ALIGNMENT.md). Each is independent and
hand-off-ready: copy one into a fresh session as the task.

## How to use these prompts (read first)

**Each prompt is a hypothesis from a prior audit, not ground truth.** The
audit was done by inspection on a specific commit; code may have moved,
the field maps may be incomplete, and the framing may be wrong. Before you
implement:

1. **Re-verify the claim against the live code.** Open the cited files and
   line numbers and confirm they still say what the prompt says. Line
   numbers drift — trust the symbol names over the line numbers.
2. **Challenge the fix.** Is the stated direction actually correct? Is
   there a simpler or safer one? Does it miss a case (other call sites,
   other endpoints, other fields)? Each prompt ends with a
   **"Verify / challenge before you build"** list — treat those as the
   *known* unknowns, not the complete set, and add any you find.
3. **Stop and report instead of guessing.** If the prompt is wrong,
   internally contradictory, or the real code has already diverged from
   the audit, say so and propose the corrected task — do **not** force the
   stated change through. A prompt that turns out to be a no-op (already
   fixed) is a valid finding; report it as such.
4. **Scope honestly.** If verifying reveals the change is bigger than the
   prompt implies (e.g. many UI consumers, a migration needed), surface
   that and ask before expanding scope.

The acceptance criteria in each prompt are a floor, not a ceiling.

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
>
> **Verify / challenge before you build:**
> - Diff the field map above against the **current** `IfcElementDto` —
>   members may have been added/removed since the audit. The map must be
>   driven by the DTO, not by this list.
> - Confirm what shape the *caller* actually passes in. The sync operator
>   that feeds `ingest_ifc_data` does **not exist in this branch** (it's
>   part of Prompt 4 / `ff2c46564`), so the snake_case input contract is
>   assumed. If you build the caller too, you may not need a snake→camel
>   map at all — you could emit camelCase at the source. Decide which is
>   cleaner and say why.
> - `compliance_pct` → bools: is there a canonical threshold anywhere
>   (server, `SpatialChecker`, tag validator)? If not, prefer dropping it
>   and letting the validator-produced `is_*` bools flow, rather than
>   inventing a cutoff.
> - Check the **other** client methods (`sync_tags`, `raise_issue`,
>   `push_compliance`) for the same snake_case-body bug. `sync_tags`
>   already sends `projectId` (camel) — is its element shape also camel?
>   Fix or flag consistently; don't fix one method and leave siblings
>   broken.
> - Confirm System.Text.Json on the server is case-*insensitive* on read
>   (it is by default) so a camelCase body binds — but verify no
>   `[JsonPropertyName]` attributes on the DTO override the member names.

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
> **Acceptance:** the element-search screen shows populated
> discipline/system/tag fields; the `lvl` vs `level` ambiguity is resolved
> explicitly in code (use `element.lvl` for the level code, not
> `element.level`/name). Add a compile-time conformance test that asserts a
> captured server JSON is assignable to the type and `@ts-expect-error`-
> guards the old verbose aliases (session 6 did this — keep it). NOTE: a
> global `tsc --noEmit` is **not** clean here — `Planscape/app/` has ~111
> pre-existing errors (WIP, out of scope). Prove instead that your change
> is **byte-identical** to that baseline (zero *new* errors) and that the
> touched files compile clean.
>
> **Verify / challenge before you build:**
> - Confirm `SearchElements` still returns the **raw entity** (not a DTO)
>   on the current server — re-read `TagSyncController.cs` around the
>   `elements/search` route. If a projection DTO was added since the audit,
>   the mobile interface may already be correct and this is a no-op:
>   report that.
> - Find **every** producer of `TaggedElement`, not just search. There's
>   at least `endpoints.ts:257` (search), `:273` (recent?), and
>   `apiClient.ts:39` (`/tagsync/elements/{id}`). A mapper must wrap all of
>   them or you'll fix one screen and leave others broken.
> - Find every **consumer** (grep the `TaggedElement` type across
>   `Planscape/src` **and `Planscape/app`** to size approach (A) vs (B)
>   honestly before choosing. NOTE (session-6 correction): the real
>   consumers live in `Planscape/app/` (Expo Router screens —
>   `app/(tabs)/scanner.tsx`, `app/ifc/index.tsx`), not `Planscape/src`,
>   which is only the data/types layer. A `Planscape/src`-only grep falsely
>   reports zero consumers.
> - `tag7Summary` vs the server's `tag7` (+ `tag7A..tag7F` sub-segments):
>   decide whether `tag7Summary` should map to `tag7`, and whether the
>   mobile type should surface the sub-segments at all.
> - The mobile interface **drops** server fields that may matter
>   (`isComplete`, `isFullyResolved`, `isStale`, `previousTag`,
>   `lastModifiedUtc`). If any screen needs compliance state, add them
>   rather than silently leaving them off.
> - Is `TaggedElement` actually rendered anywhere today, or is this dead
>   code? If unused, the cheapest correct fix may be to align the type and
>   move on — but confirm, don't assume.
> - **This drift is not limited to `TaggedElement`.** A sweep confirmed the
>   same camelCase-name mismatch on `ComplianceSnapshot`
>   (`compliancePercent` vs server `tagPercent` — dashboard %, user-visible)
>   and `Transmittal` (`transmittalNumber`/`issuedBy`/`issuedTo` vs server
>   `transmittalCode`/`createdBy`/`recipient` — list view, user-visible).
>   Fix them in the same pass. `DocumentRecord.updatedAt` (nullable) and
>   `Meeting.type` (optional legacy fallback the server never emits) are
>   minor — handle defensively. `BimIssue` / `IssueAttachment` / `Project` /
>   `UserProfile` / `LoginResponse` / `NotificationPreferences` are clean —
>   don't churn them.

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
> writing the same `TaggedElement` table, when to use which, and the real
> invariant (session-7 correction): the DTOs are **not** wire-compatible
> (they already diverge — `Disc/Loc/Lvl/…` vs `Discipline/Location/Level/…`);
> what must be preserved is (1) both mappers targeting the **shared
> `TaggedElement` columns**, and (2) the **filtered-unique key-space**
> (`(ProjectId, RevitElementId) WHERE RevitElementId>0` for Revit;
> `(ProjectId, UniqueId)` for non-Revit — `PlanscapeDbContext.cs:605-609`).
> **No behavioural change. No new
> endpoint. BCC/Revit needs no code change** — it is already aligned to
> TagSync.
>
> **Verify / challenge before you build:**
> - Before claiming the two paths "must stay field-compatible," actually
>   diff `TagSyncRequest`/`TagSyncElementDto` against `IfcElementDto`. They
>   likely **already diverge** (e.g. IFC has `IfcClass`/`IfcGlobalId`;
>   TagSync has `Tag7A..F` + `RevitElementId`). Document the real
>   divergence and which fields are the shared core — don't assert a
>   compatibility that isn't there.
> - Confirm BCC/Revit (`PlanscapeServerClient`) really uses only
>   `/tagsync/*` and never `/ifc/data`. If a code path does call `/ifc/data`
>   from Revit, the "no change" claim is wrong — flag it.

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
>
> **Verify / challenge before you build:**
> - **First confirm `ff2c46564` is reachable** from this repo
>   (`git cat-file -t ff2c46564`, `git log ff2c46564`). It was authored on
>   `claude/upbeat-noether-tg4pn` and may not be in the local graph. If
>   it's not fetchable, reconstruct from the description + the screenshot
>   in the session history rather than cherry-picking — and say so.
> - Re-verify the API signatures the sync op depends on:
>   `SpatialChecker.check_all_elements()`, `validate_tag()`, and the
>   `Pset_StingTags` property names in `shared/ifc/psets/Pset_StingTags.xml`.
>   The audit found these are `Discipline/Location/Zone/Level/System/
>   Function/Product/Sequence/FullTag` — confirm before wiring.
> - The `from __future__ import annotations` / `bpy.props` trap is the one
>   landmine here. Don't just delete the import blindly — understand *why*
>   PEP-563 breaks `StringProperty` registration so you don't reintroduce
>   it elsewhere (handlers/ops that declare props).
> - This set was integration-tested against ifcopenshell 0.8.5 only, never
>   in live Blender. Treat the panel/operator/prefs registration as
>   **unverified** and call that out — ideally load it in Blender before
>   claiming done.

---

## Prompt 5 — Fix the pre-existing `Planscape.API` build breakage (do FIRST)

> **`Planscape.Server/src/Planscape.API` does not compile**, and it has
> nothing to do with IFC/tag work — it's pre-existing on `main`/HEAD.
> Confirmed CS0101 duplicate top-level types in namespace
> `Planscape.API.Controllers`:
>
> - `AddMemberRequest` — `Controllers/DistributionGroupsController.cs:229`
>   **and** `Controllers/ProjectMembersController.cs:552`.
> - `ReorderRequest` — `Controllers/IssueCustomFieldsController.cs:216`
>   (`record ReorderRequest(ReorderItem[] Items)`) **and**
>   `Controllers/PhotoAlbumsController.cs:465`
>   (`record ReorderRequest(Guid[] Order)`).
> - Plus reported breakage in `SeedData.cs` / `SitePhotosExtController.cs` /
>   `Services/PhotoAclGate.cs` referencing absent `Photo*` DbSets, and a
>   `DocumentsController.ValidateName` collision.
>
> A non-building API means the server can't deploy and every other
> server-side task here is untestable by a full build. Fix it as a **small,
> self-contained PR** before anything else merges on top:
>
> 1. Rename the duplicates to distinct, intent-revealing names — e.g.
>    `AddDistributionMemberRequest` (DistributionGroups) vs the
>    project-members one; `ReorderAlbumsRequest` (PhotoAlbums) vs
>    `ReorderCustomFieldsRequest` (IssueCustomFields). Update every
>    reference in the owning controller only.
> 2. Resolve the `Photo*` DbSet references — either restore the missing
>    `DbSet<Photo*>` on `PlanscapeDbContext` or remove the dead
>    controllers/services that reference them, whichever matches intent.
>    Investigate git history to see which side is the orphan.
> 3. Get `dotnet build Planscape.Server/src/Planscape.API` to **0 errors**.
>
> **Acceptance:** the API project builds clean. No behavioural change to
> any endpoint — this is purely de-duplication / dead-reference removal.
> No DTO field renames that would change the wire contract (rename only
> the C# type identifiers, not their `[JsonPropertyName]` / route-bound
> shapes).
>
> **Verify / challenge before you build:**
> - There is **no .NET toolchain in the audit sandbox** — the error list
>   above is from source inspection, not a build. Run an actual
>   `dotnet build` first to get the *real, complete* error set; it may be
>   more or fewer than the 6 reported. Fix what the compiler says, not what
>   this prompt guesses.
> - For each duplicate, check which controller "owns" the canonical name
>   and which should be renamed (don't rename the one with more references
>   if you can avoid it). The two `ReorderRequest` records have **different
>   shapes**, so they were never meant to be the same type — confirm no
>   caller relies on the collision.
> - For the `Photo*` DbSets: determine whether the feature was half-removed
>   or half-added. Restoring vs deleting are opposite fixes — pick based on
>   whether the controllers are wired into routing and whether the entities
>   still exist. If ambiguous, stop and ask.

---

## Prompt 6 — Harden the cross-host mapping upsert (after Prompt 5)

> A separate session added cross-host identity to the server: an extracted
> `IfcIngestService` (`IngestAsync` + `UpsertMappingsAsync`), TagSync now
> carrying `Host`+`HostDocumentGuid` and firing a **best-effort,
> fire-and-forget** `ExternalElementMapping` upsert in a fresh DB scope
> after commit, plus nullable `RoomIfcGlobalId`/`ElementIfcGlobalId`
> columns on healthcare/penetration entities and migration
> `20260601000000_CrossHostIdentityFields`. The behaviour is good; harden
> two weaknesses:
>
> 1. **Fire-and-forget is lossy.** The mapping table *is* the deliverable
>    (cross-host issue resolution depends on it), yet a process restart or
>    transient DB error in the background scope silently drops the row with
>    no reconciliation. Add: (a) failure logging to `AuditLog`; and (b) a
>    reconciliation/backfill path — e.g. a periodic job, or a
>    `UpsertMappingsAsync` re-run that derives missing mappings from
>    existing `TaggedElement` rows (which carry `UniqueId == IfcGlobalId`
>    and host attribution). Consider whether folding the upsert into the
>    sync transaction is acceptable instead (measure the latency cost).
> 2. **Two sources of truth for "entity ↔ IFC GlobalId".** The
>    `ExternalElementMapping` index and the new `*IfcGlobalId` entity
>    columns can diverge. Document which is authoritative in the entity
>    XML-docs and the controller summaries, and make the dependent
>    queries (`healthcare/by-ifc/{ifcGlobalId}`) resilient to one being
>    populated without the other.
>
> Also confirm non-Revit callers set `Host` explicitly — TagSync defaults
> it to `"revit"`, so a Blender/ArchiCAD caller that omits it poisons the
> mapping table with wrong host attribution.
>
> **Acceptance:** mapping-upsert failures are observable (AuditLog),
> mappings are recoverable after a dropped background write, and the
> authority between the two identity surfaces is documented. Core +
> Infrastructure build clean; API builds clean (depends on Prompt 5).
>
> **Verify / challenge before you build:**
> - This cross-host work is **on a different branch, not here** — confirm
>   it's actually merged/reachable before hardening it, or you'll be
>   patching code that doesn't exist on your branch. Locate the real
>   `IfcIngestService` and TagSync changes first.
> - Decide deliberately between "same-transaction upsert" and
>   "fire-and-forget + reconciliation." The original chose fire-and-forget
>   *specifically so the mapping never fails the user's sync* — don't
>   silently regress that UX; if you move it into the transaction, justify
>   it with the measured latency.
> - Check whether a reconciliation job already exists anywhere
>   (`Hangfire`, `SyncScheduler`) before adding a new one.

---

## Prompt 7 — Make Revit key cross-host identity on the true IFC GlobalId (CRITICAL, Drift 4)

> The cross-host identity feature is a **no-op between Revit and
> Bonsai/ArchiCAD** because the hosts disagree on what the cross-host key
> *is*. `ExternalElementMapping` and `GET /ifc/mappings?ifcGuid=` are keyed
> on **IfcGlobalId**, but the Revit plugin stuffs `Element.UniqueId`
> (Revit's 45-char UniqueId) into that field — confirmed by the comment at
> `StingTools/BIMManager/PlanscapeServerClient.cs:1040`
> (*"keys on IfcGlobalId (Revit UniqueId in our case)"*). Bonsai/ArchiCAD
> send the true 22-char IFC GlobalId. The IFC GUID is *derived from* the
> Revit UniqueId but is not equal to it, so a Revit lookup never matches a
> Bonsai row, and "issue in Blender → highlight in Revit" silently fails.
>
> The plugin **already computes the correct key**:
> `StingTools/Commands/Interop/StabilizeIfcGuidsCommand.cs` persists Revit's
> `IfcGloballyUniqueId` into the shared param `IFC_GLOBAL_ID_TXT`
> (feeding `ElementGlobalIdRegistry` / `IfcAlignmentValidator`). The true
> IFC GlobalId is the **only** key every host can produce.
>
> Change the Revit cross-host path to key on `IFC_GLOBAL_ID_TXT`, not
> `Element.UniqueId`:
> 1. Wherever the Revit sync upserts `ExternalElementMapping` (the
>    TagSync fire-and-forget path from the cross-host server work) and
>    wherever the BCC selection resolution calls `GetIfcMappingsAsync` /
>    `/ifc/mappings?ifcGuid=`, read `IFC_GLOBAL_ID_TXT` off the element and
>    use that as the `ifcGuid`. Keep `Element.UniqueId` only as the
>    *host_element_id* (the host-side identifier), which is its correct
>    role.
> 2. When `IFC_GLOBAL_ID_TXT` is empty (Revit re-generates IfcGUIDs across
>    sessions until stabilised), **fall back gracefully** and surface a
>    one-line "run *Stabilize IFC GUIDs* first" hint rather than silently
>    querying with the wrong key.
> 3. Make `StabilizeIfcGuidsCommand` a documented prerequisite of the
>    cross-host workflow (it already exists — wire it into the sequence /
>    mention it in the BCC panel's empty state).
>
> **Acceptance:** a Revit element that has been through *Stabilize IFC
> GUIDs* and exported to IFC, then ingested by a Bonsai sync of the same
> IFC, resolves to the Bonsai host row via `/ifc/mappings` (and vice
> versa). Where no live multi-host setup exists, prove the key alignment
> at the unit level: the value Revit sends as `ifcGuid` equals the
> `IfcGlobalId` Bonsai would send for the same element (both = the IFC
> `GlobalId`, not the Revit UniqueId).
>
> **Verify / challenge before you build:**
> - This spans the cross-host **server** work and the **BCC** consumer,
>   both on `claude/upbeat-noether-tg4pn`, **not this branch** — confirm
>   they're reachable/merged before editing, or you'll patch absent code.
> - Confirm the direction of Revit's IFC GUID derivation:
>   `IfcGloballyUniqueId` (what `StabilizeIfcGuidsCommand` reads) **is** the
>   value Revit writes to the exported IFC file and the value Bonsai then
>   sees — verify this equality holds in your Revit version before
>   committing to it as the join key. If Revit re-derives on export, the
>   stabilised param and the file GUID could differ; that would change the
>   fix.
> - There may be **other** Revit→server paths that also key on UniqueId
>   (BOQ lines at `PlanscapeServerClient.cs:1043`, P6 link, geometry sync).
>   Decide whether this change is scoped to cross-host resolution only, or
>   whether the UniqueId-as-IfcGlobalId convention should change
>   project-wide. Flag the blast radius before expanding scope — a
>   project-wide key change is a much bigger task than the BCC feature fix.
> - `TaggedElement.UniqueId` already stores the Revit UniqueId for the
>   Revit path and the IFC GlobalId for the `/ifc/data` path — that
>   overload is itself part of the problem. Decide whether to split it
>   (e.g. add an explicit `IfcGlobalId` column) rather than overloading
>   `UniqueId`, and note the migration cost.

---

## Prompt 8 — Fix the Revit client ↔ server endpoint mismatches (Drift 6)

> Four calls in `StingTools/BIMManager/PlanscapeServerClient.*.cs` target
> server paths/verbs that don't exist → runtime 404/405:
>
> 1. `SendTransmittalAsync` @ `PlanscapeServerClient.cs:904` POSTs
>    `.../transmittals/{id}/send`, but the server is
>    `[HttpPut("{txId}/send")]` (`TransmittalsController.cs:358`) → **405**.
>    Change the client to PUT.
> 2. `PushWarningsAsync` @ `:955` POSTs `.../warnings`, but the server only
>    exposes `[HttpPost("report")]` (`WarningsController.cs:33`) → **404**.
>    Point the client at `.../warnings/report` (confirm the body matches
>    that action's DTO).
> 3. `PushBoqSnapshotAsync` @ `:1001` POSTs `.../boq/snapshot`, which has no
>    route in `BoqController` → **404**. Either add the server route or
>    redirect to the existing baseline/lines flow — investigate which was
>    intended (the BOQ baseline endpoints at `:1027`/`:1050` already exist).
> 4. `FullSyncAsync` @ `:382` POSTs `/api/tagsync/fullsync` (no route) but
>    is already `[Obsolete("Use SyncScheduler…")]`. Confirm no live caller,
>    then **delete it** rather than re-adding the route.
>
> **Acceptance:** every `/api/` literal in the client resolves to a real
> route + verb; the plugin builds clean; no behavioural change beyond the
> path/verb corrections. Don't touch request body shapes (they're already
> camelCase-correct).
>
> **Verify / challenge before you build:**
> - Re-run the inventory yourself (grep `"/api/` across the partials vs the
>   controllers) — line numbers drift and there may be more than these four.
>   Trust the current source over this list.
> - For #2 and #3, the *body* may also need to change to match the real
>   action's DTO — a path fix alone can still 400 if the payload shape is
>   wrong. Check the target action's parameter type.
> - For #4, grep for callers of `FullSyncAsync` before deleting; if
>   `SyncScheduler` or a UI button still calls it, migrate the caller first.

---

## Prompt 9 — Route StingBridge through the cross-host contract + de-duplicate the Python client (Drift 5)

> `StingBridge/planscape/client.py` is a **second, divergent** Planscape
> Python client (separate from
> `stingtools-core/python/stingtools_core/planscape/client.py`). The
> ArchiCAD/IFC-watcher bridge posts to the legacy `/api/tagsync/sync` and
> **fabricates a fake `revitElementId`** from `md5(ifc_guid)` (`client.py:181`,
> sent at `:184`), with no `Host`, no `HostElementId`, and no
> `ExternalElementMapping` write. ArchiCAD elements therefore land as
> pseudo-Revit rows and are invisible to cross-host resolution.
>
> 1. Migrate the bridge to `POST /api/projects/{id}/ifc/data` with
>    `Host="archicad"`, the **true IFC GlobalId** as the key, and the
>    ArchiCAD element GUID as `HostElementId`. Reuse the camelCase
>    `_element_to_wire` shaping from the core client (see Prompt 1) — do
>    not re-invent it, and do not send the synthetic `revitElementId`.
> 2. **De-duplicate the two Python clients.** Either make `StingBridge`
>    import and use `stingtools_core.planscape.PlanscapeClient`, or retire
>    one. Two diverging implementations of the same wire contract is how
>    Drift 1 and Drift 5 happened independently.
>
> **Acceptance:** an ArchiCAD IFC sync produces real `ExternalElementMapping`
> rows (Host="archicad", true IFC GlobalId, ArchiCAD GUID as host id) and a
> `TaggedElement` projection without a fabricated Revit id; there is a
> single shared Python client (or a clearly-retired one). Smoke tests green.
>
> **Verify / challenge before you build:**
> - **There are two sync paths, not one** (session-11 finding): the IFC
>   watcher (`watch/ifc_watcher.py`, only has `el.GlobalId`) *and* the live
>   ArchiCAD engine (`sync/engine.py`, has the ArchiCAD GUID but no IFC
>   GlobalId → derive via `ifcopenshell.guid.compress(GUID)`). Handle both.
>   Confirm the engine's `compress(GUID)` equals what ArchiCAD's IFC export
>   writes (live round-trip) before relying on engine-path matching.
> - Confirm the IFC watcher can actually recover the ArchiCAD element GUID
>   (not just the IFC GlobalId) to populate `HostElementId`; if it only has
>   the IFC GlobalId, decide what `HostElementId` should be and say so.
> - This depends on the core client's `_element_to_wire` (Prompt 1) and the
>   `/ifc/data` path being reachable — both are on
>   `claude/upbeat-noether-tg4pn`, **not this branch**. Confirm branch state
>   first (see Execution status in the audit) so you build against real code.
> - Don't break the legacy `/tagsync/sync` path if anything still depends on
>   it — check before switching wholesale; a dual-write transition may be
>   safer than a hard cutover.

---

## Prompt 10 — Generate the client contracts from the server (systemic fix for all drifts)

> **Why this exists.** Drifts 1, 2, 4, 5, and 6 are all the same root cause:
> every client (Bonsai Python, mobile TypeScript, Revit C#, StingBridge,
> ArchiCAD) **hand-writes its own copy** of the server's wire shape, so they
> drift independently — snake_case vs camelCase, `assTag1` vs `tag1`,
> `compliancePercent` vs `tagPercent`, `transmittalNumber` vs
> `transmittalCode`, Revit `UniqueId` vs true IFC GlobalId, wrong paths/verbs.
> Patching each by hand (Prompts 1–9) fixes today's drift; it doesn't stop
> tomorrow's. This prompt makes the **server the single generated source of
> truth** so the clients can't drift silently.
>
> **Prerequisite:** Prompt 5 must land first. The generator pivots on the
> API's OpenAPI document, which requires the API project to **build and
> run** — and it currently doesn't (pre-existing CS0101s). Same blocker also
> stops `dotnet ef migrations add`, so Prompt 5 is the universal unblock.
>
> **Build the contract pipeline:**
> 1. **Make the server emit a precise OpenAPI doc.** ASP.NET can already
>    produce Swagger/OpenAPI, but several endpoints return **anonymous
>    objects** (`Ok(new { byStatus, byHost })`) or **raw entities**
>    (`TagSyncController.SearchElements` → `Ok(elements)`), which OpenAPI
>    types as untyped `object` — useless for generation. Introduce explicit
>    **response DTOs** (or `[ProducesResponseType(typeof(XDto), 200)]`) for
>    every endpoint a client consumes, starting with the ones the audit
>    flagged: tagsync element search, healthcare/penetrations dashboards,
>    compliance snapshot, transmittals, meetings, `/ifc/mappings`,
>    `/ifc/data`. **This is the bulk of the work** — generation is cheap
>    once the schema is precise.
> 2. **Generate the TypeScript types** for `Planscape/` from that OpenAPI
>    (e.g. `openapi-typescript`) into a single generated module, and replace
>    the hand-written interfaces in `Planscape/src/types/api.ts` that the
>    audit found drifted (`TaggedElement`, `ComplianceSnapshot`,
>    `Transmittal`) with the generated ones. Keep a thin hand-written
>    adapter layer only where the UI genuinely wants different names — but
>    make the *wire* type generated.
> 3. **Generate (or assert) the Python wire shapes** for
>    `stingtools-core/python/.../planscape/client.py`. At minimum, generate a
>    test fixture of valid DTO member names from the OpenAPI and assert the
>    client's `_element_to_wire` output (Prompt 1) is a subset — so a server
>    DTO change breaks the Python test, not production. Do the same for
>    StingBridge once it's de-duplicated (Prompt 9).
> 4. **Encode the canonical cross-host key.** The schema and generated
>    types must name the cross-host element key `ifcGlobalId` (the true
>    22-char IFC GlobalId) everywhere, and there should be a single
>    documented place that says: host-side ids (`Element.UniqueId`,
>    ArchiCAD GUID, Blender object name) are `hostElementId`, never the
>    cross-host key. This is the contract-level statement of the Drift 4/5
>    fix; Prompts 7 and 9 are its client-side implementations.
> 5. **Schematize the opaque blobs.** `TaggedElement.ValidationErrors` is
>    currently an unschematized JSON string whose shape already diverges by
>    host (Bonsai writes severity-tagged objects; Revit writes plain
>    strings). Declare a real DTO for it (`[{ code, message, severity }]`)
>    so it's part of the generated contract rather than convention.
> 6. **Wire drift-detection into CI.** Add a CI step that regenerates the
>    TS types + Python fixture from the current server and fails if they
>    differ from what's committed (the same pattern as the existing
>    `tools/enums/compute_checksums.py` drift gate for the IFC substrate).
>    That is what actually stops recurrence.
>
> **Acceptance:** the drifted mobile/Python types are generated from the
> server (not hand-written); a deliberate rename of a server DTO member
> fails CI until the clients regenerate; the cross-host key is `ifcGlobalId`
> in the generated schema; `ValidationErrors` has a declared shape. No
> behavioural change to endpoints beyond adding response DTOs.
>
> **Verify / challenge before you build:**
> - **Confirm the scope is worth it.** Six surfaces drift, but if the team's
>   real intent is "ship the per-drift fixes (1–9) and move on," a full
>   codegen pipeline may be over-engineering. Read the room: this is the
>   *systemic* fix, justified only if drift keeps recurring. Say so and let
>   the maintainer choose codegen vs. the manual prompts.
> - **OpenAPI precision is the crux, not the generator.** Most endpoints
>   return entities/anonymous objects; without response DTOs the generated
>   types are `any`/`object` and you've gained nothing. If introducing
>   response DTOs across ~20 controllers is too big a blast radius, scope
>   this to the handful of client-consumed endpoints the audit named and
>   leave the rest — but be explicit about the partial coverage.
> - **Don't regenerate the clean types into churn.** `BimIssue`,
>   `IssueAttachment`, `Project`, `UserProfile`, `LoginResponse`,
>   `NotificationPreferences` already match — generation should reproduce
>   them, not rename them. If the generator's naming disagrees with the
>   existing clean types, that's a sign the generator config is wrong, not
>   the types.
> - **Pick the pivot deliberately.** OpenAPI-from-ASP.NET is the obvious
>   choice, but check whether the repo already has a contract artifact
>   (the IFC substrate under `shared/ifc/`, `Pset_*` XML, IDS files) that
>   should be the source for the *tag* fields instead — the 8-segment tag
>   names (`Discipline`/`System`/`FullTag`) are already canonicalised there.
>   Reconcile the two so you don't create a *seventh* vocabulary.
> - **This supersedes the manual client edits, not the server fixes.**
>   Prompts 5 (API build), 7 (Revit key), 8 (endpoint paths/verbs) are
>   server/Revit behavioural fixes codegen does **not** do. Sequence:
>   5 → (7, 8 can parallel) → 10 → then 1/2/9 collapse into "regenerate."

---

# Remaining work (Prompts 11–14)

All six drifts are fixed (Prompts 1–9 done). These four close out the
*prevention + verification + hygiene* backlog. Same stance as above: each
is a hypothesis — verify against live code, challenge it, stop and report
if it's wrong.

## Prompt 11 — Live cross-host round-trip (the one thing no sandbox could do) — HIGHEST VALUE

> Cross-host correctness is "equal by construction" but **never confirmed
> against a real IFC export**. Two host-side derivations of the cross-host
> key are assumed equal to what the IFC file actually carries:
> - **Revit:** `IFC_GLOBAL_ID_TXT` (snapshot of `IfcGloballyUniqueId` from
>   `StabilizeIfcGuidsCommand`) == the `GlobalId` in the exported IFC.
> - **ArchiCAD engine:** `ifcopenshell.guid.compress(elementGUID)` == the
>   `GlobalId` ArchiCAD writes to its IFC export.
>
> Run **one** live integration test that validates both (needs a real
> Revit + ArchiCAD + a running Planscape server + Bonsai — out of reach in
> any sandbox; this is a human/lab task):
> 1. Model the *same* physical elements in Revit and in ArchiCAD.
> 2. Revit: run **Stabilize IFC GUIDs**, sync to the server (TagSync), then
>    export IFC. ArchiCAD: sync via the bridge (engine path), export IFC.
> 3. Open both IFCs in Bonsai; run the STING sync op (`/ifc/data`).
> 4. For a sample of elements, call
>    `GET /api/projects/{id}/ifc/mappings?ifcGuid=<GlobalId>` and confirm
>    the row set includes **all** hosts that contain that element (revit +
>    archicad + blender), i.e. the GlobalIds joined.
> 5. Cross-check: the `IFC_GLOBAL_ID_TXT` Revit *sent* == the `GlobalId` in
>    Revit's *exported* IFC for the same element (this is the snapshot-vs-
>    export equality); same for ArchiCAD's `compress(GUID)` vs its export.
>
> **Acceptance:** a documented test run (or an automated integration test
> if the lab has the hosts) showing `/ifc/mappings` resolves cross-host for
> stabilised elements, and the two derivation equalities hold on a real
> export. Capture any element where they *don't* match — that's the real
> drift the construction proof can't see.
>
> **Verify / challenge before you run:**
> - This is a **test/verification task, not a code change.** If it passes,
>   the deliverable is the evidence; if it fails, file the mismatch as a new
>   drift with the element + both GUID values — do not patch blindly.
> - Confirm the Revit IFC export setting actually uses the stabilised
>   `IfcGloballyUniqueId` (export options can re-map GUIDs). If export
>   re-derives, the snapshot equality breaks and Prompt 7's assumption needs
>   revisiting — this test is exactly how you'd find that.
> - Confirm ArchiCAD's `compress(GUID)` matches its export across element
>   types (some categories may derive GUIDs differently).

## Prompt 12 — Surface "Stabilize IFC GUIDs before export" in the workflow

> Prompt 7's correctness depends on a monitored operational condition:
> `IFC_GLOBAL_ID_TXT` is a *snapshot* and only equals the exported file's
> GlobalId while the model stays stable through export. Today this is
> documented in `StabilizeIfcGuidsCommand`'s comment and detected by
> `IfcAlignmentValidator` (`GLOBALID_DRIFT >5%`), but it is not *surfaced*
> in the user's normal flow. Make the prerequisite visible:
> 1. In the BCC / IFC-push and IFC-export entry points, if any synced
>    element lacks `IFC_GLOBAL_ID_TXT` (or it post-dates the last geometry
>    change), prompt "Run Stabilize IFC GUIDs first" before proceeding
>    (non-blocking warning, with a one-click run).
> 2. Surface the server's `GLOBALID_DRIFT` result back in the plugin/BCC
>    (it's computed server-side by `IfcAlignmentValidator` — show it where
>    the user pushes), so drift is visible, not buried in a validator log.
>
> **Acceptance:** a user pushing/exporting an unstabilised or drifted model
> sees the prompt; a clean model doesn't. No change to the key logic from
> Prompt 7 — this is UX surfacing only.
>
> **Verify / challenge before you build:**
> - Check whether a prompt/gate already exists (the BCC tooltip from
>   Prompt 7 may be enough for some flows). Don't add a second nag if one
>   covers it.
> - "post-dates the last geometry change" needs a cheap staleness signal —
>   check if `STING_STALE_BOOL` or the existing stale-marker IUpdater
>   already provides it before inventing one.

## Prompt 13 — Prompt 10, scoped to Option 1 (the chosen guardrail)

> This is Prompt 10 narrowed to the maintainer's chosen Option 1 (session
> 13). Do **not** do the full codegen pipeline. Concretely:
> 1. Add explicit response DTOs (or `[ProducesResponseType(typeof(XDto),200)]`)
>    to ONLY the client-consumed endpoints where drift actually occurred:
>    `tagsync/elements/search`, healthcare `dashboard`, penetrations
>    `dashboard`, compliance snapshot, transmittals list, `/ifc/data` +
>    `/ifc/mappings`. (Meetings is lowest-priority — essentially clean;
>    include only if cheap.)
> 2. Declare a real `ValidationErrors` DTO (`[{ code, message, severity }]`)
>    and use it where `TaggedElement.ValidationErrors` is read/written, so
>    the last unschematized blob becomes part of the contract.
> 3. Add a CI gate (hook into the existing `ifc-substrate.yml` drift-gate
>    pattern + `planscape-server.yml`) that runs **server build + the
>    existing DTO-conformance tests** (the Prompt-1 Python `_element_to_wire`
>    test, the Prompt-2 mobile `TaggedElement` conformance test). Keep the
>    Prompt-2 conformance-test pattern; do **not** add `openapi-typescript`
>    or regenerate the TS types (that's Option 2).
>
> **Acceptance:** the ~7 drift-prone endpoints return typed DTOs; a server
> DTO rename breaks the conformance tests in CI; `ValidationErrors` has a
> declared shape; the gate is green on the current tree.
>
> **Verify / challenge before you build:**
> - The CI gate must be **server-only** (server build + conformance tests).
>   Do **not** wire in a mobile `tsc` gate or a test-suite gate — both are
>   red on pre-existing baselines (Prompt 14) and would make CI permanently
>   fail. Add those to the gate only after Prompt 14 clears them.
> - Re-confirm which endpoints the clients actually consume before adding
>   DTOs — the list above is from the audit; the live code may differ.
> - Adding a response DTO must not change the wire shape (it should encode
>   the *current* JSON exactly). If the current anonymous object and your
>   DTO disagree, the DTO is wrong — match the wire, don't "fix" it here.

## Prompt 14 — Hygiene backlog (four independent, pickable tasks)

> Four unrelated clean-ups, each self-contained — do any subset.
> 1. **Mobile `tsc` baseline** — `Planscape/app/` carries ~111 pre-existing
>    `tsc` errors (WIP tree). Drive to 0 so `tsc --noEmit` can become a CI
>    gate. Pure type hygiene; touch only what the compiler flags; don't
>    change runtime behaviour.
> 2. **Test project baseline** — `Planscape.Server/tests/Planscape.Tests`
>    has ~7 pre-existing errors (duplicate `WebApplicationFactory`
>    definitions + Program accessibility). Resolve so the test project
>    builds and the suite runs. (Likely a dedupe like Prompt 5's, in the
>    test project.)
> 3. **EF-migration backlog** — now generatable (the API builds since
>    Prompt 5). Run `dotnet ef migrations add` for: the restored Photo
>    DbSets (Prompt 5), `ArchiCADEventLogPersistence` (session 5), and any
>    cross-host column not yet migrated. One migration per logical change;
>    review the generated SQL before committing.
> 4. **`TaggedElement.IfcGlobalId` column** — retire the `UniqueId`
>    overload (it means Revit-UniqueId for Revit, IFC-GlobalId for
>    non-Revit). Add an explicit `IfcGlobalId` column + migration +
>    backfill + dual-write both ingest paths + index. Optional — cross-host
>    resolution already routes through `ExternalElementMapping`, so this is
>    cleanliness, not correctness.
>
> **Acceptance (per task):** (1) mobile `tsc` clean; (2) test project builds
> + suite runs; (3) migrations generated, reviewed, applied to a scratch DB;
> (4) `IfcGlobalId` column dual-written and indexed, `UniqueId` no longer
> overloaded for non-Revit hosts.
>
> **Verify / challenge before you build:**
> - Tasks 1 and 2 unblock the *stronger* CI gates (mobile `tsc`, test
>   suite) — do them before extending Prompt 13's gate, not after.
> - For task 4, this is the one that touches the shipped schema and both
>   ingest paths — highest risk in this list. Confirm it's wanted (it's
>   optional) and sequence it last.
