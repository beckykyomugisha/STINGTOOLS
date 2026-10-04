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

            // ── Rebuild mode ──────────────────────────────────────────────
            // A person picks it; a workflow preset reads params.mode (Missing Only
            // when absent — SeedBuildPresetMode) and never opens the picker.
            SeedRebuildMode rebuildMode;
            if (PresetDialog.Quiet)
            {
                var canonical = SeedBuildPresetMode.Resolve(WorkflowEngine.StepParam("mode"), out var modeError);
                if (canonical == null) { message = "Seed families: " + modeError; return Result.Failed; }
                rebuildMode = canonical == SeedBuildPresetMode.RebuildAll ? SeedRebuildMode.RebuildAll
                            : canonical == SeedBuildPresetMode.RebuildUnfinalized ? SeedRebuildMode.RebuildUnfinalized
                            : SeedRebuildMode.MissingOnly;
            }
            else
            {
                var mode = PromptRebuildMode();
                if (mode == null) return Result.Cancelled;
                rebuildMode = mode.Value;
            }

            string outRoot = ResolveSeedOutputFolder(doc);
            Directory.CreateDirectory(outRoot);

            var specs = ResolveSpecs();
            if (specs.Count == 0)
            {
                PresetDialog.Show("STING Seed Families",
                    "No seed JSON specs found. Looked in Data/Seeds/. " +
                    "Tier-1 ships with the plug-in; tier-2 + custom seeds drop in alongside.", ref message);
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
                    // The picker's choice now reaches the builder. It previously
                    // stopped at the result dialog and the log, so every mode behaved
                    // as Missing Only — "Rebuild All" silently rebuilt nothing,
                    // because the builder skips any seed whose .rfa already exists.
                    //
                    // RebuildUnfinalized maps to a full rebuild as well: nothing in
                    // the codebase ever writes the .sting-finalized sidecar its label
                    // refers to (SymbolCreationResult.Protected is never incremented
                    // and SymbolDefinition.ProtectExisting is never read), so no seed
                    // is currently finalized and there is nothing to protect. The
                    // dialog copy says so rather than implying otherwise.
                    var r = SymbolLibraryCreator.CreateAllFromFile(doc, spec, outRoot,
                        loadIntoProject: true,
                        rebuildMode: rebuildMode != SeedRebuildMode.MissingOnly);
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

            // ── MG-2: migrate renamed seed types ──────────────────────────
            // Runs in every mode, Missing Only included (the default): a reload never
            // deletes a type the project holds, and Missing Only may not reload at all.
            var migration = new SeedTypeMigrationReport();
            foreach (var spec in specs)
            {
                try { SeedTypeMigrator.MigrateFromFile(doc, spec, migration); }
                catch (Exception ex) { migration.Warnings.Add($"Type migration '{Path.GetFileName(spec)}': {ex.Message}"); }
            }
            aggregate.Warnings.AddRange(migration.Warnings);

            // ── Auto-register swap candidates ──────────────────────────────
            try { AutoRegisterSwapCandidates(doc, specs, outRoot, aggregate); }
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

            ShowResult(aggregate, perSeed, outRoot, rebuildMode, ref message, gateIncomplete, migration);
            if (PresetDialog.Quiet)
            {
                message = $"Seed families ({rebuildMode}): {aggregate.Created} created, {aggregate.Existed} existed, " +
                          $"{aggregate.Failed} failed, {aggregate.Errors.Count} error(s), {aggregate.Warnings.Count} warning(s)" +
                          (migration.Types > 0 ? $", {migration.Types} renamed type(s) migrated" : "") +
                          " (details in the STING log).";
                if (aggregate.Errors.Count > 0)
                    message += " First error: " + aggregate.Errors[0];
            }

            try { ActionAuditLog.Record("BuildSeedFamilies",
                $"mode={rebuildMode} built={built} failed={failed} protected={aggregate.Protected} " +
                $"migratedTypes={migration.Types} migratedInstances={migration.Instances} outRoot={outRoot}"); }
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
                    "Missing Only — create seeds that don't exist yet, plus any whose JSON spec " +
                    "has changed since it was built (safe default).\n" +
                    "Rebuild Unfinalized — rebuild seeds not marked finalized. No seed is " +
                    "currently finalized, so today this behaves as a full rebuild.\n" +
                    "Rebuild All — regenerate every seed (destroys manual polish).",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton  = TaskDialogResult.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Missing Only",
                "Create seeds whose .rfa does not exist. An existing seed is left alone unless " +
                "its JSON spec has changed since it was built, in which case it is regenerated.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Rebuild Unfinalized",
                "Regenerate seeds not marked finalized. Nothing writes the .sting-finalized " +
                "sidecar yet, so this currently rebuilds every seed — same as Rebuild All.");
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
        /// Reads the swapCandidates[] array from every seed JSON spec and upserts them into the
        /// project swap override, <c>_BIM_COORD/family_swap_registry.json</c> — the file
        /// <c>SwapToManufacturerCommand.LoadRegistry</c> merges over the corporate
        /// STING_FAMILY_SWAP_REGISTRY.json. Shape is the reader's:
        /// <c>seeds[{seedId, category, candidates[{familyNamePattern, typeNamePattern,
        /// seedVariantPattern, label, priority}]}]</c>.
        /// <para>
        /// Candidates are matched by label within their seed. Ones written here carry
        /// <c>"source": "auto"</c>; an auto candidate no longer declared by its seed's spec is
        /// pruned (per seed). Candidates without that tag are manual and never touched.
        /// </para>
        /// <para>
        /// The reader REPLACES a corporate seed with the project seed of the same id, so a
        /// project seed written here also carries the corporate candidates for that seed,
        /// tagged <c>"source": "corporate"</c> and refreshed on every run — otherwise
        /// registering one manufacturer family would hide every corporate candidate.
        /// </para>
        /// <para>
        /// The earlier writer put flat <c>entries[]</c> in Families/STING_FAMILY_SWAP_REGISTRY.json,
        /// which nothing read. Those entries are folded in once, and the old file is renamed
        /// <c>*.migrated_yyyyMMdd</c> (never deleted).
        /// </para>
        /// </summary>
        private static void AutoRegisterSwapCandidates(Document doc, IList<string> specs, string outRoot, SymbolCreationResult result)
        {
            string registryPath = StingPaths.MetaFile(doc, "_BIM_COORD", "family_swap_registry.json");
            if (string.IsNullOrEmpty(registryPath))
            {
                result.Warnings.Add("Swap registry: no project folder (save the model first); swap candidates not registered.");
                return;
            }

            JObject registry;
            try
            {
                registry = File.Exists(registryPath)
                    ? JObject.Parse(File.ReadAllText(registryPath))
                    : new JObject();
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Swap registry load failed: {ex.Message}");
                return;
            }

            var seeds = registry["seeds"] as JArray;
            if (seeds == null) { seeds = new JArray(); registry["seeds"] = seeds; }
            int added = 0, updated = 0, pruned = 0, folded = 0;
            bool changed = false;
            var createdSeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            JObject SeedNode(string seedId, string category)
            {
                foreach (var s in seeds.OfType<JObject>())
                    if (string.Equals((string)s["seedId"], seedId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty((string)s["category"]) && !string.IsNullOrEmpty(category))
                            s["category"] = category;
                        if (!(s["candidates"] is JArray)) s["candidates"] = new JArray();
                        return s;
                    }
                var node = new JObject { ["seedId"] = seedId, ["category"] = category ?? "", ["candidates"] = new JArray() };
                seeds.Add(node);
                createdSeeds.Add(seedId);
                changed = true;
                return node;
            }

            JObject FindByLabel(JArray cands, string label)
            {
                foreach (var c in cands.OfType<JObject>())
                    if (string.Equals((string)c["label"] ?? "", label ?? "", StringComparison.OrdinalIgnoreCase))
                        return c;
                return null;
            }

            // ── One-time fold of the legacy flat entries[] (old file, or an entries[] key) ──
            string legacyPath = null;
            try { legacyPath = Path.GetFullPath(Path.Combine(outRoot, "..", "STING_FAMILY_SWAP_REGISTRY.json")); }
            catch (Exception ex) { StingLog.Warn($"Swap registry legacy path: {ex.Message}"); }
            var legacyEntries = new List<JObject>();
            if (registry["entries"] is JArray inline)
            {
                legacyEntries.AddRange(inline.OfType<JObject>());
                registry.Remove("entries");
                changed = true;
            }
            bool legacyFileRead = false;
            if (!string.IsNullOrEmpty(legacyPath) && File.Exists(legacyPath)
                && !string.Equals(legacyPath, registryPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (JObject.Parse(File.ReadAllText(legacyPath))["entries"] is JArray arr)
                        legacyEntries.AddRange(arr.OfType<JObject>());
                    legacyFileRead = true;
                }
                catch (Exception ex) { result.Warnings.Add($"Swap registry: legacy file unreadable, left in place: {ex.Message}"); }
            }
            foreach (var e in legacyEntries)
            {
                string seedId = (string)e["seedId"];
                string label = (string)e["label"] ?? "";
                if (string.IsNullOrEmpty(seedId)) continue;
                var cands = (JArray)SeedNode(seedId, null)["candidates"];
                if (FindByLabel(cands, label) != null) continue;   // project entry already wins
                string fam = (string)e["familyNamePattern"] ?? "";
                // The old writer stored the .rfa path here, not a regex.
                if (fam.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase)
                    || fam.IndexOf('\\') >= 0 || fam.IndexOf('/') >= 0)
                    fam = FamilyPathPattern(fam);
                cands.Add(new JObject
                {
                    ["familyNamePattern"]  = fam,
                    ["typeNamePattern"]    = (string)e["typeNamePattern"] ?? "",
                    ["seedVariantPattern"] = (string)e["seedVariantPattern"] ?? "",
                    ["label"]              = label,
                    ["priority"]           = e["priority"] ?? new JValue(999),
                    ["source"]             = (string)e["source"] ?? "auto",
                });
                folded++; changed = true;
            }

            // ── Current spec candidates, per seed ──
            var declared = new Dictionary<string, List<SeedSwapCandidate>>(StringComparer.OrdinalIgnoreCase);
            var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var specPath in specs)
            {
                try
                {
                    if (!File.Exists(specPath)) continue;
                    var lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(specPath));
                    if (lib?.Symbols == null) continue;
                    foreach (var sym in lib.Symbols)
                    {
                        if (sym == null || string.IsNullOrEmpty(sym.Id) || sym.SwapCandidates == null) continue;
                        var list = sym.SwapCandidates.Where(c => !string.IsNullOrWhiteSpace(c?.FamilyPath)).ToList();
                        if (list.Count == 0) continue;
                        declared[sym.Id] = list;
                        categories[sym.Id] = sym.Category ?? "";
                    }
                }
                catch (Exception ex2) { result.Warnings.Add($"SwapCandidates parse '{Path.GetFileName(specPath)}': {ex2.Message}"); }
            }

            // ── Upsert by label + prune stale auto candidates, scoped per seed ──
            foreach (var kv in declared)
            {
                var cands = (JArray)SeedNode(kv.Key, categories[kv.Key])["candidates"];
                var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cand in kv.Value)
                {
                    labels.Add(cand.Label ?? "");
                    var node = new JObject
                    {
                        ["familyNamePattern"]  = FamilyPathPattern(cand.FamilyPath),
                        ["typeNamePattern"]    = cand.TypePattern ?? "",
                        ["seedVariantPattern"] = cand.VariantPattern ?? "",
                        ["label"]              = cand.Label ?? "",
                        ["priority"]           = cand.Priority,
                        ["source"]             = "auto",
                    };
                    var existing = FindByLabel(cands, cand.Label);
                    string src = existing == null ? null : (string)existing["source"];
                    bool writerOwned = string.Equals(src, "auto", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(src, "corporate", StringComparison.OrdinalIgnoreCase);
                    if (existing == null) { cands.Add(node); added++; changed = true; }
                    else if (!writerOwned) { /* hand-written candidate with this label: the user's wins */ }
                    else if (!JToken.DeepEquals(existing, node)) { existing.Replace(node); updated++; changed = true; }
                }
            }
            foreach (var s in seeds.OfType<JObject>())
            {
                string seedId = (string)s["seedId"] ?? "";
                if (!(s["candidates"] is JArray cands)) continue;
                declared.TryGetValue(seedId, out var cur);
                var keep = new HashSet<string>(
                    (cur ?? new List<SeedSwapCandidate>()).Select(c => c.Label ?? ""), StringComparer.OrdinalIgnoreCase);
                foreach (var c in cands.OfType<JObject>().ToList())
                {
                    if (!string.Equals((string)c["source"], "auto", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!keep.Contains((string)c["label"] ?? "")) { c.Remove(); pruned++; changed = true; }
                }
            }

            // ── Corporate candidates carried on every project seed (see summary) ──
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_FAMILY_SWAP_REGISTRY.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp)
                    && JObject.Parse(File.ReadAllText(corp))["seeds"] is JArray corpSeeds)
                {
                    foreach (var s in seeds.OfType<JObject>())
                    {
                        var cands = (JArray)s["candidates"];
                        var oldCorp = cands.OfType<JObject>()
                            .Where(c => string.Equals((string)c["source"], "corporate", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        // Only seeds this writer manages: created here, or already carrying corporate
                        // copies. A hand-written project seed that replaces the corporate one is left alone.
                        if (oldCorp.Count == 0 && !createdSeeds.Contains((string)s["seedId"] ?? "")) continue;
                        var cs = corpSeeds.OfType<JObject>().FirstOrDefault(x =>
                            string.Equals((string)x["seedId"], (string)s["seedId"], StringComparison.OrdinalIgnoreCase));
                        var before = new JArray(oldCorp.Select(c => c.DeepClone()));
                        foreach (var c in oldCorp) c.Remove();
                        if (string.IsNullOrEmpty((string)s["category"]) && cs != null) s["category"] = cs["category"];
                        if (cs?["candidates"] is JArray corpCands)
                            foreach (var cc in corpCands.OfType<JObject>())
                            {
                                if (FindByLabel(cands, (string)cc["label"]) != null) continue;
                                var copy = (JObject)cc.DeepClone();
                                copy["source"] = "corporate";
                                cands.Add(copy);
                            }
                        var after = new JArray(cands.OfType<JObject>()
                            .Where(c => string.Equals((string)c["source"], "corporate", StringComparison.OrdinalIgnoreCase)));
                        if (!JToken.DeepEquals(before, after)) changed = true;
                    }
                }
            }
            catch (Exception ex) { result.Warnings.Add($"Swap registry: corporate candidates not carried: {ex.Message}"); }

            if (!changed && !legacyFileRead) return;
            registry["_updated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            try
            {
                OutputLocationHelper.WriteAllTextAtomic(registryPath, registry.ToString(Formatting.Indented));
                result.Warnings.Add($"Swap registry: {added} added, {updated} updated, {pruned} pruned, {folded} folded from legacy → {registryPath}");
            }
            catch (Exception ex) { result.Warnings.Add($"Swap registry save failed: {ex.Message}"); return; }

            if (legacyFileRead)
            {
                try
                {
                    string moved = legacyPath + ".migrated_" + DateTime.Now.ToString("yyyyMMdd");
                    if (!File.Exists(moved)) File.Move(legacyPath, moved);
                    StingLog.Info($"Swap registry: legacy {legacyPath} folded into {registryPath}, renamed to {moved}");
                }
                catch (Exception ex) { result.Warnings.Add($"Swap registry: legacy file folded but not renamed: {ex.Message}"); }
            }
        }

        /// <summary>
        /// The reader matches <c>familyNamePattern</c> as a regex against loaded family names, so a
        /// spec's .rfa path becomes an exact, escaped match on the file name without extension.
        /// </summary>
        private static string FamilyPathPattern(string familyPath)
        {
            string name = "";
            try { name = Path.GetFileNameWithoutExtension((familyPath ?? "").Trim()); }
            catch (Exception ex) { StingLog.Warn($"FamilyPathPattern '{familyPath}': {ex.Message}"); }
            return string.IsNullOrEmpty(name) ? "" : "^" + System.Text.RegularExpressions.Regex.Escape(name) + "$";
        }

        private static string ResolveSeedOutputFolder(Document doc)
        {
            // This used to read the project directory into a baseDir, fall back to
            // Path.Combine(Path.GetTempPath(), "STING_Seeds"), and then DISCARD both: the
            // return never used either value. Eight lines that read as a resolver with a
            // safety net, and neither half ran. Removed rather than wired - StingPaths IS
            // the resolver, and a dead fallback is worse than none because it reads as cover.
            return StingPaths.MetaFile(doc, "_BIM_COORD", "Families", "Seeds");
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
            string outRoot, SeedRebuildMode mode, ref string message,
            List<(string seedName, string reason)> gateIncomplete = null,
            SeedTypeMigrationReport migration = null)
        {
            var panel = StingResultPanel.Create("Seed Families — Build");
            string migrated = migration != null && migration.Types > 0
                ? $"  |  migrated {migration.Types} type(s), {migration.Instances} instance(s)" : "";
            panel.SetSubtitle($"Mode: {mode}  |  {r.Created} created, {r.Existed} existed, " +
                              $"{r.Protected} protected, {r.Failed} failed" + migrated);

            panel.AddSection("SUMMARY")
                .Metric("Mode",       mode.ToString())
                .Metric("Created",    r.Created.ToString())
                .Metric("Existed",    r.Existed.ToString())
                .Metric("Protected",  r.Protected.ToString())
                .Metric("Failed",     r.Failed.ToString())
                .Metric("Warnings",   r.Warnings.Count.ToString())
                .Metric("Output",     outRoot);

            if (migration != null && (migration.Types > 0 || migration.Messages.Count > 0))
            {
                panel.AddSection("RENAMED TYPES MIGRATED")
                    .Metric("Migrated", $"{migration.Types} type(s), {migration.Instances} instance(s)");
                foreach (var m in migration.Messages.Take(20)) panel.Text(m);
            }

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
            PresetDialog.Show(panel, ref message);
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
                        catch (Exception ex2) { warnings.Add($"Connector audit: '{id}' — open family failed: {ex2.Message}"); continue; }

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
