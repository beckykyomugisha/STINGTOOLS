// ClashRunCommand.cs — rec-7. End-to-end headless clash run:
//   MeshExtractor.Extract
//     → ClashKernel.BuildIndexes + Run
//     → ElementFacts hydration (category / system / workset)
//     → ClashMatrix.Match              (rec-6 pre-filter)
//     → ClashRuleEngine.Classify
//     → ClashExclusions.IsExcluded
//     → ClashIdentity.Compute
//     → ClashHistory.MergeWithPrior
//     → ClashGrouper.Group
//     → ResolutionHeuristics.Suggest
//     → ClashPersistence.Save to {output}/clashes.json
//
// Stage 5 acceptance gate: this is the command dispatched by the BCC
// Clash tab's "Run Clash" button and by ClashScheduler's hourly timer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClashRunCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData?.Application?.ActiveUIDocument;
                var doc = uiDoc?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                if (!(doc.ActiveView is View3D view3d) || view3d.IsTemplate)
                {
                    var fallback = new FilteredElementCollector(doc)
                        .OfClass(typeof(View3D))
                        .Cast<View3D>()
                        .FirstOrDefault(v => !v.IsTemplate);
                    if (fallback == null)
                    {
                        TaskDialog.Show("STING Clash",
                            "No 3D view available. Open or create a 3D view to run clash detection.");
                        return Result.Cancelled;
                    }
                    view3d = fallback;
                }

                var overall = Stopwatch.StartNew();

                // ── Config / output paths ──
                string outDir = OutputLocationHelper.GetOutputDirectory(doc) ?? Path.GetTempPath();
                Directory.CreateDirectory(outDir);
                string clashesJson  = Path.Combine(outDir, "clashes.json");
                string matrixJson   = FindDataFile("default_clash_matrix.json");
                string rulesJson    = FindDataFile("default_clash_rules.json");
                string exclusionsJson = Path.Combine(outDir, "clash_exclusions.json");

                var matrix = ClashMatrix.LoadOrDefault(matrixJson);
                var rules = ClashRuleLibrary.LoadAugmented(rulesJson);
                var ruleEngine = new ClashRuleEngine(rules);
                var exclusions = ClashExclusions.Load(exclusionsJson);

                // ── Stage 1: extract meshes + build indices + run broad+narrow ──
                var meshes = MeshExtractor.Extract(doc, view3d);
                if (meshes.Count == 0)
                {
                    TaskDialog.Show("STING Clash",
                        "No tessellatable geometry found in the active 3D view.");
                    return Result.Cancelled;
                }

                var kernel = new ClashKernel();
                kernel.BuildIndexes(meshes.Values);
                var hits = kernel.Run();

                // ── rec-6: pre-hydrate ElementFacts so matrix/rule-engine runs O(1) per pair ──
                var factsByKey = BuildFactsByKey(doc, meshes);

                // ── Stage 2: classify, exclude, identity-hash ──
                var run = new ClashRunRecord
                {
                    RunId = Guid.NewGuid().ToString("N").Substring(0, 12),
                    MatrixFile = matrixJson,
                    RulesFile = rulesJson,
                    ExclusionsFile = exclusionsJson,
                };

                int filtered = 0, excluded = 0;
                foreach (var h in hits)
                {
                    factsByKey.TryGetValue(h.A, out var fa);
                    factsByKey.TryGetValue(h.B, out var fb);
                    if (fa == null || fb == null) continue;

                    // rec-6: match the pair to a matrix cell. A pair that doesn't match any
                    // enabled cell is outside the project's configured coordination scope.
                    var cell = matrix.Match(fa, fb);
                    if (cell == null) { filtered++; continue; }

                    var classified = ruleEngine.Classify(h, fa, fb, cell);
                    if (classified.Verdict != ClashVerdict.Keep) { filtered++; continue; }

                    string identity = ClashIdentity.Compute(h.A, h.B, cell.PairId, h.Centroid);
                    if (exclusions.IsExcluded(identity)) { excluded++; continue; }

                    run.Clashes.Add(new ClashRecord
                    {
                        Identity = identity,
                        MatrixPairId = cell.PairId,
                        Severity = cell.Severity ?? "MED",
                        Tolerance = cell.Tolerance ?? "HARD",
                        ElementA = ToRecord(h.A, fa),
                        ElementB = ToRecord(h.B, fb),
                        VolumeMm3 = h.VolumeMm3,
                        AabbMin = new[] { h.AabbMin.X, h.AabbMin.Y, h.AabbMin.Z },
                        AabbMax = new[] { h.AabbMax.X, h.AabbMax.Y, h.AabbMax.Z },
                        Centroid = new[] { h.Centroid.X, h.Centroid.Y, h.Centroid.Z },
                    });
                }

                // Assign CLH-N external ids in stable order.
                int seq = 1;
                foreach (var c in run.Clashes)
                    c.Id = ClashIdentity.NewClashId(DateTime.UtcNow, seq++);

                run.Stats.Raw = hits.Count;
                run.Stats.Tier1Filtered = filtered;
                run.Stats.Excluded = excluded;

                // ── Stage 2: history diff ──
                var prior = ClashPersistence.Load(clashesJson);
                ClashHistory.MergeWithPrior(run, prior);

                // ── Stage 4: group + resolution hints ──
                run.Groups = ClashGrouper.Group(run.Clashes);
                run.Stats.Groups = run.Groups.Count;
                foreach (var c in run.Clashes)
                    c.ResolutionHint = ResolutionHeuristics.Suggest(c);

                run.DurationMs = overall.ElapsedMilliseconds;

                // ── Persist ──
                ClashPersistence.Save(run, clashesJson);

                StingLog.Info($"ClashRun: raw={run.Stats.Raw} filtered={run.Stats.Tier1Filtered} " +
                    $"excluded={run.Stats.Excluded} kept={run.Clashes.Count} " +
                    $"groups={run.Stats.Groups} new={run.Stats.New} active={run.Stats.Active} " +
                    $"resolved={run.Stats.Resolved} reintro={run.Stats.Reintroduced} " +
                    $"{run.DurationMs}ms → {clashesJson}");

                TaskDialog.Show("STING Clash",
                    $"Run complete in {run.DurationMs} ms.\n\n" +
                    $"Raw hits: {run.Stats.Raw}\n" +
                    $"Filtered (matrix/rule): {run.Stats.Tier1Filtered}\n" +
                    $"Excluded: {run.Stats.Excluded}\n" +
                    $"Kept: {run.Clashes.Count}\n" +
                    $"Groups: {run.Stats.Groups}\n\n" +
                    $"New: {run.Stats.New}   Active: {run.Stats.Active}   " +
                    $"Resolved: {run.Stats.Resolved}   Reintroduced: {run.Stats.Reintroduced}\n\n" +
                    $"Saved: {clashesJson}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClashRunCommand.Execute", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// rec-6: Hydrate ElementFacts once per mesh so the matrix matcher doesn't
        /// re-read Revit element params per candidate pair. Category + workset are
        /// from the live element; system is from MEP system property when present.
        /// </summary>
        private static Dictionary<ClashElementKey, ElementFacts> BuildFactsByKey(Document doc,
            Dictionary<ClashElementKey, ClashMeshBuffer> meshes)
        {
            var map = new Dictionary<ClashElementKey, ElementFacts>();
            foreach (var kv in meshes)
            {
                var key = kv.Key;
                var mesh = kv.Value;
                Element el = null;
                try
                {
                    // Host-doc elements only — linked-doc elements don't participate
                    // in the CustomExporter-sourced Clash flow with a writable Element
                    // handle. Facts default to the mesh's category + empty system.
                    if (key.LinkInstanceElementId == -1)
                        el = doc.GetElement(new ElementId(key.ElementId));
                }
                catch { }

                var facts = new ElementFacts
                {
                    Category = mesh.Category ?? "",
                    System = ReadSystem(el),
                    Classification = "",
                    Workset = ReadWorkset(doc, el),
                };
                map[key] = facts;
            }
            return map;
        }

        private static string ReadSystem(Element el)
        {
            if (el == null) return "";
            try
            {
                // MEP curves expose MEPSystem; FamilyInstance exposes MEPModel.ConnectorManager.Owner.
                if (el is MEPCurve mc && mc.MEPSystem != null) return mc.MEPSystem.Name ?? "";
                if (el is Autodesk.Revit.DB.Mechanical.Space sp) return "";
                var p = el.LookupParameter("System Name");
                if (p != null) return p.AsString() ?? "";
            }
            catch { }
            return "";
        }

        private static string ReadWorkset(Document doc, Element el)
        {
            try
            {
                if (doc == null || el == null || !doc.IsWorkshared) return "";
                var ws = doc.GetWorksetTable().GetWorkset(el.WorksetId);
                return ws?.Name ?? "";
            }
            catch { return ""; }
        }

        private static ClashElementRecord ToRecord(ClashElementKey key, ElementFacts facts)
        {
            return new ClashElementRecord
            {
                IfcGuid = key.IfcGuid,
                UniqueId = key.UniqueId,
                ElementId = key.ElementId,
                DocGuid = key.DocGuid,
                LinkInstanceId = key.LinkInstanceElementId,
                Category = facts?.Category ?? "",
                System = facts?.System ?? "",
            };
        }

        /// <summary>
        /// Find a seed data file in either the project-output directory (user
        /// edits) or the plugin's data\clash\ directory (ships with the DLL).
        /// </summary>
        private static string FindDataFile(string fileName)
        {
            try
            {
                string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dllDir = Path.GetDirectoryName(dll) ?? "";
                string[] candidates =
                {
                    Path.Combine(dllDir, "data", "clash", fileName),
                    Path.Combine(dllDir, "data", fileName),
                    Path.Combine(dllDir, fileName),
                };
                foreach (var c in candidates) if (File.Exists(c)) return c;
            }
            catch { }
            return fileName;   // let LoadOrDefault handle missing-file fallback
        }
    }
}
