# STING — Lightning Protection Family Authoring Guide

End-to-end instructions for authoring the 20 LPS families STING needs.
Pair this guide with `LPS_FAMILY_INVENTORY.json` (the machine-readable
parameter / formula spec consumed by `FamilyParamCreator`).

You author the **3D geometry** in the Family Editor; STING stamps the
**shared parameters** for you via `FamilyParamCreator`. No need to
type GUIDs by hand.

---

## 0. Folder layout (where to drop your authored .rfa files)

```
Families/LPS/
├── README.md                            (overview)
├── AUTHORING_GUIDE.md                   (this file)
├── LPS_FAMILY_INVENTORY.json            (spec for FamilyParamCreator)
├── 1_AirTermination/
│   ├── STING_LPS_AirTerminal_Franklin.rfa
│   ├── STING_LPS_AirTerminal_MeshTape.rfa
│   ├── STING_LPS_AirTerminal_MeshNode.rfa
│   ├── STING_LPS_AirTerminal_ESE.rfa
│   └── STING_LPS_LightningMast.rfa
├── 2_DownConductors/
│   ├── STING_LPS_DownConductor_Bare.rfa
│   ├── STING_LPS_DownConductor_Concealed.rfa
│   ├── STING_LPS_TestClamp.rfa
│   └── STING_LPS_RoofPenetration.rfa
├── 3_Earth/
│   ├── STING_LPS_EarthRod.rfa
│   ├── STING_LPS_EarthPlate.rfa
│   ├── STING_LPS_RingEarth.rfa
│   ├── STING_LPS_FoundationEarth.rfa
│   ├── STING_LPS_MainEarthBar.rfa
│   └── STING_LPS_EarthPit.rfa
├── 4_Bonding/
│   ├── STING_LPS_BondingBar.rfa
│   ├── STING_LPS_BondingStrap.rfa
│   └── STING_LPS_SparkGap.rfa
└── 5_SPD/
    ├── STING_LPS_SPD_Type1.rfa
    ├── STING_LPS_SPD_Type2.rfa
    ├── STING_LPS_SPD_Type3.rfa
    └── STING_LPS_SPD_Combined.rfa
```

The LPS panel's `Load model` button + `LpsEngine.CollectLpsFamily()`
search every `.rfa` recursively from the project's family library — no
explicit registration needed once dropped into the right tier folder.

---

## 1. Universal authoring workflow

For **every** family below, the steps are:

1. **Start Revit → File → New → Family** with the correct template
   (`Metric Electrical Equipment.rft`, `Metric Generic Model.rft`, or
   `Metric Generic Model line based.rft` — see the `template` field in
   the inventory JSON).
2. **Author the geometry** using the geometry notes from the inventory.
   Use **Reference Planes / Reference Lines** as the framework; add
   solid extrusions, sweeps, revolves locked to those references.
3. **Create the listed subcategories** (Family Categories and
   Parameters → Subcategories). Use the line weight + RGB colour from
   the inventory so STING filters show consistent styling.
4. **Add type parameters** from the inventory using
   `Family Types → Add` (set the storage type from `type:`, paste the
   formula if `formula:` is non-empty, set the default `value`).
5. **Save** the .rfa into the tier subfolder.
6. **Run STING → LPS Panel → Settings → "Stamp LPS Parameters into
   Family"** (or use the legacy `FamilyParamCreator` ribbon button) —
   it injects the shared parameters from `MR_PARAMETERS.txt` automatically.
7. **Re-save** the .rfa after STING finishes.

⚠ **Critical**: Step 6 must run **after** Step 4 — STING reads which
type parameters already exist and only adds the missing shared
parameters. If you do it the other way the type-parameter formulas
may reference shared params that don't yet exist.

---

## 2. Reference template files (Revit 2025+)

| Template (`.rft`)                          | Used by                              |
|---|---|
| `Metric Electrical Equipment.rft`          | Point-hosted equipment families      |
| `Metric Generic Model.rft`                 | Static point-hosted models           |
| `Metric Generic Model line based.rft`      | Line-driven (sweeps along host line) |

Avoid `Metric Generic Annotation.rft` for any LPS family — that creates
a 2D symbol, not a 3D element. STING needs 3D real geometry.

---

## 3. Subcategory + colour conventions

| Subcategory               | Line Weight | RGB             | Used by                  |
|---|---|---|---|
| `STING_LPS_AT_*`          | 3-4         | 230,81,0  (orange) | All air-termination       |
| `STING_LPS_DownConductor` | 5           | 211,47,47 (red)    | All DCs                   |
| `STING_LPS_DownConductor_Concealed` | 4 dashed | 211,47,47    | DC concealed runs only    |
| `STING_LPS_EarthRod / Plate / etc.` | 3-4   | 46,125,50 (green)  | All earth electrodes      |
| `STING_LPS_MEB / BondingBar / BondingStrap` | 3-4 | 255,193,7 (amber) | All bonding network |
| `STING_LPS_SPD`           | 3-4         | 106,27,154 (purple) | All SPD units            |
| `STING_LPS_TestClamp`     | 3           | 0,121,107 (teal)  | All test joints           |

The colours match the corresponding entries in
`STING_AEC_FILTERS.json` so views with the `corp-elec-lps` style pack
auto-colour-code the elements without per-family work.

---

## 4. Per-family checklist

For each family below, the checklist is:

- ☐ Open template `.rft` listed in the inventory
- ☐ Set family category (only needed for Generic Model templates)
- ☐ Author the geometry (notes in inventory)
- ☐ Create subcategories listed
- ☐ Add type parameters (with formulas where given)
- ☐ Set the family's instance parameters that the inventory marks
  `"binding": "shared"` (STING fills these via `FamilyParamCreator`)
- ☐ Add a Reference Plane "Insertion Point" at the placement origin
- ☐ Save into the correct tier folder
- ☐ Run STING → FamilyParamCreator

---

## 5. Tier-specific notes

### Tier 1 — Air Termination

- **Franklin Rod** is the workhorse — author this one first.
- **Mesh Tape** is line-based so it sweeps a rectangular cross-section
  along the host line. Use a **swept solid** with a Reference Line as
  the path and the cross-section locked to type parameters
  `TapeWidth_mm` × `TapeThickness_mm`.
- **ESE Air Terminal** is the NF C 17-102 alternative — only ship if
  your projects span French / Spanish jurisdictions. The protection-
  radius formula is `r = h + 1.06 × Δt` (already in the inventory).
- **Lightning Mast** is just a tall version of the Franklin Rod —
  consider building one parametric family with `TotalHeight_m` as a
  type parameter rather than two separate families.

### Tier 2 — Down Conductors

- Bare and concealed are the same geometry topology — the difference
  is the **subcategory + line style**. Author one master family, then
  type-switch between bare and concealed.
- **Test Clamp** is the mandatory inspection point between every DC
  and earth electrode (Annex E §E.4). Author it as a small bronze
  pad with 2 bolt heads — it should be visible at 1:50 plan scale.
- **Roof Penetration** is the weatherproof seal where the DC drops
  from a roof to a wall/column.

### Tier 3 — Earth Termination

- **Earth Rod** has a nominal-resistance formula assuming 100 Ω·m
  soil; for a real project, override the measured value in
  `ELC_LPS_EARTH_RESISTANCE_OHM`.
- **Ring Earth** and **Foundation Earth** are line-based (sweep a
  cable along a closed loop around the building perimeter).
- **Main Earth Bar (MEB)** is the central node of the bonding
  network — make it visually distinct (amber subcategory).

### Tier 4 — Bonding

- **Bonding Strap** is line-based; use insulated-cable cross-section.
- **Isolating Spark Gap** is rare — only needed where cathodic
  protection or dielectric pipe couplings interrupt the bonding
  network.

### Tier 5 — SPD

- All four SPD variants share the same general geometry (DIN-rail
  modular box). The differences are **type parameters** (Iimp, In, Up)
  and **box dimensions** (Type 1 / Combined = 4-module; Type 2 = 4-
  module; Type 3 = 2-module).
- **Electrical connectors**: each SPD needs a power connector with
  the right system + voltage + phase configuration (see inventory).
  Without it the panel-schedule + voltage-drop engines can't pick
  up the device.

---

## 6. Formula quick-reference

These are the formulas the inventory bakes into type parameters.
Paste them verbatim into the `Formula` column in Family Types:

| Family | Type parameter | Formula |
|---|---|---|
| Mesh Tape | `CrossSection_mm2` | `TapeWidth_mm * TapeThickness_mm` |
| Mesh Tape | `MassPerMeter_kg` | `CrossSection_mm2 * 8.96 / 1000` |
| Down Conductor Bare | `CrossSection_mm2` | `TapeWidth_mm * TapeThickness_mm` |
| Down Conductor Bare | `MassPerMeter_kg` | `CrossSection_mm2 / 1000000 * Density_kgM3` |
| ESE Air Terminal | `_ProtectionRadius_m` | `TipHeight_m + 1.06 * AdvanceTime_us` |
| Earth Rod | `_NominalResistance_ohm` | `100 / (2 * 3.14159265 * RodLength_m)` |

Revit Family Editor formula notes:
- `*` for multiplication, `/` for division, `+` for addition
- Wrap any negative number in parens: `(-1)` not `-1` after subtraction
- Family Types panel highlights formula errors in red — fix before save
- Length-typed parameters auto-handle unit conversion — `1000` in mm
  internally stores 1 m, then displays as "1000 mm" when the project
  is metric

---

## 7. Connector authoring (SPDs only)

SPDs need at least one electrical connector so the panel-schedule +
voltage-drop engines can trace power through them.

For each SPD variant:

1. Family Editor → **Create → Electrical Connector**
2. Place on the back face of the SPD body
3. Set parameters:
   - **System**: Power - Balanced (Type 1 / 2 / Combined) OR Power -
     Other (Type 3 if line-to-neutral)
   - **Voltage**: 400/230 V (3-phase) or 230 V (single-phase)
   - **Number of Poles**: 4 (3+N) for Type 1/2/Combined; 3 (L+N+PE)
     for single-phase Type 3
   - **Apparent Load**: 0 VA (SPDs are passive when not firing)

If the SPD has separate "Signal" terminals for status (alarm contact),
add a second connector typed `Communication`. Optional but useful for
projects with BMS integration.

---

## 8. STING shared-parameter injection (FamilyParamCreator)

After you save the family with type parameters + geometry, run STING's
**Family Parameter Creator** to inject the required shared parameters.

There are two ways:

### Option A — One-by-one (small batch)

1. **In Revit (with the family open in Family Editor):**
   Ribbon → STING Tools → Tags → **Family Parameter Creator**
2. The dialog detects the family category and lists the required
   ELC_LPS_* shared parameters from `LPS_FAMILY_INVENTORY.json`
3. Click **Inject** → STING binds the params (instance vs type per the
   inventory) and saves

### Option B — Batch (whole library)

1. Save every family into the right tier folder under `Families/LPS/`
2. Ribbon → STING Tools → Temp → **Batch Family Parameter Stamper**
3. Point to `Families/LPS/` → STING walks every `.rfa`, opens it
   read-only, injects the params, saves, closes

---

## 9. Conformance check (after authoring)

Before deploying authored families into a project library, run STING's
**Family Conformance Check** (Tags tab → `FamilyConformanceCheck`):

- ☐ Required shared parameters bound by GUID (not just name)
- ☐ Tag-style matrix `TAG_*_BOOL` parameters present
- ☐ Subcategories use the canonical names listed in section 3
- ☐ Connectors present (SPDs only)
- ☐ Loads cleanly into a test project

The conformance command writes a CSV report to
`<project>/_BIM_COORD/` flagging any family that scores < 85/100.
PASS ≥ 85 means production-ready.

---

## 10. Common authoring pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Wrong family template | Element doesn't show on LPS panel | Re-create from the template named in the inventory |
| Type parameter named differently from inventory | Formulas fail or panel can't read values | Match the inventory name exactly (case-sensitive) |
| Forgot subcategory | Element shows in default colour | Family Categories → Subcategories → Add, then assign in element properties |
| Shared parameter missing | Schedule shows empty columns | Run FamilyParamCreator AFTER adding type params |
| Element category set to Generic Annotation | Element invisible in 3D | Family Categories → change to Electrical Equipment or Generic Models |
| Line-based family missing Reference Line | Geometry doesn't follow host line | Create a Reference Line from the two endpoints and lock the sweep path to it |
| SPD missing electrical connector | Panel schedule + voltage drop can't trace through SPD | Add a connector with the right system + voltage |

---

## 11. Geometry workmanship tips

- **Use Reference Planes for every dimension** — never hard-code
  values into extrusions. This lets type-parameter changes flex the
  geometry.
- **Lock dimensions to parameters** via the EQ / padlock UI in the
  Modify panel. Unlocked dimensions silently revert after a type
  switch.
- **Keep the family lightweight** — under 1 MB ideally. Each LPS
  family should load in < 200 ms.
- **Add a "Side View" + "Front View" detail level** if your geometry
  is complex — use the Visibility Settings on each extrusion to hide
  full 3D in Fine, swap for simplified 2D in Medium/Coarse.

---

## 12. Suggested authoring order

1. **Franklin Rod** — the simplest geometry; gets you familiar with
   the parameter-injection workflow.
2. **Test Clamp** — small, useful immediately for inspection schedules.
3. **Earth Rod** — second-simplest geometry; pairs with the test
   clamp for a full earth-termination point.
4. **Main Earth Bar (MEB)** — central node; once authored you can
   stamp `ELC_LPS_BOND_TYPE_TXT = "DIRECT"` on every Cu strap that
   lands on it.
5. **Down Conductor Bare** — first line-based family; the swept-solid
   pattern repeats for Mesh Tape, Ring Earth, Bonding Strap.
6. **SPD Type 1, 2, 3, Combined** — author one master Type 1 then
   duplicate-and-edit for the other three (most params + box are
   shared).
7. **Mesh Tape + Mesh Node** — completes the air-termination palette.
8. **Concealed DC, ESE, Lightning Mast, Bonding Strap, Spark Gap,
   Foundation Earth, Earth Plate, Earth Pit, Roof Penetration,
   Bonding Bar** — fill in the rest as projects need them.

Don't try to author all 20 in one sitting. The minimum viable set for
a Class II project is:
**Franklin Rod + Bare DC + Test Clamp + Earth Rod + MEB + Bonding
Strap + SPD T1 + SPD T2** — 8 families.

---

## 13. Vendor families (alternative path)

If you don't want to author from scratch, you can stamp STING params
into vendor-supplied families:

- **DEHN family library**: https://www.dehn.com/en/dehn-bim
- **OBO BIM**: https://www.obo.global/en/services/bim
- **nVent ERICO**: https://www.erico.com/category.asp?category=R142
- **Furse / ABB**: https://new.abb.com/products/earthing-lightning-protection

Download `.rfa` files → place in the right tier folder → run
`FamilyParamCreator` → STING injects the required ELC_LPS_* params
into the vendor family without touching their geometry.

This is the fastest route — typical vendor families are already
authored to a high standard, you just need STING's parameter overlay.

---

## 14. Testing checklist

After authoring (or stamping vendor families), test each one:

- ☐ Drop into a blank test project
- ☐ Place an instance — does it appear in the right Revit category?
- ☐ Open the **STING LPS panel** → click **Load model** — does the
  instance appear in the right tab grid?
- ☐ Run **Lps_Compliance** — does it pass / report a sensible message?
- ☐ Try the **3D Coverage** command — does the family register?
- ☐ Export to **IFC** — does the BS EN 62305 pset land on the IFC
  element?
- ☐ Check the **STING_AEC_FILTERS.json** filter renders the element
  in the right colour on a view using the `corp-elec-lps` style pack

If all 7 checks pass → the family is production-ready.

---

## 15. Maintenance

When BS EN 62305 / IEC 62305 is revised (last full revision: 2024):

1. Check what changed in `STING_LPS_CLASSES.json` (class thresholds,
   mesh sizes, conductor cross-sections)
2. If type parameters changed (e.g. minimum cross-section), update
   the family default values
3. Run conformance check again to confirm no regression

---

**Author**: STING Tools — Lightning Protection family inventory
**Version**: 1.0
**Last updated**: 2026-05
**Spec**: `LPS_FAMILY_INVENTORY.json`
**Standard**: BS EN 62305-3 (2024) / IEC 62305-4 (2024) / IEC 61643-11
