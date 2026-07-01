// ══════════════════════════════════════════════════════════════════════════
//  EvmPeriod.cs — pure EVM period math (PMI standard). PM-1.
//
//  Extracted from EvmCalculator.cs (which carries the Revit-coupled Save/List)
//  so the commercially-consequential EVM arithmetic is headlessly unit-tested in
//  StingTools.Cost.Tests with NO Revit API dependency. Zero Autodesk.Revit.*
//  imports on purpose — linked into the test project the same way WasteFactor is.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core.Evm
{
    public class EvmPeriod
    {
        /// <summary>Period end date (typically last day of month).</summary>
        public DateTime PeriodEnd { get; set; } = DateTime.UtcNow;
        public string PeriodLabel { get; set; } = "";

        // ── Inputs ──────────────────────────────────────────────────
        public double Bac { get; set; }     // Budget at Completion
        public double Bcws { get; set; }    // Planned Value
        public double Bcwp { get; set; }    // Earned Value
        public double Acwp { get; set; }    // Actual Cost

        // ── Derived ─────────────────────────────────────────────────
        public double Cv => Math.Round(Bcwp - Acwp, 2);
        public double Sv => Math.Round(Bcwp - Bcws, 2);
        public double Cpi => Acwp > 0 ? Math.Round(Bcwp / Acwp, 4) : 0;
        public double Spi => Bcws > 0 ? Math.Round(Bcwp / Bcws, 4) : 0;

        // PM-1 — EAC no longer collapses to 0 when CPI = 0 (project start:
        // AC>0, EV=0 → CPI=0). The CPI-typical EAC = AC + (BAC−EV)/CPI (which
        // equals BAC/CPI, the previous formula, for CPI>0); at CPI=0 it falls
        // back to the budget-rate forecast AC + (BAC−EV) so ETC stays positive
        // and VAC reads the real forecast instead of a false "on budget".
        public double Eac => Cpi > 0
            ? Math.Round(Acwp + (Bac - Bcwp) / Cpi, 2)
            : EacBudgetRate;

        /// <summary>Budget-rate EAC — remaining work at the original budget rate
        /// (used when current cost variance is judged atypical / one-off).</summary>
        public double EacBudgetRate => Math.Round(Acwp + (Bac - Bcwp), 2);

        /// <summary>Schedule-blended EAC = AC + (BAC−EV)/(CPI×SPI) — when both cost
        /// AND schedule performance are expected to persist. Falls back to the
        /// budget-rate EAC when CPI×SPI = 0.</summary>
        public double EacScheduleBlended
        {
            get
            {
                double f = Cpi * Spi;
                return f > 0 ? Math.Round(Acwp + (Bac - Bcwp) / f, 2) : EacBudgetRate;
            }
        }

        // ETC / VAC derive from the chosen (CPI-typical) EAC.
        public double Etc => Math.Round(Eac - Acwp, 2);
        public double Vac => Math.Round(Bac - Eac, 2);

        public double Tcpi
        {
            get
            {
                double denom = Bac - Acwp;
                if (Math.Abs(denom) < 0.01) return 0;
                return Math.Round((Bac - Bcwp) / denom, 4);
            }
        }

        public string CostHealth => Cpi >= 1.0 ? "Green" : Cpi >= 0.95 ? "Amber" : "Red";
        public string ScheduleHealth => Spi >= 1.0 ? "Green" : Spi >= 0.95 ? "Amber" : "Red";
    }
}
