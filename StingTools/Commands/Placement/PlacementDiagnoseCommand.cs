// Phase 139.19 — Placement diagnostic. Walks the active document and
// the loaded rule pack and prints every fact the placement engine would
// see at run time. Use when "the engine isn't placing things where I
// expect" to pinpoint the real cause: wrong family placement type,
// missing FromRoom/ToRoom, host-wall mismatch, or stale plug-in build.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlacementDiagnoseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var doc = cd?.Application?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var (text, txtPath) = BuildReportText(doc);
            string preview = text.Length > 4000 ? text.Substring(0, 4000) + "\n…(truncated, see file)" : text;
            TaskDialog.Show("STING - Placement Diagnose", preview + $"\n\nFull report: {txtPath}");
            return Result.Succeeded;
        }

        /// <summary>
        /// Build the diagnostic report text (and write it to disk). Shared by
        /// Execute (which TaskDialogs it) and the Placement Centre (which renders
        /// it inline in the shared Report panel). Returns (report, filePath).
        /// </summary>
        public static (string Text, string FilePath) BuildReportText(Document doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Build: STING placement diagnostic — Phase 139.19 ({DateTime.UtcNow:O})");
            sb.AppendLine($"Document: {doc.Title}");

            // 1. Plug-in build sanity: confirm the user is on a current dll.
            sb.AppendLine($"FixturePlacementEngine.CurrentPhase: '{FixturePlacementEngine.CurrentPhase}'");
            sb.AppendLine();

            // 2. Active view + scope.
            var view = doc.ActiveView;
            sb.AppendLine($"Active view: {view?.Name} ({view?.GetType().Name})");
            int roomsAll = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Count();
            int roomsView = view is ViewPlan vp && vp.GenLevel != null
                ? new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Cast<Room>().Count(r => r.LevelId == vp.GenLevel.Id)
                : 0;
            sb.AppendLine($"Rooms total: {roomsAll}, on active level: {roomsView}");
            sb.AppendLine();

            // 3. Doors — placement engine's view of FromRoom / ToRoom.
            var doors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
            sb.AppendLine($"DOORS ({doors.Count}):");
            int doorsWithFromRoom = 0, doorsWithToRoom = 0, doorsHostedByWall = 0, doorsHostedByOther = 0;
            foreach (var d in doors.Take(50))
            {
                Room from = null, to = null;
                try { from = d.FromRoom; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                try { to = d.ToRoom; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (from != null) doorsWithFromRoom++;
                if (to != null) doorsWithToRoom++;
                bool wallHost = d.Host is Wall;
                if (wallHost) doorsHostedByWall++; else doorsHostedByOther++;
                string hostKind = d.Host == null ? "<null>" : d.Host.GetType().Name;
                sb.AppendLine($"  Door {d.Id} ({d.Symbol?.Family?.Name}): " +
                              $"FromRoom={from?.Name ?? "<null>"}, ToRoom={to?.Name ?? "<null>"}, Host={hostKind}");
            }
            if (doors.Count > 50) sb.AppendLine($"  + {doors.Count - 50} more (truncated)");
            sb.AppendLine($"Stats: FromRoom set on {doorsWithFromRoom}/{doors.Count}, ToRoom set on {doorsWithToRoom}/{doors.Count}, " +
                          $"hosted-by-Wall {doorsHostedByWall}, hosted-by-other {doorsHostedByOther}");
            sb.AppendLine();

            // 4. Family placement types for switch + light + socket categories.
            sb.AppendLine("FAMILY PLACEMENT TYPES (the engine's biggest blind spot):");
            var watchCats = new[]
            {
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_PlumbingFixtures,
            };
            foreach (var bic in watchCats)
            {
                var symbols = new FilteredElementCollector(doc).OfCategory(bic)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
                sb.AppendLine($"  Category '{bic}' — {symbols.Count} type(s) loaded:");
                foreach (var fs in symbols.Take(20))
                {
                    var fpt = fs.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;
                    sb.AppendLine($"    '{fs.Family?.Name}' :: '{fs.Name}' → {fpt} (active={fs.IsActive})");
                }
                if (symbols.Count > 20) sb.AppendLine($"    + {symbols.Count - 20} more");
            }
            sb.AppendLine();

            // 5. Active rule pack — count rules + show wall-anchored ones whose
            // resolved family is the wrong placement type.
            try
            {
                var rules = PlacementRuleLoader.Load(doc.PathName);
                sb.AppendLine($"RULE PACK: {rules.Count} rule(s) loaded.");
                int wallAnchored = rules.Count(r =>
                {
                    var a = (r.AnchorType ?? "").ToUpperInvariant();
                    return a == "WALL_MIDPOINT" || a == "WALL_CORNER" || a == "WALL_FACE_OFFSET"
                        || a.StartsWith("DOOR_") || a.StartsWith("WINDOW_");
                });
                int ceilingAnchored = rules.Count(r =>
                {
                    var a = (r.AnchorType ?? "").ToUpperInvariant();
                    return a == "CEILING_CENTRE" || a == "CEILING_TILE_CENTRE"
                        || a == "LIGHTING_GRID" || a == "LUX_GRID";
                });
                sb.AppendLine($"  Wall-anchored:    {wallAnchored}");
                sb.AppendLine($"  Ceiling-anchored: {ceilingAnchored}");
            }
            catch (Exception ex) { sb.AppendLine($"Rule pack load: {ex.Message}"); }

            // Seed-variant coverage — how many rules bind to a type variant their
            // mapped seed actually mints (the "each rule matches a family type"
            // alignment). 100% of placeable rules should match; Conduits/Stairs
            // are intentionally seedless (routing, not family instances).
            try
            {
                var crules = PlacementRuleLoader.LoadDefaults();
                int total = 0, covered = 0, noSeed = 0;
                var gaps = new List<string>();
                var seedVarCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in crules)
                {
                    if (r == null || string.IsNullOrEmpty(r.CategoryFilter)) continue;
                    total++;
                    string seedId = CategoryToSeedRegistry.Resolve(doc, r.CategoryFilter);
                    if (string.IsNullOrWhiteSpace(seedId)) { noSeed++; continue; }
                    if (!seedVarCache.TryGetValue(seedId, out var vars))
                    { vars = ReadSeedVariantNames(seedId); seedVarCache[seedId] = vars; }
                    bool ok = false;
                    if (vars != null && vars.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(r.FamilyTypeRegex))
                        {
                            try { var rx = new System.Text.RegularExpressions.Regex(r.FamilyTypeRegex, System.Text.RegularExpressions.RegexOptions.IgnoreCase); ok = vars.Any(v => rx.IsMatch(v)); }
                            catch { }
                        }
                        if (!ok && !string.IsNullOrEmpty(r.VariantHint))
                            ok = r.VariantHint.Split(',').Select(s => s.Trim()).Any(h => h.Length > 0 && vars.Contains(h));
                        if (string.IsNullOrEmpty(r.FamilyTypeRegex) && string.IsNullOrEmpty(r.VariantHint))
                            ok = true; // no discriminator → first symbol of the right category is acceptable
                    }
                    if (ok) covered++; else gaps.Add($"{r.MergeKey} → seed {seedId}");
                }
                int placeable = total - noSeed;
                int pct = placeable > 0 ? (100 * covered / placeable) : 100;
                sb.AppendLine();
                sb.AppendLine("SEED-VARIANT COVERAGE (rule → family type):");
                sb.AppendLine($"  Rules {total} · placeable {placeable} · matched a seed variant {covered} ({pct}%)");
                sb.AppendLine($"  Seedless (Conduits/Stairs/routing): {noSeed}");
                if (gaps.Count == 0)
                    sb.AppendLine("  ✓ Every placeable rule binds to a minted seed variant.");
                else
                {
                    sb.AppendLine($"  Gaps ({gaps.Count}) — VariantHint/FamilyTypeRegex matches no minted variant:");
                    foreach (var g in gaps.Take(15)) sb.AppendLine($"    - {g}");
                    if (gaps.Count > 15) sb.AppendLine($"    + {gaps.Count - 15} more");
                }
            }
            catch (Exception ex) { sb.AppendLine($"Seed coverage: {ex.Message}"); }

            // Write to disk + show in a window the user can copy-paste.
            string outDir = OutputLocationHelper.GetOutputPath(doc, "PlacementDiagnose") ?? Path.GetTempPath();
            Directory.CreateDirectory(outDir);
            string txtPath = Path.Combine(outDir,
                $"STING_PlacementDiagnose_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            try { File.WriteAllText(txtPath, sb.ToString()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            return (sb.ToString(), txtPath);
        }

        /// <summary>Reads the type-variant NAMES from a seed spec (Data/Seeds/&lt;seedId&gt;.json),
        /// whether typeVariants live at the root or under symbols[].</summary>
        private static HashSet<string> ReadSeedVariantNames(string seedId)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = StingToolsApp.FindDataFile(seedId + ".json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return set;
                var root = Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(path));
                void Collect(Newtonsoft.Json.Linq.JToken node)
                {
                    if (node?["typeVariants"] is Newtonsoft.Json.Linq.JArray tv)
                        foreach (var v in tv)
                        { var n = (string)v["name"]; if (!string.IsNullOrEmpty(n)) set.Add(n); }
                }
                Collect(root);
                if (root["symbols"] is Newtonsoft.Json.Linq.JArray syms)
                    foreach (var s in syms) Collect(s);
            }
            catch (Exception ex) { StingLog.Warn($"ReadSeedVariantNames {seedId}: {ex.Message}"); }
            return set;
        }
    }
}
