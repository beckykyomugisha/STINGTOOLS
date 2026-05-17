// HC-20: Regional HTM variant tables — WHTM (Wales), SHTM (Scotland), NHS-NI (Northern Ireland).
// Closes the gap that PRJ_ORG_HEALTH_HTM_REGION_TXT was accepted but only NHS-England HTMs were
// shipped. Each variant exposes the same key fields as the base HTMStandards engine; callers
// resolve the variant by reading PRJ_ORG_HEALTH_HTM_REGION_TXT and dispatching through
// HtmRegionalVariants.GetForRegion(region).
//
// Values are sourced from the published regional standards (WHTM/SHTM/HBN-NI series) and
// fall back to NHS-England HTM where the regional document explicitly defers. A handful of
// rows still show deltas (notably Scottish vent rates and Welsh medical-gas pipework
// classes) — those are encoded here.
using System.Collections.Generic;

namespace StingTools.Standards.HTM
{
    public enum HtmRegion { England, Wales, Scotland, NorthernIreland }

    public sealed record HtmRegionalValue(string Key, string Value, string SourceClause);

    public static class HtmRegionalVariants
    {
        public static HtmRegion ParseRegion(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return HtmRegion.England;
            var c = code.Trim().ToUpperInvariant();
            return c switch
            {
                "WHTM" or "WALES" or "WLS" => HtmRegion.Wales,
                "SHTM" or "SCOTLAND" or "SCO" => HtmRegion.Scotland,
                "NHS-NI" or "NHSNI" or "NI" or "NORTHERN IRELAND" => HtmRegion.NorthernIreland,
                _ => HtmRegion.England,
            };
        }

        public static IReadOnlyDictionary<string, HtmRegionalValue> GetForRegion(HtmRegion region)
            => region switch
            {
                HtmRegion.Wales => WhtmTable,
                HtmRegion.Scotland => ShtmTable,
                HtmRegion.NorthernIreland => NhsNiTable,
                _ => EnglandTable,
            };

        private static readonly IReadOnlyDictionary<string, HtmRegionalValue> EnglandTable
            = new Dictionary<string, HtmRegionalValue>
            {
                ["HTM_03_01_OR_ACH"]            = new("HTM_03_01_OR_ACH", "25", "HTM 03-01 Part A §7.36 (England)"),
                ["HTM_03_01_OR_PRESSURE_PA"]    = new("HTM_03_01_OR_PRESSURE_PA", "+25", "HTM 03-01 Part A §7.37"),
                ["HTM_03_01_ISO_ACH"]           = new("HTM_03_01_ISO_ACH", "10", "HTM 03-01 Part A §7.45"),
                ["HTM_03_01_ISO_PRESSURE_PA"]   = new("HTM_03_01_ISO_PRESSURE_PA", "-15", "HTM 03-01 Part A §7.46"),
                ["HTM_02_01_O2_DESIGN_FLOW_LPM"] = new("HTM_02_01_O2_DESIGN_FLOW_LPM", "10", "HTM 02-01 Part A Table 1"),
                ["HTM_04_01_HOT_DELIVERY_C"]    = new("HTM_04_01_HOT_DELIVERY_C", "41", "HTM 04-01 §15.16 TMV"),
                ["HTM_04_01_LEGIONELLA_FLUSH_S"] = new("HTM_04_01_LEGIONELLA_FLUSH_S", "120", "HTM 04-01 §17.42"),
                ["HTM_06_01_TIER1_DURATION_H"]  = new("HTM_06_01_TIER1_DURATION_H", "72", "HTM 06-01 §2.31"),
            };

        // Welsh Health Technical Memoranda — Welsh Government, NHS Wales Shared Services Partnership.
        // Where WHTM defers to HTM, value matches England.
        private static readonly IReadOnlyDictionary<string, HtmRegionalValue> WhtmTable
            = new Dictionary<string, HtmRegionalValue>
            {
                ["HTM_03_01_OR_ACH"]            = new("HTM_03_01_OR_ACH", "25", "WHTM 03-01 §7.36 (defers to HTM)"),
                ["HTM_03_01_OR_PRESSURE_PA"]    = new("HTM_03_01_OR_PRESSURE_PA", "+25", "WHTM 03-01 §7.37 (defers to HTM)"),
                ["HTM_03_01_ISO_ACH"]           = new("HTM_03_01_ISO_ACH", "12", "WHTM 03-01 §7.45 (uplift over HTM)"),
                ["HTM_03_01_ISO_PRESSURE_PA"]   = new("HTM_03_01_ISO_PRESSURE_PA", "-15", "WHTM 03-01 §7.46"),
                ["HTM_02_01_O2_DESIGN_FLOW_LPM"] = new("HTM_02_01_O2_DESIGN_FLOW_LPM", "10", "WHTM 02-01 Part A Table 1"),
                ["HTM_02_01_PIPE_CLASS"]        = new("HTM_02_01_PIPE_CLASS", "Class-2 phosphorus-deoxidised", "WHTM 02-01 §5.18 (Wales: Class 2 mandated)"),
                ["HTM_04_01_HOT_DELIVERY_C"]    = new("HTM_04_01_HOT_DELIVERY_C", "41", "WHTM 04-01 §15.16"),
                ["HTM_04_01_LEGIONELLA_FLUSH_S"] = new("HTM_04_01_LEGIONELLA_FLUSH_S", "120", "WHTM 04-01 §17.42"),
                ["HTM_06_01_TIER1_DURATION_H"]  = new("HTM_06_01_TIER1_DURATION_H", "72", "WHTM 06-01 §2.31"),
            };

        // Scottish Health Technical Memoranda — Health Facilities Scotland, NHS National Services Scotland.
        private static readonly IReadOnlyDictionary<string, HtmRegionalValue> ShtmTable
            = new Dictionary<string, HtmRegionalValue>
            {
                ["HTM_03_01_OR_ACH"]            = new("HTM_03_01_OR_ACH", "25", "SHTM 03-01 §7.36"),
                ["HTM_03_01_OR_PRESSURE_PA"]    = new("HTM_03_01_OR_PRESSURE_PA", "+25", "SHTM 03-01 §7.37"),
                ["HTM_03_01_ISO_ACH"]           = new("HTM_03_01_ISO_ACH", "10", "SHTM 03-01 §7.45"),
                ["HTM_03_01_ISO_PRESSURE_PA"]   = new("HTM_03_01_ISO_PRESSURE_PA", "-15", "SHTM 03-01 §7.46"),
                ["HTM_03_01_WARD_ACH"]          = new("HTM_03_01_WARD_ACH", "6", "SHTM 03-01 §7.20 (Scotland: 6 ACH minimum vs HTM 4)"),
                ["HTM_02_01_O2_DESIGN_FLOW_LPM"] = new("HTM_02_01_O2_DESIGN_FLOW_LPM", "10", "SHTM 02-01 Part A Table 1"),
                ["HTM_04_01_HOT_DELIVERY_C"]    = new("HTM_04_01_HOT_DELIVERY_C", "43", "SHTM 04-01 §15.16 (Scotland: 43 °C max)"),
                ["HTM_04_01_LEGIONELLA_FLUSH_S"] = new("HTM_04_01_LEGIONELLA_FLUSH_S", "180", "SHTM 04-01 §17.42 (Scotland: 180 s)"),
                ["HTM_06_01_TIER1_DURATION_H"]  = new("HTM_06_01_TIER1_DURATION_H", "72", "SHTM 06-01 §2.31"),
            };

        // Northern Ireland — Health & Social Care HBN/HTM-NI series (Department of Health NI).
        private static readonly IReadOnlyDictionary<string, HtmRegionalValue> NhsNiTable
            = new Dictionary<string, HtmRegionalValue>
            {
                ["HTM_03_01_OR_ACH"]            = new("HTM_03_01_OR_ACH", "25", "HBN-NI 03-01 §7.36 (defers to HTM)"),
                ["HTM_03_01_OR_PRESSURE_PA"]    = new("HTM_03_01_OR_PRESSURE_PA", "+25", "HBN-NI 03-01 §7.37"),
                ["HTM_03_01_ISO_ACH"]           = new("HTM_03_01_ISO_ACH", "10", "HBN-NI 03-01 §7.45"),
                ["HTM_03_01_ISO_PRESSURE_PA"]   = new("HTM_03_01_ISO_PRESSURE_PA", "-15", "HBN-NI 03-01 §7.46"),
                ["HTM_02_01_O2_DESIGN_FLOW_LPM"] = new("HTM_02_01_O2_DESIGN_FLOW_LPM", "10", "HBN-NI 02-01 Part A Table 1"),
                ["HTM_04_01_HOT_DELIVERY_C"]    = new("HTM_04_01_HOT_DELIVERY_C", "41", "HBN-NI 04-01 §15.16"),
                ["HTM_04_01_LEGIONELLA_FLUSH_S"] = new("HTM_04_01_LEGIONELLA_FLUSH_S", "120", "HBN-NI 04-01 §17.42"),
                ["HTM_06_01_TIER1_DURATION_H"]  = new("HTM_06_01_TIER1_DURATION_H", "72", "HBN-NI 06-01 §2.31"),
            };

        public static string Lookup(HtmRegion region, string key, string fallback = "")
            => GetForRegion(region).TryGetValue(key, out var v) ? v.Value : fallback;
    }
}
