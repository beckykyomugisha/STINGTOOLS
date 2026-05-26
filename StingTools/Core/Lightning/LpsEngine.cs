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
using StingTools.Core;

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

        /// <summary>
        /// km — the insulation/medium factor for the separation distance per
        /// BS EN 62305-3 §6.3 / Table 5. This is the material BETWEEN the LPS
        /// conductor and the internal metalwork (air → 1.0, solid material such
        /// as concrete / brick / wood → 0.5), NOT the conductor metal. Unknown
        /// or unspecified media default to air (1.0) — the conservative choice
        /// because a larger km divisor would reduce the required separation.
        /// </summary>
        public static double GetMaterialFactor(string insulationMedium)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(insulationMedium)) return 1.0;
            string norm = insulationMedium.Trim().ToUpperInvariant();
            try
            {
                var classesJson = LoadJson("STING_LPS_CLASSES.json");
                var mf = classesJson?["materialFactors"] as JObject;
                if (mf != null && mf[norm] != null)
                    return mf[norm].Value<double>();
            }
            catch (Exception ex) { StingLog.Warn($"GetMaterialFactor: {ex.Message}"); }
            // Unknown medium → assume air (km = 1.0), the conservative default.
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
                // Tier 1 — project-level explicit override
                if (doc?.ProjectInformation != null)
                {
                    double ov = GetDoubleParam(doc.ProjectInformation,
                        Fabrication.LpsParams.PROJECT_NG_OVERRIDE_NR);
                    if (ov > 0) return ov;
                }
                // Tier 2 — Wave E #17 climate-site latitude estimate
                // (when HVAC's ClimateRegistry has a site for the project).
                double climate = LpsRegionalNg.EstimateFromClimate(doc);
                if (climate > 0) return climate;
            }
            catch (Exception ex) { StingLog.Warn($"GetEffectiveFlashDensity: {ex.Message}"); }
            // Tier 3 — regional default from STING_LPS_FLASH_DENSITY.json
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
        /// Down-conductor length in metres derived from the family bounding-box
        /// Z-extent. Returns 3.0 m as a safe fallback when geometry is missing
        /// or fails to evaluate. Replaces the duplicate per-command helpers.
        /// </summary>
        public static double GetConductorLengthM(FamilyInstance fi)
        {
            try
            {
                if (fi == null) { StingLog.Warn("GetConductorLengthM: fi was null — returning 3.0m fallback"); return 3.0; }
                var bb = fi.get_BoundingBox(null);
                if (bb == null) { StingLog.Warn($"GetConductorLengthM: no bbox on {fi.Id} — returning 3.0m fallback"); return 3.0; }
                double zFt = bb.Max.Z - bb.Min.Z;
                double zM = UnitUtils.ConvertFromInternalUnits(zFt, UnitTypeId.Meters);
                if (zM <= 0.1)
                {
                    StingLog.Warn($"GetConductorLengthM: bbox Z too small ({zM:F3}m) on {fi.Id} — returning 3.0m fallback");
                    return 3.0;
                }
                return zM;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetConductorLengthM: {ex.Message}");
                return 3.0;
            }
        }

        /// <summary>
        /// kc factor per BS EN 62305-3 §6.3 / Annex C.3 — partitioning of the
        /// lightning current among parallel down conductors. Uses the
        /// standard simplified table:
        ///   n = 1                          → kc = 1.00
        ///   n = 2                          → kc = 0.66
        ///   n ≥ 3 with a ring conductor    → kc = 0.44   (Type B / ring
        ///         interconnecting the down conductors, equipotential bonding)
        ///   n ≥ 3 without a ring           → kc = 0.66   (conservative — the
        ///         current does not divide as favourably without interconnection)
        /// The full §C.3 expression kc = 1/(2n) + 0.1 + 0.2·³√(c/h) requires the
        /// conductor spacing c and structure height h; this table is the
        /// documented simplification used when that geometry is not supplied.
        /// Backwards-compatible: the int-only overload preserves the legacy
        /// 1/n behaviour for callers still using it.
        /// </summary>
        public static double ComputeKcFactor(int n, bool ringConductor, bool equipotentialBonding)
        {
            if (n <= 1) return 1.0;
            if (n == 2) return 0.66;
            // n >= 3: the favourable 0.44 only applies when a ring conductor
            // interconnects the down conductors (Type B arrangement). Without
            // that interconnection keep the conservative two-conductor value.
            return (ringConductor && equipotentialBonding) ? 0.44 : 0.66;
        }

        /// <summary>Legacy 1/n form — preserved for backwards compatibility.</summary>
        public static double ComputeKcFactor(int n)
        {
            if (n <= 1) return 1.0;
            return Math.Max(1.0 / n, 0.1);
        }

        /// <summary>
        /// Separation distance s = ki · (kc / km) · l per BS EN 62305-3 §6.3.
        /// <paramref name="insulationMedium"/> is the material BETWEEN the LPS
        /// conductor and the internal metalwork (air → km 1.0, solid such as
        /// concrete / brick / wood → km 0.5) — NOT the conductor metal. Pass
        /// "AIR" (the default) for an externally-routed down conductor in air.
        /// kc defaults to 1.0 for a single down conductor path. Returns
        /// millimetres; caller passes conductor length in metres.
        /// </summary>
        public static double ComputeSeparationDistance(
            string classId,
            double conductorLengthFromAirTerminalToNearestBondM,
            string insulationMedium = "AIR",
            double kc = 1.0)
        {
            var def = LoadClass(classId);
            if (def == null) return 0.0;
            double km = GetMaterialFactor(insulationMedium);
            if (km <= 0.0) km = 1.0;
            double s_m = def.KiFactor * (kc / km) * conductorLengthFromAirTerminalToNearestBondM;
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

        /// <summary>
        /// BS EN 62305-2 risk assessment. Delegates to the full component
        /// model (R = ΣRA..RZ over RA/RB/RC/RM/RU/RV/RW/RZ) implemented in
        /// <see cref="LpsRiskModel"/>. Kept as the public entry point so the
        /// existing panel / dialog callers are unchanged; they automatically
        /// get the component-summed risk plus the per-component breakdown.
        /// </summary>
        public static LpsRiskResult RunRiskAssessment(LpsRiskInput input)
        {
            EnsureLoaded();
            return LpsRiskModel.Compute(input);
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

            // CHECK_8: Conductor cross-section -----------------------
            if (downConductors.Count > 0)
            {
                var underId = new List<ElementId>();
                var unsetId = new List<ElementId>();
                int okCount = 0;
                foreach (var dc in downConductors)
                {
                    string mat = ParameterHelpers.GetString(dc, "ELC_LPS_CONDUCTOR_MATERIAL_TXT");
                    if (string.IsNullOrWhiteSpace(mat)) mat = "COPPER";
                    double minMm2 = classDef.MinConductorCrossSectionMm2;
                    if (classDef.MinConductorCrossSectionMm2ByMaterial != null &&
                        classDef.MinConductorCrossSectionMm2ByMaterial.TryGetValue(mat.ToUpperInvariant(), out int matMin) &&
                        matMin > 0)
                    {
                        minMm2 = matMin;
                    }
                    double cs = GetDoubleParam(dc, "ELC_LPS_CONDUCTOR_CROSS_SECT_MM2");
                    if (cs <= 0) { unsetId.Add(dc.Id); continue; }
                    if (cs < minMm2) underId.Add(dc.Id); else okCount++;
                }
                if (underId.Count > 0)
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Fail, CheckName = "CONDUCTOR_CROSS_SECTION",
                        Message = $"{underId.Count} conductor(s) below class {classId} minimum cross-section (material-aware).",
                        ElementIds = underId });
                }
                else if (unsetId.Count > 0)
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Warn, CheckName = "CONDUCTOR_CROSS_SECTION",
                        Message = $"{unsetId.Count} conductor(s) missing ELC_LPS_CONDUCTOR_CROSS_SECT_MM2 — set per material spec.",
                        ElementIds = unsetId });
                }
                else
                {
                    items.Add(new LpsComplianceItem { Severity = LpsSeverity.Pass, CheckName = "CONDUCTOR_CROSS_SECTION",
                        Message = $"All {okCount} conductor(s) meet class {classId} minimum cross-section." });
                }
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
        /// using a two-pass strategy: (1) exact match against
        /// <c>ELC_LPS_ELEMENT_TYPE_TXT</c> for any keyword that maps to a known
        /// element-type tag (AIR_TERMINAL, DOWN_CONDUCTOR, EARTH_ELECTRODE,
        /// BONDING_BAR, SPD); (2) fallback substring match against family/type
        /// name. Results are de-duplicated by ElementId.
        /// </summary>
        public static List<FamilyInstance> CollectLpsFamily(Document doc, params string[] keywords)
        {
            var result = new List<FamilyInstance>();
            if (doc == null || keywords == null || keywords.Length == 0) return result;

            // Map keyword sets to canonical element-type tags so the param-based
            // pass can resolve the same logical group across legacy callers.
            var typeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in keywords)
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (k.IndexOf("Air Terminal", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("Air_Terminal", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("Franklin", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.Equals("AT", StringComparison.OrdinalIgnoreCase)
                 || k.IndexOf("Air-Terminal", StringComparison.OrdinalIgnoreCase) >= 0)
                    typeTags.Add("AIR_TERMINAL");
                if (k.IndexOf("Down Conductor", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("Down_Conductor", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("DownConductor", StringComparison.OrdinalIgnoreCase) >= 0)
                    typeTags.Add("DOWN_CONDUCTOR");
                if (k.IndexOf("Earth", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("Ground Rod", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("GroundRod", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("Earth_Rod", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("Earth Electrode", StringComparison.OrdinalIgnoreCase) >= 0)
                    typeTags.Add("EARTH_ELECTRODE");
                if (k.IndexOf("Bonding", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("Bond Bar", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("BondingBar", StringComparison.OrdinalIgnoreCase) >= 0)
                    typeTags.Add("BONDING_BAR");
                if (k.IndexOf("SPD", StringComparison.OrdinalIgnoreCase) >= 0
                 || k.IndexOf("Surge", StringComparison.OrdinalIgnoreCase) >= 0)
                    typeTags.Add("SPD");
            }

            var seen = new HashSet<ElementId>();
            BuiltInCategory[] cats = { BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_GenericModel };

            // Pass 1 — match by ELC_LPS_ELEMENT_TYPE_TXT exact value
            if (typeTags.Count > 0)
            {
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
                            if (fi == null || seen.Contains(fi.Id)) continue;
                            string et = ParameterHelpers.GetString(fi, "ELC_LPS_ELEMENT_TYPE_TXT");
                            if (!string.IsNullOrWhiteSpace(et) && typeTags.Contains(et.Trim()))
                            {
                                seen.Add(fi.Id);
                                result.Add(fi);
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"CollectLpsFamily pass1 {bic}: {ex.Message}"); }
                }
            }

            // Pass 2 — fallback substring match on family / type name
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
                catch (Exception ex) { StingLog.Warn($"CollectLpsFamily pass2 {bic}: {ex.Message}"); }
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
                                    KiFactor = c["kiFactor"]?.Value<double>() ?? 0.06,
                                    ProtectionEfficiency = c["protectionEfficiency"]?.Value<double>() ?? 0.95
                                };
                                // Per-material minimum cross-section (optional)
                                var byMat = c["minConductorCrossSectionMm2ByMaterial"] as JObject;
                                if (byMat != null)
                                {
                                    foreach (var kv in byMat)
                                    {
                                        try
                                        {
                                            int v = kv.Value?.Value<int>() ?? 0;
                                            if (v > 0)
                                                def.MinConductorCrossSectionMm2ByMaterial[kv.Key.ToUpperInvariant()] = v;
                                        }
                                        catch (Exception ex) { StingLog.Warn($"minConductorCrossSection map: {ex.Message}"); }
                                    }
                                }
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
        /// <summary>
        /// Optional per-material override of the minimum conductor cross-section
        /// (mm²). Keys: COPPER / ALUMINIUM / STEEL. Falls back to
        /// <see cref="MinConductorCrossSectionMm2"/> when the material is not listed.
        /// </summary>
        public Dictionary<string, int> MinConductorCrossSectionMm2ByMaterial { get; set; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int InspectionIntervalMonths { get; set; }
        public double EarthResistanceTargetOhm { get; set; }
        public double KiFactor { get; set; }
        /// <summary>
        /// Protection efficiency PE per BS EN 62305-2 Table 6 — used for
        /// residual risk (R1 × (1 − PE)). I=0.98, II=0.95, III=0.90, IV=0.80.
        /// </summary>
        public double ProtectionEfficiency { get; set; }
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
        /// <summary>
        /// Wave A #5 — explicit override for Ae (m²) for non-rectangular
        /// geometries (L-shape, courtyard, multi-volume). When > 0,
        /// RunRiskAssessment uses this directly and skips the Annex A.2
        /// rectangular formula. Set 0 to use the rectangular calc.
        /// </summary>
        public double AeOverrideM2 { get; set; }
        // Cb / Cc / Cd (occupant) / Ce / Cd (location) coefficients.
        // Used by the legacy screening weighting and to derive loss-model
        // categories when the structured *Id fields are not supplied.
        public double BuildingTypeCb { get; set; } = 1.0;
        public double InternalContentCc { get; set; } = 1.0;
        public double OccupantHazardCd { get; set; } = 1.0;
        public double ConsequenceCe { get; set; } = 1.0;
        public double LocationFactorCd { get; set; } = 1.0;   // C_D (Table A.1)
        public double TolerableRisk { get; set; } = 1e-5;
        public List<string> ConnectedServices { get; set; } = new List<string>();

        // ── Full BS EN 62305-2 component-model inputs ─────────────────
        // All optional; when unset the model falls back to the structured
        // *Id fields, then to the numeric coefficients above, then to the
        // standard's conservative defaults (see STING_LPS_RISK_TABLES.json).

        /// <summary>LPS class assumed present for a "verify current design" run.
        /// The risk-need assessment computes the UNPROTECTED baseline (P_B = 1)
        /// regardless, then selects a class from residual risk.</summary>
        public string LpsClassPresent { get; set; } = "NONE";
        /// <summary>Coordinated SPD protection level (NONE / III-IV / II / I /
        /// BETTER / BEST) — P_SPD (Table B.3) and P_EB (Table B.7).</summary>
        public string SpdProtectionLevel { get; set; } = "NONE";
        /// <summary>Ground/floor surface key for r_t (Table C.3): AGRICULTURAL /
        /// MARBLE_CONCRETE / GRAVEL_CARPET / ASPHALT_WOOD.</summary>
        public string SoilSurfaceType { get; set; } = "MARBLE_CONCRETE";
        /// <summary>Fire-protection provisions for r_p (Table C.4): NONE /
        /// EXTINGUISHERS_ALARM / FIRE_BRIGADE_AUTO.</summary>
        public string FireProtection { get; set; } = "NONE";
        /// <summary>Fire/explosion risk for r_f (Table C.5): EXPLOSION / HIGH /
        /// ORDINARY / LOW / NONE. Empty → resolved from building / content.</summary>
        public string FireRisk { get; set; } = "";
        /// <summary>Special-hazard key for h_z (Table C.6): NONE / LOW_PANIC /
        /// MEDIUM_PANIC / HIGH_PANIC / ENV_CONTAMINATION. Empty → resolved from
        /// occupant hazard / building use.</summary>
        public string SpecialHazard { get; set; } = "";
        /// <summary>Internal wiring routed in a shield/conduit (legacy flag;
        /// prefer <see cref="WiringType"/> for the full K_S3 table).</summary>
        public bool WiringShielded { get; set; } = false;
        /// <summary>Structure provides a spatial magnetic shield (legacy flag;
        /// prefer <see cref="SpatialShieldMeshWidthM"/>).</summary>
        public bool StructureShielded { get; set; } = false;
        /// <summary>Mesh width w_m1 (m) of the large-scale spatial shield
        /// (LPS grid). K_S1 = 0.12·w_m1 (Annex B.5). 0 ⇒ no shield (K_S1 = 1).</summary>
        public double SpatialShieldMeshWidthM { get; set; }
        /// <summary>Mesh width w_m2 (m) of the inner-LPZ shield.
        /// K_S2 = 0.12·w_m2 (Annex B.5). 0 ⇒ no inner shield (K_S2 = 1).</summary>
        public double InternalShieldMeshWidthM { get; set; }
        /// <summary>Internal-wiring routing/shielding key for K_S3 (Table B.5):
        /// UNSHIELDED_NO_PRECAUTION / UNSHIELDED_LOOP_PRECAUTION /
        /// UNSHIELDED_CLOSE_BONDING / SHIELDED_RS_5_20 / SHIELDED_RS_LE_5.
        /// Empty ⇒ derived from <see cref="WiringShielded"/>.</summary>
        public string WiringType { get; set; } = "";
        /// <summary>Rated impulse withstand voltage of internal systems, kV
        /// (K_S4 = 1/U_w, Annex B.5; also reduces P_LD / P_LI). Default 1.5 kV.</summary>
        public double UwKv { get; set; } = 1.5;
        /// <summary>Internal systems endanger life on failure (hospital ICU,
        /// explosion) → L_O contributes to R1. Empty → resolved from use.</summary>
        public bool? LifeEndangeringSystems { get; set; }
        /// <summary>Persons in the zone / total (n_z / n_t). 0 ⇒ factor 1.</summary>
        public double PersonsInZone { get; set; }
        public double PersonsTotal { get; set; }
        /// <summary>Hours per year the zone is occupied (t_z). 0 ⇒ 8760 (factor 1).</summary>
        public double OccupiedHoursPerYear { get; set; }
        /// <summary>Connected service lines. null/empty ⇒ a default power +
        /// telecom pair (or derived from <see cref="ConnectedServices"/>).</summary>
        public List<LpsServiceLine> Lines { get; set; }
    }

    /// <summary>
    /// A connected service line for the BS EN 62305-2 line-related risk
    /// components (R_U / R_V / R_W / R_Z). Lengths in metres; factor keys map
    /// into STING_LPS_RISK_TABLES.json (lineCi / lineCe / lineCt / pLD / pLI).
    /// </summary>
    public class LpsServiceLine
    {
        public string Id { get; set; } = "POWER";
        public double LengthM { get; set; } = 1000.0;        // L_L (default 1000 m when unknown)
        public string Install { get; set; } = "AERIAL";      // C_I (Table A.4)
        public string Environment { get; set; } = "SUBURBAN"; // C_E (Table A.5)
        public string Transformer { get; set; } = "NONE";    // C_T (Table A.3)
        public string Shield { get; set; } = "UNSHIELDED";   // P_LD / P_LI key (Tables B.8 / B.9)
    }

    public class LpsRiskResult
    {
        public bool RequiresLps { get; set; }
        public string RecommendedClass { get; set; } = "II";
        /// <summary>Minimal coordinated SPD protection level (NONE / III-IV /
        /// II / I / BETTER / BEST) needed — alongside the recommended LPS
        /// class — to bring every loss type's risk below its tolerable
        /// threshold. SPDs (not the LPS class) drive the surge-related
        /// components, so this is what clears the surge-dominated R2 / R4.</summary>
        public string RecommendedSpdLevel { get; set; } = "NONE";
        public double AnnualStrikeFrequency { get; set; }
        public double CollectionAreaM2 { get; set; }
        public double TolerableRisk { get; set; }
        public Dictionary<string, double> RiskComponents { get; } = new Dictionary<string, double>();
        /// <summary>
        /// Per-loss-type annual risk (L1 — life, L2 — service, L3 —
        /// cultural, L4 — economic). Populated by RunRiskAssessment
        /// when extended risk model runs; used by the inline RISK
        /// tab to show all four loss-type gates.
        /// </summary>
        public Dictionary<string, double> RiskByLossType { get; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Per-loss-type tolerable risk Rt (BS EN 62305-2 Table 7).
        /// L1=1e-5 / L2=1e-3 / L3=1e-4 / L4=1e-3 by default; can be
        /// overridden via STING_LPS_RISK_FACTORS.json lossTypes[].rt.
        /// </summary>
        public Dictionary<string, double> TolerableByLossType { get; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// True when the per-loss-type risk exceeds its tolerable
        /// threshold. The headline RequiresLps is true if ANY entry
        /// here is true.
        /// </summary>
        public Dictionary<string, bool> ExceedsByLossType { get; }
            = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Residual annual risk per LPS class after the protection efficiency
        /// is applied: R_residual = R_worst × (1 − PE). Keys are class IDs (I, II, III, IV).
        /// Worst-case is the maximum of R1..R4 so the residual gate is conservative.
        /// </summary>
        public Dictionary<string, double> ResidualRiskByClass { get; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// BS EN 62305-2 risk components for the headline loss type (RA, RB,
        /// RC, RM, RU, RV, RW, RZ). Populated by the full component model so
        /// the panel can show which damage path dominates. Empty when the
        /// legacy screening path is used.
        /// </summary>
        public Dictionary<string, double> ComponentBreakdown { get; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
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
