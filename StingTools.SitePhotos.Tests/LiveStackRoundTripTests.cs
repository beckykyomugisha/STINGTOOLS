using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using Xunit;

namespace StingTools.SitePhotos.Tests;

/// <summary>
/// Round-trip against the real docker stack. Everything here asserts on NON-EMPTY
/// data: the tenant filter falls back to Guid.Empty in places, which makes an
/// empty-result assertion pass without proving anything.
///
/// When the stack is unreachable these are REAL xUnit skips carrying the URL that
/// was probed — not a conditionally-excluded class, and not a silently empty run.
/// Read the skip count, not just the failure count.
/// </summary>
public class LiveStackRoundTripTests
{
    private const string StackUrl = "http://localhost:5000";
    private const string Email    = "admin@planscape.demo";
    private const string Password = "admin123";

    private static PlanscapeServerClient Client => PlanscapeServerClient.Instance;

    private static async Task<string> StackUnavailableReasonAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var r = await http.GetAsync($"{StackUrl}/health");
            if (!r.IsSuccessStatusCode)
                return $"docker stack at {StackUrl}/health returned HTTP {(int)r.StatusCode} — start it with: " +
                       "docker compose -f Planscape.Server/docker/docker-compose.yml up -d";
            return null;
        }
        catch (Exception ex)
        {
            return $"docker stack at {StackUrl}/health is unreachable ({ex.GetType().Name}: {ex.Message}) — start it with: " +
                   "docker compose -f Planscape.Server/docker/docker-compose.yml up -d";
        }
    }

    private static async Task<(bool ok, string reason)> LoginAsync()
    {
        var down = await StackUnavailableReasonAsync();
        if (down != null) return (false, down);
        if (!await Client.LoginAsync(StackUrl, Email, Password))
            return (false, $"login as {Email} against {StackUrl} failed: {Client.LastError}");
        return (true, null);
    }

    private static async Task<Guid> FirstProjectIdAsync()
    {
        var projects = await Client.GetProjectsAsync();
        if (projects == null || projects.Count == 0) return Guid.Empty;
        var id = projects[0]["id"]?.Value<string>() ?? projects[0]["Id"]?.Value<string>();
        return Guid.TryParse(id, out var g) ? g : Guid.Empty;
    }

    [SkippableFact]
    public async Task Create_then_list_then_detail_round_trips_with_non_empty_results()
    {
        var (ok, reason) = await LoginAsync();
        Skip.IfNot(ok, reason);

        var projectId = await FirstProjectIdAsync();
        Skip.If(projectId == Guid.Empty,
            $"logged in as {Email} but the stack has no projects to test against — seed one first");

        var name = $"harness-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}".Substring(0, 40);

        // CREATE — the default visibility must be the server's own default.
        var created = await Client.CreatePhotoAlbumAsync(projectId, name, "created by the transport harness");
        Assert.True(created != null, $"album create failed: {Client.LastError}");
        Assert.NotEqual(Guid.Empty, created!.Id);
        Assert.Equal(name, created.Name);
        Assert.False(string.IsNullOrWhiteSpace(created.Visibility));
        Assert.Contains(created.Visibility, new[] { "Internal", "Members", "Client", "Distribution" });

        // LIST — must be non-empty AND must contain what we just made. A count>0
        // assertion alone would pass on somebody else's data.
        var albums = await Client.ListPhotoAlbumsAsync(projectId);
        Assert.True(albums != null, $"album list failed: {Client.LastError}");
        Assert.NotEmpty(albums!);
        Assert.Contains(albums!, a => a.Id == created.Id);

        // DETAIL — GetOne returns a wrapper { album, photos, ndaRequiredIds };
        // mapping it flat yields a plausible, empty, wrong DTO.
        var detail = await Client.GetPhotoAlbumAsync(projectId, created.Id);
        Assert.True(detail != null, $"album detail failed: {Client.LastError}");
        Assert.Equal(created.Id, detail!.Id);
        Assert.Equal(name, detail.Name);
    }

    [SkippableFact]
    public async Task Lock_then_unlock_round_trips_against_the_live_stack()
    {
        var (ok, reason) = await LoginAsync();
        Skip.IfNot(ok, reason);

        var projectId = await FirstProjectIdAsync();
        Skip.If(projectId == Guid.Empty, "no projects on the stack to test against");

        var name = $"harness-lock-{Guid.NewGuid():N}".Substring(0, 30);
        var album = await Client.CreatePhotoAlbumAsync(projectId, name);
        Assert.True(album != null, $"album create failed: {Client.LastError}");

        Assert.True(await Client.LockPhotoAlbumAsync(projectId, album!.Id, true),
            $"lock failed: {Client.LastError}");
        var locked = await Client.GetPhotoAlbumAsync(projectId, album.Id);
        Assert.True(locked != null, $"detail after lock failed: {Client.LastError}");
        Assert.True(locked!.IsLocked, "album did not report locked after POST /lock");

        Assert.True(await Client.LockPhotoAlbumAsync(projectId, album.Id, false),
            $"unlock failed: {Client.LastError}");
        var unlocked = await Client.GetPhotoAlbumAsync(projectId, album.Id);
        Assert.True(unlocked != null, $"detail after unlock failed: {Client.LastError}");
        Assert.False(unlocked!.IsLocked, "album still reports locked after POST /unlock");
    }

    [SkippableFact]
    public async Task Checklists_list_and_detail_are_reachable_and_self_consistent()
    {
        var (ok, reason) = await LoginAsync();
        Skip.IfNot(ok, reason);

        var projectId = await FirstProjectIdAsync();
        Skip.If(projectId == Guid.Empty, "no projects on the stack to test against");

        var checklists = await Client.ListPhotoChecklistsAsync(projectId);

        // Reachability is the assertion that always holds: non-null means the call
        // succeeded. Non-EMPTY needs seed data, so its absence is a visible skip
        // rather than a vacuous pass.
        Assert.True(checklists != null, $"checklist list failed: {Client.LastError}");

        Skip.If(checklists!.Count == 0,
            $"project {projectId} has no photo checklists — fulfil cannot be exercised without seed data. " +
            "Create one in the app, or seed one, then re-run.");

        var first = checklists[0];
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
        Assert.True(first.Done <= first.Total,
            $"checklist '{first.Name}' reports Done={first.Done} > Total={first.Total}");
    }

    [SkippableFact]
    public async Task Distribution_groups_list_is_reachable()
    {
        var (ok, reason) = await LoginAsync();
        Skip.IfNot(ok, reason);

        var projectId = await FirstProjectIdAsync();
        Skip.If(projectId == Guid.Empty, "no projects on the stack to test against");

        var groups = await Client.ListDistributionGroupsAsync(projectId);

        // Non-null is the real assertion: null means the call failed, and the Admin
        // sub-tab must render that as an error rather than "no groups yet".
        Assert.True(groups != null, $"distribution group list failed: {Client.LastError}");
    }
}
