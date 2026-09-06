// ══════════════════════════════════════════════════════════════════════════
//  ProjectBaselineCommands.cs — read the model, compare it to the baseline,
//  and (only on confirmation) create what is missing.
//
//  The decision logic lives in Core/Baseline and is Revit-free. This file is
//  deliberately thin: gather names, hand them to the auditor, show the report,
//  mint what the auditor marked Missing. Nothing here decides anything.
//
//  Two contracts worth keeping:
//    * Apply mints ONLY what the audit listed as Missing. It never edits an
//      existing type — a type whose name matches but whose build-up differs is
//      reported as a conflict for a human, because the model's version may be
//      the correct one.
//    * Every creation is individually try/caught and counted. A baseline that
//      half-applied and reported success would be worse than one that failed.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Baseline;
using StingTools.Core.MaterialSchedule;

namespace StingTools.Commands.Baseline
{
    internal static class BaselineRegistry
    {
        /// <summary>Corporate baseline, with a project override layered by NAME —
        /// the same corporate/project split every other STING data file uses.</summary>
        public static ProjectBaseline Load(Document doc)
        {
            var b = ReadJson(StingToolsApp.FindDataFile("STING_PROJECT_BASELINE.json"))
                    ?? new ProjectBaseline();

            ProjectBaseline over = null;
            try { over = ReadJson(StingPaths.MetaFile(doc, "_BIM_COORD", "project_baseline.json")); }
            catch (Exception ex) { StingLog.Warn($"BaselineRegistry override: {ex.Message}"); }
            if (over == null) return b;

            Merge(b.Materials, over.Materials, m => m.Name);
            Merge(b.WallTypes, over.WallTypes, t => t.Name);
            Merge(b.FloorTypes, over.FloorTypes, t => t.Name);
            Merge(b.RoofTypes, over.RoofTypes, t => t.Name);
            Merge(b.CeilingTypes, over.CeilingTypes, t => t.Name);
            Merge(b.Levels, over.Levels, l => l.Name);
            Merge(b.FamilyExpectations, over.FamilyExpectations, f => f.Category);
            return b;
        }

        private static void Merge<T>(List<T> baseList, List<T> overrides, Func<T, string> key)
        {
            if (overrides == null || overrides.Count == 0) return;
            foreach (var o in overrides)
            {
                if (o == null) continue;
                string k = key(o);
                if (string.IsNullOrWhiteSpace(k)) continue;
                int i = baseList.FindIndex(x => string.Equals(key(x), k, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) baseList[i] = o; else baseList.Add(o);
            }
        }

        private static ProjectBaseline ReadJson(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<ProjectBaseline>(File.ReadAllText(path));
            }
            catch (Exception ex) { StingLog.Warn($"BaselineRegistry read '{path}': {ex.Message}"); return null; }
        }
    }

    internal static class BaselineModelReader
    {
        public static ModelInventory Read(Document doc)
        {
            var inv = new ModelInventory();
            if (doc == null) return inv;

            foreach (var m in Collect<Material>(doc)) Add(inv.Materials, m.Name);
            foreach (var t in Collect<WallType>(doc)) { Add(inv.WallTypes, t.Name); NoteTiling(doc, inv, t.Name, t); }
            foreach (var t in Collect<FloorType>(doc)) { Add(inv.FloorTypes, t.Name); NoteTiling(doc, inv, t.Name, t); }
            foreach (var t in Collect<RoofType>(doc)) { Add(inv.RoofTypes, t.Name); NoteTiling(doc, inv, t.Name, t); }
            foreach (var t in Collect<CeilingType>(doc)) { Add(inv.CeilingTypes, t.Name); NoteTiling(doc, inv, t.Name, t); }
            foreach (var l in Collect<Level>(doc)) Add(inv.Levels, l.Name);

            foreach (var s in Collect<FamilySymbol>(doc))
            {
                string cat = s.Category?.Name;
                if (string.IsNullOrWhiteSpace(cat)) continue;
                if (!inv.FamilyTypesByCategory.TryGetValue(cat, out var list))
                    inv.FamilyTypesByCategory[cat] = list = new List<string>();
                list.Add(s.Name ?? "");
            }
            return inv;
        }

        private static IEnumerable<T> Collect<T>(Document doc) where T : Element
        {
            List<T> found;
            try { found = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().ToList(); }
            catch (Exception ex) { StingLog.Warn($"BaselineModelReader {typeof(T).Name}: {ex.Message}"); yield break; }
            foreach (var e in found) if (e != null) yield return e;
        }

        private static void Add(HashSet<string> set, string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) set.Add(name.Trim());
        }

        /// <summary>Reuses the SAME classifier the take-off uses, so the audit
        /// cannot disagree with the schedule about what counts as tiling.</summary>
        private static void NoteTiling(Document doc, ModelInventory inv, string name, HostObjAttributes hoa)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            bool tiled = false;
            try
            {
                var layers = hoa?.GetCompoundStructure()?.GetLayers();
                if (layers != null)
                    foreach (var l in layers)
                    {
                        if (l == null) continue;
                        if (l.Function != MaterialFunctionAssignment.Finish1
                         && l.Function != MaterialFunctionAssignment.Finish2) continue;
                        if (l.MaterialId == null || l.MaterialId == ElementId.InvalidElementId) continue;
                        var mat = doc.GetElement(l.MaterialId) as Material;
                        if (mat != null && FinishTextClassifier.IsTile(mat.Name)) { tiled = true; break; }
                    }
            }
            catch (Exception ex) { StingLog.Warn($"BaselineModelReader tiling '{name}': {ex.Message}"); }
            inv.HostTypeHasTiledFinish[name.Trim()] = tiled;
        }
    }

    internal sealed class MintResult
    {
        public int Created;
        public readonly List<string> Failed = new List<string>();

        public string Summary() => Failed.Count == 0
            ? $"Created {Created} item(s)."
            : $"Created {Created} item(s); {Failed.Count} could not be created:\n  • "
              + string.Join("\n  • ", Failed.Take(12))
              + (Failed.Count > 12 ? $"\n  • …and {Failed.Count - 12} more" : "");
    }

    internal static class BaselineMinter
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>Creates exactly the findings the auditor marked Missing.
        /// Caller owns the transaction.</summary>
        public static MintResult Apply(Document doc, ProjectBaseline baseline, BaselineAuditResult audit)
        {
            var r = new MintResult();
            var missing = new HashSet<string>(
                audit.Missing.Select(f => f.Group + "|" + f.Name), StringComparer.OrdinalIgnoreCase);

            // Materials first: the host types reference them by name.
            foreach (var m in baseline.Materials ?? new List<BaselineMaterial>())
                if (m != null && missing.Contains("Materials|" + m.Name?.Trim()))
                    Try(r, $"material '{m.Name}'", () => CreateMaterial(doc, m));

            var byName = MaterialIndex(doc);
            MintHosts<WallType>(doc, r, missing, "Wall types", baseline.WallTypes, byName);
            MintHosts<FloorType>(doc, r, missing, "Floor types", baseline.FloorTypes, byName);
            MintHosts<RoofType>(doc, r, missing, "Roof types", baseline.RoofTypes, byName);
            MintHosts<CeilingType>(doc, r, missing, "Ceiling types", baseline.CeilingTypes, byName);

            foreach (var l in baseline.Levels ?? new List<BaselineLevel>())
                if (l != null && missing.Contains("Levels|" + l.Name?.Trim()))
                    Try(r, $"level '{l.Name}'", () =>
                    {
                        var lvl = Level.Create(doc, l.ElevationMm * MmToFt);
                        if (lvl != null) lvl.Name = l.Name.Trim();
                    });

            return r;
        }

        private static void Try(MintResult r, string what, Action a)
        {
            try { a(); r.Created++; }
            catch (Exception ex)
            {
                r.Failed.Add($"{what}: {ex.Message}");
                StingLog.Warn($"BaselineMinter {what}: {ex.Message}");
            }
        }

        private static void CreateMaterial(Document doc, BaselineMaterial m)
        {
            var id = Material.Create(doc, m.Name.Trim());
            var mat = doc.GetElement(id) as Material;
            if (mat == null || string.IsNullOrWhiteSpace(m.Colour)) return;
            var parts = m.Colour.Split(',');
            if (parts.Length != 3) return;
            if (byte.TryParse(parts[0].Trim(), out byte cr)
             && byte.TryParse(parts[1].Trim(), out byte cg)
             && byte.TryParse(parts[2].Trim(), out byte cb))
                mat.Color = new Autodesk.Revit.DB.Color(cr, cg, cb);
        }

        private static Dictionary<string, ElementId> MaterialIndex(Document doc)
        {
            var d = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
                if (!string.IsNullOrWhiteSpace(m.Name) && !d.ContainsKey(m.Name.Trim()))
                    d[m.Name.Trim()] = m.Id;
            return d;
        }

        private static void MintHosts<T>(Document doc, MintResult r, HashSet<string> missing,
            string group, List<BaselineHostType> types, Dictionary<string, ElementId> materials)
            where T : HostObjAttributes
        {
            var wanted = (types ?? new List<BaselineHostType>())
                .Where(t => t != null && missing.Contains(group + "|" + t.Name?.Trim())).ToList();
            if (wanted.Count == 0) return;

            // A duplicable source: any existing type of this class that already
            // has a compound structure. Without one there is nothing to clone,
            // and that is reported rather than silently skipped.
            var source = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>()
                .FirstOrDefault(t => SafeStructure(t) != null);
            if (source == null)
            {
                foreach (var t in wanted)
                    r.Failed.Add($"{group} '{t.Name}': the model has no existing {typeof(T).Name} "
                               + "with a compound structure to duplicate from");
                return;
            }

            foreach (var t in wanted)
            {
                // NOT the shared Try() helper: minting a host type is two steps,
                // and the first real run failed on the SECOND one. Duplicate had
                // already committed, so the model kept five types named for the
                // baseline that carried the SOURCE type's layers — a floor called
                // "Ceramic Tiled" with no tile in it. Worse, the next audit reads
                // those as existing and refuses to touch them, so the failure
                // becomes permanent and silent.
                //
                // A half-made type is deleted. Either the type is what the
                // baseline describes or it is not there at all.
                HostObjAttributes created = null;
                try
                {
                    created = source.Duplicate(t.Name.Trim()) as HostObjAttributes;
                    if (created == null) throw new InvalidOperationException("Duplicate returned null");
                    created.SetCompoundStructure(BuildStructure(t, materials, SafeStructure(source)));
                    r.Created++;
                }
                catch (Exception ex)
                {
                    if (created != null)
                    {
                        try { doc.Delete(created.Id); }
                        catch (Exception delEx)
                        {
                            StingLog.Warn($"BaselineMinter rollback '{t.Name}': {delEx.Message}");
                            r.Failed.Add($"{group} '{t.Name}': {ex.Message} "
                                       + "— AND the half-made type could not be removed, so delete it by hand");
                            continue;
                        }
                    }
                    r.Failed.Add($"{group} '{t.Name}': {ex.Message}");
                    StingLog.Warn($"BaselineMinter {group} '{t.Name}': {ex.Message}");
                }
            }
        }

        private static CompoundStructure SafeStructure(HostObjAttributes t)
        {
            try { return t?.GetCompoundStructure(); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        /// <summary>
        /// <paramref name="template"/> is the SOURCE type's own structure, and it
        /// is what makes this work for floors and ceilings.
        ///
        /// CreateSimpleCompoundStructure returns a structure with a wall-shaped
        /// EndCapCondition. Revit accepts that on a WallType and rejects it
        /// everywhere else — "Input compound structure has wrong EndCap
        /// condition for this element type" — which is why the first real run
        /// created 12 materials, 4 walls and 3 roofs and failed on all 4 floor
        /// types and the ceiling. The floors are the ones carrying the tiled
        /// finish layer, so the one thing the baseline exists to fix was the one
        /// thing that did not get created.
        ///
        /// Editing the source's structure in place inherits every class-specific
        /// property already valid for that element type, rather than guessing
        /// which ones Revit will object to. GetCompoundStructure returns a copy,
        /// so the source type is untouched.
        /// </summary>
        private static CompoundStructure BuildStructure(BaselineHostType t,
                                                        Dictionary<string, ElementId> materials,
                                                        CompoundStructure template = null)
        {
            var layers = new List<CompoundStructureLayer>();
            foreach (var l in t.Layers)
            {
                materials.TryGetValue((l.Material ?? "").Trim(), out var matId);
                layers.Add(new CompoundStructureLayer(
                    l.ThicknessMm * MmToFt, ParseFunction(l.Function),
                    matId ?? ElementId.InvalidElementId));
            }

            CompoundStructure cs;
            if (template != null)
            {
                cs = template;
                cs.SetLayers(layers);
            }
            else
            {
                cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
            }

            // Shell layers are everything OUTSIDE the structural core. Revit
            // rejects a structure whose core is empty or discontiguous, so the
            // core is taken as the span between the first and last Structure
            // layer and the rest becomes shell.
            int first = t.Layers.FindIndex(l => IsCore(l.Function));
            int last = t.Layers.FindLastIndex(l => IsCore(l.Function));
            if (first >= 0 && last >= first)
            {
                cs.SetNumberOfShellLayers(ShellLayerType.Exterior, first);
                cs.SetNumberOfShellLayers(ShellLayerType.Interior, t.Layers.Count - 1 - last);
            }
            return cs;
        }

        private static bool IsCore(string function) =>
            string.Equals(function, "Structure", StringComparison.OrdinalIgnoreCase);

        private static MaterialFunctionAssignment ParseFunction(string f)
        {
            switch ((f ?? "").Trim().ToLowerInvariant())
            {
                case "finish1": return MaterialFunctionAssignment.Finish1;
                case "finish2": return MaterialFunctionAssignment.Finish2;
                case "substrate": return MaterialFunctionAssignment.Substrate;
                case "insulation": return MaterialFunctionAssignment.Insulation;
                case "membrane": return MaterialFunctionAssignment.Membrane;
                default: return MaterialFunctionAssignment.Structure;
            }
        }
    }

    internal static class BaselineDoc
    {
        /// <summary>
        /// The panel dispatcher calls Execute(null, ...) on purpose and expects
        /// CurrentApp as the fallback. A command that only reads
        /// commandData.Application works from a ribbon button and is silently
        /// dead from the dock panel — no exception, no dialog, a clean
        /// start/done in the log.
        /// </summary>
        public static Document Resolve(ExternalCommandData data)
            => (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)
               ?.ActiveUIDocument?.Document;
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class BaselineAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            // StingCommandHandler.RunCommand passes NULL for ExternalCommandData
            // and expects commands to fall back to CurrentApp. Reading
            // data.Application here returned null, so both commands returned
            // Cancelled instantly and showed nothing — a dead button that logged
            // a clean start/done and no error.
            var doc = BaselineDoc.Resolve(data);
            if (doc == null)
            {
                TaskDialog.Show("STING Project Baseline", "No active document.");
                return Result.Cancelled;
            }

            var audit = BaselineAuditor.Audit(BaselineRegistry.Load(doc), BaselineModelReader.Read(doc));
            TaskDialog.Show("STING Project Baseline — audit", BaselineAuditor.Report(audit));
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BaselineApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = BaselineDoc.Resolve(data);
            if (doc == null)
            {
                TaskDialog.Show("STING Project Baseline", "No active document.");
                return Result.Cancelled;
            }

            var baseline = BaselineRegistry.Load(doc);
            var audit = BaselineAuditor.Audit(baseline, BaselineModelReader.Read(doc));

            if (audit.BaselineProblems.Count > 0)
            {
                TaskDialog.Show("STING Project Baseline",
                    "The baseline itself has problems, so nothing was applied:\n\n  • "
                    + string.Join("\n  • ", audit.BaselineProblems));
                return Result.Cancelled;
            }
            if (audit.NothingToMint)
            {
                TaskDialog.Show("STING Project Baseline", BaselineAuditor.Report(audit));
                return Result.Succeeded;
            }

            // Audit first, write on confirm. Nothing reaches a live model
            // without the full list of what will change being read first.
            var dlg = new TaskDialog("STING Project Baseline — apply?")
            {
                MainInstruction = $"Create {audit.MissingCount} missing item(s)?",
                MainContent = BaselineAuditor.Report(audit),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (dlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            MintResult mint;
            using (var tx = new Transaction(doc, "STING Apply Project Baseline"))
            {
                tx.Start();
                mint = BaselineMinter.Apply(doc, baseline, audit);
                tx.Commit();
            }

            StingLog.Info($"BaselineApply: created {mint.Created}, failed {mint.Failed.Count}");
            TaskDialog.Show("STING Project Baseline", mint.Summary()
                + "\n\nRe-run the audit to confirm, then export a material schedule: "
                + "types carrying tiled finish layers are what make tiling measurable.");
            return Result.Succeeded;
        }
    }
}
