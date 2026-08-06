// PlanscapeServerClient — Site Photos (Phase 2, BCC site-photo suite).
//
// Replaces the MergeRecoveryStubs no-ops with real authenticated HTTP against the
// server's actual contract. Follows the PlanscapeServerClient.Hvac.cs precedent:
// one partial per feature area, EnsureAuthenticatedAsync first, LastError set on
// every failure path, null (not a default value) returned on failure.
//
// ROUTES — derived from the CONTROLLER SOURCE, not from the stub signatures. The
// stubs were written blind and several of their parameters did not match the wire.
// Corrections made here, each verified against the controller:
//
//   * CreatePhotoAlbumAsync defaulted visibility to "Project". That is NOT a valid
//     value — PhotoAlbum.ValidVisibilities is { Internal, Members, Client,
//     Distribution } and PhotoAlbumsController.Create 400s on anything else. The
//     server's own default is "Members"; this client now matches it.
//   * BulkReclassifyPhotosAsync passed "newClass". The wire field is `toReason`,
//     validated against SitePhoto.ValidReasons (BulkReclassifyRequest).
//   * LockPhotoAlbumAsync took a bool. There is no boolean route — lock and unlock
//     are two separate POSTs ({albumId}/lock, {albumId}/unlock).
//   * CreatePhotoShareLinkAsync took a TimeSpan. The wire field is an ABSOLUTE
//     DateTime `expiresAt` (CreateShareLinkRequest); converted here.
//   * AcceptPhotoNdaAsync(projectId, ndaSha) — there is NO project-level accept-nda
//     route on the server. Only POST photos/{photoId}/accept-nda exists. See the
//     method for how that is handled honestly rather than faked.
//
// FAILURE CONTRACT — a failed list returns null, NEVER an empty list. The stubs
// returned `new List<T>()` on every call, which made "the server is unreachable"
// indistinguishable from "this project has no albums". That is the fabrication
// anti-pattern in a different costume: the sub-tab renders a confident, empty,
// wrong answer. Callers must treat null as "could not load" and render LastError.

// Nullable annotations only — matching MergeRecoveryStubs.cs, which is where these
// signatures used to live. The project sets <Nullable>disable</Nullable>, so
// without this the `string?` / `List<T>?` annotations that the call sites already
// expect raise CS8632. Warnings stay off; this is the per-file opt-in the codebase
// review recommends for new/edited files rather than a project-wide flip.
#nullable enable annotations
#nullable disable warnings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    public sealed partial class PlanscapeServerClient
    {
        // ── Photo policy / NDA ────────────────────────────────────────────

        /// <summary>
        /// <c>GET /api/projects/{projectId}/photo-policy</c> (PhotoPolicyController.Get).
        /// Returns null on failure — LastError carries the reason.
        /// </summary>
        /// <remarks>
        /// The server's PhotoPolicy entity carries <c>NdaText</c> but has no
        /// <c>NdaSha</c> or <c>Required</c> column. Both are DERIVED here, and that
        /// derivation is the contract the accept path depends on:
        ///   • <c>Required</c> = NdaText is non-blank (no text ⇒ nothing to accept);
        ///   • <c>NdaSha</c>   = SHA-256 of NdaText, which is exactly what
        ///     AcceptNdaRequest.AcceptedTextSha256 records, so an acceptance is
        ///     pinned to the text that was actually shown.
        /// </remarks>
        public async Task<StingTools.UI.PhotoPolicyDto?> GetPhotoPolicyAsync(Guid projectId)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
            try
            {
                var resp = await GetAsync($"/api/projects/{projectId}/photo-policy").ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Photo policy load failed ({resp.status}): {Trim(resp.body)}";
                    return null;
                }
                var j = JObject.Parse(resp.body);
                var ndaText = Str(j, "ndaText");
                return new StingTools.UI.PhotoPolicyDto
                {
                    NdaText  = ndaText,
                    NdaSha   = string.IsNullOrWhiteSpace(ndaText) ? null : Sha256Hex(ndaText),
                    Required = !string.IsNullOrWhiteSpace(ndaText),
                };
            }
            catch (Exception ex)
            {
                LastError = $"Photo policy load failed: {ex.Message}";
                StingLog.Error("GetPhotoPolicyAsync failed", ex);
                return null;
            }
        }

        /// <summary>
        /// <c>POST /api/projects/{projectId}/photos/{photoId}/accept-nda</c>
        /// (SitePhotosExtController.AcceptNda). Body: <c>{ acceptedTextSha256 }</c>.
        /// The server is idempotent — an existing acceptance returns 200.
        /// </summary>
        public async Task<bool> AcceptPhotoNdaAsync(Guid projectId, Guid photoId)
            => await AcceptPhotoNdaCoreAsync(projectId, photoId, null).ConfigureAwait(false);

        /// <summary>
        /// Accept the NDA for every photo currently flagged as NDA-required
        /// (<see cref="LastNdaRequiredIds"/>), pinning each acceptance to
        /// <paramref name="ndaSha"/>.
        /// </summary>
        /// <remarks>
        /// HONEST NOTE ON THIS OVERLOAD. The stub's signature implied a
        /// project-level "accept the NDA once" route. **No such route exists** —
        /// the only accept-nda endpoint on the server is per-photo
        /// (SitePhotosExtController:394), because PhotoNdaAcceptance is keyed on
        /// (PhotoId, UserId). Rather than fake a project-level accept or silently
        /// no-op, this fans out over the ids the last list call reported as
        /// NDA-gated and reports partial failure truthfully.
        ///
        /// With no pending ids it returns FALSE and sets LastError, rather than
        /// returning true for having done nothing — a vacuous success here would
        /// let the UI unlock content the user never accepted terms for.
        /// </remarks>
        public async Task<bool> AcceptPhotoNdaAsync(Guid projectId, string? ndaSha = null)
        {
            var pending = LastNdaRequiredIds?.ToList() ?? new List<Guid>();
            if (pending.Count == 0)
            {
                LastError = "No NDA-gated photos are pending acceptance. "
                          + "(The server has no project-level accept-NDA route; acceptance is per photo.)";
                return false;
            }

            int ok = 0;
            var failures = new List<string>();
            foreach (var photoId in pending)
            {
                if (await AcceptPhotoNdaCoreAsync(projectId, photoId, ndaSha).ConfigureAwait(false)) ok++;
                else failures.Add($"{photoId}: {LastError}");
            }

            if (failures.Count == 0)
            {
                LastNdaRequiredIds = new HashSet<Guid>();
                LastError = null;
                return true;
            }

            LastError = $"Accepted {ok} of {pending.Count} NDA-gated photos. First failure — {failures[0]}";
            return false;
        }

        private async Task<bool> AcceptPhotoNdaCoreAsync(Guid projectId, Guid photoId, string? ndaSha)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return false; }
            try
            {
                var resp = await PostJsonAsync(
                    $"/api/projects/{projectId}/photos/{photoId}/accept-nda",
                    new { acceptedTextSha256 = ndaSha }).ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"NDA acceptance failed ({resp.status}): {Trim(resp.body)}";
                    return false;
                }
                LastNdaRequiredIds?.Remove(photoId);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"NDA acceptance failed: {ex.Message}";
                StingLog.Error("AcceptPhotoNdaAsync failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Photo ids the most recent load reported as NDA-gated. Populated by the
        /// photo-list path; consumed by the project-level NDA accept overload.
        /// </summary>
        public HashSet<Guid> LastNdaRequiredIds { get; set; } = new();

        // ── Checklists ────────────────────────────────────────────────────

        /// <summary>
        /// <c>GET /api/projects/{projectId}/photo-checklists</c>
        /// (PhotoChecklistsController.List). Optional <c>status</c> filter.
        /// <para>
        /// Returns <c>null</c> on failure and an EMPTY LIST only when the project
        /// genuinely has no checklists. Those are different answers and the sub-tab
        /// must render them differently.
        /// </para>
        /// </summary>
        public async Task<List<StingTools.UI.PhotoChecklistDto>?> ListPhotoChecklistsAsync(
            Guid projectId, string? status = null)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
            try
            {
                var path = $"/api/projects/{projectId}/photo-checklists";
                if (!string.IsNullOrWhiteSpace(status)) path += $"?status={Uri.EscapeDataString(status)}";

                var resp = await GetAsync(path).ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Checklist load failed ({resp.status}): {Trim(resp.body)}";
                    return null;
                }

                var list = new List<StingTools.UI.PhotoChecklistDto>();
                foreach (var t in JArray.Parse(resp.body))
                {
                    var j = (JObject)t;
                    list.Add(new StingTools.UI.PhotoChecklistDto
                    {
                        Id        = GuidOf(j, "id"),
                        Name      = Str(j, "name") ?? "",
                        Status    = Str(j, "status") ?? "Open",
                        Kind      = Str(j, "kind"),
                        LevelCode = Str(j, "levelCode"),
                        ZoneCode  = Str(j, "zoneCode"),
                        DueAt     = Date(j, "dueAt"),
                        CreatedAt = Date(j, "createdAt") ?? DateTime.MinValue,
                        Total     = Int(j, "total"),
                        Done      = Int(j, "done"),
                    });
                }
                LastError = null;
                return list;
            }
            catch (Exception ex)
            {
                LastError = $"Checklist load failed: {ex.Message}";
                StingLog.Error("ListPhotoChecklistsAsync failed", ex);
                return null;
            }
        }

        // ── Albums ────────────────────────────────────────────────────────

        /// <summary>
        /// <c>GET /api/projects/{projectId}/photo-albums</c> (PhotoAlbumsController.List).
        /// Returns null on failure, empty list when there are genuinely no albums.
        /// </summary>
        public async Task<List<StingTools.UI.PhotoAlbumDto>?> ListPhotoAlbumsAsync(Guid projectId)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
            try
            {
                var resp = await GetAsync($"/api/projects/{projectId}/photo-albums").ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Album load failed ({resp.status}): {Trim(resp.body)}";
                    return null;
                }
                var list = new List<StingTools.UI.PhotoAlbumDto>();
                foreach (var t in JArray.Parse(resp.body)) list.Add(MapAlbum((JObject)t));
                LastError = null;
                return list;
            }
            catch (Exception ex)
            {
                LastError = $"Album load failed: {ex.Message}";
                StingLog.Error("ListPhotoAlbumsAsync failed", ex);
                return null;
            }
        }

        /// <summary>
        /// <c>GET /api/projects/{projectId}/photo-albums/{albumId}</c>
        /// (PhotoAlbumsController.GetOne). Carries the album's photo entries, which
        /// the server has already filtered through PhotoAclGate.
        /// </summary>
        /// <remarks>
        /// UNLIKE every other album route, this one returns a WRAPPER —
        /// <c>{ album, photos, ndaRequiredIds }</c> — not a flat album object. List,
        /// Create and Lock all return the album at the top level. Reading this
        /// response as a flat album silently yields an empty DTO, so the shape is
        /// handled explicitly here.
        ///
        /// <c>ndaRequiredIds</c> is the ONLY place the server tells the client which
        /// photos are NDA-gated, so it is captured into
        /// <see cref="LastNdaRequiredIds"/> — which the project-level NDA accept
        /// overload consumes.
        /// </remarks>
        public async Task<StingTools.UI.PhotoAlbumDto?> GetPhotoAlbumAsync(Guid projectId, Guid albumId)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
            try
            {
                var resp = await GetAsync($"/api/projects/{projectId}/photo-albums/{albumId}")
                    .ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Album load failed ({resp.status}): {Trim(resp.body)}";
                    return null;
                }

                var root = JObject.Parse(resp.body);
                if (root["album"] is not JObject albumObj)
                {
                    // Do not fall back to mapping the wrapper as an album — that
                    // produces a plausible, empty, wrong DTO.
                    LastError = "Album response did not carry an 'album' object.";
                    return null;
                }

                var dto = MapAlbum(albumObj);

                if (root["photos"] is JArray photos)
                {
                    foreach (var p in photos)
                    {
                        if (p is not JObject po) continue;
                        var id = GuidOf(po, "photoId");
                        if (id != System.Guid.Empty)
                            dto.Photos.Add(new StingTools.UI.PhotoAlbumEntryDto { PhotoId = id });
                    }
                    // GetOne's count is the ACL-filtered truth for this caller; the
                    // list endpoint's unfiltered photoCount is not.
                    dto.PhotoCount = dto.Photos.Count;
                }

                if (root["ndaRequiredIds"] is JArray nda)
                {
                    var gated = new HashSet<Guid>();
                    foreach (var t in nda)
                        if (System.Guid.TryParse(t?.Value<string>(), out var g)) gated.Add(g);
                    LastNdaRequiredIds = gated;
                }

                LastError = null;
                return dto;
            }
            catch (Exception ex)
            {
                LastError = $"Album load failed: {ex.Message}";
                StingLog.Error("GetPhotoAlbumAsync failed", ex);
                return null;
            }
        }

        /// <summary>
        /// <c>POST /api/projects/{projectId}/photo-albums</c> (PhotoAlbumsController.Create).
        /// </summary>
        /// <param name="visibility">
        /// One of <c>Internal | Members | Client | Distribution</c>
        /// (PhotoAlbum.ValidVisibilities). Defaults to <c>Members</c>, matching the
        /// server. The stub's old default of <c>"Project"</c> is not a valid value
        /// and would have been rejected with <c>invalid_visibility</c>.
        /// </param>
        public async Task<StingTools.UI.PhotoAlbumDto?> CreatePhotoAlbumAsync(
            Guid projectId, string name, string? description = null, string visibility = "Members")
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }

            if (string.IsNullOrWhiteSpace(name)) { LastError = "Album name is required."; return null; }
            if (!ValidAlbumVisibilities.Contains(visibility))
            {
                // Fail here rather than let the server 400 — the message names the
                // allowed set, which the server's error body does too.
                LastError = $"Invalid album visibility '{visibility}'. "
                          + $"Allowed: {string.Join(", ", ValidAlbumVisibilities)}.";
                return null;
            }

            try
            {
                var resp = await PostJsonAsync($"/api/projects/{projectId}/photo-albums", new
                {
                    name,
                    description,
                    visibility,
                }).ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Album create failed ({resp.status}): {Trim(resp.body)}";
                    return null;
                }
                LastError = null;
                return MapAlbum(JObject.Parse(resp.body));
            }
            catch (Exception ex)
            {
                LastError = $"Album create failed: {ex.Message}";
                StingLog.Error("CreatePhotoAlbumAsync failed", ex);
                return null;
            }
        }

        /// <summary>
        /// <c>POST /api/projects/{projectId}/photo-albums/{albumId}/photos</c>
        /// (PhotoAlbumsController.AddPhotos). Body <c>{ photoIds }</c>, capped at 500.
        /// </summary>
        public async Task<bool> AddPhotosToAlbumAsync(Guid projectId, Guid albumId, IEnumerable<Guid> photoIds)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return false; }

            var ids = photoIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
            if (ids.Length == 0) { LastError = "No photos selected."; return false; }
            if (ids.Length > MaxPhotosPerAlbumAdd)
            {
                LastError = $"{ids.Length} photos selected; the server accepts at most {MaxPhotosPerAlbumAdd} per request.";
                return false;
            }

            try
            {
                var resp = await PostJsonAsync(
                    $"/api/projects/{projectId}/photo-albums/{albumId}/photos",
                    new { photoIds = ids }).ConfigureAwait(false);
                if (!resp.ok)
                {
                    // The server distinguishes album_locked and smart_album_managed;
                    // surface the body so the operator sees which.
                    LastError = $"Add to album failed ({resp.status}): {Trim(resp.body)}";
                    return false;
                }
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Add to album failed: {ex.Message}";
                StingLog.Error("AddPhotosToAlbumAsync failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Lock or unlock an album. These are TWO routes on the server, not a
        /// boolean field: <c>POST {albumId}/lock</c> and <c>POST {albumId}/unlock</c>
        /// (PhotoAlbumsController.Lock / .Unlock). The bool selects the route.
        /// </summary>
        public async Task<bool> LockPhotoAlbumAsync(Guid projectId, Guid albumId, bool locked)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return false; }
            var verb = locked ? "lock" : "unlock";
            try
            {
                // Both routes take no body; PostJsonAsync with an empty object is
                // correct for a [FromBody]-less action.
                var resp = await PostJsonAsync(
                    $"/api/projects/{projectId}/photo-albums/{albumId}/{verb}",
                    new { }).ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Album {verb} failed ({resp.status}): {Trim(resp.body)}";
                    return false;
                }
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Album {verb} failed: {ex.Message}";
                StingLog.Error($"LockPhotoAlbumAsync({verb}) failed", ex);
                return false;
            }
        }

        // ── Share links ───────────────────────────────────────────────────

        /// <summary>
        /// <c>POST /api/projects/{projectId}/photo-share-links</c>
        /// (PhotoShareController.Create).
        /// </summary>
        /// <param name="expiry">
        /// Relative lifetime. The wire field is an ABSOLUTE <c>expiresAt</c>
        /// (CreateShareLinkRequest), so this is converted to
        /// <c>UtcNow + expiry</c>. Null leaves it unset and the server applies its
        /// own default of 14 days.
        /// </param>
        public async Task<StingTools.UI.PhotoShareLinkDto?> CreatePhotoShareLinkAsync(
            Guid projectId, Guid albumId, TimeSpan? expiry = null, string? label = null)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
            try
            {
                var resp = await PostJsonAsync($"/api/projects/{projectId}/photo-share-links", new
                {
                    albumId,
                    label,
                    expiresAt = expiry.HasValue ? (DateTime?)DateTime.UtcNow.Add(expiry.Value) : null,
                }).ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Share link creation failed ({resp.status}): {Trim(resp.body)}";
                    return null;
                }
                var j = JObject.Parse(resp.body);
                LastError = null;
                return new StingTools.UI.PhotoShareLinkDto
                {
                    Token         = Str(j, "token") ?? "",
                    ExpiresAt     = Date(j, "expiresAt") ?? DateTime.MinValue,
                    ForceRedacted = Bool(j, "forceRedacted"),
                };
            }
            catch (Exception ex)
            {
                LastError = $"Share link creation failed: {ex.Message}";
                StingLog.Error("CreatePhotoShareLinkAsync failed", ex);
                return null;
            }
        }

        // ── Bulk export ───────────────────────────────────────────────────

        /// <summary>
        /// <c>POST /api/projects/{projectId}/photo-export?format=zip|pdf</c>
        /// (PhotoExportController.Export) — streams the bundle to
        /// <paramref name="outputPath"/> and returns the path written, or null on
        /// failure.
        /// </summary>
        /// <remarks>
        /// THIS RESPONSE IS BINARY, NOT JSON. The server writes straight to
        /// Response.Body with <c>application/zip</c> or <c>application/pdf</c> and a
        /// Content-Disposition header; there is no JSON envelope to parse. It is
        /// streamed to disk with HttpCompletionOption.ResponseHeadersRead so a
        /// 500-photo bundle never lands in memory — which mirrors the server, which
        /// deliberately avoids a bundle-sized MemoryStream for the same reason.
        ///
        /// Server-side caps: 500 (zip) / 200 (pdf). Over-cap is a 400 carrying
        /// <c>{ error, max }</c>; that JSON is read back off the error path and
        /// reported, because on a failure the body is JSON even though success is not.
        /// </remarks>
        public async Task<string?> ExportPhotosAsync(
            Guid projectId, string outputPath, Guid? albumId = null, string format = "zip")
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
            return await ExportPhotosCoreAsync(projectId, outputPath, albumId, null, format)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Overload taking explicit photo ids; writes to the user's Documents folder
        /// under the STING output location. Returns true when a file was written.
        /// </summary>
        public async Task<bool> ExportPhotosAsync(
            Guid projectId, IEnumerable<Guid>? photoIds = null, string format = "zip")
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return false; }

            var ids = photoIds?.Distinct().ToArray();
            var isPdfExport = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);

            // Validate the SELECTION before touching the environment. Resolving an
            // output directory can fail for reasons that have nothing to do with the
            // request (no Revit document context, no writable folder), and when it
            // did run first, a user who selected 201 photos for a PDF was told
            // "Could not resolve an output folder" instead of that the cap is 200.
            // Cheap, deterministic validation belongs ahead of environment-dependent
            // validation regardless of what it does for testability.
            if (ExportSelectionRejected(null, ids, isPdfExport)) return false;

            var ext  = isPdfExport ? "pdf" : "zip";
            var name = $"photos-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}";

            string dir;
            try { dir = OutputLocationHelper.GetOutputDirectory(); }
            catch (Exception ex)
            {
                LastError = $"Could not resolve an output folder: {ex.Message}";
                return false;
            }

            var path = await ExportPhotosCoreAsync(
                projectId, Path.Combine(dir, name), null, ids, format).ConfigureAwait(false);
            return path != null;
        }

        private async Task<string?> ExportPhotosCoreAsync(
            Guid projectId, string outputPath, Guid? albumId, Guid[]? photoIds, string format)
        {
            var isPdf = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);

            // Both cheap guards now live in ExportSelectionRejected so the public
            // overload can run them BEFORE it resolves an output directory. Kept
            // here too: this is the last gate for the album overload, which does
            // not go through that path.
            if (ExportSelectionRejected(albumId, photoIds, isPdf)) return null;

            var http = SnapshotHttpClient();
            if (http == null) { LastError = "Not connected."; return null; }

            try
            {
                var body = new StringContent(
                    JsonConvert.SerializeObject(
                        new { photoIds, albumId },
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                    Encoding.UTF8, "application/json");

                var url = $"/api/projects/{projectId}/photo-export?format={(isPdf ? "pdf" : "zip")}";

                using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = body };
                using var resp = await http
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    // Failure bodies ARE JSON (e.g. { error: "batch_too_large", max: 500 }),
                    // even though a successful body is binary. Surface the cap.
                    var err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    LastError = $"Photo export failed ({(int)resp.StatusCode}): {DescribeExportError(err)}";
                    return null;
                }

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Stream to disk — never buffer the bundle.
                using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
                                                FileShare.None, 81920, useAsync: true))
                {
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }

                var written = new FileInfo(outputPath);
                if (!written.Exists || written.Length == 0)
                {
                    // A 200 with an empty body is a failure, not an empty export.
                    LastError = "Photo export returned an empty file — nothing was written.";
                    try { if (written.Exists) written.Delete(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    return null;
                }

                TouchActivity();
                LastError = null;
                StingLog.Info($"Photo export wrote {written.Length:N0} bytes to {outputPath}");
                return outputPath;
            }
            catch (Exception ex)
            {
                LastError = $"Photo export failed: {ex.Message}";
                StingLog.Error("ExportPhotosAsync failed", ex);
                return null;
            }
        }

        /// <summary>Turn the server's export error body into something an operator can act on.</summary>
        private static string DescribeExportError(string body)
        {
            try
            {
                var j = JObject.Parse(body);
                var err = j["error"]?.Value<string>();
                var max = j["max"]?.Value<int?>();
                return err switch
                {
                    "batch_too_large"         => $"too many photos for a ZIP export (max {max ?? MaxPhotosPerZipExport}).",
                    "batch_too_large_for_pdf" => $"too many photos for a PDF export (max {max ?? MaxPhotosPerPdfExport}).",
                    "ids_or_album_required"   => "no photos or album were specified.",
                    _                          => Trim(body),
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Suppressed: {ex.Message}");
                return Trim(body);
            }
        }

        // ── Admin bulk operations ─────────────────────────────────────────

        /// <summary>
        /// <c>POST /api/projects/{projectId}/photos/bulk-reclassify</c>
        /// (SitePhotosExtController.BulkReclassify). Returns the number of photos
        /// updated, or 0 on failure with LastError set — callers test <c>n &gt; 0</c>.
        /// </summary>
        /// <param name="newClass">
        /// Sent as the wire field <c>toReason</c> and validated server-side against
        /// SitePhoto.ValidReasons. The stub's name implied a field called
        /// <c>newClass</c>, which the server does not read.
        /// </param>
        public async Task<int> BulkReclassifyPhotosAsync(Guid projectId, IEnumerable<Guid> photoIds, string newClass)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return 0; }

            var ids = photoIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
            if (ids.Length == 0) { LastError = "No photos selected."; return 0; }
            if (string.IsNullOrWhiteSpace(newClass)) { LastError = "A target classification is required."; return 0; }
            if (ids.Length > MaxPhotosPerBulkOp)
            {
                LastError = $"{ids.Length} photos selected; the server accepts at most {MaxPhotosPerBulkOp} per request.";
                return 0;
            }

            return await PostBulkAsync(
                $"/api/projects/{projectId}/photos/bulk-reclassify",
                new { photoIds = ids, toReason = newClass },
                "Reclassify").ConfigureAwait(false);
        }

        /// <summary>
        /// <c>POST /api/projects/{projectId}/photos/bulk-reanchor</c>
        /// (SitePhotosExtController.BulkReanchor). Returns the number updated.
        /// </summary>
        /// <param name="payload">
        /// Optional pre-built body. When null, one is composed from
        /// <paramref name="levelCode"/> / <paramref name="zoneCode"/> — the two
        /// fields BulkReanchorRequest actually reads alongside WorkPackageId.
        /// </param>
        public async Task<int> BulkReanchorPhotosAsync(
            Guid projectId, IEnumerable<Guid> photoIds, object? payload = null,
            string? levelCode = null, string? zoneCode = null)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return 0; }

            var ids = photoIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
            if (ids.Length == 0) { LastError = "No photos selected."; return 0; }
            if (ids.Length > MaxPhotosPerBulkOp)
            {
                LastError = $"{ids.Length} photos selected; the server accepts at most {MaxPhotosPerBulkOp} per request.";
                return 0;
            }
            if (payload == null && levelCode == null && zoneCode == null)
            {
                // The server would update 0 rows' worth of fields and still report
                // success; refuse rather than report a meaningless "updated" count.
                LastError = "Nothing to re-anchor — supply a level or a zone.";
                return 0;
            }

            object body;
            if (payload != null)
            {
                var j = JObject.FromObject(payload);
                j["photoIds"] = JArray.FromObject(ids);
                body = j;
            }
            else
            {
                body = new { photoIds = ids, levelCode, zoneCode };
            }

            return await PostBulkAsync(
                $"/api/projects/{projectId}/photos/bulk-reanchor", body, "Re-anchor")
                .ConfigureAwait(false);
        }

        /// <summary>Shared body for the bulk endpoints, which all return <c>{ updated }</c>.</summary>
        private async Task<int> PostBulkAsync(string path, object body, string label)
        {
            try
            {
                var resp = await PostJsonAsync(path, body).ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"{label} failed ({resp.status}): {Trim(resp.body)}";
                    return 0;
                }
                var updated = JObject.Parse(resp.body)["updated"]?.Value<int>() ?? 0;
                if (updated == 0)
                {
                    // A 200 that changed nothing is not an error, but it must not
                    // read as success in the UI either.
                    LastError = $"{label} matched no photos — nothing was changed.";
                    return 0;
                }
                LastError = null;
                return updated;
            }
            catch (Exception ex)
            {
                LastError = $"{label} failed: {ex.Message}";
                StingLog.Error($"{label} failed", ex);
                return 0;
            }
        }

        // ── Distribution groups ───────────────────────────────────────────

        /// <summary>
        /// <c>GET /api/projects/{projectId}/distribution-groups</c>
        /// (DistributionGroupsController.List). Null on failure, empty when none.
        /// </summary>
        public async Task<List<DistributionGroupDto>?> ListDistributionGroupsAsync(Guid projectId)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return null; }
            try
            {
                var resp = await GetAsync($"/api/projects/{projectId}/distribution-groups")
                    .ConfigureAwait(false);
                if (!resp.ok)
                {
                    LastError = $"Distribution group load failed ({resp.status}): {Trim(resp.body)}";
                    return null;
                }
                var list = new List<DistributionGroupDto>();
                foreach (var t in JArray.Parse(resp.body))
                {
                    var j = (JObject)t;
                    list.Add(new DistributionGroupDto
                    {
                        Id          = GuidOf(j, "id"),
                        Name        = Str(j, "name") ?? "",
                        Kind        = Str(j, "kind"),
                        MemberCount = Int(j, "memberCount"),
                    });
                }
                LastError = null;
                return list;
            }
            catch (Exception ex)
            {
                LastError = $"Distribution group load failed: {ex.Message}";
                StingLog.Error("ListDistributionGroupsAsync failed", ex);
                return null;
            }
        }

        /// <summary>
        /// <c>POST /api/projects/{projectId}/distribution-groups</c>, then one
        /// <c>POST {groupId}/members</c> per recipient — members are a separate
        /// route (AddDistributionMemberRequest), not a field on create.
        /// </summary>
        /// <param name="kind">
        /// One of <c>Client | Internal | Mixed</c> (DistributionGroup.ValidKinds);
        /// defaults to <c>Internal</c>, matching the server.
        /// </param>
        public async Task<bool> CreateDistributionGroupAsync(
            Guid projectId, string name, IEnumerable<string>? recipients = null, string? kind = null)
        {
            if (!await EnsureAuthenticatedAsync()) { LastError = "Not connected."; return false; }
            if (string.IsNullOrWhiteSpace(name)) { LastError = "Group name is required."; return false; }

            var groupKind = string.IsNullOrWhiteSpace(kind) ? "Internal" : kind!;
            if (!ValidDistributionKinds.Contains(groupKind))
            {
                LastError = $"Invalid distribution group kind '{groupKind}'. "
                          + $"Allowed: {string.Join(", ", ValidDistributionKinds)}.";
                return false;
            }

            try
            {
                var resp = await PostJsonAsync($"/api/projects/{projectId}/distribution-groups",
                    new { name, kind = groupKind }).ConfigureAwait(false);
                if (!resp.ok)
                {
                    // 409 name_in_use is the common one and is worth naming plainly.
                    LastError = resp.status == 409
                        ? $"A distribution group named '{name}' already exists."
                        : $"Distribution group create failed ({resp.status}): {Trim(resp.body)}";
                    return false;
                }

                var groupId = GuidOf(JObject.Parse(resp.body), "id");
                var emails  = recipients?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToArray()
                              ?? Array.Empty<string>();
                if (groupId == System.Guid.Empty || emails.Length == 0) { LastError = null; return true; }

                var failed = new List<string>();
                foreach (var email in emails)
                {
                    var m = await PostJsonAsync(
                        $"/api/projects/{projectId}/distribution-groups/{groupId}/members",
                        new { externalEmail = email }).ConfigureAwait(false);
                    if (!m.ok) failed.Add(email);
                }

                if (failed.Count > 0)
                {
                    // The group exists; say so, and say which recipients did not land.
                    LastError = $"Group '{name}' was created, but {failed.Count} of {emails.Length} "
                              + $"recipients could not be added: {string.Join(", ", failed.Take(5))}"
                              + (failed.Count > 5 ? ", …" : "");
                    return false;
                }

                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Distribution group create failed: {ex.Message}";
                StingLog.Error("CreateDistributionGroupAsync failed", ex);
                return false;
            }
        }

        // ── Shared helpers ────────────────────────────────────────────────

        // Server-side caps, mirrored so the client can refuse before a round trip.
        // PhotoExportController.MaxPhotosPerExport / MaxPhotosPerPdf; the album and
        // bulk routes each hard-code 500.
        /// <summary>
        /// The two export guards that need no I/O and no network: a selection must
        /// name photos or an album, and it must sit under the server's per-format
        /// cap. Sets <see cref="LastError"/> and returns true when the selection is
        /// rejected.
        ///
        /// Shared so the wording exists once. The messages mirror the server's
        /// ids_or_album_required and batch_too_large[_for_pdf] responses, but are
        /// raised without a round trip.
        /// </summary>
        private bool ExportSelectionRejected(Guid? albumId, Guid[]? photoIds, bool isPdf)
        {
            if (albumId == null && (photoIds == null || photoIds.Length == 0))
            {
                LastError = "Select photos or an album to export.";
                return true;
            }

            var cap = isPdf ? MaxPhotosPerPdfExport : MaxPhotosPerZipExport;
            if (photoIds != null && photoIds.Length > cap)
            {
                LastError = $"{photoIds.Length} photos selected; the server caps a "
                          + $"{(isPdf ? "PDF" : "ZIP")} export at {cap}. Export in batches or use an album.";
                return true;
            }

            return false;
        }

        private const int MaxPhotosPerZipExport  = 500;
        private const int MaxPhotosPerPdfExport  = 200;
        private const int MaxPhotosPerAlbumAdd   = 500;
        private const int MaxPhotosPerBulkOp     = 500;

        /// <summary>PhotoAlbum.ValidVisibilities — kept in step with the entity.</summary>
        private static readonly string[] ValidAlbumVisibilities =
            { "Internal", "Members", "Client", "Distribution" };

        /// <summary>DistributionGroup.ValidKinds — kept in step with the entity.</summary>
        private static readonly string[] ValidDistributionKinds =
            { "Client", "Internal", "Mixed" };

        /// <summary>
        /// Map a FLAT album object — the shape returned by List, Create and
        /// Lock/Unlock. GetOne's wrapper is unwrapped by its own caller before this
        /// is reached; <c>photoCount</c> is absent on Create/Lock and maps to 0,
        /// which is correct for a newly created album.
        /// </summary>
        private static StingTools.UI.PhotoAlbumDto MapAlbum(JObject j) => new()
        {
            Id          = GuidOf(j, "id"),
            Name        = Str(j, "name") ?? "",
            Description = Str(j, "description") ?? "",
            Visibility  = Str(j, "visibility") ?? "Members",
            Kind        = Str(j, "kind"),
            PhotoCount  = Int(j, "photoCount"),
            IsLocked    = Bool(j, "isLocked"),
        };

        private static string? Str(JObject j, string name)
            => j[name]?.Type == JTokenType.Null ? null : j[name]?.Value<string>();

        private static int Int(JObject j, string name)
            => j[name]?.Type == JTokenType.Null ? 0 : (j[name]?.Value<int?>() ?? 0);

        private static bool Bool(JObject j, string name)
            => j[name]?.Type == JTokenType.Null ? false : (j[name]?.Value<bool?>() ?? false);

        private static Guid GuidOf(JObject j, string name)
            => System.Guid.TryParse(Str(j, name), out var g) ? g : System.Guid.Empty;

        private static DateTime? Date(JObject j, string name)
            => j[name]?.Type == JTokenType.Null ? null : j[name]?.Value<DateTime?>();

        /// <summary>Keep an error body short enough for a TaskDialog line.</summary>
        private static string Trim(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "(no response body)";
            var s = body.Trim().Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 300 ? s : s.Substring(0, 300) + "…";
        }

        private static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
