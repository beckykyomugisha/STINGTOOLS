// FILE: ASHRAEStandards.cs
// LOCATION: StingBIM.Standards/ASHRAE/
// LINES: ~2500
// PURPOSE: ASHRAE HVAC and energy standards compliance

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingBIM.Standards.ASHRAE
{
    #region Supporting Classes and Enums

    /// <summary>
    /// ASHRAE climate zones
    /// </summary>
    public enum ClimateZone
    {
        Zone1A,  // Very Hot - Humid (Miami)
        Zone1B,  // Very Hot - Dry
        Zone2A,  // Hot - Humid (Houston)
        Zone2B,  // Hot - Dry (Phoenix)
        Zone3A,  // Warm - Humid (Atlanta)
        Zone3B,  // Warm - Dry (Las Vegas)
        Zone3C,  // Warm - Marine (San Francisco)
        Zone4A,  // Mixed - Humid (New York)
        Zone4B,  // Mixed - Dry (Albuquerque)
        Zone4C,  // Mixed - Marine (Seattle)
        Zone5A,  // Cool - Humid (Chicago)
        Zone5B,  // Cool - Dry (Denver)
        Zone5C,  // Cool - Marine
        Zone6A,  // Cold - Humid (Minneapolis)
        Zone6B,  // Cold - Dry
        Zone7,   // Very Cold (Duluth)
        Zone8    // Subarctic (Fairbanks)
    }

    /// <summary>
    /// Building types for load calculations
    /// </summary>
    public enum BuildingType
    {
        Office,
        Retail,
        School,
        Healthcare,
        Hotel,
        Warehouse,
        Residential,
        Restaurant,
        DataCenter
    }

    /// <summary>
    /// Duct sizing methods
    /// </summary>
    public enum DuctSizingMethod
    {
        EqualFriction,
        StaticRegain,
        VelocityReduction
    }

    /// <summary>
    /// Heating/Cooling load calculation result
    /// </summary>
    public class LoadCalculationResult
    {
        public double SensibleLoad { get; set; }  // BTU/hr
        public double LatentLoad { get; set; }    // BTU/hr
        public double TotalLoad { get; set; }     // BTU/hr
        public double AirflowRequired { get; set; } // CFM
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Duct sizing result
    /// </summary>
    public class DuctSizeResult
    {
        public string Size { get; set; }  // e.g., "18x12"
        public double Width { get; set; }  // inches
        public double Height { get; set; } // inches
        public double Diameter { get; set; } // inches (for round ducts)
        public double Velocity { get; set; } // FPM
        public double FrictionLoss { get; set; } // inches w.g. per 100 ft
        public bool IsCompliant { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Ventilation requirement result
    /// </summary>
    public class VentilationResult
    {
        public double OutdoorAir { get; set; } // CFM
        public double PeopleComponent { get; set; } // CFM
        public double AreaComponent { get; set; } // CFM
        public string Standard { get; set; } // "ASHRAE 62.1-2022"
    }

    /// <summary>
    /// Psychrometric properties
    /// </summary>
    public class PsychrometricProperties
    {
        public double DryBulbTemp { get; set; }     // °F
        public double WetBulbTemp { get; set; }     // °F
        public double DewPoint { get; set; }        // °F
        public double RelativeHumidity { get; set; } // %
        public double HumidityRatio { get; set; }   // lb moisture/lb dry air
        public double Enthalpy { get; set; }        // BTU/lb
        public double SpecificVolume { get; set; }  // ft³/lb
    }

    #endregion

    /// <summary>
    /// ASHRAE (American Society of Heating, Refrigerating and Air-Conditioning Engineers) standards implementation
    /// Includes ASHRAE 90.1 (Energy), 62.1 (Ventilation), and Fundamentals calculations
    /// </summary>
    public static class ASHRAEStandards
    {
        public const string Version = "2022";

        #region ASHRAE 90.1 - Energy Standard

        /// <summary>
        /// Climate zone definitions by location
        /// Reference: ASHRAE 90.1-2022 Table B-2
        /// </summary>
        private static readonly Dictionary<string, ClimateZone> ClimateZonesByLocation = new Dictionary<string, ClimateZone>(StringComparer.OrdinalIgnoreCase)
        {
            // Sample locations - full implementation would include all US counties
            { "Miami, FL", ClimateZone.Zone1A },
            { "Houston, TX", ClimateZone.Zone2A },
            { "Phoenix, AZ", ClimateZone.Zone2B },
            { "Atlanta, GA", ClimateZone.Zone3A },
            { "Las Vegas, NV", ClimateZone.Zone3B },
            { "San Francisco, CA", ClimateZone.Zone3C },
            { "New York, NY", ClimateZone.Zone4A },
            { "Albuquerque, NM", ClimateZone.Zone4B },
            { "Seattle, WA", ClimateZone.Zone4C },
            { "Chicago, IL", ClimateZone.Zone5A },
            { "Denver, CO", ClimateZone.Zone5B },
            { "Minneapolis, MN", ClimateZone.Zone6A },
            { "Duluth, MN", ClimateZone.Zone7 },
            { "Fairbanks, AK", ClimateZone.Zone8 }
        };

        /// <summary>
        /// Get climate zone for location
        /// </summary>
        /// <param name="location">Location string (city, state)</param>
        /// <returns>Climate zone</returns>
        public static ClimateZone GetClimateZone(string location)
        {
            return ClimateZonesByLocation.TryGetValue(location, out ClimateZone zone) 
                ? zone 
                : ClimateZone.Zone4A; // Default to mixed humid
        }

        /// <summary>
        /// Design temperatures by climate zone
        /// Reference: ASHRAE Fundamentals
        /// </summary>
        private static readonly Dictionary<ClimateZone, (int coolingDB, int heatingDB)> DesignTemperatures = new Dictionary<ClimateZone, (int, int)>
        {
            { ClimateZone.Zone1A, (92, 44) },
            { ClimateZone.Zone1B, (105, 37) },
            { ClimateZone.Zone2A, (94, 29) },
            { ClimateZone.Zone2B, (103, 34) },
            { ClimateZone.Zone3A, (92, 23) },
            { ClimateZone.Zone3B, (100, 28) },
            { ClimateZone.Zone3C, (82, 41) },
            { ClimateZone.Zone4A, (91, 10) },
            { ClimateZone.Zone4B, (90, 10) },
            { ClimateZone.Zone4C, (79, 28) },
            { ClimateZone.Zone5A, (89, -2) },
            { ClimateZone.Zone5B, (88, -4) },
            { ClimateZone.Zone6A, (87, -16) },
            { ClimateZone.Zone6B, (86, -11) },
            { ClimateZone.Zone7, (84, -25) },
            { ClimateZone.Zone8, (78, -46) }
        };

        /// <summary>
        /// Get design temperatures for climate zone
        /// </summary>
        /// <param name="zone">Climate zone</param>
        /// <returns>Cooling and heating design temperatures (°F)</returns>
        public static (int coolingDB, int heatingDB) GetDesignTemperatures(ClimateZone zone)
        {
            return DesignTemperatures.TryGetValue(zone, out var temps) 
                ? temps 
                : (91, 10); // Default
        }

        #endregion

        #region Load Calculations

        /// <summary>
        /// Calculate heating load
        /// Reference: ASHRAE Fundamentals - Heat Loss Calculation
        /// Formula: Q = U × A × ΔT
        /// </summary>
        /// <param name="uValue">U-value of assembly (BTU/hr·ft²·°F)</param>
        /// <param name="area">Area (square feet)</param>
        /// <param name="indoorTemp">Indoor design temperature (°F)</param>
        /// <param name="outdoorTemp">Outdoor design temperature (°F)</param>
        /// <returns>Heating load (BTU/hr)</returns>
        public static double CalculateHeatingLoad(double uValue, double area, double indoorTemp, double outdoorTemp)
        {
            double deltaT = indoorTemp - outdoorTemp;
            return uValue * area * deltaT;
        }

        /// <summary>
        /// Calculate cooling load with CLTD method
        /// Reference: ASHRAE Cooling and Heating Load Calculation Manual
        /// </summary>
        /// <param name="uValue">U-value (BTU/hr·ft²·°F)</param>
        /// <param name="area">Area (ft²)</param>
        /// <param name="cltd">Cooling Load Temperature Difference (°F)</param>
        /// <param name="lm">Light to dark color multiplier (0.8-1.0)</param>
        /// <returns>Cooling load (BTU/hr)</returns>
        public static double CalculateCoolingLoad(double uValue, double area, double cltd, double lm = 1.0)
        {
            return uValue * area * cltd * lm;
        }

        /// <summary>
        /// Calculate solar heat gain
        /// Reference: ASHRAE Fundamentals - Solar Heat Gain
        /// </summary>
        /// <param name="area">Window area (ft²)</param>
        /// <param name="shgc">Solar Heat Gain Coefficient</param>
        /// <param name="solarRadiation">Solar radiation (BTU/hr·ft²)</param>
        /// <returns>Solar heat gain (BTU/hr)</returns>
        public static double CalculateSolarHeatGain(double area, double shgc, double solarRadiation)
        {
            return area * shgc * solarRadiation;
        }

        /// <summary>
        /// Calculate internal heat gain from people
        /// </summary>
        /// <param name="occupants">Number of occupants</param>
        /// <param name="activity">Activity level (sedentary, light work, heavy work)</param>
        /// <returns>Heat gain (BTU/hr)</returns>
        public static double CalculateOccupantLoad(int occupants, string activity)
        {
            var heatGainPerPerson = new Dictionary<string, (double sensible, double latent)>(StringComparer.OrdinalIgnoreCase)
            {
                { "sedentary", (250, 200) },      // Seated, quiet
                { "light work", (315, 235) },     // Office work
                { "moderate work", (375, 375) },  // Walking
                { "heavy work", (535, 635) }      // Heavy labor
            };

            if (!heatGainPerPerson.TryGetValue(activity, out var gains))
                gains = heatGainPerPerson["light work"]; // Default

            double sensible = occupants * gains.sensible;
            double latent = occupants * gains.latent;

            return sensible + latent;
        }

        /// <summary>
        /// Calculate equipment heat gain
        /// </summary>
        /// <param name="watts">Equipment power (watts)</param>
        /// <param name="usageFactor">Usage factor (0.0 to 1.0)</param>
        /// <param name="radiationFactor">Radiation factor (0.5-0.7 typical)</param>
        /// <returns>Sensible heat gain (BTU/hr)</returns>
        public static double CalculateEquipmentLoad(double watts, double usageFactor = 1.0, double radiationFactor = 0.6)
        {
            return watts * 3.412 * usageFactor * radiationFactor; // 3.412 BTU/hr per watt
        }

        /// <summary>
        /// Calculate lighting heat gain
        /// </summary>
        /// <param name="watts">Lighting power (watts)</param>
        /// <param name="ballastFactor">Ballast factor (1.0 for LED, 1.2 for fluorescent)</param>
        /// <param name="usageFactor">Usage factor (0.0 to 1.0)</param>
        /// <returns>Heat gain (BTU/hr)</returns>
        public static double CalculateLightingLoad(double watts, double ballastFactor = 1.0, double usageFactor = 1.0)
        {
            return watts * 3.412 * ballastFactor * usageFactor;
        }

        /// <summary>
        /// Calculate infiltration load
        /// </summary>
        /// <param name="cfm">Infiltration airflow (CFM)</param>
        /// <param name="indoorTemp">Indoor temperature (°F)</param>
        /// <param name="outdoorTemp">Outdoor temperature (°F)</param>
        /// <param name="indoorHumidity">Indoor humidity ratio (lb/lb)</param>
        /// <param name="outdoorHumidity">Outdoor humidity ratio (lb/lb)</param>
        /// <returns>Load calculation result with sensible and latent components</returns>
        public static LoadCalculationResult CalculateInfiltrationLoad(double cfm, double indoorTemp, double outdoorTemp, 
            double indoorHumidity, double outdoorHumidity)
        {
            // Sensible: Q = 1.08 × CFM × ΔT
            double sensible = 1.08 * cfm * Math.Abs(indoorTemp - outdoorTemp);

            // Latent: Q = 4840 × CFM × Δω (where ω is humidity ratio)
            double latent = 4840 * cfm * Math.Abs(indoorHumidity - outdoorHumidity);

            return new LoadCalculationResult
            {
                SensibleLoad = sensible,
                LatentLoad = latent,
                TotalLoad = sensible + latent
            };
        }

        #endregion

        #region ASHRAE 62.1 - Ventilation

        /// <summary>
        /// Ventilation rates per ASHRAE 62.1-2022 Table 6.2.2.1
        /// </summary>
        private static readonly Dictionary<string, (double peopleRate, double areaRate)> VentilationRates = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            // Space Type, (CFM/person, CFM/ft²)
            { "office", (5, 0.06) },
            { "conference room", (5, 0.06) },
            { "classroom", (10, 0.12) },
            { "retail", (7.5, 0.12) },
            { "restaurant dining", (7.5, 0.18) },
            { "kitchen", (7.5, 0.30) },
            { "gym", (20, 0.06) },
            { "theater", (5, 0.06) },
            { "library", (5, 0.12) },
            { "hospital patient room", (25, 0.06) },
            { "warehouse", (0, 0.06) }
        };

        /// <summary>
        /// Calculate ventilation requirements per ASHRAE 62.1
        /// Reference: ASHRAE 62.1-2022 Ventilation Rate Procedure
        /// </summary>
        /// <param name="spaceType">Type of space</param>
        /// <param name="occupants">Number of occupants</param>
        /// <param name="area">Floor area (ft²)</param>
        /// <returns>Ventilation result</returns>
        public static VentilationResult CalculateVentilationRequirement(string spaceType, int occupants, double area)
        {
            if (!VentilationRates.TryGetValue(spaceType, out var rates))
            {
                rates = (5, 0.06); // Default to office
            }

            double peopleComponent = occupants * rates.peopleRate;
            double areaComponent = area * rates.areaRate;
            double totalOA = peopleComponent + areaComponent;

            return new VentilationResult
            {
                OutdoorAir = totalOA,
                PeopleComponent = peopleComponent,
                AreaComponent = areaComponent,
                Standard = "ASHRAE 62.1-2022"
            };
        }

        /// <summary>
        /// Calculate minimum outdoor air fraction (for economizer control)
        /// </summary>
        /// <param name="outdoorAir">Required outdoor air (CFM)</param>
        /// <param name="supplyAir">Total supply air (CFM)</param>
        /// <returns>Minimum outdoor air fraction (0.0 to 1.0)</returns>
        public static double CalculateMinimumOAFraction(double outdoorAir, double supplyAir)
        {
            if (supplyAir <= 0) return 0;
            return Math.Min(outdoorAir / supplyAir, 1.0);
        }

        #endregion

        #region Duct Sizing

        /// <summary>
        /// Calculate duct size using equal friction method
        /// Reference: ASHRAE Fundamentals - Duct Design
        /// </summary>
        /// <param name="airflow">Airflow (CFM)</param>
        /// <param name="frictionRate">Friction rate (in. w.g. per 100 ft) - typical 0.08-0.15</param>
        /// <returns>Duct size result</returns>
        public static DuctSizeResult CalculateDuctSizeEqualFriction(double airflow, double frictionRate = 0.10)
        {
            var result = new DuctSizeResult();

            // For round duct: D = √(576 × Q / (π × V))
            // For equal friction: V = 4005 × √(D × Pf) where Pf is friction in in. w.g./100 ft
            // Rearranging: D = (Q / (4005 × √Pf))^(2/3) / (π/576)^(1/3)
            
            // Simplified calculation using friction chart approximation
            double diameter = Math.Pow(airflow / (4005 * Math.Sqrt(frictionRate)), 2.0/3.0);
            
            result.Diameter = Math.Ceiling(diameter);
            result.Velocity = (airflow * 144) / (Math.PI * Math.Pow(result.Diameter / 2, 2));
            result.FrictionLoss = frictionRate;

            // For rectangular: use aspect ratio 2:1
            result.Height = Math.Ceiling(diameter / Math.Sqrt(2));
            result.Width = result.Height * 2;
            result.Size = $"{result.Width}x{result.Height}";

            // Validate velocity limits
            ValidateDuctVelocity(result);

            return result;
        }

        /// <summary>
        /// Calculate duct size for given velocity
        /// </summary>
        /// <param name="airflow">Airflow (CFM)</param>
        /// <param name="velocity">Design velocity (FPM)</param>
        /// <returns>Duct size result</returns>
        public static DuctSizeResult CalculateDuctSizeByVelocity(double airflow, double velocity)
        {
            var result = new DuctSizeResult();

            // A = Q / V (where A is in ft², Q in CFM, V in FPM)
            double areaFt2 = airflow / velocity;
            double areaIn2 = areaFt2 * 144;

            // For round duct: A = π × r²
            result.Diameter = Math.Ceiling(Math.Sqrt(areaIn2 * 4 / Math.PI));
            result.Velocity = velocity;

            // For rectangular: use aspect ratio 2:1
            result.Height = Math.Ceiling(Math.Sqrt(areaIn2 / 2));
            result.Width = result.Height * 2;
            result.Size = $"{result.Width}x{result.Height}";

            // Estimate friction loss (rough approximation)
            result.FrictionLoss = EstimateFrictionLoss(airflow, result.Diameter);

            ValidateDuctVelocity(result);

            return result;
        }

        private static void ValidateDuctVelocity(DuctSizeResult result)
        {
            result.IsCompliant = true;

            // Typical velocity limits
            const double MaxMainDuct = 1200; // FPM for main ducts
            const double MaxBranch = 900;     // FPM for branch ducts
            const double MinVelocity = 600;   // FPM minimum to prevent settling

            if (result.Velocity > MaxMainDuct)
            {
                result.IsCompliant = false;
                result.Warnings.Add($"Velocity {result.Velocity:F0} FPM exceeds recommended maximum {MaxMainDuct} FPM for main ducts");
            }

            if (result.Velocity < MinVelocity)
            {
                result.Warnings.Add($"Velocity {result.Velocity:F0} FPM below recommended minimum {MinVelocity} FPM");
            }

            // Friction loss check
            if (result.FrictionLoss > 0.15)
            {
                result.Warnings.Add($"Friction loss {result.FrictionLoss:F3} in. w.g./100 ft exceeds typical maximum 0.15");
            }
        }

        private static double EstimateFrictionLoss(double cfm, double diameter)
        {
            // Rough estimation using Darcy-Weisbach approximation
            // This is simplified - real calculation uses friction factor charts
            double velocity = (cfm * 144) / (Math.PI * Math.Pow(diameter / 2, 2));
            return 0.0001 * Math.Pow(velocity / 1000, 1.9) * (12 / diameter);
        }

        /// <summary>
        /// Get standard duct sizes
        /// </summary>
        /// <param name="minSize">Minimum dimension (inches)</param>
        /// <returns>List of standard rectangular duct sizes</returns>
        public static List<string> GetStandardDuctSizes(double minSize = 6)
        {
            var sizes = new List<string>();
            var standardSizes = new[] { 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 36, 40, 48, 54, 60 };

            foreach (var width in standardSizes)
            {
                foreach (var height in standardSizes)
                {
                    if (height <= width && height >= minSize)
                    {
                        sizes.Add($"{width}x{height}");
                    }
                }
            }

            return sizes;
        }

        #endregion

        #region Psychrometrics

        /// <summary>
        /// Calculate psychrometric properties from dry bulb and relative humidity
        /// Reference: ASHRAE Fundamentals - Psychrometrics
        /// </summary>
        /// <param name="dryBulb">Dry bulb temperature (°F)</param>
        /// <param name="relativeHumidity">Relative humidity (%)</param>
        /// <param name="pressure">Atmospheric pressure (psia) - 14.696 at sea level</param>
        /// <returns>Psychrometric properties</returns>
        public static PsychrometricProperties CalculatePsychrometricProperties(double dryBulb, double relativeHumidity, double pressure = 14.696)
        {
            var props = new PsychrometricProperties
            {
                DryBulbTemp = dryBulb,
                RelativeHumidity = relativeHumidity
            };

            // Saturation pressure (psia) - Antoine equation approximation
            double psat = Math.Exp(77.3450 + 0.0057 * dryBulb - 7235 / (dryBulb + 459.67)) / 2.17e+7;

            // Partial pressure of water vapor
            double pw = psat * (relativeHumidity / 100.0);

            // Humidity ratio (lb moisture / lb dry air)
            props.HumidityRatio = 0.62198 * pw / (pressure - pw);

            // Wet bulb temperature (simplified approximation)
            props.WetBulbTemp = dryBulb * Math.Atan(0.151977 * Math.Pow(relativeHumidity + 8.313659, 0.5)) 
                + Math.Atan(dryBulb + relativeHumidity) 
                - Math.Atan(relativeHumidity - 1.676331) 
                + 0.00391838 * Math.Pow(relativeHumidity, 1.5) * Math.Atan(0.023101 * relativeHumidity) 
                - 4.686035;

            // Dew point (°F) - Magnus formula approximation
            double a = 17.27;
            double b = 237.7;
            double alpha = ((a * dryBulb) / (b + dryBulb)) + Math.Log(relativeHumidity / 100.0);
            props.DewPoint = (b * alpha) / (a - alpha);
            props.DewPoint = props.DewPoint * 9.0/5.0 + 32; // Convert C to F

            // Enthalpy (BTU/lb) - h = 0.240 × t + W × (1061 + 0.444 × t)
            props.Enthalpy = 0.240 * dryBulb + props.HumidityRatio * (1061 + 0.444 * dryBulb);

            // Specific volume (ft³/lb) - v = 0.370486 × (t + 459.67) × (1 + 1.6078 × W) / P
            props.SpecificVolume = 0.370486 * (dryBulb + 459.67) * (1 + 1.6078 * props.HumidityRatio) / pressure;

            return props;
        }

        /// <summary>
        /// Calculate sensible heat ratio (SHR)
        /// </summary>
        /// <param name="sensibleLoad">Sensible cooling load (BTU/hr)</param>
        /// <param name="totalLoad">Total cooling load (BTU/hr)</param>
        /// <returns>SHR (0.0 to 1.0)</returns>
        public static double CalculateSensibleHeatRatio(double sensibleLoad, double totalLoad)
        {
            if (totalLoad <= 0) return 0;
            return Math.Min(sensibleLoad / totalLoad, 1.0);
        }

        /// <summary>
        /// Calculate mixed air temperature
        /// </summary>
        /// <param name="outdoorAirTemp">Outdoor air temperature (°F)</param>
        /// <param name="returnAirTemp">Return air temperature (°F)</param>
        /// <param name="outdoorAirFraction">Outdoor air fraction (0.0 to 1.0)</param>
        /// <returns>Mixed air temperature (°F)</returns>
        public static double CalculateMixedAirTemperature(double outdoorAirTemp, double returnAirTemp, double outdoorAirFraction)
        {
            return outdoorAirTemp * outdoorAirFraction + returnAirTemp * (1 - outdoorAirFraction);
        }

        #endregion

        #region Energy Calculations

        /// <summary>
        /// Calculate cooling capacity required (tons)
        /// </summary>
        /// <param name="btuPerHour">Cooling load (BTU/hr)</param>
        /// <returns>Capacity in tons (1 ton = 12,000 BTU/hr)</returns>
        public static double CalculateCoolingTons(double btuPerHour)
        {
            return btuPerHour / 12000.0;
        }

        /// <summary>
        /// Calculate heating capacity required (MBH)
        /// </summary>
        /// <param name="btuPerHour">Heating load (BTU/hr)</param>
        /// <returns>Capacity in MBH (thousands of BTU/hr)</returns>
        public static double CalculateHeatingMBH(double btuPerHour)
        {
            return btuPerHour / 1000.0;
        }

        /// <summary>
        /// Calculate annual energy consumption
        /// </summary>
        /// <param name="powerKW">Power consumption (kW)</param>
        /// <param name="hoursPerYear">Operating hours per year</param>
        /// <param name="loadFactor">Average load factor (0.0 to 1.0)</param>
        /// <returns>Energy consumption (kWh/year)</returns>
        public static double CalculateAnnualEnergy(double powerKW, double hoursPerYear, double loadFactor = 1.0)
        {
            return powerKW * hoursPerYear * loadFactor;
        }

        /// <summary>
        /// Calculate energy efficiency ratio (EER)
        /// </summary>
        /// <param name="coolingCapacityBTU">Cooling capacity (BTU/hr)</param>
        /// <param name="powerWatts">Power consumption (watts)</param>
        /// <returns>EER (BTU/hr per watt)</returns>
        public static double CalculateEER(double coolingCapacityBTU, double powerWatts)
        {
            if (powerWatts <= 0) return 0;
            return coolingCapacityBTU / powerWatts;
        }

        /// <summary>
        /// Calculate coefficient of performance (COP)
        /// </summary>
        /// <param name="capacityBTU">Heating or cooling capacity (BTU/hr)</param>
        /// <param name="powerWatts">Power consumption (watts)</param>
        /// <returns>COP (dimensionless)</returns>
        public static double CalculateCOP(double capacityBTU, double powerWatts)
        {
            if (powerWatts <= 0) return 0;
            return capacityBTU / (powerWatts * 3.412);
        }

        #endregion
    }
}
