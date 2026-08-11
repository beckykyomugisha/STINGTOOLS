# BOQ take-off — findings against the four open items

Measured 2026-08-10 against `claude/kibale-integration` (24 `lookup()` formulas after the
G-15 deletion). Reads only — no code changed, to avoid colliding with the deploy running on
that branch.

---

## New: a third class of dual owner the sweep did not look for

G-28 searched for **formula-vs-C#** pairs and found 15. There is a third class:
**formula-vs-formula**. Two formulas computing the same physical quantity, differently.

### G-34 · 🔴 P0 · Two block-count formulas disagree by 16 %

```
CST_CALC_BLOCKS_NR   = CST_S_MAS_NET_AREA_SQ_M  × lookup(BLOCK, size, BLOCKS_PER_M2) × 1.03
CST_S_MAS_BLOCKS_NR  = CST_S_MAS_WALL_AREA_SQ_M × lookup(BLOCK, size, BLOCKS_PER_M2)
                                                × (1 + CST_S_MAS_WASTAGE_FCT_PCT / 100)
```

They differ on **two** axes at once:

| | `CST_CALC_BLOCKS_NR` | `CST_S_MAS_BLOCKS_NR` |
|---|---|---|
| area basis | `NET` — openings deducted | `WALL` — gross |
| waste | hardcoded `1.03` | parameter `CST_S_MAS_WASTAGE_FCT_PCT` |

Worked case — 50 m² gross wall, 6 m² of openings, BLOCK `400x200` (12.5 blocks/m², verified in
`MATERIAL_LOOKUP.csv`), wastage parameter 5 %:

```
CST_CALC_BLOCKS_NR   = 44.0 × 12.5 × 1.03  = 566 blocks
CST_S_MAS_BLOCKS_NR  = 50.0 × 12.5 × 1.05  = 656 blocks
                                    divergence 15.8 % — 90 blocks on ONE wall
```

Across seven cottages that is a four-figure block quantity, and **neither formula flags
anything** — both resolve their lookup cleanly. The `DEFAULTED` flag from G-28 Part 1 will not
catch this, because nothing is defaulted. Two correct lookups, two different answers.

### The sequencing trap — fixing G-32 alone creates the bug

G-32 records that `CST_S_MAS_BLOCKS_NR` is **bound to nothing** — zero rows, computing into the
void. So today only `CST_CALC_BLOCKS_NR` runs and there is **no live divergence**.

**The moment G-32 is closed by binding it, two block counts appear on every wall and disagree
by 16 %.** G-32 must not be fixed before G-34 is resolved, or the fix ships the defect.

Resolution is a decision, not a cleanup — which basis is correct:

- **Net + hardcoded waste** understates if the wastage parameter is meant to be project-tunable.
- **Gross + parameter waste** overstates by the opening area, which on a cottage with `whb`, `wc`,
  `sh`, `D5`, two en-suites and a duct riser is not a rounding error.

Correct answer for a priced bill is **net area with a project-tunable waste parameter** — neither
formula as written. Blocks are not bought for the door openings, and 3 % should not be frozen in
a formula when a wastage parameter already exists.

---

## Item 2 — the DEFAULTED rate cannot be fixed from here

It is not a code defect. The flag shipped and is honest; the number requires it to **run against
real geometry**. §7b of the operator session is the only source. Until then nobody knows whether
2 quantities are assumed or 26, and that number gates G-30 and the sequencing of the remaining
comparisons.

**One correction to the G-28 brief:** it asked for the C# resolution vocabulary to be *lifted, not
redesigned*. It already exists and is richer than the three states specified —
`CompoundTakeoffBuilder.cs:418` separates `Empty` (parameter truly unset → project default) from
`:430` `Unmatched` (key had a value, no row matched → DEFAULT), and surfaces both at `:227-228`:

```
UNMATCHED→DEFAULT (check value): …
param empty→project default: …
```

That distinction matters more than MEASURED/DEFAULTED/UNRESOLVED, because *unmatched* means
someone typed a value the tables do not know — a data-entry error — while *empty* means nobody
supplied one. They need different remedies. Lift this, do not flatten it.

---

## Item 3 — the 11 with no counterpart: two are broken before comparison matters

Comparison cannot help these; only a hand take-off against real geometry can. But reading them
surfaced two that are defective on their face and can be fixed without any geometry:

**`BLE_FINISH_TILE_QUANTITY_NR`** divides by `(Tile_Width * Tile_Height / 1000000)`.
`Tile_Width` and `Tile_Height` carry **no STING prefix** and are not in `MR_PARAMETERS.txt` —
they look like family-local parameters that will not resolve on a project element. If they
resolve to zero the formula divides by zero; if they resolve to nothing it fails silently.
**Verify before trusting any tile quantity.**

**`BLE_FINISH_GROUT_WEIGHT_KG`** keys on `BLE_TILE_JOINT_WIDTH_MM` — a **numeric** parameter used
as a lookup key. Whether `3`, `3.0` and `3 mm` all match one row depends on the resolver's
coercion. This is the single highest default-risk key in the set, and grout is a finishes
quantity on a project whose finishes are the product.

`CST_CALC_PRIMER_LITERS` uses the literal `PRIMER` as its key rather than a parameter. That is
**deliberate and correct** — primer is a fixed product, not a variant — and should not be
"fixed".

---

## Item 4 — screed, skirting, floor area

Unchanged and correctly out of scope for a reads-only pass. One note that reduces the work:
the register already establishes a `SCREED` table exists in `MATERIAL_LOOKUP.csv`
(`STANDARD`/`HEAVY_DUTY`/`DEFAULT`, keyed on `BLE_SCREED_TYPE_TXT`), so this is a parameter, a
binding and a take-off path — not a new table.

`BLE_FLR_AREA_SQ_M = ASS_ROOM_AREA_SQ_M` remains wrong: a floor runs under partitions and a room
can sit on more than one floor element. It should read the Floor element's own area.

---

## Order these in

1. **G-34 before G-32.** Binding the second block formula without resolving the basis ships a
   16 % divergence.
2. **§7b for the DEFAULTED rate.** Gates everything downstream.
3. **The two broken finishes formulas** (`TILE_QUANTITY`, `GROUT_WEIGHT`) — fixable now, no
   geometry needed.
4. Then the 14 comparisons, surface by surface.

---

# G-8 — ANSWERED, in Revit, on KNP26

Run 2026-08-10 against `KNP26-ACE-ZZ-ZZ-M3-A-0001.rvt` after Load Shared Parameters,
via a Dynamo CPython3 node iterating `doc.ParameterBindings`.

```
declared names in MR_PARAMETERS.csv : 3392
bound params checked                : 3033
bound params with no declaration    :   22
declared BOTH Type and Instance     :    0
MISMATCHES                          : 2654      ← 87 % of everything bound
```

**Every single mismatch runs one way: declared `Type`, actually bound `Instance`. Zero the
other way.** The arithmetic closes exactly — 2,654 mismatched (all declared Type) + 379
matched (all declared Instance) = 3,033 checked. There is no drift here and no partial
state: *every declared-Type parameter in the project is bound as Instance.*

## Cause — two binders that disagree

| | Behaviour |
|---|---|
| `LoadSharedParamsCommand` — **what the operator runs** | calls `NewInstanceBinding` only. 13 call sites, no `NewTypeBinding` anywhere in the file. |
| `DataPipelineCommands.cs:792` | honours the column — `? NewTypeBinding(catSet) : NewInstanceBinding(catSet)`, reading `Binding_Type` at `:649` |
| `TemplateManagerCommands.cs:2813` | also creates a `TypeBinding` |

So `Binding_Type` is live in one path and **decorative in the path everybody actually uses**.
The 2,997 `Type` declarations describe a binding that the normal command has never produced.

**G-8 is therefore a documentation defect, not a silent-write bug** — but a far more
consequential one than a stale column, because of what it has already caused.

## The consequence that matters: K-16's diagnosis is invalid

K-16 was raised as a P0 and closed with this mechanism:

> `ASS_PRODCT_COD_TXT` and `ASS_SYSTEM_TYPE_TXT` are **Type-bound** (19 rows each, all Type)
> but were read off the **instance** via `Element.LookupParameter`, which is instance-scoped.
> Both returned empty on every element, so the PROD-code and system-type rate passes never
> fired and everything fell through to the single category rate — a fire door and a cupboard
> door price identically.

Measured in the live model:

```
ASS_PRODCT_COD_TXT     declared=Type      actual=Instance
ASS_SYSTEM_TYPE_TXT    declared=Type      actual=Instance
MAT_CODE               declared=Type      actual=Instance
```

**Both are Instance-bound.** `Element.LookupParameter` finds them perfectly well. The stated
mechanism cannot be what made them empty — and the distinction K-16 drew against `MAT_CODE`
("Instance-bound and already correct") is not a distinction at all: all three are Instance.

The `ReadInstanceThenType` fix is harmless and more robust, but it did not address the actual
cause. **The most likely real cause is that the values were simply empty** — the tagging
pipeline had not populated them — which is a different defect with a different fix.

**So the fire-door/cupboard-door rate defect must be treated as OPEN until re-tested against
elements that genuinely carry a PROD code.** A P0 was diagnosed, fixed and closed from a
declaration that is wrong for 87 % of parameters.

## Which side is right

Do **not** "fix" the bindings to match the declaration. Instance is almost certainly correct:
`ASS_TAG_1_TXT` is the unique 8-segment asset tag and *must* be per-instance — Type-binding it
would give every door of one type the same asset tag. The same holds for most of the `ASS_*`
identity set.

The declaration is the wrong side. Correct or delete the `Binding_Type` column, and reconcile
the two binders so one behaviour is authoritative. Re-binding 2,654 parameters as Type on live
projects would be destructive and would break the asset register.

## Also worth recording

- **22 bound parameters have no declaration at all** — bound by something that does not go
  through `MR_PARAMETERS.csv`.
- **Nothing is declared as both Type and Instance**, so the multi-category rows in the CSV are
  at least internally consistent.

---

## G-52 · ❌ WITHDRAWN · The two "dead" room buttons were never dead

**This entry was wrong and the tool that produced it was wrong.** Recorded here rather than
deleted, because the failure is more instructive than the finding would have been.

`Tagging_RoomTagApply` and `Bedroom` were reported as having no handler case. Both are live.
`Tagging_RoomTagApply` is dispatched at `StingDockPanel.xaml.cs:719` — it reads the anchor and
leader radios and dispatches two concrete tags. It works.

The check that found them, `tools/validate_dispatch_wiring.py`, had two defects:

1. `collect_cases()` globbed **non-recursively** while `collect_buttons()` globbed recursively, so
   it never saw `UI/Plumbing/StingPlumbingCommandHandler.cs` or
   `UI/Sustainability/StingSustainabilityCommandHandler.cs`. Every `Plumb_*`, `Plumbing_*` and
   `Sustain_*` button was reported dead while correctly wired.
2. It assumed **one** dispatch surface. There are three: the `*CommandHandler.cs` switches,
   code-behind "suite runner" interception in `Cmd_Click` (`cmdTag == "…"`), and the extracted
   per-tab modules in `UI/Modules/*CommandModule.cs`, which register rather than switch.

Corrected count: **BUTTON WITHOUT CASE = 0**. All 117 were false positives.

**The lesson is the one this file has been repeating all along, turned on its author.** A gate that
mismeasures in the alarming direction is not a safe failure — it manufactures work, and if it is
believed it gets written into a register and a playbook as fact, which is what happened here.
Counting a working button as broken is the same defect as missing a broken one.

The gate is worth keeping now that it measures all three surfaces, and it is baselined at 0, so any
genuinely dead button added from here fails CI immediately.

### The related documentation defect

There is **no `Describe` command** in STING — no case, no button, no command class; the word
appears only in unrelated files. Operators reach for it because two real things sit near that idea
and neither is named "describe":

- **Type Description** — the long name behind the type code (`SAT` → *"Supply Air Terminal,
  210×60"*). Revit's own field, mirrored to `ASS_DESCRIPTION_TXT`.
- **TAG7 narrative** — the multi-sentence paragraph built in TAG STUDIO from A–F sub-sections.

Documented in playbook Part 3E so the next operator does not go looking for a command that was
never built.

---

## G-53 · 🟠 P1 · The tier gates are not what makes a tag ten lines

`TagStudio_SetTierDefaults` reported `Gate values written: 0` across 16 tag family types. That
looked like a broken command. The per-gate table shows it was not:

```
parameter               want  wrote  same  absent
TAG_PARA_STATE_1_BOOL    ON     0      0     16
TAG_PARA_STATE_2_BOOL    ON     0      0     16
TAG_PARA_STATE_3_BOOL    OFF    0      0     16
TAG_PARA_STATE_4..10     OFF    0     16      0
TAG_WARN_VISIBLE_BOOL    ON     0      0     16
```

**Tiers 4–10 are already OFF on every type** — column `same`, nothing to write. **Tiers 1, 2, 3 and
the warning row are `absent` from every type** — the type does not carry them, so there is nothing
to set.

The command wrote zero because zero needed writing. It is correct.

### What this changes

The working theory has been *"the gates are split — 1–3 instance-side, 4–10 type-side"*, and the
remedy was to set the type-side ones. That remedy is now known to be a no-op: **tiers 4–10 were
already off and the tag still renders ten lines**, so those rows are not driven by their gates.

Restated: **turning tier gates off does not shorten the tag.** Either the label rows for tiers 1–3
are not gated at all and render unconditionally, or they are gated by an instance parameter that
the placed tag does not carry.

### The check that separates those two — operator, two minutes

Select a **placed** tag and read its **instance Properties** (not Edit Type):

| What you see | What it means |
|---|---|
| `TAG_PARA_STATE_1/2/3_BOOL` present | gates are instance-side; depth is per placed tag and a command can set them |
| absent | the rows are **ungated** — no parameter anywhere suppresses them, and shortening the tag means editing the label rows in the Family Editor |

Everything about tag depth waits on that answer, including whether
`UNIVERSAL_TAG_LABEL_BUILD_SHEET.md`'s depth design was ever implemented.

### Related

An earlier note claimed a `_T3` type would render identically to `_T2` because the universal master
carries no T3 rows. That claim and this one are about the same underlying question — whether depth
is gated at all — and should be resolved together rather than as two entries.
