# Kampala Uganda Temple — STING project profile (interim)

A reusable, project-scoped STINGTOOLS configuration for the Kampala Temple
project (Symbion Consulting Group Studios). It is **specific to this project but
built to be cloned** for the next temple/multi-building job: copy the folder,
rename the building codes, and you have a new profile.

> **Status: INTERIM.** Attachments A1 and A2 define **no element tag-token
> scheme** — A1 mentions tags once, as a quality-control check; A2 not at all.
> The real tag/LOD standard is the **Owner BIM modeling / prototype standard**
> on the SPD Temple Design Hub, which A2 §A says the team *"will be provided."*
> This profile is the ISO 19650 BEP interim used **until that standard is
> issued**, at which point the tokens are finalised in one pass.

## What this profile does

| Decision | Setting |
|---|---|
| **Full ISO 19650 asset tag** | `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ` (e.g. `M-TPL-Z01-01-HVAC-SUP-AHU-0003`) |
| **Separator / number pad** | `-` / 4 digits |
| **Location = building = ISO Volume** | 6 buildings + external + project-wide (table below) |
| **Systems** | Aligned to the project's **CSI MasterFormat divisions** (A2) |
| **Status / Revision defaults** | `NEW` / `P01` |
| **Collision mode** | `AutoIncrement` (no duplicate SEQ) |
| **Compliance gate** | `0` (disabled — does not block during early authoring) |

## The full tag, mapped to ISO 19650 fields

The eight segments are a **superset** of the ISO 19650 container fields plus a
full asset classification. Crucially, **ISO "Volume" is carried by `LOC`** (the
building) — there is no missing token and no need for a 9th segment.

| Segment | Meaning | ISO 19650 field |
|---|---|---|
| **DISC** | Discipline (M, E, P, A, S, FP, LV, C) | Role |
| **LOC** | **Building** | **Volume** |
| **ZONE** | Sub-zone within a building | (STING extension) |
| **LVL** | Level (GF, 01, B1, RF …) | Level / Location |
| **SYS** | System (CSI-aligned — see below) | — |
| **FUNC** | Function (SUP, HTG, PWR …) | — |
| **PROD** | Product (AHU, DB, DR …) | — |
| **SEQ** | 4-digit sequence | Number |

`DISC`, `LOC`, `ZONE`, `LVL` and `SEQ` auto-derive today from category +
spatial + phase data. `SYS / FUNC / PROD` populate from STING's defaults
(the CSI-aligned `SYS_MAP` below, the default function map, and family-aware
product codes); their **values** are re-confirmed in one pass when the Owner
prototype standard issues — but the full tag **structure** is in place now.

The project + originator + type fields of the KUT container code
(`KUT-ZZZ-XX-XX-M3-A-0001`) are project-level and live on the **sheet/model
name** via the Drawing engine, not on every element.

## Volume / building crosswalk (LOC ↔ KUT Volume)

ISO 19650 puts **Volume on the container/sheet name**, not the element tag. The
STING Drawing engine already emits
`{Project}-{Originator}-{Volume}-{Level}-{Type}-{Role}-{Number}`. Use these
Volume codes on sheets/models; the matching `LOC` code goes on elements.

| Building (A1) | Element `LOC` | Sheet/model **Volume** (KUT) |
|---|---|---|
| Temple | `TPL` | `01` |
| Meetinghouse | `MTH` | `02` |
| Housing / ancillary | `HSG` | `03` |
| Grounds building | `GRD` | `04` |
| Utility building | `UTL` | `05` |
| Guard house | `GDH` | `06` |
| External / site | `EXT` | `XX` |
| Project-wide / multi-building | `ZZ` | `ZZ` |

## System ↔ CSI division (A2)

A2 structures the engineering work by CSI MasterFormat division. The `SYS_MAP`
in `project_config.json` follows that so model systems line up with the
SpecLink/CSI spec sections.

| `SYS` code | CSI division | System |
|---|---|---|
| `FP`, `FLS` | **21** | Fire protection / detection |
| `DCW`,`DHW`,`HWS`,`SAN`,`RWD`,`SWD`,`GAS` | **22** | Plumbing / drainage / gas |
| `HVAC` | **23** | Heating, ventilation, air conditioning |
| `LV`, `LPS` | **26** | Electrical / lightning protection |
| `COM`, `ICT` | **27** | Telecommunications / data |
| `SEC` | **28** | Safety & security |
| `ARC` | — | Architectural elements |
| `STR` | — | Structural elements |

This same `SYS` code is the natural key for the `SPEC_SECTION` parameter that
links a modelled product to its RIB SpecLink CSI section.

## How to use

1. Copy `project_config.json` into the project's STING config location
   (`<project>/_BIM_COORD/project_config.json`, or wherever
   `TagConfig.LoadFromFile` is pointed for the project).
2. Reload config in Revit (Tags → Configure, or restart the dock panel) so
   `TagConfig` picks it up.
3. Tagging commands (Auto Tag / Batch Tag / Validate) now produce
   full `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ` tags with the building (=ISO
   Volume) + CSI-aligned systems.

> **Gate:** STINGTOOLS has not yet been compiled/smoke-tested in Revit. Prove a
> clean build + one Auto Tag on a throwaway project **before** loading this on a
> live consultant model. This profile is correct on paper; the build is the
> thing still to verify.

## To clone for the next project

Copy this folder, then in `project_config.json` change `LOC_CODES` /
`CUSTOM_VALID_LOC` to the new buildings, update the Volume crosswalk here, and
trim `SYS_MAP` to the disciplines on that job. Everything else carries over.
