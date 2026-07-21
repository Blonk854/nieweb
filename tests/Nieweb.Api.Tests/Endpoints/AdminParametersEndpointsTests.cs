using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Audit;
using Nieweb.Api.Endpoints;
using Nieweb.Api.Parameters;
using Nieweb.Api.Startup;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for <see cref="AdminParametersEndpoints"/>
/// (RI3 of docs/phase-2.md §7.1). Every test starts from an isolated
/// AppParameters table and re-uses the shared <see cref="NiewebApiFactory"/>
/// so auth wiring, audit log, and EF DbContext all match a real host.
/// </summary>
public sealed class AdminParametersEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AdminParametersEndpointsTests(NiewebApiFactory factory)
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
        db.AppParameters.RemoveRange(db.AppParameters);
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

    private async Task SeedSystemAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAppParameters>();
        await svc.EnsureSeededAsync();
    }

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri("/api/admin/parameters", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task List_AsReader_Returns403()
    {
        await ResetAsync();
        _ = await CreateUserAsync("r@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("r@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri("/api/admin/parameters", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task List_AsAdmin_ReturnsSeededDefaults()
    {
        await ResetAsync();
        await SeedSystemAsync();
        _ = await CreateUserAsync("a@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri("/api/admin/parameters", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = await res.Content.ReadFromJsonAsync<List<AdminParametersEndpoints.AdminParameterDto>>();
        Assert.NotNull(rows);
        Assert.Contains(rows!, r => r.Key == "msa.gr_r" && r.IsSystem);
        Assert.Contains(rows!, r => r.Key == "batch.enabled" && r.IsSystem);
    }

    [Fact]
    public async Task Get_MissingKey_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("a2@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a2@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri("/api/admin/parameters/not.there", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Put_NewKey_Returns201AndWritesAudit()
    {
        await ResetAsync();
        _ = await CreateUserAsync("a3@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a3@t.test", "correctpassword123");
        var body = new AdminParametersEndpoints.UpsertParameterRequest
        {
            ValueType = AppParameterValueTypes.Decimal,
            Value = "1.25",
            Description = "custom knob",
        };
        using var res = await client.PutAsJsonAsync(
            new Uri("/api/admin/parameters/custom.knob", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminParametersEndpoints.AdminParameterDto>();
        Assert.NotNull(dto);
        Assert.Equal("custom.knob", dto!.Key);
        Assert.False(dto.IsSystem);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.True(await db.AuditEvents
            .AnyAsync(e => e.EventType == AuditEventTypes.AppParameterCreated && e.TargetId == "custom.knob"));
    }

    [Fact]
    public async Task Put_ExistingKey_Returns200Updated()
    {
        await ResetAsync();
        await SeedSystemAsync();
        _ = await CreateUserAsync("a4@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a4@t.test", "correctpassword123");
        var body = new AdminParametersEndpoints.UpsertParameterRequest
        {
            ValueType = AppParameterValueTypes.Decimal,
            Value = "5.55",
            Description = "override",
        };
        using var res = await client.PutAsJsonAsync(
            new Uri("/api/admin/parameters/msa.gr_r", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AdminParametersEndpoints.AdminParameterDto>();
        Assert.Equal("5.55", dto!.Value);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.True(await db.AuditEvents
            .AnyAsync(e => e.EventType == AuditEventTypes.AppParameterUpdated && e.TargetId == "msa.gr_r"));
    }

    [Fact]
    public async Task Put_UnsupportedValueType_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("a5@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a5@t.test", "correctpassword123");
        var body = new AdminParametersEndpoints.UpsertParameterRequest
        {
            ValueType = "guid",
            Value = "x",
        };
        using var res = await client.PutAsJsonAsync(
            new Uri("/api/admin/parameters/bogus.type", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Put_UnparseableDecimal_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("a6@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a6@t.test", "correctpassword123");
        var body = new AdminParametersEndpoints.UpsertParameterRequest
        {
            ValueType = AppParameterValueTypes.Decimal,
            Value = "not-a-number",
        };
        using var res = await client.PutAsJsonAsync(
            new Uri("/api/admin/parameters/bad.decimal", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Delete_NonSystem_Returns204()
    {
        await ResetAsync();
        _ = await CreateUserAsync("a7@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a7@t.test", "correctpassword123");
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAppParameters>();
        _ = await svc.UpsertAsync("tmp.knob", AppParameterValueTypes.String, "hello", null);

        using var res = await client.DeleteAsync(new Uri("/api/admin/parameters/tmp.knob", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.False(await db.AppParameters.AnyAsync(p => p.Key == "tmp.knob"));
        Assert.True(await db.AuditEvents
            .AnyAsync(e => e.EventType == AuditEventTypes.AppParameterDeleted && e.TargetId == "tmp.knob"));
    }

    [Fact]
    public async Task Delete_SystemRow_Returns409()
    {
        await ResetAsync();
        await SeedSystemAsync();
        _ = await CreateUserAsync("a8@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a8@t.test", "correctpassword123");
        using var res = await client.DeleteAsync(new Uri("/api/admin/parameters/msa.gr_r", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Delete_MissingKey_Returns404()
    {
        await ResetAsync();
        _ = await CreateUserAsync("a9@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("a9@t.test", "correctpassword123");
        using var res = await client.DeleteAsync(new Uri("/api/admin/parameters/nope", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
