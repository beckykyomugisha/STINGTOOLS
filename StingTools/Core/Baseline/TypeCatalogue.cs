// ══════════════════════════════════════════════════════════════════════════
//  TypeCatalogue.cs — B3: opt-in catalogue packs of layer-2 family types.
//
//  WHY NOT A CORPORATE CATALOGUE. The obvious move is to ship ~30 East African
//  types in the universal baseline. It is the wrong one: those sizes would be
//  ONE PERSON'S READING OF THE MARKET, applied to every future project and
//  reported by the audit as though they were standards. A project whose doors
//  are genuinely 850 wide would be told, on every run, that it is missing a
//  type it does not want.
//
//  That is the familiar failure — a confident default nobody asked for, that
//  nobody checks.
//
//  SO: named packs, NONE ACTIVE BY DEFAULT. A project adopts one by id in its
//  own override, which makes adoption a stated decision, visible in the audit.
//  A new market is a new pack, not a code change and not a change to the
//  universal baseline. Packs are versioned in the id, so -V1 → -V2 is an
//  explicit migration rather than a silent redefinition of what a project
//  already adopted.
//
//  The one shipped pack is PROVISIONAL and adopted by nobody. A provisional
//  pack nobody has adopted can be wrong without costing anything; a corporate
//  default cannot. It is a shape, to be replaced by harvesting a delivered
//  model (B4) — so the catalogue grows from work that was actually built and
//  paid for, not from anyone's recollection of the market.
//
//  Revit-free: adoption and validation decide what reaches somebody's model,
//  so they are testable without Revit.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Baseline
{
    /// <summary>One named, versioned set of layer-2 family types.</summary>
    public sealed class TypeCataloguePack
    {
        /// <summary>Stable id, version included: EA-RESIDENTIAL-V1. A project
        /// adopts by this string, so changing it is a migration.</summary>
        public string Id = "";
        public string Title = "";

        /// <summary>"provisional" or "reviewed". A provisional pack states that
        /// its sizes are a starting point, not a standard.</summary>
        public string Status = "provisional";

        /// <summary>REQUIRED on a provisional pack: where the sizes came from,
        /// and that they are to be replaced. A pack whose provenance is not
        /// stated is indistinguishable from a standard.</summary>
        public string SourceNote = "";

        public List<BaselineFamilyType> FamilyTypes = new List<BaselineFamilyType>();

        public const string Provisional = "provisional";
        public const string Reviewed = "reviewed";

        public bool IsProvisional =>
            string.Equals((Status ?? "").Trim(), Provisional, StringComparison.OrdinalIgnoreCase);

        public bool HasKnownStatus =>
            IsProvisional
            || string.Equals((Status ?? "").Trim(), Reviewed, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class TypeCatalogueLibrary
    {
        public string SchemaVersion = "1.0";
        public string Note = "";
        public List<TypeCataloguePack> Packs = new List<TypeCataloguePack>();

        public TypeCataloguePack ById(string id)
            => string.IsNullOrWhiteSpace(id) ? null
             : (Packs ?? new List<TypeCataloguePack>())
                   .FirstOrDefault(p => p != null
                       && string.Equals(p.Id?.Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Problems inside the library itself, before any project adopts
        /// anything. Every one of these would otherwise surface as a confusing
        /// audit on somebody's model.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var packs = Packs ?? new List<TypeCataloguePack>();

            foreach (var p in packs)
            {
                if (p == null) continue;
                string label = string.IsNullOrWhiteSpace(p.Id) ? "(unnamed pack)" : p.Id.Trim();

                if (string.IsNullOrWhiteSpace(p.Id))
                    problems.Add("a catalogue pack has no id, so no project could adopt it");

                if (!p.HasKnownStatus)
                    problems.Add($"pack '{label}' has status '{p.Status}', which is neither "
                               + $"'{TypeCataloguePack.Provisional}' nor '{TypeCataloguePack.Reviewed}'");

                // A provisional pack that does not say WHY it is provisional
                // reads exactly like a standard. That is the whole failure this
                // mechanism exists to avoid.
                if (p.IsProvisional && string.IsNullOrWhiteSpace(p.SourceNote))
                    problems.Add($"pack '{label}' is provisional but states no sourceNote — a "
                               + "provisional pack with no stated provenance reads as a standard");

                if ((p.FamilyTypes ?? new List<BaselineFamilyType>()).Count == 0)
                    problems.Add($"pack '{label}' declares no family types, so adopting it would "
                               + "do nothing");

                // Pack entries must satisfy exactly the layer-2 rules. Borrowing
                // ProjectBaseline.Validate rather than re-stating them is what
                // keeps the two from drifting.
                foreach (string x in new ProjectBaseline { FamilyTypes = p.FamilyTypes }.Validate())
                    problems.Add($"pack '{label}': {x}");
            }

            foreach (var g in packs.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id))
                                   .GroupBy(p => p.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                                   .Where(g2 => g2.Count() > 1))
                problems.Add($"catalogue pack id '{g.Key}' is declared more than once");

            return problems;
        }
    }

    /// <summary>What adoption actually did, for the audit report.</summary>
    public sealed class CatalogueAdoption
    {
        public readonly List<string> AdoptedPackIds = new List<string>();
        public readonly List<string> UnknownPackIds = new List<string>();
        public int TypesAdopted;

        /// <summary>Types that a pack declared and the project had already
        /// declared itself. The project's own entry wins; this is counted so
        /// the override is visible rather than mysterious.</summary>
        public readonly List<string> OverriddenByProject = new List<string>();

        public bool AnythingAdopted => AdoptedPackIds.Count > 0;

        /// <summary>The audit line, or NULL when no pack was named at all — a
        /// project that adopts nothing has nothing to report, and an invented
        /// "0 packs adopted" reads as a finding.</summary>
        public string Summary()
        {
            if (AdoptedPackIds.Count == 0 && UnknownPackIds.Count == 0) return null;

            string s = AdoptedPackIds.Count > 0
                ? $"Catalogue packs adopted: {string.Join(", ", AdoptedPackIds)} "
                  + $"({TypesAdopted} family type(s))."
                : "No catalogue pack was adopted.";

            if (OverriddenByProject.Count > 0)
                s += $" {OverriddenByProject.Count} pack type(s) were overridden by the project's own "
                   + "declaration: " + string.Join(", ", OverriddenByProject.Take(6))
                   + (OverriddenByProject.Count > 6 ? ", …" : "") + ".";

            if (UnknownPackIds.Count > 0)
                s += $" {UnknownPackIds.Count} adopted id(s) match no pack and did NOTHING: "
                   + string.Join(", ", UnknownPackIds)
                   + ". Check the spelling, or the pack version.";

            return s;
        }
    }

    public static class TypeCatalogueResolver
    {
        /// <summary>
        /// Fold the adopted packs' family types into the baseline.
        ///
        /// ADDITIVE, and the PROJECT WINS. A project can adopt a pack and still
        /// override individual types by declaring them itself — project entries
        /// win by category+name, which is the same precedence every other
        /// override in this file already has.
        ///
        /// An adopted id matching no pack is reported, never ignored: a silent
        /// no-op here means a project believes it adopted a catalogue and did
        /// not, which is exactly the kind of confident absence this codebase
        /// keeps paying for.
        /// </summary>
        public static CatalogueAdoption Apply(ProjectBaseline baseline, TypeCatalogueLibrary library)
        {
            var a = new CatalogueAdoption();
            if (baseline == null) return a;

            var wanted = (baseline.AdoptCatalogues ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (wanted.Count == 0) return a;

            baseline.FamilyTypes = baseline.FamilyTypes ?? new List<BaselineFamilyType>();

            foreach (string id in wanted)
            {
                var pack = library?.ById(id);
                if (pack == null) { a.UnknownPackIds.Add(id); continue; }
                a.AdoptedPackIds.Add(pack.Id.Trim());

                foreach (var t in pack.FamilyTypes ?? new List<BaselineFamilyType>())
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.TypeName)
                                  || string.IsNullOrWhiteSpace(t.Category)) continue;

                    bool projectHasIt = baseline.FamilyTypes.Any(x => x != null
                        && string.Equals(x.Category?.Trim(), t.Category.Trim(), StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.TypeName?.Trim(), t.TypeName.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (projectHasIt)
                    {
                        a.OverriddenByProject.Add($"{t.Category.Trim()} / {t.TypeName.Trim()}");
                        continue;
                    }
                    baseline.FamilyTypes.Add(t);
                    a.TypesAdopted++;
                }
            }
            return a;
        }
    }
}
