using System;
using System.Collections.Generic;
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
    /// B1.5 authoring aid (plan 2026-07-03-tag-tier-automation). Route B needs a
    /// human to hand-place one Label element per tier row in each tag family,
    /// because the Revit API cannot create annotation Labels. This command makes
    /// that mechanical: for the OPEN tag family it reads the family's ordered tier
    /// rows from the current STING_TAG_CONFIG_v5_0_*.csv files, verifies the
    /// content params are bound, drops a numbered guide note at each stacked Y
    /// position, and prints the per-slot checklist (param + style + colour + size).
    ///
    /// The author then: click Label tool → snap to guide N → pick the named param
    /// → set the listed style. Guides are plain TextNotes tagged with a marker so
    /// <see cref="TierTemplateClearGuidesCommand"/> can wipe them afterwards.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TierTemplatePrepCommand : IExternalCommand
    {
        internal const string GuideMarker = "STINGGUIDE:";
        private const double PitchMm = 6.0;   // vertical spacing between guide rows
        private const double GuideXmm = 40.0; // guides sit to the right of the label column

        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            UIDocument uidoc = c.Application.ActiveUIDocument;
            Document fdoc = uidoc?.Document;
            if (fdoc == null || !fdoc.IsFamilyDocument)
            {
                TaskDialog.Show("Tier Prep", "Open a STING tag family (.rfa) first.");
                return Result.Cancelled;
            }

            string famName = NormaliseName(fdoc.Title);
            List<TierAuthorRow> rows;
            try { rows = TierAuthorData.LoadFamilyRows(famName); }
            catch (Exception ex)
            {
                StingLog.Error("TierTemplatePrep: CSV load failed", ex);
                TaskDialog.Show("Tier Prep", "Could not read tag config CSVs:\n" + ex.Message);
                return Result.Failed;
            }

            if (rows.Count == 0)
            {
                TaskDialog.Show("Tier Prep",
                    $"No tier rows found in the CSVs for:\n{famName}\n\n" +
                    "Check the family title matches a 'Tag Family #N:' block name.");
                return Result.Cancelled;
            }

            // Which content params are already bound on the family?
            var bound = new HashSet<string>(
                fdoc.FamilyManager.Parameters.Cast<FamilyParameter>().Select(p => p.Definition.Name),
                StringComparer.Ordinal);

            var missing = rows.Select(r => r.Parameter)
                              .Where(p => !bound.Contains(p))
                              .Distinct().ToList();

            int guides = 0;
            try
            {
                TextNoteType tnt = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                if (tnt == null)
                {
                    TaskDialog.Show("Tier Prep", "No TextNoteType in this family — cannot drop guides.");
                }
                else
                {
                    double gx = GuideXmm / 304.8;
                    using (var tx = new Transaction(fdoc, "STING Tier Prep — drop guides"))
                    {
                        tx.Start();
                        for (int i = 0; i < rows.Count; i++)
                        {
                            var r = rows[i];
                            double gy = -(i) * PitchMm / 304.8;
                            string tag = $"{GuideMarker} {i + 1:D2} {r.Tier} {r.Parameter} [{r.Style}/{r.Color}/{r.Size}]";
                            TextNote.Create(fdoc, uidoc.ActiveView.Id, new XYZ(gx, gy, 0), tag, tnt.Id);
                            guides++;
                        }
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("TierTemplatePrep: guide placement failed", ex);
            }

            // Build the checklist + write it next to the log for copy/paste.
            var sb = new StringBuilder();
            sb.AppendLine($"{famName} — {rows.Count} label rows to author");
            sb.AppendLine($"Params bound: {rows.Select(r => r.Parameter).Distinct().Count() - missing.Count}"
                        + $" / {rows.Select(r => r.Parameter).Distinct().Count()}   guides dropped: {guides}");
            if (missing.Count > 0)
                sb.AppendLine($"MISSING PARAMS ({missing.Count}): {string.Join(", ", missing.Take(12))}"
                            + (missing.Count > 12 ? " …" : ""));
            sb.AppendLine();
            sb.AppendLine("#   Tier  Param  [Style/Color/Size]  prefix|suffix  brk");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.AppendLine($"{i + 1:D2}  {r.Tier,-4} {r.Parameter}  [{r.Style}/{r.Color}/{r.Size}]"
                            + $"  {Q(r.Prefix)}|{Q(r.Suffix)}  {(r.Brk ? "Y" : "")}");
            }

            string outPath = null;
            try
            {
                string dir = Path.GetDirectoryName(StingToolsApp.AssemblyPath) ?? Path.GetTempPath();
                outPath = Path.Combine(dir, $"TierPrep_{Sanitise(famName)}.txt");
                File.WriteAllText(outPath, sb.ToString());
            }
            catch (Exception ex) { StingLog.Warn("TierTemplatePrep: checklist write failed — " + ex.Message); }

            var dlg = new TaskDialog("Tier Prep — " + famName)
            {
                MainInstruction = $"{rows.Count} rows · {guides} guides dropped"
                                + (missing.Count > 0 ? $" · {missing.Count} params missing" : ""),
                MainContent = (outPath != null ? "Checklist written to:\n" + outPath + "\n\n" : "")
                            + string.Join("\n", sb.ToString().Split('\n').Take(24)),
            };
            dlg.Show();
            return Result.Succeeded;
        }

        private static string Q(string s) => string.IsNullOrEmpty(s) ? "" : s;
        private static string NormaliseName(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;
            if (title.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                title = title.Substring(0, title.Length - 4);
            return title.Trim();
        }
        private static string Sanitise(string s)
            => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }

    /// <summary>Removes all guide notes dropped by <see cref="TierTemplatePrepCommand"/>.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TierTemplateClearGuidesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            Document fdoc = c.Application.ActiveUIDocument?.Document;
            if (fdoc == null || !fdoc.IsFamilyDocument)
            { TaskDialog.Show("Tier Prep", "Open the tag family first."); return Result.Cancelled; }

            var ids = new FilteredElementCollector(fdoc).OfClass(typeof(TextNote)).Cast<TextNote>()
                .Where(t => (t.Text ?? "").TrimStart().StartsWith(TierTemplatePrepCommand.GuideMarker, StringComparison.Ordinal))
                .Select(t => t.Id).ToList();

            if (ids.Count == 0) { TaskDialog.Show("Tier Prep", "No STING guide notes found."); return Result.Succeeded; }

            using (var tx = new Transaction(fdoc, "STING Tier Prep — clear guides"))
            {
                tx.Start();
                fdoc.Delete(ids);
                tx.Commit();
            }
            TaskDialog.Show("Tier Prep", $"Removed {ids.Count} guide note(s).");
            return Result.Succeeded;
        }
    }

    // ---- pure-logic CSV row loader (Revit-free) --------------------------------

    internal sealed class TierAuthorRow
    {
        public string Tier, Parameter, Prefix, Suffix, Style, Color, Size;
        public bool Brk;
    }

    internal static class TierAuthorData
    {
        private static readonly string[] DiscCsvs =
        {
            "STING_TAG_CONFIG_v5_0_GEN.csv",  // GEN first so disc-specific can shadow
            "STING_TAG_CONFIG_v5_0_ARCH.csv",
            "STING_TAG_CONFIG_v5_0_MEP.csv",
            "STING_TAG_CONFIG_v5_0_STR.csv",
        };

        /// <summary>All T1..T10 label rows for one family, in file order, across the disc CSVs.</summary>
        public static List<TierAuthorRow> LoadFamilyRows(string familyName)
        {
            var rows = new List<TierAuthorRow>();
            foreach (var csv in DiscCsvs)
            {
                string path = StingToolsApp.FindDataFile(csv);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                var found = ScanFile(path, familyName);
                if (found.Count > 0) { rows = found; } // disc-specific shadows GEN by family match
            }
            return rows;
        }

        private static List<TierAuthorRow> ScanFile(string path, string familyName)
        {
            var rows = new List<TierAuthorRow>();
            bool inTarget = false;
            foreach (var raw in File.ReadLines(path))
            {
                string t = raw.TrimStart();
                if (t.StartsWith("Tag Family #", StringComparison.Ordinal))
                {
                    int colon = raw.IndexOf(':');
                    string name = colon >= 0 ? raw.Substring(colon + 1).Trim() : "";
                    inTarget = string.Equals(name, familyName, StringComparison.Ordinal);
                    continue;
                }
                if (!inTarget) continue;
                if (t.StartsWith("⚠")) break;         // ⚠ WARNING banner ends the block
                if (t.StartsWith("#")) continue;            // header / comment
                var cols = ParseCsvLine(raw);
                if (cols.Length < 14) continue;
                string tier = cols[1].Trim();
                if (tier.Length < 2 || tier[0] != 'T' || !int.TryParse(tier.Substring(1), out _)) continue;
                if (string.IsNullOrWhiteSpace(cols[2])) continue;
                rows.Add(new TierAuthorRow
                {
                    Tier = tier, Parameter = cols[2].Trim(),
                    Prefix = cols[3], Suffix = cols[4],
                    Brk = cols[6].Trim() == "✓" || cols[6].Trim().Equals("yes", StringComparison.OrdinalIgnoreCase),
                    Style = cols[11].Trim(), Color = cols[12].Trim(), Size = cols[13].Trim(),
                });
            }
            return rows;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool q = false; var cur = new StringBuilder();
            foreach (char ch in line ?? "")
            {
                if (ch == '"') q = !q;
                else if (ch == ',' && !q) { result.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            result.Add(cur.ToString());
            return result.ToArray();
        }
    }
}
