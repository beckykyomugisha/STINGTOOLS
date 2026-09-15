using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The <c>extended_params</c> section of PARAMETER_REGISTRY.json is the
    /// indirection layer every <c>ParamRegistry.Ext("KEY")</c> call reads. A key
    /// pointed at the wrong parameter name does not fail: Ext returns the wrong
    /// name, ParameterHelpers writes to a parameter that is either absent or
    /// semantically unrelated, and nothing is logged.
    ///
    /// That is not hypothetical. Until 2026-09 the key BLE_ROOM_NUM mapped to
    /// ASS_ROOM_NUM_TXT. The two are deliberately different parameters:
    ///
    ///   ASS_ROOM_*  bound &lt;ALL&gt;  — the room an ASSET sits in
    ///   BLE_ROOM_*  bound Rooms   — the room's OWN identity
    ///
    /// so MapRoomNameNumber wrote a Room's number into the asset-side slot,
    /// BLE_ROOM_NUM_TXT was never written by anything, and the room-number
    /// column of every Room schedule naming it exported blank.
    ///
    /// Both assertions below are validated against shipped data rather than
    /// against the map itself, so neither can pass by agreeing with the thing
    /// it is checking.
    /// </summary>
    public class ExtendedParamMapTests
    {
        /// <summary>The parameter-family prefixes that carry meaning about what an
        /// entry binds to. A key named for one family must not resolve into another.</summary>
        private static readonly string[] Families =
            { "ASS_", "BLE_", "PRJ_", "HVC_", "ELC_", "PLM_", "CLN_", "MGS_", "RAD_" };

        private static string DataPath(string name)
        {
            string p = Path.Combine(AppContext.BaseDirectory, "Data", name);
            if (!File.Exists(p))
                throw new FileNotFoundException(
                    "The shipped data file '" + name + "' was not copied to the test output. " +
                    "Without it this assertion would silently pass on nothing, which is the " +
                    "failure mode it exists to prevent. Check the <None Include> entries in " +
                    "StingTools.Tags.Tests.csproj.", p);
            return p;
        }

        /// <summary>Every (group, key, param_name) triple in extended_params.</summary>
        private static List<Tuple<string, string, string>> ExtendedParams()
        {
            var rows = new List<Tuple<string, string, string>>();
            var root = JObject.Parse(File.ReadAllText(DataPath("PARAMETER_REGISTRY.json")));
            var ext = root["extended_params"] as JObject;
            Assert.NotNull(ext);

            foreach (var group in ext)
            {
                var arr = group.Value as JArray;
                if (arr == null) continue;
                foreach (var item in arr.OfType<JObject>())
                {
                    string key = (string)item["key"];
                    string param = (string)item["param_name"];
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(param)) continue;
                    rows.Add(Tuple.Create(group.Key, key, param));
                }
            }

            // Guard the guard: an empty list would make both facts below vacuous.
            Assert.True(rows.Count > 100,
                "Only " + rows.Count + " extended_params entries parsed. The section is " +
                "either empty or its shape changed; these assertions would pass on nothing.");
            return rows;
        }

        /// <summary>The parameter names MR_PARAMETERS.txt actually declares.</summary>
        private static HashSet<string> SharedParameterNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(DataPath("MR_PARAMETERS.txt")))
            {
                string[] f = line.Split('\t');
                if (f.Length > 2 && f[0] == "PARAM" && !string.IsNullOrEmpty(f[2]))
                    names.Add(f[2]);
            }
            Assert.True(names.Count > 1000,
                "Only " + names.Count + " shared parameters parsed from MR_PARAMETERS.txt.");
            return names;
        }

        [Fact]
        public void Every_extended_param_resolves_to_a_declared_shared_parameter()
        {
            var declared = SharedParameterNames();

            var orphans = ExtendedParams()
                .Where(r => !declared.Contains(r.Item3))
                .Select(r => r.Item1 + "/" + r.Item2 + " -> " + r.Item3)
                .ToList();

            Assert.True(orphans.Count == 0,
                "extended_params keys resolve to parameter names that MR_PARAMETERS.txt " +
                "does not declare. Ext() returns the name, the write is discarded, and " +
                "nothing is logged:" + Environment.NewLine +
                "  " + string.Join(Environment.NewLine + "  ", orphans));
        }

        [Fact]
        public void A_key_named_for_one_parameter_family_resolves_into_that_family()
        {
            var crossed = new List<string>();

            foreach (var row in ExtendedParams())
            {
                string family = Families.FirstOrDefault(f =>
                    row.Item2.StartsWith(f, StringComparison.Ordinal));
                if (family == null) continue;   // unprefixed keys (ROOM_NUM, DESC) are free

                if (!row.Item3.StartsWith(family, StringComparison.Ordinal))
                    crossed.Add(row.Item1 + "/" + row.Item2 + " -> " + row.Item3 +
                                " (expected a " + family + "* parameter)");
            }

            Assert.True(crossed.Count == 0,
                "extended_params keys cross parameter families. The prefix is the binding " +
                "contract — BLE_* binds to its own element, ASS_* binds <ALL> — so a crossed " +
                "key writes a correct value into a slot that means something else:" +
                Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", crossed));
        }
    }
}
