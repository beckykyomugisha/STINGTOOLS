# STING Dynamo Integration — Design

**Status**: stub. Folder + csproj placeholder shipped in Phase 108m. Full implementation is a follow-on phase.

## Goal

Expose STING commands as Dynamo ZeroTouch nodes so BIM power-users can chain them inside visual scripts alongside `Element.Geometry`, `FamilyInstance.ByPoint`, `Select.Model.Elements` etc.

## Planned node catalogue (Phase 108n)

| Node | Signature | What it calls |
|------|-----------|---------------|
| `STING.BuildRoom(width, depth, height, type)` | → `List<Element>` | `ModelCreateRoomCommand` |
| `STING.BuildStructuralBay(spanX, spanY, storey, colType, beamType)` | → `List<Element>` | Bay preset |
| `STING.AutoTag(elements)` | → `bool` | `AutoTag` command |
| `STING.ValidateTags(elements)` | → `ValidationReport` | `ValidateTagsCommand` |
| `STING.BuildBOQ(doc)` | → `BOQDocument` | `BOQCostManager.BuildBOQDocument` |
| `STING.ExportBOQ(doc, xlsxPath)` | → `string` | `BOQProfessionalExportCommand` |
| `STING.ApplySectorPack(id)` | → `void` | `ApplySectorPackCommand` |

## Dependencies

- `DynamoVisualProgramming.ZeroTouchLibrary` (NuGet)
- Revit 2025 API assemblies
- Existing StingTools.dll referenced

## Implementation guidance

1. Create a thin facade — Dynamo nodes do NOT call Revit API directly; they dispatch through the existing `StingCommandHandler.ExternalEvent`.
2. Each node returns an object with a `.Status` property so graph authors can chain `Node → If → Node`.
3. NodeCategory = `"STING.BIM"` so nodes cluster in the Dynamo library under one heading.
4. Distribute as a Dynamo Package — package.json describes deps + entry-point DLL.

## Example Dynamo graph (pseudo)

```
Select.Rooms                           (built-in Dynamo)
   └─▶ STING.AddBOQ                    (new node)
          └─▶ STING.AutoTag            (new node)
                 └─▶ STING.ValidateTags
                        └─▶ STING.ExportBOQ   ("C:/out/project.xlsx")
```

No code yet. This is the placeholder.
