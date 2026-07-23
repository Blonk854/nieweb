using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Startup;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for the home-page pinned-reports endpoint
/// <c>GET /api/reports/home</c> (docs/phase-2.md §7.6 <c>RC4</c>).
/// Verifies auth-gating, pin/unpin round-trip via
/// <see cref="AdminReportsEndpoints"/>, ordering, and that locked
/// reports are still surfaced with the <c>IsLocked</c> flag set.
/// </summary>
public sealed class HomeReportsEndpointTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public HomeReportsEndpointTests(NiewebApiFactory factory)
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

    private static readonly Uri HomeRoute = new("/api/reports/home", UriKind.Relative);
    private static readonly Uri ReportsRoute = new("/api/admin/reports", UriKind.Relative);

    private static Uri ReportRoute(int id)
        => new("/api/admin/reports/" + id.ToString(CultureInfo.InvariantCulture), UriKind.Relative);

    private static Uri LockRoute(int id) => new(ReportRoute(id).OriginalString + "/lock", UriKind.Relative);

    private static async Task<AdminReportsEndpoints.ReportDto> CreateReportAsync(
        HttpClient client,
        string title,
        bool isPinnedHome = false,
        int displayOrder = 0)
    {
        var body = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = title,
            OwnerDisplayName = "admin",
            IsPinnedHome = isPinnedHome,
            DisplayOrder = displayOrder,
        };
        using var res = await client.PostAsJsonAsync(ReportsRoute, body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    // -------------------- Auth gating --------------------

    [Fact]
    public async Task HomeReports_Anonymous_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(HomeRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task HomeReports_Reader_Returns200()
    {
        // The home surface must be reachable by any signed-in user,
        // not just Admin, so Readers see their landing page. This is
        // asserted here so RC4 does not regress into an admin-only
        // route by accident.
        await ResetAsync();
        _ = await CreateUserAsync("home.r@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("home.r@t.test", "correctpassword123");
        using var res = await client.GetAsync(HomeRoute);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = await res.Content.ReadFromJsonAsync<List<ReportEndpoints.HomeReportDto>>();
        Assert.NotNull(rows);
        Assert.Empty(rows!);
    }

    // -------------------- Filter & ordering --------------------

    [Fact]
    public async Task HomeReports_Returns_Only_Pinned_Reports()
    {
        await ResetAsync();
        _ = await CreateUserAsync("home.f@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("home.f@t.test", "correctpassword123");
        _ = await CreateReportAsync(client, "Pinned A", isPinnedHome: true);
        _ = await CreateReportAsync(client, "Unpinned B", isPinnedHome: false);
        _ = await CreateReportAsync(client, "Pinned C", isPinnedHome: true);

        using var res = await client.GetAsync(HomeRoute);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = await res.Content.ReadFromJsonAsync<List<ReportEndpoints.HomeReportDto>>();
        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);
        Assert.All(rows, r => Assert.Contains("Pinned", r.Title, StringComparison.Ordinal));
    }

    [Fact]
    public async Task HomeReports_Ordered_By_DisplayOrder_Then_Title()
    {
        await ResetAsync();
        _ = await CreateUserAsync("home.o@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("home.o@t.test", "correctpassword123");
        _ = await CreateReportAsync(client, "Zebra",  isPinnedHome: true, displayOrder: 0);
        _ = await CreateReportAsync(client, "Alpha",  isPinnedHome: true, displayOrder: 0);
        _ = await CreateReportAsync(client, "Middle", isPinnedHome: true, displayOrder: 10);

        using var res = await client.GetAsync(HomeRoute);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = await res.Content.ReadFromJsonAsync<List<ReportEndpoints.HomeReportDto>>();
        Assert.NotNull(rows);
        Assert.Collection(rows!,
            r => Assert.Equal("Alpha", r.Title),
            r => Assert.Equal("Zebra", r.Title),
            r => Assert.Equal("Middle", r.Title));
    }

    // -------------------- Locked pinned reports --------------------

    [Fact]
    public async Task HomeReports_Includes_Locked_Reports_With_Flag()
    {
        // Locked reports must still appear on the home page so the
        // user can discover and unlock them. The SPA renders a
        // "locked" badge based on IsLocked, so this test asserts the
        // flag survives round-trip through /reports/home.
        await ResetAsync();
        _ = await CreateUserAsync("home.l@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("home.l@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Locked pinned", isPinnedHome: true);

        using var lockRes = await client.PostAsJsonAsync(
            LockRoute(report.Id),
            new AdminReportsEndpoints.ReportPasswordRequest { Password = "secret" });
        Assert.Equal(HttpStatusCode.OK, lockRes.StatusCode);

        using var res = await client.GetAsync(HomeRoute);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = await res.Content.ReadFromJsonAsync<List<ReportEndpoints.HomeReportDto>>();
        Assert.NotNull(rows);
        var row = Assert.Single(rows!);
        Assert.Equal(report.Id, row.Id);
        Assert.True(row.IsLocked);
    }

    // -------------------- Projection fidelity --------------------

    [Fact]
    public async Task HomeReports_Projects_Group_And_Entity_Count()
    {
        await ResetAsync();
        _ = await CreateUserAsync("home.p@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("home.p@t.test", "correctpassword123");

        // Create a group and a pinned report linked to it, plus one tile.
        var group = new AdminReportsEndpoints.GroupRequest { Name = "Landing", DisplayOrder = 1 };
        using var gres = await client.PostAsJsonAsync(new Uri("/api/admin/report-groups", UriKind.Relative), group);
        Assert.Equal(HttpStatusCode.Created, gres.StatusCode);
        var groupDto = await gres.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportGroupDto>();
        Assert.NotNull(groupDto);

        var createBody = new AdminReportsEndpoints.CreateReportRequest
        {
            Title = "Landing tile owner",
            OwnerDisplayName = "admin",
            IsPinnedHome = true,
            ReportGroupId = groupDto!.Id,
        };
        using var cres = await client.PostAsJsonAsync(ReportsRoute, createBody);
        Assert.Equal(HttpStatusCode.Created, cres.StatusCode);
        var report = await cres.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(report);

        var tile = new AdminReportsEndpoints.EntityRequest
        {
            TileType = "panel-yield",
            ConfigJson = "{}",
        };
        using var tres = await client.PostAsJsonAsync(
            new Uri("/api/admin/reports/" + report!.Id.ToString(CultureInfo.InvariantCulture) + "/entities", UriKind.Relative),
            tile);
        Assert.Equal(HttpStatusCode.Created, tres.StatusCode);

        using var res = await client.GetAsync(HomeRoute);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = await res.Content.ReadFromJsonAsync<List<ReportEndpoints.HomeReportDto>>();
        var row = Assert.Single(rows!);
        Assert.Equal(report.Id, row.Id);
        Assert.Equal("Landing", row.GroupName);
        Assert.Equal(groupDto.Id, row.ReportGroupId);
        Assert.Equal(1, row.EntityCount);
        Assert.False(row.IsLocked);
    }

    [Fact]
    public async Task HomeReports_Pin_Toggles_Via_Update()
    {
        // Verifies the RC4 workflow: users pin/unpin from the report
        // editor and the home list follows immediately.
        await ResetAsync();
        _ = await CreateUserAsync("home.t@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("home.t@t.test", "correctpassword123");
        var report = await CreateReportAsync(client, "Togglable", isPinnedHome: false);

        using var empty = await client.GetAsync(HomeRoute);
        var rows = await empty.Content.ReadFromJsonAsync<List<ReportEndpoints.HomeReportDto>>();
        Assert.Empty(rows!);

        var upd = new AdminReportsEndpoints.UpdateReportRequest
        {
            Title = report.Title,
            IsPinnedHome = true,
        };
        using var putRes = await client.PutAsJsonAsync(ReportRoute(report.Id), upd);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        using var afterPin = await client.GetAsync(HomeRoute);
        rows = await afterPin.Content.ReadFromJsonAsync<List<ReportEndpoints.HomeReportDto>>();
        Assert.Single(rows!);

        var unpin = new AdminReportsEndpoints.UpdateReportRequest
        {
            Title = report.Title,
            IsPinnedHome = false,
        };
        using var putRes2 = await client.PutAsJsonAsync(ReportRoute(report.Id), unpin);
        Assert.Equal(HttpStatusCode.OK, putRes2.StatusCode);

        using var afterUnpin = await client.GetAsync(HomeRoute);
        rows = await afterUnpin.Content.ReadFromJsonAsync<List<ReportEndpoints.HomeReportDto>>();
        Assert.Empty(rows!);
    }
}
