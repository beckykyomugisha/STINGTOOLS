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

            // ── Contact ──
            var contacts = new List<Dictionary<string, string>>();
            contacts.Add(new Dictionary<string, string>
            {
                ["Email"] = "", ["Company"] = pi?.ClientName ?? "", ["Phone"] = "",
                ["Department"] = "", ["OrganizationCode"] = "",
                ["GivenName"] = createdBy, ["FamilyName"] = "",
                ["Category"] = "Facility Manager", ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn
            });
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
                .GroupBy(fs => fs.FamilyName + ": " + fs.Name).Select(g => g.First()).Take(500))
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

                components.Add(new Dictionary<string, string>
                {
                    ["Name"] = tag, ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["TypeName"] = typeName, ["Space"] = roomName,
                    ["Description"] = ParameterHelpers.GetString(el, "ASS_DESCRIPTION_TXT"),
                    ["ExternalSystem"] = "Revit", ["ExternalObject"] = cat,
                    ["ExternalIdentifier"] = el.UniqueId,
                    ["TagNumber"] = tag, ["AssetIdentifier"] = tag
                });
                if (components.Count >= 5000) break;
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
                var mf = maintFreq.ContainsKey(code) ? maintFreq[code] : ("12", "months");
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

            // ── Document ──
            data["Document"] = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    ["Name"] = "BEP", ["CreatedBy"] = createdBy, ["CreatedOn"] = createdOn,
                    ["Category"] = "BIM Execution Plan", ["ApprovalBy"] = "",
                    ["Stage"] = "Design", ["Description"] = "BIM Execution Plan (ISO 19650)",
                    ["Reference"] = "project_bep.json"
                }
            };

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
            var pi = doc.ProjectInformation;

            StingLog.Info("BIMManager: Creating BEP from template...");

            // Step 1: Pick BEP preset
            var presetDlg = new TaskDialog("STING BEP — Select Template");
            presetDlg.MainInstruction = "BEP is a pre-contract document.\nSelect a template to start from:";
            presetDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "UK Government — Full ISO 19650", "Public sector, Soft Landings, comprehensive BEP");
            presetDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "NBS Standard — Commercial/Mixed-Use", "Standard BEP with Uniclass 2015 classification");
            presetDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Residential — Simplified", "Reduced deliverables for housing projects");
            presetDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Minimal — Small Project", "Essential sections only, quick setup");
            var presetResult = presetDlg.Show();
            string presetKey = presetResult switch
            {
                TaskDialogResult.CommandLink1 => "UK_GOV",
                TaskDialogResult.CommandLink2 => "NBS_STANDARD",
                TaskDialogResult.CommandLink3 => "RESIDENTIAL",
                TaskDialogResult.CommandLink4 => "MINIMAL",
                _ => null
            };
            if (presetKey == null) return Result.Cancelled;

            // Step 2: Pick RIBA stage
            var stageDlg = new TaskDialog("STING BEP — Project Stage");
            stageDlg.MainInstruction = "Which RIBA stage is this BEP for?";
            stageDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Stage 1 — Preparation and Briefing", "Pre-appointment BEP (ISO 19650-2 §5.3)");
            stageDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Stage 2 — Concept Design", "Post-appointment BEP with design intent");
            stageDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Stage 3 — Spatial Coordination", "Coordination-phase BEP update");
            stageDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Stage 4 — Technical Design", "Technical design BEP with detailed LOD");
            var stageResult = stageDlg.Show();
            int ribaStage = stageResult switch
            {
                TaskDialogResult.CommandLink1 => 1,
                TaskDialogResult.CommandLink2 => 2,
                TaskDialogResult.CommandLink3 => 3,
                TaskDialogResult.CommandLink4 => 4,
                _ => 2
            };

            // Step 3: Pick disciplines
            var discDlg = new TaskDialog("STING BEP — Disciplines");
            discDlg.MainInstruction = "Select primary discipline lead:";
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Architecture Lead (A)", "Architect as lead designer with MEP/Structural");
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "MEP Lead (M/E/P)", "MEP engineer lead with Architecture/Structural");
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Multi-Discipline", "All disciplines: A, M, E, P, S, Q, C");
            var discResult = discDlg.Show();
            string leadDesigner; string leadCode; string[] disciplines;
            switch (discResult)
            {
                case TaskDialogResult.CommandLink1:
                    leadDesigner = "Architect"; leadCode = "A";
                    disciplines = new[] { "M", "E", "P", "S" };
                    break;
                case TaskDialogResult.CommandLink2:
                    leadDesigner = "MEP Engineer"; leadCode = "M";
                    disciplines = new[] { "A", "E", "P", "S" };
                    break;
                default:
                    leadDesigner = "Lead Designer"; leadCode = "A";
                    disciplines = new[] { "A", "M", "E", "P", "S", "Q", "C" };
                    break;
            }

            // Generate BEP from template + user input
            string projectName = pi?.Name ?? "Untitled Project";
            string projectNumber = pi?.Number ?? "";
            string clientName = pi?.ClientName ?? "";
            string projectAddress = pi?.Address ?? "";

            var bep = BIMManagerEngine.CreateBEPFromTemplate(
                presetKey, projectName, projectNumber, clientName, projectAddress,
                ribaStage, leadDesigner, leadCode, disciplines);

            // Save
            string bepPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "project_bep.json");
            BIMManagerEngine.SaveJsonFile(bepPath, bep);

            // Also save allowed codes for BEP validation
            string dataPath = StingToolsApp.DataPath ?? "";
            if (!string.IsNullOrEmpty(dataPath))
            {
                try
                {
                    var validationBep = new JObject
                    {
                        ["project_name"] = projectName,
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
            report.AppendLine($"  Template:   {presetKey} — {BIMManagerEngine.BEPPresets[presetKey]}");
            report.AppendLine($"  Stage:      {ribaStage} — {BIMManagerEngine.RIBAStages[ribaStage]}");
            report.AppendLine($"  Project:    {projectName}");
            report.AppendLine($"  Number:     {projectNumber}");
            report.AppendLine($"  Client:     {clientName}");
            report.AppendLine($"  Lead:       {leadDesigner} [{leadCode}]");
            report.AppendLine($"  Disciplines: {string.Join(", ", disciplines)}");
            report.AppendLine($"  Sections:   {BIMManagerEngine.BEPSections.Length}");
            report.AppendLine($"  MIDP Stages: {ribaStage}-7");
            report.AppendLine();
            report.AppendLine("This BEP is template-driven (pre-contract).");
            report.AppendLine("Use 'Update BEP' later to enrich with model data.");
            report.AppendLine();
            report.AppendLine($"Edit: {bepPath}");

            TaskDialog.Show("STING BIM Manager — BEP", report.ToString());
            StingLog.Info($"BEP created: {presetKey}, stage {ribaStage}");
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
                    string contact = member["contact_name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(contact))
                        text.AppendLine($"    Contact: {contact} ({member["company"]})");
                }
                text.AppendLine();
            }

            // Remaining sections
            string[] sectionKeys = { "goals_and_uses", "roles_and_responsibilities", "process_design",
                "midp", "information_standard", "software_platforms", "model_structure",
                "cde_workflow", "level_of_information_need", "clash_detection",
                "quality_assurance", "security", "handover_requirements", "risk_register", "allowed_codes" };

            int sectionNum = 3;
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
                                    text.AppendLine($"    • {string.Join(", ", itemObj.Properties().Take(4).Select(p => $"{p.Name}={p.Value}"))}");
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
                            text.AppendLine($"  {kv.Key,-28} {kv.Value}");
                    }
                }
                else if (section is JArray sArr)
                {
                    foreach (var item in sArr)
                    {
                        if (item is JObject itemObj)
                        {
                            var summary = string.Join(" | ", itemObj.Properties().Take(4).Select(p => $"{p.Name}: {p.Value}"));
                            text.AppendLine($"  • {summary}");
                        }
                        else text.AppendLine($"  • {item}");
                    }
                }
                text.AppendLine();
                sectionNum++;
            }

            // Model data if enriched
            var modelData = bep["model_data"] as JObject;
            if (modelData != null)
            {
                text.AppendLine("MODEL DATA (from Revit)");
                text.AppendLine(new string('─', 50));
                foreach (var kv in modelData)
                {
                    if (kv.Value is JArray arr)
                        text.AppendLine($"  {kv.Key}: {string.Join(", ", arr.Take(10))}");
                    else
                        text.AppendLine($"  {kv.Key,-28} {kv.Value}");
                }
                text.AppendLine();
            }

            text.AppendLine(new string('═', 60));
            text.AppendLine($"Generated by STING BIM Manager — {DateTime.Now:yyyy-MM-dd HH:mm}");

            string txtPath = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc),
                $"BEP_{doc.ProjectInformation?.Number ?? "PRJ"}_{DateTime.Now:yyyyMMdd}.txt");
            try
            {
                File.WriteAllText(txtPath, text.ToString());
                TaskDialog.Show("STING BIM Manager", $"BEP exported:\n{txtPath}");
                StingLog.Info($"BEP exported: {txtPath}");
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

            // Create issue with auto-generated title and due date
            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            var issues = BIMManagerEngine.LoadJsonArray(issuesPath);
            string nextId = BIMManagerEngine.GetNextIssueId(issues, issueType);

            var issue = BIMManagerEngine.CreateIssue(nextId, issueType, priority,
                autoTitle, "", "", discipline, selectedIds, uidoc.ActiveView?.Name);

            issues.Add(issue);
            BIMManagerEngine.SaveJsonFile(issuesPath, issues);

            var report = new StringBuilder();
            report.AppendLine($"Issue Raised: {nextId}");
            report.AppendLine(new string('═', 40));
            report.AppendLine($"  Type:       {issueType} ({BIMManagerEngine.IssueTypes[issueType]})");
            report.AppendLine($"  Priority:   {priority}");
            report.AppendLine($"  Title:      {autoTitle}");
            report.AppendLine($"  Discipline: {discipline}");
            report.AppendLine($"  Due:        {issue["date_due"]}");
            report.AppendLine($"  View:       {issue["view_name"]}");
            report.AppendLine($"  Elements:   {selectedIds.Count} linked");
            report.AppendLine($"  Raised by:  {Environment.UserName}");
            report.AppendLine();
            report.AppendLine("Edit description and assignee in:");
            report.AppendLine($"  {issuesPath}");

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
                TaskDialog.Show("STING Issue Tracker", $"{updated} issue(s) updated.");
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
}
