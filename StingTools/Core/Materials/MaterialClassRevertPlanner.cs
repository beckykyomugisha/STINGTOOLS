// ══════════════════════════════════════════════════════════════════════════
//  MaterialClassRevertPlanner.cs — undo a material_class_plan that was applied
//  before the planner behind it was fixed.
//
//  WHY THIS EXISTS AT ALL. Materials_SetClass has one rule that makes it safe
//  to run on a delivered model: it never overwrites a Class somebody already
//  chose. On 2026-09-08 the somebody was the tool. It wrote 120 classes, 41 of
//  them wrong, and from that moment its own safety rule protected the mistake —
//  re-running the FIXED planner changes nothing, because every one of those
//  materials is now "already classified".
//
//  So the way back is not another planner run. It is this: read the plan CSV
//  the run wrote — which records, per material, what it found and what it set —
//  and put back what it found.
//
//  THE RULE THAT MAKES IT SAFE, and the only interesting line in the file:
//
//      revert ONLY where the material's class TODAY still equals what that plan
//      proposed.
//
//  If it differs, somebody has been in there since and their choice wins; the
//  row is skipped and reported, never silently overwritten. That is the same
//  rule Materials_SetClass has, applied to the tool rather than to the human —
//  and it is what makes this idempotent: after one revert the class no longer
//  equals the proposal, so a second run finds nothing to do.
//
//  Revit-free on purpose. The refusals are the half worth proving, and they are
//  provable without a Revit host.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core.Materials
{
    /// <summary>One row of a material_class_plan CSV, as written by Materials_SetClass.</summary>
    public sealed class MaterialClassPlanRow
    {
        public string MaterialName = "";
        /// <summary>What the plan recorded as already present — "" or "Unassigned" for
        /// every row it went on to write.</summary>
        public string ExistingClass = "";
        /// <summary>What the plan proposed. Blank on the rows it refused.</summary>
        public string ProposedClass = "";
    }

    public sealed class MaterialClassRevertProposal
    {
        public string MaterialName = "";
        /// <summary>What the plan set.</summary>
        public string PlannedClass = "";
        /// <summary>What the material carries now. Null when it is no longer in the model.</summary>
        public string CurrentClass;
        /// <summary>What to put back — the class the plan found before it wrote.
        /// Empty string means "no class", which is how Revit shows Unassigned.</summary>
        public string RestoreTo = "";
        public string Reason = "";

        /// <summary>True when the material already carries what the plan found — a second
        /// revert run, or a write that was never saved. Distinct from "somebody changed it
        /// since", which the message used to conflate it with.</summary>
        public bool AlreadyAsThePlanFoundIt { get; internal set; }

        public bool WillRevert { get; internal set; }
    }

    public static class MaterialClassRevertPlanner
    {
        /// <param name="currentClass">
        /// What the document holds for this material now, or null if the material is gone.
        /// </param>
        public static MaterialClassRevertProposal Plan(MaterialClassPlanRow row, string currentClass)
        {
            var p = new MaterialClassRevertProposal
            {
                MaterialName = row?.MaterialName ?? "",
                PlannedClass = (row?.ProposedClass ?? "").Trim(),
                CurrentClass = currentClass,
                RestoreTo = Normalise(row?.ExistingClass),
            };

            if (string.IsNullOrWhiteSpace(p.MaterialName))
            { p.Reason = "unnamed row in the plan"; return p; }

            if (p.PlannedClass.Length == 0)
            { p.Reason = "the plan proposed nothing for this material — nothing to undo"; return p; }

            if (currentClass == null)
            { p.Reason = "no longer in this model"; return p; }

            // ALREADY BACK is not CHANGED SINCE, and conflating them alarms the reader.
            //
            // Run on the Herring model 2026-09-09 07:48: 1,815 rows, 0 reverted, and 120
            // of them reported "changed since the plan ran — now 'Unassigned', the plan set
            // 'Ceramic'. That choice wins". Every one of those 120 was in fact ALREADY
            // reverted — the write had not been saved — so the message described a
            // colleague overwriting the reader's data when nothing of the sort had
            // happened. The verdict was right and the sentence was wrong, which is the
            // harder half to notice.
            if (string.Equals(Normalise(currentClass), p.RestoreTo, StringComparison.OrdinalIgnoreCase))
            {
                p.AlreadyAsThePlanFoundIt = true;
                p.Reason = p.RestoreTo.Length == 0
                    ? $"already back to no class, which is how the plan found it — nothing to undo "
                    + $"(the plan set '{p.PlannedClass}')"
                    : $"already back to '{p.RestoreTo}', which is how the plan found it — nothing to "
                    + $"undo (the plan set '{p.PlannedClass}')";
                return p;
            }

            if (!string.Equals(Normalise(currentClass), p.PlannedClass, StringComparison.OrdinalIgnoreCase))
            {
                p.Reason = $"changed since the plan ran — now '{Show(currentClass)}', the plan set "
                         + $"'{p.PlannedClass}'. That choice wins; not reverted.";
                return p;
            }

            p.WillRevert = true;
            p.Reason = p.RestoreTo.Length == 0
                ? $"set by the plan to '{p.PlannedClass}' and untouched since — cleared"
                : $"set by the plan to '{p.PlannedClass}' and untouched since — back to '{p.RestoreTo}'";
            return p;
        }

        public static List<MaterialClassRevertProposal> PlanAll(
            IEnumerable<MaterialClassPlanRow> planRows,
            IReadOnlyDictionary<string, string> currentClassByName)
        {
            var rows = planRows?.ToList() ?? new List<MaterialClassPlanRow>();
            var res = new List<MaterialClassRevertProposal>();
            foreach (var r in rows)
            {
                string cur = null;
                if (currentClassByName != null && r != null && r.MaterialName != null)
                    currentClassByName.TryGetValue(r.MaterialName, out cur);
                res.Add(Plan(r, cur));
            }
            return res;
        }

        public static string Summary(IReadOnlyCollection<MaterialClassRevertProposal> ps)
        {
            if (ps == null || ps.Count == 0) return "That plan file has no rows.";
            int revert = ps.Count(x => x.WillRevert);
            int nothingSet = ps.Count(x => x.PlannedClass.Length == 0);
            int gone = ps.Count(x => x.CurrentClass == null && x.PlannedClass.Length > 0);
            int already = ps.Count(x => x.AlreadyAsThePlanFoundIt);
            int moved = ps.Count - revert - nothingSet - gone - already;
            var sb = new StringBuilder();
            sb.AppendLine($"{ps.Count} row(s) in that plan: {revert} will be put back as they were.");
            sb.AppendLine($"{nothingSet} were never written and {gone} are no longer in the model.");
            if (already > 0)
                sb.AppendLine($"{already} already carry what the plan found, so there is nothing to "
                            + "undo — either this has already been run, or that write was never saved.");
            if (moved > 0)
                sb.AppendLine($"{moved} have been changed since the plan ran and are left alone, "
                            + "because somebody chose them after the tool did.");
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Reading the plan CSV
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Parses the CSV Materials_SetClass writes: Material,ExistingClass,ProposedClass,Reason.
        /// Material names carry commas, quotes and backslashes — "_ACCURENDER\Solid
        /// Colors\Black,Matte" is a real one — so this is RFC 4180 and not a Split(',').
        /// Columns are located by HEADER NAME, so a column added to the plan later does
        /// not silently shift what this reads.
        /// </summary>
        public static List<MaterialClassPlanRow> ReadPlan(IEnumerable<string> lines, out string error)
        {
            error = null;
            var all = (lines ?? Enumerable.Empty<string>()).ToList();
            if (all.Count == 0) { error = "that file is empty"; return new List<MaterialClassPlanRow>(); }

            var header = SplitCsvLine(all[0].TrimStart('﻿'));
            int iName = IndexOf(header, "Material");
            int iExisting = IndexOf(header, "ExistingClass");
            int iProposed = IndexOf(header, "ProposedClass");
            if (iName < 0 || iProposed < 0)
            {
                error = "that file is not a material_class_plan CSV — it has no "
                      + "'Material' and 'ProposedClass' columns.";
                return new List<MaterialClassPlanRow>();
            }

            var rows = new List<MaterialClassPlanRow>();
            foreach (string line in all.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = SplitCsvLine(line);
                if (f.Count <= iProposed) continue;
                rows.Add(new MaterialClassPlanRow
                {
                    MaterialName = f[iName],
                    ExistingClass = iExisting >= 0 && iExisting < f.Count ? f[iExisting] : "",
                    ProposedClass = f[iProposed],
                });
            }
            if (rows.Count == 0) error = "that file has a header but no rows.";
            return rows;
        }

        private static int IndexOf(List<string> header, string name)
            => header.FindIndex(h => string.Equals(h?.Trim(), name, StringComparison.OrdinalIgnoreCase));

        internal static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (line ?? "").Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') { cur.Append(c); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; continue; }
                    quoted = false;
                }
                else if (c == '"' && cur.Length == 0) quoted = true;
                else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            fields.Add(cur.ToString());
            return fields;
        }

        /// <summary>Revit's "Unassigned" and an empty string are the same absence.</summary>
        private static string Normalise(string cls)
        {
            string s = (cls ?? "").Trim();
            return string.Equals(s, "Unassigned", StringComparison.OrdinalIgnoreCase) ? "" : s;
        }

        private static string Show(string cls)
            => string.IsNullOrWhiteSpace(cls) ? "(blank)" : cls.Trim();
    }
}
