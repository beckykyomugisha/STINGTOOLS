// StingTools v4 MVP — hanger spacing tables (MSS SP-58 / HVCA TR/19 / SMACNA).
//
// Returns maximum hanger spacing in millimetres for a given MEP curve
// based on its system type, material, nominal diameter, and whether
// the run is insulated. Tables are curated from:
//
//   MSS SP-58 Table 4   — steel & copper pipe, 80 mm→300 mm
//   HVCA TR/19          — plastic/cast-iron drainage pipe
//   SMACNA HVAC Duct 4e — rectangular & round ducting (up to 1500 mm)
//   BS 7671 Part 3      — conduit & cable tray (as separation table)
//
// The call-site hands us a probe record; the table returns a span in
// millimetres. Out-of-range sizes extrapolate to the nearest row and
// attach a warning to the PlacementCandidate so the shop knows the
// suggestion is approximate.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Calc
{
    public enum HangerRunKind { Pipe, Duct, Conduit, CableTray }

    public class HangerSpacingQuery
    {
        public HangerRunKind Kind         { get; set; }
        public double DiameterMm          { get; set; }
        public string Material            { get; set; } = "STEEL"; // STEEL, COPPER, PLASTIC, CAST_IRON, GI_SHEET, AL_SHEET
        public bool   Insulated           { get; set; }
    }

    public class HangerSpacingResult
    {
        public double MaxSpanMm        { get; set; }
        public string Basis            { get; set; } = "";
        public bool   Extrapolated     { get; set; }
        // True when the run is buried / bedded (vitrified clay, concrete
        // mains, etc.) so no overhead support is required. The engine
        // skips placement quietly rather than emitting a "no spacing
        // table match" warning per run.
        public bool   NoHangersRequired { get; set; }
    }

    public static class HangerSpacingTable
    {
        // MSS SP-58 maximum horizontal steel pipe hanger spacing.
        // bore (mm) → span (mm).
        private static readonly (double bore, double span)[] MssSteel = new[]
        {
            ( 15.0, 2100.0 ), ( 20.0, 2700.0 ), ( 25.0, 2700.0 ),
            ( 32.0, 3000.0 ), ( 40.0, 3000.0 ), ( 50.0, 3000.0 ),
            ( 65.0, 3700.0 ), ( 80.0, 3700.0 ), (100.0, 4300.0 ),
            (125.0, 4600.0 ), (150.0, 5200.0 ), (200.0, 5800.0 ),
            (250.0, 6100.0 ), (300.0, 6700.0 ),
        };
        // MSS SP-58 copper pipe.
        private static readonly (double bore, double span)[] MssCopper = new[]
        {
            ( 15.0, 1500.0 ), ( 20.0, 1800.0 ), ( 25.0, 1800.0 ),
            ( 32.0, 2400.0 ), ( 40.0, 2400.0 ), ( 50.0, 2700.0 ),
            ( 65.0, 2700.0 ), ( 80.0, 3000.0 ), (100.0, 3700.0 ),
            (125.0, 3700.0 ), (150.0, 4600.0 ),
        };
        // HVCA TR/19 plastic drainage pipe (uPVC / ABS).
        private static readonly (double bore, double span)[] Tr19Plastic = new[]
        {
            ( 32.0, 500.0 ), ( 40.0, 500.0 ), ( 50.0, 900.0 ),
            ( 75.0, 900.0 ), (100.0, 900.0 ), (160.0, 1200.0 ),
            (200.0, 1200.0 ),
        };
        // Cast iron drainage pipe per BS 416 guidance.
        private static readonly (double bore, double span)[] CastIron = new[]
        {
            ( 50.0, 1800.0 ), ( 75.0, 2400.0 ), (100.0, 3000.0 ),
            (150.0, 3000.0 ), (200.0, 3000.0 ),
        };
        // Phase 139.29 — ACO trough / linear drainage support spacing.
        // Source: ACO Building Drainage technical manual + BS EN 1433
        // (drainage channels). Channel "diameter" here is the nominal
        // internal width (slot width) in mm. Hi-Cap channels accept
        // wider intervals because they're stiffer; standard channels
        // need closer support to prevent deflection at gully points.
        private static readonly (double widthMm, double span)[] AcoChannel = new[]
        {
            ( 100.0, 1500.0 ),  // ACO Drain S100 — 1.5 m support
            ( 150.0, 1500.0 ),  // S150
            ( 200.0, 2000.0 ),  // S200 / Multidrain
            ( 300.0, 2500.0 ),  // S300 Hi-Cap
            ( 400.0, 3000.0 ),  // S400+ Hi-Cap
        };
        // SMACNA rectangular duct hanger spacing by largest dimension.
        private static readonly (double size, double span)[] SmacnaRect = new[]
        {
            ( 600.0, 3000.0 ), ( 900.0, 3000.0 ),
            (1200.0, 2400.0 ), (1500.0, 2400.0 ),
            (1800.0, 1800.0 ), (2500.0, 1200.0 ),
        };
        // SMACNA round duct spacing.
        private static readonly (double dia, double span)[] SmacnaRound = new[]
        {
            (150.0, 3600.0 ), (300.0, 3600.0 ),
            (600.0, 3600.0 ), (900.0, 3000.0 ),
            (1200.0, 2400.0), (1500.0, 2400.0),
        };
        // BS 7671 cable tray / ladder maxima — not spec'd by Appx, but
        // manufacturer guidance typically 1500-2000 mm for steel tray.
        private static readonly (double width, double span)[] CableTraySteel = new[]
        {
            ( 75.0, 1500.0 ), (150.0, 1500.0 ), (225.0, 1500.0 ),
            (300.0, 1500.0 ), (450.0, 1200.0 ), (600.0, 1200.0 ),
        };

        public static HangerSpacingResult Query(HangerSpacingQuery q)
        {
            var r = new HangerSpacingResult();
            if (q == null) return r;

            (double, double)[] tbl = null;
            string basis = "";
            switch (q.Kind)
            {
                case HangerRunKind.Pipe:
                    // Buried / bedded mains carry no overhead supports —
                    // signal that explicitly so the engine skips placement
                    // without emitting a "no match" warning.
                    if (q.Material.Equals("VITRIFIED_CLAY", StringComparison.OrdinalIgnoreCase) ||
                        q.Material.Equals("CONCRETE",       StringComparison.OrdinalIgnoreCase) ||
                        q.Material.StartsWith("BURIED",     StringComparison.OrdinalIgnoreCase))
                    {
                        r.NoHangersRequired = true;
                        r.Basis = "buried main — no hangers required";
                        return r;
                    }
                    if (q.Material.Equals("COPPER", StringComparison.OrdinalIgnoreCase))
                    { tbl = MssCopper;   basis = "MSS SP-58 copper"; }
                    else if (q.Material.Equals("PLASTIC", StringComparison.OrdinalIgnoreCase) ||
                             q.Material.Equals("UPVC",    StringComparison.OrdinalIgnoreCase) ||
                             q.Material.Equals("ABS",     StringComparison.OrdinalIgnoreCase))
                    { tbl = Tr19Plastic; basis = "HVCA TR/19 plastic drainage"; }
                    else if (q.Material.Equals("CAST_IRON", StringComparison.OrdinalIgnoreCase))
                    { tbl = CastIron;    basis = "BS 416 cast iron"; }
                    else if (q.Material.Equals("ACO", StringComparison.OrdinalIgnoreCase) ||
                             q.Material.Equals("CHANNEL", StringComparison.OrdinalIgnoreCase) ||
                             q.Material.Equals("LINEAR_DRAIN", StringComparison.OrdinalIgnoreCase))
                    { tbl = AcoChannel;  basis = "BS EN 1433 / ACO channel"; }
                    else
                    { tbl = MssSteel;    basis = "MSS SP-58 steel"; }
                    break;

                case HangerRunKind.Duct:
                    if (q.Material.Equals("ROUND", StringComparison.OrdinalIgnoreCase) ||
                        q.DiameterMm > 0 && q.DiameterMm < 1500.0 && q.Material.Length == 0)
                    { tbl = SmacnaRound; basis = "SMACNA HVAC round duct"; }
                    else
                    { tbl = SmacnaRect;  basis = "SMACNA HVAC rectangular duct"; }
                    break;

                case HangerRunKind.Conduit:
                    // BS 7671 doesn't give a table — use pipe-steel rule of
                    // thumb scaled down (conduit is much lighter).
                    tbl = new (double, double)[]
                    {
                        (20.0, 1500.0), (25.0, 1800.0), (32.0, 2000.0),
                        (40.0, 2000.0), (50.0, 2500.0)
                    };
                    basis = "Manufacturer guidance (BS EN 61386)";
                    break;

                case HangerRunKind.CableTray:
                    tbl = CableTraySteel;
                    basis = "Manufacturer guidance (Cablofil / Legrand)";
                    break;
            }
            if (tbl == null || tbl.Length == 0) return r;

            r.MaxSpanMm = Interpolate(q.DiameterMm, tbl, out bool extrap);
            r.Basis     = basis;
            r.Extrapolated = extrap;

            // Insulation penalty: every 100mm of thick insulation reduces
            // span by ~10% because the support carries more weight.
            if (q.Insulated && r.MaxSpanMm > 0)
                r.MaxSpanMm *= 0.90;
            return r;
        }

        private static double Interpolate(double probe, (double key, double val)[] table, out bool extrapolated)
        {
            extrapolated = false;
            if (table.Length == 0) return 0;
            if (probe <= table[0].key) { extrapolated = probe < table[0].key; return table[0].val; }
            if (probe >= table[table.Length - 1].key)
            { extrapolated = probe > table[table.Length - 1].key; return table[table.Length - 1].val; }
            for (int i = 0; i + 1 < table.Length; i++)
            {
                if (probe >= table[i].key && probe <= table[i + 1].key)
                {
                    double frac = (probe - table[i].key) / (table[i + 1].key - table[i].key);
                    return table[i].val + frac * (table[i + 1].val - table[i].val);
                }
            }
            return table[table.Length - 1].val;
        }
    }
}
