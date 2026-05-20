# stingtools-bonsai/planscape/

Empty in the Day-1 scaffold. MVP Week 5 work lands here:

| File | Purpose |
|---|---|
| `client.py` | Blender-side wrapper around `stingtools_core.planscape.PlanscapeClient`. Adds modal-operator-friendly progress callbacks + tenant context caching |
| `signalr.py` | `IfcElementChangedHub` subscription — listens for cross-host element changes from other hosts (Revit/ArchiCAD/Tekla) and surfaces them via a Bonsai notification |

The heavy lifting (JWT/refresh, REST methods, audit log) is in
`stingtools-core/python/stingtools_core/planscape/`. This folder
holds the **Blender-specific** integration on top of that core.
