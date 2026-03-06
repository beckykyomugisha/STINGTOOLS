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
            // Tagging
            (@"\b(auto.?tag|tag\s+elements?|tag\s+view)\b", "AutoTag", "AutoTag", "Tag elements in active view"),
            (@"\b(batch.?tag|tag\s+all|tag\s+project|tag\s+everything)\b", "BatchTag", "BatchTag", "Tag all elements in project"),
            (@"\b(tag.?and.?combine|one.?click.?tag|full.?tag)\b", "TagAndCombine", "TagAndCombine", "One-click tag and combine pipeline"),
            (@"\b(tag\s+new|incremental.?tag|untag)\b", "TagNewOnly", "TagNewOnly", "Tag only new/untagged elements"),
            (@"\b(validate|check\s+tags?|verify\s+tags?)\b", "Validate", "ValidateTags", "Validate tag completeness"),
            (@"\b(combine|merge\s+param|write\s+containers?)\b", "CombineParams", "CombineParameters", "Combine parameters into containers"),
            (@"\b(pre.?tag|audit\s+tags?|dry.?run)\b", "PreTagAudit", "PreTagAudit", "Dry-run tag prediction audit"),
            (@"\b(duplicate.?tags?|find\s+dup|fix\s+dup)\b", "FixDuplicates", "FixDuplicates", "Find and fix duplicate tags"),
            (@"\b(build\s+tags?|rebuild\s+tags?|assemble)\b", "BuildTags", "BuildTags", "Rebuild tags from tokens"),
            (@"\b(completeness|dashboard|compliance\s+dash)\b", "CompletenessDash", "CompletenessDashboard", "Tag completeness dashboard"),

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
                // Prompt for natural language input
                var inputDlg = new TaskDialog("STING Natural Language Command");
                inputDlg.MainInstruction = "Type what you want to do:";
                inputDlg.MainContent = "Examples:\n• \"Tag all elements in the project\"\n• \"Check ISO 19650 compliance\"\n• \"Export to IFC\"\n• \"Color elements by discipline\"\n• \"Create maintenance schedule\"";
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Enter Command", "Type a natural language command");
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Browse Commands", "See all available commands");
                inputDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "BIM Knowledge Base", "Search BIM terminology");

                var choice = inputDlg.Show();

                if (choice == TaskDialogResult.CommandLink2)
                {
                    // Show all commands
                    var allCommands = NLPEngine.IntentPatterns
                        .Select(p => (p.CommandTag, p.Description))
                        .Distinct()
                        .OrderBy(c => c.Description)
                        .ToList();

                    var report = new System.Text.StringBuilder();
                    report.AppendLine("═══ ALL STING COMMANDS ═══\n");
                    var grouped = allCommands.GroupBy(c =>
                    {
                        if (c.Description.Contains("tag") || c.Description.Contains("Tag")) return "Tagging";
                        if (c.Description.Contains("Select")) return "Selection";
                        if (c.Description.Contains("export") || c.Description.Contains("Export")) return "Export";
                        if (c.Description.Contains("Create") || c.Description.Contains("create")) return "Creation";
                        if (c.Description.Contains("compliance") || c.Description.Contains("Compliance") || c.Description.Contains("check")) return "Standards";
                        return "Other";
                    });

                    foreach (var group in grouped.OrderBy(g => g.Key))
                    {
                        report.AppendLine($"── {group.Key} ──");
                        foreach (var (tag, desc) in group)
                            report.AppendLine($"  [{tag}] {desc}");
                    }

                    TaskDialog.Show("STING Commands", report.ToString());
                    return Result.Succeeded;
                }

                if (choice == TaskDialogResult.CommandLink3)
                {
                    // Knowledge base
                    var report = new System.Text.StringBuilder();
                    report.AppendLine("═══ BIM KNOWLEDGE BASE ═══\n");
                    foreach (var (term, def) in NLPEngine.BimKnowledge.OrderBy(k => k.Key))
                        report.AppendLine($"  {term}: {def}\n");

                    TaskDialog.Show("BIM Knowledge Base", report.ToString());
                    return Result.Succeeded;
                }

                // For CommandLink1 - show intent processing info
                var resultDlg = new TaskDialog("NLP Command");
                resultDlg.MainInstruction = "Natural Language Processing";
                resultDlg.MainContent = "The NLP engine recognises 90+ intent patterns across tagging, selection, export, standards, MEP, and facility management domains.\n\nUse the STING dockable panel buttons or type command tags directly.";
                resultDlg.Show();

                StingLog.Info("NLP command processor accessed");
                return Result.Succeeded;
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
                var doc = commandData.Application.ActiveUIDocument.Document;
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
                var taggable = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                    .ToList();

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
