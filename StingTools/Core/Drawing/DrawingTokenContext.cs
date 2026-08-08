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
                { "project",    ReadProjectInfo(doc, "PRJ_PROJECT_COD_TXT") },
                { "originator", ReadProjectInfo(doc, "PRJ_ORG_ORIGINATOR_CODE_TXT") },
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
            return d;
        }

        // K-7: a pattern token can fail in two silent ways, both of which
        // reach an issued sheet without a word being logged:
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
                warnings.Add(
                    $"{label} pattern '{pattern}': token(s) {{{string.Join("}, {", blank)}}} resolved empty — "
                  + "the segment is dropped, leaving a doubled separator. Supply the value at the call "
                  + "site, or set the profile default (IsoNaming.Level / .Volume / .Type / .Role).");
            if (missing.Count > 0)
                warnings.Add(
                    $"{label} pattern '{pattern}': token(s) {string.Join(", ", missing)} were not supplied and "
                  + "remain literal. Revit rejects braces in a sheet number, so the value will not be written "
                  + "unless a later stage fills them.");
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
