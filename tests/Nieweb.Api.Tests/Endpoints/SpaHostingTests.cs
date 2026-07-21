using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// Confirms the SPA hosting behaviour wired in Program.cs (F9/O1):
/// when wwwroot/app/index.html is present the API serves it as the
/// fallback for unknown /app/* routes (so hard-refreshing a deep
/// TanStack Router URL works) and redirects the bare root to /app/.
/// When the built SPA is absent we deliberately do NOT register those
/// routes so an API-only test host keeps returning 404 for unknown
/// paths - the default NiewebApiFactory covers that case.
/// </summary>
public sealed class SpaHostingTests
{
    [Fact]
    public async Task RootRedirectsToApp_WhenSpaIsPresent()
    {
        using var factory = new SpaFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/app/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DeepSpaRoute_FallsBackToIndexHtml_WhenSpaIsPresent()
    {
        using var factory = new SpaFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/app/report/panel-yield", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(SpaFactory.SentinelMarker, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_Returns404_WhenSpaIsAbsent()
    {
        // Point WebRoot at an empty temp folder so File.Exists on
        // wwwroot/app/index.html returns false and the fallback branch
        // in Program.cs is skipped. Without this override the local
        // Nieweb.Api project's real wwwroot/app (populated by an
        // earlier `dotnet publish`) would leak into the test host.
        using var factory = new EmptySpaFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/app/report/panel-yield", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Variant of NiewebApiFactory that lays down a throw-away
    /// wwwroot/app/index.html before the host is built so the SPA
    /// fallback branch in Program.cs registers itself.
    /// </summary>
    private sealed class SpaFactory : NiewebApiFactory
    {
        internal const string SentinelMarker = "<!--nieweb-spa-sentinel-->";
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "nieweb-spa-tests-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            var appDir = Path.Combine(_root, "wwwroot", "app");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(
                Path.Combine(appDir, "index.html"),
                "<!doctype html><html><body>" + SentinelMarker + "</body></html>");

            base.ConfigureWebHost(builder);
            builder.UseWebRoot(Path.Combine(_root, "wwwroot"));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_root))
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup - the temp folder will be
                    // reaped by Windows eventually.
                }
            }
        }
    }

    /// <summary>
    /// Points the host at a WebRoot that contains no built SPA so the
    /// fallback branch in Program.cs is skipped and unknown /app/*
    /// paths surface as 404s.
    /// </summary>
    private sealed class EmptySpaFactory : NiewebApiFactory
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "nieweb-spa-tests-empty-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            Directory.CreateDirectory(Path.Combine(_root, "wwwroot"));
            base.ConfigureWebHost(builder);
            builder.UseWebRoot(Path.Combine(_root, "wwwroot"));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_root))
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup.
                }
            }
        }
    }
}
