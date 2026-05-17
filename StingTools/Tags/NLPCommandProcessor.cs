// NLPCommandProcessor.cs — Natural Language Processing for STINGTOOLS
// Covers gaps: NLP command processing, intent recognition, BIM knowledge base
// Maps natural language queries to STING command classes using pattern matching
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;

namespace StingTools.Tags
{
    // ════════════════════════════════════════════════════════════════
    //  NLP ENGINE — Intent recognition and command mapping
    // ════════════════════════════════════════════════════════════════
    internal static class NLPEngine
    {
        /// <summary>Represents a recognized intent with confidence score.</summary>
        internal class IntentResult
        {
            public string Intent { get; set; }
            public string CommandTag { get; set; }
            public double Confidence { get; set; }
            public string Description { get; set; }
            public string[] Alternatives { get; set; }
        }

        // Intent patterns: regex pattern → (commandTag, intent, description)
        internal static readonly List<(string Pattern, string CommandTag, string Intent, string Description)> IntentPatterns = new()
        {
            // Phase 165 — Issue #9. Mode-switch + tier-depth + System B intents.
            (@"\b(switch\s+to\s+handover|enable\s+(fm|handover)|handover\s+mode)\b",
                "SetPatternMode_Handover", "SetPatternMode", "Switch active T4-T10 payload to handover pack"),
            (@"\b(switch\s+to\s+(dc|design)|enable\s+dc|design.{0,5}construction\s+mode|coordination\s+mode)\b",
                "SetPatternMode_DC", "SetPatternMode", "Switch active T4-T10 payload to DC pack"),
            (@"\b(custom\s+mode|enable\s+custom)\b",
                "SetPatternMode_Custom", "SetPatternMode", "Switch active T4-T10 payload to custom pack"),
            (@"\b(set\s+depth\s+(\d+)|show\s+(\d+)\s+tiers|expand\s+to\s+tier\s+(\d+))\b",
                "SetParagraphDepthExt", "SetParagraphDepth", "Set paragraph depth (1-10) — slider value passed via ParaDepth extra"),
            (@"\b(show\s+commission|commissioning\s+data|t4)\b",
                "WriteSystemBTier_4", "WriteSystemBTier", "Write T4 commissioning data on selection"),
            (@"\b(show\s+cost|cost\s+data|t5)\b",
                "WriteSystemBTier_5", "WriteSystemBTier", "Write T5 cost data on selection"),
            (@"\b(show\s+carbon|carbon\s+data|embodied\s+carbon|t6)\b",
                "WriteSystemBTier_6", "WriteSystemBTier", "Write T6 carbon data on selection"),
            (@"\b(show\s+fab|fabrication\s+data|spool|t7)\b",
                "WriteSystemBTier_7", "WriteSystemBTier", "Write T7 fabrication / QC data on selection"),
            (@"\b(show\s+clash|clash\s+triage|t8)\b",
                "WriteSystemBTier_8", "WriteSystemBTier", "Write T8 clash triage data on selection"),
            (@"\b(as.?built|t9)\b",
                "WriteSystemBTier_9", "WriteSystemBTier", "Write T9 as-built data on selection"),
            (@"\b(show\s+(audit|compliance)|audit\s+trail|t10)\b",
                "WriteSystemBTier_10", "WriteSystemBTier", "Write T10 compliance / audit data on selection"),

            // Tagging
            (@"\b(auto.?tag|tag\s+elements?|tag\s+view)\b", "AutoTag", "AutoTag", "Tag elements in active view"),
            (@"\b(batch.?tag|tag\s+all|tag\s+project|tag\s+everything)\b", "BatchTag", "BatchTag", "Tag all elements in project"),
            (@"\b(tag.?and.?combine|one.?click.?tag|full.?tag)\b", "TagAndCombine", "TagAndCombine", "One-click tag and combine pipeline"),
            (@"\b(tag\s+new|incremental.?tag|untag)\b", "TagNewOnly", "TagNewOnly", "Tag only new/untagged elements"),
            (@"\b(verify\s+tags?|check\s+tags?\s+only)\b", "Validate", "ValidateTags", "Validate tag completeness"),
            (@"\b(combine|merge\s+param|write\s+containers?)\b", "CombineParameters", "CombineParameters", "Combine parameters into containers"),
            (@"\b(pre.?tag\s+audit|audit\s+tags?)\b", "PreTagAudit", "PreTagAudit", "Dry-run tag prediction audit"),
            (@"\b(duplicate.?tags?|find\s+dup|fix\s+dup)\b", "FixDuplicates", "FixDuplicates", "Find and fix duplicate tags"),
            (@"\b(build\s+tags?|rebuild\s+tags?|assemble)\b", "BuildTags", "BuildTags", "Rebuild tags from tokens"),
            (@"\b(completeness|dashboard|compliance\s+dash)\b", "CompletenessDashboard", "CompletenessDashboard", "Tag completeness dashboard"),

            // Token setting
            (@"\b(set\s+disc|discipline|set\s+discipline)\b", "SetDisc", "SetDiscipline", "Set discipline code"),
            (@"\b(set\s+loc|location|set\s+location|building)\b", "SetLoc", "SetLocation", "Set location code"),
            (@"\b(set\s+zone|zone)\b", "SetZone", "SetZone", "Set zone code"),
            (@"\b(set\s+status|status|existing|new|demolished)\b", "SetStatus", "SetStatus", "Set status token"),
            (@"\b(assign\s+num|number|sequence|seq)\b", "AssignNumbers", "AssignNumbers", "Assign sequence numbers"),

            // Selection
            (@"\b(select\s+all|select\s+taggable|all\s+elements?)\b", "SelectAllTaggable", "SelectAll", "Select all taggable elements"),
            (@"\b(select\s+untagged|no\s+tag|without\s+tag)\b", "SelectUntagged", "SelectUntagged", "Select untagged elements"),
            (@"\b(select\s+tagged|with\s+tag|has\s+tag)\b", "SelectTagged", "SelectTagged", "Select tagged elements"),
            (@"\b(select\s+light|lighting)\b", "SelectLighting", "SelectLighting", "Select lighting fixtures"),
            (@"\b(select\s+elec|electrical)\b", "SelectElectrical", "SelectElectrical", "Select electrical equipment"),
            (@"\b(select\s+mech|mechanical)\b", "SelectMechanical", "SelectMechanical", "Select mechanical equipment"),
            (@"\b(select\s+plumb|plumbing)\b", "SelectPlumbing", "SelectPlumbing", "Select plumbing fixtures"),
            (@"\b(select\s+door)\b", "SelectDoors", "SelectDoors", "Select doors"),
            (@"\b(select\s+window)\b", "SelectWindows", "SelectWindows", "Select windows"),

            // Color
            (@"\b(color|colour|colorize|colorise|highlight)\s+(by|param|element)\b", "ColorByParam", "ColorByParameter", "Color elements by parameter value"),
            (@"\b(clear\s+color|clear\s+colour|remove\s+override|reset\s+color)\b", "ClearColorOverrides", "ClearColors", "Clear color overrides"),

            // Documents
            (@"\b(sheet\s+organ|organ.*sheet|group\s+sheet)\b", "SheetOrganizer", "SheetOrganizer", "Organize sheets by discipline"),
            (@"\b(view\s+organ|organ.*view)\b", "ViewOrganizer", "ViewOrganizer", "Organize views by type"),
            (@"\b(sheet\s+index|create\s+index)\b", "SheetIndex", "SheetIndex", "Create sheet index schedule"),
            (@"\b(transmittal|document\s+transmit)\b", "Transmittal", "Transmittal", "Generate transmittal report"),
            (@"\b(align\s+viewport|viewport\s+align)\b", "AlignViewports", "AlignViewports", "Align viewports on sheet"),
            (@"\b(delete\s+unused|remove\s+unused|purge\s+view)\b", "DeleteUnusedViews", "DeleteUnused", "Delete unused views"),

            // Template & Setup
            (@"\b(master\s+setup|full\s+setup|setup\s+project)\b", "MasterSetup", "MasterSetup", "One-click full project setup"),
            (@"\b(project\s+wizard|setup\s+wizard|wizard)\b", "ProjectSetupWizard", "Wizard", "Launch project setup wizard"),
            (@"\b(load\s+param|create\s+param|bind\s+param)\b", "LoadParams", "LoadParams", "Load shared parameters"),
            (@"\b(create\s+material|material)\b", "CreateBLEMaterials", "CreateMaterials", "Create materials from CSV"),
            (@"\b(create\s+sched|schedule|batch\s+sched)\b", "BatchSchedules", "CreateSchedules", "Batch create schedules"),
            (@"\b(view\s+template|create\s+template)\b", "ViewTemplates", "ViewTemplates", "Create view templates"),
            (@"\b(create\s+filter|filter)\b", "CreateFilters", "CreateFilters", "Create view filters"),
            (@"\b(create\s+workset|workset)\b", "CreateWorksets", "CreateWorksets", "Create worksets"),
            (@"\b(auto.?populate|full.?populate|populate)\b", "FullAutoPopulate", "AutoPopulate", "Full auto-populate pipeline"),

            // Standards
            (@"\b(iso\s*19650|iso\s+compliance|iso\s+check)\b", "Iso19650DeepCompliance", "ISO19650", "ISO 19650 deep compliance check"),
            (@"\b(cibse|velocity\s+check|duct\s+velocity|pipe\s+velocity)\b", "CibseVelocityCheck", "CIBSE", "CIBSE velocity compliance"),
            (@"\b(bs\s*7671|electrical\s+compliance|wiring\s+reg)\b", "Bs7671Compliance", "BS7671", "BS 7671 electrical compliance"),
            (@"\b(uniclass|classif)\b", "UniclassClassify", "Uniclass", "Uniclass 2015 classification"),
            (@"\b(bs\s*8300|accessib|disable|wheelchair)\b", "Bs8300Accessibility", "BS8300", "BS 8300 accessibility check"),
            (@"\b(part\s*l|energy\s+compliance|u.?value|thermal)\b", "PartLCompliance", "PartL", "Part L energy compliance"),
            (@"\b(standard.*dash|compliance.*dash|all\s+standard)\b", "StandardsDashboard", "StandardsDashboard", "Standards compliance dashboard"),

            // Operations
            (@"\b(workflow|preset|chain|pipeline)\b", "WorkflowPresets", "Workflow", "Execute workflow preset"),
            (@"\b(pdf\s+export|export\s+pdf|print\s+pdf)\b", "PdfExport", "PDFExport", "Export sheets to PDF"),
            (@"\b(ifc\s+export|export\s+ifc)\b", "IfcExport", "IFCExport", "Export to IFC"),
            (@"\b(cobie|facility\s+data)\b", "CobieExport", "COBie", "Export COBie data"),
            (@"\b(quantity|takeoff|take.?off|bill\s+of|boq)\b", "QuantityTakeoff", "QuantityTakeoff", "Quantity takeoff"),
            (@"\b(clash|collision|intersect|conflict)\b", "ClashDetection", "ClashDetection", "Clash detection"),
            (@"\b(model\s+health|health\s+check|model\s+audit)\b", "ModelHealth", "ModelHealth", "Model health check"),
            (@"\b(purge|clean\s+up|remove\s+unused)\b", "PurgeUnused", "Purge", "Purge unused elements"),
            (@"\b(batch\s+print|print\s+all|print\s+sheets?)\b", "BatchPrint", "BatchPrint", "Batch print sheets"),

            // IoT / Maintenance
            (@"\b(condition|asset\s+condition|assess)\b", "AssetCondition", "AssetCondition", "Asset condition assessment"),
            (@"\b(maintenance|maint\s+sched|preventive)\b", "MaintenanceSchedule", "Maintenance", "Maintenance schedule"),
            (@"\b(digital\s+twin|twin\s+export|iot\s+export)\b", "DigitalTwinExport", "DigitalTwin", "Digital twin data export"),
            (@"\b(energy|heating|cooling|load\s+calc)\b", "EnergyAnalysis", "Energy", "Energy analysis summary"),
            (@"\b(commission|cx|handoff\s+check)\b", "CommissioningChecklist", "Commissioning", "Commissioning checklist"),
            (@"\b(space|room\s+analysis|area\s+analysis)\b", "SpaceManagement", "SpaceManagement", "Space management analysis"),
            (@"\b(lifecycle|whole\s+life|wlc|cost\s+estim)\b", "LifecycleCost", "LifecycleCost", "Lifecycle cost estimate"),
            (@"\b(warranty|guarantee)\b", "WarrantyTracker", "Warranty", "Warranty tracker"),
            (@"\b(handover|o&m|handoff|delivery)\b", "HandoverPackage", "Handover", "Handover package generator"),
            (@"\b(sensor|bms|iot|monitoring\s+point)\b", "SensorPointMapper", "Sensors", "Sensor point mapper"),

            // DWG Import
            (@"\b(import\s+dwg|dwg\s+import|cad\s+import|autocad)\b", "ImportDWG", "ImportDWG", "Import DWG file"),
            (@"\b(preview\s+layer|dwg\s+layer|cad\s+layer)\b", "PreviewDWGLayers", "PreviewLayers", "Preview DWG layers"),
            (@"\b(link\s+dwg|link\s+cad)\b", "LinkDWG", "LinkDWG", "Link DWG file"),
            (@"\b(dwg.?to.?bim|convert\s+dwg|conversion\s+plan)\b", "DWGConversionPlan", "DWGToBIM", "DWG to BIM conversion plan"),

            // Model Creation
            (@"\b(create\s+wall|draw\s+wall|place\s+wall)\b", "CreateWalls", "CreateWalls", "Create walls"),
            (@"\b(create\s+floor|place\s+floor)\b", "CreateFloors", "CreateFloors", "Create floors from rooms"),
            (@"\b(place\s+door|insert\s+door)\b", "PlaceDoors", "PlaceDoors", "Place doors in walls"),
            (@"\b(place\s+window|insert\s+window)\b", "PlaceWindows", "PlaceWindows", "Place windows in walls"),
            (@"\b(create\s+room|auto\s+room)\b", "AutoCreateRooms", "CreateRooms", "Auto-create rooms"),
            (@"\b(create\s+grid|place\s+grid)\b", "CreateGrids", "CreateGrids", "Create grids"),
            (@"\b(create\s+level|add\s+level)\b", "CreateLevels", "CreateLevels", "Create levels"),

            // MEP
            (@"\b(place\s+mep|mep\s+equip|place\s+equip)\b", "PlaceMEPEquipment", "PlaceMEP", "Place MEP equipment"),
            (@"\b(mep\s+audit|system\s+audit|mep\s+check)\b", "MEPSystemAudit", "MEPAudit", "MEP system audit"),
            (@"\b(mep\s+siz|pipe\s+siz|duct\s+siz)\b", "MEPSizingCheck", "MEPSizing", "MEP sizing check"),
            (@"\b(connection\s+audit|disconnect|unconnect)\b", "MEPConnectionAudit", "MEPConnections", "MEP connection audit"),

            // Smart Tags
            (@"\b(smart\s+tag|auto\s+place\s+tag|annotation\s+tag)\b", "SmartPlaceTags", "SmartTags", "Smart tag placement"),
            (@"\b(arrange\s+tag|align\s+tag|tidy\s+tag)\b", "ArrangeTags", "ArrangeTags", "Arrange tags in view"),
            (@"\b(remove\s+annotation|delete\s+tag\s+annot)\b", "RemoveAnnotationTags", "RemoveAnnotations", "Remove annotation tags"),

            // Leaders
            (@"\b(toggle\s+leader|leader\s+on|leader\s+off)\b", "ToggleLeaders", "ToggleLeaders", "Toggle tag leaders"),
            (@"\b(add\s+leader)\b", "AddLeaders", "AddLeaders", "Add leaders to tags"),
            (@"\b(remove\s+leader)\b", "RemoveLeaders", "RemoveLeaders", "Remove leaders from tags"),

            // Legends
            (@"\b(create\s+legend|legend|color\s+legend)\b", "CreateMasterLegend", "Legend", "Create legend"),

            // Export
            (@"\b(export\s+csv|csv\s+export|export\s+data)\b", "ExportCSV", "ExportCSV", "Export to CSV"),
            (@"\b(tag\s+register|asset\s+register|register\s+export)\b", "TagRegisterExport", "TagRegister", "Export tag register"),

            // Stale & Anomaly (Phase 26-28)
            (@"\b(retag\s*stale|fix\s*stale|stale\s*elements|re-tag\s*stale)\b", "RetagStale", "RetagStale", "Retag stale elements"),
            (@"\b(anomaly\s*fix|auto\s*fix\s*anomal|fix\s*anomal)\b", "AnomalyAutoFix", "AnomalyAutoFix", "Auto-fix tag anomalies"),
            (@"\b(seq\s*scheme|sequence\s*scheme|set\s*seq)\b", "SetSeqScheme", "SetSeqScheme", "Set sequence numbering scheme"),
            (@"\b(map\s*sheet|sheet\s*map|native\s*sheet)\b", "MapSheets", "MapSheets", "Map native sheet parameters"),
            (@"\b(workflow\s*trend|trend\s*report|run\s*history)\b", "WorkflowTrend", "WorkflowTrend", "View workflow trend analysis"),

            // Healthcare Pack H-1..H-30
            (@"\b(healthcare\s*audit|run\s*all\s*healthcare|hospital\s*audit)\b",
                "Healthcare_RunAllValidators", "HealthcareValidate", "Run all healthcare validators"),
            (@"\b(pressure\s*(regime|cascade|audit)|aiir\s*check|negative\s*pressure)\b",
                "Healthcare_PressureAudit", "HealthcareValidate", "Pressure regime / AIIR cascade audit (HTM 03-01)"),
            (@"\b(med(ical)?\s*gas\s*(audit|verify|check)|mgps|mgs\s*audit)\b",
                "Healthcare_MgasAudit", "HealthcareValidate", "Medical gas pipeline system audit (HTM 02-01 / NFPA 99)"),
            (@"\b(mgps\s*verif|nfpa\s*99\s*verif|mgs\s*verif)\b",
                "Healthcare_MgasVerify", "HealthcareValidate", "NFPA 99 §5.1.12 MGPS verification walkthrough"),
            (@"\b(water\s*safety|legionella\s*audit|tmv\s*check|htm\s*04)\b",
                "Healthcare_WaterSafety", "HealthcareValidate", "Water safety audit (HTM 04-01)"),
            (@"\b(ees\s*branch|essential\s*power|nfpa\s*99\s*ees)\b",
                "Healthcare_EesBranch", "HealthcareValidate", "Essential Electrical System branch audit (NFPA 99)"),
            (@"\b(rad(iation)?\s*shield|ncrp\s*147|lead\s*lin(ed|ing))\b",
                "Healthcare_RadShield", "HealthcareValidate", "Radiation shielding audit (NCRP 147)"),
            (@"\b(mri\s*(zone|audit|safety)|faraday\s*cage)\b",
                "Healthcare_MriZoneAudit", "HealthcareValidate", "MRI suite zoning audit (IEC 60601-2-33)"),
            (@"\b(rds|room\s*data\s*sheet)(\s*audit|\s*completeness)?\b",
                "Healthcare_RdsCompleteness", "HealthcareValidate", "Room Data Sheet completeness audit"),
            (@"\b(issue\s*rds|render\s*rds|build\s*room\s*data)\b",
                "Healthcare_BatchRDS", "HealthcareIssue", "Render Room Data Sheets for every clinical room"),
            (@"\b(adjacency|hbn\s*adjacency|clean\s*dirty\s*flow)\b",
                "Healthcare_AdjacencyAudit", "HealthcareValidate", "Adjacency + clean/dirty flow audit (HBN-derived)"),
            (@"\b(anti.?ligature|ligature\s*audit|hbn\s*03)\b",
                "Healthcare_AntiLigature", "HealthcareValidate", "Anti-ligature audit (HBN 03-01 / FGI Pt 2)"),
            (@"\b(hybrid\s*or|cath\s*lab|interventional\s*radio)\b",
                "Healthcare_HybridOr", "HealthcareValidate", "Hybrid OR / Cath / IR area + clearance audit"),
            (@"\b(usp\s*(797|800)|pharmacy\s*cleanroom)\b",
                "Healthcare_PharmacyUsp", "HealthcareValidate", "USP <797> / <800> pharmacy cleanroom audit"),
            (@"\b(mortuary|hbn\s*16|post.?mortem)\b",
                "Healthcare_Mortuary", "HealthcareValidate", "Mortuary capacity audit (HBN 16)"),
            (@"\b(hsdu|sterile\s*service|decon\s*flow|htm\s*01.?06)\b",
                "Healthcare_Hsdu", "HealthcareValidate", "HSDU compartment audit (HBN 13)"),
            (@"\b(dialysis|ro\s*loop|hbn\s*07.?02)\b",
                "Healthcare_Dialysis", "HealthcareValidate", "Dialysis RO-loop audit (HBN 07-02)"),
            (@"\b(rtls|real.?time\s*location|ble\s*beacon|uwb\s*anchor)\b",
                "Healthcare_RtlsCoverage", "HealthcareValidate", "RTLS coverage + RF dead-zone audit"),
            (@"\b(clinical\s*waste|cytotoxic\s*waste|htm\s*07.?01)\b",
                "Healthcare_WasteFlow", "HealthcareValidate", "Clinical waste flow audit (HTM 07-01)"),
            (@"\b(iot\s*(devices|registry|inventory)|bms\s*registry)\b",
                "Healthcare_IoTRegistry", "HealthcareValidate", "IoT device registry inspector"),

            // Panel Schedule commands (Phase 176 — BatchPanelSchedulesCommand)
            (@"\b(batch\s*panel\s*schedule|create\s*panel\s*schedule|auto\s*panel)\b",
                "Panel_BatchSchedules", "PanelSchedules", "Batch-create panel schedules from rules (Phase 176)"),
            (@"\b(panel\s*audit|panel\s*drift|schedule\s*drift)\b",
                "Panel_Audit", "PanelSchedules", "Audit panels for missing schedules and template drift"),
            (@"\b(fill\s*(spare|spares)\s*(circuit|ways?|slot)|panel\s*spare)\b",
                "Panel_FillSpares", "PanelSchedules", "Fill empty circuit slots with spares"),
            (@"\b(panel\s*(schedule\s*)?export|export\s*panel\s*(to\s*)?excel)\b",
                "Panel_ExportToExcel", "PanelSchedules", "Export panel schedules to Excel"),
            (@"\b(panel\s*(schedule\s*)?import|import\s*panel\s*(from\s*)?excel)\b",
                "Panel_ImportFromExcel", "PanelSchedules", "Import panel schedules from Excel"),

            // Routing / Auto-Drop (v4 MVP — AutoDropCommand, GenerateLayoutCommand)
            (@"\b(auto\s*drop|auto.?route|mep\s*drop)\b",
                "Routing_AutoDrop", "Routing", "Auto-drop MEP services off main to branches (v4 MVP)"),
            (@"\b(generate\s*layout|routing\s*layout|mep\s*layout)\b",
                "Routing_GenerateLayout", "Routing", "Generate MEP routing layout (v4 MVP)"),
            (@"\b(validate\s*fill|fill\s*check|fill\s*ratio\s*check)\b",
                "Routing_ValidateFills", "Routing", "Validate conduit and cable tray fill ratios (v4 MVP)"),

            // Penetrations & Sleeves (PenetrationsDetectAndPlaceCommand / SleeveEngine)
            (@"\b(detect\s*penetration|penetration\s*detect|mep\s*penetration|auto\s*penetrat)\b",
                "Routing_DetectPenetrations", "Penetrations", "Detect and place MEP penetrations through structure"),
            (@"\b(auto\s*sleeve|place\s*sleeve|sleeve\s*place|sleeve\s*size)\b",
                "Routing_AutoSleeve", "Penetrations", "Auto-size and place fire-rated MEP sleeves"),
            (@"\b(sleeve\s*audit|sleeve\s*check|sleeve\s*rating|slv\s*audit)\b",
                "Routing_SleeveAudit", "Penetrations", "Audit MEP sleeves for missing fire rating / seal type"),

            // Fabrication (v4 MVP — GenerateFabPackageCommand)
            (@"\b(generate\s*fab|fab\s*package|fabrication\s*package)\b",
                "Fabrication_GeneratePackage", "Fabrication", "Generate fabrication package (spool drawings + cut list)"),
            (@"\b(export\s*cut\s*list|cut\s*list|isometric\s*export|export\s*iso)\b",
                "Fabrication_ExportCutList", "Fabrication", "Export cut list / isometric drawings from fab package"),
            (@"\b(weld\s*map|export\s*weld|iso\s*symbol)\b",
                "Fabrication_ExportWeldMap", "Fabrication", "Export weld map and ISO 6412 symbols"),
            (@"\b(spool\s*(number|nr|check|audit)|check\s*spool)\b",
                "Fabrication_SpoolAudit", "Fabrication", "Check elements for missing spool numbers (v4 MVP)"),

            // Placement (v4 MVP — PlaceFixturesCommand, LightingGridCommand)
            (@"\b(place\s*fixture|fixture\s*place|auto\s*fix\s*place)\b",
                "Placement_PlaceFixtures", "Placement", "Rule-based fixture placement (v4 MVP)"),
            (@"\b(lighting\s*grid|place\s*light\s*grid|auto\s*light)\b",
                "Placement_LightingGrid", "Placement", "Lighting grid auto-placement (v4 MVP)"),

            // Drawing Types / Scope Boxes (DrawingTemplateManager)
            (@"\b(sync\s*style|drawing\s*style\s*sync|drift\s*repair|repair\s*drift)\b",
                "DrawingTypes_SyncStyles", "DrawingTypes", "Sync drawing type styles and repair drift"),
            (@"\b(from\s*scope\s*box|scope\s*box\s*view|generate\s*from\s*scope)\b",
                "DrawingTypes_FromScopeBoxes", "DrawingTypes", "Generate views from STING scope box naming convention"),
            (@"\b(browser\s*organ|view\s*browser\s*org|drawing\s*browser)\b",
                "DrawingTypes_BrowserOrganize", "DrawingTypes", "Create browser organizer by drawing type"),
            (@"\b(inspect\s*drawing\s*type|drawing\s*type\s*inspect|list\s*drawing\s*type)\b",
                "DrawingTypes_Inspect", "DrawingTypes", "Inspect all drawing types and routing rules"),
            (@"\b(reload\s*drawing\s*type|refresh\s*drawing|drawing\s*type\s*reload)\b",
                "DrawingTypes_Reload", "DrawingTypes", "Reload drawing type registry from disk"),

            // Lightning Protection (LPS — Phase 176)
            (@"\b(lightning\s*protect|lps\s*audit|lps\s*check|earth\s*resist(ance)?)\b",
                "LPS_Audit", "LPS", "Audit lightning protection system compliance (BS EN 62305)"),
            (@"\b(down\s*conduct|air\s*terminal\s*audit|lps\s*class)\b",
                "LPS_Conductors", "LPS", "Check LPS down conductor count and cross-section"),

            // Symbol Standards (Phase 175 — multi-standard model family symbols)
            (@"\b(author\s*symbol|wire\s*symbol|inject\s*symbol|symbol\s*author|embed\s*symbol)\b",
                "Symbols_AuthorSymbols", "SymbolAuthoring", "Author IEC/ANSI/BS/NFPA/CIBSE symbol geometry into model family (.rfa) files"),
            (@"\b(switch\s*project\s*standard|project\s*standard|switch\s*symbol\s*standard|change\s*symbol\s*standard)\b",
                "Symbols_SwitchProject", "SymbolStandard", "Switch project-wide symbol standard (IEC/ANSI/BS/NFPA/CIBSE)"),
            (@"\b(switch\s*view\s*standard|view\s*standard\s*switch|set\s*view\s*standard)\b",
                "Symbols_SwitchView", "SymbolStandard", "Set symbol standard for the active view only"),
            (@"\b(set\s*element\s*standard|element\s*symbol\s*standard|instance\s*standard|per.?instance\s*standard)\b",
                "Symbols_SetElementStandard", "SymbolStandard", "Set STING_SYMBOL_STD on selected model family instances"),
            (@"\b(symbol\s*audit|audit\s*symbol|symbol\s*coverage|coverage\s*audit|symbol\s*drift)\b",
                "Symbols_Audit", "SymbolAudit", "Audit symbol standard coverage and drift across project"),
            (@"\b(place\s*symbol|symbol\s*overlay|overlay\s*symbol|place\s*overlay)\b",
                "Symbols_PlaceView", "SymbolPlacement", "Place symbol overlays for elements in the active view"),

            // GAP-NLP-01: Validation / ISO compliance patterns (previously unmapped)
            (@"\b(validate\s+tags?|check\s+iso|iso\s+(audit|check|valid)|run\s+validation|tag\s+valid)\b",
                "Validate", "ValidateTags", "Validate all tags against ISO 19650 rules"),
            (@"\b(iso\s*19650\s*(deep|full|strict)|full\s+compliance\s+check|strict\s+(tag|iso)\s+check)\b",
                "Validate", "ValidateTags", "Deep ISO 19650 compliance validation"),
            (@"\b(pre\s+tag\s+audit|dry\s+run\s+tag|predict\s+tag|tag\s+predict)\b",
                "PreTagAudit", "PreTagAudit", "Dry-run tag prediction audit before tagging"),

            // GAP-NLP-01: Token-level commands (missing from original set)
            (@"\b(set\s+level|set\s+lvl|assign\s+level)\b",
                "AssignNumbers", "SetLevel", "Set level (LVL) token on selection"),
            (@"\b(set\s+sys(tem)?|assign\s+sys(tem)?|system\s+code)\b",
                "SetSeqScheme", "SetSystem", "Set system (SYS) token on selection"),
            (@"\b(set\s+func(tion)?|assign\s+func(tion)?|function\s+code)\b",
                "BuildTags", "SetFunction", "Set function (FUNC) token on selection"),
            (@"\b(set\s+prod(uct)?|assign\s+prod(uct)?|product\s+code)\b",
                "BuildTags", "SetProduct", "Set product (PROD) token on selection"),

            // GAP-NLP-01: Placement resolution patterns (missing from original set)
            (@"\b(fix\s+overlap|resolve\s+collision|fix\s+collision|untangle\s+tag)\b",
                "ArrangeTags", "ArrangeTags", "Auto-arrange tags to resolve overlaps"),
            (@"\b(reset\s+(tag\s+)?position|revert\s+placement|move\s+tag\s+back)\b",
                "ResetTagPositions", "ResetPositions", "Reset tag positions to element centres"),
            (@"\b(lock\s+(tag\s+)?position|freeze\s+(tag|placement)|pin\s+tag)\b",
                "PinTags", "PinTags", "Lock tag positions to prevent accidental movement"),
            (@"\b(align\s+(tag\s+)?horizon|horizontal\s+align\s+tag)\b",
                "AlignTagsH", "AlignTagsH", "Align tags horizontally across the view"),
            (@"\b(align\s+(tag\s+)?vert(ical)?|vertical\s+align\s+tag)\b",
                "AlignTagsV", "AlignTagsV", "Align tags vertically across the view"),
            (@"\b(stack\s+tag|stack\s+annot|column\s+tag)\b",
                "ArrangeTags", "StackTags", "Stack tags in a vertical column layout"),
            (@"\b(learn\s+placement|learn\s+tag|capture\s+placement)\b",
                "LearnTagPlacement", "LearnPlacement", "Learn tag placement rules from current view"),
            (@"\b(apply\s+(tag\s+)?template|placement\s+template)\b",
                "ApplyTagTemplate", "ApplyTemplate", "Apply saved tag placement template to view"),
            (@"\b(batch\s+place\s+tag|multi.?view\s+tag\s+place|all\s+view\s+tag)\b",
                "BatchPlaceTags", "BatchPlace", "Place annotation tags across multiple views"),

            // GAP-NLP-01: 3D tagging
            (@"\b(tag\s+3d|3d\s+tag|tag\s+in\s+3d|perspective\s+tag)\b",
                "Tag3D", "Tag3D", "Tag elements in 3D views with spatial auto-detect"),

            // GAP-NLP-01: Repair / housekeeping
            (@"\b(repair\s+(dup|duplicate)\s+(seq|num)|fix\s+seq\s+dup)\b",
                "RepairDuplicateSeq", "RepairDuplicateSeq", "Repair duplicate SEQ numbers using spatial proximity"),
            (@"\b(decluster\s+tag|uncluster\s+tag|break\s+cluster)\b",
                "DeclusterTags", "DeclusterTags", "Break up clustered tags that share a position"),
        };

        // BIM Knowledge Base entries
        internal static readonly Dictionary<string, string> BimKnowledge = new()
        {
            ["ISO 19650"] = "International standard for managing information over the whole life cycle of a built asset using BIM. Parts 1-3 cover concepts, delivery phase, and operational phase.",
            ["LOD"] = "Level of Development / Level of Detail. LOD 100=Conceptual, 200=Approximate, 300=Precise, 350=Construction, 400=Fabrication, 500=As-built.",
            ["COBie"] = "Construction Operations Building Information Exchange. A structured data format for delivering facility management data at handover.",
            ["IFC"] = "Industry Foundation Classes. An open, neutral data format (ISO 16739) for exchanging BIM data between software platforms.",
            ["CDE"] = "Common Data Environment. A single source of information for project data, used to collect, manage and disseminate information (ISO 19650).",
            ["Uniclass 2015"] = "UK unified classification system for the construction industry. Tables: Ac (Activities), Co (Complexes), En (Entities), Ss (Systems), Pr (Products).",
            ["CIBSE"] = "Chartered Institution of Building Services Engineers. Publishes guides for building services design: Guide A (Environmental Design), Guide B (Heating/Ventilation/AC).",
            ["BS 7671"] = "IET Wiring Regulations. National standard for electrical installations in the UK. Covers circuit design, cable sizing, earthing, and protection.",
            ["BS 8300"] = "Design of an accessible and inclusive built environment. Covers wheelchair access, visual impairment, hearing impairment provisions.",
            ["Part L"] = "Building Regulations Part L: Conservation of fuel and power. Sets maximum U-values and air permeability targets for building fabric.",
            ["PAS 1192"] = "Predecessor to ISO 19650. PAS 1192-2 (design/construction) and PAS 1192-3 (operations) now superseded by BS EN ISO 19650.",
            ["NBS"] = "National Building Specification. Provides structured specification writing tools and Uniclass classification for the UK construction industry.",
            ["BIM Execution Plan"] = "Document defining how BIM will be implemented on a project. Covers roles, responsibilities, standards, software, information exchanges, and deliverables.",
            ["Digital Twin"] = "A digital representation of a physical asset that is kept synchronized through real-time data feeds (sensors, BMS, IoT devices).",
            ["Asset Tag"] = "STING uses 8-segment ISO 19650 tags: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ. Each segment encodes discipline, spatial, system, and identity data.",
        };

        /// <summary>Process natural language input and return matching intents.</summary>
        internal static List<IntentResult> ProcessQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<IntentResult>();

            var results = new List<IntentResult>();
            string normalised = query.ToLower().Trim();

            foreach (var (pattern, cmdTag, intent, desc) in IntentPatterns)
            {
                var match = Regex.Match(normalised, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    // Calculate confidence based on match quality
                    double confidence = 0.5 + (match.Length / (double)normalised.Length) * 0.5;
                    confidence = Math.Min(confidence, 0.99);

                    results.Add(new IntentResult
                    {
                        Intent = intent,
                        CommandTag = cmdTag,
                        Confidence = confidence,
                        Description = desc,
                        Alternatives = Array.Empty<string>()
                    });
                }
            }

            // Sort by confidence descending
            results = results.OrderByDescending(r => r.Confidence).ToList();

            // Add alternatives to top result
            if (results.Count > 1)
            {
                results[0].Alternatives = results.Skip(1).Take(3).Select(r => $"{r.Intent} ({r.Confidence:P0})").ToArray();
            }

            return results;
        }

        /// <summary>Search BIM knowledge base.</summary>
        internal static List<(string Term, string Definition)> SearchKnowledge(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<(string, string)>();

            string lower = query.ToLower();
            return BimKnowledge
                .Where(kvp => kvp.Key.ToLower().Contains(lower) ||
                              kvp.Value.ToLower().Contains(lower) ||
                              lower.Contains(kvp.Key.ToLower()))
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        /// <summary>Get command suggestions for partial input (autocomplete).</summary>
        internal static List<(string CommandTag, string Description)> GetSuggestions(string partial)
        {
            if (string.IsNullOrWhiteSpace(partial)) return new List<(string, string)>();

            string lower = partial.ToLower();
            return IntentPatterns
                .Where(p => p.Intent.ToLower().Contains(lower) ||
                           p.Description.ToLower().Contains(lower) ||
                           p.CommandTag.ToLower().Contains(lower))
                .Select(p => (p.CommandTag, p.Description))
                .Distinct()
                .Take(10)
                .ToList();
        }

        // Tags that StingCommandHandler dispatches directly (not via WorkflowEngine.ResolveCommand).
        // These bypass WorkflowEngine so ResolveCommandPublic returns null for them — that's correct.
        // Keep this list in sync with the direct-dispatch cases in StingCommandHandler.Execute().
        private static readonly HashSet<string> _directDispatchTags = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            // Smart placement
            "SmartPlaceTags", "ArrangeTags", "RemoveAnnotationTags", "BatchPlaceTags",
            "LearnTagPlacement", "ApplyTagTemplate", "TagOverlapAnalysis", "BatchTagTextSize",
            "SetTagCategoryLineWeight", "AlignTagBands", "SwitchTagPosition", "ExportTagPositions",
            "BatchPlaceLinkedTags", "ExportLinkedManifest", "AdjustElbows", "SetArrowheadStyle",
            // Leader / organise
            "AlignTagsH", "AlignTagsV", "StackTags", "PinTags",
            "ToggleLeaders","AddLeaders","RemoveLeaders","AlignTags","ResetTagPositions",
            "ToggleOrientation","SnapLeaderElbows","AutoAlignLeaderText",
            "FlipTags","AlignTagText","PinUnpin","NudgeTags","AttachLeader","SelectLeaderTags",
            // Tag style
            "ApplyTagStyle","ApplyColorScheme","ClearColorScheme","SetParagraphDepthExt",
            "TagStyleReport","SwitchTagStyleByDisc","BatchApplyColorScheme","ColorByVariable",
            "SetBoxColor","SetViewTagStyle",
            // Mode / tier switch patterns (dispatched inline by StingCommandHandler)
            "SetPatternMode_Handover","SetPatternMode_DC","SetPatternMode_Custom",
            "WriteSystemBTier_4","WriteSystemBTier_5","WriteSystemBTier_6",
            "WriteSystemBTier_7","WriteSystemBTier_8","WriteSystemBTier_9","WriteSystemBTier_10",
            // Misc direct-dispatch
            "Validate","FixDuplicates","CompletenessDashboard",
            "ColorByParameter","ClearColorOverrides","SaveColorPreset","LoadColorPreset","CreateFilters",
        };

        /// <summary>
        /// Validates all IntentPatterns at startup — logs any commandTag that resolves via
        /// neither WorkflowEngine nor the known direct-dispatch set.  Call once from OnStartup.
        /// </summary>
        internal static void ValidateIntentPatterns()
        {
            var unresolved = new System.Text.StringBuilder();
            int unresolvedCount = 0;

            var distinctTags = IntentPatterns
                .Select(p => p.CommandTag)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t)
                .ToList();

            foreach (string tag in distinctTags)
            {
                // Direct-dispatch tags are known-valid — skip
                if (_directDispatchTags.Contains(tag)) continue;

                // WorkflowEngine-routed tags — attempt resolution
                try
                {
                    var cmd = WorkflowEngine.ResolveCommandPublic(tag);
                    if (cmd != null) continue; // resolved OK
                }
                catch { /* ignore instantiation errors */ }

                // Tag not resolved by either path
                unresolved.Append("  ").AppendLine(tag);
                unresolvedCount++;
            }

            if (unresolvedCount == 0)
                StingLog.Info($"NLPEngine: all {distinctTags.Count} distinct commandTags validated OK");
            else
                StingLog.Warn($"NLPEngine: {unresolvedCount} commandTag(s) not resolvable:\n{unresolved}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 1: NLP Command Processor
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class NLPCommandProcessorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // FIX-7.3: Functional NLP command processor with browse, execute, and knowledge base
                var ctx = ParameterHelpers.GetContext(commandData);

                var inputDlg = new TaskDialog("STING Natural Language Command");
                inputDlg.MainInstruction = "How would you like to find a command?";
                inputDlg.MainContent = "Browse all available commands, run a quick command,\nor explore the BIM Knowledge Base.";
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Browse all commands", "See all available commands and execute one");
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Quick commands", "Common workflows (tag, validate, export)");
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "BIM Knowledge Base", "Search BIM terminology and standards");
                inputDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var choice = inputDlg.Show();

                if (choice == TaskDialogResult.CommandLink3)
                {
                    // Knowledge base via StingListPicker
                    var kbItems = NLPEngine.BimKnowledge
                        .OrderBy(k => k.Key)
                        .Select(k => new StingListPicker.ListItem { Label = k.Key, Detail = k.Value })
                        .ToList();
                    var selected = StingListPicker.Show("BIM Knowledge Base",
                        "Search BIM terminology and standards:", kbItems);
                    if (selected != null && selected.Count > 0)
                    {
                        string term = selected[0].Label;
                        if (NLPEngine.BimKnowledge.TryGetValue(term, out string definition))
                            TaskDialog.Show($"BIM Knowledge: {term}", definition);
                    }
                    return Result.Succeeded;
                }

                if (choice == TaskDialogResult.CommandLink2)
                {
                    // Quick commands — curated subset
                    var quickItems = new List<StingListPicker.ListItem>
                    {
                        new StingListPicker.ListItem { Label = "TagAndCombine", Detail = "One-click tag and combine pipeline" },
                        new StingListPicker.ListItem { Label = "BatchTag", Detail = "Tag all elements in project" },
                        new StingListPicker.ListItem { Label = "Validate", Detail = "Validate tag completeness" },
                        new StingListPicker.ListItem { Label = "FullAutoPopulate", Detail = "Full auto-populate pipeline" },
                        new StingListPicker.ListItem { Label = "MasterSetup", Detail = "One-click full project setup" },
                        new StingListPicker.ListItem { Label = "CompletenessDash", Detail = "Tag completeness dashboard" },
                        new StingListPicker.ListItem { Label = "PreTagAudit", Detail = "Dry-run tag prediction audit" },
                        new StingListPicker.ListItem { Label = "FixDuplicates", Detail = "Find and fix duplicate tags" },
                        new StingListPicker.ListItem { Label = "CobieExport", Detail = "Export COBie data" },
                        new StingListPicker.ListItem { Label = "StandardsDashboard", Detail = "Standards compliance dashboard" },
                    };
                    var quickSel = StingListPicker.Show("Quick Commands",
                        "Select a command to execute:", quickItems);
                    if (quickSel != null && quickSel.Count > 0)
                    {
                        string cmdTag = quickSel[0].Label;
                        var cmd = WorkflowEngine.ResolveCommandPublic(cmdTag);
                        if (cmd != null)
                        {
                            string refMsg = "";
                            cmd.Execute(commandData, ref refMsg, elements);
                            StingLog.Info($"NLP quick command executed: {cmdTag}");
                        }
                        else
                            TaskDialog.Show("STING", $"Command '{cmdTag}' not found in workflow engine.");
                    }
                    return Result.Succeeded;
                }

                if (choice == TaskDialogResult.CommandLink1)
                {
                    // Browse all commands via StingListPicker
                    var allItems = NLPEngine.IntentPatterns
                        .Select(p => (p.CommandTag, p.Description))
                        .Distinct()
                        .OrderBy(c => c.Description)
                        .Select(c => new StingListPicker.ListItem { Label = c.CommandTag, Detail = c.Description })
                        .ToList();
                    var browseSel = StingListPicker.Show("All STING Commands",
                        "Select a command to execute:", allItems);
                    if (browseSel != null && browseSel.Count > 0)
                    {
                        string cmdTag = browseSel[0].Label;
                        var cmd = WorkflowEngine.ResolveCommandPublic(cmdTag);
                        if (cmd != null)
                        {
                            string refMsg = "";
                            cmd.Execute(commandData, ref refMsg, elements);
                            StingLog.Info($"NLP browse command executed: {cmdTag}");
                        }
                        else
                            TaskDialog.Show("STING", $"Command '{cmdTag}' is not directly executable from here.\nUse the STING dockable panel instead.");
                    }
                    return Result.Succeeded;
                }

                StingLog.Info("NLP command processor accessed");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Error("NLP command failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 2: BIM Knowledge Base Search
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BimKnowledgeBaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ BIM KNOWLEDGE BASE ═══\n");
                report.AppendLine($"Entries: {NLPEngine.BimKnowledge.Count}\n");

                foreach (var (term, definition) in NLPEngine.BimKnowledge.OrderBy(k => k.Key))
                {
                    report.AppendLine($"── {term} ──");
                    report.AppendLine($"  {definition}\n");
                }

                TaskDialog.Show("BIM Knowledge Base", report.ToString());
                StingLog.Info("BIM knowledge base viewed");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BIM knowledge base failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  COMMAND 3: Command Suggestion Engine
    // ════════════════════════════════════════════════════════════════
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CommandSuggestionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var _ctx = ParameterHelpers.GetContext(commandData);
                if (_ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = _ctx.Doc;
                var report = new System.Text.StringBuilder();
                report.AppendLine("═══ SMART COMMAND SUGGESTIONS ═══\n");
                report.AppendLine("Based on current model state:\n");

                // Analyze model state and suggest commands
                var suggestions = new List<(string Priority, string Command, string Reason)>();

                // Check if parameters are loaded
                int stingParams = 0;
                var bindingMap = doc.ParameterBindings;
                var iter = bindingMap.ForwardIterator();
                while (iter.MoveNext())
                {
                    if (iter.Key?.Name?.StartsWith("ASS_") == true) stingParams++;
                }

                if (stingParams < 10)
                    suggestions.Add(("HIGH", "Load Parameters", "STING parameters not loaded — required before tagging"));

                // Check tagged vs untagged
                // PERF: Use ElementMulticategoryFilter instead of LINQ .Where() on entire model
                var catEnums = SharedParamGuids.AllCategoryEnums;
                var taggableColl = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                if (catEnums != null && catEnums.Length > 0)
                    taggableColl.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
                var taggable = taggableColl.ToList();

                int tagged = 0, untagged = 0;
                foreach (var el in taggable)
                {
                    string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag)) untagged++;
                    else tagged++;
                }

                if (untagged > 0 && stingParams >= 10)
                    suggestions.Add(("HIGH", "Tag & Combine", $"{untagged} untagged elements found"));

                if (tagged > 0)
                    suggestions.Add(("MEDIUM", "Validate Tags", $"Validate {tagged} existing tags for ISO 19650 compliance"));

                // Check for views without templates
                var viewsNoTemplate = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v.ViewTemplateId == ElementId.InvalidElementId)
                    .Count();

                if (viewsNoTemplate > 5)
                    suggestions.Add(("MEDIUM", "Auto-Assign Templates", $"{viewsNoTemplate} views without templates"));

                // Check warnings
                int warningCount = doc.GetWarnings().Count;
                if (warningCount > 50)
                    suggestions.Add(("MEDIUM", "Model Health Check", $"{warningCount} warnings in model"));

                // Always suggest
                suggestions.Add(("LOW", "Standards Dashboard", "Run full compliance check"));
                suggestions.Add(("LOW", "Quantity Takeoff", "Generate quantity report"));

                foreach (var (priority, command, reason) in suggestions.OrderBy(s => s.Priority))
                    report.AppendLine($"  [{priority}] {command}: {reason}");

                report.AppendLine($"\nModel state: {taggable.Count} taggable elements ({tagged} tagged, {untagged} untagged)");
                report.AppendLine($"STING parameters: {stingParams} bound");

                TaskDialog.Show("Command Suggestions", report.ToString());
                StingLog.Info($"Suggestions: {suggestions.Count} recommendations");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Command suggestions failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
