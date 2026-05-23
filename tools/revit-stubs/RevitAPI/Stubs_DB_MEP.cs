// Revit API CI Stubs — Autodesk.Revit.DB MEP sub-namespaces
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Autodesk.Revit.DB.Mechanical
{
    public class Duct : MEPCurve
    {
        public DuctType DuctType { get; }
        public DuctShape DuctShape { get; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Diameter { get; set; }
        public static Duct Create(Document doc, ElementId systemTypeId, ElementId ductTypeId, ElementId levelId, XYZ startPoint, XYZ endPoint) => throw new NotImplementedException();
        public static Duct CreatePlaceholder(Document doc, ElementId ductTypeId, ElementId levelId, XYZ startPoint, XYZ endPoint) => throw new NotImplementedException();
    }
    public class DuctType : ElementType { public DuctShape Shape { get; } }
    public enum DuctShape { Round, Rectangular, Oval, Undefined }

    public class FlexDuct : MEPCurve
    {
        public static FlexDuct Create(Document doc, ElementId flexDuctTypeId, ElementId levelId, IList<XYZ> points) => throw new NotImplementedException();
    }
    public class FlexDuctType : ElementType { }

    public class MechanicalSystem : MEPSystem
    {
        public string Name { get; }
        public DuctSystemType SystemType { get; }
        public ISet<Element> DuctNetwork { get; }
        public double GetFlow() => throw new NotImplementedException();
    }
    public enum DuctSystemType { SupplyAir, ReturnAir, ExhaustAir, OtherAir, UndefinedSystemType, GlobalAirSystem }

    public class DuctInsulation : Element { public static DuctInsulation Create(Document doc, ElementId ductId, ElementId insulationTypeId, double thickness) => throw new NotImplementedException(); }
    public class DuctInsulationType : ElementType { }
    public class DuctLining : Element { public static DuctLining Create(Document doc, ElementId ductId, ElementId liningTypeId, double thickness) => throw new NotImplementedException(); }

    public class MechanicalEquipment : FamilyInstance { }
}

namespace Autodesk.Revit.DB.Plumbing
{
    public class Pipe : MEPCurve
    {
        public PipeType PipeType { get; }
        public double Diameter { get; set; }
        public double OuterDiameter { get; }
        public static Pipe Create(Document doc, ElementId systemTypeId, ElementId pipeTypeId, ElementId levelId, XYZ startPoint, XYZ endPoint) => throw new NotImplementedException();
        public static Pipe CreatePlaceholder(Document doc, ElementId pipeTypeId, ElementId levelId, XYZ startPoint, XYZ endPoint) => throw new NotImplementedException();
    }
    public class PipeType : ElementType { }

    public class FlexPipe : MEPCurve
    {
        public static FlexPipe Create(Document doc, ElementId flexPipeTypeId, ElementId levelId, IList<XYZ> points) => throw new NotImplementedException();
    }
    public class FlexPipeType : ElementType { }

    public class PipingSystem : MEPSystem
    {
        public string Abbreviation { get; set; }
        public PipeSystemType SystemType { get; }
        public double GetFlow() => throw new NotImplementedException();
    }
    public enum PipeSystemType { DomesticColdWater, DomesticHotWater, DomesticHotWaterReturn, Sanitary, OtherDrainSupply, Other, FireSuppressWet, FireSuppressDry, FireSuppressPreAction, HydronicSupply, HydronicReturn, OtherDrainDrainageReuse, ReturningCondensate, UndefinedSystemType }

    public class PipeInsulation : Element { public static PipeInsulation Create(Document doc, ElementId pipeId, ElementId insulationTypeId, double thickness) => throw new NotImplementedException(); }
    public class PipeInsulationType : ElementType { }

    public class PlumbingFixture : FamilyInstance { }
    public class Sprinkler : FamilyInstance { }
}

namespace Autodesk.Revit.DB.Electrical
{
    public class CableTray : MEPCurve
    {
        public CableTrayType CableTrayType { get; }
        public double Width { get; set; }
        public double Height { get; set; }
        public static CableTray Create(Document doc, ElementId cableTrayTypeId, ElementId levelId, XYZ startPoint, XYZ endPoint) => throw new NotImplementedException();
    }
    public class CableTrayType : ElementType { }

    public class Conduit : MEPCurve
    {
        public ConduitType ConduitType { get; }
        public double Diameter { get; set; }
        public static Conduit Create(Document doc, ElementId conduitTypeId, ElementId levelId, XYZ startPoint, XYZ endPoint) => throw new NotImplementedException();
    }
    public class ConduitType : ElementType { }

    public class ElectricalSystem : MEPSystem
    {
        public string PanelName { get; }
        public string CircuitNumber { get; }
        public double ApparentLoad { get; }
        public double TrueLoad { get; }
        public double Voltage { get; }
        public int NumberOfPoles { get; }
        public ElectricalSystemType SystemType { get; }
        public ISet<Element> Elements { get; }
        public FamilyInstance BaseEquipment { get; }
    }
    public enum ElectricalSystemType { Data = 1, PowerBalanced = 2, PowerUnBalanced = 3, Telephone = 4, Security = 5, Fire = 6, Nurse = 7, Controls = 8, Communication = 9, UndefinedSystemType = 0 }

    public class ElectricalEquipment : FamilyInstance { }
    public class LightingFixture : FamilyInstance { }
    public class LightingDevice : FamilyInstance { }
    public class FireAlarmDevice : FamilyInstance { }
    public class DataDevice : FamilyInstance { }
    public class CommunicationDevice : FamilyInstance { }
    public class SecurityDevice : FamilyInstance { }
    public class NurseCallDevice : FamilyInstance { }
}

namespace Autodesk.Revit.DB.Structure
{
    public enum StructuralType { NonStructural = 0, Column = 1, Beam = 2, Brace = 3, Footing = 4 }
    public enum StructuralUsage { Undefined = 0, Automatic = 1, Lateral = 2, Gravity = 3, Wind = 4 }
    public enum MultiplanarOption { IncludeOnlyPlanarCurves = 0, IncludeAllWorkplanes = 1 }

    public class Rebar : Element
    {
        public RebarBarType GetBarType() => throw new NotImplementedException();
        public IList<Curve> GetCenterlineCurves(bool adjustForSelfIntersection, bool allowSplits, bool includeHooks, MultiplanarOption multiplanarOption) => throw new NotImplementedException();
        public static Rebar CreateFromCurves(Document doc, RebarStyle style, RebarBarType barType, RebarHookType hookAtStart, RebarHookType hookAtEnd, Element host, XYZ norm, IList<Curve> curves, RebarShapeDefinitionByArc startHookMode, RebarShapeDefinitionByArc endHookMode, bool useExistingShape, bool createNewShape) => throw new NotImplementedException();
    }
    public class RebarBarType : ElementType { public double BarDiameter { get; } }
    public class RebarHookType : ElementType { }
    public enum RebarStyle { Standard, StirrupTie }
    public class RebarShapeDefinitionByArc { public static RebarShapeDefinitionByArc NotUsed { get; } = null; }
    public class RebarShape : ElementType { }

    public class StructuralAsset { public string Name { get; set; } public StructuralAssetClass StructuralAssetClass { get; set; } }
    public enum StructuralAssetClass { Undefined, Basic, Concrete, Steel, PrecastConcrete, Wood, Other, Aluminum }
    public class AnalyticalModel : Element { }
}

namespace Autodesk.Revit.DB.Architecture
{
    public class Room : SpatialElement
    {
        public string Name { get; set; }
        public Phase Phase2 { get; }
        public static Room Create(Document doc, ElementId levelId, UV point) => throw new NotImplementedException();
    }
    public class RoomTag : IndependentTag
    {
        public Room Room { get; }
        public static RoomTag Create(Document doc, ElementId viewId, LinkElementId linkElementId, UV point) => throw new NotImplementedException();
    }
    public class RoomBoundaryOptions
    {
        public RoomBoundaryOptions() { }
        public SpatialElementBoundaryLocation SpatialElementBoundaryLocation { get; set; }
        public bool StoreFreeBoundaryFaces { get; set; }
    }
    public class Stairs : Element { }
    public class StairsRun : Element { }
    public class StairsLanding : Element { }
    public class StairsType : ElementType { }
    public class Railing : Element { }
    public class RailingType : ElementType { }
    public class Opening : Element { }

    public class LinkElementId
    {
        public LinkElementId(ElementId hostElementId) { }
        public LinkElementId(ElementId linkInstanceId, ElementId linkedElementId) { }
        public ElementId HostElementId { get; }
        public ElementId LinkedElementId { get; }
        public ElementId LinkInstanceId { get; }
        public bool IsLinkedElement { get; }
    }
}

namespace Autodesk.Revit.DB.Fabrication
{
    public class FabricationPart : Element
    {
        public string ItemDescription { get; }
        public string ItemNumber { get; }
        public double SpoolNumber { get; }
        public static bool IsFabricationPart(Element element) => throw new NotImplementedException();
    }
}
