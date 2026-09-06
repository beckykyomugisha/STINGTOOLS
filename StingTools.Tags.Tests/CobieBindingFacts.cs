using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// The shipped parameter facts a COBie target has to satisfy, read from the
    /// same three files the plugin ships.
    ///
    /// Loaded once. These are the only sources of truth about whether a parameter
    /// can actually receive a value:
    ///
    ///   PARAMETER_REGISTRY.json  does the parameter exist at all
    ///   RESOLVED_BINDINGS.csv    is it bound, and to which categories
    ///   MR_PARAMETERS.txt        what storage type does it have
    ///
    /// All three matter, and failing any of them fails identically at runtime:
    /// ParameterHelpers.SetString returns false and the value is discarded with
    /// no error. That is how eleven COBie columns came to be read from a
    /// spreadsheet and thrown away for years.
    /// </summary>
    internal static class CobieBindingFacts
    {
        private static readonly Lazy<HashSet<string>> _registered =
            new Lazy<HashSet<string>>(LoadRegistered);
        private static readonly Lazy<Dictionary<string, string[]>> _bindings =
            new Lazy<Dictionary<string, string[]>>(LoadBindings);
        private static readonly Lazy<Dictionary<string, string>> _datatypes =
            new Lazy<Dictionary<string, string>>(LoadDatatypes);

        public static HashSet<string> Registered { get { return _registered.Value; } }
        public static Dictionary<string, string[]> Bindings { get { return _bindings.Value; } }
        public static Dictionary<string, string> Datatypes { get { return _datatypes.Value; } }

        public static bool IsUniversal(string param)
        {
            string[] cats;
            return Bindings.TryGetValue(param, out cats) && cats.Length == 1 && cats[0] == "<ALL>";
        }

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

        private static HashSet<string> LoadRegistered()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = JToken.Parse(File.ReadAllText(DataPath("PARAMETER_REGISTRY.json")));
            Walk(root, names);
            if (names.Count == 0)
                throw new InvalidOperationException(
                    "PARAMETER_REGISTRY.json yielded no parameter names — an empty set would " +
                    "make every membership assertion below vacuously fail rather than test anything.");
            return names;
        }

        private static void Walk(JToken node, HashSet<string> into)
        {
            if (node is JObject obj)
            {
                JToken name;
                if (obj.TryGetValue("param_name", out name) && name.Type == JTokenType.String)
                    into.Add(name.ToString());
                foreach (var child in obj.PropertyValues())
                    Walk(child, into);
            }
            else if (node is JArray arr)
            {
                foreach (var child in arr)
                    Walk(child, into);
            }
        }

        private static Dictionary<string, string[]> LoadBindings()
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(DataPath("RESOLVED_BINDINGS.csv")))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int comma = line.IndexOf(',');
                if (comma <= 0) continue;
                string name = line.Substring(0, comma).Trim();
                string cats = line.Substring(comma + 1).Trim().Trim('"');
                if (name.Equals("Parameter_Name", StringComparison.OrdinalIgnoreCase)) continue;
                map[name] = cats == "<ALL>"
                    ? new[] { "<ALL>" }
                    : cats.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            }
            if (map.Count == 0)
                throw new InvalidOperationException("RESOLVED_BINDINGS.csv yielded no bindings.");
            return map;
        }

        private static Dictionary<string, string> LoadDatatypes()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int nameCol = 2, typeCol = 3;
            foreach (string raw in File.ReadAllLines(DataPath("MR_PARAMETERS.txt")))
            {
                if (raw.StartsWith("*PARAM"))
                {
                    var cols = raw.Split('\t');
                    int n = Array.FindIndex(cols, c => c == "NAME");
                    int t = Array.FindIndex(cols, c => c == "DATATYPE");
                    if (n > 0) nameCol = n;
                    if (t > 0) typeCol = t;
                    continue;
                }
                if (!raw.StartsWith("PARAM\t")) continue;
                var f = raw.Split('\t');
                if (f.Length > Math.Max(nameCol, typeCol))
                    map[f[nameCol]] = f[typeCol];
            }
            if (map.Count == 0)
                throw new InvalidOperationException("MR_PARAMETERS.txt yielded no parameter datatypes.");
            return map;
        }
    }
}
