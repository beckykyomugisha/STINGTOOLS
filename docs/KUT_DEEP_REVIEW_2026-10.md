# KUT deep review — October 2026

**Started 2026-10-04** on `claude/kut-deep-review-2026-10`, branched from `main` @ `744840139`
(after PRs #1059–#1062). Baseline on that commit: `dotnet build StingTools/StingTools.csproj`
0 errors / 0 warnings; `StingTools.Tags.Tests` 5,685 passed; KUT document gate OK.

This is a repeated, multi-angle review of the Kampala Uganda Temple (KUT) work and of everything
STINGTOOLS does for KUT. It finds defects, gaps and inconsistencies, fixes what is clearly right to
fix with a Revit-free test shown red before and green after, and logs everything larger in
[`ROADMAP.md`](ROADMAP.md) for a decision.

> Internal `docs/` file. It names products, commands and parameters freely; none of that
> vocabulary belongs in anything issued to the Owner.

---

## Method

Each loop runs nine read-only expert agents in parallel, one per angle, each with a
self-contained brief (scope, files, rules, output format). The lead then:

1. consolidates and de-duplicates their findings;
2. verifies every critical / high / medium finding against the code personally — anything not
   reproducible from the code is rejected, with the reason recorded below;
3. fixes verified findings, smallest correct change first, each with a Revit-free test shown red
   without the fix and green with it, then greps for the same pattern across plugin and server;
4. implements small, safe design improvements; logs large or behaviour-changing ones in the ROADMAP;
5. builds (0 errors / 0 warnings), runs the affected test projects, commits one logical change per
   commit, and drives CI green.

The next loop's agents receive the previous loops' summaries and are told to go deeper, where
nobody has looked yet.

**Stop rule.** At least three loops; stop when a full loop yields no new verified critical, high
or medium finding and every fix has its test. If loop 6 still finds critical or high items, stop
and report what blocks.

### The agents

| # | Angle | Prefix |
|---|---|---|
| 1 | ISO 19650 information manager — naming, containers, suitability / CDE state, BEP / MIDP / TIDP consistency, owner-pack rules | ISO |
| 2 | Revit API engineer — transactions, storage types and units, collectors, regeneration, 2025/2026/2027 differences, failure handling | API |
| 3 | MEP / FM asset engineer — codes, asset register, COBie completeness, maintainability data, Niagara points vs tags | MEP |
| 4 | Interiors / Fohlio specialist — finishes model, room matching, round-trip fidelity, conflicts | FOH |
| 5 | Integration engineer — ACC, Niagara, Fohlio, server: auth, errors, retries, idempotency, partial failure, secrets | INT |
| 6 | Performance engineer — large models, repeated collectors, O(n²), per-element transactions, I/O in loops, caches | PERF |
| 7 | Accuracy and consistency auditor — one fact two ways, docs vs code, data vs POCOs, empty scope reading as a pass | ACC |
| 8 | Flexibility and design reviewer — hard-coded KUT specifics, what a second owner breaks, how it should work better | DES |
| 9 | Test and QA engineer — coverage, vacuous tests, missing red-before-green, CI gaps | QA |

### Rules every agent and every fix followed

No deploy, no manifest change, no Revit. No force-push. No secrets anywhere. Never invent data:
an unknown value is `REPLACE_WITH_…` or `TODO(KUT):` and is reported. Never delete a deprecated
shared parameter or its GUID. Never skip, disable or weaken a test. A check that could not run says
UNVERIFIED / NOT CHECKED, never pass.

---

## Lead findings (before loop 1)

| ID | Finding | Evidence | Status |
|---|---|---|---|
| L-1 | **The KUT working models run with no KUT configuration.** The six models being worked on are opened from `C:\Users\del\Downloads` (Revit journals; files saved 2 Oct). Nothing sits beside them — no `_BIM_COORD`, no `project_config.json` — and Project Information → Number was never set, so the project root resolved to `Downloads\PROJECTN` (15 folders created 2 Oct). Every tag, audit and LOD check on those models has run on corporate defaults: BLD1–BLD3 only, no owner-standards rules, no KUT LOD overlay, no KUT tag scheme. The project-folder overlay (`KUT2026\00 Project Standards and Control\_BIM_COORD`) is a master copy no model reads. | journals; `Get-ChildItem Downloads`; `ProjectFolderEngine` resolves from `Document.PathName` | **Owner decision** — where the KUT working models live, and setting Project Information → Number. Not changed: it moves the user's models. |
| L-2 | **IM-17 is closed in the ROADMAP, but a prompt still calls it pending.** `docs/ROADMAP.md` row IM-17 reads CLOSED 2026-09-27 (one `TransmittalStatus` vocabulary; ACC publish records PREPARED; upload promotes it). `docs/PROMPT_ACC_UNATTENDED_OPERATION.md:14, :386` still say it is a pending decision. Reported, not decided. | as cited | Report only |

---

## Loop 1

Nine agents returned **149 findings** (ISO 18 · API 12 · MEP 17 · FOH 18 · INT 20 · PERF 14 ·
ACC 17 · DES 15 · QA 18). Every critical / high / medium one was read against the code by the lead
before anything was changed. Two more items came from the lead's own sweeps for siblings (MEP-14)
and from the team-playbook agent (KUTDR-2, KUTDR-13).

### Fixed in loop 1 (each with a test shown red before and green after)

| Commit | Finding(s) | Fix | Test |
|---|---|---|---|
| `2e7b7c7dd` | API-1, API-3 | Six Extensible Storage schemas had double fields with no spec, so Revit refused to build them ("Units are required for field CompliancePct" in the live log) and nothing in them was ever stored — among them the Fohlio snapshot and the compliance baseline. | `EsSchemaUnitSpecTests` |
| `02978b5fc` | FOH-1 and siblings, ACC-1, API-6, DES-1 | Fohlio round trip: match by identity (UniqueId → Fohlio ref → a key unique on both sides), refuse re-pointing and double claims, never guess a price or a currency, honour fill-empty, preview always shown, map read once; FF&E % is n/a over nothing. | `FohlioImportPlannerTests` (31), `ShippedFohlioMapTests` |
| `cecddc802` | MEP-4, ACC-2, ACC-5 | LOD gate read a NUMBER requirement as text, so the KUT LOD-500 gate could never pass; pass rate printed 100 % over nothing. | `LodRequiredParamTypeTests`, `LodPassRateTextTests` |
| `3ebc19f37` | MEP-1, MEP-2, MEP-13 | COBie lost every component from four sheets through an identifier mismatch; invented install / warranty dates and job frequencies. | `CobieExportIntegrityTests` |
| `b071eced9` | ISO-1, ISO-3 | Duplicate scheme identifiers made visible; no element filed under a real volume (`00`) by default. | `KutSchemeIdentifierTests` |
| `39710eecd` | ISO-6, ACC-13 | Owner-standards audit: a rule that examined nothing is NOT ASSESSED, never green. | `OwnerStandardsRagTests` |
| `c6f53afc0` | ACC-15 | The KUT sheet rule's own documented example failed the rule. | `OwnerRuleExampleTests` |
| `a6787aede` | INT-2, -4, -10, -11, -13, -14 | ACC clash pull read one page; a cap silently truncated; "not ready" read as failure; escalation ledger not atomic and a corrupt one read as empty, so issues were duplicated. | `AccEscalationIntegrityTests`, `AccCommandLedgerGuardTests` |
| `a709227a6` | INT-7, INT-8 | Server: any member could use the project's shared ACC connection; a sync in which every push failed recorded OK. | `ProjectAdministerCapabilityTests`, `AccSyncVerdictTests` |
| `0ff649323` | DES-3, ACC-4, ACC-7, ISO-18, QA-5 | A KUT-only workset rule was enabled in the corporate pack; a workflow label promised per-building worksets nothing makes; MonthlyReport ran one command twice under two names; the workflow gate now catches both. | `CorporateOwnerPackNeutralityTests`, `check_kut_workflow_tags.py` |
| `8e5b06d54` | API-2 | `ASS_CST_STALE_BOOL` is YESNO; three writers only wrote a String binding, so stale cost rows were never set or cleared. | `YesNoParamStorageGuardTests` (all 295 YESNO params) |
| `772cf7f69` | API-7 | Three copies of the BMS-monitorable category list, all with "Duct Accessory" (not a Revit name) and none with Mechanical Control Devices. One list. | `BmsMonitorableCategoryTests` |
| `90b7eb1db` | MEP-3 | Warranty Tracker wrote today + a default period as the expiry on every asset. Now only from recorded start + duration, else MISSING. | `WarrantyExpiryTests` |
| `f361a81bd` | MEP-5 | QR commissioning stamped a timestamp the date rule rejects, on every state change. Now the commissioning date, yyyy-MM-dd. | `CommissioningDateTests` |
| `052a3957d` | MEP-14 (sibling sweep) | Asset Condition wrote "A - Good"; Maintenance Schedule wrote default intervals and next-due from today; Sensor Mapper wrote a made-up BMS address; every tagging run wrote the tagging day as installation date and Mark as serial number. | `NoInventedAssetDataTests` |
| `e8d9441e1` | INT-17 | The BCC "Upload Model to ACC" card skipped the step that marks the ACCPublish transmittal SENT. | `AccUploadMarksSentGuardTests` |
| `683c9f0f7` | ISO-12, ISO-13, ISO-14 | Five transmittal-id allocators collided; MarkSent picked a duplicate auto row; the CDE-move row claimed an issue date. One allocator. | `TransmittalIdAllocationTests` |
| `d7b90203a` | KUTDR-1 | KUT identifier carries SYS (agent, with the ISO 19650 analysis in the final report). | `KutSchemeIdentifierTests` |
| `76bfd4bc1` | ACC-3 + 9 siblings | Classification standard written to the consolidated folder, read from the raw one (reverted on reopen); nine registries had the same raw read. | `ProjectOverridePathGuardTests` |
| `ce4eab62b` | PERF-1 | Linked models re-ran DocumentOpened and overwrote the process-wide TagConfig. | `LinkedDocumentGuardTests` |
| `e4f617856` | ACC-10, ACC-12 | KPI health gave 25 clash points to a model never clash-tested; "Sheet ISO 19650 compliance" was SHT_TAG_1 coverage. | `OwnerKpiHealthTests` |
| `1cfcc552b` | INT-12 | One ACC Issues v1 status mapping (source: APS OpenAPI `construction/issues/Issues.yaml`), unknown statuses counted and reported, pushes refused for statuses ACC would not accept. | `AccIssueStatusMapTests`, `AccIssueStatusMapServerTests` |
| `cea0f6acc` | API-5 | LOD Stamp downgraded higher milestones and never withdrew a failed claim. | `LodStampRuleTests` |

### Logged for a decision

KUTDR-2 … KUTDR-16 in [`ROADMAP.md`](ROADMAP.md) — the ones that are large, change a deliverable or
where every file is written, or need an owner value.

### Rejected

| Finding | Reason |
|---|---|
| DES-9 as a defect | `project_config.json` beside the .rvt is the repo-wide convention (dozens of readers); the real problem is that the toggle persists the whole config — logged as KUTDR-4, not fixed in place. |
| CostCommands `p.Set("")` flagged by the YESNO sweep | False positive: the string written is the reason text, a different parameter. |
| Singular category names in BOQ / LPS / symbol code ("Air Terminal", "Sprinkler") | Substring tests and descriptions, not exact category matches. |

### Loop 1 verification

Plugin build 0 errors / 0 warnings. `StingTools.Tags.Tests` 5,736 · `StingTools.Acc.Tests` 181 ·
`StingTools.Boq.Tests` 1,426 · `StingTools.Mep.Tests` 87 · `Planscape.Tests` 1,047 passed / 19 skipped.
KUT document gate (153 assertions) and workflow gate OK. Nothing was run in Revit.

## Loop 2 *(interrupted)*

Nine read-only reviewers were launched on 2026-10-04 with the loop-1 record and an instruction to
go deeper. **All nine stopped before reporting** — the account hit its usage limit (HTTP 429,
resets 19:40 EAT). No loop-2 finding exists yet, so **the stop rule is not met**: loop 2 has to be
re-run, then at least loop 3. Done in the meantime: both corporate tag-scheme examples carry SYS
(`EveryShippedCorporateSchemeCarriesTheSeqGroup`, red on the previous file, green now).

