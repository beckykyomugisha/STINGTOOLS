// StairPressurisation — pressure-differential system airflow for a protected
// stair (BS EN 12101-6 method). Revit-free; the criteria and leakage areas
// come from STING_SMOKE_CONTROL_DESIGN.json.
//
//   Leakage through a gap:      Q = 0.83 · A · ΔP^(1/2)       m³/s, A m², ΔP Pa
//   Paths in series:            A_eff = (Σ A_i^-2)^(-1/2)
//   Paths in parallel:          A_eff = Σ A_i
//   Open-door airflow:          Q = v · A_door                   (velocity criterion)
//   Door opening force:         F = F_dc + W·A·ΔP / (2·(W − d))    N
//     F_dc door-closer force N, W door width m, A door area m², d handle to
//     latch edge m. Checked against the maximum opening force.
//
// Supply = (doors-closed leakage at the design pressure + the open-door
// flow) × the leakage allowance, reported beside its parts so the engineer
// can see which case governs. This is a sizing aid for the fan duty; the
// classification, criteria and allowance must be confirmed against the
// edition of BS EN 12101-6 in force and the fire strategy.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Fire
{
    public sealed class LeakagePath
    {
        public string Label { get; set; } = "";
        public int    Count { get; set; } = 1;
        /// <summary>Leakage area of one item, m².</summary>
        public double AreaEachM2 { get; set; }
        public double TotalAreaM2 => Math.Max(0, Count) * Math.Max(0, AreaEachM2);
    }

    public sealed class StairPressurisationInput
    {
        public string SystemClass { get; set; } = "";
        /// <summary>Pressure difference across closed doors, Pa.</summary>
        public double DesignPressurePa { get; set; } = 50;
        /// <summary>Airflow velocity required through each open door, m/s.</summary>
        public double OpenDoorVelocityMs { get; set; }
        /// <summary>Number of doors assumed open for the velocity case.</summary>
        public int    OpenDoors { get; set; } = 1;
        public double DoorWidthM { get; set; } = 1.0;
        public double DoorHeightM { get; set; } = 2.1;
        /// <summary>Leakage paths from the stair while doors are closed (doors, lift doors, walls, windows).</summary>
        public List<LeakagePath> ClosedLeakage { get; } = new List<LeakagePath>();
        /// <summary>Multiplier on the calculated supply for leakage the calculation cannot see (e.g. 1.25 = +25 %).</summary>
        public double LeakageAllowance { get; set; } = 1.0;
        public double DoorCloserForceN { get; set; } = 30;
        /// <summary>Distance from the handle to the latch edge of the door, m.</summary>
        public double HandleToEdgeM { get; set; } = 0.075;
        public double MaxDoorOpeningForceN { get; set; } = 100;
        /// <summary>
        /// Pressure difference across the doors that stay closed while the
        /// velocity case runs, Pa. 0 = use <see cref="DesignPressurePa"/>
        /// (conservative: the stair rarely holds full pressure with a door open).
        /// </summary>
        public double OpenCaseResidualPressurePa { get; set; }
    }

    public sealed class StairPressurisationResult
    {
        public double ClosedLeakageAreaM2 { get; set; }
        public double ClosedDoorsFlowM3s { get; set; }
        public double OpenDoorFlowM3s { get; set; }
        /// <summary>Leakage through the doors that stay closed while the velocity case runs.</summary>
        public double OpenCaseLeakageM3s { get; set; }
        public double SupplyBeforeAllowanceM3s { get; set; }
        public double SupplyM3s { get; set; }
        public string Governs { get; set; } = "";
        public double DoorOpeningForceN { get; set; }
        public bool   DoorForceOk { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class StairPressurisation
    {
        public const double LeakageCoefficient = 0.83;

        public static double LeakageFlowM3s(double areaM2, double dpPa)
            => areaM2 <= 0 || dpPa <= 0 ? 0 : LeakageCoefficient * areaM2 * Math.Sqrt(dpPa);

        public static double SeriesArea(IEnumerable<double> areas)
        {
            var a = areas.Where(x => x > 0).ToList();
            if (a.Count == 0) return 0;
            return 1.0 / Math.Sqrt(a.Sum(x => 1.0 / (x * x)));
        }

        public static double ParallelArea(IEnumerable<double> areas) => areas.Where(x => x > 0).Sum();

        public static double DoorOpeningForceN(double closerN, double widthM, double heightM, double dpPa, double handleToEdgeM)
        {
            double lever = widthM - handleToEdgeM;
            if (lever <= 0) throw new ArgumentOutOfRangeException(nameof(handleToEdgeM), "Handle must sit inside the door width.");
            return closerN + widthM * (widthM * heightM) * dpPa / (2.0 * lever);
        }

        public static StairPressurisationResult Calculate(StairPressurisationInput i)
        {
            if (i == null) throw new ArgumentNullException(nameof(i));
            var r = new StairPressurisationResult();
            if (i.DesignPressurePa <= 0) r.Warnings.Add("Design pressure difference is not set.");
            if (i.OpenDoorVelocityMs <= 0) r.Warnings.Add("No open-door velocity criterion — only the doors-closed case is sized.");

            r.ClosedLeakageAreaM2 = ParallelArea(i.ClosedLeakage.Select(p => p.TotalAreaM2));
            r.ClosedDoorsFlowM3s = LeakageFlowM3s(r.ClosedLeakageAreaM2, i.DesignPressurePa);

            double doorArea = i.DoorWidthM * i.DoorHeightM;
            r.OpenDoorFlowM3s = Math.Max(0, i.OpenDoors) * doorArea * Math.Max(0, i.OpenDoorVelocityMs);

            // While doors stand open the stair pressure falls, so the doors that
            // remain closed leak at a lower differential. OpenCaseResidualPressurePa
            // sets it; unset, the design pressure is kept (conservative). The
            // open doors' own leakage is removed pro rata.
            double openShare = 0;
            int doorItems = i.ClosedLeakage.Where(p => p.Label.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0).Sum(p => p.Count);
            if (doorItems > 0) openShare = Math.Min(1.0, (double)Math.Max(0, i.OpenDoors) / doorItems);
            double doorLeakArea = i.ClosedLeakage.Where(p => p.Label.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0).Sum(p => p.TotalAreaM2);
            double residual = i.OpenCaseResidualPressurePa > 0 ? i.OpenCaseResidualPressurePa : i.DesignPressurePa;
            r.OpenCaseLeakageM3s = LeakageFlowM3s(r.ClosedLeakageAreaM2 - doorLeakArea * openShare, residual);

            double closedCase = r.ClosedDoorsFlowM3s;
            double openCase = r.OpenDoorFlowM3s + r.OpenCaseLeakageM3s;
            r.SupplyBeforeAllowanceM3s = Math.Max(closedCase, openCase);
            r.Governs = openCase > closedCase ? "open-door velocity" : "doors-closed pressure";
            r.SupplyM3s = r.SupplyBeforeAllowanceM3s * Math.Max(1.0, i.LeakageAllowance);

            if (i.DesignPressurePa > 0 && i.DoorWidthM > i.HandleToEdgeM)
            {
                r.DoorOpeningForceN = DoorOpeningForceN(i.DoorCloserForceN, i.DoorWidthM, i.DoorHeightM,
                                                         i.DesignPressurePa, i.HandleToEdgeM);
                r.DoorForceOk = r.DoorOpeningForceN <= i.MaxDoorOpeningForceN + 1e-9;
                if (!r.DoorForceOk)
                    r.Warnings.Add($"Door opening force {r.DoorOpeningForceN:F0} N exceeds {i.MaxDoorOpeningForceN:F0} N — " +
                                   "reduce the pressure difference, the closer force or the door width, or add pressure relief.");
            }
            return r;
        }
    }
}
