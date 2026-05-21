using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// One IUpdater for two material automations:
    ///
    ///   A1 — Auto-apply material on element creation.
    ///        Consults <see cref="MaterialRuleRegistry"/> (category × family
    ///        name regex × level regex × phase regex → material name) and
    ///        writes the picked material into the new element's Material
    ///        parameter when one isn't already assigned. Default OFF.
    ///
    ///   A2 — Auto-fill cost / carbon on Material creation.
    ///        When a new <see cref="Material"/> appears, looks up the
    ///        corporate <see cref="MaterialLookupCsv"/> by name and (when
    ///        empty) writes ALL_MODEL_COST + STING_EMB_CARBON_NR.
    ///        Default ON because it's idempotent and harmless.
    ///
    /// Both run on the same UpdaterId so we share the trigger budget.
    /// Triggers are only added when the user enables the feature
    /// (Toggle*), so an idle updater costs nothing.
    /// </summary>
    public class StingMaterialUpdater : IUpdater
    {
        private static UpdaterId _updaterId;
        private static StingMaterialUpdater _instance;
        private static bool _autoApplyEnabled;
        private static bool _autoFillEnabled = true; // default ON
        private static int _processedCount;

        private readonly AddInId _addinId;

        public StingMaterialUpdater(AddInId addinId)
        {
            _addinId = addinId;
            _updaterId = new UpdaterId(addinId, new Guid("D9F1A7C3-B5E8-4291-8C7D-F6E5D4C3B2A1"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "STING Material Updater";
        public string GetAdditionalInformation() => "Auto-applies materials to new elements + auto-fills cost / carbon on new materials.";
        public ChangePriority GetChangePriority() => ChangePriority.MEPCalculations;

        public static bool AutoApplyEnabled => _autoApplyEnabled;
        public static bool AutoFillEnabled  => _autoFillEnabled;
        public static int ProcessedCount    => _processedCount;

        public static void Register(UIControlledApplication application)
        {
            try
            {
                _instance = new StingMaterialUpdater(application.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);
                UpdaterRegistry.DisableUpdater(_updaterId);
                _autoApplyEnabled = false;
                _autoFillEnabled = true; // logical on, but triggers idle until first new Material
                AddTriggers();
                StingLog.Info("StingMaterialUpdater: registered (auto-apply OFF, auto-fill ON)");
            }
            catch (Exception ex) { StingLog.Error("StingMaterialUpdater register", ex); }
        }

        public static void Unregister()
        {
            try
            {
                if (_updaterId != null)
                    UpdaterRegistry.UnregisterUpdater(_updaterId);
            }
            catch (Exception ex) { StingLog.Warn($"StingMaterialUpdater unregister: {ex.Message}"); }
        }

        public static bool ToggleAutoApply()
        {
            _autoApplyEnabled = !_autoApplyEnabled;
            EnableIfNeeded();
            StingLog.Info($"StingMaterialUpdater: auto-apply {(_autoApplyEnabled ? "ON" : "OFF")}");
            return _autoApplyEnabled;
        }

        public static bool ToggleAutoFill()
        {
            _autoFillEnabled = !_autoFillEnabled;
            EnableIfNeeded();
            StingLog.Info($"StingMaterialUpdater: auto-fill {(_autoFillEnabled ? "ON" : "OFF")}");
            return _autoFillEnabled;
        }

        private static void EnableIfNeeded()
        {
            if (_updaterId == null) return;
            try
            {
                if (_autoApplyEnabled || _autoFillEnabled)
                    UpdaterRegistry.EnableUpdater(_updaterId);
                else
                    UpdaterRegistry.DisableUpdater(_updaterId);
            }
            catch (Exception ex) { StingLog.Warn($"EnableIfNeeded: {ex.Message}"); }
        }

        private static void AddTriggers()
        {
            try
            {
                // Trigger 1 — element additions in the categories we model:
                // Walls / Floors / Roofs / Ceilings / Doors / Windows /
                // ColumnsStructural / Furniture / Pipes / Ducts / etc.
                var cats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Roofs,
                    BuiltInCategory.OST_Ceilings,
                    BuiltInCategory.OST_Doors,
                    BuiltInCategory.OST_Windows,
                    BuiltInCategory.OST_Columns,
                    BuiltInCategory.OST_StructuralColumns,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_StructuralFoundation,
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_CableTray,
                    BuiltInCategory.OST_Furniture,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_ElectricalFixtures,
                    BuiltInCategory.OST_LightingFixtures,
                };
                var catIds = cats.Select(c => new ElementId(c)).Cast<ElementId>().ToList();
                var elementFilter = new ElementMulticategoryFilter(cats);
                UpdaterRegistry.AddTrigger(_updaterId, elementFilter,
                    Element.GetChangeTypeElementAddition());

                // Trigger 2 — new Material additions (auto-fill).
                var matFilter = new ElementCategoryFilter(BuiltInCategory.OST_Materials);
                UpdaterRegistry.AddTrigger(_updaterId, matFilter,
                    Element.GetChangeTypeElementAddition());
            }
            catch (Exception ex) { StingLog.Warn($"StingMaterialUpdater.AddTriggers: {ex.Message}"); }
        }

        // ── IUpdater.Execute ─────────────────────────────────────────────

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                if (doc == null) return;
                foreach (var id in data.GetAddedElementIds())
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    try
                    {
                        if (el is Material mat) HandleNewMaterial(doc, mat);
                        else HandleNewElement(doc, el);
                        _processedCount++;
                    }
                    catch (Exception ex) { StingLog.Warn($"StingMaterialUpdater {id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Error("StingMaterialUpdater.Execute", ex); }
        }

        // ── Auto-apply (A1) ──────────────────────────────────────────────

        private void HandleNewElement(Document doc, Element el)
        {
            if (!_autoApplyEnabled) return;
            // Skip elements that already have a material assigned.
            var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.ElementId) return;
            var existing = p.AsElementId();
            if (existing != null && existing.Value > 0) return;

            string matchedName = MaterialRuleRegistry.GetOrLoad(doc).Resolve(doc, el);
            if (string.IsNullOrEmpty(matchedName)) return;

            // Find the material by name.
            var target = new FilteredElementCollector(doc).OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, matchedName, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            try
            {
                p.Set(target.Id);
                StingLog.Info($"AutoApply: {el.Category?.Name} {el.Id} → '{matchedName}'");
            }
            catch (Exception ex) { StingLog.Warn($"AutoApply set: {ex.Message}"); }
        }

        // ── Auto-fill (A2) ──────────────────────────────────────────────

        private void HandleNewMaterial(Document doc, Material m)
        {
            if (!_autoFillEnabled) return;
            string name = m.Name ?? "";
            if (string.IsNullOrEmpty(name)) return;

            // 1) Project override wins; 2) corporate lookup fills the rest.
            var ov = MaterialOverrideRegistry.ResolveOverride(doc, name);

            // Cost — only fill if currently empty.
            try
            {
                var cp = m.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                if (cp != null && !cp.IsReadOnly && cp.StorageType == StorageType.Double && cp.AsDouble() == 0)
                {
                    double cost = ov?.Cost ?? MaterialLookupCsv.GetCost(name);
                    if (cost > 0) cp.Set(cost);
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoFill cost '{name}': {ex.Message}"); }

            // Carbon — fill into STING_EMB_CARBON_NR.
            try
            {
                var lp = m.LookupParameter("STING_EMB_CARBON_NR");
                if (lp != null && !lp.IsReadOnly && lp.StorageType == StorageType.Double && lp.AsDouble() == 0)
                {
                    double c = ov?.CarbonKgCo2e ?? MaterialLookupCsv.GetCarbon(name);
                    if (c > 0) lp.Set(c);
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoFill carbon '{name}': {ex.Message}"); }

            // EPD source + date (when the override file provides them).
            try
            {
                var es = m.LookupParameter("STING_MAT_EPD_SRC_TXT");
                if (es != null && !es.IsReadOnly && es.StorageType == StorageType.String &&
                    string.IsNullOrEmpty(es.AsString()) && !string.IsNullOrEmpty(ov?.EpdSource))
                    es.Set(ov.EpdSource);
                var ed = m.LookupParameter("STING_MAT_EPD_DATE_TXT");
                if (ed != null && !ed.IsReadOnly && ed.StorageType == StorageType.String &&
                    string.IsNullOrEmpty(ed.AsString()) && !string.IsNullOrEmpty(ov?.EpdDate))
                    ed.Set(ov.EpdDate);
            }
            catch (Exception ex) { StingLog.Warn($"AutoFill EPD '{name}': {ex.Message}"); }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  MaterialRuleRegistry — auto-apply rule loader
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads <c>&lt;project&gt;/_BIM_COORD/material_rules.json</c>
    /// (corporate fallback at <c>Data/STING_MATERIAL_RULES.json</c>).
    /// First-match-wins rule list of the shape:
    /// <code>
    /// { "category": "OST_Walls", "familyPattern": "^Basic.*",
    ///   "levelPattern": "^B\\d", "material": "BLE_Concrete_C40" }
    /// </code>
    /// All patterns are optional; missing means wildcard. Empty list →
    /// nothing is auto-applied.
    /// </summary>
    public class MaterialRuleRegistry
    {
        public List<MaterialRule> Rules { get; set; } = new List<MaterialRule>();

        private static readonly Dictionary<string, MaterialRuleRegistry> _cache =
            new Dictionary<string, MaterialRuleRegistry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();
        public const string FileName = "material_rules.json";

        public static MaterialRuleRegistry GetOrLoad(Document doc)
        {
            if (doc == null) return new MaterialRuleRegistry();
            string key = doc.PathName ?? doc.Title ?? "(untitled)";
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var loaded = LoadFromDisk(doc);
                _cache[key] = loaded;
                return loaded;
            }
        }

        public static void Reload(Document doc)
        {
            if (doc == null) return;
            string key = doc.PathName ?? doc.Title ?? "(untitled)";
            lock (_lock) { _cache.Remove(key); }
        }

        private static MaterialRuleRegistry LoadFromDisk(Document doc)
        {
            try
            {
                // Project override first.
                string projPath = Path.Combine(
                    Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "", FileName);
                if (File.Exists(projPath))
                    return JsonConvert.DeserializeObject<MaterialRuleRegistry>(File.ReadAllText(projPath))
                           ?? new MaterialRuleRegistry();
                // Corporate baseline.
                string corp = StingToolsApp.FindDataFile("STING_MATERIAL_RULES.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    return JsonConvert.DeserializeObject<MaterialRuleRegistry>(File.ReadAllText(corp))
                           ?? new MaterialRuleRegistry();
            }
            catch (Exception ex) { StingLog.Warn($"MaterialRuleRegistry load: {ex.Message}"); }
            return new MaterialRuleRegistry();
        }

        /// <summary>Resolve a rule against an element. Returns the material
        /// name to apply, or null if no rule matches.</summary>
        public string Resolve(Document doc, Element el)
        {
            if (el == null || Rules == null || Rules.Count == 0) return null;
            try
            {
                string catName = el.Category?.Name ?? "";
                long catId = el.Category?.Id?.Value ?? 0;
                string famName = "";
                if (el is FamilyInstance fi) famName = fi.Symbol?.FamilyName ?? "";
                else famName = (el as ElementType)?.FamilyName ?? "";
                string levelName = "";
                try { if (el.LevelId != null && el.LevelId.Value > 0) levelName = doc.GetElement(el.LevelId)?.Name ?? ""; }
                catch (Exception ex) { StingLog.Warn($"Resolve level: {ex.Message}"); }
                string phaseName = "";
                try
                {
                    var pp = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                    if (pp != null && pp.StorageType == StorageType.ElementId)
                        phaseName = doc.GetElement(pp.AsElementId())?.Name ?? "";
                }
                catch (Exception ex) { StingLog.Warn($"Resolve phase: {ex.Message}"); }

                foreach (var r in Rules)
                {
                    if (!string.IsNullOrEmpty(r.Category) && !string.Equals(r.Category, catName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!Match(r.FamilyPattern, famName)) continue;
                    if (!Match(r.LevelPattern, levelName)) continue;
                    if (!Match(r.PhasePattern, phaseName)) continue;
                    return r.Material;
                }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialRuleRegistry.Resolve: {ex.Message}"); }
            return null;
        }

        private static bool Match(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            try { return System.Text.RegularExpressions.Regex.IsMatch(value ?? "", pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch { return false; }
        }
    }

    public class MaterialRule
    {
        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)] public string Category { get; set; }
        [JsonProperty("familyPattern", NullValueHandling = NullValueHandling.Ignore)] public string FamilyPattern { get; set; }
        [JsonProperty("levelPattern", NullValueHandling = NullValueHandling.Ignore)] public string LevelPattern { get; set; }
        [JsonProperty("phasePattern", NullValueHandling = NullValueHandling.Ignore)] public string PhasePattern { get; set; }
        [JsonProperty("material")] public string Material { get; set; }
    }
}
