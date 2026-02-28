using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Tags
{
    // ════════════════════════════════════════════════════════════════════
    //  Set Presentation Mode — one-click paragraph depth + warning
    //  visibility switching for corporate-standard tag presentation
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Switches all element types in the project between presentation modes.
    /// Each mode sets TAG_PARA_STATE_1/2/3_BOOL and TAG_WARN_VISIBLE_BOOL
    /// to produce the appropriate tag display depth.
    ///
    /// Modes (defined in LABEL_DEFINITIONS.json):
    ///   Compact          — State 1 only, no warnings (quick labels)
    ///   Technical        — States 1+2, warnings ON (engineering docs)
    ///   Full Specification — States 1+2+3, warnings ON (detail sheets)
    ///   Presentation     — States 1+2, warnings OFF (client presentation)
    ///   BOQ              — States 1+2, warnings OFF (cost schedules)
    ///
    /// The visibility states are Type parameters — they control calculated
    /// value visibility in Revit tag family labels (Edit Label).
    /// Switching mode affects ALL instances of each type simultaneously.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetPresentationModeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Load presentation modes from LABEL_DEFINITIONS.json
            var modes = LabelDefinitionHelper.LoadPresentationModes();

            TaskDialog td = new TaskDialog("Presentation Mode");
            td.MainInstruction = "Select tag presentation mode";
            td.MainContent =
                "Controls paragraph depth and warning visibility across all element types.\n" +
                "These are Type parameters — all instances of the same type change together.\n\n" +
                "Each mode sets TAG_PARA_STATE_1/2/3_BOOL and TAG_WARN_VISIBLE_BOOL\n" +
                "to control calculated value visibility in tag family labels.";

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Compact",
                "State 1 only — tag code + type name (quick labels)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Technical",
                "States 1+2 + warnings — key properties + threshold alerts (engineering)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Full Specification",
                "States 1+2+3 + warnings — complete paragraph with all tiers (detail sheets)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Presentation",
                "States 1+2 no warnings — clean tags for client presentations");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult result = td.Show();

            bool s1, s2, s3, warn;
            string modeName;
            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    s1 = true; s2 = false; s3 = false; warn = false;
                    modeName = "Compact"; break;
                case TaskDialogResult.CommandLink2:
                    s1 = true; s2 = true; s3 = false; warn = true;
                    modeName = "Technical"; break;
                case TaskDialogResult.CommandLink3:
                    s1 = true; s2 = true; s3 = true; warn = true;
                    modeName = "Full Specification"; break;
                case TaskDialogResult.CommandLink4:
                    s1 = true; s2 = true; s3 = false; warn = false;
                    modeName = "Presentation"; break;
                default:
                    return Result.Cancelled;
            }

            // Apply to all element types
            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToList();

            int updated = 0;
            using (Transaction tx = new Transaction(doc, $"STING Set Mode: {modeName}"))
            {
                tx.Start();

                foreach (Element typeEl in allTypes)
                {
                    bool any = false;
                    any |= SetBool(typeEl, ParamRegistry.PARA_STATE_1, s1);
                    any |= SetBool(typeEl, ParamRegistry.PARA_STATE_2, s2);
                    any |= SetBool(typeEl, ParamRegistry.PARA_STATE_3, s3);
                    any |= SetBool(typeEl, ParamRegistry.WARN_VISIBLE, warn);
                    if (any) updated++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Presentation Mode",
                $"Mode: {modeName}\n" +
                $"State 1: {(s1 ? "ON" : "OFF")} | State 2: {(s2 ? "ON" : "OFF")} | " +
                $"State 3: {(s3 ? "ON" : "OFF")} | Warnings: {(warn ? "ON" : "OFF")}\n\n" +
                $"Element types updated: {updated}\n\n" +
                "Tag families with calculated values gated by these parameters\n" +
                "will now show/hide their tier content accordingly.");

            StingLog.Info($"Presentation mode set to {modeName}, {updated} types updated");
            return Result.Succeeded;
        }

        private static bool SetBool(Element el, string paramName, bool value)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;
            if (p.StorageType == StorageType.Integer)
            {
                p.Set(value ? 1 : 0);
                return true;
            }
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  View Label Spec — show Edit Label configuration guide for any
    //  category, so user knows exactly what to put in Edit Label dialog
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shows the complete Edit Label configuration guide for a selected category.
    /// Reads LABEL_DEFINITIONS.json and formats the parameter layout with
    /// prefix, suffix, spaces, and break settings exactly as they should appear
    /// in Revit's Edit Label dialog when configuring seed tag families.
    ///
    /// This is the bridge between the data file and the Family Editor —
    /// it tells the user exactly which parameters to add and what
    /// static "scheduled text words" to put in each column.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewLabelSpecCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            // Load label definitions
            var catLabels = LabelDefinitionHelper.LoadCategoryLabels();
            var paramText = LabelDefinitionHelper.LoadParameterText();

            if (catLabels.Count == 0)
            {
                TaskDialog.Show("Label Spec",
                    "Cannot load LABEL_DEFINITIONS.json.\n" +
                    "Ensure it exists in the Data/ directory.");
                return Result.Failed;
            }

            // Let user pick a category
            TaskDialog picker = new TaskDialog("View Label Specification");
            picker.MainInstruction = "Select a category to view its Edit Label guide";
            picker.MainContent =
                $"{catLabels.Count} categories defined in LABEL_DEFINITIONS.json.\n\n" +
                "Choose a discipline group:";

            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Architecture",
                "Walls, Floors, Ceilings, Roofs, Doors, Windows, Stairs, Ramps");
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "MEP — Mechanical / HVAC",
                "Mechanical Equipment, Ducts, Air Terminals, Flex Ducts");
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "MEP — Electrical / Plumbing / Fire",
                "Electrical Equipment, Lighting, Conduits, Pipes, Sprinklers");
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "All Categories (Export to File)",
                "Export complete label guide for all categories to a text file");
            picker.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult pickResult = picker.Show();
            if (pickResult == TaskDialogResult.Cancel)
                return Result.Cancelled;

            if (pickResult == TaskDialogResult.CommandLink4)
            {
                // Export all to file
                return ExportAllLabelSpecs(catLabels, paramText);
            }

            // Filter by discipline
            string[] filterCats;
            switch (pickResult)
            {
                case TaskDialogResult.CommandLink1:
                    filterCats = new[] { "Walls", "Floors", "Ceilings", "Roofs", "Doors",
                        "Windows", "Stairs", "Ramps", "Rooms", "Furniture", "Casework" };
                    break;
                case TaskDialogResult.CommandLink2:
                    filterCats = new[] { "Mechanical Equipment", "Ducts", "Duct Fittings",
                        "Duct Accessories", "Air Terminals", "Flex Ducts" };
                    break;
                default:
                    filterCats = new[] { "Electrical Equipment", "Electrical Fixtures",
                        "Lighting Fixtures", "Lighting Devices", "Conduits", "Conduit Fittings",
                        "Cable Trays", "Pipes", "Pipe Fittings", "Pipe Accessories",
                        "Plumbing Fixtures", "Sprinklers", "Fire Alarm Devices",
                        "Communication Devices", "Data Devices", "Security Devices",
                        "Nurse Call Devices" };
                    break;
            }

            // Build label spec report
            var sb = new StringBuilder();
            foreach (string cat in filterCats)
            {
                if (!catLabels.ContainsKey(cat)) continue;
                FormatCategorySpec(sb, cat, catLabels[cat], paramText);
            }

            TaskDialog report = new TaskDialog("Edit Label Guide");
            report.MainInstruction = "Edit Label Configuration Guide";
            report.MainContent = sb.Length > 2000
                ? sb.ToString().Substring(0, 2000) + "\n\n[...truncated — use Export for full guide]"
                : sb.ToString();
            report.Show();

            return Result.Succeeded;
        }

        private void FormatCategorySpec(StringBuilder sb,
            string catName, JObject catDef,
            Dictionary<string, JObject> paramText)
        {
            string famName = catDef["family_name"]?.ToString() ?? catName;
            string paraCont = catDef["paragraph_container"]?.ToString() ?? "none";

            sb.AppendLine($"═══ {catName} ═══");
            sb.AppendLine($"  Family: {famName}");
            sb.AppendLine($"  Paragraph: {paraCont}");
            sb.AppendLine();

            // Tier 1
            sb.AppendLine("  ── TIER 1 (Always Visible) ──");
            FormatTierParams(sb, catDef["tier_1"] as JArray, paramText);

            // Tier 2
            sb.AppendLine("  ── TIER 2 (Gated by TAG_PARA_STATE_2_BOOL) ──");
            sb.AppendLine("  Calculated value: if(TAG_PARA_STATE_2_BOOL, <param>, \"\")");
            FormatTierParams(sb, catDef["tier_2"] as JArray, paramText);

            // Tier 3
            sb.AppendLine("  ── TIER 3 (Gated by TAG_PARA_STATE_3_BOOL) ──");
            sb.AppendLine("  Calculated value: if(TAG_PARA_STATE_3_BOOL, <param>, \"\")");
            FormatTierParams(sb, catDef["tier_3"] as JArray, paramText);

            sb.AppendLine();
        }

        private void FormatTierParams(StringBuilder sb, JArray tierParams,
            Dictionary<string, JObject> paramText)
        {
            if (tierParams == null || tierParams.Count == 0)
            {
                sb.AppendLine("  (none)");
                return;
            }

            sb.AppendLine("  Parameter            | Prefix      | Suffix      | Spc | Brk");
            sb.AppendLine("  " + new string('-', 70));

            foreach (JObject entry in tierParams)
            {
                string param = entry["param"]?.ToString() ?? "";
                int spaces = entry["spaces"]?.Value<int>() ?? 0;
                bool brk = entry["break"]?.Value<bool>() ?? false;

                // Get prefix/suffix: override from tier entry, else from global paramText
                string prefix = entry["prefix_override"]?.ToString();
                string suffix = entry["suffix_override"]?.ToString();

                if (prefix == null && paramText.TryGetValue(param, out var ptDef))
                    prefix = ptDef["prefix"]?.ToString() ?? "";
                if (suffix == null && paramText.TryGetValue(param, out var ptDef2))
                    suffix = ptDef2["suffix"]?.ToString() ?? "";

                prefix = prefix ?? "";
                suffix = suffix ?? "";

                string paramShort = param.Length > 20 ? param.Substring(0, 17) + "..." : param;
                sb.AppendLine($"  {paramShort,-20} | \"{prefix}\"" +
                    $"{new string(' ', Math.Max(0, 10 - prefix.Length - 2))}| \"{suffix}\"" +
                    $"{new string(' ', Math.Max(0, 10 - suffix.Length - 2))}| {spaces}   | {(brk ? "YES" : "")}");
            }
        }

        private Result ExportAllLabelSpecs(
            Dictionary<string, JObject> catLabels,
            Dictionary<string, JObject> paramText)
        {
            var sb = new StringBuilder();
            sb.AppendLine("STING TAG FAMILY — EDIT LABEL CONFIGURATION GUIDE");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();
            sb.AppendLine("HOW TO USE THIS GUIDE:");
            sb.AppendLine("  1. Open each STING tag family in the Family Editor");
            sb.AppendLine("  2. Select the Label text element");
            sb.AppendLine("  3. Click 'Edit Label' in Properties");
            sb.AppendLine("  4. Add parameters listed below for each tier");
            sb.AppendLine("  5. Set Prefix, Suffix, Spaces, Break as specified");
            sb.AppendLine("  6. For Tier 2/3: use Calculated Values (fv button) with");
            sb.AppendLine("     visibility gate formulas shown below each tier");
            sb.AppendLine("  7. Set 'Wrap between parameters only' = checked");
            sb.AppendLine();
            sb.AppendLine("VISIBILITY CONTROL:");
            sb.AppendLine("  TAG_PARA_STATE_1_BOOL = Compact (always ON)");
            sb.AppendLine("  TAG_PARA_STATE_2_BOOL = Standard (ON for Technical/Presentation/BOQ)");
            sb.AppendLine("  TAG_PARA_STATE_3_BOOL = Comprehensive (ON for Full Specification)");
            sb.AppendLine("  TAG_WARN_VISIBLE_BOOL = Warnings (ON for Technical/Full Specification)");
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            foreach (var kvp in catLabels.OrderBy(k => k.Key))
            {
                FormatCategorySpec(sb, kvp.Key, kvp.Value, paramText);
            }

            // Write to file
            string outputDir = StingToolsApp.DataPath ?? Path.GetDirectoryName(StingToolsApp.AssemblyPath) ?? "";
            string filePath = Path.Combine(outputDir, "LABEL_CONFIGURATION_GUIDE.txt");

            try
            {
                File.WriteAllText(filePath, sb.ToString());
                TaskDialog.Show("Export Label Guide",
                    $"Label configuration guide exported to:\n{filePath}\n\n" +
                    $"Categories: {catLabels.Count}\n" +
                    $"File size: {sb.Length:N0} characters");
                StingLog.Info($"Label guide exported to {filePath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Export Error", $"Cannot write file:\n{ex.Message}");
                StingLog.Error("Label guide export failed", ex);
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Export Label Guide — direct export of all label specs to a file
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exports the complete Edit Label configuration guide as a text file.
    /// Covers all categories with prefix/suffix/break specifications and
    /// calculated value templates for visibility gating.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportLabelGuideCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var catLabels = LabelDefinitionHelper.LoadCategoryLabels();
            var paramText = LabelDefinitionHelper.LoadParameterText();

            if (catLabels.Count == 0)
            {
                TaskDialog.Show("Export Label Guide",
                    "Cannot load LABEL_DEFINITIONS.json.");
                return Result.Failed;
            }

            var sb = new StringBuilder();
            sb.AppendLine("STING TAG FAMILY — EDIT LABEL CONFIGURATION GUIDE");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Categories: {catLabels.Count}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            sb.AppendLine("PRESENTATION MODES (use Set Presentation Mode command):");
            sb.AppendLine("  Compact          — State 1 only, no warnings");
            sb.AppendLine("  Technical        — States 1+2, warnings ON");
            sb.AppendLine("  Full Specification — States 1+2+3, warnings ON");
            sb.AppendLine("  Presentation     — States 1+2, warnings OFF");
            sb.AppendLine("  BOQ              — States 1+2, warnings OFF");
            sb.AppendLine();

            sb.AppendLine("CALCULATED VALUE TEMPLATES (fv button in Edit Label):");
            sb.AppendLine("  Tier 2 gate: if(TAG_PARA_STATE_2_BOOL, <actual_param>, \"\")");
            sb.AppendLine("  Tier 3 gate: if(TAG_PARA_STATE_3_BOOL, <actual_param>, \"\")");
            sb.AppendLine("  Warning gate: if(TAG_WARN_VISIBLE_BOOL, <warn_param>, \"\")");
            sb.AppendLine();
            sb.AppendLine("PARAMETER TEXT MAP (scheduled text words):");
            sb.AppendLine(new string('-', 80));

            foreach (var kvp in paramText.OrderBy(k => k.Key))
            {
                string pfx = kvp.Value["prefix"]?.ToString() ?? "";
                string sfx = kvp.Value["suffix"]?.ToString() ?? "";
                string lbl = kvp.Value["label"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(pfx) || !string.IsNullOrEmpty(sfx))
                {
                    sb.AppendLine($"  {kvp.Key,-45} Prefix: \"{pfx}\"  Suffix: \"{sfx}\"  ({lbl})");
                }
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            foreach (var kvp in catLabels.OrderBy(k => k.Key))
            {
                FormatCategory(sb, kvp.Key, kvp.Value, paramText);
            }

            // Write to file
            string outputDir = StingToolsApp.DataPath ?? Path.GetDirectoryName(StingToolsApp.AssemblyPath) ?? "";
            string filePath = Path.Combine(outputDir, "LABEL_CONFIGURATION_GUIDE.txt");

            try
            {
                File.WriteAllText(filePath, sb.ToString());
                TaskDialog.Show("Export Label Guide",
                    $"Complete label configuration guide exported.\n\n" +
                    $"File: {filePath}\n" +
                    $"Categories: {catLabels.Count}\n" +
                    $"Size: {sb.Length:N0} characters\n\n" +
                    "Use this guide when configuring Edit Label in each\n" +
                    "STING seed tag family in the Family Editor.");

                StingLog.Info($"Label guide exported: {filePath}, {catLabels.Count} categories");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Export Error", $"Cannot write file:\n{ex.Message}");
                StingLog.Error("Label guide export failed", ex);
                return Result.Failed;
            }
        }

        private void FormatCategory(StringBuilder sb, string catName,
            JObject catDef, Dictionary<string, JObject> paramText)
        {
            string famName = catDef["family_name"]?.ToString() ?? catName;
            string para = catDef["paragraph_container"]?.ToString() ?? "—";
            var warnings = catDef["warnings"] as JArray;

            sb.AppendLine($"╔══ {catName.ToUpper()} ═══════════════════════════════════════════");
            sb.AppendLine($"║ Family:    {famName}");
            sb.AppendLine($"║ Paragraph: {para}");
            if (warnings != null && warnings.Count > 0)
                sb.AppendLine($"║ Warnings:  {string.Join(", ", warnings.Select(w => w.ToString()))}");
            sb.AppendLine("╚" + new string('═', 60));
            sb.AppendLine();

            FormatTier(sb, "TIER 1 — Always Visible (Compact+)", catDef["tier_1"] as JArray, paramText, null);
            FormatTier(sb, "TIER 2 — Standard+ (gate: TAG_PARA_STATE_2_BOOL)", catDef["tier_2"] as JArray, paramText,
                "if(TAG_PARA_STATE_2_BOOL, <param>, \"\")");
            FormatTier(sb, "TIER 3 — Comprehensive (gate: TAG_PARA_STATE_3_BOOL)", catDef["tier_3"] as JArray, paramText,
                "if(TAG_PARA_STATE_3_BOOL, <param>, \"\")");

            sb.AppendLine();
        }

        private void FormatTier(StringBuilder sb, string tierTitle, JArray tierParams,
            Dictionary<string, JObject> paramText, string gateFormula)
        {
            sb.AppendLine($"  {tierTitle}");

            if (gateFormula != null)
                sb.AppendLine($"  Calculated value formula: {gateFormula}");

            if (tierParams == null || tierParams.Count == 0)
            {
                sb.AppendLine("  (no parameters)");
                sb.AppendLine();
                return;
            }

            sb.AppendLine($"  {"Parameter",-40} {"Prefix",-15} {"Suffix",-12} {"Spc",3} {"Brk",4}");
            sb.AppendLine("  " + new string('─', 75));

            foreach (JObject entry in tierParams)
            {
                string param = entry["param"]?.ToString() ?? "";
                int spaces = entry["spaces"]?.Value<int>() ?? 0;
                bool brk = entry["break"]?.Value<bool>() ?? false;

                string prefix = entry["prefix_override"]?.ToString();
                string suffix = entry["suffix_override"]?.ToString();

                if (prefix == null && paramText.TryGetValue(param, out var ptDef))
                    prefix = ptDef["prefix"]?.ToString() ?? "";
                if (suffix == null && paramText.TryGetValue(param, out var ptDef2))
                    suffix = ptDef2["suffix"]?.ToString() ?? "";

                prefix = prefix ?? "";
                suffix = suffix ?? "";

                sb.AppendLine($"  {param,-40} \"{prefix}\"{new string(' ', Math.Max(0, 12 - prefix.Length))} " +
                    $"\"{suffix}\"{new string(' ', Math.Max(0, 9 - suffix.Length))} {spaces,3} {(brk ? "YES" : ""),4}");
            }

            sb.AppendLine();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Label Definition Helper — loads LABEL_DEFINITIONS.json
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads and parses LABEL_DEFINITIONS.json for use by presentation mode,
    /// label spec viewer, and seed family commands.
    /// </summary>
    internal static class LabelDefinitionHelper
    {
        private static JObject _cached;
        private static string _cachedPath;

        /// <summary>Load the full JSON document, with simple caching.</summary>
        public static JObject LoadDocument()
        {
            string path = StingToolsApp.FindDataFile("LABEL_DEFINITIONS.json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            if (_cached != null && _cachedPath == path)
                return _cached;

            try
            {
                string json = File.ReadAllText(path);
                _cached = JObject.Parse(json);
                _cachedPath = path;
                return _cached;
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to load LABEL_DEFINITIONS.json", ex);
                return null;
            }
        }

        /// <summary>Load presentation mode definitions.</summary>
        public static Dictionary<string, JObject> LoadPresentationModes()
        {
            var result = new Dictionary<string, JObject>();
            var doc = LoadDocument();
            if (doc == null) return result;

            var modes = doc["presentation_modes"] as JObject;
            if (modes == null) return result;

            foreach (var prop in modes.Properties())
            {
                result[prop.Name] = prop.Value as JObject;
            }
            return result;
        }

        /// <summary>Load category label definitions.</summary>
        public static Dictionary<string, JObject> LoadCategoryLabels()
        {
            var result = new Dictionary<string, JObject>();
            var doc = LoadDocument();
            if (doc == null) return result;

            var cats = doc["category_labels"] as JObject;
            if (cats == null) return result;

            foreach (var prop in cats.Properties())
            {
                if (prop.Name.StartsWith("_")) continue; // skip comments
                result[prop.Name] = prop.Value as JObject;
            }
            return result;
        }

        /// <summary>Load parameter text definitions (prefix/suffix/label).</summary>
        public static Dictionary<string, JObject> LoadParameterText()
        {
            var result = new Dictionary<string, JObject>();
            var doc = LoadDocument();
            if (doc == null) return result;

            var pt = doc["parameter_text"] as JObject;
            if (pt == null) return result;

            foreach (var prop in pt.Properties())
            {
                if (prop.Name.StartsWith("_")) continue;
                result[prop.Name] = prop.Value as JObject;
            }
            return result;
        }

        /// <summary>
        /// Get the list of all shared parameter names needed for a category's
        /// tag family label (across all tiers + paragraph + warnings).
        /// Used by enhanced seed family creation to add the right params.
        /// </summary>
        public static List<string> GetCategoryParams(string categoryName)
        {
            var result = new List<string>();
            var doc = LoadDocument();
            if (doc == null) return result;

            var cats = doc["category_labels"] as JObject;
            if (cats == null || cats[categoryName] == null) return result;

            var catDef = cats[categoryName] as JObject;

            // Visibility control params (always added)
            result.Add("TAG_PARA_STATE_1_BOOL");
            result.Add("TAG_PARA_STATE_2_BOOL");
            result.Add("TAG_PARA_STATE_3_BOOL");
            result.Add("TAG_WARN_VISIBLE_BOOL");

            // Collect params from all tiers
            foreach (string tierKey in new[] { "tier_1", "tier_2", "tier_3" })
            {
                var tier = catDef[tierKey] as JArray;
                if (tier == null) continue;
                foreach (JObject entry in tier)
                {
                    string param = entry["param"]?.ToString();
                    if (!string.IsNullOrEmpty(param) && !result.Contains(param))
                        result.Add(param);
                }
            }

            // Paragraph container
            string paraCont = catDef["paragraph_container"]?.ToString();
            if (!string.IsNullOrEmpty(paraCont) && paraCont != "null" && !result.Contains(paraCont))
                result.Add(paraCont);

            return result;
        }
    }
}
