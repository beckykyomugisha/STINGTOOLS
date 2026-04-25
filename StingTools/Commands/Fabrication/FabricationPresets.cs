// StingTools v4 MVP — Fabrication presets store.
//
// Persists scope + discipline rules + category mask + preferred
// action + title block + view template as named JSON bundles under
// <project>/_BIM_COORD/fab_presets.json so the workspace dialog can
// restore a project-specific setup with one click. Also holds the
// "Last session" preset that we rewrite on every dialog close so the
// next open of the project lands on the same scope.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Fabrication
{
    public enum FabAction
    {
        GeneratePackage,
        ExportCutList,
        ExportWeldMap,
        ExportIsometrics,
        BomRollup,
    }

    public class FabricationPreset
    {
        public string Name { get; set; } = "";
        public string ScopeMode { get; set; } = "Selection"; // Selection | ActiveView | Project

        // Discipline rules
        public bool RulePipe       { get; set; } = true;
        public bool RulePipeLB     { get; set; } = false;
        public bool RuleDuct       { get; set; } = true;
        public bool RuleDuctPitt   { get; set; } = false;
        public bool RuleConduit    { get; set; } = true;

        // Output toggles
        public bool GenerateAssemblies  { get; set; } = true;
        public bool GenerateViews       { get; set; } = true;
        public bool GenerateSheets      { get; set; } = true;
        public bool PlaceISO6412Symbols { get; set; } = true;
        public bool EmitPerDisciplineCsv{ get; set; } = true;
        public bool ContentModeIso6412  { get; set; } = true;

        // Per-category mask (category display name → enabled)
        public Dictionary<string, bool> CategoryMask { get; set; }
            = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Title block / view template (stored by element name so the
        // preset survives element-id churn between sessions).
        public string TitleBlockFamilyAndType { get; set; } = "";
        public string ViewTemplateName        { get; set; } = "";
        public string SheetNumberPattern      { get; set; } = "";
        public string SheetNamePattern        { get; set; } = "";

        public FabAction PreferredAction { get; set; } = FabAction.GeneratePackage;
        public DateTime  SavedAtUtc      { get; set; } = DateTime.UtcNow;
    }

    public static class FabricationPresetStore
    {
        private const string FileName = "fab_presets.json";
        private const string LastSessionName = "__last_session__";

        private static string ResolvePath(Document doc)
        {
            try
            {
                string projectDir = Path.GetDirectoryName(doc?.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projectDir))
                    projectDir = OutputLocationHelper.GetOutputDirectory(doc);
                string dir = Path.Combine(projectDir, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, FileName);
            }
            catch (Exception ex) { StingLog.Warn($"FabricationPresetStore.ResolvePath: {ex.Message}"); return ""; }
        }

        public static List<FabricationPreset> LoadAll(Document doc)
        {
            var path = ResolvePath(doc);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new List<FabricationPreset>();
            try
            {
                var txt = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<FabricationPreset>>(txt) ?? new List<FabricationPreset>();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationPresetStore.LoadAll: {ex.Message}");
                return new List<FabricationPreset>();
            }
        }

        public static List<string> NamedPresetNames(Document doc)
            => LoadAll(doc).Select(p => p.Name)
                .Where(n => !string.Equals(n, LastSessionName, StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        public static FabricationPreset Find(Document doc, string name)
            => LoadAll(doc).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        public static void Save(Document doc, FabricationPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name)) return;
            var path = ResolvePath(doc);
            if (string.IsNullOrEmpty(path)) return;
            var all = LoadAll(doc);
            all.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            preset.SavedAtUtc = DateTime.UtcNow;
            all.Add(preset);
            try { File.WriteAllText(path, JsonConvert.SerializeObject(all, Formatting.Indented)); }
            catch (Exception ex) { StingLog.Warn($"FabricationPresetStore.Save: {ex.Message}"); }
        }

        public static void Delete(Document doc, string name)
        {
            var path = ResolvePath(doc);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var all = LoadAll(doc);
            int n = all.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (n > 0)
            {
                try { File.WriteAllText(path, JsonConvert.SerializeObject(all, Formatting.Indented)); }
                catch (Exception ex) { StingLog.Warn($"FabricationPresetStore.Delete: {ex.Message}"); }
            }
        }

        // ── Capture / apply current state ──────────────────────

        public static FabricationPreset Capture(string name, Document doc, IReadOnlyDictionary<string, bool> categoryMask, FabAction preferred)
        {
            var p = new FabricationPreset
            {
                Name = name,
                ScopeMode = FabricationOptions.ScopeProject    ? "Project"
                         :  FabricationOptions.ScopeActiveView ? "ActiveView"
                                                              : "Selection",
                RulePipe       = FabricationOptions.RulePipe,
                RulePipeLB     = FabricationOptions.RulePipeLB,
                RuleDuct       = FabricationOptions.RuleDuct,
                RuleDuctPitt   = FabricationOptions.RuleDuctPitt,
                RuleConduit    = FabricationOptions.RuleConduit,
                GenerateAssemblies   = FabricationOptions.GenerateAssemblies,
                GenerateViews        = FabricationOptions.GenerateViews,
                GenerateSheets       = FabricationOptions.GenerateSheets,
                PlaceISO6412Symbols  = FabricationOptions.PlaceISO6412Symbols,
                EmitPerDisciplineCsv = FabricationOptions.EmitPerDisciplineCsv,
                ContentModeIso6412   = FabricationOptions.ContentModeIso6412,
                PreferredAction      = preferred,
            };
            if (categoryMask != null)
                foreach (var kv in categoryMask) p.CategoryMask[kv.Key] = kv.Value;

            var sd = FabricationOptions.ShopDrawing;
            if (sd != null && doc != null)
            {
                if (sd.TitleBlockSymbolId != null && sd.TitleBlockSymbolId != ElementId.InvalidElementId
                    && doc.GetElement(sd.TitleBlockSymbolId) is FamilySymbol fs)
                    p.TitleBlockFamilyAndType = $"{fs.FamilyName}:{fs.Name}";
                if (sd.ViewTemplateId != null && sd.ViewTemplateId != ElementId.InvalidElementId
                    && doc.GetElement(sd.ViewTemplateId) is View v)
                    p.ViewTemplateName = v.Name;
                p.SheetNumberPattern = sd.SheetNumberPattern ?? "";
                p.SheetNamePattern   = sd.SheetNamePattern   ?? "";
            }
            return p;
        }

        public static void SaveLastSession(Document doc, IReadOnlyDictionary<string, bool> categoryMask, FabAction preferred)
        {
            var p = Capture(LastSessionName, doc, categoryMask, preferred);
            Save(doc, p);
        }

        public static FabricationPreset LoadLastSession(Document doc) => Find(doc, LastSessionName);

        /// <summary>
        /// Applies a preset to FabricationOptions + ShopDrawing. Returns
        /// the category mask so the dialog can re-tick the boxes.
        /// </summary>
        public static Dictionary<string, bool> Apply(Document doc, FabricationPreset p)
        {
            if (p == null) return new Dictionary<string, bool>();
            FabricationOptions.ScopeSelection  = string.Equals(p.ScopeMode, "Selection",  StringComparison.OrdinalIgnoreCase);
            FabricationOptions.ScopeActiveView = string.Equals(p.ScopeMode, "ActiveView", StringComparison.OrdinalIgnoreCase);
            FabricationOptions.ScopeProject    = string.Equals(p.ScopeMode, "Project",    StringComparison.OrdinalIgnoreCase);
            FabricationOptions.RulePipe       = p.RulePipe;
            FabricationOptions.RulePipeLB     = p.RulePipeLB;
            FabricationOptions.RuleDuct       = p.RuleDuct;
            FabricationOptions.RuleDuctPitt   = p.RuleDuctPitt;
            FabricationOptions.RuleConduit    = p.RuleConduit;
            FabricationOptions.GenerateAssemblies   = p.GenerateAssemblies;
            FabricationOptions.GenerateViews        = p.GenerateViews;
            FabricationOptions.GenerateSheets       = p.GenerateSheets;
            FabricationOptions.PlaceISO6412Symbols  = p.PlaceISO6412Symbols;
            FabricationOptions.EmitPerDisciplineCsv = p.EmitPerDisciplineCsv;
            FabricationOptions.ContentModeIso6412   = p.ContentModeIso6412;

            // Shop drawing — resolve by name so the preset survives element-id changes.
            try
            {
                var sd = new ShopDrawingOptions
                {
                    SheetNumberPattern = p.SheetNumberPattern ?? "",
                    SheetNamePattern   = p.SheetNamePattern   ?? "",
                };
                if (!string.IsNullOrWhiteSpace(p.TitleBlockFamilyAndType) && doc != null)
                {
                    var parts = p.TitleBlockFamilyAndType.Split(':');
                    if (parts.Length == 2)
                    {
                        var fs = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(x => x.FamilyName == parts[0] && x.Name == parts[1]);
                        if (fs != null) sd.TitleBlockSymbolId = fs.Id;
                    }
                }
                if (!string.IsNullOrWhiteSpace(p.ViewTemplateName) && doc != null)
                {
                    var v = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(x => x.IsTemplate && x.Name == p.ViewTemplateName);
                    if (v != null) sd.ViewTemplateId = v.Id;
                }
                FabricationOptions.ShopDrawing = sd;
            }
            catch (Exception ex) { StingLog.Warn($"FabricationPresetStore.Apply shop drawing: {ex.Message}"); }

            return p.CategoryMask != null
                ? new Dictionary<string, bool>(p.CategoryMask, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>();
        }
    }
}
