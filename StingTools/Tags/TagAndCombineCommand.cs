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
    /// One-click "Tag and Combine All" — chains AutoTag + CombineParameters
    /// so the user gets a fully-tagged project with all containers populated
    /// in a single click. This is the max-automation counterpart to MasterSetup.
    ///
    /// Workflow:
    ///   1. Auto-detect LOC from project info / room data (eliminates SetLoc)
    ///   2. Auto-detect ZONE from room name patterns (eliminates SetZone)
    ///   3. Auto-populate tokens (DISC, PROD, SYS, FUNC, LVL) with family-aware PROD
    ///   4. Auto-tag all untagged elements (continues from existing sequence)
    ///   5. Combine parameters into ALL 37 containers (universal + discipline + MAT)
    ///
    /// Scope options:
    ///   - Active view only
    ///   - Selected elements only
    ///   - Entire project
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagAndCombineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Step 0: Choose scope
            TaskDialog scopeDlg = new TaskDialog("Tag & Combine All");
            scopeDlg.MainInstruction = "Tag and populate all containers";
            scopeDlg.MainContent =
                "This will:\n" +
                "  1. Auto-detect LOC/ZONE from spatial data\n" +
                "  2. Auto-populate all tokens (DISC, PROD, SYS, FUNC, LVL)\n" +
                "  3. Tag all untagged elements (continuing from existing numbers)\n" +
                "  4. Combine tokens into ALL 37 tag containers\n\n" +
                "Choose scope:";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active View",
                "Process only elements visible in the current view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Selected Elements",
                $"{uidoc.Selection.GetElementIds().Count} elements selected");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Entire Project",
                "Process all taggable elements across the entire model");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var scopeResult = scopeDlg.Show();

            ICollection<ElementId> targetIds;
            string scopeLabel;
            switch (scopeResult)
            {
                case TaskDialogResult.CommandLink1:
                    targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    scopeLabel = $"active view '{doc.ActiveView.Name}'";
                    break;
                case TaskDialogResult.CommandLink2:
                    targetIds = uidoc.Selection.GetElementIds();
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("Tag & Combine", "No elements selected.");
                        return Result.Cancelled;
                    }
                    scopeLabel = $"{targetIds.Count} selected elements";
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

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            var sw = Stopwatch.StartNew();

            // Pre-build room spatial index for LOC/ZONE auto-detection
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            int populated = 0;
            int tagged = 0;
            int combined = 0;
            int totalProcessed = 0;
            int locDetected = 0;
            int zoneDetected = 0;

            // Token parameter names for combine step
            string[] allTokenParams = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            };
            string[] shortIdTokens = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            };
            string[] sysRefTokens = new[]
            {
                "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT",
            };
            string[] locationTokens = new[]
            {
                "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT",
            };
            string[] systemTokens = new[]
            {
                "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
            };
            string[] line1Tokens = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT",
            };
            string[] line2Tokens = new[]
            {
                "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            };

            // Universal containers (apply to all tagged elements)
            var universalContainers = new (string param, string[] tokens)[]
            {
                ("ASS_TAG_1_TXT", allTokenParams),
                ("ASS_TAG_2_TXT", shortIdTokens),
                ("ASS_TAG_3_TXT", locationTokens),
                ("ASS_TAG_4_TXT", systemTokens),
                ("ASS_TAG_5_TXT", line1Tokens),
                ("ASS_TAG_6_TXT", line2Tokens),
            };

            // Discipline-specific containers (category-filtered)
            var disciplineContainers = new (string param, string[] tokens, HashSet<string> categories)[]
            {
                // HVAC Equipment
                ("HVC_EQP_TAG_01_TXT", allTokenParams, new HashSet<string> { "Mechanical Equipment" }),
                ("HVC_EQP_TAG_02_TXT", shortIdTokens, new HashSet<string> { "Mechanical Equipment" }),
                ("HVC_EQP_TAG_03_TXT", sysRefTokens, new HashSet<string> { "Mechanical Equipment" }),
                // HVAC Ductwork
                ("HVC_DCT_TAG_01_TXT", allTokenParams, new HashSet<string> { "Ducts", "Duct Fittings", "Flex Ducts", "Air Terminals", "Duct Accessories" }),
                ("HVC_DCT_TAG_02_TXT", shortIdTokens, new HashSet<string> { "Ducts", "Duct Fittings", "Flex Ducts", "Air Terminals", "Duct Accessories" }),
                ("HVC_DCT_TAG_03_TXT", systemTokens, new HashSet<string> { "Ducts", "Duct Fittings", "Flex Ducts", "Air Terminals", "Duct Accessories" }),
                // Flex Ducts
                ("HVC_FLX_TAG_01_TXT", allTokenParams, new HashSet<string> { "Flex Ducts" }),
                // Electrical Equipment
                ("ELC_EQP_TAG_01_TXT", allTokenParams, new HashSet<string> { "Electrical Equipment" }),
                ("ELC_EQP_TAG_02_TXT", shortIdTokens, new HashSet<string> { "Electrical Equipment" }),
                // Electrical Fixtures
                ("ELE_FIX_TAG_1_TXT", allTokenParams, new HashSet<string> { "Electrical Fixtures" }),
                ("ELE_FIX_TAG_2_TXT", shortIdTokens, new HashSet<string> { "Electrical Fixtures" }),
                // Lighting
                ("LTG_FIX_TAG_01_TXT", allTokenParams, new HashSet<string> { "Lighting Fixtures", "Lighting Devices" }),
                ("LTG_FIX_TAG_02_TXT", shortIdTokens, new HashSet<string> { "Lighting Fixtures", "Lighting Devices" }),
                // Pipework / Plumbing
                ("PLM_EQP_TAG_01_TXT", allTokenParams, new HashSet<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes", "Plumbing Fixtures" }),
                ("PLM_EQP_TAG_02_TXT", shortIdTokens, new HashSet<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes", "Plumbing Fixtures" }),
                // Fire & Life Safety
                ("FLS_DEV_TAG_01_TXT", allTokenParams, new HashSet<string> { "Sprinklers", "Fire Alarm Devices" }),
                ("FLS_DEV_TAG_02_TXT", shortIdTokens, new HashSet<string> { "Sprinklers", "Fire Alarm Devices" }),
                // Conduits
                ("ELC_CDT_TAG_01_TXT", allTokenParams, new HashSet<string> { "Conduits", "Conduit Fittings" }),
                ("ELC_CDT_TAG_02_TXT", shortIdTokens, new HashSet<string> { "Conduits", "Conduit Fittings" }),
                // Cable Trays
                ("ELC_CTR_TAG_01_TXT", allTokenParams, new HashSet<string> { "Cable Trays", "Cable Tray Fittings" }),
                // Communications / Low-voltage
                ("COM_DEV_TAG_01_TXT", allTokenParams, new HashSet<string> { "Communication Devices", "Telephone Devices" }),
                ("SEC_DEV_TAG_01_TXT", allTokenParams, new HashSet<string> { "Security Devices" }),
                ("NCL_DEV_TAG_01_TXT", allTokenParams, new HashSet<string> { "Nurse Call Devices" }),
                ("ICT_DEV_TAG_01_TXT", allTokenParams, new HashSet<string> { "Data Devices" }),
                // Material Tags
                ("MAT_TAG_1_TXT", allTokenParams, new HashSet<string> { "Walls", "Floors", "Ceilings", "Roofs", "Doors", "Windows" }),
                ("MAT_TAG_2_TXT", shortIdTokens, new HashSet<string> { "Walls", "Floors", "Ceilings", "Roofs", "Doors", "Windows" }),
                ("MAT_TAG_3_TXT", locationTokens, new HashSet<string> { "Walls", "Floors", "Ceilings", "Roofs" }),
                ("MAT_TAG_4_TXT", systemTokens, new HashSet<string> { "Walls", "Floors", "Ceilings", "Roofs" }),
                ("MAT_TAG_5_TXT", line1Tokens, new HashSet<string> { "Walls", "Floors", "Ceilings", "Roofs" }),
                ("MAT_TAG_6_TXT", line2Tokens, new HashSet<string> { "Walls", "Floors", "Ceilings", "Roofs" }),
            };

            using (Transaction tx = new Transaction(doc, "STING Tag & Combine All"))
            {
                tx.Start();

                foreach (ElementId id in targetIds)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !known.Contains(catName))
                        continue;

                    totalProcessed++;

                    // Step 1: Auto-detect LOC from spatial data
                    string existingLoc = ParameterHelpers.GetString(el, "ASS_LOC_TXT");
                    if (string.IsNullOrEmpty(existingLoc))
                    {
                        string detectedLoc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                        if (!string.IsNullOrEmpty(detectedLoc))
                        {
                            ParameterHelpers.SetIfEmpty(el, "ASS_LOC_TXT", detectedLoc);
                            locDetected++;
                            populated++;
                        }
                    }

                    // Step 2: Auto-detect ZONE from room data
                    string existingZone = ParameterHelpers.GetString(el, "ASS_ZONE_TXT");
                    if (string.IsNullOrEmpty(existingZone))
                    {
                        string detectedZone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                        if (!string.IsNullOrEmpty(detectedZone))
                        {
                            ParameterHelpers.SetIfEmpty(el, "ASS_ZONE_TXT", detectedZone);
                            zoneDetected++;
                            populated++;
                        }
                    }

                    // Step 3: Auto-populate tokens from category + family lookup
                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "XX";
                    if (ParameterHelpers.SetIfEmpty(el, "ASS_DISCIPLINE_COD_TXT", disc)) populated++;

                    // Family-aware PROD code: check family name before falling back to category
                    string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                    if (!string.IsNullOrEmpty(prod))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_PRODCT_COD_TXT", prod)) populated++;

                    // MEP system-aware SYS derivation (uses connected system name when available)
                    string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                    if (!string.IsNullOrEmpty(sys))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_SYSTEM_TYPE_TXT", sys)) populated++;
                    string func = TagConfig.GetFuncCode(sys);
                    if (!string.IsNullOrEmpty(func))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_FUNC_TXT", func)) populated++;
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl != "XX")
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", lvl)) populated++;

                    // Step 4: Tag if not already complete (with collision detection)
                    if (TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                        existingTags: tagIndex))
                        tagged++;

                    // Step 5: Combine into ALL containers (universal + discipline + material)
                    var tokenValues = new Dictionary<string, string>();
                    foreach (string param in allTokenParams)
                        tokenValues[param] = ParameterHelpers.GetString(el, param);

                    if (tokenValues.Values.Any(v => !string.IsNullOrEmpty(v)))
                    {
                        // Universal containers
                        foreach (var (param, tokens) in universalContainers)
                        {
                            var parts = tokens.Select(t => tokenValues.TryGetValue(t, out string v) ? v : "").ToList();
                            string assembled = string.Join(TagConfig.Separator, parts);
                            if (ParameterHelpers.SetString(el, param, assembled, overwrite: true))
                                combined++;
                        }

                        // Discipline-specific containers (category-filtered)
                        foreach (var (param, tokens, categories) in disciplineContainers)
                        {
                            if (!categories.Contains(catName)) continue;
                            var parts = tokens.Select(t => tokenValues.TryGetValue(t, out string v) ? v : "").ToList();
                            string assembled = string.Join(TagConfig.Separator, parts);
                            if (ParameterHelpers.SetString(el, param, assembled, overwrite: true))
                                combined++;
                        }
                    }
                }

                tx.Commit();
            }

            sw.Stop();

            var report = new StringBuilder();
            report.AppendLine("Tag & Combine All Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Scope:            {scopeLabel}");
            report.AppendLine($"  Processed:        {totalProcessed} elements");
            report.AppendLine($"  Populated:        {populated} token values");
            report.AppendLine($"  Tagged:           {tagged} new tags");
            report.AppendLine($"  Combined:         {combined} container values");
            if (locDetected > 0)
                report.AppendLine($"  LOC auto-detect:  {locDetected} (from rooms/project)");
            if (zoneDetected > 0)
                report.AppendLine($"  ZONE auto-detect: {zoneDetected} (from rooms)");
            report.AppendLine($"  Duration:         {sw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog("Tag & Combine All");
            td.MainInstruction = $"Processed {totalProcessed} elements ({combined} containers)";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagAndCombine: scope={scopeLabel}, processed={totalProcessed}, " +
                $"populated={populated}, tagged={tagged}, combined={combined}, " +
                $"locDetect={locDetected}, zoneDetect={zoneDetected}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
