using Nieweb.Api.Endpoints;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// Verifies the open-redirect defence on
/// <see cref="OidcEndpoints.NormaliseReturnUrl(string?)"/>: anything
/// that could bounce the browser off-origin must be discarded and
/// replaced with the SPA root.
/// </summary>
public sealed class OidcReturnUrlNormaliserTests
{
    [Theory]
    [InlineData(null, "/app/")]
    [InlineData("", "/app/")]
    [InlineData("   ", "/app/")]
    // Absolute URLs (any scheme) - the colon triggers rejection.
    [InlineData("https://evil.example.com/", "/app/")]
    [InlineData("http://evil.example.com/", "/app/")]
    [InlineData("javascript:alert(1)", "/app/")]
    [InlineData("//evil.example.com/path", "/app/")]
    [InlineData("\\\\evil.example.com\\path", "/app/")]
    // Relative-without-slash: could be re-resolved against the current URL.
    [InlineData("relative-path", "/app/")]
    // Server-side paths that aren't the SPA get coerced to the SPA root.
    [InlineData("/auth/oidc/challenge", "/app/")]
    [InlineData("/api/reports/panel-yield", "/app/")]
    [InlineData("/health/live", "/app/")]
    // Valid SPA paths pass through unchanged.
    [InlineData("/app/", "/app/")]
    [InlineData("/app/report/panel-yield", "/app/report/panel-yield")]
    [InlineData("/app/admin/users", "/app/admin/users")]
    public void NormaliseReturnUrl_rejects_anything_off_SPA(string? input, string expected)
    {
        var actual = OidcEndpoints.NormaliseReturnUrl(input);

        Assert.Equal(expected, actual);
    }
}
