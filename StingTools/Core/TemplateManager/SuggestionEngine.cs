using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// One contextual suggestion the dashboard surfaces at the top of the
    /// detail pane. Reads readiness + drift + project profile to recommend
    /// the next action a coordinator should take.
    /// </summary>
    public sealed class Suggestion
    {
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string OpTag { get; set; } = "";
        public string Severity { get; set; } = "info";   // info | warning | critical
        public int Score { get; set; }                    // 0-100 priority
    }

    public static class SuggestionEngine
    {
        /// <summary>
        /// Compute the top N suggestions for the active document. Cheap —
        /// reads the readiness snapshot + a few project-info parameters.
        /// </summary>
        public static List<Suggestion> Compute(Document doc, ReadinessSnapshot snap, int maxResults = 3)
        {
            var list = new List<Suggestion>();
            if (doc == null) return list;
            if (snap == null) snap = ProjectReadiness.Compute(doc);

            // 1) Missing parameters → critical
            var paramsLight = snap.LightOrDefault("Params");
            if (paramsLight.Total > 0 && paramsLight.Done == 0)
            {
                list.Add(new Suggestion
                {
                    Title = "Bind STING shared parameters",
                    Detail = $"0 of {paramsLight.Total} parameters bound. Most ops will fail until this is done.",
                    OpTag = "CreateParameters",
                    Severity = "critical",
                    Score = 95
                });
            }

            // 2) Missing filters when templates exist
            var filtersLight = snap.LightOrDefault("Filters");
            var tmplLight = snap.LightOrDefault("Templates");
            if (tmplLight.Done > 0 && filtersLight.Done < filtersLight.Total / 2)
            {
                list.Add(new Suggestion
                {
                    Title = "Create STING view filters",
                    Detail = $"{tmplLight.Done} STING templates loaded but only {filtersLight.Done}/{filtersLight.Total} filters present.",
                    OpTag = "CreateFilters",
                    Severity = "warning",
                    Score = 80
                });
            }

            // 3) No templates yet — recommend Master Setup
            if (tmplLight.Total > 0 && tmplLight.Done == 0)
            {
                list.Add(new Suggestion
                {
                    Title = "Run Master Setup",
                    Detail = "No STING templates detected. Run the master pipeline to bootstrap the project.",
                    OpTag = "MasterSetup",
                    Severity = "warning",
                    Score = 90
                });
            }

            // 4) AutoAssign — many views without templates
            var assignBadge = snap.BadgeOrDefault("AutoAssignTemplates");
            if (assignBadge != null && assignBadge.Total > 0)
            {
                int missing = assignBadge.Total - assignBadge.Done;
                if (missing > 10)
                {
                    list.Add(new Suggestion
                    {
                        Title = "Auto-assign templates to views",
                        Detail = $"{missing} views have no template assigned. Auto-Assign uses 5-layer matching to guess.",
                        OpTag = "AutoAssignTemplates",
                        Severity = "info",
                        Score = 60
                    });
                }
            }

            // 5) Healthcare detected
            try
            {
                var pi = doc.ProjectInformation;
                if (pi != null)
                {
                    var hcPar = pi.LookupParameter("PRJ_ORG_HEALTH_PACK_PROFILE_TXT");
                    if (hcPar != null && hcPar.HasValue && !string.IsNullOrWhiteSpace(hcPar.AsString()))
                    {
                        list.Add(new Suggestion
                        {
                            Title = "Apply Healthcare profile",
                            Detail = $"Detected PRJ_ORG_HEALTH_PACK_PROFILE_TXT = {hcPar.AsString()}. Use Healthcare compliance weight profile when scoring.",
                            OpTag = "TemplateComplianceScore",
                            Severity = "info",
                            Score = 70
                        });
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SuggestionEngine healthcare: {ex.Message}"); }

            // 6) Drift detected → SyncTemplateOverrides
            try
            {
                var drift = DriftDetector.Scan(doc);
                if (drift.Count > 0)
                {
                    list.Add(new Suggestion
                    {
                        Title = "Sync VG overrides",
                        Detail = $"{drift.Count} STING templates show VG drift. SyncVGOverrides restores discipline colours.",
                        OpTag = "SyncTemplateOverrides",
                        Severity = "warning",
                        Score = 75
                    });
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SuggestionEngine drift: {ex.Message}"); }

            // 7) Not workshared → suggest setting it up
            if (!doc.IsWorkshared)
            {
                list.Add(new Suggestion
                {
                    Title = "Enable worksharing for worksets",
                    Detail = "Project is not workshared. STING worksets need worksharing enabled.",
                    OpTag = "",
                    Severity = "info",
                    Score = 30
                });
            }

            // Sort by descending score and take top N
            return list.OrderByDescending(s => s.Score).Take(maxResults).ToList();
        }
    }
}
