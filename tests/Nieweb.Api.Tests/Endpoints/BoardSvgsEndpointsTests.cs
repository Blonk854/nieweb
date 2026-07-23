using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

using Nieweb.Api.BoardSvgs;
using Nieweb.Api.Endpoints;
using Nieweb.Api.Startup;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data;
using Nieweb.Data.Entities;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for
/// <see cref="BoardSvgsEndpoints"/> (docs/phase-2.md §7.5 TC4
/// Phase C). Overrides <see cref="IBoardSvgFileSystem"/> with an
/// in-memory fake so nothing writes to real disk.
/// </summary>
public sealed class BoardSvgsEndpointsTests : IClassFixture<NiewebApiFactory>
{
    // NiewebApiFactory sets CacheDirectory = "./test-data/board-svgs".
    private const string CacheDir = "./test-data/board-svgs";

    private readonly NiewebApiFactory _factory;

    public BoardSvgsEndpointsTests(NiewebApiFactory factory)
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
        db.UserRoles.RemoveRange(db.UserRoles);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> WithFakeFs(FakeBoardSvgFileSystem fs)
        => _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBoardSvgFileSystem));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }
            services.AddSingleton<IBoardSvgFileSystem>(fs);
        }));

    private static async Task<NiewebUser> CreateUserAsync(
        WebApplicationFactory<Program> factory, string email, string password, params string[] roles)
    {
        using var scope = factory.Services.CreateScope();
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

    private static async Task<HttpClient> LoggedInClientAsync(
        WebApplicationFactory<Program> factory, string email, string password)
    {
        using var anon = factory.CreateClient();
        var login = new AuthEndpoints.LoginRequest { Email = email, Password = password };
        using var res = await anon.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var payload = await res.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri("/api/board-svgs/ProductA", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Get_AsReader_ReturnsSvgBytes()
    {
        await ResetAsync();
        var fs = new FakeBoardSvgFileSystem();
        var mtime = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        fs.AddFile(CacheDir, "ProductA.svg", "<svg/>"u8.ToArray(), mtime);

        using var factory = WithFakeFs(fs);
        _ = await CreateUserAsync(factory, "bs.read.ok@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync(factory, "bs.read.ok@t.test", "correctpassword123");

        using var res = await client.GetAsync(new Uri("/api/board-svgs/ProductA", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("image/svg+xml", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal("<svg/>"u8.ToArray(), body);
        Assert.NotNull(res.Headers.ETag);
        Assert.True(res.Headers.ETag!.IsWeak);
        Assert.NotNull(res.Headers.CacheControl);
        Assert.Equal(TimeSpan.FromSeconds(BoardSvgsEndpoints.DefaultCacheMaxAgeSeconds), res.Headers.CacheControl!.MaxAge);
        Assert.True(res.Headers.CacheControl.Private);
        Assert.True(res.Headers.CacheControl.MustRevalidate);
    }

    [Fact]
    public async Task Get_MissingProduct_Returns404()
    {
        await ResetAsync();
        var fs = new FakeBoardSvgFileSystem();
        using var factory = WithFakeFs(fs);
        _ = await CreateUserAsync(factory, "bs.read.404@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync(factory, "bs.read.404@t.test", "correctpassword123");

        using var res = await client.GetAsync(new Uri("/api/board-svgs/DoesNotExist", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Get_WithMatchingIfNoneMatch_Returns304()
    {
        await ResetAsync();
        var fs = new FakeBoardSvgFileSystem();
        var mtime = new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc);
        fs.AddFile(CacheDir, "ProductA.svg", "<svg/>"u8.ToArray(), mtime);

        using var factory = WithFakeFs(fs);
        _ = await CreateUserAsync(factory, "bs.read.304@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync(factory, "bs.read.304@t.test", "correctpassword123");

        using var first = await client.GetAsync(new Uri("/api/board-svgs/ProductA", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag!.ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/board-svgs/ProductA", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        // ETag must still be present on 304 (per RFC 9110 §15.4.5).
        Assert.NotNull(second.Headers.ETag);
        Assert.Equal(etag, second.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task Get_WithStaleIfNoneMatch_Returns200Bytes()
    {
        await ResetAsync();
        var fs = new FakeBoardSvgFileSystem();
        var mtime = new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc);
        fs.AddFile(CacheDir, "ProductA.svg", "<svg/>"u8.ToArray(), mtime);

        using var factory = WithFakeFs(fs);
        _ = await CreateUserAsync(factory, "bs.read.stale@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync(factory, "bs.read.stale@t.test", "correctpassword123");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/board-svgs/ProductA", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-None-Match", "W/\"999-1\"");
        using var res = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("<svg/>"u8.ToArray(), await res.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Get_WithWildcardIfNoneMatch_Returns304()
    {
        await ResetAsync();
        var fs = new FakeBoardSvgFileSystem();
        fs.AddFile(CacheDir, "ProductA.svg", "<svg/>"u8.ToArray(), DateTime.UtcNow);

        using var factory = WithFakeFs(fs);
        _ = await CreateUserAsync(factory, "bs.read.star@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync(factory, "bs.read.star@t.test", "correctpassword123");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/board-svgs/ProductA", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var res = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, res.StatusCode);
    }

    [Fact]
    public async Task Get_PathTraversalName_Returns400()
    {
        await ResetAsync();
        _ = await CreateUserAsync(_factory, "bs.read.evil@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync(_factory, "bs.read.evil@t.test", "correctpassword123");

        // Encoded traversal — reaches the endpoint as ".." after routing.
        using var res = await client.GetAsync(new Uri("/api/board-svgs/..%2Fevil", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
