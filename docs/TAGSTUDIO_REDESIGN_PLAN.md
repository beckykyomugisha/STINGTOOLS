# TAG STUDIO sub-tab redesign plan (Phase B Round 3)

Per `docs/UI_PHASE_B_PATTERNS.md`. Touches **seven** sub-tabs inside the
top-level `TAG STUDIO` tab. Round 1 (INTEROP) + Round 2 (DOCS / MODEL /
SETUP) layouts and code-behind helpers stay intact — this round reuses
`OnRadioRouteChecked` and follows the same `Run<Tab>Runner(string)`
pattern but adds a fresh `RunTagStudioRunner` rather than touching the
existing runners.

## Sub-tabs explicitly NOT touched (locked by brief)

These six TAG STUDIO sub-tabs already have interactive controls
(compasses, sliders, swatches, depth grids) or the user is actively
developing them. MD5 verified BEFORE = AFTER:

| Sub-tab | Reason |
|---|---|
| `Placement` | 16-position compass + offset sliders + smart-place — already interactive |
| `Leader & Elbow` | Already interactive |
| `Style & Color` | 12 colour-scheme swatches whose Background IS the data preview |
| `Tokens & Depth` | Already interactive: depth slider + checkbox grid + paragraph builder |
| `Categories` | Already interactive |
| `Scale` | User is actively developing the sliders — explicitly forbidden |

No top-level tabs other than TAG STUDIO are touched. No dedicated panels.
No `StingButtonStyles.xaml`. No CompiledPlugin / Data churn.

## In-scope sub-tabs (7)

### 1. `Tools` (lines ~4334–4464)

Eleven section headers, each a `WrapPanel` wall of `ActionBtn` rectangles
with full-text labels. Pattern 5 + Pattern 6 candidate — one primary
action per section, secondaries fold into Expanders, short labels +
tooltips throughout.

| Section | Pattern | Rationale |
|---|---|---|
| MAINTENANCE (surfaced) | Pattern 1 | Single button — chip-shrink only |
| TAG PLACEMENT TOOLS | Pattern 5 | `Smart place` primary; 7 others fold into Expander |
| TOKEN PIPELINE | Pattern 5 | `Tag + combine` primary; 5 others fold into Expander |
| TEMPLATE TOOLS | Pattern 1 | 4 independent actions — chip row + tooltips |
| VALIDATION TOOLS | Pattern 5 | `Resolve issues` primary; 3 audits fold into Expander |
| STYLE PIPELINE | Pattern 5 | `Apply style` primary; 3 others fold into Expander |
| EXPORT TOOLS | Pattern 1 | 2 actions — chip row + tooltips |
| STATS | leave | Stat readout, not buttons |
| SCHEDULES | Pattern 1 | 4 independent actions — chip row + tooltips |
| GENERATE CODE | leave | Already has ComboBox + 2 buttons |

### 2. `Automation` (lines ~4567–4628)

Architecture / Structural / MEP / Standards bulk lists — each is 5–10
discipline-specific action buttons. The footer already has 4 quick-fire
primary buttons. Apply Pattern 5: keep the discipline header as a labelled
Expander (collapsed) so the panel collapses to footer + 4 small
expanders. Pattern 6 (short labels + tooltips) for every button.

| Section | Pattern | Rationale |
|---|---|---|
| Footer quick row (CoverAudit / WindApply / StageAudit / Compliance) | leave | Already primary chips |
| ARCHITECTURE (6 items) | Pattern 5 | Wrap discipline list in collapsed Expander |
| STRUCTURAL EXT (10 items) | Pattern 5 | Wrap in collapsed Expander |
| MEP DESIGN (7 items) | Pattern 5 | Wrap in collapsed Expander |
| STANDARDS BULK (5 items) | Pattern 5 | Wrap in collapsed Expander |
| REFERENCE | leave | Static helper text |

### 3. `Standards` (lines ~4631–4681)

Five discipline sections, each with 1–2 calculator buttons. Footer has 3
quick-fire primary buttons. Body buttons currently use verbose
`Content="Cable size (V/A/length → AWG, Vdrop)"`-style labels. Pattern 4
(CheckBox grid + Run-selected) fits because every button is an
independent calculator the user might want to run as a suite. Add new
suite-runner tag `Standards_RunSuite` wired via `RunTagStudioRunner`.

| Section | Pattern | Rationale |
|---|---|---|
| Footer quick row | leave | Primary chips |
| ELECTRICAL + STRUCTURAL + HVAC/LIGHTING + LIFE SAFETY | Pattern 4 | Single CheckBox grid + `Run selected` button |
| REFERENCE | leave | Static helper text |

### 4. `MEP` (lines ~4684–4754)

Footer has 4 quick-fire primaries. Body has INTELLIGENCE (5), AUTO-SIZE
(3), QA (9 — mixed CatBtn / ActionBtn with cryptic labels like "W-Ann",
"H-Run Batch"). Decisions:

| Section | Pattern | Rationale |
|---|---|---|
| Footer quick row | leave | Primary chips |
| INTELLIGENCE | Pattern 5 | `System analyse (model)` primary; 4 others fold into Expander |
| AUTO-SIZE | Pattern 2 | RadioButton ring (Pipes / Ducts / Conduits) + `Auto-size` Run button → `Mep_AutoSizeRun` runner reads radio and dispatches the matching tag |
| QA | Pattern 5 | `System naming audit` primary; 8 wire-annotation actions fold into Expander with full short labels + tooltips |
| REFERENCE STANDARDS | leave | Static helper text |

### 5. `Fabrication` (lines ~4757–4833)

Already RadioButton + CheckBox heavy (SCOPE, RULES, OUTPUT, CONTENT
MODE). The CheckBox lists carry per-checkbox config that the user
toggles before running — the existing `Generate package` button reads
them already. Apply Pattern 6 only — shorten footer button labels where
verbose, sharpen tooltips, no structural change.

| Section | Pattern | Rationale |
|---|---|---|
| Top + Bottom WrapPanels | Pattern 6 | Shorten labels, keep tooltips |
| SCOPE / RULES / OUTPUT / CONTENT MODE / TITLE BLOCK | leave | Already interactive |

### 6. `Routing` (lines ~4836–4853)

Five stretch buttons + 1 Open-Centre launcher + 1 result strip. Already
compact. Pattern 6 only — labels are already short; sharpen tooltips, no
structural change. Result strip stays.

### 7. `Fixtures` (lines ~4856–4873)

Same shape as Routing — 4 stretch buttons + 1 Open-Centre launcher +
result strip. Already compact. Pattern 6 only.

## Code-behind additions

One new helper `RunTagStudioRunner(string)` added to
`StingTools/UI/StingDockPanel.xaml.cs`, parallel to `RunInteropRunner` /
`RunDocsRunner` / `RunSetupRunner`. Two runner tags:

- `Standards_RunSuite` — reads `chkStdCableSize`, `chkStdWindLoad`,
  `chkStdCooling`, `chkStdLighting`, `chkStdEgress`, `chkStdSprinkler`
  and dispatches each ticked validator.
- `Mep_AutoSizeRun` — reads `rbMepSizePipes` / `rbMepSizeDucts` /
  `rbMepSizeConduits` and dispatches `Mep_AutoSizePipe` /
  `Mep_AutoSizeDuct` / `Mep_AutoSizeConduit`.

`OnRadioRouteChecked` is reused (no second handler). `Cmd_Click` gets a
fourth `if` branch checking for the TAG STUDIO runner tags and
delegating to `RunTagStudioRunner`. Matches Rounds 1/2 dispatch.

## Verification gates

1. Dispatch-tag superset proof (`comm -23 /tmp/before.txt /tmp/after.txt` empty).
2. XAML well-formed (`python3 -c "import xml.etree.ElementTree as ET; ET.parse(...)"`).
3. MD5 of the 6 locked TAG STUDIO sub-tabs matches BEFORE = AFTER.
4. `git status` shows ONLY the XAML + xaml.cs + this plan — no CompiledPlugin / Data churn.
