# KNP26 — what blocks modelling, and what does not

Kibale NP Lodge · project code **KNP26** · branch `claude/kibale-integration`

The distinction this document exists to make: **can the operator start modelling and produce
drawings?** Tool debt that does not stop that is listed separately and should not be read as a
blocker.

---

## Blocks modelling — do these before opening Revit on KNP26

| # | What | Why it blocks | Time |
|---|---|---|---|
| 1 | **Set Project Information → Number = `KNP26`**, then save, close, reopen | The folder root is stamped into ExtensibleStorage on first resolve. Set it later and you get a *second* tree, not a moved one. With no Number at all you land in the shared `PRJ` placeholder — now warned about, but still wrong. | 2 min |
| 2 | **Run Load Shared Parameters** | The 36 `PRJ_*` bindings added under K-11 only take effect when the binder runs. Until then `PRJ_PROJECT_COD_TXT` and `PRJ_ORG_ORIGINATOR_CODE_TXT` do not appear in Manage → Project Information, and **no sheet number can be produced** — under K-13 the token is now omitted and Revit rejects the number outright. | 3 min |
| 3 | **Leave Organization Name = `ACE`** | ACE are the architects; Planscape documents for them and issues on ACE's title block. Not a leftover — nothing to change. Separately, the ISO **originator** code is `PLN` (Planscape authored the containers), pending one line of written confirmation from ACE. Different fields, different questions. | 0 min |
| 4 | **Deploy the current build** (`deploy.bat`), restart Revit | Everything above is in this branch and not in whatever is currently deployed. Confirm the target: `grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin \| sort -u` | 5 min |

That is the whole blocking list. **~10 minutes.**

---

## Does NOT block modelling

### The Revit session (depth, G-8) — explicitly not a blocker

`docs/OPERATOR_SESSION_KIBALE.md` holds three questions needing Revit. **None of them stops
modelling or drawing production:**

- **Depth (per-type vs per-instance tag gates)** — tags render today at whatever depth the type
  variants carry. The open question is which mechanism *should* own depth, not whether tagging
  works. Set depth once for the project via **Tag Studio → Presentation Mode** and carry on.
- **G-8 (declared vs actual binding types)** — a *documentation* defect unless the audit finds a
  differing row. Parameters bind and write correctly today; the question is whether
  `MR_PARAMETERS.csv`'s `Binding_Type` column tells the truth about them.
- **The last leak artefact** — one stale `.sting_live_profile_sync.json` in the deploy folder.
  Cosmetic; the guard that created it is fixed.

Do that session when convenient. It is not on the critical path.

### Tool debt, carried knowingly

| Item | State | Why it can wait |
|---|---|---|
| **G-25** `SHT_*` orphaning | **CLOSED** | The resolver's `SHT` exclusion was the wrong side — all ten bind to `Sheets`, as `PRJ_SHEET_*` does. Fixed, `--apply` landed, +27 params gained a binding row and nothing live was orphaned. |
| **K-16** BOQ rate read the wrong scope | **CLOSED** | `ProdCode` and `SystemType` are Type-bound but were read off the instance, so both were always empty and every element fell to the category rate. Fixed. |
| **3B.4** presentation auto-tagging | **CLOSED** | Six `pres-*` types set to `autoTag=false`, checksums re-stamped (6 of 93 drifted, `--check` now 93/93). |
| **384** parameter-readership violations | ratchet at 384 | Mostly bucket A — a parameter genuinely missing. The gate stops the count going *up*, which is how all seven affix bugs arrived. Not a clearance target. |
| **G-20** type marks | **built this pass** | Preview then assign from the DOCS tab. |
| **K-14 / K-15** | open / closed | Mark dedup drift; `WriteToRooms` over-reporting (closed). |

---

## What changed for the operator on this branch

- **Sheet numbers now fail loudly** rather than dropping a segment. If `{project}` never resolved you
  get a rejected sheet number and a log line naming the parameter — not `-PLN-COT01-…` on an issued
  drawing.
- **Sheets carry their ISO segments individually** (`PRJ_SHEET_PROJECT_TXT` … `PRJ_SHEET_SEQ_TXT`
  plus `PRJ_SHEET_FULL_REF_TXT`), so a title-block label can show any subset — `A-GF-1001` on a busy
  plan, the full reference on a CDE stamp. Playbook Part 1 has the recipes.
- **Door and window type marks generate** (`DR-01`, `WIN-03`), monotonic and never reused, with a
  preview that writes nothing and a join check against the ISO tag.
- **Type schedules exist** — one row per product with a count, alongside the itemised register.
- **A misconfigured project code is now audible** — the `PRJ` fallback warns instead of silently
  minting a placeholder tree.

---

## Verification, this branch

```
dotnet build StingTools/StingTools.csproj        0 errors, 0 warnings
tools/validate_data_schemas.py --self-test       8 deliberate defects caught
tools/validate_data_schemas.py                   5 files validated, 0 warnings
tools/validate_param_readership.py --self-test   4 defect shapes caught, no false positives
tools/validate_param_readership.py               384 / ceiling 384 — PASS
tools/check_path_discipline.ps1                  Tier 1: 0 · Tier 2: 0
tools/restamp_content_manifest.py --check        207/207 match
tools/binding_simulator.py                       3401 declared · 3244 bound · 157 skipped · 0 conflicts
tools/param_binding_resolver.py (--check)        all generated files match disk
StampDrawingTypeChecksums --check                93/93 correct
```
