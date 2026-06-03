// Revit API CI Stubs — Autodesk.Revit.DB large enums
using System;

namespace Autodesk.Revit.DB
{
    public enum BuiltInCategory
    {
        INVALID = -1,
        OST_Mass = -2000151,
        OST_MassFloor = -2000032,
        OST_Walls = -2000011,
        OST_Floors = -2000032 + 1,
        OST_Doors = -2000023,
        OST_Windows = -2000014,
        OST_Rooms = -2000160,
        OST_Ceilings = -2000038,
        OST_Roofs = -2000035,
        OST_Columns = -2000001,
        OST_StructuralColumns = -2001330,
        OST_StructuralFraming = -2001331,
        OST_StructuralFoundation = -2001320,
        OST_StructuralStiffener = -2001332,
        OST_StructuralTruss = -2001336,
        OST_Stairs = -2000120,
        OST_StairsRailing = -2000199,
        OST_Ramps = -2000180,
        OST_CurtainWallPanels = -2000170,
        OST_CurtainWallMullions = -2000171,
        OST_MechanicalEquipment = -2000116,
        OST_PlumbingFixtures = -2000126,
        OST_ElectricalEquipment = -2000094,
        OST_ElectricalFixtures = -2000095,
        OST_LightingFixtures = -2000111,
        OST_LightingDevices = -2001350,
        OST_FireAlarmDevices = -2000289,
        OST_DataDevices = -2000287,
        OST_CommunicationDevices = -2000285,
        OST_SecurityDevices = -2000291,
        OST_NurseCallDevices = -2000288,
        OST_DuctCurves = -2000279,
        OST_DuctFitting = -2000280,
        OST_DuctAccessory = -2000284,
        OST_DuctTerminal = -2000282,
        OST_DuctInsulations = -2000290,
        OST_FlexDuctCurves = -2000278,
        OST_PipeCurves = -2000171 + 1,
        OST_PipeFitting = -2000172 + 1,
        OST_PipeAccessory = -2000283,
        OST_PipeInsulations = -2000285 + 1,
        OST_FlexPipeCurves = -2000277,
        OST_Sprinklers = -2000137,
        OST_CableTray = -2000276,
        OST_CableTrayFitting = -2000277 + 1,
        OST_Conduit = -2000274,
        OST_ConduitFitting = -2000275,
        OST_GenericModel = -2000151 + 1,
        OST_Furniture = -2000089,
        OST_FurnitureSystems = -2000090,
        OST_Casework = -2000080,
        OST_SpecialityEquipment = -2000148,
        OST_MedicalEquipment = -2000117,
        OST_Entourage = -2000099,
        OST_Site = -2000136,
        OST_Parking = -2000125,
        OST_Grids = -2000060,
        OST_Levels = -2000016,
        OST_Views = -2000279 + 1,
        OST_Sheets = -2000278 + 2,
        // OST_Rooms already defined above (line 15) — duplicate removed (CS0102).
        OST_Spaces = -2000161,
        OST_Areas = -2000162,
        OST_Tags = -2000441,
        OST_RoomTags = -2000480,
        OST_SpaceTags = -2000481,
        OST_AreaTags = -2000482,
        OST_KeynoteTags = -2000485,
        OST_MaterialTags = -2000486,
        OST_MultiCategoryTags = -2000487,
        OST_DoorTags = -2000488,
        OST_WindowTags = -2000489,
        OST_WallTags = -2000490,
        OST_FloorTags = -2000491,
        OST_CeilingTags = -2000492,
        OST_RoofTags = -2000493,
        OST_StructuralFramingTags = -2000494,
        OST_StructuralColumnTags = -2000495,
        OST_MechanicalEquipmentTags = -2000496,
        OST_PlumbingFixtureTags = -2000497,
        OST_ElectricalEquipmentTags = -2000498,
        OST_ElectricalFixtureTags = -2000499,
        OST_LightingFixtureTags = -2000500,
        OST_DuctTags = -2000501,
        OST_PipeTags = -2000502,
        OST_SprinklerTags = -2000503,
        OST_CableTrayTags = -2000504,
        OST_ConduitTags = -2000505,
        OST_GenericModelTags = -2000506,
        OST_FurnitureTags = -2000507,
        OST_CaseworkTags = -2000508,
        OST_FurnitureSystemTags = -2000509,
        OST_SpecialityEquipmentTags = -2000510,
        OST_StairsTags = -2000511,
        OST_RampTags = -2000512,
        OST_CurtainWallPanelTags = -2000513,
        OST_AnnotationSymbols = -2000360,
        OST_Lines = -2000051,
        OST_ReferencePlanes = -2000061,
        OST_Sections = -2000200,
        OST_Elevations = -2000200 + 1,
        OST_Callouts = -2000200 + 2,
        OST_FloorReferencePlane = -2000027,
        OST_Dimensions = -2000060 + 1,
        OST_Text = -2000440,
        OST_Matchline = -2000454,
        OST_SitePropertyLineSegment = -2000135,
        OST_HydronicZones = -2000395,
        OST_HVAC_Zones = -2000396,
        OST_Materials = -2000140,
        OST_Assemblies = -2000550,
        OST_AssemblyOrigin = -2000551,
        OST_Parts = -2000560,
        OST_DetailComponents = -2000151 + 2,
    }

    public enum BuiltInParameter
    {
        INVALID = -1,
        ALL_MODEL_MARK = -1002025,
        ALL_MODEL_DESCRIPTION = -1002016,
        ALL_MODEL_MANUFACTURER = -1002022,
        ALL_MODEL_MODEL = -1002019,
        ALL_MODEL_URL = -1002030,
        ALL_MODEL_FAMILY_NAME = -1002031,
        ALL_MODEL_TYPE_NAME = -1002014,
        ALL_MODEL_COST = -1002017,
        ALL_MODEL_IMAGE = -1002020,
        ALL_MODEL_KEYNOTE = -1002018,
        ALL_MODEL_TYPE_MARK = -1002014 + 1,
        ALL_MODEL_TYPE_COMMENTS = -1002024,
        ALL_MODEL_INSTANCE_COMMENTS = -1002021,
        ELEM_FAMILY_PARAM = -1002033,
        ELEM_FAMILY_AND_TYPE_PARAM = -1002032,
        ELEM_TYPE_PARAM = -1002034,
        ELEM_CATEGORY_PARAM = -1002050,
        ELEM_PARTITION_PARAM = -1002053,
        OWNER_VIEW_ID = -1002051,
        PHASE_CREATED = -1006082,
        PHASE_DEMOLISHED = -1006083,
        DOOR_HEIGHT = -1002097,
        DOOR_WIDTH = -1002098,
        WINDOW_HEIGHT = -1002099,
        WINDOW_WIDTH = -1002100,
        ROOM_NAME = -1003258,
        ROOM_NUMBER = -1003260,
        ROOM_DEPARTMENT = -1003278,
        ROOM_AREA = -1003261,
        ROOM_OCCUPANCY = -1003264,
        ROOM_FINISH_FLOOR = -1003263,
        ROOM_FINISH_WALL = -1003265,
        ROOM_FINISH_CEILING = -1003262,
        SPACE_OCCUPANCY = -1003264,
        LEVEL_ELEV = -1006090,
        SHEET_NUMBER = -1002070,
        SHEET_NAME = -1002071,
        SHEET_SCALE = -1002073,
        VIEW_NAME = -1006961,
        VIEW_SCALE_PULLDOWN_METRIC = -1007023,
        VIEW_SCALE_PULLDOWN_IMPERIAL = -1007024,
        VIEW_PHASE = -1007052,
        VIEW_PHASE_FILTER = -1007053,
        VIEW_DETAIL_LEVEL = -1007049,
        VIEW_DISCIPLINE = -1007007,
        VIEWPORT_VIEW_ID = -1010010,
        VIEWPORT_SHEET_ID = -1010009,
        VIEWPORT_DETAIL_NUMBER = -1010011,
        IMPORT_SYMBOL_EXPLODED = -1009005,
        FAMILY_LEVEL_PARAM = -1002066,
        FAMILY_BASE_LEVEL_PARAM = -1002067,
        FAMILY_TOP_LEVEL_PARAM = -1002068,
        FAMILY_BASE_LEVEL_OFFSET_PARAM = -1002069,
        FAMILY_HOSTED_PARAM = -1002017 + 1,
        INSTANCE_ELEVATION_PARAM = -1002065,
        INSTANCE_FREE_HOST_OFFSET_PARAM = -1002065 + 1,
        INSTANCE_REFERENCE_LEVEL_PARAM = -1002066 + 1,
        STRUCTURAL_USAGE_PARAM = -1002001,
        STRUCT_MULTI_STORY_UP_TO_LEVEL = -1002002,
        GRID_NAME = -1006960,
        LEVEL_NAME = -1006960 + 1,
        WALL_BASE_OFFSET = -1000790,
        WALL_BASE_CONSTRAINT = -1000789,
        WALL_TOP_OFFSET = -1000792,
        WALL_TOP_CONSTRAINT = -1000791,
        WALL_HEIGHT_TYPE = -1000789 + 1,
        ELEM_ROOM_ID = -1003272,
        RBS_SYSTEM_NAME_PARAM = -1162000,
        RBS_SYSTEM_ABBREVIATION_PARAM = -1162001,
        RBS_DUCT_FLOW_PARAM = -1163200,
        RBS_PIPE_FLOW_PARAM = -1162004,
        RBS_ELEC_CIRCUIT_NAME = -1161150,
        RBS_ELEC_PANEL_NAME = -1161120,
        RBS_ELEC_LOAD_CLASSIFICATION = -1161130,
        SCHEDULE_PHASE_PARAM = -1007053 + 1,
        HOST_VOLUME_COMPUTED = -1002065 + 2,
        CURVE_ELEM_LENGTH = -1000882,
        INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM = -1002066 + 2,
        REVISION_SEQUENCE = -1006064,
        REVISION_NUMBER = -1006065,
        REVISION_DESCRIPTION = -1006066,
        REVISION_DATE = -1006067,
        REVISION_ISSUED = -1006068,
        REVISION_ISSUED_BY = -1006069,
        REVISION_ISSUED_TO = -1006070,
    }

    public enum WorksharingTooltipInfoType { None, Owned, CheckedOut, CheckedOutByOther }

    public class WorksharingTooltipInfo
    {
        public string Creator { get; }
        public string LastChangedBy { get; }
        public string Owner { get; }
        public WorksharingTooltipInfoType CurrentOwnerId { get; }
    }

    public enum ViewType { Undefined, FloorPlan, EngineeringPlan, AreaPlan, CeilingPlan, Elevation, Section, Detail, ThreeD, Schedule, DraftingView, Legend, Report, ProjectBrowser, SystemBrowser, DrawingSheet, ReflectedCeilingPlan, WalkThrough, Rendering, ColumnSchedule, PanelSchedule, CostReport, LoadsReport }
    public enum ViewDiscipline { Architectural = 1, Structural = 2, Mechanical = 4, Electrical = 8, Plumbing = 16, Coordination = 4096 }
    public enum DisplayStyle { Wireframe, HLR, Shading, ShadingWithEdges, Consistent, FlatColors, RealisticWithEdges, Realistic, PathOfTravel, Raytrace }
    public enum DetailLevel { Coarse = 1, Medium = 2, Fine = 3 }
    public enum WorkPlane_Visibility { Visible, Invisible, UseGlobalSettings }
    public enum ViewDetailLevel { Coarse = 1, Medium = 2, Fine = 3 }

    public enum CurveLoop_Visibility { None, Visible, Hidden }
    public enum TemporaryViewMode { TemporaryHideIsolate = 1, RevealHiddenElements = 2, TemporaryViewProperties = 3, WorksharingDisplay = 4, AnalysisResults = 5 }
    public enum WallSide { Interior, Exterior }
    public enum EndpointType { Start, End }
    public enum GraphicsStyleType { Projection, Cut }
    public enum GreenBuildingScheme { LEED, BREEAM, None }
    public enum JoinGeometryState { None, NotJoined, FirstJoinsSecond, SecondJoinsFirst }
    public enum PhasesStatus { Existing, New, Demolished, Temporary, None }
    public enum WorksetKind { UserWorkset = 1, FamilyWorkset = 2, ViewWorkset = 4, CheckoutWorkset = 8, StandardWorkset = 16, None = 0 }

    public enum FamilyPlacementType { Invalid, OneLevelBased, OneLevelBasedHosted, TwoLevelsBased, WorkPlaneBased, ViewBased, CurveBased, CurveBasedDetail, CurveDrivenStructural, AdaptiveComponent, Irrelevant }

    public enum PrintRange { Select, CurrentWindow, Visible, View }
    public enum HiddenLineViewsType { HLR, Vector, Raster }
    public enum RasterQualityType { Draft, Low, Medium, High, Presentation }
    public enum ZoomFitType { FitToPage, Zoom }
    public enum PaperPlacementType { Center, Margins, UserDefined }
    public enum ColorDepthType { Color, BlackLine, GrayScale }
    public enum ExportPaperFormat { Default, ISO_A0, ISO_A1, ISO_A2, ISO_A3, ISO_A4 }
}
