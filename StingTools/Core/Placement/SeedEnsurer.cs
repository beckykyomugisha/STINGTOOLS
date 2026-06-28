// StingTools — SeedEnsurer (Item 1, seed-family-per-rule).
//
// The EnsureSeeds pre-pass. For each placement category that has NO
// manufacturer family loaded, resolve the mapped STING seed family
// (CategoryToSeedRegistry) and build+load it so a run never silently
// skips a ticked category for "no family loaded".
//
// IMPORTANT — call this OUTSIDE any open Revit transaction. It delegates
// to SymbolLibraryCreator.CreateAllFromFile, which creates new family
// documents and calls Document.LoadFamily (each opens its own implicit
// transaction). Running it before the engine opens its placement
// transaction keeps the hot placement loop fast (it only resolves
// already-loaded symbols) and avoids nested-transaction surprises.
//
// Model-modifying — verify in Revit before merge.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Symbols;

namespace StingTools.Core.Placement
{
    public static class SeedEnsurer
    {
        /// <summary>Result of an EnsureSeeds pre-pass.</summary>
        public class SeedEnsureResult
        {
            public int SeedsBuiltOrLoaded { get; set; }
            public int CategoriesAlreadyServed { get; set; }
            public int CategoriesSeedless { get; set; }
            public List<string> Messages { get; } = new List<string>();
        }

        /// <summary>Ensure seeds for every distinct CategoryFilter in the supplied rules.</summary>
        public static SeedEnsureResult EnsureSeedsForRules(Document doc, IEnumerable<PlacementRule> rules)
        {
            var cats = (rules ?? Enumerable.Empty<PlacementRule>())
                .Select(r => r?.CategoryFilter ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c));
            return EnsureSeedsForCategories(doc, cats);
        }

        /// <summary>
        /// For each distinct category with no loaded FamilySymbol, resolve the
        /// mapped seed and build/load it into the project. Idempotent: an
        /// existing .rfa is loaded (not rebuilt); an already-served category is
        /// skipped. Never throws — a per-seed failure is logged and the next
        /// seed is attempted.
        /// </summary>
        public static SeedEnsureResult EnsureSeedsForCategories(Document doc, IEnumerable<string> categories)
        {
            var result = new SeedEnsureResult();
            if (doc == null || categories == null) return result;

            var distinct = new HashSet<string>(categories.Where(c => !string.IsNullOrWhiteSpace(c)),
                StringComparer.OrdinalIgnoreCase);
            if (distinct.Count == 0) return result;

            // Index categories that already have a loaded FamilySymbol so we
            // never build a seed where a real family exists.
            var servedCats = LoadedCategoryNames(doc);

            // Dedupe seed ids: many categories can map to one seed file, and a
            // seed file can map to many — build each spec at most once.
            var seedToSpec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in distinct)
            {
                if (servedCats.Contains(cat)) { result.CategoriesAlreadyServed++; continue; }
                string seedId = CategoryToSeedRegistry.Resolve(doc, cat);
                if (string.IsNullOrWhiteSpace(seedId)) { result.CategoriesSeedless++; continue; }
                if (seedToSpec.ContainsKey(seedId)) continue;
                seedToSpec[seedId] = null; // resolve spec path lazily below
            }
            if (seedToSpec.Count == 0) return result;

            string outRoot = ResolveSeedOutputFolder(doc);
            try { Directory.CreateDirectory(outRoot); } catch (Exception ex) { StingLog.Warn($"SeedEnsurer mkdir: {ex.Message}"); }

            foreach (var seedId in seedToSpec.Keys.ToList())
            {
                try
                {
                    string spec = StingToolsApp.FindDataFile(seedId + ".json");
                    if (string.IsNullOrEmpty(spec) || !File.Exists(spec))
                    {
                        result.Messages.Add($"Seed '{seedId}' — spec Data/Seeds/{seedId}.json not found; cannot build.");
                        StingLog.Warn($"SeedEnsurer: spec not found for seed '{seedId}'.");
                        continue;
                    }

                    var r = SymbolLibraryCreator.CreateAllFromFile(doc, spec, outRoot, loadIntoProject: true);
                    int touched = r.Created + r.Existed;
                    if (touched > 0)
                    {
                        result.SeedsBuiltOrLoaded += touched;
                        result.Messages.Add($"Seed '{seedId}' — {r.Created} built, {r.Existed} loaded into project.");
                    }
                    else if (r.Failed > 0)
                    {
                        result.Messages.Add($"Seed '{seedId}' — build FAILED ({r.Failed}); rule(s) will skip (no symbol).");
                    }
                    foreach (var w in r.Warnings.Take(3)) StingLog.Info($"SeedEnsurer[{seedId}]: {w}");
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"Seed '{seedId}' — error: {ex.Message}");
                    StingLog.Warn($"SeedEnsurer build '{seedId}': {ex.Message}");
                }
            }
            return result;
        }

        /// <summary>
        /// FORCE-rebuild: regenerate the mapped seed family for every distinct
        /// rule category from JSON (latest geometry + variants) and reload it into
        /// the project — overwriting cached .rfa and the loaded family — so placed
        /// instances pick up the new definitions. Unlike EnsureSeeds (missing-only)
        /// this rebuilds even when the family is already loaded. Call OUTSIDE any
        /// open transaction.
        /// </summary>
        public static SeedEnsureResult RebuildAllForRules(Document doc, IEnumerable<PlacementRule> rules)
        {
            var result = new SeedEnsureResult();
            if (doc == null) return result;
            var cats = (rules ?? Enumerable.Empty<PlacementRule>())
                .Select(r => r?.CategoryFilter ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c));
            var distinct = new HashSet<string>(cats, StringComparer.OrdinalIgnoreCase);
            if (distinct.Count == 0) return result;

            // Distinct mapped seed ids.
            var seeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in distinct)
            {
                string sid = CategoryToSeedRegistry.Resolve(doc, cat);
                if (!string.IsNullOrWhiteSpace(sid)) seeds.Add(sid);
                else result.CategoriesSeedless++;
            }
            if (seeds.Count == 0) return result;

            string outRoot = ResolveSeedOutputFolder(doc);
            try { Directory.CreateDirectory(outRoot); } catch (Exception ex) { StingLog.Warn($"SeedEnsurer.RebuildAll mkdir: {ex.Message}"); }

            foreach (var seedId in seeds)
            {
                try
                {
                    string spec = StingToolsApp.FindDataFile(seedId + ".json");
                    if (string.IsNullOrEmpty(spec) || !File.Exists(spec))
                    {
                        result.Messages.Add($"Seed '{seedId}' — spec Data/Seeds/{seedId}.json not found; cannot rebuild.");
                        continue;
                    }
                    // Delete the cached .rfa so CreateAllFromFile regenerates it
                    // (it skips a build when the .rfa already exists).
                    try
                    {
                        string rfa = Path.Combine(outRoot, seedId + ".rfa");
                        if (File.Exists(rfa)) File.Delete(rfa);
                    }
                    catch (Exception dex) { StingLog.Warn($"SeedEnsurer.RebuildAll delete '{seedId}.rfa': {dex.Message}"); }

                    var r = SymbolLibraryCreator.CreateAllFromFile(doc, spec, outRoot, loadIntoProject: true);
                    int touched = r.Created + r.Existed;
                    result.SeedsBuiltOrLoaded += touched;
                    if (touched > 0)
                        result.Messages.Add($"Seed '{seedId}' — {r.Created} rebuilt, {r.Existed} reloaded into project.");
                    else if (r.Failed > 0)
                        result.Messages.Add($"Seed '{seedId}' — rebuild FAILED ({r.Failed}).");
                    foreach (var w in r.Warnings.Take(2)) StingLog.Info($"SeedEnsurer.RebuildAll[{seedId}]: {w}");
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"Seed '{seedId}' — error: {ex.Message}");
                    StingLog.Warn($"SeedEnsurer.RebuildAll '{seedId}': {ex.Message}");
                }
            }
            return result;
        }

        /// <summary>Set of Category.Name values that have at least one loaded FamilySymbol.</summary>
        private static HashSet<string> LoadedCategoryNames(Document doc)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                {
                    if (el is FamilySymbol fs && fs.Category != null && !string.IsNullOrEmpty(fs.Category.Name))
                        set.Add(fs.Category.Name);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SeedEnsurer.LoadedCategoryNames: {ex.Message}"); }
            return set;
        }

        /// <summary>
        /// &lt;project&gt;/_BIM_COORD/Families/Seeds/ — mirrors
        /// BuildSeedFamiliesCommand.ResolveSeedOutputFolder so the seed .rfa
        /// files (and their STING_SEED_FAMILY_TXT stamp) land in one place
        /// regardless of which surface built them.
        /// </summary>
        public static string ResolveSeedOutputFolder(Document doc)
        {
            string baseDir = null;
            try { if (!string.IsNullOrEmpty(doc?.PathName)) baseDir = Path.GetDirectoryName(doc.PathName); }
            catch (Exception ex) { StingLog.Warn($"SeedEnsurer.ResolveSeedOutputFolder: {ex.Message}"); }
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Path.GetTempPath(), "STING_Seeds");
            return Path.Combine(baseDir, "_BIM_COORD", "Families", "Seeds");
        }
    }
}
