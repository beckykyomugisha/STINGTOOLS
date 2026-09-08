# KUT naming migration map

**Purpose.** The project carried two naming conventions. This maps every code from the old
sets to the adopted standard, so a container or tag already issued can be renamed, and a
reader of an older document can tell what they are looking at.

**Adopted standard.** BS EN ISO 19650-2 UK National Annex — Table NA.2 (type codes) and
Table NA.3 (role codes). Volume is a two-character project enumeration with `ZZ` and `XX`
reserved by the standard for *all* and *not applicable*.

---

## 1. The two volume fields are different, and are not interchangeable

This is the distinction the earlier documents collapsed, and the one most likely to cause a
wrong rename.

| Field | Where it appears | Governed by | Codes |
|---|---|---|---|
| **Container Volume** | 3rd field of a container name — `KUT-SMB-`**`01`**`-GF-DR-A-1001` | ISO 19650 container naming | `01`–`06`, `00`, `ZZ`, `XX` |
| **Asset-tag location** | 2nd segment of the element tag — `M-`**`BLD1`**`-Z01-GF-HVAC-SUP-AHU-0003` | project convention, **not** ISO | `BLD1`–`BLD6`, `EXT` |

ISO 19650 governs container naming only. The element tag is a separate project identifier, so
`BLD1` there is not a standards departure and does not change.

| Building | Container volume | Asset-tag location | Old site code |
|---|---|---|---|
| Temple | `01` | `BLD1` | `TE` |
| Meetinghouse | `02` | `BLD2` | `MH` |
| Housing / ancillary | `03` | `BLD3` | `HS` |
| Grounds building | `04` | `BLD4` | `GB` |
| Utility building | `05` | `BLD5` | `UB` |
| Guard house | `06` | `BLD6` | `GH` |
| Site-wide / external | `00` | `EXT` | `ZZ` was used for both site-wide and all-volumes |
| All volumes | `ZZ` | — | — |
| Not applicable | `XX` | — | — |

> **`ZZ` was doing two jobs.** The site documents used `ZZ` for both *site-wide works* and
> *all volumes*. They are different: site-wide is a volume (external works exist and are
> built), all-volumes is the absence of one. Site-wide is `00`; `ZZ` now means all volumes
> only.

---

## 2. Role codes — BS EN ISO 19650-2 UK NA Table NA.3

The role field states the **discipline of the originator**, not the subject of the container.

| Old (repo pack) | Old (site docs) | Adopted | Role |
|---|---|---|---|
| `A` | `A` | `A` | Architect |
| — | `I` | `I` | Interior Designer — including FF&E and finishes |
| `S` | `S` | `S` | Structural Engineer |
| `M` | `M` | `M` | Mechanical Engineer |
| `E` | `E` | `E` | Electrical Engineer |
| `P` | `P` | `P` | Public Health Engineer |
| `G` | `C` | `C` | Civil Engineer |
| `FP` | `F` | `Y` | Specialist Designer — fire protection |
| `LV` | — | `Y` | Specialist Designer — low voltage and communications |
| — | `L` | `L` | Landscape Architect |
| — | — | `Q` | Quantity Surveyor |
| — | — | `W` | Contractor |
| — | — | `X` | Sub-contractor |
| `Z` | `Z` | `Z` | General — federated and multi-discipline |

**Three corrections worth understanding, not just applying:**

- **`G` was Civil.** In the standard `G` is Geographical/Land Surveyor. Civil Engineer is `C`.
  A container issued as `-G-` therefore claimed to come from a land surveyor.
- **`F` was fire protection.** In the standard `F` is Facilities Manager.
- **`FP` and `LV` are not codes at all.** Both issue under `Y`, distinguished by the
  Volume/System field. An invented role code is invalid on every downstream system that reads
  the standard, and silently so.

**Q, W and X did not exist before.** The QS, the Contractor and sub-contractors had delivery
plans and no way to name a compliant container — ten deliverables in the MIDP. That gap closes
with the standard set, and the *"cost information is issued under Z"* workaround retires.

---

## 3. Type codes — BS EN ISO 19650-2 UK NA Table NA.2

| Old (repo pack) | Old (site docs) | Adopted | Meaning |
|---|---|---|---|
| **`SH`** | `DR` | **`DR`** | **Drawing — including sheets** |
| **`SC`** | **`SH`** | **`SH`** | **Schedule** |
| `M3` | `M3` | `M3` | 3D model |
| `M2` | — | `M2` | 2D model |
| `DR` | `DR` | `DR` | Drawing |
| `SP` | `SP` | `SP` | Specification |
| `RP` | `RP` | `RP` | Report — including calculations and method statements |
| `RD` | — | `RD` | Room data sheet |
| `PP` | — | `PP` | Presentation |
| `CR` | — | `CR` | Clash rendition |
| `CA` | — | `RP` | *(calculation — withdrawn)* |
| `MS` | — | `RP` | *(method statement — withdrawn)* |
| — | `DC` | `RP` | *(document — withdrawn)* |
| `BQ` | `BQ` | `CP` | Cost plan — including bills of quantities |
| `TR` | `TR` | `IE` | Information exchange — including transmittals |
| — | — | `CO` `MI` `PR` `RI` `SN` `SU` `VS` | Correspondence · minutes · programme · RFI · snagging · survey · visualisation |

> ### `SH` is the dangerous one
>
> The repo pack read `SH` as **Sheet** and invented `SC` for a schedule. The site documents
> read `SH` as **Schedule**, which is correct. So `KUT-SMB-01-GF-SH-A-0100` was **valid under
> both conventions and meant different things** — a sheet to one reader, a schedule to the
> other. Nothing would have flagged it.
>
> **When renaming, `SC` → `SH` and `SH` → `DR` must be done in one pass.** Run sequentially,
> every `SC` becomes `SH` and then every `SH` — including the ones just written — becomes
> `DR`, silently destroying every schedule. Check the counts before and after: they must
> match.

---

## 4. Number field

The site convention's **type-banded numbering** is retained. It comes from the US National
CAD Standard, is compatible with ISO 19650 (which leaves the Number field to the project),
and is more useful than a flat sequence.

| First digit | Holds |
|---|---|
| `0` | General — cover, drawing list, key plans, legends |
| `1` | Plans — floor, site, roof, reflected ceiling |
| `2` | Elevations |
| `3` | Sections |
| `4` | Large-scale / enlarged views |
| `5` | Details |
| `6` | Schedules and diagrams |
| `7` | Schematics, risers, single-line and system diagrams |
| `8` | Reserved — coordination, sketches, mark-ups |
| `9` | 3D — isometrics, axonometrics, perspectives |

---

## 5. Renaming what is already issued

**Do not rename anything already published.** A published container is contractual and its
number appears in transmittals, registers and correspondence already sent. Superseding
revisions carry the new form; the register records both.

For WIP and Shared containers:

1. Rename the container, not just the file — the register entry, the transmittal reference and
   the model's Project Information must move together.
2. Apply §3's one-pass rule to the type field.
3. Re-issue the register so the old and new names are both traceable.
4. Where a container was issued as `-G-`, note in the register that it was a Civil container
   under the withdrawn code, not a land-survey one.

**The originator register is still outstanding.** Codes remain `[ORG]` until the Lead
Appointed Party issues it. Nothing should be numbered against a guessed originator code — that
is a second rename on top of this one.
