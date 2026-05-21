using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Coverage-gate pre-flight check. Counts modelled elements that
    /// have a Material parameter but no material assigned, and blocks
    /// downstream actions (Issue Deliverable, batch export) when the
    /// coverage ratio drops below a configurable threshold.
    ///
    /// Default threshold: 95 % of materialable elements must have a
    /// material assigned. Override per-project by setting a custom
    /// value in <c>_BIM_COORD/material_coverage.json</c>.
    ///
    /// The gate operates on the same set of "materialable" elements
    /// the Apply-to-Selection workflow uses: every element with either
    /// a "Material" parameter or a MATERIAL_ID_PARAM. Compound elements
    /// are skipped — their layers are governed by Type material binding.
    /// </summary>
    public class MaterialCoverageResult
    {
        public int TotalMaterialable { get; set; }
        public int Assigned { get; set; }
        public int Missing { get; set; }
        public double CoveragePct => TotalMaterialable == 0
            ? 100.0 : 100.0 * Assigned / TotalMaterialable;
        public List<ElementId> MissingIds { get; } = new List<ElementId>();
    }

    public static class MaterialCoverageGate
    {
        public const double DefaultThresholdPct = 95.0;

        public static MaterialCoverageResult Compute(Document doc)
        {
            var result = new MaterialCoverageResult();
            if (doc == null) return result;
            try
            {
                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
                foreach (var el in elements)
                {
                    try
                    {
                        // Walls / Floors / Roofs / Ceilings carry layer materials
                        // via Type; their per-instance Material param is N/A.
                        if (el is HostObject) continue;
                        var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (p == null || p.StorageType != StorageType.ElementId || p.IsReadOnly) continue;
                        result.TotalMaterialable++;
                        var mid = p.AsElementId();
                        if (mid != null && mid.Value > 0) result.Assigned++;
                        else { result.Missing++; result.MissingIds.Add(el.Id); }
                    }
                    catch (Exception ex) { StingLog.Warn($"CoverageGate {el?.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Error("MaterialCoverageGate.Compute", ex); }
            return result;
        }

        /// <summary>
        /// Surface a blocking TaskDialog when coverage is under the
        /// threshold. Returns true if the user proceeds anyway (or if
        /// coverage is fine); false if they cancel.
        /// </summary>
        public static bool ConfirmOrBlock(Document doc, double thresholdPct = DefaultThresholdPct, string actionLabel = "Issue Deliverable")
        {
            var r = Compute(doc);
            if (r.TotalMaterialable == 0) return true;
            if (r.CoveragePct >= thresholdPct)
            {
                StingLog.Info($"CoverageGate {actionLabel}: {r.CoveragePct:F1}% (≥ {thresholdPct}%, pass)");
                return true;
            }
            var td = new TaskDialog($"STING Material Coverage — {actionLabel}")
            {
                MainInstruction = $"Material coverage is below threshold ({r.CoveragePct:F1}% < {thresholdPct}%).",
                MainContent = $"{r.Missing} of {r.TotalMaterialable} materialable element(s) have no material assigned.\n\n" +
                              "Proceeding will issue a deliverable with material spec gaps. Recommended action: cancel, " +
                              "use the MAT > Browse \"Apply → Sel\" workflow on the unassigned elements, then re-issue.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Select unassigned elements",
                "Set Revit selection to the materialable elements that still need a material — then cancel this issue.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Proceed anyway with {actionLabel}",
                "Issue the deliverable with a known coverage gap. Recorded in the audit log.");
            var res = td.Show();
            if (res == TaskDialogResult.CommandLink1)
            {
                try
                {
                    StingCommandHandler.CurrentApp?.ActiveUIDocument?.Selection?.SetElementIds(r.MissingIds);
                }
                catch (Exception ex) { StingLog.Warn($"CoverageGate select: {ex.Message}"); }
                return false;
            }
            if (res == TaskDialogResult.CommandLink2)
            {
                // Audit log the override so we can trace who issued under-covered packages.
                MaterialAuditLogger.Log(doc, "MAT_CoverageOverride", actionLabel,
                    new Dictionary<string, object>
                    {
                        ["coveragePct"]      = Math.Round(r.CoveragePct, 1),
                        ["thresholdPct"]     = thresholdPct,
                        ["missing"]          = r.Missing,
                        ["totalMaterialable"]= r.TotalMaterialable,
                    });
                return true;
            }
            return false;
        }
    }
}
