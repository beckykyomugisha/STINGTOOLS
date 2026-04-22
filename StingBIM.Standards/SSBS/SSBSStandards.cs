using System;
using System.Collections.Generic;

namespace StingBIM.Standards.SSBS
{
    /// <summary>
    /// SSBS - South Sudan Bureau of Standards
    /// National standards body for South Sudan (Republic of South Sudan)
    /// 
    /// South Sudan is the world's newest country (independence: July 9, 2011)
    /// 
    /// Key characteristics:
    /// - Post-conflict infrastructure rebuilding
    /// - Heavy reliance on EAS (East African Standards)
    /// - Adopts Ugandan standards due to historical ties
    /// - Oil-rich but infrastructure-poor
    /// - Nile River considerations for major cities
    /// - Fragile state with ongoing security challenges
    /// </summary>
    public static class SSBSStandards
    {
        #region Country Overview

        /// <summary>
        /// South Sudan independence date
        /// </summary>
        public static readonly DateTime IndependenceDate = new DateTime(2011, 7, 9);

        /// <summary>
        /// Major cities in South Sudan
        /// </summary>
        public static readonly string[] MajorCities = new[]
        {
            "Juba - Capital (Central Equatoria State)",
            "Malakal - Oil city (Upper Nile State)",
            "Wau - Western hub (Western Bahr el Ghazal)",
            "Yei - Agricultural center (Central Equatoria)",
            "Bor - Jonglei State capital",
            "Bentiu - Oil region (Unity State)"
        };

        /// <summary>
        /// Administrative states of South Sudan
        /// </summary>
        public enum SouthSudanState
        {
            /// <summary>Central Equatoria - Capital Juba</summary>
            CentralEquatoria,
            /// <summary>Eastern Equatoria</summary>
            EasternEquatoria,
            /// <summary>Western Equatoria</summary>
            WesternEquatoria,
            /// <summary>Upper Nile - Oil production</summary>
            UpperNile,
            /// <summary>Unity - Oil production</summary>
            Unity,
            /// <summary>Jonglei - Largest state</summary>
            Jonglei,
            /// <summary>Lakes</summary>
            Lakes,
            /// <summary>Warrap</summary>
            Warrap,
            /// <summary>Northern Bahr el Ghazal</summary>
            NorthernBahrElGhazal,
            /// <summary>Western Bahr el Ghazal</summary>
            WesternBahrElGhazal
        }

        #endregion

        #region Standards Framework

        /// <summary>
        /// South Sudan adopts EAS and Ugandan standards
        /// SSBS is in early development stage
        /// </summary>
        public static class StandardsAdoption
        {
            /// <summary>Primary standards sources for South Sudan</summary>
            public static readonly string[] PrimaryStandardsSources = new[]
            {
                "EAS - East African Standards (as EAC member)",
                "UNBS - Uganda National Bureau of Standards",
                "Kenyan standards (KEBS) for specific sectors",
                "International standards (ISO, IEC) where available",
                "British Standards (BS) - colonial legacy"
            };

            /// <summary>
            /// Gets applicable standard for construction activity
            /// </summary>
            public static string GetApplicableStandard(string activity)
            {
                return activity.ToLower() switch
                {
                    "cement" => "EAS 18-1 (Portland cement) or UNBS US EAS 18-1",
                    "steel reinforcement" => "EAS 2 (Steel reinforcement) or UNBS US EAS 2",
                    "concrete blocks" => "EAS 149 (Concrete blocks) or local production",
                    "electrical" => "UNBS standards + BS 7671",
                    "plumbing" => "UNBS standards + local practice",
                    "structural design" => "Eurocodes or BS codes",
                    _ => "Consult SSBS or use Ugandan equivalent"
                };
            }
        }

        #endregion

        #region Building Regulations

        /// <summary>
        /// Building permit requirements in South Sudan
        /// Process is still developing in post-conflict environment
        /// </summary>
        public static class BuildingPermits
        {
            /// <summary>
            /// Gets building permit requirements for location
            /// </summary>
            public static string[] GetPermitRequirements(SouthSudanState state)
            {
                var baseRequirements = new List<string>
                {
                    "Architectural drawings (stamped by engineer/architect)",
                    "Structural calculations (for multi-story)",
                    "Land ownership documentation or lease agreement",
                    "State Ministry of Physical Infrastructure approval",
                    "Environmental clearance (for large projects)",
                    "Security clearance (some areas)"
                };

                if (state == SouthSudanState.CentralEquatoria)
                {
                    // Juba has more developed processes
                    baseRequirements.AddRange(new[]
                    {
                        "Juba City Council approval",
                        "Urban planning compliance",
                        "Utility connection agreements (where available)",
                        "Site inspection fees payment"
                    });
                }

                return baseRequirements.ToArray();
            }

            /// <summary>
            /// Permit processing challenges in South Sudan
            /// </summary>
            public static string[] GetPermitChallenges()
            {
                return new[]
                {
                    "Limited government capacity (new country)",
                    "Bureaucratic delays common",
                    "Unclear or evolving regulations",
                    "Corruption and informal payments may be requested",
                    "Security situation affects processing times",
                    "Limited technical review capacity",
                    "Recommendations: Engage experienced local consultant"
                };
            }
        }

        /// <summary>
        /// Minimum construction standards for South Sudan
        /// Based on EAS + Ugandan + practical considerations
        /// </summary>
        public static class MinimumStandards
        {
            /// <summary>Minimum concrete grade</summary>
            public const string MinimumConcreteGrade = "C20";

            /// <summary>Minimum steel grade</summary>
            public const string MinimumSteelGrade = "Grade 460 (per EAS 2)";

            /// <summary>Minimum roof slope for rainfall (degrees)</summary>
            public const double MinimumRoofSlope = 15.0;

            /// <summary>Minimum ceiling height (m)</summary>
            public const double MinimumCeilingHeight = 2.5;

            /// <summary>
            /// Required building setbacks (m)
            /// </summary>
            public static (double Front, double Side, double Rear) GetRequiredSetbacks(bool urbanArea)
            {
                return urbanArea ? (5.0, 3.0, 3.0) : (3.0, 2.0, 2.0);
            }
        }

        #endregion

        #region Infrastructure Challenges

        /// <summary>
        /// Electricity supply in South Sudan
        /// One of the least electrified countries in the world
        /// </summary>
        public static class ElectricitySupply
        {
            /// <summary>National grid electrification rate</summary>
            public const double ElectrificationRate = 7.0; // ~7% of population

            /// <summary>Primary electricity source</summary>
            public const string PrimarySource = "Diesel generators (most common)";

            /// <summary>Voltage and frequency (where grid exists)</summary>
            public static readonly (double Voltage, double Frequency) PowerSupply = (230, 50);

            /// <summary>
            /// Electricity access by location
            /// </summary>
            public static string GetElectricityAccess(string location)
            {
                string loc = location.ToLower();

                if (loc.Contains("juba"))
                    return "Limited grid (6-12 hrs/day), diesel generators essential";
                else if (loc.Contains("malakal") || loc.Contains("bentiu"))
                    return "Oil field power or diesel generators";
                else
                    return "No grid - 100% diesel generators or solar";
            }

            /// <summary>
            /// Power generation recommendations
            /// </summary>
            public static string[] GetPowerRecommendations()
            {
                return new[]
                {
                    "Diesel generator primary power (size for 100% load)",
                    "Fuel storage: Minimum 1 week supply (security considerations)",
                    "Solar PV + battery backup (increasingly viable)",
                    "Hybrid solar-diesel system (optimal for most projects)",
                    "Voltage stabilizers essential (poor power quality)",
                    "UPS for sensitive equipment mandatory",
                    "Plan for fuel supply chain disruptions",
                    "Consider security for fuel storage"
                };
            }

            /// <summary>
            /// Solar potential in South Sudan
            /// Excellent solar resource near equator
            /// </summary>
            public static string[] GetSolarPotential()
            {
                return new[]
                {
                    "Solar radiation: 5.0-6.0 kWh/m²/day (excellent)",
                    "Equatorial location: Consistent year-round",
                    "Dry season (Nov-Apr): Peak solar performance",
                    "Rainy season (May-Oct): Still good performance",
                    "Off-grid solar increasingly cost-effective vs diesel",
                    "Solar + battery can provide 24/7 power",
                    "No import duty on solar equipment (government incentive)"
                };
            }
        }

        /// <summary>
        /// Water supply in South Sudan
        /// Despite Nile River, water infrastructure is very limited
        /// </summary>
        public static class WaterSupply
        {
            /// <summary>Access to improved water</summary>
            public const double ImprovedWaterAccess = 65.0; // ~65% of population

            /// <summary>Piped water access</summary>
            public const double PipedWaterAccess = 8.0; // ~8% of population

            /// <summary>
            /// Gets water supply situation
            /// </summary>
            public static string GetWaterSituation(string location)
            {
                string loc = location.ToLower();

                if (loc.Contains("juba"))
                    return "Limited piped water (intermittent), most rely on boreholes/trucks";
                else if (loc.Contains("nile") || loc.Contains("river"))
                    return "River water available but requires treatment";
                else
                    return "Borehole wells primary source, rainwater harvesting critical";
            }

            /// <summary>
            /// Water source recommendations
            /// </summary>
            public static string[] GetWaterSourceRecommendations()
            {
                return new[]
                {
                    "Borehole well: Primary reliable source",
                    "Rainwater harvesting: Essential supplement (rainy season)",
                    "Water storage: Minimum 7-14 days supply",
                    "Water treatment: Filtration + chlorination mandatory",
                    "Backup tanker delivery: Plan for borehole failure",
                    "Water quality testing: Regular laboratory analysis",
                    "Nile River water: Requires comprehensive treatment plant"
                };
            }

            /// <summary>
            /// Gets recommended water storage capacity (liters)
            /// </summary>
            public static double GetRecommendedStorage(int occupants)
            {
                // 50 liters per person per day × 7-14 days minimum
                double dailyDemand = occupants * 50;
                int storageDays = 10; // Average 10 days

                return dailyDemand * storageDays;
            }

            /// <summary>
            /// Nile River considerations for Juba
            /// </summary>
            public static string[] GetNileRiverConsiderations()
            {
                return new[]
                {
                    "White Nile flows through Juba (major resource)",
                    "Seasonal flooding risk (May-November)",
                    "Water treatment plant required for river water",
                    "Elevated foundations near river (flood protection)",
                    "Intake design for variable water levels",
                    "Environmental permits for river abstraction",
                    "Crocodile risk consideration for intake structures"
                };
            }
        }

        #endregion

        #region Oil & Gas Sector

        /// <summary>
        /// Oil and gas sector construction standards
        /// South Sudan's primary revenue source
        /// </summary>
        public static class OilGasSector
        {
            /// <summary>Major oil-producing regions</summary>
            public static readonly string[] OilRegions = new[]
            {
                "Unity State (Bentiu oil fields)",
                "Upper Nile State (Paloch oil fields)",
                "Northern Liech State"
            };

            /// <summary>
            /// Oil facility construction requirements
            /// </summary>
            public static string[] GetOilFacilityRequirements()
            {
                return new[]
                {
                    "Petroleum ministry approval required",
                    "International standards: API, ASME, ISO",
                    "Environmental impact assessment mandatory",
                    "Security plan (conflict-affected regions)",
                    "Fire protection per NFPA 30 (flammable liquids)",
                    "Explosion-proof electrical installations",
                    "Spill containment and response equipment",
                    "Remote location logistics planning"
                };
            }

            /// <summary>
            /// Oil field camp construction
            /// </summary>
            public static string[] GetCampConstructionGuidelines()
            {
                return new[]
                {
                    "Prefabricated/modular construction common",
                    "Self-sufficient utilities (power, water, sewage)",
                    "Security perimeter and access control",
                    "Helicopter landing pad for medical evacuation",
                    "Communications infrastructure (satellite)",
                    "Medical clinic with emergency capabilities",
                    "Recreation facilities for worker morale",
                    "Cultural sensitivity (local communities)"
                };
            }
        }

        #endregion

        #region Climate and Environmental Considerations

        /// <summary>
        /// South Sudan climate characteristics
        /// Tropical climate with distinct wet and dry seasons
        /// </summary>
        public static class ClimateConsiderations
        {
            /// <summary>Annual rainfall (mm) - highly variable by region</summary>
            public static readonly (double Min, double Max) AnnualRainfall = (600, 1500);

            /// <summary>Temperature range (°C)</summary>
            public static readonly (double Min, double Max) TemperatureRange = (20, 40);

            /// <summary>Rainy season</summary>
            public const string RainySeason = "May to October";

            /// <summary>Dry season</summary>
            public const string DrySeason = "November to April";

            /// <summary>
            /// Gets climate zone for region
            /// </summary>
            public static string GetClimateZone(SouthSudanState state)
            {
                return state switch
                {
                    SouthSudanState.CentralEquatoria => "Tropical (higher rainfall)",
                    SouthSudanState.EasternEquatoria => "Tropical (moderate rainfall)",
                    SouthSudanState.WesternEquatoria => "Tropical (high rainfall)",
                    SouthSudanState.UpperNile => "Semi-arid (lower rainfall)",
                    SouthSudanState.Unity => "Semi-arid",
                    SouthSudanState.Jonglei => "Sudd wetlands (seasonal flooding)",
                    _ => "Savanna (moderate rainfall)"
                };
            }

            /// <summary>
            /// Climate-appropriate design strategies
            /// </summary>
            public static string[] GetDesignStrategies()
            {
                return new[]
                {
                    "Elevated buildings: Flood protection + termite prevention",
                    "Wide roof overhangs: 900mm minimum for rain protection",
                    "Natural ventilation: Cross-ventilation essential (no AC power)",
                    "High ceilings: Minimum 3m for air circulation",
                    "Light-colored roofs: Reduce heat absorption",
                    "Steep roof pitch: 20-25° for heavy rainfall",
                    "Rainwater harvesting: Mandatory (limited alternatives)",
                    "Termite protection: Chemical barriers + physical barriers",
                    "Damp-proofing: Critical during rainy season",
                    "Shading devices: Trees, verandahs for comfort"
                };
            }

            /// <summary>
            /// Seasonal construction planning
            /// </summary>
            public static string[] GetConstructionSeasonality()
            {
                return new[]
                {
                    "Dry season (Nov-Apr): Optimal for construction",
                    "Early dry season: Best for earthworks and foundations",
                    "Late dry season: Ideal for structural work",
                    "Rainy season (May-Oct): Difficult site access",
                    "Flooded areas: Impassable during rainy season",
                    "Material delivery: Plan for dry season transport",
                    "Labor availability: Better during dry season"
                };
            }
        }

        #endregion

        #region Security and Conflict Considerations

        /// <summary>
        /// Security considerations for construction in South Sudan
        /// Fragile state with ongoing conflicts
        /// </summary>
        public static class SecurityConsiderations
        {
            /// <summary>
            /// Security risk assessment
            /// </summary>
            public static string GetSecurityRisk(SouthSudanState state)
            {
                return state switch
                {
                    SouthSudanState.CentralEquatoria => "Moderate - Juba relatively stable but caution needed",
                    SouthSudanState.UpperNile => "High - Conflict affected",
                    SouthSudanState.Unity => "High - Conflict affected",
                    SouthSudanState.Jonglei => "High - Cattle raiding, ethnic conflicts",
                    _ => "Moderate to High - Check current situation"
                };
            }

            /// <summary>
            /// Security measures for construction sites
            /// </summary>
            public static string[] GetSecurityMeasures()
            {
                return new[]
                {
                    "Security assessment before project start",
                    "Armed security guards (common practice)",
                    "Perimeter fencing with controlled access",
                    "Coordination with local authorities",
                    "Community engagement and local hiring",
                    "Emergency evacuation plan",
                    "Insurance for conflict-related risks",
                    "Regular security briefings for staff",
                    "Secure accommodation for workers",
                    "Communication equipment (satellite phones)"
                };
            }

            /// <summary>
            /// Conflict-resilient design considerations
            /// </summary>
            public static string[] GetConflictResilientDesign()
            {
                return new[]
                {
                    "Robust construction (blast-resistant where needed)",
                    "Multiple exit routes from buildings",
                    "Safe rooms in high-risk facilities",
                    "Redundant utilities (power, water)",
                    "Fuel and food storage for lockdown scenarios",
                    "Communications room with backup power",
                    "First aid and medical supplies storage",
                    "Avoid high-profile designs in conflict zones"
                };
            }
        }

        #endregion

        #region Materials and Procurement

        /// <summary>
        /// Construction materials availability in South Sudan
        /// Most materials are imported
        /// </summary>
        public static class MaterialsProcurement
        {
            /// <summary>
            /// Primary material sources
            /// </summary>
            public static readonly string[] MaterialSources = new[]
            {
                "Uganda - Primary source (cement, steel, all materials)",
                "Kenya - Secondary source (via Uganda)",
                "Ethiopia - Some materials via road",
                "Sudan - Limited trade due to relations",
                "Local production - Sand, aggregates, bricks (limited)"
            };

            /// <summary>
            /// Import logistics challenges
            /// </summary>
            public static string[] GetImportChallenges()
            {
                return new[]
                {
                    "Long transport routes (2-5 days from Kampala/Mombasa)",
                    "Poor road conditions (esp. during rainy season)",
                    "Multiple checkpoints and delays",
                    "High transport costs (landlocked country)",
                    "Border delays (customs clearance)",
                    "Security risks during transport",
                    "Currency fluctuations (South Sudan Pound)",
                    "Payment challenges (USD preferred)"
                };
            }

            /// <summary>
            /// Recommended material sourcing strategy
            /// </summary>
            public static string[] GetSourcingStrategy()
            {
                return new[]
                {
                    "Order materials well in advance (3-6 months)",
                    "Source from Uganda (shortest, most reliable route)",
                    "Use trusted transporters with South Sudan experience",
                    "Consolidate shipments to reduce trips",
                    "Plan deliveries during dry season",
                    "Maintain buffer stock on site (avoid delays)",
                    "Consider air freight for critical items",
                    "Payment in USD (more stable than SSP)"
                };
            }

            /// <summary>
            /// Local materials that can be sourced
            /// </summary>
            public static string[] GetLocalMaterials()
            {
                return new[]
                {
                    "Sand - River sand (Nile), screening required",
                    "Aggregates - Crushed stone (limited quarries)",
                    "Bricks - Local production (quality varies)",
                    "Timber - Limited (mostly imported from Uganda)",
                    "Labor - Abundant but skill levels vary"
                };
            }
        }

        #endregion

        #region Professional Practice

        /// <summary>
        /// Professional engineering and architectural practice in South Sudan
        /// </summary>
        public static class ProfessionalPractice
        {
            /// <summary>
            /// Professional registration requirements
            /// </summary>
            public static string[] GetRegistrationRequirements()
            {
                return new[]
                {
                    "Engineers Board of South Sudan (if functional)",
                    "Architects registration (developing)",
                    "Foreign professionals: Work permit required",
                    "Partnership with local firm recommended",
                    "Professional indemnity insurance advised",
                    "Many use Ugandan/Kenyan registered professionals"
                };
            }

            /// <summary>
            /// Local capacity development
            /// </summary>
            public static string[] GetCapacityDevelopment()
            {
                return new[]
                {
                    "Very limited local technical capacity",
                    "University of Juba - Engineering program (developing)",
                    "TVET institutions (technical training) - limited",
                    "Most projects rely on expatriate professionals",
                    "Training and skills transfer highly valued",
                    "Apprenticeship programs beneficial",
                    "Consider hiring recent graduates for training"
                };
            }

            /// <summary>
            /// Construction supervision challenges
            /// </summary>
            public static string[] GetSupervisionChallenges()
            {
                return new[]
                {
                    "Limited qualified supervisors available",
                    "Quality control requires constant oversight",
                    "Material substitution risk (monitor closely)",
                    "Skilled labor shortage for specialized work",
                    "Communication barriers (English, Arabic, local languages)",
                    "Regular inspections essential",
                    "Clear specifications and work instructions needed"
                };
            }
        }

        #endregion
    }
}
