#nullable enable annotations
// StingTools — StabilizeIfcGuidsCommand.cs (Gap 3 — GlobalId stability)
//
// Before exporting to IFC, Revit re-generates IfcGloballyUniqueId values
// unless the model has been stable across sessions. This command persists
// each element's current IfcGloballyUniqueId into a STING shared parameter
// (IFC_GLOBAL_ID_TXT) so that Planscape's ElementGlobalIdRegistry can
// detect drift between uploads.
//
// Run this command once before the first IFC push, and again after any
// significant model restructure. Planscape IfcAlignmentValidator reports
// GLOBALID_DRIFT when more than 5 % of known elements change their GUIDs.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Interop
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StabilizeIfcGuidsCommand : IExternalCommand
    {
        // Revit built-in parameter names for IFC GUIDs (varies by version).
        private static readonly string[] IfcGuidParamNames =
        {
            "IfcGUID",
            "IFC GUID",
            "IFC_GUID",
        };

        // STING shared parameter that persists the stable GUID.
        private const string StingIfcGuidParam = "IFC_GLOBAL_ID_TXT";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) return Result.Failed;
            var doc = ctx.Doc;

            // Collect all model elements (not element types, not annotation).
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            // Pre-check: can we even write IFC_GLOBAL_ID_TXT on any element?
            // If the shared param is not bound, we skip silently and report.
            int written = 0, skippedNoParam = 0, skippedReadOnly = 0, unchanged = 0, skippedNotExported = 0;
            int total = 0;
            var conflicts = new List<string>(); // existing GUID ≠ current Revit GUID

            var progress = StingTools.UI.StingProgressDialog.Show(
                "STING — Stabilize IFC GUIDs", collector.Count);
            try
            {
                int n = 0;
                using (var tx = new Transaction(doc, "STING Stabilize IFC GUIDs"))
                {
                    tx.Start();

                    foreach (var el in collector)
                    {
                        if (EscapeChecker.IsEscapePressed()) break;
                        if (++n % 200 == 0)
                            progress.Increment($"Processing {n}/{collector.Count}…");

                        // Skip view-specific and annotation elements.
                        if (el.Category == null) continue;
                        var bic = el.Category.BuiltInCategory;
                        if (bic == BuiltInCategory.OST_Cameras  ||
                            bic == BuiltInCategory.OST_Views    ||
                            bic == BuiltInCategory.OST_Sheets   ||
                            bic == BuiltInCategory.OST_Grids    ||
                            bic == BuiltInCategory.OST_Levels)
                            continue;

                        // Read the current Revit-side IfcGUID.
                        string? revitIfcGuid = ReadRevitIfcGuid(el);
                        if (string.IsNullOrEmpty(revitIfcGuid))
                        {
                            skippedNotExported++;
                            continue;
                        }
                        total++;

                        // Write into IFC_GLOBAL_ID_TXT.
                        var stingParam = el.LookupParameter(StingIfcGuidParam);
                        if (stingParam == null)
                        {
                            skippedNoParam++;
                            continue;
                        }
                        if (stingParam.IsReadOnly)
                        {
                            skippedReadOnly++;
                            continue;
                        }

                        string existing = stingParam.AsString() ?? "";
                        if (existing == revitIfcGuid)
                        {
                            unchanged++;
                            continue;
                        }

                        // Record a conflict for reporting (previous stable GUID differs).
                        if (!string.IsNullOrEmpty(existing) && existing != revitIfcGuid)
                            conflicts.Add($"  [{el.Id.Value}] {el.Category?.Name ?? "?"}: " +
                                          $"old={existing} → new={revitIfcGuid}");

                        stingParam.Set(revitIfcGuid);
                        written++;
                    }

                    tx.Commit();
                }
            }
            finally { progress.Close(); }

            // ── Report ──────────────────────────────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine($"IFC GlobalId stabilisation complete.");
            sb.AppendLine();
            sb.AppendLine($"Elements scanned : {total}");
            sb.AppendLine($"GUIDs written    : {written}");
            sb.AppendLine($"Already current  : {unchanged}");
            if (skippedNoParam > 0)
                sb.AppendLine($"No STING param   : {skippedNoParam}  (bind IFC_GLOBAL_ID_TXT shared param first)");
            if (skippedReadOnly > 0)
                sb.AppendLine($"Read-only skipped: {skippedReadOnly}");
            if (skippedNotExported > 0)
                sb.AppendLine($"Not yet exported : {skippedNotExported}  (run IFC export once to assign GUIDs)");

            if (conflicts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"GUID drift detected — {conflicts.Count} element(s) changed since last stabilise:");
                foreach (var c in conflicts.Take(15)) sb.AppendLine(c);
                if (conflicts.Count > 15)
                    sb.AppendLine($"  … and {conflicts.Count - 15} more");
                sb.AppendLine();
                sb.AppendLine("Planscape will flag these as GLOBALID_DRIFT in the next IFC upload.");
            }

            if (skippedNoParam == total && total > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ACTION REQUIRED: No elements had the IFC_GLOBAL_ID_TXT shared parameter.");
                sb.AppendLine("Run 'TEMP → Load Params' to bind it before re-running this command.");
            }

            TaskDialog.Show("STING — Stabilize IFC GUIDs", sb.ToString());
            return Result.Succeeded;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string? ReadRevitIfcGuid(Element el)
        {
            foreach (string name in IfcGuidParamNames)
            {
                try
                {
                    var p = el.LookupParameter(name);
                    if (p != null)
                    {
                        string v = p.AsString();
                        if (!string.IsNullOrWhiteSpace(v)) return v;
                    }
                }
                catch { /* parameter not accessible on this element */ }
            }

            // No IFC GUID parameter found — element has never been exported to IFC.
            // Return null so the caller skips it rather than storing a Revit UniqueId
            // (which is NOT the 22-character IFC GloballyUniqueId and would cause
            // Planscape drift detection to compare apples to oranges).
            return null;
        }
    }
}
