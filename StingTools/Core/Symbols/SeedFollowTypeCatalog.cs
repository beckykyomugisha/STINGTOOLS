// StingTools — which instance values each seed type declares ("followsType"), read
// once from Data/Seeds/*.json. Revit-free; unit-tested by StingTools.Tags.Tests.
//
// SeedTypeSwapUpdater (a placed element changes type) and SeedTypeMigrator (Build
// Seeds renames or merges a type) both ask it: for this family and this new type,
// which followsType parameters change, and to what? The answer is always
// SeedFollowTypeRule's, so there is one rule for both paths.
//
// Scope: STING's own seed families only — a family whose name is a seed id (Revit
// appends digits when a same-named family is loaded twice). A manufacturer family is
// never touched, and neither is a type the seed does not declare (a user's duplicate).
//
// A type's value of a parameter is the variant's declared value, else the parameter's
// "default" — SymbolLibraryCreator duplicates every variant from the default type, so
// that is what the built family holds.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Symbols
{
    public sealed class SeedFollowTypeCatalog
    {
        private sealed class Seed
        {
            public string Id;
            public readonly List<string> Params = new List<string>();
            // type name -> param -> effective value ("" = nothing)
            public readonly Dictionary<string, Dictionary<string, string>> ByType =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            // old (renamedFrom) type name -> param -> value it declared
            public readonly Dictionary<string, Dictionary<string, string>> ByOldType =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            // param -> every value the seed puts there (any type, any old type, the default)
            public readonly Dictionary<string, HashSet<string>> SeedValues =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        }

        private readonly Dictionary<string, Seed> _byId = new Dictionary<string, Seed>(StringComparer.OrdinalIgnoreCase);

        public static readonly SeedFollowTypeCatalog Empty = new SeedFollowTypeCatalog();

        public bool IsEmpty => _byId.Count == 0;

        /// <summary>Seed ids that carry at least one followsType parameter.</summary>
        public IEnumerable<string> SeedIds => _byId.Keys;

        /// <summary>Every STING_SEED_*.json in <paramref name="seedsDir"/>; an unreadable file is logged and skipped.</summary>
        public static SeedFollowTypeCatalog LoadDirectory(string seedsDir)
        {
            if (string.IsNullOrEmpty(seedsDir) || !Directory.Exists(seedsDir))
            {
                StingLog.Warn($"SeedFollowTypeCatalog: seed folder not found ({seedsDir ?? "no path"}) — type changes will not restamp seed values.");
                return new SeedFollowTypeCatalog();
            }
            var libs = new List<SymbolLibrary>();
            foreach (var file in Directory.GetFiles(seedsDir, "STING_SEED_*.json"))
            {
                try { libs.Add(JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(file))); }
                catch (Exception ex) { StingLog.Warn($"SeedFollowTypeCatalog: '{Path.GetFileName(file)}' unreadable — {ex.Message}"); }
            }
            return FromLibraries(libs);
        }

        public static SeedFollowTypeCatalog FromLibraries(IEnumerable<SymbolLibrary> libraries)
        {
            var cat = new SeedFollowTypeCatalog();
            foreach (var lib in libraries ?? Enumerable.Empty<SymbolLibrary>())
                foreach (var def in lib?.Symbols ?? new List<SymbolDefinition>())
                    cat.Add(def);
            return cat;
        }

        private void Add(SymbolDefinition def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Id) || _byId.ContainsKey(def.Id)) return;
            var follow = (def.Parameters ?? new List<ParameterDefinition>())
                .Where(p => p != null && p.IsInstance && p.FollowsType == true && !string.IsNullOrWhiteSpace(p.Name))
                .ToList();
            if (follow.Count == 0) return;

            var seed = new Seed { Id = def.Id };
            foreach (var p in follow)
            {
                seed.Params.Add(p.Name);
                seed.SeedValues[p.Name] = new HashSet<string>(StringComparer.Ordinal);
                AddSeedValue(seed, p.Name, p.Default);
            }

            foreach (var v in def.TypeVariants ?? new List<TypeVariantDefinition>())
            {
                if (v == null || string.IsNullOrWhiteSpace(v.Name) || seed.ByType.ContainsKey(v.Name)) continue;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in follow)
                {
                    string val = v.Parameters != null && v.Parameters.TryGetValue(p.Name, out var x) ? x : p.Default;
                    values[p.Name] = (val ?? "").Trim();
                    AddSeedValue(seed, p.Name, val);
                }
                seed.ByType[v.Name] = values;

                foreach (var old in v.RenamedFrom ?? new List<RenamedFromEntry>())
                {
                    if (string.IsNullOrWhiteSpace(old?.Name) || old.Parameters == null) continue;
                    var oldValues = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in old.Parameters)
                    {
                        if (!seed.SeedValues.ContainsKey(kv.Key)) continue;
                        oldValues[kv.Key] = (kv.Value ?? "").Trim();
                        AddSeedValue(seed, kv.Key, kv.Value);
                    }
                    seed.ByOldType[old.Name.Trim()] = oldValues;
                }
            }
            _byId[def.Id] = seed;
        }

        private static void AddSeedValue(Seed seed, string param, string value)
        {
            string v = (value ?? "").Trim();
            if (v.Length > 0) seed.SeedValues[param].Add(v);
        }

        /// <summary>
        /// The seed id of a STING seed family (a seed id, or a seed id plus the digits Revit
        /// appends to a second load); null for any other family, or a seed with nothing to follow.
        /// </summary>
        public string SeedIdForFamily(string familyName)
        {
            string n = (familyName ?? "").Trim();
            if (n.Length == 0) return null;
            if (_byId.ContainsKey(n)) return _byId[n].Id;
            string core = n.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return core.Length < n.Length && _byId.TryGetValue(core, out var s) ? s.Id : null;
        }

        public IReadOnlyList<string> FollowsTypeParams(string seedId)
            => seedId != null && _byId.TryGetValue(seedId, out var s) ? s.Params : (IReadOnlyList<string>)Array.Empty<string>();

        public bool IsDeclaredType(string seedId, string typeName)
            => seedId != null && typeName != null && _byId.TryGetValue(seedId, out var s) && s.ByType.ContainsKey(typeName);

        /// <summary>The value <paramref name="typeName"/> declares for <paramref name="param"/> ("" = nothing); null when unknown.</summary>
        public string DeclaredValue(string seedId, string typeName, string param)
        {
            if (seedId == null || typeName == null || param == null || !_byId.TryGetValue(seedId, out var s)) return null;
            if (s.ByType.TryGetValue(typeName, out var t) && t.TryGetValue(param, out var v)) return v;
            if (s.ByOldType.TryGetValue(typeName, out var o) && o.TryGetValue(param, out var ov)) return ov;
            return null;
        }

        /// <summary>Every value the seed itself puts into <paramref name="param"/>.</summary>
        public IReadOnlyCollection<string> SeedValues(string seedId, string param)
            => seedId != null && param != null && _byId.TryGetValue(seedId, out var s) && s.SeedValues.TryGetValue(param, out var set)
                ? set : (IReadOnlyCollection<string>)Array.Empty<string>();

        /// <summary>
        /// The writes an element of family <paramref name="familyName"/> needs after moving to
        /// <paramref name="newTypeName"/>. <paramref name="readCurrent"/> returns the instance's
        /// current value of a parameter, or null when the element does not carry it (skipped).
        /// <paramref name="oldTypeName"/> is the type it came from when known (a migration);
        /// unknown (a swap seen by an updater), any value the seed declares counts as untouched.
        /// Empty for a non-seed family and for a type the seed does not declare.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> AfterTypeChange(
            string familyName, string newTypeName, Func<string, string> readCurrent, string oldTypeName = null)
        {
            var writes = new List<KeyValuePair<string, string>>();
            string id = SeedIdForFamily(familyName);
            if (id == null || !IsDeclaredType(id, newTypeName) || readCurrent == null) return writes;

            foreach (var param in FollowsTypeParams(id))
            {
                string current = readCurrent(param);
                if (current == null) continue;                        // the element does not carry it
                string oldValue = oldTypeName != null ? DeclaredValue(id, oldTypeName, param) : null;
                IEnumerable<string> untouched = oldValue != null ? new[] { oldValue } : SeedValues(id, param);
                if (SeedFollowTypeRule.Decide(current, untouched, DeclaredValue(id, newTypeName, param), out string write))
                    writes.Add(new KeyValuePair<string, string>(param, write));
            }
            return writes;
        }
    }
}
