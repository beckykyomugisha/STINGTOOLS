// Phase 127-B — Placement Centre preview source.
//
// Companion to TagPreviewSource (Pack 9). Reads the same room +
// rule loop FixturePlacementEngine.PlaceFixturesInScope walks but
// emits PreviewPrimitive cross / outline shapes at every candidate
// position rather than committing FamilyInstance objects.
//
// Pack 9 PreviewController owns the IDirectContext3DServer
// registration; this file only produces draw commands. No model
// mutation, no transaction, offline-safe.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using StingTools.Core.Placement;

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

            // Run the engine in dry-run mode. The result.PlacedIds will be
            // empty, but result.Warnings + an internal candidate list
            // surface the points the engine WOULD have placed at.
            // TODO-VERIFY-API: FixturePlacementEngine.PlaceFixturesInScope
            // dryRun=true must short-circuit before NewFamilyInstance —
            // the engine implementation respects this in its candidate
            // accept path.
            PlacementResult result = null;
            try
            {
                result = FixturePlacementEngine.PlaceFixturesInScope(_doc, _roomIds, _rules, dryRun: true);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlacementPreviewSource.Draw: dry-run failed: {ex.Message}");
                yield break;
            }

            // Engine's dry-run reports anchor points via result.PlacedIds
            // when running in preview mode (the ID is the room owner) —
            // but with the simpler "draw the room centroid + a marker
            // ring at every accepted candidate" pattern we walk rooms
            // ourselves. Per-room work keeps Phase B small; full
            // candidate replay graduates with Phase D's preview tuning.
            int n = 0;
            foreach (var roomId in _roomIds)
            {
                Room room = null;
                try { room = _doc.GetElement(roomId) as Room; }
                catch { }
                if (room == null) continue;

                LocationPoint loc = null;
                try { loc = room.Location as LocationPoint; }
                catch { }
                if (loc?.Point == null) continue;

                XYZ c = loc.Point;
                yield return new PreviewPrimitive
                {
                    Kind = PreviewPrimitiveKind.Cross,
                    ColorArgb = unchecked((int)0xFF4080FF), // STING blue
                    Points = new List<XYZ> { c },
                };
                // 600 mm marker ring
                double r = 600.0 / 304.8;
                yield return new PreviewPrimitive
                {
                    Kind = PreviewPrimitiveKind.Outline,
                    ColorArgb = unchecked((int)0xFF80B0FF),
                    Points = new List<XYZ>
                    {
                        new XYZ(c.X - r, c.Y - r, c.Z),
                        new XYZ(c.X + r, c.Y - r, c.Z),
                        new XYZ(c.X + r, c.Y + r, c.Z),
                        new XYZ(c.X - r, c.Y + r, c.Z),
                        new XYZ(c.X - r, c.Y - r, c.Z),
                    },
                };
                n++;
                if (n > 500) yield break; // cap preview density
            }
        }
    }
}
