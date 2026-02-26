using System;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// Ported from tag_logic.py — parameter read/write helpers for Revit elements.
    /// </summary>
    public static class ParameterHelpers
    {
        /// <summary>Return the string value of a named parameter, or empty string.</summary>
        public static string GetString(Element el, string paramName)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p != null && p.StorageType == StorageType.String)
            {
                string v = p.AsString();
                return v ?? string.Empty;
            }
            return string.Empty;
        }

        /// <summary>Set a TEXT parameter. Skips read-only params. Skips non-empty unless overwrite.</summary>
        public static bool SetString(Element el, string paramName, string value,
            bool overwrite = false)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                return false;

            string existing = p.AsString() ?? string.Empty;
            if (existing.Length > 0 && !overwrite)
                return false;

            p.Set(value);
            return true;
        }

        /// <summary>Set only when the parameter is currently empty.</summary>
        public static bool SetIfEmpty(Element el, string paramName, string value)
        {
            return SetString(el, paramName, value, overwrite: false);
        }

        /// <summary>Return a short level code from the element's host level.</summary>
        public static string GetLevelCode(Document doc, Element el)
        {
            try
            {
                ElementId lvlId = el.LevelId;
                if (lvlId == null || lvlId == ElementId.InvalidElementId)
                    return "XX";

                Level lvl = doc.GetElement(lvlId) as Level;
                if (lvl == null)
                    return "XX";

                string name = lvl.Name.Trim();
                string lower = name.ToLowerInvariant();

                if (lower.StartsWith("level "))
                    return "L" + name.Substring(6).Trim().PadLeft(2, '0');
                if (lower == "ground" || lower == "ground floor" || lower == "ground level")
                    return "GF";
                if (lower.StartsWith("basement") || lower.StartsWith("b"))
                {
                    string digits = ExtractDigits(name);
                    return "B" + (digits.Length > 0 ? digits : "1");
                }
                if (lower.StartsWith("roof"))
                    return "RF";

                return name.ToUpperInvariant()
                    .Replace(" ", "")
                    .Substring(0, Math.Min(4, name.Length));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetLevelCode failed for element {el?.Id}: {ex.Message}");
                return "XX";
            }
        }

        /// <summary>Return the category name of an element, or empty string.</summary>
        public static string GetCategoryName(Element el)
        {
            try
            {
                return el.Category?.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetCategoryName failed for element {el?.Id}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ExtractDigits(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsDigit(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
