using System;
using System.Collections.Generic;

namespace StingBIM.Standards.TBS
{
    /// <summary>
    /// TBS - Tanzania Bureau of Standards
    /// National standards body for Tanzania
    /// 
    /// TBS is mandated to:
    /// - Prepare and promote use of standards
    /// - Provide quality assurance and certification services
    /// - Conduct testing and calibration
    /// - Promote standardization in trade and industry
    /// 
    /// Tanzania uses TZS (Tanzania Standard) prefix for national standards
    /// and has adopted many EAS (East African Standards).
    /// </summary>
    public static class TBSStandards
    {
        #region TZS 2 - Portland Cement

        /// <summary>
        /// Cement types certified by TBS
        /// </summary>
        public enum TZSCementType
        {
            /// <summary>Ordinary Portland Cement 32.5</summary>
            OPC_32_5,
            /// <summary>Ordinary Portland Cement 42.5</summary>
            OPC_42_5,
            /// <summary>Portland Pozzolana Cement</summary>
            PPC,
            /// <summary>Portland Limestone Cement</summary>
            PLC,
            /// <summary>Sulphate Resisting Cement</summary>
            SRC
        }

        /// <summary>
        /// Major cement manufacturers in Tanzania
        /// </summary>
        public static readonly string[] TanzaniaCementManufacturers = new[]
        {
            "Tanga Cement",
            "Mbeya Cement",
            "Tanzania Portland Cement Company (Twiga)",
            "Lake Cement",
            "Dangote Cement Tanzania"
        };

        /// <summary>
        /// Gets setting time requirements for cement (minutes)
        /// </summary>
        public static (int InitialSet, int FinalSet) GetSettingTimeRequirements(TZSCementType type)
        {
            return type switch
            {
                TZSCementType.OPC_32_5 => (60, 600),  // Min 60 min, Max 600 min
                TZSCementType.OPC_42_5 => (60, 600),
                TZSCementType.PPC => (60, 600),
                TZSCementType.PLC => (60, 600),
                TZSCementType.SRC => (60, 600),
                _ => (60, 600)
            };
        }

        /// <summary>
        /// Gets soundness limit (Le Chatelier expansion) in mm
        /// </summary>
        public static double GetSoundnessLimit()
        {
            return 10.0; // Maximum 10mm expansion
        }

        #endregion

        #region TZS 148 - Structural Steel

        /// <summary>
        /// Structural steel grades used in Tanzania
        /// </summary>
        public enum StructuralSteelGrade
        {
            /// <summary>Mild steel Fe 360</summary>
            Fe360,
            /// <summary>Medium tensile Fe 430</summary>
            Fe430,
            /// <summary>High tensile Fe 510</summary>
            Fe510
        }

        /// <summary>
        /// Gets yield strength for structural steel grade (MPa)
        /// </summary>
        public static double GetSteelYieldStrength(StructuralSteelGrade grade)
        {
            return grade switch
            {
                StructuralSteelGrade.Fe360 => 235,
                StructuralSteelGrade.Fe430 => 275,
                StructuralSteelGrade.Fe510 => 355,
                _ => 0
            };
        }

        /// <summary>
        /// Gets tensile strength for structural steel grade (MPa)
        /// </summary>
        public static (double Minimum, double Maximum) GetSteelTensileStrength(StructuralSteelGrade grade)
        {
            return grade switch
            {
                StructuralSteelGrade.Fe360 => (340, 470),
                StructuralSteelGrade.Fe430 => (410, 560),
                StructuralSteelGrade.Fe510 => (490, 630),
                _ => (0, 0)
            };
        }

        /// <summary>
        /// Minimum elongation percentage for structural steel
        /// </summary>
        public static double GetMinimumElongation(StructuralSteelGrade grade)
        {
            return grade switch
            {
                StructuralSteelGrade.Fe360 => 26,
                StructuralSteelGrade.Fe430 => 22,
                StructuralSteelGrade.Fe510 => 20,
                _ => 20
            };
        }

        #endregion

        #region TZS 292 - Building Code

        /// <summary>
        /// Climate zones in Tanzania for building design
        /// </summary>
        public enum TanzaniaClimateZone
        {
            /// <summary>Coastal hot-humid (Dar es Salaam, Zanzibar)</summary>
            CoastalHotHumid,
            /// <summary>Central plateau (Dodoma, Singida)</summary>
            CentralPlateau,
            /// <summary>Lake zone (Mwanza, Bukoba)</summary>
            LakeZone,
            /// <summary>Northern highlands (Arusha, Moshi)</summary>
            NorthernHighlands,
            /// <summary>Southern highlands (Mbeya, Iringa)</summary>
            SouthernHighlands
        }

        /// <summary>
        /// Gets design temperature range for climate zone (°C)
        /// </summary>
        public static (double MinTemp, double MaxTemp) GetDesignTemperature(TanzaniaClimateZone zone)
        {
            return zone switch
            {
                TanzaniaClimateZone.CoastalHotHumid => (22, 32),
                TanzaniaClimateZone.CentralPlateau => (15, 30),
                TanzaniaClimateZone.LakeZone => (18, 28),
                TanzaniaClimateZone.NorthernHighlands => (12, 25),
                TanzaniaClimateZone.SouthernHighlands => (10, 24),
                _ => (15, 30)
            };
        }

        /// <summary>
        /// Gets annual rainfall for climate zone (mm)
        /// </summary>
        public static (double MinRainfall, double MaxRainfall) GetAnnualRainfall(TanzaniaClimateZone zone)
        {
            return zone switch
            {
                TanzaniaClimateZone.CoastalHotHumid => (1000, 1500),
                TanzaniaClimateZone.CentralPlateau => (500, 900),
                TanzaniaClimateZone.LakeZone => (1000, 1800),
                TanzaniaClimateZone.NorthernHighlands => (800, 1200),
                TanzaniaClimateZone.SouthernHighlands => (900, 1400),
                _ => (700, 1200)
            };
        }

        /// <summary>
        /// Building height categories in Tanzania
        /// </summary>
        public enum BuildingHeightCategory
        {
            /// <summary>Low-rise: Up to 4 stories (≤15m)</summary>
            LowRise,
            /// <summary>Medium-rise: 5-12 stories (15-40m)</summary>
            MediumRise,
            /// <summary>High-rise: 13-30 stories (40-100m)</summary>
            HighRise,
            /// <summary>Tall building: >30 stories (>100m)</summary>
            TallBuilding
        }

        /// <summary>
        /// Gets structural requirements for building height category
        /// </summary>
        public static string[] GetStructuralRequirements(BuildingHeightCategory category)
        {
            return category switch
            {
                BuildingHeightCategory.LowRise => new[]
                {
                    "Concrete grade C25 minimum",
                    "Foundation analysis required",
                    "Basic wind load analysis"
                },
                BuildingHeightCategory.MediumRise => new[]
                {
                    "Concrete grade C30 minimum",
                    "Detailed soil investigation",
                    "Wind tunnel testing for exposed sites",
                    "Seismic design for Rift Valley zone",
                    "Structural engineer certification"
                },
                BuildingHeightCategory.HighRise => new[]
                {
                    "Concrete grade C35 minimum",
                    "Comprehensive geotechnical report",
                    "Wind tunnel testing mandatory",
                    "Dynamic seismic analysis",
                    "Peer review by independent engineer",
                    "Foundation monitoring system"
                },
                BuildingHeightCategory.TallBuilding => new[]
                {
                    "Concrete grade C40 minimum",
                    "Advanced geotechnical investigation",
                    "CFD wind analysis",
                    "Time-history seismic analysis",
                    "International expert peer review",
                    "Structural health monitoring system",
                    "Presidential approval required"
                },
                _ => new[] { "Standard structural design" }
            };
        }

        #endregion

        #region TZS 789 - Electrical Installations

        /// <summary>
        /// Tanzania electrical supply characteristics
        /// </summary>
        public static readonly (double Voltage, double Frequency) TanzaniaPowerSupply = (230, 50);

        /// <summary>
        /// Regional electricity distribution companies
        /// </summary>
        public static readonly Dictionary<string, string> ElectricityDistributors = new Dictionary<string, string>
        {
            { "Dar es Salaam", "TANESCO - Dar es Salaam Region" },
            { "Arusha", "TANESCO - Northern Zone" },
            { "Mwanza", "TANESCO - Lake Zone" },
            { "Dodoma", "TANESCO - Central Zone" },
            { "Mbeya", "TANESCO - Southern Highlands" },
            { "Zanzibar", "ZECO - Zanzibar Electricity Corporation" }
        };

        /// <summary>
        /// Gets electrical connection requirements for building type
        /// </summary>
        public static string[] GetConnectionRequirements(string buildingType, double loadKW)
        {
            var requirements = new List<string>
            {
                "TBS approved electrical materials",
                "Licensed electrician installation",
                "Electrical drawings and specifications"
            };

            if (loadKW > 100)
            {
                requirements.Add("Load study and power factor analysis");
                requirements.Add("Dedicated transformer may be required");
                requirements.Add("TANESCO approval for connection");
            }

            if (buildingType.ToLower().Contains("industrial"))
            {
                requirements.Add("Three-phase supply mandatory");
                requirements.Add("Power factor correction equipment");
                requirements.Add("Emergency shutdown systems");
            }

            if (buildingType.ToLower().Contains("hospital") || 
                buildingType.ToLower().Contains("datacenter"))
            {
                requirements.Add("Backup generator mandatory");
                requirements.Add("UPS system for critical loads");
                requirements.Add("Automatic transfer switch");
            }

            return requirements.ToArray();
        }

        /// <summary>
        /// Voltage drop limits for electrical circuits (%)
        /// </summary>
        public static double GetMaximumVoltageDrop(string circuitType)
        {
            return circuitType.ToLower() switch
            {
                "lighting" => 3.0,
                "power" => 5.0,
                "motor" => 5.0,
                _ => 4.0
            };
        }

        #endregion

        #region TZS 845 - Water Supply and Sanitation

        /// <summary>
        /// Water supply authorities in Tanzania
        /// </summary>
        public static readonly Dictionary<string, string> WaterAuthorities = new Dictionary<string, string>
        {
            { "Dar es Salaam", "DAWASA - Dar es Salaam Water and Sewerage Authority" },
            { "Arusha", "AUWSA - Arusha Urban Water and Sanitation Authority" },
            { "Mwanza", "MWAUWASA - Mwanza Urban Water and Sewerage Authority" },
            { "Dodoma", "DUWASA - Dodoma Urban Water and Sewerage Authority" },
            { "Mbeya", "MUWSA - Mbeya Urban Water and Sewerage Authority" },
            { "Zanzibar", "ZAWA - Zanzibar Water Authority" }
        };

        /// <summary>
        /// Minimum water demand per capita (liters/day)
        /// </summary>
        public static double GetWaterDemandPerCapita(string occupancyType)
        {
            return occupancyType.ToLower() switch
            {
                "residential" => 120,
                "office" => 50,
                "school" => 30,
                "hospital" => 250,
                "hotel" => 180,
                "restaurant" => 60,
                _ => 100
            };
        }

        /// <summary>
        /// Gets whether borehole is permitted
        /// </summary>
        public static (bool Permitted, string[] Requirements) IsBoreholePermitted(
            string location,
            string waterAuthority)
        {
            // Boreholes generally permitted outside main water supply areas
            var requirements = new List<string>
            {
                "Water rights permit from Basin Water Board",
                "Environmental impact assessment",
                "Borehole drilling permit",
                "Water quality testing",
                "Metering and reporting requirements"
            };

            if (location.ToLower().Contains("dar es salaam") || 
                location.ToLower().Contains("arusha"))
            {
                requirements.Add("Justification for borehole (main supply insufficient)");
                requirements.Add("Connection to main supply still required");
            }

            return (true, requirements.ToArray());
        }

        /// <summary>
        /// Minimum sewerage pipe gradients (%)
        /// </summary>
        public static double GetMinimumSewerageGradient(int pipeDiameter)
        {
            return pipeDiameter switch
            {
                100 => 1.0, // 1% for 100mm
                150 => 0.6, // 0.6% for 150mm
                225 => 0.4, // 0.4% for 225mm
                >= 300 => 0.3, // 0.3% for 300mm+
                _ => 1.0
            };
        }

        #endregion

        #region TZS 1200 - Fire Protection

        /// <summary>
        /// Fire resistance requirements based on building type and height
        /// </summary>
        public static int GetRequiredFireResistance(string buildingType, double heightMeters)
        {
            int baseRequirement = buildingType.ToLower() switch
            {
                "residential" => 60,  // 60 minutes
                "commercial" => 90,   // 90 minutes
                "industrial" => 120,  // 120 minutes
                "hospital" => 120,
                "school" => 90,
                _ => 60
            };

            // Increase for tall buildings
            if (heightMeters > 45)
                return Math.Max(baseRequirement, 180); // 3 hours for very tall
            else if (heightMeters > 28)
                return Math.Max(baseRequirement, 120); // 2 hours for tall
            else if (heightMeters > 15)
                return Math.Max(baseRequirement, 90);  // 90 min for medium

            return baseRequirement;
        }

        /// <summary>
        /// Sprinkler system requirements
        /// </summary>
        public static bool IsSprinklerSystemRequired(
            string buildingType,
            double totalFloorArea,
            double buildingHeight)
        {
            // Mandatory for buildings >28m
            if (buildingHeight > 28) return true;

            // Mandatory for large floor areas
            if (totalFloorArea > 3000) return true;

            // Mandatory for specific occupancies
            if (buildingType.ToLower().Contains("hospital")) return true;
            if (buildingType.ToLower().Contains("hotel") && totalFloorArea > 1000) return true;
            if (buildingType.ToLower().Contains("assembly") && totalFloorArea > 1000) return true;

            return false;
        }

        /// <summary>
        /// Fire escape staircase requirements
        /// </summary>
        public static int GetRequiredFireEscapeStaircases(int numberOfFloors, int occupantLoad)
        {
            if (numberOfFloors > 10 || occupantLoad > 500)
                return 3;
            else if (numberOfFloors > 5 || occupantLoad > 200)
                return 2;
            else
                return 1;
        }

        #endregion

        #region TZS ISO 9001 - Quality Management (Tanzania Implementation)

        /// <summary>
        /// TBS quality marks and certifications
        /// </summary>
        public enum TBSQualityMark
        {
            /// <summary>TBS Mark of Quality - Mandatory standards</summary>
            TBSMark_Mandatory,
            /// <summary>TBS Mark of Quality - Voluntary standards</summary>
            TBSMark_Voluntary,
            /// <summary>Import Standards Mark</summary>
            ImportMark
        }

        /// <summary>
        /// Products requiring mandatory TBS certification
        /// </summary>
        public static readonly string[] MandatoryCertificationProducts = new[]
        {
            "Portland cement",
            "Steel reinforcement bars",
            "Electrical cables and wires",
            "Water pipes (PVC)",
            "Roofing sheets",
            "Concrete blocks",
            "Electrical switches and sockets",
            "Circuit breakers",
            "Cooking gas cylinders",
            "Paints and coatings"
        };

        /// <summary>
        /// Checks if product requires mandatory TBS certification
        /// </summary>
        public static bool RequiresMandatoryCertification(string productType)
        {
            foreach (var mandatory in MandatoryCertificationProducts)
            {
                if (productType.ToLower().Contains(mandatory.ToLower()))
                    return true;
            }
            return false;
        }

        #endregion

        #region Tanzania-Specific Requirements

        /// <summary>
        /// Major cities and their specific requirements
        /// </summary>
        public static readonly Dictionary<string, string[]> CitySpecificRequirements = 
            new Dictionary<string, string[]>
        {
            { "Dar es Salaam", new[]
            {
                "Coastal building code provisions",
                "Corrosion protection mandatory",
                "DAWASA water connection approval",
                "City Council building permit",
                "Environmental clearance (NEMC)",
                "Plot premium payment"
            }},
            { "Zanzibar", new[]
            {
                "Stone Town conservation rules (if applicable)",
                "Zanzibar Building Control Authority approval",
                "ZAWA water connection",
                "ZECO electricity connection",
                "Islamic architectural guidelines (optional)",
                "Tourism zoning compliance (if applicable)"
            }},
            { "Arusha", new[]
            {
                "Northern zone building code",
                "Seismic considerations (Mount Meru proximity)",
                "Tourism area regulations",
                "AUWSA water connection",
                "Environmental clearance"
            }},
            { "Dodoma", new[]
            {
                "Capital city development plan compliance",
                "Government precinct regulations",
                "DUWASA water connection",
                "Central zone standards"
            }}
        };

        /// <summary>
        /// Contractors Registration Board (CRB) categories
        /// </summary>
        public enum CRBCategory
        {
            /// <summary>Class 7 - Up to TZS 2M</summary>
            Class7,
            /// <summary>Class 6 - Up to TZS 10M</summary>
            Class6,
            /// <summary>Class 5 - Up to TZS 50M</summary>
            Class5,
            /// <summary>Class 4 - Up to TZS 200M</summary>
            Class4,
            /// <summary>Class 3 - Up to TZS 500M</summary>
            Class3,
            /// <summary>Class 2 - Up to TZS 1B</summary>
            Class2,
            /// <summary>Class 1 - Unlimited</summary>
            Class1
        }

        /// <summary>
        /// Gets maximum contract value for CRB category (TZS)
        /// </summary>
        public static double GetMaximumContractValue(CRBCategory category)
        {
            return category switch
            {
                CRBCategory.Class7 => 2_000_000,
                CRBCategory.Class6 => 10_000_000,
                CRBCategory.Class5 => 50_000_000,
                CRBCategory.Class4 => 200_000_000,
                CRBCategory.Class3 => 500_000_000,
                CRBCategory.Class2 => 1_000_000_000,
                CRBCategory.Class1 => double.MaxValue,
                _ => 0
            };
        }

        #endregion
    }
}
