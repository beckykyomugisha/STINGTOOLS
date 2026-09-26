// MepDesignDataLoader — Revit-side file lookup for MepDesignData.
//
// Reads the corporate file from the plugin's data folder and the project
// override from _BIM_COORD via StingPaths, fresh on every call: these files
// are small and read once per command, so an edit takes effect on the next
// run with no reload command.

using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Mep.Networks
{
    public static class MepDesignDataLoader
    {
        public const string SprinklerFile = "STING_SPRINKLER_DESIGN.json";
        public const string SprinklerProjectFile = "sprinkler_design.json";
        public const string GasFile = "STING_GAS_DESIGN.json";
        public const string GasProjectFile = "gas_design.json";
        public const string SmokeFile = "STING_SMOKE_CONTROL_DESIGN.json";
        public const string SmokeProjectFile = "smoke_control_design.json";

        public static SprinklerDesignData Sprinkler(Document doc, List<string> warnings)
        {
            var d = SprinklerDesignData.Parse(Layers(doc, SprinklerFile, SprinklerProjectFile, warnings, out var src));
            d.Sources.AddRange(src);
            return d;
        }

        public static GasDesignData Gas(Document doc, List<string> warnings)
        {
            var d = GasDesignData.Parse(Layers(doc, GasFile, GasProjectFile, warnings, out var src));
            d.Sources.AddRange(src);
            return d;
        }

        public static SmokeControlDesignData Smoke(Document doc, List<string> warnings)
        {
            var d = SmokeControlDesignData.Parse(Layers(doc, SmokeFile, SmokeProjectFile, warnings, out var src));
            d.Sources.AddRange(src);
            return d;
        }

        private static string[] Layers(Document doc, string corporate, string project,
            List<string> warnings, out List<string> sources)
        {
            sources = new List<string>();
            var layers = new List<string>();
            try
            {
                string path = StingToolsApp.FindDataFile(corporate);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    layers.Add(File.ReadAllText(path));
                    sources.Add(path);
                }
                else warnings?.Add($"{corporate} not found in the plugin data folder.");
            }
            catch (Exception ex) { warnings?.Add($"{corporate}: {ex.Message}"); StingLog.Warn($"MepDesignDataLoader {corporate}: {ex.Message}"); }

            if (doc != null)
            {
                try
                {
                    string pp = StingPaths.MetaFile(doc, "_BIM_COORD", project);
                    if (!string.IsNullOrEmpty(pp) && File.Exists(pp))
                    {
                        layers.Add(File.ReadAllText(pp));
                        sources.Add(pp);
                    }
                }
                catch (Exception ex) { warnings?.Add($"{project}: {ex.Message}"); StingLog.Warn($"MepDesignDataLoader {project}: {ex.Message}"); }
            }
            return layers.ToArray();
        }
    }
}
