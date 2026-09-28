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

            // Build Revit-side keys. A Revit UniqueId is NOT an IFC GlobalId (45 chars
            // vs the 22-char base64 form), so matching on UniqueId could never succeed.
            // Each room offers: the IfcGUID parameter (written by Revit's IFC exporter
            // when "store GUID" is on) and the GlobalId re-encoded from the element
            // (IfcGuidEncoder — what STING's own DIALux export writes).
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<Room>()
                .Where(r => r.Area > 0).ToList();
            var roomById = rooms.ToDictionary(r => r.Id.Value.ToString(), r => r);
            var roomKeys = new List<IfcSpaceMatcher.RoomKey>();
            foreach (var r in rooms)
            {
                var key = new IfcSpaceMatcher.RoomKey
                {
                    Id = r.Id.Value.ToString(),
                    Number = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                    Name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""
                };
                try
                {
                    string stored = r.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString();
                    if (string.IsNullOrEmpty(stored)) stored = r.LookupParameter("IfcGUID")?.AsString();
                    if (!string.IsNullOrEmpty(stored)) key.IfcGuids.Add(stored.Trim());
                }
                catch (Exception ex) { StingLog.Warn($"IfcResultsImport IfcGUID room {r.Id}: {ex.Message}"); }
                try { key.IfcGuids.Add(IfcGuidEncoder.FromElementGoldStandard(r)); }
                catch (Exception ex) { StingLog.Warn($"IfcResultsImport encode room {r.Id}: {ex.Message}"); }
                roomKeys.Add(key);
            }
            var spaceKeys = parsed.Spaces.Select(sp => new IfcSpaceMatcher.SpaceKey
            {
                GlobalId = sp.GlobalId, Name = sp.Name, LongName = sp.LongName
            }).ToList();
            var match = IfcSpaceMatcher.MatchAll(roomKeys, spaceKeys);

            var fixtureGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fi in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                try
                {
                    string stored = fi.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString();
                    if (!string.IsNullOrEmpty(stored)) fixtureGuids.Add(stored.Trim());
                    fixtureGuids.Add(IfcGuidEncoder.FromElementGoldStandard(fi));
                }
                catch (Exception ex) { StingLog.Warn($"IfcResultsImport fixture {fi.Id}: {ex.Message}"); }
            }

            string engineParam = ResolveEngineParam(engine);
            string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            int matchedFixtures = 0;
            var via = match.Matches.GroupBy(m => m.Via).ToDictionary(g => g.Key, g => g.Count());
            using (var tx = new Transaction(doc, $"STING Import {engine} Results"))
            {
                tx.Start();
                foreach (var m in match.Matches)
                {
                    var space = parsed.Spaces[m.SpaceIndex];
                    var target = roomById[m.RoomId];

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
                }

                foreach (var fix in parsed.LightFixtures)
                {
                    // No per-fixture results are written back; the count shows
                    // whether luminaire identity survived the round trip.
                    if (fixtureGuids.Contains(fix.GlobalId)) matchedFixtures++;
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string List(string title, List<string> items) => items.Count == 0 ? "" :
                $"\n{title} ({items.Count}):\n  " + string.Join("\n  ", items.Take(12)) +
                (items.Count > 12 ? $"\n  … +{items.Count - 12} more" : "") + "\n";
            string viaText = string.Join(", ", via.Select(kv => $"{kv.Value} by {kv.Key}"));
            TaskDialog.Show("STING IFC Import",
                $"Imported {engine} results from:\n{ifcPath}\n\n" +
                $"Matched: {match.Matches.Count} of {parsed.Spaces.Count} space(s)" +
                (viaText.Length > 0 ? $" ({viaText})" : "") + ", " +
                $"{matchedFixtures} of {parsed.LightFixtures.Count} luminaire(s) by GlobalId.\n" +
                List("NOT imported — duplicate (a room already matched by an earlier space)", match.Duplicates) +
                List("NOT imported — ambiguous (key shared by several rooms)", match.Ambiguous) +
                List("NOT imported — no GlobalId / number / name match", match.Unmatched) +
                "\nCheck the multi-engine aggregator to compare results side-by-side.");
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
