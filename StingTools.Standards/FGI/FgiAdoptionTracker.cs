// HC-25: FGI 2026 facility-code adoption tracking.
//
// FGI 2026 transitions from "Guidelines" (advisory) to enforceable code in jurisdictions
// that adopt it. As clauses become mandatory in a given state / authority, the tracker
// flips the severity of any matching validator finding from Warning → Error automatically.
//
// The lookup is intentionally additive: a clause that is not yet in the adoption table
// (or whose adoption date is in the future relative to the project's design freeze date)
// remains a Warning per legacy behaviour. This lets validators keep firing without an
// explicit jurisdiction config, while still escalating where code adoption is real.
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Standards.FGI
{
    public sealed record FgiAdoptionEntry(
        string Clause,
        string Jurisdiction,
        DateTime EffectiveDate,
        string Source);

    public enum FgiSeverity { Warning, Error }

    public static class FgiAdoptionTracker
    {
        // Seeded adoption table — sourced from FGI's tracker on health.fgi.org plus
        // published state regulations (Texas Administrative Code §133, NY Part 711,
        // California OSHPD, Florida AHCA, Washington WAC 246-320). Add rows as
        // jurisdictions formally adopt FGI 2026.
        private static readonly List<FgiAdoptionEntry> Entries = new()
        {
            new("FGI-2.1-3.2", "TX", new DateTime(2027, 1, 1), "Texas Administrative Code §133.163 (anticipated)"),
            new("FGI-2.1-3.2", "NY", new DateTime(2027, 7, 1), "10 NYCRR Part 711 (proposed)"),
            new("FGI-2.1-3.4", "CA", new DateTime(2026, 7, 1), "HCAI / OSHPD adoption"),
            new("FGI-2.1-3.4", "FL", new DateTime(2027, 1, 1), "AHCA Rule 59A-3.080 (anticipated)"),
            new("FGI-2.1-3.4", "WA", new DateTime(2027, 7, 1), "WAC 246-320-500"),
            new("FGI-2.1-2.6", "TX", new DateTime(2027, 1, 1), "Texas Administrative Code §133"),
            new("FGI-2.1-7.2.2", "CA", new DateTime(2026, 7, 1), "HCAI imaging-room sizing"),
            new("FGI-2.1-8.2", "TX", new DateTime(2028, 1, 1), "Texas behavioral-health uplift"),
            new("FGI-2.1-8.2", "NY", new DateTime(2027, 7, 1), "OMH 14 NYCRR Part 580"),
            new("FGI-2.2-4.3", "FL", new DateTime(2027, 1, 1), "AHCA outpatient surgery"),
        };

        public static FgiSeverity ResolveSeverity(
            string clause,
            string jurisdictionCode,
            DateTime designFreezeDate)
        {
            if (string.IsNullOrWhiteSpace(clause) || string.IsNullOrWhiteSpace(jurisdictionCode))
                return FgiSeverity.Warning;

            var juris = jurisdictionCode.Trim().ToUpperInvariant();
            var hit = Entries.FirstOrDefault(e =>
                string.Equals(e.Clause, clause, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Jurisdiction, juris, StringComparison.OrdinalIgnoreCase)
                && e.EffectiveDate <= designFreezeDate);

            return hit is null ? FgiSeverity.Warning : FgiSeverity.Error;
        }

        public static IReadOnlyList<FgiAdoptionEntry> ListAdopted(string jurisdictionCode)
        {
            if (string.IsNullOrWhiteSpace(jurisdictionCode))
                return Array.Empty<FgiAdoptionEntry>();
            var juris = jurisdictionCode.Trim().ToUpperInvariant();
            return Entries
                .Where(e => string.Equals(e.Jurisdiction, juris, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.EffectiveDate)
                .ToList();
        }

        public static IReadOnlyList<FgiAdoptionEntry> ListAll() => Entries;
    }
}
