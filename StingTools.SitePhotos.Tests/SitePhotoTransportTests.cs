using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StingTools.BIMManager;
using Xunit;

// PlanscapeServerClient is a process-wide singleton holding one HttpClient and one
// session. Parallel classes would race each other's base URL and token.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace StingTools.SitePhotos.Tests;

public class SitePhotoTransportTests
{
    private static PlanscapeServerClient Client => PlanscapeServerClient.Instance;

    private static async Task<CaptureServer> AuthedServerAsync()
    {
        var srv = new CaptureServer();
        var ok = await Client.LoginAsync(srv.BaseUrl, "harness@test", "pw");
        Assert.True(ok, $"harness login failed against the capture server: {Client.LastError}");
        Assert.True(Client.IsConnected, "client reports not connected after a 200 login");
        return srv;
    }

    // ─────────────────────────────────────────────────────────────────────
    // D2 — the defect this whole PR exists to prevent.
    // A transport failure must be reported as a failure. An empty collection
    // is an ANSWER, and renders as "this project has no albums".
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Server_down_every_list_returns_null_with_LastError_never_an_empty_list()
    {
        var srv = await AuthedServerAsync();

        // Authenticated first, THEN the server dies — this is the real-world shape
        // (session established, API goes away) rather than "never connected".
        srv.Kill();

        var albums = await Client.ListPhotoAlbumsAsync(Guid.NewGuid());
        var albumsErr = Client.LastError;

        var checklists = await Client.ListPhotoChecklistsAsync(Guid.NewGuid());
        var checklistsErr = Client.LastError;

        var groups = await Client.ListDistributionGroupsAsync(Guid.NewGuid());
        var groupsErr = Client.LastError;

        // Null, not empty. The distinction is the entire point.
        Assert.Null(albums);
        Assert.Null(checklists);
        Assert.Null(groups);

        Assert.False(string.IsNullOrWhiteSpace(albumsErr), "ListPhotoAlbumsAsync failed without setting LastError");
        Assert.False(string.IsNullOrWhiteSpace(checklistsErr), "ListPhotoChecklistsAsync failed without setting LastError");
        Assert.False(string.IsNullOrWhiteSpace(groupsErr), "ListDistributionGroupsAsync failed without setting LastError");
    }

    [Fact]
    public async Task Server_down_album_detail_returns_null_not_an_empty_album()
    {
        var srv = await AuthedServerAsync();
        srv.Kill();

        var album = await Client.GetPhotoAlbumAsync(Guid.NewGuid(), Guid.NewGuid());

        // A non-null album with zero photos would render as "Album is empty" over a
        // failed load, inviting an operator to re-add photos that are already there.
        Assert.Null(album);
        Assert.False(string.IsNullOrWhiteSpace(Client.LastError));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Client-side guards must fire BEFORE the request.
    // Proven by counting what arrived at the server, not by reading the message.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalid_album_visibility_is_refused_before_any_request_is_made()
    {
        using var srv = await AuthedServerAsync();
        var marker = srv.RequestCount;               // post-login baseline

        // "Project" is the value the old stub defaulted to. The server rejects it
        // with invalid_visibility; the client should not even ask.
        var album = await Client.CreatePhotoAlbumAsync(Guid.NewGuid(), "harness album", null, "Project");

        Assert.Null(album);
        Assert.False(string.IsNullOrWhiteSpace(Client.LastError));
        Assert.Empty(srv.PathsSince(marker));        // ← no HTTP call was made
    }

    [Fact]
    public async Task Valid_visibilities_are_all_accepted_by_the_guard()
    {
        using var srv = await AuthedServerAsync();
        foreach (var v in new[] { "Internal", "Members", "Client", "Distribution" })
        {
            var marker = srv.RequestCount;
            await Client.CreatePhotoAlbumAsync(Guid.NewGuid(), "a", null, v);
            // The call may fail at the server (no route registered) but it must have
            // been ATTEMPTED — otherwise the guard is rejecting a legal value.
            Assert.True(srv.PathsSince(marker).Count > 0,
                $"visibility '{v}' is valid per the server contract but the client refused it locally");
        }
    }

    [SkippableFact]
    public async Task Pdf_export_over_the_200_cap_is_refused_before_any_request_is_made()
    {
        using var srv = await AuthedServerAsync();
        var marker = srv.RequestCount;

        var tooMany = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();
        var ok = await Client.ExportPhotosAsync(Guid.NewGuid(), tooMany, "pdf");

        Assert.False(ok);

        // The only public overload that carries photo ids resolves an output
        // directory (SitePhotos.cs:550) BEFORE the cap check (:575). That resolution
        // needs a Revit document context, so in a headless host the folder guard
        // wins and the cap is unreachable. Skip visibly rather than assert on a path
        // this environment cannot enter -- and note the ordering, because it means a
        // 201-photo export is refused locally either way.
        Skip.If((Client.LastError ?? "").Contains("Could not resolve an output folder"),
            "cap unreachable headlessly: ExportPhotosAsync(projectId, photoIds, format) resolves the "
            + "output directory before the 200-photo PDF cap, and OutputLocationHelper needs a Revit "
            + "document context. LastError was: " + Client.LastError);

        Assert.Contains("200", Client.LastError ?? "");
        Assert.Empty(srv.PathsSince(marker));        // ← refused before the round trip
    }

    [SkippableFact]
    public async Task Pdf_export_at_the_cap_is_allowed_through()
    {
        using var srv = await AuthedServerAsync();
        var marker = srv.RequestCount;

        var atCap = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToList();
        await Client.ExportPhotosAsync(Guid.NewGuid(), atCap, "pdf");

        Skip.If((Client.LastError ?? "").Contains("Could not resolve an output folder"),
            "same headless limitation as the over-cap test: the output directory is resolved before "
            + "the cap is evaluated. LastError was: " + Client.LastError);

        // Proves the guard is a cap and not a blanket refusal — an off-by-one here
        // would silently block legitimate 200-photo exports.
        Assert.True(srv.PathsSince(marker).Count > 0, "a 200-photo PDF export is at the cap and must be attempted");
    }

    // ─────────────────────────────────────────────────────────────────────
    // lock and unlock are TWO routes, not a boolean field.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lock_and_unlock_hit_two_distinct_routes()
    {
        using var srv = await AuthedServerAsync();
        srv.Routes.Insert(0, (p => p.EndsWith("/lock") || p.EndsWith("/unlock"),
            _ => (200, "application/json", Encoding.UTF8.GetBytes("{\"id\":\"" + Guid.NewGuid() + "\"}"))));

        var albumId = Guid.NewGuid();

        var m1 = srv.RequestCount;
        await Client.LockPhotoAlbumAsync(Guid.NewGuid(), albumId, true);
        var lockPaths = srv.PathsSince(m1);

        var m2 = srv.RequestCount;
        await Client.LockPhotoAlbumAsync(Guid.NewGuid(), albumId, false);
        var unlockPaths = srv.PathsSince(m2);

        Assert.Contains(lockPaths, p => p.EndsWith("/lock", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(unlockPaths, p => p.EndsWith("/unlock", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lockPaths, p => p.EndsWith("/unlock", StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────────
    // photo-export streams a BINARY body. It is not JSON.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pdf_export_writes_a_real_pdf_not_json()
    {
        using var srv = await AuthedServerAsync();

        // %PDF-1.7 header + trailer. If the client parsed this as JSON, or wrote the
        // JSON error envelope to disk, the magic-number assertion below catches it.
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\ntrailer\n%%EOF\n");
        srv.Routes.Insert(0, (p => p.Contains("photo-export"),
            _ => (200, "application/pdf", pdf)));

        var outPath = Path.Combine(Path.GetTempPath(), $"sp-harness-{Guid.NewGuid():N}.pdf");
        try
        {
            var written = await Client.ExportPhotosAsync(Guid.NewGuid(), outPath, Guid.NewGuid(), "pdf");

            Assert.False(string.IsNullOrWhiteSpace(written), $"export returned no path: {Client.LastError}");
            Assert.True(File.Exists(outPath), "export reported success but wrote no file");

            var bytes = await File.ReadAllBytesAsync(outPath);
            Assert.True(bytes.Length > 0, "export wrote a zero-byte file");
            Assert.Equal(pdf.Length, bytes.Length);

            // Magic number — the actual proof it is a PDF and not a JSON envelope.
            Assert.Equal((byte)'%', bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'D', bytes[2]);
            Assert.Equal((byte)'F', bytes[3]);
        }
        finally { try { File.Delete(outPath); } catch { } }
    }

    [Fact]
    public async Task An_empty_200_export_is_treated_as_failure_and_leaves_no_file()
    {
        using var srv = await AuthedServerAsync();
        srv.Routes.Insert(0, (p => p.Contains("photo-export"),
            _ => (200, "application/zip", Array.Empty<byte>())));

        var outPath = Path.Combine(Path.GetTempPath(), $"sp-harness-{Guid.NewGuid():N}.zip");
        try
        {
            var written = await Client.ExportPhotosAsync(Guid.NewGuid(), outPath, Guid.NewGuid(), "zip");

            // A 0-byte "export" is a plausible-looking artefact of a failure.
            // Reporting success here is the same anti-pattern as an empty list.
            Assert.True(string.IsNullOrWhiteSpace(written), "a zero-byte export reported success");
            Assert.False(File.Exists(outPath), "a zero-byte export left a file on disk");
            Assert.False(string.IsNullOrWhiteSpace(Client.LastError));
        }
        finally { try { File.Delete(outPath); } catch { } }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Share link — expiry is an ABSOLUTE instant on the wire.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Share_link_expiry_parses_to_a_real_absolute_time_in_the_future()
    {
        using var srv = await AuthedServerAsync();
        var expected = DateTime.UtcNow.AddDays(7);
        srv.Routes.Insert(0, (p => p.Contains("photo-share-links"),
            _ => (200, "application/json", Encoding.UTF8.GetBytes(
                "{\"token\":\"tok_harness\",\"expiresAt\":\"" + expected.ToString("o") + "\",\"forceRedacted\":false}"))));

        var link = await Client.CreatePhotoShareLinkAsync(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromDays(7), "harness");

        Assert.NotNull(link);
        Assert.False(string.IsNullOrWhiteSpace(link!.Token));
        Assert.NotEqual(default, link.ExpiresAt);                       // not an unparsed zero
        Assert.True(link.ExpiresAt.ToUniversalTime() > DateTime.UtcNow, // a real future instant
            $"share link expiry is not in the future: {link.ExpiresAt:o}");
        Assert.True(Math.Abs((link.ExpiresAt.ToUniversalTime() - expected).TotalMinutes) < 5,
            $"expiry drifted from the wire value: got {link.ExpiresAt:o}, wire said {expected:o}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // #517 — the SAME rule, extended to the distribution-group calls that PR
    // adds. Its first draft returned `new List<T>()` on failure for three of
    // them and said so in its own doc comments ("Empty list on any failure").
    // That is the albums defect (#550 / #605) reintroduced in a different pane:
    // an empty member list renders as "No members yet", and a transmittal sent
    // to a group whose members failed to load goes to nobody, deliberately.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Server_down_group_members_returns_null_not_an_empty_member_list()
    {
        var srv = await AuthedServerAsync();
        srv.Kill();

        var members = await Client.ListDistributionGroupMembersAsync(Guid.NewGuid(), Guid.NewGuid());
        var err = Client.LastError;

        Assert.Null(members);
        Assert.False(string.IsNullOrWhiteSpace(err),
            "ListDistributionGroupMembersAsync failed without setting LastError");
    }

    [Fact]
    public async Task Server_down_resolve_recipients_returns_null_not_an_empty_recipient_list()
    {
        var srv = await AuthedServerAsync();
        srv.Kill();

        var recipients = await Client.ResolveDistributionGroupRecipientsAsync(Guid.NewGuid(), "Client Team");
        var err = Client.LastError;

        // The worst of the three to get wrong: an empty recipient list is a
        // perfectly plausible transmittal that reaches no one.
        Assert.Null(recipients);
        Assert.False(string.IsNullOrWhiteSpace(err),
            "ResolveDistributionGroupRecipientsAsync failed without setting LastError");
    }

    [Fact]
    public async Task Server_down_create_group_returns_null_and_is_not_mistakable_for_success()
    {
        var srv = await AuthedServerAsync();
        srv.Kill();

        var created = await Client.CreateDistributionGroupAsync(Guid.NewGuid(), "Client Team");
        var err = Client.LastError;

        // Create used to return bool, and its caller guarded with `if (grp == null)` —
        // always false against a bool, so a failed create showed the user nothing and
        // the list simply refreshed. main still emits CS0472 at that call site.
        Assert.Null(created);
        Assert.False(string.IsNullOrWhiteSpace(err),
            "CreateDistributionGroupAsync failed without setting LastError");
    }
}
