// StingTools Phase 110 — Standards & Compliance command wrappers.
//
// Surface five high-value calculations from StingTools.Standards:
//   CalcCableSize      — BS 7671 / IEC 60364 / NEC 310 cable sizing
//   CalcWindLoad        — ASCE 7 / Eurocode EN 1991-1-4 / BS 6399-2
//   CalcLighting        — CIBSE / EN 12464-1 / IES lux calculation
//   CalcCoolingLoad     — ASHRAE / CIBSE Guide A HVAC sizing
//   CalcEgress          — IBC / NFPA 101 / BS 9999 occupant load + egress
//   DesignSprinkler     — NFPA 13 / BS EN 12845 hydraulic design
//
// Each wrapper collects inputs via WPF input dialog, invokes the
// Standards API, and renders typed results through StingResultPanel.

using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Standards;
using StingTools.UI;

namespace StingTools.Commands.Standards
{
    // ── Small shared WPF input prompt ─────────────────────────────
    internal static class NumericPrompt
    {
        public static bool TryAsk(string title, string[] labels, double[] defaults, out double[] values)
        {
            values = defaults;
            var w = new Window
            {
                Title = title, Width = 420, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            for (int i = 0; i < labels.Length + 1; i++)
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(120) });

            var boxes = new System.Windows.Controls.TextBox[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                var lbl = new System.Windows.Controls.Label { Content = labels[i] };
                System.Windows.Controls.Grid.SetRow(lbl, i);
                System.Windows.Controls.Grid.SetColumn(lbl, 0);
                grid.Children.Add(lbl);

                var box = new System.Windows.Controls.TextBox { Text = defaults[i].ToString() };
                System.Windows.Controls.Grid.SetRow(box, i);
                System.Windows.Controls.Grid.SetColumn(box, 1);
                grid.Children.Add(box);
                boxes[i] = box;
            }

            var ok = new System.Windows.Controls.Button { Content = "Calculate", Margin = new Thickness(0,8,0,0), Height = 28 };
            System.Windows.Controls.Grid.SetRow(ok, labels.Length);
            System.Windows.Controls.Grid.SetColumn(ok, 0);
            System.Windows.Controls.Grid.SetColumnSpan(ok, 2);
            grid.Children.Add(ok);
            w.Content = grid;

            bool accepted = false;
            ok.Click += (_, __) =>
            {
                var parsed = new double[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    if (!double.TryParse(boxes[i].Text, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out parsed[i]))
                    {
                        TaskDialog.Show(title, $"'{labels[i]}' must be a number.");
                        return;
                    }
                }
                values = parsed;
                accepted = true;
                w.DialogResult = true;
                w.Close();
            };

            try { new System.Windows.Interop.WindowInteropHelper(w).Owner =
                System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
            catch (Exception ex) { StingLog.Warn($"NumericPrompt owner: {ex.Message}"); }
            w.ShowDialog();
            return accepted;
        }
    }
}

namespace StingTools.Commands.Standards
{
    // ═════════════════════════════════════════════════════════════
    //  1. Cable sizing (BS 7671 / IEC 60364 / NEC)
    // ═════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcCableSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk(
                "STING Standards — Cable sizing (BS 7671 / IEC 60364 / NEC 310)",
                new[] { "Voltage (V)", "Current (A)", "Cable run length (m)", "Conduit fill (circuits)", "Ambient °C" },
                new[] { 230.0,         100.0,          50.0,                    3.0,                       30.0 },
                out var v)) return Result.Cancelled;

            CableSizeResult res;
            try
            {
                res = StandardsAPI.CalculateCableSize(
                    voltageV: v[0], currentA: v[1], lengthM: v[2],
                    conductorType: "Copper", insulationType: "THHN",
                    conduitFill: (int)v[3], ambientTempC: v[4], standard: "IEC60364");
            }
            catch (Exception ex) { StingLog.Error("CalcCableSize failed", ex); message = ex.Message; return Result.Failed; }

            var panel = StingResultPanel.Create("Cable sizing — BS 7671 / IEC 60364");
            panel.SetSubtitle(res.Success ? (res.IsNECCompliant ? "COMPLIANT" : "REVIEW") : "FAILED");
            panel.AddSection("RESULT")
                 .Metric("Size", res.SizeAWG ?? "-")
                 .Metric("Required ampacity", $"{res.Ampacity:F1} A")
                 .Metric("Voltage drop",      $"{res.VoltageDropPercent:F2} %")
                 .Metric("Total derating",    $"{res.DeratingFactor:F3}")
                 .Metric("Conductor",         res.ConductorType ?? "-")
                 .Metric("Insulation",        res.InsulationType ?? "-");
            if (res.Warnings != null && res.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in res.Warnings) panel.Text(w);
            }
            if (!string.IsNullOrEmpty(res.NECReference))
                panel.AddSection("STANDARD").Text(res.NECReference);
            panel.Show();
            return Result.Succeeded;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  2. Wind load (ASCE 7 / Eurocode EN 1991-1-4 / BS 6399-2)
    // ═════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcWindLoadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk(
                "STING Standards — Wind load (ASCE 7 / Eurocode 1991-1-4 / BS 6399-2)",
                new[] { "Basic wind speed V (m/s)", "Mean roof height h (m)", "Exposure (1=B, 2=C, 3=D)", "Topographic Kzt", "Gust factor G" },
                new[] { 33.0,                        10.0,                      2.0,                         1.0,             0.85 },
                out var v)) return Result.Cancelled;

            try
            {
                var res = AECCalculations.CalculateWindLoad(
                    windSpeedMps: v[0], heightM: v[1],
                    exposureCategory: v[2] <= 1.5 ? "B" : v[2] <= 2.5 ? "C" : "D",
                    topographicFactor: v[3], gustFactor: v[4]);

                var panel = StingResultPanel.Create("Wind load — ASCE 7 / Eurocode EN 1991-1-4");
                panel.SetSubtitle(res.Standard ?? "");
                panel.AddSection("DESIGN WIND PRESSURE")
                     .Metric("Velocity pressure qz", $"{res.VelocityPressurePa:F0} Pa")
                     .Metric("Design pressure p",    $"{res.DesignPressurePa:F0} Pa")
                     .Metric("Basic wind speed V",   $"{v[0]:F0} m/s")
                     .Metric("Mean roof height h",   $"{v[1]:F1} m")
                     .Metric("Exposure",             v[2] <= 1.5 ? "B" : v[2] <= 2.5 ? "C" : "D")
                     .Metric("Kzt",                  $"{v[3]:F2}")
                     .Metric("Gust factor G",        $"{v[4]:F2}");
                if (res.Notes != null)
                    foreach (var n in res.Notes) panel.Text(n);
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CalcWindLoad failed", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  3. Lighting lux (CIBSE / EN 12464-1 / IES)
    // ═════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcLightingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk(
                "STING Standards — Lighting (CIBSE / EN 12464-1)",
                new[] { "Room area (m²)", "Target illuminance E (lx)", "Luminaire lumens F (lm)", "Utilisation factor UF", "Maintenance factor MF" },
                new[] { 40.0,               500.0,                        4000.0,                     0.60,                   0.80 },
                out var v)) return Result.Cancelled;

            try
            {
                var res = StandardsAPI.CalculateLighting(
                    roomAreaM2: v[0], targetLux: v[1],
                    luminaireLumens: v[2], utilisationFactor: v[3], maintenanceFactor: v[4]);

                var panel = StingResultPanel.Create("Lighting — CIBSE / EN 12464-1");
                panel.SetSubtitle(res.Standard ?? "BS EN 12464-1:2021");
                panel.AddSection("RESULT")
                     .Metric("Luminaires required", res.NumLuminaires.ToString())
                     .Metric("Design illuminance",  $"{res.DesignLux:F0} lx")
                     .Metric("Room index K",        $"{res.RoomIndex:F2}")
                     .Metric("UGR (approx)",        res.UGR.ToString("F1"))
                     .Metric("Power density",       $"{res.PowerDensityWm2:F1} W/m²");
                if (res.Recommendations != null)
                    foreach (var r in res.Recommendations) panel.Text(r);
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CalcLighting failed", ex); message = ex.Message; return Result.Failed; }
        }
    }
}

namespace StingTools.Commands.Standards
{
    // ═════════════════════════════════════════════════════════════
    //  4. HVAC cooling load (ASHRAE / CIBSE Guide A)
    // ═════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcCoolingLoadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk(
                "STING Standards — HVAC cooling load (ASHRAE / CIBSE Guide A)",
                new[] { "Floor area (m²)", "People", "Equipment W/m²", "Lighting W/m²", "Outdoor °C", "Indoor °C" },
                new[] { 100.0,              10.0,     15.0,             10.0,             35.0,         24.0 },
                out var v)) return Result.Cancelled;

            try
            {
                var res = StandardsAPI.CalculateCoolingLoad(
                    floorAreaM2: v[0], numberOfPeople: (int)v[1],
                    equipmentWm2: v[2], lightingWm2: v[3],
                    outdoorTempC: v[4], indoorTempC: v[5], buildingType: "Office");

                var panel = StingResultPanel.Create("HVAC cooling load — ASHRAE / CIBSE Guide A");
                panel.SetSubtitle(res.Standard ?? "ASHRAE 62.1 + Guide A");
                panel.AddSection("LOAD BREAKDOWN (W)")
                     .Metric("Envelope",   $"{res.EnvelopeLoadW:F0}")
                     .Metric("Ventilation",$"{res.VentilationLoadW:F0}")
                     .Metric("Occupancy",  $"{res.OccupancyLoadW:F0}")
                     .Metric("Equipment",  $"{res.EquipmentLoadW:F0}")
                     .Metric("Lighting",   $"{res.LightingLoadW:F0}")
                     .Metric("Sensible",   $"{res.SensibleLoadW:F0}")
                     .Metric("Latent",     $"{res.LatentLoadW:F0}");
                panel.AddSection("TOTALS")
                     .Metric("Total cooling", $"{res.TotalCoolingLoadKW:F1} kW / {res.TotalCoolingLoadTons:F1} tons");
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CalcCoolingLoad failed", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  5. Egress & travel distance (IBC / NFPA 101 / BS 9999)
    // ═════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalcEgressCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk(
                "STING Standards — Egress width + travel distance (IBC / NFPA 101)",
                new[] { "Floor area (m²)", "Occupancy load factor (m²/occupant)", "Story number", "Sprinklered? (0/1)" },
                new[] { 500.0,              9.3,                                     1.0,           1.0 },
                out var v)) return Result.Cancelled;

            try
            {
                var occ = AECCalculations.CalculateOccupantLoad(
                    floorAreaM2: v[0], loadFactorM2PerOccupant: v[1], occupancyType: "B-Business");
                var eg = AECCalculations.CalculateEgressWidth(
                    occupantLoad: occ.OccupantLoad, isStair: false,
                    factorMmPerOccupant: 5.1, minimumWidthMm: 915);
                var td = AECCalculations.CalculateTravelDistance(
                    occupancyType: "B-Business", isSprinklered: v[3] >= 0.5,
                    storyNumber: (int)v[2]);

                var panel = StingResultPanel.Create("Egress + travel distance — IBC / NFPA 101");
                panel.SetSubtitle($"Building occupancy {occ.OccupantLoad}, sprinklered: {(v[3] >= 0.5 ? "yes" : "no")}");

                panel.AddSection("OCCUPANT LOAD")
                     .Metric("Occupant load", occ.OccupantLoad.ToString())
                     .Metric("Area",          $"{v[0]:F0} m²")
                     .Metric("Load factor",   $"{v[1]:F1} m²/occ");

                panel.AddSection("EGRESS WIDTH")
                     .Metric("Required width", $"{eg.RequiredWidthMm:F0} mm")
                     .Metric("Min door width", $"{eg.MinimumDoorWidthMm:F0} mm")
                     .Metric("Min corridor",   $"{eg.MinimumCorridorWidthMm:F0} mm");

                panel.AddSection("TRAVEL DISTANCE")
                     .Metric("Max allowed",     $"{td.MaxTravelDistanceM:F0} m")
                     .Metric("Common path",     $"{td.CommonPathDistanceM:F0} m")
                     .Metric("Dead-end max",    $"{td.DeadEndCorridorMaxM:F0} m");

                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("CalcEgress failed", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  6. Sprinkler design (NFPA 13 / BS EN 12845)
    // ═════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DesignSprinklerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            if (!NumericPrompt.TryAsk(
                "STING Standards — Sprinkler design (NFPA 13 / BS EN 12845)",
                new[] { "Hazard class (1=LH, 2=OH1, 3=OH2, 4=HH1)", "Design area (m²)", "Density (mm/min)", "K-factor" },
                new[] { 2.0,                                          140.0,              5.0,                80.0 },
                out var v)) return Result.Cancelled;

            try
            {
                var criteria = new SprinklerDesignCriteria
                {
                    HazardClass     = v[0] <= 1.5 ? "LH" : v[0] <= 2.5 ? "OH1" : v[0] <= 3.5 ? "OH2" : "HH1",
                    DesignAreaM2    = v[1],
                    DensityMmPerMin = v[2],
                    KFactor         = v[3]
                };
                var res = StandardsAPI.DesignSprinklerSystem(criteria);

                var panel = StingResultPanel.Create("Sprinkler design — NFPA 13 / BS EN 12845");
                panel.SetSubtitle(res.Standard ?? "NFPA 13 / BS EN 12845");
                panel.AddSection("HYDRAULIC DEMAND")
                     .Metric("Hazard class",        criteria.HazardClass)
                     .Metric("Design area",         $"{criteria.DesignAreaM2:F0} m²")
                     .Metric("Density",             $"{criteria.DensityMmPerMin:F1} mm/min")
                     .Metric("Total flow required", $"{res.TotalFlowLpm:F0} L/min")
                     .Metric("Operating pressure",  $"{res.OperatingPressureBar:F1} bar")
                     .Metric("Sprinkler count",     res.NumberOfSprinklers.ToString());
                if (res.Warnings != null)
                    foreach (var w in res.Warnings) panel.Text(w);
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("DesignSprinkler failed", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
