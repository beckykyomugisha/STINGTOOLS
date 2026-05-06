// STING Tools — Standards region sidecar.
//
// Persists the active regional preset key into a per-project file
// (<project>/_BIM_COORD/sting_region.json) so the region travels with
// the .rvt even when the PROJECT_REGION shared parameter isn't bound
// to ProjectInformation. The Revit param is the preferred channel
// (visible in schedules, surveyable in the Properties palette); the
// sidecar is the resilient fallback.
//
// Read order at OnDocumentOpened: PROJECT_REGION param → sidecar →
// %APPDATA% manager state. Writes go to BOTH the param (if bound) and
// the sidecar so either store can recover the choice.

using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core
{
    public static class ProjectRegionSidecar
    {
        public static string GetSidecarPath(Document doc)
        {
            string rvt = doc?.PathName;
            if (string.IsNullOrEmpty(rvt)) return null;
            string dir = Path.GetDirectoryName(rvt);
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "_BIM_COORD", "sting_region.json");
        }

        public static string Read(Document doc)
        {
            try
            {
                string path = GetSidecarPath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                var obj = JsonConvert.DeserializeAnonymousType(json, new { region = "" });
                return string.IsNullOrWhiteSpace(obj?.region) ? null : obj.region;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectRegionSidecar.Read: {ex.Message}");
                return null;
            }
        }

        // Returns true on success.
        public static bool Write(Document doc, string region)
        {
            try
            {
                string path = GetSidecarPath(doc);
                if (string.IsNullOrEmpty(path)) return false;
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(new
                {
                    region,
                    written = DateTime.UtcNow.ToString("o")
                }, Formatting.Indented));
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectRegionSidecar.Write: {ex.Message}");
                return false;
            }
        }
    }
}
