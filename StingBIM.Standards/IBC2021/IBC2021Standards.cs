using System;
using System.Collections.Generic;

namespace StingBIM.Standards.IBC2021
{
    /// <summary>
    /// IBC 2021 - International Building Code
    /// Published by: International Code Council (ICC)
    /// Adoption: Primary building code for Liberia and many U.S. states
    /// 
    /// The IBC provides minimum requirements for building systems using
    /// prescriptive and performance-based provisions. It is founded on
    /// broad-based principles that make possible the use of new materials
    /// and new building designs.
    /// 
    /// Liberia officially adopted IBC as the national building code.
    /// </summary>
    public static class IBC2021Standards
    {
        #region Occupancy Classification (Chapter 3)

        /// <summary>
        /// IBC Occupancy Groups
        /// </summary>
        public enum OccupancyGroup
        {
            /// <summary>Group A - Assembly (A-1 through A-5)</summary>
            A_Assembly,
            /// <summary>Group B - Business</summary>
            B_Business,
            /// <summary>Group E - Educational</summary>
            E_Educational,
            /// <summary>Group F - Factory/Industrial (F-1, F-2)</summary>
            F_Factory,
            /// <summary>Group H - High Hazard (H-1 through H-5)</summary>
            H_HighHazard,
            /// <summary>Group I - Institutional (I-1 through I-4)</summary>
            I_Institutional,
            /// <summary>Group M - Mercantile</summary>
            M_Mercantile,
            /// <summary>Group R - Residential (R-1 through R-4)</summary>
            R_Residential,
            /// <summary>Group S - Storage (S-1, S-2)</summary>
            S_Storage,
            /// <summary>Group U - Utility/Miscellaneous</summary>
            U_Utility
        }

        /// <summary>
        /// Gets occupant load factor (sq ft per person) for occupancy
        /// </summary>
        public static double GetOccupantLoadFactor(OccupancyGroup group, string specificUse)
        {
            string use = specificUse.ToLower();
            
            // Assembly uses
            if (group == OccupancyGroup.A_Assembly)
            {
                if (use.Contains("concentrated") || use.Contains("chair")) return 7; // 7 sf/person
                if (use.Contains("standing")) return 5; // 5 sf/person
                if (use.Contains("unconcentrated")) return 15; // 15 sf/person
                return 7; // Default assembly
            }
            
            // Business
            if (group == OccupancyGroup.B_Business) return 150;
            
            // Educational
            if (group == OccupancyGroup.E_Educational)
            {
                if (use.Contains("classroom")) return 20;
                if (use.Contains("shop")) return 50;
                return 20;
            }
            
            // Residential
            if (group == OccupancyGroup.R_Residential) return 200;
            
            // Mercantile
            if (group == OccupancyGroup.M_Mercantile)
            {
                if (use.Contains("basement") || use.Contains("first floor")) return 30;
                return 60; // Upper floors
            }
            
            return 100; // Default
        }

        #endregion

        #region Construction Types (Chapter 6)

        /// <summary>
        /// IBC Construction Types
        /// </summary>
        public enum ConstructionType
        {
            /// <summary>Type IA - Fire-resistive (most restrictive)</summary>
            Type_IA,
            /// <summary>Type IB - Fire-resistive</summary>
            Type_IB,
            /// <summary>Type IIA - Noncombustible</summary>
            Type_IIA,
            /// <summary>Type IIB - Noncombustible</summary>
            Type_IIB,
            /// <summary>Type IIIA - Exterior walls noncombustible</summary>
            Type_IIIA,
            /// <summary>Type IIIB - Exterior walls noncombustible</summary>
            Type_IIIB,
            /// <summary>Type IV - Heavy Timber</summary>
            Type_IV_HT,
            /// <summary>Type VA - Combustible</summary>
            Type_VA,
            /// <summary>Type VB - Combustible (least restrictive)</summary>
            Type_VB
        }

        /// <summary>
        /// Gets fire resistance rating for structural elements (hours)
        /// Returns (Structural Frame, Bearing Walls, Floor/Ceiling, Roof/Ceiling)
        /// </summary>
        public static (int Frame, int BearingWalls, int FloorCeiling, int RoofCeiling) 
            GetFireResistanceRatings(ConstructionType type)
        {
            return type switch
            {
                ConstructionType.Type_IA => (3, 3, 2, 1),
                ConstructionType.Type_IB => (2, 2, 2, 1),
                ConstructionType.Type_IIA => (1, 1, 1, 1),
                ConstructionType.Type_IIB => (0, 0, 0, 0),
                ConstructionType.Type_IIIA => (1, 2, 1, 1),
                ConstructionType.Type_IIIB => (0, 2, 0, 0),
                ConstructionType.Type_IV_HT => (1, 2, 1, 1), // Heavy Timber
                ConstructionType.Type_VA => (1, 1, 1, 1),
                ConstructionType.Type_VB => (0, 0, 0, 0),
                _ => (0, 0, 0, 0)
            };
        }

        /// <summary>
        /// Gets maximum building height (feet) for construction type and occupancy
        /// </summary>
        public static int GetMaximumBuildingHeight(
            ConstructionType type, 
            OccupancyGroup occupancy,
            bool sprinklered)
        {
            // Base heights from Table 504.3 (simplified)
            int baseHeight = type switch
            {
                ConstructionType.Type_IA => 180,
                ConstructionType.Type_IB => 160,
                ConstructionType.Type_IIA => 65,
                ConstructionType.Type_IIB => 55,
                ConstructionType.Type_IIIA => 65,
                ConstructionType.Type_IIIB => 55,
                ConstructionType.Type_IV_HT => 65,
                ConstructionType.Type_VA => 50,
                ConstructionType.Type_VB => 40,
                _ => 40
            };

            // Sprinkler increase (20 feet)
            if (sprinklered) baseHeight += 20;

            return baseHeight;
        }

        #endregion

        #region Means of Egress (Chapter 10)

        /// <summary>
        /// Minimum corridor width (inches)
        /// </summary>
        public static int GetMinimumCorridorWidth(int occupantLoad)
        {
            if (occupantLoad >= 50)
                return 44; // 44 inches for ≥50 occupants
            else
                return 36; // 36 inches for <50 occupants
        }

        /// <summary>
        /// Required exit width (inches per person)
        /// </summary>
        public static double GetExitWidthPerPerson(bool isStair)
        {
            return isStair ? 0.3 : 0.2; // 0.3 in/person for stairs, 0.2 for other
        }

        /// <summary>
        /// Maximum exit access travel distance (feet)
        /// </summary>
        public static int GetMaximumTravelDistance(
            OccupancyGroup occupancy,
            bool sprinklered)
        {
            int baseDistance = occupancy switch
            {
                OccupancyGroup.A_Assembly => 200,
                OccupancyGroup.B_Business => 200,
                OccupancyGroup.E_Educational => 200,
                OccupancyGroup.F_Factory => 200,
                OccupancyGroup.H_HighHazard => 75,  // Reduced for high hazard
                OccupancyGroup.I_Institutional => 200,
                OccupancyGroup.M_Mercantile => 200,
                OccupancyGroup.R_Residential => 200,
                OccupancyGroup.S_Storage => 200,
                _ => 200
            };

            // Increase by 25% if sprinklered
            return sprinklered ? (int)(baseDistance * 1.25) : baseDistance;
        }

        /// <summary>
        /// Minimum number of exits required
        /// </summary>
        public static int GetMinimumExits(int occupantLoad, int stories)
        {
            if (occupantLoad > 500 || stories >= 4)
                return 3;
            else if (occupantLoad > 50 || stories >= 2)
                return 2;
            else
                return 1;
        }

        /// <summary>
        /// Stair riser and tread requirements (inches)
        /// </summary>
        public static (double MaxRiser, double MinTread) GetStairDimensions()
        {
            return (7.0, 11.0); // Max 7" riser, min 11" tread
        }

        #endregion

        #region Accessibility (Chapter 11)

        /// <summary>
        /// Minimum accessible route width (inches)
        /// </summary>
        public static int GetAccessibleRouteWidth()
        {
            return 36; // 36 inches minimum
        }

        /// <summary>
        /// Maximum accessible ramp slope (ratio)
        /// </summary>
        public static double GetMaximumRampSlope()
        {
            return 1.0 / 12.0; // 1:12 maximum slope
        }

        /// <summary>
        /// Number of accessible parking spaces required
        /// </summary>
        public static int GetAccessibleParkingSpaces(int totalSpaces)
        {
            if (totalSpaces <= 25) return 1;
            if (totalSpaces <= 50) return 2;
            if (totalSpaces <= 75) return 3;
            if (totalSpaces <= 100) return 4;
            if (totalSpaces <= 150) return 5;
            if (totalSpaces <= 200) return 6;
            if (totalSpaces <= 300) return 7;
            if (totalSpaces <= 400) return 8;
            if (totalSpaces <= 500) return 9;
            
            // 2% for 501-1000, then 20 + 1 per 100 over 1000
            if (totalSpaces <= 1000)
                return (int)Math.Ceiling(totalSpaces * 0.02);
            
            return 20 + (totalSpaces - 1000) / 100;
        }

        #endregion

        #region Interior Finishes (Chapter 8)

        /// <summary>
        /// Flame spread classifications for interior finishes
        /// </summary>
        public enum FlameSpreadClass
        {
            /// <summary>Class A - 0-25 flame spread</summary>
            ClassA,
            /// <summary>Class B - 26-75 flame spread</summary>
            ClassB,
            /// <summary>Class C - 76-200 flame spread</summary>
            ClassC
        }

        /// <summary>
        /// Gets required flame spread class for location
        /// </summary>
        public static FlameSpreadClass GetRequiredFlameSpread(
            OccupancyGroup occupancy,
            string location)
        {
            // Stricter requirements for exits and corridors
            if (location.ToLower().Contains("exit") || 
                location.ToLower().Contains("corridor"))
            {
                if (occupancy == OccupancyGroup.I_Institutional)
                    return FlameSpreadClass.ClassA;
                return FlameSpreadClass.ClassB;
            }

            // Rooms generally Class C acceptable
            return FlameSpreadClass.ClassC;
        }

        #endregion

        #region Fire Protection Systems (Chapter 9)

        /// <summary>
        /// Determines if automatic sprinkler system is required
        /// </summary>
        public static bool IsAutomaticSprinklerRequired(
            OccupancyGroup occupancy,
            double floorArea,
            int stories)
        {
            // Group A (Assembly)
            if (occupancy == OccupancyGroup.A_Assembly)
            {
                if (floorArea > 12000) return true; // >12,000 sf
                if (stories >= 2 && floorArea > 5000) return true;
            }

            // Group B (Business) - all stories >2
            if (occupancy == OccupancyGroup.B_Business && stories > 2)
                return true;

            // Group E (Educational) - all
            if (occupancy == OccupancyGroup.E_Educational)
                return true;

            // Group H (High Hazard) - all
            if (occupancy == OccupancyGroup.H_HighHazard)
                return true;

            // Group I (Institutional) - all
            if (occupancy == OccupancyGroup.I_Institutional)
                return true;

            // Group M (Mercantile) - >12,000 sf
            if (occupancy == OccupancyGroup.M_Mercantile && floorArea > 12000)
                return true;

            // Group R (Residential) - varies by type
            if (occupancy == OccupancyGroup.R_Residential)
            {
                // All Group R buildings require sprinklers per IBC 2021
                return true;
            }

            // Group S (Storage) - >12,000 sf
            if (occupancy == OccupancyGroup.S_Storage && floorArea > 12000)
                return true;

            return false;
        }

        /// <summary>
        /// Determines if fire alarm system is required
        /// </summary>
        public static bool IsFireAlarmRequired(
            OccupancyGroup occupancy,
            int occupantLoad,
            int stories)
        {
            // Group A - occupant load ≥300
            if (occupancy == OccupancyGroup.A_Assembly && occupantLoad >= 300)
                return true;

            // Group B - occupant load ≥500 or >3 stories
            if (occupancy == OccupancyGroup.B_Business)
            {
                if (occupantLoad >= 500 || stories > 3) return true;
            }

            // Group E - all
            if (occupancy == OccupancyGroup.E_Educational)
                return true;

            // Group I - all
            if (occupancy == OccupancyGroup.I_Institutional)
                return true;

            // Group R - varies by type, generally R-1, R-2 with >16 units
            if (occupancy == OccupancyGroup.R_Residential && stories > 2)
                return true;

            return false;
        }

        #endregion

        #region Liberia-Specific Implementation

        /// <summary>
        /// Liberia building permit authorities by county
        /// </summary>
        public static readonly Dictionary<string, string> LiberiaPermitAuthorities = 
            new Dictionary<string, string>
        {
            { "Montserrado", "Monrovia City Corporation - Building Department" },
            { "Margibi", "Margibi County Development Authority" },
            { "Grand Bassa", "Grand Bassa County Building Office" },
            { "Nimba", "Nimba County Development Office" },
            { "Bong", "Bong County Administration" }
        };

        /// <summary>
        /// Checks if development requires Environmental Protection Agency (EPA) clearance
        /// </summary>
        public static bool RequiresEPAClearance(
            double projectArea,
            string projectType,
            string location)
        {
            // Large developments
            if (projectArea > 5000) return true; // >5000 m²

            // Industrial or commercial in sensitive areas
            if (projectType.ToLower().Contains("industrial") ||
                projectType.ToLower().Contains("factory"))
                return true;

            // Coastal zone developments
            if (location.ToLower().Contains("coastal") ||
                location.ToLower().Contains("beach"))
                return true;

            return false;
        }

        /// <summary>
        /// Gets design wind speed for Liberia (mph)
        /// Based on coastal tropical climate
        /// </summary>
        public static int GetDesignWindSpeed(string location)
        {
            // Coastal areas higher wind
            if (location.ToLower().Contains("monrovia") ||
                location.ToLower().Contains("coastal"))
                return 130; // 130 mph for coastal

            // Inland areas
            return 110; // 110 mph for inland
        }

        /// <summary>
        /// Seismic design category for Liberia
        /// Liberia is in low seismic zone
        /// </summary>
        public static string GetSeismicDesignCategory()
        {
            return "A"; // SDC A - minimal seismic requirements
        }

        #endregion
    }
}
