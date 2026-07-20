# ISO 19650 Consolidation — In-Revit Verification Checklist

**Branch**: `claude/iso19650-consolidation`. Every change built clean (Release, 0/0) but the
Linux/CI build cannot exercise the Revit API. Run these in Revit before merging. Do them on a
**copy** of a real project (the migration/consolidation commands move files and write folders).

Tick order matters: some steps set up state the next one depends on (noted inline).

---

## A. No-regression baseline (existing projects unchanged)

- [ ] **A1. Existing BIM-tree project.** Open a project that already has a STING folder tree
  (`01_WIP…20_MISC`). Run a few exports (PDF, IFC, a schedule). Confirm they land in the SAME
  folders as before (`06_DRAWINGS`, `05_MODELS`, `07_SCHEDULES`), and no new/duplicate top-level
  folders appear next to the `.rvt`.
- [ ] **A2. Metadata folders.** Confirm coordination stores resolve under
  `<project>/<CODE>/_data/` (issues, document_register, meetings, transmittals) — one `_data`
  root, not scattered `_BIM_COORD` / `STING_BIM_MANAGER` siblings.
- [ ] **A3. Two projects, one folder.** Open two saved projects with different project Numbers
  from the same directory; run an export in each; confirm each writes into its OWN `<CODE>` root
  (WP6 per-document root fix — no cross-contamination).

## B. Suitability vocabulary (WP8.2)

- [ ] **B1.** Open the Document Register / BCC suitability dropdown. Confirm it now offers the
  full set incl. the authorization codes **A1–A5** and partial-sign-off **B1–B6** (not just
  S0–S7/CR/AB).
- [ ] **B2.** Open an existing document that already has an S-code (e.g. `S3`) or `CR`/`AB`;
  confirm its suitability + description still resolve (no orphaned rows).

## C. Workflow role gate (WP8.3)

- [ ] **C1.** In `project_config.json` set `USER_ROLE` to a non-approver role (e.g. `A`). Drive a
  workflow action whose transition restricts `allowed_roles` (transmittal flow). Confirm it is
  **denied** with a role message, and an audit entry `wf.transition_denied` is written.
- [ ] **C2.** Set `USER_ROLE` to `K` (Information Manager) or `C` (Coordinator); repeat; confirm
  it now **proceeds**.

## D. Unified register view + export (WP8)

- [ ] **D1.** BIM tab → **Unified Register**. Confirm a CSV is written to the REGISTERS export
  folder listing rows from BOTH stores with a `Source` column (`deliverable` / `register` /
  `both`), and the TaskDialog reports the three counts.
- [ ] **D2.** Confirm `deliverables.json` and `document_register.json` are byte-for-byte
  unchanged (read-only view).

## E. Register consolidation (WP8, dry-run command)

- [ ] **E1.** BIM tab → **Consolidate Register**. A dry-run preview CSV is written; the dialog
  shows counts. Click **No** → confirm NOTHING was written (no `register.json`).
- [ ] **E2.** Run it again, click **Yes** → confirm `<root>/_data/register.json` now exists, and
  the two source stores are still unchanged.
- [ ] **E3.** (Depends on E2.) Open the **Document Manager**; confirm its register list now shows
  the unified rows (deliverables + document-register, deduped). Edit a document-register row's
  field; confirm the edit persists (writes still go to `document_register.json`).

## DL. Deliverable state machine (WP8)

- [ ] **DL1.** Issue a deliverable (BIM → deliverable Issue). Confirm a workflow instance is
  created at state **WIP** — check `<root>/_data/_BIM_COORD/workflow_state.json` for an entry
  with `doc_id` = the deliverable number and `state: "WIP"`, and an `wf.started` audit line.
- [ ] **DL2.** Publish it at a published stage (stage ≥ 3). Confirm the instance advances to
  **Published** (walking Shared→Published), the deliverable's `WorkflowState` follows, and the
  audit shows the `share` then `publish` hops.
- [ ] **DL3. P→C promotion.** Confirm the deliverable's revision flips from the preliminary
  `P0n` to the contractual `C0n` when it reaches PUBLISHED.
- [ ] **DL4. Cancel.** Cancel a deliverable; confirm the instance moves to **Archived** (via the
  `cancel` transition). (On a project whose extracted workflow predates this change the hop is
  skipped with a log warning — Cancel still archives the record; re-extract or use a new project
  to exercise the transition.)
- [ ] **DL5. Role gate (opt-in enforcement).** In the project workflow override
  (`_data/_BIM_COORD/workflows/deliverable_issue_default.json`) add `"allowed_roles": ["K"]` to
  the `publish` transition, set `USER_ROLE` to a non-K/C role, and attempt Publish. Confirm it is
  **denied** (lifecycle returns a role message, nothing persisted) and a `wf.transition_denied`
  audit line is written. Set `USER_ROLE=K` and confirm it proceeds.
- [ ] **DL6. Permissive default.** On a project WITHOUT any `allowed_roles` override, confirm a
  normal user can still Issue/Publish (the gate infrastructure is live but does not block).

## RC. Render → CDE + register (WP8)

- [ ] **RC1.** Issue a deliverable that has a Discipline set (e.g. `A`). Confirm the rendered
  `.docx` lands under `<root>/00_WIP/<disc>/Documents/` (CDE-first) or `<root>/01_WIP_<CODE>/…`
  (BIM tree) — NOT in `_data/_BIM_COORD/generated/`.
- [ ] **RC2.** Confirm the rendered deliverable now appears in the document register with its
  `file_path` pointing at that CDE location.
- [ ] **RC3.** Confirm a **transmittal** render (Create Transmittal) still lands in
  `_data/_BIM_COORD/generated/` (only deliverables move to the CDE tree).
- [ ] **RC4. Move-on-transition (WP8, move-on-transition step).** Issue then Publish the SAME
  deliverable. Confirm the WIP render is **removed** and the current render lives under the new
  state folder (`…/02_PUBLISHED/<disc>/Documents/` or `03_PUBLISHED_<CODE>/…`) — no stale copy
  left behind in WIP.

## F. ES root-identity stamp (WP9)

- [ ] **F1.** Open a saved project (this stamps the root on first open). Note the export root
  folder name (`<CODE>`).
- [ ] **F2.** Edit the Revit project **Number** (which changes the derived CODE). Save, close,
  re-open. Run an export. Confirm it still lands in the **ORIGINAL** `<CODE>` root — NOT a new
  root named after the new number.
- [ ] **F3.** Confirm the stamp is written once (no repeated transactions on subsequent opens —
  check the log for a single "Stamp project root" entry per project).

## G. CDE-first tree for new projects (WP9)

- [ ] **G1.** Create a **brand-new empty** project; save it to an empty folder. Run any STING
  export. Confirm it lands under `<root>/00_WIP/<ContentType>/` (e.g. `00_WIP/Drawings/`), and the
  top level has states (`00_WIP`/`01_SHARED`/`02_PUBLISHED`/`03_ARCHIVE`) + cross-cutting folders
  (transmittals/issues/registers…) — NOT the numbered `05_MODELS…20_MISC` content folders.
- [ ] **G2.** Confirm a disc-scoped export nests under the content type
  (`00_WIP/<ContentType>/<Discipline>`).
- [ ] **G3.** (Opt-out.) Set `CDE_FIRST_LAYOUT=false` in `project_config.json`, create another
  new project, confirm it uses the numbered BIM tree instead.
- [ ] **G4.** Re-confirm A1 — an EXISTING project still uses its numbered tree (greenfield gate).

## H. Folder consolidation wizard (WP9, dry-run command)

- [ ] **H1.** On a copy of a project that has legacy sibling folders (`_BIM_COORD`,
  `STING_BIM_MANAGER`, `_CDE`, `STING_Exports`…), BIM tab → **Consolidate Folders**. Confirm the
  dry-run report CSV lists each legacy folder with its file count and destination. Click **No** →
  confirm nothing moved.
- [ ] **H2.** Run again, click **Yes** → confirm the files now live under `<root>/_data` (metadata
  buckets) / routed export folders, and the legacy siblings are gone or empty. Confirm the
  activity log recorded the move.

## J. Workflow instance hygiene (batch 3)

- [ ] **J1.** Take a deliverable all the way to **Published**. Open
  `<root>/_data/_BIM_COORD/workflow_state.json` and confirm there is exactly **one** instance for
  that doc number, `closed: true`, with a single WIP→Shared→Published history. Then run another
  lifecycle action on the same deliverable → confirm **no second instance appears** and the
  deliverable's workflow fields still read the terminal state (they used to reset to WIP).
- [ ] **J2.** Sign in as a role that is *not* permitted to Approve. From a WIP deliverable, run an
  action that needs two hops (e.g. Publish). Confirm **nothing** moved — `workflow_state.json`
  still shows the original state, no partial hop was written — and the denial names the required
  roles. Confirm `audit_log_*.jsonl` carries one `wf.transition_denied` row.
- [ ] **J3.** Rename an `allowed_roles` message wording in a workflow JSON is *not* needed any
  more, but sanity-check the gate still bites: as an unpermitted role, confirm the block message
  appears rather than the action silently succeeding.

## K. Revision arithmetic (batch 4)

- [ ] **K1.** A deliverable with an empty revision → issue it → revision reads **P01** (not P02).
- [ ] **K2.** Take P01 through to Published → revision reads **C01**.
- [ ] **K3.** Hand-edit a deliverable's revision to something unparseable (e.g. `REV-A`) in
  `deliverables.json`, then re-issue → confirm the value is **left unchanged** (a `+1` suffix is
  never written) and `StingTools.log` carries the "cannot increment revision" warning.
- [ ] **K4.** Confirm each transition appended a `RevisionHistory` row. If the log shows
  "no RevisionHistory list", the row schema needs fixing — the transition is otherwise silent.

## L. Store ownership + Save As (batch 4)

- [ ] **L1.** Put two *different* projects' `.rvt` files in one folder, each with its own legacy
  `_BIM_COORD` sibling. Open project A → confirm a hidden `.sting_legacy_owner` appears naming
  A's root. Open project B → confirm B **refuses** the merge (warning in `StingTools.log`) and
  B's issues/register do **not** contain A's rows.
- [ ] **L2.** Federated check: two models of the **same** project in one folder → both resolve to
  the same root, both merge normally, no warning.
- [ ] **L3.** **Save As** a project into a different folder, then create an issue and export the
  register. Confirm both land under the **new** project root, not the original one.

## M. Render naming + register fidelity (batches 3–4)

- [ ] **M1.** A deliverable identified only by `Code` (no `DocNumber`) → render it → confirm the
  output file is named with the Code, **not** `..._UNKNOWN_...`. Render a second such deliverable
  the same day → confirm two distinct files (they used to overwrite each other).
- [ ] **M2.** Set a deliverable's CDE to a non-canonical value (`ARCHIVED`, `Published`) →
  transition it → confirm the render lands in the matching CDE container, **not** `20_MISC`, and
  that the previous copy was purged.
- [ ] **M3.** Open the Document Manager on a project with `_data/register.json` → confirm the
  Status column shows the **description** (e.g. "Shared — suitable for information"), not the bare
  code, and that File Format / Created By are populated.
- [ ] **M4.** Temporarily rename `register.json` to make the merge yield nothing → confirm the
  Document Manager **falls back** to `document_register.json` rather than showing an empty list.
- [ ] **M5.** Put `=1+1` in a document title, export the unified register CSV, open it in Excel →
  confirm the cell shows the literal text, not a computed value.

## N. Replace (batch 4)

- [ ] **N1.** Select ONE deliverable → run Replace → confirm it refuses and asks for two.
- [ ] **N2.** Select two → confirm the first is marked `Replaced` with `SupersededBy` = the
  second's number, and the second carries `Supersedes` = the first's. Neither should reference
  itself.

## I. Regression gates (already green in CI, re-run if you touch the code)

- [ ] **I1.** `pwsh tools/check_path_discipline.ps1` → "0 remaining" (no new `_BIM_COORD` siblings).
- [ ] **I2.** `pwsh tools/check_dispatch_parity.ps1` → passes (no new panel-tag drift).

---

## Known-deferred (not in this branch — see docs/ROADMAP.md)

- **BCC repoint** — the BIM Coordination Center stays deliverable-focused (its grid runs
  deliverable-lifecycle bulk actions that register-only rows must not be exposed to).
- **Multi-model guid-sharing** — see the note in docs/ROADMAP.md: robust sharing already comes
  from the setup-file sibling scan + the ES stamp; a blanket guid auto-adopt would risk merging
  unrelated projects that happen to share a folder, so it needs a reliable grouping signal first.
  The `.sting_legacy_owner` claim added in batch 4 is the first half of the grouping signal this
  would need.

### Closed since this document was first written

- ~~Deliverable state machine end-to-end~~ — `DeliverableLifecycle` now drives the role-gated
  machine for every CDE-changing action (section **DL**, plus **J** for instance hygiene).
- ~~Render→WIP loop~~ — `TemplateEngine.RenderToCde` renders into `<state>/<disc>/Documents/`,
  registers the file, and purges the copy left in the previous state (sections **RC**, **M**).
- ~~Clash-store read/writer mismatch~~ — one `ClashPersistence.CanonicalPath(doc)` now serves all
  seven read/write sites.
