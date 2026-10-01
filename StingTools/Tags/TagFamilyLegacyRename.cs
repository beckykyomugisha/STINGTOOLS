// TAGFAM-7: a project that loaded a tag family under a name STING no longer ships
// (TagFamilyNameAliases.LegacyFamilyNames) has that family renamed in place before
// the library is loaded, so its placed tags stay on it and the library load updates
// it instead of adding a second family beside it.
//
// The decision is TagFamilyNameAliases.Decide (Revit-free, unit-tested); this file
// only carries it out. It never deletes a family: when the project already holds both
// the legacy and the canonical family, it reports that and changes nothing.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    internal static class TagFamilyLegacyRename
    {
        /// <summary>Report lines, one per legacy family found.</summary>
        public sealed class Outcome
        {
            public List<string> Renamed { get; } = new List<string>();
            public List<string> BothPresent { get; } = new List<string>();
            public List<string> Failed { get; } = new List<string>();
            public bool Any => Renamed.Count + BothPresent.Count + Failed.Count > 0;

            public IEnumerable<string> Lines() =>
                Renamed.Select(s => "  [RENAMED] " + s)
                .Concat(BothPresent.Select(s => "  [BOTH IN PROJECT, unchanged] " + s))
                .Concat(Failed.Select(s => "  [RENAME FAILED] " + s));
        }

        /// <summary>
        /// For each library family name (the .rfa file name, which is the name the
        /// family loads under), rename a legacy family in the project to it. Uses the
        /// caller's transaction when the document is already modifiable, else opens one.
        /// </summary>
        public static Outcome Apply(Document doc, IEnumerable<string> libraryFamilyNames)
        {
            var outcome = new Outcome();
            if (doc == null || libraryFamilyNames == null) return outcome;

            var wanted = libraryFamilyNames
                .Where(n => TagFamilyNameAliases.LegacyNamesFor(n).Any())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (wanted.Count == 0) return outcome;

            List<Family> families;
            try
            {
                families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagFamilyLegacyRename: could not list project families: {ex.Message}");
                outcome.Failed.Add($"could not list project families: {ex.Message}");
                return outcome;
            }

            var plan = new List<(Family fam, string to)>();
            foreach (string canonical in wanted)
            {
                var action = TagFamilyNameAliases.Decide(canonical, families.Select(f => f.Name), out string legacy);
                if (action == TagFamilyNameAliases.LegacyAction.BothPresent)
                {
                    string msg = $"'{legacy}' and '{canonical}' — placed tags on the old one are not moved; " +
                                 "retype them and purge the old family by hand";
                    outcome.BothPresent.Add(msg);
                    StingLog.Warn("TagFamilyLegacyRename: both present: " + msg);
                }
                else if (action == TagFamilyNameAliases.LegacyAction.Rename)
                {
                    var fam = families.First(f => string.Equals(f.Name, legacy, StringComparison.OrdinalIgnoreCase));
                    plan.Add((fam, canonical));
                }
            }
            if (plan.Count == 0) return outcome;

            void RenameAll()
            {
                foreach (var (fam, to) in plan)
                {
                    string from = fam.Name;
                    try
                    {
                        fam.Name = to;
                        outcome.Renamed.Add($"'{from}' → '{to}' (placed tags kept)");
                        StingLog.Info($"TagFamilyLegacyRename: renamed '{from}' to '{to}'");
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed.Add($"'{from}' → '{to}': {ex.Message}");
                        StingLog.Error($"TagFamilyLegacyRename: could not rename '{from}' to '{to}'", ex);
                    }
                }
            }

            if (doc.IsModifiable) { RenameAll(); return outcome; }

            try
            {
                using (var tx = new Transaction(doc, "STING Rename Legacy Tag Families"))
                {
                    tx.Start();
                    RenameAll();
                    if (outcome.Renamed.Count > 0) tx.Commit(); else tx.RollBack();
                }
            }
            catch (Exception ex)
            {
                // The transaction rolled back, so nothing it reported as renamed was kept.
                foreach (var r in outcome.Renamed) outcome.Failed.Add(r + " — rolled back: " + ex.Message);
                outcome.Renamed.Clear();
                StingLog.Error("TagFamilyLegacyRename: rename transaction failed", ex);
            }
            return outcome;
        }
    }
}
