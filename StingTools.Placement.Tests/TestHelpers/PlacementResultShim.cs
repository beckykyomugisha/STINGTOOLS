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
//
// CountsByRule uses the DEFAULT (case-sensitive) comparer to match the real
// PlacementResult in FixturePlacementEngine.cs — the engine writes and the
// validator reads with the same MergeKey, so any case-folding here would make
// the shim more forgiving than production and mask a real key-casing bug.

using System.Collections.Generic;

namespace StingTools.Core.Placement
{
    public class PlacementResult
    {
        public Dictionary<string, int> CountsByRule { get; }
            = new Dictionary<string, int>();
    }
}
