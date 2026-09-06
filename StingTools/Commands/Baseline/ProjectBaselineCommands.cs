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
            // Keyed on category+name: "STING 900x2100" may legitimately
            // exist for both a door and a window.
            Merge(b.FamilyTypes, over.FamilyTypes, t => t.Category + "|" + t.TypeName);
            Merge(b.FamilyParameters, over.FamilyParameters, f => f.Category);
            return b;
        }

        /// <summary>The names in the STING shared-parameter file, for layer 3's
        /// validation. Empty when the file cannot be read, which SKIPS the check
        /// rather than reporting every parameter as missing.</summary>
        public static ISet<string> SharedParameterNames()
            => Core.Baseline.SharedParameterNames.ParseFile(
                   StingToolsApp.FindDataFile("MR_PARAMETERS.txt"),
                   msg => StingLog.Warn($"BaselineRegistry: {msg}"));

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
        /// <summary><paramref name="baseline"/> is needed only to know WHICH
        /// type parameters to read back — see ReadDeclaredTypeParameters. Null
        /// is legal and simply skips that pass.</summary>
        public static ModelInventory Read(Document doc, ProjectBaseline baseline = null)
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

            // LAYER 2 — the FAMILIES loaded in each category. A type can only be
            // minted inside one that is already there, so this is what decides
            // Missing from Guidance.
            foreach (var f in Collect<Family>(doc))
            {
                string cat = f.FamilyCategory?.Name;
                if (string.IsNullOrWhiteSpace(cat) || string.IsNullOrWhiteSpace(f.Name)) continue;
                if (!inv.FamiliesByCategory.TryGetValue(cat, out var fams))
                    inv.FamiliesByCategory[cat] = fams = new List<string>();
                if (!fams.Contains(f.Name.Trim(), StringComparer.OrdinalIgnoreCase))
                    fams.Add(f.Name.Trim());
            }

            ReadDeclaredTypeParameters(doc, inv, baseline);
            ReadFamilyParameterState(doc, inv, baseline);
            return inv;
        }

        /// <summary>
        /// LAYER 3 — which loaded families already carry the declared parameters,
        /// and which cannot be edited at all.
        ///
        /// The parameter read is done on a TYPE of the family rather than by
        /// opening it: EditFamily is the expensive, model-mutating call this
        /// whole audit exists to happen BEFORE. A shared parameter added to a
        /// family shows on its symbols, so LookupParameter on one symbol answers
        /// the question at a fraction of the cost.
        /// </summary>
        private static void ReadFamilyParameterState(Document doc, ModelInventory inv,
                                                     ProjectBaseline baseline)
        {
            var sets = (baseline?.FamilyParameters ?? new List<BaselineFamilyParameterSet>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Category) && f.CleanParameters.Any())
                .ToList();
            if (sets.Count == 0) return;

            // One symbol per family is enough, and far cheaper than all of them.
            var oneSymbolPerFamily = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var sym in Collect<FamilySymbol>(doc))
            {
                string fam = sym.Family?.Name;
                if (string.IsNullOrWhiteSpace(fam)) continue;
                if (!oneSymbolPerFamily.ContainsKey(fam.Trim())) oneSymbolPerFamily[fam.Trim()] = sym;
            }

            foreach (var fam in Collect<Family>(doc))
            {
                string cat = fam.FamilyCategory?.Name;
                string name = fam.Name;
                if (string.IsNullOrWhiteSpace(cat) || string.IsNullOrWhiteSpace(name)) continue;

                var spec = sets.FirstOrDefault(f =>
                    string.Equals(f.Category.Trim(), cat, StringComparison.OrdinalIgnoreCase));
                if (spec == null) continue;

                // An in-place family has no .rfa behind it and cannot be edited
                // and reloaded. Reported, never attempted.
                bool editable = true;
                try { editable = fam.IsEditable && !fam.IsInPlace; }
                catch (Exception ex)
                {
                    StingLog.Warn($"BaselineModelReader editability '{name}': {ex.Message}");
                    editable = false;
                }
                if (!editable) { inv.UneditableFamilies.Add(name.Trim()); continue; }

                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (oneSymbolPerFamily.TryGetValue(name.Trim(), out var sym))
                    foreach (string wanted in spec.CleanParameters)
                    {
                        try { if (sym.LookupParameter(wanted) != null) have.Add(wanted); }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"BaselineModelReader param '{wanted}' on '{name}': {ex.Message}");
                        }
                    }
                inv.FamilyParameterNames[name.Trim()] = have;
            }
        }

        /// <summary>
        /// LAYER 2 — read back the parameters the baseline DECLARES, for the
        /// types it names, so a name match with different sizes is a conflict
        /// rather than a pass.
        ///
        /// Deliberately narrow: only the declared names, only on the declared
        /// types. Walking every parameter of every type in the model would cost
        /// far more and answer a question nobody asked.
        /// </summary>
        private static void ReadDeclaredTypeParameters(Document doc, ModelInventory inv,
                                                       ProjectBaseline baseline)
        {
            var wanted = (baseline?.FamilyTypes ?? new List<BaselineFamilyType>())
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Category)
                                      && !string.IsNullOrWhiteSpace(t.TypeName))
                .ToList();
            if (wanted.Count == 0) return;

            foreach (var sym in Collect<FamilySymbol>(doc))
            {
                string cat = sym.Category?.Name;
                if (string.IsNullOrWhiteSpace(cat) || string.IsNullOrWhiteSpace(sym.Name)) continue;

                var spec = wanted.FirstOrDefault(t =>
                    string.Equals(t.Category.Trim(), cat, StringComparison.OrdinalIgnoreCase)
                 && string.Equals(t.TypeName.Trim(), sym.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (spec == null) continue;

                var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var pd in spec.Parameters ?? new List<BaselineTypeParameter>())
                {
                    if (pd == null || string.IsNullOrWhiteSpace(pd.Name)) continue;
                    try
                    {
                        var p = sym.LookupParameter(pd.Name.Trim());
                        // An UNREADABLE parameter is not a difference. Recording
                        // 0 here would report every such type as a conflict at
                        // "0mm", which is a fabricated finding.
                        if (p == null || !p.HasValue || p.StorageType != StorageType.Double) continue;
                        values[pd.Name.Trim()] = p.AsDouble() * 304.8;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BaselineModelReader param '{pd.Name}' on '{sym.Name}': {ex.Message}");
                    }
                }
                if (values.Count > 0)
                    inv.FamilyTypeParametersMm[ModelInventory.TypeKey(cat, sym.Name)] = values;
            }
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

            MintFamilyTypes(doc, r, baseline, audit);

            foreach (var l in baseline.Levels ?? new List<BaselineLevel>())
                if (l != null && missing.Contains("Levels|" + l.Name?.Trim()))
                    Try(r, $"level '{l.Name}'", () =>
                    {
                        var lvl = Level.Create(doc, l.ElevationMm * MmToFt);
                        if (lvl != null) lvl.Name = l.Name.Trim();
                    });

            return r;
        }

        /// <summary>
        /// LAYER 2 — mint a TYPE inside a family that is already loaded.
        ///
        /// UNPROVEN. Nothing in this codebase duplicates a FamilySymbol; only
        /// TextNoteType and DimensionType (TemplateManagerCommands). Every type
        /// is therefore attempted and reported individually, the way #798 forced
        /// the host-type path to be.
        ///
        /// The rollback is the load-bearing part. Duplicate COMMITS before the
        /// parameter set runs, so a failed set leaves a baseline-NAMED type
        /// carrying the source type's sizes — which the next audit reads as
        /// existing and refuses to touch, making the failure permanent and
        /// invisible. Either the type is what the baseline describes or it is
        /// not there at all.
        ///
        /// Catalog-driven families refuse to duplicate. That is expected, not
        /// exceptional: it is reported per type and never crashes the run. An
        /// EXISTING vendor type is never edited — the auditor only ever marks
        /// absent types Missing.
        /// </summary>
        private static void MintFamilyTypes(Document doc, MintResult r,
                                            ProjectBaseline baseline, BaselineAuditResult audit)
        {
            var wanted = audit.Missing
                .Where(f => string.Equals(f.Group, BaselineAuditor.FamilyTypeGroup,
                                          StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (wanted.Count == 0) return;

            var specs = baseline.FamilyTypes ?? new List<BaselineFamilyType>();
            var symbolsByFamily = new Dictionary<string, List<FamilySymbol>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sym in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                                    .Cast<FamilySymbol>())
            {
                string fam = sym.Family?.Name;
                if (string.IsNullOrWhiteSpace(fam)) continue;
                if (!symbolsByFamily.TryGetValue(fam.Trim(), out var list))
                    symbolsByFamily[fam.Trim()] = list = new List<FamilySymbol>();
                list.Add(sym);
            }

            foreach (var finding in wanted)
            {
                // The spec is found by the finding's Name, which the auditor
                // composed as "Category / TypeName". Re-matching on the pattern
                // list here could pick a DIFFERENT family than the one the audit
                // told the user about.
                var spec = specs.FirstOrDefault(t => t != null
                    && string.Equals($"{t.Category?.Trim()} / {t.TypeName?.Trim()}", finding.Name,
                                     StringComparison.OrdinalIgnoreCase));
                if (spec == null)
                {
                    r.Failed.Add($"family type '{finding.Name}': no matching baseline entry");
                    continue;
                }

                if (!symbolsByFamily.TryGetValue((finding.HostFamily ?? "").Trim(), out var symbols)
                 || symbols.Count == 0)
                {
                    r.Failed.Add($"family type '{finding.Name}': family "
                               + $"'{finding.HostFamily}' has no loaded type to duplicate from");
                    continue;
                }

                FamilySymbol created = null;
                try
                {
                    created = symbols[0].Duplicate(spec.TypeName.Trim()) as FamilySymbol;
                    if (created == null) throw new InvalidOperationException("Duplicate returned null");

                    foreach (var pd in spec.Parameters ?? new List<BaselineTypeParameter>())
                    {
                        if (pd == null || string.IsNullOrWhiteSpace(pd.Name)) continue;
                        var p = created.LookupParameter(pd.Name.Trim());
                        // A parameter the family does not have is a FAILURE with
                        // the parameter named, not a silent skip: a type minted
                        // at the wrong size looks finished.
                        if (p == null)
                            throw new InvalidOperationException(
                                $"family '{finding.HostFamily}' has no parameter '{pd.Name.Trim()}'");
                        if (p.IsReadOnly)
                            throw new InvalidOperationException(
                                $"parameter '{pd.Name.Trim()}' is read-only in '{finding.HostFamily}'");
                        p.Set(pd.ValueMm * MmToFt);
                    }
                    r.Created++;
                }
                catch (Exception ex)
                {
                    if (created != null)
                    {
                        try { doc.Delete(created.Id); }
                        catch (Exception delEx)
                        {
                            StingLog.Warn($"BaselineMinter rollback '{finding.Name}': {delEx.Message}");
                            r.Failed.Add($"family type '{finding.Name}': {ex.Message} "
                                       + "— AND the half-made type could not be removed, so delete it by hand");
                            continue;
                        }
                    }
                    r.Failed.Add($"family type '{finding.Name}': {ex.Message}");
                    StingLog.Warn($"BaselineMinter family type '{finding.Name}': {ex.Message}");
                }
            }
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

    /// <summary>
    /// LAYER 3 — add SHARED parameters to loaded families.
    ///
    /// SHARED, not local. FamilyAugmentationEngine.AddTextParam takes the
    /// name/spec overload of FamilyManager.AddParameter, which creates a LOCAL
    /// family parameter: no GUID, no shared identity, and two families given
    /// "the same" one hold two unrelated parameters that cannot be scheduled
    /// together. This takes the ExternalDefinition overload instead — the
    /// definition-file path BatchAddFamilyParamsCommand already opens.
    ///
    /// And isInstance comes from the baseline, defaulting to FALSE: a door
    /// type's leaf material does not vary per instance.
    ///
    /// The parameters are created EMPTY. Deciding what a given type's leaf
    /// material IS remains a human declaration; inferring it from a type name
    /// is what PR #710 withdrew three rules for.
    ///
    /// UNPROVEN, like the layer-2 mint: every family is attempted individually
    /// and reported individually, and a family that cannot be edited is
    /// expected rather than exceptional.
    /// </summary>
    internal static class BaselineAugmenter
    {
        public static void Apply(Document doc, UIApplication app, ProjectBaseline baseline,
                                 BaselineAuditResult audit, MintResult r)
        {
            var work = audit.Augments.ToList();
            if (work.Count == 0) return;

            var sets = baseline.FamilyParameters ?? new List<BaselineFamilyParameterSet>();

            string originalSpf = null;
            DefinitionFile defFile = null;
            try
            {
                string spf = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                if (string.IsNullOrWhiteSpace(spf))
                {
                    r.Failed.Add("family parameters: MR_PARAMETERS.txt not found, so no SHARED "
                               + "parameter could be resolved. Nothing was added — a local "
                               + "parameter would have no shared identity and could not be scheduled.");
                    return;
                }
                originalSpf = app.Application.SharedParametersFilename;
                app.Application.SharedParametersFilename = spf;
                defFile = app.Application.OpenSharedParameterFile();
                if (defFile == null)
                {
                    r.Failed.Add("family parameters: the shared-parameter file could not be opened.");
                    return;
                }

                var defs = new Dictionary<string, ExternalDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (DefinitionGroup g in defFile.Groups)
                    foreach (ExternalDefinition d in g.Definitions)
                        if (!defs.ContainsKey(d.Name)) defs[d.Name] = d;

                var familiesByName = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase);
                foreach (var fam in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>())
                    if (!string.IsNullOrWhiteSpace(fam.Name) && !familiesByName.ContainsKey(fam.Name.Trim()))
                        familiesByName[fam.Name.Trim()] = fam;

                foreach (var finding in work)
                {
                    var spec = sets.FirstOrDefault(x => x != null
                        && string.Equals(x.Category?.Trim(), finding.Name, StringComparison.OrdinalIgnoreCase));
                    if (spec == null) { r.Failed.Add($"family parameters '{finding.Name}': no baseline entry"); continue; }

                    var wanted = new List<ExternalDefinition>();
                    foreach (string name in finding.Parameters ?? new List<string>())
                    {
                        if (defs.TryGetValue(name, out var d)) { wanted.Add(d); continue; }
                        // Validate() should have caught this before any model was
                        // touched. Reaching here means the baseline and the file
                        // disagree, and a LOCAL fallback is exactly what must not
                        // happen.
                        r.Failed.Add($"family parameters '{finding.Name}': '{name}' is not in the "
                                   + "shared-parameter file — skipped rather than added as a LOCAL "
                                   + "parameter, which could not be scheduled with its namesakes");
                    }
                    if (wanted.Count == 0) continue;

                    var group = ResolveGroup(spec.Group);
                    foreach (string famName in finding.Families ?? new List<string>())
                    {
                        if (!familiesByName.TryGetValue((famName ?? "").Trim(), out var fam))
                        {
                            r.Failed.Add($"family parameters: family '{famName}' is no longer loaded");
                            continue;
                        }
                        AugmentOne(doc, fam, wanted, group, spec.IsInstance, r);
                    }
                }
            }
            catch (Exception ex)
            {
                r.Failed.Add($"family parameters: {ex.Message}");
                StingLog.Warn($"BaselineAugmenter: {ex.Message}");
            }
            finally
            {
                try { if (originalSpf != null) app.Application.SharedParametersFilename = originalSpf; }
                catch (Exception ex) { StingLog.Warn($"BaselineAugmenter restore SPF: {ex.Message}"); }
            }
        }

        /// <summary>
        /// One family, one EditFamily/LoadFamily round trip.
        ///
        /// Idempotent: a parameter the family already has is skipped, so a
        /// re-run leaves it untouched. Families that cannot be edited — vendor-
        /// locked, workshared and owned elsewhere — are REPORTED, not crashed
        /// on; they are expected.
        /// </summary>
        private static void AugmentOne(Document doc, Family fam, List<ExternalDefinition> defs,
                                       ForgeTypeId group, bool isInstance, MintResult r)
        {
            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(fam);
                if (famDoc == null) throw new InvalidOperationException("EditFamily returned null");

                int added = 0;
                using (var tx = new Transaction(famDoc, "STING Baseline Family Parameters"))
                {
                    tx.Start();
                    var fm = famDoc.FamilyManager;
                    foreach (var d in defs)
                    {
                        if (fm.get_Parameter(d.Name) != null) continue;   // idempotent
                        fm.AddParameter(d, group, isInstance);
                        added++;
                    }
                    tx.Commit();
                }

                if (added > 0)
                {
                    famDoc.LoadFamily(doc, new AddOnlyLoadOptions());
                    r.Created += added;
                }
            }
            catch (Exception ex)
            {
                r.Failed.Add($"family '{fam?.Name}': {ex.Message}");
                StingLog.Warn($"BaselineAugmenter '{fam?.Name}': {ex.Message}");
            }
            finally
            {
                try { famDoc?.Close(false); }
                catch (Exception ex) { StingLog.Warn($"BaselineAugmenter close: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Reload the edited family, KEEPING the project's parameter values.
        ///
        /// FamilyAugmentationEngine's own options set overwriteParameterValues
        /// to TRUE. Layer 3 only ADDS empty parameters, so overwriting values is
        /// at best a no-op and at worst throws away something a user typed into
        /// a type between the audit and the apply. Additive means additive.
        /// </summary>
        private sealed class AddOnlyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = false; return true; }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family; overwriteParameterValues = false; return true;
            }
        }

        /// <summary>Group name → ForgeTypeId. An unknown name never reaches here:
        /// Validate() rejects it, so this cannot silently choose a default.</summary>
        private static ForgeTypeId ResolveGroup(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "construction": return GroupTypeId.Construction;
                case "materials":    return GroupTypeId.Materials;
                case "dimensions":   return GroupTypeId.Geometry;
                case "general":      return GroupTypeId.General;
                case "graphics":     return GroupTypeId.Graphics;
                case "data":         return GroupTypeId.Data;
                case "other":        return GroupTypeId.General;
                default:             return GroupTypeId.IdentityData;
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

            var baselineForAudit = BaselineRegistry.Load(doc);
            var audit = BaselineAuditor.Audit(baselineForAudit,
                                              BaselineModelReader.Read(doc, baselineForAudit),
                                              BaselineRegistry.SharedParameterNames());
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
            var audit = BaselineAuditor.Audit(baseline, BaselineModelReader.Read(doc, baseline),
                                              BaselineRegistry.SharedParameterNames());

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
                MainInstruction = audit.AugmentCount > 0
                    ? $"Create {audit.MissingCount} missing item(s) AND edit "
                      + $"{audit.FamiliesToAugment} famil(ies)?"
                    : $"Create {audit.MissingCount} missing item(s)?",
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

            // OUTSIDE the transaction: EditFamily cannot be called with one
            // open, and LoadFamily commits into the project itself. Its own
            // failures are collected into the same result so one report covers
            // the whole run.
            try
            {
                BaselineAugmenter.Apply(doc,
                    data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp,
                    baseline, audit, mint);
            }
            catch (Exception ex)
            {
                mint.Failed.Add($"family parameters: {ex.Message}");
                StingLog.Warn($"BaselineApply augment: {ex.Message}");
            }

            StingLog.Info($"BaselineApply: created {mint.Created}, failed {mint.Failed.Count}");
            TaskDialog.Show("STING Project Baseline", mint.Summary()
                + "\n\nRe-run the audit to confirm, then export a material schedule: "
                + "types carrying tiled finish layers are what make tiling measurable.");
            return Result.Succeeded;
        }
    }
}
