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

            var tb = TitleBlockLock.FindTitleBlock(doc, sheet);
            if (tb == null)
            {
                TaskDialog.Show("STING — Title Block Fields",
                    $"Sheet '{sheet.SheetNumber}' has no title block.");
                return Result.Cancelled;
            }

            var type = doc.GetElement(tb.GetTypeId());
            var rows = new List<string>();
            int hasValue = 0, emptyOnFamily = 0, absent = 0, guidMismatch = 0;
            var mismatches = new List<string>();

            foreach (string name in ParamRegistry.AllTitleBlockParams
                         .Concat(ExtraWatched)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                Parameter p = null;
                string where = null;
                try
                {
                    p = tb.LookupParameter(name);
                    if (p != null) where = "inst";
                    else { p = type?.LookupParameter(name); if (p != null) where = "type"; }
                }
                catch (Exception ex) { StingLog.Warn($"InspectFields '{name}': {ex.Message}"); }

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

                if (string.IsNullOrWhiteSpace(val))
                {
                    emptyOnFamily++;
                    rows.Add($"  {name,-42} [{where}] (empty){guidNote}");
                }
                else
                {
                    hasValue++;
                    rows.Add($"  {name,-42} [{where}] {Trim(val)}{guidNote}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Sheet       : {sheet.SheetNumber} — {sheet.Name}");
            sb.AppendLine($"Title block : {TypeName(doc, tb)}");
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

            sb.AppendLine("Fields:");
            foreach (var r in rows) sb.AppendLine(r);

            StingLog.Info($"TitleBlock_InspectFields '{sheet.SheetNumber}': "
                + $"{hasValue} valued, {emptyOnFamily} empty, {absent} absent, {guidMismatch} GUID mismatch");

            var td = new TaskDialog("STING — Title Block Fields")
            {
                MainInstruction = guidMismatch > 0
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
