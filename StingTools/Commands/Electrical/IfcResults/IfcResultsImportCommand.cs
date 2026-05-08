using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.IfcResults;

namespace StingTools.Commands.Electrical.IfcResults
{
    /// <summary>
    /// Reads an IFC file produced by DIALux evo / ElumTools / Relux and
    /// maps every <c>IfcLightFixture</c> + <c>IfcSpace</c> back to a Revit
    /// element by GUID (preferred) or by name (fallback). Lux / UGR /
    /// uniformity values are written into the engine-specific shared
    /// parameters introduced in Phase 181 so the multi-engine aggregator
    /// can show them side-by-side.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IfcResultsImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select IFC export from DIALux evo / ElumTools / Relux",
                Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog() != true) return Result.Cancelled;
            string ifcPath = ofd.FileName;

            string engine = AskEngine();
            if (string.IsNullOrEmpty(engine)) return Result.Cancelled;

            var parsed = IfcSimpleParser.ParseFile(ifcPath);
            if (parsed.Spaces.Count == 0 && parsed.LightFixtures.Count == 0)
            {
                TaskDialog.Show("STING IFC Import",
                    $"No IfcSpace or IfcLightFixture entities found in:\n{ifcPath}\n\n" +
                    (parsed.Warnings.Count == 0 ? "" : "Parser warnings:\n  " + string.Join("\n  ", parsed.Warnings)));
                return Result.Cancelled;
            }

            // Build Revit-side lookup tables.
            var roomsByGuid = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
            var roomsByName = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<Room>()
                .Where(r => r.Area > 0))
            {
                // Index by both the raw UniqueId AND the 22-char IFC-encoded
                // form (the export uses IfcGuidEncoder so DIALux preserves the
                // IfcGloballyUniqueId verbatim — match against both shapes for
                // safety against DIALux ever re-emitting in either form).
                try { roomsByGuid[r.UniqueId] = r; }
                catch (Exception ex) { StingLog.Warn($"IfcImport room raw key '{r.UniqueId}': {ex.Message}"); }
                try { roomsByGuid[IfcGuidEncoder.FromRevitUniqueId(r.UniqueId)] = r; }
                catch (Exception ex) { StingLog.Warn($"IfcImport room encoded key '{r.UniqueId}': {ex.Message}"); }
                if (!string.IsNullOrEmpty(r.Name)) roomsByName[r.Name] = r;
            }
            var fixturesByGuid = new Dictionary<string, FamilyInstance>(StringComparer.OrdinalIgnoreCase);
            foreach (var fi in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                try { fixturesByGuid[fi.UniqueId] = fi; }
                catch (Exception ex) { StingLog.Warn($"IfcImport fixture raw key '{fi.UniqueId}': {ex.Message}"); }
                try { fixturesByGuid[IfcGuidEncoder.FromRevitUniqueId(fi.UniqueId)] = fi; }
                catch (Exception ex) { StingLog.Warn($"IfcImport fixture encoded key '{fi.UniqueId}': {ex.Message}"); }
            }

            string engineParam = ResolveEngineParam(engine);
            string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            int matchedRooms = 0, matchedFixtures = 0, missing = 0;
            using (var tx = new Transaction(doc, $"STING Import {engine} Results"))
            {
                tx.Start();
                foreach (var space in parsed.Spaces)
                {
                    Room target = null;
                    if (roomsByGuid.TryGetValue(space.GlobalId, out var byGuid)) target = byGuid;
                    else if (!string.IsNullOrEmpty(space.Name)
                        && roomsByName.TryGetValue(space.Name, out var byName)) target = byName;
                    if (target == null) { missing++; continue; }

                    double lux = ExtractByAlias(space.Numerics, StingLightingPSet.IlluminanceAliases);
                    double ugr = ExtractByAlias(space.Numerics, StingLightingPSet.UgrAliases);
                    double uo  = ExtractByAlias(space.Numerics, StingLightingPSet.UniformityAliases);

                    if (lux > 0)
                    {
                        ParameterHelpers.SetString(target, engineParam, $"{lux:0.00}", overwrite: true);
                        // Phase 178 ELC_PHOTO_LUX_CALC stays as the "headline" value; engine
                        // params let the aggregator break it down per engine.
                        ParameterHelpers.SetString(target, ParamRegistry.ELC_PHOTO_LUX, $"{lux:0.00}", overwrite: true);
                    }
                    if (ugr > 0)
                        ParameterHelpers.SetString(target, ParamRegistry.ELC_PHOTO_UGR, $"{ugr:0.0}", overwrite: true);
                    if (uo > 0)
                        ParameterHelpers.SetString(target, ParamRegistry.ELC_PHOTO_UNIFORMITY, $"{uo:0.00}", overwrite: true);
                    ParameterHelpers.SetString(target, ParamRegistry.ELC_PHOTO_LAST_ENGINE, engine, overwrite: true);
                    ParameterHelpers.SetString(target, ParamRegistry.ELC_PHOTO_LAST_CALC_DATE, nowIso, overwrite: true);
                    matchedRooms++;
                }

                foreach (var fix in parsed.LightFixtures)
                {
                    if (!fixturesByGuid.TryGetValue(fix.GlobalId, out var revitFi)) continue;
                    // Currently no per-fixture results are written back; the
                    // luminaire matching is still useful for the aggregator
                    // and for future per-fixture metrics (e.g. utilisation %).
                    matchedFixtures++;
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING IFC Import",
                $"Imported {engine} results from:\n{ifcPath}\n\n" +
                $"Matched: {matchedRooms} room(s), {matchedFixtures} luminaire(s).\n" +
                $"Missing rooms (no GUID/name match): {missing}\n\n" +
                "Check the multi-engine aggregator to compare results side-by-side.");
            return Result.Succeeded;
        }

        private static string AskEngine()
        {
            var dlg = new TaskDialog("STING IFC Results — Engine")
            {
                MainInstruction = "Which engine produced the IFC?",
                MainContent = "STING writes the lux value to an engine-specific shared parameter so " +
                              "the multi-engine aggregator can compare results side-by-side.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "DIALux evo");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "ElumTools");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Relux");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Other / generic");
            return dlg.Show() switch
            {
                TaskDialogResult.CommandLink1 => "DIALux",
                TaskDialogResult.CommandLink2 => "ElumTools",
                TaskDialogResult.CommandLink3 => "Relux",
                TaskDialogResult.CommandLink4 => "Other",
                _ => null
            };
        }

        private static string ResolveEngineParam(string engine) => engine switch
        {
            "DIALux"    => ParamRegistry.ELC_PHOTO_LUX_DIALUX,
            "ElumTools" => ParamRegistry.ELC_PHOTO_LUX_ELUMTOOLS,
            "Relux"     => ParamRegistry.ELC_PHOTO_LUX_RELUX,
            _           => ParamRegistry.ELC_PHOTO_LUX
        };

        private static double ExtractByAlias(Dictionary<string, double> nums, string[] aliases)
        {
            foreach (var key in aliases)
                if (nums.TryGetValue(key, out double v) && v > 0) return v;
            return 0;
        }
    }
}
