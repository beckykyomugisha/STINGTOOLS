# Phase 186 verification checklist — Path A (1 week, ~5 working days)

Run through this on your dev machine to flip every ❌ in the Phase 186
verification matrix to ✅. Each item is a copy-paste shell command +
the success criterion. Tick the box, paste any output that fails into
`docs/VERIFIED.md` (template at the bottom of this doc).

If anything fails, **stop and triage before continuing** — the checks
build on each other.

---

## Pre-flight

- [ ] On branch `claude/stingtools-bim-research-8Kkwv` with a clean
      working tree.
      ```bash
      git status && git branch --show-current
      ```

- [ ] Python 3.11+ available.
      ```bash
      python3 --version          # ≥ 3.11
      ```

- [ ] .NET 8 SDK available.
      ```bash
      dotnet --version           # ≥ 8.0
      ```

- [ ] Blender 5.1 (or 4.2 LTS) installed.
      ```bash
      # macOS: /Applications/Blender.app/Contents/MacOS/Blender --version
      # Linux: blender --version
      # Windows: "%ProgramFiles%\Blender Foundation\Blender 5.1\blender.exe" --version
      ```

- [ ] Bonsai (BlenderBIM) installed in that Blender. From inside
      Blender: `Edit → Preferences → Extensions → search "Bonsai"`.

---

## Day 1 — C# compile + EF migration

### 1.1 `dotnet build` Planscape.Server

```bash
cd Planscape.Server
dotnet restore
dotnet build --no-restore
```

- [ ] Build succeeds with 0 errors. Warnings tolerable.

**Most likely failures** (and fixes):
- Missing `using Planscape.Core.Entities;` in `IfcController.cs` → add it.
- `IfcIngestResponse` reference issue → ensure `Planscape.Core.DTOs` is imported.
- `ExternalElementMapping.Project` / `.Tenant` navigation properties expect
  matching collection on the other side — Project + Tenant entities may
  need no change since they don't enforce inverse navigation here.

### 1.2 EF migration generation

```bash
cd Planscape.Server/src/Planscape.API   # or wherever the DbContext default project lives
dotnet ef migrations add IfcIngestSubstrate \
    --project ../Planscape.Infrastructure \
    --startup-project .
```

- [ ] New migration file appears under
      `Planscape.Server/src/Planscape.Infrastructure/Data/Migrations/`.
- [ ] Migration adds `ExternalElementMappings` table.
- [ ] Migration adds 2 filtered unique indexes on `TaggedElements`
      (`RevitElementId > 0`, `UniqueId <> ''`) — review the SQL.

### 1.3 Apply migration to local dev DB

```bash
dotnet ef database update \
    --project ../Planscape.Infrastructure \
    --startup-project .
```

- [ ] Migration applies cleanly.
- [ ] `psql -d planscape_dev -c '\d "ExternalElementMappings"'` shows the
      new table.

---

## Day 2 — Bonsai add-on real install

### 2.1 Symlink into Blender extensions

Identify the Blender extensions user folder for your platform
(documented in `stingtools-bonsai/README.md § Install`).

```bash
# Linux example
mkdir -p ~/.config/blender/5.1/extensions/user_default
ln -s "$(pwd)/stingtools-bonsai" \
      ~/.config/blender/5.1/extensions/user_default/stingtools_bonsai
```

### 2.2 Set the substrate env var

```bash
export STINGTOOLS_SHARED_IFC="$(pwd)/shared/ifc"
# Persist by adding to your shell rc; or pass via Blender's launch wrapper.
```

### 2.3 Enable in Blender + verify

1. Launch Blender 5.1.
2. `Edit → Preferences → Extensions → search "STING" → enable`.
3. In the 3D viewport, press `N` to open the sidebar.
4. The **STING** tab appears alongside Bonsai's **BIM** tab.

- [ ] Click **About STING**. Expected toast: `STING core v0.1.0 · 52 enums · 2 psets · 0 drift`.
- [ ] In System Console (Window → Toggle System Console on Windows, or
      check stdout on macOS/Linux), see:
      ```
      [STING] STING core v0.1.0 · 52 enums · 2 psets · 0 drift
      [STING]   pset: Pset_StingTags (12 props, 9 rules)
      [STING]   pset: Pset_StingSpatialCodes (6 props, 5 rules)
      [STING] Bonsai v0.X.X detected — pset_api=yes, context=yes
      ```
- [ ] Click **Probe Bonsai**. Console prints the detailed capabilities
      block.
- [ ] Open any IFC via Bonsai (`File → IFC → Open`). Re-click
      **Probe Bonsai**. Console shows `active file: IFC4, N IfcElement(s)`.

**Most likely failures**:
- `stingtools_core` not importable → `STINGTOOLS_SHARED_IFC` env var not
  set; or the `sys.path` injection in `__init__.py` is wrong for your
  install layout. Edit the path candidates.
- Bonsai not detected → check which module name Bonsai installs as
  on your version (`bonsai`, `bonsai_bim`, or `blenderbim`); update
  `core/bonsai.py:_probe()` if it's a new variant.

---

## Day 3 — Round-trip fixture generation

### 3.1 Install ifcopenshell

```bash
pip install ifcopenshell ifctester
```

- [ ] `python3 -c "import ifcopenshell; print(ifcopenshell.version)"`
      returns a version string.

### 3.2 Implement `round_trip.py --generate-fixture`

`tools/tests/round_trip.py` already documents the call sequence in
`generate_fixture()`. Replace the `print("TODO: ...")` block with:

```python
import ifcopenshell
from ifcopenshell.api import run

model = run("project.create_file", version="IFC4")
project = run("root.create_entity", model, ifc_class="IfcProject", name="STING test")
run("unit.assign_unit", model)
ctx = run("context.add_context", model, context_type="Model")

site     = run("root.create_entity", model, ifc_class="IfcSite",           name="Test Site")
building = run("root.create_entity", model, ifc_class="IfcBuilding",       name="Test Building")
storey   = run("root.create_entity", model, ifc_class="IfcBuildingStorey", name="L01")
zone     = run("root.create_entity", model, ifc_class="IfcZone",           name="Z01")
wall     = run("root.create_entity", model, ifc_class="IfcWall",           name="Test Wall")

run("aggregate.assign_object", model, relating_object=project,  product=site)
run("aggregate.assign_object", model, relating_object=site,     product=building)
run("aggregate.assign_object", model, relating_object=building, product=storey)
run("spatial.assign_container", model, products=[wall], relating_structure=storey)
run("group.assign_group", model, products=[wall], group=zone)

# Pset_StingSpatialCodes on each spatial entity
for entity, props in (
    (building, {"LocationCode": "BLD1"}),
    (storey,   {"LevelCode":    "L01"}),
    (zone,     {"ZoneCode":     "Z01", "ZoneCategory": "Clinical"}),
):
    pset = run("pset.add_pset", model, product=entity, name="Pset_StingSpatialCodes")
    run("pset.edit_pset", model, pset=pset, properties=props)

# Pset_StingTags on the wall
pset = run("pset.add_pset", model, product=wall, name="Pset_StingTags")
run("pset.edit_pset", model, pset=pset, properties={
    "Discipline": "A", "Location": "BLD1", "Zone": "Z01", "Level": "L01",
    "System": "ARC", "Function": "NLB", "Product": "WL", "Sequence": "0001",
    "FullTag": "A-BLD1-Z01-L01-ARC-NLB-WL-0001",
})

model.write(str(out_path))
return 0
```

Then:

```bash
python3 tools/tests/round_trip.py --generate-fixture \
    --fixture tests/fixtures/spatial_codes_ok.ifc
```

- [ ] File `tests/fixtures/spatial_codes_ok.ifc` exists, ~4–8 KB.
- [ ] Opens cleanly in Bonsai (`File → IFC → Open`).
- [ ] In Bonsai's IFC properties panel, the wall shows
      `Pset_StingTags` with all 9 properties populated.

### 3.3 Run the round-trip itself

```bash
python3 tools/tests/round_trip.py \
    --fixture tests/fixtures/spatial_codes_ok.ifc \
    --verbose
```

- [ ] Output ends with `[ OK ] round-trip: round-trip OK (sha256=... over N elements)`.

---

## Day 4 — IDS validation against the fixture

### 4.1 Run ifctester against both IDS files

```bash
python3 -c "
from ifctester import ids, reporter
import ifcopenshell

ifc = ifcopenshell.open('tests/fixtures/spatial_codes_ok.ifc')

for ids_path in [
    'shared/ifc/ids/sting-tag-grammar.ids',
    'shared/ifc/ids/sting-spatial-codes.ids',
]:
    spec = ids.open(ids_path)
    spec.validate(ifc)
    rep = reporter.Console(spec)
    print(f'=== {ids_path} ===')
    print(rep.report())
"
```

- [ ] `sting-tag-grammar.ids`: every spec reports PASS (the fixture has
      every required field set).
- [ ] `sting-spatial-codes.ids`: every spec reports PASS (LOC/LVL/ZONE
      all match container Pset_StingSpatialCodes values).

### 4.2 Create a negative fixture + verify IDS fails

Generate a second fixture where the wall's `Pset_StingTags.Location` is
deliberately `WAC` but the containing IfcBuilding's `LocationCode` is
`BLD1`:

```bash
python3 tools/tests/round_trip.py --generate-fixture \
    --fixture tests/fixtures/spatial_codes_mismatch.ifc
# Then edit the .ifc with a text editor and replace Location='BLD1' with Location='WAC'
# OR copy + mutate via a small script.
```

- [ ] Re-run ifctester. The spec `01b-LOC-PARTOF-BUILDING` from
      `sting-spatial-codes.ids` passes (the building still has a
      LocationCode), but the STING-side `SpatialChecker` (run separately
      below) reports the equality mismatch.

### 4.3 Run the STING-side spatial closeout

```bash
python3 -c "
import ifcopenshell
import sys
sys.path.insert(0, 'stingtools-core/python')
from stingtools_core.spatial import SpatialChecker

model = ifcopenshell.open('tests/fixtures/spatial_codes_mismatch.ifc')
checker = SpatialChecker(model)
for mismatch in checker.check_all_elements():
    print(f'{mismatch.rule_id}: {mismatch.message}')
"
```

- [ ] Output includes `LOC_MATCHES_BUILDING: element LOC 'WAC' != building.LocationCode 'BLD1'`.

---

## Day 5 — CI + PR + verification doc

### 5.1 Push branch + watch CI

```bash
# Empty commit to trigger CI
git commit --allow-empty -m "trigger CI for substrate validation"
git push
```

- [ ] GitHub Actions runs `ifc-substrate.yml`.
- [ ] All 6 steps pass (checksum, bSDD, XSD, IDS XML, Pset refs, GUID uniqueness).
- [ ] CI badge green.

### 5.2 Write `docs/VERIFIED.md`

Template:

```markdown
# Phase 186 verification log

Run by: <your name>
Date: <YYYY-MM-DD>
Environment: <OS, .NET version, Blender version, Bonsai version>

## Day 1 — C# + EF
- [x] dotnet build: clean, 0 errors, X warnings
- [x] EF migration generated: <filename>
- [x] Migration applied to <DB name>

## Day 2 — Bonsai add-on
- [x] About STING: 52 enums · 2 psets · 0 drift
- [x] Probe Bonsai: vX.Y.Z detected, pset_api=yes, context=yes
- [x] Active IFC: opened <path>, K IfcElements

## Day 3 — Round-trip
- [x] Fixture minted: tests/fixtures/spatial_codes_ok.ifc (X KB)
- [x] Round-trip: sha256=<hash> over N elements

## Day 4 — IDS validation
- [x] sting-tag-grammar.ids vs ok-fixture: all specs PASS
- [x] sting-spatial-codes.ids vs ok-fixture: all specs PASS
- [x] SpatialChecker vs mismatch-fixture: LOC_MATCHES_BUILDING fires

## Day 5 — CI + PR
- [x] CI run: <URL>, all 6 steps green
- [x] PR opened: <URL>

## Anomalies + fixes
<list any issues + how you resolved them>
```

### 5.3 Open PR to main

- [ ] PR title: `Phase 186 — Bonsai integration foundation`
- [ ] PR body references `docs/PHASE_186_BONSAI_INTEGRATION.md` and
      `docs/VERIFIED.md`.
- [ ] Reviewers can run the Day-1 to Day-4 checks independently and
      reproduce.

---

## After Path A

With every ❌ flipped to ✅:

- Substrate is **proven**, not assumed.
- Branch is merge-ready.
- Future MVP work (Path B) builds on a verified foundation.
- External community (Path C, parallelisable with Path B Week 1) sees
  a stable artefact, not a research branch.

The week of verification is the cheapest insurance policy in the
project.
