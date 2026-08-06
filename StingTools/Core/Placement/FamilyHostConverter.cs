// StingTools — Family Host Converter.
//
// Changes a Revit family's host / placement type (e.g. Ceiling-hosted →
// Face-based) and keeps it working across the project library. Pure engine
// (no UI / no TaskDialog) — the Placement Centre "Family Converter" tab drives
// it via RunInlineAction and reports results into the inline Report panel.
//
// Two conversion mechanisms (Family.FamilyPlacementType is READ-ONLY in the
// Revit API — a family's hosting is baked into the .rft template it was
// authored from), chosen per (source → target) pair:
//
//   P1 — Checkbox toggle (lossless): set FAMILY_WORK_PLANE_BASED on the
//        OwnerFamily inside the family doc, save, reload. Applies ONLY to
//        Unhosted level-based → Face-based. Geometry/params/connectors untouched.
//
//   P2 — Template rebuild (lossy, needs review): create a new family doc from
//        the target host template .rft, copy geometry + reference planes via
//        ElementTransformUtils.CopyElements, recreate family parameters/formulas/
//        types via FamilyManager, re-apply the original category, save a new
//        .rfa, reload. Host-driven sketch planes re-anchor to the template
//        default; MEP connectors do NOT survive CopyElements and are flagged
//        for manual review.
//
// Layered data model (mirrors MepSizingRegistry / DrawingTypeRegistry):
//   corporate baseline → Data/Placement/STING_FAMILY_HOST_TEMPLATES.json
//   project override   → <project>/_BIM_COORD/family_host_templates.json

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Placement
{
    // ── Data model ────────────────────────────────────────────────────────────

    /// <summary>A conversion target host kind, from STING_FAMILY_HOST_TEMPLATES.json.</summary>
    public sealed class FamilyHostTarget
    {
        public string Id { get; set; } = "";
        public string PlacementType { get; set; } = "";   // FamilyPlacementType name
        public string Label { get; set; } = "";
        public string TemplateMetric { get; set; } = "";
        public string TemplateImperial { get; set; } = "";
    }

    /// <summary>A (from → to) path rule. Path is "P1_Checkbox" or "P2_Rebuild".</summary>
    public sealed class FamilyHostPathRule
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Path { get; set; } = "";
    }

    /// <summary>Loaded, merged view of the corporate baseline + project override.</summary>
    public sealed class FamilyHostTemplates
    {
        public List<FamilyHostTarget> Targets { get; } = new();
        public List<FamilyHostPathRule> PathRules { get; } = new();

        public FamilyHostTarget ById(string id) =>
            Targets.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        public FamilyHostTarget ByLabel(string label) =>
            Targets.FirstOrDefault(t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Loader with corporate baseline + project override + per-doc cache.</summary>
    public static class FamilyHostTemplateRegistry
    {
        public const string DataFileName = "STING_FAMILY_HOST_TEMPLATES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/family_host_templates.json";

        private static readonly ConcurrentDictionary<string, FamilyHostTemplates> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        public static FamilyHostTemplates Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload() => _cache.Clear();

        private static FamilyHostTemplates Load(Document doc)
        {
            var tpl = new FamilyHostTemplates();
            try
            {
                string basePath = StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), tpl);

                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), tpl);
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("FamilyHostTemplateRegistry.Load", ex);
            }

            if (tpl.Targets.Count == 0) ApplyDefaults(tpl);
            return tpl;
        }

        // Merge: later source wins by id (project override replaces a corporate
        // target with the same id); pathRules are appended.
        private static void Apply(JObject j, FamilyHostTemplates tpl)
        {
            var targets = j["targets"] as JArray;
            if (targets != null)
            {
                foreach (var t in targets.OfType<JObject>())
                {
                    string id = (string)t["id"] ?? "";
                    if (string.IsNullOrEmpty(id)) continue;
                    tpl.Targets.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    tpl.Targets.Add(new FamilyHostTarget
                    {
                        Id = id,
                        PlacementType = (string)t["placementType"] ?? "",
                        Label = (string)t["label"] ?? id,
                        TemplateMetric = (string)t["templateMetric"] ?? "",
                        TemplateImperial = (string)t["templateImperial"] ?? ""
                    });
                }
            }

            var rules = j["pathRules"] as JArray;
            if (rules != null)
            {
                foreach (var r in rules.OfType<JObject>())
                {
                    tpl.PathRules.Add(new FamilyHostPathRule
                    {
                        From = (string)r["from"] ?? "",
                        To = (string)r["to"] ?? "",
                        Path = (string)r["path"] ?? ""
                    });
                }
            }
        }

        // Hard fallback if the data file is missing — mirrors the JSON baseline.
        private static void ApplyDefaults(FamilyHostTemplates tpl)
        {
            tpl.Targets.Clear();
            tpl.Targets.Add(new FamilyHostTarget { Id = "FaceBased", PlacementType = "WorkPlaneBased", Label = "Face-based (work plane)", TemplateMetric = "Metric Generic Model face based.rft", TemplateImperial = "Generic Model face based.rft" });
            tpl.Targets.Add(new FamilyHostTarget { Id = "Unhosted", PlacementType = "OneLevelBased", Label = "Unhosted (level-based)", TemplateMetric = "Metric Generic Model.rft", TemplateImperial = "Generic Model.rft" });
            tpl.Targets.Add(new FamilyHostTarget { Id = "WallBased", PlacementType = "OneLevelBasedHosted", Label = "Wall-based", TemplateMetric = "Metric Generic Model wall based.rft", TemplateImperial = "Generic Model wall based.rft" });
            tpl.Targets.Add(new FamilyHostTarget { Id = "CeilingBased", PlacementType = "OneLevelBasedHosted", Label = "Ceiling-based", TemplateMetric = "Metric Generic Model ceiling based.rft", TemplateImperial = "Generic Model ceiling based.rft" });
            tpl.Targets.Add(new FamilyHostTarget { Id = "FloorBased", PlacementType = "OneLevelBasedHosted", Label = "Floor-based", TemplateMetric = "Metric Generic Model floor based.rft", TemplateImperial = "Generic Model floor based.rft" });
            tpl.Targets.Add(new FamilyHostTarget { Id = "RoofBased", PlacementType = "OneLevelBasedHosted", Label = "Roof-based", TemplateMetric = "Metric Generic Model roof based.rft", TemplateImperial = "Generic Model roof based.rft" });
            tpl.PathRules.Add(new FamilyHostPathRule { From = "Unhosted", To = "FaceBased", Path = "P1_Checkbox" });
        }
    }

    // ── Request / result / info POCOs ─────────────────────────────────────────

    public sealed class FamilyHostConversionRequest
    {
        public ElementId FamilyId;
        public string TargetId;
        public bool RehostInstances = true;
        public bool AllowLossyRebuild = false;
    }

    /// <summary>
    /// Dry-run answer to "how much shared-parameter binding would a P2 rebuild
    /// cost me?", surfaced in Audit Only before anything is mutated.
    /// </summary>
    public sealed class SharedParamPreflight
    {
        public int FamiliesInspected;
        public int SharedParamsSeen;
        /// <summary>"family: parameter" for each shared param in neither file.</summary>
        public List<string> FallbackNames = new List<string>();
        public List<string> Warnings = new List<string>();
        /// <summary>Resolved path of STING's MR_PARAMETERS.txt ("" if not found).</summary>
        public string StingFile = "";
        /// <summary>The user's currently-pointed shared-parameter file.</summary>
        public string UserFile = "";

        public int WouldFallBack => FallbackNames.Count;
    }

    public sealed class FamilyHostConversionResult
    {
        public string FamilyName = "";
        public string FromPlacement = "";
        public string ToPlacement = "";
        public string PathUsed = "";        // "P1_Checkbox" | "P2_Rebuild" | "Skipped"
        public bool Success;
        public int InstancesRehosted;
        public int InstancesFailed;
        public List<string> Warnings = new();
        public List<string> Notes = new();
        public string NewRfaPath = "";
    }

    public sealed class FamilyHostInfo
    {
        public ElementId Id;
        public string Name = "";
        public string Category = "";
        public string CurrentPlacementType = "";
        public FamilyPlacementType Placement = FamilyPlacementType.Invalid;
        public string CurrentHostKind = "";
        public string SourceTargetId = "";       // canonical id used for path resolution
        public int InstanceCount;
        public bool Convertible;                  // false for non-model / unsupported
        public string BlockReason = "";
        public List<string> AllowedTargets = new(); // target LABELS (for the combo)
    }

    // ── Engine ────────────────────────────────────────────────────────────────

    public sealed class FamilyHostConverter
    {
        // Placement types the converter can operate on (point / host based model
        // families). Curve-driven, annotation, view-based, adaptive, and system
        // families are refused.
        private static readonly HashSet<FamilyPlacementType> SupportedSource = new()
        {
            FamilyPlacementType.OneLevelBased,
            FamilyPlacementType.OneLevelBasedHosted,
            FamilyPlacementType.WorkPlaneBased,
            FamilyPlacementType.TwoLevelsBased,
        };

        /// <summary>Read-only scan of all loaded families for the grid.</summary>
        public IReadOnlyList<FamilyHostInfo> ScanProjectFamilies(Document doc)
        {
            var list = new List<FamilyHostInfo>();
            if (doc == null) return list;

            var tpl = FamilyHostTemplateRegistry.Get(doc);

            // One pass over instances → per-family count + a sample host kind.
            var counts = new Dictionary<long, int>();
            var sampleHost = new Dictionary<long, string>();
            try
            {
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                {
                    var fam = fi.Symbol?.Family;
                    if (fam == null) continue;
                    long fid = fam.Id.Value;
                    counts.TryGetValue(fid, out int c);
                    counts[fid] = c + 1;
                    if (!sampleHost.ContainsKey(fid))
                    {
                        var host = fi.Host;
                        if (host != null)
                            sampleHost[fid] = host.Category?.Name ?? host.GetType().Name;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FamilyHostConverter.ScanProjectFamilies counts: {ex.Message}"); }

            foreach (var fam in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>())
            {
                try
                {
                    var info = BuildInfo(doc, tpl, fam, counts, sampleHost);
                    if (info != null) list.Add(info);
                }
                catch (Exception ex) { StingLog.Warn($"FamilyHostConverter scan '{fam?.Name}': {ex.Message}"); }
            }

            return list.OrderBy(i => i.Category).ThenBy(i => i.Name).ToList();
        }

        /// <summary>Load every .rfa under a folder (recursive) then rescan.</summary>
        public IReadOnlyList<FamilyHostInfo> ImportFolder(Document doc, string folder)
        {
            if (doc == null || string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return ScanProjectFamilies(doc);

            var opts = new StingTools.Tags.StingFamilyLoadOptions(true);
            int loaded = 0, failed = 0;
            foreach (var path in Directory.EnumerateFiles(folder, "*.rfa", SearchOption.AllDirectories))
            {
                try
                {
                    if (doc.LoadFamily(path, opts, out _)) loaded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    StingLog.Warn($"FamilyHostConverter.ImportFolder '{Path.GetFileName(path)}': {ex.Message}");
                }
            }
            StingLog.Info($"FamilyHostConverter.ImportFolder '{folder}': loaded {loaded}, failed {failed}");
            return ScanProjectFamilies(doc);
        }

        /// <summary>Load a single .rfa then rescan.</summary>
        public IReadOnlyList<FamilyHostInfo> ImportFile(Document doc, string rfaPath)
        {
            if (doc != null && !string.IsNullOrEmpty(rfaPath) && File.Exists(rfaPath))
            {
                try { doc.LoadFamily(rfaPath, new StingTools.Tags.StingFamilyLoadOptions(true), out _); }
                catch (Exception ex) { StingLog.Warn($"FamilyHostConverter.ImportFile '{rfaPath}': {ex.Message}"); }
            }
            return ScanProjectFamilies(doc);
        }

        private FamilyHostInfo BuildInfo(Document doc, FamilyHostTemplates tpl, Family fam,
            Dictionary<long, int> counts, Dictionary<long, string> sampleHost)
        {
            var info = new FamilyHostInfo
            {
                Id = fam.Id,
                Name = fam.Name,
                Category = fam.FamilyCategory?.Name ?? "(no category)",
            };
            counts.TryGetValue(fam.Id.Value, out int cnt);
            info.InstanceCount = cnt;

            FamilyPlacementType fpt;
            try { fpt = fam.FamilyPlacementType; }
            catch { fpt = FamilyPlacementType.Invalid; }
            info.CurrentPlacementType = fpt.ToString();
            info.Placement = fpt;

            // Non-model / unsupported → not convertible, empty combo.
            if (fam.IsInPlace)
            {
                info.Convertible = false;
                info.BlockReason = "in-place family";
                return info;
            }
            if (!SupportedSource.Contains(fpt))
            {
                info.Convertible = false;
                info.BlockReason = $"placement type {fpt} not supported (annotation / curve-driven / system)";
                return info;
            }

            // Friendly host kind.
            info.SourceTargetId = ResolveSourceId(fpt);
            if (sampleHost.TryGetValue(fam.Id.Value, out string hk))
                info.CurrentHostKind = hk;
            else if (fpt == FamilyPlacementType.OneLevelBasedHosted)
                info.CurrentHostKind = "hosted (no placed instance to sample)";
            else
                info.CurrentHostKind = "free-standing / level";

            bool categoryRebuildable = IsCategoryRebuildable(fam);

            info.Convertible = true;
            foreach (var t in tpl.Targets)
            {
                // Exclude the target that equals the resolved source (no-op).
                if (!string.IsNullOrEmpty(info.SourceTargetId) &&
                    string.Equals(t.Id, info.SourceTargetId, StringComparison.OrdinalIgnoreCase))
                    continue;

                // P2-only targets need a rebuildable category; P1 (Unhosted→Face)
                // does not (the checkbox toggle keeps the family's category).
                string path = ResolvePath(tpl, info.SourceTargetId, t.Id, fpt);
                if (path == "P2_Rebuild" && !categoryRebuildable)
                    continue;

                info.AllowedTargets.Add(t.Label);
            }
            if (info.AllowedTargets.Count == 0)
            {
                info.Convertible = false;
                info.BlockReason = "no compatible target host (category not in the STING model-family group)";
            }
            return info;
        }

        /// <summary>Resolve the canonical target id representing a source placement.</summary>
        public static string ResolveSourceId(FamilyPlacementType fpt)
        {
            switch (fpt)
            {
                case FamilyPlacementType.WorkPlaneBased: return "FaceBased";
                case FamilyPlacementType.OneLevelBased: return "Unhosted";
                default: return ""; // hosted / two-level — no single canonical id
            }
        }

        /// <summary>Pick the mechanism for a (source → target) pair.</summary>
        public static string ResolvePath(FamilyHostTemplates tpl, string sourceId, string targetId, FamilyPlacementType fpt)
        {
            var rule = tpl.PathRules.FirstOrDefault(r =>
                string.Equals(r.From, sourceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.To, targetId, StringComparison.OrdinalIgnoreCase));
            if (rule != null && string.Equals(rule.Path, "P1_Checkbox", StringComparison.OrdinalIgnoreCase)
                && fpt == FamilyPlacementType.OneLevelBased)
                return "P1_Checkbox";
            return "P2_Rebuild";
        }

        private static bool IsCategoryRebuildable(Family fam)
        {
            try
            {
                var cat = fam.FamilyCategory;
                if (cat == null) return false;
                var bic = (BuiltInCategory)cat.Id.Value;
                return StingTools.Tags.FamilyCategoryCompatibility.ModelFamilyGroup.Contains(bic);
            }
            catch { return false; }
        }

        // ── Convert ───────────────────────────────────────────────────────────

        public FamilyHostConversionResult Convert(Document doc, FamilyHostConversionRequest req)
        {
            var res = new FamilyHostConversionResult();
            if (doc == null || req == null || req.FamilyId == null)
            {
                res.Warnings.Add("Invalid request (no document or family).");
                res.PathUsed = "Skipped";
                return res;
            }

            var fam = doc.GetElement(req.FamilyId) as Family;
            if (fam == null)
            {
                res.Warnings.Add("Family not found in project.");
                res.PathUsed = "Skipped";
                return res;
            }
            res.FamilyName = fam.Name;

            FamilyPlacementType fpt;
            try { fpt = fam.FamilyPlacementType; }
            catch { fpt = FamilyPlacementType.Invalid; }
            res.FromPlacement = fpt.ToString();

            if (fam.IsInPlace || !SupportedSource.Contains(fpt))
            {
                res.PathUsed = "Skipped";
                res.Notes.Add("Non-model / unsupported family (in-place, annotation, curve-driven, or system) — cannot convert host type.");
                return res;
            }

            // §9 — workshared ownership: never mutate a family owned by another user.
            if (IsOwnedByOther(doc, fam, out string owner))
            {
                res.PathUsed = "Skipped";
                res.Notes.Add($"Skipped: family is checked out / owned by '{owner}' in this workshared model. Take ownership (or ask them to relinquish) then re-run.");
                return res;
            }

            var tpl = FamilyHostTemplateRegistry.Get(doc);
            var target = tpl.ById(req.TargetId) ?? tpl.ByLabel(req.TargetId);
            if (target == null)
            {
                res.PathUsed = "Skipped";
                res.Warnings.Add($"Target '{req.TargetId}' not found in the host-template matrix.");
                return res;
            }
            res.ToPlacement = target.PlacementType;

            string sourceId = ResolveSourceId(fpt);
            if (!string.IsNullOrEmpty(sourceId) &&
                string.Equals(sourceId, target.Id, StringComparison.OrdinalIgnoreCase))
            {
                res.PathUsed = "Skipped";
                res.Notes.Add("Family is already at the target placement — no change.");
                return res;
            }

            // §9 — flag nested usage (instances hosted inside another family
            // instance). We convert the top-level family only.
            if (HasNestedUsage(doc, fam))
                res.Notes.Add("NEXT STEP: this family also appears nested inside other family instances — only the top-level placements are converted; review the host families that nest it.");

            string path = ResolvePath(tpl, sourceId, target.Id, fpt);
            try
            {
                if (path == "P1_Checkbox")
                    return ConvertP1(doc, fam, target, res);
                return ConvertP2(doc, fam, target, req, res);
            }
            catch (Exception ex)
            {
                StingLog.Error($"FamilyHostConverter.Convert '{fam.Name}' → {target.Id}", ex);
                res.Success = false;
                res.Warnings.Add($"Conversion failed: {ex.Message}");
                if (string.IsNullOrEmpty(res.PathUsed)) res.PathUsed = path;
                return res;
            }
        }

        // ── P1 — lossless checkbox toggle (Unhosted → Face-based) ───────────────

        private FamilyHostConversionResult ConvertP1(Document doc, Family fam, FamilyHostTarget target,
            FamilyHostConversionResult res)
        {
            res.PathUsed = "P1_Checkbox";
            Document fdoc = null;
            try
            {
                fdoc = doc.EditFamily(fam);
                if (fdoc == null || !fdoc.IsFamilyDocument)
                {
                    res.Warnings.Add("EditFamily returned no family document.");
                    return res;
                }

                bool set = false;
                using (var t = new Transaction(fdoc, "STING Set WorkPlaneBased"))
                {
                    t.Start();
                    // Revit 2025's enum member is FAMILY_WORK_PLANE_BASED (the
                    // spec's FAMILY_WORK_PLANE_BASED_PARAM does not exist in this API).
                    Parameter p = fdoc.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED);
                    if (p != null && !p.IsReadOnly)
                    {
                        p.Set(1);
                        set = true;
                    }
                    t.Commit();
                }

                if (!set)
                {
                    res.Warnings.Add("Work-plane-based parameter not writable on this family — cannot toggle losslessly.");
                    return res;
                }

                var reloaded = fdoc.LoadFamily(doc, new StingTools.Tags.StingFamilyLoadOptions(true));
                res.Success = reloaded != null;
                if (res.Success)
                    res.Notes.Add("Lossless: placement widened to Face-based (work plane). Existing instances survive — no rehost needed.");
                else
                    res.Warnings.Add("LoadFamily returned null — the toggled family did not reload.");
                StingLog.Info($"FamilyHostConverter P1 '{fam.Name}': {(res.Success ? "ok" : "reload-failed")}");
                return res;
            }
            finally
            {
                try { fdoc?.Close(false); } catch (Exception ex) { StingLog.Warn($"P1 close: {ex.Message}"); }
            }
        }

        // ── P2 — template rebuild (lossy) ───────────────────────────────────────

        private FamilyHostConversionResult ConvertP2(Document doc, Family fam, FamilyHostTarget target,
            FamilyHostConversionRequest req, FamilyHostConversionResult res)
        {
            res.PathUsed = "P2_Rebuild";

            if (!req.AllowLossyRebuild)
            {
                res.PathUsed = "Skipped";
                res.Notes.Add("Requires a template rebuild (lossy) — enable 'Allow lossy rebuild (P2)' in the toolbar, then re-run.");
                return res;
            }

            // Resolve category up-front (must be rebuildable).
            var cat = fam.FamilyCategory;
            BuiltInCategory originalBic = cat != null ? (BuiltInCategory)cat.Id.Value : BuiltInCategory.OST_GenericModel;
            if (!StingTools.Tags.FamilyCategoryCompatibility.ModelFamilyGroup.Contains(originalBic))
            {
                res.PathUsed = "Skipped";
                res.Notes.Add($"Category '{cat?.Name}' is not in the STING interchangeable model-family group — template rebuild is not supported for it.");
                return res;
            }

            Application app = doc.Application;
            bool metric = doc.DisplayUnitSystem == DisplayUnit.METRIC;
            string templateFile = metric ? target.TemplateMetric : target.TemplateImperial;
            string rftPath = ResolveTemplatePath(app, templateFile);
            if (string.IsNullOrEmpty(rftPath) || !File.Exists(rftPath))
            {
                res.PathUsed = "Skipped";
                res.Warnings.Add($"Host template '{templateFile}' not found (resolved: '{rftPath}'). Check the Revit family-template library install.");
                return res;
            }

            // Snapshot instances first so we can attempt a rehost / free re-place
            // after the family definition is replaced.
            var snapshots = new List<StingTools.Tags.InstanceRehostSnapshot>();
            try
            {
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(f => f.Symbol?.Family?.Id == fam.Id))
                {
                    var snap = StingTools.Tags.InstanceRehostSnapshot.Take(fi);
                    if (snap != null) snapshots.Add(snap);
                }
            }
            catch (Exception ex) { StingLog.Warn($"P2 snapshot: {ex.Message}"); }

            Document src = null, tgt = null;
            try
            {
                src = doc.EditFamily(fam);
                if (src == null || !src.IsFamilyDocument)
                {
                    res.Warnings.Add("EditFamily returned no source family document.");
                    return res;
                }

                tgt = app.NewFamilyDocument(rftPath);
                if (tgt == null)
                {
                    res.Warnings.Add("NewFamilyDocument returned null for the target template.");
                    return res;
                }

                // 1. Copy geometry + reference planes.
                var geomIds = new FilteredElementCollector(src)
                    .WhereElementIsNotElementType()
                    .Where(IsCopyableGeometry)
                    .Select(e => e.Id).ToList();
                int copied = 0;
                if (geomIds.Count > 0)
                {
                    using var t = new Transaction(tgt, "STING Copy Geometry");
                    t.Start();
                    try
                    {
                        var newIds = ElementTransformUtils.CopyElements(
                            src, geomIds, tgt, Transform.Identity, new CopyPasteOptions());
                        copied = newIds?.Count ?? 0;
                    }
                    catch (Exception ex)
                    {
                        res.Warnings.Add($"Some geometry failed to copy: {ex.Message}");
                        StingLog.Warn($"P2 CopyElements '{fam.Name}': {ex.Message}");
                    }
                    t.Commit();
                }
                res.Notes.Add($"Copied {copied}/{geomIds.Count} geometry/reference elements from the source family.");

                // 2. Recreate family parameters, formulas, types.
                CopyFamilyParameters(src, tgt, res);

                // 3. Connectors do NOT survive CopyElements — transfer them.
                //    Tier 1 re-mints a STING seed family's connectors from its
                //    JSON spec; Tier 2 harvests and re-creates for any other
                //    family. Anything that still cannot be placed is reported
                //    with full coordinates rather than silently dropped.
                TransferConnectors(src, tgt, fam.Name, res);

                // 4. Re-apply the original category.
                using (var t = new Transaction(tgt, "STING Category"))
                {
                    t.Start();
                    try
                    {
                        Category newCat = tgt.Settings.Categories.get_Item(originalBic);
                        if (newCat != null) tgt.OwnerFamily.FamilyCategory = newCat;
                    }
                    catch (Exception ex)
                    {
                        res.Warnings.Add($"Could not re-apply category '{cat?.Name}': {ex.Message}");
                    }
                    t.Commit();
                }

                // 5. Save the new .rfa, then close the source before reloading.
                string outDir = ResolveConvertedDir(doc);
                Directory.CreateDirectory(outDir);
                string newRfa = Path.Combine(outDir, $"{Sanitize(fam.Name)}__{target.Id}.rfa");
                tgt.SaveAs(newRfa, new SaveAsOptions { OverwriteExistingFile = true });
                res.NewRfaPath = newRfa;

                try { src.Close(false); src = null; }
                catch (Exception ex) { StingLog.Warn($"P2 close src: {ex.Message}"); }

                var reloaded = tgt.LoadFamily(doc, new StingTools.Tags.StingFamilyLoadOptions(true));
                res.Success = reloaded != null;
                if (!res.Success)
                {
                    res.Warnings.Add("LoadFamily returned null — the rebuilt family did not reload into the project.");
                    return res;
                }
                res.Notes.Add($"Rebuilt family saved to {newRfa} and reloaded. Host-relative geometry re-anchored to the template default — REVIEW sketch planes / hosting.");

                // 6. Rehost / re-place instances (best-effort, non-interactive).
                if (req.RehostInstances && snapshots.Count > 0)
                    RehostInstances(doc, reloaded ?? fam, target, snapshots, res);

                StingLog.Info($"FamilyHostConverter P2 '{fam.Name}' → {target.Id}: ok, {res.InstancesRehosted} re-placed, {res.InstancesFailed} manual");
                return res;
            }
            finally
            {
                try { src?.Close(false); } catch (Exception ex) { StingLog.Warn($"P2 finally close src: {ex.Message}"); }
                try { tgt?.Close(false); } catch (Exception ex) { StingLog.Warn($"P2 finally close tgt: {ex.Message}"); }
            }
        }

        // Re-place instances that lost their host after the family definition was
        // swapped. We cannot pick faces / hosts non-interactively, so:
        //   - Face-based / Unhosted target → recreate free-standing at the snapshot
        //     location on the snapshot level (WorkPlaneBased / OneLevelBased accept it).
        //   - Hosted target (wall/ceiling/floor/roof) → cannot auto-pick a host;
        //     count as failed and flag for manual rehost.
        // Instances Revit already reconciled in place on reload are left untouched.
        private void RehostInstances(Document doc, Family reloaded, FamilyHostTarget target,
            List<StingTools.Tags.InstanceRehostSnapshot> snapshots, FamilyHostConversionResult res)
        {
            bool nonHostedTarget = string.Equals(target.PlacementType, "WorkPlaneBased", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(target.PlacementType, "OneLevelBased", StringComparison.OrdinalIgnoreCase);

            using var tg = new TransactionGroup(doc, "STING Family Convert — rehost");
            tg.Start();
            foreach (var snap in snapshots)
            {
                try
                {
                    // If the original instance still exists and is valid, Revit
                    // reconciled it on reload — leave it.
                    var existing = doc.GetElement(snap.OriginalId) as FamilyInstance;
                    if (existing != null && existing.IsValidObject && existing.Location != null)
                        continue;

                    if (!nonHostedTarget)
                    {
                        res.InstancesFailed++;
                        continue;
                    }

                    // Re-resolve the type from the RELOADED family by name — the
                    // snapshot's Symbol reference was captured before the family was
                    // rebuilt + reloaded and may point at a stale / replaced element.
                    FamilySymbol sym = ResolveReloadedSymbol(doc, reloaded, snap.TypeName) ?? snap.Symbol;
                    if (sym == null) { res.InstancesFailed++; continue; }

                    using var t = new Transaction(doc, "STING Re-place instance");
                    t.Start();
                    if (!sym.IsActive) sym.Activate();
                    FamilyInstance rep = snap.Level != null
                        ? doc.Create.NewFamilyInstance(snap.LocationPoint, sym, snap.Level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural)
                        : doc.Create.NewFamilyInstance(snap.LocationPoint, sym, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    if (rep != null)
                    {
                        StingTools.Tags.FamilyQuickEditHelpers.RestoreInstanceParams(rep, snap.Params);
                        res.InstancesRehosted++;
                        t.Commit();
                    }
                    else
                    {
                        t.RollBack();
                        res.InstancesFailed++;
                    }
                }
                catch (Exception ex)
                {
                    res.InstancesFailed++;
                    StingLog.Warn($"P2 rehost instance {snap?.OriginalId?.Value}: {ex.Message}");
                }
            }
            tg.Assimilate();

            if (res.InstancesFailed > 0)
            {
                if (!nonHostedTarget)
                    // Free-standing instances cannot survive a switch to a hosted
                    // placement — Revit deleted them on reload. They cannot be
                    // rehosted (there is nothing left to rehost); they must be
                    // re-placed from scratch on the new hosted family.
                    res.Notes.Add($"NEXT STEP: {res.InstancesFailed} placed instance(s) were DELETED when the family switched to a hosted placement — a free-standing instance cannot exist without a host. They must be RE-PLACED from scratch on the new family (Change Host cannot recover a deleted instance).");
                else
                    res.Notes.Add($"NEXT STEP: {res.InstancesFailed} instance(s) could not be re-placed automatically — re-place them manually.");
            }
        }

        // Re-resolve a family type from the reloaded family by name. The snapshot's
        // Symbol reference was captured before the family was rebuilt + reloaded and
        // may point at a stale / replaced element after LoadFamily.
        private static FamilySymbol ResolveReloadedSymbol(Document doc, Family reloaded, string typeName)
        {
            if (doc == null || reloaded == null || string.IsNullOrEmpty(typeName)) return null;
            try
            {
                foreach (var id in reloaded.GetFamilySymbolIds())
                {
                    if (doc.GetElement(id) is FamilySymbol fs &&
                        string.Equals(fs.Name, typeName, StringComparison.Ordinal))
                        return fs;
                }
            }
            catch (Exception ex) { StingLog.Warn($"P2 re-resolve symbol '{typeName}': {ex.Message}"); }
            return null;
        }

        // ── P2 helpers ──────────────────────────────────────────────────────────

        private static readonly HashSet<string> _defaultRefPlaneNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Center (Front/Back)", "Center (Left/Right)", "Center(Front/Back)", "Center(Left/Right)", "Reference"
        };

        private static bool IsCopyableGeometry(Element e)
        {
            if (e is GenericForm) return true;             // extrusion/blend/revolve/sweep/swept-blend
            if (e is CurveElement) return true;            // model line/curve, symbolic curve, detail curve
            if (e is ReferencePlane rp)
                return !_defaultRefPlaneNames.Contains(rp.Name ?? "");
            return false;
        }

        // Recreate every non-built-in family parameter (shared by GUID where the
        // shared-parameter file has it, family by name/type otherwise), then
        // formulas, then types + current-type values. Every step try/caught so a
        // single bad parameter never aborts the rebuild.
        private void CopyFamilyParameters(Document src, Document tgt, FamilyHostConversionResult res)
        {
            FamilyManager sm = src.FamilyManager, tm = tgt.FamilyManager;
            Application app = src.Application;

            var created = new Dictionary<string, FamilyParameter>(StringComparer.Ordinal);
            var srcByName = new Dictionary<string, FamilyParameter>(StringComparer.Ordinal);
            var pendingShared = new List<FamilyParameter>();
            var pendingFamily = new List<FamilyParameter>();

            // Triage first so the shared-parameter passes below can pin one file
            // at a time (an ExternalDefinition is only reliably usable while its
            // own file is the application's current shared-parameter file).
            foreach (FamilyParameter fp in sm.Parameters)
            {
                string name = fp?.Definition?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                srcByName[name] = fp;

                // Skip params the target template already carries (built-ins).
                FamilyParameter existing = null;
                try { existing = tm.get_Parameter(name); } catch { }
                if (existing != null) { created[name] = existing; continue; }

                if (fp.IsShared) pendingShared.Add(fp); else pendingFamily.Add(fp);
            }

            // The user's shared-parameter file is whatever they currently have
            // pointed. It is restored in the finally below no matter what.
            string prevSp = null;
            try { prevSp = app.SharedParametersFilename; }
            catch (Exception ex) { StingLog.Warn($"P2 read SharedParametersFilename: {ex.Message}"); }

            string stingSp = ResolveStingSharedParamFile();
            int addedShared = 0, addedFamily = 0, sharedFallback = 0;
            var searched = new List<string>();

            try
            {
                using var t = new Transaction(tgt, "STING Copy Params");
                t.Start();

                // Pass 1 — STING's own MR_PARAMETERS.txt, pinned. Without this a
                // user pointed at some other shared-parameter file would silently
                // degrade every ASS_TAG_* / token / container param to a plain
                // family parameter, quietly breaking tags, schedules, ExLink and
                // COBie on the converted family.
                if (!string.IsNullOrEmpty(stingSp))
                {
                    searched.Add("STING MR_PARAMETERS.txt");
                    addedShared += AddSharedParamsFrom(app, stingSp, tm, pendingShared, created, res);
                }
                else
                {
                    res.Warnings.Add("STING's MR_PARAMETERS.txt could not be located — STING shared parameters " +
                                     "may degrade to plain family parameters on the rebuilt family.");
                }

                // Pass 2 — the user's original file, so vendor shared parameters
                // that exist only there still resolve by GUID.
                if (pendingShared.Count > 0 && !string.IsNullOrEmpty(prevSp)
                    && File.Exists(prevSp) && !PathEquals(prevSp, stingSp))
                {
                    searched.Add("the project's own shared-parameter file");
                    addedShared += AddSharedParamsFrom(app, prevSp, tm, pendingShared, created, res);
                }

                // Pass 3 — anything in NEITHER file falls back to a family
                // parameter (shared binding lost), plus the plain family params.
                foreach (var fp in pendingShared)
                {
                    if (AddPlainFamilyParameter(tm, fp, created, res)) { addedFamily++; sharedFallback++; }
                }
                foreach (var fp in pendingFamily)
                {
                    if (AddPlainFamilyParameter(tm, fp, created, res)) addedFamily++;
                }

                // Formulas — only after every parameter exists.
                foreach (var kv in created)
                {
                    if (!srcByName.TryGetValue(kv.Key, out var sfp)) continue;
                    string formula = null;
                    try { formula = sfp.Formula; } catch { }
                    if (string.IsNullOrEmpty(formula)) continue;
                    try
                    {
                        if (!kv.Value.IsDeterminedByFormula && kv.Value.IsReadOnly == false)
                            tm.SetFormula(kv.Value, formula);
                    }
                    catch (Exception ex)
                    {
                        res.Warnings.Add($"Formula on '{kv.Key}' not copied: {ex.Message}");
                        StingLog.Warn($"P2 SetFormula '{kv.Key}': {ex.Message}");
                    }
                }

                CopyTypes(sm, tm, srcByName, created, res);
                t.Commit();
            }
            finally
            {
                // ALWAYS restore, even on throw — leaving the user pointed at
                // STING's file would silently change behaviour everywhere else
                // in their session.
                try { if (prevSp != null) app.SharedParametersFilename = prevSp; }
                catch (Exception ex) { StingLog.Warn($"P2 restore SharedParametersFilename: {ex.Message}"); }
            }

            string where = searched.Count > 0 ? string.Join(" then ", searched) : "no shared-parameter file";
            res.Notes.Add($"Parameters: {addedShared} shared + {addedFamily} family recreated (searched {where})" +
                          (sharedFallback > 0
                              ? $" — WARNING: {sharedFallback} shared param(s) were in neither file and fell back to plain " +
                                "family parameters; their shared binding is lost, so tags / schedules / ExLink / COBie " +
                                "that rely on them will not read on this family until it is re-bound."
                              : ""));
        }

        /// <summary>
        /// Pin <paramref name="spFile"/> as the application's shared-parameter
        /// file and add every still-pending shared parameter whose GUID it
        /// defines. Successfully added parameters are removed from
        /// <paramref name="pending"/>. Returns the number added.
        /// </summary>
        private static int AddSharedParamsFrom(Application app, string spFile, FamilyManager tm,
            List<FamilyParameter> pending, Dictionary<string, FamilyParameter> created,
            FamilyHostConversionResult res)
        {
            if (pending.Count == 0 || string.IsNullOrEmpty(spFile)) return 0;

            DefinitionFile df = null;
            try
            {
                app.SharedParametersFilename = spFile;
                df = app.OpenSharedParameterFile();
            }
            catch (Exception ex)
            {
                res.Warnings.Add($"Shared-parameter file '{Path.GetFileName(spFile)}' could not be opened: {ex.Message}");
                StingLog.Warn($"P2 open shared param file '{spFile}': {ex.Message}");
                return 0;
            }
            if (df == null) return 0;

            int added = 0;
            var done = new List<FamilyParameter>();
            foreach (var fp in pending)
            {
                string name = fp?.Definition?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    ExternalDefinition ext = FindExternalDefinition(df, fp.GUID);
                    if (ext == null) continue;
                    var np = tm.AddParameter(ext, fp.Definition.GetGroupTypeId(), fp.IsInstance);
                    if (np != null) { created[name] = np; added++; done.Add(fp); }
                }
                catch (Exception ex)
                {
                    res.Warnings.Add($"Shared parameter '{name}' not recreated: {ex.Message}");
                    StingLog.Warn($"P2 AddParameter(shared) '{name}': {ex.Message}");
                }
            }
            foreach (var d in done) pending.Remove(d);
            return added;
        }

        /// <summary>
        /// Add a source parameter as a plain (non-shared) family parameter.
        /// Returns true when it was created.
        /// </summary>
        private static bool AddPlainFamilyParameter(FamilyManager tm, FamilyParameter fp,
            Dictionary<string, FamilyParameter> created, FamilyHostConversionResult res)
        {
            string name = fp?.Definition?.Name;
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                var np = tm.AddParameter(name, fp.Definition.GetGroupTypeId(),
                                         fp.Definition.GetDataType(), fp.IsInstance);
                if (np == null) return false;
                created[name] = np;
                return true;
            }
            catch (Exception ex)
            {
                res.Warnings.Add($"Parameter '{name}' not recreated: {ex.Message}");
                StingLog.Warn($"P2 AddParameter '{name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Dry-run check of how many shared parameters would lose their shared
        /// binding on a P2 rebuild, so the loss is a decision the user makes
        /// up-front rather than something they discover afterwards. Read-only:
        /// opens each family document and closes it without saving.
        /// </summary>
        public SharedParamPreflight PreflightSharedParameters(Document doc, IEnumerable<ElementId> familyIds)
        {
            var pf = new SharedParamPreflight();
            if (doc == null || familyIds == null) return pf;

            Application app = doc.Application;
            string prevSp = null;
            try { prevSp = app.SharedParametersFilename; }
            catch (Exception ex) { StingLog.Warn($"Preflight read SharedParametersFilename: {ex.Message}"); }

            // GUIDs are plain values, so collecting them up-front sidesteps any
            // question of an ExternalDefinition outliving its pinned file.
            string stingSp = ResolveStingSharedParamFile();
            pf.StingFile = stingSp;
            pf.UserFile = prevSp ?? "";

            var known = new HashSet<Guid>();
            try
            {
                if (!string.IsNullOrEmpty(stingSp)) CollectGuids(app, stingSp, known, pf);
                else pf.Warnings.Add("STING's MR_PARAMETERS.txt could not be located.");

                if (!string.IsNullOrEmpty(prevSp) && File.Exists(prevSp) && !PathEquals(prevSp, stingSp))
                    CollectGuids(app, prevSp, known, pf);
            }
            finally
            {
                try { if (prevSp != null) app.SharedParametersFilename = prevSp; }
                catch (Exception ex) { StingLog.Warn($"Preflight restore SharedParametersFilename: {ex.Message}"); }
            }

            foreach (var id in familyIds)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                if (!(doc.GetElement(id) is Family fam)) continue;

                Document fdoc = null;
                try
                {
                    fdoc = doc.EditFamily(fam);
                    if (fdoc == null || !fdoc.IsFamilyDocument) continue;
                    pf.FamiliesInspected++;

                    foreach (FamilyParameter fp in fdoc.FamilyManager.Parameters)
                    {
                        if (fp == null || !fp.IsShared) continue;
                        pf.SharedParamsSeen++;
                        Guid g;
                        try { g = fp.GUID; } catch { continue; }
                        if (known.Contains(g)) continue;
                        string nm = fp.Definition?.Name ?? g.ToString();
                        pf.FallbackNames.Add($"{fam.Name}: {nm}");
                    }
                }
                catch (Exception ex)
                {
                    pf.Warnings.Add($"'{fam.Name}' could not be inspected: {ex.Message}");
                    StingLog.Warn($"Preflight EditFamily '{fam.Name}': {ex.Message}");
                }
                finally
                {
                    try { fdoc?.Close(false); }
                    catch (Exception ex) { StingLog.Warn($"Preflight close '{fam.Name}': {ex.Message}"); }
                }
            }
            return pf;
        }

        private static void CollectGuids(Application app, string spFile, HashSet<Guid> into,
            SharedParamPreflight pf)
        {
            try
            {
                app.SharedParametersFilename = spFile;
                DefinitionFile df = app.OpenSharedParameterFile();
                if (df == null) return;
                foreach (DefinitionGroup g in df.Groups)
                    foreach (Definition d in g.Definitions)
                        if (d is ExternalDefinition ed) into.Add(ed.GUID);
            }
            catch (Exception ex)
            {
                pf.Warnings.Add($"Shared-parameter file '{Path.GetFileName(spFile)}' could not be read: {ex.Message}");
                StingLog.Warn($"Preflight CollectGuids '{spFile}': {ex.Message}");
            }
        }

        /// <summary>Locate STING's own MR_PARAMETERS.txt, or "" when unavailable.</summary>
        private static string ResolveStingSharedParamFile()
        {
            try
            {
                string p = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveStingSharedParamFile: {ex.Message}"); }
            return "";
        }

        private static bool PathEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private void CopyTypes(FamilyManager sm, FamilyManager tm,
            Dictionary<string, FamilyParameter> srcByName,
            Dictionary<string, FamilyParameter> created, FamilyHostConversionResult res)
        {
            FamilyTypeSet srcTypes = sm.Types;
            if (srcTypes == null || srcTypes.Size == 0) return;

            int skippedElemId = 0;
            foreach (FamilyType st in srcTypes)
            {
                if (st == null || string.IsNullOrEmpty(st.Name)) continue;
                FamilyType nt;
                try { nt = tm.NewType(st.Name); }
                catch (Exception ex) { StingLog.Warn($"P2 NewType '{st.Name}': {ex.Message}"); continue; }
                tm.CurrentType = nt;

                foreach (var kv in created)
                {
                    if (!srcByName.TryGetValue(kv.Key, out var sfp)) continue;
                    var tfp = kv.Value;
                    if (tfp.IsReadOnly || tfp.IsDeterminedByFormula) continue;
                    try
                    {
                        switch (sfp.StorageType)
                        {
                            case StorageType.Double:
                                var d = st.AsDouble(sfp); if (d.HasValue) tm.Set(tfp, d.Value); break;
                            case StorageType.Integer:
                                var i = st.AsInteger(sfp); if (i.HasValue) tm.Set(tfp, i.Value); break;
                            case StorageType.String:
                                var s = st.AsString(sfp); if (s != null) tm.Set(tfp, s); break;
                            case StorageType.ElementId:
                                skippedElemId++; break; // element ids don't map across docs — leave default
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"P2 type value '{st.Name}.{kv.Key}': {ex.Message}"); }
                }
            }
            if (skippedElemId > 0)
                res.Notes.Add($"NEXT STEP: {skippedElemId} element-id parameter value(s) (materials / nested types) were not carried across — set them in the rebuilt family.");
        }

        /// <summary>
        /// Run the two-tier connector transfer and fold real per-connector
        /// accounting into the result. Replaces the previous blanket
        /// "connectors were NOT transferred" note — only connectors actually
        /// re-created are claimed, and every unplaced one is listed with its
        /// exact origin / normal / domain / shape / size.
        /// </summary>
        private void TransferConnectors(Document src, Document tgt, string familyName,
            FamilyHostConversionResult res)
        {
            ConnectorTransferResult ct;
            try
            {
                ct = FamilyConnectorTransfer.Transfer(src, tgt, familyName);
            }
            catch (Exception ex)
            {
                res.Warnings.Add($"Connector transfer failed outright: {ex.Message}. " +
                                 "The rebuilt family has NO connectors — re-add them in the family editor.");
                StingLog.Error($"P2 connector transfer '{familyName}'", ex);
                return;
            }

            if (ct.Harvested == 0 && ct.Recreated == 0)
            {
                res.Notes.Add("Connectors: none on the source family.");
                return;
            }

            res.Notes.Add(ct.Summary());

            foreach (var w in ct.Warnings) res.Warnings.Add($"Connector: {w}");

            if (ct.Manual > 0)
            {
                res.Notes.Add($"NEXT STEP: {ct.Manual} connector(s) could not be re-created automatically — " +
                              "re-add them in the family editor at these positions (origins are relative to the family origin):");
                foreach (var d in ct.ManualDetails)
                    res.Notes.Add($"    • {d}");
            }

            if (ct.OnReferenceLine > 0)
                res.Notes.Add($"REVIEW: {ct.OnReferenceLine} connector(s) were re-created on minted reference lines " +
                              "rather than on copied geometry faces — verify they sit on the intended surface.");
        }

        private static ExternalDefinition FindExternalDefinition(DefinitionFile file, Guid guid)
        {
            try
            {
                foreach (DefinitionGroup g in file.Groups)
                    foreach (Definition d in g.Definitions)
                        if (d is ExternalDefinition ed && ed.GUID == guid)
                            return ed;
            }
            catch (Exception ex) { StingLog.Warn($"FindExternalDefinition: {ex.Message}"); }
            return null;
        }

        private static string ResolveTemplatePath(Application app, string templateFile)
        {
            if (string.IsNullOrEmpty(templateFile)) return "";
            string folder = StingTools.Core.Symbols.SymbolLibraryCreator.ResolveTemplateFolder(app);
            if (string.IsNullOrEmpty(folder)) return "";
            // Direct hit first, then a recursive search (templates sit in sub-folders).
            string direct = Path.Combine(folder, templateFile);
            if (File.Exists(direct)) return direct;
            try
            {
                var hit = Directory.EnumerateFiles(folder, templateFile, SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(hit)) return hit;
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTemplatePath '{templateFile}': {ex.Message}"); }
            return direct;
        }

        public static string ResolveConvertedDir(Document doc)
        {
            string projDir = !string.IsNullOrEmpty(doc?.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : null;
            if (string.IsNullOrEmpty(projDir))
                projDir = Path.Combine(Path.GetTempPath(), "STING_FamilyConverter");
            return Path.Combine(projDir, "_BIM_COORD", "Families", "Converted");
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "family";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        // §9 — in a workshared model, is the family owned/checked-out by another
        // user? Only defers when the current user is resolvable (so a Username
        // lookup failure never blocks a non-workshared / single-user edit).
        private static bool IsOwnedByOther(Document doc, Element el, out string owner)
        {
            owner = "";
            try
            {
                if (doc == null || el == null || !doc.IsWorkshared) return false;
                string me = doc.Application.Username;
                var info = WorksharingUtils.GetWorksharingTooltipInfo(doc, el.Id);
                if (!string.IsNullOrEmpty(me) && !string.IsNullOrEmpty(info?.Owner) && info.Owner != me)
                {
                    owner = info.Owner;
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"IsOwnedByOther: {ex.Message}"); }
            return false;
        }

        // §9 — does any instance of this family sit nested inside another family
        // instance (non-null SuperComponent)? Cheap best-effort flag for the report.
        private static bool HasNestedUsage(Document doc, Family fam)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Any(fi => fi.Symbol?.Family?.Id == fam.Id && fi.SuperComponent != null);
            }
            catch (Exception ex) { StingLog.Warn($"HasNestedUsage: {ex.Message}"); return false; }
        }
    }
}
