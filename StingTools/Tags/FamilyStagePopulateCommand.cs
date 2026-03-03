using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Family-Stage Pre-Population: the "before tagging" intelligence layer.
    ///
    /// The highest-logic approach to tagging is to ensure ALL token values are
    /// correct BEFORE tag generation. This command pre-populates:
    ///   - DISC from element category (deterministic — never wrong)
    ///   - LOC from spatial context (room, project info, workset)
    ///   - ZONE from room department/name/workset
    ///   - LVL from element level (deterministic)
    ///   - SYS from category → system mapping (6-layer MEP-aware)
    ///   - FUNC from system → function mapping (smart subsystem differentiation)
    ///   - PROD from family name (specific) or category (generic)
    ///   - STATUS from Revit phase/workset (EXISTING, NEW, DEMOLISHED, TEMPORARY)
    ///   - REV from project revision sequence
    ///
    /// After this runs, tagging is purely mechanical: just concatenate tokens + assign SEQ.
    /// No collisions from wrong tokens, no ISO violations from invalid codes.
    ///
    /// This is designed to run at the "family stage" — after elements are placed but
    /// before any tagging occurs. It's the foundation for zero-touch automation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyStagePopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Scope selection
            TaskDialog scopeDlg = new TaskDialog("Family-Stage Populate");
            scopeDlg.MainInstruction = "Pre-populate all tokens at family stage";
            scopeDlg.MainContent =
                "This ensures every token is correct BEFORE tagging.\n" +
                "After this, tag generation is pure concatenation — no guessing.\n\n" +
                "Tokens populated: DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV\n" +
                "(SEQ is NOT assigned — that happens during tagging)";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Selected elements only",
                $"{uidoc.Selection.GetElementIds().Count} elements selected");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Active view",
                "Pre-populate all taggable elements in this view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Entire project",
                "Pre-populate every taggable element in the model");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            ICollection<ElementId> targetIds;
            string scopeLabel;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targetIds = uidoc.Selection.GetElementIds();
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("Family-Stage Populate", "No elements selected.");
                        return Result.Cancelled;
                    }
                    scopeLabel = $"{targetIds.Count} selected elements";
                    break;
                case TaskDialogResult.CommandLink2:
                    if (doc.ActiveView == null) { TaskDialog.Show("Populate", "No active view."); return Result.Failed; }
                    targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    scopeLabel = $"active view '{doc.ActiveView.Name}'";
                    break;
                case TaskDialogResult.CommandLink3:
                    targetIds = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    scopeLabel = "entire project";
                    break;
                default:
                    return Result.Cancelled;
            }

            // Override mode
            TaskDialog overDlg = new TaskDialog("Token Write Mode");
            overDlg.MainInstruction = "How to handle existing token values?";
            overDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Fill empty only (safe)",
                "Only populate tokens that are currently empty — existing values untouched");
            overDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Overwrite all (force)",
                "Re-derive all tokens from scratch, overwriting any existing values");
            overDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            bool overwrite;
            switch (overDlg.Show())
            {
                case TaskDialogResult.CommandLink1: overwrite = false; break;
                case TaskDialogResult.CommandLink2: overwrite = true; break;
                default: return Result.Cancelled;
            }

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            var sw = Stopwatch.StartNew();

            // Pre-detect project-level values once (not per-element)
            string projectRev = PhaseAutoDetect.DetectProjectRevision(doc);

            int processed = 0;
            int discSet = 0, locSet = 0, zoneSet = 0, lvlSet = 0;
            int sysSet = 0, funcSet = 0, prodSet = 0, statusSet = 0, revSet = 0;
            int familyProdUsed = 0;
            int phaseDetected = 0;

            using (Transaction tx = new Transaction(doc, "STING Family-Stage Populate"))
            {
                tx.Start();

                foreach (ElementId id in targetIds)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !known.Contains(catName))
                        continue;

                    processed++;

                    try
                    {
                    // DISC — deterministic from category (default "A" for unmapped)
                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "A";
                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(el, ParamRegistry.DISC, disc, overwrite: true))
                            discSet++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, disc))
                            discSet++;
                    }

                    // LOC — from spatial context
                    string loc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(el, ParamRegistry.LOC, loc, overwrite: true))
                            locSet++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC, loc))
                            locSet++;
                    }

                    // ZONE — from room data
                    string zone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(el, ParamRegistry.ZONE, zone, overwrite: true))
                            zoneSet++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE, zone))
                            zoneSet++;
                    }

                    // LVL — deterministic from element level (guaranteed default: "L00" for levelless)
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl == "XX") lvl = "L00";
                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(el, ParamRegistry.LVL, lvl, overwrite: true))
                            lvlSet++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL, lvl))
                            lvlSet++;
                    }

                    // SYS — MEP system-aware (6-layer detection, guaranteed default from DISC)
                    string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                    if (string.IsNullOrEmpty(sys)) sys = TagConfig.GetDiscDefaultSysCode(disc);
                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(el, ParamRegistry.SYS, sys, overwrite: true))
                            sysSet++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, sys))
                            sysSet++;
                    }

                    // DISC correction — system-aware override (e.g. M→P for plumbing pipes, M→FP for fire)
                    string correctedDisc = TagConfig.GetSystemAwareDisc(disc, sys, catName);
                    if (correctedDisc != disc)
                    {
                        disc = correctedDisc;
                        ParameterHelpers.SetString(el, ParamRegistry.DISC, disc, overwrite: true);
                    }

                    // FUNC — smart subsystem differentiation (guaranteed default via FuncMap or "GEN")
                    string func = TagConfig.GetSmartFuncCode(el, sys);
                    if (string.IsNullOrEmpty(func))
                        func = TagConfig.FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";
                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(el, ParamRegistry.FUNC, func, overwrite: true))
                            funcSet++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func))
                            funcSet++;
                    }

                    // PROD — family-aware (highest intelligence)
                    string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                    string catProd = TagConfig.ProdMap.TryGetValue(catName, out string cp) ? cp : "GEN";
                    if (prod != catProd) familyProdUsed++;

                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(el, ParamRegistry.PROD, prod, overwrite: true))
                            prodSet++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, prod))
                            prodSet++;
                    }

                    // STATUS — from Revit phase/workset (EXISTING, NEW, DEMOLISHED, TEMPORARY)
                    string status = PhaseAutoDetect.DetectStatus(doc, el);
                    if (!string.IsNullOrEmpty(status))
                    {
                        phaseDetected++;
                        if (overwrite)
                        {
                            if (ParameterHelpers.SetString(el, ParamRegistry.STATUS, status, overwrite: true))
                                statusSet++;
                        }
                        else
                        {
                            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, status))
                                statusSet++;
                        }
                    }
                    else
                    {
                        // Fallback: default to NEW if no phase data available
                        if (overwrite)
                        {
                            if (ParameterHelpers.SetString(el, ParamRegistry.STATUS, "NEW", overwrite: true))
                                statusSet++;
                        }
                        else
                        {
                            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, "NEW"))
                                statusSet++;
                        }
                    }

                    // REV — from project revision sequence (guaranteed default: "P01")
                    string rev = !string.IsNullOrEmpty(projectRev) ? projectRev : "P01";
                    if (overwrite)
                    {
                        if (ParameterHelpers.SetString(el, ParamRegistry.REV, rev, overwrite: true))
                            revSet++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.REV, rev))
                            revSet++;
                    }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"FamilyStagePopulate: element {el?.Id}: {ex.Message}", ex);
                    }
                }

                tx.Commit();
            }

            sw.Stop();
            int totalPopulated = discSet + locSet + zoneSet + lvlSet + sysSet + funcSet + prodSet + statusSet + revSet;

            var report = new StringBuilder();
            report.AppendLine("Family-Stage Pre-Population Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Scope:    {scopeLabel}");
            report.AppendLine($"  Mode:     {(overwrite ? "Overwrite all" : "Fill empty only")}");
            report.AppendLine($"  Elements: {processed}");
            report.AppendLine($"  Duration: {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.AppendLine("── TAG TOKENS ──");
            report.AppendLine($"  DISC:    {discSet,5}");
            report.AppendLine($"  LOC:     {locSet,5}  (spatial + workset auto-detect)");
            report.AppendLine($"  ZONE:    {zoneSet,5}  (room + workset auto-detect)");
            report.AppendLine($"  LVL:     {lvlSet,5}  (from element level)");
            report.AppendLine($"  SYS:     {sysSet,5}  (6-layer MEP-aware)");
            report.AppendLine($"  FUNC:    {funcSet,5}  (smart subsystem)");
            report.AppendLine($"  PROD:    {prodSet,5}  ({familyProdUsed} family-specific)");
            report.AppendLine();
            report.AppendLine("── CONSTRUCTION & REVISION ──");
            report.AppendLine($"  STATUS:  {statusSet,5}  ({phaseDetected} from Revit phases)");
            report.AppendLine($"  REV:     {revSet,5}  {(string.IsNullOrEmpty(projectRev) ? "(no revisions)" : $"('{projectRev}')")}");
            report.AppendLine($"  ─────────────");
            report.AppendLine($"  Total:   {totalPopulated,5} token values set");
            report.AppendLine();
            report.AppendLine("Next step: Run Auto Tag / Batch Tag / Tag & Combine");
            report.AppendLine("to assign SEQ numbers and assemble final tags.");

            TaskDialog td = new TaskDialog("Family-Stage Populate");
            td.MainInstruction = $"Pre-populated {totalPopulated} tokens on {processed} elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"FamilyStagePopulate: scope={scopeLabel}, elements={processed}, " +
                $"tokens={totalPopulated}, familyProd={familyProdUsed}, " +
                $"status={statusSet}, phaseDetected={phaseDetected}, rev={revSet}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
