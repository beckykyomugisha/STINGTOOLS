using System;
using System.Collections.Generic;
using StingTools.Standards.NEC2023;

namespace StingTools.Core.Electrical
{
    /// <summary>The conductor and device an NEC branch circuit / feeder is sized to.</summary>
    public sealed class NecConductorPick
    {
        /// <summary>NEC trade size as Table 310.16 keys it ("12", "1/0", "250"); null when none fits.</summary>
        public string Size { get; set; }
        /// <summary>Table 310.16 ampacity after 310.15(B)(1) / 310.15(C)(1), A.</summary>
        public double AmpacityA { get; set; }
        /// <summary>The 240.4 device selection on that conductor; null when no conductor fits.</summary>
        public ProtectiveDeviceSelection.Selection Device { get; set; }
        /// <summary>Sizes passed over because the device they need exceeds their 240.4(D)
        /// small-conductor limit, e.g. "12 AWG (needs 25 A > 240.4(D) 20 A)".</summary>
        public List<string> UpsizedPast { get; } = new List<string>();
        /// <summary>Why nothing was picked; null when <see cref="Size"/> is set.</summary>
        public string Refusal { get; set; }
    }

    /// <summary>
    /// NEC 2023 conductor + OCPD selection, Revit-free. Walks the conductor series smallest
    /// first and takes the first size that (a) carries the sizing current (Ib, ×1.25 when
    /// continuous — 210.19(A)(1) / 215.2(A)(1)) after correction, (b) gets a 240.6(A) device
    /// that 240.4(B)/(C) permits on its ampacity, and (c) for 14/12/10 AWG, whose device is
    /// within the 240.4(D) small-conductor limit. A size failing (c) is UPSIZED past, never
    /// given a device capped below the sizing current (DSCH-30 follow-up: the old code capped
    /// the breaker after selection, so a 21.25 A continuous load on 12 AWG got 20 A).
    /// </summary>
    public static class NecConductorSelection
    {
        /// <summary>NEC conductor series, smallest first. Trade sizes as
        /// <see cref="NECStandards"/> keys them: AWG below 250, then kcmil.</summary>
        public static readonly string[] SizeLadder =
        {
            "14", "12", "10", "8", "6", "4", "3", "2", "1",
            "1/0", "2/0", "3/0", "4/0",
            "250", "300", "350", "400", "500", "600", "700", "750",
        };

        public static NecConductorPick Pick(double ibA, bool continuous, ConductorMaterial material,
            double ambientC, int currentCarryingConductors, int[] ratingsA)
        {
            var pick = new NecConductorPick();
            double sizingCurrent = continuous ? ibA * 1.25 : ibA;
            bool anyCarries = false;
            foreach (string size in SizeLadder)
            {
                double a;
                try { a = NECStandards.GetConductorAmpacity(size, material, 75); }
                catch (ArgumentException) { continue; }   // size absent from the table for this material
                a = NECStandards.ApplyTemperatureCorrection(a, ambientC);
                a = NECStandards.ApplyBundlingAdjustment(a, currentCarryingConductors);
                if (a < sizingCurrent) continue;
                anyCarries = true;

                var sel = ProtectiveDeviceSelection.Select(ibA, isNec: true, continuous: continuous, ratingsA, a,
                    $"{size} Table 310.16 @75°C corrected");
                if (sel.ProposedA <= 0)
                {
                    pick.Refusal = $"No NEC Table 240.6(A) rating ≥ {sizingCurrent:0.0} A";
                    return pick;   // a larger conductor will not create a larger rating
                }
                if (sel.Blocked) continue;
                if (ApplySmallConductorLimit(sel, size, material))
                {
                    int limit = NECStandards.GetSmallConductorMaxOcpd(size, material);
                    pick.UpsizedPast.Add($"{size} AWG (needs {sel.ProposedA} A > 240.4(D) {limit} A)");
                    continue;
                }
                pick.Size = size;
                pick.AmpacityA = a;
                pick.Device = sel;
                return pick;
            }
            pick.Refusal = anyCarries
                ? $"No conductor up to 750 kcmil takes a permitted device for {sizingCurrent:0.0} A."
                : $"No single conductor in NEC Table 310.16 carries {sizingCurrent:0.0} A after " +
                  "310.15(B)(1) ambient and 310.15(C)(1) adjustment. Parallel conductors " +
                  "(310.10(G)) are required and are not sized here.";
            return pick;
        }

        /// <summary>
        /// The 240.4(D) small-conductor ceiling applied to a device already selected for
        /// <paramref name="size"/>: when the device exceeds it, the selection is BLOCKED
        /// (and no longer needs 240.4(B) confirmation) and the note says why. Returns true
        /// when it blocked. Sizes 240.4(D) does not cover are left alone.
        /// </summary>
        public static bool ApplySmallConductorLimit(ProtectiveDeviceSelection.Selection sel, string size,
            ConductorMaterial material)
        {
            if (sel == null || sel.Blocked || sel.ProposedA <= 0) return false;
            int limit = NECStandards.GetSmallConductorMaxOcpd(size, material);
            if (limit <= 0 || sel.ProposedA <= limit) return false;
            sel.Blocked = true;
            sel.NeedsConfirmation = false;
            sel.Note = (string.IsNullOrEmpty(sel.Note) ? "" : sel.Note + "; ") +
                       $"OCPD {sel.ProposedA} A > the NEC 240.4(D) limit {limit} A for {size} AWG " +
                       ConductorMaterialText.Label(material) + " — upsize the conductor, do not apply";
            return true;
        }
    }
}
