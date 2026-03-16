using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Sets paragraph depth on element types (TAG_PARA_STATE_1/2/3_BOOL).
    /// Three mutually exclusive modes:
    ///   State 1 (Compact) — tag shows tier 1 data only
    ///   State 2 (Standard) — tag shows tiers 1+2
    ///   State 3 (Comprehensive) — tag shows tiers 1+2+3
    /// These are Type parameters that control calculated value visibility
    /// in Revit tag family labels (Edit Label).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetParagraphDepthCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            // Ask user for depth level
            TaskDialog td = new TaskDialog("Set Paragraph Depth");
            td.MainInstruction = "Select paragraph depth for tag descriptions";
            td.MainContent =
                "Controls how much detail is shown in Tag 7 paragraph containers.\n" +
                "This sets Type parameters — all instances of the same type will be affected.";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Compact (State 1)", "Tier 1 only — basic identity and dimensions");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Standard (State 2)", "Tiers 1+2 — adds materials, thermal, acoustic data");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Comprehensive (State 3)", "Tiers 1+2+3 — full specification with regulatory, sustainability, QA");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult result = td.Show();

            bool s1 = false, s2 = false, s3 = false;
            string depthName;
            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    s1 = true; depthName = "Compact (State 1)"; break;
                case TaskDialogResult.CommandLink2:
                    s1 = true; s2 = true; depthName = "Standard (State 2)"; break;
                case TaskDialogResult.CommandLink3:
                    s1 = true; s2 = true; s3 = true; depthName = "Comprehensive (State 3)"; break;
                default:
                    return Result.Cancelled;
            }

            // Ask scope
            TaskDialog scopeTd = new TaskDialog("Scope");
            scopeTd.MainInstruction = "Apply to which elements?";
            scopeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Selected elements", "Set depth on types of selected elements");
            scopeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "All element types in project", "Set depth on all element types");
            scopeTd.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult scopeResult = scopeTd.Show();

            ICollection<ElementId> targetTypeIds;
            if (scopeResult == TaskDialogResult.CommandLink1)
            {
                var sel = uidoc.Selection.GetElementIds();
                if (sel.Count == 0)
                {
                    TaskDialog.Show("Set Paragraph Depth", "No elements selected.");
                    return Result.Cancelled;
                }
                targetTypeIds = new HashSet<ElementId>(
                    sel.Select(id => doc.GetElement(id))
                       .Where(e => e != null)
                       .Select(e => e.GetTypeId())
                       .Where(id => id != ElementId.InvalidElementId));
            }
            else if (scopeResult == TaskDialogResult.CommandLink2)
            {
                targetTypeIds = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .Select(e => e.Id)
                    .ToList();
            }
            else
            {
                return Result.Cancelled;
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Set Paragraph Depth"))
            {
                tx.Start();

                foreach (ElementId typeId in targetTypeIds)
                {
                    Element typeEl = doc.GetElement(typeId);
                    if (typeEl == null) continue;

                    bool anySet = false;
                    anySet |= SetYesNo(typeEl, ParamRegistry.PARA_STATE_1, s1);
                    anySet |= SetYesNo(typeEl, ParamRegistry.PARA_STATE_2, s2);
                    anySet |= SetYesNo(typeEl, ParamRegistry.PARA_STATE_3, s3);

                    if (anySet) updated++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Set Paragraph Depth",
                $"Paragraph depth set to: {depthName}\n" +
                $"Element types updated: {updated}");

            StingLog.Info($"Paragraph depth set to {depthName} on {updated} types");
            return Result.Succeeded;
        }

        private static bool SetYesNo(Element el, string paramName, bool value)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;
            if (p.StorageType == StorageType.Integer)
            {
                p.Set(value ? 1 : 0);
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
