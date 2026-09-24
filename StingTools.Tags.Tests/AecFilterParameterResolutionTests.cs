// V-12b: every AEC filter rule must name a parameter the factory can resolve.
//
// AecFilterFactory.ResolveParamId turns a rule's "param" into an ElementId.
// When it cannot, the filter is SKIPPED with a warning buried in a result
// list — the filter simply never appears in the project. Measured at
// 9775cb213, two built-in names were not Revit enum members at all
// (ANALYTICAL_U_VALUE, REBAR_BAR_TYPE), one STING name did not exist
// (PLM_DEAD_LEG_BOOL), and nine third-party names resolved only if a project
// happened to define a parameter spelled exactly that way.
//
// The resolution routes below mirror ResolveParamId:
//   kind workset/phase/level  -> fixed BuiltInParameter, param text ignored
//   kind shared               -> STING GUID map (MR_PARAMETERS + registry),
//                                then a last-resort scan of project shared
//                                parameters BY NAME (the only route an
//                                external name can take)
//   no kind                   -> BuiltInParameter enum, then STING GUID map;
//                                there is NO name scan on this route
// So an external name without kind "shared" can never resolve, and that is
// checked too.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class AecFilterParameterResolutionTests
    {
        /// <summary>
        /// Names that are deliberately NOT STING parameters. Each resolves only
        /// through the factory's by-name scan, i.e. only in a project that
        /// defines it. Adding one here is a decision, so it needs a reason.
        /// </summary>
        private static readonly Dictionary<string, string> ExternalParameters =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COBie.Component"]     = "Autodesk COBie Extension parameter; present only where that add-in has set up the project.",
            ["COBie.Type.Category"] = "Autodesk COBie Extension parameter; present only where that add-in has set up the project.",
            ["BIM_LOD"]             = "Common project parameter for a BIMForum LOD number (100..500). STING's ASS_LOD_VERIFIED_TXT records a milestone id, not an LOD number, so it is not an equivalent.",
            ["AssetOwner"]          = "Client FM parameter (Tenant / Landlord). No STING equivalent - WS_OWNER_TXT is the workset owner.",
            ["ReplacementCycleYears"] = "Client FM parameter (a cycle in years). No STING equivalent - MNT_REPLACEMENT_YEAR is a calendar year, a different quantity.",
            ["SFG20Code"]           = "Client FM parameter carrying an SFG20 schedule code (M-/E-/P- prefix). CEQ_SFG20_REF_TXT is bound to clinical equipment only, so it cannot stand in on mechanical / electrical / pipe categories.",
        };

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "Data", "STING_AEC_FILTERS.json")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data");
            return dir.FullName;
        }

        private static HashSet<string> BuiltInParameters()
        {
            var path = Path.Combine(RepoRoot(), "StingTools.Tags.Tests", "Fixtures", "BuiltInParameterNames.txt");
            Assert.True(File.Exists(path), "BuiltInParameterNames.txt missing - run tools/DumpBuiltInParameterNames");
            var set = new HashSet<string>(
                File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")),
                StringComparer.OrdinalIgnoreCase);   // Enum.TryParse(..., ignoreCase: true)
            Assert.True(set.Count > 3000, $"only {set.Count} BuiltInParameter names - the fixture looks truncated");
            return set;
        }

        /// <summary>STING shared parameters: MR_PARAMETERS.txt PARAM rows plus
        /// every param_name the registry declares (ParamRegistry.AllParamGuids is
        /// built from both).</summary>
        private static HashSet<string> StingParameters()
        {
            var data = Path.Combine(RepoRoot(), "StingTools", "Data");
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(Path.Combine(data, "MR_PARAMETERS.txt")))
            {
                if (!line.StartsWith("PARAM\t", StringComparison.Ordinal)) continue;
                var cols = line.Split('\t');
                if (cols.Length > 2 && cols[2].Length > 0) set.Add(cols[2]);
            }
            var reg = JObject.Parse(File.ReadAllText(Path.Combine(data, "PARAMETER_REGISTRY.json")));
            foreach (var t in reg.Descendants().OfType<JProperty>().Where(p => p.Name == "param_name"))
                if (t.Value.Type == JTokenType.String) set.Add((string)t.Value);
            Assert.True(set.Count > 2000, $"only {set.Count} STING parameters parsed - the parser is broken, not the data");
            return set;
        }

        private static IEnumerable<(string filterId, string param, string kind)> Leaves()
        {
            var lib = JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "StingTools", "Data", "STING_AEC_FILTERS.json")));
            foreach (var f in (JArray)lib["filters"])
                foreach (var leaf in Walk(f["rule"] as JObject))
                    yield return ((string)f["id"], (string)leaf["param"], (string)leaf["kind"]);
        }

        private static IEnumerable<JObject> Walk(JObject rule)
        {
            if (rule == null) yield break;
            if (rule["rules"] is JArray kids)
            {
                foreach (var k in kids.OfType<JObject>())
                    foreach (var l in Walk(k)) yield return l;
            }
            else yield return rule;
        }

        [Fact]
        public void EveryFilterParameterResolvesByTheRouteItsKindTakes()
        {
            var bip = BuiltInParameters();
            var sting = StingParameters();
            var leaves = Leaves().ToList();
            Assert.True(leaves.Count > 300, $"only {leaves.Count} rule leaves parsed");

            var bad = new List<string>();
            foreach (var (id, param, kind) in leaves)
            {
                if (string.IsNullOrWhiteSpace(param)) { bad.Add($"{id}: rule has no param"); continue; }
                switch (kind)
                {
                    case "workset":
                    case "phase":
                    case "level":
                        continue; // fixed BuiltInParameter; the text is not looked up
                    case "shared":
                        if (sting.Contains(param) || ExternalParameters.ContainsKey(param)) continue;
                        bad.Add($"{id}: shared '{param}' is neither a STING parameter nor an allow-listed external name");
                        continue;
                    case null:
                        if (bip.Contains(param) || sting.Contains(param)) continue;
                        bad.Add(ExternalParameters.ContainsKey(param)
                            ? $"{id}: external '{param}' has no kind \"shared\" - the no-kind route never scans by name, so it cannot resolve"
                            : $"{id}: '{param}' is not a Revit 2025 BuiltInParameter nor a STING parameter");
                        continue;
                    default:
                        bad.Add($"{id}: unknown kind '{kind}' on '{param}'");
                        continue;
                }
            }
            Assert.True(bad.Count == 0, $"{bad.Count} filter rule(s) the factory would skip:\n  " + string.Join("\n  ", bad));
        }

        [Fact]
        public void EveryAllowListedExternalNameIsStillUsed()
        {
            // A stale allowlist entry is a hole a future typo can fall through.
            var used = new HashSet<string>(Leaves().Select(l => l.param), StringComparer.Ordinal);
            var stale = ExternalParameters.Keys.Where(k => !used.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "allow-listed but unused: " + string.Join(", ", stale));
        }

        [Fact]
        public void NoAllowListedExternalNameShadowsARealStingParameter()
        {
            var sting = StingParameters();
            var shadow = ExternalParameters.Keys.Where(sting.Contains).ToList();
            Assert.True(shadow.Count == 0, "external names that are actually STING parameters: " + string.Join(", ", shadow));
        }
    }
}
