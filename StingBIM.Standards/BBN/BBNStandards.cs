using System;
using System.Collections.Generic;

namespace StingBIM.Standards.BBN
{
    /// <summary>
    /// BBN - Bureau Burundais de Normalisation (Burundi Bureau of Standardization)
    /// National standards body for Burundi
    /// 
    /// Burundi has adopted EAS (East African Standards) and implements
    /// French-influenced building practices due to colonial history.
    /// 
    /// Key characteristics:
    /// - Bilingual standards (French/Kirundi)
    /// - Heavy reliance on EAS standards
    /// - Developing national building codes
    /// - Limited local manufacturing, relies on imports
    /// </summary>
    public static class BBNStandards
    {
        #region BN EAS 2 - Cement (Burundi Implementation)

        /// <summary>
        /// Cement sources for Burundi
        /// Burundi has limited cement production
        /// </summary>
        public static readonly string[] CementSources = new[]
        {
            "BUCECO - Burundi Cement Company (local production)",
            "Imports from Tanzania (CIMERWA, Tanga Cement)",
            "Imports from Kenya (Bamburi, East African Portland)",
            "Imports from Uganda (Hima Cement)",
            "Imports from Rwanda (CIMERWA)"
        };

        /// <summary>
        /// Gets whether cement requires import documentation
        /// </summary>
        public static bool RequiresImportCertification(string source)
        {
            // Only BUCECO is local
            return !source.ToUpper().Contains("BUCECO");
        }

        /// <summary>
        /// Import requirements for construction materials in Burundi
        /// </summary>
        public static string[] GetImportRequirements()
        {
            return new[]
            {
                "BBN conformity certificate",
                "Certificate of origin",
                "EAC Standards Mark (for EAC imports)",
                "Import declaration",
                "Customs clearance",
                "Quality inspection at border"
            };
        }

        #endregion

        #region BN EAS 18 - Steel Reinforcement (Burundi Implementation)

        /// <summary>
        /// Steel reinforcement sources for Burundi
        /// All steel is imported
        /// </summary>
        public static readonly string[] SteelSources = new[]
        {
            "Imports from Tanzania",
            "Imports from Kenya",
            "Imports from Uganda",
            "Imports from DRC (Democratic Republic of Congo)",
            "International imports (requires BBN certification)"
        };

        /// <summary>
        /// Gets steel import inspection requirements
        /// </summary>
        public static string[] GetSteelImportInspection()
        {
            return new[]
            {
                "BBN quality inspection certificate",
                "Mill test certificate from manufacturer",
                "Physical testing at BBN laboratory (sample basis)",
                "Verification of grade and specifications",
                "Rust and defect inspection"
            };
        }

        #endregion

        #region Burundi Building Regulations

        /// <summary>
        /// Administrative regions of Burundi
        /// </summary>
        public enum BurundiProvince
        {
            /// <summary>Bujumbura Mairie - Capital city</summary>
            BujumburaMairie,
            /// <summary>Bujumbura Rural</summary>
            BujumburaRural,
            /// <summary>Gitega - Political capital</summary>
            Gitega,
            /// <summary>Ngozi - Northern province</summary>
            Ngozi,
            /// <summary>Other provinces</summary>
            OtherProvinces
        }

        /// <summary>
        /// Gets building permit requirements for province
        /// </summary>
        public static string[] GetBuildingPermitRequirements(BurundiProvince province)
        {
            var baseRequirements = new List<string>
            {
                "Approved architectural plans (French or Kirundi)",
                "Land title or lease agreement",
                "Provincial urban planning approval",
                "Environmental impact assessment (for large projects)",
                "Structural engineer certification (multi-story buildings)"
            };

            if (province == BurundiProvince.BujumburaMairie)
            {
                baseRequirements.AddRange(new[]
                {
                    "Bujumbura city council approval",
                    "Urban master plan compliance",
                    "Parking requirements verification",
                    "Utility connection agreements (REGIDESO, SOBUGEA)",
                    "Building inspection schedule"
                });
            }
            else if (province == BurundiProvince.Gitega)
            {
                baseRequirements.AddRange(new[]
                {
                    "Gitega municipal approval (new political capital)",
                    "Development plan compliance",
                    "Infrastructure coordination"
                });
            }

            return baseRequirements.ToArray();
        }

        /// <summary>
        /// Minimum construction standards for Burundi
        /// Based on EAS and French building practices
        /// </summary>
        public static class MinimumStandards
        {
            /// <summary>Minimum concrete grade</summary>
            public const string MinimumConcreteGrade = "C20/25";

            /// <summary>Minimum steel grade</summary>
            public const string MinimumSteelGrade = "Grade 460 (per EAS 18)";

            /// <summary>Minimum roof slope for Burundi's rainfall (degrees)</summary>
            public const double MinimumRoofSlope = 15.0;

            /// <summary>Minimum ceiling height (m)</summary>
            public const double MinimumCeilingHeight = 2.5;

            /// <summary>Minimum room sizes (m²)</summary>
            public static readonly Dictionary<string, double> MinimumRoomSizes = new()
            {
                { "Living room", 12.0 },
                { "Bedroom", 9.0 },
                { "Kitchen", 6.0 },
                { "Bathroom", 3.0 }
            };

            /// <summary>
            /// Required setbacks from boundaries (m)
            /// French-influenced urban planning
            /// </summary>
            public static (double Front, double Side, double Rear) GetRequiredSetbacks(
                BurundiProvince province)
            {
                return province switch
                {
                    BurundiProvince.BujumburaMairie => (5.0, 3.0, 3.0),
                    BurundiProvince.Gitega => (4.0, 2.5, 2.5),
                    _ => (3.0, 2.0, 2.0)
                };
            }
        }

        #endregion

        #region Electricity - REGIDESO

        /// <summary>
        /// Electricity and water utility in Burundi
        /// REGIDESO provides both electricity and water
        /// </summary>
        public const string ElectricityWaterProvider = "REGIDESO - Régie de Production et de Distribution d'Eau et d'Electricité";

        /// <summary>
        /// Burundi electrical supply characteristics
        /// </summary>
        public static readonly (double Voltage, double Frequency) BurundiPowerSupply = (220, 50);

        /// <summary>
        /// Gets electrical connection requirements
        /// </summary>
        public static string[] GetElectricalConnectionRequirements(double loadKW)
        {
            var requirements = new List<string>
            {
                "REGIDESO connection application",
                "BBN approved electrical materials",
                "Licensed electrician certificate",
                "Electrical installation plans",
                "REGIDESO inspection approval"
            };

            if (loadKW > 30)
            {
                requirements.Add("Three-phase connection required");
                requirements.Add("Load calculation documentation");
            }

            if (loadKW > 100)
            {
                requirements.Add("REGIDESO engineering review");
                requirements.Add("Dedicated transformer consideration");
            }

            // Power supply challenges in Burundi
            requirements.Add("Backup power system strongly recommended (frequent outages)");
            requirements.Add("Voltage stabilizer recommended");

            return requirements.ToArray();
        }

        /// <summary>
        /// Power reliability considerations for Burundi
        /// Burundi faces significant power supply challenges
        /// </summary>
        public static class PowerReliability
        {
            /// <summary>Average power outage frequency</summary>
            public const string OutageFrequency = "Frequent (daily in some areas)";

            /// <summary>
            /// Gets backup power recommendations
            /// </summary>
            public static string[] GetBackupPowerRecommendations(string buildingType)
            {
                var recommendations = new List<string>
                {
                    "Generator backup essential for critical facilities",
                    "UPS system for sensitive equipment",
                    "Solar power system recommended (abundant sunlight)",
                    "Battery storage for essential lighting"
                };

                if (buildingType.ToLower().Contains("hospital") ||
                    buildingType.ToLower().Contains("clinic") ||
                    buildingType.ToLower().Contains("data"))
                {
                    recommendations.Add("Dual redundant backup systems required");
                    recommendations.Add("Automatic transfer switch mandatory");
                }

                return recommendations.ToArray();
            }

            /// <summary>
            /// Solar energy potential in Burundi
            /// Burundi has good solar radiation despite being equatorial
            /// </summary>
            public static string[] GetSolarEnergyBenefits()
            {
                return new[]
                {
                    "Average solar radiation: 4.5-5.5 kWh/m²/day",
                    "Reduces dependence on unreliable grid",
                    "Government incentives for renewable energy",
                    "No import duty on solar equipment",
                    "Long-term cost savings"
                };
            }
        }

        #endregion

        #region Water Supply - REGIDESO

        /// <summary>
        /// Gets water connection requirements
        /// </summary>
        public static string[] GetWaterConnectionRequirements(BurundiProvince province)
        {
            var requirements = new List<string>
            {
                "REGIDESO connection application",
                "Plumbing plans and specifications",
                "BBN approved plumbing materials",
                "Licensed plumber certificate",
                "Water meter installation",
                "Connection fee payment"
            };

            if (province == BurundiProvince.BujumburaMairie)
            {
                requirements.Add("Water conservation measures recommended");
                requirements.Add("Water storage tank recommended (supply interruptions)");
            }
            else
            {
                requirements.Add("Alternative water source required (borehole/rainwater)");
                requirements.Add("Water quality testing for boreholes");
            }

            return requirements.ToArray();
        }

        /// <summary>
        /// Water supply challenges in Burundi
        /// </summary>
        public static class WaterSupply
        {
            /// <summary>
            /// Reliability of piped water supply
            /// </summary>
            public const string SupplyReliability = "Intermittent in most areas";

            /// <summary>
            /// Gets water storage recommendations
            /// </summary>
            public static double GetRecommendedStorageCapacity(
                int occupants,
                BurundiProvince province)
            {
                // Base: 50 liters per person per day
                double dailyDemand = occupants * 50;

                // Storage for 3-7 days depending on location
                int storageDays = province == BurundiProvince.BujumburaMairie ? 3 : 7;

                return dailyDemand * storageDays;
            }

            /// <summary>
            /// Alternative water sources for Burundi
            /// </summary>
            public static string[] GetAlternativeWaterSources()
            {
                return new[]
                {
                    "Borehole/well (requires Ministry of Water approval)",
                    "Rainwater harvesting (recommended)",
                    "Protected spring (rural areas)",
                    "Water delivery service (tanker trucks)",
                    "Community water points"
                };
            }

            /// <summary>
            /// Water quality considerations
            /// Lake Tanganyika is major water source
            /// </summary>
            public static string[] GetWaterQualityRequirements()
            {
                return new[]
                {
                    "Water quality testing required for boreholes",
                    "Treatment system for surface water",
                    "Filtration recommended for all water sources",
                    "Chlorination for storage tanks",
                    "Regular tank cleaning (every 6 months)"
                };
            }
        }

        #endregion

        #region Fire Safety and Building Code

        /// <summary>
        /// Fire safety requirements for Burundi
        /// Basic fire safety standards apply
        /// </summary>
        public static string[] GetFireSafetyRequirements(
            string buildingType,
            double buildingHeight,
            int occupantLoad)
        {
            var requirements = new List<string>
            {
                "Fire extinguishers (CO2 or powder)",
                "Fire assembly point",
                "Emergency exit signage",
                "Clear evacuation routes"
            };

            if (buildingHeight > 10 || occupantLoad > 50)
            {
                requirements.AddRange(new[]
                {
                    "Fire alarm system",
                    "Emergency lighting",
                    "Minimum two exits",
                    "Fire safety training"
                });
            }

            if (buildingType.ToLower().Contains("public") ||
                buildingType.ToLower().Contains("commercial"))
            {
                requirements.Add("Fire department notification");
                requirements.Add("Fire safety inspection before occupancy");
            }

            return requirements.ToArray();
        }

        /// <summary>
        /// Building material fire resistance
        /// </summary>
        public static string[] GetFireResistanceRequirements(int floors)
        {
            if (floors <= 2)
            {
                return new[]
                {
                    "Load-bearing walls: 1 hour fire resistance",
                    "Non-combustible roofing materials"
                };
            }
            else if (floors <= 5)
            {
                return new[]
                {
                    "Load-bearing walls: 2 hours fire resistance",
                    "Floor slabs: 1.5 hours fire resistance",
                    "Escape stairs: 2 hours fire resistance",
                    "Non-combustible construction materials"
                };
            }
            else
            {
                return new[]
                {
                    "Load-bearing structure: 3 hours fire resistance",
                    "Floor slabs: 2 hours fire resistance",
                    "Escape stairs: 3 hours fire resistance",
                    "Automatic sprinkler system",
                    "Fire-resistant materials throughout"
                };
            }
        }

        #endregion

        #region Seismic Considerations

        /// <summary>
        /// Seismic activity in Burundi
        /// Burundi is in the East African Rift System
        /// </summary>
        public static class SeismicConsiderations
        {
            /// <summary>
            /// Seismic zone classification
            /// Western Burundi (near Lake Tanganyika) has higher seismic risk
            /// </summary>
            public static string GetSeismicZone(string location)
            {
                if (location.ToLower().Contains("bujumbura") ||
                    location.ToLower().Contains("tanganyika") ||
                    location.ToLower().Contains("west"))
                {
                    return "Moderate seismic zone (near East African Rift)";
                }
                else
                {
                    return "Low seismic zone";
                }
            }

            /// <summary>
            /// Gets seismic design recommendations
            /// </summary>
            public static string[] GetSeismicDesignRecommendations(
                string location,
                double buildingHeight)
            {
                var recommendations = new List<string>();

                if (location.ToLower().Contains("bujumbura") && buildingHeight > 15)
                {
                    recommendations.AddRange(new[]
                    {
                        "Seismic design per Eurocode 8 recommended",
                        "Ductile detailing for reinforced concrete",
                        "Proper anchorage of non-structural elements",
                        "Structural engineer certification required"
                    });
                }

                recommendations.Add("Good construction practices essential");
                recommendations.Add("Regular structural inspections");

                return recommendations.ToArray();
            }
        }

        #endregion

        #region French-Influenced Construction Practices

        /// <summary>
        /// Burundi follows French construction norms due to colonial history
        /// </summary>
        public static class FrenchInfluencedPractices
        {
            /// <summary>
            /// Documentation language requirements
            /// </summary>
            public static string[] GetDocumentationRequirements()
            {
                return new[]
                {
                    "Official documents accepted in French or Kirundi",
                    "Technical drawings should include French labels",
                    "Material specifications can reference French standards (NFP)",
                    "International standards accepted with translation"
                };
            }

            /// <summary>
            /// Measurement system
            /// </summary>
            public const string MeasurementSystem = "Metric system (SI units)";

            /// <summary>
            /// Professional qualifications
            /// </summary>
            public static string[] GetProfessionalRequirements()
            {
                return new[]
                {
                    "Architects must be registered with OAB (Ordre des Architectes du Burundi)",
                    "Engineers must be certified",
                    "Contractors require business license",
                    "Foreign professionals need work permits and local partnership"
                };
            }
        }

        #endregion

        #region Climate and Environmental Considerations

        /// <summary>
        /// Burundi climate characteristics
        /// Tropical highland climate
        /// </summary>
        public static class ClimateConsiderations
        {
            /// <summary>Annual rainfall (mm)</summary>
            public const double AnnualRainfall = 1200;

            /// <summary>Temperature range (°C)</summary>
            public static readonly (double Min, double Max) TemperatureRange = (17, 28);

            /// <summary>Rainy seasons</summary>
            public static readonly string[] RainySeasons = new[]
            {
                "October to December",
                "February to May"
            };

            /// <summary>
            /// Gets construction recommendations for climate
            /// </summary>
            public static string[] GetClimateDesignRecommendations()
            {
                return new[]
                {
                    "Roof slope minimum 15° for rainfall",
                    "Wide roof overhangs (600mm minimum)",
                    "Natural ventilation design (highland climate)",
                    "Thermal mass for temperature regulation",
                    "Waterproofing critical during rainy seasons",
                    "Termite protection required",
                    "Damp-proofing for ground contact",
                    "Schedule major construction during dry season (June-September)"
                };
            }

            /// <summary>
            /// Gets elevation-based design considerations
            /// Burundi is mostly highland (1,400-2,000m altitude)
            /// </summary>
            public static string[] GetElevationConsiderations(double elevationM)
            {
                var considerations = new List<string>();

                if (elevationM > 1500)
                {
                    considerations.AddRange(new[]
                    {
                        "Cooler temperatures - heating may be needed",
                        "Lower air pressure affects concrete curing",
                        "UV protection for materials (higher altitude)"
                    });
                }

                if (elevationM < 1000)
                {
                    considerations.AddRange(new[]
                    {
                        "Higher humidity near Lake Tanganyika",
                        "Enhanced corrosion protection",
                        "Mosquito protection (malaria zone)"
                    });
                }

                return considerations.ToArray();
            }
        }

        #endregion
    }
}
