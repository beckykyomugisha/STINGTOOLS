// ============================================================================
// TagStyleEngine.cs — Tag Style Control Engine for STING Tools
//
// Controls tag appearance by manipulating the {SIZE}{STYLE}_{COLOR}_BOOL
// parameter matrix. Each tag family has label rows bound to these BOOL params;
// exactly ONE param is true per element type, making that label row visible.
//
// The presentation screenshots show the same building in different discipline
// color schemes (warm/salmon, green/teal, red/structural, yellow/electrical,
// blue/plumbing, black-white/monochrome). This engine achieves that by:
//   1. Setting the correct TAG_{SIZE}{STYLE}_{COLOR}_BOOL per discipline
//   2. Applying view-level OverrideGraphicSettings for element coloring
//   3. Controlling paragraph state visibility (10 tiers)
//   4. Managing view color schemes as named presets
//
// Architecture:
//   TagStyleEngine (static)
//     ├── StylePreset — named style preset (size + style + color)
//     ├── ColorScheme — named view color scheme (discipline → color)
//     ├── ApplyTagStyle() — set BOOL params on elements/types
//     ├── ApplyColorScheme() — apply element graphic overrides per discipline
//     ├── SetParagraphDepth() — set state 1-10 visibility tiers
//     └── Presets: Discipline, Warm, Cool, Red, Yellow, Blue, Mono, Dark
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    #region Data Types

    /// <summary>
    /// A tag style preset defines the text size, style, and color for a discipline or context.
    /// Maps to a specific TAG_{SIZE}{STYLE}_{COLOR}_BOOL parameter.
    /// </summary>
    internal class StylePreset
    {
        public string Name { get; set; }
        public string Size { get; set; }
        public string Style { get; set; }
        public string Color { get; set; }

        /// <summary>The BOOL parameter name, e.g. "TAG_2BOLD_RED_BOOL".</summary>
        public string ParamName => ParamRegistry.TagStyleParamName(Size, Style, Color);

        /// <summary>The tag family type name, e.g. "2BOLD_RED".</summary>
        public string TypeName => $"{Size}{Style}_{Color}";
    }

    /// <summary>
    /// Bounding box color preset — controls the tag annotation box fill, border, and leader colors
    /// separately from text color. Matched to discipline/system/status/zone meaning.
    /// </summary>
    internal class BoxColorPreset
    {
        public string Name { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public string BoxStyle { get; set; } = "SOLID"; // SOLID/DASHED/NONE/ROUND
        public bool BoxVisible { get; set; } = true;
        public Color ToColor() => new Color(R, G, B);
    }

    /// <summary>
    /// A color scheme maps discipline codes to RGB colors and tag style presets.
    /// Used for view-level graphic overrides and tag style switching.
    /// </summary>
    internal class ColorScheme
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, Color> DisciplineColors { get; set; } = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, StylePreset> DisciplineTagStyles { get; set; } = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Default tag style for disciplines not explicitly mapped.</summary>
        public StylePreset DefaultTagStyle { get; set; }
        /// <summary>Element override color (for view surface coloring).</summary>
        public Dictionary<string, Color> ElementColors { get; set; } = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Bounding box color presets per variable value (e.g. "M" → blue box).</summary>
        public Dictionary<string, BoxColorPreset> BoxColors { get; set; } = new Dictionary<string, BoxColorPreset>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Leader color presets per variable value.</summary>
        public Dictionary<string, BoxColorPreset> LeaderColors { get; set; } = new Dictionary<string, BoxColorPreset>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Defines which element variable drives the color/style scheme.
    /// Controls which parameter is read per element to determine its color and style.
    /// </summary>
    internal enum StyleVariable
    {
        /// <summary>Color by ASS_DISCIPLINE_COD_TXT (M/E/P/A/S/FP/LV/G).</summary>
        Discipline,
        /// <summary>Color by ASS_SYSTEM_TYPE_TXT (HVAC/DCW/SAN/etc.).</summary>
        System,
        /// <summary>Color by ASS_STATUS_TXT (NEW/EXISTING/DEMOLISHED/TEMPORARY).</summary>
        Status,
        /// <summary>Color by ASS_ZONE_TXT (Z01/Z02/Z03/Z04).</summary>
        Zone,
        /// <summary>Color by ASS_LVL_COD_TXT (GF/L01/L02/B1/RF).</summary>
        Level,
        /// <summary>Color by ASS_FUNC_TXT (SUP/HTG/DCW/PWR/etc.).</summary>
        Function,
        /// <summary>Color by ASS_LOC_TXT (BLD1/BLD2/BLD3/EXT).</summary>
        Location,
    }

    /// <summary>
    /// A variable-driven color scheme that maps ANY tag variable to colors and styles.
    /// More flexible than fixed discipline-only schemes.
    /// </summary>
    internal class VariableColorScheme
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public StyleVariable Variable { get; set; }
        /// <summary>Maps variable values to element colors.</summary>
        public Dictionary<string, Color> ValueColors { get; set; } = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Maps variable values to tag text styles.</summary>
        public Dictionary<string, StylePreset> ValueStyles { get; set; } = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Maps variable values to bounding box fill colors.</summary>
        public Dictionary<string, BoxColorPreset> ValueBoxColors { get; set; } = new Dictionary<string, BoxColorPreset>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Default color for unmapped values.</summary>
        public Color DefaultColor { get; set; } = new Color(128, 128, 128);
        /// <summary>Default tag style for unmapped values.</summary>
        public StylePreset DefaultStyle { get; set; } = new StylePreset { Name = "Default", Size = "2", Style = "NOM", Color = "BLACK" };
    }

    #endregion

    /// <summary>
    /// Static engine for controlling tag appearance through the BOOL parameter matrix,
    /// view graphic overrides, and paragraph state management.
    /// </summary>
    internal static class TagStyleEngine
    {
        // ════════════════════════════════════════════════════════════════
        // BUILT-IN COLOR SCHEMES (matching the presentation screenshots)
        // ════════════════════════════════════════════════════════════════

        /// <summary>All built-in color schemes.</summary>
        public static readonly Dictionary<string, ColorScheme> BuiltInSchemes = BuildSchemes();

        private static Dictionary<string, ColorScheme> BuildSchemes()
        {
            var d = new Dictionary<string, ColorScheme>(StringComparer.OrdinalIgnoreCase);

            // ── Discipline (default STING scheme) ──
            d["Discipline"] = new ColorScheme
            {
                Name = "Discipline",
                Description = "Standard ISO 19650 discipline colors",
                DisciplineColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "M", new Color(0, 128, 255) },       // Blue — Mechanical
                    { "E", new Color(255, 180, 0) },        // Gold — Electrical
                    { "P", new Color(0, 180, 0) },          // Green — Plumbing
                    { "A", new Color(120, 120, 120) },      // Grey — Architecture
                    { "S", new Color(200, 0, 0) },          // Red — Structural
                    { "FP", new Color(255, 100, 0) },       // Orange — Fire Protection
                    { "LV", new Color(160, 0, 200) },       // Purple — Low Voltage
                    { "G", new Color(128, 80, 0) },         // Brown — General
                },
                DisciplineTagStyles = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase)
                {
                    { "M", new StylePreset { Name = "Mech", Size = "2", Style = "BOLD", Color = "BLUE" } },
                    { "E", new StylePreset { Name = "Elec", Size = "2", Style = "BOLD", Color = "RED" } },
                    { "P", new StylePreset { Name = "Plumb", Size = "2", Style = "BOLD", Color = "GREEN" } },
                    { "A", new StylePreset { Name = "Arch", Size = "2", Style = "NOM", Color = "BLACK" } },
                    { "S", new StylePreset { Name = "Struct", Size = "2", Style = "BOLD", Color = "RED" } },
                    { "FP", new StylePreset { Name = "Fire", Size = "2", Style = "BOLD", Color = "RED" } },
                    { "LV", new StylePreset { Name = "LV", Size = "2", Style = "ITALIC", Color = "BLUE" } },
                    { "G", new StylePreset { Name = "Gen", Size = "2", Style = "NOM", Color = "BLACK" } },
                },
                DefaultTagStyle = new StylePreset { Name = "Default", Size = "2", Style = "NOM", Color = "BLACK" }
            };

            // ── Warm (salmon/terracotta — screenshots 164817, 172339) ──
            d["Warm"] = new ColorScheme
            {
                Name = "Warm",
                Description = "Warm salmon/terracotta tones — architectural presentation",
                ElementColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ALL", new Color(200, 120, 100) },
                },
                DisciplineColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "M", new Color(180, 80, 60) },
                    { "E", new Color(200, 100, 80) },
                    { "P", new Color(160, 100, 80) },
                    { "A", new Color(200, 140, 120) },
                    { "S", new Color(180, 60, 40) },
                },
                DefaultTagStyle = new StylePreset { Name = "Warm", Size = "2.5", Style = "NOM", Color = "RED" }
            };

            // ── Cool (green/teal — screenshots 164859, 164918) ──
            d["Cool"] = new ColorScheme
            {
                Name = "Cool",
                Description = "Cool green/teal tones — MEP presentation",
                ElementColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ALL", new Color(140, 180, 160) },
                },
                DisciplineColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "M", new Color(80, 160, 120) },
                    { "E", new Color(100, 180, 140) },
                    { "P", new Color(60, 140, 120) },
                    { "A", new Color(160, 200, 180) },
                    { "S", new Color(80, 140, 100) },
                },
                DefaultTagStyle = new StylePreset { Name = "Cool", Size = "2.5", Style = "NOM", Color = "GREEN" }
            };

            // ── Red (structural/fire — screenshots 172339, 172424) ──
            d["Red"] = new ColorScheme
            {
                Name = "Red",
                Description = "Red/terracotta — structural emphasis",
                ElementColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ALL", new Color(180, 40, 30) },
                },
                DefaultTagStyle = new StylePreset { Name = "Red", Size = "2.5", Style = "BOLD", Color = "RED" }
            };

            // ── Yellow (electrical — screenshots 172513, 172548) ──
            d["Yellow"] = new ColorScheme
            {
                Name = "Yellow",
                Description = "Yellow/amber — electrical emphasis",
                ElementColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ALL", new Color(230, 200, 0) },
                },
                DefaultTagStyle = new StylePreset { Name = "Yellow", Size = "2.5", Style = "BOLD", Color = "BLACK" }
            };

            // ── Blue (plumbing/MEP — screenshot 165222) ──
            d["Blue"] = new ColorScheme
            {
                Name = "Blue",
                Description = "Blue/slate — plumbing/MEP emphasis",
                ElementColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ALL", new Color(100, 130, 180) },
                },
                DefaultTagStyle = new StylePreset { Name = "Blue", Size = "2.5", Style = "NOM", Color = "BLUE" }
            };

            // ── Monochrome (print-ready — screenshot 172731) ──
            d["Mono"] = new ColorScheme
            {
                Name = "Mono",
                Description = "Black and white — print-ready",
                ElementColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ALL", new Color(60, 60, 60) },
                },
                DefaultTagStyle = new StylePreset { Name = "Mono", Size = "2", Style = "NOM", Color = "BLACK" }
            };

            // ── Dark (inverted — screenshots 172641, 172731 dark) ──
            d["Dark"] = new ColorScheme
            {
                Name = "Dark",
                Description = "Dark background presentation",
                ElementColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ALL", new Color(220, 220, 220) },
                },
                DefaultTagStyle = new StylePreset { Name = "Dark", Size = "2.5", Style = "NOM", Color = "BLACK" }
            };

            return d;
        }

        // ════════════════════════════════════════════════════════════════
        // SEMANTIC COLOR REGISTRY — Colors REPRESENT something
        // Each color has a meaning in each context:
        //   - Discipline → M=BLUE, E=ORANGE, P=GREEN, A=GREY, S=RED
        //   - Status → NEW=GREEN, EXISTING=BLUE, DEMOLISHED=RED, TEMP=ORANGE
        //   - System → HVAC=BLUE, ELEC=ORANGE, PLUMB=GREEN, FIRE=RED
        //   - Zone → Z01=BLUE, Z02=GREEN, Z03=ORANGE, Z04=RED
        //   - Level → GF=GREEN, L01=BLUE, L02=PURPLE, B1=RED, RF=ORANGE
        //   - Location → BLD1=BLUE, BLD2=GREEN, BLD3=ORANGE, EXT=PURPLE
        // ════════════════════════════════════════════════════════════════

        /// <summary>All built-in variable-driven color schemes.</summary>
        public static readonly Dictionary<string, VariableColorScheme> VariableSchemes = BuildVariableSchemes();

        private static Dictionary<string, VariableColorScheme> BuildVariableSchemes()
        {
            var d = new Dictionary<string, VariableColorScheme>(StringComparer.OrdinalIgnoreCase);

            // ── By System Type ──
            d["System"] = new VariableColorScheme
            {
                Name = "System", Description = "Color by MEP system type (CIBSE/Uniclass codes)",
                Variable = StyleVariable.System,
                ValueColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "HVAC", new Color(0, 128, 255) },     // Blue
                    { "DCW", new Color(0, 180, 120) },       // Teal
                    { "DHW", new Color(255, 140, 0) },       // Orange
                    { "HWS", new Color(200, 60, 0) },        // Dark Orange
                    { "SAN", new Color(0, 160, 0) },          // Green
                    { "RWD", new Color(80, 130, 180) },       // Steel Blue
                    { "GAS", new Color(200, 200, 0) },        // Yellow
                    { "FP", new Color(200, 0, 0) },           // Red
                    { "LV", new Color(160, 0, 200) },         // Purple
                    { "FLS", new Color(255, 60, 60) },        // Bright Red
                    { "COM", new Color(100, 100, 200) },      // Periwinkle
                    { "ICT", new Color(0, 200, 200) },        // Cyan
                    { "SEC", new Color(180, 0, 180) },        // Magenta
                    { "ARC", new Color(120, 120, 120) },      // Grey
                    { "STR", new Color(200, 0, 0) },          // Red
                    { "GEN", new Color(80, 80, 80) },         // Dark Grey
                },
                ValueStyles = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase)
                {
                    { "HVAC", new StylePreset { Name = "HVAC", Size = "2.5", Style = "BOLD", Color = "BLUE" } },
                    { "FP",   new StylePreset { Name = "Fire", Size = "2.5", Style = "BOLD", Color = "RED" } },
                    { "LV",   new StylePreset { Name = "LV",   Size = "2",   Style = "ITALIC", Color = "PURPLE" } },
                    { "SAN",  new StylePreset { Name = "San",  Size = "2",   Style = "NOM", Color = "GREEN" } },
                    { "DCW",  new StylePreset { Name = "DCW",  Size = "2",   Style = "NOM", Color = "GREEN" } },
                    { "DHW",  new StylePreset { Name = "DHW",  Size = "2",   Style = "BOLD", Color = "ORANGE" } },
                    { "ARC",  new StylePreset { Name = "Arc",  Size = "2",   Style = "NOM", Color = "GREY" } },
                    { "STR",  new StylePreset { Name = "Str",  Size = "2.5", Style = "BOLD", Color = "RED" } },
                },
                ValueBoxColors = new Dictionary<string, BoxColorPreset>(StringComparer.OrdinalIgnoreCase)
                {
                    { "HVAC", new BoxColorPreset { Name = "HVAC Box", R = 200, G = 220, B = 255, BoxStyle = "SOLID" } },
                    { "FP",   new BoxColorPreset { Name = "Fire Box", R = 255, G = 200, B = 200, BoxStyle = "SOLID" } },
                    { "SAN",  new BoxColorPreset { Name = "San Box",  R = 200, G = 255, B = 220, BoxStyle = "SOLID" } },
                },
            };

            // ── By Status ──
            d["Status"] = new VariableColorScheme
            {
                Name = "Status", Description = "Color by element lifecycle status",
                Variable = StyleVariable.Status,
                ValueColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "NEW",         new Color(0, 180, 0) },      // Green
                    { "EXISTING",    new Color(0, 128, 255) },    // Blue
                    { "DEMOLISHED",  new Color(200, 0, 0) },      // Red
                    { "TEMPORARY",   new Color(255, 160, 0) },    // Orange
                },
                ValueStyles = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase)
                {
                    { "NEW",         new StylePreset { Name = "New",   Size = "2.5", Style = "BOLD",   Color = "GREEN" } },
                    { "EXISTING",    new StylePreset { Name = "Exist", Size = "2",   Style = "NOM",    Color = "BLUE" } },
                    { "DEMOLISHED",  new StylePreset { Name = "Demo",  Size = "2",   Style = "ITALIC", Color = "RED" } },
                    { "TEMPORARY",   new StylePreset { Name = "Temp",  Size = "2",   Style = "ITALIC", Color = "ORANGE" } },
                },
                ValueBoxColors = new Dictionary<string, BoxColorPreset>(StringComparer.OrdinalIgnoreCase)
                {
                    { "NEW",         new BoxColorPreset { Name = "New",   R = 200, G = 255, B = 200, BoxStyle = "SOLID" } },
                    { "EXISTING",    new BoxColorPreset { Name = "Exist", R = 200, G = 220, B = 255, BoxStyle = "DASHED" } },
                    { "DEMOLISHED",  new BoxColorPreset { Name = "Demo",  R = 255, G = 200, B = 200, BoxStyle = "DASHED" } },
                    { "TEMPORARY",   new BoxColorPreset { Name = "Temp",  R = 255, G = 240, B = 200, BoxStyle = "DASHED" } },
                },
            };

            // ── By Zone ──
            d["Zone"] = new VariableColorScheme
            {
                Name = "Zone", Description = "Color by spatial zone",
                Variable = StyleVariable.Zone,
                ValueColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Z01", new Color(0, 128, 255) },    // Blue
                    { "Z02", new Color(0, 180, 0) },      // Green
                    { "Z03", new Color(255, 160, 0) },    // Orange
                    { "Z04", new Color(200, 0, 0) },      // Red
                    { "ZZ",  new Color(128, 128, 128) },  // Grey (unassigned)
                    { "XX",  new Color(80, 80, 80) },     // Dark Grey (unknown)
                },
                ValueStyles = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Z01", new StylePreset { Name = "Z01", Size = "2", Style = "NOM", Color = "BLUE" } },
                    { "Z02", new StylePreset { Name = "Z02", Size = "2", Style = "NOM", Color = "GREEN" } },
                    { "Z03", new StylePreset { Name = "Z03", Size = "2", Style = "NOM", Color = "ORANGE" } },
                    { "Z04", new StylePreset { Name = "Z04", Size = "2", Style = "NOM", Color = "RED" } },
                },
            };

            // ── By Level ──
            d["Level"] = new VariableColorScheme
            {
                Name = "Level", Description = "Color by building level",
                Variable = StyleVariable.Level,
                ValueColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "GF",  new Color(0, 180, 0) },      // Green — Ground
                    { "L01", new Color(0, 128, 255) },     // Blue — Level 1
                    { "L02", new Color(160, 0, 200) },     // Purple — Level 2
                    { "L03", new Color(0, 200, 200) },     // Cyan — Level 3
                    { "B1",  new Color(200, 0, 0) },       // Red — Basement
                    { "B2",  new Color(160, 0, 0) },       // Dark Red — Sub-basement
                    { "RF",  new Color(255, 160, 0) },     // Orange — Roof
                    { "XX",  new Color(128, 128, 128) },   // Grey — Unknown
                },
                ValueStyles = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase)
                {
                    { "GF",  new StylePreset { Name = "GF",  Size = "2.5", Style = "BOLD", Color = "GREEN" } },
                    { "L01", new StylePreset { Name = "L01", Size = "2",   Style = "NOM",  Color = "BLUE" } },
                    { "L02", new StylePreset { Name = "L02", Size = "2",   Style = "NOM",  Color = "PURPLE" } },
                    { "B1",  new StylePreset { Name = "B1",  Size = "2",   Style = "ITALIC", Color = "RED" } },
                    { "RF",  new StylePreset { Name = "RF",  Size = "2",   Style = "ITALIC", Color = "ORANGE" } },
                },
            };

            // ── By Location ──
            d["Location"] = new VariableColorScheme
            {
                Name = "Location", Description = "Color by building location",
                Variable = StyleVariable.Location,
                ValueColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "BLD1", new Color(0, 128, 255) },    // Blue
                    { "BLD2", new Color(0, 180, 0) },      // Green
                    { "BLD3", new Color(255, 160, 0) },    // Orange
                    { "EXT",  new Color(160, 0, 200) },    // Purple
                    { "XX",   new Color(128, 128, 128) },  // Grey
                },
                ValueStyles = new Dictionary<string, StylePreset>(StringComparer.OrdinalIgnoreCase)
                {
                    { "BLD1", new StylePreset { Name = "BLD1", Size = "2.5", Style = "BOLD", Color = "BLUE" } },
                    { "BLD2", new StylePreset { Name = "BLD2", Size = "2.5", Style = "BOLD", Color = "GREEN" } },
                    { "BLD3", new StylePreset { Name = "BLD3", Size = "2.5", Style = "BOLD", Color = "ORANGE" } },
                    { "EXT",  new StylePreset { Name = "EXT",  Size = "2.5", Style = "BOLDITALIC", Color = "PURPLE" } },
                },
            };

            // ── By Function ──
            d["Function"] = new VariableColorScheme
            {
                Name = "Function", Description = "Color by element function code",
                Variable = StyleVariable.Function,
                ValueColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
                {
                    { "SUP", new Color(0, 128, 255) },      // Blue — Supply
                    { "HTG", new Color(200, 60, 0) },        // Dark Orange — Heating
                    { "CLG", new Color(0, 180, 200) },       // Cyan — Cooling
                    { "DCW", new Color(0, 180, 120) },       // Teal — Cold Water
                    { "SAN", new Color(0, 160, 0) },         // Green — Sanitary
                    { "PWR", new Color(255, 180, 0) },       // Gold — Power
                    { "LTG", new Color(255, 255, 0) },       // Yellow — Lighting
                    { "FIR", new Color(200, 0, 0) },         // Red — Fire
                    { "COM", new Color(100, 100, 200) },     // Periwinkle — Comms
                    { "VEN", new Color(100, 200, 255) },     // Light Blue — Ventilation
                    { "DRN", new Color(80, 130, 80) },       // Olive — Drainage
                    { "GEN", new Color(120, 120, 120) },     // Grey — General
                },
            };

            return d;
        }

        // ════════════════════════════════════════════════════════════════
        // APPLY TAG STYLE — Set BOOL params on element types
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply a tag style preset to element types. Sets exactly ONE style BOOL to true
        /// and all others to false, making the corresponding label row visible in tag families.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="preset">The style preset to apply.</param>
        /// <param name="elements">Elements to style (null = all element types).</param>
        /// <returns>Number of element types updated.</returns>
        public static int ApplyTagStyle(Document doc, StylePreset preset, ICollection<Element> elements = null)
        {
            string activeParam = preset.ParamName;
            string[] allStyleParams = ParamRegistry.AllTagStyleParams;
            int updated = 0;

            var targets = elements ?? new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToList();

            foreach (Element el in targets)
            {
                bool any = false;
                foreach (string param in allStyleParams)
                {
                    Parameter p = el.LookupParameter(param);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                    bool shouldBeOn = (param == activeParam);
                    int current = p.AsInteger();
                    if (current != (shouldBeOn ? 1 : 0))
                    {
                        p.Set(shouldBeOn ? 1 : 0);
                        any = true;
                    }
                }
                if (any) updated++;
            }

            StingLog.Info($"TagStyle: Applied {preset.TypeName} to {updated} types");
            return updated;
        }

        /// <summary>
        /// Apply discipline-aware tag styles from a color scheme.
        /// Each element gets the style matching its DISC token.
        /// </summary>
        public static int ApplyDisciplineTagStyles(Document doc, ColorScheme scheme)
        {
            int updated = 0;
            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToList();

            // Also need instances to determine discipline per type
            var instancesByType = new Dictionary<ElementId, string>();
            var allInstances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var inst in allInstances)
            {
                var typeId = inst.GetTypeId();
                if (typeId == ElementId.InvalidElementId) continue;
                if (instancesByType.ContainsKey(typeId)) continue;

                string disc = ParameterHelpers.GetString(inst, ParamRegistry.DISC);
                if (!string.IsNullOrEmpty(disc))
                    instancesByType[typeId] = disc;
            }

            string[] allStyleParams = ParamRegistry.AllTagStyleParams;

            foreach (Element typeEl in allTypes)
            {
                // Determine discipline for this type
                string disc = "";
                if (instancesByType.TryGetValue(typeEl.Id, out string d))
                    disc = d;

                // Find the style preset for this discipline
                StylePreset preset = scheme.DefaultTagStyle;
                if (!string.IsNullOrEmpty(disc) && scheme.DisciplineTagStyles.TryGetValue(disc, out StylePreset dp))
                    preset = dp;

                if (preset == null) continue;

                string activeParam = preset.ParamName;
                bool any = false;
                foreach (string param in allStyleParams)
                {
                    Parameter p = typeEl.LookupParameter(param);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                    bool shouldBeOn = (param == activeParam);
                    int current = p.AsInteger();
                    if (current != (shouldBeOn ? 1 : 0))
                    {
                        p.Set(shouldBeOn ? 1 : 0);
                        any = true;
                    }
                }
                if (any) updated++;
            }

            StingLog.Info($"TagStyle: Applied discipline styles from scheme '{scheme.Name}', {updated} types updated");
            return updated;
        }

        // ════════════════════════════════════════════════════════════════
        // APPLY COLOR SCHEME — View graphic overrides per discipline
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply a color scheme to the active view. Colors all taggable elements
        /// by their discipline, matching the presentation screenshot styles.
        /// </summary>
        public static int ApplyColorScheme(Document doc, View view, ColorScheme scheme)
        {
            int colored = 0;
            var solidFill = FindSolidFill(doc);

            // Get all taggable elements in view
            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var el in elements)
            {
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                Color color = null;

                // Try discipline-specific color first
                if (!string.IsNullOrEmpty(disc) && scheme.DisciplineColors.TryGetValue(disc, out Color dc))
                    color = dc;
                // Fall back to "ALL" color
                else if (scheme.ElementColors.TryGetValue("ALL", out Color allColor))
                    color = allColor;

                if (color == null) continue;

                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(color);
                if (solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    ogs.SetCutForegroundPatternId(solidFill.Id);
                    ogs.SetCutForegroundPatternColor(color);
                }

                view.SetElementOverrides(el.Id, ogs);
                colored++;
            }

            StingLog.Info($"TagStyle: Applied color scheme '{scheme.Name}' to {colored} elements in view '{view.Name}'");
            return colored;
        }

        /// <summary>Clear all graphic overrides from the active view.</summary>
        public static int ClearColorScheme(Document doc, View view)
        {
            int cleared = 0;
            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToList();

            var blank = new OverrideGraphicSettings();
            foreach (var el in elements)
            {
                view.SetElementOverrides(el.Id, blank);
                cleared++;
            }

            StingLog.Info($"TagStyle: Cleared overrides from {cleared} elements in view '{view.Name}'");
            return cleared;
        }

        // ════════════════════════════════════════════════════════════════
        // PARAGRAPH DEPTH — Set state 1-10 visibility tiers
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Set paragraph depth by enabling states 1 through maxTier (inclusive)
        /// and disabling states above maxTier. Works on element types.
        /// </summary>
        /// <param name="doc">The document.</param>
        /// <param name="maxTier">Maximum visible tier (1-10). Tiers 1..maxTier ON, rest OFF.</param>
        /// <param name="warnVisible">Show/hide warning text.</param>
        /// <returns>Number of element types updated.</returns>
        public static int SetParagraphDepth(Document doc, int maxTier, bool warnVisible)
        {
            maxTier = Math.Max(1, Math.Min(10, maxTier));
            string[] states = ParamRegistry.AllParaStates;
            int updated = 0;

            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToList();

            foreach (Element typeEl in allTypes)
            {
                bool any = false;
                for (int i = 0; i < states.Length; i++)
                {
                    Parameter p = typeEl.LookupParameter(states[i]);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                    bool shouldBeOn = (i < maxTier);
                    if (p.AsInteger() != (shouldBeOn ? 1 : 0))
                    {
                        p.Set(shouldBeOn ? 1 : 0);
                        any = true;
                    }
                }

                // Warning visibility
                Parameter warnP = typeEl.LookupParameter(ParamRegistry.WARN_VISIBLE);
                if (warnP != null && !warnP.IsReadOnly && warnP.StorageType == StorageType.Integer)
                {
                    if (warnP.AsInteger() != (warnVisible ? 1 : 0))
                    {
                        warnP.Set(warnVisible ? 1 : 0);
                        any = true;
                    }
                }

                if (any) updated++;
            }

            StingLog.Info($"TagStyle: Set paragraph depth to tier {maxTier}, warnings {(warnVisible ? "ON" : "OFF")}, {updated} types updated");
            return updated;
        }

        // ════════════════════════════════════════════════════════════════
        // REPORT — Generate style status report
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scan element types and report which tag styles are currently active.
        /// </summary>
        public static string GenerateStyleReport(Document doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TAG STYLE STATUS REPORT");
            sb.AppendLine(new string('═', 50));

            string[] allStyleParams = ParamRegistry.AllTagStyleParams;
            var styleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToList();

            int withStyle = 0;
            int noStyle = 0;

            foreach (Element typeEl in allTypes)
            {
                string activeStyle = null;
                foreach (string param in allStyleParams)
                {
                    Parameter p = typeEl.LookupParameter(param);
                    if (p != null && p.StorageType == StorageType.Integer && p.AsInteger() == 1)
                    {
                        activeStyle = param.Replace("TAG_", "").Replace("_BOOL", "");
                        break;
                    }
                }

                if (activeStyle != null)
                {
                    withStyle++;
                    if (styleCounts.ContainsKey(activeStyle))
                        styleCounts[activeStyle]++;
                    else
                        styleCounts[activeStyle] = 1;
                }
                else
                {
                    noStyle++;
                }
            }

            sb.AppendLine($"Types with active style: {withStyle}");
            sb.AppendLine($"Types without style: {noStyle}");
            sb.AppendLine();

            if (styleCounts.Count > 0)
            {
                sb.AppendLine("Active styles:");
                foreach (var kv in styleCounts.OrderByDescending(x => x.Value))
                    sb.AppendLine($"  {kv.Key}: {kv.Value} types");
            }

            // Paragraph state scan
            sb.AppendLine();
            sb.AppendLine("PARAGRAPH STATE:");
            string[] states = ParamRegistry.AllParaStates;
            int typeSample = 0;
            int[] stateOnCounts = new int[states.Length];
            foreach (Element typeEl in allTypes.Take(100))
            {
                typeSample++;
                for (int i = 0; i < states.Length; i++)
                {
                    Parameter p = typeEl.LookupParameter(states[i]);
                    if (p != null && p.StorageType == StorageType.Integer && p.AsInteger() == 1)
                        stateOnCounts[i]++;
                }
            }
            for (int i = 0; i < states.Length; i++)
            {
                sb.AppendLine($"  State {i + 1}: {stateOnCounts[i]}/{typeSample} types ON");
            }

            return sb.ToString();
        }

        // ════════════════════════════════════════════════════════════════
        // VARIABLE-DRIVEN STYLE APPLICATION — Color by any tag variable
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get the element's value for a given style variable.
        /// </summary>
        public static string GetVariableValue(Element el, StyleVariable variable)
        {
            return variable switch
            {
                StyleVariable.Discipline => ParameterHelpers.GetString(el, ParamRegistry.DISC),
                StyleVariable.System     => ParameterHelpers.GetString(el, ParamRegistry.SYS),
                StyleVariable.Status     => ParameterHelpers.GetString(el, ParamRegistry.STATUS),
                StyleVariable.Zone       => ParameterHelpers.GetString(el, ParamRegistry.ZONE),
                StyleVariable.Level      => ParameterHelpers.GetString(el, ParamRegistry.LVL),
                StyleVariable.Function   => ParameterHelpers.GetString(el, ParamRegistry.FUNC),
                StyleVariable.Location   => ParameterHelpers.GetString(el, ParamRegistry.LOC),
                _ => "",
            };
        }

        /// <summary>
        /// Apply a variable-driven color scheme to a view.
        /// Colors elements AND sets box colors AND switches tag styles
        /// based on any tag variable (not just discipline).
        /// </summary>
        public static int ApplyVariableScheme(Document doc, View view, VariableColorScheme scheme)
        {
            int colored = 0;
            var solidFill = FindSolidFill(doc);

            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var el in elements)
            {
                string value = GetVariableValue(el, scheme.Variable);
                Color color = null;

                if (!string.IsNullOrEmpty(value) && scheme.ValueColors.TryGetValue(value, out Color vc))
                    color = vc;
                else
                    color = scheme.DefaultColor;

                if (color == null) continue;

                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(color);
                if (solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    ogs.SetCutForegroundPatternId(solidFill.Id);
                    ogs.SetCutForegroundPatternColor(color);
                }

                view.SetElementOverrides(el.Id, ogs);
                colored++;

                // Set bounding box color parameters if scheme defines them
                if (scheme.ValueBoxColors.TryGetValue(value, out BoxColorPreset boxPreset))
                    SetBoxColor(el, boxPreset);
            }

            StingLog.Info($"TagStyle: Applied variable scheme '{scheme.Name}' ({scheme.Variable}) to {colored} elements");
            return colored;
        }

        /// <summary>
        /// Apply variable-driven tag styles to element types.
        /// Reads the variable from instances, then applies the matching style
        /// BOOL to the corresponding type.
        /// </summary>
        public static int ApplyVariableTagStyles(Document doc, VariableColorScheme scheme)
        {
            int updated = 0;
            string[] allStyleParams = ParamRegistry.AllTagStyleParams;

            // Build type → variable value map from instances
            var typeVariables = new Dictionary<ElementId, string>();
            var allInstances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var inst in allInstances)
            {
                var typeId = inst.GetTypeId();
                if (typeId == ElementId.InvalidElementId || typeVariables.ContainsKey(typeId)) continue;
                string value = GetVariableValue(inst, scheme.Variable);
                if (!string.IsNullOrEmpty(value))
                    typeVariables[typeId] = value;
            }

            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToList();

            foreach (Element typeEl in allTypes)
            {
                // Determine variable value for this type
                string value = "";
                if (typeVariables.TryGetValue(typeEl.Id, out string v))
                    value = v;

                // Find the style preset for this value
                StylePreset preset = scheme.DefaultStyle;
                if (!string.IsNullOrEmpty(value) && scheme.ValueStyles.TryGetValue(value, out StylePreset sp))
                    preset = sp;

                if (preset == null) continue;

                string activeParam = preset.ParamName;
                bool any = false;
                foreach (string param in allStyleParams)
                {
                    Parameter p = typeEl.LookupParameter(param);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                    bool shouldBeOn = (param == activeParam);
                    int current = p.AsInteger();
                    if (current != (shouldBeOn ? 1 : 0))
                    {
                        p.Set(shouldBeOn ? 1 : 0);
                        any = true;
                    }
                }
                if (any) updated++;
            }

            StingLog.Info($"TagStyle: Applied variable styles for '{scheme.Variable}', {updated} types updated");
            return updated;
        }

        // ════════════════════════════════════════════════════════════════
        // BOUNDING BOX COLOR CONTROL — Separate from text color
        // Colors match semantic meaning: discipline/system/status/zone
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Set bounding box color parameters on an element.
        /// These control the tag family's box fill color when the label family
        /// has a filled region bound to TAG_BOX_COLOR_R/G/B_INT parameters.
        /// </summary>
        public static void SetBoxColor(Element el, BoxColorPreset preset)
        {
            SetIntParam(el, ParamRegistry.TAG_BOX_COLOR_R, preset.R);
            SetIntParam(el, ParamRegistry.TAG_BOX_COLOR_G, preset.G);
            SetIntParam(el, ParamRegistry.TAG_BOX_COLOR_B, preset.B);

            Parameter boxVis = el.LookupParameter(ParamRegistry.TAG_BOX_VISIBLE);
            if (boxVis != null && !boxVis.IsReadOnly && boxVis.StorageType == StorageType.Integer)
                boxVis.Set(preset.BoxVisible ? 1 : 0);

            Parameter boxStyle = el.LookupParameter(ParamRegistry.TAG_BOX_STYLE);
            if (boxStyle != null && !boxStyle.IsReadOnly && boxStyle.StorageType == StorageType.String)
                boxStyle.Set(preset.BoxStyle);
        }

        /// <summary>
        /// Set leader color parameters on an element.
        /// </summary>
        public static void SetLeaderColor(Element el, BoxColorPreset preset)
        {
            SetIntParam(el, ParamRegistry.TAG_LEADER_COLOR_R, preset.R);
            SetIntParam(el, ParamRegistry.TAG_LEADER_COLOR_G, preset.G);
            SetIntParam(el, ParamRegistry.TAG_LEADER_COLOR_B, preset.B);
        }

        /// <summary>
        /// Apply bounding box colors to all elements in view based on a variable.
        /// Box colors MATCH the element/discipline colors but are controlled SEPARATELY
        /// so you can have blue-boxed mechanical tags on green-colored plumbing views.
        /// </summary>
        public static int ApplyBoxColorsByVariable(Document doc, View view, VariableColorScheme scheme)
        {
            int colored = 0;
            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var el in elements)
            {
                string value = GetVariableValue(el, scheme.Variable);
                if (string.IsNullOrEmpty(value)) continue;

                if (scheme.ValueBoxColors.TryGetValue(value, out BoxColorPreset boxPreset))
                {
                    SetBoxColor(el, boxPreset);
                    colored++;
                }
            }

            StingLog.Info($"TagStyle: Set box colors for {colored} elements by {scheme.Variable}");
            return colored;
        }

        private static void SetIntParam(Element el, string paramName, int value)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer)
                p.Set(value);
        }

        // ── Helper ────────────────────────────────────────────────────

        private static FillPatternElement FindSolidFill(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            }
            catch { return null; }
        }
    }
}
