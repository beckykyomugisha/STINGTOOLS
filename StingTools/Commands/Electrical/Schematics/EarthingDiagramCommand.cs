// StingTools — D2: Earthing Arrangement Diagram (Phase 179)
//
// Generates a TN-S / TN-C-S / TN-C / IT earthing system diagram in a new
// ViewDrafting. The system type is read from ELC_EARTHING_SYSTEM_TXT on
// ProjectInformation (default "TN-S"). The MET location description is read
// from ELC_MET_LOCATION_TXT.
//
// Workflow tag: Earthing_Diagram

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Schematics
{
    /// <summary>
    /// Generates a TN-S / TN-C-S / TN-C / IT earthing arrangement diagram in a
    /// new drafting view. Reads system type from ELC_EARTHING_SYSTEM_TXT on
    /// ProjectInformation (default "TN-S").
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EarthingDiagramCommand : IExternalCommand
    {
        public const string Tag = "Earthing_Diagram";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Read project-level earthing parameters.
            var projInfo = doc.ProjectInformation;
            string system = projInfo?.LookupParameter("ELC_EARTHING_SYSTEM_TXT")?.AsString()?.Trim();
            if (string.IsNullOrEmpty(system)) system = "TN-S";

            string metLocation = projInfo?.LookupParameter("ELC_MET_LOCATION_TXT")?.AsString()?.Trim();
            if (string.IsNullOrEmpty(metLocation)) metLocation = "Main Switchroom";

            using (var tx = new Transaction(doc, "STING Earthing Arrangement Diagram"))
            {
                tx.Start();

                var view = CreateDraftingView(doc, "STING - Earthing Arrangement");
                if (view == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("STING Earthing Diagram",
                        "Could not create a drafting view — no Drafting ViewFamilyType found.");
                    return Result.Failed;
                }

                DrawEarthingArrangement(doc, view, system, metLocation);

                tx.Commit();

                TaskDialog.Show("STING Earthing Diagram",
                    $"Earthing arrangement diagram generated.\n\n" +
                    $"View:   {view.Name}\n" +
                    $"System: {system}\n" +
                    $"MET:    {metLocation}");
            }

            return Result.Succeeded;
        }

        // ---------------------------------------------------------------- drawing

        private static void DrawEarthingArrangement(Document doc, ViewDrafting view,
            string system, string metLocation)
        {
            // Coordinate origin (0,0) = top-left of the diagram.
            // All dimensions in Revit internal feet via Mm().

            bool isTnCs = system.Equals("TN-C-S", StringComparison.OrdinalIgnoreCase);
            bool isTnC  = system.Equals("TN-C",   StringComparison.OrdinalIgnoreCase);
            bool isIt   = system.Equals("IT",      StringComparison.OrdinalIgnoreCase);

            // ── Supply transformer block (60 mm × 40 mm) at origin ──────────────
            double txX = 0.0;
            double txY = 0.0;
            double txW = Mm(60.0);
            double txH = Mm(40.0);

            if (isIt)
            {
                // IT: draw isolation transformer symbol (two nested boxes) instead.
                DrawBox(doc, view, txX,         txY,         txW,         txH);
                DrawBox(doc, view, txX + Mm(5), txY + Mm(5), txW - Mm(10), txH - Mm(10));
                PlaceLabel(doc, view, txX + Mm(2), txY + txH + Mm(2),
                    $"Isolation Transformer\n{system}");
            }
            else
            {
                DrawBox(doc, view, txX, txY, txW, txH);
                PlaceLabel(doc, view, txX + Mm(2), txY + txH / 2.0 + Mm(1),
                    $"Supply Transformer\n{system}");
            }

            // ── Neutral line — horizontal right from transformer ─────────────────
            double neutralY = txY + txH * 0.35;
            double neutralEndX = txX + txW + Mm(80.0);

            DrawLine(doc, view,
                new XYZ(txX + txW, neutralY, 0),
                new XYZ(neutralEndX, neutralY, 0));
            PlaceLabel(doc, view, txX + txW + Mm(2), neutralY + Mm(2), "N");

            // ── Earth / PEN line — angled down-right from transformer ───────────
            string earthLabel = (isTnC || isTnCs) ? "PEN" : "PE";
            double earthStartX = txX + txW;
            double earthStartY = txY + txH * 0.65;
            double earthMidX   = txX + txW + Mm(30.0);
            double earthMidY   = earthStartY + Mm(20.0);  // angled down

            DrawLine(doc, view,
                new XYZ(earthStartX, earthStartY, 0),
                new XYZ(earthMidX,   earthMidY,   0));
            PlaceLabel(doc, view, earthStartX + Mm(2), earthStartY - Mm(5), earthLabel);

            // ── TN-C-S: split point (PEN → N + PE) ──────────────────────────────
            double splitX = earthMidX;
            double splitY = earthMidY;

            if (isTnCs)
            {
                // "Split" marker box.
                DrawBox(doc, view, splitX - Mm(5), splitY - Mm(5), Mm(10), Mm(10));
                PlaceLabel(doc, view, splitX - Mm(4), splitY - Mm(8), "Split\nPEN→N+PE");

                // N branch from split.
                DrawLine(doc, view,
                    new XYZ(splitX, splitY, 0),
                    new XYZ(neutralEndX, neutralY, 0));
                PlaceLabel(doc, view, splitX + Mm(5), splitY - Mm(2), "N");
            }

            // ── MET box (20 mm × 10 mm) below origin ────────────────────────────
            double metX = txX + Mm(15.0);
            double metY = txY - Mm(30.0);
            double metW = Mm(20.0);
            double metH = Mm(10.0);

            DrawBox(doc, view, metX, metY, metW, metH);
            PlaceLabel(doc, view, metX, metY - Mm(8),
                $"MET\n{metLocation}");

            // Bonding line from transformer earth down to MET.
            DrawLine(doc, view,
                new XYZ(txX + txW * 0.4, txY, 0),
                new XYZ(metX + metW / 2.0, metY + metH, 0));

            // Earth line from MET to split/earth mid, if TN-S or IT.
            if (!isTnCs)
            {
                DrawLine(doc, view,
                    new XYZ(earthMidX, earthMidY, 0),
                    new XYZ(metX + metW / 2.0, metY + metH, 0));
            }

            // ── Earth electrode symbol: small triangle + vertical line ───────────
            double eeX = metX + metW / 2.0;
            double eeTopY = metY;
            double eeBottomY = metY - Mm(20.0);

            DrawLine(doc, view,
                new XYZ(eeX, eeTopY, 0),
                new XYZ(eeX, eeBottomY, 0));
            // Triangle: three lines forming a downward-pointing triangle.
            DrawLine(doc, view,
                new XYZ(eeX - Mm(6), eeBottomY + Mm(6), 0),
                new XYZ(eeX + Mm(6), eeBottomY + Mm(6), 0));
            DrawLine(doc, view,
                new XYZ(eeX - Mm(6), eeBottomY + Mm(6), 0),
                new XYZ(eeX, eeBottomY, 0));
            DrawLine(doc, view,
                new XYZ(eeX + Mm(6), eeBottomY + Mm(6), 0),
                new XYZ(eeX, eeBottomY, 0));
            PlaceLabel(doc, view, eeX + Mm(3), eeBottomY - Mm(4), "Earth Electrode");

            // ── Consumer / main distribution board (40 mm × 30 mm) ──────────────
            double mdbX = neutralEndX;
            double mdbY = neutralY - Mm(30.0);
            double mdbW = Mm(40.0);
            double mdbH = Mm(30.0);

            DrawBox(doc, view, mdbX, mdbY, mdbW, mdbH);
            PlaceLabel(doc, view, mdbX + Mm(2), mdbY + mdbH / 2.0 + Mm(1),
                "Main Distribution Board");

            // Connect neutral to MDB.
            DrawLine(doc, view,
                new XYZ(neutralEndX, neutralY, 0),
                new XYZ(mdbX, mdbY + mdbH * 0.5, 0));

            // Connect PE/PEN from split / earth mid to MDB bottom.
            double peInX = isTnCs ? splitX : earthMidX;
            double peInY = isTnCs ? splitY : earthMidY;
            DrawLine(doc, view,
                new XYZ(peInX, peInY, 0),
                new XYZ(mdbX + mdbW * 0.5, mdbY, 0));
            PlaceLabel(doc, view, mdbX + mdbW * 0.5 + Mm(1), mdbY - Mm(4), "PE");
        }

        // ---------------------------------------------------------------- helpers

        private static ViewDrafting CreateDraftingView(Document doc, string name)
        {
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
            if (vft == null) return null;
            var v = ViewDrafting.Create(doc, vft.Id);
            try { v.Name = name; } catch { }
            return v;
        }

        private static double Mm(double mm) => mm / 304.8;

        private static void DrawLine(Document doc, ViewDrafting view, XYZ start, XYZ end)
        {
            if (start.IsAlmostEqualTo(end)) return;
            try
            {
                doc.Create.NewDetailCurve(view, Line.CreateBound(start, end));
            }
            catch (Exception ex) { StingLog.Warn($"DrawLine: {ex.Message}"); }
        }

        private static void DrawBox(Document doc, ViewDrafting view,
            double x, double y, double w, double h)
        {
            try
            {
                doc.Create.NewDetailCurve(view,
                    Line.CreateBound(new XYZ(x,   y,   0), new XYZ(x+w, y,   0)));
                doc.Create.NewDetailCurve(view,
                    Line.CreateBound(new XYZ(x+w, y,   0), new XYZ(x+w, y+h, 0)));
                doc.Create.NewDetailCurve(view,
                    Line.CreateBound(new XYZ(x+w, y+h, 0), new XYZ(x,   y+h, 0)));
                doc.Create.NewDetailCurve(view,
                    Line.CreateBound(new XYZ(x,   y+h, 0), new XYZ(x,   y,   0)));
            }
            catch (Exception ex) { StingLog.Warn($"DrawBox: {ex.Message}"); }
        }

        private static void PlaceLabel(Document doc, ViewDrafting view,
            double x, double y, string text)
        {
            try
            {
                var tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                if (tnt != ElementId.InvalidElementId)
                    TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, tnt);
            }
            catch (Exception ex) { StingLog.Warn($"PlaceLabel: {ex.Message}"); }
        }
    }
}
