# Converters — STING substrate → host-native formats

These tools project the STING IFC substrate (enums + psets) onto the
formats that host plugins and buildingSMART tooling consume.

| Tool | Reads | Produces |
|---|---|---|
| `sting_to_psd.py` | `shared/ifc/enums/*.xml` + `shared/ifc/psets/*.xml` | `shared/ifc/psd_out/PEnum_*.xml` + `Pset_*.xml` — buildingSMART PSD format, consumed by Solibri / BIM Vision / ArchiCAD IFC translator / BlenderBIM PSD importer |
| `sting_to_revit_params.py` | `shared/ifc/psets/*.xml` | `shared/ifc/revit_out/MR_PARAMETERS_Pset_fragment.txt` — Revit shared-parameter file fragment with deterministic GUIDs |
| (future) `sting_to_blender_pset.py` | enums + psets | Blender add-on data files |
| (future) `sting_to_archicad_props.py` | enums + psets | ArchiCAD property-manager import JSON |
| (future) `sting_to_tekla_uda.py` | enums + psets | Tekla UDA file |

## Usage pattern

All converters are idempotent — re-running produces identical output
unless the source XML changed. Wire them into the CI pipeline to fail
PRs that change the substrate without regenerating the converted
artefacts.

```bash
# convert everything
python3 tools/converters/sting_to_psd.py
python3 tools/converters/sting_to_revit_params.py

# verify under --dry-run before commit
python3 tools/converters/sting_to_psd.py --dry-run
```

## Output folders

| Folder | Purpose |
|---|---|
| `shared/ifc/psd_out/` | PSD XML, regenerated each build, *not* committed (gitignored). |
| `shared/ifc/revit_out/` | Revit shared-parameter file fragments, *committed* (used by `LoadSharedParamsCommand`). |

The PSD output is regenerated; the Revit output is committed because
shared-parameter GUIDs must be stable across Revit sessions, and the
deterministic-GUID algorithm in `sting_to_revit_params.py` produces
the same GUIDs on every run — so the committed file IS the source of
truth at runtime, regenerated whenever the Pset XML changes.

## Limitations

1. **PSD per-property GUIDs** — PSD strictly requires each
   `PropertyDef` to carry its own `ifdguid`. The current Pset XML
   format carries `ifdguid` per Pset, not per Property. The converter
   emits empty `ifdguid=""` on properties until the Pset XML schema is
   extended (see `_schema.xsd` Pset extension on the roadmap).

2. **bSDD IRI references** — the PSD spec for IFC4 references the
   IfdGuid attribute; bSDD-published terms get a separate IRI via
   `IfcExternalReference`. The converter doesn't emit that linkage
   yet; the `shared/ifc/bsdd/publication_plan.json` tracks which IRIs
   to inject when published terms come back from bSDD.

3. **Cross-entity validation rules** — the Pset XML carries
   `<CrossEntityValidationRules>` that don't map to PSD at all (PSD is
   schema, not rules). These rules live separately as IDS specifications
   in `shared/ifc/ids/`. The PSD converter strips them silently.

4. **Bundled output** — PSD tooling sometimes expects all enumerations
   in one file and all Psets in another. The converter writes
   one-file-per-artefact; downstream tooling can concatenate if
   needed.
