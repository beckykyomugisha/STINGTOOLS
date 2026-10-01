// StingTools — Drawing Template Manager · the one sheet-number engine
//
// WHY THIS FILE EXISTS
//
// Sheet numbers were built in four places that did not agree:
//
//   * DrawingProducer substituted tokens with SafeShort shaping ("" -> "XX",
//     8-char cap) and uniquified with -A..-Z then a RANDOM suffix.
//   * DrawingRenumberCommand re-implemented substitution through the title-
//     block resolver, passed no level, so "A-RCP-{lvl}-{seq:D3}" renumbered
//     to "A-RCP-001" — the level silently erased (review P-3).
//   * ShopDrawingComposer carried a second copy of the uniquifier, silent and
//     O(N^2) over a batch (P-11).
//   * Everything read "the sequence" as the LAST run of digits, which on an
//     ISO number "...-0003-S2-P01" is the revision, not the sequence.
//
// Everything here is Revit-free, so the rules are unit-tested rather than
// discovered on an issued drawing. Revit-bound callers resolve the field
// values and hand over strings; this file does the string work.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public static class SheetNumberEngine
    {
        /// <summary>Stand-in for the sequence while a pattern is resolved
        /// without one. A control character cannot appear in an authored
        /// pattern or a Revit sheet number, so it cannot be confused with data.</summary>
        internal const char SeqSentinel = '\u0001';

        private static readonly Regex AnySeq = new Regex(@"\{seq(?::[Dd]\d+)?\}", RegexOptions.Compiled);

        // ── Substitution ───────────────────────────────────────────────

        /// <summary>
        /// The producer's token substitution. Moved here verbatim from
        /// DrawingProducer so production and renumbering cannot build the same
        /// sheet's number two different ways.
        /// </summary>
        public static string ApplyTokenPattern(string pattern,
            string disc, string lvl, string sys, string mark, string spool, string purpose,
            int seq, IDictionary<string, string> extras)
        {
            if (string.IsNullOrEmpty(pattern)) return pattern;
            var p = SubstituteExceptSeq(pattern, disc, lvl, sys, mark, spool, purpose, extras);

            // {seq:Dn} at whatever width the pattern asks for; a bare {seq} at
            // SheetNumberTokens.DefaultSeqWidth -- the same rule the title-block
            // pattern (SheetDisciplineResolver.FormatNumber) uses.
            return SheetNumberTokens.ApplySeq(p, seq);
        }

        private static string SubstituteExceptSeq(string pattern,
            string disc, string lvl, string sys, string mark, string spool, string purpose,
            IDictionary<string, string> extras)
        {
            var p = pattern;
            // Producer-specific shaping first (SafeShort sanitises and caps at
            // 8 chars), before the extras sweep can let raw values through.
            p = p.Replace("{disc}", SafeShort(disc));
            p = p.Replace("{discipline}", disc ?? "");
            p = p.Replace("{lvl}", SafeShort(lvl));
            p = p.Replace("{sys}", SafeShort(sys));
            p = p.Replace("{mark}", SafeShort(mark));
            p = p.Replace("{spool}", SafeShort(spool));
            p = p.Replace("{purpose}", purpose ?? "");

            // ISO 19650 tokens ({project} {originator} {vol} {type} {role}
            // {suit} {rev}) and anything else the canonical builder supplies.
            // "seq" is skipped: its width is the pattern's business, not the dict's.
            if (extras != null)
            {
                foreach (var kv in extras)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    if (string.Equals(kv.Key, "seq", StringComparison.OrdinalIgnoreCase)) continue;
                    // Every spelling of the token: {proj} and {project} are one token
                    // (SheetNumberTokens), as they are in the title-block pattern.
                    string canonical = SheetNumberTokens.CanonicalName(kv.Key);
                    p = p.Replace("{" + kv.Key + "}", kv.Value ?? "");
                    foreach (var alias in SheetNumberTokens.Spellings(canonical))
                        p = p.Replace("{" + alias + "}", kv.Value ?? "");
                }
            }
            return p;
        }

        /// <summary>Sanitise a segment and cap it at 8 characters. Empty is
        /// "XX": a visible placeholder, never a silently dropped segment.</summary>
        public static string SafeShort(string s)
        {
            if (string.IsNullOrEmpty(s)) return "XX";
            return new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').Take(8).ToArray());
        }

        // ── DTW-198: names are not numbers ────────────────────────────

        /// <summary>Characters Revit refuses in a view or sheet name.</summary>
        private static readonly char[] RevitNameIllegal = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        /// <summary>
        /// A value for a sheet NAME: kept whole, with only the characters Revit refuses
        /// in a name replaced (and the spacing tidied). <see cref="SafeShort"/> is number
        /// shaping — it stripped spaces and cut at eight, so "Ground Floor" printed
        /// "GroundFl" on the sheet's name. Empty is "XX", as in a number.
        /// </summary>
        public static string SheetNameSafe(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "XX";
            var chars = s.Select(c => RevitNameIllegal.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray();
            var tidy = Regex.Replace(new string(chars), @"\s+", " ").Trim();
            return tidy.Length == 0 ? "XX" : tidy;
        }

        /// <summary>
        /// A sheet NAME from <paramref name="pattern"/>: {lvl}, {mark}, {spool}, {sys} and
        /// {disc} take the full values (<see cref="SheetNameSafe"/>), everything else —
        /// the ISO extras, {seq} — exactly as <see cref="ApplyTokenPattern"/> resolves
        /// them for the number. <paramref name="levelName"/> is the level's NAME, never
        /// its code.
        /// </summary>
        public static string ApplyNamePattern(string pattern,
            string disc, string levelName, string sys, string mark, string spool, string purpose,
            int seq, IDictionary<string, string> extras)
        {
            if (string.IsNullOrEmpty(pattern)) return pattern;
            var p = pattern;
            if (p.IndexOf("{lvl}", StringComparison.Ordinal) >= 0) p = p.Replace("{lvl}", SheetNameSafe(levelName));
            if (p.IndexOf("{mark}", StringComparison.Ordinal) >= 0) p = p.Replace("{mark}", SheetNameSafe(mark));
            if (p.IndexOf("{spool}", StringComparison.Ordinal) >= 0) p = p.Replace("{spool}", SheetNameSafe(spool));
            if (p.IndexOf("{sys}", StringComparison.Ordinal) >= 0) p = p.Replace("{sys}", SheetNameSafe(sys));
            if (p.IndexOf("{disc}", StringComparison.Ordinal) >= 0) p = p.Replace("{disc}", SheetNameSafe(disc));
            return ApplyTokenPattern(p, disc, levelName, sys, mark, spool, purpose, seq, extras);
        }

        /// <summary>
        /// A level for a sheet NUMBER when no code is known: <see cref="SafeShort"/>, but
        /// a trailing number survives the eight-character cut — "Basement 1" is
        /// "Basemen1", not "Basement" (which "Basement 2" also became).
        /// </summary>
        public static string ShortLevel(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName)) return "XX";
            var clean = new string(levelName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            if (clean.Length == 0) return "XX";
            if (clean.Length <= 8) return clean;
            var digits = Regex.Match(clean, @"\d+$").Value;
            if (digits.Length == 0 || digits.Length >= 8) return clean.Substring(0, 8);
            return clean.Substring(0, 8 - digits.Length) + digits;
        }

        /// <summary>
        /// {lvl} for a sheet NUMBER: the level's CODE (ScopeBoxRevit.LevelCodes, or the
        /// ISO level code under an ISO pattern) when the caller has one, else
        /// <see cref="ShortLevel"/> of the name. Two levels never share a code; two
        /// truncated names did.
        /// </summary>
        public static string NumberLevelToken(string levelCode, string levelName)
        {
            if (!string.IsNullOrWhiteSpace(levelCode))
            {
                var code = SafeShort(levelCode);
                if (!string.IsNullOrEmpty(code)) return code;
            }
            return ShortLevel(levelName);
        }

        // ── DTW-210: an existing sheet's {lvl} follows its own number ──

        /// <summary>True when <paramref name="sheetNumber"/> is a full ISO 19650 identifier,
        /// with or without its suitability / revision tail.</summary>
        public static bool IsIsoShapedNumber(string sheetNumber)
            => Iso19650DocumentCode.LooksAssembled(sheetNumber)
            || Iso19650DocumentCode.LooksAssembled(SheetNumberPolicy.StripStatusSuffix(sheetNumber));

        /// <summary>
        /// {lvl} for re-stamping an EXISTING sheet's title block. Decided by the shape of
        /// the sheet's own number — an ISO-shaped number carries the ISO level code, any
        /// other the level name — not by the policy in force today. After a policy switch
        /// the current policy described sheets numbered under the old one, and their
        /// title blocks stopped matching their numbers. <paramref name="levelIsName"/>
        /// false (a value from the profile, not a level name) is returned unchanged.
        /// </summary>
        public static string ExistingSheetLevel(string sheetNumber, string level, bool levelIsName,
            IDictionary<string, string> isoCodesByName)
            => SheetNumberPolicy.ExistingSheetLevelToken(
                IsIsoShapedNumber(sheetNumber) ? SheetNumberPolicy.IsoPattern : null,
                level, levelIsName, isoCodesByName);

        /// <summary>
        /// The pattern resolved in every token except the sequence, which is
        /// left as <see cref="SeqSentinel"/>, then tidied. The shape every
        /// number in one counter bucket shares. Null when the pattern has no
        /// sequence token, or more than one.
        /// </summary>
        public static string Template(string pattern,
            string disc, string lvl, string sys, string mark, string spool, string purpose,
            IDictionary<string, string> extras)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            if (AnySeq.Matches(pattern).Count != 1) return null;
            var marked = AnySeq.Replace(pattern, SeqSentinel.ToString());
            return SheetNumberTidy.Collapse(
                SubstituteExceptSeq(marked, disc, lvl, sys, mark, spool, purpose, extras));
        }

        /// <summary>Human-readable form of a template: the sequence as '#'.</summary>
        public static string Mask(string template)
            => template?.Replace(SeqSentinel, '#');

        // ── Reading a sequence back ────────────────────────────────────

        /// <summary>
        /// The sequence a sheet number was issued with, read against the
        /// template it was built from rather than by position. Tolerates the
        /// "-A" style suffix <see cref="MakeUnique"/> adds. Null when the number
        /// does not have the template's shape — which a caller must treat as
        /// "not ours", never as zero.
        /// </summary>
        public static int? ExtractSequence(string sheetNumber, string template)
        {
            if (string.IsNullOrEmpty(sheetNumber) || string.IsNullOrEmpty(template)) return null;
            var parts = template.Split(SeqSentinel);
            if (parts.Length != 2) return null;
            var rx = "^" + Regex.Escape(parts[0]) + @"(\d+)" + Regex.Escape(parts[1]) + @"(?:-[A-Z0-9]{1,6})?$";
            var m = Regex.Match(sheetNumber, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success) return null;
            return int.TryParse(m.Groups[1].Value, out var n) ? n : (int?)null;
        }

        /// <summary>
        /// The trailing-digit heuristic, kept for sheets with no known template
        /// (hand-numbered, legacy). Skips a trailing ISO revision code
        /// ("-P01", "-C02") and a uniquifier suffix, so "...-0003-S2-P01" reads 3,
        /// not 1.
        /// </summary>
        public static int? ExtractTrailingSequence(string sheetNumber)
        {
            if (string.IsNullOrEmpty(sheetNumber)) return null;
            var s = sheetNumber;
            // Strip ISO suitability + revision suffixes, e.g. "-S2-P01" / "-P01".
            s = Regex.Replace(s, @"(-[SAB]\d)?-[PC]\d{2}$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"-[A-Z]$", "");
            var m = Regex.Match(s, @"(\d+)\D*$");
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : (int?)null;
        }

        // ── Uniqueness ─────────────────────────────────────────────────

        /// <summary>
        /// Return <paramref name="baseNumber"/>, or the first free "-A".."-Z",
        /// then "-AA".."-ZZ", variant. Deterministic — the random suffix it
        /// replaces made a re-run produce a different number for the same sheet.
        /// The chosen number is added to <paramref name="taken"/>, so one set
        /// serves a whole batch (no per-sheet re-collection). <paramref name="note"/>
        /// is non-null whenever the number was changed: a suffixed sheet number
        /// is a collision, and a collision is never silent.
        /// </summary>
        public static string MakeUnique(string baseNumber, ISet<string> taken, out string note)
        {
            note = null;
            if (string.IsNullOrEmpty(baseNumber) || taken == null) return baseNumber;
            if (taken.Add(baseNumber)) return baseNumber;

            foreach (var suffix in Suffixes())
            {
                var candidate = baseNumber + "-" + suffix;
                if (taken.Add(candidate))
                {
                    note = $"Sheet number '{baseNumber}' already exists; used '{candidate}'. " +
                           "Two drawings resolved to the same number — check their patterns.";
                    return candidate;
                }
            }
            note = $"Sheet number '{baseNumber}' and all 702 suffixed variants exist; left unassigned.";
            return null;
        }

        private static IEnumerable<string> Suffixes()
        {
            for (char c = 'A'; c <= 'Z'; c++) yield return c.ToString();
            for (char a = 'A'; a <= 'Z'; a++)
                for (char b = 'A'; b <= 'Z'; b++) yield return new string(new[] { a, b });
        }

        // ── Counter buckets ────────────────────────────────────────────

        /// <summary>
        /// The persisted counter a sheet draws its sequence from.
        ///
        /// Under the Profile policy this is the historical (type, package,
        /// discipline, volume) key, byte-for-byte, so no project in flight has
        /// its counters reset by this change.
        ///
        /// Under the ISO policy the NUMBER does not carry the drawing-type id —
        /// 29 architectural profiles resolve to the same volume/type/role — so a
        /// per-type counter hands arch-plan and arch-rcp on one level the same
        /// "...-0001-..." and the second is suffixed. The bucket is therefore the
        /// template itself: drawings that would share a number share a counter.
        /// </summary>
        public static string CounterBucket(SheetNumberPolicyKind policy, string template,
            string drawingTypeId, string packageId, string discipline, string vol)
        {
            if (policy == SheetNumberPolicyKind.Iso && !string.IsNullOrEmpty(template))
                return "iso|" + Mask(template);
            return string.Join("|", drawingTypeId ?? "", packageId ?? "", discipline ?? "", vol ?? "");
        }

        // ── Renumbering ────────────────────────────────────────────────

        public sealed class RenumberItem
        {
            public string Id;
            public string CurrentNumber;
            /// <summary>Counter bucket — sheets in one bucket share one 1..N run.</summary>
            public string Bucket;
            public int? CurrentSeq;
            public bool Locked;
            /// <summary>DTW-211: the current number is not in the shape of the pattern the
            /// policy now gives this sheet (e.g. a profile-era number under the ISO policy) —
            /// renumbering it CONVERTS it rather than closing a gap.</summary>
            public bool ShapeChange;
            /// <summary>DTW-211: the sheet carries an issued revision.</summary>
            public bool Issued;
            /// <summary>This sheet's number for a given sequence, built from its
            /// OWN tokens (level, mark), so compaction never erases identity.</summary>
            public Func<int, string> NumberFor;
        }

        public sealed class RenumberMove
        {
            public string Id;
            public string From;
            public string To;
            public int Seq;
        }

        public sealed class RenumberPlan
        {
            public List<RenumberMove> Moves { get; } = new List<RenumberMove>();
            /// <summary>Sheets left alone because their target belongs to a sheet
            /// outside the plan. Reported, never forced.</summary>
            public List<string> Conflicts { get; } = new List<string>();
            /// <summary>Highest sequence in use per bucket after the plan,
            /// INCLUDING locked and pinned sheets — the value the counter must
            /// resume from, or the next production collides with a locked sheet.</summary>
            public Dictionary<string, int> HighWater { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
            /// <summary>DTW-7: sheets kept on their number because it is a full
            /// ISO 19650 identifier and the policy is not ISO. Reported, never moved.</summary>
            public List<string> IsoPreserved { get; } = new List<string>();
            /// <summary>DTW-211: the moves that change a sheet's number SHAPE (a conversion
            /// to the policy's pattern), listed apart from gap-closing moves.</summary>
            public List<RenumberMove> Conversions { get; } = new List<RenumberMove>();
            /// <summary>DTW-211: issued sheets a conversion would have renumbered, kept
            /// because the caller did not opt in to converting issued sheets.</summary>
            public List<string> IssuedKept { get; } = new List<string>();
        }

        /// <summary>
        /// DTW-7: must this sheet keep its number because it already carries a full
        /// ISO 19650 identifier? Under any policy but ISO the sheet's pattern is
        /// not the identifier's, so "compacting" it replaces an issued identifier
        /// with a profile short number. Under the ISO policy the identifier IS the
        /// policy's number and may be compacted like any other.
        /// </summary>
        public static bool PinsIsoIdentifier(string currentNumber, SheetNumberPolicyKind policy)
            => policy != SheetNumberPolicyKind.Iso && Iso19650DocumentCode.LooksAssembled(currentNumber);

        /// <summary>
        /// Compact each bucket to a gap-free run while keeping every sheet's own
        /// identity segments. Locked sheets keep both number and sequence, and
        /// their sequences are skipped rather than reused. A move whose target is
        /// held by a sheet outside the plan (unstamped, another bucket) pins the
        /// mover in place instead; pinning repeats until the plan is conflict-free,
        /// so what is returned can be applied without Revit rejecting any of it.
        /// </summary>
        /// <para>DTW-211: a sheet whose number is not in its pattern's shape
        /// (<see cref="RenumberItem.ShapeChange"/>) is converted, not compacted; such moves
        /// are listed in <see cref="RenumberPlan.Conversions"/>. An ISSUED sheet is never
        /// converted unless <paramref name="convertIssued"/> — its number is on drawings
        /// already sent out; it is pinned and listed in <see cref="RenumberPlan.IssuedKept"/>.</para>
        public static RenumberPlan PlanRenumber(IReadOnlyList<RenumberItem> items, IEnumerable<string> allNumbers,
            SheetNumberPolicyKind policy = SheetNumberPolicyKind.Profile, bool convertIssued = false)
        {
            var plan = PlanRenumberCore(items, allNumbers, policy, convertIssued);
            if (items == null) return plan;
            var shape = new HashSet<string>(items.Where(i => i.ShapeChange).Select(i => i.Id), StringComparer.Ordinal);
            plan.Conversions.AddRange(plan.Moves.Where(m => shape.Contains(m.Id)));
            return plan;
        }

        private static RenumberPlan PlanRenumberCore(IReadOnlyList<RenumberItem> items, IEnumerable<string> allNumbers,
            SheetNumberPolicyKind policy, bool convertIssued)
        {
            var plan = new RenumberPlan();
            if (items == null || items.Count == 0) return plan;

            var pinned = new HashSet<string>(items.Where(i => i.Locked).Select(i => i.Id), StringComparer.Ordinal);
            if (!convertIssued)
                foreach (var i in items)
                {
                    if (i.Locked || !i.ShapeChange || !i.Issued || PinsIsoIdentifier(i.CurrentNumber, policy)) continue;
                    pinned.Add(i.Id);
                    plan.IssuedKept.Add($"{i.CurrentNumber}: issued (has an issued revision); kept — converting it to the " +
                                        $"{policy} numbering would change the number on drawings already sent out.");
                }
            foreach (var i in items)
            {
                if (i.Locked || !PinsIsoIdentifier(i.CurrentNumber, policy)) continue;
                pinned.Add(i.Id);
                plan.IsoPreserved.Add($"{i.CurrentNumber}: already a full ISO 19650 identifier; kept " +
                                      $"(the {policy} policy would replace it — renumber it with the ISO tools).");
            }
            var ids = new HashSet<string>(items.Select(i => i.Id), StringComparer.Ordinal);
            var numberOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in items)
                if (!string.IsNullOrEmpty(i.CurrentNumber)) numberOwner[i.CurrentNumber] = i.Id;
            var foreign = new HashSet<string>(
                (allNumbers ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n) && !numberOwner.ContainsKey(n)),
                StringComparer.OrdinalIgnoreCase);

            for (int guard = 0; guard <= items.Count; guard++)
            {
                plan.Moves.Clear();
                plan.HighWater.Clear();
                var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // number -> id
                // Pinned sheets hold their numbers.
                foreach (var i in items.Where(i => pinned.Contains(i.Id)))
                    if (!string.IsNullOrEmpty(i.CurrentNumber)) targets[i.CurrentNumber] = i.Id;

                foreach (var g in items.GroupBy(i => i.Bucket ?? "", StringComparer.Ordinal))
                {
                    var reserved = new HashSet<int>(g.Where(i => pinned.Contains(i.Id) && i.CurrentSeq.HasValue)
                                                     .Select(i => i.CurrentSeq.Value));
                    int high = reserved.Count > 0 ? reserved.Max() : 0;
                    int seq = 0;
                    foreach (var i in g.Where(i => !pinned.Contains(i.Id))
                                       .OrderBy(i => i.CurrentSeq ?? int.MaxValue)
                                       .ThenBy(i => i.CurrentNumber, StringComparer.Ordinal))
                    {
                        do { seq++; } while (reserved.Contains(seq));
                        high = Math.Max(high, seq);
                        var to = i.NumberFor?.Invoke(seq);
                        if (string.IsNullOrEmpty(to)) { to = i.CurrentNumber; }
                        if (!string.Equals(to, i.CurrentNumber, StringComparison.Ordinal))
                            plan.Moves.Add(new RenumberMove { Id = i.Id, From = i.CurrentNumber, To = to, Seq = seq });
                        if (!string.IsNullOrEmpty(to)) targets.TryAdd(to, i.Id);
                    }
                    plan.HighWater[g.Key] = high;
                }

                // A move collides if its target is foreign, or another sheet in the
                // plan ends up on the same number.
                var clash = new List<RenumberMove>();
                var finalCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                // DTW-19: index the moves once per pass — a FirstOrDefault per item
                // made each pass O(n^2) and the pinning loop O(n^3).
                var moveById = new Dictionary<string, RenumberMove>(StringComparer.Ordinal);
                foreach (var m in plan.Moves) moveById[m.Id] = m;
                foreach (var i in items)
                {
                    moveById.TryGetValue(i.Id, out var mv);
                    var final = mv?.To ?? i.CurrentNumber;
                    if (string.IsNullOrEmpty(final)) continue;
                    finalCount[final] = finalCount.TryGetValue(final, out var c) ? c + 1 : 1;
                }
                foreach (var mv in plan.Moves)
                    if (foreign.Contains(mv.To) || finalCount[mv.To] > 1) clash.Add(mv);

                if (clash.Count == 0) return plan;
                foreach (var mv in clash)
                {
                    pinned.Add(mv.Id);
                    plan.Conflicts.Add(foreign.Contains(mv.To)
                        ? $"{mv.From} -> {mv.To}: '{mv.To}' is held by a sheet outside this renumber; left as {mv.From}."
                        : $"{mv.From} -> {mv.To}: would duplicate another sheet's number; left as {mv.From}.");
                }
            }
            // Unreachable in practice: every iteration pins at least one sheet.
            plan.Moves.Clear();
            plan.Conflicts.Add("Renumber plan did not converge; nothing will be changed.");
            return plan;
        }
    }
}
