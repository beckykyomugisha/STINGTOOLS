using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core.Classification
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (C2) — CSI MasterFormat resolver (pure logic).
    //
    // Resolves a Revit element (category + family + type + SYS token) to a CSI
    // MasterFormat section + title from STING_CSI_MASTERFORMAT_MAP.csv. Free of
    // Autodesk.Revit.* so it can be unit-tested; the command (CsiCommands) reads
    // the CSV (corporate + project overlay) and walks the model.
    // ─────────────────────────────────────────────────────────────────────────

    public class CsiRule
    {
        public string Category { get; set; } = "";
        public string FamilyRegex { get; set; } = "";
        public string TypeRegex { get; set; } = "";
        public string Sys { get; set; } = "";
        public string Section { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>Optional NRM2 work-section code, so a single rule resolves both the
        /// CSI MasterFormat section AND the NRM2 section a BOQ line is billed under.
        /// Blank = let the BOQ engine derive the NRM2 section from the category.</summary>
        public string Nrm2 { get; set; } = "";
        /// <summary>Optional spec measurement basis (m2/m3/m/kg/each) for the section, so a
        /// spec can drive the BOQ measurement-basis advisory. Blank = no opinion.</summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// Optional element PHASE STATE this rule applies to — one of the
        /// <see cref="ElementPhaseState"/> values. KUT-5.
        ///
        /// <para>CSI Division 02 is Existing Conditions: demolition and removals. Before this
        /// column the resolver was never handed the element's phase, and nobody names a
        /// toposolid "demolition", so a naming-keyed Division 02 rule <b>could not fire on a
        /// real model</b>.</para>
        ///
        /// <para><b>It scores <see cref="PhaseWeight"/>, which outranks every other qualifier
        /// combined.</b> That is deliberate and it is what MasterFormat says: Division 02
        /// classifies by the STATE OF THE WORK, not by what the thing is. A demolished
        /// precast beam is demolition — it is not billed as precast concrete because the
        /// contractor is removing it, not casting it. Without the higher weight a demolished
        /// element carrying any family or type rule would keep its product classification and
        /// the Division 02 rule would fire only on elements no other rule matched, which is
        /// the same "reads as coverage, delivers nothing" outcome the withdrawn naming rules
        /// would have had.</para>
        ///
        /// <para>Matched exactly and case-insensitively, like <see cref="Sys"/>. An element
        /// whose state is unknown carries the empty string and therefore matches no
        /// phase-qualified rule.</para>
        /// </summary>
        public string Phase { get; set; } = "";

        /// <summary>What a matched <see cref="Phase"/> contributes. Ten, so that a
        /// phase-qualified rule beats any combination of the product qualifiers (whose
        /// maximum is four) while phase rules still discriminate among themselves on the
        /// ordinary qualifiers.</summary>
        public const int PhaseWeight = 10;

        private Regex _famRx, _typeRx;
        private bool _compiled;

        private void Compile()
        {
            if (_compiled) return;
            _compiled = true;
            // Always case-insensitive - authors (project-overlay rows especially) need not
            // remember the inline "(?i)" prefix. The shipped maps already carry it, so this
            // only rescues rows that would otherwise have silently matched nothing.
            const RegexOptions opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            if (!string.IsNullOrEmpty(FamilyRegex)) { try { _famRx = new Regex(FamilyRegex, opt); } catch { } }
            if (!string.IsNullOrEmpty(TypeRegex)) { try { _typeRx = new Regex(TypeRegex, opt); } catch { } }
        }

        /// <summary>Match score, or -1 when the rule does not apply. Higher = more specific.</summary>
        public int Score(string category, string family, string type, string sys)
            => Score(category, family, type, sys, null);

        /// <summary>Match score including the element's phase state, or -1 when the rule does
        /// not apply. See <see cref="Phase"/> for why phase outranks the product qualifiers.</summary>
        public int Score(string category, string family, string type, string sys, string phase)
        {
            Compile();
            int score = 0;

            if (!string.IsNullOrEmpty(Phase))
            {
                if (string.IsNullOrEmpty(phase) ||
                    !string.Equals(Phase, phase, StringComparison.OrdinalIgnoreCase)) return -1;
                // MERGE NOTE (KUT-10): that change scales the other qualifiers to
                // CsiRule.QualifierWeight. PhaseWeight is already far above their maximum, so
                // nothing here needs to move; only the score++ lines below become
                // "score += QualifierWeight".
                score += PhaseWeight;
            }

            bool anyCat = !string.IsNullOrEmpty(Category) && Category != "*";
            if (anyCat)
            {
                if (!string.Equals(Category, category, StringComparison.OrdinalIgnoreCase)) return -1;
                score++;
            }
            if (!string.IsNullOrEmpty(FamilyRegex))
            {
                if (_famRx == null || string.IsNullOrEmpty(family) || !_famRx.IsMatch(family)) return -1;
                score++;
            }
            if (!string.IsNullOrEmpty(TypeRegex))
            {
                if (_typeRx == null || string.IsNullOrEmpty(type) || !_typeRx.IsMatch(type)) return -1;
                score++;
            }
            if (!string.IsNullOrEmpty(Sys))
            {
                if (!string.Equals(Sys, sys, StringComparison.OrdinalIgnoreCase)) return -1;
                score++;
            }
            return score;
        }
    }

    public static class CsiMasterFormat
    {
        /// <summary>Parse map CSV lines (skips blank, '#'-comment, and the header row).</summary>
        public static List<CsiRule> ParseCsvLines(IEnumerable<string> lines)
        {
            var rules = new List<CsiRule>();
            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.TrimEnd('\r');
                if (line.TrimStart().StartsWith("#")) continue;
                // Split into 10 so the optional trailing columns are read while 6-column
                // legacy rows keep working. The shipped Titles carry no commas, so Title
                // stays whole on the shorter rows.
                //
                //   7  Nrm2      8  Unit      9  (reserved)      10  Phase
                //
                // COLUMN 9 IS RESERVED, NOT UNUSED. KUT-10 claims it for a Material
                // qualifier in a sibling change. Phase was placed at 10 so the two sets of
                // shipped rows compose under either merge order; putting both at 9 would
                // have made one branch's data silently mean the other's.
                //
                // A PROJECT OVERLAY WRITTEN AGAINST ANY EARLIER COLUMN COUNT STILL LOADS.
                // That is why the guard below is "< 6" rather than an exact count:
                // ParseCsvLines DROPS a row with fewer fields than it demands, SILENTLY,
                // with no error anywhere — so tightening it would take every project's
                // _BIM_COORD/csi_map.csv offline and look like the map simply had no opinion.
                var f = line.Split(new[] { ',' }, 10);
                if (f.Length < 6) continue;
                string cat = f[0].Trim();
                if (cat.Length == 0) continue;
                // skip the header row
                if (cat.Equals("Category", StringComparison.OrdinalIgnoreCase)) continue;
                rules.Add(new CsiRule
                {
                    Category = cat,
                    FamilyRegex = f[1].Trim(),
                    TypeRegex = f[2].Trim(),
                    Sys = f[3].Trim(),
                    Section = f[4].Trim(),
                    Title = f[5].Trim(),
                    Nrm2 = f.Length >= 7 ? f[6].Trim() : "",
                    Unit = f.Length >= 8 ? f[7].Trim() : "",
                    // f[8] is the reserved column — see the split comment above.
                    Phase = f.Length >= 10 ? f[9].Trim() : "",
                });
            }
            return rules;
        }

        /// <summary>Best-matching rule for the element context, or null when none apply.
        /// Highest score wins; ties resolve to the earliest rule in the list.</summary>
        public static CsiRule Resolve(IReadOnlyList<CsiRule> rules, string category, string family, string type, string sys)
            => Resolve(rules, category, family, type, sys, null, out _, out _);

        /// <summary>Phase-aware resolve (KUT-5). The four-argument overloads remain and
        /// delegate here with no phase, so every caller that has not been taught about phases
        /// behaves exactly as before rather than silently changing answer.</summary>
        public static CsiRule Resolve(IReadOnlyList<CsiRule> rules, string category, string family, string type,
            string sys, string phase)
            => Resolve(rules, category, family, type, sys, phase, out _, out _);

        /// <summary>Resolve + report the winning <paramref name="score"/> and how many rules
        /// tied at that top score (<paramref name="tieCount"/>). tieCount &gt; 1 means the match
        /// is AMBIGUOUS - row order silently decided it - so an audit can surface it and a
        /// more-specific rule be authored. Ties are counted by DISTINCT Section, so two rules
        /// that tie but agree on the code are not flagged.</summary>
        public static CsiRule Resolve(IReadOnlyList<CsiRule> rules, string category, string family, string type, string sys,
            out int score, out int tieCount)
            => Resolve(rules, category, family, type, sys, null, out score, out tieCount);

        /// <summary>Phase-aware resolve reporting the winning score and the tie count.</summary>
        public static CsiRule Resolve(IReadOnlyList<CsiRule> rules, string category, string family, string type, string sys,
            string phase, out int score, out int tieCount)
        {
            CsiRule best = null;
            int bestScore = -1;
            score = -1; tieCount = 0;
            if (rules == null) return null;
            for (int i = 0; i < rules.Count; i++)
            {
                int s = rules[i].Score(category, family, type, sys, phase);
                if (s > bestScore) { bestScore = s; best = rules[i]; }
            }
            if (bestScore < 0) return null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rules.Count; i++)
                if (rules[i].Score(category, family, type, sys, phase) == bestScore)
                    seen.Add(NormalizeSection(rules[i].Section));
            score = bestScore; tieCount = seen.Count;
            return best;
        }

        /// <summary>CSI section -&gt; NRM2 work-section bridge. Builds a normalised-section
        /// -&gt; NRM2-code lookup from every rule that carries an Nrm2. Pure (host-free).
        /// The EARLIEST rule wins on a section collision, because project-overlay rows are
        /// loaded before corporate ones and are meant to override them.
        ///
        /// <para>Prefer <see cref="Nrm2For"/> over indexing this directly. One CSI section
        /// can legitimately bill under two NRM2 sections depending on what the element is -
        /// 03 30 00 Cast-in-Place Concrete is NRM2 5 for a slab and 14 for a wall - and a
        /// section-keyed lookup has to pick one. This map is the fallback for an element
        /// that carries a stamped CSI section no rule matched; the matched rule's own Nrm2
        /// is the answer whenever there is one.</para></summary>
        public static Dictionary<string, string> BuildSectionToNrm2(IEnumerable<CsiRule> rules)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in rules ?? Enumerable.Empty<CsiRule>())
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Nrm2) || string.IsNullOrWhiteSpace(r.Section)) continue;
                string key = NormalizeSection(r.Section);
                if (key.Length == 0 || d.ContainsKey(key)) continue;
                d[key] = r.Nrm2.Trim();
            }
            return d;
        }

        /// <summary>CSI section -&gt; preferred measurement unit. Same shape and same
        /// first-wins rule as <see cref="BuildSectionToNrm2"/>; lets a spec drive the BOQ
        /// measurement-basis advisory.</summary>
        public static Dictionary<string, string> BuildSectionToUnit(IEnumerable<CsiRule> rules)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in rules ?? Enumerable.Empty<CsiRule>())
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Unit) || string.IsNullOrWhiteSpace(r.Section)) continue;
                string key = NormalizeSection(r.Section);
                if (key.Length == 0 || d.ContainsKey(key)) continue;
                d[key] = r.Unit.Trim();
            }
            return d;
        }

        /// <summary>The NRM2 work section for a priced line: the MATCHED rule's own Nrm2
        /// first, then the section-keyed bridge, then null meaning "no opinion - let the
        /// category derivation decide".
        ///
        /// <para>Rule-first is the whole point. Six of the shipped rows share section
        /// 03 30 00 with two different NRM2 answers, so reading the section map alone would
        /// bill every concrete slab, column, foundation, stair and ramp under NRM2 14
        /// (masonry) purely because the concrete-wall row is listed first.</para></summary>
        public static string Nrm2For(CsiRule matched, IReadOnlyDictionary<string, string> sectionToNrm2, string csiSection)
        {
            if (matched != null && !string.IsNullOrWhiteSpace(matched.Nrm2)) return matched.Nrm2.Trim();
            if (sectionToNrm2 != null && !string.IsNullOrWhiteSpace(csiSection) &&
                sectionToNrm2.TryGetValue(NormalizeSection(csiSection), out string n) && !string.IsNullOrWhiteSpace(n))
                return n;
            return null;
        }

        /// <summary>Canonical key for a CSI section number. Removes ALL whitespace (and
        /// upper-cases) so spaced "23 05 00" and unspaced "230500" reconcile to the same
        /// key — SpecLink exports spaced, models often store unspaced. Dots are preserved,
        /// so a child section "23 05 00.13" stays distinct from its parent.</summary>
        public static string NormalizeSection(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return Regex.Replace(s.Trim().ToUpperInvariant(), "\\s+", "");
        }

        /// <summary>
        /// The DISPLAY form of a CSI section number: canonical CSI spacing,
        /// "23 05 00" — what MasterFormat prints and what a QS expects to read.
        ///
        /// <para>This is the other half of <see cref="NormalizeSection"/>, and #554 is
        /// what happens without it. Normalisation strips whitespace so SpecLink's spaced
        /// "23 05 00" and a model's unspaced "230500" reconcile to one key — that is
        /// correct and must stay. But <see cref="Reconcile"/> then reported the
        /// normalised KEY as the section's identity, so its output read "224000" where
        /// CSI canonically writes "22 40 00". A matching convention had leaked into
        /// user-facing output.</para>
        ///
        /// <para>Matching and display are different jobs. Collapsing them either breaks
        /// reconciliation across the two source formats or prints a section number in a
        /// form CSI does not use; keeping both functions costs one method and closes the
        /// seam instead of picking a loser.</para>
        ///
        /// <para>Level-4 child sections keep their dot suffix: "230500.13" renders as
        /// "23 05 00.13". Anything that is not a recognisable CSI number is returned
        /// UNCHANGED rather than forced into pairs — inventing a shape for input we do
        /// not understand would print a confident wrong section number, which is worse
        /// than printing exactly what we were given.</para>
        /// </summary>
        public static string FormatSection(string s)
        {
            string key = NormalizeSection(s);
            if (key.Length == 0) return "";

            // Split an optional level-4 suffix: "230500.13" → "230500" + ".13"
            string stem = key, suffix = "";
            int dot = key.IndexOf('.');
            if (dot >= 0) { stem = key.Substring(0, dot); suffix = key.Substring(dot); }

            // CSI numbers are an even count of digits in 2-digit pairs (division 23,
            // level-2 2305, level-3 230500). Anything else is not ours to reformat.
            if (stem.Length < 2 || stem.Length % 2 != 0) return key;
            foreach (char c in stem) if (c < '0' || c > '9') return key;

            var sb = new System.Text.StringBuilder(stem.Length + stem.Length / 2 + suffix.Length);
            for (int i = 0; i < stem.Length; i += 2)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(stem, i, 2);
            }
            sb.Append(suffix);
            return sb.ToString();
        }

        public class CsiTocEntry { public string Section; public string Title; }
        public class CsiReconcileResult
        {
            // model CSI sections with no matching spec section
            public List<CsiTocEntry> SpecGaps { get; } = new List<CsiTocEntry>();
            // spec sections with no model content (INFO — possible over-specification)
            public List<CsiTocEntry> OverSpec { get; } = new List<CsiTocEntry>();
            // section present in both but titles differ
            public List<(string Section, string ModelTitle, string SpecTitle)> TitleMismatches { get; }
                = new List<(string, string, string)>();
        }

        /// <summary>
        /// Reconcile model CSI sections against the spec TOC. modelSections /
        /// specSections map a normalised section number → a representative title.
        /// </summary>
        public static CsiReconcileResult Reconcile(
            IDictionary<string, string> modelSections, IDictionary<string, string> specSections)
        {
            var result = new CsiReconcileResult();
            var model = Norm(modelSections);
            var spec = Norm(specSections);

            // #554 — MATCH on the normalised key (kv.Key), REPORT the display form.
            // Ordering also uses the normalised key: it sorts identically to the spaced
            // form and avoids re-deriving it per comparison.
            foreach (var kv in model.OrderBy(k => k.Key))
            {
                if (!spec.TryGetValue(kv.Key, out string specTitle))
                    result.SpecGaps.Add(new CsiTocEntry { Section = FormatSection(kv.Key), Title = kv.Value });
                else if (!TitlesEqual(kv.Value, specTitle))
                    result.TitleMismatches.Add((FormatSection(kv.Key), kv.Value, specTitle));
            }
            foreach (var kv in spec.OrderBy(k => k.Key))
                if (!model.ContainsKey(kv.Key))
                    result.OverSpec.Add(new CsiTocEntry { Section = FormatSection(kv.Key), Title = kv.Value });

            return result;
        }

        private static Dictionary<string, string> Norm(IDictionary<string, string> src)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in src ?? new Dictionary<string, string>())
            {
                string key = NormalizeSection(kv.Key);
                if (key.Length == 0) continue;
                if (!d.ContainsKey(key)) d[key] = kv.Value ?? "";
            }
            return d;
        }

        private static bool TitlesEqual(string a, string b)
        {
            string Na(string s) => Regex.Replace((s ?? "").Trim().ToLowerInvariant(), "\\s+", " ");
            string na = Na(a), nb = Na(b);
            if (na.Length == 0 || nb.Length == 0) return true; // missing title ≠ mismatch
            return na == nb;
        }
    }
}
