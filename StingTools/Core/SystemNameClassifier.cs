// SystemNameClassifier.cs — the one place that turns a Revit MEP system name
// ("Hydronic Supply", "Storm Drainage", "Medical Oxygen") into a STING SYS code.
//
// Revit-free so StingTools.Tags.Tests can <Compile Include> it. TagConfig's
// connector and system-type layers both call FromSystemName.
//
// WHY THIS EXISTS (2026-09-28)
//
// The mapping lived inside TagConfig (Revit-bound), so it could only be tested by
// reading its source text for the order of return statements. Three wrong answers
// had survived that way:
//
//  * Revit's own default piping systems "Hydronic Supply" and "Hydronic Return"
//    matched no rule, so their pipes fell through to the category default and were
//    tagged as domestic cold water (DCW).
//  * The sanitary rule's "DRAIN" ran before the rainwater rules, so "Storm Drainage",
//    "Roof Drain" and "Surface Water Drainage" were tagged as foul drainage (SAN).
//  * Medical gas had no system. "Medical Gas O2" contains "GAS" and was tagged as
//    natural gas (GAS); "Oxygen" or "Medical Vacuum" matched nothing and became DCW.
//    Medical gas is now SYS=MGS, with the gas as the FUNC token.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core.Plumbing;

namespace StingTools.Core
{
    public static class SystemNameClassifier
    {
        /// <summary>SYS code for medical gas pipeline systems (HTM 02-01 / ISO 7396-1).</summary>
        public const string MedicalGasSys = "MGS";

        // Words that make an otherwise ambiguous gas word ("air", "vacuum") medical.
        // Room words (surgical, anaesthetic, clinical) are not markers: "Anaesthetic Room
        // Extract" is ventilation.
        private static readonly string[] MedicalMarkers =
            { "MEDICAL", "MGPS", "MEDGAS", "MED GAS", "MED AIR", "MED VAC" };

        // Ductwork never carries medical gas, whatever its system is called.
        private static readonly HashSet<string> AirCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "Ducts", "Duct Fittings", "Duct Accessories", "Duct Insulation", "Duct Lining", "Flex Ducts",
            "Air Terminals", "MEP Fabrication Ductwork", "Analytical Duct Segments",
        };

        // Gas words that are medical on their own. "AIR", "VAC", "N2", "CO2", "HE" are not:
        // supply air, a vacuum-drainage system and CO2 fire suppression are not medical gas.
        private static readonly HashSet<string> UnambiguousGasWords =
            new HashSet<string>(StringComparer.Ordinal) { "O2", "OXYGEN", "N2O", "NITROUS", "ENTONOX", "MA4", "MA7", "AGS", "AGSS", "HELIOX" };

        /// <summary>
        /// True when an MEP system name is a medical gas system. <paramref name="gasCode"/>
        /// is its gas in the MGS_GAS_TYPE_TXT vocabulary (MedicalGasFixtures.GasCodes), or
        /// null when the name says medical gas but not which gas.
        /// </summary>
        public static bool TryMedicalGas(string systemName, out string gasCode)
        {
            gasCode = null;
            string upper = (systemName ?? "").ToUpperInvariant();
            if (upper.Trim().Length == 0) return false;

            var words = Regex.Split(upper, "[^A-Z0-9]+").Where(w => w.Length > 0).ToList();
            bool medical = MedicalMarkers.Any(m => upper.Contains(m));
            bool scavenging = upper.Contains("SCAVENG");

            foreach (string w in words)
                if (UnambiguousGasWords.Contains(w)) { gasCode = MedicalGasFixtures.CanonicalGasCode(w); return true; }
            if (scavenging) { gasCode = "AGS"; return true; }
            // Surgical air is the 700 kPa medical supply; the phrase is enough on its own.
            if (upper.Contains("SURGICAL AIR") || upper.Contains("SURGAIR")) { gasCode = "MA7"; return true; }
            if (!medical) return false;
            if (upper.Contains("INSTRUMENT AIR")) { gasCode = "MA7"; return true; }
            foreach (string w in words)
            {
                string g = MedicalGasFixtures.CanonicalGasCode(w);
                if (g != null) { gasCode = g; return true; }
            }
            return true;
        }

        // ── Systems read off a FAMILY name: high voltage, BMS, radiation protection ──
        //
        // These three were real systems in STING_FUNC_SYS_MATRIX.csv and in the PROD table's
        // SYSTEM column, but not in the runtime SysMap, so the tagger never wrote them and
        // the validator rejected them (2026-09-28). They carry no piping or duct system, so
        // the family / type name is what identifies them.

        public const string HighVoltageSys = "HV";
        public const string BmsSys = "BMS";
        public const string RadiationSys = "RAD";

        private static List<string> Words(string upper) => Regex.Split(upper, "[^A-Z0-9]+").Where(w => w.Length > 0).ToList();

        private static bool HasAny(string upper, params string[] phrases) => phrases.Any(upper.Contains);

        /// <summary>
        /// HV, BMS or RAD for a family / type name, or null. Checked before the LV
        /// patterns, so an HV switch panel is not filed as LV.
        /// </summary>
        public static string FromFamilyName(string familyAndType)
        {
            string upper = (familyAndType ?? "").ToUpperInvariant();
            if (upper.Trim().Length == 0) return null;
            var words = Words(upper);

            // Radiation protection (NCRP 147 / IPEM 75): shielding, controlled areas and the
            // sources that make them necessary.
            if (HasAny(upper, "RADIATION", "LEAD LINED", "LEAD-LINED", "LEAD SHIELD", "LEAD GLASS", "X-RAY", "XRAY",
                       "C-ARM", "LINEAR ACCELERATOR", "LINAC", "FARADAY", "RF SHIELD", "DOSIMET", "5 GAUSS", "GAUSS LINE",
                       "CT SCANNER", "FLUOROSCOP", "MAMMOGRAPH")
                // "CT" alone is also a current transformer, so only the phrase counts.
                || words.Contains("MRI") || words.Contains("GAMMA"))
                return RadiationSys;

            // Building management / BACS (BS EN ISO 16484).
            if (HasAny(upper, "BUILDING MANAGEMENT", "BACNET", "OUTSTATION")
                || words.Contains("BMS") || words.Contains("BACS") || words.Contains("DDC"))
                return BmsSys;

            // High voltage (above 1 kV): HV / MV switchgear, ring main units, the main
            // HV/LV transformer. An isolating, control or IPS transformer is LV.
            if (HasAny(upper, "HIGH VOLTAGE", "MEDIUM VOLTAGE", "RING MAIN")
                || words.Contains("HV") || words.Contains("MV") || words.Contains("RMU"))
                return HighVoltageSys;
            if (upper.Contains("TRANSFORMER")
                && !HasAny(upper, "ISOLAT", "CONTROL", "SAFETY", "IPS", "UPS", "LV ", "LOW VOLTAGE", "CURRENT TRANSFORMER", "VOLTAGE TRANSFORMER"))
                return HighVoltageSys;
            return null;
        }

        /// <summary>
        /// FUNC for an element on HV, BMS or RAD, read off its family / type name, or null
        /// when the name does not say (the caller falls back to FuncMap). Codes are the
        /// ones STING_FUNC_SYS_MATRIX.csv lists for each system.
        /// </summary>
        public static string FunctionFromName(string sys, string familyAndType)
        {
            string upper = (familyAndType ?? "").ToUpperInvariant();
            var words = Words(upper);
            switch (sys)
            {
                case HighVoltageSys:
                    return upper.Contains("TRANSFORMER") ? "TRF" : "PWR";
                case BmsSys:
                    if (HasAny(upper, "SENSOR", "TRANSMITTER", "THERMOSTAT", "PROBE", "HUMIDISTAT")) return "SNS";
                    if (HasAny(upper, "ACTUATOR", "CONTROL VALVE", "MOTORISED", "MOTORIZED")) return "CTL";
                    if (HasAny(upper, "CONTROLLER", "OUTSTATION", "PLC") || words.Contains("DDC")) return "FCT";
                    if (HasAny(upper, "METER", "MONITOR")) return "MON";
                    return null;
                case RadiationSys:
                    if (HasAny(upper, "MONITOR", "DOSIMET", "DETECTOR")) return "MON";
                    if (HasAny(upper, "ZONE", "BOUNDARY", "FENCE", "GAUSS", "CONTROLLED AREA")) return "ZNE";
                    if (HasAny(upper, "SHIELD", "LEAD", "BARRIER", "FARADAY")) return "SHD";
                    return null;
                default:
                    return null;
            }
        }

        private static readonly HashSet<string> ColdWaterCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes", "Plumbing Fixtures", "Plumbing Equipment",
        };

        /// <summary>
        /// SYS code for an MEP system name (connector system or system type), or null when
        /// the name says nothing reliable. <paramref name="categoryName"/> settles the two
        /// words that mean different things on a pipe and a duct: "VENT" (soil vent pipe vs
        /// ventilation) and "CW" (cold water vs condenser water).
        /// </summary>
        public static string FromSystemName(string sysName, string categoryName = null)
        {
            if (string.IsNullOrWhiteSpace(sysName)) return null;
            sysName = sysName.ToUpperInvariant();
            bool isPipe = CategoryTokenDefaults.PipeCategories.Contains(categoryName ?? "");

            // Medical gas first: its names contain GAS, AIR and VAC, which the rules
            // below would read as natural gas and ventilation.
            if (!AirCategories.Contains(categoryName ?? "") && TryMedicalGas(sysName, out _)) return MedicalGasSys;

            // HVAC air systems — full names and Revit abbreviated system types
            if (sysName.Contains("SUPPLY AIR") || sysName.Contains("SUPPLY DUCT")) return "HVAC";
            if (sysName.Contains("RETURN AIR") || sysName.Contains("RETURN DUCT")) return "HVAC";
            if (sysName.Contains("EXHAUST") || sysName.Contains("EXTRACT")) return "HVAC";
            if (sysName.Contains("FRESH AIR") || sysName.Contains("OUTSIDE AIR")) return "HVAC";
            // HVAC water: chilled, cooling and condenser water are the cooling plant's.
            if (sysName.Contains("CHILLED") || sysName.Contains("COOLING") || sysName.Contains("CONDENSER")) return "HVAC";
            // Air ventilation is duct/HVAC. For pipe categories "Vent" is the sanitary
            // soil-vent pipe (BS EN 12056-2), handled with the drainage rules below.
            if ((sysName.Contains("VENT") || sysName.Contains("VENTILATION")) && !isPipe) return "HVAC";
            if (sysName == "SA" || sysName.StartsWith("SA ") || sysName.Contains(" SA ")) return "HVAC";
            if (sysName == "RA" || sysName.StartsWith("RA ") || sysName.Contains(" RA ")) return "HVAC";
            if (sysName == "EA" || sysName.StartsWith("EA ") || sysName.Contains(" EA ")) return "HVAC";
            if (sysName == "OA" || sysName.StartsWith("OA ") || sysName.Contains(" OA ")) return "HVAC";
            if (sysName == "CHW" || sysName.StartsWith("CHW ") || sysName.Contains(" CHW ")) return "HVAC";
            // "CW" is cold water on a pipe or plumbing element, condenser water elsewhere.
            if (sysName == "CW" || sysName.StartsWith("CW ") || sysName.Contains(" CW "))
                return ColdWaterCategories.Contains(categoryName ?? "") ? "DCW" : "HVAC";
            if (sysName == "FCU" || sysName.StartsWith("FCU ")) return "HVAC";

            // Domestic hot water is its own system (DHW → DISC P, FUNC DHW), tested before
            // the generic HOT WATER → HWS rule so Revit's "Domestic Hot Water" type is not
            // filed with the heating circuits.
            if (sysName.Contains("DOMESTIC HOT") || sysName.Contains("DHW") ||
                sysName.Contains("HOT WATER SUPPLY") || sysName.Contains("HOT WATER SERVICE") ||
                sysName.Contains("CALORIFIER"))
                return "DHW";

            // Heating water. "Hydronic" is Revit's default name for the heating/cooling
            // water loop; chilled and condenser water were claimed above.
            if (sysName.Contains("HYDRONIC")) return "HWS";
            if (sysName.Contains("HOT WATER") || sysName.Contains("HWS")) return "HWS";
            if (sysName.Contains("HEATING") || sysName.Contains("LTHW") || sysName.Contains("MTHW")) return "HWS";
            if (sysName.Contains("RADIATOR") || sysName.Contains("UNDERFLOOR")) return "HWS";
            if (sysName.Contains("STEAM") || sysName.Contains("CONDENSATE")) return "HWS";
            if (sysName == "LTHW" || sysName == "MTHW" || sysName == "HTHW") return "HWS";
            if (sysName == "HW" || sysName.StartsWith("HW ")) return "HWS";

            // Domestic cold water
            if (sysName.Contains("COLD WATER") || sysName.Contains("CWS") || sysName.Contains("DCW")) return "DCW";
            if (sysName.Contains("DOMESTIC COLD") || sysName.Contains("BOOSTED COLD")) return "DCW";
            if (sysName.Contains("MAINS WATER") || sysName.Contains("POTABLE")) return "DCW";

            // Fire protection
            if (sysName.Contains("FIRE") || sysName.Contains("SPRINKLER") || sysName.Contains("WET RISER")) return "FP";
            if (sysName.Contains("DRY RISER") || sysName.Contains("HYDRANT")) return "FP";

            // Rainwater before foul drainage: "Storm Drainage" and "Roof Drain" contain DRAIN.
            if (sysName.Contains("RAINWATER") || sysName.Contains("STORM") || sysName.Contains("SURFACE WATER")) return "RWD";
            if (sysName.Contains("ROOF DRAIN")) return "RWD";
            if (sysName == "RWP" || sysName.StartsWith("RWP ")) return "RWD";

            // Sanitary / foul drainage
            if (sysName.Contains("SANITARY") || sysName.Contains("WASTE") || sysName.Contains("SOIL")) return "SAN";
            if (sysName.Contains("DRAIN") || sysName.Contains("SEWAGE") || sysName.Contains("FOUL")) return "SAN";
            if (sysName == "SVP" || sysName == "WP" || sysName.StartsWith("SVP ") || sysName.StartsWith("WP ")) return "SAN";
            if (sysName.Contains("VENT")) return "SAN";  // pipe vent; HVAC vent handled above

            // Fuel gas (medical gas was claimed first)
            if (sysName.Contains("GAS") || sysName.Contains("LPG")) return "GAS";

            // Further HVAC and heating names
            if (sysName.Contains("RELIEF")) return "HVAC";
            if (sysName.Contains("BALANCED") && sysName.Contains("VENT")) return "HVAC";
            if (sysName == "UFH" || sysName.StartsWith("UFH ") || sysName.Contains("UNDERFLOOR HEAT")) return "HWS";
            if (sysName.Contains("THERMAL STORAGE") || sysName.Contains("BUFFER TANK")) return "HWS";
            if (sysName.Contains("SOLAR THERMAL") || sysName.Contains("SOLAR PANEL")) return "HWS";

            return null;
        }
    }
}
