using System;
using System.Collections.Generic;

namespace StingBIM.Standards.ECOWAS
{
    /// <summary>
    /// ECOWAS - Economic Community of West African States
    /// Regional standards for West Africa
    /// 
    /// Member states (15): Benin, Burkina Faso, Cape Verde, Côte d'Ivoire, Gambia,
    /// Ghana, Guinea, Guinea-Bissau, Liberia, Mali, Niger, Nigeria, Senegal,
    /// Sierra Leone, Togo
    /// 
    /// ECOWAS promotes:
    /// - Harmonized standards across West Africa
    /// - Free movement of goods and services
    /// - Regional trade facilitation
    /// - Quality infrastructure development
    /// 
    /// Critical for Liberia as ECOWAS member state
    /// </summary>
    public static class ECOWASStandards
    {
        #region ECOWAS Member States

        /// <summary>
        /// ECOWAS member states
        /// </summary>
        public static readonly string[] MemberStates = new[]
        {
            "Benin",
            "Burkina Faso",
            "Cape Verde",
            "Côte d'Ivoire",
            "Gambia",
            "Ghana",
            "Guinea",
            "Guinea-Bissau",
            "Liberia",
            "Mali",
            "Niger",
            "Nigeria",
            "Senegal",
            "Sierra Leone",
            "Togo"
        };

        /// <summary>
        /// Checks if country is ECOWAS member
        /// </summary>
        public static bool IsECOWASMember(string country)
        {
            return Array.Exists(MemberStates, 
                state => state.Equals(country, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region ECOWAS Building Materials Standards

        /// <summary>
        /// ECOWAS cement specifications
        /// Harmonized across West Africa
        /// </summary>
        public static class CementStandards
        {
            /// <summary>
            /// Cement types recognized in ECOWAS
            /// </summary>
            public enum ECOWASCementType
            {
                /// <summary>Type I - General purpose (CEM I)</summary>
                TypeI_GeneralPurpose,
                /// <summary>Type II - Moderate sulfate resistance (CEM II)</summary>
                TypeII_Blended,
                /// <summary>Type III - High early strength</summary>
                TypeIII_HighEarly,
                /// <summary>Type IV - Low heat (CEM IV)</summary>
                TypeIV_LowHeat,
                /// <summary>Type V - Sulfate resistant (CEM V)</summary>
                TypeV_SulfateResistant
            }

            /// <summary>
            /// Gets minimum compressive strength (MPa) at 28 days
            /// </summary>
            public static double GetMinimumStrength(ECOWASCementType type)
            {
                return type switch
                {
                    ECOWASCementType.TypeI_GeneralPurpose => 42.5,
                    ECOWASCementType.TypeII_Blended => 32.5,
                    ECOWASCementType.TypeIII_HighEarly => 52.5,
                    ECOWASCementType.TypeIV_LowHeat => 32.5,
                    ECOWASCementType.TypeV_SulfateResistant => 42.5,
                    _ => 32.5
                };
            }

            /// <summary>
            /// Major cement producers in ECOWAS region
            /// </summary>
            public static readonly string[] MajorProducers = new[]
            {
                "Dangote Cement (Nigeria, Ghana, Senegal, others)",
                "LafargeHolcim (Nigeria, Ghana, Liberia)",
                "BUA Cement (Nigeria)",
                "CIMAF (Cameroon, Côte d'Ivoire, Guinea, others)",
                "Diamond Cement (Liberia)",
                "GHACEM (Ghana)",
                "WACEM (Togo, Benin)"
            };
        }

        /// <summary>
        /// ECOWAS steel reinforcement standards
        /// </summary>
        public static class SteelStandards
        {
            /// <summary>
            /// Standard steel grades in ECOWAS region
            /// </summary>
            public static readonly int[] StandardGrades = new[] { 250, 410, 460, 500 };

            /// <summary>
            /// Gets whether cross-border steel trade is permitted
            /// </summary>
            public static bool IsIntraECOWASTrade(string originCountry, string destCountry)
            {
                return IsECOWASMember(originCountry) && IsECOWASMember(destCountry);
            }

            /// <summary>
            /// Required documentation for intra-ECOWAS steel trade
            /// </summary>
            public static string[] GetIntraECOWASTradeRequirements()
            {
                return new[]
                {
                    "ECOWAS Trade Liberalization Scheme (ETLS) certificate",
                    "Certificate of origin",
                    "Mill test certificate",
                    "Quality compliance certificate",
                    "Commercial invoice",
                    "No customs duty for intra-ECOWAS trade"
                };
            }
        }

        #endregion

        #region ECOWAS Building Code Harmonization

        /// <summary>
        /// ECOWAS building code framework
        /// Being developed for regional harmonization
        /// </summary>
        public static class BuildingCodeFramework
        {
            /// <summary>
            /// Minimum structural safety requirements
            /// </summary>
            public static class StructuralSafety
            {
                /// <summary>Minimum concrete grade for structures</summary>
                public const string MinimumConcreteGrade = "C20/25";

                /// <summary>Minimum steel grade</summary>
                public const int MinimumSteelGrade = 410; // MPa

                /// <summary>Minimum safety factor for dead load</summary>
                public const double SafetyFactorDeadLoad = 1.4;

                /// <summary>Minimum safety factor for live load</summary>
                public const double SafetyFactorLiveLoad = 1.6;

                /// <summary>
                /// Gets minimum live loads for different occupancies (kN/m²)
                /// </summary>
                public static double GetMinimumLiveLoad(string occupancy)
                {
                    return occupancy.ToLower() switch
                    {
                        "residential" => 2.0,
                        "office" => 2.5,
                        "retail" => 4.0,
                        "storage" => 5.0,
                        "industrial" => 6.0,
                        "assembly" => 5.0,
                        _ => 2.5
                    };
                }
            }

            /// <summary>
            /// Fire safety minimum requirements
            /// </summary>
            public static class FireSafety
            {
                /// <summary>
                /// Gets minimum fire resistance rating (minutes)
                /// </summary>
                public static int GetMinimumFireResistance(int floors)
                {
                    if (floors <= 2) return 60;
                    else if (floors <= 5) return 90;
                    else if (floors <= 10) return 120;
                    else return 180;
                }

                /// <summary>
                /// Required fire safety equipment
                /// </summary>
                public static string[] GetRequiredFireEquipment(string buildingType)
                {
                    var equipment = new List<string>
                    {
                        "Fire extinguishers (ABC type)",
                        "Fire assembly point",
                        "Emergency exit signs",
                        "Fire alarm system"
                    };

                    if (buildingType.ToLower().Contains("public") ||
                        buildingType.ToLower().Contains("commercial"))
                    {
                        equipment.AddRange(new[]
                        {
                            "Emergency lighting",
                            "Fire hose reels",
                            "Sprinkler system (for large buildings)",
                            "Fire safety training for staff"
                        });
                    }

                    return equipment.ToArray();
                }
            }
        }

        #endregion

        #region ECOWAS Climate Zones and Design

        /// <summary>
        /// West African climate zones
        /// </summary>
        public enum WestAfricanClimateZone
        {
            /// <summary>Sahel - Hot and dry (northern region)</summary>
            Sahel_HotDry,
            /// <summary>Sudan Savanna - Hot with seasonal rain</summary>
            SudanSavanna,
            /// <summary>Guinea Savanna - Moderate tropical</summary>
            GuineaSavanna,
            /// <summary>Rainforest - Hot and humid (coastal)</summary>
            Rainforest_Coastal,
            /// <summary>Coastal - Hot and very humid</summary>
            Coastal_Marine
        }

        /// <summary>
        /// Gets climate zone for ECOWAS country/city
        /// </summary>
        public static WestAfricanClimateZone GetClimateZone(string location)
        {
            string loc = location.ToLower();

            // Coastal areas
            if (loc.Contains("monrovia") || loc.Contains("accra") || loc.Contains("lagos") ||
                loc.Contains("freetown") || loc.Contains("conakry") || loc.Contains("abidjan"))
            {
                return WestAfricanClimateZone.Rainforest_Coastal;
            }
            // Sahel region
            else if (loc.Contains("niger") || loc.Contains("mali") || loc.Contains("burkina"))
            {
                return WestAfricanClimateZone.Sahel_HotDry;
            }
            // Savanna
            else if (loc.Contains("nigeria") && !loc.Contains("lagos"))
            {
                return WestAfricanClimateZone.SudanSavanna;
            }
            else
            {
                return WestAfricanClimateZone.GuineaSavanna;
            }
        }

        /// <summary>
        /// Gets climate-specific design recommendations
        /// </summary>
        public static string[] GetClimateDesignRecommendations(WestAfricanClimateZone zone)
        {
            return zone switch
            {
                WestAfricanClimateZone.Sahel_HotDry => new[]
                {
                    "High thermal mass for temperature moderation",
                    "Minimal window area to reduce heat gain",
                    "Light-colored roof and walls for heat reflection",
                    "Shade structures and courtyards",
                    "Dust protection for mechanical systems",
                    "Solar panel efficiency excellent"
                },
                
                WestAfricanClimateZone.Rainforest_Coastal => new[]
                {
                    "Elevated buildings for flood protection",
                    "Wide roof overhangs (minimum 900mm)",
                    "Natural ventilation critical (high humidity)",
                    "Corrosion protection essential (coastal)",
                    "Mold-resistant materials required",
                    "Steep roof pitch (minimum 25°) for heavy rainfall",
                    "Termite protection mandatory",
                    "Waterproofing critical for all elements"
                },
                
                WestAfricanClimateZone.Coastal_Marine => new[]
                {
                    "Salt spray corrosion protection (ASTM B117 testing)",
                    "Stainless steel or hot-dip galvanized metals",
                    "Marine-grade materials for exposed elements",
                    "Regular maintenance schedule required",
                    "Elevated electrical equipment",
                    "Concrete with corrosion inhibitors"
                },
                
                _ => new[]
                {
                    "Natural ventilation design",
                    "Roof overhangs for sun and rain protection",
                    "Termite protection",
                    "Damp-proofing for ground contact",
                    "Rain screen for external walls"
                }
            };
        }

        #endregion

        #region ECOWAS Electrical Standards

        /// <summary>
        /// Electrical supply characteristics in ECOWAS countries
        /// </summary>
        public static (double Voltage, double Frequency) GetElectricalSupply(string country)
        {
            // Most ECOWAS countries use 230V, 50Hz
            // Liberia uses 120V, 60Hz (American system)
            
            if (country.Equals("Liberia", StringComparison.OrdinalIgnoreCase))
                return (120, 60);
            else
                return (230, 50);
        }

        /// <summary>
        /// Power reliability in ECOWAS region
        /// </summary>
        public static class PowerReliability
        {
            /// <summary>
            /// Gets power reliability rating
            /// </summary>
            public static string GetReliabilityRating(string country)
            {
                return country.ToLower() switch
                {
                    "ghana" => "Moderate - improving with gas power",
                    "nigeria" => "Poor to moderate - frequent outages",
                    "senegal" => "Moderate - better than regional average",
                    "liberia" => "Poor - very limited grid coverage",
                    "sierra leone" => "Poor - limited grid access",
                    _ => "Poor to moderate - backup power essential"
                };
            }

            /// <summary>
            /// Backup power recommendations for West Africa
            /// </summary>
            public static string[] GetBackupPowerRecommendations()
            {
                return new[]
                {
                    "Generator backup essential for all facilities",
                    "UPS for sensitive equipment",
                    "Solar + battery storage increasingly viable",
                    "Automatic transfer switch for critical loads",
                    "Fuel storage for generators (3-7 days minimum)",
                    "Voltage stabilizers to protect equipment",
                    "Surge protection devices mandatory"
                };
            }
        }

        #endregion

        #region ECOWAS Water and Sanitation

        /// <summary>
        /// Water supply in ECOWAS region
        /// </summary>
        public static class WaterSupply
        {
            /// <summary>
            /// Gets water supply reliability
            /// </summary>
            public static string GetWaterReliability(string country)
            {
                return country.ToLower() switch
                {
                    "ghana" => "Moderate - urban areas better served",
                    "nigeria" => "Poor to moderate - varies by city",
                    "liberia" => "Poor - very limited piped water",
                    "senegal" => "Moderate in cities, poor in rural",
                    _ => "Poor - alternative sources often needed"
                };
            }

            /// <summary>
            /// Alternative water sources common in West Africa
            /// </summary>
            public static string[] GetAlternativeWaterSources()
            {
                return new[]
                {
                    "Borehole wells (most common)",
                    "Hand-dug wells (shallow aquifers)",
                    "Rainwater harvesting (essential)",
                    "Water tanker delivery",
                    "Community water points",
                    "River/stream (requires treatment)",
                    "Packaged water for drinking"
                };
            }

            /// <summary>
            /// Gets recommended water storage capacity (liters)
            /// </summary>
            public static double GetRecommendedStorage(
                int occupants,
                bool hasPipedWater,
                WestAfricanClimateZone climate)
            {
                // Base: 50 liters per person per day
                double dailyDemand = occupants * 50;

                // Storage duration depends on reliability
                int days = hasPipedWater ? 3 : 7;

                // Increase for dry climate zones
                if (climate == WestAfricanClimateZone.Sahel_HotDry)
                    days += 3;

                return dailyDemand * days;
            }

            /// <summary>
            /// Water quality treatment requirements
            /// </summary>
            public static string[] GetWaterTreatmentRequirements()
            {
                return new[]
                {
                    "Filtration system for borehole water",
                    "Chlorination for stored water",
                    "UV treatment for drinking water (optional)",
                    "Regular water quality testing",
                    "Tank cleaning every 6 months",
                    "First-flush diverter for rainwater",
                    "Separate storage for non-potable uses"
                };
            }
        }

        #endregion

        #region ECOWAS Construction Materials Trade

        /// <summary>
        /// ECOWAS Trade Liberalization Scheme (ETLS)
        /// Promotes free movement of goods within ECOWAS
        /// </summary>
        public static class TradeLiberalization
        {
            /// <summary>
            /// Products eligible for duty-free trade
            /// </summary>
            public static readonly string[] DutyFreeProducts = new[]
            {
                "Cement (ECOWAS origin)",
                "Steel reinforcement (ECOWAS origin)",
                "Concrete blocks and bricks",
                "Roofing materials",
                "Electrical cables and wires",
                "Plumbing fittings",
                "Doors and windows",
                "Paints and coatings"
            };

            /// <summary>
            /// Required documentation for ETLS
            /// </summary>
            public static string[] GetETLSRequirements()
            {
                return new[]
                {
                    "ECOWAS Certificate of Origin (Form A)",
                    "Commercial invoice",
                    "Packing list",
                    "Certificate of conformity to standards",
                    "Transport documents",
                    "Value Addition Certificate (40% local content)"
                };
            }

            /// <summary>
            /// Gets whether product qualifies for ETLS
            /// </summary>
            public static bool QualifiesForETLS(
                string productOrigin,
                double localContentPercent)
            {
                return IsECOWASMember(productOrigin) && localContentPercent >= 40.0;
            }
        }

        #endregion

        #region Liberia-Specific ECOWAS Integration

        /// <summary>
        /// Liberia as ECOWAS member - specific considerations
        /// </summary>
        public static class LiberiaECOWASIntegration
        {
            /// <summary>
            /// Benefits for Liberia from ECOWAS membership
            /// </summary>
            public static string[] GetECOWASBenefitsForLiberia()
            {
                return new[]
                {
                    "Duty-free import of construction materials from ECOWAS",
                    "Access to regional cement supply (Dangote, LafargeHolcim)",
                    "Free movement of skilled workers",
                    "Harmonized standards reduce technical barriers",
                    "Regional infrastructure development programs",
                    "Preferential treatment for Liberian contractors in ECOWAS region"
                };
            }

            /// <summary>
            /// Challenges for Liberia in ECOWAS integration
            /// </summary>
            public static string[] GetIntegrationChallenges()
            {
                return new[]
                {
                    "Electrical system incompatibility (120V vs 230V)",
                    "Use of imperial units vs metric in most ECOWAS",
                    "Limited local manufacturing capacity",
                    "Infrastructure gaps at borders",
                    "Need for standards harmonization"
                };
            }

            /// <summary>
            /// Key ECOWAS construction material suppliers for Liberia
            /// </summary>
            public static readonly string[] KeySuppliers = new[]
            {
                "Nigeria - Cement (Dangote), Steel, Electrical equipment",
                "Ghana - Roofing materials, Tiles, Hardware",
                "Côte d'Ivoire - Cement, Paint, Fittings",
                "Senegal - Building materials, Fixtures",
                "Guinea - Aggregates, Stone, Sand"
            };
        }

        #endregion

        #region ECOWAS Quality Infrastructure

        /// <summary>
        /// ECOWAS quality infrastructure development
        /// </summary>
        public static class QualityInfrastructure
        {
            /// <summary>
            /// Regional quality institutions
            /// </summary>
            public static readonly string[] RegionalInstitutions = new[]
            {
                "WAMCO - West African Metrology Cooperation",
                "ECOWAS Regional Quality Infrastructure Programme",
                "ARSO - African Organisation for Standardisation (observer)"
            };

            /// <summary>
            /// Priority areas for standards harmonization
            /// </summary>
            public static readonly string[] HarmonizationPriorities = new[]
            {
                "Cement and concrete standards",
                "Steel reinforcement specifications",
                "Electrical safety standards",
                "Building code framework",
                "Fire safety requirements",
                "Environmental standards",
                "Energy efficiency standards"
            };

            /// <summary>
            /// Testing and certification requirements
            /// </summary>
            public static string[] GetTestingRequirements(string productType)
            {
                return new[]
                {
                    $"Testing by accredited laboratory (ECOWAS recognized)",
                    $"Conformity to ECOWAS standard (where applicable)",
                    $"Certification mark from national standards body",
                    $"Batch testing for critical materials",
                    $"Traceability documentation"
                };
            }
        }

        #endregion
    }
}
