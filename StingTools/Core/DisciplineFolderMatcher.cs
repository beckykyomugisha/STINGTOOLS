// StingTools — which discipline sub-folder an export belongs in
//
// A project's discipline folders are named "A_Architectural", "M_Mechanical",
// "S_Structural"… (ProjectSetup.Disciplines). Exports arrive carrying whatever
// discipline code their source had: a sheet-number prefix ("A", "ME"), a role
// segment from the ISO identifier ("S"), or a STING discipline code ("MECH",
// "FP"). This maps one onto the other.
//
// Revit-free so it is unit-tested (StingTools.Tags.Tests).

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    public static class DisciplineFolderMatcher
    {
        /// <summary>The project's folder for <paramref name="code"/>, or null.
        ///
        /// Tries the code as given first ("A" → "A_Architectural", or a folder
        /// literally named after the code), then the ISO 19650 role letter it folds
        /// to ("MECH" → "M" → "M_Mechanical", "FP" → "Y" → a Y_ folder if the project
        /// has one).
        ///
        /// Null, never a guess, when nothing matches: the caller then leaves the file
        /// in the parent folder, where it is visible, rather than inventing a sibling
        /// folder ("02_SHARED/ME") beside the project's own ("02_SHARED/M_Mechanical").
        /// That sibling is exactly how the Export Centre's discipline split used to
        /// fork the tree.</summary>
        public static string Match(IEnumerable<string> disciplineFolders, string code)
        {
            if (disciplineFolders == null) return null;
            string c = (code ?? "").Trim();
            if (c.Length == 0) return null;

            var folders = disciplineFolders.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            string hit = FindByCode(folders, c);
            if (hit != null) return hit;

            string role = Drawing.Iso19650DocumentCode.NormaliseRole(c);
            if (!string.IsNullOrEmpty(role) && !string.Equals(role, c, StringComparison.OrdinalIgnoreCase))
                return FindByCode(folders, role);
            return null;
        }

        private static string FindByCode(List<string> folders, string code)
        {
            foreach (var f in folders)
                if (string.Equals(f, code, StringComparison.OrdinalIgnoreCase)) return f;
            foreach (var f in folders)
                if (f.StartsWith(code + "_", StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }
    }
}
