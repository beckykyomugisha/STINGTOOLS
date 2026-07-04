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
            int catalogueOk = 0, catalogueBad = 0, symbolTotal = 0;

            try
            {
                // ── 1) Catalogue + seed non-empty Symbols check ───────────────
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
                    }
                }

                // ── 2) Standards fallback-chain resolution ────────────────────
                ValidateStandardsFallback(issues);

                // ── 3) Alias targets resolve to defined concepts ──────────────
                ValidateAliasTargets(issues);
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
            sb.AppendLine($"  Catalogues OK   : {catalogueOk} ({symbolTotal} symbols)");
            sb.AppendLine($"  Catalogues BAD  : {catalogueBad}");
            sb.AppendLine($"  Issues          : {issues.Count}");
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
    }
}
