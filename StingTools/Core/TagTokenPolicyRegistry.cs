// StingTools — tag token policy, per-document resolution.
//
// The thin Revit-bound half of TagTokenPolicy: it resolves the two file paths and
// caches the merged library per document. Every DECISION lives in TagTokenPolicy,
// which is Revit-free and unit-tested (StingTools.Tags.Tests).
//
// Cache shape mirrors MepSizingRegistry: keyed on doc.PathName, cleared wholesale by
// Reload so an edit to either file is picked up without restarting Revit.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    public static class TagTokenPolicyRegistry
    {
        private static readonly ConcurrentDictionary<string, TagTokenPolicyLibrary> _cache =
            new ConcurrentDictionary<string, TagTokenPolicyLibrary>(StringComparer.Ordinal);

        /// <summary>Warnings from the most recent load, for the tagging result to surface.
        /// Keyed the same way as the cache so two open projects do not overwrite each other.</summary>
        private static readonly ConcurrentDictionary<string, List<string>> _warnings =
            new ConcurrentDictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>The merged policy for this document. Never null — an empty library
        /// still resolves through TagTokenPolicy's built-in safety net.</summary>
        public static TagTokenPolicyLibrary Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc, key));
        }

        /// <summary>Warnings raised while loading this document's policy (malformed JSON,
        /// an unrecognised level). Empty when the load was clean.</summary>
        public static IReadOnlyList<string> WarningsFor(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            List<string> w;
            return _warnings.TryGetValue(key, out w) ? w : (IReadOnlyList<string>)new List<string>();
        }

        /// <summary>Force a reload from disk for every cached project.</summary>
        public static void Reload()
        {
            _cache.Clear();
            _warnings.Clear();
        }

        private static TagTokenPolicyLibrary Load(Document doc, string key)
        {
            var warnings = new List<string>();

            var baseline = new TagTokenPolicyLibrary();
            try
            {
                string path = StingToolsApp.FindDataFile(TagTokenPolicy.BaselineFileName);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    baseline = TagTokenPolicy.LoadFile(path, warnings);
                else
                    warnings.Add("The corporate baseline " + TagTokenPolicy.BaselineFileName +
                                 " was not found beside the plugin. Tag token substitution falls " +
                                 "back to the built-in literals.");
            }
            catch (Exception ex)
            {
                warnings.Add("Could not read " + TagTokenPolicy.BaselineFileName + ": " + ex.Message);
                StingLog.Warn("TagTokenPolicyRegistry baseline: " + ex.Message);
            }

            var project = new TagTokenPolicyLibrary();
            try
            {
                if (doc != null)
                    project = TagTokenPolicy.LoadFile(
                        StingPaths.MetaFile(doc, "_BIM_COORD", TagTokenPolicy.ProjectFileName),
                        warnings);
            }
            catch (Exception ex)
            {
                warnings.Add("Could not read the project tag token policy: " + ex.Message);
                StingLog.Warn("TagTokenPolicyRegistry project: " + ex.Message);
            }

            var merged = TagTokenPolicy.Merge(baseline, project);
            _warnings[key] = warnings;

            foreach (string w in warnings) StingLog.Warn("TagTokenPolicy: " + w);
            StingLog.Info("TagTokenPolicy loaded for '" + key + "': " +
                          (merged.Tokens != null ? merged.Tokens.Count : 0) + " token rule(s)");
            return merged;
        }
    }
}
