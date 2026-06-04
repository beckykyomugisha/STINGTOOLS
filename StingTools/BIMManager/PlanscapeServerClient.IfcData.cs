#nullable enable
// PlanscapeServerClient — H-1: the Revit producer for the cross-host
// /ifc/data ingest contract.
//
//     POST /api/projects/{projectId}/ifc/data   (IfcController.IngestData)
//
// Until now ONLY the Python hosts (StingBridge / Bonsai) posted /ifc/data;
// Revit element data rode the [Obsolete] /api/tagsync/sync path, so Revit fed
// the cross-host ExternalElementMapping only indirectly. This partial gives
// Revit a first-class producer for the SAME contract every other host speaks.
//
// Identity contract (unified — see GeometrySyncHandler + PlatformLinkCommands):
// each element's cross-host key is the stabilised IFC GlobalId
// (IFC_GLOBAL_ID_TXT). The CALLER builds the element payloads (it needs the
// Revit Document + ParameterHelpers to read tokens) and MUST skip elements
// whose IFC_GLOBAL_ID_TXT is empty rather than send a wrong key — matching the
// server's "skip, don't mis-key" rule (IfcIngestService drops empty-GlobalId
// rows). This method is the transport only; it does not touch the Revit API,
// so it lives in the server-client layer and is unit-reviewable without Revit.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StingTools.BIMManager
{
    public sealed partial class PlanscapeServerClient
    {
        /// <summary>
        /// Push a batch of IFC elements to <c>POST /api/projects/{id}/ifc/data</c>.
        /// <paramref name="elements"/> are objects shaped to the server's
        /// <c>IfcElementDto</c> (camelCase: ifcGlobalId, hostElementId,
        /// discipline, location, zone, level, system, function, product,
        /// sequence, fullTag, ifcClass, categoryName, familyName, typeName,
        /// status, rev, roomName, levelName, isComplete, isFullyResolved,
        /// isStale, validationErrors). Returns the parsed IfcIngestResponse
        /// (newMappings / updatedMappings / newElements / …) or null on failure
        /// (LastError set). <paramref name="host"/> defaults to "revit".
        /// </summary>
        public async Task<JObject?> PushIfcDataAsync(
            Guid projectId,
            IReadOnlyList<object> elements,
            string host = "revit",
            string? hostDocumentGuid = null,
            string pluginVersion = "stingtools-revit",
            string userName = "")
        {
            if (elements == null || elements.Count == 0) return null;
            if (!await EnsureAuthenticatedAsync()) return null;
            try
            {
                var payload = new
                {
                    host,
                    hostDocumentGuid,
                    pluginVersion,
                    userName,
                    elements,
                };
                var resp = await PostJsonAsync($"/api/projects/{projectId}/ifc/data", payload)
                    .ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"IFC data push failed ({resp.status}): {resp.body}";
                    return null;
                }
                return JObject.Parse(resp.body);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }
    }
}
