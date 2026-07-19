# Universal Tag — Status Badge Glyph Building Guide

A step-by-step, Revit-specific guide to **drawing the status-badge glyphs** for the universal
tag: the traffic-light symbols (🟢 / 🟡 / 🔴) for the data gate and QA gate. This covers ONLY
the glyph construction — how to draw them, colour them, set line weight, and the scale question.
The visibility wiring (`VIS_*` params, placement on the tag) is in
`UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md` Part 3.

---

## 0. What you are actually building

You are **not** drawing 6 shapes inside the tag. You build **3 tiny reusable Generic Annotation
families** — one per colour — and nest each one twice (left = data gate, right = QA gate):

| Glyph family | Colour | Shape (recommended) | Meaning |
|---|---|---|---|
| `STING_Badge_Green.rfa` | green | ● circle **with ✓** | pass / complete |
| `STING_Badge_Amber.rfa` | amber | ▲ triangle | in progress / warning |
| `STING_Badge_Red.rfa`   | red   | ■ square (or ✕) | fail / missing |

Why distinct **shapes** and not just three coloured dots: ~8% of men are red-green colour-blind.
Shape + colour together means the badge is still readable in greyscale and by colour-blind users.
If you truly want a pure traffic-light, make all three circles — but shape redundancy is better.

Only **one** glyph per side is ever visible at a time (the `VIS_*` formulas are mutually
exclusive), so all three overlay at the same point.

---

## 1. Does the glyph need a scale parameter? — **NO.** (Read this first.)

**Annotation families live in "paper space."** A Generic Annotation (like a tag) is drawn at its
**true printed size in millimetres** and Revit keeps it that size **on the sheet at every view
scale**. A 2.5 mm glyph is 2.5 mm on the printed A1 whether the view is 1:20, 1:50 or 1:100.

Consequences:
- **Draw the glyph once at final paper size (2.5 mm).** Do not add a scale parameter, scale
  formula, or per-scale visibility. The tag's own text scale tiers do **not** apply to it.
- Do **not** draw it in model units (metres) — annotation families are already mm-on-paper.
- The only thing that changes the printed size is if you *edit the drawn geometry* — so pick the
  size once (below) and commit.

(Contrast: *model* elements scale with the drawing; *annotation* elements do not. Badges are
annotation, so they are fixed-size on paper — exactly what you want for a status symbol.)

---

## 2. Start the glyph family

1. **New family** → template **`Metric Generic Annotation.rft`** (Family Category =
   *Generic Annotation*). Do this **once per colour** (3 families).
2. You get two reference planes crossing at the **origin (0,0)**. The origin is the family's
   insertion point — **centre your glyph on it** so nesting places predictably.
3. Delete the sample note text ("Note text…") if the template includes one.
4. Set **Family Category and Parameters** is already Generic Annotation — leave it.

---

## 3. Draw the shape (geometry)

Use a **Filled Region** for a bold, solid, coloured shape (best legibility at 2.5 mm). All
coordinates below are in **mm from the origin**; final glyph fits in a **2.5 mm** box.

**Create → Filled Region** → draw the boundary → **Finish Edit Mode (✓)**.

| Glyph | How to draw the boundary | Target size |
|---|---|---|
| **Green — circle** | Filled Region → *Circle* tool → centre (0,0), radius **1.25 mm** | ⌀2.5 mm |
| **Amber — triangle** | Filled Region → *Line* tool, 3 lines: apex (0, 1.45) → (−1.25, −0.85) → (1.25, −0.85) → back to apex | ~2.5 mm tall |
| **Red — square** | Filled Region → *Rectangle*, corners (−1.1, −1.1) to (1.1, 1.1) | 2.2 mm |

Optional inner mark (adds shape redundancy — draw AFTER the fill, as thin **white/masking**
lines or a second small filled region in white):
- Green ✓: a check mark inside the circle.
- Red ✕: two crossing lines (or just use the square alone).

Keep it simple: **the solid coloured shape alone is enough**; the inner mark is a nicety.

> If you prefer an outline/line glyph (✓ / ✕ drawn with lines instead of a solid fill), see §6.

---

## 4. Colour the glyph (the important part)

Colour on a **Filled Region** comes from its **Type**, not from a subcategory — which is good,
because a type colour travels into the project and **won't be overridden** by project object
styles.

1. With the filled region selected (or via **Manage → Additional Settings → Fill patterns** and
   the region Type), open the **Filled Region Type** → **Duplicate** → name it e.g.
   `STING Badge Green`.
2. Set:
   - **Foreground Fill Pattern = `Solid fill`** (this is what makes it a solid colour block).
   - **Foreground Pattern Color** = the RGB below.
   - **Background Fill Pattern = `<none>`**.
   - **Masking = unticked** (so it doesn't hide the label text behind it — the badge sits in its
     own reserved corner, not over text).
3. Repeat with a duplicated type per colour.

**Exact RGB values (set in Foreground Pattern Color → Custom):**

| Glyph | R | G | B | Hex |
|---|---|---|---|---|
| Green | 0 | 176 | 80 | `00B050` |
| Amber | 255 | 192 | 0 | `FFC000` |
| Red | 192 | 0 | 0 | `C00000` |

(These are the standard Office traffic-light set — high contrast, print-safe. If your firm has a
brand palette, substitute, but keep green clearly ≠ amber ≠ red in greyscale: green mid, amber
light, red dark.)

---

## 5. Line weight (the boundary)

A filled region has a **boundary line**. For a clean solid glyph you do **not** want a heavy dark
outline competing with the fill.

Choose one:
- **Cleanest — invisible boundary:** while sketching the region, set **Line Style =
  `<Invisible lines>`**. Only the coloured fill shows. **Recommended.**
- **Thin matching edge:** set the boundary **Line Weight = 1** (the thinnest annotation pen,
  ≈0.1 mm) on the Filled Region Type, and a line colour equal to the fill. Gives a crisp edge
  without a dark ring.

Avoid pens 4+ on the boundary — at 2.5 mm a thick outline swallows the shape.

**About Revit line weights:** annotation line weights are **pen numbers 1–16**, each mapped to a
millimetre value in **Manage → Additional Settings → Line Weights → Annotation Line Weights**.
They are **scale-independent** (an annotation pen is a fixed mm on paper), which is consistent
with §1 — nothing here needs scale logic.

---

## 6. Alternative: line glyphs (✓ / ✕ instead of solid shapes)

If you want a drawn check/cross rather than a solid block:
1. **Create → Line** (annotation/symbolic lines), draw the ✓ or ✕ within the 2.5 mm box.
2. Colour via a **Line Style**: **Manage → Additional Settings → Line Styles → New Subcategory**
   → e.g. `STING Badge Green` → set **Line Color** + **Line Weight = 4–6** (≈0.35–0.5 mm, so the
   thin mark reads at small size). Assign the lines to that subcategory.
3. Trade-off: line glyphs are lighter/less punchy than solid fills at 2.5 mm. For a status badge,
   **solid filled shapes (§3–4) read better** — prefer those unless you specifically want outlines.

---

## 7. Print-optional subcategory (do this at nesting, not here)

The badge must be switchable OFF on issued/printed sheets. That is controlled by putting the
**nested badge instances** on subcategory **`STING_TagStatus`** *inside the tag family* (Object
Styles → Generic Annotation → New Subcategory), then hiding that subcategory in print/issue view
templates (VG). You assign the subcategory when you nest (main guide Part 3), not in the glyph
family itself. Keep the glyph families clean.

---

## 8. Save the 3 glyph families

- Save as `STING_Badge_Green.rfa`, `STING_Badge_Amber.rfa`, `STING_Badge_Red.rfa`.
- Keep them next to the tag families (e.g. `Data/TagFamilies/Badges/`) so they travel with the
  master and propagate.

---

## 9. Using them (brief — full wiring in main guide Part 3)

1. In the **tag master** family: **Insert → Load Family** (or Load into Project) the 3 glyph
   families, then **place** them:
   - LEFT/data gate anchor: place Green, Amber, Red **all at the same point** (top-left, fixed —
     never over the reflowing label; see main guide Part 3e).
   - RIGHT/QA gate anchor: place another Green, Amber, Red trio at the top-right point.
   → 6 nested instances total.
2. Select each nested instance → in Properties, associate its **Visible** parameter (the small
   `=` button) with the matching `VIS_*` Yes/No family parameter (main guide Part 3b).
3. Assign each nested instance's **Subcategory = `STING_TagStatus`** (for print control, §7).

Because only one `VIS_*` per side is ever true, exactly one glyph shows per gate.

---

## 10. Glyph-building checklist

- [ ] 3 Generic Annotation families created (green / amber / red).
- [ ] Each glyph centred on the family **origin (0,0)**.
- [ ] Shape drawn as a **Filled Region**, fits a **2.5 mm** box (⌀2.5 circle / 2.5 triangle / 2.2 square).
- [ ] **NO scale parameter** — annotation is paper-space, fixed size on sheet.
- [ ] Filled Region Type = **Solid fill**, Foreground Pattern Color = the exact RGB (00B050 / FFC000 / C00000), Masking OFF.
- [ ] Boundary = **`<Invisible lines>`** (or pen 1 matching the fill).
- [ ] Distinct shapes per colour (accessibility) — or all circles if you want a pure traffic light.
- [ ] Saved as 3 `.rfa` in `Data/TagFamilies/Badges/`.
- [ ] (At nesting) 6 instances placed at fixed L/R anchors, `Visible` → `VIS_*`, subcategory `STING_TagStatus`.
