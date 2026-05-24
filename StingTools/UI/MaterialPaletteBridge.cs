using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.UI
{
    /// <summary>
    /// A-5 — Bridge between TagStyleEngine's discipline colour palettes
    /// and the MAT panel. Closes the "two colour stories" gap by
    /// surfacing a single Brush per material via its primary discipline
    /// (derived from MaterialDisciplineAffinity).
    ///
    /// Used by callers that want to paint MAT chips / legend swatches
    /// consistently with the tag colours users already see on sheets.
    /// </summary>
    public static class MaterialPaletteBridge
    {
        public const string DefaultSchemeKey = "Discipline";

        public static Brush BrushForMaterialClass(string materialClass, string schemeKey = null)
        {
            var disc = MaterialDisciplineAffinity.ResolvePrimary(materialClass);
            return BrushForDiscipline(disc, schemeKey);
        }

        public static Brush BrushForDiscipline(string discipline, string schemeKey = null)
        {
            if (string.IsNullOrEmpty(discipline)) return Brushes.Gray;
            try
            {
                schemeKey ??= DefaultSchemeKey;
                // TagStyleEngine.BuiltInSchemes is `internal`; we read it
                // via reflection so this file stays out of the Tags
                // assembly's internal surface.
                var type = typeof(TagStyleEngine);
                var fld = type.GetField("BuiltInSchemes",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);
                var dict = fld?.GetValue(null) as System.Collections.IDictionary;
                if (dict == null) return Brushes.Gray;
                if (!dict.Contains(schemeKey)) return Brushes.Gray;
                var scheme = dict[schemeKey];
                var discColours = scheme.GetType().GetProperty("DisciplineColors")?.GetValue(scheme) as System.Collections.IDictionary;
                if (discColours == null || !discColours.Contains(discipline)) return Brushes.Gray;
                var revitColor = discColours[discipline];
                // Revit Color → WPF Brush.
                byte r = 0, g = 0, b = 0;
                try
                {
                    var rt = revitColor.GetType();
                    r = (byte)(int)rt.GetProperty("Red").GetValue(revitColor);
                    g = (byte)(int)rt.GetProperty("Green").GetValue(revitColor);
                    b = (byte)(int)rt.GetProperty("Blue").GetValue(revitColor);
                }
                catch (Exception ex) { StingLog.WarnRateLimited("MatPalette.Brush", $"Color extract: {ex.Message}"); return Brushes.Gray; }
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            catch (Exception ex) { StingLog.WarnRateLimited("MatPalette", $"BrushForDiscipline: {ex.Message}"); return Brushes.Gray; }
        }
    }
}
