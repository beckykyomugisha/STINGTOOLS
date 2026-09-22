# The 60 "undeclared" tag families were declared all along

**Status: withdrawn and replaced by a parser fix.** 2026-09-21.

This document began as a proposed mapping of 60 tag families to Revit
categories, drafted because `Fix Categories` reported them
`NO-DECLARATION`. A line-by-line review killed it. The mapping is not
here any more, because publishing 60 opinions next to 60 facts is how
the opinions end up getting applied.

## What was actually wrong

The tag config is not one format. It grew two, and the parser learned one.

**The prose dialect** — `ARCH` / `GEN` / `MEP` / `STR` and their
`_DesignConstruction` twins:

```
Tag Family #7: STING - Air Terminal Tag
TAG7: HVC_TAG_7_PARA_AT_TXT  •  Category: Air Terminals
```

**The row dialect** — `HEALTH` and `HEALTH_DesignConstruction`, written by the
Healthcare Pack, with the category in the **fourth field**:

```
TAG_FAMILY,STING - Clinical Room Tag,H,Rooms,1,CLN_ROOM_CLASS_TXT,Clinical room class label
```

Measured across the ten config files:

| Dialect | ARCH/GEN/MEP/STR (8 files) | HEALTH (2 files) |
|---|---:|---:|
| `Tag Family #N:` | 311 | **0** |
| `TAG_FAMILY,` | **0** | 116 |

`TagCategoryResolver.ParseOne` understood only the first. Every healthcare
family therefore resolved to *"no Category declared in
`STING_TAG_CONFIG_v5_0_*.csv`"* — **a confident report of absence produced by a
reader that could not see the data.** The families were declared twice over: in
that CSV, and in `TagFamilyCreatorCommand.HealthcareVariantFamilies`, the table
that minted the `.rfa` files in the first place. The two agree on all 58.

## What the mapping would have cost

The review checked the draft against the engines rather than against intuition,
and found **17 of 60 rows wrong**. Four of them were load-bearing:

| Family | I proposed | The code requires | Where |
|---|---|---|---|
| Medical Gas Terminal Unit | Medical Equipment | **Plumbing Fixtures** | `Core/MedGas/MgasNetwork.cs:88-95` |
| Area Alarm Panel | Electrical Equipment | **Plumbing Fixtures** | same |
| Master Alarm Panel | Electrical Equipment | **Plumbing Fixtures** | same |
| Zone Valve Box | Mechanical Equipment | **Pipe Accessories** | `MgasNetwork.cs:86-87` |

`MgasNetwork.ClassifyRole` switches on `BuiltInCategory`. It recognises `TU`,
`AAP` and `MAP` **only** under `OST_PlumbingFixtures`, and `ZVB` **only** under
`OST_PipeAccessory`. Applying those four rows would have made terminal units,
both alarm panels and every zone valve box invisible to the medical-gas graph
walker, the flow solver and the NFPA 99 verification log **at the same time** —
and each one would still have looked correctly categorised in the audit.

That is the exact failure this repo keeps producing, and I very nearly added a
new instance of it while fixing an old one.

## What was done instead

1. **`TagConfigDeclarations`** — a Revit-free parser that reads both dialects.
   Recovers 58 families from data that already shipped.
2. **`TagCategoryNameForms.NormaliseKey`** — recovers 9 more whose declaration
   used a character Windows forbids in a file name (`Brace / Truss` on disk as
   `Brace - Truss`).
3. **Two genuine gaps filled**, each from evidence in the repo, not judgement:
   - `STING - MEP Sleeve Tag` → `Generic Models`. The declaration existed at
     `STING_TAG_CONFIG_v5_0_MEP.csv:3340` but was **commented out**, and
     `Core/Mep/SleeveEngine.cs:322` searches `OST_GenericModel` first.
   - `STING - Tie-In Gas Pipe Tag` → `Pipes`, matching
     `TagFamilyCreatorCommand.cs:369` (`OST_PipeCurves`) and its eight Tie-In
     siblings.

**Undeclared: 69 → 0**, with no category decided by opinion.

## The gate

`TagConfigDeclarationsTests.EveryShippedTagFamilyHasADeclaration` asserts that
every `.rfa` in `Data/TagFamilies` has a resolvable declaration. It would have
failed with 69 this morning. A new family dropped into the library without a
declaration now fails a test instead of surfacing months later as a silent
`NO-DECLARATION` row.

Two more tests guard the data: no family declared twice with different
categories, and no declaration written as prose rather than a category name.
The second carries a **named** exemption list of three LPS families that declare
several categories at once — see below. Shrink that list; do not grow it.

## What is still genuinely open — three rows, not sixty

These are the only ones no evidence in the repo settles.

| Family | Declared | The question |
|---|---|---|
| LPS Foundation Earth | `Structural Foundations / Rebar` | a family can only be ONE category |
| LPS Generic Component | `Generic Models / Specialty Equipment` | as above |
| LPS Natural Air Termination | `Roofs / Walls / Curtain Wall` | as above |

Two further rows have the repo disagreeing with itself, and are worth a look
when a healthcare model is open:

- **RTLS Reader** — the creator table says `Specialty Equipment`;
  `Core/Validation/Healthcare/RtlsCoverageValidator.cs:23-27` accepts Data,
  Communication, Nurse Call and Medical Equipment and **explicitly not**
  Specialty. One tag cannot span four categories.
- **MRI Zone** — the creator table says `Generic Models`; the same validator
  reads `CLN_MRI_ZONE_INT` off a **Room** via `GetRoomAtPoint`.

Both are settled by opening a healthcare model and reading what the real
families carry. That is a measurement, and it beats any amount of argument in a
markdown file — including this one.
