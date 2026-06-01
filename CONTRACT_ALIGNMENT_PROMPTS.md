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
> **Acceptance:** `tsc --noEmit` clean. The element-search screen shows
> populated discipline/system/tag fields (or, if no live server, a unit
> test of the mapper/interface against a captured server JSON sample). The
> `lvl` vs `level` ambiguity is resolved explicitly in code, not left to
> chance.
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
>   `Planscape/src`) to size approach (A) vs (B) honestly before choosing.
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
