using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Audit;
using Nieweb.Api.Endpoints;
using Nieweb.Api.Startup;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for <see cref="AdminBoardSvgSourcesEndpoints"/>
/// (docs/phase-2.md §7.5 <c>TC4</c> Phase A). Reuses
/// <see cref="NiewebApiFactory"/> so auth, audit, and EF wiring all
/// match a real host.
/// </summary>
public sealed class AdminBoardSvgSourcesEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AdminBoardSvgSourcesEndpointsTests(NiewebApiFactory factory)
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
        db.BoardSvgSources.RemoveRange(db.BoardSvgSources);
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

    private static string Route(string suffix) => "/api/admin/board-svgs/sources" + suffix;

    private static string Route(int id, string suffix)
        => "/api/admin/board-svgs/sources/" + id.ToString(CultureInfo.InvariantCulture) + suffix;

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri(Route(string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task List_AsReader_Returns403()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.r@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("bs.r@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri(Route(string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task List_AsAdmin_EmptyThenSeeded()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.a@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.a@t.test", "correctpassword123");

        using var empty = await client.GetAsync(new Uri(Route(string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var rows = await empty.Content.ReadFromJsonAsync<List<AdminBoardSvgSourcesEndpoints.BoardSvgSourceDto>>();
        Assert.NotNull(rows);
        Assert.Empty(rows!);

        var create = new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
        {
            MachineName = "AOI-Line1-Post",
            UncPath = @"\\aoi-l1-post\svg",
            IsEnabled = true,
        };
        using var post = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), create);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        using var listed = await client.GetAsync(new Uri(Route(string.Empty), UriKind.Relative));
        var listedRows = await listed.Content.ReadFromJsonAsync<List<AdminBoardSvgSourcesEndpoints.BoardSvgSourceDto>>();
        Assert.Single(listedRows!);
        Assert.Equal("AOI-Line1-Post", listedRows![0].MachineName);
        Assert.Equal(@"\\aoi-l1-post\svg", listedRows[0].UncPath);
        Assert.True(listedRows[0].IsEnabled);
        Assert.Null(listedRows[0].LastSyncedUtc);
    }

    [Fact]
    public async Task Post_WithBlankMachineName_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.bn@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.bn@t.test", "correctpassword123");
        var body = new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
        {
            MachineName = "   ",
            UncPath = @"\\host\share",
        };
        using var res = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Post_WithBlankPath_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.bp@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.bp@t.test", "correctpassword123");
        var body = new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
        {
            MachineName = "AOI-1",
            UncPath = "",
        };
        using var res = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Post_DuplicateMachineName_Returns409()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.dp@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.dp@t.test", "correctpassword123");
        var body = new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
        {
            MachineName = "AOI-1",
            UncPath = @"\\a\1",
        };
        using var first = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var body2 = new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
        {
            MachineName = "AOI-1",
            UncPath = @"\\b\2",
        };
        using var second = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), body2);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Put_TogglesEnabledAndWritesAudit()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.up@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.up@t.test", "correctpassword123");

        var post = await client.PostAsJsonAsync(
            new Uri(Route(string.Empty), UriKind.Relative),
            new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
            {
                MachineName = "AOI-2",
                UncPath = @"\\host\share",
                IsEnabled = true,
            });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<AdminBoardSvgSourcesEndpoints.BoardSvgSourceDto>();

        using var put = await client.PutAsJsonAsync(
            new Uri(Route(created!.Id, string.Empty), UriKind.Relative),
            new AdminBoardSvgSourcesEndpoints.UpdateSourceRequest
            {
                MachineName = "AOI-2-renamed",
                UncPath = @"\\host\share",
                IsEnabled = false,
            });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await put.Content.ReadFromJsonAsync<AdminBoardSvgSourcesEndpoints.BoardSvgSourceDto>();
        Assert.Equal("AOI-2-renamed", updated!.MachineName);
        Assert.False(updated.IsEnabled);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(e =>
            e.EventType == AuditEventTypes.BoardSvgSourceUpdated
            && e.TargetId == created.Id.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Put_RenameToTakenName_Returns409()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.rn@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.rn@t.test", "correctpassword123");

        var a = await client.PostAsJsonAsync(
            new Uri(Route(string.Empty), UriKind.Relative),
            new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
            {
                MachineName = "A",
                UncPath = @"\\a\1",
            });
        var b = await client.PostAsJsonAsync(
            new Uri(Route(string.Empty), UriKind.Relative),
            new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
            {
                MachineName = "B",
                UncPath = @"\\b\1",
            });
        var bDto = await b.Content.ReadFromJsonAsync<AdminBoardSvgSourcesEndpoints.BoardSvgSourceDto>();

        using var put = await client.PutAsJsonAsync(
            new Uri(Route(bDto!.Id, string.Empty), UriKind.Relative),
            new AdminBoardSvgSourcesEndpoints.UpdateSourceRequest
            {
                MachineName = "A",
                UncPath = @"\\b\1",
                IsEnabled = true,
            });
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        _ = a;
    }

    [Fact]
    public async Task Put_Missing_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.pm@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.pm@t.test", "correctpassword123");
        using var put = await client.PutAsJsonAsync(
            new Uri(Route(99999, string.Empty), UriKind.Relative),
            new AdminBoardSvgSourcesEndpoints.UpdateSourceRequest
            {
                MachineName = "X",
                UncPath = @"\\x\1",
                IsEnabled = true,
            });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204AndWritesAudit()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.del@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.del@t.test", "correctpassword123");
        var post = await client.PostAsJsonAsync(
            new Uri(Route(string.Empty), UriKind.Relative),
            new AdminBoardSvgSourcesEndpoints.CreateSourceRequest
            {
                MachineName = "AOI-Del",
                UncPath = @"\\host\del",
            });
        var dto = await post.Content.ReadFromJsonAsync<AdminBoardSvgSourcesEndpoints.BoardSvgSourceDto>();

        using var del = await client.DeleteAsync(new Uri(Route(dto!.Id, string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.False(await db.BoardSvgSources.AnyAsync(s => s.Id == dto.Id));
        Assert.True(await db.AuditEvents.AnyAsync(e =>
            e.EventType == AuditEventTypes.BoardSvgSourceRemoved
            && e.TargetId == dto.Id.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.dm@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.dm@t.test", "correctpassword123");
        using var del = await client.DeleteAsync(new Uri(Route(99999, string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);
    }

    [Fact]
    public async Task Get_Missing_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("bs.gm@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("bs.gm@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri(Route(99999, string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
