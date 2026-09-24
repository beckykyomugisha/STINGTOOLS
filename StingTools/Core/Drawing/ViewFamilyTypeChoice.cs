using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// Which Revit view type (ViewFamilyType) a produced view is created with.
    ///
    /// The producer used to take the FIRST view type of the right family, so a
    /// section drawing got whichever section type happened to load first — its
    /// section head, its detail level, its naming — and a project with a
    /// "Building Section" and a "Wall Section" type got one at random. A drawing
    /// type can now name its view type (<see cref="DrawingType.ViewFamilyTypeName"/>),
    /// and <c>DrawingTypes_EnsureViewTypes</c> creates the named types.
    ///
    /// A name applies only to views of its own family: a section drawing type
    /// that also produces a key plan names a SECTION type, and the plan quietly
    /// takes the default. A name that is in the project under no family at all
    /// is a missing setup step, and says so.
    /// </summary>
    public static class ViewFamilyTypeChoice
    {
        /// <summary>
        /// The Revit ViewFamily (by enum name) a drawing-type purpose produces, or null
        /// when the purpose makes no single kind of view (Clarification, Coordination …).
        /// </summary>
        public static string FamilyForPurpose(string purpose)
        {
            switch ((purpose ?? "").Trim().ToLowerInvariant())
            {
                case "plan":      return "FloorPlan";
                case "rcp":       return "CeilingPlan";
                case "section":   return "Section";
                case "elevation": return "Elevation";
                case "detail":    return "Detail";
                case "3d":        return "ThreeDimensional";
                case "legend":    return "Legend";
                default:          return null;
            }
        }

        /// <summary>
        /// Pick from <paramref name="candidates"/> (name, ViewFamily enum name). Returns the
        /// index chosen, or -1 when the project has no view type of that family at all;
        /// <c>warning</c> is non-null when the pick is not the one the data asked for.
        /// </summary>
        public static int Pick(IList<(string Name, string Family)> candidates, string targetFamily,
            string wantedName, out string warning)
        {
            warning = null;
            int first = -1, named = -1;
            bool nameElsewhere = false;
            bool wantsName = !string.IsNullOrWhiteSpace(wantedName);
            for (int i = 0; i < (candidates?.Count ?? 0); i++)
            {
                var c = candidates[i];
                bool sameFamily = string.Equals(c.Family, targetFamily, StringComparison.OrdinalIgnoreCase);
                bool sameName = wantsName && string.Equals(c.Name?.Trim(), wantedName.Trim(), StringComparison.OrdinalIgnoreCase);
                if (sameFamily && first < 0) first = i;
                if (sameFamily && sameName && named < 0) named = i;
                if (!sameFamily && sameName) nameElsewhere = true;
            }

            if (first < 0)
            {
                warning = $"No view type of family '{targetFamily}' in this project.";
                return -1;
            }
            if (!wantsName || named >= 0) return named >= 0 ? named : first;

            // The name belongs to another kind of view — this rule is not what it names.
            if (nameElsewhere) return first;

            warning = $"View type '{wantedName.Trim()}' is not in this project — using '{candidates[first].Name}'. " +
                      "Run DrawingTypes_EnsureViewTypes to create it.";
            return first;
        }
    }
}
