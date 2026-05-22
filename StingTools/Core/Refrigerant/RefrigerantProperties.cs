// StingTools — Refrigerant fluid properties at typical VRF/VRV
// operating saturations. Values are spot-design data sufficient for
// pipe sizing — not a full equation-of-state engine.
//
// Sources:
//   ASHRAE Handbook Fundamentals 2021, Ch. 30 "Thermophysical
//     Properties of Refrigerants".
//   REFPROP 10 tables (gas + liquid at saturation).
//   Daikin VRV Engineering Manual 2024 (R32, R410A).
//
// For each fluid we store representative density / dynamic viscosity
// pairs for the SUCTION (low-pressure superheated gas), DISCHARGE
// (high-pressure superheated gas) and LIQUID legs at the design
// saturation condition typical of VRF system sizing.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Refrigerant
{
    public enum RefrigerantLeg { Suction, Discharge, Liquid, HotGasReturn }

    public class RefrigerantState
    {
        public string Id          { get; set; } = "";  // "R410A", "R32", "R134a", "CO2"
        public string Label       { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>Saturated suction temperature design °C (e.g. 5 for AC).</summary>
        public double SuctionSatTempC  { get; set; }
        /// <summary>Saturated condensing temperature design °C (e.g. 45).</summary>
        public double CondSatTempC     { get; set; }

        public double RhoSuctionGasKgM3   { get; set; }
        public double RhoDischargeGasKgM3 { get; set; }
        public double RhoLiquidKgM3       { get; set; }

        public double MuSuctionGasPaS   { get; set; }
        public double MuDischargeGasPaS { get; set; }
        public double MuLiquidPaS       { get; set; }

        /// <summary>Latent heat of vaporisation at suction, kJ/kg.</summary>
        public double HfgKJperKg        { get; set; }

        /// <summary>Min velocity for oil return up vertical risers, m/s.</summary>
        public double MinVerticalVelMs  { get; set; }
        /// <summary>Min velocity for oil return along horizontal runs, m/s.</summary>
        public double MinHorizontalVelMs { get; set; }
        /// <summary>Max recommended velocity to avoid noise + erosion.</summary>
        public double MaxVelocityMs     { get; set; }
        /// <summary>Max equivalent line length, m. Vendor-specific guideline.</summary>
        public double MaxEquivLengthM   { get; set; }
        /// <summary>Max vertical lift outdoor unit above indoor unit, m.</summary>
        public double MaxLiftAboveIndoorM { get; set; }
        /// <summary>Max vertical drop outdoor unit below indoor unit, m.</summary>
        public double MaxLiftBelowIndoorM { get; set; }

        /// <summary>
        /// (dT/dP)_sat slope at the condensing design temperature (K/kPa).
        /// Used by the LIQUID-leg flash-gas check: line ΔP × slope = the
        /// saturation-temperature drop the liquid sees over the run. If
        /// this exceeds the subcooling reserve the TXV will see two-phase.
        /// Values from REFPROP / ASHRAE Handbook Fundamentals 2021 Ch.30,
        /// evaluated near the typical 45 °C condensing point.
        /// </summary>
        public double DtDpKperKpa     { get; set; }

        /// <summary>Vendor-typical line ΔP budget for the SUCTION leg (kPa).
        /// Driven by the equivalent-saturation-temperature loss the vendor
        /// allows for capacity guarantees (~1 K T_sat at typical conditions).</summary>
        public double SuctionBudgetKpa   { get; set; } = 30;
        /// <summary>Vendor-typical line ΔP budget for the DISCHARGE leg (kPa).</summary>
        public double DischargeBudgetKpa { get; set; } = 50;
        /// <summary>Vendor-typical line ΔP budget for the LIQUID leg (kPa).</summary>
        public double LiquidBudgetKpa    { get; set; } = 50;

        /// <summary>Return the vendor-recommended ΔP budget for a leg.</summary>
        public double DefaultBudgetForLeg(RefrigerantLeg leg) => leg switch
        {
            RefrigerantLeg.Suction       => SuctionBudgetKpa,
            RefrigerantLeg.Discharge     => DischargeBudgetKpa,
            RefrigerantLeg.HotGasReturn  => DischargeBudgetKpa,
            RefrigerantLeg.Liquid        => LiquidBudgetKpa,
            _                            => 30
        };

        public string Source { get; set; } = "";
    }

    public static class RefrigerantProperties
    {
        public static readonly Dictionary<string, RefrigerantState> All
            = new Dictionary<string, RefrigerantState>(StringComparer.OrdinalIgnoreCase)
        {
            ["R410A"] = new RefrigerantState
            {
                Id = "R410A", Label = "R410A (HFC blend)",
                Description = "Legacy HFC 50/50 R32/R125 blend. GWP 2088.",
                SuctionSatTempC = 5, CondSatTempC = 45,
                RhoSuctionGasKgM3 = 34.4, RhoDischargeGasKgM3 = 105.5, RhoLiquidKgM3 = 1018,
                MuSuctionGasPaS = 1.21e-5, MuDischargeGasPaS = 1.48e-5, MuLiquidPaS = 1.32e-4,
                HfgKJperKg = 195,
                MinVerticalVelMs = 6.0, MinHorizontalVelMs = 3.5, MaxVelocityMs = 18.0,
                MaxEquivLengthM = 165, MaxLiftAboveIndoorM = 50, MaxLiftBelowIndoorM = 40,
                DtDpKperKpa = 0.0185,                                       // R410A @ 45 °C cond.
                SuctionBudgetKpa = 30, DischargeBudgetKpa = 50, LiquidBudgetKpa = 50,
                Source = "Daikin VRV IV-S 2021 Engineering Data Book"
            },
            ["R32"] = new RefrigerantState
            {
                Id = "R32", Label = "R32 (single-component HFC)",
                Description = "Replacing R410A in new VRV/VRF. GWP 675.",
                SuctionSatTempC = 5, CondSatTempC = 45,
                RhoSuctionGasKgM3 = 23.6, RhoDischargeGasKgM3 = 76.5, RhoLiquidKgM3 = 962,
                MuSuctionGasPaS = 1.25e-5, MuDischargeGasPaS = 1.55e-5, MuLiquidPaS = 1.10e-4,
                HfgKJperKg = 248,
                MinVerticalVelMs = 5.0, MinHorizontalVelMs = 3.0, MaxVelocityMs = 20.0,
                MaxEquivLengthM = 180, MaxLiftAboveIndoorM = 50, MaxLiftBelowIndoorM = 40,
                DtDpKperKpa = 0.0170,                                       // R32 @ 45 °C cond.
                SuctionBudgetKpa = 25, DischargeBudgetKpa = 50, LiquidBudgetKpa = 50,
                Source = "Daikin VRV 5 2024 Engineering Data Book"
            },
            ["R134a"] = new RefrigerantState
            {
                Id = "R134a", Label = "R134a (HFC, legacy chillers)",
                Description = "Single-component HFC. Mainly large screw chillers.",
                SuctionSatTempC = 4, CondSatTempC = 40,
                RhoSuctionGasKgM3 = 17.5, RhoDischargeGasKgM3 = 48.8, RhoLiquidKgM3 = 1239,
                MuSuctionGasPaS = 1.10e-5, MuDischargeGasPaS = 1.35e-5, MuLiquidPaS = 2.05e-4,
                HfgKJperKg = 195,
                MinVerticalVelMs = 5.0, MinHorizontalVelMs = 3.0, MaxVelocityMs = 18.0,
                MaxEquivLengthM = 150, MaxLiftAboveIndoorM = 30, MaxLiftBelowIndoorM = 20,
                DtDpKperKpa = 0.034,                                        // R134a @ 40 °C cond.
                SuctionBudgetKpa = 20, DischargeBudgetKpa = 40, LiquidBudgetKpa = 30,
                Source = "Carrier 30XW chiller IOM 2023"
            },
            ["CO2"] = new RefrigerantState
            {
                Id = "CO2", Label = "R744 (CO₂, transcritical)",
                Description = "Transcritical commercial refrigeration / heat pump. GWP 1.",
                SuctionSatTempC = -10, CondSatTempC = 35,  // gas-cooler outlet
                RhoSuctionGasKgM3 = 73.4, RhoDischargeGasKgM3 = 240.0, RhoLiquidKgM3 = 872,
                MuSuctionGasPaS = 1.38e-5, MuDischargeGasPaS = 1.95e-5, MuLiquidPaS = 8.50e-5,
                HfgKJperKg = 245,
                MinVerticalVelMs = 4.0, MinHorizontalVelMs = 2.5, MaxVelocityMs = 12.0,
                MaxEquivLengthM = 80, MaxLiftAboveIndoorM = 20, MaxLiftBelowIndoorM = 15,
                DtDpKperKpa = 0.0046,                                       // CO₂ — much flatter slope; trans-critical
                SuctionBudgetKpa = 50, DischargeBudgetKpa = 100, LiquidBudgetKpa = 80,
                Source = "Bitzer CO₂ Manual 2022"
            }
        };

        public static RefrigerantState Get(string id)
            => All.TryGetValue(id ?? "R410A", out var s) ? s : All["R410A"];

        /// <summary>Return (rho, mu) for the requested leg of a refrigerant.</summary>
        public static (double rho, double mu) Pair(RefrigerantState s, RefrigerantLeg leg)
        {
            return leg switch
            {
                RefrigerantLeg.Suction       => (s.RhoSuctionGasKgM3,   s.MuSuctionGasPaS),
                RefrigerantLeg.Discharge     => (s.RhoDischargeGasKgM3, s.MuDischargeGasPaS),
                RefrigerantLeg.HotGasReturn  => (s.RhoDischargeGasKgM3, s.MuDischargeGasPaS),
                RefrigerantLeg.Liquid        => (s.RhoLiquidKgM3,       s.MuLiquidPaS),
                _                            => (s.RhoSuctionGasKgM3,   s.MuSuctionGasPaS)
            };
        }
    }
}
