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
/// Integration tests for <see cref="AdminUsersEndpoints"/>. Every test
/// starts from a fresh Users table so role guards (last-admin, self-
/// disable) can be exercised deterministically.
/// </summary>
public sealed class AdminUsersEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AdminUsersEndpointsTests(NiewebApiFactory factory)
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

    private async Task ClearUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        // UserRoles + Users cascade via the join table; wipe both to
        // isolate each test from residual state left by a previous fixture.
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
        var create = await users.CreateAsync(user, password);
        Assert.True(create.Succeeded,
            "CreateAsync failed: " + string.Join("; ", create.Errors.Select(e => e.Code + " " + e.Description)));
        if (roles.Length > 0)
        {
            var add = await users.AddToRolesAsync(user, roles);
            Assert.True(add.Succeeded,
                "AddToRolesAsync failed: " + string.Join("; ", add.Errors.Select(e => e.Code + " " + e.Description)));
        }
        return user;
    }

    private async Task<HttpClient> LoggedInClientAsync(string email, string password)
    {
        using var anonymous = _factory.CreateClient();
        var login = new AuthEndpoints.LoginRequest { Email = email, Password = password };
        using var response = await anonymous.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        Assert.NotNull(payload);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        await ClearUsersAsync();
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_AsNonAdmin_Returns403()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("reader1@nieweb.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("reader1@nieweb.test", "correctpassword123");
        using var response = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_AsAdmin_ReturnsAllUsersWithRoles()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("root@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        _ = await CreateUserAsync("alice@nieweb.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("root@nieweb.test", "correctpassword123");

        using var response = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content
            .ReadFromJsonAsync<List<AdminUsersEndpoints.AdminUserDto>>();
        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);
        var alice = rows.Single(r => r.Email == "alice@nieweb.test");
        Assert.Contains(BootstrapAdmin.RoleReader, alice.Roles);
        var root = rows.Single(r => r.Email == "root@nieweb.test");
        Assert.Contains(BootstrapAdmin.RoleAdmin, root.Roles);
    }

    [Fact]
    public async Task Create_AsAdmin_ProvisionsUserWithRoles()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("root2@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("root2@nieweb.test", "correctpassword123");

        var body = new AdminUsersEndpoints.CreateUserRequest
        {
            Email = "newbie@nieweb.test",
            DisplayName = "New Bee",
            Password = "correctpassword123",
            Roles = new[] { BootstrapAdmin.RoleReader, BootstrapAdmin.RoleAuthor },
        };
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/users", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AdminUsersEndpoints.AdminUserDto>();
        Assert.NotNull(dto);
        Assert.Equal("newbie@nieweb.test", dto!.Email);
        Assert.Contains(BootstrapAdmin.RoleReader, dto.Roles);
        Assert.Contains(BootstrapAdmin.RoleAuthor, dto.Roles);

        // Newly-created user can sign in.
        using var newClient = await LoggedInClientAsync("newbie@nieweb.test", "correctpassword123");
        using var whoResponse = await newClient.GetAsync(new Uri("/auth/whoami", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, whoResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateEmail_Returns409()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("root3@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        _ = await CreateUserAsync("dup@nieweb.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("root3@nieweb.test", "correctpassword123");

        var body = new AdminUsersEndpoints.CreateUserRequest
        {
            Email = "dup@nieweb.test",
            DisplayName = "dup",
            Password = "correctpassword123",
            Roles = new[] { BootstrapAdmin.RoleReader },
        };
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/users", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownRole_Returns400ValidationProblem()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("root4@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("root4@nieweb.test", "correctpassword123");

        var body = new AdminUsersEndpoints.CreateUserRequest
        {
            Email = "bogus@nieweb.test",
            DisplayName = "bogus",
            Password = "correctpassword123",
            Roles = new[] { "SuperUser" },
        };
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/users", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesDisplayNameAndRoles()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("root5@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        var target = await CreateUserAsync("bob@nieweb.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("root5@nieweb.test", "correctpassword123");

        var body = new AdminUsersEndpoints.UpdateUserRequest
        {
            DisplayName = "Bob Renamed",
            IsDisabled = false,
            Roles = new[] { BootstrapAdmin.RoleAuthor },
        };
        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/users/{target.Id}", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AdminUsersEndpoints.AdminUserDto>();
        Assert.NotNull(dto);
        Assert.Equal("Bob Renamed", dto!.DisplayName);
        Assert.Single(dto.Roles);
        Assert.Contains(BootstrapAdmin.RoleAuthor, dto.Roles);
        Assert.DoesNotContain(BootstrapAdmin.RoleReader, dto.Roles);
    }

    [Fact]
    public async Task Update_DisablingLastAdmin_Returns409()
    {
        await ClearUsersAsync();
        var soleAdmin = await CreateUserAsync("only@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        _ = await CreateUserAsync("second@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        // We now have TWO admins; log in as one of them and try to disable the OTHER.
        // That should succeed because one admin remains. To trip the guard we then
        // disable ourselves via a fresh admin session and see 409.
        using var client = await LoggedInClientAsync("second@nieweb.test", "correctpassword123");

        // Demote+disable the sole admin: allowed because 'second' still holds Admin.
        var ok = new AdminUsersEndpoints.UpdateUserRequest
        {
            DisplayName = soleAdmin.DisplayName,
            IsDisabled = true,
            Roles = new[] { BootstrapAdmin.RoleReader },
        };
        using var okResponse = await client.PutAsJsonAsync(
            new Uri($"/api/admin/users/{soleAdmin.Id}", UriKind.Relative), ok);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);

        // Now 'second' is the only remaining active admin. Attempting to
        // demote them must be rejected as "last admin".
        var secondId = await GetUserIdAsync("second@nieweb.test");
        var demote = new AdminUsersEndpoints.UpdateUserRequest
        {
            DisplayName = "second",
            IsDisabled = false,
            Roles = new[] { BootstrapAdmin.RoleReader },
        };
        using var demoteResponse = await client.PutAsJsonAsync(
            new Uri($"/api/admin/users/{secondId}", UriKind.Relative), demote);
        Assert.Equal(HttpStatusCode.Conflict, demoteResponse.StatusCode);
    }

    [Fact]
    public async Task Update_DisablingSelf_Returns409()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("selfadmin@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        _ = await CreateUserAsync("otheradmin@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("selfadmin@nieweb.test", "correctpassword123");

        var selfId = await GetUserIdAsync("selfadmin@nieweb.test");
        var body = new AdminUsersEndpoints.UpdateUserRequest
        {
            DisplayName = "self",
            IsDisabled = true,
            Roles = new[] { BootstrapAdmin.RoleAdmin },
        };
        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/users/{selfId}", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_UpdatesCredential()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("root6@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        var target = await CreateUserAsync("carol@nieweb.test", "originalpass123", BootstrapAdmin.RoleReader);
        using var admin = await LoggedInClientAsync("root6@nieweb.test", "correctpassword123");

        var body = new AdminUsersEndpoints.ResetPasswordRequest { NewPassword = "rotatedpass123" };
        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/admin/users/{target.Id}/reset-password", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Old password no longer works.
        using var anonymous = _factory.CreateClient();
        using var oldLogin = await anonymous.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new AuthEndpoints.LoginRequest { Email = "carol@nieweb.test", Password = "originalpass123" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        // New password does.
        using var newLogin = await anonymous.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new AuthEndpoints.LoginRequest { Email = "carol@nieweb.test", Password = "rotatedpass123" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        await ClearUsersAsync();
        _ = await CreateUserAsync("root7@nieweb.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("root7@nieweb.test", "correctpassword123");
        using var response = await client.GetAsync(new Uri("/api/admin/users/9999", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<int> GetUserIdAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        return user.Id;
    }
}
