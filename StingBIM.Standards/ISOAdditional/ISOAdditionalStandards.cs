using System;
using System.Collections.Generic;

namespace StingBIM.Standards.ISOAdditional
{
    /// <summary>
    /// Additional ISO Standards for Construction and BIM Projects
    /// Complements ISO 19650 (BIM) with quality, environmental, and safety management
    /// 
    /// Standards covered:
    /// - ISO 9001: Quality Management Systems
    /// - ISO 14001: Environmental Management Systems
    /// - ISO 45001: Occupational Health and Safety Management
    /// - ISO 3010: Building and Civil Engineering - Vocabulary
    /// - ISO 6707: Building and Civil Engineering - Terminology
    /// - ISO 10005: Quality Management - Guidelines for Quality Plans
    /// </summary>
    public static class ISOAdditionalStandards
    {
        #region ISO 9001:2015 - Quality Management Systems

        /// <summary>
        /// ISO 9001:2015 quality management principles
        /// </summary>
        public static readonly string[] QualityManagementPrinciples = new[]
        {
            "Customer focus",
            "Leadership",
            "Engagement of people",
            "Process approach",
            "Improvement",
            "Evidence-based decision making",
            "Relationship management"
        };

        /// <summary>
        /// ISO 9001 clauses (structure)
        /// </summary>
        public enum ISO9001Clause
        {
            /// <summary>Context of the organization</summary>
            Clause4_Context,
            /// <summary>Leadership</summary>
            Clause5_Leadership,
            /// <summary>Planning</summary>
            Clause6_Planning,
            /// <summary>Support</summary>
            Clause7_Support,
            /// <summary>Operation</summary>
            Clause8_Operation,
            /// <summary>Performance evaluation</summary>
            Clause9_PerformanceEvaluation,
            /// <summary>Improvement</summary>
            Clause10_Improvement
        }

        /// <summary>
        /// Gets quality objectives for construction projects
        /// </summary>
        public static string[] GetQualityObjectives()
        {
            return new[]
            {
                "Meet or exceed client requirements",
                "Complete project on time and within budget",
                "Achieve zero defects at handover",
                "Ensure health and safety compliance",
                "Maintain subcontractor quality standards",
                "Continuous improvement of processes",
                "Customer satisfaction score ≥ 90%"
            };
        }

        /// <summary>
        /// Quality documentation requirements for construction
        /// </summary>
        public static class QualityDocumentation
        {
            /// <summary>Required quality documents</summary>
            public static readonly string[] RequiredDocuments = new[]
            {
                "Quality Policy",
                "Quality Manual",
                "Quality Objectives",
                "Procedures and Work Instructions",
                "Inspection and Test Plans (ITPs)",
                "Quality Control Forms",
                "Non-Conformance Reports (NCRs)",
                "Corrective Action Reports (CARs)",
                "Material Test Certificates",
                "As-Built Drawings",
                "Commissioning Records"
            };

            /// <summary>Document control requirements</summary>
            public static string[] GetDocumentControlRequirements()
            {
                return new[]
                {
                    "Unique document identification",
                    "Revision control and history",
                    "Approval and authorization process",
                    "Distribution control",
                    "Obsolete document management",
                    "Periodic review schedule",
                    "Electronic document management system"
                };
            }
        }

        /// <summary>
        /// Inspection and Test Plan (ITP) requirements
        /// </summary>
        public static class InspectionTestPlan
        {
            /// <summary>ITP stages</summary>
            public enum ITPStage
            {
                /// <summary>H - Hold point (must not proceed)</summary>
                H_HoldPoint,
                /// <summary>W - Witness point (client may witness)</summary>
                W_WitnessPoint,
                /// <summary>R - Review (document review)</summary>
                R_Review,
                /// <summary>S - Surveillance (routine inspection)</summary>
                S_Surveillance
            }

            /// <summary>
            /// Gets typical ITP activities for construction
            /// </summary>
            public static Dictionary<string, ITPStage> GetTypicalITPActivities()
            {
                return new Dictionary<string, ITPStage>
                {
                    { "Site preparation approval", ITPStage.H_HoldPoint },
                    { "Foundation excavation inspection", ITPStage.W_WitnessPoint },
                    { "Reinforcement inspection", ITPStage.H_HoldPoint },
                    { "Concrete pour approval", ITPStage.H_HoldPoint },
                    { "Structural steel welding inspection", ITPStage.W_WitnessPoint },
                    { "MEP rough-in inspection", ITPStage.W_WitnessPoint },
                    { "Waterproofing test", ITPStage.H_HoldPoint },
                    { "Material certificates review", ITPStage.R_Review },
                    { "Finishing works inspection", ITPStage.S_Surveillance },
                    { "Final inspection and handover", ITPStage.H_HoldPoint }
                };
            }
        }

        #endregion

        #region ISO 14001:2015 - Environmental Management Systems

        /// <summary>
        /// ISO 14001:2015 environmental aspects for construction
        /// </summary>
        public static readonly string[] EnvironmentalAspects = new[]
        {
            "Air emissions (dust, exhaust gases)",
            "Water pollution (sediment runoff, chemical spills)",
            "Soil contamination",
            "Noise pollution",
            "Waste generation (construction and demolition waste)",
            "Energy consumption",
            "Natural resource depletion",
            "Habitat disruption",
            "Visual impact"
        };

        /// <summary>
        /// Environmental objectives for construction projects
        /// </summary>
        public static string[] GetEnvironmentalObjectives()
        {
            return new[]
            {
                "Minimize waste generation (target: <5% material waste)",
                "Achieve ≥70% waste recycling rate",
                "Reduce water consumption by 20%",
                "Reduce energy consumption by 15%",
                "Zero environmental incidents",
                "Compliance with all environmental regulations",
                "Minimize noise complaints from neighbors",
                "Protect existing vegetation where possible"
            };
        }

        /// <summary>
        /// Environmental management requirements for construction sites
        /// </summary>
        public static class EnvironmentalManagement
        {
            /// <summary>
            /// Waste management hierarchy
            /// </summary>
            public static readonly string[] WasteHierarchy = new[]
            {
                "1. Prevention - Design out waste",
                "2. Minimization - Reduce waste generation",
                "3. Reuse - Use materials multiple times",
                "4. Recycling - Process for new materials",
                "5. Recovery - Energy from waste",
                "6. Disposal - Last resort only"
            };

            /// <summary>
            /// Gets waste management plan requirements
            /// </summary>
            public static string[] GetWasteManagementRequirements()
            {
                return new[]
                {
                    "Waste segregation at source (minimum 4 streams)",
                    "Designated waste storage areas with signage",
                    "Licensed waste contractors for disposal",
                    "Waste tracking and documentation",
                    "Monthly waste reports (quantities, disposal routes)",
                    "Hazardous waste special handling",
                    "Waste reduction targets and monitoring"
                };
            }

            /// <summary>
            /// Erosion and sediment control measures
            /// </summary>
            public static string[] GetErosionControlMeasures()
            {
                return new[]
                {
                    "Silt fencing around site perimeter",
                    "Sediment traps at drainage points",
                    "Stabilization of exposed soil areas",
                    "Temporary seeding or mulching",
                    "Wheel wash facilities at site exits",
                    "Storm water management plan",
                    "Regular monitoring and maintenance"
                };
            }

            /// <summary>
            /// Dust control measures
            /// </summary>
            public static string[] GetDustControlMeasures()
            {
                return new[]
                {
                    "Water spraying of haul roads",
                    "Speed limits on site (reduce dust generation)",
                    "Covering of stockpiles",
                    "Wetting of demolition areas",
                    "Enclosure of dusty activities",
                    "Use of dust suppression additives",
                    "Wind barriers for exposed areas"
                };
            }
        }

        #endregion

        #region ISO 45001:2018 - Occupational Health and Safety

        /// <summary>
        /// ISO 45001:2018 OH&S hazards in construction
        /// </summary>
        public static readonly string[] ConstructionHazards = new[]
        {
            "Falls from height",
            "Struck by falling objects",
            "Electrocution",
            "Caught in/between machinery",
            "Manual handling injuries",
            "Vehicle/equipment collisions",
            "Excavation collapse",
            "Exposure to hazardous substances",
            "Heat stress/cold stress",
            "Noise-induced hearing loss",
            "Vibration injuries",
            "Fire and explosion"
        };

        /// <summary>
        /// OH&S objectives for construction
        /// </summary>
        public static string[] GetOHSObjectives()
        {
            return new[]
            {
                "Zero fatalities",
                "Zero lost-time injuries",
                "100% incident reporting and investigation",
                "100% toolbox talk attendance",
                "100% PPE compliance",
                "Monthly safety inspections completed",
                "Emergency drill conducted quarterly"
            };
        }

        /// <summary>
        /// Hierarchy of controls for hazard mitigation
        /// </summary>
        public static class HierarchyOfControls
        {
            /// <summary>Control levels (most to least effective)</summary>
            public static readonly string[] ControlLevels = new[]
            {
                "1. Elimination - Remove the hazard",
                "2. Substitution - Replace with less hazardous",
                "3. Engineering Controls - Isolate people from hazard",
                "4. Administrative Controls - Change work practices",
                "5. PPE - Personal Protective Equipment (last resort)"
            };

            /// <summary>
            /// Gets control measures for common construction hazards
            /// </summary>
            public static string[] GetControlMeasures(string hazard)
            {
                return hazard.ToLower() switch
                {
                    "falls from height" => new[]
                    {
                        "Eliminate: Design out work at height where possible",
                        "Engineering: Edge protection, scaffolding, safety nets",
                        "Administrative: Permit to work, supervision, training",
                        "PPE: Full body harness with lanyard (last resort)"
                    },
                    
                    "electrocution" => new[]
                    {
                        "Elimination: De-energize equipment before work",
                        "Engineering: RCD/GFCI protection, proper earthing",
                        "Administrative: Lockout/tagout procedures, competent persons",
                        "PPE: Insulated tools, rubber gloves (for live work)"
                    },
                    
                    "excavation collapse" => new[]
                    {
                        "Engineering: Shoring, sloping, benching of excavations",
                        "Engineering: Trench boxes for deep excavations",
                        "Administrative: Daily excavation inspections, exclusion zones",
                        "PPE: Hard hats for workers near excavations"
                    },
                    
                    _ => new[]
                    {
                        "Risk assessment required",
                        "Apply hierarchy of controls",
                        "Consult with safety professional"
                    }
                };
            }
        }

        /// <summary>
        /// PPE requirements for construction
        /// </summary>
        public static class PPERequirements
        {
            /// <summary>Minimum PPE for construction sites</summary>
            public static readonly string[] MinimumPPE = new[]
            {
                "Safety helmet (hard hat)",
                "Safety footwear (steel toe boots)",
                "High-visibility vest",
                "Safety glasses/goggles",
                "Work gloves"
            };

            /// <summary>
            /// Gets task-specific PPE requirements
            /// </summary>
            public static string[] GetTaskSpecificPPE(string task)
            {
                return task.ToLower() switch
                {
                    "grinding" => new[] { "Face shield", "Hearing protection", "Dust mask" },
                    "welding" => new[] { "Welding helmet", "Welding gloves", "Leather apron", "Welding screen" },
                    "painting" => new[] { "Respirator", "Chemical-resistant gloves", "Coveralls" },
                    "working at height" => new[] { "Full body harness", "Shock-absorbing lanyard" },
                    "demolition" => new[] { "Dust mask/respirator", "Hearing protection", "Heavy-duty gloves" },
                    "confined space" => new[] { "Breathing apparatus", "Gas monitor", "Rescue harness" },
                    _ => MinimumPPE
                };
            }
        }

        #endregion

        #region ISO 6707 - Building and Civil Engineering Vocabulary

        /// <summary>
        /// Standard terminology for construction documentation
        /// Ensures consistent use of terms across international projects
        /// </summary>
        public static class ConstructionVocabulary
        {
            /// <summary>
            /// Key building element terms (ISO 6707 standardized)
            /// </summary>
            public static readonly Dictionary<string, string> BuildingElements = new()
            {
                { "Substructure", "Part of building below ground level including foundations" },
                { "Superstructure", "Part of building above ground level" },
                { "Primary elements", "Load-bearing elements (walls, columns, beams, slabs)" },
                { "Secondary elements", "Non-load-bearing elements (partitions, cladding)" },
                { "Finishing", "Materials applied to surfaces for protection or decoration" },
                { "Services", "Systems providing utilities (mechanical, electrical, plumbing)" },
                { "Fittings", "Items that are fixed but can be removed (cabinets, fixtures)" },
                { "Furniture", "Movable items (chairs, tables, equipment)" }
            };

            /// <summary>
            /// Construction process terms
            /// </summary>
            public static readonly Dictionary<string, string> ProcessTerms = new()
            {
                { "Design", "Process of creating drawings and specifications" },
                { "Procurement", "Process of acquiring materials and services" },
                { "Construction", "Physical building process" },
                { "Commissioning", "Testing and verification of systems" },
                { "Handover", "Transfer of completed building to client" },
                { "Defects liability period", "Period after handover for fixing defects" },
                { "Operation and maintenance", "Ongoing use and upkeep of building" }
            };
        }

        #endregion

        #region ISO 10005 - Quality Plans

        /// <summary>
        /// Quality plan structure per ISO 10005
        /// </summary>
        public static class QualityPlan
        {
            /// <summary>
            /// Required sections in quality plan
            /// </summary>
            public static readonly string[] RequiredSections = new[]
            {
                "1. Scope and field of application",
                "2. Quality plan input",
                "3. Communications and document control",
                "4. Design control",
                "5. Control of purchased products and services",
                "6. Product identification and traceability",
                "7. Process control",
                "8. Inspection and testing",
                "9. Control of inspection, measuring and test equipment",
                "10. Control of nonconformities",
                "11. Corrective and preventive action",
                "12. Handling, storage, packaging, preservation and delivery",
                "13. Quality records",
                "14. Audits",
                "15. Training"
            };

            /// <summary>
            /// Gets quality plan template for construction phase
            /// </summary>
            public static string[] GetConstructionPhaseQualityPlan(string phase)
            {
                return phase.ToLower() switch
                {
                    "foundations" => new[]
                    {
                        "Excavation depth verification",
                        "Bearing capacity testing",
                        "Reinforcement inspection (size, spacing, cover)",
                        "Concrete mix design approval",
                        "Concrete testing (slump, cubes)",
                        "Formwork inspection",
                        "As-built survey"
                    },
                    
                    "structural frame" => new[]
                    {
                        "Reinforcement inspection",
                        "Concrete strength verification",
                        "Dimensional tolerances check",
                        "Structural steel inspection (welding, bolting)",
                        "Construction joints inspection",
                        "Load testing (if required)"
                    },
                    
                    "mep systems" => new[]
                    {
                        "Material approval (brands, specifications)",
                        "Installation per drawings",
                        "Pressure testing (pipes)",
                        "Insulation inspection",
                        "Electrical testing and commissioning",
                        "System balancing",
                        "Performance testing"
                    },
                    
                    "finishes" => new[]
                    {
                        "Surface preparation inspection",
                        "Material approval (samples)",
                        "Workmanship standards check",
                        "Final cleaning",
                        "Snagging/punch list",
                        "Client walkthrough"
                    },
                    
                    _ => new[]
                    {
                        "Define specific quality requirements",
                        "Establish inspection points",
                        "Document acceptance criteria"
                    }
                };
            }
        }

        #endregion

        #region Integrated Management System (IMS)

        /// <summary>
        /// Integration of ISO 9001, 14001, and 45001
        /// Many organizations use integrated management systems
        /// </summary>
        public static class IntegratedManagementSystem
        {
            /// <summary>
            /// Common elements across all three standards
            /// </summary>
            public static readonly string[] CommonElements = new[]
            {
                "Context of the organization",
                "Leadership and commitment",
                "Policy",
                "Objectives and planning",
                "Resources and competence",
                "Communication",
                "Documented information",
                "Operational planning and control",
                "Monitoring and measurement",
                "Internal audit",
                "Management review",
                "Continual improvement"
            };

            /// <summary>
            /// Benefits of integrated management system
            /// </summary>
            public static readonly string[] IMSBenefits = new[]
            {
                "Single audit process (cost savings)",
                "Reduced documentation duplication",
                "Holistic risk management approach",
                "Better resource utilization",
                "Improved communication and coordination",
                "Enhanced organizational performance",
                "Simplified compliance management"
            };

            /// <summary>
            /// Gets IMS policy statement template
            /// </summary>
            public static string GetIMSPolicyTemplate()
            {
                return @"
[Company Name] is committed to:
- Delivering quality construction projects that meet or exceed client expectations
- Protecting the environment and preventing pollution
- Providing a safe and healthy workplace for all workers
- Complying with all applicable legal and other requirements
- Continually improving our management systems and performance

This policy is communicated to all workers and is available to interested parties.
                ";
            }
        }

        #endregion
    }
}
