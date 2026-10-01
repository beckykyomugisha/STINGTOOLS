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

        // ── DTW-220: the run-level gate notes, it never stops the run ──
        //
        // The run used to borrow Project Information before its first item and stop when a
        // colleague held it — even a re-run where every item reuses its sheet and no number
        // is ever reserved — and then kept Project Information borrowed until sync,
        // blocking the next colleague. A new sheet number is checked where it is needed
        // (DrawingProducer, SheetSequenceStore.WriteBlockReason), so the run-level gate
        // only says, once, that items needing a new sheet will be refused.

        internal enum CountersGate { Proceed, NoteNewSheetsSkipped }

        /// <summary>
        /// What the run does about the sheet-number counters, from their status read
        /// without borrowing: <paramref name="ownerIfOther"/> is the colleague holding
        /// Project Information (null when free or mine); <paramref name="outOfDate"/> when it
        /// changed in central since the last reload. Never stops the run.
        /// </summary>
        internal static CountersGate SheetCountersGate(bool workshared, string ownerIfOther, bool outOfDate)
            => workshared && (!string.IsNullOrWhiteSpace(ownerIfOther) || outOfDate)
                ? CountersGate.NoteNewSheetsSkipped
                : CountersGate.Proceed;

        /// <summary>The run-level note when new sheet numbers cannot be reserved; null when they can.</summary>
        internal static string CountersNote(bool workshared, string ownerIfOther, bool outOfDate)
        {
            if (SheetCountersGate(workshared, ownerIfOther, outOfDate) == CountersGate.Proceed) return null;
            var why = new List<string>();
            if (!string.IsNullOrWhiteSpace(ownerIfOther))
                why.Add($"Project Information is owned by {ownerIfOther.Trim()} — ask them to synchronise and relinquish");
            if (outOfDate)
                why.Add("Project Information has changed in the central model — reload latest");
            return "New sheet numbers cannot be reserved: " + string.Join("; ", why) + ". "
                 + "Items that reuse their existing sheet are produced; items needing a new sheet will be skipped.";
        }

        /// <summary>The per-item failure when a sheet cannot be numbered.</summary>
        internal static string SheetNotNumberedLine(string drawingTypeId, string reason)
            => $"'{drawingTypeId}': no sheet was made — its number could not be reserved "
             + $"({(string.IsNullOrWhiteSpace(reason) ? "reason unknown" : reason.Trim())}). Nothing of it was produced.";

        // ── DTW-216: every caller rolls a refused item back ────────────

        /// <summary>
        /// An item's notes into <paramref name="into"/>, and its failure handed back (null
        /// when the item can be kept). The failure line is left out of the notes: the
        /// caller rolls the item back and reports it once, as the reason.
        /// </summary>
        internal static string TakeItem(IEnumerable<string> notes, string failure, List<string> into)
        {
            if (into != null && notes != null)
                into.AddRange(failure == null ? notes : notes.Where(w => w != failure));
            return failure;
        }

        /// <summary>The report line for an item rolled back because production refused it.</summary>
        internal static string RolledBackLine(string label, string failure)
            => $"{label}: {(string.IsNullOrWhiteSpace(failure) ? "production refused the item" : failure.Trim())} — rolled back; nothing of it was kept.";

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

        // ── DTW-215: Sync Styles honours the same fit ──────────────────

        /// <summary>
        /// The scale Sync Styles / Force Resync keeps for a view, or 0 to apply the drawing
        /// type's — the same rule as a production refresh (<see cref="ScaleOnRefresh"/>).
        /// <paramref name="dropFit"/> is true when a fit is recorded but the type's scale has
        /// changed since: the type's scale is applied and the record, which no longer
        /// describes the view, is dropped (the next production run fits it again).
        /// </summary>
        internal static int ScaleOnResync(int typeScale, int fittedScale, int fitBaseScale, out bool dropFit)
        {
            int keep = ScaleOnRefresh(typeScale, fittedScale, fitBaseScale);
            dropFit = fittedScale > 0 && keep == 0;
            return keep;
        }

        /// <summary>
        /// The scale the drift check expects of a view: its fitted scale while that fit is
        /// current, else the type's. Without it every fitted view read as SCALE drift, and
        /// Sync Styles listed it on every run.
        /// </summary>
        internal static int ExpectedScale(int typeScale, int fittedScale, int fitBaseScale)
        {
            int keep = ScaleOnRefresh(typeScale, fittedScale, fitBaseScale);
            return keep > 0 ? keep : typeScale;
        }

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

        // ── DTW-206: an area view its box cannot crop is not produced ───

        /// <summary>
        /// Null when the scope box's vertical extent reaches the level (feet, model
        /// coordinates); otherwise why the plan cannot be cropped to it. Revit refuses a
        /// box that misses the view's level as the crop, and the view stayed a whole-floor
        /// plan on an area-named sheet with only a warning.
        /// </summary>
        internal static string BoxMissesLevel(string boxName, string levelName, double zMinFt, double zMaxFt, double levelFt)
        {
            const double tol = 1e-3;
            if (levelFt >= zMinFt - tol && levelFt <= zMaxFt + tol) return null;
            return $"scope box '{boxName}' does not reach level '{levelName}' (box {zMinFt * 0.3048:0.###}–{zMaxFt * 0.3048:0.###} m, "
                 + $"level {levelFt * 0.3048:0.###} m), so its plan cannot be cropped to it — extend the box to the level. Nothing of it was produced.";
        }

        /// <summary>The failure line when a produced area plan did not take its box as crop.</summary>
        internal static string NotCroppedLine(string viewName, string boxName)
            => $"'{viewName}' could not be cropped to scope box '{boxName}' (the box was not accepted as its crop); "
             + "an uncropped whole-floor view is not kept for an area sheet.";

        // ── DTW-208: a produced view's phase filter ────────────────────

        /// <summary>Revit's own phase-filter names (English). A localised project that
        /// names them differently gets no default, only the pack's explicit filter.</summary>
        internal const string ShowAll = "Show All", ShowComplete = "Show Complete";

        /// <summary>
        /// The phase filter a NEW produced view should take, or null to leave it. The view
        /// style pack's <c>phaseFilter</c> wins when the project has it; otherwise a view on
        /// Revit's "Show All" (demolished elements shown, and tagged by the annotation pass)
        /// moves to "Show Complete". Never when the view's template controls the filter
        /// (<paramref name="templateControls"/>) — the template is the project's choice.
        /// </summary>
        internal static string ProductionPhaseFilter(string packPhaseFilter, string currentFilter, bool templateControls,
            IEnumerable<string> available)
        {
            if (templateControls) return null;
            var names = new HashSet<string>(available ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            string pick = null;
            if (!string.IsNullOrWhiteSpace(packPhaseFilter) && names.Contains(packPhaseFilter.Trim()))
                pick = names.First(n => string.Equals(n, packPhaseFilter.Trim(), StringComparison.OrdinalIgnoreCase));
            else if (string.Equals(currentFilter, ShowAll, StringComparison.OrdinalIgnoreCase) && names.Contains(ShowComplete))
                pick = names.First(n => string.Equals(n, ShowComplete, StringComparison.OrdinalIgnoreCase));
            return pick != null && !string.Equals(pick, currentFilter, StringComparison.OrdinalIgnoreCase) ? pick : null;
        }

        // ── DTW-209: a reused sheet's name follows a level rename ──────

        /// <summary>
        /// The new name for a REUSED sheet, or null to leave it. Production refreshes the
        /// name only when it still is the one production gave: equal to the stored
        /// generated name, or — for a sheet produced before that was stored — equal to the
        /// pattern resolved with the level name in its old context stamp. A name edited by
        /// hand, or a style-locked sheet, is left alone.
        /// </summary>
        internal static string ReusedSheetRename(string currentName, string storedGenerated, string legacyExpected,
            string expectedNow, bool locked)
        {
            if (locked || string.IsNullOrWhiteSpace(expectedNow)) return null;
            if (string.Equals(currentName, expectedNow, StringComparison.Ordinal)) return null;
            bool generated = !string.IsNullOrEmpty(storedGenerated)
                ? string.Equals(currentName, storedGenerated, StringComparison.Ordinal)
                : legacyExpected != null && string.Equals(currentName, legacyExpected, StringComparison.Ordinal);
            return generated ? expectedNow : null;
        }

        // ── DTW-199: a view moved to another sheet stays there ─────────

        /// <summary>The report line for a view kept on the sheet someone moved it to.</summary>
        internal static string KeptOnOtherSheetLine(string viewName, string otherSheet, string thisSheet)
            => $"'{viewName}' is kept on sheet {otherSheet}, where it was moved; it was not placed on {thisSheet} "
             + "and its scale was left alone.";

        // ── DTW-224: the style elements an item edits are pre-checked ──
        //
        // Besides its stamped views and sheets, an item edits its pack's managed templates
        // (STING:{packId}:{ViewType}, ManagedTemplateSyncer) and the pack's filters (rebuilt
        // in place on drift, DTW-167). One owned by a colleague failed every item using the
        // pack at commit; it is now a skip with the reason.

        /// <summary>
        /// The Revit ViewType names whose managed template an item with these production
        /// rule view types can touch, or null when they are not all known — then every
        /// template of the pack is checked. A FloorPlan rule may make a structural
        /// (EngineeringPlan) or area plan, so those are included.
        /// </summary>
        internal static HashSet<string> ManagedTemplateViewTypes(IEnumerable<string> ruleViewTypes)
        {
            var list = ruleViewTypes?.ToList();
            if (list == null || list.Count == 0) return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in list)
            {
                switch ((raw ?? "").Trim())
                {
                    case "FloorPlan": set.Add("FloorPlan"); set.Add("EngineeringPlan"); set.Add("AreaPlan"); break;
                    case "RCP":
                    case "CeilingPlan": set.Add("CeilingPlan"); break;
                    case "Section": set.Add("Section"); break;
                    case "Detail": set.Add("Detail"); set.Add("Section"); break;
                    case "Elevation": set.Add("Elevation"); break;
                    case "ThreeD": set.Add("ThreeD"); break;
                    case "DraftingView": set.Add("DraftingView"); break;
                    case "Schedule": set.Add("Schedule"); break;
                    default: return null;
                }
            }
            return set;
        }

        /// <summary>
        /// Why an item is skipped for its style elements — "style pack template/filter owned
        /// by X ('…')", "… not up to date … reload latest" — or null when none blocks it.
        /// </summary>
        internal static string StylePackBlockReason(IEnumerable<KeyValuePair<string, string>> ownedByOthers,
            IEnumerable<string> outOfDate)
        {
            var why = ProductionRunReport.BlockReason(ownedByOthers, outOfDate, null);
            return why == null ? null : "style pack template/filter " + why;
        }

        /// <summary>The name of the filter a pack's byMaterialClass entry is drawn through
        /// (ViewStylePackApplier.EnsureMaterialClassFilter creates it under this name).</summary>
        // DT-R11-C: a material class is user text, and Revit refuses a filter name holding
        // \ : { } [ ] | ; < > ? ` ~ — one rule for the name, RevitNameRules.
        internal static string MaterialClassFilterName(string className) => RevitNameRules.Sanitize("STING_MAT_CLASS_" + className);

        /// <summary>
        /// DTW-227: the names of the filters a pack's filter pass edits — its named filters
        /// and the filters of its byMaterialClass entries — trimmed, blanks dropped, each once.
        /// The worksharing pre-check resolves these read-only.
        /// </summary>
        internal static List<string> PackFilterNames(IEnumerable<string> filterNames, IEnumerable<string> materialClasses)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // Revit filter names: case-insensitive
            if (filterNames != null)
                foreach (var n in filterNames)
                    // DT-R11-C: the Revit name (sanitised) first, then the name as written,
                    // so the filter is found whichever spelling the model holds.
                    foreach (var c in RevitNameRules.Candidates(n))
                        if (seen.Add(c)) result.Add(c);
            if (materialClasses != null)
                foreach (var c in materialClasses)
                {
                    // The class is used as the applier uses it (untrimmed), so the name matches
                    // the filter it creates.
                    if (string.IsNullOrWhiteSpace(c)) continue;
                    var name = MaterialClassFilterName(c);
                    if (seen.Add(name)) result.Add(name);
                }
            return result;
        }

        /// <summary>
        /// DTW-228: the filters on a view that a pack with <c>filterEnabled: false</c> may
        /// disable — those whose name is one of the pack's own filters
        /// (<see cref="PackFilterNames"/>). Every other filter on the view (MEP system
        /// colours, Visibility Centre filters, the user's own) is left as it is; the pack
        /// used to disable all of them. Names compare as Revit compares filter names:
        /// trimmed, case-insensitive.
        /// </summary>
        internal static List<T> PackFiltersOnView<T>(IEnumerable<KeyValuePair<T, string>> viewFilters,
            IEnumerable<string> packFilterNames)
        {
            var result = new List<T>();
            if (viewFilters == null || packFilterNames == null) return result;
            var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in packFilterNames)
                if (!string.IsNullOrWhiteSpace(n)) own.Add(n.Trim());
            if (own.Count == 0) return result;
            foreach (var kv in viewFilters)
                if (!string.IsNullOrWhiteSpace(kv.Value) && own.Contains(kv.Value.Trim())) result.Add(kv.Key);
            return result;
        }
    }
}
