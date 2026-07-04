// StingTools — Symbol data-integrity validator (read-only).
//
// Command tag: Symbols_Validate
//
// A pre-flight, on-demand check that catches the class of authoring defect that
// silently disabled the BS/CIBSE/NFPA SLD catalogues (top-level JSON key
// "symbolDefinitions" instead of "symbols", so SymbolLibrary.Symbols bound to an
// empty list and CreateAllFromFile returned "No symbols in library."). Verifies:
//
//   1. Every catalogue JSON under Data/Symbols (the SymbolBatchHelper.AllBatches
//      set) and every Data/Seeds/STING_SEED_*.json deserializes into a NON-EMPTY
//      SymbolLibrary.Symbols list against the production SymbolDefinition POCO.
//   2. Fallback chains in STING_SYMBOL_STANDARDS.json resolve to defined standards.
//   3. Alias targets in STING_SYMBOL_ALIASES.json resolve to defined concepts
//      (keys in STING_SYMBOL_CONCEPTS.json).
//
// Read-only: no transaction, no model change. Reports a summary via TaskDialog and
// logs every issue to StingTools.log.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Symbols;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Commands.Symbols
{
    [Transaction(TransactionMode.ReadOnly)]
    public class SymbolValidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var issues = new List<string>();
            int catalogueOk = 0, catalogueBad = 0, symbolTotal = 0, coordViolations = 0;
            int blindSpots = 0, connectorlessSeeds = 0, connectorDefects = 0;
            int refTotal = 0, danglingTotal = 0, danglingPrefix = 0, danglingViewCtx = 0, danglingAbsent = 0;

            try
            {
                // ── 1) Catalogue + seed non-empty Symbols check + coord range (1d) ─
                foreach (var (path, label) in EnumerateCatalogueFiles())
                {
                    if (!File.Exists(path))
                    {
                        // A trimmed data pack may legitimately omit optional catalogues;
                        // note it but don't count it as a hard failure.
                        issues.Add($"[missing] {label}: {Path.GetFileName(path)} not on disk (skipped).");
                        continue;
                    }

                    SymbolLibrary lib = null;
                    string parseErr = null;
                    try { lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(path)); }
                    catch (Exception ex) { parseErr = ex.Message; }

                    if (parseErr != null)
                    {
                        catalogueBad++;
                        issues.Add($"[parse-fail] {Path.GetFileName(path)}: {parseErr}");
                    }
                    else if (lib?.Symbols == null || lib.Symbols.Count == 0)
                    {
                        catalogueBad++;
                        issues.Add($"[empty] {Path.GetFileName(path)}: deserialized to 0 symbols " +
                                   "(check the top-level key is \"symbols\").");
                    }
                    else
                    {
                        catalogueOk++;
                        symbolTotal += lib.Symbols.Count;
                        int noId = lib.Symbols.Count(s => string.IsNullOrWhiteSpace(s.Id));
                        if (noId > 0)
                            issues.Add($"[warn] {Path.GetFileName(path)}: {noId} symbol(s) have no id.");
                        // 1d — geometry coordinate range: |value| > 2.0 is silently dropped
                        // at build by the creator's ValidateGeometryCoord.
                        foreach (var s in lib.Symbols)
                            coordViolations += ScanCoordViolations(s, Path.GetFileName(path), issues);
                    }
                }

                // ── 2) Standards fallback-chain resolution ────────────────────
                ValidateStandardsFallback(issues);

                // ── 3) Alias targets resolve to defined concepts ──────────────
                ValidateAliasTargets(issues);

                // ── 1a) Catalogue blind spots (symbol libs not registered) ────
                blindSpots = CheckCatalogueBlindSpots(issues);

                // ── 1b) Concept → family reference integrity ──────────────────
                CheckConceptFamilyIntegrity(issues,
                    out refTotal, out danglingTotal, out danglingPrefix, out danglingViewCtx, out danglingAbsent);

                // ── 1c) Connector completeness on MEP seeds ───────────────────
                connectorlessSeeds = CheckSeedConnectors(issues);

                // ── 1e) Raw-JSON connector validity (field-binding correctness) ─
                connectorDefects = CheckSeedConnectorValidity(issues);
            }
            catch (Exception ex)
            {
                StingLog.Error("Symbols_Validate", ex);
                msg = ex.Message;
                return Result.Failed;
            }

            // ── Report ────────────────────────────────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine("Symbol data-integrity validation");
            sb.AppendLine();
            sb.AppendLine($"  Catalogues OK        : {catalogueOk} ({symbolTotal} symbols)");
            sb.AppendLine($"  Catalogues BAD       : {catalogueBad}");
            sb.AppendLine($"  Catalogue blind spots: {blindSpots} (symbol libs not in AllBatches)");
            sb.AppendLine($"  Coord violations     : {coordViolations} (|value| > 2.0, dropped at build)");
            sb.AppendLine($"  Connector-less seeds : {connectorlessSeeds} (MEP-category, 0 connectors)");
            sb.AppendLine($"  Connector defects    : {connectorDefects} (malformed/unbound connector fields)");
            sb.AppendLine($"  Concept refs         : {refTotal}, dangling {danglingTotal} " +
                          $"(prefix-fixable {danglingPrefix} / view-context {danglingViewCtx} / absent {danglingAbsent})");
            sb.AppendLine($"  Issues               : {issues.Count}");
            if (issues.Count > 0)
            {
                sb.AppendLine();
                foreach (var i in issues.Take(30)) sb.AppendLine("  · " + i);
                if (issues.Count > 30) sb.AppendLine($"  … +{issues.Count - 30} more (see StingTools.log).");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("  All checks passed.");
            }

            foreach (var i in issues) StingLog.Warn($"Symbols_Validate: {i}");
            TaskDialog.Show("STING - Symbol Validation", sb.ToString());
            return Result.Succeeded;
        }

        /// <summary>
        /// Every catalogue file under Data/Symbols (from SymbolBatchHelper.AllBatches)
        /// plus every Data/Seeds/STING_SEED_*.json.
        /// </summary>
        private static IEnumerable<(string Path, string Label)> EnumerateCatalogueFiles()
        {
            foreach (var b in SymbolBatchHelper.AllBatches)
            {
                string p = StingToolsApp.FindDataFile("Symbols/" + b.File)
                    ?? StingToolsApp.FindDataFile(b.File);
                yield return (p ?? b.File, b.Label);
            }

            string dataPath = StingToolsApp.DataPath;
            if (!string.IsNullOrEmpty(dataPath))
            {
                string seedDir = Path.Combine(dataPath, "Seeds");
                if (Directory.Exists(seedDir))
                {
                    foreach (var f in Directory.GetFiles(seedDir, "STING_SEED_*.json"))
                        yield return (f, "Seed: " + Path.GetFileNameWithoutExtension(f));
                }
            }
        }

        private static void ValidateStandardsFallback(List<string> issues)
        {
            string path = StingToolsApp.FindDataFile("Symbols/STING_SYMBOL_STANDARDS.json")
                ?? StingToolsApp.FindDataFile("STING_SYMBOL_STANDARDS.json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                issues.Add("[missing] STING_SYMBOL_STANDARDS.json not found (fallback-chain check skipped).");
                return;
            }
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                var standards = root["standards"] as JObject;
                var defined = new HashSet<string>(
                    standards?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

                var chain = root["fallbackChain"] as JObject;
                if (chain == null) return;
                foreach (var prop in chain.Properties())
                {
                    // A source key that isn't (yet) a defined standard is inert config,
                    // not an error — it only matters that the fallback *target* resolves
                    // to a defined standard so the chain lands somewhere valid.
                    string target = prop.Value?.ToString();
                    if (!string.IsNullOrEmpty(target) && !defined.Contains(target))
                        issues.Add($"[standards] fallbackChain '{prop.Name}' → '{target}' targets an undefined standard.");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"[standards] parse failed: {ex.Message}");
            }
        }

        private static void ValidateAliasTargets(List<string> issues)
        {
            string aliasPath = StingToolsApp.FindDataFile("Symbols/STING_SYMBOL_ALIASES.json")
                ?? StingToolsApp.FindDataFile("STING_SYMBOL_ALIASES.json");
            string conceptPath = StingToolsApp.FindDataFile("Symbols/STING_SYMBOL_CONCEPTS.json")
                ?? StingToolsApp.FindDataFile("STING_SYMBOL_CONCEPTS.json");

            if (string.IsNullOrEmpty(aliasPath) || !File.Exists(aliasPath))
            {
                issues.Add("[missing] STING_SYMBOL_ALIASES.json not found (alias-target check skipped).");
                return;
            }
            if (string.IsNullOrEmpty(conceptPath) || !File.Exists(conceptPath))
            {
                issues.Add("[missing] STING_SYMBOL_CONCEPTS.json not found (alias-target check skipped).");
                return;
            }

            try
            {
                var conceptsRoot = JObject.Parse(File.ReadAllText(conceptPath));
                var concepts = conceptsRoot["concepts"] as JObject;
                var conceptIds = new HashSet<string>(
                    concepts?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);

                var aliasRoot = JObject.Parse(File.ReadAllText(aliasPath));
                var aliases = aliasRoot["aliases"] as JObject;
                if (aliases == null) return;
                foreach (var prop in aliases.Properties())
                {
                    string target = prop.Value?.ToString();
                    if (!string.IsNullOrEmpty(target) && !conceptIds.Contains(target))
                        issues.Add($"[alias] '{prop.Name}' → '{target}' targets an undefined concept.");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"[alias] parse failed: {ex.Message}");
            }
        }

        private static readonly string[] StdPrefixes = { "IEC_", "IEEE_", "BS_", "NFPA_", "CIBSE_" };

        // ── 1d) geometry coordinate range ─────────────────────────────────────
        private static int ScanCoordViolations(SymbolDefinition s, string file, List<string> issues)
        {
            if (s?.Geometry == null) return 0;
            int n = 0;
            void ChkLines(List<LineDefinition> lines)
            {
                if (lines == null) return;
                foreach (var l in lines)
                    if (Bad(l.X1) || Bad(l.Y1) || Bad(l.X2) || Bad(l.Y2)) n++;
            }
            void ChkArcs(List<ArcDefinition> arcs)
            {
                if (arcs == null) return;
                foreach (var a in arcs)
                    if (Bad(a.Cx) || Bad(a.Cy) || Bad(a.R)) n++;
            }
            void ChkText(List<TextDefinition> text)
            {
                if (text == null) return;
                foreach (var t in text) if (Bad(t.X) || Bad(t.Y)) n++;
            }
            ChkLines(s.Geometry.Lines);
            ChkLines(s.Geometry.ConnectionLines);
            ChkArcs(s.Geometry.Arcs);
            ChkText(s.Geometry.Text);
            if (s.Geometry.FilledRegions != null)
                foreach (var fr in s.Geometry.FilledRegions)
                    if (fr.Boundary != null)
                        foreach (var p in fr.Boundary) if (Bad(p.X) || Bad(p.Y)) n++;
            if (s.Geometry.Section != null)
            {
                ChkLines(s.Geometry.Section.Lines);
                ChkArcs(s.Geometry.Section.Arcs);
                ChkText(s.Geometry.Section.Text);
            }
            if (n > 0)
                issues.Add($"[coord] {file}:{s.Id}: {n} geometry coordinate(s) |value| > 2.0 — dropped at build.");
            return n;
        }

        private static bool Bad(double v) => Math.Abs(v) > 2.0;

        // ── 1a) catalogue blind spots ─────────────────────────────────────────
        private static int CheckCatalogueBlindSpots(List<string> issues)
        {
            string symDir = SymbolsDir();
            if (symDir == null || !Directory.Exists(symDir)) return 0;
            var registered = new HashSet<string>(
                SymbolBatchHelper.AllBatches.Select(b => b.File), StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var f in Directory.GetFiles(symDir, "*.json"))
            {
                string name = Path.GetFileName(f);
                if (registered.Contains(name)) continue;
                SymbolLibrary lib = null;
                try { lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(f)); }
                catch { continue; } // non-catalogue JSON (standards/concepts/aliases) — ignore
                if (lib?.Symbols != null && lib.Symbols.Count > 0)
                {
                    n++;
                    issues.Add($"[blind-spot] {name}: {lib.Symbols.Count} symbols but not in " +
                               "SymbolBatchHelper.AllBatches — invisible to the builder.");
                }
            }
            return n;
        }

        // ── 1b) concept → family reference integrity ──────────────────────────
        private static void CheckConceptFamilyIntegrity(List<string> issues,
            out int refTotal, out int dangling, out int prefixFixable, out int viewCtx, out int absent)
        {
            refTotal = dangling = prefixFixable = viewCtx = absent = 0;
            var catalogueIds = CollectCatalogueSymbolIds();
            string conceptPath = StingToolsApp.FindDataFile("Symbols/STING_SYMBOL_CONCEPTS.json")
                ?? StingToolsApp.FindDataFile("STING_SYMBOL_CONCEPTS.json");
            if (string.IsNullOrEmpty(conceptPath) || !File.Exists(conceptPath) || catalogueIds.Count == 0)
            {
                issues.Add("[concept-refs] concepts file or catalogue ids unavailable — reference check skipped.");
                return;
            }
            var absentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var concepts = JObject.Parse(File.ReadAllText(conceptPath))["concepts"] as JObject;
                if (concepts == null) return;
                foreach (var cp in concepts.Properties())
                {
                    var maps = (cp.Value as JObject)?["standardMappings"] as JObject;
                    if (maps == null) continue;
                    foreach (var sp in maps.Properties())
                    {
                        if (!(sp.Value is JObject m)) continue;
                        // base refs
                        foreach (var kind in new[] { "genericAnnotation", "tagFamily" })
                        {
                            string v = m[kind]?.ToString();
                            if (string.IsNullOrEmpty(v)) continue;
                            refTotal++;
                            if (catalogueIds.Contains(v)) continue;
                            dangling++;
                            string stripped = StripPrefix(v);
                            if (stripped != v && catalogueIds.Contains(stripped))
                            {
                                prefixFixable++;
                                issues.Add($"[concept-ref] {cp.Name}[{sp.Name}].{kind}: '{v}' — prefix-fixable to '{stripped}'.");
                            }
                            else { absent++; absentNames.Add(v); }
                        }
                        // view-context / scale-variant refs
                        foreach (var ovKey in new[] { "viewContextOverrides", "scaleVariants" })
                        {
                            if (!(m[ovKey] is JObject ov)) continue;
                            foreach (var op in ov.Properties())
                            {
                                string v = op.Value?.ToString();
                                if (string.IsNullOrEmpty(v)) continue;
                                refTotal++;
                                if (catalogueIds.Contains(v)) continue;
                                dangling++; viewCtx++;
                            }
                        }
                    }
                }
                if (absentNames.Count > 0)
                    issues.Add($"[concept-refs] {absentNames.Count} unique family name(s) referenced by concepts " +
                               "are not defined in any catalogue (authoring backlog — see ROADMAP; full list in log).");
                foreach (var a in absentNames.OrderBy(x => x))
                    StingLog.Warn($"Symbols_Validate: absent concept ref → {a}");
            }
            catch (Exception ex) { issues.Add($"[concept-refs] parse failed: {ex.Message}"); }
        }

        // ── 1c) connector completeness on MEP seeds ───────────────────────────
        private static readonly string[] MepCategoryKeys =
        {
            "Duct", "Pipe", "Electrical", "Plumbing", "Mechanical", "Fire Alarm", "FireAlarm",
            "Med Gas", "MedGas", "Sprinkler", "Air Terminal", "AirTerminal", "Lighting",
            "Fire Damper", "FireDamper", "Security", "Nurse Call", "NurseCall"
        };

        private static int CheckSeedConnectors(List<string> issues)
        {
            string seedDir = SeedsDir();
            if (seedDir == null || !Directory.Exists(seedDir)) return 0;
            int n = 0;
            foreach (var f in Directory.GetFiles(seedDir, "*.json"))
            {
                SymbolLibrary lib = null;
                try { lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(f)); }
                catch { continue; }
                if (lib?.Symbols == null) continue;
                foreach (var s in lib.Symbols)
                {
                    string haystack = $"{s.Category} {s.Discipline} {s.Id}";
                    bool isMep = MepCategoryKeys.Any(k =>
                        haystack.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!isMep) continue;
                    int cc = s.Connectors?.Count ?? 0;
                    if (s.TypeVariants != null)
                        foreach (var v in s.TypeVariants) cc += v.Connectors?.Count ?? 0;
                    if (cc == 0)
                    {
                        n++;
                        issues.Add($"[connectors] {Path.GetFileName(f)}:{s.Id} [{s.Category}] — MEP seed with 0 connectors.");
                    }
                }
            }
            return n;
        }

        // ── 1e) raw-JSON connector validity ───────────────────────────────────
        // Deserializing into ConnectorDefinition silently DROPS non-binding keys
        // (e.g. x/y/z instead of offsetX/offsetY/offsetZ), so the POCO shows valid-
        // looking connectors that actually collapsed to the origin at build. This
        // check inspects the RAW JSON to catch that class of defect.
        private static readonly HashSet<string> ConnectorAllowedKeys = new HashSet<string>(
            new[] { "domain", "systemType", "shape", "sizeMm", "widthMm", "heightMm",
                    "direction", "offsetX", "offsetY", "offsetZ", "facing" },
            StringComparer.Ordinal);
        private static readonly HashSet<string> ConnectorDomains = new HashSet<string>(
            new[] { "HVAC", "Piping", "Electrical", "Conduit", "CableTray" }, StringComparer.Ordinal);
        private static readonly HashSet<string> ConnectorFacings = new HashSet<string>(
            new[] { "+X", "-X", "+Y", "-Y", "+Z", "-Z" }, StringComparer.Ordinal);
        private static readonly HashSet<string> ConnectorDirections = new HashSet<string>(
            new[] { "In", "Out", "Bidirectional" }, StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<string>> ConnectorSystemTypes =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["HVAC"] = new HashSet<string>(new[] { "SupplyAir", "ReturnAir", "ExhaustAir" }, StringComparer.Ordinal),
            ["Piping"] = new HashSet<string>(new[] {
                "DomesticColdWater", "DomesticHotWater", "Sanitary", "FireProtectionWet",
                "FireProtectionDry", "FireProtectionPreaction", "ChilledWaterSupply",
                "ChilledWaterReturn", "HotWaterSupply", "HotWaterReturn", "Hydronic" }, StringComparer.Ordinal),
            ["Electrical"] = new HashSet<string>(new[] {
                "Power", "Lighting", "Data", "Telephone", "FireAlarm", "Security",
                "NurseCall", "Communication" }, StringComparer.Ordinal),
        };

        private static int CheckSeedConnectorValidity(List<string> issues)
        {
            string seedDir = SeedsDir();
            if (seedDir == null || !Directory.Exists(seedDir)) return 0;
            int defects = 0;
            foreach (var f in Directory.GetFiles(seedDir, "*.json"))
            {
                JObject root;
                try { root = JObject.Parse(File.ReadAllText(f)); }
                catch (Exception ex) { issues.Add($"[connector] {Path.GetFileName(f)}: JSON parse failed — {ex.Message}"); continue; }

                var syms = root["symbols"] as JArray;
                if (syms == null) continue;
                foreach (var sym in syms.OfType<JObject>())
                {
                    string sid = sym["id"]?.ToString() ?? "(no id)";
                    // symbol-level connectors + per-variant connectors (validate both;
                    // per-variant sets are ignored by the creator but still worth flagging).
                    defects += ValidateConnectorArray(sym["connectors"] as JArray, Path.GetFileName(f), sid, "", issues);
                    var variants = sym["typeVariants"] as JArray;
                    if (variants != null)
                    {
                        foreach (var v in variants.OfType<JObject>())
                        {
                            string vn = v["name"]?.ToString() ?? "?";
                            defects += ValidateConnectorArray(v["connectors"] as JArray, Path.GetFileName(f), sid,
                                $"variant '{vn}' ", issues);
                        }
                    }
                }
            }
            return defects;
        }

        private static int ValidateConnectorArray(JArray connectors, string file, string sid, string scope,
            List<string> issues)
        {
            if (connectors == null) return 0;
            int defects = 0;
            var positions = new Dictionary<(double, double, double), int>();
            for (int i = 0; i < connectors.Count; i++)
            {
                var c = connectors[i] as JObject;
                if (c == null)
                {
                    Add(issues, ref defects, file, sid, scope, i, "not a JSON object.");
                    continue;
                }

                // (i) unknown / non-binding keys (comment keys starting with "_" allowed).
                foreach (var prop in c.Properties())
                {
                    if (prop.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    if (!ConnectorAllowedKeys.Contains(prop.Name))
                        Add(issues, ref defects, file, sid, scope, i,
                            $"unbound/unknown key '{prop.Name}' (dropped by Newtonsoft — did you mean offsetX/offsetY/offsetZ/facing?).");
                }

                // (ii) domain
                string domain = c["domain"]?.ToString();
                if (string.IsNullOrEmpty(domain))
                    Add(issues, ref defects, file, sid, scope, i, "missing 'domain'.");
                else if (!ConnectorDomains.Contains(domain))
                    Add(issues, ref defects, file, sid, scope, i, $"domain '{domain}' not recognised.");

                // (iii) systemType (required for the flow domains that carry one).
                string st = c["systemType"]?.ToString();
                if (!string.IsNullOrEmpty(domain) && ConnectorSystemTypes.TryGetValue(domain, out var allowedSt))
                {
                    if (string.IsNullOrEmpty(st))
                        Add(issues, ref defects, file, sid, scope, i, $"missing 'systemType' for domain '{domain}'.");
                    else if (!allowedSt.Contains(st))
                        Add(issues, ref defects, file, sid, scope, i, $"systemType '{st}' not recognised for domain '{domain}'.");
                }

                // (iv) direction (optional; validate when present).
                var dirTok = c["direction"];
                if (dirTok != null && !ConnectorDirections.Contains(dirTok.ToString()))
                    Add(issues, ref defects, file, sid, scope, i,
                        $"direction '{dirTok}' invalid (expected In/Out/Bidirectional).");

                // (v) facing (optional; validate when present).
                var faceTok = c["facing"];
                if (faceTok != null && !ConnectorFacings.Contains(faceTok.ToString()))
                    Add(issues, ref defects, file, sid, scope, i,
                        $"facing '{faceTok}' invalid (expected +X/-X/+Y/-Y/+Z/-Z).");

                // (vi) coincident position — missing offset key counts as 0.
                double ox = c["offsetX"]?.Value<double>() ?? 0.0;
                double oy = c["offsetY"]?.Value<double>() ?? 0.0;
                double oz = c["offsetZ"]?.Value<double>() ?? 0.0;
                var key = (ox, oy, oz);
                if (positions.TryGetValue(key, out int firstIdx))
                    Add(issues, ref defects, file, sid, scope, i,
                        $"coincident position ({ox},{oy},{oz}) with conn[{firstIdx}] — connectors collapsed to the same point.");
                else
                    positions[key] = i;
            }
            return defects;
        }

        private static void Add(List<string> issues, ref int defects, string file, string sid, string scope,
            int idx, string reason)
        {
            defects++;
            issues.Add($"[connector] {file}:{sid} {scope}conn[{idx}] — {reason}");
        }

        // ── shared helpers ────────────────────────────────────────────────────
        private static string SymbolsDir()
        {
            string dp = StingToolsApp.DataPath;
            return string.IsNullOrEmpty(dp) ? null : Path.Combine(dp, "Symbols");
        }

        private static string SeedsDir()
        {
            string dp = StingToolsApp.DataPath;
            return string.IsNullOrEmpty(dp) ? null : Path.Combine(dp, "Seeds");
        }

        private static HashSet<string> CollectCatalogueSymbolIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string symDir = SymbolsDir();
            if (symDir == null || !Directory.Exists(symDir)) return ids;
            foreach (var f in Directory.GetFiles(symDir, "*.json"))
            {
                SymbolLibrary lib = null;
                try { lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(f)); }
                catch { continue; }
                if (lib?.Symbols == null) continue;
                foreach (var s in lib.Symbols)
                    if (!string.IsNullOrWhiteSpace(s.Id)) ids.Add(s.Id);
            }
            return ids;
        }

        private static string StripPrefix(string name)
        {
            foreach (var p in StdPrefixes)
                if (name.StartsWith(p, StringComparison.Ordinal)) return name.Substring(p.Length);
            return name;
        }
    }
}
