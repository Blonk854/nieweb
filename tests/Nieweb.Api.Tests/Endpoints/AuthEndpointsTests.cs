using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Endpoints;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

public sealed class AuthEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AuthEndpointsTests(NiewebApiFactory factory)
    {
        _factory = factory;
        EnsureDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<NiewebUser> CreateUserAsync(string email, string password, bool disabled = false)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }
        var user = new NiewebUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
            CreatedUtc = DateTime.UtcNow,
            IsDisabled = disabled,
        };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded,
            "CreateAsync failed: " + string.Join("; ", result.Errors.Select(e => e.Code + " " + e.Description)));
        return user;
    }

    [Fact]
    public async Task WhoAmI_WithoutToken_Returns401WithBearerChallenge()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/auth/whoami", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        using var client = _factory.CreateClient();
        var body = new AuthEndpoints.LoginRequest { Email = "ghost@nieweb.test", Password = "does-not-matter" };
        using var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithBadPassword_Returns401()
    {
        _ = await CreateUserAsync("badpw@nieweb.test", "correctpassword123");
        using var client = _factory.CreateClient();
        var body = new AuthEndpoints.LoginRequest { Email = "badpw@nieweb.test", Password = "wrongpassword" };
        using var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithDisabledAccount_Returns401()
    {
        _ = await CreateUserAsync("disabled@nieweb.test", "correctpassword123", disabled: true);
        using var client = _factory.CreateClient();
        var body = new AuthEndpoints.LoginRequest { Email = "disabled@nieweb.test", Password = "correctpassword123" };
        using var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndWhoAmIRoundTrips()
    {
        _ = await CreateUserAsync("alice@nieweb.test", "correctpassword123");
        using var client = _factory.CreateClient();

        var login = new AuthEndpoints.LoginRequest { Email = "alice@nieweb.test", Password = "correctpassword123" };
        using var loginResponse = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var payload = await loginResponse.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrEmpty(payload!.AccessToken));
        Assert.Equal("Bearer", payload.TokenType);
        Assert.True(payload.ExpiresUtc > DateTime.UtcNow);
        // Three base64url segments separated by '.' -> JWS compact form.
        Assert.Equal(3, payload.AccessToken.Split('.').Length);

        using var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        using var whoResponse = await authed.GetAsync(new Uri("/auth/whoami", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, whoResponse.StatusCode);

        var who = await whoResponse.Content.ReadFromJsonAsync<AuthEndpoints.WhoAmIResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(who);
        Assert.Equal("alice@nieweb.test", who!.Email);
        Assert.NotNull(who.UserId);
    }

    [Fact]
    public async Task Login_UpdatesLastLoginUtc_OnSuccess()
    {
        _ = await CreateUserAsync("stamped@nieweb.test", "correctpassword123");
        using var client = _factory.CreateClient();

        var before = DateTime.UtcNow.AddSeconds(-1);
        var login = new AuthEndpoints.LoginRequest { Email = "stamped@nieweb.test", Password = "correctpassword123" };
        using var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == "stamped@nieweb.test");
        Assert.NotNull(user.LastLoginUtc);
        Assert.True(user.LastLoginUtc!.Value >= before);
    }
}
