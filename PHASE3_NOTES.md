# Phase 3 notes — main STING dock-panel polish + reorg

Branch: `feature/phase3` (off `main`). Build clean `--no-incremental`, 0/0, no CompiledPlugin churn.

## 3a — RESTYLE (this push)

### What landed
- **New `StingTools/UI/Resources/StingButtonStyles.xaml`** — one ResourceDictionary, the
  single tuning point. Named brushes (`ActionBtnBrush`, `PrimaryBtnBrush`, `MutedBtnBrush`,
  `DangerBtnBrush`, `SectionHeaderBrush`, `BtnFgBrush`, `BtnBorderBrush`, `BtnHoverBrush`,
  `BtnPressBrush`) + named sizes (`BtnHeight`, `BtnMinWidth`, `BtnFontSize`,
  `SectionHeaderHeight`) + styles: `StingButton` (default), `StingPrimaryButton`,
  `StingMutedButton`, `StingDangerButton`, `StingSectionHeader`, `StingDivider`.
- `StingButton` gives the polish the dedicated panels' flat default lacked: rounded
  corners + a **hover/press overlay** (a translucent white/black layer → brightness lift,
  **never a colour swap**, per the brief). Consistent height/min-width/font from the named sizes.
- **`StingDockPanel.xaml`**: merged the dictionary at the top of `Page.Resources`; the
  dictionary's keyless `<Style TargetType="Button"/>` is the **implicit default** so every
  bare `<Button/>` (and the ~23 keyless ones) inherits the look. The existing local styles
  were re-pointed to inherit it:
  - `ActionBtn`, `CatBtn` → `BasedOn StingButton` (neutral; **colour unchanged**).
  - `GreenBtn` → `BasedOn StingPrimaryButton` — **key-action mapping**: every Run/Apply/
    Build/Issue button that uses `GreenBtn` (109 buttons) now carries the shared Primary
    accent. Retune all of them from `PrimaryBtnBrush` in one edit.
  - `RedBtn` → `BasedOn StingDangerButton` (17 buttons) — delete/reset → shared Danger.
  - `OrangeBtn` / `BlueBtn` / `PurpleBtn` / `TealBtn` → `BasedOn ActionBtn` (inherit the
    rounded template + hover + metrics) **keeping their existing colours** (lowest-risk;
    "not worse").
  - `SectionLabel` (141 headers) → `BasedOn StingSectionHeader` → header colour now flows
    from `SectionHeaderBrush`.

### Decisions you may want to revisit (all one-edit tweaks)
1. **Base buttons now use a fixed neutral (`ActionBtnBrush` = `#F0F0F0`) instead of the
   theme's `DynamicResource ButtonBg`.** Trade-off: you asked for a tunable named brush,
   which means a concrete colour. `#F0F0F0` == the *default (light)* theme's `ButtonBg`, so
   nothing looks different today — but main-panel base buttons no longer recolour when you
   switch to Warm/Cool/Corporate themes. If you'd rather keep theme-following on the base,
   say so and I'll point `StingButton.Background` back at `DynamicResource ButtonBg` (you
   lose the single-brush tunability on the base only).
2. **`ActionBtnBrush` is a cool neutral, not "warm."** Your aesthetic note said "warm action
   buttons." I left it neutral `#F0F0F0` for a zero-surprise first pass — tell me a warmth
   and I'll nudge it (e.g. `#F2EEE9`) in one line.
3. **Primary colour = teal `#00897B`** (lifted from the HVAC panel's primary). Change
   `PrimaryBtnBrush` to your preferred accent.
4. **Green→teal recolour.** Run buttons that were green (`GreenBtn`) are now teal (Primary).
   That's the deliberate "apply Primary to key actions" step. If you want run buttons to stay
   green, set `PrimaryBtnBrush` to a green.

### Scope notes / what I deliberately did NOT do in 3a
- **Did NOT migrate the dedicated panels** (HVAC / Electrical / Plumbing / LPS / MaterialHub)
  to the shared dictionary — per your revised instruction, that's a follow-up after you OK
  the main-panel look. They keep their existing local styles. Main panel may therefore look
  *slightly more* polished than them for now (rounded + hover) — better, not worse.
- **Did NOT insert new section headers.** The panel already carries 141 `SectionLabel`
  headers; restyling that one style polished them all. Inserting headers *where missing* is
  cleaner to do during the 3b reorg (when groups are moving anyway) — flagged for then.
- **`StingMutedButton` is wired but not bulk-applied.** Audit/diagnostic buttons mostly use
  the neutral `ActionBtn` already (which reads as muted). Per-button muted tagging is a
  precise pass best done in 3b; say the word if you want it sooner.
- **TAGS → Scale sub-tab: untouched.** No XAML in that sub-tab was edited. Its buttons
  inherit the global implicit style (sliders are unaffected — Button styles don't target
  Slider).

### Possible side effect to eye-check in Revit
- The implicit `<Style TargetType="Button"/>` targets `Button` exactly (not `ToggleButton` /
  `RepeatButton`), so ComboBox/ScrollBar internals are safe. Any *plain `Button`* living
  inside a DataGrid cell template / dialog within the panel will also pick up the new look —
  intended, but worth a glance.

## Flagged (NOT fixed here — unrelated to the restyle)
- **`docs/UI_CLEANUP_CAMPAIGN.md` does not exist.** It's referenced as the campaign-state
  doc but was never created; the campaign state lives across `MISWIRE_AUDIT.md`,
  `ORPHANS_AUDIT.md`, `DEDUPE_AUDIT.md`, `SILENT_BUTTONS_TODO.md`, `HEALTHCARE_WIRING.md`.
  Not creating it as part of a restyle; flagging per "don't auto-fix unrelated issues."

## 3b — REORG (next, after you OK the 3a look)
Audit-first: `REORG_PLAN.md` enumerating every top-level tab + its buttons (incl. the
Group-5 additions) and a proposed new map, one-line justification per move. Committed +
pushed with **no XAML moves**, then I pause for your approval before moving anything.
Constraints honoured: no behaviour change, Scale sub-tab and Healthcare dispatcher untouched,
dedicated-panel work stays out of the main panel.
