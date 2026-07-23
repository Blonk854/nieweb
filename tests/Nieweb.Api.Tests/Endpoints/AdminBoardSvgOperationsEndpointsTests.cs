using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using Nieweb.Api.Audit;
using Nieweb.Api.BoardSvgs;
using Nieweb.Api.Endpoints;
using Nieweb.Api.Startup;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data;
using Nieweb.Data.Entities;
using Nieweb.DataSources;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for
/// <see cref="AdminBoardSvgOperationsEndpoints"/> (docs/phase-2.md
/// §7.5 <c>TC4</c> Phase B). Overrides <see cref="IBoardSvgFileSystem"/>
/// with an in-memory fake so nothing writes to real disk.
/// </summary>
public sealed class AdminBoardSvgOperationsEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private static readonly SourceDescriptor PostDescriptor =
        new("postreflow", "Post-Reflow", "5.0", Capabilities.PinLevel);
    private const string SourcePath = @"\\aoi-a\svg";

    private readonly NiewebApiFactory _factory;

    public AdminBoardSvgOperationsEndpointsTests(NiewebApiFactory factory)
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

    private async Task SeedSourceAsync(string machineName, string uncPath, bool enabled = true)
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBoardSvgSources>();
        _ = await repo.CreateAsync(machineName, uncPath, enabled, CancellationToken.None);
    }

    private WebApplicationFactory<Program> WithFakes(FakeBoardSvgFileSystem fs, params IAoiSource[] aoi)
        => _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            // Replace the disk-backed filesystem with the fake.
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBoardSvgFileSystem));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }
            services.AddSingleton<IBoardSvgFileSystem>(fs);
            foreach (var src in aoi)
            {
                services.AddSingleton(src);
            }
        }));

    private static async Task<NiewebUser> CreateUserAsync(WebApplicationFactory<Program> factory, string email, string password, params string[] roles)
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

    private static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory, string email, string password)
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

    private static FakeAoiSource NewAoi(params string[] products)
    {
        var list = products
            .Select((n, i) => new Product(i + 1, n, Revision: null, Description: null))
            .ToList();
        return new FakeAoiSource(PostDescriptor) { SeededProducts = list };
    }

    [Fact]
    public async Task Status_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri("/api/admin/board-svgs/status", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Status_AsReader_Returns403()
    {
        await ResetAsync();
        _ = await CreateUserAsync(_factory, "bs.op.r@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync(_factory, "bs.op.r@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri("/api/admin/board-svgs/status", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Status_ReturnsEmptyEnvelopeOnFreshHost()
    {
        await ResetAsync();
        var fs = new FakeBoardSvgFileSystem();
        using var factory = WithFakes(fs);
        _ = await CreateUserAsync(factory, "bs.op.s1@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync(factory, "bs.op.s1@t.test", "correctpassword123");

        using var res = await client.GetAsync(new Uri("/api/admin/board-svgs/status", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var payload = await res.Content.ReadFromJsonAsync<AdminBoardSvgOperationsEndpoints.BoardSvgStatusDto>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Sources);
        Assert.Empty(payload.Cache);
        Assert.Empty(payload.KnownProducts);
        Assert.Empty(payload.MissingProducts);
        Assert.False(payload.SyncEnabled); // NiewebApiFactory sets Enabled=false.
    }

    [Fact]
    public async Task Status_ReportsMissingProducts()
    {
        await ResetAsync();
        await SeedSourceAsync("AOI-A", SourcePath);
        var fs = new FakeBoardSvgFileSystem();
        // Cache has one product already; another is known but missing.
        fs.EnsureDirectoryExists("./test-data/board-svgs");
        fs.AddFile("./test-data/board-svgs", "Cached.svg", "x"u8.ToArray(), DateTime.UtcNow);

        using var factory = WithFakes(fs, NewAoi("Cached", "MissingProduct"));
        _ = await CreateUserAsync(factory, "bs.op.st2@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync(factory, "bs.op.st2@t.test", "correctpassword123");

        using var res = await client.GetAsync(new Uri("/api/admin/board-svgs/status", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var payload = await res.Content.ReadFromJsonAsync<AdminBoardSvgOperationsEndpoints.BoardSvgStatusDto>();

        Assert.NotNull(payload);
        Assert.Single(payload!.Sources);
        Assert.Equal("AOI-A", payload.Sources[0].MachineName);
        Assert.Contains("Cached", payload.KnownProducts);
        Assert.Contains("MissingProduct", payload.KnownProducts);
        Assert.Equal("MissingProduct", Assert.Single(payload.MissingProducts));
        Assert.Single(payload.Cache);
        Assert.Equal("Cached", payload.Cache[0].ProductName);
    }

    [Fact]
    public async Task Sync_WithoutToken_Returns401()
    {
        await ResetAsync();
        using var client = _factory.CreateClient();
        using var res = await client.PostAsync(new Uri("/api/admin/board-svgs/sync", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Sync_AsReader_Returns403()
    {
        await ResetAsync();
        _ = await CreateUserAsync(_factory, "bs.op.sr@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync(_factory, "bs.op.sr@t.test", "correctpassword123");
        using var res = await client.PostAsync(new Uri("/api/admin/board-svgs/sync", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Sync_AsAdmin_CopiesFileAndUpdatesLastSyncedUtc()
    {
        await ResetAsync();
        await SeedSourceAsync("AOI-A", SourcePath);
        var fs = new FakeBoardSvgFileSystem();
        fs.AddFile(SourcePath, "ProductA.svg", "svg-bytes"u8.ToArray(), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        using var factory = WithFakes(fs, NewAoi("ProductA"));
        _ = await CreateUserAsync(factory, "bs.op.ok@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync(factory, "bs.op.ok@t.test", "correctpassword123");

        using var res = await client.PostAsync(new Uri("/api/admin/board-svgs/sync", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var payload = await res.Content.ReadFromJsonAsync<AdminBoardSvgOperationsEndpoints.BoardSvgSyncResultDto>();

        Assert.NotNull(payload);
        Assert.Single(payload!.Sources);
        Assert.True(payload.Sources[0].Reachable);
        var product = Assert.Single(payload.Products);
        Assert.True(product.Copied);
        Assert.Equal("AOI-A", product.SourceMachineName);
        Assert.Equal(9L, product.BytesCopied);

        // LastSyncedUtc must have been written on the source row.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var row = await db.BoardSvgSources.SingleAsync();
        Assert.NotNull(row.LastSyncedUtc);
        Assert.Null(row.LastSyncErrorUtc);

        // Audit trail must have a BoardSvgSynced event.
        Assert.True(await db.AuditEvents.AnyAsync(e =>
            e.EventType == AuditEventTypes.BoardSvgSynced
            && e.TargetType == AuditTargetTypes.BoardSvg
            && e.TargetId == "ProductA"));
    }

    [Fact]
    public async Task Sync_UnreachableSource_RecordsFailureOnRow()
    {
        await ResetAsync();
        await SeedSourceAsync("AOI-Broken", SourcePath);
        var fs = new FakeBoardSvgFileSystem();
        fs.MakeDirectoryUnreachable(SourcePath);

        using var factory = WithFakes(fs, NewAoi("ProductA"));
        _ = await CreateUserAsync(factory, "bs.op.err@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync(factory, "bs.op.err@t.test", "correctpassword123");

        using var res = await client.PostAsync(new Uri("/api/admin/board-svgs/sync", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var payload = await res.Content.ReadFromJsonAsync<AdminBoardSvgOperationsEndpoints.BoardSvgSyncResultDto>();

        Assert.NotNull(payload);
        Assert.False(payload!.Sources[0].Reachable);
        Assert.NotNull(payload.Sources[0].Error);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        var row = await db.BoardSvgSources.SingleAsync();
        Assert.NotNull(row.LastSyncErrorUtc);
        Assert.NotNull(row.LastSyncError);
        Assert.Null(row.LastSyncedUtc); // never succeeded
    }
}
