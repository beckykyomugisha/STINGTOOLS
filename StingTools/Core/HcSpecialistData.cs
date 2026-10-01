using System;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    // ── HcSpecialistData ───────────────────────────────────────────────
    //
    // Reads the healthcare specialist reference files under
    // Data/Healthcare/Specialist/ (STING_HC_HYBRID_OR.json, STING_HC_MATERNITY.json,
    // ...). They are the stated source for HcOptions' specialist defaults:
    // the panel value wins, then the file, then the constant in code.
    //
    // A file value is used only where it agrees with the code's constant
    // today; where it disagrees, the caller keeps the code value and calls
    // KeepCode so the difference is logged rather than silently changing a
    // result. Fields no code checks stay in the files as reference.
    public static class HcSpecialistData
    {
        private static readonly ConcurrentDictionary<string, JObject> _files
            = new ConcurrentDictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The number at <paramref name="jsonPath"/> (Newtonsoft SelectToken syntax)
        /// in <paramref name="fileName"/>, or <paramref name="fallback"/> when the
        /// file, path or value is absent or not numeric.
        /// </summary>
        public static double Get(string fileName, string jsonPath, double fallback)
            => Number(fileName, jsonPath) ?? fallback;

        /// <summary>
        /// Returns <paramref name="codeValue"/> unchanged, logging once when the file
        /// carries a different value at <paramref name="jsonPath"/>. For limits where
        /// data and code disagree and the code value is deliberately kept.
        /// </summary>
        public static double KeepCode(string fileName, string jsonPath, double codeValue)
        {
            var data = Number(fileName, jsonPath);
            if (data.HasValue && Math.Abs(data.Value - codeValue) > 1e-9)
                StingLog.WarnRateLimited("HcSpecialistData." + fileName + ":" + jsonPath,
                    $"HcSpecialistData: {fileName} {jsonPath} = {data.Value:0.###}, code keeps {codeValue:0.###} (not changed until reconciled)");
            return codeValue;
        }

        /// <summary>Drop cached files so an edit is picked up.</summary>
        public static void Reload() => _files.Clear();

        private static double? Number(string fileName, string jsonPath)
        {
            try
            {
                var t = Load(fileName)?.SelectToken(jsonPath);
                if (t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer))
                {
                    double v = t.Value<double>();
                    if (!double.IsNaN(v) && !double.IsInfinity(v)) return v;
                }
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("HcSpecialistData.Read",
                    $"HcSpecialistData: {fileName} {jsonPath} unreadable: {ex.Message}");
            }
            return null;
        }

        private static JObject Load(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            return _files.GetOrAdd(fileName, f =>
            {
                try
                {
                    string path = StingToolsApp.FindDataFile(f);
                    if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(StingToolsApp.DataPath))
                        path = Path.Combine(StingToolsApp.DataPath, "Healthcare", "Specialist", f);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        StingLog.WarnRateLimited("HcSpecialistData.Missing",
                            $"HcSpecialistData: '{f}' not found; using code defaults");
                        return new JObject();
                    }
                    return JObject.Parse(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"HcSpecialistData: '{f}' unreadable ({ex.Message}); using code defaults");
                    return new JObject();
                }
            });
        }
    }
}
