// StingTools — BuildSeedFamiliesCommand.
//
// Scaffolds the STING seed-family library from JSON specs in
// Data/Seeds/. For each spec, calls SymbolLibraryCreator to:
//
//   1. Pick the right .rft template via SymbolDefinition.Hosting +
//      Category (face-based / wall-based / ceiling-based / standalone).
//   2. Inject the STING parameter scheme (TAG containers, identity,
//      discipline-specific params, photometric / penetration / circuit
//      groupings as appropriate per seed).
//   3. Add MEP connectors where the seed declares them (panels +
//      junction boxes — the orphan-connector fix).
//   4. Stamp STING_SEED_FAMILY_TXT on every type variant so the swap
//      registry can find them.
//   5. Emit the .rfa in <project>/_BIM_COORD/Families/Seeds/<seed>.rfa
//      and load it into the active project.
//
// Manual finishing per the layman's guide (Families/Seeds/README.md)
// adds visual polish — the auto-generated symbols are deliberately
// minimal so authors can replace them with project-specific
// conventions without fighting the auto-generator.

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
using StingTools.UI;

namespace StingTools.Commands.Symbols
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BuildSeedFamiliesCommand : IExternalCommand
    {
        // The five tier-1 seed specs ship with this command. Tier-2
        // adds another five (PlumbingFixture, AirTerminal, MechanicalEquipment,
        // Sprinkler, CommunicationDevice) — all live under Data/Seeds/
        // and are picked up automatically by ResolveSpecs() so a project
        // wanting an extra seed only adds a JSON file.
        private static readonly string[] _tier1Specs = new[]
        {
            "STING_SEED_LightingFixture.json",
            "STING_SEED_ElectricalFixture.json",
            "STING_SEED_ElectricalEquipment.json",
            "STING_SEED_FireAlarmDevice.json",
            "STING_SEED_SpecialityEquipment.json",
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string outRoot = ResolveSeedOutputFolder(doc);
            Directory.CreateDirectory(outRoot);

            var specs = ResolveSpecs();
            if (specs.Count == 0)
            {
                TaskDialog.Show("STING Seed Families",
                    "No seed JSON specs found. Looked in Data/Seeds/. " +
                    "Tier-1 ships with the plug-in; tier-2 + custom seeds drop in alongside.");
                return Result.Cancelled;
            }

            var aggregate = new SymbolCreationResult();
            int built = 0, failed = 0;
            var perSeed = new List<(string seed, int created, int failed, int warnings)>();

            foreach (var spec in specs)
            {
                string seedName = Path.GetFileNameWithoutExtension(spec);
                try
                {
                    var r = SymbolLibraryCreator.CreateAllFromFile(doc, spec, outRoot, loadIntoProject: true);
                    aggregate.Created += r.Created;
                    aggregate.Existed += r.Existed;
                    aggregate.Failed  += r.Failed;
                    aggregate.Warnings.AddRange(r.Warnings);
                    aggregate.Errors.AddRange(r.Errors);
                    aggregate.CreatedRfaPaths.AddRange(r.CreatedRfaPaths);
                    built  += r.Created;
                    failed += r.Failed;
                    perSeed.Add((seedName, r.Created, r.Failed, r.Warnings.Count));
                }
                catch (Exception ex)
                {
                    aggregate.Errors.Add($"{seedName}: {ex.Message}");
                    failed++;
                    perSeed.Add((seedName, 0, 1, 0));
                }
            }

            ShowResult(aggregate, perSeed, outRoot);

            try { ActionAuditLog.Record("BuildSeedFamilies",
                $"built={built} failed={failed} outRoot={outRoot}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            return aggregate.Errors.Count == 0 ? Result.Succeeded : Result.Succeeded;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static string ResolveSeedOutputFolder(Document doc)
        {
            string baseDir = null;
            try
            {
                if (!string.IsNullOrEmpty(doc.PathName))
                    baseDir = Path.GetDirectoryName(doc.PathName);
            }
            catch (Exception ex) { StingLog.Warn($"ResolveSeedOutputFolder: {ex.Message}"); }
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Path.GetTempPath(), "STING_Seeds");
            return Path.Combine(baseDir, "_BIM_COORD", "Families", "Seeds");
        }

        /// <summary>
        /// Resolve every JSON file under Data/Seeds/. Defaults to the
        /// tier-1 list when the directory scan returns nothing, so a
        /// project that has trimmed its data pack still gets the
        /// canonical seeds.
        /// </summary>
        private static List<string> ResolveSpecs()
        {
            var found = new List<string>();
            try
            {
                string dataPath = StingToolsApp.DataPath;
                if (!string.IsNullOrEmpty(dataPath))
                {
                    string seedDir = Path.Combine(dataPath, "Seeds");
                    if (Directory.Exists(seedDir))
                    {
                        foreach (var f in Directory.GetFiles(seedDir, "STING_SEED_*.json"))
                            found.Add(f);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveSpecs: {ex.Message}"); }
            if (found.Count == 0)
            {
                foreach (var s in _tier1Specs)
                {
                    string p = StingToolsApp.FindDataFile(s);
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) found.Add(p);
                }
            }
            return found;
        }

        private static void ShowResult(SymbolCreationResult r,
            List<(string seed, int created, int failed, int warnings)> perSeed, string outRoot)
        {
            var panel = StingResultPanel.Create("Seed Families — Build");
            panel.SetSubtitle($"{r.Created} created, {r.Existed} existed, {r.Failed} failed");

            panel.AddSection("SUMMARY")
                .Metric("Created",  r.Created.ToString())
                .Metric("Existed",  r.Existed.ToString())
                .Metric("Failed",   r.Failed.ToString())
                .Metric("Warnings", r.Warnings.Count.ToString())
                .Metric("Output",   outRoot);

            if (perSeed.Count > 0)
            {
                panel.AddSection("BY SEED");
                foreach (var s in perSeed)
                    panel.Metric(s.seed,
                        s.created.ToString(),
                        s.failed > 0 ? $"failed={s.failed}, warnings={s.warnings}" :
                        s.warnings > 0 ? $"warnings={s.warnings}" : "OK");
            }

            if (r.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in r.Warnings.Take(15)) panel.Text(w);
                if (r.Warnings.Count > 15) panel.Text($"+{r.Warnings.Count - 15} more (StingLog).");
            }

            if (r.Errors.Count > 0)
            {
                panel.AddSection("ERRORS");
                foreach (var e in r.Errors.Take(15)) panel.Text(e);
            }

            panel.AddSection("NEXT STEPS")
                .Text("Open the produced .rfa files in Family Editor and finish per Families/Seeds/README.md.")
                .Text("Run 'Swap to Manufacturer' to replace seeds with real families once procurement decides.")
                .Text("Re-run this command after editing JSON specs — existing .rfa files are overwritten.");
            panel.Show();
        }
    }
}
