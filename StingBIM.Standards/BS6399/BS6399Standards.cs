using System;
using System.Collections.Generic;

namespace StingBIM.Standards.BS6399
{
    /// <summary>
    /// BS 6399 - Loading for Buildings
    /// British Standard for structural loading
    /// Widely used in Uganda, Kenya, Tanzania, and East African Commonwealth countries
    /// 
    /// Parts:
    /// - BS 6399-1: Code of practice for dead and imposed loads
    /// - BS 6399-2: Code of practice for wind loads  
    /// - BS 6399-3: Code of practice for imposed roof loads
    /// 
    /// Note: Being superseded by Eurocodes (EN 1991) but still widely referenced
    /// </summary>
    public static class BS6399Standards
    {
        #region BS 6399-1: Dead and Imposed Loads

        /// <summary>
        /// Unit weights of common building materials (kN/m³)
        /// </summary>
        public static Dictionary<string, double> GetMaterialDensities()
        {
            return new Dictionary<string, double>
            {
                // Concrete and masonry
                { "Reinforced concrete (normal weight)", 24.0 },
                { "Lightweight concrete (1600 kg/m³)", 17.0 },
                { "Brickwork (clay)", 22.0 },
                { "Blockwork (concrete)", 20.0 },
                { "Blockwork (lightweight)", 12.0 },
                { "Stone masonry", 27.0 },
                
                // Steel and metals
                { "Structural steel", 77.0 },
                { "Aluminium", 27.0 },
                { "Copper", 87.0 },
                
                // Timber
                { "Hardwood", 10.0 },
                { "Softwood", 6.0 },
                { "Plywood", 7.0 },
                
                // Miscellaneous
                { "Asphalt roofing", 23.0 },
                { "Glass", 25.0 },
                { "Plaster (gypsum)", 13.0 },
                { "Soil (dry)", 17.0 },
                { "Water", 10.0 }
            };
        }

        /// <summary>
        /// Imposed floor loads for buildings (kN/m²)
        /// </summary>
        public static double GetImposedFloorLoad(string occupancyType, string location = "")
        {
            string occ = occupancyType.ToLower();
            
            // Residential
            if (occ.Contains("dwelling") || occ.Contains("residential"))
            {
                if (location.ToLower().Contains("bedroom")) return 1.5;
                if (location.ToLower().Contains("bathroom")) return 1.5;
                return 2.0; // General residential
            }
            
            // Offices
            if (occ.Contains("office"))
            {
                if (occ.Contains("general")) return 2.5;
                if (occ.Contains("filing") || occ.Contains("storage")) return 5.0;
                return 2.5;
            }
            
            // Education
            if (occ.Contains("classroom")) return 3.0;
            if (occ.Contains("assembly") || occ.Contains("hall")) return 5.0;
            
            // Healthcare
            if (occ.Contains("hospital"))
            {
                if (location.ToLower().Contains("ward")) return 2.0;
                if (location.ToLower().Contains("operating")) return 3.0;
                return 2.0;
            }
            
            // Retail
            if (occ.Contains("shop") || occ.Contains("retail"))
            {
                if (occ.Contains("general")) return 4.0;
                if (occ.Contains("storage")) return 6.0;
                return 4.0;
            }
            
            // Assembly
            if (occ.Contains("assembly"))
            {
                if (occ.Contains("fixed seat")) return 4.0;
                if (occ.Contains("movable")) return 5.0;
                return 5.0;
            }
            
            // Storage and industrial
            if (occ.Contains("storage"))
            {
                if (occ.Contains("general")) return 6.0;
                if (occ.Contains("warehouse")) return 7.5;
                if (occ.Contains("file") || occ.Contains("archive")) return 10.0;
                return 6.0;
            }
            
            if (occ.Contains("industrial"))
            {
                if (occ.Contains("light")) return 5.0;
                if (occ.Contains("heavy")) return 10.0;
                return 7.5;
            }
            
            // Parking
            if (occ.Contains("parking") || occ.Contains("garage"))
            {
                if (occ.Contains("car")) return 2.5;
                if (occ.Contains("truck") || occ.Contains("lorry")) return 7.5;
                return 2.5;
            }
            
            return 3.0; // Default conservative value
        }

        /// <summary>
        /// Concentrated loads for floors (kN)
        /// </summary>
        public static double GetConcentratedLoad(string occupancyType)
        {
            return occupancyType.ToLower() switch
            {
                var s when s.Contains("residential") => 1.4,
                var s when s.Contains("office") => 2.7,
                var s when s.Contains("assembly") => 3.6,
                var s when s.Contains("retail") => 3.6,
                var s when s.Contains("storage") => 4.5,
                var s when s.Contains("parking") => 9.0, // For wheel loads
                _ => 2.7
            };
        }

        /// <summary>
        /// Load reduction factors for large areas
        /// </summary>
        public static double GetAreaReductionFactor(
            double loadedArea,
            string occupancyType,
            int numberOfFloors)
        {
            // Area reduction for offices, not applicable to storage or assembly
            if (occupancyType.ToLower().Contains("storage") ||
                occupancyType.ToLower().Contains("assembly"))
                return 1.0; // No reduction

            // For offices and similar
            if (loadedArea < 40) return 1.0; // No reduction for small areas
            
            double areaFactor = 0.7 + 12.0 / Math.Sqrt(loadedArea);
            if (areaFactor > 1.0) areaFactor = 1.0;
            if (areaFactor < 0.5) areaFactor = 0.5; // Minimum 50% reduction
            
            return areaFactor;
        }

        #endregion

        #region BS 6399-2: Wind Loads

        /// <summary>
        /// Basic wind speed for major East African cities (m/s)
        /// Based on 50-year return period
        /// </summary>
        public static double GetBasicWindSpeed(string location)
        {
            return location.ToLower() switch
            {
                // Uganda
                var s when s.Contains("kampala") => 22.0,
                var s when s.Contains("entebbe") => 24.0,
                var s when s.Contains("jinja") => 22.0,
                var s when s.Contains("gulu") => 21.0,
                
                // Kenya
                var s when s.Contains("nairobi") => 24.0,
                var s when s.Contains("mombasa") => 28.0, // Coastal
                var s when s.Contains("kisumu") => 22.0,
                var s when s.Contains("eldoret") => 23.0,
                
                // Tanzania
                var s when s.Contains("dar es salaam") => 30.0, // Coastal
                var s when s.Contains("dodoma") => 23.0,
                var s when s.Contains("arusha") => 22.0,
                var s when s.Contains("mwanza") => 24.0,
                
                // Rwanda
                var s when s.Contains("kigali") => 21.0,
                
                // Default for region
                _ => 25.0 // Conservative default
            };
        }

        /// <summary>
        /// Terrain and building factor
        /// </summary>
        public enum TerrainCategory
        {
            /// <summary>Sea, coastal areas, flat country</summary>
            CategoryA,
            /// <summary>Farmland with windbreaks, country with scattered buildings</summary>
            CategoryB,
            /// <summary>Suburban areas, forests</summary>
            CategoryC,
            /// <summary>Urban areas with tall buildings</summary>
            CategoryD
        }

        /// <summary>
        /// Gets terrain and height factor
        /// </summary>
        public static double GetTerrainFactor(
            TerrainCategory terrain,
            double heightAboveGround)
        {
            // Simplified terrain factors
            double baseFactor = terrain switch
            {
                TerrainCategory.CategoryA => 1.00,
                TerrainCategory.CategoryB => 0.86,
                TerrainCategory.CategoryC => 0.73,
                TerrainCategory.CategoryD => 0.64,
                _ => 0.86
            };

            // Height adjustment factor
            double heightFactor = 1.0;
            if (heightAboveGround > 10)
            {
                heightFactor = Math.Pow(heightAboveGround / 10.0, 0.2);
            }

            return baseFactor * heightFactor;
        }

        /// <summary>
        /// Pressure coefficients for building shapes
        /// </summary>
        public static (double Windward, double Leeward, double Side) GetPressureCoefficients(
            string buildingShape,
            double heightToWidth)
        {
            // Rectangular buildings
            if (buildingShape.ToLower().Contains("rectangular"))
            {
                double windward = 0.8;
                double leeward = -0.5;
                double side = -0.7;
                
                return (windward, leeward, side);
            }
            
            // Cylindrical buildings
            if (buildingShape.ToLower().Contains("cylindrical"))
            {
                return (0.7, -0.4, -0.5);
            }
            
            // Default
            return (0.8, -0.5, -0.7);
        }

        /// <summary>
        /// Dynamic augmentation factor for slender structures
        /// </summary>
        public static double GetDynamicFactor(double heightToWidth)
        {
            if (heightToWidth > 5)
                return 1.0 + 0.05 * (heightToWidth - 5); // Increase for slender
            else
                return 1.0; // No augmentation for stocky buildings
        }

        /// <summary>
        /// Calculates design wind pressure (kN/m²)
        /// </summary>
        public static double CalculateWindPressure(
            string location,
            TerrainCategory terrain,
            double height,
            string buildingShape,
            double heightToWidth)
        {
            double basicWindSpeed = GetBasicWindSpeed(location);
            double terrainFactor = GetTerrainFactor(terrain, height);
            double dynamicFactor = GetDynamicFactor(heightToWidth);
            
            double designWindSpeed = basicWindSpeed * terrainFactor * dynamicFactor;
            
            // Wind pressure q = 0.613 * V² (N/m²) where V is in m/s
            double pressure = 0.000613 * designWindSpeed * designWindSpeed; // kN/m²
            
            return pressure;
        }

        #endregion

        #region BS 6399-3: Imposed Roof Loads

        /// <summary>
        /// Imposed roof loads for maintenance access (kN/m²)
        /// </summary>
        public static double GetRoofImposedLoad(string roofType, double slope)
        {
            // Flat roofs (slope < 10°)
            if (slope < 10)
            {
                if (roofType.ToLower().Contains("accessible") || 
                    roofType.ToLower().Contains("terrace"))
                    return 1.5; // As per floor use
                else
                    return 0.6; // Maintenance access only
            }
            
            // Sloped roofs (slope ≥ 10°)
            if (slope < 30)
                return 0.6; // Reduced for slope
            else if (slope < 60)
                return 0.6 * (60 - slope) / 30.0; // Linear reduction
            else
                return 0.0; // Too steep for access
        }

        /// <summary>
        /// Snow loads for East African highlands (kN/m²)
        /// </summary>
        public static double GetSnowLoad(string location, double altitude)
        {
            // Most of East Africa doesn't experience significant snow
            // Only highlands above 3000m
            if (altitude < 3000) return 0.0;
            
            // Highland areas (Mount Kenya, Kilimanjaro, Rwenzori)
            if (altitude > 4000)
            {
                // Ground snow load = 0.4 + 0.0002 * (altitude - 3000)
                double groundLoad = 0.4 + 0.0002 * (altitude - 3000);
                return groundLoad * 0.8; // Roof snow load factor
            }
            
            return 0.2; // Minimal for high altitude areas
        }

        /// <summary>
        /// Minimum point load for roofs (maintenance personnel)
        /// </summary>
        public static double GetRoofPointLoad()
        {
            return 0.9; // 0.9 kN concentrated load
        }

        #endregion

        #region Load Combinations

        /// <summary>
        /// Ultimate limit state load factors
        /// </summary>
        public static (double Dead, double Imposed, double Wind) GetULSLoadFactors(
            string loadCase)
        {
            return loadCase.ToLower() switch
            {
                "permanent and variable" => (1.4, 1.6, 0.0),
                "permanent and wind" => (1.2, 0.0, 1.2),
                "permanent, variable and wind" => (1.2, 1.2, 1.2),
                _ => (1.4, 1.6, 0.0)
            };
        }

        /// <summary>
        /// Serviceability limit state load factors
        /// </summary>
        public static (double Dead, double Imposed, double Wind) GetSLSLoadFactors()
        {
            return (1.0, 1.0, 1.0); // No factors for SLS
        }

        #endregion

        #region East Africa Specific Adjustments

        /// <summary>
        /// Gets climate adjustment factors for East Africa
        /// </summary>
        public static double GetClimateAdjustment(string climateZone)
        {
            return climateZone.ToLower() switch
            {
                "coastal" => 1.1,  // Increased for tropical storms
                "highland" => 1.0, // Standard
                "lake region" => 1.05, // Slightly increased
                _ => 1.0
            };
        }

        /// <summary>
        /// Checks if cyclone/hurricane provisions apply
        /// </summary>
        public static bool RequiresCycloneDesign(string location)
        {
            // Coastal areas of Tanzania, Kenya require cyclone consideration
            if (location.ToLower().Contains("mombasa") ||
                location.ToLower().Contains("dar es salaam") ||
                location.ToLower().Contains("tanga") ||
                location.ToLower().Contains("kilifi") ||
                location.ToLower().Contains("lamu"))
                return true;
            
            return false;
        }

        #endregion
    }
}
