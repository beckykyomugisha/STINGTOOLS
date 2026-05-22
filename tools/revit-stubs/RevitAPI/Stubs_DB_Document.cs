// Revit API CI Stubs — Autodesk.Revit.DB Document, View, Sheet, Level, Phase…
using System;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;

namespace Autodesk.Revit.DB
{
    public class FormatOptions { }
    [Obsolete] public enum UnitType { UT_Length, UT_Area, UT_Volume, UT_Angle, UT_Number, UT_Currency, UT_Mass }

    public class Units
    {
        public static double ConvertToInternalUnits(double value, ForgeTypeId unitTypeId) => throw new NotImplementedException();
        public static double ConvertFromInternalUnits(double value, ForgeTypeId unitTypeId) => throw new NotImplementedException();
        [Obsolete] public static double ConvertToInternalUnits(double value, DisplayUnitType dut) => throw new NotImplementedException();
        [Obsolete] public static double ConvertFromInternalUnits(double value, DisplayUnitType dut) => throw new NotImplementedException();
    }
    [Obsolete] public enum DisplayUnitType { DUT_DECIMAL_FEET, DUT_FEET_FRACTIONAL_INCHES, DUT_DECIMAL_INCHES, DUT_METERS, DUT_CENTIMETERS, DUT_MILLIMETERS, DUT_METERS_CENTIMETERS, DUT_DECIMETERS }
    [Obsolete] public class UnitUtils { [Obsolete] public static double ConvertToInternalUnits(double v, DisplayUnitType dut) => throw new NotImplementedException(); [Obsolete] public static double ConvertFromInternalUnits(double v, DisplayUnitType dut) => throw new NotImplementedException(); public static double ConvertToInternalUnits(double v, ForgeTypeId uid) => throw new NotImplementedException(); public static double ConvertFromInternalUnits(double v, ForgeTypeId uid) => throw new NotImplementedException(); }

    public class WorksharingUtils
    {
        public static WorksharingStatus GetCheckoutStatus(Document doc, ElementId id) => throw new NotImplementedException();
        public static WorksharingDisplayData GetWorksharingTooltipInfo(Document doc, ElementId id) => throw new NotImplementedException();
    }
    public enum WorksharingStatus { Owned, OwnedByCurrentUser, CheckedOut, CheckedOutByOtherUser, ModelUpToDate, NotSaved, NotApplicable }
    public class WorksharingDisplayData { public string Creator { get; } public string LastChangedBy { get; } public string Owner { get; } }

    public class ProjectInfo : Element
    {
        public string Name { get; set; }
        public string Number { get; set; }
        public string Address { get; set; }
        public string Author { get; set; }
        public string BuildingName { get; set; }
        public string ClientName { get; set; }
        public string IssueDate { get; set; }
        public string OrganizationDescription { get; set; }
        public string OrganizationName { get; set; }
        public string Status { get; set; }
    }

    public class SiteLocation : Element
    {
        public string PlaceName { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double TimeZone { get; set; }
    }

    public class Level : Element
    {
        public double Elevation { get; set; }
        public double ProjectElevation { get; }
        public static Level Create(Document doc, double elevation) => throw new NotImplementedException();
    }

    public class Phase : Element { }

    public class Workset
    {
        public WorksetId Id { get; }
        public string Name { get; }
        public WorksetKind Kind { get; }
        public bool IsOpen { get; }
        public bool IsEditable { get; }
        public bool IsDefaultWorkset { get; }
        public bool IsUserModifiable { get; }
        public bool UniqueId { get; }
    }
    public class WorksetId { public int IntegerValue { get; } public static WorksetId InvalidWorksetId { get; } = new WorksetId(); }
    public class WorksetTable { public WorksetId GetActiveWorksetId() => throw new NotImplementedException(); public bool SetActiveWorksetId(WorksetId id) => throw new NotImplementedException(); public IList<Workset> GetWorksets() => throw new NotImplementedException(); public Workset GetWorkset(WorksetId id) => throw new NotImplementedException(); }

    public class Document : IDisposable
    {
        public string Title { get; }
        public string PathName { get; }
        public bool IsWorkshared { get; }
        public bool IsFamilyDocument { get; }
        public bool IsLinked { get; }
        public bool IsReadOnly { get; }
        public bool IsModified { get; }
        public bool IsDetached { get; }
        public Application Application { get; }
        public ProjectInfo ProjectInformation { get; }
        public SiteLocation SiteLocation { get; }
        public BindingMap ParameterBindings { get; }
        public WorksetTable GetWorksetTable() => throw new NotImplementedException();
        public Element GetElement(ElementId id) => throw new NotImplementedException();
        public Element GetElement(Reference reference) => throw new NotImplementedException();
        public T GetElement<T>(ElementId id) where T : Element => throw new NotImplementedException();
        public IList<Element> GetElements(ICollection<ElementId> ids) => throw new NotImplementedException();
        public void Delete(ICollection<ElementId> ids) => throw new NotImplementedException();
        public ISet<ElementId> Delete(ElementId id) => throw new NotImplementedException();
        public View ActiveView { get; }
        public FamilyManager FamilyManager { get; }
        public Settings Settings { get; }
        public Units GetUnits() => throw new NotImplementedException();
        public void SetUnits(Units units) => throw new NotImplementedException();
        public string GetCloudModelPath() => throw new NotImplementedException();
        public void Regenerate() => throw new NotImplementedException();
        public void Close(bool saveModified = false) => throw new NotImplementedException();
        public RevisionTable GetRevisionTable() => throw new NotImplementedException();
        public PhaseArray Phases { get; }
        public void Dispose() { }
        public bool IsValidObject { get; }
    }

    public class Settings
    {
        public Categories Categories { get; }
        public FillPatternSettings FillPatternSettings { get; }
        public LinePatternSettings LinePatternSettings { get; }
    }

    public class Categories : IEnumerable<Category>
    {
        public IEnumerator<Category> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public Category get_Item(BuiltInCategory bic) => throw new NotImplementedException();
        public Category get_Item(string name) => throw new NotImplementedException();
        public bool Contains(BuiltInCategory bic) => throw new NotImplementedException();
    }

    public class FillPatternSettings { }
    public class LinePatternSettings { }

    public class PhaseArray : IEnumerable<Phase>
    {
        public IEnumerator<Phase> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerable.GetEnumerator GetEnumerator2() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public int Size { get; }
        public Phase get_Item(int index) => throw new NotImplementedException();
    }

    // ── View ─────────────────────────────────────────────────────────────────
    public class View : Element
    {
        public ViewType ViewType { get; }
        public int Scale { get; set; }
        public DetailLevel DetailLevel { get; set; }
        public ViewDiscipline Discipline { get; set; }
        public string ViewName { get; set; }
        public bool IsTemplate { get; }
        public string ViewTemplateId_Name { get; }
        public ElementId ViewTemplateId { get; set; }
        public bool TemporaryViewPropertiesModeEnabled { get; }
        public bool IsCallout { get; }
        public bool IsPerspective { get; }
        public DisplayStyle DisplayStyle { get; set; }
        public bool AreGraphicsOverridesAllowed() => throw new NotImplementedException();
        public ElementId PhaseId { get; set; }
        public ElementId PhaseFilterId { get; set; }
        public OverrideGraphicSettings GetElementOverrides(ElementId elementId) => throw new NotImplementedException();
        public void SetElementOverrides(ElementId elementId, OverrideGraphicSettings overrideGraphicSettings) => throw new NotImplementedException();
        public bool GetCategoryHidden(ElementId categoryId) => throw new NotImplementedException();
        public void SetCategoryHidden(ElementId categoryId, bool hidden) => throw new NotImplementedException();
        public bool GetCategoryHidden(Category category) => throw new NotImplementedException();
        public void SetCategoryHidden(Category category, bool hidden) => throw new NotImplementedException();
        public bool GetFilterVisibility(ElementId filterId) => throw new NotImplementedException();
        public void SetFilterVisibility(ElementId filterId, bool visibility) => throw new NotImplementedException();
        public OverrideGraphicSettings GetFilterOverrides(ElementId filterId) => throw new NotImplementedException();
        public void SetFilterOverrides(ElementId filterId, OverrideGraphicSettings overrides) => throw new NotImplementedException();
        public ICollection<ElementId> GetFilters() => throw new NotImplementedException();
        public void AddFilter(ElementId filterId) => throw new NotImplementedException();
        public void RemoveFilter(ElementId filterId) => throw new NotImplementedException();
        public IList<ElementId> GetDependentViewIds() => throw new NotImplementedException();
        public ElementId GenLevel { get; }
        public ElementId AssociatedLevelId { get; }
        public void EnableTemporaryViewPropertiesMode(ElementId viewId) => throw new NotImplementedException();
        public void DisableTemporaryViewMode(TemporaryViewMode mode) => throw new NotImplementedException();
        public void ConvertTemporaryHideIsolateToPermanent() => throw new NotImplementedException();
        public ViewOrientation3D GetOrientation() => throw new NotImplementedException();
        public void SetOrientation(ViewOrientation3D orientation) => throw new NotImplementedException();
        public CropRegionShapeManager GetCropRegionShapeManager() => throw new NotImplementedException();
        public bool CropBoxActive { get; set; }
        public BoundingBoxXYZ CropBox { get; set; }
        public bool CropBoxVisible { get; set; }
        public bool AnnotationCropActive { get; set; }
        public ElementId GetTypeId() => throw new NotImplementedException();
        public ElementId LookupParameter_Id(string name) => throw new NotImplementedException();
        public Outline get_Outline() => throw new NotImplementedException();
    }

    public class View3D : View
    {
        public void SetSectionBox(BoundingBoxXYZ box) => throw new NotImplementedException();
        public BoundingBoxXYZ GetSectionBox() => throw new NotImplementedException();
        public bool IsSectionBoxActive { get; set; }
        public static View3D CreateIsometric(Document doc, ElementId viewFamilyTypeId) => throw new NotImplementedException();
        public static View3D CreatePerspective(Document doc, ElementId viewFamilyTypeId) => throw new NotImplementedException();
    }

    public class ViewPlan : View
    {
        public ViewType ViewType { get; }
        public Level GenLevel { get; }
        public static ViewPlan Create(Document doc, ElementId viewFamilyTypeId, ElementId levelId) => throw new NotImplementedException();
    }

    public class ViewSection : View
    {
        public static ViewSection CreateSection(Document doc, ElementId viewFamilyTypeId, BoundingBoxXYZ sectionBox) => throw new NotImplementedException();
        public static ViewSection CreateCallout(Document doc, ElementId parentViewId, ElementId viewFamilyTypeId, UV point1, UV point2) => throw new NotImplementedException();
    }

    public class ViewDrafting : View
    {
        public static ViewDrafting Create(Document doc, ElementId viewFamilyTypeId) => throw new NotImplementedException();
    }

    public class ViewSheet : View
    {
        public string SheetNumber { get; set; }
        public string Name { get; set; }
        public ISet<ElementId> GetAllViewports() => throw new NotImplementedException();
        public ISet<ElementId> GetAllPlacedViews() => throw new NotImplementedException();
        public bool CanViewBePlaced(ElementId viewId) => throw new NotImplementedException();
        public static ViewSheet Create(Document doc, ElementId titleBlockTypeId) => throw new NotImplementedException();
        public bool IsPlaceholder { get; set; }
    }

    public class ViewFamilyType : ElementType { public ViewFamily ViewFamily { get; } }
    public enum ViewFamily { Invalid, FloorPlan, CeilingPlan, Section, Elevation, Detail, ThreeDimensional, Walkthrough, Rendering, DraftingView, Schedule, ImageView, DrawingSheet, ReportView, Legend, EngineeringPlan, AreaPlan, AssemblyView, CostReport, LoadsReport, PanelSchedule }

    public class ViewOrientation3D { public ViewOrientation3D(XYZ eyePosition, XYZ upDirection, XYZ forwardDirection) { } public XYZ EyePosition { get; } public XYZ UpDirection { get; } public XYZ ForwardDirection { get; } }

    public class CropRegionShapeManager
    {
        public bool CanHaveShape { get; }
        public void RemoveCropRegionShape() => throw new NotImplementedException();
        public void SetCropShape(CurveLoop cropShape) => throw new NotImplementedException();
        public CurveLoop GetCropShape() => throw new NotImplementedException();
    }

    // ── Viewport & Sheet ─────────────────────────────────────────────────────
    public class Viewport : Element
    {
        public ElementId SheetId { get; }
        public ElementId ViewId { get; }
        public XYZ GetBoxCenter() => throw new NotImplementedException();
        public void SetBoxCenter(XYZ center) => throw new NotImplementedException();
        public Outline GetBoxOutline() => throw new NotImplementedException();
        public Outline GetLabelOutline() => throw new NotImplementedException();
        public ElementId GetTypeId() => throw new NotImplementedException();
        public void ChangeTypeId(ElementId newTypeId) => throw new NotImplementedException();
        public string get_DetailNumber() => throw new NotImplementedException();
        public static Viewport Create(Document doc, ElementId sheetId, ElementId viewId, XYZ point) => throw new NotImplementedException();
        public static bool CanAddViewToSheet(Document doc, ElementId sheetId, ElementId viewId) => throw new NotImplementedException();
    }

    public class Outline { public Outline(XYZ minimum, XYZ maximum) { } public XYZ MinimumPoint { get; } public XYZ MaximumPoint { get; } }

    // ── Revision ─────────────────────────────────────────────────────────────
    public class Revision : Element
    {
        public string RevisionDate { get; set; }
        public string Description { get; set; }
        public string IssuedBy { get; set; }
        public string IssuedTo { get; set; }
        public bool Issued { get; set; }
        public RevisionNumberType NumberType { get; set; }
        public int SequenceNumber { get; }
        public static Revision Create(Document doc) => throw new NotImplementedException();
    }
    public enum RevisionNumberType { None, Numeric, Alphanumeric, PerProject, PerSheet }
    public class RevisionTable
    {
        public IList<ElementId> GetRevisionIds() => throw new NotImplementedException();
        public int GetRevisionNumber(Revision revision) => throw new NotImplementedException();
    }

    // ── OverrideGraphicSettings ──────────────────────────────────────────────
    public class OverrideGraphicSettings
    {
        public OverrideGraphicSettings() { }
        public bool IsHalftone { get; }
        public int Transparency { get; }
        public void SetHalftone(bool value) => throw new NotImplementedException();
        public void SetSurfaceTransparency(int value) => throw new NotImplementedException();
        public void SetProjectionLineColor(Color c) => throw new NotImplementedException();
        public void SetProjectionLineWeight(int w) => throw new NotImplementedException();
        public void SetProjectionLinePatternId(ElementId id) => throw new NotImplementedException();
        public void SetSurfaceForegroundPatternColor(Color c) => throw new NotImplementedException();
        public void SetSurfaceForegroundPatternId(ElementId id) => throw new NotImplementedException();
        public void SetSurfaceBackgroundPatternColor(Color c) => throw new NotImplementedException();
        public void SetSurfaceBackgroundPatternId(ElementId id) => throw new NotImplementedException();
        public void SetCutLineColor(Color c) => throw new NotImplementedException();
        public void SetCutLineWeight(int w) => throw new NotImplementedException();
        public void SetCutLinePatternId(ElementId id) => throw new NotImplementedException();
        public void SetCutForegroundPatternColor(Color c) => throw new NotImplementedException();
        public void SetCutForegroundPatternId(ElementId id) => throw new NotImplementedException();
        public void SetCutBackgroundPatternColor(Color c) => throw new NotImplementedException();
        public void SetCutBackgroundPatternId(ElementId id) => throw new NotImplementedException();
        public Color ProjectionLineColor { get; }
        public int ProjectionLineWeight { get; }
        public ElementId ProjectionLinePatternId { get; }
        public Color SurfaceForegroundPatternColor { get; }
        public ElementId SurfaceForegroundPatternId { get; }
        public Color SurfaceBackgroundPatternColor { get; }
        public ElementId SurfaceBackgroundPatternId { get; }
        public Color CutLineColor { get; }
        public int CutLineWeight { get; }
        public ElementId CutLinePatternId { get; }
        public Color CutForegroundPatternColor { get; }
        public ElementId CutForegroundPatternId { get; }
        public bool IsCutForegroundPatternVisible { get; }
        public bool IsSurfaceForegroundPatternVisible { get; }
        public bool IsProjectionLineColorByCategory() => throw new NotImplementedException();
        public bool IsSurfaceForegroundPatternByCategory() => throw new NotImplementedException();
    }

    // ── GraphicsStyle ────────────────────────────────────────────────────────
    public class GraphicsStyle : Element { public GraphicsStyleType GraphicsStyleType { get; } public Category GraphicsStyleCategory { get; } }
    public class LinePatternId { public LinePatternId() { } }
    public class LinePatternElement : Element { public string Name { get; set; } public static ElementId GetSolidPatternId() => throw new NotImplementedException(); public LinePattern GetLinePattern() => throw new NotImplementedException(); }
    public class LinePattern { public string Name { get; set; } public IList<LinePatternSegment> GetSegments() => throw new NotImplementedException(); public static string SolidPatternName => "Solid"; }
    public class LinePatternSegment { public LinePatternSegmentType Type { get; } public double Length { get; } }
    public enum LinePatternSegmentType { Dot, Dash, Space }

    public class FillPatternElement : Element
    {
        public FillPattern GetFillPattern() => throw new NotImplementedException();
        public static FillPatternElement Create(Document doc, FillPattern pattern) => throw new NotImplementedException();
        public static IList<FillPatternElement> GetFillPatternElementsByName(Document doc, FillPatternTarget target, string name) => throw new NotImplementedException();
    }
    public class FillPattern { public string Name { get; set; } public bool IsSolidFill { get; } public FillPatternTarget Target { get; } public FillPattern(string name, FillPatternTarget target, FillPatternHostOrientation orient, double angle, double spacing1, double spacing2) { } }
    public enum FillPatternTarget { Model = 0, Drafting = 1 }
    public enum FillPatternHostOrientation { ToView, ToHost }

    // ── Annotation ───────────────────────────────────────────────────────────
    public class IndependentTag : Element
    {
        public ElementId TaggedElementId { get; }
        public XYZ TagHeadPosition { get; set; }
        public bool HasLeader { get; set; }
        public LeaderEndCondition LeaderEndCondition { get; set; }
        public TagOrientation TagOrientation { get; set; }
        public TagMode TagMode { get; }
        public Reference GetTaggedReference() => throw new NotImplementedException();
        public IList<Reference> GetTaggedReferences() => throw new NotImplementedException();
        public XYZ GetLeaderEnd(Reference reference) => throw new NotImplementedException();
        public void SetLeaderElbow(Reference reference, XYZ elbowPoint) => throw new NotImplementedException();
        public XYZ GetLeaderElbow(Reference reference) => throw new NotImplementedException();
        public bool IsOrphaned { get; }
        public static IndependentTag Create(Document doc, ElementId tagSymbolId, ElementId viewId, Reference reference, bool addLeader, TagOrientation tagOrientation, XYZ point) => throw new NotImplementedException();
        public static IndependentTag Create(Document doc, ElementId viewId, Reference reference, bool addLeader, TagMode tagMode, TagOrientation tagOrientation, XYZ point) => throw new NotImplementedException();
        public void ChangeTypeId(ElementId typeId) => throw new NotImplementedException();
    }

    public enum LeaderEndCondition { Free, Attached }
    public enum TagOrientation { Horizontal, Vertical, AnyModelTagOrientation }
    public enum TagMode { TM_ADDBY_CATEGORY = 0, TM_ADDBY_MULTICATEGORY = 1, TM_ADDBY_MATERIAL = 2, TM_ADDBY_ELEMENT = 3 }

    public class TextNote : Element
    {
        public string Text { get; set; }
        public XYZ Coord { get; set; }
        public static TextNote Create(Document doc, ElementId viewId, XYZ origin, string text, TextNoteOptions options) => throw new NotImplementedException();
        public static TextNote Create(Document doc, ElementId viewId, XYZ origin, double width, string text, TextNoteOptions options) => throw new NotImplementedException();
    }
    public class TextNoteOptions { public TextNoteOptions() { } public TextNoteOptions(ElementId typeId) { } public ElementId TypeId { get; set; } public HorizontalTextAlignment HorizontalAlignment { get; set; } public bool KeepRotatedTextReadable { get; set; } }
    public enum HorizontalTextAlignment { Left, Center, Right }

    public class TextNoteType : ElementType { }
    public class DimensionType : ElementType { }
    public class Dimension : Element { }
}
