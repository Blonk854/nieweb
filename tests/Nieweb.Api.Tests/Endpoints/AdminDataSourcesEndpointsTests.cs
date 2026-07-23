using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;

using Nieweb.Api.DataSources;
using Nieweb.Api.Endpoints;
using Nieweb.Api.Startup;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for
/// <see cref="AdminDataSourcesEndpoints"/> — currently focused on the
/// RFC 7232 <c>If-None-Match: *</c> race-guard that turns the
/// otherwise-idempotent <c>PUT /api/admin/data-sources/{key}</c> into
/// a safe create when two admins race the "Add database" modal.
/// </summary>
public sealed class AdminDataSourcesEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AdminDataSourcesEndpointsTests(NiewebApiFactory factory)
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
        db.AoiSourceConfigs.RemoveRange(db.AoiSourceConfigs);
        db.AuditEvents.RemoveRange(db.AuditEvents);
        db.UserRoles.RemoveRange(db.UserRoles);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> LoggedInAdminAsync(string email)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
            var user = new NiewebUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = email.Split('@')[0],
                CreatedUtc = DateTime.UtcNow,
            };
            Assert.True((await users.CreateAsync(user, "correctpassword123")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, BootstrapAdmin.RoleAdmin)).Succeeded);
        }

        using var anon = _factory.CreateClient();
        using var res = await anon.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new AuthEndpoints.LoginRequest { Email = email, Password = "correctpassword123" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var payload = await res.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    private static AdminDataSourcesEndpoints.UpsertRequest FakeSpec(string displayName = "Fake source") =>
        new(
            DisplayName: displayName,
            Kind: AoiSourceKinds.Fake,
            Server: null,
            Database: null,
            User: null,
            Password: null,
            ConnectTimeoutSeconds: 15,
            QueryTimeoutSeconds: 30,
            TrustServerCertificate: true,
            Encrypt: false,
            IsEnabled: true);

    private static HttpRequestMessage PutRequest(string key, AdminDataSourcesEndpoints.UpsertRequest body, bool ifNoneMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/admin/data-sources/{key}", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        if (ifNoneMatch)
        {
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        }
        return req;
    }

    [Fact]
    public async Task Put_WithoutIfNoneMatch_UpsertsIdempotently()
    {
        await ResetAsync();
        using var client = await LoggedInAdminAsync("ds.up@t.test");

        // First PUT creates.
        using var first = await client.SendAsync(PutRequest("scratch", FakeSpec("Original"), ifNoneMatch: false));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second PUT with the same key updates in place — no header,
        // no conflict. This is the pre-race-guard behavior we preserve
        // for edit-mode round-trips.
        using var second = await client.SendAsync(PutRequest("scratch", FakeSpec("Renamed"), ifNoneMatch: false));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<AoiSourceConfigView>();
        Assert.Equal("Renamed", body!.DisplayName);
    }

    [Fact]
    public async Task Put_WithIfNoneMatch_OnNewKey_Creates()
    {
        await ResetAsync();
        using var client = await LoggedInAdminAsync("ds.cr@t.test");

        using var res = await client.SendAsync(PutRequest("scratch", FakeSpec(), ifNoneMatch: true));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AoiSourceConfigView>();
        Assert.Equal("scratch", body!.Key);
    }

    [Fact]
    public async Task Put_WithIfNoneMatch_OnExistingKey_Returns409()
    {
        await ResetAsync();
        using var client = await LoggedInAdminAsync("ds.dup@t.test");

        // Seed the row.
        using var first = await client.SendAsync(PutRequest("scratch", FakeSpec("Original"), ifNoneMatch: false));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second PUT is a race-safe create attempt on the same key —
        // must be rejected with 409 and MUST NOT overwrite the row.
        using var second = await client.SendAsync(PutRequest("scratch", FakeSpec("Hijacked"), ifNoneMatch: true));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var check = await client.GetAsync(new Uri("/api/admin/data-sources/scratch", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var row = await check.Content.ReadFromJsonAsync<AoiSourceConfigView>();
        Assert.Equal("Original", row!.DisplayName);
    }
}
