# Baseline layers 2 and 3 — family types and family parameters

**Spec for implementation.** Extends the model-authoring baseline (#789) so it can reach
family-backed elements, not only system families.

---

## 1. A correction to the T6 proposal

`docs/MATERIAL_SCHEDULE_T6_FAMILY_MATERIALS_PROPOSAL.md` §6 states that adding parameters to
families is *"yours — not a code task"*. **That is wrong, and this spec supersedes it.**

`StingTools/Core/Symbols/FamilyAugmentationEngine.cs` already performs the round trip on
families **loaded in the project**, with no `.rfa` file editing and no re-issuing:

```
doc.EditFamily(fam) → famDoc.FamilyManager → add parameters
                    → famDoc.LoadFamily(doc, new ReuseLoadOptions()) → famDoc.Close(false)
```

`RollbackAugmentation` exists alongside it. It is used today for symbol families; nothing
prevents pointing the same mechanism at doors and windows.

**What remains a human decision is the VALUE, not the parameter.** See §5 — the line between
a declaration and an inference is the whole reason PR #710 was withdrawn.

---

## 2. Three layers, one command

| Layer | What it creates | Mechanism | Status |
|---|---|---|---|
| **1 — Host types** | wall / floor / roof / ceiling types | `Duplicate` + `SetCompoundStructure` | **Built** (#789) |
| **2 — Family types** | door / window / column / foundation **types** | `FamilySymbol.Duplicate(name)` + set type params | **This spec** |
| **3 — Family parameters** | missing STING params on loaded families | `EditFamily` + `FamilyManager.AddParameter` + `LoadFamily` | **This spec** |

All three are declared in the **same** `StingTools/Data/STING_PROJECT_BASELINE.json`, with the
same project override, audited by the **same read-only** `Baseline_Audit`, and written only on
the same confirm. **Do not create new commands.** One report, one decision point.

### The honest split survives unchanged

Layer 2 mints types *inside* a family. **It cannot conjure the family.** If the family is not
loaded, it is reported as guidance — exactly as `Structural Columns: 0 of 1 expected type(s)
match` reports today. A baseline that claimed to create a door family would be inventing its
own deliverable.

---

## 3. Layer 2 — family types

### 3.1 Schema

New `familyTypes` array in `STING_PROJECT_BASELINE.json`:

```json
{
  "familyTypes": [
    {
      "category": "Doors",
      "familyNamePatterns": ["Single-Flush", "M_Single-Flush", "Door"],
      "typeName": "STING Flush Door 900x2100",
      "purpose": "Standard internal single leaf",
      "parameters": [
        { "name": "Width",  "valueMm": 900 },
        { "name": "Height", "valueMm": 2100 }
      ]
    }
  ]
}
```

`familyNamePatterns` is an ordered preference list. The **first loaded family in that category
whose name contains any pattern** is the host. Absent that, guidance — never a substitute
family from a different category.

### 3.2 Minting

```csharp
var symbol = /* an existing FamilySymbol of the chosen family */;
var created = symbol.Duplicate(typeName) as FamilySymbol;
foreach (var p in spec.Parameters) SetOnType(created, p);
```

**Note:** nothing in the codebase duplicates a `FamilySymbol` today — only `TextNoteType` and
`DimensionType` (`TemplateManagerCommands.cs`). Treat the first implementation as unproven
and report per-type failures individually, the way #798 had to for host types.

### 3.3 Two failures already paid for elsewhere — do not repeat them

1. **Roll back a half-made type.** `Duplicate` commits before the parameter set runs. If the
   set fails, delete the duplicate. #798: five floor types survived as baseline-named husks
   carrying the source's build-up, and the next audit read them as conforming, making the
   failure permanent and invisible.
2. **A name match with different parameters is a CONFLICT, not an overwrite.** The model's
   900×2100 may be deliberately different from the baseline's. Report it; never rewrite it.

### 3.4 Parameter setting

Set by **parameter name**, not built-in enum: door families vary. Convert mm → feet at the
boundary (`/ 304.8`). A parameter the family does not have is a per-type failure with the
parameter named — **not a silent skip**.

---

## 4. Layer 3 — family parameters

### 4.1 Shared, not local — this is the load-bearing detail

`FamilyAugmentationEngine.AddTextParam` calls:

```csharp
fm.AddParameter(name, GroupTypeId.IdentityData, SpecTypeId.String.Text, isInstance: true);
```

That creates a **local family parameter**: no GUID, no shared identity. Two families given
"the same" parameter this way hold two unrelated parameters — they cannot be scheduled
together, and the material schedule cannot read them reliably across a project.

**Layer 3 must add SHARED parameters** from the STING shared-parameter file, via the
`ExternalDefinition` overload — the path `BatchAddFamilyParamsCommand` already takes
(`defFile.Groups` → `ExternalDefinition`).

And **`isInstance: false`** — T6 specifies type parameters. A door type's leaf material does
not vary per instance.

### 4.2 Schema

```json
{
  "familyParameters": [
    {
      "category": "Doors",
      "parameters": ["BLE_DOOR_LEAF_MATERIAL_TXT",
                     "BLE_DOOR_FRAME_MATERIAL_TXT",
                     "BLE_DOOR_IRONMONGERY_SET_TXT"],
      "isInstance": false,
      "group": "IdentityData"
    }
  ]
}
```

Every name must resolve in the shared-parameter file. **A name that does not resolve is a
baseline validation error**, caught by `ProjectBaseline.Validate()` before any model is
touched — the same class as a layer naming an undeclared material.

### 4.3 Audit-first, and genuinely reversible

`EditFamily` + `LoadFamily` across every door and window family is a heavy, model-mutating
operation. It must:

- appear in the audit as *"N famil(ies) would be augmented with M parameter(s)"* **before**
  any confirm;
- run inside the existing single transaction;
- **report, not crash, on families that cannot be edited** — in-place families, some vendor
  families, and workshared families owned by another user. These are expected, not
  exceptional;
- leave a family untouched if it already has the parameter (idempotent re-runs).

`RollbackAugmentation` exists; wire it to a `Baseline_RollbackParams` command **only if** you
also give it a button. A `case` label alone is not a wiring (#792).

---

## 5. The line between a declaration and an inference

Layer 2 sets **dimensions** (900×2100) — objective, no judgement.

Layer 3 creates **empty** material parameters. Somebody must decide that a given door type's
leaf is `Flush timber, hollow core`.

That can be data-driven, and the distinction is exact:

- A **reviewed table** mapping type-name patterns to values, signed off once by a human and
  living in the project override, is a **declaration**. Legitimate.
- The **same mapping applied silently in code**, shipped as a corporate default, is an
  **inference** — and inference from type names is precisely what was withdrawn in PR #710
  for pricing whole walls as paint.

**Therefore:** if a value-mapping table is implemented, it ships **empty** in the corporate
baseline and lives only in the project override, and the export names how many values came
from it. Never a corporate default that silently fills materials.

---

## 6. Reporting

Extend the existing audit report; add no new dialogs.

```
WILL CREATE 6 family type(s):
  Doors:
    + STING Flush Door 900x2100   (in family "M_Single-Flush")

WILL AUGMENT 12 famil(ies) with 3 parameter(s) each   [type parameters, shared]

CANNOT CREATE — 2 item(s) need families loaded by hand:
  ? Structural Columns: no loaded family matches [Concrete | RC | STING]

WILL NOT TOUCH — 1 existing type differs from the baseline:
  ! Doors / STING Flush Door 900x2100: exists at 800x2100
```

And one export note, in the established denominator style:

```
Family materials: 47 door/window type(s) inspected, 0 declare a leaf or frame material.
```

---

## 7. Task breakdown

| | Task | Effort |
|---|---|---|
| **B1** | Layer 2 schema + audit + mint + rollback + tests | 5h |
| **B2** | Layer 3 schema + shared-param resolution + augment + audit + tests | 5h |
| **B3** | Catalogue-pack mechanism + one provisional `EA-RESIDENTIAL-V1` pack (§10) | 3h |
| **B4** | `Baseline_HarvestTypes` — read placed types, write a pack (§10.3) | 3h |

B1 and B2 are independent. B3 depends on B1 (it is layer-2 data). B4 depends on B3's schema
but not on its content.

---

## 8. Decisions needed

- **D1** — Layer 3 default: audit-only until explicitly enabled, or included in the normal
  Apply confirm? *Recommendation: included, since Apply is already audit-first — but it must
  be a separately-counted line in the report, because editing families is a bigger act than
  adding a wall type.*
- **D2** — Value-mapping table now, or leave the parameters empty for manual fill?
  *Recommendation: leave empty. The parameters are worthless until T6's emitter exists, and
  an empty parameter is honest where a guessed one is not.*
- **D3 — DECIDED.** Not a fixed catalogue. Opt-in packs, seeded by harvesting real
  projects. See §10.
- **D4 — DECIDED: yes, vendor families included.** See §10.4 for why this is safe here and
  what it requires.

---

## 9. Not recommended

- Creating families from scratch. `SymbolLibraryCreator` can do it for seed symbols, but a
  door family authored programmatically will not match what a vendor supplies, and the
  schedule gains nothing a type on a generic family does not already give.
- Adding parameters as **local** family parameters (§4.1). They cannot be scheduled together.
- A corporate value-mapping table (§5).
- Any new top-level command. Layers 2 and 3 belong to `Baseline_Audit` / `Baseline_Apply`.

---

## 10. B3/D3 — the catalogue, decided

### 10.1 Why not a fixed corporate catalogue

The obvious move is to ship ~30 East African types in the corporate baseline. It is the wrong
one. Those sizes would be **one person's reading of the market**, applied to every future
project, and reported by the audit as though they were standards. A project whose doors are
genuinely 850 wide would be told, on every run, that it is missing a type it does not want.

The failure mode is familiar: a confident default nobody asked for, that nobody checks.

### 10.2 The mechanism — opt-in named packs

`StingTools/Data/STING_TYPE_CATALOGUES.json`, **none active by default**:

```json
{
  "schemaVersion": "1.0",
  "note": "Catalogue packs are OPT-IN. No pack applies unless a project adopts it by id.",
  "packs": [
    {
      "id": "EA-RESIDENTIAL-V1",
      "title": "East African residential — provisional starter",
      "status": "provisional",
      "sourceNote": "Seeded from common Ugandan residential practice, NOT from a standard or a completed project. Replace by harvesting a delivered model (see 10.3).",
      "familyTypes": [ /* layer-2 familyType entries */ ]
    }
  ]
}
```

A project adopts packs in its own override:

```json
{ "adoptCatalogues": ["EA-RESIDENTIAL-V1"] }
```

Properties that make this sustainable:

- **Adoption is a stated decision**, per project, and visible in the audit report.
- **A pack is additive.** A project can adopt one and still override individual types by
  declaring them in `familyTypes` — project entries win by name, as they already do.
- **A new market is a new pack**, not a code change and not a change to the universal
  baseline. `EA-COMMERCIAL-V1`, `RW-RESIDENTIAL-V1`, a client's own house standard.
- **Packs are versioned in the id.** `-V1` → `-V2` is an explicit migration, never a silent
  redefinition of what a project already adopted.
- The **universal baseline stays minimal** — only what every project needs.

### 10.3 The seeding engine — harvest, do not guess

This is the part that makes the catalogue improve rather than ossify.

`Baseline_HarvestTypes` (read-only) reads the types **actually placed** in a delivered model
and writes a pack to `<project>/_BIM_COORD/harvested_catalogue.json`, ready to be reviewed,
renamed and promoted to corporate.

So the catalogue grows from **work that was actually built and paid for**, not from anyone's
recollection of the market. After two or three delivered projects the provisional starter pack
should be replaced outright.

Harvest reports placed types only — a type nobody used is not evidence of practice.

**It needs a handler case AND a button** (#792), and it is read-only: it writes a JSON file,
never the model.

### 10.4 D4 — vendor families: yes, and here is why it is safe

Layer 2 mints types in vendor families as well as generic ones.

The obvious objection is that a vendor's next release, reloaded with "overwrite existing",
wipes the STING types. That is true — **and the audit/apply cycle already handles it.** After
such a reload the next audit reports those types as Missing and Apply re-mints them. The
system is idempotent and therefore self-healing; the loss is visible and one click from
repaired.

What it requires:

1. **The `STING ` name prefix is the contract**, exactly as `"STING VIS - "` is for visibility
   filters. It is what makes a minted type identifiable as ours after a reload.
2. **Catalog-driven families will refuse.** Many vendor families are backed by a type catalog
   (`.txt`) or driven by formulas, where duplicating a type and setting Width/Height violates
   a constraint. That is a **per-type reported failure**, not a crash — and given §3.3, the
   half-made duplicate must be rolled back.
3. **Never edit an existing vendor type.** Minting a new one is additive; changing theirs is
   not ours to do. This is already the CONFLICT rule.

### 10.5 What ships now

- The pack **mechanism** (10.2) and the **harvest** command (10.3) — B3 and B4.
- **One** pack, `EA-RESIDENTIAL-V1`, marked `provisional`, adopted by **nobody** by default,
  with a `sourceNote` on the pack saying where the sizes came from and that they are to be
  replaced by harvest.

A provisional pack that nobody has adopted can be wrong without costing anything. A corporate
default cannot.

