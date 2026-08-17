# RUNNER — STING Visibility Center, enhancement pass

The Visibility Center shipped and runs in Revit (PR #656, branch `claude/visibility-center`).
This pass fixes the gaps found by using it on a real model. **Read the whole file before
writing code.** Ground truth first — several of these facts were learned the hard way and
will cost you an afternoon if you rediscover them.

---

## 0. Ground truth — verify before you rely on any of it

| Fact | Where | Why it matters |
|---|---|---|
| `StingCommandHandler.RunCommand<T>` calls `Execute(null, …)` **on purpose** | [StingCommandHandler.cs:4417](../StingTools/UI/StingCommandHandler.cs) | `ExternalCommandData` is **null** on every panel/Hub dispatch. Use `VisibilityCommandHelper.ResolveApp(cmd)`, never `cmd.Application`. Reading it directly produced a bogus "No active view." with a view plainly open. 53 other files already follow the `CurrentApp` convention. |
| `StingDockPanel._instance` is assigned at the END of the constructor | [StingDockPanel.xaml.cs](../StingTools/UI/StingDockPanel.xaml.cs) | It was never assigned at all until this branch, so `LastInstance` was permanently null and ~20 `LastInstance?.X()` call sites silently no-opped. Do not "tidy" this away. |
| Two open tags, deliberately distinct | `Vis_OpenDropdown` (SELECT tab → anchored popup) · `Vis_OpenFloating` (Hub/QAT → floating window at cursor) | The launch **source** picks the presentation. Keep both. |
| Token parameter names are computed properties | `ParamRegistry.DISC` / `.LOC` / `.ZONE` / `.LVL` / `.SYS` / `.FUNC` / `.PROD` | Never hardcode `"ASS_ZONE_TXT"`. They are `TokenParamName(i)` and are project-configurable. |
| Project paths go through `StingPaths` only | [StingPaths.cs](../StingTools/Core/StingPaths.cs) | `tools/check_path_discipline.ps1` is a hard build gate, Tier 1 and Tier 2 both zero. |
| Filter prefix `"STING VIS - "` is a contract | `VisibilityRuleMatcher.FilterPrefix` | It is how `Vis_PurgeFilters` cleans up without touching `STING - Stale Elements`. A test asserts this. |
| Tests live in `StingTools.Visibility.Tests` (64 passing) | wired via the workflow's `StingTools.*.Tests/*.csproj` glob | No yml edit needed for a new test file. A project that does not COMPILE reports nothing — that is how #553 hid 97 tests for ten weeks. |
| Build + deploy | `dotnet build StingTools/StingTools.csproj -c Release` then `STING_DEPLOY=1 bash extract_plugin.sh` | **Do not use `deploy.bat` from a non-interactive shell** — `build.bat:39` calls `bash`, which resolves to WSL (not installed) and fails *after* a successful build. Revit must be closed. |

Baseline to beat: **0 errors / 0 warnings**, **64 tests green**.

---

## 1. The dropdown must reflect what is ALREADY hidden  ← highest value, do first

### The defect

`VisRowVm._isChecked` defaults to `true` ([VisibilityRowVm.cs:17](../StingTools/UI/Visibility/VisibilityRowVm.cs)) and
`VisibilityDropdown.Load` ([VisibilityDropdown.xaml.cs:35](../StingTools/UI/Visibility/VisibilityDropdown.xaml.cs))
builds every row fresh. Nothing anywhere reads the view's current visibility state — grep for
`IsTemporaryHideIsolateActive`, `GetCategoryHidden`, `GetFilters()` under `UI/Visibility/` and
`TokenValueHarvester` returns **nothing**.

So: hide Ducts → close → reopen → Ducts shows **ticked**, footer says **"Nothing hidden"**.
The panel asserts something false about the model. It is write-only.

This is a correctness bug, not polish: the next Apply is computed from a "nothing is hidden"
baseline, so a previous hide is silently reverted or compounded depending on mode.

### What to build

A `VisibilityStateReader` (new, `Core/Visibility/`, Revit-bound, ~180 lines) returning a
`VisibilityState` record: which category ids are hidden, which `STING VIS -` filters are
applied and what rule each encodes, and whether a temporary hide/isolate is active.

`VisibilityDropdown.Load(harvest, state)` seeds each row's `IsChecked` from it.

**The trap — read this twice.** Revit exposes **no API to enumerate temporarily hidden
elements**. `View.IsTemporaryHideIsolateActive()` tells you the mode is ON, not what it hid.
The only honest read-back is a set difference:

```csharp
// A collector scoped to a view HONOURS temporary hide/isolate; one scoped to the
// document does not. The difference is what is temporarily hidden.
var visible = new FilteredElementCollector(doc, view.Id)
    .WhereElementIsNotElementType().ToElementIds();
var all = new FilteredElementCollector(doc)      // same category scope as the harvest
    .WhereElementIsNotElementType().ToElementIds();
```

Do NOT invent a persistent side-record of "what we hid" to dodge this — it desynchronises the
moment the user hides something with Revit's own HH/HI commands, and a state reader that
disagrees with the model is worse than none. Derive from the model, every time.

For the other two mechanisms the API is direct: `view.GetCategoryHidden(catId)`, and
`view.GetFilters()` + `view.GetFilterVisibility(id)` filtered by `IsStingVisibilityFilter`,
parsed back through the existing `VisibilityRuleMatcher.TryParseFilterName`.

### Footer

Replace "Nothing hidden — 97 elements visible" with the truth, e.g.
`3 categories + ZONE Z02 hidden · 61 of 97 visible · saved to view`. When nothing is hidden,
the current wording is fine.

### Tests (pure layer only — the reader itself needs Revit)

Put the **diff and reconciliation logic** in a Revit-free static so it can be tested:
given (all ids, visible ids, hidden category ids, parsed filter rules) → expected row states.
Cover: nothing hidden; a category hidden; a token filter applied; both at once; a filter
present but `SetFilterVisibility(true)` (applied yet NOT hiding — must read as visible).

---

## 2. Make the category list usable on a real model

### The defect

`TokenValueHarvester.Harvest` tallies every distinct category it meets and sorts by name —
no filtering, no nesting ([TokenValueHarvester.cs:189](../StingTools/Core/Visibility/TokenValueHarvester.cs)).

Observed on a nearly-empty architectural view (97 elements, 20 rows): `Cameras (2)`,
`Views (2)`, `Scope Boxes (2)`, `Section Boxes (1)`, `ACAD-masindi TP.dxf (1)`,
`ACAD-masindi TP.dxf (2) (1)`, and `Runs` / `Supports` / `Top Rails` / `Stair Paths` sitting as
siblings of `Railings` and `Stairs`. On a real MEP model this is unreadable.

### Three changes

1. **Exclude view-management categories.** Cameras, Views, Section Boxes, Scope Boxes are not
   model content; hiding "Cameras" is never the intent. Ship the list as
   `"excludedCategories": ["OST_Cameras", "OST_Views", "OST_SectionBox", "OST_ScopeBoxes"]`
   in the existing `Data/STING_VISIBILITY_PRESETS.json` so it is overridable per project
   through the path that already exists — do NOT add a new data file, and do NOT hardcode it.
   **Keep Grids and Levels**: hiding those is a real, common request.

2. **Nest subcategories under their parent** via `Category.Parent`. A parent row is tri-state:
   unticking Railings unticks Runs/Supports/Top Rails. This is the `VgRow` tri-state pattern
   `RevitVgEditor` already uses — copy the pattern, do not import that file.

3. **Split Model / Annotation / Imports** into three groups, mirroring Revit's own V/G tabs so
   it is instantly familiar. `Category.CategoryType` gives you this
   (`CategoryType.Model` / `.Annotation`); imports are `OST_ImportObjectStyles` descendants —
   detect via `CategoryType` plus `Category.Id` being an import instance category, and put
   anything you cannot classify in Model rather than dropping it silently.

**Nothing may be silently dropped.** If a category is excluded or unclassifiable, it still
counts toward the totals; log the exclusion count once per harvest at Info.

---

## 3. Empty token groups must say why they are empty

On an untagged model all seven token groups expand to nothing, and nothing distinguishes
"this view has no ZONE values" from "the harvest failed". Add a single muted row —
`no ZONE values in this view — run tagging first` — and put the count in the group header
(`ZONE (4)`, `LEVEL (0)`) so you can see what is populated without expanding all seven.

Small, but it is the difference between a user trusting the panel and filing a bug.

---

## 4. Small wins, same pass

- **Hidden-count badge** on the SELECT-tab row and the Hub button tooltip — e.g. `👁 Show / Hide (3 hidden)` — so a filtered view is visible without opening anything. This is the single best defence against "why can't I see my ducts".
- **Tooltip the undo asymmetry** on the mode toggle, verbatim: *"Temporary hide is not undoable with Ctrl+Z and does not print. Saved to view is undoable and prints."* Revit temporary view modes are not transactional; saved filters are. This will surprise people otherwise.
- **Reset** already clears both mechanisms — keep it that way, and make the new footer prove it by showing zero hidden afterwards.

---

## 5. Explicitly OUT of scope

- Live hover-highlight of matching elements — expensive per tick; the live count already answers the question.
- Category **isolate** in Saved mode — structurally impossible (a view filter only acts on the categories it binds to) and already reported as a clear blocker. Do not attempt a workaround.
- Worksets, phases, design options as visibility axes.
- Any use of `Autodesk.Windows` / `AdWindows` — undocumented, version-fragile, and it can corrupt the user's saved QAT layout. The one legitimate need (owning a window to Revit) is already solved with the supported `UIApplication.MainWindowHandle`.

Log anything you want from this list into `docs/ROADMAP.md` rather than building it.

---

## 6. Definition of done

- [ ] `dotnet build StingTools/StingTools.csproj -c Debug -t:Rebuild` → **0 errors, 0 warnings**, no new `NoWarn`.
- [ ] `StingTools.Visibility.Tests` green, **more than 64** tests, including the §1 reconciliation cases.
- [ ] `tools/check_path_discipline.ps1` passes.
- [ ] No empty `catch` blocks; every catch wrapping a mutation logs via `StingLog`.
- [ ] No file over ~400 lines.
- [ ] **Verified in Revit** on a model with real MEP content, not a 97-element architectural view:
      hide a category → reopen → it reads back **unticked**; hide by ZONE in Saved mode →
      reopen → the ZONE row reads back unticked; Reset → footer shows nothing hidden;
      the category list shows no Cameras/Views and nests Runs under Railings.
- [ ] `docs/CHANGELOG.md` entry; new gaps to `docs/ROADMAP.md`; CLAUDE.md Visibility section updated if the command set changed.

Report each item's real state, and mark the in-Revit item ✗ rather than claiming it if you
could not drive Revit. That honesty is what caught three separate bugs on this feature already
— every one of them lived in the Revit-bound layer that no unit test can reach.
