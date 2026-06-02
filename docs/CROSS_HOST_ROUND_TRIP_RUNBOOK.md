# Cross-host round-trip — lab runbook (Prompt 11)

**Status:** pre-flight done in sandbox; **live run pending** (needs real
Revit + ArchiCAD + a running Planscape server + Bonsai — out of reach in
any agent sandbox).

This is the one verification `CONTRACT_ALIGNMENT.md` calls *"the only
unverified link left in cross-host correctness"*: two host-side
derivations of the cross-host key are **equal by construction** but never
confirmed against a **real IFC export**.

| # | Equality the live run confirms | Code site |
|---|---|---|
| A | Revit `IFC_GLOBAL_ID_TXT` snapshot **==** GlobalId in Revit's *exported* IFC | `StingTools/Commands/Interop/StabilizeIfcGuidsCommand.cs` |
| B | ArchiCAD `ifcopenshell.guid.compress(GUID)` **==** GlobalId in ArchiCAD's *exported* IFC | `StingBridge/sync/engine.py:_ifc_global_id_from_acguid` |
| C | A GlobalId shared by two hosts resolves to **both** via `/ifc/mappings` | `Planscape.Server/.../IfcController.GetMappings` |

**This is a test/verification task, not a code change.** If it passes, the
deliverable is the evidence (the JSON + the filled tables below). If it
fails, **file the mismatch as a new drift** with the element + both GUID
values — do **not** patch blindly. The test harness captures mismatches
for exactly this reason.

The automated harness for the run is
[`tools/tests/cross_host_round_trip.py`](../tools/tests/cross_host_round_trip.py).

---

## 0. Read this first — what the cross-host join actually unifies

The prompt's literal step 4 ("the row set includes **all** hosts that
contain that element — revit + archicad + blender") **cannot hold for an
independently-modeled twin.** Revit's GlobalId for a wall and ArchiCAD's
GlobalId for "the same" wall are derived from *different native GUIDs*, so
they are **different** 22-char strings.

`GET /ifc/mappings?ifcGuid=X` joins rows that **share the one GlobalId
`X`** — i.e. **one IFC lineage**. A GlobalId is unified across hosts only
when those hosts see the **same IFC file**:

```
Revit  --export-->  model_R.ifc  (GlobalId G_R per element)
                       |
                       +--> Bonsai opens model_R.ifc  -> sees G_R
   => /ifc/mappings?ifcGuid=G_R  resolves {revit, blender}

ArchiCAD --export--> model_A.ifc (GlobalId G_A per element)
                       |
                       +--> Bonsai opens model_A.ifc  -> sees G_A
   => /ifc/mappings?ifcGuid=G_A  resolves {archicad, blender}
```

So the **correct** acceptance is: **per-export derivation equality (A, B)
+ per-lineage join (C)** — `{revit,blender}` on `G_R`, `{archicad,blender}`
on `G_A`. A single GlobalId carrying *all three* hosts only happens if one
host's export is the shared file all three consume. The harness encodes
the per-lineage form.

> If your real workflow **is** "one authoritative IFC consumed by every
> host" (federated single-source), then a single GlobalId legitimately
> carries all hosts and `expect = {revit, archicad, blender}` — adjust the
> lineage `expect` set in the harness accordingly. State which workflow you
> ran in § Results.

---

## 1. Pre-flight — verified in sandbox (no hosts needed)

Done by the agent before handing this to the lab. These are the
code-level facts the live run rests on; confirming them up front means a
live failure points at the *host/export*, not the wiring.

| Check | Result | Evidence |
|---|---|---|
| Revit snapshot source | ✅ | `StabilizeIfcGuidsCommand` reads Revit's `IfcGUID`/`IFC GUID`/`IFC_GUID` instance param and writes it verbatim into `IFC_GLOBAL_ID_TXT`. It returns `null` (skips) when no IFC GUID param exists — it never substitutes a Revit `UniqueId`. |
| ArchiCAD derivation uses canonical compress | ✅ | `_ifc_global_id_from_acguid` normalises `strip("{}").replace("-","").lower()` then `ifcopenshell.guid.compress`. |
| `compress` machinery is bit-correct | ✅ | In-sandbox: `guid.expand(guid.compress(g)) == g` for `aaaa…`/`eeee…` and a mixed-case GUID; output is 22 chars. The only unverified part is whether *ArchiCAD's export* uses the same compress on the same GUID — that is Test B's live job. |
| `/ifc/mappings` join | ✅ | `IfcController.GetMappings` filters `ExternalElementMapping` by `ProjectId` + `IfcGlobalId` and returns **all hosts** for that GUID. `Host` is normalised lowercase via `MappingHosts` (`revit\|blender\|archicad\|tekla\|headless\|iot`). |
| Wire contract | ✅ | `/ifc/data` envelope = `{host, hostDocumentGuid, pluginVersion, userName, elements[]}`; element key field is `ifcGlobalId`, host id `hostElementId` (per `IfcElementDto` / `test_ifc_ingest.py`). |
| Harness skip-safety | ✅ | `cross_host_round_trip.py` with no inputs → 3×SKIP, exit 0; emits well-formed evidence JSON. |

---

## 2. Risks the live run must catch (challenge before you run)

These are the assumptions that the construction proof **cannot** see and
that the live export is the only way to test. Watch for each.

### R1 — Revit may re-map GUIDs on export (highest risk; breaks A)

Test A's equality holds **only if Revit's IFC exporter writes each
element's `IfcGUID` instance param as the `GlobalId`**, rather than
re-deriving. Two things to confirm in Revit's IFC export setup *before*
the export you ingest:

- The **same instance param** `StabilizeIfcGuidsCommand` reads (`IfcGUID`)
  is the one the exporter honours. In-repo exporters
  (`Temp/OperationsCommands.cs`, `Commands/Mep/ExportPfvIfcCommand.cs`,
  `Docs/ExportCenterEngine.cs`, `ExLink/AutomationEngine.cs`) build plain
  `IFCExportOptions` and do **not** pin a "GUID source" option — so the
  result depends on Revit's default + the export setup's *"Store the IFC
  GUID in the project after export"* / GUID-from-parameter settings.
- If the exporter **re-derives** GUIDs (e.g. via
  `ExporterIFCUtils.CreateGUID`, or a mapping/coordination setup that
  remaps), the snapshot ≠ export and **Prompt 7's assumption is wrong** —
  this test is exactly how you'd discover that. Capture the element +
  both values and file it; do not "fix" by changing the snapshot source
  without understanding which surface is authoritative.

### R2 — Ordering chicken-and-egg (no GUIDs to snapshot; A silently empty)

`StabilizeIfcGuidsCommand.ReadRevitIfcGuid` returns `null` for any element
that has **never been exported** ("run IFC export once to assign GUIDs").
So the run order **must** be:

1. **Export IFC once** (assigns/persists `IfcGUID` params).
2. **Run Stabilize IFC GUIDs** (snapshots them into `IFC_GLOBAL_ID_TXT`).
3. **Export IFC again** — *this* is the export you ingest into Bonsai and
   compare in Test A.

If you stabilise on a never-exported model, `IFC_GLOBAL_ID_TXT` stays
empty, the cross-host key is absent, and Test A will report
`label_not_found`/0-matched rather than a true equality.

### R3 — ArchiCAD GUID derivation may differ by element type (breaks B partially)

`_ifc_global_id_from_acguid`'s own docstring flags this: it *presumes*
ArchiCAD derives the export GlobalId from the same element GUID its JSON
API exposes, "unverified against a live round-trip." Some categories
(library parts, hierarchical/morph elements, openings) may derive GUIDs
differently. **Test B buckets matches/mismatches by IFC category** so a
"works for walls, fails for doors" pattern is visible, not averaged away.

### R4 — `IfcGuidEncoder` is NOT on this path (off-path note, do not conflate)

`StingTools/IfcResults/IfcGuidEncoder.cs` is used **only** by
`DIALuxExportCommand` (Phase-181 DIALux round-trip), **never** by the
cross-host key path. Two facts worth recording so a future change doesn't
quietly route a cross-host key through it:

- Its docstring claims parity with "IfcOpenShell, xbim, and Revit's
  `ExporterIFCUtils.CreateGUID`". It is **not** parity: it packs the 16
  bytes as **3 / 6 / 7 → 4 / 8 / 10 chars**, whereas canonical IFC
  compression packs **1 / 3 / 3 / 3 / 3 / 3 → 2 / 4 / 4 / 4 / 4 / 4**. For
  the same bytes the first output character already diverges
  (`byte0 >> 2` vs `byte0 >> 6`). It is safe for DIALux only because STING
  matches its **own** encoder output on the return trip.
- It must **never** be adopted as a cross-host key derivation. If a future
  Revit element lacks an `IfcGUID` and someone reaches for this encoder as
  a fallback, the key won't match Bonsai/ArchiCAD/server. File a drift if
  you see that creep in.

This is a latent-bug note, not a Prompt-11 blocker.

---

## 3. Lab procedure

### Prerequisites
- Revit 2025/2026/2027 with StingTools loaded; `IFC_GLOBAL_ID_TXT` bound
  (TEMP → Load Params).
- ArchiCAD + StingBridge configured against the same Planscape project.
- A running Planscape server (`Planscape.Server/docker → docker compose up -d`).
- Bonsai (Blender 4.2+) with the `stingtools-bonsai` add-on + its
  `sync_planscape` op pointed at the project.
- Python with `ifcopenshell` + `requests` on the machine running the harness.

### Step 1 — Revit lineage
1. Model a handful of elements (e.g. 1 wall, 1 duct, 1 door — span >1 category).
2. **Export IFC** (assigns IfcGUIDs) → discard this file.
3. Run **Stabilize IFC GUIDs** (the StingTools command).
4. **Export IFC again** → `model_R.ifc` (the file you ingest + compare).
5. Emit the snapshot the plugin holds as a CSV `revit_snapshot.csv`:
   ```
   label,ifcGlobalId
   <element Name or FullTag>,<IFC_GLOBAL_ID_TXT value>
   ```
   (`label` must match `--match-by`: the IFC element `Name`, or its
   `Pset_StingTags.FullTag` if you use `--match-by tag`.)
6. Sync Revit → server (TagSync / BCC) so a `revit` mapping row exists.
7. In Bonsai, open `model_R.ifc` and run **sync to Planscape**
   (host=`blender`).

### Step 2 — ArchiCAD lineage
1. Model the equivalent elements in ArchiCAD.
2. Run the StingBridge sync (`host="archicad"`) — it posts `/ifc/data`
   keyed on `compress(GUID)`.
3. **Export IFC** from ArchiCAD → `model_A.ifc`.
4. Emit `archicad_guids.csv` of the bridge's element GUIDs:
   ```
   label,acGuid
   <element Name or FullTag>,<ArchiCAD element GUID as the bridge sees it>
   ```
5. In Bonsai, open `model_A.ifc` and run **sync to Planscape** (host=`blender`).

### Step 3 — Run the harness
```bash
python tools/tests/cross_host_round_trip.py \
  --revit-ifc      model_R.ifc      --revit-snapshot  revit_snapshot.csv \
  --archicad-ifc   model_A.ifc      --archicad-guids  archicad_guids.csv \
  --match-by       name \
  --server  http://localhost:5000 --project <PROJECT_GUID> \
  --email   admin@planscape.demo  --password admin123 \
  --out     cross_host_evidence.json
```
- Run with only `--revit-ifc/--revit-snapshot` to do **just Test A**, etc.
  Each test is independent and skips when its inputs are absent.
- Exit code: `0` = all runnable tests passed (or skipped); `1` = a real
  mismatch (details in the evidence JSON + stdout).

> **Manual cross-check (Step 5 of the prompt), if you skip Test A's CSV:**
> open `model_R.ifc` in Bonsai, pick an element, read its `GlobalId`, and
> eyeball it against that element's `IFC_GLOBAL_ID_TXT` in Revit. Equal =
> A holds for that element. The CSV path just does this for every element
> and records mismatches.

---

## 4. Results (fill in on the live run)

**Run by / date / environment:** _______________________________________

**Workflow:** ☐ independent twins (per-lineage join) ☐ single authoritative IFC (tri-host join)

### Test A — Revit snapshot == exported GlobalId
| metric | value |
|---|---|
| elements in snapshot | |
| matched | |
| **mismatched** (drift!) | |
| label not found in IFC | |

Mismatches (element, snapshot GUID, export GUID) — **file each as a drift**:

```
(paste from cross_host_evidence.json → tests["A:…"].mismatches)
```

### Test B — ArchiCAD compress(GUID) == exported GlobalId
| metric | value |
|---|---|
| elements in guidset | |
| matched | |
| **mismatched** (drift!) | |
| by-category match/mismatch | |

Mismatches (category, acGuid, compressed, export):

```
(paste from tests["B:…"].mismatches — watch for a category-specific pattern → R3)
```

### Test C — server cross-host join
| lineage | probe GlobalId | expected hosts | hosts found | status |
|---|---|---|---|---|
| revit-lineage | | revit, blender | | |
| archicad-lineage | | archicad, blender | | |

### Verdict
- A holds: ☐ yes ☐ no → if no, **R1/R2** — which? ____________________
- B holds: ☐ yes ☐ no → if no, **R3** — which categories? ____________
- C holds: ☐ yes ☐ no
- Drifts filed: ____________________________________________________

---

## 5. On flip — update the verification trail

When the live run passes, record it in `docs/VERIFIED.md` (same table
style as Phase 186) and flip the line in `CONTRACT_ALIGNMENT.md` that
currently reads *"One consolidated live round-trip validates the whole
story … the only unverified link left in cross-host correctness."*

When it fails, the mismatch rows above **are** the deliverable: each is a
new drift entry (element id + both GUID values + which risk R1–R3 it
matches). Do not change the snapshot source or the compress normalisation
to "make it pass" without first establishing which surface is
authoritative.
