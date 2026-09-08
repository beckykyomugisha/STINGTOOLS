// ══════════════════════════════════════════════════════════════════════════
//  ProdNameCode.cs — a type name that STATES its product code, read rather
//  than re-inferred.
//
//  BS EN ISO 22014:2024 (superseding BS 8541-1) names a library object in
//  three underscore-separated fields — Source_Type_Subtype — with no spaces
//  and components hyphenated inside a field. The middle field is the object's
//  TYPE, and for STING that is the PROD code the plugin already mints:
//
//      PLNS_WBL_Hollow200-Plastered
//      ^^^^ ^^^ ^^^^^^^^^^^^^^^^^^^
//      |    |   subtype: what distinguishes this one
//      |    the ISO 19650 PROD code
//      originator, from PRJ_ORG_ORIGINATOR_CODE_TXT
//
//  WHY THIS TIER EXISTS. Before it, a type name had to CONTAIN a substance
//  word — "Blockwork", "Clay Brick" — for the pattern rules to infer a code
//  from it. That works, and rescues the badly-named types a delivered model is
//  full of. But it means a conforming model carries TWO vocabularies for one
//  fact: the word "Blockwork" in the name and the code WBL the resolver
//  derives from it, free to disagree the moment either is edited.
//
//  One fact, one place. When the name states the code, believe it; the pattern
//  rules stay exactly as they are for every name that does not.
//
//  THE SAFETY PROPERTY: only a token that IS a known PROD code counts, and only
//  in the second field. "PLNS_WBL_DR-Set" reads WBL, not DR — a subtype word
//  that happens to collide with some code cannot hijack the name.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    public static class ProdNameCode
    {
        /// <summary>
        /// The PROD code a type name declares, or null when it declares none.
        ///
        /// <param name="typeName">The Revit type name.</param>
        /// <param name="knownCodes">Every code the loaded rule set can issue. A token
        /// is only a code if it is one of these — otherwise any underscore-delimited
        /// word would become one, which is how a naming convention turns into a
        /// guessing game.</param>
        /// </summary>
        public static string Extract(string typeName, ICollection<string> knownCodes)
        {
            if (string.IsNullOrWhiteSpace(typeName) || knownCodes == null || knownCodes.Count == 0)
                return null;

            // Fields are underscore-separated; components inside a field are hyphenated,
            // so a hyphen must not split. ISO 22014 puts the Type in the second field.
            var fields = typeName.Split('_');
            if (fields.Length < 2) return null;

            string candidate = fields[1].Trim();
            if (candidate.Length == 0) return null;

            foreach (string code in knownCodes)
                if (string.Equals(code, candidate, StringComparison.OrdinalIgnoreCase))
                    return code;

            return null;
        }

        /// <summary>
        /// Compose an ISO 22014 name. Subtype components arrive as separate words and
        /// are joined with hyphens, because the underscore is reserved for the field
        /// boundary — mixing the two is what makes a coded name unparseable.
        /// </summary>
        public static string Compose(string originator, string prodCode, params string[] subtypeParts)
        {
            string org = Clean(originator);
            string code = Clean(prodCode);
            if (org.Length == 0) org = "PLNS";
            if (code.Length == 0) return null;

            var parts = new List<string>();
            foreach (string p in subtypeParts ?? new string[0])
            {
                string c = Clean(p);
                if (c.Length > 0) parts.Add(c);
            }

            return parts.Count > 0
                ? $"{org}_{code}_{string.Join("-", parts)}"
                : $"{org}_{code}";
        }

        /// <summary>Strip what ISO 22014 does not allow: spaces, and the two separators
        /// when they appear inside a component. PascalCase survives untouched.</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.Trim())
                if (!char.IsWhiteSpace(c) && c != '_' && c != '-') sb.Append(c);
            return sb.ToString();
        }
    }
}
