using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Audit;
using Nieweb.Api.Endpoints;
using Nieweb.Api.Shifts;
using Nieweb.Api.Startup;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for <see cref="AdminShiftsEndpoints"/>
/// (docs/phase-2.md §7.4 <c>PL1</c>). Focuses on cycle replace, ordering,
/// validation, and audit-row emission. The <see cref="IShifts.BuildShiftDefinitionAsync"/>
/// contract is exercised in-process to prove downstream reports can
/// consume the persisted cycle.
/// </summary>
public sealed class AdminShiftsEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AdminShiftsEndpointsTests(NiewebApiFactory factory)
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
        db.ShiftBreakpoints.RemoveRange(db.ShiftBreakpoints);
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

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri("/api/admin/shifts", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task List_AsReader_Returns403()
    {
        await ResetAsync();
        _ = await CreateUserAsync("sh.r@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("sh.r@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri("/api/admin/shifts", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task List_AsAdmin_ReturnsEmpty()
    {
        await ResetAsync();
        _ = await CreateUserAsync("sh.a@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("sh.a@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri("/api/admin/shifts", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var rows = await res.Content.ReadFromJsonAsync<List<AdminShiftsEndpoints.ShiftBreakpointDto>>();
        Assert.NotNull(rows);
        Assert.Empty(rows!);
    }

    [Fact]
    public async Task Replace_Sorted_ReturnsOrdered()
    {
        await ResetAsync();
        _ = await CreateUserAsync("sh.rep@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("sh.rep@t.test", "correctpassword123");

        // Send out of order — server must re-sort by (Hour, Minute).
        var body = new AdminShiftsEndpoints.ReplaceShiftsRequest
        {
            Entries = new[]
            {
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 16, Minute = 0, Label = "Afternoon" },
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 0, Minute = 0, Label = "Night" },
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 8, Minute = 0, Label = "Morning" },
            },
        };
        using var put = await client.PutAsJsonAsync(new Uri("/api/admin/shifts", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var rows = await put.Content.ReadFromJsonAsync<List<AdminShiftsEndpoints.ShiftBreakpointDto>>();
        Assert.Equal(3, rows!.Count);
        Assert.Equal(0, rows[0].Hour);
        Assert.Equal(8, rows[1].Hour);
        Assert.Equal(16, rows[2].Hour);
        Assert.Equal("Night", rows[0].Label);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(e =>
            e.EventType == AuditEventTypes.ShiftsReplaced && e.TargetId == "site"));
    }

    [Fact]
    public async Task Replace_Empty_ClearsCycle()
    {
        await ResetAsync();
        _ = await CreateUserAsync("sh.cl@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("sh.cl@t.test", "correctpassword123");

        // Seed a cycle first.
        var seed = new AdminShiftsEndpoints.ReplaceShiftsRequest
        {
            Entries = new[]
            {
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 6, Minute = 0 },
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 18, Minute = 0 },
            },
        };
        _ = await client.PutAsJsonAsync(new Uri("/api/admin/shifts", UriKind.Relative), seed);

        // Now wipe.
        var empty = new AdminShiftsEndpoints.ReplaceShiftsRequest
        {
            Entries = Array.Empty<AdminShiftsEndpoints.ShiftBreakpointInputDto>(),
        };
        using var put = await client.PutAsJsonAsync(new Uri("/api/admin/shifts", UriKind.Relative), empty);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        Assert.False(await db.ShiftBreakpoints.AnyAsync());
    }

    [Fact]
    public async Task Replace_Duplicate_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("sh.dup@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("sh.dup@t.test", "correctpassword123");
        var body = new AdminShiftsEndpoints.ReplaceShiftsRequest
        {
            Entries = new[]
            {
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 8, Minute = 0 },
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 8, Minute = 0 },
            },
        };
        using var res = await client.PutAsJsonAsync(new Uri("/api/admin/shifts", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Replace_OutOfRangeHour_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync("sh.rng@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("sh.rng@t.test", "correctpassword123");
        var body = new AdminShiftsEndpoints.ReplaceShiftsRequest
        {
            Entries = new[]
            {
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 25, Minute = 0 },
            },
        };
        using var res = await client.PutAsJsonAsync(new Uri("/api/admin/shifts", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task BuildShiftDefinition_ReflectsPersistedCycle()
    {
        await ResetAsync();
        _ = await CreateUserAsync("sh.bd@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("sh.bd@t.test", "correctpassword123");

        var body = new AdminShiftsEndpoints.ReplaceShiftsRequest
        {
            Entries = new[]
            {
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 8, Minute = 0, Label = "Morning" },
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 16, Minute = 0 },
                new AdminShiftsEndpoints.ShiftBreakpointInputDto { Hour = 0, Minute = 0 },
            },
        };
        _ = await client.PutAsJsonAsync(new Uri("/api/admin/shifts", UriKind.Relative), body);

        using var scope = _factory.Services.CreateScope();
        var shifts = scope.ServiceProvider.GetRequiredService<IShifts>();
        var def = await shifts.BuildShiftDefinitionAsync();
        Assert.NotNull(def);
        Assert.Equal(3, def!.Starts.Length);
        Assert.Equal(new TimeOnly(0, 0), def.Starts[0]);
        Assert.Equal(new TimeOnly(8, 0), def.Starts[1]);
        Assert.Equal(new TimeOnly(16, 0), def.Starts[2]);
        Assert.Equal("Shift 1", def.Labels[0]); // no label → default
        Assert.Equal("Morning", def.Labels[1]);
    }

    [Fact]
    public async Task BuildShiftDefinition_EmptyCycle_ReturnsNull()
    {
        await ResetAsync();
        using var scope = _factory.Services.CreateScope();
        var shifts = scope.ServiceProvider.GetRequiredService<IShifts>();
        var def = await shifts.BuildShiftDefinitionAsync();
        Assert.Null(def);
    }
}
