// ════════════════════════════════════════════════════════════════════════════
// Bs7671TableLayering — the project override for the BS 7671 Appendix 4 tables (ELEC-21).
//
// A project can replace, add or remove capacity tables and override the Table 4B1 / 4C1
// correction factors in <project>/_BIM_COORD/bs7671_wire_tables.json, on top of the
// corporate STING_WIRE_TABLES.json. Revit-free, so the rules below are unit-tested.
//
//   • Tables are keyed by what FindTable looks up — conductor / insulation / cable type /
//     reference method — not by id. A manufacturer table has its own id; keying on id
//     would ADD a second table the lookup never reaches.
//   • A supplied table replaces the corporate one WHOLE. No row-level merge: a table that
//     mixes corporate and manufacturer rows is a table no source printed.
//   • Every project row is single-source (VERIFY) unless the table carries a
//     twoSourceCheck naming two distinct sources, who checked and when. A row claiming
//     verified without one is downgraded, with a warning.
//   • Project data never reads as corporate: tables cite themselves as "project table …"
//     and the sizer's basis says the result was sized on project data.
//   • The override is atomic. One error and none of it applies — and the corporate data
//     is NOT used as a substitute either: LoadError is set, the table list is empty, and
//     every sizer refuses with the reason. Sizing on data the project has said is wrong
//     for it would be worse than sizing nothing.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Electrical
{
    public static class Bs7671TableLayering
    {
        public const string ProjectOverrideFileName = "bs7671_wire_tables.json";
        public const string OverrideSchema = "sting.bs7671WireTables.override/1";

        private static readonly HashSet<string> RootKeys = new HashSet<string>(StringComparer.Ordinal)
            { "schema", "source", "bs7671Appendix4" };
        private static readonly HashSet<string> SectionKeys = new HashSet<string>(StringComparer.Ordinal)
            { "capacityTables", "removeTables", "ambientTemperatureFactors", "groupingFactors", "semiEnclosedFuseFactorCf" };
        private static readonly HashSet<string> TableKeys = new HashSet<string>(StringComparer.Ordinal)
            { "id", "voltDropTable", "description", "conductor", "insulation", "cableType", "installMethod",
              "maxConductorTempC", "source", "twoSourceCheck", "sizes", "columns", "notes" };

        /// <summary>
        /// Read the corporate file and, when present, the project override, and layer them.
        /// Never throws: a read or parse failure becomes LoadError.
        /// </summary>
        public static Bs7671Data LoadLayered(string corporatePath, string overridePath)
        {
            JObject corporate;
            try
            {
                if (string.IsNullOrEmpty(corporatePath) || !File.Exists(corporatePath))
                    return Failed($"Corporate wire tables not found ({corporatePath ?? "no path"}); nothing can be sized.", null);
                corporate = JObject.Parse(File.ReadAllText(corporatePath));
            }
            catch (Exception ex)
            {
                return Failed($"Corporate wire tables {Path.GetFileName(corporatePath)} could not be read: {ex.Message}", null);
            }

            if (string.IsNullOrEmpty(overridePath) || !File.Exists(overridePath))
                return Layer(corporate, null, null);

            string label = "_BIM_COORD/" + Path.GetFileName(overridePath);
            JObject over;
            try { over = JObject.Parse(File.ReadAllText(overridePath)); }
            catch (Exception ex)
            {
                return Failed(RefusalText(label, new List<string> { $"it is not valid JSON ({ex.Message})" }), label);
            }
            return Layer(corporate, over, label);
        }

        /// <summary>Layer an override (may be null) on the corporate data.</summary>
        public static Bs7671Data Layer(JObject corporateRoot, JObject overrideRoot, string overrideLabel)
        {
            var d = Bs7671Data.FromJson(corporateRoot);
            if (d.Tables.Count == 0)
            {
                d.LoadError = "The corporate STING_WIRE_TABLES.json has no bs7671Appendix4 capacity tables; nothing can be sized.";
                return d;
            }
            if (overrideRoot == null) return d;

            string label = string.IsNullOrEmpty(overrideLabel) ? "_BIM_COORD/" + ProjectOverrideFileName : overrideLabel;
            var errors = Validate(overrideRoot, d);
            if (errors.Count > 0) return Failed(RefusalText(label, errors), label);

            var sec = (JObject)overrideRoot["bs7671Appendix4"];
            string rootSource = ((string)overrideRoot["source"] ?? "").Trim();
            d.HasProjectLayer = true;
            d.OverrideFile = label;

            foreach (var rm in (sec["removeTables"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string key = KeyOf(rm);
                var gone = d.Tables.First(t => Bs7671Data.Key(t) == key);
                d.Tables.Remove(gone);
                d.RemovedKeys.Add(key);
                d.ProjectChanges.Add($"removed Table {gone.Id} ({Describe(rm)})");
            }

            foreach (var jt in (sec["capacityTables"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var pt = Bs7671Data.ParseTable(jt, strictFlags: true);
                pt.Origin = Bs7671Origin.Project;
                string src = ((string)jt["source"] ?? "").Trim();
                pt.SourceNote = src.Length > 0 ? src : rootSource;
                pt.TwoSourceCheckNote = TwoSourceCheck(jt["twoSourceCheck"] as JObject, out string attestationProblem);
                if (pt.TwoSourceCheckNote == null)
                {
                    int downgraded = pt.Rows.Count(r => r.Verified || r.MvVerified);
                    foreach (var r in pt.Rows) { r.Verified = false; r.MvVerified = false; }
                    if (downgraded > 0)
                        d.Warnings.Add($"Project table {pt.Id}: {downgraded} row(s) claim verified, but " +
                                       (attestationProblem ?? "there is no twoSourceCheck") +
                                       " — treated as single-source (VERIFY).");
                }

                string key = Bs7671Data.Key(pt);
                int at = d.Tables.FindIndex(t => Bs7671Data.Key(t) == key);
                if (at >= 0)
                {
                    pt.ReplacedCorporateId = d.Tables[at].Id;
                    d.Tables[at] = pt;
                    d.ProjectChanges.Add($"replaced Table {pt.ReplacedCorporateId} ({DescribeTable(pt)}) with project table {pt.Id}");
                }
                else
                {
                    d.Tables.Add(pt);
                    d.ProjectChanges.Add($"added project table {pt.Id} ({DescribeTable(pt)})");
                }
            }

            if (sec["ambientTemperatureFactors"] is JObject amb)
                foreach (var p in amb.Properties().Where(p => !p.Name.StartsWith("_")))
                {
                    d.Ambient[p.Name] = Bs7671Data.ParseAmbient((JObject)p.Value);
                    d.AmbientFromProject.Add(p.Name);
                    d.ProjectChanges.Add($"Table 4B1 ambient factors for {p.Name}");
                }
            if (sec["groupingFactors"] is JObject grp)
                foreach (var p in grp.Properties().Where(p => !p.Name.StartsWith("_")))
                {
                    d.Grouping[p.Name] = Bs7671Data.ParseGrouping((JObject)p.Value);
                    d.GroupingFromProject.Add(p.Name);
                    d.ProjectChanges.Add($"Table 4C1 grouping factors for {p.Name}");
                }
            if (sec["semiEnclosedFuseFactorCf"] != null)
            {
                d.SemiEnclosedFuseCf = sec["semiEnclosedFuseFactorCf"].Value<double>();
                d.CfFromProject = true;
                d.ProjectChanges.Add($"semi-enclosed fuse factor Cf = {d.SemiEnclosedFuseCf:0.###}");
            }
            return d;
        }

        /// <summary>Every problem with an override; empty when it can be applied.
        /// <paramref name="corporate"/> is needed to check removeTables names a real table.</summary>
        public static List<string> Validate(JObject root, Bs7671Data corporate)
        {
            var e = new List<string>();
            if (root == null) { e.Add("the file is empty"); return e; }
            foreach (var p in root.Properties().Where(p => !RootKeys.Contains(p.Name) && !p.Name.StartsWith("_")))
                e.Add($"unknown key '{p.Name}' (only the bs7671Appendix4 section can be overridden)");
            if ((string)root["schema"] != OverrideSchema)
                e.Add($"schema must be \"{OverrideSchema}\"");
            if (!(root["bs7671Appendix4"] is JObject sec)) { e.Add("no bs7671Appendix4 object"); return e; }
            foreach (var p in sec.Properties().Where(p => !SectionKeys.Contains(p.Name) && !p.Name.StartsWith("_")))
                e.Add($"unknown key 'bs7671Appendix4.{p.Name}'");

            string rootSource = ((string)root["source"] ?? "").Trim();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var corporateKeys = new HashSet<string>((corporate?.Tables ?? new List<Bs7671CapacityTable>())
                                                     .Select(Bs7671Data.Key), StringComparer.OrdinalIgnoreCase);

            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rm in (sec["removeTables"] as JArray ?? new JArray()))
            {
                if (!(rm is JObject o)) { e.Add("removeTables entries must be objects"); continue; }
                string missing = RequiredMissing(o, "conductor", "insulation", "cableType", "installMethod");
                if (missing != null) { e.Add($"removeTables entry is missing {missing}"); continue; }
                string key = KeyOf(o);
                if (!corporateKeys.Contains(key)) e.Add($"removeTables names no corporate table ({Describe(o)})");
                else removed.Add(key);
            }

            int i = 0;
            foreach (var tok in (sec["capacityTables"] as JArray ?? new JArray()))
            {
                i++;
                if (!(tok is JObject t)) { e.Add($"capacityTables[{i}] is not an object"); continue; }
                string name = (string)t["id"] ?? $"capacityTables[{i}]";
                foreach (var p in t.Properties().Where(p => !TableKeys.Contains(p.Name) && !p.Name.StartsWith("_")))
                    e.Add($"{name}: unknown key '{p.Name}'");
                string missing = RequiredMissing(t, "id", "voltDropTable", "conductor", "insulation", "cableType", "installMethod");
                if (missing != null) { e.Add($"{name}: missing {missing}"); continue; }
                if ((t["maxConductorTempC"]?.Type != JTokenType.Integer && t["maxConductorTempC"]?.Type != JTokenType.Float)
                    || t["maxConductorTempC"].Value<double>() <= 0)
                    e.Add($"{name}: maxConductorTempC must be a number > 0");
                if (((string)t["source"] ?? "").Trim().Length == 0 && rootSource.Length == 0)
                    e.Add($"{name}: no source (set \"source\" on the table or the file)");
                string key = KeyOf(t);
                if (!seen.Add(key)) e.Add($"{name}: a second table for {Describe(t)}");
                if (removed.Contains(key)) e.Add($"{name}: {Describe(t)} is both removed and supplied");
                ValidateRows(t, name, e);
            }

            if (sec["ambientTemperatureFactors"] != null) ValidateFactors(sec["ambientTemperatureFactors"], "ambientTemperatureFactors", e, integerKeys: false);
            if (sec["groupingFactors"] != null) ValidateFactors(sec["groupingFactors"], "groupingFactors", e, integerKeys: true);
            if (sec["semiEnclosedFuseFactorCf"] != null)
            {
                var cf = sec["semiEnclosedFuseFactorCf"];
                if ((cf.Type != JTokenType.Float && cf.Type != JTokenType.Integer) || cf.Value<double>() <= 0 || cf.Value<double>() > 1)
                    e.Add("semiEnclosedFuseFactorCf must be a number in (0, 1]");
            }
            return e;
        }

        private static void ValidateRows(JObject t, string name, List<string> e)
        {
            if (!(t["sizes"] is JArray sizes) || sizes.Count == 0) { e.Add($"{name}: sizes is empty"); return; }
            var csas = new HashSet<double>();
            var rows = new List<(double csa, double it1, double it3)>();
            foreach (var tok in sizes)
            {
                if (!(tok is JObject r)) { e.Add($"{name}: a size row is not an object"); continue; }
                double csa = Number(r["csaMm2"], out bool csaOk);
                if (!csaOk || csa <= 0) { e.Add($"{name}: a row has no csaMm2 > 0"); continue; }
                if (!csas.Add(csa)) e.Add($"{name}: {csa:0.##} mm² appears twice");
                foreach (var col in new[] { "It_1ph", "It_3ph", "mVAm_1ph", "mVAm_3ph" })
                {
                    var v = r[col];
                    if (v == null || v.Type == JTokenType.Null) continue;
                    if ((v.Type != JTokenType.Float && v.Type != JTokenType.Integer) || v.Value<double>() < 0)
                        e.Add($"{name}: {csa:0.##} mm² {col} must be a number ≥ 0");
                }
                rows.Add((csa, Number(r["It_1ph"], out _), Number(r["It_3ph"], out _)));
            }
            if (rows.Count > 0 && rows.All(x => x.it1 <= 0 && x.it3 <= 0)) e.Add($"{name}: no row carries a current rating");
            rows.Sort((a, b) => a.csa.CompareTo(b.csa));
            foreach (var pick in new Func<(double csa, double it1, double it3), double>[] { x => x.it1, x => x.it3 })
            {
                double last = 0, lastCsa = 0;
                foreach (var x in rows)
                {
                    double v = pick(x);
                    if (v <= 0) continue;                        // 0 / null = not carried
                    if (v < last) { e.Add($"{name}: It falls from {last:0.#} A at {lastCsa:0.##} mm² to {v:0.#} A at {x.csa:0.##} mm² (columns swapped or a unit slip?)"); break; }
                    last = v; lastCsa = x.csa;
                }
            }
        }

        private static void ValidateFactors(JToken tok, string what, List<string> e, bool integerKeys)
        {
            if (!(tok is JObject o)) { e.Add($"{what} must be an object"); return; }
            foreach (var p in o.Properties().Where(p => !p.Name.StartsWith("_")))
            {
                if (!(p.Value is JObject map) || !map.Properties().Any(q => !q.Name.StartsWith("_")))
                { e.Add($"{what}.{p.Name} is empty"); continue; }
                foreach (var q in map.Properties().Where(q => !q.Name.StartsWith("_")))
                {
                    bool keyOk = integerKeys
                        ? int.TryParse(q.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0
                        : double.TryParse(q.Name, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                    if (!keyOk) e.Add($"{what}.{p.Name}: key '{q.Name}' is not a number");
                    var v = q.Value;
                    if ((v.Type != JTokenType.Float && v.Type != JTokenType.Integer) || v.Value<double>() <= 0 || v.Value<double>() > 1.5)
                        e.Add($"{what}.{p.Name}.{q.Name} must be a factor in (0, 1.5]");
                }
            }
        }

        /// <summary>A valid attestation as display text, or null (with the reason) when absent or insufficient.</summary>
        private static string TwoSourceCheck(JObject a, out string problem)
        {
            problem = null;
            if (a == null) return null;
            var sources = (a["sources"] as JArray ?? new JArray()).Select(x => ((string)x ?? "").Trim())
                                                                   .Where(x => x.Length > 0)
                                                                   .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string by = ((string)a["checkedBy"] ?? "").Trim(), date = ((string)a["date"] ?? "").Trim();
            if (sources.Count < 2) { problem = "its twoSourceCheck names fewer than two distinct sources"; return null; }
            if (by.Length == 0 || date.Length == 0) { problem = "its twoSourceCheck has no checkedBy or date"; return null; }
            return $"{string.Join(" + ", sources)}, checked by {by} on {date}";
        }

        /// <summary>What Cable_ReloadTables shows: what is in force and where it came from.</summary>
        public static string Describe(Bs7671Data d)
        {
            var s = new StringBuilder();
            if (d == null) return "No wire tables resolved.";
            if (!string.IsNullOrEmpty(d.LoadError))
            {
                s.AppendLine("NOT IN FORCE — nothing will be sized until this is fixed:");
                s.AppendLine(d.LoadError);
                return s.ToString();
            }
            int corp = d.Tables.Count(t => t.Origin == Bs7671Origin.Corporate);
            int proj = d.Tables.Count - corp;
            s.AppendLine($"Corporate STING_WIRE_TABLES.json: {corp} table(s) in force.");
            if (!d.HasProjectLayer) s.AppendLine("Project override: none.");
            else
            {
                s.AppendLine($"Project override {d.OverrideFile}: {proj} project table(s).");
                foreach (var c in d.ProjectChanges) s.AppendLine("  • " + c);
            }
            foreach (var w in d.Warnings) s.AppendLine("WARNING: " + w);
            s.AppendLine("Copper fault-current tables and conduit areas are corporate only; the override does not reach them.");
            return s.ToString();
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private static Bs7671Data Failed(string error, string label)
            => new Bs7671Data { LoadError = error, OverrideFile = label, HasProjectLayer = label != null };

        private static string RefusalText(string label, List<string> errors)
            => $"Project wire-table override {label} is invalid: {errors[0]}" +
               (errors.Count > 1 ? $" (+{errors.Count - 1} more: {string.Join("; ", errors.Skip(1))})" : "") +
               ". Nothing was sized. Fix or remove the file, then run Cable_ReloadTables. " +
               "Corporate tables were NOT used as a substitute.";

        private static string KeyOf(JObject o)
            => Bs7671Data.Key((string)o["conductor"], (string)o["insulation"], (string)o["cableType"], (string)o["installMethod"]);

        private static string Describe(JObject o)
            => $"{(string)o["conductor"]} / {(string)o["insulation"]} / {(string)o["cableType"]} / method {(string)o["installMethod"]}";

        private static string DescribeTable(Bs7671CapacityTable t)
            => $"{t.Conductor} / {t.Insulation} / {t.CableType} / method {t.InstallMethod}";

        private static string RequiredMissing(JObject o, params string[] keys)
        {
            foreach (var k in keys)
                if (((string)o[k] ?? "").Trim().Length == 0) return k;
            return null;
        }

        private static double Number(JToken t, out bool ok)
        {
            ok = t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer);
            return ok ? t.Value<double>() : 0;
        }
    }
}
