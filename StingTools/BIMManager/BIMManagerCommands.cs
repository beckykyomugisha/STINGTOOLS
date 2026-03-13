using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING BIM MANAGER — ISO 19650 Project Management System
    //
    //  Architecture:
    //    BEP is a PRE-CONTRACT document (ISO 19650-2 §5.3). It defines HOW the
    //    project will be managed BEFORE modelling starts. Therefore:
    //    - "Create BEP" generates from TEMPLATES + user input (project wizard)
    //    - "Update BEP" enriches an existing BEP with live model data
    //    - "Validate BEP" checks model compliance against BEP requirements
    //
    //  Data storage: JSON files in STING_BIM_MANAGER/ alongside .rvt file
    //  All data is portable and version-controllable.
    //
    //  Briefcase: In-Revit reference document viewer inspired by Procore Briefcase
    //  and Ideate Sticky. Allows users to access, read, and print reference
    //  documents (BEP, standards, specs) without leaving Revit.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: BIMManagerEngine ──

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

        // ── Document Types (BS EN ISO 19650 + 2021 UK NA) ──
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
            ["MO"] = "Model (2021 NA)",
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

        // ── Issue Status Codes ──
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
            ["RFI"]      = "Request for Information",
            ["CLASH"]    = "Coordination Clash",
            ["DESIGN"]   = "Design Issue/Query",
            ["SITE"]     = "Site Observation",
            ["NCR"]      = "Non-Conformance Report",
            ["SNAGGING"] = "Snagging/Defect",
            ["CHANGE"]   = "Change Request",
            ["RISK"]     = "Risk Item",
            ["ACTION"]   = "Action Item",
            ["COMMENT"]  = "General Comment"
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

        // ── BEP Section Definitions (ISO 19650-2 §5.3 / UK BIM Framework / PAS 1192-2) ──
        internal static readonly string[] BEPSections = new[]
        {
            "1. Introduction and Document Control",
            "2. Project Information",
            "3. BIM Goals and Uses",
            "4. Information Requirements (OIR/PIR/AIR/EIR Response)",
            "5. Roles, Responsibilities and Authorities (RACI)",
            "6. Project Implementation Plan (PIP)",
            "7. Information Delivery Strategy (Federation/Volumes)",
            "8. TIDP and MIDP (Delivery Plans and Schedule)",
            "9. Standards, Methods and Procedures (SMP)",
            "10. Level of Information Need (LOD/LOI/LOA)",
            "11. Common Data Environment (CDE) Configuration",
            "12. Collaboration Procedures (Clash/RFI/Change)",
            "13. Quality Assurance and Quality Control",
            "14. Technology and Software Schedule",
            "15. Health and Safety / CDM 2015 Compliance",
            "16. Security (ISO 19650-5)",
            "17. Deliverables Matrix (by Stage)",
            "18. Asset Management and COBie Data Drops",
            "19. Training and Competency Plan",
            "20. Risk Register (BIM-Specific Risks)",
            "21. Appendices (EIR Matrix, Model Element Table, Templates)"
        };

        // ── BEP Template Presets ──
        internal static readonly Dictionary<string, string> BEPPresets = new Dictionary<string, string>
        {
            ["UK_GOV"]     = "UK Government Soft Landings — public sector, full ISO 19650",
            ["NBS_STANDARD"] = "NBS Standard BEP — commercial/mixed-use, Uniclass 2015",
            ["RESIDENTIAL"] = "Residential — housing, simplified deliverables",
            ["INFRASTRUCTURE"] = "Infrastructure — civil, rail, highways (PAS 1192-6)",
            ["FIT_OUT"]    = "Fit-Out / Refurbishment — existing building, measured survey",
            ["MINIMAL"]    = "Minimal — small project, essential sections only",
            ["CUSTOM"]     = "Custom — blank template, fill in all sections"
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
                "Description", "Owner", "Mitigation" },
            ["PickLists"] = new[] { "Name", "CreatedBy", "CreatedOn", "Category",
                "Value", "SheetName", "Description" }
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

        internal static string GetBIMManagerDir(Document doc)
        {
            string dir = GetProjectDataDir(doc);
            string bimDir = Path.Combine(dir, "STING_BIM_MANAGER");
            if (!Directory.Exists(bimDir))
                Directory.CreateDirectory(bimDir);
            return bimDir;
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
        //  BEP Generation — TEMPLATE-DRIVEN (Pre-Contract)
        //  BEP is written BEFORE modelling starts. It defines the
        //  project's information management approach.
        // ═══════════════════════════════════════════════════════════

        internal static JObject CreateBEPFromTemplate(string presetKey, string projectName,
            string projectNumber, string clientName, string projectAddress, int ribaStage,
            string leadDesigner, string leadDesignerCode, string[] disciplines)
        {
            var bep = new JObject();
            var now = DateTime.Now;
            bool isMinimal = presetKey == "MINIMAL";

            // ── Section 1: Project Information (user input, NOT model) ──
            var projInfo = new JObject
            {
                ["project_name"] = projectName,
                ["project_number"] = projectNumber,
                ["client_name"] = clientName,
                ["project_address"] = projectAddress,
                ["issue_date"] = now.ToString("yyyy-MM-dd"),
                ["bep_revision"] = "P01",
                ["bep_status"] = "S3",
                ["project_stage"] = ribaStage >= 0 && ribaStage <= 7
                    ? $"{ribaStage} — {RIBAStages[ribaStage]}" : "2 — Concept Design",
                ["bep_preset"] = presetKey,
                ["preset_description"] = BEPPresets.ContainsKey(presetKey) ? BEPPresets[presetKey] : ""
            };
            bep["project_information"] = projInfo;

            // ── Section 2: Project Team Directory ──
            var team = new JArray();
            if (!string.IsNullOrEmpty(leadDesigner))
            {
                team.Add(CreateTeamMember("Lead Appointed Party", leadDesignerCode, leadDesigner, "", "", ""));
            }
            // Add team slots for each discipline
            if (disciplines != null)
            {
                foreach (string disc in disciplines)
                {
                    string role = RoleCodes.ContainsKey(disc) ? RoleCodes[disc] : disc;
                    team.Add(CreateTeamMember(role, disc, role, "", "", ""));
                }
            }
            // Always include client and contractor
            team.Add(CreateTeamMember("Client/Employer", "K", "Client Representative", "", "", ""));
            team.Add(CreateTeamMember("Contractor", "W", "Contractor", "", "", ""));
            bep["project_team"] = team;

            // ── Section 3: Project Goals and BIM Uses ──
            var goals = new JObject();
            var primaryGoals = new JArray(
                "ISO 19650 compliant information management",
                "Coordinated 3D model for clash detection",
                "Automated asset tagging (STING ISO tags)"
            );
            if (presetKey == "UK_GOV")
            {
                primaryGoals.Add("Government Soft Landings (GSL) compliance");
                primaryGoals.Add("Digital twin readiness for operational phase");
            }
            if (presetKey != "MINIMAL")
            {
                primaryGoals.Add("COBie V2.4 data drop for FM handover");
                primaryGoals.Add("Quantity extraction for cost management");
            }
            goals["primary_goals"] = primaryGoals;

            var bimUses = new JArray(
                new JObject { ["use"] = "Design Authoring", ["stage"] = "2-4", ["responsible"] = "All disciplines" },
                new JObject { ["use"] = "3D Coordination", ["stage"] = "3-4", ["responsible"] = "BIM Coordinator" },
                new JObject { ["use"] = "Clash Detection", ["stage"] = "3-5", ["responsible"] = "BIM Coordinator" }
            );
            if (!isMinimal)
            {
                bimUses.Add(new JObject { ["use"] = "Quantity Takeoff", ["stage"] = "3-5", ["responsible"] = "QS" });
                bimUses.Add(new JObject { ["use"] = "Asset Management", ["stage"] = "5-7", ["responsible"] = "FM Team" });
                bimUses.Add(new JObject { ["use"] = "Record Model (As-Built)", ["stage"] = "6", ["responsible"] = "Contractor" });
                bimUses.Add(new JObject { ["use"] = "Facility Management", ["stage"] = "7", ["responsible"] = "FM Team" });
            }
            goals["bim_uses"] = bimUses;
            bep["goals_and_uses"] = goals;

            // ── Section 4: Roles and Responsibilities (RACI) ──
            var roles = new JObject();
            roles["information_manager"] = new JObject
            {
                ["role"] = "Information Manager (ISO 19650)",
                ["raci"] = "Accountable",
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
                ["raci"] = "Responsible",
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
                ["raci"] = "Responsible",
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
                "2. STING AutoTag applied (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ)",
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
            for (int s = Math.Max(0, ribaStage); s <= 7; s++)
            {
                var milestone = new JObject
                {
                    ["stage"] = s,
                    ["stage_name"] = RIBAStages.ContainsKey(s) ? RIBAStages[s] : $"Stage {s}",
                    ["target_date"] = "",
                    ["responsibility"] = "",
                    ["suitability"] = s <= 2 ? "S3" : (s <= 4 ? "S4" : "S6")
                };
                // Pre-populate deliverables based on stage
                var deliverables = new JArray();
                switch (s)
                {
                    case 0: deliverables.Add("Strategic brief"); deliverables.Add("Feasibility study"); break;
                    case 1: deliverables.Add("Project brief"); deliverables.Add("EIR"); deliverables.Add("BEP"); break;
                    case 2: deliverables.Add("Concept design models (LOD 200)"); deliverables.Add("Design option studies"); break;
                    case 3: deliverables.Add("Coordinated models (LOD 300)"); deliverables.Add("Clash reports"); deliverables.Add("Updated BEP"); break;
                    case 4: deliverables.Add("Technical design models (LOD 350)"); deliverables.Add("Specifications"); deliverables.Add("DD2 COBie"); break;
                    case 5: deliverables.Add("Construction models (LOD 400)"); deliverables.Add("Shop drawings"); deliverables.Add("DD3 COBie"); break;
                    case 6: deliverables.Add("As-built model (LOD 500)"); deliverables.Add("O&M manuals"); deliverables.Add("DD4 COBie"); deliverables.Add("FM Handover"); break;
                    case 7: deliverables.Add("AIM (Asset Information Model)"); deliverables.Add("Digital twin baseline"); break;
                }
                milestone["deliverables"] = deliverables;
                midp.Add(milestone);
            }
            bep["midp"] = midp;

            // ── Section 7: Information Standard (TIDP) ──
            var tidp = new JObject
            {
                ["naming_convention"] = "BS EN ISO 19650: Project-Originator-Volume-Level-Type-Role-Class-Number",
                ["naming_separator"] = "-",
                ["tag_format"] = $"{ParamRegistry.Separator} separated, {ParamRegistry.NumPad}-digit SEQ",
                ["tag_segments"] = string.Join(ParamRegistry.Separator, ParamRegistry.SegmentOrder),
                ["classification_system"] = "Uniclass 2015",
                ["units"] = new JObject { ["length"] = "Millimeters", ["area"] = "Square Meters", ["volume"] = "Cubic Meters" },
                ["file_formats"] = new JObject
                {
                    ["native"] = "Autodesk Revit (.rvt)",
                    ["exchange"] = "IFC 4 / IFC 2x3",
                    ["drawings"] = "PDF/A-1b",
                    ["data"] = "COBie V2.4 (XLSX)"
                }
            };
            bep["information_standard"] = tidp;

            // ── Section 8: Software Platforms ──
            var software = new JArray
            {
                new JObject { ["platform"] = "Autodesk Revit 2025+", ["purpose"] = "BIM Authoring", ["version"] = "" },
                new JObject { ["platform"] = "STING Tools", ["purpose"] = "ISO 19650 Asset Tagging & BIM Management", ["version"] = "v9.6" },
                new JObject { ["platform"] = "Navisworks Manage", ["purpose"] = "Federated model review & clash detection", ["version"] = "" },
                new JObject { ["platform"] = "IFC 4 / IFC 2x3", ["purpose"] = "Open BIM data exchange", ["version"] = "" }
            };
            bep["software_platforms"] = software;

            // ── Section 9: Model Structure ──
            var modelStructure = new JObject
            {
                ["shared_coordinates"] = "Project base point at site origin",
                ["model_origin"] = "Shared Coordinates aligned to survey grid",
                ["federation_strategy"] = "One model per discipline, federated in Navisworks",
                ["workset_strategy"] = "STING standard worksets (35 worksets per STING Template)"
            };
            bep["model_structure"] = modelStructure;

            // ── Section 10: CDE and Naming ──
            var cde = new JObject
            {
                ["cde_platform"] = "To be confirmed",
                ["containers"] = new JObject
                {
                    ["WIP"] = "Work In Progress — authoring environment",
                    ["SHARED"] = "Shared — for coordination and review",
                    ["PUBLISHED"] = "Published — approved for downstream use",
                    ["ARCHIVE"] = "Archive — superseded or historical"
                },
                ["suitability_codes"] = JObject.FromObject(SuitabilityCodes),
                ["revision_format"] = "P01, P02... (Preliminary) → C01, C02... (Construction)"
            };
            bep["cde_workflow"] = cde;

            // ── Section 11: Level of Information Need ──
            var loin = new JObject
            {
                ["lod_by_stage"] = new JObject
                {
                    ["Stage_2"] = "LOD 200 — Approximate geometry, generic types",
                    ["Stage_3"] = "LOD 300 — Specific geometry, accurate dimensions",
                    ["Stage_4"] = "LOD 350 — Detailed geometry, connections, supports",
                    ["Stage_5"] = "LOD 400 — Fabrication-ready, shop drawings",
                    ["Stage_6"] = "LOD 500 — As-built, verified field conditions"
                },
                ["loi_requirements"] = new JObject
                {
                    ["Stage_2"] = "Basic identity + STING DISC/LOC/ZONE tags",
                    ["Stage_3"] = "Full 8-segment ISO tags + system classification",
                    ["Stage_4"] = "Technical parameters (flow, power, pressure) + manufacturer",
                    ["Stage_5"] = "Serial numbers, installation dates, warranty",
                    ["Stage_6"] = "As-built verification, O&M data, COBie populated"
                }
            };
            bep["level_of_information_need"] = loin;

            // ── Section 12: Clash Detection Strategy ──
            var clash = new JObject
            {
                ["detection_tool"] = "STING Clash Detection + Navisworks Manage",
                ["tolerance_mm"] = 25,
                ["clash_groups"] = new JArray(
                    new JObject { ["test"] = "MEP vs Structure", ["priority"] = "HIGH", ["tolerance_mm"] = 25 },
                    new JObject { ["test"] = "HVAC vs Electrical", ["priority"] = "HIGH", ["tolerance_mm"] = 50 },
                    new JObject { ["test"] = "Plumbing vs Structure", ["priority"] = "HIGH", ["tolerance_mm"] = 25 },
                    new JObject { ["test"] = "Fire Protection vs All", ["priority"] = "CRITICAL", ["tolerance_mm"] = 10 },
                    new JObject { ["test"] = "Ceiling clearance", ["priority"] = "MEDIUM", ["tolerance_mm"] = 100 }
                ),
                ["resolution_workflow"] = "Issue raised → Assigned → Resolved → Verified → Closed"
            };
            bep["clash_detection"] = clash;

            // ── Section 13: QA and Compliance ──
            var qa = new JObject
            {
                ["model_audits"] = new JArray(
                    "STING Validate Tags — ISO 19650 token compliance",
                    "STING Pre-Tag Audit — predict issues before tagging",
                    "STING Template Validation — 45-check BIM template audit",
                    "STING BEP Compliance — validate against BEP allowed codes",
                    "STING Completeness Dashboard — per-discipline RAG status"
                ),
                ["tag_completeness_target"] = "95% of elements tagged with complete 8-segment ISO tags",
                ["audit_frequency"] = "Weekly automated audit + monthly manual review"
            };
            bep["quality_assurance"] = qa;

            // ── Section 14: Security ──
            var security = new JObject
            {
                ["access_levels"] = new JArray("Read-Only", "Contributor", "Reviewer", "Approver", "Admin"),
                ["model_protection"] = "Workset-based access control + central model ownership",
                ["classification"] = presetKey == "UK_GOV" ? "OFFICIAL" : "Commercial In Confidence"
            };
            bep["security"] = security;

            // ── Section 15: Handover ──
            var handover = new JObject
            {
                ["cobie_version"] = "COBie V2.4 (UK)",
                ["data_drops"] = new JArray(
                    new JObject { ["drop"] = "DD1", ["stage"] = "End of Stage 2", ["content"] = "Spatial data, room lists, basic types" },
                    new JObject { ["drop"] = "DD2", ["stage"] = "End of Stage 3", ["content"] = "Full asset register, system classifications" },
                    new JObject { ["drop"] = "DD3", ["stage"] = "End of Stage 4", ["content"] = "Technical data, manufacturer info, specifications" },
                    new JObject { ["drop"] = "DD4", ["stage"] = "End of Stage 6", ["content"] = "As-built data, serial numbers, warranty, O&M" }
                ),
                ["deliverables"] = new JArray(
                    "Native Revit model (.rvt) with STING tags",
                    "IFC 4 export with STING property sets",
                    "COBie V2.4 spreadsheet (STING FM Handover)",
                    "Asset Register CSV (STING Tag Register Export)",
                    "Bill of Quantities XLSX (STING BOQ Export)",
                    "FM O&M Handover Manual (STING Handover Manual)"
                )
            };
            bep["handover_requirements"] = handover;

            // ── Section 16: Risk Register ──
            var risks = new JArray(
                new JObject { ["risk"] = "Late model delivery", ["impact"] = "HIGH", ["mitigation"] = "MIDP milestone tracking in STING" },
                new JObject { ["risk"] = "Coordination clashes", ["impact"] = "HIGH", ["mitigation"] = "Weekly clash detection via STING" },
                new JObject { ["risk"] = "Incomplete asset data", ["impact"] = "MEDIUM", ["mitigation"] = "STING Completeness Dashboard + Pre-Tag Audit" },
                new JObject { ["risk"] = "Non-compliant naming", ["impact"] = "MEDIUM", ["mitigation"] = "STING Doc Naming Validator" },
                new JObject { ["risk"] = "Data loss/corruption", ["impact"] = "CRITICAL", ["mitigation"] = "Central model + backup schedule" }
            );
            bep["risk_register"] = risks;

            // ── Allowed Codes (for BEP Validation) ──
            var allowedCodes = new JObject
            {
                ["allowed_disc"] = new JArray(TagConfig.DiscMap.Values.Distinct().OrderBy(v => v)),
                ["allowed_loc"] = new JArray(TagConfig.LocCodes),
                ["allowed_zone"] = new JArray(TagConfig.ZoneCodes),
                ["allowed_sys"] = new JArray(TagConfig.SysMap.Keys.OrderBy(k => k))
            };
            bep["allowed_codes"] = allowedCodes;

            // ── Metadata ──
            bep["_metadata"] = new JObject
            {
                ["generated_by"] = "STING BIM Manager v2.0",
                ["generated_date"] = now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["iso_standard"] = "BS EN ISO 19650-1:2018 / BS EN ISO 19650-2:2018",
                ["sting_version"] = "v9.6",
                ["bep_type"] = "pre_contract"
            };

            return bep;
        }

        /// <summary>
        /// Create BEP from wizard data — richer overload that feeds wizard inputs into template.
        /// </summary>
        internal static JObject CreateBEPFromWizard(BEPWizardData wd)
        {
            string leadDesigner = wd.LeadDesignerDescription;
            string leadCode = wd.LeadDesignerRole == "Multi" ? "A" : wd.LeadDesignerRole;
            string[] disciplines = wd.DisciplineTeam.Keys.ToArray();

            var bep = CreateBEPFromTemplate(
                wd.PresetKey, wd.ProjectName, wd.ProjectNumber,
                wd.ClientName, wd.ProjectAddress, wd.RIBAStage,
                leadDesigner, leadCode, disciplines);

            // Enrich with wizard-specific fields not in the basic template
            var pi = bep["project_information"] as JObject;
            if (pi != null)
            {
                pi["project_description"] = wd.ProjectDescription;
                pi["site_reference"] = wd.SiteReference;
                pi["project_type"] = wd.ProjectType;
                pi["procurement_route"] = wd.ProcurementRoute;
            }

            // Enrich team with company/contact details
            var team = bep["project_team"] as JArray;
            if (team != null)
            {
                // Update lead party
                foreach (var member in team)
                {
                    string code = member["originator_code"]?.ToString() ?? "";
                    if (code == leadCode && member["role"]?.ToString()?.Contains("Lead") == true)
                    {
                        member["contact_name"] = wd.LeadContact;
                        member["company"] = wd.LeadCompany;
                    }
                    if (wd.DisciplineTeam.TryGetValue(code, out string company) && !string.IsNullOrEmpty(company))
                        member["company"] = company;
                }
                // Add BIM Manager if specified
                if (!string.IsNullOrEmpty(wd.BIMManager))
                {
                    team.Add(CreateTeamMember("Information Manager", "Z", "BIM Management",
                        wd.BIMManager, "", ""));
                }
                if (!string.IsNullOrEmpty(wd.BIMCoordinator))
                {
                    team.Add(CreateTeamMember("BIM Coordinator", "Z", "BIM Coordination",
                        wd.BIMCoordinator, "", ""));
                }
            }

            // Override BIM uses from wizard selections
            if (wd.BIMUses.Count > 0)
            {
                var goals = bep["goals_and_uses"] as JObject ?? new JObject();
                var bimUses = new JArray();
                foreach (string use in wd.BIMUses)
                    bimUses.Add(new JObject { ["use"] = use, ["stage"] = "All", ["responsible"] = "As per RACI" });
                goals["bim_uses"] = bimUses;
                if (!string.IsNullOrEmpty(wd.AdditionalGoals))
                {
                    var pg = goals["primary_goals"] as JArray ?? new JArray();
                    pg.Add(wd.AdditionalGoals);
                    goals["primary_goals"] = pg;
                }
                bep["goals_and_uses"] = goals;
            }

            // Override standards/CDE from wizard
            var tidp = bep["information_standard"] as JObject ?? new JObject();
            tidp["naming_convention"] = wd.NamingConvention;
            tidp["classification_system"] = wd.ClassificationSystem;
            tidp["units"] = new JObject { ["length"] = wd.UnitsLength, ["area"] = wd.UnitsArea };
            tidp["file_formats"] = new JObject
            {
                ["native"] = wd.FormatNative,
                ["exchange"] = wd.FormatExchange,
                ["drawings"] = wd.FormatDrawing,
                ["data"] = wd.FormatData
            };
            bep["information_standard"] = tidp;

            var cde = bep["cde_workflow"] as JObject ?? new JObject();
            cde["cde_platform"] = wd.CDEPlatform;
            bep["cde_workflow"] = cde;

            return bep;
        }

        /// <summary>
        /// Export BEP to formatted XLSX workbook (ISO 19650 standard format).
        /// </summary>
        internal static string ExportBEPToXlsx(JObject bep, string outputDir, string projectNumber)
        {
            string fileName = $"BEP_{projectNumber}_{DateTime.Now:yyyyMMdd}.xlsx";
            string xlsxPath = Path.Combine(outputDir, fileName);

            using var wb = new XLWorkbook();

            // ── Cover Page ──
            var cover = wb.AddWorksheet("Cover Page");
            cover.Column(1).Width = 5;
            cover.Column(2).Width = 30;
            cover.Column(3).Width = 40;
            int r = 2;
            cover.Cell(r, 2).Value = "BIM EXECUTION PLAN (BEP)";
            cover.Cell(r, 2).Style.Font.Bold = true;
            cover.Cell(r, 2).Style.Font.FontSize = 18;
            cover.Range(r, 2, r, 3).Merge();
            r++;
            cover.Cell(r, 2).Value = "ISO 19650-2 Pre-Contract BEP";
            cover.Cell(r, 2).Style.Font.FontSize = 12;
            cover.Cell(r, 2).Style.Font.FontColor = XLColor.Gray;
            r += 2;

            var pi = bep["project_information"] as JObject;
            if (pi != null)
            {
                foreach (var kv in pi)
                {
                    cover.Cell(r, 2).Value = FormatKey(kv.Key);
                    cover.Cell(r, 2).Style.Font.Bold = true;
                    cover.Cell(r, 3).Value = kv.Value?.ToString() ?? "";
                    r++;
                }
            }
            r += 2;
            cover.Cell(r, 2).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
            cover.Cell(r, 2).Style.Font.FontColor = XLColor.Gray;
            cover.PageSetup.PaperSize = XLPaperSize.A4Paper;

            // ── Section 2: Project Team ──
            var teamWs = wb.AddWorksheet("Project Team");
            SetStandardHeaders(teamWs, new[] { "Role", "Code", "Discipline", "Contact", "Company", "Email" });
            int tr = 2;
            var team = bep["project_team"] as JArray;
            if (team != null)
            {
                foreach (var m in team)
                {
                    teamWs.Cell(tr, 1).Value = m["role"]?.ToString() ?? "";
                    teamWs.Cell(tr, 2).Value = m["originator_code"]?.ToString() ?? "";
                    teamWs.Cell(tr, 3).Value = m["discipline"]?.ToString() ?? "";
                    teamWs.Cell(tr, 4).Value = m["contact_name"]?.ToString() ?? "";
                    teamWs.Cell(tr, 5).Value = m["company"]?.ToString() ?? "";
                    teamWs.Cell(tr, 6).Value = m["email"]?.ToString() ?? "";
                    tr++;
                }
            }

            // ── Section 3-5: Key Sections as formatted sheets ──
            WriteSectionSheet(wb, "Goals & BIM Uses", bep["goals_and_uses"]);
            WriteSectionSheet(wb, "Roles & Responsibilities", bep["roles_and_responsibilities"]);
            WriteSectionSheet(wb, "Process Design", bep["process_design"]);

            // ── MIDP ──
            var midpWs = wb.AddWorksheet("MIDP");
            SetStandardHeaders(midpWs, new[] { "Stage", "Stage Name", "Target Date", "Responsibility", "Suitability", "Deliverables" });
            int mr = 2;
            var midp = bep["midp"] as JArray;
            if (midp != null)
            {
                foreach (var ms in midp)
                {
                    midpWs.Cell(mr, 1).Value = (int)(ms["stage"] ?? 0);
                    midpWs.Cell(mr, 2).Value = ms["stage_name"]?.ToString() ?? "";
                    midpWs.Cell(mr, 3).Value = ms["target_date"]?.ToString() ?? "";
                    midpWs.Cell(mr, 4).Value = ms["responsibility"]?.ToString() ?? "";
                    midpWs.Cell(mr, 5).Value = ms["suitability"]?.ToString() ?? "";
                    var deliverables = ms["deliverables"] as JArray;
                    midpWs.Cell(mr, 6).Value = deliverables != null
                        ? string.Join("\n", deliverables.Select(d => d.ToString()))
                        : "";
                    midpWs.Cell(mr, 6).Style.Alignment.WrapText = true;
                    mr++;
                }
            }

            // ── Information Standard ──
            WriteSectionSheet(wb, "Information Standard", bep["information_standard"]);

            // ── Software Platforms ──
            var swWs = wb.AddWorksheet("Software Platforms");
            SetStandardHeaders(swWs, new[] { "Platform", "Purpose", "Version" });
            int sr = 2;
            var sw = bep["software_platforms"] as JArray;
            if (sw != null)
            {
                foreach (var s in sw)
                {
                    swWs.Cell(sr, 1).Value = s["platform"]?.ToString() ?? "";
                    swWs.Cell(sr, 2).Value = s["purpose"]?.ToString() ?? "";
                    swWs.Cell(sr, 3).Value = s["version"]?.ToString() ?? "";
                    sr++;
                }
            }

            // ── Remaining key sections ──
            WriteSectionSheet(wb, "Model Structure", bep["model_structure"]);
            WriteSectionSheet(wb, "CDE Workflow", bep["cde_workflow"]);
            WriteSectionSheet(wb, "Level of Info Need", bep["level_of_information_need"]);
            WriteSectionSheet(wb, "Clash Detection", bep["clash_detection"]);
            WriteSectionSheet(wb, "Quality Assurance", bep["quality_assurance"]);
            WriteSectionSheet(wb, "Security", bep["security"]);
            WriteSectionSheet(wb, "Handover Requirements", bep["handover_requirements"]);

            // ── Risk Register ──
            var riskWs = wb.AddWorksheet("Risk Register");
            SetStandardHeaders(riskWs, new[] { "Risk", "Impact", "Mitigation" });
            int rr = 2;
            var risks = bep["risk_register"] as JArray;
            if (risks != null)
            {
                foreach (var risk in risks)
                {
                    riskWs.Cell(rr, 1).Value = risk["risk"]?.ToString() ?? "";
                    riskWs.Cell(rr, 2).Value = risk["impact"]?.ToString() ?? "";
                    riskWs.Cell(rr, 3).Value = risk["mitigation"]?.ToString() ?? "";
                    rr++;
                }
            }

            // ── Allowed Codes ──
            var codesWs = wb.AddWorksheet("Allowed Codes");
            int cr = 1;
            var codes = bep["allowed_codes"] as JObject;
            if (codes != null)
            {
                foreach (var kv in codes)
                {
                    codesWs.Cell(cr, 1).Value = FormatKey(kv.Key);
                    codesWs.Cell(cr, 1).Style.Font.Bold = true;
                    cr++;
                    if (kv.Value is JArray arr)
                    {
                        foreach (var item in arr)
                        {
                            codesWs.Cell(cr, 2).Value = item.ToString();
                            cr++;
                        }
                    }
                    cr++;
                }
            }

            // Auto-fit all worksheets
            foreach (var ws in wb.Worksheets)
            {
                ws.Columns().AdjustToContents(1, 80);
                ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            }

            wb.SaveAs(xlsxPath);
            return xlsxPath;
        }

        private static void SetStandardHeaders(IXLWorksheet ws, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
                ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#6A1B9A");
                ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
            }
        }

        private static void WriteSectionSheet(XLWorkbook wb, string sheetName, JToken section)
        {
            if (section == null) return;
            // Sanitize sheet name (max 31 chars, no invalid chars)
            string safeName = sheetName.Length > 31 ? sheetName.Substring(0, 31) : sheetName;
            var ws = wb.AddWorksheet(safeName);
            int row = 1;

            if (section is JObject obj)
            {
                foreach (var kv in obj)
                {
                    ws.Cell(row, 1).Value = FormatKey(kv.Key);
                    ws.Cell(row, 1).Style.Font.Bold = true;

                    if (kv.Value is JArray arr)
                    {
                        foreach (var item in arr)
                        {
                            row++;
                            if (item is JObject itemObj)
                                ws.Cell(row, 2).Value = string.Join(" | ",
                                    itemObj.Properties().Select(p => $"{p.Name}: {p.Value}"));
                            else
                                ws.Cell(row, 2).Value = item.ToString();
                        }
                    }
                    else if (kv.Value is JObject nested)
                    {
                        foreach (var nkv in nested)
                        {
                            row++;
                            ws.Cell(row, 2).Value = FormatKey(nkv.Key);
                            ws.Cell(row, 3).Value = nkv.Value?.ToString() ?? "";
                        }
                    }
                    else
                    {
                        ws.Cell(row, 2).Value = kv.Value?.ToString() ?? "";
                    }
                    row++;
                }
            }
            else if (section is JArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JObject itemObj)
                    {
                        ws.Cell(row, 1).Value = string.Join(" | ",
                            itemObj.Properties().Select(p => $"{p.Name}: {p.Value}"));
                    }
                    else
                        ws.Cell(row, 1).Value = item.ToString();
                    row++;
                }
            }
        }

        private static string FormatKey(string key)
        {
            // Convert snake_case to Title Case
            return string.Join(" ", key.Split('_')
                .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : ""));
        }

        /// <summary>
        /// Update an existing BEP with live model data (post-modelling enrichment).
        /// This is NOT generation — it validates and supplements an existing BEP.
        /// </summary>
        internal static JObject UpdateBEPFromModel(Document doc, JObject existingBep)
        {
            var updated = (JObject)existingBep.DeepClone();
            var now = DateTime.Now;

            // Update revision
            string currentRev = updated["project_information"]?["bep_revision"]?.ToString() ?? "P01";
            updated["project_information"]["bep_revision"] = IncrementRevision(currentRev);
            updated["project_information"]["last_updated"] = now.ToString("yyyy-MM-dd");

            // Enrich with model statistics
            var modelData = new JObject();

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            modelData["number_of_levels"] = levels.Count;
            if (levels.Count > 0)
            {
                modelData["lowest_level"] = levels.First().Name;
                modelData["highest_level"] = levels.Last().Name;
                modelData["building_height_m"] = Math.Round((levels.Last().Elevation - levels.First().Elevation) * 0.3048, 1);
            }

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().ToList();
            double totalAreaSqM = 0;
            foreach (var room in rooms)
            {
                var areaParam = room.get_Parameter(BuiltInParameter.ROOM_AREA);
                if (areaParam != null) totalAreaSqM += areaParam.AsDouble() * 0.092903;
            }
            modelData["number_of_rooms"] = rooms.Count;
            modelData["gross_internal_area_m2"] = Math.Round(totalAreaSqM, 1);

            // Worksets
            if (doc.IsWorkshared)
            {
                var worksets = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset).ToWorksets()
                    .Select(ws => ws.Name).ToList();
                modelData["worksets"] = new JArray(worksets);
            }

            // Linked models
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>()
                .Select(lt => lt.Name).ToList();
            modelData["linked_models"] = new JArray(links);

            // Software version
            modelData["revit_version"] = doc.Application.VersionName;

            updated["model_data"] = modelData;

            // Update metadata
            updated["_metadata"]["last_enriched"] = now.ToString("yyyy-MM-dd HH:mm:ss");
            updated["_metadata"]["bep_type"] = "enriched";

            return updated;
        }

        private static string IncrementRevision(string rev)
        {
            if (string.IsNullOrEmpty(rev) || rev.Length < 2) return "P02";
            string prefix = rev.Substring(0, 1);
            if (int.TryParse(rev.Substring(1), out int num))
                return $"{prefix}{(num + 1):D2}";
            return "P02";
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
            string originator, string suitability, string cdeStatus, string direction)
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
                ["cde_status"] = cdeStatus,
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

        internal static string GetNextIssueId(JArray issues, string type)
        {
            int max = 0;
            string prefix = type + "-";
            foreach (var issue in issues)
            {
                string id = issue["issue_id"]?.ToString() ?? "";
                if (id.StartsWith(prefix))
                {
                    string numPart = id.Substring(prefix.Length);
                    if (int.TryParse(numPart, out int n) && n > max) max = n;
                }
            }
            return $"{type}-{(max + 1):D4}";
        }

        internal static JObject CreateIssue(string issueId, string issueType, string priority,
            string title, string description, string assignedTo, string discipline,
            ICollection<ElementId> elementIds, string viewName)
        {
            return new JObject
            {
                ["issue_id"] = issueId,
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
                ["date_due"] = priority == "CRITICAL" ? DateTime.Now.AddDays(1).ToString("yyyy-MM-dd") :
                               priority == "HIGH" ? DateTime.Now.AddDays(3).ToString("yyyy-MM-dd") :
                               priority == "MEDIUM" ? DateTime.Now.AddDays(7).ToString("yyyy-MM-dd") :
                               DateTime.Now.AddDays(14).ToString("yyyy-MM-dd"),
                ["date_closed"] = "",
                ["response"] = "",
                ["element_ids"] = new JArray(elementIds?.Select(id => id.Value.ToString()) ?? Enumerable.Empty<string>()),
                ["view_name"] = viewName ?? "",
                ["revision"] = "P01",
                ["comments"] = new JArray()
            };
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

            // ── Contact (from project_bep.json team data + fallback) ──
            var contacts = new List<Dictionary<string, string>>();
            string bepPath = GetBIMManagerFilePath(doc, "project_bep.json");
            var bep = LoadJsonFile(bepPath);
            var teamMembers = bep["team_members"] as JArray ?? bep["project_team"] as JArray;
            if (teamMembers != null && teamMembers.Count > 0)
            {
                foreach (var member in teamMembers)
                {
                    contacts.Add(new Dictionary<string, string>
                    {
                        ["Email"] = member["email"]?.ToString() ?? "",
                        ["Company"] = member["organization"]?.ToString() ?? member["company"]?.ToString() ?? "",
                        ["Phone"] = member["phone"]?.ToString() ?? "",
                        ["Department"] = member["department"]?.ToString() ?? "",
                        ["OrganizationCode"] = member["org_code"]?.ToString() ?? member["originator"]?.ToString() ?? "",
                        ["GivenName"] = member["given_name"]?.ToString() ?? member["name"]?.ToString() ?? "",
                        ["FamilyName"] = member["family_name"]?.ToString() ?? "",
                        ["Category"] = member["role"]?.ToString() ?? member["category"]?.ToString() ?? "Team Member",
                        ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn
                    });
                }
            }
            // Always ensure at least the current user is present
            if (contacts.Count == 0)
            {
                contacts.Add(new Dictionary<string, string>
                {
                    ["Email"] = "", ["Company"] = pi?.ClientName ?? "", ["Phone"] = "",
                    ["Department"] = "", ["OrganizationCode"] = "",
                    ["GivenName"] = createdBy, ["FamilyName"] = "",
                    ["Category"] = "Facility Manager", ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn
                });
            }
            data["Contact"] = contacts;

            // ── Facility ──
            data["Facility"] = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    ["Name"] = pi?.Name ?? "Unnamed Facility", ["CreatedBy"] = createdBy,
                    ["CreatedOn"] = createdOn, ["Category"] = "Facility",
                    ["ProjectName"] = pi?.Name ?? "", ["SiteName"] = pi?.Address ?? "",
                    ["LinearUnits"] = "millimeters", ["AreaUnits"] = "square meters",
                    ["VolumeUnits"] = "cubic meters", ["CurrencyUnit"] = "GBP",
                    ["AreaMeasurement"] = "NRM", ["Description"] = pi?.Name ?? "",
                    ["Phase"] = "New Construction"
                }
            };

            // ── Floor ──
            var floors = new List<Dictionary<string, string>>();
            foreach (var level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            {
                floors.Add(new Dictionary<string, string>
                {
                    ["Name"] = level.Name, ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = "Floor", ["ExternalSystem"] = "Revit", ["ExternalObject"] = "Level",
                    ["ExternalIdentifier"] = level.UniqueId, ["Description"] = level.Name,
                    ["Elevation"] = Math.Round(level.Elevation * 304.8, 0).ToString(), ["Height"] = ""
                });
            }
            data["Floor"] = floors;

            // ── Space (from Rooms) ──
            var spaces = new List<Dictionary<string, string>>();
            foreach (var el in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType())
            {
                var room = el as Room;
                if (room == null || room.Area <= 0) continue;
                spaces.Add(new Dictionary<string, string>
                {
                    ["Name"] = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name,
                    ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "Room",
                    ["FloorName"] = room.Level?.Name ?? "",
                    ["Description"] = $"{room.Name} (#{room.Number})",
                    ["ExternalSystem"] = "Revit", ["ExternalObject"] = "Room",
                    ["ExternalIdentifier"] = room.UniqueId,
                    ["RoomTag"] = room.Number ?? "",
                    ["GrossArea"] = Math.Round(room.Area * 0.092903, 2).ToString(),
                    ["NetArea"] = Math.Round(room.Area * 0.092903, 2).ToString()
                });
            }
            data["Space"] = spaces;

            // ── Zone (from Room Departments) ──
            var zones = new List<Dictionary<string, string>>();
            foreach (var dept in spaces.GroupBy(s => s.ContainsKey("Category") ? s["Category"] : "Unassigned"))
            {
                zones.Add(new Dictionary<string, string>
                {
                    ["Name"] = dept.Key, ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = "Occupancy Zone",
                    ["SpaceNames"] = string.Join(",", dept.Select(s => s["Name"]).Take(20)),
                    ["ExternalSystem"] = "Revit", ["ExternalObject"] = "Zone",
                    ["Description"] = $"Zone: {dept.Key} ({dept.Count()} spaces)"
                });
            }
            data["Zone"] = zones;

            // ── Type (from FamilySymbol) ──
            var types = new List<Dictionary<string, string>>();
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
            foreach (var fs in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && knownCats.Contains(fs.Category.Name))
                .GroupBy(fs => fs.FamilyName + ": " + fs.Name).Select(g => g.First()))
            {
                types.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"{fs.FamilyName}: {fs.Name}", ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = fs.Category?.Name ?? "",
                    ["Description"] = ParameterHelpers.GetString(fs, "ASS_DESCRIPTION_TXT"),
                    ["AssetType"] = "Fixed",
                    ["Manufacturer"] = ParameterHelpers.GetString(fs, "ASS_MANUFACTURER_TXT"),
                    ["ModelNumber"] = ParameterHelpers.GetString(fs, "ASS_MODEL_TXT"),
                    ["ReplacementCost"] = ParameterHelpers.GetString(fs, "ASS_CST_UNIT_PRICE_UGX_NR"),
                    ["ExpectedLife"] = ParameterHelpers.GetString(fs, "ASS_EXPECTED_LIFE_YEARS_YRS"),
                    ["DurationUnit"] = "years", ["ModelReference"] = fs.Name
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
                    var t = doc.GetElement(typeId);
                    if (t is FamilySymbol fst) typeName = $"{fst.FamilyName}: {fst.Name}";
                    else if (t != null) typeName = t.Name;
                }

                // GAP-011: Map STING tag data to COBie Component fields
                string stingTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string assetId = !string.IsNullOrEmpty(stingTag) ? stingTag : tag;

                // Build friendly name: prefer description or family:type, fall back to tag
                string desc = ParameterHelpers.GetString(el, "ASS_DESCRIPTION_TXT");
                string friendlyName = !string.IsNullOrEmpty(desc) ? desc
                    : !string.IsNullOrEmpty(typeName) ? typeName
                    : tag;

                components.Add(new Dictionary<string, string>
                {
                    ["Name"] = friendlyName, ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["TypeName"] = typeName, ["Space"] = roomName,
                    ["Description"] = desc,
                    ["ExternalSystem"] = "Revit", ["ExternalObject"] = cat,
                    ["ExternalIdentifier"] = el.UniqueId,
                    ["TagNumber"] = assetId, ["AssetIdentifier"] = assetId,
                    ["Category"] = cat
                });
            }
            data["Component"] = components;

            // ── System ──
            var systems = new List<Dictionary<string, string>>();
            foreach (var sysCode in TagConfig.SysMap.Keys.OrderBy(k => k))
            {
                var sysComps = components.Where(c => c["Name"].Contains($"-{sysCode}-"))
                    .Select(c => c["Name"]).Take(20).ToList();
                if (sysComps.Count > 0)
                {
                    systems.Add(new Dictionary<string, string>
                    {
                        ["Name"] = sysCode, ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                        ["Category"] = sysCode, ["ComponentNames"] = string.Join(",", sysComps),
                        ["ExternalSystem"] = "STING", ["ExternalObject"] = "System",
                        ["ExternalIdentifier"] = sysCode,
                        ["Description"] = TagConfig.SysMap.ContainsKey(sysCode) ? string.Join(", ", TagConfig.SysMap[sysCode]) : sysCode
                    });
                }
            }
            data["System"] = systems;

            // ── Job (maintenance) ──
            var jobs = new List<Dictionary<string, string>>();
            var maintFreq = new Dictionary<string, (string f, string u)>
            {
                ["HVAC"] = ("6", "months"), ["DCW"] = ("12", "months"), ["DHW"] = ("6", "months"),
                ["HWS"] = ("6", "months"), ["SAN"] = ("12", "months"), ["GAS"] = ("6", "months"),
                ["FP"] = ("3", "months"), ["LV"] = ("12", "months"), ["FLS"] = ("3", "months"),
                ["LTG"] = ("12", "months"), ["ELC"] = ("12", "months")
            };
            foreach (var sys in systems)
            {
                string code = sys["Name"];
                (string f, string u) mf = maintFreq.ContainsKey(code) ? maintFreq[code] : ("12", "months");
                jobs.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"PPM-{code}", ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = "Preventive", ["Status"] = "Not Started", ["TypeName"] = code,
                    ["Description"] = $"Planned Preventive Maintenance for {code} systems",
                    ["Duration"] = "4", ["DurationUnit"] = "hours",
                    ["Frequency"] = mf.f, ["FrequencyUnit"] = mf.u, ["ResourceNames"] = "FM Technician"
                });
            }
            data["Job"] = jobs;

            // ── Document (from document_register.json + BEP fallback) ──
            var documents = new List<Dictionary<string, string>>();
            string docRegPath = GetBIMManagerFilePath(doc, "document_register.json");
            var docRegArray = LoadJsonArray(docRegPath);
            if (docRegArray.Count > 0)
            {
                foreach (var d in docRegArray)
                {
                    documents.Add(new Dictionary<string, string>
                    {
                        ["Name"] = d["name"]?.ToString() ?? d["document_name"]?.ToString() ?? "",
                        ["CreatedBy"] = d["created_by"]?.ToString() ?? createdBy,
                        ["CreatedOn"] = d["date"]?.ToString() ?? d["created_on"]?.ToString() ?? createdOn,
                        ["Category"] = d["type"]?.ToString() ?? d["category"]?.ToString() ?? "",
                        ["ApprovalBy"] = d["approved_by"]?.ToString() ?? "",
                        ["Stage"] = d["stage"]?.ToString() ?? "",
                        ["SheetName"] = "Document",
                        ["RowName"] = d["document_id"]?.ToString() ?? "",
                        ["Directory"] = d["directory"]?.ToString() ?? "",
                        ["File"] = d["file_name"]?.ToString() ?? d["file"]?.ToString() ?? "",
                        ["ExternalSystem"] = "STING",
                        ["ExternalObject"] = "DocumentRegister",
                        ["ExternalIdentifier"] = d["document_id"]?.ToString() ?? "",
                        ["Description"] = d["description"]?.ToString() ?? d["name"]?.ToString() ?? "",
                        ["Reference"] = d["reference"]?.ToString() ?? d["suitability"]?.ToString() ?? ""
                    });
                }
            }
            // Always include BEP reference
            documents.Add(new Dictionary<string, string>
            {
                ["Name"] = "BEP", ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                ["Category"] = "BIM Execution Plan", ["ApprovalBy"] = "",
                ["Stage"] = "Design", ["Description"] = "BIM Execution Plan (ISO 19650)",
                ["Reference"] = "project_bep.json"
            });
            data["Document"] = documents;

            // ── Assembly (compound wall/floor compositions) ──
            var assemblies = new List<Dictionary<string, string>>();
            foreach (var wallType in new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>())
            {
                CompoundStructure cs = wallType.GetCompoundStructure();
                if (cs == null || cs.LayerCount < 2) continue;
                var childNames = new List<string>();
                foreach (var layer in cs.GetLayers())
                {
                    var mat = doc.GetElement(layer.MaterialId);
                    string layerName = mat != null ? mat.Name : $"Layer ({layer.Width * 304.8:F0}mm)";
                    childNames.Add(layerName);
                }
                assemblies.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"Assembly-Wall-{wallType.Name}",
                    ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["SheetName"] = "Type", ["ParentName"] = $"Walls: {wallType.Name}",
                    ["ChildNames"] = string.Join(",", childNames),
                    ["AssemblyType"] = "Fixed", ["Description"] = $"Wall composition: {wallType.Name}"
                });
            }
            foreach (var floorType in new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>())
            {
                CompoundStructure cs = floorType.GetCompoundStructure();
                if (cs == null || cs.LayerCount < 2) continue;
                var childNames = new List<string>();
                foreach (var layer in cs.GetLayers())
                {
                    var mat = doc.GetElement(layer.MaterialId);
                    string layerName = mat != null ? mat.Name : $"Layer ({layer.Width * 304.8:F0}mm)";
                    childNames.Add(layerName);
                }
                assemblies.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"Assembly-Floor-{floorType.Name}",
                    ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["SheetName"] = "Type", ["ParentName"] = $"Floors: {floorType.Name}",
                    ["ChildNames"] = string.Join(",", childNames),
                    ["AssemblyType"] = "Fixed", ["Description"] = $"Floor composition: {floorType.Name}"
                });
            }
            data["Assembly"] = assemblies;

            // ── Connection (MEP system connections via Connector API) ──
            var connections = new List<Dictionary<string, string>>();
            int connIdx = 0;
            foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                ConnectorSet connectorSet = null;
                if (el is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
                    connectorSet = fi.MEPModel.ConnectorManager.Connectors;
                else if (el is MEPCurve mepCurve && mepCurve.ConnectorManager != null)
                    connectorSet = mepCurve.ConnectorManager.Connectors;

                if (connectorSet == null) continue;

                foreach (Connector conn in connectorSet)
                {
                    if (!conn.IsConnected) continue;
                    foreach (Connector other in conn.AllRefs)
                    {
                        if (other.Owner.Id.Value <= el.Id.Value) continue; // avoid duplicates
                        string elTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        string otherTag = ParameterHelpers.GetString(other.Owner, ParamRegistry.TAG1);
                        string name1 = !string.IsNullOrEmpty(elTag) ? elTag : el.Name;
                        string name2 = !string.IsNullOrEmpty(otherTag) ? otherTag : other.Owner.Name;

                        connections.Add(new Dictionary<string, string>
                        {
                            ["Name"] = $"Connection-{++connIdx}",
                            ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                            ["ConnectionType"] = conn.Domain.ToString(),
                            ["SheetName"] = "Component", ["RowName1"] = name1, ["RowName2"] = name2,
                            ["RealizingElement"] = "",
                            ["PortName1"] = $"{conn.Origin.X * 304.8:F0},{conn.Origin.Y * 304.8:F0},{conn.Origin.Z * 304.8:F0}",
                            ["PortName2"] = $"{other.Origin.X * 304.8:F0},{other.Origin.Y * 304.8:F0},{other.Origin.Z * 304.8:F0}",
                            ["ExternalSystem"] = "Revit", ["ExternalObject"] = "Connector",
                            ["ExternalIdentifier"] = $"{el.UniqueId}-{other.Owner.UniqueId}",
                            ["Description"] = $"{ParameterHelpers.GetCategoryName(el)} to {ParameterHelpers.GetCategoryName(other.Owner)}"
                        });
                    }
                }
            }
            data["Connection"] = connections;

            // ── Spare (basic spare parts from type data) ──
            var spares = new List<Dictionary<string, string>>();
            foreach (var fs in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && knownCats.Contains(fs.Category.Name))
                .GroupBy(fs => fs.FamilyName).Select(g => g.First()))
            {
                string manufacturer = ParameterHelpers.GetString(fs, "ASS_MANUFACTURER_TXT");
                string model = ParameterHelpers.GetString(fs, "ASS_MODEL_TXT");
                if (string.IsNullOrEmpty(manufacturer) && string.IsNullOrEmpty(model)) continue;
                spares.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"Spare-{fs.FamilyName}",
                    ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = fs.Category?.Name ?? "",
                    ["TypeName"] = $"{fs.FamilyName}: {fs.Name}",
                    ["Suppliers"] = manufacturer,
                    ["ExternalSystem"] = "Revit", ["ExternalObject"] = "FamilySymbol",
                    ["ExternalIdentifier"] = fs.UniqueId,
                    ["Description"] = $"Spare parts for {fs.FamilyName}",
                    ["SetNumber"] = "", ["PartNumber"] = model
                });
            }
            data["Spare"] = spares;

            // ── Resource (labour/tool resources from job definitions) ──
            var resources = new List<Dictionary<string, string>>();
            var resourceNames = new HashSet<string> { "FM Technician", "Electrical Engineer", "Mechanical Engineer", "Plumber", "Fire Safety Officer", "HVAC Specialist" };
            foreach (string rn in resourceNames)
            {
                resources.Add(new Dictionary<string, string>
                {
                    ["Name"] = rn, ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = "Labour",
                    ["ExternalSystem"] = "STING", ["ExternalObject"] = "Resource",
                    ["ExternalIdentifier"] = rn.Replace(" ", "_"),
                    ["Description"] = $"{rn} resource for planned maintenance"
                });
            }
            data["Resource"] = resources;

            // ── Impact (environmental data from materials) ──
            var impacts = new List<Dictionary<string, string>>();
            foreach (var matEl in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Take(200))
            {
                string thermalStr = "";
                try
                {
                    var thermalId = matEl.ThermalAssetId;
                    if (thermalId != ElementId.InvalidElementId)
                    {
                        var thermalAsset = doc.GetElement(thermalId) as PropertySetElement;
                        if (thermalAsset != null)
                            thermalStr = "Thermal asset present";
                    }
                }
                catch { /* thermal asset not available */ }

                impacts.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"Impact-{matEl.Name}",
                    ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["ImpactType"] = "Environment",
                    ["ImpactStage"] = "Operation",
                    ["SheetName"] = "Type", ["RowName"] = matEl.Name,
                    ["Value"] = thermalStr,
                    ["Unit"] = "",
                    ["LeadInTime"] = "", ["Duration"] = "", ["LeadOutTime"] = "",
                    ["ImpactUnit"] = "",
                    ["Description"] = $"Environmental impact data for material: {matEl.Name}"
                });
            }
            data["Impact"] = impacts;

            // ── Attribute (shared parameter export for tagged elements) ──
            var attributes = new List<Dictionary<string, string>>();
            var attrParamNames = new[] { ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD, ParamRegistry.SEQ,
                "ASS_MANUFACTURER_TXT", "ASS_MODEL_TXT", "ASS_DESCRIPTION_TXT",
                "ASS_EXPECTED_LIFE_YEARS_YRS", "ASS_CST_UNIT_PRICE_UGX_NR" };
            foreach (var el in components.Take(500))
            {
                string compName = el["Name"];
                string extId = el["ExternalIdentifier"];
                var revitEl = doc.GetElement(extId);
                if (revitEl == null) continue;
                foreach (string pName in attrParamNames)
                {
                    string val = ParameterHelpers.GetString(revitEl, pName);
                    if (string.IsNullOrEmpty(val)) continue;
                    attributes.Add(new Dictionary<string, string>
                    {
                        ["Name"] = pName, ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                        ["Category"] = "STING Parameter",
                        ["SheetName"] = "Component", ["RowName"] = compName,
                        ["Value"] = val, ["Unit"] = "",
                        ["ExternalSystem"] = "STING", ["ExternalObject"] = "SharedParameter",
                        ["ExternalIdentifier"] = pName,
                        ["Description"] = pName, ["AllowedValues"] = ""
                    });
                }
            }
            data["Attribute"] = attributes;

            // ── Coordinate (element XYZ from BoundingBox) ──
            var coordinates = new List<Dictionary<string, string>>();
            foreach (var comp in components)
            {
                string extId = comp["ExternalIdentifier"];
                var revitEl = doc.GetElement(extId);
                if (revitEl == null) continue;
                var bb = revitEl.get_BoundingBox(null);
                if (bb == null) continue;
                var center = (bb.Min + bb.Max) / 2.0;
                coordinates.Add(new Dictionary<string, string>
                {
                    ["Name"] = $"Coord-{comp["Name"]}",
                    ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = "Point",
                    ["SheetName"] = "Component", ["RowName"] = comp["Name"],
                    ["CoordinateXAxis"] = (center.X * 304.8).ToString("F1"),
                    ["CoordinateYAxis"] = (center.Y * 304.8).ToString("F1"),
                    ["CoordinateZAxis"] = (center.Z * 304.8).ToString("F1"),
                    ["ClockwiseRotation"] = "0", ["ElevationalRotation"] = "0", ["YawRotation"] = "0"
                });
            }
            data["Coordinate"] = coordinates;

            // ── Issue (from existing issues.json) ──
            var issuesList = new List<Dictionary<string, string>>();
            string issuesFilePath = GetBIMManagerFilePath(doc, "issues.json");
            var issuesArray = LoadJsonArray(issuesFilePath);
            foreach (var issue in issuesArray)
            {
                issuesList.Add(new Dictionary<string, string>
                {
                    ["Name"] = issue["title"]?.ToString() ?? issue["issue_id"]?.ToString() ?? "",
                    ["CreatedBy"] = issue["raised_by"]?.ToString() ?? createdBy,
                    ["CreatedOn"] = issue["date_raised"]?.ToString() ?? createdOn,
                    ["Type"] = issue["type"]?.ToString() ?? issue["category"]?.ToString() ?? "Design",
                    ["Risk"] = issue["priority"]?.ToString() ?? "Medium",
                    ["Chance"] = "", ["Impact"] = issue["priority"]?.ToString() ?? "",
                    ["SheetName1"] = "Component", ["RowName1"] = issue["element_id"]?.ToString() ?? "",
                    ["SheetName2"] = "", ["RowName2"] = "",
                    ["Description"] = issue["description"]?.ToString() ?? "",
                    ["Owner"] = issue["assigned_to"]?.ToString() ?? "",
                    ["Mitigation"] = issue["resolution"]?.ToString() ?? ""
                });
            }
            data["Issue"] = issuesList;

            // ── PickLists (valid COBie pick list values) ──
            var pickLists = new List<Dictionary<string, string>>();
            var pickListValues = new Dictionary<string, string[]>
            {
                ["AssetType"] = new[] { "Fixed", "Moveable" },
                ["CategoryFacility"] = new[] { "Facility" },
                ["CategoryFloor"] = new[] { "Floor", "Site", "Basement" },
                ["CategorySpace"] = new[] { "Office", "Corridor", "Plant Room", "WC", "Kitchen", "Store", "Circulation", "Room" },
                ["CategoryZone"] = new[] { "Occupancy Zone", "Fire Zone", "Lighting Zone", "HVAC Zone", "Security Zone" },
                ["DurationUnit"] = new[] { "years", "months", "weeks", "days", "hours" },
                ["FloorType"] = new[] { "Basement", "Ground", "Upper", "Roof" },
                ["ImpactType"] = new[] { "Environment", "Health", "Safety", "Cost" },
                ["ImpactStage"] = new[] { "Construction", "Operation", "Demolition" },
                ["JobStatus"] = new[] { "Not Started", "In Progress", "Complete" },
                ["JobType"] = new[] { "Preventive", "Corrective", "Condition-Based", "Emergency" },
                ["ObjType"] = new[] { "Fixed", "Moveable", "Component" },
                ["LinearUnits"] = new[] { "millimeters", "meters", "feet", "inches" },
                ["AreaUnits"] = new[] { "square meters", "square feet" },
                ["VolumeUnits"] = new[] { "cubic meters", "cubic feet" },
                ["CurrencyUnit"] = new[] { "GBP", "USD", "EUR" },
                ["AreaMeasurement"] = new[] { "NRM", "RICS", "IPMS" },
                ["ConnectionType"] = new[] { "Logical", "Physical", "Control" },
                ["IssueRisk"] = new[] { "High", "Medium", "Low" },
                ["CoordinateType"] = new[] { "Point", "Box" }
            };
            foreach (var kvp in pickListValues)
            {
                foreach (string val in kvp.Value)
                {
                    pickLists.Add(new Dictionary<string, string>
                    {
                        ["Name"] = kvp.Key, ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                        ["Category"] = "PickList",
                        ["Value"] = val, ["SheetName"] = "",
                        ["Description"] = $"Valid value for {kvp.Key}"
                    });
                }
            }
            data["PickLists"] = pickLists;

            return data;
        }

        // ═══════════════════════════════════════════════════════════
        //  Project Dashboard Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject BuildDashboard(Document doc)
        {
            var dashboard = new JObject();
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);

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

            var modelStats = new JObject
            {
                ["levels"] = new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount(),
                ["rooms"] = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount(),
                ["views"] = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Count(v => !v.IsTemplate),
                ["sheets"] = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount(),
                ["linked_models"] = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).GetElementCount(),
                ["is_workshared"] = doc.IsWorkshared
            };
            dashboard["model_statistics"] = modelStats;

            // Issues
            string issuesPath = GetBIMManagerFilePath(doc, "issues.json");
            var issues = LoadJsonArray(issuesPath);
            var issueSummary = new JObject { ["total"] = issues.Count };
            foreach (var status in IssueStatuses.Keys)
            {
                int count = issues.Count(i => i["status"]?.ToString() == status);
                if (count > 0) issueSummary[status.ToLower()] = count;
            }
            dashboard["issue_summary"] = issueSummary;

            // Documents
            string docsPath = GetBIMManagerFilePath(doc, "document_register.json");
            var docs = LoadJsonArray(docsPath);
            dashboard["document_count"] = docs.Count;
            dashboard["documents_incoming"] = docs.Count(d => d["direction"]?.ToString() == "IN");
            dashboard["documents_outgoing"] = docs.Count(d => d["direction"]?.ToString() == "OUT");

            // BEP
            string bepPath = GetBIMManagerFilePath(doc, "project_bep.json");
            dashboard["bep_exists"] = File.Exists(bepPath);
            if (File.Exists(bepPath))
            {
                var bep = LoadJsonFile(bepPath);
                dashboard["bep_revision"] = bep["project_information"]?["bep_revision"]?.ToString() ?? "P01";
                dashboard["bep_stage"] = bep["project_information"]?["project_stage"]?.ToString() ?? "";
            }

            // RAG
            double pct = (double)(dashboard["tag_completeness_pct"] ?? 0);
            int openIssues = (int)(issueSummary["total"] ?? 0) - (int)(issueSummary["closed"] ?? 0) - (int)(issueSummary["void"] ?? 0);
            dashboard["rag_status"] = pct >= 80 && openIssues < 10 ? "GREEN" :
                                      pct >= 50 || openIssues < 50 ? "AMBER" : "RED";

            return dashboard;
        }

        // ═══════════════════════════════════════════════════════════
        //  Document Naming
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
            if (parts.Length < 6) { errors.Add($"Expected 6-8 fields, got {parts.Length}"); return false; }
            if (parts[0].Length < 2 || parts[0].Length > 6) errors.Add($"Project code should be 2-6 chars: '{parts[0]}'");
            if (parts[1].Length < 1 || parts[1].Length > 6) errors.Add($"Originator code should be 1-6 chars: '{parts[1]}'");
            if (parts.Length >= 5 && !DocumentTypes.ContainsKey(parts[4].ToUpper()))
                errors.Add($"Unknown document type code: '{parts[4]}' (valid: {string.Join(", ", DocumentTypes.Keys.Take(10))}...)");
            if (parts.Length >= 6 && !RoleCodes.ContainsKey(parts[5].ToUpper()))
                errors.Add($"Unknown role code: '{parts[5]}' (valid: {string.Join(", ", RoleCodes.Keys)})");
            return errors.Count == 0;
        }

        // ═══════════════════════════════════════════════════════════
        //  Transmittal Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject CreateTransmittal(Document doc, string recipientOrg, string recipientRole,
            string suitability, string reason, JArray documentIds)
        {
            var pi = doc.ProjectInformation;
            return new JObject
            {
                ["transmittal_id"] = GetNextSequentialId(doc.PathName ?? "TX", "TX"),
                ["project_name"] = pi?.Name ?? "Untitled",
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
                ["acknowledged"] = false
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  Review Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject CreateReview(string documentId, string reviewerName,
            string reviewerRole, string reviewType)
        {
            return new JObject
            {
                ["review_id"] = GetNextSequentialId(documentId, "RV"),
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

        // ═══════════════════════════════════════════════════════════
        //  Briefcase Engine — Document Viewer/Reader in Revit
        // ═══════════════════════════════════════════════════════════

        internal static List<BriefcaseItem> GetBriefcaseItems(Document doc)
        {
            var items = new List<BriefcaseItem>();
            string bimDir = GetBIMManagerDir(doc);

            // 1. BEP
            string bepPath = Path.Combine(bimDir, "project_bep.json");
            if (File.Exists(bepPath))
                items.Add(new BriefcaseItem("BIM Execution Plan (BEP)", bepPath, "BEP", "ISO 19650-2 §5.3"));

            // 2. Project Dashboard
            string dashPath = Path.Combine(bimDir, "project_dashboard.json");
            if (File.Exists(dashPath))
                items.Add(new BriefcaseItem("Project Dashboard", dashPath, "Dashboard", "Live project status"));

            // 3. Issues
            string issuesPath = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesPath))
                items.Add(new BriefcaseItem("Issue Register", issuesPath, "Issues", "BCF-compatible issue tracker"));

            // 4. Document Register
            string docsPath = Path.Combine(bimDir, "document_register.json");
            if (File.Exists(docsPath))
                items.Add(new BriefcaseItem("Document Register", docsPath, "DocReg", "ISO 19650 document tracking"));

            // 5. Transmittals
            string txPath = Path.Combine(bimDir, "transmittals.json");
            if (File.Exists(txPath))
                items.Add(new BriefcaseItem("Transmittals", txPath, "Transmittal", "ISO 19650 transmittal notes"));

            // 6. Reviews
            string rvPath = Path.Combine(bimDir, "reviews.json");
            if (File.Exists(rvPath))
                items.Add(new BriefcaseItem("Review Register", rvPath, "Reviews", "Document review tracker"));

            // 7. COBie exports
            string cobieDir = Path.Combine(bimDir, "COBie_V24");
            if (Directory.Exists(cobieDir))
            {
                foreach (string f in Directory.GetFiles(cobieDir, "*.csv").Take(5))
                    items.Add(new BriefcaseItem($"COBie: {Path.GetFileNameWithoutExtension(f)}", f, "COBie", "BS 1192-4:2014"));
                foreach (string f in Directory.GetFiles(cobieDir, "*.xlsx").Take(2))
                    items.Add(new BriefcaseItem($"COBie XLSX: {Path.GetFileName(f)}", f, "COBie", "Complete workbook"));
            }

            // 8. Any text/pdf files user has placed in the BIM Manager folder
            foreach (string f in Directory.GetFiles(bimDir, "*.txt").Take(10))
            {
                string fn = Path.GetFileName(f);
                if (!items.Any(i => i.FilePath == f))
                    items.Add(new BriefcaseItem(fn, f, "Reference", "User document"));
            }
            foreach (string f in Directory.GetFiles(bimDir, "*.pdf").Take(10))
                items.Add(new BriefcaseItem(Path.GetFileName(f), f, "Reference", "User PDF"));
            foreach (string f in Directory.GetFiles(bimDir, "*.xlsx").Take(5))
            {
                if (!items.Any(i => i.FilePath == f))
                    items.Add(new BriefcaseItem(Path.GetFileName(f), f, "Reference", "User spreadsheet"));
            }

            // 9. STING data files
            string dataPath = StingToolsApp.DataPath ?? "";
            if (!string.IsNullOrEmpty(dataPath) && Directory.Exists(dataPath))
            {
                string tagGuide = Path.Combine(dataPath, "TAG_GUIDE_V3.csv");
                if (File.Exists(tagGuide))
                    items.Add(new BriefcaseItem("Tag Guide V3", tagGuide, "Reference", "STING tag reference"));
                string paramReg = Path.Combine(dataPath, "PARAMETER_REGISTRY.json");
                if (File.Exists(paramReg))
                    items.Add(new BriefcaseItem("Parameter Registry", paramReg, "Reference", "STING parameter definitions"));
            }

            return items;
        }

        internal static string ReadBriefcaseContent(BriefcaseItem item, int maxLines = 200)
        {
            if (!File.Exists(item.FilePath))
                return $"File not found: {item.FilePath}";

            string ext = Path.GetExtension(item.FilePath).ToLower();
            try
            {
                switch (ext)
                {
                    case ".json":
                        return FormatJsonForDisplay(File.ReadAllText(item.FilePath), maxLines);
                    case ".csv":
                        return FormatCsvForDisplay(item.FilePath, maxLines);
                    case ".txt":
                        var lines = File.ReadLines(item.FilePath).Take(maxLines).ToList();
                        return string.Join("\n", lines);
                    case ".xlsx":
                        return $"[Excel file — {new FileInfo(item.FilePath).Length / 1024} KB]\n" +
                               $"Open externally: {item.FilePath}";
                    case ".pdf":
                        return $"[PDF file — {new FileInfo(item.FilePath).Length / 1024} KB]\n" +
                               $"Open externally: {item.FilePath}";
                    default:
                        return File.ReadAllText(item.FilePath).Substring(0, Math.Min(File.ReadAllText(item.FilePath).Length, 5000));
                }
            }
            catch (Exception ex)
            {
                return $"Error reading file: {ex.Message}";
            }
        }

        private static string FormatJsonForDisplay(string json, int maxLines)
        {
            try
            {
                var obj = JToken.Parse(json);
                var sb = new StringBuilder();
                FormatJsonToken(obj, sb, 0, maxLines);
                return sb.ToString();
            }
            catch { return json.Substring(0, Math.Min(json.Length, 5000)); }
        }

        private static void FormatJsonToken(JToken token, StringBuilder sb, int indent, int maxLines)
        {
            if (sb.ToString().Split('\n').Length >= maxLines) return;
            string pad = new string(' ', indent * 2);

            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    if (sb.ToString().Split('\n').Length >= maxLines) { sb.AppendLine($"{pad}..."); return; }
                    if (prop.Value is JObject || prop.Value is JArray)
                    {
                        sb.AppendLine($"{pad}{prop.Name}:");
                        FormatJsonToken(prop.Value, sb, indent + 1, maxLines);
                    }
                    else
                    {
                        sb.AppendLine($"{pad}{prop.Name}: {prop.Value}");
                    }
                }
            }
            else if (token is JArray arr)
            {
                int i = 0;
                foreach (var item in arr)
                {
                    if (sb.ToString().Split('\n').Length >= maxLines) { sb.AppendLine($"{pad}... ({arr.Count - i} more)"); return; }
                    if (item is JObject itemObj)
                    {
                        var summary = string.Join(", ", itemObj.Properties().Take(3).Select(p => $"{p.Name}={p.Value}"));
                        sb.AppendLine($"{pad}• {summary}");
                    }
                    else sb.AppendLine($"{pad}• {item}");
                    i++;
                }
            }
            else sb.AppendLine($"{pad}{token}");
        }

        private static string FormatCsvForDisplay(string path, int maxLines)
        {
            var sb = new StringBuilder();
            int lineNum = 0;
            foreach (string line in File.ReadLines(path))
            {
                if (lineNum >= maxLines) { sb.AppendLine($"... ({lineNum}+ rows)"); break; }
                sb.AppendLine(line);
                lineNum++;
            }
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════
        //  CSV Helper
        // ═══════════════════════════════════════════════════════════

        internal static string QuoteCSV(string val)
        {
            if (string.IsNullOrEmpty(val)) return "\"\"";
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }

        /// <summary>
        /// Generate sequential IDs like TX-0001, RV-0001 using a persistent counter file.
        /// </summary>
        private static readonly Dictionary<string, int> _seqCounters = new Dictionary<string, int>();
        internal static string GetNextSequentialId(string context, string prefix)
        {
            string key = $"{prefix}_{context}";
            if (!_seqCounters.ContainsKey(key)) _seqCounters[key] = 0;
            _seqCounters[key]++;
            return $"{prefix}-{_seqCounters[key]:D4}";
        }

        /// <summary>
        /// Reset sequential ID counter after loading existing data.
        /// Call this when loading existing transmittals/reviews to continue numbering.
        /// </summary>
        internal static void SyncSequentialCounter(JArray existing, string prefix)
        {
            int max = 0;
            foreach (var item in existing)
            {
                string id = (item[$"{prefix.ToLower()}_id"] ?? item["transmittal_id"] ?? item["review_id"])?.ToString() ?? "";
                if (id.StartsWith(prefix + "-"))
                {
                    string numPart = id.Substring(prefix.Length + 1);
                    if (int.TryParse(numPart, out int n) && n > max) max = n;
                }
            }
            string key = $"{prefix}_";
            _seqCounters[key] = max;
        }
    }

    internal class BriefcaseItem
    {
        public string Title { get; }
        public string FilePath { get; }
        public string Category { get; }
        public string Description { get; }

        public BriefcaseItem(string title, string filePath, string category, string description)
        {
            Title = title;
            FilePath = filePath;
            Category = category;
            Description = description;
        }
    }

    #endregion



    // ════════════════════════════════════════════════════════════════════════════
    //  COMMANDS
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Command 1: Create BEP (Template-Driven, Pre-Contract) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateBEPCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            StingLog.Info("BIMManager: Launching BEP Wizard...");

            // Launch BEP Wizard WPF dialog
            var wizard = new BEPWizard();
            wizard.PrePopulate(doc);

            bool? result = wizard.ShowDialog();
            if (result != true || !wizard.CreateRequested || wizard.WizardData == null)
                return Result.Cancelled;

            var wd = wizard.WizardData;

            // Generate BEP from wizard data
            var bep = BIMManagerEngine.CreateBEPFromWizard(wd);

            // Save JSON (internal use — validation, Update BEP enrichment)
            string bepPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "project_bep.json");
            BIMManagerEngine.SaveJsonFile(bepPath, bep);

            // Export XLSX (standard format document)
            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string xlsxPath = "";
            try
            {
                xlsxPath = BIMManagerEngine.ExportBEPToXlsx(bep, bimDir,
                    wd.ProjectNumber.Length > 0 ? wd.ProjectNumber : "PRJ");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BEP XLSX export failed: {ex.Message}");
            }

            // Save allowed codes for BEP validation
            string dataPath = StingToolsApp.DataPath ?? "";
            if (!string.IsNullOrEmpty(dataPath))
            {
                try
                {
                    var validationBep = new JObject
                    {
                        ["project_name"] = wd.ProjectName,
                        ["allowed_disc"] = bep["allowed_codes"]?["allowed_disc"],
                        ["allowed_loc"] = bep["allowed_codes"]?["allowed_loc"],
                        ["allowed_zone"] = bep["allowed_codes"]?["allowed_zone"],
                        ["allowed_sys"] = bep["allowed_codes"]?["allowed_sys"]
                    };
                    File.WriteAllText(Path.Combine(dataPath, "project_bep.json"),
                        validationBep.ToString(Formatting.Indented));
                }
                catch (Exception ex) { StingLog.Warn($"BEP validation file: {ex.Message}"); }
            }

            var report = new StringBuilder();
            report.AppendLine("BIM Execution Plan Created");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Template:   {wd.PresetKey} — {BIMManagerEngine.BEPPresets.GetValueOrDefault(wd.PresetKey, "")}");
            report.AppendLine($"  Stage:      {wd.RIBAStage} — {BIMManagerEngine.RIBAStages.GetValueOrDefault(wd.RIBAStage, "")}");
            report.AppendLine($"  Project:    {wd.ProjectName}");
            report.AppendLine($"  Number:     {wd.ProjectNumber}");
            report.AppendLine($"  Client:     {wd.ClientName}");
            report.AppendLine($"  Lead:       {wd.LeadDesignerDescription}");
            report.AppendLine($"  Disciplines: {string.Join(", ", wd.DisciplineTeam.Keys)}");
            report.AppendLine($"  BIM Uses:   {wd.BIMUses.Count}");
            report.AppendLine($"  Sections:   {BIMManagerEngine.BEPSections.Length}");
            report.AppendLine();
            report.AppendLine("Output files:");
            report.AppendLine($"  JSON: {bepPath}");
            if (!string.IsNullOrEmpty(xlsxPath))
                report.AppendLine($"  XLSX: {xlsxPath}");
            report.AppendLine();
            report.AppendLine("Use 'Update BEP' to enrich with model data.");
            report.AppendLine("Use 'Export BEP' to regenerate the XLSX.");

            TaskDialog.Show("STING BIM Manager — BEP", report.ToString());
            StingLog.Info($"BEP created: {wd.PresetKey}, stage {wd.RIBAStage}, xlsx={!string.IsNullOrEmpty(xlsxPath)}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 2: Update BEP from Model (Post-Modelling Enrichment) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class UpdateBEPCommand : IExternalCommand
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
                TaskDialog.Show("STING BIM Manager",
                    "No BEP found.\n\nUse 'Create BEP' first to generate a BEP from a template.\n" +
                    "Then use 'Update BEP' to enrich it with live model data.");
                return Result.Succeeded;
            }

            var existingBep = BIMManagerEngine.LoadJsonFile(bepPath);
            string oldRev = existingBep["project_information"]?["bep_revision"]?.ToString() ?? "P01";

            var updated = BIMManagerEngine.UpdateBEPFromModel(doc, existingBep);

            // GAP-014: Auto-enrich BEP with element counts, parameter bindings, and tag completeness
            try
            {
                var enrichment = new JObject();

                // Element count summary per category
                var catCounts = new JObject();
                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!knownCats.Contains(cat)) continue;
                    int cur = catCounts[cat]?.Value<int>() ?? 0;
                    catCounts[cat] = cur + 1;
                }
                enrichment["element_counts_by_category"] = catCounts;

                // Parameter binding count from ParamRegistry
                enrichment["parameter_binding_count"] = ParamRegistry.AllParamGuids.Count;

                // Tag completeness from ComplianceScan
                var compliance = ComplianceScan.Scan(doc);
                enrichment["tag_completeness_pct"] = Math.Round(compliance.CompliancePercent, 1);
                enrichment["tag_rag_status"] = compliance.RAGStatus;
                enrichment["tagged_elements"] = compliance.TaggedComplete;
                enrichment["untagged_elements"] = compliance.Untagged;

                updated["auto_enrichment"] = enrichment;
                if (updated["model_data"] is JObject md)
                    md["enriched_date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BEP auto-enrichment failed: {ex.Message}");
            }

            BIMManagerEngine.SaveJsonFile(bepPath, updated);

            string newRev = updated["project_information"]?["bep_revision"]?.ToString() ?? "";
            var modelData = updated["model_data"] as JObject;

            var report = new StringBuilder();
            report.AppendLine("BEP Updated with Model Data");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Revision: {oldRev} → {newRev}");
            if (modelData != null)
            {
                report.AppendLine($"  Levels:       {modelData["number_of_levels"]}");
                report.AppendLine($"  Rooms:        {modelData["number_of_rooms"]}");
                report.AppendLine($"  GIA:          {modelData["gross_internal_area_m2"]} m²");
                report.AppendLine($"  Height:       {modelData["building_height_m"]} m");
                report.AppendLine($"  Revit:        {modelData["revit_version"]}");
                var ws = modelData["worksets"] as JArray;
                if (ws != null) report.AppendLine($"  Worksets:     {ws.Count}");
                var lm = modelData["linked_models"] as JArray;
                if (lm != null) report.AppendLine($"  Linked Models: {lm.Count}");
            }
            var autoEnrich = updated["auto_enrichment"] as JObject;
            if (autoEnrich != null)
            {
                report.AppendLine();
                report.AppendLine("  AUTO-ENRICHMENT");
                report.AppendLine($"    Parameters:     {autoEnrich["parameter_binding_count"]}");
                report.AppendLine($"    Tag Complete:   {autoEnrich["tag_completeness_pct"]}%  ({autoEnrich["tag_rag_status"]})");
                report.AppendLine($"    Tagged:         {autoEnrich["tagged_elements"]}");
                report.AppendLine($"    Untagged:       {autoEnrich["untagged_elements"]}");
                var catCnts = autoEnrich["element_counts_by_category"] as JObject;
                if (catCnts != null)
                    report.AppendLine($"    Categories:     {catCnts.Count}  ({catCnts.Properties().Sum(p => p.Value.Value<int>())} elements)");
            }
            report.AppendLine();
            report.AppendLine($"Saved: {bepPath}");

            TaskDialog.Show("STING BIM Manager — BEP", report.ToString());
            StingLog.Info($"BEP updated: {oldRev} → {newRev}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 3: Export BEP ──

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
                TaskDialog.Show("STING BIM Manager", "No BEP found. Create one first.");
                return Result.Succeeded;
            }

            var bep = BIMManagerEngine.LoadJsonFile(bepPath);
            string projectNumber = bep["project_information"]?["project_number"]?.ToString() ?? doc.ProjectInformation?.Number ?? "PRJ";

            // Export XLSX (standard format)
            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            try
            {
                string xlsxPath = BIMManagerEngine.ExportBEPToXlsx(bep, bimDir, projectNumber);
                TaskDialog.Show("STING BIM Manager",
                    $"BEP exported to standard XLSX format:\n\n{xlsxPath}\n\n" +
                    $"Sections: {BIMManagerEngine.BEPSections.Length}\n" +
                    $"Worksheets: Cover + Team + MIDP + Standards + Risk Register + more");
                StingLog.Info($"BEP exported: {xlsxPath}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING BIM Manager", $"Export failed: {ex.Message}");
                StingLog.Error("BEP export failed", ex);
            }
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 4: Project Dashboard ──

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

            // Save dashboard JSON
            BIMManagerEngine.SaveJsonFile(
                BIMManagerEngine.GetBIMManagerFilePath(doc, "project_dashboard.json"), dashboard);

            var report = new StringBuilder();
            report.AppendLine("STING BIM Manager — Project Dashboard");
            report.AppendLine(new string('═', 55));

            string rag = dashboard["rag_status"]?.ToString() ?? "UNKNOWN";
            report.AppendLine($"  Overall Status: {rag}");
            report.AppendLine();

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

            var discBreakdown = dashboard["discipline_breakdown"] as JObject;
            if (discBreakdown != null && discBreakdown.Count > 0)
            {
                report.AppendLine("  DISCIPLINE BREAKDOWN");
                foreach (var kv in discBreakdown)
                    report.AppendLine($"    {kv.Key,-6} {kv.Value,6} elements");
                report.AppendLine();
            }

            var issueSummary = dashboard["issue_summary"] as JObject;
            if (issueSummary != null)
            {
                report.AppendLine("  ISSUE TRACKER");
                report.AppendLine($"    Total: {issueSummary["total"]}");
                foreach (var kv in issueSummary)
                    if (kv.Key != "total") report.AppendLine($"    {kv.Key,-14} {kv.Value}");
                report.AppendLine();
            }

            report.AppendLine("  DOCUMENT REGISTER");
            report.AppendLine($"    Total: {dashboard["document_count"]}  |  In: {dashboard["documents_incoming"]}  |  Out: {dashboard["documents_outgoing"]}");
            report.AppendLine();

            report.AppendLine("  BEP STATUS");
            report.AppendLine($"    BEP exists: {dashboard["bep_exists"]}");
            if ((bool)(dashboard["bep_exists"] ?? false))
            {
                report.AppendLine($"    Revision: {dashboard["bep_revision"]}");
                report.AppendLine($"    Stage: {dashboard["bep_stage"]}");
            }
            report.AppendLine();

            // GAP-009: Check tag completeness against BEP minimum threshold
            try
            {
                var compliance = ComplianceScan.Scan(doc);
                int pct = (int)Math.Round(compliance.CompliancePercent);
                const int bepMinThreshold = 80;
                report.AppendLine("  COMPLIANCE");
                report.AppendLine($"    Tag Completeness: {pct}%  (RAG: {compliance.RAGStatus})");
                string topIssues = compliance.TopIssues;
                if (!string.IsNullOrEmpty(topIssues) && topIssues != "No issues")
                    report.AppendLine($"    Top Issues: {topIssues}");
                if (pct < bepMinThreshold)
                {
                    report.AppendLine();
                    report.AppendLine($"  \u26a0 WARNING: Tag completeness ({pct}%) is below BEP minimum threshold ({bepMinThreshold}%)");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Dashboard compliance scan failed: {ex.Message}");
            }

            TaskDialog.Show("STING BIM Manager", report.ToString());
            StingLog.Info($"Dashboard: RAG={rag}, tagged={dashboard["tagged"]}/{dashboard["total_elements"]}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 5: Raise Issue / RFI ──

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

            var selectedIds = uidoc.Selection.GetElementIds();

            // Step 1: Issue type
            var typeDlg = new TaskDialog("STING Issue — Type");
            typeDlg.MainInstruction = "Select issue type:";
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "RFI — Request for Information");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "CLASH — Coordination Clash");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "DESIGN — Design Issue/Query");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "More types... (NCR, SNAGGING, CHANGE, RISK, SITE, ACTION)");
            var typeResult = typeDlg.Show();
            string issueType;
            switch (typeResult)
            {
                case TaskDialogResult.CommandLink1: issueType = "RFI"; break;
                case TaskDialogResult.CommandLink2: issueType = "CLASH"; break;
                case TaskDialogResult.CommandLink3: issueType = "DESIGN"; break;
                case TaskDialogResult.CommandLink4:
                    var moreDlg = new TaskDialog("STING Issue — More Types");
                    moreDlg.MainInstruction = "Select issue type:";
                    moreDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "NCR — Non-Conformance Report");
                    moreDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "SNAGGING — Snagging/Defect");
                    moreDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "CHANGE — Change Request");
                    moreDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "SITE — Site Observation");
                    var moreResult = moreDlg.Show();
                    issueType = moreResult switch
                    {
                        TaskDialogResult.CommandLink1 => "NCR",
                        TaskDialogResult.CommandLink2 => "SNAGGING",
                        TaskDialogResult.CommandLink3 => "CHANGE",
                        TaskDialogResult.CommandLink4 => "SITE",
                        _ => null
                    };
                    break;
                default: issueType = null; break;
            }
            if (issueType == null) return Result.Cancelled;

            // Step 2: Priority
            var priDlg = new TaskDialog("STING Issue — Priority");
            priDlg.MainInstruction = "Select priority:";
            priDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "CRITICAL — Blocks progress, immediate action");
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

            // Auto-detect discipline and build title from context
            string discipline = "";
            string autoTitle = "";
            if (selectedIds.Count > 0)
            {
                var firstEl = doc.GetElement(selectedIds.First());
                discipline = ParameterHelpers.GetString(firstEl, ParamRegistry.DISC);
                string cat = ParameterHelpers.GetCategoryName(firstEl);
                string tag = ParameterHelpers.GetString(firstEl, ParamRegistry.TAG1);
                autoTitle = $"{issueType}: {cat}";
                if (!string.IsNullOrEmpty(tag)) autoTitle += $" [{tag}]";
                if (selectedIds.Count > 1) autoTitle += $" (+{selectedIds.Count - 1} more)";
            }
            else
            {
                autoTitle = $"{issueType}: {uidoc.ActiveView?.Name ?? "General"}";
            }
            if (string.IsNullOrEmpty(discipline)) discipline = "Z";

            // Step 3: Assignee
            var assignDlg = new TaskDialog("STING Issue — Assign To");
            assignDlg.MainInstruction = "Assign issue to:";
            assignDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Environment.UserName, "Assign to yourself");
            assignDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "BIM Coordinator", "Assign to BIM Coordinator");
            assignDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Design Lead", "Assign to discipline design lead");
            assignDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Unassigned", "Leave unassigned for now");
            var assignResult = assignDlg.Show();
            string assignee = assignResult switch
            {
                TaskDialogResult.CommandLink1 => Environment.UserName,
                TaskDialogResult.CommandLink2 => "BIM Coordinator",
                TaskDialogResult.CommandLink3 => "Design Lead",
                TaskDialogResult.CommandLink4 => "",
                _ => ""
            };

            // Auto-generate description from context
            string description = $"{issueType} raised against ";
            if (selectedIds.Count > 0)
            {
                var firstEl = doc.GetElement(selectedIds.First());
                string cat = ParameterHelpers.GetCategoryName(firstEl);
                string lvl = ParameterHelpers.GetString(firstEl, ParamRegistry.LVL);
                description += $"{cat} on {(string.IsNullOrEmpty(lvl) ? "unknown level" : lvl)}";
                if (selectedIds.Count > 1) description += $" and {selectedIds.Count - 1} other element(s)";
            }
            else
            {
                description += $"view '{uidoc.ActiveView?.Name ?? "unknown"}'";
            }
            description += $". Priority: {priority}.";

            // Create issue with auto-generated title, description, and assignee
            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            var issues = BIMManagerEngine.LoadJsonArray(issuesPath);
            string nextId = BIMManagerEngine.GetNextIssueId(issues, issueType);

            var issue = BIMManagerEngine.CreateIssue(nextId, issueType, priority,
                autoTitle, description, assignee, discipline, selectedIds, uidoc.ActiveView?.Name);

            issues.Add(issue);
            BIMManagerEngine.SaveJsonFile(issuesPath, issues);

            var report = new StringBuilder();
            report.AppendLine($"Issue Raised: {nextId}");
            report.AppendLine(new string('═', 40));
            report.AppendLine($"  Type:       {issueType} ({BIMManagerEngine.IssueTypes[issueType]})");
            report.AppendLine($"  Priority:   {priority}");
            report.AppendLine($"  Title:      {autoTitle}");
            report.AppendLine($"  Assigned:   {(string.IsNullOrEmpty(assignee) ? "Unassigned" : assignee)}");
            report.AppendLine($"  Discipline: {discipline}");
            report.AppendLine($"  Due:        {issue["date_due"]}");
            report.AppendLine($"  View:       {issue["view_name"]}");
            report.AppendLine($"  Elements:   {selectedIds.Count} linked");
            report.AppendLine($"  Raised by:  {Environment.UserName}");

            TaskDialog.Show("STING Issue Tracker", report.ToString());
            StingLog.Info($"Issue raised: {nextId} ({issueType}, {priority})");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 6: Issue Dashboard ──

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
            report.AppendLine($"  Total Issues: {issues.Count}");
            report.AppendLine();

            var byStatus = issues.GroupBy(i => i["status"]?.ToString() ?? "?").OrderBy(g => g.Key);
            report.AppendLine("  BY STATUS:");
            foreach (var g in byStatus) report.AppendLine($"    {g.Key,-14} {g.Count(),4}");
            report.AppendLine();

            var byType = issues.GroupBy(i => i["type"]?.ToString() ?? "?").OrderBy(g => g.Key);
            report.AppendLine("  BY TYPE:");
            foreach (var g in byType) report.AppendLine($"    {g.Key,-14} {g.Count(),4}");
            report.AppendLine();

            var byPriority = issues.GroupBy(i => i["priority"]?.ToString() ?? "?").OrderBy(g => g.Key);
            report.AppendLine("  BY PRIORITY:");
            foreach (var g in byPriority) report.AppendLine($"    {g.Key,-14} {g.Count(),4}");
            report.AppendLine();

            // Overdue issues
            var now = DateTime.Now;
            var overdue = issues.Where(i =>
            {
                string s = i["status"]?.ToString() ?? "";
                if (s == "CLOSED" || s == "VOID" || s == "ACCEPTED") return false;
                string due = i["date_due"]?.ToString() ?? "";
                return DateTime.TryParse(due, out DateTime d) && d < now;
            }).ToList();
            if (overdue.Count > 0)
            {
                report.AppendLine($"  OVERDUE: {overdue.Count} issue(s)");
                foreach (var o in overdue.Take(5))
                    report.AppendLine($"    {o["issue_id"],-16} {o["priority"],-10} due {o["date_due"]}  {o["title"]}");
                report.AppendLine();
            }

            // Recent
            report.AppendLine("  RECENT (last 10):");
            foreach (var issue in issues.Reverse().Take(10))
            {
                string title = issue["title"]?.ToString() ?? "(untitled)";
                if (title.Length > 35) title = title.Substring(0, 32) + "...";
                report.AppendLine($"    {issue["issue_id"],-16} {issue["status"],-12} {issue["priority"],-10} {title}");
            }
            report.AppendLine();
            // GAP-021: Add element count context and compliance tie-in
            int issuesWithElements = issues.Count(i =>
            {
                var elIds = i["element_ids"];
                return elIds != null && elIds.HasValues;
            });
            if (issuesWithElements > 0)
                report.AppendLine($"  Issues with element links: {issuesWithElements}");

            // Show compliance context if available
            var compScan = ComplianceScan.Scan(doc);
            if (compScan != null)
                report.AppendLine($"  Model compliance: {compScan.StatusBarText}");

            report.AppendLine();
            report.AppendLine($"  File: {issuesPath}");

            TaskDialog.Show("STING Issue Tracker", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 7: Update Issue ──

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

            var dlg = new TaskDialog("STING Issue Tracker — Update");
            dlg.MainInstruction = $"{openIssues.Count} open issue(s). Select action:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Update Specific Issue", "Pick an open issue by ID to change status");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Bulk: OPEN → IN_PROGRESS ({issues.Count(i => i["status"]?.ToString() == "OPEN")})");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, $"Bulk: Close All RESPONDED ({issues.Count(i => i["status"]?.ToString() == "RESPONDED")})");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Export All Issues to CSV");
            var result = dlg.Show();

            int updated = 0;
            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    // Pick a specific issue to update
                    var recentOpen = openIssues.Take(4).ToList();
                    if (recentOpen.Count == 0) { TaskDialog.Show("STING", "No open issues."); return Result.Succeeded; }
                    var pickDlg = new TaskDialog("STING Issue — Select Issue");
                    pickDlg.MainInstruction = "Select issue to update:";
                    for (int i = 0; i < recentOpen.Count; i++)
                    {
                        var iss = recentOpen[i];
                        var linkId = (TaskDialogCommandLinkId)(i + (int)TaskDialogCommandLinkId.CommandLink1);
                        pickDlg.AddCommandLink(linkId,
                            $"{iss["issue_id"]} [{iss["status"]}]",
                            $"{iss["priority"]} — {iss["title"]}");
                    }
                    var pickResult = pickDlg.Show();
                    int issueIdx = pickResult switch
                    {
                        TaskDialogResult.CommandLink1 => 0,
                        TaskDialogResult.CommandLink2 => 1,
                        TaskDialogResult.CommandLink3 => 2,
                        TaskDialogResult.CommandLink4 => 3,
                        _ => -1
                    };
                    if (issueIdx < 0 || issueIdx >= recentOpen.Count) return Result.Cancelled;
                    var target = recentOpen[issueIdx];

                    // Pick new status
                    var statusDlg = new TaskDialog("STING Issue — New Status");
                    statusDlg.MainInstruction = $"Update {target["issue_id"]}:";
                    statusDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "IN_PROGRESS — Being investigated");
                    statusDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "RESPONDED — Response provided");
                    statusDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "CLOSED — Issue resolved");
                    statusDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "VOID — Withdrawn/superseded");
                    var statusResult = statusDlg.Show();
                    string newStatus = statusResult switch
                    {
                        TaskDialogResult.CommandLink1 => "IN_PROGRESS",
                        TaskDialogResult.CommandLink2 => "RESPONDED",
                        TaskDialogResult.CommandLink3 => "CLOSED",
                        TaskDialogResult.CommandLink4 => "VOID",
                        _ => null
                    };
                    if (newStatus == null) return Result.Cancelled;
                    target["status"] = newStatus;
                    if (newStatus == "CLOSED" || newStatus == "VOID")
                        target["date_closed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    updated = 1;
                    break;
                case TaskDialogResult.CommandLink2:
                    foreach (var issue in issues.Where(i => i["status"]?.ToString() == "OPEN"))
                    {
                        issue["status"] = "IN_PROGRESS";
                        updated++;
                    }
                    break;
                case TaskDialogResult.CommandLink3:
                    foreach (var issue in issues.Where(i => i["status"]?.ToString() == "RESPONDED"))
                    {
                        issue["status"] = "CLOSED";
                        issue["date_closed"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                        updated++;
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

                // GAP-016: When issues are closed/accepted, check if linked elements are tagged
                var closeReport = new StringBuilder();
                closeReport.AppendLine($"{updated} issue(s) updated.");
                foreach (var issue in issues.Where(i =>
                {
                    string s = i["status"]?.ToString() ?? "";
                    return s == "CLOSED" || s == "ACCEPTED";
                }))
                {
                    var ids = issue["element_ids"] as JArray;
                    if (ids != null && ids.Count > 0)
                    {
                        int untagged = 0;
                        foreach (var id in ids)
                        {
                            if (long.TryParse(id.ToString(), out long idVal))
                            {
                                Element el = doc.GetElement(new ElementId(idVal));
                                if (el != null && string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.TAG1)))
                                    untagged++;
                            }
                        }
                        if (untagged > 0)
                            closeReport.AppendLine($"  \u26a0 {issue["issue_id"]}: {untagged} linked element(s) still untagged");
                    }
                }

                TaskDialog.Show("STING Issue Tracker", closeReport.ToString());
            }
            return Result.Succeeded;
        }

        private void ExportIssuesToCSV(Document doc, JArray issues)
        {
            string csvPath = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc),
                $"STING_ISSUES_{DateTime.Now:yyyyMMdd}.csv");
            var sb = new StringBuilder();
            sb.AppendLine("Issue_ID,Type,Priority,Status,Discipline,Title,Raised_By,Date_Raised,Date_Due,Date_Closed,Assigned_To,View,Element_Count");
            foreach (var issue in issues)
            {
                var ids = issue["element_ids"] as JArray;
                sb.AppendLine(string.Join(",",
                    BIMManagerEngine.QuoteCSV(issue["issue_id"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["type"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["priority"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["status"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["discipline"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["title"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["raised_by"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["date_raised"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["date_due"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["date_closed"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["assigned_to"]?.ToString()),
                    BIMManagerEngine.QuoteCSV(issue["view_name"]?.ToString()),
                    ids?.Count.ToString() ?? "0"
                ));
            }
            try
            {
                File.WriteAllText(csvPath, sb.ToString());
                TaskDialog.Show("STING Issue Tracker", $"Exported {issues.Count} issues to:\n{csvPath}");
            }
            catch (Exception ex) { TaskDialog.Show("STING", $"Export failed: {ex.Message}"); }
        }
    }

    #endregion

    #region ── Command 8: Document Register ──

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
            report.AppendLine($"  Total: {docs.Count}  |  In: {docs.Count(d => d["direction"]?.ToString() == "IN")}  |  Out: {docs.Count(d => d["direction"]?.ToString() == "OUT")}");
            report.AppendLine();

            var bySuit = docs.GroupBy(d => d["suitability"]?.ToString() ?? "N/A").OrderBy(g => g.Key);
            report.AppendLine("  BY SUITABILITY:");
            foreach (var g in bySuit)
            {
                string desc = BIMManagerEngine.SuitabilityCodes.ContainsKey(g.Key) ? BIMManagerEngine.SuitabilityCodes[g.Key] : g.Key;
                report.AppendLine($"    {g.Key,-4} {desc,-30} {g.Count(),4}");
            }
            report.AppendLine();

            var byCDE = docs.GroupBy(d => d["cde_status"]?.ToString() ?? "N/A").OrderBy(g => g.Key);
            report.AppendLine("  BY CDE STATUS:");
            foreach (var g in byCDE) report.AppendLine($"    {g.Key,-12} {g.Count(),4}");
            report.AppendLine();

            report.AppendLine("  RECENT (last 10):");
            foreach (var d in docs.Reverse().Take(10))
            {
                string title = d["title"]?.ToString() ?? "";
                if (title.Length > 30) title = title.Substring(0, 27) + "...";
                report.AppendLine($"    {d["doc_id"],-24} {d["direction"],-4} {d["suitability"],-4} {title}");
            }
            report.AppendLine();
            report.AppendLine($"  File: {docsPath}");

            TaskDialog.Show("STING BIM Manager", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 9: Add Document ──

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
            var dirDlg = new TaskDialog("STING Doc Register — Direction");
            dirDlg.MainInstruction = "Document direction:";
            dirDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "INCOMING — Received from external party");
            dirDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "OUTGOING — Issued to external party");
            var dirResult = dirDlg.Show();
            string direction = dirResult == TaskDialogResult.CommandLink1 ? "IN" :
                               dirResult == TaskDialogResult.CommandLink2 ? "OUT" : null;
            if (direction == null) return Result.Cancelled;

            // Document type — paginated to cover all 30+ types
            var typeDlg = new TaskDialog("STING Doc Register — Type (Page 1/4)");
            typeDlg.MainInstruction = "Select document type:";
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "M3 — 3D Model");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "DR — Drawing (2D)");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "SP — Specification");
            typeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "More types →");
            var typeResult = typeDlg.Show();
            string docType;
            switch (typeResult)
            {
                case TaskDialogResult.CommandLink1: docType = "M3"; break;
                case TaskDialogResult.CommandLink2: docType = "DR"; break;
                case TaskDialogResult.CommandLink3: docType = "SP"; break;
                case TaskDialogResult.CommandLink4:
                    var pg2 = new TaskDialog("STING Doc Register — Type (Page 2/4)");
                    pg2.MainInstruction = "Select document type:";
                    pg2.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "RP — Report");
                    pg2.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "RI — Request for Information");
                    pg2.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "SH — Schedule");
                    pg2.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "More types →");
                    var pg2Result = pg2.Show();
                    switch (pg2Result)
                    {
                        case TaskDialogResult.CommandLink1: docType = "RP"; break;
                        case TaskDialogResult.CommandLink2: docType = "RI"; break;
                        case TaskDialogResult.CommandLink3: docType = "SH"; break;
                        case TaskDialogResult.CommandLink4:
                            var pg3 = new TaskDialog("STING Doc Register — Type (Page 3/4)");
                            pg3.MainInstruction = "Select document type:";
                            pg3.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "BQ — Bill of Quantities");
                            pg3.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "CA — Calculations");
                            pg3.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "CP — Cost Plan");
                            pg3.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "More types →");
                            var pg3Result = pg3.Show();
                            switch (pg3Result)
                            {
                                case TaskDialogResult.CommandLink1: docType = "BQ"; break;
                                case TaskDialogResult.CommandLink2: docType = "CA"; break;
                                case TaskDialogResult.CommandLink3: docType = "CP"; break;
                                case TaskDialogResult.CommandLink4:
                                    var pg4 = new TaskDialog("STING Doc Register — Type (Page 4/4)");
                                    pg4.MainInstruction = "Select document type:";
                                    pg4.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "IE — COBie Data Exchange");
                                    pg4.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "HS — Health and Safety");
                                    pg4.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "MS — Method Statement");
                                    pg4.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "PP/MI/SN/TN/VS/AF/CR/SU...");
                                    var pg4Result = pg4.Show();
                                    docType = pg4Result switch
                                    {
                                        TaskDialogResult.CommandLink1 => "IE",
                                        TaskDialogResult.CommandLink2 => "HS",
                                        TaskDialogResult.CommandLink3 => "MS",
                                        TaskDialogResult.CommandLink4 => "MR",
                                        _ => "RP"
                                    };
                                    break;
                                default: docType = "RP"; break;
                            }
                            break;
                        default: docType = "RP"; break;
                    }
                    break;
                default: return Result.Cancelled;
            }

            // Suitability
            var suitDlg = new TaskDialog("STING Doc Register — Suitability");
            suitDlg.MainInstruction = "ISO 19650 suitability code:";
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

            // Generate ISO 19650 document ID
            var pi = doc.ProjectInformation;
            string project = pi?.Number ?? "PRJ";
            if (project.Length > 6) project = project.Substring(0, 6);

            string docsPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "document_register.json");
            var docs = BIMManagerEngine.LoadJsonArray(docsPath);

            string docId = BIMManagerEngine.GenerateDocumentName(
                project, "Z", "ZZ", "ZZ", docType, "Z", "Zz_99", (docs.Count + 1).ToString("D4"));

            string typeDesc = BIMManagerEngine.DocumentTypes.ContainsKey(docType) ? BIMManagerEngine.DocumentTypes[docType] : docType;
            var entry = BIMManagerEngine.CreateDocumentEntry(docId, typeDesc, docType, "Z", suitability, "WIP", direction);

            // GAP-013: Store current project revision in document entry
            entry["revision"] = PhaseAutoDetect.DetectProjectRevision(doc) ?? "P01";

            docs.Add(entry);
            BIMManagerEngine.SaveJsonFile(docsPath, docs);

            var report = new StringBuilder();
            report.AppendLine("Document Registered");
            report.AppendLine(new string('═', 40));
            report.AppendLine($"  ID:          {docId}");
            report.AppendLine($"  Type:        {docType} ({typeDesc})");
            report.AppendLine($"  Direction:   {direction}");
            report.AppendLine($"  Suitability: {suitability} ({BIMManagerEngine.SuitabilityCodes[suitability]})");
            report.AppendLine($"  CDE Status:  WIP");
            report.AppendLine();
            report.AppendLine($"Edit: {docsPath}");

            TaskDialog.Show("STING Document Register", report.ToString());
            StingLog.Info($"Document registered: {docId}");
            return Result.Succeeded;
        }
    }

    #endregion


    #region ── Command 10: COBie Export ──

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

            string cobieDir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc),
                $"COBie_V24_{DateTime.Now:yyyyMMdd}");
            if (!Directory.Exists(cobieDir)) Directory.CreateDirectory(cobieDir);

            int totalRows = 0;
            var summary = new StringBuilder();

            foreach (var ws in cobieData)
            {
                totalRows += ws.Value.Count;
                string[] headers = BIMManagerEngine.COBieWorksheets.ContainsKey(ws.Key)
                    ? BIMManagerEngine.COBieWorksheets[ws.Key]
                    : (ws.Value.Count > 0 ? ws.Value[0].Keys.ToArray() : Array.Empty<string>());
                if (headers.Length == 0) continue;

                var csv = new StringBuilder();
                csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));
                foreach (var row in ws.Value)
                    csv.AppendLine(string.Join(",", headers.Select(h =>
                        $"\"{(row.ContainsKey(h) ? row[h]?.Replace("\"", "\"\"") : "")}\"")));

                try { File.WriteAllText(Path.Combine(cobieDir, $"COBie_{ws.Key}.csv"), csv.ToString()); }
                catch (Exception ex) { StingLog.Warn($"COBie {ws.Key}: {ex.Message}"); }
                summary.AppendLine($"    {ws.Key,-16} {ws.Value.Count,6} rows");
            }

            // XLSX export
            try
            {
                string xlsxPath = Path.Combine(cobieDir, "COBie_V24_Complete.xlsx");
                using (var wb = new ClosedXML.Excel.XLWorkbook())
                {
                    foreach (var ws in cobieData)
                    {
                        string name = ws.Key.Length > 31 ? ws.Key.Substring(0, 31) : ws.Key;
                        var sheet = wb.Worksheets.Add(name);
                        if (ws.Value.Count == 0) continue;
                        string[] headers = BIMManagerEngine.COBieWorksheets.ContainsKey(ws.Key)
                            ? BIMManagerEngine.COBieWorksheets[ws.Key] : ws.Value[0].Keys.ToArray();
                        for (int c = 0; c < headers.Length; c++)
                        {
                            sheet.Cell(1, c + 1).Value = headers[c];
                            sheet.Cell(1, c + 1).Style.Font.Bold = true;
                            sheet.Cell(1, c + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                        }
                        for (int r = 0; r < ws.Value.Count; r++)
                            for (int c = 0; c < headers.Length; c++)
                                sheet.Cell(r + 2, c + 1).Value = ws.Value[r].ContainsKey(headers[c]) ? ws.Value[r][headers[c]] : "";
                        sheet.Columns().AdjustToContents(1, 100);
                    }
                    wb.SaveAs(xlsxPath);
                }
            }
            catch (Exception ex) { StingLog.Warn($"COBie XLSX: {ex.Message}"); }

            var report = new StringBuilder();
            report.AppendLine("COBie V2.4 Export Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Standard:   COBie V2.4 (BS 1192-4:2014)");
            report.AppendLine($"  Worksheets: {cobieData.Count}");
            report.AppendLine($"  Total rows: {totalRows}");
            report.AppendLine();
            report.Append(summary);
            report.AppendLine();
            report.AppendLine($"  Output: {cobieDir}");

            TaskDialog.Show("STING BIM Manager — COBie", report.ToString());
            StingLog.Info($"COBie: {cobieData.Count} worksheets, {totalRows} rows");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 11: Create Transmittal ──

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

            // GAP-015: CDE status pre-flight check before transmittal
            string cdeStatus = ParameterHelpers.GetString(doc.ProjectInformation, "ASS_CDE_STATUS_TXT");
            if (string.IsNullOrEmpty(cdeStatus) || cdeStatus == "WIP")
            {
                var warn = new TaskDialog("STING Transmittal Pre-Flight");
                warn.MainInstruction = "CDE Status Warning";
                warn.MainContent = $"Current CDE status is '{(string.IsNullOrEmpty(cdeStatus) ? "NOT SET" : cdeStatus)}'.\n" +
                    "ISO 19650 requires SHARED or PUBLISHED status before transmittal.\n\n" +
                    "Continue anyway?";
                warn.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                if (warn.Show() == TaskDialogResult.No) return Result.Cancelled;
            }

            // Suitability
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

            // Auto-attach registered documents
            string docsPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "document_register.json");
            var registeredDocs = BIMManagerEngine.LoadJsonArray(docsPath);
            var outgoingIds = new JArray(registeredDocs
                .Where(d => d["direction"]?.ToString() == "OUT" && d["cde_status"]?.ToString() != "ARCHIVE")
                .Select(d => d["doc_id"]?.ToString())
                .Where(id => !string.IsNullOrEmpty(id)));

            var transmittal = BIMManagerEngine.CreateTransmittal(
                doc, "", "", suitability, "Model drop per MIDP schedule", outgoingIds);

            string txPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "transmittals.json");
            var transmittals = BIMManagerEngine.LoadJsonArray(txPath);
            transmittals.Add(transmittal);
            BIMManagerEngine.SaveJsonFile(txPath, transmittals);

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
            note.AppendLine($"  Suitability:     {suitability} — {BIMManagerEngine.SuitabilityCodes[suitability]}");
            note.AppendLine($"  Documents:       {outgoingIds.Count} attached from register");
            note.AppendLine();
            note.AppendLine($"  Edit: {txPath}");

            // Save text version
            string txtPath = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc),
                $"TX_{transmittal["transmittal_id"]}_{DateTime.Now:yyyyMMdd}.txt");
            try { File.WriteAllText(txtPath, note.ToString()); }
            catch { }

            TaskDialog.Show("STING Transmittal", note.ToString());
            StingLog.Info($"Transmittal: {transmittal["transmittal_id"]}, {outgoingIds.Count} docs");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 12: CDE Status ──

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

            var dlg = new TaskDialog("STING CDE Status Manager");
            dlg.MainInstruction = "Set CDE container status for this model:";
            dlg.MainContent = "ISO 19650 defines 4 CDE containers.\nStored in Project Information parameters.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "WIP — Work In Progress");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "SHARED — For Coordination");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "PUBLISHED — Approved");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "ARCHIVE — Superseded");
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

            string suitCode = status == "PUBLISHED" ? "S6" : status == "SHARED" ? "S3" : "S0";

            using (Transaction tx = new Transaction(doc, "STING Set CDE Status"))
            {
                tx.Start();
                ParameterHelpers.SetString(doc.ProjectInformation, "ASS_CDE_STATUS_TXT", status, true);
                ParameterHelpers.SetString(doc.ProjectInformation, "ASS_CDE_SUITABILITY_TXT", suitCode, true);
                tx.Commit();
            }

            TaskDialog.Show("STING CDE Status",
                $"CDE Status: {status} ({BIMManagerEngine.CDEStates[status]})\n" +
                $"Suitability: {suitCode}\n\n" +
                $"Stored in: ASS_CDE_STATUS_TXT, ASS_CDE_SUITABILITY_TXT");

            StingLog.Info($"CDE status: {status}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 13: Validate Document Naming ──

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

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();

            int compliant = 0, nonCompliant = 0;
            var issues = new List<string>();

            foreach (var sheet in sheets)
            {
                string number = sheet.SheetNumber ?? "";
                string name = sheet.Name ?? "";

                if (BIMManagerEngine.ValidateDocumentName(number, out var errs))
                    compliant++;
                else
                {
                    nonCompliant++;
                    if (issues.Count < 20)
                        issues.Add($"  {number}: {name}\n    → {string.Join("; ", errs)}");
                }
            }

            var report = new StringBuilder();
            report.AppendLine("ISO 19650 Document Naming Validation");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Total sheets:  {sheets.Count}");
            report.AppendLine($"  Compliant:     {compliant}");
            report.AppendLine($"  Non-compliant: {nonCompliant}");
            report.AppendLine();

            if (nonCompliant > 0)
            {
                report.AppendLine("  NON-COMPLIANT:");
                foreach (string s in issues) report.AppendLine(s);
                if (nonCompliant > 20) report.AppendLine($"  ... and {nonCompliant - 20} more");
                report.AppendLine();
            }

            report.AppendLine("  Expected: Project-Originator-Volume-Level-Type-Role-Class-Number");
            report.AppendLine("  Example:  PRJ-ABC-ZZ-01-DR-A-Zz_99-0001");

            TaskDialog.Show("STING BIM Manager", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 14: Review Tracker ──

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
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Create New Review Request");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "View Review Status");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Complete Oldest Pending Review");
            var result = dlg.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    // Link to a document from the register
                    string docsPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "document_register.json");
                    var docs = BIMManagerEngine.LoadJsonArray(docsPath);

                    // Pick document to review
                    string docRef;
                    if (docs.Count == 0)
                    {
                        TaskDialog.Show("STING Review Tracker",
                            "No documents in register. Add documents first using 'Add Document'.");
                        return Result.Succeeded;
                    }
                    var pickDocDlg = new TaskDialog("STING Review — Select Document");
                    pickDocDlg.MainInstruction = "Select document to review:";
                    int docIdx = Math.Max(0, docs.Count - 4);
                    var recentDocs = docs.Skip(docIdx).Take(4).ToList();
                    for (int i = 0; i < recentDocs.Count && i < 4; i++)
                    {
                        string id = recentDocs[i]["doc_id"]?.ToString() ?? $"DOC-{i + 1}";
                        string title = recentDocs[i]["title"]?.ToString() ?? "(untitled)";
                        var linkId = (TaskDialogCommandLinkId)(i + (int)TaskDialogCommandLinkId.CommandLink1);
                        pickDocDlg.AddCommandLink(linkId, $"{id}", title);
                    }
                    var pickResult = pickDocDlg.Show();
                    int pickIdx = pickResult switch
                    {
                        TaskDialogResult.CommandLink1 => 0,
                        TaskDialogResult.CommandLink2 => 1,
                        TaskDialogResult.CommandLink3 => 2,
                        TaskDialogResult.CommandLink4 => 3,
                        _ => -1
                    };
                    if (pickIdx < 0 || pickIdx >= recentDocs.Count) return Result.Cancelled;
                    docRef = recentDocs[pickIdx]["doc_id"]?.ToString() ?? "DOC-0001";

                    // Review type
                    var rvTypeDlg = new TaskDialog("STING Review — Type");
                    rvTypeDlg.MainInstruction = "Review type:";
                    rvTypeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Design Review", "Review design intent and coordination");
                    rvTypeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Stage Approval", "Gate review for RIBA stage sign-off");
                    rvTypeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Technical Check", "Detailed technical/compliance check");
                    rvTypeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Client Review", "Client/employer review and comment");
                    var rvTypeResult = rvTypeDlg.Show();
                    string reviewType = rvTypeResult switch
                    {
                        TaskDialogResult.CommandLink1 => "Design Review",
                        TaskDialogResult.CommandLink2 => "Stage Approval",
                        TaskDialogResult.CommandLink3 => "Technical Check",
                        TaskDialogResult.CommandLink4 => "Client Review",
                        _ => "Design Review"
                    };

                    // Sync counter so IDs continue from existing
                    BIMManagerEngine.SyncSequentialCounter(reviews, "RV");
                    string reviewerName = Environment.UserName;
                    var review = BIMManagerEngine.CreateReview(docRef, reviewerName, "Reviewer", reviewType);
                    reviews.Add(review);
                    BIMManagerEngine.SaveJsonFile(reviewPath, reviews);
                    TaskDialog.Show("STING Review Tracker",
                        $"Review created: {review["review_id"]}\n" +
                        $"Document: {docRef}\nType: {reviewType}\nReviewer: {reviewerName}\nDue: {review["date_due"]}\n\n" +
                        $"Edit: {reviewPath}");
                    break;

                case TaskDialogResult.CommandLink2:
                    var statusReport = new StringBuilder();
                    statusReport.AppendLine("Review Status");
                    statusReport.AppendLine(new string('═', 50));
                    foreach (var g in reviews.GroupBy(r => r["status"]?.ToString() ?? "").OrderBy(g => g.Key))
                        statusReport.AppendLine($"  {g.Key,-14} {g.Count(),4}");
                    statusReport.AppendLine();
                    statusReport.AppendLine("  PENDING:");
                    foreach (var r in reviews.Where(r => r["status"]?.ToString() == "PENDING").Take(10))
                        statusReport.AppendLine($"    {r["review_id"]} — {r["document_id"]} — due {r["date_due"]}");
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
                            $"Review {pending["review_id"]} → COMPLETED (APPROVED)");
                    }
                    else TaskDialog.Show("STING Review Tracker", "No pending reviews.");
                    break;

                default: return Result.Cancelled;
            }
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 15: Select Issue Elements ──

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

            var allIds = new List<ElementId>();
            foreach (var issue in openWithElements)
            {
                var ids = issue["element_ids"] as JArray;
                if (ids == null) continue;
                foreach (var id in ids)
                    if (long.TryParse(id.ToString(), out long idVal))
                        allIds.Add(new ElementId(idVal));
            }

            var validIds = allIds.Where(id => doc.GetElement(id) != null).ToList();
            if (validIds.Count > 0)
            {
                uidoc.Selection.SetElementIds(validIds);
                TaskDialog.Show("STING Issue Tracker",
                    $"Selected {validIds.Count} elements from {openWithElements.Count} open issues.");
            }
            else TaskDialog.Show("STING Issue Tracker", "No valid elements found.");

            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 16: ISO 19650 Reference ──

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
            foreach (var kv in BIMManagerEngine.DocumentTypes)
                report.AppendLine($"    {kv.Key,-4} {kv.Value}");
            report.AppendLine();

            report.AppendLine("  ROLE CODES:");
            foreach (var kv in BIMManagerEngine.RoleCodes)
                report.AppendLine($"    {kv.Key,-4} {kv.Value}");
            report.AppendLine();

            report.AppendLine("  RIBA PLAN OF WORK STAGES:");
            foreach (var kv in BIMManagerEngine.RIBAStages)
                report.AppendLine($"    Stage {kv.Key}: {kv.Value}");
            report.AppendLine();

            report.AppendLine("  BEP SECTIONS (ISO 19650-2 §5.3):");
            foreach (string s in BIMManagerEngine.BEPSections)
                report.AppendLine($"    {s}");

            TaskDialog.Show("STING BIM Manager — ISO 19650", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 17: Bulk Export ──

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

            StingLog.Info("BIMManager: Bulk export...");
            string dir = BIMManagerEngine.GetBIMManagerDir(doc);

            // Dashboard
            var dashboard = BIMManagerEngine.BuildDashboard(doc);
            BIMManagerEngine.SaveJsonFile(Path.Combine(dir, "project_dashboard.json"), dashboard);

            // COBie
            var cobieData = BIMManagerEngine.BuildCOBieData(doc);
            int cobieRows = cobieData.Values.Sum(v => v.Count);
            string cobieDir = Path.Combine(dir, "COBie_V24");
            if (!Directory.Exists(cobieDir)) Directory.CreateDirectory(cobieDir);
            foreach (var ws in cobieData)
            {
                if (ws.Value.Count == 0) continue;
                var headers = ws.Value[0].Keys.ToArray();
                var csv = new StringBuilder();
                csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));
                foreach (var row in ws.Value)
                    csv.AppendLine(string.Join(",", headers.Select(h =>
                        $"\"{(row.ContainsKey(h) ? row[h]?.Replace("\"", "\"\"") : "")}\"")));
                try { File.WriteAllText(Path.Combine(cobieDir, $"COBie_{ws.Key}.csv"), csv.ToString()); }
                catch { }
            }

            // Update BEP if exists
            string bepPath = Path.Combine(dir, "project_bep.json");
            if (File.Exists(bepPath))
            {
                var existingBep = BIMManagerEngine.LoadJsonFile(bepPath);
                var updated = BIMManagerEngine.UpdateBEPFromModel(doc, existingBep);
                BIMManagerEngine.SaveJsonFile(bepPath, updated);
            }

            var report = new StringBuilder();
            report.AppendLine("STING BIM Manager — Bulk Export");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Dashboard: RAG {dashboard["rag_status"]}, {dashboard["tagged"]}/{dashboard["total_elements"]} tagged");
            report.AppendLine($"  COBie:     {cobieData.Count} worksheets, {cobieRows} rows");
            report.AppendLine($"  BEP:       {(File.Exists(bepPath) ? "Updated" : "Not found — create first")}");
            report.AppendLine();
            report.AppendLine($"  Output: {dir}");

            TaskDialog.Show("STING BIM Manager", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 18: Briefcase — View Reference Documents ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BriefcaseViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var items = BIMManagerEngine.GetBriefcaseItems(doc);
            if (items.Count == 0)
            {
                TaskDialog.Show("STING Briefcase",
                    "No documents found in your project briefcase.\n\n" +
                    "Documents are loaded from:\n" +
                    $"  {BIMManagerEngine.GetBIMManagerDir(doc)}\n\n" +
                    "Place reference files (PDF, TXT, XLSX) in this folder,\n" +
                    "or generate BEP/COBie/Dashboard first.");
                return Result.Succeeded;
            }

            // Build index by category
            var byCategory = items.GroupBy(i => i.Category).OrderBy(g => g.Key);

            var report = new StringBuilder();
            report.AppendLine("STING Briefcase — Project Documents");
            report.AppendLine(new string('═', 55));
            report.AppendLine($"  {items.Count} documents available");
            report.AppendLine();

            foreach (var cat in byCategory)
            {
                report.AppendLine($"  [{cat.Key.ToUpper()}]");
                int idx = 1;
                foreach (var item in cat)
                {
                    long sizekb = 0;
                    try { sizekb = new FileInfo(item.FilePath).Length / 1024; } catch { }
                    report.AppendLine($"    {idx}. {item.Title,-35} {sizekb,5} KB  {item.Description}");
                    idx++;
                }
                report.AppendLine();
            }

            // Show first item preview
            var dlg = new TaskDialog("STING Briefcase");
            dlg.MainInstruction = $"Briefcase: {items.Count} documents";
            dlg.MainContent = report.ToString();
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "View BEP", "Read the BIM Execution Plan");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "View Latest Document", "Read the most recent document");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Open Briefcase Folder", "Open the folder in Windows Explorer");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Print Document List", "Export document index to text file");
            var result = dlg.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    var bepItem = items.FirstOrDefault(i => i.Category == "BEP");
                    if (bepItem != null)
                    {
                        string content = BIMManagerEngine.ReadBriefcaseContent(bepItem);
                        TaskDialog.Show($"STING Briefcase — {bepItem.Title}", content);
                    }
                    else TaskDialog.Show("STING Briefcase", "No BEP found. Create one first.");
                    break;

                case TaskDialogResult.CommandLink2:
                    var latest = items.Last();
                    string latestContent = BIMManagerEngine.ReadBriefcaseContent(latest);
                    TaskDialog.Show($"STING Briefcase — {latest.Title}", latestContent);
                    break;

                case TaskDialogResult.CommandLink3:
                    try { System.Diagnostics.Process.Start("explorer.exe", BIMManagerEngine.GetBIMManagerDir(doc)); }
                    catch (Exception ex) { TaskDialog.Show("STING", $"Could not open folder: {ex.Message}"); }
                    break;

                case TaskDialogResult.CommandLink4:
                    string indexPath = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc),
                        $"BRIEFCASE_INDEX_{DateTime.Now:yyyyMMdd}.txt");
                    try
                    {
                        File.WriteAllText(indexPath, report.ToString());
                        TaskDialog.Show("STING Briefcase", $"Index exported to:\n{indexPath}");
                    }
                    catch (Exception ex) { TaskDialog.Show("STING", $"Export failed: {ex.Message}"); }
                    break;
            }

            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 19: Briefcase — Read Specific Document ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BriefcaseReadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var items = BIMManagerEngine.GetBriefcaseItems(doc);
            if (items.Count == 0)
            {
                TaskDialog.Show("STING Briefcase", "No documents in briefcase.");
                return Result.Succeeded;
            }

            // Pick category
            var categories = items.Select(i => i.Category).Distinct().OrderBy(c => c).ToList();
            var catDlg = new TaskDialog("STING Briefcase — Category");
            catDlg.MainInstruction = "Select document category:";
            if (categories.Count >= 1)
                catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, categories[0],
                    $"{items.Count(i => i.Category == categories[0])} documents");
            if (categories.Count >= 2)
                catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, categories[1],
                    $"{items.Count(i => i.Category == categories[1])} documents");
            if (categories.Count >= 3)
                catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, categories[2],
                    $"{items.Count(i => i.Category == categories[2])} documents");
            if (categories.Count >= 4)
                catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    $"More... ({categories.Count - 3} more categories)", "");
            var catResult = catDlg.Show();

            string selectedCat = catResult switch
            {
                TaskDialogResult.CommandLink1 => categories.Count >= 1 ? categories[0] : null,
                TaskDialogResult.CommandLink2 => categories.Count >= 2 ? categories[1] : null,
                TaskDialogResult.CommandLink3 => categories.Count >= 3 ? categories[2] : null,
                TaskDialogResult.CommandLink4 => categories.Count >= 4 ? categories[3] : null,
                _ => null
            };
            if (selectedCat == null) return Result.Cancelled;

            // Show documents in category
            var catItems = items.Where(i => i.Category == selectedCat).ToList();
            var docDlg = new TaskDialog($"STING Briefcase — {selectedCat}");
            docDlg.MainInstruction = $"{catItems.Count} document(s):";
            if (catItems.Count >= 1)
                docDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, catItems[0].Title, catItems[0].Description);
            if (catItems.Count >= 2)
                docDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, catItems[1].Title, catItems[1].Description);
            if (catItems.Count >= 3)
                docDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, catItems[2].Title, catItems[2].Description);
            if (catItems.Count >= 4)
                docDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, catItems[3].Title, catItems[3].Description);
            var docResult = docDlg.Show();

            int idx = docResult switch
            {
                TaskDialogResult.CommandLink1 => 0,
                TaskDialogResult.CommandLink2 => 1,
                TaskDialogResult.CommandLink3 => 2,
                TaskDialogResult.CommandLink4 => 3,
                _ => -1
            };
            if (idx < 0 || idx >= catItems.Count) return Result.Cancelled;

            var selectedItem = catItems[idx];
            string content = BIMManagerEngine.ReadBriefcaseContent(selectedItem);

            // Show with print option
            var viewDlg = new TaskDialog($"STING Briefcase — {selectedItem.Title}");
            viewDlg.MainInstruction = selectedItem.Title;
            viewDlg.MainContent = content.Length > 3000 ? content.Substring(0, 3000) + "\n\n... (truncated)" : content;
            viewDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Save as Text File", "Export to a printable text file");
            viewDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Open in Default App", "Open file with associated application");
            viewDlg.CommonButtons = TaskDialogCommonButtons.Close;
            var viewResult = viewDlg.Show();

            switch (viewResult)
            {
                case TaskDialogResult.CommandLink1:
                    string savePath = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc),
                        $"PRINT_{Path.GetFileNameWithoutExtension(selectedItem.FilePath)}_{DateTime.Now:yyyyMMdd}.txt");
                    try
                    {
                        File.WriteAllText(savePath, content);
                        TaskDialog.Show("STING Briefcase", $"Saved to:\n{savePath}");
                    }
                    catch (Exception ex) { TaskDialog.Show("STING", $"Save failed: {ex.Message}"); }
                    break;

                case TaskDialogResult.CommandLink2:
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(selectedItem.FilePath) { UseShellExecute = true }); }
                    catch (Exception ex) { TaskDialog.Show("STING", $"Could not open: {ex.Message}"); }
                    break;
            }

            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command 20: Briefcase — Add Reference File ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BriefcaseAddFileCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);

            var dlg = new TaskDialog("STING Briefcase — Add File");
            dlg.MainInstruction = "Add a reference file to the project briefcase";
            dlg.MainContent =
                $"Briefcase folder:\n{bimDir}\n\n" +
                "Place reference files (PDF, TXT, XLSX, CSV) in this folder.\n" +
                "They will appear in the Briefcase viewer automatically.\n\n" +
                "Supported file types:\n" +
                "  • PDF — Specifications, standards, drawings\n" +
                "  • TXT — Notes, meeting minutes, reports\n" +
                "  • XLSX — Schedules, BOQs, data tables\n" +
                "  • CSV — Data files, tag guides\n" +
                "  • JSON — Configuration, BEP, registers";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open Briefcase Folder", "Add files manually");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Create Quick Note", "Create a text note in the briefcase");
            var result = dlg.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    try { System.Diagnostics.Process.Start("explorer.exe", bimDir); }
                    catch (Exception ex) { TaskDialog.Show("STING", $"Could not open: {ex.Message}"); }
                    break;

                case TaskDialogResult.CommandLink2:
                    string notePath = Path.Combine(bimDir,
                        $"NOTE_{DateTime.Now:yyyyMMdd_HHmm}.txt");
                    var note = new StringBuilder();
                    note.AppendLine($"Project Note — {DateTime.Now:yyyy-MM-dd HH:mm}");
                    note.AppendLine(new string('─', 40));
                    note.AppendLine($"Author: {Environment.UserName}");
                    note.AppendLine($"Project: {doc.ProjectInformation?.Name ?? ""}");
                    note.AppendLine();
                    note.AppendLine("[Edit this file to add your notes]");
                    note.AppendLine();
                    try
                    {
                        File.WriteAllText(notePath, note.ToString());
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(notePath) { UseShellExecute = true });
                        TaskDialog.Show("STING Briefcase", $"Note created and opened:\n{notePath}");
                    }
                    catch (Exception ex) { TaskDialog.Show("STING", $"Failed: {ex.Message}"); }
                    break;
            }

            return Result.Succeeded;
        }
    }

    #endregion

    // Backwards-compatible alias so old command handler wiring still works
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateBEPCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return new CreateBEPCommand().Execute(commandData, ref message, elements);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  STING BIM TOOLS — Procore Briefcase Export, Ideate-Style Sticky Notes,
    //  Model Health Dashboard, MIDP Tracker, 4D/5D Export, Compliance Integration
    // ════════════════════════════════════════════════════════════════════════════

    #region Document Briefcase (Procore-style)

    /// <summary>
    /// Document Briefcase: generates a portable project information package
    /// containing all essential BIM metadata, schedules, tag registers,
    /// and compliance reports in a single export folder.
    /// Inspired by Procore's Briefcase offline document sync.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DocumentBriefcaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                // Prompt for output folder
                var dlg = new TaskDialog("Document Briefcase");
                dlg.MainInstruction = "Generate Document Briefcase";
                dlg.MainContent =
                    "Creates a portable project folder with:\n" +
                    "  • Project Information Summary\n" +
                    "  • Tag Register (all tagged elements)\n" +
                    "  • Compliance Report (ISO 19650)\n" +
                    "  • Parameter Audit (completeness)\n" +
                    "  • Model Statistics\n" +
                    "  • Sheet Index\n" +
                    "  • Discipline Breakdown\n\n" +
                    "Output saved alongside the Revit model.";
                dlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                if (dlg.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

                string modelPath = doc.PathName;
                string outputDir;
                if (!string.IsNullOrEmpty(modelPath))
                {
                    string dir = Path.GetDirectoryName(modelPath);
                    string name = Path.GetFileNameWithoutExtension(modelPath);
                    outputDir = Path.Combine(dir, $"{name}_Briefcase_{DateTime.Now:yyyyMMdd_HHmmss}");
                }
                else
                {
                    outputDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"STING_Briefcase_{DateTime.Now:yyyyMMdd_HHmmss}");
                }

                Directory.CreateDirectory(outputDir);
                var sw = Stopwatch.StartNew();
                int filesGenerated = 0;

                // 1. Project Information Summary
                filesGenerated += BriefcaseEngine.ExportProjectInfo(doc, outputDir);

                // 2. Tag Register
                filesGenerated += BriefcaseEngine.ExportTagRegister(doc, outputDir);

                // 3. Compliance Report
                filesGenerated += BriefcaseEngine.ExportComplianceReport(doc, outputDir);

                // 4. Parameter Audit
                filesGenerated += BriefcaseEngine.ExportParameterAudit(doc, outputDir);

                // 5. Model Statistics
                filesGenerated += BriefcaseEngine.ExportModelStats(doc, outputDir);

                // 6. Sheet Index
                filesGenerated += BriefcaseEngine.ExportSheetIndex(doc, outputDir);

                // 7. Discipline Breakdown
                filesGenerated += BriefcaseEngine.ExportDisciplineBreakdown(doc, outputDir);

                // 8. MIDP Register
                filesGenerated += BriefcaseEngine.ExportMidpRegister(doc, outputDir);

                sw.Stop();

                TaskDialog.Show("Document Briefcase",
                    $"Briefcase generated successfully.\n\n" +
                    $"Location: {outputDir}\n" +
                    $"Files: {filesGenerated}\n" +
                    $"Duration: {sw.Elapsed.TotalSeconds:F1}s");

                StingLog.Info($"DocumentBriefcase: {filesGenerated} files to {outputDir} in {sw.Elapsed.TotalSeconds:F1}s");
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("DocumentBriefcaseCommand failed", ex);
                TaskDialog.Show("STING", $"Briefcase generation failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Element Sticky Notes (Ideate-style)

    /// <summary>
    /// Element Sticky Notes: attach persistent text annotations to elements
    /// stored in shared parameters. Notes survive across sessions and can be
    /// exported for QA reviews. Inspired by Ideate Sticky Notes for Revit.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElementStickyNoteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var selIds = uidoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    TaskDialog.Show("Sticky Note", "Select one or more elements first.");
                    return Result.Cancelled;
                }

                // Prompt for note text
                var dlg = new TaskDialog("Element Sticky Note");
                dlg.MainInstruction = $"Add note to {selIds.Count} element(s)";
                dlg.MainContent =
                    "Choose action:\n" +
                    "• Add/Edit Note — write a sticky note\n" +
                    "• View Notes — display existing notes\n" +
                    "• Clear Notes — remove all notes from selection";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Add/Edit Note", "Write a new note or append to existing");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "View Notes", "Display all notes on selected elements");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Clear Notes", "Remove notes from selected elements");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var result = dlg.Show();

                switch (result)
                {
                    case TaskDialogResult.CommandLink1:
                        return StickyEngine.AddNote(doc, selIds);
                    case TaskDialogResult.CommandLink2:
                        return StickyEngine.ViewNotes(doc, selIds);
                    case TaskDialogResult.CommandLink3:
                        return StickyEngine.ClearNotes(doc, selIds);
                    default:
                        return Result.Cancelled;
                }
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("ElementStickyNoteCommand failed", ex);
                TaskDialog.Show("STING", $"Sticky note failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Export all sticky notes across the project to CSV for QA review.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportStickyNotesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                return StickyEngine.ExportAllNotes(doc);
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportStickyNotesCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Select all elements that have sticky notes attached.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectStickyElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var elementsWithNotes = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        string note = ParameterHelpers.GetString(e, "STING_STICKY_NOTE_TXT");
                        return !string.IsNullOrEmpty(note);
                    })
                    .Select(e => e.Id)
                    .ToList();

                if (elementsWithNotes.Count == 0)
                {
                    TaskDialog.Show("Sticky Notes", "No elements with sticky notes found.");
                    return Result.Succeeded;
                }

                uidoc.Selection.SetElementIds(elementsWithNotes);
                TaskDialog.Show("Sticky Notes",
                    $"Selected {elementsWithNotes.Count} elements with sticky notes.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SelectStickyElementsCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Model Health Dashboard

    /// <summary>
    /// Comprehensive model health check covering: file size, warnings, worksets,
    /// linked models, design options, groups, in-place families, imported instances,
    /// unused families, and parameter completeness.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelHealthDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var report = ModelHealthEngine.RunHealthCheck(doc);

                TaskDialog td = new TaskDialog("Model Health Dashboard");
                td.MainInstruction = $"Model Health: {report.OverallScore}/100 ({report.Rating})";
                td.MainContent = report.Summary;
                td.ExpandedContent = report.Details;
                td.Show();

                StingLog.Info($"ModelHealth: score={report.OverallScore}, rating={report.Rating}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelHealthDashboardCommand failed", ex);
                TaskDialog.Show("STING", $"Health check failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Export model health report to CSV file for tracking over time.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportModelHealthCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var report = ModelHealthEngine.RunHealthCheck(doc);
                string path = ModelHealthEngine.ExportReport(doc, report);

                TaskDialog.Show("Model Health", $"Report exported to:\n{path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportModelHealthCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region MIDP (Master Information Delivery Plan) Tracker

    /// <summary>
    /// MIDP Register: tracks document deliverables per ISO 19650 with
    /// suitability codes, status tracking, and deliverable milestones.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MidpTrackerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var midpData = MidpEngine.BuildMidpRegister(doc);

                var report = new StringBuilder();
                report.AppendLine("MIDP Register — Master Information Delivery Plan");
                report.AppendLine(new string('═', 60));
                report.AppendLine($"  Total Deliverables:  {midpData.TotalDeliverables}");
                report.AppendLine($"  Sheets (Published):  {midpData.PublishedSheets}/{midpData.TotalSheets}");
                report.AppendLine($"  Models:              {midpData.LinkedModels}");
                report.AppendLine($"  Suitability S0-S6:   {midpData.SuitabilityBreakdown}");
                report.AppendLine();
                report.AppendLine("Discipline Breakdown:");
                foreach (var kvp in midpData.ByDiscipline.OrderByDescending(k => k.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value} deliverables");
                report.AppendLine();
                report.AppendLine("Status:");
                foreach (var kvp in midpData.ByStatus.OrderByDescending(k => k.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value}");

                TaskDialog td = new TaskDialog("MIDP Tracker");
                td.MainInstruction = $"MIDP: {midpData.TotalDeliverables} deliverables tracked";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MidpTrackerCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region 4D/5D Integration

    /// <summary>
    /// 4D Timeline Export: exports element-phase relationships for construction
    /// sequencing tools (Navisworks, Synchro, MS Project import format).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Export4DTimelineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                string path = SchedulingEngine.Export4DTimeline(doc);

                TaskDialog.Show("4D Timeline",
                    $"Timeline data exported for construction sequencing.\n\nFile: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Export4DTimelineCommand failed", ex);
                TaskDialog.Show("STING", $"4D export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 5D Cost Export: exports element quantities with cost data for
    /// quantity surveying tools (CostX, Causeway, BOQ format).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Export5DCostDataCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                string path = SchedulingEngine.Export5DCostData(doc);

                TaskDialog.Show("5D Cost Data",
                    $"Cost data exported for quantity surveying.\n\nFile: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Export5DCostDataCommand failed", ex);
                TaskDialog.Show("STING", $"5D export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Compliance Integration

    /// <summary>
    /// Full ISO 19650 compliance dashboard integrating tag compliance,
    /// naming conventions, suitability codes, and deliverable tracking.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FullComplianceDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var tagCompliance = ComplianceScan.Scan(doc);
                var healthReport = ModelHealthEngine.RunHealthCheck(doc);
                var midpData = MidpEngine.BuildMidpRegister(doc);

                var report = new StringBuilder();
                report.AppendLine("ISO 19650 Full Compliance Dashboard");
                report.AppendLine(new string('═', 60));
                report.AppendLine();
                report.AppendLine($"  TAG COMPLIANCE:     {tagCompliance.StatusBarText ?? "N/A"}");
                report.AppendLine($"  MODEL HEALTH:       {healthReport.OverallScore}/100 ({healthReport.Rating})");
                report.AppendLine($"  MIDP COVERAGE:      {midpData.TotalDeliverables} deliverables");
                report.AppendLine($"  SHEETS PUBLISHED:   {midpData.PublishedSheets}/{midpData.TotalSheets}");
                report.AppendLine();

                // RAG summary
                int overallScore = (int)((tagCompliance.CompliancePercent * 0.5)
                    + (healthReport.OverallScore * 0.3)
                    + (midpData.TotalSheets > 0 ? (midpData.PublishedSheets * 100.0 / midpData.TotalSheets) * 0.2 : 0));
                string overallRag = overallScore >= 80 ? "GREEN" : overallScore >= 50 ? "AMBER" : "RED";

                report.AppendLine($"  OVERALL: {overallScore}% — {overallRag}");

                string topIssues = tagCompliance.TopIssues;
                if (!string.IsNullOrEmpty(topIssues) && topIssues != "No issues")
                {
                    report.AppendLine();
                    report.AppendLine($"Top Issues: {topIssues}");
                }

                TaskDialog td = new TaskDialog("ISO 19650 Compliance");
                td.MainInstruction = $"Overall Compliance: {overallScore}% ({overallRag})";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FullComplianceDashboardCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Predecessor / Dependency Links

    /// <summary>
    /// Link elements as predecessors/successors for construction sequencing.
    /// Stores relationships in shared parameters for 4D timeline export.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LinkPredecessorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var selIds = uidoc.Selection.GetElementIds().ToList();
                if (selIds.Count < 2)
                {
                    TaskDialog.Show("Link Predecessors",
                        "Select 2+ elements: first is predecessor, rest are successors.");
                    return Result.Cancelled;
                }

                Element predecessor = doc.GetElement(selIds[0]);
                string predTag = ParameterHelpers.GetString(predecessor, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(predTag))
                {
                    TaskDialog.Show("Link Predecessors",
                        "Predecessor element must be tagged first.");
                    return Result.Failed;
                }

                int linked = 0;
                using (Transaction tx = new Transaction(doc, "STING Link Predecessors"))
                {
                    tx.Start();
                    for (int i = 1; i < selIds.Count; i++)
                    {
                        Element successor = doc.GetElement(selIds[i]);
                        if (successor == null) continue;
                        string existing = ParameterHelpers.GetString(successor, "STING_PREDECESSOR_TAGS_TXT");
                        string newVal = string.IsNullOrEmpty(existing)
                            ? predTag
                            : existing + ";" + predTag;
                        ParameterHelpers.SetString(successor, "STING_PREDECESSOR_TAGS_TXT", newVal, overwrite: true);
                        linked++;
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Link Predecessors",
                    $"Linked {linked} successor(s) to predecessor '{predTag}'.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LinkPredecessorsCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Weekend / Working Calendar

    /// <summary>
    /// Assign construction phase dates and working calendar to elements
    /// for 4D schedule generation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AssignPhaseDatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                UIDocument uidoc = ctx.UIDoc;
                Document doc = ctx.Doc;

                var selIds = uidoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    TaskDialog.Show("Phase Dates", "Select elements to assign phase dates.");
                    return Result.Cancelled;
                }

                // Auto-derive dates from Revit phase sequence
                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .ToList();

                int assigned = 0;
                using (Transaction tx = new Transaction(doc, "STING Assign Phase Dates"))
                {
                    tx.Start();
                    foreach (ElementId id in selIds)
                    {
                        Element el = doc.GetElement(id);
                        if (el == null) continue;

                        // Derive start date from phase ordinal
                        Parameter createdParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                        if (createdParam != null && createdParam.HasValue)
                        {
                            ElementId phaseId = createdParam.AsElementId();
                            int ordinal = phases.FindIndex(p => p.Id == phaseId);
                            if (ordinal >= 0)
                            {
                                // Simple scheduling: each phase = 1 month from project start
                                string startDate = DateTime.Today.AddMonths(ordinal).ToString("yyyy-MM-dd");
                                string endDate = DateTime.Today.AddMonths(ordinal + 1).ToString("yyyy-MM-dd");
                                ParameterHelpers.SetIfEmpty(el, "STING_4D_START_DATE_TXT", startDate);
                                ParameterHelpers.SetIfEmpty(el, "STING_4D_END_DATE_TXT", endDate);
                                assigned++;
                            }
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Phase Dates",
                    $"Assigned phase dates to {assigned} elements based on {phases.Count} Revit phases.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AssignPhaseDatesCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Measured Quantities

    /// <summary>
    /// Extract measured quantities from elements for NRM/SMM cost estimation.
    /// Exports lengths, areas, volumes, counts by category and discipline.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MeasuredQuantitiesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                string path = SchedulingEngine.ExportMeasuredQuantities(doc);

                TaskDialog.Show("Measured Quantities",
                    $"Quantities exported for cost estimation.\n\nFile: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MeasuredQuantitiesCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    #region Element Count Summary

    /// <summary>
    /// Quick element count by category, discipline, level, and phase.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElementCountSummaryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                Document doc = ctx.Doc;

                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .ToList();

                var byCat = new Dictionary<string, int>();
                var byDisc = new Dictionary<string, int>();
                int total = 0;

                foreach (var el in allElements)
                {
                    string catName = el.Category?.Name ?? "Unknown";
                    if (!TagConfig.DiscMap.ContainsKey(catName)) continue;
                    total++;

                    if (!byCat.ContainsKey(catName)) byCat[catName] = 0;
                    byCat[catName]++;

                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "?";
                    if (!byDisc.ContainsKey(disc)) byDisc[disc] = 0;
                    byDisc[disc]++;
                }

                var report = new StringBuilder();
                report.AppendLine($"Element Count Summary — {total} taggable elements");
                report.AppendLine(new string('─', 50));
                report.AppendLine();
                report.AppendLine("By Discipline:");
                foreach (var kvp in byDisc.OrderByDescending(k => k.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value:N0}");
                report.AppendLine();
                report.AppendLine("Top Categories:");
                foreach (var kvp in byCat.OrderByDescending(k => k.Value).Take(15))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value:N0}");

                TaskDialog td = new TaskDialog("Element Count");
                td.MainInstruction = $"{total:N0} taggable elements in model";
                td.MainContent = report.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ElementCountSummaryCommand failed", ex);
                return Result.Failed;
            }
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  ENGINE CLASSES (internal helpers)
    // ═══════════════════════════════════════════════════════════════

    #region BriefcaseEngine

    internal static class BriefcaseEngine
    {
        public static int ExportProjectInfo(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "01_PROJECT_INFO.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Property,Value");

                var pi = doc.ProjectInformation;
                if (pi != null)
                {
                    sb.AppendLine($"\"Project Name\",\"{Esc(pi.Name)}\"");
                    sb.AppendLine($"\"Project Number\",\"{Esc(pi.Number)}\"");
                    sb.AppendLine($"\"Client\",\"{Esc(pi.ClientName)}\"");
                    sb.AppendLine($"\"Building Name\",\"{Esc(pi.BuildingName)}\"");
                    sb.AppendLine($"\"Address\",\"{Esc(pi.Address)}\"");
                    sb.AppendLine($"\"Author\",\"{Esc(pi.Author)}\"");
                    sb.AppendLine($"\"Organization\",\"{Esc(pi.OrganizationName)}\"");
                    sb.AppendLine($"\"Issue Date\",\"{Esc(pi.IssueDate)}\"");
                    sb.AppendLine($"\"Status\",\"{Esc(pi.Status)}\"");
                }

                sb.AppendLine($"\"Model Path\",\"{Esc(doc.PathName)}\"");
                sb.AppendLine($"\"Export Date\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
                sb.AppendLine($"\"Revit Version\",\"{doc.Application.VersionName}\"");

                // Phase information
                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase)).Cast<Phase>().ToList();
                sb.AppendLine($"\"Phases\",\"{phases.Count}: {string.Join(", ", phases.Select(p => p.Name))}\"");

                // Level information
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();
                sb.AppendLine($"\"Levels\",\"{levels.Count}: {string.Join(", ", levels.Select(l => l.Name))}\"");

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportProjectInfo: {ex.Message}"); return 0; }
        }

        public static int ExportTagRegister(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "02_TAG_REGISTER.csv");
                var sb = new StringBuilder();
                sb.AppendLine("ElementId,Category,Family,Type,TAG1,DISC,LOC,ZONE,LVL,SYS,FUNC,PROD,SEQ,STATUS,REV");

                var elems = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                    .ToList();

                foreach (var el in elems)
                {
                    sb.AppendLine(string.Join(",",
                        $"\"{el.Id}\"",
                        $"\"{Esc(ParameterHelpers.GetCategoryName(el))}\"",
                        $"\"{Esc(ParameterHelpers.GetFamilyName(el))}\"",
                        $"\"{Esc(ParameterHelpers.GetFamilySymbolName(el))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.TAG1))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.DISC))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.LOC))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.ZONE))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.LVL))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.SYS))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.FUNC))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.PROD))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.SEQ))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.STATUS))}\"",
                        $"\"{Esc(ParameterHelpers.GetString(el, ParamRegistry.REV))}\""));
                }

                File.WriteAllText(path, sb.ToString());
                StingLog.Info($"TagRegister: exported {elems.Count} tagged elements");
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportTagRegister: {ex.Message}"); return 0; }
        }

        public static int ExportComplianceReport(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "03_COMPLIANCE_REPORT.csv");
                var scan = ComplianceScan.Scan(doc);
                var sb = new StringBuilder();
                sb.AppendLine("Metric,Value");
                sb.AppendLine($"\"RAG Status\",\"{scan.RAGStatus}\"");
                sb.AppendLine($"\"Complete %\",\"{scan.CompliancePercent:F1}\"");
                sb.AppendLine($"\"Complete Elements\",\"{scan.TaggedComplete}\"");
                sb.AppendLine($"\"Incomplete Elements\",\"{scan.TaggedIncomplete}\"");
                sb.AppendLine($"\"Untagged Elements\",\"{scan.Untagged}\"");
                sb.AppendLine($"\"Total Taggable\",\"{scan.TotalElements}\"");
                string issues = scan.TopIssues;
                if (!string.IsNullOrEmpty(issues) && issues != "No issues")
                    sb.AppendLine($"\"Top Issues\",\"{Esc(issues)}\"");
                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportComplianceReport: {ex.Message}"); return 0; }
        }

        public static int ExportParameterAudit(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "04_PARAMETER_AUDIT.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Parameter,Populated,Empty,Total,Completeness%");

                string[] keyParams = {
                    ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                    ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                    ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.TAG1,
                    ParamRegistry.STATUS, ParamRegistry.REV
                };

                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                    .ToList();

                foreach (string param in keyParams)
                {
                    int pop = 0, empty = 0;
                    foreach (var el in allElements)
                    {
                        string val = ParameterHelpers.GetString(el, param);
                        if (!string.IsNullOrEmpty(val)) pop++;
                        else empty++;
                    }
                    int total = pop + empty;
                    double pct = total > 0 ? pop * 100.0 / total : 0;
                    sb.AppendLine($"\"{param}\",{pop},{empty},{total},{pct:F1}");
                }

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportParameterAudit: {ex.Message}"); return 0; }
        }

        public static int ExportModelStats(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "05_MODEL_STATISTICS.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Metric,Value");

                int totalElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType().GetElementCount();
                int totalTypes = new FilteredElementCollector(doc)
                    .WhereElementIsElementType().GetElementCount();
                int levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).GetElementCount();
                int sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).GetElementCount();
                int views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Count(v => !v.IsTemplate);
                int families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family)).GetElementCount();
                int rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().GetElementCount();
                int links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance)).GetElementCount();

                sb.AppendLine($"\"Total Elements\",{totalElements}");
                sb.AppendLine($"\"Total Types\",{totalTypes}");
                sb.AppendLine($"\"Levels\",{levels}");
                sb.AppendLine($"\"Sheets\",{sheets}");
                sb.AppendLine($"\"Views\",{views}");
                sb.AppendLine($"\"Families\",{families}");
                sb.AppendLine($"\"Rooms\",{rooms}");
                sb.AppendLine($"\"Linked Models\",{links}");

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportModelStats: {ex.Message}"); return 0; }
        }

        public static int ExportSheetIndex(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "06_SHEET_INDEX.csv");
                var sb = new StringBuilder();
                sb.AppendLine("SheetNumber,SheetName,Discipline,ViewsPlaced,ApprovedBy,IssuedDate");

                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .OrderBy(s => s.SheetNumber)
                    .ToList();

                foreach (var sheet in sheets)
                {
                    string num = sheet.SheetNumber ?? "";
                    string name = sheet.Name ?? "";
                    string disc = num.Length >= 2 ? num.Substring(0, 2) : "";
                    int viewCount = sheet.GetAllPlacedViews().Count;
                    string approved = sheet.get_Parameter(BuiltInParameter.SHEET_APPROVED_BY)?.AsString() ?? "";
                    string issued = sheet.get_Parameter(BuiltInParameter.SHEET_ISSUE_DATE)?.AsString() ?? "";

                    sb.AppendLine($"\"{Esc(num)}\",\"{Esc(name)}\",\"{disc}\",{viewCount},\"{Esc(approved)}\",\"{Esc(issued)}\"");
                }

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportSheetIndex: {ex.Message}"); return 0; }
        }

        public static int ExportDisciplineBreakdown(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "07_DISCIPLINE_BREAKDOWN.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Discipline,Category,Count,Tagged,Untagged,Completeness%");

                var elems = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                    .ToList();

                var groups = elems
                    .GroupBy(e => new {
                        Disc = TagConfig.DiscMap.TryGetValue(e.Category.Name, out string d) ? d : "?",
                        Cat = e.Category.Name
                    })
                    .OrderBy(g => g.Key.Disc).ThenBy(g => g.Key.Cat);

                foreach (var g in groups)
                {
                    int count = g.Count();
                    int tagged = g.Count(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)));
                    int untagged = count - tagged;
                    double pct = count > 0 ? tagged * 100.0 / count : 0;
                    sb.AppendLine($"\"{g.Key.Disc}\",\"{Esc(g.Key.Cat)}\",{count},{tagged},{untagged},{pct:F1}");
                }

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportDisciplineBreakdown: {ex.Message}"); return 0; }
        }

        public static int ExportMidpRegister(Document doc, string outputDir)
        {
            try
            {
                string path = Path.Combine(outputDir, "08_MIDP_REGISTER.csv");
                var midp = MidpEngine.BuildMidpRegister(doc);
                var sb = new StringBuilder();
                sb.AppendLine("Deliverable,Type,Discipline,Status,Suitability");

                foreach (var item in midp.Items)
                    sb.AppendLine($"\"{Esc(item.Name)}\",\"{item.Type}\",\"{item.Discipline}\",\"{item.Status}\",\"{item.Suitability}\"");

                File.WriteAllText(path, sb.ToString());
                return 1;
            }
            catch (Exception ex) { StingLog.Warn($"ExportMidpRegister: {ex.Message}"); return 0; }
        }

        internal static string Esc(string s) => (s ?? "").Replace("\"", "\"\"");
    }

    #endregion

    #region StickyEngine

    internal static class StickyEngine
    {
        private const string NoteParam = "STING_STICKY_NOTE_TXT";
        private const string NoteAuthorParam = "STING_NOTE_AUTHOR_TXT";
        private const string NoteDateParam = "STING_NOTE_DATE_TXT";

        public static Result AddNote(Document doc, ICollection<ElementId> ids)
        {
            // Use a simple text prompt via TaskDialog
            var dlg = new TaskDialog("Add Sticky Note");
            dlg.MainInstruction = "Enter note text:";
            dlg.MainContent =
                "Note will be stored in STING_STICKY_NOTE_TXT parameter.\n" +
                "Use pipe (|) to separate multiple notes.\n\n" +
                "Enter your note in the verification text field below:";
            dlg.VerificationText = "QA Review Required";
            dlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;

            // Since TaskDialog doesn't support free text input, we use a well-known
            // pattern: write a placeholder and let the user know where to find it
            if (dlg.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            string noteText = $"[{DateTime.Now:yyyy-MM-dd HH:mm}] Review note — update via parameter editor";
            if (dlg.WasVerificationChecked())
                noteText = $"[{DateTime.Now:yyyy-MM-dd HH:mm}] QA REVIEW REQUIRED";

            int written = 0;
            using (Transaction tx = new Transaction(doc, "STING Add Sticky Note"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    string existing = ParameterHelpers.GetString(el, NoteParam);
                    string newNote = string.IsNullOrEmpty(existing)
                        ? noteText
                        : existing + " | " + noteText;
                    ParameterHelpers.SetString(el, NoteParam, newNote, overwrite: true);
                    ParameterHelpers.SetString(el, NoteDateParam,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), overwrite: true);
                    written++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Sticky Note", $"Note added to {written} element(s).");
            return Result.Succeeded;
        }

        public static Result ViewNotes(Document doc, ICollection<ElementId> ids)
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (ElementId id in ids)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                string note = ParameterHelpers.GetString(el, NoteParam);
                if (!string.IsNullOrEmpty(note))
                {
                    count++;
                    string cat = ParameterHelpers.GetCategoryName(el);
                    string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    sb.AppendLine($"[{el.Id}] {cat} — {tag}");
                    sb.AppendLine($"  Note: {note}");
                    sb.AppendLine();
                }
            }

            if (count == 0)
                TaskDialog.Show("Sticky Notes", "No notes found on selected elements.");
            else
                TaskDialog.Show("Sticky Notes", $"{count} note(s) found:\n\n{sb}");

            return Result.Succeeded;
        }

        public static Result ClearNotes(Document doc, ICollection<ElementId> ids)
        {
            int cleared = 0;
            using (Transaction tx = new Transaction(doc, "STING Clear Sticky Notes"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    if (!string.IsNullOrEmpty(ParameterHelpers.GetString(el, NoteParam)))
                    {
                        ParameterHelpers.SetString(el, NoteParam, "", overwrite: true);
                        ParameterHelpers.SetString(el, NoteDateParam, "", overwrite: true);
                        cleared++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Sticky Notes", $"Cleared notes from {cleared} element(s).");
            return Result.Succeeded;
        }

        public static Result ExportAllNotes(Document doc)
        {
            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, NoteParam)))
                .ToList();

            if (elements.Count == 0)
            {
                TaskDialog.Show("Export Notes", "No sticky notes found in the project.");
                return Result.Succeeded;
            }

            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_StickyNotes_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("ElementId,Category,Family,Tag,Note,Date");
            foreach (var el in elements)
            {
                sb.AppendLine(string.Join(",",
                    $"\"{el.Id}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetCategoryName(el))}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetFamilyName(el))}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetString(el, ParamRegistry.TAG1))}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetString(el, NoteParam))}\"",
                    $"\"{BriefcaseEngine.Esc(ParameterHelpers.GetString(el, NoteDateParam))}\""));
            }

            File.WriteAllText(path, sb.ToString());
            TaskDialog.Show("Export Notes", $"Exported {elements.Count} notes to:\n{path}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ModelHealthEngine

    internal static class ModelHealthEngine
    {
        public class HealthReport
        {
            public int OverallScore { get; set; }
            public string Rating { get; set; }
            public string Summary { get; set; }
            public string Details { get; set; }
        }

        public static HealthReport RunHealthCheck(Document doc)
        {
            var checks = new List<(string name, int score, int maxScore, string detail)>();

            // 1. Warnings count (low = good)
            int warningCount = doc.GetWarnings()?.Count ?? 0;
            int warnScore = warningCount == 0 ? 10 : warningCount < 50 ? 8 : warningCount < 200 ? 5 : 2;
            checks.Add(("Warnings", warnScore, 10, $"{warningCount} warnings in model"));

            // 2. In-place families (fewer = better)
            int inPlace = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Count(fi => fi.Symbol?.Family?.IsInPlace == true);
            int ipScore = inPlace == 0 ? 10 : inPlace < 10 ? 7 : inPlace < 50 ? 4 : 1;
            checks.Add(("In-Place Families", ipScore, 10, $"{inPlace} in-place families"));

            // 3. Imported instances (CAD imports — fewer = better)
            int imports = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance)).GetElementCount();
            int impScore = imports == 0 ? 10 : imports < 5 ? 7 : imports < 20 ? 4 : 1;
            checks.Add(("CAD Imports", impScore, 10, $"{imports} imported instances"));

            // 4. Groups (fewer complex groups = better)
            int groups = new FilteredElementCollector(doc)
                .OfClass(typeof(Group)).GetElementCount();
            int grpScore = groups == 0 ? 10 : groups < 20 ? 8 : groups < 100 ? 5 : 2;
            checks.Add(("Groups", grpScore, 10, $"{groups} model groups"));

            // 5. Design options (none = simplest)
            int designOptions = new FilteredElementCollector(doc)
                .OfClass(typeof(DesignOption)).GetElementCount();
            int doScore = designOptions == 0 ? 10 : designOptions < 5 ? 7 : 3;
            checks.Add(("Design Options", doScore, 10, $"{designOptions} design options"));

            // 6. Linked models (reference, not embedded)
            int links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance)).GetElementCount();
            int lnkScore = links < 10 ? 10 : links < 30 ? 7 : 4;
            checks.Add(("Linked Models", lnkScore, 10, $"{links} linked models"));

            // 7. View count (too many unplaced = bloat)
            int totalViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Count(v => !v.IsTemplate);
            int placedViews = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .SelectMany(s => s.GetAllPlacedViews())
                .Distinct().Count();
            int unplacedViews = totalViews - placedViews;
            int viewScore = unplacedViews < 20 ? 10 : unplacedViews < 100 ? 6 : 3;
            checks.Add(("View Hygiene", viewScore, 10, $"{unplacedViews} unplaced views of {totalViews} total"));

            // 8. Tag completeness
            var compScan = ComplianceScan.Scan(doc);
            int tagScore = (int)(compScan.CompliancePercent / 10.0);
            checks.Add(("Tag Completeness", tagScore, 10, $"{compScan.CompliancePercent:F0}% complete"));

            // 9. Room coverage
            int rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().GetElementCount();
            int levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).GetElementCount();
            int roomScore = rooms > 0 && levels > 0 ? Math.Min(10, rooms / levels) : 0;
            checks.Add(("Room Coverage", roomScore, 10, $"{rooms} rooms across {levels} levels"));

            // 10. Sheet coverage
            int sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).GetElementCount();
            int sheetScore = sheets > 0 ? 10 : 0;
            checks.Add(("Sheet Setup", sheetScore, 10, $"{sheets} sheets"));

            int totalScore = checks.Sum(c => c.score);
            int maxTotal = checks.Sum(c => c.maxScore);
            int pct = maxTotal > 0 ? totalScore * 100 / maxTotal : 0;
            string rating = pct >= 80 ? "HEALTHY" : pct >= 60 ? "FAIR" : pct >= 40 ? "NEEDS ATTENTION" : "CRITICAL";

            var summary = new StringBuilder();
            foreach (var c in checks)
                summary.AppendLine($"  [{c.score}/{c.maxScore}] {c.name}: {c.detail}");

            var details = new StringBuilder();
            details.AppendLine("Recommendations:");
            if (warningCount > 50) details.AppendLine("  • Resolve Revit warnings (currently " + warningCount + ")");
            if (inPlace > 5) details.AppendLine("  • Convert in-place families to loadable families");
            if (imports > 0) details.AppendLine("  • Remove or link CAD imports instead of importing");
            if (unplacedViews > 50) details.AppendLine("  • Delete unplaced views to reduce model size");

            return new HealthReport
            {
                OverallScore = pct,
                Rating = rating,
                Summary = summary.ToString(),
                Details = details.ToString()
            };
        }

        public static string ExportReport(Document doc, HealthReport report)
        {
            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_ModelHealth_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Metric,Value");
            sb.AppendLine($"\"Overall Score\",\"{report.OverallScore}\"");
            sb.AppendLine($"\"Rating\",\"{report.Rating}\"");
            sb.AppendLine($"\"Date\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
            sb.AppendLine($"\"Summary\",\"{report.Summary.Replace("\"", "\"\"")}\"");

            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }

    #endregion

    #region MidpEngine

    internal static class MidpEngine
    {
        public class MidpItem
        {
            public string Name { get; set; }
            public string Type { get; set; }      // Sheet, Model, Drawing
            public string Discipline { get; set; }
            public string Status { get; set; }     // Draft, ForReview, Approved, Published
            public string Suitability { get; set; } // S0-S6
        }

        public class MidpData
        {
            public int TotalDeliverables { get; set; }
            public int TotalSheets { get; set; }
            public int PublishedSheets { get; set; }
            public int LinkedModels { get; set; }
            public string SuitabilityBreakdown { get; set; }
            public Dictionary<string, int> ByDiscipline { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, int> ByStatus { get; set; } = new Dictionary<string, int>();
            public List<MidpItem> Items { get; set; } = new List<MidpItem>();
        }

        public static MidpData BuildMidpRegister(Document doc)
        {
            var data = new MidpData();

            // Sheets as deliverables
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .ToList();

            data.TotalSheets = sheets.Count;

            var suitCounts = new Dictionary<string, int>();
            foreach (var sheet in sheets)
            {
                string num = sheet.SheetNumber ?? "";
                string name = sheet.Name ?? "";
                string disc = num.Length >= 2 ? num.Substring(0, 2) : "XX";

                // Derive suitability from approved/issued status
                string approved = sheet.get_Parameter(BuiltInParameter.SHEET_APPROVED_BY)?.AsString() ?? "";
                string issued = sheet.get_Parameter(BuiltInParameter.SHEET_ISSUE_DATE)?.AsString() ?? "";
                string status;
                string suit;

                if (!string.IsNullOrEmpty(issued))
                {
                    status = "Published";
                    suit = "S3";
                    data.PublishedSheets++;
                }
                else if (!string.IsNullOrEmpty(approved))
                {
                    status = "Approved";
                    suit = "S2";
                }
                else if (sheet.GetAllPlacedViews().Count > 0)
                {
                    status = "ForReview";
                    suit = "S1";
                }
                else
                {
                    status = "Draft";
                    suit = "S0";
                }

                if (!suitCounts.ContainsKey(suit)) suitCounts[suit] = 0;
                suitCounts[suit]++;

                if (!data.ByDiscipline.ContainsKey(disc)) data.ByDiscipline[disc] = 0;
                data.ByDiscipline[disc]++;
                if (!data.ByStatus.ContainsKey(status)) data.ByStatus[status] = 0;
                data.ByStatus[status]++;

                data.Items.Add(new MidpItem
                {
                    Name = $"{num} - {name}",
                    Type = "Sheet",
                    Discipline = disc,
                    Status = status,
                    Suitability = suit
                });
            }

            // Linked models as deliverables
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            data.LinkedModels = links.Count;
            foreach (var link in links)
            {
                string linkName = link.Name ?? "Unknown Link";
                data.Items.Add(new MidpItem
                {
                    Name = linkName,
                    Type = "Model",
                    Discipline = "XX",
                    Status = "Active",
                    Suitability = "S3"
                });
            }

            data.TotalDeliverables = data.Items.Count;
            data.SuitabilityBreakdown = string.Join(", ",
                suitCounts.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));

            return data;
        }
    }

    #endregion

    #region SchedulingEngine (4D/5D)

    internal static class SchedulingEngine
    {
        public static string Export4DTimeline(Document doc)
        {
            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_4D_Timeline_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("ElementId,Category,Tag,Phase,Level,Discipline,StartDate,EndDate,Predecessors,Duration_Days");

            var phases = new FilteredElementCollector(doc)
                .OfClass(typeof(Phase)).Cast<Phase>().ToList();

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                .ToList();

            foreach (var el in elements)
            {
                string catName = el.Category?.Name ?? "";
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                string predecessors = ParameterHelpers.GetString(el, "STING_PREDECESSOR_TAGS_TXT");
                string startDate = ParameterHelpers.GetString(el, "STING_4D_START_DATE_TXT");
                string endDate = ParameterHelpers.GetString(el, "STING_4D_END_DATE_TXT");

                // Derive phase name
                string phaseName = "";
                Parameter createdParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (createdParam != null && createdParam.HasValue)
                {
                    Phase phase = doc.GetElement(createdParam.AsElementId()) as Phase;
                    phaseName = phase?.Name ?? "";
                }

                // Estimate duration from category
                int durationDays = EstimateDuration(catName);

                sb.AppendLine(string.Join(",",
                    $"\"{el.Id}\"",
                    $"\"{Esc(catName)}\"",
                    $"\"{Esc(tag)}\"",
                    $"\"{Esc(phaseName)}\"",
                    $"\"{Esc(lvl)}\"",
                    $"\"{Esc(disc)}\"",
                    $"\"{Esc(startDate)}\"",
                    $"\"{Esc(endDate)}\"",
                    $"\"{Esc(predecessors)}\"",
                    $"{durationDays}"));
            }

            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"4DTimeline: exported {elements.Count} elements to {path}");
            return path;
        }

        public static string Export5DCostData(Document doc)
        {
            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_5D_CostData_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("ElementId,Category,Tag,Discipline,Family,Type,Quantity,Unit,EstimatedCost_GBP");

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                .ToList();

            foreach (var el in elements)
            {
                string catName = el.Category?.Name ?? "";
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string family = ParameterHelpers.GetFamilyName(el);
                string type = ParameterHelpers.GetFamilySymbolName(el);

                // Extract quantity
                (double qty, string unit) = ExtractQuantity(el);

                // Estimate cost
                double cost = EstimateCost(catName, qty);

                sb.AppendLine(string.Join(",",
                    $"\"{el.Id}\"",
                    $"\"{Esc(catName)}\"",
                    $"\"{Esc(tag)}\"",
                    $"\"{Esc(disc)}\"",
                    $"\"{Esc(family)}\"",
                    $"\"{Esc(type)}\"",
                    $"{qty:F2}",
                    $"\"{unit}\"",
                    $"{cost:F2}"));
            }

            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"5DCostData: exported {elements.Count} elements to {path}");
            return path;
        }

        public static string ExportMeasuredQuantities(Document doc)
        {
            string dir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(dir, $"STING_MeasuredQty_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Category,Discipline,Count,TotalLength_m,TotalArea_m2,TotalVolume_m3");

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && TagConfig.DiscMap.ContainsKey(e.Category.Name))
                .ToList();

            var groups = elements.GroupBy(e => e.Category.Name).OrderBy(g => g.Key);

            const double ftToM = 0.3048;
            const double sqFtToSqM = 0.092903;
            const double cuFtToCuM = 0.0283168;

            foreach (var g in groups)
            {
                string catName = g.Key;
                string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "?";
                int count = g.Count();
                double totalLength = 0, totalArea = 0, totalVolume = 0;

                foreach (var el in g)
                {
                    Parameter lenP = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (lenP != null && lenP.HasValue) totalLength += lenP.AsDouble() * ftToM;

                    Parameter areaP = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (areaP != null && areaP.HasValue) totalArea += areaP.AsDouble() * sqFtToSqM;

                    Parameter volP = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                    if (volP != null && volP.HasValue) totalVolume += volP.AsDouble() * cuFtToCuM;
                }

                sb.AppendLine($"\"{Esc(catName)}\",\"{disc}\",{count},{totalLength:F2},{totalArea:F2},{totalVolume:F4}");
            }

            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"MeasuredQty: exported for {groups.Count()} categories to {path}");
            return path;
        }

        private static int EstimateDuration(string categoryName)
        {
            // NRM/CIBSE-based duration estimates (days per unit)
            if (categoryName.Contains("Wall")) return 5;
            if (categoryName.Contains("Floor")) return 3;
            if (categoryName.Contains("Roof")) return 7;
            if (categoryName.Contains("Column")) return 2;
            if (categoryName.Contains("Duct")) return 1;
            if (categoryName.Contains("Pipe")) return 1;
            if (categoryName.Contains("Electrical")) return 2;
            if (categoryName.Contains("Lighting")) return 1;
            return 1;
        }

        private static (double qty, string unit) ExtractQuantity(Element el)
        {
            const double ftToM = 0.3048;
            const double sqFtToSqM = 0.092903;

            // Try length first
            Parameter lenP = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (lenP != null && lenP.HasValue && lenP.AsDouble() > 0)
                return (lenP.AsDouble() * ftToM, "m");

            // Try area
            Parameter areaP = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (areaP != null && areaP.HasValue && areaP.AsDouble() > 0)
                return (areaP.AsDouble() * sqFtToSqM, "m2");

            // Default: 1 each
            return (1.0, "ea");
        }

        private static double EstimateCost(string categoryName, double qty)
        {
            // NRM2-based cost estimates (GBP per unit)
            double rate = 50.0; // default £50/unit
            if (categoryName.Contains("Wall")) rate = 120.0;
            if (categoryName.Contains("Floor")) rate = 85.0;
            if (categoryName.Contains("Roof")) rate = 180.0;
            if (categoryName.Contains("Door")) rate = 450.0;
            if (categoryName.Contains("Window")) rate = 600.0;
            if (categoryName.Contains("Mechanical Equipment")) rate = 2500.0;
            if (categoryName.Contains("Electrical Equipment")) rate = 1500.0;
            if (categoryName.Contains("Lighting")) rate = 250.0;
            if (categoryName.Contains("Plumbing")) rate = 350.0;
            if (categoryName.Contains("Duct")) rate = 75.0;
            if (categoryName.Contains("Pipe")) rate = 65.0;
            return rate * qty;
        }

        private static string Esc(string s) => (s ?? "").Replace("\"", "\"\"");
    }

    #endregion

}
