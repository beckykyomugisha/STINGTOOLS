using System;
using System.Collections.Generic;

namespace StingBIM.Standards.CIBSE
{
    /// <summary>
    /// CIBSE - Chartered Institution of Building Services Engineers
    /// UK-based professional engineering institution providing comprehensive
    /// guidance for building services engineering (MEP systems)
    /// 
    /// CIBSE is the authoritative source for:
    /// - HVAC design and sizing
    /// - Lighting design and calculations
    /// - Electrical systems design
    /// - Public health (plumbing) engineering
    /// - Energy efficiency and sustainability
    /// - Building control systems
    /// - Climate data and design parameters
    /// 
    /// Widely used across Commonwealth countries including Africa
    /// </summary>
    public static class CIBSEStandards
    {
        #region Overview and Applicability

        /// <summary>
        /// Main CIBSE guides relevant to building services
        /// </summary>
        public static readonly string[] MainGuides = new[]
        {
            "Guide A - Environmental Design",
            "Guide B - Heating, Ventilating, Air Conditioning and Refrigeration",
            "Guide C - Reference Data",
            "Guide D - Transportation Systems in Buildings",
            "Guide E - Fire Safety Engineering",
            "Guide F - Energy Efficiency in Buildings",
            "Guide G - Public Health and Plumbing Engineering",
            "Guide H - Building Control Systems",
            "Guide J - Weather, Solar and Illuminance Data",
            "Guide K - Electricity in Buildings",
            "Guide L - Sustainability",
            "Guide M - Maintenance Engineering and Management"
        };

        /// <summary>
        /// CIBSE applicability in Africa
        /// Widely used due to Commonwealth ties and UK training
        /// </summary>
        public static string[] GetAfricanApplicability()
        {
            return new[]
            {
                "Commonwealth countries (Uganda, Kenya, Tanzania, etc.) - Primary reference",
                "Former British colonies - Standard practice",
                "Professional engineers trained in UK system",
                "Used alongside local codes (UNBS, KEBS, etc.)",
                "Climate data adapted for tropical/sub-tropical conditions",
                "Energy efficiency increasingly important in Africa",
                "Sustainability focus aligns with green building trends"
            };
        }

        #endregion

        #region Guide A - Environmental Design

        /// <summary>
        /// CIBSE Guide A - Environmental Design
        /// Thermal comfort, indoor air quality, acoustics, lighting
        /// </summary>
        public static class GuideA_EnvironmentalDesign
        {
            /// <summary>
            /// Thermal comfort parameters
            /// </summary>
            public static class ThermalComfort
            {
                /// <summary>
                /// Recommended indoor temperatures for different building types (°C)
                /// </summary>
                public static Dictionary<string, (double Summer, double Winter)> GetIndoorTemperatures()
                {
                    return new Dictionary<string, (double, double)>
                    {
                        { "Offices", (23.0, 21.0) },
                        { "Schools - Classrooms", (21.0, 18.0) },
                        { "Hospitals - Wards", (24.0, 22.0) },
                        { "Hospitals - Operating Theatres", (21.0, 21.0) },
                        { "Hotels - Bedrooms", (23.0, 21.0) },
                        { "Retail - Shops", (21.0, 19.0) },
                        { "Residential - Living Areas", (23.0, 21.0) },
                        { "Residential - Bedrooms", (20.0, 18.0) },
                        { "Sports Halls", (17.0, 15.0) },
                        { "Restaurants", (22.0, 20.0) }
                    };
                }

                /// <summary>
                /// Adapting comfort temperatures for African climates
                /// </summary>
                public static double GetAdaptedComfortTemperature(
                    double outdoorTemperature,
                    bool naturalVentilation)
                {
                    if (naturalVentilation)
                    {
                        // Adaptive comfort model - people acclimatized to warmer climates
                        // Accept higher indoor temperatures
                        double baseComfort = 23.0;
                        double adaptation = (outdoorTemperature - 25.0) * 0.3;
                        return Math.Min(baseComfort + adaptation, 28.0); // Max 28°C
                    }
                    else
                    {
                        // Air-conditioned buildings - conventional comfort range
                        return 23.0;
                    }
                }

                /// <summary>
                /// Relative humidity recommendations (%)
                /// </summary>
                public static (double Min, double Max) GetHumidityRange(string buildingType)
                {
                    return buildingType.ToLower() switch
                    {
                        "hospital" => (40.0, 60.0),      // Tighter control
                        "data center" => (40.0, 55.0),   // Prevent static
                        "museum" => (45.0, 55.0),        // Artifact preservation
                        "office" => (30.0, 70.0),        // General comfort
                        _ => (30.0, 70.0)                // Default comfort range
                    };
                }
            }

            /// <summary>
            /// Indoor Air Quality (IAQ) requirements
            /// </summary>
            public static class IndoorAirQuality
            {
                /// <summary>
                /// Fresh air requirements (litres per second per person)
                /// </summary>
                public static double GetFreshAirRequirement(string spaceType)
                {
                    return spaceType.ToLower() switch
                    {
                        "office" => 10.0,
                        "classroom" => 8.0,
                        "conference room" => 12.0,
                        "restaurant" => 12.0,
                        "retail" => 8.0,
                        "residential" => 0.3,  // Air changes per hour for whole dwelling
                        "gym" => 15.0,
                        "hospital ward" => 10.0,
                        "operating theatre" => 25.0,
                        _ => 10.0  // Default
                    };
                }

                /// <summary>
                /// CO2 concentration limits (ppm)
                /// </summary>
                public const int MaxCO2Concentration = 1000; // 1000 ppm above outdoor

                /// <summary>
                /// Ventilation strategies for African climates
                /// </summary>
                public static string[] GetVentilationStrategies()
                {
                    return new[]
                    {
                        "Natural Ventilation - Primary strategy where climate permits",
                        "Cross Ventilation - Openings on opposite facades",
                        "Stack Ventilation - Vertical air movement (hot air rises)",
                        "Mixed-Mode - Natural + mechanical as needed",
                        "Night Cooling - Purge warm air accumulated during day",
                        "Ceiling Fans - Enhance air movement and comfort",
                        "Mechanical Ventilation - Where natural not feasible (hot-humid)",
                        "Heat Recovery - Not typically cost-effective in Africa"
                    };
                }
            }

            /// <summary>
            /// Acoustic design parameters
            /// </summary>
            public static class Acoustics
            {
                /// <summary>
                /// Maximum noise levels (NR - Noise Rating)
                /// </summary>
                public static int GetMaximumNoiseRating(string roomType)
                {
                    return roomType.ToLower() switch
                    {
                        "concert hall" => 25,
                        "recording studio" => 20,
                        "theatre" => 25,
                        "bedroom" => 30,
                        "hospital ward" => 30,
                        "office - executive" => 35,
                        "office - open plan" => 40,
                        "classroom" => 35,
                        "restaurant" => 45,
                        "retail" => 45,
                        _ => 40
                    };
                }

                /// <summary>
                /// Reverberation time recommendations (seconds)
                /// </summary>
                public static double GetReverberationTime(string roomType, double volumeM3)
                {
                    double baseTime = roomType.ToLower() switch
                    {
                        "lecture theatre" => 0.8,
                        "classroom" => 0.6,
                        "conference room" => 0.6,
                        "office" => 0.5,
                        "restaurant" => 1.0,
                        "church" => 2.0,
                        _ => 0.8
                    };

                    // Adjust for volume (larger rooms allow slightly longer RT)
                    double volumeFactor = Math.Log10(volumeM3 / 100.0);
                    return baseTime * (1.0 + volumeFactor * 0.1);
                }
            }
        }

        #endregion

        #region Guide B - HVAC Systems

        /// <summary>
        /// CIBSE Guide B - Heating, Ventilating, Air Conditioning and Refrigeration
        /// </summary>
        public static class GuideB_HVAC
        {
            /// <summary>
            /// Cooling load calculation factors for Africa
            /// </summary>
            public static class CoolingLoads
            {
                /// <summary>
                /// Solar heat gain factors for different orientations (W/m² glazing)
                /// Tropical location - high solar gains
                /// </summary>
                public static Dictionary<string, double> GetSolarHeatGain(double latitude)
                {
                    // Values for clear day, vertical glazing
                    // African locations typically 0-30° latitude
                    return new Dictionary<string, double>
                    {
                        { "North", 150.0 },
                        { "Northeast", 250.0 },
                        { "East", 450.0 },
                        { "Southeast", 400.0 },
                        { "South", 300.0 },
                        { "Southwest", 400.0 },
                        { "West", 450.0 },
                        { "Northwest", 250.0 },
                        { "Horizontal (roof)", 800.0 }
                    };
                }

                /// <summary>
                /// Internal heat gains (W/m²)
                /// </summary>
                public static double GetInternalHeatGain(
                    string buildingType,
                    double occupancyDensityPerM2,
                    double lightingWattsPerM2,
                    double equipmentWattsPerM2)
                {
                    // People: 90W sensible + 50W latent per person
                    double occupantHeat = occupancyDensityPerM2 * 140.0;
                    
                    // Lighting: Assume 80% of lighting becomes heat
                    double lightingHeat = lightingWattsPerM2 * 0.8;
                    
                    // Equipment: Varies by diversity factor
                    double equipmentHeat = equipmentWattsPerM2 * 0.7; // 70% diversity
                    
                    return occupantHeat + lightingHeat + equipmentHeat;
                }

                /// <summary>
                /// Infiltration and ventilation loads
                /// </summary>
                public static double GetVentilationLoad(
                    double freshAirLPS,
                    double outdoorTemp,
                    double indoorTemp,
                    double outdoorHumidity,
                    double indoorHumidity)
                {
                    // Sensible cooling load (W)
                    double sensibleLoad = freshAirLPS * 1.2 * 1.005 * (outdoorTemp - indoorTemp);
                    
                    // Latent cooling load (W)
                    double latentLoad = freshAirLPS * 1.2 * 2501 * (outdoorHumidity - indoorHumidity);
                    
                    return sensibleLoad + latentLoad;
                }
            }

            /// <summary>
            /// Air conditioning system types suitable for Africa
            /// </summary>
            public static class ACSystemTypes
            {
                /// <summary>
                /// Gets recommended AC system type
                /// </summary>
                public static string[] GetRecommendedSystem(
                    string buildingType,
                    double areaM2,
                    bool reliablePower)
                {
                    var recommendations = new List<string>();

                    if (areaM2 < 100)
                    {
                        recommendations.Add("Split AC units - Most common, affordable");
                        if (!reliablePower)
                            recommendations.Add("Solar-powered split AC - Emerging option");
                    }
                    else if (areaM2 < 1000)
                    {
                        recommendations.Add("Multi-split or VRF system - Efficient for medium buildings");
                        recommendations.Add("Package AC units - Simple, modular");
                        if (buildingType.ToLower().Contains("office"))
                            recommendations.Add("Cassette units - Good for open offices");
                    }
                    else
                    {
                        recommendations.Add("Chilled water system - Most efficient for large buildings");
                        recommendations.Add("VRF (Variable Refrigerant Flow) - Flexible zoning");
                        recommendations.Add("District cooling - If available in development");
                    }

                    if (!reliablePower)
                    {
                        recommendations.Add("CRITICAL: Size generators for full AC load");
                        recommendations.Add("Consider thermal mass and passive cooling to reduce AC demand");
                    }

                    return recommendations.ToArray();
                }

                /// <summary>
                /// Refrigerant selection for hot climates
                /// </summary>
                public static string[] GetRefrigerantRecommendations()
                {
                    return new[]
                    {
                        "R410A - Most common, good efficiency",
                        "R32 - Lower GWP (Global Warming Potential), emerging standard",
                        "R134a - Older systems, being phased out",
                        "Avoid R22 - Banned under Montreal Protocol",
                        "Natural refrigerants (CO2, ammonia) - Sustainable but limited availability"
                    };
                }
            }

            /// <summary>
            /// Energy efficiency recommendations
            /// Critical for African context (high energy costs)
            /// </summary>
            public static class EnergyEfficiency
            {
                /// <summary>
                /// Minimum Energy Efficiency Ratio (EER) recommendations
                /// Higher is better - more cooling per unit of electricity
                /// </summary>
                public static double GetMinimumEER(string systemType)
                {
                    return systemType.ToLower() switch
                    {
                        "split ac" => 3.0,          // EER 3.0 = 10.2 SEER
                        "window ac" => 2.5,         // Lower efficiency
                        "vrf system" => 3.5,        // High efficiency
                        "chiller" => 3.2,           // Water-cooled
                        "package unit" => 2.8,
                        _ => 3.0
                    };
                }

                /// <summary>
                /// Energy-saving strategies for African buildings
                /// </summary>
                public static string[] GetEnergySavingStrategies()
                {
                    return new[]
                    {
                        "Passive Cooling First - Orientation, shading, thermal mass",
                        "Natural Ventilation - Use when outdoor conditions permit",
                        "High-Efficiency Equipment - Inverter AC, high EER/SEER",
                        "Proper Sizing - Don't oversize (reduces efficiency)",
                        "Insulation - Roof and walls to reduce cooling load",
                        "Solar Shading - External shading devices most effective",
                        "Light Colors - Reflect solar radiation on roof/walls",
                        "LED Lighting - Reduces internal heat gains",
                        "Occupancy Sensors - AC only when spaces occupied",
                        "Variable Speed Drives - On fans and pumps",
                        "Heat Recovery - Limited benefit in hot climates",
                        "Free Cooling - Night ventilation to purge heat",
                        "Ceiling Fans - Extend comfort range, reduce AC hours"
                    };
                }
            }
        }

        #endregion

        #region Guide C - Reference Data

        /// <summary>
        /// CIBSE Guide C - Reference Data
        /// Physical properties, conversion factors, design data
        /// </summary>
        public static class GuideC_ReferenceData
        {
            /// <summary>
            /// Thermal properties of common building materials
            /// </summary>
            public static class ThermalProperties
            {
                /// <summary>
                /// Gets thermal conductivity (W/m·K)
                /// </summary>
                public static double GetThermalConductivity(string material)
                {
                    return material.ToLower() switch
                    {
                        // Structural materials
                        "concrete (dense)" => 1.4,
                        "concrete (lightweight)" => 0.5,
                        "brick (common)" => 0.6,
                        "stone" => 2.0,
                        "timber" => 0.13,
                        
                        // Insulation materials
                        "mineral wool" => 0.035,
                        "expanded polystyrene (eps)" => 0.033,
                        "extruded polystyrene (xps)" => 0.030,
                        "polyurethane foam" => 0.025,
                        
                        // Finishes
                        "plaster" => 0.5,
                        "cement render" => 1.0,
                        "gypsum board" => 0.25,
                        
                        // Metals
                        "steel" => 50.0,
                        "aluminum" => 160.0,
                        
                        _ => 1.0  // Default
                    };
                }

                /// <summary>
                /// Gets specific heat capacity (J/kg·K)
                /// </summary>
                public static double GetSpecificHeatCapacity(string material)
                {
                    return material.ToLower() switch
                    {
                        "concrete" => 1000.0,
                        "brick" => 800.0,
                        "timber" => 1600.0,
                        "steel" => 450.0,
                        "water" => 4200.0,
                        _ => 1000.0
                    };
                }

                /// <summary>
                /// Calculates thermal transmittance (U-value) for wall/roof (W/m²·K)
                /// Lower U-value = better insulation
                /// </summary>
                public static double CalculateUValue(
                    double[] layerThicknessMM,
                    double[] layerConductivity)
                {
                    double totalResistance = 0.1 + 0.04; // Internal + External surface resistance
                    
                    for (int i = 0; i < layerThicknessMM.Length; i++)
                    {
                        double thicknessM = layerThicknessMM[i] / 1000.0;
                        totalResistance += thicknessM / layerConductivity[i];
                    }
                    
                    return 1.0 / totalResistance; // U-value
                }

                /// <summary>
                /// Recommended maximum U-values for tropical climates (W/m²·K)
                /// </summary>
                public static Dictionary<string, double> GetRecommendedUValues()
                {
                    return new Dictionary<string, double>
                    {
                        { "Roof", 0.25 },          // Good insulation critical
                        { "External Wall", 0.40 }, // Moderate insulation
                        { "Windows", 2.0 },        // Single glazing acceptable in hot climates
                        { "Floor", 0.35 }          // If conditioned space below
                    };
                }
            }

            /// <summary>
            /// Psychrometric calculations
            /// </summary>
            public static class Psychrometrics
            {
                /// <summary>
                /// Calculates relative humidity from dry bulb and wet bulb temperatures
                /// </summary>
                public static double CalculateRelativeHumidity(
                    double dryBulbC,
                    double wetBulbC,
                    double pressureKPa = 101.325)
                {
                    // Simplified calculation
                    // For precise work, use full psychrometric equations
                    double dewPoint = wetBulbC - ((dryBulbC - wetBulbC) * 1.5);
                    double es = 0.611 * Math.Exp(17.27 * dryBulbC / (dryBulbC + 237.3));
                    double e = 0.611 * Math.Exp(17.27 * dewPoint / (dewPoint + 237.3));
                    
                    return (e / es) * 100.0; // Percentage
                }

                /// <summary>
                /// Typical outdoor design conditions for major African cities
                /// Dry bulb (°C), Wet bulb (°C)
                /// </summary>
                public static Dictionary<string, (double DryBulb, double WetBulb)> 
                    GetDesignConditions()
                {
                    return new Dictionary<string, (double, double)>
                    {
                        { "Lagos, Nigeria", (33.0, 27.0) },
                        { "Nairobi, Kenya", (27.0, 18.0) },
                        { "Dar es Salaam, Tanzania", (32.0, 26.0) },
                        { "Kampala, Uganda", (28.0, 21.0) },
                        { "Accra, Ghana", (32.0, 26.0) },
                        { "Kigali, Rwanda", (26.0, 19.0) },
                        { "Johannesburg, South Africa", (30.0, 19.0) },
                        { "Cairo, Egypt", (39.0, 22.0) },
                        { "Khartoum, Sudan", (43.0, 24.0) },
                        { "Mombasa, Kenya", (33.0, 27.0) }
                    };
                }
            }
        }

        #endregion

        #region Guide G - Public Health Engineering

        /// <summary>
        /// CIBSE Guide G - Public Health and Plumbing Engineering
        /// Water supply, drainage, sanitation
        /// </summary>
        public static class GuideG_PublicHealth
        {
            /// <summary>
            /// Cold water storage requirements
            /// </summary>
            public static class ColdWaterStorage
            {
                /// <summary>
                /// Gets minimum storage capacity (liters)
                /// Critical in Africa due to unreliable water supply
                /// </summary>
                public static double GetStorageCapacity(
                    string buildingType,
                    int occupants,
                    bool reliableSupply)
                {
                    // Base consumption per person per day
                    double litersPerPersonPerDay = buildingType.ToLower() switch
                    {
                        "residential" => 150.0,
                        "office" => 40.0,
                        "school" => 30.0,
                        "hospital" => 200.0,
                        "hotel" => 200.0,
                        _ => 100.0
                    };

                    // Days of storage
                    int storageDays = reliableSupply ? 1 : 3;  // Africa: 3 days minimum
                    
                    return occupants * litersPerPersonPerDay * storageDays;
                }

                /// <summary>
                /// Tank sizing recommendations for Africa
                /// </summary>
                public static string[] GetTankSizingRecommendations()
                {
                    return new[]
                    {
                        "Minimum 3-7 days storage (unreliable supply)",
                        "Multiple tanks for redundancy (cleaning, maintenance)",
                        "Elevated tank + ground tank combination",
                        "Elevated tank: 30-50% of total storage",
                        "Ground tank: 50-70% of total storage",
                        "Tank material: Concrete, plastic (polyethylene), or steel",
                        "All tanks: Covered, insect-proof, overflow protection",
                        "Access for cleaning every 6 months",
                        "First flush diverter for rainwater harvesting"
                    };
                }
            }

            /// <summary>
            /// Hot water systems
            /// </summary>
            public static class HotWaterSystems
            {
                /// <summary>
                /// Gets hot water demand (liters per day)
                /// </summary>
                public static double GetHotWaterDemand(
                    string buildingType,
                    int occupants)
                {
                    double litersPerPersonPerDay = buildingType.ToLower() switch
                    {
                        "residential" => 50.0,
                        "hotel" => 80.0,
                        "hospital" => 90.0,
                        "gym/sports" => 40.0,
                        "office" => 5.0,
                        _ => 30.0
                    };
                    
                    return occupants * litersPerPersonPerDay;
                }

                /// <summary>
                /// Hot water system options for Africa
                /// </summary>
                public static string[] GetHotWaterSystemOptions()
                {
                    return new[]
                    {
                        "Solar water heater - Most cost-effective for Africa",
                        "Electric geyser/heater - Backup when solar insufficient",
                        "Hybrid solar-electric - Best reliability",
                        "Instantaneous electric (point-of-use) - Low demand",
                        "Heat pump water heater - Energy efficient but higher cost",
                        "Gas water heater - Where LPG/natural gas available",
                        "Avoid electric central systems - High energy consumption"
                    };
                }

                /// <summary>
                /// Solar water heater sizing for Africa
                /// Excellent solar resource across most of continent
                /// </summary>
                public static (double CollectorAreaM2, double StorageCapacityL) 
                    SizeSolarWaterHeater(
                        int occupants,
                        double dailyDemandLitersPerPerson = 50.0)
                {
                    double totalDemandL = occupants * dailyDemandLitersPerPerson;
                    
                    // Storage: 1.0-1.5 × daily demand
                    double storageL = totalDemandL * 1.2;
                    
                    // Collector area: 40-50 liters per m² per day
                    // Africa has good solar radiation (5-6 kWh/m²/day)
                    double collectorM2 = totalDemandL / 45.0;
                    
                    return (collectorM2, storageL);
                }
            }

            /// <summary>
            /// Drainage and sanitation
            /// </summary>
            public static class Drainage
            {
                /// <summary>
                /// Sanitary fixture units (loading units)
                /// Used for drain pipe sizing
                /// </summary>
                public static Dictionary<string, double> GetFixtureUnits()
                {
                    return new Dictionary<string, double>
                    {
                        { "WC (toilet) - 9L flush", 2.0 },
                        { "WC (toilet) - 6L flush", 1.5 },
                        { "Wash basin", 0.5 },
                        { "Shower", 1.0 },
                        { "Bath", 1.5 },
                        { "Kitchen sink", 1.5 },
                        { "Urinal", 0.5 },
                        { "Washing machine", 2.0 },
                        { "Dishwasher", 1.5 }
                    };
                }

                /// <summary>
                /// Gets drain pipe diameter (mm) for given loading units
                /// </summary>
                public static int GetDrainPipeDiameter(double totalLoadingUnits)
                {
                    if (totalLoadingUnits <= 1.5) return 40;
                    else if (totalLoadingUnits <= 3.0) return 50;
                    else if (totalLoadingUnits <= 6.0) return 65;
                    else if (totalLoadingUnits <= 12.0) return 80;
                    else if (totalLoadingUnits <= 24.0) return 100;
                    else if (totalLoadingUnits <= 50.0) return 125;
                    else if (totalLoadingUnits <= 100.0) return 150;
                    else return 200;
                }

                /// <summary>
                /// Rainwater drainage
                /// African tropical rainfall requires adequate sizing
                /// </summary>
                public static double CalculateRainwaterRunoff(
                    double roofAreaM2,
                    double rainfallIntensityMMPerHour)
                {
                    // Flow rate in liters per second
                    // Assumes 100% runoff coefficient for roof
                    return (roofAreaM2 * rainfallIntensityMMPerHour) / 3600.0;
                }

                /// <summary>
                /// Design rainfall intensities for African cities (mm/hr)
                /// Based on 10-year return period, 5-minute duration
                /// </summary>
                public static Dictionary<string, double> GetDesignRainfallIntensity()
                {
                    return new Dictionary<string, double>
                    {
                        { "Lagos, Nigeria", 150.0 },
                        { "Kampala, Uganda", 120.0 },
                        { "Dar es Salaam, Tanzania", 140.0 },
                        { "Nairobi, Kenya", 100.0 },
                        { "Kigali, Rwanda", 110.0 },
                        { "Accra, Ghana", 130.0 },
                        { "Kinshasa, DRC", 160.0 },
                        { "Johannesburg, South Africa", 80.0 }
                    };
                }
            }
        }

        #endregion

        #region Guide K - Electricity in Buildings

        /// <summary>
        /// CIBSE Guide K - Electricity in Buildings
        /// Electrical distribution, lighting, power systems
        /// </summary>
        public static class GuideK_Electricity
        {
            /// <summary>
            /// Electrical load estimation
            /// </summary>
            public static class LoadEstimation
            {
                /// <summary>
                /// Gets electrical load density (W/m²)
                /// </summary>
                public static double GetLoadDensity(string buildingType)
                {
                    return buildingType.ToLower() switch
                    {
                        "residential" => 20.0,
                        "office - naturally ventilated" => 40.0,
                        "office - air conditioned" => 80.0,
                        "retail - standard" => 50.0,
                        "retail - high street" => 100.0,
                        "school" => 30.0,
                        "hospital" => 70.0,
                        "hotel" => 60.0,
                        "data center" => 500.0,
                        "light industrial" => 40.0,
                        _ => 50.0
                    };
                }

                /// <summary>
                /// Diversity factors for load sizing
                /// Actual demand is less than sum of all loads
                /// </summary>
                public static double GetDiversityFactor(
                    string loadType,
                    int numberOfUnits)
                {
                    if (loadType.ToLower().Contains("lighting"))
                    {
                        // Lighting: High diversity
                        if (numberOfUnits <= 5) return 1.0;
                        else if (numberOfUnits <= 20) return 0.8;
                        else return 0.7;
                    }
                    else if (loadType.ToLower().Contains("socket") || 
                             loadType.ToLower().Contains("power"))
                    {
                        // Socket outlets: Moderate diversity
                        if (numberOfUnits <= 10) return 0.8;
                        else if (numberOfUnits <= 50) return 0.6;
                        else return 0.5;
                    }
                    else if (loadType.ToLower().Contains("hvac") || 
                             loadType.ToLower().Contains("ac"))
                    {
                        // HVAC: Low diversity (assume most run simultaneously in hot climates)
                        return 0.9;
                    }
                    else
                    {
                        return 0.7; // Default
                    }
                }
            }

            /// <summary>
            /// Lighting design
            /// </summary>
            public static class LightingDesign
            {
                /// <summary>
                /// Recommended illuminance levels (lux)
                /// </summary>
                public static int GetRecommendedIlluminance(string spaceType)
                {
                    return spaceType.ToLower() switch
                    {
                        // General areas
                        "corridor" => 100,
                        "staircase" => 150,
                        "entrance lobby" => 200,
                        
                        // Offices
                        "office - general" => 300,
                        "office - drawing/cad" => 500,
                        "boardroom" => 300,
                        
                        // Education
                        "classroom" => 300,
                        "lecture theatre" => 300,
                        "library - reading" => 500,
                        
                        // Healthcare
                        "ward" => 100,
                        "examination room" => 500,
                        "operating theatre" => 1000,
                        
                        // Retail
                        "retail - general" => 300,
                        "retail - high-end" => 500,
                        
                        // Industrial
                        "warehouse" => 200,
                        "workshop" => 300,
                        "assembly - fine work" => 500,
                        
                        _ => 300  // Default
                    };
                }

                /// <summary>
                /// Lighting power density limits (W/m²)
                /// For energy efficiency
                /// </summary>
                public static double GetMaximumLightingPowerDensity(string buildingType)
                {
                    return buildingType.ToLower() switch
                    {
                        "office" => 12.0,      // With LED lighting
                        "retail" => 15.0,
                        "school" => 10.0,
                        "hospital" => 12.0,
                        "hotel" => 10.0,
                        "warehouse" => 8.0,
                        _ => 12.0
                    };
                }

                /// <summary>
                /// Daylight factor recommendations
                /// Higher in Africa due to abundant sunlight
                /// </summary>
                public static string[] GetDaylightingStrategies()
                {
                    return new[]
                    {
                        "Target 2-5% daylight factor in main spaces",
                        "Large windows on north/south facades (equatorial regions)",
                        "Light shelves to bounce daylight deeper into space",
                        "Light-colored internal surfaces (reflectance 60-80%)",
                        "Clerestory windows for deep spaces",
                        "Skylights/rooflights for top floor (with shading)",
                        "Daylight sensors to dim artificial lights",
                        "Solar shading essential (prevent glare and heat gain)",
                        "LED lighting: Most energy-efficient artificial source"
                    };
                }
            }

            /// <summary>
            /// Power quality and backup systems
            /// Critical in Africa due to unreliable grid
            /// </summary>
            public static class PowerQuality
            {
                /// <summary>
                /// Voltage fluctuation tolerance
                /// African grids often have poor quality
                /// </summary>
                public static string[] GetPowerQualityIssues()
                {
                    return new[]
                    {
                        "Voltage fluctuations: ±10-20% common",
                        "Frequency variation: ±2-5% possible",
                        "Power outages: Daily in many locations",
                        "Voltage sags and swells",
                        "Harmonics from non-linear loads",
                        "Lightning strikes (tropical storms)"
                    };
                }

                /// <summary>
                /// Power protection and backup recommendations
                /// </summary>
                public static string[] GetBackupPowerRecommendations(
                    string buildingType,
                    bool criticalLoads)
                {
                    var recommendations = new List<string>
                    {
                        "Voltage stabilizers/AVR - Essential for sensitive equipment",
                        "Surge protection devices (SPD) - Lightning protection"
                    };

                    if (criticalLoads)
                    {
                        recommendations.AddRange(new[]
                        {
                            "UPS (Uninterruptible Power Supply) - Immediate backup (5-30 min)",
                            "Diesel generator - Extended backup (hours to days)",
                            "Solar PV + battery - Renewable backup (day + night)",
                            "Automatic transfer switch (ATS) - Seamless changeover",
                            "Fuel storage - Minimum 3-7 days diesel supply"
                        });
                    }
                    else
                    {
                        recommendations.Add("Diesel generator - Manual start acceptable");
                        recommendations.Add("Solar PV - Reduce grid dependency");
                    }

                    if (buildingType.ToLower().Contains("hospital") ||
                        buildingType.ToLower().Contains("data"))
                    {
                        recommendations.Add("N+1 redundancy - Backup for backup systems");
                    }

                    return recommendations.ToArray();
                }

                /// <summary>
                /// Generator sizing for African context
                /// </summary>
                public static double SizeBackupGenerator(
                    double totalConnectedLoadKW,
                    double diversityFactor,
                    bool includeACLoad)
                {
                    double demandLoadKW = totalConnectedLoadKW * diversityFactor;
                    
                    // Add margin for starting currents (motors, compressors)
                    double startingMargin = 1.25;
                    
                    // AC loads have high inrush (if included)
                    if (includeACLoad)
                        startingMargin = 1.5;
                    
                    double generatorSizeKW = demandLoadKW * startingMargin;
                    
                    // Round up to next standard generator size
                    double[] standardSizes = { 10, 15, 20, 30, 40, 50, 60, 75, 100, 125, 150, 
                                               200, 250, 300, 400, 500, 600, 750, 1000 };
                    
                    foreach (var size in standardSizes)
                    {
                        if (size >= generatorSizeKW)
                            return size;
                    }
                    
                    return generatorSizeKW; // If larger than standard sizes
                }
            }
        }

        #endregion

        #region Guide L - Sustainability

        /// <summary>
        /// CIBSE Guide L - Sustainability
        /// Environmental impact, energy efficiency, low carbon design
        /// </summary>
        public static class GuideL_Sustainability
        {
            /// <summary>
            /// Energy performance targets for African buildings
            /// </summary>
            public static class EnergyPerformance
            {
                /// <summary>
                /// Gets energy use intensity (EUI) targets (kWh/m²/year)
                /// </summary>
                public static (double Good, double Typical, double Poor) 
                    GetEnergyUseIntensity(string buildingType)
                {
                    return buildingType.ToLower() switch
                    {
                        "office - naturally ventilated" => (50, 80, 120),
                        "office - air conditioned" => (120, 200, 300),
                        "school" => (40, 70, 100),
                        "hospital" => (200, 350, 500),
                        "hotel" => (150, 250, 400),
                        "retail" => (100, 200, 300),
                        "residential" => (30, 60, 100),
                        _ => (80, 150, 250)
                    };
                }

                /// <summary>
                /// Energy reduction strategies prioritized for Africa
                /// </summary>
                public static string[] GetEnergyReductionStrategies()
                {
                    return new[]
                    {
                        "1. PASSIVE DESIGN FIRST (lowest cost, highest impact)",
                        "   - Building orientation and form",
                        "   - Natural ventilation and daylighting",
                        "   - Solar shading and light colors",
                        "   - Thermal mass and insulation",
                        
                        "2. EFFICIENT SYSTEMS",
                        "   - High-efficiency HVAC (EER >3.0)",
                        "   - LED lighting throughout",
                        "   - Energy-efficient appliances",
                        "   - Variable speed drives (fans, pumps)",
                        
                        "3. CONTROLS AND MONITORING",
                        "   - Occupancy sensors (lights, AC)",
                        "   - Daylight sensors",
                        "   - BMS/BEMS for large buildings",
                        "   - Sub-metering for monitoring",
                        
                        "4. RENEWABLE ENERGY",
                        "   - Solar PV (excellent resource in Africa)",
                        "   - Solar water heating",
                        "   - Consider battery storage",
                        
                        "5. BEHAVIORAL CHANGE",
                        "   - User education and engagement",
                        "   - Energy awareness campaigns",
                        "   - Feedback on consumption"
                    };
                }
            }

            /// <summary>
            /// Carbon emissions and low carbon design
            /// </summary>
            public static class CarbonEmissions
            {
                /// <summary>
                /// Grid carbon intensity (kg CO2/kWh) for African countries
                /// Varies significantly based on generation mix
                /// </summary>
                public static Dictionary<string, double> GetGridCarbonIntensity()
                {
                    return new Dictionary<string, double>
                    {
                        { "Uganda", 0.15 },         // Mostly hydro (low carbon)
                        { "Kenya", 0.35 },          // Geothermal + hydro + thermal
                        { "Tanzania", 0.45 },       // Gas + hydro + diesel
                        { "Rwanda", 0.20 },         // Hydro + thermal
                        { "South Africa", 0.95 },   // Coal-dominated (very high)
                        { "Nigeria", 0.65 },        // Gas + diesel
                        { "Ghana", 0.40 },          // Hydro + gas + diesel
                        { "Ethiopia", 0.05 }        // Almost 100% hydro (very low)
                    };
                }

                /// <summary>
                /// Embodied carbon in materials (kg CO2/kg material)
                /// </summary>
                public static Dictionary<string, double> GetEmbodiedCarbon()
                {
                    return new Dictionary<string, double>
                    {
                        { "Cement (OPC)", 0.83 },
                        { "Concrete (C25)", 0.13 },
                        { "Steel (virgin)", 2.10 },
                        { "Steel (recycled)", 0.50 },
                        { "Aluminum", 8.00 },
                        { "Timber", 0.45 },
                        { "Brick", 0.23 },
                        { "Glass", 0.85 },
                        { "Insulation (mineral wool)", 1.35 }
                    };
                }
            }

            /// <summary>
            /// Water efficiency
            /// Critical in water-scarce African regions
            /// </summary>
            public static class WaterEfficiency
            {
                /// <summary>
                /// Water-efficient fixtures and fittings
                /// </summary>
                public static Dictionary<string, double> GetWaterEfficientFixtures()
                {
                    return new Dictionary<string, double>
                    {
                        { "WC - Standard flush (liters)", 9.0 },
                        { "WC - Dual flush (liters)", 6.0 },  // 6/3 liters
                        { "WC - Ultra-low flush (liters)", 4.5 },  // 4.5/3 liters
                        { "Tap/Faucet - Standard flow (L/min)", 10.0 },
                        { "Tap/Faucet - Water-efficient (L/min)", 6.0 },
                        { "Shower - Standard (L/min)", 12.0 },
                        { "Shower - Water-efficient (L/min)", 8.0 },
                        { "Urinal - Flush (liters)", 1.5 },
                        { "Urinal - Waterless (liters)", 0.0 }
                    };
                }

                /// <summary>
                /// Water saving strategies for Africa
                /// </summary>
                public static string[] GetWaterSavingStrategies()
                {
                    return new[]
                    {
                        "Rainwater harvesting - Mandatory in many African cities",
                        "Greywater recycling - Reuse for toilets and irrigation",
                        "Water-efficient fixtures - 30-50% reduction in consumption",
                        "Leak detection and repair - Prevent wastage",
                        "Native landscaping - No irrigation required",
                        "Drip irrigation - 40% more efficient than sprinklers",
                        "Sub-metering - Monitor and manage consumption",
                        "User education - Behavioral change important"
                    };
                }
            }
        }

        #endregion
    }
}
