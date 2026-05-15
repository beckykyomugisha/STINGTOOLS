#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

// ─────────────────────────────────────────────────────────────────────────────
//  ArchiCAD → Revit IFC Import System
//
//  Root causes of misalignment — and their fixes:
//
//  1. COORDINATE SYSTEM MISMATCH
//     IfcLocalPlacement arg 0 = PlacementRelTo (parent), arg 1 = RelativePlacement.
//     We walk the full chain, compose 4×4 matrices correctly (rotation + translation),
//     subtract the accumulated site origin, invert the site rotation, and convert
//     IFC length-units → Revit internal feet using the file's IfcUnitAssignment.
//
//  2. LEVEL ELEVATION MISMATCH
//     IfcBuildingStorey.Elevation is relative to the building base, not absolute.
//     We use the storey's ObjectPlacement chain Z (absolute world Z) instead,
//     converted via the same unit scale, then match ±5 mm against Revit Levels.
//
//  3. ELEMENT GEOMETRY IS NEVER POPULATED
//     Every element's ObjectPlacement gives its world-space insertion point.
//     For walls we extract the IfcWallAxis / IfcExtrudedAreaSolid sweep path.
//     For all others we harvest an IfcBoundingBox if present; otherwise we use
//     the insertion point as a single vertex so DirectShape can at least mark
//     the element location.
//
//  4. ELEMENT TYPE / PROPERTY FIDELITY
//     JSON-driven ARCHICAD_IFC_MAPPING.json maps 40 IfcType/PredefinedType
//     combinations. ArchiCAD Psets are written to STING shared params + Revit
//     built-ins via ArchiCadPropertyMapper.
// ─────────────────────────────────────────────────────────────────────────────

namespace StingTools.Commands.Interop
{
    // =========================================================================
    //  4×4 homogeneous transform (column-major, right-handed, IFC convention)
    // =========================================================================

    public struct Mat4
    {
        // Column vectors: X, Y, Z, T (translation)
        public double Xx, Yx, Zx, Tx;
        public double Xy, Yy, Zy, Ty;
        public double Xz, Yz, Zz, Tz;

        public static Mat4 Identity() => new Mat4
        { Xx = 1, Yy = 1, Zz = 1 };

        public static Mat4 FromPlacement(double[] loc, double[] axisZ, double[] refX)
        {
            // Y = Z × X
            double[] Z = Normalise(axisZ);
            double[] X = Normalise(refX);
            double[] Y = Cross(Z, X);

            return new Mat4
            {
                Xx = X[0], Yx = Y[0], Zx = Z[0], Tx = loc[0],
                Xy = X[1], Yy = Y[1], Zy = Z[1], Ty = loc[1],
                Xz = X[2], Yz = Y[2], Zz = Z[2], Tz = loc[2]
            };
        }

        // parent * local   (apply local first, then parent)
        public static Mat4 Compose(Mat4 p, Mat4 l) => new Mat4
        {
            Xx = p.Xx*l.Xx + p.Yx*l.Xy + p.Zx*l.Xz,
            Yx = p.Xx*l.Yx + p.Yx*l.Yy + p.Zx*l.Yz,
            Zx = p.Xx*l.Zx + p.Yx*l.Zy + p.Zx*l.Zz,
            Tx = p.Xx*l.Tx + p.Yx*l.Ty + p.Zx*l.Tz + p.Tx,

            Xy = p.Xy*l.Xx + p.Yy*l.Xy + p.Zy*l.Xz,
            Yy = p.Xy*l.Yx + p.Yy*l.Yy + p.Zy*l.Yz,
            Zy = p.Xy*l.Zx + p.Yy*l.Zy + p.Zy*l.Zz,
            Ty = p.Xy*l.Tx + p.Yy*l.Ty + p.Zy*l.Tz + p.Ty,

            Xz = p.Xz*l.Xx + p.Yz*l.Xy + p.Zz*l.Xz,
            Yz = p.Xz*l.Yx + p.Yz*l.Yy + p.Zz*l.Yz,
            Zz = p.Xz*l.Zx + p.Yz*l.Zy + p.Zz*l.Zz,
            Tz = p.Xz*l.Tx + p.Yz*l.Ty + p.Zz*l.Tz + p.Tz
        };

        public double[] TransformPoint(double x, double y, double z) =>
            new[] { Xx*x + Yx*y + Zx*z + Tx,
                    Xy*x + Yy*y + Zy*z + Ty,
                    Xz*x + Yz*y + Zz*z + Tz };

        public double[] TranslationOnly() => new[] { Tx, Ty, Tz };

        private static double[] Normalise(double[] v)
        {
            double len = Math.Sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2]);
            return len < 1e-10 ? new[] { 1.0, 0.0, 0.0 } : new[] { v[0]/len, v[1]/len, v[2]/len };
        }

        private static double[] Cross(double[] a, double[] b) =>
            new[] { a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0] };
    }

    // =========================================================================
    //  Data model
    // =========================================================================

    public sealed class AcIfcEntity
    {
        public int    Id   { get; set; }
        public string Type { get; set; } = "";
        public string Raw  { get; set; } = "";
    }

    public sealed class AcIfcStorey
    {
        public int    Id             { get; set; }
        public string GlobalId       { get; set; } = "";
        public string Name           { get; set; } = "";
        public double AbsoluteElevM  { get; set; }   // from placement chain Z (authoritative)
        public long   RevitLevelId   { get; set; } = -1;
        public bool   WasCreated     { get; set; }   // true if a new Revit level was created
    }

    public sealed class AcIfcElement
    {
        public int    Id              { get; set; }
        public string IfcType         { get; set; } = "";
        public string GlobalId        { get; set; } = "";
        public string Name            { get; set; } = "";
        public string ObjectType      { get; set; } = "";
        public string PredefinedType  { get; set; } = "";
        public int    StoreyId        { get; set; }
        /// <summary>World-space insertion point in IFC units (metres by default).</summary>
        public double[] InsertionPoint { get; set; } = { 0, 0, 0 };
        /// <summary>World-space transform matrix from the placement chain.</summary>
        public Mat4   WorldTransform   { get; set; } = Mat4.Identity();
        /// <summary>Axis start/end points for linear elements (walls, beams) in IFC units.</summary>
        public double[]? AxisStart    { get; set; }
        public double[]? AxisEnd      { get; set; }
        /// <summary>Bounding box half-extents in local coordinates (IFC units).</summary>
        public double BboxHalfX       { get; set; }
        public double BboxHalfY       { get; set; }
        public double Height          { get; set; }   // IFC units
        // Tessellated triangles in IFC WORLD space (pre-transformed through WorldTransform).
        // Each entry is a flat array of 9 doubles = (x0,y0,z0, x1,y1,z1, x2,y2,z2).
        public List<double[]> BrepTriangles { get; } = new();
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>True when geometry was resolved via AABB fallback (BRep was unsupported).</summary>
        public bool GeometryIsAabbFallback { get; set; }
    }

    // =========================================================================
    //  JSON mapping configuration
    // =========================================================================

    public sealed class AcIfcTypeMapping
    {
        [JsonProperty("ifc_type")]          public string IfcType         { get; set; } = "";
        [JsonProperty("predefined_type")]   public string PredefinedType  { get; set; } = "";
        [JsonProperty("revit_category")]    public string RevitCategory   { get; set; } = "";
        [JsonProperty("revit_family_hint")] public string RevitFamilyHint { get; set; } = "";
        [JsonProperty("use_direct_shape")]  public bool   UseDirectShape  { get; set; }
        [JsonProperty("notes")]             public string Notes           { get; set; } = "";
    }

    public sealed class AcIfcPropMapping
    {
        [JsonProperty("archicad_pset")]   public string ArchiCadPset  { get; set; } = "";
        [JsonProperty("archicad_prop")]   public string ArchiCadProp  { get; set; } = "";
        [JsonProperty("sting_param")]     public string StingParam    { get; set; } = "";
        [JsonProperty("revit_builtin")]   public string RevitBuiltIn  { get; set; } = "";
        [JsonProperty("notes")]           public string Notes         { get; set; } = "";
        /// <summary>When non-empty, mapping only applies to IFC elements whose IfcType is in this list.</summary>
        [JsonProperty("element_types")]   public List<string> ElementTypes { get; set; } = new();
    }

    public sealed class AcIfcMappingConfig
    {
        [JsonProperty("version")]           public string Version        { get; set; } = "";
        [JsonProperty("type_mappings")]     public List<AcIfcTypeMapping> TypeMappings  { get; set; } = new();
        [JsonProperty("property_mappings")] public List<AcIfcPropMapping> PropMappings  { get; set; } = new();
    }

    // =========================================================================
    //  ArchiCAD IFC STEP parser  (full placement chain + geometry extraction)
    // =========================================================================

    public sealed class ArchiCadIfcParser
    {
        // ── public results ────────────────────────────────────────────────────
        public string ProjectName       { get; private set; } = "";
        /// <summary>World-space transform for the site (used as the coordinate origin).</summary>
        public Mat4   SiteWorldTransform { get; private set; } = Mat4.Identity();
        /// <summary>Scale: IFC length units → metres (1.0 for metres, 0.001 for mm, etc.).</summary>
        public double UnitScale         { get; private set; } = 1.0;
        public List<AcIfcStorey>  Storeys  { get; } = new();
        public List<AcIfcElement> Elements { get; } = new();
        public List<string>       Warnings { get; } = new();

        // ── internal tables ───────────────────────────────────────────────────
        private readonly Dictionary<int, AcIfcEntity> _e         = new();
        private readonly Dictionary<int, Dictionary<string,string>> _psets = new();
        private readonly Dictionary<int, List<int>>   _relDef    = new();
        private readonly Dictionary<int, int>         _relContain= new();
        // element id → absolute world transform (cached during geometry extraction)
        private readonly Dictionary<int, Mat4>        _worldXf   = new();
        // type-object id → list of pset ids (populated from IfcTypeObject relationships)
        private readonly Dictionary<int, List<int>>   _typeObjPsets = new();
        // element id → type-object id (populated from IfcRelDefinesByType)
        private readonly Dictionary<int, int>         _relDefByType = new();
        // element id → predominant material name from IfcMaterialLayerSet
        private readonly Dictionary<int, string>      _predominantMaterial = new();
        // element id → comma-separated layer composition string e.g. "Brick(150mm), Insulation(50mm)"
        private readonly Dictionary<int, string>      _layerComposition    = new();
        // element/space id → zone name from IfcZone (via IfcRelAssignsToGroup)
        private readonly Dictionary<int, string>      _zoneAssignment      = new();
        // spatial id → parent spatial id (building→storey→space via IfcRelAggregates)
        private readonly Dictionary<int, int>         _spatialParent       = new();
        // spatial id → name (for building/storey/space name lookup)
        private readonly Dictionary<int, string>      _spatialName         = new();
        // element id → enclosing IfcSpace id (from IfcRelContainedInSpatialStructure for spaces)
        private readonly Dictionary<int, int>         _elementSpace        = new();
        /// <summary>True if any IfcElementQuantity entities were found in the file.</summary>
        public bool HasQuantitySets { get; private set; }

        // ── entry point ───────────────────────────────────────────────────────
        public static ArchiCadIfcParser ParseFile(string path)
        {
            var p = new ArchiCadIfcParser();
            if (!File.Exists(path)) { p.Warnings.Add("File not found: " + path); return p; }
            try   { p.ParseInternal(File.ReadAllText(path)); }
            catch (Exception ex) { p.Warnings.Add("Parse error: " + ex.Message); }
            return p;
        }

        // ── flatten multi-line STEP ───────────────────────────────────────────
        private static string Flatten(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool inString = false;
            foreach (char c in text)
            {
                if (c == '\'') inString = !inString;
                if (!inString && (c == '\r' || c == '\n')) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static readonly Regex RxEntity = new(
            @"#(\d+)\s*=\s*([A-Z][A-Z0-9_]*)\s*\(([^;]*)\)\s*;",
            RegexOptions.Compiled);

        private void ParseInternal(string text)
        {
            string flat = Flatten(text);
            foreach (Match m in RxEntity.Matches(flat))
            {
                int id = int.Parse(m.Groups[1].Value);
                _e[id] = new AcIfcEntity
                {
                    Id   = id,
                    Type = m.Groups[2].Value.ToUpperInvariant(),
                    Raw  = m.Groups[3].Value
                };
            }

            ResolveUnits();
            ResolveProject();
            ResolveSite();
            ResolveStoreys();
            ResolvePropertySets();
            ResolveQuantitySets();
            ResolveRelDefinesByProperties();
            ResolveRelDefinesByType();
            ResolveRelAssociatesMaterial();
            ResolveRelAggregates();
            ResolveRelContainedInSpatialStructure();
            ResolveZoneAssignments();
            ResolveRelVoidsElement();
            ResolveElements();
            MergePropertiesIntoElements();
            MergeMaterialLayerProperties();
            AssignStoreyToElements();
            AssignSpatialHierarchy();
            ComputeDerivedQuantities();
            PopulateStingTokens();
            ValidateDataQuality();
        }

        // ── IFC unit scale ────────────────────────────────────────────────────
        private void ResolveUnits()
        {
            // IfcProject.UnitsInContext → IfcUnitAssignment → units (SET)
            var proj = _e.Values.FirstOrDefault(e => e.Type == "IFCPROJECT");
            if (proj == null) return;

            int unitsRef = ExtractRef(proj.Raw, 8); // arg 8 = UnitsInContext
            if (!_e.TryGetValue(unitsRef, out var ua)) return;
            // IfcUnitAssignment: arg 0 = Units (SET)
            foreach (int uid in ExtractRefList(ua.Raw, 0))
            {
                if (!_e.TryGetValue(uid, out var u)) continue;
                if (u.Type != "IFCSIUNIT" && u.Type != "IFCCONVERSIONBASEDUNIT") continue;
                var parts = SplitArgs(u.Raw);
                // IfcSIUnit: UnitType = arg 1 (e.g. .LENGTHUNIT.)
                if (parts.Count < 2) continue;
                string unitType = parts[1].Trim('.').Trim();
                if (!unitType.Equals("LENGTHUNIT", StringComparison.OrdinalIgnoreCase)) continue;

                // Prefix = arg 0: .MILLI. → 0.001, blank or .NONE. → 1.0
                string prefix = parts[0].Trim().Trim('.');
                UnitScale = prefix.Equals("MILLI", StringComparison.OrdinalIgnoreCase) ? 0.001
                           : prefix.Equals("CENTI", StringComparison.OrdinalIgnoreCase) ? 0.01
                           : 1.0;
                break;
            }
        }

        // ── Project name ──────────────────────────────────────────────────────
        private void ResolveProject()
        {
            var proj = _e.Values.FirstOrDefault(e => e.Type == "IFCPROJECT");
            if (proj != null) ProjectName = ExtractString(proj.Raw, 2);
        }

        // ── Site world transform (coordinate origin for the whole file) ───────
        private void ResolveSite()
        {
            var site = _e.Values.FirstOrDefault(e => e.Type == "IFCSITE");
            if (site == null) return;
            int placRef = ExtractRef(site.Raw, 5); // arg 5 = ObjectPlacement
            SiteWorldTransform = ResolveWorldTransform(placRef);
        }

        // ── Storeys — use placement chain Z, not the Elevation attribute ──────
        private void ResolveStoreys()
        {
            foreach (var e in _e.Values.Where(e => e.Type == "IFCBUILDINGSTOREY"))
            {
                int placRef = ExtractRef(e.Raw, 5); // arg 5 = ObjectPlacement
                Mat4 xf     = ResolveWorldTransform(placRef);
                double absZ = xf.Tz; // absolute Z in IFC units

                Storeys.Add(new AcIfcStorey
                {
                    Id            = e.Id,
                    GlobalId      = ExtractString(e.Raw, 0),
                    Name          = ExtractString(e.Raw, 2),
                    AbsoluteElevM = absZ * UnitScale
                });
            }
        }

        // ── Property sets ─────────────────────────────────────────────────────
        private void ResolvePropertySets()
        {
            foreach (var e in _e.Values.Where(e => e.Type == "IFCPROPERTYSET"))
            {
                string psetName = ExtractString(e.Raw, 2); // arg 2 = Name
                var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (int pref in ExtractRefList(e.Raw, 4)) // arg 4 = HasProperties
                {
                    if (!_e.TryGetValue(pref, out var pv)) continue;
                    if (pv.Type != "IFCPROPERTYSINGLEVALUE") continue;
                    string propName = ExtractString(pv.Raw, 0);
                    string rawVal   = ExtractNominalValue(pv.Raw);
                    if (!string.IsNullOrEmpty(propName))
                        props[$"{psetName}.{propName}"] = rawVal;
                }
                _psets[e.Id] = props;
            }
        }

        // ── Quantity sets (Change 1) ──────────────────────────────────────────
        private void ResolveQuantitySets()
        {
            foreach (var e in _e.Values.Where(e => e.Type == "IFCELEMENTQUANTITY"))
            {
                HasQuantitySets = true;
                string qsetName = ExtractString(e.Raw, 2); // arg 2 = Name
                var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (int qref in ExtractRefList(e.Raw, 5)) // arg 5 = Quantities
                {
                    if (!_e.TryGetValue(qref, out var qv)) continue;
                    string qName = ExtractString(qv.Raw, 0);
                    if (string.IsNullOrEmpty(qName)) continue;
                    string qVal = "";
                    try
                    {
                        switch (qv.Type)
                        {
                            case "IFCQUANTITYLENGTH":
                            case "IFCQUANTITYAREA":
                            case "IFCQUANTITYVOLUME":
                            case "IFCQUANTITYWEIGHT":
                            case "IFCQUANTITYCOUNT":
                                var qparts = SplitArgs(qv.Raw);
                                if (qparts.Count > 2) qVal = qparts[2].Trim();
                                break;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"QuantityParse {qv.Type}: {ex.Message}"); }
                    if (!string.IsNullOrEmpty(qVal))
                        props[$"{qsetName}.{qName}"] = qVal;
                }
                _psets[e.Id] = props;
            }
        }

        private void ResolveRelDefinesByProperties()
        {
            foreach (var e in _e.Values.Where(e => e.Type == "IFCRELDEFINESBYPROPERTIES"))
            {
                var elemRefs = ExtractRefList(e.Raw, 4); // arg 4 = RelatedObjects
                int psetRef  = ExtractRef(e.Raw, 5);     // arg 5 = RelatingPropertyDefinition
                if (!_psets.ContainsKey(psetRef)) continue;
                foreach (int eid in elemRefs)
                {
                    if (!_relDef.TryGetValue(eid, out var list))
                        _relDef[eid] = list = new();
                    list.Add(psetRef);
                }
            }
        }

        // ── IfcRelDefinesByType (Change 2) ────────────────────────────────────
        //  Merges type-level Psets onto each instance; instance values win.
        private void ResolveRelDefinesByType()
        {
            try
            {
                // First, collect Pset references attached to each type object via
                // IfcRelDefinesByProperties where the related object is a type object.
                // Simpler: we gather directly from IfcRelDefinesByType entries.
                foreach (var e in _e.Values.Where(e => e.Type == "IFCRELDEFINESBYTYPE"))
                {
                    var instanceRefs = ExtractRefList(e.Raw, 4); // arg 4 = RelatedObjects
                    int typeRef      = ExtractRef(e.Raw, 5);     // arg 5 = RelatingType
                    if (typeRef <= 0) continue;
                    foreach (int eid in instanceRefs)
                        _relDefByType[eid] = typeRef;
                }

                // Collect Psets that belong to type objects (via IfcRelDefinesByProperties
                // where the related object is a type-class entity).
                foreach (var e in _e.Values.Where(e => e.Type == "IFCRELDEFINESBYPROPERTIES"))
                {
                    int psetRef = ExtractRef(e.Raw, 5);
                    if (!_psets.ContainsKey(psetRef)) continue;
                    foreach (int eid in ExtractRefList(e.Raw, 4))
                    {
                        if (!_e.TryGetValue(eid, out var ent)) continue;
                        // Type-class entities start with IfcWallType, IfcSlabType, etc.
                        if (!ent.Type.EndsWith("TYPE", StringComparison.OrdinalIgnoreCase)
                            && !ent.Type.EndsWith("STYLE", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!_typeObjPsets.TryGetValue(eid, out var tlist))
                            _typeObjPsets[eid] = tlist = new();
                        tlist.Add(psetRef);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn("ResolveRelDefinesByType: " + ex.Message); }
        }

        // ── Material layer set (Change 6) ─────────────────────────────────────
        private void ResolveRelAssociatesMaterial()
        {
            try
            {
                foreach (var e in _e.Values.Where(e => e.Type == "IFCRELASSOCIATESMATERIAL"))
                {
                    var elemRefs  = ExtractRefList(e.Raw, 4); // arg 4 = RelatedObjects
                    int matRelRef = ExtractRef(e.Raw, 5);     // arg 5 = RelatingMaterial
                    if (!_e.TryGetValue(matRelRef, out var matRel)) continue;

                    int layerSetRef = -1;
                    if (matRel.Type == "IFCMATERIALLAYERSETUSAGE")
                        layerSetRef = ExtractRef(matRel.Raw, 0); // arg 0 = ForLayerSet
                    else if (matRel.Type == "IFCMATERIALLAYERSET")
                        layerSetRef = matRel.Id;

                    if (layerSetRef <= 0 || !_e.TryGetValue(layerSetRef, out var layerSet)) continue;

                    // IfcMaterialLayerSet: arg 0 = MaterialLayers (LIST of IfcMaterialLayer)
                    double maxThick = -1;
                    string matName = "";
                    var layerParts = new List<string>();
                    foreach (int layRef in ExtractRefList(layerSet.Raw, 0))
                    {
                        if (!_e.TryGetValue(layRef, out var layer) || layer.Type != "IFCMATERIALLAYER") continue;
                        int matRef = ExtractRef(layer.Raw, 0); // arg 0 = Material (IfcMaterial)
                        var thickParts = SplitArgs(layer.Raw);
                        double thick = 0;
                        if (thickParts.Count > 1)
                            double.TryParse(thickParts[1].Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out thick);
                        string layerMatName = "";
                        if (_e.TryGetValue(matRef, out var mat))
                            layerMatName = ExtractString(mat.Raw, 0); // IfcMaterial.Name = arg 0
                        if (!string.IsNullOrEmpty(layerMatName))
                        {
                            // thickness is in IFC length units; convert to mm for the label
                            double thickMm = thick * UnitScale * 1000.0;
                            layerParts.Add($"{layerMatName}({(int)Math.Round(thickMm)}mm)");
                        }
                        if (thick > maxThick)
                        {
                            maxThick = thick;
                            matName = layerMatName;
                        }
                    }
                    if (!string.IsNullOrEmpty(matName))
                        foreach (int eid in elemRefs)
                            _predominantMaterial[eid] = matName;
                    if (layerParts.Count > 0)
                    {
                        string composition = string.Join(", ", layerParts);
                        foreach (int eid in elemRefs)
                            _layerComposition[eid] = composition;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn("ResolveRelAssociatesMaterial: " + ex.Message); }
        }

        private void ResolveRelContainedInSpatialStructure()
        {
            foreach (var e in _e.Values.Where(e => e.Type == "IFCRELCONTAINEDINSPATIALSTRUCTURE"))
            {
                var elemRefs   = ExtractRefList(e.Raw, 4); // arg 4 = RelatedElements
                int spatialRef = ExtractRef(e.Raw, 5);     // arg 5 = RelatingStructure
                foreach (int eid in elemRefs)
                    _relContain[eid] = spatialRef;
            }
        }

        private void ResolveZoneAssignments()
        {
            try
            {
                foreach (var e in _e.Values.Where(e => e.Type == "IFCRELASSIGNSTOGROUP"))
                {
                    int groupRef = ExtractRef(e.Raw, 6); // arg 6 = RelatingGroup
                    if (!_e.TryGetValue(groupRef, out var group) || group.Type != "IFCZONE") continue;
                    // IfcZone inherits IfcRoot: arg 0=GlobalId, arg 1=OwnerHistory, arg 2=Name
                    string zoneName = ExtractString(group.Raw, 2);
                    if (string.IsNullOrEmpty(zoneName)) continue;
                    foreach (int objId in ExtractRefList(e.Raw, 4)) // arg 4 = RelatedObjects
                        _zoneAssignment[objId] = zoneName;
                }
            }
            catch (Exception ex) { StingLog.Warn("ResolveZoneAssignments: " + ex.Message); }
        }

        // ── Spatial containment hierarchy (IfcRelAggregates) ─────────────────
        //  Builds building→storey→zone→space parent chain so we can walk UP from
        //  any element and emit IfcHierarchy.Building / Storey / Space properties.
        private static readonly HashSet<string> _spatialEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "IFCBUILDING","IFCBUILDINGSTOREY","IFCSPACE","IFCZONE",
            "IFCSITE","IFCPROJECT","IFCEXTERNALSPATIALELEMENT"
        };

        private void ResolveRelAggregates()
        {
            try
            {
                foreach (var e in _e.Values.Where(e => _spatialEntityTypes.Contains(e.Type)))
                foreach (var e in _e.Values.Where(e => SpatialTypes.Contains(e.Type)))
                    _spatialName[e.Id] = ExtractString(e.Raw, 2); // arg 2 = Name on all IfcRoot subclasses

                // IfcRelAggregates: arg 4 = RelatingObject (parent), arg 5 = RelatedObjects (children)
                foreach (var e in _e.Values.Where(e => e.Type == "IFCRELAGGREGATES"))
                {
                    int parentRef = ExtractRef(e.Raw, 4);
                    if (parentRef <= 0) continue;
                    foreach (int childRef in ExtractRefList(e.Raw, 5))
                        _spatialParent[childRef] = parentRef;
                }

                // Also map IfcSpace membership from IfcRelContainedInSpatialStructure
                // where the relating structure is an IfcSpace (elements inside rooms).
                foreach (var e in _e.Values.Where(e => e.Type == "IFCRELCONTAINEDINSPATIALSTRUCTURE"))
                {
                    int spatialRef = ExtractRef(e.Raw, 5);
                    if (!_e.TryGetValue(spatialRef, out var spatial)) continue;
                    if (!spatial.Type.Equals("IFCSPACE", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (int eid in ExtractRefList(e.Raw, 4))
                        _elementSpace[eid] = spatialRef;
                }
            }
            catch (Exception ex) { StingLog.Warn("ResolveRelAggregates: " + ex.Message); }
        }

        // Walk the _spatialParent chain from a given id, collecting ancestors by IFC type.
        private string? WalkToType(int startId, string targetType)
        {
            int cur = startId;
            int safety = 20;
            while (cur > 0 && safety-- > 0)
            {
                if (_e.TryGetValue(cur, out var ent)
                    && ent.Type.Equals(targetType, StringComparison.OrdinalIgnoreCase))
                    return _spatialName.TryGetValue(cur, out string? n) ? n : null;
                if (!_spatialParent.TryGetValue(cur, out int parent)) break;
                cur = parent;
            }
            return null;
        }

        // ── Assign building/storey/space hierarchy to element Properties ──────
        private void AssignSpatialHierarchy()
        {
            foreach (var el in Elements)
            {
                // Start from the direct spatial container (storey from _relContain)
                int spatialBase = _relContain.TryGetValue(el.Id, out int s) ? s : 0;

                // Building name
                string? bldName = spatialBase > 0 ? WalkToType(spatialBase, "IFCBUILDING") : null;
                if (!string.IsNullOrEmpty(bldName))
                    el.Properties["IfcHierarchy.Building"] = bldName!;

                // Storey name (already set via el.StoreyId; use _spatialName for consistency)
                if (el.StoreyId > 0 && _spatialName.TryGetValue(el.StoreyId, out string? storeyName)
                    && !string.IsNullOrEmpty(storeyName))
                    el.Properties["IfcHierarchy.Storey"] = storeyName;

                // Enclosing space / room name
                if (_elementSpace.TryGetValue(el.Id, out int spaceId)
                    && _spatialName.TryGetValue(spaceId, out string? spaceName)
                    && !string.IsNullOrEmpty(spaceName))
                {
                    el.Properties["IfcHierarchy.Space"] = spaceName;
                    // Also expose space's psets for room-level data (e.g. AC_Pset_ZoneCategory)
                    if (_relDef.TryGetValue(spaceId, out var spacePsets))
                    {
                        foreach (int pid in spacePsets)
                        {
                            if (!_psets.TryGetValue(pid, out var spaceProps)) continue;
                            foreach (var kv in spaceProps)
                                if (!el.Properties.ContainsKey(kv.Key))
                                    el.Properties[kv.Key] = kv.Value;
                        }
                    }
                }
            }
        }

        // ── Derived quantity calculations ─────────────────────────────────────
        //  Computes net quantities by subtracting opening areas/volumes from gross,
        //  and derives cost totals from quantity × unit rate when both are present.
        private void ComputeDerivedQuantities()
        {
            // Build opening area index: host wall id → total area of all its openings
            // We use the opening elements' gross area from their BaseQuantities psets.
            var openingAreaByHost = new Dictionary<int, double>();
            foreach (var el in Elements.Where(e =>
                e.IfcType.Equals("IFCOPENINGELEMENT", StringComparison.OrdinalIgnoreCase)))
            {
                if (!_openingHost.TryGetValue(el.Id, out int hostId)) continue;
                // Try several quantity key patterns ArchiCAD uses
                double area = TryGetQuantityM2(el, "Qto_OpeningElementBaseQuantities.Area")
                           ?? TryGetQuantityM2(el, "BaseQuantities.CrossSectionArea")
                           ?? TryGetQuantityM2(el, "BaseQuantities.Area")
                           ?? 0.0;
                if (area > 0)
                    openingAreaByHost[hostId] = openingAreaByHost.GetValueOrDefault(hostId) + area;
            }

            foreach (var el in Elements)
            {
                // ── Net side area for walls ────────────────────────────────────
                double? grossSide = TryGetQuantityM2(el, "Qto_WallBaseQuantities.GrossSideArea")
                                 ?? TryGetQuantityM2(el, "BaseQuantities.GrossSideArea");
                if (grossSide.HasValue && openingAreaByHost.TryGetValue(el.Id, out double oArea) && oArea > 0)
                {
                    double net = Math.Max(0, grossSide.Value - oArea);
                    el.Properties["DerivedQty.NetSideArea_m2"] = net.ToString("F4", CultureInfo.InvariantCulture);
                }

                // ── Net floor area for slabs/spaces ───────────────────────────
                double? grossFloor = TryGetQuantityM2(el, "Qto_SlabBaseQuantities.GrossArea")
                                  ?? TryGetQuantityM2(el, "Qto_SpaceBaseQuantities.GrossFloorArea")
                                  ?? TryGetQuantityM2(el, "BaseQuantities.GrossArea");
                if (grossFloor.HasValue)
                {
                    // Net = Gross for floors/spaces (openings don't subtract floor area)
                    el.Properties["DerivedQty.NetFloorArea_m2"] = grossFloor.Value.ToString("F4", CultureInfo.InvariantCulture);
                }

                // ── Net volume ────────────────────────────────────────────────
                double? grossVol = TryGetQuantityM3(el, "Qto_WallBaseQuantities.GrossVolume")
                                ?? TryGetQuantityM3(el, "Qto_SlabBaseQuantities.GrossVolume")
                                ?? TryGetQuantityM3(el, "BaseQuantities.GrossVolume");
                if (grossVol.HasValue)
                {
                    // Approximate void volume as opening area × average wall thickness
                    double voidVol = 0;
                    if (openingAreaByHost.TryGetValue(el.Id, out double oa))
                    {
                        double? thick = TryGetQuantityM(el, "Qto_WallBaseQuantities.Width")
                                     ?? TryGetQuantityM(el, "BaseQuantities.Width");
                        if (thick.HasValue && thick.Value > 0)
                            voidVol = oa * thick.Value;
                    }
                    double netVol = Math.Max(0, grossVol.Value - voidVol);
                    el.Properties["DerivedQty.NetVolume_m3"] = netVol.ToString("F4", CultureInfo.InvariantCulture);
                }

                // ── Cost total = unit rate × primary quantity ─────────────────
                if (el.Properties.TryGetValue("CST_UNIT_RATE_TXT", out string? rateStr)
                    && double.TryParse(rateStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)
                    && rate > 0)
                {
                    // Pick the best quantity for costing: floor area > side area > volume
                    double? qty = grossFloor ?? grossSide ?? grossVol;
                    if (qty.HasValue && qty.Value > 0)
                    {
                        double total = rate * qty.Value;
                        el.Properties["DerivedQty.CostTotal"] = total.ToString("F2", CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        // Helper: parse a named quantity (area in m²) from an element's merged properties.
        // The key is "QsetName.QuantityName". UnitScale converts IFC units → metres.
        private double? TryGetQuantityM2(AcIfcElement el, string key)
        {
            if (!el.Properties.TryGetValue(key, out string? v)) return null;
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw)) return null;
            // IFC areas are in length-unit² → multiply by UnitScale²
            return raw * UnitScale * UnitScale;
        }

        private double? TryGetQuantityM3(AcIfcElement el, string key)
        {
            if (!el.Properties.TryGetValue(key, out string? v)) return null;
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw)) return null;
            return raw * UnitScale * UnitScale * UnitScale;
        }

        private double? TryGetQuantityM(AcIfcElement el, string key)
        {
            if (!el.Properties.TryGetValue(key, out string? v)) return null;
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw)) return null;
            return raw * UnitScale;
        }

        // ── Opening host relationships (IfcRelVoidsElement) ──────────────────
        private void ResolveRelVoidsElement()
        {
            try
            {
                foreach (var e in _e.Values.Where(e => e.Type == "IFCRELVOIDSELEMENT"))
                {
                    int hostRef    = ExtractRef(e.Raw, 4); // arg 4 = RelatingBuildingElement
                    int openingRef = ExtractRef(e.Raw, 5); // arg 5 = RelatedOpeningElement
                    if (hostRef > 0 && openingRef > 0)
                        _openingHost[openingRef] = hostRef;
                }
            }
            catch (Exception ex) { StingLog.Warn("ResolveRelVoidsElement: " + ex.Message); }
        }

        // ── STING token auto-population from parsed IFC data ─────────────────
        //  Runs after all merges so every element's Properties bag is complete.
        //  Writes synthetic "STING.*" keys that ArchiCadPropertyMapper can then
        //  write to the corresponding Revit shared-parameter at apply time.
        private static readonly Dictionary<string,string> _renovMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "New",                "NEW"         },
                { "Existing",           "EXISTING"    },
                { "Demolished",         "DEMOLISHED"  },
                { "ToBeReconstructed",  "NEW"         },
                { "ToBeRetrofitted",    "EXISTING"    },
                { "ToBeRemoved",        "DEMOLISHED"  },
                { "ToBeReplaced",       "DEMOLISHED"  },
                { "Temporary",          "TEMPORARY"   },
            };

        private void PopulateStingTokens()
        {
            var storeyById = Storeys.ToDictionary(s => s.Id, s => s);

            foreach (var el in Elements)
            {
                // ── STING.STATUS from AC_Pset_RenovationInfo.RenovationStatus ──
                if (el.Properties.TryGetValue("AC_Pset_RenovationInfo.RenovationStatus", out string? renovRaw)
                    && !string.IsNullOrEmpty(renovRaw))
                {
                    if (_renovMap.TryGetValue(renovRaw.Trim('.'), out string? status))
                        el.Properties["STING.STATUS"] = status;
                    else
                        el.Properties["STING.STATUS"] = "EXISTING";
                }
                else if (!el.Properties.ContainsKey("STING.STATUS"))
                {
                    el.Properties["STING.STATUS"] = "NEW";
                }

                // ── STING.LVL from storey name ────────────────────────────────
                if (el.StoreyId > 0 && storeyById.TryGetValue(el.StoreyId, out var storey))
                {
                    el.Properties["STING.LVL"] = DeriveLevel(storey.Name, storey.AbsoluteElevM);
                }

                // ── STING.ZONE from IfcZone assignment ───────────────────────
                if (el.Properties.TryGetValue("IfcZone.Name", out string? zone)
                    && !string.IsNullOrEmpty(zone)
                    && !el.Properties.ContainsKey("STING.ZONE"))
                {
                    el.Properties["STING.ZONE"] = SanitiseZoneCode(zone);
                }

                // ── STING.DISC from IfcType ───────────────────────────────────
                if (!el.Properties.ContainsKey("STING.DISC"))
                    el.Properties["STING.DISC"] = DeriveDisc(el.IfcType);

                // ── Opening host reference ────────────────────────────────────
                if (_openingHost.TryGetValue(el.Id, out int hostId))
                    el.Properties["STING.HOST_ELEMENT_ID"] = hostId.ToString();
            }
        }

        // Convert storey name to a short STING level code (GF / L01 / B1 / RFT / …)
        private static string DeriveLevel(string storeyName, double elevM)
        {
            string n = storeyName.Trim().ToUpperInvariant();

            if (n.Contains("GROUND") || n is "GF" or "EG" or "RDC" or "0" or "00")
                return "GF";
            if (n.Contains("ROOF") || n.StartsWith("RF") || n.StartsWith("ROOF"))
                return "RF";
            if (n.StartsWith("B") && n.Length <= 3
                && int.TryParse(n[1..], out int bNum))
                return $"B{bNum}";
            // Try to pull a number out of the name
            var digits = System.Text.RegularExpressions.Regex.Match(n, @"(\d+)");
            if (digits.Success)
            {
                int lvlNum = int.Parse(digits.Groups[1].Value);
                if (elevM < -0.5) return $"B{lvlNum}";
                return $"L{lvlNum:D2}";
            }
            return storeyName.Length > 6 ? storeyName[..6].ToUpperInvariant() : storeyName.ToUpperInvariant();
        }

        private static string SanitiseZoneCode(string zone)
        {
            // Strip common prefixes like "Zone ", "Z-", and trim to ≤6 chars
            string s = System.Text.RegularExpressions.Regex.Replace(zone.Trim(), @"^(Zone\s*|Z[-_])", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[^A-Za-z0-9_]", "");
            if (s.Length == 0) return zone.Length > 4 ? zone[..4].ToUpperInvariant() : zone.ToUpperInvariant();
            return s.Length > 6 ? s[..6].ToUpperInvariant() : s.ToUpperInvariant();
        }

        private static string DeriveDisc(string ifcType)
        {
            string t = ifcType.ToUpperInvariant();
            if (t.Contains("STRUCT") || t is "IFCCOLUMN" or "IFCBEAM" or "IFCPILE"
                or "IFCFOOTING" or "IFCREINFORCINGBAR" or "IFCMEMBER" or "IFCPLATE")
                return "S";
            if (t.Contains("PIPE") || t.Contains("PUMP") || t.Contains("VALVE")
                || t.Contains("SANITARY") || t.Contains("WASTE"))
                return "P";
            if (t.Contains("DUCT") || t.Contains("AIR") || t.Contains("FAN")
                || t.Contains("CHILLER") || t.Contains("BOILER") || t.Contains("HVAC")
                || t.Contains("HEATEXCHANGER") || t.Contains("HUMIDIFIER"))
                return "M";
            if (t.Contains("ELECTRIC") || t.Contains("LIGHT") || t.Contains("DISTRIBUTION"))
                return "E";
            if (t is "IFCWALL" or "IFCWALLSTANDARDCASE" or "IFCSLAB" or "IFCROOF"
                or "IFCCOVERING" or "IFCSTAIR" or "IFCRAMP" or "IFCRAILING"
                or "IFCDOOR" or "IFCWINDOW" or "IFCSPACE" or "IFCFURNITURE"
                or "IFCFURNISHINGELEMENT" or "IFCBUILDINGELEMENT")
                return "A";
            return "A";
        }

        // ── Data quality validation ───────────────────────────────────────────
        private void ValidateDataQuality()
        {
            // Duplicate GlobalId detection
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in Elements)
            {
                if (string.IsNullOrEmpty(el.GlobalId)) continue;
                if (seen.TryGetValue(el.GlobalId, out int prevId))
                    Warnings.Add($"Duplicate GlobalId {el.GlobalId} on #${el.Id} and #{prevId}");
                else
                    seen[el.GlobalId] = el.Id;
            }

            // Coordinate sanity check (IFC units — if any element is >100 km from origin, warn)
            const double MaxReasonableM = 100_000;
            foreach (var el in Elements)
            {
                double d = Math.Sqrt(el.InsertionPoint[0]*el.InsertionPoint[0]
                         + el.InsertionPoint[1]*el.InsertionPoint[1]);
                if (d * UnitScale > MaxReasonableM)
                {
                    Warnings.Add($"Element #{el.Id} ({el.IfcType}) is {d*UnitScale/1000:F1} km from origin — check coordinate system");
                    break; // one warning is enough
                }
            }

            // Geometry fallback count
            int aabbCount = Elements.Count(e => e.GeometryIsAabbFallback);
            if (aabbCount > 0)
                Warnings.Add($"{aabbCount} element(s) used AABB geometry fallback (AdvancedBrep / TriangulatedFaceSet / SweptDisk not supported by the lightweight parser — geometry will be approximate bounding boxes).");

            // Warn when mandatory ArchiCAD psets are missing entirely
            bool hasRenovPset = Elements.Any(e => e.Properties.Keys
                .Any(k => k.StartsWith("AC_Pset_RenovationInfo.", StringComparison.OrdinalIgnoreCase)));
            if (!hasRenovPset && Elements.Count > 0)
                Warnings.Add("AC_Pset_RenovationInfo not found — renovation status will default to NEW. Enable 'Export renovation status properties' in ArchiCAD IFC Translator.");

            // Spatial hierarchy coverage
            int withHierarchy = Elements.Count(e => e.Properties.ContainsKey("IfcHierarchy.Storey"));
            if (withHierarchy == 0 && Elements.Count > 0)
                Warnings.Add("No IfcRelAggregates hierarchy found — building/storey/space structure unavailable. Check ArchiCAD IFC export includes spatial structure.");

            // Derived quantity coverage
            int withNetArea = Elements.Count(e => e.Properties.ContainsKey("DerivedQty.NetSideArea_m2")
                                                || e.Properties.ContainsKey("DerivedQty.NetFloorArea_m2"));
            int withCostTotal = Elements.Count(e => e.Properties.ContainsKey("DerivedQty.CostTotal"));
            if (HasQuantitySets && withNetArea > 0)
                StingLog.Info($"ArchiCAD: derived net quantities for {withNetArea} elements; cost totals for {withCostTotal} elements.");
            else if (HasQuantitySets && withNetArea == 0)
                Warnings.Add("Quantity sets found but no net area/volume derivation was possible — check Qto pset names match expected Qto_WallBaseQuantities / Qto_SlabBaseQuantities patterns.");
        }

        // ── Elements (geometry extraction) ────────────────────────────────────
        private static readonly HashSet<string> ElementTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "IFCWALL","IFCWALLSTANDARDCASE","IFCSLAB","IFCCOLUMN","IFCBEAM","IFCMEMBER",
            "IFCDOOR","IFCWINDOW","IFCROOF","IFCCOVERING","IFCSPACE","IFCFURNISHINGELEMENT",
            "IFCFLOWSEGMENT","IFCFLOWTERMINAL","IFCFLOWFITTING","IFCFLOWMOVINGDEVICE",
            "IFCFLOWCONTROLLER","IFCSTAIR","IFCSTAIRFLIGHT","IFCRAMP","IFCRAILING",
            "IFCPLATE","IFCOPENINGELEMENT","IFCPILE","IFCFOOTING","IFCREINFORCINGBAR",
            // Expanded: ArchiCAD frequently emits these types
            "IFCBUILDINGELEMENT","IFCPROXY","IFCGEOGRAPHICELEMENT","IFCANNOTATION",
            "IFCASSET","IFCBUILDINGSTOREY","IFCFURNITURE","IFCMECHANICALFASTENER",
            "IFCPIPESEGMENT","IFCPIPEFITTING","IFCDUCTFITTING","IFCDUCTSEGMENT",
            "IFCAIRTERMINAL","IFCLIGHTFIXTURE","IFCELECTRICDISTRIBUTIONBOARD",
            "IFCELECTRICAPPLIANCE","IFCSANITARYTERMINAL","IFCWASTETERMINAL",
            "IFCBURNER","IFCPUMP","IFCFAN","IFCVALVE","IFCFILTER","IFCHUMIDIFIER",
            "IFCCHILLER","IFCBOILER","IFCHEATEXCHANGER","IFCSYSTEMFURNITUREELEMENT"
        };

        // element id → host wall id (via IfcRelVoidsElement for openings/doors/windows)
        private readonly Dictionary<int, int> _openingHost = new();

        private void ResolveElements()
        {
            foreach (var e in _e.Values.Where(e => ElementTypes.Contains(e.Type)))
            {
                var parts = SplitArgs(e.Raw);

                // Placement → world transform
                int placRef = ExtractRef(e.Raw, 5); // arg 5 = ObjectPlacement
                Mat4 xf     = ResolveWorldTransform(placRef);
                _worldXf[e.Id] = xf;
                double[] ins = xf.TranslationOnly();

                var el = new AcIfcElement
                {
                    Id             = e.Id,
                    IfcType        = e.Type,
                    GlobalId       = parts.Count > 0 ? Unquote(parts[0]) : "",
                    Name           = parts.Count > 2 ? Unquote(parts[2]) : "",
                    ObjectType     = parts.Count > 4 ? Unquote(parts[4]) : "",
                    PredefinedType = parts.Count > 8 ? Unquote(parts[8]) : "",
                    InsertionPoint = ins,
                    WorldTransform = xf,
                };

                // Geometry: try representation (arg 6)
                int repRef = ExtractRef(e.Raw, 6);
                ExtractGeometry(el, repRef);

                Elements.Add(el);
            }
        }

        // ── Geometry extraction from IfcProductDefinitionShape ────────────────
        //  Priority: IfcPolyline axis (walls) > IfcExtrudedAreaSolid > IfcBoundingBox
        private void ExtractGeometry(AcIfcElement el, int repShapeRef)
        {
            if (!_e.TryGetValue(repShapeRef, out var repShape)) return;
            // IfcProductDefinitionShape: arg 2 = Representations (SET of IfcShapeRepresentation)
            foreach (int srRef in ExtractRefList(repShape.Raw, 2))
            {
                if (!_e.TryGetValue(srRef, out var sr)) continue;
                // IfcShapeRepresentation: arg 1 = RepresentationIdentifier, arg 3 = Items
                string repId = ExtractString(sr.Raw, 1);
                foreach (int itemRef in ExtractRefList(sr.Raw, 3))
                {
                    if (!_e.TryGetValue(itemRef, out var item)) continue;

                    switch (item.Type)
                    {
                        case "IFCPOLYLINE":
                            // Wall axis: arg 0 = Points (LIST of IfcCartesianPoint)
                            if (repId.Equals("Axis", StringComparison.OrdinalIgnoreCase)
                                || el.IfcType.Contains("WALL"))
                            {
                                var pts = ExtractRefList(item.Raw, 0);
                                if (pts.Count >= 2)
                                {
                                    el.AxisStart = TransformLocalPt(el, GetPoint(pts[0]));
                                    el.AxisEnd   = TransformLocalPt(el, GetPoint(pts[pts.Count-1]));
                                }
                            }
                            break;

                        case "IFCEXTRUDEDAREASOLID":
                            // arg 0 = SweptArea, arg 2 = ExtrudedDirection, arg 3 = Depth
                            ExtractExtrudedSolid(el, item);
                            break;

                        case "IFCBOUNDINGBOX":
                            // arg 0 = Corner (IfcCartesianPoint), arg 1 = XDim, arg 2 = YDim, arg 3 = ZDim
                            ExtractBoundingBox(el, item);
                            break;

                        case "IFCFACETEDBREP":
                        case "IFCSHELLBASEDSURFACEMODEL":
                        case "IFCOPENSHELL":
                        case "IFCCLOSEDSHELL":
                            ExtractBrepFaces(el, item);
                            break;

                        case "IFCADVANCEDBREP":
                        case "IFCADVANCEDBREPWITHVOIDS":
                        case "IFCBSPLINESURFACE":
                        case "IFCBSPLINESURFACEWITHKNOTS":
                        case "IFCRATIONALBSPLINESURFACEWITHKNOTS":
                        case "IFCSWEPTDISKSOLID":
                        case "IFCSWEPTDISKSOLIDPOLYGONAL":
                        case "IFCREVOLVEDAREASOLID":
                        case "IFCSURFACECURVESWEPTAREASOLID":
                        case "IFCTRIANGULATEDFACESET":
                        case "IFCPOLYGONALFACESET":
                            // Unsupported by the lightweight parser — collapse to an AABB
                            // box so the element at least lands at the right place.
                            ApplyReachableAabb(el, item.Id);
                            el.GeometryIsAabbFallback = true;
                            break;

                        case "IFCMAPPEDITEM":
                            // arg 0 = MappingSource (IfcRepresentationMap),
                            // arg 1 = MappingTarget (IfcCartesianTransformationOperator, ignored — small offset)
                            ExtractMappedItem(el, item);
                            break;

                        case "IFCPOLYGONALBOUNDEDHALFSPACE":
                            // Boolean clipping primitive — skip
                            break;
                    }
                }
            }
        }

        private void ExtractExtrudedSolid(AcIfcElement el, AcIfcEntity solid)
        {
            var parts = SplitArgs(solid.Raw);
            // arg 3 = Depth
            if (parts.Count > 3 && double.TryParse(parts[3].Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double depth))
                el.Height = Math.Max(el.Height, depth);

            // arg 0 = SweptArea (IfcRectangleProfileDef or IfcArbitraryClosedProfileDef)
            int areaRef = ExtractRef(solid.Raw, 0);
            if (!_e.TryGetValue(areaRef, out var area)) return;

            if (area.Type == "IFCRECTANGLEPROFILEDEF")
            {
                // arg 3 = XDim, arg 4 = YDim
                var ap = SplitArgs(area.Raw);
                if (ap.Count > 4)
                {
                    double bx = 0, by = 0;
                    double.TryParse(ap[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out bx);
                    double.TryParse(ap[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out by);
                    el.BboxHalfX = bx / 2; el.BboxHalfY = by / 2;
                }
            }
            else if (area.Type == "IFCARBITRARYCLOSEDPROFILEDEF"
                  || area.Type == "IFCARBITRARYPROFILEDEFWITHVOIDS")
            {
                // arg 2 = OuterCurve (IfcPolyline | IfcCompositeCurve)
                int outerRef = ExtractRef(area.Raw, 2);
                ApplyPolylineBbox(el, outerRef);
            }
            else if (area.Type == "IFCCIRCLEPROFILEDEF")
            {
                // arg 3 = Radius
                var ap = SplitArgs(area.Raw);
                if (ap.Count > 3 && double.TryParse(ap[3].Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double r) && r > 0)
                { el.BboxHalfX = r; el.BboxHalfY = r; }
            }
        }

        // Walk any IfcCurve (Polyline / CompositeCurve / TrimmedCurve / …) and update the
        // element's planar half-extents from every IfcCartesianPoint reachable from it.
        // For curves whose extent is parametric (arcs / b-splines) the points still bound
        // the swept profile sufficiently for a fallback box, and full tessellation is the
        // job of ExtractBrepFaces.
        private void ApplyPolylineBbox(AcIfcElement el, int curveRef)
        {
            var pts = CollectReachableCartesianPoints(curveRef, 2000);
            if (pts.Count == 0) return;
            double minX=double.MaxValue, maxX=double.MinValue;
            double minY=double.MaxValue, maxY=double.MinValue;
            foreach (var p in pts)
            {
                if (p[0]<minX) minX=p[0]; if (p[0]>maxX) maxX=p[0];
                if (p[1]<minY) minY=p[1]; if (p[1]>maxY) maxY=p[1];
            }
            double hx=(maxX-minX)/2.0, hy=(maxY-minY)/2.0;
            if (hx > el.BboxHalfX) el.BboxHalfX = hx;
            if (hy > el.BboxHalfY) el.BboxHalfY = hy;
        }

        // Catch-all bbox for advanced brep / b-spline / swept-disk / triangulated-faceset
        // primitives: walks the reachable point cloud and applies its AABB to the element.
        // Coarse but ensures we never lose an element entirely.
        private void ApplyReachableAabb(AcIfcElement el, int rootRef)
        {
            var pts = CollectReachableCartesianPoints(rootRef, 5000);
            if (pts.Count < 2) return;
            double minX=double.MaxValue, minY=double.MaxValue, minZ=double.MaxValue;
            double maxX=double.MinValue, maxY=double.MinValue, maxZ=double.MinValue;
            foreach (var p in pts)
            {
                if (p[0]<minX) minX=p[0]; if (p[0]>maxX) maxX=p[0];
                if (p[1]<minY) minY=p[1]; if (p[1]>maxY) maxY=p[1];
                if (p[2]<minZ) minZ=p[2]; if (p[2]>maxZ) maxZ=p[2];
            }
            double hx=(maxX-minX)/2.0, hy=(maxY-minY)/2.0, hz=(maxZ-minZ);
            if (hx > el.BboxHalfX) el.BboxHalfX = hx;
            if (hy > el.BboxHalfY) el.BboxHalfY = hy;
            if (hz > el.Height)    el.Height    = hz;
        }

        // Generic DFS over STEP refs collecting every IfcCartesianPoint reachable from
        // `rootRef`. Used by both curve bbox and brep AABB fallback.
        private List<double[]> CollectReachableCartesianPoints(int rootRef, int cap)
        {
            var result = new List<double[]>();
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(rootRef);
            while (stack.Count > 0 && seen.Count < cap)
            {
                int id = stack.Pop();
                if (!seen.Add(id)) continue;
                if (!_e.TryGetValue(id, out var ent)) continue;
                if (ent.Type == "IFCCARTESIANPOINT") { result.Add(GetPoint(id)); continue; }
                // Sample IfcCircle apex points so trimmed arcs contribute their bulge
                // to the bbox, not just their chord endpoints.
                if (ent.Type == "IFCCIRCLE")
                {
                    var ap = SplitArgs(ent.Raw);
                    if (ap.Count > 1 && double.TryParse(ap[1].Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
                    {
                        int placRef = ExtractRef(ent.Raw, 0);
                        var origin = ExtractAxis2DOrigin(placRef);
                        // Push 4 cardinal apex points so the bbox covers the full arc envelope.
                        result.Add(new[] { origin[0] + r, origin[1],     origin[2] });
                        result.Add(new[] { origin[0] - r, origin[1],     origin[2] });
                        result.Add(new[] { origin[0],     origin[1] + r, origin[2] });
                        result.Add(new[] { origin[0],     origin[1] - r, origin[2] });
                    }
                    continue;
                }
                foreach (Match m in Regex.Matches(ent.Raw, @"#(\d+)"))
                    if (int.TryParse(m.Groups[1].Value, out int r)) stack.Push(r);
            }
            return result;
        }

        // Lightweight origin extraction for IfcAxis2Placement2D / 3D — for arc sampling only.
        private double[] ExtractAxis2DOrigin(int placRef)
        {
            if (placRef <= 0 || !_e.TryGetValue(placRef, out var p)) return new double[3];
            int locRef = ExtractRef(p.Raw, 0);
            return locRef > 0 ? GetPoint(locRef) : new double[3];
        }

        // IfcFacetedBrep / IfcShellBasedSurfaceModel — tessellate faces into triangles.
        // Polygons are fan-triangulated (ArchiCAD output is reliably planar+simple).
        // Each triangle is pushed into el.BrepTriangles in IFC WORLD space so the
        // mapper can hand them straight to TessellatedShapeBuilder.
        // Falls back to AABB-only if no faces are found.
        private void ExtractBrepFaces(AcIfcElement el, AcIfcEntity brep)
        {
            // IfcFacetedBrep.Outer = arg 0 (IfcClosedShell);
            // IfcShellBasedSurfaceModel.SbsmBoundary = arg 0 (SET of IfcShell)
            var shellIds = new List<int>();
            int single = ExtractRef(brep.Raw, 0);
            if (single > 0) shellIds.Add(single);
            shellIds.AddRange(ExtractRefList(brep.Raw, 0));
            shellIds = shellIds.Distinct().ToList();

            int triCount = 0;
            double minX=double.MaxValue, minY=double.MaxValue, minZ=double.MaxValue;
            double maxX=double.MinValue, maxY=double.MinValue, maxZ=double.MinValue;

            foreach (int shellId in shellIds)
            {
                if (!_e.TryGetValue(shellId, out var shell)) continue;
                if (shell.Type != "IFCCLOSEDSHELL" && shell.Type != "IFCOPENSHELL") continue;

                // CfsFaces = arg 0 (LIST of IfcFace)
                foreach (int faceId in ExtractRefList(shell.Raw, 0))
                {
                    if (!_e.TryGetValue(faceId, out var face)) continue;
                    // IfcFace.Bounds = arg 0 (SET of IfcFaceBound)
                    foreach (int fbId in ExtractRefList(face.Raw, 0))
                    {
                        if (!_e.TryGetValue(fbId, out var fb)) continue;
                        // IfcFaceBound.Bound = arg 0 (IfcLoop), arg 1 = Orientation
                        int loopId = ExtractRef(fb.Raw, 0);
                        bool orient = !ExtractString(fb.Raw, 1).Equals(".F.", StringComparison.OrdinalIgnoreCase);
                        if (!_e.TryGetValue(loopId, out var loop) || loop.Type != "IFCPOLYLOOP") continue;

                        // IfcPolyLoop.Polygon = arg 0 (LIST of IfcCartesianPoint)
                        var ringRefs = ExtractRefList(loop.Raw, 0);
                        if (ringRefs.Count < 3) continue;

                        // Transform ring vertices to world space once.
                        var ring = new List<double[]>(ringRefs.Count);
                        foreach (int pr in ringRefs)
                        {
                            var lp = GetPoint(pr);
                            var w = el.WorldTransform.TransformPoint(lp[0], lp[1], lp[2]);
                            ring.Add(w);
                            if (w[0]<minX) minX=w[0]; if (w[0]>maxX) maxX=w[0];
                            if (w[1]<minY) minY=w[1]; if (w[1]>maxY) maxY=w[1];
                            if (w[2]<minZ) minZ=w[2]; if (w[2]>maxZ) maxZ=w[2];
                        }
                        if (!orient) ring.Reverse();

                        // Fan triangulation around ring[0].
                        for (int i = 1; i < ring.Count - 1; i++)
                        {
                            el.BrepTriangles.Add(new[]
                            {
                                ring[0][0], ring[0][1], ring[0][2],
                                ring[i][0], ring[i][1], ring[i][2],
                                ring[i+1][0], ring[i+1][1], ring[i+1][2],
                            });
                            triCount++;
                            if (triCount > 50000) break; // safety cap
                        }
                        if (triCount > 50000) break;
                    }
                    if (triCount > 50000) break;
                }
                if (triCount > 50000) break;
            }

            // Update AABB so even if Revit rejects the tessellation we have a sensible
            // fallback box. The values stored here are in element-local space, so we
            // re-base around the element's local origin.
            if (triCount > 0)
            {
                var origin = el.WorldTransform.TranslationOnly();
                double hx = Math.Max(maxX-origin[0], origin[0]-minX);
                double hy = Math.Max(maxY-origin[1], origin[1]-minY);
                double hz = Math.Max(maxZ-origin[2], origin[2]-minZ);
                if (hx > el.BboxHalfX) el.BboxHalfX = hx;
                if (hy > el.BboxHalfY) el.BboxHalfY = hy;
                if (hz > el.Height)    el.Height    = hz;
                return;
            }

            // No faces decoded — fall back to point-cloud AABB.
            var pts = CollectReachableCartesianPoints(brep.Id, 5000);
            if (pts.Count < 2) return;
            foreach (var p in pts)
            {
                if (p[0]<minX) minX=p[0]; if (p[0]>maxX) maxX=p[0];
                if (p[1]<minY) minY=p[1]; if (p[1]>maxY) maxY=p[1];
                if (p[2]<minZ) minZ=p[2]; if (p[2]>maxZ) maxZ=p[2];
            }
            double bx=(maxX-minX)/2.0, by=(maxY-minY)/2.0, bz=(maxZ-minZ);
            if (bx > el.BboxHalfX) el.BboxHalfX = bx;
            if (by > el.BboxHalfY) el.BboxHalfY = by;
            if (bz > el.Height)    el.Height    = bz;
        }

        // IfcMappedItem: arg 0 = IfcRepresentationMap (MappingOrigin + MappedRepresentation).
        // The mapped representation is a full IfcShapeRepresentation we can recurse into,
        // applying the mapping origin as an extra local placement.
        private void ExtractMappedItem(AcIfcElement el, AcIfcEntity mapped)
        {
            int mapRef = ExtractRef(mapped.Raw, 0);
            if (!_e.TryGetValue(mapRef, out var map) || map.Type != "IFCREPRESENTATIONMAP") return;
            int originRef = ExtractRef(map.Raw, 0); // IfcAxis2Placement3D
            int repRef    = ExtractRef(map.Raw, 1); // IfcShapeRepresentation

            // Compose the mapped origin onto the element's world transform so points
            // extracted below land in true world space.
            Mat4 saved = el.WorldTransform;
            Mat4 mapXf = ResolveAxis2Placement3D(originRef);
            el.WorldTransform = Mat4.Compose(saved, mapXf);

            // Walk items of the mapped representation directly (it's a shape rep, not a
            // product-definition-shape, so its items live at arg 3).
            if (_e.TryGetValue(repRef, out var sr))
            {
                foreach (int itemRef in ExtractRefList(sr.Raw, 3))
                {
                    if (!_e.TryGetValue(itemRef, out var item)) continue;
                    switch (item.Type)
                    {
                        case "IFCEXTRUDEDAREASOLID": ExtractExtrudedSolid(el, item); break;
                        case "IFCBOUNDINGBOX":       ExtractBoundingBox(el, item); break;
                        case "IFCFACETEDBREP":
                        case "IFCSHELLBASEDSURFACEMODEL": ExtractBrepFaces(el, item); break;
                    }
                }
            }

            el.WorldTransform = saved;
        }

        private void ExtractBoundingBox(AcIfcElement el, AcIfcEntity bbox)
        {
            var parts = SplitArgs(bbox.Raw);
            if (parts.Count < 4) return;
            double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double xd);
            double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double yd);
            double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double zd);
            if (xd > 0) el.BboxHalfX = xd / 2;
            if (yd > 0) el.BboxHalfY = yd / 2;
            if (zd > 0) el.Height    = Math.Max(el.Height, zd);
        }

        private double[] GetPoint(int ptRef)
        {
            if (!_e.TryGetValue(ptRef, out var pt)) return new double[3];
            string inner = pt.Raw.Trim();
            if (inner.StartsWith("(") && inner.EndsWith(")")) inner = inner[1..^1];
            var vals = inner.Split(',').Select(v => {
                double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d);
                return d;
            }).ToArray();
            return new[] { vals.Length > 0 ? vals[0] : 0,
                           vals.Length > 1 ? vals[1] : 0,
                           vals.Length > 2 ? vals[2] : 0 };
        }

        private static double[] TransformLocalPt(AcIfcElement el, double[] localPt)
            => el.WorldTransform.TransformPoint(localPt[0], localPt[1], localPt[2]);

        private void MergePropertiesIntoElements()
        {
            foreach (var el in Elements)
            {
                // Merge type-object psets first (instance values will override below)
                if (_relDefByType.TryGetValue(el.Id, out int typeId)
                    && _typeObjPsets.TryGetValue(typeId, out var typePsetIds))
                {
                    foreach (int pid in typePsetIds)
                    {
                        if (!_psets.TryGetValue(pid, out var typeProps)) continue;
                        foreach (var kv in typeProps)
                            if (!el.Properties.ContainsKey(kv.Key)) // instance wins
                                el.Properties[kv.Key] = kv.Value;
                    }
                }

                // Merge instance psets (instance values override type values)
                if (!_relDef.TryGetValue(el.Id, out var psetIds)) continue;
                foreach (int pid in psetIds)
                {
                    if (!_psets.TryGetValue(pid, out var props)) continue;
                    foreach (var kv in props) el.Properties[kv.Key] = kv.Value;
                }
            }
        }

        private void MergeMaterialLayerProperties()
        {
            foreach (var el in Elements)
            {
                if (_predominantMaterial.TryGetValue(el.Id, out string? matName)
                    && !string.IsNullOrEmpty(matName))
                    // Store as a synthetic property so the property mapper can write it
                    el.Properties["IfcMaterialLayer.PredominantMaterial"] = matName;

                if (_layerComposition.TryGetValue(el.Id, out string? composition)
                    && !string.IsNullOrEmpty(composition))
                    el.Properties["IfcMaterialLayer.LayerComposition"] = composition;

                if (_zoneAssignment.TryGetValue(el.Id, out string? zoneName)
                    && !string.IsNullOrEmpty(zoneName))
                    el.Properties["IfcZone.Name"] = zoneName;
            }
        }

        private void AssignStoreyToElements()
        {
            var storeyIds = new HashSet<int>(Storeys.Select(s => s.Id));
            foreach (var el in Elements)
            {
                if (_relContain.TryGetValue(el.Id, out int sid) && storeyIds.Contains(sid))
                    el.StoreyId = sid;
            }
        }

        // ── Placement resolution (full 4×4 matrix walk) ───────────────────────
        //  IfcLocalPlacement: arg 0 = PlacementRelTo (parent, OPTIONAL)
        //                     arg 1 = RelativePlacement (IfcAxis2Placement3D)
        private Mat4 ResolveWorldTransform(int placementId)
        {
            if (placementId <= 0 || !_e.TryGetValue(placementId, out var pe))
                return Mat4.Identity();

            if (pe.Type != "IFCLOCALPLACEMENT") return Mat4.Identity();

            int parentRef = ExtractRef(pe.Raw, 0); // arg 0 = PlacementRelTo (parent)
            int axisRef   = ExtractRef(pe.Raw, 1); // arg 1 = RelativePlacement

            Mat4 parent = parentRef > 0 ? ResolveWorldTransform(parentRef) : Mat4.Identity();
            Mat4 local  = ResolveAxis2Placement3D(axisRef);
            return Mat4.Compose(parent, local);
        }

        private Mat4 ResolveAxis2Placement3D(int id)
        {
            if (id <= 0 || !_e.TryGetValue(id, out var e)) return Mat4.Identity();
            if (e.Type != "IFCAXIS2PLACEMENT3D") return Mat4.Identity();

            // arg 0 = Location (IfcCartesianPoint)
            // arg 1 = Axis     (IfcDirection, Z-axis, OPTIONAL)
            // arg 2 = RefDirection (IfcDirection, X-axis, OPTIONAL)
            double[] loc  = GetPoint(ExtractRef(e.Raw, 0));
            double[] axZ  = ExtractRef(e.Raw, 1) > 0 ? GetPoint(ExtractRef(e.Raw, 1)) : new[] { 0.0, 0.0, 1.0 };
            double[] refX = ExtractRef(e.Raw, 2) > 0 ? GetPoint(ExtractRef(e.Raw, 2)) : new[] { 1.0, 0.0, 0.0 };
            return Mat4.FromPlacement(loc, axZ, refX);
        }

        // ── STEP parsing helpers ──────────────────────────────────────────────

        internal static List<string> SplitArgs(string raw)
        {
            var result = new List<string>();
            int depth = 0; bool inStr = false;
            var cur = new StringBuilder();
            foreach (char c in raw)
            {
                if (c == '\'' && depth == 0) inStr = !inStr;
                if (!inStr)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0)
                    { result.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                }
                cur.Append(c);
            }
            if (cur.Length > 0) result.Add(cur.ToString().Trim());
            return result;
        }

        private static string ExtractString(string raw, int idx)
        {
            var p = SplitArgs(raw);
            return idx < p.Count ? Unquote(p[idx]) : "";
        }

        private static int ExtractRef(string raw, int idx)
        {
            var p = SplitArgs(raw);
            if (idx >= p.Count) return -1;
            string s = p[idx].Trim();
            return s.StartsWith("#") && int.TryParse(s[1..], out int id) ? id : -1;
        }

        private static List<int> ExtractRefList(string raw, int idx)
        {
            var result = new List<int>();
            var p = SplitArgs(raw);
            if (idx >= p.Count) return result;
            string s = p[idx].Trim();
            if (s.StartsWith("(") && s.EndsWith(")")) s = s[1..^1];
            foreach (string t in s.Split(','))
            {
                string tok = t.Trim();
                if (tok.StartsWith("#") && int.TryParse(tok[1..], out int id)) result.Add(id);
            }
            return result;
        }

        private static string ExtractNominalValue(string raw)
        {
            var parts = SplitArgs(raw);
            if (parts.Count < 3) return "";
            string v = parts[2].Trim();
            var m = Regex.Match(v, @"^IFC\w+\((.+)\)$", RegexOptions.IgnoreCase);
            return m.Success ? Unquote(m.Groups[1].Value.Trim()) : Unquote(v);
        }

        private static string Unquote(string s)
        {
            s = s.Trim();
            return s.Length >= 2 && s[0] == '\'' && s[^1] == '\'' ? s[1..^1] : s;
        }
    }

    // =========================================================================
    //  Coordinate aligner  (IFC world space → Revit internal feet)
    //  Subtracts the site world-transform origin so elements land on the
    //  Revit Project Base Point, then applies the inverse site rotation.
    // =========================================================================

    public sealed class ArchiCadCoordinateAligner
    {
        private const double M2ft = 3.28083989501312;

        private readonly double _unitScale;         // IFC units → metres
        private readonly double _ox, _oy, _oz;     // site origin in IFC units
        // Inverse of the site rotation as a full 3×3 (rows of original R-transpose)
        private readonly double _rxx, _rxy, _rxz;
        private readonly double _ryx, _ryy, _ryz;
        private readonly double _rzx, _rzy, _rzz;
        private double _udx, _udy, _udz;           // user manual override in Revit feet

        public ArchiCadCoordinateAligner(Mat4 siteXf, double unitScale)
        {
            _unitScale = unitScale;
            _ox = siteXf.Tx; _oy = siteXf.Ty; _oz = siteXf.Tz;
            // R is orthonormal → R⁻¹ = Rᵀ. Take the transpose of the 3×3 rotation
            // block so we can handle arbitrary 3D site orientation, not just Z-spin.
            _rxx = siteXf.Xx; _rxy = siteXf.Xy; _rxz = siteXf.Xz;
            _ryx = siteXf.Yx; _ryy = siteXf.Yy; _ryz = siteXf.Yz;
            _rzx = siteXf.Zx; _rzy = siteXf.Zy; _rzz = siteXf.Zz;
        }

        public void SetUserOffset(double dx, double dy, double dz)
        { _udx = dx; _udy = dy; _udz = dz; }

        /// <summary>Convert IFC-world-space coordinates to Revit internal feet.</summary>
        public XYZ ToRevit(double ifcX, double ifcY, double ifcZ)
        {
            // 1. Remove site origin
            double lx = ifcX - _ox, ly = ifcY - _oy, lz = ifcZ - _oz;
            // 2. Apply inverse site rotation (Rᵀ · v). For column-major Mat4 the
            //    forward rotation is (Xx,Xy,Xz)=col0 etc.; its transpose has rows
            //    = those columns, so the inverse-rotated vector is:
            double rx = _rxx * lx + _rxy * ly + _rxz * lz;
            double ry = _ryx * lx + _ryy * ly + _ryz * lz;
            double rz = _rzx * lx + _rzy * ly + _rzz * lz;
            // 3. IFC units → metres → Revit feet
            return new XYZ(rx * _unitScale * M2ft + _udx,
                           ry * _unitScale * M2ft + _udy,
                           rz * _unitScale * M2ft + _udz);
        }

        public XYZ ToRevit(double[] pt) => ToRevit(pt[0], pt[1], pt[2]);

        /// <summary>Convert elevation in IFC units (absolute world Z) to Revit feet.</summary>
        public double ElevToRevit(double ifcZ)
            => (ifcZ - _oz) * _unitScale * M2ft + _udz;

        /// <summary>Scale only (no origin/rotation) — for lengths/heights.</summary>
        public double ScaleToRevit(double ifcLen) => ifcLen * _unitScale * M2ft;

        /// <summary>AbsoluteElevM (already in metres) → Revit internal feet.</summary>
        public double ElevMetresToRevitFt(double elevM)
            => (elevM - _oz * _unitScale) * M2ft + _udz;
    }

    // =========================================================================
    //  Level matcher
    // =========================================================================

    public sealed class ArchiCadLevelMatcher
    {
        private const double ToleranceFt = 5.0 / 304.8; // 5 mm

        private readonly Document _doc;
        private readonly ArchiCadCoordinateAligner _c;
        private readonly Dictionary<int, ElementId> _map = new();

        public ArchiCadLevelMatcher(Document doc, ArchiCadCoordinateAligner c)
        { _doc = doc; _c = c; }

        public void MatchStoreys(List<AcIfcStorey> storeys, bool createMissing,
            List<string> warnings)
        {
            var revitLevels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level)).Cast<Level>().ToList();

            foreach (var s in storeys)
            {
                double targetFt = _c.ElevMetresToRevitFt(s.AbsoluteElevM);

                Level? best = revitLevels
                    .OrderBy(l => Math.Abs(l.Elevation - targetFt))
                    .FirstOrDefault();

                if (best != null && Math.Abs(best.Elevation - targetFt) <= ToleranceFt)
                {
                    _map[s.Id] = best.Id;
                    s.RevitLevelId = best.Id.Value;
                }
                else if (createMissing)
                {
                    Level created = Level.Create(_doc, targetFt);
                    created.Name = $"AC_{s.Name}";
                    _map[s.Id] = created.Id;
                    s.RevitLevelId = created.Id.Value;
                    s.WasCreated = true;
                    revitLevels.Add(created);
                }
                else
                {
                    warnings.Add($"No Revit level for '{s.Name}' at {s.AbsoluteElevM:F3} m");
                    if (best != null) { _map[s.Id] = best.Id; s.RevitLevelId = best.Id.Value; }
                }
            }
        }

        public ElementId GetLevel(int storeyId)
            => _map.TryGetValue(storeyId, out var id) ? id : ElementId.InvalidElementId;
    }

    // =========================================================================
    //  Element mapper
    // =========================================================================

    public sealed class ArchiCadElementMapper
    {
        private readonly Document _doc;
        private readonly ArchiCadCoordinateAligner _c;
        private readonly ArchiCadLevelMatcher _lm;
        private readonly Dictionary<string, AcIfcTypeMapping> _typeLookup;

        public int CreatedNative { get; private set; }
        public int CreatedDirect { get; private set; }
        public int Skipped       { get; private set; }
        public List<string> Warnings { get; } = new();

        // Pre-cached family symbols & types (avoid repeated full collector scans)
        private WallType?        _defaultWallType;
        private FloorType?       _defaultFloorType;
        private FamilySymbol?    _defaultColumn;
        private FamilySymbol?    _defaultBeam;
        private FamilySymbol?    _defaultDoor;
        private FamilySymbol?    _defaultWindow;
        private List<WallType>   _allWallTypes  = new();
        private List<Wall>       _allWalls      = new();
        private bool             _wallsLoaded;

        public ArchiCadElementMapper(Document doc, ArchiCadCoordinateAligner c,
            ArchiCadLevelMatcher lm, AcIfcMappingConfig config)
        {
            _doc = doc; _c = c; _lm = lm;
            _typeLookup = config.TypeMappings.ToDictionary(
                m => (m.IfcType + "|" + m.PredefinedType).ToUpperInvariant());

            // Cache family types once
            _allWallTypes   = new FilteredElementCollector(doc).OfClass(typeof(WallType))
                                  .Cast<WallType>().ToList();
            _defaultWallType  = _allWallTypes.FirstOrDefault();
            _defaultFloorType = new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                                  .Cast<FloorType>().FirstOrDefault();
            _defaultColumn    = FindSymbol("Columns", "Column", "Structural Column");
            _defaultBeam      = FindSymbol("Structural Framing", "Beam", "W-Wide Flange");
            _defaultDoor      = FindSymbol("Doors", "Single-Flush", "Door");
            _defaultWindow    = FindSymbol("Windows", "Fixed", "Window");
        }

        public Element? MapElement(AcIfcElement el)
        {
            string key1 = (el.IfcType + "|" + el.PredefinedType).ToUpperInvariant();
            string key2 = (el.IfcType + "|").ToUpperInvariant();
            _typeLookup.TryGetValue(key1, out var mapping);
            mapping ??= _typeLookup.GetValueOrDefault(key2);

            ElementId levelId = _lm.GetLevel(el.StoreyId);

            try
            {
                if (mapping != null && !mapping.UseDirectShape)
                {
                    Element? native = el.IfcType.ToUpperInvariant() switch
                    {
                        "IFCWALL" or "IFCWALLSTANDARDCASE" => CreateWall(el, levelId),
                        "IFCSLAB"                          => CreateFloor(el, levelId),
                        "IFCCOLUMN"                        => CreateColumn(el, levelId),
                        "IFCBEAM" or "IFCMEMBER"           => CreateBeam(el, levelId),
                        "IFCDOOR"                          => CreateDoor(el, levelId),
                        "IFCWINDOW"                        => CreateWindow(el, levelId),
                        "IFCSPACE"                         => CreateRoom(el, levelId),
                        _                                  => null
                    };
                    if (native != null) return native;
                }
                return CreateDirectShape(el, mapping?.RevitCategory ?? "");
            }
            catch (Exception ex)
            {
                Warnings.Add($"{el.IfcType} '{el.Name}': {ex.Message} → DirectShape");
                return CreateDirectShape(el, mapping?.RevitCategory ?? "");
            }
        }

        // ── Wall ─────────────────────────────────────────────────────────────
        private Element? CreateWall(AcIfcElement el, ElementId levelId)
        {
            if (levelId == ElementId.InvalidElementId) return CreateDirectShape(el, "");

            // Prefer extracted axis; fall back to computing from insertion + bbox
            XYZ start, end;
            if (el.AxisStart != null && el.AxisEnd != null)
            {
                start = _c.ToRevit(el.AxisStart);
                end   = _c.ToRevit(el.AxisEnd);
            }
            else
            {
                // Synthesise from insertion point + X-direction of wall + half-length
                double halfLen = el.BboxHalfX > 0 ? el.BboxHalfX : 1.5; // 1.5 m default
                start = _c.ToRevit(el.InsertionPoint[0] - halfLen,
                                   el.InsertionPoint[1], el.InsertionPoint[2]);
                end   = _c.ToRevit(el.InsertionPoint[0] + halfLen,
                                   el.InsertionPoint[1], el.InsertionPoint[2]);
            }

            if (start.DistanceTo(end) < 1.0 / 12.0) return CreateDirectShape(el, ""); // < 1 inch

            double heightFt = el.Height > 0
                ? _c.ScaleToRevit(el.Height)
                : TryPropLength(el, "Pset_WallCommon.Height", 3.0);

            // Pick wall type by ArchiCAD reference name
            WallType? wt = PickWallType(el);
            Level? lv = _doc.GetElement(levelId) as Level;
            if (lv == null) return CreateDirectShape(el, "");

            Wall wall = wt != null
                ? Wall.Create(_doc, Line.CreateBound(start, end), wt.Id, levelId, heightFt, 0, false, false)
                : Wall.Create(_doc, Line.CreateBound(start, end), levelId, false);

            Stamp(wall, el); CreatedNative++; return wall;
        }

        // ── Floor (Revit 2025+ API) ───────────────────────────────────────────
        private Element? CreateFloor(AcIfcElement el, ElementId levelId)
        {
            if (levelId == ElementId.InvalidElementId || _defaultFloorType == null)
                return CreateDirectShape(el, "");

            XYZ ins = _c.ToRevit(el.InsertionPoint);
            double hx = el.BboxHalfX > 0 ? _c.ScaleToRevit(el.BboxHalfX) : 3.0;
            double hy = el.BboxHalfY > 0 ? _c.ScaleToRevit(el.BboxHalfY) : 3.0;

            // Build rectangular boundary centred on insertion point
            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(new XYZ(ins.X-hx, ins.Y-hy, ins.Z), new XYZ(ins.X+hx, ins.Y-hy, ins.Z)));
            loop.Append(Line.CreateBound(new XYZ(ins.X+hx, ins.Y-hy, ins.Z), new XYZ(ins.X+hx, ins.Y+hy, ins.Z)));
            loop.Append(Line.CreateBound(new XYZ(ins.X+hx, ins.Y+hy, ins.Z), new XYZ(ins.X-hx, ins.Y+hy, ins.Z)));
            loop.Append(Line.CreateBound(new XYZ(ins.X-hx, ins.Y+hy, ins.Z), new XYZ(ins.X-hx, ins.Y-hy, ins.Z)));

            try
            {
                Floor floor = Floor.Create(_doc, new List<CurveLoop> { loop },
                    _defaultFloorType.Id, levelId);
                Stamp(floor, el); CreatedNative++; return floor;
            }
            catch { return CreateDirectShape(el, "OST_Floors"); }
        }

        // ── Column ────────────────────────────────────────────────────────────
        private Element? CreateColumn(AcIfcElement el, ElementId levelId)
        {
            if (_defaultColumn == null || levelId == ElementId.InvalidElementId)
                return CreateDirectShape(el, "");
            if (!_defaultColumn.IsActive) _defaultColumn.Activate();
            Level? lv = _doc.GetElement(levelId) as Level;
            if (lv == null) return CreateDirectShape(el, "");

            XYZ ins = _c.ToRevit(el.InsertionPoint);
            FamilyInstance fi = _doc.Create.NewFamilyInstance(
                ins, _defaultColumn, lv,
                Autodesk.Revit.DB.Structure.StructuralType.Column);
            Stamp(fi, el); CreatedNative++; return fi;
        }

        // ── Beam ──────────────────────────────────────────────────────────────
        private Element? CreateBeam(AcIfcElement el, ElementId levelId)
        {
            if (_defaultBeam == null) return CreateDirectShape(el, "");
            if (!_defaultBeam.IsActive) _defaultBeam.Activate();
            Level? lv = levelId != ElementId.InvalidElementId
                ? _doc.GetElement(levelId) as Level : null;

            XYZ s = _c.ToRevit(el.AxisStart ?? el.InsertionPoint);
            double halfLen = el.BboxHalfX > 0 ? _c.ScaleToRevit(el.BboxHalfX) : 1.5;
            XYZ e2 = el.AxisEnd != null ? _c.ToRevit(el.AxisEnd)
                : new XYZ(s.X + halfLen * 2, s.Y, s.Z);

            if (s.DistanceTo(e2) < 1.0 / 12.0) return CreateDirectShape(el, "");
            FamilyInstance fi = _doc.Create.NewFamilyInstance(
                Line.CreateBound(s, e2), _defaultBeam, lv,
                Autodesk.Revit.DB.Structure.StructuralType.Beam);
            Stamp(fi, el); CreatedNative++; return fi;
        }

        // ── Door ──────────────────────────────────────────────────────────────
        private Element? CreateDoor(AcIfcElement el, ElementId levelId)
        {
            if (_defaultDoor == null) return CreateDirectShape(el, "OST_Doors");
            if (!_defaultDoor.IsActive) _defaultDoor.Activate();
            Level? lv = levelId != ElementId.InvalidElementId
                ? _doc.GetElement(levelId) as Level : null;
            if (lv == null) return CreateDirectShape(el, "OST_Doors");

            XYZ ins  = _c.ToRevit(el.InsertionPoint);
            Wall? host = FindNearestWall(ins);
            if (host == null) return CreateDirectShape(el, "OST_Doors");

            FamilyInstance fi = _doc.Create.NewFamilyInstance(
                ins, _defaultDoor, host, lv,
                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            Stamp(fi, el); CreatedNative++; return fi;
        }

        // ── Window ────────────────────────────────────────────────────────────
        private Element? CreateWindow(AcIfcElement el, ElementId levelId)
        {
            if (_defaultWindow == null) return CreateDirectShape(el, "OST_Windows");
            if (!_defaultWindow.IsActive) _defaultWindow.Activate();
            Level? lv = levelId != ElementId.InvalidElementId
                ? _doc.GetElement(levelId) as Level : null;
            if (lv == null) return CreateDirectShape(el, "OST_Windows");

            XYZ ins  = _c.ToRevit(el.InsertionPoint);
            Wall? host = FindNearestWall(ins);
            if (host == null) return CreateDirectShape(el, "OST_Windows");

            FamilyInstance fi = _doc.Create.NewFamilyInstance(
                ins, _defaultWindow, host, lv,
                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            Stamp(fi, el); CreatedNative++; return fi;
        }

        // ── Room ──────────────────────────────────────────────────────────────
        private Element? CreateRoom(AcIfcElement el, ElementId levelId)
        {
            if (levelId == ElementId.InvalidElementId) return null;
            Level? lv = _doc.GetElement(levelId) as Level;
            if (lv == null) return null;
            XYZ ins = _c.ToRevit(el.InsertionPoint);
            Room? room = _doc.Create.NewRoom(lv, new UV(ins.X, ins.Y));
            if (room != null) { room.Name = el.Name; Stamp(room, el); CreatedNative++; }
            return room;
        }

        // ── DirectShape ───────────────────────────────────────────────────────
        private Element? CreateDirectShape(AcIfcElement el, string categoryHint)
        {
            BuiltInCategory bic = ResolveBic(el.IfcType, categoryHint);
            DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(bic));
            ds.Name = string.IsNullOrEmpty(el.Name) ? el.IfcType : el.Name;

            // Prefer real tessellation when we parsed brep faces; fall back to a box.
            IList<GeometryObject>? shape = null;
            if (el.BrepTriangles.Count > 0)
                shape = BuildTessellatedShape(el);

            if (shape == null || shape.Count == 0)
            {
                var solid = BuildBoxSolid(el);
                shape = solid != null ? new List<GeometryObject> { solid } : null;
            }

            if (shape != null && shape.Count > 0)
                ds.SetShape(shape);

            Stamp(ds, el);
            CreatedDirect++;
            return ds;
        }

        private IList<GeometryObject>? BuildTessellatedShape(AcIfcElement el)
        {
            // Try as a closed (manifold) shell first; if Revit rejects it we retry as an
            // open shell which is permissive about self-intersection / non-manifold edges.
            var open = TryBuildTessellated(el, closed: true);
            if (open != null && open.Count > 0) return open;
            return TryBuildTessellated(el, closed: false);
        }

        private IList<GeometryObject>? TryBuildTessellated(AcIfcElement el, bool closed)
        {
            try
            {
                var tsb = new TessellatedShapeBuilder();
                tsb.OpenConnectedFaceSet(closed);
                int added = 0;
                foreach (var t in el.BrepTriangles)
                {
                    XYZ a = _c.ToRevit(t[0], t[1], t[2]);
                    XYZ b = _c.ToRevit(t[3], t[4], t[5]);
                    XYZ c = _c.ToRevit(t[6], t[7], t[8]);
                    if (a.DistanceTo(b) < 1e-6 || b.DistanceTo(c) < 1e-6 || a.DistanceTo(c) < 1e-6)
                        continue; // skip degenerate
                    tsb.AddFace(new TessellatedFace(
                        new List<XYZ> { a, b, c }, ElementId.InvalidElementId));
                    added++;
                }
                tsb.CloseConnectedFaceSet();
                if (added == 0) return null;

                tsb.Target = TessellatedShapeBuilderTarget.AnyGeometry;
                tsb.Fallback = TessellatedShapeBuilderFallback.Mesh;
                tsb.Build();
                var result = tsb.GetBuildResult();
                var geom = result.GetGeometricalObjects();
                return geom.Count > 0 ? geom : null;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn(
                    $"BrepTess(closed={closed}) {el.IfcType} '{el.Name}': {ex.Message}");
                return null;
            }
        }

        private Solid? BuildBoxSolid(AcIfcElement el)
        {
            try
            {
                XYZ ctr = _c.ToRevit(el.InsertionPoint);
                double hx = el.BboxHalfX > 0 ? _c.ScaleToRevit(el.BboxHalfX) : 0.5;
                double hy = el.BboxHalfY > 0 ? _c.ScaleToRevit(el.BboxHalfY) : 0.5;
                double h  = el.Height    > 0 ? _c.ScaleToRevit(el.Height)    : 1.0;

                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(new XYZ(ctr.X-hx,ctr.Y-hy,ctr.Z), new XYZ(ctr.X+hx,ctr.Y-hy,ctr.Z)));
                loop.Append(Line.CreateBound(new XYZ(ctr.X+hx,ctr.Y-hy,ctr.Z), new XYZ(ctr.X+hx,ctr.Y+hy,ctr.Z)));
                loop.Append(Line.CreateBound(new XYZ(ctr.X+hx,ctr.Y+hy,ctr.Z), new XYZ(ctr.X-hx,ctr.Y+hy,ctr.Z)));
                loop.Append(Line.CreateBound(new XYZ(ctr.X-hx,ctr.Y+hy,ctr.Z), new XYZ(ctr.X-hx,ctr.Y-hy,ctr.Z)));
                return GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop }, XYZ.BasisZ, h);
            }
            catch { return null; }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private FamilySymbol? FindSymbol(params string[] keywords)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => keywords.Any(kw =>
                    s.Family?.FamilyCategory?.Name?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Family?.Name?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private WallType? PickWallType(AcIfcElement el)
        {
            string hint = el.Properties.GetValueOrDefault("Pset_WallCommon.Reference")
                       ?? el.Properties.GetValueOrDefault("ArchiCAD_PropertyGroup_General.BuildingMaterialName")
                       ?? "";
            if (hint.Length > 0)
            {
                var match = _allWallTypes.FirstOrDefault(wt =>
                    wt.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }
            return _defaultWallType;
        }

        private Wall? FindNearestWall(XYZ point)
        {
            if (!_wallsLoaded)
            {
                _allWalls = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .OfClass(typeof(Wall)).Cast<Wall>().ToList();
                _wallsLoaded = true;
            }
            return _allWalls
                .Where(w => w.Location is LocationCurve)
                .OrderBy(w => ((LocationCurve)w.Location).Curve.Distance(point))
                .FirstOrDefault();
        }

        private double TryPropLength(AcIfcElement el, string key, double defaultM)
        {
            if (el.Properties.TryGetValue(key, out string? v) &&
                double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double m))
                return _c.ScaleToRevit(m);
            return _c.ScaleToRevit(defaultM);
        }

        private static BuiltInCategory ResolveBic(string ifcType, string hint)
        {
            if (hint.StartsWith("OST_") && Enum.TryParse<BuiltInCategory>(hint, out var bic2))
                return bic2;
            return ifcType.ToUpperInvariant() switch
            {
                "IFCWALL" or "IFCWALLSTANDARDCASE" => BuiltInCategory.OST_Walls,
                "IFCSLAB"                          => BuiltInCategory.OST_Floors,
                "IFCCOLUMN"                        => BuiltInCategory.OST_StructuralColumns,
                "IFCBEAM" or "IFCMEMBER"           => BuiltInCategory.OST_StructuralFraming,
                "IFCDOOR"                          => BuiltInCategory.OST_Doors,
                "IFCWINDOW"                        => BuiltInCategory.OST_Windows,
                "IFCSPACE"                         => BuiltInCategory.OST_Rooms,
                "IFCROOF"                          => BuiltInCategory.OST_Roofs,
                "IFCRAILING"                       => BuiltInCategory.OST_StairsRailing,
                "IFCSTAIR" or "IFCSTAIRFLIGHT"     => BuiltInCategory.OST_Stairs,
                _                                  => BuiltInCategory.OST_GenericModel
            };
        }

        private static void Stamp(Element el, AcIfcElement src)
        {
            var p1 = el.LookupParameter("ARCHICAD_GUID")
                  ?? el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            p1?.Set("AC:" + src.GlobalId);

            if (!string.IsNullOrEmpty(src.Name))
            {
                var p2 = el.LookupParameter("ARCHICAD_ELEMENT_NAME")
                      ?? el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                p2?.Set(src.Name);
            }
        }
    }

    // =========================================================================
    //  Property mapper
    // =========================================================================

    public sealed class ArchiCadPropertyMapper
    {
        private readonly List<AcIfcPropMapping> _mappings;
        public int Written { get; private set; }

        public ArchiCadPropertyMapper(AcIfcMappingConfig config)
            => _mappings = config.PropMappings;

        public void Apply(Element revitEl, AcIfcElement src)
        {
            // Post-process: write predominant material layer to STING param
            ApplyMaterialLayer(revitEl, src);

            foreach (var m in _mappings)
            {
                // Change 3: element-type filter
                if (m.ElementTypes.Count > 0 &&
                    !m.ElementTypes.Contains(src.IfcType, StringComparer.OrdinalIgnoreCase))
                    continue;

                // Change 4: scan_all_psets — when pset is empty or notes says scan_all_psets,
                // search all psets for the first occurrence of the property name.
                string? val = null;
                bool scanAll = string.IsNullOrEmpty(m.ArchiCadPset)
                    || m.Notes.IndexOf("scan_all_psets", StringComparison.OrdinalIgnoreCase) >= 0;

                if (scanAll)
                {
                    // Iterate all properties looking for any key ending with ".<prop>"
                    string suffix = "." + m.ArchiCadProp;
                    foreach (var kv in src.Properties)
                    {
                        if (kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(kv.Value))
                        { val = kv.Value; break; }
                    }
                }
                else
                {
                    src.Properties.TryGetValue($"{m.ArchiCadPset}.{m.ArchiCadProp}", out val);
                }

                if (string.IsNullOrWhiteSpace(val)) continue;

                bool wrote = false;
                if (!string.IsNullOrEmpty(m.StingParam))
                    wrote = Write(revitEl.LookupParameter(m.StingParam), val);
                if (!wrote && !string.IsNullOrEmpty(m.RevitBuiltIn) &&
                    Enum.TryParse<BuiltInParameter>(m.RevitBuiltIn, out var bip))
                    wrote = Write(revitEl.get_Parameter(bip), val);
                if (wrote) Written++;
            }
        }

        // Change 6: write predominant material layer to a STING parameter
        private static void ApplyMaterialLayer(Element revitEl, AcIfcElement src)
        {
            try
            {
                // Write predominant material
                if (src.Properties.TryGetValue("IfcMaterialLayer.PredominantMaterial", out string? matName)
                    && !string.IsNullOrEmpty(matName))
                {
                    var p = revitEl.LookupParameter("STING_MATERIAL_TXT")
                         ?? revitEl.LookupParameter("MAT_FINISH_TXT")
                         ?? revitEl.LookupParameter("ASS_SYSTEM_TYPE_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String
                        && string.IsNullOrEmpty(p.AsString()))
                        p.Set(matName);
                }

                // Write full layer composition string
                if (src.Properties.TryGetValue("IfcMaterialLayer.LayerComposition", out string? composition)
                    && !string.IsNullOrEmpty(composition))
                {
                    var pc = revitEl.LookupParameter("MAT_LAYER_COMPOSITION_TXT");
                    if (pc != null && !pc.IsReadOnly && pc.StorageType == StorageType.String
                        && string.IsNullOrEmpty(pc.AsString()))
                        pc.Set(composition);
                }

                // Write zone name to ASS_ZONE_TXT if empty
                if (src.Properties.TryGetValue("IfcZone.Name", out string? zoneName)
                    && !string.IsNullOrEmpty(zoneName))
                {
                    var pz = revitEl.LookupParameter("ASS_ZONE_TXT");
                    if (pz != null && !pz.IsReadOnly && pz.StorageType == StorageType.String
                        && string.IsNullOrEmpty(pz.AsString()))
                        pz.Set(zoneName);
                }

                // ── Spatial hierarchy properties ──────────────────────────────
                if (src.Properties.TryGetValue("IfcHierarchy.Space", out string? spaceName)
                    && !string.IsNullOrEmpty(spaceName))
                {
                    var p = revitEl.LookupParameter("ASS_ROOM_TXT")
                         ?? revitEl.LookupParameter("ROOM_NAME_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String
                        && string.IsNullOrEmpty(p.AsString()))
                        p.Set(spaceName);
                }

                if (src.Properties.TryGetValue("IfcHierarchy.Building", out string? bldName)
                    && !string.IsNullOrEmpty(bldName))
                {
                    var p = revitEl.LookupParameter("ASS_LOC_TXT")
                         ?? revitEl.LookupParameter("BLD_NAME_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String
                        && string.IsNullOrEmpty(p.AsString()))
                        p.Set(bldName);
                }

                // ── Derived quantity properties ────────────────────────────────
                if (src.Properties.TryGetValue("DerivedQty.NetSideArea_m2", out string? netArea))
                {
                    var p = revitEl.LookupParameter("STING_NET_AREA_M2_TXT")
                         ?? revitEl.LookupParameter("NET_AREA_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                        p.Set(netArea);
                }

                if (src.Properties.TryGetValue("DerivedQty.CostTotal", out string? costTotal))
                {
                    var p = revitEl.LookupParameter("CST_TOTAL_TXT")
                         ?? revitEl.LookupParameter("STING_COST_TOTAL_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                        p.Set(costTotal);
                }

                // ── Write STING token params from PopulateStingTokens() results ──

                // STING.ZONE → ASS_ZONE_TXT
                if (src.Properties.TryGetValue("STING.ZONE", out string? stingZone)
                    && !string.IsNullOrEmpty(stingZone))
                {
                    var p = revitEl.LookupParameter("ASS_ZONE_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String
                        && string.IsNullOrEmpty(p.AsString()))
                        p.Set(stingZone);
                }

                // STING.LVL → ASS_LVL_COD_TXT
                if (src.Properties.TryGetValue("STING.LVL", out string? stingLvl)
                    && !string.IsNullOrEmpty(stingLvl))
                {
                    var p = revitEl.LookupParameter("ASS_LVL_COD_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String
                        && string.IsNullOrEmpty(p.AsString()))
                        p.Set(stingLvl);
                }

                // STING.STATUS → ASS_STATUS_TXT / STATUS_TXT
                if (src.Properties.TryGetValue("STING.STATUS", out string? stingStatus)
                    && !string.IsNullOrEmpty(stingStatus))
                {
                    var p = revitEl.LookupParameter("ASS_STATUS_TXT")
                         ?? revitEl.LookupParameter("STATUS_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String
                        && string.IsNullOrEmpty(p.AsString()))
                        p.Set(stingStatus);
                }

                // STING.DISC → ASS_DISCIPLINE_COD_TXT
                if (src.Properties.TryGetValue("STING.DISC", out string? stingDisc)
                    && !string.IsNullOrEmpty(stingDisc))
                {
                    var p = revitEl.LookupParameter("ASS_DISCIPLINE_COD_TXT");
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String
                        && string.IsNullOrEmpty(p.AsString()))
                        p.Set(stingDisc);
                }
            }
            catch (Exception ex) { StingLog.Warn("ApplyMaterialLayer: " + ex.Message); }
        }

        private static bool Write(Parameter? p, string val)
        {
            if (p == null || p.IsReadOnly) return false;
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        if (p.AsString() == val) return false;
                        p.Set(val); return true;
                    case StorageType.Double when double.TryParse(val, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double d):
                        p.Set(d); return true;
                    case StorageType.Integer when int.TryParse(val, out int i):
                        p.Set(i); return true;
                }
            }
            catch { }
            return false;
        }
    }

    // =========================================================================
    //  Import result
    // =========================================================================

    public sealed class ArchiCadImportResult
    {
        public int    Total              { get; set; }
        public int    Native             { get; set; }
        public int    Direct             { get; set; }
        public int    Skipped            { get; set; }
        public int    LevelsMatched      { get; set; }
        public int    LevelsCreated      { get; set; }
        public int    PropsWritten       { get; set; }
        public int    GeometryFallbacks   { get; set; }
        public int    WithHierarchy       { get; set; }
        public int    WithDerivedQty      { get; set; }
        public int    WithCostTotal       { get; set; }
        public double UnitScale       { get; set; }
        public double SiteOx          { get; set; }
        public double SiteOy          { get; set; }
        public double SiteOz          { get; set; }
        public double SiteRotDeg      { get; set; }
        public List<string> Warnings  { get; } = new();

        public string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("── ArchiCAD IFC Import Complete ──────────────────────");
            sb.AppendLine($"IFC unit scale    : {UnitScale} → metres");
            sb.AppendLine($"Site origin (IFC units): X={SiteOx:F3}  Y={SiteOy:F3}  Z={SiteOz:F3}");
            sb.AppendLine($"Site rotation     : {SiteRotDeg:F2}°");
            sb.AppendLine();
            sb.AppendLine($"Elements total    : {Total}");
            sb.AppendLine($"  Native Revit    : {Native}");
            sb.AppendLine($"  DirectShape     : {Direct}");
            sb.AppendLine($"  Skipped         : {Skipped}");
            sb.AppendLine($"Levels matched    : {LevelsMatched}");
            sb.AppendLine($"Levels created    : {LevelsCreated}");
            sb.AppendLine($"Properties written: {PropsWritten}");
            if (GeometryFallbacks > 0)
                sb.AppendLine($"  Geometry fallback : {GeometryFallbacks} elements used AABB (approx)");
            if (WithHierarchy > 0)
                sb.AppendLine($"Spatial hierarchy : {WithHierarchy} elements have building/storey/space");
            if (WithDerivedQty > 0)
                sb.AppendLine($"Derived quantities: {WithDerivedQty} elements (net area/vol); {WithCostTotal} with cost totals");
            if (Warnings.Count > 0)
            {
                sb.AppendLine($"\nWarnings ({Warnings.Count}):");
                foreach (string w in Warnings.Take(20)) sb.AppendLine("  • " + w);
                if (Warnings.Count > 20)
                    sb.AppendLine($"  … and {Warnings.Count - 20} more (see STING log)");
            }
            return sb.ToString();
        }
    }

    // =========================================================================
    //  Main command
    // =========================================================================

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ArchiCadIfcImportCommand : IExternalCommand
    {
        private const string MapFile = "ARCHICAD_IFC_MAPPING.json";

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            Document doc = data.Application.ActiveUIDocument.Document;

            AcIfcMappingConfig? config = LoadConfig();
            if (config == null)
            {
                TaskDialog.Show("ArchiCAD IFC Import",
                    $"{MapFile} not found in STING data directory.");
                return Result.Failed;
            }

            string ifcPath = PickFile();
            if (string.IsNullOrEmpty(ifcPath)) return Result.Cancelled;

            StingLog.Info($"ArchiCAD IFC Import: parsing {ifcPath}");
            ArchiCadIfcParser parser = ArchiCadIfcParser.ParseFile(ifcPath);

            if (parser.Elements.Count == 0 && parser.Warnings.Count > 0)
            {
                TaskDialog.Show("ArchiCAD IFC Import — Parse Error",
                    string.Join("\n", parser.Warnings.Take(10)));
                return Result.Failed;
            }

            var dlg = new ArchiCadImportDialog(parser);
            if (!dlg.ShowDialog()) return Result.Cancelled;

            var aligner = new ArchiCadCoordinateAligner(parser.SiteWorldTransform, parser.UnitScale);
            aligner.SetUserOffset(dlg.UserOffsetX, dlg.UserOffsetY, dlg.UserOffsetZ);

            var lm   = new ArchiCadLevelMatcher(doc, aligner);
            var em   = new ArchiCadElementMapper(doc, aligner, lm, config);
            var pm   = new ArchiCadPropertyMapper(config);

            var result = new ArchiCadImportResult
            {
                Total      = parser.Elements.Count,
                UnitScale  = parser.UnitScale,
                SiteOx     = parser.SiteWorldTransform.Tx,
                SiteOy     = parser.SiteWorldTransform.Ty,
                SiteOz     = parser.SiteWorldTransform.Tz,
                SiteRotDeg = Math.Atan2(parser.SiteWorldTransform.Xy,
                                        parser.SiteWorldTransform.Xx) * 180 / Math.PI
            };
            result.Warnings.AddRange(parser.Warnings);

            using var tg = new TransactionGroup(doc, "STING — ArchiCAD IFC Import");
            tg.Start();

            using (var t = new Transaction(doc, "STING — Match Levels"))
            {
                t.Start();
                var lvlWarn = new List<string>();
                lm.MatchStoreys(parser.Storeys, dlg.CreateMissingLevels, lvlWarn);
                result.Warnings.AddRange(lvlWarn);
                result.LevelsMatched = parser.Storeys.Count(s => s.RevitLevelId >= 0 && !s.WasCreated);
                result.LevelsCreated = parser.Storeys.Count(s => s.WasCreated);
                t.Commit();
            }

            using (var t = new Transaction(doc, "STING — Create ArchiCAD Elements"))
            {
                t.Start();
                var fh = t.GetFailureHandlingOptions()
                          .SetFailuresPreprocessor(new SuppressWarnings());
                t.SetFailureHandlingOptions(fh);

                int n = 0;
                foreach (var el in parser.Elements)
                {
                    Element? created = em.MapElement(el);
                    if (created != null) pm.Apply(created, el);
                    if (++n % 200 == 0)
                        StingLog.Info($"  {n}/{parser.Elements.Count} elements");
                }
                t.Commit();
            }

            result.Native             = em.CreatedNative;
            result.Direct             = em.CreatedDirect;
            result.Skipped            = em.Skipped;
            result.PropsWritten       = pm.Written;
            result.GeometryFallbacks  = parser.Elements.Count(e => e.GeometryIsAabbFallback);
            result.WithHierarchy      = parser.Elements.Count(e => e.Properties.ContainsKey("IfcHierarchy.Storey"));
            result.WithDerivedQty     = parser.Elements.Count(e => e.Properties.ContainsKey("DerivedQty.NetSideArea_m2")
                                                                 || e.Properties.ContainsKey("DerivedQty.NetFloorArea_m2"));
            result.WithCostTotal      = parser.Elements.Count(e => e.Properties.ContainsKey("DerivedQty.CostTotal"));
            result.Warnings.AddRange(em.Warnings);
            tg.Assimilate();

            // Warn + write an ArchiCAD translator override file when no Qto sets were found
            if (!parser.HasQuantitySets)
            {
                string qtoGuide = WriteQtoTranslatorGuide(ifcPath);
                result.Warnings.Insert(0,
                    "⚠ No quantity sets (Qto) found. Cost extraction and area/volume data are unavailable. " +
                    "To fix: in ArchiCAD open File → Interoperability → IFC → IFC Translator, select your " +
                    "translator, click Edit, go to 'Properties of IFC Elements' tab, enable " +
                    "'Export Quantity Sets (Qto)' checkbox, and re-export. " +
                    (string.IsNullOrEmpty(qtoGuide) ? "" :
                        $"A step-by-step guide has been written to: {qtoGuide}"));
            }

            foreach (string w in result.Warnings) StingLog.Warn("ArchiCAD: " + w);

            new TaskDialog("ArchiCAD IFC Import — Complete")
            {
                MainInstruction = "Import finished",
                MainContent     = result.BuildReport(),
                CommonButtons   = TaskDialogCommonButtons.Ok
            }.Show();

            return Result.Succeeded;
        }

        private static AcIfcMappingConfig? LoadConfig()
        {
            string path = StingToolsApp.FindDataFile(MapFile);
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<AcIfcMappingConfig>(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Error("ArchiCAD config load", ex); return null; }
        }

        /// <summary>
        /// Writes an ArchiCAD-specific Qto-enable guide file next to the IFC export.
        /// Returns the path written, or empty string if it could not be written.
        /// </summary>
        private static string WriteQtoTranslatorGuide(string ifcPath)
        {
            try
            {
                string dir  = Path.GetDirectoryName(ifcPath) ?? "";
                string name = Path.GetFileNameWithoutExtension(ifcPath);
                string guidePath = Path.Combine(dir, $"{name}_ENABLE_QTO_GUIDE.txt");
                File.WriteAllText(guidePath, @"HOW TO ENABLE QUANTITY SETS (Qto) IN ARCHICAD IFC TRANSLATOR
==============================================================

Without Qto, STING cannot extract wall areas, slab volumes, or compute
cost estimates from this ArchiCAD model. Follow these steps once:

ARCHICAD 25 / 26 / 27
1. Open File → Interoperability → IFC → IFC Translator Settings...
2. Select the translator you use for export (e.g. 'Design Transfer View').
3. Click 'Duplicate' to make a project-specific copy (name it e.g. 'STING Export').
4. Click 'Edit...' and go to the 'Properties of IFC Elements' tab.
5. Tick the checkbox: 'Export Quantity Sets (Qto)' (may be labelled
   'Export Base Quantities' in older versions).
6. Click OK → Save.
7. Re-export the model using the new 'STING Export' translator.
8. Re-import into STING using the new .ifc file.

ARCHICAD 24 AND EARLIER
Steps are the same; the checkbox is under
File → Interoperability → IFC → IFC Translator → Edit → 'Quantities' tab.

VERIFYING THE FIX
Open the .ifc file in a text editor and search for 'IFCELEMENTQUANTITY'.
You should find entries like:
  #1234= IFCELEMENTQUANTITY('...',..,'Qto_WallBaseQuantities',...,(...));

QUANTITIES STING USES
  Qto_WallBaseQuantities.GrossSideArea / NetSideArea / Width / GrossVolume
  Qto_SlabBaseQuantities.GrossArea / GrossVolume / Width
  Qto_SpaceBaseQuantities.GrossFloorArea / NetFloorArea / GrossVolume
  Qto_OpeningElementBaseQuantities.Area
  BaseQuantities.Area / Volume (ArchiCAD fallback names)

If you need help, see: https://help.graphisoft.com/AC/27/INT/ArchiCAD-27_Help/020_Interoperability/020_Interoperability-14.htm
");
                return guidePath;
            }
            catch { return ""; }
        }

        private static string PickFile()
        {
            using var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title  = "Select ArchiCAD IFC Export",
                Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*"
            };
            return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : "";
        }
    }

    // =========================================================================
    //  Failure preprocessor
    // =========================================================================

    internal sealed class SuppressWarnings : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            foreach (var m in fa.GetFailureMessages())
                if (m.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(m);
            return FailureProcessingResult.Continue;
        }
    }

    // =========================================================================
    //  Import dialog
    // =========================================================================

    public sealed class ArchiCadImportDialog
    {
        private readonly ArchiCadIfcParser _p;

        public bool   CreateMissingLevels { get; private set; } = true;
        public double UserOffsetX { get; private set; }
        public double UserOffsetY { get; private set; }
        public double UserOffsetZ { get; private set; }

        public ArchiCadImportDialog(ArchiCadIfcParser p) => _p = p;

        public bool ShowDialog()
        {
            var win = new System.Windows.Window
            {
                Title   = "ArchiCAD IFC Import — Alignment",
                Width   = 580, Height = 500,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                Background = System.Windows.Media.Brushes.WhiteSmoke
            };

            var sp = new System.Windows.Controls.StackPanel
                { Margin = new System.Windows.Thickness(16) };

            Add(sp, Bold("ArchiCAD IFC Import — Coordinate Alignment", 15));
            Add(sp, Lbl($"Project: {_p.ProjectName}"));

            Mat4 s = _p.SiteWorldTransform;
            double rotDeg = Math.Atan2(s.Xy, s.Xx) * 180 / Math.PI;
            Add(sp, Lbl($"IFC unit scale  : {_p.UnitScale} (1.0 = metres, 0.001 = mm)"));
            Add(sp, Lbl($"Site origin     : X={s.Tx:F3}  Y={s.Ty:F3}  Z={s.Tz:F3}  (IFC units)"));
            Add(sp, Lbl($"Site rotation   : {rotDeg:F2}°  (auto-corrected)"));
            Add(sp, Lbl($"Storeys : {_p.Storeys.Count}   Elements : {_p.Elements.Count}"));
            Add(sp, new System.Windows.Controls.Separator
                { Margin = new System.Windows.Thickness(0,8,0,8) });

            Add(sp, Lbl("Additional manual offset in Revit internal feet (leave at 0 unless auto-alignment is off):"));

            var (tbX, tbY, tbZ) = (TB(), TB(), TB());
            var gr = new System.Windows.Controls.Grid();
            for (int i=0; i<6; i++)
                gr.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                    { Width = new System.Windows.GridLength(i%2==0?40:80) });
            SetCell(gr, Lbl("ΔX:"), 0); SetCell(gr, tbX, 1);
            SetCell(gr, Lbl("ΔY:"), 2); SetCell(gr, tbY, 3);
            SetCell(gr, Lbl("ΔZ:"), 4); SetCell(gr, tbZ, 5);
            Add(sp, gr);
            Add(sp, new System.Windows.Controls.Separator
                { Margin = new System.Windows.Thickness(0,8,0,8) });

            var cbLevels = new System.Windows.Controls.CheckBox
            {
                Content   = "Create Revit levels for ArchiCAD storeys that have no match (±5 mm)",
                IsChecked = true, Margin = new System.Windows.Thickness(0,4,0,4)
            };
            Add(sp, cbLevels);

            Add(sp, Lbl("Storeys detected:"));
            var lv = new System.Windows.Controls.ListView
                { Height = 90, Margin = new System.Windows.Thickness(0,0,0,8) };
            foreach (var st in _p.Storeys)
                lv.Items.Add($"{st.Name}  —  {st.AbsoluteElevM:F3} m");
            Add(sp, lv);

            var btnRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var ok  = Btn("Import", true);
            var can = Btn("Cancel", false);
            btnRow.Children.Add(ok); btnRow.Children.Add(can);
            Add(sp, btnRow);

            bool confirmed = false;
            ok.Click  += (_, _) => { confirmed = true;  win.Close(); };
            can.Click += (_, _) => { confirmed = false; win.Close(); };

            win.Content = sp;
            win.ShowDialog();
            if (!confirmed) return false;

            CreateMissingLevels = cbLevels.IsChecked == true;
            double.TryParse(tbX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dx);
            double.TryParse(tbY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dy);
            double.TryParse(tbZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dz);
            UserOffsetX = dx; UserOffsetY = dy; UserOffsetZ = dz;
            return true;
        }

        private static void Add(System.Windows.Controls.Panel p, System.Windows.UIElement e)
            => p.Children.Add(e);

        private static System.Windows.Controls.TextBlock Lbl(string t) =>
            new() { Text = t, Margin = new System.Windows.Thickness(0,2,0,2),
                    TextWrapping = System.Windows.TextWrapping.Wrap };

        private static System.Windows.Controls.TextBlock Bold(string t, double size) =>
            new() { Text = t, FontSize = size,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Margin = new System.Windows.Thickness(0,0,0,8) };

        private static System.Windows.Controls.TextBox TB() =>
            new() { Text = "0", Width = 72, Margin = new System.Windows.Thickness(2) };

        private static System.Windows.Controls.Button Btn(string label, bool isDefault) =>
            new() { Content = label, Width = 80,
                    Margin = new System.Windows.Thickness(4), IsDefault = isDefault };

        private static void SetCell(System.Windows.Controls.Grid g,
            System.Windows.UIElement el, int col)
        {
            System.Windows.Controls.Grid.SetColumn(el, col);
            g.Children.Add(el);
        }
    }
}

