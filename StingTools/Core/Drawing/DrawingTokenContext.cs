using StingTools.Core;
// StingTools — Drawing Template Manager · INT-06 fix
//
// DrawingTokenContext is the single source of truth for the token
// dictionary fed into TitleBlockParamApplier (and any future
// substitution path). Before this helper existed, two parallel call
// sites — ShopDrawingComposer and DrawingTypeSheetAdapter — each
// produced their own token dict, so the SheetManager path got an
// impoverished {disc, discipline, seq, spool, sys, lvl, mark} and the
// fabrication path got the full ISO 19650 set. The applier then
// silently produced different title-block cells from the same
// profile depending on which command was invoked.
//
// All callers now go through Build(...). Optional fields stay empty
// rather than missing so the regex-based applier always sees a
// canonical key set.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class DrawingTokenContext
    {
        /// <summary>
        /// Canonical token dictionary fed into TitleBlockParamApplier.
        /// Every caller — fabrication, sheet manager, scope-box generator,
        /// production-rule engine — passes through this builder.
        /// Optional values are left as empty strings (never null) so the
        /// applier's literal-passthrough rule applies uniformly.
        /// </summary>
        public static Dictionary<string, string> Build(
            Document doc,
            DrawingType dt,
            string discCode = null,
            string discipline = null,
            string sysCode = null,
            string levelCode = null,
            int? seq = null,
            int seqWidth = 4,
            string spool = null,
            string mark = null)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "spool",      spool      ?? string.Empty },
                { "disc",       discCode   ?? dt?.Discipline ?? string.Empty },
                { "discipline", discipline ?? dt?.Discipline ?? string.Empty },
                // P4: fall back to the profile's own System code (DCW/HVAC/…)
                // when the caller doesn't pass one, mirroring the disc/discipline
                // fallback above. Lets every producer that routes through this
                // builder fill the {sys} token / SYSTEM title-block cell with no
                // per-call-site change.
                { "sys",        sysCode    ?? dt?.System ?? string.Empty },
                // K-7: {lvl} was the one ISO segment with no profile fallback —
                // vol/type/role/suit/rev all read IsoNaming, {lvl} read the
                // caller or nothing. A producer with no Level in context
                // therefore emitted an empty segment into the sheet number and
                // said nothing. Fall back to IsoNaming.Level like its siblings.
                { "lvl",        levelCode  ?? dt?.IsoNaming?.Level ?? string.Empty },
                { "mark",       mark       ?? string.Empty },
                { "purpose",    dt?.Purpose ?? string.Empty },
                { "phase",      dt?.Phase   ?? string.Empty },
                // K-13: {project} and {originator} are NOT seeded here. They are the
                // only two tokens with no fallback of any kind, and they are added
                // below only when their source parameter actually holds a value —
                // see the block after this initialiser.
                // ISO 19650 fields with profile fallback to discipline.
                { "vol",        dt?.IsoNaming?.Volume      ?? string.Empty },
                { "type",       dt?.IsoNaming?.Type        ?? string.Empty },
                { "role",       dt?.IsoNaming?.Role        ?? discCode ?? dt?.Discipline ?? string.Empty },
                { "suit",       dt?.IsoNaming?.Suitability ?? string.Empty },
                { "rev",        dt?.IsoNaming?.Revision    ?? string.Empty },
            };
            // GAP-D: only emit "seq" when the caller actually has a value.
            // The applier's TryGetValue(...) miss path leaves the literal
            // "{seq:Dn}" untouched so a downstream stage (sheet renumber,
            // package sequencer) can fill it later — better than silently
            // rendering "Sheet Number A--" because the upstream had no seq.
            if (seq.HasValue)
                d["seq"] = seq.Value.ToString("D" + Math.Max(1, seqWidth));

            // K-13: {project} and {originator} follow the SAME rule as {seq} — omit
            // rather than blank.
            //
            // These two read a shared parameter on ProjectInformation and have no
            // profile fallback. Seeded with an empty string they produced a sheet
            // number that LOOKS deliberate: "{project}-{originator}-{vol}-…" renders
            // "-PLN-COT01-GF-DR-A-1001", and nothing distinguishes a dropped leading
            // segment from a number the author meant to write.
            //
            // Omitting them instead leaves the literal "{project}" in the string.
            // Revit rejects braces in a sheet number outright, so the assignment
            // throws and the sheet keeps its default number — visibly wrong, and
            // impossible to issue by accident.
            //
            // NOT substituted with "XX" or any placeholder: a fabricated project code
            // produces a sheet that looks correct and is not, which is worse than one
            // that will not save. Same principle as G-5 (report null, never 0) and the
            // path resolver refusing rather than defaulting to PRJ.
            foreach (var kv in TokenSourceParam)
            {
                string v = ReadProjectInfo(doc, kv.Value);
                if (!string.IsNullOrWhiteSpace(v)) d[kv.Key] = v;
            }
            return d;
        }

        // THE RULE FOR EVERY TOKEN, not just the one that prompted it.
        //
        // K-7 wrote this up for {lvl} and never asked whether the other fifteen
        // shared the hole. K-11 audited all sixteen. They do not all share it, and
        // the reason they do not is the rule:
        //
        //   A token must have EITHER a fallback that is itself authored somewhere a
        //   human can see and fix, OR no entry at all — never a blank entry.
        //
        //   * lvl, vol, type, role, suit, rev, sys, disc, discipline
        //       fall back to the DrawingType profile (IsoNaming.*, .System,
        //       .Discipline). The profile is a JSON file an author edits, so a blank
        //       there is visible and attributable. Safe.
        //   * seq, project, originator
        //       have no such backstop, so they are OMITTED when unknown. The literal
        //       survives, Revit rejects the braces, and the sheet number fails to
        //       save. Loud. ({seq} by GAP-D, {project}/{originator} by K-13.)
        //   * purpose, phase, spool, mark
        //       are not ISO name segments — they feed title-block cells, where a
        //       blank is a blank cell, not a corrupted identifier. Safe to blank.
        //
        // So: if you add a token, decide which of the three groups it is in. A new
        // ISO-segment token with a blank default is the defect this comment exists
        // to prevent.
        //
        // The two silent failure modes, both of which reach an issued sheet without
        // a word being logged:
        //
        //   1. The key resolves to an EMPTY string. The applier substitutes it
        //      happily and the segment vanishes, so
        //      "{project}-{originator}-{vol}-{lvl}-{type}-{role}-{seq:D4}"
        //      renders "KBL26-PLN-COT01--DR-A-1001" — a double separator where
        //      the level should be. Nothing distinguishes that from a sheet
        //      number the author meant to write.
        //
        //   2. The key is ABSENT from the dict. TitleBlockParamApplier's
        //      TryGetValue miss path deliberately leaves the literal "{seq:D4}"
        //      in place so a later stage can fill it (GAP-D). That is the right
        //      behaviour and is not changed here — but if no later stage runs,
        //      braces end up on the sheet, and Revit rejects braces in a sheet
        //      number outright, so the assignment throws and the sheet silently
        //      keeps its default number.
        //
        // Neither case is repaired here — repairing them would override a
        // caller's deliberate blank. They are only made *audible*, so the
        // producer's existing Warnings list carries them to the operator.
        /// <summary>
        /// Tokens whose value comes from a shared parameter on ProjectInformation
        /// rather than from the caller or the DrawingType profile. These are the only
        /// two in the whole token set with NO fallback of any kind — every other token
        /// either falls back to the profile (lvl/vol/type/role/suit/rev/sys/disc) or is
        /// deliberately omitted so the literal survives ({seq}, GAP-D).
        /// </summary>
        private static readonly Dictionary<string, string> TokenSourceParam =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "project",    "PRJ_PROJECT_COD_TXT" },
                { "originator", "PRJ_ORG_ORIGINATOR_CODE_TXT" },
            };

        private static readonly System.Text.RegularExpressions.Regex _tokenRx =
            new System.Text.RegularExpressions.Regex(@"\{([A-Za-z0-9_]+)(?::D(\d+))?\}",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Report every token in <paramref name="pattern"/> that will render
        /// empty or survive as a literal brace against <paramref name="tokens"/>.
        /// Returns an empty list when the pattern is fully satisfied.
        /// <paramref name="label"/> names the pattern in the message
        /// ("Sheet number", "Sheet name").
        /// </summary>
        public static List<string> AuditPattern(
            string pattern, IDictionary<string, string> tokens, string label)
        {
            var warnings = new List<string>();
            if (string.IsNullOrEmpty(pattern) || tokens == null) return warnings;

            var blank   = new List<string>();
            var missing = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in _tokenRx.Matches(pattern))
            {
                var key = m.Groups[1].Value;
                // ${PRJ_…} project-info lookups and literal passthroughs are the
                // applier's business, not this dict's — only audit keys the
                // canonical builder is responsible for.
                if (!tokens.TryGetValue(key, out var val))
                {
                    if (!missing.Contains(m.Value)) missing.Add(m.Value);
                }
                else if (string.IsNullOrEmpty(val))
                {
                    if (!blank.Contains(key)) blank.Add(key);
                }
            }

            if (blank.Count > 0)
            {
                // K-11 2.3: name the SOURCE, not just the token. The generic advice
                // below ("set the profile default") is wrong for the two tokens backed
                // by a shared parameter rather than the profile — an operator told to
                // edit IsoNaming for {project} will not find it there. These are also
                // the two that had no fallback of any kind, so they are the ones most
                // likely to appear in this list.
                var sourced = new List<string>();
                foreach (var k in blank)
                    if (TokenSourceParam.TryGetValue(k, out var src))
                        sourced.Add($"{{{k}}} <- {src} on Project Information");

                warnings.Add(
                    $"{label} pattern '{pattern}': token(s) {{{string.Join("}, {", blank)}}} resolved empty — "
                  + "the segment is dropped, leaving a doubled separator. Supply the value at the call "
                  + "site, or set the profile default (IsoNaming.Level / .Volume / .Type / .Role)."
                  + (sourced.Count > 0
                        ? " Parameter-backed token(s): " + string.Join("; ", sourced)
                          + ". If the parameter is not visible in Manage > Project Information, it is not"
                          + " bound — run Load Shared Parameters and re-open."
                        : string.Empty));
            }
            if (missing.Count > 0)
            {
                // K-13: name the source for the parameter-backed tokens, so the log
                // explains the rejection Revit is about to produce rather than just
                // reporting it.
                var sourcedMissing = new List<string>();
                foreach (var m in missing)
                {
                    var bare = m.Trim('{', '}').Split(':')[0];
                    if (TokenSourceParam.TryGetValue(bare, out var src))
                        sourcedMissing.Add($"{m} <- {src} on Project Information is empty or unbound");
                }

                warnings.Add(
                    $"{label} pattern '{pattern}': token(s) {string.Join(", ", missing)} were not supplied and "
                  + "remain literal. Revit rejects braces in a sheet number, so the value will not be written "
                  + "unless a later stage fills them."
                  + (sourcedMissing.Count > 0
                        ? " " + string.Join("; ", sourcedMissing)
                          + ". This is deliberate: the token is omitted rather than blanked so the sheet "
                          + "number fails to save instead of silently losing a segment. Set the parameter "
                          + "in Manage > Project Information; if it is not listed there it is not bound, "
                          + "so run Load Shared Parameters and re-open."
                        : string.Empty));
            }
            return warnings;
        }

        /// <summary>
        /// SheetManager fallback: extract {seq} from the sheet-number's
        /// trailing digit run when no explicit seq is known. Called by
        /// <see cref="StingTools.Docs.DrawingTypeSheetAdapter"/> so the
        /// "Create From Template" path produces stable tokens even when
        /// the user picks a profile without an existing sequence counter.
        /// </summary>
        public static int? ExtractSeqFromSheetNumber(string sheetNumber)
        {
            if (string.IsNullOrEmpty(sheetNumber)) return null;
            string seq = string.Empty;
            for (int i = sheetNumber.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(sheetNumber[i])) seq = sheetNumber[i] + seq;
                else if (seq.Length > 0) break;
            }
            if (int.TryParse(seq, out var n)) return n;
            return null;
        }

        private static string ReadProjectInfo(Document doc, string paramName)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return string.Empty;
                var p = pi.LookupParameter(paramName);
                if (p == null) return string.Empty;
                switch (p.StorageType)
                {
                    case StorageType.String:  return p.AsString() ?? string.Empty;
                    case StorageType.Integer: return p.AsInteger().ToString();
                    case StorageType.Double:  return p.AsDouble().ToString("0.###");
                    default:                  return p.AsValueString() ?? string.Empty;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return string.Empty; }
        }
    }
}
