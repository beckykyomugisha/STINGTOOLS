// RateResolutionLevel.cs — how specifically a rate resolved.
//
// Lives in its own file, and NOT in IRateProvider.cs, because that file imports
// the Revit API for RateRequest.Element and would drag a Document into any test
// project that wanted to assert a pass order. Same namespace as before, so no
// call site changed when it moved.
namespace StingTools.BOQ.Rates
{
    public enum RateResolutionLevel
    {
        /// <summary>No rate at all. The item is not priced.</summary>
        None = 0,
        /// <summary>Category average — every product in the category shares one rate.</summary>
        Category = 1,
        /// <summary>Material-level.</summary>
        Material = 2,
        /// <summary>Category refined by MEP system.</summary>
        System = 3,
        /// <summary>The actual product. The only level that prices a fire door
        /// differently from a cupboard door.</summary>
        Product = 4,
    }
}
