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
/// HTTP integration tests for <see cref="AdminProductionLinesEndpoints"/>
/// (docs/phase-2.md §7.4 <c>PL1</c>). Reuses <see cref="NiewebApiFactory"/>
/// so auth, audit, and EF wiring all match a real host.
/// </summary>
public sealed class AdminProductionLinesEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AdminProductionLinesEndpointsTests(NiewebApiFactory factory)
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
        db.ProductionLineMachines.RemoveRange(db.ProductionLineMachines);
        db.ProductionLines.RemoveRange(db.ProductionLines);
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

    private static string Route(string suffix) => "/api/admin/production-lines" + suffix;

    private static string Route(int id, string suffix)
        => "/api/admin/production-lines/" + id.ToString(CultureInfo.InvariantCulture) + suffix;

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
        _ = await CreateUserAsync("pl.r@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("pl.r@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri(Route(string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task List_AsAdmin_ReturnsEmptyThenSeeded()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.a@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.a@t.test", "correctpassword123");

        using var empty = await client.GetAsync(new Uri(Route(string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var rows = await empty.Content.ReadFromJsonAsync<List<AdminProductionLinesEndpoints.ProductionLineDto>>();
        Assert.NotNull(rows);
        Assert.Empty(rows!);

        // Create one and re-list.
        var create = new AdminProductionLinesEndpoints.CreateLineRequest { Name = "Line 1", DisplayOrder = 1 };
        using var post = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), create);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        using var listed = await client.GetAsync(new Uri(Route(string.Empty), UriKind.Relative));
        var listedRows = await listed.Content.ReadFromJsonAsync<List<AdminProductionLinesEndpoints.ProductionLineDto>>();
        Assert.Single(listedRows!);
        Assert.Equal("Line 1", listedRows![0].Name);
        Assert.Equal(0, listedRows[0].MachineCount);
    }

    [Fact]
    public async Task Post_WithBlankName_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.b@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.b@t.test", "correctpassword123");
        var body = new AdminProductionLinesEndpoints.CreateLineRequest { Name = "   " };
        using var res = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Post_DuplicateName_Returns409()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.d@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.d@t.test", "correctpassword123");
        var body = new AdminProductionLinesEndpoints.CreateLineRequest { Name = "Line 1" };
        using var first = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Put_RenameToTakenName_Returns409()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.rn@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.rn@t.test", "correctpassword123");

        var a = await client.PostAsJsonAsync(
            new Uri(Route(string.Empty), UriKind.Relative),
            new AdminProductionLinesEndpoints.CreateLineRequest { Name = "A" });
        var aDto = await a.Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDto>();
        var b = await client.PostAsJsonAsync(
            new Uri(Route(string.Empty), UriKind.Relative),
            new AdminProductionLinesEndpoints.CreateLineRequest { Name = "B" });
        var bDto = await b.Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDto>();

        using var put = await client.PutAsJsonAsync(
            new Uri(Route(bDto!.Id, string.Empty), UriKind.Relative),
            new AdminProductionLinesEndpoints.UpdateLineRequest { Name = "A", DisplayOrder = 0 });
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        _ = aDto;
    }

    [Fact]
    public async Task Delete_Existing_Returns204AndWritesAudit()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.del@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.del@t.test", "correctpassword123");
        var post = await client.PostAsJsonAsync(
            new Uri(Route(string.Empty), UriKind.Relative),
            new AdminProductionLinesEndpoints.CreateLineRequest { Name = "Line X" });
        var dto = await post.Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDto>();

        using var del = await client.DeleteAsync(new Uri(Route(dto!.Id, string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.False(await db.ProductionLines.AnyAsync(l => l.Id == dto.Id));
        Assert.True(await db.AuditEvents.AnyAsync(e =>
            e.EventType == AuditEventTypes.ProductionLineDeleted
            && e.TargetId == dto.Id.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.dm@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.dm@t.test", "correctpassword123");
        using var del = await client.DeleteAsync(new Uri(Route(99999, string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);
    }

    [Fact]
    public async Task AddMachine_AttachesAndAudits()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.am@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.am@t.test", "correctpassword123");

        var post = await client.PostAsJsonAsync(
            new Uri(Route(string.Empty), UriKind.Relative),
            new AdminProductionLinesEndpoints.CreateLineRequest { Name = "L1" });
        var line = await post.Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDto>();

        var addReq = new AdminProductionLinesEndpoints.AddMachineRequest
        {
            SourceId = "postreflow",
            MachineId = 42,
            MachineName = "AOI-42",
            Category = "AOI",
            DisplayOrder = 1,
        };
        using var add = await client.PostAsJsonAsync(
            new Uri(Route(line!.Id, "/machines"), UriKind.Relative), addReq);
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var assignment = await add.Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineMachineDto>();
        Assert.NotNull(assignment);
        Assert.Equal(42, assignment!.MachineId);

        // GET should surface the machine list.
        using var get = await client.GetAsync(new Uri(Route(line.Id, string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var detail = await get.Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(1, detail!.Line.MachineCount);
        Assert.Single(detail.Machines);
        Assert.Equal("AOI-42", detail.Machines[0].MachineName);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(e =>
            e.EventType == AuditEventTypes.ProductionLineMachineAdded));
    }

    [Fact]
    public async Task AddMachine_DuplicateAssignmentAcrossLines_Returns409()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.dup@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.dup@t.test", "correctpassword123");

        var l1 = await (await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative),
                new AdminProductionLinesEndpoints.CreateLineRequest { Name = "L1" }))
            .Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDto>();
        var l2 = await (await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative),
                new AdminProductionLinesEndpoints.CreateLineRequest { Name = "L2" }))
            .Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDto>();

        var addReq = new AdminProductionLinesEndpoints.AddMachineRequest
        {
            SourceId = "postreflow",
            MachineId = 7,
            MachineName = "AOI-7",
        };
        using var addA = await client.PostAsJsonAsync(new Uri(Route(l1!.Id, "/machines"), UriKind.Relative), addReq);
        Assert.Equal(HttpStatusCode.Created, addA.StatusCode);
        using var addB = await client.PostAsJsonAsync(new Uri(Route(l2!.Id, "/machines"), UriKind.Relative), addReq);
        Assert.Equal(HttpStatusCode.Conflict, addB.StatusCode);
    }

    [Fact]
    public async Task AddMachine_LineMissing_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.mm@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.mm@t.test", "correctpassword123");
        using var add = await client.PostAsJsonAsync(
            new Uri(Route(999, "/machines"), UriKind.Relative),
            new AdminProductionLinesEndpoints.AddMachineRequest
            {
                SourceId = "postreflow",
                MachineId = 1,
                MachineName = "M",
            });
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
    }

    [Fact]
    public async Task RemoveMachine_DetachesAndAudits()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.rm@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.rm@t.test", "correctpassword123");

        var line = await (await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative),
                new AdminProductionLinesEndpoints.CreateLineRequest { Name = "L" }))
            .Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDto>();
        var assignment = await (await client.PostAsJsonAsync(new Uri(Route(line!.Id, "/machines"), UriKind.Relative),
                new AdminProductionLinesEndpoints.AddMachineRequest
                {
                    SourceId = "postreflow",
                    MachineId = 3,
                    MachineName = "AOI-3",
                }))
            .Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineMachineDto>();

        using var del = await client.DeleteAsync(new Uri(
            Route(line.Id, "/machines/" + assignment!.Id.ToString(CultureInfo.InvariantCulture)),
            UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.False(await db.ProductionLineMachines.AnyAsync(m => m.Id == assignment.Id));
        Assert.True(await db.AuditEvents.AnyAsync(e =>
            e.EventType == AuditEventTypes.ProductionLineMachineRemoved));
    }

    [Fact]
    public async Task DeleteLine_CascadesMachines()
    {
        await ResetAsync();
        _ = await CreateUserAsync("pl.cd@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("pl.cd@t.test", "correctpassword123");

        var line = await (await client.PostAsJsonAsync(new Uri(Route(string.Empty), UriKind.Relative),
                new AdminProductionLinesEndpoints.CreateLineRequest { Name = "Casc" }))
            .Content.ReadFromJsonAsync<AdminProductionLinesEndpoints.ProductionLineDto>();
        _ = await client.PostAsJsonAsync(new Uri(Route(line!.Id, "/machines"), UriKind.Relative),
            new AdminProductionLinesEndpoints.AddMachineRequest
            {
                SourceId = "postreflow",
                MachineId = 11,
                MachineName = "M11",
            });

        using var del = await client.DeleteAsync(new Uri(Route(line.Id, string.Empty), UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.False(await db.ProductionLineMachines.AnyAsync(m => m.ProductionLineId == line.Id));
    }
}
