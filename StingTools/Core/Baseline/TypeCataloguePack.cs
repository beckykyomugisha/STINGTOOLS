// ══════════════════════════════════════════════════════════════════════════
//  TypeCataloguePack.cs — regional type catalogues, opt-in.
//
//  WHY PACKS RATHER THAN A CORPORATE LIST. Shipping thirty East African types
//  in the baseline would make one person's reading of the market a standard
//  that every future project is audited against. A project whose doors are
//  genuinely 850 wide would be told, on every run, that it is missing a type
//  it does not want — a confident default nobody asked for, which is the
//  failure mode this schedule has spent sixteen fixes removing.
//
//  So: no pack applies unless a project adopts it BY ID. Packs are additive,
//  overridable per type, and versioned in the id so V1 -> V2 is a migration
//  rather than a silent redefinition of what somebody already adopted.
//
//  The shipped pack is marked provisional and adopted by nobody. It is to be
//  replaced by harvesting a delivered model — see Baseline_HarvestTypes. A
//  provisional pack nobody has adopted can be wrong for free.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Baseline
{
    public sealed class TypeCataloguePack
    {
        /// <summary>Versioned, e.g. EA-RESIDENTIAL-V1. The version is part of
        /// the identity so a later revision cannot silently redefine what a
        /// project adopted.</summary>
        public string Id = "";
        public string Title = "";

        /// <summary>"provisional" or "adopted". Provisional means the sizes are
        /// a starting shape, not a standard.</summary>
        public string Status = "provisional";

        /// <summary>Where the sizes came from. Required on a provisional pack:
        /// a catalogue that cannot say where its numbers came from is a guess
        /// wearing a standard's clothes.</summary>
        public string SourceNote = "";

        public List<BaselineFamilyType> FamilyTypes = new List<BaselineFamilyType>();

        public bool IsProvisional =>
            !string.Equals(Status, "adopted", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class TypeCatalogueLibrary
    {
        public string SchemaVersion = "1.0";
        public string Note = "";
        public List<TypeCataloguePack> Packs = new List<TypeCataloguePack>();

        public TypeCataloguePack ById(string id) =>
            string.IsNullOrWhiteSpace(id) ? null
            : Packs?.FirstOrDefault(p => p != null
                && string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Problems inside the catalogue file itself, before adoption.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            foreach (var p in Packs ?? new List<TypeCataloguePack>())
            {
                if (p == null) continue;
                if (string.IsNullOrWhiteSpace(p.Id)) { problems.Add("a pack has no id"); continue; }
                if (p.IsProvisional && (p.SourceNote ?? "").Trim().Length < 40)
                    problems.Add($"pack '{p.Id}' is provisional but its sourceNote is too short to "
                               + "say where its sizes came from");
                if (p.FamilyTypes == null || p.FamilyTypes.Count == 0)
                    problems.Add($"pack '{p.Id}' declares no family types");
            }

            var dupes = (Packs ?? new List<TypeCataloguePack>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id))
                .GroupBy(p => p.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (string d in dupes) problems.Add($"pack id '{d}' is declared more than once");

            return problems;
        }
    }

    public sealed class CatalogueAdoptionResult
    {
        public readonly List<string> Adopted = new List<string>();
        public readonly List<string> Unknown = new List<string>();
        public int TypesAdded;
        public int TypesOverriddenByProject;

        /// <summary>Null when the project adopted nothing — the default, and not
        /// worth a line. An unknown id is always worth one.</summary>
        public string Summary()
        {
            if (Adopted.Count == 0 && Unknown.Count == 0) return null;

            var parts = new List<string>();
            if (Adopted.Count > 0)
            {
                parts.Add($"Adopted {Adopted.Count} catalogue pack(s): {string.Join(", ", Adopted)} "
                        + $"— {TypesAdded} type(s) added"
                        + (TypesOverriddenByProject > 0
                            ? $", {TypesOverriddenByProject} overridden by this project" : "")
                        + ".");
            }
            if (Unknown.Count > 0)
                parts.Add($"{Unknown.Count} adopted pack id(s) do not exist and were ignored: "
                        + string.Join(", ", Unknown)
                        + ". Check the spelling against STING_TYPE_CATALOGUES.json.");
            return string.Join("\n", parts);
        }
    }

    public static class CatalogueAdopter
    {
        /// <summary>
        /// Merge the family types of every pack the baseline adopts BY ID.
        ///
        /// A type the project declares itself wins over the pack's version of
        /// the same (category, typeName) — adopting a pack must never take away
        /// a project's ability to differ from it.
        /// </summary>
        public static CatalogueAdoptionResult Adopt(ProjectBaseline baseline, TypeCatalogueLibrary library)
        {
            var r = new CatalogueAdoptionResult();
            if (baseline?.AdoptCatalogues == null || baseline.AdoptCatalogues.Count == 0) return r;

            var already = new HashSet<string>(
                (baseline.FamilyTypes ?? new List<BaselineFamilyType>())
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.TypeName))
                    .Select(Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (string id in baseline.AdoptCatalogues)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                var pack = library?.ById(id);
                if (pack == null)
                {
                    // Silently ignoring a typo would leave somebody believing
                    // they adopted a catalogue they did not.
                    r.Unknown.Add(id.Trim());
                    continue;
                }

                r.Adopted.Add(pack.Id);
                foreach (var ft in pack.FamilyTypes ?? new List<BaselineFamilyType>())
                {
                    if (ft == null || string.IsNullOrWhiteSpace(ft.TypeName)) continue;
                    if (already.Contains(Key(ft))) { r.TypesOverriddenByProject++; continue; }
                    baseline.FamilyTypes.Add(ft);
                    already.Add(Key(ft));
                    r.TypesAdded++;
                }
            }
            return r;
        }

        private static string Key(BaselineFamilyType t) =>
            (t.Category ?? "").Trim() + "|" + (t.TypeName ?? "").Trim();
    }
}
