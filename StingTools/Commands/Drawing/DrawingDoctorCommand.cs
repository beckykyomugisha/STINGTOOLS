// StingTools — Drawing Template Manager · Phase 168
//
// DrawingDoctorCommand audits the title-block layer for cross-stamp
// conflicts between the legacy CSV populate path
// (TitleBlockPopulateCommand → PRJ_TB_LAST_SYNC_TXT) and the recipe
// binding path (DrawingTypePresentation.ApplyToSheet →
// STING_DRAWING_TYPE_ID_TXT). A sheet that carries BOTH stamps may
// have diverged values: the recipe applies on each sync, but the CSV
// populate writes a separate vocabulary, leaving the operator unsure
// which is authoritative. This command reports cross-stamps, missing
// title blocks, family swaps, and stale syncs so the operator can
// pick a single doctrine per project.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.ReadOnly)]
    public class DrawingDoctorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .OrderBy(s => s.SheetNumber, StringComparer.Ordinal)
                    .ToList();

                int totalSheets = sheets.Count;
                var crossStamped = new List<string>();
                var recipeOnly   = new List<string>();
                var csvOnly      = new List<string>();
                var unstamped    = new List<string>();
                var missingTb    = new List<string>();
                var familySwap   = new List<string>();
                var staleSync    = new List<string>(); // CSV-stamp older than 30 days

                foreach (var s in sheets)
                {
                    var dtId    = SafeRead(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                    var lastSync = SafeReadFromTb(doc, s, "PRJ_TB_LAST_SYNC_TXT");
                    bool hasRecipe = !string.IsNullOrEmpty(dtId);
                    bool hasCsv    = !string.IsNullOrEmpty(lastSync);

                    if (hasRecipe && hasCsv) crossStamped.Add($"{s.SheetNumber}  recipe='{dtId}'  csvSync='{lastSync}'");
                    else if (hasRecipe)      recipeOnly.Add($"{s.SheetNumber}  recipe='{dtId}'");
                    else if (hasCsv)         csvOnly.Add($"{s.SheetNumber}  csvSync='{lastSync}'");
                    else                     unstamped.Add(s.SheetNumber);

                    var tbs = TbsOnSheet(doc, s);
                    if (tbs.Count == 0) { missingTb.Add(s.SheetNumber); continue; }

                    if (hasRecipe)
                    {
                        var dt = DrawingTypeRegistry.Get(doc, dtId);
                        if (dt != null)
                        {
                            // P5 — compare the live family against the CONCRETE
                            // family the resolver maps the profile to, not the
                            // logical name (else every sheet false-flags a swap).
                            string declared = dt.TitleBlockFamily;
                            try { declared = DrawingDispatcher.ResolveTitleBlockVariant(dt).family; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                            if (string.IsNullOrWhiteSpace(declared)) declared = dt.TitleBlockFamily;
                            string concrete = declared;
                            try { concrete = TitleBlockResolver.ToConcreteFamily(doc, dt, declared); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                            if (!string.IsNullOrEmpty(concrete))
                            {
                                foreach (var tb in tbs)
                                {
                                    var liveFam = tb.Symbol?.FamilyName ?? "(unknown)";
                                    if (!string.Equals(liveFam, concrete, StringComparison.OrdinalIgnoreCase))
                                    {
                                        familySwap.Add($"{s.SheetNumber}  live='{liveFam}'  profile='{concrete}'");
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (hasCsv && DateTime.TryParse(lastSync, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)
                        && (DateTime.UtcNow - when).TotalDays > 30)
                        staleSync.Add($"{s.SheetNumber}  lastSync={when:yyyy-MM-dd}");
                }

                // DTW-123: views and sheets produced for a context whose level, room or
                // scope box has since been deleted. Production never revisits them, so they
                // never converge; they are listed (and offered for selection), never deleted.
                var orphanIds = new List<ElementId>();
                var orphaned = FindOrphanedContexts(doc, sheets, orphanIds);

                // DTW-144: scope boxes whose STING-LOC / ZONE / AREA name the strict grammar
                // (DTW-93) refuses. Tagging puts the elements inside them on the fallback LOC /
                // ZONE and the planner skips them; this names them.
                var badBoxNames = SpatialAutoDetect.AuditScopeBoxNames(doc);

                // DTW-203: views and sheets stamped with a drawing-type id the catalogue no
                // longer has. A renamed type minted a parallel set and left these behind;
                // a type whose `replaces` names the old id adopts them on the next run.
                var unknownTypeIds = new List<ElementId>();
                var unknownType = FindUnknownTypeStamps(doc, sheets, unknownTypeIds);

                var sb = new StringBuilder();
                sb.AppendLine($"STING — Drawing Doctor");
                sb.AppendLine($"  Total sheets: {totalSheets}");
                sb.AppendLine($"  Recipe-stamped only:    {recipeOnly.Count}");
                sb.AppendLine($"  CSV-populated only:     {csvOnly.Count}");
                sb.AppendLine($"  Cross-stamped (BOTH):   {crossStamped.Count}    ◀ pick a doctrine");
                sb.AppendLine($"  Unstamped:              {unstamped.Count}");
                sb.AppendLine($"  Sheets with no TB:      {missingTb.Count}");
                sb.AppendLine($"  Family swaps:           {familySwap.Count}");
                sb.AppendLine($"  Stale CSV sync (>30d):  {staleSync.Count}");
                sb.AppendLine($"  Context deleted:        {orphaned.Count}    (level / room / box gone)");
                sb.AppendLine($"  Unparsed box names:     {badBoxNames.Count}    (STING-LOC / ZONE / AREA name refused)");
                sb.AppendLine($"  Unknown type stamps:    {unknownType.Count}    (drawing-type id not in the catalogue)");
                AppendList(sb, "Views and sheets stamped with a drawing-type id the catalogue does not have (add the old id to the new type's \"replaces\" so production adopts them, or delete them)", unknownType);
                AppendList(sb, "Views and sheets whose production context was deleted (not removed — review, then delete or re-produce)", orphaned);
                AppendList(sb, "Scope boxes whose STING-LOC / ZONE / AREA name does not parse (elements inside take the fallback LOC / ZONE; rename, e.g. STING-LOC::BLOCK-A)", badBoxNames);
                AppendList(sb, "Cross-stamped sheets", crossStamped);
                AppendList(sb, "Family swaps",         familySwap);
                AppendList(sb, "Missing title block",  missingTb);
                AppendList(sb, "Stale CSV sync",       staleSync);
                AppendList(sb, "Unstamped sheets",     unstamped);

                var dlg = new TaskDialog("STING — Drawing Doctor")
                {
                    MainInstruction = $"{crossStamped.Count} cross-stamp(s), {familySwap.Count} family swap(s), {missingTb.Count} missing TB, {orphaned.Count} with a deleted context"
                                    + (badBoxNames.Count > 0 ? $", {badBoxNames.Count} scope-box name(s) refused" : "")
                                    + (unknownType.Count > 0 ? $", {unknownType.Count} with an unknown drawing type" : ""),
                    MainContent = "Doctor inspects the title-block layer for divergence between the CSV-populate path and the recipe-binding path. " +
                                  "Cross-stamped sheets carry stamps from both paths — values may have diverged." +
                                  (orphaned.Count > 0
                                      ? $"\n\n{orphaned.Count} view(s)/sheet(s) were produced for a level, room or scope box that no longer exists. Nothing is deleted."
                                      : "") +
                                  (badBoxNames.Count > 0
                                      ? $"\n\n{badBoxNames.Count} scope box(es) are named like a STING-LOC / ZONE / AREA box but do not parse: elements inside them were tagged with the fallback LOC / ZONE. See the details."
                                      : ""),
                    ExpandedContent = sb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Close,
                };
                if (orphanIds.Count > 0)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        $"Select the {orphanIds.Count} view(s)/sheet(s) with a deleted context",
                        "Selects them in the model so they can be reviewed, deleted or re-produced. Nothing is changed.");
                // Inside a workflow preset: the full report to the log, the headline to the step message.
                if (PresetDialog.Quiet)
                {
                    StingLog.Info(sb.ToString());
                    msg = $"Drawing Doctor: {dlg.MainInstruction}; {unstamped.Count} unstamped of {totalSheets} sheet(s) (details in the STING log).";
                }
                else if (dlg.Show() == TaskDialogResult.CommandLink1)
                {
                    try
                    {
                        var uidoc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument;
                        uidoc?.Selection.SetElementIds(orphanIds);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DrawingDoctor select orphaned contexts: {ex.Message}");
                        TaskDialog.Show("STING — Drawing Doctor", $"Could not select them: {ex.Message}");
                    }
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingDoctor", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// DTW-123 — every stamped view and sheet whose context ids (ProductionContextKey,
        /// DTW-42) name a level, room or scope box that no longer resolves. Stamps without
        /// ids cannot be judged and are skipped.
        /// </summary>
        private static List<string> FindOrphanedContexts(Document doc, List<ViewSheet> sheets, List<ElementId> ids)
        {
            var lines = new List<string>();
            var stamped = new List<(Element El, string Stamp, string Label)>();
            foreach (var s in sheets)
            {
                var st = DrawingTypeStamper.ReadSheetContext(s);
                if (!string.IsNullOrEmpty(st)) stamped.Add((s, st, $"Sheet {s.SheetNumber} - {s.Name}"));
            }
            try
            {
                foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                {
                    if (v == null || v.IsTemplate || v is ViewSheet) continue;
                    var st = SafeRead(v, ParamRegistry.STING_VIEW_CONTEXT_TAG);
                    if (!string.IsNullOrEmpty(st)) stamped.Add((v, st, $"View '{v.Name}'"));
                }
            }
            catch (Exception ex) { StingLog.Warn($"DrawingDoctor orphaned views: {ex.Message}"); }

            foreach (var (el, stamp, label) in stamped)
            {
                var ctx = ProductionContextIds.Parse(stamp);
                if (!ctx.Any) continue;
                var gone = new List<string>();
                try
                {
                    if (ctx.LevelId.HasValue && !(doc.GetElement(new ElementId(ctx.LevelId.Value)) is Level))
                        gone.Add($"level #{ctx.LevelId} ('{ProductionContextIds.LevelName(stamp) ?? "?"}')");
                    if (ctx.RoomId != null && long.TryParse(ctx.RoomId, out var rid)
                        && doc.GetElement(new ElementId(rid)) == null)
                        gone.Add($"room #{ctx.RoomId}");
                    if (ctx.BoxUniqueId != null && doc.GetElement(ctx.BoxUniqueId) == null)
                        gone.Add("scope box");
                }
                catch (Exception ex) { StingLog.Warn($"DrawingDoctor context of {el.Id}: {ex.Message}"); continue; }
                if (gone.Count == 0) continue;
                lines.Add($"{label} [id {el.Id.Value}] - {string.Join(", ", gone)} deleted");
                ids.Add(el.Id);
            }
            return lines;
        }

        /// <summary>
        /// DTW-203 — every view and sheet whose drawing-type stamp names an id the catalogue
        /// (corporate + project override) does not have, with the type that replaces it
        /// when one's `replaces` list names the old id.
        /// </summary>
        private static List<string> FindUnknownTypeStamps(Document doc, List<ViewSheet> sheets, List<ElementId> ids)
        {
            var lines = new List<string>();
            IReadOnlyList<DrawingType> catalogue;
            try { catalogue = DrawingTypeRegistry.ListAll(doc); }
            catch (Exception ex) { StingLog.Warn($"DrawingDoctor catalogue: {ex.Message}"); return lines; }
            if (catalogue == null || catalogue.Count == 0) return lines;   // no catalogue: cannot judge

            var stamped = new List<(Element El, string Label)>();
            foreach (var s in sheets) stamped.Add((s, $"Sheet {s.SheetNumber} - {s.Name}"));
            try
            {
                foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                    if (v != null && !v.IsTemplate && !(v is ViewSheet)) stamped.Add((v, $"View '{v.Name}'"));
            }
            catch (Exception ex) { StingLog.Warn($"DrawingDoctor unknown-type views: {ex.Message}"); }

            foreach (var (el, label) in stamped)
            {
                var id = SafeRead(el, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                var replacement = ProductionEdgeDecisions.ReplacementFor(id, catalogue);
                if (replacement == null) continue;
                lines.Add(replacement.Length > 0
                    ? $"{label} [id {el.Id.Value}] - stamped '{id}', replaced by '{replacement}' (adopted on its next production run)"
                    : $"{label} [id {el.Id.Value}] - stamped '{id}', which no drawing type has or replaces");
                ids.Add(el.Id);
            }
            return lines;
        }

        private static string SafeRead(Element el, string paramName)
        {
            try { return el?.LookupParameter(paramName)?.AsString(); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static string SafeReadFromTb(Document doc, ViewSheet sheet, string paramName)
        {
            try
            {
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .FirstOrDefault();
                return tb?.LookupParameter(paramName)?.AsString();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static List<FamilyInstance> TbsOnSheet(Document doc, ViewSheet sheet)
        {
            try
            {
                return new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return new List<FamilyInstance>(); }
        }

        private static void AppendList(StringBuilder sb, string label, List<string> items)
        {
            if (items == null || items.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine($"{label} ({items.Count}):");
            foreach (var it in items.Take(25)) sb.AppendLine("  " + it);
            if (items.Count > 25) sb.AppendLine($"  …({items.Count - 25} more)");
        }
    }
}
