// Revit-free: which TEXT parameter mirrors which typed one.
//
// MR_PARAMETERS.txt gives every numeric / length / area / yes-no STING parameter a
// TEXT twin whose description begins "<BASE_NAME> display mirror" (for example
// ASS_ELEVATION_TXT is "ASS_ELEVATION_M display mirror [auto-generated]"). Tag
// labels must read the mirror: Revit refuses a family that carries a shared
// parameter under a different type from the project.

using System;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    public static class DisplayMirrorNames
    {
        private static readonly Regex MirrorOf =
            new Regex(@"^\s*(\S+)\s+display mirror\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The parameter a definition mirrors, read from its description, or null
        /// when the description does not declare it a display mirror.
        /// </summary>
        public static string MirroredParameter(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return null;
            var m = MirrorOf.Match(description);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
