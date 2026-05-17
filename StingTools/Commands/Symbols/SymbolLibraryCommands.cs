// StingTools — MEP/FP/SLD Symbol Library commands (Phase 175)
//
// Six commands wrap the SymbolLibraryCreator engine:
//
//   * CreateSymbolLibraryCommand  — mints every JSON in data/Symbols
//   * CreateSLDSymbolsCommand     — SLD-only batch
//   * CreateLightingSymbolsCommand — Lighting-only batch
//   * CreateFPSymbolsCommand      — Fire-protection batch
//   * ReloadSymbolLibraryCommand  — re-load every previously-created .rfa
//   * InspectSymbolLibraryCommand — read-only diagnostic
//
// Output is written to <project>/_BIM_COORD/Families/Symbols/<group>/.

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

namespace StingTools.Commands.Symbols
{
    internal static class SymbolBatchHelper
    {
        public static readonly (string File, string Folder, string Label)[] AllBatches = new[]
        {
            ("STING_SLD_SYMBOLS.json",       "SLD/IEC",    "Single Line Diagram (IEC 60617)"),
            ("STING_SLD_SYMBOLS_IEEE.json",  "SLD/IEEE",   "Single Line Diagram (IEEE 315)"),
            ("STING_SLD_SYMBOLS_BS.json",    "SLD/BS",     "Single Line Diagram (BS EN 60617)"),
            ("STING_SLD_SYMBOLS_NFPA.json",  "SLD/NFPA",   "Single Line Diagram (NFPA 70)"),
            ("STING_SLD_SYMBOLS_CIBSE.json", "SLD/CIBSE",  "Building Services (CIBSE)"),
            ("STING_LIGHTING_SYMBOLS.json",  "Lighting",   "Lighting"),
            ("STING_FP_SYMBOLS.json",        "FireProt",   "Fire Protection"),
            ("STING_MEP_SYMBOLS.json",       "HVAC",       "HVAC / Mechanical"),
            ("STING_ELEC_SYMBOLS.json",      "Electrical", "Electrical Devices"),
            ("STING_PLUMBING_SYMBOLS.json",  "Plumbing",   "Plumbing"),
            ("STING_PIPE_ACCESSORIES.json",  "PipeAcc",    "Pipe Accessories"),
            ("STING_ISO6412_SYMBOLS.json",   "ISO6412",    "ISO 6412 Piping/Duct/Conduit Spool Symbols"),
        };

        public static string ResolveOutputRoot(Document doc)
        {
            string baseDir = null;
            try
            {
                if (!string.IsNullOrEmpty(doc.PathName))
                    baseDir = Path.GetDirectoryName(doc.PathName);
            }
            catch (Exception ex) { StingLog.Warn($"ResolveOutputRoot: {ex.Message}"); }

            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Path.GetTempPath(), "STING_Symbols");

            var outDir = Path.Combine(baseDir, "_BIM_COORD", "Families", "Symbols");
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        /// <summary>
        /// Returns the path to the project-level symbol_size_config.json.
        /// File is at &lt;project&gt;/_BIM_COORD/symbol_size_config.json.
        /// </summary>
        public static string ResolveSizeConfigPath(Document doc)
        {
            string baseDir = null;
            try
            {
                if (!string.IsNullOrEmpty(doc.PathName))
                    baseDir = Path.GetDirectoryName(doc.PathName);
            }
            catch { }
            if (string.IsNullOrEmpty(baseDir)) return null;
            return Path.Combine(baseDir, "_BIM_COORD", "symbol_size_config.json");
        }

        public static SymbolCreationResult RunBatch(Document doc, string jsonName, string subFolder,
            SymbolSizeConfig sizeConfig = null)
        {
            var aggregate = new SymbolCreationResult();
            string jsonPath = StingToolsApp.FindDataFile(jsonName);
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                aggregate.Errors.Add($"Data file not found: {jsonName}");
                return aggregate;
            }

            string outRoot = ResolveOutputRoot(doc);
            string outFolder = Path.Combine(outRoot, subFolder);
            Directory.CreateDirectory(outFolder);

            // Load project-level size config if caller didn't supply one.
            if (sizeConfig == null)
                sizeConfig = SymbolSizeConfig.LoadOrDefault(ResolveSizeConfigPath(doc));

            var r = SymbolLibraryCreator.CreateAllFromFile(doc, jsonPath, outFolder,
                loadIntoProject: true, sizeConfig: sizeConfig);
            aggregate.Created += r.Created;
            aggregate.Existed += r.Existed;
            aggregate.Failed  += r.Failed;
            aggregate.Warnings.AddRange(r.Warnings);
            aggregate.Errors.AddRange(r.Errors);
            aggregate.CreatedRfaPaths.AddRange(r.CreatedRfaPaths);
            return aggregate;
        }

        public static string FormatReport(string title, SymbolCreationResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"  • Created : {r.Created}");
            sb.AppendLine($"  • Existed : {r.Existed}");
            sb.AppendLine($"  • Failed  : {r.Failed}");
            if (r.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Warnings ({r.Warnings.Count}):");
                foreach (var w in r.Warnings.Take(25)) sb.AppendLine("  · " + w);
                if (r.Warnings.Count > 25) sb.AppendLine($"  … +{r.Warnings.Count - 25} more (StingTools.log)");
            }
            if (r.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Errors ({r.Errors.Count}):");
                foreach (var e in r.Errors.Take(15)) sb.AppendLine("  ✗ " + e);
                if (r.Errors.Count > 15) sb.AppendLine($"  … +{r.Errors.Count - 15} more (StingTools.log)");
            }

            foreach (var w in r.Warnings) StingLog.Warn($"SymbolLibrary: {w}");
            foreach (var e in r.Errors)   StingLog.Error($"SymbolLibrary: {e}");
            return sb.ToString();
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateSymbolLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbol Library", "No document open."); return Result.Failed; }

            var aggregate = new SymbolCreationResult();
            foreach (var b in SymbolBatchHelper.AllBatches)
            {
                var r = SymbolBatchHelper.RunBatch(ctx.Doc, b.File, b.Folder);
                aggregate.Created += r.Created;
                aggregate.Existed += r.Existed;
                aggregate.Failed  += r.Failed;
                aggregate.Warnings.AddRange(r.Warnings);
                aggregate.Errors.AddRange(r.Errors);
            }

            TaskDialog.Show("STING - Symbol Library",
                SymbolBatchHelper.FormatReport("Symbol Library — full build", aggregate));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateSLDSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }
            var r = SymbolBatchHelper.RunBatch(ctx.Doc, "STING_SLD_SYMBOLS.json", "SLD/IEC");
            TaskDialog.Show("STING - SLD Symbols", SymbolBatchHelper.FormatReport("SLD Symbols (IEC 60617)", r));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateSLDSymbolsIEEECommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }
            var r = SymbolBatchHelper.RunBatch(ctx.Doc, "STING_SLD_SYMBOLS_IEEE.json", "SLD/IEEE");
            TaskDialog.Show("STING - SLD Symbols (IEEE)", SymbolBatchHelper.FormatReport("SLD Symbols (IEEE 315)", r));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateSLDSymbolsBSCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }
            var r = SymbolBatchHelper.RunBatch(ctx.Doc, "STING_SLD_SYMBOLS_BS.json", "SLD/BS");
            TaskDialog.Show("STING - SLD Symbols (BS)", SymbolBatchHelper.FormatReport("SLD Symbols (BS EN 60617)", r));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateSLDSymbolsNFPACommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }
            var r = SymbolBatchHelper.RunBatch(ctx.Doc, "STING_SLD_SYMBOLS_NFPA.json", "SLD/NFPA");
            TaskDialog.Show("STING - SLD Symbols (NFPA)", SymbolBatchHelper.FormatReport("SLD Symbols (NFPA 70)", r));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateCIBSESymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }
            var r = SymbolBatchHelper.RunBatch(ctx.Doc, "STING_SLD_SYMBOLS_CIBSE.json", "SLD/CIBSE");
            TaskDialog.Show("STING - CIBSE Symbols", SymbolBatchHelper.FormatReport("Building Services (CIBSE)", r));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateLightingSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }
            var r = SymbolBatchHelper.RunBatch(ctx.Doc, "STING_LIGHTING_SYMBOLS.json", "Lighting");
            TaskDialog.Show("STING - Lighting Symbols", SymbolBatchHelper.FormatReport("Lighting Symbols", r));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFPSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }
            var r = SymbolBatchHelper.RunBatch(ctx.Doc, "STING_FP_SYMBOLS.json", "FireProt");
            TaskDialog.Show("STING - Fire Protection Symbols",
                SymbolBatchHelper.FormatReport("Fire Protection Symbols", r));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReloadSymbolLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }

            // Also flush the JSON shapes cache so any disk edits to
            // STING_SYMBOL_SHAPES.json are picked up on the next AuthorSymbols call.
            FamilySymbolAuthor.ReloadSymbolShapes();

            string outRoot = SymbolBatchHelper.ResolveOutputRoot(ctx.Doc);
            int loaded = 0, failed = 0;
            var warnings = new List<string>();

            try
            {
                var rfas = Directory.GetFiles(outRoot, "*.rfa", SearchOption.AllDirectories);
                if (rfas.Length == 0)
                {
                    TaskDialog.Show("STING - Symbols",
                        $"No .rfa families found under:\n{outRoot}\n\nRun 'Create All Symbols' first.");
                    return Result.Succeeded;
                }

                using (var tx = new Transaction(ctx.Doc, "STING Reload Symbol Families"))
                {
                    tx.Start();
                    var opts = new ReloadFamilyLoadOpts();
                    foreach (var rfa in rfas)
                    {
                        try
                        {
                            Family fam;
                            if (ctx.Doc.LoadFamily(rfa, opts, out fam)) loaded++;
                            else { failed++; warnings.Add($"LoadFamily returned false: {Path.GetFileName(rfa)}"); }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            warnings.Add($"{Path.GetFileName(rfa)}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("ReloadSymbolLibrary", ex);
                msg = ex.Message;
                return Result.Failed;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Reload from: {outRoot}");
            sb.AppendLine($"  • Loaded : {loaded}");
            sb.AppendLine($"  • Failed : {failed}");
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Warnings ({warnings.Count}):");
                foreach (var w in warnings.Take(20)) sb.AppendLine("  · " + w);
                if (warnings.Count > 20) sb.AppendLine($"  … +{warnings.Count - 20} more (StingTools.log)");
            }
            foreach (var w in warnings) StingLog.Warn($"ReloadSymbolLibrary: {w}");

            TaskDialog.Show("STING - Symbols Reloaded", sb.ToString());
            return Result.Succeeded;
        }

        private sealed class ReloadFamilyLoadOpts : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Fix 7 — Compound symbol command (tag: Symbols_CreateCompound)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fix 7 — Creates compound annotation families from concept definitions
    /// that declare compoundComponents or compoundRungs. Each compound is
    /// assembled from the component .rfa files already created by the symbol
    /// library build (CreateSymbolLibraryCommand or a batch command), nested
    /// inside a new GenericAnnotation family document, and saved as
    /// {conceptId}_compound.rfa in the same output folder.
    ///
    /// The command tag is "Symbols_CreateCompound". Wiring into
    /// WorkflowEngine.ResolveCommand and StingElectricalCommandHandler is
    /// handled by the main session after both agents complete.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateCompoundSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Compound Symbols", "No document open."); return Result.Failed; }

            string conceptsPath = StingToolsApp.FindDataFile("Symbols/STING_SYMBOL_CONCEPTS.json")
                ?? StingToolsApp.FindDataFile("STING_SYMBOL_CONCEPTS.json");
            if (string.IsNullOrEmpty(conceptsPath) || !File.Exists(conceptsPath))
            {
                TaskDialog.Show("STING - Compound Symbols",
                    "STING_SYMBOL_CONCEPTS.json not found in data directory.\n" +
                    "Ensure the data files are correctly deployed alongside StingTools.dll.");
                return Result.Failed;
            }

            // Output folder mirrors the primary symbol library output so component
            // .rfa files are found by the compound builder without extra configuration.
            string outRoot   = SymbolBatchHelper.ResolveOutputRoot(ctx.Doc);
            string outFolder = Path.Combine(outRoot, "SLD");  // Compound SLD symbols live in SLD sub-folder.
            Directory.CreateDirectory(outFolder);

            SymbolCreationResult r;
            try
            {
                r = SymbolLibraryCreator.CreateCompoundSymbols(
                    ctx.Doc, conceptsPath, outFolder, loadIntoProject: true);
            }
            catch (Exception ex)
            {
                StingLog.Error("CreateCompoundSymbolsCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }

            TaskDialog.Show("STING - Compound Symbols",
                SymbolBatchHelper.FormatReport("Compound Symbols", r));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class InspectSymbolLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbols", "No document open."); return Result.Failed; }

            var sb = new StringBuilder();
            int totalDefined = 0, totalLoaded = 0, totalMissing = 0;

            // Loaded family names in the project, indexed for membership testing.
            var loadedNames = new HashSet<string>(
                new FilteredElementCollector(ctx.Doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var b in SymbolBatchHelper.AllBatches)
            {
                string jsonPath = StingToolsApp.FindDataFile(b.File);
                if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
                {
                    sb.AppendLine($"[!] {b.Label} — JSON missing ({b.File})");
                    continue;
                }

                SymbolLibrary lib;
                try
                {
                    lib = Newtonsoft.Json.JsonConvert.DeserializeObject<SymbolLibrary>(
                        File.ReadAllText(jsonPath));
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[!] {b.Label} — JSON parse failed: {ex.Message}");
                    continue;
                }

                int defined = lib?.Symbols?.Count ?? 0;
                int loadedCount = lib?.Symbols?.Count(s => loadedNames.Contains(s.Id)) ?? 0;
                int missing = defined - loadedCount;
                totalDefined += defined;
                totalLoaded  += loadedCount;
                totalMissing += missing;

                sb.AppendLine($"{b.Label,-20} defined: {defined,3}   loaded: {loadedCount,3}   missing: {missing,3}");
            }

            sb.AppendLine();
            sb.AppendLine($"TOTAL  defined: {totalDefined,3}   loaded: {totalLoaded,3}   missing: {totalMissing,3}");
            if (totalMissing > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Run 'Create All Symbols' to mint and load every missing family.");
            }

            TaskDialog.Show("STING - Symbol Library Inspect", sb.ToString());
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ConfigureSymbolSizesCommand
    // Read-only view of current size config + a TaskDialog that lets the user
    // choose a global multiplier preset or open the config file directly.
    // For fine-grained per-category/per-symbol edits the user opens the JSON
    // file; this command is the discoverable entry point.
    // ─────────────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConfigureSymbolSizesCommand : IExternalCommand
    {
        // Named presets: label → globalMultiplier
        private static readonly (string Label, double Multiplier)[] Presets = new[]
        {
            ("Small  — 75%  (tight drawings, A3 sheets)",         0.75),
            ("Normal — 100% (default ISO 6412 sizes)",            1.00),
            ("Large  — 125% (large-format or presentation spools)", 1.25),
            ("XL     — 150% (coordination / review prints)",       1.50),
        };

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING - Symbol Sizes", "No document open."); return Result.Failed; }

            string configPath = SymbolBatchHelper.ResolveSizeConfigPath(ctx.Doc);
            var config = SymbolSizeConfig.LoadOrDefault(configPath);

            // Build a summary of current state
            var sb = new StringBuilder();
            sb.AppendLine("Current symbol size configuration:");
            sb.AppendLine($"  Global multiplier : {config.GlobalMultiplier:F2}×");
            sb.AppendLine($"  Category overrides: {config.CategoryOverrides.Count}");
            sb.AppendLine($"  Symbol overrides  : {config.SymbolOverrides.Count}");
            if (config.CategoryOverrides.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Category overrides:");
                foreach (var kv in config.CategoryOverrides)
                    sb.AppendLine($"  {kv.Key,-20} {kv.Value:F1} mm");
            }
            sb.AppendLine();
            sb.AppendLine("Default symbolSize values:");
            sb.AppendLine("  Pipe Fittings / Valves / Flanges   6 mm");
            sb.AppendLine("  Duct Fittings                      8 mm");
            sb.AppendLine("  Conduit / Cable Tray / Notation    5 mm");
            sb.AppendLine("  Welds                              4 mm");
            sb.AppendLine();
            sb.AppendLine("After changing size, run 'Create All Symbols (Overwrite)' to rebuild.");
            sb.AppendLine();
            sb.AppendLine("Config file:");
            sb.AppendLine(string.IsNullOrEmpty(configPath) ? "  (project not saved — save first)" : $"  {configPath}");

            var td = new TaskDialog("STING - Symbol Sizes")
            {
                MainInstruction = "Symbol Size Control",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Close
            };

            // Add preset buttons
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Presets[0].Label);
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Presets[1].Label);
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, Presets[2].Label);
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, Presets[3].Label);

            var tdResult = td.Show();

            double? chosenMultiplier = tdResult switch
            {
                TaskDialogResult.CommandLink1 => Presets[0].Multiplier,
                TaskDialogResult.CommandLink2 => Presets[1].Multiplier,
                TaskDialogResult.CommandLink3 => Presets[2].Multiplier,
                TaskDialogResult.CommandLink4 => Presets[3].Multiplier,
                _ => null
            };

            if (chosenMultiplier.HasValue)
            {
                config.GlobalMultiplier = chosenMultiplier.Value;
                if (!string.IsNullOrEmpty(configPath))
                {
                    config.Save(configPath);
                    TaskDialog.Show("STING - Symbol Sizes",
                        $"Saved: globalMultiplier = {chosenMultiplier.Value:F2}×\n\n" +
                        $"Run 'Create All Symbols (Overwrite)' to apply the new sizes.\n\n" +
                        $"For per-category or per-symbol overrides, edit:\n{configPath}");
                }
                else
                {
                    TaskDialog.Show("STING - Symbol Sizes",
                        "Project must be saved before the config file can be written.");
                }
            }

            return Result.Succeeded;
        }
    }
}
