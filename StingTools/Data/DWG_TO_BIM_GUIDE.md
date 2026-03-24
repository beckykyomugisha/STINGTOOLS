# DWG-to-BIM Structural Conversion Guide

## Overview

The STING Structural DWG-to-BIM conversion tool converts 2D structural CAD drawings (DWG/DXF) into 3D Revit BIM elements. It automatically detects structural members from DWG layers and creates corresponding Revit elements with ISO 19650-compliant tagging.

## Quick Start

1. **Link a DWG** — Link or import your structural DWG plan into the Revit project
2. **Open the Wizard** — Click `StrCADWizard` from the MODEL tab or dispatch `"StrCADWizard"`
3. **Analyze Layers** — Select the DWG import and click **Analyze Layers**
4. **Map Layers** — Review auto-detected layer classifications and adjust **Map To** assignments
5. **Configure** — Set levels, dimensions, construction logic, and numbering
6. **Convert** — Click **Convert to BIM** to create structural elements

## Dialog Sections

### 1. DWG Import & Layer Analysis

| Control | Description |
|---------|-------------|
| **DWG Import** dropdown | Select from linked/imported DWG instances in the document |
| **Analyze Layers** button | Scans DWG geometry, classifies layers, detects structural members |
| **Select All / None** | Toggle all layer checkboxes |
| **Structural Only** | Auto-select only layers classified as structural |
| **Auto-Map Layers** | Auto-assign **Map To** categories based on layer classification |

**Layer Grid Columns:**
| Column | Description |
|--------|-------------|
| ✓ | Include this layer in conversion |
| Layer Name | DWG layer name |
| Entities | Total entity count on this layer |
| Lines | Number of line entities |
| Arcs | Number of arc/circle entities |
| Auto-Detect | Structural classification (Column, Beam, Wall, Slab, Grid, etc.) |
| Conf. | Classification confidence percentage |
| Map To | **Dropdown** — Override the auto-detected category (Column/Beam/Wall/Slab/Foundation/Grid/Annotation/Skip) |

### 2. Element-Layer Mapping

Six dropdown selectors for explicit layer-to-element assignment. Each has a checkbox to enable/disable that element type:

| Mapping | Description |
|---------|-------------|
| **Column Layer** | DWG layer containing column cross-sections (circles, rectangles) |
| **Beam Layer** | DWG layer containing beam centerlines |
| **Wall Layer** | DWG layer containing wall lines (parallel pairs) |
| **Slab Layer** | DWG layer containing slab boundary outlines |
| **Foundation** | DWG layer containing foundation blocks/outlines |
| **Grid Layer** | DWG layer containing structural grid lines |

These dropdowns are populated with all DWG layers after analysis. The best matching layer is auto-selected based on the **Map To** classification.

### 3. Levels & Element Properties

**Level Configuration:**
- **Base Level** — Bottom level for element placement (e.g., Foundation Level, Ground Floor)
- **Top Level** — Upper level constraint for columns
- **Auto-detect sizes from geometry** — Use DWG dimensions to determine element sizes

**Element Dimensions (mm):**

| Element | Property | Default | Description |
|---------|----------|---------|-------------|
| Column | Height | 3000 | Floor-to-floor height (overridden by level constraints) |
| Beam | Depth | 450 | Beam cross-section depth |
| Beam | Width | 250 | Beam cross-section width |
| Wall | Height | 3000 | Wall height |
| Wall | Thick | 200 | Wall thickness |
| Slab | Thick | 200 | Slab thickness |
| Foundation | Depth | 600 | Foundation/pad footing depth |

### 4. Construction Logic & Tagging

**Construction Relationships:**

| Option | Default | Description |
|--------|---------|-------------|
| Beams rest on top of walls | ✓ | Beam bottom at wall top elevation |
| Beams connect to slabs | ✓ | Beam top at slab soffit elevation |
| **Columns stop at slab soffit** | ✓ | Column top = Top Level − Slab Thickness (not Top Level). This is the correct structural behavior — columns bear the slab, they don't penetrate it. |
| **Create as Structural Walls** | ✓ | When checked, walls are created with `IsStructural = true` for structural analysis. When unchecked, creates architectural walls. |

**Construction Sequence:** Foundation → Column → Beam → Slab (elements created in this order)

**STING ISO 19650 Tagging:**

| Option | Description |
|--------|-------------|
| Auto-tag all created elements | Runs `TagPipelineHelper.RunFullPipeline()` on all created elements |
| Auto-assign SEQ numbers per type | Applies numbering based on config |
| Tag prefix | Optional prefix for STING tags |
| Numbering | By Level / By Grid / Sequential |
| Tag family | Default STING tags or project-specific tag families |

**Tag Format Preview:** Shows predicted ISO 19650 tags:
```
Column: S-BLD1-Z01-L01-STR-SUP-COL-0001
Beam:   S-BLD1-Z01-L01-STR-SUP-BM-0001
```

### 5. Element Numbering (Graitec-Style)

Inspired by **Graitec PowerPack Numbering Tool**, this section provides template-based numbering with group and element enumeration.

**Configuration:**

| Setting | Description | Example |
|---------|-------------|---------|
| Category | Revit category to number | Structural Columns |
| Parameter | Target parameter for the number | Mark |
| Text (prefix) | Fixed text prefix | GFC |
| Separator | Character between prefix and number | - |
| Group Enumeration | Optional group letter/number | A, B, C... or 1, 2, 3... |
| Element Enumeration | Per-element sequential number | 01, 02, 03... |
| Start from | First number in sequence | 1 |
| Number of digits | Zero-padding width | 2 (→ 01, 02) |
| Increment by | Step between numbers | 1 |

**Enumeration Styles:**
| Style | Example |
|-------|---------|
| Numeric (1, 2, 3...) | 01, 02, 03 |
| Capital Letters (A, B, C...) | A, B, C |
| Lower Letters (a, b, c...) | a, b, c |
| Capital Romans (I, II, III...) | I, II, III |
| Lower Romans (i, ii, iii...) | i, ii, iii |

**Preview:** Shows a live preview of the numbering sequence:
```
GFC-01   GFC-02   GFC-03   GFC-04   GFC-05   GFC-06   GFC-07   GFC-08
```

**With Group Enumeration:**
```
GFC-A-01   GFC-A-02   GFC-B-01   GFC-B-02   ...
```

**Grouping Algorithms:**
| Algorithm | Description |
|-----------|-------------|
| None | Pure sequential numbering |
| By Level | Group by Revit level |
| By Type | Group by family type |
| By Grid Line | Group by nearest structural grid |
| By Location | Group by spatial proximity |
| By Mark | Group by existing Mark value |

**Options:**
- **Omit already numbered** — Skip elements that already have a value in the target parameter

## Detection Algorithms

### Column Detection
- **Round columns** — Detected from circles/arcs with diameter 150–1500mm
- **Rectangular columns** — Detected from 4-line closed loops with perpendicularity validation (±10°), aspect ratio ≤ 3.0, size range 150–1500mm

### Beam Detection
- Context-aware: must satisfy at least ONE of:
  - Axis-aligned (within 10° of horizontal/vertical)
  - Endpoint within 1m of a detected column
  - Length ≥ 2m (structural regardless of context)
- Minimum length: 500mm

### Wall Detection
- Parallel line pairs with perpendicular distance 150–500mm
- Minimum 40% longitudinal overlap between pair lines
- Direction tolerance: ±5°

### Slab Detection
- True closed-loop detection (not bounding box)
- Minimum 4 lines forming a closed polygon
- Minimum area: 1 m²

### Grid Detection
- Lines on layers containing "grid", "axis", or "raster"
- Collinear segments merged (0.3 ft tolerance)
- Auto-labeled: A, B, C (horizontal) / 1, 2, 3 (vertical)

### Foundation Detection
- DWG blocks with foundation/footing/pile layer names
- Pad foundations auto-created under detected column positions

## Column Soffit Logic

When **"Columns stop at slab soffit"** is checked:

```
Column Top = Top Level Elevation − Slab Thickness

Example:
  Top Level = Ground Floor Level = +3.000m
  Slab Thickness = 200mm
  Column Top = +3.000m − 0.200m = +2.800m (slab soffit)
```

This is implemented via Revit's `FAMILY_TOP_LEVEL_PARAM` (set to Top Level) and `FAMILY_TOP_LEVEL_OFFSET_PARAM` (set to −SlabThickness). This ensures columns extend from Base Level to the underside of the slab, not through it.

## API Reference

### NumberingEngine

```csharp
// Generate preview
var config = new NumberingEngine.NumberingConfig
{
    Category = BuiltInCategory.OST_StructuralColumns,
    ParameterName = "Mark",
    Prefix = "GFC",
    Separator = "-",
    UseElementEnum = true,
    ElementStyle = NumberingEngine.EnumStyle.Numeric,
    StartFrom = 1,
    NumberOfDigits = 2,
    IncrementBy = 1,
    Grouping = NumberingEngine.GroupingAlgorithm.ByLevel,
};
var preview = NumberingEngine.GeneratePreview(config, 8);
// → ["GFC-01", "GFC-02", "GFC-03", ...]

// Apply to document
int count = NumberingEngine.ApplyNumbering(doc, config);
```

### StructuralCADPipeline

```csharp
var pipeline = new StructuralCADPipeline(doc);
var config = new DWGConversionConfig
{
    ColumnsStopAtSoffit = true,
    SlabThicknessMm = 200,
    CreateStructuralWalls = true,
    CreateFoundations = true,
    FoundationDepthMm = 600,
    AutoTag = true,
};
var result = pipeline.RunFullPipelineWithConfig(importInstance, config);
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Layer grid empty after Analyze | Ensure DWG is linked/imported (not just referenced). Check the DWG Import dropdown shows your file. |
| No layers detected as structural | DWG uses non-standard layer names. Use **Auto-Map Layers** or manually set **Map To** for each layer. |
| Columns penetrate slab | Check **"Columns stop at slab soffit"** checkbox. Verify Slab Thickness value matches your design. |
| No foundations created | Ensure a Structural Foundation family is loaded in the project. |
| Walls not structural | Check **"Create as Structural Walls"** checkbox in Construction Logic. |
| Elements at wrong level | Verify **Base Level** and **Top Level** are set correctly. |
| Numbering not applied | Ensure elements have the target parameter (e.g., Mark) and it's not read-only. |

## Files

| File | Lines | Description |
|------|-------|-------------|
| `Model/StructuralCADWizard.cs` | ~1,718 | Single-page WPF dialog + NumberingEngine + DWGConversionConfig |
| `Model/StructuralCADPipeline.cs` | ~2,400 | Detection algorithms + element creation + enhanced pipeline |
| `Model/StructuralModelingCommands.cs` | — | `StrCADWizardCommand` (launches wizard and runs pipeline) |
| `Data/DWG_TO_BIM_GUIDE.md` | — | This documentation file |
