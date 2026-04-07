using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StingBIM.Tests;

/// <summary>
/// Integration tests covering Issues, Documents, Meetings, Transmittals,
/// Compliance, Workflows, Warnings, Search, SeqSync, Admin, ProjectMembers, MIM, and TagSync.
/// </summary>
public class CoreApiTests : IClassFixture<StingBimWebApplicationFactory>
{
    private readonly StingBimWebApplicationFactory _factory;
    private readonly string _projBase;

    public CoreApiTests(StingBimWebApplicationFactory factory)
    {
        _factory = factory;
        _projBase = $"/api/projects/{TestData.ProjectId}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Issues
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Issues_CreateAndList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var createResp = await client.PostAsJsonAsync($"{_projBase}/issues", new
        {
            type = "RFI",
            title = "Missing fire rating data",
            description = "Level 3 elements need fire rating",
            priority = "HIGH",
            discipline = "A"
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.GetProperty("issueCode").GetString()!.StartsWith("RFI-"));

        var listResp = await client.GetAsync($"{_projBase}/issues");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetProperty("items").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Issues_Update_ChangesStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Create issue first
        var createResp = await client.PostAsJsonAsync($"{_projBase}/issues", new
        {
            type = "NCR",
            title = "Duct clearance violation",
            priority = "CRITICAL"
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var issueId = created.GetProperty("id").GetGuid();

        // Update status
        var updateResp = await client.PutAsJsonAsync(
            $"{_projBase}/issues/{issueId}", new { status = "IN_PROGRESS" });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
    }

    [Fact]
    public async Task Issues_SLAReport_ReturnsData()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{_projBase}/issues/sla");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("totalOpen", out _));
    }

    [Fact]
    public async Task Issues_OtherTenant_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");
        var response = await client.GetAsync($"{_projBase}/issues");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Documents
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Documents_CreateAndList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var createResp = await client.PostAsJsonAsync($"{_projBase}/documents", new
        {
            fileName = "TST-STR-XX-L01-DR-0001.pdf",
            documentType = "DR",
            discipline = "S",
            revision = "P01"
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var listResp = await client.GetAsync($"{_projBase}/documents");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetProperty("items").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Documents_TransitionState_WipToShared()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Create doc
        var createResp = await client.PostAsJsonAsync($"{_projBase}/documents", new
        {
            fileName = "TST-MEC-XX-L02-DR-0002.pdf",
            documentType = "DR",
            discipline = "M"
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var docId = created.GetProperty("id").GetGuid();

        // Transition WIP → SHARED
        var transResp = await client.PutAsJsonAsync(
            $"{_projBase}/documents/{docId}/state",
            new { newState = "SHARED", suitabilityCode = "S3" });
        Assert.Equal(HttpStatusCode.OK, transResp.StatusCode);
    }

    [Fact]
    public async Task Documents_GetHistory()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Create a doc with a transition
        var createResp = await client.PostAsJsonAsync($"{_projBase}/documents", new
        {
            fileName = "TST-ARC-XX-RF-DR-0003.pdf"
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var docId = created.GetProperty("id").GetGuid();

        var histResp = await client.GetAsync($"{_projBase}/documents/{docId}/history");
        Assert.Equal(HttpStatusCode.OK, histResp.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Compliance
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Compliance_PushAndGetLatest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var pushResp = await client.PostAsJsonAsync($"{_projBase}/compliance", new
        {
            totalElements = 1000,
            taggedComplete = 700,
            taggedIncomplete = 100,
            untagged = 200,
            fullyResolved = 650,
            staleCount = 10,
            placeholderCount = 50,
            warningCount = 25,
            warningHealthScore = 75,
            tagPercent = 80.0,
            strictPercent = 65.0,
            containerPercent = 78.0,
            ragStatus = "AMBER"
        });
        Assert.Equal(HttpStatusCode.Created, pushResp.StatusCode);

        var latestResp = await client.GetAsync($"{_projBase}/compliance/latest");
        Assert.Equal(HttpStatusCode.OK, latestResp.StatusCode);
        var latest = await latestResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1000, latest.GetProperty("totalElements").GetInt32());
    }

    [Fact]
    public async Task Compliance_History_ReturnsArray()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Push a snapshot first
        await client.PostAsJsonAsync($"{_projBase}/compliance", new
        {
            totalElements = 500, taggedComplete = 400, taggedIncomplete = 50,
            untagged = 50, fullyResolved = 350, staleCount = 5,
            placeholderCount = 20, warningCount = 10, warningHealthScore = 85,
            tagPercent = 90.0, strictPercent = 70.0, containerPercent = 88.0,
            ragStatus = "GREEN"
        });

        var histResp = await client.GetAsync($"{_projBase}/compliance/history");
        Assert.Equal(HttpStatusCode.OK, histResp.StatusCode);
        var hist = await histResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(hist.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Compliance_Trend_ReturnsDailyData()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var trendResp = await client.GetAsync($"{_projBase}/compliance/trend?days=7");
        Assert.Equal(HttpStatusCode.OK, trendResp.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Meetings
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Meetings_CreateAndList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var createResp = await client.PostAsJsonAsync($"{_projBase}/meetings", new
        {
            title = "Weekly BIM Coordination",
            meetingType = "BIM_COORDINATION",
            scheduledAt = DateTime.UtcNow.AddDays(1)
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var listResp = await client.GetAsync($"{_projBase}/meetings");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
    }

    [Fact]
    public async Task Meetings_AddActionItem_AndGetOpen()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Create meeting
        var meetingResp = await client.PostAsJsonAsync($"{_projBase}/meetings", new
        {
            title = "Clash Resolution",
            scheduledAt = DateTime.UtcNow.AddDays(2)
        });
        var meeting = await meetingResp.Content.ReadFromJsonAsync<JsonElement>();
        var meetingId = meeting.GetProperty("id").GetGuid();

        // Add action item
        var actionResp = await client.PostAsJsonAsync(
            $"{_projBase}/meetings/{meetingId}/actions", new
            {
                description = "Resolve MEP clash at Level 3",
                assignee = "John",
                dueDate = DateTime.UtcNow.AddDays(7)
            });
        Assert.Equal(HttpStatusCode.Created, actionResp.StatusCode);

        // Get open actions
        var openResp = await client.GetAsync($"{_projBase}/meetings/actions/open");
        Assert.Equal(HttpStatusCode.OK, openResp.StatusCode);
        var openActions = await openResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(openActions.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Meetings_LogMinutes()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var meetingResp = await client.PostAsJsonAsync($"{_projBase}/meetings", new
        {
            title = "Design Review",
            scheduledAt = DateTime.UtcNow.AddDays(3)
        });
        var meeting = await meetingResp.Content.ReadFromJsonAsync<JsonElement>();
        var meetingId = meeting.GetProperty("id").GetGuid();

        var minutesResp = await client.PutAsJsonAsync(
            $"{_projBase}/meetings/{meetingId}/minutes",
            new { minutes = "Discussed structural coordination issues." });
        Assert.Equal(HttpStatusCode.OK, minutesResp.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Transmittals
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Transmittals_CreateAndList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var createResp = await client.PostAsJsonAsync($"{_projBase}/transmittals", new
        {
            recipient = "external@consultant.com",
            notes = "Stage 4 design package"
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.GetProperty("transmittalCode").GetString()!.StartsWith("TX-"));

        var listResp = await client.GetAsync($"{_projBase}/transmittals");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
    }

    [Fact]
    public async Task Transmittals_MarkSent()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var createResp = await client.PostAsJsonAsync($"{_projBase}/transmittals", new
        {
            recipient = "client@example.com",
            notes = "Monthly report"
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var txId = created.GetProperty("id").GetGuid();

        var sentResp = await client.PutAsync(
            $"{_projBase}/transmittals/{txId}/send", null);
        Assert.Equal(HttpStatusCode.OK, sentResp.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Workflows
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Workflows_LogRunAndGetHistory()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var logResp = await client.PostAsJsonAsync($"{_projBase}/workflows/run", new
        {
            presetName = "DailyQA",
            userName = "Test Admin",
            stepsPassed = 8,
            stepsFailed = 0,
            stepsSkipped = 2,
            durationMs = 12500.0,
            complianceBefore = 75.0,
            complianceAfter = 82.0
        });
        Assert.Equal(HttpStatusCode.Created, logResp.StatusCode);

        var histResp = await client.GetAsync($"{_projBase}/workflows/history");
        Assert.Equal(HttpStatusCode.OK, histResp.StatusCode);
        var hist = await histResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(hist.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Workflows_Trend_ReturnsData()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{_projBase}/workflows/trend?days=30");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Warnings
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Warnings_PushReport()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var pushResp = await client.PostAsJsonAsync($"{_projBase}/warnings/report", new
        {
            totalWarnings = 45,
            healthScore = 72,
            byCategoryJson = """{"Geometric": 20, "MEP": 15, "Data": 10}""",
            bySeverityJson = """{"CRITICAL": 2, "HIGH": 8, "MEDIUM": 25, "LOW": 10}"""
        });
        Assert.Equal(HttpStatusCode.Created, pushResp.StatusCode);
    }

    [Fact]
    public async Task Warnings_SaveBaseline()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var baseResp = await client.PostAsJsonAsync($"{_projBase}/warnings/baseline", new
        {
            warningCount = 45,
            healthScore = 72,
            totalElements = 1000,
            compliancePercent = 80.0
        });
        Assert.Equal(HttpStatusCode.OK, baseResp.StatusCode);
    }

    [Fact]
    public async Task Warnings_Trend_ReturnsData()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{_projBase}/warnings/trend?days=14");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Search
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Search_ValidQuery_ReturnsResults()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Create an issue to search for
        await client.PostAsJsonAsync($"{_projBase}/issues", new
        {
            type = "RFI",
            title = "Searchable fire rating issue"
        });

        var response = await client.GetAsync("/api/search?q=fire+rating&limit=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_ShortQuery_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/search?q=a");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SEQ Sync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SeqSync_SyncAndGet()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var syncResp = await client.PostAsJsonAsync($"{_projBase}/seq/sync", new
        {
            counters = new Dictionary<string, int>
            {
                ["M_HVAC_SUP_AHU"] = 42,
                ["E_LV_PWR_DB"] = 15
            }
        });
        Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);
        var syncResult = await syncResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(syncResult.GetProperty("merged").GetArrayLength() >= 2);

        var getResp = await client.GetAsync($"{_projBase}/seq");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var counters = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(counters.GetArrayLength() >= 2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Admin
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Admin_GetOrg_ReturnsOrgDetails()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/admin/org");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Organisation", json.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Admin_GetUsers_ReturnsTenantUsers()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(users.GetArrayLength() >= 2); // admin + member
    }

    [Fact]
    public async Task Admin_CreateUser_Returns201()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/admin/users", new
        {
            email = "newuser@test.org",
            displayName = "New User",
            password = "StrongPass99!",
            role = "Member",
            iso19650Role = "E"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Admin_MemberRole_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");
        var response = await client.GetAsync("/api/admin/org");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_GetAuditLog_ReturnsArray()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/admin/audit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_GetLicenses_ReturnsList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/admin/licenses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var licenses = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(licenses.GetArrayLength() >= 1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Project Members
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Members_AddAndList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var addResp = await client.PostAsJsonAsync($"{_projBase}/members", new
        {
            userId = TestData.MemberUserId,
            projectRole = "Engineer",
            iso19650Role = "E"
        });
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);

        var listResp = await client.GetAsync($"{_projBase}/members");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var members = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(members.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Members_GetRoles_ReturnsISO19650Roles()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{_projBase}/members/roles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var roles = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(roles.GetArrayLength() >= 10); // 14 ISO 19650 roles
    }

    [Fact]
    public async Task Members_InviteByEmail()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync($"{_projBase}/members/invite", new
        {
            email = "invited@test.org",
            displayName = "Invited User",
            projectRole = "Viewer",
            iso19650Role = "C"
        });
        // Should succeed — creates pending user and adds as member
        Assert.True(
            response.StatusCode == HttpStatusCode.Created ||
            response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Members_MemberRole_CannotAdd()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "member@test.org", "Password123!");
        var response = await client.PostAsJsonAsync($"{_projBase}/members", new
        {
            userId = TestData.AdminUserId,
            projectRole = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MIM (Model Information Management)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Mim_CreateAssetAndList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var createResp = await client.PostAsJsonAsync($"{_projBase}/mim/assets", new
        {
            assetTag = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001",
            assetName = "Air Handling Unit 1",
            categoryName = "Mechanical Equipment",
            discipline = "M",
            systemCode = "HVAC",
            functionCode = "SUP",
            productCode = "AHU",
            location = "BLD1",
            zone = "Z01",
            level = "L02"
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var listResp = await client.GetAsync($"{_projBase}/mim/assets");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
    }

    [Fact]
    public async Task Mim_Dashboard_ReturnsMetrics()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"{_projBase}/mim/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Mim_CreateMaintenanceTask()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Create asset first
        var assetResp = await client.PostAsJsonAsync($"{_projBase}/mim/assets", new
        {
            assetTag = "E-BLD1-Z01-L01-LV-PWR-DB-0001",
            assetName = "Distribution Board 1",
            discipline = "E"
        });
        var asset = await assetResp.Content.ReadFromJsonAsync<JsonElement>();
        var assetId = asset.GetProperty("id").GetGuid();

        var taskResp = await client.PostAsJsonAsync($"{_projBase}/mim/maintenance", new
        {
            assetId,
            description = "Annual thermographic survey",
            taskType = "PPM",
            priority = "MEDIUM",
            dueDate = DateTime.UtcNow.AddDays(30),
            frequencyDays = 365
        });
        Assert.Equal(HttpStatusCode.Created, taskResp.StatusCode);
    }

    [Fact]
    public async Task Mim_BulkCreateAssets()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var assets = new[]
        {
            new { assetTag = "M-BLD1-Z01-L01-HVAC-SUP-FCU-0001", assetName = "FCU 1", discipline = "M" },
            new { assetTag = "M-BLD1-Z01-L01-HVAC-SUP-FCU-0002", assetName = "FCU 2", discipline = "M" },
            new { assetTag = "M-BLD1-Z01-L01-HVAC-SUP-FCU-0003", assetName = "FCU 3", discipline = "M" }
        };

        var response = await client.PostAsJsonAsync($"{_projBase}/mim/assets/bulk", assets);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, result.GetProperty("created").GetInt32());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Tag Sync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TagSync_SyncElements()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var syncResp = await client.PostAsJsonAsync("/api/tagsync/sync", new
        {
            projectId = TestData.ProjectId,
            userName = "TestUser",
            revitVersion = "2025",
            pluginVersion = "1.0.0",
            elements = new[]
            {
                new
                {
                    revitElementId = "12345",
                    uniqueId = "abc-def-123",
                    disc = "M", loc = "BLD1", zone = "Z01",
                    lvl = "L02", sys = "HVAC", func = "SUP",
                    prod = "AHU", seq = "0001",
                    tag1 = "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001",
                    categoryName = "Mechanical Equipment",
                    familyName = "AHU_Standard",
                    isComplete = true,
                    isFullyResolved = true
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);
        var result = await syncResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("received").GetInt32());
    }

    [Fact]
    public async Task TagSync_GetCompliance()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Sync some elements first
        await client.PostAsJsonAsync("/api/tagsync/sync", new
        {
            projectId = TestData.ProjectId,
            userName = "TestUser",
            revitVersion = "2025",
            pluginVersion = "1.0.0",
            elements = new[]
            {
                new
                {
                    revitElementId = "99999",
                    uniqueId = "xyz-789",
                    disc = "E", loc = "BLD1", zone = "Z01",
                    lvl = "L01", sys = "LV", func = "PWR",
                    prod = "DB", seq = "0001",
                    tag1 = "E-BLD1-Z01-L01-LV-PWR-DB-0001",
                    categoryName = "Electrical Equipment",
                    isComplete = true, isFullyResolved = true
                }
            }
        });

        var compResp = await client.GetAsync($"/api/tagsync/compliance/{TestData.ProjectId}");
        Assert.Equal(HttpStatusCode.OK, compResp.StatusCode);
    }

    [Fact]
    public async Task TagSync_GetElements_Paginated()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync(
            $"/api/tagsync/elements/{TestData.ProjectId}?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Tenant Isolation (cross-cutting)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TenantIsolation_OtherTenantCannotAccessDocuments()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");
        var response = await client.GetAsync($"{_projBase}/documents");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantCannotAccessMeetings()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");
        var response = await client.GetAsync($"{_projBase}/meetings");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantCannotAccessMim()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");
        var response = await client.GetAsync($"{_projBase}/mim/assets");
        // Could be 404 (project not found) or 403 (MIM not enabled)
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantCannotSyncTags()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(
            "admin@other.org", "Password123!");
        var response = await client.PostAsJsonAsync("/api/tagsync/sync", new
        {
            projectId = TestData.ProjectId,
            userName = "Intruder",
            revitVersion = "2025",
            pluginVersion = "1.0.0",
            elements = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
