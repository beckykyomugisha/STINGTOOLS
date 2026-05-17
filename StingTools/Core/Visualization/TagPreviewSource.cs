using System;
// Pack 9 — preview source for Smart Tag Placement.
//
// Reads the same scoring loop Smart Place uses, but instead of calling
// IndependentTag.Create, it emits outlined rectangles + leader polylines
// to the preview renderer. Users Confirm → the normal placement command
// runs; Cancel → nothing.
//
// The source is dumb on purpose — it only mirrors what the placement
// engine already computes. Any drift between preview and commit means
// the preview source is wrong, not the engine.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Tags;

namespace StingTools.Core.Visualization
{
    public class TagPreviewSource : IPreviewSource
    {
        private readonly View _view;
        private readonly IEnumerable<ElementId> _hostIds;

        public string Name => "Smart Tag Placement";

        public TagPreviewSource(View view, IEnumerable<ElementId> hostIds)
        {
            _view = view;
            _hostIds = hostIds ?? new List<ElementId>();
        }

        public IEnumerable<PreviewPrimitive> Draw()
        {
            if (_view == null) yield break;
            Document doc = _view.Document;
            double offset = TagPlacementEngine.GetModelOffset(_view);

            foreach (var id in _hostIds)
            {
                Element host;
                try { host = doc.GetElement(id); } catch { continue; }
                if (host == null) continue;

                XYZ centre;
                try { centre = TagPlacementEngine.GetElementCenter(host, _view); }
                catch { continue; }
                if (centre == null) continue;

                // Pack 4 anchor-aware offsets → the preview honours the same
                // placement rules as the committing code path.
                var offsets = TagPlacementEngine.GetCandidateOffsetsWithAnchor(offset, host);
                if (offsets == null || offsets.Length == 0) continue;
                XYZ cand = centre + offsets[0];

                // Leader polyline — centre → candidate tag head.
                yield return new PreviewPrimitive
                {
                    Kind = PreviewPrimitiveKind.Polyline,
                    ColorArgb = unchecked((int)0xFF4080FF), // STING blue
                    Points = new List<XYZ> { centre, cand },
                };

                // Rectangle outline around the candidate tag head — cheap
                // proxy for the tag bounding box.
                double hw = 1.0, hh = 0.5;
                yield return new PreviewPrimitive
                {
                    Kind = PreviewPrimitiveKind.Outline,
                    ColorArgb = unchecked((int)0xFF4080FF),
                    Points = new List<XYZ>
                    {
                        new XYZ(cand.X - hw, cand.Y - hh, cand.Z),
                        new XYZ(cand.X + hw, cand.Y - hh, cand.Z),
                        new XYZ(cand.X + hw, cand.Y + hh, cand.Z),
                        new XYZ(cand.X - hw, cand.Y + hh, cand.Z),
                        new XYZ(cand.X - hw, cand.Y - hh, cand.Z),
                    },
                };
            }
        }
    }
}
