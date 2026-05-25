using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using StingTools.Core;
using StingTools.UI;   // MaterialAuditLogger

namespace StingTools.Core.Materials
{
    /// <summary>
    /// Applies a <see cref="TexturePackManifest"/> to a Revit
    /// <see cref="Material"/>'s appearance asset. Prefers the Prism
    /// (Autodesk Standard Surface) schema and falls back to Generic when
    /// the material's asset doesn't expose Prism properties.
    /// </summary>
    /// <remarks>
    /// All writes happen inside a single <see cref="AppearanceAssetEditScope"/>
    /// per material, which itself must run inside a Revit Transaction.
    /// Callers wrap each call in a Transaction (singletons) or share one
    /// Transaction across many calls (bulk apply).
    /// </remarks>
    public static class PbrTextureApplier
    {
        // ── Prism (Advanced) property names ──────────────────────────────
        // See Autodesk Standard Surface schema. Names are stable across
        // Revit 2025/2026/2027.
        private const string PrismBaseColor       = "advanced_base_color";
        private const string PrismRoughness       = "advanced_roughness";
        private const string PrismMetalness       = "advanced_metalness";
        private const string PrismNormal          = "advanced_normal";
        private const string PrismBump            = "advanced_bump";
        private const string PrismCutout          = "advanced_cutout";
        private const string PrismEmission        = "advanced_emission";
        private const string PrismDisplacement    = "advanced_displacement";
        private const string PrismAnisotropy      = "advanced_anisotropy";

        // ── Generic schema fallback ──────────────────────────────────────
        private const string GenericDiffuse       = "generic_diffuse";
        private const string GenericBump          = "generic_bump_map";
        private const string GenericBumpAmount    = "generic_bump_amount";
        private const string GenericCutout        = "generic_cutout";
        private const string GenericSelfIllum     = "generic_self_illum_color";
        private const string GenericSelfIllumLum  = "generic_self_illum_luminance";
        private const string GenericRoughness     = "generic_glossiness";   // inverse semantic, but the slot we have
        private const string GenericReflectivity  = "generic_reflectivity_at_0deg";

        public sealed class ApplyResult
        {
            public bool Success { get; set; }
            public int SlotsWritten { get; set; }
            public List<string> Warnings { get; } = new List<string>();
            public string SchemaUsed { get; set; }   // "Prism" | "Generic" | "None"

            /// <summary>Side-effects that must fire only AFTER the outer
            /// transaction commits successfully — audit log, activity feed,
            /// cache invalidations. Calling this after a rollback would
            /// leave the chain pointing at work that didn't happen.</summary>
            public Action<Document, Material> PostCommit { get; set; }
        }

        /// <summary>
        /// Write every populated slot in <paramref name="manifest"/> into
        /// <paramref name="mat"/>'s appearance asset. The caller MUST have an
        /// open Revit Transaction on <paramref name="doc"/>.
        /// </summary>
        public static ApplyResult Apply(Document doc, Material mat, TexturePackManifest manifest)
        {
            var r = new ApplyResult();
            if (doc == null || mat == null || manifest == null)
            {
                r.Warnings.Add("Apply: null doc/material/manifest");
                return r;
            }
            if (mat.AppearanceAssetId == null || mat.AppearanceAssetId.Value <= 0)
            {
                r.Warnings.Add($"Apply: material '{mat.Name}' has no AppearanceAssetId");
                return r;
            }

            try
            {
                using (var scope = new AppearanceAssetEditScope(doc))
                {
                    Asset asset = scope.Start(mat.AppearanceAssetId);
                    if (asset == null)
                    {
                        r.Warnings.Add("Apply: scope.Start returned null asset");
                        scope.Cancel();
                        return r;
                    }

                    bool isPrism = LooksLikePrism(asset);
                    r.SchemaUsed = isPrism ? "Prism" : "Generic";

                    if (isPrism) ApplyPrism(asset, manifest, r);
                    else         ApplyGeneric(asset, manifest, r);

                    scope.Commit(true);
                }

                r.Success = r.SlotsWritten > 0;
                if (r.Success)
                {
                    // Defer audit + feed + cache invalidation until the
                    // outer transaction commits successfully. If the caller
                    // forgets to invoke PostCommit nothing leaks — but rolling
                    // back leaves no orphan log entries claiming work the
                    // user actually undid.
                    var packId = manifest.PackId;
                    var providerId = manifest.ProviderId;
                    var schemaUsed = r.SchemaUsed;
                    var slotsWritten = r.SlotsWritten;
                    r.PostCommit = (postDoc, postMat) =>
                    {
                        try { MaterialAuditLogger.Log(postDoc, "MAT_PbrApply", postMat?.Name, new Dictionary<string, object>
                        {
                            ["pack"] = packId,
                            ["provider"] = providerId,
                            ["schema"] = schemaUsed,
                            ["slots"] = slotsWritten,
                        }); } catch { /* non-fatal */ }
                        try { MaterialNameCache.Invalidate(postDoc); } catch { /* non-fatal */ }
                        try { MaterialUsageIndex.Invalidate(postDoc); } catch { /* non-fatal */ }
                        try { MaterialActivityFeed.Add("MAT_PbrApply", postMat?.Name,
                            $"{slotsWritten} maps · {schemaUsed} · {providerId}"); } catch { /* non-fatal */ }
                    };
                }
                return r;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PbrTextureApplier.Apply('{mat?.Name}'): {ex.Message}");
                r.Warnings.Add(ex.Message);
                return r;
            }
        }

        private static bool LooksLikePrism(Asset asset)
        {
            try { return asset.FindByName(PrismBaseColor) != null; }
            catch { return false; }
        }

        // ── Prism (advanced) writer ──────────────────────────────────────
        private static void ApplyPrism(Asset asset, TexturePackManifest m, ApplyResult r)
        {
            if (TrySetBitmap(asset, PrismBaseColor, m.Maps.BaseColor, m.Defaults)) r.SlotsWritten++;
            if (TrySetBitmap(asset, PrismRoughness, m.Maps.Roughness, m.Defaults)) r.SlotsWritten++;
            if (TrySetBitmap(asset, PrismMetalness, m.Maps.Metalness, m.Defaults)) r.SlotsWritten++;
            if (TrySetBitmap(asset, PrismNormal,    m.Maps.Normal,    m.Defaults)) r.SlotsWritten++;
            if (TrySetBitmap(asset, PrismCutout,    m.Maps.Opacity,   m.Defaults)) r.SlotsWritten++;
            if (TrySetBitmap(asset, PrismEmission,  m.Maps.Emission,  m.Defaults)) r.SlotsWritten++;
            if (TrySetBitmap(asset, PrismAnisotropy,m.Maps.Anisotropy,m.Defaults)) r.SlotsWritten++;

            // Bump and Displacement are separate Prism slots; the same height
            // file is usually appropriate for both.
            if (TrySetBitmap(asset, PrismBump,         m.Maps.Bump,         m.Defaults)) r.SlotsWritten++;
            if (m.Defaults.DisplacementEnabled &&
                TrySetBitmap(asset, PrismDisplacement, m.Maps.Displacement, m.Defaults)) r.SlotsWritten++;

            TrySetBumpAmount(asset, PrismBump, m.Defaults.BumpAmount, r);
        }

        // ── Generic writer (fallback, lossy) ─────────────────────────────
        private static void ApplyGeneric(Asset asset, TexturePackManifest m, ApplyResult r)
        {
            if (TrySetBitmap(asset, GenericDiffuse, m.Maps.BaseColor, m.Defaults)) r.SlotsWritten++;
            // Generic accepts a single bump-style map. Prefer normal, then
            // bump, then displacement source.
            string bumpSrc = !string.IsNullOrEmpty(m.Maps.Normal) ? m.Maps.Normal
                          : !string.IsNullOrEmpty(m.Maps.Bump)   ? m.Maps.Bump
                          : m.Maps.Displacement;
            if (TrySetBitmap(asset, GenericBump, bumpSrc, m.Defaults))
            {
                r.SlotsWritten++;
                TrySetBumpAmount(asset, GenericBump, m.Defaults.BumpAmount, r);
                if (!string.IsNullOrEmpty(m.Maps.Displacement) || !string.IsNullOrEmpty(m.Maps.Bump))
                    r.Warnings.Add("Generic schema: bump + displacement collapsed into the single Generic bump slot. Convert to Prism for full PBR.");
            }
            if (TrySetBitmap(asset, GenericCutout, m.Maps.Opacity, m.Defaults)) r.SlotsWritten++;
            if (TrySetBitmap(asset, GenericSelfIllum, m.Maps.Emission, m.Defaults)) r.SlotsWritten++;

            if (!string.IsNullOrEmpty(m.Maps.Roughness)) r.Warnings.Add("Generic schema doesn't expose a roughness slot — convert to Prism for full PBR.");
            if (!string.IsNullOrEmpty(m.Maps.Metalness)) r.Warnings.Add("Generic schema doesn't expose a metalness slot — convert to Prism for full PBR.");
            if (!string.IsNullOrEmpty(m.Maps.Ao))        r.Warnings.Add("Generic schema doesn't expose an AO slot — convert to Prism for full PBR.");
        }

        /// <summary>Connect a UnifiedBitmap to <paramref name="slotName"/> on
        /// the asset and stamp it with the manifest's UV defaults. Rejects
        /// UNC paths and any path that isn't fully qualified to a local
        /// drive — both because Revit blocks UI on network IO during
        /// material resolution, and to neutralise credential-leak vectors
        /// from hostile <c>manifest.json</c> files.</summary>
        private static bool TrySetBitmap(Asset asset, string slotName, string filePath, TexturePackDefaults defaults)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            if (!IsSafeLocalPath(filePath))
            {
                StingLog.WarnRateLimited("PbrUncBlock", $"TrySetBitmap rejected non-local path '{filePath}'");
                return false;
            }
            if (!File.Exists(filePath)) return false;
            try
            {
                var slot = asset.FindByName(slotName) as AssetProperty;
                if (slot == null) return false;

                // Create / locate a UnifiedBitmap asset and connect it.
                // Revit 2024+ `AssetProperty.AddConnectedAsset(string)` returns
                // void — fetch the newly-connected asset via
                // GetSingleConnectedAsset() / GetConnectedProperty(0).
                Asset bmpAsset = null;
                if (slot.NumberOfConnectedProperties > 0 &&
                    slot.GetConnectedProperty(0) is Asset existing &&
                    existing.Name == "UnifiedBitmapSchema")
                {
                    bmpAsset = existing;
                }
                else
                {
                    bmpAsset = slot.GetSingleConnectedAsset();
                    if (bmpAsset == null)
                    {
                        slot.AddConnectedAsset("UnifiedBitmapSchema");
                        bmpAsset = slot.GetSingleConnectedAsset()
                                ?? slot.GetConnectedProperty(0) as Asset;
                    }
                }
                if (bmpAsset == null) return false;

                if (bmpAsset.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) is AssetPropertyString pathProp)
                    pathProp.Value = filePath;

                // UV controls — real-world scale + offset + rotation.
                if (bmpAsset.FindByName(UnifiedBitmap.TextureRealWorldScaleX) is AssetPropertyDistance rwX)
                    rwX.Value = MillimetresToFeet(defaults.RealWorldScaleXMm);
                if (bmpAsset.FindByName(UnifiedBitmap.TextureRealWorldScaleY) is AssetPropertyDistance rwY)
                    rwY.Value = MillimetresToFeet(defaults.RealWorldScaleYMm);
                if (bmpAsset.FindByName(UnifiedBitmap.TextureRealWorldOffsetX) is AssetPropertyDistance rwOx)
                    rwOx.Value = MillimetresToFeet(defaults.UvOffsetX);
                if (bmpAsset.FindByName(UnifiedBitmap.TextureRealWorldOffsetY) is AssetPropertyDistance rwOy)
                    rwOy.Value = MillimetresToFeet(defaults.UvOffsetY);
                if (bmpAsset.FindByName(UnifiedBitmap.TextureWAngle) is AssetPropertyDouble rot)
                    rot.Value = defaults.UvRotationDeg;
                return true;
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("PbrSetBitmap", $"TrySetBitmap '{slotName}': {ex.Message}");
                return false;
            }
        }

        private static void TrySetBumpAmount(Asset asset, string bumpSlotName, double amount, ApplyResult r)
        {
            try
            {
                // Some schemas expose bump amount as a sibling double property;
                // others bake it into the asset itself. Best-effort.
                var prop = asset.FindByName(bumpSlotName + "_amount") as AssetPropertyDouble;
                if (prop != null) prop.Value = Clamp01x10(amount);
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"Set bump amount: {ex.Message}");
            }
        }

        private static double Clamp01x10(double v) => v < 0 ? 0 : v > 10 ? 10 : v;

        /// <summary>Revit internal length unit is feet. Use this for the
        /// UnifiedBitmap real-world dimension properties.</summary>
        private static double MillimetresToFeet(double mm) => mm / 304.8;

        /// <summary>True only for rooted local paths (e.g.
        /// <c>C:\…</c>). Rejects UNC (<c>\\server\share</c>), URIs
        /// (<c>http://…</c>), and relative paths. Allows long
        /// (<c>\\?\C:\…</c>) prefixed paths.</summary>
        private static bool IsSafeLocalPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            try
            {
                if (p.StartsWith("\\\\?\\", StringComparison.Ordinal)) p = p.Substring(4);
                if (p.StartsWith("\\\\", StringComparison.Ordinal)) return false; // UNC
                if (p.IndexOf("://", StringComparison.Ordinal) >= 0) return false; // URI scheme
                return Path.IsPathRooted(p) && !string.IsNullOrEmpty(Path.GetPathRoot(p));
            }
            catch { return false; }
        }

        public sealed class ClearResult
        {
            public int SlotsCleared { get; set; }
            public Action<Document, Material> PostCommit { get; set; }
        }

        /// <summary>Disconnect bitmaps from every PBR slot. Caller MUST hold a
        /// Revit Transaction. Backwards-compatible overload returns just the
        /// count; the structured overload exposes the deferred-effect hook.</summary>
        public static int ClearAllSlots(Document doc, Material mat)
            => ClearAllSlotsWithResult(doc, mat).SlotsCleared;

        public static ClearResult ClearAllSlotsWithResult(Document doc, Material mat)
        {
            var result = new ClearResult();
            int cleared = 0;
            if (doc == null || mat?.AppearanceAssetId == null) return result;
            try
            {
                using (var scope = new AppearanceAssetEditScope(doc))
                {
                    var asset = scope.Start(mat.AppearanceAssetId);
                    if (asset == null) { scope.Cancel(); return result; }

                    bool isPrism = LooksLikePrism(asset);
                    string[] slots = isPrism
                        ? new[] { PrismBaseColor, PrismRoughness, PrismMetalness, PrismNormal,
                                  PrismBump, PrismDisplacement, PrismCutout, PrismEmission, PrismAnisotropy }
                        : new[] { GenericDiffuse, GenericBump, GenericCutout, GenericSelfIllum };

                    foreach (var slotName in slots)
                    {
                        try
                        {
                            var prop = asset.FindByName(slotName) as AssetProperty;
                            if (prop == null) continue;
                            // Revit 2024+ `RemoveConnectedAsset()` takes no args
                            // and detaches the single connection. Loop until the
                            // slot reports zero connections — safe even when more
                            // than one was attached (rare).
                            int guard = 0;
                            while (prop.NumberOfConnectedProperties > 0 && guard++ < 8)
                            {
                                prop.RemoveConnectedAsset();
                                cleared++;
                            }
                        }
                        catch (Exception ex) { StingLog.WarnRateLimited("PbrClear", $"Clear '{slotName}': {ex.Message}"); }
                    }
                    scope.Commit(true);
                }
                if (cleared > 0)
                {
                    int clearedCount = cleared;
                    result.PostCommit = (postDoc, postMat) =>
                    {
                        try { MaterialAuditLogger.Log(postDoc, "MAT_PbrClear", postMat?.Name, new Dictionary<string, object> { ["slotsCleared"] = clearedCount }); } catch { /* non-fatal */ }
                        try { MaterialNameCache.Invalidate(postDoc); } catch { /* non-fatal */ }
                        try { MaterialUsageIndex.Invalidate(postDoc); } catch { /* non-fatal */ }
                        try { MaterialActivityFeed.Add("MAT_PbrClear", postMat?.Name, $"{clearedCount} slot(s) cleared"); } catch { /* non-fatal */ }
                    };
                }
            }
            catch (Exception ex) { StingLog.Warn($"PbrTextureApplier.ClearAllSlots('{mat?.Name}'): {ex.Message}"); }
            result.SlotsCleared = cleared;
            return result;
        }
    }
}
