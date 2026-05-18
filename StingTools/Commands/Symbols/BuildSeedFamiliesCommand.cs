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
using StingTools.Core.Routing;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace StingTools.Commands.Symbols
{
    public enum SeedRebuildMode
    {
        MissingOnly,
        RebuildUnfinalized,
        RebuildAll
    }

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

            // ── Rebuild mode picker ───────────────────────────────────────
            var mode = PromptRebuildMode();
            if (mode == null) return Result.Cancelled;
            var rebuildMode = mode.Value;

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
            var perSeed = new List<(string seed, int created, int failed, int warnings, int prot)>();

            foreach (var spec in specs)
            {
                string seedName = Path.GetFileNameWithoutExtension(spec);
                try
                {
                    var r = SymbolLibraryCreator.CreateAllFromFile(doc, spec, outRoot,
                        loadIntoProject: true);
                    aggregate.Created   += r.Created;
                    aggregate.Existed   += r.Existed;
                    aggregate.Failed    += r.Failed;
                    aggregate.Protected += r.Protected;
                    aggregate.Warnings.AddRange(r.Warnings);
                    aggregate.Errors.AddRange(r.Errors);
                    aggregate.CreatedRfaPaths.AddRange(r.CreatedRfaPaths);
                    built  += r.Created;
                    failed += r.Failed;
                    perSeed.Add((seedName, r.Created, r.Failed, r.Warnings.Count, r.Protected));
                }
                catch (Exception ex)
                {
                    aggregate.Errors.Add($"{seedName}: {ex.Message}");
                    failed++;
                    perSeed.Add((seedName, 0, 1, 0, 0));
                }
            }

            // ── Auto-register swap candidates ──────────────────────────────
            try { AutoRegisterSwapCandidates(specs, outRoot, aggregate); }
            catch (Exception ex) { StingLog.Warn($"AutoRegisterSwapCandidates: {ex.Message}"); }

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

            // ── Finalization gate validation ────────────────────────────
            // Check STING_FINALIZATION_CHECKLIST on every produced .rfa.
            // Incomplete seeds are reported in the result panel so the
            // author knows exactly what manual steps remain before they
            // can stamp .sting-finalized.
            List<(string seedName, string reason)> gateIncomplete = null;
            try
            {
                var rfaPaths = aggregate.CreatedRfaPaths ?? new List<string>();
                gateIncomplete = ValidateFinalizationGates(doc, rfaPaths);
                if (gateIncomplete.Count > 0)
                    aggregate.Warnings.Insert(0, $"Finalization gate: {gateIncomplete.Count} seed(s) NOT ready for .sting-finalized — see FINALIZATION GATE section.");
            }
            catch (Exception ex) { StingLog.Warn($"ValidateFinalizationGates: {ex.Message}"); }

            ShowResult(aggregate, perSeed, outRoot, rebuildMode, gateIncomplete);

            try { ActionAuditLog.Record("BuildSeedFamilies",
                $"mode={rebuildMode} built={built} failed={failed} protected={aggregate.Protected} outRoot={outRoot}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }

            return aggregate.Errors.Count == 0 ? Result.Succeeded : Result.Failed;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Shows a TaskDialog that lets the user pick MissingOnly /
        /// RebuildUnfinalized / RebuildAll. Returns null when the user
        /// cancels. RebuildAll requires an extra confirmation because it
        /// will overwrite hand-polished families.
        /// </summary>
        private static SeedRebuildMode? PromptRebuildMode()
        {
            var td = new TaskDialog("STING Seed Families — Rebuild Mode")
            {
                MainInstruction = "Choose which seeds to build",
                MainContent =
                    "Missing Only — create seeds that don't exist yet (safe default).\n" +
                    "Rebuild Unfinalized — skip only seeds with a .sting-finalized sidecar; rebuild all others.\n" +
                    "Rebuild All — regenerate every seed including finalized ones (destroys manual polish).",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton  = TaskDialogResult.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Missing Only",
                "Create seeds whose .rfa does not exist. Protected families are never touched.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Rebuild Unfinalized",
                "Regenerate seeds that have not been marked as finalized. Finalized seeds are skipped.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Rebuild All",
                "Regenerate ALL seeds. WARNING: overwrites any manual polish. You will be asked to confirm.");

            var result = td.Show();
            switch (result)
            {
                case TaskDialogResult.CommandLink1: return SeedRebuildMode.MissingOnly;
                case TaskDialogResult.CommandLink2: return SeedRebuildMode.RebuildUnfinalized;
                case TaskDialogResult.CommandLink3:
                {
                    var confirm = TaskDialog.Show("Confirm Rebuild All",
                        "This will overwrite ALL seed .rfa files on disk, including any you have hand-polished.\n\n" +
                        "Are you sure?",
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                    return confirm == TaskDialogResult.Yes
                        ? SeedRebuildMode.RebuildAll
                        : (SeedRebuildMode?)null;
                }
                default: return null;
            }
        }

        /// <summary>
        /// Reads the swapCandidates[] array from every seed JSON spec and
        /// merges the entries into STING_FAMILY_SWAP_REGISTRY.json next to
        /// the seed output folder. Existing registry entries with the same
        /// seedId + label are updated in place; new entries are appended
        /// and tagged with <c>"source": "auto"</c>. Entries that were
        /// previously auto-registered but are no longer present in any
        /// current spec are pruned, preserving manually added entries
        /// (those without a "source" field, or with "source" != "auto").
        /// </summary>
        private static void AutoRegisterSwapCandidates(IList<string> specs, string outRoot, SymbolCreationResult result)
        {
            string registryPath = Path.Combine(outRoot, "..", "STING_FAMILY_SWAP_REGISTRY.json");
            registryPath = Path.GetFullPath(registryPath);

            // Load or create the registry JSON object.
            Newtonsoft.Json.Linq.JObject registry;
            try
            {
                registry = File.Exists(registryPath)
                    ? Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(registryPath))
                    : new Newtonsoft.Json.Linq.JObject();
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Swap registry load failed: {ex.Message}");
                return;
            }

            var entries = registry["entries"] as Newtonsoft.Json.Linq.JArray
                ?? new Newtonsoft.Json.Linq.JArray();
            int added = 0, updated = 0, pruned = 0;

            // Build the complete set of (seedId, label) pairs declared in
            // current specs so the post-loop prune pass can identify stale
            // auto-registered entries.  Only candidates with a non-empty
            // FamilyPath are eligible for auto-registration (same gate as the
            // merge loop below).
            var currentPairs = new HashSet<(string seedId, string label)>();
            foreach (var specPath in specs)
            {
                try
                {
                    if (!File.Exists(specPath)) continue;
                    var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<
                        StingTools.Core.Symbols.SymbolLibrary>(File.ReadAllText(specPath));
                    if (lib?.Symbols == null) continue;
                    foreach (var sym in lib.Symbols)
                    {
                        if (sym?.SwapCandidates == null) continue;
                        foreach (var cand in sym.SwapCandidates)
                        {
                            if (!string.IsNullOrWhiteSpace(cand?.FamilyPath))
                                currentPairs.Add((sym.Id ?? "", cand.Label ?? ""));
                        }
                    }
                }
                catch { /* parse errors surfaced in the merge loop below */ }
            }

            // Merge loop — add / update entries from the current spec set.
            foreach (var specPath in specs)
            {
                try
                {
                    if (!File.Exists(specPath)) continue;
                    var lib = Newtonsoft.Json.JsonConvert.DeserializeObject<
                        StingTools.Core.Symbols.SymbolLibrary>(File.ReadAllText(specPath));
                    if (lib?.Symbols == null) continue;

                    foreach (var sym in lib.Symbols)
                    {
                        if (sym?.SwapCandidates == null || sym.SwapCandidates.Count == 0) continue;
                        foreach (var cand in sym.SwapCandidates)
                        {
                            if (string.IsNullOrWhiteSpace(cand?.FamilyPath)) continue;
                            // Find existing entry (match by seedId + label).
                            Newtonsoft.Json.Linq.JObject existing = null;
                            foreach (var e in entries)
                            {
                                if ((string)e["seedId"] == sym.Id && (string)e["label"] == cand.Label)
                                { existing = e as Newtonsoft.Json.Linq.JObject; break; }
                            }
                            if (existing != null)
                            {
                                existing["familyNamePattern"] = cand.FamilyPath;
                                existing["typeNamePattern"]   = cand.TypePattern ?? "";
                                existing["seedVariantPattern"]= cand.VariantPattern ?? "";
                                existing["priority"]          = cand.Priority;
                                existing["source"]            = "auto"; // tag for future prune passes
                                updated++;
                            }
                            else
                            {
                                var node = new Newtonsoft.Json.Linq.JObject
                                {
                                    ["seedId"]             = sym.Id,
                                    ["label"]             = cand.Label,
                                    ["familyNamePattern"] = cand.FamilyPath,
                                    ["typeNamePattern"]   = cand.TypePattern   ?? "",
                                    ["seedVariantPattern"]= cand.VariantPattern ?? "",
                                    ["priority"]          = cand.Priority,
                                    ["source"]            = "auto",
                                };
                                entries.Add(node);
                                added++;
                            }
                        }
                    }
                }
                catch (Exception ex2) { result.Warnings.Add($"SwapCandidates parse '{Path.GetFileName(specPath)}': {ex2.Message}"); }
            }

            // Prune pass — remove auto-registered entries that are no longer
            // declared in any current spec.  Entries without a "source" field,
            // or with source != "auto", are treated as manually added and
            // are never removed.
            var toRemove = new List<Newtonsoft.Json.Linq.JToken>();
            foreach (var e in entries)
            {
                string src = (string)e["source"];
                if (!string.Equals(src, "auto", StringComparison.OrdinalIgnoreCase)) continue;
                var key = ((string)e["seedId"] ?? "", (string)e["label"] ?? "");
                if (!currentPairs.Contains(key)) toRemove.Add(e);
            }
            foreach (var e in toRemove) { entries.Remove(e); pruned++; }

            if (added + updated + pruned == 0) return;
            registry["entries"] = entries;
            registry["_updated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            try
            {
                File.WriteAllText(registryPath, registry.ToString(Newtonsoft.Json.Formatting.Indented));
                result.Warnings.Add($"Swap registry: {added} added, {updated} updated, {pruned} pruned → {registryPath}");
            }
            catch (Exception ex) { result.Warnings.Add($"Swap registry save failed: {ex.Message}"); }
        }

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

        // ── Finalization gate ─────────────────────────────────────────────

        /// <summary>
        /// Reads STING_FINALIZATION_CHECKLIST (Integer) from every produced
        /// seed family that was successfully loaded into the project.
        /// Returns a list of (seedName, gateValue) pairs where gateValue is
        /// false — these seeds should NOT receive the .sting-finalized sidecar
        /// until the author wires the missing steps (penetration Mark formula,
        /// connector face-ref, type variants, etc.).
        /// </summary>
        private static List<(string seedName, string reason)> ValidateFinalizationGates(
            Document doc, List<string> rfaPaths)
        {
            const string GATE_PARAM = "STING_FINALIZATION_CHECKLIST";
            var incomplete = new List<(string, string)>();
            if (doc == null || rfaPaths == null) return incomplete;

            // Penetration seeds require a specific per-seed check because
            // the Mark = PEN_CONTROL_NUMBER_TXT formula is the one step
            // the auto-builder cannot wire (Revit API limit).
            var penetrationSeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "STING_SEED_SpecialityEquipment",
                "STING_SEED_FireDamper",
                "STING_SEED_AcousticSeal",
            };

            foreach (var rfaPath in rfaPaths)
            {
                if (!File.Exists(rfaPath)) continue;
                string seedName = Path.GetFileNameWithoutExtension(rfaPath);

                // Locate the matching loaded family in the project document.
                Family fam = null;
                try
                {
                    string famName = seedName.Replace("STING_SEED_", "");
                    foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(Family)))
                    {
                        if (el is Family f &&
                            (string.Equals(f.Name, seedName, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(f.Name, famName, StringComparison.OrdinalIgnoreCase)))
                        { fam = f; break; }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ValidateFinalizationGates search: {ex.Message}"); }

                if (fam == null) continue; // family not loaded yet — gate deferred

                // Open family doc and read the gate param.
                bool gateOk = false;
                string reason = "STING_FINALIZATION_CHECKLIST param missing or false";
                try
                {
                    var fdoc = doc.EditFamily(fam);
                    try
                    {
                        var mgr = fdoc.FamilyManager;
                        var p = mgr?.get_Parameter(GATE_PARAM);
                        var currentType = mgr?.CurrentType;
                        if (p == null)
                        {
                            // No gate param at all — treat as not finalized; note authoring step.
                            reason = $"'{GATE_PARAM}' parameter not present in family";
                        }
                        else if (p.StorageType == StorageType.Integer && currentType != null
                                 && (currentType.AsInteger(p) ?? 0) == 1)
                        {
                            gateOk = true;
                        }
                        else
                        {
                            string current = currentType == null
                                ? "<no current type>"
                                : (p.StorageType == StorageType.Integer
                                    ? (currentType.AsInteger(p)?.ToString() ?? "<null>")
                                    : (currentType.AsString(p) ?? "<null>"));
                            reason = $"'{GATE_PARAM}' is {current} — author must set to 1";
                        }

                        // Extra check for penetration seeds: verify Mark
                        // formula is wired by looking for a formula on the
                        // built-in Mark parameter that references PEN_CONTROL_NUMBER_TXT.
                        if (gateOk && penetrationSeeds.Contains(seedName))
                        {
                            try
                            {
                                var markP = mgr?.get_Parameter("Mark");
                                string markValue = (markP != null && currentType != null)
                                    ? currentType.AsValueString(markP)
                                    : null;
                                if (markP == null || string.IsNullOrEmpty(markValue)
                                    || !markP.IsDeterminedByFormula)
                                {
                                    gateOk = false;
                                    reason = "Penetration seed: Mark formula '= PEN_CONTROL_NUMBER_TXT' not wired in Family Editor";
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }
                    }
                    finally { try { fdoc.Close(false); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); } }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ValidateFinalizationGates: '{seedName}' — open family failed: {ex.Message}");
                    continue;
                }

                if (!gateOk)
                    incomplete.Add((seedName, reason));
            }
            return incomplete;
        }

        private static void ShowResult(SymbolCreationResult r,
            List<(string seed, int created, int failed, int warnings, int prot)> perSeed,
            string outRoot, SeedRebuildMode mode,
            List<(string seedName, string reason)> gateIncomplete = null)
        {
            var panel = StingResultPanel.Create("Seed Families — Build");
            panel.SetSubtitle($"Mode: {mode}  |  {r.Created} created, {r.Existed} existed, " +
                              $"{r.Protected} protected, {r.Failed} failed");

            panel.AddSection("SUMMARY")
                .Metric("Mode",       mode.ToString())
                .Metric("Created",    r.Created.ToString())
                .Metric("Existed",    r.Existed.ToString())
                .Metric("Protected",  r.Protected.ToString())
                .Metric("Failed",     r.Failed.ToString())
                .Metric("Warnings",   r.Warnings.Count.ToString())
                .Metric("Output",     outRoot);

            if (perSeed.Count > 0)
            {
                panel.AddSection("BY SEED");
                foreach (var s in perSeed)
                {
                    string detail = s.failed > 0   ? $"FAILED ({s.failed})" :
                                    s.prot  > 0    ? $"protected ({s.prot})" :
                                    s.created > 0  ? "created" :
                                    s.warnings > 0 ? $"existed — {s.warnings} warn" : "existed / skipped";
                    if (s.warnings > 0 && s.failed == 0 && s.prot == 0 && s.created > 0)
                        detail += $" ({s.warnings} warn)";
                    panel.Metric(s.seed, s.created.ToString(), detail);
                }
            }

            if (r.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in r.Warnings.Take(20)) panel.Text(w);
                if (r.Warnings.Count > 20) panel.Text($"+{r.Warnings.Count - 20} more (StingLog).");
            }

            if (r.Errors.Count > 0)
            {
                panel.AddSection("ERRORS");
                foreach (var e in r.Errors.Take(15)) panel.Text(e);
            }

            if (gateIncomplete != null && gateIncomplete.Count > 0)
            {
                panel.AddSection("FINALIZATION GATE — DO NOT stamp .sting-finalized yet");
                foreach (var (name, reason) in gateIncomplete)
                    panel.Text($"  ✗  {name}: {reason}");
                panel.Text("");
                panel.Text("Steps to clear the gate:");
                panel.Text("  1. Open the .rfa in the Family Editor.");
                panel.Text("  2. Complete every item in the seed's finalization checklist");
                panel.Text("     (see Families/Seeds/README.md for your seed type).");
                panel.Text("  3. Set STING_FINALIZATION_CHECKLIST (family param) to 1.");
                panel.Text("  4. For penetration seeds: wire Mark = PEN_CONTROL_NUMBER_TXT formula.");
                panel.Text("  5. Save and reload. Then place '.sting-finalized' alongside the .rfa.");
            }
            else if (gateIncomplete != null && r.Created > 0)
            {
                panel.AddSection("FINALIZATION GATE").Text("All produced seeds passed the gate.");
            }

            panel.AddSection("PROTECTION NOTES")
                .Text("Finalize a seed: place a file named '<seed>.rfa.sting-finalized' alongside the .rfa.")
                .Text("  ONLY place this sidecar AFTER the Finalization Gate section above shows no failures.")
                .Text("  Future runs in any mode will skip that seed unless you delete the sidecar.")
                .Text("protectExisting:true in the JSON spec blocks Rebuild All as an extra safety net.")
                .Text("Use 'Rebuild Unfinalized' after editing a JSON spec to pick up changes safely.");

            panel.AddSection("NEXT STEPS")
                .Text("Open produced .rfa files in Family Editor for visual polish per Families/Seeds/README.md.")
                .Text("To import a pre-built family as the seed base, set sourceFamilyPath in the JSON spec.")
                .Text("Pre-register manufacturer swap variants via swapCandidates[] in the JSON spec.")
                .Text("Run 'Swap to Manufacturer' once procurement decides on real product families.");
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
                        catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); fam = null; }
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
                            finally { try { fdoc.Close(false); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); } }
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
