using System;
using System.Collections.Generic;

namespace StingBIM.Standards.RSB
{
    /// <summary>
    /// RSB - Rwanda Standards Board
    /// National standards body for Rwanda
    /// 
    /// RSB is responsible for:
    /// - Development and promotion of standards
    /// - Quality assurance and certification
    /// - Metrology and calibration
    /// - Conformity assessment
    /// 
    /// Rwanda has adopted most EAS (East African Standards) and implements
    /// strict building codes, especially in Kigali (capital city).
    /// Rwanda is known for its stringent quality control and cleanliness standards.
    /// </summary>
    public static class RSBStandards
    {
        #region RS EAS 2 - Cement (Rwanda Implementation)

        /// <summary>
        /// Approved cement manufacturers in Rwanda
        /// </summary>
        public static readonly string[] ApprovedCementManufacturers = new[]
        {
            "CIMERWA - Rwanda's National Cement Factory",
            "PPC Rwanda",
            "Imported cement (requires RSB certification)"
        };

        /// <summary>
        /// Gets whether cement requires import permit
        /// </summary>
        public static bool RequiresImportPermit(string manufacturer)
        {
            // Only CIMERWA and PPC Rwanda are locally produced
            return !manufacturer.ToLower().Contains("cimerwa") && 
                   !manufacturer.ToLower().Contains("ppc rwanda");
        }

        /// <summary>
        /// Cement quality requirements for Rwanda's volcanic soil
        /// Rwanda sits on volcanic terrain which affects foundation design
        /// </summary>
        public static string[] GetVolcanicSoilCementRequirements()
        {
            return new[]
            {
                "Sulfate-resisting cement recommended for volcanic soils",
                "Minimum grade 42.5 for structural work",
                "Low alkali cement to prevent alkali-silica reaction",
                "Extended curing period (minimum 10 days) recommended"
            };
        }

        #endregion

        #region RS EAS 18 - Steel Reinforcement (Rwanda Implementation)

        /// <summary>
        /// Approved steel manufacturers and importers in Rwanda
        /// </summary>
        public static readonly string[] ApprovedSteelSuppliers = new[]
        {
            "Rwanda Steel Rolling Mills",
            "Certified imports from Kenya (KEBS approved)",
            "Certified imports from Tanzania (TBS approved)",
            "Certified imports from Uganda (UNBS approved)"
        };

        /// <summary>
        /// Gets corrosion protection requirements based on Rwanda's climate
        /// Rwanda has two rainy seasons: March-May and October-December
        /// </summary>
        public static string GetCorrosionProtection(string location)
        {
            if (location.ToLower().Contains("kigali"))
            {
                return "Standard concrete cover (25-40mm) adequate for Kigali's moderate climate";
            }
            else if (location.ToLower().Contains("lake") || 
                     location.ToLower().Contains("kivu"))
            {
                return "Increased cover (50mm) recommended near Lake Kivu due to higher humidity";
            }
            else
            {
                return "Standard protection with epoxy coating for long-term structures";
            }
        }

        #endregion

        #region RS 19 - Building Code for Rwanda

        /// <summary>
        /// Building zones in Rwanda
        /// </summary>
        public enum RwandaBuildingZone
        {
            /// <summary>Kigali City - Strict urban planning</summary>
            KigaliCity,
            /// <summary>Secondary cities (Huye, Musanze, Rubavu, Rusizi)</summary>
            SecondaryCities,
            /// <summary>Rural areas</summary>
            RuralAreas,
            /// <summary>Special economic zones</summary>
            SpecialEconomicZones
        }

        /// <summary>
        /// Gets building permit requirements for zone
        /// </summary>
        public static string[] GetBuildingPermitRequirements(RwandaBuildingZone zone)
        {
            var baseRequirements = new List<string>
            {
                "RSB approved architectural plans",
                "Structural engineer certification (for multi-story)",
                "Environmental impact assessment",
                "District approval",
                "Land title/lease agreement"
            };

            if (zone == RwandaBuildingZone.KigaliCity)
            {
                baseRequirements.AddRange(new[]
                {
                    "City of Kigali urban planning approval",
                    "Kigali Master Plan compliance",
                    "Parking provision as per zoning",
                    "Green building assessment (for large buildings)",
                    "Fire safety certification",
                    "Waste management plan"
                });
            }
            else if (zone == RwandaBuildingZone.SpecialEconomicZones)
            {
                baseRequirements.AddRange(new[]
                {
                    "Rwanda Development Board (RDB) approval",
                    "Fast-track processing available",
                    "Special incentives may apply"
                });
            }

            return baseRequirements.ToArray();
        }

        /// <summary>
        /// Minimum building standards for Rwanda
        /// Rwanda has high standards for construction quality
        /// </summary>
        public static class MinimumStandards
        {
            /// <summary>Minimum concrete grade for structures</summary>
            public const string MinimumConcreteGrade = "C25";

            /// <summary>Minimum steel grade for reinforcement</summary>
            public const string MinimumSteelGrade = "Grade 460";

            /// <summary>Minimum roof slope for Rwanda's rainfall (degrees)</summary>
            public const double MinimumRoofSlope = 15.0;

            /// <summary>Minimum ceiling height (m)</summary>
            public const double MinimumCeilingHeight = 2.7;

            /// <summary>Minimum window-to-floor area ratio (%)</summary>
            public const double MinimumWindowRatio = 10.0;

            /// <summary>
            /// Required setbacks from boundaries (m)
            /// </summary>
            public static (double Front, double Side, double Rear) GetRequiredSetbacks(
                RwandaBuildingZone zone)
            {
                return zone switch
                {
                    RwandaBuildingZone.KigaliCity => (5.0, 3.0, 3.0),
                    RwandaBuildingZone.SecondaryCities => (4.0, 2.5, 2.5),
                    RwandaBuildingZone.RuralAreas => (3.0, 2.0, 2.0),
                    RwandaBuildingZone.SpecialEconomicZones => (3.0, 2.0, 2.0),
                    _ => (3.0, 2.0, 2.0)
                };
            }
        }

        #endregion

        #region RS 120 - Electrical Installations

        /// <summary>
        /// Rwanda electrical supply characteristics
        /// </summary>
        public static readonly (double Voltage, double Frequency) RwandaPowerSupply = (230, 50);

        /// <summary>
        /// Electricity utility in Rwanda
        /// </summary>
        public const string ElectricityProvider = "REG - Rwanda Energy Group";

        /// <summary>
        /// Gets electrical connection requirements
        /// </summary>
        public static string[] GetElectricalConnectionRequirements(
            string buildingType,
            double loadKW)
        {
            var requirements = new List<string>
            {
                "REG (Rwanda Energy Group) connection approval",
                "RSB certified electrical materials",
                "Licensed electrician installation certificate",
                "Electrical inspection by RSB",
                "Wiring diagram and single line diagram"
            };

            if (loadKW > 50)
            {
                requirements.Add("Three-phase supply connection");
                requirements.Add("Load calculation and power factor study");
            }

            if (loadKW > 200)
            {
                requirements.Add("Dedicated transformer may be required");
                requirements.Add("REG engineering approval");
            }

            if (buildingType.ToLower().Contains("commercial") || 
                buildingType.ToLower().Contains("industrial"))
            {
                requirements.Add("Backup power system recommended");
                requirements.Add("Power quality monitoring equipment");
            }

            return requirements.ToArray();
        }

        /// <summary>
        /// Gets whether solar installation is encouraged
        /// Rwanda has strong solar energy programs
        /// </summary>
        public static bool IsSolarEnergyRecommended(string buildingType, double roofArea)
        {
            // Rwanda encourages solar energy
            if (roofArea > 100) return true;
            if (buildingType.ToLower().Contains("public")) return true;
            if (buildingType.ToLower().Contains("school")) return true;
            if (buildingType.ToLower().Contains("hospital")) return true;

            return false;
        }

        #endregion

        #region RS 256 - Water Supply and Sanitation

        /// <summary>
        /// Water utility in Rwanda
        /// </summary>
        public const string WaterProvider = "WASAC - Water and Sanitation Corporation";

        /// <summary>
        /// Gets water connection requirements
        /// </summary>
        public static string[] GetWaterConnectionRequirements(string location)
        {
            var requirements = new List<string>
            {
                "WASAC connection approval",
                "Plumbing drawings and specifications",
                "RSB approved plumbing materials",
                "Licensed plumber installation certificate",
                "Water meter installation",
                "Backflow prevention device"
            };

            if (location.ToLower().Contains("kigali"))
            {
                requirements.Add("Rainwater harvesting system (mandatory in Kigali)");
                requirements.Add("Water conservation fixtures required");
            }

            return requirements.ToArray();
        }

        /// <summary>
        /// Rainwater harvesting requirements for Rwanda
        /// Rwanda promotes water conservation
        /// </summary>
        public static (bool Mandatory, double MinimumCapacity) GetRainwaterHarvestingRequirements(
            double roofArea,
            string location)
        {
            bool mandatory = location.ToLower().Contains("kigali") && roofArea > 50;

            // Calculate minimum storage: 30% of annual harvestable
            double annualRainfall = 1000; // mm average for Rwanda
            double harvestableWater = roofArea * annualRainfall * 0.8; // 80% efficiency
            double minimumCapacity = harvestableWater * 0.3; // 30% minimum

            return (mandatory, minimumCapacity);
        }

        /// <summary>
        /// Sanitation requirements for Rwanda
        /// Rwanda has strict sanitation standards
        /// </summary>
        public static string[] GetSanitationRequirements(string buildingType)
        {
            var requirements = new List<string>
            {
                "Connection to sewer system where available",
                "Septic tank design per RS standards (where sewer unavailable)",
                "Proper ventilation of drainage system",
                "Grease traps for commercial kitchens",
                "Regular maintenance schedule"
            };

            if (buildingType.ToLower().Contains("public") || 
                buildingType.ToLower().Contains("commercial"))
            {
                requirements.Add("Separate facilities for men and women");
                requirements.Add("Facilities for persons with disabilities");
                requirements.Add("Regular cleaning and maintenance schedule");
            }

            return requirements.ToArray();
        }

        #endregion

        #region RS 340 - Fire Safety

        /// <summary>
        /// Fire safety requirements based on building type and height
        /// Rwanda has strict fire safety regulations especially in Kigali
        /// </summary>
        public static string[] GetFireSafetyRequirements(
            string buildingType,
            double buildingHeight,
            int occupantLoad)
        {
            var requirements = new List<string>
            {
                "Fire extinguishers per RS specifications",
                "Fire assembly point marked",
                "Emergency exit signage",
                "Fire safety training for occupants"
            };

            if (buildingHeight > 10 || occupantLoad > 50)
            {
                requirements.AddRange(new[]
                {
                    "Fire alarm system",
                    "Emergency lighting",
                    "Minimum two fire exits",
                    "Fire drill procedure documented"
                });
            }

            if (buildingHeight > 20 || occupantLoad > 200)
            {
                requirements.AddRange(new[]
                {
                    "Automatic fire detection system",
                    "Fire suppression system (sprinklers)",
                    "Fire fighter access",
                    "Pressurized stairwells",
                    "Fire department approval required"
                });
            }

            if (buildingType.ToLower().Contains("school") || 
                buildingType.ToLower().Contains("hospital") ||
                buildingType.ToLower().Contains("hotel"))
            {
                requirements.Add("Enhanced fire safety measures required");
                requirements.Add("Regular fire safety inspections");
                requirements.Add("Fire safety certificate renewal annually");
            }

            return requirements.ToArray();
        }

        /// <summary>
        /// Gets maximum travel distance to exit (m)
        /// </summary>
        public static double GetMaximumTravelDistance(bool sprinklered)
        {
            return sprinklered ? 45 : 30;
        }

        #endregion

        #region Seismic Considerations for Rwanda

        /// <summary>
        /// Seismic zones in Rwanda
        /// Rwanda is in the East African Rift System and has seismic activity
        /// </summary>
        public enum RwandaSeismicZone
        {
            /// <summary>Low seismicity - Eastern Rwanda</summary>
            Low_Eastern,
            /// <summary>Moderate seismicity - Central Rwanda (including Kigali)</summary>
            Moderate_Central,
            /// <summary>Higher seismicity - Western Rwanda (near Lake Kivu, volcanic)</summary>
            Higher_Western
        }

        /// <summary>
        /// Gets seismic zone for major cities
        /// </summary>
        public static RwandaSeismicZone GetSeismicZone(string location)
        {
            string loc = location.ToLower();

            if (loc.Contains("kigali") || loc.Contains("huye") || loc.Contains("gitarama"))
                return RwandaSeismicZone.Moderate_Central;
            else if (loc.Contains("gisenyi") || loc.Contains("kibuye") || 
                     loc.Contains("cyangugu") || loc.Contains("kivu"))
                return RwandaSeismicZone.Higher_Western;
            else
                return RwandaSeismicZone.Low_Eastern;
        }

        /// <summary>
        /// Gets seismic design requirements
        /// </summary>
        public static string[] GetSeismicDesignRequirements(
            RwandaSeismicZone zone,
            double buildingHeight)
        {
            var requirements = new List<string>();

            if (zone == RwandaSeismicZone.Higher_Western)
            {
                requirements.AddRange(new[]
                {
                    "Seismic design per Eurocode 8 required",
                    "Structural engineer certification mandatory",
                    "Regular structural inspections",
                    "Volcanic activity monitoring for western region"
                });

                if (buildingHeight > 15)
                {
                    requirements.Add("Dynamic seismic analysis required");
                    requirements.Add("Ductile detailing for concrete and steel");
                }
            }
            else if (zone == RwandaSeismicZone.Moderate_Central)
            {
                if (buildingHeight > 20)
                {
                    requirements.Add("Seismic considerations per Eurocode 8");
                    requirements.Add("Lateral load resisting system required");
                }
            }

            return requirements.ToArray();
        }

        /// <summary>
        /// Volcanic hazard considerations for western Rwanda
        /// Rwanda has active and dormant volcanoes near Lake Kivu
        /// </summary>
        public static string[] GetVolcanicHazardRequirements(string location)
        {
            if (location.ToLower().Contains("gisenyi") || 
                location.ToLower().Contains("kivu") ||
                location.ToLower().Contains("rubavu"))
            {
                return new[]
                {
                    "Volcanic risk assessment required",
                    "Lake Kivu gas monitoring (methane and CO2)",
                    "Emergency evacuation plan",
                    "Reinforced foundations for volcanic soil",
                    "Consultation with Rwanda Mines, Petroleum and Gas Board"
                };
            }

            return new string[0];
        }

        #endregion

        #region Rwanda-Specific Quality Standards

        /// <summary>
        /// Rwanda is known for strict quality control and cleanliness
        /// "Umuganda" - Community cleanliness day (last Saturday of month)
        /// </summary>
        public static class RwandaQualityStandards
        {
            /// <summary>
            /// Environmental cleanliness requirements during construction
            /// </summary>
            public static string[] GetConstructionSiteRequirements()
            {
                return new[]
                {
                    "Construction site must be kept clean at all times",
                    "No littering or waste disposal on site",
                    "Hoarding/fencing required around construction site",
                    "Dust control measures mandatory",
                    "No work on public holidays and Umuganda day",
                    "Respect for neighboring properties",
                    "Noise control between 6 PM - 7 AM"
                };
            }

            /// <summary>
            /// Plastic bag ban in Rwanda
            /// Rwanda has banned single-use plastic bags since 2008
            /// </summary>
            public static string[] GetPlasticBanCompliance()
            {
                return new[]
                {
                    "No single-use plastic bags allowed",
                    "Use paper or reusable bags for materials",
                    "Biodegradable packaging required",
                    "Heavy fines for plastic bag violations"
                };
            }

            /// <summary>
            /// Green building incentives in Rwanda
            /// </summary>
            public static string[] GetGreenBuildingIncentives()
            {
                return new[]
                {
                    "Tax incentives for green buildings",
                    "Fast-track approval for sustainable designs",
                    "EDGE certification encouraged",
                    "Solar energy subsidies available",
                    "Rainwater harvesting incentives"
                };
            }
        }

        #endregion

        #region Rwanda Development Board (RDB) Requirements

        /// <summary>
        /// RDB is the one-stop shop for investment and business in Rwanda
        /// </summary>
        public static class RDBRequirements
        {
            /// <summary>
            /// Gets RDB registration requirements for construction business
            /// </summary>
            public static string[] GetContractorRegistrationRequirements()
            {
                return new[]
                {
                    "Company registration with RDB",
                    "Tax Identification Number (TIN)",
                    "Social Security registration (CSR)",
                    "Professional liability insurance",
                    "Proof of technical capacity",
                    "Financial capacity statement",
                    "List of qualified personnel"
                };
            }

            /// <summary>
            /// Construction permit processing time in Rwanda
            /// Rwanda is known for fast business processes
            /// </summary>
            public static int GetPermitProcessingDays(bool fastTrack)
            {
                return fastTrack ? 7 : 30; // Days
            }
        }

        #endregion
    }
}
