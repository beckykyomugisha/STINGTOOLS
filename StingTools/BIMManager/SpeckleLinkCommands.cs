using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING Speckle Link — Snapshot-Based Speckle Integration (Phase 6a, engine)
    //
    //  Serializes tagged elements to a local JSON snapshot in
    //  STING_BIM_MANAGER/speckle_snapshot.json. HTTP push/pull to a Speckle
    //  server is stubbed out pending Speckle SDK v2 integration.
    //
    //  Pattern: matches PlatformLinkEngine (internal static class + atomic
    //  temp-file + File.Move write pattern from StructuralCADPipeline, CLAUDE.md
    //  §683).
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DTO for a single tagged element in a Speckle snapshot. Kept flat and
    /// JSON-serialisable so the snapshot can round-trip through Speckle or any
    /// third-party JSON consumer.
    /// </summary>
    internal class SpeckleElementDto
    {
        public string ElementId { get; set; }
        public string Tag1 { get; set; }
        public string Tag2 { get; set; }
        public string Tag3 { get; set; }
        public string CategoryName { get; set; }
        public string FamilyName { get; set; }
        public DateTime ExportedAt { get; set; }
    }

    #region ── Internal Engine: SpeckleLinkEngine ──

    internal static class SpeckleLinkEngine
    {
        // Snapshot filename persisted alongside project in STING_BIM_MANAGER/
        private const string SnapshotFileName = "speckle_snapshot.json";

        /// <summary>
        /// Collect all elements that carry a non-empty STING_TAG1, project them
        /// to <see cref="SpeckleElementDto"/>, and persist the list to
        /// STING_BIM_MANAGER/speckle_snapshot.json using the atomic temp+Move
        /// write pattern. HTTP push to the Speckle stream is deferred until
        /// the Speckle SDK v2 is added to the project.
        /// </summary>
        internal static void SendToSpeckle(Document doc, string streamUrl, string token)
        {
            if (doc == null)
            {
                StingLog.Error("Speckle: SendToSpeckle called with null document");
                return;
            }

            int count = 0;
            try
            {
                var dtos = CollectTaggedDtos(doc);
                count = dtos.Count;

                string json = JsonConvert.SerializeObject(dtos, Formatting.Indented);
                string snapshotPath = Path.Combine(
                    BIMManagerEngine.GetBIMManagerDir(doc), SnapshotFileName);

                // Atomic write: temp file + File.Move (overwrite). Copied from
                // StructuralCADPipeline sidecar pattern (CLAUDE.md §683) — prevents
                // a corrupt snapshot if the process crashes mid-write.
                string tempPath = snapshotPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, snapshotPath, true);

                StingLog.Info($"Speckle: exported {count} elements to snapshot");
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: SendToSpeckle failed while writing snapshot", ex);
                TaskDialog.Show("Speckle Send", $"Snapshot save failed:\n{ex.Message}");
                return;
            }

            // TODO: HTTP push to streamUrl — Speckle SDK v2 not yet added.
            //       When integrated, POST dtos as a Speckle commit to the stream
            //       identified by streamUrl, authenticated with the provided token.
            try
            {
                TaskDialog.Show("Speckle Send",
                    $"Snapshot saved — {count} elements.\n(Server push pending SDK integration.)");
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: SendToSpeckle post-write dialog failed", ex);
            }
        }

        /// <summary>
        /// Load the JSON snapshot from STING_BIM_MANAGER/speckle_snapshot.json
        /// and return the deserialised DTO list. Returns an empty list if the
        /// snapshot does not exist. HTTP pull from the Speckle stream is
        /// deferred until the Speckle SDK v2 is added.
        /// </summary>
        internal static List<SpeckleElementDto> ReceiveFromSpeckle(
            Document doc, string streamUrl, string token)
        {
            var result = new List<SpeckleElementDto>();
            if (doc == null)
            {
                StingLog.Error("Speckle: ReceiveFromSpeckle called with null document");
                return result;
            }

            try
            {
                string snapshotPath = Path.Combine(
                    BIMManagerEngine.GetBIMManagerDir(doc), SnapshotFileName);
                if (!File.Exists(snapshotPath))
                {
                    StingLog.Info("Speckle: loaded 0 elements from snapshot (file not found)");
                    return result;
                }

                string json = File.ReadAllText(snapshotPath);
                var parsed = JsonConvert.DeserializeObject<List<SpeckleElementDto>>(json);
                if (parsed != null) result = parsed;

                StingLog.Info($"Speckle: loaded {result.Count} elements from snapshot");
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: ReceiveFromSpeckle failed while reading snapshot", ex);
                return new List<SpeckleElementDto>();
            }

            // TODO: HTTP pull from streamUrl — Speckle SDK v2 not yet added.
            //       When integrated, fetch the latest commit from the stream
            //       identified by streamUrl (authenticated with token), merge
            //       with the local snapshot, and return the combined list.

            return result;
        }

        /// <summary>
        /// Compare the persisted snapshot against the current model's tagged
        /// elements and return (Added, Removed, Changed) counts. Matching is
        /// done on <see cref="SpeckleElementDto.ElementId"/> string equality.
        /// "Changed" means same ElementId but one of Tag1/Tag2/Tag3/Category/
        /// Family differs between snapshot and current model.
        /// </summary>
        internal static (int Added, int Removed, int Changed) DiffSnapshot(Document doc)
        {
            int added = 0, removed = 0, changed = 0;
            if (doc == null)
            {
                StingLog.Error("Speckle: DiffSnapshot called with null document");
                return (0, 0, 0);
            }

            try
            {
                var snapshot = ReceiveFromSpeckle(doc, "", "");
                var current = CollectTaggedDtos(doc);

                var snapshotById = new Dictionary<string, SpeckleElementDto>(StringComparer.Ordinal);
                foreach (var dto in snapshot)
                {
                    if (dto == null || string.IsNullOrEmpty(dto.ElementId)) continue;
                    snapshotById[dto.ElementId] = dto;
                }

                var currentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var dto in current)
                {
                    if (dto == null || string.IsNullOrEmpty(dto.ElementId)) continue;
                    currentIds.Add(dto.ElementId);

                    if (!snapshotById.TryGetValue(dto.ElementId, out var prev))
                    {
                        added++;
                        continue;
                    }

                    if (!TokenEquals(prev.Tag1, dto.Tag1) ||
                        !TokenEquals(prev.Tag2, dto.Tag2) ||
                        !TokenEquals(prev.Tag3, dto.Tag3) ||
                        !TokenEquals(prev.CategoryName, dto.CategoryName) ||
                        !TokenEquals(prev.FamilyName, dto.FamilyName))
                    {
                        changed++;
                    }
                }

                foreach (string snapId in snapshotById.Keys)
                {
                    if (!currentIds.Contains(snapId)) removed++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: DiffSnapshot failed", ex);
                return (0, 0, 0);
            }

            StingLog.Info($"Speckle diff: +{added} -{removed} ~{changed}");
            return (added, removed, changed);
        }

        // ── Internal helpers ───────────────────────────────────────────────

        /// <summary>
        /// Collect all elements where STING_TAG1 is non-empty and project to
        /// <see cref="SpeckleElementDto"/>. Uses FilteredElementCollector with
        /// WhereElementIsNotElementType() to match the Excel/Platform engines.
        /// </summary>
        private static List<SpeckleElementDto> CollectTaggedDtos(Document doc)
        {
            var dtos = new List<SpeckleElementDto>();
            DateTime stamp = DateTime.UtcNow;

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                foreach (Element el in collector)
                {
                    if (el == null) continue;

                    string tag1 = ParameterHelpers.GetString(el, "STING_TAG1");
                    if (string.IsNullOrEmpty(tag1)) continue;

                    dtos.Add(new SpeckleElementDto
                    {
                        ElementId = el.Id.Value.ToString(),
                        Tag1 = tag1,
                        Tag2 = ParameterHelpers.GetString(el, "STING_TAG2"),
                        Tag3 = ParameterHelpers.GetString(el, "STING_TAG3"),
                        CategoryName = ParameterHelpers.GetCategoryName(el) ?? string.Empty,
                        FamilyName = ParameterHelpers.GetFamilyName(el) ?? string.Empty,
                        ExportedAt = stamp,
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Speckle: CollectTaggedDtos failed", ex);
            }

            return dtos;
        }

        /// <summary>Null-safe ordinal comparison, treating null and "" as equal.</summary>
        private static bool TokenEquals(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    //  Speckle Commands (Phase 6b) — thin IExternalCommand wrappers around
    //  SpeckleLinkEngine. Config is loaded from STING_BIM_MANAGER/speckle_config.json
    //  following the same pattern as planscape_connection.json.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Speckle Send ──

    /// <summary>
    /// Push tagged elements to a Speckle stream. Reads streamUrl/token from
    /// STING_BIM_MANAGER/speckle_config.json (created out-of-band by the user).
    /// Missing config is tolerated — the engine writes the local snapshot and
    /// leaves HTTP push as a no-op pending Speckle SDK v2 integration.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpeckleSendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null || ctx.Doc == null) { message = "No document open."; return Result.Failed; }

                // Config: read streamUrl/token from STING_BIM_MANAGER/speckle_config.json
                // (same pattern as planscape_connection.json in PlanscapeConnectCommand).
                string cfgPath = Path.Combine(BIMManagerEngine.GetBIMManagerDir(ctx.Doc), "speckle_config.json");
                string streamUrl = "", token = "";
                if (File.Exists(cfgPath))
                {
                    var cfg = JObject.Parse(File.ReadAllText(cfgPath));
                    streamUrl = cfg["streamUrl"]?.Value<string>() ?? "";
                    token     = cfg["token"]?.Value<string>()     ?? "";
                }

                SpeckleLinkEngine.SendToSpeckle(ctx.Doc, streamUrl, token);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SpeckleSendCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion

    #region ── Speckle Receive ──

    /// <summary>
    /// Load the Speckle snapshot from disk and report the element count.
    /// HTTP pull from the Speckle stream is deferred until the SDK is added.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpeckleReceiveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null || ctx.Doc == null) { message = "No document open."; return Result.Failed; }

                var elements2 = SpeckleLinkEngine.ReceiveFromSpeckle(ctx.Doc, "", "");
                TaskDialog.Show("Speckle Receive", $"Snapshot contains {elements2.Count} elements.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SpeckleReceiveCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion

    #region ── Speckle Diff ──

    /// <summary>
    /// Compare the local Speckle snapshot against the current model's tagged
    /// elements and report Added/Removed/Changed counts.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpeckleDiffCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null || ctx.Doc == null) { message = "No document open."; return Result.Failed; }

                var (added, removed, changed) = SpeckleLinkEngine.DiffSnapshot(ctx.Doc);
                TaskDialog.Show("Speckle Diff",
                    $"vs last snapshot:\n  Added:   {added}\n  Removed: {removed}\n  Changed: {changed}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SpeckleDiffCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion
}
