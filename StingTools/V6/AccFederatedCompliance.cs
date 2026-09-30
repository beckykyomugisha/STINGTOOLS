// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccFederatedCompliance.cs — the ISO 19650 tag tally across an ACC federation.
//
// Input: what AccModelProperties read from each model. Output: per model, per discipline and
// federation-wide counts (fully tagged / partial / untagged), which tokens are missing most,
// codes outside STING's vocabularies, and DUPLICATE TAGS ACROSS MODELS - the same ASS_TAG_1_TXT
// on two elements in two consultants' models, which no in-model check can see, because each
// model's own sequence counter only knows its own elements.
//
// Definitions (same as ComplianceScan's "strict" reading):
//   fully tagged = all 8 tokens non-blank AND the assembled tag non-blank
//   untagged     = no token and no tag
//   partial      = anything between
//
// A document that was NOT read contributes nothing and is listed as unread; the federation is
// then "incomplete", never compliant. Zero elements scanned is "no elements in scope", never
// 100 %. (CLAUDE.md: an empty scope is not a pass.)
//
// Revit-free and log-free.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StingTools.V6
{
    public sealed class AccComplianceCounts
    {
        public int Scanned, Full, Partial, Untagged, WithInvalidCode, DuplicateTagged;
        /// <summary>Token parameter → elements missing it.</summary>
        public Dictionary<string, int> MissingByToken { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Token parameter → elements whose value is outside the vocabulary.</summary>
        public Dictionary<string, int> InvalidByToken { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Null when nothing was scanned - an empty scope has no percentage.</summary>
        public double? FullPct => Scanned == 0 ? (double?)null : 100.0 * Full / Scanned;

        public string TopMissing(int n = 3)
        {
            var top = MissingByToken.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(n).Select(kv => $"{kv.Key} {kv.Value}").ToList();
            return top.Count == 0 ? "-" : string.Join("; ", top);
        }

        internal void Add(AccElementVerdict v, IEnumerable<string> tokenNames)
        {
            Scanned++;
            if (v.State == AccTagState.Full) Full++;
            else if (v.State == AccTagState.Partial) Partial++;
            else Untagged++;
            if (v.InvalidTokens.Count > 0) WithInvalidCode++;
            if (v.Duplicate) DuplicateTagged++;
            foreach (var t in tokenNames)
                if (!MissingByToken.ContainsKey(t)) MissingByToken[t] = 0;
            foreach (var t in v.MissingTokens) MissingByToken[t] = MissingByToken.TryGetValue(t, out var c) ? c + 1 : 1;
            foreach (var t in v.InvalidTokens) InvalidByToken[t] = InvalidByToken.TryGetValue(t, out var c) ? c + 1 : 1;
        }
    }

    public enum AccTagState { Untagged = 0, Partial = 1, Full = 2 }

    /// <summary>One element's verdict.</summary>
    public sealed class AccElementVerdict
    {
        public AccElementRecord Element { get; set; }
        public AccTagState State { get; set; }
        public string Tag { get; set; } = string.Empty;
        public string Discipline { get; set; } = string.Empty;
        public List<string> MissingTokens { get; } = new List<string>();
        public List<string> InvalidTokens { get; } = new List<string>();
        public bool Duplicate { get; set; }
    }

    public sealed class AccTagOccurrence
    {
        public string DocumentName { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    public sealed class AccDuplicateTag
    {
        public string Tag { get; set; } = string.Empty;
        public List<AccTagOccurrence> Occurrences { get; } = new List<AccTagOccurrence>();
        /// <summary>The occurrences span more than one model: invisible to any in-model check.</summary>
        public bool CrossModel => Occurrences.Select(o => o.DocumentName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
    }

    public sealed class AccDocumentSummary
    {
        public string DocumentName { get; set; } = string.Empty;
        public string VersionUrn { get; set; } = string.Empty;
        public AccDocumentReadStatus Status { get; set; }
        public string Detail { get; set; } = string.Empty;
        public List<string> AbsentParameters { get; } = new List<string>();
        public AccComplianceCounts Counts { get; } = new AccComplianceCounts();
    }

    /// <summary>Codes STING accepts per token (e.g. ISO19650Validator's DISC / SYS / FUNC sets).
    /// A token with no entry is not checked.</summary>
    public sealed class AccComplianceVocabulary
    {
        public Dictionary<string, HashSet<string>> ValidByToken { get; } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public void Add(string tokenParameter, IEnumerable<string> codes)
        {
            if (string.IsNullOrEmpty(tokenParameter) || codes == null) return;
            var set = new HashSet<string>(codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
            if (set.Count > 0) ValidByToken[tokenParameter] = set;
        }
    }

    public sealed class AccFederatedComplianceReport
    {
        public List<string> TokenNames { get; } = new List<string>();
        public string TagName { get; set; } = string.Empty;
        public List<AccDocumentSummary> Documents { get; } = new List<AccDocumentSummary>();
        public SortedDictionary<string, AccComplianceCounts> ByDiscipline { get; } = new SortedDictionary<string, AccComplianceCounts>(StringComparer.Ordinal);
        public AccComplianceCounts Federation { get; } = new AccComplianceCounts();
        public List<AccDuplicateTag> Duplicates { get; } = new List<AccDuplicateTag>();
        public List<AccElementVerdict> Elements { get; } = new List<AccElementVerdict>();

        public IEnumerable<AccDocumentSummary> Unread => Documents.Where(d => d.Status == AccDocumentReadStatus.Failed);
        public IEnumerable<AccDocumentSummary> NotRevit => Documents.Where(d => d.Status == AccDocumentReadStatus.NotRevit);
        public int CrossModelDuplicates => Duplicates.Count(d => d.CrossModel);

        /// <summary>Every document in the federation was read. False means the numbers cover
        /// only part of the federation and must be presented as such.</summary>
        public bool Complete => !Unread.Any();

        public string Headline()
        {
            var f = Federation;
            string scope = f.Scanned == 0
                ? "NO ELEMENTS IN SCOPE"
                : $"{f.Full:N0} of {f.Scanned:N0} elements fully tagged ({f.FullPct:F1} %)";
            int read = Documents.Count(d => d.Status == AccDocumentReadStatus.Read);
            string s = $"{scope} across {read} model(s)";
            if (!Complete) s = $"INCOMPLETE - {Unread.Count()} model(s) could not be read; " + s;
            if (CrossModelDuplicates > 0) s += $"; {CrossModelDuplicates} tag(s) duplicated ACROSS models";
            return s;
        }
    }

    public static class AccFederatedCompliance
    {
        public const string NoDiscipline = "(blank DISC)";

        /// <param name="tokenNames">The 8 token parameters in tag order (DISC first).</param>
        /// <param name="tagName">The assembled tag parameter (ASS_TAG_1_TXT).</param>
        public static AccFederatedComplianceReport Build(
            IEnumerable<AccDocumentRead> reads, IList<string> tokenNames, string tagName, AccComplianceVocabulary vocabulary = null)
        {
            var report = new AccFederatedComplianceReport { TagName = tagName ?? string.Empty };
            report.TokenNames.AddRange((tokenNames ?? Array.Empty<string>()).Where(t => !string.IsNullOrEmpty(t)));
            string discToken = report.TokenNames.FirstOrDefault() ?? string.Empty;

            var byTag = new Dictionary<string, AccDuplicateTag>(StringComparer.OrdinalIgnoreCase);
            var perDocVerdicts = new List<(AccDocumentSummary sum, List<AccElementVerdict> verdicts)>();

            foreach (var r in reads ?? Enumerable.Empty<AccDocumentRead>())
            {
                if (r == null) continue;
                var sum = new AccDocumentSummary
                {
                    DocumentName = r.DocumentName, VersionUrn = r.VersionUrn, Status = r.Status, Detail = r.Detail,
                };
                sum.AbsentParameters.AddRange(r.AbsentParameters);
                report.Documents.Add(sum);
                if (r.Status != AccDocumentReadStatus.Read) continue;

                var verdicts = new List<AccElementVerdict>();
                foreach (var el in r.Elements)
                {
                    var v = Judge(el, report.TokenNames, report.TagName, vocabulary);
                    v.Discipline = Clean(el.Get(discToken));
                    if (v.Discipline.Length == 0) v.Discipline = NoDiscipline;
                    verdicts.Add(v);
                    if (v.Tag.Length > 0)
                    {
                        if (!byTag.TryGetValue(v.Tag, out var dup)) byTag[v.Tag] = dup = new AccDuplicateTag { Tag = v.Tag };
                        dup.Occurrences.Add(new AccTagOccurrence { DocumentName = el.DocumentName, ExternalId = el.ExternalId, Category = el.Category });
                    }
                }
                perDocVerdicts.Add((sum, verdicts));
            }

            foreach (var d in byTag.Values.Where(d => d.Occurrences.Count > 1)
                         .OrderByDescending(d => d.CrossModel).ThenByDescending(d => d.Occurrences.Count).ThenBy(d => d.Tag, StringComparer.OrdinalIgnoreCase))
                report.Duplicates.Add(d);
            var dupTags = new HashSet<string>(report.Duplicates.Select(d => d.Tag), StringComparer.OrdinalIgnoreCase);

            foreach (var (sum, verdicts) in perDocVerdicts)
                foreach (var v in verdicts)
                {
                    v.Duplicate = v.Tag.Length > 0 && dupTags.Contains(v.Tag);
                    sum.Counts.Add(v, report.TokenNames);
                    report.Federation.Add(v, report.TokenNames);
                    if (!report.ByDiscipline.TryGetValue(v.Discipline, out var dc)) report.ByDiscipline[v.Discipline] = dc = new AccComplianceCounts();
                    dc.Add(v, report.TokenNames);
                    report.Elements.Add(v);
                }
            return report;
        }

        internal static AccElementVerdict Judge(AccElementRecord el, IList<string> tokenNames, string tagName, AccComplianceVocabulary vocabulary)
        {
            var v = new AccElementVerdict { Element = el, Tag = Clean(el.Get(tagName)) };
            int present = 0;
            foreach (var t in tokenNames)
            {
                string val = Clean(el.Get(t));
                if (val.Length == 0) { v.MissingTokens.Add(t); continue; }
                present++;
                if (vocabulary != null && vocabulary.ValidByToken.TryGetValue(t, out var ok) && !ok.Contains(val))
                    v.InvalidTokens.Add(t);
            }
            if (present == tokenNames.Count && tokenNames.Count > 0 && v.Tag.Length > 0) v.State = AccTagState.Full;
            else if (present == 0 && v.Tag.Length == 0) v.State = AccTagState.Untagged;
            else v.State = AccTagState.Partial;
            return v;
        }

        private static string Clean(string s) => (s ?? string.Empty).Trim();

        // ── CSV ────────────────────────────────────────────────────────────────

        /// <summary>Summary CSV: one row per model, per discipline, the federation, then every
        /// duplicate tag occurrence.</summary>
        public static string SummaryCsv(AccFederatedComplianceReport r, string header = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(header))
                foreach (var line in header.Split('\n')) sb.AppendLine("# " + line.TrimEnd('\r'));
            sb.AppendLine("# " + r.Headline());
            sb.AppendLine("Scope,Name,Read,Scanned,Fully tagged,Partial,Untagged,Fully tagged %,Invalid codes,Duplicate-tagged,Top missing tokens,Absent STING parameters,Detail");
            foreach (var d in r.Documents)
                sb.AppendLine(Row("Model", d.DocumentName, d.Status.ToString(), d.Status == AccDocumentReadStatus.Read ? d.Counts : null,
                    string.Join(" ", d.AbsentParameters), d.Detail));
            foreach (var kv in r.ByDiscipline)
                sb.AppendLine(Row("Discipline", kv.Key, "", kv.Value, "", ""));
            sb.AppendLine(Row("Federation", "ALL", r.Complete ? "Complete" : "INCOMPLETE", r.Federation, "", r.Headline()));
            sb.AppendLine();
            sb.AppendLine("Duplicate tag,Cross-model,Occurrences,Model,Category,UniqueId");
            foreach (var d in r.Duplicates)
                foreach (var o in d.Occurrences)
                    sb.AppendLine(string.Join(",", Csv(d.Tag), d.CrossModel ? "YES" : "no", d.Occurrences.Count.ToString(CultureInfo.InvariantCulture),
                        Csv(o.DocumentName), Csv(o.Category), Csv(o.ExternalId)));
            return sb.ToString();
        }

        /// <summary>Element CSV: every element that is not fully tagged, or has an invalid code,
        /// or carries a duplicated tag - the worklist per consultant.</summary>
        public static string ElementsCsv(AccFederatedComplianceReport r)
        {
            var sb = new StringBuilder();
            sb.Append("Model,UniqueId,Category,Name,State,Tag,Duplicate,Missing tokens,Invalid tokens");
            foreach (var t in r.TokenNames) sb.Append(',').Append(Csv(t));
            sb.AppendLine();
            foreach (var v in r.Elements.Where(v => v.State != AccTagState.Full || v.InvalidTokens.Count > 0 || v.Duplicate)
                         .OrderBy(v => v.Element.DocumentName, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.State))
            {
                sb.Append(string.Join(",", Csv(v.Element.DocumentName), Csv(v.Element.ExternalId), Csv(v.Element.Category), Csv(v.Element.Name),
                    v.State.ToString(), Csv(v.Tag), v.Duplicate ? "YES" : "", Csv(string.Join(" ", v.MissingTokens)), Csv(string.Join(" ", v.InvalidTokens))));
                foreach (var t in r.TokenNames) sb.Append(',').Append(Csv(v.Element.Get(t)));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string Row(string scope, string name, string read, AccComplianceCounts c, string absent, string detail)
        {
            if (c == null)
                return string.Join(",", scope, Csv(name), Csv(read), "", "", "", "", "", "", "", "", Csv(absent), Csv(detail));
            string pct = c.FullPct.HasValue ? c.FullPct.Value.ToString("F1", CultureInfo.InvariantCulture) : "n/a (none in scope)";
            return string.Join(",", scope, Csv(name), Csv(read), I(c.Scanned), I(c.Full), I(c.Partial), I(c.Untagged), Csv(pct),
                I(c.WithInvalidCode), I(c.DuplicateTagged), Csv(c.TopMissing()), Csv(absent), Csv(detail));
        }

        private static string I(int n) => n.ToString(CultureInfo.InvariantCulture);

        internal static string Csv(string s)
        {
            s ??= string.Empty;
            // A leading = + - @ would be run as a formula by Excel (CSV injection); model data
            // is third-party input.
            if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s;
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
