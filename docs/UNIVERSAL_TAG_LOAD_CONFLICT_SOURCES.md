# Where the 12 conflicts come from — including in a brand-new project

Answering: *"I used a new project without any parameters and the 12 were already
corrected, so what else could cause the same 12 conflicts? Is it because the tags/seeds
already had the old 12 parameters?"*

**Yes — the family side is the remaining source, and your hypothesis is the right shape.
But the sharper answer is that a new project does not clear the conflict, because the
project side is *acquired*, not configured — and the command's own pre-condition is what
acquires it.**

Measured 2026-09-17 against `main` @ `114e38244`, from the propagation run at 19:41 and
the code that produced it.

---

## 1 · Why a blank project does not help

Revit identifies a shared parameter by its **GUID**. A project holds one
`SharedParameterElement` per shared parameter it has **ever acquired** — and it acquires
them two ways:

1. **Binding** them (`Load Shared Params`, project setup, a template), or
2. **Loading one family that carries one.** No binding needed. The first family in wins,
   and its type is the project's type from then on.

So "a project with no parameters" describes the project *before* anything was loaded into
it. It is not a state the conflict can be tested in, because:

> **Propagate Universal refuses to run with fewer than two STING annotation families
> loaded.** `PropagateUniversalTagCommand.Execute` — *"Need the universal master plus at
> least one target family loaded."*

Loading the master **and** `STING - Duct Tag` is the pre-condition. Whichever of the two
loads first fixes the type of every shared parameter it carries; the second one is then
checked against it. In a blank project the two families are checked against **each other**,
with the project as the register.

That is consistent with the error text you saw: the family offers **Text**, the project
holds **Number / Currency / Length / Yes-No**. Those are the `MR_PARAMETERS.txt` types —
which is what the other family, or a STING project setup, would have put there.

### The experiment that settles it

Two loads into a fresh project, in this order, nothing else:

1. Load **only** `STING_Tag_Universal.rfa`.
   - **Errors** → the master still carries the Text-typed parameters itself. §3 of the
     runbook (the deletions) is not finished. The project is not involved at all.
   - **Clean** → the master is fixed. Go to 2.
2. Now load **`STING - Duct Tag`**.
   - **Errors naming the same 12** → the *duct tag* is the one carrying them, and it was
     the source all along. It will need the same treatment, and so will any other family
     of the same lineage.

Whichever way it falls, one of the two families is named, and no guessing is involved.

---

## 2 · What can put a Text-typed version into a family — every source, audited

Each row is what the code actually does, not what the file looks like it does.

| Source | Read at runtime? | Can it type a family parameter? | Verdict |
|---|---|---|---|
| **The `.rfa` itself** | — | Already typed, by whatever shared-parameter file the author was pointed at | **The remaining source.** See §3 |
| `docs/UNIVERSAL_TAG_MASTER_PARAMS.txt` (the build kit) | Only while Revit is pointed at it | **Yes** — a family parameter added from it takes its type | 2 of the 12 came from here (a NUMBER and a YESNO typed TEXT under the same GUIDs). Now gated by `tools/check_shared_param_types.py` |
| `PropagateUniversalTagCommand` → `AddMissingParams` | Yes | **No** — the command sets `app.SharedParametersFilename = MR_PARAMETERS.txt` for the whole run, so the 138 parameters it adds carry MR's types by construction | Not a source. Note this **overrides** whatever file you selected for §0.3 of the runbook |
| `FAMILY_PARAMETER_BINDINGS.csv` — `DataType` column (332 rows disagree with MR) | Yes | **No** — `BatchAddFamilyParamsCommand` opens `MR_PARAMETERS.txt` and binds the *definition*; the CSV supplies which parameter and which category, never the type | Not a load hazard on that path. It is still a documentation hazard, and the baseline in `check_shared_param_types.py` keeps it from growing |
| `PARAMETER_CATEGORIES.csv` — `DataType` column (`HVC_VEL_MPS` says TEXT, MR says NUMBER) | Yes, but only the **Categories** column, for `AuditCrossCsvConsistency` | **No** | Documentation hazard only |
| `TAG_PARAM_ALIGNMENT_AUDIT.csv` — lists all 12 as `TEXT` | **No consumer anywhere in the C# tree** | No | Inert, and actively misleading to read. It looks like the answer and is not |
| `STING_TAG_CONFIG_v5_0_*.csv` — the `Type` column (`Text`) | Yes | **No** — that column types the label row's *calculated value*, not the shared parameter the formula references | Not a source, but see §3: it is **why** the parameters are in the families at all |
| `…/data/TagFamilies/Seeds/` — 137 obsolete families | **No** — the probe was removed (`TagFamilyCreatorCommand.cs`: *"A 'Seeds/' sub-folder is deliberately NOT probed"*) | No | Inert. Not the cause |

**So no shipped data file can introduce the Text-typed 12 into a family at runtime today.**
The two that could are in the kit, and they are gated.

---

## 3 · Why the parameters are in the tag families in the first place

Not an accident, and not removable: the label rows **reference** them.

`STING_TAG_CONFIG_v5_0_*.csv` declares rows such as

```
12,T5,ASS_CST_STALE_BOOL,Stale:,,0,,Common,Text,Show T5 - Cost - Stale Flag,
   "if(TAG_PARA_STATE_5_BOOL, ASS_CST_STALE_BOOL, """")",BOLD,ORANGE,2.0,None,None
```

A formula can only name a parameter the family carries, so building that row **requires**
`ASS_CST_STALE_BOOL` to be added to the family first. Its type then comes from whichever
shared-parameter file was selected at that moment — and that is the whole mechanism. The
parameter is not wrong to be there; its *type* is wrong, and a numeric parameter cannot be
retyped to Text for a label, which is why the fix is the `_TXT` display mirror rather than
a deletion of the row.

The same CSVs drive all 206 families, so **if the master acquired a Text-typed version this
way, its siblings plausibly did too.** That is inference, not measurement — see below.

### Why no static gate can tell you which families are affected

I tried. A `.rfa` stores its parameter table compressed inside an OLE compound file:
searching `STING - Duct Tag.rfa` (430 KB) for the twelve GUIDs and their names, as ASCII
and as UTF-16, returns **zero hits**, while the words `Duct`, `STING` and `Autodesk` appear
a handful of times. There is nothing for a script to read.

**Any answer about what is inside these 206 files has to come from inside Revit.** That is
why the checks added in this change run there, and why the answer to "which families carry
it" is a command to run rather than a table in this document.

---

## 4 · The twelve, and what `MR_PARAMETERS.txt` says they are

The project side is right in every case; the family side is what has to change.

| Parameter | MR type | In the label? |
|---|---|---|
| `ASS_CST_STALE_BOOL` | YESNO | yes — row 22, repointed to `ASS_CST_STALE_TXT` |
| `ASS_CRITICALITY_RATING_NR` | NUMBER | yes — row 50, repointed to `ASS_CRITICALITY_RATING_TXT` |
| `ASS_CST_UNIT_PRICE_UGX_NR` | CURRENCY | no |
| `ASS_ELEVATION_M` | LENGTH | no |
| `MNT_HGT_MM` | LENGTH | no |
| `HVC_DCT_FLW_CFM` | NUMBER | no |
| `HVC_VEL_MPS` | NUMBER | no |
| `HVC_DCT_SOUNDLVL_DB` | NUMBER | no |
| `PER_EMBODIED_ENERGY_MJ` | NUMBER | no |
| `PER_EXPECTED_LIFE_YEARS` | NUMBER | no |
| `PER_RECYCLABILITY_PCT` | NUMBER | no |
| `PER_REPLACEMENT_COST_UGX` | CURRENCY | no |

The ten that are in no label row are the orphans of §3 of the runbook — inherited from the
Air Terminal master this family grew out of. Deleting a label row never deleted the family
parameter.

---

## 5 · What the code does about it now

Three changes, all in service of one thing: **the twelve names appear without a human
reading a modal dialog.**

1. **Propagation pre-flights the load.** Before the first family is touched,
   `SharedParamPreflight.Check` opens the master, reads what it offers, reads what the
   project holds, and names every parameter that will block the load. Conflicts abort the
   run with the list. Every target gets a clone of the same master, so this was always a
   whole-run fact — it used to be discovered 206 times, or once at minute 17.
2. **A refused load says why.** `Document.LoadFamily` returns a bare `false`; Revit's
   explanation goes to a dialog. A `CapturingFailuresPreprocessor` on the load transaction
   now collects it, so the report cell reads
   `LoadFamily back into project failed: <Revit's own text>` instead of the four words it
   carried on 2026-09-17.
3. **The conformance audit can veto a family.** Its criterion (8) was *"Loads cleanly into
   the target document (10 pts)"*, awarded for `OpenDocumentFile` not throwing — it never
   tested a load. It is renamed to what it proves, and a real check added as a **veto**: a
   family whose shared parameters disagree with the open project is `BLOCK` with the
   parameters named, whatever else it scores. With no project open the check reports
   **NOT RUN** rather than passing.

### To find every affected family in one run

Open the project, then **Family Conformance Check** → point it at
`…\CompiledPlugin\data\TagFamilies`. Every family that cannot load into that project comes
back `BLOCK` with `LOAD BLOCKED:` rows naming the parameters and both types. It opens 206
families, so give it time.

---

## 6 · The duct propagation failure, named

The 19:41 run, in full:

```
19:41:17  RunCommand<PropagateUniversalTagCommand>: start
19:41:59  PropagateUniversalTag: 'STING - Duct Tag' —
19:58:10  PropagateUniversalTag: succeeded=0, failed=1, params=138, types=14
```

- **The failure reason** was in the Excel report, not the log:
  `Target: STING - Duct Tag · Category: Generic Model Tags · ParamsAdded: 138 ·
  TypesCreated: 14 · Status: FAILED · Error: LoadFamily back into project failed`.
  The clone was built, the parameters added and 14 type variants created; the **load back**
  was refused. Consistent with the shared-parameter conflicts and nothing else observed.
- **The empty warning line** is a second, separate defect, now fixed.
  `TagCategoryResolver.Resolve` wrote `Note` only on its failure paths, so a *resolved
  mismatch* — the one case callers log — came back with `Note` null and printed the family
  name followed by a dash and nothing. It now says which category was declared and which
  the family carries. `FamilyConformanceCheckCommand` had the same blank line
  (`CATEGORY MISMATCH: `) and is fixed by the same change.
- **`STING - Duct Tag` is categorised `Generic Model Tags`**, the same defect
  `TagCategoryResolver` was written for. Propagation would have corrected it — if the load
  had succeeded.

**Nothing in the smoke test has been invalidated.** V2 — whether recategorising preserves
the 65 label rows — is still unproven, because the run never reached a loaded family.
Re-run it once the master loads cleanly.

---

## 7 · MEASURED: the twelve are in ~188 of the families, not in the master

Run 2026-09-18 11:26, Family Conformance Check over the deployed tag library with check (10)
(each family against `MR_PARAMETERS.txt`) live for the first time. 343 families audited,
**248 rows carrying a type disagreement**, **152 distinct parameters** wrong-typed across
**180 families**. The head of the list is the twelve:

| families | parameter | family has | `MR_PARAMETERS.txt` declares |
|---|---|---|---|
| **189** | `ASS_CST_UNIT_PRICE_UGX_NR` | string | currency |
| **188** | `ASS_ELEVATION_M` | string | length |
| **188** | `PER_EMBODIED_ENERGY_MJ` | string | number |
| **188** | `PER_EXPECTED_LIFE_YEARS` | string | number |
| **188** | `PER_RECYCLABILITY_PCT` | string | number |
| **188** | `PER_REPLACEMENT_COST_UGX` | string | currency |
| **141** | `ASS_CRITICALITY_RATING_NR` | string | number |
| **140** | `ASS_CST_STALE_BOOL` | string | bool |
| 24 | `MNT_HGT_MM` | string | length |
| 9 | `HVC_DCT_FLW_CFM` | string | number |
| 5 | `HVC_VEL_MPS` | string | number |

Worst single families: `STING - Electrical Equipment Tag` (38 wrong-typed parameters),
`STING - Electrical Connectors Tag` (26), `STING - Structural Framing Tag` (20).

### What this overturns

§2 of this document concluded that the remaining source was "the `.rfa` itself" and that no
static gate could say which files were affected. The first half was right. The second was
only true of a *static* gate: a check that opens each family **inside Revit** and compares it
against the declaration answers it exactly, and the answer is that the Text-typed versions
are in almost the whole library.

So the reading in §1 — that a blank project conflicts because the families race each other —
is confirmed and can be stated more plainly: **any two families loaded together are likely to
disagree, because ~188 of them carry these parameters as Text and the declaration says
numeric.** The master was cleaned by hand on 2026-09-17; that fixed 1 file of 189.

### What it means for the twelve

They cannot be fixed in the master, and they were never a property of the master. Options, in
increasing cost:

1. **Accept Text everywhere.** Change `MR_PARAMETERS.txt` to declare these as TEXT. One file,
   and it makes 188 families correct at a stroke — but it retypes parameters that carry real
   numbers elsewhere (`BOQCostManager` reads `ASS_CST_UNIT_PRICE_UGX_NR` as currency,
   `ASS_CST_STALE_BOOL` is written as a Yes/No by the cost engine), so it trades a load
   conflict for a data-model lie. **Not recommended.**
2. **The `_TXT` display-mirror migration.** What the master already does for rows 22 and 50:
   each wrong-typed parameter is replaced in the family by a real TEXT mirror carrying the
   formatted value, and the numeric original stays numeric. Correct, and it is 152 parameters
   × 180 families of family-editor work unless it is automated — and automating it means
   removing a shared parameter that label rows reference, which strips those rows.
3. **Propagate the (clean) master over all 206.** The clone carries the master's parameter
   set, so a propagated family inherits clean types by construction. This is the cheapest
   correct route and it is already built — but it is gated on V2, whether recategorising
   preserves the 65 label rows.

Option 3 is the one to aim at, which makes V2 the next thing to answer, not a formality.
