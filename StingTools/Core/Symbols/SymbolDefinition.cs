// StingTools — MEP/FP/SLD Symbol Library (Phase 175)
//
// JSON-backed POCO model for STING_*_SYMBOLS.json. Each definition
// describes one Revit family that the SymbolLibraryCreator emits:
//   * GenericAnnotation — SLD / schematic / legend keys
//   * MEPAccessory      — inline pipe or duct accessories (two connectors)
//   * MEPEquipment      — point devices (terminals, sanitary ware,
//                         alarms) with symbolic plan + 3D mass
//
// Geometry coordinates are normalised −0.5…+0.5 over a unit square; the
// creator scales them by SymbolSize (millimetres) to Revit internal feet
// at runtime.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Core.Symbols
{
    public sealed class SymbolLibrary
    {
        [JsonProperty("version")]  public string Version { get; set; } = "1.0";
        [JsonProperty("standard")] public string Standard { get; set; }
        [JsonProperty("symbols")]  public List<SymbolDefinition> Symbols { get; set; }
            = new List<SymbolDefinition>();
    }

    public sealed class SymbolDefinition
    {
        [JsonProperty("id")]           public string Id { get; set; }
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("category")]    public string Category { get; set; }

        /// <summary>GenericAnnotation | MEPAccessory | MEPEquipment</summary>
        [JsonProperty("familyType")]  public string FamilyType { get; set; }

        [JsonProperty("discipline")]  public string Discipline { get; set; }
        [JsonProperty("subcategory")] public string Subcategory { get; set; }

        /// <summary>Visible symbol size in millimetres at 1:100 (drives geometry scale).</summary>
        [JsonProperty("symbolSize")]  public double SymbolSize { get; set; } = 3.0;

        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public List<ParameterDefinition> Parameters { get; set; }
            = new List<ParameterDefinition>();

        [JsonProperty("geometry", NullValueHandling = NullValueHandling.Ignore)]
        public SymbolGeometry Geometry { get; set; }

        [JsonProperty("connectors", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConnectorDefinition> Connectors { get; set; }

        [JsonProperty("solid3D", NullValueHandling = NullValueHandling.Ignore)]
        public Solid3DDefinition Solid3D { get; set; }

        /// <summary>
        /// Optional per-standard geometry overrides. When present, the
        /// library creator emits one extra family named
        /// <c>&lt;STANDARD&gt;_&lt;Id&gt;</c> (e.g. <c>IEEE_SLD_MCB</c>)
        /// using the override's geometry / connectors / solid3D, falling
        /// back to the base values for any field the override doesn't
        /// set. The base family (bare id) is always emitted regardless,
        /// so standards with no override automatically use it via the
        /// concept registry's fallback chain.
        /// </summary>
        [JsonProperty("standardOverrides", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, StandardGeometryOverride> StandardOverrides { get; set; }
    }

    public sealed class StandardGeometryOverride
    {
        [JsonProperty("symbolSize", NullValueHandling = NullValueHandling.Ignore)]
        public double? SymbolSize { get; set; }

        [JsonProperty("geometry", NullValueHandling = NullValueHandling.Ignore)]
        public SymbolGeometry Geometry { get; set; }

        [JsonProperty("connectors", NullValueHandling = NullValueHandling.Ignore)]
        public List<ConnectorDefinition> Connectors { get; set; }

        [JsonProperty("solid3D", NullValueHandling = NullValueHandling.Ignore)]
        public Solid3DDefinition Solid3D { get; set; }
    }

    public sealed class ParameterDefinition
    {
        [JsonProperty("name")]   public string Name { get; set; }
        /// <summary>Text | Integer | Number | Length | YesNo | Material</summary>
        [JsonProperty("type")]   public string Type { get; set; } = "Text";
        [JsonProperty("shared")] public bool IsShared { get; set; }
        [JsonProperty("instance")] public bool IsInstance { get; set; } = true;
        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public string Default { get; set; }
    }

    public sealed class SymbolGeometry
    {
        [JsonProperty("lines", NullValueHandling = NullValueHandling.Ignore)]
        public List<LineDefinition> Lines { get; set; }

        [JsonProperty("arcs", NullValueHandling = NullValueHandling.Ignore)]
        public List<ArcDefinition> Arcs { get; set; }

        [JsonProperty("filledRegions", NullValueHandling = NullValueHandling.Ignore)]
        public List<FilledRegionDefinition> FilledRegions { get; set; }

        [JsonProperty("connectionLines", NullValueHandling = NullValueHandling.Ignore)]
        public List<LineDefinition> ConnectionLines { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public List<TextDefinition> Text { get; set; }
    }

    public sealed class LineDefinition
    {
        [JsonProperty("x1")] public double X1 { get; set; }
        [JsonProperty("y1")] public double Y1 { get; set; }
        [JsonProperty("x2")] public double X2 { get; set; }
        [JsonProperty("y2")] public double Y2 { get; set; }
        [JsonProperty("style", NullValueHandling = NullValueHandling.Ignore)]
        public string Style { get; set; }
    }

    public sealed class ArcDefinition
    {
        [JsonProperty("cx")]       public double Cx { get; set; }
        [JsonProperty("cy")]       public double Cy { get; set; }
        [JsonProperty("r")]        public double R { get; set; }
        [JsonProperty("startDeg")] public double StartDeg { get; set; } = 0;
        [JsonProperty("endDeg")]   public double EndDeg { get; set; } = 360;
        [JsonProperty("style", NullValueHandling = NullValueHandling.Ignore)]
        public string Style { get; set; }
    }

    public sealed class FilledRegionDefinition
    {
        [JsonProperty("boundary")] public List<Point2D> Boundary { get; set; }
            = new List<Point2D>();
        [JsonProperty("fillType", NullValueHandling = NullValueHandling.Ignore)]
        public string FillType { get; set; }
    }

    public sealed class Point2D
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
    }

    public sealed class TextDefinition
    {
        [JsonProperty("x")]      public double X { get; set; }
        [JsonProperty("y")]      public double Y { get; set; }
        [JsonProperty("value")]  public string Value { get; set; }
        [JsonProperty("heightMm")] public double HeightMm { get; set; } = 2.5;
    }

    public sealed class ConnectorDefinition
    {
        /// <summary>Piping | HVAC | Electrical | CableTray | Conduit</summary>
        [JsonProperty("domain")]      public string Domain { get; set; }
        [JsonProperty("systemType")]  public string SystemType { get; set; }
        /// <summary>Round | Rectangular | Oval</summary>
        [JsonProperty("shape")]       public string Shape { get; set; } = "Round";
        [JsonProperty("sizeMm")]      public double SizeMm { get; set; } = 25;
        [JsonProperty("widthMm")]     public double WidthMm { get; set; }
        [JsonProperty("heightMm")]    public double HeightMm { get; set; }
        /// <summary>In | Out | Bidirectional</summary>
        [JsonProperty("direction")]   public string Direction { get; set; } = "Bidirectional";
        [JsonProperty("offsetX")]     public double OffsetX { get; set; }
        [JsonProperty("offsetY")]     public double OffsetY { get; set; }
        [JsonProperty("offsetZ")]     public double OffsetZ { get; set; }
        /// <summary>Direction the connector faces — +X, -X, +Y, -Y, +Z, -Z.</summary>
        [JsonProperty("facing")]      public string Facing { get; set; } = "-X";
    }

    public sealed class Solid3DDefinition
    {
        /// <summary>Extrusion | Revolution | Cylinder | Box</summary>
        [JsonProperty("type")]     public string Type { get; set; } = "Box";
        [JsonProperty("heightMm")] public double HeightMm { get; set; } = 50;
        [JsonProperty("widthMm")]  public double WidthMm { get; set; }
        [JsonProperty("depthMm")]  public double DepthMm { get; set; }
        [JsonProperty("diameterMm")] public double DiameterMm { get; set; }
        [JsonProperty("profile", NullValueHandling = NullValueHandling.Ignore)]
        public List<Point2D> Profile { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────
    // Standards / Concepts / Profiles — Phase 175 expansion
    // ──────────────────────────────────────────────────────────────────

    public sealed class SymbolStandardsFile
    {
        [JsonProperty("version")]  public string Version { get; set; } = "1.0";
        [JsonProperty("standards")] public Dictionary<string, StandardDefinition> Standards { get; set; }
            = new Dictionary<string, StandardDefinition>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("fallbackChain")] public Dictionary<string, string> FallbackChain { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("scaleTierThresholds")] public Dictionary<string, int> ScaleTierThresholds { get; set; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class StandardDefinition
    {
        [JsonProperty("name")]               public string Name { get; set; }
        [JsonProperty("region")]             public List<string> Region { get; set; } = new List<string>();
        [JsonProperty("disciplines")]        public List<string> Disciplines { get; set; } = new List<string>();
        [JsonProperty("symbolSizeMm")]       public double SymbolSizeMm { get; set; } = 3.0;
        [JsonProperty("symbolScaleTiers")]   public Dictionary<string, double> SymbolScaleTiers { get; set; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("annotationRules")]    public AnnotationRules AnnotationRules { get; set; }
        [JsonProperty("lineWeightSymbol")]   public int LineWeightSymbol { get; set; } = 3;
        [JsonProperty("lineWeightConnection")] public int LineWeightConnection { get; set; } = 1;
        [JsonProperty("colorScheme")]        public string ColorScheme { get; set; } = "Monochrome";
    }

    public sealed class AnnotationRules
    {
        [JsonProperty("labelPosition")]      public string LabelPosition { get; set; } = "Above";
        [JsonProperty("textHeightMm")]       public double TextHeightMm { get; set; } = 2.0;
        [JsonProperty("leaderRequired")]     public bool LeaderRequired { get; set; }
        [JsonProperty("ratingFormat")]       public string RatingFormat { get; set; } = "{rating}{unit}";
        [JsonProperty("circuitRefPrefix")]   public string CircuitRefPrefix { get; set; } = "";
        [JsonProperty("circuitRefSuffix")]   public string CircuitRefSuffix { get; set; } = "";
    }

    public sealed class MixedStandardProfilesFile
    {
        [JsonProperty("version")]  public string Version { get; set; } = "1.0";
        [JsonProperty("profiles")] public List<MixedStandardProfile> Profiles { get; set; }
            = new List<MixedStandardProfile>();
    }

    public sealed class MixedStandardProfile
    {
        [JsonProperty("id")]                 public string Id { get; set; }
        [JsonProperty("name")]               public string Name { get; set; }
        [JsonProperty("disciplineMappings")] public Dictionary<string, string> DisciplineMappings { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("isDefault")]          public bool IsDefault { get; set; }
    }

    public sealed class ConceptsFile
    {
        [JsonProperty("version")]  public string Version { get; set; } = "1.0";
        [JsonProperty("concepts")] public Dictionary<string, SymbolConcept> Concepts { get; set; }
            = new Dictionary<string, SymbolConcept>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class SymbolConcept
    {
        [JsonProperty("conceptId")]       public string ConceptId { get; set; }
        [JsonProperty("name")]            public string Name { get; set; }
        [JsonProperty("discipline")]      public string Discipline { get; set; }
        [JsonProperty("subcategory")]     public string Subcategory { get; set; }
        [JsonProperty("revitCategory")]   public string RevitCategory { get; set; }
        /// <summary>Schematic | Model | Both</summary>
        [JsonProperty("universe")]        public string Universe { get; set; } = "Schematic";
        [JsonProperty("isViewDependent")] public bool IsViewDependent { get; set; }
        [JsonProperty("standardMappings")] public Dictionary<string, ConceptStandardMapping> StandardMappings { get; set; }
            = new Dictionary<string, ConceptStandardMapping>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("compoundComponents", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> CompoundComponents { get; set; }
        /// <summary>
        /// Optional multi-component rung definition for ladder-mode layout.
        /// Each <see cref="CompoundRung"/> places its components in series
        /// along one horizontal rung. When present, takes precedence over
        /// <see cref="CompoundComponents"/> in ladder mode; ignored by
        /// vertical-stack and horizontal-series modes (which use
        /// <see cref="CompoundComponents"/>).
        /// </summary>
        [JsonProperty("compoundRungs", NullValueHandling = NullValueHandling.Ignore)]
        public List<CompoundRung> CompoundRungs { get; set; }
        [JsonProperty("connectorDomain", NullValueHandling = NullValueHandling.Ignore)]
        public string ConnectorDomain { get; set; }
        [JsonProperty("orientationStates", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> OrientationStates { get; set; }
    }

    /// <summary>
    /// One horizontal rung in a ladder-style compound layout. Components
    /// are placed left-to-right in <see cref="Components"/> order, in
    /// series between the supply and neutral rails.
    /// </summary>
    public sealed class CompoundRung
    {
        [JsonProperty("components")] public List<string> Components { get; set; }
            = new List<string>();
        /// <summary>Optional human-readable label drawn next to the rung.</summary>
        [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)]
        public string Label { get; set; }
    }

    public sealed class ConceptStandardMapping
    {
        [JsonProperty("genericAnnotation", NullValueHandling = NullValueHandling.Ignore)]
        public string GenericAnnotation { get; set; }
        [JsonProperty("tagFamily", NullValueHandling = NullValueHandling.Ignore)]
        public string TagFamily { get; set; }
        [JsonProperty("viewContextOverrides", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> ViewContextOverrides { get; set; }
        [JsonProperty("scaleVariants", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> ScaleVariants { get; set; }
    }
}
