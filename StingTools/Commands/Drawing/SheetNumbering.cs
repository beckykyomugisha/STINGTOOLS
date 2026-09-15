// StingTools — Drawing Template Manager · the one place sheet numbers get written
//
// Auto-Number and Reorder both renumber, and both need the same two things: the
// configured pattern, and a rename that Revit will accept. Two copies of that
// would be two copies of the parking logic, and the day they disagree is the day
// a sheet is left sitting on "__STING_TEMP_1234".

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Docs;

namespace StingTools.Commands.Drawing
{
    internal static class SheetNumbering
    {
        internal sealed class Change
        {
            public ViewSheet Sheet;
            public string Old;
            public string New;
        }

        internal sealed class Outcome
        {
            public int Done;
            public int Failed;
            public List<string> Failures = new List<string>();

            /// <summary>Where the old -> new pairs were recorded, or null when they
            /// could not be. Null is reported, not hidden: an operator told a trail
            /// exists when it does not is worse off than one told nothing.</summary>
            public string HistoryPath;

            /// <summary>How many sheets had their ISO 19650 identifier rebuilt. A
            /// renumber that does not rebuild it leaves the title block printing the
            /// OLD number while the browser shows the new one.</summary>
            public int Retagged;
            public List<string> RetagFailures = new List<string>();
        }

        /// <summary>The file Sheet_NumberFromIso, Tidy and Restore already append to.
        /// One file, because the question it answers -- "this PDF says A-001, what is
        /// that sheet called now?" -- does not care which command did the renaming,
        /// and three separate logs would each hold a third of the answer.</summary>
        internal const string HistoryFile = "sheet_number_history.json";

        /// <summary>The number pattern, from TITLE_BLOCK.csv's DefaultValue column.
        ///
        /// Read through the same loader Populate uses, so a project-local CSV wins
        /// exactly as it does everywhere else. A second way of finding the same file
        /// is how two commands come to disagree about it.</summary>
        internal static string ReadPattern(Document doc)
        {
            try
            {
                string path = TitleBlockPopulateCommand.ResolveCsvPath(doc, "TITLE_BLOCK.csv");
                var csv = TitleBlockCsv.Load(path);
                string p = csv.ValueFor(ParamRegistry.TB_SHEET_NUMBER_PATTERN, "");
                if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            }
            catch (Exception ex) { StingLog.Warn($"SheetNumbering pattern read: {ex.Message}"); }
            return "{disc}-{seq:D3}";
        }

        /// <summary>Apply a renumber plan in two passes, inside one transaction.
        ///
        /// Revit refuses a duplicate sheet number even for an instant, so every sheet
        /// parks on a unique temporary number before any final number is set. A sheet
        /// whose final number is refused goes back to the one it started with —
        /// "__STING_TEMP_1234" is a worse number than the old one, and leaving it
        /// there turns a failed rename into a corrupted set.</summary>
        internal static Outcome Apply(Document doc, List<Change> plan, string transactionName)
        {
            var outcome = new Outcome();
            if (plan == null || plan.Count == 0) return outcome;

            using (var tx = new Transaction(doc, transactionName))
            {
                tx.Start();

                var parked = new List<Change>();
                foreach (var c in plan)
                {
                    try
                    {
                        c.Sheet.SheetNumber = $"__STING_TEMP_{c.Sheet.Id.Value}";
                        parked.Add(c);
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        outcome.Failures.Add($"  {c.Old}: could not park — {ex.Message}");
                        StingLog.Warn($"SheetNumbering park '{c.Old}': {ex.Message}");
                    }
                }

                foreach (var c in parked)
                {
                    try { c.Sheet.SheetNumber = c.New; outcome.Done++; }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        outcome.Failures.Add($"  {c.Old} -> {c.New}: {ex.Message}");
                        StingLog.Warn($"SheetNumbering set '{c.New}': {ex.Message}");
                        try { c.Sheet.SheetNumber = c.Old; }
                        catch (Exception ex2)
                        {
                            StingLog.Warn($"SheetNumbering restore '{c.Old}': {ex2.Message}");
                            outcome.Failures.Add($"  {c.Old}: ALSO could not be put back — "
                                + "it is sitting on a temporary number and needs renaming by hand");
                        }
                    }
                }

                tx.Commit();
            }

            Retag(doc, plan, outcome, transactionName);
            outcome.HistoryPath = RecordHistory(doc, plan, outcome, transactionName);
            return outcome;
        }
        /// <summary>Rebuild the ISO 19650 identifier on every sheet that was
        /// renumbered.
        ///
        /// SHT_TAG_1_TXT is DERIVED from the sheet number, and so are the discipline,
        /// form and level tokens it is assembled out of. Changing the number without
        /// rebuilding them leaves derived data contradicting its own source: the
        /// project browser shows A-001 while the title block still prints
        /// SAH-PLNS-ZZ-01-LG-Z-0003, and nothing on the drawing admits the two
        /// disagree. That is not a follow-up step for the operator to remember — it
        /// is part of renumbering, so it happens here.
        ///
        /// A separate transaction, because the rename must be committed before the
        /// tokens are derived from it. Both are undoable; a second Ctrl+Z reverses
        /// the rename after the first reverses the re-tag.
        ///
        /// Only the renumbered sheets, and only with reDerive — a sheet nobody
        /// touched keeps every token exactly as it was.</summary>
        private static void Retag(Document doc, List<Change> plan, Outcome outcome, string source)
        {
            if (doc == null || plan == null || plan.Count == 0) return;

            try
            {
                string originator = NativeParamMapper.SheetTagger.DetectOriginator(doc);
                string projectCode = NativeParamMapper.SheetTagger.DetectProjectCode(doc);
                string rev = PhaseAutoDetect.DetectProjectRevision(doc) ?? "P01";

                using (var tx = new Transaction(doc, source + " — rebuild identifiers"))
                {
                    tx.Start();
                    foreach (var c in plan)
                    {
                        if (c.Sheet == null) continue;
                        // A sheet whose rename failed still holds its OLD number, so
                        // its tokens are still correct. Re-deriving would be a no-op
                        // at best and is skipped rather than counted.
                        if (!string.Equals(c.Sheet.SheetNumber, c.New, StringComparison.Ordinal))
                            continue;

                        try
                        {
                            NativeParamMapper.SheetTagger.TagSheet(
                                doc, c.Sheet, originator, projectCode, rev, reDerive: true);
                            outcome.Retagged++;
                        }
                        catch (Exception ex)
                        {
                            outcome.RetagFailures.Add($"  {c.New}: {ex.Message}");
                            StingLog.Warn($"SheetNumbering retag '{c.New}': {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                // Named, not swallowed. The renumber HAS happened; an operator who
                // believes the identifiers followed and finds they did not would be
                // chasing the wrong thing entirely.
                outcome.RetagFailures.Add("  the identifier rebuild failed for the whole run — "
                    + ex.Message + " — run Tag Sheets to finish it");
                StingLog.Error("SheetNumbering: identifier rebuild failed", ex);
            }
        }

        /// <summary>Append what changed to sheet_number_history.json.
        ///
        /// This is the answer to the one question a renumber creates: somebody is
        /// holding a PDF that says A-001 and needs to know what that sheet is called
        /// now. Revit keeps no record -- the old number is simply gone -- so if this
        /// is not written, the link between an issued drawing and the model is lost
        /// with nothing anywhere admitting it.
        ///
        /// Only sheets that actually took their new number are logged. Recording a
        /// rename that failed would send the reader to a sheet number that does not
        /// exist.</summary>
        private static string RecordHistory(Document doc, List<Change> plan,
                                            Outcome outcome, string source)
        {
            try
            {
                if (doc == null || doc.IsFamilyDocument || string.IsNullOrEmpty(doc.PathName))
                    return null;

                var applied = new List<Change>();
                foreach (var c in plan)
                {
                    bool failed = outcome.Failures.Exists(f =>
                        f.TrimStart().StartsWith(c.Old + " ", StringComparison.Ordinal)
                        || f.TrimStart().StartsWith(c.Old + ":", StringComparison.Ordinal));
                    if (!failed) applied.Add(c);
                }
                if (applied.Count == 0) return null;

                string path = StingPaths.MetaFile(doc, "_BIM_COORD", HistoryFile);
                if (string.IsNullOrEmpty(path)) return null;

                Newtonsoft.Json.Linq.JArray log;
                try
                {
                    log = System.IO.File.Exists(path)
                        ? Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(path))
                        : new Newtonsoft.Json.Linq.JArray();
                }
                catch (Exception ex)
                {
                    // A corrupt log must not cost us THIS run's record, and must not
                    // be silently replaced either.
                    StingLog.Warn($"SheetNumbering: {HistoryFile} unreadable, starting a new "
                        + $"one beside it: {ex.Message}");
                    log = new Newtonsoft.Json.Linq.JArray();
                }

                log.Add(Newtonsoft.Json.Linq.JObject.FromObject(new
                {
                    when = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    source,
                    changes = applied.ConvertAll(c => new { from = c.Old, to = c.New }),
                }));

                System.IO.File.WriteAllText(path,
                    Newtonsoft.Json.JsonConvert.SerializeObject(log, Newtonsoft.Json.Formatting.Indented));
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetNumbering: could not write {HistoryFile}: {ex.Message}");
                return null;
            }
        }
    }
}
