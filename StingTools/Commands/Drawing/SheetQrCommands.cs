using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    // ══════════════════════════════════════════════════════════════════════
    //  Sheet QR stamp — commands.
    //
    //  Sheet_StampQR     — active sheet (or the selected sheets)
    //  Sheet_StampQRAll  — every sheet in the project
    //  Sheet_ClearQR     — remove STING QR stamps
    //  Sheet_InspectQR   — read-only: what WOULD happen, and what is there now
    // ══════════════════════════════════════════════════════════════════════

    internal static class SheetQrCommandHelpers
    {
        /// <summary>Sheets to act on: the selection if it holds any, else the active
        /// sheet. Returns empty rather than silently widening to the whole project —
        /// "I selected nothing so it did everything" is a bad surprise on a command
        /// that writes to issued drawings.</summary>
        public static List<ViewSheet> ResolveScope(UIDocument uiDoc, out string scopeLabel)
        {
            var doc = uiDoc.Document;
            var selected = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<ViewSheet>()
                .ToList();
            if (selected.Count > 0)
            {
                scopeLabel = $"{selected.Count} selected sheet(s)";
                return selected;
            }
            if (doc.ActiveView is ViewSheet active)
            {
                scopeLabel = $"active sheet '{active.SheetNumber}'";
                return new List<ViewSheet> { active };
            }
            scopeLabel = "nothing";
            return new List<ViewSheet>();
        }

        public static List<ViewSheet> AllSheets(Document doc)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Report the result. Deliberately distinguishes "did nothing" from
        /// "succeeded" — the defect class this codebase produces is a silent no-op
        /// reported as a success, and an un-stamped issued drawing cannot be recalled.</summary>
        public static void Report(string title, SheetQrResult r, string scopeLabel)
        {
            var body = new System.Text.StringBuilder();
            body.AppendLine($"Scope: {scopeLabel}");
            body.AppendLine();
            body.AppendLine($"Stamped         : {r.Stamped}");
            body.AppendLine($"Payloads written: {r.PayloadsWritten}");
            body.AppendLine($"Images placed   : {r.ImagesPlaced} (replaced {r.ImagesReplaced})");
            body.AppendLine($"Hidden (toggle) : {r.HiddenByToggle}");
            body.AppendLine($"Locked, skipped : {r.LockedSkipped}");
            body.AppendLine($"Failed          : {r.Failed}");

            if (r.Warnings.Count > 0)
            {
                body.AppendLine();
                body.AppendLine($"Notes ({r.Warnings.Count}):");
                foreach (var w in r.Warnings.Take(15)) body.AppendLine("  • " + w);
                if (r.Warnings.Count > 15)
                    body.AppendLine($"  … {r.Warnings.Count - 15} more — see the STING log.");
            }

            string headline =
                r.DidNothing ? "No sheets were stamped"
                : r.Failed > 0 ? $"Stamped {r.Stamped}, {r.Failed} failed"
                : $"Stamped {r.Stamped} sheet(s)";

            TaskDialog.Show(title, headline + "\n\n" + body);
        }
    }

    /// <summary>Stamp the QR on the active / selected sheets.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetStampQrCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = ParameterHelpers.GetApp(commandData);
                var uiDoc = uiApp?.ActiveUIDocument;
                if (uiDoc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Failed;
                }

                var sheets = SheetQrCommandHelpers.ResolveScope(uiDoc, out var scope);
                if (sheets.Count == 0)
                {
                    TaskDialog.Show("STING — Sheet QR",
                        "Open a sheet, or select sheets in the Project Browser, then run this again.\n\n" +
                        "Use 'Stamp QR (all sheets)' to do the whole project.");
                    return Result.Cancelled;
                }

                SheetQrResult r;
                using (var t = new Transaction(uiDoc.Document, "STING Stamp Sheet QR"))
                {
                    t.Start();
                    r = SheetQrStamper.Stamp(uiDoc.Document, sheets);
                    t.Commit();
                }

                SheetQrCommandHelpers.Report("STING — Sheet QR", r, scope);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SheetStampQrCommand failed", ex);
                TaskDialog.Show("STING", $"Sheet QR stamp failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>Stamp the QR on every sheet in the project.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetStampQrAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = ParameterHelpers.GetApp(commandData);
                var doc = uiApp?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("STING", "No active document.");
                    return Result.Failed;
                }

                var sheets = SheetQrCommandHelpers.AllSheets(doc);
                if (sheets.Count == 0)
                {
                    TaskDialog.Show("STING — Sheet QR", "This project has no sheets.");
                    return Result.Cancelled;
                }

                SheetQrResult r;
                using (var t = new Transaction(doc, "STING Stamp Sheet QR (all)"))
                {
                    t.Start();
                    r = SheetQrStamper.Stamp(doc, sheets);
                    t.Commit();
                }

                SheetQrCommandHelpers.Report("STING — Sheet QR (all)", r, $"all {sheets.Count} sheet(s)");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SheetStampQrAllCommand failed", ex);
                TaskDialog.Show("STING", $"Sheet QR stamp failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>Remove STING QR stamps from the active / selected sheets. Matches on
    /// the "STING QR - " image-type prefix only; an operator's own images are never
    /// touched.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetClearQrCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = ParameterHelpers.GetApp(commandData);
                var uiDoc = uiApp?.ActiveUIDocument;
                if (uiDoc == null) { TaskDialog.Show("STING", "No active document."); return Result.Failed; }
                var doc = uiDoc.Document;

                var sheets = SheetQrCommandHelpers.ResolveScope(uiDoc, out var scope);
                if (sheets.Count == 0)
                {
                    TaskDialog.Show("STING — Clear Sheet QR", "Open or select the sheets to clear.");
                    return Result.Cancelled;
                }

                int cleared = 0, payloadsCleared = 0;
                using (var t = new Transaction(doc, "STING Clear Sheet QR"))
                {
                    t.Start();
                    foreach (var sheet in sheets)
                    {
                        var doomed = new FilteredElementCollector(doc, sheet.Id)
                            .OfClass(typeof(ImageInstance))
                            .Cast<ImageInstance>()
                            .Where(i => (doc.GetElement(i.GetTypeId())?.Name ?? "")
                                .StartsWith(SheetQrStamper.ImageNamePrefix, StringComparison.Ordinal))
                            .Select(i => i.Id)
                            .ToList();
                        if (doomed.Count > 0) { doc.Delete(doomed); cleared += doomed.Count; }

                        var tb = new FilteredElementCollector(doc, sheet.Id)
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .WhereElementIsNotElementType()
                            .FirstElement();
                        var p = tb?.LookupParameter(ParamRegistry.TB_QR_PAYLOAD);
                        if (p != null && !p.IsReadOnly) { p.Set(string.Empty); payloadsCleared++; }
                    }
                    t.Commit();
                }

                TaskDialog.Show("STING — Clear Sheet QR",
                    $"Cleared {cleared} QR image(s) and {payloadsCleared} payload value(s) across {scope}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SheetClearQrCommand failed", ex);
                TaskDialog.Show("STING", $"Clear failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>Read-only: report what the QR chain would do for every sheet, and
    /// what is actually on them now. Exists because the whole feature was documented
    /// as shipped while nothing implemented it — this answers "is it really there?"
    /// against the model rather than against a doc.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetInspectQrCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = ParameterHelpers.GetApp(commandData);
                var doc = uiApp?.ActiveUIDocument?.Document;
                if (doc == null) { TaskDialog.Show("STING", "No active document."); return Result.Failed; }

                var sheets = SheetQrCommandHelpers.AllSheets(doc);
                string projectCode = SheetQrStamper.ResolveProjectCode(doc);

                int withImage = 0, withPayload = 0, noToggle = 0, noPayloadParam = 0, noTitleBlock = 0;
                var sample = new List<string>();

                foreach (var sheet in sheets)
                {
                    var tb = new FilteredElementCollector(doc, sheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsNotElementType()
                        .FirstElement();
                    if (tb == null) { noTitleBlock++; continue; }

                    if (tb.LookupParameter(ParamRegistry.TB_SHOW_QR_CODE) == null) noToggle++;

                    var p = tb.LookupParameter(ParamRegistry.TB_QR_PAYLOAD);
                    if (p == null) noPayloadParam++;
                    else if (!string.IsNullOrWhiteSpace(p.AsString())) withPayload++;

                    bool hasImage = new FilteredElementCollector(doc, sheet.Id)
                        .OfClass(typeof(ImageInstance))
                        .Cast<ImageInstance>()
                        .Any(i => (doc.GetElement(i.GetTypeId())?.Name ?? "")
                            .StartsWith(SheetQrStamper.ImageNamePrefix, StringComparison.Ordinal));
                    if (hasImage) withImage++;

                    if (sample.Count < 10)
                        sample.Add($"  {sheet.SheetNumber,-14} image:{(hasImage ? "yes" : "no ")}  " +
                                   $"payload:{(p == null ? "n/a" : string.IsNullOrWhiteSpace(p.AsString()) ? "empty" : "set")}");
                }

                var body = new System.Text.StringBuilder();
                body.AppendLine($"Project code : {projectCode}");
                body.AppendLine($"Sheets       : {sheets.Count}");
                body.AppendLine();
                body.AppendLine($"QR image present      : {withImage}");
                body.AppendLine($"{ParamRegistry.TB_QR_PAYLOAD} set : {withPayload}");
                body.AppendLine();
                body.AppendLine($"No title block                     : {noTitleBlock}");
                body.AppendLine($"Title block lacks the SHOW toggle  : {noToggle}");
                body.AppendLine($"Title block lacks the payload param: {noPayloadParam}");
                if (noToggle > 0 || noPayloadParam > 0)
                    body.AppendLine("\n  → run TitleBlock_CreateAll to regenerate those families.");
                body.AppendLine();
                body.AppendLine("Example URL for the first sheet:");
                body.AppendLine("  " + (sheets.Count > 0
                    ? StingQrFormat.BuildSheetUrl(projectCode, sheets[0].SheetNumber)
                    : "(no sheets)"));
                if (sample.Count > 0)
                {
                    body.AppendLine();
                    body.AppendLine("First sheets:");
                    foreach (var s in sample) body.AppendLine(s);
                }

                TaskDialog.Show("STING — Sheet QR status", body.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SheetInspectQrCommand failed", ex);
                TaskDialog.Show("STING", $"Inspect failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
