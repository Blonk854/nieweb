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
/// HTTP integration tests for <see cref="AuthorReportsEndpoints"/>
/// (docs/phase-2.md §7.6 <c>RC2</c> — self-service author path). Verifies
/// the role gate (<c>Author</c>/<c>Admin</c> only) and the own-only
/// ownership scoping that keeps one author out of another's reports.
/// </summary>
public sealed class AuthorReportsEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AuthorReportsEndpointsTests(NiewebApiFactory factory)
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

    private const string ReportsRoot = "/api/reports";
    private const string MineRoute = "/api/reports/mine";
    private static string ReportRoute(int id) => "/api/reports/" + id.ToString(CultureInfo.InvariantCulture);
    private static string EntitiesRoute(int id) => ReportRoute(id) + "/entities";
    private static string EntityRoute(int id, int entityId)
        => EntitiesRoute(id) + "/" + entityId.ToString(CultureInfo.InvariantCulture);
    private static string DuplicateRoute(int id) => ReportRoute(id) + "/duplicate";

    private static async Task<AdminReportsEndpoints.ReportDto> CreateReportAsync(HttpClient client, string title)
    {
        using var res = await client.PostAsJsonAsync(
            new Uri(ReportsRoot, UriKind.Relative),
            new { title, displayOrder = 0 });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    // -------------------- role gate --------------------

    [Fact]
    public async Task Mine_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri(MineRoute, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Create_AsReader_Returns403()
    {
        await ResetAsync();
        _ = await CreateUserAsync("ar.reader@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("ar.reader@t.test", "correctpassword123");
        using var res = await client.PostAsJsonAsync(
            new Uri(ReportsRoot, UriKind.Relative),
            new { title = "Nope", displayOrder = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // -------------------- own-report lifecycle --------------------

    [Fact]
    public async Task Author_CanCreateListAndGetOwnReport()
    {
        await ResetAsync();
        _ = await CreateUserAsync("ar.a1@t.test", "correctpassword123", BootstrapAdmin.RoleAuthor);
        using var client = await LoggedInClientAsync("ar.a1@t.test", "correctpassword123");

        var created = await CreateReportAsync(client, "My first report");
        Assert.Equal("My first report", created.Title);
        Assert.False(created.IsPinnedHome);
        Assert.Equal("ar.a1", created.OwnerDisplayName);

        using var listRes = await client.GetAsync(new Uri(MineRoute, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var mine = await listRes.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto[]>();
        Assert.NotNull(mine);
        Assert.Contains(mine!, r => r.Id == created.Id);

        using var getRes = await client.GetAsync(new Uri(ReportRoute(created.Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
    }

    [Fact]
    public async Task Author_UpdateOwnReport_CannotPinToHome()
    {
        await ResetAsync();
        _ = await CreateUserAsync("ar.a2@t.test", "correctpassword123", BootstrapAdmin.RoleAuthor);
        using var client = await LoggedInClientAsync("ar.a2@t.test", "correctpassword123");
        var created = await CreateReportAsync(client, "Editable");

        // Even if the client tries to smuggle isPinnedHome, the author
        // endpoint ignores it (the field isn't on the request DTO).
        using var res = await client.PutAsJsonAsync(
            new Uri(ReportRoute(created.Id), UriKind.Relative),
            new { title = "Edited", displayOrder = 3, isPinnedHome = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var updated = await res.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(updated);
        Assert.Equal("Edited", updated!.Title);
        Assert.False(updated.IsPinnedHome);
    }

    [Fact]
    public async Task Author_AddTileAndDeleteOwnReport()
    {
        await ResetAsync();
        _ = await CreateUserAsync("ar.a3@t.test", "correctpassword123", BootstrapAdmin.RoleAuthor);
        using var client = await LoggedInClientAsync("ar.a3@t.test", "correctpassword123");
        var created = await CreateReportAsync(client, "With tiles");

        using var addRes = await client.PostAsJsonAsync(
            new Uri(EntitiesRoute(created.Id), UriKind.Relative),
            new { tileType = "pareto", title = (string?)null, displayOrder = -1, configJson = "{}" });
        Assert.Equal(HttpStatusCode.Created, addRes.StatusCode);

        using var delRes = await client.DeleteAsync(new Uri(ReportRoute(created.Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        using var getRes = await client.GetAsync(new Uri(ReportRoute(created.Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
    }

    // -------------------- ownership isolation --------------------

    [Fact]
    public async Task Author_CannotReadUpdateDeleteAnothersReport()
    {
        await ResetAsync();
        _ = await CreateUserAsync("ar.owner@t.test", "correctpassword123", BootstrapAdmin.RoleAuthor);
        _ = await CreateUserAsync("ar.other@t.test", "correctpassword123", BootstrapAdmin.RoleAuthor);
        using var owner = await LoggedInClientAsync("ar.owner@t.test", "correctpassword123");
        using var other = await LoggedInClientAsync("ar.other@t.test", "correctpassword123");

        var report = await CreateReportAsync(owner, "Owner's report");

        using var getRes = await other.GetAsync(new Uri(ReportRoute(report.Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        using var putRes = await other.PutAsJsonAsync(
            new Uri(ReportRoute(report.Id), UriKind.Relative),
            new { title = "Hijacked", displayOrder = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, putRes.StatusCode);

        using var delRes = await other.DeleteAsync(new Uri(ReportRoute(report.Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, delRes.StatusCode);

        using var tileRes = await other.PostAsJsonAsync(
            new Uri(EntitiesRoute(report.Id), UriKind.Relative),
            new { tileType = "pareto", displayOrder = -1, configJson = "{}" });
        Assert.Equal(HttpStatusCode.Forbidden, tileRes.StatusCode);

        // The other author's "mine" list must not include it.
        using var mineRes = await other.GetAsync(new Uri(MineRoute, UriKind.Relative));
        var mine = await mineRes.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto[]>();
        Assert.DoesNotContain(mine!, r => r.Id == report.Id);
    }

    [Fact]
    public async Task Author_MissingReport_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("ar.a4@t.test", "correctpassword123", BootstrapAdmin.RoleAuthor);
        using var client = await LoggedInClientAsync("ar.a4@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri(ReportRoute(999999), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // -------------------- duplicate (cross-owner allowed) --------------------

    [Fact]
    public async Task Author_CanDuplicateAnothersReportIntoOwnedCopy()
    {
        await ResetAsync();
        _ = await CreateUserAsync("ar.src@t.test", "correctpassword123", BootstrapAdmin.RoleAuthor);
        _ = await CreateUserAsync("ar.cloner@t.test", "correctpassword123", BootstrapAdmin.RoleAuthor);
        using var src = await LoggedInClientAsync("ar.src@t.test", "correctpassword123");
        using var cloner = await LoggedInClientAsync("ar.cloner@t.test", "correctpassword123");

        var report = await CreateReportAsync(src, "Shared template");
        using var addRes = await src.PostAsJsonAsync(
            new Uri(EntitiesRoute(report.Id), UriKind.Relative),
            new { tileType = "panelYield", displayOrder = -1, configJson = "{}" });
        Assert.Equal(HttpStatusCode.Created, addRes.StatusCode);

        using var dupRes = await cloner.PostAsJsonAsync(
            new Uri(DuplicateRoute(report.Id), UriKind.Relative),
            new { title = "My copy" });
        Assert.Equal(HttpStatusCode.Created, dupRes.StatusCode);
        var copy = await dupRes.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDto>();
        Assert.NotNull(copy);
        Assert.Equal("My copy", copy!.Title);
        Assert.Equal("ar.cloner", copy.OwnerDisplayName);
        Assert.NotEqual(report.Id, copy.Id);

        // The clone is owned by the cloner and carries the source tile.
        using var getRes = await cloner.GetAsync(new Uri(ReportRoute(copy.Id), UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var detail = await getRes.Content.ReadFromJsonAsync<AdminReportsEndpoints.ReportDetailDto>();
        Assert.NotNull(detail);
        Assert.Single(detail!.Entities);
    }

    // -------------------- admin may also use the author surface --------------------

    [Fact]
    public async Task Admin_CanUseAuthorSurface()
    {
        await ResetAsync();
        _ = await CreateUserAsync("ar.admin@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("ar.admin@t.test", "correctpassword123");
        var created = await CreateReportAsync(client, "Admin-authored");
        Assert.Equal("Admin-authored", created.Title);
    }
}
