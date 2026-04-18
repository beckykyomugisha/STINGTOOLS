// BcfEngine.Clash.cs — Stage 3.4. Clash-aware BCF viewpoint builder that produces
// a real <PerspectiveCamera> + <ClippingPlanes> + <Components> block from a
// ClashRecord, falling back to the existing stub XML when the record is null.
//
// Lives in StingTools (not Planscape.Shared) because it depends on the clash
// engine. Deliberately declared in the Planscape.Shared.BCF namespace so
// callers that already `using Planscape.Shared.BCF;` pick it up without a
// second import.
using StingTools.Core.Clash;

namespace Planscape.Shared.BCF
{
    public static class BcfEngineClashExtensions
    {
        public static string BuildViewpointFromClash(ClashRecord clash, string guid)
        {
            if (clash == null) return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<VisualizationInfo><Components/></VisualizationInfo>";
            return BcfViewpointBuilder.FromClash(clash).BuildBcfv();
        }
    }
}
