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
    /// Naviate-style "Combine Parameters" command. Reads individual token parameters
    /// from each element and assembles them into ALL tag containers:
    ///   - ASS_TAG_1_TXT through ASS_TAG_6_TXT (universal containers)
    ///   - Discipline-specific containers (HVC_EQP_TAG_01_TXT, ELC_EQP_TAG_01_TXT, etc.)
    ///
    /// Each tag container has a configurable format:
    ///   - Which tokens to include (segment selection)
    ///   - Separator character (default "-")
    ///   - Prefix and suffix strings
    ///   - Max text lines (for multi-line label tags)
    ///
    /// This replaces the need for Naviate's "Configure" button by computing all
    /// tag variations automatically from the source tokens.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CombineParametersCommand : IExternalCommand
    {
        /// <summary>
        /// Defines how a tag container is assembled from source tokens.
        /// </summary>
        private class TagContainerDef
        {
            public string ParamName { get; set; }
            public string[] SourceTokens { get; set; }
            public string Separator { get; set; } = "-";
            public string Prefix { get; set; } = "";
            public string Suffix { get; set; } = "";
            public int MaxLines { get; set; } = 1;
            public int MaxCharsPerLine { get; set; } = 40;
        }

        // Source token parameter names in order
        private static readonly string[] AllTokenParams = new[]
        {
            "ASS_DISCIPLINE_COD_TXT",  // 0: DISC
            "ASS_LOC_TXT",             // 1: LOC
            "ASS_ZONE_TXT",            // 2: ZONE
            "ASS_LVL_COD_TXT",         // 3: LVL
            "ASS_SYSTEM_TYPE_TXT",     // 4: SYS
            "ASS_FUNC_TXT",            // 5: FUNC
            "ASS_PRODCT_COD_TXT",      // 6: PROD
            "ASS_SEQ_NUM_TXT",         // 7: SEQ
        };

        /// <summary>
        /// Universal tag container definitions. Each ASS_TAG_N_TXT gets a different
        /// combination of tokens for different label/schedule uses:
        ///   TAG_1: Full 8-segment tag (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ)
        ///   TAG_2: Short ID (DISC-PROD-SEQ) — compact label for drawings
        ///   TAG_3: Location ref (LOC-ZONE-LVL) — spatial reference
        ///   TAG_4: System ref (SYS-FUNC) — system classification
        ///   TAG_5: Full tag, line 1 (DISC-LOC-ZONE-LVL) — multi-line top
        ///   TAG_6: Full tag, line 2 (SYS-FUNC-PROD-SEQ) — multi-line bottom
        /// </summary>
        private static readonly TagContainerDef[] UniversalContainers = new[]
        {
            new TagContainerDef
            {
                ParamName = "ASS_TAG_1_TXT",
                SourceTokens = AllTokenParams,
                Separator = "-",
            },
            new TagContainerDef
            {
                ParamName = "ASS_TAG_2_TXT",
                SourceTokens = new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" },
                Separator = "-",
            },
            new TagContainerDef
            {
                ParamName = "ASS_TAG_3_TXT",
                SourceTokens = new[] { "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT" },
                Separator = "-",
            },
            new TagContainerDef
            {
                ParamName = "ASS_TAG_4_TXT",
                SourceTokens = new[] { "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT" },
                Separator = "-",
            },
            new TagContainerDef
            {
                ParamName = "ASS_TAG_5_TXT",
                SourceTokens = new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT" },
                Separator = "-",
                MaxLines = 1,
            },
            new TagContainerDef
            {
                ParamName = "ASS_TAG_6_TXT",
                SourceTokens = new[] { "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" },
                Separator = "-",
                MaxLines = 1,
            },
        };

        /// <summary>
        /// Discipline-specific tag mappings. Maps each discipline tag parameter
        /// to the Revit categories it applies to, and which tokens to assemble.
        /// These tags appear in discipline-specific tag families.
        /// </summary>
        private static readonly DisciplineTagDef[] DisciplineTags = new[]
        {
            // HVAC Equipment — full tag
            new DisciplineTagDef("HVC_EQP_TAG_01_TXT", "Mechanical Equipment",
                AllTokenParams),
            // HVAC Equipment — short ID
            new DisciplineTagDef("HVC_EQP_TAG_02_TXT", "Mechanical Equipment",
                new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            // HVAC Equipment — system ref
            new DisciplineTagDef("HVC_EQP_TAG_03_TXT", "Mechanical Equipment",
                new[] { "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT" }),
            // Duct tags
            new DisciplineTagDef("HVC_DCT_TAG_01_TXT",
                new[] { "Ducts", "Duct Fittings", "Flex Ducts", "Air Terminals", "Duct Accessories" },
                AllTokenParams),
            new DisciplineTagDef("HVC_DCT_TAG_02_TXT",
                new[] { "Ducts", "Duct Fittings", "Flex Ducts", "Air Terminals", "Duct Accessories" },
                new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            new DisciplineTagDef("HVC_DCT_TAG_03_TXT",
                new[] { "Ducts", "Duct Fittings", "Flex Ducts", "Air Terminals", "Duct Accessories" },
                new[] { "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT" }),
            new DisciplineTagDef("HVC_FLX_TAG_01_TXT", "Flex Ducts", AllTokenParams),
            // Electrical Equipment
            new DisciplineTagDef("ELC_EQP_TAG_01_TXT", "Electrical Equipment", AllTokenParams),
            new DisciplineTagDef("ELC_EQP_TAG_02_TXT", "Electrical Equipment",
                new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            // Electrical Fixtures + Lighting
            new DisciplineTagDef("ELE_FIX_TAG_1_TXT", "Electrical Fixtures", AllTokenParams),
            new DisciplineTagDef("ELE_FIX_TAG_2_TXT", "Electrical Fixtures",
                new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            new DisciplineTagDef("LTG_FIX_TAG_01_TXT",
                new[] { "Lighting Fixtures", "Lighting Devices" }, AllTokenParams),
            new DisciplineTagDef("LTG_FIX_TAG_02_TXT",
                new[] { "Lighting Fixtures", "Lighting Devices" },
                new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            // Pipework
            new DisciplineTagDef("PLM_EQP_TAG_01_TXT",
                new[] { "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes", "Plumbing Fixtures" },
                AllTokenParams),
            new DisciplineTagDef("PLM_EQP_TAG_02_TXT",
                new[] { "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes", "Plumbing Fixtures" },
                new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            // Fire & Life Safety
            new DisciplineTagDef("FLS_DEV_TAG_01_TXT",
                new[] { "Sprinklers", "Fire Alarm Devices" }, AllTokenParams),
            new DisciplineTagDef("FLS_DEV_TAG_02_TXT",
                new[] { "Sprinklers", "Fire Alarm Devices" },
                new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            // Conduits
            new DisciplineTagDef("ELC_CDT_TAG_01_TXT",
                new[] { "Conduits", "Conduit Fittings" }, AllTokenParams),
            new DisciplineTagDef("ELC_CDT_TAG_02_TXT",
                new[] { "Conduits", "Conduit Fittings" },
                new[] { "ASS_DISCIPLINE_COD_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" }),
            // Cable Trays
            new DisciplineTagDef("ELC_CTR_TAG_01_TXT",
                new[] { "Cable Trays", "Cable Tray Fittings" }, AllTokenParams),
            // Low-voltage / Communications
            new DisciplineTagDef("COM_DEV_TAG_01_TXT",
                new[] { "Communication Devices", "Telephone Devices" }, AllTokenParams),
            new DisciplineTagDef("SEC_DEV_TAG_01_TXT", "Security Devices", AllTokenParams),
            new DisciplineTagDef("NCL_DEV_TAG_01_TXT", "Nurse Call Devices", AllTokenParams),
            new DisciplineTagDef("ICT_DEV_TAG_01_TXT", "Data Devices", AllTokenParams),
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();
            var knownCategories = new HashSet<string>(TagConfig.DiscMap.Keys);

            int universalUpdated = 0;
            int disciplineUpdated = 0;
            int totalElements = 0;
            int skippedNoDisc = 0;

            using (Transaction tx = new Transaction(doc, "STING Combine Parameters"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !knownCategories.Contains(catName))
                        continue;

                    // Skip elements with no DISC token (not yet tagged)
                    string disc = ParameterHelpers.GetString(el, "ASS_DISCIPLINE_COD_TXT");
                    if (string.IsNullOrEmpty(disc))
                    {
                        skippedNoDisc++;
                        continue;
                    }

                    totalElements++;

                    // Read all source token values once
                    var tokenValues = new Dictionary<string, string>();
                    foreach (string param in AllTokenParams)
                        tokenValues[param] = ParameterHelpers.GetString(el, param);

                    // Assemble universal containers (ASS_TAG_1 through ASS_TAG_6)
                    foreach (var container in UniversalContainers)
                    {
                        string assembled = AssembleTag(tokenValues, container);
                        if (!string.IsNullOrEmpty(assembled))
                        {
                            if (ParameterHelpers.SetString(el, container.ParamName, assembled, overwrite: true))
                                universalUpdated++;
                        }
                    }

                    // Assemble discipline-specific containers
                    foreach (var dtag in DisciplineTags)
                    {
                        if (!dtag.AppliesTo(catName)) continue;

                        string assembled = AssembleFromTokens(tokenValues, dtag.SourceTokens, "-", "", "");
                        if (!string.IsNullOrEmpty(assembled))
                        {
                            if (ParameterHelpers.SetString(el, dtag.ParamName, assembled, overwrite: true))
                                disciplineUpdated++;
                        }
                    }
                }

                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine("Combine Parameters Complete");
            report.AppendLine(new string('─', 40));
            report.AppendLine($"Elements processed:     {totalElements}");
            report.AppendLine($"Universal tags written:  {universalUpdated}");
            report.AppendLine($"Discipline tags written: {disciplineUpdated}");
            if (skippedNoDisc > 0)
                report.AppendLine($"Skipped (no DISC code):  {skippedNoDisc}");
            report.AppendLine();
            report.AppendLine("Tag containers populated:");
            report.AppendLine("  ASS_TAG_1: Full 8-segment tag");
            report.AppendLine("  ASS_TAG_2: Short ID (DISC-PROD-SEQ)");
            report.AppendLine("  ASS_TAG_3: Location (LOC-ZONE-LVL)");
            report.AppendLine("  ASS_TAG_4: System (SYS-FUNC)");
            report.AppendLine("  ASS_TAG_5: Multi-line top (DISC-LOC-ZONE-LVL)");
            report.AppendLine("  ASS_TAG_6: Multi-line bottom (SYS-FUNC-PROD-SEQ)");
            report.AppendLine($"  + {DisciplineTags.Length} discipline-specific tags");

            TaskDialog td = new TaskDialog("Combine Parameters");
            td.MainInstruction = $"Combined parameters for {totalElements} elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"CombineParameters: {totalElements} elements, " +
                $"{universalUpdated} universal, {disciplineUpdated} discipline tags");

            return Result.Succeeded;
        }

        private static string AssembleTag(Dictionary<string, string> tokenValues,
            TagContainerDef container)
        {
            string raw = AssembleFromTokens(tokenValues, container.SourceTokens,
                container.Separator, container.Prefix, container.Suffix);

            if (string.IsNullOrEmpty(raw))
                return null;

            // Apply multi-line splitting if MaxLines > 1
            if (container.MaxLines > 1 && raw.Length > container.MaxCharsPerLine)
            {
                var lines = new List<string>();
                int tokenCount = container.SourceTokens.Length;
                int tokensPerLine = (int)Math.Ceiling((double)tokenCount / container.MaxLines);

                for (int i = 0; i < container.MaxLines; i++)
                {
                    int start = i * tokensPerLine;
                    int count = Math.Min(tokensPerLine, tokenCount - start);
                    if (count <= 0) break;

                    var lineTokens = container.SourceTokens.Skip(start).Take(count).ToArray();
                    string line = AssembleFromTokens(tokenValues, lineTokens,
                        container.Separator, "", "");
                    if (!string.IsNullOrEmpty(line))
                        lines.Add(line);
                }

                return container.Prefix + string.Join("\n", lines) + container.Suffix;
            }

            return raw;
        }

        private static string AssembleFromTokens(Dictionary<string, string> tokenValues,
            string[] sourceTokens, string separator, string prefix, string suffix)
        {
            var parts = new List<string>();
            foreach (string param in sourceTokens)
            {
                string val = tokenValues.TryGetValue(param, out string v) ? v : "";
                parts.Add(val);
            }

            // Check at least one token is non-empty
            if (parts.All(p => string.IsNullOrEmpty(p)))
                return null;

            return prefix + string.Join(separator, parts) + suffix;
        }

        private class DisciplineTagDef
        {
            public string ParamName { get; }
            public HashSet<string> Categories { get; }
            public string[] SourceTokens { get; }

            public DisciplineTagDef(string paramName, string category, string[] sourceTokens)
            {
                ParamName = paramName;
                Categories = new HashSet<string> { category };
                SourceTokens = sourceTokens;
            }

            public DisciplineTagDef(string paramName, string[] categories, string[] sourceTokens)
            {
                ParamName = paramName;
                Categories = new HashSet<string>(categories);
                SourceTokens = sourceTokens;
            }

            public bool AppliesTo(string categoryName)
            {
                return Categories.Contains(categoryName);
            }
        }
    }
}
