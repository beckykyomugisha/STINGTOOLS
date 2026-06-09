# StingTools ↔ Bonsai relationship

> **Bonsai** (formerly **BlenderBIM**) is the OpenBIM add-on for
> Blender. **StingTools** sits on top of it as the ISO 19650 +
> Planscape coordination layer. Two add-ons. One Blender. Same IFC.

## Division of responsibility

```
┌─────────────────────────────────────────────────────────────────────┐
│  Blender (host)                                                     │
│                                                                     │
│  ┌─────────────────────────────────┐  ┌───────────────────────────┐ │
│  │  Bonsai (BlenderBIM)            │  │  StingTools for Bonsai    │ │
│  │                                 │  │                           │ │
│  │  - IFC load / save              │  │  - 8-segment STING tag    │ │
│  │  - Geometry creation            │  │    grammar                │ │
│  │  - Property set UI              │  │  - Pset_StingTags +       │ │
│  │  - Classifications              │  │    Pset_StingSpatialCodes │ │
│  │  - Materials                    │  │  - 52 enum registry +     │ │
│  │  - BCF import / export          │  │    project overlays       │ │
│  │  - Drawings + sections          │  │  - IDS validation         │ │
│  │  - Quantities + cost            │  │  - Cross-host spatial     │ │
│  │  - QTOs                         │  │    equality check         │ │
│  │  - ifcopenshell.api wrapper     │  │  - Planscape federation   │ │
│  │                                 │  │    (REST + SignalR)       │ │
│  │                                 │  │  - SHA-256 audit log      │ │
│  │                                 │  │  - Healthcare validators  │ │
│  │                                 │  │    (Phase 2)              │ │
│  └─────────────────────────────────┘  └───────────────────────────┘ │
│                  ↑                              ↑                   │
│                  └──────────── ifcopenshell ────┘                   │
│                                     ↓                               │
│                          single IFC file in memory                  │
└─────────────────────────────────────────────────────────────────────┘
```

## When STING calls Bonsai

Every STING-side IFC mutation routes through `BonsaiBridge` in
`core/bonsai.py`:

```python
from stingtools_bonsai.core import bonsai

# Read
model = bonsai.active_ifc()                # → ifcopenshell.file or None
wall = model.by_type("IfcWall")[0]

# Write — delegates to ifcopenshell.api so Bonsai's undo + UI track it
bonsai.add_pset(wall, "Pset_StingTags", {
    "Discipline": "A",
    "Location":   "BLD1",
    "Level":      "L02",
    # ...
})
```

This means:
- Bonsai's undo stack tracks STING writes (Ctrl-Z works).
- Bonsai's UI refreshes (the property set appears in Bonsai's own
  panel too).
- The single IFC file in memory stays consistent — no two libraries
  fighting over it.

## When STING is loaded WITHOUT Bonsai (hard dependency)

**Bonsai is a hard dependency. There is no in-Blender standalone mode.**
Stock Blender does not ship `ifcopenshell`, and Bonsai is what provides both
the IFC library and the undo-aware mutation layer STING delegates to.

When `BonsaiBridge.installed` returns `False`:

- The N-panel renders a "**Bonsai is required**" banner with a re-check button
  and **stops** — STING ops are not offered, because they would no-op without
  ifcopenshell.
- No IFC writes are attempted.

**Headless / batch tagging runs *outside* Blender**, not in a Blender standalone
mode: the StingBridge worker (`python -m StingBridge.bridge …`) and
`stingtools-core` run with a **pip-installed `ifcopenshell`**, so servers / cron
/ CI never need Blender or Bonsai. That is the supported automation path —
`ifcopenshell` is never bundled into the Blender extension.

## Version compatibility

| Bonsai | STING | Status |
|---|---|---|
| 0.7.x (BlenderBIM era) | 0.1.0 | works — detection covers `blenderbim` module name |
| 0.8.x (Bonsai rebrand) | 0.1.0 | works — primary target |
| Future ≥ 0.9.x | 0.1.0 | will work — `_probe()` is defensive |

`BonsaiBridge._probe()` tries three module names in order: `bonsai`,
`bonsai_bim`, `blenderbim`. Each capability probe (pset API,
attribute API, IfcStore access) is wrapped in try/except so a renamed
sub-module doesn't break detection of the parent.

## What STING never does

- ❌ Open or save IFC files when Bonsai is loaded. Bonsai owns that.
- ❌ Mutate IFC entities outside Bonsai's `tool.Ifc.run()` (which wraps
  `ifcopenshell.api` with undo + UI refresh) when Bonsai is present.
- ❌ Register UI panels in the **BIM** sidebar tab (Bonsai's tab).
  STING uses its own **STING** tab.
- ❌ Override Bonsai's handlers (file load, depsgraph). STING
  registers its own handlers alongside Bonsai's.
- ❌ Bundle a copy of ifcopenshell or Bonsai. In Blender, STING relies on
  the ifcopenshell that Bonsai ships. Headless automation gets ifcopenshell
  from `pip` via StingBridge — never bundled into the extension.

## What Bonsai never does (yet)

The whole point of STING:

- ❌ Enforce ISO 19650 tag grammar with corporate-locked enumerations.
- ❌ Validate against Pset_Sting* contracts via IDS.
- ❌ Stamp + audit-log writes with SHA-256 tamper-evidence chains.
- ❌ Cross-host coordination via Planscape Server (Revit / ArchiCAD /
  Tekla federation).
- ❌ Healthcare-pack validators (HTM, HBN, NCRP).
- ❌ Workflow engine with stage-gated conditional steps.

These live in STING. Long-term it's possible some sting features
upstream into Bonsai itself; in the meantime the two-add-on
arrangement keeps responsibilities clean.
