using System;
using System.Collections.Generic;

namespace StingBIM.Standards.GreenBuilding
{
    /// <summary>
    /// Green Building Standards for Sustainable Construction
    /// 
    /// Standards covered:
    /// - EDGE (Excellence in Design for Greater Efficiencies) - IFC/World Bank
    /// - LEED (Leadership in Energy and Environmental Design) - USGBC
    /// - Green Star Africa - Green Building Council South Africa
    /// 
    /// Critical for Africa: EDGE is designed specifically for emerging markets
    /// and is widely used across Africa, including Uganda, Kenya, Tanzania, Rwanda
    /// </summary>
    public static class GreenBuildingStandards
    {
        #region EDGE Certification

        /// <summary>
        /// EDGE (Excellence in Design for Greater Efficiencies)
        /// Developed by IFC (International Finance Corporation - World Bank Group)
        /// Specifically designed for emerging markets
        /// </summary>
        public static class EDGECertification
        {
            /// <summary>
            /// EDGE certification levels
            /// </summary>
            public enum EDGELevel
            {
                /// <summary>EDGE Certified - 20% savings in each category</summary>
                EDGE_Certified,
                /// <summary>EDGE Advanced - 40% savings in each category</summary>
                EDGE_Advanced,
                /// <summary>EDGE Zero Carbon - Net zero carbon emissions</summary>
                EDGE_ZeroCarbon
            }

            /// <summary>
            /// Three performance categories in EDGE
            /// </summary>
            public static readonly string[] PerformanceCategories = new[]
            {
                "Energy - Reduced energy consumption",
                "Water - Reduced water consumption",
                "Materials - Reduced embodied energy in materials"
            };

            /// <summary>
            /// Gets minimum savings required for certification level
            /// </summary>
            public static (double Energy, double Water, double Materials) 
                GetRequiredSavings(EDGELevel level)
            {
                return level switch
                {
                    EDGELevel.EDGE_Certified => (20.0, 20.0, 20.0),
                    EDGELevel.EDGE_Advanced => (40.0, 40.0, 40.0),
                    EDGELevel.EDGE_ZeroCarbon => (100.0, 20.0, 20.0), // 100% energy reduction via renewables
                    _ => (20.0, 20.0, 20.0)
                };
            }

            /// <summary>
            /// EDGE measures for energy efficiency
            /// </summary>
            public static class EnergyMeasures
            {
                public static readonly Dictionary<string, double> MeasuresSavings = new()
                {
                    // Building envelope
                    { "External wall insulation", 5.0 },
                    { "Roof insulation", 8.0 },
                    { "High-performance glazing", 12.0 },
                    { "Reduced window-to-wall ratio", 7.0 },
                    { "Light-colored roof", 3.0 },
                    { "External shading devices", 6.0 },
                    
                    // HVAC
                    { "High-efficiency air conditioning (SEER >14)", 15.0 },
                    { "Natural ventilation design", 25.0 },
                    { "Ceiling fans", 10.0 },
                    
                    // Lighting
                    { "LED lighting throughout", 12.0 },
                    { "Daylight sensors", 8.0 },
                    { "Occupancy sensors", 5.0 },
                    
                    // Renewable energy
                    { "Solar PV system (5kW)", 20.0 },
                    { "Solar water heating", 15.0 },
                    
                    // Appliances
                    { "Energy-efficient refrigerator", 3.0 },
                    { "Energy-efficient water heater", 8.0 }
                };

                /// <summary>
                /// Gets recommended measures to achieve target savings
                /// </summary>
                public static string[] GetRecommendedMeasures(double targetSavings)
                {
                    var recommended = new List<string>();
                    double accumulatedSavings = 0;

                    // Priority order: High impact measures first
                    var priorityMeasures = new[]
                    {
                        "Natural ventilation design",
                        "Solar PV system (5kW)",
                        "High-efficiency air conditioning (SEER >14)",
                        "Solar water heating",
                        "LED lighting throughout",
                        "High-performance glazing",
                        "Roof insulation",
                        "Daylight sensors"
                    };

                    foreach (var measure in priorityMeasures)
                    {
                        if (accumulatedSavings >= targetSavings) break;
                        
                        if (MeasuresSavings.ContainsKey(measure))
                        {
                            recommended.Add($"{measure} ({MeasuresSavings[measure]}% savings)");
                            accumulatedSavings += MeasuresSavings[measure];
                        }
                    }

                    return recommended.ToArray();
                }
            }

            /// <summary>
            /// EDGE measures for water efficiency
            /// </summary>
            public static class WaterMeasures
            {
                public static readonly Dictionary<string, double> MeasuresSavings = new()
                {
                    // Fixtures
                    { "Dual-flush toilets", 25.0 },
                    { "Low-flow showerheads (8 LPM)", 20.0 },
                    { "Low-flow faucets (6 LPM)", 15.0 },
                    { "Waterless urinals", 30.0 },
                    
                    // Appliances
                    { "High-efficiency washing machine", 10.0 },
                    { "Efficient dishwasher", 8.0 },
                    
                    // Landscape
                    { "Drip irrigation", 40.0 },
                    { "Native/drought-tolerant landscaping", 50.0 },
                    { "Rainwater harvesting for irrigation", 35.0 },
                    
                    // Water reuse
                    { "Greywater recycling system", 30.0 },
                    { "Rainwater harvesting for non-potable", 25.0 }
                };

                /// <summary>
                /// Gets recommended water efficiency measures
                /// </summary>
                public static string[] GetRecommendedMeasures(double targetSavings)
                {
                    var recommended = new List<string>();
                    double accumulatedSavings = 0;

                    var priorityMeasures = new[]
                    {
                        "Native/drought-tolerant landscaping",
                        "Drip irrigation",
                        "Rainwater harvesting for irrigation",
                        "Waterless urinals",
                        "Dual-flush toilets",
                        "Low-flow showerheads (8 LPM)",
                        "Low-flow faucets (6 LPM)"
                    };

                    foreach (var measure in priorityMeasures)
                    {
                        if (accumulatedSavings >= targetSavings) break;
                        
                        if (MeasuresSavings.ContainsKey(measure))
                        {
                            recommended.Add($"{measure} ({MeasuresSavings[measure]}% savings)");
                            accumulatedSavings += MeasuresSavings[measure];
                        }
                    }

                    return recommended.ToArray();
                }
            }

            /// <summary>
            /// EDGE measures for materials efficiency
            /// </summary>
            public static class MaterialsMeasures
            {
                public static readonly Dictionary<string, double> MeasuresSavings = new()
                {
                    // Structure
                    { "Optimized concrete mix (fly ash/GGBS)", 15.0 },
                    { "Recycled steel reinforcement", 8.0 },
                    { "Hollow core slabs", 12.0 },
                    
                    // Walls
                    { "Concrete blocks (hollow)", 10.0 },
                    { "Autoclaved aerated concrete (AAC)", 18.0 },
                    { "Recycled brick", 12.0 },
                    
                    // Roofing
                    { "Cool roof materials", 5.0 },
                    { "Recycled metal roofing", 8.0 },
                    
                    // Finishes
                    { "Recycled floor tiles", 6.0 },
                    { "Low-VOC paints", 3.0 },
                    { "Recycled gypsum board", 5.0 },
                    
                    // Windows
                    { "Double glazing", 10.0 },
                    { "Aluminum frames (recycled content)", 7.0 }
                };
            }

            /// <summary>
            /// EDGE certification process
            /// </summary>
            public static string[] GetCertificationProcess()
            {
                return new[]
                {
                    "1. Register project on EDGE online platform",
                    "2. Input building characteristics and proposed measures",
                    "3. EDGE software calculates projected savings",
                    "4. Achieve minimum 20% savings in each category",
                    "5. Submit design for preliminary certification",
                    "6. Construct building according to EDGE design",
                    "7. Document implementation of measures",
                    "8. EDGE Auditor conducts site verification",
                    "9. Submit verification report",
                    "10. Receive final EDGE certification"
                };
            }

            /// <summary>
            /// Benefits of EDGE certification in Africa
            /// </summary>
            public static string[] GetEDGEBenefitsAfrica()
            {
                return new[]
                {
                    "20-30% reduction in utility bills",
                    "Access to green financing (lower interest rates)",
                    "Higher property values and rental premiums",
                    "Faster sale/lease of certified buildings",
                    "Reduced environmental impact",
                    "Improved occupant comfort and health",
                    "Compliance with emerging green building codes",
                    "Corporate social responsibility credentials",
                    "IFC/World Bank recognition and support"
                };
            }

            /// <summary>
            /// EDGE markets in Africa (active as of 2024)
            /// </summary>
            public static readonly string[] EDGEMarketsAfrica = new[]
            {
                "South Africa",
                "Kenya",
                "Uganda",
                "Tanzania",
                "Rwanda",
                "Ghana",
                "Nigeria",
                "Ethiopia",
                "Zambia",
                "Zimbabwe",
                "Morocco",
                "Egypt"
            };
        }

        #endregion

        #region LEED Certification

        /// <summary>
        /// LEED (Leadership in Energy and Environmental Design)
        /// Developed by USGBC (US Green Building Council)
        /// International green building certification
        /// </summary>
        public static class LEEDCertification
        {
            /// <summary>
            /// LEED certification levels
            /// </summary>
            public enum LEEDLevel
            {
                /// <summary>Certified - 40-49 points</summary>
                Certified,
                /// <summary>Silver - 50-59 points</summary>
                Silver,
                /// <summary>Gold - 60-79 points</summary>
                Gold,
                /// <summary>Platinum - 80+ points</summary>
                Platinum
            }

            /// <summary>
            /// Gets points required for certification level
            /// </summary>
            public static (int Minimum, int Maximum) GetPointsRequired(LEEDLevel level)
            {
                return level switch
                {
                    LEEDLevel.Certified => (40, 49),
                    LEEDLevel.Silver => (50, 59),
                    LEEDLevel.Gold => (60, 79),
                    LEEDLevel.Platinum => (80, 110),
                    _ => (40, 49)
                };
            }

            /// <summary>
            /// LEED v4.1 BD+C (Building Design and Construction) categories
            /// </summary>
            public static class LEEDCategories
            {
                public static readonly Dictionary<string, int> MaximumPoints = new()
                {
                    { "Integrative Process", 1 },
                    { "Location and Transportation", 16 },
                    { "Sustainable Sites", 10 },
                    { "Water Efficiency", 11 },
                    { "Energy and Atmosphere", 33 },
                    { "Materials and Resources", 13 },
                    { "Indoor Environmental Quality", 16 },
                    { "Innovation", 6 },
                    { "Regional Priority", 4 }
                };

                /// <summary>Total possible points</summary>
                public const int TotalPoints = 110;
            }

            /// <summary>
            /// Key LEED prerequisites (mandatory)
            /// </summary>
            public static string[] GetMandatoryPrerequisites()
            {
                return new[]
                {
                    "Construction Activity Pollution Prevention",
                    "Minimum Energy Performance (5% improvement)",
                    "Building-Level Energy Metering",
                    "Fundamental Refrigerant Management",
                    "Outdoor Water Use Reduction",
                    "Indoor Water Use Reduction (20% reduction)",
                    "Storage and Collection of Recyclables",
                    "Construction and Demolition Waste Management",
                    "Minimum Indoor Air Quality Performance",
                    "Environmental Tobacco Smoke Control"
                };
            }

            /// <summary>
            /// High-value LEED credits for African projects
            /// </summary>
            public static class AfricaFocusCredits
            {
                public static readonly Dictionary<string, string> HighValueCredits = new()
                {
                    { "Optimize Energy Performance (18 pts)", 
                      "20-50% improvement over baseline. Solar PV highly effective in Africa." },
                    
                    { "Renewable Energy Production (3 pts)", 
                      "5% of energy from on-site renewables. Abundant solar in most of Africa." },
                    
                    { "Outdoor Water Use Reduction (2 pts)", 
                      "Use native plants (no irrigation). Critical for water-scarce regions." },
                    
                    { "Indoor Water Use Reduction (6 pts)", 
                      "30-40% reduction. Low-flow fixtures reduce pressure on limited supply." },
                    
                    { "Rainwater Management (3 pts)", 
                      "Manage runoff. Reduces infrastructure burden in cities." },
                    
                    { "Heat Island Reduction (2 pts)", 
                      "Cool roofs/pavements. Important for hot African climates." },
                    
                    { "Daylight (3 pts)", 
                      "Daylight in 75% of spaces. Abundant equatorial light in Africa." },
                    
                    { "Quality Views (1 pt)", 
                      "Views for 75% of spaces. Often achievable at low cost." }
                };
            }

            /// <summary>
            /// Challenges for LEED in Africa
            /// </summary>
            public static string[] GetLEEDChallengesAfrica()
            {
                return new[]
                {
                    "Higher certification costs than EDGE",
                    "Requirement for LEED AP (accredited professional)",
                    "US-centric standards may not suit local context",
                    "Limited local expertise and materials database",
                    "Complex documentation requirements",
                    "Energy modeling software learning curve",
                    "Commissioning requirements can be expensive",
                    "Limited green product availability in some markets"
                };
            }

            /// <summary>
            /// LEED certification process
            /// </summary>
            public static string[] GetCertificationProcess()
            {
                return new[]
                {
                    "1. Register project with USGBC/GBCI",
                    "2. Assemble LEED project team (include LEED AP)",
                    "3. Conduct integrative process workshop",
                    "4. Design to target credit achievement",
                    "5. Submit design review (optional)",
                    "6. Construct per LEED requirements",
                    "7. Document all credits and prerequisites",
                    "8. Upload documentation to LEED Online",
                    "9. GBCI review (2-3 review cycles typical)",
                    "10. Address review comments",
                    "11. Receive final certification"
                };
            }
        }

        #endregion

        #region Green Star Africa

        /// <summary>
        /// Green Star Africa - Adapted from Green Star Australia
        /// Developed by Green Building Council South Africa (GBCSA)
        /// </summary>
        public static class GreenStarAfrica
        {
            /// <summary>
            /// Green Star certification levels
            /// </summary>
            public enum GreenStarLevel
            {
                /// <summary>4 Stars - Best Practice</summary>
                FourStar_BestPractice,
                /// <summary>5 Stars - African Excellence</summary>
                FiveStar_Excellence,
                /// <summary>6 Stars - World Leadership</summary>
                SixStar_WorldLeadership
            }

            /// <summary>
            /// Gets points required for certification
            /// </summary>
            public static (int Minimum, string Description) GetPointsRequired(GreenStarLevel level)
            {
                return level switch
                {
                    GreenStarLevel.FourStar_BestPractice => (45, "Best Practice - 45-59 points"),
                    GreenStarLevel.FiveStar_Excellence => (60, "African Excellence - 60-74 points"),
                    GreenStarLevel.SixStar_WorldLeadership => (75, "World Leadership - 75+ points"),
                    _ => (45, "Best Practice")
                };
            }

            /// <summary>
            /// Green Star categories
            /// </summary>
            public static class GreenStarCategories
            {
                public static readonly Dictionary<string, int> MaximumPoints = new()
                {
                    { "Management", 15 },
                    { "Indoor Environment Quality", 28 },
                    { "Energy", 30 },
                    { "Transport", 11 },
                    { "Water", 14 },
                    { "Materials", 19 },
                    { "Land Use & Ecology", 8 },
                    { "Emissions", 10 },
                    { "Innovation", 10 }
                };
            }

            /// <summary>
            /// Africa-specific Green Star credits
            /// </summary>
            public static string[] GetAfricaSpecificCredits()
            {
                return new[]
                {
                    "Socio-Economic Empowerment - Local job creation",
                    "Embodied Carbon - Reduced carbon in materials",
                    "Water Efficiency - Critical for water-scarce regions",
                    "Indigenous Vegetation - Protect biodiversity",
                    "Stormwater Management - Reduce urban flooding",
                    "Thermal Comfort - Passive design for hot climates",
                    "Public Transport Access - Reduce vehicle emissions",
                    "Construction Waste - Divert from landfills"
                };
            }

            /// <summary>
            /// Green Star active markets in Africa
            /// </summary>
            public static readonly string[] ActiveMarkets = new[]
            {
                "South Africa - Mature market, 500+ certified buildings",
                "Kenya - Growing adoption",
                "Nigeria - Emerging market",
                "Ghana - Pilot projects"
            };

            /// <summary>
            /// Green Star vs EDGE comparison
            /// </summary>
            public static string[] GetComparisonWithEDGE()
            {
                return new[]
                {
                    "Green Star: More comprehensive (9 categories vs 3)",
                    "Green Star: Higher certification cost",
                    "Green Star: More suited to commercial/high-end projects",
                    "EDGE: Simpler, more affordable",
                    "EDGE: Better for residential and emerging markets",
                    "EDGE: Specific focus on energy, water, materials efficiency",
                    "Both: Internationally recognized",
                    "Both: Provide market differentiation"
                };
            }
        }

        #endregion

        #region Green Building Strategies for Africa

        /// <summary>
        /// Climate-appropriate green building strategies for Africa
        /// </summary>
        public static class AfricaGreenStrategies
        {
            /// <summary>
            /// Passive design strategies for hot-humid climates (coastal)
            /// </summary>
            public static string[] GetHotHumidStrategies()
            {
                return new[]
                {
                    "Building orientation: Long axis east-west",
                    "Cross ventilation: Openings on opposite walls",
                    "High ceilings: Minimum 3m for air circulation",
                    "Covered verandahs: 1-2m overhang on sun-facing sides",
                    "Light-colored roofs: Reflect solar radiation",
                    "Vegetation: Trees for shading and cooling",
                    "Minimal west-facing windows: Reduce afternoon heat gain",
                    "Ceiling fans: Improve air movement and comfort",
                    "Raised buildings: Allow air circulation underneath",
                    "Lightweight construction: Reduce heat storage"
                };
            }

            /// <summary>
            /// Passive design strategies for hot-dry climates (Sahel)
            /// </summary>
            public static string[] GetHotDryStrategies()
            {
                return new[]
                {
                    "High thermal mass: Thick walls absorb day heat, release at night",
                    "Compact form: Minimize surface area exposed to sun",
                    "Small windows: Reduce heat gain",
                    "Internal courtyards: Create microclimates",
                    "Light-colored exteriors: Reflect solar radiation",
                    "Shading devices: Deep overhangs, screens",
                    "Evaporative cooling: Water features, vegetation",
                    "Night ventilation: Purge heat accumulated during day",
                    "Earth-contact buildings: Benefit from stable ground temperature"
                };
            }

            /// <summary>
            /// Water conservation strategies for Africa
            /// </summary>
            public static string[] GetWaterConservationStrategies()
            {
                return new[]
                {
                    "Rainwater harvesting: Mandatory in many African cities",
                    "Greywater recycling: Reuse for irrigation, toilets",
                    "Low-flow fixtures: Reduce consumption by 30-50%",
                    "Drip irrigation: 40% more efficient than sprinklers",
                    "Native landscaping: Eliminate irrigation needs",
                    "Leak detection: Automatic shut-off systems",
                    "Water-efficient appliances: Front-load washers, etc.",
                    "Education: Occupant awareness programs"
                };
            }

            /// <summary>
            /// Renewable energy strategies for Africa
            /// </summary>
            public static string[] GetRenewableEnergyStrategies()
            {
                return new[]
                {
                    "Solar PV: Excellent resource across most of Africa",
                    "Solar water heating: Simple, cost-effective",
                    "Mini-grids: For off-grid/unreliable grid locations",
                    "Battery storage: Tesla Powerwall, local alternatives",
                    "Net metering: Where available (Kenya, South Africa)",
                    "Biogas: From organic waste (rural applications)",
                    "Hybrid systems: Solar + diesel for reliability",
                    "Energy-efficient design: Reduce demand first"
                };
            }

            /// <summary>
            /// Local materials for green building in Africa
            /// </summary>
            public static string[] GetLocalGreenMaterials()
            {
                return new[]
                {
                    "Compressed earth blocks (CEB): Low embodied energy",
                    "Rammed earth: Thermal mass, locally sourced",
                    "Bamboo: Fast-growing, renewable",
                    "Thatch roofing: Traditional, biodegradable",
                    "Stabilized soil blocks: Improved earth blocks",
                    "Recycled steel: From local scrap",
                    "Fly ash concrete: Industrial waste utilization",
                    "Local stone: Reduces transportation",
                    "Clay tiles: Traditional, durable"
                };
            }
        }

        #endregion

        #region Green Building Financing in Africa

        /// <summary>
        /// Green financing opportunities for certified buildings
        /// </summary>
        public static class GreenFinancing
        {
            /// <summary>
            /// Green bond and loan providers in Africa
            /// </summary>
            public static readonly string[] GreenFinanciers = new[]
            {
                "IFC - International Finance Corporation",
                "African Development Bank (AfDB)",
                "Standard Bank - Green building loans (South Africa)",
                "Stanbic Bank - EDGE financing (East Africa)",
                "KCB Bank - Green loans (Kenya)",
                "Standard Chartered - Sustainable finance",
                "FMO - Dutch development bank",
                "Proparco - French development finance"
            };

            /// <summary>
            /// Typical green financing incentives
            /// </summary>
            public static string[] GetFinancingIncentives()
            {
                return new[]
                {
                    "Interest rate reduction: 0.5-2% below standard rates",
                    "Extended loan tenure: Up to 20 years",
                    "Higher loan-to-value: Up to 90% vs 80% standard",
                    "Grace periods: Longer repayment start periods",
                    "Tax incentives: VAT exemptions on green materials (some countries)",
                    "Expedited approvals: Fast-track processing",
                    "Technical assistance: Free EDGE/LEED consulting"
                };
            }

            /// <summary>
            /// Requirements for green financing
            /// </summary>
            public static string[] GetFinancingRequirements()
            {
                return new[]
                {
                    "Preliminary EDGE/LEED certification",
                    "Energy and water savings projections",
                    "Commitment to final certification",
                    "Qualified design team (LEED AP or EDGE Expert)",
                    "Monitoring and verification plan",
                    "Reporting requirements during construction"
                };
            }
        }

        #endregion

        #region Certification Comparison and Selection

        /// <summary>
        /// Helps select appropriate green building certification
        /// </summary>
        public static class CertificationSelection
        {
            /// <summary>
            /// Comparison matrix for African context
            /// </summary>
            public static Dictionary<string, (string EDGE, string LEED, string GreenStar)> 
                GetComparisonMatrix()
            {
                return new Dictionary<string, (string, string, string)>
                {
                    { "Cost", ("Low ($2-5/m²)", "High ($10-20/m²)", "High ($10-15/m²)") },
                    { "Complexity", ("Simple", "Complex", "Complex") },
                    { "Time", ("3-6 months", "12-18 months", "12-18 months") },
                    { "Best for", ("Residential, emerging markets", "Commercial, corporate", "Commercial, South Africa") },
                    { "Market recognition", ("Africa, Asia", "Global", "Africa, Australia") },
                    { "Local expertise", ("Growing", "Limited", "SA only") },
                    { "Focus", ("Energy, water, materials", "Comprehensive", "Comprehensive") },
                    { "Financing access", ("Excellent", "Good", "Good") }
                };
            }

            /// <summary>
            /// Recommends certification based on project
            /// </summary>
            public static string RecommendCertification(
                string buildingType,
                string country,
                double budgetPerM2,
                string projectGoal)
            {
                // EDGE for residential and emerging markets
                if (buildingType.ToLower().Contains("residential") ||
                    budgetPerM2 < 1000 ||
                    projectGoal.ToLower().Contains("affordable"))
                {
                    return "EDGE Certified - Best fit for residential, cost-effective, strong financing support";
                }

                // Green Star for South Africa
                if (country.Equals("South Africa", StringComparison.OrdinalIgnoreCase) &&
                    buildingType.ToLower().Contains("commercial"))
                {
                    return "Green Star Africa - Established in SA market, comprehensive standard";
                }

                // LEED for high-end commercial / corporate
                if (buildingType.ToLower().Contains("office") ||
                    projectGoal.ToLower().Contains("corporate") ||
                    projectGoal.ToLower().Contains("global recognition"))
                {
                    return "LEED - Global recognition, corporate preference, comprehensive";
                }

                // Default to EDGE for most African projects
                return "EDGE Certified - Most practical for African context";
            }
        }

        #endregion
    }
}
