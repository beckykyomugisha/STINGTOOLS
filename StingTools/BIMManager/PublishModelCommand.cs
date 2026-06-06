#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// MODEL-VIEWER — Publishes a 3D model (glTF / GLB / IFC) to the Planscape
    /// server together with an element map sidecar that bridges the exporter's
    /// element GUIDs to STING ISO 19650 tags.
    ///
    /// Workflow:
    ///   1. Sign in to Planscape (PlanscapeServerClient.Instance.LoginAsync)
    ///   2. Pick a project on the server to publish into
    ///   3. Plugin asks the user to select a glTF/GLB/IFC file that was
    ///      produced by an external exporter (Revit doesn't ship a built-in
    ///      glTF writer — any of the following work:
    ///        - Autodesk Platform Services (APS) Model Derivative
    ///        - 3rd party: SimLab glTF exporter, rvt2gltf, Blender via IFC
    ///        - Autodesk Dynamo package "Rhythm → ExportToGltf"
    ///   4. Plugin generates the element map JSON from the currently visible
    ///      elements in the active 3D view (mapping Revit UniqueId ↔ ISO tag).
    ///   5. Both files are uploaded to /api/projects/{id}/models.
    ///
    /// The element map is optional — the viewer works without it (element
    /// names come from the glTF userData), but rich tooltips + discipline
    /// filter need the mapping.
    /// </summary>
    /// <summary>
    /// Publish modes offered by the up-front picker. Each maps to a
    /// different combination of server endpoints and dedup behaviour so
    /// coordinators can fit the operation to what's actually changed.
    /// </summary>
    public enum PublishMode
    {
        /// <summary>
        /// Default. Hash-dedup — if the bytes match an existing entry the
        /// server refreshes the element-map / metadata on that row;
        /// otherwise it creates a new entry. The "least-surprise" mode
        /// for everyday re-publishes after re-tagging.
        /// </summary>
        Auto,

        /// <summary>
        /// Always create a new ProjectModel row, even when the geometry
        /// hash matches. Used when a coordinator wants a discrete new
        /// revision label even though the bytes haven't changed (e.g.
        /// for an issue-for-coordination snapshot).
        /// </summary>
        ForceNewRevision,

        /// <summary>
        /// Soft-delete the latest model on the server, then upload a
        /// fresh one. Useful when an old broken row is poisoning the
        /// viewer (e.g. StorageMissing on an entry whose original GLB
        /// no longer exists locally).
        /// </summary>
        ReplaceExisting,

        /// <summary>
        /// Push a new element-map / thumbnail / metadata against an
        /// existing model id WITHOUT re-uploading geometry. Bandwidth-
        /// friendly when only the tag overlay has changed.
        /// </summary>
        RefreshMetadataOnly,
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PublishModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            // ── Step 1: ensure connected ───────────────────────────────
            var client = PlanscapeServerClient.Instance;
            if (string.IsNullOrEmpty(client.ConnectedUser))
            {
                TaskDialog.Show(
                    "Publish Model to Planscape",
                    "You're not signed in to Planscape. Use 'Sign in to Planscape' first (BIM tab → Platform Integration).");
                return Result.Cancelled;
            }

            // ── Step 2: pick a project ─────────────────────────────────
            var projectId = PickProject(client);
            if (projectId == Guid.Empty) return Result.Cancelled;

            // ── Step 3: pick the publish mode up front ─────────────────
            // Showing every option BEFORE we generate / upload anything
            // means coordinators with a slow connection don't waste a
            // GLB export only to discover the dedup path didn't do what
            // they wanted.
            var mode = PromptForPublishMode();
            if (mode == null) return Result.Cancelled;

            // ── Step 4: pick or export geometry ────────────────────────
            // RefreshMetadataOnly still needs a path so we can hash it
            // and find the existing model id on the server — but we
            // never actually upload the bytes in that mode.
            var modelPath = PromptForModelFileOrExport(doc);
            if (string.IsNullOrEmpty(modelPath)) return Result.Cancelled;

            // ── Step 5: build element map sidecar ──────────────────────
            string? mapPath = null;
            try
            {
                mapPath = Path.Combine(
                    OutputLocationHelper.GetOutputDirectory(doc),
                    Path.GetFileNameWithoutExtension(modelPath) + "-elements.json");
                BuildElementMap(doc, mapPath, out var elementCount, out var bounds);
                StingLog.Info($"Planscape: element map generated ({elementCount} elements) → {mapPath}");

                return mode switch
                {
                    PublishMode.RefreshMetadataOnly => DoRefreshMetadata(
                        client, projectId, modelPath!, mapPath, doc, elementCount),

                    PublishMode.ReplaceExisting => DoReplaceExisting(
                        client, projectId, modelPath!, mapPath, doc, elementCount, bounds),

                    PublishMode.ForceNewRevision => DoUpload(
                        client, projectId, modelPath!, mapPath, doc, elementCount, bounds, force: true,
                        successHeadline: "Published as a new revision (forced)"),

                    _ => DoUpload(
                        client, projectId, modelPath!, mapPath, doc, elementCount, bounds, force: false,
                        successHeadline: "Published"),
                };
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Publish Model", $"Failed: {ex.Message}");
                StingLog.Error("Planscape: publish model failed", ex);
                return Result.Failed;
            }
        }

        // ── Mode picker ────────────────────────────────────────────────

        private static PublishMode? PromptForPublishMode()
        {
            // Note: TaskDialog.DefaultButton only accepts common-button
            // values (Ok / Cancel / Close / Yes / No), not CommandLink1-4.
            // Setting DefaultButton = CommandLink1 throws ArgumentException
            // ("Corresponding button not found. Parameter name: defaultButton").
            // The first command link is naturally the keyboard default
            // anyway, so we just don't set DefaultButton.
            var dlg = new TaskDialog("Publish Model to Planscape")
            {
                MainInstruction = "How do you want to publish this model?",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto — smart dedup (recommended)",
                "If the geometry hash matches an existing entry, the server refreshes its element-map " +
                "and metadata on the existing row. Otherwise a new entry is created. Best for everyday re-publishes.");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Force new revision",
                "Always create a new ProjectModel row, even when the bytes match. Use this when you want a " +
                "discrete revision label for an unchanged GLB (e.g. an issue-for-coordination snapshot).");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Replace existing — delete & re-upload",
                "Soft-deletes the matching model on the server first, then uploads a fresh one. Use when an old " +
                "row is poisoning the viewer (e.g. its bytes were wiped and you want a clean slate).");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Refresh metadata only — no re-upload",
                "Pushes a new element-map / thumbnail / revision label against the matching existing entry " +
                "without re-uploading the GLB. Bandwidth-friendly when only the tag overlay has changed.");
            var r = dlg.Show();
            return r switch
            {
                TaskDialogResult.CommandLink1 => PublishMode.Auto,
                TaskDialogResult.CommandLink2 => PublishMode.ForceNewRevision,
                TaskDialogResult.CommandLink3 => PublishMode.ReplaceExisting,
                TaskDialogResult.CommandLink4 => PublishMode.RefreshMetadataOnly,
                _ => null,
            };
        }

        // ── Mode dispatchers ───────────────────────────────────────────

        private static Result DoUpload(
            PlanscapeServerClient client, Guid projectId,
            string modelPath, string? mapPath, Document doc,
            int elementCount, double[] bounds,
            bool force, string successHeadline)
        {
            var result = Task.Run(() => client.UploadModelAsync(
                projectId,
                modelPath,
                mapPath,
                name: doc.Title,
                description: $"Published from Revit {doc.Application.VersionName}",
                discipline: DetectDocDiscipline(doc),
                revision: PhaseAutoDetect.DetectProjectRevision(doc),
                units: "mm",
                elementCount: elementCount,
                bounds: bounds,
                force: force)).GetAwaiter().GetResult();

            if (!result.ok)
            {
                TaskDialog.Show("Publish Model", $"Upload failed: {result.error}");
                StingLog.Warn($"Planscape: model upload failed — {result.error}");
                return Result.Failed;
            }

            // The server's hash-dedup branch returns 200 with `duplicate=true`
            // when bytes already exist; the plugin's UploadModelAsync surfaces
            // that as alreadyExisted=true. In Auto mode we treat this as a
            // refresh success; in ForceNewRevision mode it's an oddity (the
            // server would have created a new row anyway) so we just report
            // the standard success.
            var refreshed = result.alreadyExisted && !force;
            var headline = refreshed
                ? "Geometry already published — element-map and metadata refreshed on the existing entry."
                : successHeadline;
            TaskDialog.Show(
                "Publish Model to Planscape",
                $"{headline}\n\n" +
                $"File: {Path.GetFileName(modelPath)}\n" +
                $"Project: {projectId}\n" +
                $"Model id: {result.modelId}\n" +
                $"Elements mapped: {elementCount}\n\n" +
                (refreshed
                    ? "The viewer + mobile app will pick up the new element-map on next open. " +
                      "To create a NEW revision instead of refreshing, run Publish again and pick 'Force new revision'."
                    : "Site users can now open the model from the Planscape mobile app → Models, or from the web viewer."));
            StingLog.Info($"Planscape: model published ({(refreshed ? "refreshed" : (force ? "forced" : "new"))}) → {result.modelId}");
            return Result.Succeeded;
        }

        private static Result DoRefreshMetadata(
            PlanscapeServerClient client, Guid projectId,
            string modelPath, string? mapPath, Document doc, int elementCount)
        {
            // Find the existing model row by content hash so the user
            // doesn't have to pick from a list. If the model doesn't
            // exist on the server yet, fall back to a normal upload —
            // refresh-metadata-only on a missing row would be confusing.
            string hash = PlanscapeServerClient.ComputeSha256(modelPath);
            var modelId = Task.Run(() => client.FindModelByHashAsync(projectId, hash)).GetAwaiter().GetResult();
            if (modelId == null)
            {
                var fallback = new TaskDialog("Refresh Metadata")
                {
                    MainInstruction = "No matching model found on the server",
                    MainContent =
                        "There's no published entry with the same SHA-256 as this file, so there's nothing to " +
                        "refresh against. Upload the geometry instead?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.Yes,
                };
                if (fallback.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                return DoUpload(client, projectId, modelPath, mapPath, doc, elementCount,
                    new[] { 0d, 0d, 0d, 0d, 0d, 0d }, force: false, successHeadline: "Published");
            }

            var result = Task.Run(() => client.RefreshModelMetadataAsync(
                projectId, modelId.Value,
                elementMapPath: mapPath,
                name: doc.Title,
                discipline: DetectDocDiscipline(doc),
                revision: PhaseAutoDetect.DetectProjectRevision(doc),
                elementCount: elementCount)).GetAwaiter().GetResult();
            if (!result.ok)
            {
                TaskDialog.Show("Refresh Metadata", $"Failed: {result.error}");
                StingLog.Warn($"Planscape: refresh-metadata failed — {result.error}");
                return Result.Failed;
            }
            TaskDialog.Show(
                "Publish Model to Planscape",
                $"Element-map and metadata refreshed on the existing entry.\n\n" +
                $"Project: {projectId}\n" +
                $"Model id: {modelId}\n" +
                $"Elements mapped: {elementCount}\n\n" +
                "The geometry on the server is unchanged. The viewer will pick up the new element-map on next open.");
            StingLog.Info($"Planscape: model metadata refreshed → {modelId}");
            return Result.Succeeded;
        }

        private static Result DoReplaceExisting(
            PlanscapeServerClient client, Guid projectId,
            string modelPath, string? mapPath, Document doc,
            int elementCount, double[] bounds)
        {
            // Find existing row by hash; if it's there, soft-delete it so
            // the new upload doesn't trigger the dedup branch. If no
            // matching row exists, this collapses to a normal upload.
            string hash = PlanscapeServerClient.ComputeSha256(modelPath);
            var existingId = Task.Run(() => client.FindModelByHashAsync(projectId, hash)).GetAwaiter().GetResult();
            if (existingId.HasValue)
            {
                var del = Task.Run(() => client.DeleteModelAsync(projectId, existingId.Value)).GetAwaiter().GetResult();
                if (!del.ok)
                {
                    TaskDialog.Show("Replace Model", $"Couldn't delete the old entry: {del.error}");
                    StingLog.Warn($"Planscape: delete-before-replace failed — {del.error}");
                    return Result.Failed;
                }
                StingLog.Info($"Planscape: replaced existing model {existingId} for project {projectId}");
            }
            return DoUpload(client, projectId, modelPath, mapPath, doc,
                elementCount, bounds, force: true,
                successHeadline: existingId.HasValue
                    ? "Old entry deleted; new model published"
                    : "Published (no matching prior entry)");
        }

        // ── Project picker ─────────────────────────────────────────────

        private static Guid PickProject(PlanscapeServerClient client)
        {
            var projects = Task.Run(() => client.GetProjectsAsync()).GetAwaiter().GetResult();
            if (projects == null || projects.Count == 0)
            {
                TaskDialog.Show("Publish Model", "No Planscape projects are visible to your account.");
                return Guid.Empty;
            }

            // Reuse StingListPicker via its public surface when present.
            var names = projects.Select(p => (p["name"]?.Value<string>() ?? "") + "  ·  " + (p["code"]?.Value<string>() ?? "")).ToList();
            var dlg = new TaskDialog("Publish Model") { MainInstruction = "Select the target project" };
            for (int i = 0; i < Math.Min(names.Count, 4); i++)
            {
                dlg.AddCommandLink(
                    i == 0 ? TaskDialogCommandLinkId.CommandLink1 :
                    i == 1 ? TaskDialogCommandLinkId.CommandLink2 :
                    i == 2 ? TaskDialogCommandLinkId.CommandLink3 :
                             TaskDialogCommandLinkId.CommandLink4,
                    names[i]);
            }
            var r = dlg.Show();
            int idx = r == TaskDialogResult.CommandLink1 ? 0
                    : r == TaskDialogResult.CommandLink2 ? 1
                    : r == TaskDialogResult.CommandLink3 ? 2
                    : r == TaskDialogResult.CommandLink4 ? 3 : -1;
            if (idx < 0 || idx >= projects.Count) return Guid.Empty;
            return Guid.TryParse(projects[idx]["id"]?.Value<string>() ?? "", out var id) ? id : Guid.Empty;
        }

        // ── File picker ────────────────────────────────────────────────

        private static string? PromptForModelFileOrExport(Document doc)
        {
            var dlg = new TaskDialog("Publish Model")
            {
                MainInstruction = "How do you want to provide the 3D geometry?",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Export current 3D view to GLB  (recommended)",
                "Uses the built-in STING glTF exporter. Active view must be a 3D view. " +
                "Produces a file the web/mobile viewer renders directly — no server conversion needed.");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Pick an existing file (.glb / .gltf / .ifc)",
                "GLB/glTF render directly. IFC is auto-converted to GLB on the server " +
                "(requires the Planscape converter to be enabled). OBJ/FBX are NOT supported — " +
                "the viewer can't render them and there is no converter, so they're excluded.");
            var r = dlg.Show();
            if (r == TaskDialogResult.CommandLink1) return ExportActiveView(doc);
            if (r == TaskDialogResult.CommandLink2) return PromptForModelFile();
            return null;
        }

        private static string? ExportActiveView(Document doc)
        {
            if (doc.ActiveView is not View3D v3d || v3d.IsTemplate)
            {
                TaskDialog.Show("Publish Model",
                    "The active view is not a non-template 3D view. Open a 3D view first.");
                return null;
            }
            // Sanitise: Revit's auto-generated default 3D view is named "{3D}" (literal
            // curly braces), and other views can contain ':' / '\\' / etc. Strip any
            // characters that would survive into the GLB filename ugly or illegal.
            string safeViewName = SanitiseFilenameSegment(v3d.Name);
            string safeDocName  = SanitiseFilenameSegment(
                Path.GetFileNameWithoutExtension(doc.PathName ?? doc.Title));
            var outPath = Path.Combine(
                OutputLocationHelper.GetOutputDirectory(doc),
                $"{safeDocName}-{safeViewName}.glb");
            try
            {
                // Phase 2 — "PlanscapeExportTextures" export option: real Revit material
                // textures (ON for presentation / as-built, OFF for lean coordination /
                // low-bandwidth). Opt in via env var PLANSCAPE_EXPORT_TEXTURES=1 or by
                // setting RevitGltfExporter.ExportTextures=true. Default OFF (unchanged).
                bool wantTextures =
                    string.Equals(Environment.GetEnvironmentVariable("PLANSCAPE_EXPORT_TEXTURES"), "1", StringComparison.OrdinalIgnoreCase)
                    || RevitGltfExporter.ExportTextures;
                var result = RevitGltfExporter.Export(doc, v3d, outPath, exportTextures: wantTextures);
                StingLog.Info($"Planscape: GLB exported ({result.ElementCount} elements, {result.FileSizeBytes:N0} bytes) → {outPath}");
                return outPath;
            }
            catch (Exception ex)
            {
                StingLog.Error("Planscape: GLB export failed", ex);
                TaskDialog.Show("Publish Model", $"GLB export failed: {ex.Message}");
                return null;
            }
        }

        private static string? PromptForModelFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select 3D model to publish",
                // Only formats the platform can actually display: GLB/glTF render
                // directly in the web/mobile viewer; IFC is auto-converted to GLB
                // server-side by ModelDerivativeJob. OBJ/FBX are intentionally
                // excluded — there is no converter for them, so publishing one
                // produces a model that opens to an empty viewer.
                Filter = "Viewable 3D models (*.glb;*.gltf;*.ifc)|*.glb;*.gltf;*.ifc|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
            };
            var ok = dlg.ShowDialog();
            return ok == true ? dlg.FileName : null;
        }

        // ── Element map generator ──────────────────────────────────────

        private static void BuildElementMap(
            Document doc, string outputPath,
            out int elementCount, out double[] bounds)
        {
            var activeView = doc.ActiveView;
            var collector = activeView is View3D
                ? new FilteredElementCollector(doc, activeView.Id)
                : new FilteredElementCollector(doc);

            var elements = collector
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.get_Geometry(new Options()) != null)
                .ToList();

            var map = new JObject();
            var bb = new BoundingBoxXYZ { Min = new XYZ(double.MaxValue, double.MaxValue, double.MaxValue),
                                          Max = new XYZ(double.MinValue, double.MinValue, double.MinValue) };
            int count = 0;
            int boundsContributors = 0;

            foreach (var el in elements)
            {
                var guid = el.UniqueId;
                var tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

                // Track bounds from EVERY element with a bounding box, not just
                // tagged ones. Otherwise, on a fresh project where the tag
                // pipeline hasn't run, we get zero contributors and the bb stays
                // at sentinel (MaxValue/MinValue), which overflows to ±Infinity
                // when scaled to mm and the server rejects with HTTP 400.
                var eb = el.get_BoundingBox(null);
                if (eb != null)
                {
                    bb.Min = new XYZ(Math.Min(bb.Min.X, eb.Min.X),
                                     Math.Min(bb.Min.Y, eb.Min.Y),
                                     Math.Min(bb.Min.Z, eb.Min.Z));
                    bb.Max = new XYZ(Math.Max(bb.Max.X, eb.Max.X),
                                     Math.Max(bb.Max.Y, eb.Max.Y),
                                     Math.Max(bb.Max.Z, eb.Max.Z));
                    boundsContributors++;
                }

                if (string.IsNullOrEmpty(tag))
                {
                    // PUBLISH-WHOLE-MODEL — emit a minimal entry for every
                    // element with geometry so the viewer's tree, discipline
                    // chips, level strip, and properties panel work end-to-end
                    // even on models that haven't been through the STING tag
                    // pipeline yet. Tagged elements get the rich block below;
                    // untagged ones still get name + category + level + elementId
                    // which is what the right-panel Properties tab needs.
                    string lvlOnly = "";
                    try { lvlOnly = ParameterHelpers.GetLevelCode(doc, el) ?? ""; } catch { }
                    var untaggedEntry = new JObject
                    {
                        ["name"]      = el.Name ?? "",
                        ["category"]  = el.Category?.Name ?? "",
                        // M3 — derive a discipline from the Revit category so the viewer's
                        // BY DISCIPLINE / colour-by-discipline work on as-built (untagged) models.
                        ["discipline"] = DeriveDisciplineFromCategory(el.Category?.Name),
                        ["level"]     = lvlOnly,
                        ["elementId"] = el.Id.Value,
                    };
                    AddCost(el, untaggedEntry);   // M3 — per-element cost (rate × measured qty)
                    AddQuantitiesAndMaterials(doc, el, untaggedEntry);   // E4 — area/volume/length + materials
                    map[guid] = untaggedEntry;
                    count++;
                    continue;
                }
                var disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                var loc  = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                var lvl  = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                var sys  = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                var stat = ParameterHelpers.GetString(el, ParamRegistry.STATUS);

                var taggedEntry = new JObject
                {
                    ["tag"]        = tag ?? "",
                    ["name"]       = el.Name ?? "",
                    ["category"]   = el.Category?.Name ?? "",
                    // Fall back to a category-derived discipline if the DISC token is blank.
                    ["discipline"] = string.IsNullOrWhiteSpace(disc) ? DeriveDisciplineFromCategory(el.Category?.Name) : disc,
                    ["location"]   = loc,
                    ["level"]      = lvl,
                    ["system"]     = sys,
                    ["status"]     = stat,
                    ["elementId"]  = el.Id.Value,
                };
                AddCost(el, taggedEntry);     // M3 — per-element cost
                AddQuantitiesAndMaterials(doc, el, taggedEntry);   // E4 — area/volume/length + materials
                map[guid] = taggedEntry;
                count++;
            }

            // Convert feet → mm for the bounds (Revit internal units are feet).
            // If nothing contributed bounds (e.g. empty 3D view), send zeros so
            // the server's [Range] validators don't see ±Infinity.
            const double feetToMm = 304.8;
            bounds = boundsContributors > 0
                ? new[]
                {
                    bb.Min.X * feetToMm, bb.Min.Y * feetToMm, bb.Min.Z * feetToMm,
                    bb.Max.X * feetToMm, bb.Max.Y * feetToMm, bb.Max.Z * feetToMm,
                }
                : new[] { 0d, 0d, 0d, 0d, 0d, 0d };
            elementCount = count;

            File.WriteAllText(outputPath, map.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
        }

        /// <summary>
        /// M3 — write per-element cost into the element-map entry. Cost = unit rate
        /// (ASS_CST_UNIT_RATE_NR) × measured quantity (volume m³ / area m² / length m,
        /// whichever the element exposes). When no rate is set, nothing is written
        /// (the viewer shows "—" — never a fabricated number). Currency from
        /// ASS_CST_CURRENCY_TXT when present.
        /// </summary>
        private static void AddCost(Element el, JObject entry)
        {
            try
            {
                var rateStr = ParameterHelpers.GetString(el, ParamRegistry.CST_UNIT_RATE_NR);
                if (!double.TryParse(rateStr, out var rate) || rate <= 0) return;
                double qty = MeasuredQuantity(el);
                double cost = qty > 0 ? rate * qty : rate;   // no measurable qty ⇒ rate is the line cost
                entry["cost"] = Math.Round(cost, 2);
                var cur = ParameterHelpers.GetString(el, ParamRegistry.CST_CURRENCY_TXT);
                if (!string.IsNullOrWhiteSpace(cur)) entry["costCurrency"] = cur;
            }
            catch { /* cost is best-effort; never block the publish */ }
        }

        /// <summary>
        /// E4 — emit per-element quantities (area m² / volume m³ / length m) and a
        /// per-material breakdown (name + area + volume) into the element-map entry so
        /// the viewer's Properties → Materials / Quantities sections populate. All
        /// best-effort + metric; absent values are simply not written (the client only
        /// renders the sections when present).
        /// </summary>
        private static void AddQuantitiesAndMaterials(Document doc, Element el, JObject entry)
        {
            const double ft3 = 0.0283168, ft2 = 0.092903, ft = 0.3048;
            try
            {
                Parameter p;
                if ((p = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)) != null && p.HasValue && p.AsDouble() > 0) entry["volume"] = Math.Round(p.AsDouble() * ft3, 3);
                if ((p = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED))   != null && p.HasValue && p.AsDouble() > 0) entry["area"]   = Math.Round(p.AsDouble() * ft2, 3);
                if ((p = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH))    != null && p.HasValue && p.AsDouble() > 0) entry["length"] = Math.Round(p.AsDouble() * ft, 3);
            }
            catch { }
            try
            {
                var mats = new JArray();
                foreach (ElementId mid in el.GetMaterialIds(false))
                {
                    if (!(doc.GetElement(mid) is Material m)) continue;
                    var mo = new JObject { ["name"] = m.Name ?? "" };
                    try { double a = el.GetMaterialArea(mid, false); if (a > 0) mo["area"]   = Math.Round(a * ft2, 3); } catch { }
                    try { double v = el.GetMaterialVolume(mid);      if (v > 0) mo["volume"] = Math.Round(v * ft3, 3); } catch { }
                    mats.Add(mo);
                }
                if (mats.Count > 0) entry["materials"] = mats;
            }
            catch { }
        }

        /// <summary>Primary measured quantity in metric: volume (m³) → area (m²) → length (m).</summary>
        private static double MeasuredQuantity(Element el)
        {
            const double ft3 = 0.0283168, ft2 = 0.092903, ft = 0.3048;
            Parameter p;
            if ((p = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)) != null && p.HasValue && p.AsDouble() > 0) return p.AsDouble() * ft3;
            if ((p = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED))   != null && p.HasValue && p.AsDouble() > 0) return p.AsDouble() * ft2;
            if ((p = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH))    != null && p.HasValue && p.AsDouble() > 0) return p.AsDouble() * ft;
            return 0;
        }

        /// <summary>
        /// M3 — map a Revit category name to an ISO discipline code so the viewer's
        /// BY DISCIPLINE / colour-by-discipline / presets populate on as-built models
        /// that never went through the STING tag pipeline. Mirrors the client discOf().
        /// </summary>
        private static string DeriveDisciplineFromCategory(string cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return "";
            var c = cat.ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"duct|air\s*terminal|diffuser|grille|hvac|vav|ahu|fcu|mechanical|fan|damper")) return "M";
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"pipe|plumb|sanitary|fixture|valve")) return "P";
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"cable|conduit|electric|lighting|light\s*fixture|panel|switch|socket|data|fire\s*alarm|device")) return "E";
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"fire\s*protect|sprinkler")) return "FP";
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"column|beam|brace|footing|foundation|framing|structural|rebar|truss")) return "S";
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"wall|floor|ceiling|roof|door|window|stair|railing|furniture|casework|room|curtain|generic\s*model|topograph")) return "A";
            return "";
        }

        /// <summary>
        /// Strip OS-illegal filename chars plus the curly braces Revit uses for its
        /// auto-generated "{3D}" default-view name. Collapses runs of whitespace and
        /// trims leading/trailing junk so the result is filesystem-clean.
        /// </summary>
        private static string SanitiseFilenameSegment(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "model";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (c == '{' || c == '}') continue;
                if (Path.GetInvalidFileNameChars().Contains(c)) { sb.Append('_'); continue; }
                sb.Append(c);
            }
            string cleaned = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            return string.IsNullOrEmpty(cleaned) ? "model" : cleaned;
        }

        private static string DetectDocDiscipline(Document doc)
        {
            // Heuristic: look at the document name for a discipline prefix.
            var name = doc.Title.ToUpperInvariant();
            if (name.Contains("MECH") || name.StartsWith("M-") || name.StartsWith("M_")) return "M";
            if (name.Contains("ELEC") || name.StartsWith("E-") || name.StartsWith("E_")) return "E";
            if (name.Contains("PLUMB") || name.StartsWith("P-") || name.StartsWith("P_")) return "P";
            if (name.Contains("STRUCT") || name.StartsWith("S-") || name.StartsWith("S_")) return "S";
            if (name.Contains("ARCH") || name.StartsWith("A-") || name.StartsWith("A_")) return "A";
            if (name.Contains("FIRE") || name.StartsWith("FP-")) return "FP";
            return "";
        }
    }
}
