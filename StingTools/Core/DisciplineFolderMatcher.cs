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

        /// <summary>The ISO 19650 Role segment of a model file name, or null.
        ///
        /// A federated model is named as an information container —
        /// Project-Originator-Volume-Level-Type-Role-Number — so "SAH-PLNS-ZZ-XX-M3-S-0001.rvt"
        /// is the structural model. That is the only discipline a model carries on its
        /// own: it has no sheet number to read one from. A workshared local copy's
        /// "_username" suffix and any "_detached"-style tail are ignored. Anything that
        /// is not a seven-field identifier returns null — a name like "Hospital_v3.rvt"
        /// says nothing about discipline, and guessing would file a model in the wrong
        /// discipline's folder.</summary>
        public static string RoleFromModelFileName(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName)) return null;
            string name = pathOrName.Trim();
            int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            if (slash >= 0) name = name.Substring(slash + 1);
            int dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
            int us = name.IndexOf('_');
            if (us > 0) name = name.Substring(0, us);
            var seg = Drawing.Iso19650DocumentCode.Decompose(name);
            return string.IsNullOrWhiteSpace(seg?.Role) ? null : seg.Role.ToUpperInvariant();
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
