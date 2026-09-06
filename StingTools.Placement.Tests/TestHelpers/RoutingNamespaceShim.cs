// PlacementRule.cs carries `using StingTools.Core.Routing;` but does not
// actually reference a type from that namespace. Declaring the namespace here
// keeps the using directive resolvable inside this Revit-free test project
// without editing production code.
//
// If PlacementRule ever does start using a routing type, compilation here will
// fail with a missing-type error rather than passing silently — the intended
// tripwire.

namespace StingTools.Core.Routing
{
}
