using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;

namespace StingTools.Tags
{
    /// <summary>
    /// Manual token writer commands — the 13 individual ISO 19650 token setters
    /// from the StingTools V2.1 CREATE tab. Each sets a single token on all
    /// selected elements (or all taggable elements if nothing is selected).
    /// Includes PROJ, ORIG, VOL, LVL, DISC, LOC, ZONE, SYS, FUNC, PROD, SEQ, STATUS, REV.
    /// </summary>
    internal static class TokenWriter
    {
        public static Result WriteToken(ExternalCommandData cmd, string paramName,
            string label, string[] options)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            // Build target set: selected elements or all taggable in view
            var targetIds = uidoc.Selection.GetElementIds();
            bool usingSelection = targetIds.Count > 0;

            if (!usingSelection)
            {
                if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                targetIds = new FilteredElementCollector(doc, ctx.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id)
                    .ToList();
            }

            if (targetIds.Count == 0)
            {
                TaskDialog.Show(label, "No elements to update.");
                return Result.Succeeded;
            }

            // Interactive dialog with search, select, cancel, OK
            var optionItems = options.Select(o => new StingListPicker.ListItem
            {
                Label = o,
                Detail = "",
                Tag = o
            }).ToList();

            string scopeLabel = usingSelection ? "Selected elements" : "All taggable in view";
            var picked = StingListPicker.Show(
                $"Set {label}",
                $"{targetIds.Count} elements ({scopeLabel}). Pick a value to apply.",
                optionItems, allowMultiSelect: false);

            if (picked == null || picked.Count == 0) return Result.Cancelled;
            string value = picked[0].Tag as string;
            if (string.IsNullOrEmpty(value)) return Result.Cancelled;

            // Validate the chosen value against ISO 19650 code lists
            string validationError = ISO19650Validator.ValidateToken(paramName, value);
            if (validationError != null)
            {
                var warnDlg = new TaskDialog("Token Validation Warning");
                warnDlg.MainInstruction = "ISO 19650 validation warning";
                warnDlg.MainContent = $"{validationError}\n\nContinue anyway?";
                warnDlg.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                if (warnDlg.Show() != TaskDialogResult.Yes)
                    return Result.Cancelled;
            }

            int written = 0;
            int tagsRebuilt = 0;
            using (Transaction tx = new Transaction(doc, $"Set {label}"))
            {
                tx.Start();
                foreach (ElementId id in targetIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    if (ParameterHelpers.SetString(elem, paramName, value, overwrite: true))
                        written++;
                }

                // TAG-01: After setting the token, rebuild TAG1 + containers so they
                // reflect the change immediately. Previously TAG1/containers remained
                // stale until user ran a separate BuildTags or Combine command.
                if (written > 0)
                {
                    var existingTags = TagConfig.BuildExistingTagIndex(doc);
                    var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
                    foreach (ElementId id in targetIds)
                    {
                        Element elem = doc.GetElement(id);
                        if (elem == null) continue;
                        string tag1 = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(tag1)) continue; // Only rebuild already-tagged elements
                        try
                        {
                            TagConfig.BuildAndWriteTag(doc, elem, seqCounters,
                                skipComplete: false, existingTags, TagCollisionMode.AutoIncrement);
                            string[] tokens = ParamRegistry.ReadTokenValues(elem);
                            if (tokens != null && tokens.Length >= 8)
                            {
                                string catName = ParameterHelpers.GetCategoryName(elem);
                                ParamRegistry.WriteContainers(elem, tokens, catName);
                                TagConfig.WriteTag7All(doc, elem, catName, tokens, overwrite: true);
                            }
                            tagsRebuilt++;
                        }
                        catch (Exception rebuildEx)
                        {
                            StingLog.Warn($"TokenWriter TAG1 rebuild for {elem.Id}: {rebuildEx.Message}");
                        }
                    }
                }

                tx.Commit();
            }

            // FIX-WR08: Invalidate caches after token writes so dashboard/auto-tagger reflect changes
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            if (written > 0)
            {
                // seqCounters was built earlier (line 99) and passed to BuildAndWriteTag
                try { TagConfig.SaveSeqSidecar(doc, TagConfig.GetExistingSequenceCounters(doc)); }
                catch (Exception ssEx) { StingLog.Warn($"TokenWriter SaveSeqSidecar: {ssEx.Message}"); }
            }

            string resultMsg = $"Set '{value}' on {written} elements.";
            if (tagsRebuilt > 0) resultMsg += $"\nRebuilt TAG1 + containers on {tagsRebuilt} elements.";
            TaskDialog.Show(label, resultMsg);
            return Result.Succeeded;
        }
    }

    /// <summary>Set the DISC (discipline) token on selected/view elements.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetDiscCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, ParamRegistry.DISC, "Discipline (DISC)",
                new[] { "M", "E", "P", "A", "S", "FP", "LV", "G" });
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetLocCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, ParamRegistry.LOC, "Location (LOC)",
                TagConfig.LocCodes.ToArray());
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetZoneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, ParamRegistry.ZONE, "Zone (ZONE)",
                TagConfig.ZoneCodes.ToArray());
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, ParamRegistry.STATUS, "Status",
                new[] { "EXISTING", "NEW", "DEMOLISHED", "TEMPORARY" });
    }

    /// <summary>
    /// Assign sequential numbers to selected elements, grouped by (DISC, SYS, LVL).
    /// Standalone version of the sequence numbering embedded in AutoTag.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AssignNumbersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Determine scope
            var targetIds = uidoc.Selection.GetElementIds();
            if (targetIds.Count == 0)
            {
                if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
                targetIds = new FilteredElementCollector(doc, ctx.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id).ToList();
            }

            // Use canonical BuildTagIndexAndCounters (merges sidecar data for session continuity)
            var (_, maxSeq) = TagConfig.BuildTagIndexAndCounters(doc);

            int assigned = 0;
            using (Transaction tx = new Transaction(doc, "STING Assign Numbers"))
            {
                tx.Start();
                foreach (ElementId id in targetIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    string cat = ParameterHelpers.GetCategoryName(elem);
                    if (!known.Contains(cat)) continue;

                    // Skip if already has a sequence number
                    string existing = ParameterHelpers.GetString(elem, ParamRegistry.SEQ);
                    if (!string.IsNullOrEmpty(existing)) continue;

                    string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                    string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                    string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                    if (string.IsNullOrEmpty(disc))
                    {
                        disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "A";
                        ParameterHelpers.SetIfEmpty(elem, ParamRegistry.DISC, disc);
                    }
                    if (string.IsNullOrEmpty(sys))
                    {
                        sys = TagConfig.GetMepSystemAwareSysCode(elem, cat);
                        if (string.IsNullOrEmpty(sys)) sys = TagConfig.GetDiscDefaultSysCode(disc);
                        ParameterHelpers.SetIfEmpty(elem, ParamRegistry.SYS, sys);
                    }
                    if (string.IsNullOrEmpty(lvl))
                    {
                        lvl = ParameterHelpers.GetLevelCode(doc, elem);
                        if (lvl == "XX") lvl = "L00";
                        ParameterHelpers.SetIfEmpty(elem, ParamRegistry.LVL, lvl);
                    }

                    // LOGIC-04: Include ZONE in group key for distinct sequences per zone
                    string zone = ParameterHelpers.GetString(elem, ParamRegistry.ZONE);
                    if (string.IsNullOrEmpty(zone)) zone = "ZZ";
                    string func = ParameterHelpers.GetString(elem, ParamRegistry.FUNC);
                    string prod = ParameterHelpers.GetString(elem, ParamRegistry.PROD);
                    // Use canonical BuildSeqKey for consistent key format across all commands
                    string key = TagConfig.BuildSeqKey(disc, sys, func, prod, lvl, zone);
                    if (!maxSeq.ContainsKey(key)) maxSeq[key] = 0;
                    maxSeq[key]++;
                    // Honor SeqScheme setting (Numeric/Alpha/ZonePrefix/DiscPrefix)
                    string seqContext = TagConfig.CurrentSeqScheme == SeqScheme.ZonePrefix ? zone
                                      : TagConfig.CurrentSeqScheme == SeqScheme.DiscPrefix ? disc
                                      : "";
                    string seq = TagConfig.BuildSeqString(maxSeq[key], TagConfig.CurrentSeqScheme, seqContext);
                    ParameterHelpers.SetString(elem, ParamRegistry.SEQ, seq, overwrite: true);
                    assigned++;
                }

                // SEQ-01: After assigning SEQ numbers, rebuild TAG1 + containers so they
                // reflect the new sequence. Previously TAG1 remained stale until a separate
                // BuildTags command was run.
                var existingTags = TagConfig.BuildExistingTagIndex(doc);
                int rebuilt = 0;
                foreach (ElementId id in targetIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    string tag1 = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1) &&
                        string.IsNullOrEmpty(ParameterHelpers.GetString(elem, ParamRegistry.DISC)))
                        continue; // Skip untagged elements with no DISC
                    try
                    {
                        TagConfig.BuildAndWriteTag(doc, elem, maxSeq,
                            skipComplete: false, existingTags, TagCollisionMode.Skip);
                        string[] tokens = ParamRegistry.ReadTokenValues(elem);
                        if (tokens != null && tokens.Length >= 8)
                        {
                            string catName = ParameterHelpers.GetCategoryName(elem);
                            ParamRegistry.WriteContainers(elem, tokens, catName);
                            TagConfig.WriteTag7All(doc, elem, catName, tokens, overwrite: true);
                        }
                        rebuilt++;
                    }
                    catch (Exception rebuildEx)
                    {
                        StingLog.Warn($"AssignNumbers TAG1 rebuild for {elem.Id}: {rebuildEx.Message}");
                    }
                }

                tx.Commit();
            }

            // FIX-WR07: Save SEQ sidecar + invalidate caches after sequence assignment
            TagConfig.SaveSeqSidecar(doc, maxSeq);
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();

            string resultMsg = $"Assigned sequence numbers to {assigned} elements.";
            if (rebuilt > 0) resultMsg += $"\nRebuilt TAG1 + containers on {rebuilt} elements.";
            TaskDialog.Show("Assign Numbers", resultMsg);
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Build/rebuild assembled tags from existing individual token parameters
    /// without changing any token values. Respects existing LOC/ZONE values.
    ///
    /// Writes ALL tag containers (from ParamRegistry) with collision detection —
    /// auto-increments SEQ if a duplicate tag is detected.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BuildTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            var targetIds = uidoc.Selection.GetElementIds();
            if (targetIds.Count == 0)
            {
                if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                targetIds = new FilteredElementCollector(doc, ctx.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id).ToList();
            }

            if (targetIds.Count == 0)
            {
                TaskDialog.Show("Build Tags", "No taggable elements found.");
                return Result.Succeeded;
            }

            // Build collision detection index and sequence counters
            var (existingTags, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);

            // FIX-B01: Use RunFullPipeline for canonical per-element processing
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);

            int built = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Build Tags + All Containers"))
            {
                tx.Start();
                foreach (ElementId id in targetIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;

                    // FIX-B01: Delegate to unified pipeline (handles all 11 canonical steps)
                    // overwrite: false — preserves manually-set token values;
                    // PopulateAll only fills empty tokens, respecting user edits
                    bool ok = TagPipelineHelper.RunFullPipeline(
                        doc, elem, popCtx, existingTags, seqCounters,
                        formulas, gridLines,
                        overwrite: false, skipComplete: false,
                        collisionMode: TagCollisionMode.AutoIncrement);

                    if (ok) built++;
                    else skipped++;
                }
                tx.Commit();
            }

            // FIX-WR04: Save SEQ sidecar + invalidate caches after tag building
            TagConfig.SaveSeqSidecar(doc, seqCounters);
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();

            var report = new StringBuilder();
            report.AppendLine($"Built tags for {built} elements.");
            if (skipped > 0)
                report.AppendLine($"Skipped {skipped} elements.");

            TaskDialog.Show("Build Tags", report.ToString());
            StingLog.Info($"BuildTags: built={built}, skipped={skipped}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// ISO 19650 completeness dashboard — reports per-discipline compliance.
    /// Shows both standard compliance (tag has 8 non-empty segments) and strict
    /// compliance (no XX/ZZ placeholder segments = fully resolved tags).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CompletenessDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var stats = new Dictionary<string, (int total, int valid, int resolved, int incomplete, int missing)>();
            int emptyStatus = 0, emptyRev = 0;

            var dashColl = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var dashCatEnums = SharedParamGuids.AllCategoryEnums;
            if (dashCatEnums != null && dashCatEnums.Length > 0)
                dashColl.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(dashCatEnums)));
            foreach (Element elem in dashColl)
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "A";
                if (!stats.ContainsKey(disc)) stats[disc] = (0, 0, 0, 0, 0);

                // Track STATUS/REV completeness
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(elem, ParamRegistry.STATUS))) emptyStatus++;
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(elem, ParamRegistry.REV))) emptyRev++;

                var s = stats[disc];
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag))
                    stats[disc] = (s.total + 1, s.valid, s.resolved, s.incomplete, s.missing + 1);
                else if (TagConfig.TagIsFullyResolved(tag))
                    stats[disc] = (s.total + 1, s.valid + 1, s.resolved + 1, s.incomplete, s.missing);
                else if (TagConfig.TagIsComplete(tag))
                    stats[disc] = (s.total + 1, s.valid + 1, s.resolved, s.incomplete, s.missing);
                else
                    stats[disc] = (s.total + 1, s.valid, s.resolved, s.incomplete + 1, s.missing);
            }

            var report = new StringBuilder();
            report.AppendLine("═══ ISO 19650 Completeness Dashboard ═══");
            report.AppendLine();
            report.AppendLine($"{"DISC",-6} {"Total",7} {"Valid",7} {"Resol",7} {"Incp",7} {"Miss",7} {"Comp%",7} {"Strict%",7}");
            report.AppendLine(new string('─', 56));

            int grandTotal = 0, grandValid = 0, grandResolved = 0, grandInc = 0, grandMiss = 0;
            foreach (var kvp in stats.OrderBy(x => x.Key))
            {
                var s = kvp.Value;
                double pct = s.total > 0 ? s.valid * 100.0 / s.total : 0;
                double strictPct = s.total > 0 ? s.resolved * 100.0 / s.total : 0;
                report.AppendLine($"{kvp.Key,-6} {s.total,7} {s.valid,7} {s.resolved,7} {s.incomplete,7} {s.missing,7} {pct,6:F1}% {strictPct,6:F1}%");
                grandTotal += s.total;
                grandValid += s.valid;
                grandResolved += s.resolved;
                grandInc += s.incomplete;
                grandMiss += s.missing;
            }

            report.AppendLine(new string('─', 56));
            double grandPct = grandTotal > 0 ? grandValid * 100.0 / grandTotal : 0;
            double grandStrictPct = grandTotal > 0 ? grandResolved * 100.0 / grandTotal : 0;
            report.AppendLine($"{"TOTAL",-6} {grandTotal,7} {grandValid,7} {grandResolved,7} {grandInc,7} {grandMiss,7} {grandPct,6:F1}% {grandStrictPct,6:F1}%");
            report.AppendLine();
            report.AppendLine("Valid = tag has 8 non-empty segments");
            report.AppendLine("Resolved = no placeholders (XX/ZZ/0000)");
            if (emptyStatus > 0)
                report.AppendLine($"Empty STATUS: {emptyStatus} elements (run Tag & Combine to auto-detect)");
            if (emptyRev > 0)
                report.AppendLine($"Empty REV: {emptyRev} elements (run Tag & Combine to auto-set)");

            // GAP-005 fix: Show revision distribution and stale revision detection
            var revDistribution = new Dictionary<string, int>();
            string currentProjectRev = "";
            try { currentProjectRev = BIMManager.RevisionEngine.GetCurrentProjectRevision(doc); } catch (Exception ex) { StingLog.Warn($"Get current project revision failed: {ex.Message}"); }
            int staleRevCount = 0;
            foreach (Element elem2 in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat2 = ParameterHelpers.GetCategoryName(elem2);
                if (!known.Contains(cat2)) continue;
                string revVal = ParameterHelpers.GetString(elem2, ParamRegistry.REV);
                if (string.IsNullOrEmpty(revVal)) continue;
                if (!revDistribution.ContainsKey(revVal)) revDistribution[revVal] = 0;
                revDistribution[revVal]++;
                if (!string.IsNullOrEmpty(currentProjectRev) &&
                    !string.Equals(revVal, currentProjectRev, StringComparison.OrdinalIgnoreCase))
                    staleRevCount++;
            }
            if (revDistribution.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("── Revision Distribution ──");
                if (!string.IsNullOrEmpty(currentProjectRev))
                    report.AppendLine($"Current project revision: {currentProjectRev}");
                foreach (var rv in revDistribution.OrderByDescending(x => x.Value))
                {
                    string marker = (!string.IsNullOrEmpty(currentProjectRev) &&
                        string.Equals(rv.Key, currentProjectRev, StringComparison.OrdinalIgnoreCase))
                        ? " ← CURRENT" : "";
                    report.AppendLine($"  {rv.Key}: {rv.Value} elements{marker}");
                }
                if (staleRevCount > 0)
                    report.AppendLine($"  Stale revisions: {staleRevCount} elements not at latest revision");
            }

            TaskDialog td = new TaskDialog("ISO Completeness Dashboard");
            td.MainInstruction = $"Compliance: {grandPct:F1}% | Strict: {grandStrictPct:F1}% ({grandResolved}/{grandTotal})";
            td.MainContent = report.ToString();
            td.FooterText = "Click 'Yes' to create a discipline compliance legend.";
            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            td.DefaultButton = TaskDialogResult.No;

            if (td.Show() == TaskDialogResult.Yes)
            {
                var legendEntries = new List<LegendBuilder.LegendEntry>();
                foreach (var kvp in stats.OrderBy(x => x.Key))
                {
                    var s = kvp.Value;
                    double pct2 = s.total > 0 ? s.valid * 100.0 / s.total : 0;
                    legendEntries.Add(new LegendBuilder.LegendEntry
                    {
                        Color = StingColorRegistry.GetDisciplineColor(kvp.Key),
                        Label = $"{kvp.Key} — {pct2:F0}%",
                        Description = $"{s.valid}/{s.total} valid ({s.missing} missing)",
                        Bold = pct2 >= 90,
                    });
                }

                if (legendEntries.Count > 0)
                {
                    using (Transaction ltx = new Transaction(doc, "STING Compliance Legend"))
                    {
                        ltx.Start();
                        var legendConfig = new LegendBuilder.LegendConfig
                        {
                            Title = "Discipline Compliance",
                            Subtitle = $"Overall: {grandPct:F1}% | Strict: {grandStrictPct:F1}%",
                            Footer = "STING Tools — ISO 19650 Completeness Dashboard",
                        };
                        LegendBuilder.CreateLegendView(doc, legendEntries, legendConfig);
                        ltx.Commit();
                    }
                }
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Configure the sequence numbering scheme (numeric, alphabetic, zone-prefixed, disc-prefixed).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetSeqSchemeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var td = new TaskDialog("STING — Sequence Scheme");
            td.MainInstruction = "Select numbering scheme for SEQ token";
            td.MainContent = $"Current: {TagConfig.CurrentSeqScheme}\n\n" +
                "WARNING: Changing scheme may require re-numbering all elements.";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Numeric (0001, 0042)", "Standard zero-padded numbers");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Alphabetic (A, B, AA)", "Base-26 letter codes");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Zone-Prefixed (Z1-0042)", "Zone prefix before number");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Discipline-Prefixed (M-0042)", "Discipline prefix before number");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            var result = td.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1: TagConfig.CurrentSeqScheme = SeqScheme.Numeric; break;
                case TaskDialogResult.CommandLink2: TagConfig.CurrentSeqScheme = SeqScheme.Alpha; break;
                case TaskDialogResult.CommandLink3: TagConfig.CurrentSeqScheme = SeqScheme.ZonePrefix; break;
                case TaskDialogResult.CommandLink4: TagConfig.CurrentSeqScheme = SeqScheme.DiscPrefix; break;
                default: return Result.Cancelled;
            }

            TaskDialog.Show("STING", $"Sequence scheme set to: {TagConfig.CurrentSeqScheme}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Map native Revit title block / sheet parameters (Drawn By, Checked By,
    /// Approved By, Sheet Number, Sheet Name, Issue Date, Revision) to STING
    /// shared parameters on all ViewSheets. Non-destructive: only writes to
    /// empty STING parameters.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MapSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            int written;
            using (Transaction tx = new Transaction(doc, "STING Map Sheet Parameters"))
            {
                tx.Start();
                written = NativeParamMapper.MapSheets(doc);
                tx.Commit();
            }

            int sheetCount = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).GetElementCount();

            TaskDialog.Show("STING Map Sheets",
                $"Sheet parameter mapping complete.\n\n" +
                $"Sheets processed: {sheetCount}\n" +
                $"Values written: {written}\n\n" +
                "Mapped: Sheet Number, Sheet Name, Drawn By, Checked By,\n" +
                "Approved By, Issue Date, Current Revision");

            StingLog.Info($"MapSheetsCommand: {written} values written across {sheetCount} sheets");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Tag all sheets with ISO 19650 document codes by scanning viewport contents
    /// to derive discipline, form, level, originator, and revision tokens.
    /// Assembles SHT_TAG_1 (full document code) and SHT_TAG_7 (rich narrative).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            int sheetCount = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).GetElementCount();

            if (sheetCount == 0)
            {
                TaskDialog.Show("STING", "No sheets found in the project.");
                return Result.Succeeded;
            }

            var confirm = new TaskDialog("STING — Tag Sheets");
            confirm.MainInstruction = $"Tag {sheetCount} sheets with ISO 19650 document codes?";
            confirm.MainContent =
                "This will scan viewport contents on each sheet to derive:\n\n" +
                "• SHT_DISC — Discipline (majority vote from elements)\n" +
                "• SHT_FORM — Document form (DR/SH/M3/LG)\n" +
                "• SHT_LEVEL — Level code (from viewport views)\n" +
                "• SHT_ORIGINATOR — From Project Information\n" +
                "• SHT_REV — Current project revision\n" +
                "• SHT_TAG_1 — Assembled ISO 19650 document code\n" +
                "• SHT_TAG_7 — Rich narrative description\n\n" +
                "Existing sheet token values will be overwritten.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            int sheetsProcessed = 0, tokensWritten = 0;
            using (Transaction tx = new Transaction(doc, "STING Tag Sheets"))
            {
                tx.Start();

                // Map native sheet params first
                NativeParamMapper.MapSheets(doc);

                // Run full sheet tagging pipeline
                var (sheets, tokens) = NativeParamMapper.TagSheets(doc);
                sheetsProcessed = sheets;
                tokensWritten = tokens;

                tx.Commit();
            }

            sw.Stop();

            // Cache invalidation — ensure compliance dashboard reflects sheet changes
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();

            var report = new System.Text.StringBuilder();
            report.AppendLine("Sheet Tagging Complete");
            report.AppendLine(new string('═', 40));
            report.AppendLine($"  Sheets processed:  {sheetsProcessed}");
            report.AppendLine($"  Tokens written:    {tokensWritten}");
            report.AppendLine($"  Duration:          {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.AppendLine("Parameters written per sheet:");
            report.AppendLine("  SHT_DISC, SHT_FORM, SHT_LEVEL,");
            report.AppendLine("  SHT_ORIGINATOR, SHT_REV,");
            report.AppendLine("  SHT_TAG_1 (document code), SHT_TAG_7 (narrative)");

            TaskDialog.Show("STING Tag Sheets", report.ToString());
            StingLog.Info($"TagSheetsCommand: {sheetsProcessed} sheets, {tokensWritten} tokens, {sw.Elapsed.TotalSeconds:F1}s");
            return Result.Succeeded;
        }
    }
}
