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
    /// Manual token writer commands — the 13 individual ISO 19650 token setters
    /// from the STINGTags v9.6 CREATE tab. Each sets a single token on all
    /// selected elements (or all taggable elements if nothing is selected).
    /// Includes PROJ, ORIG, VOL, LVL, DISC, LOC, ZONE, SYS, FUNC, PROD, SEQ, STATUS, REV.
    /// </summary>
    internal static class TokenWriter
    {
        public static Result WriteToken(ExternalCommandData cmd, string paramName,
            string label, string[] options)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Build target set: selected elements or all taggable in view
            var targetIds = uidoc.Selection.GetElementIds();
            bool usingSelection = targetIds.Count > 0;

            if (!usingSelection)
            {
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
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

            // Show options dialog
            TaskDialog td = new TaskDialog(label);
            td.MainInstruction = $"Set {label} on {targetIds.Count} elements";
            td.MainContent = usingSelection ? "(Selected elements)" : "(All taggable in view)";

            for (int i = 0; i < Math.Min(options.Length, 4); i++)
            {
                td.AddCommandLink((TaskDialogCommandLinkId)(i + 201), options[i]);
            }
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = td.Show();
            string value = null;

            switch (result)
            {
                case TaskDialogResult.CommandLink1: value = options.Length > 0 ? options[0] : null; break;
                case TaskDialogResult.CommandLink2: value = options.Length > 1 ? options[1] : null; break;
                case TaskDialogResult.CommandLink3: value = options.Length > 2 ? options[2] : null; break;
                case TaskDialogResult.CommandLink4: value = options.Length > 3 ? options[3] : null; break;
                default: return Result.Cancelled;
            }

            if (value == null) return Result.Cancelled;

            int written = 0;
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
                tx.Commit();
            }

            TaskDialog.Show(label, $"Set '{value}' on {written} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>Set the DISC (discipline) token on selected/view elements.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetDiscCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, "ASS_DISCIPLINE_COD_TXT", "Discipline (DISC)",
                new[] { "M", "E", "P", "A" });
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetLocCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, "ASS_LOC_TXT", "Location (LOC)",
                TagConfig.LocCodes.ToArray());
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetZoneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, "ASS_ZONE_TXT", "Zone (ZONE)",
                TagConfig.ZoneCodes.ToArray());
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
            => TokenWriter.WriteToken(cmd, "ASS_STATUS_TXT", "Status",
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
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Determine scope
            var targetIds = uidoc.Selection.GetElementIds();
            if (targetIds.Count == 0)
            {
                targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id).ToList();
            }

            // Use shared sequence counter scan (continues from highest existing SEQ per group)
            var maxSeq = TagConfig.GetExistingSequenceCounters(doc);

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
                    string existing = ParameterHelpers.GetString(elem, "ASS_SEQ_NUM_TXT");
                    if (!string.IsNullOrEmpty(existing)) continue;

                    string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                    string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                    string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                    if (string.IsNullOrEmpty(disc))
                    {
                        disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "XX";
                        ParameterHelpers.SetIfEmpty(elem, "ASS_DISCIPLINE_COD_TXT", disc);
                    }
                    if (string.IsNullOrEmpty(sys))
                    {
                        sys = TagConfig.GetSysCode(cat);
                        ParameterHelpers.SetIfEmpty(elem, "ASS_SYSTEM_TYPE_TXT", sys);
                    }
                    if (string.IsNullOrEmpty(lvl))
                    {
                        lvl = ParameterHelpers.GetLevelCode(doc, elem);
                        ParameterHelpers.SetIfEmpty(elem, "ASS_LVL_COD_TXT", lvl);
                    }

                    string key = $"{disc}_{sys}_{lvl}";
                    if (!maxSeq.ContainsKey(key)) maxSeq[key] = 0;
                    maxSeq[key]++;
                    string seq = maxSeq[key].ToString().PadLeft(TagConfig.NumPad, '0');
                    ParameterHelpers.SetString(elem, "ASS_SEQ_NUM_TXT", seq, overwrite: true);
                    assigned++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Assign Numbers", $"Assigned sequence numbers to {assigned} elements.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Build/rebuild assembled tags (ASS_TAG_1_TXT) from existing individual token
    /// parameters without changing any token values. Respects existing LOC/ZONE values.
    ///
    /// Enhanced: writes ALL 37 tag containers (not just ASS_TAG_1_TXT) with
    /// collision detection — auto-increments SEQ if a duplicate tag is detected.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BuildTagsCommand : IExternalCommand
    {
        /// <summary>All 8 source token parameters in tag order.</summary>
        private static readonly string[] AllTokenParams = new[]
        {
            "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
            "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
            "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
        };

        /// <summary>
        /// Container definitions for all 37 tag parameters across 16 groups.
        /// Each tuple: (paramName, sourceTokenIndices, separator).
        /// Indices refer to AllTokenParams: 0=DISC,1=LOC,2=ZONE,3=LVL,4=SYS,5=FUNC,6=PROD,7=SEQ.
        /// </summary>
        private static readonly (string param, int[] tokens, string sep, string[] categories)[] Containers = new[]
        {
            // Universal (all categories)
            ("ASS_TAG_1_TXT", new[] {0,1,2,3,4,5,6,7}, "-", (string[])null),
            ("ASS_TAG_2_TXT", new[] {0,6,7},            "-", (string[])null),
            ("ASS_TAG_3_TXT", new[] {1,2,3},            "-", (string[])null),
            ("ASS_TAG_4_TXT", new[] {4,5},              "-", (string[])null),
            ("ASS_TAG_5_TXT", new[] {0,1,2,3},          "-", (string[])null),
            ("ASS_TAG_6_TXT", new[] {4,5,6,7},          "-", (string[])null),
            // HVAC Equipment
            ("HVC_EQP_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Mechanical Equipment"}),
            ("HVC_EQP_TAG_02_TXT", new[] {0,6,7},            "-", new[] {"Mechanical Equipment"}),
            ("HVC_EQP_TAG_03_TXT", new[] {4,5,6},            "-", new[] {"Mechanical Equipment"}),
            // HVAC Ductwork
            ("HVC_DCT_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Ducts","Duct Fittings","Flex Ducts","Air Terminals","Duct Accessories"}),
            ("HVC_DCT_TAG_02_TXT", new[] {0,6,7},            "-", new[] {"Ducts","Duct Fittings","Flex Ducts","Air Terminals","Duct Accessories"}),
            ("HVC_DCT_TAG_03_TXT", new[] {4,5},              "-", new[] {"Ducts","Duct Fittings","Flex Ducts","Air Terminals","Duct Accessories"}),
            // Flex Ducts
            ("HVC_FLX_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Flex Ducts"}),
            // Electrical Equipment
            ("ELC_EQP_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Electrical Equipment"}),
            ("ELC_EQP_TAG_02_TXT", new[] {0,6,7},            "-", new[] {"Electrical Equipment"}),
            // Electrical Fixtures
            ("ELE_FIX_TAG_1_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Electrical Fixtures"}),
            ("ELE_FIX_TAG_2_TXT", new[] {0,6,7},            "-", new[] {"Electrical Fixtures"}),
            // Lighting
            ("LTG_FIX_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Lighting Fixtures","Lighting Devices"}),
            ("LTG_FIX_TAG_02_TXT", new[] {0,6,7},            "-", new[] {"Lighting Fixtures","Lighting Devices"}),
            // Pipework / Plumbing
            ("PLM_EQP_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Pipes","Pipe Fittings","Pipe Accessories","Flex Pipes","Plumbing Fixtures"}),
            ("PLM_EQP_TAG_02_TXT", new[] {0,6,7},            "-", new[] {"Pipes","Pipe Fittings","Pipe Accessories","Flex Pipes","Plumbing Fixtures"}),
            // Fire & Life Safety
            ("FLS_DEV_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Sprinklers","Fire Alarm Devices"}),
            ("FLS_DEV_TAG_02_TXT", new[] {0,6,7},            "-", new[] {"Sprinklers","Fire Alarm Devices"}),
            // Conduits
            ("ELC_CDT_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Conduits","Conduit Fittings"}),
            ("ELC_CDT_TAG_02_TXT", new[] {0,6,7},            "-", new[] {"Conduits","Conduit Fittings"}),
            // Cable Trays
            ("ELC_CTR_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Cable Trays","Cable Tray Fittings"}),
            // Communications
            ("COM_DEV_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Communication Devices","Telephone Devices"}),
            // Security
            ("SEC_DEV_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Security Devices"}),
            // Nurse Call
            ("NCL_DEV_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Nurse Call Devices"}),
            // ICT
            ("ICT_DEV_TAG_01_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Data Devices"}),
            // Material Tags
            ("MAT_TAG_1_TXT", new[] {0,1,2,3,4,5,6,7}, "-", new[] {"Walls","Floors","Ceilings","Roofs","Doors","Windows"}),
            ("MAT_TAG_2_TXT", new[] {0,6,7},            "-", new[] {"Walls","Floors","Ceilings","Roofs","Doors","Windows"}),
            ("MAT_TAG_3_TXT", new[] {1,2,3},            "-", new[] {"Walls","Floors","Ceilings","Roofs","Doors","Windows"}),
            ("MAT_TAG_4_TXT", new[] {4,5},              "-", new[] {"Walls","Floors","Ceilings","Roofs","Doors","Windows"}),
            ("MAT_TAG_5_TXT", new[] {0,1,2,3},          "-", new[] {"Walls","Floors","Ceilings","Roofs","Doors","Windows"}),
            ("MAT_TAG_6_TXT", new[] {4,5,6,7},          "-", new[] {"Walls","Floors","Ceilings","Roofs","Doors","Windows"}),
        };

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            UIDocument uidoc = cmd.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var targetIds = uidoc.Selection.GetElementIds();
            if (targetIds.Count == 0)
            {
                var known = new HashSet<string>(TagConfig.DiscMap.Keys);
                targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
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
            var existingTags = TagConfig.BuildExistingTagIndex(doc);
            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);

            int built = 0;
            int collisions = 0;
            int containers = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Build Tags + All Containers"))
            {
                tx.Start();
                foreach (ElementId id in targetIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;

                    string catName = ParameterHelpers.GetCategoryName(elem);

                    // Read all 8 tokens
                    string[] tokenValues = new string[AllTokenParams.Length];
                    for (int i = 0; i < AllTokenParams.Length; i++)
                        tokenValues[i] = ParameterHelpers.GetString(elem, AllTokenParams[i]);

                    string disc = tokenValues[0]; // DISC
                    if (string.IsNullOrEmpty(disc)) { skipped++; continue; }

                    string seq = tokenValues[7]; // SEQ

                    // Build the full 8-segment tag
                    string tag = string.Join(TagConfig.Separator, tokenValues);

                    // Collision detection: if tag exists, auto-increment SEQ
                    if (existingTags.Contains(tag))
                    {
                        string sys = tokenValues[4];
                        string lvl = tokenValues[3];
                        string seqKey = $"{disc}_{sys}_{lvl}";
                        if (!seqCounters.ContainsKey(seqKey)) seqCounters[seqKey] = 0;

                        int safety = 10000;
                        while (existingTags.Contains(tag) && safety-- > 0)
                        {
                            seqCounters[seqKey]++;
                            seq = seqCounters[seqKey].ToString().PadLeft(TagConfig.NumPad, '0');
                            tokenValues[7] = seq;
                            tag = string.Join(TagConfig.Separator, tokenValues);
                        }

                        // Write the new SEQ back to the element
                        ParameterHelpers.SetString(elem, "ASS_SEQ_NUM_TXT", seq, overwrite: true);
                        collisions++;
                    }

                    // Register tag in the index
                    existingTags.Add(tag);

                    // Write ASS_TAG_1_TXT (the master assembled tag)
                    ParameterHelpers.SetString(elem, "ASS_TAG_1_TXT", tag, overwrite: true);
                    built++;

                    // Write ALL 37 containers (category-filtered)
                    foreach (var (param, tokenIdxs, sep, categories) in Containers)
                    {
                        // Skip ASS_TAG_1_TXT (already written above)
                        if (param == "ASS_TAG_1_TXT") continue;

                        // Category filter: skip if container doesn't apply to this element
                        if (categories != null && !Array.Exists(categories, c => c == catName))
                            continue;

                        // Assemble container value from specified token indices
                        var parts = new List<string>();
                        bool anyValue = false;
                        foreach (int idx in tokenIdxs)
                        {
                            string val = tokenValues[idx];
                            parts.Add(val);
                            if (!string.IsNullOrEmpty(val)) anyValue = true;
                        }

                        if (anyValue)
                        {
                            string assembled = string.Join(sep, parts);
                            if (ParameterHelpers.SetString(elem, param, assembled, overwrite: true))
                                containers++;
                        }
                    }
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Built tags for {built} elements.");
            if (collisions > 0)
                report.AppendLine($"Resolved {collisions} collisions (auto-incremented SEQ).");
            report.AppendLine($"Wrote {containers} container parameters (37 containers × {built} elements).");
            if (skipped > 0)
                report.AppendLine($"Skipped {skipped} elements (no DISC token).");

            TaskDialog.Show("Build Tags", report.ToString());
            StingLog.Info($"BuildTags: built={built}, collisions={collisions}, containers={containers}");
            return Result.Succeeded;
        }
    }

    /// <summary>ISO 19650 completeness dashboard — reports per-discipline compliance.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CompletenessDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var stats = new Dictionary<string, (int total, int valid, int incomplete, int missing)>();

            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "XX";
                if (!stats.ContainsKey(disc)) stats[disc] = (0, 0, 0, 0);

                var s = stats[disc];
                string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
                if (string.IsNullOrEmpty(tag))
                    stats[disc] = (s.total + 1, s.valid, s.incomplete, s.missing + 1);
                else if (TagConfig.TagIsComplete(tag))
                    stats[disc] = (s.total + 1, s.valid + 1, s.incomplete, s.missing);
                else
                    stats[disc] = (s.total + 1, s.valid, s.incomplete + 1, s.missing);
            }

            var report = new StringBuilder();
            report.AppendLine("═══ ISO 19650 Completeness Dashboard ═══");
            report.AppendLine();
            report.AppendLine($"{"DISC",-6} {"Total",7} {"Valid",7} {"Incp",7} {"Miss",7} {"Comp%",7}");
            report.AppendLine(new string('─', 42));

            int grandTotal = 0, grandValid = 0, grandInc = 0, grandMiss = 0;
            foreach (var kvp in stats.OrderBy(x => x.Key))
            {
                var s = kvp.Value;
                double pct = s.total > 0 ? s.valid * 100.0 / s.total : 0;
                report.AppendLine($"{kvp.Key,-6} {s.total,7} {s.valid,7} {s.incomplete,7} {s.missing,7} {pct,6:F1}%");
                grandTotal += s.total;
                grandValid += s.valid;
                grandInc += s.incomplete;
                grandMiss += s.missing;
            }

            report.AppendLine(new string('─', 42));
            double grandPct = grandTotal > 0 ? grandValid * 100.0 / grandTotal : 0;
            report.AppendLine($"{"TOTAL",-6} {grandTotal,7} {grandValid,7} {grandInc,7} {grandMiss,7} {grandPct,6:F1}%");

            TaskDialog td = new TaskDialog("ISO Completeness Dashboard");
            td.MainInstruction = $"Overall compliance: {grandPct:F1}% ({grandValid}/{grandTotal})";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
