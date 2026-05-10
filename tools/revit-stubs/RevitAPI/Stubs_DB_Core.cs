// Revit API CI Stubs — Autodesk.Revit.DB (core types)
using System;
using System.Collections;
using System.Collections.Generic;

namespace Autodesk.Revit.DB
{
    // ── Primitives ───────────────────────────────────────────────────────────
    public class Color
    {
        public static Color InvalidColorValue { get; } = new Color(0,0,0);
        public Color(byte red, byte green, byte blue) { Red=red; Green=green; Blue=blue; }
        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
        public bool IsValid { get; }
    }

    public struct XYZ
    {
        public static readonly XYZ Zero = new XYZ(0,0,0);
        public static readonly XYZ BasisX = new XYZ(1,0,0);
        public static readonly XYZ BasisY = new XYZ(0,1,0);
        public static readonly XYZ BasisZ = new XYZ(0,0,1);
        public XYZ(double x, double y, double z) { X=x; Y=y; Z=z; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double DistanceTo(XYZ other) => throw new NotImplementedException();
        public XYZ Add(XYZ other) => throw new NotImplementedException();
        public XYZ Subtract(XYZ other) => throw new NotImplementedException();
        public XYZ Multiply(double scalar) => throw new NotImplementedException();
        public XYZ Divide(double scalar) => throw new NotImplementedException();
        public XYZ Negate() => throw new NotImplementedException();
        public XYZ Normalize() => throw new NotImplementedException();
        public double DotProduct(XYZ other) => throw new NotImplementedException();
        public XYZ CrossProduct(XYZ other) => throw new NotImplementedException();
        public bool IsAlmostEqualTo(XYZ other) => throw new NotImplementedException();
        public bool IsAlmostEqualTo(XYZ other, double tolerance) => throw new NotImplementedException();
        public double GetLength() => throw new NotImplementedException();
        public static XYZ operator +(XYZ a, XYZ b) => throw new NotImplementedException();
        public static XYZ operator -(XYZ a, XYZ b) => throw new NotImplementedException();
        public static XYZ operator *(XYZ a, double s) => throw new NotImplementedException();
        public static XYZ operator *(double s, XYZ a) => throw new NotImplementedException();
    }

    public struct UV
    {
        public UV(double u, double v) { U=u; V=v; }
        public double U { get; }
        public double V { get; }
    }

    public class BoundingBoxXYZ
    {
        public XYZ Min { get; set; }
        public XYZ Max { get; set; }
        public Transform Transform { get; set; }
        public bool Enabled { get; set; }
    }

    public class Transform
    {
        public static Transform Identity { get; } = new Transform();
        public XYZ Origin { get; set; }
        public XYZ BasisX { get; set; }
        public XYZ BasisY { get; set; }
        public XYZ BasisZ { get; set; }
        public double Scale { get; set; }
        public bool IsIdentity { get; }
        public bool IsConformal { get; }
        public XYZ OfPoint(XYZ point) => throw new NotImplementedException();
        public XYZ OfVector(XYZ vector) => throw new NotImplementedException();
        public Transform GetInverse() => throw new NotImplementedException();
        public static Transform CreateTranslation(XYZ vector) => throw new NotImplementedException();
        public static Transform CreateRotation(XYZ axis, double angle) => throw new NotImplementedException();
        public static Transform CreateRotationAtPoint(XYZ axis, double angle, XYZ origin) => throw new NotImplementedException();
        public static Transform CreateReflection(Plane plane) => throw new NotImplementedException();
        public Transform Multiply(Transform other) => throw new NotImplementedException();
    }

    public class Plane
    {
        public XYZ Normal { get; }
        public XYZ Origin { get; }
        public static Plane CreateByNormalAndOrigin(XYZ normal, XYZ origin) => throw new NotImplementedException();
        public static Plane CreateByThreePoints(XYZ p0, XYZ p1, XYZ p2) => throw new NotImplementedException();
    }

    // ── ElementId ────────────────────────────────────────────────────────────
    public class ElementId : IComparable<ElementId>, IEquatable<ElementId>
    {
        public static readonly ElementId InvalidElementId = new ElementId(-1);
        public ElementId(int id) { Value = id; }
        public ElementId(long id) { Value = id; }
        public ElementId(BuiltInCategory bic) { Value = (int)bic; }
        public ElementId(BuiltInParameter bip) { Value = (int)bip; }
        public long Value { get; }
        [Obsolete] public int IntegerValue => (int)Value;
        public int CompareTo(ElementId other) => throw new NotImplementedException();
        public bool Equals(ElementId other) => other?.Value == Value;
        public override bool Equals(object obj) => obj is ElementId e && e.Value == Value;
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ElementId a, ElementId b) => a?.Value == b?.Value;
        public static bool operator !=(ElementId a, ElementId b) => !(a == b);
    }

    // ── Element base ─────────────────────────────────────────────────────────
    public abstract class Element
    {
        public ElementId Id { get; }
        public string Name { get; set; }
        public Category Category { get; }
        public Document Document { get; }
        public Parameters Parameters { get; }
        public bool IsValidObject { get; }
        public bool Pinned { get; set; }
        public ElementId LevelId { get; }
        public IList<ElementId> GetDependentElements(ElementFilter filter) => throw new NotImplementedException();
        public Parameter get_Parameter(BuiltInParameter bip) => throw new NotImplementedException();
        public Parameter get_Parameter(Definition definition) => throw new NotImplementedException();
        public Parameter LookupParameter(string name) => throw new NotImplementedException();
        public IList<Parameter> GetParameters(string name) => throw new NotImplementedException();
        public IList<Parameter> GetOrderedParameters() => throw new NotImplementedException();
        public BoundingBoxXYZ get_BoundingBox(View view) => throw new NotImplementedException();
        public Geometry.GeometryElement get_Geometry(Options options) => throw new NotImplementedException();
        public IList<ElementId> GetEntitySchemaGuids() => throw new NotImplementedException();
        public ExtensibleStorage.Entity GetEntity(ExtensibleStorage.Schema schema) => throw new NotImplementedException();
        public void SetEntity(ExtensibleStorage.Entity entity) => throw new NotImplementedException();
        public WorksharingTooltipInfo GetWorksharingTooltipInfo() => throw new NotImplementedException();
        public static ChangeTypeId GetChangeTypeAny() => throw new NotImplementedException();
        public static ChangeTypeId GetChangeTypeElementAddition() => throw new NotImplementedException();
        public static ChangeTypeId GetChangeTypeElementDeletion() => throw new NotImplementedException();
        public static ChangeTypeId GetChangeTypeGeometry() => throw new NotImplementedException();
        public static ChangeTypeId GetChangeTypeParameter(ElementId parameterId) => throw new NotImplementedException();
    }

    public class ChangeTypeId { }

    // ── Failure handling ─────────────────────────────────────────────────────
    public interface IFailuresPreprocessor
    {
        FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor);
    }
    public enum FailureProcessingResult { Continue, ProceedWithCommit, ProceedWithRollBack, WaitForUserInput }
    public class FailuresAccessor
    {
        public IList<FailureMessageAccessor> GetFailureMessages() => throw new NotImplementedException();
        public void DeleteAllWarnings() => throw new NotImplementedException();
        public void ResolveFailure(FailureMessageAccessor message) => throw new NotImplementedException();
    }
    public class FailureMessageAccessor
    {
        public FailureSeverity GetSeverity() => throw new NotImplementedException();
        public string GetDescriptionText() => throw new NotImplementedException();
        public IList<ElementId> GetFailingElementIds() => throw new NotImplementedException();
    }
    public enum FailureSeverity { None, Warning, Error, DocumentCorruption }

    // ── Transaction ──────────────────────────────────────────────────────────
    public enum TransactionStatus { Uninitialized, Started, Committed, RolledBack, Pending, Error }
    public class FailureHandlingOptions
    {
        public FailureHandlingOptions SetFailuresPreprocessor(IFailuresPreprocessor preprocessor) => this;
        public FailureHandlingOptions SetClearAfterRollback(bool clear) => this;
        public FailureHandlingOptions SetDelayedMiniWarnings(bool delay) => this;
    }

    public class Transaction : IDisposable
    {
        public Transaction(Document doc, string name) { }
        public Transaction(Document doc) { }
        public string GetName() => throw new NotImplementedException();
        public TransactionStatus Start() => throw new NotImplementedException();
        public TransactionStatus Commit() => throw new NotImplementedException();
        public TransactionStatus RollBack() => throw new NotImplementedException();
        public TransactionStatus GetStatus() => throw new NotImplementedException();
        public FailureHandlingOptions GetFailureHandlingOptions() => throw new NotImplementedException();
        public void SetFailureHandlingOptions(FailureHandlingOptions options) => throw new NotImplementedException();
        public bool HasStarted() => throw new NotImplementedException();
        public bool HasEnded() => throw new NotImplementedException();
        public void Dispose() { }
    }

    public class TransactionGroup : IDisposable
    {
        public TransactionGroup(Document doc, string name) { }
        public TransactionGroup(Document doc) { }
        public TransactionStatus Start() => throw new NotImplementedException();
        public TransactionStatus Commit() => throw new NotImplementedException();
        public TransactionStatus Assimilate() => throw new NotImplementedException();
        public TransactionStatus RollBack() => throw new NotImplementedException();
        public TransactionStatus GetStatus() => throw new NotImplementedException();
        public bool HasStarted() => throw new NotImplementedException();
        public void Dispose() { }
    }

    public class SubTransaction : IDisposable
    {
        public SubTransaction(Document doc) { }
        public TransactionStatus Start() => throw new NotImplementedException();
        public TransactionStatus Commit() => throw new NotImplementedException();
        public TransactionStatus RollBack() => throw new NotImplementedException();
        public void Dispose() { }
    }

    // ── Category ─────────────────────────────────────────────────────────────
    public class Category
    {
        public ElementId Id { get; }
        public string Name { get; }
        public bool AllowsVisibilityControl(View view) => throw new NotImplementedException();
        public CategoryType CategoryType { get; }
        public Category Parent { get; }
        public CategoryNameMap SubCategories { get; }
        public bool IsTagCategory { get; }
        public static Category GetCategory(Document doc, BuiltInCategory bic) => throw new NotImplementedException();
        public static Category GetCategory(Document doc, ElementId id) => throw new NotImplementedException();
        public LinePatternId GetLinePatternId(GraphicsStyleType gst) => throw new NotImplementedException();
    }
    public enum CategoryType { Invalid, Model, Annotation, AnalyticalModel, Internal }
    public class CategoryNameMap : IEnumerable<Category>
    {
        public IEnumerator<Category> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public Category get_Item(string name) => throw new NotImplementedException();
        public bool Contains(string name) => throw new NotImplementedException();
    }

    // ── Parameter ────────────────────────────────────────────────────────────
    public enum StorageType { None, Integer, Double, String, ElementId }
    public enum ParameterType { Invalid, Text, Integer, Number, Length, Area, Volume, Angle, URL, Material, YesNo, Force, LinearForce, AreaForce, Moment, NumberOfPoles, FixtureUnit, FamilyType, LoadClassification, Image, MultilineText, ElectricalTemperature, ElectricalLuminousFlux, ElectricalLuminousIntensity, ElectricalIlluminance, ElectricalLuminance, ElectricalEfficacy, ElectricalWaveLength, ColorTemperature }
    public enum BuiltInParameterGroup { INVALID, PG_GENERAL, PG_GEOMETRY, PG_CONSTRAINTS, PG_STRUCTURAL, PG_PHASING, PG_DATA, PG_IDENTITY_DATA, PG_IFC, PG_GRAPHICS, PG_MATERIALS, PG_COUPLER_ARRAY, PG_REBAR_SYSTEM_LAYERS, PG_MECHANICAL, PG_MECHANICAL_AIRFLOW, PG_MECHANICAL_LOADS, PG_PLUMBING, PG_PLUMBING_FLOW, PG_ELECTRICAL, PG_ELECTRICAL_CIRCUITING, PG_ELECTRICAL_LIGHTING, PG_ELECTRICAL_LOADS, PG_FIRE_PROTECTION, PG_ENERGY_ANALYSIS, PG_GREEN_BUILDING, PG_SEGMENTS_FITTINGS, PG_TITLE }

    public class Parameter
    {
        public string Definition_Name => Definition?.Name;
        public Definition Definition { get; }
        public StorageType StorageType { get; }
        public bool IsReadOnly { get; }
        public bool HasValue { get; }
        public ElementId Id { get; }
        public bool IsShared { get; }
        public string GUID_String => throw new NotImplementedException();
        public string AsString() => throw new NotImplementedException();
        public double AsDouble() => throw new NotImplementedException();
        public int AsInteger() => throw new NotImplementedException();
        public ElementId AsElementId() => throw new NotImplementedException();
        public string AsValueString() => throw new NotImplementedException();
        public bool Set(string value) => throw new NotImplementedException();
        public bool Set(double value) => throw new NotImplementedException();
        public bool Set(int value) => throw new NotImplementedException();
        public bool Set(ElementId value) => throw new NotImplementedException();
        public bool UserModifiable { get; }
    }

    public class Parameters : IEnumerable<Parameter>
    {
        public IEnumerator<Parameter> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public int Size { get; }
        public bool IsEmpty { get; }
    }

    public abstract class Definition
    {
        public string Name { get; }
        public StorageType ParameterType_Legacy { get; }
        public ForgeTypeId GetDataType() => throw new NotImplementedException();
        public BuiltInParameterGroup ParameterGroup { get; }
        [Obsolete] public ParameterType ParameterType { get; }
    }

    public class ExternalDefinition : Definition
    {
        public System.Guid GUID { get; }
        public string OwnerGroupName { get; }
        public bool Visible { get; }
    }

    public class ExternalDefinitionCreationOptions
    {
        public ExternalDefinitionCreationOptions(string name, ForgeTypeId type) { }
        public ExternalDefinitionCreationOptions(string name, ParameterType type) { }
        public string Name { get; set; }
        public System.Guid GUID { get; set; }
        public bool Visible { get; set; }
        public string Description { get; set; }
    }

    public class InternalDefinition : Definition
    {
        public BuiltInParameter BuiltInParameter { get; }
    }

    public class DefinitionGroup
    {
        public string Name { get; }
        public DefinitionBindingMap Definitions { get; }
        public ExternalDefinition RetrieveDefinition(string name) => throw new NotImplementedException();
        public ExternalDefinition Create(ExternalDefinitionCreationOptions options) => throw new NotImplementedException();
    }

    public class DefinitionGroups : IEnumerable<DefinitionGroup>
    {
        public IEnumerator<DefinitionGroup> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public DefinitionGroup get_Item(string name) => throw new NotImplementedException();
        public DefinitionGroup Create(string name) => throw new NotImplementedException();
        public bool Contains(string name) => throw new NotImplementedException();
    }

    public class DefinitionFile
    {
        public string Filename { get; }
        public DefinitionGroups Groups { get; }
    }

    // ── Binding ───────────────────────────────────────────────────────────────
    public abstract class Binding { }
    public class TypeBinding : Binding { public TypeBinding() { } public TypeBinding(CategorySet categories) { } public CategorySet Categories { get; } }
    public class InstanceBinding : Binding { public InstanceBinding() { } public InstanceBinding(CategorySet categories) { } public CategorySet Categories { get; } }

    public class BindingMap
    {
        public IList<Definition> Keys { get; }
        public bool ReInsert(Definition def, Binding binding) => throw new NotImplementedException();
        public bool Insert(Definition def, Binding binding) => throw new NotImplementedException();
        public bool Insert(Definition def, Binding binding, BuiltInParameterGroup group) => throw new NotImplementedException();
        public Binding get_Item(Definition def) => throw new NotImplementedException();
        public bool Remove(Definition def) => throw new NotImplementedException();
        public bool Contains(Definition def) => throw new NotImplementedException();
    }

    public class DefinitionBindingMap : BindingMap { }

    public class CategorySet : IEnumerable<Category>
    {
        public CategorySet() { }
        public IEnumerator<Category> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public bool Insert(Category category) => throw new NotImplementedException();
        public bool Erase(Category category) => throw new NotImplementedException();
        public bool Contains(Category category) => throw new NotImplementedException();
        public bool IsEmpty { get; }
        public int Size { get; }
    }

    // ── ForgeTypeId ──────────────────────────────────────────────────────────
    public class ForgeTypeId : IEquatable<ForgeTypeId>
    {
        public ForgeTypeId() { }
        public ForgeTypeId(string typeId) { }
        public string TypeId { get; }
        public bool Empty() => throw new NotImplementedException();
        public bool Equals(ForgeTypeId other) => throw new NotImplementedException();
        public override bool Equals(object obj) => throw new NotImplementedException();
        public override int GetHashCode() => throw new NotImplementedException();
        public static bool operator ==(ForgeTypeId a, ForgeTypeId b) => throw new NotImplementedException();
        public static bool operator !=(ForgeTypeId a, ForgeTypeId b) => throw new NotImplementedException();
    }

    public static class SpecTypeId
    {
        public static ForgeTypeId String { get; } = new ForgeTypeId("autodesk.spec:string-2.0.0");
        public static ForgeTypeId Int32 { get; } = new ForgeTypeId("autodesk.spec:int32-2.0.0");
        public static ForgeTypeId Bool { get; } = new ForgeTypeId("autodesk.spec:bool-2.0.0");
        public static ForgeTypeId Length { get; } = new ForgeTypeId("autodesk.spec.aec:length-2.0.0");
        public static ForgeTypeId Area { get; } = new ForgeTypeId("autodesk.spec.aec:area-2.0.0");
        public static ForgeTypeId Volume { get; } = new ForgeTypeId("autodesk.spec.aec:volume-2.0.0");
        public static ForgeTypeId Angle { get; } = new ForgeTypeId("autodesk.spec.aec:angle-2.0.0");
        public static ForgeTypeId Number { get; } = new ForgeTypeId("autodesk.spec:measurable.number-2.0.0");
        public static ForgeTypeId Mass { get; } = new ForgeTypeId("autodesk.spec.aec:mass-2.0.0");
        public static ForgeTypeId Currency { get; } = new ForgeTypeId("autodesk.spec:currency-2.0.0");
        public static ForgeTypeId Reference { get; } = new ForgeTypeId("autodesk.spec:reference-2.0.0");
        public static class AirFlow { public static ForgeTypeId CfmPerSf { get; } = new ForgeTypeId("autodesk.spec.aec.hvac:airFlow-2.0.0"); }
        public static class Flow { public static ForgeTypeId Gpm { get; } = new ForgeTypeId("autodesk.spec.aec.hvac:flow-2.0.0"); }
        public static class ElectricalPower { public static ForgeTypeId Watts { get; } = new ForgeTypeId("autodesk.spec.aec.electrical:power-2.0.0"); }
    }

    public static class UnitTypeId
    {
        public static ForgeTypeId Feet { get; } = new ForgeTypeId("autodesk.unit.unit:feet-1.0.1");
        public static ForgeTypeId Meters { get; } = new ForgeTypeId("autodesk.unit.unit:meters-1.0.1");
        public static ForgeTypeId Millimeters { get; } = new ForgeTypeId("autodesk.unit.unit:millimeters-1.0.1");
        public static ForgeTypeId Inches { get; } = new ForgeTypeId("autodesk.unit.unit:inches-1.0.1");
        public static ForgeTypeId Degrees { get; } = new ForgeTypeId("autodesk.unit.unit:degrees-1.0.1");
    }
}
