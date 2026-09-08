# KUT project documents

One place for the KUT documents that are **edited by hand**. Everything here is markdown, so
it diffs, reviews and merges like code.

Updated 2026-09-08 to the BS EN ISO 19650-2 UK National Annex code set (PRs #865, #852, #868).


## Layout

```
KUT_DOCS_WORKING/
  README.md      this file
  issued/        the built deliverables -- generated, never hand-edited
  source/        hand-edited markdown, and the naming migration map
```

**`issued/` is written by the generators, not by hand.** Editing a file there is lost at the
next build, and the gate detects it. The five gated documents plus the internal playbook now
live here rather than scattered at the repository root.

### What could not move, and why

| Stays where it is | Reason |
|---|---|
| `tools/*.py` | the generators; `check_kut_documents.py` resolves everything relative to the repo root |
| `project-templates/KUT/` | read by the plugin at runtime — moving it breaks deployment, not just the build |
| `GUIDES/KUT_BEP_TEMPLATE.md`, `GUIDES/KUT_PROJECT_DELIVERY_PLAYBOOK.md` | scanned by the leakage check as client-facing sources, and referenced in the CI path filter |
| `docs/qs_review/nrm2_site_civil_review.csv` | the QS round-trip writes it; `qs_nrm2_review.py` owns the path |

---

## What is here

| File | Status | Where it belongs |
|---|---|---|
| `KUT_Drawing_and_Document_Numbering_Convention.md` | corrected, **not yet copied back** | `KUT2026\00 Project Standards and Control\` |
| `KUT_BIM_MANAGING_PLAYBOOK.md` | corrected, **not yet copied back** | same |
| `KUT_BIM_MODELLING_PLAYBOOK.md` | corrected, **not yet copied back** | same |
| `KUT_BIM_MANAGER_PLAYBOOK.md` | corrected, **not yet copied back** | same |
| `KUT_NAMING_MIGRATION_MAP.md` | new | issue with the corrected set |

The four corrected files still need copying into the project folder. Their originals there are
untouched — that folder is outside this repository.

**What changed in them:** container volumes `TE`/`MH`/… → `01`–`06`; role `F` → `Y`
(fire protection is Specialist Designer; `F` is Facilities Manager in the standard); type
`DC`/`BQ`/`TR` → `RP`/`CP`/`IE`. Asset tags keep `BLD1`–`BLD6`, which is correct — see below.

---

## The thing most likely to waste your afternoon

**The five issued documents are NOT edited as documents.** They are built by generators, and
the content lives in Python, not in any markdown or Word file:

| Issued document | Built by |
|---|---|
| `KUT_BIM_Execution_Plan.docx` | `tools/build_bep.py` |
| `KUT_Project_Delivery_Playbook.docx` | `tools/build_team_playbook.py` |
| `KUT_Document_Control_Standard.docx` | `tools/build_document_control.py` |
| `KUT_Master_Information_Delivery_Plan.xlsx` | `tools/build_midp.py` + `tools/midp_rows.py` |
| `KUT_Mobilisation_Information_Request.docx` | `tools/build_symbion_request.py` |

Edit the `.docx` and your change is lost at the next build. The gate detects it and says so.

### `GUIDES/KUT_PROJECT_DELIVERY_PLAYBOOK.md` is a reference note, not an input

It is deliberately **not** in this folder. Its own header says *"Edit content here, then
regenerate the .docx"* — **that is not true.** `build_team_playbook.py` names it as "Source
content" in a comment and never reads it; the text is in the Python. Editing that markdown
changes nothing, and nothing warns you.

To change the issued playbook, edit `tools/build_team_playbook.py`.

---

## Codes, in one place

The single source is `tools/kut_naming.py`. The five issued documents, the sheet-name pattern
in `owner_standards.json` and the MIDP register all derive from it. **Change a code there and
regenerate — never in a document.**

| Field | Codes | Standard |
|---|---|---|
| Volume (container) | `01`–`06`, `00` site, `ZZ` all, `XX` n/a | ISO 19650, project-enumerated |
| Location (asset tag) | `BLD1`–`BLD6`, `EXT` | project — the tag is **not** an ISO container name |
| Role | `A C E I M P Q S W X Y` + `Z` | UK NA Table NA.3 |
| Type | `CO CP CR DR IE M2 M3 MI PP PR RD RI RP SH SN SP SU VS` | UK NA Table NA.2 |

**Two codes to be careful with:**

- **`SH` is a Schedule, not a Sheet.** A sheet is a drawing, `DR`. The old set had this
  inverted, so a name like `KUT-SMB-01-GF-SH-A-0100` was valid under both readings and meant
  different things. If you ever remap again, `SC`→`SH` and `SH`→`DR` must happen in **one
  pass** — sequentially, every schedule silently becomes a drawing.
- **Register refs are not role codes.** `FP-200`, `LV-200`, `G-100` keep the withdrawn letters
  on purpose. They are internal ids; renaming them collides (`FP-200` and `LV-200` would both
  become `Y-200`). The reason is recorded beside `TIDPS` in `tools/midp_rows.py`.

---

## After any edit

```bash
python tools/build_bep.py
python tools/build_team_playbook.py
python tools/build_document_control.py
python tools/build_midp.py
python tools/build_symbion_request.py
python tools/check_kut_documents.py      # 82 assertions across the five documents
```

The gate checks the pack is **internally consistent** and matches the configuration the LOD
gate enforces. It proves nothing about whether the requirements are right, and nothing has
been exercised against a real Revit model. A green run is not a validated pack.

---

## Still outstanding

- **The originator register.** Codes are `[ORG]` until the Lead Appointed Party issues it.
  Nothing should be numbered against a guessed code — that is a second rename on top of this
  one.
- **Copying the four corrected files** into the project folder.
- **Anything already numbered** under either old convention — see the migration map. Published
  containers are not renamed; superseding revisions carry the new form.
