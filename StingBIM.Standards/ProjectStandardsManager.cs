// ============================================================================
// StingBIM Standards - Project-Level Standards Configuration Manager
// Enables flexible switching between international building standards
// Supports regional presets and per-discipline customization
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingBIM.Standards
{
    /// <summary>
    /// Manages project-level standards configuration for flexible switching
    /// between international building codes and engineering standards.
    /// Singleton pattern ensures consistent standards across all calculations.
    /// </summary>
    public sealed class ProjectStandardsManager
    {
        #region Singleton Implementation

        private static readonly Lazy<ProjectStandardsManager> _instance =
            new Lazy<ProjectStandardsManager>(() => new ProjectStandardsManager());

        /// <summary>
        /// Gets the singleton instance of ProjectStandardsManager.
        /// </summary>
        public static ProjectStandardsManager Instance => _instance.Value;

        private ProjectStandardsManager()
        {
            _currentConfig = new ProjectStandardsConfig();
            LoadConfiguration();
        }

        #endregion

        #region Private Fields

        private ProjectStandardsConfig _currentConfig;
        private readonly object _configLock = new object();
        private string _configFilePath;

        #endregion

        #region Events

        /// <summary>
        /// Raised when standards configuration changes.
        /// Subscribe to update UI or recalculate values.
        /// </summary>
        public event EventHandler<StandardsChangedEventArgs> StandardsChanged;

        #endregion

        #region Public Properties - Current Standards

        /// <summary>
        /// Gets or sets the project region (affects default standards).
        /// </summary>
        public string Region
        {
            get { lock (_configLock) return _currentConfig.Region; }
            set { UpdateSetting(() => _currentConfig.Region = value, "Region", value); }
        }

        /// <summary>
        /// Gets or sets the electrical standard (IEC60364, BS7671, NEC, etc.).
        /// </summary>
        public string ElectricalStandard
        {
            get { lock (_configLock) return _currentConfig.ElectricalStandard; }
            set { UpdateSetting(() => _currentConfig.ElectricalStandard = value, "Electrical", value); }
        }

        /// <summary>
        /// Gets or sets the HVAC standard (ASHRAE, CIBSE, etc.).
        /// </summary>
        public string HVACStandard
        {
            get { lock (_configLock) return _currentConfig.HVACStandard; }
            set { UpdateSetting(() => _currentConfig.HVACStandard = value, "HVAC", value); }
        }

        /// <summary>
        /// Gets or sets the plumbing standard (IPC, UPC, BS EN, etc.).
        /// </summary>
        public string PlumbingStandard
        {
            get { lock (_configLock) return _currentConfig.PlumbingStandard; }
            set { UpdateSetting(() => _currentConfig.PlumbingStandard = value, "Plumbing", value); }
        }

        /// <summary>
        /// Gets or sets the structural standard (AISC, Eurocode3, BS5950, etc.).
        /// </summary>
        public string StructuralStandard
        {
            get { lock (_configLock) return _currentConfig.StructuralStandard; }
            set { UpdateSetting(() => _currentConfig.StructuralStandard = value, "Structural", value); }
        }

        /// <summary>
        /// Gets or sets the fire protection standard (NFPA13, BS9999, etc.).
        /// </summary>
        public string FireProtectionStandard
        {
            get { lock (_configLock) return _currentConfig.FireProtectionStandard; }
            set { UpdateSetting(() => _currentConfig.FireProtectionStandard = value, "FireProtection", value); }
        }

        /// <summary>
        /// Gets or sets the lighting standard (IES, CIBSE-L, EN12464, etc.).
        /// </summary>
        public string LightingStandard
        {
            get { lock (_configLock) return _currentConfig.LightingStandard; }
            set { UpdateSetting(() => _currentConfig.LightingStandard = value, "Lighting", value); }
        }

        /// <summary>
        /// Gets or sets the energy standard (ASHRAE90.1, LEED, BREEAM, etc.).
        /// </summary>
        public string EnergyStandard
        {
            get { lock (_configLock) return _currentConfig.EnergyStandard; }
            set { UpdateSetting(() => _currentConfig.EnergyStandard = value, "Energy", value); }
        }

        /// <summary>
        /// Gets or sets the unit system preference (Metric, Imperial, Mixed).
        /// </summary>
        public UnitSystem UnitSystem
        {
            get { lock (_configLock) return _currentConfig.UnitSystem; }
            set { UpdateSetting(() => _currentConfig.UnitSystem = value, "UnitSystem", value.ToString()); }
        }

        /// <summary>
        /// Gets whether strict compliance mode is enabled.
        /// When true, warnings are treated as errors.
        /// </summary>
        public bool StrictComplianceMode
        {
            get { lock (_configLock) return _currentConfig.StrictComplianceMode; }
            set { UpdateSetting(() => _currentConfig.StrictComplianceMode = value, "StrictCompliance", value.ToString()); }
        }

        #endregion

        #region Regional Presets

        /// <summary>
        /// Available regional presets with their default standards.
        /// </summary>
        public static readonly Dictionary<string, RegionalPreset> RegionalPresets = new Dictionary<string, RegionalPreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["USA"] = new RegionalPreset
            {
                Name = "United States",
                Region = "USA",
                ElectricalStandard = "NEC",
                HVACStandard = "ASHRAE",
                PlumbingStandard = "IPC",
                StructuralStandard = "AISC360",
                FireProtectionStandard = "NFPA13",
                LightingStandard = "IES",
                EnergyStandard = "ASHRAE90.1",
                UnitSystem = UnitSystem.Imperial
            },
            ["UK"] = new RegionalPreset
            {
                Name = "United Kingdom",
                Region = "UK",
                ElectricalStandard = "BS7671",
                HVACStandard = "CIBSE",
                PlumbingStandard = "BS-EN",
                StructuralStandard = "Eurocode3",
                FireProtectionStandard = "BS9999",
                LightingStandard = "CIBSE-L",
                EnergyStandard = "BREEAM",
                UnitSystem = UnitSystem.Metric
            },
            ["Europe"] = new RegionalPreset
            {
                Name = "European Union",
                Region = "Europe",
                ElectricalStandard = "IEC60364",
                HVACStandard = "EN-HVAC",
                PlumbingStandard = "EN-Plumbing",
                StructuralStandard = "Eurocode3",
                FireProtectionStandard = "EN-Fire",
                LightingStandard = "EN12464",
                EnergyStandard = "EPBD",
                UnitSystem = UnitSystem.Metric
            },
            ["EastAfrica"] = new RegionalPreset
            {
                Name = "East Africa (EAC)",
                Region = "EastAfrica",
                ElectricalStandard = "EAS-Electrical",
                HVACStandard = "ASHRAE",
                PlumbingStandard = "EAS-Plumbing",
                StructuralStandard = "EAS-Structural",
                FireProtectionStandard = "NFPA13",
                LightingStandard = "IES",
                EnergyStandard = "EAS-Energy",
                UnitSystem = UnitSystem.Metric
            },
            ["Uganda"] = new RegionalPreset
            {
                Name = "Uganda (UNBS)",
                Region = "Uganda",
                ElectricalStandard = "UNBS-Electrical",
                HVACStandard = "ASHRAE",
                PlumbingStandard = "UNBS-Plumbing",
                StructuralStandard = "UNBS-Structural",
                FireProtectionStandard = "NFPA13",
                LightingStandard = "IES",
                EnergyStandard = "UNBS-Energy",
                UnitSystem = UnitSystem.Metric
            },
            ["Kenya"] = new RegionalPreset
            {
                Name = "Kenya (KEBS)",
                Region = "Kenya",
                ElectricalStandard = "KEBS-Electrical",
                HVACStandard = "ASHRAE",
                PlumbingStandard = "KEBS-Plumbing",
                StructuralStandard = "KEBS-Structural",
                FireProtectionStandard = "NFPA13",
                LightingStandard = "IES",
                EnergyStandard = "KEBS-Energy",
                UnitSystem = UnitSystem.Metric
            },
            ["SouthAfrica"] = new RegionalPreset
            {
                Name = "South Africa (SANS)",
                Region = "SouthAfrica",
                ElectricalStandard = "SANS10142",
                HVACStandard = "SANS204",
                PlumbingStandard = "SANS10252",
                StructuralStandard = "SANS10162",
                FireProtectionStandard = "SANS10400",
                LightingStandard = "SANS10114",
                EnergyStandard = "SANS10400XA",
                UnitSystem = UnitSystem.Metric
            },
            ["Australia"] = new RegionalPreset
            {
                Name = "Australia/New Zealand",
                Region = "Australia",
                ElectricalStandard = "AS3008",
                HVACStandard = "AS1668",
                PlumbingStandard = "AS3500",
                StructuralStandard = "AS4100",
                FireProtectionStandard = "AS2118",
                LightingStandard = "AS1680",
                EnergyStandard = "NCC-Energy",
                UnitSystem = UnitSystem.Metric
            },
            ["International"] = new RegionalPreset
            {
                Name = "International (IEC/ISO)",
                Region = "International",
                ElectricalStandard = "IEC60364",
                HVACStandard = "ASHRAE",
                PlumbingStandard = "IPC",
                StructuralStandard = "Eurocode3",
                FireProtectionStandard = "NFPA13",
                LightingStandard = "IES",
                EnergyStandard = "ISO50001",
                UnitSystem = UnitSystem.Metric
            }
        };

        /// <summary>
        /// Applies a regional preset to the current configuration.
        /// </summary>
        /// <param name="regionKey">The region key (e.g., "USA", "UK", "EastAfrica").</param>
        public void ApplyRegionalPreset(string regionKey)
        {
            if (!RegionalPresets.TryGetValue(regionKey, out var preset))
            {
                throw new ArgumentException($"Unknown region: {regionKey}. Available: {string.Join(", ", RegionalPresets.Keys)}");
            }

            lock (_configLock)
            {
                _currentConfig.Region = preset.Region;
                _currentConfig.ElectricalStandard = preset.ElectricalStandard;
                _currentConfig.HVACStandard = preset.HVACStandard;
                _currentConfig.PlumbingStandard = preset.PlumbingStandard;
                _currentConfig.StructuralStandard = preset.StructuralStandard;
                _currentConfig.FireProtectionStandard = preset.FireProtectionStandard;
                _currentConfig.LightingStandard = preset.LightingStandard;
                _currentConfig.EnergyStandard = preset.EnergyStandard;
                _currentConfig.UnitSystem = preset.UnitSystem;

                SaveConfiguration();
            }

            StandardsChanged?.Invoke(this, new StandardsChangedEventArgs("Region", regionKey, $"Applied {preset.Name} preset"));
        }

        /// <summary>
        /// Gets all available regional preset names.
        /// </summary>
        public IEnumerable<string> GetAvailableRegions()
        {
            return RegionalPresets.Keys.OrderBy(k => k);
        }

        #endregion

        #region Standards Lookup for API Calls

        /// <summary>
        /// Gets the appropriate standard code for the StandardsAPI based on discipline.
        /// </summary>
        /// <param name="discipline">The engineering discipline.</param>
        /// <returns>The standard code to use in API calls.</returns>
        public string GetStandardForDiscipline(StandardsDiscipline discipline)
        {
            lock (_configLock)
            {
                switch (discipline)
                {
                    case StandardsDiscipline.Electrical:
                        return MapToAPIStandard(_currentConfig.ElectricalStandard, discipline);
                    case StandardsDiscipline.HVAC:
                        return MapToAPIStandard(_currentConfig.HVACStandard, discipline);
                    case StandardsDiscipline.Plumbing:
                        return MapToAPIStandard(_currentConfig.PlumbingStandard, discipline);
                    case StandardsDiscipline.Structural:
                        return MapToAPIStandard(_currentConfig.StructuralStandard, discipline);
                    case StandardsDiscipline.FireProtection:
                        return MapToAPIStandard(_currentConfig.FireProtectionStandard, discipline);
                    case StandardsDiscipline.Lighting:
                        return MapToAPIStandard(_currentConfig.LightingStandard, discipline);
                    case StandardsDiscipline.Energy:
                        return MapToAPIStandard(_currentConfig.EnergyStandard, discipline);
                    default:
                        return "International";
                }
            }
        }

        /// <summary>
        /// Maps user-friendly standard names to StandardsAPI parameter values.
        /// </summary>
        private string MapToAPIStandard(string userStandard, StandardsDiscipline discipline)
        {
            // Electrical mappings
            if (discipline == StandardsDiscipline.Electrical)
            {
                var electricalMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NEC"] = "NEC310",
                    ["BS7671"] = "BS7671",
                    ["IEC60364"] = "IEC60364",
                    ["AS3008"] = "AS/NZS3008.1",
                    ["SANS10142"] = "SANS10142",
                    ["UNBS-Electrical"] = "IEC60364",
                    ["KEBS-Electrical"] = "IEC60364",
                    ["EAS-Electrical"] = "IEC60364"
                };
                return electricalMap.TryGetValue(userStandard, out var mapped) ? mapped : "IEC60364";
            }

            // HVAC mappings
            if (discipline == StandardsDiscipline.HVAC)
            {
                var hvacMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ASHRAE"] = "ASHRAE",
                    ["CIBSE"] = "CIBSE",
                    ["EN-HVAC"] = "EN-HVAC"
                };
                return hvacMap.TryGetValue(userStandard, out var mapped) ? mapped : "ASHRAE";
            }

            // Plumbing mappings
            if (discipline == StandardsDiscipline.Plumbing)
            {
                var plumbingMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["IPC"] = "IPC",
                    ["UPC"] = "UPC",
                    ["BS-EN"] = "BS-EN",
                    ["AS3500"] = "AS3500"
                };
                return plumbingMap.TryGetValue(userStandard, out var mapped) ? mapped : "IPC";
            }

            // Structural mappings
            if (discipline == StandardsDiscipline.Structural)
            {
                var structuralMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AISC360"] = "AISC360",
                    ["Eurocode3"] = "Eurocode3",
                    ["BS5950"] = "BS5950",
                    ["AS4100"] = "AS4100"
                };
                return structuralMap.TryGetValue(userStandard, out var mapped) ? mapped : "AISC360";
            }

            // Fire protection mappings
            if (discipline == StandardsDiscipline.FireProtection)
            {
                var fireMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NFPA13"] = "NFPA13",
                    ["NFPA13R"] = "NFPA13R",
                    ["NFPA13D"] = "NFPA13D",
                    ["BS9999"] = "BS9999"
                };
                return fireMap.TryGetValue(userStandard, out var mapped) ? mapped : "NFPA13";
            }

            return userStandard;
        }

        #endregion

        #region Configuration Summary

        /// <summary>
        /// Gets a summary of the current standards configuration.
        /// </summary>
        public ProjectStandardsSummary GetConfigurationSummary()
        {
            lock (_configLock)
            {
                return new ProjectStandardsSummary
                {
                    Region = _currentConfig.Region,
                    Standards = new Dictionary<string, string>
                    {
                        ["Electrical"] = _currentConfig.ElectricalStandard,
                        ["HVAC"] = _currentConfig.HVACStandard,
                        ["Plumbing"] = _currentConfig.PlumbingStandard,
                        ["Structural"] = _currentConfig.StructuralStandard,
                        ["Fire Protection"] = _currentConfig.FireProtectionStandard,
                        ["Lighting"] = _currentConfig.LightingStandard,
                        ["Energy"] = _currentConfig.EnergyStandard
                    },
                    UnitSystem = _currentConfig.UnitSystem,
                    StrictMode = _currentConfig.StrictComplianceMode
                };
            }
        }

        /// <summary>
        /// Gets configuration as formatted string for display.
        /// </summary>
        public string GetConfigurationDisplay()
        {
            var summary = GetConfigurationSummary();
            var lines = new List<string>
            {
                $"Region: {summary.Region}",
                $"Unit System: {summary.UnitSystem}",
                $"Strict Mode: {(summary.StrictMode ? "Yes" : "No")}",
                "",
                "Standards by Discipline:"
            };

            foreach (var kvp in summary.Standards)
            {
                lines.Add($"  {kvp.Key}: {kvp.Value}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Loads configuration from file.
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                _configFilePath = GetConfigFilePath();

                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var loaded = JsonConvert.DeserializeObject<ProjectStandardsConfig>(json);
                    if (loaded != null)
                    {
                        _currentConfig = loaded;
                    }
                }
                else
                {
                    // Apply default preset (International)
                    ApplyRegionalPreset("International");
                }
            }
            catch
            {
                // Use defaults on error
                ApplyRegionalPreset("International");
            }
        }

        /// <summary>
        /// Saves current configuration to file.
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_currentConfig, Formatting.Indented);
                var directory = Path.GetDirectoryName(_configFilePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_configFilePath, json);
            }
            catch
            {
                // Silently fail - configuration will be in memory only
            }
        }

        private string GetConfigFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "StingBIM", "config", "project-standards.json");
        }

        #endregion

        #region Helper Methods

        private void UpdateSetting(Action updateAction, string settingName, string newValue)
        {
            lock (_configLock)
            {
                updateAction();
                SaveConfiguration();
            }

            StandardsChanged?.Invoke(this, new StandardsChangedEventArgs(settingName, newValue));
        }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Engineering discipline categories for standard selection.
    /// </summary>
    public enum StandardsDiscipline
    {
        Electrical,
        HVAC,
        Plumbing,
        Structural,
        FireProtection,
        Lighting,
        Energy
    }

    /// <summary>
    /// Unit system preferences.
    /// </summary>
    public enum UnitSystem
    {
        /// <summary>Metric (SI) units - meters, kilograms, Celsius</summary>
        Metric,
        /// <summary>Imperial (US) units - feet, pounds, Fahrenheit</summary>
        Imperial,
        /// <summary>Mixed units based on discipline conventions</summary>
        Mixed
    }

    /// <summary>
    /// Regional preset configuration.
    /// </summary>
    public class RegionalPreset
    {
        public string Name { get; set; }
        public string Region { get; set; }
        public string ElectricalStandard { get; set; }
        public string HVACStandard { get; set; }
        public string PlumbingStandard { get; set; }
        public string StructuralStandard { get; set; }
        public string FireProtectionStandard { get; set; }
        public string LightingStandard { get; set; }
        public string EnergyStandard { get; set; }
        public UnitSystem UnitSystem { get; set; }
    }

    /// <summary>
    /// Project standards configuration data.
    /// </summary>
    public class ProjectStandardsConfig
    {
        public string Region { get; set; } = "International";
        public string ElectricalStandard { get; set; } = "IEC60364";
        public string HVACStandard { get; set; } = "ASHRAE";
        public string PlumbingStandard { get; set; } = "IPC";
        public string StructuralStandard { get; set; } = "Eurocode3";
        public string FireProtectionStandard { get; set; } = "NFPA13";
        public string LightingStandard { get; set; } = "IES";
        public string EnergyStandard { get; set; } = "ISO50001";
        public UnitSystem UnitSystem { get; set; } = UnitSystem.Metric;
        public bool StrictComplianceMode { get; set; } = false;
    }

    /// <summary>
    /// Summary of current standards configuration.
    /// </summary>
    public class ProjectStandardsSummary
    {
        public string Region { get; set; }
        public Dictionary<string, string> Standards { get; set; }
        public UnitSystem UnitSystem { get; set; }
        public bool StrictMode { get; set; }
    }

    /// <summary>
    /// Event arguments for standards configuration changes.
    /// </summary>
    public class StandardsChangedEventArgs : EventArgs
    {
        public string SettingName { get; }
        public string NewValue { get; }
        public string Message { get; }
        public DateTime ChangedAt { get; }

        public StandardsChangedEventArgs(string settingName, string newValue, string message = null)
        {
            SettingName = settingName;
            NewValue = newValue;
            Message = message ?? $"{settingName} changed to {newValue}";
            ChangedAt = DateTime.UtcNow;
        }
    }

    #endregion

    #region StandardsAPI Extension Methods

    /// <summary>
    /// Extension methods for StandardsAPI integration with ProjectStandardsManager.
    /// </summary>
    public static class StandardsAPIExtensions
    {
        /// <summary>
        /// Calculate cable size using project-configured electrical standard.
        /// </summary>
        public static CableSizeResult CalculateCableSizeWithProjectStandard(
            double voltageV,
            double currentA,
            double lengthM,
            string conductorType = "Copper",
            string insulationType = "THHN",
            int conduitFill = 3,
            double ambientTempC = 30)
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Electrical);
            return StandardsAPI.CalculateCableSize(voltageV, currentA, lengthM, conductorType, insulationType, conduitFill, ambientTempC, standard);
        }

        /// <summary>
        /// Verify circuit breaker using project-configured electrical standard.
        /// </summary>
        public static CircuitBreakerResult VerifyCircuitBreakerWithProjectStandard(
            double loadCurrentA,
            double voltageV,
            string breakerType = "MCCB",
            bool typeCoordination = false)
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Electrical);
            return StandardsAPI.VerifyCircuitBreaker(loadCurrentA, voltageV, breakerType, standard, typeCoordination);
        }

        /// <summary>
        /// Calculate plumbing pipe size using project-configured plumbing standard.
        /// </summary>
        public static PipeSizeResult CalculatePipeSizeWithProjectStandard(
            double flowRateGPM,
            double lengthFt,
            string pipeType = "Copper",
            int numberOfFixtures = 0)
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Plumbing);
            return StandardsAPI.CalculatePlumbingPipeSize(flowRateGPM, lengthFt, pipeType, standard, numberOfFixtures);
        }

        /// <summary>
        /// Calculate drainage size using project-configured plumbing standard.
        /// </summary>
        public static DrainageSizeResult CalculateDrainageSizeWithProjectStandard(
            int numberOfFixtures,
            string fixtureType,
            double pipeSlope = 0.25)
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Plumbing);
            return StandardsAPI.CalculateDrainageSize(numberOfFixtures, fixtureType, pipeSlope, standard);
        }

        /// <summary>
        /// Design sprinkler system using project-configured fire protection standard.
        /// </summary>
        public static SprinklerResult DesignSprinklerSystemWithProjectStandard(
            double floorAreaM2,
            string occupancyType,
            string hazardClassification = "Light")
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.FireProtection);
            return StandardsAPI.DesignSprinklerSystem(floorAreaM2, occupancyType, hazardClassification, standard);
        }

        /// <summary>
        /// Design steel beam using project-configured structural standard.
        /// </summary>
        public static BeamDesignResult DesignSteelBeamWithProjectStandard(
            double spanM,
            double totalLoadKN,
            string loadType = "Uniform",
            string steelGrade = "A992")
        {
            var standard = ProjectStandardsManager.Instance.GetStandardForDiscipline(StandardsDiscipline.Structural);
            string designMethod = standard.Contains("AISC") ? "LRFD" : "Eurocode";
            return StandardsAPI.DesignSteelBeam(spanM, totalLoadKN, loadType, steelGrade, designMethod);
        }
    }

    #endregion
}
