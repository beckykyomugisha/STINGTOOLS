// EmergencyKeywordRegistry — loads the emergency keyword list for a document:
// corporate Data/STING_EMERGENCY_KEYWORDS.json, then the project override at
// <project>/_BIM_COORD/emergency_keywords.json (additive unless it sets
// "replaceBaseline": true). Falls back to EmergencyKeywords.BuiltIn() — which
// is identical to the shipped file — and logs, so a missing or broken file
// never degrades to "nothing is emergency".

using System;
using System.Collections.Concurrent;
using System.IO;
using Autodesk.Revit.DB;

namespace StingTools.Core.Electrical
{
    internal static class EmergencyKeywordRegistry
    {
        public const string CorporateFile = "STING_EMERGENCY_KEYWORDS.json";
        public const string ProjectFile = "emergency_keywords.json";

        private static EmergencyKeywords _corporate;
        private static DateTime _corporateStamp;
        private static readonly ConcurrentDictionary<string, (DateTime stamp, EmergencyKeywords kw)> _project =
            new ConcurrentDictionary<string, (DateTime, EmergencyKeywords)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Corporate baseline only (for callers with no document).</summary>
        public static EmergencyKeywords Corporate()
        {
            string path = null;
            try { path = StingToolsApp.FindDataFile(CorporateFile); }
            catch (Exception ex) { StingLog.Warn($"EmergencyKeywordRegistry locate: {ex.Message}"); }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (_corporate == null)
                    StingLog.Warn($"EmergencyKeywordRegistry: {CorporateFile} not found — using the built-in list.");
                return _corporate ??= EmergencyKeywords.BuiltIn();
            }
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_corporate != null && stamp == _corporateStamp) return _corporate;
            try
            {
                _corporate = EmergencyKeywords.Merge(EmergencyKeywords.FromJson(File.ReadAllText(path)), null);
            }
            catch (Exception ex)
            {
                StingLog.Error($"EmergencyKeywordRegistry: {path} unreadable — using the built-in list", ex);
                _corporate = EmergencyKeywords.BuiltIn();
            }
            _corporateStamp = stamp;
            return _corporate;
        }

        /// <summary>Corporate baseline + this project's override.</summary>
        public static EmergencyKeywords ForDocument(Document doc)
        {
            var baseline = Corporate();
            string path = null;
            try { path = StingPaths.MetaFile(doc, "_BIM_COORD", ProjectFile); }
            catch (Exception ex) { StingLog.Warn($"EmergencyKeywordRegistry project path: {ex.Message}"); }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return baseline;

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_project.TryGetValue(path, out var hit) && hit.stamp == stamp) return hit.kw;
            EmergencyKeywords merged;
            try
            {
                merged = EmergencyKeywords.Merge(baseline, EmergencyKeywords.FromJson(File.ReadAllText(path)));
            }
            catch (Exception ex)
            {
                StingLog.Error($"EmergencyKeywordRegistry: project override {path} unreadable — using the corporate list", ex);
                merged = baseline;
            }
            _project[path] = (stamp, merged);
            return merged;
        }
    }
}
