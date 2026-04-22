using System;
using System.Collections.Generic;

namespace StingBIM.Standards.CIDB
{
    /// <summary>
    /// CIDB - Construction Industry Development Board
    /// Regional framework for construction industry development and contractor registration
    /// 
    /// Based primarily on South African CIDB (most developed in Africa)
    /// Adapted for regional African context
    /// 
    /// Key functions:
    /// - Contractor registration and grading
    /// - Tender processes and procurement
    /// - Skills development and training
    /// - Quality assurance frameworks
    /// - Industry transformation (BEE/local content)
    /// - Construction standards and best practices
    /// </summary>
    public static class CIDBStandards
    {
        #region Contractor Registration and Grading

        /// <summary>
        /// CIDB contractor grading levels
        /// Based on financial capacity and project size capability
        /// </summary>
        public enum ContractorGrade
        {
            /// <summary>Grade 1 - Up to R200,000 (≈$10,000)</summary>
            Grade1_Entry,
            /// <summary>Grade 2 - R200k to R650k (≈$10k-$35k)</summary>
            Grade2_Small,
            /// <summary>Grade 3 - R650k to R2M (≈$35k-$110k)</summary>
            Grade3_Medium,
            /// <summary>Grade 4 - R2M to R4M (≈$110k-$220k)</summary>
            Grade4_Established,
            /// <summary>Grade 5 - R4M to R6.5M (≈$220k-$350k)</summary>
            Grade5_Advanced,
            /// <summary>Grade 6 - R6.5M to R13M (≈$350k-$700k)</summary>
            Grade6_Large,
            /// <summary>Grade 7 - R13M to R40M (≈$700k-$2.2M)</summary>
            Grade7_Major,
            /// <summary>Grade 8 - R40M to R130M (≈$2.2M-$7M)</summary>
            Grade8_Mega,
            /// <summary>Grade 9 - No limit (>R130M / >$7M)</summary>
            Grade9_Unlimited
        }

        /// <summary>
        /// Work class categories
        /// </summary>
        public enum WorkClass
        {
            /// <summary>CE - Civil Engineering</summary>
            CE_CivilEngineering,
            /// <summary>GB - General Building</summary>
            GB_GeneralBuilding,
            /// <summary>EB - Electrical Engineering Building</summary>
            EB_ElectricalBuilding,
            /// <summary>ME - Mechanical Engineering</summary>
            ME_MechanicalEngineering,
            /// <summary>SP - Specialist Works</summary>
            SP_Specialist
        }

        /// <summary>
        /// Gets tender value range for contractor grade
        /// </summary>
        public static (double MinUSD, double MaxUSD) GetTenderValueRange(ContractorGrade grade)
        {
            return grade switch
            {
                ContractorGrade.Grade1_Entry => (0, 10000),
                ContractorGrade.Grade2_Small => (10000, 35000),
                ContractorGrade.Grade3_Medium => (35000, 110000),
                ContractorGrade.Grade4_Established => (110000, 220000),
                ContractorGrade.Grade5_Advanced => (220000, 350000),
                ContractorGrade.Grade6_Large => (350000, 700000),
                ContractorGrade.Grade7_Major => (700000, 2200000),
                ContractorGrade.Grade8_Mega => (2200000, 7000000),
                ContractorGrade.Grade9_Unlimited => (7000000, double.MaxValue),
                _ => (0, 10000)
            };
        }

        /// <summary>
        /// Registration requirements for contractor grading
        /// </summary>
        public static class RegistrationRequirements
        {
            /// <summary>
            /// Gets required documentation for registration
            /// </summary>
            public static string[] GetRequiredDocuments(ContractorGrade targetGrade)
            {
                var baseDocuments = new List<string>
                {
                    "Company registration certificate",
                    "Tax clearance certificate",
                    "Valid tax registration number",
                    "Company profile and organogram",
                    "CVs of key personnel",
                    "Proof of address",
                    "Banking details and bank confirmation",
                    "Directors' IDs and proof of residence"
                };

                if (targetGrade >= ContractorGrade.Grade3_Medium)
                {
                    baseDocuments.AddRange(new[]
                    {
                        "Audited financial statements (3 years)",
                        "List of completed projects with references",
                        "Professional indemnity insurance",
                        "Public liability insurance",
                        "Equipment list and ownership proof",
                        "Health and safety policy",
                        "Quality management policy"
                    });
                }

                if (targetGrade >= ContractorGrade.Grade6_Large)
                {
                    baseDocuments.AddRange(new[]
                    {
                        "ISO 9001 certification (or equivalent)",
                        "ISO 14001 certification (environmental)",
                        "ISO 45001 certification (health & safety)",
                        "BEE/local content certificate",
                        "Professional registrations (engineers, architects)",
                        "Employee register and skills matrix",
                        "Statutory compliance (labor, tax, safety)"
                    });
                }

                return baseDocuments.ToArray();
            }

            /// <summary>
            /// Financial capacity requirements
            /// </summary>
            public static string[] GetFinancialCapacityRequirements(ContractorGrade grade)
            {
                return new[]
                {
                    $"Net worth: Minimum equivalent to grade tender limit",
                    $"Working capital: 30% of grade limit minimum",
                    $"Annual turnover: 3x grade limit (3-year average)",
                    $"Unencumbered assets: Security for bonding",
                    $"Bank facilities: Lines of credit established",
                    $"Financial ratios: Current ratio >1.5, debt/equity <2.0",
                    $"No adverse judgments or tax defaults",
                    $"Audited statements by registered accountant (Grade {(int)grade}+)"
                };
            }

            /// <summary>
            /// Technical capacity requirements
            /// </summary>
            public static string[] GetTechnicalCapacityRequirements(ContractorGrade grade)
            {
                var requirements = new List<string>
                {
                    "Qualified site manager/foreman",
                    "Safety officer (trained and certified)",
                    "Basic construction equipment"
                };

                if (grade >= ContractorGrade.Grade3_Medium)
                {
                    requirements.AddRange(new[]
                    {
                        "Professional engineer or technician on staff",
                        "Quality control personnel",
                        "Surveying equipment and operator",
                        "Project management software capability"
                    });
                }

                if (grade >= ContractorGrade.Grade6_Large)
                {
                    requirements.AddRange(new[]
                    {
                        "Multiple qualified engineers (structural, civil, etc.)",
                        "Dedicated QA/QC department",
                        "Equipment fleet (owned or leased)",
                        "Design capability (in-house or partnered)",
                        "Advanced project management systems",
                        "Document control and BIM capability"
                    });
                }

                return requirements.ToArray();
            }
        }

        #endregion

        #region Tender and Procurement Processes

        /// <summary>
        /// Procurement methods
        /// </summary>
        public enum ProcurementMethod
        {
            /// <summary>Open competitive bidding</summary>
            OpenTender,
            /// <summary>Restricted tender (pre-qualified contractors)</summary>
            RestrictedTender,
            /// <summary>Two-stage tender (technical then price)</summary>
            TwoStageTender,
            /// <summary>Negotiated tender</summary>
            NegotiatedTender,
            /// <summary>Request for quotations (small works)</summary>
            RFQ,
            /// <summary>Design-build tender</summary>
            DesignBuild,
            /// <summary>Public-private partnership</summary>
            PPP
        }

        /// <summary>
        /// Tender evaluation criteria
        /// </summary>
        public static class TenderEvaluation
        {
            /// <summary>
            /// Standard evaluation criteria with weightings
            /// </summary>
            public static Dictionary<string, double> GetEvaluationCriteria(bool includesTransformation)
            {
                var criteria = new Dictionary<string, double>
                {
                    { "Price/Cost", 80.0 },
                    { "Technical Capability", 10.0 },
                    { "Experience and Track Record", 5.0 },
                    { "Methodology and Program", 5.0 }
                };

                if (includesTransformation)
                {
                    // Adjust for BEE/local content points
                    criteria = new Dictionary<string, double>
                    {
                        { "Price/Cost", 70.0 },
                        { "Technical Capability", 10.0 },
                        { "Experience and Track Record", 5.0 },
                        { "Methodology and Program", 5.0 },
                        { "BEE/Local Content", 10.0 }
                    };
                }

                return criteria;
            }

            /// <summary>
            /// Price evaluation methods
            /// </summary>
            public enum PriceEvaluationMethod
            {
                /// <summary>Lowest conforming bid wins</summary>
                LowestPrice,
                /// <summary>90/10 preference system (price + preference points)</summary>
                Preference90_10,
                /// <summary>80/20 preference system</summary>
                Preference80_20,
                /// <summary>Quality-cost based selection (QCBS)</summary>
                QCBS
            }

            /// <summary>
            /// Calculates preference points for local content/BEE
            /// </summary>
            public static double CalculatePreferencePoints(
                double beeLevel, 
                double localContentPercent,
                PriceEvaluationMethod method)
            {
                double maxPoints = method switch
                {
                    PriceEvaluationMethod.Preference90_10 => 10.0,
                    PriceEvaluationMethod.Preference80_20 => 20.0,
                    _ => 0.0
                };

                // Simplified calculation (actual formula varies by country)
                double beePoints = (beeLevel / 8.0) * (maxPoints * 0.7); // 70% for BEE
                double localPoints = (localContentPercent / 100.0) * (maxPoints * 0.3); // 30% for local content

                return Math.Min(beePoints + localPoints, maxPoints);
            }
        }

        /// <summary>
        /// Tender documentation requirements
        /// </summary>
        public static class TenderDocumentation
        {
            /// <summary>
            /// Standard tender documents
            /// </summary>
            public static readonly string[] StandardDocuments = new[]
            {
                "Invitation to Tender",
                "Instructions to Tenderers",
                "Conditions of Tender",
                "Form of Tender and Appendices",
                "Contract Data (Particular Conditions)",
                "General Conditions of Contract",
                "Specifications (Technical)",
                "Drawings and Schedules",
                "Bills of Quantities / Schedule of Rates",
                "Health and Safety Specification",
                "Quality Requirements"
            };

            /// <summary>
            /// Contractor submission requirements
            /// </summary>
            public static string[] GetSubmissionRequirements()
            {
                return new[]
                {
                    "Completed tender forms (signed and stamped)",
                    "Priced bills of quantities",
                    "Program of works (Gantt chart)",
                    "Methodology statement",
                    "CVs of key personnel to be deployed",
                    "List of subcontractors (if any)",
                    "Equipment to be used",
                    "Safety plan",
                    "Quality assurance plan",
                    "Valid tax clearance certificate",
                    "CIDB registration certificate (correct grade)",
                    "Company registration documents",
                    "BEE certificate (where applicable)",
                    "Joint venture agreement (if JV tender)",
                    "Proof of site visit",
                    "Tender guarantee/bid bond (if required)"
                };
            }
        }

        #endregion

        #region Skills Development and Training

        /// <summary>
        /// Skills development framework
        /// Essential for industry growth and transformation
        /// </summary>
        public static class SkillsDevelopment
        {
            /// <summary>
            /// Skill categories in construction
            /// </summary>
            public enum SkillCategory
            {
                /// <summary>Professional (engineers, architects)</summary>
                Professional,
                /// <summary>Technical (technicians, supervisors)</summary>
                Technical,
                /// <summary>Artisan/Craft (electricians, plumbers, masons)</summary>
                Artisan,
                /// <summary>Semi-skilled (operators, assistants)</summary>
                SemiSkilled,
                /// <summary>General labor</summary>
                GeneralLabor
            }

            /// <summary>
            /// Skills development requirements for contractors
            /// </summary>
            public static string[] GetSkillsDevelopmentRequirements(ContractorGrade grade)
            {
                var requirements = new List<string>
                {
                    "Skills development levy payment (1-2% of payroll)",
                    "Training plan submitted annually",
                    "Apprenticeship programs for artisans",
                    "On-the-job training for semi-skilled workers"
                };

                if (grade >= ContractorGrade.Grade4_Established)
                {
                    requirements.AddRange(new[]
                    {
                        "Learnerships for youth employment",
                        "Bursaries for university students",
                        "Partnerships with TVET institutions",
                        "Mentorship programs for emerging contractors",
                        "Health and safety training for all workers"
                    });
                }

                if (grade >= ContractorGrade.Grade7_Major)
                {
                    requirements.AddRange(new[]
                    {
                        "In-house training center",
                        "Professional development programs",
                        "Management training and leadership development",
                        "Technical skills transfer to local staff",
                        "Annual skills audit and gap analysis"
                    });
                }

                return requirements.ToArray();
            }

            /// <summary>
            /// Critical skills shortages in African construction
            /// </summary>
            public static readonly string[] CriticalSkillsShortages = new[]
            {
                "Structural Engineers",
                "Project Managers (PMP/PRINCE2)",
                "Quantity Surveyors",
                "Construction Managers",
                "Electrical Engineers (power systems)",
                "HVAC Technicians",
                "BIM Specialists",
                "Welders (coded welders)",
                "Steel Fixers (rebar)",
                "Crane Operators",
                "QA/QC Inspectors",
                "Health and Safety Officers"
            };

            /// <summary>
            /// Training institutions in Africa
            /// </summary>
            public static readonly string[] TrainingInstitutions = new[]
            {
                "Technical and Vocational Education and Training (TVET) centers",
                "Universities (engineering, architecture, construction management)",
                "Professional bodies (engineering councils, SACPCMP, etc.)",
                "Contractor development programs (CIDB, MBA programs)",
                "International certifications (PMP, PRINCE2, NEBOSH)",
                "Manufacturer training (equipment, materials)",
                "Industry associations (MBA, SAFCEC, etc.)"
            };
        }

        #endregion

        #region Quality Assurance Framework

        /// <summary>
        /// Quality assurance requirements for construction projects
        /// </summary>
        public static class QualityAssurance
        {
            /// <summary>
            /// Quality management system requirements
            /// </summary>
            public static string[] GetQMSRequirements(ContractorGrade grade)
            {
                var requirements = new List<string>
                {
                    "Quality policy documented",
                    "Site quality control plan",
                    "Inspection and test plans (ITPs)",
                    "Non-conformance reporting system",
                    "Corrective action procedures"
                };

                if (grade >= ContractorGrade.Grade5_Advanced)
                {
                    requirements.AddRange(new[]
                    {
                        "ISO 9001 certification recommended",
                        "Internal quality audits quarterly",
                        "Management review meetings",
                        "Continuous improvement program",
                        "Customer satisfaction tracking"
                    });
                }

                return requirements.ToArray();
            }

            /// <summary>
            /// Material testing and certification requirements
            /// </summary>
            public static class MaterialTesting
            {
                /// <summary>
                /// Gets testing frequency for materials
                /// </summary>
                public static string GetTestingFrequency(string material)
                {
                    return material.ToLower() switch
                    {
                        "concrete" => "1 set of cubes per 50m³ or per day (whichever greater)",
                        "steel reinforcement" => "1 sample per 20 tons or per batch",
                        "cement" => "1 sample per 30 tons",
                        "aggregates" => "1 sample per 500m³",
                        "bricks/blocks" => "1 sample per 10,000 units",
                        "structural steel" => "Mill certificates + random sampling",
                        "soil" => "Per design requirements (CBR, compaction, etc.)",
                        _ => "As specified in project quality plan"
                    };
                }

                /// <summary>
                /// Accredited testing laboratories requirement
                /// </summary>
                public static string[] GetLaboratoryRequirements()
                {
                    return new[]
                    {
                        "SANAS/ISO 17025 accredited laboratories",
                        "NATA accreditation (Australia)",
                        "UKAS accreditation (UK)",
                        "Competent testing personnel",
                        "Calibrated equipment (valid certificates)",
                        "Test reports signed by qualified person",
                        "Traceability of samples",
                        "Chain of custody documentation"
                    };
                }
            }

            /// <summary>
            /// Defects management
            /// </summary>
            public static class DefectsManagement
            {
                /// <summary>Defects liability period</summary>
                public const int StandardDefectsLiabilityMonths = 12;

                /// <summary>
                /// Defect categories
                /// </summary>
                public enum DefectCategory
                {
                    /// <summary>Critical - Safety or structural</summary>
                    Critical,
                    /// <summary>Major - Functionality impaired</summary>
                    Major,
                    /// <summary>Minor - Cosmetic or small issues</summary>
                    Minor,
                    /// <summary>Latent - Hidden defects appearing later</summary>
                    Latent
                }

                /// <summary>
                /// Gets rectification timeline for defect category
                /// </summary>
                public static int GetRectificationDays(DefectCategory category)
                {
                    return category switch
                    {
                        DefectCategory.Critical => 1,    // Immediate
                        DefectCategory.Major => 7,       // 1 week
                        DefectCategory.Minor => 30,      // 1 month
                        DefectCategory.Latent => 60,     // 2 months
                        _ => 30
                    };
                }
            }
        }

        #endregion

        #region Transformation and Local Content

        /// <summary>
        /// Broad-Based Black Economic Empowerment (South Africa)
        /// Adapted as local content/empowerment for other African countries
        /// </summary>
        public static class TransformationFramework
        {
            /// <summary>
            /// BEE/Empowerment levels
            /// </summary>
            public enum EmpowermentLevel
            {
                /// <summary>Level 1 - 135% procurement recognition (>100% black-owned)</summary>
                Level1,
                /// <summary>Level 2 - 125% procurement recognition</summary>
                Level2,
                /// <summary>Level 3 - 110% procurement recognition</summary>
                Level3,
                /// <summary>Level 4 - 100% procurement recognition</summary>
                Level4,
                /// <summary>Level 5-8 - 80-100% recognition</summary>
                Level5to8,
                /// <summary>Non-compliant</summary>
                NonCompliant
            }

            /// <summary>
            /// BEE scorecard elements (South African model)
            /// </summary>
            public static readonly Dictionary<string, double> BEEScorecard = new()
            {
                { "Ownership", 25.0 },
                { "Management Control", 19.0 },
                { "Skills Development", 20.0 },
                { "Enterprise and Supplier Development", 40.0 },
                { "Socio-Economic Development", 5.0 }
            };

            /// <summary>
            /// Local content requirements (adapted for Africa)
            /// </summary>
            public static class LocalContent
            {
                /// <summary>
                /// Gets minimum local content requirement
                /// </summary>
                public static double GetMinimumLocalContent(
                    string projectType,
                    double projectValueUSD)
                {
                    if (projectValueUSD < 100000)
                        return 60.0; // 60% local content for small projects

                    return projectType.ToLower() switch
                    {
                        "infrastructure" => 70.0,
                        "building" => 65.0,
                        "civil works" => 75.0,
                        _ => 60.0
                    };
                }

                /// <summary>
                /// Local content elements
                /// </summary>
                public static readonly string[] LocalContentElements = new[]
                {
                    "Local labor (citizens and residents)",
                    "Local materials (produced within country)",
                    "Local subcontractors (registered locally)",
                    "Local equipment hire",
                    "Local professional services",
                    "Skills transfer to local personnel",
                    "Local enterprise development"
                };

                /// <summary>
                /// Calculating local content percentage
                /// </summary>
                public static double CalculateLocalContent(
                    double localLaborCost,
                    double localMaterialCost,
                    double localServiceCost,
                    double totalProjectCost)
                {
                    double localContent = localLaborCost + localMaterialCost + localServiceCost;
                    return (localContent / totalProjectCost) * 100.0;
                }
            }

            /// <summary>
            /// Emerging contractor development
            /// </summary>
            public static class ContractorDevelopment
            {
                /// <summary>
                /// Subcontracting requirements for large contractors
                /// </summary>
                public static string[] GetSubcontractingRequirements(ContractorGrade grade)
                {
                    if (grade >= ContractorGrade.Grade6_Large)
                    {
                        return new[]
                        {
                            "Minimum 30% subcontracting to emerging contractors",
                            "Grade 1-4 contractors targeted for subcontracting",
                            "Mentorship and support to subcontractors",
                            "Timely payment to subcontractors (30 days)",
                            "Joint venture partnerships encouraged",
                            "Skills transfer programs",
                            "Reporting on subcontractor performance"
                        };
                    }

                    return new string[0];
                }

                /// <summary>
                /// Support mechanisms for emerging contractors
                /// </summary>
                public static readonly string[] SupportMechanisms = new[]
                {
                    "Set-asides (projects reserved for emerging contractors)",
                    "Price preference (10-15% for emerging contractors)",
                    "Access to finance (development finance institutions)",
                    "Mentorship programs (pairing with established contractors)",
                    "Training and capacity building",
                    "Business development support",
                    "Joint ventures (equity partnerships)",
                    "Performance guarantees (bonding support)"
                };
            }
        }

        #endregion

        #region Health, Safety, and Environment

        /// <summary>
        /// Health and safety requirements for construction
        /// </summary>
        public static class HealthAndSafety
        {
            /// <summary>
            /// Mandatory safety documentation
            /// </summary>
            public static readonly string[] MandatorySafetyDocuments = new[]
            {
                "Health and Safety Plan (project-specific)",
                "Risk Assessment (hazard identification)",
                "Method Statements (for high-risk activities)",
                "Safety File (legal requirement - maintained throughout)",
                "Emergency Response Plan",
                "Incident Investigation Procedures",
                "Safety Induction Register",
                "Toolbox Talk Records",
                "Site Safety Inspection Reports",
                "Accident/Incident Register"
            };

            /// <summary>
            /// Gets safety officer requirements
            /// </summary>
            public static string GetSafetyOfficerRequirement(int numberOfWorkers)
            {
                if (numberOfWorkers < 20)
                    return "Site manager responsible for safety (with basic training)";
                else if (numberOfWorkers < 50)
                    return "Part-time safety officer (qualified)";
                else if (numberOfWorkers < 100)
                    return "Full-time safety officer (NEBOSH or equivalent)";
                else
                    return "Full-time safety officer + safety team (1 per 100 workers)";
            }

            /// <summary>
            /// Environmental management requirements
            /// </summary>
            public static string[] GetEnvironmentalRequirements(double projectValueUSD)
            {
                var requirements = new List<string>
                {
                    "Environmental Management Plan (EMP)",
                    "Waste management plan",
                    "Erosion and sediment control",
                    "Dust control measures",
                    "Noise management",
                    "Water pollution prevention"
                };

                if (projectValueUSD > 1000000)
                {
                    requirements.AddRange(new[]
                    {
                        "Environmental Impact Assessment (EIA)",
                        "Environmental authorization certificate",
                        "Environmental Control Officer (ECO) on site",
                        "Environmental monitoring and reporting",
                        "Compliance with ISO 14001 recommended"
                    });
                }

                return requirements.ToArray();
            }
        }

        #endregion

        #region Performance Monitoring and Compliance

        /// <summary>
        /// Contractor performance monitoring
        /// </summary>
        public static class PerformanceMonitoring
        {
            /// <summary>
            /// Key performance indicators (KPIs) for contractors
            /// </summary>
            public static readonly Dictionary<string, string> StandardKPIs = new()
            {
                { "Time Performance", "% on-time completion (target: 100%)" },
                { "Cost Performance", "Budget variance (target: ±5%)" },
                { "Quality Performance", "Defects per 100m² (target: <5)" },
                { "Safety Performance", "Lost Time Injury Frequency Rate (target: <1.0)" },
                { "Environmental Performance", "Incidents and non-compliances (target: 0)" },
                { "Client Satisfaction", "Score out of 10 (target: ≥8)" },
                { "Subcontractor Payment", "Days to payment (target: ≤30)" },
                { "Skills Development", "Training hours per employee (target: ≥40/year)" }
            };

            /// <summary>
            /// Performance rating system
            /// </summary>
            public enum PerformanceRating
            {
                /// <summary>Excellent - Exceeds all expectations</summary>
                Excellent,
                /// <summary>Good - Meets all requirements</summary>
                Good,
                /// <summary>Satisfactory - Minor issues</summary>
                Satisfactory,
                /// <summary>Poor - Significant deficiencies</summary>
                Poor,
                /// <summary>Unacceptable - Critical failures</summary>
                Unacceptable
            }

            /// <summary>
            /// Consequences of poor performance
            /// </summary>
            public static string[] GetPoorPerformanceConsequences()
            {
                return new[]
                {
                    "Warning letter and improvement plan",
                    "Increased supervision and monitoring",
                    "Withholding of payment (for defective work)",
                    "Financial penalties (liquidated damages)",
                    "Suspension from tender lists (temporary)",
                    "Reduction in CIDB grading",
                    "Deregistration (severe/repeat violations)",
                    "Legal action for breach of contract"
                };
            }
        }

        #endregion
    }
}
