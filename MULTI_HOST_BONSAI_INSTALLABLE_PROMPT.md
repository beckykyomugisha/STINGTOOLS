# Prompt — Make StingTools-for-Bonsai a REAL, installable plugin with working buttons

Hand this to a **build-capable agent with Blender 4.2+/5.x and the Bonsai add-on
installed**. Mission: turn `stingtools-bonsai/` from "code in a repo" into an
**installable Blender extension that visibly adds a `STING` sidebar panel with
working buttons** for tagging, IDS validation, and Planscape coordination — and
prove it loads and functions on a clean Blender + Bonsai install.

The buttons already exist in source. The gap is purely **packaging + vendoring +
install + verification**. Today the add-on only works when run from the repo
checkout; installed as a `.zip` it fails to load its core and shows nothing.

Repo: `beckykyomugisha/stingtools`. Branch off the latest default branch; never
commit to it directly. Re-enumerate the live tree — trust it over this doc.

---

## 0. What exists today (verify first, then fill the gaps)

Run these to confirm before changing anything:

- **UI is real.** `stingtools-bonsai/ui/panel_main.py` defines a `STING` N-panel
  (`STING_PT_main`) with four sub-panels and ~20 operator buttons:
  - **SELECT** — `sting.select_untagged` / `select_stale` / `select_by_discipline` / `select_compliant`
  - **TAGS** — `sting.auto_tag` / `tag_selected` / `set_discipline` / `set_system` / `assign_sequence` / `build_full_tags`
  - **VALIDATE** — `sting.validate_ids` / `validation_dashboard` / `clear_validation`
  - **COORD** — `sting.planscape_login` / `sync_to_planscape` / `raise_issue`
  - Diagnostics — `sting.about` / `reload_substrate` / `bonsai_probe`
- **Operators are registered** (`ops/__init__.py` → `register()` loops `CLASSES`).
- **COORD ops are real, not stubs** (`ops/coord_ops.py` calls the live
  `planscape/client.py`: `login`, `ingest_ifc`, `raise_issue`).
- **Phase A landed**: hard-Bonsai-dependency gate, `tool.Ifc.run` undo-aware
  writes, the `stingtools_core.hosts` contract, substrate drift-check.

**Why nothing shows up when "installed":**

1. **`stingtools_core` is NOT vendored.** `stingtools-bonsai/_vendor/` holds only a
   README. `__init__.py` finds core via a repo-relative `sys.path` hack
   (`_THIS.parent.parent / stingtools-core/python`) that exists only in a repo
   checkout. From an installed extension that path is gone → `import
   stingtools_core` fails → the panel renders "core: NOT LOADED" and every op
   no-ops.
2. **No build/package script** — nothing produces the installable `.zip`.
3. **The substrate (`shared/ifc/`) is not bundled** — enums/psets/IDS won't
   resolve on a clean machine (the loader walks parents for
   `shared/ifc/enums/_README.md`, absent in an install).
4. **Never installed or smoke-tested in real Blender.**

---

## 1. Scope & non-goals (read before you start)

- **In scope:** make the existing plugin install and work — vendoring, substrate
  bundling, a build script producing the `.zip`, install + in-Blender
  verification, fixing any op that doesn't actually function, and install docs.
- **There is NO "Revit" button in Blender, and you must not add one.** Revit is a
  separate C# plugin. In Blender, Revit/ArchiCAD/Tekla interop happens **through
  Planscape** (the hub): the COORD panel pushes this model's IFC tags to
  Planscape, raises/sees issues, and (later, Phase B) pulls other hosts' changes
  back. Keep the Blender surface to **tagging + validation + Planscape sync**.
- **Do not re-implement core logic in the add-on** — the Phase A6 boundary lint
  (`tools/ci/check_adapter_boundary.py`) must stay green. Vendoring copies core in;
  it does not move logic into adapter files.
- **Bonsai is a hard dependency** (Phase A1). Keep the "Bonsai required" gate.

---

## 2. Hard rules

1. **Prove it in real Blender.** "Done" means: install the built `.zip` on a clean
   Blender+Bonsai, the `STING` tab appears, and the acceptance click-path (§5)
   works. Code that merely registers is not done.
2. **Vendor, don't bundle dependencies you don't own.** Copy `stingtools_core` +
   the `shared/ifc/` substrate into the extension package. Do **not** bundle
   `ifcopenshell` — Bonsai provides it (Phase A5 hard-dependency decision).
3. **One source of truth.** The vendored core + substrate must be *copied at build
   time* from the repo's `stingtools-core/python` and `shared/ifc/`, never
   hand-edited in `_vendor/`. A drift check should fail the build if they differ.
4. **Keep dev-mode working too.** The repo-path fallback in `__init__.py` must
   still work for developers running from a checkout; vendored mode is the
   installed path. Resolve `stingtools_core` and the substrate from the vendored
   copy first, repo checkout second.
5. **No secrets; no force-push; no PR unless asked.** Clear commits; push
   `-u origin <branch>`; retry network with backoff.

---

## 3. Build gates

```bash
# Core still green (no regression)
cd stingtools-core/python && python -m pytest -q
python tools/ci/check_adapter_boundary.py            # boundary stays clean

# Build the extension (Blender 4.2+ extension toolchain)
blender --command extension build --source-dir stingtools-bonsai --output-dir dist/
#   → produces dist/stingtools_bonsai-<ver>.zip   (validate the manifest too:)
blender --command extension validate stingtools-bonsai

# Install + headless smoke (REAL Blender with Bonsai installed)
blender --background --python stingtools-bonsai/tests/verify_blender.py
```

If Blender isn't available to you, **stop and say so** — this task is defined by
in-Blender verification; do not mark it done from syntax checks alone.

---

## 4. Implementation

### 4.1 Vendor the core + substrate into the package
- Add a **build step** that copies `stingtools-core/python/stingtools_core/` →
  `stingtools-bonsai/_vendor/stingtools_core/` and `shared/ifc/` →
  `stingtools-bonsai/_vendor/shared/ifc/` (or a `data/` subdir). Prefer a small
  script (`stingtools-bonsai/build.py` or a `tools/` script) invoked before
  `extension build`, plus a `.gitignore` for the vendored copy if you don't want
  it committed (decide: commit-vendored for reproducibility, or build-time copy +
  CI artifact — recommend build-time copy + a committed manifest of expected files).
- Update `__init__.py` path resolution to prefer `_vendor/stingtools_core` and set
  `STINGTOOLS_SHARED_IFC` to the vendored `shared/ifc` when present, before the
  repo-walk fallback. Confirm `stingtools_core.paths.find_shared_ifc()` then
  resolves from the vendored copy.
- Verify `blender_manifest.toml` `paths_exclude_pattern` does **not** exclude
  `_vendor/`, and that `tests/` exclusion still holds.

### 4.2 Build script → installable `.zip`
- Wire `blender --command extension build` (the 4.2+ extensions toolchain) and
  capture the output `.zip` to `dist/`. Document the exact command. If your
  Blender predates the `extension` command, fall back to a deterministic `zip` of
  the add-on dir matching the manifest layout.
- Ensure the manifest `id`, `version`, `blender_version_min`, and
  `[permissions]` (files + network) are correct for an extensions.blender.org-style
  install.

### 4.3 Make every button actually do something
Walk each operator and confirm it functions against a real IFC opened in Bonsai;
fix any that don't:
- **TAGS** — `auto_tag` writes `Pset_StingTags` via the Phase-A2 `tool.Ifc.run`
  path (undo works); `assign_sequence` / `build_full_tags` populate correctly.
- **VALIDATE** — `validate_ids` runs the real IDS (`shared/ifc/*.ids` via
  `ifctester`/`stingtools_core.ids`), caches results, and `validation_dashboard`
  renders them; the VALIDATE panel's live summary strip updates.
- **SELECT** — selectors actually select the right elements in the viewport.
- **COORD** — `planscape_login` stores the token in prefs; `sync_to_planscape`
  pushes via `ingest.collect_elements` + `client.ingest_ifc` (host="bonsai");
  `raise_issue` posts an issue. Surface success/failure in the panel.
- Add **add-on Preferences** (server URL / email / project id) if not already
  present, since COORD reads them.

### 4.4 Smoke test (`verify_blender.py`)
Extend it to: register the extension, assert the `STING_PT_*` panels exist, open a
tiny fixture IFC via Bonsai, then exercise one op per panel —
`auto_tag` (assert `Pset_StingTags` written + `ed.undo()` reverts it),
`validate_ids` (assert a result dict), a selector (assert a selection), and a
mocked/recorded COORD call. Print PASS/FAIL per check.

### 4.5 Docs
- `stingtools-bonsai/README.md`: install instructions (build → `Edit >
  Preferences > Add-ons / Get Extensions > Install from Disk` → enable), the
  Bonsai prerequisite, a screenshot or GIF of the `STING` tab, and a 60-second
  "tag → validate → push to Planscape" quickstart.

---

## 5. Definition of Done — acceptance click-path (fill in your report)

On a **clean Blender + Bonsai**, install the built `.zip` and verify:

| # | Check | ✅/❌ |
|---|---|---|
| 1 | Extension installs from the `.zip` with no error; appears in Add-ons | |
| 2 | A `STING` tab is visible in the 3D-view sidebar (N-panel) | |
| 3 | With Bonsai **disabled**, the panel shows "Bonsai is required" and stops | |
| 4 | With Bonsai enabled + an IFC open, panel shows "core v… / Bonsai v…" | |
| 5 | **Auto-Tag** writes `Pset_StingTags` on elements; **Ctrl-Z reverts it** | |
| 6 | **Run IDS Validation** produces a result; the VALIDATE summary updates | |
| 7 | A **SELECT** button selects the expected elements | |
| 8 | **Planscape Login** + **Push to Planscape** succeed against a running server | |
| 9 | `pytest` core green + boundary lint green (no regression) | |
| 10 | `verify_blender.py` headless smoke passes | |

Mark any check ❓ only with the reason (e.g. "no running Planscape server" for #8 —
in which case mock the client and verify the call shape).

## 6. Guardrails

- Don't bundle `ifcopenshell` (Bonsai owns it). Don't add a Revit button.
- Vendored core/substrate must be a **copy of** the repo source — add a build-time
  drift check so they can't silently diverge.
- Keep the boundary lint green — vendoring ≠ moving logic into adapters.
- Preserve the live `host=bonsai` Planscape push and the Phase-A undo-aware writes.
- The substrate loader resolves via `STINGTOOLS_SHARED_IFC` or a parent-walk; make
  sure the installed extension sets the env to its vendored copy so it doesn't try
  to walk a non-existent repo tree.

## 7. Reporting

End with the §5 matrix (honestly marked), the build command that produced the
`.zip`, what you fixed in which operators, anything left ❓ with the reason, and
whether the in-Blender checks ran on **real Blender** or were unavailable.
