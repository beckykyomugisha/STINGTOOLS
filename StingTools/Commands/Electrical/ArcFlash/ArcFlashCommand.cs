using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.Coordination;
using StingTools.Commands.Electrical.FaultCurrent;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical.ArcFlash
{
    public class ArcFlashRow
    {
        public string PanelName              { get; set; } = "";
        public double FaultKa                { get; set; }
        public double IncidentEnergy_CalCm2  { get; set; }
        public double BoundaryMm             { get; set; }
        public int    PpeCategory            { get; set; }
        public double WorkDistMm             { get; set; }
        public string LabelText              { get; set; } = "";
        public double VoltageV               { get; set; }
        public double ClearingTimeMs         { get; set; }
        public string EquipmentClass         { get; set; } = "";
        /// <summary>Calculation basis — always <see cref="ArcFlashEngine.Basis"/>.</summary>
        public string Basis                  { get; set; } = ArcFlashEngine.Basis;
    }

    /// <summary>
    /// Arc-flash incident energy per panel by the IEEE 1584-2002 LV method
    /// (<see cref="ArcFlashEngine"/>). Indicative only — every value written carries
    /// <see cref="ArcFlashEngine.Basis"/>. Panels that cannot be calculated honestly
    /// (no voltage, voltage outside 208–1000 V, fault outside 0.7–106 kA, no clearing
    /// time) are stamped NOT CALCULATED and get no numbers; stale values from earlier
    /// runs are overwritten.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArcFlashCommand : IExternalCommand
    {
        /// <summary>Calculated panels only — not-calculated panels are never exported as values.</summary>
        public static List<ArcFlashRow> LastResults { get; private set; } = new List<ArcFlashRow>();

        private const string NotApplicable = "N/A";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var faultResults = FaultCurrentCommand.LastResults;
            if (faultResults == null || faultResults.Count == 0)
            {
                TaskDialog.Show("STING Arc Flash",
                    "Run Fault Current Propagation (Phase 178) first.\n" +
                    "Arc-flash calculation requires a fault level at each panel.");
                return Result.Cancelled;
            }

            var optDlg = new TaskDialog("STING Arc Flash — Options")
            {
                MainInstruction = "Clearing time source",
                MainContent =
                    "Method: " + ArcFlashEngine.Basis + ".\n\n" +
                    "Choose the protective-device clearing time used for the arc duration.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fixed 100 ms (assumed — you must confirm the device clears in 100 ms)");
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Per panel from the main breaker's IEC 60898 band",
                "Upper edge of the generic MCB band at Ia and 0.85·Ia. MCCB / ACB have no generic curve → NOT CALCULATED.");
            var sel = optDlg.Show();
            bool useFixed;
            if (sel == TaskDialogResult.CommandLink1) useFixed = true;
            else if (sel == TaskDialogResult.CommandLink2) useFixed = false;
            else return Result.Cancelled;
            const double fixedClearingS = 0.100;

            var tcc = useFixed ? null : TccDatabaseLoader.Load(null);

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            var byPanelId = faultResults
                .Where(r => r.PanelId is ElementId)
                .ToDictionary(r => ((ElementId)r.PanelId).Value, r => r);

            var results = new List<ArcFlashRow>();
            var notCalculated = new List<string>();
            using (var tx = new Transaction(doc, "STING Arc Flash (IEEE 1584-2002 indicative)"))
            {
                tx.Start();
                foreach (var panel in panels)
                {
                    string reason;
                    if (!byPanelId.TryGetValue(panel.Id.Value, out var fr))
                    {
                        reason = "no fault level from Fault Current Propagation";
                        StampNotCalculated(panel, reason, notCalculated);
                        continue;
                    }

                    // IEEE 1584-2002 models three-phase arcs only. The fault study now
                    // returns the line-to-neutral Ik1 for single-phase boards; feeding
                    // that in as a 3-phase bolted fault understates the energy.
                    if (fr.Phases != 3)
                    {
                        reason = fr.Phases == 1
                            ? "single-phase board — IEEE 1584-2002 models three-phase arcs only"
                            : "phase count unknown — cannot confirm a three-phase arc";
                        StampNotCalculated(panel, reason, notCalculated);
                        continue;
                    }

                    // Line-to-line voltage from the fault study (the same voltage its Ik
                    // was computed at); the equipment parameter can hold L-N (230 V).
                    string vSource = "fault study L-L";
                    double voltageV = LeadingNumber(fr.Voltage);
                    if (voltageV <= 0) voltageV = PanelVoltageV(doc, panel, out vSource);
                    var cls = ClassifyEquipment(panel);

                    double overrideMm = ReadWorkingDistanceOverride(panel);
                    var input = new ArcFlashInput
                    {
                        BoltedFaultKa = fr.FaultKa,
                        VoltageV = voltageV,
                        EquipmentClass = cls,
                        WorkingDistanceMm = overrideMm,   // 0 → class default
                        SolidlyGrounded = false,          // conservative until earthing is confirmed
                        ClearingTimeS = fixedClearingS
                    };

                    ArcFlashResult r;
                    string tSource;
                    if (useFixed)
                    {
                        r = ArcFlashEngine.Calculate(input);
                        tSource = "fixed 100 ms — assumed";
                    }
                    else
                    {
                        var band = ResolveMainBreakerBand(panel, tcc, out string brkLabel);
                        if (band == null || !band.HasBand)
                        {
                            r = new ArcFlashResult
                            {
                                Calculated = false,
                                NotCalculatedReason = band == null
                                    ? "main breaker rating not set"
                                    : $"no curve data for main breaker '{brkLabel}' ({band.Curve}) — manufacturer TCC required"
                            };
                        }
                        else
                        {
                            r = ArcFlashEngine.Calculate(input, iaKa =>
                            {
                                double t = band.MaxClearTimeS(iaKa * 1000.0);
                                return double.IsInfinity(t) ? double.NaN : t;
                            });
                        }
                        // DeviceBand.ToString appends "(curve assumed)" when the curve letter
                        // came from the TCC database's generic default rather than the
                        // breaker label — so it reaches tSource and the stamped label.
                        tSource = $"{band} max-clear, {IecMcbBands.Basis}";
                    }

                    if (!r.Calculated)
                    {
                        StampNotCalculated(panel, r.NotCalculatedReason, notCalculated);
                        continue;
                    }

                    string lbl = ArcFlashEngine.FormatLabel(panel.Name, voltageV, cls, r, tSource)
                                 + $"\nIbf: {fr.FaultKa:0.##} kA (STING fault engine — unverified); V from {vSource}";
                    if (r.Notes.Count > 0) lbl += "\nNotes: " + string.Join("; ", r.Notes);

                    var inv = CultureInfo.InvariantCulture;
                    try
                    {
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_IE,    r.IncidentEnergyCalCm2.ToString("0.00", inv), overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_BD,    r.BoundaryMm.ToString("0", inv),              overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_PPE,   r.PpeCategory.ToString(inv),                  overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_WD,    r.WorkingDistanceMm.ToString("0", inv),       overwrite: true);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_LABEL, lbl,                                          overwrite: true);
                    }
                    catch (Exception ex) { StingLog.Warn($"Stamp arc flash on {panel.Name}: {ex.Message}"); }

                    ApplyPpeColorOverride(doc, panel, r.PpeCategory);
                    results.Add(new ArcFlashRow
                    {
                        PanelName = panel.Name, FaultKa = fr.FaultKa,
                        IncidentEnergy_CalCm2 = r.IncidentEnergyCalCm2, BoundaryMm = r.BoundaryMm,
                        PpeCategory = r.PpeCategory, WorkDistMm = r.WorkingDistanceMm, LabelText = lbl,
                        VoltageV = voltageV, ClearingTimeMs = r.GoverningClearingTimeS * 1000.0,
                        EquipmentClass = cls.ToString()
                    });
                }
                tx.Commit();
            }
            LastResults = results;
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            StingLog.Info($"ArcFlash ({ArcFlashEngine.BasisShort}): {results.Count} calculated, {notCalculated.Count} not calculated.");
            int dangerous = results.Count(r => r.PpeCategory < 0);
            int cat4 = results.Count(r => r.PpeCategory == 4);
            TaskDialog.Show("STING Arc Flash",
                $"Method: {ArcFlashEngine.Basis}.\n\n" +
                $"{results.Count} panel(s) calculated, {notCalculated.Count} NOT CALCULATED.\n" +
                $"  {dangerous} exceed 40 cal/cm²; {cat4} are Category 4.\n\n" +
                (notCalculated.Count > 0
                    ? "Not calculated (first 10):\n  " + string.Join("\n  ", notCalculated.Take(10)) + "\n\n"
                    : "") +
                "Fault levels come from the STING IEC 60909-style LV fault engine, which carries " +
                "labelled assumptions (source X/R, cable reactance, feeder lengths) and has not " +
                "been verified in a live model — these energies are indicative and must not be " +
                "used to specify PPE without a licensed study.\n\n" +
                "Run 'Arc Flash Labels' to generate the label drafting view.");
            return Result.Succeeded;
        }

        /// <summary>Overwrite any earlier values so a stale (or fabricated) figure is never left behind.</summary>
        private static void StampNotCalculated(FamilyInstance panel, string reason, List<string> log)
        {
            log.Add($"{panel.Name}: {reason}");
            try
            {
                var r = new ArcFlashResult { Calculated = false, NotCalculatedReason = reason };
                string lbl = ArcFlashEngine.FormatLabel(panel.Name, 0, ArcEquipmentClass.PanelMcc, r, "");
                ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_IE,    NotApplicable, overwrite: true);
                ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_BD,    NotApplicable, overwrite: true);
                ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_PPE,   NotApplicable, overwrite: true);
                ParameterHelpers.SetString(panel, ParamRegistry.ELC_ARC_FLASH_LABEL, lbl,           overwrite: true);
            }
            catch (Exception ex) { StingLog.Warn($"Stamp arc flash N/A on {panel.Name}: {ex.Message}"); }
        }

        /// <summary>
        /// System voltage in volts, read through <see cref="ElecUnits"/> (never a raw
        /// internal value, never a hard-coded default). RBS_ELEC_VOLTAGE first, then the
        /// panel's distribution system line-to-line voltage. 0 when unknown.
        /// </summary>
        /// <summary>Leading number of a string like "400V 3ph"; 0 when none.</summary>
        private static double LeadingNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"^\s*(\d+(?:\.\d+)?)");
            return m.Success && double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        private static double PanelVoltageV(Document doc, FamilyInstance panel, out string source)
        {
            double v = ElecUnits.Volts(panel);
            if (v > 0) { source = "equipment voltage"; return v; }
            try
            {
                var dsId = panel.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_DISTRIBUTION_SYSTEM)?.AsElementId();
                if (dsId != null && dsId != ElementId.InvalidElementId
                    && doc.GetElement(dsId) is DistributionSysType ds)
                {
                    if (ds.VoltageLineToLine != null)
                    {
                        v = ElecUnits.VoltsFromInternal(ds.VoltageLineToLine.ActualValue);
                        if (v > 0) { source = "distribution system L-L"; return v; }
                    }
                    if (ds.VoltageLineToGround != null)
                    {
                        v = ElecUnits.VoltsFromInternal(ds.VoltageLineToGround.ActualValue);
                        if (v > 0) { source = "distribution system L-G (no L-L defined)"; return v; }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ArcFlash voltage {panel.Name}: {ex.Message}"); }
            source = "unknown";
            return 0;
        }

        /// <summary>
        /// Switchgear / switchboard by name → IEEE 1584-2002 switchgear class; everything
        /// else → panelboard / MCC (the closer 455 mm working distance, which is the
        /// conservative choice when the equipment type is not stated).
        /// </summary>
        private static ArcEquipmentClass ClassifyEquipment(FamilyInstance panel)
        {
            string n = ((panel.Symbol?.FamilyName ?? "") + " " + (panel.Name ?? "")).ToLowerInvariant();
            return n.Contains("switchgear") || n.Contains("switchboard")
                ? ArcEquipmentClass.Switchgear
                : ArcEquipmentClass.PanelMcc;
        }

        /// <summary>Per-equipment working-distance override (ELC_ARC_FLASH_WORK_DIST_MM); 0 = class default.</summary>
        private static double ReadWorkingDistanceOverride(FamilyInstance panel)
        {
            try
            {
                var p = panel.LookupParameter("ELC_ARC_FLASH_WORK_DIST_MM");
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String
                    && double.TryParse(p.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ov))
                    return ov;
            }
            catch (Exception ex) { StingLog.Warn($"ArcFlash WD override {panel.Name}: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// The panel main breaker as a band: the text label (e.g. "C63", "250A MCCB") if
        /// set, else the numeric rating resolved against the TCC database. Null when no
        /// rating is recorded at all.
        /// </summary>
        private static DeviceBand ResolveMainBreakerBand(FamilyInstance panel, TccDatabase tcc, out string label)
        {
            label = "";
            try
            {
                foreach (string name in new[] { "ELC_PANEL_MAIN_BREAKER_TXT", "ELC_PNL_MAIN_BRK_TXT" })
                {
                    string s = ParameterHelpers.GetString(panel, name);
                    if (!string.IsNullOrWhiteSpace(s)) { label = s.Trim(); return tcc.ResolveBand(label); }
                }
                var p = panel.LookupParameter(ParamRegistry.ELC_MAIN_BRK);
                if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                {
                    label = $"{p.AsDouble().ToString("0", CultureInfo.InvariantCulture)}A";
                    return tcc.ResolveBand(label);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ArcFlash main breaker {panel.Name}: {ex.Message}"); }
            return null;
        }

        private static void ApplyPpeColorOverride(Document doc, Element el, int ppe)
        {
            try
            {
                var view = doc.ActiveView;
                if (view == null) return;
                var solidFill = ParameterHelpers.GetSolidFillPattern(doc);
                if (solidFill == null) return;
                Color c = ppe switch
                {
                    < 0 => new Color(180, 0, 0),
                    4   => new Color(255, 64, 64),
                    3   => new Color(255, 140, 0),
                    2   => new Color(255, 210, 0),
                    _   => new Color(0, 200, 80)
                };
                var ogs = new OverrideGraphicSettings();
                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                ogs.SetSurfaceForegroundPatternColor(c);
                view.SetElementOverrides(el.Id, ogs);
            }
            catch (Exception ex) { StingLog.Warn($"ArcFlash colour override: {ex.Message}"); }
        }
    }
}
