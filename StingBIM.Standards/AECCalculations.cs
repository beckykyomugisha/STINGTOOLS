// ============================================================================
// StingBIM - Comprehensive AEC/FM Calculations Engine
// Architecture, Engineering, Construction & Facilities Management
// All calculations are offline-capable with embedded standards data
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingBIM.Standards
{
    /// <summary>
    /// Comprehensive AEC (Architecture, Engineering, Construction) and
    /// FM (Facilities Management) calculations engine.
    /// All methods work completely offline with embedded standards data.
    /// </summary>
    public static class AECCalculations
    {
        #region Architecture - Occupancy & Egress (IBC, NFPA 101)

        /// <summary>
        /// Calculate occupant load per IBC Table 1004.5 or NFPA 101.
        /// </summary>
        public static OccupantLoadResult CalculateOccupantLoad(
            double floorAreaSqFt,
            string occupancyType,
            string standard = "IBC")
        {
            // IBC Table 1004.5 - Occupant Load Factors (sq ft per occupant)
            var occupantLoadFactors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                // Assembly
                {"Assembly-Concentrated", 7},
                {"Assembly-Standing", 5},
                {"Assembly-Unconcentrated", 15},
                {"Assembly-Chairs", 18},

                // Business
                {"Business", 150},
                {"Office", 150},

                // Educational
                {"Classroom", 20},
                {"Shop/Lab", 50},

                // Industrial
                {"Industrial", 100},
                {"Manufacturing", 100},
                {"Warehouse", 500},

                // Mercantile
                {"Retail-Ground", 30},
                {"Retail-Upper", 60},
                {"Mall", 30},

                // Residential
                {"Residential", 200},
                {"Hotel", 200},
                {"Dormitory", 50},

                // Institutional
                {"Hospital-Inpatient", 240},
                {"Hospital-Outpatient", 100},
                {"Prison", 120},

                // Storage
                {"Storage", 300},
                {"Parking", 200}
            };

            double loadFactor = occupantLoadFactors.ContainsKey(occupancyType)
                ? occupantLoadFactors[occupancyType]
                : 100; // Default

            int occupantLoad = (int)Math.Ceiling(floorAreaSqFt / loadFactor);

            return new OccupantLoadResult
            {
                Success = true,
                OccupantLoad = occupantLoad,
                LoadFactor = loadFactor,
                FloorArea = floorAreaSqFt,
                OccupancyType = occupancyType,
                StandardReference = $"{standard} Table 1004.5"
            };
        }

        /// <summary>
        /// Calculate required egress width per IBC Section 1005 or NFPA 101.
        /// </summary>
        public static EgressResult CalculateEgressWidth(
            int occupantLoad,
            string egressComponent,
            bool sprinklered = true,
            string standard = "IBC")
        {
            // IBC Table 1005.1 - Egress width per occupant (inches)
            double widthPerOccupant;

            if (egressComponent.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                widthPerOccupant = sprinklered ? 0.2 : 0.3;
            }
            else // Doors, corridors, ramps
            {
                widthPerOccupant = sprinklered ? 0.15 : 0.2;
            }

            double requiredWidthInches = occupantLoad * widthPerOccupant;

            // Minimum widths per IBC
            double minimumWidth = egressComponent.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0
                ? 44 : 36; // inches

            if (occupantLoad < 50)
                minimumWidth = 36;

            double finalWidth = Math.Max(requiredWidthInches, minimumWidth);

            // Number of exits required (IBC 1006.2)
            int exitsRequired;
            if (occupantLoad <= 500) exitsRequired = 2;
            else if (occupantLoad <= 1000) exitsRequired = 3;
            else exitsRequired = 4;

            return new EgressResult
            {
                Success = true,
                RequiredWidthInches = finalWidth,
                RequiredWidthMM = finalWidth * 25.4,
                MinimumWidthInches = minimumWidth,
                ExitsRequired = exitsRequired,
                OccupantLoad = occupantLoad,
                IsSprinklered = sprinklered,
                StandardReference = $"{standard} Section 1005"
            };
        }

        /// <summary>
        /// Calculate maximum travel distance per IBC Table 1017.2.
        /// </summary>
        public static TravelDistanceResult CalculateTravelDistance(
            string occupancyGroup,
            bool sprinklered = true,
            string standard = "IBC")
        {
            // IBC Table 1017.2 - Exit Access Travel Distance (feet)
            var travelDistances = new Dictionary<string, (double unsprinklered, double sprinklered)>(StringComparer.OrdinalIgnoreCase)
            {
                {"A", (200, 250)},
                {"B", (200, 300)},
                {"E", (200, 250)},
                {"F-1", (200, 250)},
                {"F-2", (300, 400)},
                {"H-1", (75, 75)},
                {"H-2", (100, 100)},
                {"H-3", (150, 150)},
                {"H-4", (175, 175)},
                {"H-5", (200, 200)},
                {"I-1", (200, 250)},
                {"I-2", (150, 200)},
                {"I-3", (150, 200)},
                {"I-4", (150, 200)},
                {"M", (200, 250)},
                {"R-1", (200, 250)},
                {"R-2", (200, 250)},
                {"R-3", (200, 250)},
                {"R-4", (200, 250)},
                {"S-1", (200, 250)},
                {"S-2", (300, 400)},
                {"U", (300, 400)}
            };

            var distances = travelDistances.ContainsKey(occupancyGroup)
                ? travelDistances[occupancyGroup]
                : (200, 250);

            double maxDistance = sprinklered ? distances.sprinklered : distances.unsprinklered;

            return new TravelDistanceResult
            {
                Success = true,
                MaxTravelDistanceFt = maxDistance,
                MaxTravelDistanceM = maxDistance * 0.3048,
                OccupancyGroup = occupancyGroup,
                IsSprinklered = sprinklered,
                StandardReference = $"{standard} Table 1017.2"
            };
        }

        /// <summary>
        /// Calculate parking requirements per IBC/local codes.
        /// </summary>
        public static ParkingResult CalculateParkingRequirements(
            double grossFloorAreaSqFt,
            string useType,
            int dwellingUnits = 0,
            int seats = 0)
        {
            // Typical parking ratios (spaces per 1000 sq ft or per unit)
            var parkingRatios = new Dictionary<string, (double ratio, string basis)>(StringComparer.OrdinalIgnoreCase)
            {
                {"Office", (3.3, "per 1000 sqft")},
                {"Retail", (4.0, "per 1000 sqft")},
                {"Restaurant", (10.0, "per 1000 sqft")},
                {"Medical Office", (5.0, "per 1000 sqft")},
                {"Industrial", (1.5, "per 1000 sqft")},
                {"Warehouse", (0.5, "per 1000 sqft")},
                {"Hotel", (1.0, "per room")},
                {"Residential-Single", (2.0, "per unit")},
                {"Residential-Multi", (1.5, "per unit")},
                {"School", (0.2, "per student")},
                {"Church", (0.33, "per seat")},
                {"Theater", (0.25, "per seat")}
            };

            var ratio = parkingRatios.ContainsKey(useType)
                ? parkingRatios[useType]
                : (3.0, "per 1000 sqft");

            int standardSpaces;
            if (ratio.basis == "per 1000 sqft")
                standardSpaces = (int)Math.Ceiling((grossFloorAreaSqFt / 1000) * ratio.ratio);
            else if (ratio.basis == "per unit" || ratio.basis == "per room")
                standardSpaces = (int)Math.Ceiling(dwellingUnits * ratio.ratio);
            else if (ratio.basis == "per seat" || ratio.basis == "per student")
                standardSpaces = (int)Math.Ceiling(seats * ratio.ratio);
            else
                standardSpaces = (int)Math.Ceiling((grossFloorAreaSqFt / 1000) * ratio.ratio);

            // ADA accessible spaces (2010 ADA Standards)
            int accessibleSpaces = CalculateAccessibleParkingSpaces(standardSpaces);
            int vanAccessible = (int)Math.Ceiling(accessibleSpaces / 6.0);

            return new ParkingResult
            {
                Success = true,
                StandardSpaces = standardSpaces,
                AccessibleSpaces = accessibleSpaces,
                VanAccessibleSpaces = vanAccessible,
                TotalSpaces = standardSpaces,
                ParkingRatio = ratio.ratio,
                RatioBasis = ratio.basis,
                UseType = useType,
                StandardReference = "IBC/ADA 2010"
            };
        }

        private static int CalculateAccessibleParkingSpaces(int totalSpaces)
        {
            // ADA 2010 Table 208.2
            if (totalSpaces <= 25) return 1;
            if (totalSpaces <= 50) return 2;
            if (totalSpaces <= 75) return 3;
            if (totalSpaces <= 100) return 4;
            if (totalSpaces <= 150) return 5;
            if (totalSpaces <= 200) return 6;
            if (totalSpaces <= 300) return 7;
            if (totalSpaces <= 400) return 8;
            if (totalSpaces <= 500) return 9;
            if (totalSpaces <= 1000) return (int)(totalSpaces * 0.02);
            return 20 + (int)((totalSpaces - 1000) * 0.01);
        }

        #endregion

        #region Accessibility - ADA/DDA Compliance

        /// <summary>
        /// Calculate accessible route requirements per ADA 2010 / BS 8300.
        /// </summary>
        public static AccessibilityResult CalculateAccessibleRoute(
            double routeWidthInches,
            double maxSlopePct,
            double runLengthFt,
            string standard = "ADA2010")
        {
            var issues = new List<string>();
            bool compliant = true;

            // ADA minimum clear width: 36" (44" for passing)
            double minWidth = 36;
            double passingWidth = 60;

            if (routeWidthInches < minWidth)
            {
                issues.Add($"Route width {routeWidthInches}\" is less than minimum {minWidth}\"");
                compliant = false;
            }

            // ADA maximum slope: 1:20 (5%) for walking surface, 1:12 (8.33%) for ramp
            double maxWalkingSlope = 5.0;
            double maxRampSlope = 8.33;

            bool isRamp = maxSlopePct > maxWalkingSlope;

            if (isRamp)
            {
                if (maxSlopePct > maxRampSlope)
                {
                    issues.Add($"Ramp slope {maxSlopePct:F1}% exceeds maximum {maxRampSlope:F1}%");
                    compliant = false;
                }

                // Ramp run length limits (30 ft max per ADA 405.7)
                if (runLengthFt > 30)
                {
                    issues.Add($"Ramp run {runLengthFt}ft exceeds 30ft maximum - landing required");
                }

                // Handrails required if rise > 6"
                double riseInches = (maxSlopePct / 100) * (runLengthFt * 12);
                bool handrailsRequired = riseInches > 6;

                return new AccessibilityResult
                {
                    Success = true,
                    IsCompliant = compliant,
                    IsRamp = true,
                    RequiredWidth = minWidth,
                    ActualWidth = routeWidthInches,
                    MaxAllowedSlope = maxRampSlope,
                    ActualSlope = maxSlopePct,
                    HandrailsRequired = handrailsRequired,
                    LandingsRequired = runLengthFt > 30,
                    Issues = issues,
                    StandardReference = $"{standard} Section 405"
                };
            }

            return new AccessibilityResult
            {
                Success = true,
                IsCompliant = compliant,
                IsRamp = false,
                RequiredWidth = minWidth,
                ActualWidth = routeWidthInches,
                MaxAllowedSlope = maxWalkingSlope,
                ActualSlope = maxSlopePct,
                HandrailsRequired = false,
                Issues = issues,
                StandardReference = $"{standard} Section 403"
            };
        }

        /// <summary>
        /// Calculate accessible toilet room requirements per ADA 2010.
        /// </summary>
        public static AccessibleToiletResult CalculateAccessibleToilet(
            double roomWidthInches,
            double roomDepthInches,
            string fixtureLayout = "Side")
        {
            var issues = new List<string>();
            bool compliant = true;

            // Minimum dimensions for wheelchair accessible toilet
            double minWidth = 60; // 5 feet clear width
            double minDepth = 56; // Side approach: 56", Front approach: 48" + fixture

            if (fixtureLayout == "Front")
                minDepth = 66; // 48" + 18" fixture

            if (roomWidthInches < minWidth)
            {
                issues.Add($"Room width {roomWidthInches}\" is less than minimum {minWidth}\"");
                compliant = false;
            }

            if (roomDepthInches < minDepth)
            {
                issues.Add($"Room depth {roomDepthInches}\" is less than minimum {minDepth}\"");
                compliant = false;
            }

            // Grab bar requirements
            var grabBars = new Dictionary<string, string>
            {
                {"Side Wall", "42\" long, 12\" from rear wall, 33-36\" AFF"},
                {"Rear Wall", "36\" long minimum, 33-36\" AFF"},
                {"Mounting", "1-1/4\" to 2\" diameter, 1-1/2\" clearance from wall"}
            };

            // Toilet centerline: 16-18" from side wall
            double toiletCenterline = 17; // Recommended

            return new AccessibleToiletResult
            {
                Success = true,
                IsCompliant = compliant,
                RequiredWidth = minWidth,
                RequiredDepth = minDepth,
                ActualWidth = roomWidthInches,
                ActualDepth = roomDepthInches,
                ToiletCenterlineFromWall = toiletCenterline,
                GrabBarRequirements = grabBars,
                Issues = issues,
                StandardReference = "ADA 2010 Section 604"
            };
        }

        /// <summary>
        /// Calculate required number of accessible fixtures per ADA.
        /// </summary>
        public static AccessibleFixturesResult CalculateAccessibleFixtures(
            int totalToilets,
            int totalUrinals,
            int totalLavatories,
            int totalDrinkingFountains)
        {
            // ADA scoping requirements
            int accessibleToilets = Math.Max(1, (int)Math.Ceiling(totalToilets * 0.05));
            int accessibleUrinals = totalUrinals > 0 ? 1 : 0; // At least one if urinals provided
            int accessibleLavatories = Math.Max(1, (int)Math.Ceiling(totalLavatories * 0.05));

            // Drinking fountains: one high and one low if more than one provided
            int hiLoDrinkingFountains = totalDrinkingFountains >= 1 ? 1 : 0;

            return new AccessibleFixturesResult
            {
                Success = true,
                AccessibleToilets = accessibleToilets,
                AccessibleUrinals = accessibleUrinals,
                AccessibleLavatories = accessibleLavatories,
                HiLoDrinkingFountains = hiLoDrinkingFountains,
                TotalToilets = totalToilets,
                TotalUrinals = totalUrinals,
                TotalLavatories = totalLavatories,
                TotalDrinkingFountains = totalDrinkingFountains,
                StandardReference = "ADA 2010 Chapter 2"
            };
        }

        #endregion

        #region Structural - Load Calculations (ASCE 7, IBC)

        /// <summary>
        /// Calculate floor live loads per ASCE 7 Table 4.3-1.
        /// </summary>
        public static LiveLoadResult CalculateFloorLiveLoad(
            string occupancyType,
            bool reducible = true,
            double tributaryAreaSqFt = 0)
        {
            // ASCE 7-22 Table 4.3-1 Minimum Uniformly Distributed Live Loads
            var liveLoads = new Dictionary<string, (double uniform, double concentrated)>(StringComparer.OrdinalIgnoreCase)
            {
                // Assembly
                {"Assembly-Fixed Seats", (60, 0)},
                {"Assembly-Movable Seats", (100, 0)},
                {"Assembly-Stage", (150, 0)},

                // Residential
                {"Residential", (40, 0)},
                {"Hotel-Guest", (40, 0)},
                {"Hotel-Public", (100, 0)},

                // Office
                {"Office", (50, 2000)},
                {"Office-Lobbies", (100, 2000)},

                // Mercantile
                {"Retail-First Floor", (100, 1000)},
                {"Retail-Upper Floors", (75, 1000)},

                // Storage
                {"Storage-Light", (125, 0)},
                {"Storage-Heavy", (250, 0)},

                // Industrial
                {"Manufacturing-Light", (125, 2000)},
                {"Manufacturing-Heavy", (250, 3000)},

                // Educational
                {"Classroom", (40, 1000)},
                {"Corridor-First Floor", (100, 0)},
                {"Corridor-Upper", (80, 0)},

                // Healthcare
                {"Hospital-Patient", (40, 1000)},
                {"Hospital-Operating", (60, 1000)},
                {"Hospital-Corridor", (80, 1000)},

                // Parking
                {"Parking-Passenger", (40, 3000)},
                {"Parking-Truck", (250, 16000)}
            };

            var load = liveLoads.ContainsKey(occupancyType)
                ? liveLoads[occupancyType]
                : (50, 2000);

            double uniformLoad = load.uniform;
            double concentratedLoad = load.concentrated;

            // Live load reduction per ASCE 7-22 Section 4.7
            double reductionFactor = 1.0;
            if (reducible && tributaryAreaSqFt > 0 && uniformLoad <= 100)
            {
                // L = Lo * (0.25 + 15/sqrt(KLL*AT))
                double KLL = 4; // Interior columns
                double reduction = 0.25 + (15 / Math.Sqrt(KLL * tributaryAreaSqFt));
                reductionFactor = Math.Min(1.0, Math.Max(0.4, reduction)); // Min 40% of unreduced

                if (uniformLoad >= 100) // Not reducible for heavy live loads
                    reductionFactor = 1.0;
            }

            double reducedLoad = uniformLoad * reductionFactor;

            return new LiveLoadResult
            {
                Success = true,
                UniformLoadPSF = uniformLoad,
                UniformLoadKPa = uniformLoad * 0.0479,
                ReducedLoadPSF = reducedLoad,
                ReducedLoadKPa = reducedLoad * 0.0479,
                ConcentratedLoadLbs = concentratedLoad,
                ConcentratedLoadKN = concentratedLoad * 0.00445,
                ReductionFactor = reductionFactor,
                TributaryArea = tributaryAreaSqFt,
                OccupancyType = occupancyType,
                StandardReference = "ASCE 7-22 Table 4.3-1"
            };
        }

        /// <summary>
        /// Calculate wind loads per ASCE 7 simplified method.
        /// </summary>
        public static WindLoadResult CalculateWindLoad(
            double basicWindSpeedMPH,
            string exposureCategory,
            double buildingHeightFt,
            string riskCategory = "II")
        {
            // ASCE 7-22 Simplified Wind Load Calculation

            // Velocity pressure coefficient Kz (Table 26.10-1)
            double Kz = CalculateKz(buildingHeightFt, exposureCategory);

            // Topographic factor (assume flat terrain)
            double Kzt = 1.0;

            // Directionality factor (Table 26.6-1)
            double Kd = 0.85; // Buildings

            // Ground elevation factor (assume sea level)
            double Ke = 1.0;

            // Velocity pressure (Eq. 26.10-1)
            double qz = 0.00256 * Kz * Kzt * Kd * Ke * Math.Pow(basicWindSpeedMPH, 2);

            // External pressure coefficients (simplified)
            double CpWindward = 0.8;
            double CpLeeward = -0.5;

            // Design wind pressure
            double windwardPressure = qz * CpWindward;
            double leewardPressure = qz * Math.Abs(CpLeeward);

            return new WindLoadResult
            {
                Success = true,
                BasicWindSpeed = basicWindSpeedMPH,
                VelocityPressurePSF = qz,
                VelocityPressureKPa = qz * 0.0479,
                WindwardPressurePSF = windwardPressure,
                LeewardPressurePSF = leewardPressure,
                Kz = Kz,
                Kzt = Kzt,
                Kd = Kd,
                ExposureCategory = exposureCategory,
                StandardReference = "ASCE 7-22 Chapter 26"
            };
        }

        private static double CalculateKz(double heightFt, string exposure)
        {
            // ASCE 7 Table 26.10-1 simplified
            double alpha, zg;

            switch (exposure.ToUpper())
            {
                case "B": alpha = 7.0; zg = 1200; break;
                case "C": alpha = 9.5; zg = 900; break;
                case "D": alpha = 11.5; zg = 700; break;
                default: alpha = 9.5; zg = 900; break; // Default to C
            }

            double z = Math.Max(15, Math.Min(heightFt, zg));
            return 2.01 * Math.Pow(z / zg, 2.0 / alpha);
        }

        /// <summary>
        /// Calculate seismic base shear per ASCE 7 equivalent lateral force.
        /// </summary>
        public static SeismicLoadResult CalculateSeismicLoad(
            double buildingWeightKips,
            double Ss,  // Spectral acceleration at short period
            double S1,  // Spectral acceleration at 1 second
            string siteClass = "D",
            string riskCategory = "II",
            double R = 6,  // Response modification factor
            double Ie = 1.0) // Importance factor
        {
            // Site coefficients (ASCE 7 Tables 11.4-1, 11.4-2)
            double Fa = GetFa(siteClass, Ss);
            double Fv = GetFv(siteClass, S1);

            // Design spectral accelerations
            double SDs = (2.0 / 3.0) * Fa * Ss;
            double SD1 = (2.0 / 3.0) * Fv * S1;

            // Seismic response coefficient Cs
            double Cs = SDs / (R / Ie);
            double CsMax = SD1 / (R / Ie); // Simplified for T < TL
            double CsMin = Math.Max(0.044 * SDs * Ie, 0.01);

            Cs = Math.Min(Cs, CsMax);
            Cs = Math.Max(Cs, CsMin);

            // Base shear V = Cs * W
            double baseShearKips = Cs * buildingWeightKips;

            // Seismic Design Category
            string sdc = DetermineSDC(SDs, SD1, riskCategory);

            return new SeismicLoadResult
            {
                Success = true,
                BaseShearKips = baseShearKips,
                BaseShearKN = baseShearKips * 4.448,
                Cs = Cs,
                SDs = SDs,
                SD1 = SD1,
                Fa = Fa,
                Fv = Fv,
                SeismicDesignCategory = sdc,
                BuildingWeight = buildingWeightKips,
                StandardReference = "ASCE 7-22 Chapter 12"
            };
        }

        private static double GetFa(string siteClass, double Ss)
        {
            // Simplified Fa values
            switch (siteClass.ToUpper())
            {
                case "A": return 0.8;
                case "B": return 0.9;
                case "C": return 1.0;
                case "D": return Ss <= 0.25 ? 1.6 : (Ss >= 1.25 ? 1.0 : 1.4);
                case "E": return Ss <= 0.25 ? 2.4 : (Ss >= 1.25 ? 0.9 : 1.1);
                default: return 1.2;
            }
        }

        private static double GetFv(string siteClass, double S1)
        {
            // Simplified Fv values
            switch (siteClass.ToUpper())
            {
                case "A": return 0.8;
                case "B": return 0.8;
                case "C": return 1.5;
                case "D": return S1 <= 0.1 ? 2.4 : (S1 >= 0.5 ? 1.5 : 2.0);
                case "E": return S1 <= 0.1 ? 4.2 : (S1 >= 0.5 ? 2.4 : 3.0);
                default: return 2.0;
            }
        }

        private static string DetermineSDC(double SDs, double SD1, string riskCategory)
        {
            // ASCE 7 Tables 11.6-1 and 11.6-2
            string sdcBySDs, sdcBySD1;

            if (SDs < 0.167) sdcBySDs = "A";
            else if (SDs < 0.33) sdcBySDs = "B";
            else if (SDs < 0.50) sdcBySDs = "C";
            else sdcBySDs = "D";

            if (SD1 < 0.067) sdcBySD1 = "A";
            else if (SD1 < 0.133) sdcBySD1 = "B";
            else if (SD1 < 0.20) sdcBySD1 = "C";
            else sdcBySD1 = "D";

            // Return more severe
            return string.Compare(sdcBySDs, sdcBySD1) > 0 ? sdcBySDs : sdcBySD1;
        }

        /// <summary>
        /// Calculate load combinations per ASCE 7 Section 2.
        /// </summary>
        public static LoadCombinationResult CalculateLoadCombinations(
            double deadLoadKips,
            double liveLoadKips,
            double roofLiveLoadKips = 0,
            double snowLoadKips = 0,
            double windLoadKips = 0,
            double seismicLoadKips = 0,
            string designMethod = "LRFD")
        {
            var combinations = new Dictionary<string, double>();

            if (designMethod == "LRFD")
            {
                // ASCE 7-22 Section 2.3.1 LRFD Combinations
                combinations["1. 1.4D"] = 1.4 * deadLoadKips;
                combinations["2. 1.2D + 1.6L + 0.5Lr"] = 1.2 * deadLoadKips + 1.6 * liveLoadKips + 0.5 * roofLiveLoadKips;
                combinations["3. 1.2D + 1.6Lr + L"] = 1.2 * deadLoadKips + 1.6 * roofLiveLoadKips + liveLoadKips;
                combinations["4. 1.2D + 1.6S + L"] = 1.2 * deadLoadKips + 1.6 * snowLoadKips + liveLoadKips;
                combinations["5. 1.2D + W + L + 0.5Lr"] = 1.2 * deadLoadKips + windLoadKips + liveLoadKips + 0.5 * roofLiveLoadKips;
                combinations["6. 1.2D + E + L + 0.2S"] = 1.2 * deadLoadKips + seismicLoadKips + liveLoadKips + 0.2 * snowLoadKips;
                combinations["7. 0.9D + W"] = 0.9 * deadLoadKips + windLoadKips;
                combinations["8. 0.9D + E"] = 0.9 * deadLoadKips + seismicLoadKips;
            }
            else // ASD
            {
                // ASCE 7-22 Section 2.4.1 ASD Combinations
                combinations["1. D"] = deadLoadKips;
                combinations["2. D + L"] = deadLoadKips + liveLoadKips;
                combinations["3. D + Lr"] = deadLoadKips + roofLiveLoadKips;
                combinations["4. D + 0.75L + 0.75Lr"] = deadLoadKips + 0.75 * liveLoadKips + 0.75 * roofLiveLoadKips;
                combinations["5. D + 0.6W"] = deadLoadKips + 0.6 * windLoadKips;
                combinations["6. D + 0.7E"] = deadLoadKips + 0.7 * seismicLoadKips;
                combinations["7. D + 0.75L + 0.75(0.6W)"] = deadLoadKips + 0.75 * liveLoadKips + 0.45 * windLoadKips;
                combinations["8. 0.6D + 0.6W"] = 0.6 * deadLoadKips + 0.6 * windLoadKips;
            }

            var maxCombo = combinations.OrderByDescending(c => c.Value).First();

            return new LoadCombinationResult
            {
                Success = true,
                Combinations = combinations,
                GoverningCombination = maxCombo.Key,
                GoverningLoadKips = maxCombo.Value,
                GoverningLoadKN = maxCombo.Value * 4.448,
                DesignMethod = designMethod,
                StandardReference = "ASCE 7-22 Section 2"
            };
        }

        #endregion

        #region MEP - Duct Sizing (ASHRAE, SMACNA)

        /// <summary>
        /// Calculate duct size using equal friction method per ASHRAE.
        /// </summary>
        public static DuctSizeResult CalculateDuctSize(
            double airflowCFM,
            double frictionRateInWG = 0.08,
            double maxVelocityFPM = 2000,
            string ductShape = "Round")
        {
            // Equal Friction Method per ASHRAE Handbook

            // For round duct: D = (Q / (0.7854 * V))^0.5
            // Using friction rate correlation

            // Approximate diameter using friction factor
            double diameterInches = Math.Pow((airflowCFM * 4.68) / Math.Pow(frictionRateInWG, 0.533), 0.385);

            // Velocity check
            double areaFt2 = Math.PI * Math.Pow(diameterInches / 24, 2);
            double velocityFPM = airflowCFM / areaFt2;

            // If velocity exceeds max, increase size
            if (velocityFPM > maxVelocityFPM)
            {
                areaFt2 = airflowCFM / maxVelocityFPM;
                diameterInches = Math.Sqrt(areaFt2 * 576 / Math.PI);
                velocityFPM = maxVelocityFPM;
            }

            // Round to standard sizes
            double[] standardSizes = { 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48 };
            double roundDiameter = standardSizes.First(s => s >= diameterInches);

            // Equivalent rectangular
            double widthInches = 0, heightInches = 0;
            if (ductShape == "Rectangular")
            {
                // Aspect ratio 2:1 typical
                heightInches = Math.Sqrt((Math.PI * Math.Pow(roundDiameter, 2)) / (4 * 2));
                widthInches = heightInches * 2;

                // Round to standard increments (2")
                heightInches = Math.Ceiling(heightInches / 2) * 2;
                widthInches = Math.Ceiling(widthInches / 2) * 2;
            }

            return new DuctSizeResult
            {
                Success = true,
                RoundDiameterInches = roundDiameter,
                RoundDiameterMM = roundDiameter * 25.4,
                RectWidthInches = widthInches,
                RectHeightInches = heightInches,
                AirflowCFM = airflowCFM,
                VelocityFPM = velocityFPM,
                VelocityMPS = velocityFPM * 0.00508,
                FrictionRate = frictionRateInWG,
                StandardReference = "ASHRAE Handbook - Fundamentals"
            };
        }

        /// <summary>
        /// Calculate pump head and select pump per Hydraulic Institute standards.
        /// </summary>
        public static PumpSizingResult CalculatePumpSize(
            double flowRateGPM,
            double totalHeadFt,
            double pumpEfficiency = 0.70,
            string fluidType = "Water")
        {
            // Specific gravity
            double specificGravity = fluidType == "Water" ? 1.0 : 0.85; // Assume glycol mix

            // Brake horsepower: BHP = (GPM * Head * SG) / (3960 * Efficiency)
            double brakePower = (flowRateGPM * totalHeadFt * specificGravity) / (3960 * pumpEfficiency);

            // Water horsepower (theoretical)
            double waterHP = (flowRateGPM * totalHeadFt * specificGravity) / 3960;

            // Motor sizing (next standard size)
            double[] motorSizes = { 0.5, 0.75, 1, 1.5, 2, 3, 5, 7.5, 10, 15, 20, 25, 30, 40, 50, 60, 75, 100 };
            double motorHP = motorSizes.First(s => s >= brakePower * 1.15); // 15% safety factor

            // NPSH required (approximate)
            double NPSHr = 4 + 0.0007 * flowRateGPM; // Approximate for centrifugal

            return new PumpSizingResult
            {
                Success = true,
                FlowRateGPM = flowRateGPM,
                FlowRateLPS = flowRateGPM * 0.0631,
                TotalHeadFt = totalHeadFt,
                TotalHeadM = totalHeadFt * 0.3048,
                BrakeHorsepower = brakePower,
                BrakeKW = brakePower * 0.746,
                MotorHorsepower = motorHP,
                MotorKW = motorHP * 0.746,
                Efficiency = pumpEfficiency,
                NPSHRequired = NPSHr,
                StandardReference = "Hydraulic Institute Standards"
            };
        }

        #endregion

        #region Electrical - Power Systems

        /// <summary>
        /// Calculate transformer sizing per IEEE C57 / NEC.
        /// </summary>
        public static TransformerSizeResult CalculateTransformerSize(
            double connectedLoadKVA,
            double demandFactor = 0.80,
            double powerFactor = 0.85,
            double growthFactor = 1.25)
        {
            // Calculate demand load
            double demandLoadKVA = connectedLoadKVA * demandFactor;

            // Apply growth factor
            double designLoadKVA = demandLoadKVA * growthFactor;

            // Standard transformer sizes (kVA)
            double[] standardSizes = { 15, 25, 37.5, 50, 75, 100, 112.5, 150, 167, 200, 225, 250, 300,
                                       500, 750, 1000, 1500, 2000, 2500, 3000, 3750, 5000, 7500, 10000 };

            double transformerSize = standardSizes.First(s => s >= designLoadKVA);

            // Calculate currents (assuming 480V 3-phase secondary)
            double secondaryVoltage = 480;
            double fullLoadAmps = (transformerSize * 1000) / (Math.Sqrt(3) * secondaryVoltage);

            // Impedance (typical 5.75% for 500 kVA and above)
            double impedancePct = transformerSize >= 500 ? 5.75 : 5.0;

            // Short circuit current
            double shortCircuitAmps = fullLoadAmps * (100 / impedancePct);

            return new TransformerSizeResult
            {
                Success = true,
                ConnectedLoadKVA = connectedLoadKVA,
                DemandLoadKVA = demandLoadKVA,
                DesignLoadKVA = designLoadKVA,
                TransformerSizeKVA = transformerSize,
                FullLoadAmps = fullLoadAmps,
                ShortCircuitAmps = shortCircuitAmps,
                ImpedancePercent = impedancePct,
                DemandFactor = demandFactor,
                GrowthFactor = growthFactor,
                StandardReference = "IEEE C57 / NEC Article 450"
            };
        }

        /// <summary>
        /// Calculate generator sizing per NFPA 110 / IEEE 446.
        /// </summary>
        public static GeneratorSizeResult CalculateGeneratorSize(
            double totalLoadKW,
            double motorLoadKW,
            double largestMotorHP,
            double powerFactor = 0.8,
            string application = "Standby")
        {
            // Starting kVA for largest motor (assume 6x FLA starting)
            double largestMotorKW = largestMotorHP * 0.746;
            double motorStartingKVA = (largestMotorKW * 6) / powerFactor;

            // Running load kVA
            double runningKVA = totalLoadKW / powerFactor;

            // Total required (consider motor starting)
            double requiredKVA = Math.Max(runningKVA, runningKVA - largestMotorKW + motorStartingKVA);

            // Apply diversity and growth
            double diversityFactor = application == "Prime" ? 0.9 : 0.8;
            double growthFactor = 1.2;

            double designKVA = requiredKVA * growthFactor / diversityFactor;

            // Standard generator sizes (kW)
            double[] standardSizes = { 20, 30, 40, 50, 60, 80, 100, 125, 150, 175, 200, 250, 300, 350,
                                       400, 500, 600, 750, 800, 1000, 1250, 1500, 1750, 2000, 2500, 3000 };

            double generatorKW = standardSizes.First(s => s * 0.8 >= designKVA * powerFactor);
            double generatorKVA = generatorKW / powerFactor;

            // Fuel consumption (approximate diesel)
            double fuelConsumptionGPH = generatorKW * 0.07; // ~7 gal/hr per 100 kW

            return new GeneratorSizeResult
            {
                Success = true,
                TotalLoadKW = totalLoadKW,
                RunningKVA = runningKVA,
                StartingKVA = motorStartingKVA,
                RequiredKVA = requiredKVA,
                GeneratorSizeKW = generatorKW,
                GeneratorSizeKVA = generatorKVA,
                FuelConsumptionGPH = fuelConsumptionGPH,
                FuelConsumptionLPH = fuelConsumptionGPH * 3.785,
                Application = application,
                StandardReference = "NFPA 110 / IEEE 446"
            };
        }

        #endregion

        #region Sustainability - LEED/BREEAM Calculations

        /// <summary>
        /// Calculate Energy Use Intensity (EUI) per ASHRAE 90.1.
        /// </summary>
        public static EUIResult CalculateEUI(
            double annualEnergyKWh,
            double grossFloorAreaSqFt,
            string buildingType)
        {
            double euiKBtuPerSqFt = (annualEnergyKWh * 3.412) / grossFloorAreaSqFt;
            double euiKWhPerM2 = annualEnergyKWh / (grossFloorAreaSqFt * 0.0929);

            // ENERGY STAR baseline EUIs (kBtu/sq ft)
            var baselineEUI = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                {"Office", 92.9},
                {"Retail", 77.1},
                {"Hotel", 95.8},
                {"Hospital", 218.9},
                {"School K-12", 58.5},
                {"Warehouse", 26.6},
                {"Multifamily", 53.1}
            };

            double baseline = baselineEUI.ContainsKey(buildingType) ? baselineEUI[buildingType] : 90;
            double percentBetterThanBaseline = ((baseline - euiKBtuPerSqFt) / baseline) * 100;

            // LEED points estimate (EA Credit)
            int leedPoints = 0;
            if (percentBetterThanBaseline >= 50) leedPoints = 18;
            else if (percentBetterThanBaseline >= 46) leedPoints = 16;
            else if (percentBetterThanBaseline >= 42) leedPoints = 14;
            else if (percentBetterThanBaseline >= 38) leedPoints = 12;
            else if (percentBetterThanBaseline >= 34) leedPoints = 10;
            else if (percentBetterThanBaseline >= 30) leedPoints = 8;
            else if (percentBetterThanBaseline >= 24) leedPoints = 6;
            else if (percentBetterThanBaseline >= 18) leedPoints = 4;
            else if (percentBetterThanBaseline >= 10) leedPoints = 2;
            else if (percentBetterThanBaseline >= 5) leedPoints = 1;

            return new EUIResult
            {
                Success = true,
                EUIKBtuPerSqFt = euiKBtuPerSqFt,
                EUIKWhPerM2 = euiKWhPerM2,
                BaselineEUI = baseline,
                PercentBetterThanBaseline = percentBetterThanBaseline,
                EstimatedLEEDPoints = leedPoints,
                BuildingType = buildingType,
                StandardReference = "ASHRAE 90.1 / ENERGY STAR"
            };
        }

        /// <summary>
        /// Calculate water use reduction for LEED WE credits.
        /// </summary>
        public static WaterUseResult CalculateWaterUseReduction(
            int occupants,
            int workdaysPerYear = 250,
            double toiletGPF = 1.28,
            double urinalGPF = 0.5,
            double lavatoryGPM = 0.5,
            double showerGPM = 1.5,
            double kitchenGPM = 1.5)
        {
            // Baseline fixture performance (EPAct 1992/2005)
            double baselineToiletGPF = 1.6;
            double baselineUrinalGPF = 1.0;
            double baselineLavatoryGPM = 2.2;
            double baselineShowerGPM = 2.5;
            double baselineKitchenGPM = 2.2;

            // Usage assumptions (LEED default)
            int toiletUsesPerDay = 3;
            int urinalUsesPerDay = 2; // Male only
            int lavatoryUsesPerDay = 3;
            double lavatoryDuration = 0.25; // minutes
            double showerUsesPerDay = 0.1;
            double showerDuration = 5; // minutes

            // Baseline annual water use (gallons)
            double baselineAnnual = occupants * workdaysPerYear * (
                (toiletUsesPerDay * baselineToiletGPF) +
                (urinalUsesPerDay * baselineUrinalGPF * 0.5) + // 50% male
                (lavatoryUsesPerDay * baselineLavatoryGPM * lavatoryDuration) +
                (showerUsesPerDay * baselineShowerGPM * showerDuration));

            // Design annual water use
            double designAnnual = occupants * workdaysPerYear * (
                (toiletUsesPerDay * toiletGPF) +
                (urinalUsesPerDay * urinalGPF * 0.5) +
                (lavatoryUsesPerDay * lavatoryGPM * lavatoryDuration) +
                (showerUsesPerDay * showerGPM * showerDuration));

            double reductionPercent = ((baselineAnnual - designAnnual) / baselineAnnual) * 100;

            // LEED points (WE Prerequisite requires 20%, credits for more)
            int leedPoints = 0;
            if (reductionPercent >= 50) leedPoints = 6;
            else if (reductionPercent >= 45) leedPoints = 5;
            else if (reductionPercent >= 40) leedPoints = 4;
            else if (reductionPercent >= 35) leedPoints = 3;
            else if (reductionPercent >= 30) leedPoints = 2;
            else if (reductionPercent >= 25) leedPoints = 1;

            return new WaterUseResult
            {
                Success = true,
                BaselineGallonsPerYear = baselineAnnual,
                DesignGallonsPerYear = designAnnual,
                ReductionPercent = reductionPercent,
                AnnualSavingsGallons = baselineAnnual - designAnnual,
                EstimatedLEEDPoints = leedPoints,
                MeetsPrerequisite = reductionPercent >= 20,
                StandardReference = "LEED v4.1 WE Credit"
            };
        }

        #endregion

        #region Facilities Management

        /// <summary>
        /// Calculate space efficiency metrics per BOMA/IFMA standards.
        /// </summary>
        public static SpaceEfficiencyResult CalculateSpaceEfficiency(
            double grossFloorAreaSqFt,
            double usableAreaSqFt,
            double rentableAreaSqFt,
            int totalOccupants,
            int workstations)
        {
            // BOMA metrics
            double efficiencyRatio = usableAreaSqFt / grossFloorAreaSqFt * 100;
            double loadFactor = rentableAreaSqFt / usableAreaSqFt;
            double sqFtPerPerson = usableAreaSqFt / totalOccupants;
            double sqFtPerWorkstation = usableAreaSqFt / workstations;

            // Benchmarks
            string efficiencyRating;
            if (efficiencyRatio >= 85) efficiencyRating = "Excellent";
            else if (efficiencyRatio >= 80) efficiencyRating = "Good";
            else if (efficiencyRatio >= 75) efficiencyRating = "Average";
            else efficiencyRating = "Below Average";

            // Cost metrics (typical)
            double estimatedRentPSF = 35; // Average office rent
            double annualRentCost = rentableAreaSqFt * estimatedRentPSF;
            double costPerPerson = annualRentCost / totalOccupants;

            return new SpaceEfficiencyResult
            {
                Success = true,
                GrossFloorArea = grossFloorAreaSqFt,
                UsableArea = usableAreaSqFt,
                RentableArea = rentableAreaSqFt,
                EfficiencyRatio = efficiencyRatio,
                LoadFactor = loadFactor,
                SqFtPerPerson = sqFtPerPerson,
                SqFtPerWorkstation = sqFtPerWorkstation,
                EfficiencyRating = efficiencyRating,
                EstimatedAnnualRent = annualRentCost,
                CostPerPerson = costPerPerson,
                StandardReference = "BOMA Z65.1 / IFMA"
            };
        }

        /// <summary>
        /// Calculate maintenance costs per RSMeans/IFMA benchmarks.
        /// </summary>
        public static MaintenanceCostResult CalculateMaintenanceCosts(
            double grossFloorAreaSqFt,
            string buildingType,
            int buildingAgeYears,
            string conditionRating = "Good")
        {
            // IFMA benchmark costs per sq ft (2024 data)
            var baseCosts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                {"Office", 7.50},
                {"Retail", 5.50},
                {"Hospital", 12.00},
                {"School", 6.00},
                {"Warehouse", 3.00},
                {"Manufacturing", 4.50}
            };

            double baseCostPSF = baseCosts.ContainsKey(buildingType) ? baseCosts[buildingType] : 6.00;

            // Age adjustment factor
            double ageFactor = 1.0;
            if (buildingAgeYears > 30) ageFactor = 1.5;
            else if (buildingAgeYears > 20) ageFactor = 1.3;
            else if (buildingAgeYears > 10) ageFactor = 1.15;

            // Condition adjustment
            double conditionFactor = conditionRating switch
            {
                "Excellent" => 0.8,
                "Good" => 1.0,
                "Fair" => 1.25,
                "Poor" => 1.6,
                _ => 1.0
            };

            double adjustedCostPSF = baseCostPSF * ageFactor * conditionFactor;
            double annualMaintenanceCost = grossFloorAreaSqFt * adjustedCostPSF;

            // Cost breakdown (typical percentages)
            var costBreakdown = new Dictionary<string, double>
            {
                {"HVAC", annualMaintenanceCost * 0.30},
                {"Electrical", annualMaintenanceCost * 0.15},
                {"Plumbing", annualMaintenanceCost * 0.10},
                {"Building Envelope", annualMaintenanceCost * 0.12},
                {"Interior Finishes", annualMaintenanceCost * 0.08},
                {"Grounds/Exterior", annualMaintenanceCost * 0.10},
                {"Janitorial", annualMaintenanceCost * 0.15}
            };

            return new MaintenanceCostResult
            {
                Success = true,
                AnnualMaintenanceCost = annualMaintenanceCost,
                CostPerSqFt = adjustedCostPSF,
                BaseCostPerSqFt = baseCostPSF,
                AgeFactor = ageFactor,
                ConditionFactor = conditionFactor,
                CostBreakdown = costBreakdown,
                BuildingType = buildingType,
                StandardReference = "IFMA Benchmark Report"
            };
        }

        /// <summary>
        /// Calculate equipment replacement schedule per ASHRAE lifecycle data.
        /// </summary>
        public static EquipmentLifecycleResult CalculateEquipmentLifecycle(
            string equipmentType,
            int currentAgeYears,
            double replacementCost,
            double annualMaintenanceCost)
        {
            // ASHRAE median service life (years)
            var serviceLives = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                {"Boiler-Cast Iron", 35},
                {"Boiler-Steel", 25},
                {"Chiller-Centrifugal", 23},
                {"Chiller-Screw", 20},
                {"Cooling Tower", 20},
                {"AHU", 20},
                {"Rooftop Unit", 15},
                {"Split System", 15},
                {"VAV Box", 20},
                {"Pump-Base Mounted", 20},
                {"Pump-Inline", 10},
                {"Fan-Centrifugal", 25},
                {"Transformer-Dry", 30},
                {"Switchgear", 30},
                {"Elevator-Hydraulic", 20},
                {"Elevator-Traction", 25},
                {"Roof-Built Up", 20},
                {"Roof-Single Ply", 20},
                {"Windows", 30},
                {"Carpet", 10},
                {"Paint", 5}
            };

            int expectedLife = serviceLives.ContainsKey(equipmentType) ? serviceLives[equipmentType] : 20;
            int remainingLife = Math.Max(0, expectedLife - currentAgeYears);
            double percentLifeUsed = (double)currentAgeYears / expectedLife * 100;

            // Replacement priority
            string priority;
            if (remainingLife <= 2) priority = "Immediate";
            else if (remainingLife <= 5) priority = "Near-Term (1-5 years)";
            else if (remainingLife <= 10) priority = "Mid-Term (5-10 years)";
            else priority = "Long-Term (10+ years)";

            // Simple lifecycle cost (no NPV for offline simplicity)
            double totalLifecycleCost = replacementCost + (annualMaintenanceCost * expectedLife);
            double annualizedCost = totalLifecycleCost / expectedLife;

            return new EquipmentLifecycleResult
            {
                Success = true,
                EquipmentType = equipmentType,
                ExpectedLifeYears = expectedLife,
                CurrentAgeYears = currentAgeYears,
                RemainingLifeYears = remainingLife,
                PercentLifeUsed = percentLifeUsed,
                ReplacementPriority = priority,
                ReplacementCost = replacementCost,
                TotalLifecycleCost = totalLifecycleCost,
                AnnualizedCost = annualizedCost,
                StandardReference = "ASHRAE Handbook - HVAC Applications Ch.37"
            };
        }

        #endregion
    }

    #region Result Classes

    public class OccupantLoadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int OccupantLoad { get; set; }
        public double LoadFactor { get; set; }
        public double FloorArea { get; set; }
        public string OccupancyType { get; set; }
        public string StandardReference { get; set; }
    }

    public class EgressResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double RequiredWidthInches { get; set; }
        public double RequiredWidthMM { get; set; }
        public double MinimumWidthInches { get; set; }
        public int ExitsRequired { get; set; }
        public int OccupantLoad { get; set; }
        public bool IsSprinklered { get; set; }
        public string StandardReference { get; set; }
    }

    public class TravelDistanceResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double MaxTravelDistanceFt { get; set; }
        public double MaxTravelDistanceM { get; set; }
        public string OccupancyGroup { get; set; }
        public bool IsSprinklered { get; set; }
        public string StandardReference { get; set; }
    }

    public class ParkingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int StandardSpaces { get; set; }
        public int AccessibleSpaces { get; set; }
        public int VanAccessibleSpaces { get; set; }
        public int TotalSpaces { get; set; }
        public double ParkingRatio { get; set; }
        public string RatioBasis { get; set; }
        public string UseType { get; set; }
        public string StandardReference { get; set; }
    }

    public class AccessibilityResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsCompliant { get; set; }
        public bool IsRamp { get; set; }
        public double RequiredWidth { get; set; }
        public double ActualWidth { get; set; }
        public double MaxAllowedSlope { get; set; }
        public double ActualSlope { get; set; }
        public bool HandrailsRequired { get; set; }
        public bool LandingsRequired { get; set; }
        public List<string> Issues { get; set; }
        public string StandardReference { get; set; }
    }

    public class AccessibleToiletResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsCompliant { get; set; }
        public double RequiredWidth { get; set; }
        public double RequiredDepth { get; set; }
        public double ActualWidth { get; set; }
        public double ActualDepth { get; set; }
        public double ToiletCenterlineFromWall { get; set; }
        public Dictionary<string, string> GrabBarRequirements { get; set; }
        public List<string> Issues { get; set; }
        public string StandardReference { get; set; }
    }

    public class AccessibleFixturesResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int AccessibleToilets { get; set; }
        public int AccessibleUrinals { get; set; }
        public int AccessibleLavatories { get; set; }
        public int HiLoDrinkingFountains { get; set; }
        public int TotalToilets { get; set; }
        public int TotalUrinals { get; set; }
        public int TotalLavatories { get; set; }
        public int TotalDrinkingFountains { get; set; }
        public string StandardReference { get; set; }
    }

    public class LiveLoadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double UniformLoadPSF { get; set; }
        public double UniformLoadKPa { get; set; }
        public double ReducedLoadPSF { get; set; }
        public double ReducedLoadKPa { get; set; }
        public double ConcentratedLoadLbs { get; set; }
        public double ConcentratedLoadKN { get; set; }
        public double ReductionFactor { get; set; }
        public double TributaryArea { get; set; }
        public string OccupancyType { get; set; }
        public string StandardReference { get; set; }
    }

    public class WindLoadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double BasicWindSpeed { get; set; }
        public double VelocityPressurePSF { get; set; }
        public double VelocityPressureKPa { get; set; }
        public double WindwardPressurePSF { get; set; }
        public double LeewardPressurePSF { get; set; }
        public double Kz { get; set; }
        public double Kzt { get; set; }
        public double Kd { get; set; }
        public string ExposureCategory { get; set; }
        public string StandardReference { get; set; }
    }

    public class SeismicLoadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double BaseShearKips { get; set; }
        public double BaseShearKN { get; set; }
        public double Cs { get; set; }
        public double SDs { get; set; }
        public double SD1 { get; set; }
        public double Fa { get; set; }
        public double Fv { get; set; }
        public string SeismicDesignCategory { get; set; }
        public double BuildingWeight { get; set; }
        public string StandardReference { get; set; }
    }

    public class LoadCombinationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, double> Combinations { get; set; }
        public string GoverningCombination { get; set; }
        public double GoverningLoadKips { get; set; }
        public double GoverningLoadKN { get; set; }
        public string DesignMethod { get; set; }
        public string StandardReference { get; set; }
    }

    public class DuctSizeResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double RoundDiameterInches { get; set; }
        public double RoundDiameterMM { get; set; }
        public double RectWidthInches { get; set; }
        public double RectHeightInches { get; set; }
        public double AirflowCFM { get; set; }
        public double VelocityFPM { get; set; }
        public double VelocityMPS { get; set; }
        public double FrictionRate { get; set; }
        public string StandardReference { get; set; }
    }

    public class PumpSizingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double FlowRateGPM { get; set; }
        public double FlowRateLPS { get; set; }
        public double TotalHeadFt { get; set; }
        public double TotalHeadM { get; set; }
        public double BrakeHorsepower { get; set; }
        public double BrakeKW { get; set; }
        public double MotorHorsepower { get; set; }
        public double MotorKW { get; set; }
        public double Efficiency { get; set; }
        public double NPSHRequired { get; set; }
        public string StandardReference { get; set; }
    }

    public class TransformerSizeResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double ConnectedLoadKVA { get; set; }
        public double DemandLoadKVA { get; set; }
        public double DesignLoadKVA { get; set; }
        public double TransformerSizeKVA { get; set; }
        public double FullLoadAmps { get; set; }
        public double ShortCircuitAmps { get; set; }
        public double ImpedancePercent { get; set; }
        public double DemandFactor { get; set; }
        public double GrowthFactor { get; set; }
        public string StandardReference { get; set; }
    }

    public class GeneratorSizeResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double TotalLoadKW { get; set; }
        public double RunningKVA { get; set; }
        public double StartingKVA { get; set; }
        public double RequiredKVA { get; set; }
        public double GeneratorSizeKW { get; set; }
        public double GeneratorSizeKVA { get; set; }
        public double FuelConsumptionGPH { get; set; }
        public double FuelConsumptionLPH { get; set; }
        public string Application { get; set; }
        public string StandardReference { get; set; }
    }

    public class EUIResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double EUIKBtuPerSqFt { get; set; }
        public double EUIKWhPerM2 { get; set; }
        public double BaselineEUI { get; set; }
        public double PercentBetterThanBaseline { get; set; }
        public int EstimatedLEEDPoints { get; set; }
        public string BuildingType { get; set; }
        public string StandardReference { get; set; }
    }

    public class WaterUseResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double BaselineGallonsPerYear { get; set; }
        public double DesignGallonsPerYear { get; set; }
        public double ReductionPercent { get; set; }
        public double AnnualSavingsGallons { get; set; }
        public int EstimatedLEEDPoints { get; set; }
        public bool MeetsPrerequisite { get; set; }
        public string StandardReference { get; set; }
    }

    public class SpaceEfficiencyResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double GrossFloorArea { get; set; }
        public double UsableArea { get; set; }
        public double RentableArea { get; set; }
        public double EfficiencyRatio { get; set; }
        public double LoadFactor { get; set; }
        public double SqFtPerPerson { get; set; }
        public double SqFtPerWorkstation { get; set; }
        public string EfficiencyRating { get; set; }
        public double EstimatedAnnualRent { get; set; }
        public double CostPerPerson { get; set; }
        public string StandardReference { get; set; }
    }

    public class MaintenanceCostResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double AnnualMaintenanceCost { get; set; }
        public double CostPerSqFt { get; set; }
        public double BaseCostPerSqFt { get; set; }
        public double AgeFactor { get; set; }
        public double ConditionFactor { get; set; }
        public Dictionary<string, double> CostBreakdown { get; set; }
        public string BuildingType { get; set; }
        public string StandardReference { get; set; }
    }

    public class EquipmentLifecycleResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string EquipmentType { get; set; }
        public int ExpectedLifeYears { get; set; }
        public int CurrentAgeYears { get; set; }
        public int RemainingLifeYears { get; set; }
        public double PercentLifeUsed { get; set; }
        public string ReplacementPriority { get; set; }
        public double ReplacementCost { get; set; }
        public double TotalLifecycleCost { get; set; }
        public double AnnualizedCost { get; set; }
        public string StandardReference { get; set; }
    }

    #endregion
}
