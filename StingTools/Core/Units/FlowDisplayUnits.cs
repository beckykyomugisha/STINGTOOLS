using System;
using System.Globalization;

namespace StingTools.Core.Units
{
    /// <summary>
    /// KUT-6 — a PRESENTATION layer for MEP flows and pressures. The Owner reads CFM and
    /// GPM; the engines work in l/s and Pa and are not touched.
    ///
    /// <para><b>SI stays the internal representation, deliberately.</b> Every solver, every
    /// sizing table and every stored parameter is metric. Converting the engines would touch
    /// the friction solver, the balancing engine, the fixture-unit aggregator and every
    /// shipped rules file, to move a number that only ever mattered on a drawing. So this
    /// converts at the edge — where a value is shown and where one is entered — and nowhere
    /// else.</para>
    ///
    /// <para><b>The round trip is the hard part, and it is why there is one factor per pair
    /// rather than two.</b> The usual way to write this is a pair of published constants —
    /// 0.4719 l/s per CFM and 2.119 CFM per l/s — which are not reciprocals. 1 000 CFM
    /// becomes 471.9 l/s becomes 1 000.0 CFM only by luck; at 850 CFM the pair returns
    /// 850.06, so a value the engineer typed comes back changed. Every conversion here
    /// multiplies or divides by a SINGLE exact factor, so <c>ToSi(FromSi(x)) == x</c> to
    /// within double precision for every x.</para>
    ///
    /// <para>Revit-free, so the round trip is provable without a host.</para>
    /// </summary>
    public static class FlowDisplayUnits
    {
        // ── Exact definitions. Each is a DEFINITION, not a measurement, so each is
        //    exact and its inverse is exact by construction.

        /// <summary>Litres per second in one cubic foot per minute.
        /// 1 ft = 0.3048 m exactly (international foot, 1959), so
        /// 1 ft³ = 0.028316846592 m³ exactly; ÷ 60 s × 1000 L/m³.</summary>
        public const double LitrePerSecondPerCfm = 0.028316846592 * 1000.0 / 60.0;

        /// <summary>Litres per second in one US gallon per minute.
        /// 1 US liquid gallon = 3.785411784 L exactly (231 in³); ÷ 60 s.</summary>
        public const double LitrePerSecondPerUsGpm = 3.785411784 / 60.0;

        /// <summary>Pascals in one inch of water gauge at 60 °F — the convention SMACNA and
        /// ASHRAE duct work uses. <b>Not interchangeable with the 4 °C definition</b>
        /// (249.0889 Pa); the two differ by 0.1 %, which is invisible on one duct and is a
        /// whole fan selection across a system, so the reference temperature is named here
        /// rather than left to whoever reads the number.</summary>
        public const double PascalPerInchWaterGauge60F = 248.84;

        // ── Symbols ─────────────────────────────────────────────────────────────

        public const string AirFlowSiSymbol = "l/s";
        public const string AirFlowImperialSymbol = "CFM";
        public const string WaterFlowSiSymbol = "l/s";
        public const string WaterFlowImperialSymbol = "GPM";
        public const string PressureSiSymbol = "Pa";
        public const string PressureImperialSymbol = "in.w.g.";

        /// <summary>What a value means, so a caller cannot convert an air flow with the water
        /// factor. Air and water are both l/s in SI and different units in imperial, which is
        /// exactly the confusion a single "flow" conversion would invite.</summary>
        public enum Quantity
        {
            /// <summary>Volumetric air flow. SI l/s, imperial CFM.</summary>
            AirFlow,
            /// <summary>Volumetric water flow. SI l/s, imperial US GPM.</summary>
            WaterFlow,
            /// <summary>Pressure or pressure drop. SI Pa, imperial inches water gauge.</summary>
            Pressure,
        }

        /// <summary>The factor for one SI unit of a quantity, per imperial unit. One value,
        /// used for both directions.</summary>
        public static double SiPerImperial(Quantity q)
        {
            switch (q)
            {
                case Quantity.AirFlow: return LitrePerSecondPerCfm;
                case Quantity.WaterFlow: return LitrePerSecondPerUsGpm;
                case Quantity.Pressure: return PascalPerInchWaterGauge60F;
                default: throw new ArgumentOutOfRangeException(nameof(q));
            }
        }

        public static string Symbol(Quantity q, bool imperial)
        {
            switch (q)
            {
                case Quantity.AirFlow: return imperial ? AirFlowImperialSymbol : AirFlowSiSymbol;
                case Quantity.WaterFlow: return imperial ? WaterFlowImperialSymbol : WaterFlowSiSymbol;
                case Quantity.Pressure: return imperial ? PressureImperialSymbol : PressureSiSymbol;
                default: throw new ArgumentOutOfRangeException(nameof(q));
            }
        }

        // ── Conversion ──────────────────────────────────────────────────────────

        /// <summary>SI → display. Returns the SI value unchanged when not imperial, so a
        /// metric project's numbers are bit-identical to before this existed.</summary>
        public static double FromSi(double si, Quantity q, bool imperial)
            => imperial ? si / SiPerImperial(q) : si;

        /// <summary>Display → SI. The exact inverse of <see cref="FromSi"/>.</summary>
        public static double ToSi(double display, Quantity q, bool imperial)
            => imperial ? display * SiPerImperial(q) : display;

        // ── Presentation ────────────────────────────────────────────────────────

        /// <summary>
        /// Format an SI value for display, with its unit symbol.
        ///
        /// <para><paramref name="decimals"/> defaults to a sensible precision per quantity
        /// rather than a fixed one: CFM figures run into the thousands where a decimal place
        /// is noise, GPM are tens, and a pressure drop in in.w.g. is a fraction where two
        /// decimals is the whole number.</para>
        ///
        /// <para><b>Formatting rounds; storage does not.</b> A caller that round-trips must
        /// go through <see cref="ToSi"/> from the value the user typed, never from this
        /// string — re-parsing a rounded display is how 850 CFM becomes 849.7.</para>
        /// </summary>
        public static string Format(double si, Quantity q, bool imperial, int decimals = -1)
        {
            double v = FromSi(si, q, imperial);
            if (decimals < 0) decimals = DefaultDecimals(q, imperial);
            return v.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture),
                              CultureInfo.InvariantCulture)
                 + " " + Symbol(q, imperial);
        }

        /// <summary>Format without the unit symbol, for a grid column whose header carries
        /// it. Same rounding as <see cref="Format"/>.</summary>
        public static string FormatValue(double si, Quantity q, bool imperial, int decimals = -1)
        {
            double v = FromSi(si, q, imperial);
            if (decimals < 0) decimals = DefaultDecimals(q, imperial);
            return v.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture),
                              CultureInfo.InvariantCulture);
        }

        private static int DefaultDecimals(Quantity q, bool imperial)
        {
            switch (q)
            {
                case Quantity.AirFlow: return imperial ? 0 : 1;      // CFM whole, l/s to 0.1
                case Quantity.WaterFlow: return imperial ? 1 : 2;
                case Quantity.Pressure: return imperial ? 2 : 0;     // in.w.g. is a fraction
                default: return 1;
            }
        }

        /// <summary>
        /// Parse a value the user typed IN THE DISPLAY UNIT and return SI.
        ///
        /// <para>Tolerates a trailing unit symbol and thousands separators, because people
        /// paste "1,200 CFM" out of a schedule. Returns false rather than 0 on anything it
        /// cannot read — a silent 0 in a flow field sizes a duct to nothing.</para>
        /// </summary>
        public static bool TryParseToSi(string text, Quantity q, bool imperial, out double si)
        {
            si = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string t = text.Trim();
            foreach (string sym in new[]
            {
                AirFlowImperialSymbol, WaterFlowImperialSymbol, PressureImperialSymbol,
                AirFlowSiSymbol, PressureSiSymbol, "L/s", "in wg", "inwg",
            })
            {
                if (t.EndsWith(sym, StringComparison.OrdinalIgnoreCase))
                {
                    t = t.Substring(0, t.Length - sym.Length).Trim();
                    break;
                }
            }

            if (!double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands,
                                 CultureInfo.InvariantCulture, out double v))
                return false;

            si = ToSi(v, q, imperial);
            return true;
        }
    }
}
