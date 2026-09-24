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

        private static readonly Regex SeqWidth = new Regex(@"\{seq:D(\d+)\}", RegexOptions.Compiled);
        private static readonly Regex AnySeq = new Regex(@"\{seq(?::D\d+)?\}", RegexOptions.Compiled);

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

            // {seq:Dn} at whatever width the pattern asks for, then bare {seq}
            // at the historical 4-digit default. The extras sweep may already
            // have consumed a bare {seq}: DrawingTokenContext.Build emits it at
            // D4, the same width as here, so whichever runs first agrees.
            p = SeqWidth.Replace(p, m => seq.ToString("D" + m.Groups[1].Value));
            p = p.Replace("{seq}", seq.ToString("D4"));
            return p;
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
                    p = p.Replace("{" + kv.Key + "}", kv.Value ?? "");
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
        }

        /// <summary>
        /// Compact each bucket to a gap-free run while keeping every sheet's own
        /// identity segments. Locked sheets keep both number and sequence, and
        /// their sequences are skipped rather than reused. A move whose target is
        /// held by a sheet outside the plan (unstamped, another bucket) pins the
        /// mover in place instead; pinning repeats until the plan is conflict-free,
        /// so what is returned can be applied without Revit rejecting any of it.
        /// </summary>
        public static RenumberPlan PlanRenumber(IReadOnlyList<RenumberItem> items, IEnumerable<string> allNumbers)
        {
            var plan = new RenumberPlan();
            if (items == null || items.Count == 0) return plan;

            var pinned = new HashSet<string>(items.Where(i => i.Locked).Select(i => i.Id), StringComparer.Ordinal);
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
                foreach (var i in items)
                {
                    var mv = plan.Moves.FirstOrDefault(m => m.Id == i.Id);
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
