// BS EN 12056-2 — Gravity drainage systems inside buildings.
// Discharge units (DU) tables, minimum slopes, stack capacity,
// vent sizing, trap-seal range. Phase 178b foundation module.
//
// All units SI (DU dimensionless, slope %, DN mm, DU stack
// capacity dimensionless). Pure-static lookup tables — no Revit
// dependency, callable from any layer.
//
// Reference: BS EN 12056-2:2000 Sections 6 + Annexes B and E,
// BS 5572:2017 (UK practice complement).

using System;
using System.Collections.Generic;

namespace StingTools.Standards.BSEN12056
{
    public static class BSen12056Standards
    {
        // Section 6.2 — Table 2: Discharge units (DU) by appliance.
        // System III (UK / IRL) selected; other systems (I, II, IV)
        // use the same DU values for the appliances listed here.
        public static readonly Dictionary<string, double> DischargeUnits = new Dictionary<string, double>
        {
            { "WC_6L",            2.0 },
            { "WC_4L",            1.8 },
            { "WC_9L_LEGACY",     2.5 },
            { "URINAL_BOWL",      0.5 },
            { "URINAL_TROUGH_PER_PERSON", 0.2 },
            { "BASIN",            0.5 },
            { "BIDET",            0.5 },
            { "BATH",             0.8 },
            { "SHOWER_NO_PLUG",   0.6 },
            { "SHOWER_WITH_PLUG", 0.8 },
            { "KITCHEN_SINK",     0.8 },
            { "DISHWASHER_DOM",   0.8 },
            { "WASHING_MACHINE_DOM", 0.8 },
            { "WASHING_MACHINE_COMM_6KG", 1.5 },
            { "FLOOR_DRAIN_50",   0.8 },
            { "FLOOR_DRAIN_70",   1.5 },
            { "FLOOR_DRAIN_100",  2.0 },
            { "URINAL_SLAB",      0.4 },
            { "BUTLER_SINK",      1.5 },
            { "MOP_SINK",         1.5 },
        };

        public static double GetDischargeUnits(string fixtureCode)
            => DischargeUnits.TryGetValue(fixtureCode?.ToUpperInvariant() ?? "", out var v) ? v : 0;

        // Section 6.4 — minimum gradient for branch waste runs.
        // Steeper than IPC; main runs (>1.5 m) double the branch %.
        public static double GetMinimumSlopePct(int dnMm, bool isStack = false, bool isMain = false)
        {
            if (isStack) return 0.0; // stacks vertical; slope undefined
            if (dnMm <= 50) return isMain ? 4.0 : 2.0;
            if (dnMm <= 75) return isMain ? 2.5 : 1.25;
            if (dnMm <= 100) return isMain ? 2.0 : 1.0;
            if (dnMm <= 125) return isMain ? 1.0 : 0.5;
            return 0.5;
        }

        // Section 6.5 — Table 11 stack capacity in DU (System III).
        public static double GetStackCapacityDu(int dnMm)
        {
            if (dnMm <= 50)  return 1.5;
            if (dnMm <= 60)  return 2.0;
            if (dnMm <= 70)  return 2.7;
            if (dnMm <= 75)  return 4.0;
            if (dnMm <= 90)  return 5.5;
            if (dnMm <= 100) return 6.0;
            if (dnMm <= 125) return 12.0;
            if (dnMm <= 150) return 20.0;
            if (dnMm <= 200) return 50.0;
            return 80.0;
        }

        // Annex B / Table 6 — minimum vent DN given drain DN and DU.
        public static int GetVentPipeDnMm(int drainDnMm, double totalDu)
        {
            int vent;
            if      (drainDnMm <= 50)  vent = totalDu <= 1.5 ? 32 : 40;
            else if (drainDnMm <= 75)  vent = totalDu <= 4.0 ? 40 : 50;
            else if (drainDnMm <= 100) vent = totalDu <= 6.0 ? 50 : 75;
            else if (drainDnMm <= 125) vent = 75;
            else if (drainDnMm <= 150) vent = 100;
            else                       vent = 100;
            return vent;
        }

        // Section 7 — trap seal min/max depths (mm).
        public static (int minSealDepthMm, int maxSealDepthMm) GetTrapSealRange(string fixtureKind)
        {
            string k = (fixtureKind ?? "").ToUpperInvariant();
            if (k.Contains("WC") || k.Contains("URINAL")) return (50, 75);
            if (k.Contains("FLOOR") || k.Contains("GULLY")) return (75, 100);
            if (k.Contains("KITCHEN") || k.Contains("MACHINE")) return (75, 100);
            return (50, 75); // basin / shower / bath / general
        }

        public const double SelfCleansingVelocityMps = 0.7;
        public const double MaxFillRatioNormal       = 0.667; // 2/3 full at design flow
        public const double MaxFillRatioVentilated   = 0.500;

        // Maximum branch length for un-vented self-siphonage protection
        // (Annex C). Beyond this length a relief or branch vent must
        // be added regardless of trap-seal depth.
        public static double GetMaxUnventedBranchLengthM(int dnMm, double slopePct)
        {
            if (dnMm <= 32) return 1.7;
            if (dnMm <= 40) return 3.0;
            if (dnMm <= 50) return 4.0;
            if (dnMm <= 75) return 6.0;
            return 10.0;
        }
    }
}
