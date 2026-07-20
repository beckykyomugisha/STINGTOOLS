// Minimal PlacementResult shim.
//
// The real PlacementResult lives in FixturePlacementEngine.cs alongside heavy
// Revit types (ElementId, XYZ, Document). PlacementAdvisoryValidator only ever
// reads CountsByRule, so this shim exposes that surface and nothing else —
// same approach as StingTools.Routing.Tests/TestHelpers/StingCableShim.cs.
//
// If PlacementAdvisoryValidator ever starts reading more of PlacementResult,
// this shim must grow to match or the test project stops compiling — which is
// the intended tripwire.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Placement
{
    public class PlacementResult
    {
        public Dictionary<string, int> CountsByRule { get; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
