# MEP Drawing-Type Print-Readiness Runner

**Autonomous-agent runner.** Mission: make every MEP drawing type in STING truly
*"ready to print"* — a user drops in a view and the sheet is fully rendered and
correctly annotated — **and** make the DrawingType-configured *token depth,
paragraph depth, segment mask, display mode, and SEQ zero-pad* actually flow into
the produced tags. Findings below are pre-verified (two review passes + one direct
JSON check); file:line anchors are given so you don't re-discover them.

---

## 0. Ground truth / environment

- **Repo:** `C:\Dev\STINGTOOLS` (shared checkout — MANY agents use it). **Isolate in a
  git worktree; never `reset --hard` a shared checkout; verify branch before any
  destructive op.**
- **Base branch:** `claude/gold-integration-20260709` — the integrated tree (tag
  work + title-block/`98bdf1165` + newer main). **Branch your fix work FROM THIS.**
  Create a new worktree, e.g. `git worktree add -b claude/mep-print-ready
  C:/Dev/STINGTOOLS/.claude/worktrees/mep-print-ready claude/gold-integration-20260709`.
- **This machine CAN build.** `dotnet build StingTools/StingTools.csproj -c Release
  -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`. **Clean baseline = 0
  errors / 6 warnings** (not 0 warnings). Do NOT use the "Linux sandbox / no build"
  caveat — verify every fix compiles.
- **Deploy target:** `C:\Dev\STING_PLACEMENT_GOLD` (the Revit `.addin` points here).
  Deploy = build Release, `robocopy StingTools\bin\Release → GOLD`, **only when
  `Get-Process Revit` is empty**, then verify the deployed DLL md5 == your build.
  GOLD is shared — you must be the LAST writer; coordinate or another agent's deploy
  drops your work.
- **Key files:**
  - `StingTools/Data/STING_DRAWING_TYPES.json` (~10,644 lines; `routing[]` ~L9969)
  - `StingTools/Data/STING_VIEW_STYLE_PACKS.json`
  - `StingTools/Core/Drawing/DrawingType.cs`, `DrawingTypePresentation.cs`,
    `AnnotationRunner.cs`, `TokenProfileApplier.cs`
  - `StingTools/Core/TagConfig.cs`, `TagConfig.Tag7.cs`; `StingTools/Tags/ParagraphDepthCommand.cs`,
    `RefreshTagDisplayCommand.cs`
  - Tag-family catalogue: `StingTools/Data/STING_TAG_CONFIG_v5_0_{ARCH,MEP,STR,GEN}.csv`
  - Corporate baselines are checksum-locked (`origin:"corporate"`). Edits flip
    `origin`→`project` via drift detection — keep corporate entries pristine unless
    you intend to change the corporate baseline; add NEW packs/types as corporate.

## 1. STEP 0 — reconcile sibling branches BEFORE writing any code

Two branches may already implement parts of this. Do NOT duplicate or you'll create
merge conflicts:

- `claude/fix-tokens-depth-engine` (tip `c69e6fb40`)
- `claude/drawing-types-realign` (tip `7da1e7a07`)

For each: `git log --oneline claude/gold-integration-20260709..<branch>` and
`git diff --stat claude/gold-integration-20260709...<branch>`. If a branch already
lands a fix below (e.g. produce-path display refresh, or MEP style packs), **merge/adopt
it instead of re-implementing**. Report what each contains and your integration
decision before proceeding.

## 2. The work — prioritized (do 1→3 first; they touch the whole plan set)

### FIX 1 — BLOCKER: MEP discipline view-style packs

**Problem (verified):** MEP plans are bound to `corp-standard-plan`, an *architectural*
pack. Its `vgOverrides` in `STING_VIEW_STYLE_PACKS.json`:
```
Pipes:  { visible: false }        Ducts: { visible: false }
Mechanical Equipment / Electrical Equipment / Plumbing Fixtures: { halftone grey #808080 }
```
So a produced MEP plan renders **pipes and ducts invisible** and equipment as faint
grey background. No `corp-standard-hvac/-elec/-plumb` pack exists.

**Do:**
1. Author 3 corporate packs: `corp-standard-hvac`, `corp-standard-elec`,
   `corp-standard-plumb` (extend `corp-base`). Each shows ITS discipline's categories
   bold + coloured (visible:true, not halftone), keeps grids/rooms/walls as light
   context (walls thin halftone), and halftones the OTHER MEP disciplines. Propose the
   exact category→override table in your report for sign-off (this is a design call —
   don't guess silently; give sensible defaults and flag it).
2. Rebind the affected types' `viewStylePackId`:
   - HVAC → `mep-plan-A1-1to100`, `mep-hvac-duct-A1-1to100`, `mep-plantroom-A1-1to50`
   - Elec → `elec-power-A1-1to100`, `elec-lighting-A1-1to100`, `elec-fire-alarm-A1-1to100`
   - Plumb → `plumb-drainage-A1-1to100`, `plumb-ag-drainage-A1-1to100`,
     `plumb-rwd-layout-A1-1to100`, `plumb-water-treatment-A1-1to50`
3. Keep `corp-coordination` for `mep-coord`/`coord-clash` (multi-discipline overlay is
   correct there).

**Gate:** JSON assert — for each rebound plan, its pack shows its discipline's primary
categories `visible:true` and NOT `visible:false`.

### FIX 2 — tagFamilies coverage + key normalisation

**Problem:** (a) 3 types have AutoTag rules but **no `tagFamilies` map at all** →
every tag blank: `mep-coord-A1-1to50` (~L1469), `elec-riser-A3-1to200` (~L1965),
`coord-clash-A1-1to50` (~L4509). (b) Most plans map only 2–3 of the 6–9 AutoTagged
categories, and keys are inconsistent (`"Mechanical Equipment"` with a space vs
`"MechanicalEquipment"`).

**Do:**
1. FIRST read `AnnotationRunner.cs` to learn the EXACT key format the category→family
   lookup expects (Revit display name w/ spaces? `OST_` enum? concept name?). This
   decides everything — don't mass-edit before confirming.
2. Normalise ALL `tagFamilies` keys across MEP types to that format.
3. Fill EVERY AutoTagged category in every MEP type with a real tag family from the
   catalogue (`STING_TAG_CONFIG_v5_0_*.csv`). Verify each named family exists.

**Gate:** zero AutoTag rules with a blank/absent `tagFamilies` value across all MEP
types; every referenced family name exists in the catalogue.

### FIX 3 — produce-path: segment-mask scope bug + display refresh (token/para/zero-pad ask)

Two related defects mean mask/display don't materialise when a sheet is *produced*
(they only appear after a separate Auto/Batch-Tag pass):

- **Mask scope bug:** `TokenProfileApplier.cs:92-98` writes `SegmentMask` to the
  **VIEW's** `TAG_SEG_MASK`, but the only consumer `TagConfig.BuildDisplayTag`
  (`TagConfig.cs:2768`) reads it off the **ELEMENT** (or the view's *different*
  `VIEW_TOKEN_MASK`, `:2763`). → the mask lands where nothing reads it. Move the write
  into the per-element loop (alongside the display-mode write at `:121-137`) onto the
  element's `TAG_SEG_MASK_TXT`. Fix the misleading comment at `TagConfig.cs:2748`.
- **Display not re-rendered on produce:** `DrawingTypePresentation.Apply` (~`:611-646`)
  places `IndependentTag`s (`AnnotationRunner.cs:392`) but never calls `BuildDisplayTag`
  (only `BuildAndWriteTag:2418` does). After `TokenProfileApplier.Apply`, iterate the
  view's tagged elements and call `TagConfig.BuildDisplayTag(el)`. **This closes the
  segment-mask AND display-mode gaps together.**
- **Mask under compact display mode:** `BuildDisplayTag:2772-2773` applies the mask only
  in display modes 0/5. If a DrawingType sets a compact mode *and* a mask, the mask is
  dropped. Decide: either document "masks require mode 0/5", or apply the mask to the
  mode-resolved string in all modes as `RefreshTagDisplayCommand.cs:98` already does.

**Gate (in-Revit):** produce a sheet, confirm `ASS_DISPLAY_TXT` reflects the configured
mask + display mode WITHOUT a separate tag pass (smoke on a Duct + a socket).

### FIX 4 — routing, skeletal schedules, malformed slot, synthetic templates

- **Unrouted MEP types** (can't auto-produce): add routing rules `E/*/SPOOL →
  elec-spool-A1-1to50` and `E/*/COORD → elec-coord-A1-1to50` near the existing
  `M/*/SPOOL` and `M/*/COORD` rules (~L10042 / L10108).
- **Skeletal schedules** — `mech-equip-schedule-A3` (L9838), `elec-panel-schedule-A3`
  (L9882), `valve-schedule-A3` (L9926) have no `titleBlockParams`/`viewportTypeName`/
  `print`/`viewStylePackId`. Add the `${PRJ_ORG_*}` titleBlockParams block + viewport
  type so the sheet title block auto-populates like every other type.
- **Malformed slot** — `plumb-drainage-schematic-A1` (L9717) uses `{x,y,w,h}` (L9737)
  instead of `{normX,normY,normW,normH}`; also unique TB/VP and no `viewStylePackId`,
  and `purpose:"Schedule"` but it's a schematic riser. Fix slot keys, add a style pack,
  reclassify purpose.
- **Synthetic template names** — `elec-spool`/`elec-coord` use
  `viewTemplateName:"STING:corp-fabrication-shop:FloorPlan"` (L7361 / L7509), a
  `pack:viewtype` token, not a real named template. Either repoint to a real
  `"STING - …"` template or confirm a resolver handles that token form.

### FIX 5 — SEQ zero-pad scope decision (the "zero digits" ask)

Today SEQ pad is a **project-global** setting: `TagConfig.SeqPadWidth` /
`EffectiveSeqPad` (`TagConfig.cs:52,60`), set only from the dock panel
(`StingDockPanel.xaml.cs:1044`) — **it is NOT a DrawingType field.** And an
already-tagged element only re-pads under `TagCollisionMode.Overwrite`
(`TagConfig.cs:2318`); `AutoIncrement`+`skipComplete` skips it (`:2130-2134`).
Decide + implement one:
- (Recommended) add a `seqPad` field to `AnnotationTokenProfile` and push it into
  `TagConfig.SeqPadWidth` from the produce path (consistent with per-type paragraph
  depth), **or**
- document SEQ pad as a project-global knob and remove the false per-type expectation.

### FIX 6 — per-category paragraph-depth consistency (low)

`AnnotationRunner.cs:397-402` writes a single `PARA_STATE_{depth}` bool, whereas
`TokenProfileApplier.WriteCategoryDepths` (`:337-341`) writes the cumulative
`PARA_STATE_1..N`. Make the AnnotationRunner path cumulative so the two per-category
paths agree. (Global + per-category depth are otherwise **RESPECTED**, per-category
correctly overrides global — do not "fix" what works.)

### FIX 7 — Fire discipline completeness (scope decision)

Only *detection* exists (`elec-fire-alarm`, under E). There is no sprinkler/suppression
layout, no fire section, no fire detail as MEP types. Decide whether to author them or
record as out-of-scope in `docs/ROADMAP.md`. Don't silently leave the set looking
"complete."

## 3. What is ALREADY correct — do not touch

- **Token depth** (TAG7 A–F sections, T4–T10 tiers, pattern mode) — RESPECTED; written
  to the live gate params families read (`TokenProfileApplier.cs:138-146,544-626`).
  (Only caveat: composed `ASS_TAG_7_TXT` narrative rebuilds on a re-tag, addressed by
  FIX 3's produce-path pass if you extend it to `WriteTag7All`.)
- **Paragraph depth** global + per-category — RESPECTED; per-category overrides global.
- Every MEP type's **sheet identity** (paper, TB, scale, view template, viewport type,
  slots, number/name patterns) is populated. Don't rebuild these.

## 4. Verification gates

- Build green (0 err / 6 warn) after EACH fix, not just at the end.
- FIX 1 JSON check + FIX 2 zero-blank check as above (scriptable).
- Routing check: every MEP type id is reachable by a `routing[]` rule; every routing
  `drawingTypeId` resolves to a real type.
- **In-Revit acceptance (final):** produce one A1 plan per discipline (M/E/P) →
  (a) pipes/ducts/equipment VISIBLE, (b) tags placed with the correct per-category
  families, (c) token + paragraph depth honoured on the tag, (d) `ASS_DISPLAY_TXT`
  shows the configured mask/mode without a separate tag pass, (e) SEQ pad as configured.

## 5. Build + deploy + reporting

- Build Release; deploy to GOLD only with Revit closed; verify md5 match; be the last
  writer (GOLD is shared).
- **Honest gating:** report each fix DONE / PARTIAL / BLOCKED with concrete evidence
  (file:line, JSON assertion output, or in-Revit screenshot). No "should work."
- Any design decision (style-pack colours, fire scope, SEQ scope) → STOP and present
  options with a recommendation; do not guess.
- Do NOT merge to `main`. Leave the work on `claude/mep-print-ready` for review; log in
  `docs/CHANGELOG.md` with the no-merge note.

## 6. Constraints

- Worktree isolation; never reset a shared checkout; never clobber a live GOLD deploy.
- Corporate baselines checksum-locked — add new packs/types as `origin:"corporate"`;
  keep existing corporate entries byte-stable unless deliberately changing the baseline.
