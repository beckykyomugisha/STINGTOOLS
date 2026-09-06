# RUNNER — STING Visibility Center

**Goal:** one reachable dropdown on the main STING panel that shows/hides elements by
**category** *and* by **ISO 19650 tag token** (DISC / LOC / ZONE / LVL / SYS / FUNC / PROD),
in either **temporary** (session) or **saved view-filter** (persistent, prints) mode.

Deliver it as a **pure engine + thin UI**, not as another `TaskDialog`-fused command. The
engine must be runnable without Revit UI so it can carry unit tests — this is deliberately
the pattern CLAUDE.md P1 #4 asks someone to prove on one high-value feature.

---

## 0. Ground truth — read these before writing anything

| Read | Why |
|---|---|
| [StingCommandHandler.cs:4479-4522](../StingTools/UI/StingCommandHandler.cs) — `ViewIsolateSelected` / `ViewHideSelected` / `ViewResetIsolate` / `ViewRevealHidden` | The only hide code that exists today. Temporary-mode idiom. Tags `ViewIsolate` / `ViewHide` / `ViewReset` at switch lines 762-765. **Keep these working; the new feature supersedes but must not break them.** |
| [StaleFlagCommands.cs:96-140](../StingTools/Select/StaleFlagCommands.cs) — `FindOrCreateFilter` + `ApplyHighlight` | The canonical `ParameterFilterElement.Create` → `view.AddFilter` → `SetFilterVisibility` idiom in this codebase. **Copy this shape exactly.** |
| [RevitVgEditor.cs](../StingTools/UI/RevitVgEditor.cs) (1,352 lines) — `VgRow`, tri-state `Visible`, `SetAllVisible` / `InvertVisible` | Existing tri-state category checkbox row model. Reuse the *pattern*; do not extend this file. |
| [TagSelectorCommands.cs](../StingTools/Select/TagSelectorCommands.cs) — `SelectTagsByTokenCommand`, `SelectTagsByDisciplineCodeCommand` | Token-matching logic already exists for *selection*. Reuse the token→parameter resolution, do not re-derive it. |
| [ParamRegistry.cs:121-133](../StingTools/Core/ParamRegistry.cs) — `DISC` / `LOC` / `ZONE` / `LVL` / `SYS` / `FUNC` / `PROD` | Token parameter names are `TokenParamName(i)` properties, **not** literals. Never hardcode `"ASS_ZONE_TXT"`. |
| [AecFilterFactory.cs](../StingTools/Core/Drawing/AecFilterFactory.cs) + `AecFilterRegistry` | Existing JSON→`ParameterFilterElement` factory. Check whether it can be reused before writing a new one; if it can't, say why in the PR body. |
| [StingPaths.cs](../StingTools/Core/StingPaths.cs) | **Never hand-build a project path.** Presets go through `StingPaths.MetaFile(doc, "_BIM_COORD", "visibility_presets.json")`. `tools/check_path_discipline.ps1` fails the build otherwise. |
| [StingDockPanel.xaml:272-320](../StingTools/UI/StingDockPanel.xaml) — SELECT tab | Where the entry point goes. Note the `Tag="X" Click="Cmd_Click"` dispatch convention and the `CatBtn` / `BlueBtn` / `GreenBtn` styles. |

---

## 1. Architecture

Five new files under `StingTools/Core/Visibility/`, one UI file, one command file.
**No file over ~400 lines.** If one grows past that, split it.

### 1.1 `Core/Visibility/VisibilityRule.cs` — the data model (~90 lines, Revit-free)

```csharp
public enum VisibilityRuleKind { Category, Token }
public enum VisibilityAction   { Hide, ShowOnly }   // ShowOnly == isolate
public enum VisibilityMode     { Temporary, ViewFilter }
public enum VisibilityTarget   { ActiveView, SelectedViews, AllViewsOnSheet, ViewTemplate }

public sealed class VisibilityRule
{
    public VisibilityRuleKind Kind { get; set; }
    public int      CategoryId { get; set; }   // BuiltInCategory as int, Kind==Category
    public string   TokenKey   { get; set; }   // "DISC"|"LOC"|"ZONE"|"LVL"|"SYS"|"FUNC"|"PROD"
    public List<string> Values { get; set; }   // e.g. ["Z02","Z03"] — OR within a rule
    public VisibilityAction Action { get; set; }
}

public sealed class VisibilitySet          // what a preset serialises to
{
    public string Name { get; set; }
    public VisibilityMode   Mode   { get; set; }
    public VisibilityTarget Target { get; set; }
    public List<VisibilityRule> Rules { get; set; }
}
```

Semantics — **state these in XML doc comments, they are the whole contract**:
- Values **within** one rule are OR-ed (`ZONE ∈ {Z02, Z03}`).
- Rules **across** kinds are AND-ed (`ZONE ∈ {Z02} AND LOC ∈ {BLD1}` → only elements matching both).
- Any `ShowOnly` rule present flips the whole set to isolate semantics; mixing `Hide` and
  `ShowOnly` is **rejected with a clear message**, not silently resolved.

### 1.2 `Core/Visibility/TokenValueHarvester.cs` (~140 lines)

Scans the document (or active view — respect the existing `SetSelectionScopeCommand`
project/view scope toggle) and returns `Dictionary<string tokenKey, SortedSet<string> values>`
plus a per-value element count, so the dropdown can render `Z02 (147)`.

- Cache per-document with a 30-second stale window, **mirroring `ComplianceScan`** — same
  `InvalidateCache()` public method, and call it from the existing cache-invalidation points.
- Skip null/empty token values but **count them** and surface as a synthetic `(unset)` entry —
  hiding untagged elements is a real workflow.

### 1.3 `Core/Visibility/VisibilityEngine.cs` (~300 lines) — **the pure core**

```csharp
public static VisibilityPlan Plan(Document doc, View view, VisibilitySet set);
public static VisibilityResult Apply(Document doc, View view, VisibilityPlan plan); // needs open txn
public static VisibilityResult Reset(Document doc, View view, VisibilityMode mode);
```

`Plan` computes and **touches nothing** — it returns a record of: matched element ids,
required filters (name + category ids + rule), per-rule counts, and a `List<string> Blockers`.
`Apply` performs the write. This split is what makes the engine testable.

**Temporary path:** resolve ids → `view.HideElementsTemporary(ids)` /
`IsolateElementsTemporary(ids)`. No transaction needed (temporary view modes are not
transactional), but check `view.CanUseTemporaryVisibilityModes()` first.

**ViewFilter path** — follow `StaleFlagCommands.FindOrCreateFilter` exactly:
1. Filter name is deterministic: `"STING VIS - {TokenKey}={Value}"` or `"STING VIS - Cat {CategoryName}"`.
   **The `STING VIS - ` prefix is the contract** — `Vis_PurgeFilters` finds and deletes by it.
2. Look up an existing `ParameterFilterElement` by that name before creating.
3. Category ids from `ParameterFilterUtilities.GetAllFilterableCategories()` ∩
   `SharedParamGuids.AllCategoryEnums` — a non-filterable category in the list throws.
4. Resolve the token parameter's `ElementId` the same way `ResolveStaleParamId` does; if the
   shared parameter is **not bound** to the target categories, that is a `Blocker`, not an
   exception — report "ZONE is not bound to Ducts; 3 categories skipped".
5. `ParameterFilterRuleFactory.CreateEqualsRule(paramId, value, caseSensitive:false)`,
   OR-combined via `LogicalOrFilter` for multi-value rules.
6. `view.AddFilter(id)` then `view.SetFilterVisibility(id, false)` to hide.

**Blockers the engine MUST detect and report rather than throw:**
- View is controlled by a view template that locks V/G → offer "apply to the template instead".
- `view.AreGraphicsOverridesAllowed() == false`.
- Legend / schedule / sheet views that accept neither mechanism.
- Zero elements matched (report the count, do not silently no-op).

### 1.4 `Core/Visibility/VisibilityPresetStore.cs` (~110 lines)

Load/save `List<VisibilitySet>` to
`StingPaths.MetaFile(doc, "_BIM_COORD", "visibility_presets.json")`. Layer a corporate
baseline from `Data/STING_VISIBILITY_PRESETS.json` underneath, project entries winning by
`Name` — the **same corporate-baseline + project-override pattern** as `DrawingTypeRegistry`
and `MepSizingRegistry`. Ship 4 baseline presets: `Zone isolation`, `Discipline solo`,
`Hide untagged`, `MEP only`.

> Newtonsoft silently leaves mistyped fields at default. Round-trip the shipped JSON through
> `VisibilityPresetStore.Load` in a unit test so a typo fails the build, not a user's Friday.

### 1.5 `UI/Visibility/VisibilityDropdown.xaml(.cs)` (~350 lines)

A WPF `Popup` (not a modal window) anchored to the panel button. Layout top → bottom:

```
[ Temporary ⇄ Saved to view ]        [ Active view ▾ ]     ← mode + target
[ 🔍 search…                                        ]      ← filters the tree live
├ ▾ CATEGORIES                    [All] [None] [Invert]
│   ☑ Ducts (412)   ☑ Pipes (233)  ☐ Furniture (88) …
├ ▾ ZONE                          [All] [None] [Invert]
│   ☑ Z01 (147)  ☐ Z02 (98)  ☐ (unset) (12)
├ ▸ LOCATION   ▸ LEVEL   ▸ DISCIPLINE   ▸ SYSTEM   ▸ FUNCTION   ▸ PRODUCT
[ Preset ▾ ] [Save…]        [Reset all] [Isolate] [Apply]
```

- Tri-state checkboxes; reuse the `VgRow`/`ChevronVisibility` binding shape from
  `RevitVgEditor`, but in a **new small view-model** — do not import that file.
- Unchecking = Hide. The **Isolate** button applies `ShowOnly` to whatever is checked.
- Token groups are collapsed by default and populated lazily from `TokenValueHarvester` on
  first expand — a 50k-element model must not stall on open.
- A live footer line: `"Will hide 1,204 of 8,331 elements · 3 filters"` computed from
  `VisibilityEngine.Plan` (cheap, no writes) so the user sees the effect **before** Apply.
- **Reset all** must clear *both* mechanisms — `DisableTemporaryViewMode` **and** remove every
  `STING VIS - ` filter from the view. The single most likely support ticket is "I hit Reset
  and it's still hidden"; make one button fix both.

### 1.6 `Commands/Visibility/VisibilityCommands.cs`

| Tag | Class | Txn | Does |
|---|---|---|---|
| `Vis_OpenDropdown` | `OpenVisibilityDropdownCommand` | ReadOnly | Opens the popup |
| `Vis_Apply` | `ApplyVisibilityCommand` | Manual | Engine `Apply` for the current set |
| `Vis_Isolate` | `IsolateVisibilityCommand` | Manual | `ShowOnly` variant |
| `Vis_ResetAll` | `ResetVisibilityCommand` | Manual | Clears temporary + `STING VIS -` filters |
| `Vis_PurgeFilters` | `PurgeVisibilityFiltersCommand` | Manual | Deletes every `STING VIS - ` `ParameterFilterElement` project-wide; reports count |
| `Vis_ApplyToTemplate` | `ApplyVisibilityToTemplateCommand` | Manual | Pushes the set onto the active view's template |
| `Vis_SavePreset` / `Vis_LoadPreset` | — | Manual | Preset store round-trip |

Register every tag in `StingCommandHandler`'s switch **next to the existing `ViewHide` /
`ViewIsolate` / `ViewReset` cases at lines 762-765** so the visibility cases sit together.

### 1.7 Entry point — SELECT tab

Add one row at the top of the SELECT tab in `StingDockPanel.xaml`, above `AI SMART SELECT`:

```xml
<TextBlock Style="{StaticResource SectionLabel}" Text="👁 VISIBILITY"/>
<Border Style="{StaticResource GroupBorder}" BorderBrush="#4CAF50">
  <WrapPanel>
    <Button Style="{StaticResource GreenBtn}" Content="👁 Show / Hide ▾" Tag="Vis_OpenDropdown" Click="Cmd_Click"
            ToolTip="Show/hide by category or ISO tag token (zone, location, level, discipline…)"/>
    <Button Style="{StaticResource ActionBtn}" Content="Reset" Tag="Vis_ResetAll" Click="Cmd_Click"/>
    <Button Style="{StaticResource BlueBtn}"   Content="Presets ▾" Tag="Vis_LoadPreset" Click="Cmd_Click"/>
  </WrapPanel>
</Border>
```

The dropdown is WPF-only and needs no Revit API to *open*, so `Vis_OpenDropdown` may open
directly in `Cmd_Click` without the `ExternalEvent` round-trip. Every **Apply** still goes
through `_handler.SetCommand(tag)` + `_externalEvent.Raise()` — the panel snapshots the
checkbox state into static fields on the handler first, exactly as `StingHvacPanel` does with
`CurrentRegion` / `CurrentStandard`.

---

## 2. Tests — non-negotiable

New project `StingTools.Visibility.Tests` (xUnit, net8.0), following the `Clash`/`Routing`
pattern: `<Compile Include>` the Revit-free files (`VisibilityRule.cs`,
`VisibilityPresetStore.cs`, and the rule-matching half of the engine) behind the existing
hand-written Revit stubs.

Cover at minimum:
1. Values within a rule OR; rules across kinds AND.
2. Mixed `Hide` + `ShowOnly` is rejected with a message.
3. `(unset)` matches null **and** empty-string token values.
4. Preset JSON round-trips — load the shipped `STING_VISIBILITY_PRESETS.json` and assert 4
   presets with non-null `Rules` (catches the Newtonsoft silent-default trap).
5. Filter naming is stable and round-trips: `name → (kind, token, value) → name`.
6. `Plan` on an empty rule set returns zero matches and **no** blockers.

Add the project to `.github/workflows/stingtools-unit-tests.yml` — a test project that does
not compile reports nothing at all, which is how 97 dead tests once got counted as coverage.

---

## 3. Constraints and traps

- **Do not** hardcode `"ASS_ZONE_TXT"` — go through `ParamRegistry.ZONE` etc. Token parameter
  names are configurable and a literal will silently stop matching on a renamed project.
- **Do not** build project paths by hand. `StingPaths` only; the path-discipline check is a
  hard zero-tolerance build gate.
- **Temporary hide does not print and does not survive view close.** Say so in the mode
  toggle's tooltip, verbatim. This is the #1 source of confusion between the two modes.
- Every `catch` that wraps a *write* logs via `StingLog.Warn/Error`. Benign optional-parameter
  reads may stay bare; mutations may not.
- `[Transaction(TransactionMode.Manual)]` + `[Regeneration(RegenerationOption.Manual)]` on the
  writing commands, `ReadOnly` on the dropdown opener.
- Wrap multi-view application (`AllViewsOnSheet`, `SelectedViews`) in a `TransactionGroup` so
  a mid-run failure rolls back cleanly rather than leaving half the sheet filtered.
- 50k-element performance: `TokenValueHarvester` must use one `FilteredElementCollector` pass
  with `WhereElementIsNotElementType()`, reading parameters once per element into all seven
  token buckets — not seven separate passes.

---

## 4. Definition of done

- [ ] `dotnet build StingTools/StingTools.csproj -c Debug` → **0 errors, 0 warnings** (the
      repo baseline is 0/0; anything else is a regression you introduced).
- [ ] `StingTools.Visibility.Tests` runs green and is wired into the unit-test workflow.
- [ ] `tools/check_path_discipline.ps1` passes.
- [ ] Verified **in Revit** on a real model: hide by category, hide by ZONE, hide by two
      tokens combined, isolate, Reset-all clears both mechanisms, preset save + reload,
      `Vis_PurgeFilters` leaves the project clean.
- [ ] A view-template-locked view produces the "apply to the template instead" blocker rather
      than a silent no-op or an exception.
- [ ] `docs/CHANGELOG.md` gets a `#### Completed (Phase N — Visibility Center)` block.
      New gaps discovered go to `docs/ROADMAP.md`, not into CLAUDE.md.
- [ ] CLAUDE.md gains a short **Visibility Center** section (≤ 40 lines) in the same shape as
      the Placement Center section — file table, command tags, data file, caveats.

## 5. Explicitly out of scope

Worksets and phases as visibility axes; design-option visibility (already covered by
`DesignOptions_LockView`); per-element graphic overrides beyond show/hide (that is
`RevitVgEditor`'s job); syncing visibility state to Planscape Server. Log any of these to
`docs/ROADMAP.md` if they come up — do not build them here.
