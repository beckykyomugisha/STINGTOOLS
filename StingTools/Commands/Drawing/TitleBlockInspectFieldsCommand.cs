// StingTools — Drawing Template Manager · title-block field inspector
//
// WHY THIS FILE EXISTS
// --------------------
// Populate reported "3 sheets updated, 8 fields each" and Count Sheets reported
// "3 pagination cells written", while the CDE REF and SHEET cells on the drawing
// still rendered "?". Both commands were telling the truth: SetString found the
// parameter and wrote it. The label still showed nothing.
//
// Two different things can cause that, and they need opposite fixes:
//
//   1. NO LABEL. The family never displays that parameter. The value is stored
//      and correct; nothing draws it. Fix: add the label to the seed .rfa.
//      TitleBlockCreateAll already warns about this ("seed carries 23 label(s)
//      but the spec declares 73") but the warning names 50 parameters at once
//      and scrolls past.
//
//   2. TWO PARAMETERS, ONE NAME. A shared parameter is identified by GUID, not
//      by name. If the family's label binds to a parameter named
//      PRJ_TB_DELIVERABLE_CDE_TXT with one GUID, and the plugin writes the one
//      declared in MR_PARAMETERS.txt with a DIFFERENT GUID, then both exist,
//      the write succeeds, and the label reads the other one — permanently
//      blank. This happens when a family was authored against an older or
//      hand-made shared-parameter file. LoadSharedParams already refuses this
//      case at the project level (nameOwnedByOtherGuid); inside a family
//      nothing was checking.
//
// Guessing between the two costs an afternoon. This reports which.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockInspectFieldsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            var doc = uiApp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING — Title Block Fields", "No active document.");
                return Result.Failed;
            }

            var sheet = doc.ActiveView as ViewSheet;
            if (sheet == null)
            {
                TaskDialog.Show("STING — Title Block Fields",
                    "Open a SHEET first — this reports the title block on the active sheet.");
                return Result.Cancelled;
            }

            // EVERY title block on the sheet, not just the first.
            //
            // Populate, Count Sheets, the QR stamper and three more all resolve "the
            // title block" as the FIRST element the collector yields, and collector
            // order is not a documented guarantee. If a sheet carries two -- an old
            // one left behind by a swap, or a second dragged on by accident -- then
            // one command can write to one and the drawing can display the other.
            // Every write reports success and the cell stays blank, which is exactly
            // the symptom this inspector was built for and the one thing it could not
            // see when it only looked at the first.
            var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToList();

            if (titleBlocks.Count == 0)
            {
                TaskDialog.Show("STING — Title Block Fields",
                    $"Sheet '{sheet.SheetNumber}' has no title block.");
                return Result.Cancelled;
            }

            var tb = titleBlocks[0];

            var type = doc.GetElement(tb.GetTypeId());
            var rows = new List<string>();
            int hasValue = 0, emptyOnFamily = 0, absent = 0, guidMismatch = 0;
            int alsoOnSheet = 0, readOnly = 0;
            var mismatches = new List<string>();
            var sheetHomes = new List<string>();

            foreach (string name in ParamRegistry.AllTitleBlockParams
                         .Concat(ExtraWatched)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                // THREE homes, not one. A name can exist on the sheet (a project
                // parameter), on the title-block instance, and on its type, all at
                // once and all different. The label in the .rfa binds to exactly one
                // of them; every STING command writes to the title-block instance. If
                // those are not the same home, the write succeeds and the drawing
                // never changes -- which is the symptom that sent us round four
                // theories, and the one thing this could not see while it looked at
                // the title block alone.
                Parameter p = null, onSheet = null;
                string where = null;
                try { onSheet = sheet.LookupParameter(name); }
                catch (Exception ex) { StingLog.Warn($"InspectFields sheet '{name}': {ex.Message}"); }
                try
                {
                    p = tb.LookupParameter(name);
                    if (p != null) where = "inst";
                    else { p = type?.LookupParameter(name); if (p != null) where = "type"; }
                }
                catch (Exception ex) { StingLog.Warn($"InspectFields '{name}': {ex.Message}"); }

                if (onSheet != null)
                {
                    string sv = null;
                    try { sv = onSheet.StorageType == StorageType.Integer
                                ? onSheet.AsInteger().ToString() : onSheet.AsString(); }
                    catch (Exception ex) { StingLog.Warn($"InspectFields sheet read '{name}': {ex.Message}"); }
                    alsoOnSheet++;
                    sheetHomes.Add($"  {name,-42} sheet holds: "
                        + (string.IsNullOrWhiteSpace(sv) ? "(empty)" : Trim(sv))
                        + (p != null ? "   — AND the title block has its own copy" : ""));
                }

                if (p == null)
                {
                    absent++;
                    rows.Add($"  {name,-42} ABSENT from the family");
                    continue;
                }

                string val = null;
                try { val = p.StorageType == StorageType.Integer
                            ? p.AsInteger().ToString()
                            : p.AsString(); }
                catch (Exception ex) { StingLog.Warn($"InspectFields read '{name}': {ex.Message}"); }

                // The GUID check is the whole point. A shared parameter is its GUID;
                // the name is a label on top. Two with one name is invisible in every
                // UI and explains "the write worked and the sheet is still blank".
                string guidNote = "";
                try
                {
                    if (p.IsShared)
                    {
                        var expected = ParamRegistry.GetGuid(name);
                        if (expected != Guid.Empty && p.GUID != expected)
                        {
                            guidMismatch++;
                            guidNote = "  << GUID MISMATCH";
                            mismatches.Add($"  {name}\n      family has {p.GUID}\n      STING writes {expected}");
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"InspectFields guid '{name}': {ex.Message}"); }

                // "shared" vs "family": a LABEL bound to a shared parameter and a
                // family parameter of the same name are different things, and only one
                // of them is what gets written.
                string kind = "";
                try { kind = p.IsShared ? " {shared}" : " {family}"; }
                catch (Exception ex) { StingLog.Warn($"InspectFields kind '{name}': {ex.Message}"); }
                string ro = "";
                try { if (p.IsReadOnly) { readOnly++; ro = " READ-ONLY"; } }
                catch (Exception ex) { StingLog.Warn($"InspectFields readonly '{name}': {ex.Message}"); }

                if (string.IsNullOrWhiteSpace(val))
                {
                    emptyOnFamily++;
                    rows.Add($"  {name,-42} [{where}]{kind}{ro} (empty){guidNote}");
                }
                else
                {
                    hasValue++;
                    rows.Add($"  {name,-42} [{where}]{kind}{ro} {Trim(val)}{guidNote}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Sheet       : {sheet.SheetNumber} — {sheet.Name}");
            sb.AppendLine($"Title block : {TypeName(doc, tb)}  [id {tb.Id}]");
            if (titleBlocks.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine($"*** THIS SHEET CARRIES {titleBlocks.Count} TITLE BLOCKS ***");
                sb.AppendLine("Every command that writes to \"the\" title block takes the FIRST one the");
                sb.AppendLine("collector yields, and that order is not guaranteed. So a write can land");
                sb.AppendLine("on one while the drawing shows the other -- the write reports success and");
                sb.AppendLine("the cell stays blank. If a field below holds a value you cannot see on");
                sb.AppendLine("the sheet, this is almost certainly why.");
                sb.AppendLine();
                foreach (var extra in titleBlocks)
                    sb.AppendLine($"    id {extra.Id,-12} {TypeName(doc, extra)}");
                sb.AppendLine();
                sb.AppendLine("Delete the one that is not wanted, then re-run Populate.");
                sb.AppendLine("Fields below are read from the FIRST one only (id " + tb.Id + ").");
            }
            sb.AppendLine();
            sb.AppendLine($"Carrying a value : {hasValue}");
            sb.AppendLine($"Present but empty: {emptyOnFamily}");
            sb.AppendLine($"Absent entirely  : {absent}");
            sb.AppendLine($"GUID mismatches  : {guidMismatch}");
            sb.AppendLine();

            if (guidMismatch > 0)
            {
                // This is the answer to "it says it wrote and the sheet is blank".
                sb.AppendLine("A GUID MISMATCH means the family's label and this plugin are using");
                sb.AppendLine("TWO DIFFERENT parameters that happen to share a name. Every write");
                sb.AppendLine("succeeds and the cell stays blank forever, because the label is");
                sb.AppendLine("reading the other one.");
                sb.AppendLine();
                sb.AppendLine("Fix it in the seed .rfa: delete the family's copy, then re-add the");
                sb.AppendLine("parameter from StingTools' own MR_PARAMETERS.txt and re-bind the");
                sb.AppendLine("label to it.");
                sb.AppendLine();
                foreach (var m in mismatches.Take(10)) sb.AppendLine(m);
                sb.AppendLine();
            }
            else if (hasValue > 0 && emptyOnFamily + absent > 0)
            {
                sb.AppendLine("No GUID mismatch. So a cell that prints \"?\" while this list shows a");
                sb.AppendLine("value means the family has NO LABEL drawing that parameter — the value");
                sb.AppendLine("is stored correctly and nothing displays it. Add the label to the");
                sb.AppendLine("seed .rfa (Annotate → Label) and rebuild.");
                sb.AppendLine();
            }

            if (alsoOnSheet > 0)
            {
                sb.AppendLine($"*** {alsoOnSheet} of these names ALSO exist on the SHEET ***");
                sb.AppendLine("A label in the .rfa binds to ONE parameter. Every STING command writes");
                sb.AppendLine("to the title-block instance. Where a name lives in both places, the");
                sb.AppendLine("label may be reading the sheet's copy while the writes land on the");
                sb.AppendLine("title block's — the write succeeds and the drawing never changes.");
                sb.AppendLine();
                foreach (var h in sheetHomes) sb.AppendLine(h);
                sb.AppendLine();
            }
            if (readOnly > 0)
            {
                sb.AppendLine($"{readOnly} field(s) are READ-ONLY on this title block and can never be");
                sb.AppendLine("written by any command — a formula or a reporting parameter drives them.");
                sb.AppendLine();
            }
            sb.AppendLine("Fields:   [where] {shared|family}  value");
            foreach (var r in rows) sb.AppendLine(r);

            StingLog.Info($"TitleBlock_InspectFields '{sheet.SheetNumber}': "
                + $"{hasValue} valued, {emptyOnFamily} empty, {absent} absent, {guidMismatch} GUID mismatch");

            var td = new TaskDialog("STING — Title Block Fields")
            {
                MainInstruction = titleBlocks.Count > 1
                    ? $"{titleBlocks.Count} title blocks on this sheet — writes may be landing on the wrong one"
                    : guidMismatch > 0
                        ? $"{guidMismatch} parameter(s) are duplicated under one name"
                        : $"{hasValue} of {rows.Count} fields carry a value",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Ok,
            };
            td.Show();
            return Result.Succeeded;
        }

        /// <summary>Label parameters that are NOT in AllTitleBlockParams (which is
        /// only the 20 the CSV drives) but are printed on the sheet, so a blank one
        /// is just as visible. These are the cells this inspector exists for.</summary>
        private static readonly string[] ExtraWatched =
        {
            "PRJ_SHEET_OF_TOTAL_TXT",
            "PRJ_DWG_SUITABILITY_COD_TXT",
            "PRJ_DWG_SUITABILITY_DESC_TXT",
            "PRJ_DWG_LOIN_LOD_TXT",
            "PRJ_TB_CDE_REF_TXT",
            "PRJ_DWG_ISSUE_PURPOSE_TXT",
            "PRJ_TB_FEDERATION_STATUS_TXT",
            "PRJ_TB_PAPER_SZ_TXT",
            "PRJ_SHEET_FULL_REF_TXT",
            "PRJ_SHEET_SYSTEM_TXT",
        };

        private static string TypeName(Document doc, Element tb)
        {
            try { return doc.GetElement(tb.GetTypeId())?.Name ?? "(unnamed type)"; }
            catch (Exception ex) { StingLog.Warn($"InspectFields type name: {ex.Message}"); return "(unknown)"; }
        }

        private static string Trim(string s) =>
            s.Length <= 40 ? s : s.Substring(0, 40) + "…";
    }
}
