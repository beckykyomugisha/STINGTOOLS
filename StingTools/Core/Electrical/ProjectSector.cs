// ProjectSector — which sector this project is, for everything that needs one.
//
// Split from SpareCapacityTarget so that file stays Revit-free and its matching
// rules can be tested against real project names without opening Revit.
//
// It reads PRJ_BUILDING_USE_TXT FIRST, which the old resolver in
// LoadDemandAuditCommand did not. That parameter is what SustainabilityEngine
// already asks for, so a project that has answered the question once should not
// be asked again by inference - and inference from a building's NAME is exactly
// where a wrong answer looks most convincing.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Electrical
{
    /// <summary>Resolves a project's sector from its own declarations, then its names.</summary>
    public static class ProjectSector
    {
        /// <summary>
        /// The project's sector. Never throws; an undecidable project comes
        /// back as <see cref="SpareCapacityTarget.DefaultSector"/>.
        /// </summary>
        public static string Resolve(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return SpareCapacityTarget.DefaultSector;

                // Declared beats inferred. SustainabilityEngine reads the same
                // parameter, so answering it once settles it for both.
                foreach (string pn in new[] { "PRJ_BUILDING_USE_TXT", "Building Type" })
                {
                    string declared = pi.LookupParameter(pn)?.AsString();
                    if (string.IsNullOrWhiteSpace(declared)) continue;

                    string fromDeclared = SpareCapacityTarget.SectorFromText(declared);
                    // An explicit value that matches nothing is worth a line in
                    // the log: it means the project HAS answered and the answer
                    // is not one this code understands, which is different from
                    // not having answered.
                    if (!string.Equals(fromDeclared, SpareCapacityTarget.DefaultSector,
                                       StringComparison.OrdinalIgnoreCase))
                        return fromDeclared;

                    StingLog.Info($"ProjectSector: {pn} = '{declared}' did not match a known sector; " +
                                  "falling through to the project's names");
                }

                return SpareCapacityTarget.SectorFromText(
                    (pi.OrganizationDescription ?? "") + " " +
                    (pi.BuildingName ?? "") + " " +
                    (pi.Name ?? ""));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSector.Resolve: {ex.Message} — using {SpareCapacityTarget.DefaultSector}");
                return SpareCapacityTarget.DefaultSector;
            }
        }
    }
}
