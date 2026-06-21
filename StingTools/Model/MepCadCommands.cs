// ============================================================================
// MepCadCommands.cs — Phase: MEP-from-DWG V1.
//
// MODEL-tab command surface for MEP-from-DWG, peer to the structural StrCAD*
// commands. Both reuse the shared CADToModelEngine extraction core via the
// MepDetectionEngine / MepFixtureBuilder pipeline (Core/Cad/Mep).
//
//   Mep_CadPreview  (ReadOnly) — audit: per-layer counts + which DWG blocks
//                   would place as which fixture vs skip, + straight-run candidates.
//   Mep_CadToModel  (Manual)   — place MEP fixtures from blocks AND straight runs
//                   (Duct/Pipe/Conduit/CableTray) from lines.
//
// V1 placed fixtures from blocks. V2 adds straight runs (this file + MepRunBuilder).
// Fixture host-snapping and the per-layer wizard are the remaining V2 items.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Cad.Mep;
using StingTools.Core.Storage;
using StingTools.UI;

namespace StingTools.Model
{
    internal static class MepCadShared
    {
        /// <summary>Pick the DWG import to convert. Prefers one shown in the active
        /// view; otherwise the first. Returns null + a message when none.</summary>
        public static ImportInstance PickImport(Document doc, out int count)
        {
            var imports = CADToModelEngine.FindImportInstances(doc);
            count = imports.Count;
            if (imports.Count == 0) return null;
            if (doc.ActiveView != null)
            {
                var inView = imports.FirstOrDefault(i => i.OwnerViewId == doc.ActiveView.Id);
                if (inView != null) return inView;
            }
            return imports[0];
        }

        /// <summary>Target level: the active plan's level, else the lowest level.</summary>
        public static Level ResolveLevel(Document doc)
        {
            if (doc.ActiveView is ViewPlan vp && vp.GenLevel != null) return vp.GenLevel;
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).FirstOrDefault();
        }

        /// <summary>True when at least one FamilySymbol of the named category is loaded.</summary>
        public static bool AnySymbol(Document doc, string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return false;
            return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Any(fs => fs.Category != null &&
                           string.Equals(fs.Category.Name, categoryName, StringComparison.OrdinalIgnoreCase));
        }

        public struct PlaceOutcome
        {
            public MepBuildResult Fixtures;
            public MepRunBuildResult Runs;
            public MepRunBuildResult Risers;
            public MepFittingBuildResult Fittings;
        }

        /// <summary>The full V1→V3 placement pass: fixtures (host-snapped) → horizontal runs
        /// → risers → fittings at coincident run/riser ends. One outcome for reporting.</summary>
        public static PlaceOutcome PlaceAll(Document doc, MepDetectionResult detection, Level level, bool hostSnap)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            var fx = new MepFixtureBuilder(doc).Place(detection, level, hostSnap);
            // P1.3 — pass riser/stack candidates so drainage chains fall toward a stack.
            // P6-2.4 — one MepRunBuilder for both passes so types/systems resolve once.
            var rb = new MepRunBuilder(doc);
            var run = rb.Build(detection.Runs, level, null, detection.Risers);
            var riser = rb.BuildRisers(detection.Risers, level, levels);
            var ids = run.CreatedIds.Concat(riser.CreatedIds).ToList();
            var fitB = new MepFittingBuilder(doc);
            var fit = ids.Count > 1 ? fitB.Build(ids) : new MepFittingBuildResult();
            // P5 2.1 — mid-run branch taps (split main + tee). Run after the end-junction pass.
            if (ids.Count > 1) fitB.BuildMidRunTaps(ids, fit);
            // P1.4 — report which risers joined a horizontal run.
            if (riser.CreatedIds.Count > 0) fitB.CountRiserJoins(riser.CreatedIds, fit);
            return new PlaceOutcome { Fixtures = fx, Runs = run, Risers = riser, Fittings = fit };
        }

        // ── P5 1.1/1.2 — re-run guard + atomic pass ──────────────────────────
        public struct ConversionRun { public bool Cancelled; public PlaceOutcome Outcome; public string Preface; }

        /// <summary>Stable key for "the same import" — prefer the CAD type (DWG file) name.</summary>
        public static string ImportKey(Document doc, ImportInstance import)
        {
            if (import == null) return "import";
            try { if (doc.GetElement(import.GetTypeId()) is ElementType t && !string.IsNullOrEmpty(t.Name)) return t.Name; }
            catch { }
            try { if (!string.IsNullOrEmpty(import.Name)) return import.Name; } catch { }
            return import.UniqueId ?? "import";
        }

        public static List<ElementId> AllCreatedIds(PlaceOutcome o)
        {
            var ids = new List<ElementId>();
            ids.AddRange(o.Fixtures.CreatedIds);
            ids.AddRange(o.Runs.CreatedIds);
            ids.AddRange(o.Risers.CreatedIds);
            ids.AddRange(o.Fittings.CreatedIds);
            return ids;
        }

        /// <summary>The whole conversion as ONE undo/rollback unit (TransactionGroup), with a
        /// re-run guard: a prior conversion of the same import offers Replace / Add / Skip,
        /// and every created element is stamped with the import key for next time.</summary>
        public static ConversionRun RunConversion(Document doc, MepDetectionResult detection, Level level, bool hostSnap, ImportInstance import)
        {
            var run = new ConversionRun { Preface = "" };
            string key = ImportKey(doc, import);
            var prior = StingMepCadStampSchema.FindStamped(doc, key);
            bool replace = false;
            if (prior.Count > 0)
            {
                var td = new TaskDialog("MEP CAD → Model")
                {
                    MainInstruction = $"'{key}' was already converted",
                    MainContent = $"Found {prior.Count} MEP element(s) from a previous conversion of this import. Choose how to proceed:",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    AllowCancellation = true
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Replace", $"Delete the {prior.Count} previous element(s), then convert again");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Add anyway", "Convert again without deleting (this will duplicate)");
                var r = td.Show();
                if (r == TaskDialogResult.Cancel) { run.Cancelled = true; run.Preface = "Skipped — existing conversion left in place."; return run; }
                replace = r == TaskDialogResult.CommandLink1;
            }

            using (var tg = new TransactionGroup(doc, "STING MEP CAD → Model"))
            {
                tg.Start();
                try
                {
                    if (replace) DeleteElements(doc, prior);
                    run.Outcome = PlaceAll(doc, detection, level, hostSnap);
                    StampAll(doc, key, AllCreatedIds(run.Outcome));
                    tg.Assimilate();
                }
                catch (Exception ex)
                {
                    tg.RollBack();
                    StingLog.Error("MepCad RunConversion", ex);
                    run.Cancelled = true;
                    run.Preface = "Conversion failed and was rolled back: " + ex.Message;
                    return run;
                }
            }
            run.Preface = replace ? $"Replaced {prior.Count} prior element(s). "
                : prior.Count > 0 ? "Added alongside the existing conversion. " : "";
            return run;
        }

        private static void DeleteElements(Document doc, List<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            using (var t = new Transaction(doc, "STING MEP CAD: Remove previous conversion"))
            {
                t.Start();
                try { doc.Delete(ids.Where(id => doc.GetElement(id) != null).ToList()); t.Commit(); }
                catch (Exception ex) { t.RollBack(); StingLog.Warn($"Remove previous conversion: {ex.Message}"); }
            }
        }

        private static void StampAll(Document doc, string key, List<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            using (var t = new Transaction(doc, "STING MEP CAD: Stamp conversion"))
            {
                t.Start();
                foreach (var id in ids) { var el = doc.GetElement(id); if (el != null) StingMepCadStampSchema.Stamp(el, key); }
                t.Commit();
            }
        }

        /// <summary>Human-readable result block shared by the Convert command + the wizard.</summary>
        public static string Report(PlaceOutcome o)
        {
            var fx = o.Fixtures; var run = o.Runs; var riser = o.Risers; var fit = o.Fittings;
            var sb = new StringBuilder();
            sb.AppendLine("FIXTURES");
            sb.AppendLine($"   Placed: {fx.Placed}  (hosted {fx.Hosted}, skipped-no-family {fx.SkippedNoSymbol}, failed {fx.Failed})");
            if (fx.ByCategory.Count > 0)
                foreach (var kv in fx.ByCategory.OrderByDescending(k => k.Value.placed + k.Value.skipped))
                    sb.AppendLine($"      {kv.Key,-22} {kv.Value.placed} / {kv.Value.skipped}");
            sb.AppendLine();
            sb.AppendLine("RUNS");
            sb.AppendLine($"   Created: {run.Created}  (failed {run.Failed})");
            foreach (var kv in run.ByKind.OrderByDescending(k => k.Value))
                sb.AppendLine($"      {kv.Key,-10} {kv.Value}");
            if (run.BySystem.Count > 0)
            {
                sb.AppendLine("   Systems assigned:");
                foreach (var kv in run.BySystem.OrderByDescending(k => k.Value))
                    sb.AppendLine($"      {kv.Key,-26} {kv.Value}");
            }
            if (run.ServiceDefaultedDuct > 0 || run.ServiceDefaultedPipe > 0)
                sb.AppendLine($"   ⚠ Defaulted systems: {run.ServiceDefaultedDuct} duct(s) → Supply (no service keyword), {run.ServiceDefaultedPipe} pipe(s) → first-available (no token).");
            if (run.SizeSnapped > 0)
                sb.AppendLine($"   ⚠ {run.SizeSnapped} run(s) — applied size snapped to the type catalog (differs from the requested size).");
            if (run.DrainageDirectionUnverified > 0)
                sb.AppendLine($"   ⚠ {run.DrainageDirectionUnverified} drainage run(s) — fall direction unverified (no stack); confirm fall.");
            if (run.DrainageDirectionAmbiguous > 0)
                sb.AppendLine($"   ⚠ {run.DrainageDirectionAmbiguous} drainage run(s) — fall target ambiguous (≥2 stacks equally near); confirm.");
            sb.AppendLine();
            sb.AppendLine("RISERS");
            sb.AppendLine($"   Created: {riser.Created}  (failed {riser.Failed}; joined-to-run {fit.JoinedRisers}, floating {fit.FloatingRisers})");
            sb.AppendLine();
            sb.AppendLine("FITTINGS");
            sb.AppendLine($"   Junctions: {fit.Junctions}   Elbows {fit.Elbows} · Tees {fit.Tees} · Crosses {fit.Crosses} · Unions {fit.Unions} · failed {fit.Failed}");
            sb.AppendLine($"   Mid-run taps: {fit.TapsJoined}/{fit.TapsFound} teed" +
                          (fit.TapsUnsupported > 0 ? $" ({fit.TapsUnsupported} conduit/tray unsupported)" : ""));
            if (fit.NearMissJunctions > 0)
                sb.AppendLine($"   ⚠ ≈{fit.NearMissJunctions} near-miss junction(s) just outside tolerance — raise the fitting tolerance to capture.");

            var warns = fx.Warnings.Concat(run.Warnings).Concat(riser.Warnings).Concat(fit.Warnings).ToList();
            if (warns.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var w in warns.Take(12)) sb.AppendLine($"   {w}");
            }
            return sb.ToString();
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepCadPreviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var import = MepCadShared.PickImport(doc, out int importCount);
            if (import == null)
            {
                TaskDialog.Show("MEP CAD Preview",
                    "No DWG/DXF import found. Import (not just link) the MEP drawing first, then re-run.");
                return Result.Succeeded;
            }

            var detection = new MepDetectionEngine(doc).Detect(import);

            var sb = new StringBuilder();
            sb.AppendLine($"DWG imports in document: {importCount} (previewing 1).");
            sb.AppendLine($"Entities: {detection.TotalEntities}   Blocks: {detection.TotalBlocks}   Layers: {detection.LayerCounts.Count}");
            sb.AppendLine();

            int wouldPlace = 0, wouldSkipNoFamily = 0;
            sb.AppendLine("Fixtures recognised by the map (block → category):");
            if (detection.Fixtures.Count == 0)
                sb.AppendLine("   (none — no block names matched STING_DWG_FIXTURE_MAP.json)");
            foreach (var g in detection.ByCategory())
            {
                bool hasFamily = MepCadShared.AnySymbol(doc, g.Key);
                int n = g.Count();
                if (hasFamily) wouldPlace += n; else wouldSkipNoFamily += n;
                sb.AppendLine($"   {g.Key,-22} {n,4}   {(hasFamily ? "→ would place" : "→ SKIP (no family of this category loaded)")}");
            }

            sb.AppendLine();
            sb.AppendLine($"Would place: {wouldPlace}    Would skip (no family loaded): {wouldSkipNoFamily}");

            // P2.1 — low-confidence matches: block name matched a rule but the block's DWG
            // layer points at a different discipline. Surface so the user can confirm.
            if (detection.LayerMismatchCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"⚠ Low-confidence ({detection.LayerMismatchCount}) — block name ↔ layer discipline disagree (confirm before placing):");
                foreach (var f in detection.Fixtures.Where(f => f.LayerMismatch).Take(10))
                    sb.AppendLine($"   {f.BlockName}  →  {f.Category} [{f.RuleDiscipline}]  but layer '{f.Block?.LayerName}' is [{f.LayerDiscipline}]");
            }

            // P3.1 — hosting honesty: which placeable fixtures will host vs be forced
            // unhosted (family is not a hosted/work-plane type), so preview matches reality.
            if (detection.Fixtures.Count > 0)
            {
                int willHost = 0, forcedUnhosted = 0, free = 0;
                var symByCat = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
                foreach (var fx in detection.Fixtures)
                {
                    if (!symByCat.TryGetValue(fx.Category, out var sym))
                        symByCat[fx.Category] = sym = MepFixtureBuilder.PreviewResolveSymbol(doc, fx.Category, fx.Rule?.FamilyHint, fx.Rule?.TypeHint);
                    if (sym == null) continue;   // already counted as skip-no-family
                    string intent = MepFixtureBuilder.HostIntentName(fx.Category, fx.Rule?.MountingReference);
                    if (intent == "None") free++;
                    else if (MepFixtureBuilder.FamilyIsHostable(sym)) willHost++;
                    else forcedUnhosted++;
                }
                if (willHost + forcedUnhosted + free > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Hosting: {willHost} will host to wall/ceiling, {free} placed free, " +
                                  $"{forcedUnhosted} forced unhosted (family is not a hosted/work-plane type).");
                }
            }

            // V2 — straight-run candidates from lines.
            sb.AppendLine();
            sb.AppendLine($"Straight runs from lines ({detection.Runs.Count} of {detection.TotalLines} lines on MEP layers):");
            if (detection.Runs.Count == 0)
                sb.AppendLine("   (none — no lines on duct/pipe/conduit/tray layers above the 0.5 m floor)");
            foreach (var (kind, count, totalM) in detection.RunsByKind())
                sb.AppendLine($"   {kind,-10} {count,4}   {totalM:F1} m");
            if (detection.Runs.Count > 0)
            {
                sb.AppendLine("   Service (→ MEP system at placement):");
                foreach (var (service, count) in detection.RunsByService())
                    sb.AppendLine($"      {service,-26} {count}");
            }
            // P6-1.1 — flag runs whose system will fall back silently (no service keyword).
            if (detection.DuctServiceDefaulted > 0 || detection.PipeServiceDefaulted > 0)
                sb.AppendLine($"   ⚠ Defaulted systems: {detection.DuctServiceDefaulted} duct(s) → Supply, {detection.PipeServiceDefaulted} pipe(s) → first-available (no service keyword — verify).");
            // P6-1.3 — accuracy flags.
            if (detection.RectCoercedRunCount > 0)
                sb.AppendLine($"   ⚠ {detection.RectCoercedRunCount} pipe/conduit run(s) — a W×H layer size was coerced to a diameter (larger dim); confirm.");
            if (detection.DrainageRunCount > 0)
                sb.AppendLine($"   (of which {detection.DrainageRunCount} drainage pipe(s) get a gravity fall toward the nearest stack)");
            if (detection.MirroredFixtureCount > 0)
                sb.AppendLine($"   ⚠ {detection.MirroredFixtureCount} mirrored (negative-scale) block(s) — placement rotation may be wrong; check orientation.");
            sb.AppendLine($"Risers (UP/DN/RISER blocks → vertical segments): {detection.Risers.Count}");

            // P3.2 — routing-preference pre-flight: will junctions actually form? A run type
            // with no elbow/tee fitting family in its routing prefs silently produces no
            // fittings — surface that here, before placing.
            var kindsPresent = detection.Runs.Select(r => r.Kind)
                .Concat(detection.Risers.Select(r => r.Kind)).Distinct().ToList();
            if (kindsPresent.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Fitting pre-flight (run type routing preferences):");
                foreach (var st in MepFittingBuilder.PreflightRoutingPrefs(doc, kindsPresent))
                {
                    if (!st.TypeFound) { sb.AppendLine($"   {st.Kind,-10} — no type in project (runs + fittings will skip)"); continue; }
                    string elbow = st.HasElbow ? "elbows ✓" : "elbows ✗";
                    string tee = st.HasJunction ? "tees ✓" : "tees ✗";
                    string flag = st.Ok ? "" : "   ⚠ junctions may not form";
                    sb.AppendLine($"   {st.Kind,-10} '{st.TypeName}'  {elbow} · {tee}{flag}");
                }
            }

            if (detection.UnmatchedBlockCounts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Unmatched block names ({detection.UnmatchedBlockCounts.Count}) — add rules to _BIM_COORD/dwg_fixture_map.json:");
                foreach (var kv in detection.UnmatchedBlockCounts.OrderByDescending(k => k.Value).Take(12))
                    sb.AppendLine($"   {kv.Value,4}  {kv.Key}");
            }

            new TaskDialog("MEP CAD Preview")
            {
                MainInstruction = $"{detection.Fixtures.Count} fixture(s) · {wouldPlace} placeable · {detection.Runs.Count} run(s) · {detection.Risers.Count} riser(s)",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Mep_CadPreview: recognised={detection.Fixtures.Count} placeable={wouldPlace} noFamily={wouldSkipNoFamily} runs={detection.Runs.Count} unmatched={detection.UnmatchedBlockCounts.Count}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepCadToModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var import = MepCadShared.PickImport(doc, out int importCount);
            if (import == null)
            {
                TaskDialog.Show("MEP CAD → Model",
                    "No DWG/DXF import found. Import (not just link) the MEP drawing first, then re-run.");
                return Result.Succeeded;
            }

            var level = MepCadShared.ResolveLevel(doc);
            if (level == null) { TaskDialog.Show("MEP CAD → Model", "No levels in the project — create a level first."); return Result.Failed; }

            var detection = new MepDetectionEngine(doc).Detect(import);
            if (detection.Fixtures.Count == 0 && detection.Runs.Count == 0)
            {
                TaskDialog.Show("MEP CAD → Model",
                    "No DWG blocks matched the fixture map and no run lines were found. Run MEP CAD Preview to see " +
                    "unmatched block names + run candidates, then add rules to _BIM_COORD/dwg_fixture_map.json.");
                return Result.Succeeded;
            }

            // Confirm before writing.
            var confirm = new TaskDialog("MEP CAD → Model")
            {
                MainInstruction = $"Place {detection.Fixtures.Count} fixture(s) + {detection.Runs.Count} run(s) + {detection.Risers.Count} riser(s) on level '{level.Name}'?",
                MainContent = "Fixtures with no matching family loaded are skipped + counted (no geometry synthesised). " +
                              "Runs use the first matching type/system, sized from the layer or a default, at a per-kind " +
                              "elevation; drainage pipes get a gravity fall. Risers become vertical segments to the adjacent " +
                              "level; fittings (elbow/tee/cross) are inserted where run ends meet. Everything is workset-" +
                              "assigned and ISO 19650 auto-tagged.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Place fixtures + runs + risers + fittings");
            if (confirm.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

            // P5 1.1/1.2 — re-run guard + atomic (one TransactionGroup + stamp).
            var conv = MepCadShared.RunConversion(doc, detection, level, hostSnap: true, import);
            if (conv.Cancelled)
            {
                if (!string.IsNullOrEmpty(conv.Preface)) TaskDialog.Show("MEP CAD → Model", conv.Preface);
                return Result.Cancelled;
            }
            var outcome = conv.Outcome;

            var sb = new StringBuilder();
            sb.AppendLine($"Level: {level.Name}   DWG imports: {importCount}");
            if (!string.IsNullOrEmpty(conv.Preface)) sb.AppendLine(conv.Preface);
            sb.AppendLine();
            sb.Append(MepCadShared.Report(outcome));

            new TaskDialog("MEP CAD → Model")
            {
                MainInstruction = $"{outcome.Fixtures.Placed} fixture(s) · {outcome.Runs.Created} run(s) · {outcome.Risers.Created} riser(s) · {outcome.Fittings.Created} fitting(s)",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Mep_CadToModel: fixtures={outcome.Fixtures.Placed} runs={outcome.Runs.Created} risers={outcome.Risers.Created} fittings={outcome.Fittings.Created}");
            return Result.Succeeded;
        }
    }

    /// <summary>Per-layer mapping wizard: pick level + host-snap, include/exclude layers,
    /// override run kind + offset, then place fixtures + runs.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepCadWizardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var import = MepCadShared.PickImport(doc, out int importCount);
            if (import == null)
            {
                TaskDialog.Show("MEP CAD Wizard",
                    "No DWG/DXF import found. Import (not just link) the MEP drawing first, then re-run.");
                return Result.Succeeded;
            }

            var detection = new MepDetectionEngine(doc).Detect(import);
            if (detection.Fixtures.Count == 0 && detection.Runs.Count == 0)
            {
                TaskDialog.Show("MEP CAD Wizard",
                    "No DWG blocks matched the fixture map and no run lines were found. " +
                    "Run MEP CAD Preview to see what is on the drawing.");
                return Result.Succeeded;
            }

            var wiz = new MepCadWizard(doc, detection);
            bool? shown = wiz.ShowDialog();
            if (shown != true || !wiz.Confirmed) return Result.Cancelled;

            var level = wiz.SelectedLevel ?? MepCadShared.ResolveLevel(doc);
            if (level == null) { TaskDialog.Show("MEP CAD Wizard", "No level selected / available."); return Result.Failed; }

            var filtered = wiz.ApplyTo(detection);
            // P5 1.1/1.2 — re-run guard + atomic (one TransactionGroup + stamp).
            var conv = MepCadShared.RunConversion(doc, filtered, level, wiz.HostSnap, import);
            if (conv.Cancelled)
            {
                if (!string.IsNullOrEmpty(conv.Preface)) TaskDialog.Show("MEP CAD Wizard", conv.Preface);
                return Result.Cancelled;
            }
            var outcome = conv.Outcome;

            var sb = new StringBuilder();
            sb.AppendLine($"Level: {level.Name}   Host-snap: {(wiz.HostSnap ? "on" : "off")}");
            if (!string.IsNullOrEmpty(conv.Preface)) sb.AppendLine(conv.Preface);
            sb.AppendLine();
            sb.Append(MepCadShared.Report(outcome));

            new TaskDialog("MEP CAD Wizard")
            {
                MainInstruction = $"{outcome.Fixtures.Placed} fixture(s) · {outcome.Runs.Created} run(s) · {outcome.Risers.Created} riser(s) · {outcome.Fittings.Created} fitting(s)",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Mep_CadWizard: fixtures={outcome.Fixtures.Placed} runs={outcome.Runs.Created} risers={outcome.Risers.Created} fittings={outcome.Fittings.Created} level={level.Name}");
            return Result.Succeeded;
        }
    }
}
