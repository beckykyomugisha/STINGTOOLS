// ══════════════════════════════════════════════════════════════════════════
//  BoqSyncCoordinator.cs — Orchestrates plugin → server BOQ baseline push.
//
//  Flow:
//    1. BOQCostManager.SaveSnapshot writes local JSON + computes checksum
//       via BoqSnapshotHasher.
//    2. Caller invokes BoqSyncCoordinator.PushSnapshotAsync(snapshot).
//    3. Coordinator resolves the server project id (config file or
//       PlanscapeServerClient.GetOrCreateProjectAsync), POSTs a baseline,
//       then upserts quantity lines in chunked batches.
//    4. Returns the server-assigned BaselineId so the caller can stamp
//       it onto BOQSnapshotMeta for future reconciliation.
//
//  Offline policy: if Planscape is not configured / not authenticated /
//  not reachable, the coordinator stamps SyncState = "Pending" on the
//  local meta and returns null. The push retries on the next sync cycle
//  via the existing background scheduler (Planscape.PluginSync).
//
//  P1 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.BOQ.Sync
{
    /// <summary>
    /// Result of a sync attempt. Either the server baseline id (success)
    /// or a SyncState describing why it deferred.
    /// </summary>
    public class BoqSyncResult
    {
        public Guid? ServerBaselineId;
        public string Checksum;
        public string SyncState;   // "Synced" | "Pending" | "Conflict" | "Disabled"
        public string Detail;
        public int LinesCreated;
        public int LinesUpdated;
    }

    internal static class BoqSyncCoordinator
    {
        /// <summary>
        /// Push a freshly-saved snapshot to the server. Returns a result
        /// describing success or the deferral reason. Never throws —
        /// caller can attempt sync in a fire-and-forget manner.
        /// </summary>
        public static async Task<BoqSyncResult> PushSnapshotAsync(
            Document doc, BOQDocument boq, string checksum, string snapshotLabel)
        {
            var result = new BoqSyncResult
            {
                Checksum = checksum,
                SyncState = "Pending"
            };

            if (doc == null || boq == null)
            {
                result.SyncState = "Disabled";
                result.Detail = "Document or BOQ is null";
                return result;
            }

            try
            {
                // Resolve project id from BIMManager config.
                Guid projectId;
                if (!TryResolveProjectId(doc, out projectId))
                {
                    result.SyncState = "Disabled";
                    result.Detail = "No Planscape project configured for this Revit file";
                    return result;
                }

                var client = PlanscapeServerClient.Instance;
                if (client == null)
                {
                    result.SyncState = "Pending";
                    result.Detail = "PlanscapeServerClient unavailable";
                    return result;
                }

                // 1. Create the baseline shell.
                var baselinePayload = new
                {
                    name = string.IsNullOrEmpty(snapshotLabel) ? boq.SnapshotLabel : snapshotLabel,
                    kind = MapSnapshotTypeToBaselineKind(boq.SnapshotType),
                    currency = boq.Currency ?? "UGX",
                    description = $"Auto-pushed by STING plugin on {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}",
                    checksum = checksum
                };

                Guid? baselineId = await CreateBaselineAsync(client, projectId, baselinePayload);
                if (!baselineId.HasValue)
                {
                    result.SyncState = "Pending";
                    result.Detail = "Baseline create failed: " + (client.LastError ?? "(no detail)");
                    return result;
                }

                // 2. Build line-item payload for the upsert call.
                var lines = boq.AllItems.Select(BuildLinePayload).ToList();
                if (lines.Count == 0)
                {
                    result.ServerBaselineId = baselineId;
                    result.SyncState = "Synced";
                    result.Detail = "Baseline created with zero lines";
                    return result;
                }

                // 3. Upsert lines in chunks of 200 to stay under the
                //    server's pageSize cap and avoid large request bodies.
                int created = 0, updated = 0;
                const int chunkSize = 200;
                for (int i = 0; i < lines.Count; i += chunkSize)
                {
                    var chunk = lines.Skip(i).Take(chunkSize).ToList();
                    var (ok, c, u) = await client
                        .UpsertBoqLinesAsync(projectId, baselineId.Value, chunk);
                    if (!ok)
                    {
                        result.ServerBaselineId = baselineId;
                        result.SyncState = "Pending";
                        result.Detail = $"Line upsert failed at chunk {i / chunkSize + 1}: {client.LastError}";
                        return result;
                    }
                    created += c;
                    updated += u;
                }

                result.ServerBaselineId = baselineId;
                result.LinesCreated = created;
                result.LinesUpdated = updated;
                result.SyncState = "Synced";
                result.Detail = $"Baseline {baselineId} — {created} created, {updated} updated";
                StingLog.Info($"BoqSyncCoordinator: pushed snapshot ({checksum.Substring(0, Math.Min(8, checksum.Length))}) — {result.Detail}");
                return result;
            }
            catch (Exception ex)
            {
                result.SyncState = "Pending";
                result.Detail = "Exception: " + ex.Message;
                StingLog.Warn($"BoqSyncCoordinator.PushSnapshotAsync: {ex.Message}");
                return result;
            }
        }

        private static async Task<Guid?> CreateBaselineAsync(
            PlanscapeServerClient client, Guid projectId, object payload)
        {
            try { return await client.CreateBoqBaselineAsync(projectId, payload); }
            catch (Exception ex)
            {
                StingLog.Warn($"BoqSyncCoordinator.CreateBaseline: {ex.Message}");
                return null;
            }
        }

        private static bool TryResolveProjectId(Document doc, out Guid projectId)
        {
            projectId = Guid.Empty;
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return false;
                string configPath = System.IO.Path.Combine(bimDir, "planscape_config.json");
                var (_, _, projectIdStr) = PlanscapeServerClient.LoadConnectionSettings(configPath);
                return Guid.TryParse(projectIdStr, out projectId);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BoqSyncCoordinator.TryResolveProjectId: {ex.Message}");
                return false;
            }
        }

        private static string MapSnapshotTypeToBaselineKind(string snapshotType)
        {
            // BOQCostManager snapshot types: DD / Stage / Weekly / Handover / Manual / Live / Tender
            // BoqBaseline.Kind canonical: Tender / Contract / Interim / Final / Variation
            switch ((snapshotType ?? "").Trim().ToLowerInvariant())
            {
                case "tender": return "Tender";
                case "contract": return "Contract";
                case "handover":
                case "final": return "Final";
                case "stage":
                case "dd":
                case "weekly":
                case "live":
                case "manual":
                default: return "Interim";
            }
        }

        private static object BuildLinePayload(BOQLineItem item)
        {
            return new
            {
                sectionCode = item.NRM2Section ?? "",
                itemDescription = string.IsNullOrEmpty(item.ResolvedNRM2Paragraph)
                    ? (item.Category ?? "")
                    : item.ResolvedNRM2Paragraph,
                ifcGlobalId = item.UniqueId ?? "",
                ifcType = item.Category ?? "",
                revitElementId = item.RevitElementId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                level = item.Level ?? "",
                zone = item.Location ?? "",
                unit = item.Unit ?? "each",
                netQuantity = Math.Round(item.Quantity, 6),
                wastePercent = 0,           // P0 reserves; honoured once ES schema v2 lands
                unitRate = Math.Round(item.RateUGX, 2),
                currency = "UGX",
                lineKind = MapSourceToLineKind(item.Source),
                pricingBasis = "Remeasure",
                embodiedCarbonPerUnit = item.Quantity > 0
                    ? Math.Round(item.EmbodiedCarbonKg / item.Quantity, 4)
                    : 0
            };
        }

        private static string MapSourceToLineKind(BOQRowSource source)
        {
            switch (source)
            {
                case BOQRowSource.ProvisionalSum: return "ProvisionalSum";
                case BOQRowSource.Manual: return "Manual";
                case BOQRowSource.Model:
                default: return "Measured";
            }
        }
    }
}
