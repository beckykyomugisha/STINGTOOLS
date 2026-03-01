using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Naviate-style "Combine Parameters" command with interactive selection.
    ///
    /// Presents a multi-step dialog where the user:
    ///   Step 1: Chooses a mode (All Containers, Universal Only, Discipline Only, Pick Containers)
    ///   Step 2: In "Pick" mode, selects which tag container groups to populate
    ///
    /// Each tag container assembles source tokens with configurable:
    ///   - Segment selection (which tokens to include)
    ///   - Separator character
    ///   - Prefix and suffix strings
    ///
    /// Containers supported (16 groups, 37 parameters):
    ///   - Universal: ASS_TAG_1 through ASS_TAG_6
    ///   - HVAC: HVC_EQP_TAG, HVC_DCT_TAG, HVC_FLX_TAG
    ///   - Electrical: ELC_EQP_TAG, ELE_FIX_TAG, LTG_FIX_TAG, ELC_CDT_TAG, ELC_CTR_TAG
    ///   - Plumbing: PLM_EQP_TAG
    ///   - Fire/Safety: FLS_DEV_TAG
    ///   - Comms/LV: COM_DEV_TAG, SEC_DEV_TAG, NCL_DEV_TAG, ICT_DEV_TAG
    ///   - Material: MAT_TAG_1 through MAT_TAG_6
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CombineParametersCommand : IExternalCommand
    {
        // ── Source token parameter names (all from TagConfig) ──────────
        private static readonly string[] AllTokenParams = TagConfig.TokenParamNames;
        private static readonly string[] ShortIdTokens = TagConfig.ShortIdTokens;
        private static readonly string[] LocationTokens = TagConfig.LocationTokens;
        private static readonly string[] SystemTokens = TagConfig.SystemTokens;
        private static readonly string[] Line1Tokens = TagConfig.Line1Tokens;
        private static readonly string[] Line2Tokens = TagConfig.Line2Tokens;
        private static readonly string[] SysRefTokens = TagConfig.SysRefTokens;

        // ── Container group definitions ──────────────────────────────

        /// <summary>All selectable container groups for the interactive UI.</summary>
        private static readonly ContainerGroup[] AllGroups = new[]
        {
            // Universal (applies to all tagged elements)
            new ContainerGroup("Universal (ASS_TAG_1-6)", "UNIVERSAL", null, new[]
            {
                new ContainerDef("ASS_TAG_1_TXT", AllTokenParams,  "-", "", "", "Full 8-segment tag"),
                new ContainerDef("ASS_TAG_2_TXT", ShortIdTokens,   "-", "", "", "Short ID (DISC-PROD-SEQ)"),
                new ContainerDef("ASS_TAG_3_TXT", LocationTokens,  "-", "", "", "Location (LOC-ZONE-LVL)"),
                new ContainerDef("ASS_TAG_4_TXT", SystemTokens,    "-", "", "", "System (SYS-FUNC)"),
                new ContainerDef("ASS_TAG_5_TXT", Line1Tokens,     "-", "", "", "Multi-line top"),
                new ContainerDef("ASS_TAG_6_TXT", Line2Tokens,     "-", "", "", "Multi-line bottom"),
            }),

            // HVAC Equipment
            new ContainerGroup("HVAC Equipment", "HVC_EQP",
                new[] { "Mechanical Equipment" }, new[]
            {
                new ContainerDef("HVC_EQP_TAG_01_TXT", AllTokenParams,  "-", "", "", "Full tag"),
                new ContainerDef("HVC_EQP_TAG_02_TXT", ShortIdTokens,   "-", "", "", "Short ID"),
                new ContainerDef("HVC_EQP_TAG_03_TXT", SysRefTokens,    "-", "", "", "System ref"),
            }),

            // HVAC Ductwork
            new ContainerGroup("HVAC Ductwork", "HVC_DCT",
                new[] { "Ducts", "Duct Fittings", "Flex Ducts", "Air Terminals", "Duct Accessories" }, new[]
            {
                new ContainerDef("HVC_DCT_TAG_01_TXT", AllTokenParams,  "-", "", "", "Full tag"),
                new ContainerDef("HVC_DCT_TAG_02_TXT", ShortIdTokens,   "-", "", "", "Short ID"),
                new ContainerDef("HVC_DCT_TAG_03_TXT", SystemTokens,    "-", "", "", "System"),
            }),

            // Flex Ducts
            new ContainerGroup("Flex Ducts", "HVC_FLX",
                new[] { "Flex Ducts" }, new[]
            {
                new ContainerDef("HVC_FLX_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
            }),

            // Electrical Equipment
            new ContainerGroup("Electrical Equipment", "ELC_EQP",
                new[] { "Electrical Equipment" }, new[]
            {
                new ContainerDef("ELC_EQP_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
                new ContainerDef("ELC_EQP_TAG_02_TXT", ShortIdTokens,  "-", "", "", "Short ID"),
            }),

            // Electrical Fixtures
            new ContainerGroup("Electrical Fixtures", "ELE_FIX",
                new[] { "Electrical Fixtures" }, new[]
            {
                new ContainerDef("ELE_FIX_TAG_1_TXT", AllTokenParams, "-", "", "", "Full tag"),
                new ContainerDef("ELE_FIX_TAG_2_TXT", ShortIdTokens,  "-", "", "", "Short ID"),
            }),

            // Lighting
            new ContainerGroup("Lighting", "LTG_FIX",
                new[] { "Lighting Fixtures", "Lighting Devices" }, new[]
            {
                new ContainerDef("LTG_FIX_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
                new ContainerDef("LTG_FIX_TAG_02_TXT", ShortIdTokens,  "-", "", "", "Short ID"),
            }),

            // Pipework / Plumbing
            new ContainerGroup("Pipework / Plumbing", "PLM_EQP",
                new[] { "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes", "Plumbing Fixtures" }, new[]
            {
                new ContainerDef("PLM_EQP_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
                new ContainerDef("PLM_EQP_TAG_02_TXT", ShortIdTokens,  "-", "", "", "Short ID"),
            }),

            // Fire & Life Safety
            new ContainerGroup("Fire & Life Safety", "FLS_DEV",
                new[] { "Sprinklers", "Fire Alarm Devices" }, new[]
            {
                new ContainerDef("FLS_DEV_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
                new ContainerDef("FLS_DEV_TAG_02_TXT", ShortIdTokens,  "-", "", "", "Short ID"),
            }),

            // Conduits
            new ContainerGroup("Conduits", "ELC_CDT",
                new[] { "Conduits", "Conduit Fittings" }, new[]
            {
                new ContainerDef("ELC_CDT_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
                new ContainerDef("ELC_CDT_TAG_02_TXT", ShortIdTokens,  "-", "", "", "Short ID"),
            }),

            // Cable Trays
            new ContainerGroup("Cable Trays", "ELC_CTR",
                new[] { "Cable Trays", "Cable Tray Fittings" }, new[]
            {
                new ContainerDef("ELC_CTR_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
            }),

            // Communications / Low-voltage
            new ContainerGroup("Communications", "COM_DEV",
                new[] { "Communication Devices", "Telephone Devices" }, new[]
            {
                new ContainerDef("COM_DEV_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
            }),
            new ContainerGroup("Security", "SEC_DEV",
                new[] { "Security Devices" }, new[]
            {
                new ContainerDef("SEC_DEV_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
            }),
            new ContainerGroup("Nurse Call", "NCL_DEV",
                new[] { "Nurse Call Devices" }, new[]
            {
                new ContainerDef("NCL_DEV_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
            }),
            new ContainerGroup("ICT / Data", "ICT_DEV",
                new[] { "Data Devices" }, new[]
            {
                new ContainerDef("ICT_DEV_TAG_01_TXT", AllTokenParams, "-", "", "", "Full tag"),
            }),

            // Material Tags (for compound-structure elements)
            new ContainerGroup("Material Tags (MAT_TAG_1-6)", "MAT_TAG",
                new[] { "Walls", "Floors", "Ceilings", "Roofs", "Doors", "Windows" }, new[]
            {
                new ContainerDef("MAT_TAG_1_TXT", AllTokenParams,  "-", "", "", "Full tag"),
                new ContainerDef("MAT_TAG_2_TXT", ShortIdTokens,   "-", "", "", "Short ID"),
                new ContainerDef("MAT_TAG_3_TXT", LocationTokens,  "-", "", "", "Location"),
                new ContainerDef("MAT_TAG_4_TXT", SystemTokens,    "-", "", "", "System"),
                new ContainerDef("MAT_TAG_5_TXT", Line1Tokens,     "-", "", "", "Line 1"),
                new ContainerDef("MAT_TAG_6_TXT", Line2Tokens,     "-", "", "", "Line 2"),
            }),
        };

        // ── Main Execute ─────────────────────────────────────────────

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Step 1: Mode selection
            TaskDialog modeDlg = new TaskDialog("Combine Parameters");
            modeDlg.MainInstruction = "Which tag containers to populate?";
            modeDlg.MainContent =
                "Reads token parameters (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ) " +
                "and assembles them into tag container parameters.\n\n" +
                $"Available: {AllGroups.Length} groups, " +
                $"{AllGroups.Sum(g => g.Containers.Length)} total containers";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "All Containers",
                $"Populate all {AllGroups.Length} groups ({AllGroups.Sum(g => g.Containers.Length)} parameters)");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Universal Only (ASS_TAG_1-6)",
                "6 universal containers applied to all tagged elements");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Discipline Only",
                "MEP + Comms discipline-specific containers (excludes Universal and Material)");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Pick Container Groups...",
                "Interactively choose which groups to populate");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var modeResult = modeDlg.Show();

            HashSet<string> selectedGroupIds;
            switch (modeResult)
            {
                case TaskDialogResult.CommandLink1:
                    selectedGroupIds = new HashSet<string>(AllGroups.Select(g => g.GroupId));
                    break;
                case TaskDialogResult.CommandLink2:
                    selectedGroupIds = new HashSet<string> { "UNIVERSAL" };
                    break;
                case TaskDialogResult.CommandLink3:
                    selectedGroupIds = new HashSet<string>(
                        AllGroups.Where(g => g.GroupId != "UNIVERSAL" && g.GroupId != "MAT_TAG")
                                 .Select(g => g.GroupId));
                    break;
                case TaskDialogResult.CommandLink4:
                    selectedGroupIds = ShowGroupPicker(doc);
                    if (selectedGroupIds == null || selectedGroupIds.Count == 0)
                        return Result.Cancelled;
                    break;
                default:
                    return Result.Cancelled;
            }

            var activeGroups = AllGroups.Where(g => selectedGroupIds.Contains(g.GroupId)).ToArray();
            return ExecuteCombine(doc, activeGroups);
        }

        // ── Group picker: paged TaskDialog selection ─────────────────

        private HashSet<string> ShowGroupPicker(Document doc)
        {
            // Count elements per category to show relevance
            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);
            var catCounts = new Dictionary<string, int>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCategories.Contains(cat)) continue;
                if (catCounts.ContainsKey(cat)) catCounts[cat]++;
                else catCounts[cat] = 1;
            }

            var selected = new HashSet<string>();
            int page = 0;
            int pageSize = 4; // Revit TaskDialog supports max 4 command links

            while (true)
            {
                int start = page * pageSize;
                if (start >= AllGroups.Length)
                {
                    // Wrap around or finish
                    if (selected.Count > 0) break;
                    page = 0;
                    start = 0;
                }

                int count = Math.Min(pageSize, AllGroups.Length - start);
                var pageGroups = AllGroups.Skip(start).Take(count).ToArray();
                int totalPages = (int)Math.Ceiling((double)AllGroups.Length / pageSize);

                TaskDialog picker = new TaskDialog("Select Container Groups");
                picker.MainInstruction = $"Toggle groups (page {page + 1}/{totalPages})";
                picker.MainContent = selected.Count > 0
                    ? $"Selected: {string.Join(", ", AllGroups.Where(g => selected.Contains(g.GroupId)).Select(g => g.Label))}"
                    : "Click a group to select/deselect it. Cancel when done selecting.";

                for (int i = 0; i < pageGroups.Length; i++)
                {
                    var g = pageGroups[i];
                    int elemCount = g.Categories != null
                        ? g.Categories.Sum(c => catCounts.TryGetValue(c, out int n) ? n : 0)
                        : catCounts.Values.Sum();
                    string mark = selected.Contains(g.GroupId) ? "[X] " : "[ ] ";
                    picker.AddCommandLink(
                        (TaskDialogCommandLinkId)(i + 201),
                        $"{mark}{g.Label}",
                        $"{g.Containers.Length} containers | {elemCount} elements");
                }

                picker.CommonButtons = TaskDialogCommonButtons.Cancel;

                var pickResult = picker.Show();

                int linkIndex = -1;
                switch (pickResult)
                {
                    case TaskDialogResult.CommandLink1: linkIndex = 0; break;
                    case TaskDialogResult.CommandLink2: linkIndex = 1; break;
                    case TaskDialogResult.CommandLink3: linkIndex = 2; break;
                    case TaskDialogResult.CommandLink4: linkIndex = 3; break;
                    default:
                        // Cancel or close → advance page, finish if already past end
                        page++;
                        if (page * pageSize >= AllGroups.Length)
                            break;
                        continue;
                }

                if (linkIndex >= 0 && linkIndex < pageGroups.Length)
                {
                    string id = pageGroups[linkIndex].GroupId;
                    if (selected.Contains(id))
                        selected.Remove(id);
                    else
                        selected.Add(id);
                    continue; // Stay on same page for more toggles
                }
            }

            return selected.Count > 0 ? selected : null;
        }

        // ── Core combine logic ───────────────────────────────────────

        private Result ExecuteCombine(Document doc, ContainerGroup[] activeGroups)
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);

            int totalElements = 0;
            int totalWrites = 0;
            int skippedNoDisc = 0;
            var writesPerGroup = new Dictionary<string, int>();

            foreach (var g in activeGroups)
                writesPerGroup[g.GroupId] = 0;

            using (Transaction tx = new Transaction(doc, "STING Combine Parameters"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !knownCategories.Contains(catName))
                        continue;

                    string disc = ParameterHelpers.GetString(el, "ASS_DISCIPLINE_COD_TXT");
                    if (string.IsNullOrEmpty(disc))
                    {
                        skippedNoDisc++;
                        continue;
                    }

                    totalElements++;

                    // Read all source tokens once
                    var tokenValues = new Dictionary<string, string>();
                    foreach (string param in AllTokenParams)
                        tokenValues[param] = ParameterHelpers.GetString(el, param);

                    foreach (var group in activeGroups)
                    {
                        if (group.Categories != null && !group.Categories.Contains(catName))
                            continue;

                        foreach (var container in group.Containers)
                        {
                            string assembled = AssembleFromTokens(
                                tokenValues, container.SourceTokens,
                                container.Separator, container.Prefix, container.Suffix);

                            if (!string.IsNullOrEmpty(assembled))
                            {
                                if (ParameterHelpers.SetString(el, container.ParamName,
                                    assembled, overwrite: true))
                                {
                                    totalWrites++;
                                    writesPerGroup[group.GroupId]++;
                                }
                            }
                        }
                    }
                }

                tx.Commit();
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine("Combine Parameters Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Elements processed:  {totalElements}");
            report.AppendLine($"  Parameters written:  {totalWrites}");
            if (skippedNoDisc > 0)
                report.AppendLine($"  Skipped (untagged):  {skippedNoDisc}");
            report.AppendLine();
            report.AppendLine("Container groups populated:");
            report.AppendLine($"  {"Group",-35} {"Writes",7}");
            report.AppendLine($"  {new string('─', 43)}");
            foreach (var group in activeGroups)
            {
                int w = writesPerGroup[group.GroupId];
                report.AppendLine($"  {group.Label,-35} {w,7}");
                foreach (var c in group.Containers)
                    report.AppendLine($"    -> {c.ParamName,-28} {c.Description}");
            }

            TaskDialog td = new TaskDialog("Combine Parameters");
            td.MainInstruction = $"Combined {totalWrites} parameters across {totalElements} elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"CombineParameters: {totalElements} elements, {totalWrites} writes, " +
                $"{activeGroups.Length} groups");

            return Result.Succeeded;
        }

        // ── Token assembly ───────────────────────────────────────────

        private static string AssembleFromTokens(Dictionary<string, string> tokenValues,
            string[] sourceTokens, string separator, string prefix, string suffix)
        {
            var parts = new List<string>();
            foreach (string param in sourceTokens)
            {
                string val = tokenValues.TryGetValue(param, out string v) ? v : "";
                parts.Add(val);
            }

            if (parts.All(p => string.IsNullOrEmpty(p)))
                return null;

            return prefix + string.Join(separator, parts) + suffix;
        }

        // ── Data types ───────────────────────────────────────────────

        private class ContainerDef
        {
            public string ParamName { get; }
            public string[] SourceTokens { get; }
            public string Separator { get; }
            public string Prefix { get; }
            public string Suffix { get; }
            public string Description { get; }

            public ContainerDef(string paramName, string[] sourceTokens,
                string separator, string prefix, string suffix, string description)
            {
                ParamName = paramName;
                SourceTokens = sourceTokens;
                Separator = separator;
                Prefix = prefix;
                Suffix = suffix;
                Description = description;
            }
        }

        private class ContainerGroup
        {
            public string Label { get; }
            public string GroupId { get; }
            /// <summary>Categories this group applies to. Null = all categories.</summary>
            public HashSet<string> Categories { get; }
            public ContainerDef[] Containers { get; }

            public ContainerGroup(string label, string groupId,
                string[] categories, ContainerDef[] containers)
            {
                Label = label;
                GroupId = groupId;
                Categories = categories != null ? new HashSet<string>(categories) : null;
                Containers = containers;
            }
        }
    }
}
