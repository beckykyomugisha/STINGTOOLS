using System.Security.Cryptography;

namespace Planscape.Companion;

internal sealed record SyncOutcome(int Downloaded, int Skipped, int Superseded, int Failed)
{
    public override string ToString() =>
        $"{Downloaded} downloaded, {Skipped} unchanged, {Superseded} superseded, {Failed} failed";
}

/// <summary>
/// The download-and-place engine. <b>One code path, four triggers</b> — push,
/// reconnect delta, initial link and manual all call
/// <see cref="SyncProjectAsync"/>; only the <see cref="SyncTrigger"/> in the log
/// line differs.
///
/// <para><b>There is no OS-level locking here, deliberately.</b> No
/// <c>FileShare.None</c>, no lock files, nothing that treats the local
/// filesystem as the record of who owns an edit. The CDE state machine on the
/// server is that record. This is the specific mechanism that fails in Autodesk
/// Desktop Connector — files stranded "checked out" after the local app closes,
/// needing a manual unlock — and the design says in as many words not to
/// reproduce it.</para>
///
/// <para>Non-WIP copies do get the <b>read-only file attribute</b>. That is a
/// hint, not a lock: any user can clear it, nothing here fights them for it, and
/// it is never consulted to decide anything. It exists so an Author who opens a
/// PUBLISHED reference copy in Word is told, by Word, that this is not the file
/// to edit.</para>
/// </summary>
internal sealed class SyncEngine
{
    private readonly PlanscapeApiClient _api;
    private readonly CompanionSettings _settings;

    /// <summary>
    /// How long a superseded copy is kept. From the design: long enough that
    /// someone who had the file open on Friday still finds it on Monday, short
    /// enough that it cleans itself up without anyone managing it.
    /// </summary>
    public static readonly TimeSpan SupersededRetention = TimeSpan.FromDays(7);

    private const string SupersededMarker = " (superseded ";

    public SyncEngine(PlanscapeApiClient api, CompanionSettings settings)
    {
        _api = api;
        _settings = settings;
    }

    /// <summary>
    /// Sync one project. Pages through the delta until the server says there is
    /// no more, then advances the project's high-water mark.
    /// </summary>
    public async Task<SyncOutcome> SyncProjectAsync(
        LinkedProject project, SyncTrigger trigger, CancellationToken ct = default)
    {
        var folder = _settings.FolderFor(project);
        Directory.CreateDirectory(folder);

        // InitialLink means "everything currently visible", so `since` is
        // deliberately dropped even if a stamp exists from a previous linking.
        var since = trigger == SyncTrigger.InitialLink ? null : project.LastSyncUtc;
        CompanionLog.Info(
            $"sync {project.ProjectCode} ({trigger}) since={since?.ToString("O") ?? "(everything)"} → {folder}");

        int downloaded = 0, skipped = 0, superseded = 0, failed = 0;
        DateTime? newHighWater = null;

        while (true)
        {
            var page = await _api.ChangedSinceAsync(project.ProjectId, since, ct);

            foreach (var doc in page.Items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result = await PlaceAsync(project, folder, doc, ct);
                    if (result == PlaceResult.Downloaded) downloaded++;
                    else if (result == PlaceResult.DownloadedOverSuperseded) { downloaded++; superseded++; }
                    else skipped++;
                }
                catch (CompanionAuthException) { throw; }   // an auth failure ends the pass, not one file
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // One bad document must not abandon the rest of the project.
                    failed++;
                    CompanionLog.Error($"could not sync '{doc.FileName}'", ex);
                }
            }

            // The server's clock, never ours — see LinkedProject.LastSyncUtc.
            newHighWater = page.ServerTimeUtc;

            if (!page.HasMore) break;

            // A full page means more is waiting. Continue from the last item's
            // change time rather than the server clock, or the tail beyond this
            // page is skipped.
            var last = page.Items.LastOrDefault();
            if (last == null) break;
            since = last.ChangedAt;
        }

        superseded += PurgeExpiredSuperseded(folder);

        // Only advance the mark once the pass finished. Advancing on a partial
        // pass would mean a failure permanently skips whatever it failed on.
        if (failed == 0 && newHighWater.HasValue)
            project.LastSyncUtc = newHighWater;
        else if (failed > 0)
            CompanionLog.Warn(
                $"{project.ProjectCode}: {failed} file(s) failed — not advancing the sync mark so they are retried");

        var outcome = new SyncOutcome(downloaded, skipped, superseded, failed);
        CompanionLog.Info($"sync {project.ProjectCode} done — {outcome}");
        return outcome;
    }

    private enum PlaceResult { Downloaded, DownloadedOverSuperseded, Unchanged }

    private async Task<PlaceResult> PlaceAsync(
        LinkedProject project, string folder, RemoteDocument doc, CancellationToken ct)
    {
        // An unscanned or infected file is refused by the download endpoint (423).
        // Skipping here keeps that out of the failure count, where it would look
        // like a bug rather than the scanner simply not having run yet.
        if (!string.IsNullOrEmpty(doc.ScanStatus)
            && doc.ScanStatus is not ("CLEAN" or "SKIPPED"))
        {
            CompanionLog.Info($"skipping '{doc.FileName}' — scan status {doc.ScanStatus}");
            return PlaceResult.Unchanged;
        }

        var target = Path.Combine(folder, SafeFileName(doc.FileName));

        // Content-addressed skip. Comparing the server's hash against the bytes on
        // disk means no local index to drift or corrupt: if the file already IS
        // the file, there is nothing to do, whatever the timestamps say.
        if (File.Exists(target) && !string.IsNullOrEmpty(doc.ContentHash)
            && string.Equals(Sha256Of(target), doc.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            ApplyReadOnlyHint(target, doc.CdeStatus);
            return PlaceResult.Unchanged;
        }

        var supersededExisting = false;
        if (File.Exists(target))
        {
            // The safety net. Something external (Acrobat, Word) may have this
            // open; renaming rather than overwriting means their handle stays
            // valid and the file does not vanish underneath them.
            supersededExisting = SupersedeExisting(target);
        }

        // Download to a temp name in the same folder, then move into place, so a
        // half-downloaded file is never visible under the real name — and so the
        // move is same-volume and atomic.
        var temp = target + ".part";
        try
        {
            await _api.DownloadAsync(project.ProjectId, doc.Id, temp, ct);
            ClearReadOnly(target);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }

        ApplyReadOnlyHint(target, doc.CdeStatus);
        return supersededExisting ? PlaceResult.DownloadedOverSuperseded : PlaceResult.Downloaded;
    }

    /// <summary>
    /// Rename the outgoing copy to <c>{Name} (superseded {yyyy-MM-dd}).{ext}</c>.
    /// Returns false when there was nothing to rename or the rename failed — a
    /// failure here must not stop the new revision from landing.
    /// </summary>
    private static bool SupersedeExisting(string target)
    {
        try
        {
            var dir = Path.GetDirectoryName(target)!;
            var stem = Path.GetFileNameWithoutExtension(target);
            var ext = Path.GetExtension(target);
            var stamped = $"{stem}{SupersededMarker}{DateTime.Now:yyyy-MM-dd}){ext}";
            var dest = Path.Combine(dir, stamped);

            // Two revisions superseded on the same day would collide on the name.
            var n = 2;
            while (File.Exists(dest))
                dest = Path.Combine(dir, $"{stem}{SupersededMarker}{DateTime.Now:yyyy-MM-dd} #{n++}){ext}");

            ClearReadOnly(target);
            File.Move(target, dest);
            CompanionLog.Info($"superseded → {Path.GetFileName(dest)}");
            return true;
        }
        catch (Exception ex)
        {
            // Most likely cause: the file is genuinely locked by another process.
            // Log it and let the caller overwrite — losing the safety net for one
            // file is better than refusing to deliver the current revision.
            CompanionLog.Warn($"could not rename the superseded copy of '{Path.GetFileName(target)}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Delete superseded copies older than <see cref="SupersededRetention"/>.
    /// Called on every sync pass and at startup — an age check, not a scheduler,
    /// which is all the design asks for.
    /// </summary>
    public static int PurgeExpiredSuperseded(string folder)
    {
        var purged = 0;
        try
        {
            if (!Directory.Exists(folder)) return 0;
            var cutoff = DateTime.UtcNow - SupersededRetention;
            foreach (var file in Directory.EnumerateFiles(folder, "*" + SupersededMarker.TrimEnd() + "*"))
            {
                try
                {
                    // Match on the marker, never on age alone — a user's own file
                    // that happens to be old must never be touched by this.
                    if (!Path.GetFileName(file).Contains(SupersededMarker, StringComparison.Ordinal)) continue;
                    if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                    ClearReadOnly(file);
                    File.Delete(file);
                    purged++;
                    CompanionLog.Info($"purged expired superseded copy {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    CompanionLog.Warn($"could not purge '{Path.GetFileName(file)}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CompanionLog.Warn($"purge scan failed in {folder}: {ex.Message}");
        }
        return purged;
    }

    /// <summary>
    /// Mark non-WIP copies read-only. A HINT — see the class remarks. WIP copies
    /// are left writable because a WIP document checked out to this Author is the
    /// working copy they are meant to edit.
    /// </summary>
    private static void ApplyReadOnlyHint(string path, string cdeStatus)
    {
        try
        {
            var isWorkingCopy = string.Equals(cdeStatus, "WIP", StringComparison.OrdinalIgnoreCase);
            var attrs = File.GetAttributes(path);
            File.SetAttributes(path, isWorkingCopy
                ? attrs & ~FileAttributes.ReadOnly
                : attrs | FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            // Cosmetic. Never fail a sync over an attribute.
            CompanionLog.Warn($"could not set the read-only hint on '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch (Exception) { /* the move/delete will report the real problem */ }
    }

    /// <summary>
    /// A server-supplied file name lands in a local path, so strip separators and
    /// invalid characters. Without this a document named <c>..\..\evil.exe</c>
    /// writes outside the project folder.
    /// </summary>
    public static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(name)) return "document";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    public static string Sha256Of(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
