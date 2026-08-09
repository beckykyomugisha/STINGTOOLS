# F-9 · SpatialCodeRegistry — reconciliation report

**Status: REPORT ONLY. No behaviour has changed.** `GetLevelCode`, `ParseLocCode`,
`ISO19650Validator`, `TagConfig` and the wizard are all untouched. The registry data file and class
exist but **nothing calls them yet**.

Measured 2026-08-09 on `claude/kibale-integration`. Produced because changing what `GetLevelCode`
returns changes tag content on every existing project, and that needs a look before, not after.

---

## 1. The five level vocabularies, counted

| # | Source | Size | Alphabet |
|---|---|---|---|
| 1 | `ParameterHelpers.GetLevelCode` if-chain (`:483-566`) | **130** reachable codes | `L00`–`L99`, `GF`, `LG`, `UG`, `B1`–`B9`, `SB`/`SB1`–`SB9`, `RF`, `PH`, `AT`, `TR`, `POD`, `MZ`, `PL`, `XX`, + 12-char passthrough |
| 2 | `shared/ifc/enums/StingLevelCodes.xml` | **19** | `B3`–`B1`, `GF`, `MZ`, `L01`–`L10`, `RF`, **`PR`**, `XX`, `*` |
| 3 | `Core/Drawing/Iso19650Vocabulary.LevelCodes` (`:257`) | **30** | `ZZ`, `XX`, `B2`, `B1`, **`00`–`20`**, `RF`, `MZ`, `PH` |
| 4 | `ISO19650Validator` LVL branch (`:224-242`) | membership list of 10 + 3 patterns | as #1 |
| 5 | `TagConfig` | **none — there is no `LvlCodes` at all** | — |

Three findings that are not in the register:

- **#2 and #1 cannot both be right.** `StingLevelCodes.xml` declares `PR`; `GetLevelCode` can never
  produce it and the validator's known-list does not contain it. It is the only XML code with no
  producer.
- **#3 is a different alphabet entirely.** `00`–`20` versus `L00`–`L99`, and `GF` has no
  representation at all. Anything reconciling STING tags with ISO 19650 file names must translate,
  not compare — which is why the registry carries `code` **and** `isoCode` per level rather than one
  field.
- **The validator's membership list has no teeth.** The final test is
  `if (!isKnownLvl && !lvlUpper.All(char.IsLetterOrDigit))` — so an *unknown but alphanumeric* code
  **passes**. `PR`, `ZZ`, `00`, `MZ2`, `UR` all validate today. `isKnownLvl` only ever fires in
  combination with a non-alphanumeric character. The 12-char passthrough is caught by the **length**
  check at `:228`, not by membership.

## 2. The three LOC vocabularies, and the live defect

`ParseLocCode` (`ParameterHelpers.cs:1083-1101`) can return exactly four values: `BLD1`, `BLD2`,
`BLD3`, `EXT`. It never consults any configured vocabulary.

Kibale's own `project_config.json` declares:

```
LOC_CODES        = [COT01..COT08, STF, KDR, POOL, EXT, XX]
CUSTOM_VALID_LOC = [same]
LOC_CODES_EXTRA  = [same, minus EXT/XX]
```

**Not one of `COT01`–`COT08`, `STF`, `KDR` or `POOL` is reachable by `ParseLocCode`.** On Kibale,
every element whose room/building name does not contain "EXTERNAL"/"EXTERIOR" gets **no LOC
auto-detection at all** and falls to `XX`. That is F-1 and F-2 meeting: the vocabulary is
configurable, the parser is not, and the failure is silent.

The three keys have three different consumers, which is why they exist and why they disagree —
`LOC_CODES` feeds `TagConfig`, `CUSTOM_VALID_LOC` is the only key `ISO19650Validator` honours, and
`LOC_CODES_EXTRA` is read only by `FederationReview` and `BuildingAwareCDEFolders`.

`TagConfig.LocPatterns` (`:594`, seeded `:1265`) already holds the alias table this needs
— `main building`, `annex`, `primary`/`secondary`/`tertiary`, `site`, `landscape`, `car park` — and
has **zero consumers**. It is nearly the missing half of `ParseLocCode`, written and never wired.

## 3. Current versus proposed, per level name

`GetLevelCode` is a pure function of the Revit level name, so this table is the behaviour change in
full — no model required. `*` marks a proposed value that depends on aliases the owner must confirm.

| Revit level name | GetLevelCode today | validator verdict | proposed |
|---|---|---|---|
| Level 1 | `L01` | ok | `L01` |
| Level 2 | `L02` | ok | `L02` |
| Level 10 | `L10` | ok | `L10` |
| Ground Floor | `GF` | ok | `GF` |
| Ground | `GF` | ok | `GF` |
| Lower Ground | `LG` | ok | `LG` |
| Upper Ground | `UG` | ok | `UG` |
| Basement | `B1` | ok | `B1` |
| Basement 2 | `B2` | ok | `B2` |
| Sub-Basement 1 | `SB1` | ok | `SB1` |
| Roof | `RF` | ok | `RF` |
| Penthouse | `PH` | ok | `PH` |
| Attic | `AT` | ok | `AT` |
| Terrace | `TR` | ok | `TR` |
| Podium | `POD` | ok | `POD` |
| Mezzanine | `MZ` | ok | `MZ` |
| Mezzanine 2 | `MZ` | ok | `MZ2` **CHANGE** |
| Plant Room | `PL` | ok | `PL` |
| Plant Level | `PLANT-LEVEL` | FAIL len>4 | `PL` **CHANGE** |
| First Floor | `L01` | ok | `L01` |
| Second Floor | `L02` | ok | `L02` |
| Ring beam | `RING-BEAM` | FAIL len>4 | `XX*` **CHANGE** |
| Foundation | `FOUNDATION` | FAIL len>4 | `XX*` **CHANGE** |
| Truss Bearing | `TRUSS-BEARIN` | FAIL len>4 | `XX*` **CHANGE** |
| T.O. Slab | `TO-SLAB` | FAIL len>4 | `GF*` **CHANGE** |
| Parapet | `PARAPET` | FAIL len>4 | `RF*` **CHANGE** |
| Upper Roof | `UPPER-ROOF` | FAIL len>4 | `UR` **CHANGE** |
| Cottage Floor | `COTTAGE-FLOO` | FAIL len>4 | `XX*` **CHANGE** |
| 00 - Ground | `L00` | ok | `GF` **CHANGE** |
| 01 - First | `L01` | ok | `L01` |

**10 of 30 names change.**

**Read the change count carefully: 8 of the 10 changes are to names that FAIL validation today** (the
12-char passthrough — `RING-BEAM`, `FOUNDATION`, `TRUSS-BEARIN`, `COTTAGE-FLOO`, `PLANT-LEVEL`,
`PARAPET`, `TO-SLAB`, `UPPER-ROOF`). Those are not regressions; they are the defect being fixed.

**Only two changes affect a name that currently produces a valid code:**

| Name | Today | Proposed | Why it matters |
|---|---|---|---|
| `Mezzanine 2` | `MZ` | `MZ2` | Today the second mezzanine **collides** with the first — same tag, two levels. This is F-6. Fixing it changes existing tags on any model with more than one mezzanine. |
| `00 - Ground` | `L00` | `GF` | Only affects projects using ISO-style level names. `L00` and `GF` are both currently valid, so this silently re-codes a level. |

**This is not widespread change** by the standard the constraint sets, but the two rows above are
real and the `Mezzanine 2` one is a correction that will move tags. Neither should ship without the
owner's sign-off.

## 4. Where the drafted baseline is wrong

`GUIDES/kibale-project-config/spatial_codes.json` on `claude/kibale-np-bim-modeling-f5e653`, 24 level
entries. Reconciled against the four live vocabularies:

| Issue | Detail |
|---|---|
| **A note is an array element** | Entry `[16]` has **no `code`** — it is a `_generatorNote` string sitting inside `levels[]`. Any consumer iterating the array trips on it. A note belongs in a sibling key, not in the data it describes. |
| **Three codes no producer can emit** | `MZ2`, `UR`, `ZZ` are in the draft; `GetLevelCode` cannot produce any of them. They are only reachable once the alias table replaces the if-chain — correct as an intent, but they are **new vocabulary**, not reconciliation, and should be labelled as such. |
| **`PR` is missing** | The one code unique to `StingLevelCodes.xml`. The draft neither adopts nor retires it. It must do one or the other, or the XML stays a fifth vocabulary. |
| **Stops at `L05`** | The XML goes to `L10`, `GetLevelCode` to `L99`. The draft's own note says the registry should *synthesise* `Lnn` on demand — right answer, but then `L01`–`L05` should not be enumerated either, or the rule and the rows disagree. |
| **`SB` has no ordinal** | `GetLevelCode` emits `SB`, `SB1`…`SB9`. The draft has bare `SB` only, so `SB1` would fall through to the miss path. |
| **`isoCode` is mostly an identity map** | `XX→XX`, `ZZ→ZZ`, `B1→B1`, `LG→LG`, `UG→UG`, `SB→SB`. Only `GF→00` and `Lnn→nn` actually translate. `LG`/`UG`/`SB`/`POD`/`TR`/`AT` have **no ISO 19650 equivalent** and the identity mapping quietly asserts they do. They should be explicitly null so a consumer must decide, rather than emitting a non-ISO code into an ISO field. |

The draft's structure — `code` / `isoCode` / `label` / `aliases` / `sortOrder`, plus
`projectTypePresets` — is sound and is what the shipped baseline adopts.

## 5. What is NOT in this pass

Nothing in §3 has been applied. The choke points listed in the brief — `GetLevelCode`,
`ParseLocCode`/`ParseZoneCode`, `ISO19650Validator`, `TagConfig.LocCodes`, `MultiBuildingCommands`,
`BuildLocationPhrase`, `ProjectSetupWizard`, `SectorPackCommand` — are all untouched, and
`DrawingProducer` / `DrawingTokenContext` are excluded by instruction.
