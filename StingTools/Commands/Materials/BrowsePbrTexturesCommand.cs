using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Materials;
using StingTools.Core.Materials.Providers;
using StingTools.UI;

namespace StingTools.Commands.Materials
{
    /// <summary>
    /// Opens the <see cref="MaterialHubProviderBrowserDialog"/> and, on
    /// OK, applies the downloaded pack to the currently-selected
    /// Revit material. Quick path that doesn't require the Material Hub
    /// panel to be open.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BrowsePbrTexturesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) { TaskDialog.Show("STING PBR", "No active document."); return Result.Cancelled; }

                Material mat = ResolveActiveMaterial(uidoc);
                if (mat == null)
                {
                    TaskDialog.Show("STING PBR",
                        "Select a single material-host element (or pick a material in the Material Hub) before running this command.");
                    return Result.Cancelled;
                }

                var dlg = new MaterialHubProviderBrowserDialog(doc);
                if (dlg.ShowDialog() != true || dlg.Result == null) return Result.Cancelled;

                using (var t = new Transaction(doc, "STING Apply PBR pack"))
                {
                    t.Start();
                    bool convertedAlready = GenericToPrismConverter.IsPrism(doc, mat);
                    if (!convertedAlready)
                    {
                        var conv = GenericToPrismConverter.Convert(doc, mat,
                            GenericToPrismConverter.ConvertMode.DuplicateMaterial);
                        if (!conv.Success) { t.RollBack(); TaskDialog.Show("STING PBR", "Convert failed: " + conv.Note); return Result.Failed; }
                        mat = conv.ResultMaterial;
                    }
                    var ar = PbrTextureApplier.Apply(doc, mat, dlg.Result);
                    if (ar.Success) t.Commit(); else t.RollBack();
                    TaskDialog.Show("STING PBR", ar.Success
                        ? $"Applied {ar.SlotsWritten} maps to '{mat.Name}' ({ar.SchemaUsed} schema)."
                        : "Apply failed:\n" + string.Join("\n", ar.Warnings));
                    return ar.Success ? Result.Succeeded : Result.Failed;
                }
            }
            catch (System.Exception ex)
            {
                StingLog.Error("BrowsePbrTexturesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Material ResolveActiveMaterial(UIDocument uidoc)
        {
            var doc = uidoc?.Document;
            if (doc == null) return null;

            // 1. If exactly one element is selected and it has at least one
            //    material, use the first one.
            var sel = uidoc.Selection?.GetElementIds();
            if (sel != null && sel.Count == 1)
            {
                var firstId = sel.First();
                var el = doc.GetElement(firstId);
                if (el != null)
                {
                    foreach (var matId in el.GetMaterialIds(false))
                    {
                        if (doc.GetElement(matId) is Material m) return m;
                    }
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Project-wide bulk apply: walks every pack folder under
    /// `_BIM_COORD/textures/` and applies it to the material whose name
    /// matches the pack folder (case-insensitive, with light fuzziness).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BulkApplyPbrTexturesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { TaskDialog.Show("STING PBR Bulk", "No active document."); return Result.Cancelled; }

                string root = TextureProviderRegistry.ProjectTexturesRoot(doc);
                if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root))
                {
                    TaskDialog.Show("STING PBR Bulk",
                        "No _BIM_COORD/textures/ folder yet. Use 'Browse PBR library…' first to download a pack, or drop folders manually.");
                    return Result.Cancelled;
                }

                // Build pack name → manifest map.
                var packs = new System.Collections.Generic.List<TexturePackManifest>();
                var rules = TextureProviderRegistry.SuffixRulesFor(doc);
                foreach (var dir in System.IO.Directory.GetDirectories(root, "*", System.IO.SearchOption.AllDirectories))
                {
                    var m = TexturePackIngester.LoadOrIngest(dir, providerId: ResolveProviderId(root, dir), suffixRules: rules);
                    if (m != null && m.Maps.FilledSlotCount > 0) packs.Add(m);
                }

                if (packs.Count == 0)
                {
                    TaskDialog.Show("STING PBR Bulk", "Texture root exists but no recognisable PBR packs were found.");
                    return Result.Cancelled;
                }

                // Build material name → element map.
                var matsByName = new System.Collections.Generic.Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);
                foreach (Material mat in new FilteredElementCollector(doc).OfClass(typeof(Material)))
                {
                    if (!matsByName.ContainsKey(mat.Name)) matsByName[mat.Name] = mat;
                }

                int applied = 0, skipped = 0, failed = 0;
                using (var t = new Transaction(doc, "STING Bulk-apply PBR packs"))
                {
                    t.Start();
                    foreach (var pack in packs)
                    {
                        if (!matsByName.TryGetValue(pack.DisplayName, out Material match) &&
                            !matsByName.TryGetValue(pack.PackId, out match) &&
                            !TryFuzzyMatch(matsByName, pack, out match))
                        {
                            skipped++;
                            continue;
                        }
                        var convResult = GenericToPrismConverter.Convert(doc, match,
                            GenericToPrismConverter.ConvertMode.InPlace);
                        var ar = PbrTextureApplier.Apply(doc, convResult.ResultMaterial ?? match, pack);
                        if (ar.Success) applied++; else failed++;
                    }
                    t.Commit();
                }

                TaskDialog.Show("STING PBR Bulk",
                    $"Packs found: {packs.Count}\nApplied: {applied}\nSkipped (no name match): {skipped}\nFailed: {failed}");
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                StingLog.Error("BulkApplyPbrTexturesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string ResolveProviderId(string textureRoot, string dir)
        {
            string parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(dir) ?? "");
            return string.IsNullOrEmpty(parent) || parent.Equals("textures", System.StringComparison.OrdinalIgnoreCase)
                ? "user-folder" : parent.ToLowerInvariant();
        }

        private static bool TryFuzzyMatch(System.Collections.Generic.IDictionary<string, Material> mats, TexturePackManifest pack, out Material hit)
        {
            // Light fuzziness: strip whitespace + underscores + hyphens
            // and compare. Real semantic match should be added later.
            string Norm(string s) => new string((s ?? "").ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c)).ToArray());
            string target = Norm(pack.DisplayName);
            if (string.IsNullOrEmpty(target)) { hit = null; return false; }
            foreach (var kv in mats)
            {
                if (Norm(kv.Key) == target) { hit = kv.Value; return true; }
            }
            hit = null;
            return false;
        }
    }

    /// <summary>Reloads the texture provider catalogue from disk (corporate
    /// JSON + any project override). Use after editing
    /// `<project>/_BIM_COORD/texture_providers.json`.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ReloadPbrProvidersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                TextureProviderRegistry.Reload();
                TaskDialog.Show("STING PBR", "Provider catalogue reloaded.");
                return Result.Succeeded;
            }
            catch (System.Exception ex) { StingLog.Error("ReloadPbrProvidersCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
