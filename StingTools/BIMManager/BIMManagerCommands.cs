using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING BIM MANAGER — ISO 19650 Project Management System
    //  Provides: BEP generation, document register, issue/RFI tracking,
    //  COBie V2.4 export, CDE status management, transmittal tracking,
    //  review/approval workflows, and project compliance dashboard.
    //
    //  Data storage: JSON files in project data/ directory + Revit ProjectInfo
    //  parameters. All data is portable and version-controlled.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: BIMManagerEngine ──

    /// <summary>
    /// Core engine for the STING BIM Manager system.
    /// Manages JSON-based project data, BEP generation, document registers,
    /// issue tracking, and CDE status workflows.
    /// </summary>
    internal static class BIMManagerEngine
    {
        // ── ISO 19650 Suitability Codes ──
        internal static readonly Dictionary<string, string> SuitabilityCodes = new Dictionary<string, string>
        {
            ["S0"] = "Work In Progress",
            ["S1"] = "Fit for Coordination",
            ["S2"] = "Fit for Information",
            ["S3"] = "Fit for Review and Comment",
            ["S4"] = "Fit for Stage Approval",
            ["S5"] = "Fit for Manufacturing/Procurement",
            ["S6"] = "Fit for PIM Authorization",
            ["S7"] = "Fit for AIM Authorization",
            ["CR"] = "As-Constructed Record Document",
            ["AB"] = "Abandoned/Superseded"
        };

        // ── CDE Container States (ISO 19650-1 §12) ──
        internal static readonly Dictionary<string, string> CDEStates = new Dictionary<string, string>
        {
            ["WIP"]       = "Work In Progress — being developed by originator",
            ["SHARED"]    = "Shared — issued for coordination/review",
            ["PUBLISHED"] = "Published — approved for use",
            ["ARCHIVE"]   = "Archive — retained for reference only"
        };

        // ── RIBA Plan of Work 2020 Stages ──
        internal static readonly Dictionary<int, string> RIBAStages = new Dictionary<int, string>
        {
            [0] = "Strategic Definition",
            [1] = "Preparation and Briefing",
            [2] = "Concept Design",
            [3] = "Spatial Coordination",
            [4] = "Technical Design",
            [5] = "Manufacturing and Construction",
            [6] = "Handover",
            [7] = "Use"
        };

        // ── ISO 19650 Document Naming Fields ──
        // Format: Project-Originator-Volume/System-Level/Location-Type-Role-Classification-Number
        internal static readonly string[] DocNamingFields = new[]
        {
            "Project", "Originator", "Volume_System", "Level_Location",
            "Type", "Role", "Classification", "Number"
        };

        // ── Document Types (BS EN ISO 19650) ──
        internal static readonly Dictionary<string, string> DocumentTypes = new Dictionary<string, string>
        {
            ["AF"] = "Animation / Fly-through",
            ["BQ"] = "Bill of Quantities",
            ["CA"] = "Calculations",
            ["CM"] = "Combined Model (Federated)",
            ["CP"] = "Cost Plan",
            ["CR"] = "Correspondence",
            ["DB"] = "Database",
            ["DR"] = "Drawing (2D)",
            ["FN"] = "File Note",
            ["HS"] = "Health and Safety",
            ["IE"] = "Information Exchange (COBie)",
            ["M2"] = "2D Model",
            ["M3"] = "3D Model",
            ["MI"] = "Minutes / Action Notes",
            ["MO"] = "Model (2021 NA — replaces M2/M3)",
            ["MR"] = "Model-derived Report",
            ["MS"] = "Method Statement",
            ["PP"] = "Presentation",
            ["PR"] = "Programme",
            ["RD"] = "Room Data Sheet",
            ["RI"] = "Request for Information",
            ["RP"] = "Report",
            ["SA"] = "Schedule of Accommodation",
            ["SH"] = "Schedule",
            ["SK"] = "Sketch",
            ["SN"] = "Snagging List",
            ["SP"] = "Specification",
            ["SU"] = "Survey",
            ["TN"] = "Technical Note",
            ["VS"] = "Visualisation"
        };

        // ── Originator/Role Codes ──
        internal static readonly Dictionary<string, string> RoleCodes = new Dictionary<string, string>
        {
            ["A"]  = "Architect",
            ["B"]  = "Building Surveyor",
            ["C"]  = "Civil Engineer",
            ["D"]  = "Drainage/Hydraulic Engineer",
            ["E"]  = "Electrical Engineer",
            ["F"]  = "Facilities Manager",
            ["G"]  = "Geotechnical Engineer",
            ["H"]  = "Heating/HVAC Engineer",
            ["I"]  = "Interior Designer",
            ["K"]  = "Client/Employer",
            ["L"]  = "Landscape Architect",
            ["M"]  = "Mechanical Engineer",
            ["P"]  = "Public Health Engineer",
            ["Q"]  = "Quantity Surveyor/Cost Manager",
            ["S"]  = "Structural Engineer",
            ["T"]  = "Town Planner",
            ["W"]  = "Contractor",
            ["X"]  = "Subcontractor",
            ["Z"]  = "General/Non-disciplinary"
        };

        // ── Issue / RFI Status Codes ──
        internal static readonly Dictionary<string, string> IssueStatuses = new Dictionary<string, string>
        {
            ["OPEN"]        = "New issue raised, awaiting action",
            ["IN_PROGRESS"] = "Being investigated or resolved",
            ["RESPONDED"]   = "Response provided, awaiting acceptance",
            ["ACCEPTED"]    = "Response accepted, issue closed",
            ["REJECTED"]    = "Response rejected, requires rework",
            ["CLOSED"]      = "Issue resolved and verified",
            ["VOID"]        = "Issue withdrawn or superseded"
        };

        // ── Issue Types (BCF-compatible) ──
        internal static readonly Dictionary<string, string> IssueTypes = new Dictionary<string, string>
        {
            ["RFI"]     = "Request for Information",
            ["CLASH"]   = "Coordination Clash",
            ["DESIGN"]  = "Design Issue/Query",
            ["SITE"]    = "Site Observation",
            ["NCR"]     = "Non-Conformance Report",
            ["SNAGGING"]= "Snagging/Defect",
            ["CHANGE"]  = "Change Request",
            ["RISK"]    = "Risk Item",
            ["ACTION"]  = "Action Item",
            ["COMMENT"] = "General Comment"
        };

        // ── Issue Priority Levels ──
        internal static readonly Dictionary<string, string> IssuePriorities = new Dictionary<string, string>
        {
            ["CRITICAL"] = "Blocks progress — immediate action required",
            ["HIGH"]     = "Significant impact — action within 24 hours",
            ["MEDIUM"]   = "Moderate impact — action within 1 week",
            ["LOW"]      = "Minor impact — action at convenience",
            ["INFO"]     = "For information only — no action required"
        };

        // ── BEP Section Definitions (ISO 19650-2 §5.3) ──
        internal static readonly string[] BEPSections = new[]
        {
            "1. Project Information",
            "2. Project Team Directory",
            "3. Project Goals and Uses",
            "4. Organizational Roles and Responsibilities",
            "5. BIM Process Design",
            "6. Information Delivery Milestones (MIDP)",
            "7. Information Standard and Methods (TIDP)",
            "8. IT Solutions / Software Platforms",
            "9. Model Structure and Federation Strategy",
            "10. CDE Workflow and Naming Conventions",
            "11. Level of Information Need (LOD/LOI)",
            "12. Clash Detection and Coordination",
            "13. Quality Assurance and Compliance",
            "14. Security and Access Protocols",
            "15. Handover and O&M Requirements",
            "16. Risk Register",
            "17. Appendices (Standards, Templates, Reference)"
        };

        // ── COBie V2.4 Worksheet Definitions ──
        internal static readonly Dictionary<string, string[]> COBieWorksheets = new Dictionary<string, string[]>
        {
            ["Contact"] = new[] { "Email", "Company", "Phone", "Department", "OrganizationCode",
                "GivenName", "FamilyName", "Street", "PostalBox", "Town", "StateRegion",
                "PostalCode", "Country", "Category", "CreatedBy", "CreatedOn" },
            ["Facility"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "ProjectName",
                "SiteName", "LinearUnits", "AreaUnits", "VolumeUnits", "CurrencyUnit",
                "AreaMeasurement", "ExternalSystem", "ExternalProjectObject", "ExternalProjectIdentifier",
                "ExternalSiteObject", "ExternalSiteIdentifier", "ExternalFacilityObject",
                "ExternalFacilityIdentifier", "Description", "ProjectDescription", "SiteDescription", "Phase" },
            ["Floor"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "ExternalSystem",
                "ExternalObject", "ExternalIdentifier", "Description", "Elevation", "Height" },
            ["Space"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "FloorName",
                "Description", "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                "RoomTag", "UsableHeight", "GrossArea", "NetArea" },
            ["Zone"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "SpaceNames",
                "ExternalSystem", "ExternalObject", "ExternalIdentifier", "Description" },
            ["Type"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "Description",
                "AssetType", "Manufacturer", "ModelNumber", "WarrantyGuarantorParts",
                "WarrantyDurationParts", "WarrantyGuarantorLabor", "WarrantyDurationLabor",
                "WarrantyDurationUnit", "ReplacementCost", "ExpectedLife", "DurationUnit",
                "NominalLength", "NominalWidth", "NominalHeight", "ModelReference",
                "Shape", "Size", "Color", "Finish", "Grade", "Material",
                "Constituents", "Features", "AccessibilityPerformance", "CodePerformance",
                "SustainabilityPerformance" },
            ["Component"] = new[] { "Name", "CreatedBy", "CreatedOn", "TypeName", "Space",
                "Description", "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                "SerialNumber", "InstallationDate", "WarrantyStartDate", "TagNumber",
                "BarCode", "AssetIdentifier" },
            ["System"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "ComponentNames",
                "ExternalSystem", "ExternalObject", "ExternalIdentifier", "Description" },
            ["Assembly"] = new[] { "Name", "CreatedBy", "CreatedOn", "SheetName", "ParentName",
                "ChildNames", "AssemblyType", "Description" },
            ["Connection"] = new[] { "Name", "CreatedBy", "CreatedOn", "ConnectionType",
                "SheetName", "RowName1", "RowName2", "RealizingElement", "PortName1",
                "PortName2", "ExternalSystem", "ExternalObject", "ExternalIdentifier", "Description" },
            ["Spare"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "TypeName",
                "Suppliers", "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                "Description", "SetNumber", "PartNumber" },
            ["Resource"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "ExternalSystem",
                "ExternalObject", "ExternalIdentifier", "Description" },
            ["Job"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "Status",
                "TypeName", "Description", "Duration", "DurationUnit", "Start",
                "TaskStartUnit", "Frequency", "FrequencyUnit", "Priors", "ResourceNames" },
            ["Impact"] = new[] { "Name", "CreatedBy", "CreatedOn", "ImpactType", "ImpactStage",
                "SheetName", "RowName", "Value", "Unit", "LeadInTime", "Duration",
                "LeadOutTime", "ImpactUnit", "Description" },
            ["Document"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "ApprovalBy",
                "Stage", "SheetName", "RowName", "Directory", "File", "ExternalSystem",
                "ExternalObject", "ExternalIdentifier", "Description", "Reference" },
            ["Attribute"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category", "SheetName",
                "RowName", "Value", "Unit", "ExternalSystem", "ExternalObject",
                "ExternalIdentifier", "Description", "AllowedValues" },
            ["Coordinate"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category",
                "SheetName", "RowName", "CoordinateXAxis", "CoordinateYAxis",
                "CoordinateZAxis", "ClockwiseRotation", "ElevationalRotation",
                "YawRotation" },
            ["Issue"] = new[] { "Name", "CreatedBy", "CreatedOn", "Type", "Risk",
                "Chance", "Impact", "SheetName1", "RowName1", "SheetName2", "RowName2",
                "Description", "Owner", "Mitigation" }
        };

        // ═══════════════════════════════════════════════════════════
        //  JSON Data File Helpers
        // ═══════════════════════════════════════════════════════════

        private static string GetProjectDataDir(Document doc)
        {
            string path = doc.PathName;
            if (string.IsNullOrEmpty(path))
                return StingToolsApp.DataPath ?? "";
            return Path.GetDirectoryName(path) ?? "";
        }

        internal static string GetBIMManagerFilePath(Document doc, string fileName)
        {
            string dir = GetProjectDataDir(doc);
            string bimDir = Path.Combine(dir, "STING_BIM_MANAGER");
            if (!Directory.Exists(bimDir))
                Directory.CreateDirectory(bimDir);
            return Path.Combine(bimDir, fileName);
        }

        internal static JObject LoadJsonFile(string path)
        {
            if (!File.Exists(path)) return new JObject();
            try { return JObject.Parse(File.ReadAllText(path)); }
            catch { return new JObject(); }
        }

        internal static JArray LoadJsonArray(string path)
        {
            if (!File.Exists(path)) return new JArray();
            try { return JArray.Parse(File.ReadAllText(path)); }
            catch { return new JArray(); }
        }

        internal static void SaveJsonFile(string path, JToken data)
        {
            try
            {
                File.WriteAllText(path, data.ToString(Formatting.Indented));
                StingLog.Info($"BIMManager: saved {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"BIMManager: failed to save {Path.GetFileName(path)}", ex);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  BEP Generation Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject GenerateBEP(Document doc)
        {
            var bep = new JObject();
            var now = DateTime.Now;

            // ── Section 1: Project Information ──
            var projInfo = new JObject();
            var pi = doc.ProjectInformation;
            projInfo["project_name"] = pi?.Name ?? "Untitled Project";
            projInfo["project_number"] = pi?.Number ?? "";
            projInfo["client_name"] = pi?.ClientName ?? "";
            projInfo["project_address"] = pi?.Address ?? "";
            projInfo["issue_date"] = now.ToString("yyyy-MM-dd");
            projInfo["bep_revision"] = "P01";
            projInfo["bep_status"] = "S3";
            projInfo["project_stage"] = "2 — Concept Design";

            // Auto-detect building info
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            projInfo["number_of_levels"] = levels.Count;
            if (levels.Count > 0)
            {
                projInfo["lowest_level"] = levels.First().Name;
                projInfo["highest_level"] = levels.Last().Name;
                double heightFt = levels.Last().Elevation - levels.First().Elevation;
                projInfo["building_height_m"] = Math.Round(heightFt * 0.3048, 1);
            }

            // Auto-detect project area from rooms
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .ToList();
            double totalAreaSqM = 0;
            foreach (var room in rooms)
            {
                var areaParam = room.get_Parameter(BuiltInParameter.ROOM_AREA);
                if (areaParam != null)
                    totalAreaSqM += areaParam.AsDouble() * 0.092903;
            }
            projInfo["gross_internal_area_m2"] = Math.Round(totalAreaSqM, 1);
            projInfo["number_of_rooms"] = rooms.Count;
            bep["project_information"] = projInfo;

            // ── Section 2: Project Team Directory ──
            var team = new JArray();
            team.Add(CreateTeamMember("Lead Appointed Party", "A", "Architect", "", "", ""));
            team.Add(CreateTeamMember("MEP Engineer", "M", "Mechanical Engineer", "", "", ""));
            team.Add(CreateTeamMember("MEP Engineer", "E", "Electrical Engineer", "", "", ""));
            team.Add(CreateTeamMember("MEP Engineer", "P", "Public Health Engineer", "", "", ""));
            team.Add(CreateTeamMember("Structural Engineer", "S", "Structural Engineer", "", "", ""));
            team.Add(CreateTeamMember("Contractor", "W", "Contractor", "", "", ""));
            team.Add(CreateTeamMember("Client/Employer", "K", "Client Representative", "", "", ""));
            bep["project_team"] = team;

            // ── Section 3: Project Goals and BIM Uses ──
            var goals = new JObject();
            goals["primary_goals"] = new JArray(
                "ISO 19650 compliant information management",
                "Coordinated 3D model for clash detection",
                "Automated asset tagging (STING ISO tags)",
                "COBie V2.4 data drop for FM handover",
                "Quantity extraction for cost management"
            );
            goals["bim_uses"] = new JArray(
                new JObject { ["use"] = "Design Authoring", ["stage"] = "2-4", ["responsible"] = "All disciplines" },
                new JObject { ["use"] = "3D Coordination", ["stage"] = "3-4", ["responsible"] = "BIM Coordinator" },
                new JObject { ["use"] = "Clash Detection", ["stage"] = "3-5", ["responsible"] = "BIM Coordinator" },
                new JObject { ["use"] = "Quantity Takeoff", ["stage"] = "3-5", ["responsible"] = "QS" },
                new JObject { ["use"] = "Asset Management", ["stage"] = "5-7", ["responsible"] = "FM Team" },
                new JObject { ["use"] = "Record Model (As-Built)", ["stage"] = "6", ["responsible"] = "Contractor" },
                new JObject { ["use"] = "Facility Management", ["stage"] = "7", ["responsible"] = "FM Team" }
            );
            bep["goals_and_uses"] = goals;

            // ── Section 4: Roles and Responsibilities ──
            var roles = new JObject();
            roles["information_manager"] = new JObject
            {
                ["role"] = "Information Manager (ISO 19650)",
                ["responsibilities"] = new JArray(
                    "Manage the CDE and information workflows",
                    "Coordinate information exchange milestones",
                    "Validate suitability codes and approvals",
                    "Maintain the BEP and MIDP",
                    "Conduct information model audits"
                )
            };
            roles["bim_coordinator"] = new JObject
            {
                ["role"] = "BIM Coordinator",
                ["responsibilities"] = new JArray(
                    "Federate discipline models",
                    "Run clash detection and resolution",
                    "Maintain STING tag compliance",
                    "Generate coordination reports",
                    "Manage model origin and shared coordinates"
                )
            };
            roles["task_team_members"] = new JObject
            {
                ["role"] = "Task Team Members",
                ["responsibilities"] = new JArray(
                    "Author discipline-specific models",
                    "Apply STING tags to all elements",
                    "Follow naming conventions and LOD requirements",
                    "Issue models with correct suitability codes",
                    "Respond to coordination issues and RFIs"
                )
            };
            bep["roles_and_responsibilities"] = roles;

            // ── Section 5: BIM Process Design ──
            var process = new JObject();
            process["coordination_workflow"] = new JArray(
                "1. Each task team authors discipline model in Revit",
                "2. STING AutoTag applied to all elements (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ)",
                "3. Models issued to CDE SHARED container with suitability code",
                "4. BIM Coordinator federates and runs clash detection",
                "5. Issues raised and tracked in STING Issue Tracker",
                "6. Resolved models re-issued; clashes closed",
                "7. Information Manager authorizes to PUBLISHED container"
            );
            process["information_exchange_frequency"] = "Fortnightly model drops aligned to MIDP milestones";
            process["model_review_meetings"] = "Weekly BIM coordination meetings";
            bep["process_design"] = process;

            // ── Section 6: MIDP (Master Information Delivery Plan) ──
            var midp = new JArray();
            foreach (var stage in RIBAStages)
            {
                midp.Add(new JObject
                {
                    ["stage"] = stage.Key,
                    ["stage_name"] = stage.Value,
                    ["deliverables"] = new JArray(),
                    ["target_date"] = "",
                    ["responsibility"] = "",
                    ["suitability"] = stage.Key <= 2 ? "S3" : (stage.Key <= 4 ? "S4" : "S6")
                });
            }
            bep["midp"] = midp;

            // ── Section 7: Information Standard (TIDP) ──
            var tidp = new JObject();
            tidp["naming_convention"] = "BS EN ISO 19650: Project-Originator-Volume-Level-Type-Role-Class-Number";
            tidp["naming_separator"] = "-";
            tidp["tag_format"] = $"{ParamRegistry.Separator} separated, {ParamRegistry.NumPad}-digit SEQ";
            tidp["tag_segments"] = string.Join(ParamRegistry.Separator, ParamRegistry.SegmentOrder);
            tidp["classification_system"] = "Uniclass 2015";
            tidp["units"] = new JObject { ["length"] = "Millimeters", ["area"] = "Square Meters" };
            bep["information_standard"] = tidp;

            // ── Section 8: Software Platforms ──
            var software = new JArray();
            software.Add(new JObject { ["platform"] = "Autodesk Revit 2025+", ["purpose"] = "BIM Authoring", ["version"] = doc.Application.VersionName });
            software.Add(new JObject { ["platform"] = "STING Tools", ["purpose"] = "ISO 19650 Asset Tagging & BIM Management", ["version"] = "v9.6" });
            software.Add(new JObject { ["platform"] = "Navisworks Manage", ["purpose"] = "Federated model review & clash detection", ["version"] = "" });
            software.Add(new JObject { ["platform"] = "IFC 4 / IFC 2x3", ["purpose"] = "Open BIM data exchange", ["version"] = "" });
            bep["software_platforms"] = software;

            // ── Section 9: Model Structure ──
            var modelStructure = new JObject();
            // Auto-detect worksets
            if (doc.IsWorkshared)
            {
                var worksets = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .ToWorksets()
                    .Select(ws => ws.Name)
                    .ToList();
                modelStructure["worksets"] = new JArray(worksets);
            }
            modelStructure["shared_coordinates"] = "Project base point at site origin";
            modelStructure["model_origin"] = "Shared Coordinates aligned to survey grid";

            // Auto-detect linked models
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .Select(lt => lt.Name)
                .ToList();
            modelStructure["linked_models"] = new JArray(links);
            bep["model_structure"] = modelStructure;

            // ── Section 10: CDE and Naming ──
            var cde = new JObject();
            cde["cde_platform"] = "To be confirmed";
            cde["containers"] = new JObject
            {
                ["WIP"] = "Work In Progress — authoring environment",
                ["SHARED"] = "Shared — for coordination and review",
                ["PUBLISHED"] = "Published — approved for downstream use",
                ["ARCHIVE"] = "Archive — superseded or historical"
            };
            cde["suitability_codes"] = JObject.FromObject(SuitabilityCodes);
            cde["revision_format"] = "P01, P02... (Preliminary) → C01, C02... (Construction)";
            bep["cde_workflow"] = cde;

            // ── Section 11: Level of Information Need ──
            var loin = new JObject();
            loin["lod_by_stage"] = new JObject
            {
                ["Stage_2"] = "LOD 200 — Approximate geometry, generic types",
                ["Stage_3"] = "LOD 300 — Specific geometry, accurate dimensions",
                ["Stage_4"] = "LOD 350 — Detailed geometry, connections, supports",
                ["Stage_5"] = "LOD 400 — Fabrication-ready, shop drawings",
                ["Stage_6"] = "LOD 500 — As-built, verified field conditions"
            };
            loin["loi_requirements"] = new JObject
            {
                ["Stage_2"] = "Basic identity + STING DISC/LOC/ZONE tags",
                ["Stage_3"] = "Full 8-segment ISO tags + system classification",
                ["Stage_4"] = "Technical parameters (flow, power, pressure) + manufacturer",
                ["Stage_5"] = "Serial numbers, installation dates, warranty",
                ["Stage_6"] = "As-built verification, O&M data, COBie populated"
            };
            bep["level_of_information_need"] = loin;

            // ── Section 12: Clash Detection Strategy ──
            var clash = new JObject();
            clash["detection_tool"] = "STING Clash Detection + Navisworks Manage";
            clash["tolerance_mm"] = 25;
            clash["clash_groups"] = new JArray(
                new JObject { ["test"] = "MEP vs Structure", ["priority"] = "HIGH", ["tolerance_mm"] = 25 },
                new JObject { ["test"] = "HVAC vs Electrical", ["priority"] = "HIGH", ["tolerance_mm"] = 50 },
                new JObject { ["test"] = "Plumbing vs Structure", ["priority"] = "HIGH", ["tolerance_mm"] = 25 },
                new JObject { ["test"] = "Fire Protection vs All", ["priority"] = "CRITICAL", ["tolerance_mm"] = 10 },
                new JObject { ["test"] = "Ceiling clearance", ["priority"] = "MEDIUM", ["tolerance_mm"] = 100 }
            );
            clash["resolution_workflow"] = "Issue raised → Assigned → Resolved → Verified → Closed";
            bep["clash_detection"] = clash;

            // ── Section 13: QA and Compliance ──
            var qa = new JObject();
            qa["model_audits"] = new JArray(
                "STING Validate Tags — ISO 19650 token compliance",
                "STING Pre-Tag Audit — predict issues before tagging",
                "STING Template Validation — 45-check BIM template audit",
                "STING BEP Compliance — validate against BEP allowed codes",
                "STING Completeness Dashboard — per-discipline RAG status"
            );
            qa["tag_completeness_target"] = "95% of elements tagged with complete 8-segment ISO tags";
            qa["audit_frequency"] = "Weekly automated audit + monthly manual review";
            bep["quality_assurance"] = qa;

            // ── Section 14: Security ──
            var security = new JObject();
            security["access_levels"] = new JArray("Read-Only", "Contributor", "Reviewer", "Approver", "Admin");
            security["model_protection"] = "Workset-based access control + central model ownership";
            bep["security"] = security;

            // ── Section 15: Handover ──
            var handover = new JObject();
            handover["cobie_version"] = "COBie V2.4 (UK)";
            handover["data_drops"] = new JArray(
                new JObject { ["drop"] = "DD1", ["stage"] = "End of Stage 2", ["content"] = "Spatial data, room lists, basic types" },
                new JObject { ["drop"] = "DD2", ["stage"] = "End of Stage 3", ["content"] = "Full asset register, system classifications" },
                new JObject { ["drop"] = "DD3", ["stage"] = "End of Stage 4", ["content"] = "Technical data, manufacturer info, specifications" },
                new JObject { ["drop"] = "DD4", ["stage"] = "End of Stage 6", ["content"] = "As-built data, serial numbers, warranty, O&M" }
            );
            handover["deliverables"] = new JArray(
                "Native Revit model (.rvt) with STING tags",
                "IFC 4 export with STING property sets",
                "COBie V2.4 spreadsheet (STING FM Handover)",
                "Asset Register CSV (STING Tag Register Export)",
                "Bill of Quantities XLSX (STING BOQ Export)",
                "FM O&M Handover Manual (STING Handover Manual)"
            );
            bep["handover_requirements"] = handover;

            // ── Section 16: Allowed Codes (for BEP Validation) ──
            var allowedCodes = new JObject();
            allowedCodes["allowed_disc"] = new JArray(TagConfig.DiscMap.Values.Distinct().OrderBy(v => v));
            allowedCodes["allowed_loc"] = new JArray(TagConfig.LocCodes);
            allowedCodes["allowed_zone"] = new JArray(TagConfig.ZoneCodes);
            allowedCodes["allowed_sys"] = new JArray(TagConfig.SysMap.Keys.OrderBy(k => k));
            bep["allowed_codes"] = allowedCodes;

            // ── Metadata ──
            bep["_metadata"] = new JObject
            {
                ["generated_by"] = "STING BIM Manager v1.0",
                ["generated_date"] = now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["iso_standard"] = "BS EN ISO 19650-1:2018 / BS EN ISO 19650-2:2018",
                ["sting_version"] = "v9.6"
            };

            return bep;
        }

        private static JObject CreateTeamMember(string role, string code, string discipline,
            string name, string company, string email)
        {
            return new JObject
            {
                ["role"] = role,
                ["originator_code"] = code,
                ["discipline"] = discipline,
                ["contact_name"] = name,
                ["company"] = company,
                ["email"] = email
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  Document Register Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject CreateDocumentEntry(string docId, string title, string type,
            string originator, string suitability, string status, string direction)
        {
            return new JObject
            {
                ["doc_id"] = docId,
                ["title"] = title,
                ["type"] = type,
                ["type_description"] = DocumentTypes.ContainsKey(type) ? DocumentTypes[type] : type,
                ["originator"] = originator,
                ["originator_role"] = RoleCodes.ContainsKey(originator) ? RoleCodes[originator] : originator,
                ["suitability"] = suitability,
                ["suitability_desc"] = SuitabilityCodes.ContainsKey(suitability) ? SuitabilityCodes[suitability] : suitability,
                ["cde_status"] = status,
                ["direction"] = direction,
                ["revision"] = "P01",
                ["date_created"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["date_received"] = direction == "IN" ? DateTime.Now.ToString("yyyy-MM-dd") : "",
                ["date_issued"] = direction == "OUT" ? DateTime.Now.ToString("yyyy-MM-dd") : "",
                ["reviewed_by"] = "",
                ["review_date"] = "",
                ["review_status"] = "",
                ["comments"] = "",
                ["file_reference"] = ""
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  Issue / RFI Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject CreateIssue(string issueType, string priority, string title,
            string description, string assignedTo, string discipline, ICollection<ElementId> elementIds)
        {
            string nextId = $"{issueType}-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmmss}";
            return new JObject
            {
                ["issue_id"] = nextId,
                ["type"] = issueType,
                ["type_description"] = IssueTypes.ContainsKey(issueType) ? IssueTypes[issueType] : issueType,
                ["priority"] = priority,
                ["title"] = title,
                ["description"] = description,
                ["status"] = "OPEN",
                ["assigned_to"] = assignedTo,
                ["discipline"] = discipline,
                ["raised_by"] = Environment.UserName,
                ["date_raised"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                ["date_due"] = "",
                ["date_closed"] = "",
                ["response"] = "",
                ["element_ids"] = new JArray(elementIds?.Select(id => id.Value.ToString()) ?? Enumerable.Empty<string>()),
                ["view_name"] = "",
                ["revision"] = "P01",
                ["comments"] = new JArray()
            };
        }

        internal static string GetNextIssueNumber(JArray issues, string type)
        {
            int max = 0;
            string prefix = type + "-";
            foreach (var issue in issues)
            {
                string id = issue["issue_id"]?.ToString() ?? "";
                if (id.StartsWith(prefix))
                {
                    string numPart = id.Substring(prefix.Length).Replace("-", "");
                    if (int.TryParse(numPart, out int n) && n > max) max = n;
                }
            }
            return $"{type}-{(max + 1):D4}";
        }

        // ═══════════════════════════════════════════════════════════
        //  COBie V2.4 Export Engine
        // ═══════════════════════════════════════════════════════════

        internal static Dictionary<string, List<Dictionary<string, string>>> BuildCOBieData(Document doc)
        {
            var data = new Dictionary<string, List<Dictionary<string, string>>>();
            string createdBy = Environment.UserName;
            string createdOn = DateTime.Now.ToString("yyyy-MM-dd");
            var pi = doc.ProjectInformation;

            // ── Contact ──
            var contacts = new List<Dictionary<string, string>>();
            contacts.Add(new Dictionary<string, string>
            {
                ["Email"] = "",
                ["Company"] = pi?.ClientName ?? "",
                ["Phone"] = "",
                ["Department"] = "",
                ["OrganizationCode"] = "",
                ["GivenName"] = createdBy,
                ["FamilyName"] = "",
                ["Category"] = "Facility Manager",
                ["CreatedBy"] = createdBy,
                ["CreatedOn"] = createdOn
            });
            data["Contact"] = contacts;

            // ── Facility ──
            var facilities = new List<Dictionary<string, string>>();
            facilities.Add(new Dictionary<string, string>
            {
                ["Name"] = pi?.Name ?? "Unnamed Facility",
                ["CreatedBy"] = createdBy,
                ["CreatedOn"] = createdOn,
                ["Category"] = "Facility",
                ["ProjectName"] = pi?.Name ?? "",
                ["SiteName"] = pi?.Address ?? "",
                ["LinearUnits"] = "millimeters",
                ["AreaUnits"] = "square meters",
                ["VolumeUnits"] = "cubic meters",
                ["CurrencyUnit"] = "GBP",
                ["AreaMeasurement"] = "NRM",
                ["Description"] = pi?.Name ?? "",
                ["Phase"] = "New Construction"
            });
            data["Facility"] = facilities;

            // ── Floor ──
            var floors = new List<Dictionary<string, string>>();
            foreach (var level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            {
                floors.Add(new Dictionary<string, string>
                {
                    ["Name"] = level.Name,
                    ["CreatedBy"] = createdBy,
                    ["CreatedOn"] = createdOn,
                    ["Category"] = "Floor",
                    ["ExternalSystem"] = "Revit",
                    ["ExternalObject"] = "Level",
                    ["ExternalIdentifier"] = level.UniqueId,
                    ["Description"] = level.Name,
                    ["Elevation"] = Math.Round(level.Elevation * 304.8, 0).ToString(),
                    ["Height"] = ""
                });
            }
            data["Floor"] = floors;

            // ── Space (from Rooms) ──
            var spaces = new List<Dictionary<string, string>>();
            foreach (var el in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType())
            {
                var room = el as Autodesk.Revit.DB.Architecture.Room;
                if (room == null || room.Area <= 0) continue;
                string lvl = room.Level?.Name ?? "";
                spaces.Add(new Dictionary<string, string>
                {
                    ["Name"] = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name,
                    ["CreatedBy"] = createdBy,
                    ["CreatedOn"] = createdOn,
                    ["Category"] = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "Room",
                    ["FloorName"] = lvl,
                    ["Description"] = $"{room.Name} (#{room.Number})",
                    ["ExternalSystem"] = "Revit",
                    ["ExternalObject"] = "Room",
                    ["ExternalIdentifier"] = room.UniqueId,
                    ["RoomTag"] = room.Number ?? "",
                    ["UsableHeight"] = "",
                    ["GrossArea"] = Math.Round(room.Area * 0.092903, 2).ToString(),
                    ["NetArea"] = Math.Round(room.Area * 0.092903, 2).ToString()
                });
            }
            data["Space"] = spaces;

            // ── Zone (from Room Departments) ──
            var zones = new List<Dictionary<string, string>>();
            var deptGroups = spaces.GroupBy(s => s.ContainsKey("Category") ? s["Category"] : "Unassigned");
            foreach (var dept in deptGroups)
            {
                zones.Add(new Dictionary<string, string>
                {
                    ["Name"] = dept.Key,
                    ["CreatedBy"] = createdBy,
                    ["CreatedOn"] = createdOn,
                    ["Category"] = "Occupancy Zone",
                    ["SpaceNames"] = string.Join(",", dept.Select(s => s["Name"]).Take(20)),
                    ["ExternalSystem"] = "Revit",
                    ["ExternalObject"] = "Zone",
                    ["ExternalIdentifier"] = "",
                    ["Description"] = $"Zone: {dept.Key} ({dept.Count()} spaces)"
                });
            }
            data["Zone"] = zones;

            // ── Type (from FamilySymbol) ──
            var types = new List<Dictionary<string, string>>();
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
            var familySymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && knownCats.Contains(fs.Category.Name))
                .GroupBy(fs => fs.FamilyName + ": " + fs.Name)
                .Select(g => g.First())
                .Take(500)
                .ToList();

            foreach (var fs in familySymbols)
            {
                types.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"{fs.FamilyName}: {fs.Name}",
                    ["CreatedBy"] = createdBy,
                    ["CreatedOn"] = createdOn,
                    ["Category"] = fs.Category?.Name ?? "",
                    ["Description"] = ParameterHelpers.GetString(fs, "ASS_DESCRIPTION_TXT"),
                    ["AssetType"] = "Fixed",
                    ["Manufacturer"] = ParameterHelpers.GetString(fs, "ASS_MANUFACTURER_TXT"),
                    ["ModelNumber"] = ParameterHelpers.GetString(fs, "ASS_MODEL_TXT"),
                    ["WarrantyGuarantorParts"] = "",
                    ["WarrantyDurationParts"] = "",
                    ["ReplacementCost"] = ParameterHelpers.GetString(fs, "ASS_CST_UNIT_PRICE_UGX_NR"),
                    ["ExpectedLife"] = ParameterHelpers.GetString(fs, "ASS_EXPECTED_LIFE_YEARS_YRS"),
                    ["DurationUnit"] = "years",
                    ["ModelReference"] = fs.Name
                });
            }
            data["Type"] = types;

            // ── Component (from tagged elements) ──
            var components = new List<Dictionary<string, string>>();
            foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCats.Contains(cat)) continue;
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag)) continue;

                string roomName = "";
                var roomAtEl = ParameterHelpers.GetRoomAtElement(doc, el);
                if (roomAtEl != null) roomName = roomAtEl.Name;

                string typeName = "";
                var typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var type = doc.GetElement(typeId);
                    if (type is FamilySymbol fst) typeName = $"{fst.FamilyName}: {fst.Name}";
                    else if (type != null) typeName = type.Name;
                }

                components.Add(new Dictionary<string, string>
                {
                    ["Name"] = tag,
                    ["CreatedBy"] = createdBy,
                    ["CreatedOn"] = createdOn,
                    ["TypeName"] = typeName,
                    ["Space"] = roomName,
                    ["Description"] = ParameterHelpers.GetString(el, "ASS_DESCRIPTION_TXT"),
                    ["ExternalSystem"] = "Revit",
                    ["ExternalObject"] = cat,
                    ["ExternalIdentifier"] = el.UniqueId,
                    ["SerialNumber"] = "",
                    ["InstallationDate"] = "",
                    ["WarrantyStartDate"] = "",
                    ["TagNumber"] = tag,
                    ["BarCode"] = "",
                    ["AssetIdentifier"] = tag
                });

                if (components.Count >= 5000) break; // Safety limit
            }
            data["Component"] = components;

            // ── System (from MEP systems) ──
            var systems = new List<Dictionary<string, string>>();
            foreach (var sysCode in TagConfig.SysMap.Keys.OrderBy(k => k))
            {
                var sysComponents = components.Where(c =>
                {
                    string t = c.ContainsKey("Name") ? c["Name"] : "";
                    return t.Contains($"-{sysCode}-");
                }).Select(c => c["Name"]).Take(20).ToList();

                if (sysComponents.Count > 0)
                {
                    systems.Add(new Dictionary<string, string>
                    {
                        ["Name"] = sysCode,
                        ["CreatedBy"] = createdBy,
                        ["CreatedOn"] = createdOn,
                        ["Category"] = sysCode,
                        ["ComponentNames"] = string.Join(",", sysComponents),
                        ["ExternalSystem"] = "STING",
                        ["ExternalObject"] = "System",
                        ["ExternalIdentifier"] = sysCode,
                        ["Description"] = TagConfig.SysMap.ContainsKey(sysCode) ?
                            string.Join(", ", TagConfig.SysMap[sysCode]) : sysCode
                    });
                }
            }
            data["System"] = systems;

            // ── Job (maintenance recommendations) ──
            var jobs = new List<Dictionary<string, string>>();
            var maintenanceTypes = new Dictionary<string, (string freq, string unit)>
            {
                ["HVAC"] = ("6", "months"), ["DCW"] = ("12", "months"), ["DHW"] = ("6", "months"),
                ["HWS"] = ("6", "months"), ["SAN"] = ("12", "months"), ["GAS"] = ("6", "months"),
                ["FP"] = ("3", "months"), ["LV"] = ("12", "months"), ["FLS"] = ("3", "months"),
                ["LTG"] = ("12", "months"), ["ELC"] = ("12", "months")
            };
            foreach (var sys in systems)
            {
                string code = sys["Name"];
                var mt = maintenanceTypes.ContainsKey(code) ? maintenanceTypes[code] : ("12", "months");
                jobs.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"PPM-{code}",
                    ["CreatedBy"] = createdBy,
                    ["CreatedOn"] = createdOn,
                    ["Category"] = "Preventive",
                    ["Status"] = "Not Started",
                    ["TypeName"] = code,
                    ["Description"] = $"Planned Preventive Maintenance for {code} systems",
                    ["Duration"] = "4",
                    ["DurationUnit"] = "hours",
                    ["Start"] = "",
                    ["Frequency"] = mt.freq,
                    ["FrequencyUnit"] = mt.unit,
                    ["ResourceNames"] = "FM Technician"
                });
            }
            data["Job"] = jobs;

            // ── Document ──
            var documents = new List<Dictionary<string, string>>();
            documents.Add(new Dictionary<string, string>
            {
                ["Name"] = "BEP",
                ["CreatedBy"] = createdBy,
                ["CreatedOn"] = createdOn,
                ["Category"] = "BIM Execution Plan",
                ["ApprovalBy"] = "",
                ["Stage"] = "Design",
                ["Description"] = "BIM Execution Plan (ISO 19650)",
                ["Reference"] = "project_bep.json"
            });
            data["Document"] = documents;

            return data;
        }

        // ═══════════════════════════════════════════════════════════
        //  Project Dashboard Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject BuildDashboard(Document doc)
        {
            var dashboard = new JObject();
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);

            // ── Element Statistics ──
            int totalElements = 0, tagged = 0, untagged = 0;
            var discCounts = new Dictionary<string, int>();

            foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCats.Contains(cat)) continue;
                totalElements++;

                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag))
                {
                    tagged++;
                    string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                    if (!string.IsNullOrEmpty(disc))
                    {
                        if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                        discCounts[disc]++;
                    }
                }
                else untagged++;
            }

            dashboard["total_elements"] = totalElements;
            dashboard["tagged"] = tagged;
            dashboard["untagged"] = untagged;
            dashboard["tag_completeness_pct"] = totalElements > 0 ? Math.Round(100.0 * tagged / totalElements, 1) : 0;
            dashboard["discipline_breakdown"] = JObject.FromObject(discCounts.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value));

            // ── Model Statistics ──
            var modelStats = new JObject();
            modelStats["levels"] = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount();
            modelStats["rooms"] = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount();
            modelStats["views"] = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Count(v => !v.IsTemplate);
            modelStats["sheets"] = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
            modelStats["linked_models"] = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).GetElementCount();
            modelStats["is_workshared"] = doc.IsWorkshared;
            dashboard["model_statistics"] = modelStats;

            // ── Issue Summary ──
            string issuesPath = GetBIMManagerFilePath(doc, "issues.json");
            var issues = LoadJsonArray(issuesPath);
            var issueSummary = new JObject();
            issueSummary["total"] = issues.Count;
            foreach (var status in IssueStatuses.Keys)
            {
                int count = issues.Count(i => i["status"]?.ToString() == status);
                if (count > 0) issueSummary[status.ToLower()] = count;
            }
            dashboard["issue_summary"] = issueSummary;

            // ── Document Register Summary ──
            string docsPath = GetBIMManagerFilePath(doc, "document_register.json");
            var docs = LoadJsonArray(docsPath);
            dashboard["document_count"] = docs.Count;
            dashboard["documents_incoming"] = docs.Count(d => d["direction"]?.ToString() == "IN");
            dashboard["documents_outgoing"] = docs.Count(d => d["direction"]?.ToString() == "OUT");

            // ── BEP Status ──
            string bepPath = GetBIMManagerFilePath(doc, "project_bep.json");
            dashboard["bep_exists"] = File.Exists(bepPath);
            if (File.Exists(bepPath))
            {
                var bep = LoadJsonFile(bepPath);
                dashboard["bep_revision"] = bep["project_information"]?["bep_revision"]?.ToString() ?? "P01";
            }

            // ── RAG Status ──
            double pct = (double)(dashboard["tag_completeness_pct"] ?? 0);
            int openIssues = (int)(issueSummary["total"] ?? 0) - (int)(issueSummary["closed"] ?? 0) - (int)(issueSummary["void"] ?? 0);
            string ragStatus = pct >= 80 && openIssues < 10 ? "GREEN" :
                               pct >= 50 || openIssues < 50 ? "AMBER" : "RED";
            dashboard["rag_status"] = ragStatus;

            return dashboard;
        }

        // ═══════════════════════════════════════════════════════════
        //  ISO 19650 Document Naming Generator
        // ═══════════════════════════════════════════════════════════

        internal static string GenerateDocumentName(string project, string originator,
            string volume, string level, string type, string role, string classification, string number)
        {
            return $"{project}-{originator}-{volume}-{level}-{type}-{role}-{classification}-{number}";
        }

        internal static bool ValidateDocumentName(string name, out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(name)) { errors.Add("Document name is empty"); return false; }
            var parts = name.Split('-');
            if (parts.Length < 6) { errors.Add($"Expected 6-8 fields separated by hyphens, got {parts.Length}"); return false; }
            if (parts.Length >= 5 && !DocumentTypes.ContainsKey(parts[4].ToUpper()))
                errors.Add($"Unknown document type code: {parts[4]}");
            if (parts.Length >= 6 && !RoleCodes.ContainsKey(parts[5].ToUpper()))
                errors.Add($"Unknown role code: {parts[5]}");
            return errors.Count == 0;
        }

        // ═══════════════════════════════════════════════════════════
        //  Transmittal Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject CreateTransmittal(Document doc, string recipientOrg, string recipientRole,
            string suitability, string reason, JArray documentIds)
        {
            var pi = doc.ProjectInformation;
            string projectName = pi?.Name ?? "Untitled";

            return new JObject
            {
                ["transmittal_id"] = $"TX-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmmss}",
                ["project_name"] = projectName,
                ["project_number"] = pi?.Number ?? "",
                ["from_organization"] = Environment.UserName,
                ["to_organization"] = recipientOrg,
                ["to_role"] = recipientRole,
                ["date_issued"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["suitability_code"] = suitability,
                ["suitability_desc"] = SuitabilityCodes.ContainsKey(suitability) ? SuitabilityCodes[suitability] : suitability,
                ["reason_for_issue"] = reason,
                ["document_ids"] = documentIds ?? new JArray(),
                ["document_count"] = documentIds?.Count ?? 0,
                ["status"] = "ISSUED",
                ["acknowledged"] = false,
                ["acknowledged_date"] = "",
                ["comments"] = ""
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  Review Tracker Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject CreateReview(string documentId, string reviewerName,
            string reviewerRole, string reviewType)
        {
            return new JObject
            {
                ["review_id"] = $"RV-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmmss}",
                ["document_id"] = documentId,
                ["reviewer_name"] = reviewerName,
                ["reviewer_role"] = reviewerRole,
                ["review_type"] = reviewType,
                ["date_issued"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["date_due"] = DateTime.Now.AddDays(7).ToString("yyyy-MM-dd"),
                ["date_completed"] = "",
                ["status"] = "PENDING",
                ["decision"] = "",
                ["comments"] = new JArray(),
                ["revision_required"] = false
            };
        }
    }

    #endregion


    #region ── Command 1: Generate BEP ──

    // ════════════════════════════════════════════════════════════════════════════
    //  GENERATE BEP — Auto-generates ISO 19650 BIM Execution Plan from project
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateBEPCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            StingLog.Info("BIMManager: Generating BEP...");
            var bep = BIMManagerEngine.GenerateBEP(doc);

            string bepPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "project_bep.json");
            BIMManagerEngine.SaveJsonFile(bepPath, bep);

            // Also save the allowed_codes section as standalone BEP validation file
            // (compatible with existing ValidateBepComplianceCommand)
            string bepValidationPath = StingToolsApp.FindDataFile("project_bep.json");
            if (string.IsNullOrEmpty(bepValidationPath))
                bepValidationPath = Path.Combine(StingToolsApp.DataPath ?? "", "project_bep.json");
            if (!string.IsNullOrEmpty(bepValidationPath))
            {
                try
                {
                    var validationBep = new JObject
                    {
                        ["project_name"] = bep["project_information"]?["project_name"],
                        ["allowed_disc"] = bep["allowed_codes"]?["allowed_disc"],
                        ["allowed_loc"] = bep["allowed_codes"]?["allowed_loc"],
                        ["allowed_zone"] = bep["allowed_codes"]?["allowed_zone"],
                        ["allowed_sys"] = bep["allowed_codes"]?["allowed_sys"]
                    };
                    File.WriteAllText(bepValidationPath, validationBep.ToString(Formatting.Indented));
                    StingLog.Info($"BEP validation file also saved to: {bepValidationPath}");
                }
                catch (Exception ex) { StingLog.Warn($"Could not save BEP validation file: {ex.Message}"); }
            }

            var pi = doc.ProjectInformation;
            var report = new StringBuilder();
            report.AppendLine("BIM Execution Plan Generated");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Project: {pi?.Name ?? "Untitled"}");
            report.AppendLine($"  Number: {pi?.Number ?? "N/A"}");
            report.AppendLine($"  Client: {pi?.ClientName ?? "N/A"}");
            report.AppendLine($"  Sections: {BIMManagerEngine.BEPSections.Length}");
            report.AppendLine($"  Levels: {bep["project_information"]?["number_of_levels"]}");
            report.AppendLine($"  Rooms: {bep["project_information"]?["number_of_rooms"]}");
            report.AppendLine($"  GIA: {bep["project_information"]?["gross_internal_area_m2"]} m²");
            report.AppendLine();
            report.AppendLine("Sections included:");
            foreach (string section in BIMManagerEngine.BEPSections)
                report.AppendLine($"  {section}");
            report.AppendLine();
            report.AppendLine($"Saved to: {bepPath}");
            report.AppendLine();
            report.AppendLine("This BEP is auto-populated from your Revit model.");
            report.AppendLine("Edit the JSON file to add team details, dates, and");
            report.AppendLine("project-specific requirements.");

            TaskDialog.Show("STING BIM Manager — BEP", report.ToString());
            StingLog.Info($"BEP generated: {bepPath}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 2: Project Dashboard ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProjectDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            StingLog.Info("BIMManager: Building project dashboard...");
            var dashboard = BIMManagerEngine.BuildDashboard(doc);

            var report = new StringBuilder();
            report.AppendLine("STING BIM Manager — Project Dashboard");
            report.AppendLine(new string('═', 55));

            string rag = dashboard["rag_status"]?.ToString() ?? "UNKNOWN";
            report.AppendLine($"  Overall Status: {rag}");
            report.AppendLine();

            // Model Stats
            report.AppendLine("  MODEL STATISTICS");
            report.AppendLine($"    Elements:     {dashboard["total_elements"]}");
            report.AppendLine($"    Tagged:       {dashboard["tagged"]}");
            report.AppendLine($"    Untagged:     {dashboard["untagged"]}");
            report.AppendLine($"    Completeness: {dashboard["tag_completeness_pct"]}%");
            report.AppendLine();

            var modelStats = dashboard["model_statistics"] as JObject;
            if (modelStats != null)
            {
                report.AppendLine($"    Levels: {modelStats["levels"]}  |  Rooms: {modelStats["rooms"]}");
                report.AppendLine($"    Views: {modelStats["views"]}   |  Sheets: {modelStats["sheets"]}");
                report.AppendLine($"    Links: {modelStats["linked_models"]}  |  Workshared: {modelStats["is_workshared"]}");
            }
            report.AppendLine();

            // Discipline Breakdown
            var discBreakdown = dashboard["discipline_breakdown"] as JObject;
            if (discBreakdown != null && discBreakdown.Count > 0)
            {
                report.AppendLine("  DISCIPLINE BREAKDOWN");
                foreach (var kv in discBreakdown)
                    report.AppendLine($"    {kv.Key,-6} {kv.Value,6} elements");
                report.AppendLine();
            }

            // Issues
            var issueSummary = dashboard["issue_summary"] as JObject;
            if (issueSummary != null)
            {
                report.AppendLine("  ISSUE TRACKER");
                report.AppendLine($"    Total: {issueSummary["total"]}");
                foreach (var kv in issueSummary)
                    if (kv.Key != "total") report.AppendLine($"    {kv.Key,-14} {kv.Value}");
                report.AppendLine();
            }

            // Documents
            report.AppendLine("  DOCUMENT REGISTER");
            report.AppendLine($"    Total documents: {dashboard["document_count"]}");
            report.AppendLine($"    Incoming: {dashboard["documents_incoming"]}  |  Outgoing: {dashboard["documents_outgoing"]}");
            report.AppendLine();

            // BEP
            report.AppendLine("  BEP STATUS");
            report.AppendLine($"    BEP exists: {dashboard["bep_exists"]}");
            if ((bool)(dashboard["bep_exists"] ?? false))
                report.AppendLine($"    Revision: {dashboard["bep_revision"]}");

            TaskDialog.Show("STING BIM Manager", report.ToString());
            StingLog.Info($"Dashboard: RAG={rag}, tagged={dashboard["tagged"]}/{dashboard["total_elements"]}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 3: Raise Issue / RFI ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RaiseIssueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            // Get selected elements (if any)
            var selectedIds = uidoc.Selection.GetElementIds();

            // Pick issue type
            var typeDlg = new TaskDialog("STING Issue Tracker — Type");
            typeDlg.MainInstruction = "Select issue type:";
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "RFI — Request for Information");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "CLASH — Coordination Clash");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "DESIGN — Design Issue/Query");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "NCR — Non-Conformance Report");
            var typeResult = typeDlg.Show();
            string issueType = typeResult switch
            {
                TaskDialogResult.CommandLink1 => "RFI",
                TaskDialogResult.CommandLink2 => "CLASH",
                TaskDialogResult.CommandLink3 => "DESIGN",
                TaskDialogResult.CommandLink4 => "NCR",
                _ => null
            };
            if (issueType == null) return Result.Cancelled;

            // Pick priority
            var priDlg = new TaskDialog("STING Issue Tracker — Priority");
            priDlg.MainInstruction = "Select priority:";
            priDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "CRITICAL — Blocks progress");
            priDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "HIGH — Action within 24 hours");
            priDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "MEDIUM — Action within 1 week");
            priDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "LOW — Action at convenience");
            var priResult = priDlg.Show();
            string priority = priResult switch
            {
                TaskDialogResult.CommandLink1 => "CRITICAL",
                TaskDialogResult.CommandLink2 => "HIGH",
                TaskDialogResult.CommandLink3 => "MEDIUM",
                TaskDialogResult.CommandLink4 => "LOW",
                _ => "MEDIUM"
            };

            // Get discipline from selected elements or prompt
            string discipline = "";
            if (selectedIds.Count > 0)
            {
                var firstEl = doc.GetElement(selectedIds.First());
                discipline = ParameterHelpers.GetString(firstEl, ParamRegistry.DISC);
            }
            if (string.IsNullOrEmpty(discipline)) discipline = "Z";

            // Create the issue
            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            var issues = BIMManagerEngine.LoadJsonArray(issuesPath);
            string nextId = BIMManagerEngine.GetNextIssueNumber(issues, issueType);

            var issue = BIMManagerEngine.CreateIssue(issueType, priority, "", "", "", discipline, selectedIds);
            issue["issue_id"] = nextId;
            issue["view_name"] = uidoc.ActiveView?.Name ?? "";

            issues.Add(issue);
            BIMManagerEngine.SaveJsonFile(issuesPath, issues);

            var report = new StringBuilder();
            report.AppendLine($"Issue Raised: {nextId}");
            report.AppendLine(new string('═', 40));
            report.AppendLine($"  Type:       {issueType} ({BIMManagerEngine.IssueTypes[issueType]})");
            report.AppendLine($"  Priority:   {priority}");
            report.AppendLine($"  Discipline: {discipline}");
            report.AppendLine($"  Status:     OPEN");
            report.AppendLine($"  View:       {issue["view_name"]}");
            report.AppendLine($"  Elements:   {selectedIds.Count} selected");
            report.AppendLine($"  Raised by:  {Environment.UserName}");
            report.AppendLine();
            report.AppendLine($"Edit: {issuesPath}");
            report.AppendLine("Add title, description, and assignee in the JSON file.");

            TaskDialog.Show("STING Issue Tracker", report.ToString());
            StingLog.Info($"Issue raised: {nextId} ({issueType}, {priority})");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 4: Issue Dashboard ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class IssueDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            var issues = BIMManagerEngine.LoadJsonArray(issuesPath);

            if (issues.Count == 0)
            {
                TaskDialog.Show("STING Issue Tracker", "No issues found.\nUse 'Raise Issue' to create one.");
                return Result.Succeeded;
            }

            var report = new StringBuilder();
            report.AppendLine("STING Issue Tracker — Dashboard");
            report.AppendLine(new string('═', 55));

            // Summary
            var byStatus = issues.GroupBy(i => i["status"]?.ToString() ?? "UNKNOWN")
                .ToDictionary(g => g.Key, g => g.Count());
            var byType = issues.GroupBy(i => i["type"]?.ToString() ?? "UNKNOWN")
                .ToDictionary(g => g.Key, g => g.Count());
            var byPriority = issues.GroupBy(i => i["priority"]?.ToString() ?? "UNKNOWN")
                .ToDictionary(g => g.Key, g => g.Count());

            report.AppendLine($"  Total Issues: {issues.Count}");
            report.AppendLine();
            report.AppendLine("  BY STATUS:");
            foreach (var kv in byStatus.OrderBy(kv => kv.Key))
                report.AppendLine($"    {kv.Key,-14} {kv.Value,4}");
            report.AppendLine();
            report.AppendLine("  BY TYPE:");
            foreach (var kv in byType.OrderBy(kv => kv.Key))
                report.AppendLine($"    {kv.Key,-14} {kv.Value,4}");
            report.AppendLine();
            report.AppendLine("  BY PRIORITY:");
            foreach (var kv in byPriority.OrderBy(kv => kv.Key))
                report.AppendLine($"    {kv.Key,-14} {kv.Value,4}");
            report.AppendLine();

            // Recent issues (last 10)
            report.AppendLine("  RECENT ISSUES:");
            var recent = issues.Reverse().Take(10);
            foreach (var issue in recent)
            {
                string id = issue["issue_id"]?.ToString() ?? "";
                string status = issue["status"]?.ToString() ?? "";
                string pri = issue["priority"]?.ToString() ?? "";
                string title = issue["title"]?.ToString() ?? "(untitled)";
                if (title.Length > 40) title = title.Substring(0, 37) + "...";
                report.AppendLine($"    {id,-18} {status,-12} {pri,-10} {title}");
            }

            report.AppendLine();
            report.AppendLine($"  File: {issuesPath}");

            TaskDialog.Show("STING Issue Tracker", report.ToString());
            StingLog.Info($"Issue dashboard: {issues.Count} issues");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 5: Update Issue Status ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class UpdateIssueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            var issues = BIMManagerEngine.LoadJsonArray(issuesPath);

            // Find open issues
            var openIssues = issues.Where(i =>
            {
                string s = i["status"]?.ToString() ?? "";
                return s == "OPEN" || s == "IN_PROGRESS" || s == "RESPONDED";
            }).ToList();

            if (openIssues.Count == 0)
            {
                TaskDialog.Show("STING Issue Tracker", "No open issues to update.");
                return Result.Succeeded;
            }

            // Pick action
            var dlg = new TaskDialog("STING Issue Tracker — Update");
            dlg.MainInstruction = $"{openIssues.Count} open issue(s). Select action:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Close All RESPONDED Issues");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Mark All OPEN as IN_PROGRESS");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Close Oldest Open Issue");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Export Issues to CSV");
            var result = dlg.Show();

            int updated = 0;
            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    foreach (var issue in issues)
                    {
                        if (issue["status"]?.ToString() == "RESPONDED")
                        {
                            issue["status"] = "CLOSED";
                            issue["date_closed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                            updated++;
                        }
                    }
                    break;
                case TaskDialogResult.CommandLink2:
                    foreach (var issue in issues)
                    {
                        if (issue["status"]?.ToString() == "OPEN")
                        {
                            issue["status"] = "IN_PROGRESS";
                            updated++;
                        }
                    }
                    break;
                case TaskDialogResult.CommandLink3:
                    var oldest = openIssues.FirstOrDefault();
                    if (oldest != null)
                    {
                        oldest["status"] = "CLOSED";
                        oldest["date_closed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                        updated = 1;
                    }
                    break;
                case TaskDialogResult.CommandLink4:
                    ExportIssuesToCSV(doc, issues);
                    return Result.Succeeded;
                default:
                    return Result.Cancelled;
            }

            if (updated > 0)
            {
                BIMManagerEngine.SaveJsonFile(issuesPath, issues);
                TaskDialog.Show("STING Issue Tracker", $"{updated} issue(s) updated.");
            }

            StingLog.Info($"Issue update: {updated} issues changed");
            return Result.Succeeded;
        }

        private void ExportIssuesToCSV(Document doc, JArray issues)
        {
            string dir = BIMManagerEngine.GetBIMManagerFilePath(doc, "");
            string csvPath = Path.Combine(dir, $"STING_ISSUES_{DateTime.Now:yyyyMMdd}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Issue_ID,Type,Priority,Status,Discipline,Title,Raised_By,Date_Raised,Date_Due,Date_Closed,Assigned_To,View,Element_Count");
            foreach (var issue in issues)
            {
                var ids = issue["element_ids"] as JArray;
                sb.AppendLine(string.Join(",",
                    QuoteCSV(issue["issue_id"]?.ToString()),
                    QuoteCSV(issue["type"]?.ToString()),
                    QuoteCSV(issue["priority"]?.ToString()),
                    QuoteCSV(issue["status"]?.ToString()),
                    QuoteCSV(issue["discipline"]?.ToString()),
                    QuoteCSV(issue["title"]?.ToString()),
                    QuoteCSV(issue["raised_by"]?.ToString()),
                    QuoteCSV(issue["date_raised"]?.ToString()),
                    QuoteCSV(issue["date_due"]?.ToString()),
                    QuoteCSV(issue["date_closed"]?.ToString()),
                    QuoteCSV(issue["assigned_to"]?.ToString()),
                    QuoteCSV(issue["view_name"]?.ToString()),
                    ids?.Count.ToString() ?? "0"
                ));
            }

            try
            {
                File.WriteAllText(csvPath, sb.ToString());
                TaskDialog.Show("STING Issue Tracker", $"Exported {issues.Count} issues to:\n{csvPath}");
                StingLog.Info($"Issues exported to CSV: {csvPath}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING Issue Tracker", $"Export failed: {ex.Message}");
                StingLog.Error("Issue CSV export failed", ex);
            }
        }

        private static string QuoteCSV(string val)
        {
            if (string.IsNullOrEmpty(val)) return "\"\"";
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
    }

    #endregion


    #region ── Command 6: Document Register ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DocumentRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string docsPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "document_register.json");
            var docs = BIMManagerEngine.LoadJsonArray(docsPath);

            var report = new StringBuilder();
            report.AppendLine("STING Document Register");
            report.AppendLine(new string('═', 55));
            report.AppendLine($"  Total documents: {docs.Count}");
            report.AppendLine($"  Incoming: {docs.Count(d => d["direction"]?.ToString() == "IN")}");
            report.AppendLine($"  Outgoing: {docs.Count(d => d["direction"]?.ToString() == "OUT")}");
            report.AppendLine();

            // Group by suitability
            var bySuit = docs.GroupBy(d => d["suitability"]?.ToString() ?? "N/A")
                .OrderBy(g => g.Key);
            report.AppendLine("  BY SUITABILITY:");
            foreach (var g in bySuit)
            {
                string desc = BIMManagerEngine.SuitabilityCodes.ContainsKey(g.Key) ?
                    BIMManagerEngine.SuitabilityCodes[g.Key] : g.Key;
                report.AppendLine($"    {g.Key,-4} {desc,-30} {g.Count(),4}");
            }
            report.AppendLine();

            // Group by CDE status
            var byCDE = docs.GroupBy(d => d["cde_status"]?.ToString() ?? "N/A")
                .OrderBy(g => g.Key);
            report.AppendLine("  BY CDE STATUS:");
            foreach (var g in byCDE)
                report.AppendLine($"    {g.Key,-12} {g.Count(),4}");
            report.AppendLine();

            // Recent documents
            report.AppendLine("  RECENT DOCUMENTS (last 10):");
            foreach (var d in docs.Reverse().Take(10))
            {
                string id = d["doc_id"]?.ToString() ?? "";
                string title = d["title"]?.ToString() ?? "";
                string suit = d["suitability"]?.ToString() ?? "";
                string dir = d["direction"]?.ToString() ?? "";
                if (title.Length > 35) title = title.Substring(0, 32) + "...";
                report.AppendLine($"    {id,-20} {dir,-4} {suit,-4} {title}");
            }
            report.AppendLine();
            report.AppendLine($"  File: {docsPath}");

            TaskDialog.Show("STING BIM Manager", report.ToString());
            StingLog.Info($"Document register: {docs.Count} documents");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 7: Add Document ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AddDocumentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Direction
            var dirDlg = new TaskDialog("STING Document Register — Direction");
            dirDlg.MainInstruction = "Document direction:";
            dirDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "INCOMING — Received from external party");
            dirDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "OUTGOING — Issued to external party");
            var dirResult = dirDlg.Show();
            string direction = dirResult == TaskDialogResult.CommandLink1 ? "IN" :
                               dirResult == TaskDialogResult.CommandLink2 ? "OUT" : null;
            if (direction == null) return Result.Cancelled;

            // Document type
            var typeDlg = new TaskDialog("STING Document Register — Type");
            typeDlg.MainInstruction = "Select document type:";
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "M3 — 3D Model");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "DR — Drawing (2D)");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "RP — Report");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "SP — Specification");
            var typeResult = typeDlg.Show();
            string docType = typeResult switch
            {
                TaskDialogResult.CommandLink1 => "M3",
                TaskDialogResult.CommandLink2 => "DR",
                TaskDialogResult.CommandLink3 => "RP",
                TaskDialogResult.CommandLink4 => "SP",
                _ => "M3"
            };

            // Suitability code
            var suitDlg = new TaskDialog("STING Document Register — Suitability");
            suitDlg.MainInstruction = "Select suitability code (ISO 19650):";
            suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "S0 — Work In Progress");
            suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "S2 — Fit for Information");
            suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "S3 — Fit for Review and Comment");
            suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "S4 — Fit for Stage Approval");
            var suitResult = suitDlg.Show();
            string suitability = suitResult switch
            {
                TaskDialogResult.CommandLink1 => "S0",
                TaskDialogResult.CommandLink2 => "S2",
                TaskDialogResult.CommandLink3 => "S3",
                TaskDialogResult.CommandLink4 => "S4",
                _ => "S0"
            };

            // Generate document ID using ISO 19650 naming
            var pi = doc.ProjectInformation;
            string project = pi?.Number ?? "PRJ";
            if (project.Length > 6) project = project.Substring(0, 6);

            string docsPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "document_register.json");
            var docs = BIMManagerEngine.LoadJsonArray(docsPath);
            int nextNum = docs.Count + 1;

            string docId = BIMManagerEngine.GenerateDocumentName(
                project, "Z", "ZZ", "ZZ", docType, "Z", "Zz_99", nextNum.ToString("D4"));

            var entry = BIMManagerEngine.CreateDocumentEntry(
                docId, $"Document {nextNum}", docType, "Z", suitability, "WIP", direction);

            docs.Add(entry);
            BIMManagerEngine.SaveJsonFile(docsPath, docs);

            var report = new StringBuilder();
            report.AppendLine("Document Registered");
            report.AppendLine(new string('═', 40));
            report.AppendLine($"  ID:          {docId}");
            report.AppendLine($"  Type:        {docType} ({BIMManagerEngine.DocumentTypes[docType]})");
            report.AppendLine($"  Direction:   {direction}");
            report.AppendLine($"  Suitability: {suitability} ({BIMManagerEngine.SuitabilityCodes[suitability]})");
            report.AppendLine($"  CDE Status:  WIP");
            report.AppendLine($"  Revision:    P01");
            report.AppendLine();
            report.AppendLine($"Edit details in: {docsPath}");

            TaskDialog.Show("STING Document Register", report.ToString());
            StingLog.Info($"Document registered: {docId} ({docType}, {direction})");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 8: COBie V2.4 Export ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            StingLog.Info("BIMManager: Generating COBie V2.4 export...");
            var cobieData = BIMManagerEngine.BuildCOBieData(doc);

            // Export as CSV files (one per worksheet) + summary
            string dir = BIMManagerEngine.GetBIMManagerFilePath(doc, "");
            string cobieDir = Path.Combine(dir, $"COBie_V24_{DateTime.Now:yyyyMMdd}");
            if (!Directory.Exists(cobieDir)) Directory.CreateDirectory(cobieDir);

            int totalRows = 0;
            var worksheetSummary = new StringBuilder();

            foreach (var worksheet in cobieData)
            {
                string wsName = worksheet.Key;
                var rows = worksheet.Value;
                totalRows += rows.Count;

                // Get column headers from COBie definition or from first row
                string[] headers;
                if (BIMManagerEngine.COBieWorksheets.ContainsKey(wsName))
                    headers = BIMManagerEngine.COBieWorksheets[wsName];
                else if (rows.Count > 0)
                    headers = rows[0].Keys.ToArray();
                else
                    continue;

                var csv = new StringBuilder();
                csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

                foreach (var row in rows)
                {
                    var values = headers.Select(h =>
                    {
                        string val = row.ContainsKey(h) ? row[h] : "";
                        return $"\"{val?.Replace("\"", "\"\"")}\"";
                    });
                    csv.AppendLine(string.Join(",", values));
                }

                string csvPath = Path.Combine(cobieDir, $"COBie_{wsName}.csv");
                try
                {
                    File.WriteAllText(csvPath, csv.ToString());
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"COBie export {wsName}: {ex.Message}");
                }

                worksheetSummary.AppendLine($"    {wsName,-16} {rows.Count,6} rows");
            }

            // Also export as single XLSX if ClosedXML available
            try
            {
                string xlsxPath = Path.Combine(cobieDir, $"COBie_V24_Complete.xlsx");
                using (var workbook = new ClosedXML.Excel.XLWorkbook())
                {
                    foreach (var worksheet in cobieData)
                    {
                        string wsName = worksheet.Key;
                        if (wsName.Length > 31) wsName = wsName.Substring(0, 31);
                        var ws = workbook.Worksheets.Add(wsName);
                        var rows = worksheet.Value;
                        if (rows.Count == 0) continue;

                        string[] headers;
                        if (BIMManagerEngine.COBieWorksheets.ContainsKey(worksheet.Key))
                            headers = BIMManagerEngine.COBieWorksheets[worksheet.Key];
                        else
                            headers = rows[0].Keys.ToArray();

                        // Headers
                        for (int c = 0; c < headers.Length; c++)
                        {
                            ws.Cell(1, c + 1).Value = headers[c];
                            ws.Cell(1, c + 1).Style.Font.Bold = true;
                            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                        }

                        // Data
                        for (int r = 0; r < rows.Count; r++)
                        {
                            for (int c = 0; c < headers.Length; c++)
                            {
                                string val = rows[r].ContainsKey(headers[c]) ? rows[r][headers[c]] : "";
                                ws.Cell(r + 2, c + 1).Value = val;
                            }
                        }

                        ws.Columns().AdjustToContents(1, 100);
                    }

                    workbook.SaveAs(xlsxPath);
                    StingLog.Info($"COBie XLSX exported: {xlsxPath}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"COBie XLSX export: {ex.Message}");
            }

            var report = new StringBuilder();
            report.AppendLine("COBie V2.4 Export Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Standard: COBie V2.4 (BS 1192-4:2014)");
            report.AppendLine($"  Worksheets: {cobieData.Count}");
            report.AppendLine($"  Total rows: {totalRows}");
            report.AppendLine();
            report.AppendLine("  WORKSHEET SUMMARY:");
            report.Append(worksheetSummary);
            report.AppendLine();
            report.AppendLine($"  Output: {cobieDir}");
            report.AppendLine();
            report.AppendLine("Worksheets exported as individual CSVs");
            report.AppendLine("and combined XLSX workbook.");

            TaskDialog.Show("STING BIM Manager — COBie", report.ToString());
            StingLog.Info($"COBie V2.4 export: {cobieData.Count} worksheets, {totalRows} rows");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 9: Transmittal Manager ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTransmittalCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Suitability code for transmittal
            var suitDlg = new TaskDialog("STING Transmittal — Suitability");
            suitDlg.MainInstruction = "Suitability code for this transmittal:";
            suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "S1 — Fit for Coordination");
            suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "S2 — Fit for Information");
            suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "S3 — Fit for Review and Comment");
            suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "S4 — Fit for Stage Approval");
            var suitResult = suitDlg.Show();
            string suitability = suitResult switch
            {
                TaskDialogResult.CommandLink1 => "S1",
                TaskDialogResult.CommandLink2 => "S2",
                TaskDialogResult.CommandLink3 => "S3",
                TaskDialogResult.CommandLink4 => "S4",
                _ => "S3"
            };

            // Create transmittal
            var transmittal = BIMManagerEngine.CreateTransmittal(
                doc, "", "", suitability, "Model drop per MIDP schedule", new JArray());

            string txPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "transmittals.json");
            var transmittals = BIMManagerEngine.LoadJsonArray(txPath);
            transmittals.Add(transmittal);
            BIMManagerEngine.SaveJsonFile(txPath, transmittals);

            // Also generate a text transmittal note
            var pi = doc.ProjectInformation;
            var note = new StringBuilder();
            note.AppendLine("╔══════════════════════════════════════════════════════╗");
            note.AppendLine("║          ISO 19650 DOCUMENT TRANSMITTAL             ║");
            note.AppendLine("╚══════════════════════════════════════════════════════╝");
            note.AppendLine();
            note.AppendLine($"  Transmittal No:  {transmittal["transmittal_id"]}");
            note.AppendLine($"  Date:            {transmittal["date_issued"]}");
            note.AppendLine($"  Project:         {pi?.Name ?? ""}");
            note.AppendLine($"  Project No:      {pi?.Number ?? ""}");
            note.AppendLine();
            note.AppendLine($"  From:            {transmittal["from_organization"]}");
            note.AppendLine($"  To:              {transmittal["to_organization"]}");
            note.AppendLine();
            note.AppendLine($"  Suitability:     {suitability} — {BIMManagerEngine.SuitabilityCodes[suitability]}");
            note.AppendLine($"  Reason:          {transmittal["reason_for_issue"]}");
            note.AppendLine();
            note.AppendLine("  DOCUMENTS ENCLOSED:");
            note.AppendLine("    (Edit transmittals.json to add document references)");
            note.AppendLine();
            note.AppendLine("  NOTE: This transmittal is auto-generated by STING Tools.");
            note.AppendLine("  Edit the JSON file to add recipient, documents, and notes.");
            note.AppendLine();
            note.AppendLine($"  File: {txPath}");

            // Save text version
            string txtPath = Path.Combine(
                BIMManagerEngine.GetBIMManagerFilePath(doc, ""),
                $"TX_{transmittal["transmittal_id"]}_{DateTime.Now:yyyyMMdd}.txt");
            try { File.WriteAllText(txtPath, note.ToString()); }
            catch { /* non-critical */ }

            TaskDialog.Show("STING Transmittal", note.ToString());
            StingLog.Info($"Transmittal created: {transmittal["transmittal_id"]}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 10: CDE Status Manager ──

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CDEStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Show current CDE status and allow change
            var dlg = new TaskDialog("STING CDE Status Manager");
            dlg.MainInstruction = "Set CDE container status for this model:";
            dlg.MainContent = "ISO 19650 defines 4 CDE containers.\n" +
                "The status is stored in Project Information.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "WIP — Work In Progress", "Model is being actively developed");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "SHARED — For Coordination", "Model issued for coordination/review");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "PUBLISHED — Approved", "Model approved for downstream use");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "ARCHIVE — Superseded", "Model retained for reference only");
            var result = dlg.Show();

            string status = result switch
            {
                TaskDialogResult.CommandLink1 => "WIP",
                TaskDialogResult.CommandLink2 => "SHARED",
                TaskDialogResult.CommandLink3 => "PUBLISHED",
                TaskDialogResult.CommandLink4 => "ARCHIVE",
                _ => null
            };
            if (status == null) return Result.Cancelled;

            // Write to Project Information parameter
            using (Transaction tx = new Transaction(doc, "STING Set CDE Status"))
            {
                tx.Start();
                var pi = doc.ProjectInformation;
                ParameterHelpers.SetString(pi, "ASS_CDE_STATUS_TXT", status, true);
                ParameterHelpers.SetString(pi, "ASS_CDE_SUITABILITY_TXT",
                    status == "PUBLISHED" ? "S6" : status == "SHARED" ? "S3" : "S0", true);
                tx.Commit();
            }

            TaskDialog.Show("STING CDE Status",
                $"CDE Status set to: {status}\n" +
                $"({BIMManagerEngine.CDEStates[status]})\n\n" +
                $"This is stored in Project Information parameters:\n" +
                $"  ASS_CDE_STATUS_TXT = {status}\n" +
                $"  ASS_CDE_SUITABILITY_TXT = {(status == "PUBLISHED" ? "S6" : status == "SHARED" ? "S3" : "S0")}");

            StingLog.Info($"CDE status set to {status}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 11: Document Naming Validator ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateDocNamingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Check all sheet names against ISO 19650 naming
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            int compliant = 0, nonCompliant = 0;
            var issues = new List<string>();

            foreach (var sheet in sheets)
            {
                string name = sheet.Name ?? "";
                string number = sheet.SheetNumber ?? "";
                string fullName = $"{number} - {name}";

                // Check minimum field count (at least Project-Originator-Type in number)
                var parts = number.Split('-');
                if (parts.Length >= 3)
                {
                    compliant++;
                }
                else
                {
                    nonCompliant++;
                    if (issues.Count < 20)
                        issues.Add($"  {number}: {name} (only {parts.Length} fields, need 3+)");
                }
            }

            var report = new StringBuilder();
            report.AppendLine("ISO 19650 Document Naming Validation");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Total sheets: {sheets.Count}");
            report.AppendLine($"  Compliant:    {compliant}");
            report.AppendLine($"  Non-compliant: {nonCompliant}");
            report.AppendLine();

            if (nonCompliant > 0)
            {
                report.AppendLine("  NON-COMPLIANT SHEETS:");
                foreach (string s in issues) report.AppendLine(s);
                if (nonCompliant > 20) report.AppendLine($"  ... and {nonCompliant - 20} more");
            }

            report.AppendLine();
            report.AppendLine("  Expected format:");
            report.AppendLine("  Project-Originator-Volume-Level-Type-Role-Class-Number");
            report.AppendLine("  Example: PRJ-ABC-ZZ-01-DR-A-Zz_99-0001");

            TaskDialog.Show("STING BIM Manager", report.ToString());
            StingLog.Info($"Doc naming: {compliant} compliant, {nonCompliant} non-compliant");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 12: Review Tracker ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReviewTrackerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string reviewPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "reviews.json");
            var reviews = BIMManagerEngine.LoadJsonArray(reviewPath);

            var dlg = new TaskDialog("STING Review Tracker");
            dlg.MainInstruction = $"Review Tracker ({reviews.Count} reviews)";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Create New Review Request", "Issue a document for review");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "View Review Status", "Show all pending and completed reviews");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Complete Review", "Mark the oldest pending review as complete");
            var result = dlg.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    var review = BIMManagerEngine.CreateReview(
                        $"DOC-{reviews.Count + 1:D4}", "", "", "Design Review");
                    reviews.Add(review);
                    BIMManagerEngine.SaveJsonFile(reviewPath, reviews);
                    TaskDialog.Show("STING Review Tracker",
                        $"Review created: {review["review_id"]}\n" +
                        $"Due: {review["date_due"]}\n\n" +
                        $"Edit {reviewPath} to add reviewer details.");
                    break;

                case TaskDialogResult.CommandLink2:
                    var statusReport = new StringBuilder();
                    statusReport.AppendLine("Review Status");
                    statusReport.AppendLine(new string('═', 50));
                    var byStatus = reviews.GroupBy(r => r["status"]?.ToString() ?? "")
                        .OrderBy(g => g.Key);
                    foreach (var g in byStatus)
                        statusReport.AppendLine($"  {g.Key,-14} {g.Count(),4}");
                    statusReport.AppendLine();
                    statusReport.AppendLine("  PENDING REVIEWS:");
                    foreach (var r in reviews.Where(r => r["status"]?.ToString() == "PENDING").Take(10))
                    {
                        statusReport.AppendLine($"    {r["review_id"]} — {r["document_id"]} — due {r["date_due"]}");
                    }
                    TaskDialog.Show("STING Review Tracker", statusReport.ToString());
                    break;

                case TaskDialogResult.CommandLink3:
                    var pending = reviews.FirstOrDefault(r => r["status"]?.ToString() == "PENDING");
                    if (pending != null)
                    {
                        pending["status"] = "COMPLETED";
                        pending["date_completed"] = DateTime.Now.ToString("yyyy-MM-dd");
                        pending["decision"] = "APPROVED";
                        BIMManagerEngine.SaveJsonFile(reviewPath, reviews);
                        TaskDialog.Show("STING Review Tracker",
                            $"Review {pending["review_id"]} marked as COMPLETED (APPROVED).");
                    }
                    else
                    {
                        TaskDialog.Show("STING Review Tracker", "No pending reviews.");
                    }
                    break;

                default:
                    return Result.Cancelled;
            }

            StingLog.Info($"Review tracker: {reviews.Count} reviews");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 13: Select Issue Elements ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectIssueElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            var issues = BIMManagerEngine.LoadJsonArray(issuesPath);

            // Get open issues with element IDs
            var openWithElements = issues.Where(i =>
            {
                string s = i["status"]?.ToString() ?? "";
                var ids = i["element_ids"] as JArray;
                return (s == "OPEN" || s == "IN_PROGRESS") && ids != null && ids.Count > 0;
            }).ToList();

            if (openWithElements.Count == 0)
            {
                TaskDialog.Show("STING Issue Tracker", "No open issues with linked elements.");
                return Result.Succeeded;
            }

            // Collect all element IDs from open issues
            var allIds = new List<ElementId>();
            foreach (var issue in openWithElements)
            {
                var ids = issue["element_ids"] as JArray;
                if (ids == null) continue;
                foreach (var id in ids)
                {
                    if (long.TryParse(id.ToString(), out long idVal))
                        allIds.Add(new ElementId(idVal));
                }
            }

            // Filter to existing elements
            var validIds = allIds.Where(id => doc.GetElement(id) != null).ToList();
            if (validIds.Count > 0)
            {
                uidoc.Selection.SetElementIds(validIds);
                TaskDialog.Show("STING Issue Tracker",
                    $"Selected {validIds.Count} elements from {openWithElements.Count} open issues.");
            }
            else
            {
                TaskDialog.Show("STING Issue Tracker", "No valid elements found from open issues.");
            }

            StingLog.Info($"Issue elements: selected {validIds.Count} from {openWithElements.Count} issues");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 14: Export BEP to Text ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportBEPCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string bepPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "project_bep.json");
            if (!File.Exists(bepPath))
            {
                TaskDialog.Show("STING BIM Manager", "No BEP found. Generate one first using 'Generate BEP'.");
                return Result.Succeeded;
            }

            var bep = BIMManagerEngine.LoadJsonFile(bepPath);
            var text = new StringBuilder();

            text.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            text.AppendLine("║       BIM EXECUTION PLAN (BEP) — ISO 19650-2                ║");
            text.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            text.AppendLine();

            // Section 1: Project Info
            var pi = bep["project_information"] as JObject;
            if (pi != null)
            {
                text.AppendLine("1. PROJECT INFORMATION");
                text.AppendLine(new string('─', 50));
                foreach (var kv in pi)
                    text.AppendLine($"  {kv.Key,-28} {kv.Value}");
                text.AppendLine();
            }

            // Section 2: Team
            var team = bep["project_team"] as JArray;
            if (team != null)
            {
                text.AppendLine("2. PROJECT TEAM DIRECTORY");
                text.AppendLine(new string('─', 50));
                foreach (var member in team)
                {
                    text.AppendLine($"  {member["role"],-25} [{member["originator_code"]}] {member["discipline"]}");
                    if (!string.IsNullOrEmpty(member["contact_name"]?.ToString()))
                        text.AppendLine($"    Contact: {member["contact_name"]} ({member["company"]})");
                }
                text.AppendLine();
            }

            // Section 3: Goals
            var goals = bep["goals_and_uses"] as JObject;
            if (goals != null)
            {
                text.AppendLine("3. PROJECT GOALS AND BIM USES");
                text.AppendLine(new string('─', 50));
                var primaryGoals = goals["primary_goals"] as JArray;
                if (primaryGoals != null)
                    foreach (var g in primaryGoals) text.AppendLine($"  • {g}");
                text.AppendLine();
                var uses = goals["bim_uses"] as JArray;
                if (uses != null)
                {
                    text.AppendLine("  BIM Uses:");
                    foreach (var u in uses)
                        text.AppendLine($"    Stage {u["stage"],-6} {u["use"],-25} ({u["responsible"]})");
                }
                text.AppendLine();
            }

            // Sections 4-17 (abbreviated)
            string[] sectionKeys = { "roles_and_responsibilities", "process_design",
                "midp", "information_standard", "software_platforms", "model_structure",
                "cde_workflow", "level_of_information_need", "clash_detection",
                "quality_assurance", "security", "handover_requirements", "allowed_codes" };

            int sectionNum = 4;
            foreach (string key in sectionKeys)
            {
                var section = bep[key];
                if (section == null) { sectionNum++; continue; }

                string title = BIMManagerEngine.BEPSections.Length > sectionNum - 1 ?
                    BIMManagerEngine.BEPSections[sectionNum - 1] : $"{sectionNum}. {key}";
                text.AppendLine(title.ToUpper());
                text.AppendLine(new string('─', 50));

                if (section is JObject obj)
                {
                    foreach (var kv in obj)
                    {
                        if (kv.Value is JArray arr)
                        {
                            text.AppendLine($"  {kv.Key}:");
                            foreach (var item in arr)
                            {
                                if (item is JObject itemObj)
                                    text.AppendLine($"    • {string.Join(", ", itemObj.Properties().Select(p => $"{p.Name}={p.Value}"))}");
                                else
                                    text.AppendLine($"    • {item}");
                            }
                        }
                        else if (kv.Value is JObject nested)
                        {
                            text.AppendLine($"  {kv.Key}:");
                            foreach (var nkv in nested)
                                text.AppendLine($"    {nkv.Key,-24} {nkv.Value}");
                        }
                        else
                        {
                            text.AppendLine($"  {kv.Key,-28} {kv.Value}");
                        }
                    }
                }
                text.AppendLine();
                sectionNum++;
            }

            text.AppendLine(new string('═', 60));
            text.AppendLine($"Generated by STING BIM Manager — {DateTime.Now:yyyy-MM-dd HH:mm}");

            // Save text file
            string txtPath = Path.Combine(
                BIMManagerEngine.GetBIMManagerFilePath(doc, ""),
                $"BEP_{doc.ProjectInformation?.Number ?? "PRJ"}_{DateTime.Now:yyyyMMdd}.txt");
            try
            {
                File.WriteAllText(txtPath, text.ToString());
                TaskDialog.Show("STING BIM Manager",
                    $"BEP exported as text document:\n{txtPath}\n\n" +
                    $"Sections: {BIMManagerEngine.BEPSections.Length}");
                StingLog.Info($"BEP exported: {txtPath}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING BIM Manager", $"Export failed: {ex.Message}");
                StingLog.Error("BEP text export failed", ex);
            }

            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 15: ISO 19650 Reference Guide ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ISO19650ReferenceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var report = new StringBuilder();
            report.AppendLine("ISO 19650 Quick Reference Guide");
            report.AppendLine(new string('═', 55));
            report.AppendLine();

            report.AppendLine("  SUITABILITY CODES:");
            foreach (var kv in BIMManagerEngine.SuitabilityCodes)
                report.AppendLine($"    {kv.Key,-4} {kv.Value}");
            report.AppendLine();

            report.AppendLine("  CDE CONTAINERS:");
            foreach (var kv in BIMManagerEngine.CDEStates)
                report.AppendLine($"    {kv.Key,-10} {kv.Value}");
            report.AppendLine();

            report.AppendLine("  DOCUMENT TYPE CODES:");
            foreach (var kv in BIMManagerEngine.DocumentTypes.Take(15))
                report.AppendLine($"    {kv.Key,-4} {kv.Value}");
            if (BIMManagerEngine.DocumentTypes.Count > 15)
                report.AppendLine($"    ... and {BIMManagerEngine.DocumentTypes.Count - 15} more");
            report.AppendLine();

            report.AppendLine("  ROLE CODES:");
            foreach (var kv in BIMManagerEngine.RoleCodes)
                report.AppendLine($"    {kv.Key,-4} {kv.Value}");
            report.AppendLine();

            report.AppendLine("  RIBA PLAN OF WORK STAGES:");
            foreach (var kv in BIMManagerEngine.RIBAStages)
                report.AppendLine($"    Stage {kv.Key}: {kv.Value}");
            report.AppendLine();

            report.AppendLine("  DOCUMENT NAMING:");
            report.AppendLine("    Project-Originator-Volume-Level-Type-Role-Class-Number");
            report.AppendLine("    Example: PRJ-ABC-ZZ-01-DR-A-Zz_99-0001");
            report.AppendLine();

            report.AppendLine("  BEP SECTIONS (ISO 19650-2 §5.3):");
            foreach (string s in BIMManagerEngine.BEPSections)
                report.AppendLine($"    {s}");

            TaskDialog.Show("STING BIM Manager — ISO 19650", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 16: Bulk Export All BIM Data ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BulkBIMExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            StingLog.Info("BIMManager: Bulk export starting...");
            string dir = BIMManagerEngine.GetBIMManagerFilePath(doc, "");
            int exports = 0;

            // 1. Generate BEP
            var bep = BIMManagerEngine.GenerateBEP(doc);
            BIMManagerEngine.SaveJsonFile(Path.Combine(dir, "project_bep.json"), bep);
            exports++;

            // 2. Project Dashboard
            var dashboard = BIMManagerEngine.BuildDashboard(doc);
            BIMManagerEngine.SaveJsonFile(Path.Combine(dir, "project_dashboard.json"), dashboard);
            exports++;

            // 3. COBie data
            var cobieData = BIMManagerEngine.BuildCOBieData(doc);
            int cobieRows = cobieData.Values.Sum(v => v.Count);
            string cobieDir = Path.Combine(dir, "COBie_V24");
            if (!Directory.Exists(cobieDir)) Directory.CreateDirectory(cobieDir);
            foreach (var ws in cobieData)
            {
                if (ws.Value.Count == 0) continue;
                var csv = new StringBuilder();
                var headers = ws.Value[0].Keys.ToArray();
                csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));
                foreach (var row in ws.Value)
                    csv.AppendLine(string.Join(",", headers.Select(h =>
                        $"\"{(row.ContainsKey(h) ? row[h]?.Replace("\"", "\"\"") : "")}\"")));
                try { File.WriteAllText(Path.Combine(cobieDir, $"COBie_{ws.Key}.csv"), csv.ToString()); }
                catch { }
            }
            exports++;

            // Summary
            var report = new StringBuilder();
            report.AppendLine("STING BIM Manager — Bulk Export");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  BEP:       Generated ({BIMManagerEngine.BEPSections.Length} sections)");
            report.AppendLine($"  Dashboard: Generated (RAG: {dashboard["rag_status"]})");
            report.AppendLine($"  COBie:     {cobieData.Count} worksheets, {cobieRows} rows");
            report.AppendLine();
            report.AppendLine($"  Output: {dir}");

            TaskDialog.Show("STING BIM Manager", report.ToString());
            StingLog.Info($"Bulk export: {exports} datasets to {dir}");
            return Result.Succeeded;
        }
    }

    #endregion
}
