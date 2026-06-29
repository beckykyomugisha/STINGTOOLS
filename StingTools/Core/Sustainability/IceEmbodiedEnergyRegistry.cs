// StingTools — Embodied-energy (MJ/kg) registry (WS C3).
//
// Supplies the per-kg embodied-energy factor the materials carbon path needs so
// the embodied-energy (MJ) figure comes from a real cradle-to-gate dataset
// rather than the flat carbonKg×12 ratio fallback. Factors are KEYED ON A
// MATERIAL-NAME KEYWORD (substring, longest-match-wins) so a model's free-text
// material names resolve without a per-project mapping table.
//
// Two tracks are never conflated: this registry is the embodied-ENERGY track
// (MJ); embodied CARBON (kgCO₂e) stays with CarbonFactorResolver. A material may
// carry its own per-m³ EPD PERT+PENRT energy (preferred, read off the material
// param); when it doesn't, SustainMaterialCarbon uses THIS per-kg factor — but
// only when the project's FactorSources.EmbodiedEnergy order permits a mass-based
// DB (so B1 still governs the dataset preference).
//
// DATA, not code: the corporate baseline is a documented SEED (ICE v3 published
// figures, with provenance per row) under Data/STING_ICE_EMBODIED_ENERGY.json;
// projects override/extend it at
// <project>/_BIM_COORD/sustainability/ice_embodied_energy.json. No number is
// hardcoded in this file — an empty/absent dataset simply yields 0 (ratio
// fallback), never an invented value.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Sustainability
{
    /// <summary>One embodied-energy row: a material-name keyword + its MJ/kg
    /// cradle-to-gate (A1–A3) energy and a provenance string.</summary>
    public class IceEnergyFactor
    {
        public string Match    { get; set; } = "";
        public double MjPerKg  { get; set; }
        public string Source   { get; set; } = "";
    }

    public class IceEmbodiedEnergyRegistry
    {
        // Stored longest-keyword-first so the most specific match wins
        // (e.g. "reinforced concrete" beats "concrete").
        private readonly List<IceEnergyFactor> _factors = new List<IceEnergyFactor>();

        public IReadOnlyList<IceEnergyFactor> All => _factors;

        /// <summary>Resolve the MJ/kg for a material name (case-insensitive
        /// substring, longest keyword wins). Returns 0 when nothing matches — the
        /// caller then keeps whatever fallback it documents (never an invented
        /// number here).</summary>
        public double GetMjPerKg(string materialName) => Match(materialName)?.MjPerKg ?? 0;

        /// <summary>Resolve the full row (factor + provenance) or null.</summary>
        public IceEnergyFactor Match(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return null;
            string lc = materialName.ToLowerInvariant();
            // _factors is kept sorted longest-keyword-first → first hit is the
            // most specific.
            return _factors.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.Match) && lc.Contains(f.Match.ToLowerInvariant()));
        }

        public static IceEmbodiedEnergyRegistry LoadFromJson(string corporateJson, string projectJson = null)
        {
            var reg = new IceEmbodiedEnergyRegistry();
            if (!string.IsNullOrWhiteSpace(corporateJson)) reg.Apply(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))   reg.Apply(projectJson);
            reg.Sort();
            return reg;
        }

        public static IceEmbodiedEnergyRegistry LoadFromFiles(string corporatePath, string projectPath)
            => LoadFromJson(SafeRead(corporatePath), SafeRead(projectPath));

        private static string SafeRead(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private void Apply(string json)
        {
            JObject root;
            try { root = JObject.Parse(json); } catch { return; }
            var arr = root["materials"] as JArray;
            if (arr == null) return;
            foreach (var m in arr.OfType<JObject>())
            {
                string match = (string)m["match"];
                if (string.IsNullOrWhiteSpace(match)) continue;
                double mj = (double?)m["mjPerKg"] ?? 0;
                var row = new IceEnergyFactor
                {
                    Match = match.Trim(),
                    MjPerKg = mj,
                    Source = (string)m["source"] ?? ""
                };
                // Project override replaces a corporate row by identical keyword.
                int existing = _factors.FindIndex(f =>
                    string.Equals(f.Match, row.Match, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) _factors[existing] = row;
                else _factors.Add(row);
            }
        }

        private void Sort()
            => _factors.Sort((a, b) => (b.Match?.Length ?? 0).CompareTo(a.Match?.Length ?? 0));
    }
}
