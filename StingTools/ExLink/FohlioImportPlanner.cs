// FohlioImportPlanner.cs — the Revit-free half of the Fohlio round trip: which file row
// belongs to which model element, what a price cell means, and what a linked-percentage
// is over an empty scope. Kept free of Revit types so StingTools.Boq.Tests can
// <Compile Include> it and prove each rule.
//
// WHY THIS EXISTS (KUT deep review, 2026-10)
//
//  * Rooms were matched on Room Number alone, first room wins. KUT is one model per
//    building and the Owner keeps ONE site-wide finishes register in Fohlio, so importing
//    it into the Meetinghouse model wrote the Temple's room 101 finishes onto the
//    Meetinghouse's room 101 — silently, because the number matched.
//  * FF&E rows were matched on the element tag alone, although FOHLIO_REF_TXT is the
//    link key. After a re-tag a row followed the tag to whichever element now carried it,
//    and a duplicate tag sent the row to the first element and reported it "matched".
//  * A price cell such as "$1,250.00" or "UGX 4,500,000" failed an invariant-culture
//    parse, became 0, and was dropped without a word; a blank currency was then priced
//    as USD by the BOQ — an invented value on a figure that goes into a tender.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.ExLink
{
    /// <summary>One model-side candidate: an FF&E element or a room, by stable id.</summary>
    public sealed class FohlioCandidate
    {
        public long Id { get; set; }
        /// <summary>Revit UniqueId — stable across sessions and the only identity that
        /// cannot collide between two building models.</summary>
        public string UniqueId { get; set; } = "";
        /// <summary>Element tag (FF&E) or Room Number (rooms).</summary>
        public string Key { get; set; } = "";
        /// <summary>FOHLIO_REF_TXT currently on the element.</summary>
        public string FohlioRef { get; set; } = "";
    }

    /// <summary>One file-side row reduced to its identity fields.</summary>
    public sealed class FohlioRowIdentity
    {
        public int RowIndex { get; set; }
        public string UniqueId { get; set; } = "";
        public string Key { get; set; } = "";
        public string FohlioRef { get; set; } = "";
    }

    public sealed class FohlioMatch
    {
        public int RowIndex { get; set; }
        public long CandidateId { get; set; }
        /// <summary>"unique id", "Fohlio ref" or "key".</summary>
        public string By { get; set; } = "";
    }

    public sealed class FohlioUnmatched
    {
        public int RowIndex { get; set; }
        public string Key { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public sealed class FohlioMatchPlan
    {
        public List<FohlioMatch> Matches { get; } = new List<FohlioMatch>();
        public List<FohlioUnmatched> Unmatched { get; } = new List<FohlioUnmatched>();
    }

    public static class FohlioImportPlanner
    {
        public static string Norm(string s) => (s ?? "").Trim();

        /// <summary>
        /// Match file rows to model candidates.
        /// <list type="number">
        ///   <item>A row carrying a UniqueId matches only that element. If this model does not
        ///         contain it, the row belongs to another model and is skipped — it never falls
        ///         back to the key, which is how one building's room 101 used to land on
        ///         another's.</item>
        ///   <item>Otherwise, when <paramref name="useFohlioRef"/>, a row with a Fohlio ref
        ///         matches the one element carrying that ref.</item>
        ///   <item>Otherwise the key (tag / room number) — only when it is unique in the model
        ///         AND unique in the file. An ambiguous key is reported, never guessed.</item>
        /// </list>
        /// A row whose Fohlio ref differs from a non-empty ref already on the matched element is
        /// refused: re-pointing a link is a decision, not an import side effect. Two rows may
        /// never claim one element.
        /// </summary>
        public static FohlioMatchPlan Match(IEnumerable<FohlioRowIdentity> rows,
                                            IEnumerable<FohlioCandidate> candidates,
                                            bool useFohlioRef)
        {
            var plan = new FohlioMatchPlan();
            var cands = (candidates ?? Enumerable.Empty<FohlioCandidate>()).ToList();
            var rowList = (rows ?? Enumerable.Empty<FohlioRowIdentity>()).ToList();

            var byUid = cands.Where(c => Norm(c.UniqueId).Length > 0)
                             .GroupBy(c => Norm(c.UniqueId), StringComparer.Ordinal)
                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var byRef = cands.Where(c => Norm(c.FohlioRef).Length > 0)
                             .GroupBy(c => Norm(c.FohlioRef), StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var byKey = cands.Where(c => Norm(c.Key).Length > 0)
                             .GroupBy(c => Norm(c.Key), StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var keyCountInFile = rowList.Where(r => Norm(r.Key).Length > 0)
                                        .GroupBy(r => Norm(r.Key), StringComparer.OrdinalIgnoreCase)
                                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var claimed = new Dictionary<long, int>();
            foreach (var row in rowList)
            {
                string uid = Norm(row.UniqueId), key = Norm(row.Key), fref = Norm(row.FohlioRef);
                FohlioCandidate hit = null; string by = null, reason = null;

                if (uid.Length > 0)
                {
                    if (byUid.TryGetValue(uid, out hit)) by = "unique id";
                    else reason = "its Revit id is not in this model (row belongs to another model)";
                }
                else if (useFohlioRef && fref.Length > 0 && byRef.TryGetValue(fref, out var refHits))
                {
                    if (refHits.Count == 1) { hit = refHits[0]; by = "Fohlio ref"; }
                    else
                    {
                        // A Fohlio ref is item-level: 40 chairs share "CH-01". After the first
                        // import every instance carries it, so refusing here meant a priced item
                        // could never be updated again (L2-FOH-2). Narrow by the row's key.
                        var narrowed = key.Length == 0 ? new List<FohlioCandidate>()
                            : refHits.Where(c => string.Equals(Norm(c.Key), key, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (narrowed.Count == 1
                            && !(keyCountInFile.TryGetValue(key, out int nk) && nk > 1))
                        { hit = narrowed[0]; by = "Fohlio ref + key"; }
                        else reason = $"Fohlio ref '{fref}' is on {refHits.Count} elements in the model, and the row's key does not pick one";
                    }
                }
                else if (key.Length == 0)
                {
                    reason = "no key value";
                }
                else if (!byKey.TryGetValue(key, out var keyHits))
                {
                    reason = "no element in this model has this key";
                }
                else if (keyHits.Count > 1)
                {
                    reason = $"{keyHits.Count} elements in the model share this key";
                }
                else if (keyCountInFile.TryGetValue(key, out int n) && n > 1)
                {
                    reason = $"{n} rows in the file share this key";
                }
                else { hit = keyHits[0]; by = "key"; }

                if (hit != null && fref.Length > 0 && Norm(hit.FohlioRef).Length > 0
                    && !string.Equals(Norm(hit.FohlioRef), fref, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"element is already linked to Fohlio ref '{Norm(hit.FohlioRef)}', row says '{fref}'";
                    hit = null;
                }
                if (hit != null && claimed.TryGetValue(hit.Id, out int firstRow))
                {
                    reason = $"element already claimed by row {firstRow + 1}";
                    hit = null;
                }

                if (hit != null)
                {
                    claimed[hit.Id] = row.RowIndex;
                    plan.Matches.Add(new FohlioMatch { RowIndex = row.RowIndex, CandidateId = hit.Id, By = by });
                }
                else
                {
                    plan.Unmatched.Add(new FohlioUnmatched { RowIndex = row.RowIndex, Key = key.Length > 0 ? key : uid, Reason = reason });
                }
            }
            return plan;
        }

        /// <summary>Preview text listing unmatched rows with their reasons — the old dialogs said
        /// only "N unmatched", so an ambiguous room or a row from another model was invisible.</summary>
        public static string DescribeUnmatched(FohlioMatchPlan plan, string noun, int max = 15)
        {
            if (plan == null || plan.Unmatched.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"Unmatched {noun.ToLowerInvariant()} rows ({plan.Unmatched.Count}) — nothing is written for these:");
            foreach (var u in plan.Unmatched.Take(max))
                sb.AppendLine($"  row {u.RowIndex + 2} '{u.Key}': {u.Reason}");
            if (plan.Unmatched.Count > max) sb.AppendLine($"  … +{plan.Unmatched.Count - max} more (see the log)");
            return sb.ToString();
        }

        /// <summary>Linked percentage, or null when nothing is in scope. An empty scope is not
        /// "100% linked" — it is usually a wrong category list, a bad map or an empty model.</summary>
        public static double? LinkedPct(int total, int linked) =>
            total <= 0 ? (double?)null : 100.0 * linked / total;
    }

    /// <summary>Price cells as Fohlio and Excel actually write them.</summary>
    public static class FohlioMoney
    {
        private static readonly Dictionary<string, string> SymbolToIso = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "US$", "USD" }, { "USD$", "USD" }, { "€", "EUR" }, { "£", "GBP" },
            { "USh", "UGX" }, { "UShs", "UGX" }, { "Shs", "UGX" }, { "KSh", "KES" }, { "KShs", "KES" },
        };

        /// <summary>
        /// A currency code the BOQ can convert: an ISO-4217-shaped three-letter code, or a
        /// symbol that names exactly one currency. A bare "$" is ambiguous (USD, AUD, CAD…)
        /// and returns null — guessing a currency on a priced line is inventing data.
        /// </summary>
        public static string NormalizeCurrency(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return null;
            if (SymbolToIso.TryGetValue(s, out string iso)) return iso;
            if (Regex.IsMatch(s, "^[A-Za-z]{3}$")) return s.ToUpperInvariant();
            return null;
        }

        /// <summary>
        /// Parse a cost cell. Accepts "1250", "1,250.00", "$1,250.00", "UGX 4,500,000",
        /// "4 500 000", "1.250,00" (decimal comma) and returns the currency it names, if any.
        /// Returns false for anything it cannot read unambiguously — the caller reports it,
        /// never writes 0.
        /// </summary>
        public static bool TryParseCost(string raw, out double value, out string currencyFromCell)
        {
            value = 0; currencyFromCell = null;
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return false;

            // Peel a leading or trailing currency token.
            var m = Regex.Match(s, @"^(?<cur>[A-Za-z]{2,4}\$?|US\$|\$|€|£)\s*(?<num>.+)$");
            if (!m.Success) m = Regex.Match(s, @"^(?<num>.+?)\s*(?<cur>[A-Za-z]{2,4}|€|£)$");
            string num = m.Success ? m.Groups["num"].Value.Trim() : s;
            if (m.Success)
            {
                string cur = m.Groups["cur"].Value;
                currencyFromCell = cur == "$" ? null : NormalizeCurrency(cur);
                if (cur != "$" && currencyFromCell == null) return false;
            }

            num = num.Replace(" ", "").Replace(" ", "").Replace("'", "");
            if (!Regex.IsMatch(num, @"^[0-9.,]+$")) return false;

            int lastDot = num.LastIndexOf('.'), lastComma = num.LastIndexOf(',');
            string normalised;
            if (lastDot >= 0 && lastComma >= 0)
            {
                // Whichever separator comes last is the decimal point.
                normalised = lastComma > lastDot
                    ? num.Replace(".", "").Replace(',', '.')
                    : num.Replace(",", "");
            }
            else if (lastComma >= 0)
            {
                // "1,250" / "4,500,000" are thousands; "12,5" is a decimal comma.
                var groups = num.Split(',');
                bool thousands = groups.Skip(1).All(g => g.Length == 3);
                if (thousands) normalised = num.Replace(",", "");
                else if (groups.Length == 2) normalised = num.Replace(',', '.');
                else return false;
            }
            else
            {
                // Dots only: "1250.5" is a decimal; "1.250.000" is thousands.
                var groups = num.Split('.');
                if (groups.Length > 2)
                {
                    if (!groups.Skip(1).All(g => g.Length == 3)) return false;
                    normalised = num.Replace(".", "");
                }
                else if (groups.Length == 2 && groups[1].Length == 3)
                {
                    // "1.250" / "450.000": thousands in EUR- and UGX-formatted exports, a
                    // three-place decimal elsewhere. Reading it either way can be 1000x wrong,
                    // so it is reported, not guessed (L2-FOH-5).
                    return false;
                }
                else normalised = num;
            }

            return double.TryParse(normalised, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                   && value >= 0;
        }
    }

    /// <summary>
    /// Is a linked element out of step with what the last Fohlio import left on it? One rule for
    /// the Fohlio audit and the Owner KPI (L2-FOH-4). Text columns compare with the snapshot's
    /// recorded text; the unit cost compares as a NUMBER with the snapshot's cost and the currency
    /// with the snapshot's currency — the old text comparison read a NUMBER as "" on both sides,
    /// so a price edited after import never showed as stale.
    /// </summary>
    public static class FohlioStale
    {
        public static bool IsStale(IEnumerable<string> writeParams, Func<string, string> heldText,
                                   double heldCost, string heldCurrency,
                                   IDictionary<string, string> snapshotText, double snapCost, string snapCurrency,
                                   string costParam, string currencyParam)
        {
            foreach (var p in writeParams ?? Enumerable.Empty<string>())
            {
                if (string.Equals(p, costParam, StringComparison.OrdinalIgnoreCase))
                {
                    if (Math.Abs(heldCost - snapCost) > 0.005) return true;
                }
                else if (string.Equals(p, currencyParam, StringComparison.OrdinalIgnoreCase))
                {
                    string a = FohlioMoney.NormalizeCurrency(heldCurrency) ?? (heldCurrency ?? "").Trim();
                    string b = FohlioMoney.NormalizeCurrency(snapCurrency) ?? (snapCurrency ?? "").Trim();
                    if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else
                {
                    string snapV = snapshotText != null && snapshotText.TryGetValue(p, out var v) ? v ?? "" : "";
                    if (!string.Equals(heldText(p) ?? "", snapV, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Reads a CSV the user may have re-saved in Excel. Export writes UTF-8; Excel's plain "CSV"
    /// save is Windows-1252, which decoded as UTF-8 turns "600×600 – matt" into replacement
    /// characters that then overwrite the model as a "change" (L2-FOH-7). Refused, with the fix.
    /// </summary>
    public static class FohlioCsv
    {
        public static string[] ReadUtf8Lines(string path)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            string text;
            try { text = new System.Text.UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
            catch (System.Text.DecoderFallbackException)
            {
                throw new System.IO.InvalidDataException(
                    "The file is not UTF-8 (it was probably re-saved in Excel as plain 'CSV'). Nothing was imported. " +
                    "Save it again as 'CSV UTF-8 (Comma delimited)' and re-run.");
            }
            return text.Replace("\r\n", "\n").Split('\n').Where((l, i) => l.Length > 0 || i == 0).ToArray();
        }
    }
}
