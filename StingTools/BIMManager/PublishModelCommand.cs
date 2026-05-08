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

            // ── Step 3: pick or export geometry ────────────────────────
            var modelPath = PromptForModelFileOrExport(doc);
            if (string.IsNullOrEmpty(modelPath)) return Result.Cancelled;

            // ── Step 4: collect element map ────────────────────────────
            string? mapPath = null;
            try
            {
                mapPath = Path.Combine(
                    OutputLocationHelper.GetOutputDirectory(doc),
                    Path.GetFileNameWithoutExtension(modelPath) + "-elements.json");
                BuildElementMap(doc, mapPath, out var elementCount, out var bounds);
                StingLog.Info($"Planscape: element map generated ({elementCount} elements) → {mapPath}");

                // ── Step 5: upload to server ───────────────────────────
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
                    bounds: bounds)).GetAwaiter().GetResult();

                if (!result.ok)
                {
                    TaskDialog.Show("Publish Model", $"Upload failed: {result.error}");
                    StingLog.Warn($"Planscape: model upload failed — {result.error}");
                    return Result.Failed;
                }

                if (result.alreadyExisted)
                {
                    // Modern server (commits 1b7ff61+) refreshes the element-
                    // map / thumbnail / bounds / revision on the existing row
                    // even when the GLB hash is identical, so this branch is
                    // now the "re-publish to refresh sidecars" success path,
                    // not a dead-end. Coordinators expect re-publishing to
                    // pick up new tagging / new map data.
                    TaskDialog.Show(
                        "Publish Model to Planscape",
                        $"Geometry already published — element-map and metadata refreshed on the existing entry.\n\n" +
                        $"File: {Path.GetFileName(modelPath)}\n" +
                        $"Project: {projectId}\n" +
                        $"Model id: {result.modelId}\n\n" +
                        "The viewer + mobile app will pick up the new element-map on next open. " +
                        "To create a NEW revision instead of refreshing, change the geometry first " +
                        "(re-export the 3D view) and re-publish.");
                    StingLog.Info($"Planscape: model sidecars refreshed (dedup) → {result.modelId}");
                    return Result.Succeeded;
                }

                TaskDialog.Show(
                    "Publish Model to Planscape",
                    $"Published {Path.GetFileName(modelPath)}\n\n" +
                    $"Elements published: {elementCount}\n" +
                    $"Project: {projectId}\n" +
                    $"Model id:   {result.modelId}\n\n" +
                    "Site users can now open the model from the Planscape mobile app → Models.");
                StingLog.Info($"Planscape: model published → {result.modelId}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Publish Model", $"Failed: {ex.Message}");
                StingLog.Error("Planscape: publish model failed", ex);
                return Result.Failed;
            }
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
                "Export current 3D view to GLB",
                "Uses the built-in STING glTF exporter. Active view must be a 3D view.");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Pick an existing file (.glb / .gltf / .ifc / .obj / .fbx)",
                "Use a file produced by APS, SimLab, rvt2gltf, Dynamo, etc.");
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
            var outPath = Path.Combine(
                OutputLocationHelper.GetOutputDirectory(doc),
                $"{Path.GetFileNameWithoutExtension(doc.PathName ?? doc.Title)}-{v3d.Name}.glb");
            try
            {
                var result = RevitGltfExporter.Export(doc, v3d, outPath);
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
                Filter = "3D models (*.glb;*.gltf;*.ifc;*.obj;*.fbx)|*.glb;*.gltf;*.ifc;*.obj;*.fbx|All files (*.*)|*.*",
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
                    map[guid] = new JObject
                    {
                        ["name"]      = el.Name ?? "",
                        ["category"]  = el.Category?.Name ?? "",
                        ["level"]     = lvlOnly,
                        ["elementId"] = el.Id.Value,
                    };
                    count++;
                    continue;
                }
                var disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                var loc  = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                var lvl  = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                var sys  = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                var stat = ParameterHelpers.GetString(el, ParamRegistry.STATUS);

                map[guid] = new JObject
                {
                    ["tag"]        = tag ?? "",
                    ["name"]       = el.Name ?? "",
                    ["category"]   = el.Category?.Name ?? "",
                    ["discipline"] = disc,
                    ["location"]   = loc,
                    ["level"]      = lvl,
                    ["system"]     = sys,
                    ["status"]     = stat,
                    ["elementId"]  = el.Id.Value,
                };
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
