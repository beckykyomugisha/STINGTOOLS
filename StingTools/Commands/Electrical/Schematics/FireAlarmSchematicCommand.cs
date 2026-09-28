// StingTools — D1: Fire Alarm Loop / Zone Schematic Generator (Phase 179)
//
// Generates a BS 5839-1 / NFPA 72 fire alarm loop schematic in a new
// ViewDrafting. Each loop (keyed on FLS_SFTY_DEV_LOOP_TXT) is drawn as a
// horizontal bus with vertical drops for each device. The FACP box
// anchors the left end of each loop.
//
// Workflow tag: FireAlarm_Schematic

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
    /// Generates a BS 5839-1 / NFPA 72 fire alarm loop schematic in a
    /// new drafting view. Devices are grouped by FLS_SFTY_DEV_LOOP_TXT; each
    /// loop is drawn as a horizontal bus with vertical drops and device labels.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FireAlarmSchematicCommand : IExternalCommand
    {
        // Workflow tag consumed by WorkflowEngine / StingCommandHandler.
        public const string Tag = "FireAlarm_Schematic";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Collect fire alarm device elements.
            var devices = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .WhereElementIsNotElementType()
                .ToList();

            if (devices.Count == 0)
            {
                TaskDialog.Show("STING Fire Alarm Schematic",
                    "No Fire Alarm Device elements found in the project.\n\n" +
                    "Populate FLS_SFTY_DEV_LOOP_TXT on fire alarm devices and re-run.");
                return Result.Succeeded;
            }

            // Group devices by loop reference.
            // Devices with no loop value are grouped under NoLoop, drawn last and
            // counted in the result — never filed under an invented loop.
            var loopGroups = devices
                .GroupBy(LoopOf)
                .OrderBy(g => g.Key == NoLoop ? 1 : 0)
                .ThenBy(g => g.Key)
                .ToList();
            int noLoopCount = loopGroups.FirstOrDefault(g => g.Key == NoLoop)?.Count() ?? 0;
            if (noLoopCount > 0)
                StingLog.Warn($"FireAlarmSchematic: {noLoopCount} device(s) have no " +
                              $"{string.Join(" / ", LoopParams)} value — drawn under '{NoLoop}'");

            using (var tx = new Transaction(doc, "STING Fire Alarm Schematic"))
            {
                tx.Start();

                var view = CreateDraftingView(doc,
                    $"STING - Fire Alarm Schematic - {DateTime.Now:yyyyMMdd}");
                if (view == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("STING Fire Alarm Schematic",
                        "Could not create a drafting view — no Drafting ViewFamilyType found.");
                    return Result.Failed;
                }

                int totalDevicesDrawn = 0;

                for (int loopIdx = 0; loopIdx < loopGroups.Count; loopIdx++)
                {
                    var group = loopGroups[loopIdx];
                    string loopRef = group.Key;
                    var loopDevices = group.ToList();

                    // Vertical position: each loop is 80 mm apart.
                    double busY = -loopIdx * Mm(80.0);

                    // FACP / Zone box: 40 mm wide × 20 mm tall, left-anchored at x=0.
                    double facpW = Mm(40.0);
                    double facpH = Mm(20.0);
                    double facpX = 0.0;
                    double facpY = busY - facpH / 2.0;

                    DrawBox(doc, view, facpX, facpY, facpW, facpH);
                    PlaceLabel(doc, view,
                        facpX + Mm(2.0),
                        facpY + facpH / 2.0 + Mm(1.0),
                        loopRef == NoLoop ? NoLoop : $"FACP / Zone {loopRef}");

                    // Horizontal bus line from right edge of FACP to last device.
                    double busStartX = facpX + facpW;
                    int devCount = loopDevices.Count;
                    double busEndX = busStartX + devCount * Mm(30.0) + Mm(10.0);

                    try
                    {
                        doc.Create.NewDetailCurve(view,
                            Line.CreateBound(
                                new XYZ(busStartX, busY, 0),
                                new XYZ(busEndX,  busY, 0)));
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"FireAlarmSchematic: bus line loop {loopRef}: {ex.Message}");
                    }

                    // Device drops — vertical lines + labels.
                    for (int devIdx = 0; devIdx < devCount; devIdx++)
                    {
                        var el = loopDevices[devIdx];
                        double dropX = busStartX + (devIdx + 1) * Mm(30.0);
                        double dropTopY    = busY;
                        double dropBottomY = busY - Mm(25.0);

                        // Vertical drop line.
                        try
                        {
                            doc.Create.NewDetailCurve(view,
                                Line.CreateBound(
                                    new XYZ(dropX, dropTopY,    0),
                                    new XYZ(dropX, dropBottomY, 0)));
                        }
                        catch (Exception ex2)
                        {
                            StingLog.Warn($"FireAlarmSchematic: drop line dev {devIdx}: {ex2.Message}");
                        }

                        // Device symbol: small circle approximated as a tiny box.
                        double symH = Mm(6.0);
                        double symW = Mm(6.0);
                        DrawBox(doc, view,
                            dropX - symW / 2.0,
                            dropBottomY - symH,
                            symW, symH);

                        // Label: element name + mark.
                        string devName = el.Name ?? "Device";
                        string mark    = el.LookupParameter("MARK")?.AsString()
                                      ?? el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
                                      ?? "";
                        string label   = string.IsNullOrEmpty(mark)
                                         ? devName
                                         : $"{devName}\n{mark}";

                        PlaceLabel(doc, view,
                            dropX - Mm(8.0),
                            dropBottomY - symH - Mm(3.0),
                            label);

                        totalDevicesDrawn++;
                    }
                }

                tx.Commit();

                int realLoops = loopGroups.Count(g => g.Key != NoLoop);
                string result =
                    $"Schematic generated.\n\n" +
                    $"View:    {view.Name}\n" +
                    $"Loops:   {realLoops}\n" +
                    $"Devices: {totalDevicesDrawn}";
                if (noLoopCount > 0)
                    result += $"\n\n{noLoopCount} device(s) have no loop set " +
                              $"({string.Join(" / ", LoopParams)} empty) and are drawn under " +
                              $"'{NoLoop}', not on a loop. Populate the loop and re-run.";
                TaskDialog.Show("STING Fire Alarm Schematic", result);
            }

            return Result.Succeeded;
        }

        // ---------------------------------------------------------------- helpers

        // The device loop lives on the FLS_ fire-alarm parameters the fire alarm
        // device tag and schedule already show: FLS_SFTY_DEV_LOOP_TXT, then
        // FLS_SFTY_LOOP_NR_TXT. ELC_FIRE_LOOP_REF, read here before, was defined
        // nowhere, so every device fell into one loop.
        private static readonly string[] LoopParams = { "FLS_SFTY_DEV_LOOP_TXT", "FLS_SFTY_LOOP_NR_TXT" };

        private static string LoopOf(Element e)
        {
            foreach (string pn in LoopParams)
            {
                string v = e.LookupParameter(pn)?.AsString()?.Trim();
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return NoLoop;
        }

        /// <summary>Group key for a device with no loop value. Was "Zone 1", which
        /// filed unassigned devices under a loop nobody had defined.</summary>
        private const string NoLoop = "(no loop set)";

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
