using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    // today. Fields no code checks stay in the files as reference.
    //
    // STING_HC_PHARMACY_USP.json is different: it is the ONE owner of the USP
    // <797>/<800> cascade (DSCH-25) and is read whole through UspCascadeData —
    // no code constant backs it, and an unusable file means NOT CHECKED.
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

        public const string UspFile = "STING_HC_PHARMACY_USP.json";
        private static StingTools.Core.Validation.Healthcare.UspCascadeFile _usp;
        private static List<string> _uspErrors = new List<string>();
        private static bool _uspLoaded;

        /// <summary>The USP &lt;797&gt;/&lt;800&gt; cascade, or null when the file is missing or
        /// invalid (reasons in <see cref="UspCascadeErrors"/>, logged once).</summary>
        public static StingTools.Core.Validation.Healthcare.UspCascadeFile UspCascadeData
        {
            get
            {
                if (_uspLoaded) return _usp;
                var j = Load(UspFile);
                _usp = StingTools.Core.Validation.Healthcare.UspCascade.Parse(
                    j == null || !j.HasValues ? null : j.ToString(), out var errors);
                _uspErrors = errors;
                if (_usp == null)
                    StingLog.Error($"HcSpecialistData: {UspFile} unusable — USP checks will report NOT CHECKED: " + string.Join("; ", errors));
                _uspLoaded = true;
                return _usp;
            }
        }

        public static IReadOnlyList<string> UspCascadeErrors { get { var _ = UspCascadeData; return _uspErrors; } }

        /// <summary>Drop cached files so an edit is picked up.</summary>
        public static void Reload() { _files.Clear(); _uspLoaded = false; _usp = null; }

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
