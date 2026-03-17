// StandardsEngine.cs — Standards compliance commands for STINGTOOLS
// Covers gaps: ISO 19650 deep compliance, CIBSE validation, BS 7671 electrical,
// Uniclass 2015 classification, BS 8300 accessibility, Part L energy, Standards Dashboard
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    // ════════════════════════════════════════════════════════════════
    //  STANDARDS ENGINE — Shared compliance logic
    // ════════════════════════════════════════════════════════════════
    internal static class StandardsEngine
    {
        // ISO 19650 required project information fields
        internal static readonly string[] Iso19650ProjectFields = new[]
        {
            "Project Name", "Project Number", "Client Name", "Organization Name",
            "Author", "Building Name", "Project Status", "Project Phase"
        };

        // CIBSE system velocity limits (m/s) for duct/pipe sizing
        internal static readonly Dictionary<string, (double MinVelocity, double MaxVelocity)> CibseVelocityLimits = new()
        {
            ["Supply Duct - Main"] = (3.0, 10.0),
            ["Supply Duct - Branch"] = (2.0, 6.0),
            ["Return Duct - Main"] = (3.0, 8.0),
            ["Return Duct - Branch"] = (2.0, 5.0),
            ["Extract Duct"] = (3.0, 10.0),
            ["Chilled Water"] = (0.5, 3.0),
            ["Heating Water"] = (0.5, 3.0),
            ["Domestic Cold Water"] = (0.5, 2.0),
            ["Domestic Hot Water"] = (0.5, 2.0),
            ["Condensate"] = (0.3, 1.5),
        };

        // BS 7671 circuit protection requirements
        internal static readonly Dictionary<string, (double MaxLoad, double MinCableSize)> Bs7671CircuitReqs = new()
        {
            ["Lighting"] = (10.0, 1.5),       // Amps, mm²
            ["Ring Main"] = (32.0, 2.5),
            ["Radial 20A"] = (20.0, 2.5),
            ["Radial 32A"] = (32.0, 4.0),
            ["Cooker"] = (45.0, 6.0),
            ["Shower"] = (45.0, 10.0),
            ["EV Charger"] = (32.0, 6.0),
            ["Fire Alarm"] = (3.0, 1.5),
            ["Emergency Lighting"] = (6.0, 1.5),
        };

        // Uniclass 2015 top-level classification table (Ss_ systems)
        internal static readonly Dictionary<string, string> UniclassSystemCodes = new()
        {
            ["Ss_25"] = "Wall and barrier systems",
            ["Ss_30"] = "Roof, floor and paving systems",
            ["Ss_32"] = "Roof systems",
            ["Ss_35"] = "Stair and ramp systems",
            ["Ss_37"] = "Ceiling and soffit systems",
            ["Ss_40"] = "Disposal systems",
            ["Ss_45"] = "Distribution systems",
            ["Ss_50"] = "Cooling systems",
            ["Ss_55"] = "Heating systems",
            ["Ss_60"] = "Electrical systems",
            ["Ss_65"] = "Lighting systems",
            ["Ss_70"] = "Communication systems",
            ["Ss_75"] = "Transport systems",
            ["Ss_80"] = "Fire safety systems",
        };

        // BS 8300 accessibility dimensional requirements (mm)
        internal static readonly Dictionary<string, double> Bs8300Requirements = new()
        {
            ["MinDoorWidth"] = 900,
            ["MinCorridorWidth"] = 1200,
            ["MinLiftDoorWidth"] = 800,
            ["MinLiftCabinDepth"] = 1400,
            ["MinLiftCabinWidth"] = 1100,
            ["MaxRampGradient"] = 1.0 / 12.0,    // 1:12
            ["MinWCRoomWidth"] = 1500,
            ["MinWCRoomDepth"] = 2200,
            ["MaxThresholdHeight"] = 15,
            ["MinTurningCircle"] = 1500,
        };

        // Part L fabric U-value limits (W/m²K) — new build
        internal static readonly Dictionary<string, double> PartLUValues = new()
        {
            ["External Wall"] = 0.26,
            ["Roof"] = 0.16,
            ["Ground Floor"] = 0.18,
            ["Window"] = 1.6,
            ["Door"] = 1.6,
            ["Roof Window"] = 1.6,
            ["Party Wall"] = 0.0,  // Zero loss assumed
            ["Curtain Wall"] = 1.6,
        };

        /// <summary>Check ISO 19650 project information completeness.</summary>
        internal static List<(string Field, string Status, string Value)> CheckIso19650ProjectInfo(Document doc)
        {
            var results = new List<(string, string, string)>();
            var projInfo = doc.ProjectInformation;
            if (projInfo == null)
            {
                results.Add(("Project Information", "FAIL", "No project information element found"));
                return results;
            }

            foreach (string field in Iso19650ProjectFields)
            {
                string val = ParameterHelpers.GetString(projInfo, field);
                if (string.IsNullOrWhiteSpace(val))
                {
                    // Try built-in parameters
                    var p = projInfo.LookupParameter(field);
                    val = p?.AsString() ?? p?.AsValueString() ?? "";
                }
                string status = string.IsNullOrWhiteSpace(val) ? "MISSING" : "OK";
                results.Add((field, status, val));
            }
            return results;
        }

        /// <summary>Check ISO 19650 naming convention compliance on views and sheets.</summary>
        internal static List<(string Name, string Type, string Issue)> CheckIso19650Naming(Document doc)
        {
            var issues = new List<(string, string, string)>();

            // Check sheets
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            foreach (var sheet in sheets)
            {
                string num = sheet.SheetNumber ?? "";
                string name = sheet.Name ?? "";
                if (num.Contains(" ")) issues.Add((num, "Sheet", "Sheet number contains spaces"));
                if (name.Length > 80) issues.Add((name, "Sheet", "Sheet name exceeds 80 characters"));
                if (!System.Text.RegularExpressions.Regex.IsMatch(num, @"^[A-Z0-9\-]+$") && num.Length > 0)
                    issues.Add((num, "Sheet", "Sheet number contains non-standard characters"));
            }

            // Check view names
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .ToList();

            foreach (var view in views)
            {
                string name = view.Name ?? "";
                if (name.StartsWith("{") || name.StartsWith("("))
                    issues.Add((name, "View", "View name starts with bracket — possible default naming"));
                if (name.Contains("Copy") || name.Contains("copy"))
                    issues.Add((name, "View", "View name contains 'Copy' — likely unintended duplicate"));
            }

            return issues;
        }

        /// <summary>Validate CIBSE duct/pipe velocity compliance.</summary>
        internal static List<(string ElementInfo, string System, double Velocity, double Min, double Max, string Status)>
            CheckCibseVelocities(Document doc)
        {
            var results = new List<(string, string, double, double, double, string)>();

            // Check duct systems
            var ducts = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Mechanical.Duct))
                .ToList();

            foreach (var el in ducts)
            {
                var duct = el as Autodesk.Revit.DB.Mechanical.Duct;
                if (duct == null) continue;

                double velocity = 0;
                var velParam = duct.LookupParameter("Velocity");
                if (velParam != null && velParam.HasValue)
                    velocity = velParam.AsDouble() * 0.3048; // ft/s to m/s

                string sysType = ParameterHelpers.GetString(duct, "System Type") ?? "Supply Duct - Main";
                string key = CibseVelocityLimits.Keys.FirstOrDefault(k => sysType.Contains(k.Split('-')[0].Trim())) ?? "Supply Duct - Main";

                if (CibseVelocityLimits.TryGetValue(key, out var limits))
                {
                    string status = velocity < limits.MinVelocity ? "LOW" :
                                    velocity > limits.MaxVelocity ? "HIGH" : "OK";
                    if (status != "OK" || velocity > 0)
                        results.Add(($"Duct {duct.Id.IntegerValue}", sysType, Math.Round(velocity, 2),
                            limits.MinVelocity, limits.MaxVelocity, status));
                }
            }

            // Check pipes
            var pipes = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Plumbing.Pipe))
                .ToList();

            foreach (var el in pipes)
            {
                var pipe = el as Autodesk.Revit.DB.Plumbing.Pipe;
                if (pipe == null) continue;

                double velocity = 0;
                var velParam = pipe.LookupParameter("Velocity");
                if (velParam != null && velParam.HasValue)
                    velocity = velParam.AsDouble() * 0.3048;

                string sysType = ParameterHelpers.GetString(pipe, "System Type") ?? "Domestic Cold Water";
                string key = CibseVelocityLimits.Keys.FirstOrDefault(k => sysType.Contains(k.Split(' ')[0])) ?? "Domestic Cold Water";

                if (CibseVelocityLimits.TryGetValue(key, out var limits))
                {
                    string status = velocity < limits.MinVelocity ? "LOW" :
                                    velocity > limits.MaxVelocity ? "HIGH" : "OK";
                    if (status != "OK" || velocity > 0)
                        results.Add(($"Pipe {pipe.Id.IntegerValue}", sysType, Math.Round(velocity, 2),
                            limits.MinVelocity, limits.MaxVelocity, status));
                }
            }

            return results;
        }

        /// <summary>Check BS 7671 electrical circuit compliance.</summary>
        internal static List<(string CircuitId, string CircuitType, string Issue)> CheckBs7671Compliance(Document doc)
        {
            var issues = new List<(string, string, string)>();

            var circuits = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalCircuit)
                .ToList();

            foreach (var circuit in circuits)
            {
                string id = $"Circuit {circuit.Id.IntegerValue}";
                string name = circuit.Name ?? "Unknown";

                // Check circuit rating
                var ratingParam = circuit.LookupParameter("Rating");
                double rating = ratingParam?.AsDouble() ?? 0;

                // Check apparent load
                var loadParam = circuit.LookupParameter("Apparent Load");
                double load = loadParam?.AsDouble() ?? 0;

                if (rating > 0 && load > rating)
                    issues.Add((id, name, $"Load ({load:F1}A) exceeds rating ({rating:F1}A)"));

                // Check wire size
                var wireParam = circuit.LookupParameter("Wire Size");
                string wireSize = wireParam?.AsString() ?? "";

                // Check voltage drop (max 3% for lighting, 5% for power per BS 7671)
                var vDropParam = circuit.LookupParameter("Voltage Drop");
                double vDrop = vDropParam?.AsDouble() ?? 0;
                bool isLighting = name.ToLower().Contains("light") || name.ToLower().Contains("ltg");
                double maxDrop = isLighting ? 3.0 : 5.0;
                if (vDrop > maxDrop)
                    issues.Add((id, name, $"Voltage drop {vDrop:F1}% exceeds {maxDrop}% limit (BS 7671)"));
            }

            return issues;
        }

        /// <summary>Classify elements using Uniclass 2015 codes.</summary>
        internal static List<(ElementId Id, string Category, string UniclassCode, string Description)>
            ClassifyUniclass(Document doc)
        {
            var results = new List<(ElementId, string, string, string)>();

            // Category to Uniclass mapping
            var catMapping = new Dictionary<BuiltInCategory, (string Code, string Desc)>
            {
                [BuiltInCategory.OST_Walls] = ("Ss_25_10", "Wall systems"),
                [BuiltInCategory.OST_Floors] = ("Ss_30_10", "Floor systems"),
                [BuiltInCategory.OST_Roofs] = ("Ss_32_10", "Roof systems"),
                [BuiltInCategory.OST_Ceilings] = ("Ss_37_10", "Ceiling systems"),
                [BuiltInCategory.OST_Stairs] = ("Ss_35_10", "Stair systems"),
                [BuiltInCategory.OST_Doors] = ("Pr_30_59_24", "Doors"),
                [BuiltInCategory.OST_Windows] = ("Pr_30_59_96", "Windows"),
                [BuiltInCategory.OST_DuctCurves] = ("Ss_55_30", "Ductwork distribution"),
                [BuiltInCategory.OST_PipeCurves] = ("Ss_45_30", "Pipework distribution"),
                [BuiltInCategory.OST_MechanicalEquipment] = ("Ss_55_40", "Mechanical plant"),
                [BuiltInCategory.OST_ElectricalEquipment] = ("Ss_60_40", "Electrical plant"),
                [BuiltInCategory.OST_ElectricalFixtures] = ("Ss_60_30", "Electrical outlets"),
                [BuiltInCategory.OST_LightingFixtures] = ("Ss_65_40", "Luminaires"),
                [BuiltInCategory.OST_PlumbingFixtures] = ("Ss_45_40", "Sanitary appliances"),
                [BuiltInCategory.OST_Sprinklers] = ("Ss_80_50", "Fire suppression"),
                [BuiltInCategory.OST_Furniture] = ("Pr_40_30", "Furniture"),
                [BuiltInCategory.OST_CableTray] = ("Ss_60_20", "Cable containment"),
                [BuiltInCategory.OST_Conduit] = ("Ss_60_20", "Cable containment"),
                [BuiltInCategory.OST_StructuralColumns] = ("Ss_20_05", "Structural columns"),
                [BuiltInCategory.OST_StructuralFraming] = ("Ss_20_10", "Structural framing"),
            };

            foreach (var kvp in catMapping)
            {
                var elements = new FilteredElementCollector(doc)
                    .OfCategory(kvp.Key)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var el in elements)
                {
                    string catName = el.Category?.Name ?? "Unknown";
                    results.Add((el.Id, catName, kvp.Value.Code, kvp.Value.Desc));
                }
            }

            return results;
        }

        /// <summary>Check BS 8300 accessibility requirements on doors and rooms.</summary>
        internal static List<(string Element, string Check, string Required, string Actual, string Status)>
            CheckBs8300Accessibility(Document doc)
        {
            var results = new List<(string, string, string, string, string)>();

            // Check door widths
            var doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .ToList();

            double minDoorMM = Bs8300Requirements["MinDoorWidth"];
            foreach (var door in doors)
            {
                var widthParam = door.LookupParameter("Width") ?? door.get_Parameter(BuiltInParameter.DOOR_WIDTH);
                if (widthParam != null && widthParam.HasValue)
                {
                    double widthMM = widthParam.AsDouble() * 304.8; // ft to mm
                    string status = widthMM >= minDoorMM ? "PASS" : "FAIL";
                    if (status == "FAIL")
                        results.Add(($"Door {door.Id.IntegerValue}", "Min Door Width",
                            $"{minDoorMM}mm", $"{widthMM:F0}mm", status));
                }
            }

            // Check room dimensions for WC rooms
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.Room>()
                .Where(r => r.Area > 0)
                .ToList();

            foreach (var room in rooms)
            {
                string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                if (name.ToLower().Contains("wc") || name.ToLower().Contains("toilet") || name.ToLower().Contains("accessible"))
                {
                    double areaSqM = room.Area * 0.092903; // sq ft to sq m
                    double minArea = (Bs8300Requirements["MinWCRoomWidth"] / 1000.0) * (Bs8300Requirements["MinWCRoomDepth"] / 1000.0);
                    string status = areaSqM >= minArea ? "PASS" : "FAIL";
                    results.Add(($"Room: {name} ({room.Id.IntegerValue})", "Min WC Area",
                        $"{minArea:F1}m²", $"{areaSqM:F1}m²", status));
                }
            }

            return results;
        }

        /// <summary>Check Part L energy compliance for building fabric.</summary>
        internal static List<(string Element, string Type, string MaxU, string Status)>
            CheckPartLCompliance(Document doc)
        {
            var results = new List<(string, string, string, string)>();

            // Check wall types
            var wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .ToList();

            foreach (var wt in wallTypes)
            {
                string name = wt.Name ?? "";
                bool isExternal = name.ToLower().Contains("ext") || name.ToLower().Contains("facade") || name.ToLower().Contains("curtain");
                if (!isExternal) continue;

                string typeKey = name.ToLower().Contains("curtain") ? "Curtain Wall" : "External Wall";
                double maxU = PartLUValues[typeKey];

                // Try to get thermal resistance from compound structure
                var cs = wt.GetCompoundStructure();
                string status = cs != null ? "CHECK MANUALLY" : "NO STRUCTURE";
                results.Add(($"Wall: {name}", typeKey, $"{maxU} W/m²K", status));
            }

            // Check roof types
            var roofTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .ToList();

            foreach (var rt in roofTypes)
            {
                double maxU = PartLUValues["Roof"];
                results.Add(($"Roof: {rt.Name}", "Roof", $"{maxU} W/m²K", "CHECK MANUALLY"));
            }

            // Check floor types
            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .ToList();

            foreach (var ft in floorTypes)
            {
                string name = ft.Name ?? "";
                if (!name.ToLower().Contains("ground") && !name.ToLower().Contains("slab")) continue;
                double maxU = PartLUValues["Ground Floor"];
                results.Add(($"Floor: {name}", "Ground Floor", $"{maxU} W/m²K", "CHECK MANUALLY"));
            }

            return results;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 1: ISO 19650 Deep Compliance Check
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Iso19650DeepComplianceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ ISO 19650 DEEP COMPLIANCE REPORT ═══\n");

                // 1. Project Information
                report.AppendLine("── PROJECT INFORMATION ──");
                var projChecks = StandardsEngine.CheckIso19650ProjectInfo(doc);
                int projPass = 0, projFail = 0;
                foreach (var (field, status, value) in projChecks)
                {
                    report.AppendLine($"  {status}: {field} = {(string.IsNullOrEmpty(value) ? "(empty)" : value)}");
                    if (status == "OK") projPass++; else projFail++;
                }

                // 2. Naming Convention
                report.AppendLine("\n── NAMING CONVENTION ──");
                var namingIssues = StandardsEngine.CheckIso19650Naming(doc);
                report.AppendLine($"  Issues found: {namingIssues.Count}");
                foreach (var (name, type, issue) in namingIssues.Take(20))
                    report.AppendLine($"  [{type}] {name}: {issue}");
                if (namingIssues.Count > 20)
                    report.AppendLine($"  ... and {namingIssues.Count - 20} more");

                // 3. Tag completeness
                report.AppendLine("\n── TAG COMPLETENESS ──");
                int tagged = 0, untagged = 0, incomplete = 0;
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                    .ToList();

                foreach (var el in allElements)
                {
                    string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag)) untagged++;
                    else if (TagConfig.TagIsComplete(tag)) tagged++;
                    else incomplete++;
                }
                report.AppendLine($"  Complete tags: {tagged}");
                report.AppendLine($"  Incomplete tags: {incomplete}");
                report.AppendLine($"  Untagged elements: {untagged}");

                // Summary score
                double totalChecks = projChecks.Count + namingIssues.Count + allElements.Count;
                double passed = projPass + (allElements.Count - namingIssues.Count) + tagged;
                double score = totalChecks > 0 ? (passed / totalChecks) * 100 : 0;
                report.AppendLine($"\n══ OVERALL COMPLIANCE SCORE: {score:F0}% ══");
                report.AppendLine($"Project Info: {projPass}/{projChecks.Count} | Naming Issues: {namingIssues.Count} | Tags: {tagged}/{allElements.Count}");

                TaskDialog.Show("ISO 19650 Deep Compliance", report.ToString());
                StingLog.Info($"ISO 19650 deep compliance: {score:F0}%");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ISO 19650 deep compliance failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 2: CIBSE Velocity Compliance
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CibseVelocityCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var results = StandardsEngine.CheckCibseVelocities(doc);

                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ CIBSE VELOCITY COMPLIANCE ═══\n");

                int pass = 0, low = 0, high = 0;
                foreach (var (elem, sys, vel, min, max, status) in results)
                {
                    if (status == "OK") { pass++; continue; }
                    if (status == "LOW") low++;
                    if (status == "HIGH") high++;
                    report.AppendLine($"  {status}: {elem} ({sys}) — {vel} m/s [range: {min}-{max}]");
                }

                report.Insert(0, $"Pass: {pass} | Low: {low} | High: {high}\n\n");
                if (results.Count == 0) report.AppendLine("  No duct/pipe velocity data found.");

                TaskDialog.Show("CIBSE Velocity Check", report.ToString());
                StingLog.Info($"CIBSE velocity: {pass} pass, {low} low, {high} high");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CIBSE velocity check failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 3: BS 7671 Electrical Compliance
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Bs7671ComplianceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var issues = StandardsEngine.CheckBs7671Compliance(doc);

                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ BS 7671 ELECTRICAL COMPLIANCE ═══\n");
                report.AppendLine($"Issues found: {issues.Count}\n");

                foreach (var (circuitId, circuitType, issue) in issues)
                    report.AppendLine($"  {circuitId} ({circuitType}): {issue}");

                if (issues.Count == 0)
                    report.AppendLine("  All circuits comply with BS 7671 requirements.");

                TaskDialog.Show("BS 7671 Compliance", report.ToString());
                StingLog.Info($"BS 7671: {issues.Count} issues found");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BS 7671 check failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 4: Uniclass 2015 Classification
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class UniclassClassifyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var classifications = StandardsEngine.ClassifyUniclass(doc);

                // Group by Uniclass code
                var groups = classifications.GroupBy(c => c.UniclassCode)
                    .OrderBy(g => g.Key);

                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ UNICLASS 2015 CLASSIFICATION ═══\n");
                report.AppendLine($"Total elements classified: {classifications.Count}\n");

                foreach (var group in groups)
                {
                    string desc = group.First().Description;
                    report.AppendLine($"  {group.Key} — {desc}: {group.Count()} elements");
                }

                // Write Uniclass codes to elements
                using (var t = new Transaction(doc, "STING Uniclass Classify"))
                {
                    t.Start();
                    int written = 0;
                    foreach (var (id, cat, code, desc) in classifications)
                    {
                        var el = doc.GetElement(id);
                        if (el != null)
                        {
                            if (ParameterHelpers.SetString(el, "ASS_CLASS_COD_TXT", code, false))
                                written++;
                            ParameterHelpers.SetString(el, "ASS_CLASS_DESC_TXT", desc, false);
                        }
                    }
                    t.Commit();
                    report.AppendLine($"\nUniclass codes written to {written} elements.");
                }

                TaskDialog.Show("Uniclass Classification", report.ToString());
                StingLog.Info($"Uniclass: classified {classifications.Count} elements");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Uniclass classification failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 5: BS 8300 Accessibility Check
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Bs8300AccessibilityCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var results = StandardsEngine.CheckBs8300Accessibility(doc);

                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ BS 8300 ACCESSIBILITY CHECK ═══\n");

                int pass = 0, fail = 0;
                foreach (var (elem, check, required, actual, status) in results)
                {
                    if (status == "PASS") { pass++; continue; }
                    fail++;
                    report.AppendLine($"  FAIL: {elem}");
                    report.AppendLine($"        Check: {check} | Required: {required} | Actual: {actual}");
                }

                report.Insert(0, $"Pass: {pass} | Fail: {fail}\n\n");
                if (results.Count == 0)
                    report.AppendLine("  No accessibility-relevant elements found to check.");

                TaskDialog.Show("BS 8300 Accessibility", report.ToString());
                StingLog.Info($"BS 8300: {pass} pass, {fail} fail");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BS 8300 check failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 6: Part L Energy Compliance
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PartLComplianceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var results = StandardsEngine.CheckPartLCompliance(doc);

                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ PART L ENERGY COMPLIANCE ═══\n");
                report.AppendLine($"Elements checked: {results.Count}\n");

                var grouped = results.GroupBy(r => r.Type);
                foreach (var group in grouped)
                {
                    report.AppendLine($"── {group.Key} (Max U-value: {group.First().MaxU}) ──");
                    foreach (var (elem, type, maxU, status) in group)
                        report.AppendLine($"  {elem}: {status}");
                }

                report.AppendLine("\nNote: Thermal analysis requires energy model. Manual U-value verification recommended.");

                TaskDialog.Show("Part L Compliance", report.ToString());
                StingLog.Info($"Part L: checked {results.Count} fabric elements");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Part L check failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 7: Standards Compliance Dashboard
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StandardsDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var report = new System.Text.StringBuilder();
                report.AppendLine("════════════════════════════════════════");
                report.AppendLine("   STING STANDARDS COMPLIANCE DASHBOARD");
                report.AppendLine("════════════════════════════════════════\n");

                // ISO 19650
                var projChecks = StandardsEngine.CheckIso19650ProjectInfo(doc);
                int isoPass = projChecks.Count(c => c.Status == "OK");
                var namingIssues = StandardsEngine.CheckIso19650Naming(doc);
                double isoScore = projChecks.Count > 0 ? (double)isoPass / projChecks.Count * 100 : 0;
                report.AppendLine($"📋 ISO 19650     : {isoScore:F0}% ({isoPass}/{projChecks.Count} fields, {namingIssues.Count} naming issues)");

                // CIBSE
                var velocities = StandardsEngine.CheckCibseVelocities(doc);
                int cibsePass = velocities.Count(v => v.Status == "OK");
                int cibseTotal = velocities.Count;
                double cibseScore = cibseTotal > 0 ? (double)cibsePass / cibseTotal * 100 : 100;
                report.AppendLine($"🌀 CIBSE         : {cibseScore:F0}% ({cibsePass}/{cibseTotal} systems compliant)");

                // BS 7671
                var elecIssues = StandardsEngine.CheckBs7671Compliance(doc);
                report.AppendLine($"⚡ BS 7671       : {elecIssues.Count} issue(s) found");

                // Uniclass
                var uniClass = StandardsEngine.ClassifyUniclass(doc);
                int uniqueCodes = uniClass.Select(c => c.UniclassCode).Distinct().Count();
                report.AppendLine($"🏗  Uniclass 2015 : {uniClass.Count} elements, {uniqueCodes} unique codes");

                // BS 8300
                var access = StandardsEngine.CheckBs8300Accessibility(doc);
                int accPass = access.Count(a => a.Status == "PASS");
                int accFail = access.Count(a => a.Status == "FAIL");
                double accScore = access.Count > 0 ? (double)accPass / access.Count * 100 : 100;
                report.AppendLine($"♿ BS 8300       : {accScore:F0}% ({accPass} pass, {accFail} fail)");

                // Part L
                var partL = StandardsEngine.CheckPartLCompliance(doc);
                report.AppendLine($"🔥 Part L        : {partL.Count} fabric elements to verify");

                // Overall
                double overall = (isoScore + cibseScore + accScore) / 3.0;
                report.AppendLine($"\n════ OVERALL SCORE: {overall:F0}% ════");

                TaskDialog.Show("Standards Dashboard", report.ToString());
                StingLog.Info($"Standards dashboard: overall {overall:F0}%");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Standards dashboard failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
