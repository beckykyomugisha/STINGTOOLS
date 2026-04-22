using System;
using System.Collections.Generic;

namespace StingBIM.Standards.IMC2021
{
    /// <summary>
    /// IMC 2021 - International Mechanical Code
    /// Published by: International Code Council (ICC)
    /// Adoption: Used in Liberia and many U.S. jurisdictions
    /// 
    /// Provides minimum regulations for mechanical systems including
    /// HVAC, ventilation, exhaust systems, duct construction, and
    /// refrigeration systems.
    /// </summary>
    public static class IMC2021Standards
    {
        #region General Requirements (Chapter 3)

        /// <summary>
        /// Minimum ventilation rates for occupancies (cfm per person)
        /// </summary>
        public static double GetMinimumVentilationRate(string occupancyType)
        {
            return occupancyType.ToLower() switch
            {
                // Residential
                var s when s.Contains("dwelling") => 15,
                var s when s.Contains("bedroom") => 5,
                
                // Educational
                var s when s.Contains("classroom") => 15,
                var s when s.Contains("laboratory") => 20,
                
                // Office
                var s when s.Contains("office") => 5,
                var s when s.Contains("conference") => 20,
                
                // Assembly
                var s when s.Contains("auditorium") => 5,
                var s when s.Contains("theater") => 5,
                
                // Retail
                var s when s.Contains("retail") => 7.5,
                var s when s.Contains("sales") => 7.5,
                
                // Healthcare
                var s when s.Contains("patient room") => 25,
                var s when s.Contains("operating room") => 30,
                
                _ => 15 // Default
            };
        }

        /// <summary>
        /// Minimum outdoor air requirements (cfm/sq ft of floor area)
        /// </summary>
        public static double GetMinimumOutdoorAir(string spaceType)
        {
            return spaceType.ToLower() switch
            {
                var s when s.Contains("warehouse") => 0.06,
                var s when s.Contains("storage") => 0.06,
                var s when s.Contains("office") => 0.06,
                var s when s.Contains("retail") => 0.12,
                var s when s.Contains("restaurant") => 0.18,
                var s when s.Contains("kitchen") => 0.70,
                _ => 0.06
            };
        }

        #endregion

        #region Ventilation (Chapter 4)

        /// <summary>
        /// Natural ventilation opening requirements
        /// </summary>
        public static double GetNaturalVentilationArea(double floorArea, string climateZone)
        {
            // Minimum 4% of floor area for natural ventilation
            double minArea = floorArea * 0.04;
            
            // Tropical climate (Liberia) - increase for better air circulation
            if (climateZone.ToLower().Contains("tropical"))
            {
                minArea = floorArea * 0.05; // 5% for tropical
            }
            
            return minArea;
        }

        /// <summary>
        /// Exhaust ventilation rates for specific rooms (cfm)
        /// </summary>
        public static int GetExhaustRate(string roomType)
        {
            return roomType.ToLower() switch
            {
                "bathroom" => 50,
                "toilet room" => 50,
                "kitchen" => 100,
                "laundry" => 100,
                "garage" => 100,
                _ => 50
            };
        }

        /// <summary>
        /// Commercial kitchen hood requirements
        /// </summary>
        public static (double MinCFMPerFoot, string HoodType) GetKitchenHoodRequirements(
            string applianceType)
        {
            return applianceType.ToLower() switch
            {
                var s when s.Contains("heavy duty") => (400, "Type I - Grease"),
                var s when s.Contains("medium duty") => (300, "Type I - Grease"),
                var s when s.Contains("light duty") => (200, "Type I - Grease"),
                var s when s.Contains("oven") => (150, "Type II - Heat"),
                var s when s.Contains("steamer") => (500, "Type II - Condensate"),
                _ => (200, "Type I - Grease")
            };
        }

        #endregion

        #region Exhaust Systems (Chapter 5)

        /// <summary>
        /// Minimum duct velocities for different exhaust systems (fpm)
        /// </summary>
        public static (int Minimum, int Maximum) GetDuctVelocity(string systemType)
        {
            return systemType.ToLower() switch
            {
                "residential supply" => (400, 900),
                "residential return" => (300, 700),
                "commercial supply" => (600, 2000),
                "commercial return" => (500, 1500),
                "kitchen exhaust" => (500, 2000),
                "bathroom exhaust" => (500, 1500),
                _ => (500, 1500)
            };
        }

        /// <summary>
        /// Hazardous exhaust system requirements
        /// </summary>
        public static string[] GetHazardousExhaustRequirements(string hazardClass)
        {
            return hazardClass.ToLower() switch
            {
                "flammable" => new[]
                {
                    "Dedicated exhaust system required",
                    "No mixing with other exhaust streams",
                    "Explosion-proof fan and motor",
                    "Bonding and grounding of ductwork",
                    "Minimum duct velocity 1,500 fpm"
                },
                "corrosive" => new[]
                {
                    "Corrosion-resistant ductwork and fan",
                    "No aluminum or galvanized in contact with vapors",
                    "Provide emergency exhaust activation",
                    "Minimum duct velocity 1,000 fpm"
                },
                "toxic" => new[]
                {
                    "Dedicated exhaust system",
                    "Discharge above roof level",
                    "No recirculation permitted",
                    "Emergency power backup",
                    "Minimum duct velocity 2,000 fpm"
                },
                _ => new[]
                {
                    "Follow general exhaust requirements",
                    "Provide adequate ventilation"
                }
            };
        }

        #endregion

        #region Duct Systems (Chapter 6)

        /// <summary>
        /// Minimum duct insulation R-values
        /// </summary>
        public static double GetMinimumDuctInsulation(
            string ductLocation,
            string climateZone)
        {
            // Liberia is tropical - focus on preventing heat gain
            if (ductLocation.ToLower().Contains("unconditioned") ||
                ductLocation.ToLower().Contains("attic") ||
                ductLocation.ToLower().Contains("exterior"))
            {
                if (climateZone.ToLower().Contains("tropical"))
                    return 6.0; // R-6 for tropical unconditioned spaces
                return 8.0; // R-8 for other climates
            }
            
            return 3.5; // R-3.5 for conditioned spaces
        }

        /// <summary>
        /// Maximum duct leakage rates (cfm per 100 sq ft at 1" w.g.)
        /// </summary>
        public static double GetMaximumDuctLeakage(string ductClass)
        {
            return ductClass.ToLower() switch
            {
                "supply" => 4.0,
                "return" => 6.0,
                "exhaust" => 6.0,
                _ => 6.0
            };
        }

        /// <summary>
        /// Duct construction standards by pressure class
        /// </summary>
        public static string GetDuctConstruction(double staticPressure)
        {
            if (staticPressure <= 2.0)
                return "Spiral seam or equivalent - 26 gauge minimum";
            else if (staticPressure <= 3.0)
                return "Spiral seam or welded - 24 gauge minimum";
            else if (staticPressure <= 6.0)
                return "Welded construction - 22 gauge minimum";
            else
                return "Welded construction - 20 gauge minimum, special design required";
        }

        #endregion

        #region Combustion Air (Chapter 7)

        /// <summary>
        /// Combustion air requirements for fuel-burning appliances (cu in per 1000 Btu/h)
        /// </summary>
        public static double GetCombustionAirOpening(
            string installationLocation,
            double totalBtuInput)
        {
            double openingArea;
            
            if (installationLocation.ToLower().Contains("confined"))
            {
                // Confined space - two permanent openings required
                // 1 sq in per 1,000 Btu/h if openings are vertical
                openingArea = totalBtuInput / 1000.0; // sq inches
            }
            else
            {
                // Unconfined space - 50 cu ft per 1,000 Btu/h
                // Or openings per confined space requirements
                openingArea = totalBtuInput / 1000.0; // sq inches
            }
            
            return openingArea;
        }

        /// <summary>
        /// Chimney and vent connector clearances (inches)
        /// </summary>
        public static int GetMinimumClearance(string connectorType, bool combustibleMaterial)
        {
            if (!combustibleMaterial) return 0; // No clearance to non-combustibles
            
            return connectorType.ToLower() switch
            {
                "single wall metal" => 18,
                "type b vent" => 1,
                "type l vent" => 1,
                "masonry chimney" => 2,
                _ => 18
            };
        }

        #endregion

        #region Hydronic Systems (Chapter 12)

        /// <summary>
        /// Minimum pipe insulation for hydronic piping
        /// </summary>
        public static double GetHydronicPipeInsulation(
            double pipeSize,
            double fluidTemperature,
            string location)
        {
            // For Liberia (tropical climate) - focus on chilled water systems
            if (fluidTemperature < 60) // Chilled water
            {
                if (pipeSize <= 1.5)
                    return 0.5; // 1/2" insulation
                else if (pipeSize <= 4.0)
                    return 1.0; // 1" insulation
                else
                    return 1.5; // 1-1/2" insulation
            }
            else if (fluidTemperature > 140) // Hot water/steam
            {
                if (pipeSize <= 1.5)
                    return 1.0;
                else if (pipeSize <= 4.0)
                    return 1.5;
                else
                    return 2.0;
            }
            
            return 0.5; // Minimum for other systems
        }

        /// <summary>
        /// Expansion tank sizing for closed loop systems
        /// </summary>
        public static double GetExpansionTankSize(
            double systemVolume,
            double maxTemp,
            double fillTemp)
        {
            // Simplified calculation
            double tempRise = maxTemp - fillTemp;
            double expansionFactor = tempRise * 0.0002; // Approximate for water
            double expansionVolume = systemVolume * expansionFactor;
            
            // Tank should be 1.5x expansion volume
            return expansionVolume * 1.5;
        }

        #endregion

        #region Refrigeration (Chapter 11)

        /// <summary>
        /// Minimum refrigeration machinery room ventilation (cfm)
        /// </summary>
        public static double GetMachineryRoomVentilation(double refrigerantPounds)
        {
            // Based on refrigerant quantity
            if (refrigerantPounds <= 6.6)
                return 0; // No mechanical ventilation required
            else
                return refrigerantPounds * 100; // 100 cfm per lb of refrigerant
        }

        /// <summary>
        /// Refrigerant detector requirements
        /// </summary>
        public static bool RequiresRefrigerantDetector(
            string refrigerantType,
            double refrigerantPounds,
            string occupancyType)
        {
            // Group A1 refrigerants (R-134a, R-410A, etc.) - lower toxicity
            if (refrigerantType.Contains("A1"))
            {
                if (refrigerantPounds > 26) return true;
            }
            
            // Group A2 or higher - toxic or flammable
            if (refrigerantType.Contains("A2") || 
                refrigerantType.Contains("A3") ||
                refrigerantType.Contains("B"))
            {
                if (refrigerantPounds > 6.6) return true;
            }
            
            // Institutional occupancies - stricter
            if (occupancyType.ToLower().Contains("hospital") ||
                occupancyType.ToLower().Contains("school"))
            {
                if (refrigerantPounds > 6.6) return true;
            }
            
            return false;
        }

        #endregion

        #region Liberia-Specific Requirements

        /// <summary>
        /// Gets climate-adjusted ventilation for Liberia (tropical)
        /// </summary>
        public static double GetTropicalVentilationAdjustment(double baseVentilation)
        {
            // Increase ventilation by 25% for tropical humid climate
            return baseVentilation * 1.25;
        }

        /// <summary>
        /// Humidity control requirements for tropical climate
        /// </summary>
        public static string[] GetHumidityControlRequirements()
        {
            return new[]
            {
                "Maintain indoor relative humidity 40-60%",
                "Provide adequate dehumidification capacity",
                "Install moisture barriers in building envelope",
                "Ensure positive building pressurization",
                "Provide adequate condensate drainage"
            };
        }

        /// <summary>
        /// Generator ventilation for backup power systems
        /// </summary>
        public static double GetGeneratorVentilation(double generatorKW)
        {
            // Liberia frequently experiences power outages
            // Generator rooms need substantial ventilation
            double cfmPerKW = 200; // 200 cfm per kW for diesel generators
            return generatorKW * cfmPerKW;
        }

        #endregion
    }
}
