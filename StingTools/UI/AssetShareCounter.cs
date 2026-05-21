using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.UI
{
    /// <summary>
    /// For each material in the supplied set, count how many OTHER
    /// materials share each of its three assets (Appearance / Physical /
    /// Thermal). The result drives the "Shared by N other materials"
    /// chip in the Assets sub-tab and lets the user spot orphan-prone
    /// assets before merging materials.
    ///
    /// Pure data — no transactions. O(N) over the supplied list.
    /// </summary>
    public struct AssetShareCount
    {
        public int Appearance;
        public int Physical;
        public int Thermal;
    }

    public static class AssetShareCounter
    {
        public static Dictionary<long, AssetShareCount> Compute(IReadOnlyList<Material> materials)
        {
            var result = new Dictionary<long, AssetShareCount>();
            if (materials == null || materials.Count == 0) return result;

            // Count usage of each asset id across the whole set.
            var appearance = new Dictionary<long, int>();
            var physical   = new Dictionary<long, int>();
            var thermal    = new Dictionary<long, int>();

            foreach (var m in materials)
            {
                try
                {
                    long a = SafeId(m.AppearanceAssetId);
                    long p = SafeId(m.StructuralAssetId);
                    long t = SafeId(m.ThermalAssetId);
                    if (a > 0) appearance[a] = appearance.TryGetValue(a, out int va) ? va + 1 : 1;
                    if (p > 0) physical[p]   = physical.TryGetValue(p, out int vp)   ? vp + 1 : 1;
                    if (t > 0) thermal[t]    = thermal.TryGetValue(t, out int vt)    ? vt + 1 : 1;
                }
                catch (Exception) { /* asset access can throw on linked elements; ignore */ }
            }

            // Per-material share count = (asset usage − 1) — the material's
            // own use is not "shared".
            foreach (var m in materials)
            {
                try
                {
                    long a = SafeId(m.AppearanceAssetId);
                    long p = SafeId(m.StructuralAssetId);
                    long t = SafeId(m.ThermalAssetId);
                    var share = new AssetShareCount
                    {
                        Appearance = a > 0 && appearance.TryGetValue(a, out int va) ? Math.Max(0, va - 1) : 0,
                        Physical   = p > 0 && physical.TryGetValue(p, out int vp)   ? Math.Max(0, vp - 1) : 0,
                        Thermal    = t > 0 && thermal.TryGetValue(t, out int vt)    ? Math.Max(0, vt - 1) : 0,
                    };
                    result[m.Id.Value] = share;
                }
                catch (Exception) { /* defensive — keep walking */ }
            }
            return result;
        }

        private static long SafeId(ElementId id) => id == null ? 0 : id.Value;
    }
}
