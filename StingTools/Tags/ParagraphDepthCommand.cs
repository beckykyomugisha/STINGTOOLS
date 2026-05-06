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

            // Phase 165 — Issue #19. Resolve active mode once so depth-label
            // dialog text matches the tier surface the writer will persist.
            ParamRegistry.TagMode activeMode = ParamRegistry.GetActiveTagMode(doc);

            // Path 1: depth supplied via dock-panel slider / extra param
            string paraExtra = StingCommandHandler.GetExtraParam("ParaDepth");
            if (!string.IsNullOrEmpty(paraExtra) &&
                int.TryParse(paraExtra, out int parsed) &&
                parsed >= 1 && parsed <= MaxTier)
            {
                depth = parsed;
                depthName = DepthDisplayName(depth, activeMode);
            }
            else
            {
                // Path 2: legacy TaskDialog — three-way Compact/Standard/Comprehensive
                // plus a 4th link to the full 1-10 picker. Mode-aware copy so the
                // 4-10 link advertises the right tier set per Issue #19.
                string deeperHint = activeMode == ParamRegistry.TagMode.DC
                    ? "DC mode — covers Lifecycle (T4) / Technical Specs (T5) / Classification (T6)"
                    : (activeMode == ParamRegistry.TagMode.Custom
                        ? "Custom mode — project-defined T4-T10 payload"
                        : "Handover mode — Commissioning / Cost / Carbon / Fab / Clash / As-Built / Audit");

                TaskDialog td = new TaskDialog("Set Paragraph Depth");
                td.MainInstruction = $"Select paragraph depth (mode: {activeMode})";
                td.MainContent =
                    "Controls how much detail is shown in Tag 7 paragraph containers.\n" +
                    "This sets Type parameters — all instances of the same type will be affected.\n\n" +
                    "Switch DC ↔ Handover mode in Tag Studio → Pattern Mode buttons.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    DepthDisplayName(1, activeMode), "Tier 1 only — identity / dimensions");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    DepthDisplayName(2, activeMode), "Tiers 1+2 — adds system & function");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    DepthDisplayName(3, activeMode), "Tiers 1+2+3 — adds spatial context");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    "Pick depth 4-10...", deeperHint);
                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                TaskDialogResult result = td.Show();
                switch (result)
                {
                    case TaskDialogResult.CommandLink1: depth = 1; break;
                    case TaskDialogResult.CommandLink2: depth = 2; break;
                    case TaskDialogResult.CommandLink3: depth = 3; break;
                    case TaskDialogResult.CommandLink4:
                        int? picked = PickDepth4To10(activeMode);
                        if (picked == null) return Result.Cancelled;
                        depth = picked.Value;
                        break;
                    default: return Result.Cancelled;
                }
                depthName = DepthDisplayName(depth, activeMode);
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
                scopeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "By discipline...", "Pick a discipline (M/E/P/A/S/...) and apply depth only to its types");
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
                else if (scopeResult == TaskDialogResult.CommandLink3)
                {
                    string disc = PickDiscipline();
                    if (string.IsNullOrEmpty(disc)) return Result.Cancelled;
                    targetTypeIds = CollectTypeIdsForDiscipline(doc, disc);
                    if (targetTypeIds.Count == 0)
                    {
                        TaskDialog.Show("Set Paragraph Depth",
                            $"No element types found for discipline '{disc}'.\n" +
                            "Tag elements first (Auto Tag) so DISC tokens propagate to types.");
                        return Result.Cancelled;
                    }
                    depthName = $"{depthName} (discipline: {disc})";
                }
                else
                {
                    return Result.Cancelled;
                }
            }

            // Phase 165 perf — reuse the cached ParamRegistry.AllParaStates
            // (10-entry static array) instead of allocating a fresh array.
            string[] paraNames = ParamRegistry.AllParaStates;

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
            => PickDepth4To10(ParamRegistry.TagMode.DC);

        // Phase 165 — Issue #12 / #19. Mode-aware list picker. Each row's
        // descriptive label changes per mode so users see the right tier set.
        private static int? PickDepth4To10(ParamRegistry.TagMode mode)
        {
            var items = new List<string>();
            for (int d = 1; d <= 10; d++)
                items.Add($"{d} — {DepthDisplayName(d, mode)}");

            string picked = null;
            try
            {
                picked = StingTools.Select.StingListPicker.Show(
                    $"Set Paragraph Depth ({mode})",
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

        /// <summary>
        /// Phase 165 — Issue #19. Mode-aware depth-label resolver. T1-T3 are
        /// shared between modes; T4-T10 differ:
        ///
        ///   DC mode    T4 = Lifecycle &amp; Status   (TAG7D)
        ///              T5 = Technical Specs       (TAG7E)
        ///              T6 = Classification        (TAG7F)
        ///              T7-T10 = (not used in DC)
        ///
        ///   Handover   T4 = Commissioning         (COMM_*)
        ///              T5 = Cost                  (CST_*)
        ///              T6 = Carbon                (CBN_*)
        ///              T7 = Fabrication           (FAB_*)
        ///              T8 = Clash Triage          (CLH_*)
        ///              T9 = As-Built              (ASB_*)
        ///              T10 = Compliance / Audit   (AUD_*)
        ///
        /// Custom mode mirrors Handover labels but with a "Custom: " prefix
        /// so projects know they're seeing project-defined payload.
        /// </summary>
        public static string DepthDisplayName(int depth, ParamRegistry.TagMode mode)
        {
            // T1-T3 shared.
            switch (depth)
            {
                case 1: return "Compact (T1 — Identity)";
                case 2: return "Standard (T1-T2 — + System & Function)";
                case 3: return "Comprehensive (T1-T3 — + Spatial)";
            }
            if (mode == ParamRegistry.TagMode.DC)
            {
                switch (depth)
                {
                    case 4:  return "T4 — Lifecycle & Status";
                    case 5:  return "T5 — Technical Specs";
                    case 6:  return "T6 — Classification";
                    case 7:
                    case 8:
                    case 9:
                    case 10: return $"T{depth} — (not used in DC mode)";
                }
            }
            else
            {
                string p = mode == ParamRegistry.TagMode.Custom ? "Custom: " : "";
                switch (depth)
                {
                    case 4:  return $"{p}T4 — Commissioning";
                    case 5:  return $"{p}T5 — Cost";
                    case 6:  return $"{p}T6 — Carbon";
                    case 7:  return $"{p}T7 — Fabrication";
                    case 8:  return $"{p}T8 — Clash Triage";
                    case 9:  return $"{p}T9 — As-Built";
                    case 10: return $"{p}T10 — Compliance / Audit";
                }
            }
            return $"Tier {depth}";
        }

        /// <summary>Backward-compatible overload — defaults to DC mode.</summary>
        private static string DepthDisplayName(int depth)
            => DepthDisplayName(depth, ParamRegistry.TagMode.DC);

        // Legacy switch retained below for cases not covered above (defensive).
        private static string DepthDisplayNameLegacy(int depth)
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

        /// <summary>
        /// Discipline-scoped depth: collect every element TYPE that has at
        /// least one tagged INSTANCE whose ASS_DISCIPLINE_COD_TXT matches.
        /// This stops cross-talk between disciplines that share generic tag
        /// families (review fix for token-depth issue #1).
        /// </summary>
        private static ICollection<ElementId> CollectTypeIdsForDiscipline(Document doc, string disc)
        {
            var typeIds = new HashSet<ElementId>();
            foreach (Element inst in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType())
            {
                string d = ParameterHelpers.GetString(inst, ParamRegistry.DISC);
                if (!string.Equals(d, disc, StringComparison.OrdinalIgnoreCase)) continue;
                ElementId tid = inst.GetTypeId();
                if (tid != ElementId.InvalidElementId) typeIds.Add(tid);
            }
            return typeIds;
        }

        /// <summary>
        /// Discipline picker — pulls choices from configured DisciplineProfiles
        /// or the static DISC list when no profiles are configured.
        /// Returns null on cancel.
        /// </summary>
        private static string PickDiscipline()
        {
            var choices = new List<string>();
            if (TagConfig.DisciplineProfiles != null && TagConfig.DisciplineProfiles.Count > 0)
                choices.AddRange(TagConfig.DisciplineProfiles.Keys);
            if (choices.Count == 0)
                choices.AddRange(new[] { "M", "E", "P", "A", "S", "FP", "LV", "G" });
            choices.Sort(StringComparer.OrdinalIgnoreCase);

            try
            {
                string picked = StingTools.Select.StingListPicker.Show(
                    "Set Paragraph Depth — by discipline",
                    "Choose discipline. Depth will apply only to types whose tagged " +
                    "instances carry that DISC token.",
                    choices);
                return picked;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PickDiscipline list picker failed: {ex.Message}");
                return null;
            }
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
