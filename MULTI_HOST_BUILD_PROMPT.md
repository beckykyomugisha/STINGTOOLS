# Prompt — Build run: installable Bonsai plugin (Part 1) + Phase B bidirectional sync (Part 2), one pass

Hand this to a **build-capable agent** with: Python 3.11, **Blender 4.2+/5.x with
the Bonsai add-on installed**, the **.NET 8 SDK**, and (ideally) a running
Planscape Server (`Planscape.Server/docker` compose, or mock where noted).

**Mission, in order:**

1. **Part 1 — Make StingTools-for-Bonsai a real, installable plugin** with working
   buttons, verified on a clean Blender + Bonsai install.
2. **Part 2 — Implement Phase B (bidirectional sync)** from
   `docs/MULTI_HOST_INTEGRATION_PLAN.md` §1.4 on top of that installed base.
3. **Part 3 — Re-package and prove the loop end-to-end**: a change pushed from one
   side is pulled and visible on the other, in the installed plugin.

**Why this order (do not reorder):** packaging is not a final compile step — it is
the delivery pipeline. Part 1 is what makes Part 2 *verifiable in the installed
environment* (path resolution, vendoring, prefs, network permissions all behave
differently installed vs. repo-checkout). Build B against the installed extension,
not against repo-mode luck.

Authoritative specs (in-repo, read them):
- `docs/MULTI_HOST_INTEGRATION_PLAN.md` — architecture; §1.0 hub-and-spoke, §1.4 sync.
- `MULTI_HOST_INTEGRATION_PROMPT.md` — the master A–E runner (Phase B section).
- `MULTI_HOST_BONSAI_INSTALLABLE_PROMPT.md` — the detailed Part-1 spec (vendoring,
  build, click-path). This document inlines its essentials; that one carries the detail.

Where docs disagree, the plan wins; STOP and flag the discrepancy rather than guess.

Repo: `beckykyomugisha/stingtools`. Branch off the latest default branch. Never
commit to it directly. Re-enumerate the live tree; trust git over this doc.

---

## 0. Standing decisions (locked — do not relitigate)

- **Hub-and-spoke, never host pairs.** Hosts talk only to Planscape. No
  Revit-in-Blender buttons; Revit/ArchiCAD/Tekla interop is via the COORD panel →
  Planscape.
- **Bonsai is a hard dependency** of the add-on. Headless runs via StingBridge +
  pip `ifcopenshell` (pinned `>=0.8.0,<0.9`). **Never bundle ifcopenshell** into
  the extension.
- **Core owns logic; adapters own glue.** The A6 boundary lint
  (`tools/ci/check_adapter_boundary.py`) must stay green throughout.
- **IFC GlobalId is the cross-host key.** Everything that syncs is keyed on it.
- **Conflict policy:** last-writer-wins on `LastModifiedUtc`; the losing edit is
  surfaced as a Planscape issue, never silently clobbered.

## 1. Hard rules

1. **Gate every part** (§2). Part 2 may not start until Part 1's click-path passes
   on a real install. Part 3's demo is the overall exit criterion.
2. **Additive.** Don't break the live `host=bonsai` push, `IfcController`/
   `TagSyncController`, `AutoAlignService`, or the Phase-A undo-aware write path.
3. **Vendored copies are build artifacts of the repo source** — a build-time drift
   check must fail if `_vendor/` diverges from `stingtools-core/python` or
   `shared/ifc/`. Never hand-edit `_vendor/`.
4. **Honest reporting.** ✅ only for checks you ran; ❓ with the reason otherwise.
   If Blender or a live server is unavailable, STOP and say so — don't simulate
   verification.
5. **No secrets, no force-push, no PR unless asked.** Push `-u origin <branch>`;
   retry network failures with backoff.

## 2. Build gates

```bash
# Always-green throughout the run
cd stingtools-core/python && python -m pytest -q
python tools/ci/check_adapter_boundary.py

# Part 1 exit gate
blender --command extension validate stingtools-bonsai
blender --command extension build --source-dir stingtools-bonsai --output-dir dist/
blender --background --python stingtools-bonsai/tests/verify_blender.py
#  + the manual/scripted click-path of §3.4 on an INSTALLED copy

# Part 2 exit gate
cd stingtools-core/python && python -m pytest -q          # incl. new sync tests
cd StingBridge && python -m pytest                        # sync subcommand tests
dotnet build Planscape.Server/Planscape.sln               # if server touched

# Part 3 exit gate — the end-to-end demo of §5
```

---

## 3. PART 1 — Installable plugin with working buttons

The N-panel + ~20 operators (SELECT / TAGS / VALIDATE / COORD + diagnostics)
already exist in `stingtools-bonsai/` and the COORD ops are real. The add-on is
just not installable: `_vendor/` is empty, there's no build script, and the
`shared/ifc/` substrate isn't bundled — so an installed `.zip` can't import
`stingtools_core` and shows nothing.

**3.1 Vendor at build time.** A build script (`stingtools-bonsai/build.py` or
`tools/build_bonsai_extension.py`) copies `stingtools-core/python/stingtools_core/`
→ `_vendor/stingtools_core/` and `shared/ifc/` → `_vendor/shared/ifc/`, writes a
manifest of copied files + hashes, then invokes
`blender --command extension build`. Add the drift check (rule 3).

**3.2 Path resolution, installed-first.** `__init__.py` prefers
`_vendor/stingtools_core` and sets `STINGTOOLS_SHARED_IFC` to the vendored
substrate when present; the repo-walk stays as the dev fallback. Confirm
`stingtools_core.paths.find_shared_ifc()` resolves vendored. Confirm
`blender_manifest.toml` doesn't exclude `_vendor/`.

**3.3 Every button functions.** Against a real IFC open in Bonsai, walk each
operator and fix what doesn't work:
- TAGS: `auto_tag` writes `Pset_StingTags` through the Phase-A2 `tool.Ifc.run`
  path (**Ctrl-Z reverts**); `assign_sequence`/`build_full_tags` correct.
- VALIDATE: `validate_ids` runs the real `shared/ifc/*.ids`; dashboard + summary
  strip render results.
- SELECT: selectors select the right viewport objects.
- COORD: login stores the token in add-on Preferences (server URL / email /
  project id — add prefs if missing); `sync_to_planscape` pushes
  (host="bonsai"); `raise_issue` posts. Surface success/failure in the panel.

**3.4 Acceptance click-path (on an INSTALLED .zip, clean Blender + Bonsai):**

| # | Check |
|---|---|
| 1 | `.zip` installs; add-on enables without error |
| 2 | `STING` tab visible in the 3D-view sidebar |
| 3 | Bonsai disabled → "Bonsai is required" banner, no ops |
| 4 | Bonsai enabled + IFC open → "core v… / Bonsai v…" header |
| 5 | Auto-Tag writes pset; **Ctrl-Z reverts** |
| 6 | Run IDS Validation produces results; summary updates |
| 7 | A SELECT button selects expected elements |
| 8 | Login + Push to Planscape succeed (live server; else mock + verify call shape, mark ❓) |

**3.5 Docs.** `stingtools-bonsai/README.md`: build command, install steps,
Bonsai prerequisite, screenshot of the STING tab, 60-second
tag → validate → push quickstart.

---

## 4. PART 2 — Phase B: bidirectional sync (build on the installed base)

All sync logic lives in **core**; hosts are thin consumers. Spec source:
plan §1.4 + master prompt Phase B.

**4.1 Pull client** — `stingtools_core/planscape/`:
`pull_changes(project_id, since_cursor) -> list[ChangeDelta]` over
`GET /ifc/mappings` + **issues/BCF/clash** changes (the review payload, not just
tags — this is what makes cross-host model review work). Stdlib urllib, matching
the existing client. If the server lacks a suitable change-feed endpoint, add a
minimal one (`GET /api/projects/{id}/changes?since=…`) following existing
controller conventions — keyed on GlobalId, cursor-paged, covering tag upserts +
issue/clash events; `dotnet build` + a test required.

**4.2 Reconciliation engine** — core: `reconcile(adapter, remote_deltas)`
resolving **LWW on `LastModifiedUtc`**; conflicts emit a Planscape issue. Unit
tests: newer-remote wins, newer-local wins, tie, conflict-issue emission,
idempotent re-run.

**4.3 Chunked push.** Split pushes into ≤500-element request bodies (server
batches at 500; client timeout is 30 s). Test with a synthetic 5k-element model.

**4.4 GlobalId stability fixture** — standing test asserting a fixture IFC's
GlobalIds survive the collect→push→pull round trip unchanged. Scaffold the
Revit-export half if no Revit fixture exists; mark ❓.

**4.5 Hosts consume the engine:**
- **Bonsai** — a **modal-timer pull operator** (`wm.event_timer_add`, interval
  from prefs, default 60 s) + a manual **"Sync now"** button in COORD. Deltas
  apply via `tool.Ifc.run` (undo-aware). Panel shows last-sync time + counts +
  conflicts. Respect the A6 boundary: the operator is glue; diff/apply logic is core.
- **StingBridge** — `python -m StingBridge.bridge sync --watch-interval N`:
  pull → reconcile → apply via `ifcopenshell.api` → write the `.ifc` back.

**Part 2 acceptance:** unit tests green for pull/reconcile/chunking; the
installed Bonsai add-on shows a working "Sync now" that applies a server-side
change to the open model with undo support.

---

## 5. PART 3 — Re-package + end-to-end demo (exit criterion)

1. Re-run the Part-1 build → fresh `.zip`; re-install.
2. **Demo loop**, scripted or recorded step-by-step:
   a. In Blender: tag elements → **Push to Planscape**.
   b. Simulate another host: POST a tag change + raise an issue on one of those
      GlobalIds via the server API (curl/python — stands in for Revit until its
      pull lands).
   c. In Blender: **Sync now** → the changed tag appears on the element
      (Ctrl-Z reverts), and the issue is visible in the panel.
3. Capture evidence: terminal transcript + screenshots (or a short capture).

This demo IS the deliverable narrative: *any host's change reaches Blender
through the hub, in an installed plugin.*

---

## 6. Definition of Done — report this matrix

| Check | Part | ✅/❓/❌ |
|---|---|---|
| Vendoring build script + drift check | 1 | |
| `.zip` builds + validates | 1 | |
| Click-path 1–8 on installed copy | 1 | |
| README install docs + screenshot | 1 | |
| Pull client (+ change-feed endpoint if added, with dotnet test) | 2 | |
| Reconcile engine + LWW/conflict tests | 2 | |
| Chunked push (5k-element test) | 2 | |
| GlobalId round-trip fixture | 2 | |
| Bonsai modal-timer pull + Sync now (undo-aware) | 2 | |
| StingBridge `sync` subcommand | 2 | |
| Re-packaged `.zip` + end-to-end demo with evidence | 3 | |
| Core pytest + boundary lint green throughout | all | |

## 7. Guardrails

- No ifcopenshell in the `.zip`; no Revit button; no host-pair code paths.
- Pull must never corrupt local work: apply only via the adapter
  (`tool.Ifc.run` in Blender), and never auto-apply a conflict — surface it.
- The modal timer must not block the UI thread or fire during render/modal
  operations; guard re-entrancy (skip a tick if a sync is already running).
- Network failures in pull/push are reported in the panel and retried next
  tick — never an unhandled exception in an operator.
- Keep `verify_blender.py` updated as features land; it's the regression net.

## 8. Reporting

End with: the §6 matrix (honest), the build command + `.zip` artifact path, the
demo evidence, any server endpoint added (+ tests), divergences from the spec
docs you corrected, and anything ❓ with the reason.
