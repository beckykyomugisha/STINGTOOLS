using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using StingTools.Core;
using StingTools.UI;   // MaterialAuditLogger

namespace StingTools.Core.Materials
{
    /// <summary>
    /// Helpers for migrating a material's appearance asset from the legacy
    /// Generic schema to the modern Prism (Advanced) schema so the full
    /// 10-slot PBR pipeline can write to it.
    /// </summary>
    /// <remarks>
    /// Revit doesn't expose an in-place "change schema" API. The strategy is:
    /// (1) duplicate a known Prism appearance asset, (2) point the material
    /// at the duplicate. Optionally the caller can request a NEW material
    /// (clone) instead of mutating the original — useful when many element
    /// types share the appearance asset and we don't want a global change.
    /// </remarks>
    public static class GenericToPrismConverter
    {
        public enum ConvertMode
        {
            /// <summary>Mutate the material in place by swapping its appearance asset.</summary>
            InPlace,
            /// <summary>Duplicate the material, swap the appearance asset on
            /// the duplicate, leave the original untouched. Caller receives
            /// the new material.</summary>
            DuplicateMaterial,
        }

        public sealed class ConvertResult
        {
            public bool Success { get; set; }
            public Material ResultMaterial { get; set; }
            public string Note { get; set; }
        }

        /// <summary>True when the material's appearance asset is Prism-shaped
        /// (Autodesk Standard Surface). False = Generic or unknown.</summary>
        public static bool IsPrism(Document doc, Material mat)
        {
            try
            {
                if (mat?.AppearanceAssetId == null || mat.AppearanceAssetId.Value <= 0) return false;
                if (!(doc.GetElement(mat.AppearanceAssetId) is AppearanceAssetElement aae)) return false;
                var src = aae.GetRenderingAsset();
                return src?.FindByName("advanced_base_color") != null;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IsPrism", $"IsPrism: {ex.Message}"); return false; }
        }

        /// <summary>Convert/clone. Caller MUST have an open Revit Transaction.</summary>
        public static ConvertResult Convert(Document doc, Material mat, ConvertMode mode)
        {
            var r = new ConvertResult();
            if (doc == null || mat == null)
            {
                r.Note = "null doc/material";
                return r;
            }
            if (IsPrism(doc, mat))
            {
                r.Success = true;
                r.ResultMaterial = mat;
                r.Note = "Already Prism";
                return r;
            }

            try
            {
                var donor = FindPrismDonorAsset(doc);
                if (donor == null)
                {
                    r.Note = "No Prism appearance asset present in this project to duplicate from. Load a Prism material from the Autodesk Library and retry.";
                    return r;
                }

                Material target = mat;
                if (mode == ConvertMode.DuplicateMaterial)
                {
                    string newName = UniqueMaterialName(doc, mat.Name + " (PBR)");
                    // Material.Duplicate returns a Material (not an ElementId)
                    // since Revit 2017.
                    target = mat.Duplicate(newName) ?? mat;
                }

                // Duplicate the donor's appearance-asset element so we don't
                // share state with the donor.
                string assetName = UniqueAssetName(doc, target.Name + " - Prism");
                AppearanceAssetElement newAae = donor.Duplicate(assetName);
                if (newAae == null)
                {
                    r.Note = "Donor.Duplicate returned null";
                    return r;
                }

                // Reset the duplicate's base color to white so the destination
                // pack's base-color bitmap isn't multiplied by stale tint.
                try
                {
                    using (var scope = new AppearanceAssetEditScope(doc))
                    {
                        var editable = scope.Start(newAae.Id);
                        if (editable?.FindByName("advanced_base_color") is AssetPropertyDoubleArray4d bc)
                            bc.SetValueAsDoubles(new[] { 1.0, 1.0, 1.0, 1.0 });
                        scope.Commit(true);
                    }
                }
                catch { /* non-fatal: keep donor's default tint */ }

                target.AppearanceAssetId = newAae.Id;

                MaterialAuditLogger.Log(doc, "MAT_PrismConvert", mat.Name, new Dictionary<string, object>
                {
                    ["mode"] = mode.ToString(),
                    ["resultMaterial"] = target.Name,
                });

                r.Success = true;
                r.ResultMaterial = target;
                r.Note = mode == ConvertMode.DuplicateMaterial
                    ? $"Created '{target.Name}' with a fresh Prism appearance asset."
                    : "Swapped the in-place appearance asset to Prism.";
                return r;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GenericToPrismConverter.Convert('{mat?.Name}'): {ex.Message}");
                r.Note = ex.Message;
                return r;
            }
        }

        private static AppearanceAssetElement FindPrismDonorAsset(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(AppearanceAssetElement))
                .Cast<AppearanceAssetElement>()
                .FirstOrDefault(aae =>
                {
                    try
                    {
                        var ra = aae.GetRenderingAsset();
                        return ra?.FindByName("advanced_base_color") != null;
                    }
                    catch { return false; }
                });
        }

        private static string UniqueMaterialName(Document doc, string baseName)
        {
            var existing = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(Material))
                .Cast<Material>().Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string c = $"{baseName} {i}";
                if (!existing.Contains(c)) return c;
            }
            return $"{baseName} {Guid.NewGuid():N}";
        }

        private static string UniqueAssetName(Document doc, string baseName)
        {
            var existing = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(AppearanceAssetElement))
                .Cast<AppearanceAssetElement>().Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string c = $"{baseName} {i}";
                if (!existing.Contains(c)) return c;
            }
            return $"{baseName} {Guid.NewGuid():N}";
        }
    }
}
