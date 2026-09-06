# MEP Print-Ready — Cross-Check Punch-List Runner

**Autonomous-agent runner.** Fixes the defects a correctness/consistency cross-check
found in the `claude/mep-print-ready` MEP print-readiness work. The MEP *data* work
(packs, rebinds, routing, schedules, D4/D5 logic, cumulative depth) is verified
CORRECT — do not redo it. This runner closes the gaps that make the mask feature
silently non-functional, a false drift, one in-scope data defect, one pack tweak, and
records the out-of-scope tag debt.

---

## 0. Ground truth / environment

- **Repo:** `C:\Dev\STINGTOOLS` — shared by many agents. **Isolate in a git worktree;
  never `reset --hard` a shared checkout; verify branch before any destructive op.**
- **Base branch:** `claude/mep-print-ready` (tip `381764cc7`) — contains all MEP
  print-readiness work + my integration base (tag latch fix, spec binder, title-blocks).
  **Branch FROM THIS:** `git worktree add -b claude/mep-punchlist
  C:/Dev/STINGTOOLS/.claude/worktrees/mep-punchlist claude/mep-print-ready`.
- **Build:** `dotnet build StingTools/StingTools.csproj -c Release
  -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`. Clean baseline = **0 err /
  6 warn**. Verify after every fix.
- **Deploy:** `robocopy StingTools\bin\Release → C:\Dev\STING_PLACEMENT_GOLD`, **only
  when `Get-Process Revit` is empty**, then verify GOLD DLL md5 == your build. GOLD is
  shared — be the last writer.
- **Activation note:** the plugin shows *"not activated"* on the test machine (code
  `ADD3-E01C-3412-14C8-175E`). In-Revit verification gates can't run until a license is
  issued/applied for that machine. Flag to the owner; not a code task.
- **Verified real family names** (from the `.rfa` set on disk, `STING - X Tag` form) —
  use these, never the `STING_TAG_*` underscore form (no `.rfa` matches it):
  `STING - Mechanical Equipment Tag`, `STING - Electrical Equipment Tag`,
  `STING - Plumbing Fixture Tag`, `STING - Plumbing Equipment Tag`,
  `STING - Generic Model Tag`, `STING - Door Tag`, `STING - Window Tag`,
  `STING - Room Tag`, `STING - Structural Column Tag`, `STING - Architectural Column Tag`,
  `STING - Structural Rebar Tag`. **There is NO QR family.**

---

## 1. 🔴 HIGH — segment-mask feature is silently non-functional (fix first)

**Problem:** `TAG_SEG_MASK_TXT` is **Type**-bound to model categories
(`FAMILY_PARAMETER_BINDINGS.csv` + `MR_PARAMETERS.csv` rows: `…,TEXT,Type,…`). But the
produce path writes/reads it on the **instance**: `TokenProfileApplier.cs` FIX-3a does
`ParameterHelpers.SetString(el, ParamRegistry.TAG_SEG_MASK, …)` and
`TagConfig.BuildDisplayTag` reads `GetString(el, …)`. Both go through
`ParameterHelpers.CachedLookup`, which is instance-only (`el.LookupParameter` /
`el.get_Parameter` — no `el.Symbol` / `GetTypeId`). `instance.LookupParameter` cannot
see a Type param → the write returns false (no-op) and the read returns null → **the
mask never applies to produced tags.** Fails closed (no crash), which is why it compiled
and passed every "is it wired?" check.

**Also audit the sibling per-element writes** — the same bug class applies to any
Type-bound param the produce path stamps per-element. Check the binding scope of:
`STING_DISPLAY_MODE` (written per-element in `TokenProfileApplier.cs:111-133`),
`TAG_7_SECTION_VISIBLE_A..F_BOOL` (written per-element ~`:138-146`). For each: if it's
Type-bound and written per-instance without type resolution, it's the same silent no-op.
(Paragraph tiers `PARA_STATE_*` are already handled correctly — `WriteCategoryDepths`
resolves the type element ~`:311`; use that as the reference pattern.)

**Fix — decide per param by whether the datum is per-element or per-type:**
- **Per-element display attributes** — segment mask, display mode, TAG7 section
  visibility: these are *per-tag* choices, so **rebind them as Instance** (recommended).
  Flip `Type`→`Instance` for these params in `FAMILY_PARAMETER_BINDINGS.csv` +
  `MR_PARAMETERS.csv` (+ `MR_PARAMETERS.txt` if it carries a scope flag), then ensure
  `LoadSharedParamsCommand` re-binds on next load (Revit `ReInsert` changes an existing
  binding's instance/type flag). Note the migration: any value previously stored on the
  Type param is dropped, but these are freshly-written display hints — low value.
- **If rebinding is undesirable**, instead resolve the **type** element in BOTH the
  writer (`TokenProfileApplier` FIX-3a) and the reader (`BuildDisplayTag`) the way
  `WriteCategoryDepths` does — but be aware this makes the mask **type-scoped** (every
  instance of a family type shares one mask), which is wrong for per-element masking.
  Prefer rebinding for the mask.

**Gate (in-Revit, 30 s):** place a tag, set a DrawingType SegmentMask, Produce → confirm
`ASS_DISPLAY_TXT` is actually shortened. Also confirm display-mode + TAG7 sections
materialise on Produce.

## 2. 🟠 MED — drift detector emits a permanent false positive

`DrawingDriftDetector.cs:593` reads the mask from the **view**:
`string actual = ReadStringParam(v, ParamRegistry.TAG_SEG_MASK)`. The mask is a
model-category param written to the **element**, so this always reads empty → every
mask-configured DrawingType shows an unhealable
`TOKEN_PROFILE: TAG_SEG_MASK '(empty)' vs profile 'xxxx'` drift.
**Fix:** make the detector read the mask from the same scope the writer uses (the element
— sample the view's tagged elements), or drop the view-based mask drift check entirely.
Keep it consistent with whatever scope decision #1 lands on.

## 3. 🟠 MED — `fm-asset-location-A1-1to100` data defect (in-scope)

In `STING_DRAWING_TYPES.json`, this type's `tagFamilies` has (a) **unspaced keys** and
(b) `STING_TAG_FM_QR`, which is not a real family. Its 3 equipment AutoTags double-fail
(`ResolveTagTypeId` does exact `TagFamilies[rule.Category]` + `FamilyName` match).
**Fix — space the keys and point at real families** (no QR family exists):
```json
"tagFamilies": {
  "Mechanical Equipment": "STING - Mechanical Equipment Tag",
  "Electrical Equipment":  "STING - Electrical Equipment Tag",
  "Plumbing Fixtures":     "STING - Plumbing Fixture Tag",
  "Rooms":                 "STING - Room Tag"
}
```
(If a QR asset-locator tag is genuinely wanted, add a real `STING - FM QR Tag.rfa` to
`Data/TagFamilies/` and reference it by its real `FamilyName` — otherwise use the above.)

## 4. 🟢 FIX-1 amendment — `corp-standard-hvac` must SHOW pipes

Per owner: HVAC includes piping (CHW / HW / condensate / refrigerant), so HVAC plans must
not halftone pipes. In `STING_VIEW_STYLE_PACKS.json`, pack `corp-standard-hvac`, change
`Pipes`, `Pipe Fittings`, `Flex Pipes` from `{visible:true, halftone:true}` to
`{visible:true, halftone:false}` (own-tier, same treatment as Ducts). Leave `Plumbing
Fixtures` halftone (sanitary appliances are plumbing, not HVAC). Leave `corp-standard-elec`
/ `corp-standard-plumb` pipe treatment unchanged.
**Gate:** JSON assert `corp-standard-hvac.vgOverrides.Pipes.halftone == false`.

## 5. ⚪ Follow-up (separate scope) — arch/struct/health tag debt → ROADMAP

The cross-check found auto-tagging broadly broken OUTSIDE MEP (pre-existing, NOT this
run): ~87 key↔category mismatches; **19 `STING_TAG_*` non-existent family refs on 14
arch/struct types**; `STING - Generic Tag` (should be `STING - Generic Model Tag`) on
**22 health types**. Same fix pattern as MEP FIX-2 (spaced keys + real `STING - X Tag`
families — real targets exist: `STING - Door Tag`, `STING - Window Tag`,
`STING - Structural Column Tag`, `STING - Structural Rebar Tag`, etc.).
**Action:** add a `docs/ROADMAP.md` entry — *"Tag-family normalization: arch/struct/health
DrawingTypes still use unspaced/OST_ keys + non-existent `STING_TAG_*` / `STING - Generic
Tag` families; auto-tag broken outside MEP. Fix with the MEP-2 pattern."* — so the
"MEP is fixed" success does NOT imply the whole file is. Do the actual arch/struct/health
rewrite only if explicitly scoped; otherwise leave it recorded.

---

## Do NOT touch — verified correct
FIX 1 packs (except the hvac-pipes tweak in #4) + all 10 rebinds; FIX 2 MEP tagFamilies
(28 types, 0 broken refs); FIX 4 routing (`E/*/SPOOL`+`COORD` first-match) + 3 schedules
+ plumb slot; **D4** numeric tiers (`GetDisplayText`, both paths converge); **D5** all-modes
mask *logic* (correct — only gated by the binding bug in #1); **cumulative depth**
(`for t=1..10`). Re-doing any of these risks regressing correct work.

## Verification gates
- Build 0-err/6-warn after each fix.
- #1: in-Revit mask/display-mode/section materialise on Produce (needs activation).
- #2: produce a mask-configured type twice → no residual `TAG_SEG_MASK` drift.
- #3: `fm-asset-location` — `rule.Category ∈ tagFamilies.keys` for all 3, every value ∈
  real `.rfa` set.
- #4: `corp-standard-hvac.Pipes.halftone == false`.
- Re-run the cross-check script (key↔category alignment + family existence over MEP types)
  → 0 in-scope failures.

## Build + deploy + reporting
- Deploy only Revit-closed, md5-verified, last writer.
- Report each item DONE / PARTIAL / BLOCKED with evidence (file:line, JSON assert, or the
  in-Revit result). No "should work" — #1 specifically must be confirmed in Revit, since it
  compiles clean whether or not it functions.
- The binding rebind (#1) is a schema decision — if unsure whether to rebind vs type-resolve,
  STOP and present the trade-off rather than guessing.
- Don't merge to `main`; log in `docs/CHANGELOG.md` with the no-merge note.
