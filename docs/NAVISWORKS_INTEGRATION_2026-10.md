# Navisworks Manage ↔ STINGTOOLS ↔ ACC — integration plan (2026-10)

**Status:** ✅ current · researched 2026-10-01 on `claude/acc-work-review-gaps-7e2ac7` · NW-1 shipped, NW-2..NW-7 open in [`ROADMAP.md`](ROADMAP.md).

**Question asked:** the KUT team will use Navisworks Manage. What is the most flexible and sustainable way to integrate it with STINGTOOLS and ACC?

## The answer in one paragraph

**ACC is the hub. Each tool talks to ACC through Autodesk's supported paths and to the others through open files, and one element identity runs through all three.** STING already reads and writes ACC (Docs, Model Coordination clashes, Issues, Reviews). Navisworks Manage reaches ACC through Autodesk's own Coordination Issues Add-In. Nothing in this plan needs STING and Navisworks to call each other directly. Anything that must cross between them goes as a file Navisworks already imports or exports: NWC, search-set XML, clash-test XML, clash-report XML. That means:

- an Autodesk release cannot break a private bridge, because there is none;
- any one tool can be replaced without rebuilding the other two;
- every piece works without the others.

```
            ┌──────────── ACC (system of record) ─────────────┐
            │  Docs · Model Coordination · Issues · Reviews    │
            └───▲───────────────▲──────────────────▲───────────┘
   APS REST     │  (built)      │ Coordination     │ Desktop Connector /
   (STING)      │               │ Issues Add-In    │ Docs folders
            ┌───┴────┐          │ (Autodesk)       │
            │ STING  │  NWC ────┼─────────────────►│
            │ (Revit)│  search-set / clash-test XML ──► ┌────────────┐
            │        │◄── clash-report XML ──────────── │ Navisworks │
            └────────┘                                  │  Manage    │
                                                        └────────────┘
   identity: Revit element id + UniqueId (NWC "Element ID") + STING tag (ASS_TAG_1_TXT)
```

## What is possible (sourced)

| Capability | Fact | Source |
|---|---|---|
| ACC auto-clashes NWC | Tracked NWC from Revit supports aggregation **and** automatic clash detection. NWD supports aggregation only. | [Supported Files in Model Coordination](https://help.autodesk.com/cloudhelp/ENU/Coord-GS/files/Model_Coord_Supported_Files.html) · [NWD in Model Coordination](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-use-NWD-files-in-ACC-Model-Coordination.html) |
| Navisworks ↔ ACC issues | The Coordination Issues Add-In opens Model Coordination models and views in Navisworks and syncs issues both ways. The models must be in Autodesk Docs, inside a coordination space. | [Coordination Issues Add-In (2026)](https://help.autodesk.com/cloudhelp/2026/ENU/Navisworks/files/GUID-92D8E626-BB61-4CB8-AA46-D9E5A9517D65.htm) · [Syncing clash issue status](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/How-to-sync-clash-issue-status-between-Navisworks-and-Autodesk-Construction-Cloud-ACC.html) |
| Search sets as XML | Saved search sets export and import as XML. | [Search-set XML (Autodesk forum)](https://forums.autodesk.com/t5/navisworks-forum/selection-set-xml-export-and-import-navisworks-manage-2009-and/td-p/2686191) |
| Clash tests as XML | Clash Detective imports and exports tests as XML, **only when the tests use search sets**. Tests built on hand-picked items are not exported. | [Export Clash Tests](https://help.autodesk.com/cloudhelp/2017/ENU/Navisworks-Manage/files/GUID-9E08D2D7-5DA8-48E4-A04B-12BEDFF4C189.htm) · [Import a Clash Test](https://knowledge.autodesk.com/support/navisworks-products/learn-explore/caas/CloudHelp/cloudhelp/2017/ENU/Navisworks-Manage/files/GUID-9646B7AE-F903-43C4-832C-BADAD2399319-htm.html) · [Report export/import limits](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Navisworks-Clash-Report-export-import-not-working-as-expected.html) |
| Clash results as XML | A Clash Detective report in XML format holds every clash, its details and a JPEG viewpoint. | [To Create a Clash Report](https://knowledge.autodesk.com/support/navisworks-products/learn-explore/caas/CloudHelp/cloudhelp/2017/ENU/Navisworks-Manage/files/GUID-3D392AAA-EB37-452A-B621-72D6E129C9D0-htm.html) |
| Revit NWC options | `NavisworksExportOptions` has `ExportScope` (Model / View / SelectedElements), `Coordinates` (Shared / Internal), `ExportElementIds`, `Parameters` (None / Elements / All), `ViewId`, `ExportLinks`, and more. `OptionalFunctionalityUtils.IsNavisworksExporterAvailable()` reports whether the exporter is installed. | Revit 2025 `RevitAPI.xml`, checked 2026-10-01 |
| ACC clash API | Read only. Clash tests run on every new model-set version and cannot be started by the API. | [Model Coordination clash field guide](https://aps.autodesk.com/en/docs/acc/v1/overview/field-guide/model-coordination/mcfg-clash/) |

### What is NOT possible, so do not plan on it

- **No Navisworks in the cloud.** APS Design Automation has no Navisworks engine. Navisworks-side automation needs a licensed Navisworks on a machine.
- **ACC cannot run Navisworks clash tests or take their rules.** ACC clashes every model pair in a set by its own rules. Clearance tests, tolerance tests and NWD/point-cloud tests stay in Navisworks.
- **ACC does not clash NWD.** Publish tracked NWC (or RVT), never NWD, into the coordination folder.
- **Navisworks has no built-in BCF.** BIMcollab and similar plug-ins are third-party. The issue round-trip goes through ACC Issues.
- **The Navisworks .NET API is .NET Framework 4.8.** STING's Revit 2025 plug-in is .NET 8. A Navisworks plug-in would be a separate project and a separate install (NW-7).

## Recommended three-way workflow

1. **Revit → model in ACC.** STING already uploads models (`ACC_UploadModel`). If a discipline publishes NWC instead of RVT, export it from the Export Centre. As of NW-1 that exports the whole model in shared coordinates, with element IDs and all parameters. Upload it to the folder the coordination space watches (NW-2 automates this).
2. **ACC clashes it automatically.** STING's **Pull Clashes** reads the result, escalates the worst groups to ACC Issues and tracks them until they are resolved (built and tested; smoke tests S2–S4 in [`KUT_ACC_SETUP_AND_SMOKE_TEST.md`](KUT_ACC_SETUP_AND_SMOKE_TEST.md)).
3. **Navisworks Manage handles detailed coordination.** Open the same models through the Coordination Issues Add-In. Use STING-generated search sets (NW-3) and clash tests (NW-4), so Navisworks groups and tests by the same DISC / SYS / LVL / ZONE tokens STING tags with.
4. **Issues go one way, into ACC.** Issues raised in Navisworks go to ACC Issues through the add-in. STING's Sync Issue Status and Import Issues pick them up. There is no separate STING ↔ Navisworks issue channel to maintain.
5. **Navisworks-only clash results come back to Revit.** A Clash Detective XML report is imported by STING (NW-5). STING maps each clash to the Revit element by the NWC Element ID, then stamps it, colours it in a view and escalates it to ACC like an ACC clash.

**The identity contract.** An element has to be recognisable in all three tools. NWC carries the Revit element id (with `ExportElementIds`). With `Parameters = All` it also carries STING's tag parameters, including `ASS_TAG_1_TXT`. Every NW-* item matches on Element ID first, then on the STING tag; never on names or positions.

## Roadmap

| Id | Item | Needs | Status |
|---|---|---|---|
| NW-1 | Fix the Export Centre NWC defaults. They exported an empty selection in internal coordinates; now the whole model, shared coordinates, element IDs, all parameters. Enums set by name; the real exporter check; the failure reason reported. ExLink NWC checks the exporter and the written file. | — | ✅ shipped `78895e663` |
| NW-2 | "Publish NWC to the coordination folder": Export Centre option + workflow step reusing `AccModelUpload`, targeting the model set's folder. | Plugin only | open |
| NW-3 | Generate Navisworks search-set XML from STING tokens: per discipline, system, level and zone. | One search-set XML **exported from your Navisworks** as the schema sample. The format is not published; field names will not be guessed. | open · NEEDS SAMPLE |
| NW-4 | Generate clash-test XML from STING's clash rule matrix (`ClashRuleEngine`), built on NW-3's sets, so one rule set drives the Revit, Navisworks (and grouping in ACC) checks. | A clash-test XML exported from your Navisworks | open · NEEDS SAMPLE |
| NW-5 | Import a Clash Detective XML report: map by Element ID, stamp and colour, escalate through the existing ACC issue path. | A clash-report XML from your Navisworks | open · NEEDS SAMPLE |
| NW-6 | Documentation only. Standardise on the Coordination Issues Add-In for Navisworks ↔ ACC issues; add it to the KUT guide. | — | open |
| NW-7 | *Optional, last.* A .NET 4.8 Navisworks plug-in ("STING" tab): load NW-3/NW-4 files from the project's `_data/coord`, run tests, write NW-5 reports. Optional overnight runs via Navisworks' command line / Task Scheduler. | Navisworks licence on a build machine; a separate installer | deferred until NW-3..NW-5 prove too manual |

**Order matters.** NW-2..NW-5 are file-based. They work with any Navisworks version and need no install. Build NW-7 only if the file round-trip turns out to be the bottleneck.

**One open decision for the team, not the code: `ExportLinks`.** ExLink's NWC still embeds linked models (`ExportLinks = true`). In a federated workflow, where every discipline publishes its own model, that geometry appears twice and produces false clashes. The Export Centre path leaves the exporter's default. Pick one rule per project.
