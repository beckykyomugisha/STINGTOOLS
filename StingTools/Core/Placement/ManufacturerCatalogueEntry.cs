// Phase 139.2 — Manufacturer catalogue POCO.
//
// Backs STING_MANUFACTURER_CATALOGUE.json. Each entry describes one
// manufacturer SKU (MK Logic Plus 1G flush, BESA round box, …) with the
// dimensions PlacementScorer / TwoPhaseBoxPlacer / CompoundClusterPlacer
// need at runtime. All linear dimensions are millimetres.

namespace StingTools.Core.Placement
{
    public class ManufacturerCatalogueEntry
    {
        public string ManufacturerCode  { get; set; } = "";
        public string CatalogueRef      { get; set; } = "";
        public string Description       { get; set; } = "";
        public int    GangCount         { get; set; } = 0;
        public int    BoxDepthMm        { get; set; } = 0;
        public double BoxExternalLMm    { get; set; } = 0.0;
        public double BoxExternalWMm    { get; set; } = 0.0;
        public double FixingCentresMm   { get; set; } = 0.0;
        public double ModulePitchMm     { get; set; } = 0.0;
        public string IpRating          { get; set; } = "";
        public string MountType         { get; set; } = "";
        public string RevitFamilyName   { get; set; } = "";
        public string RevitTypeName     { get; set; } = "";
        public string InsertionOrigin   { get; set; } = "";
        public string FaceplateStandard { get; set; } = "";
    }
}
