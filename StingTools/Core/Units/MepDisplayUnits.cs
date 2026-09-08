using System;
using StingTools.Standards;

namespace StingTools.Core.Units
{
    /// <summary>
    /// KUT-6 — whether MEP flows and pressures are SHOWN in imperial, project-scoped.
    ///
    /// <para><b>This reuses <see cref="ProjectStandardsManager.UnitSystem"/> rather than
    /// adding a second setting.</b> That property already exists, is already set by the
    /// regional preset (the USA preset carries <c>Imperial</c>), and was already synced
    /// per-document from <c>PROJECT_REGION</c> on open — it was simply never read by anything
    /// that formats an engineering value. A second, parallel "units" preference is the shape
    /// of defect KUT-8 was: two settings for one question, neither of which is authoritative,
    /// and a header that disagrees with the calculation.</para>
    ///
    /// <para><b>Defaults to metric.</b> <c>ProjectStandardsConfig.UnitSystem</c> is
    /// <c>Metric</c> and the default region is International, so a project that has never
    /// chosen anything is unchanged: <see cref="FlowDisplayUnits.FromSi"/> returns the SI
    /// value untouched, and every existing report prints exactly what it printed before.</para>
    ///
    /// <para><c>Mixed</c> resolves to METRIC here. It means "units by discipline convention",
    /// and the discipline convention for MEP flow in a metric project is l/s; reading it as
    /// imperial would flip every number on the strength of a word that does not say so.</para>
    /// </summary>
    public static class MepDisplayUnits
    {
        /// <summary>True when flows and pressures should be shown in CFM / GPM / in.w.g.
        /// Never throws — a failure to read the preference shows SI, because SI is what the
        /// engines computed and is the honest fallback.</summary>
        public static bool Imperial
        {
            get
            {
                try { return ProjectStandardsManager.Instance.UnitSystem == UnitSystem.Imperial; }
                catch (Exception ex)
                {
                    StingLog.WarnRateLimited("MepUnits", $"MepDisplayUnits: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>Air flow, SI l/s in, formatted string out.</summary>
        public static string AirFlow(double litresPerSecond, int decimals = -1)
            => FlowDisplayUnits.Format(litresPerSecond, FlowDisplayUnits.Quantity.AirFlow, Imperial, decimals);

        /// <summary>Water flow, SI l/s in, formatted string out.</summary>
        public static string WaterFlow(double litresPerSecond, int decimals = -1)
            => FlowDisplayUnits.Format(litresPerSecond, FlowDisplayUnits.Quantity.WaterFlow, Imperial, decimals);

        /// <summary>Pressure or pressure drop, SI Pa in, formatted string out.</summary>
        public static string Pressure(double pascals, int decimals = -1)
            => FlowDisplayUnits.Format(pascals, FlowDisplayUnits.Quantity.Pressure, Imperial, decimals);

        /// <summary>The symbol alone, for a grid column header or a schedule caption.</summary>
        public static string AirFlowSymbol
            => FlowDisplayUnits.Symbol(FlowDisplayUnits.Quantity.AirFlow, Imperial);

        public static string WaterFlowSymbol
            => FlowDisplayUnits.Symbol(FlowDisplayUnits.Quantity.WaterFlow, Imperial);

        public static string PressureSymbol
            => FlowDisplayUnits.Symbol(FlowDisplayUnits.Quantity.Pressure, Imperial);

        /// <summary>Read a value the user typed in the CURRENT display unit and return SI.
        /// The entry half of the round trip — a field that displays CFM must parse CFM.</summary>
        public static bool TryParseAirFlow(string text, out double litresPerSecond)
            => FlowDisplayUnits.TryParseToSi(text, FlowDisplayUnits.Quantity.AirFlow, Imperial, out litresPerSecond);

        public static bool TryParseWaterFlow(string text, out double litresPerSecond)
            => FlowDisplayUnits.TryParseToSi(text, FlowDisplayUnits.Quantity.WaterFlow, Imperial, out litresPerSecond);

        public static bool TryParsePressure(string text, out double pascals)
            => FlowDisplayUnits.TryParseToSi(text, FlowDisplayUnits.Quantity.Pressure, Imperial, out pascals);
    }
}
