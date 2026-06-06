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

### Rounds

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
