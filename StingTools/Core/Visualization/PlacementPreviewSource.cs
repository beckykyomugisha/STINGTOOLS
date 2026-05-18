// Phase 127-B + PC-22 — Placement Centre preview source.
//
// Companion to TagPreviewSource (Pack 9). Walks every room × rule pair
// the engine would consider, runs PlacementScorer in-process to obtain
// the candidate XYZs, and emits a coloured cross + outline ring per
// candidate. PC-22 hashes each rule's MergeKey to a distinct ARGB so
// the user can tell at a glance which rule produced which candidate.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using StingTools.Core.Placement;
using StingTools.Core;

namespace StingTools.Core.Visualization
{
    public class PlacementPreviewSource : IPreviewSource
    {
        private readonly Document _doc;
        private readonly IList<ElementId> _roomIds;
        private readonly IList<PlacementRule> _rules;

        public string Name => "Placement Centre — preview";

        public PlacementPreviewSource(Document doc, IList<ElementId> roomIds, IList<PlacementRule> rules)
        {
            _doc = doc;
            _roomIds = roomIds ?? new List<ElementId>();
            _rules = rules ?? new List<PlacementRule>();
        }

        public IEnumerable<PreviewPrimitive> Draw()
        {
            if (_doc == null) yield break;

            // Per-rule colour cache so identical merge keys reuse the same
            // hue across primitives.
            var colourFor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var scorer = new PlacementScorer(_doc);
            int total = 0;
            const int CAP = 1500; // hard cap so the preview stays responsive
            foreach (var roomId in _roomIds)
            {
                Room room = null;
                try { room = _doc.GetElement(roomId) as Room; } catch { }
                if (room == null) continue;
                foreach (var rule in _rules)
                {
                    if (rule == null) continue;
                    List<PlacementCandidate> cands = null;
                    try
                    {
                        cands = scorer.Score(room, rule, alreadyPlaced: new List<XYZ>(), countInRoomSoFar: 0);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"PlacementPreviewSource Score room {roomId} rule {rule.MergeKey}: {ex.Message}");
                        continue;
                    }
                    if (cands == null || cands.Count == 0) continue;

                    string key = rule.MergeKey ?? "";
                    if (!colourFor.TryGetValue(key, out int rgb))
                    {
                        rgb = HueFromKey(key);
                        colourFor[key] = rgb;
                    }
                    int dim = DimColour(rgb);

                    foreach (var c in cands)
                    {
                        XYZ pt = c.Position;
                        if (pt == null) continue;
                        yield return new PreviewPrimitive
                        {
                            Kind = PreviewPrimitiveKind.Cross,
                            ColorArgb = rgb,
                            Points = new List<XYZ> { pt },
                        };
                        double r = 600.0 / 304.8;
                        yield return new PreviewPrimitive
                        {
                            Kind = PreviewPrimitiveKind.Outline,
                            ColorArgb = dim,
                            Points = new List<XYZ>
                            {
                                new XYZ(pt.X - r, pt.Y - r, pt.Z),
                                new XYZ(pt.X + r, pt.Y - r, pt.Z),
                                new XYZ(pt.X + r, pt.Y + r, pt.Z),
                                new XYZ(pt.X - r, pt.Y + r, pt.Z),
                                new XYZ(pt.X - r, pt.Y - r, pt.Z),
                            },
                        };
                        total++;
                        if (total > CAP) yield break;
                    }
                }
            }
        }

        /// <summary>Stable hash → bright HSV colour, returned as 0xFFRRGGBB.</summary>
        private static int HueFromKey(string key)
        {
            // Deterministic hash so the same rule keeps the same colour.
            unchecked
            {
                int h = 23;
                foreach (var ch in key ?? "") h = h * 31 + ch;
                double hue = ((h & 0xFFFF) / 65535.0) * 360.0;
                HsvToRgb(hue, 0.85, 0.95, out int r, out int g, out int b);
                return (int)(0xFF000000U | (uint)((r << 16) | (g << 8) | b));
            }
        }

        private static int DimColour(int argb)
        {
            int r = (argb >> 16) & 0xFF;
            int g = (argb >> 8) & 0xFF;
            int b = argb & 0xFF;
            r = (int)(r * 0.6); g = (int)(g * 0.6); b = (int)(b * 0.6);
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        private static void HsvToRgb(double h, double s, double v, out int r, out int g, out int b)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double rp = 0, gp = 0, bp = 0;
            if (h < 60)       { rp = c; gp = x; bp = 0; }
            else if (h < 120) { rp = x; gp = c; bp = 0; }
            else if (h < 180) { rp = 0; gp = c; bp = x; }
            else if (h < 240) { rp = 0; gp = x; bp = c; }
            else if (h < 300) { rp = x; gp = 0; bp = c; }
            else              { rp = c; gp = 0; bp = x; }
            r = Math.Max(0, Math.Min(255, (int)Math.Round((rp + m) * 255)));
            g = Math.Max(0, Math.Min(255, (int)Math.Round((gp + m) * 255)));
            b = Math.Max(0, Math.Min(255, (int)Math.Round((bp + m) * 255)));
        }
    }
}
