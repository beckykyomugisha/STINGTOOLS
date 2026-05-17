// StingTools — D4: Lightning Protection System Schematic (Phase 179)
//
// Generates a BS EN 62305-3 / NFPA 780 LPS schematic in a new ViewDrafting
// showing the capture mesh (air terminals), down conductors, bonding bars, and
// earth electrodes. Reads LPS_COMPONENT_TYPE_TXT on any element to classify
// components into four groups. Protection level is read from
// LPS_PROTECTION_LEVEL_TXT on ProjectInformation.
//
// Workflow tag: LPS_Schematic

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
    /// Generates a BS EN 62305-3 / NFPA 780 LPS schematic in a new drafting view.
    /// Components are read by LPS_COMPONENT_TYPE_TXT; project protection level from
    /// LPS_PROTECTION_LEVEL_TXT on ProjectInformation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LPSSchematicCommand : IExternalCommand
    {
        public const string Tag = "LPS_Schematic";

        // Known LPS component type values.
        private const string TypeAirTerminal  = "AirTerminal";
        private const string TypeDownConductor = "DownConductor";
        private const string TypeEarthElectrode = "EarthElectrode";
        private const string TypeBondingBar   = "BondingBar";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Read project-level LPS parameters.
            var projInfo = doc.ProjectInformation;
            string protLevel = projInfo?.LookupParameter("LPS_PROTECTION_LEVEL_TXT")?.AsString()?.Trim();
            if (string.IsNullOrEmpty(protLevel)) protLevel = "I";
            string meshSize = projInfo?.LookupParameter("LPS_MESH_SIZE_TXT")?.AsString()?.Trim()
                ?? "";

            // Collect all elements with LPS_COMPONENT_TYPE_TXT populated.
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            var airTerminals   = new List<Element>();
            var downConductors = new List<Element>();
            var earthElectrodes = new List<Element>();
            var bondingBars    = new List<Element>();

            foreach (var el in allElements)
            {
                string compType = null;
                try { compType = el.LookupParameter("LPS_COMPONENT_TYPE_TXT")?.AsString()?.Trim(); }
                catch { /* parameter not applicable to this element */ }

                if (string.IsNullOrEmpty(compType)) continue;

                if (compType.Equals(TypeAirTerminal,   StringComparison.OrdinalIgnoreCase))
                    airTerminals.Add(el);
                else if (compType.Equals(TypeDownConductor, StringComparison.OrdinalIgnoreCase))
                    downConductors.Add(el);
                else if (compType.Equals(TypeEarthElectrode, StringComparison.OrdinalIgnoreCase))
                    earthElectrodes.Add(el);
                else if (compType.Equals(TypeBondingBar, StringComparison.OrdinalIgnoreCase))
                    bondingBars.Add(el);
            }

            int totalComponents = airTerminals.Count + downConductors.Count
                                + earthElectrodes.Count + bondingBars.Count;

            using (var tx = new Transaction(doc, "STING LPS Schematic"))
            {
                tx.Start();

                var view = CreateDraftingView(doc, "STING - LPS Schematic");
                if (view == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("STING LPS Schematic",
                        "Could not create a drafting view — no Drafting ViewFamilyType found.");
                    return Result.Failed;
                }

                DrawLPSSchematic(doc, view, protLevel, meshSize,
                    airTerminals, downConductors, earthElectrodes, bondingBars);

                tx.Commit();

                string summary = totalComponents > 0
                    ? $"Components found and drawn:\n" +
                      $"  Air Terminals:    {airTerminals.Count}\n" +
                      $"  Down Conductors:  {downConductors.Count}\n" +
                      $"  Earth Electrodes: {earthElectrodes.Count}\n" +
                      $"  Bonding Bars:     {bondingBars.Count}"
                    : "No LPS components found (LPS_COMPONENT_TYPE_TXT not populated).\n" +
                      "Schematic drawn with placeholder geometry.";

                TaskDialog.Show("STING LPS Schematic",
                    $"LPS schematic generated.\n\nView: {view.Name}\n\n{summary}");
            }

            return Result.Succeeded;
        }

        // ---------------------------------------------------------------- drawing

        private static void DrawLPSSchematic(Document doc, ViewDrafting view,
            string protLevel, string meshSize,
            List<Element> airTerminals, List<Element> downConductors,
            List<Element> earthElectrodes, List<Element> bondingBars)
        {
            // ── Roof outline (dashed rectangle 200 mm × 150 mm) ─────────────────
            // Dashed line is approximated with solid DetailCurves (no graphic
            // override API in drafting views without a view template).
            double roofX = 0.0;
            double roofY = 0.0;
            double roofW = Mm(200.0);
            double roofH = Mm(150.0);

            DrawBox(doc, view, roofX, roofY, roofW, roofH);
            PlaceLabel(doc, view, roofX + Mm(2), roofY + roofH + Mm(2), "Building Roof");

            // ── Protection level badge — top-right corner ────────────────────────
            string lvlBadge = $"LPL {protLevel}";
            if (!string.IsNullOrEmpty(meshSize)) lvlBadge += $"\nMesh {meshSize}";
            PlaceLabel(doc, view, roofX + roofW - Mm(40), roofY + roofH + Mm(2), lvlBadge);

            // ── Air terminals: cross (+) symbols on roof ─────────────────────────
            int atCount = airTerminals.Count > 0 ? airTerminals.Count : 4; // default 4 if none
            bool moreAT  = atCount > 12;
            int  drawAT  = Math.Min(atCount, 12);

            int atCols = (int)Math.Ceiling(Math.Sqrt(drawAT));
            int atRows = (int)Math.Ceiling((double)drawAT / atCols);
            double atSpacingX = roofW / (atCols + 1);
            double atSpacingY = roofH / (atRows + 1);

            for (int i = 0; i < drawAT; i++)
            {
                int col = i % atCols;
                int row = i / atCols;
                double atX = roofX + atSpacingX * (col + 1);
                double atY = roofY + atSpacingY * (row + 1);

                // Cross symbol: two short lines.
                double arm = Mm(4.0);
                DrawLine(doc, view, new XYZ(atX - arm, atY, 0), new XYZ(atX + arm, atY, 0));
                DrawLine(doc, view, new XYZ(atX, atY - arm, 0), new XYZ(atX, atY + arm, 0));

                PlaceLabel(doc, view, atX + Mm(1), atY + Mm(1), $"AT-{i + 1}");
            }

            if (moreAT)
                PlaceLabel(doc, view,
                    roofX + Mm(2), roofY + Mm(5),
                    $"x{atCount} air terminals total");

            // ── Down conductors: vertical lines from roof edge to ground ─────────
            double groundY = roofY - Mm(40.0); // ground level below the roof diagram
            int dcCount = downConductors.Count > 0 ? downConductors.Count : 4;
            double dcSpacingX = roofW / (dcCount + 1);

            for (int i = 0; i < dcCount; i++)
            {
                double dcX = roofX + dcSpacingX * (i + 1);

                // Vertical line from roof bottom edge to ground.
                DrawLine(doc, view,
                    new XYZ(dcX, roofY,    0),
                    new XYZ(dcX, groundY,  0));

                PlaceLabel(doc, view, dcX + Mm(1), roofY - Mm(5), $"DC-{i + 1}");
            }

            // ── Bonding / MEB bar: horizontal line connecting all DCs at ground ──
            int mebCount = bondingBars.Count > 0 ? bondingBars.Count : 1;
            double mebY  = groundY;
            double mebX0 = roofX + dcSpacingX;
            double mebX1 = roofX + dcSpacingX * dcCount;

            DrawLine(doc, view, new XYZ(mebX0, mebY, 0), new XYZ(mebX1, mebY, 0));
            PlaceLabel(doc, view,
                mebX0 - Mm(5), mebY - Mm(4),
                $"MEB ({mebCount} Bonding Bar{(mebCount == 1 ? "" : "s")})");

            // ── Earth electrodes: horizontal line below ground + small markers ───
            int eeCount = earthElectrodes.Count > 0 ? earthElectrodes.Count : 2;
            double eeY  = groundY - Mm(20.0);
            double eeSpacingX = roofW / (eeCount + 1);

            for (int i = 0; i < eeCount; i++)
            {
                double eeX = roofX + eeSpacingX * (i + 1);

                // Vertical connection from MEB line down to electrode.
                DrawLine(doc, view, new XYZ(eeX, mebY,  0), new XYZ(eeX, eeY, 0));

                // Electrode: small horizontal bar + cross.
                double eeArm = Mm(5.0);
                DrawLine(doc, view,
                    new XYZ(eeX - eeArm, eeY, 0),
                    new XYZ(eeX + eeArm, eeY, 0));
                DrawLine(doc, view,
                    new XYZ(eeX, eeY,           0),
                    new XYZ(eeX, eeY - Mm(8.0), 0));

                PlaceLabel(doc, view, eeX + Mm(2), eeY - Mm(4), $"EE-{i + 1}");
            }

            // Ground level annotation.
            PlaceLabel(doc, view, roofX - Mm(30), groundY + Mm(2), "Ground Level ▼");
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
            try { doc.Create.NewDetailCurve(view, Line.CreateBound(start, end)); }
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
