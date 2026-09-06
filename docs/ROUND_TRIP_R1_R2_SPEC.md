# Implementation Spec — R1 (identity survives the loop) + R2 (both return legs)

Closes the two architectural gaps from the **ArchiCAD → Planscape → Revit → ArchiCAD**
round-trip review (`docs/ROADMAP.md` → "round-trip cross-tool gaps"). R3 (compliance on the
`/ifc/data` door) already shipped; this spec covers the two gaps that actually make the loop a loop.

> **Goal:** an element tagged in ArchiCAD, edited in Revit, is re-found and updated back in
> ArchiCAD — and every element is counted exactly once. Today the loop is two disjoint half-loops
> that share a database but not an identity, a merge policy, or a return path.

File/line references were verified against `main` at the time of writing; treat line numbers as
approximate (cite symbols, not lines, when implementing).

---

## 0. The one lever

The wire contract **already carries the right key**: `TagElementDto.IfcGlobalId`
(`Planscape.Server/src/Planscape.Core/DTOs/SyncDtos.cs`) is documented verbatim as *"the canonical
cross-host key, equal to the IfcGlobalId Bonsai/ArchiCAD send for the same element."* `ExternalElementMapping`
already stores it, keyed `(IfcGlobalId, Host, HostDocumentGuid)`, populated by **all three** ingest doors.

What's missing is small and mechanical: the projection row (`TaggedElement`) neither **stores** nor
**keys on** that GlobalId, the changes feed **emits the wrong key**, Revit **re-mints** the GlobalId
instead of carrying the ArchiCAD one, and the two **return legs are never called**. R1/R2 wire the
existing pieces together; they are not a rebuild.

---

## 1. Current state (verified against `main`)

| Fact | Evidence |
|---|---|
| `TaggedElement` has **no** `IfcGlobalId` column — only `RevitElementId` (long), `UniqueId` (string, "Revit UniqueId"), `Source` (nullable). | `Planscape.Core/Entities/TaggedElement.cs:12,13,65` |
| Two partial-unique indexes → the same physical element becomes **two rows**: Revit keyed `(ProjectId, RevitElementId>0)`, IFC/ArchiCAD keyed `(ProjectId, UniqueId=IfcGlobalId)`, `RevitElementId=0`. | `PlanscapeDbContext` element index config |
| `MapDtoToEntity` sets `entity.UniqueId = dto.UniqueId` and **drops `dto.IfcGlobalId`** (no column to hold it). | `TagSyncController.MapDtoToEntity` |
| The wire DTO **does** carry `IfcGlobalId` (nullable), documented as the canonical cross-host key. | `SyncDtos.cs:47` + doc comment `:39-47` |
| `ExternalElementMapping` (PK `IfcGlobalId+Host+HostDocumentGuid`) is populated by all doors but **read only** by `IfcController` + `TwinBindingService`. | `IdentityResolverService` consumers |
| The changes feed emits **`globalId = t.UniqueId`** — the Revit UniqueId for Revit rows, the IFC GlobalId for IFC rows → a Revit edit reaches a Python/ArchiCAD host with an **unmatchable key** and is dropped. | `ChangesController.cs:101`; `StingBridge/sync/ifc_reconcile.py` (`absent`) |
| **Planscape→Revit pull is unwired:** `GetElementsDeltaAsync` exists with **zero callers**; nothing stamps pulled tags onto native Revit elements. | `StingTools/BIMManager/PlanscapeServerClient.cs:512` |
| **Revit destroys ArchiCAD identity:** import stores the ArchiCAD GlobalId in `ARCHICAD_GUID`, but `StabilizeIfcGuids` fills `IFC_GLOBAL_ID_TXT` with a **fresh Revit-minted** GlobalId; the push reads the latter. | `ArchiCadIfcImportCommand.cs:2051`; `StabilizeIfcGuidsCommand.cs` |
| **→ArchiCAD return leg absent/stubbed:** the StingBridge live path's "Planscape wins" only pulls **timestamps** (`get_element_timestamps`), never values, so it writes back stale local data. The real writer — `ArchiCadHostAdapter.apply_remote_change` — **exists** but is wired only to the IFC path/tests. `StingTools.ArchiCAD` (C++) is a scaffold. | `StingBridge/sync/engine.py:186,423`; `StingBridge/archicad/host_adapter.py:157` |
| `stingtools_core` (reconcile/feed/grammar) is **Python-only**; Revit is a separate C# implementation on a different pull endpoint. | agent audit (R4) |

---

## 2. Design decisions (need owner sign-off before build)

| # | Decision | Recommendation |
|---|---|---|
| **D1** | Canonical cross-host key = the 22-char **IFC GlobalId**. | **Yes** — already the documented intent; every host can produce it. |
| **D2** | `TaggedElement` identity: add an `IfcGlobalId` column and make **`(ProjectId, IfcGlobalId)` the primary upsert key when present**, falling back to `RevitElementId`/`UniqueId` only when it's absent — collapsing the two-row problem — **vs.** keep two rows and merge via a DB view. | **Add the column + key on it.** A merge-view leaves compliance/pull double-counting; the column is additive and nullable. This is the single biggest change. |
| **D3** | On ArchiCAD→Revit import, carry the imported GlobalId into `IFC_GLOBAL_ID_TXT` (so a later push preserves it) and make `StabilizeIfcGuids` **never overwrite** an externally-authored GlobalId. | **Yes.** Re-minting is the root of the Revit-hop identity loss. |
| **D4** | Does Revit consume the Python `stingtools_core` (via a service/IPC), or stay C# with a golden cross-implementation parity test? | **Stay C# for now**; converge only the *feed key* + add a parity test. A shared reconcile engine is a later item (R4), not a blocker for R1/R2. |
| **D5** | Migration for existing duplicate rows in live projects. | **One-time reconciliation job**: backfill `IfcGlobalId` from `ExternalElementMapping`, then merge Revit+IFC row pairs. Idempotent, gated. |
| **D6** | First-pass scope for hop ④ (→ArchiCAD): finish the **StingBridge live write-back** vs. build the **C++ add-on**. | **StingBridge live path** — the adapter already exists; the C++ add-on is a separate, larger build tracked apart. |

---

## 3. R1 — Identity survives the loop

### R1.1 — Server: persist + key on IFC GlobalId  *(Infrastructure + API)*
- Add nullable `TaggedElement.IfcGlobalId` (string, ≤64) + a **filtered unique index** `(ProjectId, IfcGlobalId)` where `IfcGlobalId <> ''`. Additive migration (see D5).
- `TagSyncController.MapDtoToEntity`: copy `dto.IfcGlobalId` onto the entity.
- **Upsert precedence:** when `IfcGlobalId` is present, match/merge on `(ProjectId, IfcGlobalId)` **first**; fall back to `(ProjectId, RevitElementId)` / `(ProjectId, UniqueId)` only when it's absent. This is what collapses the duplicate rows — a Revit push and an ArchiCAD push for the same GlobalId now hit **one** row.
- Set `TaggedElement.Source` on **both** live doors (`/tagsync` → `"revit"`, `/ifc/data` → the request host) — this closes **R5** as a cheap side-effect and lets conflict logic know origin.
- `ComplianceSnapshotJob` / `ComputeComplianceAsync` become correct automatically once rows are deduped (no per-query dedup needed).

### R1.2 — Server: emit the real key on the changes feed  *(API)*
- `ChangesController` (`:101`): emit `globalId = t.IfcGlobalId ?? t.UniqueId`. With R1.1 populating `IfcGlobalId`, Revit-authored deltas now carry the **IFC GlobalId** Python hosts key on — Revit edits stop being dropped as `absent` (`StingBridge/sync/ifc_reconcile.py`).

### R1.3 — Revit: preserve the ArchiCAD-origin GlobalId  *(StingTools/Commands/Interop)*
- `ArchiCadIfcImportCommand`: in addition to `ARCHICAD_GUID`, write the imported IFC GlobalId into `IFC_GLOBAL_ID_TXT` on the created element.
- `StabilizeIfcGuidsCommand`: **guard** — if `IFC_GLOBAL_ID_TXT` is already non-empty (externally authored), do **not** overwrite it; only mint for Revit-native elements that lack one. This is the fix for "Revit re-mints a fresh GUID and destroys ArchiCAD identity."
- (No DTO change — `TagElementDto.IfcGlobalId` already ships; confirm the push populates it from `IFC_GLOBAL_ID_TXT`.)

### R1.4 — Bridge/core: key alignment + de-fork  *(StingBridge)*
- Once R1.2 lands, the Python `PullClient`/`ReconcileEngine` already key on `globalId` — add a test proving a Revit-authored delta now reconciles (was `absent`).
- Fix the **two-path fork**: the live path derives `ifc_global_id = compress(acGUID)` with `host_document_guid = null`; the IFC path uses the real GlobalId + `sha1(path)`. Make both derive the **same** GlobalId (verify `compress(acGUID)` equals the exported IFC GlobalId; if not, resolve via the export mapping) and use a **consistent `host_document_guid`** so one element doesn't fork into two `ExternalElementMapping` rows. (`StingBridge/sync/engine.py`, `StingBridge/watch/ifc_watcher.py`.)

### R1.5 — Server: consume the mapping downstream *(API — the payoff, phaseable)*
- `IssuesController` (and clash/BCF): resolve `LinkedElementIds` through `IdentityResolverService` so an issue linked to a Revit `ElementId`/GlobalId **surfaces in ArchiCAD** on the matching host element id. This is the user-visible payoff of R1; it can land after Phase A.

---

## 4. R2 — Wire the two return legs

### R2.1 — Planscape → Revit tag write-back  *(new StingTools command)*
- New command `Planscape_PullTags`: call `GetElementsDeltaAsync(projectId, lastPullWatermark)`; for each returned element, **re-find the native Revit element** by `IFC_GLOBAL_ID_TXT` reverse-lookup (fall back to `UniqueId`), then stamp the `ASS_*` token params + assembled tag.
- **Conflict policy:** skip when the local element's last edit is newer than the server row (staleness guard); log conflicts. Never blind-overwrite (contrast the existing `P6WritebackCommand`, which writes `overwrite:true` unconditionally — do not copy that).
- Persist a per-project **last-pull watermark** (Extensible Storage or a project setting) for resumable delta pulls.
- Acceptance: E2 below.

### R2.2 — → ArchiCAD write-back (close hop ④)  *(StingBridge)*
- Add a client method to fetch remote **token values** (not just timestamps) for a set of GlobalIds — the missing piece behind `get_element_timestamps`.
- Replace the live engine's "Planscape wins → writes local values" branch (`sync/engine.py:423`) with a call to the **existing** `ArchiCadHostAdapter.apply_remote_change` (`archicad/host_adapter.py:157`), which writes tokens into live ArchiCAD via the JSON API. The adapter is already correct and tested — it is simply not wired into the live path.
- IFC path: the reconciled values currently land in a `_sting.ifc` **side file** that ArchiCAD never re-opens. Route them through the same live JSON-API writer instead (or, minimally, document the re-import step). Prefer the live writer.
- `StingTools.ArchiCAD` C++ add-on stays **out of scope** for the first pass (scaffold; tracked separately).

### R2.3 — SEQ concurrency (server-authoritative)  *(ties to R4)*
- Revit's SEQ merge is `max-per-key` and its own comment admits it "cannot stop Revit and StingBridge minting the same number concurrently." Make the **server** the atomic authority: Revit reserves SEQ server-side (as StingBridge already does via `reserve_seq`) instead of merging max-per-key. Minimum viable: an atomic `POST /seq/reserve` both hosts call.

---

## 5. Phasing

| Phase | Items | Outcome |
|---|---|---|
| **A — identity** | R1.1, R1.2, R1.3, R1.4 + D5 migration | Identity survives the loop; compliance dedups; Revit edits reach Python hosts. **This alone converts two half-loops into one.** |
| **B — return legs** | R2.1, R2.2 | Changes flow *back* into Revit and live ArchiCAD. The loop closes. |
| **C — payoff + hardening** | R1.5 (mapping-aware issues/clash), R2.3 (SEQ), R6 (tombstones), R4 (shared reconcile) | Cross-host issues surface; no duplicate SEQ; deletions propagate. |

Rough effort: Phase A ≈ 3–5 days, Phase B ≈ 4–6 days, Phase C ≈ 1–2 weeks.

---

## 6. Acceptance tests (end-to-end)

- **E1 — identity:** tag element *X* in ArchiCAD → push → open the same model in Revit; *X* carries the same GlobalId in `IFC_GLOBAL_ID_TXT`. Edit *X*'s tag in Revit → push → the server holds **one** `TaggedElement` row for *X*, and `Project.TotalElements` counts *X* once.
- **E2 — Revit pull:** change *X*'s tag on the server → run `Planscape_PullTags` → *X*'s `ASS_*` params update; a newer local edit is **not** clobbered.
- **E3 — ArchiCAD write-back:** change *X* on the server → StingBridge `sync` → the live ArchiCAD element *X*'s STING props update (via `apply_remote_change`).
- **E4 — no double-count:** push the same model from Revit **and** ArchicAD → `Project.TotalElements` counts each element once (guards against R1 regression).
- **E5 — feed key:** a Revit-authored delta on the changes feed carries the IFC GlobalId and reconciles on a Python host (was previously dropped as `absent`).

---

## 7. Risks & rollback

- **Schema change** (new nullable column + filtered index + backfill/merge job) is additive and reversible; the merge step must be idempotent and gated behind D5.
- **Feed-key change** is low-risk: the only Revit pull consumer (`GetElementsDeltaAsync`) is unwired, and Python hosts already expect the GlobalId.
- **Never overwrite an externally-authored GlobalId** (R1.3 guard) — a bug here churns identity for the whole model; cover with a unit test.
- **Dedup migration** on live data: run read-only first (report duplicate pairs), then merge under a transaction with a backup.

---

## 8. Out of scope (tracked elsewhere)

- The `StingTools.ArchiCAD` C++ add-on (a separate native build).
- Extracting a shared C#/Python reconcile engine (R4) — this spec only converges the *feed key*.
- Geometry/unit normalization (R7) and tombstones (R6) — Phase C / separate.
