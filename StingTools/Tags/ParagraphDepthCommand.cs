using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Tags
{
    /// <summary>
    /// Sets paragraph depth on element types (TAG_PARA_STATE_1..10_BOOL).
    /// Depth is cumulative — depth N enables states 1..N. Tag family label rows
    /// use Calculated Values gated by TAG_PARA_STATE_n_BOOL, so raising depth
    /// widens the visible tier set progressively.
    /// These are Type parameters — changes apply to every instance sharing the type.
    ///
    /// Depth → enabled states:
    ///   1  T1 only              (Compact — identity + dimensions)
    ///   2  T1+T2                (Standard — adds materials, thermal, acoustic)
    ///   3  T1+T2+T3             (Comprehensive — regulatory, sustainability, QA)
    ///   4  T1..T4               (+ Commissioning & handover)
    ///   5  T1..T5               (+ Cost & procurement)
    ///   6  T1..T6               (+ Carbon & sustainability)
    ///   7  T1..T7               (+ Fabrication & QC)
    ///   8  T1..T8               (+ Clash & coordination)
    ///   9  T1..T9               (+ As-built & health)
    ///   10 T1..T10              (Full audit trail incl. compliance)
    ///
    /// Depth can be supplied directly via the "ParaDepth" extra-param (e.g.
    /// from a slider on the dock panel). When absent, a TaskDialog covers 1-3
    /// and the full 1-10 picker is reserved for the slider path.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetParagraphDepthCommand : IExternalCommand
    {
        private const int MaxTier = 10;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            int depth;
            string depthName;

            // Path 1: depth supplied via dock-panel slider / extra param
            string paraExtra = StingCommandHandler.GetExtraParam("ParaDepth");
            if (!string.IsNullOrEmpty(paraExtra) &&
                int.TryParse(paraExtra, out int parsed) &&
                parsed >= 1 && parsed <= MaxTier)
            {
                depth = parsed;
                depthName = DepthDisplayName(depth);
            }
            else
            {
                // Path 2: legacy TaskDialog — three-way Compact/Standard/Comprehensive.
                TaskDialog td = new TaskDialog("Set Paragraph Depth");
                td.MainInstruction = "Select paragraph depth for tag descriptions";
                td.MainContent =
                    "Controls how much detail is shown in Tag 7 paragraph containers.\n" +
                    "This sets Type parameters — all instances of the same type will be affected.\n\n" +
                    "For tiers 4-10 use the depth slider in Tag Studio → Tokens & Depth.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Compact (T1)", "Tier 1 only — basic identity and dimensions");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Standard (T1-T2)", "Tiers 1+2 — adds materials, thermal, acoustic data");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Comprehensive (T1-T3)", "Tiers 1+2+3 — full spec with regulatory, sustainability, QA");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    "Pick depth 4-10...", "Open the full T4-T10 picker (commissioning → compliance)");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                TaskDialogResult result = td.Show();
                switch (result)
                {
                    case TaskDialogResult.CommandLink1: depth = 1; break;
                    case TaskDialogResult.CommandLink2: depth = 2; break;
                    case TaskDialogResult.CommandLink3: depth = 3; break;
                    case TaskDialogResult.CommandLink4:
                        int? picked = PickDepth4To10();
                        if (picked == null) return Result.Cancelled;
                        depth = picked.Value;
                        break;
                    default: return Result.Cancelled;
                }
                depthName = DepthDisplayName(depth);
            }

            // Scope: if selection is non-empty use that; otherwise all element types.
            // Skipping the second dialog when the depth was supplied by the slider
            // makes the dock-panel slider a single-click experience.
            ICollection<ElementId> targetTypeIds;
            var sel = uidoc.Selection.GetElementIds();
            bool sliderPath = !string.IsNullOrEmpty(paraExtra);
            if (sliderPath && sel.Count > 0)
            {
                targetTypeIds = CollectTypeIdsFromSelection(doc, sel);
            }
            else if (sliderPath)
            {
                targetTypeIds = CollectAllElementTypeIds(doc);
            }
            else
            {
                TaskDialog scopeTd = new TaskDialog("Scope");
                scopeTd.MainInstruction = "Apply to which elements?";
                scopeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Selected elements", "Set depth on types of selected elements");
                scopeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "All element types in project", "Set depth on all element types");
                scopeTd.CommonButtons = TaskDialogCommonButtons.Cancel;

                TaskDialogResult scopeResult = scopeTd.Show();
                if (scopeResult == TaskDialogResult.CommandLink1)
                {
                    if (sel.Count == 0)
                    {
                        TaskDialog.Show("Set Paragraph Depth", "No elements selected.");
                        return Result.Cancelled;
                    }
                    targetTypeIds = CollectTypeIdsFromSelection(doc, sel);
                }
                else if (scopeResult == TaskDialogResult.CommandLink2)
                {
                    targetTypeIds = CollectAllElementTypeIds(doc);
                }
                else
                {
                    return Result.Cancelled;
                }
            }

            // Build the ten paragraph-state param names once (avoid string work per element).
            string[] paraNames = new[]
            {
                ParamRegistry.PARA_STATE_1, ParamRegistry.PARA_STATE_2, ParamRegistry.PARA_STATE_3,
                ParamRegistry.PARA_STATE_4, ParamRegistry.PARA_STATE_5, ParamRegistry.PARA_STATE_6,
                ParamRegistry.PARA_STATE_7, ParamRegistry.PARA_STATE_8, ParamRegistry.PARA_STATE_9,
                ParamRegistry.PARA_STATE_10,
            };

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Set Paragraph Depth"))
            {
                tx.Start();
                foreach (ElementId typeId in targetTypeIds)
                {
                    Element typeEl = doc.GetElement(typeId);
                    if (typeEl == null) continue;
                    bool anySet = false;
                    for (int i = 0; i < MaxTier; i++)
                    {
                        bool enabled = (i + 1) <= depth;
                        anySet |= SetYesNo(typeEl, paraNames[i], enabled);
                    }
                    if (anySet) updated++;
                }
                tx.Commit();
            }

            if (!sliderPath)
            {
                TaskDialog.Show("Set Paragraph Depth",
                    $"Paragraph depth set to: {depthName}\n" +
                    $"Element types updated: {updated}");
            }
            StingLog.Info($"Paragraph depth set to {depthName} on {updated} types");
            return Result.Succeeded;
        }

        /// <summary>
        /// Phase 165 — full 10-tier picker reachable from the legacy depth dialog's
        /// fourth command link. Mirrors the slider on the dock panel for users
        /// without slider access. Returns 1-10 or null on cancel.
        /// </summary>
        private static int? PickDepth4To10()
        {
            var items = new List<string>
            {
                "1 — Compact (T1 — Identity only)",
                "2 — Standard (T1-T2 — + System & dimensions)",
                "3 — Comprehensive (T1-T3 — + Spatial / regulatory / QA)",
                "4 — + Commissioning data (T4)",
                "5 — + Cost & Procurement (T5)",
                "6 — + Carbon & Sustainability (T6)",
                "7 — + Fabrication & QC (T7)",
                "8 — + Clash Triage (T8)",
                "9 — + As-Built Record (T9)",
                "10 — Full Specification (T1-T10 incl. compliance audit)",
            };

            string picked = null;
            try
            {
                picked = StingTools.UI.StingListPicker.Show(
                    "Set Paragraph Depth",
                    "Choose how many tiers (T1-T10) of tag content are shown",
                    items);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PickDepth4To10 list picker failed, falling back to TaskDialog: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(picked))
            {
                int sp = picked.IndexOf(' ');
                string head = sp > 0 ? picked.Substring(0, sp) : picked;
                if (int.TryParse(head, out int d) && d >= 1 && d <= 10) return d;
                return null;
            }

            // Fallback — chain two TaskDialogs (TD command links cap at 4).
            TaskDialog td = new TaskDialog("Set Paragraph Depth");
            td.MainInstruction = "Pick depth tier range";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "1-3 (Compact / Standard / Comprehensive)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "4-6 (+ Commissioning / Cost / Carbon)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "7-10 (+ Fabrication / Clash / As-built / Full)");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            int rangeStart;
            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: rangeStart = 1; break;
                case TaskDialogResult.CommandLink2: rangeStart = 4; break;
                case TaskDialogResult.CommandLink3: rangeStart = 7; break;
                default: return null;
            }

            TaskDialog td2 = new TaskDialog("Set Paragraph Depth");
            td2.MainInstruction = $"Pick exact depth ({rangeStart}-{Math.Min(rangeStart + 3, 10)})";
            int rangeMax = rangeStart == 7 ? 10 : rangeStart + 2;
            for (int d = rangeStart; d <= rangeMax; d++)
            {
                var cmd = (TaskDialogCommandLinkId)
                    ((int)TaskDialogCommandLinkId.CommandLink1 + (d - rangeStart));
                td2.AddCommandLink(cmd, $"Depth {d}");
            }
            td2.CommonButtons = TaskDialogCommonButtons.Cancel;
            var r = td2.Show();
            if (r == TaskDialogResult.Cancel) return null;
            int offset = (int)r - (int)TaskDialogResult.CommandLink1;
            return rangeStart + offset;
        }

        private static string DepthDisplayName(int depth)
        {
            switch (depth)
            {
                case 1: return "Compact (T1)";
                case 2: return "Standard (T1-T2)";
                case 3: return "Comprehensive (T1-T3)";
                default: return $"Tier {depth} (T1-T{depth})";
            }
        }

        private static ICollection<ElementId> CollectTypeIdsFromSelection(
            Document doc, ICollection<ElementId> sel)
        {
            var types = new HashSet<ElementId>();
            foreach (ElementId id in sel)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;
                ElementId tid = e.GetTypeId();
                if (tid != ElementId.InvalidElementId) types.Add(tid);
            }
            return types;
        }

        private static ICollection<ElementId> CollectAllElementTypeIds(Document doc)
        {
            return new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToElementIds();
        }

        private static bool SetYesNo(Element el, string paramName, bool value)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;

            // TAG_PARA_STATE_N_BOOL is stored as TEXT in MR_PARAMETERS so that
            // Revit tag-family Calculated Values can use it inside if(...) — Yes/No
            // parameters cannot be referenced directly by label-formula conditions.
            // Legacy families that still carry the YESNO datatype are kept working
            // by the Integer branch.
            if (p.StorageType == StorageType.String)
            {
                string target = value ? "Yes" : "No";
                string cur = p.AsString() ?? "";
                if (string.Equals(cur, target, StringComparison.OrdinalIgnoreCase)) return false;
                p.Set(target);
                return true;
            }
            if (p.StorageType == StorageType.Integer)
            {
                int target = value ? 1 : 0;
                if (p.AsInteger() == target) return false;
                p.Set(target);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Enhanced warning visibility command — supports five modes:
    ///   None     → suppress ALL warnings (TAG_WARN_VISIBLE_BOOL = No/0)
    ///   Critical → show only critical-severity warnings
    ///   High     → show Critical + High
    ///   Medium   → show Critical + High + Medium
    ///   All      → show all warnings (default)
    ///
    /// Mode is passed via StingCommandHandler.GetExtraParam("WarnMode") from
    /// the Tag Studio → Tokens & Depth radio buttons (rbWarnNone / rbWarnCritical
    /// / rbWarnHigh / rbWarnMedium / rbWarnAll).  When no mode is supplied
    /// (legacy "Toggle Warnings" button call) a TaskDialog is shown instead.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleWarningVisibilityCommand : IExternalCommand
    {
        public const string FILTER_NONE     = "NONE";
        public const string FILTER_CRITICAL = "CRITICAL";
        public const string FILTER_HIGH     = "HIGH";
        public const string FILTER_MEDIUM   = "MEDIUM";
        public const string FILTER_ALL      = "ALL";

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Read mode from UI radio buttons passed via command handler
            string mode = StingCommandHandler.GetExtraParam("WarnMode");

            if (string.IsNullOrEmpty(mode))
            {
                // Legacy path: no radio selection — show dialog
                mode = ShowLegacyDialog();
                if (mode == null) return Result.Cancelled;
            }

            bool showWarnings = !mode.Equals(FILTER_NONE, StringComparison.OrdinalIgnoreCase);
            string severityFilter = mode.ToUpperInvariant();

            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToList();

            int updatedVis = 0, updatedSev = 0;

            using (Transaction tx = new Transaction(doc, "STING Set Warning Visibility"))
            {
                tx.Start();

                foreach (Element typeEl in allTypes)
                {
                    // --- TAG_WARN_VISIBLE_BOOL ---
                    Parameter pVis = typeEl.LookupParameter(ParamRegistry.WARN_VISIBLE);
                    if (pVis != null && !pVis.IsReadOnly)
                    {
                        if (pVis.StorageType == StorageType.Integer)
                        {
                            int target = showWarnings ? 1 : 0;
                            if (pVis.AsInteger() != target) { pVis.Set(target); updatedVis++; }
                        }
                        else if (pVis.StorageType == StorageType.String)
                        {
                            string target = showWarnings ? "Yes" : "No";
                            if (!string.Equals(pVis.AsString(), target,
                                StringComparison.OrdinalIgnoreCase))
                            { pVis.Set(target); updatedVis++; }
                        }
                    }

                    // --- TAG_WARN_SEVERITY_FILTER_TXT ---
                    Parameter pSev = typeEl.LookupParameter("TAG_WARN_SEVERITY_FILTER_TXT");
                    if (pSev != null && !pSev.IsReadOnly &&
                        pSev.StorageType == StorageType.String)
                    {
                        if (!string.Equals(pSev.AsString(), severityFilter,
                            StringComparison.OrdinalIgnoreCase))
                        { pSev.Set(severityFilter); updatedSev++; }
                    }
                }

                tx.Commit();
            }

            string stateLabel = showWarnings
                ? $"ENABLED (filter: {severityFilter})"
                : "DISABLED (None — all warnings suppressed)";

            StingLog.Info($"Warning visibility {stateLabel}: " +
                          $"vis={updatedVis} types, sev={updatedSev} types");

            // Show result dialog only for manual/legacy calls
            if (string.IsNullOrEmpty(StingCommandHandler.GetExtraParam("WarnMode")))
            {
                TaskDialog.Show("Warning Visibility",
                    $"Warning visibility: {stateLabel}\n" +
                    $"Types updated (visibility):       {updatedVis}\n" +
                    $"Types updated (severity filter): {updatedSev}");
            }

            return Result.Succeeded;
        }

        private static string ShowLegacyDialog()
        {
            TaskDialog td = new TaskDialog("Warning Visibility");
            td.MainInstruction = "Show or hide threshold warnings in tags?";
            td.MainContent =
                "Warning text (e.g. [!U > 0.70], [!VD > 4%]) is appended to tags\n" +
                "when parameter values exceed standards-based thresholds.\n\n" +
                "Use Tag Studio → Tokens & Depth to set severity level\n" +
                "(None / Critical / High / Medium / All).";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Show all warnings", "Enable all threshold warning text");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Hide all warnings", "Suppress all warnings (presentation / clean mode)");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            return td.Show() switch
            {
                TaskDialogResult.CommandLink1 => FILTER_ALL,
                TaskDialogResult.CommandLink2 => FILTER_NONE,
                _                             => null
            };
        }
    }

}
