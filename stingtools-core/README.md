# stingtools-core

Dual-language shared library for the STING IFC substrate.

| Language | Path | Status |
|---|---|---|
| Python | `python/` | ✅ v0.1.0 — enums + psets + tag grammar + spatial check + Planscape client + audit log |
| .NET 8 | `dotnet/` | ⏳ future — same surface, ports as needed |

The Python half is consumed by:
- `stingtools-blender/` — the Blender 4.2+ add-on
- `Planscape.Server/` — IFC-data ingest endpoint (server-side validation)
- Headless / CI tooling under `tools/`
- Future `stingtools-archicad/` plugin
- Future `StingTools.Tekla.Connector/` server-side worker

The .NET half (future) will be consumed by:
- The existing Revit plugin (`StingTools/`)
- A Tekla Open API connector (Windows-only worker)

Both halves read the **same** XML / JSON contracts under `shared/ifc/`,
so cross-host behaviour stays semantically identical regardless of
language.

See `python/README.md` for installation + public API.
