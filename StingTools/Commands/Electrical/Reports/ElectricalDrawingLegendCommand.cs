using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Symbols;
using StingTools.Tags;

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Auto-builds an electrical-drawing legend by walking every electrical
    /// family instance in the project, deduplicating by family/type, and
    /// resolving each to its IEC 60617 / IEEE 315 / NFPA 170 / CIBSE / BS 1553
    /// concept ID via <see cref="SymbolConceptRegistry"/>. Produces a
    /// drafting / Legend view titled "STING Electrical Symbols Legend"
    /// with one row per unique symbol type:
    ///
    ///   [discipline-coloured swatch]  Family : Type   ·   Concept ID   ·   Standard ref
    ///
    /// The discipline-coloured swatch communicates at a glance whether the
    /// row is Lighting (yellow), Power (blue), Fire (red), Data (cyan), etc.
    /// — the actual visual symbol still appears on the drawings themselves;
    /// this view documents *which* symbols are used and to which standard.
    ///
    /// Reuses the Phase 92 LegendBuilder engine (FilledRegion swatches +
    /// TextNote labels + multi-column layout + native Legend view fallback)
    /// so the output integrates with the rest of STING's legend pipeline
    /// (PlaceLegendOnAllSheets / SheetContextLegend / UpdateLegend).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElectricalDrawingLegendCommand : IExternalCommand
    {
        // Categories considered "electrical drawing scope" for the legend.
        private static readonly BuiltInCategory[] ElectricalCategories =
        {
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_CommunicationDevices,
        };

        // Discipline → swatch colour. Echoes the STING convention used
        // elsewhere (LegendBuilder.DisciplineNames + ColorHelper palettes)
        // so a viewer who's seen one STING legend recognises the colour.
        private static readonly Dictionary<BuiltInCategory, Color> CategorySwatch =
            new Dictionary<BuiltInCategory, Color>
            {
                { BuiltInCategory.OST_LightingFixtures,    new Color(255, 235, 59)  }, // yellow
                { BuiltInCategory.OST_LightingDevices,     new Color(255, 193, 7)   }, // amber
                { BuiltInCategory.OST_ElectricalEquipment, new Color(33, 150, 243)  }, // blue
                { BuiltInCategory.OST_ElectricalFixtures,  new Color(63, 81, 181)   }, // indigo
                { BuiltInCategory.OST_FireAlarmDevices,    new Color(244, 67, 54)   }, // red
                { BuiltInCategory.OST_DataDevices,         new Color(0, 188, 212)   }, // cyan
                { BuiltInCategory.OST_TelephoneDevices,    new Color(0, 150, 136)   }, // teal
                { BuiltInCategory.OST_NurseCallDevices,    new Color(233, 30, 99)   }, // pink
                { BuiltInCategory.OST_SecurityDevices,     new Color(96, 125, 139)  }, // blue-grey
                { BuiltInCategory.OST_CommunicationDevices,new Color(121, 85, 72)   }, // brown
            };

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Active project standard for the legend subtitle. ResolveStandard
            // with null view/host falls through Levels 1-4 and lands on the
            // project-global config (Level 5) — same value SetProjectStandard
            // writes. No public GetProjectStandard exists; this is the
            // documented read path.
            string activeStd = "";
            try { activeStd = SymbolStandardResolver.ResolveStandard(doc, null, null) ?? ""; } catch { }
            if (string.IsNullOrEmpty(activeStd)) activeStd = "Mixed (per family)";

            // Walk every electrical family instance, dedupe by (category, family, type).
            var groups = new Dictionary<string, RowGroup>();
            foreach (var cat in ElectricalCategories)
            {
                try
                {
                    foreach (var fi in new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType()
                        .OfType<FamilyInstance>())
                    {
                        try
                        {
                            string famName = fi.Symbol?.FamilyName ?? "—";
                            string typeName = fi.Symbol?.Name ?? "—";
                            string key = $"{(int)cat}|{famName}|{typeName}";
                            if (!groups.TryGetValue(key, out var g))
                            {
                                string conceptId = "";
                                string standard  = "";
                                try
                                {
                                    conceptId = fi.LookupParameter("STING_SYMBOL_ID")?.AsString() ?? "";
                                    standard  = fi.LookupParameter("STING_SYMBOL_STANDARD")?.AsString() ?? "";
                                }
                                catch { }
                                if (string.IsNullOrEmpty(conceptId))
                                {
                                    // Fall back to the registry's family-name → concept lookup.
                                    try { conceptId = SymbolConceptRegistry.GetConceptForFamily(famName) ?? ""; } catch { }
                                }
                                string description = "";
                                if (!string.IsNullOrEmpty(conceptId))
                                {
                                    try { description = SymbolConceptRegistry.GetConcept(conceptId)?.Name ?? ""; } catch { }
                                }
                                g = new RowGroup
                                {
                                    Category    = cat,
                                    FamilyName  = famName,
                                    TypeName    = typeName,
                                    ConceptId   = conceptId,
                                    Description = description,
                                    Standard    = string.IsNullOrEmpty(standard) ? activeStd : standard,
                                    Count       = 0
                                };
                                groups[key] = g;
                            }
                            g.Count++;
                        }
                        catch (Exception ex) { StingLog.Warn($"DrawingLegend instance: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"DrawingLegend cat {cat}: {ex.Message}"); }
            }

            if (groups.Count == 0)
            {
                TaskDialog.Show("STING Electrical Drawing Legend",
                    "No electrical family instances found in the project.\n\n" +
                    "Place at least one luminaire / electrical device / fire alarm device, " +
                    "then re-run.");
                return Result.Cancelled;
            }

            // Build LegendEntry list. Sort by category, then concept ID, then family name —
            // this groups all lighting together, then all power, then fire alarm, etc.
            var entries = groups.Values
                .OrderBy(g => Array.IndexOf(ElectricalCategories, g.Category))
                .ThenBy(g => g.ConceptId)
                .ThenBy(g => g.FamilyName)
                .ThenBy(g => g.TypeName)
                .Select(g => new LegendBuilder.LegendEntry
                {
                    Color = CategorySwatch.TryGetValue(g.Category, out var c) ? c : new Color(128, 128, 128),
                    Label = $"{g.FamilyName} : {g.TypeName}",
                    Description = string.IsNullOrEmpty(g.ConceptId)
                        ? $"{CategoryShortName(g.Category)} · {g.Count} placed"
                        : $"{g.ConceptId} — {g.Description} · {g.Standard} · {g.Count} placed",
                    Bold = false,
                    Italic = false
                })
                .ToList();

            var config = new LegendBuilder.LegendConfig
            {
                Title = "Electrical Symbols Legend",
                Subtitle = $"Project standard: {activeStd}  ·  {entries.Count} unique symbols  ·  {DateTime.Now:yyyy-MM-dd}",
                Columns = entries.Count > 30 ? 2 : 1,
                ColumnWidth = 0.45,
                ShowCounts = false,
                IncludeTimestamp = true,
                Footer = "Per IEC 60617:2020 / IEEE/ANSI 315:1975 / NFPA 170:2021 / CIBSE Guide / BS 1553. " +
                         "Generated by STING Tools — verify symbol families match the project standard.",
                DrawSwatchBorders = true,
                DrawSeparators = true
            };

            View legendView = null;
            using (var tx = new Transaction(doc, "STING Electrical Drawing Legend"))
            {
                tx.Start();
                try
                {
                    legendView = LegendBuilder.CreateLegendView(doc, entries, config);
                }
                catch (Exception ex)
                {
                    StingLog.Error($"DrawingLegend create: {ex.Message}", ex);
                    msg = ex.Message;
                    tx.RollBack();
                    return Result.Failed;
                }
                tx.Commit();
            }

            if (legendView == null)
            {
                TaskDialog.Show("STING Electrical Drawing Legend",
                    "Failed to create the legend view. Check the project has at least one " +
                    "Drafting view family type loaded.");
                return Result.Failed;
            }

            // Activate the new view so the user sees the result immediately.
            try { ctx.UIDoc.ActiveView = legendView; } catch { }

            TaskDialog.Show("STING Electrical Drawing Legend",
                $"Created '{legendView.Name}' with {entries.Count} unique symbol(s) " +
                $"across {groups.Values.Select(g => g.Category).Distinct().Count()} discipline(s).\n\n" +
                "Drag the legend onto sheets via Project Browser, or use " +
                "DOCS → Place on All Sheets to batch-place it on the drawing set.");
            return Result.Succeeded;
        }

        private static string CategoryShortName(BuiltInCategory cat)
        {
            switch (cat)
            {
                case BuiltInCategory.OST_LightingFixtures:    return "Lighting";
                case BuiltInCategory.OST_LightingDevices:     return "Lighting control";
                case BuiltInCategory.OST_ElectricalEquipment: return "Power equipment";
                case BuiltInCategory.OST_ElectricalFixtures:  return "Power outlet";
                case BuiltInCategory.OST_FireAlarmDevices:    return "Fire alarm";
                case BuiltInCategory.OST_DataDevices:         return "Data";
                case BuiltInCategory.OST_TelephoneDevices:    return "Telephone";
                case BuiltInCategory.OST_NurseCallDevices:    return "Nurse call";
                case BuiltInCategory.OST_SecurityDevices:     return "Security";
                case BuiltInCategory.OST_CommunicationDevices:return "Comms";
                default: return "Electrical";
            }
        }

        private class RowGroup
        {
            public BuiltInCategory Category;
            public string FamilyName, TypeName, ConceptId, Description, Standard;
            public int Count;
        }
    }
}
