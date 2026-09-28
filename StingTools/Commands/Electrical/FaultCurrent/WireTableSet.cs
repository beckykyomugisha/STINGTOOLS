// WireTableSet + CableResistance — Revit-free conductor resistance and the
// BS 7671 earth-fault loop impedance arithmetic.
//
// Extracted from FaultCurrentEngine.cs / BS7671ComplianceEngine.cs so the
// shipped resistance data and a worked Zs example can be pinned by tests
// (StingTools.Tags.Tests compile-includes this file). The file lookup stays
// in FaultCurrentEngine.cs (WireTableSet.Load), which needs StingToolsApp.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Commands.Electrical.FaultCurrent
{
    /// <summary>
    /// Conductor resistance table from STING_WIRE_TABLES.json → copperTables[0]
    /// (mohm_per_m = BS EN 60228 class 2 copper at 20 °C). Held by
    /// FaultCurrentEngine / FeederSizerEngine / BS7671ComplianceEngine.
    /// Aluminium = copper × 1.61 if no Al table is shipped.
    /// </summary>
    public partial class WireTableSet
    {
        private readonly List<(double csaMm2, double mohmPerM)> _copper = new();
        private const double AluminiumFactor = 1.61;

        /// <summary>Number of tabulated sizes (0 = nothing loaded).</summary>
        public int Count => _copper.Count;

        /// <summary>Parses the first copperTables entry of STING_WIRE_TABLES.json.</summary>
        public static WireTableSet FromJson(JObject root)
        {
            var ws = new WireTableSet();
            var table = (root?["copperTables"] as JArray)?.OfType<JObject>().FirstOrDefault();
            if (table == null) return ws;
            foreach (var sz in table["sizes"] as JArray ?? new JArray())
            {
                double csa = sz["csaMm2"]?.Value<double>() ?? 0;
                double r   = sz["mohm_per_m"]?.Value<double>() ?? 0;
                if (csa > 0 && r > 0) ws._copper.Add((csa, r));
            }
            ws._copper.Sort((a, b) => a.csaMm2.CompareTo(b.csaMm2));
            return ws;
        }

        /// <summary>
        /// Return mΩ/m at 20 °C for the nominal CSA (closest tabulated size, or
        /// linearly interpolated when between two entries).
        /// </summary>
        public double GetMohmPerMetre(double csaMm2, string material)
        {
            if (csaMm2 <= 0 || _copper.Count == 0) return 0;
            double r;
            if (csaMm2 <= _copper[0].csaMm2) r = _copper[0].mohmPerM;
            else if (csaMm2 >= _copper[^1].csaMm2) r = _copper[^1].mohmPerM;
            else
            {
                int idx = _copper.FindLastIndex(p => p.csaMm2 <= csaMm2);
                var lo = _copper[idx];
                var hi = _copper[idx + 1];
                double t = (csaMm2 - lo.csaMm2) / (hi.csaMm2 - lo.csaMm2);
                r = lo.mohmPerM + t * (hi.mohmPerM - lo.mohmPerM);
            }
            return string.Equals(material, "Al", StringComparison.OrdinalIgnoreCase) ? r * AluminiumFactor : r;
        }
    }

    /// <summary>Pure cable-run resistance and Zs arithmetic.</summary>
    public static class CableResistance
    {
        /// <summary>Linear temperature coefficients of resistance, per K (20 °C base).</summary>
        public const double AlphaCu = 0.00393, AlphaAl = 0.00403;

        /// <summary>
        /// Resistance of one conductor run in milliohms at the conductor operating
        /// temperature: R20 × (1 + α(θ − 20)) × L. The insulation, when given,
        /// sets θ (XLPE → 90 °C, PVC → 70 °C); otherwise <paramref name="operatingTempC"/>.
        /// </summary>
        public static double RunMohm(WireTableSet wireTables, double csaMm2, string material, double lengthM,
            double operatingTempC = 70.0, string insulation = null)
        {
            if (wireTables == null || csaMm2 <= 0 || lengthM <= 0) return 0;
            double r = wireTables.GetMohmPerMetre(csaMm2, material);
            if (r <= 0) return 0;
            double tempC = !string.IsNullOrEmpty(insulation)
                ? StingTools.Commands.Electrical.VoltageDrop.VoltageDropEngine.OperatingTempForInsulation(insulation)
                : operatingTempC;
            double alpha = string.Equals(material, "Al", StringComparison.OrdinalIgnoreCase) ? AlphaAl : AlphaCu;
            return r * (1.0 + alpha * (tempC - 20.0)) * lengthM;
        }

        /// <summary>
        /// Zs = Ze + R1 + R2, ohms. R1 (line) and R2 (CPC) at the conductor operating
        /// temperature over the circuit length. A CPC CSA of 0 is taken equal to the line.
        /// </summary>
        public static double ZsOhm(double zeOhm, double phaseCsaMm2, double cpcCsaMm2, double lengthM,
            string material, string insulation, WireTableSet wireTables)
        {
            if (lengthM <= 0 || phaseCsaMm2 <= 0) return zeOhm;
            double r1 = RunMohm(wireTables, phaseCsaMm2, material, lengthM, insulation: insulation);
            double r2 = cpcCsaMm2 > 0
                ? RunMohm(wireTables, cpcCsaMm2, material, lengthM, insulation: insulation)
                : r1;
            return zeOhm + (r1 + r2) / 1000.0;
        }
    }
}
