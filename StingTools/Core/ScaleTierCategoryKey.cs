using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    /// <summary>
    /// DSCH-27 — which Tag Studio Scale-tab multiplier (DUCTS / PIPES / EQUIPMENT /
    /// FIXTURES, persisted as SCALE_CATEGORY_MULTIPLIERS in project_config.json)
    /// applies to a Revit category. Revit-free so the mapping is tested.
    /// A category that belongs to none of the four gets no key and no multiplier.
    /// </summary>
    public static class ScaleTierCategoryKey
    {
        public const string Ducts = "DUCTS";
        public const string Pipes = "PIPES";
        public const string Equipment = "EQUIPMENT";
        public const string Fixtures = "FIXTURES";

        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ducts"] = Ducts,
            ["Duct Fittings"] = Ducts,
            ["Duct Accessories"] = Ducts,
            ["Flex Ducts"] = Ducts,
            ["Duct Insulation"] = Ducts,
            ["Duct Lining"] = Ducts,
            ["Air Terminals"] = Ducts,

            ["Pipes"] = Pipes,
            ["Pipe Fittings"] = Pipes,
            ["Pipe Accessories"] = Pipes,
            ["Flex Pipes"] = Pipes,
            ["Pipe Insulation"] = Pipes,

            ["Mechanical Equipment"] = Equipment,
            ["Electrical Equipment"] = Equipment,
            ["Plumbing Equipment"] = Equipment,
            ["Specialty Equipment"] = Equipment,
            ["Medical Equipment"] = Equipment,

            ["Lighting Fixtures"] = Fixtures,
            ["Plumbing Fixtures"] = Fixtures,
            ["Electrical Fixtures"] = Fixtures,
            ["Sprinklers"] = Fixtures,
            ["Lighting Devices"] = Fixtures,
            ["Fire Alarm Devices"] = Fixtures,
            ["Communication Devices"] = Fixtures,
            ["Data Devices"] = Fixtures,
            ["Security Devices"] = Fixtures,
            ["Nurse Call Devices"] = Fixtures,
            ["Telephone Devices"] = Fixtures,
        };

        /// <summary>The multiplier key for a Revit category display name, or null.</summary>
        public static string For(string categoryName)
            => !string.IsNullOrEmpty(categoryName) && Map.TryGetValue(categoryName, out var k) ? k : null;
    }
}
