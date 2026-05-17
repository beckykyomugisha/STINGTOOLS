// Phase 139 D2 — Coverage grid generator.
//
// For rules with GuaranteeCoverage=true and CoverageRadiusMm>0.
// Produces an evenly-spaced grid that fills the room polygon so every
// floor point lies within CoverageRadiusMm of a placed device, honouring
// MaxSpacingMm, MinSpacingMm, WallClearanceMm and ObstructionClearanceMm.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Placement
{
    public class CoverageGridGenerator
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double FtToMm = 304.8;

        private readonly Document _doc;

        public class CoverageResult
        {
            public List<XYZ> Points { get; set; } = new List<XYZ>();
            public double CoveragePercent { get; set; } = 0.0;
            public List<string> Warnings { get; set; } = new List<string>();
            public double SpacingUsedMm { get; set; }
            public int BayCount { get; set; }
            public List<XYZ> UncoveredSamples { get; set; } = new List<XYZ>();
        }

        public CoverageGridGenerator(Document doc) { _doc = doc; }

        /// <summary>
        /// Generate coverage grid for a single room.  Returns ordered
        /// candidate XYZs at MountingHeight already applied at anchorZ.
        /// </summary>
        public CoverageResult Generate(Room room, PlacementRule rule, double anchorZ)
        {
            var result = new CoverageResult();
            if (room == null || rule == null) return result;
            if (rule.CoverageRadiusMm <= 0) return result;

            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb == null)
                {
                    result.Warnings.Add("CoverageGrid: room has no bounding box");
                    return result;
                }

                // 1. Compute spacing.  Start from CoverageRadiusMm × √2
                //    (guarantees every point in a square cell with diagonal
                //    2R is within R of the centre).
                double spacingMm = rule.CoverageRadiusMm * Math.Sqrt(2.0);
                if (rule.MaxSpacingMm > 0 && spacingMm > rule.MaxSpacingMm)
                    spacingMm = rule.MaxSpacingMm;
                if (rule.MinSpacingMm > 0 && spacingMm < rule.MinSpacingMm)
                {
                    spacingMm = rule.MinSpacingMm;
                    result.Warnings.Add(
                        $"CoverageGrid: spacing capped to MinSpacing {rule.MinSpacingMm:F0}mm — coverage may be < 100%");
                }
                result.SpacingUsedMm = spacingMm;
                double spacingFt = spacingMm * MmToFt;

                // 2. Wall clearance.
                double wallClearFt = (rule.WallClearanceMm > 0 ? rule.WallClearanceMm : 0.0) * MmToFt;

                // 3. Re-partition by obstructions creating bays.  v1 keeps
                //    a single bay = the room bbox shrunk by wall clearance.
                //    Future enhancement: scan beam/duct soffits to split.
                var bays = new List<(double minX, double minY, double maxX, double maxY)>
                {
                    (bb.Min.X + wallClearFt, bb.Min.Y + wallClearFt,
                     bb.Max.X - wallClearFt, bb.Max.Y - wallClearFt),
                };
                result.BayCount = bays.Count;

                // 4. Generate candidate grid in each bay.
                var rawCandidates = new List<XYZ>();
                foreach (var bay in bays)
                {
                    if (bay.maxX <= bay.minX || bay.maxY <= bay.minY) continue;
                    double bayW = bay.maxX - bay.minX;
                    double bayH = bay.maxY - bay.minY;
                    int cols = Math.Max(1, (int)Math.Ceiling(bayW / spacingFt));
                    int rows = Math.Max(1, (int)Math.Ceiling(bayH / spacingFt));
                    double stepX = bayW / cols;
                    double stepY = bayH / rows;
                    for (int i = 0; i < cols; i++)
                    {
                        for (int j = 0; j < rows; j++)
                        {
                            double x = bay.minX + (i + 0.5) * stepX;
                            double y = bay.minY + (j + 0.5) * stepY;
                            var pt = new XYZ(x, y, anchorZ);
                            // Inside-room test
                            try { if (!room.IsPointInRoom(new XYZ(x, y, room.Level?.Elevation ?? bb.Min.Z))) continue; }
                            catch { /* IsPointInRoom can fail on unbounded rooms — accept */ }
                            rawCandidates.Add(pt);
                        }
                    }
                }

                // 5. Filter by obstruction clearance using ObstructionIndex.
                var obstructions = new List<ExclusionRect>();
                try { obstructions = ObstructionIndex.BuildForRoom(_doc, room); }
                catch (Exception ex) { result.Warnings.Add($"CoverageGrid: obstruction build failed: {ex.Message}"); }

                double obsClearFt = (rule.ObstructionClearanceMm > 0 ? rule.ObstructionClearanceMm : 0.0) * MmToFt;
                foreach (var p in rawCandidates)
                {
                    if (HasObstructionWithin(p, obstructions, obsClearFt)) continue;
                    result.Points.Add(p);
                }

                // 6. Compute coverage % via point-in-circle sampling.
                result.CoveragePercent = ComputeCoveragePercent(room, bb, result.Points,
                    rule.CoverageRadiusMm * MmToFt, result.UncoveredSamples);

                // Phase 139.30 (M-08 deep) — iterative densification.
                // The bbox grid + IsPointInRoom filter is correct but
                // sparse in narrow legs of L-shaped / T-shaped / U-shaped
                // rooms. When GuaranteeCoverage is on and coverage is
                // below 99 %, cluster the uncovered samples and place an
                // extra point at each cluster centroid; obstruction-test
                // those extras through the same gate as the base grid.
                if (rule.GuaranteeCoverage && result.CoveragePercent < 99.0
                    && result.UncoveredSamples != null && result.UncoveredSamples.Count > 0)
                {
                    int densifiedAdded = 0;
                    for (int pass = 0; pass < 3 && result.CoveragePercent < 99.0; pass++)
                    {
                        // Cluster uncovered samples using a coverage-radius
                        // greedy grouping. Each cluster centroid becomes a
                        // candidate extra point.
                        var clusters = ClusterUncoveredSamples(
                            result.UncoveredSamples, rule.CoverageRadiusMm * MmToFt);
                        if (clusters.Count == 0) break;

                        int addedThisPass = 0;
                        foreach (var c in clusters)
                        {
                            // Snap Z back to the active anchorZ so the
                            // extra point lands at mounting height.
                            var pt = new XYZ(c.X, c.Y, anchorZ);
                            // Wall + obstruction clearance gates same as
                            // the base grid.
                            if (HasObstructionWithin(pt, obstructions, obsClearFt)) continue;
                            try { if (!room.IsPointInRoom(new XYZ(pt.X, pt.Y, room.Level?.Elevation ?? bb.Min.Z))) continue; }
                            catch { /* unbounded room — accept */ }
                            // Reject points too close to the bay edge
                            // (wall clearance already trimmed the bay,
                            // but cluster centroids can drift outside).
                            bool inBay = false;
                            foreach (var bay in bays)
                            {
                                if (pt.X >= bay.minX && pt.X <= bay.maxX
                                 && pt.Y >= bay.minY && pt.Y <= bay.maxY)
                                { inBay = true; break; }
                            }
                            if (!inBay) continue;
                            // Don't double-up — skip if an existing point
                            // is within MinSpacing.
                            double minSpacingFt = (rule.MinSpacingMm > 0 ? rule.MinSpacingMm : 0.0) * MmToFt;
                            bool tooClose = false;
                            if (minSpacingFt > 0)
                            {
                                double mssq = minSpacingFt * minSpacingFt;
                                foreach (var existing in result.Points)
                                {
                                    double dx = existing.X - pt.X, dy = existing.Y - pt.Y;
                                    if (dx * dx + dy * dy < mssq) { tooClose = true; break; }
                                }
                            }
                            if (tooClose) continue;
                            result.Points.Add(pt);
                            addedThisPass++;
                            densifiedAdded++;
                        }
                        if (addedThisPass == 0) break;
                        result.UncoveredSamples.Clear();
                        result.CoveragePercent = ComputeCoveragePercent(
                            room, bb, result.Points,
                            rule.CoverageRadiusMm * MmToFt, result.UncoveredSamples);
                    }
                    if (densifiedAdded > 0)
                        result.Warnings.Add(
                            $"CoverageGrid: densified +{densifiedAdded} extra point(s) for non-rectangular " +
                            $"room — bbox grid left {result.UncoveredSamples?.Count ?? 0} uncovered sample(s) " +
                            $"in narrow leg(s).");
                }

                if (rule.GuaranteeCoverage && result.CoveragePercent < 99.0)
                {
                    result.Warnings.Add(
                        $"UNCOVERED_ZONE: room {room.Id.Value} only {result.CoveragePercent:F1}% covered " +
                        $"(rule {rule.RuleId} requires 100%)");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CoverageGridGenerator.Generate: {ex.Message}");
                result.Warnings.Add($"CoverageGrid exception: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Phase 139.30 (M-08 deep) — group uncovered samples by greedy
        /// 2× coverage-radius proximity and return cluster centroids.
        /// Cheap O(n²) for the small uncovered-sample count we expect
        /// (≤ 100 by ComputeCoveragePercent's cap).
        /// </summary>
        private static List<XYZ> ClusterUncoveredSamples(IList<XYZ> samples, double coverageRadiusFt)
        {
            var centroids = new List<XYZ>();
            if (samples == null || samples.Count == 0) return centroids;
            // 2× radius — points within this distance of each other can
            // share a single new fixture.
            double clusterRadiusFt = coverageRadiusFt * 2.0;
            double rSq = clusterRadiusFt * clusterRadiusFt;
            var claimed = new bool[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                if (claimed[i] || samples[i] == null) continue;
                claimed[i] = true;
                XYZ acc = samples[i];
                int n = 1;
                for (int j = i + 1; j < samples.Count; j++)
                {
                    if (claimed[j] || samples[j] == null) continue;
                    double dx = samples[j].X - samples[i].X;
                    double dy = samples[j].Y - samples[i].Y;
                    if (dx * dx + dy * dy <= rSq) { acc += samples[j]; n++; claimed[j] = true; }
                }
                centroids.Add(acc * (1.0 / n));
            }
            return centroids;
        }

        private static bool HasObstructionWithin(XYZ pt, List<ExclusionRect> obstructions, double clearFt)
        {
            if (obstructions == null || obstructions.Count == 0) return false;
            foreach (var r in obstructions)
            {
                if (r.Contains(pt.X, pt.Y)) return true;
                double dx = Math.Max(0.0, Math.Max(r.MinX - pt.X, pt.X - r.MaxX));
                double dy = Math.Max(0.0, Math.Max(r.MinY - pt.Y, pt.Y - r.MaxY));
                double d  = Math.Sqrt(dx * dx + dy * dy);
                if (d < clearFt) return true;
            }
            return false;
        }

        /// <summary>
        /// Approximate coverage by sampling a uniform 0.5m grid across
        /// the room bbox, testing each sample point against the union of
        /// CoverageRadiusFt circles centred on placed points.
        /// </summary>
        private double ComputeCoveragePercent(Room room, BoundingBoxXYZ bb, List<XYZ> placed,
            double coverageRadiusFt, List<XYZ> uncoveredSamples)
        {
            if (placed == null || placed.Count == 0) return 0.0;
            double sampleStepFt = 0.5 / 0.3048; // 0.5m
            int total = 0, covered = 0;
            double cr2 = coverageRadiusFt * coverageRadiusFt;
            double zSample = room.Level?.Elevation ?? bb.Min.Z;
            for (double x = bb.Min.X + 0.5; x <= bb.Max.X; x += sampleStepFt)
            {
                for (double y = bb.Min.Y + 0.5; y <= bb.Max.Y; y += sampleStepFt)
                {
                    bool inside = true;
                    try { inside = room.IsPointInRoom(new XYZ(x, y, zSample)); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    if (!inside) continue;
                    total++;
                    bool isCovered = false;
                    foreach (var p in placed)
                    {
                        double dx = p.X - x, dy = p.Y - y;
                        if (dx * dx + dy * dy <= cr2) { isCovered = true; break; }
                    }
                    if (isCovered) covered++;
                    else if (uncoveredSamples != null && uncoveredSamples.Count < 100)
                        uncoveredSamples.Add(new XYZ(x, y, zSample));
                }
            }
            if (total == 0) return 0.0;
            return 100.0 * covered / total;
        }
    }
}
