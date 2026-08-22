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
            var (projectId, projectName, projectCode) = PickProject(client);
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

                Result result = mode switch
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

                // ── Step 6: link the model to the project it published into ──
                // Publishing IS an explicit "this model belongs to this project"
                // statement, so persist the link per-document (and set the
                // in-memory CurrentProjectId) the moment a publish succeeds. This
                // is what lets the invite path, PluginSyncTickBridge, BOQ sync and
                // the BCC header all recognise the model as linked without a
                // separate "Link to project" step.
                if (result == Result.Succeeded)
                {
                    try
                    {
                        PlanscapeProjectLink.Set(
                            PlanscapeProjectLink.ConfigPathFor(doc),
                            projectId, projectName, projectCode, client.ConnectedUser);
                    }
                    catch (Exception linkEx)
                    {
                        StingLog.Warn($"Planscape: publish succeeded but link persist failed: {linkEx.Message}");
                    }
                }
                return result;
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
            // B2 — send the survey position alongside the geometry so the server
            // can place this model in the federation without a coordinator
            // typing a transform. Null when the document has no project location
            // or its survey point was never moved off the origin, in which case
            // the model publishes exactly as before (at the origin) rather than
            // being placed by a guess.
            var georef = RevitGeoref.Read(doc);
            bool hasGeoref = georef != null && georef.HasSurveyOrigin;
            if (hasGeoref)
            {
                StingLog.Info(
                    $"Planscape: publishing with georef E={georef!.EastingM:F3}m N={georef.NorthingM:F3}m " +
                    $"north={georef.TrueNorthDeg:F3}° crs={georef.CrsEpsg ?? "(undeclared)"}");
                if (string.IsNullOrWhiteSpace(georef.CrsEpsg))
                {
                    // Worth saying out loud: without a CRS the server stores the
                    // transform but will not apply it on its own, so the model
                    // still needs one confirmation. Setting the parameter once
                    // per project removes that step for every future publish.
                    StingLog.Info(
                        $"Planscape: no {RevitGeoref.CrsParamName} on Project Information — the transform will be " +
                        "stored as a suggestion, not auto-applied. Set it once per project to match the project's " +
                        "declared coordinate system on the server.");
                }
            }
            else
            {
                StingLog.Info("Planscape: no survey origin in this document — model will publish un-placed at the project origin.");
            }

            var result = Task.Run(() => client.UploadModelAsync(
                projectId,
                modelPath,
                mapPath,
                name: doc.Title,
                description: $"Published from Revit {doc.Application.VersionName}",
                discipline: DetectDocDiscipline(doc),
                revision: PhaseAutoDetect.DetectProjectRevision(doc),
                units: "m",   // P3 — the GLB vertices are metres (glTF 2.0)
                elementCount: elementCount,
                bounds: bounds,
                force: force,
                georefEastingM:     hasGeoref ? georef!.EastingM     : (double?)null,
                georefNorthingM:    hasGeoref ? georef!.NorthingM    : (double?)null,
                georefElevationM:   hasGeoref ? georef!.ElevationM   : (double?)null,
                georefTrueNorthDeg: hasGeoref ? georef!.TrueNorthDeg : (double?)null,
                georefCrsEpsg:      hasGeoref ? georef!.CrsEpsg      : null,
                georefLengthUnit:   hasGeoref ? "m"                  : null,
                georefExportMode:   hasGeoref ? georef!.ExportMode   : null)).GetAwaiter().GetResult();

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

        private static (Guid id, string name, string code) PickProject(PlanscapeServerClient client)
        {
            var none = (Guid.Empty, "", "");
            var projects = Task.Run(() => client.GetProjectsAsync()).GetAwaiter().GetResult();
            if (projects == null || projects.Count == 0)
            {
                TaskDialog.Show("Publish Model", "No Planscape projects are visible to your account.");
                return none;
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
            if (idx < 0 || idx >= projects.Count) return none;
            if (!Guid.TryParse(projects[idx]["id"]?.Value<string>() ?? "", out var id)) return none;
            return (id,
                    projects[idx]["name"]?.Value<string>() ?? "",
                    projects[idx]["code"]?.Value<string>() ?? "");
        }

        // ── File picker ────────────────────────────────────────────────

        private static string? PromptForModelFileOrExport(Document doc)
        {
            // Reset per publish. This is static state, so without it a run that picks an
            // existing file would inherit the ANSWER FROM THE PREVIOUS RUN and build an
            // element map that disagrees with the geometry — the same
            // meshes-without-properties symptom, arriving by a different route and only
            // on the second publish of a session, which is the worst kind to reproduce.
            // Only ExportActiveView, which actually asks, may set it true.
            _includeLinksThisRun = false;

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
                _includeLinksThisRun = AskIncludeLinks(doc, v3d);
                var result = RevitGltfExporter.Export(doc, v3d, outPath,
                                                      exportTextures: wantTextures,
                                                      includeLinks: _includeLinksThisRun);
                StingLog.Info($"Planscape: GLB exported ({result.ElementCount} elements, {result.FileSizeBytes:N0} bytes) → {outPath}");

                // Say it out loud. The whole reason this option exists is that links were
                // being dropped SILENTLY, and a deliberate default is not a licence to
                // reproduce that — a user who publishes a federated site and gets an empty
                // container must be told why, at the moment it happens.
                if (result.SkippedLinkCount > 0)
                    TaskDialog.Show("Publish Model",
                        $"Published the host model only — {result.SkippedLinkCount} linked model(s) were not included.\n\n"
                      + "If you expected the buildings or site to appear, publish again and choose "
                      + "\"Include linked models\".");
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

        // ── Federation support ─────────────────────────────────────────

        /// <summary>
        /// Whether THIS publish includes linked models. Set by <see cref="AskIncludeLinks"/>
        /// immediately before the geometry export and read by <c>BuildElementMap</c>, so
        /// the GLB and the element map can never disagree about what was published — a
        /// disagreement shows as meshes with no properties, or tree rows with no mesh.
        /// </summary>
        private static bool _includeLinksThisRun;

        /// <summary>
        /// Ask once per publish, and only when it matters.
        ///
        /// <para>No links in the view means no question — a dialog whose answer cannot
        /// change anything is noise. When links ARE present the choice is explicit,
        /// because both answers are reasonable: a discipline publish wants the host alone
        /// (smaller, faster, and the viewer federates published models anyway), while a
        /// site or coordination publish wants one artefact containing everything.</para>
        ///
        /// <para><c>PLANSCAPE_EXPORT_LINKS=1</c> skips the prompt for scripted runs.</para>
        /// </summary>
        private static bool AskIncludeLinks(Document doc, View view)
        {
            int linkCount;
            try
            {
                var lc = view is View3D
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);
                linkCount = lc.OfClass(typeof(RevitLinkInstance)).GetElementCount();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Publish: could not count links — {ex.Message}");
                linkCount = 0;
            }

            if (linkCount == 0) return false;

            if (string.Equals(Environment.GetEnvironmentVariable("PLANSCAPE_EXPORT_LINKS"), "1",
                              StringComparison.OrdinalIgnoreCase)
                || RevitGltfExporter.IncludeLinks)
            {
                StingLog.Info($"Publish: including {linkCount} linked model(s) (set by environment/static override).");
                return true;
            }

            var dlg = new TaskDialog("Publish Model")
            {
                MainInstruction = $"This view contains {linkCount} linked model(s). Include them?",
                MainContent = "Linked models are the buildings, site or discipline models attached to this file.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Host model only  (default)",
                "Publishes just this file. Smaller and faster, and you can publish each linked "
              + "model separately — the viewer federates them and gives you per-model visibility.");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Include linked models",
                "Publishes everything visible in this view as one model — the right choice for a "
              + "federated site or a coordination snapshot. Larger file and a slower export.");
            var r = dlg.Show();
            if (r == TaskDialogResult.CommandLink2)
            {
                StingLog.Info($"Publish: including {linkCount} linked model(s) by user choice.");
                return true;
            }
            StingLog.Info($"Publish: EXCLUDING {linkCount} linked model(s) by user choice (host model only).");
            return false;
        }


        /// <summary>One element to publish, with the document it came from, the transform
        /// into host coordinates, and the key it shares with its GLB mesh node.</summary>
        private readonly struct MapItem
        {
            public MapItem(Document doc, Element el, Transform xf, string key)
            { Doc = doc; El = el; Xf = xf; Key = key; }
            public Document Doc { get; }
            public Element El { get; }
            public Transform Xf { get; }
            public string Key { get; }
        }

        /// <summary>
        /// Every element inside every loaded Revit link, paired with its document and
        /// placement transform.
        ///
        /// <para><b>Scope note, stated plainly:</b> this returns model elements with
        /// geometry from each link, NOT "elements visible in the host's active view".
        /// A view id belongs to the host document, so it cannot filter a linked
        /// document's collector, and per-element visibility across a link is not
        /// something the API answers cheaply. The GLB — which Revit's own view traversal
        /// produces — remains the authority on what is drawn. The practical effect is
        /// that the map can list a few elements the geometry does not show; a tree row
        /// with no mesh is a far smaller problem than the mesh with no properties this
        /// replaces.</para>
        ///
        /// <para>Links that are unloaded, or that fail to resolve, are skipped with a
        /// warning rather than failing the publish — a partial federation is still worth
        /// publishing, and the log says which link was missing.</para>
        /// </summary>
        private static IEnumerable<MapItem> CollectLinkedElements(Document host, View activeView)
        {
            var results = new List<MapItem>();
            // Keyed by document identity so a cycle (A links B links A) terminates, and
            // so the same file linked twice is not collected twice into one map.
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                RevitGltfExporter.LinkScope(host)
            };
            CollectLinksRecursive(host, activeView, Transform.Identity, visited, results, depth: 0);
            return results;
        }

        /// <summary>
        /// Walks links, then links inside those links.
        ///
        /// <para>Revit's own view traversal descends nested links, so the GLB contains
        /// them. A one-level metadata walk would therefore reproduce the exact bug this
        /// change fixes, one level down — geometry present, properties missing — which is
        /// the failure mode hardest to notice because the model looks right.</para>
        ///
        /// <para>Depth is capped and documents are visited once. A cycle is legal in
        /// Revit and would otherwise recurse until the stack gives out.</para>
        /// </summary>
        private static void CollectLinksRecursive(
            Document parent, View activeView, Transform parentXf,
            HashSet<string> visited, List<MapItem> results, int depth)
        {
            const int MaxDepth = 8;
            if (depth >= MaxDepth)
            {
                StingLog.Warn($"Publish: link nesting deeper than {MaxDepth} in '{parent.Title}' — not descending further.");
                return;
            }

            List<RevitLinkInstance> links;
            try
            {
                // At the top level, filter by the active view so links hidden in THIS view
                // are not published — the link INSTANCE is a host element, so unlike
                // per-element visibility this is a question the host view can answer.
                // Deeper down there is no equivalent view, so take them all.
                var lc = (depth == 0 && activeView is View3D)
                    ? new FilteredElementCollector(parent, activeView.Id)
                    : new FilteredElementCollector(parent);
                links = lc.OfClass(typeof(RevitLinkInstance))
                          .Cast<RevitLinkInstance>()
                          .ToList();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Publish: could not enumerate links in '{parent.Title}' — {ex.Message}");
                return;
            }

            foreach (var link in links)
            {
                Document ldoc = null;
                try { ldoc = link.GetLinkDocument(); }
                catch (Exception ex) { StingLog.Warn($"Publish: link '{link.Name}' — {ex.Message}"); }

                if (ldoc == null)
                {
                    // Unloaded link. Named explicitly: silently publishing a federation
                    // minus one building is exactly the failure this whole change fixes.
                    StingLog.Warn($"Publish: link '{link.Name}' is not loaded — its elements are NOT in this publish.");
                    continue;
                }

                Transform xf;
                try { xf = parentXf.Multiply(link.GetTotalTransform() ?? Transform.Identity); }
                catch { xf = parentXf; }

                string scope = RevitGltfExporter.LinkScope(ldoc);
                if (!visited.Add(scope))
                {
                    StingLog.Info($"Publish: link '{ldoc.Title}' already collected — skipping duplicate/cyclic reference.");
                    continue;
                }
                int n = 0;
                try
                {
                    foreach (var e in new FilteredElementCollector(ldoc)
                                          .WhereElementIsNotElementType()
                                          .Where(e => e.Category != null
                                                      && e.Category.CategoryType == CategoryType.Model
                                                      && e.get_Geometry(new Options()) != null))
                    {
                        results.Add(new MapItem(ldoc, e, xf, scope + "|" + e.UniqueId));
                        n++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Publish: collecting from link '{link.Name}' failed — {ex.Message}");
                }
                StingLog.Info($"Publish: link '{ldoc.Title}' contributed {n} element(s).");

                // Descend. The transform accumulates, so a building nested two links deep
                // still lands in host coordinates.
                CollectLinksRecursive(ldoc, activeView, xf, visited, results, depth + 1);
            }
        }

        /// <summary>
        /// The eight transformed corners of a bounding box.
        ///
        /// <para>All eight, not Min and Max. Under rotation the transformed Min is not
        /// the minimum of the transformed box — taking the two corners gives an AABB that
        /// can be smaller than the geometry it is meant to contain, and a link placed at
        /// an angle is the normal case, not an exotic one.</para>
        /// </summary>
        private static IEnumerable<XYZ> Corners(BoundingBoxXYZ b, Transform xf)
        {
            var t = xf ?? Transform.Identity;
            // A BoundingBoxXYZ carries its own Transform; fold it in so a box expressed
            // in a non-identity local frame is read correctly before the link transform.
            var local = b.Transform ?? Transform.Identity;
            for (int i = 0; i < 8; i++)
            {
                var p = new XYZ((i & 1) == 0 ? b.Min.X : b.Max.X,
                                (i & 2) == 0 ? b.Min.Y : b.Max.Y,
                                (i & 4) == 0 ? b.Min.Z : b.Max.Z);
                yield return t.OfPoint(local.OfPoint(p));
            }
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

            // Host elements first, each paired with the document it belongs to and the
            // transform that puts it in host coordinates (identity, by definition).
            var elements = collector
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.get_Geometry(new Options()) != null)
                .Select(e => new MapItem(doc, e, Transform.Identity,
                                         RevitGltfExporter.ElementKey(doc, doc, e)))
                .ToList();

            // …then everything inside the loaded links.
            //
            // Without this the map described the HOST FILE ONLY. On a federated model —
            // where the host is a container and every building is a link — that is a
            // handful of link instances and nothing else, which is why the viewer
            // reported "8 ELEMENTS / 0% TAGGED" for a whole site. The GLB had the same
            // fault in its own way (see RevitGltfExporter's document stack); fixing one
            // without the other gives you either geometry with no properties or
            // properties with no geometry.
            if (_includeLinksThisRun)
                elements.AddRange(CollectLinkedElements(doc, activeView));
            else
                StingLog.Info("Publish: element map covers the host model only (links excluded for this publish).");

            var map = new JObject();
            var bb = new BoundingBoxXYZ { Min = new XYZ(double.MaxValue, double.MaxValue, double.MaxValue),
                                          Max = new XYZ(double.MinValue, double.MinValue, double.MinValue) };
            int count = 0;
            int boundsContributors = 0;
            // A — MEP system capture coverage (mirrors the [tex] SUMMARY).
            int sysResolved = 0, sysUnresolved = 0;
            var sysHist = new Dictionary<string, int>();
            var sysUnresolvedCats = new Dictionary<string, int>();

            foreach (var item in elements)
            {
                var el   = item.El;
                // The element's OWN document. Level lookup, quantities and materials all
                // read from it, and for a linked element the host knows none of them.
                var edoc = item.Doc;
                var guid = item.Key;
                var tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

                // Track bounds from EVERY element with a bounding box, not just
                // tagged ones. Otherwise, on a fresh project where the tag
                // pipeline hasn't run, we get zero contributors and the bb stays
                // at sentinel (MaxValue/MinValue), which overflows to ±Infinity
                // when scaled to mm and the server rejects with HTTP 400.
                var eb = el.get_BoundingBox(null);
                if (eb != null)
                {
                    // A linked element's bounding box is in ITS OWN coordinates. Folded
                    // into the host AABB untransformed, a link with any offset drags the
                    // published bounds out to enclose empty space, and the viewer opens
                    // zoomed to nothing. Transform all eight corners — transforming only
                    // Min/Max is wrong as soon as the link is rotated.
                    foreach (var c in Corners(eb, item.Xf))
                    {
                        bb.Min = new XYZ(Math.Min(bb.Min.X, c.X), Math.Min(bb.Min.Y, c.Y), Math.Min(bb.Min.Z, c.Z));
                        bb.Max = new XYZ(Math.Max(bb.Max.X, c.X), Math.Max(bb.Max.Y, c.Y), Math.Max(bb.Max.Z, c.Z));
                    }
                    boundsContributors++;
                }

                // A — resolve the element's MEP system ONCE (used by both tagged + untagged
                // entries). Non-MEP elements → empty SYS (they just won't colour by System).
                var catName = el.Category?.Name ?? "";
                var (mepSys, sysClass, sysName, isMep) = ResolveMepSystem(el, catName);
                if (!string.IsNullOrEmpty(mepSys))
                {
                    sysResolved++;
                    // Prefix the histogram key with the discipline so the SUMMARY shows the
                    // M / E / P split (e.g. "M:SupplyAir", "E:PowerCircuit", "P:DCW").
                    var dcode = DeriveDisciplineFromCategory(catName);
                    // Item 7 — UNIFORM disc prefix (use "?" when unknown) so buckets don't split
                    // into "P:Domestic Cold Water" vs bare "Domestic Cold Water".
                    var k = (string.IsNullOrEmpty(dcode) ? "?" : dcode) + ":" + (string.IsNullOrEmpty(sysClass) ? mepSys : sysClass);
                    sysHist[k] = sysHist.TryGetValue(k, out var n) ? n + 1 : 1;
                }
                else if (isMep)
                {
                    sysUnresolved++;
                    sysUnresolvedCats[catName] = sysUnresolvedCats.TryGetValue(catName, out var u) ? u + 1 : 1;
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
                    try { lvlOnly = ParameterHelpers.GetLevelCode(edoc, el) ?? ""; } catch { }
                    var untaggedEntry = new JObject
                    {
                        ["name"]      = el.Name ?? "",
                        ["category"]  = el.Category?.Name ?? "",
                        // M3 — derive a discipline from the Revit category so the viewer's
                        // BY DISCIPLINE / colour-by-discipline work on as-built (untagged) models.
                        ["discipline"] = DeriveDisciplineFromCategory(el.Category?.Name ?? ""),
                        ["level"]     = lvlOnly,
                        // A — MEP system (resolved from Revit's MEPSystem at export, since
                        // untagged/as-built elements carry no ASS_SYSTEM_TYPE_TXT token).
                        ["system"]    = mepSys,
                        ["sysClass"]  = sysClass,
                        ["sysName"]   = sysName,
                        ["elementId"] = el.Id.Value,
                    };
                    AddCost(el, untaggedEntry);   // M3 — per-element cost (rate × measured qty)
                    AddQuantitiesAndMaterials(edoc, el, untaggedEntry);   // E4 — area/volume/length + materials
                    map[guid] = untaggedEntry;
                    count++;
                    continue;
                }
                var disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                var loc  = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                var lvl  = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                var sys  = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                var stat = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                // P1 — the remaining ISO 19650 tokens so the viewer's "ISO 19650 Tag" group
                // shows all 8 (DISC·LOC·ZONE·LVL·SYS·FUNC·PROD·SEQ) instead of hinting them.
                var zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                var func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
                var prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
                var seq  = ParameterHelpers.GetString(el, ParamRegistry.SEQ);

                var taggedEntry = new JObject
                {
                    ["tag"]        = tag ?? "",
                    ["name"]       = el.Name ?? "",
                    ["category"]   = el.Category?.Name ?? "",
                    // Fall back to a category-derived discipline if the DISC token is blank.
                    ["discipline"] = string.IsNullOrWhiteSpace(disc) ? DeriveDisciplineFromCategory(el.Category?.Name ?? "") : disc,
                    ["location"]   = loc,
                    ["zone"]       = zone,
                    ["level"]      = lvl,
                    // A — stamped SYS token wins; else the system resolved from Revit's MEPSystem.
                    ["system"]     = string.IsNullOrWhiteSpace(sys) ? mepSys : sys,
                    ["sysClass"]   = sysClass,
                    ["sysName"]    = sysName,
                    ["func"]       = func,
                    ["prod"]       = prod,
                    ["seq"]        = seq,
                    ["status"]     = stat,
                    ["elementId"]  = el.Id.Value,
                };
                AddCost(el, taggedEntry);     // M3 — per-element cost
                AddQuantitiesAndMaterials(edoc, el, taggedEntry);   // E4 — area/volume/length + materials
                map[guid] = taggedEntry;
                count++;
            }

            // A — [sys] coverage SUMMARY (mirror of [tex]): so a re-publish shows how many
            // elements got a SYS, the per-classification histogram, and which categories of
            // MEP elements fell through unresolved.
            try
            {
                var hist = string.Join(" ", sysHist.OrderByDescending(kv => kv.Value)
                    .Take(24).Select(kv => kv.Key + "=" + kv.Value));
                StingLog.Info($"[sys] SUMMARY resolved={sysResolved} unresolved={sysUnresolved} | {hist}");
                if (sysUnresolved > 0)
                    StingLog.Info("[sys] unresolved by category: " + string.Join(" ", sysUnresolvedCats
                        .OrderByDescending(kv => kv.Value).Take(24).Select(kv => kv.Key + "=" + kv.Value)));
            }
            catch (Exception ex) { StingLog.Warn($"[sys] summary failed: {ex.Message}"); }

            // P3 — bounds are in METRES, matching the GLB vertices.
            //
            // These describe the same geometry the exporter writes, so they have
            // to use the same unit or the manifest AABB disagrees with what is
            // rendered. Both were millimetres before; both are metres now,
            // because glTF 2.0 defines metres as the unit for linear distances
            // and the other GLB writer in this repo (GlbSerializer) already
            // complied. Changing one without the other would swap a visible
            // 1000x scale bug for an invisible bounds one.
            //
            // If nothing contributed bounds (e.g. empty 3D view), send zeros so
            // the server's [Range] validators don't see ±Infinity.
            const double feetToMetres = 0.3048;
            bounds = boundsContributors > 0
                ? new[]
                {
                    bb.Min.X * feetToMetres, bb.Min.Y * feetToMetres, bb.Min.Z * feetToMetres,
                    bb.Max.X * feetToMetres, bb.Max.Y * feetToMetres, bb.Max.Z * feetToMetres,
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
        // A1 — root discipline classification. MUST mirror the viewer's discOf() RULES
        // (coordination-viewer.js): ORDER MATTERS — Electrical BEFORE Plumbing (so
        // "Lighting Fixtures" never falls under a bare-"fixture" plumbing rule), Fire
        // protection before Plumbing, Plumbing made SPECIFIC (never bare "fixture"),
        // Toposolid/site → Architectural. Keep this in sync with discOf on changes.
        // A — resolve an element's MEP system → (STING SYS code, raw classification, instance
        // name, isMep). MEPCurve uses .MEPSystem; fittings/fixtures/accessories walk connectors.
        // Element-level RBS_SYSTEM_CLASSIFICATION_PARAM / RBS_SYSTEM_NAME_PARAM are read first
        // (Revit exposes them directly on MEP elements). Non-MEP → ("","","",false). Never throws.
        private static (string sys, string sysClass, string sysName, bool isMep) ResolveMepSystem(Element el, string categoryName)
        {
            string sysClass = "", sysName = "";
            try { var pc = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM); if (pc != null) sysClass = pc.AsValueString() ?? pc.AsString() ?? ""; } catch { }
            try { var pn = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM); if (pn != null) sysName = pn.AsString() ?? pn.AsValueString() ?? ""; } catch { }
            bool isMep = el is MEPCurve;
            if (string.IsNullOrEmpty(sysClass) || string.IsNullOrEmpty(sysName))
            {
                try
                {
                    MEPSystem mep = null;
                    if (el is MEPCurve mc) mep = mc.MEPSystem;
                    else if (el is FamilyInstance fi && fi.MEPModel != null)
                    {
                        isMep = true;
                        var cm = fi.MEPModel.ConnectorManager;
                        if (cm != null)
                        {
                            foreach (Connector c in cm.Connectors)
                            {
                                try { if (c.MEPSystem != null) { mep = c.MEPSystem; break; } } catch { }
                            }
                        }
                    }
                    if (mep != null)
                    {
                        isMep = true;
                        if (string.IsNullOrEmpty(sysName)) sysName = mep.Name ?? "";
                        if (string.IsNullOrEmpty(sysClass))
                        {
                            try { var p = mep.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM); if (p != null) sysClass = p.AsValueString() ?? p.AsString() ?? ""; } catch { }
                        }
                    }
                }
                catch { }
            }
            // Electrical — devices/fixtures have NO MEPSystem; pull the assigned circuit's
            // SystemType (PowerCircuit/Data/Telephone/Security/FireAlarm/Communication/Controls)
            // + name from the electrical system(s).
            if (string.IsNullOrEmpty(sysClass) && el is FamilyInstance efi && efi.MEPModel != null)
            {
                try
                {
                    System.Collections.Generic.ISet<Autodesk.Revit.DB.Electrical.ElectricalSystem> esets = null;
                    try { esets = efi.MEPModel.GetElectricalSystems(); } catch { }
                    if (esets == null || esets.Count == 0) { try { esets = efi.MEPModel.GetAssignedElectricalSystems(); } catch { } }
                    if (esets != null)
                    {
                        foreach (var es in esets)
                        {
                            if (es == null) continue;
                            isMep = true;
                            if (string.IsNullOrEmpty(sysName)) sysName = es.Name ?? "";
                            try { sysClass = es.SystemType.ToString(); } catch { }
                            break;
                        }
                    }
                }
                catch { }
            }
            // Containment (conduit / cable tray) + other MEP categories carry no system object —
            // mark them MEP so the category-based SYS code (LV/ICT/…) is emitted, not left blank.
            if (!isMep && !string.IsNullOrEmpty(categoryName) &&
                System.Text.RegularExpressions.Regex.IsMatch(categoryName,
                    @"conduit|cable\s*tray|duct|pipe|plumb|sprinkler|fire\s*alarm|electric|lighting|luminaire|\bdata\b|telephon|communicat|security|nurse\s*call|\bwire\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                isMep = true;
            if (!string.IsNullOrEmpty(sysClass) || !string.IsNullOrEmpty(sysName)) isMep = true;
            // Item 7 — collapse a multi-system classification (comma-joined, possibly duplicated,
            // e.g. "Power,Domestic Cold Water,Domestic Cold Water") to ONE canonical class so the
            // viewer palette + [sys] buckets don't fragment.
            if (!string.IsNullOrEmpty(sysClass) && sysClass.IndexOf(',') >= 0)
            {
                var parts = sysClass.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();
                if (parts.Count > 0) sysClass = parts[0];
            }
            string sys = "";
            if (isMep) { try { sys = TagConfig.GetMepSystemAwareSysCode(el, categoryName) ?? ""; } catch { } }
            return (sys, sysClass, sysName, isMep);
        }

        private static string DeriveDisciplineFromCategory(string cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return "";
            var c = cat.ToLowerInvariant();
            bool Rx(string p) => System.Text.RegularExpressions.Regex.IsMatch(c, p);
            // Mechanical / HVAC
            if (Rx(@"duct|air\s*terminal|diffuser|grille|hvac|\bvav\b|\bahu\b|\bfcu\b|mechanical|\bfan\b|damper|air\s*handl|chiller|\bboiler\b|cooling\s*tower")) return "M";
            // Electrical (incl. lighting + comms/data + fire-alarm) — BEFORE plumbing.
            if (Rx(@"electric|lighting|luminaire|light\s*fixture|\bconduit|cable\s*tray|\bcable\b|\bwire\b|\bdata\b|fire\s*alarm|communicat|security\s*device|nurse\s*call|telephon|\bswitch\b|socket|receptacle|panelboard|distribution\s*board|busway|bus\s*duct")) return "E";
            // Fire protection — BEFORE plumbing (sprinklers / standpipes / hydrants).
            if (Rx(@"sprinkler|fire\s*protect|fire\s*supp|fire\s*pump|standpipe|hydrant")) return "FP";
            // Plumbing / public health — SPECIFIC; never bare "fixture".
            if (Rx(@"plumb|sanitary|water\s*closet|\bwc\b|lavatory|urinal|\bbasin\b|\bsink\b|cistern|\bsoil\b|\bwaste\b|drainage|\bpipe|\bvalve\b|\btap\b|cold\s*water|hot\s*water|rainwater|\bgully\b")) return "P";
            // Structural
            if (Rx(@"column|\bbeam\b|brace|footing|foundation|framing|structural|rebar|truss|slab\s*edge|\bpile\b")) return "S";
            // Architectural (building-element catch-all incl. toposolid/site)
            if (Rx(@"wall|floor|ceiling|roof|door|window|stair|railing|handrail|furniture|casework|\broom\b|curtain|generic\s*model|toposolid|topograph|planting|\bsite\b|\bmass\b|parking|\bramp\b|\bpad\b|grading")) return "A";
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
