// Phase 139 — Project building profile for the Placement Centre.
//
// Filters which placement rules activate based on building type and
// active standards.  Persisted to <project>/_BIM_COORD/placement_profile.json.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Per-project building profile.  Empty BuildingType / ActiveStandards
    /// means "no filter" — every rule is active.
    /// </summary>
    public class ProjectBuildingProfile
    {
        public string BuildingType { get; set; } = "";
        public string[] ActiveStandards { get; set; } = new string[0];
        public string OccupancyBasis { get; set; } = "MIXED";
        public double DefaultOccupancyDensityM2PerPerson { get; set; } = 10.0;
        public bool EnableWetZoneChecks { get; set; } = true;
        public bool EnableAccessibilityChecks { get; set; } = true;
        public bool EnableCoverageGuarantee { get; set; } = true;
        public bool EnforceApprovedDocumentL { get; set; } = false;
        public string BuildingTypeTable { get; set; } = "";

        public ProjectBuildingProfile Clone()
        {
            return new ProjectBuildingProfile
            {
                BuildingType                       = this.BuildingType,
                ActiveStandards                    = (string[])(this.ActiveStandards?.Clone() ?? new string[0]),
                OccupancyBasis                     = this.OccupancyBasis,
                DefaultOccupancyDensityM2PerPerson = this.DefaultOccupancyDensityM2PerPerson,
                EnableWetZoneChecks                = this.EnableWetZoneChecks,
                EnableAccessibilityChecks          = this.EnableAccessibilityChecks,
                EnableCoverageGuarantee            = this.EnableCoverageGuarantee,
                EnforceApprovedDocumentL           = this.EnforceApprovedDocumentL,
                BuildingTypeTable                  = this.BuildingTypeTable,
            };
        }
    }

    public static class ProjectBuildingProfileIO
    {
        public const string FileName = "placement_profile.json";

        public static ProjectBuildingProfile Load(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath)) return new ProjectBuildingProfile();
            try
            {
                string dir = Path.GetDirectoryName(projectPath);
                if (string.IsNullOrEmpty(dir)) return new ProjectBuildingProfile();
                string path = Path.Combine(dir, "_BIM_COORD", FileName);
                if (!File.Exists(path)) return new ProjectBuildingProfile();
                var profile = JsonConvert.DeserializeObject<ProjectBuildingProfile>(File.ReadAllText(path));
                return profile ?? new ProjectBuildingProfile();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectBuildingProfileIO.Load: {ex.Message}");
                return new ProjectBuildingProfile();
            }
        }

        public static bool Save(string projectPath, ProjectBuildingProfile profile)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || profile == null) return false;
            try
            {
                string dir = Path.GetDirectoryName(projectPath);
                if (string.IsNullOrEmpty(dir)) return false;
                string folder = Path.Combine(dir, "_BIM_COORD");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, FileName);
                File.WriteAllText(path, JsonConvert.SerializeObject(profile, Formatting.Indented));
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectBuildingProfileIO.Save: {ex.Message}");
                return false;
            }
        }
    }
}
