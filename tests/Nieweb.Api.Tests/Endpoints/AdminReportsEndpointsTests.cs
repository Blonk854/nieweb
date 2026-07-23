using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Startup;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for <see cref="AdminReportsEndpoints"/>
/// (docs/phase-2.md §7.6 <c>RC1</c>). Reuses <see cref="NiewebApiFactory"/>
/// so auth, audit, and EF wiring all match a real host.
/// </summary>
public sealed class AdminReportsEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;
    private static readonly int[] ExpectedTileOrders = new[] { 0, 1, 2 };
    private static readonly string[] DuplicateTileTypes = new[] { "panel-yield", "pareto" };
    private static readonly string[] DuplicateExpectedTileTypes = new[] { "panel-yield", "pareto" };

    public AdminReportsEndpointsTests(NiewebApiFactory factory)
    {
        _factory = factory;
        EnsureDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        await db.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<NiewebRole>>();
        foreach (var name in new[] { BootstrapAdmin.RoleReader, BootstrapAdmin.RoleAuthor, BootstrapAdmin.RoleAdmin })
        {
            if (!await roles.RoleExistsAsync(name))
            {
                _ = await roles.CreateAsync(new NiewebRole
                {
                    Name = name,
                    NormalizedName = name.ToUpperInvariant(),
                });
            }
        }
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        db.ReportEntities.RemoveRange(db.ReportEntities);
        db.Reports.RemoveRange(db.Reports);
        db.ReportGroups.RemoveRange(db.ReportGroups);
        db.AuditEvents.RemoveRange(db.AuditEvents);
        db.UserRoles.RemoveRange(db.UserRoles);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();
    }

    private async Task<NiewebUser> CreateUserAsync(string email, string password, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
        var user = new NiewebUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
            CreatedUtc = DateTime.UtcNow,
        };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        if (roles.Length > 0)
        {
            Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        }
        return user;
    }

    private async Task<HttpClient> LoggedInClientAsync(string email, string password)
    {
        using var anon = _factory.CreateClient();
        var login = new AuthEndpoints.LoginRequest { Email = email, Password = password };
        using var res = await anon.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var payload = await res.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    private static string GroupsRoute(string suffix = "") => "/api/admin/report-groups" + suffix;
    private static string ReportsRoute(string suffix = "") => "/api/admin/reports" + suffix;
    private static string ReportRoute(int id, string suffix = "")
        => "/api/admin/reports/" + id.ToString(CultureInfo.InvariantCulture) + suffix;
    private static string EntityRoute(int reportId, int entityId)
        => ReportRoute(reportId, "/entities/" + entityId.ToString(CultureInfo.InvariantCulture));

    // -------------------- Auth-gate checks --------------------

    [Fact]
    public async Task Groups_List_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri(GroupsRoute(), UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Groups_List_AsReader_Returns403()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rg.r@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("rg.r@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri(GroupsRoute(), UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Reports_List_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri(ReportsRoute(), UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // -------------------- Group CRUD --------------------

    [Fact]
    public async Task Groups_Create_And_List_HappyPath()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rg.a@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rg.a@t.test", "correctpassword123");

        var body = new AdminReportsEndpoints.GroupRequest { Name = "Daily production", DisplayOrder = 2 };
        using var create = await client.PostAsJsonAsync(new Uri(GroupsRoute(), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var dto = await create.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportGroupDto>();
        Assert.NotNull(dto);
        Assert.Equal("Daily production", dto!.Name);
        Assert.Equal(2, dto.DisplayOrder);
        Assert.Equal(0, dto.ReportCount);

        using var list = await client.GetAsync(new Uri(GroupsRoute(), UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var rows = await list.Content.ReadFromJsonAsync<List<AdminReportsEndpoints.ReportGroupDto>>();
        Assert.NotNull(rows);
        Assert.Single(rows!);
        Assert.Equal("Daily production", rows![0].Name);
    }

    [Fact]
    public async Task Groups_Create_Duplicate_Returns409()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rg.dup@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rg.dup@t.test", "correctpassword123");

        var body = new AdminReportsEndpoints.GroupRequest { Name = "Weekly summary" };
        using var first = await client.PostAsJsonAsync(new Uri(GroupsRoute(), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await client.PostAsJsonAsync(new Uri(GroupsRoute(), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Groups_Update_Renames_Row()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rg.upd@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rg.upd@t.test", "correctpassword123");

        var body = new AdminReportsEndpoints.GroupRequest { Name = "Old" };
        using var created = await client.PostAsJsonAsync(new Uri(GroupsRoute(), UriKind.Relative), body);
        var dto = await created.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportGroupDto>();
        Assert.NotNull(dto);

        var upd = new AdminReportsEndpoints.GroupRequest { Name = "New", DisplayOrder = 5 };
        using var update = await client.PutAsJsonAsync(
            new Uri(GroupsRoute("/" + dto!.Id.ToString(CultureInfo.InvariantCulture)), UriKind.Relative),
            upd);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportGroupDto>();
        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Name);
        Assert.Equal(5, updated.DisplayOrder);
    }

    [Fact]
    public async Task Groups_Delete_Nulls_Report_GroupId_Instead_Of_Cascading()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rg.del@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rg.del@t.test", "correctpassword123");

        var groupBody = new AdminReportsEndpoints.GroupRequest { Name = "Temporary" };
        using var createdG = await client.PostAsJsonAsync(new Uri(GroupsRoute(), UriKind.Relative), groupBody);
        var group = await createdG.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportGroupDto>();

        var reportBody = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "Daily boards",
            OwnerDisplayName = "admin",
            ReportGroupId = group!.Id,
        };
        using var createdR = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), reportBody);
        Assert.Equal(HttpStatusCode.Created, createdR.StatusCode);
        var report = await createdR.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.Equal(group.Id, report!.ReportGroupId);

        using var del = await client.DeleteAsync(
            new Uri(GroupsRoute("/" + group.Id.ToString(CultureInfo.InvariantCulture)), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // The report survives, but its GroupId is now null.
        using var fetched = await client.GetAsync(
            new Uri(ReportRoute(report.Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var detail = await fetched.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDetailDto>();
        Assert.NotNull(detail);
        Assert.Null(detail!.Report.ReportGroupId);
    }

    // -------------------- Report CRUD --------------------

    [Fact]
    public async Task Reports_Create_MissingTitle_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.title@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.title@t.test", "correctpassword123");

        var body = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = string.Empty,
            OwnerDisplayName = "admin",
        };
        using var res = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Create_Unknown_Group_Returns409()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.grp@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.grp@t.test", "correctpassword123");

        var body = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "Orphan",
            OwnerDisplayName = "admin",
            ReportGroupId = 999_999,
        };
        using var res = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Create_NegativeRefresh_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.ref@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.ref@t.test", "correctpassword123");

        var body = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "Bad refresh",
            OwnerDisplayName = "admin",
            RefreshFrequencySeconds = -5,
        };
        using var res = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Full_Lifecycle_With_Tiles()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.life@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.life@t.test", "correctpassword123");

        // 1) Create a report shell.
        var createBody = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "SMT overview",
            Description = "Post-reflow overview",
            OwnerDisplayName = "admin",
            RefreshFrequencySeconds = 300,
            DisplayOrder = 1,
        };
        using var created = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), createBody);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var report = await created.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(report);
        Assert.Equal(0, report!.EntityCount);

        // 2) Add three tiles (auto-append via DisplayOrder=-1).
        var tileOrders = new List<int>();
        foreach (var type in new[] { "panel-yield", "pareto", "trend-chart" })
        {
            var body = new AdminReportsEndpoints.EntityRequest
            {
                TileType = type,
                Title = type + " tile",
                ConfigJson = "{\"axis\":\"machine\"}",
            };
            using var res = await client.PostAsJsonAsync(
                new Uri(ReportRoute(report.Id, "/entities"), UriKind.Relative), body);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportEntityDto>();
            Assert.NotNull(dto);
            tileOrders.Add(dto!.DisplayOrder);
        }
        Assert.Equal(ExpectedTileOrders, tileOrders);

        // 3) GET /{id} returns tiles ordered by DisplayOrder.
        using var fetched = await client.GetAsync(new Uri(ReportRoute(report.Id), UriKind.Relative));
        var detail = await fetched.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(3, detail!.Entities.Count);
        Assert.Equal("panel-yield", detail.Entities[0].TileType);
        Assert.Equal("trend-chart", detail.Entities[2].TileType);

        // 4) Update the middle tile (change TileType + DisplayOrder).
        var middle = detail.Entities[1];
        var updateBody = new AdminReportsEndpoints.EntityRequest
        {
            TileType = "deviation-chart",
            Title = "Deviation X",
            DisplayOrder = 5,
            ConfigJson = "{\"axis\":\"delta-x\"}",
        };
        using var updated = await client.PutAsJsonAsync(
            new Uri(EntityRoute(report.Id, middle.Id), UriKind.Relative), updateBody);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        // 5) Remove the first tile.
        using var removed = await client.DeleteAsync(
            new Uri(EntityRoute(report.Id, detail.Entities[0].Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // 6) Final layout: 2 tiles (trend + deviation moved to end).
        using var final = await client.GetAsync(new Uri(ReportRoute(report.Id), UriKind.Relative));
        var afterDetail = await final.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDetailDto>();
        Assert.Equal(2, afterDetail!.Entities.Count);
        // trend-chart had DisplayOrder=2, deviation-chart now has DisplayOrder=5.
        Assert.Equal("trend-chart", afterDetail.Entities[0].TileType);
        Assert.Equal("deviation-chart", afterDetail.Entities[1].TileType);

        // 7) Delete the whole report cascades tiles.
        using var deleted = await client.DeleteAsync(new Uri(ReportRoute(report.Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Verify tiles are gone at the DB layer.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var remaining = await db.ReportEntities.CountAsync(e => e.ReportId == report.Id);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task Reports_Get_Unknown_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.404@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.404@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri(ReportRoute(999_999), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Entities_Add_To_Unknown_Report_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("re.404@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("re.404@t.test", "correctpassword123");
        var body = new AdminReportsEndpoints.EntityRequest { TileType = "pareto" };
        using var res = await client.PostAsJsonAsync(
            new Uri(ReportRoute(999_999, "/entities"), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Entities_Update_Missing_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("re.uh@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("re.uh@t.test", "correctpassword123");
        var createBody = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "Empty",
            OwnerDisplayName = "admin",
        };
        using var created = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), createBody);
        var report = await created.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        var body = new AdminReportsEndpoints.EntityRequest { TileType = "pareto" };
        using var res = await client.PutAsJsonAsync(
            new Uri(EntityRoute(report!.Id, 999_999), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Entities_Add_MissingTileType_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("re.tt@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("re.tt@t.test", "correctpassword123");
        var createBody = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "NoType",
            OwnerDisplayName = "admin",
        };
        using var created = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), createBody);
        var report = await created.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        var body = new AdminReportsEndpoints.EntityRequest { TileType = "  " };
        using var res = await client.PostAsJsonAsync(
            new Uri(ReportRoute(report!.Id, "/entities"), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Audit_Trail_Written_For_Full_Lifecycle()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.aud@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.aud@t.test", "correctpassword123");

        var body = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "Audited",
            OwnerDisplayName = "admin",
        };
        using var created = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), body);
        var report = await created.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        var tile = new AdminReportsEndpoints.EntityRequest { TileType = "pareto" };
        using var addTile = await client.PostAsJsonAsync(
            new Uri(ReportRoute(report!.Id, "/entities"), UriKind.Relative), tile);
        var tileDto = await addTile.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportEntityDto>();
        using var removeTile = await client.DeleteAsync(
            new Uri(EntityRoute(report.Id, tileDto!.Id), UriKind.Relative));
        using var removeReport = await client.DeleteAsync(new Uri(ReportRoute(report.Id), UriKind.Relative));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var events = await db.AuditEvents
            .Where(a => a.EventType.StartsWith("report."))
            .OrderBy(a => a.Id)
            .Select(a => a.EventType)
            .ToListAsync();
        Assert.Contains("report.created", events);
        Assert.Contains("report.entity.added", events);
        Assert.Contains("report.entity.removed", events);
        Assert.Contains("report.deleted", events);
    }

    // -------------------- RC3: lock / unlock / duplicate --------------------

    private static string LockRoute(int id) => ReportRoute(id, "/lock");
    private static string UnlockRoute(int id) => ReportRoute(id, "/unlock");
    private static string DuplicateRoute(int id) => ReportRoute(id, "/duplicate");

    private static async Task<AdminReportsEndpoints.ReportDto> CreateReportAsync(
        HttpClient client,
        string title = "Locked candidate")
    {
        var body = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = title,
            OwnerDisplayName = "admin",
        };
        using var res = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    [Fact]
    public async Task Reports_Lock_HappyPath_Sets_IsLocked_And_Stores_Hash()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.lock.ok@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.lock.ok@t.test", "correctpassword123");
        var report = await CreateReportAsync(client);
        Assert.False(report.IsLocked);

        var body = new AdminReportsEndpoints.ReportPasswordRequest { Password = "shhh-secret" };
        using var res = await client.PostAsJsonAsync(new Uri(LockRoute(report.Id), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.True(dto!.IsLocked);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var entity = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id);
        Assert.True(entity.IsLocked);
        Assert.False(string.IsNullOrEmpty(entity.LockPasswordHash));
        // Argon2id PHC format starts with $argon2id$.
        Assert.StartsWith("$argon2id$", entity.LockPasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_Lock_EmptyPassword_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.lock.mt@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.lock.mt@t.test", "correctpassword123");
        var report = await CreateReportAsync(client);
        var body = new AdminReportsEndpoints.ReportPasswordRequest { Password = "   " };
        using var res = await client.PostAsJsonAsync(new Uri(LockRoute(report.Id), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Lock_Unknown_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.lock.404@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.lock.404@t.test", "correctpassword123");
        var body = new AdminReportsEndpoints.ReportPasswordRequest { Password = "anything" };
        using var res = await client.PostAsJsonAsync(new Uri(LockRoute(999_999), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Lock_Rotates_Existing_Hash()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.lock.rot@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.lock.rot@t.test", "correctpassword123");
        var report = await CreateReportAsync(client);

        var first = new AdminReportsEndpoints.ReportPasswordRequest { Password = "first-pass" };
        using var r1 = await client.PostAsJsonAsync(new Uri(LockRoute(report.Id), UriKind.Relative), first);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        string hashBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
            hashBefore = (await db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id)).LockPasswordHash!;
        }

        var second = new AdminReportsEndpoints.ReportPasswordRequest { Password = "second-pass" };
        using var r2 = await client.PostAsJsonAsync(new Uri(LockRoute(report.Id), UriKind.Relative), second);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var after = (await db2.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id)).LockPasswordHash!;
        Assert.NotEqual(hashBefore, after);
    }

    [Fact]
    public async Task Reports_Unlock_HappyPath_Clears_IsLocked()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.unl.ok@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.unl.ok@t.test", "correctpassword123");
        var report = await CreateReportAsync(client);
        var lockBody = new AdminReportsEndpoints.ReportPasswordRequest { Password = "sesame" };
        using var lockRes = await client.PostAsJsonAsync(new Uri(LockRoute(report.Id), UriKind.Relative), lockBody);
        Assert.Equal(HttpStatusCode.OK, lockRes.StatusCode);

        using var unlockRes = await client.PostAsJsonAsync(new Uri(UnlockRoute(report.Id), UriKind.Relative), lockBody);
        Assert.Equal(HttpStatusCode.OK, unlockRes.StatusCode);
        var dto = await unlockRes.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.False(dto!.IsLocked);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var entity = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id);
        Assert.False(entity.IsLocked);
        Assert.Null(entity.LockPasswordHash);
    }

    [Fact]
    public async Task Reports_Unlock_WrongPassword_Returns401_And_Leaves_State()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.unl.wp@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.unl.wp@t.test", "correctpassword123");
        var report = await CreateReportAsync(client);
        using var l = await client.PostAsJsonAsync(
            new Uri(LockRoute(report.Id), UriKind.Relative),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "correct" });
        Assert.Equal(HttpStatusCode.OK, l.StatusCode);

        using var wrong = await client.PostAsJsonAsync(
            new Uri(UnlockRoute(report.Id), UriKind.Relative),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var entity = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id);
        Assert.True(entity.IsLocked);
        Assert.NotNull(entity.LockPasswordHash);
    }

    [Fact]
    public async Task Reports_Unlock_NotLocked_Returns409()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.unl.nl@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.unl.nl@t.test", "correctpassword123");
        var report = await CreateReportAsync(client);
        using var res = await client.PostAsJsonAsync(
            new Uri(UnlockRoute(report.Id), UriKind.Relative),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "anything" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Unlock_Unknown_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.unl.404@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.unl.404@t.test", "correctpassword123");
        using var res = await client.PostAsJsonAsync(
            new Uri(UnlockRoute(999_999), UriKind.Relative),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "anything" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Update_Header_Preserves_IsLocked_And_Hash()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.upd.lk@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.upd.lk@t.test", "correctpassword123");
        var report = await CreateReportAsync(client);
        using var l = await client.PostAsJsonAsync(
            new Uri(LockRoute(report.Id), UriKind.Relative),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "kept" });
        Assert.Equal(HttpStatusCode.OK, l.StatusCode);

        string hashBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
            hashBefore = (await db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id)).LockPasswordHash!;
        }

        // PUT header explicitly says IsLocked=false — the server must ignore it.
        var updateBody = new AdminReportsEndpoints.UpdateReportRequest
        {
            Title = "Renamed but still locked",
            IsLocked = false,
        };
        using var upd = await client.PutAsJsonAsync(
            new Uri(ReportRoute(report.Id), UriKind.Relative), updateBody);
        Assert.Equal(HttpStatusCode.OK, upd.StatusCode);
        var dto = await upd.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.True(dto!.IsLocked);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var after = await db2.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id);
        Assert.True(after.IsLocked);
        Assert.Equal(hashBefore, after.LockPasswordHash);
    }

    [Fact]
    public async Task Reports_Create_Ignores_IsLocked_Bit()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.cr.lk@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.cr.lk@t.test", "correctpassword123");
        var body = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "Sneaky locked",
            OwnerDisplayName = "admin",
            IsLocked = true,
        };
        using var res = await client.PostAsJsonAsync(new Uri(ReportsRoute(), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.False(dto!.IsLocked);
    }

    [Fact]
    public async Task Reports_Duplicate_Clones_Report_Unlocked_With_Tiles()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.dup.ok@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.dup.ok@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Original");

        foreach (var type in DuplicateTileTypes)
        {
            var tile = new AdminReportsEndpoints.EntityRequest
            {
                TileType = type,
                ConfigJson = "{\"axis\":\"machine\"}",
            };
            using var t = await client.PostAsJsonAsync(
                new Uri(ReportRoute(report.Id, "/entities"), UriKind.Relative), tile);
            Assert.Equal(HttpStatusCode.Created, t.StatusCode);
        }

        // Lock the source; the duplicate must still come out unlocked.
        using var lk = await client.PostAsJsonAsync(
            new Uri(LockRoute(report.Id), UriKind.Relative),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "src-pass" });
        Assert.Equal(HttpStatusCode.OK, lk.StatusCode);

        var dupBody = new AdminReportsEndpoints.DuplicateReportRequest
        {
            Title = "Copy of Original",
            OwnerDisplayName = "admin",
        };
        using var dup = await client.PostAsJsonAsync(
            new Uri(DuplicateRoute(report.Id), UriKind.Relative), dupBody);
        Assert.Equal(HttpStatusCode.Created, dup.StatusCode);
        var clone = await dup.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(clone);
        Assert.NotEqual(report.Id, clone!.Id);
        Assert.False(clone.IsLocked);
        Assert.Equal(2, clone.EntityCount);

        // Tiles: same types + config, new ids.
        using var detailRes = await client.GetAsync(new Uri(ReportRoute(clone.Id), UriKind.Relative));
        var detail = await detailRes.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDetailDto>();
        Assert.Equal(DuplicateExpectedTileTypes, detail!.Entities.Select(e => e.TileType).ToArray());
        Assert.All(detail.Entities, e => Assert.Equal("{\"axis\":\"machine\"}", e.ConfigJson));

        // Clone's LockPasswordHash must be null at the DB layer.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var entity = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == clone.Id);
        Assert.False(entity.IsLocked);
        Assert.Null(entity.LockPasswordHash);
    }

    [Fact]
    public async Task Reports_Duplicate_DefaultsTitle_When_Omitted()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.dup.dt@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.dup.dt@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Weekly");
        var body = new AdminReportsEndpoints.DuplicateReportRequest
        {
            OwnerDisplayName = "admin",
        };
        using var res = await client.PostAsJsonAsync(
            new Uri(DuplicateRoute(report.Id), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.Equal("Copy of Weekly", dto!.Title);
    }

    [Fact]
    public async Task Reports_Duplicate_Unknown_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.dup.404@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.dup.404@t.test", "correctpassword123");
        var body = new AdminReportsEndpoints.DuplicateReportRequest
        {
            Title = "x",
            OwnerDisplayName = "admin",
        };
        using var res = await client.PostAsJsonAsync(
            new Uri(DuplicateRoute(999_999), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Lock_Unlock_Duplicate_Emit_Audit_Events()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.lud.aud@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.lud.aud@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Audit source");

        using var lk = await client.PostAsJsonAsync(
            new Uri(LockRoute(report.Id), UriKind.Relative),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "pw" });
        Assert.Equal(HttpStatusCode.OK, lk.StatusCode);
        using var un = await client.PostAsJsonAsync(
            new Uri(UnlockRoute(report.Id), UriKind.Relative),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "pw" });
        Assert.Equal(HttpStatusCode.OK, un.StatusCode);
        using var dup = await client.PostAsJsonAsync(
            new Uri(DuplicateRoute(report.Id), UriKind.Relative),
            new AdminReportsEndpoints.DuplicateReportRequest
            {
                Title = "Clone",
                OwnerDisplayName = "admin",
            });
        Assert.Equal(HttpStatusCode.Created, dup.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var events = await db.AuditEvents
            .Where(a => a.EventType.StartsWith("report."))
            .Select(a => a.EventType)
            .ToListAsync();
        Assert.Contains("report.locked", events);
        Assert.Contains("report.unlocked", events);
        Assert.Contains("report.duplicated", events);
    }

    // -------------------- F14: pin / unpin --------------------

    private static string PinRoute(int id) => ReportRoute(id, "/pin");
    private static string UnpinRoute(int id) => ReportRoute(id, "/unpin");

    [Fact]
    public async Task Reports_Pin_HappyPath_Sets_IsPinnedHome()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.pin.ok@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.pin.ok@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Pin candidate");
        Assert.False(report.IsPinnedHome);

        using var res = await client.PostAsync(new Uri(PinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(dto);
        Assert.True(dto!.IsPinnedHome);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var entity = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id);
        Assert.True(entity.IsPinnedHome);
    }

    [Fact]
    public async Task Reports_Unpin_HappyPath_Clears_IsPinnedHome()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.unpin.ok@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.unpin.ok@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Unpin candidate");

        using var pin = await client.PostAsync(new Uri(PinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, pin.StatusCode);

        using var res = await client.PostAsync(new Uri(UnpinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.False(dto!.IsPinnedHome);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var entity = await db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id);
        Assert.False(entity.IsPinnedHome);
    }

    [Fact]
    public async Task Reports_Pin_Is_Idempotent()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.pin.idem@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.pin.idem@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Pin twice");

        using var r1 = await client.PostAsync(new Uri(PinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        using var r2 = await client.PostAsync(new Uri(PinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var dto = await r2.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.True(dto!.IsPinnedHome);
    }

    [Fact]
    public async Task Reports_Pin_Unknown_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.pin.404@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.pin.404@t.test", "correctpassword123");
        using var res = await client.PostAsync(new Uri(PinRoute(999_999), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Unpin_Unknown_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.unpin.404@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.unpin.404@t.test", "correctpassword123");
        using var res = await client.PostAsync(new Uri(UnpinRoute(999_999), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Pin_NonAdmin_Returns403()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.pin.admin@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var admin = await LoggedInClientAsync("rp.pin.admin@t.test", "correctpassword123");
        var report = await CreateReportAsync(admin, "Guarded pin");

        _ = await CreateUserAsync("rp.pin.reader@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var reader = await LoggedInClientAsync("rp.pin.reader@t.test", "correctpassword123");
        using var res = await reader.PostAsync(new Uri(PinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Pin_Anonymous_Returns401()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.pin.anon@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var admin = await LoggedInClientAsync("rp.pin.anon@t.test", "correctpassword123");
        var report = await CreateReportAsync(admin, "Anon pin");

        using var anon = _factory.CreateClient();
        using var res = await anon.PostAsync(new Uri(PinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Reports_Pin_Writes_Audit_Event()
    {
        await ResetAsync();
        _ = await CreateUserAsync("rp.pin.audit@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("rp.pin.audit@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Audited pin");

        using var pin = await client.PostAsync(new Uri(PinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, pin.StatusCode);
        using var unpin = await client.PostAsync(new Uri(UnpinRoute(report.Id), UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, unpin.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var events = await db.AuditEvents
            .Where(a => a.EventType == "report.pinned" || a.EventType == "report.unpinned")
            .Select(a => a.EventType)
            .ToListAsync();
        Assert.Contains("report.pinned", events);
        Assert.Contains("report.unpinned", events);
    }
}
