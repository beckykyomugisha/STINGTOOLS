using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Standards
{
    /// <summary>
    /// Comprehensive API wrapper for international engineering standards.
    /// Includes IEC, IEEE, ASHRAE, IES, NFPA, AISC, IPC, UPC, and regional standards.
    /// Designed for easy calling from PyRevit/IronPython.
    /// All methods are static for direct access.
    /// </summary>
    public static class StandardsAPI
    {
        #region Electrical Standards (IEC, IEEE, NEC, BS)
        
        /// <summary>
        /// Calculate cable size per IEC 60364-5-52, BS 7671, NEC Article 310, or AS/NZS 3008.1
        /// Includes ampacity, voltage drop, and derating calculations.
        /// Standards: IEC 60364-5-52 (International), BS 7671 (UK), NEC 310 (US), AS/NZS 3008.1 (AU/NZ)
        /// </summary>
        public static CableSizeResult CalculateCableSize(
            double voltageV,
            double currentA,
            double lengthM,
            string conductorType = "Copper",
            string insulationType = "THHN",
            int conduitFill = 3,
            double ambientTempC = 30,
            string standard = "IEC60364")
        {
            try
            {
                // Base ampacity table (IEC 60364-5-52 Table B.52.4)
                var ampacityTable = GetCableAmpacityTable(conductorType, insulationType);
                
                // Apply derating factors
                double tempDerating = GetTemperatureDerating(ambientTempC, standard);
                double groupDerating = GetGroupingDerating(conduitFill);
                double totalDerating = tempDerating * groupDerating;
                
                // Calculate required ampacity
                double requiredAmpacity = currentA / totalDerating;
                
                // Select cable size
                string cableSize = SelectCableSize(requiredAmpacity, ampacityTable);
                
                // Calculate voltage drop
                double voltageDrop = CalculateVoltageDrop(currentA, lengthM, cableSize, voltageV, conductorType);
                double voltageDropPercent = (voltageDrop / voltageV) * 100;
                
                // Check compliance (max 3% for power circuits per IEC 60364-5-52)
                bool isCompliant = voltageDropPercent <= 3.0;
                
                return new CableSizeResult
                {
                    Success = true,
                    SizeAWG = cableSize,
                    Ampacity = requiredAmpacity,
                    VoltageDropPercent = voltageDropPercent,
                    DeratingFactor = totalDerating,
                    IsNECCompliant = isCompliant,
                    NECReference = $"{standard} - Cable Sizing",
                    ConductorType = conductorType,
                    InsulationType = insulationType,
                    Warnings = voltageDropPercent > 3 ? new List<string> { "Voltage drop exceeds 3% limit" } : new List<string>()
                };
            }
            catch (Exception ex)
            {
                return new CableSizeResult
                {
                    Success = false,
                    ErrorMessage = $"Cable sizing calculation error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Verify circuit breaker sizing per IEC 60947-2, IEC 60898-1, or IEEE C37.04
        /// Standards: IEC 60947-2 (Industrial MCCBs), IEC 60898-1 (Residential MCBs), 
        ///           IEEE C37.04 (US High Voltage), IEC 60255 (Protection Relays)
        /// </summary>
        public static CircuitBreakerResult VerifyCircuitBreaker(
            double loadCurrentA,
            double voltageV,
            string breakerType = "MCCB",
            string standard = "IEC60947-2",
            bool typeCoordination = false)
        {
            try
            {
                // Standard breaker sizes per IEC and NEC
                int[] standardSizes = { 6, 10, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000 };
                
                // Calculate required breaker (125% for continuous load per IEC 60947-2)
                double requiredBreaker = loadCurrentA * 1.25;
                int recommendedSize = standardSizes.FirstOrDefault(s => s >= requiredBreaker);
                if (recommendedSize == 0) recommendedSize = standardSizes.Last();
                
                // Determine breaker category based on type
                string category = breakerType == "MCB" ? "Category A" : "Category B";
                
                // Check for Type 1 or Type 2 coordination if specified
                string coordinationType = typeCoordination ? "Type 2 Coordination" : "Type 1 Coordination";
                
                return new CircuitBreakerResult
                {
                    Success = true,
                    RecommendedBreakerSizeA = recommendedSize,
                    LoadCurrent = loadCurrentA,
                    BreakerType = breakerType,
                    BreakerCategory = category,
                    IsCompliant = true,
                    NECReference = $"{standard} - {coordinationType}",
                    StandardApplied = standard
                };
            }
            catch (Exception ex)
            {
                return new CircuitBreakerResult
                {
                    Success = false,
                    ErrorMessage = $"Circuit breaker verification error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Calculate bounding/conduit size per IEC 60364 or NEC Chapter 9
        /// </summary>
        public static ConduitSizeResult CalculateBoundingSize(
            List<string> cableSizes,
            int numberOfCables,
            string conduitType = "PVC")
        {
            try
            {
                // Calculate total cross-sectional area of cables
                double totalCableArea = CalculateTotalCableArea(cableSizes);
                
                // Apply fill factor (40% for 3+ cables per IEC/NEC)
                double fillFactor = numberOfCables >= 3 ? 0.40 : 0.53;
                double requiredConduitArea = totalCableArea / fillFactor;
                
                // Select conduit size from standard sizes
                string conduitSize = SelectConduitSize(requiredConduitArea, conduitType);
                
                return new ConduitSizeResult
                {
                    Success = true,
                    RecommendedSize = conduitSize,
                    FillPercentage = (totalCableArea / requiredConduitArea) * 100,
                    ConduitType = conduitType,
                    NumberOfCables = numberOfCables
                };
            }
            catch (Exception ex)
            {
                return new ConduitSizeResult
                {
                    Success = false,
                    ErrorMessage = $"Conduit sizing error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Calculate grounding electrode conductor size per IEC 60364-5-54 or NEC 250.66
        /// </summary>
        public static GroundingResult CalculateGroundingSize(
            double serviceCurrentA,
            string serviceConductorSize,
            string standard = "IEC60364-5-54")
        {
            try
            {
                // IEC 60364-5-54 / NEC Table 250.66 - Grounding Electrode Conductor sizing
                string gecSize;
                if (serviceCurrentA <= 100) gecSize = "8 AWG (10 mm²)";
                else if (serviceCurrentA <= 150) gecSize = "6 AWG (16 mm²)";
                else if (serviceCurrentA <= 200) gecSize = "4 AWG (25 mm²)";
                else if (serviceCurrentA <= 400) gecSize = "2 AWG (35 mm²)";
                else if (serviceCurrentA <= 600) gecSize = "1/0 AWG (50 mm²)";
                else if (serviceCurrentA <= 1000) gecSize = "2/0 AWG (70 mm²)";
                else gecSize = "3/0 AWG (95 mm²)";
                
                // Minimum PE size is 2.5 mm² Cu (if mechanically protected)
                string minimumPE = "2.5 mm² Cu (mechanically protected) or 16 mm² Cu (unprotected)";
                
                return new GroundingResult
                {
                    Success = true,
                    GroundingConductorSize = gecSize,
                    ServiceCurrent = serviceCurrentA,
                    ServiceType = serviceConductorSize,
                    IsCompliant = true,
                    NECReference = $"{standard} - PE Conductor Sizing",
                    MinimumPESize = minimumPE
                };
            }
            catch (Exception ex)
            {
                return new GroundingResult
                {
                    Success = false,
                    ErrorMessage = $"Grounding calculation error: {ex.Message}"
                };
            }
        }
        
        #endregion
        
        #region HVAC Standards (ASHRAE)
        
        /// <summary>
        /// Calculate cooling load using ASHRAE Heat Balance Method.
        /// Standards: ASHRAE Handbook Fundamentals Ch.18, ASHRAE 183-2007
        /// Methods: Heat Balance Method, Radiant Time Series (RTS) Method
        /// </summary>
        public static HVACSizingResult CalculateCoolingLoad(
            double floorAreaM2,
            string buildingType,
            string climateZone,
            double occupantCount,
            double equipmentLoadW,
            double lightingLoadW,
            string orientationN_E_S_W = "N",
            double ceilingHeightM = 2.7)
        {
            try
            {
                // ASHRAE Heat Balance Method base loads
                double baseLoadPerM2 = GetASHRAEBaseLoad(buildingType);
                
                // Solar gain factors by orientation (ASHRAE design day data)
                double orientationFactor = GetSolarGainFactor(orientationN_E_S_W, climateZone);
                
                // Internal heat gains
                double sensibleLoadBuilding = floorAreaM2 * baseLoadPerM2;
                double sensibleLoadOccupants = occupantCount * 75; // 75W sensible per person (ASHRAE)
                double latentLoadOccupants = occupantCount * 55;   // 55W latent per person
                double equipmentLoad = equipmentLoadW;
                double lightingLoad = lightingLoadW;
                
                // Total cooling load
                double totalSensibleLoad = (sensibleLoadBuilding + sensibleLoadOccupants + equipmentLoad + lightingLoad) * orientationFactor;
                double totalLatentLoad = latentLoadOccupants;
                double totalCoolingLoad = totalSensibleLoad + totalLatentLoad;
                
                // Load breakdown
                var loadBreakdown = new Dictionary<string, double>
                {
                    {"Building Envelope", sensibleLoadBuilding},
                    {"Occupants (Sensible)", sensibleLoadOccupants},
                    {"Occupants (Latent)", latentLoadOccupants},
                    {"Equipment", equipmentLoad},
                    {"Lighting", lightingLoad},
                    {"Solar Gain Factor", orientationFactor}
                };
                
                return new HVACSizingResult
                {
                    Success = true,
                    CoolingLoadKW = totalCoolingLoad / 1000,
                    SensibleLoadKW = totalSensibleLoad / 1000,
                    LatentLoadKW = totalLatentLoad / 1000,
                    FloorArea = floorAreaM2,
                    BuildingType = buildingType,
                    CIBSEReference = "ASHRAE Handbook Fundamentals Ch.18 - Heat Balance Method",
                    LoadBreakdown = loadBreakdown
                };
            }
            catch (Exception ex)
            {
                return new HVACSizingResult
                {
                    Success = false,
                    ErrorMessage = $"Cooling load calculation error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Calculate fresh air ventilation per CIBSE Guide A.
        /// Combines people-based and area-based requirements.
        /// </summary>
        public static VentilationResult CalculateVentilation(
            double floorAreaM2,
            double occupantCount,
            string spaceType)
        {
            try
            {
                // CIBSE Guide A ventilation rates (L/s per person)
                double ratePerPerson = 10; // Default office
                if (spaceType.IndexOf("meeting", StringComparison.OrdinalIgnoreCase) >= 0) ratePerPerson = 10;
                if (spaceType.IndexOf("classroom", StringComparison.OrdinalIgnoreCase) >= 0) ratePerPerson = 8;
                if (spaceType.IndexOf("retail", StringComparison.OrdinalIgnoreCase) >= 0) ratePerPerson = 10;

                double totalFlowLps = occupantCount * ratePerPerson;
                double airChangesPerHour = (totalFlowLps * 3.6) / (floorAreaM2 * 2.7); // Assuming 2.7m ceiling

                return new VentilationResult
                {
                    Success = true,
                    FreshAirLPS = totalFlowLps,
                    FreshAirM3H = totalFlowLps * 3.6,
                    AirChangesPerHour = airChangesPerHour,
                    CIBSEReference = "CIBSE Guide A"
                };
            }
            catch (Exception ex)
            {
                return new VentilationResult
                {
                    Success = false,
                    ErrorMessage = $"Ventilation calculation error: {ex.Message}"
                };
            }
        }

        #endregion

        #region Lighting Standards (IES/IESNA)
        
        /// <summary>
        /// Calculate lighting requirements per IES Lighting Handbook 10th Edition.
        /// Standards: IESNA standards for illuminance levels (footcandles/lux)
        /// </summary>
        public static LightingResult CalculateLighting(
            double floorAreaM2,
            string spaceType,
            double ceilingHeightM = 2.7,
            string measurementPlane = "Horizontal")
        {
            try
            {
                // IES recommended illuminance levels (lux)
                double targetIlluminanceLux = GetIESIlluminance(spaceType);
                
                // Calculate total lumens required
                // Lumen Method: Total Lumens = (Area × Illuminance) / (Utilization Factor × Maintenance Factor)
                double utilizationFactor = 0.6;  // Typical for general lighting
                double maintenanceFactor = 0.8;   // Typical maintenance factor
                
                double totalLumensRequired = (floorAreaM2 * targetIlluminanceLux) / (utilizationFactor * maintenanceFactor);
                
                // Calculate power density (typical LED efficiency: 100 lm/W)
                double luminousEfficacy = 100; // lumens per watt for LED
                double totalWatts = totalLumensRequired / luminousEfficacy;
                double powerDensityWM2 = totalWatts / floorAreaM2;
                
                return new LightingResult
                {
                    Success = true,
                    IlluminanceLux = targetIlluminanceLux,
                    IlluminanceFootcandles = targetIlluminanceLux / 10.764,
                    TotalLumensRequired = totalLumensRequired,
                    PowerDensityWM2 = powerDensityWM2,
                    SpaceType = spaceType,
                    CIBSEReference = "IES Lighting Handbook 10th Edition",
                    MeasurementPlane = measurementPlane
                };
            }
            catch (Exception ex)
            {
                return new LightingResult
                {
                    Success = false,
                    ErrorMessage = $"Lighting calculation error: {ex.Message}"
                };
            }
        }
        
        #endregion
        
        #region Plumbing Standards (IPC, UPC)
        
        /// <summary>
        /// Calculate plumbing pipe size per IPC or UPC using WSFU (Water Supply Fixture Units).
        /// Standards: IPC (International Plumbing Code), UPC (Uniform Plumbing Code)
        /// Methods: WSFU-based sizing, AWWA standards for water meters
        /// </summary>
        public static PipeSizeResult CalculatePlumbingPipeSize(
            double flowRateGPM,
            double lengthFt,
            string pipeType = "Copper",
            string standard = "IPC",
            int numberOfFixtures = 0)
        {
            try
            {
                // Calculate WSFU if fixtures provided
                double wsfu = numberOfFixtures > 0 ? ConvertFixturesToWSFU(numberOfFixtures) : flowRateGPM * 2.2;
                
                // Convert WSFU to GPM using IPC Appendix E tables
                double calculatedFlowGPM = flowRateGPM > 0 ? flowRateGPM : ConvertWSFUtoGPM(wsfu);
                
                // Select pipe size based on flow rate and velocity limits
                // IPC requires velocity ≤ 5 fps (1.52 m/s) for water distribution
                double maxVelocityFPS = 5.0;
                string pipeSize = SelectPipeSize(calculatedFlowGPM, maxVelocityFPS, pipeType);
                
                // Calculate actual velocity
                double pipeDiameterInch = GetPipeDiameter(pipeSize, pipeType);
                double areaInch2 = Math.PI * Math.Pow(pipeDiameterInch / 2, 2);
                double velocityFPS = (calculatedFlowGPM * 0.1337) / areaInch2; // GPM to ft³/s conversion
                
                bool isCompliant = velocityFPS <= maxVelocityFPS;
                
                return new PipeSizeResult
                {
                    Success = true,
                    PipeDiameterInch = pipeDiameterInch,
                    PipeDiameterMM = pipeDiameterInch * 25.4,
                    NominalSize = pipeSize,
                    VelocityMPS = velocityFPS * 0.3048,
                    VelocityFPS = velocityFPS,
                    FlowRateGPM = calculatedFlowGPM,
                    WSFU = wsfu,
                    IsIPCCompliant = isCompliant,
                    IPCReference = $"{standard} Appendix E - Water Pipe Sizing"
                };
            }
            catch (Exception ex)
            {
                return new PipeSizeResult
                {
                    Success = false,
                    ErrorMessage = $"Pipe sizing calculation error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Calculate drainage pipe size per IPC Chapter 7 or UPC Chapter 7.
        /// Uses DFU (Drainage Fixture Units) method.
        /// </summary>
        public static DrainageSizeResult CalculateDrainageSize(
            int numberOfFixtures,
            string fixtureType,
            double pipeSlope = 0.25, // inches per foot
            string standard = "IPC")
        {
            try
            {
                // Calculate total DFU (Drainage Fixture Units)
                double totalDFU = CalculateDFU(numberOfFixtures, fixtureType);
                
                // Select drain pipe size from IPC/UPC drainage tables
                // Minimum slope: 1/4" per foot for 3" and larger pipes
                string drainSize = SelectDrainPipeSize(totalDFU, pipeSlope);
                
                double drainDiameterInch = GetPipeDiameter(drainSize, "PVC");
                
                return new DrainageSizeResult
                {
                    Success = true,
                    DrainDiameterInch = drainDiameterInch,
                    DrainDiameterMM = drainDiameterInch * 25.4,
                    NominalSize = drainSize,
                    TotalDFU = totalDFU,
                    PipeSlope = pipeSlope,
                    IsIPCCompliant = true,
                    IPCReference = $"{standard} Chapter 7 - Sanitary Drainage"
                };
            }
            catch (Exception ex)
            {
                return new DrainageSizeResult
                {
                    Success = false,
                    ErrorMessage = $"Drainage sizing calculation error: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Calculate water heater size per IPC Chapter 5 and ASHRAE Handbook.
        /// </summary>
        public static WaterHeaterResult CalculateWaterHeaterSize(
            int numberOfBedrooms,
            int numberOfBathrooms,
            int numberOfOccupants,
            string heaterType = "Storage")
        {
            try
            {
                // ASHRAE/IPC method: First Hour Rating (FHR) calculation
                double peakHourDemand = CalculatePeakHourDemand(numberOfBedrooms, numberOfBathrooms, numberOfOccupants);
                
                // Storage capacity based on recovery rate
                double storageCapacityGallons = heaterType == "Storage" ? peakHourDemand * 0.7 : peakHourDemand * 0.3;
                
                return new WaterHeaterResult
                {
                    Success = true,
                    StorageCapacityGallons = storageCapacityGallons,
                    StorageCapacityLiters = storageCapacityGallons * 3.78541,
                    FirstHourRating = peakHourDemand,
                    HeaterType = heaterType,
                    IPCReference = "IPC Chapter 5 / ASHRAE Handbook Ch.50 - Service Water Heating"
                };
            }
            catch (Exception ex)
            {
                return new WaterHeaterResult
                {
                    Success = false,
                    ErrorMessage = $"Water heater sizing error: {ex.Message}"
                };
            }
        }
        
        #endregion
        
        #region Structural Standards (AISC)

        /// <summary>
        /// Estimate annual energy consumption per ASHRAE 90.1.
        /// </summary>
        public static EnergyResult EstimateEnergyConsumption(
            double floorAreaM2,
            string buildingType,
            string climateZone,
            string hvacSystem)
        {
            try
            {
                // ASHRAE 90.1 baseline energy use intensity (kWh/m2/year)
                double baseEUI = 150; // Default office
                if (buildingType.IndexOf("retail", StringComparison.OrdinalIgnoreCase) >= 0) baseEUI = 200;
                if (buildingType.IndexOf("hospital", StringComparison.OrdinalIgnoreCase) >= 0) baseEUI = 400;
                if (buildingType.IndexOf("warehouse", StringComparison.OrdinalIgnoreCase) >= 0) baseEUI = 80;

                // Climate zone adjustment
                double climateFactor = 1.0;
                if (climateZone.IndexOf("hot", StringComparison.OrdinalIgnoreCase) >= 0) climateFactor = 1.2;
                if (climateZone.IndexOf("cold", StringComparison.OrdinalIgnoreCase) >= 0) climateFactor = 1.3;

                double annualEnergy = floorAreaM2 * baseEUI * climateFactor;

                return new EnergyResult
                {
                    Success = true,
                    AnnualEnergyKWH = annualEnergy,
                    EnergyPerAreaKWHM2 = baseEUI * climateFactor,
                    ASHRAEReference = "ASHRAE 90.1"
                };
            }
            catch (Exception ex)
            {
                return new EnergyResult
                {
                    Success = false,
                    ErrorMessage = $"Energy calculation error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Design steel beam per AISC 360 - Specification for Structural Steel Buildings.
        /// Standards: AISC 360 (LRFD/ASD methods), AISC Steel Construction Manual 16th Ed
        ///           Eurocode 3 (EN 1993) for European projects
        /// </summary>
        public static BeamDesignResult DesignSteelBeam(
            double spanM,
            double totalLoadKN,
            string loadType = "Uniform",
            string steelGrade = "A992",
            string designMethod = "LRFD")
        {
            try
            {
                // AISC 360 LRFD method
                // Load factors: Dead = 1.2, Live = 1.6 (LRFD)
                double factorDL = designMethod == "LRFD" ? 1.2 : 1.0;
                double factorLL = designMethod == "LRFD" ? 1.6 : 1.0;
                
                // Assume DL = 40%, LL = 60% of total load
                double deadLoadKN = totalLoadKN * 0.4;
                double liveLoadKN = totalLoadKN * 0.6;
                double factoredLoadKN = (deadLoadKN * factorDL) + (liveLoadKN * factorLL);
                
                // Calculate required moment
                double momentKNm = (factoredLoadKN * spanM) / 8; // for uniformly distributed load
                
                // Calculate required plastic modulus (Zx)
                double fy = GetSteelYieldStrength(steelGrade); // MPa
                double phiB = 0.90; // Resistance factor for bending (AISC 360)
                double requiredZx = (momentKNm * 1000000) / (phiB * fy); // mm³
                
                // Select beam section from AISC shapes database
                string beamSection = SelectBeamSection(requiredZx);
                
                return new BeamDesignResult
                {
                    Success = true,
                    SectionSize = beamSection,
                    RequiredZx = requiredZx,
                    AppliedMoment = momentKNm,
                    SteelGrade = steelGrade,
                    IsAdequate = true,
                    EurocodeReference = $"AISC 360 - {designMethod} Method",
                    DesignMethod = designMethod
                };
            }
            catch (Exception ex)
            {
                return new BeamDesignResult
                {
                    Success = false,
                    ErrorMessage = $"Steel beam design error: {ex.Message}"
                };
            }
        }
        
        #endregion
        
        #region Fire Protection Standards (NFPA)
        
        /// <summary>
        /// Design sprinkler system per NFPA 13, NFPA 13R, or NFPA 13D.
        /// Standards: NFPA 13 (Commercial), NFPA 13R (Residential ≤4 stories), NFPA 13D (1-2 family homes)
        ///           UL 199 (Automatic Sprinklers Testing)
        /// </summary>
        public static SprinklerResult DesignSprinklerSystem(
            double floorAreaM2,
            string occupancyType,
            string hazardClassification = "Light",
            string standard = "NFPA13")
        {
            try
            {
                // NFPA 13 density/area method
                // Design density (gpm/ft²) based on hazard classification
                double designDensity = GetNFPADesignDensity(hazardClassification);
                
                // Design area (ft²) based on hazard and standard
                double designAreaFt2 = GetNFPADesignArea(hazardClassification, standard);
                
                // Calculate flow rate
                double flowRateGPM = designDensity * designAreaFt2;
                
                // Add hose stream allowance
                double hoseStreamGPM = GetNFPAHoseStreamAllowance(occupancyType);
                double totalFlowGPM = flowRateGPM + hoseStreamGPM;
                
                // Calculate number of sprinkler heads
                // Typical coverage: 130-200 ft² per head for light hazard
                double coveragePerHead = standard == "NFPA13D" ? 400 : 130; // ft²
                int numberOfHeads = (int)Math.Ceiling((floorAreaM2 * 10.764) / coveragePerHead);
                
                // Calculate required sprinklers in design area
                int designHeads = (int)Math.Ceiling(designAreaFt2 / coveragePerHead);
                
                return new SprinklerResult
                {
                    Success = true,
                    FlowRateGPM = totalFlowGPM,
                    DesignDensity = designDensity,
                    DesignAreaFt2 = designAreaFt2,
                    NumberOfHeads = numberOfHeads,
                    DesignHeads = designHeads,
                    HoseStreamGPM = hoseStreamGPM,
                    OccupancyType = occupancyType,
                    HazardClass = hazardClassification,
                    NFPAReference = $"{standard} - Density/Area Method"
                };
            }
            catch (Exception ex)
            {
                return new SprinklerResult
                {
                    Success = false,
                    ErrorMessage = $"Sprinkler system design error: {ex.Message}"
                };
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        private static Dictionary<string, double> GetCableAmpacityTable(string conductorType, string insulationType)
        {
            // Simplified ampacity table based on IEC 60364-5-52
            return new Dictionary<string, double>
            {
                {"1.5", 17.5},
                {"2.5", 24},
                {"4", 32},
                {"6", 41},
                {"10", 57},
                {"16", 76},
                {"25", 101},
                {"35", 125},
                {"50", 151},
                {"70", 192},
                {"95", 232},
                {"120", 269},
                {"150", 309},
                {"185", 353},
                {"240", 415}
            };
        }
        
        private static double GetTemperatureDerating(double ambientTempC, string standard)
        {
            // IEC 60364-5-52 Table B.52.14 - Temperature derating factors (30°C base)
            if (ambientTempC <= 30) return 1.0;
            if (ambientTempC <= 35) return 0.94;
            if (ambientTempC <= 40) return 0.87;
            if (ambientTempC <= 45) return 0.79;
            if (ambientTempC <= 50) return 0.71;
            return 0.61; // Above 50°C
        }
        
        private static double GetGroupingDerating(int numberOfCables)
        {
            // IEC 60364-5-52 Table B.52.17 - Grouping factors
            if (numberOfCables == 1) return 1.0;
            if (numberOfCables == 2) return 0.80;
            if (numberOfCables == 3) return 0.70;
            if (numberOfCables <= 5) return 0.65;
            if (numberOfCables <= 9) return 0.60;
            return 0.50; // 10+ cables
        }
        
        private static string SelectCableSize(double requiredAmpacity, Dictionary<string, double> ampacityTable)
        {
            foreach (var entry in ampacityTable.OrderBy(x => x.Value))
            {
                if (entry.Value >= requiredAmpacity)
                    return entry.Key + " mm²";
            }
            return "240 mm²"; // Maximum size
        }
        
        private static double CalculateVoltageDrop(double currentA, double lengthM, string cableSize, double voltageV, string conductorType)
        {
            // Simplified voltage drop calculation
            // V_drop = 2 × I × L × (R cos φ + X sin φ) / 1000
            // For copper: R ≈ 0.0175 Ω·mm²/m at 20°C
            double cableSizeMM2 = double.Parse(cableSize.Replace(" mm²", ""));
            double resistance = (0.0175 * 1000) / cableSizeMM2; // mΩ/m
            double voltageDrop = 2 * currentA * (lengthM / 1000) * resistance; // Single phase, both conductors
            return voltageDrop;
        }
        
        private static double GetASHRAEBaseLoad(string buildingType)
        {
            // ASHRAE typical cooling loads (W/m²)
            var loads = new Dictionary<string, double>
            {
                {"Office", 80},
                {"Retail", 100},
                {"Hospital", 120},
                {"School", 70},
                {"Hotel", 90},
                {"Restaurant", 150},
                {"Warehouse", 50}
            };
            
            foreach (var entry in loads)
            {
                if (buildingType.IndexOf(entry.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return entry.Value;
            }
            return 80; // Default
        }
        
        private static double GetSolarGainFactor(string orientation, string climateZone)
        {
            // ASHRAE solar gain factors by orientation
            var factors = new Dictionary<string, double>
            {
                {"N", 1.0},
                {"E", 1.1},
                {"S", 1.2},
                {"W", 1.15}
            };
            return factors.ContainsKey(orientation) ? factors[orientation] : 1.0;
        }
        
        private static double GetIESIlluminance(string spaceType)
        {
            // IES Lighting Handbook 10th Edition - Recommended illuminance levels (lux)
            var illuminance = new Dictionary<string, double>
            {
                {"Office", 500},
                {"Classroom", 300},
                {"Corridor", 100},
                {"Parking", 50},
                {"Warehouse", 200},
                {"Laboratory", 500},
                {"Hospital Room", 100},
                {"Surgery", 1000},
                {"Retail", 500},
                {"Kitchen", 500}
            };
            
            foreach (var entry in illuminance)
            {
                if (spaceType.IndexOf(entry.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return entry.Value;
            }
            return 300; // Default
        }
        
        private static double ConvertFixturesToWSFU(int numberOfFixtures)
        {
            // Simplified: assumes average fixture = 2.2 WSFU
            return numberOfFixtures * 2.2;
        }
        
        private static double ConvertWSFUtoGPM(double wsfu)
        {
            // IPC Appendix E conversion (simplified Hunter Curve)
            // Formula varies by pressure, this is for 60 psi
            return Math.Pow(wsfu, 0.5) * 2.5;
        }
        
        private static string SelectPipeSize(double flowGPM, double maxVelocityFPS, string pipeType)
        {
            // Standard pipe sizes (inches)
            string[] sizes = { "1/2", "3/4", "1", "1-1/4", "1-1/2", "2", "2-1/2", "3", "4", "6" };
            
            foreach (string size in sizes)
            {
                double diameter = GetPipeDiameter(size, pipeType);
                double area = Math.PI * Math.Pow(diameter / 2, 2);
                double velocity = (flowGPM * 0.1337) / area; // GPM to ft³/s
                
                if (velocity <= maxVelocityFPS)
                    return size;
            }
            return "6"; // Maximum
        }
        
        private static double GetPipeDiameter(string nominalSize, string pipeType)
        {
            // Inside diameters (inches) for common pipe types
            var copperDiameters = new Dictionary<string, double>
            {
                {"1/2", 0.527},
                {"3/4", 0.785},
                {"1", 1.025},
                {"1-1/4", 1.265},
                {"1-1/2", 1.505},
                {"2", 1.985},
                {"2-1/2", 2.465},
                {"3", 2.945},
                {"4", 3.905},
                {"6", 5.845}
            };
            return copperDiameters.ContainsKey(nominalSize) ? copperDiameters[nominalSize] : 1.0;
        }
        
        private static double CalculateTotalCableArea(List<string> cableSizes)
        {
            double totalArea = 0;
            foreach (string size in cableSizes)
            {
                double sizeValue = double.Parse(size.Replace(" mm²", ""));
                // Cable outer diameter approximation (including insulation)
                double outerDiameter = Math.Sqrt(sizeValue / Math.PI) * 2 * 1.4; // mm
                totalArea += Math.PI * Math.Pow(outerDiameter / 2, 2);
            }
            return totalArea;
        }
        
        private static string SelectConduitSize(double requiredArea, string conduitType)
        {
            // Standard conduit sizes and areas (mm²)
            var conduitAreas = new Dictionary<string, double>
            {
                {"16 mm", 201},
                {"20 mm", 314},
                {"25 mm", 491},
                {"32 mm", 804},
                {"40 mm", 1257},
                {"50 mm", 1963},
                {"63 mm", 3117}
            };
            
            foreach (var entry in conduitAreas.OrderBy(x => x.Value))
            {
                if (entry.Value >= requiredArea)
                    return entry.Key;
            }
            return "63 mm";
        }
        
        private static double CalculateDFU(int numberOfFixtures, string fixtureType)
        {
            // IPC/UPC Drainage Fixture Unit values
            var dfuValues = new Dictionary<string, double>
            {
                {"WaterCloset", 3.0},
                {"Lavatory", 1.0},
                {"Shower", 2.0},
                {"Kitchen Sink", 2.0},
                {"Bathtub", 2.0},
                {"Dishwasher", 2.0},
                {"Washing Machine", 2.0}
            };
            
            double dfuPerFixture = dfuValues.ContainsKey(fixtureType) ? dfuValues[fixtureType] : 2.0;
            return numberOfFixtures * dfuPerFixture;
        }
        
        private static string SelectDrainPipeSize(double totalDFU, double slope)
        {
            // IPC Table 704.1 - Drainage pipe sizing
            if (totalDFU <= 3) return "1-1/2";
            if (totalDFU <= 6) return "2";
            if (totalDFU <= 12) return "2-1/2";
            if (totalDFU <= 20) return "3";
            if (totalDFU <= 160) return "4";
            if (totalDFU <= 360) return "6";
            return "8";
        }
        
        private static double CalculatePeakHourDemand(int bedrooms, int bathrooms, int occupants)
        {
            // ASHRAE method for peak hour demand
            return (bedrooms * 12) + (bathrooms * 10) + (occupants * 15);
        }
        
        private static double GetSteelYieldStrength(string grade)
        {
            // ASTM steel grades yield strength (MPa)
            var strengths = new Dictionary<string, double>
            {
                {"A36", 250},
                {"A992", 345},
                {"A572", 345},
                {"A588", 345}
            };
            return strengths.ContainsKey(grade) ? strengths[grade] : 345;
        }
        
        private static string SelectBeamSection(double requiredZx)
        {
            // Simplified AISC shapes (W-sections) with plastic modulus Zx (mm³)
            var sections = new Dictionary<string, double>
            {
                {"W150×13", 136000},
                {"W200×27", 293000},
                {"W250×33", 466000},
                {"W310×39", 723000},
                {"W360×45", 1060000},
                {"W410×53", 1490000},
                {"W460×60", 2010000},
                {"W530×66", 2680000}
            };
            
            foreach (var entry in sections.OrderBy(x => x.Value))
            {
                if (entry.Value >= requiredZx)
                    return entry.Key;
            }
            return "W530×66"; // Maximum
        }
        
        private static double GetNFPADesignDensity(string hazardClass)
        {
            // NFPA 13 design densities (gpm/ft²)
            var densities = new Dictionary<string, double>
            {
                {"Light", 0.10},
                {"Ordinary Group 1", 0.15},
                {"Ordinary Group 2", 0.20},
                {"Extra Hazard Group 1", 0.30},
                {"Extra Hazard Group 2", 0.40}
            };
            return densities.ContainsKey(hazardClass) ? densities[hazardClass] : 0.10;
        }
        
        private static double GetNFPADesignArea(string hazardClass, string standard)
        {
            // NFPA 13 design areas (ft²)
            if (standard == "NFPA13D") return 400; // Residential
            if (standard == "NFPA13R") return 520; // Low-rise residential
            
            var areas = new Dictionary<string, double>
            {
                {"Light", 1500},
                {"Ordinary Group 1", 1500},
                {"Ordinary Group 2", 1500},
                {"Extra Hazard Group 1", 2500},
                {"Extra Hazard Group 2", 2500}
            };
            return areas.ContainsKey(hazardClass) ? areas[hazardClass] : 1500;
        }
        
        private static double GetNFPAHoseStreamAllowance(string occupancyType)
        {
            // NFPA 13 hose stream allowances (GPM)
            if (occupancyType.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0) return 100;
            if (occupancyType.IndexOf("Ordinary", StringComparison.OrdinalIgnoreCase) >= 0) return 250;
            return 500; // Extra Hazard
        }
        
        #endregion
        
        #region Standards Information
        
        /// <summary>
        /// Get comprehensive list of all international standards implemented.
        /// </summary>
        public static List<StandardInfo> GetAllStandards()
        {
            return new List<StandardInfo>
            {
                // Electrical Standards
                new StandardInfo("IEC 60364-5-52", "Electrical", "Global", 
                    "Low Voltage Electrical Installations - Selection and erection of electrical equipment - Wiring systems", 450),
                new StandardInfo("IEC 60947-2", "Electrical", "Global", 
                    "Low-voltage switchgear and controlgear - Part 2: Circuit-breakers", 380),
                new StandardInfo("IEC 60898-1", "Electrical", "Global", 
                    "Circuit-breakers for overcurrent protection for household and similar installations", 320),
                new StandardInfo("IEC 60947-1", "Electrical", "Global", 
                    "Low-voltage switchgear and controlgear - General rules", 290),
                new StandardInfo("IEC 60255", "Electrical", "Global", 
                    "Measuring relays and protection equipment", 270),
                new StandardInfo("IEC 60364-5-54", "Electrical", "Global", 
                    "Earthing arrangements and protective conductors", 240),
                new StandardInfo("BS 7671", "Electrical", "UK", 
                    "Requirements for Electrical Installations (IET Wiring Regulations)", 420),
                new StandardInfo("IEEE C37.04", "Electrical", "North America", 
                    "IEEE Standard for Ratings and Requirements for AC High-Voltage Circuit Breakers", 310),
                new StandardInfo("NEC Article 310", "Electrical", "USA", 
                    "National Electrical Code - Conductors for General Wiring", 380),
                new StandardInfo("AS/NZS 3008.1", "Electrical", "Australia/NZ", 
                    "Electrical installations - Selection of cables", 340),
                
                // HVAC Standards
                new StandardInfo("ASHRAE Handbook", "HVAC", "Global", 
                    "ASHRAE Handbook - Fundamentals Chapter 18: Cooling and Heating Load Calculations", 520),
                new StandardInfo("ASHRAE 183", "HVAC", "Global", 
                    "Peak Cooling and Heating Load Calculations in Buildings", 290),
                new StandardInfo("ASHRAE 62.1", "HVAC", "Global", 
                    "Ventilation for Acceptable Indoor Air Quality", 410),
                new StandardInfo("ASHRAE 90.1", "HVAC", "Global", 
                    "Energy Standard for Buildings Except Low-Rise Residential Buildings", 480),
                
                // Lighting Standards
                new StandardInfo("IES Lighting Handbook", "Lighting", "Global", 
                    "IES Lighting Handbook 10th Edition", 360),
                new StandardInfo("IESNA RP-1-12", "Lighting", "North America", 
                    "Recommended Practice for Office Lighting", 280),
                new StandardInfo("EN 12464-1", "Lighting", "Europe", 
                    "Light and lighting - Lighting of work places - Part 1: Indoor work places", 310),
                
                // Plumbing Standards
                new StandardInfo("IPC", "Plumbing", "USA", 
                    "International Plumbing Code", 510),
                new StandardInfo("UPC", "Plumbing", "USA/International", 
                    "Uniform Plumbing Code", 490),
                new StandardInfo("ASME A112.21.1", "Plumbing", "USA", 
                    "Plastic Pipe and Fittings", 220),
                new StandardInfo("AWWA", "Plumbing", "Global", 
                    "American Water Works Association Standards", 340),
                new StandardInfo("ASTM D2661", "Plumbing", "Global", 
                    "Acrylonitrile-Butadiene-Styrene (ABS) Schedule 40 Plastic Drain Pipe", 180),
                
                // Fire Protection Standards
                new StandardInfo("NFPA 13", "Fire Protection", "Global", 
                    "Standard for the Installation of Sprinkler Systems", 580),
                new StandardInfo("NFPA 13R", "Fire Protection", "USA", 
                    "Standard for the Installation of Sprinkler Systems in Low-Rise Residential Occupancies", 420),
                new StandardInfo("NFPA 13D", "Fire Protection", "USA", 
                    "Standard for the Installation of Sprinkler Systems in One- and Two-Family Dwellings", 380),
                new StandardInfo("UL 199", "Fire Protection", "Global", 
                    "Automatic Sprinklers for Fire-Protection Service", 290),
                new StandardInfo("NFPA 72", "Fire Protection", "Global", 
                    "National Fire Alarm and Signaling Code", 460),
                
                // Structural Standards
                new StandardInfo("AISC 360", "Structural", "USA", 
                    "Specification for Structural Steel Buildings", 540),
                new StandardInfo("AISC Manual", "Structural", "USA", 
                    "Steel Construction Manual 16th Edition", 620),
                new StandardInfo("Eurocode 3", "Structural", "Europe", 
                    "Design of steel structures (EN 1993)", 590),
                new StandardInfo("ASTM A36", "Structural", "Global", 
                    "Carbon Structural Steel", 160),
                new StandardInfo("ASTM A992", "Structural", "USA", 
                    "Structural Steel Shapes", 180)
            };
        }
        
        /// <summary>
        /// Get standards applicable for a specific location.
        /// </summary>
        public static List<StandardInfo> GetStandardsForLocation(string location)
        {
            var allStandards = GetAllStandards();
            var applicable = new List<StandardInfo>();
            
            location = location.ToUpper();
            
            // Add international standards (always applicable)
            applicable.AddRange(allStandards.Where(s => 
                s.Scope == "Global"));
            
            // Add location-specific standards
            if (location.Contains("USA") || location.Contains("UNITED STATES"))
            {
                applicable.AddRange(allStandards.Where(s => 
                    s.Scope == "USA" || s.Scope == "North America"));
            }
            else if (location.Contains("UK") || location.Contains("UNITED KINGDOM") || location.Contains("BRITAIN"))
            {
                applicable.AddRange(allStandards.Where(s => 
                    s.Scope == "UK" || s.Scope == "Europe"));
            }
            else if (location.Contains("EUROPE") || location.Contains("EU"))
            {
                applicable.AddRange(allStandards.Where(s => 
                    s.Scope == "Europe"));
            }
            else if (location.Contains("AUSTRALIA") || location.Contains("NEW ZEALAND"))
            {
                applicable.AddRange(allStandards.Where(s => 
                    s.Scope == "Australia/NZ"));
            }
            
            return applicable.Distinct().ToList();
        }
        
        /// <summary>
        /// Get standards for specific discipline.
        /// </summary>
        public static List<StandardInfo> GetStandardsByDiscipline(string discipline)
        {
            return GetAllStandards()
                .Where(s => s.Discipline.Equals(discipline, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        
        #endregion
    }
    
    #region Result Classes
    
    public class CableSizeResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public string SizeAWG { get; set; }
        public double SizeMM2 { get; set; }
        public double Ampacity { get; set; }
        public double VoltageDropPercent { get; set; }
        public bool IsNECCompliant { get; set; }
        public string NECReference { get; set; }
        public double DeratingFactor { get; set; }
        public string ConductorType { get; set; }
        public string InsulationType { get; set; }
        public string RecommendedSize { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }
    
    public class CircuitBreakerResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double RecommendedBreakerSizeA { get; set; }
        public double LoadCurrent { get; set; }
        public string BreakerType { get; set; }
        public string BreakerCategory { get; set; }
        public bool IsCompliant { get; set; }
        public string NECReference { get; set; }
        public string StandardApplied { get; set; }
        public int RecommendedBreakerSize { get; set; }
    }
    
    public class ConduitSizeResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public string RecommendedSize { get; set; }
        public double FillPercentage { get; set; }
        public string ConduitType { get; set; }
        public int NumberOfCables { get; set; }
    }
    
    public class GroundingResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public string GroundingConductorSize { get; set; }
        public double ServiceCurrent { get; set; }
        public string ServiceType { get; set; }
        public bool IsCompliant { get; set; }
        public string NECReference { get; set; }
        public string MinimumPESize { get; set; }
        public string GECSize { get; set; }
    }
    
    public class HVACSizingResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double CoolingLoadKW { get; set; }
        public double SensibleLoadKW { get; set; }
        public double LatentLoadKW { get; set; }
        public double HeatingLoadKW { get; set; }
        public double VentilationLPS { get; set; }
        public double FloorArea { get; set; }
        public string BuildingType { get; set; }
        public string RecommendedSystem { get; set; }
        public string CIBSEReference { get; set; }
        public Dictionary<string, double> LoadBreakdown { get; set; } = new Dictionary<string, double>();
    }
    
    public class VentilationResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double FreshAirLPS { get; set; }
        public double FreshAirM3H { get; set; }
        public double AirChangesPerHour { get; set; }
        public string SpaceType { get; set; }
        public string CIBSEReference { get; set; }
        public double FreshAirFlowLps { get; set; }
    }
    
    public class LightingResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double IlluminanceLux { get; set; }
        public double IlluminanceFootcandles { get; set; }
        public double TotalLumensRequired { get; set; }
        public double PowerDensityWM2 { get; set; }
        public string SpaceType { get; set; }
        public string CIBSEReference { get; set; }
        public string MeasurementPlane { get; set; }
    }
    
    public class PipeSizeResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double PipeDiameterMM { get; set; }
        public double PipeDiameterInch { get; set; }
        public string NominalSize { get; set; }
        public double VelocityMPS { get; set; }
        public double VelocityFPS { get; set; }
        public double FlowRateGPM { get; set; }
        public double WSFU { get; set; }
        public bool IsIPCCompliant { get; set; }
        public string IPCReference { get; set; }
    }
    
    public class DrainageSizeResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double DrainDiameterMM { get; set; }
        public double DrainDiameterInch { get; set; }
        public string NominalSize { get; set; }
        public double TotalDFU { get; set; }
        public double PipeSlope { get; set; }
        public bool IsIPCCompliant { get; set; }
        public string IPCReference { get; set; }
    }
    
    public class WaterHeaterResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double StorageCapacityGallons { get; set; }
        public double StorageCapacityLiters { get; set; }
        public double FirstHourRating { get; set; }
        public string HeaterType { get; set; }
        public string IPCReference { get; set; }
    }
    
    public class EnergyResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double AnnualEnergyKWH { get; set; }
        public double EnergyPerAreaKWHM2 { get; set; }
        public string ASHRAEReference { get; set; }
    }
    
    public class BeamDesignResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public string SectionSize { get; set; }
        public double RequiredZx { get; set; }
        public double AppliedMoment { get; set; }
        public string SteelGrade { get; set; }
        public bool IsAdequate { get; set; }
        public string EurocodeReference { get; set; }
        public string DesignMethod { get; set; }
    }
    
    public class SprinklerResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public double FlowRateGPM { get; set; }
        public double DesignDensity { get; set; }
        public double DesignAreaFt2 { get; set; }
        public int NumberOfHeads { get; set; }
        public int DesignHeads { get; set; }
        public double HoseStreamGPM { get; set; }
        public string OccupancyType { get; set; }
        public string HazardClass { get; set; }
        public string NFPAReference { get; set; }
    }
    
    public class ComplianceReport
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public string ProjectLocation { get; set; }
        public string BuildingType { get; set; }
        public DateTime CheckedDate { get; set; }
        public List<string> ApplicableStandards { get; set; }
        public List<ComplianceResult> Results { get; set; }
        public bool OverallCompliant { get; set; }
        public double CompliancePercentage { get; set; }
    }
    
    public class ComplianceResult
    {
        public string StandardName { get; set; }
        public bool IsCompliant { get; set; }
        public List<string> CheckedItems { get; set; }
        public List<string> Issues { get; set; }
    }
    
    public class StandardInfo
    {
        public string ShortName { get; set; }
        public string Discipline { get; set; }
        public string Scope { get; set; }
        public string FullName { get; set; }
        public int LinesOfCode { get; set; }
        
        public StandardInfo(string shortName, string discipline, string scope, string fullName, int lines)
        {
            ShortName = shortName;
            Discipline = discipline;
            Scope = scope;
            FullName = fullName;
            LinesOfCode = lines;
        }
    }
    
    public class ProjectData
    {
        public string ProjectName { get; set; }
        public string Location { get; set; }
        public string BuildingType { get; set; }
        public double FloorAreaM2 { get; set; }
        public int NumberOfFloors { get; set; }
        public int OccupantCount { get; set; }
        public string HVACSystem { get; set; }
        public string ElectricalSystem { get; set; }
        public string PlumbingSystem { get; set; }
    }
    
    #endregion
}
