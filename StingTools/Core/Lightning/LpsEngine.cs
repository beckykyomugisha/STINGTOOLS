// StingTools — LpsEngine.cs
//
// Lightning Protection System engine. Pure C# utility class that loads
// the corporate LPS data (BS EN 62305 class table, flash density,
// risk factors), exposes parameter calculations, and runs the model
// validators consumed by the LightningProtectionCommands.
//
// Per CLAUDE.md: this is engine logic only — no IExternalCommand types
// live here. All Revit reads go through ParameterHelpers and use
// FilteredElementCollector. No transactions are opened in this file;
// callers wrap writes in their own "STING …" transaction.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using StingTools.Core.Fabrication;

namespace StingTools.Core.Lightning
{
    public static class LpsEngine
    {
        // ── Cached library objects ────────────────────────────────────

        private static readonly object _lock = new object();
        private static Dictionary<string, LpsClassDef> _classes;
        private static JObject _flashDensity;
        private static JObject _riskFactors;

        // ── Class table loader ────────────────────────────────────────

        public static LpsClassDef LoadClass(string classId)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(classId)) return null;
            string norm = classId.Trim().ToUpperInvariant();
            return _classes.TryGetValue(norm, out var def) ? def : null;
        }

        public static IReadOnlyList<LpsClassDef> AllClasses()
        {
            EnsureLoaded();
            return _classes.Values.OrderBy(c => c.Id).ToList();
        }

        public static double GetMaterialFactor(string material)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(material)) return 1.0;
            string norm = material.Trim().ToUpperInvariant();
            try
            {
                var classesJson = LoadJson("STING_LPS_CLASSES.json");
                var mf = classesJson?["materialFactors"] as JObject;
                if (mf != null && mf[norm] != null)
                    return mf[norm].Value<double>();
            }
            catch (Exception ex) { StingLog.Warn($"GetMaterialFactor: {ex.Message}"); }
            // Fallback to copper
            return 1.0;
        }

        // ── Flash density / risk factors ──────────────────────────────

        public static JObject GetFlashDensityLibrary()
        {
            EnsureLoaded();
            return _flashDensity;
        }

        public static JObject GetRiskFactorLibrary()
        {
            EnsureLoaded();
            return _riskFactors;
        }

        public static double GetFlashDensity(string regionId)
        {
            try
            {
                var lib = GetFlashDensityLibrary();
                var regions = lib?["regions"] as JArray;
                if (regions == null) return 2.0;
                foreach (var r in regions)
                {
                    if (string.Equals(r["id"]?.ToString(), regionId, StringComparison.OrdinalIgnoreCase))
                        return r["ng"]?.Value<double>() ?? 2.0;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetFlashDensity: {ex.Message}"); }
            return 2.0;
        }

        /// <summary>
        /// Effective ground flash density for the project: prefers the
        /// ELC_LPS_PROJECT_NG_OVERRIDE_NR value on ProjectInformation when > 0,
        /// falls back to the supplied region default. Use this instead of
        /// GetFlashDensity directly for project-aware code paths.
        /// </summary>
        public static double GetEffectiveFlashDensity(Document doc, string regionId)
        {
            try
            {
                if (doc?.ProjectInformation != null)
                {
                    double ov = GetDoubleParam(doc.ProjectInformation,
                        Fabrication.LpsParams.PROJECT_NG_OVERRIDE_NR);
                    if (ov > 0) return ov;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetEffectiveFlashDensity: {ex.Message}"); }
            return GetFlashDensity(regionId);
        }

        // ── Calculations ──────────────────────────────────────────────

        /// <summary>
        /// Protection cone half-angle for the given LPS class and air-terminal
        /// height (m). Interpolates the height bucket from the class table.
        /// Returns degrees, or 0 when the height exceeds the class limit
        /// (which means the air terminal alone cannot protect the volume —
        /// caller must use mesh or rolling sphere methods).
        /// </summary>
        public static double ComputeProtectionAngle(string classId, double airTerminalHeightM)
        {
            var def = LoadClass(classId);
            if (def == null) return 0.0;
            if (airTerminalHeightM <= 10.0) return def.ProtectionAngleDeg10m;
            if (airTerminalHeightM <= 20.0) return def.ProtectionAngleDeg20m;
            if (airTerminalHeightM <= 30.0) return def.ProtectionAngleDeg30m;
            if (airTerminalHeightM <= 45.0) return def.ProtectionAngleDeg45m;
            if (airTerminalHeightM <= 60.0) return def.ProtectionAngleDeg60m;
            return 0.0;
        }

        /// <summary>
        /// Separation distance s = (ki / km) * kc * l per BS EN 62305-3 §6.3.
        /// kc defaults to 1.0 for a single down conductor path. Returns
        /// millimetres; caller passes conductor length in metres.
        /// </summary>
        public static double ComputeSeparationDistance(
            string classId,
            double conductorLengthFromAirTerminalToNearestBondM,
            string routingMaterial,
            double kc = 1.0)
        {
            var def = LoadClass(classId);
            if (def == null) return 0.0;
            double km = GetMaterialFactor(routingMaterial);
            if (km <= 0.0) km = 1.0;
            double s_m = (def.KiFactor / km) * kc * conductorLengthFromAirTerminalToNearestBondM;
            return s_m * 1000.0;
        }

        /// <summary>
        /// Minimum down conductor count for the perimeter. Returns at least 2
        /// per BS EN 62305-3 §5.3.3 even for very small structures.
        /// </summary>
        public static int ComputeMinDownConductors(string classId, double perimeterM)
        {
            var def = LoadClass(classId);
            if (def == null) return 2;
            if (perimeterM <= 0.0) return 2;
            int n = (int)Math.Ceiling(perimeterM / def.DownConductorSpacingM);
            return Math.Max(2, n);
        }

        // ── Risk assessment ───────────────────────────────────────────

        public static LpsRiskResult RunRiskAssessment(LpsRiskInput input)
        {
            var result = new LpsRiskResult();
            try
            {
                EnsureLoaded();
                if (input == null) { result.Notes = "No input provided"; return result; }

                // Collection area Ae per BS EN 62305-2 §A.2 (rectangular building):
                //   Ae = L*W + 2*(3H)*(L+W) + π*(3H)^2
                double L = input.PlanLengthM;
                double W = input.PlanWidthM;
                double H = input.HeightM;
                if (L <= 0 || W <= 0 || H <= 0)
                {
                    // Fall back to a square plan derived from area/perimeter inputs.
                    double area = input.PlanAreaM2;
                    if (area > 0 && L <= 0 && W <= 0)
                    {
                        L = Math.Sqrt(area);
                        W = L;
                    }
                }
                double Ae = (L * W) + (2.0 * (3.0 * H) * (L + W)) + (Math.PI * Math.Pow(3.0 * H, 2));
                if (Ae < 1.0) Ae = 1.0;
                result.CollectionAreaM2 = Ae;

                // Annual dangerous events: Nd = Ng * Ae * Cd * 10^-6
                double Nd = input.GroundFlashDensity * Ae * input.LocationFactorCd * 1e-6;
                result.AnnualStrikeFrequency = Nd;

                // Risk components — simplified BS EN 62305-2 Annex B
                double R1 = Nd * input.BuildingTypeCb * input.InternalContentCc *
                            input.OccupantHazardCd * input.ConsequenceCe;
                result.RiskComponents["R1_Direct"] = R1;
                double Rt = input.TolerableRisk > 0 ? input.TolerableRisk : 1e-5;
                result.TolerableRisk = Rt;
                result.RequiresLps = R1 > Rt;

                // Recommended class via threshold table
                result.RecommendedClass = RecommendClass(Nd);

                if (!result.RequiresLps)
                    result.RecommendedClass = "NONE";

                result.Notes = string.Format(
                    "Nd={0:F4} flashes/yr; R1={1:E2} vs Rt={2:E2}; recommended class {3}.",
                    Nd, R1, Rt, result.RecommendedClass ?? "NONE");
            }
            catch (Exception ex)
            {
                StingLog.Error("RunRiskAssessment failed", ex);
                result.Notes = "Risk assessment failed: " + ex.Message;
            }
            return result;
        }

        private static string RecommendClass(double nd)
        {
            try
            {
                var lib = GetRiskFactorLibrary();
                var thresholds = lib?["classRecommendation"]?["thresholds"] as JArray;
                if (thresholds == null) return "II";
                foreach (var t in thresholds)
                {
                    double mn = t["ndMin"]?.Value<double>() ?? 0;
                    double mx = t["ndMax"]?.Value<double>() ?? double.MaxValue;
                    if (nd >= mn && nd < mx)
                        return t["class"]?.ToString() ?? "II";
                }
            }
            catch (Exception ex) { StingLog.Warn($"RecommendClass: {ex.Message}"); }
            return "II";
        }

        // ── Model validation ─────────────────────────────────────────

        public static IReadOnlyList<LpsComplianceItem> ValidateModel(Document doc)
        {
            var items = new List<LpsComplianceItem>();
            if (doc == null) return items;

            // Resolve project-wide configuration ----------------------
            var prjInfo = doc.ProjectInformation;
            string classId = ParameterHelpers.GetString(prjInfo, "ELC_LPS_CLASS_TXT");
            if (string.IsNullOrWhiteSpace(classId))
            {
                items.Add(new LpsComplianceItem
                {
                    Severity = LpsSeverity.Fail,
                    CheckName = "PROJECT_CLASS",
                    Message = "ELC_LPS_CLASS_TXT not set on Project Information. Run LPS Class Setup."
                });
                return items;
            }
            var classDef = LoadClass(classId);
            if (classDef == null)
            {
                items.Add(new LpsComplianceItem
                {
                    Severity = LpsSeverity.Fail,
                    CheckName = "PROJECT_CLASS",
                    Message = $"Unknown LPS class '{classId}'. Expected I / II / III / IV."
                });
                return items;
            }

            // Collect LPS elements ------------------------------------
            var airTerminals  = CollectLpsFamily(doc, "Air Terminal", "Air_Terminal", "Franklin", "Air-Terminal", "AT");
            var downConductors = CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor");
            var earthElectrodes = CollectLpsFamily(doc, "Earth", "Ground Rod", "GroundRod", "Earth_Rod", "Earth Electrode");

            // CHECK_2: Air terminal count -----------------------------
            items.Add(airTerminals.Count > 0
                ? new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "AIR_TERMINAL_COUNT",
                    Message = $"{airTerminals.Count} air terminal(s) placed." }
                : new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "AIR_TERMINAL_COUNT",
                    Message = "No air terminals found. LPS requires at least 1 air terminal per BS EN 62305-3." });

            // CHECK_3: Earth electrode count --------------------------
            items.Add(earthElectrodes.Count > 0
                ? new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "EARTH_ELECTRODE_COUNT",
                    Message = $"{earthElectrodes.Count} earth electrode(s) placed." }
                : new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "EARTH_ELECTRODE_COUNT",
                    Message = "No earth electrodes found. BS EN 62305-3 §5.4 requires at least one earth electrode per down conductor." });

            // CHECK_1: Down conductor spacing -------------------------
            if (downConductors.Count >= 2)
            {
                int violations = 0;
                var ids = new List<ElementId>();
                foreach (var dc in downConductors)
                {
                    double minDist = double.MaxValue;
                    var p = (dc.Location as LocationPoint)?.Point;
                    if (p == null) continue;
                    foreach (var other in downConductors)
                    {
                        if (other.Id == dc.Id) continue;
                        var op = (other.Location as LocationPoint)?.Point;
                        if (op == null) continue;
                        double d = Math.Sqrt(Math.Pow(p.X - op.X, 2) + Math.Pow(p.Y - op.Y, 2));
                        if (d < minDist) minDist = d;
                    }
                    double minDistM = UnitUtils.ConvertFromInternalUnits(minDist, UnitTypeId.Meters);
                    if (minDistM > classDef.DownConductorSpacingM)
                    {
                        violations++;
                        ids.Add(dc.Id);
                    }
                }
                items.Add(violations == 0
                    ? new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "DOWN_CONDUCTOR_SPACING",
                        Message = $"All {downConductors.Count} down conductors within {classDef.DownConductorSpacingM} m spacing." }
                    : new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "DOWN_CONDUCTOR_SPACING",
                        Message = $"{violations} down conductor(s) exceed {classDef.DownConductorSpacingM} m class {classId} spacing.",
                        ElementIds = ids });
            }
            else if (downConductors.Count == 1)
            {
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "DOWN_CONDUCTOR_SPACING",
                    Message = "Only 1 down conductor placed. BS EN 62305-3 requires minimum 2.",
                    ElementIds = downConductors.Select(d => d.Id).ToList() });
            }
            else
            {
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "DOWN_CONDUCTOR_SPACING",
                    Message = "No down conductors found." });
            }

            // CHECK_4: Earth resistance per electrode -----------------
            if (earthElectrodes.Count > 0)
            {
                var bad = new List<ElementId>();
                int unread = 0;
                foreach (var el in earthElectrodes)
                {
                    double r = GetDoubleParam(el, "ELC_LPS_EARTH_RESISTANCE_OHM");
                    if (r <= 0) { unread++; continue; }
                    if (r > classDef.EarthResistanceTargetOhm) bad.Add(el.Id);
                }
                if (bad.Count > 0)
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "EARTH_RESISTANCE",
                        Message = $"{bad.Count} electrode(s) exceed {classDef.EarthResistanceTargetOhm} ohm target.",
                        ElementIds = bad });
                }
                else if (unread > 0)
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Warn, CheckName = "EARTH_RESISTANCE",
                        Message = $"{unread} electrode(s) have no resistance reading — physical test required." });
                }
                else
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "EARTH_RESISTANCE",
                        Message = $"All {earthElectrodes.Count} electrodes within {classDef.EarthResistanceTargetOhm} ohm target." });
                }
            }

            // CHECK_5: Separation distance stamped on down conductors -
            if (downConductors.Count > 0)
            {
                int missing = 0;
                var ids = new List<ElementId>();
                foreach (var dc in downConductors)
                {
                    double s = GetDoubleParam(dc, "ELC_LPS_SEPARATION_DISTANCE_MM");
                    if (s <= 0) { missing++; ids.Add(dc.Id); }
                }
                if (missing == 0)
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "SEPARATION_DISTANCE_STAMP",
                        Message = "All down conductors have separation distance computed." });
                }
                else
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Warn, CheckName = "SEPARATION_DISTANCE_STAMP",
                        Message = $"{missing} down conductor(s) missing ELC_LPS_SEPARATION_DISTANCE_MM. Run Sep Distance check.",
                        ElementIds = ids });
                }
            }

            // CHECK_6: Zone tag on rooms ------------------------------
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(r => (r as Room)?.Area > 0.0)
                .ToList();
            int taggedRooms = rooms.Count(r => !string.IsNullOrWhiteSpace(
                ParameterHelpers.GetString(r, "ELC_LPS_ZONE_TXT")));
            if (rooms.Count == 0)
            {
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Warn, CheckName = "ROOM_LPZ_TAG",
                    Message = "No rooms found in model — cannot verify LPZ zoning." });
            }
            else if (taggedRooms == rooms.Count)
            {
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "ROOM_LPZ_TAG",
                    Message = $"All {rooms.Count} rooms have ELC_LPS_ZONE_TXT assigned." });
            }
            else
            {
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Warn, CheckName = "ROOM_LPZ_TAG",
                    Message = $"{rooms.Count - taggedRooms} of {rooms.Count} rooms have no LPZ assigned. Run Zone Tag Rooms." });
            }

            // CHECK_7: Inspection date freshness ----------------------
            string testDate = ParameterHelpers.GetString(prjInfo, "ELC_LPS_TEST_DATE_TXT");
            if (string.IsNullOrWhiteSpace(testDate))
            {
                items.Add(new LpsComplianceItem { Severity = LpsSeverity.Warn, CheckName = "TEST_DATE",
                    Message = "ELC_LPS_TEST_DATE_TXT not set on Project Information." });
            }
            else if (DateTime.TryParse(testDate, out var dt))
            {
                int monthsOld = ((DateTime.Today.Year - dt.Year) * 12) + DateTime.Today.Month - dt.Month;
                if (monthsOld > classDef.InspectionIntervalMonths)
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Warn, CheckName = "TEST_DATE",
                        Message = $"Last test {monthsOld} months ago — exceeds {classDef.InspectionIntervalMonths} month interval." });
                }
                else
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "TEST_DATE",
                        Message = $"Last test {monthsOld} month(s) ago, within interval." });
                }
            }

            return items;
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Collect FamilyInstances in OST_ElectricalEquipment + OST_GenericModel
        /// whose family name contains any of the given substrings (case-insensitive).
        /// Used to identify LPS components since Revit has no native LPS category.
        /// </summary>
        public static List<FamilyInstance> CollectLpsFamily(Document doc, params string[] keywords)
        {
            var result = new List<FamilyInstance>();
            if (doc == null || keywords == null || keywords.Length == 0) return result;

            var seen = new HashSet<ElementId>();
            BuiltInCategory[] cats = { BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_GenericModel };
            foreach (var bic in cats)
            {
                try
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>();
                    foreach (var fi in collector)
                    {
                        if (fi == null || !seen.Add(fi.Id)) continue;
                        string famName = fi.Symbol?.FamilyName ?? string.Empty;
                        string typeName = fi.Symbol?.Name ?? string.Empty;
                        if (keywords.Any(k => famName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0
                                           || typeName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            result.Add(fi);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CollectLpsFamily {bic}: {ex.Message}"); }
            }
            return result;
        }

        // ── JSON loading ─────────────────────────────────────────────

        private static void EnsureLoaded()
        {
            if (_classes != null && _flashDensity != null && _riskFactors != null) return;
            lock (_lock)
            {
                if (_classes == null)
                {
                    var dict = new Dictionary<string, LpsClassDef>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var json = LoadJson("STING_LPS_CLASSES.json");
                        var arr = json?["classes"] as JArray;
                        if (arr != null)
                        {
                            foreach (var c in arr)
                            {
                                var def = new LpsClassDef
                                {
                                    Id = c["id"]?.ToString() ?? "II",
                                    RollingSphereRadiusM = c["rollingSphereRadiusM"]?.Value<double>() ?? 30,
                                    MeshSizeM = c["meshSizeM"]?.Value<double>() ?? 10,
                                    ProtectionAngleDeg10m = c["protectionAngleDeg_10m"]?.Value<double>() ?? 0,
                                    ProtectionAngleDeg20m = c["protectionAngleDeg_20m"]?.Value<double>() ?? 0,
                                    ProtectionAngleDeg30m = c["protectionAngleDeg_30m"]?.Value<double>() ?? 0,
                                    ProtectionAngleDeg45m = c["protectionAngleDeg_45m"]?.Value<double>() ?? 0,
                                    ProtectionAngleDeg60m = c["protectionAngleDeg_60m"]?.Value<double>() ?? 0,
                                    DownConductorSpacingM = c["downConductorSpacingM"]?.Value<double>() ?? 10,
                                    MinConductorCrossSectionMm2 = c["minConductorCrossSectionMm2"]?.Value<double>() ?? 50,
                                    InspectionIntervalMonths = c["inspectionIntervalMonths"]?.Value<int>() ?? 12,
                                    EarthResistanceTargetOhm = c["earthResistanceTargetOhm"]?.Value<double>() ?? 10.0,
                                    KiFactor = c["kiFactor"]?.Value<double>() ?? 0.06
                                };
                                dict[def.Id.ToUpperInvariant()] = def;
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Error("Failed loading STING_LPS_CLASSES.json", ex); }
                    if (dict.Count == 0)
                    {
                        // Hardcoded fallback so the engine still runs without data files.
                        dict["II"] = new LpsClassDef
                        {
                            Id = "II", RollingSphereRadiusM = 30, MeshSizeM = 10,
                            ProtectionAngleDeg10m = 72, ProtectionAngleDeg20m = 35,
                            DownConductorSpacingM = 10, MinConductorCrossSectionMm2 = 50,
                            InspectionIntervalMonths = 12, EarthResistanceTargetOhm = 10.0,
                            KiFactor = 0.06
                        };
                    }
                    _classes = dict;
                }
                if (_flashDensity == null)
                {
                    try { _flashDensity = LoadJson("STING_LPS_FLASH_DENSITY.json") ?? new JObject(); }
                    catch (Exception ex) { StingLog.Warn($"Flash density load: {ex.Message}"); _flashDensity = new JObject(); }
                }
                if (_riskFactors == null)
                {
                    try { _riskFactors = LoadJson("STING_LPS_RISK_FACTORS.json") ?? new JObject(); }
                    catch (Exception ex) { StingLog.Warn($"Risk factors load: {ex.Message}"); _riskFactors = new JObject(); }
                }
            }
        }

        /// <summary>
        /// Read a numeric parameter as double, handling Integer / Double / String storage.
        /// Returns 0.0 if the parameter is missing, empty, or non-numeric.
        /// </summary>
        public static double GetDoubleParam(Element el, string paramName)
        {
            if (el == null || string.IsNullOrEmpty(paramName)) return 0.0;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0.0;
                switch (p.StorageType)
                {
                    case StorageType.Double:  return p.AsDouble();
                    case StorageType.Integer: return p.AsInteger();
                    case StorageType.String:
                        if (double.TryParse(p.AsString(), out double v)) return v;
                        return 0.0;
                    default: return 0.0;
                }
            }
            catch (Exception ex) { StingLog.Warn($"GetDoubleParam {paramName}: {ex.Message}"); return 0.0; }
        }

        private static JObject LoadJson(string fileName)
        {
            string path = StingToolsApp.FindDataFile(fileName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StingLog.Warn($"LpsEngine: data file not found: {fileName}");
                return null;
            }
            return JObject.Parse(File.ReadAllText(path));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  POCOs
    // ══════════════════════════════════════════════════════════════════

    public class LpsClassDef
    {
        public string Id { get; set; }
        public double RollingSphereRadiusM { get; set; }
        public double MeshSizeM { get; set; }
        public double ProtectionAngleDeg10m { get; set; }
        public double ProtectionAngleDeg20m { get; set; }
        public double ProtectionAngleDeg30m { get; set; }
        public double ProtectionAngleDeg45m { get; set; }
        public double ProtectionAngleDeg60m { get; set; }
        public double DownConductorSpacingM { get; set; }
        public double MinConductorCrossSectionMm2 { get; set; }
        public int InspectionIntervalMonths { get; set; }
        public double EarthResistanceTargetOhm { get; set; }
        public double KiFactor { get; set; }
    }

    public class LpsRiskInput
    {
        public string BuildingTypeId { get; set; }
        public string InternalContentId { get; set; }
        public string OccupantHazardId { get; set; }
        public string ConsequenceId { get; set; }
        public string LossTypeId { get; set; } = "L1";
        public string RegionId { get; set; }
        public double GroundFlashDensity { get; set; } = 2.0;
        public double PlanLengthM { get; set; }
        public double PlanWidthM { get; set; }
        public double PlanAreaM2 { get; set; }
        public double HeightM { get; set; }
        // Cb / Cc / Cd (occupant) / Ce / Cd (location) coefficients
        public double BuildingTypeCb { get; set; } = 1.0;
        public double InternalContentCc { get; set; } = 1.0;
        public double OccupantHazardCd { get; set; } = 1.0;
        public double ConsequenceCe { get; set; } = 1.0;
        public double LocationFactorCd { get; set; } = 1.0;
        public double TolerableRisk { get; set; } = 1e-5;
        public List<string> ConnectedServices { get; set; } = new List<string>();
    }

    public class LpsRiskResult
    {
        public bool RequiresLps { get; set; }
        public string RecommendedClass { get; set; } = "II";
        public double AnnualStrikeFrequency { get; set; }
        public double CollectionAreaM2 { get; set; }
        public double TolerableRisk { get; set; }
        public Dictionary<string, double> RiskComponents { get; } = new Dictionary<string, double>();
        public string Notes { get; set; }
    }

    public enum LpsSeverity { Pass, Warn, Fail }

    public class LpsComplianceItem
    {
        public LpsSeverity Severity { get; set; }
        public string CheckName { get; set; }
        public string Message { get; set; }
        public List<ElementId> ElementIds { get; set; } = new List<ElementId>();
    }

}
