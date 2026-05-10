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
        // Canonical seed list — tier-1 + tier-2. Used as the fallback
        // ordering when ResolveSpecs() can't scan Data/Seeds/ at runtime.
        // Adding an 11th seed = drop the JSON spec into Data/Seeds/, no
        // code change required.
        private static readonly string[] _tier1Specs = new[]
        {
            // Tier 1
            "STING_SEED_LightingFixture.json",
            "STING_SEED_ElectricalFixture.json",
            "STING_SEED_ElectricalEquipment.json",
            "STING_SEED_FireAlarmDevice.json",
            "STING_SEED_SpecialityEquipment.json",
            // Tier 2
            "STING_SEED_PlumbingFixture.json",
            "STING_SEED_AirTerminal.json",
            "STING_SEED_MechanicalEquipment.json",
            "STING_SEED_Sprinkler.json",
            "STING_SEED_CommunicationDevice.json",
            // Automation seed — auto-placed at BS 7671 §522.8.5 break-points
            "STING_SEED_JunctionBox.json",
            // Phase 178e tier-3 — central plumbing plant, medical-gas
            // outlets, and emergency / lab fixtures. Each ships the
            // worst-case connector union so AutoPipeDrop wires every
            // service in one pass.
            "STING_SEED_PlumbingEquipment.json",
            "STING_SEED_MedGasOutlet.json",
            "STING_SEED_LabFixture.json",
            // Phase 178f — penetration product expansion. Fire damper
            // for ducts crossing fire-rated barriers (BS EN 1366-2);
            // acoustic seal for non-rated but acoustically-sensitive
            // hosts (BS 8233 / Approved Doc E).
            "STING_SEED_FireDamper.json",
            "STING_SEED_AcousticSeal.json",
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

            // Phase 178e — connector audit. Walks each loaded seed
            // family and confirms the connector count declared in the
            // JSON matches what landed on the .rfa. Catches the
            // ConnectorElement.Create… reflection failures that
            // SymbolLibraryCreator silently warns on.
            try
            {
                var auditWarnings = AuditLoadedSeedConnectors(doc, specs);
                aggregate.Warnings.AddRange(auditWarnings);
            }
            catch (Exception ex) { StingLog.Warn($"Connector audit: {ex.Message}"); }

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

        /// <summary>
        /// Phase 178e — connector audit. For each seed JSON, count the
        /// connectors declared at symbol level + every variant level,
        /// then walk the matching loaded family in the project doc and
        /// confirm at least that many connectors landed. Returns a
        /// list of warning strings; empty when every seed matches.
        /// </summary>
        private static List<string> AuditLoadedSeedConnectors(Autodesk.Revit.DB.Document doc, IList<string> specs)
        {
            var warnings = new List<string>();
            if (doc == null || specs == null) return warnings;

            foreach (var specPath in specs)
            {
                try
                {
                    if (!File.Exists(specPath)) continue;
                    string raw = File.ReadAllText(specPath);
                    var token = Newtonsoft.Json.Linq.JToken.Parse(raw);
                    var symbols = token["symbols"] as Newtonsoft.Json.Linq.JArray;
                    if (symbols == null) continue;
                    foreach (var sym in symbols)
                    {
                        string id = (string)sym["id"];
                        if (string.IsNullOrEmpty(id)) continue;
                        int declared = 0;
                        var symConn = sym["connectors"] as Newtonsoft.Json.Linq.JArray;
                        if (symConn != null) declared += symConn.Count;
                        var variants = sym["typeVariants"] as Newtonsoft.Json.Linq.JArray;
                        if (variants != null)
                        {
                            foreach (var v in variants)
                            {
                                var vc = v["connectors"] as Newtonsoft.Json.Linq.JArray;
                                if (vc != null) declared += vc.Count;
                            }
                        }
                        if (declared == 0) continue; // nothing to audit

                        // Locate the matching family in the active document.
                        Autodesk.Revit.DB.Family fam = null;
                        try
                        {
                            foreach (var f in new Autodesk.Revit.DB.FilteredElementCollector(doc)
                                .OfClass(typeof(Autodesk.Revit.DB.Family)))
                            {
                                if (f is Autodesk.Revit.DB.Family ff &&
                                    string.Equals(ff.Name, id, StringComparison.OrdinalIgnoreCase))
                                { fam = ff; break; }
                            }
                        }
                        catch { fam = null; }
                        if (fam == null)
                        {
                            warnings.Add($"Connector audit: family '{id}' is not loaded — skipped.");
                            continue;
                        }

                        // Open the family doc, count ConnectorElements.
                        int actual = 0;
                        try
                        {
                            var fdoc = doc.EditFamily(fam);
                            try
                            {
                                actual = new Autodesk.Revit.DB.FilteredElementCollector(fdoc)
                                    .OfClass(typeof(Autodesk.Revit.DB.ConnectorElement))
                                    .GetElementCount();
                            }
                            finally { try { fdoc.Close(false); } catch { } }
                        }
                        catch (Exception ex) { warnings.Add($"Connector audit: '{id}' — open family failed: {ex.Message}"); continue; }

                        if (actual < declared)
                        {
                            warnings.Add($"Connector audit: '{id}' declares {declared} connector(s) " +
                                $"but the loaded family has {actual} — verify ConnectorElement minting + family-editor finish.");
                        }
                    }
                }
                catch (Exception ex) { warnings.Add($"Connector audit on '{specPath}': {ex.Message}"); }
            }
            return warnings;
        }
    }
}
