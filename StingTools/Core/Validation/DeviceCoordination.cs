using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (B4) — Device Coordination geometry (pure logic).
    //
    // The Revit-free math behind the Device Coordination validator (A1 §7):
    // axis-aligned bounding-box overlap / planar clearance + per-room
    // mounting-height-band outlier detection + a swing-side sign test. All
    // distances are millimetres. The command (DeviceCoordinationCommand) builds
    // these AABBs from Revit bounding boxes and calls this.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Axis-aligned bounding box in millimetres.</summary>
    public struct Aabb
    {
        public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public double CenterX => (MinX + MaxX) * 0.5;
        public double CenterY => (MinY + MaxY) * 0.5;
        public double CenterZ => (MinZ + MaxZ) * 0.5;

        public bool OverlapsXY(Aabb o)
            => MinX <= o.MaxX && MaxX >= o.MinX && MinY <= o.MaxY && MaxY >= o.MinY;

        /// <summary>Minimum planar (XY) separation in mm; 0 when the boxes overlap in plan.</summary>
        public double PlanarGapMm(Aabb o)
        {
            double dx = Math.Max(0, Math.Max(MinX - o.MaxX, o.MinX - MaxX));
            double dy = Math.Max(0, Math.Max(MinY - o.MaxY, o.MinY - MaxY));
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public class DeviceCoordRule
    {
        public string Id { get; set; } = "";
        /// <summary>clearance | overlap | mountingHeight | doorSwing.</summary>
        public string Type { get; set; } = "clearance";
        public List<string> DeviceCategories { get; set; } = new List<string>();
        public List<string> AgainstCategories { get; set; } = new List<string>();
        public double MinClearanceMm { get; set; }
        public double AlignmentToleranceMm { get; set; } = 50;
        public bool SameWallOnly { get; set; }
        public string Severity { get; set; } = "WARN";
        public string Description { get; set; } = "";
    }

    public class DeviceCoordRulePack
    {
        public string Version { get; set; }
        public string Description { get; set; }
        public List<DeviceCoordRule> Rules { get; set; } = new List<DeviceCoordRule>();
    }

    public static class DeviceCoordination
    {
        /// <summary>Clearance violation: planar gap strictly less than the minimum.</summary>
        public static bool ClearanceViolation(Aabb device, Aabb obstacle, double minClearanceMm)
            => device.PlanarGapMm(obstacle) < minClearanceMm;

        /// <summary>Indices into <paramref name="zMm"/> whose height deviates from the group
        /// median by more than the tolerance. Empty when fewer than 2 devices.</summary>
        public static List<int> MountingHeightOutliers(IList<double> zMm, double tolMm)
        {
            var outliers = new List<int>();
            if (zMm == null || zMm.Count < 2) return outliers;
            double median = Median(zMm);
            for (int i = 0; i < zMm.Count; i++)
                if (Math.Abs(zMm[i] - median) > tolMm) outliers.Add(i);
            return outliers;
        }

        public static double Median(IList<double> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            int n = s.Count;
            if (n == 0) return 0;
            return (n % 2 == 1) ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) * 0.5;
        }

        /// <summary>
        /// True when the device point lies on the side the door leaf sweeps into —
        /// i.e. the dot product of (device − doorCentre) with the swing direction is
        /// positive. A switch/outlet on the swing side is obstructed by the open leaf.
        /// </summary>
        public static bool OnSwingSide(double doorX, double doorY, double swingDirX, double swingDirY,
            double devX, double devY)
        {
            double dot = (devX - doorX) * swingDirX + (devY - doorY) * swingDirY;
            return dot > 0;
        }
    }
}
