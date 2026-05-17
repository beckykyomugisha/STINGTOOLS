using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.IfcResults
{
    /// <summary>
    /// Minimal line-oriented IFC STEP-format parser focused on the entities
    /// STING needs for the Phase 181 results-back path:
    ///   • IfcProject (one per file)
    ///   • IfcSpace (rooms — name + GlobalId)
    ///   • IfcLightFixture (luminaires — name + GlobalId)
    ///   • IfcRelDefinesByProperties + IfcPropertySet + IfcPropertySingleValue
    ///     (the PSet block carrying lux / UGR / uniformity values)
    ///
    /// We deliberately skip xbim / GeometryGym to keep the dependency surface
    /// small. The trade-off is that multi-line entity definitions (IFC parsers
    /// often wrap long attribute lists across lines) are pre-joined before
    /// regex matching; this handles the standard DIALux evo / ElumTools /
    /// Relux IFC outputs without bringing in 30 MB of NuGet packages.
    /// </summary>
    public class IfcSimpleParser
    {
        public class IfcEntity
        {
            public int Id { get; set; }
            public string Type { get; set; } = "";
            public string Raw  { get; set; } = "";
        }

        public class IfcSpace
        {
            public string GlobalId { get; set; } = "";
            public string Name     { get; set; } = "";
            public Dictionary<string, double> Numerics { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Strings  { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public class IfcLightFixture
        {
            public string GlobalId { get; set; } = "";
            public string Name     { get; set; } = "";
            public Dictionary<string, double> Numerics { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Strings  { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ProjectName { get; private set; } = "";
        public List<IfcSpace> Spaces { get; } = new List<IfcSpace>();
        public List<IfcLightFixture> LightFixtures { get; } = new List<IfcLightFixture>();
        public List<string> Warnings { get; } = new List<string>();

        public static IfcSimpleParser ParseFile(string path)
        {
            var p = new IfcSimpleParser();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                { p.Warnings.Add("File not found"); return p; }
                string text = File.ReadAllText(path);
                p.ParseInternal(text);
            }
            catch (Exception ex) { p.Warnings.Add($"Parse error: {ex.Message}"); }
            return p;
        }

        private void ParseInternal(string text)
        {
            // Re-flow: an IFC entity is always terminated by ");". Fold every
            // multi-line entity onto a single line so a simple regex picks
            // attributes correctly.
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '\r' || c == '\n') sb.Append(' ');
                else sb.Append(c);
            }
            string flat = sb.ToString();
            var entities = SplitEntities(flat);

            // Pass 1: index every entity by id; collect spaces / fixtures.
            var byId = new Dictionary<int, IfcEntity>();
            foreach (var e in entities)
            {
                if (e.Id > 0) byId[e.Id] = e;
                if (e.Type.Equals("IFCPROJECT", StringComparison.OrdinalIgnoreCase))
                    ProjectName = ExtractStringAt(e.Raw, 2);
                else if (e.Type.Equals("IFCSPACE", StringComparison.OrdinalIgnoreCase))
                {
                    Spaces.Add(new IfcSpace
                    {
                        GlobalId = ExtractStringAt(e.Raw, 0),
                        Name = ExtractStringAt(e.Raw, 2)
                    });
                }
                else if (e.Type.Equals("IFCLIGHTFIXTURE", StringComparison.OrdinalIgnoreCase))
                {
                    LightFixtures.Add(new IfcLightFixture
                    {
                        GlobalId = ExtractStringAt(e.Raw, 0),
                        Name = ExtractStringAt(e.Raw, 2)
                    });
                }
            }

            // Pass 2: walk IfcRelDefinesByProperties → IfcPropertySet →
            // IfcPropertySingleValue. We resolve each property's value and
            // attach it to the related space / fixture by GlobalId.
            var spacesByGuid = Spaces.ToDictionary(s => s.GlobalId, s => s);
            var fixturesByGuid = LightFixtures.ToDictionary(f => f.GlobalId, f => f);
            foreach (var rel in entities.Where(x => x.Type.Equals("IFCRELDEFINESBYPROPERTIES",
                StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var refs = ExtractRefList(rel.Raw, 4);            // related objects
                    int psetId = ExtractRefAt(rel.Raw, 5);            // relating PSet
                    if (psetId <= 0 || !byId.TryGetValue(psetId, out var psetEntity)) continue;
                    if (!psetEntity.Type.Equals("IFCPROPERTYSET", StringComparison.OrdinalIgnoreCase)) continue;
                    var propIds = ExtractRefList(psetEntity.Raw, 4);  // HasProperties
                    var props = new Dictionary<string, (double? num, string str)>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pid in propIds)
                    {
                        if (!byId.TryGetValue(pid, out var prop)) continue;
                        if (!prop.Type.Equals("IFCPROPERTYSINGLEVALUE",
                            StringComparison.OrdinalIgnoreCase)) continue;
                        string name = ExtractStringAt(prop.Raw, 0);
                        if (string.IsNullOrEmpty(name)) continue;
                        var value = ExtractValueAt(prop.Raw, 2);
                        props[name] = value;
                    }

                    foreach (var rid in refs)
                    {
                        if (!byId.TryGetValue(rid, out var related)) continue;
                        string guid = ExtractStringAt(related.Raw, 0);
                        if (spacesByGuid.TryGetValue(guid, out var space))
                            ApplyProps(space.Numerics, space.Strings, props);
                        else if (fixturesByGuid.TryGetValue(guid, out var fix))
                            ApplyProps(fix.Numerics, fix.Strings, props);
                    }
                }
                catch (Exception ex) { Warnings.Add($"Rel parse: {ex.Message}"); }
            }
        }

        private static void ApplyProps(Dictionary<string, double> nums, Dictionary<string, string> strs,
            Dictionary<string, (double? num, string str)> props)
        {
            foreach (var kv in props)
            {
                if (kv.Value.num.HasValue) nums[kv.Key] = kv.Value.num.Value;
                if (!string.IsNullOrEmpty(kv.Value.str)) strs[kv.Key] = kv.Value.str;
            }
        }

        // ── tokenisers ──────────────────────────────────────────────────

        private static readonly Regex EntityRegex =
            new Regex(@"#(\d+)\s*=\s*([A-Z0-9_]+)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static List<IfcEntity> SplitEntities(string flat)
        {
            var list = new List<IfcEntity>();
            // Find every "#NN=TYPE(" anchor and read characters up to the
            // matching ");" — the closing semicolon is unambiguous outside
            // string literals once the file is on one line.
            int pos = 0;
            while (pos < flat.Length)
            {
                var m = EntityRegex.Match(flat, pos);
                if (!m.Success) break;
                int idStart = m.Index + 1;
                int eqIdx = flat.IndexOf('=', idStart);
                int parenStart = flat.IndexOf('(', eqIdx);
                int idVal = int.TryParse(flat.Substring(idStart, eqIdx - idStart).Trim(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
                string type = flat.Substring(eqIdx + 1, parenStart - eqIdx - 1).Trim();
                int closeSemi = FindEntityEnd(flat, parenStart);
                if (closeSemi < 0) break;
                string raw = flat.Substring(parenStart, closeSemi - parenStart + 1);
                list.Add(new IfcEntity { Id = idVal, Type = type, Raw = raw });
                pos = closeSemi + 1;
            }
            return list;
        }

        /// <summary>Find the index of ");" terminating an entity, respecting nested parens and string literals.</summary>
        private static int FindEntityEnd(string flat, int parenStart)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = parenStart; i < flat.Length; i++)
            {
                char c = flat[i];
                if (inStr)
                {
                    if (c == '\'')
                    {
                        if (i + 1 < flat.Length && flat[i + 1] == '\'') { i++; continue; }
                        inStr = false;
                    }
                    continue;
                }
                if (c == '\'') { inStr = true; continue; }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        int j = i + 1;
                        while (j < flat.Length && char.IsWhiteSpace(flat[j])) j++;
                        if (j < flat.Length && flat[j] == ';') return j;
                        return i;
                    }
                }
            }
            return -1;
        }

        /// <summary>Split an entity's argument list into top-level tokens (commas at depth 0 only).</summary>
        public static List<string> SplitArgs(string raw)
        {
            var args = new List<string>();
            if (string.IsNullOrEmpty(raw)) return args;
            // raw includes the outer parens — strip them.
            int start = raw.IndexOf('(');
            int end   = raw.LastIndexOf(')');
            if (start < 0 || end <= start) return args;
            string body = raw.Substring(start + 1, end - start - 1);
            int depth = 0; bool inStr = false;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (inStr)
                {
                    if (c == '\'')
                    {
                        if (i + 1 < body.Length && body[i + 1] == '\'') { sb.Append("''"); i++; continue; }
                        inStr = false;
                    }
                    sb.Append(c); continue;
                }
                if (c == '\'') { inStr = true; sb.Append(c); continue; }
                if (c == '(') { depth++; sb.Append(c); continue; }
                if (c == ')') { depth--; sb.Append(c); continue; }
                if (c == ',' && depth == 0) { args.Add(sb.ToString().Trim()); sb.Clear(); continue; }
                sb.Append(c);
            }
            if (sb.Length > 0) args.Add(sb.ToString().Trim());
            return args;
        }

        public static string ExtractStringAt(string raw, int argIndex)
        {
            var args = SplitArgs(raw);
            if (argIndex < 0 || argIndex >= args.Count) return "";
            string a = args[argIndex];
            if (a == "$" || a == "*") return "";
            if (a.StartsWith("'") && a.EndsWith("'") && a.Length >= 2)
                return a.Substring(1, a.Length - 2).Replace("''", "'");
            return a;
        }

        public static int ExtractRefAt(string raw, int argIndex)
        {
            var args = SplitArgs(raw);
            if (argIndex < 0 || argIndex >= args.Count) return -1;
            string a = args[argIndex];
            if (a.StartsWith("#") && int.TryParse(a.Substring(1), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int v)) return v;
            return -1;
        }

        public static List<int> ExtractRefList(string raw, int argIndex)
        {
            var args = SplitArgs(raw);
            var ids = new List<int>();
            if (argIndex < 0 || argIndex >= args.Count) return ids;
            string a = args[argIndex];
            if (!a.StartsWith("(") || !a.EndsWith(")")) return ids;
            string inner = a.Substring(1, a.Length - 2);
            foreach (var part in inner.Split(','))
            {
                string t = part.Trim();
                if (t.StartsWith("#") && int.TryParse(t.Substring(1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int v)) ids.Add(v);
            }
            return ids;
        }

        /// <summary>
        /// Extract an IfcPropertySingleValue argument 2 — IFCREAL(x), IFCINTEGER(x),
        /// IFCLABEL('s'), IFCBOOLEAN(.T.) etc. Returns numeric where parseable.
        /// </summary>
        public static (double? num, string str) ExtractValueAt(string raw, int argIndex)
        {
            var args = SplitArgs(raw);
            if (argIndex < 0 || argIndex >= args.Count) return (null, "");
            string a = args[argIndex];
            if (a == "$" || a == "*") return (null, "");
            int lp = a.IndexOf('(');
            int rp = a.LastIndexOf(')');
            if (lp > 0 && rp > lp)
            {
                string body = a.Substring(lp + 1, rp - lp - 1).Trim();
                if (body.StartsWith("'") && body.EndsWith("'"))
                    return (null, body.Substring(1, body.Length - 2).Replace("''", "'"));
                if (double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    return (v, body);
                return (null, body);
            }
            if (a.StartsWith("'") && a.EndsWith("'") && a.Length >= 2)
                return (null, a.Substring(1, a.Length - 2).Replace("''", "'"));
            if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                return (n, a);
            return (null, a);
        }
    }
}
