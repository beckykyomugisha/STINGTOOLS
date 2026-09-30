// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccIssueAttachment.cs — ACC-HARD-5, part 3: a BCF 2.1 file on the issue.
//
// ACC Issues v1 DOES accept attachments by API (POST construction/issues/v1/projects/{p}/
// attachments, Issues.yaml "add-attachments"), and the "Upload Issue Attachments" tutorial
// (acc/v1/tutorials/upload-issue-attachment) gives the exact sequence implemented here:
//
//   1. GET  project/v1/hubs/{hub}/projects/{p}/topFolders  → 'Project Files'.parent = root folder
//   2. POST data/v1/projects/{p}/storage (target = root)    → storage URN  (bucket/objectKey)
//   3. GET  oss/v2/buckets/{bucket}/objects/{key}/signeds3upload → uploadKey + urls
//   4. PUT  bytes → urls[0]   (no bearer)
//   5. POST oss/v2/buckets/{bucket}/objects/{key}/signeds3upload {uploadKey}
//   6. POST construction/issues/v1/projects/{p}/attachments
//        { domainEntityId: issueId, attachments: [ { attachmentId = key without extension,
//          displayName, fileName = key, attachmentType: "issue-attachment", storageUrn } ] }
//
// What is attached is a one-topic .bcfzip: the clash's two elements as BCF Components (IFC
// GUID derived from the Revit UniqueId + the UniqueId itself as AuthoringToolId), so a BCF
// manager in Revit or Navisworks can select them. There is NO camera in it: ACC clash data
// carries no coordinates, and a made-up camera would be a fabricated viewpoint.
//
// UNCONFIRMED: that ACC accepts the .bcfzip extension as an issue attachment. The spec
// says "images, PDFs, or other supported formats". A refusal is reported, not hidden.
//
// Revit-free.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Planscape.Shared.BCF;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class AccAttachResult
    {
        public bool Ok { get; set; }
        /// <summary>Which step failed (topFolders / storage / sign / put / finalise / attach).</summary>
        public string Step { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public AccFetchStatus Status { get; set; } = AccFetchStatus.TransportFailed;
        public string AttachmentId { get; set; } = string.Empty;
    }

    public static class AccIssueAttachment
    {
        private static readonly object _gate = new object();
        private static readonly Dictionary<string, string> _rootByProject = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static void ClearCache() { lock (_gate) _rootByProject.Clear(); }

        private static string Host => AccIssueSync.Host;
        private const string JsonApi = "application/vnd.api+json";

        public static async Task<AccAttachResult> AttachAsync(AccCredentials creds, string issueId,
            byte[] content, string displayName, string extension)
        {
            if (string.IsNullOrEmpty(issueId) || content == null || content.Length == 0)
                return Fail("input", AccFetchStatus.NotFound, "nothing to attach");
            if (string.IsNullOrWhiteSpace(creds?.HubId))
                return Fail("topFolders", AccFetchStatus.NotFound,
                    "the ACC hub is unknown, so the project's root folder cannot be found (Discover on the ACC card records it)");

            string dmProject = AccIds.ForDataManagement(creds.ProjectId);
            extension = (extension ?? "").TrimStart('.');

            // 1. Root folder = parent of 'Project Files' (per the tutorial).
            string root;
            lock (_gate) _rootByProject.TryGetValue(dmProject, out root);
            if (string.IsNullOrEmpty(root))
            {
                var tf = await Send(HttpMethod.Get, $"{Host}/project/v1/hubs/{Uri.EscapeDataString(AccIds.ForDataManagement(creds.HubId))}/projects/{Uri.EscapeDataString(dmProject)}/topFolders",
                    creds, null, null, idempotent: true).ConfigureAwait(false);
                if (!tf.IsSuccess) return Fail("topFolders", tf);
                try
                {
                    foreach (var f in JObject.Parse(tf.Body)["data"] as JArray ?? new JArray())
                    {
                        string name = (string)(f["attributes"]?["name"] ?? f["attributes"]?["displayName"]) ?? "";
                        if (!name.Equals("Project Files", StringComparison.OrdinalIgnoreCase)) continue;
                        root = (string)f["relationships"]?["parent"]?["data"]?["id"];
                        break;
                    }
                }
                catch (Exception ex) { return Fail("topFolders", AccFetchStatus.TransportFailed, "top folders were not JSON: " + ex.Message); }
                if (string.IsNullOrEmpty(root))
                    return Fail("topFolders", AccFetchStatus.NotFound, "no 'Project Files' top folder with a parent was found");
                lock (_gate) _rootByProject[dmProject] = root;
            }

            // 2. Storage object.
            var storageBody = new JObject
            {
                ["jsonapi"] = new JObject { ["version"] = "1.0" },
                ["data"] = new JObject
                {
                    ["type"] = "objects",
                    ["attributes"] = new JObject { ["name"] = Guid.NewGuid().ToString() + (extension.Length > 0 ? "." + extension : "") },
                    ["relationships"] = new JObject
                    {
                        ["target"] = new JObject { ["data"] = new JObject { ["type"] = "folders", ["id"] = root } }
                    }
                }
            };
            var st = await Send(HttpMethod.Post, $"{Host}/data/v1/projects/{Uri.EscapeDataString(dmProject)}/storage",
                creds, storageBody, JsonApi, idempotent: false).ConfigureAwait(false);
            if (!st.IsSuccess) return Fail("storage", st);
            string storageUrn;
            try { storageUrn = (string)JObject.Parse(st.Body)["data"]?["id"]; }
            catch (Exception ex) { return Fail("storage", AccFetchStatus.TransportFailed, "storage response was not JSON: " + ex.Message); }
            if (!TrySplitStorageUrn(storageUrn, out string bucket, out string objectKey))
                return Fail("storage", AccFetchStatus.TransportFailed, $"unexpected storage id '{storageUrn}'");

            // 3–5. Signed single-part upload.
            string signBase = $"{Host}/oss/v2/buckets/{Uri.EscapeDataString(bucket)}/objects/{Uri.EscapeDataString(objectKey)}/signeds3upload";
            var sign = await Send(HttpMethod.Get, signBase, creds, null, null, idempotent: true).ConfigureAwait(false);
            if (!sign.IsSuccess) return Fail("sign", sign);
            string uploadKey, putUrl;
            try
            {
                var j = JObject.Parse(sign.Body);
                uploadKey = (string)j["uploadKey"];
                putUrl = (string)(j["urls"] as JArray)?.FirstOrDefault();
            }
            catch (Exception ex) { return Fail("sign", AccFetchStatus.TransportFailed, "signed-upload response was not JSON: " + ex.Message); }
            if (string.IsNullOrEmpty(uploadKey) || string.IsNullOrEmpty(putUrl))
                return Fail("sign", AccFetchStatus.TransportFailed, "signed-upload response had no uploadKey or URL");

            var put = await AccHttp.SendAsync(() => new HttpRequestMessage(HttpMethod.Put, putUrl) { Content = new ByteArrayContent(content) },
                creds: null, idempotent: true).ConfigureAwait(false);
            if (!put.IsSuccess) return Fail("put", put);

            var fin = await Send(HttpMethod.Post, signBase, creds, new JObject { ["uploadKey"] = uploadKey }, "application/json", idempotent: true).ConfigureAwait(false);
            if (!fin.IsSuccess) return Fail("finalise", fin);

            // 6. Link the stored file to the issue.
            string attachmentId = objectKey.Contains('.') ? objectKey.Substring(0, objectKey.LastIndexOf('.')) : objectKey;
            var body = new JObject
            {
                ["domainEntityId"] = issueId,
                ["attachments"] = new JArray(new JObject
                {
                    ["attachmentId"] = attachmentId,
                    ["displayName"] = displayName ?? objectKey,
                    ["fileName"] = objectKey,
                    ["attachmentType"] = "issue-attachment",
                    ["storageUrn"] = storageUrn,
                }),
            };
            var att = await AccHttp.SendAsync(() => AccIssueSync.WithRegion(new HttpRequestMessage(HttpMethod.Post,
                    $"{Host}/construction/issues/v1/projects/{Uri.EscapeDataString(AccIds.ForAcc(creds.ProjectId))}/attachments")
                { Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json") }, creds),
                creds, idempotent: false).ConfigureAwait(false);
            if (!att.IsSuccess) return Fail("attach", att);

            return new AccAttachResult { Ok = true, Status = AccFetchStatus.Ok, Step = "attach", AttachmentId = attachmentId };
        }

        /// <summary>urn:adsk.objects:os.object:{bucketKey}/{objectKey}.</summary>
        internal static bool TrySplitStorageUrn(string urn, out string bucket, out string objectKey)
        {
            bucket = objectKey = string.Empty;
            if (string.IsNullOrEmpty(urn)) return false;
            int colon = urn.LastIndexOf(':');
            string tail = colon >= 0 ? urn.Substring(colon + 1) : urn;
            int slash = tail.IndexOf('/');
            if (slash <= 0 || slash == tail.Length - 1) return false;
            bucket = tail.Substring(0, slash);
            objectKey = tail.Substring(slash + 1);
            return true;
        }

        private static Task<AccHttpResponse> Send(HttpMethod m, string url, AccCredentials creds, JObject body, string contentType, bool idempotent)
        {
            string payload = body?.ToString(Newtonsoft.Json.Formatting.None);
            return AccHttp.SendAsync(() =>
            {
                var req = new HttpRequestMessage(m, url);
                if (payload != null)
                {
                    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    if (contentType != null) req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                }
                return req;
            }, creds, idempotent, timeout: TimeSpan.FromSeconds(60), maxAttempts: 3);
        }

        private static AccAttachResult Fail(string step, AccHttpResponse r) =>
            Fail(step, r.Classify(), $"HTTP {r.Status}: {r.Describe()}" + (string.IsNullOrEmpty(r.Body) ? "" : " — " + (r.Body.Length > 200 ? r.Body.Substring(0, 200) : r.Body)));

        private static AccAttachResult Fail(string step, AccFetchStatus status, string detail)
        {
            StingLog.Warn($"AccIssueAttachment [{step}]: {detail}");
            return new AccAttachResult { Ok = false, Step = step, Status = status, Detail = $"{step}: {detail}" };
        }
    }

    /// <summary>A one-topic BCF 2.1 file for a clash, written by the shared BcfEngine.</summary>
    public static class AccClashBcf
    {
        public sealed class Element
        {
            public string UniqueId { get; set; } = string.Empty;
            public string DocumentName { get; set; } = string.Empty;
        }

        /// <summary>The .bcfzip bytes, or null when no side resolved to a Revit element (a BCF
        /// topic with no components would locate nothing, so none is attached).</summary>
        public static byte[] Build(string signature, string title, string description, string referenceLink,
            IEnumerable<Element> elements)
        {
            var els = (elements ?? Enumerable.Empty<Element>()).Where(e => AccModelDerivative.IsRevitUniqueId(e?.UniqueId)).ToList();
            if (els.Count == 0) return null;

            var issue = new CoordIssue
            {
                Guid = StableGuid(signature),
                Title = title ?? "Clash",
                Description = description,
                Type = "CLASH",
                Priority = "HIGH",
                Status = "OPEN",
                Author = "STING",
                ReferenceLink = referenceLink,
                Labels = new List<string> { "clash", "ACC" },
                ViewpointBcfvXml = BuildViewpoint(els).ToString(),
            };
            return BcfEngine.ExportToBytes(new[] { issue });
        }

        /// <summary>BCF 2.1 VisualizationInfo with the elements selected and no camera (the
        /// camera elements are optional in the 2.1 schema; ACC gives no coordinates to aim one).</summary>
        internal static XDocument BuildViewpoint(IList<Element> els)
        {
            var selection = new XElement("Selection");
            foreach (var e in els)
            {
                var comp = new XElement("Component");
                string ifc = RevitIfcGuid.FromUniqueId(e.UniqueId);
                if (ifc != null) comp.Add(new XAttribute("IfcGuid", ifc));
                comp.Add(new XElement("OriginatingSystem", "Autodesk Revit" +
                    (string.IsNullOrEmpty(e.DocumentName) ? "" : " — " + e.DocumentName)));
                comp.Add(new XElement("AuthoringToolId", e.UniqueId));
                selection.Add(comp);
            }
            return new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement("VisualizationInfo",
                    new XAttribute("Guid", Guid.NewGuid().ToString()),
                    new XElement("Components",
                        selection,
                        new XElement("Visibility", new XAttribute("DefaultVisibility", "true")))));
        }

        /// <summary>Same clash → same topic GUID, so a BCF manager de-duplicates re-sends.</summary>
        internal static string StableGuid(string seed)
        {
            if (string.IsNullOrEmpty(seed)) return Guid.NewGuid().ToString();
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("sting-acc-clash|" + seed));
            return new Guid(bytes.Take(16).ToArray()).ToString();
        }
    }

    /// <summary>
    /// The IFC GlobalId Revit's IFC exporter gives an element by default, computed from its
    /// UniqueId: the export GUID is the UniqueId's episode GUID with its last 32 bits XORed
    /// with the element id (what ExportUtils.GetExportId returns), then compressed to the
    /// 22-character IFC base-64 form. A model whose IfcGUID parameter was overridden exports
    /// a different value - then the BCF component matches nothing, never something else.
    /// </summary>
    public static class RevitIfcGuid
    {
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        public static string FromUniqueId(string uniqueId)
        {
            string g = ExportGuidFromUniqueId(uniqueId);
            return g == null ? null : Compress(g);
        }

        /// <summary>The 36-character export GUID, or null for anything that is not a UniqueId.</summary>
        public static string ExportGuidFromUniqueId(string uniqueId)
        {
            if (!AccModelDerivative.IsRevitUniqueId(uniqueId)) return null;
            string uid = uniqueId.Trim().ToLowerInvariant();
            uint elementId = uint.Parse(uid.Substring(37), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            uint last = uint.Parse(uid.Substring(28, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return uid.Substring(0, 28) + (last ^ elementId).ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>IFC base-64 compression of a GUID written in its canonical text order.</summary>
        public static string Compress(string guid)
        {
            string hex = Guid.Parse(guid).ToString("N");
            var b = new ulong[16];
            for (int i = 0; i < 16; i++) b[i] = Convert.ToUInt64(hex.Substring(i * 2, 2), 16);
            var num = new ulong[]
            {
                b[0],
                (b[1] << 16) | (b[2] << 8) | b[3],
                (b[4] << 16) | (b[5] << 8) | b[6],
                (b[7] << 16) | (b[8] << 8) | b[9],
                (b[10] << 16) | (b[11] << 8) | b[12],
                (b[13] << 16) | (b[14] << 8) | b[15],
            };
            var sb = new StringBuilder(22);
            for (int i = 0; i < 6; i++)
            {
                int len = i == 0 ? 2 : 4;
                var chunk = new char[len];
                ulong n = num[i];
                for (int j = len - 1; j >= 0; j--) { chunk[j] = Alphabet[(int)(n % 64)]; n /= 64; }
                sb.Append(chunk);
            }
            return sb.ToString();
        }
    }
}
