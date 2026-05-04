// FILE: ISO19650Standards.cs - ISO 19650 BIM Standards
// BIM INFORMATION MANAGEMENT
// LINES: ~400 (optimized)

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingBIM.Standards.ISO19650
{
    public enum InformationDeliveryMilestone { TI, AIR, PIP, EIR, BEP, MIDP, TIDP }
    public enum LevelOfInformationNeed { LOI_1, LOI_2, LOI_3, LOI_4, LOI_5, LOI_6 }
    public enum ModelPurpose { Strategic, Design, Construction, AsBuilt, OperationsMaintenance }
    public enum InformationContainer { Work_In_Progress, Shared, Published, Archive }
    
    public class BIMRequirements
    {
        public string ProjectName { get; set; }
        public string InformationDeliveryMilestone { get; set; }
        public LevelOfInformationNeed GeometricDetail { get; set; }
        public LevelOfInformationNeed AlphanumericDetail { get; set; }
        public string DocumentationDetail { get; set; }
        public List<string> RequiredDeliverables { get; set; } = new List<string>();
        public string CDEWorkflow { get; set; }
    }
    
    /// <summary>
    /// ISO 19650 - BIM Information Management Standards
    /// Part 1: Concepts and principles
    /// Part 2: Delivery phase (TIDP, BEP, EIR)
    /// Part 3: Operational phase (AIM, AIR)
    /// </summary>
    public static class ISO19650Standards
    {
        public const string Standard = "ISO 19650";
        public const string Part1 = "ISO 19650-1:2018 - Concepts and principles";
        public const string Part2 = "ISO 19650-2:2018 - Delivery phase";
        public const string Part3 = "ISO 19650-3:2020 - Operational phase";
        
        // PART 1 - CONCEPTS AND PRINCIPLES
        
        /// <summary>
        /// Get Level of Information Need description
        /// </summary>
        public static string GetLevelOfInformationNeed(LevelOfInformationNeed level)
        {
            return level switch
            {
                LevelOfInformationNeed.LOI_1 => "Symbolic representation only",
                LevelOfInformationNeed.LOI_2 => "Generic representation (approximate geometry)",
                LevelOfInformationNeed.LOI_3 => "Specific representation (precise geometry)",
                LevelOfInformationNeed.LOI_4 => "Detailed representation (fabrication level)",
                LevelOfInformationNeed.LOI_5 => "As-constructed representation",
                LevelOfInformationNeed.LOI_6 => "As-built with operational data",
                _ => "Not specified"
            };
        }
        
        /// <summary>
        /// Validate naming convention - ISO 19650-2:2018 Annex B
        /// Format: PROJECT-ORIGINATOR-VOLUME-LEVEL-TYPE-ROLE-CLASSIFICATION-NUMBER-REVISION
        /// Example: PRJ-ABC-ZZ-XX-M3-E-40-1001-P01
        /// </summary>
        public static bool ValidateFileNaming(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return false;
            
            string[] parts = filename.Split('-');
            
            // Must have at least 9 parts for full naming convention
            if (parts.Length < 9) return false;
            
            // Basic validation checks
            if (parts[0].Length == 0) return false; // Project
            if (parts[1].Length < 2) return false;  // Originator
            if (parts[2].Length != 2) return false; // Volume/System
            if (parts[3].Length != 2) return false; // Level/Location
            if (parts[4].Length < 2) return false;  // Type (M3, DR, etc.)
            if (parts[5].Length < 1) return false;  // Role (A, E, M, S, etc.)
            
            return true;
        }
        
        /// <summary>
        /// Generate compliant filename
        /// </summary>
        public static string GenerateFileName(
            string project,
            string originator,
            string volume,
            string level,
            string type,
            string role,
            string classification,
            int number,
            string revision)
        {
            return $"{project}-{originator}-{volume}-{level}-{type}-{role}-{classification}-{number:D4}-{revision}";
        }
        
        // PART 2 - DELIVERY PHASE
        
        /// <summary>
        /// Common Data Environment workflow states
        /// </summary>
        public static string GetCDEWorkflowState(InformationContainer container)
        {
            return container switch
            {
                InformationContainer.Work_In_Progress => "WIP - Work in Progress (individual working)",
                InformationContainer.Shared => "SHARED - Shared for review/approval",
                InformationContainer.Published => "PUBLISHED - Approved and issued",
                InformationContainer.Archive => "ARCHIVE - Superseded or historical",
                _ => "Unknown"
            };
        }
        
        /// <summary>
        /// Status codes for information containers
        /// </summary>
        public static string GetStatusCode(string workStage, bool isApproved)
        {
            string baseCode = workStage switch
            {
                "Strategic_Definition" => "S0",
                "Preparation_Brief" => "S1",
                "Concept_Design" => "S2",
                "Spatial_Coordination" => "S3",
                "Technical_Design" => "S4",
                "Manufacturing_Construction" => "S5",
                "Handover" => "S6",
                "Use" => "S7",
                _ => "XX"
            };
            
            // Add approval suffix
            return isApproved ? $"{baseCode}-A" : $"{baseCode}-D";
        }
        
        /// <summary>
        /// Task Information Delivery Plan (TIDP) requirements
        /// </summary>
        public static BIMRequirements GetTIDPRequirements(string projectStage, string discipline)
        {
            var requirements = new BIMRequirements
            {
                InformationDeliveryMilestone = projectStage,
                CDEWorkflow = "WIP → Shared → Published"
            };
            
            // Set LOD based on stage
            if (projectStage.Contains("Concept"))
            {
                requirements.GeometricDetail = LevelOfInformationNeed.LOI_2;
                requirements.AlphanumericDetail = LevelOfInformationNeed.LOI_2;
                requirements.DocumentationDetail = "Outline specifications";
            }
            else if (projectStage.Contains("Technical"))
            {
                requirements.GeometricDetail = LevelOfInformationNeed.LOI_4;
                requirements.AlphanumericDetail = LevelOfInformationNeed.LOI_4;
                requirements.DocumentationDetail = "Full specifications, schedules, calculations";
            }
            else if (projectStage.Contains("Construction"))
            {
                requirements.GeometricDetail = LevelOfInformationNeed.LOI_5;
                requirements.AlphanumericDetail = LevelOfInformationNeed.LOI_5;
                requirements.DocumentationDetail = "As-built documentation";
            }
            
            // Add discipline-specific deliverables
            if (discipline == "Architecture")
            {
                requirements.RequiredDeliverables.AddRange(new[] {
                    "3D model (RVT/IFC)",
                    "2D drawings (DWG/PDF)",
                    "Room data sheets",
                    "Door/window schedules",
                    "Finish schedules"
                });
            }
            else if (discipline == "Structural")
            {
                requirements.RequiredDeliverables.AddRange(new[] {
                    "3D structural model",
                    "Structural calculations",
                    "Reinforcement schedules",
                    "Steel schedules",
                    "Foundation details"
                });
            }
            else if (discipline == "MEP")
            {
                requirements.RequiredDeliverables.AddRange(new[] {
                    "3D MEP model",
                    "Equipment schedules",
                    "Load calculations",
                    "Single line diagrams",
                    "Pipe/duct schedules"
                });
            }
            
            return requirements;
        }
        
        /// <summary>
        /// Exchange Information Requirements (EIR) validation
        /// </summary>
        public static bool ValidateEIR(
            bool hasInformationRequirements,
            bool hasSupportingInformationRequirements,
            bool hasDeliveryStrategy,
            bool hasStandardsCompliance)
        {
            return hasInformationRequirements &&
                   hasSupportingInformationRequirements &&
                   hasDeliveryStrategy &&
                   hasStandardsCompliance;
        }
        
        /// <summary>
        /// BIM Execution Plan (BEP) components
        /// </summary>
        public static List<string> GetBEPRequiredComponents()
        {
            return new List<string>
            {
                "Project information management strategy",
                "Delivery team capabilities and capacity",
                "Information delivery milestones",
                "Standards, methods, and procedures",
                "Common data environment",
                "Information production methods and procedures",
                "Security strategy",
                "Mobilisation plan",
                "Risk register",
                "High level responsibility matrix"
            };
        }
        
        // PART 3 - OPERATIONAL PHASE
        
        /// <summary>
        /// Asset Information Requirements (AIR) for FM
        /// </summary>
        public static List<string> GetAssetInformationRequirements(string assetType)
        {
            var requirements = new List<string>
            {
                "Asset identification and location",
                "Asset specifications",
                "Warranty information",
                "Maintenance schedules",
                "Spare parts information"
            };
            
            if (assetType.Contains("MEP") || assetType.Contains("Equipment"))
            {
                requirements.AddRange(new[] {
                    "Installation date",
                    "Expected lifespan",
                    "Manufacturer contact",
                    "Operating instructions",
                    "Energy consumption data",
                    "Commissioning certificates"
                });
            }
            
            if (assetType.Contains("Building"))
            {
                requirements.AddRange(new[] {
                    "Building systems integration",
                    "Space allocation",
                    "Access control",
                    "Emergency procedures"
                });
            }
            
            return requirements;
        }
        
        /// <summary>
        /// Asset Information Model (AIM) validation
        /// </summary>
        public static bool ValidateAIM(
            bool hasGeometricData,
            bool hasAssetRegister,
            bool hasMaintenanceSchedules,
            bool hasWarrantyInfo,
            bool hasOperatingManuals)
        {
            return hasGeometricData &&
                   hasAssetRegister &&
                   hasMaintenanceSchedules &&
                   hasWarrantyInfo &&
                   hasOperatingManuals;
        }
        
        // DATA QUALITY AND VALIDATION
        
        /// <summary>
        /// Model quality check categories
        /// </summary>
        public static List<string> GetModelQualityCheckCategories()
        {
            return new List<string>
            {
                "Geometric accuracy",
                "Data completeness",
                "Naming conventions",
                "Clash detection",
                "Code compliance",
                "Level of information need",
                "File format compliance (IFC, COBie)",
                "Metadata accuracy",
                "Security and permissions",
                "Version control"
            };
        }
        
        /// <summary>
        /// IFC export validation requirements
        /// </summary>
        public static bool ValidateIFCExport(string ifcVersion, bool hasPropertySets, bool hasClassification)
        {
            // IFC 4 or later required for ISO 19650 compliance
            bool validVersion = ifcVersion.StartsWith("IFC4") || ifcVersion.StartsWith("IFC2x3");
            
            return validVersion && hasPropertySets && hasClassification;
        }
        
        /// <summary>
        /// COBie data requirements
        /// </summary>
        public static List<string> GetCOBieRequiredSheets()
        {
            return new List<string>
            {
                "Contact - Project contacts",
                "Facility - Building information",
                "Floor - Floor/level data",
                "Space - Room/space data",
                "Zone - Functional zones",
                "Type - Equipment types",
                "Component - Installed equipment",
                "System - System groupings",
                "Assembly - Assemblies",
                "Connection - System connections",
                "Spare - Spare parts",
                "Resource - Resources needed",
                "Job - Maintenance jobs",
                "Document - Associated documents"
            };
        }
        
        // INFORMATION SECURITY
        
        /// <summary>
        /// Information security classification levels
        /// </summary>
        public static string GetSecurityClassification(string sensitivity)
        {
            return sensitivity switch
            {
                "Public" => "CL0 - Public information",
                "Internal" => "CL1 - Internal use only",
                "Confidential" => "CL2 - Confidential (authorized personnel)",
                "Restricted" => "CL3 - Restricted (named individuals only)",
                "Secret" => "CL4 - Secret (special handling)",
                _ => "CL1 - Internal use only"
            };
        }
        
        /// <summary>
        /// Collaboration planning - team roles
        /// </summary>
        public static List<string> GetProjectTeamRoles()
        {
            return new List<string>
            {
                "Appointing Party (Client)",
                "Lead Appointed Party (Main Contractor/Consultant)",
                "Appointed Party (Sub-contractors/Sub-consultants)",
                "Task Team (Discipline teams)",
                "Information Manager (BIM Manager)",
                "CDE Administrator",
                "Model Reviewer/Checker",
                "Information Coordinator"
            };
        }
        
        /// <summary>
        /// Delivery milestone validation
        /// </summary>
        public static bool ValidateInformationDelivery(
            InformationDeliveryMilestone milestone,
            bool modelComplete,
            bool dataComplete,
            bool reviewComplete,
            bool approvalObtained)
        {
            // All criteria must be met for milestone completion
            return modelComplete && dataComplete && reviewComplete && approvalObtained;
        }
    }
}
