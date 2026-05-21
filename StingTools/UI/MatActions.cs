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
            var doc = Doc(app); var uidoc = UiDoc(app);
            if (doc == null || uidoc == null) return;
            try
            {
                var sel = uidoc.Selection.GetElementIds();
                Element host = null;
                if (sel != null && sel.Count > 0) host = doc.GetElement(sel.First());
                if (host == null)
                {
                    TaskDialog.Show("Read Layers",
                        "Select a Wall / Floor / Roof / Ceiling / Foundation / Pad in Revit first.");
                    return;
                }
                var rows = MaterialLayerInspector.Read(doc, host);
                if (rows.Count == 0)
                {
                    TaskDialog.Show("Read Layers",
                        $"'{host.Category?.Name} {host.Id}' has no compound structure (or it couldn't be read). Layered tags only apply to System Family hosts.");
                    return;
                }
                StingDockPanel.LastInstance?.SetLayerRows(rows, host.Id);
                TaskDialog.Show("Read Layers",
                    $"Read {rows.Count} layer(s) from '{host.Name}'. They're now editable in the Layers sub-tab. Click 'Generate Layer Tag' to write STING_LAYERS_TXT on the element TYPE.");
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Read Layers failed: {ex.Message}"); }
        }

        public static void GenerateLayerTag(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var rows = StingDockPanel.LastInstance?.GetLayerRows();
                var hostId = StingDockPanel.LastInstance?.GetLayerHostId();
                if (rows == null || rows.Count == 0 || hostId == null || hostId.Value <= 0)
                {
                    TaskDialog.Show("Generate Layer Tag", "Click 'Read Layers' first.");
                    return;
                }
                var host = doc.GetElement(hostId);
                if (host == null) { TaskDialog.Show("Generate Layer Tag", "Host element no longer exists."); return; }
                string tag = MaterialLayerInspector.BuildLayerTag(rows);
                bool ok;
                using (var t = new Transaction(doc, "STING Generate Layer Tag"))
                {
                    t.Start();
                    ok = MaterialLayerInspector.WriteLayerTag(doc, host, tag);
                    t.Commit();
                }
                if (ok)
                {
                    MaterialAuditLogger.Log(doc, "MAT_LayerTag", host.Name,
                        new Dictionary<string, object> { ["lines"] = rows.Count, ["typeId"] = host.GetTypeId()?.Value ?? 0 });
                    TaskDialog.Show("Generate Layer Tag",
                        $"Wrote {rows.Count} layer line(s) to STING_LAYERS_TXT on '{host.Name}' (Type).\n\nLayer tag preview:\n{tag}");
                }
                else
                {
                    TaskDialog.Show("Generate Layer Tag",
                        "Couldn't write STING_LAYERS_TXT — make sure the parameter is bound to the host category. Run 'Load Shared Params' from the dock panel if needed.");
                }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Generate Layer Tag failed: {ex.Message}"); }
        }

        // ── Duplicates ──────────────────────────────────────────────────────

        public static void FindDuplicates(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var mode = StingDockPanel.LastInstance?.GetDuplicateMode() ?? DuplicateMode.SameName;
                var rows = MaterialDuplicateFinder.Find(doc, mode);
                StingDockPanel.LastInstance?.SetDuplicateRows(rows);
                if (rows.Count == 0)
                    TaskDialog.Show("Find Duplicates",
                        $"No duplicate clusters found for mode '{mode}'.\n\nTry a different mode (fuzzy / RGB / appearance) for a wider search.");
                else
                {
                    int clusters = rows.GroupBy(r => r.ClusterKey).Count();
                    TaskDialog.Show("Find Duplicates",
                        $"Found {clusters} cluster(s) covering {rows.Count} material(s) in mode '{mode}'.\n\nReview the Duplicates grid — the most-used material is checked as the default keeper. Adjust the keepers if needed, then click 'Merge Selected'.");
                }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Find Duplicates failed: {ex.Message}"); }
        }

        public static void MergeDuplicates(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var rows = StingDockPanel.LastInstance?.GetDuplicateRows();
                if (rows == null || rows.Count == 0)
                { TaskDialog.Show("Merge Duplicates", "Run 'Find Duplicates' first."); return; }

                var clusters = rows.GroupBy(r => r.ClusterKey).ToList();
                int losers = rows.Count - clusters.Count;
                var td = new TaskDialog("Merge Duplicates")
                {
                    MainInstruction = $"Merge {losers} material(s) across {clusters.Count} cluster(s)?",
                    MainContent = "Every usage of the losing materials will repoint to the keeper, then the losers are deleted. This is a single transaction — Ctrl+Z reverts the whole batch.",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Commit merge", "");
                if (td.Show() != TaskDialogResult.CommandLink1) return;

                int merged = MaterialDuplicateFinder.Merge(doc, rows.ToList());
                TaskDialog.Show("Merge Duplicates",
                    $"Merged {merged} material(s). The Duplicates grid will be empty — re-run 'Find Duplicates' to verify.");
                StingDockPanel.LastInstance?.SetDuplicateRows(new List<DuplicateRow>());
                StingDockPanel.LastInstance?.ShowMaterialsTab();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Merge failed: {ex.Message}"); }
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

        // ── A10 — Sustainability gate (LEED v5 / BREEAM v6 / EN 15978) ─────

        public static void RunSustainabilityGate(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) { TaskDialog.Show("Material Manager", "No document open."); return; }
            try
            {
                // Use the rows the panel has cached if any; otherwise rebuild
                // so the gate works even before the user clicks Refresh.
                var rows = StingDockPanel.LastInstance?.GetCachedMaterialRows()
                           ?? MaterialRowBuilder.Build(doc).ToList();
                var findings = MaterialSustainabilityGate.RunAll(doc, rows);
                if (findings.Count == 0)
                {
                    TaskDialog.Show("Sustainability Gate",
                        "All clear — no findings against the LEED v5 / BREEAM v6 / EN 15978 rule set.");
                    return;
                }

                // Build a digest grouped by standard + severity.
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{findings.Count} finding(s) across {findings.Select(f => f.RuleId).Distinct().Count()} rule(s):");
                sb.AppendLine();
                int blocks = findings.Count(f => string.Equals(f.Severity, "Block", StringComparison.OrdinalIgnoreCase));
                int warns  = findings.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
                int infos  = findings.Count(f => string.Equals(f.Severity, "Info", StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"  Block: {blocks}    Warning: {warns}    Info: {infos}");
                sb.AppendLine();
                foreach (var g in findings.GroupBy(f => f.RuleId).OrderByDescending(g => g.Count()))
                {
                    var sample = g.First();
                    sb.AppendLine($"  [{sample.Standard}] {sample.RuleId} — {g.Count()} material(s)");
                    foreach (var f in g.Take(3))
                        sb.AppendLine($"      · {f.MaterialName}");
                    if (g.Count() > 3) sb.AppendLine($"      · … and {g.Count() - 3} more");
                }

                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                string csv = System.IO.Path.Combine(outDir, $"STING_sustainability_findings_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var lines = new List<string> { "RuleId,Standard,Severity,MaterialName,Message" };
                lines.AddRange(findings.Select(f =>
                    $"\"{f.RuleId}\",\"{f.Standard}\",\"{f.Severity}\",\"{f.MaterialName}\",\"{f.Message?.Replace("\"", "''")}\""));
                System.IO.File.WriteAllLines(csv, lines);

                MaterialAuditLogger.Log(doc, "MAT_SustainabilityGate", "(all)",
                    new Dictionary<string, object>
                    {
                        ["findings"] = findings.Count,
                        ["blocks"] = blocks,
                        ["warnings"] = warns,
                        ["infos"] = infos,
                        ["csv"] = csv,
                    });

                TaskDialog.Show("Sustainability Gate", sb.ToString() + $"\n\nFull CSV: {csv}");
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(csv) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"Open csv: {ex.Message}"); }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Sustainability gate failed: {ex.Message}"); }
        }

        public static void EditSustainabilityGate(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                string projPath = System.IO.Path.Combine(
                    Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "",
                    "sustainability_gates.json");
                if (!System.IO.File.Exists(projPath))
                {
                    string corp = StingToolsApp.FindDataFile("STING_SUSTAINABILITY_GATES.json");
                    if (!string.IsNullOrEmpty(corp) && System.IO.File.Exists(corp))
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(projPath));
                        System.IO.File.Copy(corp, projPath);
                    }
                }
                if (System.IO.File.Exists(projPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(projPath) { UseShellExecute = true })?.Dispose();
                else
                    TaskDialog.Show("Sustainability", "No rule file available.");
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Edit gates failed: {ex.Message}"); }
        }

        // ── N6 — Family-side material index ─────────────────────────────────

        public static void ShowFamilyMaterials(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) { TaskDialog.Show("Material Manager", "No document open."); return; }
            try
            {
                var rows = FamilySideMaterialIndex.Index(doc);
                if (rows.Count == 0)
                {
                    TaskDialog.Show("Family Materials",
                        "No Material parameters found on loaded Family Types.\n\nNote: this only scans loaded families. To audit unloaded .rfa files, use I/O > Audit Family Folder.");
                    return;
                }
                // Group by material name, count distinct family-type references.
                var byName = rows.GroupBy(r => r.MaterialName, StringComparer.OrdinalIgnoreCase)
                                 .OrderByDescending(g => g.Count())
                                 .ToList();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Found {rows.Count} family-type Material references across {byName.Count} distinct material(s).");
                sb.AppendLine();
                sb.AppendLine("Top 20 by usage:");
                foreach (var g in byName.Take(20))
                {
                    int loaded = g.Count(r => r.IsLoadedInProject);
                    sb.AppendLine($"  {g.Key}  ({g.Count()} family types, {loaded} loaded as Project Material, origin: {g.First().Origin})");
                }
                if (byName.Count > 20) sb.AppendLine($"  … and {byName.Count - 20} more (full list logs to console).");

                // Also write a CSV so the user has the full data.
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                string csv = System.IO.Path.Combine(outDir, $"STING_family_side_materials_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var lines = new List<string> { "MaterialName,FamilyName,TypeName,Origin,LoadedInProject" };
                lines.AddRange(rows.OrderBy(r => r.MaterialName).Select(r =>
                    $"\"{r.MaterialName}\",\"{r.FamilyName}\",\"{r.TypeName}\",{r.Origin},{r.IsLoadedInProject}"));
                System.IO.File.WriteAllLines(csv, lines);

                MaterialAuditLogger.Log(doc, "MAT_FamilyIndex", "(project)",
                    new Dictionary<string, object>
                    {
                        ["distinctMaterials"] = byName.Count,
                        ["totalReferences"] = rows.Count,
                        ["csv"] = csv,
                    });

                TaskDialog.Show("Family Materials", sb.ToString() + $"\n\nFull CSV: {csv}");
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(csv) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"Open csv: {ex.Message}"); }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Family Materials failed: {ex.Message}"); }
        }

        // ── N5 — Asset detach / repoint ─────────────────────────────────────

        /// <summary>
        /// Duplicate the asset (Appearance / Physical / Thermal) selected
        /// in the Assets sub-tab and re-point the active material at the
        /// new copy. Other materials keep the original so this material's
        /// edits won't leak.
        /// </summary>
        public static void DetachAsset(UIApplication app, string p1)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var mat = ResolveMaterial(app, p1);
                if (mat == null) { TaskDialog.Show("Asset", "Pick a material in the Browse tab first."); return; }

                string kind = StingDockPanel.LastInstance?.GetSelectedAssetKind() ?? "";
                if (string.IsNullOrEmpty(kind)) { TaskDialog.Show("Asset", "Pick an asset row in the Assets sub-tab first."); return; }

                ElementId srcId = AssetIdForKind(mat, kind);
                if (srcId == null || srcId.Value <= 0)
                {
                    TaskDialog.Show("Asset", $"Material '{mat.Name}' has no {kind} asset to detach.");
                    return;
                }

                bool ok = false;
                using (var t = new Transaction(doc, $"STING Detach {kind} asset"))
                {
                    t.Start();
                    ok = DuplicateAndRepoint(doc, mat, kind, srcId);
                    t.Commit();
                }
                MaterialAuditLogger.Log(doc, "MAT_AssetDetach", mat.Name,
                    new Dictionary<string, object> { ["kind"] = kind, ["srcAssetId"] = srcId.Value, ["ok"] = ok });
                TaskDialog.Show("Asset Detach",
                    ok
                    ? $"Detached {kind} asset on '{mat.Name}'. Other materials keep the original copy."
                    : $"Detach failed — see log for details.");
                StingDockPanel.LastInstance?.ShowMaterialsTab();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Detach failed: {ex.Message}"); }
        }

        /// <summary>
        /// Point the selected asset slot at the same asset another picked
        /// material uses — consolidates duplicate assets.
        /// </summary>
        public static void RepointAsset(UIApplication app, string p1)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var mat = ResolveMaterial(app, p1);
                if (mat == null) { TaskDialog.Show("Asset", "Pick a material in the Browse tab first."); return; }

                string kind = StingDockPanel.LastInstance?.GetSelectedAssetKind() ?? "";
                if (string.IsNullOrEmpty(kind)) { TaskDialog.Show("Asset", "Pick an asset row in the Assets sub-tab first."); return; }

                // Build a list of candidate materials sharing the right asset kind.
                var candidates = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>().Where(m => m.Id.Value != mat.Id.Value &&
                        AssetIdForKind(m, kind) is ElementId aid && aid != null && aid.Value > 0)
                    .OrderBy(m => m.Name).Take(40).ToList();
                if (candidates.Count == 0)
                {
                    TaskDialog.Show("Repoint", $"No other materials have a {kind} asset to point at.");
                    return;
                }
                var td = new TaskDialog($"Repoint {kind} asset")
                {
                    MainInstruction = $"Pick a material whose {kind} asset to share.",
                    MainContent = "After repoint, this material's edits to that asset will flow to every other material sharing it.",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                int link = 1;
                var byLink = new Dictionary<TaskDialogResult, Material>();
                foreach (var c in candidates.Take(4))
                {
                    var lid = (TaskDialogCommandLinkId)Enum.Parse(typeof(TaskDialogCommandLinkId), "CommandLink" + link);
                    td.AddCommandLink(lid, c.Name, $"asset id {AssetIdForKind(c, kind)?.Value}");
                    byLink[(TaskDialogResult)lid] = c;
                    link++;
                }
                var res = td.Show();
                if (!byLink.TryGetValue(res, out Material target)) return;

                bool ok = false;
                using (var t = new Transaction(doc, $"STING Repoint {kind} asset"))
                {
                    t.Start();
                    ok = RepointAssetTo(doc, mat, kind, AssetIdForKind(target, kind));
                    t.Commit();
                }
                MaterialAuditLogger.Log(doc, "MAT_AssetRepoint", mat.Name,
                    new Dictionary<string, object>
                    {
                        ["kind"] = kind,
                        ["targetMaterial"] = target.Name,
                        ["ok"] = ok,
                    });
                TaskDialog.Show("Asset Repoint",
                    ok
                    ? $"'{mat.Name}' {kind} asset now shares with '{target.Name}'."
                    : "Repoint failed — see log.");
                StingDockPanel.LastInstance?.ShowMaterialsTab();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Repoint failed: {ex.Message}"); }
        }

        private static ElementId AssetIdForKind(Material m, string kind) => kind switch
        {
            "Appearance" => m.AppearanceAssetId,
            "Physical"   => m.StructuralAssetId,
            "Thermal"    => m.ThermalAssetId,
            _ => null,
        };

        private static bool DuplicateAndRepoint(Document doc, Material mat, string kind, ElementId srcId)
        {
            // For Appearance assets we can use AppearanceAssetElement.Duplicate.
            // For Physical / Thermal (PropertySetElement) we duplicate via
            // GetDuplicate on the asset element.
            try
            {
                if (kind == "Appearance")
                {
                    var src = doc.GetElement(srcId) as AppearanceAssetElement;
                    if (src == null) return false;
                    var dupe = src.Duplicate($"{src.Name}_copy");
                    mat.AppearanceAssetId = dupe.Id;
                    return true;
                }
                // Structural + Thermal asset duplication is via PropertySetElement.Duplicate.
                if (doc.GetElement(srcId) is PropertySetElement pse)
                {
                    var dupe = pse.Duplicate($"{pse.Name}_copy");
                    if (kind == "Physical") mat.StructuralAssetId = dupe.Id;
                    else if (kind == "Thermal") mat.ThermalAssetId = dupe.Id;
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"DuplicateAndRepoint: {ex.Message}"); }
            return false;
        }

        private static bool RepointAssetTo(Document doc, Material mat, string kind, ElementId targetId)
        {
            if (targetId == null || targetId.Value <= 0) return false;
            try
            {
                if (kind == "Appearance") mat.AppearanceAssetId = targetId;
                else if (kind == "Physical") mat.StructuralAssetId = targetId;
                else if (kind == "Thermal") mat.ThermalAssetId = targetId;
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"RepointAssetTo: {ex.Message}"); return false; }
        }

        // ── F14 — Family folder audit (CTC parity) ─────────────────────────

        public static void FamilyFolderAudit(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null || app?.Application == null) return;
            try
            {
                var fbd = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Pick a folder of .rfa families to audit (read-only)",
                    ShowNewFolderButton = false,
                };
                if (fbd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                var td = new TaskDialog("Family Material Audit")
                {
                    MainInstruction = "Audit options",
                    MainContent = $"Folder: {fbd.SelectedPath}\n\nOpening every .rfa read-only is slow (~1-2 s per family). A 500-family vendor drop takes ~15 minutes.",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Top-level only", "Scan .rfa in the picked folder, ignore sub-folders.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Recursive",     "Scan .rfa in every sub-folder.");
                var res = td.Show();
                if (res == TaskDialogResult.Cancel) return;
                bool recursive = (res == TaskDialogResult.CommandLink2);

                var result = FamilyMaterialAuditor.Run(app.Application, fbd.SelectedPath, recursive);
                string reportPath = FamilyMaterialAuditor.WriteReport(fbd.SelectedPath, result);
                MaterialAuditLogger.Log(doc, "MAT_FamilyAudit", fbd.SelectedPath,
                    new Dictionary<string, object>
                    {
                        ["familiesScanned"] = result.FamiliesScanned,
                        ["materialsFound"]  = result.Rows.Count,
                        ["failures"]        = result.Failures.Count,
                        ["elapsedSeconds"]  = (int)result.Elapsed.TotalSeconds,
                        ["recursive"]       = recursive,
                    });

                TaskDialog.Show("Family Material Audit",
                    $"Scanned {result.FamiliesScanned} family file(s) in {result.Elapsed.TotalSeconds:F0}s\n" +
                    $"Found {result.Rows.Count} material reference(s)\n" +
                    $"Failures: {result.Failures.Count}\n\n" +
                    $"CSV report: {reportPath}");
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(reportPath) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"Open report: {ex.Message}"); }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Family Audit failed: {ex.Message}"); }
        }

        // ── A6 — Material packs (Drawing-Type binding) ──────────────────────

        public static void LoadMaterialPack(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) { TaskDialog.Show("Material Manager", "No document open."); return; }
            try
            {
                var file = MaterialPackRegistry.GetOrLoad(doc);
                if (file?.Packs == null || file.Packs.Count == 0)
                {
                    TaskDialog.Show("Material Packs",
                        "No packs available. Add packs to Data/STING_MATERIAL_PACKS.json or _BIM_COORD/material_packs.json.");
                    return;
                }
                var td = new TaskDialog("Load Material Pack")
                {
                    MainInstruction = "Pick a pack to load into this project.",
                    MainContent = "Existing materials with the same name are left alone — pack-load is additive.",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                int linkIdx = 1;
                var idByLink = new Dictionary<TaskDialogResult, string>();
                foreach (var kv in file.Packs.Take(4))
                {
                    var linkId = (TaskDialogCommandLinkId)Enum.Parse(typeof(TaskDialogCommandLinkId), "CommandLink" + linkIdx);
                    td.AddCommandLink(linkId, kv.Value.Name ?? kv.Key,
                        $"{kv.Value.Description ?? ""}\n({kv.Value.Materials?.Count ?? 0} materials)");
                    idByLink[(TaskDialogResult)linkId] = kv.Key;
                    linkIdx++;
                    if (linkIdx > 4) break;
                }
                var res = td.Show();
                if (!idByLink.TryGetValue(res, out string packId)) return;

                var pack = MaterialPackRegistry.Get(doc, packId);
                if (pack == null) return;
                int created = MaterialPackRegistry.LoadPack(doc, pack);
                TaskDialog.Show("Material Pack",
                    created > 0
                    ? $"Loaded {created} new material(s) from '{pack.Name}'."
                    : $"Every material in '{pack.Name}' was already present — nothing new minted.");
                StingDockPanel.LastInstance?.ShowMaterialsTab();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Load Pack failed: {ex.Message}"); }
        }

        // Per-session dedupe so batch-stamping (e.g. 60 sheets at once) only
        // prompts once per drawing type.
        private static readonly HashSet<string> _packSuggestionsShown =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Hook fired from DrawingTypeStamper after a sheet/view is stamped.
        /// Surfaces a suggestion to load the matching pack when the
        /// profile declares one. Dedupes per drawing-type id so a batch
        /// stamp doesn't pop sixty dialogs.
        /// </summary>
        public static void SuggestPackForDrawingType(Document doc, string drawingTypeId)
        {
            if (doc == null || string.IsNullOrEmpty(drawingTypeId)) return;
            lock (_packSuggestionsShown)
            {
                if (_packSuggestionsShown.Contains(drawingTypeId)) return;
                _packSuggestionsShown.Add(drawingTypeId);
            }
            try
            {
                var dt = StingTools.Core.Drawing.DrawingTypeRegistry.Get(doc, drawingTypeId);
                if (dt == null || string.IsNullOrEmpty(dt.MaterialPack)) return;
                var pack = MaterialPackRegistry.Get(doc, dt.MaterialPack);
                if (pack == null) return;

                // Skip the prompt if every material in the pack already exists.
                var existing = new HashSet<string>(
                    new FilteredElementCollector(doc).OfClass(typeof(Material))
                        .Cast<Material>().Select(m => m.Name ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                int missing = pack.Materials?.Count(n => !existing.Contains(n)) ?? 0;
                if (missing == 0) return;

                var td = new TaskDialog("STING Material Pack")
                {
                    MainInstruction = $"Drawing Type '{drawingTypeId}' is bound to pack '{pack.Name}'.",
                    MainContent = $"{missing} of {pack.Materials?.Count ?? 0} materials are missing in this project. Load them now?",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Load Pack", $"Create the {missing} missing material(s).");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip", "Don't load now (you can run Library > Load Pack later).");
                if (td.Show() != TaskDialogResult.CommandLink1) return;

                int created = MaterialPackRegistry.LoadPack(doc, pack);
                StingLog.Info($"SuggestPackForDrawingType: '{drawingTypeId}' loaded {created} material(s) from '{pack.Name}'");
            }
            catch (Exception ex) { StingLog.Warn($"SuggestPackForDrawingType: {ex.Message}"); }
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

        // ── Coverage gate — run on demand ───────────────────────────────────

        public static void RunCoverageCheck(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var r = MaterialCoverageGate.Compute(doc);
                if (r.TotalMaterialable == 0)
                {
                    TaskDialog.Show("Material Coverage",
                        "No materialable elements found in this project.");
                    return;
                }
                MaterialAuditLogger.Log(doc, "MAT_CoverageCheck", "(project)",
                    new Dictionary<string, object>
                    {
                        ["totalMaterialable"] = r.TotalMaterialable,
                        ["assigned"]          = r.Assigned,
                        ["missing"]           = r.Missing,
                        ["coveragePct"]       = Math.Round(r.CoveragePct, 1),
                    });
                var td = new TaskDialog("Material Coverage")
                {
                    MainInstruction = $"{r.CoveragePct:F1}% coverage ({r.Assigned}/{r.TotalMaterialable})",
                    MainContent = r.Missing > 0
                        ? $"{r.Missing} element(s) still need a material assigned.\nThreshold for Issue Deliverable: {MaterialCoverageGate.DefaultThresholdPct}%."
                        : "All materialable elements have a material assigned.",
                    CommonButtons = TaskDialogCommonButtons.Close,
                };
                if (r.Missing > 0)
                {
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Select unassigned",
                        "Set Revit selection to every element missing a material.");
                    var res = td.Show();
                    if (res == TaskDialogResult.CommandLink1)
                    {
                        try { app.ActiveUIDocument?.Selection?.SetElementIds(r.MissingIds); }
                        catch (Exception ex) { StingLog.Warn($"Coverage select: {ex.Message}"); }
                    }
                }
                else td.Show();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Coverage check failed: {ex.Message}"); }
        }

        // ── A11 — Procurement hand-off (RFQ generator) ──────────────────────

        public static void GenerateRfq(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var rows = MaterialRfqGenerator.Build(doc);
                if (rows.Count == 0)
                {
                    TaskDialog.Show("Generate RFQ",
                        "No materials have Manufacturer / Model / URL set.\n\nPopulate those fields in the Browse grid (Edit Identity) or in Revit's Material Browser, then re-run.");
                    return;
                }
                string csv = MaterialRfqGenerator.WriteCsv(doc, rows);
                MaterialAuditLogger.Log(doc, "MAT_RfqGenerate", "(project)",
                    new Dictionary<string, object>
                    {
                        ["rows"] = rows.Count,
                        ["totalEstimate"] = rows.Sum(r => r.EstimatedTotal),
                        ["csv"] = csv,
                    });
                TaskDialog.Show("Generate RFQ",
                    $"Generated {rows.Count} RFQ row(s).\n" +
                    $"Estimated total: {rows.Sum(r => r.EstimatedTotal):F0}\n\n" +
                    $"CSV: {csv}");
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(csv) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"Open RFQ csv: {ex.Message}"); }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Generate RFQ failed: {ex.Message}"); }
        }

        // ── N+6 — Healthcare material gates ─────────────────────────────────

        public static void RunHealthcareGate(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var findings = HealthcareMaterialGate.RunAll(doc);
                if (findings.Count == 0)
                {
                    TaskDialog.Show("Healthcare Material Gate",
                        "All clear — no element/material pairing breaches the HTM 04-01 / MRI / HTM 03-01 / NCRP 147 / HTM 06-01 rule pack.");
                    return;
                }

                int blocks = findings.Count(f => string.Equals(f.Severity, "Block", StringComparison.OrdinalIgnoreCase));
                int warns  = findings.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
                int infos  = findings.Count(f => string.Equals(f.Severity, "Info", StringComparison.OrdinalIgnoreCase));

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{findings.Count} finding(s):");
                sb.AppendLine($"  Block: {blocks}    Warning: {warns}    Info: {infos}");
                sb.AppendLine();
                foreach (var g in findings.GroupBy(f => f.RuleId).OrderByDescending(g => g.Count()))
                {
                    var first = g.First();
                    sb.AppendLine($"  [{first.Standard}] {first.RuleId} — {g.Count()} element(s)");
                    foreach (var f in g.Take(3))
                        sb.AppendLine($"      · {f.ElementId} '{f.ElementName}' — material '{f.MaterialName}'");
                    if (g.Count() > 3) sb.AppendLine($"      · … and {g.Count() - 3} more");
                }

                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                string csv = System.IO.Path.Combine(outDir,
                    $"STING_healthcare_findings_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var lines = new List<string> { "RuleId,Standard,Severity,ElementId,ElementName,MaterialName,Message" };
                lines.AddRange(findings.Select(f =>
                    $"\"{f.RuleId}\",\"{f.Standard}\",\"{f.Severity}\",{f.ElementId},\"{f.ElementName}\",\"{f.MaterialName}\",\"{f.Message?.Replace("\"", "''")}\""));
                System.IO.File.WriteAllLines(csv, lines);

                MaterialAuditLogger.Log(doc, "MAT_HealthcareGate", "(project)",
                    new Dictionary<string, object>
                    {
                        ["findings"] = findings.Count,
                        ["blocks"]   = blocks,
                        ["warnings"] = warns,
                        ["infos"]    = infos,
                        ["csv"]      = csv,
                    });

                var td = new TaskDialog("Healthcare Material Gate")
                {
                    MainInstruction = $"{findings.Count} finding(s) across HTM 04 / 03 / 06, MRI safety, NCRP 147",
                    MainContent = sb.ToString() + $"\n\nFull CSV: {csv}",
                    CommonButtons = TaskDialogCommonButtons.Close,
                };
                if (findings.Any(f => f.ElementId > 0))
                {
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Select offending elements",
                        "Set Revit selection to every element that triggered a finding.");
                    var res = td.Show();
                    if (res == TaskDialogResult.CommandLink1)
                    {
                        try
                        {
                            var ids = findings.Where(f => f.ElementId > 0)
                                              .Select(f => new ElementId(f.ElementId))
                                              .ToList();
                            app.ActiveUIDocument?.Selection?.SetElementIds(ids);
                        }
                        catch (Exception ex) { StingLog.Warn($"Healthcare select: {ex.Message}"); }
                    }
                }
                else td.Show();

                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(csv) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"Open healthcare csv: {ex.Message}"); }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Healthcare gate failed: {ex.Message}"); }
        }

        public static void EditHealthcareGate(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                string projPath = System.IO.Path.Combine(
                    Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "",
                    "healthcare_material_gates.json");
                if (!System.IO.File.Exists(projPath))
                {
                    string corp = StingToolsApp.FindDataFile("STING_HEALTHCARE_MATERIAL_GATES.json");
                    if (!string.IsNullOrEmpty(corp) && System.IO.File.Exists(corp))
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(projPath));
                        System.IO.File.Copy(corp, projPath);
                    }
                }
                if (System.IO.File.Exists(projPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(projPath) { UseShellExecute = true })?.Dispose();
                else
                    TaskDialog.Show("Healthcare", "No rule file available.");
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Edit healthcare gates failed: {ex.Message}"); }
        }

        // ── N+5 — COBie material sync ───────────────────────────────────────

        public static void SyncCobie(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var r = CobieMaterialBridge.Audit(doc);
                if (string.IsNullOrEmpty(r.CsvPath))
                {
                    TaskDialog.Show("Sync COBie",
                        "COBIE_TYPE_MAP.csv not found (neither project override nor corporate baseline).");
                    return;
                }
                if (r.RowsScanned == 0)
                {
                    TaskDialog.Show("Sync COBie", $"COBie file is empty:\n{r.CsvPath}");
                    return;
                }
                MaterialAuditLogger.Log(doc, "MAT_CobieAudit", "(project)",
                    new Dictionary<string, object>
                    {
                        ["rowsScanned"] = r.RowsScanned,
                        ["mismatches"]  = r.Mismatches.Count,
                        ["csv"]         = r.CsvPath,
                    });
                if (r.Mismatches.Count == 0)
                {
                    TaskDialog.Show("Sync COBie",
                        $"All clear — {r.RowsScanned} COBie row(s) resolve cleanly to project materials.");
                    return;
                }
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{r.Mismatches.Count} COBie Material reference(s) need attention:");
                sb.AppendLine();
                foreach (var m in r.Mismatches.Take(20))
                    sb.AppendLine($"  {m.TypeCode}  {m.TypeName} — '{m.CobieMaterial}' → {m.Resolution}");
                if (r.Mismatches.Count > 20) sb.AppendLine($"  … and {r.Mismatches.Count - 20} more.");
                sb.AppendLine();
                sb.AppendLine($"Source: {r.CsvPath}");
                TaskDialog.Show("Sync COBie", sb.ToString());
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"Sync COBie failed: {ex.Message}"); }
        }

        // ── N+11 — What-if material swap ────────────────────────────────────

        public static void WhatIfSwap(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                // Two-step picker — From, then To. Reuse the simple TaskDialog
                // pattern; a richer modeless dialog can land later.
                var materials = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>().OrderBy(m => m.Name).ToList();
                if (materials.Count < 2)
                {
                    TaskDialog.Show("What-If", "Need at least 2 materials in the project."); return;
                }

                string fromName = PickMaterialName(materials, "From material (current)");
                if (string.IsNullOrEmpty(fromName)) return;
                string toName = PickMaterialName(materials.Where(m => m.Name != fromName).ToList(),
                                                  "To material (swap target)");
                if (string.IsNullOrEmpty(toName)) return;

                var preview = MaterialWhatIfEngine.Preview(doc, fromName, toName);
                if (preview.ElementCount == 0)
                {
                    TaskDialog.Show("What-If",
                        $"No elements use '{fromName}'. Nothing to swap.");
                    return;
                }

                var td = new TaskDialog("What-If — Preview")
                {
                    MainInstruction = $"{fromName}  →  {toName}",
                    MainContent =
                        $"{preview.ElementCount} element(s) affected.\n\n" +
                        $"Σ cost   : {preview.OldCostTotal:F0} → {preview.NewCostTotal:F0}  (Δ {preview.CostDeltaTotal:+#,##0;-#,##0;0})\n" +
                        $"Σ carbon : {preview.OldCarbonTotal:F0} → {preview.NewCarbonTotal:F0} kgCO₂e  (Δ {preview.CarbonDeltaTotal:+#,##0;-#,##0;0})",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Commit swap",
                    "Repoint every element from the old material to the new one. Single transaction — Ctrl+Z reverts the whole batch.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Export preview only",
                    "Write the preview rows to CSV without committing.");
                var res = td.Show();
                if (res == TaskDialogResult.Cancel) return;

                if (res == TaskDialogResult.CommandLink2)
                {
                    string csv = WriteWhatIfCsv(doc, preview);
                    TaskDialog.Show("What-If", $"Preview written:\n{csv}");
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(csv) { UseShellExecute = true })?.Dispose(); }
                    catch (Exception ex) { StingLog.Warn($"Open whatif csv: {ex.Message}"); }
                    return;
                }

                int written = MaterialWhatIfEngine.Commit(doc, preview);
                TaskDialog.Show("What-If",
                    $"Committed: {written} element(s) repointed from '{fromName}' to '{toName}'.\n\n" +
                    $"Δ cost  = {preview.CostDeltaTotal:+#,##0;-#,##0;0}\n" +
                    $"Δ carbon = {preview.CarbonDeltaTotal:+#,##0;-#,##0;0} kgCO₂e\n\n" +
                    $"Ctrl+Z reverts the whole batch.");
                StingDockPanel.LastInstance?.ShowMaterialsTab();
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"What-If failed: {ex.Message}"); }
        }

        private static string PickMaterialName(List<Material> materials, string instruction)
        {
            // Simple 4-option chooser. For more materials, the user picks one
            // of the top-4 most-used; for arbitrary picks, future commit
            // could land a real WPF picker.
            var top = materials.Take(8).ToList();
            var td = new TaskDialog("What-If — Pick material")
            {
                MainInstruction = instruction,
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            int link = 1;
            var byLink = new Dictionary<TaskDialogResult, string>();
            foreach (var m in top.Take(4))
            {
                var lid = (TaskDialogCommandLinkId)Enum.Parse(typeof(TaskDialogCommandLinkId), "CommandLink" + link);
                td.AddCommandLink(lid, m.Name, m.MaterialClass ?? "");
                byLink[(TaskDialogResult)lid] = m.Name;
                link++;
            }
            var res = td.Show();
            return byLink.TryGetValue(res, out string n) ? n : null;
        }

        private static string WriteWhatIfCsv(Document doc, WhatIfPreview preview)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            string path = System.IO.Path.Combine(outDir,
                $"STING_whatif_{preview.FromMaterial}_to_{preview.ToMaterial}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# What-If: {preview.FromMaterial} → {preview.ToMaterial}");
            sb.AppendLine($"# Elements: {preview.ElementCount}");
            sb.AppendLine($"# Δ cost = {preview.CostDeltaTotal:F2}");
            sb.AppendLine($"# Δ carbon = {preview.CarbonDeltaTotal:F1} kgCO₂e");
            sb.AppendLine();
            sb.AppendLine("ElementId,ElementName,Category,Quantity,Unit,OldCost,NewCost,CostDelta,OldCarbonKg,NewCarbonKg,CarbonDelta");
            foreach (var r in preview.Rows)
                sb.AppendLine($"{r.ElementId},\"{r.ElementName}\",\"{r.Category}\",{r.Quantity:F2},{r.Unit},{r.OldCost:F2},{r.NewCost:F2},{r.CostDelta:F2},{r.OldCarbon:F1},{r.NewCarbon:F1},{r.CarbonDelta:F1}");
            System.IO.File.WriteAllText(path, sb.ToString());
            return path;
        }

        // ── N+10 — BOQ by material pivot ────────────────────────────────────

        public static void BoqByMaterial(UIApplication app)
        {
            var doc = Doc(app);
            if (doc == null) return;
            try
            {
                var boq = StingTools.BOQ.BOQCostManager.BuildBOQDocument(doc);
                if (boq == null || boq.AllItems.Count == 0)
                {
                    TaskDialog.Show("BOQ by Material",
                        "BOQ has no line items — nothing to pivot. Run the BOQ Cost Manager first.");
                    return;
                }
                var pivot = StingTools.BOQ.BOQByMaterialView.Build(doc, boq);
                string csv = StingTools.BOQ.BOQByMaterialView.WriteCsv(doc, pivot);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{pivot.Rows.Count} distinct material(s) across {pivot.ItemsScanned} BOQ row(s).");
                sb.AppendLine($"  Σ cost   = UGX {pivot.TotalCostUGX:N0}");
                sb.AppendLine($"  Σ carbon = {pivot.TotalCarbonKg:N0} kgCO₂e ({pivot.TotalCarbonKg / 1000:F1} tCO₂e)");
                if (pivot.ItemsWithoutMaterial > 0)
                    sb.AppendLine($"  {pivot.ItemsWithoutMaterial} row(s) had no resolvable material.");
                sb.AppendLine();
                sb.AppendLine("Top 15 by cost:");
                foreach (var r in pivot.Rows.Take(15))
                    sb.AppendLine($"  {r.MaterialName}  ({r.ElementCount} elements, {r.TotalQuantity:F1} {r.DominantUnit}) — UGX {r.TotalCostUGX:N0} · {r.TotalCarbonKg:N0} kgCO₂e");
                if (pivot.Rows.Count > 15) sb.AppendLine($"  … and {pivot.Rows.Count - 15} more.");
                sb.AppendLine();
                sb.AppendLine($"Full CSV: {csv}");

                MaterialAuditLogger.Log(doc, "MAT_BoqByMaterial", "(project)",
                    new Dictionary<string, object>
                    {
                        ["distinctMaterials"] = pivot.Rows.Count,
                        ["totalCostUgx"]      = pivot.TotalCostUGX,
                        ["totalCarbonKg"]     = pivot.TotalCarbonKg,
                        ["csv"]               = csv,
                    });
                TaskDialog.Show("BOQ by Material", sb.ToString());
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(csv) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { StingLog.Warn($"Open BOQ-by-material csv: {ex.Message}"); }
            }
            catch (Exception ex) { TaskDialog.Show("Material Manager", $"BOQ by Material failed: {ex.Message}"); }
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
