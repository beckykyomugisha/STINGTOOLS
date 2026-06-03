// Revit API CI Stubs — Autodesk.Revit.DB element types
using System;
using System.Collections.Generic;

namespace Autodesk.Revit.DB
{
    // ── Collector ────────────────────────────────────────────────────────────
    public class FilteredElementCollector : IEnumerable<Element>
    {
        public FilteredElementCollector(Document doc) { }
        public FilteredElementCollector(Document doc, ElementId viewId) { }
        public FilteredElementCollector(Document doc, ICollection<ElementId> ids) { }
        public FilteredElementCollector OfClass(Type type) => this;
        public FilteredElementCollector OfCategory(BuiltInCategory cat) => this;
        public FilteredElementCollector WhereElementIsCurveDriven() => this;
        public FilteredElementCollector WhereElementIsElementType() => this;
        public FilteredElementCollector WhereElementIsNotElementType() => this;
        public FilteredElementCollector WherePasses(ElementFilter filter) => this;
        public FilteredElementCollector UnionWith(FilteredElementCollector other) => this;
        public FilteredElementCollector Excluding(ICollection<ElementId> excludedIds) => this;
        public FilteredElementCollector OwnedByView(ElementId viewId) => this;
        public IList<Element> ToElements() => throw new NotImplementedException();
        public IList<ElementId> ToElementIds() => throw new NotImplementedException();
        public Element FirstElement() => throw new NotImplementedException();
        public ElementId FirstElementId() => throw new NotImplementedException();
        public int GetElementCount() => throw new NotImplementedException();
        public System.Collections.IEnumerator GetEnumerator_NonGeneric() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public IEnumerator<Element> GetEnumerator() => throw new NotImplementedException();
        public T FirstOrDefault<T>() where T : Element => throw new NotImplementedException();
        public IEnumerable<T> OfType<T>() => throw new NotImplementedException();
        public IEnumerable<Element> Cast_Element() => throw new NotImplementedException();
    }

    // ── Filters ──────────────────────────────────────────────────────────────
    public abstract class ElementFilter { public bool Inverted { get; } }
    public class ElementCategoryFilter : ElementFilter { public ElementCategoryFilter(BuiltInCategory cat) { } public ElementCategoryFilter(BuiltInCategory cat, bool inverted) { } }
    public class ElementClassFilter : ElementFilter { public ElementClassFilter(Type type) { } public ElementClassFilter(Type type, bool inverted) { } }
    public class ElementMulticategoryFilter : ElementFilter { public ElementMulticategoryFilter(ICollection<BuiltInCategory> cats) { } public ElementMulticategoryFilter(ICollection<BuiltInCategory> cats, bool inverted) { } }
    public class ElementMulticlassFilter : ElementFilter { public ElementMulticlassFilter(ICollection<Type> types) { } }
    public class LogicalAndFilter : ElementFilter { public LogicalAndFilter(IList<ElementFilter> filters) { } public LogicalAndFilter(ElementFilter a, ElementFilter b) { } }
    public class LogicalOrFilter : ElementFilter { public LogicalOrFilter(IList<ElementFilter> filters) { } public LogicalOrFilter(ElementFilter a, ElementFilter b) { } }
    public class ElementParameterFilter : ElementFilter { public ElementParameterFilter(FilterRule rule) { } public ElementParameterFilter(IList<FilterRule> rules) { } public ElementParameterFilter(FilterRule rule, bool inverted) { } }
    public class ElementIsElementTypeFilter : ElementFilter { public ElementIsElementTypeFilter() { } public ElementIsElementTypeFilter(bool inverted) { } }
    public class ElementInView : ElementFilter { public ElementInView(ElementId viewId) { } }
    public class BoundingBoxIntersectsFilter : ElementFilter { public BoundingBoxIntersectsFilter(Outline outline) { } public BoundingBoxIntersectsFilter(Outline outline, double tolerance) { } }
    public class BoundingBoxIsInsideFilter : ElementFilter { public BoundingBoxIsInsideFilter(Outline outline) { } }
    public class BoundingBoxContainsPointFilter : ElementFilter { public BoundingBoxContainsPointFilter(XYZ point) { } public BoundingBoxContainsPointFilter(XYZ point, double tolerance) { } }

    public abstract class FilterRule { }
    public class FilterStringRule : FilterRule { public FilterStringRule(FilterableValueProvider provider, FilterStringRuleEvaluator evaluator, string ruleString) { } [Obsolete] public FilterStringRule(FilterableValueProvider provider, FilterStringRuleEvaluator evaluator, string ruleString, bool caseSensitive) { } }
    public class FilterDoubleRule : FilterRule { public FilterDoubleRule(FilterableValueProvider provider, FilterNumericRuleEvaluator evaluator, double ruleValue, double epsilon) { } }
    public class FilterIntegerRule : FilterRule { public FilterIntegerRule(FilterableValueProvider provider, FilterNumericRuleEvaluator evaluator, int ruleValue) { } }
    public class FilterElementIdRule : FilterRule { public FilterElementIdRule(FilterableValueProvider provider, FilterNumericRuleEvaluator evaluator, ElementId ruleValue) { } }
    public abstract class FilterableValueProvider { }
    public class ParameterValueProvider : FilterableValueProvider { public ParameterValueProvider(ElementId parameterId) { } }
    public abstract class FilterStringRuleEvaluator { }
    public class FilterStringEquals : FilterStringRuleEvaluator { public FilterStringEquals() { } }
    public class FilterStringContains : FilterStringRuleEvaluator { public FilterStringContains() { } }
    public class FilterStringBeginsWith : FilterStringRuleEvaluator { public FilterStringBeginsWith() { } }
    public class FilterStringEndsWith : FilterStringRuleEvaluator { public FilterStringEndsWith() { } }
    public class FilterStringGreater : FilterStringRuleEvaluator { public FilterStringGreater() { } }
    public class FilterStringGreaterOrEqual : FilterStringRuleEvaluator { public FilterStringGreaterOrEqual() { } }
    public abstract class FilterNumericRuleEvaluator { }
    public class FilterNumericEquals : FilterNumericRuleEvaluator { public FilterNumericEquals() { } }
    public class FilterNumericGreater : FilterNumericRuleEvaluator { public FilterNumericGreater() { } }
    public class FilterNumericGreaterOrEqual : FilterNumericRuleEvaluator { public FilterNumericGreaterOrEqual() { } }
    public class FilterNumericLess : FilterNumericRuleEvaluator { public FilterNumericLess() { } }
    public class FilterNumericLessOrEqual : FilterNumericRuleEvaluator { public FilterNumericLessOrEqual() { } }
    public class FilterNumericNotEquals : FilterNumericRuleEvaluator { public FilterNumericNotEquals() { } }
    public class HasValueFilterRule : FilterRule { public HasValueFilterRule(ElementId parameterId) { } }
    public class HasNoValueFilterRule : FilterRule { public HasNoValueFilterRule(ElementId parameterId) { } }
    public class SharedParameterApplicableRule : FilterRule { public SharedParameterApplicableRule(string paramName) { } }

    public class ParameterFilterElement : Element
    {
        public string Name { get; set; }
        public IList<ElementId> GetCategories() => throw new NotImplementedException();
        public void SetCategories(IList<ElementId> categoryIds) => throw new NotImplementedException();
        public ElementFilter GetElementFilter() => throw new NotImplementedException();
        public void SetElementFilter(ElementFilter filter) => throw new NotImplementedException();
        public bool AllRuleParametersApplicable(Document doc) => throw new NotImplementedException();
        public static ParameterFilterElement Create(Document doc, string name, IList<ElementId> categoryIds) => throw new NotImplementedException();
        public static ParameterFilterElement Create(Document doc, string name, IList<ElementId> categoryIds, ElementFilter filter) => throw new NotImplementedException();
        public static bool IsNameUnique(Document doc, string name) => throw new NotImplementedException();
    }

    // ── ElementType ──────────────────────────────────────────────────────────
    public class ElementType : Element
    {
        public string FamilyName { get; }
        public ElementType Duplicate(string name) => throw new NotImplementedException();
    }

    public class FamilySymbol : ElementType
    {
        public Family Family { get; }
        public bool IsActive { get; }
        public void Activate() => throw new NotImplementedException();
        public FamilyParameterSet Parameters2 { get; }
    }

    public class FamilyInstance : Element
    {
        public FamilySymbol Symbol { get; }
        public ElementId GetTypeId() => throw new NotImplementedException();
        public void ChangeTypeId(ElementId typeId) => throw new NotImplementedException();
        public XYZ GetTransform() => throw new NotImplementedException();
        public Transform GetTransform2() => throw new NotImplementedException();
        public LocationPoint LocationPoint { get; }
        public Location Location { get; }
        public MEPModel MEPModel { get; }
        public XYZ FacingOrientation => throw new NotImplementedException();
        public XYZ HandOrientation => throw new NotImplementedException();
        public Element Host { get; }
        public ElementId LevelId { get; }
        public bool CanRotate { get; }
        public void rotate(Line axis, double angle) => throw new NotImplementedException();
        public ConnectorManager MEPModel_ConnectorManager => throw new NotImplementedException();
        public Autodesk.Revit.DB.Structure.StructuralType StructuralType { get; }
    }

    // MEP model exposed by FamilyInstance.MEPModel (lives in Autodesk.Revit.DB).
    public class MEPModel
    {
        public ConnectorManager ConnectorManager { get; }
        public ISet<Autodesk.Revit.DB.Electrical.ElectricalSystem> GetElectricalSystems() => throw new NotImplementedException();
    }

    public class Family : Element
    {
        public string Name { get; }
        public ISet<ElementId> GetFamilySymbolIds() => throw new NotImplementedException();
        public bool IsEditable { get; }
        public Document EditFamily() => throw new NotImplementedException();
        public bool IsInPlace { get; }
        public FamilyPlacementType FamilyPlacementType { get; }
    }

    public class FamilyManager
    {
        public IList<FamilyParameter> GetParameters() => throw new NotImplementedException();
        public FamilyParameter get_Parameter(string name) => throw new NotImplementedException();
        public FamilyParameter AddParameter(string name, BuiltInParameterGroup group, ParameterType type, bool isInstance) => throw new NotImplementedException();
        public FamilyParameter AddParameter(ExternalDefinition def, BuiltInParameterGroup group, bool isInstance) => throw new NotImplementedException();
        public FamilyParameter AddParameter(string name, ForgeTypeId group, ForgeTypeId type, bool isInstance) => throw new NotImplementedException();
        public void MakeInstance(FamilyParameter param) => throw new NotImplementedException();
        public void MakeType(FamilyParameter param) => throw new NotImplementedException();
        public void DeleteParameter(FamilyParameter param) => throw new NotImplementedException();
        public FamilyTypeSet Types { get; }
        public FamilyType CurrentType { get; }
        public void SetCurrentFamilyType(FamilyType type) => throw new NotImplementedException();
        public void Set(FamilyParameter param, int value) => throw new NotImplementedException();
        public void Set(FamilyParameter param, double value) => throw new NotImplementedException();
        public void Set(FamilyParameter param, string value) => throw new NotImplementedException();
        public void Set(FamilyParameter param, ElementId value) => throw new NotImplementedException();
        public FamilyType NewType(string name) => throw new NotImplementedException();
    }

    public class FamilyParameter : Parameter { public bool IsInstance { get; } public bool CanAssignFormula { get; } }
    public class FamilyParameterSet : IEnumerable<FamilyParameter> { public IEnumerator<FamilyParameter> GetEnumerator() => throw new NotImplementedException(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException(); public int Size { get; } }
    public class FamilyType { public string Name { get; } public bool HasValue(FamilyParameter param) => throw new NotImplementedException(); }
    public class FamilyTypeSet : IEnumerable<FamilyType> { public IEnumerator<FamilyType> GetEnumerator() => throw new NotImplementedException(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException(); }

    // ── Location ──────────────────────────────────────────────────────────────
    public abstract class Location { public void Rotate(Line axis, double angle) => throw new NotImplementedException(); public void Move(XYZ translation) => throw new NotImplementedException(); }
    public class LocationPoint : Location { public XYZ Point { get; set; } public double Rotation { get; } }
    public class LocationCurve : Location { public Curve Curve { get; set; } }

    // ── Geometry ──────────────────────────────────────────────────────────────
    public class Reference
    {
        public ElementId ElementId { get; }
        public Reference(Element element) { }
        public static Reference ParseFromStableRepresentation(Document doc, string rep) => throw new NotImplementedException();
        public string ConvertToStableRepresentation(Document doc) => throw new NotImplementedException();
    }

    public class ReferenceArray : IEnumerable<Reference>
    {
        public ReferenceArray() { }
        public void Append(Reference reference) => throw new NotImplementedException();
        public int Size { get; }
        public IEnumerator<Reference> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
    }

    public class Options
    {
        public Options() { }
        public ViewDetailLevel DetailLevel { get; set; }
        public View View { get; set; }
        public bool IncludeNonVisibleObjects { get; set; }
        public bool ComputeReferences { get; set; }
    }

    // ── Curves ───────────────────────────────────────────────────────────────
    public abstract class Curve
    {
        public XYZ GetEndPoint(int index) => throw new NotImplementedException();
        public double Length { get; }
        public bool IsBound { get; }
        public bool IsCyclic { get; }
        public XYZ Evaluate(double parameter, bool normalized) => throw new NotImplementedException();
        public XYZ ComputeDerivatives(double parameter, bool normalized) => throw new NotImplementedException();
        public Curve CreateTransformed(Transform transform) => throw new NotImplementedException();
        public Curve Clone() => throw new NotImplementedException();
        public void MakeBound(double param0, double param1) => throw new NotImplementedException();
        public void MakeUnbound() => throw new NotImplementedException();
        public IList<XYZ> Tessellate() => throw new NotImplementedException();
        public bool Intersect(Curve curve, out IntersectionResultArray results) => throw new NotImplementedException();
        public SetComparisonResult Intersect(Curve curve) => throw new NotImplementedException();
    }
    public enum SetComparisonResult { Disjoint, Subset, Superset, Equal, Overlap }

    public class Line : Curve
    {
        public XYZ Origin { get; }
        public XYZ Direction { get; }
        public static Line CreateBound(XYZ start, XYZ end) => throw new NotImplementedException();
        public static Line CreateUnbound(XYZ origin, XYZ direction) => throw new NotImplementedException();
    }
    public class Arc : Curve
    {
        public XYZ Center { get; }
        public double Radius { get; }
        public static Arc Create(XYZ center, double radius, double startAngle, double endAngle, XYZ xAxis, XYZ yAxis) => throw new NotImplementedException();
        public static Arc Create(XYZ start, XYZ end, XYZ pointOnArc) => throw new NotImplementedException();
        public static Arc Create(Plane plane, double radius, double startAngle, double endAngle) => throw new NotImplementedException();
    }
    public class Ellipse : Curve
    {
        public XYZ Center { get; }
        public double RadiusX { get; }
        public double RadiusY { get; }
        public static Ellipse CreateCurve(XYZ center, double radiusX, double radiusY, XYZ xVec, XYZ yVec, double param0, double param1) => throw new NotImplementedException();
    }
    public class NurbSpline : Curve
    {
        public static NurbSpline Create(IList<XYZ> controlPoints, IList<double> weights, IList<double> knots, int degree, bool closed, bool rational) => throw new NotImplementedException();
        public static NurbSpline CreateCurve(IList<XYZ> points, IList<double> weights) => throw new NotImplementedException();
    }
    public class HermiteSpline : Curve
    {
        public static HermiteSpline Create(IList<XYZ> points, bool isPeriodic) => throw new NotImplementedException();
        public static HermiteSpline Create(IList<XYZ> points, bool isPeriodic, HermiteSplineTangents tangents) => throw new NotImplementedException();
    }
    public class HermiteSplineTangents { public IList<XYZ> Tangents { get; set; } }

    public class CurveArray : IEnumerable<Curve>
    {
        public CurveArray() { }
        public void Append(Curve curve) => throw new NotImplementedException();
        public void Insert(Curve curve, int index) => throw new NotImplementedException();
        public int Size { get; }
        public IEnumerator<Curve> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public Curve get_Item(int index) => throw new NotImplementedException();
    }

    public class CurveArrArray : IEnumerable<CurveArray>
    {
        public CurveArrArray() { }
        public void Append(CurveArray curveArray) => throw new NotImplementedException();
        public int Size { get; }
        public IEnumerator<CurveArray> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public CurveArray get_Item(int index) => throw new NotImplementedException();
    }

    public class CurveLoop : IEnumerable<Curve>
    {
        public CurveLoop() { }
        public static CurveLoop Create(IList<Curve> curves) => throw new NotImplementedException();
        public void Append(Curve curve) => throw new NotImplementedException();
        public bool IsOpen() => throw new NotImplementedException();
        public bool IsClosed() => throw new NotImplementedException();
        public bool IsCounterclockwise(XYZ normal) => throw new NotImplementedException();
        public void Flip() => throw new NotImplementedException();
        public double GetExactLength() => throw new NotImplementedException();
        public IEnumerator<Curve> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public static CurveLoop CreateViaOffset(CurveLoop curveLoop, double offset, XYZ normal) => throw new NotImplementedException();
        public XYZ GetPlane() => throw new NotImplementedException();
    }

    public class IntersectionResultArray : IEnumerable<IntersectionResult>
    {
        public IEnumerator<IntersectionResult> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public int Size { get; }
        public bool IsEmpty { get; }
    }
    public class IntersectionResult { public XYZ XYZPoint { get; } public double Parameter { get; } public double Parameter2 { get; } }

    // ── SketchPlane & ReferencePlane ─────────────────────────────────────────
    public class SketchPlane : Element
    {
        public Plane GetPlane() => throw new NotImplementedException();
        public static SketchPlane Create(Document doc, Plane plane) => throw new NotImplementedException();
        public static SketchPlane Create(Document doc, ElementId levelId) => throw new NotImplementedException();
        public static SketchPlane Create(Document doc, Level level) => throw new NotImplementedException();
        public static SketchPlane Create(Document doc, Face planarFace) => throw new NotImplementedException();
    }
    public class ReferencePlane : Element
    {
        public Plane GetPlane() => throw new NotImplementedException();
        public XYZ Normal { get; }
        public XYZ BubbleEnd { get; }
        public XYZ FreeEnd { get; }
    }

    // ── ModelCurve, DetailCurve ──────────────────────────────────────────────
    public class ModelCurve : Element { public Curve GeometryCurve { get; set; } }
    public class ModelLine : ModelCurve { }
    public class DetailCurve : Element { public Curve GeometryCurve { get; set; } }
    public class DetailLine : DetailCurve { }

    // ── HostObject ────────────────────────────────────────────────────────────
    public abstract class HostObject : Element
    {
        public CompoundStructure GetCompoundStructure() => throw new NotImplementedException();
        public void SetCompoundStructure(CompoundStructure cs) => throw new NotImplementedException();
        public IList<ElementId> FindInserts(bool addRectOpenings, bool includeShadows, bool includeEmbeddedWalls, bool includeSharedEmbeddedInserts) => throw new NotImplementedException();
    }
    public class HostObjAttributes : ElementType
    {
        public CompoundStructure GetCompoundStructure() => throw new NotImplementedException();
        public void SetCompoundStructure(CompoundStructure cs) => throw new NotImplementedException();
    }
    public class CompoundStructure
    {
        public static CompoundStructure Create(Document doc, IList<CompoundStructureLayer> layers) => throw new NotImplementedException();
        public IList<CompoundStructureLayer> GetLayers() => throw new NotImplementedException();
        public void SetLayers(IList<CompoundStructureLayer> layers) => throw new NotImplementedException();
        public double GetWidth() => throw new NotImplementedException();
    }
    public class CompoundStructureLayer
    {
        public CompoundStructureLayer(double width, MaterialFunctionAssignment function, ElementId materialId) { }
        public double Width { get; set; }
        public MaterialFunctionAssignment Function { get; set; }
        public ElementId MaterialId { get; set; }
    }
    public enum MaterialFunctionAssignment { None, Structure, Substrate, ThermalOrAir, Membrane, Finish1, Finish2, Insulation, StructuralDeck }

    // ── Wall, Floor, Ceiling, Roof ────────────────────────────────────────────
    public class Wall : HostObject
    {
        public WallType WallType { get; }
        public Curve GetLocationCurve_Wall() => throw new NotImplementedException();
        public bool Flipped { get; }
        public void Flip() => throw new NotImplementedException();
        public static Wall Create(Document doc, Curve curve, ElementId wallTypeId, ElementId levelId, double height, double offset, bool flip, bool structural) => throw new NotImplementedException();
        public static Wall Create(Document doc, IList<Curve> profile, bool structural) => throw new NotImplementedException();
        public static Wall Create(Document doc, IList<Curve> profile, ElementId wallTypeId, ElementId levelId, bool structural) => throw new NotImplementedException();
    }
    public class WallType : HostObjAttributes { }

    public class Floor : HostObject
    {
        public FloorType FloorType { get; }
        public static Floor Create(Document doc, IList<CurveLoop> profile, ElementId floorTypeId, ElementId levelId) => throw new NotImplementedException();
        [Obsolete] public static Floor Create(Document doc, CurveArray profile, FloorType floorType, Level level) => throw new NotImplementedException();
        [Obsolete] public static Floor Create(Document doc, CurveArray profile, FloorType floorType, Level level, bool structural) => throw new NotImplementedException();
    }
    public class FloorType : HostObjAttributes { }

    public class Ceiling : HostObject
    {
        public static Ceiling Create(Document doc, IList<CurveLoop> ceilingLoops, ElementId ceilingTypeId, ElementId levelId) => throw new NotImplementedException();
    }
    public class CeilingType : HostObjAttributes { }

    public class RoofBase : HostObject { }
    public class FootPrintRoof : RoofBase
    {
        public ModelCurveArray GetProfiles() => throw new NotImplementedException();
    }
    public class RoofType : HostObjAttributes { }
    public class ModelCurveArray : IEnumerable<ModelCurve> { public IEnumerator<ModelCurve> GetEnumerator() => throw new NotImplementedException(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException(); public int Size { get; } }

    // ── Material ──────────────────────────────────────────────────────────────
    public class Material : Element
    {
        public string MaterialClass { get; set; }
        public MaterialCategory Category2 { get; }
        public Color Color { get; set; }
        public int Shininess { get; set; }
        public int Smoothness { get; set; }
        public int Transparency { get; set; }
        public ElementId SurfaceForegroundPatternId { get; set; }
        public Color SurfaceForegroundPatternColor { get; set; }
        public ElementId SurfaceBackgroundPatternId { get; set; }
        public Color SurfaceBackgroundPatternColor { get; set; }
        public ElementId CutForegroundPatternId { get; set; }
        public Color CutForegroundPatternColor { get; set; }
        public ElementId AppearanceAssetId { get; set; }
        public ElementId StructuralAssetId { get; set; }
        public ElementId ThermalAssetId { get; set; }
    }
    public enum MaterialCategory { Generic, Concrete, Metal, Wood, Masonry, Other, Glass, Finish, PlasticVinyl, Stone, Ceramic, Liquid, Gas, Earth, Paint, Insulation }

    // ── DataStorage ──────────────────────────────────────────────────────────
    public class DataStorage : Element
    {
        public static DataStorage Create(Document doc) => throw new NotImplementedException();
    }

    // ── AssemblyInstance ─────────────────────────────────────────────────────
    public class AssemblyInstance : Element
    {
        public ElementId AssemblyTypeName { get; }
        public IList<ElementId> GetMemberIds() => throw new NotImplementedException();
        public void SetMemberIds(IList<ElementId> ids) => throw new NotImplementedException();
        public string AssemblyTypeName_String { get; set; }
        public XYZ GetMainLocation() => throw new NotImplementedException();
        public static AssemblyInstance Create(Document doc, ICollection<ElementId> memberIds, ElementId levelId) => throw new NotImplementedException();
        public static bool IsValidNamingCategory(Document doc, ElementId categoryId, ICollection<ElementId> memberIds) => throw new NotImplementedException();
        public View CreateSingleCategorySchedule(Document doc, ElementId viewTypeId, BuiltInCategory builtInCategory) => throw new NotImplementedException();
        public static bool AreElementsValidForAssembly(Document doc, ICollection<ElementId> elements, ElementId contextElementId) => throw new NotImplementedException();
    }

    // ── FilledRegion ─────────────────────────────────────────────────────────
    public class FilledRegion : Element
    {
        public ElementId GetTypeId() => throw new NotImplementedException();
        public IList<CurveLoop> GetBoundaries() => throw new NotImplementedException();
        public static FilledRegion Create(Document doc, ElementId typeId, ElementId viewId, IList<CurveLoop> boundaries) => throw new NotImplementedException();
    }
    public class FilledRegionType : ElementType
    {
        public Color BackgroundPatternColor { get; set; }
        public ElementId BackgroundPatternId { get; set; }
        public Color ForegroundPatternColor { get; set; }
        public ElementId ForegroundPatternId { get; set; }
        public bool IsMasking { get; set; }
        public int LineWeight { get; set; }
        public ElementId LinePatternId { get; set; }
        public Color LineColor { get; set; }
    }

    // ── SpatialElement ────────────────────────────────────────────────────────
    public class SpatialElement : Element
    {
        public string Number { get; set; }
        public double Area { get; }
        public double Volume { get; }
        public double Perimeter { get; }
        public Level Level { get; }
        public Phase Phase { get; }
        public SpatialElementGeometryCalculator CalculateGeometry() => throw new NotImplementedException();
        public IList<IList<BoundarySegment>> GetBoundarySegments(SpatialElementBoundaryOptions options) => throw new NotImplementedException();
    }

    public class SpatialElementBoundaryOptions
    {
        public SpatialElementBoundaryLocation SpatialElementBoundaryLocation { get; set; }
        public bool StoreFreeBoundaryFaces { get; set; }
    }
    public enum SpatialElementBoundaryLocation { Finish, Center, CoreBoundary, CoreCenter }
    public class BoundarySegment { public Curve GetCurve() => throw new NotImplementedException(); public Reference GetLinkElementId() => throw new NotImplementedException(); public ElementId ElementId { get; } }
    public class SpatialElementGeometryCalculator : IDisposable
    {
        public SpatialElementGeometryCalculator(Document doc) { }
        public SpatialElementGeometryCalculator(Document doc, SpatialElementBoundaryOptions opts) { }
        public SpatialElementGeometryResults CalculateSpatialElementGeometry(SpatialElement elem) => throw new NotImplementedException();
        public void Dispose() { }
    }
    public class SpatialElementGeometryResults { public Geometry.Solid GetGeometry() => throw new NotImplementedException(); }

    // ── Grid ─────────────────────────────────────────────────────────────────
    public class Grid : Element
    {
        public string Name { get; set; }
        public Curve Curve { get; set; }
        public bool IsCurved { get; }
        public static Grid Create(Document doc, Line line) => throw new NotImplementedException();
        public static Grid Create(Document doc, Arc arc) => throw new NotImplementedException();
    }

    // ── ViewSchedule / Schedule ──────────────────────────────────────────────
    public class ViewSchedule : View
    {
        public ScheduleDefinition Definition { get; }
        public TableData GetTableData() => throw new NotImplementedException();
        public static ViewSchedule CreateSchedule(Document doc, ElementId categoryId) => throw new NotImplementedException();
        public static ViewSchedule CreateSchedule(Document doc, ElementId categoryId, ElementId templateId) => throw new NotImplementedException();
        public static ViewSchedule CreateMaterialTakeoff(Document doc, ElementId categoryId) => throw new NotImplementedException();
        public static ViewSchedule CreateNoteBlock(Document doc, ElementId symbolId) => throw new NotImplementedException();
        public static ViewSchedule CreateKeySchedule(Document doc, ElementId categoryId) => throw new NotImplementedException();
    }

    public class ScheduleDefinition
    {
        public string GetFieldName(int fieldIndex) => throw new NotImplementedException();
        public int GetFieldCount() => throw new NotImplementedException();
        public ScheduleField GetField(int fieldIndex) => throw new NotImplementedException();
        public ScheduleField AddField(ScheduleFieldType type, ElementId parameterId) => throw new NotImplementedException();
        public ScheduleField AddField(ScheduleField field) => throw new NotImplementedException();
        public void RemoveField(ScheduleFieldId fieldId) => throw new NotImplementedException();
        public IList<ScheduleFieldId> GetFieldOrder() => throw new NotImplementedException();
        public void SetFieldOrder(IList<ScheduleFieldId> order) => throw new NotImplementedException();
        public ScheduleFilter AddFilter(ScheduleFilter filter) => throw new NotImplementedException();
        public bool ShowHeaders { get; set; }
        public bool ShowTitle { get; set; }
        public bool ShowGrandTotal { get; set; }
        public string GrandTotalTitle { get; set; }
        public bool IsItemized { get; set; }
        public bool ShowGridLines { get; set; }
        public ElementId CategoryId { get; }
        public ScheduleFieldId GetFieldId(int index) => throw new NotImplementedException();
    }

    public class ScheduleField
    {
        public string ColumnHeading { get; set; }
        public ScheduleFieldType FieldType { get; }
        public ElementId ParameterId { get; }
        public ScheduleFieldId FieldId { get; }
        public bool IsHidden { get; set; }
        public HorizontalAlignmentStyle HorizontalAlignment { get; set; }
        public bool IsCalculatedField { get; }
        public string GetName() => throw new NotImplementedException();
    }
    public enum ScheduleFieldType { Invalid, Instance, Count, Calculated, CombinedParameter, MaterialQuantity, ViewBased, AssemblyQuantity }
    public enum HorizontalAlignmentStyle { Left, Center, Right, Default }
    public class ScheduleFieldId { }
    public class ScheduleFilter { public ScheduleFilter(ScheduleFieldId fieldId, ScheduleFilterType type, string value) { } public ScheduleFilter(ScheduleFieldId fieldId, ScheduleFilterType type, double value) { } public ScheduleFilter(ScheduleFieldId fieldId, ScheduleFilterType type, int value) { } }
    public enum ScheduleFilterType { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Contains, NotContains, BeginsWith, NotBeginsWith, EndsWith, NotEndsWith, HasValue, HasNoValue }

    public class TableData
    {
        public TableSectionData GetSectionData(SectionType sectionType) => throw new NotImplementedException();
    }
    public class TableSectionData
    {
        public int NumberOfRows { get; }
        public int NumberOfColumns { get; }
        public string GetCellText(int row, int column) => throw new NotImplementedException();
        public void SetCellText(int row, int column, string value) => throw new NotImplementedException();
        public bool IsValidCellIndex(int row, int column) => throw new NotImplementedException();
        public void InsertRow(int rowIndex) => throw new NotImplementedException();
        public void RemoveRow(int rowIndex) => throw new NotImplementedException();
    }
    public enum SectionType { None, Header, Body, Summary, Footer }

    // ── PanelSchedule ─────────────────────────────────────────────────────────
    public class PanelScheduleView : View
    {
        public ElementId GetParentView() => throw new NotImplementedException();
        public Autodesk.Revit.DB.Electrical.ElectricalSystem GetCircuitByCell(int row, int column) => throw new NotImplementedException();
        public void AddSpare(int row, int column) => throw new NotImplementedException();
        public void AddSpace(int row, int column) => throw new NotImplementedException();
        public void RemoveSpace(int row, int column) => throw new NotImplementedException();
        public void RemoveSpare(int row, int column) => throw new NotImplementedException();
        public static PanelScheduleView CreateInstanceView(Document doc, ElementId panelId, ElementId templateId = null) => throw new NotImplementedException();
    }
    public class PanelScheduleTemplate : ElementType { }
    public class PanelScheduleSheetInstance : Element { public static PanelScheduleSheetInstance Create(Document doc, ElementId panelScheduleViewId, ViewSheet sheet) => throw new NotImplementedException(); }

    // ── Import ────────────────────────────────────────────────────────────────
    public class ImportInstance : Element
    {
        public bool IsLinked { get; }
        public ISet<string> GetLayerIds() => throw new NotImplementedException();
        public bool IsLayerVisible(string layerId) => throw new NotImplementedException();
        public void SetLayerVisible(View view, string layerId, bool visible) => throw new NotImplementedException();
    }
    public class DWGImportOptions
    {
        public DWGImportOptions() { }
        public bool AutoCorrectAlmostHorizontalLines { get; set; }
        public ImportColorMode ColorMode { get; set; }
        public ImportCustomScale CustomScale { get; set; }
        public ImportUnit Unit { get; set; }
        public string LayerQuery { get; set; }
        public bool OrientToView { get; set; }
        public bool Placement { get; set; }
        public bool PreserveCoordinates { get; set; }
        public bool RefreshDeferral { get; set; }
        public bool ThisViewOnly { get; set; }
    }
    public enum ImportColorMode { Inverted, BlackAndWhite, Preserved }
    public enum ImportCustomScale { Auto }
    public enum ImportUnit { Foot, Inch, Meter, Centimeter, Millimeter, AutoDetect }
    public class ImageImportOptions { public string FileName { get; set; } public XYZ RefPoint { get; set; } public double Resolution { get; set; } }

    // ── ParameterElement ────────────────────────────────────────────────────
    public class ParameterElement : Element { public ExternalDefinition GetDefinition() => throw new NotImplementedException(); }
    public class SharedParameterElement : ParameterElement { public System.Guid GuidValue { get; } public static SharedParameterElement Lookup(Document doc, System.Guid guid) => throw new NotImplementedException(); public static SharedParameterElement Create(Document doc, ExternalDefinition definition) => throw new NotImplementedException(); }
    public class GlobalParametersManager { public static bool IsGlobalParametersSupported(Document doc) => throw new NotImplementedException(); }

    // ── UpdaterRegistry ──────────────────────────────────────────────────────
    public class UpdaterId
    {
        public UpdaterId(System.Guid applicationGuid, System.Guid updaterGuid) { }
    }

    public class UpdaterRegistry
    {
        public static void RegisterUpdater(IUpdater updater) => throw new NotImplementedException();
        public static void RegisterUpdater(IUpdater updater, bool isOptional) => throw new NotImplementedException();
        public static void UnregisterUpdater(UpdaterId id) => throw new NotImplementedException();
        public static void AddTrigger(UpdaterId id, Document doc, ChangeTypeId change) => throw new NotImplementedException();
        public static void AddTrigger(UpdaterId id, Document doc, ICollection<ElementId> elementIds, ChangeTypeId change) => throw new NotImplementedException();
        public static void AddTrigger(UpdaterId id, Document doc, ElementFilter filter, ChangeTypeId change) => throw new NotImplementedException();
        public static void RemoveTrigger(UpdaterId id, Document doc, ChangeTypeId change) => throw new NotImplementedException();
        public static bool IsUpdaterRegistered(UpdaterId id) => throw new NotImplementedException();
        public static bool IsUpdaterEnabled(UpdaterId id) => throw new NotImplementedException();
    }

    public interface IUpdater
    {
        void Execute(UpdaterData data);
        string GetAdditionalInformation();
        ChangePriority GetChangePriority();
        UpdaterId GetUpdaterId();
        string GetUpdaterName();
    }
    public class UpdaterData
    {
        public Document GetDocument() => throw new NotImplementedException();
        public ICollection<ElementId> GetModifiedElementIds() => throw new NotImplementedException();
        public ICollection<ElementId> GetAddedElementIds() => throw new NotImplementedException();
        public ICollection<ElementId> GetDeletedElementIds() => throw new NotImplementedException();
        public bool IsChangeTriggered(ElementId elementId, ChangeTypeId change) => throw new NotImplementedException();
        public ICollection<ElementId> GetModifiedElementIds(UpdaterId updaterId, ChangeTypeId change) => throw new NotImplementedException();
        public bool IsChangeTriggered_For(UpdaterId id, ElementId elementId, ChangeTypeId change) => throw new NotImplementedException();
    }
    public enum ChangePriority { Annotations = 30, FreeStandingAnnotations = 30, Grid = 20, MEPSystems = 10, MEPLinks = 10, RoomsAndAreas = 20, Structure = 10, Views = 30, WallJoinsAndCuts = 10 }

    // ── ReferenceIntersector ────────────────────────────────────────────────
    public class ReferenceIntersector
    {
        public ReferenceIntersector(View3D view) { }
        public ReferenceIntersector(ElementId categoryId, FindReferenceTarget target, View3D view) { }
        public ReferenceIntersector(IList<ElementId> categoryIds, FindReferenceTarget target, View3D view) { }
        public IList<ReferenceWithContext> Find(XYZ origin, XYZ direction) => throw new NotImplementedException();
        public ReferenceWithContext FindNearest(XYZ origin, XYZ direction) => throw new NotImplementedException();
        public bool FindReferencesInRevitLinks { get; set; }
        public TargetType TargetType { get; set; }
    }
    public enum FindReferenceTarget { All, Element, Face, Mesh, Edge, Curve, LinkedElement }
    public enum TargetType { All, Face }
    public class ReferenceWithContext { public Reference GetReference() => throw new NotImplementedException(); public double Proximity { get; } }

    // ── RevisionCloud ────────────────────────────────────────────────────────
    public class RevisionCloud : Element
    {
        public ElementId RevisionId { get; set; }
        public static RevisionCloud Create(Document doc, View view, ElementId revisionId, IList<CurveLoop> curves) => throw new NotImplementedException();
    }

    // ── Print ────────────────────────────────────────────────────────────────
    public class PrintManager : IDisposable
    {
        public PrintRange PrintRange { get; set; }
        public bool PrintToFile { get; set; }
        public string PrintToFileName { get; set; }
        public bool CombinedFile { get; set; }
        public int PrintSetup_CurrentPrintSetting { get; }
        public PrintSetup PrintSetup { get; }
        public ViewSheetSetting ViewSheetSetting { get; }
        public int Apply() => throw new NotImplementedException();
        public bool SubmitPrint() => throw new NotImplementedException();
        public bool SubmitPrint(View view) => throw new NotImplementedException();
        public void Dispose() { }
    }
    public class PrintSetup { public IPrintSetting CurrentPrintSetting { get; } public void InSession_SaveAs(string name) => throw new NotImplementedException(); }
    public interface IPrintSetting { string Name { get; } PrintParameters GetParameters(); }
    public class PrintParameters { public PaperPlacementType PaperPlacement { get; set; } public HiddenLineViewsType HiddenLineViews { get; set; } public int Zoom { get; set; } public bool ZoomType { get; set; } public RasterQualityType RasterQuality { get; set; } public ColorDepthType ColorDepth { get; set; } }
    public class ViewSheetSetting { public ICollection<Element> CurrentViewSheetSet { get; set; } public void SaveAs(string name) => throw new NotImplementedException(); }

    // ── NavisworksExportOptions ──────────────────────────────────────────────
    public class NavisworksExportOptions { public bool ExportLinks { get; set; } public bool ExportRoomGeometry { get; set; } public bool ExportRoomAsAttribute { get; set; } public bool ExportUrls { get; set; } public bool ConvertLinkedFiles { get; set; } public bool DivideFileIntoLevels { get; set; } public bool ExportParts { get; set; } public bool ExportRoomAsAttribute_1 { get; } public NavisworksCoordinates Coordinates { get; set; } public NavisworksParameters Parameters { get; set; } }
    public enum NavisworksCoordinates { Shared, Internal }
    public enum NavisworksParameters { All, ModelProperties, None, Elements }

    // ── ElementSet ────────────────────────────────────────────────────────────
    public class ElementSet : IEnumerable<Element>
    {
        public ElementSet() { }
        public void Insert(Element element) => throw new NotImplementedException();
        public bool Erase(Element element) => throw new NotImplementedException();
        public bool Contains(Element element) => throw new NotImplementedException();
        public bool IsEmpty { get; }
        public int Size { get; }
        public IEnumerator<Element> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
    }
}
