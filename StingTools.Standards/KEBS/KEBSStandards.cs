using System;
using System.Collections.Generic;

namespace StingTools.Standards.KEBS
{
    /// <summary>
    /// KEBS - Kenya Bureau of Standards
    /// National standards body for Kenya
    /// 
    /// KEBS is responsible for:
    /// - Development and enforcement of standards
    /// - Quality assurance and certification
    /// - Metrology and testing
    /// - Import inspection
    /// 
    /// Kenya is the largest economy in the East African Community
    /// and a major hub for construction and manufacturing.
    /// </summary>
    public static class KEBSStandards
    {
        #region KS 02-1070 - Code of Practice for Design and Construction

        /// <summary>
        /// Building categories as per KS 02-1070
        /// </summary>
        public enum BuildingCategory
        {
            /// <summary>Category I - Minor importance buildings</summary>
            CategoryI_Minor,
            /// <summary>Category II - Normal importance buildings</summary>
            CategoryII_Normal,
            /// <summary>Category III - Important buildings</summary>
            CategoryIII_Important,
            /// <summary>Category IV - Critical/essential facilities</summary>
            CategoryIV_Critical
        }

        /// <summary>
        /// Gets importance factor for structural design
        /// </summary>
        public static double GetImportanceFactor(BuildingCategory category)
        {
            return category switch
            {
                BuildingCategory.CategoryI_Minor => 0.8,
                BuildingCategory.CategoryII_Normal => 1.0,
                BuildingCategory.CategoryIII_Important => 1.15,
                BuildingCategory.CategoryIV_Critical => 1.5,
                _ => 1.0
            };
        }

        /// <summary>
        /// Gets minimum design life for building category (years)
        /// </summary>
        public static int GetMinimumDesignLife(BuildingCategory category)
        {
            return category switch
            {
                BuildingCategory.CategoryI_Minor => 10,
                BuildingCategory.CategoryII_Normal => 50,
                BuildingCategory.CategoryIII_Important => 100,
                BuildingCategory.CategoryIV_Critical => 100,
                _ => 50
            };
        }

        /// <summary>
        /// Seismic zones in Kenya
        /// </summary>
        public enum SeismicZone
        {
            /// <summary>Zone 0 - No seismic activity</summary>
            Zone0_None,
            /// <summary>Zone I - Low seismicity</summary>
            ZoneI_Low,
            /// <summary>Zone II - Moderate seismicity (Rift Valley)</summary>
            ZoneII_Moderate,
            /// <summary>Zone III - High seismicity</summary>
            ZoneIII_High
        }

        /// <summary>
        /// Gets seismic zone factor for design
        /// </summary>
        public static double GetSeismicZoneFactor(SeismicZone zone)
        {
            return zone switch
            {
                SeismicZone.Zone0_None => 0.0,
                SeismicZone.ZoneI_Low => 0.1,
                SeismicZone.ZoneII_Moderate => 0.15, // Rift Valley areas
                SeismicZone.ZoneIII_High => 0.25,
                _ => 0.1
            };
        }

        /// <summary>
        /// Major cities and their seismic zones
        /// </summary>
        public static readonly Dictionary<string, SeismicZone> CitySeismicZones = 
            new Dictionary<string, SeismicZone>
        {
            { "Nairobi", SeismicZone.ZoneII_Moderate },
            { "Mombasa", SeismicZone.ZoneI_Low },
            { "Kisumu", SeismicZone.ZoneII_Moderate },
            { "Nakuru", SeismicZone.ZoneII_Moderate },
            { "Eldoret", SeismicZone.ZoneII_Moderate },
            { "Thika", SeismicZone.ZoneII_Moderate },
            { "Malindi", SeismicZone.ZoneI_Low },
            { "Garissa", SeismicZone.ZoneI_Low }
        };

        #endregion

        #region KS EAS 2 - Portland Cement (Kenya Implementation)

        /// <summary>
        /// Approved cement manufacturers in Kenya
        /// </summary>
        public static readonly string[] ApprovedCementManufacturers = new[]
        {
            "Bamburi Cement",
            "East African Portland Cement (EAPCC)",
            "National Cement",
            "Mombasa Cement",
            "Savannah Cement",
            "ARM Cement",
            "Athi River Mining"
        };

        /// <summary>
        /// Gets whether a cement brand is KEBS certified
        /// </summary>
        public static bool IsCertifiedCement(string manufacturer, string grade)
        {
            // All approved manufacturers must maintain KEBS certification
            foreach (var approved in ApprovedCementManufacturers)
            {
                if (manufacturer.IndexOf(approved, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region KS EAS 18 - Steel Reinforcement (Kenya Implementation)

        /// <summary>
        /// Approved steel manufacturers in Kenya
        /// </summary>
        public static readonly string[] ApprovedSteelManufacturers = new[]
        {
            "Devki Steel Mills",
            "Devki Steelco",
            "Mabati Rolling Mills",
            "Steel Africa",
            "Hass Petroleum (TMT Bars)",
            "Simba Industries"
        };

        /// <summary>
        /// Gets corrosion protection requirements based on exposure condition
        /// </summary>
        public static string GetCorrosionProtection(string exposureCondition)
        {
            return exposureCondition.ToLower() switch
            {
                "coastal" or "marine" => "Epoxy-coated or stainless steel reinforcement required",
                "industrial" => "Increased concrete cover (75mm minimum)",
                "aggressive" => "Corrosion inhibitors in concrete mix required",
                "normal" => "Standard concrete cover (25-40mm)",
                _ => "Standard protection adequate"
            };
        }

        #endregion

        #region KS 1600 - Electrical Installations (Based on BS 7671)

        /// <summary>
        /// Kenya electrical supply characteristics
        /// </summary>
        public static readonly (double Voltage, double Frequency) KenyaPowerSupply = (240, 50);

        /// <summary>
        /// Gets required earthing system for Kenya installations
        /// </summary>
        public static string GetRequiredEarthingSystem(string buildingType)
        {
            return buildingType.ToLower() switch
            {
                "residential" => "TN-S or TN-C-S system",
                "commercial" => "TN-S system with separate neutral and earth",
                "industrial" => "TN-S system with additional earth electrodes",
                "high-rise" => "TN-S with lightning protection system",
                _ => "TN-S system"
            };
        }

        /// <summary>
        /// Minimum earthing electrode resistance (Ω)
        /// </summary>
        public static double GetMaximumEarthResistance(string buildingType)
        {
            return buildingType.ToLower() switch
            {
                "residential" => 10.0,
                "commercial" => 5.0,
                "industrial" => 1.0,
                "hospital" or "datacenter" => 1.0,
                _ => 10.0
            };
        }

        /// <summary>
        /// Gets whether generator backup is mandatory
        /// </summary>
        public static bool IsGeneratorBackupRequired(string buildingType, double capacity)
        {
            if (buildingType.ToLower().Contains("hospital")) return true;
            if (buildingType.ToLower().Contains("datacenter")) return true;
            if (buildingType.ToLower().Contains("telecom")) return true;
            if (buildingType.ToLower() == "commercial" && capacity > 500) return true; // >500kW
            
            return false;
        }

        #endregion

        #region KS 1686 - Water Supply Installations

        /// <summary>
        /// Water supply pressure requirements in Kenya
        /// </summary>
        public static (double Minimum, double Maximum) GetWaterPressureRequirements(string buildingType)
        {
            return buildingType.ToLower() switch
            {
                "residential" => (150, 500), // kPa
                "commercial" => (200, 600),
                "industrial" => (300, 800),
                "high-rise" => (250, 700),
                _ => (150, 500)
            };
        }

        /// <summary>
        /// Minimum water storage capacity per person (liters/day)
        /// </summary>
        public static double GetMinimumWaterStorage(string occupancyType)
        {
            return occupancyType.ToLower() switch
            {
                "residential" => 135, // 135 L/person/day
                "office" => 60,
                "school" => 30,
                "hospital" => 250,
                "hotel" => 200,
                "restaurant" => 50,
                _ => 100
            };
        }

        /// <summary>
        /// Gets whether rainwater harvesting is required
        /// </summary>
        public static bool IsRainwaterHarvestingRequired(double plotArea, string county)
        {
            // Mandatory in Nairobi for plots > 100m²
            if (county.ToLower() == "nairobi" && plotArea > 100) return true;
            
            // Encouraged in all urban areas
            if (plotArea > 500) return true;
            
            return false;
        }

        /// <summary>
        /// Minimum rainwater storage capacity (liters)
        /// </summary>
        public static double GetMinimumRainwaterStorage(double roofArea)
        {
            // 30% of annual harvestable rainwater
            double annualRainfall = 800; // mm (Nairobi average)
            double harvestableRainwater = roofArea * annualRainfall * 0.8; // 80% efficiency
            return harvestableRainwater * 0.3; // 30% minimum storage
        }

        #endregion

        #region KS 2054 - Fire Safety in Buildings

        /// <summary>
        /// Fire safety categories based on building height and occupancy
        /// </summary>
        public enum FireSafetyCategory
        {
            /// <summary>Low risk - Single story, low occupancy</summary>
            LowRisk,
            /// <summary>Medium risk - Multi-story, normal occupancy</summary>
            MediumRisk,
            /// <summary>High risk - High-rise or high occupancy</summary>
            HighRisk,
            /// <summary>Special risk - Hazardous materials or critical facilities</summary>
            SpecialRisk
        }

        /// <summary>
        /// Determines fire safety category
        /// </summary>
        public static FireSafetyCategory DetermineFireSafetyCategory(
            double buildingHeight,
            int occupantLoad,
            bool hazardousMaterials)
        {
            if (hazardousMaterials)
                return FireSafetyCategory.SpecialRisk;
            
            if (buildingHeight > 30 || occupantLoad > 1000)
                return FireSafetyCategory.HighRisk;
            
            if (buildingHeight > 10 || occupantLoad > 100)
                return FireSafetyCategory.MediumRisk;
            
            return FireSafetyCategory.LowRisk;
        }

        /// <summary>
        /// Gets required fire detection and alarm system
        /// </summary>
        public static string[] GetRequiredFireSystems(FireSafetyCategory category)
        {
            return category switch
            {
                FireSafetyCategory.SpecialRisk => new[]
                {
                    "Automatic fire detection system",
                    "Automatic fire suppression (sprinklers)",
                    "Manual fire alarm system",
                    "Emergency voice communication",
                    "Fire fighter access panels",
                    "Smoke control system"
                },
                FireSafetyCategory.HighRisk => new[]
                {
                    "Automatic fire detection system",
                    "Automatic fire suppression (sprinklers)",
                    "Manual fire alarm system",
                    "Emergency lighting",
                    "Fire fighter lifts"
                },
                FireSafetyCategory.MediumRisk => new[]
                {
                    "Manual fire alarm system",
                    "Fire extinguishers",
                    "Emergency lighting",
                    "Fire assembly points"
                },
                FireSafetyCategory.LowRisk => new[]
                {
                    "Fire extinguishers",
                    "Fire assembly point"
                },
                _ => new[] { "Fire extinguishers" }
            };
        }

        /// <summary>
        /// Minimum number of fire exits required
        /// </summary>
        public static int GetMinimumFireExits(int occupantLoad, int buildingFloors)
        {
            if (occupantLoad > 500 || buildingFloors > 5)
                return 3;
            else if (occupantLoad > 100 || buildingFloors > 2)
                return 2;
            else
                return 1;
        }

        #endregion

        #region KS ISO 9001 - Quality Management (Kenya Context)

        /// <summary>
        /// KEBS quality certification marks
        /// </summary>
        public enum KEBSCertificationMark
        {
            /// <summary>Diamond Mark of Quality</summary>
            DiamondMark,
            /// <summary>Standardization Mark</summary>
            StandardizationMark,
            /// <summary>Permit Mark (for imports)</summary>
            PermitMark
        }

        /// <summary>
        /// Gets certification requirements for product category
        /// </summary>
        public static string[] GetCertificationRequirements(string productCategory)
        {
            return productCategory.ToLower() switch
            {
                "cement" => new[]
                {
                    "Factory inspection by KEBS",
                    "Laboratory testing of samples",
                    "Quality management system certification",
                    "Diamond Mark of Quality application",
                    "Annual surveillance audits"
                },
                "steel" => new[]
                {
                    "Material composition testing",
                    "Mechanical properties testing",
                    "Dimensional tolerance verification",
                    "Quality system certification",
                    "Quarterly monitoring"
                },
                "electrical" => new[]
                {
                    "Safety testing",
                    "Performance testing",
                    "EMC testing",
                    "Factory audit",
                    "Standardization Mark"
                },
                _ => new[]
                {
                    "Product testing",
                    "Quality system audit",
                    "KEBS certification"
                }
            };
        }

        #endregion

        #region KS 2527 - Concrete Mix Design

        /// <summary>
        /// Standard concrete grades used in Kenya
        /// </summary>
        public enum ConcreteGrade
        {
            /// <summary>C12/15 - Blinding and mass concrete</summary>
            C12_15,
            /// <summary>C20/25 - Lightly loaded structures</summary>
            C20_25,
            /// <summary>C25/30 - General structural use</summary>
            C25_30,
            /// <summary>C30/37 - Heavy duty structures</summary>
            C30_37,
            /// <summary>C35/45 - Columns and high-rise</summary>
            C35_45,
            /// <summary>C40/50 - Prestressed concrete</summary>
            C40_50
        }

        /// <summary>
        /// Gets characteristic strength for concrete grade (MPa)
        /// Cylinder/Cube format
        /// </summary>
        public static (double Cylinder, double Cube) GetConcreteStrength(ConcreteGrade grade)
        {
            return grade switch
            {
                ConcreteGrade.C12_15 => (12, 15),
                ConcreteGrade.C20_25 => (20, 25),
                ConcreteGrade.C25_30 => (25, 30),
                ConcreteGrade.C30_37 => (30, 37),
                ConcreteGrade.C35_45 => (35, 45),
                ConcreteGrade.C40_50 => (40, 50),
                _ => (0, 0)
            };
        }

        /// <summary>
        /// Gets typical mix proportions (Cement:Sand:Aggregate)
        /// </summary>
        public static (double Cement, double Sand, double Aggregate) GetMixProportions(
            ConcreteGrade grade)
        {
            return grade switch
            {
                ConcreteGrade.C12_15 => (1, 3, 6),
                ConcreteGrade.C20_25 => (1, 2, 4),
                ConcreteGrade.C25_30 => (1, 1.5, 3),
                ConcreteGrade.C30_37 => (1, 1, 2),
                ConcreteGrade.C35_45 => (1, 1, 1.5),
                ConcreteGrade.C40_50 => (1, 0.75, 1.25),
                _ => (1, 2, 4)
            };
        }

        /// <summary>
        /// Gets water-cement ratio for target strength
        /// </summary>
        public static double GetWaterCementRatio(ConcreteGrade grade, string exposureCondition)
        {
            double baseRatio = grade switch
            {
                ConcreteGrade.C12_15 => 0.65,
                ConcreteGrade.C20_25 => 0.60,
                ConcreteGrade.C25_30 => 0.55,
                ConcreteGrade.C30_37 => 0.50,
                ConcreteGrade.C35_45 => 0.45,
                ConcreteGrade.C40_50 => 0.40,
                _ => 0.60
            };

            // Reduce for aggressive exposure
            if (exposureCondition.ToLower().Contains("marine") ||
                exposureCondition.ToLower().Contains("aggressive"))
            {
                baseRatio = Math.Min(baseRatio, 0.45);
            }

            return baseRatio;
        }

        #endregion

        #region Kenya-Specific Requirements

        /// <summary>
        /// National Construction Authority (NCA) registration categories
        /// </summary>
        public enum NCACategory
        {
            /// <summary>NCA 1 - Up to KES 1M</summary>
            NCA1,
            /// <summary>NCA 2 - Up to KES 5M</summary>
            NCA2,
            /// <summary>NCA 3 - Up to KES 10M</summary>
            NCA3,
            /// <summary>NCA 4 - Up to KES 20M</summary>
            NCA4,
            /// <summary>NCA 5 - Up to KES 50M</summary>
            NCA5,
            /// <summary>NCA 6 - Up to KES 100M</summary>
            NCA6,
            /// <summary>NCA 7 - Up to KES 200M</summary>
            NCA7,
            /// <summary>NCA 8 - Unlimited</summary>
            NCA8
        }

        /// <summary>
        /// Gets maximum contract value for NCA category (KES)
        /// </summary>
        public static double GetMaximumContractValue(NCACategory category)
        {
            return category switch
            {
                NCACategory.NCA1 => 1_000_000,
                NCACategory.NCA2 => 5_000_000,
                NCACategory.NCA3 => 10_000_000,
                NCACategory.NCA4 => 20_000_000,
                NCACategory.NCA5 => 50_000_000,
                NCACategory.NCA6 => 100_000_000,
                NCACategory.NCA7 => 200_000_000,
                NCACategory.NCA8 => double.MaxValue,
                _ => 0
            };
        }

        /// <summary>
        /// County governments and their specific building requirements
        /// </summary>
        public static readonly Dictionary<string, string[]> CountyRequirements = 
            new Dictionary<string, string[]>
        {
            { "Nairobi", new[] { "NCA approval", "County approval", "NEMA approval", "Water harvesting mandatory", "Parking as per zoning" } },
            { "Mombasa", new[] { "Coastal building code", "Cyclone resistance", "Corrosion protection", "Port authority clearance" } },
            { "Kisumu", new[] { "Lake Victoria zone requirements", "Environmental clearance", "Water table considerations" } },
            { "Nakuru", new[] { "Seismic zone II design", "Rift Valley geology", "County approval" } }
        };

        #endregion
    }
}
