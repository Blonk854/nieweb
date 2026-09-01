using System.Net;
using System.Text.Json;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// Smoke tests for the OIDC endpoint group (I2). Verifies that when
/// SSO is disabled (the default) <c>/auth/config</c> advertises so,
/// and the challenge / callback-return endpoints refuse the request
/// cleanly rather than 500-ing on a missing scheme registration.
/// </summary>
public sealed class OidcEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public OidcEndpointsTests(NiewebApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthConfig_reports_oidc_disabled_by_default()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/auth/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("oidcEnabled").GetBoolean());
        Assert.Equal(string.Empty, root.GetProperty("oidcButtonLabel").GetString());
        Assert.Equal(string.Empty, root.GetProperty("oidcChallengePath").GetString());
        Assert.True(root.GetProperty("analyseEnabled").GetBoolean());
    }

    [Fact]
    public async Task OidcChallenge_returns_404_when_disabled()
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/auth/oidc/challenge?returnUrl=/app/");

        // OIDC off -> the endpoint pretends not to exist so a scanner
        // can't fingerprint whether SSO is configured on this host.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OidcCallbackReturn_returns_404_when_disabled()
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/auth/oidc/callback-return?returnUrl=/app/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
