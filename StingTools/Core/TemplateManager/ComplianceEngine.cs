using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// One view's score under a given weight profile.
    /// </summary>
    public sealed class ComplianceScore
    {
        public int ViewId { get; set; }
        public string ViewName { get; set; } = "";
        public string ViewType { get; set; } = "";
        public string Profile { get; set; } = "default";
        public double Score { get; set; }
        public double MaxScore { get; set; }
        public Dictionary<string, double> Breakdown { get; set; } = new();
        public string Status =>
            MaxScore <= 0 ? "Unknown" :
            (Score / MaxScore) >= 0.85 ? "Green" :
            (Score / MaxScore) >= 0.5 ? "Amber" : "Red";
        public double Pct => MaxScore <= 0 ? 0 : Score * 100.0 / MaxScore;
    }

    /// <summary>
    /// Project-wide compliance roll-up.
    /// </summary>
    public sealed class ComplianceReport
    {
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
        public string Profile { get; set; } = "default";
        public double AverageScore { get; set; }
        public double MaxScore { get; set; }
        public int TotalViews { get; set; }
        public int Green { get; set; }
        public int Amber { get; set; }
        public int Red { get; set; }
        public List<ComplianceScore> Scores { get; set; } = new();
        public Dictionary<string, int> ByViewType { get; set; } = new();
        public Dictionary<string, int> ByDiscipline { get; set; } = new();

        public string OverallStatus =>
            MaxScore <= 0 ? "Unknown" :
            (AverageScore / MaxScore) >= 0.85 ? "Green" :
            (AverageScore / MaxScore) >= 0.5 ? "Amber" : "Red";

        public double Pct => MaxScore <= 0 ? 0 : AverageScore * 100.0 / MaxScore;
    }

    /// <summary>
    /// Replaces TemplateManager.ScoreViewCompliance with: (a) full coverage
    /// (no Take(50)), (b) per-profile weight selection, (c) cached + cheap
    /// per-view scoring, (d) project-wide roll-up.
    /// </summary>
    public static class ComplianceEngine
    {
        // Per-view cache (avoids re-scoring on every dashboard repaint)
        private static readonly ConcurrentDictionary<string, (DateTime t, ComplianceScore s)> _viewCache = new();
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

        public static ComplianceReport ScoreProject(Document doc, string explicitProfile = null)
        {
            var report = new ComplianceReport();
            if (doc == null) return report;

            var weights = TemplateRulesRegistry.ResolveComplianceProfile(doc, explicitProfile);
            string profile = weights == TemplateRulesRegistry.Get(doc).ComplianceWeights.FirstOrDefault(kvp => kvp.Value == weights).Value
                ? TemplateRulesRegistry.Get(doc).ComplianceWeights.First(kvp => kvp.Value == weights).Key
                : (explicitProfile ?? "default");
            report.Profile = profile;

            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && IsScorable(v)).ToList();
            report.TotalViews = views.Count;

            double totalScore = 0, totalMax = 0;
            foreach (var v in views)
            {
                var s = ScoreView(doc, v, weights, profile);
                report.Scores.Add(s);
                totalScore += s.Score;
                totalMax += s.MaxScore;
                if (s.Status == "Green") report.Green++;
                else if (s.Status == "Amber") report.Amber++;
                else report.Red++;
                report.ByViewType[s.ViewType] = report.ByViewType.GetValueOrDefault(s.ViewType, 0) + 1;
                string disc = GuessDiscFromName(s.ViewName);
                report.ByDiscipline[disc] = report.ByDiscipline.GetValueOrDefault(disc, 0) + 1;
            }
            report.AverageScore = views.Count > 0 ? totalScore / views.Count : 0;
            report.MaxScore = views.Count > 0 ? totalMax / views.Count : 0;
            return report;
        }

        public static ComplianceScore ScoreView(Document doc, View view,
            Dictionary<string, double> weights = null, string profile = null)
        {
            if (view == null) return new ComplianceScore();
            string cacheKey = $"{(doc?.PathName ?? "")}|{(int)view.Id.Value}|{profile ?? "default"}";
            if (_viewCache.TryGetValue(cacheKey, out var hit) && DateTime.UtcNow - hit.t < StaleAfter)
                return hit.s;

            weights ??= TemplateRulesRegistry.ResolveComplianceProfile(doc, profile);
            var s = new ComplianceScore
            {
                ViewId = (int)view.Id.Value,
                ViewName = view.Name ?? "",
                ViewType = view.ViewType.ToString(),
                Profile = profile ?? "default"
            };

            bool hasTemplate = view.ViewTemplateId != ElementId.InvalidElementId;
            View tmpl = hasTemplate ? doc?.GetElement(view.ViewTemplateId) as View : null;

            foreach (var criterion in weights.Keys)
            {
                double w = weights[criterion];
                double earned = ScoreCriterion(doc, view, tmpl, hasTemplate, criterion, w);
                s.Breakdown[criterion] = earned;
                s.Score += earned;
                s.MaxScore += w;
            }

            _viewCache[cacheKey] = (DateTime.UtcNow, s);
            return s;
        }

        public static void Invalidate(Document doc = null)
        {
            if (doc == null) _viewCache.Clear();
            else
            {
                string prefix = (doc.PathName ?? "") + "|";
                foreach (var k in _viewCache.Keys.Where(k => k.StartsWith(prefix)).ToList())
                    _viewCache.TryRemove(k, out _);
            }
        }

        private static double ScoreCriterion(Document doc, View view, View tmpl, bool hasTemplate,
            string criterion, double weight)
        {
            try
            {
                switch (criterion)
                {
                    case "HasTemplate":
                        return hasTemplate ? weight : 0;
                    case "IsStingTemplate":
                        return hasTemplate && tmpl != null
                            && tmpl.Name?.StartsWith("STING", StringComparison.OrdinalIgnoreCase) == true
                            ? weight : 0;
                    case "HasFilters":
                    {
                        if (!hasTemplate || tmpl == null) return 0;
                        int n = tmpl.GetFilters()?.Count ?? 0;
                        return n >= 5 ? weight : weight * n / 5.0;
                    }
                    case "FilterOverrides":
                    {
                        if (!hasTemplate || tmpl == null) return 0;
                        var fids = tmpl.GetFilters();
                        if (fids.Count == 0) return 0;
                        int overridden = 0;
                        foreach (var fid in fids)
                        {
                            try
                            {
                                var ogs = tmpl.GetFilterOverrides(fid);
                                if ((ogs.ProjectionLineColor != null && ogs.ProjectionLineColor.IsValid)
                                    || ogs.Halftone || ogs.Transparency > 0)
                                    overridden++;
                            }
                            catch { }
                        }
                        return weight * overridden / fids.Count;
                    }
                    case "DetailLevel":
                        return view.DetailLevel != ViewDetailLevel.Undefined ? weight : 0;
                    case "CorrectDiscipline":
                    {
                        string match = null;
                        try { match = global::StingTools.Temp.TemplateManager.FindMatchingTemplate(view); }
                        catch { }
                        if (!hasTemplate || tmpl == null) return weight * 0.3;
                        return match != null
                            && string.Equals(tmpl.Name, match, StringComparison.OrdinalIgnoreCase)
                            ? weight : weight * 0.3;
                    }
                    case "PhaseCorrect":
                    {
                        var p = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
                        return (p != null && p.HasValue) ? weight : 0;
                    }
                    case "VGConsistent":
                    {
                        if (!hasTemplate || tmpl == null) return 0;
                        var tplFids = tmpl.GetFilters();
                        var viewFids = view.GetFilters();
                        if (tplFids.Count == 0) return weight;
                        var ts = new HashSet<ElementId>(tplFids);
                        int matching = viewFids.Count(f => ts.Contains(f));
                        return weight * matching / tplFids.Count;
                    }
                    case "NoOrphans":
                    {
                        if (!hasTemplate || tmpl == null) return weight;
                        foreach (var fid in tmpl.GetFilters())
                            if (doc.GetElement(fid) == null) return 0;
                        return weight;
                    }
                    case "ScaleAppropriate":
                    {
                        int sc = view.Scale;
                        return (sc >= 10 && sc <= 500) ? weight : weight * 0.5;
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ComplianceEngine.{criterion}: {ex.Message}"); }
            return 0;
        }

        private static bool IsScorable(View v)
        {
            if (v == null) return false;
            switch (v.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.ThreeD:
                case ViewType.AreaPlan:
                case ViewType.EngineeringPlan:
                case ViewType.Detail:
                    return true;
                default:
                    return false;
            }
        }

        private static string GuessDiscFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            string n = name.ToUpperInvariant();
            if (n.Contains("MECH") || n.Contains("HVAC")) return "M";
            if (n.Contains("ELEC") || n.Contains("POWER")) return "E";
            if (n.Contains("PLUMB")) return "P";
            if (n.Contains("STRUCT")) return "S";
            if (n.Contains("FIRE")) return "FP";
            if (n.Contains("LIGHTING")) return "E";
            return "A";
        }
    }
}
