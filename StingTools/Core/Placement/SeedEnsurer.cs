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
            var list = (rules ?? Enumerable.Empty<PlacementRule>()).Where(r => r != null).ToList();
            // A rule that names its own seed (PlacementRule.SeedId) is served by that
            // seed, not by whatever family the category happens to have loaded — so
            // it neither counts toward the category map nor is skipped because some
            // other family of its category is loaded.
            var cats = list
                .Where(r => string.IsNullOrWhiteSpace(r.SeedId))
                .Select(r => r.CategoryFilter ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c));
            var result = EnsureSeedsForCategories(doc, cats);

            var explicitSeeds = new HashSet<string>(list
                .Select(r => r.SeedId?.Trim())
                .Where(s => !string.IsNullOrEmpty(s)), StringComparer.OrdinalIgnoreCase);
            if (doc == null || explicitSeeds.Count == 0) return result;

            var loadedSeeds = LoadedSeedIds(doc);
            var missing = explicitSeeds.Where(s => !loadedSeeds.Contains(s)).ToList();
            if (missing.Count > 0) BuildSeeds(doc, missing, result);
            return result;
        }

        /// <summary>
        /// DTW-113 — seed-required mode. <see cref="EnsureSeedsForCategories(Document, IEnumerable{string})"/>
        /// treats a category as served when ANY family of it is loaded, which is right for
        /// the rule engine (it places whatever family the category has) but wrong for a
        /// caller that places the SEED itself — the DWG fixture bridge resolves only the
        /// seed family, so a project with one manufacturer basin loaded skipped every
        /// plumbing fixture as "seed not built". With <paramref name="requireSeedFamily"/>
        /// true the check is on the mapped seed family (STING_SEED_FAMILY_TXT marker or
        /// family name), and a missing seed is built even when the category has other
        /// families loaded.
        /// </summary>
        public static SeedEnsureResult EnsureSeedsForCategories(Document doc, IEnumerable<string> categories, bool requireSeedFamily)
        {
            if (!requireSeedFamily) return EnsureSeedsForCategories(doc, categories);
            var result = new SeedEnsureResult();
            if (doc == null || categories == null) return result;

            var distinct = new HashSet<string>(categories.Where(c => !string.IsNullOrWhiteSpace(c)),
                StringComparer.OrdinalIgnoreCase);
            if (distinct.Count == 0) return result;

            var loadedSeeds = LoadedSeedIds(doc);
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in distinct)
            {
                string seedId = CategoryToSeedRegistry.Resolve(doc, cat);
                if (string.IsNullOrWhiteSpace(seedId)) { result.CategoriesSeedless++; continue; }
                seedId = seedId.Trim();
                if (loadedSeeds.Contains(seedId)) { result.CategoriesAlreadyServed++; continue; }
                missing.Add(seedId);
            }
            if (missing.Count > 0) BuildSeeds(doc, missing, result);
            return result;
        }

        /// <summary>Seed ids whose family is loaded: the STING_SEED_FAMILY_TXT marker any
        /// loaded type carries, plus every loaded family name (a seed family is named after
        /// its seed id — the fallback for a seed built before the marker existed).</summary>
        private static HashSet<string> LoadedSeedIds(Document doc)
        {
            var set = LoadedFamilyNames(doc);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                {
                    string marker = null;
                    try { marker = el.LookupParameter("STING_SEED_FAMILY_TXT")?.AsString(); }
                    catch (Exception ex) { StingLog.Warn($"SeedEnsurer.LoadedSeedIds {el.Id}: {ex.Message}"); }
                    if (!string.IsNullOrWhiteSpace(marker)) set.Add(marker.Trim());
                }
            }
            catch (Exception ex) { StingLog.Warn($"SeedEnsurer.LoadedSeedIds: {ex.Message}"); }
            return set;
        }

        /// <summary>Build + load each seed from its Data/Seeds JSON spec (missing-only).</summary>
        private static void BuildSeeds(Document doc, IEnumerable<string> seedIds, SeedEnsureResult result)
        {
            string outRoot = ResolveSeedOutputFolder(doc);
            try { Directory.CreateDirectory(outRoot); } catch (Exception ex) { StingLog.Warn($"SeedEnsurer mkdir: {ex.Message}"); }
            foreach (var seedId in seedIds)
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
                        result.Messages.Add($"Seed '{seedId}' — build FAILED ({r.Failed}); rule(s) naming it will skip (no symbol).");
                    foreach (var w in r.Warnings.Take(3)) StingLog.Info($"SeedEnsurer[{seedId}]: {w}");
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"Seed '{seedId}' — error: {ex.Message}");
                    StingLog.Warn($"SeedEnsurer build '{seedId}': {ex.Message}");
                }
            }
        }

        /// <summary>Names of the loaded families (a seed family is named after its seed id).</summary>
        private static HashSet<string> LoadedFamilyNames(Document doc)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(Family)))
                    if (el is Family f && !string.IsNullOrEmpty(f.Name)) set.Add(f.Name);
            }
            catch (Exception ex) { StingLog.Warn($"SeedEnsurer.LoadedFamilyNames: {ex.Message}"); }
            return set;
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
                .Where(r => r != null && string.IsNullOrWhiteSpace(r.SeedId))
                .Select(r => r.CategoryFilter ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c));
            var distinct = new HashSet<string>(cats, StringComparer.OrdinalIgnoreCase);
            bool anyExplicit = (rules ?? Enumerable.Empty<PlacementRule>()).Any(r => !string.IsNullOrWhiteSpace(r?.SeedId));
            if (distinct.Count == 0 && !anyExplicit) return result;

            // Distinct mapped seed ids.
            var seeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in distinct)
            {
                string sid = CategoryToSeedRegistry.Resolve(doc, cat);
                if (!string.IsNullOrWhiteSpace(sid)) seeds.Add(sid);
                else result.CategoriesSeedless++;
            }
            // Seeds a rule names directly (PlacementRule.SeedId) rebuild too.
            foreach (var r in rules ?? Enumerable.Empty<PlacementRule>())
                if (!string.IsNullOrWhiteSpace(r?.SeedId)) seeds.Add(r.SeedId.Trim());
            if (seeds.Count == 0) return result;

            string outRoot = ResolveSeedOutputFolder(doc);
            try { Directory.CreateDirectory(outRoot); } catch (Exception ex) { StingLog.Warn($"SeedEnsurer.RebuildAll mkdir: {ex.Message}"); }
            result.Messages.Add($"Attempting {seeds.Count} seed(s) from {distinct.Count} categor(ies) → {outRoot}");

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
                    // Force a clean rebuild: delete the cached .rfa AND the
                    // .sting-finalized sidecar so CreateAllFromFile regenerates
                    // instead of skipping it as protected/existing.
                    try
                    {
                        string rfa = Path.Combine(outRoot, seedId + ".rfa");
                        if (File.Exists(rfa)) File.Delete(rfa);
                        string fin = Path.Combine(outRoot, seedId + ".sting-finalized");
                        if (File.Exists(fin)) File.Delete(fin);
                    }
                    catch (Exception dex) { StingLog.Warn($"SeedEnsurer.RebuildAll delete '{seedId}': {dex.Message}"); }

                    var r = SymbolLibraryCreator.CreateAllFromFile(doc, spec, outRoot, loadIntoProject: true);
                    int touched = r.Created + r.Existed;
                    result.SeedsBuiltOrLoaded += touched;
                    // Always report the full breakdown so a "0" is self-diagnosing
                    // (built / loaded / failed / protected + first error/warning).
                    string detail = $"'{seedId}': built {r.Created}, loaded {r.Existed}, failed {r.Failed}, protected {r.Protected}";
                    if (r.Errors != null && r.Errors.Count > 0) detail += " · ERR: " + r.Errors[0];
                    else if (touched == 0 && r.Warnings != null && r.Warnings.Count > 0) detail += " · WARN: " + r.Warnings[0];
                    result.Messages.Add(detail);
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
                    if (el is FamilySymbol fs && fs.Category != null && !string.IsNullOrEmpty(global::StingTools.Core.ParameterHelpers.GetCategoryName(fs)))
                        set.Add(global::StingTools.Core.ParameterHelpers.GetCategoryName(fs));
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
            try { if (!string.IsNullOrEmpty(doc?.PathName)) baseDir = global::StingTools.Core.StingPaths.ModelDir(doc); }
            catch (Exception ex) { StingLog.Warn($"SeedEnsurer.ResolveSeedOutputFolder: {ex.Message}"); }
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Path.GetTempPath(), "STING_Seeds");
            return StingPaths.MetaFile(doc, "_BIM_COORD", "Families", "Seeds");
        }
    }
}
