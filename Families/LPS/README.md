# STING — Lightning Protection Family Library

Drop zone for the 20 Lightning Protection `.rfa` families STING needs.

## Two ways to populate this folder

1. **Author from scratch** — follow `AUTHORING_GUIDE.md`. The spec for
   every family (parameters, formulas, geometry notes, subcategories)
   is in `LPS_FAMILY_INVENTORY.json`. Use Family Editor, save into the
   right tier folder, run STING's `FamilyParamCreator` to stamp the
   shared parameters.

2. **Stamp vendor families** — download `.rfa` files from DEHN / OBO /
   nVent / Furse / Indelec, drop them into the right tier folder, run
   `FamilyParamCreator` to inject STING's required ELC_LPS_* shared
   parameters. Fastest route — most vendors ship Revit families that
   already cover the geometry.

## Tier folders (created on demand as you author / drop families)

```
1_AirTermination/   — Franklin rods, mesh tape, mesh nodes, ESE, mast
2_DownConductors/   — bare + concealed runs, test clamps, roof penetrations
3_Earth/            — earth rods, plates, ring + foundation earths, MEB, pit
4_Bonding/          — bonding bars, conductor straps, spark gaps
5_SPD/              — Type 1 / 2 / 3 / Combined surge protective devices
```

## Discovery

`LpsEngine.CollectLpsFamily()` walks every `.rfa` under this folder
recursively. Once a family is placed in a project, STING's
`LpsElementIndex` (5-min cached) picks it up automatically — no
project-level registration needed.

## Conformance

Before deploying any authored family into production, run STING's
Family Conformance Checker (Tags tab → `FamilyConformanceCheck`). It
validates parameter binding, subcategory naming, connector presence,
and produces a 100-point score. PASS ≥ 85 = production-ready.

## Minimum viable set

For a basic Class II BS EN 62305 project you need only **8 families**:

1. STING_LPS_AirTerminal_Franklin
2. STING_LPS_DownConductor_Bare
3. STING_LPS_TestClamp
4. STING_LPS_EarthRod
5. STING_LPS_MainEarthBar
6. STING_LPS_BondingStrap
7. STING_LPS_SPD_Type1
8. STING_LPS_SPD_Type2

The other 12 cover edge cases (ESE for French / Spanish projects,
foundation earth for new builds, isolating spark gaps for cathodic
protection, etc.).

See `AUTHORING_GUIDE.md` § 12 for the recommended authoring order.
