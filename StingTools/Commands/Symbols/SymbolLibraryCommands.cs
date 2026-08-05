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
using Newtonsoft.Json;

namespace StingTools.Commands.Symbols
{
    internal static class SymbolBatchHelper
    {
        public static readonly (string File, string Folder, string Label)[] AllBatches = new[]
        {
            ("STING_SLD_SYMBOLS.json",             "SLD/IEC",    "Single Line Diagram (IEC 60617)"),
            ("STING_SLD_SYMBOLS_IEEE.json",        "SLD/IEEE",   "Single Line Diagram (IEEE 315)"),
            ("STING_SLD_SYMBOLS_BS.json",          "SLD/BS",     "Single Line Diagram (BS EN 60617)"),
            ("STING_SLD_SYMBOLS_NFPA.json",        "SLD/NFPA",   "Single Line Diagram (NFPA 70)"),
            ("STING_SLD_SYMBOLS_CIBSE.json",       "SLD/CIBSE",  "Building Services (CIBSE)"),
            ("STING_LIGHTING_SYMBOLS.json",        "Lighting",   "Lighting"),
            ("STING_FP_SYMBOLS.json",              "FireProt",   "Fire Protection"),
            ("STING_MEP_SYMBOLS.json",             "HVAC",       "HVAC / Mechanical"),
            ("STING_ELEC_SYMBOLS.json",            "Electrical", "Electrical Devices"),
            ("STING_PLUMBING_SYMBOLS.json",        "Plumbing",   "Plumbing"),
            ("STING_PIPE_ACCESSORIES.json",        "PipeAcc",    "Pipe Accessories"),
            ("STING_ISO6412_SYMBOLS.json",         "ISO6412",    "ISO 6412 Piping/Duct/Conduit Spool Symbols"),
            // ── Phase 188: 8 new corporate-baseline catalogues ────────────────
            ("STING_WIRE_ANNOTATIONS.json",        "Wire",       "Wire / Cable Annotations (BS EN 60617-2)"),
            ("STING_EARTHING_SYMBOLS.json",        "Earth",      "Earthing & Bonding (BS 7430, BS EN 62305)"),
            ("STING_BMS_SYMBOLS.json",             "BMS",        "BMS / DDC Controls (CIBSE Guide H)"),
            ("STING_TELECOM_SYMBOLS.json",         "Telecom",    "Telecom / Voice / Data / AV (BS EN 50173)"),
            ("STING_STRUCTURAL_ANNOTATIONS.json",  "Struct",     "Structural Annotations (BS 8666, ISO 2553)"),
            ("STING_SAFETY_SYMBOLS.json",          "Safety",     "Safety Pictograms (ISO 7010)"),
            ("STING_GAS_SYMBOLS.json",             "Gas",        "Natural Gas / LPG (IGEM TD/4, BS 6891)"),
            ("STING_DRAINAGE_ABOVE.json",          "DrainAbove", "Above-Ground Drainage (BS EN 12056)"),
        };

        public static string ResolveOutputRoot(Document doc)
        {
            // W-3 — a project that ALREADY has a generated library keeps writing
            // there, even once a shared root exists.
            //
            // Without this, defaulting the shared root would strand every existing
            // library: builds would move to the shared root while the read path
            // (projectFirst precedence) still found the older project-local copies
            // first. The stale families would keep winning and W-1's invalidation
            // would silently accomplish nothing. Migration is a deliberate act, not
            // a side effect of an upgrade.
            string existingProjectLib = ExistingProjectLibrary(doc);
            if (!string.IsNullOrEmpty(existingProjectLib))
                return existingProjectLib;

            // Firm-wide shared library takes precedence so one build serves every
            // project. STING_SYMBOL_LIB (or sting_symbols.json) points directly at
            // the symbols root; the per-standard sub-folders land beneath it.
            string shared = MepSymbolEngine.ResolveSharedLibraryRoot();
            if (!string.IsNullOrEmpty(shared))
            {
                try
                {
                    Directory.CreateDirectory(shared);
                    return shared;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Shared symbol root '{shared}' unusable, "
                        + $"falling back to per-project: {ex.Message}");
                }
            }

            string baseDir = null;
            try
            {
                if (!string.IsNullOrEmpty(doc.PathName))
                    baseDir = Path.GetDirectoryName(doc.PathName);
            }
            catch (Exception ex) { StingLog.Warn($"ResolveOutputRoot: {ex.Message}"); }

            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Path.GetTempPath(), "STING_Symbols");

            var outDir = StingPaths.MetaFile(doc, "_BIM_COORD", "Families", "Symbols");
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        /// <summary>
        /// The project's own generated-symbol folder, but only when it already holds
        /// built families. An empty or absent folder returns null so a fresh project
        /// goes to the shared library instead of minting a private copy.
        /// </summary>
        private static string ExistingProjectLibrary(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;

                // Both layouts must be probed, in the same order ContentRoots uses:
                // the consolidated <root>/_data/_BIM_COORD/… and the legacy
                // <projDir>/_BIM_COORD/… sibling. Hand-assembling the legacy form
                // alone would miss a populated library on any project that has been
                // consolidated — and missing it is precisely the orphaning this guard
                // exists to prevent. Path layout stays owned by StingPaths /
                // ProjectFolderEngine rather than being rebuilt here.
                foreach (var lib in new[]
                {
                    StingPaths.Meta(doc, "_BIM_COORD", "Families", "Symbols"),
                    ProjectFolderEngine.GetLegacyMetaDir(doc, "_BIM_COORD", "Families", "Symbols"),
                })
                {
                    if (string.IsNullOrEmpty(lib) || !Directory.Exists(lib)) continue;

                    // Any .rfa anywhere beneath it counts — the build fans out into
                    // per-standard sub-folders (SLD/IEC, Lighting, …), so a top-level
                    // check alone would miss a populated library.
                    if (!Directory.EnumerateFiles(lib, "*.rfa", SearchOption.AllDirectories).Any())
                        continue;

                    StingLog.Info($"ResolveOutputRoot: using the project's existing symbol library at '{lib}'.");
                    return lib;
                }
                return null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExistingProjectLibrary: {ex.Message}");
                return null;
            }
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
            return StingPaths.MetaFile(doc, "_BIM_COORD", "symbol_size_config.json");
        }

        /// <param name="rebuildMode">
        /// Forces every family in the catalogue to be regenerated even when the
        /// cache manifest reports it fresh. Normal builds leave this false and let
        /// <see cref="SymbolCacheManifest"/> decide — see Symbols_Rebuild.
        /// </param>
        public static SymbolCreationResult RunBatch(Document doc, string jsonName, string subFolder,
            SymbolSizeConfig sizeConfig = null, bool rebuildMode = false)
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
                loadIntoProject: true, rebuildMode: rebuildMode, sizeConfig: sizeConfig);
            aggregate.Created += r.Created;
            aggregate.Existed += r.Existed;
            aggregate.Failed  += r.Failed;
            aggregate.Warnings.AddRange(r.Warnings);
            aggregate.Errors.AddRange(r.Errors);
            aggregate.CreatedRfaPaths.AddRange(r.CreatedRfaPaths);
            return aggregate;
        }

        /// <summary>
        /// True when a batch built nothing AND the warnings/errors carry the
        /// "no family template found" signature SymbolLibraryCreator emits when
        /// the Revit family-template folder can't be resolved (the usual cause of
        /// empty SLD / Generic Annotation output folders).
        /// </summary>
        public static bool LooksLikeMissingTemplate(SymbolCreationResult r)
        {
            if (r == null || r.Created > 0) return false;
            return r.Warnings.Concat(r.Errors).Any(w =>
                w?.IndexOf("no family template", StringComparison.OrdinalIgnoreCase) >= 0
             || w?.IndexOf("template found", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public const string TemplateFixHint =
              "ACTION — 0 families created: the Revit family TEMPLATE folder is not set.\n"
            + "  Revit → Options → File Locations → 'Family Template Files' (or 'Default\n"
            + "  template files') must point at a folder containing the generic-annotation\n"
            + "  template (e.g. 'Metric Generic Annotation.rft'). Set it, then re-run.";

        /// <summary>
        /// The accurate version of <see cref="TemplateFixHint"/>.
        ///
        /// <para>The flat hint blamed the template FOLDER for every empty batch, which
        /// misreads the common case: the folder is set and most catalogues build from it
        /// fine, but one specific .rft (e.g. 'Metric Pipe Accessory.rft') is absent from
        /// this Revit install. Telling users to set a path they have already set sends
        /// them the wrong way, so name the missing family types instead.</para>
        /// </summary>
        public static string DescribeTemplateProblem(SymbolCreationResult r)
        {
            if (r == null) return TemplateFixHint;

            var all = r.Warnings.Concat(r.Errors).Where(w => w != null).ToList();

            // Folder-level failure: nothing on the machine resolved at all.
            if (all.Any(w => w.IndexOf("no family template folder", StringComparison.OrdinalIgnoreCase) >= 0))
                return TemplateFixHint;

            // Per-type failure: harvest the "FamilyType/Discipline" tails so the user
            // knows exactly which template to install.
            const string marker = "no family template found for ";
            var kinds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in all)
            {
                int i = w.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                string tail = w.Substring(i + marker.Length).Trim();
                if (tail.Length > 0) kinds.Add(tail);
            }
            if (kinds.Count == 0) return TemplateFixHint;

            return "ACTION — 0 families created: this Revit install has no template for "
                 + string.Join(", ", kinds) + ".\n"
                 + "  The template FOLDER is fine — other catalogues built from it.\n"
                 + "  Install the matching .rft (a Pipe Accessory batch needs\n"
                 + "  'Metric Pipe Accessory.rft') into the folder set at Revit → Options →\n"
                 + "  File Locations → 'Family Template Files', then re-run.";
        }

        public static string FormatReport(string title, SymbolCreationResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"  • Created : {r.Created}");
            sb.AppendLine($"  • Existed : {r.Existed}");
            sb.AppendLine($"  • Failed  : {r.Failed}");
            if (LooksLikeMissingTemplate(r))
            {
                sb.AppendLine();
                sb.AppendLine(DescribeTemplateProblem(r));
            }
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
            var emptyBatches = new List<string>();
            foreach (var b in SymbolBatchHelper.AllBatches)
            {
                var r = SymbolBatchHelper.RunBatch(ctx.Doc, b.File, b.Folder);
                aggregate.Created += r.Created;
                aggregate.Existed += r.Existed;
                aggregate.Failed  += r.Failed;
                aggregate.Warnings.AddRange(r.Warnings);
                aggregate.Errors.AddRange(r.Errors);
                // Per-batch detection: the aggregate hides a single failed batch when
                // others succeed, so flag the empty ones by name here.
                if (SymbolBatchHelper.LooksLikeMissingTemplate(r))
                    emptyBatches.Add($"{b.Label}  ({b.Folder})");
            }

            string report = SymbolBatchHelper.FormatReport("Symbol Library — full build", aggregate);
            if (emptyBatches.Count > 0)
            {
                report += "\n\n" + $"{emptyBatches.Count} catalogue(s) produced 0 families:\n"
                    + string.Join("\n", emptyBatches.Select(x => "  · " + x))
                    // Aggregate, not a single batch: 838 families can build from a folder
                    // that is plainly set while one catalogue's .rft is simply absent, so
                    // the message must be derived from the warnings, not assumed.
                    + "\n\n" + SymbolBatchHelper.DescribeTemplateProblem(aggregate);
            }
            TaskDialog.Show("STING - Symbol Library", report);
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
