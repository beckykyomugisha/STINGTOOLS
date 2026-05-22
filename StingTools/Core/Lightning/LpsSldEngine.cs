// LpsSldEngine — graph-aware Single Line Diagram renderer for the
// Lightning Protection System. Walks the LPS topology in the active
// document (air terminals → down conductors → main earth bar → earth
// electrodes; SPDs branching off the MEB), lays the nodes out in a
// canonical 4-tier vertical pattern, and renders boxes + connecting
// lines + labels into a Drafting view.
//
// This is the engine Wave 4's LpsSldOverlayCommand stubbed for —
// instead of just dropping star markers on a user-provided SLD,
// LpsSldGenerateCommand calls this engine to build a complete
// LPS-only single-line from scratch. Idempotent: re-running clears
// the previous content of the view and re-renders.
//
// Layout (all coordinates in feet; converted from mm for clarity):
//
//      ROW 1 — AT band     y = +50  ★ AT-1  ★ AT-2  ★ AT-3 …
//                                   │       │       │
//      ROW 2 — DC midpoint y =  +20 │ DC-1  │ DC-2  │ DC-3
//                                   │       │       │
//      ROW 3 — MEB         y =    0 ━━━━━━━━━━━━━━━━ (Main Earth Bar)
//                                   │       │       │
//      ROW 4 — Earth       y =  -25 ⏚ EE-1  ⏚ EE-2  ⏚ EE-3
//
//      ROW 5 — SPDs        y =  +5  ★ T1 SPD ★ T2 SPD …  (branched left of MEB)

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Storage;

namespace StingTools.Core.Lightning
{
    public static class LpsSldEngine
    {
        public const string DefaultViewName = "STING - LPS Single Line";

        // Layout constants (feet)
        private const double Y_AT     =  50.0;
        private const double Y_DC_MID =  20.0;
        private const double Y_SPD    =   5.0;
        private const double Y_MEB    =   0.0;
        private const double Y_EE     = -25.0;
        private const double COL_W    =   6.0;   // horizontal pitch between vertical legs
        private const double BOX_W    =   4.0;
        private const double BOX_H    =   2.0;
        private const double MEB_PAD  =   2.0;   // padding at MEB ends

        public class BuildResult
        {
            public ViewDrafting View { get; set; }
            public int AirTerminals { get; set; }
            public int DownConductors { get; set; }
            public int EarthElectrodes { get; set; }
            public int SurgeProtectors { get; set; }
            public int LinesDrawn { get; set; }
            public string Notes { get; set; } = "";
        }

        /// <summary>
        /// Build / refresh the LPS SLD drafting view. Caller must wrap
        /// in a Transaction. Returns counts + notes via BuildResult.
        /// </summary>
        public static BuildResult Build(Document doc, string viewName = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            var result = new BuildResult();

            // ── 1. Collect components ────────────────────────────────
            var ats   = LpsEngine.CollectLpsFamily(doc, "Air Terminal", "Air_Terminal", "Franklin", "Air-Terminal", "AT");
            var dcs   = LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor");
            var ees   = LpsEngine.CollectLpsFamily(doc, "Earth", "Ground Rod", "GroundRod", "Earth_Rod", "Earth Electrode");
            var spds  = LpsEngine.CollectLpsFamily(doc, "SPD", "Surge");

            result.AirTerminals    = ats.Count;
            result.DownConductors  = dcs.Count;
            result.EarthElectrodes = ees.Count;
            result.SurgeProtectors = spds.Count;

            if (ats.Count + dcs.Count + ees.Count + spds.Count == 0)
            {
                result.Notes = "No LPS components in the model. Place air terminals / down conductors / earth electrodes first.";
                return result;
            }

            // ── 2. Resolve / create the drafting view ────────────────
            string name = string.IsNullOrEmpty(viewName) ? DefaultViewName : viewName;
            var view = new FilteredElementCollector(doc).OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            if (view == null)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                if (vft == null) throw new InvalidOperationException("No Drafting view family type available.");
                view = ViewDrafting.Create(doc, vft.Id);
                try { view.Name = name; } catch (Exception ex) { StingTools.Core.StingLog.Warn($"Rename: {ex.Message}"); }
            }
            result.View = view;

            // Clear any previous content this engine deposited.
            // Wave A #1 — scope deletes to engine-owned elements via the
            // StingLpsSldStampSchema ES marker so user annotations on the
            // same view survive a rebuild.
            try
            {
                var allDetailLines  = new FilteredElementCollector(doc, view.Id).OfClass(typeof(DetailLine)).ToElements();
                var allTextNotes    = new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).ToElements();
                var allFilledRegs   = new FilteredElementCollector(doc, view.Id).OfClass(typeof(FilledRegion)).ToElements();
                var purge = new List<ElementId>();
                foreach (var el in allDetailLines.Concat(allTextNotes).Concat(allFilledRegs))
                    if (StingLpsSldStampSchema.IsOwned(el)) purge.Add(el.Id);
                if (purge.Count > 0) doc.Delete(purge);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SldEngine purge: {ex.Message}"); }

            // ── 3. Pick a TextNoteType ───────────────────────────────
            var textType = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>().FirstOrDefault();
            if (textType == null) { result.Notes = "No TextNoteType in document."; return result; }

            // ── 4. Layout positions ──────────────────────────────────
            // Use the *count of down conductors* as the column count
            // (since each DC defines one vertical leg). Air terminals
            // hang off DCs proportionally; if AT count != DC count we
            // bunch ATs evenly across the same span.
            int columns = Math.Max(1, Math.Max(dcs.Count, ats.Count));
            double spanWidth = (columns - 1) * COL_W;
            double xStart = -spanWidth / 2.0;

            var dcX = new List<double>();
            for (int i = 0; i < columns; i++)
                dcX.Add(xStart + i * COL_W);

            // ── 5. Draw the main earth bar (MEB) ─────────────────────
            double mebX0 = dcX.First() - MEB_PAD;
            double mebX1 = dcX.Last()  + MEB_PAD;
            DrawLine(doc, view, new XYZ(mebX0, Y_MEB, 0), new XYZ(mebX1, Y_MEB, 0));
            DrawLine(doc, view, new XYZ(mebX0, Y_MEB - 0.4, 0), new XYZ(mebX1, Y_MEB - 0.4, 0)); // double line for MEB
            result.LinesDrawn += 2;
            PlaceLabel(doc, view, textType, new XYZ((mebX0 + mebX1) / 2.0, Y_MEB - 1.5, 0),
                       "MEB — Main Earth Bar (BS EN 62305-3 §6.2)");

            // ── 6. Render each vertical leg ──────────────────────────
            int colsToFill = Math.Min(columns, Math.Max(ats.Count, Math.Max(dcs.Count, ees.Count)));
            for (int i = 0; i < colsToFill; i++)
            {
                double x = dcX[i];

                // Air terminal box at top
                if (i < ats.Count)
                {
                    string atTag = ResolveTag(ats[i], "AT", i + 1);
                    DrawBox(doc, view, x, Y_AT, BOX_W, BOX_H, "★ " + atTag, textType);
                    result.LinesDrawn += 4;
                    DrawLine(doc, view, new XYZ(x, Y_AT - BOX_H / 2.0, 0), new XYZ(x, Y_DC_MID + 1, 0));
                    result.LinesDrawn += 1;
                }

                // Down conductor label midway
                if (i < dcs.Count)
                {
                    string dcTag = ResolveTag(dcs[i], "DC", i + 1);
                    PlaceLabel(doc, view, textType, new XYZ(x + 0.5, Y_DC_MID, 0), dcTag);
                    DrawLine(doc, view, new XYZ(x, Y_DC_MID + 1, 0), new XYZ(x, Y_MEB + 0.4, 0));
                    result.LinesDrawn += 1;
                }

                // Earth electrode at bottom
                if (i < ees.Count)
                {
                    string eeTag = ResolveTag(ees[i], "EE", i + 1);
                    DrawLine(doc, view, new XYZ(x, Y_MEB - 0.4, 0), new XYZ(x, Y_EE + BOX_H, 0));
                    DrawBox(doc, view, x, Y_EE, BOX_W, BOX_H, "⏚ " + eeTag, textType);
                    result.LinesDrawn += 5;
                }
            }

            // ── 7. Render SPDs branching left off the MEB ────────────
            // Stack SPDs vertically just above the MEB at x = mebX0 - 4
            double spdX = mebX0 - 6.0;
            double y = Y_SPD;
            for (int i = 0; i < spds.Count; i++)
            {
                string tag  = ResolveTag(spds[i], "SPD", i + 1);
                string type = StingTools.Core.ParameterHelpers.GetString(spds[i],
                    StingTools.Core.Fabrication.LpsParams.SURGE_PROTECTION_LVL_TXT);
                if (string.IsNullOrEmpty(type)) type = "Type 1+2";
                DrawBox(doc, view, spdX, y, BOX_W + 1.0, BOX_H, $"★ {tag} ({type})", textType);
                DrawLine(doc, view, new XYZ(spdX + (BOX_W + 1.0) / 2.0, y, 0), new XYZ(mebX0, Y_MEB, 0));
                result.LinesDrawn += 5;
                y -= (BOX_H + 1.0);
            }

            // ── 8. Title block (top-left) ────────────────────────────
            PlaceLabel(doc, view, textType, new XYZ(mebX0, Y_AT + BOX_H + 3.0, 0),
                       "LIGHTNING PROTECTION — SINGLE LINE DIAGRAM");
            PlaceLabel(doc, view, textType, new XYZ(mebX0, Y_AT + BOX_H + 1.5, 0),
                       "BS EN 62305-3 / IEC 62305-3");

            // ── 9. Legend (top-right) ────────────────────────────────
            double legX = mebX1 + 4.0;
            PlaceLabel(doc, view, textType, new XYZ(legX, Y_AT + 1.0, 0), "LEGEND");
            PlaceLabel(doc, view, textType, new XYZ(legX, Y_AT - 1.0, 0), "★  Air terminal / SPD");
            PlaceLabel(doc, view, textType, new XYZ(legX, Y_AT - 2.5, 0), "│  Down conductor");
            PlaceLabel(doc, view, textType, new XYZ(legX, Y_AT - 4.0, 0), "⏚  Earth electrode");
            PlaceLabel(doc, view, textType, new XYZ(legX, Y_AT - 5.5, 0), "═  Main earth bar");

            // ── 10. Stamp the drawing-type id (Phase 113 alignment) ──
            try
            {
                StingTools.Core.Drawing.DrawingTypeStamper.Stamp(view, "elec-lps-coverage-A3");
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SldEngine stamp: {ex.Message}"); }

            result.Notes =
                $"Built LPS SLD: {ats.Count} AT · {dcs.Count} DC · {ees.Count} EE · {spds.Count} SPD" +
                $" — {result.LinesDrawn} lines / labels drawn.";
            return result;
        }

        // ── Drawing primitives ─────────────────────────────────────────

        private static void DrawLine(Document doc, ViewDrafting view, XYZ a, XYZ b)
        {
            try
            {
                if (a == null || b == null) return;
                if (a.DistanceTo(b) < 1e-9) return;
                var dl = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                // Wave A #1 — mark every line as engine-owned so the next
                // rebuild only purges what this engine placed.
                if (dl != null) StingLpsSldStampSchema.Stamp(dl);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DrawLine: {ex.Message}"); }
        }

        private static void DrawBox(Document doc, ViewDrafting view,
            double cx, double cy, double w, double h, string label, TextNoteType textType)
        {
            try
            {
                var ll = new XYZ(cx - w / 2.0, cy - h / 2.0, 0);
                var lr = new XYZ(cx + w / 2.0, cy - h / 2.0, 0);
                var ur = new XYZ(cx + w / 2.0, cy + h / 2.0, 0);
                var ul = new XYZ(cx - w / 2.0, cy + h / 2.0, 0);
                DrawLine(doc, view, ll, lr);
                DrawLine(doc, view, lr, ur);
                DrawLine(doc, view, ur, ul);
                DrawLine(doc, view, ul, ll);
                if (textType != null && !string.IsNullOrEmpty(label))
                {
                    var tn = TextNote.Create(doc, view.Id, new XYZ(cx, cy, 0), label, textType.Id);
                    if (tn != null) StingLpsSldStampSchema.Stamp(tn);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DrawBox: {ex.Message}"); }
        }

        private static void PlaceLabel(Document doc, ViewDrafting view, TextNoteType textType, XYZ p, string text)
        {
            try
            {
                if (textType == null || string.IsNullOrEmpty(text)) return;
                var tn = TextNote.Create(doc, view.Id, p, text, textType.Id);
                if (tn != null) StingLpsSldStampSchema.Stamp(tn);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"PlaceLabel: {ex.Message}"); }
        }

        private static string ResolveTag(FamilyInstance fi, string fallbackPrefix, int seq)
        {
            try
            {
                string explicitTag = StingTools.Core.ParameterHelpers.GetString(fi, "ASS_TAG_1");
                if (!string.IsNullOrEmpty(explicitTag)) return explicitTag;
                string s = StingTools.Core.ParameterHelpers.GetString(fi, StingTools.Core.ParamRegistry.SEQ);
                if (!string.IsNullOrEmpty(s)) return $"{fallbackPrefix}-{s}";
                return $"{fallbackPrefix}-{seq:D3}";
            }
            catch
            {
                return $"{fallbackPrefix}-{seq:D3}";
            }
        }
    }
}
