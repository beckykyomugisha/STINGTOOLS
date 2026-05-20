# STING IFC Property Set Templates

This folder hosts the `Pset_Sting*` PropertySetTemplate XML files that
the host plugins write, and the IDS specs validate, against IFC4 / IFC4X3
/ IFC2X3 IfcPropertySet entities.

The format is a STING-specific XML that compiles down to:

- a **buildingSMART `IfcPropertySetTemplate` XML** for tools that consume
  the standard PSD format (Solibri, BIM Vision, ArchiCAD IFC translator)
- **embedded `IfcPropertySetTemplate` entities** inside every IFC file
  STING emits (self-describing payload)
- **shared-parameter fragments** the build tooling injects into
  `MR_PARAMETERS.txt` (Revit) + Blender Pset templates + ArchiCAD
  property-manager imports + Tekla UDA files

The `Pset_StingSpatialCodes` template seeded here is the **anchor for the
spatial-code template flow** (Phase 4). It defines the codes that the
LOC / LVL / ZONE tag tokens reference, and it documents the cross-entity
validation rules that the IDS author will encode as a Property facet
applicability + Property facet requirement.

The full Pset bundle (15 templates) lands as Phase 1 of the
`stingtools-core` Python + dotnet port; this folder is its anchor.

See `_README.md` in `shared/ifc/enums/` for the matching governance,
lock-protocol, and IFC-embed strategy that applies equally to Psets.
