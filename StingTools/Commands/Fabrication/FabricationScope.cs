// StingTools v4 MVP — Fabrication scope resolver.
//
// Shared helper used by every Fabrication command (Generate Package,
// Cut List, Isometrics, Weld Map). Honours the Fabrication tab's
// scope radios but with a smart fallback: when "Selection" is chosen
// and the current uidoc selection is empty, we fall back to the
// active view so users who clicked a panel button without an explicit
// pick don't get an unhelpful "no MEP elements" dead-end.
//
// All four commands used to collect scope differently — Generate
// Package honoured the radio, but Cut List / Weld Map / Isometrics
// ignored it entirely and scanned the whole doc. This class unifies
// the behaviour so the panel's scope radio is authoritative for every
// action, with per-category counts exposed for the workspace dialog.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    public enum FabScopeMode { Selection, ActiveView, Project }
    public enum FabScopeFallback { None, SelectionToActiveView }

    /// <summary>
    /// Result of resolving the Fabrication scope — either a populated
    /// list grouped by Revit category, or a well-formed "empty" result
    /// the caller can present to the user.
    /// </summary>
    public class FabScopeResult
    {
        public FabScopeMode RequestedMode { get; set; }
        public FabScopeMode ResolvedMode  { get; set; }
        public FabScopeFallback Fallback  { get; set; } = FabScopeFallback.None;

        /// <summary>IDs grouped by category display name (Pipes, Ducts, …).</summary>
        public Dictionary<string, List<ElementId>> ByCategory { get; }
            = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>IDs grouped by discipline bucket (Pipe, Duct, Electrical).</summary>
        public Dictionary<string, List<ElementId>> ByDiscipline { get; }
            = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Element → system token (PLM_SYS_TXT / MEC_SYS_TXT / ELC_SYS_TXT).</summary>
        public Dictionary<long, string> SystemByElement { get; }
            = new Dictionary<long, string>();

        /// <summary>Element → level code (ASS_LVL_COD_TXT).</summary>
        public Dictionary<long, string> LevelByElement { get; }
            = new Dictionary<long, string>();

        public IEnumerable<string> DistinctSystems =>
            SystemByElement.Values.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> DistinctLevels =>
            LevelByElement.Values.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

        public List<ElementId> AllIds => ByCategory.SelectMany(kv => kv.Value).ToList();
        public int TotalCount => ByCategory.Sum(kv => kv.Value.Count);

        public string ScopeLabel
        {
            get
            {
                string @base = ResolvedMode switch
                {
                    FabScopeMode.Project    => "project",
                    FabScopeMode.ActiveView => "active view",
                    _                       => "current selection",
                };
                if (Fallback == FabScopeFallback.SelectionToActiveView)
                    @base += " (auto-fallback from empty selection)";
                return @base;
            }
        }
    }

    public static class FabricationScope
    {
        // Seventeen MEP categories the v4 fabricator can consume.
        // Grouped into three discipline buckets for the workspace preview.
        private static readonly (BuiltInCategory Bic, string CatName, string Discipline)[] CatMap = new[]
        {
            (BuiltInCategory.OST_PipeCurves,       "Pipes",            "Pipe"),
            (BuiltInCategory.OST_FlexPipeCurves,   "Flex pipes",       "Pipe"),
            (BuiltInCategory.OST_PipeFitting,      "Pipe fittings",    "Pipe"),
            (BuiltInCategory.OST_PipeAccessory,    "Pipe accessories", "Pipe"),
            (BuiltInCategory.OST_DuctCurves,       "Ducts",            "Duct"),
            (BuiltInCategory.OST_FlexDuctCurves,   "Flex ducts",       "Duct"),
            (BuiltInCategory.OST_DuctFitting,      "Duct fittings",    "Duct"),
            (BuiltInCategory.OST_DuctAccessory,    "Duct accessories", "Duct"),
            (BuiltInCategory.OST_Conduit,          "Conduit",          "Electrical"),
            (BuiltInCategory.OST_ConduitFitting,   "Conduit fittings", "Electrical"),
            (BuiltInCategory.OST_CableTray,        "Cable tray",       "Electrical"),
            (BuiltInCategory.OST_CableTrayFitting, "Cable tray fittings", "Electrical"),
        };

        private static readonly HashSet<int> CatBicInts =
            new HashSet<int>(CatMap.Select(t => (int)t.Bic));

        /// <summary>Resolve the scope declared on the Fabrication tab radios.</summary>
        public static FabScopeResult Resolve(Document doc, UIDocument uidoc)
        {
            var mode = FabricationOptions.ScopeProject    ? FabScopeMode.Project
                     : FabricationOptions.ScopeActiveView ? FabScopeMode.ActiveView
                                                          : FabScopeMode.Selection;
            var res = new FabScopeResult { RequestedMode = mode, ResolvedMode = mode };

            try
            {
                if (mode == FabScopeMode.Selection)
                {
                    CollectSelection(doc, uidoc, res);
                    // Smart fallback — an empty selection almost always
                    // means "the user didn't pre-pick, just use what's
                    // visible". Promote to ActiveView rather than fail.
                    if (res.TotalCount == 0)
                    {
                        res.Fallback = FabScopeFallback.SelectionToActiveView;
                        res.ResolvedMode = FabScopeMode.ActiveView;
                        CollectActiveView(doc, res);
                    }
                }
                else if (mode == FabScopeMode.ActiveView)
                {
                    CollectActiveView(doc, res);
                }
                else
                {
                    CollectProject(doc, res);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationScope.Resolve: {ex.Message}");
            }
            return res;
        }

        private static void CollectSelection(Document doc, UIDocument uidoc, FabScopeResult res)
        {
            var sel = uidoc?.Selection?.GetElementIds();
            if (sel == null) return;
            foreach (var id in sel)
            {
                var el = doc.GetElement(id);
                if (el?.Category == null) continue;
                int bic = (int)el.Category.Id.Value;
                if (CatBicInts.Contains(bic)) Bucket(res, el);
            }
        }

        private static void CollectActiveView(Document doc, FabScopeResult res)
        {
            var view = doc?.ActiveView;
            if (view == null) return;
            var filter = new ElementMulticategoryFilter(CatMap.Select(t => t.Bic).ToList());
            var col = new FilteredElementCollector(doc, view.Id)
                .WherePasses(filter)
                .WhereElementIsNotElementType();
            foreach (var e in col) Bucket(res, e);
        }

        private static void CollectProject(Document doc, FabScopeResult res)
        {
            var filter = new ElementMulticategoryFilter(CatMap.Select(t => t.Bic).ToList());
            var col = new FilteredElementCollector(doc)
                .WherePasses(filter)
                .WhereElementIsNotElementType();
            foreach (var e in col) Bucket(res, e);
        }

        private static void Bucket(FabScopeResult res, Element el)
        {
            if (el?.Category == null) return;
            int bic = (int)el.Category.Id.Value;
            var entry = CatMap.FirstOrDefault(t => (int)t.Bic == bic);
            if (entry.CatName == null) return;
            if (!res.ByCategory.TryGetValue(entry.CatName, out var list))
                res.ByCategory[entry.CatName] = list = new List<ElementId>();
            list.Add(el.Id);
            if (!res.ByDiscipline.TryGetValue(entry.Discipline, out var dlist))
                res.ByDiscipline[entry.Discipline] = dlist = new List<ElementId>();
            dlist.Add(el.Id);

            // System & level lookup so the workspace dialog can render
            // per-system / per-level filter pills without re-scanning.
            try
            {
                string sys = el.LookupParameter("PLM_SYS_TXT")?.AsString()
                          ?? el.LookupParameter("MEC_SYS_TXT")?.AsString()
                          ?? el.LookupParameter("ELC_SYS_TXT")?.AsString()
                          ?? "";
                res.SystemByElement[el.Id.Value] = sys ?? "";
                string lvl = el.LookupParameter("ASS_LVL_COD_TXT")?.AsString() ?? "";
                res.LevelByElement[el.Id.Value] = lvl ?? "";
            }
            catch { }
        }

        /// <summary>
        /// Applies discipline-rule toggles on the Fabrication tab and
        /// the per-category check state from the workspace dialog.
        /// Categories whose discipline rule is off, or which the user
        /// unticked in the dialog, are dropped.
        /// </summary>
        public static List<ElementId> FilterByRulesAndCategoryMask(
            FabScopeResult res, IReadOnlyDictionary<string, bool> categoryMask)
            => FilterFull(res, categoryMask, null, null);

        /// <summary>
        /// Full filter honouring category mask + per-system + per-level
        /// pill filters. Null means "do not filter on that axis".
        /// </summary>
        public static List<ElementId> FilterFull(
            FabScopeResult res,
            IReadOnlyDictionary<string, bool> categoryMask,
            IReadOnlyCollection<string> systemAllow,
            IReadOnlyCollection<string> levelAllow)
        {
            var keep = new List<ElementId>();
            if (res == null) return keep;
            bool sysActive = systemAllow != null && systemAllow.Count > 0;
            bool lvlActive = levelAllow != null && levelAllow.Count > 0;
            var sysSet = sysActive ? new HashSet<string>(systemAllow, StringComparer.OrdinalIgnoreCase) : null;
            var lvlSet = lvlActive ? new HashSet<string>(levelAllow, StringComparer.OrdinalIgnoreCase) : null;

            foreach (var kv in res.ByCategory)
            {
                if (categoryMask != null && categoryMask.TryGetValue(kv.Key, out var on) && !on)
                    continue;
                var disc = CatMap.FirstOrDefault(t => string.Equals(t.CatName, kv.Key, StringComparison.OrdinalIgnoreCase)).Discipline;
                if (!DisciplineEnabled(disc)) continue;
                foreach (var id in kv.Value)
                {
                    if (sysActive)
                    {
                        res.SystemByElement.TryGetValue(id.Value, out var s);
                        if (!sysSet.Contains(s ?? "")) continue;
                    }
                    if (lvlActive)
                    {
                        res.LevelByElement.TryGetValue(id.Value, out var l);
                        if (!lvlSet.Contains(l ?? "")) continue;
                    }
                    keep.Add(id);
                }
            }
            return keep;
        }

        private static bool DisciplineEnabled(string disc) => disc switch
        {
            "Pipe"       => FabricationOptions.RulePipe || FabricationOptions.RulePipeLB,
            "Duct"       => FabricationOptions.RuleDuct || FabricationOptions.RuleDuctPitt,
            "Electrical" => FabricationOptions.RuleConduit,
            _            => true,
        };
    }
}
