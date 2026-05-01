// StingTools — Phase 170 revision · Title-block slot automation commands
//
// Two operator-facing commands that pair the new two-family BIM
// architecture with the slot system from STING_TITLE_BLOCKS.json:
//
//   TitleBlock_AutoPlaceViewports — for each selected view (or every
//     unplaced view in the project when nothing is selected), look up
//     the view's purpose tag from STING_VIEWPORT_PLACEMENT_RULES.json,
//     find the slot on the active sheet's title-block .rfa with the
//     matching tag, and create a Viewport at the slot's centre.
//
//   TitleBlock_ToggleBIMMode — read STING_SHEET_BIM_MODE_TXT on the
//     active sheet, swap the title-block family between *_BIM_* and
//     *_NONBIM_*, transfer existing viewports onto the new family
//     (positions transfer 1:1 since slot ids are stable across modes).
//
// Phase 170 ships these as REGISTERED command classes with the full
// scaffolding (data loading, sheet inspection, slot resolution) but
// the actual viewport-placement and family-swap logic is stubbed
// behind a "Phase 171" TaskDialog so the buttons are wired and
// reviewable end-to-end. The Phase 171 implementation drops in.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockAutoPlaceViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var uiApp = data?.Application ?? StingCommandHandler.CurrentApp;
            if (uiApp?.ActiveUIDocument == null)
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    "No active document.");
                return Result.Failed;
            }
            var doc = uiApp.ActiveUIDocument.Document;

            // 1. Pre-flight — must be standing on a sheet.
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    "Open the target sheet view first. The auto-placer reads slots from the sheet's title-block family.");
                return Result.Cancelled;
            }

            // 2. Load placement rules
            var rules = ViewportPlacementRules.Load();
            if (rules == null)
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    "STING_VIEWPORT_PLACEMENT_RULES.json not found in data/. "
                    + "Reinstall the addin or restore the data folder.");
                return Result.Failed;
            }

            // 3. Resolve the sheet's title-block family + its slots.
            var titleBlockFamilyName = ResolveTitleBlockFamilyName(doc, sheet);
            if (string.IsNullOrEmpty(titleBlockFamilyName))
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    "Active sheet has no title block placed. Add a STING title block first.");
                return Result.Failed;
            }
            var slotIds = ReadSlotIdsFromTitleBlock(doc, titleBlockFamilyName);

            // 4. Survey selected views.
            var selectedIds = uiApp.ActiveUIDocument.Selection.GetElementIds();
            var candidateViews = new List<View>();
            foreach (var id in selectedIds)
            {
                if (doc.GetElement(id) is View v
                    && v.ViewType != ViewType.DrawingSheet
                    && !v.IsTemplate)
                    candidateViews.Add(v);
            }

            // 5. Phase 171 stub — actual Viewport.Create loop lives here.
            var sb = new StringBuilder();
            sb.AppendLine("STING — Auto-place Viewports (Phase 171 implementation pending)");
            sb.AppendLine();
            sb.AppendLine($"Active sheet     : {sheet.SheetNumber} — {sheet.Name}");
            sb.AppendLine($"Title block      : {titleBlockFamilyName}");
            sb.AppendLine($"Slots discovered : {slotIds.Count}  ({string.Join(", ", slotIds.Take(8))}{(slotIds.Count > 8 ? ", …" : "")})");
            sb.AppendLine($"Selected views   : {candidateViews.Count}");
            sb.AppendLine($"Routing rules    : {rules.Rules?.Count ?? 0}");
            sb.AppendLine();
            sb.AppendLine("Routing preview (first 10):");
            foreach (var v in candidateViews.Take(10))
            {
                var tag = ResolvePurposeTag(rules, v);
                sb.AppendLine($"  {v.ViewType,-12} '{v.Name}' → tag '{tag}'");
            }
            if (candidateViews.Count > 10)
                sb.AppendLine($"  … +{candidateViews.Count - 10} more");
            sb.AppendLine();
            sb.AppendLine("Phase 171 will run the Viewport.Create loop with discipline-aware "
                       + "slot routing and per-slot viewport-type / scale application.");

            TaskDialog.Show("STING — Auto-place Viewports", sb.ToString());
            return Result.Succeeded;
        }

        internal static string ResolvePurposeTag(ViewportPlacementRules rules, View v)
        {
            if (rules?.Rules == null) return rules?.DefaultPurposeTag ?? "main-plan";
            string vtName = v.ViewType.ToString();
            string vName  = v.Name ?? "";
            foreach (var rule in rules.Rules)
            {
                if (!string.Equals(rule.ViewType, vtName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!GlobMatch(rule.NamePattern, vName)) continue;
                return rule.PurposeTag;
            }
            return rules.DefaultPurposeTag ?? "main-plan";
        }

        private static bool GlobMatch(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            // Convert glob (* / ?) → regex.
            var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? "", rx, RegexOptions.IgnoreCase);
        }

        private static string ResolveTitleBlockFamilyName(Document doc, ViewSheet sheet)
        {
            try
            {
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();
                if (tb == null) return null;
                var sym = doc.GetElement(tb.GetTypeId()) as FamilySymbol;
                return sym?.Family?.Name;
            }
            catch { return null; }
        }

        /// <summary>Cheap heuristic: read slot ids back from the
        /// title-block family without opening the .rfa. The factory
        /// places a TextNote slot-id marker per slot; we walk the
        /// title-block instance's family doc reference planes and
        /// derive ids from their names (`<id>_TOP/BOT/LFT/RGT`).</summary>
        private static List<string> ReadSlotIdsFromTitleBlock(Document doc, string familyName)
        {
            var slotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Find the loaded Family by name.
                var family = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .OfType<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
                if (family == null) return new List<string>(slotIds);
                // Open an in-memory document of the family.
                var famDoc = doc.EditFamily(family);
                if (famDoc == null) return new List<string>(slotIds);
                try
                {
                    foreach (var rp in new FilteredElementCollector(famDoc)
                        .OfClass(typeof(ReferencePlane))
                        .OfType<ReferencePlane>())
                    {
                        var name = rp.Name ?? "";
                        var i = name.LastIndexOf('_');
                        if (i <= 0) continue;
                        var suffix = name.Substring(i + 1);
                        if (suffix == "TOP" || suffix == "BOT" || suffix == "LFT" || suffix == "RGT")
                        {
                            slotIds.Add(name.Substring(0, i));
                        }
                    }
                }
                finally
                {
                    try { famDoc.Close(false); } catch { }
                }
            }
            catch { /* swallow — degrade gracefully */ }
            return slotIds.ToList();
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockToggleBIMModeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var uiApp = data?.Application ?? StingCommandHandler.CurrentApp;
            if (uiApp?.ActiveUIDocument == null)
            {
                TaskDialog.Show("STING — Toggle BIM Mode", "No active document.");
                return Result.Failed;
            }
            var doc = uiApp.ActiveUIDocument.Document;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("STING — Toggle BIM Mode",
                    "Open the target sheet first.");
                return Result.Cancelled;
            }

            // Find the current title-block instance + read its BIM mode.
            var tb = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();
            if (tb == null)
            {
                TaskDialog.Show("STING — Toggle BIM Mode",
                    "Active sheet has no title block.");
                return Result.Cancelled;
            }
            var sym = doc.GetElement(tb.GetTypeId()) as FamilySymbol;
            var currentName = sym?.Family?.Name ?? "";
            var bimModeParam = tb.LookupParameter("STING_SHEET_BIM_MODE_TXT");
            var currentMode = bimModeParam?.AsString() ?? GuessModeFromName(currentName);
            string targetMode = string.Equals(currentMode, "BIM", StringComparison.OrdinalIgnoreCase)
                ? "NONBIM" : "BIM";
            string targetFamilyName = currentName
                .Replace("_BIM_",   "_<TMP>_")
                .Replace("_NONBIM_", "_BIM_")
                .Replace("_<TMP>_", "_NONBIM_");
            // The replacement above flips BIM↔NONBIM regardless of which side is current.
            if (targetMode == "BIM" && currentName.IndexOf("_BIM_", StringComparison.OrdinalIgnoreCase) < 0
                && currentName.IndexOf("_NONBIM_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                targetFamilyName = currentName.Replace("_NONBIM_", "_BIM_");
            }

            // Phase 171 stub — emit the planned swap so the operator can
            // sanity-check the routing before we wire the actual swap.
            var sb = new StringBuilder();
            sb.AppendLine("STING — Toggle BIM Mode (Phase 171 implementation pending)");
            sb.AppendLine();
            sb.AppendLine($"Active sheet      : {sheet.SheetNumber} — {sheet.Name}");
            sb.AppendLine($"Current title-block family : {currentName}");
            sb.AppendLine($"Current BIM mode  : {currentMode}");
            sb.AppendLine($"Target  BIM mode  : {targetMode}");
            sb.AppendLine($"Target family name (planned): {targetFamilyName}");
            sb.AppendLine();
            sb.AppendLine("Phase 171 will:");
            sb.AppendLine("  1. Verify the target family is loaded (load it if missing).");
            sb.AppendLine("  2. Swap the title-block instance's FamilySymbol to the target.");
            sb.AppendLine("  3. Update STING_SHEET_BIM_MODE_TXT on the sheet.");
            sb.AppendLine("  4. Re-read viewports' positions and re-anchor against the new");
            sb.AppendLine("     slot bounds (positions transfer 1:1 since slot ids are stable).");

            TaskDialog.Show("STING — Toggle BIM Mode", sb.ToString());
            return Result.Succeeded;
        }

        private static string GuessModeFromName(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return "(unknown)";
            if (familyName.IndexOf("_BIM_",    StringComparison.OrdinalIgnoreCase) >= 0) return "BIM";
            if (familyName.IndexOf("_NONBIM_", StringComparison.OrdinalIgnoreCase) >= 0) return "NONBIM";
            return "(unknown)";
        }
    }

    /// <summary>POCO mirror of STING_VIEWPORT_PLACEMENT_RULES.json. The
    /// auto-placer walks Rules in declared order; first match wins.</summary>
    public sealed class ViewportPlacementRules
    {
        [JsonProperty("schemaVersion")]      public int    SchemaVersion { get; set; } = 1;
        [JsonProperty("name")]               public string Name { get; set; }
        [JsonProperty("description")]        public string Description { get; set; }
        [JsonProperty("defaultPurposeTag")]  public string DefaultPurposeTag { get; set; } = "main-plan";
        [JsonProperty("noMatchBehaviour")]   public string NoMatchBehaviour { get; set; } = "place-at-default-with-warning";
        [JsonProperty("rules")]              public List<ViewportPlacementRule> Rules { get; set; }
            = new List<ViewportPlacementRule>();

        /// <summary>For slot-routing: when an exact purposeTag isn't in
        /// the title block, try its aliases (e.g. main-plan-half-left
        /// → main-plan).</summary>
        [JsonProperty("purposeTagAliases")]  public Dictionary<string, List<string>> PurposeTagAliases { get; set; }
            = new Dictionary<string, List<string>>();

        public static ViewportPlacementRules Load()
        {
            try
            {
                var path = StingToolsApp.FindDataFile("STING_VIEWPORT_PLACEMENT_RULES.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn("ViewportPlacementRules: STING_VIEWPORT_PLACEMENT_RULES.json not found");
                    return null;
                }
                return JsonConvert.DeserializeObject<ViewportPlacementRules>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Error("ViewportPlacementRules.Load", ex);
                return null;
            }
        }
    }

    public sealed class ViewportPlacementRule
    {
        [JsonProperty("viewType")]    public string ViewType { get; set; }     // "FloorPlan" / "Section" / "ThreeD" / "Legend" / "Schedule" / …
        [JsonProperty("namePattern")] public string NamePattern { get; set; } = "*";
        [JsonProperty("purposeTag")]  public string PurposeTag { get; set; }
    }
}
