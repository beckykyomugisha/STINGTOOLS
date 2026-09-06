// ══════════════════════════════════════════════════════════════════════════
//  RateProvenanceLabel.cs — turn a rate's internal source token into
//  something a reader can act on.
//
//  MaterialCommodity.RateSource has always been populated ("baseline" /
//  "project" / "unpriced") and written to no sheet at all, so a priced
//  schedule showed 28,000 against cement without saying whether that was the
//  user's supplier quote or the shipped guess. Both print identically, and
//  only one of them is safe to tender against.
//
//  The labels are deliberately NOT the raw tokens. "baseline" reads as
//  authority; the shipped file it comes from says the opposite of that in its
//  own header — "indicative Kampala market prices; a live project MUST
//  re-price before tender". So the label says Indicative.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.MaterialSchedule
{
    public static class RateProvenanceLabel
    {
        public const string Project    = "Project";
        public const string Indicative = "Indicative";
        public const string NotPriced  = "NOT PRICED";
        public const string None       = "—";

        /// <summary>
        /// The cell text for one commodity.
        ///
        /// A memorandum gets the same em dash its Amount cell gets: it carries
        /// no rate by design, and labelling it "NOT PRICED" would read as a
        /// defect to fix — which is exactly the misreading that had four
        /// intermediate measures priced alongside their own constituents.
        /// </summary>
        public static string For(MaterialCommodity c)
        {
            if (c == null) return None;
            if (c.IsMemorandum) return None;
            if (c.IsUnpriced) return NotPriced;

            switch ((c.RateSource ?? "").Trim().ToLowerInvariant())
            {
                case "project":  return Project;
                case "baseline": return Indicative;
                // A rate with no recorded source is not the same as no rate.
                // Saying so beats inventing a provenance for it.
                default:         return "?";
            }
        }

        /// <summary>
        /// One line for the export notes, so the column is a summary somebody
        /// reads once rather than 58 cells they scan.
        ///
        /// Returns NULL when the schedule carries no priced rows at all — a
        /// quantities-only export has no provenance to report, and a line of
        /// zeroes there would read as a finding.
        /// </summary>
        public static string Summary(IEnumerable<MaterialCommodity> commodities)
        {
            var rows = (commodities ?? Enumerable.Empty<MaterialCommodity>())
                       .Where(c => c != null && !c.IsMemorandum).ToList();
            if (rows.Count == 0) return null;

            int project    = rows.Count(c => For(c) == Project);
            int indicative = rows.Count(c => For(c) == Indicative);
            int unpriced   = rows.Count(c => For(c) == NotPriced);
            int unknown    = rows.Count(c => For(c) == "?");

            if (project == 0 && indicative == 0 && unpriced == 0 && unknown == 0) return null;

            string s = $"Rate provenance: {project} row(s) priced from THIS project's rates, "
                     + $"{indicative} from the shipped indicative figures, {unpriced} not priced.";

            if (indicative > 0)
                s += " The indicative rates are Kampala market figures shipped with the plugin and "
                   + "the file that carries them says a live project must re-price before tender — "
                   + "they are a starting point, not a quotation. Use Price Commodities to replace "
                   + "them with your own.";
            if (unpriced > 0)
                s += $" The {unpriced} unpriced row(s) total zero in this schedule, so the grand "
                   + "total is an UNDER-statement, not an estimate of them.";
            if (unknown > 0)
                s += $" {unknown} row(s) carry a rate with no recorded source, which should not "
                   + "happen — report it rather than trusting the number.";
            return s;
        }

        /// <summary>
        /// Where this run's project rates live, stated ONCE.
        ///
        /// The per-row R3 messages used to repeat "_BIM_COORD/commodity_rates.csv"
        /// on every unpriced row — 25 times in one export, and wrong: _BIM_COORD
        /// is the alias, the live folder is _data/coord, and a reader following
        /// it literally opened a stale legacy folder and concluded the file did
        /// not exist. It genuinely did not, because until the rate editor
        /// NOTHING in the codebase ever wrote it.
        ///
        /// So: the rows name the action, this names the file, and the path is
        /// the RESOLVED absolute one rather than a relative guess.
        ///
        /// Returns NULL when nothing is unpriced — a path nobody needs is noise
        /// on a clean export.
        /// </summary>
        public static string RatesFileNote(string resolvedPath, int unpricedCount)
        {
            if (unpricedCount <= 0) return null;

            string s = "Rates for the unpriced rows: use BOQ tab -> Price Commodities, which lists "
                     + "every commodity with the unpriced ones first and needs only a number typed "
                     + "— the keys come from the schedule, and most of them contain characters that "
                     + "cannot be retyped reliably.";

            if (!string.IsNullOrWhiteSpace(resolvedPath))
                s += " It writes " + resolvedPath.Trim()
                   + ", which is a plain CSV: hand-editable, and copyable to the next project.";
            else
                s += " It writes commodity_rates.csv in the project's coordination folder — but this "
                   + "run could not resolve that path, which usually means the project has not been "
                   + "saved.";
            return s;
        }
    }
}
