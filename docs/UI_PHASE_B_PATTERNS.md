# UI Phase B — Per-Tab Interaction Patterns

**Goal:** stop wasting vertical space with rows of full-text buttons. Replace
each section with the right control type so the panel reads like a tool (LPS
panel style) instead of a wall of rectangles.

**Locked from Phase A — do not undo:**
- Outline aesthetic (Primary / Danger / Blue / Orange / Purple / Teal are
  transparent-fill + coloured border + coloured text)
- Theme-driven base button surface (`{DynamicResource ButtonBg/Fg/BorderColor}`)
- Multi-target theme registry (every open panel cycles)
- Brand brushes (`PrimaryBtnBrush`, `DangerBtnBrush`) are NOT theme-cycled

**Do not touch:**
- TAG STUDIO → Scale sub-tab (sliders the user is actively developing)
- HEALTHCARE tab (re-wired last round, leave alone)
- Dedicated panels (HVAC / Electrical / Plumbing / LPS / Material Hub)
- `StingButtonStyles.xaml` (Phase A baseline)
- Any dispatch tag string (`Tag="..."` values) — keep IDENTICAL so the
  `StingCommandHandler` switch keeps routing
- Colour-scheme swatch buttons in TAG STUDIO → Style & Color (12 buttons
  whose Background IS the data preview)
- Gradient swatches and result-strip Borders (4 surfaces with intentional
  inline `Background="#..."`)

**Verification gate (replaces the Phase 3 button-count invariant):**

Phase A's "button count must stay 1487" rule does NOT apply to Phase B —
the whole point is to fold N buttons into 1 picker. Use instead:

1. **Every dispatch tag still reachable.** Run `grep -oE 'Tag="[A-Za-z_0-9]+"'
   StingTools/UI/StingDockPanel.xaml | sort -u` before AND after. The
   AFTER set must be a superset of the BEFORE set (it's fine to add new
   tags for new mode pickers; never lose one).
2. **XAML parses well-formed** (`python3 -c "import xml.etree.ElementTree
   as ET; ET.parse('StingTools/UI/StingDockPanel.xaml')"`).
3. **Zero CompiledPlugin/Data churn** (`git status` after commit).
4. **One tab per commit** so reverts are surgical.

---

## Pattern catalogue

### Pattern 1 — Action chip row

**Use when:** 3-8 buttons trigger immediate, independent actions (no
"pick mode then run"). Common in: export rows, audit rows, validator rows.

**Symptom in current XAML:** long `<WrapPanel>` with `<Button
Style="{StaticResource ActionBtn}">` repeated 4-8 times, each full text.

**Fix:** shrink buttons to chip size + short labels + tooltips. No structural
change to dispatch.

**Before:**
```xml
<WrapPanel>
    <Button Style="{StaticResource ActionBtn}" Content="Export to PDF" Tag="ExportPdf" Click="Cmd_Click" ToolTip="Export selected sheets to PDF"/>
    <Button Style="{StaticResource ActionBtn}" Content="Export to DWG" Tag="ExportDwg" Click="Cmd_Click"/>
    <Button Style="{StaticResource ActionBtn}" Content="Export to IFC" Tag="ExportIfc" Click="Cmd_Click"/>
    <Button Style="{StaticResource ActionBtn}" Content="Export to NWC" Tag="ExportNwc" Click="Cmd_Click"/>
</WrapPanel>
```

**After:**
```xml
<WrapPanel>
    <Button Style="{StaticResource ActionBtn}" Content="PDF" Tag="ExportPdf" Click="Cmd_Click" ToolTip="Export selected sheets to PDF" MinWidth="44"/>
    <Button Style="{StaticResource ActionBtn}" Content="DWG" Tag="ExportDwg" Click="Cmd_Click" ToolTip="Export to AutoCAD DWG" MinWidth="44"/>
    <Button Style="{StaticResource ActionBtn}" Content="IFC" Tag="ExportIfc" Click="Cmd_Click" ToolTip="Export to IFC4 (buildingSMART)" MinWidth="44"/>
    <Button Style="{StaticResource ActionBtn}" Content="NWC" Tag="ExportNwc" Click="Cmd_Click" ToolTip="Export to Navisworks NWC" MinWidth="44"/>
</WrapPanel>
```

**Rule:** label = 3-8 chars max in the button; full description in ToolTip;
`MinWidth="44"` (or smaller for very short labels).

### Pattern 2 — Mode picker (RadioButton ring)

**Use when:** 2-4 mutually exclusive scopes / modes that gate a single Run.
Common in: scope (Selection / View / Project), strategy (Velocity / Friction
/ Static Regain), unit (Metric / Imperial).

**Symptom in current XAML:** 2-4 buttons whose only distinction is the scope
they apply.

**Fix:** RadioButton group + single Run button. The Run button's Click handler
reads which radio is checked and routes to the matching tag via a small switch.

**Before:**
```xml
<WrapPanel>
    <Button Style="{StaticResource BlueBtn}" Content="View" Tag="SetScopeView" Click="Cmd_Click"/>
    <Button Style="{StaticResource OrangeBtn}" Content="Project" Tag="SetScopeProject" Click="Cmd_Click"/>
</WrapPanel>
```

**After:**
```xml
<TextBlock Text="Scope" FontSize="9" Foreground="{DynamicResource SubtleFg}" Margin="0,2,0,1"/>
<StackPanel Orientation="Horizontal" Margin="0,0,0,4">
    <RadioButton x:Name="rbScopeView"    Content="View"    GroupName="SelectionScope" IsChecked="True" Margin="0,0,10,0" FontSize="10" VerticalAlignment="Center"/>
    <RadioButton x:Name="rbScopeProject" Content="Project" GroupName="SelectionScope" Margin="0,0,10,0" FontSize="10" VerticalAlignment="Center"/>
</StackPanel>
```

The two original Tag handlers (`SetScopeView`, `SetScopeProject`) **stay
wired** — they're set when the RadioButton is checked. Wire via the existing
`Cmd_Click` or a dedicated radio-checked handler in the panel's code-behind:

```csharp
// In StingDockPanel.xaml.cs — one handler covers all scope radios.
private void OnScopeRadioChecked(object sender, RoutedEventArgs e)
{
    if (sender is RadioButton rb && !string.IsNullOrEmpty(rb.Tag as string))
        DispatchCommand(rb.Tag.ToString());
}
```

Tag the radios with the dispatch string instead of using Click — keeps the
agent's diff XAML-only:

```xml
<RadioButton x:Name="rbScopeView"    Content="View"    GroupName="SelectionScope" Tag="SetScopeView"    Checked="OnScopeRadioChecked" IsChecked="True"/>
<RadioButton x:Name="rbScopeProject" Content="Project" GroupName="SelectionScope" Tag="SetScopeProject" Checked="OnScopeRadioChecked"/>
```

`OnScopeRadioChecked` is a SINGLE shared handler — add it ONCE to the
code-behind (Phase B Round 1, by the first agent that needs it). Subsequent
agents reuse it. No merge conflict.

### Pattern 3 — ComboBox filter

**Use when:** 5+ choices, or the list is data-driven (level names, view
templates, discipline codes).

**Symptom in current XAML:** scrollable list of N small buttons, or a series
of "Filter by X" buttons.

**Before:**
```xml
<WrapPanel>
    <Button Content="L01" Tag="FilterByLevel_L01"/>
    <Button Content="L02" Tag="FilterByLevel_L02"/>
    <Button Content="L03" Tag="FilterByLevel_L03"/>
    <!-- ... 12 more ... -->
</WrapPanel>
```

**After:**
```xml
<DockPanel LastChildFill="True" Margin="0,2">
    <TextBlock DockPanel.Dock="Left" Text="Level" FontSize="10" VerticalAlignment="Center" Margin="0,0,6,0"/>
    <ComboBox x:Name="cmbLevelFilter" FontSize="10" Height="22" SelectedIndex="0">
        <ComboBoxItem Content="(all levels)" Tag=""/>
        <ComboBoxItem Content="L01" Tag="L01"/>
        <ComboBoxItem Content="L02" Tag="L02"/>
        <!-- ... -->
    </ComboBox>
</DockPanel>
<Button Style="{StaticResource StingPrimaryButton}" Content="Apply filter" Tag="FilterByLevel" Click="Cmd_Click" Margin="0,2,0,0"/>
```

Dispatch reads `cmbLevelFilter.SelectedItem` via the existing dispatch path.
For data-driven combos (levels, views), populate from code-behind on tab load.

### Pattern 4 — Multi-flag checkbox grid

**Use when:** user picks a SUBSET of independent flags before running.
Common in: validator suite (which checks to run), COBie sheet selection.

**Before:**
```xml
<WrapPanel>
    <Button Content="Run Naming Check" Tag="ValidateNaming"/>
    <Button Content="Run MEP Clearance" Tag="ValidateMepClearance"/>
    <Button Content="Run IFC Props" Tag="ValidateIfcProps"/>
    <Button Content="Run All" Tag="ValidateAll"/>
</WrapPanel>
```

**After:**
```xml
<Grid Margin="0,2">
    <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
    <Grid.RowDefinitions><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
    <CheckBox Grid.Row="0" Grid.Column="0" x:Name="chkValidateNaming"       Content="Naming"        IsChecked="True"  FontSize="10" Margin="0,1"/>
    <CheckBox Grid.Row="0" Grid.Column="1" x:Name="chkValidateMepClearance" Content="MEP clearance" IsChecked="True"  FontSize="10" Margin="0,1"/>
    <CheckBox Grid.Row="1" Grid.Column="0" x:Name="chkValidateIfcProps"     Content="IFC props"     IsChecked="False" FontSize="10" Margin="0,1"/>
</Grid>
<Button Style="{StaticResource StingPrimaryButton}" Content="Run selected" Tag="ValidateSuite" Click="Cmd_Click" Margin="0,4,0,0"/>
```

`ValidateSuite` is a new dispatch tag — the handler reads which CheckBoxes
are ticked and calls each individual validator in sequence. Add ONCE in
StingCommandHandler.cs by the first agent that needs it; subsequent suites
follow the same pattern.

### Pattern 5 — `<Expander>` for advanced options

**Use when:** a section has 1-2 primary actions plus 4-10 secondary / power-
user actions. Default the Expander to collapsed.

**Symptom in current XAML:** big WrapPanel with primary buttons mixed in
with options nobody uses daily.

**Before:**
```xml
<TextBlock Style="{StaticResource SectionLabel}" Text="SCHEDULE EXPORT"/>
<WrapPanel>
    <Button Style="{StaticResource StingPrimaryButton}" Content="Export schedule" Tag="ExportSchedule"/>
    <Button Content="Schedule Audit" Tag="ScheduleAudit"/>
    <Button Content="Schedule Compare" Tag="ScheduleCompare"/>
    <Button Content="Duplicate Schedule" Tag="ScheduleDuplicate"/>
    <Button Content="Field Manager" Tag="ScheduleFieldMgr"/>
    <Button Content="Color Schedule" Tag="ScheduleColor"/>
    <Button Content="Stats" Tag="ScheduleStats"/>
</WrapPanel>
```

**After:**
```xml
<TextBlock Style="{StaticResource SectionLabel}" Text="SCHEDULE EXPORT"/>
<Button Style="{StaticResource StingPrimaryButton}" Content="Export schedule" Tag="ExportSchedule" HorizontalAlignment="Stretch" Margin="0,0,0,4"/>
<Expander Header="Advanced schedule ops" FontSize="10" Margin="0,2,0,0">
    <WrapPanel Margin="0,4">
        <Button Style="{StaticResource ActionBtn}" Content="Audit"     Tag="ScheduleAudit"      ToolTip="Audit all schedules for issues"/>
        <Button Style="{StaticResource ActionBtn}" Content="Compare"   Tag="ScheduleCompare"    ToolTip="Compare two schedules side-by-side"/>
        <Button Style="{StaticResource ActionBtn}" Content="Duplicate" Tag="ScheduleDuplicate"  ToolTip="Duplicate a schedule with new name"/>
        <Button Style="{StaticResource ActionBtn}" Content="Fields"    Tag="ScheduleFieldMgr"   ToolTip="Field manager — add/remove/reorder columns"/>
        <Button Style="{StaticResource ActionBtn}" Content="Colour"    Tag="ScheduleColor"      ToolTip="Apply colour scheme to schedule rows"/>
        <Button Style="{StaticResource ActionBtn}" Content="Stats"     Tag="ScheduleStats"      ToolTip="Schedule statistics + summary"/>
    </WrapPanel>
</Expander>
```

Primary action sits proud at the top; advanced collapsed by default. Massive
visual reduction without losing any function.

### Pattern 6 — Short labels + ToolTips (universal)

**Apply EVERYWHERE.** Every button on every tab. Rule:
- Label ≤ 12 characters where possible
- `ToolTip="Full descriptive text..."` carries the long form
- Glyph prefixes encouraged: ✓ for apply, ⚠ for warning, ★ for primary, ⊕
  for add, ⊖ for remove, ⚡ for run

**Before:**
```xml
<Button Content="Cross-Model Clash Detection Run" Tag="CrossModelClash"/>
```

**After:**
```xml
<Button Content="X-Clash" Tag="CrossModelClash" ToolTip="Cross-model clash detection — runs against the active model + every linked Revit model"/>
```

---

## Decision tree

For each existing section, the agent decides:

```
Is the section a list of immediate-action buttons (no mode pick)?
├── YES → Pattern 1 (action chips: shorten labels, add tooltips, keep WrapPanel)
└── NO ↓

Does the section let the user pick ONE of N mutually-exclusive modes / scopes?
├── 2-4 options → Pattern 2 (RadioButton ring + Run)
├── 5+ options or data-driven → Pattern 3 (ComboBox + Run)
└── NO ↓

Does the user pick a SUBSET of independent flags?
├── YES → Pattern 4 (CheckBox grid + Run)
└── NO ↓

Does the section have 1-2 primary actions + 4-10 secondary?
├── YES → Pattern 5 (primary out, secondaries in Expander)
└── NO → leave as-is; just apply Pattern 6 (short labels + tooltips)
```

Pattern 6 (labels + tooltips) is **universal** — applied on top of any other
pattern decision.

---

## Shared code-behind helpers

The first agent to use Pattern 2 (RadioButton routing) or Pattern 4
(CheckBox suite) adds the helper handler to `StingDockPanel.xaml.cs`.
Subsequent agents reuse — no merge conflict.

```csharp
// One-handler-fits-all: every routing RadioButton tagged with its dispatch
// string fires the same Cmd_Click logic.
private void OnRadioRouteChecked(object sender, RoutedEventArgs e)
{
    if (sender is RadioButton rb && rb.Tag is string tag && !string.IsNullOrEmpty(tag))
        DispatchTag(tag);
}

// CheckBox-suite runner: every "Run selected" button with Tag="ValidateSuite"
// (or similar suite tag) reads its sibling CheckBoxes and dispatches each
// ticked one in sequence. Suite tags are wired in StingCommandHandler.cs.
```

---

## Per-tab scope (round briefs)

Each Phase B round briefs ONE agent on ONE tab (or sub-tab cluster). The
brief must include:

- This patterns doc URL
- The tab's line range in `StingDockPanel.xaml`
- The list of sections inside it (the agent audits first, commits an audit
  doc, then makes changes — same pattern as REORG_PLAN.md / INTEROP_PLAN.md)
- "Tag preservation" reminder (no dispatch tag can disappear)
- "XAML-only; if you need new code-behind, add it idempotently"

---

## Out of scope for Phase B

- New features / new dispatch tags (beyond what's needed for suite runners)
- Theme changes (Phase A is done)
- Touching dedicated panels (separate campaign)
- 3× SPECKLE dedup (separate cleanup commit)
- Quick-access duplicate dedupe (PHASE3_NOTES.md backlog)
- Anything in the `docs/UI_CLEANUP_CAMPAIGN.md` follow-up queue
