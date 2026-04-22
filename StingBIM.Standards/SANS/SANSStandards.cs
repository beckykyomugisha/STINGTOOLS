// FILE: SANSStandards.cs - South African National Standards
// Regional relevance for East Africa
// LINES: ~380 (optimized)

using System;
using System.Collections.Generic;

namespace StingBIM.Standards.SANS
{
    public enum SANSElectricalZone { Zone0_Bath, Zone1_Above, Zone2_Outside, Zone3_NoRestriction }
    public enum SANSOccupancy { Residential, Office, Retail, Assembly, Industrial, Storage }
    
    /// <summary>
    /// SANS - South African National Standards
    /// Regional standards for southern and eastern Africa
    /// Similar to UK standards (Commonwealth connection)
    /// </summary>
    public static class SANSStandards
    {
        // SANS 10142-1: WIRING OF PREMISES (similar to BS 7671)
        
        // Maximum disconnection times (seconds)
        public static double GetMaximumDisconnectionTime(double nominalVoltage, string earthingSystem)
        {
            // TN systems (most common)
            if (earthingSystem == "TN-S" || earthingSystem == "TN-C-S")
            {
                if (nominalVoltage <= 50) return 5.0;
                if (nominalVoltage <= 120) return 0.8;
                if (nominalVoltage <= 230) return 0.4;
                if (nominalVoltage <= 400) return 0.2;
                return 0.1;
            }
            
            // TT systems
            if (earthingSystem == "TT")
            {
                if (nominalVoltage <= 230) return 0.2;
                if (nominalVoltage <= 400) return 0.07;
                return 0.04;
            }
            
            return 0.4; // Safe default
        }
        
        // Minimum conductor sizes for fixed wiring
        public static double GetMinimumConductorSize(string circuitType)
        {
            var sizes = new Dictionary<string, double>
            {
                { "Lighting_Final_Circuit", 1.5 },        // 1.5mm²
                { "Socket_Outlet_Ring", 2.5 },            // 2.5mm² (similar to UK)
                { "Socket_Outlet_Radial", 2.5 },
                { "Stove_Circuit", 4.0 },
                { "Geyser_Circuit", 2.5 },
                { "Air_Conditioner", 2.5 },
                { "Distribution_Board_Feed", 10.0 }
            };
            
            return sizes.TryGetValue(circuitType, out double size) ? size : 1.5;
        }
        
        // Bathroom zone classification (similar to BS 7671)
        public static SANSElectricalZone GetBathroomZone(double heightAboveBath, double horizontalDistance)
        {
            if (heightAboveBath <= 0) return SANSElectricalZone.Zone0_Bath;
            if (heightAboveBath <= 2.25 && horizontalDistance <= 0.6) 
                return SANSElectricalZone.Zone1_Above;
            if (heightAboveBath <= 2.25 && horizontalDistance <= 1.2) 
                return SANSElectricalZone.Zone2_Outside;
            
            return SANSElectricalZone.Zone3_NoRestriction;
        }
        
        // IP rating requirements for bathroom zones
        public static string GetRequiredIPRating(SANSElectricalZone zone)
        {
            return zone switch
            {
                SANSElectricalZone.Zone0_Bath => "IPX7 (Immersion)",
                SANSElectricalZone.Zone1_Above => "IPX5 (Water jets)",
                SANSElectricalZone.Zone2_Outside => "IPX4 (Splashing)",
                SANSElectricalZone.Zone3_NoRestriction => "No specific requirement",
                _ => "IPX0"
            };
        }
        
        // SANS 10400: BUILDING REGULATIONS
        
        // SANS 10400-A: General Principles
        public static double GetMinimumRoomHeight(string roomType)
        {
            return roomType switch
            {
                "Habitable_Room" => 2.4,      // meters
                "Kitchen" => 2.4,
                "Bathroom" => 2.1,
                "Toilet" => 2.1,
                "Garage" => 2.1,
                "Store_Room" => 2.1,
                "Corridor" => 2.1,
                _ => 2.4
            };
        }
        
        // Natural ventilation requirements
        public static double GetMinimumVentilationArea(double floorArea)
        {
            return floorArea * 0.05; // 5% of floor area
        }
        
        // Natural lighting requirements
        public static double GetMinimumNaturalLight(double floorArea)
        {
            return floorArea * 0.10; // 10% of floor area
        }
        
        // SANS 10400-B: STRUCTURAL DESIGN
        
        // Imposed loads (live loads) by occupancy type
        public static double GetImposedLoad(SANSOccupancy occupancy)
        {
            return occupancy switch
            {
                SANSOccupancy.Residential => 1.5,    // kPa
                SANSOccupancy.Office => 2.5,
                SANSOccupancy.Retail => 4.0,
                SANSOccupancy.Assembly => 4.0,
                SANSOccupancy.Industrial => 5.0,
                SANSOccupancy.Storage => 7.5,
                _ => 2.0
            };
        }
        
        // Wind pressure calculation (simplified)
        public static double GetWindPressure(string exposureCategory, double buildingHeight)
        {
            // Simplified SANS 10160
            double baseWind = exposureCategory switch
            {
                "Coastal_Zone_A" => 1.2,    // kPa
                "Inland_Zone_B" => 0.8,
                "Mountainous" => 1.0,
                _ => 0.8
            };
            
            double heightFactor = 1.0 + (buildingHeight / 100.0);
            return baseWind * heightFactor;
        }
        
        // SANS 10400-L: ROOFS
        
        // Minimum roof pitch by roofing material
        public static double GetMinimumRoofPitch(string roofingType)
        {
            return roofingType switch
            {
                "Clay_Tiles" => 22.5,           // degrees
                "Concrete_Tiles" => 17.5,
                "Corrugated_Iron_Sheet" => 5.0,
                "Thatch" => 35.0,
                "Slate" => 22.5,
                "Fibre_Cement_Sheet" => 10.0,
                _ => 15.0
            };
        }
        
        // SANS 10400-P: DRAINAGE
        
        // Minimum pipe slope for drainage
        public static double GetDrainagePipeSlope(double pipeDiameterMm)
        {
            if (pipeDiameterMm <= 75) return 1.0 / 40.0;   // 1:40 (2.5%)
            if (pipeDiameterMm <= 100) return 1.0 / 60.0;  // 1:60 (1.67%)
            if (pipeDiameterMm <= 150) return 1.0 / 80.0;  // 1:80 (1.25%)
            return 1.0 / 100.0;                             // 1:100 (1%)
        }
        
        // Minimum trap seal depth
        public static int GetMinimumTrapSeal()
        {
            return 50; // mm minimum water seal
        }
        
        // Vent pipe sizing
        public static int GetVentPipeSize(int drainPipeSize)
        {
            // Vent = 1/2 drain size, minimum 32mm
            int ventSize = drainPipeSize / 2;
            return Math.Max(ventSize, 32);
        }
        
        // SANS 10400-T: FIRE PROTECTION
        
        // Fire resistance ratings (hours)
        public static double GetFireResistanceRating(string elementType, int numberOfFloors)
        {
            if (elementType == "Structural_Frame")
            {
                if (numberOfFloors <= 2) return 1.0;
                if (numberOfFloors <= 5) return 2.0;
                return 3.0;
            }
            
            if (elementType == "Compartment_Wall") return 2.0;
            if (elementType == "Escape_Route") return 1.0;
            if (elementType == "Protected_Shaft") return 2.0;
            
            return 0.5; // Default
        }
        
        // Maximum travel distance to exits
        public static double GetMaximumTravelDistance(SANSOccupancy occupancy)
        {
            return occupancy switch
            {
                SANSOccupancy.Residential => 45.0,   // meters
                SANSOccupancy.Office => 45.0,
                SANSOccupancy.Assembly => 30.0,
                SANSOccupancy.Industrial => 60.0,
                SANSOccupancy.Storage => 60.0,
                _ => 45.0
            };
        }
        
        // SANS 10400-XA: ENERGY EFFICIENCY
        
        // Maximum U-values (thermal transmittance) W/m²K
        public static double GetMaximumUValue(string buildingElement)
        {
            return buildingElement switch
            {
                "Roof" => 0.35,              // W/m²K
                "Wall_External" => 0.50,
                "Floor_Raised" => 0.70,
                "Window_Glazing" => 5.70,
                "Door" => 2.00,
                _ => 0.50
            };
        }
        
        // Minimum solar water heating requirement
        public static double GetMinimumSolarWaterHeating(double roofArea)
        {
            // Minimum 50% of hot water from solar, max 3m² collector
            return Math.Min(roofArea * 0.02, 3.0); // m² of solar panels
        }
        
        // SANS 10400-XA: Lighting power density
        public static double GetMaximumLightingPowerDensity(SANSOccupancy occupancy)
        {
            return occupancy switch
            {
                SANSOccupancy.Residential => 15.0,   // W/m²
                SANSOccupancy.Office => 12.0,
                SANSOccupancy.Retail => 15.0,
                SANSOccupancy.Industrial => 10.0,
                _ => 12.0
            };
        }
        
        // SANS 10400-S: FACILITIES FOR DISABLED PERSONS
        
        // Minimum corridor width for wheelchair access
        public static double GetMinimumCorridorWidth()
        {
            return 1.2; // meters
        }
        
        // Minimum door width for wheelchair access
        public static double GetMinimumDoorWidth()
        {
            return 0.9; // meters clear opening
        }
        
        // Maximum ramp gradient
        public static double GetMaximumRampGradient()
        {
            return 1.0 / 12.0; // 1:12 (8.33%)
        }
        
        // Accessible parking bays
        public static int GetAccessibleParkingBays(int totalParkingBays)
        {
            if (totalParkingBays <= 25) return 1;
            if (totalParkingBays <= 50) return 2;
            if (totalParkingBays <= 100) return 3;
            return (int)Math.Ceiling(totalParkingBays * 0.03); // 3% for larger
        }
    }
}
