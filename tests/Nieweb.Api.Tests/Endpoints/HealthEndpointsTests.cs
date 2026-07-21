using System.Net;
using System.Text.Json;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// Confirms the /health/live, /health/ready, and /health/db probes
/// wired by HealthEndpoints. All three return JSON with a Healthy
/// status against the in-memory SQLite NiewebDbContext used by
/// NiewebApiFactory. AOI Superviseur DBs are intentionally not
/// probed by these endpoints - see HealthEndpoints doc comment.
/// </summary>
public sealed class HealthEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public HealthEndpointsTests(NiewebApiFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.NiewebDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Live_ReturnsHealthy()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadPayloadAsync(response);
        Assert.Equal("Healthy", payload.Status);
        Assert.Contains("self", payload.CheckNames);
        Assert.DoesNotContain("nieweb-db", payload.CheckNames);
    }

    [Fact]
    public async Task Ready_ReturnsHealthy_AndIncludesDbCheck()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadPayloadAsync(response);
        Assert.Equal("Healthy", payload.Status);
        Assert.Contains("self", payload.CheckNames);
        Assert.Contains("nieweb-db", payload.CheckNames);
    }

    [Fact]
    public async Task Db_ReturnsHealthy_AndOnlyIncludesDbCheck()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/db", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadPayloadAsync(response);
        Assert.Equal("Healthy", payload.Status);
        Assert.Contains("nieweb-db", payload.CheckNames);
        Assert.DoesNotContain("self", payload.CheckNames);
    }

    [Fact]
    public async Task HealthEndpoints_DoNotRequireAuthentication()
    {
        using var client = _factory.CreateClient();

        // No Bearer token; must succeed anyway.
        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        using var db = await client.GetAsync(new Uri("/health/db", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, db.StatusCode);
    }

    private static async Task<HealthPayload> ReadPayloadAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        var status = root.GetProperty("status").GetString() ?? throw new InvalidOperationException("status missing");
        var names = root.GetProperty("checks")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToArray();
        return new HealthPayload(status, names);
    }

    private sealed record HealthPayload(string Status, string[] CheckNames);
}
