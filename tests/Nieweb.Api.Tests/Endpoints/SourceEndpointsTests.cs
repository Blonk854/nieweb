using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Identity;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data.Entities;
using Nieweb.DataSources;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

public sealed class SourceEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public SourceEndpointsTests(NiewebApiFactory factory)
    {
        _factory = factory;
        EnsureDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.NiewebDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private async Task<string> IssueTokenAsync(HttpClient client, string email = "sources-tester@nieweb.test")
    {
        await CreateUserAsync(email, "correctpassword123");
        var login = new AuthEndpoints.LoginRequest { Email = email, Password = "correctpassword123" };
        using var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        Assert.NotNull(payload);
        return payload!.AccessToken;
    }

    private async Task CreateUserAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            return;
        }
        var user = new NiewebUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
            CreatedUtc = DateTime.UtcNow,
            IsDisabled = false,
        };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded,
            "CreateAsync failed: " + string.Join("; ", result.Errors.Select(e => e.Code + " " + e.Description)));
    }

    private static readonly JsonSerializerOptions _responseJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] _expectedPostCaps = ["BarcodeProductView", "IsLastInspectionFilter", "PinLevel"];
    private static readonly string[] _expectedPreCaps = ["FeederAnalytics", "PastePrintMetrics"];

    [Fact]
    public async Task ListSources_WithoutToken_Returns401WithBearerChallenge()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/sources", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task ListSources_WithNoSourcesConfigured_ReturnsEmptyArray()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "sources-empty@nieweb.test");

        using var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await authed.GetAsync(new Uri("/api/sources", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var infos = await response.Content.ReadFromJsonAsync<SourceEndpoints.SourceInfo[]>(_responseJson);
        Assert.NotNull(infos);
        Assert.Empty(infos!);
    }

    [Fact]
    public async Task ListSources_WithTwoFakeSources_ReturnsBothOrderedById()
    {
        var post = new FakeAoiSource(
            new SourceDescriptor("postreflow", "Post-reflow AOI", "5.0",
                Capabilities.PinLevel | Capabilities.IsLastInspectionFilter | Capabilities.BarcodeProductView),
            latestPanelUtc: new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        var pre = new FakeAoiSource(
            new SourceDescriptor("prereflow", "Pre-reflow AOI", "4.3.1",
                Capabilities.PastePrintMetrics | Capabilities.FeederAnalytics),
            latestPanelThrows: new InvalidOperationException("simulated outage"));

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAoiSource>(pre);
                services.AddSingleton<IAoiSource>(post);
            }));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "sources-two@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await authed.GetAsync(new Uri("/api/sources", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var infos = await response.Content.ReadFromJsonAsync<SourceEndpoints.SourceInfo[]>(_responseJson);
        Assert.NotNull(infos);
        Assert.Equal(2, infos!.Length);

        // Ordinal sort: "postreflow" > "prereflow" (o < r) -> post is index 0, pre is index 1.
        Assert.Equal("postreflow", infos[0].Id);
        Assert.Equal("Post-reflow AOI", infos[0].DisplayName);
        Assert.Equal("5.0", infos[0].SchemaVersion);
        Assert.True(infos[0].Available);
        Assert.Equal(new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc), infos[0].LatestPanelUtc);
        Assert.Equal(_expectedPostCaps, infos[0].Capabilities);

        Assert.Equal("prereflow", infos[1].Id);
        Assert.False(infos[1].Available);
        Assert.Null(infos[1].LatestPanelUtc);
        Assert.Equal(_expectedPreCaps, infos[1].Capabilities);
    }
}
