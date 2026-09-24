using System;
using System.Globalization;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Electrical;

namespace StingTools.Commands.Electrical.Lighting
{
    /// <summary>
    /// Reads luminaire lumens / watts from the instance, then from its type.
    /// A missing value is reported as missing (0) — the caller decides whether
    /// to assume one, and if it does it must say so in its output. Nothing in
    /// here invents a number.
    /// </summary>
    internal static class LuminaireDataReader
    {
        /// <summary>Lumen fallback used only when a caller explicitly opts in and flags it.</summary>
        public const double AssumedLumens = 4000;
        /// <summary>Wattage fallback used only when a caller explicitly opts in and flags it.</summary>
        public const double AssumedWatts = 36;

        private static readonly string[] LumenNames =
            { "ELC_PHOTO_LUMENS_NR", "Luminous Flux", "Initial Intensity" };
        private static readonly string[] WattNames =
            { "LTG_FIX_LMP_WATTAGE_W", "ELC_PHOTO_WATTS_NR", "Wattage" };

        /// <summary>Lumens per luminaire; 0 when no source carries a value.</summary>
        public static double Lumens(FamilyInstance fi, out string source)
            => ReadInstanceThenType(fi, LumenNames, isFlux: true, out source);

        /// <summary>Watts per luminaire (SI, via ElecUnits); 0 when no source carries a value.</summary>
        public static double Watts(FamilyInstance fi, out string source)
            => ReadInstanceThenType(fi, WattNames, isFlux: false, out source);

        private static double ReadInstanceThenType(FamilyInstance fi, string[] names, bool isFlux, out string source)
        {
            source = "";
            if (fi == null) return 0;
            double v = ReadFirst(fi, names, isFlux, out string n);
            if (v > 0) { source = $"instance:{n}"; return v; }
            Element type = null;
            try { type = fi.Document.GetElement(fi.GetTypeId()); }
            catch (Exception ex) { StingLog.Warn($"LuminaireDataReader type lookup {fi.Id}: {ex.Message}"); }
            v = ReadFirst(type, names, isFlux, out n);
            if (v > 0) { source = $"type:{n}"; return v; }
            if (isFlux && type != null)
            {
                // Revit's own photometric flux on the type.
                try
                {
                    var p = type.get_Parameter(BuiltInParameter.FBX_LIGHT_LIMUNOUS_FLUX);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                    {
                        source = "type:Luminous Flux (built-in)";
                        return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Lumens);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"LuminaireDataReader FBX flux {fi.Id}: {ex.Message}"); }
            }
            return 0;
        }

        private static double ReadFirst(Element el, string[] names, bool isFlux, out string usedName)
        {
            usedName = "";
            if (el == null) return 0;
            foreach (var n in names)
            {
                Parameter p;
                try { p = el.LookupParameter(n); } catch { continue; }
                if (p == null || !p.HasValue) continue;
                double v = 0;
                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Double:
                            ForgeTypeId spec = null;
                            try { spec = p.Definition?.GetDataType(); } catch { }
                            // A luminous INTENSITY is candela, not lumens — never read it as flux.
                            if (isFlux && spec == SpecTypeId.LuminousIntensity) continue;
                            if (isFlux && spec == SpecTypeId.LuminousFlux)
                                v = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Lumens);
                            else if (!isFlux)
                                v = ElecUnits.ToSi(p); // W / VA stored in internal units
                            else
                                v = p.AsDouble();
                            break;
                        case StorageType.Integer:
                            v = p.AsInteger();
                            break;
                        case StorageType.String:
                            v = ParseLeadingNumber(p.AsString());
                            break;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"LuminaireDataReader '{n}' on {el.Id}: {ex.Message}"); }
                if (v > 0) { usedName = n; return v; }
            }
            return 0;
        }

        /// <summary>"3400 lm" / "3,400" / "36W" → leading number; 0 when none.</summary>
        internal static double ParseLeadingNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim();
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == ',')) i++;
            string num = s.Substring(0, i).Replace(",", "");
            return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }
}
