# BlenderBIM / Bonsai MVP scope

Captured from the architectural conversation that produced Phase 185.
The MVP is the next major deliverable after Path A verification + PR to
main.

> **Goal**: 16 production operators in 8 weeks (single dev) / 5 weeks
> (pair) that prove StingTools-for-Bonsai is a daily-driver tool.

## Success demo — 5 minutes, no slides

This is the acceptance criteria. If you can do this end-to-end without
scripts, the MVP ships.

1. Open a federated IFC (Revit-exported + ArchiCAD-exported) in Blender
   with Bonsai + STING enabled.
2. STING N-panel shows: **132 untagged · 8 partial · 0 complete**
   (compliance: 0%, RAG: red).
3. Click **Auto Tag (active view)** → progress bar → **140 tagged,
   IDS validation 138 pass / 2 fail**.
4. Click a failed element → selected in viewport; tooltip reads
   `LOC_MATCHES_BUILDING: LOC "ACU" ≠ building.LocationCode "WAC"`.
5. Click **Raise Issue** → fill RFI form → issue lands in Planscape
   Server, immediately visible in Planscape mobile.
6. Save → IDS re-validates on save → audit-log entry with SHA-256 chain.
7. Run **Daily QA workflow** → 8 conditional steps → compliance
   dashboard turns green.

If you can't do this, MVP isn't done.

## 16 in-scope commands

| Category | Operator | Effort (days) |
|---|---|---|
| Selection | `sting.select_untagged`, `sting.select_stale`, `sting.select_by_discipline` | 2 |
| Tagging | `sting.auto_tag`, `sting.tag_selected`, `sting.tag_and_combine`, `sting.retag`, `sting.family_stage_populate` | 8 |
| Tokens | `sting.set_disc`, `sting.set_loc`, `sting.set_zone`, `sting.set_lvl`, `sting.assign_numbers`, `sting.build_tags`, `sting.combine_parameters` | 4 |
| Validation | `sting.validate_tags`, `sting.completeness_dashboard` | 3 |
| Coordination | `sting.raise_issue`, `sting.sync_to_planscape` | 5 |

**16 ops · 22 dev-days of operator logic.** Everything else is
infrastructure those ops depend on.

## Out of scope (defer to V1.1+)

- Sheet manager — no native sheets in Blender; full PDF pipeline = own project
- Schedule manager — use openpyxl/CSV export instead
- Smart tag placement (visual tag annotations) — Blender's annotation
  rendering is a separate effort
- Family creator — Bonsai's Library + Asset Browser fills the role
- Material commands — use Bonsai's existing material UI
- Healthcare pack — Phase 186
- 4D/5D scheduling — Planscape's web UI already has it
- COBie export — Planscape server can generate from federated IFC
- Revisions / transmittals / deliverables — Planscape template engine
- DWG auto-modelling — Revit detour covers it for MVP
- Carbon / BREEAM calculators — substrate carries values, reports defer
- Full workflow engine — ship 3 essential presets only

## Module structure

```
stingtools-bonsai/
├── blender_manifest.toml
├── __init__.py
├── core/
│   ├── bonsai.py                  EXISTS — BonsaiBridge
│   ├── state.py                   NEW — cached EnumRegistry + PsetRegistry singletons
│   ├── enum_loader.py             NEW — Blender-side overlay merge wrapper
│   ├── tag_pipeline.py            NEW — port of TokenAutoPopulator
│   ├── spatial_apply.py           NEW — write to native IFC entities via BonsaiBridge
│   └── ids_runner.py              NEW — wraps stingtools_core.ids.IdsRunner
├── ops/
│   ├── about.py                   EXISTS
│   ├── reload_substrate.py        EXISTS
│   ├── bonsai_probe.py            EXISTS
│   ├── selection.py               NEW — 3 selection ops
│   ├── tagging.py                 NEW — 5 tagging ops
│   ├── tokens.py                  NEW — 7 token writers
│   ├── validation.py              NEW — 2 validation ops
│   └── coordination.py            NEW — 2 coordination ops
├── ui/
│   ├── panel_main.py              EXISTS — extends to nested layout
│   ├── panel_select.py            NEW
│   ├── panel_tags.py              NEW
│   ├── panel_validate.py          NEW
│   └── panel_coord.py             NEW
├── handlers/
│   ├── on_save.py                 NEW — IDS validate before save
│   ├── on_depsgraph.py            NEW — stale-tag detection
│   └── on_load.py                 NEW — enum/pset loader trigger
├── planscape/
│   ├── client.py                  NEW — wraps stingtools_core.planscape.PlanscapeClient
│   └── signalr.py                 NEW — IfcElementChangedHub subscription
└── workflows/
    ├── engine.py                  NEW — JSON-driven runner (subset of WorkflowEngine.cs)
    ├── DailyQA.json               NEW
    ├── ProjectKickoff.json        NEW
    └── IssueDeliverable.json      NEW
```

## Timeline — 8 weeks single dev

| Week | Deliverable |
|---|---|
| 1 | Plugin skeleton already exists. Build out `core/state.py` (cached registries), `handlers/on_load.py` (IFC-open trigger), hot-reload working |
| 2 | `core/enum_loader.py` Blender-side wrapper + project overlay merge; ports of `TokenAutoPopulator` into `core/tag_pipeline.py` |
| 3 | Tagging ops: Auto Tag / Tag Selected / Tag and Combine / Re-Tag / Family-Stage Populate. Token ops: Set DISC/LOC/ZONE/LVL, Assign Numbers, Build Tags, Combine Parameters |
| 4 | `core/ids_runner.py` (ifctester wrapper), `core/spatial_apply.py` (cross-entity closeout), Validate Tags + Completeness Dashboard ops, IDS report panel |
| 5 | `planscape/client.py` + JWT/refresh. Audit log JSONL with SHA-256 chain. SignalR client subscription |
| 6 | Workflow engine port + 3 preset JSONs. Issue raise op with BCF viewpoint. Resolve All Issues |
| 7 | Stale detection via depsgraph handler. On-save IDS validation. Polish UI, error handling, progress feedback |
| 8 | Test fixtures wired up. CI workflow for the Bonsai extension itself. Documentation. Beta release tag. Demo dry-run |
| (+1) | Buffer week |

**Pair**: weeks 1-3 split (one builds core/loaders, other builds ops/UI).
Weeks 4-8 mostly serial integration. Estimate 5 weeks calendar.

## Risks

1. **Bonsai API drift** — pin Bonsai version in `blender_manifest.toml`;
   revisit each Bonsai release.
2. **IFC GUID instability** — Revit sometimes mints new GUIDs on
   re-export. `ExternalElementMapping` table on Planscape handles this;
   each host plugin must respect "if GUID changed since last sync,
   treat as new element". Spec the rule explicitly in
   `core/tag_pipeline.py`.
3. **Performance on >100MB IFCs** — architect every operator to be
   progressive (yield to UI, process in batches of 500). Test on a real
   hospital IFC by week 6.
4. **ifctester in-process ergonomics** — wrap cleanly in `core/ids_runner.py`.
5. **Cross-host GUID join failures** — `ExternalElementMapping` must be
   populated by BOTH Blender + Revit plugins. The Bonsai-side write
   ships in MVP; the Revit-side companion update is a separate
   1-day task on the Revit plugin.

## Success metrics

| Metric | MVP target |
|---|---|
| Daily-driver tagging workflow | works end-to-end on a real Revit-exported IFC |
| IDS validation | < 5 s on 50MB IFC, < 30 s on 200MB |
| Bonsai → Planscape issue | < 2 s sync with BCF viewpoint attached |
| Test coverage | round-trip harness passes on 3+ fixture IFCs; CI green |
| Documentation | README + walkthrough video + per-operator tooltips |
| Beta users | 2-3 friendly clients running alongside Revit/STING |
