# Prompt — Implement Phase A (Foundation Hardening) in one pass, A1–A6

Hand this to a **build-capable agent with a real environment**: Python 3.11,
**Blender 4.2/5.x with the Bonsai add-on installed**, and the **.NET 8 SDK**.
Mission: implement and **verify** all six Phase-A items (A1–A6) of
`docs/MULTI_HOST_INTEGRATION_PLAN.md` so the multi-host foundation is clean,
enforced, and proven — not just compiled.

This prompt is **self-contained**: an agent with only this document can do the
whole of Phase A. It is also **idempotent** — a first cut already landed on
branch `claude/charming-newton-Qk18G` (commit `92af0b3`). **Reconcile with what
exists; do not duplicate.** Where the branch already has a correct
implementation, your job is to (a) finish the unbuilt parts (the A4 server
endpoint), and (b) run the verifications the original sandbox could not
(Blender-headless undo/UI, `dotnet build`/`test`). Where the branch differs from
this spec, this spec + the plan win — fix the code, and note the divergence.

Repo: `beckykyomugisha/stingtools`. Work on `claude/charming-newton-Qk18G`
(or branch from it). Re-enumerate the live tree; trust git, not line numbers here.

---

## 0. Why Phase A exists (context you must hold)

Planscape is a **hub**; Revit / Bonsai / ArchiCAD-IFC / Tekla-IFC are **spokes**
(`docs/MULTI_HOST_INTEGRATION_PLAN.md` §1.0). Phase A builds the foundation every
later phase stands on:

- **One shared core** (`stingtools-core/python`, pure, no `bpy`) owns all logic.
- **Thin host adapters** own only the contract glue.
- **Bonsai is a hard dependency** of the interactive add-on; headless runs via
  StingBridge + pip-installed `ifcopenshell`, never by bundling into the extension.
- The **core/adapter boundary is enforced in CI** so it can't erode.

If you write tag/enum/IDS/alignment/inference logic anywhere other than core,
you have failed Phase A regardless of whether it compiles.

---

## 1. Hard rules (violating any is a failure)

1. **Build-gate everything** (§2). A part is "done" only when its gate passes:
   `pytest` green, boundary lint green, **Blender-headless smoke green**,
   **`dotnet build` + `dotnet test` green** for the A4 endpoint.
2. **Additive, not destructive.** Do not break the live `host=bonsai` push,
   `IfcController`/`TagSyncController`, the LoGeoRef-50 `AutoAlignService`, or the
   existing N-panel ops.
3. **Core owns logic; adapters own glue.** No tag/enum/IDS/inference/alignment
   logic in `stingtools-bonsai/ops/*`, StingBridge adapters, or future host
   adapters. The A6 lint enforces this and must stay green.
4. **One source of truth** for the substrate — read `shared/ifc/`; never fork it.
5. **No secrets; no force-push; no PR unless asked.** Commit with clear messages;
   push `-u origin <branch>`; retry network failures with backoff.
6. **EF migrations are explicit** — the A4 endpoint adds no entity, but if you
   choose to persist a per-project substrate hash, ship a named migration.
7. **Report honestly.** Mark each verification ✅ only if you actually ran it;
   ❓ with the reason otherwise. Never claim Blender/.NET verification you didn't run.

---

## 2. Environment & build gates

```bash
# Core (pure Python) — MUST be green
cd stingtools-core/python && python -m pytest -q

# Adapter-boundary lint — MUST exit 0
python tools/ci/check_adapter_boundary.py

# Bonsai add-on — headless smoke in REAL Blender with Bonsai installed (A1/A2)
blender --background --python stingtools-bonsai/tests/verify_blender.py
# (extend this script per §A1-V / §A2-V below)

# Server (A4 endpoint) — MUST build + test green
dotnet build  Planscape.Server/Planscape.Server.sln
dotnet test   Planscape.Server
```

If any tool is genuinely unavailable, **stop and say so** — do not silently skip
a gate and call the item done.

---

## 3. Current state on the branch (verify, then complete)

| Item | Landed on branch | Your job |
|---|---|---|
| A1 | Panel "Bonsai required" banner + early return; standalone prose removed from manifest + `BONSAI_RELATIONSHIP.md` | **Verify in real Blender** (banner shows without Bonsai; ops hidden) |
| A2 | `BonsaiBridge._tool_ifc()` + `_run()`; `add_pset`/`edit_attribute` route via `tool.Ifc.run` | **Verify undo + UI refresh in real Blender+Bonsai** |
| A3 | `stingtools_core/hosts/` (`adapter.py`, `ifc_file.py`, `inference.py`); Bonsai op delegates; 50 core tests | Verify; extend `IfcFileHostAdapter` only if needed by tests |
| A4 | Host half only: `stingtools_core/substrate.py` (+ tests) | **Build the server endpoint + client wiring + drift warning** (the gap) |
| A5 | `ifcopenshell>=0.8.0,<0.9` (StingBridge); Bonsai range in manifest | Verify the pin matches the Bonsai you test against |
| A6 | `tools/ci/check_adapter_boundary.py` + `.github/workflows/multi-host-core.yml` | Verify CI job runs green on a PR |

Read `git show 92af0b3 --stat` first to see exactly what exists.

---

## A1 — Hard dependency on Bonsai

**Spec.** The interactive add-on requires Bonsai (it provides `ifcopenshell` +
the undo-aware IFC layer). No in-Blender standalone mode.

- `stingtools-bonsai/ui/panel_main.py` → when `BonsaiBridge.capabilities.installed`
  is False, the root panel draws an **alert box** ("Bonsai is required", a
  re-check button) and **returns** without offering ops.
- `blender_manifest.toml` + `BONSAI_RELATIONSHIP.md` carry **no standalone-mode
  prose**; they state the hard dependency and point headless users to StingBridge.
- `__init__.py` `bl_info`/docstring must not advertise "with or without Bonsai".

**A1-V (verify in real Blender).** Extend `stingtools-bonsai/tests/verify_blender.py`
to: register the add-on with Bonsai **disabled**, draw `STING_PT_main`, assert the
"Bonsai is required" path is taken (e.g. capabilities.installed is False and the
op sub-panels are not drawn); then enable Bonsai and assert the diagnostics panel
renders. **Acceptance:** banner shows without Bonsai; ops appear with it.

---

## A2 — Undo-aware IFC writes (the correctness fix)

**Spec.** `BonsaiBridge` (in `stingtools-bonsai/core/bonsai.py`) must mutate IFC
through Bonsai's transaction-aware layer when Bonsai is present:

- `_tool_ifc()` resolves `bonsai.tool.Ifc` (trying `bonsai` → `bonsai_bim` →
  `blenderbim`), or None.
- `_run(command, model, **kwargs)` calls `tool.Ifc.run(command, **kwargs)` when
  available (no `model` arg — Bonsai owns the active file), else
  `ifcopenshell.api.run(command, model, **kwargs)`.
- `add_pset` / `edit_attribute` go through `_run`. Bare `ifcopenshell.api.run`
  appears only on the headless fallback path.

**A2-V (verify in real Blender+Bonsai).** In `verify_blender.py`: open a tiny IFC
via Bonsai, run `sting.auto_tag` (or call `bonsai.add_pset` directly) on one
element, then assert:
1. `Pset_StingTags` is present on the element (write succeeded).
2. **`bpy.ops.ed.undo()` removes the STING write** (proves it landed on Bonsai's
   undo stack — the whole point of A2).
3. Bonsai's own pset panel data reflects the change (UI refresh).
**Acceptance:** all three hold. If undo does NOT revert the write, A2 is not done —
investigate whether `tool.Ifc.run` is actually being reached (log `_tool_ifc()`).

---

## A3 — Host Adapter Contract + logic extraction

**Spec.** `stingtools_core/hosts/` defines the single seam:

- `adapter.HostAdapter` (ABC) with exactly: `read_elements`, `global_id`,
  `host_element_id`, `read_tag`, `write_tag`, `apply_remote_change`,
  `georef_descriptor`, `host_name`. Plus value objects `GeorefDescriptor`
  (LoGeoRef tier + CRS/E/N/elev/north/scale/unit) and `ChangeDelta`
  (`kind` ∈ tag|issue|bcf|clash|transform, `global_id`, `payload`,
  `last_modified_utc`, `cursor`).
- `ifc_file.IfcFileHostAdapter` — headless adapter over an `ifcopenshell.file`;
  reused by StingBridge and (Phase E) ArchiCAD/Tekla. `host_name` defaults "ifc",
  settable to "archicad"/"tekla".
- `inference.py` — **all** token inference, pure + defensive:
  `discipline_for_class` / `infer_discipline`, `level_for_storey_name` /
  `infer_level`, `system_for_name` / `infer_system`, `product_for_type_name` /
  `infer_product`, and `SequenceAllocator` (replaces the old module-global counter).
- The Bonsai op (`ops/tagging_ops.py`) **delegates** to `inference.*` and a module
  `SequenceAllocator` instance — it defines none of this logic itself.

**A3-V.** `pytest` covers `discipline_for_class`, `level_for_storey_name`,
`system_for_name`, `product_for_type_name`, `SequenceAllocator` monotonicity +
high-water-mark, the ABC is non-instantiable, `IfcFileHostAdapter` satisfies the
contract, and **no `bpy` is imported** by core. Keep these green; add cases for any
inference branch not yet covered.

---

## A4 — Substrate drift-check (FINISH THIS — host half done, server half missing)

**Goal.** On login, each host compares its `shared/ifc` substrate hash against the
server's; mismatch ⇒ warn (this host reads a different/stale/forked vocabulary).

**Host half (already on branch — verify):** `stingtools_core/substrate.py`
`substrate_manifest_sha256()` = SHA-256 over `shared/ifc/enums/_manifest.json`;
`compare(local, remote)` (None/"" remote ⇒ no-drift). Tests in `tests/test_substrate.py`.

**Server half (BUILD):**
1. **`SubstrateController`** — `GET /api/substrate/manifest` (Authorize; no project
   scope — the substrate is global). Returns
   `{ "sha256": "<hex>", "schemaVersion": 2, "totalEnums": 52 }`.
   - Compute the hash from the server's copy of `shared/ifc/enums/_manifest.json`.
     Resolve its path from config `Substrate:ManifestPath` (default: a copy placed
     under the API content root at build via a csproj `Content` include, or the
     repo `shared/ifc/...` in dev). **Cache** the computed hash in a singleton
     (`ISubstrateManifestProvider`) — it is immutable per deployment.
   - Match existing controller conventions (`[ApiController]`, DI, `ILogger`,
     `ProducesResponseType`). Register the provider in `Program.cs`.
2. **Client wiring** — `stingtools_core/planscape/client.py`:
   `get_substrate_manifest() -> dict` (`GET /api/substrate/manifest`); and a helper
   `check_substrate_drift(client) -> tuple[bool, str]` returning
   `(ok, message)` using `substrate.substrate_manifest_sha256()` +
   `substrate.compare`.
3. **Surface the warning** at login on both live surfaces:
   - **Bonsai** — `ops/coord_ops.py` login op: after a successful login, call
     `check_substrate_drift`; on mismatch `self.report({'WARNING'}, …)` and push a
     row to the panel.
   - **StingBridge** — at worker startup after auth, log a `WARNING` on mismatch.

**A4-V.** `dotnet build` + `dotnet test` green; a unit/integration test hits
`GET /api/substrate/manifest` and asserts a 64-hex `sha256`. Core test asserts the
client helper returns `ok=True` when hashes match and `ok=False` + a message when
they differ (mock the client).

---

## A5 — Version pinning at every seam

**Spec.**
- `StingBridge/requirements.txt` pins `ifcopenshell>=0.8.0,<0.9` (must match the
  ifcopenshell **Bonsai bundles** in the Blender you verify against — check
  Bonsai's bundled version and adjust the pin if it differs, in lockstep with the
  manifest note).
- `stingtools-bonsai/blender_manifest.toml` documents the Bonsai dependency + range
  (schema 1.0.0 can't formally depend on another extension — enforced at runtime by A1).
- `stingtools-core/python/pyproject.toml` optional `ifc` extra pinned `>=0.8.0,<0.9`.
- The Planscape ingest DTO is versioned (confirm/add a version field or header).

**A5-V.** Print the Bonsai-bundled ifcopenshell version in `verify_blender.py`
(`import ifcopenshell; print(ifcopenshell.version)`) and confirm it satisfies the pin.

---

## A6 — Adapter-boundary CI lint

**Spec.** `tools/ci/check_adapter_boundary.py` scans adapter globs
(`stingtools-bonsai/ops/*.py`, plus future host adapters — add them as they land)
and exits 1 if any re-implements core logic. Banned signatures: IFC-class→code
maps (≥3 `"Ifc…":"X"` entries), `IfcRelAssignsToGroup` system inference,
`.isalpha()` product extraction, module-global `^_SEQ_COUNTERS`. Keep it narrow
(no false positives on legit Blender UI enums / local grouping dicts).
`.github/workflows/multi-host-core.yml` runs: core `pytest` + the lint + add-on
`py_compile` on changes under the relevant paths.

**A6-V.** The lint exits 0 on the current tree. Add a **deliberately-violating
fixture test** (a temp file with an IFC-class map) under a self-test in the
script or a `pytest` so a regression that re-introduces logic into an adapter is
caught — then confirm the real adapters still pass. The CI job is green on a PR.

---

## 4. Definition of Done — fill this in your report

| Check | Item | ✅/❓/❌ |
|---|---|---|
| `pytest` core green (host + substrate + existing) | A3/A4 | |
| Boundary lint exits 0; violating-fixture caught | A6 | |
| CI workflow green on a PR | A6 | |
| Blender: "Bonsai required" banner without Bonsai; ops with it | A1 | |
| Blender+Bonsai: STING write present, **undo reverts it**, UI refreshes | A2 | |
| Bonsai-bundled ifcopenshell satisfies the pin | A5 | |
| `dotnet build` + `dotnet test` green | A4 | |
| `GET /api/substrate/manifest` returns 64-hex sha256 | A4 | |
| Login warns on substrate mismatch (Bonsai + StingBridge) | A4 | |
| No standalone-mode prose anywhere | A1 | |

## 5. Guardrails / traps

- `tool.Ifc.run` takes **no `model` arg** (Bonsai owns the active file);
  `ifcopenshell.api.run` does. Don't cross them.
- If undo doesn't revert a STING write, you're still on the bare-API path — fix
  `_run`/`_tool_ifc`, don't paper over it.
- Keep the boundary lint **narrow** — a false positive on a UI enum or a local
  dict will block unrelated PRs and erode trust in the gate.
- The substrate hash must be computed over the **same file** on host and server
  (`shared/ifc/enums/_manifest.json`) or it will always "drift".
- Don't bundle `ifcopenshell` into the Blender extension — Bonsai provides it;
  StingBridge pip-installs it.

## 6. Reporting

End with the DoD matrix (honestly marked), the commit/branch, any EF migration
added, any divergence from this spec you corrected, and anything you had to leave
❓ with the reason. Note clearly which checks ran in **real Blender / real .NET**
vs. were unavailable.
