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
| **B3** | East African type catalogue as data — **after** B1, and after human sign-off on the list | 2h |

B1 and B2 are independent. B3 is data only and must not begin before §8 D3.

---

## 8. Decisions needed

- **D1** — Layer 3 default: audit-only until explicitly enabled, or included in the normal
  Apply confirm? *Recommendation: included, since Apply is already audit-first — but it must
  be a separately-counted line in the report, because editing families is a bigger act than
  adding a wall type.*
- **D2** — Value-mapping table now, or leave the parameters empty for manual fill?
  *Recommendation: leave empty. The parameters are worthless until T6's emitter exists, and
  an empty parameter is honest where a guessed one is not.*
- **D3** — The East African type catalogue in §3 of the advice needs your sign-off. Sizes,
  naming, and which are STING standards versus project-specific.
- **D4** — Should Layer 2 mint types in **vendor** families, or only in generic ones? Adding
  a type to a manufacturer's family can conflict with their next release.

---

## 9. Not recommended

- Creating families from scratch. `SymbolLibraryCreator` can do it for seed symbols, but a
  door family authored programmatically will not match what a vendor supplies, and the
  schedule gains nothing a type on a generic family does not already give.
- Adding parameters as **local** family parameters (§4.1). They cannot be scheduled together.
- A corporate value-mapping table (§5).
- Any new top-level command. Layers 2 and 3 belong to `Baseline_Audit` / `Baseline_Apply`.
