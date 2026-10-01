// StingTools — Drawing Template Manager · edge-case decisions of the producer (DTW-194..213)
//
// The producer's edge cases are decided here, away from the Revit calls that feed them,
// so each rule is unit-tested rather than found on an issued drawing. DrawingProducer,
// ProductionItemRunner and the Drawing Doctor read the model and hand over plain values;
// this file says what to do with them.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    internal static class ProductionEdgeDecisions
    {
        // ── DTW-194: the sheet-number counters ─────────────────────────

        /// <summary>
        /// The line a production run stops with when the sheet-number counters (Extensible
        /// Storage on Project Information) cannot be written. Numbering from a guess instead
        /// gave two users the same numbers, so nothing is produced.
        /// </summary>
        internal static string CountersBlockedLine(string reason)
            => "Run stopped before any drawing was produced: the sheet-number counters on Project Information "
             + $"cannot be written — {(string.IsNullOrWhiteSpace(reason) ? "reason unknown" : reason.Trim())}. "
             + "Sheets are never numbered from a guess; fix this and run again.";

        /// <summary>The per-item failure when a sheet cannot be numbered.</summary>
        internal static string SheetNotNumberedLine(string drawingTypeId, string reason)
            => $"'{drawingTypeId}': no sheet was made — its number could not be reserved "
             + $"({(string.IsNullOrWhiteSpace(reason) ? "reason unknown" : reason.Trim())}). Nothing of it was produced.";

        // ── DTW-197: the sheet is made after the first view ────────────

        /// <summary>
        /// Make the request's new sheet now? Only when a sheet was asked for, none exists
        /// for the request, none has been tried yet, and a view has just been produced. The
        /// sheet used to come first, so a request whose every rule failed left an empty,
        /// numbered sheet counted as produced.
        /// </summary>
        internal static bool CreateSheetNow(bool createSheetRequested, bool sheetKnown, bool sheetAttempted, bool viewProduced)
            => createSheetRequested && !sheetKnown && !sheetAttempted && viewProduced;

        /// <summary>
        /// Remove a sheet this request made? When it was meant to carry the views and ends
        /// with none on it — every placement failed, or each view is kept on another sheet.
        /// A sheet asked for without placement (views placed later) is left alone.
        /// </summary>
        internal static bool DiscardNewSheet(bool createdThisRequest, bool placeOnSheet, int placed, int reused)
            => createdThisRequest && placeOnSheet && placed <= 0 && reused <= 0;

        // ── DTW-196: a re-run keeps a fitted scale, reports a template swap ──

        /// <summary>
        /// The scale a re-run keeps for a view production fitted to its slot, or 0 to apply
        /// the drawing type's. Kept while the type's scale is still the one the fit started
        /// from; after the type's scale changes the view takes the new scale and is fitted
        /// again (<see cref="RefitOnRerun"/>).
        /// </summary>
        internal static int ScaleOnRefresh(int typeScale, int fittedScale, int fitBaseScale)
            => fittedScale > 0 && fitBaseScale == typeScale ? fittedScale : 0;

        /// <summary>
        /// Fit a view that is already on its sheet again? When nothing pins its scale and
        /// either no fit was recorded (a view produced before the record existed) or the
        /// type's scale has changed since.
        /// </summary>
        internal static bool RefitOnRerun(bool scalePinned, bool fitRecorded, int fitBaseScale, int typeScale)
            => !scalePinned && (!fitRecorded || fitBaseScale != typeScale);

        /// <summary>The report line for a view template a re-run replaced.</summary>
        internal static string TemplateReplacedLine(string viewName, string oldTemplate, string newTemplate, string drawingTypeId)
            => $"'{viewName}': view template '{(string.IsNullOrWhiteSpace(oldTemplate) ? "(unnamed)" : oldTemplate)}' was replaced by "
             + $"{(string.IsNullOrWhiteSpace(newTemplate) ? "none" : "'" + newTemplate + "'")} (drawing type '{drawingTypeId}'). "
             + "Lock the view's style to keep a template chosen by hand.";

        // ── DTW-198: names take the full level name, numbers a distinct short one ──

        /// <summary>
        /// {lvl} for a sheet NUMBER. An ISO-shaped pattern takes the level's ISO code when
        /// one is known; any other pattern takes <see cref="SheetNumberEngine.ShortLevel"/>
        /// of the name, which keeps a trailing number — "Basement 1" and "Basement 2" both
        /// became "Basement" under the plain eight-character cut. No level: the fallback
        /// (the profile's isoNaming level). Numbers stay on names, not level codes:
        /// moving them would renumber every existing profile sheet (decision DTW-198).
        /// </summary>
        internal static string NumberLevel(string pattern, string levelName, string isoCode, string fallback)
        {
            if (levelName == null) return fallback ?? "";
            if (SheetNumberPolicy.IsAlreadyIso(pattern) && !string.IsNullOrEmpty(isoCode)) return isoCode;
            return SheetNumberEngine.ShortLevel(levelName);
        }

        /// <summary>
        /// A produced sheet's NAME: the pattern with the full level name, mark and system
        /// (<see cref="SheetNumberEngine.ApplyNamePattern"/>), plus — DTW-51 — the area when
        /// the sheet is for a scope box and the pattern does not already name it.
        /// </summary>
        internal static string SheetName(string pattern, string disc, string levelName, string sys, string tag,
            string purpose, int seq, IDictionary<string, string> extras, string areaName)
        {
            var name = SheetNumberEngine.ApplyNamePattern(pattern, disc, levelName, sys, tag ?? "", tag ?? "", purpose, seq, extras);
            if (!string.IsNullOrWhiteSpace(areaName))
            {
                var p = pattern ?? "";
                bool namesArea = p.IndexOf("{mark}", StringComparison.OrdinalIgnoreCase) >= 0
                              || p.IndexOf("{spool}", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!namesArea && (name ?? "").IndexOf(areaName, StringComparison.OrdinalIgnoreCase) < 0)
                    name = $"{name} - {areaName}";
            }
            return name;
        }

        // ── DTW-213: a name is held by the element that took it, not by its id ──

        /// <summary>
        /// Does the element at the recorded owner id still hold <paramref name="key"/>?
        /// Only when it exists, is the kind that took the name (a sheet for a sheet number,
        /// a view for a view name) and still carries that name. After a rollback Revit can
        /// reuse an ElementId for a different element; judged by id alone, it kept the
        /// rolled-back sheet's number taken (a needless "-A") or its context claim.
        /// <paramref name="ownerUnknown"/> (no owner recorded) always holds.
        /// </summary>
        internal static bool StillHolds(bool ownerUnknown, bool exists, bool expectedKind, string currentName,
            string key, bool ignoreCase)
        {
            if (ownerUnknown) return true;
            if (!exists || !expectedKind) return false;
            return string.Equals(currentName ?? "", key ?? "",
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        // ── DTW-203: a renamed drawing type adopts what its old id made ──

        /// <summary>
        /// The ids whose views and sheets this request adopts: the type's own
        /// <c>replaces</c> list plus the caller's former ids, without blanks, duplicates or
        /// the type's current id.
        /// </summary>
        internal static IReadOnlyList<string> FormerIds(DrawingType dt, IEnumerable<string> callerFormer)
            => (dt?.Replaces ?? Enumerable.Empty<string>())
                .Concat(callerFormer ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Where(id => !string.Equals(id, dt?.Id, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// For a drawing-type id stamped on a view or sheet: null when the catalogue knows
        /// it; otherwise the id of the type whose <c>replaces</c> names it, or "" when none
        /// does (an orphan — its type was renamed or removed with nothing taking it over).
        /// </summary>
        internal static string ReplacementFor(string stampedId, IEnumerable<DrawingType> catalogue)
        {
            if (string.IsNullOrWhiteSpace(stampedId)) return null;
            var list = (catalogue ?? Enumerable.Empty<DrawingType>()).Where(t => t != null).ToList();
            if (list.Any(t => string.Equals(t.Id, stampedId, StringComparison.OrdinalIgnoreCase))) return null;
            var by = list.FirstOrDefault(t => t.Replaces != null
                && t.Replaces.Any(r => string.Equals(r?.Trim(), stampedId.Trim(), StringComparison.OrdinalIgnoreCase)));
            return by?.Id ?? "";
        }

        // ── DTW-199: a view moved to another sheet stays there ─────────

        /// <summary>The report line for a view kept on the sheet someone moved it to.</summary>
        internal static string KeptOnOtherSheetLine(string viewName, string otherSheet, string thisSheet)
            => $"'{viewName}' is kept on sheet {otherSheet}, where it was moved; it was not placed on {thisSheet} "
             + "and its scale was left alone.";
    }
}
