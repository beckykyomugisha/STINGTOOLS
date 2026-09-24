// ══════════════════════════════════════════════════════════════════════════
//  MaterialIdentityPlanner.cs — one identity per material, readable by a tag.
//
//  WHY. A material callout (STING - Materials Tag, SPECIALIST_TAG_BUILD_SHEET
//  §5) reads the MATERIAL's built-in identity — Mark, Description, Keynote — because
//  those need no shared parameter. Today they disagree:
//    • Mark = MAT_CODE only on materials CreateBLE/MEPMaterials made; StampCodes and
//      the compound-type creator write the shared MAT_CODE and never Mark.
//    • Description holds the "enriched" paragraph (Category: … | Application: … |
//      Durability: …) — hundreds of characters, unusable as a callout.
//    • Keynote holds MAT_ISO_19650_ID, a key that is in no keynote table.
//  This plans the fix: Mark ← code, Keynote ← code, Description ← the short name,
//  the long paragraph moved to MAT_SPECIFICATIONS, the shared MAT_CODE filled where
//  the register names the material (the StampCodes rule), and the shared MAT_NAME
//  filled when empty — STING - Materials Tag prints MAT_CODE / MAT_NAME.
//
//  ── THE RULES ────────────────────────────────────────────────────────────
//  CODE: the shared MAT_CODE if set, else the register's code for an exact name
//  match (MaterialRegistry.ByName — the StampCodes rule, never fuzzy). No code → the
//  material is reported, nothing is written.
//
//  NEVER CLOBBER A PERSON. A field is written when it is empty, or when it holds a
//  value STING itself wrote and the new value supersedes (the ISO id in Keynote, the
//  enriched paragraph in Description). A different value someone typed is REPORTED
//  and left — unless the caller forces it, which is an explicit choice.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core.Materials
{
    /// <summary>A material's identity fields as read off the model.</summary>
    public sealed class MaterialIdentityInput
    {
        public string Name = "";
        public string SharedCode = "";      // MAT_CODE (shared)
        public string Mark = "";            // ALL_MODEL_MARK
        public string Description = "";     // ALL_MODEL_DESCRIPTION
        public string Keynote = "";         // KEYNOTE_PARAM
        public string Specifications = "";  // MAT_SPECIFICATIONS (shared)
        public bool HasSharedCodeParam = true;
        public bool HasSpecificationsParam = true;
        /// <summary>MAT_NAME (shared) — row 2 of STING - Materials Tag.</summary>
        public string SharedName = "";
        public bool HasSharedNameParam = true;
    }

    /// <summary>One planned field write.</summary>
    public sealed class MaterialIdentityWrite
    {
        public string Field = "";   // Mark / Description / Keynote / MAT_CODE / MAT_SPECIFICATIONS
        public string From = "";
        public string To = "";
        public override string ToString() => $"{Field}: '{From}' → '{To}'";
    }

    public enum MaterialIdentityVerdict
    {
        /// <summary>Something will be written.</summary>
        Update,
        /// <summary>Already consistent — nothing to do.</summary>
        InSync,
        /// <summary>No code on the material and no register row names it.</summary>
        NoCode,
    }

    public sealed class MaterialIdentityRow
    {
        public string MaterialName = "";
        public string Code = "";
        /// <summary>"shared MAT_CODE", "register", or "".</summary>
        public string CodeSource = "";
        public MaterialIdentityVerdict Verdict;
        public List<MaterialIdentityWrite> Writes = new List<MaterialIdentityWrite>();
        /// <summary>Fields holding a value someone set that differs from the plan — left alone.</summary>
        public List<string> Conflicts = new List<string>();
        public bool WillWrite => Writes.Count > 0;
    }

    public static class MaterialIdentityPlanner
    {
        /// <summary>Longest Description that still reads as a callout name.</summary>
        public const int CalloutNameMaxLength = 80;

        private static readonly string[] EnrichedLabels =
        {
            "Category: ", "Application: ", "Features: ", "Specifications: ", "Durability: ",
            "Fire Rating: ", "Density: ", "Thermal Conductivity: ", "Embodied Carbon: ",
        };

        /// <summary>
        /// True when <paramref name="description"/> is the paragraph
        /// MaterialCommands.BuildEnrichedDescription writes: labelled segments joined by
        /// " | ". A person's own description does not look like that.
        /// </summary>
        public static bool IsStingEnrichedDescription(string description)
        {
            var d = description ?? "";
            if (d.Length == 0) return false;
            bool labelled = EnrichedLabels.Any(l => d.StartsWith(l, StringComparison.Ordinal)
                                                  || d.Contains(" | " + l));
            return labelled;
        }

        public static MaterialIdentityRow Plan(MaterialIdentityInput m, MaterialRegistry registry, bool force = false)
        {
            m = m ?? new MaterialIdentityInput();
            string T(string s) => (s ?? "").Trim();
            var row = new MaterialIdentityRow { MaterialName = T(m.Name) };

            var byName = registry?.ByName(row.MaterialName);
            string shared = T(m.SharedCode);
            if (shared.Length > 0) { row.Code = shared; row.CodeSource = "shared MAT_CODE"; }
            else if (byName != null && T(byName.Code).Length > 0) { row.Code = T(byName.Code); row.CodeSource = "register"; }

            if (row.Code.Length == 0)
            {
                row.Verdict = MaterialIdentityVerdict.NoCode;
                return row;
            }

            // The row that describes THIS code: by code first (a coded material may have
            // been renamed), else the name match.
            var reg = registry?.ByCode(row.Code) ?? byName;
            string shortName = T(reg?.Name);
            if (shortName.Length == 0 || shortName.Length > CalloutNameMaxLength) shortName = row.MaterialName;
            string iso = T(reg?.Iso19650Id);

            void Write(string field, string from, string to) =>
                row.Writes.Add(new MaterialIdentityWrite { Field = field, From = from ?? "", To = to });

            // Shared MAT_NAME — what STING - Materials Tag prints under the code. Only
            // CreateBLE/MEPMaterials ever wrote it, so every other material's tag printed
            // a code with nothing under it. Filled when empty; never overwritten.
            if (T(m.SharedName).Length == 0 && m.HasSharedNameParam)
                Write("MAT_NAME", "", shortName);

            // Shared MAT_CODE — the StampCodes rule: only when empty, from the register.
            if (shared.Length == 0 && m.HasSharedCodeParam)
                Write("MAT_CODE", "", row.Code);

            // Mark ← code.
            string mark = T(m.Mark);
            if (mark.Length == 0 || (force && mark != row.Code)) Write("Mark", mark, row.Code);
            else if (!string.Equals(mark, row.Code, StringComparison.OrdinalIgnoreCase))
                row.Conflicts.Add($"Mark is '{mark}', code is '{row.Code}'");

            // Keynote ← code. The ISO id STING wrote is superseded without asking.
            string key = T(m.Keynote);
            bool keyIsSting = iso.Length > 0 && string.Equals(key, iso, StringComparison.OrdinalIgnoreCase);
            if (key.Length == 0 || keyIsSting || (force && key != row.Code)) Write("Keynote", key, row.Code);
            else if (!string.Equals(key, row.Code, StringComparison.OrdinalIgnoreCase))
                row.Conflicts.Add($"Keynote is '{key}', code is '{row.Code}'");

            // Description ← short name. STING's enriched paragraph moves to MAT_SPECIFICATIONS.
            string desc = m.Description ?? "";
            bool descIsSting = IsStingEnrichedDescription(desc);
            if (T(desc).Length == 0 || descIsSting || (force && T(desc) != shortName))
            {
                if (T(desc) != shortName) Write("Description", desc, shortName);
                if (descIsSting && m.HasSpecificationsParam && T(m.Specifications).Length == 0)
                    Write("MAT_SPECIFICATIONS", "", desc);
            }
            else if (!string.Equals(T(desc), shortName, StringComparison.OrdinalIgnoreCase))
                row.Conflicts.Add($"Description is set by someone ('{Clip(desc)}')");

            row.Verdict = row.WillWrite ? MaterialIdentityVerdict.Update : MaterialIdentityVerdict.InSync;
            return row;
        }

        public static List<MaterialIdentityRow> PlanAll(IEnumerable<MaterialIdentityInput> materials,
            MaterialRegistry registry, bool force = false)
            => (materials ?? Enumerable.Empty<MaterialIdentityInput>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => Plan(m, registry, force)).ToList();

        public static string Summary(IList<MaterialIdentityRow> rows, bool applied)
        {
            rows = rows ?? new List<MaterialIdentityRow>();
            if (rows.Count == 0) return "This model has no materials.";
            int upd = rows.Count(r => r.Verdict == MaterialIdentityVerdict.Update);
            int sync = rows.Count(r => r.Verdict == MaterialIdentityVerdict.InSync);
            int none = rows.Count(r => r.Verdict == MaterialIdentityVerdict.NoCode);
            int conf = rows.Count(r => r.Conflicts.Count > 0);
            var sb = new StringBuilder();
            sb.AppendLine($"{rows.Count} material(s):");
            sb.AppendLine($"  {upd} {(applied ? "updated" : "to update")} — " +
                          $"{rows.Sum(r => r.Writes.Count)} field write(s)");
            sb.AppendLine($"  {sync} already in sync");
            sb.AppendLine($"  {none} have no code — neither a MAT_CODE nor a register row with that name. " +
                          "A callout on these prints nothing.");
            if (conf > 0)
                sb.AppendLine($"  {conf} have a Mark / Keynote / Description someone set that differs — left alone " +
                              "(listed in the CSV; re-run with Overwrite to replace them).");
            return sb.ToString();
        }

        /// <summary>One line per material: what it has, what it gets, and anything left alone.</summary>
        public static List<string> ToCsv(IEnumerable<MaterialIdentityRow> rows)
        {
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
            var outLines = new List<string> { "Material,Code,CodeSource,Verdict,Writes,LeftAlone" };
            foreach (var r in rows ?? Enumerable.Empty<MaterialIdentityRow>())
                outLines.Add(string.Join(",", Q(r.MaterialName), Q(r.Code), Q(r.CodeSource), r.Verdict.ToString(),
                    Q(string.Join("; ", r.Writes.Select(w => w.Field + "=" + Clip(w.To)))),
                    Q(string.Join("; ", r.Conflicts))));
            return outLines;
        }

        private static string Clip(string s) => s.Length <= 40 ? s : s.Substring(0, 37) + "…";
    }
}
