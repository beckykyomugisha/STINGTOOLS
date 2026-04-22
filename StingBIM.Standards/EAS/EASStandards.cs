using System;
using System.Collections.Generic;

namespace StingBIM.Standards.EAS
{
    /// <summary>
    /// East African Standards (EAS)
    /// Harmonized standards for East African Community (EAC) member states:
    /// Uganda, Kenya, Tanzania, Rwanda, Burundi, South Sudan, DR Congo
    /// 
    /// Developed by: East African Community Standardization, Quality Assurance,
    /// Metrology and Testing (EAC-SQMT)
    /// 
    /// These are harmonized standards adopted by all EAC member states to facilitate
    /// trade, ensure quality, and promote regional integration.
    /// </summary>
    public static class EASStandards
    {
        #region EAS 2 - Cement Specifications

        /// <summary>
        /// Cement types as per EAS 2
        /// </summary>
        public enum CementType
        {
            /// <summary>Ordinary Portland Cement (OPC) 32.5</summary>
            OPC_32_5,
            /// <summary>Ordinary Portland Cement (OPC) 42.5</summary>
            OPC_42_5,
            /// <summary>Ordinary Portland Cement (OPC) 52.5</summary>
            OPC_52_5,
            /// <summary>Pozzolana Portland Cement (PPC)</summary>
            PPC,
            /// <summary>Sulphate Resisting Cement (SRC)</summary>
            SRC,
            /// <summary>Low Heat Portland Cement</summary>
            LowHeat,
            /// <summary>Rapid Hardening Portland Cement</summary>
            RapidHardening
        }

        /// <summary>
        /// Gets the minimum compressive strength for cement type (MPa)
        /// </summary>
        public static (double At2Days, double At7Days, double At28Days) GetCementStrength(CementType type)
        {
            return type switch
            {
                CementType.OPC_32_5 => (0, 16, 32.5),
                CementType.OPC_42_5 => (10, 0, 42.5),
                CementType.OPC_52_5 => (20, 0, 52.5),
                CementType.PPC => (0, 16, 32.5),
                CementType.SRC => (0, 0, 42.5),
                CementType.LowHeat => (0, 0, 32.5),
                CementType.RapidHardening => (0, 27, 42.5),
                _ => (0, 0, 0)
            };
        }

        /// <summary>
        /// Maximum chloride content in cement (% by mass)
        /// </summary>
        public static double GetMaximumChlorideContent(CementType type)
        {
            return 0.1; // 0.1% maximum for all types per EAS 2
        }

        #endregion

        #region EAS 18 - Steel Reinforcement Bars

        /// <summary>
        /// Steel reinforcement grades as per EAS 18
        /// </summary>
        public enum ReinforcementGrade
        {
            /// <summary>Mild steel, Grade 250</summary>
            Grade250,
            /// <summary>High yield, Grade 460</summary>
            Grade460,
            /// <summary>High yield, Grade 500</summary>
            Grade500
        }

        /// <summary>
        /// Gets the characteristic strength for reinforcement grade (MPa)
        /// </summary>
        public static (double YieldStrength, double TensileStrength) GetReinforcementStrength(
            ReinforcementGrade grade)
        {
            return grade switch
            {
                ReinforcementGrade.Grade250 => (250, 410),
                ReinforcementGrade.Grade460 => (460, 540),
                ReinforcementGrade.Grade500 => (500, 580),
                _ => (0, 0)
            };
        }

        /// <summary>
        /// Standard bar sizes available in East Africa (mm)
        /// </summary>
        public static readonly int[] StandardBarSizes = new[]
        {
            6, 8, 10, 12, 16, 20, 25, 32, 40
        };

        /// <summary>
        /// Gets the cross-sectional area for a bar size (mm²)
        /// </summary>
        public static double GetBarArea(int diameter)
        {
            return Math.PI * diameter * diameter / 4.0;
        }

        /// <summary>
        /// Gets the mass per meter for a bar size (kg/m)
        /// </summary>
        public static double GetBarMass(int diameter)
        {
            double area = GetBarArea(diameter); // mm²
            double density = 7850; // kg/m³ for steel
            return area * density / 1000000; // kg/m
        }

        #endregion

        #region EAS 19 - Concrete Blocks

        /// <summary>
        /// Concrete block types as per EAS 19
        /// </summary>
        public enum BlockType
        {
            /// <summary>Solid load-bearing blocks</summary>
            SolidLoadBearing,
            /// <summary>Hollow load-bearing blocks</summary>
            HollowLoadBearing,
            /// <summary>Hollow non-load-bearing blocks</summary>
            HollowNonLoadBearing
        }

        /// <summary>
        /// Standard block sizes in East Africa (mm)
        /// Format: Length x Height x Width
        /// </summary>
        public static readonly Dictionary<string, (int Length, int Height, int Width)> StandardBlockSizes = 
            new Dictionary<string, (int, int, int)>
        {
            { "Standard", (450, 225, 225) },
            { "Half", (225, 225, 225) },
            { "Stretcher", (450, 225, 150) },
            { "Corner", (450, 225, 300) }
        };

        /// <summary>
        /// Gets minimum compressive strength for block type (MPa)
        /// </summary>
        public static double GetMinimumBlockStrength(BlockType type)
        {
            return type switch
            {
                BlockType.SolidLoadBearing => 3.5,
                BlockType.HollowLoadBearing => 2.8,
                BlockType.HollowNonLoadBearing => 2.0,
                _ => 0
            };
        }

        /// <summary>
        /// Gets water absorption limit for blocks (%)
        /// </summary>
        public static double GetMaximumWaterAbsorption(BlockType type)
        {
            return 12.0; // 12% maximum for all types per EAS 19
        }

        #endregion

        #region EAS 124 - Structural Steel Fabrication

        /// <summary>
        /// Structural steel grades commonly used in East Africa
        /// </summary>
        public enum StructuralSteelGrade
        {
            /// <summary>S275 - Yield strength 275 MPa</summary>
            S275,
            /// <summary>S355 - Yield strength 355 MPa</summary>
            S355,
            /// <summary>S450 - Yield strength 450 MPa</summary>
            S450
        }

        /// <summary>
        /// Gets the design strength for structural steel grade (MPa)
        /// </summary>
        public static double GetSteelDesignStrength(StructuralSteelGrade grade, double thickness)
        {
            if (grade == StructuralSteelGrade.S275)
            {
                if (thickness <= 16) return 275;
                if (thickness <= 40) return 265;
                return 255;
            }
            else if (grade == StructuralSteelGrade.S355)
            {
                if (thickness <= 16) return 355;
                if (thickness <= 40) return 345;
                return 335;
            }
            else if (grade == StructuralSteelGrade.S450)
            {
                if (thickness <= 16) return 450;
                if (thickness <= 40) return 430;
                return 410;
            }
            
            return 0;
        }

        /// <summary>
        /// Welding requirements as per EAS 124
        /// </summary>
        public static string[] GetWeldingRequirements()
        {
            return new[]
            {
                "Welders must be certified to ISO 9606",
                "Welding procedures must be qualified per ISO 15614",
                "Visual inspection required for all welds",
                "NDT testing for critical structural welds",
                "Minimum preheat temperature based on thickness and grade",
                "Post-weld heat treatment for high-strength steels"
            };
        }

        #endregion

        #region EAS 148 - Building Code Requirements

        /// <summary>
        /// Occupancy classifications per EAS 148
        /// </summary>
        public enum OccupancyClass
        {
            /// <summary>Single and multi-family dwellings</summary>
            Residential,
            /// <summary>Educational facilities</summary>
            Educational,
            /// <summary>Institutional facilities</summary>
            Institutional,
            /// <summary>Assembly buildings</summary>
            Assembly,
            /// <summary>Business and office</summary>
            Business,
            /// <summary>Mercantile and retail</summary>
            Mercantile,
            /// <summary>Industrial and manufacturing</summary>
            Industrial,
            /// <summary>Storage and warehouse</summary>
            Storage,
            /// <summary>High hazard occupancies</summary>
            HighHazard
        }

        /// <summary>
        /// Gets the minimum floor live load for occupancy type (kN/m²)
        /// </summary>
        public static double GetMinimumLiveLoad(OccupancyClass occupancy)
        {
            return occupancy switch
            {
                OccupancyClass.Residential => 1.5,
                OccupancyClass.Educational => 3.0,
                OccupancyClass.Institutional => 2.0,
                OccupancyClass.Assembly => 4.0,
                OccupancyClass.Business => 2.5,
                OccupancyClass.Mercantile => 4.0,
                OccupancyClass.Industrial => 5.0,
                OccupancyClass.Storage => 6.0,
                OccupancyClass.HighHazard => 6.0,
                _ => 0
            };
        }

        /// <summary>
        /// Gets minimum ceiling height requirement (m)
        /// </summary>
        public static double GetMinimumCeilingHeight(OccupancyClass occupancy)
        {
            return occupancy switch
            {
                OccupancyClass.Residential => 2.4,
                OccupancyClass.Educational => 3.0,
                OccupancyClass.Institutional => 2.7,
                OccupancyClass.Assembly => 3.6,
                OccupancyClass.Business => 2.7,
                OccupancyClass.Mercantile => 3.0,
                OccupancyClass.Industrial => 4.0,
                OccupancyClass.Storage => 4.0,
                OccupancyClass.HighHazard => 4.0,
                _ => 2.4
            };
        }

        #endregion

        #region EAS 268 - Water Supply Pipes (PVC)

        /// <summary>
        /// PVC pipe pressure classes per EAS 268
        /// </summary>
        public enum PVCPressureClass
        {
            /// <summary>Class 6 - 6 bar (0.6 MPa)</summary>
            Class6,
            /// <summary>Class 9 - 9 bar (0.9 MPa)</summary>
            Class9,
            /// <summary>Class 12 - 12 bar (1.2 MPa)</summary>
            Class12,
            /// <summary>Class 15 - 15 bar (1.5 MPa)</summary>
            Class15
        }

        /// <summary>
        /// Gets the working pressure for PVC pipe class (bar)
        /// </summary>
        public static double GetPVCWorkingPressure(PVCPressureClass pressureClass)
        {
            return pressureClass switch
            {
                PVCPressureClass.Class6 => 6.0,
                PVCPressureClass.Class9 => 9.0,
                PVCPressureClass.Class12 => 12.0,
                PVCPressureClass.Class15 => 15.0,
                _ => 0
            };
        }

        /// <summary>
        /// Standard PVC pipe sizes available in East Africa (mm nominal diameter)
        /// </summary>
        public static readonly int[] StandardPVCPipeSizes = new[]
        {
            20, 25, 32, 40, 50, 63, 75, 90, 110, 125, 140, 160, 200, 250, 315, 400, 500
        };

        #endregion

        #region EAS 287 - Electrical Cables and Wires

        /// <summary>
        /// Cable types per EAS 287
        /// </summary>
        public enum CableType
        {
            /// <summary>PVC insulated cable for fixed installations</summary>
            PVC_Fixed,
            /// <summary>PVC insulated flexible cable</summary>
            PVC_Flexible,
            /// <summary>XLPE insulated cable</summary>
            XLPE,
            /// <summary>Armoured cable</summary>
            Armoured
        }

        /// <summary>
        /// Standard cable conductor sizes in East Africa (mm²)
        /// </summary>
        public static readonly double[] StandardCableSizes = new[]
        {
            1.5, 2.5, 4.0, 6.0, 10.0, 16.0, 25.0, 35.0, 50.0, 70.0, 95.0, 120.0, 150.0, 185.0, 240.0, 300.0
        };

        /// <summary>
        /// Gets maximum operating temperature for cable type (°C)
        /// </summary>
        public static int GetMaximumOperatingTemperature(CableType type)
        {
            return type switch
            {
                CableType.PVC_Fixed => 70,
                CableType.PVC_Flexible => 70,
                CableType.XLPE => 90,
                CableType.Armoured => 70,
                _ => 60
            };
        }

        #endregion

        #region EAS 340 - Roofing Sheets

        /// <summary>
        /// Roofing sheet types per EAS 340
        /// </summary>
        public enum RoofingSheetType
        {
            /// <summary>Galvanized corrugated iron sheets</summary>
            GalvanizedIron,
            /// <summary>Pre-painted steel sheets</summary>
            PrePaintedSteel,
            /// <summary>Aluminium roofing sheets</summary>
            Aluminium,
            /// <summary>Fibre cement sheets</summary>
            FibreCement
        }

        /// <summary>
        /// Standard roofing sheet profiles available in East Africa
        /// </summary>
        public static readonly string[] StandardRoofingProfiles = new[]
        {
            "Box Profile 0.5mm",
            "Corrugated 0.5mm",
            "Tile Profile 0.5mm",
            "Longspan 0.7mm",
            "Standing Seam"
        };

        /// <summary>
        /// Gets minimum sheet thickness for type (mm)
        /// </summary>
        public static double GetMinimumSheetThickness(RoofingSheetType type)
        {
            return type switch
            {
                RoofingSheetType.GalvanizedIron => 0.4,
                RoofingSheetType.PrePaintedSteel => 0.4,
                RoofingSheetType.Aluminium => 0.5,
                RoofingSheetType.FibreCement => 5.0,
                _ => 0.4
            };
        }

        /// <summary>
        /// Gets the zinc coating weight for galvanized sheets (g/m²)
        /// </summary>
        public static double GetMinimumZincCoating(string profile)
        {
            return 275; // Z275 minimum per EAS 340
        }

        #endregion

        #region EAS 456 - Plumbing Fixtures

        /// <summary>
        /// Water efficiency ratings for plumbing fixtures
        /// </summary>
        public enum WaterEfficiencyRating
        {
            /// <summary>Standard efficiency</summary>
            Standard,
            /// <summary>High efficiency</summary>
            HighEfficiency,
            /// <summary>Ultra-high efficiency</summary>
            UltraHighEfficiency
        }

        /// <summary>
        /// Maximum flush volume for water closets (liters)
        /// </summary>
        public static double GetMaximumFlushVolume(WaterEfficiencyRating rating)
        {
            return rating switch
            {
                WaterEfficiencyRating.Standard => 9.0,
                WaterEfficiencyRating.HighEfficiency => 6.0,
                WaterEfficiencyRating.UltraHighEfficiency => 4.5,
                _ => 9.0
            };
        }

        /// <summary>
        /// Maximum flow rate for taps and showers (liters/minute)
        /// </summary>
        public static double GetMaximumFlowRate(string fixtureType, WaterEfficiencyRating rating)
        {
            if (fixtureType.ToLower().Contains("shower"))
            {
                return rating switch
                {
                    WaterEfficiencyRating.Standard => 12.0,
                    WaterEfficiencyRating.HighEfficiency => 9.0,
                    WaterEfficiencyRating.UltraHighEfficiency => 7.5,
                    _ => 12.0
                };
            }
            else // Taps
            {
                return rating switch
                {
                    WaterEfficiencyRating.Standard => 8.0,
                    WaterEfficiencyRating.HighEfficiency => 6.0,
                    WaterEfficiencyRating.UltraHighEfficiency => 4.5,
                    _ => 8.0
                };
            }
        }

        #endregion

        #region EAS 680 - Fire Safety Requirements

        /// <summary>
        /// Fire resistance ratings required per EAS 680
        /// </summary>
        public enum FireResistanceRating
        {
            /// <summary>30 minutes fire resistance</summary>
            FRR_30,
            /// <summary>60 minutes fire resistance</summary>
            FRR_60,
            /// <summary>90 minutes fire resistance</summary>
            FRR_90,
            /// <summary>120 minutes fire resistance</summary>
            FRR_120,
            /// <summary>180 minutes fire resistance</summary>
            FRR_180
        }

        /// <summary>
        /// Gets required fire resistance rating based on building height and occupancy
        /// </summary>
        public static FireResistanceRating GetRequiredFireRating(
            double buildingHeight, 
            OccupancyClass occupancy)
        {
            if (buildingHeight > 45)
                return FireResistanceRating.FRR_120;
            else if (buildingHeight > 28)
                return FireResistanceRating.FRR_90;
            else if (buildingHeight > 15)
                return FireResistanceRating.FRR_60;
            else if (occupancy == OccupancyClass.Assembly || 
                     occupancy == OccupancyClass.HighHazard)
                return FireResistanceRating.FRR_60;
            else
                return FireResistanceRating.FRR_30;
        }

        /// <summary>
        /// Maximum travel distance to exit (m)
        /// </summary>
        public static double GetMaximumTravelDistance(OccupancyClass occupancy, bool sprinklered)
        {
            double baseDistance = occupancy switch
            {
                OccupancyClass.Residential => 30,
                OccupancyClass.Educational => 35,
                OccupancyClass.Assembly => 25,
                OccupancyClass.Business => 40,
                OccupancyClass.Industrial => 35,
                OccupancyClass.HighHazard => 20,
                _ => 30
            };

            // Increase by 25% if sprinklered
            return sprinklered ? baseDistance * 1.25 : baseDistance;
        }

        /// <summary>
        /// Minimum exit width per person (mm)
        /// </summary>
        public static int GetMinimumExitWidth()
        {
            return 10; // 10mm per person as per EAS 680
        }

        #endregion

        #region EAC Regional Requirements

        /// <summary>
        /// Gets the EAC member states that have adopted this standard
        /// </summary>
        public static string[] GetEACMemberStates()
        {
            return new[]
            {
                "Uganda",
                "Kenya",
                "Tanzania",
                "Rwanda",
                "Burundi",
                "South Sudan",
                "DR Congo (observer status)"
            };
        }

        /// <summary>
        /// Validates if a product meets EAS certification requirements
        /// </summary>
        public static (bool IsCompliant, List<string> NonCompliantItems) ValidateEASCompliance(
            string productType,
            Dictionary<string, object> specifications)
        {
            var nonCompliant = new List<string>();
            bool isCompliant = true;

            // Example validation logic (would be expanded based on product type)
            if (productType == "Cement" && specifications.ContainsKey("ChlorideContent"))
            {
                double chloride = Convert.ToDouble(specifications["ChlorideContent"]);
                if (chloride > 0.1)
                {
                    nonCompliant.Add($"Chloride content {chloride}% exceeds maximum 0.1%");
                    isCompliant = false;
                }
            }

            if (productType == "ConcreteBlock" && specifications.ContainsKey("WaterAbsorption"))
            {
                double absorption = Convert.ToDouble(specifications["WaterAbsorption"]);
                if (absorption > 12.0)
                {
                    nonCompliant.Add($"Water absorption {absorption}% exceeds maximum 12%");
                    isCompliant = false;
                }
            }

            return (isCompliant, nonCompliant);
        }

        #endregion
    }
}
