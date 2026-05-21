using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Inline MAT-tab action handlers. Each method runs on the Revit API
    /// thread under the existing IExternalEventHandler; they own their own
    /// transactions and surface user feedback through TaskDialogs.
    ///
    /// Why a static class: keeps the StingCommandHandler switch statement
    /// short. Methods are stateless; the active MaterialRow is passed in
    /// via the p1 channel of the external event (the selected material's
    /// ElementId as a long).
    /// </summary>
    internal static class MatActions
    {
        // ── Helpers ─────────────────────────────────────────────────────────

        private static Document Doc(UIApplication app) => app?.ActiveUIDocument?.Document;
        private static UIDocument UiDoc(UIApplication app) => app?.ActiveUIDocument;

        private static ElementId IdFromParam(string p1)
        {
            if (string.IsNullOrEmpty(p1)) return ElementId.InvalidElementId;
            return long.TryParse(p1, out long v) ? new ElementId(v) : ElementId.InvalidElementId;
        }

        private static Material ResolveMaterial(UIApplication app, string p1)
        {
            var doc = Doc(app);
            if (doc == null) return null;
            var id = IdFromParam(p1);
            if (id != null && id.Value > 0)
                return doc.GetElement(id) as Material;
            return null;
        }

        private static void RefreshDockMaterials()
        {
            try { StingDockPanel.LastInstance?.ShowMaterialsTab(); }
            catch (Exception ex) { StingLog.Warn($"RefreshDockMaterials: {ex.Message}"); }
        }

        // ── Where Used ──────────────────────────────────────────────────────

        public static void WhereUsed(UIApplication app, string p1)
        {
            var doc = Doc(app); var uidoc = UiDoc(app);
            var m = ResolveMaterial(app, p1);
            if (doc == null || uidoc == null) return;
            if (m == null)
            {
                TaskDialog.Show("Material Manager", "Pick a material row in the Browse tab first.");
                return;
            }
            try
            {
                var hosts = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => ElementUsesMaterial(e, m.Id))
                    .Select(e => e.Id)
                    .ToList();
                if (hosts.Count == 0)
                {
                    TaskDialog.Show("Where Used",
                        $"No elements found using '{m.Name}'.\n\nIt may only be referenced from a family Type Material, a paint, a view template override, or a filter — those are scanned in Phase B.");
                    return;
                }
                uidoc.Selection.SetElementIds(hosts);
                try { uidoc.ShowElements(hosts); } catch (Exception ex) { StingLog.Warn($"WhereUsed ShowElements: {ex.Message}"); }
                TaskDialog.Show("Where Used",
                    $"Selected {hosts.Count} element(s) using '{m.Name}'.");
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Where Used failed: {ex.Message}"); }
        }

        // ── Apply to Selection ──────────────────────────────────────────────

        public static void ApplyToSelection(UIApplication app, string p1)
        {
            var doc = Doc(app); var uidoc = UiDoc(app);
            var m = ResolveMaterial(app, p1);
            if (doc == null || uidoc == null) return;
            if (m == null)
            {
                TaskDialog.Show("Material Manager", "Pick a material row in the Browse tab first.");
                return;
            }
            var sel = uidoc.Selection.GetElementIds();
            if (sel == null || sel.Count == 0)
            {
                TaskDialog.Show("Apply to Selection",
                    $"Nothing selected in Revit. Select elements first, then click Apply → Sel.");
                return;
            }
            int written = 0;
            using (var t = new Transaction(doc, $"STING Apply Material '{m.Name}'"))
            {
                t.Start();
                foreach (var id in sel)
                {
                    try
                    {
                        var el = doc.GetElement(id);
                        var p = el?.LookupParameter("Material") ?? el?.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId)
                        { p.Set(m.Id); written++; }
                    }
                    catch (Exception ex) { StingLog.Warn($"MAT apply {id}: {ex.Message}"); }
                }
                t.Commit();
            }
            MaterialAuditLogger.Log(doc, "MAT_Apply", m.Name, new Dictionary<string, object>
            {
                ["elementsTouched"] = written,
                ["selectionCount"]  = sel.Count,
            });
            TaskDialog.Show("Apply to Selection",
                $"Material '{m.Name}' applied to {written} of {sel.Count} selected element(s).");
        }

        // ── Eyedropper ──────────────────────────────────────────────────────

        public static void Eyedropper(UIApplication app)
        {
            var uidoc = UiDoc(app); var doc = Doc(app);
            if (uidoc == null || doc == null) return;
            try
            {
                var reference = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Face,
                    "Pick a face to sample its material (ESC to cancel)");
                if (reference == null) return;
                var el = doc.GetElement(reference);
                if (el == null) return;
                var face = el.GetGeometryObjectFromReference(reference) as Face;
                if (face == null) return;
                ElementId matId = face.MaterialElementId;
                if (matId == null || matId.Value <= 0)
                {
                    TaskDialog.Show("Eyedropper", "The picked face has no material assigned.");
                    return;
                }
                if (doc.GetElement(matId) is Material m)
                {
                    try { System.Windows.Clipboard.SetText(m.Name ?? ""); }
                    catch (Exception ex) { StingLog.Warn($"Clipboard: {ex.Message}"); }
                    TaskDialog.Show("Eyedropper",
                        $"Sampled material: {m.Name}\n\nCopied to clipboard — paste with Ctrl+V into any Material parameter, or pick the matching row in the MAT tab to act on it.");
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* user pressed ESC */ }
            catch (Exception ex) { TaskDialog.Show("Eyedropper", $"Failed: {ex.Message}"); }
        }

        // ── Edit Identity (delegates to the Revit Material Browser) ─────────

        public static void EditIdentity(UIApplication app, string p1)
        {
            var m = ResolveMaterial(app, p1);
            if (m == null)
            {
                TaskDialog.Show("Material Manager", "Pick a material row in the Browse tab first.");
                return;
            }
            try
            {
                // Revit doesn't expose a programmatic "open material editor for X",
                // but PostCommand on the Materials browser is reliable.
                app.PostCommand(Autodesk.Revit.UI.RevitCommandId.LookupPostableCommandId(
                    Autodesk.Revit.UI.PostableCommand.Materials));
                TaskDialog.Show("Edit Identity",
                    $"Revit's Material Browser is opening. Locate '{m.Name}' in the browser to edit its Identity / Graphics / Appearance / Physical / Thermal tabs.");
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Edit failed: {ex.Message}"); }
        }

        // ── Layers (Phase B follow-up) ──────────────────────────────────────

        public static void ReadLayers(UIApplication app)
        {
            TaskDialog.Show("Read Layers",
                "Layer reading lands in Phase B (next commit). For now use the Layers sub-tab in the legacy modal (TEMP > Material Manager).");
        }

        public static void GenerateLayerTag(UIApplication app)
        {
            TaskDialog.Show("Generate Layer Tag",
                "Layer-tag generation lands in Phase B (next commit).");
        }

        // ── Duplicates ──────────────────────────────────────────────────────

        public static void FindDuplicates(UIApplication app)
        {
            TaskDialog.Show("Find Duplicates",
                "Duplicate-detection grid lands in commit C (asset-share count). For now use TEMP > Material Manager > Duplicates tab.");
        }

        public static void MergeDuplicates(UIApplication app)
        {
            TaskDialog.Show("Merge Duplicates",
                "Merge lands in commit C (asset-share count). For now use TEMP > Material Manager > Duplicates tab.");
        }

        // ── Library overrides ───────────────────────────────────────────────

        public static void EditProjectOverrides(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) { TaskDialog.Show("Material Manager", "No document open."); return; }
            try
            {
                string path = MaterialOverrideRegistry.GetOverrideFilePath(doc);
                MaterialOverrideRegistry.EnsureOverrideFile(doc);
                MaterialAuditLogger.Log(doc, "MAT_OverrideEdit", "(opened in editor)", new Dictionary<string, object>
                {
                    ["path"] = path,
                });
                if (File.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                else
                    TaskDialog.Show("Material Overrides", $"Override file not found:\n{path}");
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Open overrides failed: {ex.Message}"); }
        }

        public static void ReloadLibrary(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                MaterialOverrideRegistry.Reload(doc);
                TaskDialog.Show("Material Library", "Corporate baseline and project override reloaded.");
                RefreshDockMaterials();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Reload failed: {ex.Message}"); }
        }

        public static void PushToCorporate(UIApplication app, string p1)
        {
            TaskDialog.Show("Push to Corporate",
                "Promoting project overrides to the corporate baseline is an admin action and lands in a follow-up commit. Edit the corporate CSV (BLE_MATERIALS.csv / MEP_MATERIALS.csv) for now.");
        }

        // ── I/O ─────────────────────────────────────────────────────────────

        public static void ExportCsv(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var materials = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>().OrderBy(m => m.Name).ToList();
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                string filePath = Path.Combine(outDir, "STING_MATERIALS_EXPORT.csv");
                StingTools.Temp.MaterialPropertyHelper.ExportMaterialsCsv(doc, materials, filePath);
                TaskDialog.Show("Export CSV", $"Exported {materials.Count} material(s) to:\n{filePath}");
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"Open csv: {ex.Message}"); }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Export failed: {ex.Message}"); }
        }

        public static void ImportCsv(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) { TaskDialog.Show("Material Manager", "No document open."); return; }
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick a material CSV (Name + Cost + EmbodiedCarbon + Class columns)",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                };
                if (ofd.ShowDialog() != true) return;
                string csvPath = ofd.FileName;

                var diff = MaterialCsvDiff.Compute(doc, csvPath);
                if (diff == null) { TaskDialog.Show("Material Manager", "Diff failed to compute."); return; }

                var preview = new System.Text.StringBuilder();
                preview.AppendLine($"Source: {csvPath}");
                preview.AppendLine($"Project: {doc.Title}");
                preview.AppendLine();
                preview.AppendLine($"Changes to apply:");
                preview.AppendLine($"  • {diff.Updates.Count} material(s) with updated cost / carbon / class");
                preview.AppendLine($"  • {diff.NewRows.Count} new material(s) in CSV (NOT created — out of scope)");
                preview.AppendLine($"  • {diff.MissingInCsv.Count} project material(s) not in CSV (kept as-is)");
                preview.AppendLine();
                if (diff.Updates.Count > 0)
                {
                    preview.AppendLine("First 10 updates:");
                    int n = 0;
                    foreach (var u in diff.Updates)
                    {
                        if (n++ >= 10) break;
                        preview.AppendLine($"  {u.MaterialName}: {u.ChangeSummary}");
                    }
                }

                var td = new TaskDialog("Material Import — Diff Preview")
                {
                    MainInstruction = $"Apply {diff.Updates.Count} update(s) to project materials?",
                    MainContent = preview.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Commit changes", "Write cost / carbon / class to existing materials.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel", "Discard the diff — no changes.");
                var res = td.Show();
                if (res != TaskDialogResult.CommandLink1) return;

                int written = MaterialCsvDiff.Apply(doc, diff);
                MaterialAuditLogger.Log(doc, "MAT_Import", "(csv-import)", new System.Collections.Generic.Dictionary<string, object>
                {
                    ["sourcePath"] = csvPath,
                    ["updatesPlanned"] = diff.Updates.Count,
                    ["updatesWritten"] = written,
                });
                TaskDialog.Show("Material Import", $"Committed {written} of {diff.Updates.Count} update(s).");
                StingDockPanel.LastInstance?.ShowMaterialsTab();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Import failed: {ex.Message}"); }
        }

        public static void EditRules(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) { TaskDialog.Show("Material Manager", "No document open."); return; }
            try
            {
                string projPath = Path.Combine(
                    Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "",
                    MaterialRuleRegistry.FileName);
                if (!File.Exists(projPath))
                {
                    // Seed project file from corporate baseline so the user can edit.
                    string corp = StingToolsApp.FindDataFile("STING_MATERIAL_RULES.json");
                    if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(projPath));
                        File.Copy(corp, projPath, false);
                        TaskDialog.Show("Material Rules", $"Seeded project rule file from corporate baseline:\n{projPath}");
                    }
                }
                if (File.Exists(projPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(projPath) { UseShellExecute = true })?.Dispose();
                else
                    TaskDialog.Show("Material Rules", "No rule file available — corporate baseline missing from data/.");
                MaterialRuleRegistry.Reload(doc);
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Edit rules failed: {ex.Message}"); }
        }

        public static void ToggleAutoApply(UIApplication app)
        {
            bool now = StingMaterialUpdater.ToggleAutoApply();
            TaskDialog.Show("Material Auto-Apply",
                now
                ? "Auto-apply is ON. New Walls / Floors / Pipes / etc. will get their material from material_rules.json when no material is already assigned."
                : "Auto-apply is OFF. New elements keep whatever Revit defaults assign.");
        }

        public static void ToggleAutoFill(UIApplication app)
        {
            bool now = StingMaterialUpdater.ToggleAutoFill();
            TaskDialog.Show("Material Auto-Fill",
                now
                ? "Auto-fill is ON. New Materials get Cost + Carbon from the project override / MATERIAL_LOOKUP.csv on creation."
                : "Auto-fill is OFF. New Materials keep their Revit defaults.");
        }

        public static void OpenTemplate(UIApplication app)
        {
            try
            {
                string p1 = StingToolsApp.FindDataFile("BLE_MATERIALS.csv");
                string p2 = StingToolsApp.FindDataFile("MEP_MATERIALS.csv");
                if (!string.IsNullOrEmpty(p1) && File.Exists(p1))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p1) { UseShellExecute = true })?.Dispose();
                if (!string.IsNullOrEmpty(p2) && File.Exists(p2))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p2) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Open template failed: {ex.Message}"); }
        }

        // ── Element/material usage helper (shared with MaterialRowBuilder) ──

        private static bool ElementUsesMaterial(Element el, ElementId matId)
        {
            try
            {
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                    foreach (var m in mats)
                        if (m == matId) return true;
                var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId && p.AsElementId() == matId) return true;
            }
            catch (Exception ex) { StingLog.Warn($"ElementUsesMaterial {el?.Id}: {ex.Message}"); }
            return false;
        }
    }
}
