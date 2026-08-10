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
