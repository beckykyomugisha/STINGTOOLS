# Document Manager — accuracy / consistency / integration gaps (runner)

**Target file:** [`StingTools/UI/DocumentManagementDialog.cs`](../StingTools/UI/DocumentManagementDialog.cs) (6,661 lines)
**Baseline:** `main` @ `0bbcb19e5`. Re-derive every line number before editing — this file moves.
**Status of the surface:** the delete/restore and silent-failure defects were repaired and merged in
PRs #662 / #696 / #701 and verified in Revit. Everything below is *different* work: the dialog
disagrees with registries that already exist elsewhere in the same assembly.

Read this whole file before touching code. Each work package is independently shippable.

---

## 0. Orientation

The Document Management Center is a code-built WPF dialog (no XAML) opened from the BIM tab. It has
9 tabs in a bottom action bar, ~146 buttons: ~37 inline handlers (`MakeActBtn`), ~86 that close the
dialog and dispatch a command tag (`MakeDispatchBtn`), 23 context-menu items. It reads 14 loaders
into one `ObservableCollection<DocItemVM>` filtered by a `ListCollectionView`.

Store access is already correct — every store resolves through `CoordStores` / `IssueStore`; do not
reintroduce `Path.Combine(dir, "<name>.json")`. `GetBimManagerDir` was deleted deliberately.

### Hard constraints

- `dotnet build StingTools/StingTools.csproj -c Release` must stay **0 errors / 0 warnings**.
- `tools/check_path_discipline.ps1` must stay clean (Tier 1 and Tier 2 both zero).
- Do not change behaviour of the BIM Coordination Center (`UI/BIMCoordinationCenter.cs`) — several
  registries below are shared with it. Read-only reuse only.
- Follow CLAUDE.md conventions: `StingLog` not silent catches, `TaskDialog`/`MessageBox` for user
  messages, transactions prefixed "STING".
- **Every user-visible failure path must say what happened.** The whole recent repair of this file
  was about the UI asserting things that were not true; do not add a silent `return;`.

### Do NOT do this

**Do not merge `document_register.json` and `deliverables.json`.** Two registers coexist by
design; [`Core/DocumentRegister.cs`](../StingTools/Core/DocumentRegister.cs) documents why the
destructive merge is deferred (different schemas, needs a dry-run migration command plus in-Revit
verification) and provides `BuildUnified` as a read-only unifier. It is tracked in ROADMAP. Leave it.

---

## WP1 — Suitability vocabulary is wrong and incomplete  *(P0 — wrong data reaches issued documents)*

### Evidence

`DocumentManagementDialog.cs:2017-2018` (Quick Transmittal suitability combo) hardcodes:

```csharp
foreach (string s in new[] { "S0 — WIP", "S1 — Coordination", "S2 — Information", "S3 — Review & Comment",
    "S4 — Stage Approval", "S5 — Costing", "S6 — Contractor Design", "S7 — Manufacture" })
```

The canonical table is [`Core/Drawing/Iso19650Vocabulary.cs:142`](../StingTools/Core/Drawing/Iso19650Vocabulary.cs)
`SuitabilityLabels` (public, 22 entries):

| Code | Dialog claims | Canonical (ISO 19650) |
|---|---|---|
| S5 | Costing | Suitable for manufacture / procurement |
| S6 | Contractor Design | Suitable for PIM authorization |
| S7 | Manufacture | Suitable for AIM authorization |

The dialog's S5/S6/S7 are pre-2018 BS 1192 meanings. A user selecting "S6 — Contractor Design"
stamps `S6` onto a transmittal, where `S6` means PIM authorization. **This is incorrect metadata
leaving the office on an issued document.**

Separately the dialog exposes only S0–S7. The canonical set also carries A1–A5 (authorized),
B1–B6 (partial sign-off), CR, AB, AR — so there is currently **no way to issue a PUBLISHED
transmittal with a correct authorization code**.

Note the same file already uses the canonical source in the context menu
(`BIMManagerEngine.SuitabilityCodes`, which is just `Iso19650Vocabulary.SuitabilityLabels`), so the
dialog contradicts itself.

### Do

1. Replace the hardcoded array at `:2017-2018` with `Iso19650Vocabulary.SuitabilityLabels`, ordered
   S → A → B → CR/AB/AR. The values are already formatted `"S0 — Initial WIP / draft"`.
2. The existing parse is `suitCombo.SelectedItem.ToString().Split(' ')[0]` — verify it still yields
   the bare code for every entry (it does for `"A1 — …"`), or switch to a keyed item so parsing is
   not positional.
3. Audit for any other hardcoded suitability list in this file and route it the same way.

### Acceptance

- Every code in `SuitabilityLabels` is selectable in Quick Transmittal.
- The stored `suitability` field equals the bare code (`S4`, `A2`, …).
- No suitability string literal remains in `DocumentManagementDialog.cs`.

---

## WP2 — Two CDE state machines disagree  *(P0)*

### Evidence

`DocumentManagementDialog.cs:4845`:

```csharp
private static readonly Dictionary<string, string[]> CDETransitions = new(...)
{ [""] = {"WIP"}, ["WIP"] = {"SHARED"}, ["SHARED"] = {"WIP","PUBLISHED"},
  ["PUBLISHED"] = {"ARCHIVE"}, ["ARCHIVE"] = {} };
```

`BIMManager/BIMManagerCommands.cs:82` `CDEStateTransitions` (Phase 40, ISO 19650-2), 7 states:

```
WIP        → SHARED, WITHDRAWN
SHARED     → PUBLISHED, WIP, SUPERSEDED, WITHDRAWN
PUBLISHED  → ARCHIVE, SUPERSEDED, WITHDRAWN
ARCHIVE    → OBSOLETE
SUPERSEDED → ARCHIVE, OBSOLETE
WITHDRAWN  → OBSOLETE
OBSOLETE   → (terminal)
```

Consequences: the Document Manager **cannot withdraw or supersede** a document — the two operations
ISO cares most about after issue — and it enforces different rules than the Coordination Center on
the same files. There is also `ValidateCDETransition(current, next)` at `BIMManagerCommands.cs:95` (uses the table at `:82`)
returning null-or-error, already written and unused here.

### Do

1. Delete `CDETransitions` from the dialog. Use `BIMManagerEngine.CDEStateTransitions` and
   `ValidateCDETransition` (both `internal`, same assembly — accessible).
2. `BulkUpdateCDE` currently maps the chosen state to a target folder with a 4-arm switch. Decide
   and implement where SUPERSEDED / WITHDRAWN / OBSOLETE files go — most likely `ARCHIVE` on disk
   with the CDE state recorded in the register rather than a new folder per state. **State this
   decision in a comment**; do not invent three new top-level folders without checking
   `ProjectSetup` folder definitions first.
3. Keep the existing mixed-state warning and the ISO suitability auto-map, extending the map for the
   new states (SUPERSEDED → `AB`, WITHDRAWN → `AB`, OBSOLETE → `AR` — confirm against
   `SuitabilityLabels` before hardcoding).

### Acceptance

- Withdraw and supersede are reachable from both Update CDE and the right-click Set CDE Status menu.
- An illegal transition is refused with the engine's message, not silently allowed.
- The dialog and the BCC accept exactly the same transitions for the same document.

---

## WP3 — Vocabularies hardcoded instead of bound to registries  *(P1 — has a real victim today)*

### Evidence

| Concept | Hardcoded at | Canonical source | Dialog refs |
|---|---|---|---|
| Discipline | `:785` filter bar, `:2836` Add-Doc combo | `TagConfig.DiscMap` (loaded from `project_config.json`) | **0** |
| Issue status | `:848` filter bar | `IssueStatusNormalizer` (`Canonical`, `IsOpen`, `Normalize`) | **0** |
| Priority | `:830`, `:2218`, `:6338` | none yet — see below | — |
| Note category | `:5116` | none yet | — |

Disciplines are hardcoded as `M, E, P, A, S, FP, LV, G, Z`. Per CLAUDE.md:729 the Healthcare pack
adds **H (Healthcare), MG (Medical Gas), RP (Radiation Protection)**. So a healthcare project
**cannot filter its own documents by its own disciplines**, and any project that customises
`DiscMap` via `project_config.json` gets the configurability silently ignored at the UI.

### Do

1. Discipline: build both lists from `TagConfig.DiscMap.Keys` (retain the blank "any" entry).
2. Issue status: build the filter list from the `IssueStatusKind` enum via
   `IssueStatusNormalizer.Canonical(kind)`, so spellings match what `IssueStore` persists.
3. Priority and note category have **no** canonical registry. Either add small static tables next to
   the existing vocabularies (preferred — one place, reusable by the BCC) or leave them hardcoded and
   note it. Do not invent a JSON config file for four values without asking.
4. De-duplicate the three priority lists into one member regardless.

### Acceptance

- Opening the dialog on a project whose `DiscMap` includes `H`/`MG`/`RP` shows those in both
  discipline lists.
- No discipline or status string literal remains in `DocumentManagementDialog.cs`.

---

## WP4 — SLA check bypasses the status normalizer  *(P1 — produces wrong numbers)*

### Evidence

`RunSLACheck` at `DocumentManagementDialog.cs:3777`:

```csharp
string status = issue["status"]?.ToString() ?? "";
if (status == "CLOSED" || status == "RESOLVED") continue;
```

Raw string comparison against a field that `IssueSchema` guarantees only through
`IssueStatusNormalizer.Canonical`. Any other spelling (`Closed`, `VOID`, a server-sourced variant)
is counted as **open**, inflating overdue and escalation counts on a report people act on.

`Core/IssueEscalationEngine.cs:75` exists and the dialog references it **0 times**, so escalation is
whatever this loop re-implements.

### Do

1. Replace the comparison with `IssueStatusNormalizer.IsOpen(status)`.
2. Read the issue rows through `IssueStore.Load(doc)` rather than `JArray.Parse(File.ReadAllText(...))`.
3. Evaluate whether the hand-rolled escalation tiers duplicate `IssueEscalationEngine`. If they do,
   delegate; if the engine is genuinely a different feature (batch auto-raise vs report), say so in a
   comment and leave both.

### Acceptance

- An issue stored as `Closed`/`VOID` is not reported overdue.
- SLA totals match `IssueStore.Open(doc).Count` for the same filter.

---

## WP5 — The lifecycle is not reachable from the Document Manager  *(P1 — wiring, not building)*

### Evidence

All eight deliverable lifecycle commands resolve in `StingCommandHandler` and appear on **none** of
the nine tabs:

```
IssueDeliverable  ReIssueDeliverable  PublishDeliverable  CancelDeliverable
SupersedeDeliverable  ReplaceDeliverable  BulkIssueDeliverables  CreateTransmittalOrchestrated
        dialog: 0 occurrences        handler: 1 case each
```

The dialog reads deliverable rows through `DocumentRegister.BuildUnified` (display-only) but cannot
act on them. To issue or supersede, a user must close the Document *Management* Center and use the
dock panel.

### Do

Add a `LIFECYCLE` section to the **DOCS / CDE** tab using `MakeDispatchBtn` with those exact tags —
the same mechanism the other 86 dispatch buttons use. Add matching entries to `GetButtonTooltip`
(a missing key renders no tooltip; see the `NullIfBlank` helper).

Do **not** build new lifecycle logic. This is exposure only.

### Acceptance

- All eight commands are reachable from DOCS / CDE and dispatch correctly.
- `docs/guides/DOCUMENT_MANAGER_GUIDE.md` Part 4 updated — it is generated from source and currently
  says these are dock-panel only.

---

## WP6 — Lucene search built and unwired  *(P2)*

`Docs/Search/DocumentIndex.cs`, `SearchQueryBuilder.cs` (`SavedSearch`, `SavedSearchStore`) exist;
the dialog references them **0 times** and still filters with a free-text `Contains` box. This is
ROADMAP `TPL-FOLLOW-05` ("data layer ready, dialog still uses the legacy free-text box") and the
entry is still accurate.

The dialog already has `_activeFacets` and `_savedSearches` fields and a `_facetPillsPanel` — the UI
scaffolding was started. Finish it against `DocumentIndex.Search` and persist through
`SavedSearchStore`, or delete the dead fields. Decide, do not leave both.

---

## WP7 — No server integration for documents  *(P2 — needs a decision, not just code)*

The dialog contains **zero** sync/push references. `IssueStore` pushes issues to Planscape
(`PushCreateFireAndForget`, `ReconcileToServerAsync`); the document register, transmittals and
revisions never leave the machine. Inside one dialog, one entity type is server-backed and the rest
are local-only.

**Do not implement this without confirming the intended product behaviour.** Questions the owner
must answer first: is the register meant to be server-authoritative or local-first with push? What
happens on conflict? Does the existing `TagSyncController` / `DocumentsController` on the server
already define a contract to honour? Write findings into `docs/ROADMAP.md` and stop there unless
told otherwise.

---

## WP8 — Automation surface is thin  *(P3)*

Automation today: file watcher (`StartWatching`), auto-transmittal on CDE move, cloud mirror on
publish, notification queue. `WorkflowEngine` is referenced **0 times**, so no document workflow
(Weekly Data Drop, Doc Package) can be launched from the document surface, and nothing is scheduled.

Lowest priority and the most speculative. Propose before building.

---

## Verification protocol (every WP)

```bash
dotnet build StingTools/StingTools.csproj -c Release -clp:Summary     # must be 0/0
powershell -ExecutionPolicy Bypass -File tools/check_path_discipline.ps1
dotnet test StingTools.Boq.Tests --nologo                              # 196/196 expected
```

**Known pre-existing failures — not yours, do not "fix":** `StingTools.Tags.Tests` has 2 failing
CSI MasterFormat tests; `StingTools.Clash.Tests` does not compile (missing Revit API reference).
Confirm they are identical before and after your change.

Also re-run the dispatch crosscheck if you add buttons — every `MakeDispatchBtn` tag must resolve
against the `StingCommandHandler` switch or a `UI/Modules/*CommandModule.cs` registration:

```bash
grep -o 'MakeDispatchBtn("[^"]*", "[^"]*"' StingTools/UI/DocumentManagementDialog.cs | sed 's/.*", "//; s/"$//' | sort -u
```

### In-Revit verification

The vocabulary changes (WP1–WP3) are **not** provable by build alone — they are what the user sees
and stamps. Before claiming done:

1. Deploy. **Re-grep the addin path every time; it moves between worktrees:**
   `grep -h '<Assembly>' "$APPDATA/Autodesk/Revit/Addins/"20*/StingTools.addin`
2. Close Revit **and stop `Planscape.Companion.exe`** — it runs from the deploy folder and silently
   half-fails the copy. Verify the copied DLL by hash, not timestamp.
3. Quick Transmittal → confirm the full suitability list and that the stored code in
   `_data/coord/transmittals.json` is the bare code.
4. Update CDE → confirm withdraw/supersede appear and an illegal transition is refused.

---

## Commit / PR guidance

One PR per work package, or WP1+WP2+WP3 together as "bind the Document Manager to the canonical ISO
vocabularies" — they touch the same lines and share a rationale.

`main` moves fast (this branch fell 116 commits behind once). **Merge `origin/main` before opening
the PR and rebuild**, or CI will test a stale tree.

The PR body should state plainly which findings were verified in Revit and which are build-only.
