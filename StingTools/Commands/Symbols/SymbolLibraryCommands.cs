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

namespace StingTools.Commands.Symbols
{
    internal static class SymbolBatchHelper
    {
        public static readonly (string File, string Folder, string Label)[] AllBatches = new[]
        {
            ("STING_SLD_SYMBOLS.json",       "SLD",        "Single Line Diagram"),
            ("STING_LIGHTING_SYMBOLS.json",  "Lighting",   "Lighting"),
            ("STING_FP_SYMBOLS.json",        "FireProt",   "Fire Protection"),
            ("STING_MEP_SYMBOLS.json",       "HVAC",       "HVAC / Mechanical"),
            ("STING_ELEC_SYMBOLS.json",      "Electrical", "Electrical Devices"),
            ("STING_PLUMBING_SYMBOLS.json",  "Plumbing",   "Plumbing"),
            ("STING_PIPE_ACCESSORIES.json",  "PipeAcc",    "Pipe Accessories"),
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

        public static SymbolCreationResult RunBatch(Document doc, string jsonName, string subFolder)
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

            var r = SymbolLibraryCreator.CreateAllFromFile(doc, jsonPath, outFolder, loadIntoProject: true);
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
            var r = SymbolBatchHelper.RunBatch(ctx.Doc, "STING_SLD_SYMBOLS.json", "SLD");
            TaskDialog.Show("STING - SLD Symbols", SymbolBatchHelper.FormatReport("SLD Symbols (IEC 60617)", r));
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
}
