// StingTools — HVAC commissioning checklist generator.
//
// Scans the project for mechanical equipment and emits an ASHRAE
// Guideline 0 / CIBSE TM39 pre-Cx + functional Cx checklist as a CSV
// in <project>/_BIM_COORD/cx/ (created on demand). Each equipment row
// gets a class-specific task list driven by `_taskLibrary` below; the
// CSV columns are designed to drop straight into a commissioning
// agent's witnessing form.
//
// Output columns:
//   Tag, Family, Type, System, Class, Phase, Task, Method, Acceptance,
//   PassFail, Date, Signature
//
// "Phase" is one of:
//   PreInstall — drawings / data sheets / submittals
//   PreStartup — physical install + connections
//   Startup    — first energize / fill / vent
//   Functional — sequence-of-operation test under varied loads
//   Handover   — TAB report, O&M, training, warranty
//
// Tasks are conservative defaults; projects can append rows by
// dropping <project>/_BIM_COORD/cx/cx_tasks_override.json.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacGenerateCxChecklistCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var equipment = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
                if (equipment.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Commissioning Checklist",
                        "No mechanical equipment found in the project.");
                    return Result.Cancelled;
                }

                // Output directory
                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projDir))
                {
                    TaskDialog.Show("STING HVAC", "Save the project before generating the Cx checklist.");
                    return Result.Cancelled;
                }
                string cxDir = Path.Combine(projDir, "_BIM_COORD", "cx");
                Directory.CreateDirectory(cxDir);
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string csvPath = Path.Combine(cxDir, $"cx_checklist_{ts}.csv");

                int rows = 0;
                var byClass = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var sb = new StringBuilder();
                sb.AppendLine("Tag,Family,Type,System,Class,Phase,Task,Method,Acceptance,PassFail,Date,Signature");
                foreach (var fi in equipment)
                {
                    try
                    {
                        string tag    = fi.LookupParameter("ASS_TAG_1")?.AsString() ?? $"#{fi.Id.Value}";
                        string family = fi.Symbol?.Family?.Name ?? fi.Name ?? "";
                        string type   = fi.Symbol?.Name ?? "";
                        string system = SystemNameOf(fi);
                        string cls    = ClassifyEquipment(family, type);
                        byClass[cls]  = byClass.TryGetValue(cls, out var n) ? n + 1 : 1;

                        foreach (var task in _taskLibrary.GetValueOrDefault(cls) ?? _taskLibrary["Generic"])
                        {
                            sb.AppendLine(string.Join(",",
                                Esc(tag), Esc(family), Esc(type), Esc(system), Esc(cls),
                                Esc(task.Phase), Esc(task.Task), Esc(task.Method),
                                Esc(task.Acceptance), "", "", ""));
                            rows++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Cx row {fi.Id}: {ex.Message}"); }
                }
                File.WriteAllText(csvPath, sb.ToString());

                var panel = StingResultPanel.Create("HVAC — Commissioning Checklist");
                panel.SetSubtitle($"{equipment.Count} equipment / {rows} Cx tasks / {byClass.Count} classes");
                panel.AddSection("OUTPUT")
                     .Metric("CSV", csvPath)
                     .Metric("Rows", rows.ToString());

                panel.AddSection("BY EQUIPMENT CLASS");
                foreach (var kv in byClass.OrderByDescending(k => k.Value))
                {
                    int taskCount = (_taskLibrary.GetValueOrDefault(kv.Key) ?? _taskLibrary["Generic"]).Count;
                    panel.Metric(kv.Key, $"{kv.Value} units × {taskCount} tasks");
                }
                panel.Text("Aligned to ASHRAE Guideline 0-2019 + CIBSE TM39 phases " +
                           "(PreInstall / PreStartup / Startup / Functional / Handover). " +
                           "Drop the CSV into the commissioning agent's witnessing form, " +
                           "sign off PassFail + Date per row.");
                panel.Show();
                try { StingHvacPanel.Instance?.PushRunRow($"Cx checklist ({rows} rows → {Path.GetFileName(csvPath)})", "⬤"); }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacGenerateCxChecklistCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Equipment classification ────────────────────────────────

        private static string ClassifyEquipment(string family, string type)
        {
            string s = ($"{family} {type}").ToLowerInvariant();
            if (s.Contains("ahu") || s.Contains("air handl")) return "AHU";
            if (s.Contains("vrv") || s.Contains("vrf"))       return "VRF";
            if (s.Contains("chiller"))                         return "Chiller";
            if (s.Contains("boiler"))                          return "Boiler";
            if (s.Contains("pump"))                            return "Pump";
            if (s.Contains("fan") || s.Contains("blower"))     return "Fan";
            if (s.Contains("fcu") || s.Contains("fan coil"))   return "FCU";
            if (s.Contains("vav"))                             return "VAV";
            if (s.Contains("heat pump") || s.Contains("hp"))   return "HeatPump";
            if (s.Contains("cooling tower"))                   return "CoolingTower";
            if (s.Contains("heat exch") || s.Contains("hx"))   return "HeatExchanger";
            if (s.Contains("damper"))                          return "Damper";
            return "Generic";
        }

        private static string SystemNameOf(FamilyInstance fi)
        {
            try
            {
                var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) return "";
                foreach (Connector c in conns)
                    if (c?.MEPSystem != null) return c.MEPSystem.Name ?? "";
            }
            catch { }
            return "";
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // ── Cx task library ─────────────────────────────────────────
        // Conservative ASHRAE Guideline 0 + CIBSE TM39 task sets per class.
        // Project teams extend via <project>/_BIM_COORD/cx/cx_tasks_override.json
        // (planned future enhancement; library is hardcoded for now).

        private class CxTask
        {
            public string Phase      = "";
            public string Task       = "";
            public string Method     = "";
            public string Acceptance = "";
            public CxTask(string p, string t, string m, string a) { Phase = p; Task = t; Method = m; Acceptance = a; }
        }

        private static readonly Dictionary<string, List<CxTask>> _taskLibrary
            = new Dictionary<string, List<CxTask>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AHU"] = new()
            {
                new("PreInstall", "Approved submittal received",       "Document review",   "Stamped 'Reviewed' by engineer"),
                new("PreStartup", "Filter media installed correctly",  "Visual inspection", "MERV/EU class matches spec"),
                new("PreStartup", "Coil orientation + drain pan slope","Visual inspection", "Slope ≥ 1:60 to drain"),
                new("PreStartup", "Fan motor megger test",             "Insulation tester", "≥ 1 MΩ at 500 V DC"),
                new("Startup",    "Fan rotation correct",              "Visual + ammeter",  "Rotation matches arrow; FLA ≤ nameplate"),
                new("Startup",    "Drive belt tension",                "Tension gauge",     "Per manufacturer's deflection chart"),
                new("Functional", "Supply air temp tracks setpoint",   "Trend 24 h",        "±1 K of setpoint"),
                new("Functional", "Economiser changeover",             "Override OA temp",  "Damper modulates correctly"),
                new("Functional", "Static pressure reset works",       "Trend 24 h",        "ΔP within band; no hunting"),
                new("Handover",   "TAB report submitted",              "Document review",   "Signed by independent TAB engineer"),
                new("Handover",   "O&M + as-builts handed over",       "Document review",   "Complete + indexed")
            },
            ["Chiller"] = new()
            {
                new("PreInstall", "Approved submittal + IOM received",  "Document review",   "Stamped + indexed"),
                new("PreStartup", "Refrigerant charge verified",        "Sight glass + scales", "Per nameplate ±2%"),
                new("PreStartup", "Vibration isolators installed",      "Visual",            "Loaded per manufacturer"),
                new("PreStartup", "Strainers installed upstream",       "Visual",            "Mesh per spec"),
                new("Startup",    "Manufacturer commissioning visit",   "Witnessed",         "Sign-off received"),
                new("Startup",    "Oil level + heater check",           "Sight glass",       "In band"),
                new("Functional", "Capacity steps load correctly",      "Sequence 0-100%",   "No alarms; chilled water ΔT held"),
                new("Functional", "Safety interlocks tested",           "Forced trip",       "All set-points within spec"),
                new("Handover",   "Refrigerant logbook started",        "F-Gas register",    "Logged + bound"),
                new("Handover",   "Operator training delivered",        "Witnessed session", "Sign-off form")
            },
            ["Boiler"] = new()
            {
                new("PreInstall", "Approved submittal + IOM received",  "Document review",   "Stamped + indexed"),
                new("PreStartup", "Gas tightness test",                 "Manometer / TDR",   "BS 6891 / NFPA 54 pass"),
                new("PreStartup", "Flue terminal location",             "Visual",            "Clearances per IGEM/UP/10"),
                new("Startup",    "Combustion analysis",                "Flue gas analyzer", "CO₂ + O₂ + CO in band"),
                new("Startup",    "Pressure relief discharges correctly","Witnessed lift",    "Discharge to safe location"),
                new("Functional", "Modulation tracks load",              "Trend 24 h",        "Flow temp ±1 K"),
                new("Functional", "Boiler interlocks tested",            "Forced trip",       "Shut-off + alarm in <2 s"),
                new("Handover",   "Gas safety certificate",              "Document review",   "Signed by Gas Safe engineer")
            },
            ["Pump"] = new()
            {
                new("PreInstall", "Approved submittal received",        "Document review",   "Stamped"),
                new("PreStartup", "Alignment shaft to driver",          "Dial indicator",    "≤ 0.05 mm"),
                new("PreStartup", "Suction strainer fitted",            "Visual",            "Removed after first 24 h flush"),
                new("Startup",    "Rotation correct",                   "Visual",            "Matches arrow"),
                new("Startup",    "Bearing temperature",                "IR thermometer",    "≤ 60 °C after 2 h run"),
                new("Functional", "Duty point matches design",          "Pressure + flow",   "±5% of design curve"),
                new("Functional", "VSD ramp + minimum speed",           "BMS trend",         "No cavitation at min speed"),
                new("Handover",   "Mech. seal flush + isolation valves","Visual",            "Operable, labelled")
            },
            ["VRF"] = new()
            {
                new("PreInstall", "Approved system schematic",          "Document review",   "Indoor + outdoor unit list"),
                new("PreStartup", "Pipe insulation continuity",         "Visual",            "No bare runs"),
                new("PreStartup", "Refrigerant leak test",              "Nitrogen 4 MPa 24 h", "No pressure drop"),
                new("PreStartup", "Vacuum dehydration",                 "Vac gauge",         "<500 microns for 1 h"),
                new("Startup",    "Refrigerant additional charge",      "Vendor calc + scales", "Per length table"),
                new("Startup",    "Auto-address per remote",            "Vendor tool",       "All IDUs registered"),
                new("Functional", "Cooling + heating mode each IDU",    "Test each",         "Capacity per zone"),
                new("Handover",   "F-Gas log + service ports",          "Document review",   "Logged; capped")
            },
            ["FCU"] = new()
            {
                new("PreInstall", "Approved submittal received",        "Document review",   "Stamped"),
                new("PreStartup", "Drain pan + condensate trap",        "Visual + water test","No leaks, primed trap"),
                new("Startup",    "Fan speeds + control valve stroke",  "BMS commands",      "All speeds reachable"),
                new("Functional", "Room temperature ±1 K of setpoint",  "Trend 24 h",        "Stable, no hunting"),
                new("Handover",   "Filter access labelled",             "Visual",            "Identifiable + tooled access")
            },
            ["VAV"] = new()
            {
                new("PreInstall", "Box airflow design vs nameplate",    "Document review",   "Within turn-down range"),
                new("PreStartup", "Actuator stroke + zero",             "Visual",            "Full close at 0 %"),
                new("Startup",    "Min + max airflow setpoints set",    "Controller",        "Match design"),
                new("Functional", "Reheat coil staged correctly",       "Trend",             "Within deadband"),
                new("Handover",   "BMS graphic + alarms tested",        "Witnessed",         "Pass")
            },
            ["CoolingTower"] = new()
            {
                new("PreInstall", "Approved submittal + IOM",           "Document review",   "Stamped"),
                new("PreStartup", "Basin filled + chemical dosed",      "Visual + test kit", "pH + conductivity in band"),
                new("PreStartup", "Drift eliminator + screens",         "Visual",            "Clean, no missing panels"),
                new("Startup",    "Fan reversibility test",             "BMS command",       "Functions for de-icing"),
                new("Functional", "Approach temp meets design",         "Trend 24 h",        "Within ±1 K"),
                new("Handover",   "Legionella risk assessment",         "Document review",   "Signed; logbook in place")
            },
            ["HeatPump"] = new()
            {
                new("PreInstall", "Approved submittal + COP curve",     "Document review",   "Stamped"),
                new("PreStartup", "Defrost drain + heater tape",        "Visual + ammeter",  "Heater functional"),
                new("Startup",    "Manufacturer first-start visit",     "Witnessed",         "Sign-off received"),
                new("Functional", "COP at design points",                "Power meter + flow","Within 5 % of catalogue"),
                new("Functional", "Defrost cycle initiates + recovers", "Force cold",        "Returns to heating in <10 min"),
                new("Handover",   "F-Gas logbook + service ports",      "Document review",   "Logged; capped")
            },
            ["Fan"] = new()
            {
                new("PreInstall", "Approved selection vs noise spec",   "Document review",   "≤ NR/NC target"),
                new("PreStartup", "Belt + bearing alignment",           "Visual + gauge",    "Per IOM"),
                new("Startup",    "Rotation + FLA",                     "Visual + ammeter",  "Per nameplate"),
                new("Functional", "Air flow + ΔP at design point",      "Pitot + manometer", "±5 % design"),
                new("Handover",   "Vibration baseline reading",         "Vibrometer",        "Logged for trending")
            },
            ["HeatExchanger"] = new()
            {
                new("PreInstall", "Approved submittal received",        "Document review",   "Stamped"),
                new("PreStartup", "Pressure test both sides",           "Hydraulic",         "1.5× working, 24 h, no drop"),
                new("Startup",    "Vent + drain valves operate",        "Visual",            "Air cleared from top"),
                new("Functional", "Approach + LMTD per design",          "Inlet/outlet trend","Within 1 K of expected"),
                new("Handover",   "Cleaning + isolation procedure",      "Document review",   "Issued to FM team")
            },
            ["Damper"] = new()
            {
                new("PreStartup", "Actuator stroke + linkage",          "Visual",            "Closes tight, opens fully"),
                new("Functional", "Fail-safe position on power loss",   "Pull breaker",       "Defaults per spec"),
                new("Handover",   "Fire damper fusible link verified",  "Visual",            "Link + access door operable")
            },
            ["Generic"] = new()
            {
                new("PreInstall", "Approved submittal received",        "Document review",   "Stamped"),
                new("PreStartup", "Connections per IOM",                "Visual",            "Per manufacturer drawings"),
                new("Startup",    "Energize + functional check",        "Power on",          "No alarms"),
                new("Functional", "Operates per sequence",              "BMS / standalone",  "Per design intent"),
                new("Handover",   "O&M + as-built submitted",           "Document review",   "Complete + indexed")
            }
        };
    }
}
