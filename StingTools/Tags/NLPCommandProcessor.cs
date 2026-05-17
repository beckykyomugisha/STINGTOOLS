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

            // ── Element Modification / Bulk Operations ──────────────────────────
            (@"\b(bulk\s+(set|write|fill|param)|mass\s+set|set\s+all\s+param)\b",
                "BulkParamWrite", "BulkParamWrite", "Bulk set parameter values on multiple elements"),
            (@"\b(copy\s+(tag|param|value)\s+to|copy\s+to\s+all|spread\s+value)\b",
                "CopyTags", "CopyTags", "Copy tag values from one element to all selected"),
            (@"\b(swap\s+(tag|param|value)|exchange\s+(tag|param))\b",
                "SwapTags", "SwapTags", "Swap tag values between two selected elements"),
            (@"\b(select\s+by\s+disc(ipline)?|discipline\s+select|filter\s+disc)\b",
                "SelectByDiscipline", "SelectByDiscipline", "Select elements of a specific discipline"),
            (@"\b(re.?tag\s+select(ed)?|retag\s+select|force\s+retag)\b",
                "ReTagSelected", "ReTagSelected", "Force re-derive tags on selected elements"),
            (@"\b(family\s+stage\s+pop|pre.?pop(ulate)?|stage\s+fill)\b",
                "FamilyStagePopulate", "FamilyStagePopulate", "Pre-populate 7 tokens from category and spatial data"),
            (@"\b(select\s+stale|stale\s+select|find\s+stale)\b",
                "SelectStale", "SelectStale", "Select stale elements needing re-tag"),
            (@"\b(quick\s+tag\s+preview|preview\s+tag|tag\s+preview)\b",
                "QuickTagPreview", "QuickTagPreview", "Preview tag values for selected element"),
            (@"\b(highlight\s+invalid|color\s+invalid|mark\s+invalid)\b",
                "HighlightInvalid", "HighlightInvalid", "Colour-code missing/incomplete tags red/orange"),
            (@"\b(clear\s+overrides?|reset\s+overrides?|remove\s+overrides?)\b",
                "ClearOverrides", "ClearOverrides", "Reset all graphic overrides in active view"),
            (@"\b(select\s+spray|select\s+sprinkler|sprinkler\s+select)\b",
                "SelectSprinklers", "SelectSprinklers", "Select all sprinkler elements"),
            (@"\b(select\s+air\s+terminal|select\s+grille|select\s+diffuser)\b",
                "SelectAirTerminals", "SelectAirTerminals", "Select air terminals in active view"),
            (@"\b(select\s+furniture|furniture\s+select)\b",
                "SelectFurniture", "SelectFurniture", "Select furniture and casework elements"),
            (@"\b(select\s+(structural|column|beam)|structural\s+select)\b",
                "SelectStructural", "SelectStructural", "Select structural elements"),
            (@"\b(select\s+empty\s+mark|empty\s+mark|missing\s+mark)\b",
                "SelectEmptyMark", "SelectEmptyMark", "Select elements with empty Mark parameter"),
            (@"\b(select\s+pin(ned)?|pinned\s+elem)\b",
                "SelectPinned", "SelectPinned", "Select pinned elements"),
            (@"\b(select\s+unpin(ned)?|unpinned\s+elem)\b",
                "SelectUnpinned", "SelectUnpinned", "Select unpinned elements"),
            (@"\b(audit\s+to\s+csv|export\s+audit|tag\s+audit\s+csv)\b",
                "AuditTagsCSV", "AuditCSV", "Export tag audit to CSV file"),
            (@"\b(tag\s+stats?|tag\s+statistics|count\s+tags?)\b",
                "TagStats", "TagStats", "Show tag counts by discipline/system/level"),

            // ── Sheet Management Extended ─────────────────────────────────────────
            (@"\b(clone\s+sheet|copy\s+sheet|duplicate\s+sheet)\b",
                "CloneSheet", "CloneSheet", "Clone sheet with all viewports"),
            (@"\b(place\s+unplaced|unplaced\s+view|put\s+views?\s+on\s+sheet)\b",
                "PlaceUnplacedViews", "PlaceUnplaced", "Place unplaced views on sheets"),
            (@"\b(optimal\s+scale|calculate\s+scale|best\s+scale|auto\s+scale)\b",
                "OptimalScale", "OptimalScale", "Calculate optimal viewport scale for sheet"),
            (@"\b(sheet\s+audit|audit\s+sheet|check\s+sheet)\b",
                "SheetAudit", "SheetAudit", "Audit sheets for issues and empty viewports"),
            (@"\b(batch\s+arrange|arrange\s+all\s+sheet|auto\s+arrange)\b",
                "BatchArrange", "BatchArrange", "Auto-arrange viewports across multiple sheets"),
            (@"\b(move\s+viewport|viewport\s+move|shift\s+viewport)\b",
                "MoveViewport", "MoveViewport", "Move viewport between sheets"),
            (@"\b(maxrects|bin\s+pack|max\s+rect\s+layout|space\s+pack)\b",
                "MaxRectsLayout", "MaxRectsLayout", "MaxRects bin packing layout for viewports"),
            (@"\b(save\s+layout\s+preset|layout\s+preset\s+save)\b",
                "SaveLayoutPreset", "SaveLayoutPreset", "Save current sheet layout as named preset"),
            (@"\b(apply\s+layout\s+preset|load\s+layout\s+preset|use\s+layout\s+preset)\b",
                "ApplyLayoutPreset", "ApplyLayoutPreset", "Apply saved sheet layout preset"),
            (@"\b(batch\s+clone\s+sheet|clone\s+multiple\s+sheet|bulk\s+clone)\b",
                "BatchCloneSheets", "BatchCloneSheets", "Clone multiple sheets at once"),
            (@"\b(batch\s+renumber\s+sheet|renumber\s+all\s+sheet|sheet\s+renumber)\b",
                "BatchRenumberSheets", "BatchRenumberSheets", "Batch renumber sheets within discipline groups"),
            (@"\b(auto\s+(assign|set)\s+viewport\s+type|viewport\s+type\s+auto)\b",
                "AutoAssignVPTypes", "AutoAssignVPTypes", "Auto-assign viewport types by rules"),
            (@"\b(export\s+sheet\s+set|sheet\s+set\s+export|export\s+sheet\s+list)\b",
                "ExportSheetSet", "ExportSheetSet", "Export sheet set listing to CSV"),
            (@"\b(place\s+with\s+overflow|overflow\s+sheet|auto\s+overflow)\b",
                "PlaceWithOverflow", "PlaceWithOverflow", "Place views with auto-overflow sheets"),
            (@"\b(save\s+sheet\s+template|template\s+save\s+sheet)\b",
                "SaveSheetTemplate", "SaveSheetTemplate", "Save current sheet as reusable template"),
            (@"\b(sheet\s+(iso|compliance)|iso\s+sheet|sheet\s+standard\s+check)\b",
                "SheetComplianceCheck", "SheetComplianceCheck", "ISO 19650 sheet compliance audit"),
            (@"\b(grid\s+align\s+viewport|snap\s+viewport\s+grid|viewport\s+grid)\b",
                "GridAlignViewports", "GridAlignViewports", "Snap viewport centres to alignment grid"),
            (@"\b(align\s+viewport\s+edge|edge\s+align\s+viewport)\b",
                "AlignViewportEdges", "AlignViewportEdges", "Align viewport edges left/right/top/bottom"),
            (@"\b(distribute\s+viewport|space\s+viewport|even\s+viewport)\b",
                "DistributeViewports", "DistributeViewports", "Distribute viewports evenly across sheet"),
            (@"\b(sheet\s+register\s+export|export\s+register|register\s+csv)\b",
                "ExportSheetRegister", "ExportSheetRegister", "Export comprehensive sheet register to CSV"),
            (@"\b(sheet\s+manager|manage\s+sheets?|open\s+sheet\s+manager)\b",
                "SheetManager", "SheetManager", "Open dual-panel sheet manager dialog"),

            // ── View Management ────────────────────────────────────────────────────
            (@"\b(duplicate\s+view|copy\s+view|clone\s+view)\b",
                "DuplicateView", "DuplicateView", "Duplicate view with detailing or view-only mode"),
            (@"\b(copy\s+view\s+(settings?|template)|clone\s+view\s+settings?)\b",
                "CopyViewSettings", "CopyViewSettings", "Copy filters and graphic overrides between views"),
            (@"\b(auto.?place\s+viewport|viewport\s+auto\s+place)\b",
                "AutoPlaceViewports", "AutoPlaceViewports", "Auto-place viewports on sheets"),
            (@"\b(crop\s+to\s+content|auto\s+crop|smart\s+crop|tight\s+crop)\b",
                "CropToContent", "CropToContent", "Auto-crop view to element extents"),
            (@"\b(batch\s+align\s+viewport|align\s+all\s+viewport)\b",
                "BatchAlignViewports", "BatchAlignViewports", "Batch align viewports across sheets"),
            (@"\b(magic\s+rename|smart\s+rename\s+view|auto\s+rename\s+view)\b",
                "MagicRenameViews", "MagicRenameViews", "Smart rename views by discipline and level"),
            (@"\b(view\s+tab\s+col|colorise\s+tab|colour\s+tab|tab\s+color)\b",
                "ViewTabColour", "ViewTabColour", "Colour-code view tabs by discipline"),
            (@"\b(apply\s+filter\s+to\s+view|view\s+filter\s+apply)\b",
                "ApplyFiltersToViews", "ApplyFilters", "Apply view filters to selected views"),
            (@"\b(auto.?assign\s+(view\s+)?template|template\s+auto\s+assign)\b",
                "AutoAssignTemplates", "AutoAssignTemplates", "Auto-assign view templates using 5-layer matching"),
            (@"\b(view\s+template\s+audit|template\s+audit|audit\s+template)\b",
                "TemplateAudit", "TemplateAudit", "Audit views for template assignment and compliance"),
            (@"\b(template\s+diff|compare\s+template|diff\s+template)\b",
                "TemplateDiff", "TemplateDiff", "Compare VG settings between two view templates"),
            (@"\b(compliance\s+score|template\s+score|view\s+compliance\s+score)\b",
                "TemplateComplianceScore", "TemplateComplianceScore", "Score views on 10-point template compliance scale"),
            (@"\b(auto.?fix\s+template|fix\s+template|repair\s+template)\b",
                "AutoFixTemplate", "AutoFixTemplate", "Auto-repair template issues in views"),
            (@"\b(sync\s+vg\s+override|sync\s+override|push\s+vg\s+override)\b",
                "SyncTemplateOverrides", "SyncTemplateOverrides", "Push VG overrides from template to all bound views"),
            (@"\b(clone\s+template|copy\s+template|duplicate\s+template)\b",
                "CloneTemplate", "CloneTemplate", "Deep clone view template with all VG settings"),
            (@"\b(batch\s+vg\s+reset|reset\s+vg\s+batch|vg\s+batch\s+reset)\b",
                "BatchVGReset", "BatchVGReset", "Reset VG overrides across multiple views"),
            (@"\b(create\s+batch\s+(views?|section)|batch\s+section|batch\s+elevation)\b",
                "BatchSections", "BatchSections", "Batch-create sections or elevations from rooms"),

            // ── Legends Extended ──────────────────────────────────────────────────
            (@"\b(discipline\s+legend|legend\s+disc|disc\s+legend)\b",
                "CreateDisciplineLegend", "DisciplineLegend", "Create discipline colour-coded legend"),
            (@"\b(system\s+legend|legend\s+system|sys\s+legend)\b",
                "CreateSystemLegend", "SystemLegend", "Create system type colour legend"),
            (@"\b(material\s+legend|legend\s+material|material\s+color\s+key)\b",
                "CreateMaterialLegend", "MaterialLegend", "Create material legend from model"),
            (@"\b(equipment\s+legend|legend\s+equip|equip\s+key)\b",
                "CreateEquipmentLegend", "EquipmentLegend", "Create equipment type legend"),
            (@"\b(fire\s+rating\s+legend|fire\s+legend|fire\s+rating\s+key)\b",
                "CreateFireRatingLegend", "FireRatingLegend", "Create fire rating key/legend"),
            (@"\b(tag\s+legend|legend\s+tag|iso\s+tag\s+legend)\b",
                "CreateTagLegend", "TagLegend", "Create ISO 19650 tag format legend"),
            (@"\b(status\s+legend|legend\s+status|state\s+legend)\b",
                "CreateStatusLegend", "StatusLegend", "Create element status legend (new/existing/demolished)"),
            (@"\b(phase\s+legend|legend\s+phase|demolition\s+legend)\b",
                "CreatePhaseLegend", "PhaseLegend", "Create phase legend for demolition and new work"),
            (@"\b(symbol\s+legend|legend\s+symbol|drawing\s+key)\b",
                "CreateSymbolLegend", "SymbolLegend", "Create symbol/notation key for drawings"),
            (@"\b(sync\s+legend|update\s+legend|refresh\s+legend)\b",
                "SyncLegend", "SyncLegend", "Synchronize legend with current element data"),
            (@"\b(color\s+swatch|colour\s+swatch|swatch\s+legend)\b",
                "CreateColorLegend", "ColorLegend", "Create colour swatch legend from current scheme"),

            // ── Tag Style Extended ────────────────────────────────────────────────
            (@"\b(tag\s+style\s+report|report\s+tag\s+style|style\s+status\s+report)\b",
                "TagStyleReport", "TagStyleReport", "Report current tag style status per element type"),
            (@"\b(switch\s+tag\s+style\s+disc|discipline\s+tag\s+style)\b",
                "SwitchTagStyleByDisc", "SwitchTagStyleByDisc", "Switch tag styles by discipline (M=Blue, E=Gold, P=Green)"),
            (@"\b(batch\s+apply\s+(color\s+)?scheme|apply\s+scheme\s+all\s+views?)\b",
                "BatchApplyColorScheme", "BatchApplyColorScheme", "Apply colour scheme to all views in project"),
            (@"\b(color\s+by\s+var(iable)?|variable\s+color|param\s+range\s+color)\b",
                "ColorByVariable", "ColorByVariable", "Color elements by any parameter value or range"),
            (@"\b(set\s+box\s+color|tag\s+box\s+color|border\s+color\s+tag)\b",
                "SetBoxColor", "SetBoxColor", "Set tag box/border colour"),
            (@"\b(set\s+view\s+tag\s+style|view\s+tag\s+style\s+set)\b",
                "SetViewTagStyle", "SetViewTagStyle", "Set tag style mode for entire active view"),
            (@"\b(rag\s+status\s+color|red\s+amber\s+green|compliance\s+color\s+scheme)\b",
                "ApplyColorScheme", "RAGStatus", "Apply RAG (Red/Amber/Green) compliance colour scheme"),
            (@"\b(set\s+tag\s+(text\s+)?size|tag\s+size\s+set|text\s+size\s+tags?)\b",
                "BatchTagTextSize", "BatchTagTextSize", "Set text size for all tags in view"),
            (@"\b(tag\s+line\s+weight|set\s+line\s+weight\s+tag|tag\s+weight)\b",
                "SetTagCategoryLineWeight", "SetLineWeight", "Set line weight for tag annotations by category"),
            (@"\b(set\s+(tag\s+)?text\s+color|tag\s+text\s+colour|annotation\s+text\s+color)\b",
                "SetTagTextColor", "SetTagTextColor", "Set text colour for selected annotation tags"),

            // ── Structural Extended ───────────────────────────────────────────────
            (@"\b(pad\s+footing|pad\s+foundation|column\s+base\s+foundation)\b",
                "StructuralPadFooting", "PadFooting", "Create pad footing foundation under column"),
            (@"\b(strip\s+footing|strip\s+foundation|wall\s+footing)\b",
                "StructuralStripFooting", "StripFooting", "Create strip footing foundation under wall"),
            (@"\b(retaining\s+wall|basement\s+retaining|earth\s+retaining)\b",
                "StructuralRetainingWall", "RetainingWall", "Create retaining wall element"),
            (@"\b(structural\s+slab|concrete\s+slab|rc\s+slab|reinforced\s+slab)\b",
                "StructuralSlab", "StructuralSlab", "Create structural concrete slab"),
            (@"\b(rebar|reinforce(ment)?|bar\s+size|rc\s+design)\b",
                "StructuralRebar", "Rebar", "Add rebar reinforcement to structural elements"),
            (@"\b(bar\s+bending|bbs|rebar\s+schedule|bar\s+bending\s+sched)\b",
                "StructuralBarBendingSchedule", "BBS", "Create bar bending schedule to BS 8666"),
            (@"\b(truss|roof\s+truss|steel\s+truss)\b",
                "StructuralTruss", "Truss", "Create structural truss element"),
            (@"\b(bracing|cross\s+brace|lateral\s+brace)\b",
                "StructuralBracing", "Bracing", "Create structural bracing member"),
            (@"\b(structural\s+analysis|load\s+analysis|frame\s+analysis)\b",
                "StructuralAnalysis", "StructuralAnalysis", "Run structural analysis (load paths, deflection, stress)"),
            (@"\b(load\s+takedown|column\s+load\s+takedown|gravity\s+load)\b",
                "StructuralLoadTakedown", "LoadTakedown", "Calculate column load takedown"),
            (@"\b(deflection\s+check|slab\s+deflect|beam\s+deflect)\b",
                "StructuralDeflection", "Deflection", "Check beam and slab deflection to Eurocode 2"),
            (@"\b(steel\s+section|ub\s+section|uc\s+section|rsj|universal\s+beam)\b",
                "StructuralSteelSection", "SteelSection", "Select and apply UK steel section from library"),
            (@"\b(connection\s+design|steel\s+connection|bolted\s+joint)\b",
                "StructuralConnection", "ConnectionDesign", "Design steel connections to SCI P358"),
            (@"\b(structural\s+dwg\s+wizard|dwg\s+struct\s+wizard)\b",
                "StructuralDWGWizard", "StructuralDWGWizard", "7-page wizard for structural DWG to BIM"),
            (@"\b(excel\s+structural|structural\s+spreadsheet|import\s+structural\s+excel)\b",
                "ExcelStructuralImport", "StructuralExcel", "Import structural member data from Excel"),
            (@"\b(auto\s+size\s+struct|auto\s+size\s+beam|smart\s+size\s+struct)\b",
                "StructuralAutoSize", "AutoSize", "Auto-size structural members to Eurocode"),
            (@"\b(foundation\s+design|ec7|eurocode\s+7|soil\s+bearing)\b",
                "StructuralFoundationDesign", "FoundationDesign", "Foundation design check to EC7"),
            (@"\b(seismic|earthquake\s+load|lateral\s+seismic)\b",
                "StructuralSeismic", "Seismic", "Seismic lateral load analysis"),
            (@"\b(wind\s+load|wind\s+analysis|bs\s+en\s*1991.1.4)\b",
                "StructuralWindLoad", "WindLoad", "Wind load analysis to BS EN 1991-1-4"),
            (@"\b(structural\s+optim|carbon\s+optim\s+struct|section\s+optim)\b",
                "StructuralOptimize", "StructuralOptimize", "Optimize structural sections for carbon and cost"),
            (@"\b(punching\s+shear|flat\s+slab\s+design|column\s+punch)\b",
                "StructuralPunchingShear", "PunchingShear", "Punching shear check for flat slabs to Eurocode 2"),

            // ── Electrical Extended ───────────────────────────────────────────────
            (@"\b(circuit\s+trace|trace\s+circuit|follow\s+circuit)\b",
                "ElecCircuitTrace", "CircuitTrace", "Trace electrical circuit from panel to endpoint"),
            (@"\b(load\s+calc(ulation)?|connected\s+load|demand\s+factor)\b",
                "ElecLoadCalc", "LoadCalc", "Calculate electrical loads and demand factors"),
            (@"\b(arc\s+flash|fault\s+current|short\s+circuit\s+calc|isc\s+calc)\b",
                "ElecArcFlash", "ArcFlash", "Arc flash hazard and fault current analysis"),
            (@"\b(sld|single\s+line\s+diagram|elec\s+schematic|one\s+line\s+diag)\b",
                "ElecSLD", "SLD", "Generate single-line diagram for electrical distribution"),
            (@"\b(cable\s+siz(ing)?|conductor\s+siz|wire\s+siz(ing)?)\b",
                "ElecCableSize", "CableSize", "Size electrical cables to BS 7671"),
            (@"\b(earth(ing)?|bond(ing)?|cpc\s+siz|protective\s+conductor)\b",
                "ElecEarthing", "Earthing", "Check earthing and bonding conductor sizes"),
            (@"\b(rcd|rccb|gfci|residual\s+current|earth\s+fault\s+prot)\b",
                "ElecRCD", "RCD", "RCD/RCCB protection circuit audit"),
            (@"\b(busbar|bus\s+bar|main\s+switch|incomer\s+rating)\b",
                "ElecBusbar", "Busbar", "Busbar rating and incomer circuit check"),
            (@"\b(lighting\s+calc|lux\s+level|illuminance|em\s+lighting)\b",
                "ElecLightingCalc", "LightingCalc", "Lighting calculation and lux level check"),
            (@"\b(fire\s+alarm\s+zone|detector\s+layout|fa\s+zone|fire\s+alarm\s+layout)\b",
                "ElecFireAlarm", "FireAlarmZone", "Fire alarm detector layout and zone audit"),
            (@"\b(create\s+conduit|run\s+conduit|conduit\s+route)\b",
                "CreateConduits", "CreateConduits", "Create conduit runs for cable routing"),
            (@"\b(create\s+cable\s+tray|cable\s+tray\s+create|run\s+tray)\b",
                "CreateCableTrays", "CreateCableTrays", "Create cable tray runs"),
            (@"\b(mep\s+sched|elec\s+fixture\s+sched|panel\s+list)\b",
                "MEPScheduleCommands", "MEPSchedule", "Create MEP equipment/fixture schedules"),

            // ── Plumbing & Public Health ───────────────────────────────────────────
            (@"\b(drain(age)?|sanitary\s+drain|foul\s+water|sewer)\b",
                "PlumbDrainage", "Drainage", "Plumbing drainage and sanitary system layout"),
            (@"\b(cold\s+water|dcw|mains\s+water|potable\s+water)\b",
                "PlumbColdWater", "ColdWater", "Cold water distribution system layout"),
            (@"\b(hot\s+water|dhw|hws|domestic\s+hot\s+water)\b",
                "PlumbHotWater", "HotWater", "Hot water service system layout and sizing"),
            (@"\b(wc\s+layout|toilet\s+layout|bathroom\s+layout|sanitary\s+fittings?)\b",
                "PlumbSanitaryLayout", "SanitaryLayout", "Sanitary fittings layout and drainage grouping"),
            (@"\b(booster\s+pump|water\s+boost|pressurisation\s+set)\b",
                "PlumbBoosterPump", "BoosterPump", "Cold water booster pump sizing and layout"),
            (@"\b(rainwater\s+drain|storm\s+water|roof\s+drain|surface\s+water)\b",
                "PlumbRainwater", "Rainwater", "Rainwater and stormwater drainage layout"),
            (@"\b(gas\s+(supply|pipe|network|meter)|natural\s+gas\s+pipe)\b",
                "PlumbGas", "Gas", "Natural gas supply pipe sizing and layout"),
            (@"\b(grease\s+trap|oil\s+interceptor|grease\s+interceptor)\b",
                "PlumbGreaseTrap", "GreaseTrap", "Grease trap and interceptor placement"),
            (@"\b(backflow|non.?return\s+valve|prevent\s+contamination)\b",
                "PlumbBackflow", "Backflow", "Backflow prevention device placement audit"),
            (@"\b(pipe\s+siz(ing)?|plumb\s+siz|flow\s+rate\s+calc(ulation)?)\b",
                "PlumbPipeSize", "PipeSize", "Plumbing pipe sizing and flow rate calculations"),
            (@"\b(create\s+pipe|run\s+pipe|place\s+pipe|pipe\s+route)\b",
                "CreatePipes", "CreatePipes", "Create plumbing pipe runs"),
            (@"\b(plumbing\s+audit|water\s+audit|plumbing\s+check)\b",
                "PlumbAudit", "PlumbingAudit", "Plumbing system completeness audit"),

            // ── HVAC / Mechanical Extended ────────────────────────────────────────
            (@"\b(hvac\s+(design|layout|system)|air\s+handling\s+unit|ahu)\b",
                "MEPCreation", "HVACDesign", "HVAC system design and equipment placement"),
            (@"\b(duct\s+siz(ing)?|ductwork\s+siz|duct\s+velocity\s+check)\b",
                "MEPSizingCheck", "DuctSize", "Duct sizing and velocity check to CIBSE Guide B"),
            (@"\b(extract\s+duct|supply\s+duct|duct\s+route|ductwork\s+layout)\b",
                "CreateDucts", "DuctRoute", "Create supply/extract ductwork runs"),
            (@"\b(heat\s+load|cooling\s+load|hvac\s+load|thermal\s+load)\b",
                "EnergyAnalysis", "HeatLoad", "HVAC heat and cooling load calculation"),
            (@"\b(vav|variable\s+air\s+volume|air\s+balance|air\s+flow\s+balance)\b",
                "MEPFlowBalance", "AirBalance", "Variable air volume and air flow balancing"),
            (@"\b(chiller|cooling\s+tower|condenser\s+unit)\b",
                "MEPCoolingPlant", "CoolingPlant", "Cooling plant equipment layout and connections"),
            (@"\b(boiler|heat\s+exchanger|heating\s+plant|calorifier)\b",
                "MEPHeatingPlant", "HeatingPlant", "Heating plant equipment layout and connections"),
            (@"\b(ventilation\s+audit|mech\s+audit|hvac\s+audit)\b",
                "MEPSystemAudit", "HVACAudit", "HVAC and ventilation system audit"),
            (@"\b(create\s+duct|place\s+duct|duct\s+create|run\s+duct)\b",
                "CreateDucts", "CreateDuct", "Create HVAC duct runs"),

            // ── Schedules Extended ────────────────────────────────────────────────
            (@"\b(door\s+schedule|schedule\s+doors?)\b",
                "DoorSchedule", "DoorSchedule", "Create door schedule with hardware specifications"),
            (@"\b(window\s+schedule|schedule\s+windows?)\b",
                "WindowSchedule", "WindowSchedule", "Create window schedule with specifications"),
            (@"\b(room\s+schedule|space\s+schedule|area\s+schedule)\b",
                "RoomSpaceAudit", "RoomSchedule", "Create room/space schedule with areas and departments"),
            (@"\b(finish\s+schedule|material\s+finish\s+sched|interior\s+finish)\b",
                "FinishSchedule", "FinishSchedule", "Create interior finish schedule for floors/walls/ceilings"),
            (@"\b(equipment\s+schedule|plant\s+schedule|asset\s+schedule)\b",
                "MEPScheduleCommands", "EquipmentSchedule", "Create MEP equipment and plant schedule"),
            (@"\b(compare\s+schedule|schedule\s+diff|schedule\s+delta)\b",
                "ScheduleCompare", "ScheduleCompare", "Compare two schedules and report differences"),
            (@"\b(schedule\s+audit|audit\s+sched(ule)?)\b",
                "ScheduleAudit", "ScheduleAudit", "Audit schedules for completeness and accuracy"),
            (@"\b(schedule\s+field|field\s+manager\s+sched|add\s+field\s+sched)\b",
                "ScheduleFieldMgr", "ScheduleFieldMgr", "Manage schedule fields and column order"),
            (@"\b(schedule\s+colou?r|colou?r\s+sched|schedule\s+format)\b",
                "ScheduleColor", "ScheduleColor", "Apply colour formatting to schedule rows"),
            (@"\b(schedule\s+stat|sched\s+stats|schedule\s+summary)\b",
                "ScheduleStats", "ScheduleStats", "Generate schedule statistics report"),
            (@"\b(import\s+sched(ule)?|sched(ule)?\s+from\s+excel)\b",
                "ImportSchedulesFromExcel", "ImportSchedule", "Import schedule data from Excel workbook"),
            (@"\b(export\s+sched(ule)?|sched(ule)?\s+to\s+excel)\b",
                "ExportSchedulesToExcel", "ExportSchedule", "Export schedule data to Excel workbook"),
            (@"\b(refresh\s+sched|update\s+sched|recalculate\s+sched)\b",
                "ScheduleRefresh", "ScheduleRefresh", "Refresh and recalculate schedule data"),
            (@"\b(key\s+sched|keynote\s+sched|keynote\s+sync)\b",
                "KeynoteSync", "KeynoteSchedule", "Create keynote schedule and synchronise keynotes"),
            (@"\b(drawing\s+register\s+sched|register\s+sched|doc\s+register\s+sched)\b",
                "DrawingRegisterSchedule", "DrawingRegisterSchedule", "Create drawing register schedule"),

            // ── Sustainability / Carbon ────────────────────────────────────────────
            (@"\b(breeam|green\s+building\s+cert|sustainability\s+cert)\b",
                "SustainabilityBreeam", "BREEAM", "BREEAM v6 sustainability assessment"),
            (@"\b(embodied\s+carbon|upfront\s+carbon|a1.?a3\s+carbon)\b",
                "SustainabilityCarbon", "EmbodiedCarbon", "Embodied carbon assessment (BS EN 15978, ICE v3)"),
            (@"\b(lifecycle\s+carbon|whole\s+life\s+carbon|wlc\s+carbon|cradle.?grave)\b",
                "SustainabilityLifecycle", "LifecycleCarbon", "Whole lifecycle carbon analysis (A1–C4+D)"),
            (@"\b(carbon\s+footprint|co2\s+emiss|greenhouse\s+gas|ghg\s+report)\b",
                "SustainabilityCarbon", "CarbonFootprint", "Project carbon footprint report"),
            (@"\b(ice\s+database|ice\s+v3|inventory\s+carbon\s+energy)\b",
                "SustainabilityCarbonICE", "ICEDatabase", "ICE Database v3 material carbon intensity lookup"),
            (@"\b(circularity|circular\s+economy|end.?of.?life\s+material|reuse\s+score)\b",
                "SustainabilityCircularity", "Circularity", "Circularity and end-of-life recyclability scoring"),
            (@"\b(leed\s+cert|leed\s+point|leed\s+credit)\b",
                "SustainabilityLEED", "LEED", "LEED credit checklist and assessment"),
            (@"\b(well\s+building|well\s+cert|health\s+well\s+standard)\b",
                "SustainabilityWELL", "WELL", "WELL building standard health checks"),
            (@"\b(passive\s+house|passivhaus|ultra.?low\s+energy\s+design)\b",
                "SustainabilityPassivhaus", "Passivhaus", "Passivhaus energy standard compliance check"),
            (@"\b(solar\s+(pv|panel|gain)|photovoltaic|renewable\s+energy)\b",
                "SustainabilitySolar", "Solar", "Solar gain and PV panel feasibility analysis"),
            (@"\b(airtight(ness)?|air\s+permea|q50|blower\s+door\s+test)\b",
                "SustainabilityAirtightness", "Airtightness", "Air permeability and airtightness check to Part L"),

            // ── Worksets & Revisions ───────────────────────────────────────────────
            (@"\b(workset\s+audit|audit\s+workset|check\s+workset)\b",
                "WorksetAudit", "WorksetAudit", "Audit workset assignments and detect orphaned elements"),
            (@"\b(create\s+revision|new\s+revision|add\s+revision)\b",
                "CreateRevision", "CreateRevision", "Create new revision in project"),
            (@"\b(revision\s+cloud|auto\s+revision\s+cloud|mark\s+change\s+cloud)\b",
                "AutoRevisionCloud", "RevisionCloud", "Auto-generate revision clouds from changed elements"),
            (@"\b(revision\s+sched|revision\s+table|revision\s+block)\b",
                "RevisionSchedule", "RevisionSchedule", "Create revision schedule for title block"),
            (@"\b(track\s+element\s+revision|element\s+change\s+track|change\s+tracking)\b",
                "TrackElementRevisions", "TrackRevisions", "Track element changes across revisions"),
            (@"\b(revision\s+compare|compare\s+revision|revision\s+diff)\b",
                "RevisionCompare", "RevisionCompare", "Compare elements between two revisions"),
            (@"\b(issue\s+sheets?\s+for\s+revision|revision\s+issue\s+sheets?)\b",
                "IssueSheetsForRevision", "IssueSheetsRevision", "Issue sheets for current revision"),
            (@"\b(revision\s+export|export\s+revision\s+log|revision\s+history)\b",
                "RevisionExport", "RevisionExport", "Export revision history to CSV"),
            (@"\b(bulk\s+revision\s+stamp|stamp\s+all\s+revision|batch\s+rev\s+stamp)\b",
                "BulkRevisionStamp", "BulkRevisionStamp", "Bulk stamp revision on all tagged elements"),
            (@"\b(revision\s+dashboard|rev\s+dashboard|revision\s+status\s+dash)\b",
                "RevisionDashboard", "RevisionDashboard", "Open revision management dashboard"),

            // ── Issues / BIM Coordination ──────────────────────────────────────────
            (@"\b(raise\s+issue|create\s+issue|log\s+rfi|new\s+rfi|log\s+issue)\b",
                "RaiseIssue", "RaiseIssue", "Raise new BIM issue or RFI"),
            (@"\b(issue\s+dashboard|issues\s+list|open\s+issues|view\s+issues)\b",
                "IssueDashboard", "IssueDashboard", "Open issue management dashboard"),
            (@"\b(update\s+issue|close\s+issue|resolve\s+issue)\b",
                "UpdateIssue", "UpdateIssue", "Update or close existing BIM issue"),
            (@"\b(select\s+issue\s+elem|zoom\s+to\s+issue|issue\s+elem)\b",
                "SelectIssueElements", "SelectIssueElements", "Select and zoom to elements linked to issue"),
            (@"\b(bcf\s+export|export\s+bcf|save\s+bcf)\b",
                "BCFExport", "BCFExport", "Export issues to BCF 2.1 format"),
            (@"\b(bcf\s+import|import\s+bcf|load\s+bcf)\b",
                "BCFImport", "BCFImport", "Import BCF issues from coordination tool"),
            (@"\b(sticky\s+note|model\s+note|3d\s+comment|3d\s+markup)\b",
                "ElementStickyNote", "StickyNote", "Create sticky note on 3D element"),
            (@"\b(export\s+sticky|sticky\s+report|note\s+export)\b",
                "ExportStickyNotes", "ExportStickyNotes", "Export all sticky notes to report"),
            (@"\b(acc\s+publish|bim\s+360\s+upload|acc\s+upload|autodesk\s+cloud)\b",
                "ACCPublish", "ACCPublish", "Publish model to Autodesk Construction Cloud"),
            (@"\b(cde\s+package|iso\s+deliverable\s+package|acc\s+package)\b",
                "CDEPackage", "CDEPackage", "Create ISO 19650 CDE deliverable package"),
            (@"\b(platform\s+sync|bim\s+platform\s+sync|cloud\s+model\s+sync)\b",
                "PlatformSync", "PlatformSync", "Synchronise BIM data to cloud platform"),
            (@"\b(bim\s+coord(ination)?\s+center|open\s+bim\s+coord|bim\s+center)\b",
                "BIMCoordinationCenter", "BIMCoordCenter", "Open BIM Coordination Center dashboard"),
            (@"\b(sharepoint\s+export|sharepoint\s+sync|share\s+point)\b",
                "SharePointExport", "SharePointExport", "Export and sync files to SharePoint"),

            // ── Materials Extended ─────────────────────────────────────────────────
            (@"\b(material\s+library|browse\s+material|material\s+browser)\b",
                "CreateBLEMaterials", "MaterialLibrary", "Browse and apply materials from STING library"),
            (@"\b(create\s+ble\s+material|ble\s+material)\b",
                "CreateBLEMaterials", "CreateBLEMaterials", "Create building element materials from CSV (815 entries)"),
            (@"\b(create\s+mep\s+material|mep\s+material)\b",
                "CreateMEPMaterials", "CreateMEPMaterials", "Create MEP materials from CSV (464 entries)"),
            (@"\b(material\s+takeoff|material\s+quantity|material\s+count)\b",
                "QuantityTakeoff", "MaterialTakeoff", "Generate material quantity takeoff"),
            (@"\b(material\s+schema|material\s+prop(erty)?|material\s+data)\b",
                "CheckDataFiles", "MaterialSchema", "Inspect material schema and data files"),

            // ── Formula Engine ─────────────────────────────────────────────────────
            (@"\b(formula|evaluate\s+formula|calc(ulate)?\s+formula|formula\s+engine)\b",
                "FormulaEvaluator", "Formula", "Run formula evaluator on elements (199 formulas)"),
            (@"\b(formula\s+audit|formula\s+check|eval\s+all\s+formula)\b",
                "DataPipelineValidate", "FormulaAudit", "Audit formula evaluation for all parameters"),
            (@"\b(parameter\s+formula|param\s+calc|derived\s+param(eter)?)\b",
                "FormulaEvaluator", "ParamFormula", "Calculate derived parameter values using formulas"),

            // ── COBie / Handover Extended ──────────────────────────────────────────
            (@"\b(fm\s+handover|o&m\s+manual|operations?\s+manual|maintenance\s+manual)\b",
                "HandoverManual", "HandoverManual", "Generate FM operations and maintenance manual"),
            (@"\b(asset\s+health\s+report|asset\s+health\s+check|health\s+report\s+asset)\b",
                "AssetHealthReport", "AssetHealthReport", "Generate asset condition and health report"),
            (@"\b(space\s+handover|room\s+handover|area\s+handover\s+report)\b",
                "SpaceHandoverReport", "SpaceHandover", "Generate space handover report by department"),
            (@"\b(cobie\s+type\s+map|type\s+map\s+browser|cobie\s+type\s+browser)\b",
                "COBieTypeMap", "COBieTypeMap", "Browse and manage COBie equipment type mappings"),
            (@"\b(cobie\s+picklist|picklist\s+browser|controlled\s+vocab(ulary)?)\b",
                "COBiePicklistBrowser", "COBiePicklist", "Browse COBie V2.4 controlled vocabulary picklists"),
            (@"\b(handover\s+cert(ificate)?|practical\s+complet|completion\s+cert)\b",
                "HandoverCertificate", "HandoverCertificate", "Generate handover certificate document"),
            (@"\b(issue\s+deliverable|deliverable\s+issue|publish\s+deliverable)\b",
                "IssueDeliverable", "IssueDeliverable", "Issue deliverable through CDE workflow"),
            (@"\b(create\s+transmittal|transmittal\s+create|document\s+issue\s+transmit)\b",
                "CreateTransmittalOrchestrated", "CreateTransmittal", "Create and issue document transmittal"),
            (@"\b(bulk\s+issue\s+deliverable|batch\s+issue\s+deliverable)\b",
                "BulkIssueDeliverables", "BulkIssue", "Bulk issue all selected deliverables"),
            (@"\b(rfi|request\s+for\s+info(rmation)?|technical\s+query|tq\s+create)\b",
                "CreateTransmittalOrchestrated", "RFI", "Create Request for Information (RFI) document"),
            (@"\b(variation\s+order|change\s+order|vo\s+create|design\s+instruction)\b",
                "CreateTransmittalOrchestrated", "VariationOrder", "Create variation/change order document"),

            // ── Meetings & Documents ───────────────────────────────────────────────
            (@"\b(meeting\s+minute|meeting\s+notes?|create\s+meeting)\b",
                "CreateMeeting", "MeetingMinutes", "Create meeting minutes from agenda and actions"),
            (@"\b(action\s+(item|point)|follow\s+up\s+action|open\s+action)\b",
                "CreateMeeting", "ActionItems", "Record and track meeting action items"),
            (@"\b(daily\s+(qa|quality|check)|morning\s+health\s+check|daily\s+audit)\b",
                "WorkflowDailyQA", "DailyQA", "Run daily quality assurance workflow"),
            (@"\b(weekly\s+report|weekly\s+data\s+drop|weekly\s+iso\s+drop)\b",
                "WorkflowWeeklyDataDrop", "WeeklyReport", "Run weekly ISO 19650 data drop workflow"),
            (@"\b(document\s+register|doc\s+register\s+open|add\s+document\s+cde)\b",
                "DocumentRegister", "DocumentRegister", "Open document register and CDE management"),
            (@"\b(validate\s+doc\s+naming|doc\s+naming\s+check|file\s+naming\s+check)\b",
                "ValidateDocNaming", "ValidateDocNaming", "Validate document naming against ISO 19650"),
            (@"\b(cde\s+status|cde\s+state\s+update|document\s+state)\b",
                "CDEStatus", "CDEStatus", "Check and update CDE document status"),
            (@"\b(review\s+tracker|review\s+log|technical\s+review\s+track)\b",
                "ReviewTracker", "ReviewTracker", "Track document reviews and approvals"),

            // ── BIM Management / BEP ───────────────────────────────────────────────
            (@"\b(create\s+bep|bim\s+exec(ution)?\s+plan\s+create|bep\s+wizard)\b",
                "CreateBEP", "CreateBEP", "Create BIM Execution Plan document"),
            (@"\b(update\s+bep|edit\s+bep|revise\s+bep)\b",
                "UpdateBEP", "UpdateBEP", "Update existing BIM Execution Plan"),
            (@"\b(generate\s+bep|auto.?gen\s+bep|bep\s+auto\s+generate)\b",
                "GenerateBEP", "GenerateBEP", "Auto-generate BEP from project settings"),
            (@"\b(project\s+dashboard|bim\s+overview\s+dash|model\s+overview)\b",
                "ProjectDashboard", "ProjectDashboard", "Open project overview dashboard"),
            (@"\b(model\s+health\s+export|export\s+health\s+report)\b",
                "ExportModelHealth", "ExportModelHealth", "Export model health report to CSV"),
            (@"\b(full\s+compliance\s+dash|all\s+standards\s+check|compliance\s+full)\b",
                "FullComplianceDashboard", "FullCompliance", "Run full compliance dashboard across all standards"),
            (@"\b(midp\s+track|master\s+info\s+delivery|info\s+delivery\s+plan\s+track)\b",
                "MIDPTracker", "MIDP", "Track MIDP deliverables and information delivery milestones"),
            (@"\b(stage\s+compliance\s+gate|riba\s+stage\s+gate|design\s+gate)\b",
                "StageComplianceGate", "StageGate", "Validate project meets RIBA stage compliance gate"),
            (@"\b(excel\s+export|export\s+to\s+excel|data\s+export\s+excel)\b",
                "ExportToExcel", "ExportExcel", "Export element data to Excel workbook"),
            (@"\b(excel\s+import|import\s+from\s+excel|data\s+from\s+excel)\b",
                "ImportFromExcel", "ImportExcel", "Import element data from Excel workbook"),
            (@"\b(excel\s+round\s+trip|round\s+trip\s+excel|bidirect\s+excel)\b",
                "ExcelRoundTrip", "ExcelRoundTrip", "Bidirectional Excel data round-trip sync"),
            (@"\b(briefcase\s+view|document\s+briefcase|bim\s+briefcase\s+open)\b",
                "BriefcaseView", "BriefcaseView", "Open BIM document briefcase viewer"),
            (@"\b(export\s+4d\s+timeline|4d\s+export|construction\s+sequence\s+export)\b",
                "Export4DTimeline", "Export4D", "Export 4D construction timeline"),
            (@"\b(export\s+5d|5d\s+cost\s+export|cost\s+timeline\s+export)\b",
                "Export5DCostData", "Export5D", "Export 5D cost data and timeline"),
            (@"\b(measured\s+quantity|nrm\s+quantity|measured\s+quant)\b",
                "MeasuredQuantities", "MeasuredQuantities", "Generate measured quantities report"),
            (@"\b(element\s+count|count\s+elements?|element\s+summary)\b",
                "ElementCountSummary", "ElementCount", "Generate element count summary by category"),
            (@"\b(set\s+output\s+dir(ectory)?|output\s+path\s+set|change\s+output)\b",
                "SetOutputDirectory", "SetOutput", "Set output directory for export files"),
            (@"\b(journal\s+pars(er)?|revit\s+journal\s+diagnos|journal\s+file)\b",
                "JournalParser", "JournalParser", "Parse Revit journal files for errors and diagnostics"),
            (@"\b(iso\s+reference|iso\s+code\s+ref|lookup\s+iso\s+code)\b",
                "ISO19650Reference", "ISOReference", "Look up ISO 19650 codes and requirements"),

            // ── Residential / Design Brief ─────────────────────────────────────────
            (@"\b(design\s+(a|me\s+a?)\s+(house|home|building)|house\s+design\s+brief)\b",
                "DesignBrief_Residential", "DesignBrief", "Parse residential design brief and generate building model"),
            (@"\b(\d+\s+bed(room)?s?\s+house|bedroom\s+house|\d+\s+bed\s+home)\b",
                "DesignBrief_Residential", "DesignBrief", "Design house with specified number of bedrooms"),
            (@"\b(budget\s+feasib(ility)?|cost\s+feasib|can\s+i\s+afford|budget\s+estim(ate)?)\b",
                "DesignBrief_Residential", "BudgetFeasibility", "Residential budget feasibility check"),
            (@"\b(ugx|uganda\s+shilling|kampala\s+build|uganda\s+house)\b",
                "DesignBrief_Residential", "UGXBudget", "Residential design with Uganda Shilling budget"),
            (@"\b(modern\s+house|contemporary\s+home|bungalow\s+design|storey\s+house)\b",
                "DesignBrief_Residential", "HouseStyle", "Residential design for specified architectural style"),
            (@"\b(floor\s+plan\s+layout|room\s+layout\s+design|space\s+planning\s+house)\b",
                "DesignBrief_Residential", "FloorPlanLayout", "Generate residential floor plan layout"),
            (@"\b(commercial\s+build(ing)?|office\s+design|retail\s+space|warehouse\s+design)\b",
                "DesignBrief_Commercial", "CommercialBrief", "Commercial building design brief parsing"),

            // ── BIM Knowledge Q&A ──────────────────────────────────────────────────
            (@"\b(what\s+is|explain|define|what\s+does|meaning\s+of)\s+(iso|bim|cde|lod|cobie|ifc)\b",
                "NLPKnowledgeQuery", "KnowledgeQuery", "Answer a BIM knowledge question"),
            (@"\b(what\s+is|explain|define)\s+(uniclass|cibse|breeam|ashrae|nfpa)\b",
                "NLPKnowledgeQuery", "KnowledgeQuery", "Explain a BIM standard or code"),
            (@"\b(what\s+is|explain|define)\s+(bep|midp|tidp|oir|pir|air|eir|aim|pim)\b",
                "NLPKnowledgeQuery", "KnowledgeQuery", "Explain a BIM information management term"),
            (@"\b(how\s+do\s+i|how\s+to\s+use|can\s+i|show\s+me\s+how)\s+\w+\b",
                "NLPKnowledgeQuery", "KnowledgeQuery", "Step-by-step guide for a BIM task"),
            (@"\b(what\s+(are|is)\s+disc(ipline)?\s+codes?|list\s+disc\s+codes?)\b",
                "NLPKnowledgeQuery", "DisciplineCodes", "List and explain STING discipline codes"),
            (@"\b(what\s+(are|is)\s+sys(tem)?\s+codes?|system\s+type\s+codes?)\b",
                "NLPKnowledgeQuery", "SystemCodes", "List and explain STING system type codes"),
            (@"\b(suitability\s+code|cde\s+suitab|s0|s1|s2|s3|s4\s+suitab)\b",
                "NLPKnowledgeQuery", "SuitabilityCode", "Explain ISO 19650 suitability codes"),
            (@"\b(riba\s+stage|plan\s+of\s+work\s+stage|riba\s+plan\s+of\s+work)\b",
                "NLPKnowledgeQuery", "RIBAStages", "Explain RIBA Plan of Work stages 0-7"),
            (@"\b(tag\s+format\s+explain|iso\s+tag\s+format\s+help|how\s+are\s+tags?\s+format)\b",
                "NLPKnowledgeQuery", "TagExplain", "Explain STING ISO 19650 tag format"),
            (@"\b(list\s+workflow|what\s+workflows?\s+(are|exist)|available\s+workflows?)\b",
                "ListWorkflowPresets", "ListWorkflows", "List all available workflow presets"),

            // ── AI / Smart Features ────────────────────────────────────────────────
            (@"\b(ai\s+(tag|learn|smart)|machine\s+learn\s+tag|learn\s+tag\s+rule)\b",
                "Placement_Learn", "AITagLearn", "Learn tagging rules from existing placement using AI"),
            (@"\b(ai\s+(design|generate|brief)|generate\s+with\s+ai|ai\s+building)\b",
                "DesignBrief_Residential", "AIDesign", "Generate building design from AI design brief"),
            (@"\b(ai\s+(question|q&a)|ask\s+ai|bim\s+ai\s+assist)\b",
                "BimKnowledgeBase", "AIQuestion", "Ask an AI-powered BIM knowledge question"),
            (@"\b(ai\s+draft|draft\s+with\s+ai|ai\s+write\s+doc|generate\s+document\s+ai)\b",
                "CreateTransmittalOrchestrated", "AIDraft", "Draft a BIM document using AI assistance"),
            (@"\b(suggest\s+command|smart\s+suggest|what\s+should\s+i|recommend\s+command)\b",
                "CommandSuggestion", "CommandSuggestion", "Get smart command suggestions based on model state"),
            (@"\b(knowledge\s+base|bim\s+knowledge|standards?\s+lookup|look\s+up\s+standard)\b",
                "BimKnowledgeBase", "KnowledgeBase", "Search the BIM knowledge base"),

            // ── Project Setup Extended ─────────────────────────────────────────────
            (@"\b(check\s+data\s+files?|data\s+file\s+check|data\s+inventory)\b",
                "CheckDataFiles", "CheckDataFiles", "Inventory all STING data files with checksums"),
            (@"\b(create\s+line\s+style|line\s+style\s+create|add\s+line\s+style)\b",
                "CreateLineStyles", "CreateStyles", "Create ISO-standard line styles"),
            (@"\b(fill\s+pattern\s+create|hatch\s+create|drafting\s+pattern)\b",
                "CreateFillPatterns", "FillPatterns", "Create standard AEC fill patterns and hatching"),
            (@"\b(object\s+style\s+create|category\s+style|revit\s+category\s+style)\b",
                "CreateObjectStyles", "ObjectStyles", "Create and apply Revit object styles by category"),
            (@"\b(dim(ension)?\s+style\s+create|create\s+dim\s+style)\b",
                "CreateDimensionStyles", "DimensionStyles", "Create standard AEC dimension styles"),
            (@"\b(family\s+param\s+creator|inject\s+param\s+family|shared\s+param\s+family)\b",
                "FamilyParamCreator", "FamilyParamCreator", "Inject STING shared parameters into family files"),
            (@"\b(family\s+param\s+proc|batch\s+family\s+param\s+proc|process\s+rfa)\b",
                "FamilyParameterProcessor", "FamilyParameterProcessor", "Batch process RFA families to add shared parameters"),
            (@"\b(nlp\s+processor|natural\s+language\s+command|command\s+processor\s+nlp)\b",
                "NLPCommandProcessor", "NLPProcessor", "Open NLP command processor for natural language commands"),
        };

        // BIM Knowledge Base — 63 entries covering standards, STING-specific terms, and BIM concepts
        internal static readonly Dictionary<string, string> BimKnowledge = new()
        {
            ["ISO 19650"] = "International standard for managing information over the whole life cycle of a built asset using BIM. Parts 1-3 cover concepts, delivery phase, and operational phase.",
            ["LOD"] = "Level of Development / Level of Detail. LOD 100=Conceptual, 200=Approximate, 300=Precise, 350=Construction, 400=Fabrication, 500=As-built.",
            ["COBie"] = "Construction Operations Building Information Exchange V2.4. Structured data format for facility management handover. 19 worksheets: Contact, Facility, Floor, Space, Zone, Type, Component, System, Assembly, Connection, Spare, Resource, Job, Impact, Document, Attribute, Coordinate, Issue, Picklists.",
            ["IFC"] = "Industry Foundation Classes. An open, neutral data format (ISO 16739) for exchanging BIM data between software platforms. Key entities: IfcWall, IfcSlab, IfcColumn, IfcBeam, IfcDoor, IfcWindow, IfcSpace, IfcSystem, IfcFlowSegment.",
            ["CDE"] = "Common Data Environment. A single source of information for project data. ISO 19650 CDE workflow states: WIP (work in progress), Shared (for review), Published (approved, S-code assigned), Archived (superseded).",
            ["Uniclass 2015"] = "UK unified classification system. Tables: Ac=Activities, Co=Complexes, En=Entities, SL=Spaces/locations, EF=Elements/functions, Ss=Systems, Pr=Products, PM=Project management, FI=Form of information, Ro=Roles.",
            ["CIBSE"] = "Chartered Institution of Building Services Engineers. Guide A=Environmental Design (loads), Guide B=HVAC (B1 Heating, B2 Ventilation, B3 Duct sizing), Guide C=Reference Data, Guide F=Energy Efficiency, TM54=Operational energy.",
            ["BS 7671"] = "IET Wiring Regulations. UK national standard for electrical installations. Covers circuit design, cable sizing (Part 5), earthing (Part 5), protection (Part 4), and special locations (Part 7).",
            ["BS 8300"] = "Design of an accessible and inclusive built environment. Covers wheelchair access, visual impairment, hearing impairment provisions, accessible routes, and sanitary facilities.",
            ["Part L"] = "Building Regulations Part L: Conservation of fuel and power. Sets maximum U-values, air permeability, and minimum HVAC/lighting efficiencies. L1A/L2A for new dwellings/buildings.",
            ["PAS 1192"] = "Predecessor to ISO 19650. PAS 1192-2 (design/construction) and PAS 1192-3 (operations) now superseded by BS EN ISO 19650 parts 1 and 2.",
            ["NBS"] = "National Building Specification. Provides structured specification writing tools and Uniclass classification for the UK construction industry.",
            ["BIM Execution Plan"] = "BEP: Document defining how BIM will be implemented on a project. Covers BIM roles, software, model breakdown structure, information exchanges per stage, and naming conventions.",
            ["Digital Twin"] = "A digital representation of a physical asset kept synchronised through real-time data feeds (sensors, BMS, IoT devices). Enables predictive maintenance and performance optimisation.",
            ["Asset Tag"] = "STING uses 8-segment ISO 19650 tags: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ. Example: M-BLD1-Z01-L02-HVAC-SUP-AHU-0003 = Mechanical, Building 1, Zone 1, Level 2, HVAC, Supply, AHU, unit 3.",
            // Information management terms
            ["EIR"] = "Employer's Information Requirements. The client's document setting out information they need at each project stage. Forms the basis of the BEP.",
            ["AIR"] = "Asset Information Requirements. Information the asset owner needs to operate and maintain the asset after handover. Generated from the OIR.",
            ["OIR"] = "Organisational Information Requirements. Information an organisation needs to fulfil its objectives. The top-level driver for EIR and AIR.",
            ["PIR"] = "Project Information Requirements. Specific information required for a project, derived from the OIR and AIR.",
            ["PIM"] = "Project Information Model. The information model during the design and construction phase. Becomes the AIM at handover.",
            ["AIM"] = "Asset Information Model. The information model representing the built asset during the operational phase. Received from the PIM at handover.",
            ["MIDP"] = "Master Information Delivery Plan. The project-level plan for all information deliverables, compiled from individual TIDPs. Lists what information, who produces it, and when.",
            ["TIDP"] = "Task Information Delivery Plan. An individual team member's plan for their specific deliverables, contributing to the overall MIDP.",
            // Standards and codes
            ["RIBA Stages"] = "RIBA Plan of Work 2020: Stage 0=Strategic Definition, 1=Preparation and Briefing, 2=Concept Design, 3=Spatial Coordination, 4=Technical Design, 5=Manufacturing and Construction, 6=Handover, 7=Use.",
            ["Suitability Codes"] = "ISO 19650 information suitability: S0=Work in Progress, S1=Suitable for Coordination, S2=Suitable for Information, S3=Suitable for Review/Comment, S4=Suitable for Construction/Implementation.",
            ["DISC Codes"] = "STING discipline codes: M=Mechanical/HVAC, E=Electrical LV, P=Plumbing/Public Health, A=Architectural, S=Structural, FP=Fire Protection, LV=Low Voltage/ICT, G=General, H=Healthcare, MG=Medical Gas, RP=Radiation Protection.",
            ["SYS Codes"] = "STING system codes: HVAC, DCW (Domestic Cold Water), DHW (Domestic Hot Water), HWS (Hot Water Service), SAN (Sanitary), RWD (Rainwater Drainage), GAS, FP (Fire Protection), LV (Low Voltage), FLS (Fire Life Safety), COM (Communications), ICT, NCL (Nurse Call), SEC (Security).",
            ["HTM"] = "Health Technical Memoranda. UK NHS guidance for engineering and building services in healthcare. Key: HTM 02-01 (Medical Gas), HTM 03-01 (Heating/Ventilation), HTM 04-01 (Water), HTM 07-01 (Waste).",
            ["HBN"] = "Health Building Notes. UK NHS guidance for the design and briefing of healthcare buildings. Key: HBN 00-01 (General), HBN 03-01 (Mental Health), HBN 07-02 (Dialysis), HBN 13 (HSDU), HBN 16 (Mortuary).",
            ["NFPA 99"] = "US National Fire Protection Association standard for healthcare facilities. Covers medical gas and vacuum systems (Chapter 5), electrical systems (Chapter 6), and essential electrical systems (§5.1.12–5.1.13).",
            ["NCRP 147"] = "US National Council on Radiation Protection Report 147. Structural shielding design for medical X-ray imaging facilities. STING implements the Archer α/β/γ W·U·T calculator for 70-200 kVp.",
            ["BS 9999"] = "Code of practice for fire safety in design, management, and use of buildings. Risk-based approach for means of escape. Replaces BS 5588 series. Incorporates population profiles and travel distances.",
            ["Approved Documents"] = "UK Building Regulations guidance documents. Key: A=Structure, B=Fire Safety, C=Site Preparation, E=Sound, F=Ventilation, G=Water, H=Drainage, J=Combustion, K=Stairs, L=Energy, M=Accessibility, P=Electrical.",
            ["ASHRAE 170"] = "Ventilation of Health Care Facilities. US standard for HVAC in hospitals. Covers pressure relationships (positive/negative), temperature, humidity, air changes, and filtration requirements.",
            // Carbon and sustainability
            ["BS EN 15978"] = "Sustainability of construction works. Assessment of environmental performance of buildings. Lifecycle stages: A1-A5 (construction), B1-B7 (use), C1-C4 (end of life), D (reuse/recovery).",
            ["ICE Database"] = "Inventory of Carbon & Energy (ICE) v3. University of Bath material carbon intensity database. Provides embodied carbon (kgCO2e/kg and kgCO2e/m²) for 200+ construction materials.",
            ["BREEAM"] = "Building Research Establishment Environmental Assessment Method. UK green building rating. Categories: Energy, Water, Materials, Waste, Land, Pollution, Health+Wellbeing, Transport, Management, Innovation. Ratings: Pass/Good/Very Good/Excellent/Outstanding.",
            ["GWP"] = "Global Warming Potential. Carbon equivalence measure (kgCO2e). Based on IPCC AR5 100-year time horizon. Used in all STING embodied carbon calculations via ICE v3.",
            // Classification systems
            ["Uniclass Ss"] = "Uniclass 2015 Systems table (Ss). Used for the SYS token in STING tags. Examples: Ss_30=HVAC, Ss_35=Electrical, Ss_40=Plumbing, Ss_50=Fire Protection, Ss_75=Telecommunications.",
            ["OmniClass"] = "US construction classification system (OCCS). Table 11=Entities by Function, Table 13=Spaces by Function, Table 21=Elements, Table 22=Work Results, Table 23=Products, Table 41=Materials.",
            ["Uniformat II"] = "US construction element classification. Level 1: A=Substructure, B=Shell, C=Interiors, D=Services, E=Equipment, F=Special, G=Sitework. Used for COBie attributes and cost planning.",
            ["NRM"] = "New Rules of Measurement (RICS). NRM1 for cost planning, NRM2 for detailed measurement (bills of quantities), NRM3 for maintenance costs. UK standard for construction cost management.",
            // Structural standards
            ["BS 4449"] = "Carbon steel bars for reinforcement of concrete. UK rebar standard. Grade B500B is the standard ductility class. Defines yield strength (500 MPa), elongation, and bend test requirements.",
            ["BS 8666"] = "Scheduling, dimensioning, bending and cutting of steel reinforcement for concrete. UK bar bending schedule standard. Shape codes 00-99 define standard bar forms used in BBS.",
            // BIM roles and process
            ["BIM Roles"] = "ISO 19650 BIM roles: Appointing Party (client), Lead Appointed Party (main contractor/designer), Appointed Party (sub-contractor/specialist), Information Manager, Task Team, BIM Coordinator.",
            ["Hard Clash"] = "Clash types: Hard clash = two elements physically occupy the same space. Soft/clearance clash = elements violate a required clearance zone (e.g. maintenance access). Duplicate clash = identical elements.",
            ["SFG20"] = "Standard Maintenance Specification for Building Services. UK PPM task interval reference. Skill levels, estimated durations, and task descriptions used by STING COBie Job template generation.",
            // STING-specific terms
            ["TAG7 Sections"] = "STING TAG7 rich narrative sections: A=Identity Header (name, product, manufacturer), B=System and Function, C=Spatial Context (room/grid ref), D=Lifecycle/Status, E=Technical Specs (capacity/flow/voltage), F=Classification (Uniformat/OmniClass/keynote).",
            ["SEQ Scheme"] = "STING sequence numbering schemes: Sequential (global), ByDisc (per discipline), BySys (per system), ByLevel (per level), ByRoom (per room). Set via SetSeqScheme command. Persisted in .sting_seq.json sidecar.",
            ["WorkflowEngine"] = "STING workflow engine. Chains command tags in JSON presets with conditional steps: maxCompliancePct, minCompliancePct, requiresStaleElements. Built-in presets: ProjectKickoff (26 steps), DailyQA (9 steps), DocumentPackage (6 steps).",
            ["TokenPipeline"] = "STING 9-step tagging pipeline per element: 1=CategoryFilter, 2=TypeTokenInherit, 3=PopulateAll, 4=NativeParamMapper, 5=FormulaEngine, 6=BuildAndWriteTag, 7=WriteContainers, 8=WriteTag7All, 9=GetGridRef.",
            ["DrawingType"] = "STING Drawing Type: JSON-defined bundle answering every presentation question for a drawing — paper size, title block, scale, view template, slot layout, crop strategy, annotation rules, print settings.",
            ["ViewStylePack"] = "STING ViewStylePack: shared graphic overrides, filters, VG settings, text/dim styles, and tag-family maps factored out of individual DrawingTypes so 40+ profiles share 11 visual packs.",
            ["NLPEngine"] = "STING NLP Engine: rule-based regex pattern matcher mapping natural language queries to command tags. 600+ patterns. Offline, deterministic, instant. LLM used only for design briefs, BIM Q&A, and document drafting.",
            ["ComplianceScan"] = "STING ComplianceScan: lightweight cached compliance check. RAG status — Red <50%, Amber 50-80%, Green >80% fully-tagged elements. Updates status bar after every tagging operation.",
            // Contract and project management
            ["NEC Contract"] = "New Engineering Contract. UK standard form. NEC4 options: A=Priced with activity schedule, B=Priced with BQ, C=Target with activity schedule, E=Cost reimbursable, F=Management contract.",
            ["CPM"] = "Critical Path Method. Project scheduling technique identifying the longest dependency chain of activities. Float = slack on non-critical activities. Used in STING 4D scheduling.",
            ["BSRIA BG 6"] = "Soft Landings Framework. Ensures buildings perform as designed after handover. Stages: Enhanced Brief, Design Review, Construction Review, Pre-handover, Initial Aftercare (Year 1), Extended Aftercare (Years 2-3).",
            // Additional MEP and building services
            ["CIBSE Guide B"] = "CIBSE Guide B: Heating, Ventilating, Air Conditioning and Refrigeration. B1=Heating, B2=Ventilation, B3=Duct/pipe sizing and system design. Key reference for STING MEP sizing checks.",
            ["HTM 02-01"] = "Medical Gas Pipeline Systems. UK NHS standard. Covers design, installation, testing, and commissioning of MGPS. NFPA 99 §5.1.12 equivalent for US projects. STING MgasFlowSolver implements diversity factors.",
            ["MIDP Deliverables"] = "Common MIDP deliverable codes: EX=Exchange, DR=Drawing, SP=Specification, SH=Schedule, CA=Calculation, CO=Correspondence, MO=Model. Combined with discipline (e.g. M-DR = Mechanical Drawing).",
            ["COBie Type Map"] = "STING maps 70+ equipment types to COBie attributes: AHU, FCU, Chiller, Boiler, Pump, Fan, Luminaire, Panel, Switchboard, UPS, Generator, Sprinkler, Detector etc. Each type has 8-12 expected attributes.",
            ["STING Parameters"] = "STING uses 2,555 shared parameters in 26 groups defined in MR_PARAMETERS.txt. Key groups: ASS_ (tagging), PRJ_ORG_ (project org), ELC_ (electrical), HVC_ (HVAC), PLM_ (plumbing), STR_ (structural), CLN_ (clinical healthcare).",
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
