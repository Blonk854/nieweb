using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

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
/// Integration tests for the I4 audit trail: every admin action and
/// every authentication attempt must persist one <see cref="AuditEvent"/>
/// row, and <c>GET /api/admin/audit</c> must project it back with
/// filter + pagination.
/// </summary>
public sealed class AuditEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AuditEndpointsTests(NiewebApiFactory factory)
    {
        _factory = factory;
        EnsureAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureAsync()
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

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
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
        var create = await users.CreateAsync(user, password);
        Assert.True(create.Succeeded,
            "CreateAsync failed: " + string.Join("; ", create.Errors.Select(e => e.Code + " " + e.Description)));
        if (roles.Length > 0)
        {
            var add = await users.AddToRolesAsync(user, roles);
            Assert.True(add.Succeeded);
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
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    private async Task<IReadOnlyList<AuditEvent>> AllEventsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        return await db.AuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    // ---------- auth.signin.* rows ----------

    [Fact]
    public async Task Login_success_writes_auth_signin_ok_event()
    {
        await ClearAsync();
        _ = await CreateUserAsync("alice@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var _c = await LoggedInClientAsync("alice@audit.test", "correctpassword123");

        var events = await AllEventsAsync();
        var ok = Assert.Single(events, e => e.EventType == AuditEventTypes.AuthSignInOk);
        Assert.Equal(AuditTargetTypes.Session, ok.TargetType);
        Assert.NotNull(ok.ActorUserId);
        Assert.Equal("alice", ok.ActorDisplayName);
        // DetailsJson is opaque but must be a JSON object with the email.
        using var doc = JsonDocument.Parse(ok.DetailsJson);
        Assert.Equal("alice@audit.test", doc.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Login_bad_password_writes_auth_signin_failed_event()
    {
        await ClearAsync();
        _ = await CreateUserAsync("bob@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var anonymous = _factory.CreateClient();
        var body = new AuthEndpoints.LoginRequest { Email = "bob@audit.test", Password = "wrong" };
        using var response = await anonymous.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var events = await AllEventsAsync();
        var failed = Assert.Single(events, e => e.EventType == AuditEventTypes.AuthSignInFailed);
        Assert.Equal(AuditTargetTypes.Session, failed.TargetType);
        using var doc = JsonDocument.Parse(failed.DetailsJson);
        Assert.Equal("bad-password", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Login_unknown_email_writes_auth_signin_failed_event_with_null_actor()
    {
        await ClearAsync();
        using var anonymous = _factory.CreateClient();
        var body = new AuthEndpoints.LoginRequest { Email = "ghost@audit.test", Password = "whatever" };
        using var response = await anonymous.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var events = await AllEventsAsync();
        var failed = Assert.Single(events, e => e.EventType == AuditEventTypes.AuthSignInFailed);
        Assert.Null(failed.ActorUserId);
        using var doc = JsonDocument.Parse(failed.DetailsJson);
        Assert.Equal("unknown-account", doc.RootElement.GetProperty("reason").GetString());
    }

    // ---------- admin user.* rows ----------

    [Fact]
    public async Task Admin_create_user_writes_user_created_event()
    {
        await ClearAsync();
        _ = await CreateUserAsync("root@audit.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var admin = await LoggedInClientAsync("root@audit.test", "correctpassword123");

        var body = new AdminUsersEndpoints.CreateUserRequest
        {
            Email = "new@audit.test",
            DisplayName = "New",
            Password = "correctpassword123",
            Roles = new[] { BootstrapAdmin.RoleReader },
        };
        using var response = await admin.PostAsJsonAsync(new Uri("/api/admin/users", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var events = await AllEventsAsync();
        var created = Assert.Single(events, e => e.EventType == AuditEventTypes.UserCreated);
        Assert.Equal(AuditTargetTypes.User, created.TargetType);
        // Actor is the signed-in admin, not the newly created user.
        Assert.NotNull(created.ActorUserId);
        Assert.Equal("root", created.ActorDisplayName);
        using var doc = JsonDocument.Parse(created.DetailsJson);
        Assert.Equal("new@audit.test", doc.RootElement.GetProperty("email").GetString());
        var rolesArr = doc.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains(BootstrapAdmin.RoleReader, rolesArr);
    }

    [Fact]
    public async Task Admin_update_user_roles_writes_user_updated_and_user_role_changed_events()
    {
        await ClearAsync();
        _ = await CreateUserAsync("root@audit.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        var target = await CreateUserAsync("target@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var admin = await LoggedInClientAsync("root@audit.test", "correctpassword123");

        var body = new AdminUsersEndpoints.UpdateUserRequest
        {
            DisplayName = "Target Promoted",
            IsDisabled = false,
            Roles = new[] { BootstrapAdmin.RoleReader, BootstrapAdmin.RoleAuthor },
        };
        using var response = await admin.PutAsJsonAsync(
            new Uri($"/api/admin/users/{target.Id}", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await AllEventsAsync();
        Assert.Single(events, e => e.EventType == AuditEventTypes.UserUpdated);
        var roleChanged = Assert.Single(events, e => e.EventType == AuditEventTypes.UserRoleChanged);
        using var doc = JsonDocument.Parse(roleChanged.DetailsJson);
        var added = doc.RootElement.GetProperty("rolesAdded").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains(BootstrapAdmin.RoleAuthor, added);
    }

    [Fact]
    public async Task Admin_update_user_no_role_change_writes_only_user_updated()
    {
        await ClearAsync();
        _ = await CreateUserAsync("root@audit.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        var target = await CreateUserAsync("plain@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var admin = await LoggedInClientAsync("root@audit.test", "correctpassword123");

        var body = new AdminUsersEndpoints.UpdateUserRequest
        {
            DisplayName = "Renamed Only",
            IsDisabled = false,
            Roles = new[] { BootstrapAdmin.RoleReader }, // unchanged
        };
        using var response = await admin.PutAsJsonAsync(
            new Uri($"/api/admin/users/{target.Id}", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await AllEventsAsync();
        Assert.Single(events, e => e.EventType == AuditEventTypes.UserUpdated);
        Assert.DoesNotContain(events, e => e.EventType == AuditEventTypes.UserRoleChanged);
    }

    [Fact]
    public async Task Admin_reset_password_writes_user_password_reset_event()
    {
        await ClearAsync();
        _ = await CreateUserAsync("root@audit.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        var target = await CreateUserAsync("resetme@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var admin = await LoggedInClientAsync("root@audit.test", "correctpassword123");

        var body = new AdminUsersEndpoints.ResetPasswordRequest { NewPassword = "brandnew12345" };
        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/admin/users/{target.Id}/reset-password", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var events = await AllEventsAsync();
        var reset = Assert.Single(events, e => e.EventType == AuditEventTypes.UserPasswordReset);
        Assert.Equal(target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), reset.TargetId);
    }

    [Fact]
    public async Task Change_password_writes_auth_password_changed_event()
    {
        await ClearAsync();
        _ = await CreateUserAsync("changer@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("changer@audit.test", "correctpassword123");

        var body = new AuthEndpoints.ChangePasswordRequest
        {
            CurrentPassword = "correctpassword123",
            NewPassword = "newpassword12345",
        };
        using var response = await client.PostAsJsonAsync(
            new Uri("/auth/change-password", UriKind.Relative), body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var events = await AllEventsAsync();
        Assert.Single(events, e => e.EventType == AuditEventTypes.AuthPasswordChanged);
    }

    // ---------- /api/admin/audit endpoint ----------

    [Fact]
    public async Task Audit_endpoint_requires_admin_role()
    {
        await ClearAsync();
        _ = await CreateUserAsync("reader@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var reader = await LoggedInClientAsync("reader@audit.test", "correctpassword123");

        using var response = await reader.GetAsync(new Uri("/api/admin/audit", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Audit_endpoint_returns_recent_events_paged_desc()
    {
        await ClearAsync();
        _ = await CreateUserAsync("root@audit.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var admin = await LoggedInClientAsync("root@audit.test", "correctpassword123");

        // Sign-in above already wrote one row; add a couple more via
        // additional admin operations so we can exercise ordering.
        var target = await CreateUserAsync("t1@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var _r = await admin.PostAsJsonAsync(
            new Uri($"/api/admin/users/{target.Id}/reset-password", UriKind.Relative),
            new AdminUsersEndpoints.ResetPasswordRequest { NewPassword = "brandnew12345" });

        using var response = await admin.GetAsync(new Uri("/api/admin/audit?pageSize=10", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuditEndpoints.AuditListResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.Items.Count >= 2, $"expected >= 2 items, got {payload.Items.Count}");
        // Descending order by EventTimeUtc.
        for (int i = 1; i < payload.Items.Count; i++)
        {
            Assert.True(payload.Items[i - 1].EventTimeUtc >= payload.Items[i].EventTimeUtc,
                $"row {i - 1} {payload.Items[i - 1].EventTimeUtc:O} must be >= row {i} {payload.Items[i].EventTimeUtc:O}");
        }
    }

    [Fact]
    public async Task Audit_endpoint_filters_by_event_type()
    {
        await ClearAsync();
        _ = await CreateUserAsync("root@audit.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var admin = await LoggedInClientAsync("root@audit.test", "correctpassword123");

        var target = await CreateUserAsync("t2@audit.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var _r = await admin.PostAsJsonAsync(
            new Uri($"/api/admin/users/{target.Id}/reset-password", UriKind.Relative),
            new AdminUsersEndpoints.ResetPasswordRequest { NewPassword = "brandnew12345" });

        using var response = await admin.GetAsync(
            new Uri($"/api/admin/audit?eventType={AuditEventTypes.UserPasswordReset}", UriKind.Relative));
        var payload = await response.Content.ReadFromJsonAsync<AuditEndpoints.AuditListResponse>();
        Assert.NotNull(payload);
        Assert.All(payload!.Items, item => Assert.Equal(AuditEventTypes.UserPasswordReset, item.EventType));
        Assert.NotEmpty(payload.Items);
    }
}
