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
                { "sys",        sysCode    ?? string.Empty },
                { "lvl",        levelCode  ?? string.Empty },
                { "mark",       mark       ?? string.Empty },
                { "purpose",    dt?.Purpose ?? string.Empty },
                { "phase",      dt?.Phase   ?? string.Empty },
                { "project",    ReadProjectInfo(doc, "PRJ_ORG_PROJECT_CODE") },
                { "originator", ReadProjectInfo(doc, "PRJ_ORG_ORIGINATOR_CODE") },
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
            catch { return string.Empty; }
        }
    }
}
