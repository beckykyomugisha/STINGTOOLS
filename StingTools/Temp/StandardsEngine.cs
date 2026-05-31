// StandardsEngine.cs — Standards compliance commands for STINGTOOLS
// Covers gaps: ISO 19650 deep compliance, CIBSE validation, BS 7671 electrical,
// Uniclass 2015 classification, BS 8300 accessibility, Part L energy, Standards Dashboard
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB.Architecture;

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
            // Z-23 (8.1): supply-main max tightened 10.0 → 9.0 m/s per CIBSE Guide B
            // noise guidance (≤7.5–9 m/s for occupied-space supply mains); 10 was permissive.
            ["Supply Duct - Main"] = (3.0, 9.0),
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

        /// <summary>Match a Revit system type string to the best CIBSE velocity key.
        /// Prefers exact match, then longest key contained in the system type.</summary>
        internal static string MatchCibseKey(string sysType, string fallback)
        {
            if (string.IsNullOrEmpty(sysType)) return fallback;
            // Exact match first
            if (CibseVelocityLimits.ContainsKey(sysType)) return sysType;
            // Longest key that is a substring of the system type (prefer "Supply Duct - Branch" over "Supply Duct - Main")
            string best = null;
            int bestLen = 0;
            foreach (var k in CibseVelocityLimits.Keys)
            {
                if (sysType.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 && k.Length > bestLen)
                {
                    best = k;
                    bestLen = k.Length;
                }
            }
            return best ?? fallback;
        }

        // BS 7671 circuit protection requirements
        internal static readonly Dictionary<string, (double MaxLoad, double MinCableSize)> Bs7671CircuitReqs = new()
        {
            ["Lighting"] = (10.0, 1.5),       // Amps, mm²
            ["Ring Main"] = (32.0, 2.5),
            ["Radial 20A"] = (20.0, 2.5),
            ["Radial 32A"] = (32.0, 4.0),
            // Z-23 (8.2): 45 A cooker circuit on 6 mm² is clipped-correct (Iz ~46–47 A
            // for reference methods C/100) but MARGINAL under thermal-insulation
            // installation methods — verify the actual reference method per BS 7671
            // 18th Ed App.4 before relying on 6 mm² for a 45 A cooker. Left as-is.
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
                    var p = ParameterHelpers.CachedLookup(projInfo, field);
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
                var velParam = ParameterHelpers.CachedLookup(duct, "Velocity");
                if (velParam != null && velParam.HasValue)
                    velocity = velParam.AsDouble() * 0.3048; // ft/s to m/s

                string sysType = ParameterHelpers.GetString(duct, "System Type") ?? "Supply Duct - Main";
                string key = MatchCibseKey(sysType, "Supply Duct - Main");

                if (CibseVelocityLimits.TryGetValue(key, out var limits))
                {
                    string status = velocity < limits.MinVelocity ? "LOW" :
                                    velocity > limits.MaxVelocity ? "HIGH" : "OK";
                    if (status != "OK" || velocity > 0)
                        results.Add(($"Duct {duct.Id.Value}", sysType, Math.Round(velocity, 2),
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
                var velParam = ParameterHelpers.CachedLookup(pipe, "Velocity");
                if (velParam != null && velParam.HasValue)
                    velocity = velParam.AsDouble() * 0.3048;

                string sysType = ParameterHelpers.GetString(pipe, "System Type") ?? "Domestic Cold Water";
                string key = MatchCibseKey(sysType, "Domestic Cold Water");

                if (CibseVelocityLimits.TryGetValue(key, out var limits))
                {
                    string status = velocity < limits.MinVelocity ? "LOW" :
                                    velocity > limits.MaxVelocity ? "HIGH" : "OK";
                    if (status != "OK" || velocity > 0)
                        results.Add(($"Pipe {pipe.Id.Value}", sysType, Math.Round(velocity, 2),
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
                string id = $"Circuit {circuit.Id.Value}";
                string name = circuit.Name ?? "Unknown";

                // Check circuit rating
                var ratingParam = ParameterHelpers.CachedLookup(circuit, "Rating");
                double rating = ratingParam?.AsDouble() ?? 0;

                // Check apparent load
                var loadParam = ParameterHelpers.CachedLookup(circuit, "Apparent Load");
                double load = loadParam?.AsDouble() ?? 0;

                if (rating > 0 && load > rating)
                    issues.Add((id, name, $"Load ({load:F1}A) exceeds rating ({rating:F1}A)"));

                // Check wire size
                var wireParam = ParameterHelpers.CachedLookup(circuit, "Wire Size");
                string wireSize = wireParam?.AsString() ?? "";

                // Check voltage drop (max 3% for lighting, 5% for power per BS 7671)
                var vDropParam = ParameterHelpers.CachedLookup(circuit, "Voltage Drop");
                double vDrop = vDropParam?.AsDouble() ?? 0;
                bool isLighting = name.ToLower().Contains("light") || name.ToLower().Contains("ltg");
                double maxDrop = isLighting ? 3.0 : 5.0;
                if (vDrop > maxDrop)
                    issues.Add((id, name, $"Voltage drop {vDrop:F1}% exceeds {maxDrop}% limit (BS 7671)"));

                // HIGH-SE-02: Validate load and cable size against Bs7671CircuitReqs lookup table
                string nameLower = name.ToLower();
                string matchedCircuitType = null;
                foreach (var reqKey in Bs7671CircuitReqs.Keys)
                {
                    if (nameLower.Contains(reqKey.ToLower()))
                    {
                        matchedCircuitType = reqKey;
                        break;
                    }
                }
                if (matchedCircuitType != null)
                {
                    var reqs = Bs7671CircuitReqs[matchedCircuitType];
                    if (rating > 0 && rating > reqs.MaxLoad)
                        issues.Add((id, name, $"Rating ({rating:F0}A) exceeds max for {matchedCircuitType} ({reqs.MaxLoad:F0}A) per BS 7671"));
                    if (!string.IsNullOrEmpty(wireSize) && double.TryParse(
                        System.Text.RegularExpressions.Regex.Match(wireSize, @"[\d.]+").Value, out double wireMm2))
                    {
                        if (wireMm2 < reqs.MinCableSize)
                            issues.Add((id, name, $"Cable size ({wireMm2}mm²) below min {reqs.MinCableSize}mm² for {matchedCircuitType} per BS 7671"));
                    }
                }
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

            // HIGH-SE-03: Single multi-category collector instead of 18 separate per-category scans
            var uniclassCatFilter = new ElementMulticategoryFilter(new List<BuiltInCategory>(catMapping.Keys));
            var allElements = new FilteredElementCollector(doc)
                .WherePasses(uniclassCatFilter)
                .WhereElementIsNotElementType();

            foreach (var el in allElements)
            {
                if (el.Category == null) continue;
                var bic = (BuiltInCategory)el.Category.Id.Value;
                if (catMapping.TryGetValue(bic, out var mapping))
                {
                    string catName = el.Category.Name ?? "Unknown";
                    results.Add((el.Id, catName, mapping.Code, mapping.Desc));
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
                var widthParam = ParameterHelpers.CachedLookup(door, "Width") ?? door.get_Parameter(BuiltInParameter.DOOR_WIDTH);
                if (widthParam != null && widthParam.HasValue)
                {
                    double widthMM = widthParam.AsDouble() * 304.8; // ft to mm
                    string status = widthMM >= minDoorMM ? "PASS" : "FAIL";
                    if (status == "FAIL")
                        results.Add(($"Door {door.Id.Value}", "Min Door Width",
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
                    results.Add(($"Room: {name} ({room.Id.Value})", "Min WC Area",
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

                // HIGH-SE-04: Compute approximate U-value from compound structure layers
                var cs = wt.GetCompoundStructure();
                if (cs != null)
                {
                    double totalR = CalculateCompoundR(doc, cs);
                    if (totalR > 0)
                    {
                        double uValue = 1.0 / totalR;
                        string status = uValue <= maxU ? "PASS" : "FAIL";
                        results.Add(($"Wall: {name}", typeKey, $"{maxU} W/m²K",
                            $"{status} (U={uValue:F2} W/m²K)"));
                    }
                    else
                    {
                        results.Add(($"Wall: {name}", typeKey, $"{maxU} W/m²K", "CHECK MANUALLY"));
                    }
                }
                else
                {
                    results.Add(($"Wall: {name}", typeKey, $"{maxU} W/m²K", "NO STRUCTURE"));
                }
            }

            // Check roof types
            var roofTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .ToList();

            foreach (var rt in roofTypes)
            {
                double maxU = PartLUValues["Roof"];
                var rcs = rt.GetCompoundStructure();
                if (rcs != null)
                {
                    double totalR = CalculateCompoundR(doc, rcs);
                    if (totalR > 0)
                    {
                        double uValue = 1.0 / totalR;
                        string status = uValue <= maxU ? "PASS" : "FAIL";
                        results.Add(($"Roof: {rt.Name}", "Roof", $"{maxU} W/m²K",
                            $"{status} (U={uValue:F2} W/m²K)"));
                        continue;
                    }
                }
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
                var fcs = ft.GetCompoundStructure();
                if (fcs != null)
                {
                    double totalR = CalculateCompoundR(doc, fcs);
                    if (totalR > 0)
                    {
                        double uValue = 1.0 / totalR;
                        string status = uValue <= maxU ? "PASS" : "FAIL";
                        results.Add(($"Floor: {name}", "Ground Floor", $"{maxU} W/m²K",
                            $"{status} (U={uValue:F2} W/m²K)"));
                        continue;
                    }
                }
                results.Add(($"Floor: {name}", "Ground Floor", $"{maxU} W/m²K", "CHECK MANUALLY"));
            }

            return results;
        }

        /// <summary>
        /// Calculate total thermal resistance (m²K/W) from compound structure layers.
        /// Includes internal (0.13) and external (0.04) surface resistances per BS EN ISO 6946.
        /// Returns 0 if thermal conductivity is unavailable for any layer.
        /// </summary>
        private static double CalculateCompoundR(Document doc, CompoundStructure cs)
        {
            const double Rsi = 0.13; // internal surface resistance
            const double Rse = 0.04; // external surface resistance
            double totalR = Rsi + Rse;

            foreach (var layer in cs.GetLayers())
            {
                double thicknessM = layer.Width * 0.3048; // ft to m
                if (thicknessM <= 0) continue;

                var matId = layer.MaterialId;
                if (matId == null || matId == ElementId.InvalidElementId) return 0;

                var mat = doc.GetElement(matId) as Material;
                if (mat == null) return 0;

                var thermalAsset = mat.ThermalAssetId != ElementId.InvalidElementId
                    ? doc.GetElement(mat.ThermalAssetId) as PropertySetElement
                    : null;

                if (thermalAsset != null)
                {
                    var conductivityParam = ParameterHelpers.CachedLookup(thermalAsset, "Thermal Conductivity");
                    if (conductivityParam != null && conductivityParam.HasValue)
                    {
                        double conductivity = conductivityParam.AsDouble(); // W/(m·K) in Revit internal units
                        if (conductivity > 0)
                        {
                            totalR += thicknessM / conductivity;
                            continue;
                        }
                    }
                }
                // If we can't get conductivity for any layer, bail out
                return 0;
            }

            return totalR;
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
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
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

                // Summary score — weighted average of 3 independent sections
                double projScore = projChecks.Count > 0 ? (double)projPass / projChecks.Count * 100 : 0;
                double namingScore = namingIssues.Count == 0 ? 100 : Math.Max(0, 100 - namingIssues.Count * 5);
                double tagScore = allElements.Count > 0 ? (double)tagged / allElements.Count * 100 : 0;
                double score = (projScore + namingScore + tagScore) / 3.0;
                report.AppendLine($"\n══ OVERALL COMPLIANCE SCORE: {score:F0}% ══");
                report.AppendLine($"Project Info: {projPass}/{projChecks.Count} ({projScore:F0}%) | Naming: {namingScore:F0}% ({namingIssues.Count} issues) | Tags: {tagged}/{allElements.Count} ({tagScore:F0}%)");

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
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
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
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
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
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
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
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
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
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
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
        // Lightweight cache to avoid redundant computation on rapid re-clicks
        private static string _cachedReport;
        private static string _cachedDocPath;
        private static DateTime _cachedTime = DateTime.MinValue;
        private const int CacheTtlSeconds = 10;

        internal static void InvalidateCache() { _cachedTime = DateTime.MinValue; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;

                // Return cached report if still fresh and same document
                string docKey = doc.PathName ?? doc.Title ?? "";
                if (_cachedReport != null && docKey == _cachedDocPath
                    && (DateTime.UtcNow - _cachedTime).TotalSeconds < CacheTtlSeconds)
                {
                    TaskDialog.Show("Standards Dashboard", _cachedReport);
                    return Result.Succeeded;
                }

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

                string reportText = report.ToString();
                _cachedReport = reportText;
                _cachedDocPath = docKey;
                _cachedTime = DateTime.UtcNow;

                TaskDialog.Show("Standards Dashboard", reportText);
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
