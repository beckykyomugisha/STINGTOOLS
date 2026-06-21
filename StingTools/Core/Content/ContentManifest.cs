// StingTools — Content Library (Phase: Content Library foundation)
//
// ContentManifest is the single source of truth for STING-authored reusable
// content: model-family seeds, 2D symbol catalogues, annotation tag families,
// plus the view-template / filter / shared-param / manufacturer-swap references
// that round out a "starter" content set. It is the index the three content
// engines (SymbolLibraryCreator, TagFamilyCreator seed-load, MEP-from-DWG
// placement) resolve against, so one version + checksum envelope covers content
// that is otherwise scattered across Data/Seeds, Data/Symbols and
// Data/TagFamilies/Seeds.
//
// ContentManifestRegistry mirrors DrawingTypeRegistry exactly:
//   1. Loads Data/STING_CONTENT_MANIFEST.json (shipped corporate baseline)
//   2. Layers project-scoped overrides from
//      <project>/_BIM_COORD/content_manifest.json, if present (merge-by-id;
//      project entries win, additive name lists union)
//   3. Falls back to a minimal in-code default when no JSON exists, so a
//      brand-new deployment still resolves the core seeds/tags
//   4. Computes a SHA-256 checksum for every "corporate" origin entry and flips
//      the origin flag to "project" on first edit, so the shipped baseline
//      cannot be silently mutated on disk
//
// The merged manifest is cached per-document; callers use Get(doc),
// ForCategory(doc, category, kind), TagFor(doc, category), ListMissingSpecs(doc)
// or Reload(doc).
//
// NOTE: project override is on-disk only for now. An Extensible-Storage surface
// (mirroring StingDrawingTypesSchema, survives "Save As") is the natural next
// step but is intentionally out of this slice.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Content
{
    // ── POCOs ────────────────────────────────────────────────────────────

    /// <summary>One reusable content item. A single flexible type serves every
    /// kind (modelFamily seed, symbolCatalogue, tagFamily) so the loader and
    /// resolver stay uniform; <see cref="Kind"/> + the populated fields say what
    /// it is and how to materialise it.</summary>
    public class ContentEntry
    {
        /// <summary>Stable id, e.g. "seed-mech-equipment", "tag-ducts", "sym-mep".</summary>
        [JsonProperty("id")] public string Id { get; set; }

        /// <summary>"modelFamily" | "symbolCatalogue" | "tagFamily".</summary>
        [JsonProperty("kind")] public string Kind { get; set; }

        /// <summary>Revit category this content serves (e.g. "Mechanical Equipment").
        /// Empty for catalogue entries that span categories.</summary>
        [JsonProperty("category")] public string Category { get; set; }

        /// <summary>Single-letter discipline hint (A/S/M/E/P/FP/LV/G/H/MG/RP/*) — metadata only.</summary>
        [JsonProperty("discipline")] public string Discipline { get; set; }

        /// <summary>Seed spec filename under Data/Seeds (modelFamily entries) →
        /// SymbolLibraryCreator.BuildOne. Null when the family is shipped pre-built.</summary>
        [JsonProperty("buildSpec")] public string BuildSpec { get; set; }

        /// <summary>Catalogue filename under Data/Symbols (symbolCatalogue entries) →
        /// MepSymbolEngine.</summary>
        [JsonProperty("catalogue")] public string Catalogue { get; set; }

        /// <summary>Symbol standard for catalogue entries (IEC / BS / CIBSE / IEEE /
        /// NFPA / ISO) — metadata only.</summary>
        [JsonProperty("standard")] public string Standard { get; set; }

        /// <summary>Produced / shipped .rfa filename (modelFamily + tagFamily entries) →
        /// on-disk load via the resolver root chain.</summary>
        [JsonProperty("familyFile")] public string FamilyFile { get; set; }

        /// <summary>Phase-185 selective type-catalog key (optional).</summary>
        [JsonProperty("typeCatalogKey")] public string TypeCatalogKey { get; set; }

        /// <summary>SHA-256 (hex) of the on-disk artefact (.rfa / catalogue) when it
        /// exists. Null for not-yet-built seeds. Advisory in this slice (no hard
        /// enforcement) — the resolver verifies .rfa integrity against it.</summary>
        [JsonProperty("checksum")] public string Checksum { get; set; }

        /// <summary>Baseline-lock hash over the entry's own JSON (NOT the artefact).
        /// Written by the registry; flips <see cref="Origin"/> to "project" when the
        /// shipped entry is edited on disk. Kept separate from <see cref="Checksum"/>
        /// so the two checksum semantics never collide.</summary>
        [JsonProperty("originChecksum")] public string OriginChecksum { get; set; }

        /// <summary>"built" (artefact on disk) | "spec" (buildable from buildSpec) |
        /// "needs-spec" (target category with a tag but no model-family seed yet —
        /// a tracked coverage gap, not an error).</summary>
        [JsonProperty("status")] public string Status { get; set; }

        /// <summary>True when the content must never be overwritten (mirrors a seed's
        /// protectExisting flag).</summary>
        [JsonProperty("protected")] public bool Protected { get; set; }

        /// <summary>"corporate" (checksum-locked baseline) | "project" (editable / drifted).</summary>
        [JsonProperty("origin")] public string Origin { get; set; }

        /// <summary>Free-text note (scope caveats, priority, etc.).</summary>
        [JsonProperty("notes")] public string Notes { get; set; }
    }

    /// <summary>Named grouping so a load/seed command can scope to a subset
    /// ("load just MEP", "load healthcare").</summary>
    public class ContentBundle
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("members")] public List<string> Members { get; set; } = new List<string>();
    }

    public class ContentManifest
    {
        [JsonProperty("libraryVersion")] public string LibraryVersion { get; set; } = "0.0.0";

        /// <summary>"projectFirst" (reproducibility — a frozen project ignores firm
        /// updates) | "sharedFirst" (legacy symbol-engine order). Consumed by
        /// ContentRoots, recorded here so the choice is explicit.</summary>
        [JsonProperty("rootPrecedence")] public string RootPrecedence { get; set; } = "projectFirst";

        [JsonProperty("symbols")] public List<ContentEntry> Symbols { get; set; } = new List<ContentEntry>();
        [JsonProperty("symbolCatalogues")] public List<ContentEntry> SymbolCatalogues { get; set; } = new List<ContentEntry>();
        [JsonProperty("tagFamilies")] public List<ContentEntry> TagFamilies { get; set; } = new List<ContentEntry>();

        /// <summary>View-template names — resolved through ManagedTemplateSyncer.</summary>
        [JsonProperty("viewTemplates")] public List<string> ViewTemplates { get; set; } = new List<string>();
        /// <summary>Filter names — resolved through AecFilterRegistry.</summary>
        [JsonProperty("filters")] public List<string> Filters { get; set; } = new List<string>();
        /// <summary>Shared-parameter file name (LoadSharedParams).</summary>
        [JsonProperty("sharedParams")] public string SharedParams { get; set; }
        /// <summary>Seed→manufacturer swap registry filename.</summary>
        [JsonProperty("swapMap")] public string SwapMap { get; set; }

        [JsonProperty("bundles")] public List<ContentBundle> Bundles { get; set; } = new List<ContentBundle>();

        /// <summary>Every entry across the three kinds (read-only convenience).</summary>
        [JsonIgnore]
        public IEnumerable<ContentEntry> AllEntries =>
            (Symbols ?? Enumerable.Empty<ContentEntry>())
            .Concat(SymbolCatalogues ?? Enumerable.Empty<ContentEntry>())
            .Concat(TagFamilies ?? Enumerable.Empty<ContentEntry>());
    }

    // ── Registry ─────────────────────────────────────────────────────────

    public static class ContentManifestRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, ContentManifest> _cache =
            new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase);

        // Public surface -------------------------------------------------

        /// <summary>Merged corporate+project manifest for the document (cached).</summary>
        public static ContentManifest Get(Document doc)
        {
            var key = DocKey(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;

                var corporate = LoadCorporate();
                var project = LoadProjectOverride(doc);
                var merged = Merge(corporate, project);
                ComputeChecksums(merged);
                _cache[key] = merged;
                return merged;
            }
        }

        /// <summary>Best content entry for a category + kind. Prefers status="built"
        /// over "spec" over "needs-spec"; null when nothing targets the category.</summary>
        public static ContentEntry ForCategory(Document doc, string category, string kind)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            var m = Get(doc);
            IEnumerable<ContentEntry> pool =
                string.Equals(kind, "tagFamily", StringComparison.OrdinalIgnoreCase) ? m.TagFamilies :
                string.Equals(kind, "symbolCatalogue", StringComparison.OrdinalIgnoreCase) ? m.SymbolCatalogues :
                m.Symbols;
            return (pool ?? Enumerable.Empty<ContentEntry>())
                .Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => StatusRank(e.Status))
                .FirstOrDefault();
        }

        /// <summary>Tag family entry for a category (convenience).</summary>
        public static ContentEntry TagFor(Document doc, string category)
            => ForCategory(doc, category, "tagFamily");

        /// <summary>Coverage ledger — every entry whose status starts with "needs-"
        /// ("needs-spec": a category with a tag but no model-family seed yet;
        /// "needs-build": a canonical tag family with no generated .rfa yet).
        /// Surfaced by a future Content_Coverage diagnostic so gaps are tracked,
        /// not silent.</summary>
        public static IReadOnlyList<ContentEntry> ListMissingSpecs(Document doc)
            => Get(doc).AllEntries
                .Where(e => !string.IsNullOrEmpty(e.Status)
                            && e.Status.StartsWith("needs-", StringComparison.OrdinalIgnoreCase))
                .ToList();

        /// <summary>Resolve a bundle's member entries (for scoped load/seed commands).</summary>
        public static IReadOnlyList<ContentEntry> Bundle(Document doc, string bundleId)
        {
            var m = Get(doc);
            var b = m.Bundles?.FirstOrDefault(x => string.Equals(x.Id, bundleId, StringComparison.OrdinalIgnoreCase));
            if (b == null) return new List<ContentEntry>();
            var ids = new HashSet<string>(b.Members ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            return m.AllEntries.Where(e => e.Id != null && ids.Contains(e.Id)).ToList();
        }

        public static void Reload(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_cache.ContainsKey(key)) _cache.Remove(key);
            }
        }

        // Loading --------------------------------------------------------

        private static ContentManifest LoadCorporate()
        {
            try
            {
                var path = StingToolsApp.FindDataFile("STING_CONTENT_MANIFEST.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var m = JsonConvert.DeserializeObject<ContentManifest>(json);
                    if (m != null)
                    {
                        foreach (var e in m.AllEntries)
                            if (string.IsNullOrEmpty(e.Origin)) e.Origin = "corporate";
                        return m;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ContentManifestRegistry: corporate JSON load failed — using minimal default. {ex.Message}");
            }
            return BuildDefault();
        }

        private static ContentManifest LoadProjectOverride(Document doc)
        {
            if (doc == null) return null;
            try
            {
                var projPath = doc.PathName;
                if (string.IsNullOrEmpty(projPath)) return null;
                var dir = Path.GetDirectoryName(projPath);
                if (string.IsNullOrEmpty(dir)) return null;
                var path = Path.Combine(dir, "_BIM_COORD", "content_manifest.json");
                if (!File.Exists(path)) return null;
                var m = JsonConvert.DeserializeObject<ContentManifest>(File.ReadAllText(path));
                if (m != null)
                    foreach (var e in m.AllEntries)
                        if (string.IsNullOrEmpty(e.Origin)) e.Origin = "project";
                return m;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ContentManifestRegistry: project override load failed — {ex.Message}");
                return null;
            }
        }

        private static ContentManifest Merge(ContentManifest baseM, ContentManifest over)
        {
            if (over == null) return baseM ?? new ContentManifest();

            var merged = new ContentManifest
            {
                // Project scalars win when set.
                LibraryVersion = !string.IsNullOrEmpty(over.LibraryVersion) && over.LibraryVersion != "0.0.0"
                    ? over.LibraryVersion : baseM?.LibraryVersion ?? "0.0.0",
                RootPrecedence = !string.IsNullOrEmpty(over.RootPrecedence) ? over.RootPrecedence
                    : baseM?.RootPrecedence ?? "projectFirst",
                SharedParams = !string.IsNullOrEmpty(over.SharedParams) ? over.SharedParams : baseM?.SharedParams,
                SwapMap = !string.IsNullOrEmpty(over.SwapMap) ? over.SwapMap : baseM?.SwapMap,
                Symbols = MergeById(baseM?.Symbols, over.Symbols),
                SymbolCatalogues = MergeById(baseM?.SymbolCatalogues, over.SymbolCatalogues),
                TagFamilies = MergeById(baseM?.TagFamilies, over.TagFamilies),
                // Additive name lists — project ADDS to corporate (distinct).
                ViewTemplates = UnionDistinct(baseM?.ViewTemplates, over.ViewTemplates),
                Filters = UnionDistinct(baseM?.Filters, over.Filters),
                Bundles = MergeBundles(baseM?.Bundles, over.Bundles),
            };
            return merged;
        }

        private static List<ContentEntry> MergeById(List<ContentEntry> baseList, List<ContentEntry> over)
        {
            var byId = new Dictionary<string, ContentEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in baseList ?? new List<ContentEntry>())
                if (!string.IsNullOrWhiteSpace(e.Id)) byId[e.Id] = e;
            foreach (var e in over ?? new List<ContentEntry>())
                if (!string.IsNullOrWhiteSpace(e.Id)) byId[e.Id] = e;   // project wins
            return byId.Values.ToList();
        }

        private static List<string> UnionDistinct(List<string> a, List<string> b)
        {
            var set = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in (a ?? new List<string>()).Concat(b ?? new List<string>()))
                if (!string.IsNullOrWhiteSpace(s) && seen.Add(s)) set.Add(s);
            return set;
        }

        private static List<ContentBundle> MergeBundles(List<ContentBundle> a, List<ContentBundle> b)
        {
            var byId = new Dictionary<string, ContentBundle>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in a ?? new List<ContentBundle>())
                if (!string.IsNullOrWhiteSpace(x.Id)) byId[x.Id] = x;
            foreach (var x in b ?? new List<ContentBundle>())
                if (!string.IsNullOrWhiteSpace(x.Id)) byId[x.Id] = x;   // project wins
            return byId.Values.ToList();
        }

        // Corporate-lock checksum (mirror of DrawingTypeRegistry) ---------

        private static void ComputeChecksums(ContentManifest m)
        {
            if (m == null) return;
            foreach (var e in m.AllEntries)
            {
                if (!string.Equals(e.Origin, "corporate", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    // Hash the entry's own JSON EXCLUDING both checksum fields, so the
                    // artefact checksum (.rfa bytes) and this baseline-lock hash are
                    // independent and neither perturbs the other.
                    var priorOrigin = e.OriginChecksum;
                    e.OriginChecksum = null;
                    var keepArtefact = e.Checksum;
                    e.Checksum = null;
                    var json = JsonConvert.SerializeObject(e, Formatting.None);
                    e.Checksum = keepArtefact;   // restore artefact hash untouched
                    using (var sha = SHA256.Create())
                    {
                        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                        var sb = new StringBuilder(hash.Length * 2);
                        foreach (var b in hash) sb.Append(b.ToString("x2"));
                        var actual = sb.ToString();
                        if (!string.IsNullOrEmpty(priorOrigin) && priorOrigin.Length == 64 && priorOrigin != actual)
                        {
                            StingLog.Warn(
                                $"ContentEntry '{e.Id}' drift: shipped manifest entry edited on disk; origin flipped to 'project'.");
                            e.Origin = "project";
                        }
                        e.OriginChecksum = actual;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ContentEntry checksum '{e.Id}': {ex.Message}");
                }
            }
        }

        // Minimal in-code default ---------------------------------------
        //
        // Last-resort only (JSON missing). The real comprehensive catalogue
        // ships in Data/STING_CONTENT_MANIFEST.json.

        private static ContentManifest BuildDefault()
        {
            return new ContentManifest
            {
                LibraryVersion = "0.0.0-fallback",
                RootPrecedence = "projectFirst",
                SharedParams = "MR_PARAMETERS.txt",
                SwapMap = "STING_FAMILY_SWAP_REGISTRY.json",
                Symbols = new List<ContentEntry>
                {
                    new ContentEntry { Id = "seed-mech-equipment", Kind = "modelFamily", Category = "Mechanical Equipment",
                        Discipline = "M", BuildSpec = "STING_SEED_MechanicalEquipment.json", Status = "spec", Origin = "corporate" },
                    new ContentEntry { Id = "seed-air-terminal", Kind = "modelFamily", Category = "Air Terminals",
                        Discipline = "M", BuildSpec = "STING_SEED_AirTerminal.json", Status = "spec", Origin = "corporate" },
                    new ContentEntry { Id = "seed-plumbing-fixture", Kind = "modelFamily", Category = "Plumbing Fixtures",
                        Discipline = "P", BuildSpec = "STING_SEED_PlumbingFixture.json", Status = "spec", Origin = "corporate" },
                    new ContentEntry { Id = "seed-elec-fixture", Kind = "modelFamily", Category = "Electrical Fixtures",
                        Discipline = "E", BuildSpec = "STING_SEED_ElectricalFixture.json", Status = "spec", Origin = "corporate" },
                },
            };
        }

        private static int StatusRank(string status)
            => string.Equals(status, "built", StringComparison.OrdinalIgnoreCase) ? 0
             : string.Equals(status, "spec", StringComparison.OrdinalIgnoreCase) ? 1
             : 2; // needs-spec / needs-build / unknown last

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }
    }
}
