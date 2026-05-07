namespace StingTools.IfcResults
{
    /// <summary>
    /// STING + DIALux + ElumTools + Relux property-set contract for
    /// photometric round-trip via IFC 4. There is no standard IFC PSet for
    /// illuminance, so we publish our own and export it on every IFC the
    /// STING DIALux exporter (Phase 179) writes. Importers (DIALux evo /
    /// ElumTools / Relux) that respect the field names round-trip cleanly;
    /// importers that don't are matched by display-name fallbacks in
    /// IfcResultsImportEngine.
    /// </summary>
    public static class StingLightingPSet
    {
        /// <summary>Property-set name we publish on every IfcSpace + IfcLightFixture.</summary>
        public const string PSetName = "Pset_StingLightingResults";

        /// <summary>Maintained illuminance at the working plane in lux.</summary>
        public const string IlluminanceLux = "IlluminanceLux";
        /// <summary>Average illuminance over the whole calculation surface (lux).</summary>
        public const string AverageLux = "AverageLux";
        /// <summary>Minimum point illuminance over the calculation surface (lux).</summary>
        public const string MinimumLux = "MinimumLux";
        /// <summary>Maximum point illuminance over the calculation surface (lux).</summary>
        public const string MaximumLux = "MaximumLux";
        /// <summary>Uniformity ratio Emin / Eavg per BS EN 12464-1.</summary>
        public const string UniformityRatio = "UniformityRatio";
        /// <summary>Unified Glare Rating per CIE 117.</summary>
        public const string UGR = "UGR";
        /// <summary>ISO 8601 timestamp of the calculation that produced these values.</summary>
        public const string CalculationDate = "CalculationDate";
        /// <summary>"DIALux" / "ElumTools" / "Relux" / "Estimate".</summary>
        public const string EngineUsed = "EngineUsed";
        /// <summary>Engine version string (e.g. "DIALux evo 13.2").</summary>
        public const string EngineVersion = "EngineVersion";
        /// <summary>Working-plane height used for the calculation (m).</summary>
        public const string WorkingPlaneHeightM = "WorkingPlaneHeightM";

        /// <summary>
        /// Display-name fallbacks the importer accepts when the source IFC
        /// uses a different convention. Order matters — first match wins.
        /// </summary>
        public static readonly string[] IlluminanceAliases =
        {
            IlluminanceLux,
            "MaintainedIlluminance",
            "Illuminance",
            "AverageIlluminance",
            "Em",
            "E_avg"
        };

        public static readonly string[] UgrAliases =
        {
            UGR, "UnifiedGlareRating", "UGR_value"
        };

        public static readonly string[] UniformityAliases =
        {
            UniformityRatio, "Uniformity", "Uo", "U0", "EminEavgRatio"
        };
    }
}
