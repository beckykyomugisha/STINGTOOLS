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
            // Local holder — 'out values' can't be captured inside the Click lambda (CS1628).
            double[] localValues = defaults;
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
                localValues = parsed;
                accepted = true;
                w.DialogResult = true;
                w.Close();
            };

            try { new System.Windows.Interop.WindowInteropHelper(w).Owner =
                System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
            catch (Exception ex) { StingLog.Warn($"NumericPrompt owner: {ex.Message}"); }
            w.ShowDialog();
            values = localValues;
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
                // Region-aware: pull the active electrical standard from the project
                // (BS 7671 for UK, NEC 310 for US, IEC 60364 for International, etc.).
                string elecStd = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Electrical);
                res = StandardsAPI.CalculateCableSize(
                    voltageV: v[0], currentA: v[1], lengthM: v[2],
                    conductorType: "Copper", insulationType: "THHN",
                    conduitFill: (int)v[3], ambientTempC: v[4], standard: elecStd);
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
                // Library signature: (basicWindSpeedMPH, exposureCategory, buildingHeightFt, riskCategory)
                double mph = v[0] * 2.2369;
                double ft  = v[1] * 3.2808;
                string exp = v[2] <= 1.5 ? "B" : v[2] <= 2.5 ? "C" : "D";
                var res = AECCalculations.CalculateWindLoad(
                    basicWindSpeedMPH: mph, exposureCategory: exp,
                    buildingHeightFt: ft, riskCategory: "II");

                var panel = StingResultPanel.Create("Wind load — ASCE 7 / Eurocode EN 1991-1-4");
                panel.SetSubtitle(res.StandardReference ?? "");
                panel.AddSection("DESIGN WIND PRESSURE")
                     .Metric("Basic wind speed",     $"{res.BasicWindSpeed:F0} mph ({v[0]:F0} m/s)")
                     .Metric("Velocity pressure qz", $"{res.VelocityPressureKPa * 1000:F0} Pa ({res.VelocityPressurePSF:F1} psf)")
                     .Metric("Windward p",           $"{res.WindwardPressurePSF:F1} psf")
                     .Metric("Leeward p",            $"{res.LeewardPressurePSF:F1} psf")
                     .Metric("Mean roof height h",   $"{v[1]:F1} m")
                     .Metric("Exposure",             res.ExposureCategory ?? exp)
                     .Metric("Kz",                    $"{res.Kz:F2}")
                     .Metric("Kzt",                   $"{res.Kzt:F2}")
                     .Metric("Kd",                    $"{res.Kd:F2}");
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
                new[] { "Floor area (m²)", "Ceiling height (m)", "Space type (1=Office, 2=Class, 3=Retail)" },
                new[] { 40.0,                2.7,                   1.0 },
                out var v)) return Result.Cancelled;

            try
            {
                string space = v[2] <= 1.5 ? "Office" : v[2] <= 2.5 ? "Classroom" : "Retail";
                var res = StandardsAPI.CalculateLighting(
                    floorAreaM2: v[0], spaceType: space, ceilingHeightM: v[1]);

                var panel = StingResultPanel.Create("Lighting — CIBSE / EN 12464-1");
                panel.SetSubtitle(res.CIBSEReference ?? "BS EN 12464-1:2021");
                panel.AddSection("RESULT")
                     .Metric("Space type",           res.SpaceType ?? space)
                     .Metric("Illuminance target",   $"{res.IlluminanceLux:F0} lx ({res.IlluminanceFootcandles:F0} fc)")
                     .Metric("Total lumens required",$"{res.TotalLumensRequired:F0} lm")
                     .Metric("Power density",        $"{res.PowerDensityWM2:F1} W/m²")
                     .Metric("Measurement plane",     res.MeasurementPlane ?? "-");
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
                new[] { "Floor area (m²)", "People", "Equipment W/m²", "Lighting W/m²", "Ceiling height (m)" },
                new[] { 100.0,              10.0,     15.0,             10.0,             2.7 },
                out var v)) return Result.Cancelled;

            try
            {
                // Library signature: (floorAreaM2, buildingType, climateZone, occupantCount, equipmentLoadW, lightingLoadW, orientation, ceilingHeightM)
                double equipW  = v[2] * v[0];
                double lightW  = v[3] * v[0];
                var res = StandardsAPI.CalculateCoolingLoad(
                    floorAreaM2: v[0], buildingType: "Office", climateZone: "3A",
                    occupantCount: v[1],
                    equipmentLoadW: equipW, lightingLoadW: lightW,
                    orientationN_E_S_W: "N", ceilingHeightM: v[4]);

                var panel = StingResultPanel.Create("HVAC cooling load — ASHRAE / CIBSE Guide A");
                panel.SetSubtitle(res.CIBSEReference ?? "ASHRAE 62.1 + CIBSE Guide A");
                panel.AddSection("LOADS")
                     .Metric("Sensible",      $"{res.SensibleLoadKW:F1} kW")
                     .Metric("Latent",        $"{res.LatentLoadKW:F1} kW")
                     .Metric("Total cooling", $"{res.CoolingLoadKW:F1} kW ({res.CoolingLoadKW / 3.517:F1} tons)")
                     .Metric("Heating (est.)",$"{res.HeatingLoadKW:F1} kW")
                     .Metric("Ventilation",   $"{res.VentilationLPS:F0} L/s");
                if (!string.IsNullOrEmpty(res.RecommendedSystem))
                    panel.AddSection("RECOMMENDATION").Text(res.RecommendedSystem);
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
                // Library signatures:
                //   CalculateOccupantLoad(floorAreaSqFt, occupancyType, standard)
                //   CalculateEgressWidth(occupantLoad, egressComponent, sprinklered, standard)
                //   CalculateTravelDistance(occupancyGroup, sprinklered, standard)
                double areaSqFt = v[0] * 10.7639;
                var occ = AECCalculations.CalculateOccupantLoad(
                    floorAreaSqFt: areaSqFt, occupancyType: "B-Business");
                var eg = AECCalculations.CalculateEgressWidth(
                    occupantLoad: occ.OccupantLoad,
                    egressComponent: "Corridor",
                    sprinklered: v[3] >= 0.5);
                var td = AECCalculations.CalculateTravelDistance(
                    occupancyGroup: "B", sprinklered: v[3] >= 0.5);

                var panel = StingResultPanel.Create("Egress + travel distance — IBC / NFPA 101");
                panel.SetSubtitle($"Occupancy {occ.OccupantLoad}, sprinklered: {(v[3] >= 0.5 ? "yes" : "no")}");

                panel.AddSection("OCCUPANT LOAD")
                     .Metric("Occupant load", occ.OccupantLoad.ToString())
                     .Metric("Area",          $"{v[0]:F0} m² ({areaSqFt:F0} ft²)")
                     .Metric("Load factor",   $"{occ.LoadFactor:F1}")
                     .Metric("Ref",           occ.StandardReference ?? "-");

                panel.AddSection("EGRESS WIDTH")
                     .Metric("Required width", $"{eg.RequiredWidthMM:F0} mm ({eg.RequiredWidthInches:F1} in)")
                     .Metric("Min width",       $"{eg.MinimumWidthInches:F1} in")
                     .Metric("Exits required",  eg.ExitsRequired.ToString());

                panel.AddSection("TRAVEL DISTANCE")
                     .Metric("Max travel",   $"{td.MaxTravelDistanceM:F0} m ({td.MaxTravelDistanceFt:F0} ft)")
                     .Metric("Occupancy",    td.OccupancyGroup ?? "B")
                     .Metric("Ref",           td.StandardReference ?? "-");

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
                new[] { "Floor area (m²)", "Hazard (1=Light, 2=OrdGrp1, 3=OrdGrp2, 4=Extra)" },
                new[] { 500.0,              2.0 },
                out var v)) return Result.Cancelled;

            try
            {
                // Library signature: (floorAreaM2, occupancyType, hazardClassification, standard)
                string hazard = v[1] <= 1.5 ? "Light" : v[1] <= 2.5 ? "OrdinaryGroup1" :
                                v[1] <= 3.5 ? "OrdinaryGroup2" : "ExtraHazard";
                string fireStd = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.FireProtection);
                var res = StandardsAPI.DesignSprinklerSystem(
                    floorAreaM2: v[0], occupancyType: "Office",
                    hazardClassification: hazard, standard: fireStd);

                var panel = StingResultPanel.Create("Sprinkler design — NFPA 13 / BS EN 12845");
                panel.SetSubtitle(res.NFPAReference ?? "NFPA 13 / BS EN 12845");
                panel.AddSection("HYDRAULIC DEMAND")
                     .Metric("Hazard class",       res.HazardClass ?? hazard)
                     .Metric("Occupancy",           res.OccupancyType ?? "Office")
                     .Metric("Design area",         $"{res.DesignAreaFt2:F0} ft² ({res.DesignAreaFt2 * 0.0929:F0} m²)")
                     .Metric("Design density",      $"{res.DesignDensity:F3} gpm/ft²")
                     .Metric("Design heads",        res.DesignHeads.ToString())
                     .Metric("Total heads",         res.NumberOfHeads.ToString())
                     .Metric("Flow rate",           $"{res.FlowRateGPM:F0} gpm ({res.FlowRateGPM * 3.785:F0} L/min)")
                     .Metric("Hose stream",         $"{res.HoseStreamGPM:F0} gpm");
                if (!res.Success && !string.IsNullOrEmpty(res.ErrorMessage))
                    panel.Text(res.ErrorMessage);
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("DesignSprinkler failed", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
