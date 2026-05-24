using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Priority 4 — texture + hatch pattern editing.
    /// Texture editing uses AppearanceAssetEditScope to write the
    /// `generic_diffuse` asset's bitmap path. Hatch editing is plain
    /// element-id assignment on the Material's SurfaceForeground /
    /// Background / Cut Foreground / Background pattern properties.
    /// </summary>
    public static class MaterialAppearanceActions
    {
        /// <summary>List FillPatternElements in the document, ordered by
        /// name. Used by the inline hatch picker.</summary>
        public static IReadOnlyList<FillPatternElement> ListSurfacePatterns(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .Where(f => f.GetFillPattern().Target == FillPatternTarget.Drafting ||
                                f.GetFillPattern().Target == FillPatternTarget.Model)
                    .OrderBy(f => f.Name).ToList();
            }
            catch (Exception ex) { StingLog.Warn($"ListSurfacePatterns: {ex.Message}"); return Array.Empty<FillPatternElement>(); }
        }

        public static bool SetHatchPattern(Document doc, Material mat, string slot, ElementId patternId)
        {
            if (doc == null || mat == null || patternId == null) return false;
            try
            {
                using (var t = new Transaction(doc, $"STING Set {slot} pattern on '{mat.Name}'"))
                {
                    t.Start();
                    switch (slot)
                    {
                        case "SurfaceFg": mat.SurfaceForegroundPatternId = patternId; break;
                        case "SurfaceBg": mat.SurfaceBackgroundPatternId = patternId; break;
                        case "CutFg":     mat.CutForegroundPatternId     = patternId; break;
                        case "CutBg":     mat.CutBackgroundPatternId     = patternId; break;
                        default: t.RollBack(); return false;
                    }
                    t.Commit();
                }
                MaterialAuditLogger.Log(doc, "MAT_PatternChange", mat.Name,
                    new Dictionary<string, object> { ["slot"] = slot, ["pattern"] = patternId.Value });
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"SetHatchPattern: {ex.Message}"); return false; }
        }

        public static bool SetSurfaceColor(Document doc, Material mat, byte r, byte g, byte b)
        {
            if (doc == null || mat == null) return false;
            try
            {
                using (var t = new Transaction(doc, $"STING Set color on '{mat.Name}'"))
                {
                    t.Start();
                    mat.Color = new Color(r, g, b);
                    t.Commit();
                }
                MaterialAuditLogger.Log(doc, "MAT_ColorChange", mat.Name,
                    new Dictionary<string, object> { ["rgb"] = $"{r},{g},{b}" });
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"SetSurfaceColor: {ex.Message}"); return false; }
        }

        /// <summary>Replace the bitmap path on the material's appearance
        /// asset's generic_diffuse property. Requires the asset to be a
        /// generic-shading asset (most BLE materials are).</summary>
        public static bool SetTexturePath(Document doc, Material mat, string newPath)
        {
            if (doc == null || mat == null || string.IsNullOrEmpty(newPath)) return false;
            try
            {
                var aaeId = mat.AppearanceAssetId;
                if (aaeId == null || aaeId.Value <= 0) return false;
                var aae = doc.GetElement(aaeId) as AppearanceAssetElement;
                if (aae == null) return false;
                using (var scope = new AppearanceAssetEditScope(doc))
                {
                    Asset editable = scope.Start(aaeId);
                    var diffuse = editable.FindByName("generic_diffuse") as AssetProperty;
                    if (diffuse == null) { scope.Cancel(); return false; }
                    var connected = diffuse.GetConnectedProperty(0) as Asset;
                    if (connected == null) { scope.Cancel(); return false; }
                    var path = connected.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;
                    if (path == null) { scope.Cancel(); return false; }
                    path.Value = newPath;
                    scope.Commit(true);
                }
                MaterialAuditLogger.Log(doc, "MAT_TextureChange", mat.Name,
                    new Dictionary<string, object> { ["path"] = newPath });
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"SetTexturePath '{newPath}': {ex.Message}"); return false; }
        }

        public static string ReadCurrentTexturePath(Document doc, Material mat)
        {
            try
            {
                var aaeId = mat?.AppearanceAssetId;
                if (aaeId == null || aaeId.Value <= 0) return "";
                if (!(doc.GetElement(aaeId) is AppearanceAssetElement aae)) return "";
                var src = aae.GetRenderingAsset();
                // Prism (Autodesk Standard Surface) materials live behind
                // `advanced_base_color`; Generic materials behind
                // `generic_diffuse`. Try Prism first because the PBR pipeline
                // creates Prism assets and the Generic-only path produced a
                // false-empty preview for converted materials.
                foreach (var slotName in new[] { "advanced_base_color", "generic_diffuse" })
                {
                    var slot = src.FindByName(slotName) as AssetProperty;
                    if (slot == null) continue;
                    var connected = slot.GetConnectedProperty(0) as Asset
                                 ?? slot.GetSingleConnectedAsset();
                    var path = connected?.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;
                    if (!string.IsNullOrEmpty(path?.Value)) return path.Value;
                }
                return "";
            }
            catch (Exception ex) { StingLog.WarnRateLimited("ReadTexture", $"ReadCurrentTexturePath: {ex.Message}"); return ""; }
        }
    }
}
