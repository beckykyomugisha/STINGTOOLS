using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review MEP-4 / ACC-2. The LOD gate read every required parameter with
    /// GetString, which returns "" for anything that is not TEXT. The KUT matrix requires
    /// ASS_MAINTENANCE_FREQUENCY_MONTHS, which is NUMBER, so every Tier A element (mechanical,
    /// electrical and specialty equipment) failed rung 500 as "missing" however it was filled —
    /// the Deliverable D gate could never pass.
    /// </summary>
    public class LodRequiredParamTypeTests
    {
        private static string Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate the repo root");
            return dir.FullName;
        }

        private static Dictionary<string, string> DeclaredTypes()
        {
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(Repo(), "StingTools", "Data", "MR_PARAMETERS.txt")))
            {
                var f = line.Split('\t');
                if (f.Length > 3 && f[0] == "PARAM") types[f[2]] = f[3];
            }
            Assert.True(types.Count > 1000, $"read only {types.Count} parameter definitions");
            return types;
        }

        private static IEnumerable<string> RequiredParams(string path)
        {
            var found = new List<string>();
            void Walk(JToken t)
            {
                if (t is JObject o)
                    foreach (var p in o.Properties())
                        if (p.Name == "requiredParams" && p.Value is JArray a)
                            found.AddRange(a.Select(x => ((string)x).TrimStart('+')));
                        else Walk(p.Value);
                else if (t is JArray arr) foreach (var x in arr) Walk(x);
            }
            Walk(JToken.Parse(File.ReadAllText(path)));
            return found;
        }

        [Fact]
        public void TheGateReadsEveryNonTextRequiredParameterByValue()
        {
            var types = DeclaredTypes();
            var required = RequiredParams(Path.Combine(Repo(), "project-templates", "KUT", "_BIM_COORD", "lod_matrix.json"))
                .Concat(RequiredParams(Path.Combine(Repo(), "StingTools", "Data", "STING_LOD_MATRIX.json")))
                .Distinct().ToList();
            Assert.True(required.Count > 10, $"found only {required.Count} required parameters");

            var unknown = required.Where(p => !types.ContainsKey(p)).ToList();
            Assert.True(unknown.Count == 0, "required but not defined in MR_PARAMETERS.txt: " + string.Join(", ", unknown));

            var nonText = required.Where(p => types[p] != "TEXT").ToList();
            if (nonText.Count == 0) return;   // nothing to guard while every requirement is TEXT

            // A non-TEXT requirement exists (today ASS_MAINTENANCE_FREQUENCY_MONTHS, NUMBER), so the
            // engine must not read requirements with GetString.
            string engine = File.ReadAllText(Path.Combine(Repo(), "StingTools", "Core", "Validation", "LodVerificationEngine.cs"));
            var loop = Regex.Match(engine, @"foreach \(var p in check\.RequiredParams[^\n]*\n(?<body>(?:.*\n){1,12})", RegexOptions.None);
            Assert.True(loop.Success, "could not find the required-parameter loop in LodVerificationEngine.cs");
            string body = loop.Groups["body"].Value;
            Assert.DoesNotContain("GetString(el, p)", body);
            Assert.Contains("GetValueText(el, p)", body);
        }
    }
}
