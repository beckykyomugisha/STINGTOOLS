// Fire_StairPressurisation — supply airflow for a pressurised stair
// (BS EN 12101-6 method) and the door-opening-force check.
//
// Optionally select the stair ROOM first: the doors on its boundary are
// counted by how they swing (a door whose ToRoom is the stair opens into it)
// and by leaf count, and those counts prefill the form. Everything stays
// editable, because the model rarely knows about lift landing doors or the
// leakage of the stair walls. Nothing is written to the model.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fire;
using StingTools.Core.Mep.Networks;
using StingTools.UI;

namespace StingTools.Commands.Fire
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StairPressurisationCommand : IExternalCommand
    {
        private const string Title = "STING Fire — Stair Pressurisation";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var warnings = new List<string>();
                SmokeControlDesignData data;
                try { data = MepDesignDataLoader.Smoke(doc, warnings); }
                catch (Exception ex) { TaskDialog.Show(Title, $"Smoke control design data could not be read: {ex.Message}"); return Result.Failed; }
                var problems = data.Validate();
                if (problems.Count > 0)
                {
                    TaskDialog.Show(Title, "Smoke control design data is invalid:\n\n" + string.Join("\n", problems.Take(12)));
                    return Result.Failed;
                }

                var room = ctx.UIDoc?.Selection?.GetElementIds()?.Select(id => doc.GetElement(id)).OfType<Room>().FirstOrDefault();
                var counts = room != null ? CountDoors(doc, room) : new DoorCounts();
                string note = room != null
                    ? $"Door counts from room '{room.Name}' ({counts.Total} doors on its boundary). Check them — lift doors are not modelled as doors."
                    : "Tip: select the stair room first to count its doors from the model.";

                int clsIdx = Math.Max(0, data.Classes.FindIndex(c => string.Equals(c.Id, data.DefaultClassId, StringComparison.OrdinalIgnoreCase)));
                var form = new StingFormDialog(Title, "Stair and doors", note)
                    .Choice("class", "System class", data.Classes.Select(c =>
                        $"{c.Label}: {c.DesignPressurePa:0} Pa, {c.OpenDoorVelocityMs:0.##} m/s{(c.Verify ? " (verify)" : "")}"), clsIdx)
                    .Number("into", "Single-leaf doors opening INTO the stair", counts.SingleInto, 0, 500)
                    .Number("out", "Single-leaf doors opening OUT of the stair", counts.SingleOut, 0, 500)
                    .Number("dbl", "Double-leaf doors", counts.Double, 0, 500)
                    .Number("lift", "Lift landing doors", 0, 0, 500)
                    .Number("other", "Other leakage (walls, windows, vents) m²", 0, 0, 100)
                    .Number("open", "Doors open in the velocity case", data.Class(null)?.OpenDoors ?? 1, 0, 20)
                    .Number("w", "Open door width (m)", counts.TypicalWidthM > 0 ? counts.TypicalWidthM : 1.0, 0.5, 3)
                    .Number("h", "Open door height (m)", counts.TypicalHeightM > 0 ? counts.TypicalHeightM : 2.1, 1.5, 4)
                    .Number("allow", "Leakage allowance (×)", data.LeakageAllowance, 1, 3)
                    .Number("resid", "Residual ΔP across closed doors, open case (Pa, 0 = design)", data.OpenCaseResidualPressurePa, 0, 100)
                    .Number("closer", "Door closer force (N)", data.DoorCloserForceN, 0, 200);
                if (form.ShowDialog() != true) return Result.Cancelled;

                var cls = data.Classes[Math.Max(0, form.ChoiceIndex("class"))];
                var input = new StairPressurisationInput
                {
                    SystemClass = cls.Id,
                    DesignPressurePa = cls.DesignPressurePa,
                    OpenDoorVelocityMs = cls.OpenDoorVelocityMs,
                    OpenDoors = (int)Math.Round(form.Get("open")),
                    DoorWidthM = form.Get("w"),
                    DoorHeightM = form.Get("h"),
                    LeakageAllowance = form.Get("allow"),
                    OpenCaseResidualPressurePa = form.Get("resid"),
                    DoorCloserForceN = form.Get("closer"),
                    HandleToEdgeM = data.HandleToEdgeM,
                    MaxDoorOpeningForceN = data.MaxDoorOpeningForceN
                };
                void Add(string label, double count, string key)
                {
                    if (count > 0) input.ClosedLeakage.Add(new LeakagePath { Label = label, Count = (int)Math.Round(count), AreaEachM2 = data.DoorLeakage(key) });
                }
                Add("Single door, into stair", form.Get("into"), "singleLeafOpeningIntoStair");
                Add("Single door, out of stair", form.Get("out"), "singleLeafOpeningOutOfStair");
                Add("Double door", form.Get("dbl"), "doubleLeaf");
                Add("Lift landing door", form.Get("lift"), "liftLandingDoor");
                if (form.Get("other") > 0) input.ClosedLeakage.Add(new LeakagePath { Label = "Other leakage", Count = 1, AreaEachM2 = form.Get("other") });

                var r = StairPressurisation.Calculate(input);
                warnings.AddRange(r.Warnings);

                var panel = StingResultPanel.Create("Stair Pressurisation");
                panel.SetSubtitle($"{cls.Label} · {cls.DesignPressurePa:0} Pa · {cls.OpenDoorVelocityMs:0.##} m/s" + (room != null ? $" · room {room.Name}" : ""));
                panel.AddSection("SUPPLY")
                     .MetricHighlight("Fan supply", $"{r.SupplyM3s:F2} m³/s ({r.SupplyM3s * 1000:F0} L/s)",
                                      $"includes ×{input.LeakageAllowance:0.##} allowance")
                     .Metric("Governing case", r.Governs)
                     .Metric("Doors-closed leakage", $"{r.ClosedDoorsFlowM3s:F3} m³/s through {r.ClosedLeakageAreaM2:F3} m²")
                     .Metric("Open-door airflow", $"{r.OpenDoorFlowM3s:F3} m³/s ({input.OpenDoors} × {input.DoorWidthM:0.##}×{input.DoorHeightM:0.##} m @ {input.OpenDoorVelocityMs:0.##} m/s)")
                     .Metric("Leakage in open case", $"{r.OpenCaseLeakageM3s:F3} m³/s");
                var door = panel.AddSection("DOOR OPENING FORCE");
                if (r.DoorForceOk) door.Metric("Force at handle", $"{r.DoorOpeningForceN:F0} N ≤ {input.MaxDoorOpeningForceN:F0} N");
                else door.MetricError("Force at handle", $"{r.DoorOpeningForceN:F0} N > {input.MaxDoorOpeningForceN:F0} N");

                var rows = input.ClosedLeakage.Select(p => new[] { p.Label, p.Count.ToString(), $"{p.AreaEachM2:F3}", $"{p.TotalAreaM2:F3}" }).ToList();
                panel.AddSection("LEAKAGE PATHS").Table(new[] { "Path", "No.", "m² each", "m²" }, rows);

                var basis = panel.AddSection("BASIS");
                basis.Text("Q = 0.83·A·ΔP^½ through gaps; open doors at the velocity criterion; F = F_dc + W·A·ΔP / (2(W − d)).");
                basis.Text("Pressure relief (to hold the stair below the maximum with all doors shut) is not sized here.");
                if (cls.Verify) basis.Text($"Class '{cls.Id}' criteria and the door leakage areas are marked verify — confirm against BS EN 12101-6 and the fire strategy.");
                foreach (var src in data.Sources) basis.Text("Data: " + src);
                if (warnings.Count > 0)
                {
                    var w = panel.AddSection("WARNINGS");
                    foreach (var line in warnings.Distinct().Take(15)) w.Text(line);
                }
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StairPressurisationCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private sealed class DoorCounts
        {
            public int SingleInto, SingleOut, Double;
            public double TypicalWidthM, TypicalHeightM;
            public int Total => SingleInto + SingleOut + Double;
        }

        /// <summary>
        /// Doors whose From/To room is the stair, in the last phase. A door
        /// swings into its ToRoom. Double leaf by family/type name, else by a
        /// width of 1.5 m or more.
        /// </summary>
        private static DoorCounts CountDoors(Document doc, Room stair)
        {
            var c = new DoorCounts();
            var widths = new List<double>();
            var heights = new List<double>();
            Phase phase = null;
            try { phase = doc.Phases.Cast<Phase>().LastOrDefault(); }
            catch (Exception ex) { StingLog.Warn($"StairPressurisation phases: {ex.Message}"); }
            foreach (var door in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors)
                         .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                try
                {
                    var to = phase != null ? door.get_ToRoom(phase) : door.ToRoom;
                    var from = phase != null ? door.get_FromRoom(phase) : door.FromRoom;
                    bool intoStair = to?.Id == stair.Id;
                    if (!intoStair && from?.Id != stair.Id) continue;

                    string name = ((door.Symbol?.FamilyName ?? "") + " " + (door.Symbol?.Name ?? "")).ToLowerInvariant();
                    double w = (door.Symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble()
                                ?? door.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble() ?? 0) * 0.3048;
                    double h = (door.Symbol?.get_Parameter(BuiltInParameter.DOOR_HEIGHT)?.AsDouble()
                                ?? door.get_Parameter(BuiltInParameter.DOOR_HEIGHT)?.AsDouble() ?? 0) * 0.3048;
                    bool dbl = name.Contains("double") || name.Contains("dbl") || name.Contains("pair") || w >= 1.5;
                    if (dbl) c.Double++;
                    else if (intoStair) c.SingleInto++;
                    else c.SingleOut++;
                    if (!dbl && w > 0) widths.Add(w);
                    if (!dbl && h > 0) heights.Add(h);
                }
                catch (Exception ex) { StingLog.Warn($"StairPressurisation door {door.Id}: {ex.Message}"); }
            }
            if (widths.Count > 0) c.TypicalWidthM = Math.Round(widths.OrderBy(x => x).ElementAt(widths.Count / 2), 2);
            if (heights.Count > 0) c.TypicalHeightM = Math.Round(heights.OrderBy(x => x).ElementAt(heights.Count / 2), 2);
            return c;
        }
    }
}
