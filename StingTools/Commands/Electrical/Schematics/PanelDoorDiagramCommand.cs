// StingTools — D3: Panel Door / Busbar Interior Layout Diagram (Phase 179)
//
// Generates a panel door / busbar interior layout diagram in a new ViewDrafting
// showing breaker slot positions and busbar arrangement. The user selects a panel
// via TaskDialog (first four) or from the active selection.
//
// Workflow tag: Panel_DoorDiagram

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Schematics
{
    /// <summary>
    /// Generates a panel door / busbar interior layout diagram in a new drafting
    /// view. Reads circuit slot count from RBS_ELEC_NUMBER_OF_CIRCUITS, draws a
    /// two-column breaker layout with SPARE labels for unloaded slots, and an
    /// earth bar at the bottom.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelDoorDiagramCommand : IExternalCommand
    {
        public const string Tag = "Panel_DoorDiagram";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc  = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // Collect all electrical equipment panels.
            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Panel Door Diagram",
                    "No Electrical Equipment elements found in the project.");
                return Result.Succeeded;
            }

            // Prefer active selection if it contains electrical equipment.
            FamilyInstance chosenPanel = null;
            try
            {
                var selIds = uidoc.Selection.GetElementIds();
                chosenPanel = selIds
                    .Select(id => doc.GetElement(id) as FamilyInstance)
                    .FirstOrDefault(fi => fi != null
                        && fi.Category?.Id?.Value ==
                           (long)BuiltInCategory.OST_ElectricalEquipment);
            }
            catch { /* no active selection — fall through to picker */ }

            // If no selection, let user pick via TaskDialog (up to 4 options).
            if (chosenPanel == null)
            {
                var td = new TaskDialog("STING Panel Door Diagram")
                {
                    MainInstruction = "Select a panel to generate the door layout for:",
                    CommonButtons   = TaskDialogCommonButtons.Cancel
                };

                var candidates = panels.Take(4).ToList();
                var cmdLinks   = new[]
                {
                    TaskDialogCommandLinkId.CommandLink1,
                    TaskDialogCommandLinkId.CommandLink2,
                    TaskDialogCommandLinkId.CommandLink3,
                    TaskDialogCommandLinkId.CommandLink4
                };

                for (int i = 0; i < candidates.Count; i++)
                {
                    string nm = candidates[i]
                        .LookupParameter("Panel Name")?.AsString()
                        ?? candidates[i].Name
                        ?? $"Panel {i + 1}";
                    td.AddCommandLink(cmdLinks[i], nm);
                }

                if (panels.Count > 4)
                    td.FooterText = $"(Showing first 4 of {panels.Count} panels)";

                var tdResult = td.Show();
                int picked = tdResult switch
                {
                    TaskDialogResult.CommandLink1 => 0,
                    TaskDialogResult.CommandLink2 => 1,
                    TaskDialogResult.CommandLink3 => 2,
                    TaskDialogResult.CommandLink4 => 3,
                    _                             => -1
                };

                if (picked < 0 || picked >= candidates.Count) return Result.Cancelled;
                chosenPanel = candidates[picked];
            }

            // Read panel data.
            string panelName = chosenPanel
                .LookupParameter("Panel Name")?.AsString()
                ?? chosenPanel.Name
                ?? "Panel";

            int slotCount = chosenPanel
                .get_Parameter(BuiltInParameter.RBS_ELEC_NUMBER_OF_CIRCUITS)?.AsInteger()
                ?? 0;
            if (slotCount <= 0) slotCount = 24; // sensible default

            string ratingInfo = chosenPanel.LookupParameter("ELC_BUSBAR_RATING_TXT")?.AsString()
                ?? chosenPanel
                    .LookupParameter("Electrical - Panel Service")?.AsString()
                ?? "";

            // Gather circuits whose BaseEquipment matches this panel.
            var circuits = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .Where(es =>
                {
                    try { return es.BaseEquipment?.Id == chosenPanel.Id; }
                    catch { return false; }
                })
                .ToList();

            // Build a slot → circuit map (circuit number → circuit).
            var circuitBySlot = new Dictionary<int, ElectricalSystem>();
            foreach (var es in circuits)
            {
                try
                {
                    int circNum = es
                        .get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsInteger()
                        ?? -1;
                    if (circNum > 0 && !circuitBySlot.ContainsKey(circNum))
                        circuitBySlot[circNum] = es;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PanelDoorDiagram: circuit read {es?.Id}: {ex.Message}");
                }
            }

            using (var tx = new Transaction(doc, "STING Panel Door Diagram"))
            {
                tx.Start();

                string viewName = $"STING - Panel Layout - {panelName}";
                var view = CreateDraftingView(doc, viewName);
                if (view == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("STING Panel Door Diagram",
                        "Could not create a drafting view — no Drafting ViewFamilyType found.");
                    return Result.Failed;
                }

                DrawPanelLayout(doc, view, panelName, ratingInfo, slotCount, circuitBySlot);

                tx.Commit();

                TaskDialog.Show("STING Panel Door Diagram",
                    $"Panel door diagram generated.\n\n" +
                    $"View:     {view.Name}\n" +
                    $"Slots:    {slotCount}\n" +
                    $"Circuits: {circuits.Count}");
            }

            return Result.Succeeded;
        }

        // ---------------------------------------------------------------- drawing

        private static void DrawPanelLayout(Document doc, ViewDrafting view,
            string panelName, string ratingInfo, int slotCount,
            Dictionary<int, ElectricalSystem> circuitBySlot)
        {
            // Enclosure dimensions.
            // Width: 80 mm; Height: slotCount * 12 mm + 20 mm header + 15 mm footer.
            double encW  = Mm(80.0);
            double rowH  = Mm(12.0);
            double hdrH  = Mm(20.0);
            double ftrH  = Mm(15.0);
            double encH  = slotCount * rowH + hdrH + ftrH;
            double encX  = 0.0;
            double encY  = 0.0;

            // Outer enclosure box.
            DrawBox(doc, view, encX, encY, encW, encH);

            // Panel name label at top-centre.
            PlaceLabel(doc, view, encX + Mm(2), encY + encH - Mm(5), panelName);

            // Busbar line inside header area.
            double busY = encY + encH - hdrH + Mm(4);
            DrawLine(doc, view,
                new XYZ(encX + Mm(5),      busY, 0),
                new XYZ(encX + encW - Mm(5), busY, 0));
            string busLabel = string.IsNullOrEmpty(ratingInfo)
                ? "Busbar"
                : $"Busbar {ratingInfo}";
            PlaceLabel(doc, view, encX + Mm(6), busY + Mm(2), busLabel);

            // Vertical centreline dividing left and right columns.
            double colDiv = encX + encW / 2.0;

            // Breaker slot rows — two columns, odd on left, even on right.
            // Slots run from top (just below header) downward.
            double slotsTopY = encY + encH - hdrH;

            double brkW = encW / 2.0 - Mm(6.0);  // breaker width within column
            double brkH = Mm(8.0);
            double brkMarginX = Mm(3.0);
            double brkMarginY = (rowH - brkH) / 2.0;

            for (int slot = 1; slot <= slotCount; slot++)
            {
                // Row index counting from top.
                int rowIdx = (slot - 1) / 2;
                bool isLeft = (slot % 2 != 0); // odd = left column

                double rowTopY = slotsTopY - (rowIdx + 1) * rowH;
                double brkY    = rowTopY + brkMarginY;
                double brkX    = isLeft
                    ? encX + brkMarginX
                    : colDiv + brkMarginX;

                // Determine if this slot has a circuit.
                circuitBySlot.TryGetValue(slot, out var es);
                bool hasCircuit = es != null;

                if (hasCircuit)
                {
                    DrawBox(doc, view, brkX, brkY, brkW, brkH);

                    // Circuit description and rating.
                    string desc   = es.LookupParameter("ELC_CIRCUIT_DESC_TXT")?.AsString() ?? "";
                    string rating = es.LookupParameter("ELC_CIRCUIT_RATING_TXT")?.AsString() ?? "";
                    if (string.IsNullOrEmpty(rating))
                        rating = es.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM)
                                    ?.AsValueString() ?? "";

                    string slotLabel = string.IsNullOrEmpty(desc)
                        ? $"{slot}"
                        : $"{slot} {desc}";
                    if (!string.IsNullOrEmpty(rating)) slotLabel += $"\n{rating}";

                    PlaceLabel(doc, view, brkX + Mm(1), brkY + brkH - Mm(1), slotLabel);
                }
                else
                {
                    // Spare slot: outlined box with "SPARE" text.
                    DrawBox(doc, view, brkX, brkY, brkW, brkH);
                    PlaceLabel(doc, view, brkX + Mm(1), brkY + brkH - Mm(1), "SPARE");
                }
            }

            // Earth bar at the bottom of the enclosure.
            double ebY = encY + ftrH / 2.0;
            DrawLine(doc, view,
                new XYZ(encX + Mm(5),      ebY, 0),
                new XYZ(encX + encW - Mm(5), ebY, 0));
            PlaceLabel(doc, view, encX + Mm(6), ebY + Mm(2), "Earth Bar");
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
