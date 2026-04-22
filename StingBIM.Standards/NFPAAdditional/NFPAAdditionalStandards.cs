using System;
using System.Collections.Generic;

namespace StingBIM.Standards.NFPAAdditional
{
    /// <summary>
    /// Additional NFPA Standards for Fire Protection and Life Safety
    /// Complements base NFPA with comprehensive fire codes
    /// 
    /// Standards covered:
    /// - NFPA 101: Life Safety Code
    /// - NFPA 20: Standard for Installation of Stationary Pumps for Fire Protection
    /// - NFPA 24: Standard for Installation of Private Fire Service Mains
    /// - NFPA 30: Flammable and Combustible Liquids Code
    /// - NFPA 70: National Electrical Code (NEC) - Fire safety aspects
    /// </summary>
    public static class NFPAAdditionalStandards
    {
        #region NFPA 101 - Life Safety Code

        /// <summary>
        /// NFPA 101 occupancy classifications
        /// </summary>
        public enum OccupancyClassification
        {
            /// <summary>Assembly - Theaters, restaurants, churches</summary>
            Assembly,
            /// <summary>Educational - Schools, day care</summary>
            Educational,
            /// <summary>Day care</summary>
            DayCare,
            /// <summary>Health care - Hospitals, nursing homes</summary>
            HealthCare,
            /// <summary>Ambulatory health care - Clinics</summary>
            AmbulatoryHealthCare,
            /// <summary>Detention and correctional</summary>
            DetentionCorrectional,
            /// <summary>Residential - Hotels, apartments, dormitories</summary>
            Residential,
            /// <summary>Mercantile - Shops, malls</summary>
            Mercantile,
            /// <summary>Business - Offices</summary>
            Business,
            /// <summary>Industrial</summary>
            Industrial,
            /// <summary>Storage</summary>
            Storage
        }

        /// <summary>
        /// Gets required means of egress per NFPA 101
        /// </summary>
        public static int GetRequiredExits(int occupantLoad)
        {
            if (occupantLoad <= 500) return 2;
            else if (occupantLoad <= 1000) return 3;
            else return 4;
        }

        /// <summary>
        /// Gets exit capacity requirements
        /// </summary>
        public static class ExitCapacity
        {
            /// <summary>
            /// Capacity factor for doors and corridors (persons per unit width)
            /// </summary>
            public static double GetCapacityFactor(
                OccupancyClassification occupancy,
                bool isStair)
            {
                if (isStair)
                {
                    // Stairs: 0.3 inches per person
                    // Convert to mm: 7.6mm per person
                    return occupancy switch
                    {
                        OccupancyClassification.HealthCare => 5.0, // More conservative
                        _ => 7.6
                    };
                }
                else
                {
                    // Doors and level components: 0.2 inches per person
                    // Convert to mm: 5mm per person
                    return occupancy switch
                    {
                        OccupancyClassification.HealthCare => 3.8,
                        _ => 5.0
                    };
                }
            }

            /// <summary>
            /// Calculates minimum exit width (mm)
            /// </summary>
            public static double CalculateMinimumExitWidth(
                int occupantLoad,
                OccupancyClassification occupancy,
                bool isStair)
            {
                double factor = GetCapacityFactor(occupancy, isStair);
                double calculatedWidth = occupantLoad * factor;
                
                // Minimum widths per NFPA 101
                double minimumWidth = isStair ? 1120 : 815; // mm
                
                return Math.Max(calculatedWidth, minimumWidth);
            }
        }

        /// <summary>
        /// Travel distance limits per NFPA 101
        /// </summary>
        public static double GetMaximumTravelDistance(
            OccupancyClassification occupancy,
            bool sprinklered)
        {
            // Returns maximum travel distance in meters
            var baseDistance = occupancy switch
            {
                OccupancyClassification.Assembly => 60.0,
                OccupancyClassification.Educational => 60.0,
                OccupancyClassification.HealthCare => 45.0,
                OccupancyClassification.Residential => 45.0,
                OccupancyClassification.Business => 60.0,
                OccupancyClassification.Mercantile => 45.0,
                OccupancyClassification.Industrial => 60.0,
                OccupancyClassification.Storage => 60.0,
                _ => 60.0
            };

            // Increase allowed distance if sprinklered
            return sprinklered ? baseDistance * 1.5 : baseDistance;
        }

        /// <summary>
        /// Emergency lighting and exit signs requirements
        /// </summary>
        public static class EmergencyLighting
        {
            /// <summary>Minimum illumination level (lux)</summary>
            public const double MinimumIllumination = 10.0;

            /// <summary>Minimum battery backup duration (minutes)</summary>
            public const int MinimumBackupDuration = 90;

            /// <summary>Maximum spacing for exit signs (meters)</summary>
            public const double MaximumExitSignSpacing = 30.0;

            /// <summary>
            /// Exit sign requirements
            /// </summary>
            public static string[] GetExitSignRequirements()
            {
                return new[]
                {
                    "Minimum letter height: 150mm (6 inches)",
                    "Illuminated internally or externally",
                    "Visible from all directions of egress",
                    "Mounted above door or along path of egress",
                    "Battery backup for 90 minutes minimum",
                    "Self-testing capability recommended"
                };
            }
        }

        #endregion

        #region NFPA 20 - Fire Pumps

        /// <summary>
        /// Fire pump types per NFPA 20
        /// </summary>
        public enum FirePumpType
        {
            /// <summary>Horizontal split case</summary>
            HorizontalSplitCase,
            /// <summary>Vertical turbine</summary>
            VerticalTurbine,
            /// <summary>Vertical inline</summary>
            VerticalInline,
            /// <summary>End suction</summary>
            EndSuction
        }

        /// <summary>
        /// Fire pump driver types
        /// </summary>
        public enum FirePumpDriver
        {
            /// <summary>Electric motor</summary>
            ElectricMotor,
            /// <summary>Diesel engine</summary>
            DieselEngine
        }

        /// <summary>
        /// Fire pump sizing and performance
        /// </summary>
        public static class FirePumpDesign
        {
            /// <summary>
            /// Gets minimum rated capacity (L/min)
            /// </summary>
            public static double GetMinimumCapacity()
            {
                return 380; // 380 L/min (100 gpm) minimum per NFPA 20
            }

            /// <summary>
            /// Gets rated pressure at various flow percentages
            /// </summary>
            public static (double Pressure140, double Pressure100, double Pressure65) 
                GetPressureRequirements(double ratedPressure)
            {
                // Rated pressure at 100% flow
                double pressure100 = ratedPressure;
                
                // Pressure at 140% flow (run-out condition)
                double pressure140 = ratedPressure * 0.65; // Should not exceed 65% of rated
                
                // Pressure at 65% flow (churn/shut-off)
                double pressure65 = ratedPressure * 1.40; // Should not exceed 140% of rated
                
                return (pressure140, pressure100, pressure65);
            }

            /// <summary>
            /// Weekly test requirements
            /// </summary>
            public static string[] GetWeeklyTestRequirements()
            {
                return new[]
                {
                    "Run pump for minimum 10 minutes",
                    "Check discharge pressure",
                    "Check suction pressure",
                    "Verify pump starts automatically",
                    "Check for leaks and unusual noise",
                    "Record test results",
                    "For diesel: Run at no-load conditions"
                };
            }

            /// <summary>
            /// Annual flow test requirements
            /// </summary>
            public static string[] GetAnnualFlowTestRequirements()
            {
                return new[]
                {
                    "Full flow test at 100%, 150% rated capacity",
                    "Verify pressure at each flow point",
                    "Plot pump curve",
                    "Compare with original performance curve",
                    "Check for cavitation",
                    "Inspect wearing rings (if accessible)",
                    "Professional testing company recommended"
                };
            }
        }

        /// <summary>
        /// Diesel engine requirements for fire pumps
        /// </summary>
        public static class DieselEngineRequirements
        {
            /// <summary>Minimum fuel tank capacity</summary>
            public static string FuelTankCapacity = "1 gallon per horsepower + 5% volume + 5 gallons";

            /// <summary>
            /// Gets fuel consumption estimate
            /// </summary>
            public static double GetFuelConsumption(double horsePower)
            {
                // Approximate: 0.04 gallons per HP per hour at full load
                return horsePower * 0.04;
            }

            /// <summary>
            /// Battery requirements
            /// </summary>
            public static string[] GetBatteryRequirements()
            {
                return new[]
                {
                    "Minimum two batteries",
                    "Battery charger with automatic/manual mode",
                    "Pilot light to indicate charger operation",
                    "Low battery alarm",
                    "Battery disconnecting means"
                };
            }
        }

        #endregion

        #region NFPA 24 - Private Fire Service Mains

        /// <summary>
        /// Underground fire main pipe materials
        /// </summary>
        public static readonly string[] ApprovedPipeMaterials = new[]
        {
            "Ductile iron pipe",
            "Steel pipe",
            "Cement-lined ductile iron",
            "Concrete pressure pipe",
            "HDPE pipe (specific applications)"
        };

        /// <summary>
        /// Minimum pipe sizes for fire mains
        /// </summary>
        public static class FireMainSizing
        {
            /// <summary>
            /// Gets minimum pipe size (mm) based on flow
            /// </summary>
            public static int GetMinimumPipeSize(double flowLPM)
            {
                if (flowLPM <= 1900) return 100;      // 4 inches
                else if (flowLPM <= 3800) return 150; // 6 inches
                else if (flowLPM <= 7600) return 200; // 8 inches
                else if (flowLPM <= 11400) return 250; // 10 inches
                else if (flowLPM <= 17000) return 300; // 12 inches
                else return 400; // 16 inches for higher flows
            }

            /// <summary>Minimum pipe size for fire main system</summary>
            public const int MinimumFireMainSize = 150; // 150mm (6 inches)
        }

        /// <summary>
        /// Fire hydrant requirements
        /// </summary>
        public static class FireHydrants
        {
            /// <summary>
            /// Maximum spacing between fire hydrants (meters)
            /// </summary>
            public static double GetMaximumHydrantSpacing(string occupancy)
            {
                return occupancy.ToLower() switch
                {
                    "high hazard" => 60,    // 200 ft
                    "commercial" => 90,      // 300 ft
                    "residential" => 120,    // 400 ft
                    _ => 90
                };
            }

            /// <summary>
            /// Hydrant outlet configuration
            /// </summary>
            public static string[] GetHydrantOutlets()
            {
                return new[]
                {
                    "One 100mm (4 inch) pumper connection",
                    "Two 65mm (2.5 inch) hose connections"
                };
            }

            /// <summary>
            /// Hydrant color coding (typical)
            /// </summary>
            public static string GetHydrantColorCode(double flowLPM)
            {
                if (flowLPM < 1900) return "Red - <500 gpm";
                else if (flowLPM < 3800) return "Orange - 500-999 gpm";
                else if (flowLPM < 5700) return "Green - 1000-1499 gpm";
                else return "Blue - 1500+ gpm";
            }
        }

        #endregion

        #region NFPA 30 - Flammable and Combustible Liquids

        /// <summary>
        /// Flammable and combustible liquid classifications
        /// </summary>
        public enum LiquidClassification
        {
            /// <summary>Class IA - Flash point < 22.8°C (73°F), boiling < 37.8°C (100°F)</summary>
            ClassIA,
            /// <summary>Class IB - Flash point < 22.8°C (73°F), boiling ≥ 37.8°C (100°F)</summary>
            ClassIB,
            /// <summary>Class IC - Flash point ≥ 22.8°C and < 37.8°C (73-100°F)</summary>
            ClassIC,
            /// <summary>Class II - Flash point ≥ 37.8°C and < 60°C (100-140°F)</summary>
            ClassII,
            /// <summary>Class IIIA - Flash point ≥ 60°C and < 93°C (140-200°F)</summary>
            ClassIIIA,
            /// <summary>Class IIIB - Flash point ≥ 93°C (200°F)</summary>
            ClassIIIB
        }

        /// <summary>
        /// Storage cabinet requirements for flammable liquids
        /// </summary>
        public static class FlammableStorageCabinets
        {
            /// <summary>
            /// Maximum capacity for storage cabinets (liters)
            /// </summary>
            public static double GetMaximumCapacity(LiquidClassification classification)
            {
                if (classification <= LiquidClassification.ClassIC)
                    return 227; // 60 gallons Class I
                else
                    return 454; // 120 gallons Class II/III
            }

            /// <summary>
            /// Cabinet construction requirements
            /// </summary>
            public static string[] GetCabinetRequirements()
            {
                return new[]
                {
                    "Double-wall 18-gauge steel construction",
                    "1.5 inch (38mm) air space between walls",
                    "Three-point locking system",
                    "2-inch (50mm) leak-proof sill",
                    "Self-closing doors",
                    "Conspicuous labeling: FLAMMABLE - KEEP FIRE AWAY",
                    "Venting only if required by other regulations"
                };
            }

            /// <summary>
            /// Maximum number of cabinets per fire area
            /// </summary>
            public const int MaximumCabinetsPerFireArea = 3;
        }

        /// <summary>
        /// Tank storage requirements
        /// </summary>
        public static class TankStorage
        {
            /// <summary>
            /// Underground tank requirements
            /// </summary>
            public static string[] GetUndergroundTankRequirements()
            {
                return new[]
                {
                    "Corrosion protection (cathodic protection or coatings)",
                    "Leak detection system",
                    "Overfill protection",
                    "Spill containment",
                    "Minimum 1-meter (3 ft) depth of cover",
                    "Tank testing before installation",
                    "Inventory reconciliation records",
                    "Registration with environmental authority"
                };
            }

            /// <summary>
            /// Aboveground tank requirements
            /// </summary>
            public static string[] GetAbovegroundTankRequirements(double capacityLiters)
            {
                var requirements = new List<string>
                {
                    "Secondary containment (110% of largest tank)",
                    "Impervious containment floor",
                    "Emergency venting",
                    "Overfill protection",
                    "Grounding and bonding",
                    "Flame arresters on vents",
                    "Tank gauging system"
                };

                if (capacityLiters > 25000) // > 6,600 gallons
                {
                    requirements.AddRange(new[]
                    {
                        "Automatic fire suppression system",
                        "Fire water supply dedicated to tank area",
                        "Remote emergency shutoff",
                        "Dike/berm containment"
                    });
                }

                return requirements.ToArray();
            }

            /// <summary>
            /// Separation distances for outdoor tanks (meters)
            /// </summary>
            public static double GetSeparationDistance(
                double tankCapacityLiters,
                string adjacentTo)
            {
                double baseSeparation = adjacentTo.ToLower() switch
                {
                    "property line" => 15,
                    "building" => 10,
                    "other tank" => tankCapacityLiters < 5000 ? 1 : 3,
                    _ => 10
                };

                return baseSeparation;
            }
        }

        /// <summary>
        /// Dispensing and handling requirements
        /// </summary>
        public static class DispensingHandling
        {
            /// <summary>
            /// Bonding and grounding requirements
            /// </summary>
            public static string[] GetBondingGroundingRequirements()
            {
                return new[]
                {
                    "Bond container to container during transfer",
                    "Ground container being filled",
                    "Use approved bonding/grounding equipment",
                    "Verify continuity before transfer",
                    "Maintain bonding throughout transfer",
                    "Maximum resistance: 10 ohms"
                };
            }

            /// <summary>
            /// Spill control and containment
            /// </summary>
            public static string[] GetSpillControlRequirements()
            {
                return new[]
                {
                    "Spill containment pallets for containers",
                    "Absorbent materials readily available",
                    "Spill response kit accessible",
                    "Trained personnel for spill response",
                    "Emergency contact information posted",
                    "Spill reporting procedures documented"
                };
            }
        }

        #endregion

        #region Fire Protection System Integration

        /// <summary>
        /// Integrating NFPA fire protection systems
        /// </summary>
        public static class SystemIntegration
        {
            /// <summary>
            /// Gets comprehensive fire protection strategy
            /// </summary>
            public static string[] GetIntegratedFireProtectionStrategy(
                string buildingType,
                double areaM2,
                int floors)
            {
                var systems = new List<string>
                {
                    "Fire detection and alarm system (NFPA 72)",
                    "Portable fire extinguishers (NFPA 10)",
                    "Emergency lighting and exit signs (NFPA 101)",
                    "Fire-resistant construction (NFPA 5000/IBC)"
                };

                if (areaM2 > 500 || floors > 2)
                {
                    systems.Add("Automatic sprinkler system (NFPA 13)");
                    systems.Add("Standpipe system (NFPA 14)");
                }

                if (floors > 5 || buildingType.ToLower().Contains("high-rise"))
                {
                    systems.Add("Fire pump (NFPA 20)");
                    systems.Add("Emergency voice communication system");
                    systems.Add("Smoke control system (NFPA 92)");
                    systems.Add("Fire department connections (NFPA 14)");
                }

                if (buildingType.ToLower().Contains("kitchen") ||
                    buildingType.ToLower().Contains("restaurant"))
                {
                    systems.Add("Kitchen hood suppression system (NFPA 96)");
                }

                if (buildingType.ToLower().Contains("data") ||
                    buildingType.ToLower().Contains("server"))
                {
                    systems.Add("Clean agent fire suppression (NFPA 2001)");
                }

                return systems.ToArray();
            }

            /// <summary>
            /// Commissioning requirements for fire protection systems
            /// </summary>
            public static string[] GetCommissioningRequirements()
            {
                return new[]
                {
                    "Factory acceptance testing (FAT) of equipment",
                    "Installation verification per NFPA standards",
                    "Functional testing of all components",
                    "Integration testing of multiple systems",
                    "Performance testing under design conditions",
                    "Training of building staff",
                    "As-built documentation",
                    "Operation and maintenance manuals",
                    "Authority having jurisdiction (AHJ) approval",
                    "Certificate of occupancy issuance"
                };
            }

            /// <summary>
            /// Ongoing maintenance and testing schedule
            /// </summary>
            public static Dictionary<string, string> GetMaintenanceSchedule()
            {
                return new Dictionary<string, string>
                {
                    { "Fire extinguishers", "Monthly visual, annual servicing" },
                    { "Fire alarm system", "Weekly test, quarterly inspection, annual testing" },
                    { "Sprinkler system", "Quarterly inspection, annual testing, 5-year internal inspection" },
                    { "Fire pump", "Weekly run test, annual flow test" },
                    { "Standpipes", "Annual flow test" },
                    { "Emergency lighting", "Monthly 30-sec test, annual 90-min test" },
                    { "Fire doors", "Annual inspection and testing" },
                    { "Kitchen suppression", "Semi-annual inspection and testing" }
                };
            }
        }

        #endregion
    }
}
