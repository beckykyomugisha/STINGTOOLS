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
            => TokenWriter.WriteToken(cmd, ParamRegistry.DISC, "Discipline (DISC)",
                new[] { "M", "E", "P", "A" });
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
                    string existing = ParameterHelpers.GetString(elem, ParamRegistry.SEQ);
                    if (!string.IsNullOrEmpty(existing)) continue;

                    string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                    string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                    string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                    if (string.IsNullOrEmpty(disc))
                    {
                        disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "XX";
                        ParameterHelpers.SetIfEmpty(elem, ParamRegistry.DISC, disc);
                    }
                    if (string.IsNullOrEmpty(sys))
                    {
                        sys = TagConfig.GetSysCode(cat);
                        ParameterHelpers.SetIfEmpty(elem, ParamRegistry.SYS, sys);
                    }
                    if (string.IsNullOrEmpty(lvl))
                    {
                        lvl = ParameterHelpers.GetLevelCode(doc, elem);
                        ParameterHelpers.SetIfEmpty(elem, ParamRegistry.LVL, lvl);
                    }

                    string key = $"{disc}_{sys}_{lvl}";
                    if (!maxSeq.ContainsKey(key)) maxSeq[key] = 0;
                    maxSeq[key]++;
                    string seq = maxSeq[key].ToString().PadLeft(ParamRegistry.NumPad, '0');
                    ParameterHelpers.SetString(elem, ParamRegistry.SEQ, seq, overwrite: true);
                    assigned++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Assign Numbers", $"Assigned sequence numbers to {assigned} elements.");
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
            int containerWrites = 0;
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
                    string[] tokenValues = ParamRegistry.ReadTokenValues(elem);

                    string disc = tokenValues[0]; // DISC
                    if (string.IsNullOrEmpty(disc)) { skipped++; continue; }

                    string seq = tokenValues[7]; // SEQ

                    // Build the full 8-segment tag
                    string tag = string.Join(ParamRegistry.Separator, tokenValues);

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
                            seq = seqCounters[seqKey].ToString().PadLeft(ParamRegistry.NumPad, '0');
                            tokenValues[7] = seq;
                            tag = string.Join(ParamRegistry.Separator, tokenValues);
                        }

                        // Write the new SEQ back to the element
                        ParameterHelpers.SetString(elem, ParamRegistry.SEQ, seq, overwrite: true);
                        collisions++;
                    }

                    // Register tag in the index
                    existingTags.Add(tag);

                    // Write TAG1 (the master assembled tag)
                    ParameterHelpers.SetString(elem, ParamRegistry.TAG1, tag, overwrite: true);
                    built++;

                    // Write ALL containers (category-filtered) via ParamRegistry
                    containerWrites += ParamRegistry.WriteContainers(
                        elem, tokenValues, catName, overwrite: true,
                        skipParam: ParamRegistry.TAG1);
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Built tags for {built} elements.");
            if (collisions > 0)
                report.AppendLine($"Resolved {collisions} collisions (auto-incremented SEQ).");
            report.AppendLine($"Wrote {containerWrites} container parameters across {built} elements.");
            if (skipped > 0)
                report.AppendLine($"Skipped {skipped} elements (no DISC token).");

            TaskDialog.Show("Build Tags", report.ToString());
            StingLog.Info($"BuildTags: built={built}, collisions={collisions}, containers={containerWrites}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// ISO 19650 completeness dashboard — reports per-discipline compliance.
    /// Shows both standard compliance (tag has 8 non-empty segments) and strict
    /// compliance (no XX/ZZ placeholder segments = fully resolved tags).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CompletenessDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            Document doc = cmd.Application.ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var stats = new Dictionary<string, (int total, int valid, int resolved, int incomplete, int missing)>();

            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "XX";
                if (!stats.ContainsKey(disc)) stats[disc] = (0, 0, 0, 0, 0);

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

            TaskDialog td = new TaskDialog("ISO Completeness Dashboard");
            td.MainInstruction = $"Compliance: {grandPct:F1}% | Strict: {grandStrictPct:F1}% ({grandResolved}/{grandTotal})";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
