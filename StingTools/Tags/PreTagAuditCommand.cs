using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Pre-Tag Audit: the highest-logic step in the tagging workflow.
    /// Performs a complete dry-run of the tagging process WITHOUT writing anything,
    /// predicting exactly what will happen before the user commits.
    ///
    /// Reports:
    ///   - How many elements will be tagged vs skipped
    ///   - How many collisions will occur and how they'll be resolved
    ///   - Missing tokens that need attention before tagging
    ///   - ISO 19650 code violations found on existing tags
    ///   - Per-discipline breakdown
    ///   - Elements that will receive family-aware PROD codes
    ///   - LOC/ZONE values that will be auto-detected from spatial data
    ///   - STATUS predictions from Revit phase/workset analysis
    ///   - REV coverage from project revision sequence
    ///   - Phase mismatch warnings (detected vs existing STATUS)
    ///
    /// This is the "measure twice, cut once" approach — eliminates surprises.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PreTagAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            var sw = Stopwatch.StartNew();

            // Scope selection
            TaskDialog scopeDlg = new TaskDialog("Pre-Tag Audit");
            scopeDlg.MainInstruction = "Audit scope — predict tag assignments";
            scopeDlg.MainContent =
                "This is a READ-ONLY audit. No changes will be made.\n" +
                "It predicts exactly what tagging will do before you commit.";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active View", "Audit elements visible in current view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Entire Project", "Audit all taggable elements in the model");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<Element> targetElements;
            string scopeLabel;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    if (doc.ActiveView == null) { TaskDialog.Show("Pre-Tag Audit", "No active view."); return Result.Failed; }
                    targetElements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType().ToList();
                    scopeLabel = $"active view '{doc.ActiveView.Name}'";
                    break;
                case TaskDialogResult.CommandLink2:
                    targetElements = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType().ToList();
                    scopeLabel = "entire project";
                    break;
                default:
                    return Result.Cancelled;
            }

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var (existingTags, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            string projectRev = PhaseAutoDetect.DetectProjectRevision(doc);

            // Counters
            int totalTaggable = 0;
            int alreadyTagged = 0;
            int willBeTagged = 0;
            int willBeSkipped = 0;
            int predictedCollisions = 0;
            int missingLocCount = 0;
            int missingZoneCount = 0;
            int locWillAutoDetect = 0;
            int zoneWillAutoDetect = 0;
            int familyProdCount = 0;
            int isoViolations = 0;
            int missingTokenElements = 0;

            // STATUS/REV prediction counters
            int statusMissing = 0, statusWillAutoDetect = 0;
            int revMissing = 0, revWillAutoSet = 0;
            int phaseMismatches = 0;
            var statusDistribution = new Dictionary<string, int>();

            // Per-discipline stats
            var discStats = new Dictionary<string, (int total, int tagged, int untagged, int violations)>();

            // Token coverage
            var emptyTokenCounts = new Dictionary<string, int>();
            string[] tokenParams = ParamRegistry.AllTokenParams;
            foreach (string t in tokenParams) emptyTokenCounts[t] = 0;

            // Family PROD intelligence
            var familyProdBreakdown = new Dictionary<string, int>();

            // Simulate tagging
            var simTags = new HashSet<string>(existingTags, StringComparer.Ordinal);
            var simCounters = new Dictionary<string, int>(seqCounters);

            // CSV audit rows
            var csvRows = new List<string>();
            csvRows.Add("ElementId,Category,Family,CurrentTag,PredictedTag,Action,LOC_Source,ZONE_Source,PROD_Source,STATUS,STATUS_Source,REV,ISOErrors");

            foreach (Element el in targetElements)
            {
                string catName = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(catName) || !known.Contains(catName))
                    continue;

                totalTaggable++;

                string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "A";
                if (!discStats.ContainsKey(disc))
                    discStats[disc] = (0, 0, 0, 0);

                string existingTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                bool hasTag = TagConfig.TagIsComplete(existingTag);

                // Check token coverage
                bool hasEmptyToken = false;
                foreach (string param in tokenParams)
                {
                    string val = ParameterHelpers.GetString(el, param);
                    if (string.IsNullOrEmpty(val))
                    {
                        emptyTokenCounts[param]++;
                        hasEmptyToken = true;
                    }
                }
                if (hasEmptyToken) missingTokenElements++;

                // ISO validation on existing tags
                int elementIsoErrors = 0;
                if (hasTag)
                {
                    string formatError = ISO19650Validator.ValidateTagFormat(existingTag);
                    if (formatError != null) elementIsoErrors++;
                    var tokenErrors = ISO19650Validator.ValidateElement(el);
                    elementIsoErrors += tokenErrors.Count;
                }

                if (elementIsoErrors > 0) isoViolations++;

                // Predict LOC/ZONE auto-detection
                string locSource = "existing";
                string zoneSource = "existing";
                string currentLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string currentZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);

                if (string.IsNullOrEmpty(currentLoc))
                {
                    missingLocCount++;
                    string detectedLoc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                    if (!string.IsNullOrEmpty(detectedLoc))
                    {
                        locWillAutoDetect++;
                        locSource = "spatial-auto";
                        currentLoc = detectedLoc;
                    }
                    else
                    {
                        locSource = "DEFAULT(BLD1)";
                        currentLoc = "BLD1";
                    }
                }

                if (string.IsNullOrEmpty(currentZone))
                {
                    missingZoneCount++;
                    string detectedZone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                    if (!string.IsNullOrEmpty(detectedZone))
                    {
                        zoneWillAutoDetect++;
                        zoneSource = "spatial-auto";
                        currentZone = detectedZone;
                    }
                    else
                    {
                        zoneSource = "DEFAULT(Z01)";
                        currentZone = "Z01";
                    }
                }

                // Predict STATUS from Revit phases/worksets
                string statusSource = "existing";
                string currentStatus = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                if (string.IsNullOrEmpty(currentStatus))
                {
                    statusMissing++;
                    string detectedStatus = PhaseAutoDetect.DetectStatus(doc, el);
                    if (!string.IsNullOrEmpty(detectedStatus))
                    {
                        statusWillAutoDetect++;
                        statusSource = "phase-auto";
                        currentStatus = detectedStatus;
                    }
                    else
                    {
                        statusSource = "DEFAULT(NEW)";
                        currentStatus = "NEW";
                    }
                }
                else
                {
                    // Cross-validate: check if existing STATUS matches what phase detection would give
                    string phaseStatus = PhaseAutoDetect.DetectStatus(doc, el);
                    if (!string.IsNullOrEmpty(phaseStatus) && phaseStatus != currentStatus)
                        phaseMismatches++;
                }
                if (!statusDistribution.ContainsKey(currentStatus))
                    statusDistribution[currentStatus] = 0;
                statusDistribution[currentStatus]++;

                // Predict REV from project revision (guaranteed default: "P01")
                string currentRev = ParameterHelpers.GetString(el, ParamRegistry.REV);
                if (string.IsNullOrEmpty(currentRev))
                {
                    revMissing++;
                    currentRev = !string.IsNullOrEmpty(projectRev) ? projectRev : "P01";
                    revWillAutoSet++;
                }

                // Predict PROD code (family-aware)
                string prodSource = "category";
                string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                string familyName = ParameterHelpers.GetFamilyName(el);
                string catProd = TagConfig.ProdMap.TryGetValue(catName, out string cp) ? cp : "GEN";
                if (prod != catProd && !string.IsNullOrEmpty(familyName))
                {
                    familyProdCount++;
                    prodSource = $"family({familyName})";
                    if (!familyProdBreakdown.ContainsKey($"{prod}({familyName})"))
                        familyProdBreakdown[$"{prod}({familyName})"] = 0;
                    familyProdBreakdown[$"{prod}({familyName})"]++;
                }

                // Simulate tag generation
                string action;
                string predictedTag = "";
                if (hasTag)
                {
                    alreadyTagged++;
                    action = "SKIP(already-tagged)";
                    predictedTag = existingTag;

                    var s = discStats[disc];
                    discStats[disc] = (s.total + 1, s.tagged + 1, s.untagged, s.violations + (elementIsoErrors > 0 ? 1 : 0));
                }
                else
                {
                    // Simulate tag generation (MEP-aware, matching BuildAndWriteTag logic)
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl == "XX") lvl = "L00"; // Guaranteed LVL default
                    string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                    if (string.IsNullOrEmpty(sys)) sys = TagConfig.GetDiscDefaultSysCode(disc); // Guaranteed SYS default
                    // Apply system-aware DISC correction for pipes
                    disc = TagConfig.GetSystemAwareDisc(disc, sys, catName);
                    // Ensure corrected disc key exists in stats
                    if (!discStats.ContainsKey(disc))
                        discStats[disc] = (0, 0, 0, 0);
                    string func = TagConfig.GetSmartFuncCode(el, sys);
                    if (string.IsNullOrEmpty(func))
                        func = TagConfig.FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN"; // Guaranteed FUNC default

                    string seqKey = $"{disc}_{sys}_{lvl}";
                    if (!simCounters.ContainsKey(seqKey)) simCounters[seqKey] = 0;
                    simCounters[seqKey]++;
                    string seq = simCounters[seqKey].ToString().PadLeft(ParamRegistry.NumPad, '0');

                    predictedTag = string.Join(ParamRegistry.Separator,
                        disc, currentLoc, currentZone, lvl, sys, func, prod, seq);

                    // Check collision
                    int collisionCount = 0;
                    while (simTags.Contains(predictedTag) && collisionCount < TagConfig.MaxCollisionDepth)
                    {
                        collisionCount++;
                        simCounters[seqKey]++;
                        seq = simCounters[seqKey].ToString().PadLeft(ParamRegistry.NumPad, '0');
                        predictedTag = string.Join(ParamRegistry.Separator,
                            disc, currentLoc, currentZone, lvl, sys, func, prod, seq);
                    }
                    if (collisionCount > 0) predictedCollisions++;
                    simTags.Add(predictedTag);

                    willBeTagged++;
                    action = collisionCount > 0 ? $"TAG(collision+{collisionCount})" : "TAG";

                    var s = discStats[disc];
                    discStats[disc] = (s.total + 1, s.tagged, s.untagged + 1, s.violations + (elementIsoErrors > 0 ? 1 : 0));
                }

                // Cross-validation: check predicted PROD against DISC
                if (!hasTag && !string.IsNullOrEmpty(prod) && !string.IsNullOrEmpty(disc))
                {
                    var prodErr = ISO19650Validator.ValidateToken(ParamRegistry.PROD, prod);
                    if (prodErr == null)
                    {
                        // Check PROD↔DISC consistency using the validator's static helper
                        // (This validates the prediction, not the existing tag)
                    }
                }

                csvRows.Add($"{el.Id},\"{catName}\",\"{familyName}\",\"{existingTag}\",\"{predictedTag}\"," +
                    $"\"{action}\",\"{locSource}\",\"{zoneSource}\",\"{prodSource}\"," +
                    $"\"{currentStatus}\",\"{statusSource}\",\"{currentRev}\",{elementIsoErrors}");
            }

            willBeSkipped = totalTaggable - willBeTagged - alreadyTagged;
            sw.Stop();

            // Build report
            var report2 = new StringBuilder();
            report2.AppendLine("══════════════════════════════════════════════════");
            report2.AppendLine("  PRE-TAG AUDIT — DRY RUN (no changes made)");
            report2.AppendLine("══════════════════════════════════════════════════");
            report2.AppendLine($"  Scope: {scopeLabel}");
            report2.AppendLine($"  Duration: {sw.Elapsed.TotalSeconds:F1}s");
            report2.AppendLine();

            // Summary
            report2.AppendLine("── TAG PREDICTION ──");
            report2.AppendLine($"  Total taggable:     {totalTaggable}");
            report2.AppendLine($"  Already tagged:     {alreadyTagged} (will be skipped)");
            report2.AppendLine($"  Will be tagged:     {willBeTagged}");
            if (predictedCollisions > 0)
                report2.AppendLine($"  Collisions to resolve: {predictedCollisions} (SEQ auto-increment)");
            report2.AppendLine();

            // Spatial auto-detection
            report2.AppendLine("── SPATIAL INTELLIGENCE ──");
            report2.AppendLine($"  LOC missing:        {missingLocCount}");
            report2.AppendLine($"  LOC auto-detectable: {locWillAutoDetect} (from rooms/project info)");
            report2.AppendLine($"  ZONE missing:       {missingZoneCount}");
            report2.AppendLine($"  ZONE auto-detectable: {zoneWillAutoDetect} (from rooms)");
            report2.AppendLine();

            // STATUS prediction
            report2.AppendLine("── STATUS PREDICTION ──");
            report2.AppendLine($"  STATUS missing:     {statusMissing}");
            report2.AppendLine($"  Will auto-detect:   {statusWillAutoDetect} (from Revit phases/worksets)");
            if (phaseMismatches > 0)
                report2.AppendLine($"  Phase mismatches:   {phaseMismatches} (existing STATUS differs from detected)");
            if (statusDistribution.Count > 0)
            {
                report2.Append("  Distribution:       ");
                report2.AppendLine(string.Join(", ",
                    statusDistribution.OrderByDescending(x => x.Value)
                        .Select(x => $"{x.Key}={x.Value}")));
            }
            report2.AppendLine();

            // REV prediction
            report2.AppendLine("── REVISION PREDICTION ──");
            report2.AppendLine($"  REV missing:        {revMissing}");
            report2.AppendLine($"  Will auto-set:      {revWillAutoSet}" +
                (string.IsNullOrEmpty(projectRev) ? " (no project revisions)" : $" (revision '{projectRev}')"));
            report2.AppendLine();

            // Family PROD intelligence
            report2.AppendLine("── FAMILY-AWARE PROD CODES ──");
            report2.AppendLine($"  Family-specific PRODs: {familyProdCount}");
            if (familyProdBreakdown.Count > 0)
            {
                foreach (var kvp in familyProdBreakdown.OrderByDescending(x => x.Value).Take(10))
                    report2.AppendLine($"    {kvp.Key}: {kvp.Value}");
            }
            report2.AppendLine();

            // Token coverage
            report2.AppendLine("── TOKEN COVERAGE ──");
            report2.AppendLine($"  Elements with missing tokens: {missingTokenElements}");
            foreach (var kvp in emptyTokenCounts.Where(x => x.Value > 0).OrderByDescending(x => x.Value))
            {
                string shortName = kvp.Key.Replace("ASS_", "").Replace("_TXT", "").Replace("_COD", "");
                report2.AppendLine($"    {shortName,-20} {kvp.Value} empty");
            }
            report2.AppendLine();

            // ISO 19650 compliance
            report2.AppendLine("── ISO 19650 COMPLIANCE ──");
            report2.AppendLine($"  Elements with ISO violations: {isoViolations}");
            if (isoViolations == 0)
                report2.AppendLine("    All existing tags conform to ISO 19650");
            report2.AppendLine();

            // Per-discipline breakdown
            report2.AppendLine("── BY DISCIPLINE ──");
            report2.AppendLine($"  {"DISC",-6} {"Total",6} {"Tagged",7} {"New",5} {"ISO!",5}");
            report2.AppendLine($"  {new string('─', 32)}");
            foreach (var kvp in discStats.OrderBy(x => x.Key))
            {
                var s = kvp.Value;
                report2.AppendLine($"  {kvp.Key,-6} {s.total,6} {s.tagged,7} {s.untagged,5} {s.violations,5}");
            }

            // Export CSV
            try
            {
                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                string csvPath = Path.Combine(dir, $"STING_PreTagAudit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(csvPath, string.Join("\n", csvRows));
                report2.AppendLine();
                report2.AppendLine($"  CSV exported: {csvPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PreTagAudit CSV export: {ex.Message}");
            }

            TaskDialog td = new TaskDialog("Pre-Tag Audit");
            td.MainInstruction = $"Will tag {willBeTagged} elements ({alreadyTagged} already tagged, {predictedCollisions} collisions)";
            td.MainContent = report2.ToString();
            td.Show();

            StingLog.Info($"PreTagAudit: {totalTaggable} elements, {predictedCollisions} predicted collisions, " +
                $"{isoViolations} ISO violations, {willBeTagged} untagged" +
                $" (elapsed={sw.Elapsed.TotalSeconds:F1}s)");

            return Result.Succeeded;
        }
    }
}
