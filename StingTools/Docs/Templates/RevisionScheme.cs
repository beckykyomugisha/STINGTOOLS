// RevisionScheme.cs — IM-15: config-driven preliminary→contractual revision scheme.
//
// Deliberately Revit-free (no Autodesk.Revit.* / no Document) so the parsing rules
// are unit-testable outside a Revit host — see StingTools.Tags.Tests. The thin
// TemplateManifest-aware wrapper lives in DeliverableLifecycle, which is the only
// Revit-coupled part.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Planscape.Docs.Templates
{
    /// <summary>
    /// Preliminary → contractual revision prefixes for a project, parsed from the
    /// manifest's <c>revision_scheme</c> (default <c>"P01,P02,C01,C02"</c>).
    ///
    /// ISO 19650 uses P (preliminary) → C (contractual); appointments that mandate a
    /// different convention set <c>revision_scheme</c> in
    /// <c>_BIM_COORD/templates/manifest.json</c> rather than needing a code change.
    /// Stage prefixes are read in order of first appearance: the first distinct alpha
    /// prefix is the preliminary series, the second is the contractual one. Anything
    /// unset / unparseable falls back to P/C, so existing projects are unchanged.
    /// </summary>
    public sealed class RevisionScheme
    {
        public string PreliminaryPrefix { get; private set; } = "P";
        public string ContractualPrefix { get; private set; } = "C";
        public string FirstPreliminary  { get; private set; } = "P01";
        public string FirstContractual  { get; private set; } = "C01";

        /// <summary>True when the scheme declares no separate contractual series.</summary>
        public bool SingleStage =>
            string.Equals(PreliminaryPrefix, ContractualPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>ISO 19650 default: P01,P02 preliminary → C01,C02 contractual.</summary>
        public static readonly RevisionScheme Default = new RevisionScheme();

        /// <summary>
        /// Parses a comma/semicolon-separated revision scheme (e.g. "P01,P02,C01,C02").
        /// Null, blank, or prefix-less input yields <see cref="Default"/>.
        /// </summary>
        public static RevisionScheme Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Default;

            var stages = new List<KeyValuePair<string, string>>();   // prefix → first code carrying it
            foreach (string code in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim())
                                       .Where(s => s.Length > 0))
            {
                string prefix = new string(code.TakeWhile(char.IsLetter).ToArray());
                if (prefix.Length == 0) continue;                    // purely numeric entry — no stage letter
                if (stages.Any(s => string.Equals(s.Key, prefix, StringComparison.OrdinalIgnoreCase)))
                    continue;
                stages.Add(new KeyValuePair<string, string>(prefix, code));
            }
            if (stages.Count == 0) return Default;

            var scheme = new RevisionScheme
            {
                PreliminaryPrefix = stages[0].Key,
                FirstPreliminary  = stages[0].Value
            };
            if (stages.Count >= 2)
            {
                scheme.ContractualPrefix = stages[1].Key;
                scheme.FirstContractual  = stages[1].Value;
            }
            else
            {
                // Single-stage scheme: there is no contractual series to promote into,
                // so promotion becomes a no-op rather than inventing a "C" series the
                // appointment never asked for.
                scheme.ContractualPrefix = scheme.PreliminaryPrefix;
                scheme.FirstContractual  = scheme.FirstPreliminary;
            }
            return scheme;
        }

        /// <summary>
        /// Promotes a preliminary revision into the contractual series (P01 → C01 by
        /// default). Idempotent for revisions already outside the preliminary series.
        /// </summary>
        public string PromoteToContractual(string rev)
        {
            if (string.IsNullOrWhiteSpace(rev)) return FirstContractual;
            rev = rev.Trim();
            if (!rev.StartsWith(PreliminaryPrefix, StringComparison.OrdinalIgnoreCase)) return rev;
            // No separate contractual series declared ⇒ nothing to promote into.
            if (SingleStage) return rev;
            // A bare prefix carries no number: contractual prefix + "" would stamp the
            // deliverable with a malformed revision that no later Bump can parse.
            string suffix = rev.Substring(PreliminaryPrefix.Length);
            return suffix.Length == 0 ? FirstContractual : ContractualPrefix + suffix;
        }

        /// <summary>
        /// Increments a revision within its own series (P01 → P02, C01 → C02).
        /// Blank input yields the scheme's first preliminary revision. Unparseable input
        /// is returned unchanged — writing a sentinel would corrupt the sequence
        /// permanently, so the caller/user resolves it instead.
        /// </summary>
        public string Bump(string cur)
        {
            // No revision yet ⇒ this IS the first one. Returning P02 skipped P01 entirely.
            if (string.IsNullOrWhiteSpace(cur)) return FirstPreliminary;
            cur = cur.Trim();

            string prefix = new string(cur.TakeWhile(char.IsLetter).ToArray());
            string numStr = new string(cur.SkipWhile(char.IsLetter).ToArray());
            if (int.TryParse(numStr, out int n))
                return $"{prefix}{(n + 1).ToString(new string('0', Math.Max(numStr.Length, 1)))}";

            // Letter-only sequences (P, A, B…) advance the letter.
            if (numStr.Length == 0 && prefix.Length == 1 && prefix[0] != 'Z' && prefix[0] != 'z')
                return ((char)(prefix[0] + 1)).ToString();

            // Unparseable. The old code returned cur + "+1" — a sentinel that was then WRITTEN
            // to the revision field and persisted, corrupting the sequence permanently. Keep the
            // value as-is and let the caller/user resolve it; DeliverableLifecycle logs the
            // no-op so it stays visible in StingTools.log.
            return cur;
        }
    }
}
