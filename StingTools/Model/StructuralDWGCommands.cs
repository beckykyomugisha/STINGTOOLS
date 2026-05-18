// ============================================================================
// StructuralDWGCommands.cs — Phase-141 standalone commands wrapping
// StructuralDWGEngine for batch / scriptable use.
//
// CLAUDE.md mentioned this file ("2 commands: StructuralDWGWizard +
// QuickStructuralDWG") but it didn't exist on disk. The
// StructuralDWGWizard slot is already filled by `StrCADWizardCommand` in
// StructuralModelingCommands.cs:1009 — duplicating it would just confuse
// the dispatcher. This file therefore lands the genuinely-new commands:
//
//   - QuickStructuralDWGCommand        — one-click conversion (no wizard)
//                                       on the first DWG import in the doc
//                                       using default config + base level.
//   - StructuralDWGAuditCommand        — non-destructive audit; reports
//                                       extraction summary + quality score.
//   - StructuralDWGJunctionScanCommand — runs DetectJunctions over the
//                                       active DWG and places ⚠ TextNotes
//                                       at every unsupported intersection
//                                       and free beam end.
//
// All three operate on the FIRST `ImportInstance` found in the document
// (matching the pattern used by `StrCADWizardCommand`); a future
// enhancement will let users pick which import.
// ============================================================================

using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Model
{
    /// <summary>
    /// Phase-141 — Quick structural DWG-to-BIM conversion. Bypasses the wizard
    /// and runs the pipeline with default config on the first imported DWG.
    /// Useful for scriptable / repeated workflows where the user has already
    /// configured their layers and just wants to re-convert.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class QuickStructuralDWGCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var import = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>().FirstOrDefault();
                if (import == null)
                {
                    TaskDialog.Show("STRUCT — Quick DWG-to-BIM",
                        "No DWG import found in the project. Import a structural DWG first.");
                    return Result.Cancelled;
                }

                // Resolve a default base level — first level by elevation.
                var baseLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).FirstOrDefault();

                var engine = new StructuralDWGEngine(doc);
                var result = engine.RunWithDefaults(import, baseLevel?.Name);

                var dlg = new TaskDialog("STRUCT — Quick DWG-to-BIM Result")
                {
                    MainInstruction = result.Success
                        ? $"Created {result.TotalCreated} element(s)"
                        : "Conversion finished with errors",
                    MainContent = result.Summary,
                    ExpandedContent = result.Warnings.Count > 0
                        ? string.Join(Environment.NewLine, result.Warnings.Take(50))
                        : null,
                };
                dlg.Show();

                return result.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error("QuickStructuralDWGCommand", ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Phase-141 — Non-destructive audit. Runs detection + quality scoring
    /// without writing anything to the model. Useful for sanity-checking a
    /// DWG before committing to conversion, or for QA reporting.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class StructuralDWGAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var import = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>().FirstOrDefault();
                if (import == null)
                {
                    TaskDialog.Show("STRUCT — DWG Audit",
                        "No DWG import found in the project.");
                    return Result.Cancelled;
                }

                var engine = new StructuralDWGEngine(doc);
                var audit = engine.Audit(import);

                var dlg = new TaskDialog("STRUCT — DWG-to-BIM Audit")
                {
                    MainInstruction = $"Quality score: {audit.QualityScore.Total:F1} / 100",
                    MainContent = audit.FormatSummary(),
                };
                dlg.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error("StructuralDWGAuditCommand", ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Phase-141 — Run DetectJunctions across the active DWG import and place
    /// "⚠ STING-STRUCT" TextNote markers in the active view at every
    /// unsupported beam intersection and free beam end. Surfaces the data
    /// the legacy pipeline computed but only used for the summary string.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StructuralDWGJunctionScanCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                if (doc.ActiveView == null)
                {
                    TaskDialog.Show("STRUCT — Junction Scan",
                        "No active view — open a plan view first so warnings can be placed.");
                    return Result.Cancelled;
                }

                var import = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>().FirstOrDefault();
                if (import == null)
                {
                    TaskDialog.Show("STRUCT — Junction Scan",
                        "No DWG import found in the project.");
                    return Result.Cancelled;
                }

                var engine = new StructuralDWGEngine(doc);
                var extraction = engine.ExtractAll(import);
                var junctions = engine.DetectJunctions(extraction);

                int unsupported = 0, freeEnds = 0;
                var warningMessages = new System.Collections.Generic.List<string>();
                var warningPoints = new System.Collections.Generic.List<XYZ>();
                foreach (var (pt, jType, beamCount) in junctions)
                {
                    if (jType == null) continue;
                    if (jType.IndexOf("WARNING", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        warningMessages.Add($"{jType} ({beamCount} beam(s))");
                        warningPoints.Add(pt);
                        unsupported++;
                    }
                    else if (jType.StartsWith("Free end", StringComparison.OrdinalIgnoreCase))
                    {
                        warningMessages.Add($"{jType} ({beamCount} beam(s))");
                        warningPoints.Add(pt);
                        freeEnds++;
                    }
                }

                int placed = 0;
                if (warningMessages.Count > 0)
                {
                    placed = StructuralWarningPlacer.PlaceWarningsAtPoints(
                        doc, doc.ActiveView, warningMessages, warningPoints);
                }

                TaskDialog.Show("STRUCT — Junction Scan",
                    $"Detected {junctions.Count} junction(s).\n" +
                    $"  • Unsupported intersections: {unsupported}\n" +
                    $"  • Free beam ends:            {freeEnds}\n" +
                    $"  • Warning notes placed:      {placed}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                StingLog.Error("StructuralDWGJunctionScanCommand", ex);
                return Result.Failed;
            }
        }
    }
}
