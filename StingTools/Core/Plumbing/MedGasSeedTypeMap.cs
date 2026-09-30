// StingTools — which gas each type of the STING medical-gas seed declares (MG-1).
// Revit-free; unit-tested by StingTools.Tags.Tests against the shipped seed.
//
// MGS_GAS_TYPE_TXT is an instance parameter the seed sets per type as a default, so
// swapping a placed outlet from TERMINAL_UNIT_O2 to TERMINAL_UNIT_VAC keeps "O2", and a
// FamilySymbol cannot read the value at all. The seed spec is the one place the
// type -> gas pairing is written down; this reads it from there rather than restating it.
// MedGasOutletPlacementCommand uses it to find the seed type for a gas. Keeping the gas
// with the type after a swap is SeedTypeSwapUpdater's job (MGS_GAS_TYPE_TXT is a
// "followsType" parameter), under the same rule as every other seed value.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Plumbing
{
    public sealed class MedGasSeedTypeMap
    {
        public const string SeedFileName = "STING_SEED_MedGasOutlet.json";
        private const string GasParam = "MGS_GAS_TYPE_TXT";

        // Declared type name -> canonical gas code, or null for a type that declares none.
        private readonly Dictionary<string, string> _gasByType =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        public static readonly MedGasSeedTypeMap Empty = new MedGasSeedTypeMap();

        public bool IsEmpty => _gasByType.Count == 0;
        public IReadOnlyList<string> TypeNames => _order;

        /// <summary>The map from the seed spec at <paramref name="path"/>; empty (and logged) when unreadable.</summary>
        public static MedGasSeedTypeMap Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StingLog.Warn($"MedGasSeedTypeMap: seed spec not found ({path ?? "no path"}) — gas outlet placement will not find seed types by gas.");
                return new MedGasSeedTypeMap();
            }
            try { return Parse(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                StingLog.Warn($"MedGasSeedTypeMap: '{path}' unreadable — {ex.Message}");
                return new MedGasSeedTypeMap();
            }
        }

        public static MedGasSeedTypeMap Parse(string json)
        {
            var map = new MedGasSeedTypeMap();
            var root = JObject.Parse(json);
            var symbols = root["symbols"] as JArray;
            if (symbols == null) return map;
            var sym = symbols.OfType<JObject>().FirstOrDefault(s =>
                          string.Equals((string)s["id"], MedicalGasFixtures.SeedFamilyId, StringComparison.OrdinalIgnoreCase))
                      ?? symbols.OfType<JObject>().FirstOrDefault();
            foreach (var v in (sym?["typeVariants"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string name = ((string)v["name"] ?? "").Trim();
                if (name.Length == 0 || map._gasByType.ContainsKey(name)) continue;   // "_group" separators
                string declared = (string)(v["params"]?[GasParam]);
                string gas = MedicalGasFixtures.CanonicalGasCode(declared);
                if (!string.IsNullOrWhiteSpace(declared) && gas == null)
                    StingLog.Warn($"MedGasSeedTypeMap: {name} declares unknown gas '{declared}' — treated as none.");
                map._gasByType[name] = gas;
                map._order.Add(name);
            }
            return map;
        }

        public bool IsDeclaredType(string typeName)
            => typeName != null && _gasByType.ContainsKey(typeName);

        /// <summary>The canonical gas the type declares, or null (no gas, or not a seed type).</summary>
        public string GasForType(string typeName)
            => typeName != null && _gasByType.TryGetValue(typeName, out var g) ? g : null;

        /// <summary>The seed types that declare <paramref name="gas"/> (aliases accepted), in spec order.</summary>
        public IReadOnlyList<string> TypesForGas(string gas)
        {
            string canon = MedicalGasFixtures.CanonicalGasCode(gas);
            if (canon == null) return Array.Empty<string>();
            return _order.Where(n => _gasByType[n] == canon).ToList();
        }
    }
}
