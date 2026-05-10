// Phase 178a hardening: SI bridge over IPCStandards.
//
// IPCStandards.cs ships fully imperial (gpm / psi / inches / inches-per-foot).
// UK/EU consultants work in SI (l/s / kPa / mm / percent slope). Without a
// bridge layer every UI button calling IPC tables silently returned an
// imperial answer that the rest of the codebase then treated as SI.
//
// This adapter:
//   • Accepts SI inputs (mm, l/s, kPa, percent).
//   • Converts to imperial, calls IPCStandards.
//   • Parses IPC's string pipe sizes (e.g. "3\"") back to mm.
//
// The adapter does not duplicate IPC tables — it only re-shapes I/O.
// All sizing logic stays in IPCStandards so the two codes (BS EN 12056
// and IPC 2021) remain independently maintainable.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace StingTools.Standards.IPC2021
{
    public static class IPCSiAdapter
    {
        public const double InchToMm     = 25.4;
        public const double FtToM        = 0.3048;
        public const double PsiToKpa     = 6.89476;
        public const double GpmToLps     = 0.0630902;
        public const double InchPerFtToPct = 100.0 / 12.0; // 1 in/ft = 8.333%

        // SI → imperial conversions ------------------------------------------------

        public static double SlopePctToInchesPerFt(double slopePct) => slopePct / InchPerFtToPct;
        public static double InchesPerFtToSlopePct(double inPerFt) => inPerFt * InchPerFtToPct;
        public static double LpsToGpm(double lps) => lps / GpmToLps;
        public static double GpmToLpsConv(double gpm) => gpm * GpmToLps;
        public static double KpaToPsi(double kpa)  => kpa / PsiToKpa;
        public static double PsiToKpaConv(double psi) => psi * PsiToKpa;
        public static double MmToInch(double mm)   => mm / InchToMm;

        // Parse an IPC pipe-size string into mm. IPC formats observed:
        //   "3\""           → 76.2 mm
        //   "1-1/4\""        → 31.75 mm
        //   "1 1/2\""        → 38.1 mm
        //   "1-1/2"          → 38.1 mm  (some helpers return without quote)
        //   "2"              → 50.8 mm
        // Returns 0 if unparseable.
        public static double InchStringToMm(string ipcSize)
        {
            if (string.IsNullOrWhiteSpace(ipcSize)) return 0;
            var s = ipcSize.Trim().Replace("\"", "").Replace(" ", "-");
            try
            {
                if (s.Contains("-"))
                {
                    var parts = s.Split('-');
                    if (parts.Length == 2)
                    {
                        double whole = double.Parse(parts[0], CultureInfo.InvariantCulture);
                        var frac = parts[1];
                        if (frac.Contains("/"))
                        {
                            var fp = frac.Split('/');
                            double num = double.Parse(fp[0], CultureInfo.InvariantCulture);
                            double den = double.Parse(fp[1], CultureInfo.InvariantCulture);
                            return (whole + num / den) * InchToMm;
                        }
                        return (whole + double.Parse(frac, CultureInfo.InvariantCulture)) * InchToMm;
                    }
                }
                if (s.Contains("/"))
                {
                    var fp = s.Split('/');
                    return (double.Parse(fp[0], CultureInfo.InvariantCulture) /
                            double.Parse(fp[1], CultureInfo.InvariantCulture)) * InchToMm;
                }
                return double.Parse(s, CultureInfo.InvariantCulture) * InchToMm;
            }
            catch
            {
                return 0;
            }
        }

        // Round mm to the nearest IPC nominal (so SI input matches IPC lookup keys).
        private static readonly int[] NominalMmAscending =
            { 32, 38, 50, 65, 75, 100, 125, 150, 200, 250, 300, 375, 450, 600 };

        public static int RoundUpToNominalMm(double mm)
        {
            foreach (var nom in NominalMmAscending)
                if (mm <= nom) return nom;
            return NominalMmAscending[NominalMmAscending.Length - 1];
        }

        // SI-friendly wrappers over IPCStandards ----------------------------------

        // Returns minimum drain pipe DN in mm given DFU and slope-percent.
        public static int GetMinimumDrainPipeSizeMm(double dfu, double slopePct, bool isStack = false)
        {
            double inPerFt = SlopePctToInchesPerFt(slopePct);
            string ipcSize = IPCStandards.GetMinimumDrainPipeSize(dfu, inPerFt, isStack);
            double mm = InchStringToMm(ipcSize);
            return mm > 0 ? RoundUpToNominalMm(mm) : 0;
        }

        // Returns minimum vent DN in mm given drain DN in mm and total developed
        // vent length in metres.
        public static int GetVentSizeMm(int drainDnMm, double ventLengthM)
        {
            string drainSize = MmToIpcInchString(drainDnMm);
            double ventLengthFt = ventLengthM / FtToM;
            string ventSize = IPCStandards.GetVentSize(drainSize, ventLengthFt);
            double mm = InchStringToMm(ventSize);
            return mm > 0 ? RoundUpToNominalMm(mm) : 0;
        }

        // Returns minimum water supply pipe DN in mm given WSFU.
        public static int GetMinimumWaterSupplySizeMm(double wsfu)
        {
            string ipcSize = IPCStandards.GetMinimumWaterSupplySize(wsfu);
            double mm = InchStringToMm(ipcSize);
            return mm > 0 ? RoundUpToNominalMm(mm) : 0;
        }

        // Reverse map: mm → IPC inch string keys used by lookup tables.
        private static readonly Dictionary<int, string> MmToIpc = new Dictionary<int, string>
        {
            { 32,  "1-1/4\"" }, { 38,  "1-1/2\"" }, { 50,  "2\"" },
            { 65,  "2-1/2\"" }, { 75,  "3\"" },     { 100, "4\"" },
            { 125, "5\"" },     { 150, "6\"" },     { 200, "8\"" },
            { 250, "10\"" },    { 300, "12\"" },    { 375, "15\"" },
            { 450, "18\"" },    { 600, "24\"" }
        };

        public static string MmToIpcInchString(int mm)
        {
            int nom = RoundUpToNominalMm(mm);
            return MmToIpc.TryGetValue(nom, out var s) ? s : $"{(mm / InchToMm):F1}\"";
        }
    }
}
