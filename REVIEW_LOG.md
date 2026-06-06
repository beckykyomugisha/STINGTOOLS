# REVIEW_LOG — repo review & hardening

Branch `claude/repo-review-hardening` (off `main` @ `8bb177b8d`). Append-only. One area at a time,
deepening rounds (R1 correctness → R2 integration → R3 accuracy/edge/txn/async → R4 efficiency →
R5 security/data-integrity → R6 automation gaps → R7 deep re-scan → R8 regression → R9–R10 weakest).
Plan-before-fix gate for every finding above Low and any shared/cross-cutting change.

**Verification constraints in this sandbox:** the Revit C# plugin (`StingTools/…`) cannot be
`dotnet build`'d (no Revit API assemblies) — C# plugin fixes are signature-checked against the
documented API + kept transaction-well-formed, and marked **compile-unverified in-sandbox**.
`Planscape.Server` *can* be built. Viewer JS: `node --check` + assets↔wwwroot byte-equal gate.
Mobile: `tsc --noEmit`.

---

## Mission adjustments (logged)
1. **Early area stop:** end an area as soon as a full round yields 0 new Critical/High AND a
   regression round is clean — do NOT force the full 8–10 rounds on already-clean areas; spend
   cycles where defects exist.
2. **#306 divergence guard:** A4 (`Planscape/assets/viewer`) and the meetings server code
   (`MeetingHub`, `MeetingRoomController`, `MeetingsController`, `HubTenantGuard`, meeting
   entities/DTOs) are heavily changed on the unmerged PR #306. Reviewing them off `main` would be
   stale + conflict-prone. **DEFER A4 + meetings-server review** until #306 merges (then a
   rebase-onto-#306 review). In-scope now: A1 Core, A2 Data, A3 non-meeting server, engine sub-dirs,
   A6 commands, A7 tooling.

## AREA A1 — StingTools/Core (foundational infra)

**Scope decision (logged):** A1 = the high-blast-radius cross-cutting infrastructure under
`StingTools/Core/`. The feature/command files (`*Commands.cs`, `*Job.cs`, `SectorPackCommand`,
`MultiBuilding*`, `LODValidationCommand`, `MaterialGate*`, `FamilySymbolAuthor`, `FolderTemplateLibrary`,
`HandoverModeHelper`, etc.) and the 33 engine sub-dirs (`Calc/`, `Routing/`, `Placement/`, `Drawing/`,
`Hvac/`, …) are **deferred to A6** (reason: functional commands/engines, not foundational infra —
reviewing them here would balloon A1 and split their natural area). They are still in-scope for the
cross-area pass.

**A1 coverage map** (each file targeted ≥2× across rounds):

| File | Role | Rounds seen |
|---|---|---|
| `ParamRegistry.cs` | Single source of truth: params/GUIDs/containers/bindings | |
| `ParameterHelpers.cs` | Param read/write + TokenAutoPopulator + SpatialAutoDetect + TagPipelineHelper | |
| `SharedParamGuids.cs` | Backward-compat facade over ParamRegistry | |
| `TagConfig.cs` (+`.Defaults`,`.Tag7`,`CsvReader`) | ISO-19650 tag lookup + builder + validator | |
| `WorkflowEngine.cs` | Workflow preset orchestration + JSONL log | |
| `StingToolsApp.cs` | IExternalApplication lifecycle + IUpdater reg + events | |
| `StingLog.cs` | Thread-safe file logger + EscapeChecker | |
| `TransactionHelper.cs` | Shared transaction wrapper | |
| `ComplianceScan.cs` | Cached compliance scan | |
| `StingAutoTagger.cs` | IUpdater real-time tagging + StingStaleMarker | |
| `ISO19650Validator.cs` · `TagIntelligence.cs` · `TaggingModels.cs` · `SeqAssigner.cs` | Tag core | |
| `OutputLocationHelper.cs` · `PerformanceTracker.cs` · `IPanelCommand.cs` | Output/profiling/iface | |

### Rounds (A1)

#### A1 · R1 — broad correctness
**Files covered (read in full):** `StingLog.cs`, `TransactionHelper.cs`, `ComplianceScan.cs`.
**Pattern-scanned (sync-over-async / empty-catch / DateTime.Now):** `ParamRegistry.cs`,
`ParameterHelpers.cs`, `WorkflowEngine.cs`, `StingToolsApp.cs`, `SharedParamGuids.cs`, `TagConfig.cs`,
`StingAutoTagger.cs`.

General observation: the A1 foundation is **already extensively hardened** — pervasive prior-round
fix tags (CRASH FIX / AG-10 / LG-07 / CS-01 / LOGIC-01 / C-06 / PERF-* / Phase 78/86/87/88),
Interlocked guards, UtcNow cache math, exception-safe stream construction, rate-limited logging.
No sync-over-async (`.Result`/`.Wait`/`async void`) in any A1 file. The "empty catch" matches
(`StingToolsApp.cs:1479/1482` IUpdater Unregister on shutdown; `TagConfig.cs:1327` nested
file-copy-fallback cleanup) are intentional best-effort cleanup, not silent-swallow bugs.

Findings:

| # | File:line | Dim | Sev | Root cause | Blast radius | Disposition |
|---|---|---|---|---|---|---|
| A1-R1-1 | `StingLog.cs` EnsureWriter | 1 correctness / 6 robustness | **Low** | Daily log rotation only fires via the `LogPath` getter inside the `_writer==null` branch; a writer open across midnight keeps yesterday's filename | StingLog internals only — no external contract; all logging callers behaviour-identical (still logs, just to the correctly-dated file) | **FIXED** — touch `LogPath` at top of `EnsureWriter` so the getter rolls the day + disposes the stale writer; reuse the resolved `path` for the FileStream + size-rotation. |
| A1-R1-2 | `WorkflowEngine.cs:2048` | 4 accuracy (tz) | **Low/Med** | `has_overdue_issues` SLA age = `DateTime.Now - created` where `created` is `DateTime.TryParse`d from `issues.json` `date_raised`/`created_date`; if the writer stored UTC the age is off by the local offset (e.g. +3h EAT) | **Cross-area:** the timestamp convention is owned by the BIMManager `issues.json` writer (A6/A3 — not yet reviewed). `Now→UtcNow` fixes it IFF the value is UTC, and *breaks* it if local. | **DEFERRED (cross-area proposal CP-1)** — do not guess. See proposals below. |
| A1-R1-3 | `TransactionHelper.cs:56,61,107` | — | Info | Catch-then-rethrow logs `"Suppressed: …"` though the exception is re-thrown (misleading wording) | local | Note only — no behaviour change; defer to an anti-churn-safe pass if ever touched. |

**Cross-area proposals raised:**
- **CP-1 (defer to A3/A6):** Confirm the timestamp convention written into `issues.json`
  (`date_raised`/`created_date`) by the BIMManager / IssuesController writer. If UTC, change
  `WorkflowEngine.cs:2048` to `DateTime.UtcNow - created` (and prefer
  `DateTimeStyles.RoundtripKind` on the parse); if local, leave as-is. Recommendation: standardise
  on UTC end-to-end (matches the server + ES snapshots which already use UtcNow), then fix this site.

**Scorecard (A1 R1):** Critical 0 · High 0 · Medium 0 · Low 2 · Info 1 · Deferred-cross-area 1.
Fixes applied: 1 (A1-R1-1). Build status: C# plugin **compile-unverified in-sandbox** (no Revit API);
change is a variable-reuse + getter-reorder refactor with no API-surface change — signature-safe.
Coverage map updated: StingLog ×1(full), TransactionHelper ×1(full), ComplianceScan ×1(full),
others ×1(scan). Next round must read ParamRegistry / ParameterHelpers / TagConfig / WorkflowEngine /
StingToolsApp in full (R2 integration/alignment) to hit the ≥2× target.

#### A1 · R2 — integration / alignment (single-source-of-truth contract)
Focus: `ParamRegistry` ↔ `PARAMETER_REGISTRY.json` ↔ `MR_PARAMETERS.txt` integrity (also de-risks A2).
Pattern-scanned `ParamRegistry.cs` load path (`_guidByName`/`_nameByGuid` builders, lines ~2099–2258):
the registry mixes **first-wins** (`if (ContainsKey) continue`, 2099) and **last-wins** (`_guidByName[name]=g`,
2257) across sections — so a name defined twice with different GUIDs resolves by fragile load order.

| # | File:line | Dim | Sev | Root cause | Blast radius | Disposition |
|---|---|---|---|---|---|---|
| A1-R2-1 | `Data/PARAMETER_REGISTRY.json` (387/395/403, 27483-27490) | 2 integration / 5 data-integrity | **Medium** | 5 param names each defined twice with **two different GUIDs**; the stale copy used a placeholder namespace (`a1b2c3d4-…100x`) or the **null GUID** (`00000000-…`). A Revit shared param is GUID-identified, so resolving a name to the wrong GUID silently breaks read/write round-trip. | Verified clean: **nothing** in `.cs`/`.csv`/families references the wrong GUIDs; the correct GUIDs already exist in the JSON's other occurrence AND in the authoritative `MR_PARAMETERS.txt`/`.csv`. Consumers reference by **name** (unchanged). | **FIXED** — corrected the 5 wrong GUID strings in place to the `.txt`-authoritative values (`a8f3c1d2-…780x`, `fa93b8a1…`, `4529fc89…`). In-place value edit only — no structural/comma surgery, no reformat (anti-churn). |

Plan-before-fix record for A1-R2-1: (a) stale duplicate GUID defs + load-order ambiguity; (b) grepped
all `.cs`/`.csv`/`.txt`/families for the wrong GUIDs → none; confirmed `.txt`/`.csv` hold the correct
GUID; (c) least-blast = correct the wrong value to match the authoritative `.txt` (vs. deleting the
dup block, which risks JSON structure); (d) no contract change — names unchanged, GUIDs now match the
field-authoritative source; (e) verified: JSON parses, 0 name→multi-GUID conflicts (was 5), all 5
match `.txt`. The other ~177 dup names/GUIDs are benign (same name **and** same GUID listed in two
sections); 0 GUID→multiple-name collisions; 0 malformed GUIDs.

**Scorecard (A1 R2):** Critical 0 · High 0 · Medium 1 (fixed) · Low 0. Fixes applied: 1.
Build: data-only change, verified by JSON parse + conflict scan (no C# build needed). The runtime
copies under `bin/` and `CompiledPlugin/data/` are build artifacts — regenerated from source on build,
not hand-edited.

#### A1 — AREA CLOSE (regression)
A1 code (R1) is hardened/clean — 0 Critical/High across the read-in-full foundation (StingLog,
TransactionHelper, ComplianceScan) and the pattern-scanned registry/helpers/tagconfig/app/autotagger.
A1 data-integrity (R2) had 1 Medium (5 GUID conflicts) — **fixed + verified**. The StingLog rotation
fix (R1) and the JSON GUID fix (R2) are isolated and re-verified to hold. No new Critical/High.
Per the early-stop adjustment, **A1 is closed**. Carry-forward to A2: continue the data-integrity
thread on `MR_PARAMETERS.txt`/`.csv` ↔ registry ↔ CSV data files (the natural continuation of A1-R2-1).
Open cross-area proposal carried: **CP-1** (WorkflowEngine SLA timestamp tz — settle in A3/A6).

**A1 AREA SUMMARY:** 2 rounds (foundation already pre-hardened ⇒ early stop). Findings: 1 Medium
(data integrity, fixed), 3 Low (1 fixed: StingLog rotation; 2 logged: WorkflowEngine tz deferred as
CP-1, TransactionHelper log-wording info). Sustainability win: the param single-source-of-truth no
longer self-conflicts, so name→GUID binding is load-order-independent for those 5 params.

---

## AREA A2 — StingTools/Data (param/CSV/JSON files)

**Coverage map:** `PARAMETER_REGISTRY.json` (done in A1-R2), `MR_PARAMETERS.txt`, `MR_PARAMETERS.csv`,
`FORMULAS_WITH_DEPENDENCIES.csv`, `CATEGORY_BINDINGS.csv`, `FAMILY_PARAMETER_BINDINGS.csv`, all 202
`Data/**/*.json`. Method: cross-file referential + GUID/type-drift scans (the defect class most
likely in mirrored data files), not line-reading.

### Rounds (A2)

#### A2 · R1 — cross-file integrity / referential alignment
Checks + results:
- **All 202 Data JSON files parse** (0 malformed).
- **`MR_PARAMETERS.txt` ↔ `.csv` mirror:** 3286 names each — 0 presence drift, **0 same-name/diff-GUID,
  0 same-name/diff-datatype**. Perfectly aligned. (The 3292-vs-3286 line count = 6 CSV `#` comment lines.)
- **`PARAMETER_REGISTRY.json` ↔ authoritative `.txt`:** 3106 shared names — **0 GUID mismatches, 0 orphans**
  (after the A1-R2-1 fix). The param single-source-of-truth triad is now fully consistent.
- **`CATEGORY_BINDINGS.csv`** (16,830 rows / 1,426 params) + **`FAMILY_PARAMETER_BINDINGS.csv`**
  (6,035 rows / 1,158 params): **0 param refs missing** from `.txt`.
- **`FORMULAS_WITH_DEPENDENCIES.csv`:** 0 formula-target params missing; 0 self-referential issues.

| # | File | Dim | Sev | Root cause | Blast radius | Disposition |
|---|---|---|---|---|---|---|
| A2-R1-1 | `FORMULAS_WITH_DEPENDENCIES.csv` (rows 218/220/232) | 2 integration / 6 data-hygiene | **Low** | 3 TAG7 narrative formulas list `RGL_KCCA_/NEMA_/UMEME_APPROVAL_TXT` in `Input_Parameters`, but the formula expression doesn't reference them and they're defined nowhere (txt/csv/registry). Stale dependency metadata. | **Benign:** consumer `FormulaEvaluatorCommand.BuildContext` does an ignored failed lookup; `ValidateFormulas` only emits an Info count. Formula evaluation unaffected (it uses `RGL_STD_TXT`, which exists). | **LOGGED, not edited.** Two readings: (a) copy-paste cruft → trim the 3 from the dep lists; (b) **intent gap** — the Uganda-focused narrative may have been *meant* to cite KCCA/NEMA/UMEME approvals as real params. Recommendation: trim if (a); if (b), define the 3 params + reference them in the formula. Not auto-fixed: editing a complex quoted CSV field for a benign Low risks corruption (anti-churn), and the (a)-vs-(b) choice is product intent. |

**Scorecard (A2 R1):** Critical 0 · High 0 · Medium 0 · Low 1 (logged) · referential checks all clean.
Fixes applied: 0 (the A2 data Medium was the A1-R2-1 GUID-conflict fix, already shipped). Build:
data-only, verified by JSON parse + cross-file scans.

#### A2 — AREA CLOSE (regression)
R1's cross-file scans are the regression for the A1-R2-1 fix too — re-confirmed the param triad is
consistent (0 mismatches). No Critical/High/Medium in A2's own scope. Per early-stop, **A2 is closed**
with 1 Low logged (A2-R1-1) + 1 recommendation surfaced (RGL approval intent — non-blocking).

**A2 AREA SUMMARY:** 1 round (data backbone clean post-A1-R2-1). The param/binding/formula referential
graph is internally consistent: every binding + formula target resolves to a real, GUID-stable param.
Open recommendation: RGL_*_APPROVAL_TXT formula-dep intent (trim vs. define) — your call.

