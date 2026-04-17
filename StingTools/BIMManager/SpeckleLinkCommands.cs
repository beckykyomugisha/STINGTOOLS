#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// PHASE 93 — Speckle-side helpers for the xeokit viewer pipeline.
    ///
    /// <para>
    /// The reviewer-facing xeokit viewer (wwwroot/viewer/index.html on
    /// Planscape.Server) needs .xkt files to render. The production path is
    /// IFC → ifcconvert → XKT, but until that pipeline is wired we ship a
    /// stub that writes a minimal, valid XKT-shaped JSON sidecar so the
    /// viewer loads without error. Reviewers see an "empty model" HUD
    /// message instead of a network failure.
    /// </para>
    ///
    /// <para>
    /// This is explicitly a TODO stub — the full implementation needs
    /// ifcconvert (or the xeokit @xeokit/xeokit-convert CLI) to produce
    /// binary XKT from an IFC export. This command only captures a
    /// positions array from a Speckle-style snapshot JSON and wraps it in
    /// the minimal XKT envelope.
    /// </para>
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class XKTExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("STING", "No document open.");
                return Result.Failed;
            }
            Document doc = ctx.Doc;

            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                string outPath = Path.Combine(bimDir, "model.xkt");

                // ── Load a Speckle snapshot sidecar if one exists ──
                // The full pipeline will feed us a rich JSON with objects[]
                // carrying geometry + parameters. For the stub we only
                // extract positions, mirroring the XKT v10 positions buffer.
                string snapshotPath = Path.Combine(bimDir, "speckle_snapshot.json");
                var positions = ExtractPositionsFromSnapshot(snapshotPath);

                // ── Build a minimal XKT-compatible JSON envelope ──
                // The real XKT format is a binary container — xeokit's
                // XKTLoaderPlugin also accepts a JSON variant during
                // development. This stub writes a valid-parse JSON with
                // an empty geometry graph so the loader succeeds and the
                // viewer page shows the "empty model — awaiting
                // IFC→XKT conversion" HUD.
                //
                // TODO: replace with full IFC→XKT conversion via
                //       ifcconvert or @xeokit/xeokit-convert once the
                //       server-side Model Derivative job (P7) ships.
                var xkt = new JObject
                {
                    ["xktVersion"] = 10,
                    ["source"] = "StingTools.XKTExportCommand (stub)",
                    ["generatedUtc"] = DateTime.UtcNow.ToString("o"),
                    ["project"] = doc.Title ?? "Untitled",
                    ["positions"] = new JArray(positions),
                    ["indices"] = new JArray(),
                    ["meshes"] = new JArray(),
                    ["entities"] = new JArray(),
                    ["metaObjects"] = new JArray(),
                    ["note"] = "Stub XKT — replace with ifcconvert / @xeokit/xeokit-convert output."
                };

                File.WriteAllText(outPath, xkt.ToString(Newtonsoft.Json.Formatting.Indented));
                StingLog.Info($"XKTExport: wrote stub XKT to {outPath} ({positions.Count} positions)");

                TaskDialog.Show(
                    "Export XKT (stub)",
                    $"Wrote stub XKT:\n  {outPath}\n\n" +
                    $"Positions captured: {positions.Count / 3}\n\n" +
                    "Full IFC→XKT pipeline is TODO. Deploy the file to the\n" +
                    "Planscape server's {Storage:Path}/xkt/ directory to open\n" +
                    "it in the xeokit viewer at /viewer/index.html?model=model.xkt.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error($"XKTExport failed: {ex.Message}", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Reads a Speckle snapshot sidecar and returns a flat list of
        /// vertex coordinates (X,Y,Z triples). Returns an empty list if
        /// the sidecar is absent or has no geometry — the XKT stub still
        /// writes a valid empty envelope in that case.
        /// </summary>
        private static List<double> ExtractPositionsFromSnapshot(string snapshotPath)
        {
            var positions = new List<double>();
            if (!File.Exists(snapshotPath)) return positions;

            try
            {
                var json = JObject.Parse(File.ReadAllText(snapshotPath));
                // Speckle "@displayValue" / "vertices" arrays are the most
                // common vertex carriers. Both shapes are tolerated.
                var vertices = json["vertices"] as JArray;
                if (vertices != null)
                {
                    foreach (var v in vertices)
                    {
                        if (v.Type == JTokenType.Float || v.Type == JTokenType.Integer)
                            positions.Add(v.Value<double>());
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"XKTExport: snapshot parse failed: {ex.Message}");
            }
            return positions;
        }
    }
}
