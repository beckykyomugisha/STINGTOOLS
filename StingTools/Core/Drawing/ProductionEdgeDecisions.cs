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
    }
}
