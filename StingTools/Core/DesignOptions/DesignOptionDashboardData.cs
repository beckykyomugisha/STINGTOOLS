// StingTools — Design Options dashboard aggregator.
//
// Phase 175 — assembles the data the BIM Coordination Center "Options"
// tab needs in one read-only call. Does NOT couple to BCC's WPF layer;
// instead returns a POCO that the existing tab loaders can consume the
// same way as warnings / issues / revisions data.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.DesignOptions
{
    public class DesignOptionDashboardRow
    {
        public string SetName;
        public string OptionName;
        public bool   IsPrimary;
        public bool   IsActive;
        public int    ElementCount;
        public int    LockedSheets;
        public int    LinkedIssues;
        public double CostDelta;
        public double CarbonDelta;
        public double AreaDelta;
        public string DecisionStatus;          // "Decided" | "Pending" | "Overdue"
        public DateTime? DecisionDate;
        public string Purpose;
        public bool   ClientFacing;
    }

    public class DesignOptionDashboardSummary
    {
        public int Sets;
        public int Options;
        public int PrimaryOptions;
        public int DecidedSets;
        public int OverdueSets;       // sets with decisionDate < today and !decided
        public int ClientFacingSets;
        public int OptionsWithIssues;
        public int OptionsWithSheets;
        public List<DesignOptionDashboardRow> Rows = new List<DesignOptionDashboardRow>();

        /// <summary>RAG status for the BCC dashboard strip:
        /// Red = at least one overdue undecided set,
        /// Amber = decision pending within 14 days,
        /// Green = all sets decided.</summary>
        public string RagStatus = "Green";
    }

    public static class DesignOptionDashboardData
    {
        public static DesignOptionDashboardSummary Build(Document doc)
        {
            var sum = new DesignOptionDashboardSummary();
            if (doc == null) return sum;

            try
            {
                var sets = DesignOptionRegistry.Snapshot(doc);
                sum.Sets = sets.Count;
                if (sets.Count == 0) return sum;

                // Pre-index sheets by their VIEWER_OPTION_VISIBILITY.
                var sheetsByOption = new Dictionary<ElementId, int>();
                foreach (var sh in new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSheet))
                            .Cast<ViewSheet>()
                            .Where(s => !s.IsPlaceholder))
                {
                    foreach (var vid in sh.GetAllViewports())
                    {
                        if (!(doc.GetElement(vid) is Viewport vp)) continue;
                        if (!(doc.GetElement(vp.ViewId) is View v)) continue;
                        var p = v.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                        var oid = p?.AsElementId();
                        if (oid == null || oid == ElementId.InvalidElementId) continue;
                        sheetsByOption.TryGetValue(oid, out int n);
                        sheetsByOption[oid] = n + 1;
                    }
                }

                DateTime today = DateTime.UtcNow.Date;
                bool anyOverdue = false, anyPendingSoon = false;

                foreach (var s in sets)
                {
                    bool decided = s.Metadata?.Decided ?? false;
                    if (decided) sum.DecidedSets++;
                    if (s.Metadata?.ClientFacing == true) sum.ClientFacingSets++;
                    if (s.Metadata?.DecisionDate.HasValue == true)
                    {
                        var due = s.Metadata.DecisionDate.Value.Date;
                        if (!decided && due < today) { sum.OverdueSets++; anyOverdue = true; }
                        else if (!decided && (due - today).TotalDays <= 14) anyPendingSoon = true;
                    }
                    sum.Options += s.Options.Count;
                    sum.PrimaryOptions += s.Options.Count(o => o.IsPrimary);

                    foreach (var o in s.Options)
                    {
                        var row = new DesignOptionDashboardRow
                        {
                            SetName       = s.Name,
                            OptionName    = o.Name,
                            IsPrimary     = o.IsPrimary,
                            IsActive      = o.IsActive,
                            ElementCount  = o.ElementCount,
                            CostDelta     = o.Metadata?.CostDelta ?? 0,
                            CarbonDelta   = o.Metadata?.CarbonDelta ?? 0,
                            AreaDelta     = o.Metadata?.AreaDelta ?? 0,
                            LinkedIssues  = o.Metadata?.LinkedIssues?.Count ?? 0,
                            LockedSheets  = sheetsByOption.TryGetValue(o.OptionId, out int n) ? n : 0,
                            DecisionDate  = s.Metadata?.DecisionDate,
                            Purpose       = s.Metadata?.Purpose,
                            ClientFacing  = s.Metadata?.ClientFacing ?? false,
                            DecisionStatus = decided ? "Decided"
                                            : (s.Metadata?.DecisionDate.HasValue == true
                                                && s.Metadata.DecisionDate.Value.Date < today
                                                ? "Overdue" : "Pending")
                        };
                        if (row.LinkedIssues > 0)  sum.OptionsWithIssues++;
                        if (row.LockedSheets > 0)  sum.OptionsWithSheets++;
                        sum.Rows.Add(row);
                    }
                }

                sum.RagStatus = anyOverdue ? "Red" : (anyPendingSoon ? "Amber" : "Green");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DesignOptionDashboardData.Build: {ex.Message}");
            }
            return sum;
        }
    }
}
