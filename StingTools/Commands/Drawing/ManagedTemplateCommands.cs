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
                var doc = data?.Application?.ActiveUIDocument?.Document;
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
                var pack = ViewStylePackRegistry.Get(doc, packId);
                if (pack == null) { msg = "Pack not found."; return Result.Failed; }

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
                    catch { /* non-fatal */ }

                    tx.Commit();
                }

                // Persist to project override
                SaveProjectOverride(doc, pack);
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
            try { pack.Discipline = tpl.Discipline.ToString(); } catch { }
            try { pack.VisualStyle = tpl.DisplayStyle.ToString(); } catch { }
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
            catch { }

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
            catch { }

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
                    pack.Filters.Add(rule);
                    filterRead++;
                }
            }
            catch { }
        }

        private static string ColorToHex(Autodesk.Revit.DB.Color c)
            => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

        internal static void SaveProjectOverride(Document doc, ViewStylePack pack)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return;
                var dir = Path.Combine(Path.GetDirectoryName(doc.PathName), "_BIM_COORD");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "view_style_packs.json");

                ViewStylePackLibrary lib = null;
                if (File.Exists(path))
                {
                    try { lib = JsonConvert.DeserializeObject<ViewStylePackLibrary>(File.ReadAllText(path)); }
                    catch { lib = null; }
                }
                if (lib == null) lib = new ViewStylePackLibrary { Version = 1 };
                lib.Packs = lib.Packs ?? new List<ViewStylePack>();

                var existing = lib.Packs.FirstOrDefault(p =>
                    string.Equals(p.Id, pack.Id, StringComparison.OrdinalIgnoreCase));
                if (existing != null) lib.Packs.Remove(existing);
                lib.Packs.Add(pack);

                File.WriteAllText(path, JsonConvert.SerializeObject(lib, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn("SaveProjectOverride: " + ex.Message); }
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
                var doc = data?.Application?.ActiveUIDocument?.Document;
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
                var pack = ViewStylePackRegistry.Get(doc, packId);
                if (pack == null) { msg = "Pack not found."; return Result.Failed; }

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
                        catch { }
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
                            var candidate = $"{newBase} — {suffix}";
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
                    tx.Commit();
                }

                ConvertPackToManagedCommand.SaveProjectOverride(doc, pack);
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

    // ──────────────────────────────────────────────────────────────────
    // 3. Regenerate every managed pack template
    // ──────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RegeneratePackTemplatesCommand : IExternalCommand
    {
        // Common ViewTypes we generate templates for. ViewType has no
        // public Schedule/Legend canonical mapping that matters for
        // templating, so we stick to graphic views.
        private static readonly ViewType[] _viewTypes = new[]
        {
            ViewType.FloorPlan,
            ViewType.CeilingPlan,
            ViewType.Section,
            ViewType.Elevation,
            ViewType.ThreeD,
            ViewType.Detail,
            ViewType.DraftingView,
        };

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var managed = ViewStylePackRegistry.ListAll(doc)
                    .Where(p => p.IsManaged)
                    .ToList();
                if (managed.Count == 0)
                {
                    TaskDialog.Show("STING — Regenerate Templates",
                        "No managed packs to regenerate.");
                    return Result.Cancelled;
                }

                int created = 0, updated = 0, skipped = 0;
                var warnings = new List<string>();

                using (var tx = new Transaction(doc, "STING — Regenerate managed templates"))
                {
                    tx.Start();
                    foreach (var pack in managed)
                    {
                        foreach (var vt in _viewTypes)
                        {
                            try
                            {
                                var rs = ManagedTemplateSyncer.EnsureTemplate(doc, pack, vt);
                                if (rs.Created) created++;
                                else if (rs.Updated) updated++;
                                else if (rs.TemplateId == ElementId.InvalidElementId) skipped++;
                                if (rs.Warnings.Count > 0)
                                    warnings.AddRange(rs.Warnings.Select(w => $"[{pack.Id}/{vt}] {w}"));
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"[{pack.Id}/{vt}] {ex.Message}");
                            }
                        }
                    }
                    tx.Commit();
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Regenerated managed templates across {managed.Count} pack(s).");
                sb.AppendLine($"  • {created} created");
                sb.AppendLine($"  • {updated} updated (checksum drift healed)");
                sb.AppendLine($"  • {skipped} skipped (no seed template of that ViewType)");
                if (warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Warnings:");
                    foreach (var w in warnings.Take(15)) sb.AppendLine("  " + w);
                    if (warnings.Count > 15) sb.AppendLine($"  …({warnings.Count - 15} more)");
                }
                TaskDialog.Show("STING — Regenerate Templates", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypes_RegenerateTemplates", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }
}
