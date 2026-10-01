// StingTools — Drawing Template Manager · Phase 137
//
// Three migration commands for STING-Managed View Templates:
//   ConvertPackToManagedCommand     reads a Revit template into the pack,
//                                   flips templateMode = "managed".
//   DetachFromManagedCommand        renames STING-managed templates to a
//                                   plain name, flips templateMode = "external".
//   RegeneratePackTemplatesCommand  force-resyncs every STING:* template
//                                   for every managed pack across all
//                                   common ViewTypes.
//
// All three persist the pack edit to <project>/_BIM_COORD/view_style_packs.json
// (project override). Corporate baseline on disk is never mutated.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Select;

namespace StingTools.Commands.Drawing
{
    // ──────────────────────────────────────────────────────────────────
    // 1. Convert pack to managed
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConvertPackToManagedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                // Pick a pack (only packs that are NOT already managed)
                var allPacks = ViewStylePackRegistry.ListAll(doc)
                    .Where(p => !p.IsManaged)
                    .ToList();
                if (allPacks.Count == 0)
                {
                    TaskDialog.Show("STING — Convert to Managed", "No external packs to convert.");
                    return Result.Cancelled;
                }
                var packLabels = allPacks.Select(p => $"{p.Id} — {p.Name}").ToList();
                var packPicked = StingListPicker.Show(
                    "Convert pack to managed",
                    "Select the pack you want STING to start managing.",
                    packLabels);
                if (string.IsNullOrEmpty(packPicked)) return Result.Cancelled;
                var packId = packPicked.Split('—')[0].Trim();
                var resolved = ViewStylePackRegistry.Get(doc, packId);
                if (resolved == null) { msg = "Pack not found."; return Result.Failed; }
                // DTW-9: work on a copy. Get() returns the registry's memoised pack,
                // so editing it in place changed what every caller saw even when the
                // conversion was then abandoned.
                var pack = ClonePack(resolved);

                // Pick a Revit template
                var templates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate)
                    .OrderBy(v => v.Name)
                    .ToList();
                if (templates.Count == 0)
                {
                    TaskDialog.Show("STING — Convert to Managed",
                        "No view templates exist in this project. Create one first.");
                    return Result.Cancelled;
                }
                var tplPicked = StingListPicker.Show(
                    "Pick source template",
                    "STING will copy this template's settings into the pack.",
                    templates.Select(v => v.Name).ToList());
                if (string.IsNullOrEmpty(tplPicked)) return Result.Cancelled;
                var sourceTemplate = templates.First(v => v.Name == tplPicked);

                // Read settings
                int vgRead, filterRead;
                string originalTemplateName = sourceTemplate.Name;
                using (var tx = new Transaction(doc, "STING — Read template into pack"))
                {
                    tx.Start();
                    ReadTemplateIntoPack(doc, sourceTemplate, pack, out vgRead, out filterRead);
                    pack.TemplateMode = "managed";
                    if (pack.ManagedFields == null || pack.ManagedFields.Count == 0)
                        pack.ManagedFields = new List<string>
                            { "vg", "filters", "detailLevel", "discipline", "phaseFilter" };
                    pack.Origin = "project";

                    // Rename the legacy template so its slot is preserved
                    // but does not clash with future STING:<id>:<viewType> names.
                    try
                    {
                        var legacyName = sourceTemplate.Name + "_legacy";
                        // avoid clobbering an existing _legacy
                        if (!new FilteredElementCollector(doc).OfClass(typeof(View))
                            .Cast<View>().Any(v => v.IsTemplate && v.Name == legacyName))
                        {
                            sourceTemplate.Name = legacyName;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Convert to Managed: source template not renamed — {ex.Message}"); }

                    // DTW-9: persist BEFORE committing. The override used to be
                    // written after the commit by a method that silently returned on
                    // an unsaved model or an IO error, so the template was renamed
                    // to *_legacy and the dialog said "now managed" while nothing
                    // had been saved. If the pack cannot be saved, nothing changes.
                    if (!SaveProjectOverride(doc, pack, out var saveError))
                    {
                        tx.RollBack();
                        var fail = $"Pack '{pack.Id}' was NOT converted — the project override could not be saved:\n{saveError}\n\n" +
                                   $"Nothing was changed; the template '{originalTemplateName}' keeps its name.";
                        StingLog.Warn("Convert to Managed: " + fail);
                        PresetDialog.Show("STING — Convert to Managed", fail, ref msg);
                        return Result.Failed;
                    }

                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                        StingLog.Warn($"Convert to Managed: the template rename did not commit ({status}); the pack itself was saved as managed.");
                }

                ViewStylePackRegistry.Reload(doc);

                var sb = new StringBuilder();
                sb.AppendLine($"Pack '{pack.Id}' is now managed.");
                sb.AppendLine($"  • {vgRead} category overrides imported");
                sb.AppendLine($"  • {filterRead} filter rules imported");
                sb.AppendLine($"  • discipline = {pack.Discipline}");
                sb.AppendLine($"  • visualStyle = {pack.VisualStyle}");
                sb.AppendLine($"  • phaseFilter = {pack.PhaseFilter}");
                sb.AppendLine();
                sb.AppendLine($"Source template renamed to '{sourceTemplate.Name}' (not deleted).");
                sb.AppendLine("Run Sync Styles to generate STING-managed templates for each ViewType.");
                TaskDialog.Show("STING — Convert to Managed", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypes_ConvertToManaged", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static void ReadTemplateIntoPack(
            Document doc, View tpl, ViewStylePack pack,
            out int vgRead, out int filterRead)
        {
            vgRead = 0; filterRead = 0;

            // Discipline / visual style / detail / phase filter
            try { pack.Discipline = tpl.Discipline.ToString(); }
            catch (Exception ex) { StingLog.Warn($"Convert to Managed: discipline not read from '{tpl.Name}' — {ex.Message}"); }
            try { pack.VisualStyle = tpl.DisplayStyle.ToString(); }
            catch (Exception ex) { StingLog.Warn($"Convert to Managed: visual style not read from '{tpl.Name}' — {ex.Message}"); }
            // PhaseFilter is not on the View base class; read via parameter.
            try
            {
                var pfParam = tpl.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);
                if (pfParam != null && pfParam.HasValue)
                {
                    var pfId = pfParam.AsElementId();
                    if (pfId != null && pfId != ElementId.InvalidElementId)
                    {
                        var pfElem = doc.GetElement(pfId) as PhaseFilter;
                        if (pfElem != null) pack.PhaseFilter = pfElem.Name;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Convert to Managed: phase filter not read from '{tpl.Name}' — {ex.Message}"); }

            // VG overrides per category — only categories that the template
            // actually overrides (different from default).
            pack.VgOverrides = pack.VgOverrides ?? new Dictionary<string, StyleVgOverride>();
            try
            {
                foreach (Category c in doc.Settings.Categories)
                {
                    if (c == null) continue;
                    if (!c.AllowsBoundParameters) continue;
                    OverrideGraphicSettings ogs;
                    try { ogs = tpl.GetCategoryOverrides(c.Id); }
                    catch { continue; }
                    if (ogs == null) continue;

                    var ov = new StyleVgOverride();
                    bool any = false;
                    if (ogs.Halftone)        { ov.Halftone = true; any = true; }
                    if (ogs.ProjectionLineWeight > 0) { ov.ProjectionLineWeight = ogs.ProjectionLineWeight; any = true; }
                    if (ogs.ProjectionLineColor != null && ogs.ProjectionLineColor.IsValid)
                        { ov.ProjectionLineColor = ColorToHex(ogs.ProjectionLineColor); any = true; }
                    if (ogs.CutLineWeight > 0) { ov.CutLineWeight = ogs.CutLineWeight; any = true; }
                    if (ogs.CutLineColor != null && ogs.CutLineColor.IsValid)
                        { ov.CutLineColor = ColorToHex(ogs.CutLineColor); any = true; }
                    if (ogs.Transparency > 0) { ov.Transparency = ogs.Transparency; any = true; }
                    if (any) { pack.VgOverrides[c.Name] = ov; vgRead++; }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Convert to Managed: category overrides not fully read from '{tpl.Name}' — {ex.Message}"); }

            // Filter rules
            pack.Filters = pack.Filters ?? new List<StyleFilterRule>();
            try
            {
                foreach (var fid in tpl.GetFilters())
                {
                    var pf = doc.GetElement(fid) as ParameterFilterElement;
                    if (pf == null) continue;
                    var ogs = tpl.GetFilterOverrides(fid);
                    if (ogs == null) continue;

                    var rule = new StyleFilterRule
                    {
                        FilterName = pf.Name,
                        Visible    = tpl.GetFilterVisibility(fid),
                        Halftone   = ogs.Halftone,
                    };
                    if (ogs.ProjectionLineColor != null && ogs.ProjectionLineColor.IsValid)
                        rule.ProjectionLineColor = ColorToHex(ogs.ProjectionLineColor);
                    if (ogs.ProjectionLineWeight > 0) rule.ProjectionLineWeight = ogs.ProjectionLineWeight;
                    if (ogs.CutLineColor != null && ogs.CutLineColor.IsValid)
                        rule.CutLineColor = ColorToHex(ogs.CutLineColor);
                    if (ogs.CutLineWeight > 0) rule.CutLineWeight = ogs.CutLineWeight;
                    if (ogs.Transparency > 0)   rule.Transparency = ogs.Transparency;
                    // DTW-9: replace, don't append — the pack may already carry a
                    // rule for this filter (inherited, or from an earlier convert),
                    // and two rules for one filter left the result order-dependent.
                    // DT-R11-C: the pack may spell it as the data does ("STING - Struct: Concrete").
                    pack.Filters.RemoveAll(f => RevitNameRules.Matches(pf.Name, f?.FilterName));
                    pack.Filters.Add(rule);
                    filterRead++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Convert to Managed: filters not fully read from '{tpl.Name}' — {ex.Message}"); }
        }

        private static string ColorToHex(Autodesk.Revit.DB.Color c)
            => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

        internal static ViewStylePack ClonePack(ViewStylePack pack)
            => JsonConvert.DeserializeObject<ViewStylePack>(JsonConvert.SerializeObject(pack));

        /// <summary>
        /// Write <paramref name="pack"/> into the project override
        /// (_BIM_COORD/view_style_packs.json). DTW-9: returns false with a reason
        /// instead of returning silently — on an unsaved model (no project folder),
        /// an unreadable existing override (which used to be overwritten with this
        /// one pack, losing the rest) or an IO error.
        /// </summary>
        internal static bool SaveProjectOverride(Document doc, ViewStylePack pack, out string error)
        {
            error = null;
            try
            {
                if (doc == null) { error = "no document"; return false; }
                if (string.IsNullOrEmpty(doc.PathName))
                { error = "the model has never been saved, so it has no project folder for the override — save it first"; return false; }
                var dir = StingPaths.Meta(doc, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "view_style_packs.json");

                ViewStylePackLibrary lib = null;
                if (File.Exists(path))
                {
                    try { lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(File.ReadAllText(path)); }
                    catch (Exception ex)
                    {
                        error = $"the existing override '{path}' could not be read ({ex.Message}); fix or move it — it was not overwritten";
                        return false;
                    }
                }
                if (lib == null) lib = new ViewStylePackLibrary { Version = 1 };
                lib.Packs = lib.Packs ?? new List<ViewStylePack>();

                var existing = lib.Packs.FirstOrDefault(p =>
                    string.Equals(p.Id, pack.Id, StringComparison.OrdinalIgnoreCase));
                if (existing != null) lib.Packs.Remove(existing);
                lib.Packs.Add(pack);

                File.WriteAllText(path, JsonConvert.SerializeObject(lib, Formatting.Indented));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                StingLog.Warn("SaveProjectOverride: " + ex.Message);
                return false;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. Detach from managed
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DetachFromManagedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var managedPacks = ViewStylePackRegistry.ListAll(doc)
                    .Where(p => p.IsManaged)
                    .ToList();
                if (managedPacks.Count == 0)
                {
                    TaskDialog.Show("STING — Detach Managed", "No managed packs to detach.");
                    return Result.Cancelled;
                }
                var picked = StingListPicker.Show(
                    "Detach managed pack",
                    "Pack will be flipped to external. STING templates renamed; STING stops auto-updating them.",
                    managedPacks.Select(p => $"{p.Id} — {p.Name}").ToList());
                if (string.IsNullOrEmpty(picked)) return Result.Cancelled;
                var packId = picked.Split('—')[0].Trim();
                var resolvedPack = ViewStylePackRegistry.Get(doc, packId);
                if (resolvedPack == null) { msg = "Pack not found."; return Result.Failed; }
                // DTW-9: edit a copy, not the registry's memoised pack.
                var pack = ConvertPackToManagedCommand.ClonePack(resolvedPack);

                var prefix = $"STING:{pack.Id}:";
                var managedTemplates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.IsTemplate && (v.Name ?? "").StartsWith(prefix, StringComparison.Ordinal))
                    .ToList();

                int renamed = 0;
                using (var tx = new Transaction(doc, "STING — Detach managed pack"))
                {
                    tx.Start();
                    // Ensure templates exist before renaming
                    if (managedTemplates.Count == 0)
                    {
                        // Run syncer for FloorPlan as a baseline so detach has
                        // something to rename — best-effort.
                        try { ManagedTemplateSyncer.EnsureTemplate(doc, pack, ViewType.FloorPlan); }
                        catch (Exception ex) { StingLog.Warn($"Detach: baseline FloorPlan template not minted — {ex.Message}"); }
                        managedTemplates = new FilteredElementCollector(doc)
                            .OfClass(typeof(View)).Cast<View>()
                            .Where(v => v.IsTemplate && (v.Name ?? "").StartsWith(prefix, StringComparison.Ordinal))
                            .ToList();
                    }

                    string newBase = pack.Name ?? pack.Id;
                    string firstRenamed = null;
                    foreach (var tpl in managedTemplates)
                    {
                        try
                        {
                            // Strip prefix; replace : with — for clarity
                            var suffix = tpl.Name.Substring(prefix.Length);
                            var candidate = RevitNameRules.Sanitize($"{newBase} — {suffix}");   // DT-R11-C: a pack name is free text
                            try { tpl.Name = candidate; }
                            catch { tpl.Name = candidate + "_" + Guid.NewGuid().ToString("N").Substring(0, 4); }
                            firstRenamed = firstRenamed ?? tpl.Name;
                            renamed++;
                        }
                        catch (Exception ex) { StingLog.Warn("Detach rename: " + ex.Message); }
                    }
                    pack.TemplateMode = "external";
                    if (firstRenamed != null && string.IsNullOrEmpty(pack.Name))
                        pack.Name = newBase;

                    // DTW-9: save before committing the renames; if the pack cannot
                    // be saved as external, STING would keep managing templates it
                    // no longer recognises by name — roll everything back instead.
                    if (!ConvertPackToManagedCommand.SaveProjectOverride(doc, pack, out var saveError))
                    {
                        tx.RollBack();
                        var fail = $"Pack '{pack.Id}' was NOT detached — the project override could not be saved:\n{saveError}\n\nNothing was changed.";
                        StingLog.Warn("Detach Managed: " + fail);
                        PresetDialog.Show("STING — Detach Managed", fail, ref msg);
                        return Result.Failed;
                    }
                    tx.Commit();
                }

                ViewStylePackRegistry.Reload(doc);

                var sb = new StringBuilder();
                sb.AppendLine($"Pack '{pack.Id}' is now external.");
                sb.AppendLine($"Renamed {renamed} STING-managed template(s).");
                sb.AppendLine("STING will no longer auto-update these templates.");
                TaskDialog.Show("STING — Detach Managed", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypes_DetachManaged", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }

}
