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
using StingTools.UI;

namespace StingTools.Tags
{
    /// <summary>
    /// Applies the active paragraph preset from PARAGRAPH_PRESETS.json to element
    /// instances in scope. For each tier T4..T10 the command composes rows as
    /// "{prefix} {param_value} {suffix}" joined with spaces (or newline when Brk),
    /// concatenates tiers with " | ", and writes the result to ASS_TAG_7_TXT.
    ///
    /// Rows whose parameter is missing or empty on a given element are skipped so
    /// a Handover preset still works on a model that has not yet been populated
    /// with commissioning data.
    ///
    /// Preset selection priority:
    ///   1. extra-param "ParagraphPreset" (set by Paragraph Builder dialog)
    ///   2. project_config.json key "HANDOVER_MODE" (set by mode toggle)
    ///   3. JSON "active_preset"
    ///   4. "Handover" fallback
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyParagraphPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            ParagraphPresets presets;
            try { presets = ParagraphPresets.Load(); }
            catch (Exception ex)
            {
                StingLog.Error("ApplyParagraphPreset: cannot load PARAGRAPH_PRESETS.json", ex);
                TaskDialog.Show("STING", $"Failed to load PARAGRAPH_PRESETS.json:\n{ex.Message}");
                return Result.Failed;
            }

            string requested = StingCommandHandler.GetExtraParam("ParagraphPreset");
            if (string.IsNullOrEmpty(requested)) requested = ReadConfigMode(doc);
            if (string.IsNullOrEmpty(requested)) requested = presets.ActivePreset;
            if (string.IsNullOrEmpty(requested) || !presets.Entries.ContainsKey(requested))
                requested = "Handover";

            ParagraphPreset preset = presets.Entries[requested];

            var scope = CollectScope(ctx.UIDoc);
            if (scope.Count == 0)
            {
                TaskDialog.Show("Apply Paragraph Preset",
                    "No elements in scope.\nSelect elements, open a view, or run on the project.");
                return Result.Cancelled;
            }

            int updated = 0, skippedReadOnly = 0;
            using (Transaction tx = new Transaction(doc, $"STING Apply Paragraph Preset · {requested}"))
            {
                tx.Start();
                foreach (ElementId id in scope)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    string composed = ComposeParagraph(el, preset);
                    if (string.IsNullOrEmpty(composed)) continue;

                    Parameter p = el.LookupParameter(ParamRegistry.TAG7);
                    if (p == null) continue;
                    if (p.IsReadOnly) { skippedReadOnly++; continue; }
                    if (p.StorageType != StorageType.String) continue;
                    string cur = p.AsString() ?? "";
                    if (cur == composed) continue;
                    p.Set(composed);
                    updated++;
                }
                tx.Commit();
            }

            StingLog.Info($"ApplyParagraphPreset[{requested}]: updated={updated} readonly={skippedReadOnly} scope={scope.Count}");
            if (string.IsNullOrEmpty(StingCommandHandler.GetExtraParam("SuppressDialog")))
            {
                TaskDialog.Show("Paragraph Preset Applied",
                    $"Preset: {preset.DisplayName}\n" +
                    $"Scope:  {scope.Count} elements\n" +
                    $"Updated: {updated}\n" +
                    (skippedReadOnly > 0 ? $"Skipped (read-only): {skippedReadOnly}" : ""));
            }
            return Result.Succeeded;
        }

        // ------------------------------------------------------------------
        // Paragraph composition — "{prefix} {value} {suffix}" per row,
        // joined by " " (or newline when Brk), tiers joined by " | ".
        // ------------------------------------------------------------------
        private static string ComposeParagraph(Element el, ParagraphPreset preset)
        {
            var sbAll = new StringBuilder();
            string[] tierOrder = { "T4", "T5", "T6", "T7", "T8", "T9", "T10" };
            bool firstTier = true;
            foreach (string t in tierOrder)
            {
                if (!preset.Tiers.TryGetValue(t, out var tier) || tier.Rows.Count == 0) continue;
                var sbTier = new StringBuilder();
                foreach (var row in tier.Rows)
                {
                    if (!row.Enabled) continue;
                    string val = ReadParamAsText(el, row.Parameter);
                    if (string.IsNullOrEmpty(val)) continue;
                    if (sbTier.Length > 0) sbTier.Append(row.Brk ? "\n" : " ");
                    if (!string.IsNullOrEmpty(row.Prefix)) { sbTier.Append(row.Prefix); sbTier.Append(' '); }
                    sbTier.Append(val);
                    if (!string.IsNullOrEmpty(row.Suffix)) { sbTier.Append(' '); sbTier.Append(row.Suffix); }
                }
                if (sbTier.Length == 0) continue;
                if (!firstTier) sbAll.Append(" | ");
                sbAll.Append(sbTier);
                firstTier = false;
            }
            return sbAll.ToString();
        }

        private static string ReadParamAsText(Element el, string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            Parameter p = el.LookupParameter(name);
            if (p == null) return "";
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString() ?? "";
                case StorageType.Integer:
                    return p.AsInteger().ToString();
                case StorageType.Double:
                    return p.AsValueString() ?? p.AsDouble().ToString("0.###");
                case StorageType.ElementId:
                    // Revit's Parameter class has no .Document property;
                    // resolving an ElementId to a name requires a Document
                    // reference from the caller. Returning the raw id is
                    // the safe fallback — downstream consumers only need
                    // a stable stringification.
                    var eid = p.AsElementId();
                    return eid == null || eid == ElementId.InvalidElementId
                           ? "" : eid.Value.ToString();
                default:
                    return "";
            }
        }

        private static ICollection<ElementId> CollectScope(UIDocument uidoc)
        {
            var sel = uidoc.Selection.GetElementIds();
            if (sel.Count > 0) return sel;
            string scopeKey = StingCommandHandler.GetExtraParam("ParagraphScope");
            if (string.Equals(scopeKey, "Project", StringComparison.OrdinalIgnoreCase))
            {
                return new FilteredElementCollector(uidoc.Document)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
            }
            // Default: active view
            return new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
        }

        private static string ReadConfigMode(Document doc)
        {
            try
            {
                string cfgPath = Path.Combine(
                    Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                    "project_config.json");
                if (!File.Exists(cfgPath)) return null;
                var jo = JObject.Parse(File.ReadAllText(cfgPath));
                return (string)jo["HANDOVER_MODE"];
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ApplyParagraphPreset: ReadConfigMode failed: {ex.Message}");
                return null;
            }
        }
    }

    // ------------------------------------------------------------------
    // Paragraph preset data model + loader — shared by the dialog and
    // the apply command. Lives in Tags namespace to avoid a new file.
    // ------------------------------------------------------------------

    public sealed class ParagraphPresetRow
    {
        public string Parameter { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public bool Brk { get; set; }
        public string Style { get; set; } = "NOM";
        public string Color { get; set; } = "GREY";
        public double Size { get; set; } = 2.0;
        public bool Enabled { get; set; } = true;
    }

    public sealed class ParagraphPresetTier
    {
        public string Label { get; set; } = "";
        public List<ParagraphPresetRow> Rows { get; set; } = new List<ParagraphPresetRow>();
    }

    public sealed class ParagraphPreset
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Readonly { get; set; }
        public string Source { get; set; } = "";
        public Dictionary<string, ParagraphPresetTier> Tiers { get; set; }
            = new Dictionary<string, ParagraphPresetTier>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ParagraphPresets
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string ActivePreset { get; set; } = "Handover";
        public Dictionary<string, ParagraphPreset> Entries { get; set; }
            = new Dictionary<string, ParagraphPreset>(StringComparer.OrdinalIgnoreCase);

        public static string JsonPath =>
            Core.StingToolsApp.FindDataFile("PARAGRAPH_PRESETS.json") ??
            Path.Combine(Core.StingToolsApp.DataPath ?? "", "PARAGRAPH_PRESETS.json");

        public static ParagraphPresets Load()
        {
            string path = JsonPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("PARAGRAPH_PRESETS.json not found", path);
            var jo = JObject.Parse(File.ReadAllText(path));
            var pp = new ParagraphPresets
            {
                SchemaVersion = (string)jo["schema_version"] ?? "1.0",
                ActivePreset = (string)jo["active_preset"] ?? "Handover",
            };
            var presetsObj = jo["presets"] as JObject;
            if (presetsObj == null) return pp;
            foreach (var kv in presetsObj)
            {
                var p = ParseEntry(kv.Key, kv.Value as JObject);
                if (p != null) pp.Entries[kv.Key] = p;
            }
            return pp;
        }

        public void Save()
        {
            var root = new JObject
            {
                ["schema_version"] = SchemaVersion,
                ["active_preset"] = ActivePreset,
            };
            var presetsObj = new JObject();
            foreach (var kv in Entries)
            {
                presetsObj[kv.Key] = SerializeEntry(kv.Value);
            }
            root["presets"] = presetsObj;
            File.WriteAllText(JsonPath, root.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private static ParagraphPreset ParseEntry(string key, JObject obj)
        {
            if (obj == null) return null;
            var p = new ParagraphPreset
            {
                Key = key,
                DisplayName = (string)obj["display_name"] ?? key,
                Description = (string)obj["description"] ?? "",
                Readonly = (bool?)obj["readonly"] ?? false,
                Source = (string)obj["source"] ?? "",
            };
            var tiersObj = obj["tiers"] as JObject;
            if (tiersObj == null) return p;
            foreach (var kv in tiersObj)
            {
                var t = new ParagraphPresetTier { Label = (string)kv.Value["label"] ?? "" };
                var rowsArr = kv.Value["rows"] as JArray;
                if (rowsArr != null)
                {
                    foreach (var r in rowsArr)
                    {
                        t.Rows.Add(new ParagraphPresetRow
                        {
                            Parameter = (string)r["parameter"] ?? "",
                            Prefix    = (string)r["prefix"] ?? "",
                            Suffix    = (string)r["suffix"] ?? "",
                            Brk       = (bool?)r["break"] ?? false,
                            Style     = (string)r["style"] ?? "NOM",
                            Color     = (string)r["color"] ?? "GREY",
                            Size      = (double?)r["size"] ?? 2.0,
                            Enabled   = (bool?)r["enabled"] ?? true,
                        });
                    }
                }
                p.Tiers[kv.Key] = t;
            }
            return p;
        }

        private static JObject SerializeEntry(ParagraphPreset p)
        {
            var obj = new JObject
            {
                ["display_name"] = p.DisplayName,
                ["description"] = p.Description,
                ["readonly"] = p.Readonly,
                ["source"] = p.Source,
            };
            var tiersObj = new JObject();
            foreach (var kv in p.Tiers)
            {
                var t = kv.Value;
                var rowsArr = new JArray();
                foreach (var r in t.Rows)
                {
                    rowsArr.Add(new JObject
                    {
                        ["parameter"] = r.Parameter,
                        ["prefix"]    = r.Prefix,
                        ["suffix"]    = r.Suffix,
                        ["break"]     = r.Brk,
                        ["style"]     = r.Style,
                        ["color"]     = r.Color,
                        ["size"]      = r.Size,
                        ["enabled"]   = r.Enabled,
                    });
                }
                tiersObj[kv.Key] = new JObject
                {
                    ["label"] = t.Label,
                    ["rows"] = rowsArr,
                };
            }
            obj["tiers"] = tiersObj;
            return obj;
        }
    }

    /// <summary>
    /// Writes HANDOVER_MODE to project_config.json and invalidates compliance cache.
    /// Triggered by the Handover Mode toggle in Tag Studio > Tokens & Depth.
    /// Mode is passed via extra-param "HandoverMode" (values: Handover / DesignConstruction / Custom).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SetHandoverModeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string mode = StingCommandHandler.GetExtraParam("HandoverMode");
            if (string.IsNullOrEmpty(mode)) mode = "Handover";

            try
            {
                string cfgPath = Path.Combine(
                    Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                    "project_config.json");
                JObject jo = File.Exists(cfgPath)
                    ? JObject.Parse(File.ReadAllText(cfgPath))
                    : new JObject();
                jo["HANDOVER_MODE"] = mode;
                if (!string.IsNullOrEmpty(cfgPath))
                    File.WriteAllText(cfgPath, jo.ToString(Newtonsoft.Json.Formatting.Indented));

                // Mirror into PARAGRAPH_PRESETS.json active_preset so Apply picks it up
                try
                {
                    var presets = ParagraphPresets.Load();
                    presets.ActivePreset = mode;
                    presets.Save();
                }
                catch (Exception ex) { StingLog.Warn($"SetHandoverMode: preset mirror failed: {ex.Message}"); }

                // Reload TagConfig so category warnings (loaded from the
                // mode-specific CSVs via HandoverModeHelper) refresh live
                // instead of waiting for the next document open.
                try { Core.TagConfig.LoadDefaults(); }
                catch (Exception ex) { StingLog.Warn($"SetHandoverMode: TagConfig reload failed: {ex.Message}"); }

                StingLog.Info($"Handover mode set to {mode}");
                if (string.IsNullOrEmpty(StingCommandHandler.GetExtraParam("SuppressDialog")))
                {
                    TaskDialog.Show("Handover Mode",
                        $"Mode: {mode}\n\n" +
                        "Tag Studio > Tokens & Depth > Paragraph Builder can edit the rows.\n" +
                        "Click 'Apply preset' or run Workflow > TierConversionHandover to push to elements.");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SetHandoverMode failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
