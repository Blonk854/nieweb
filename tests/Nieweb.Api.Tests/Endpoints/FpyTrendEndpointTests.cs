using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Identity;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data.Entities;
using Nieweb.DataSources;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// Tests for <c>GET /api/reports/fpy-trend</c> — the per-line FPY trend
/// that runs across every registered source and returns one result per
/// source (namespaced, because machine ids collide across pre / post).
/// </summary>
public sealed class FpyTrendEndpointTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public FpyTrendEndpointTests(NiewebApiFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.NiewebDbContext>();
        db.Database.EnsureCreated();
    }

    private static readonly SourceDescriptor _postDescriptor = new(
        "postreflow", "Post-reflow AOI", "5.0",
        Capabilities.PinLevel | Capabilities.IsLastInspectionFilter);

    private static readonly SourceDescriptor _preDescriptor = new(
        "prereflow", "Pre-reflow AOI", "4.3.1",
        Capabilities.PastePrintMetrics | Capabilities.FeederAnalytics);

    private const string StartUtc = "2026-01-01T00:00:00Z";
    private const string EndUtc = "2026-01-02T00:00:00Z";
    private const int WindowStartEpoch = 1767225600;

    [Fact]
    public async Task FpyTrend_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri(
            $"/api/reports/fpy-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FpyTrend_NonDayWeekBucket_Returns400()
    {
        var post = new FakeAoiSource(_postDescriptor);
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "fpytrend-badbucket@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/fpy-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=hour-6", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// An empty / inverted window must carry the machine-readable
    /// <c>empty_window</c> code so the SPA can render "the date range is
    /// empty" instead of a bare "HTTP 400 Bad Request".
    /// </summary>
    [Fact]
    public async Task FpyTrend_EndBeforeStart_Returns400WithEmptyWindowCode()
    {
        var post = new FakeAoiSource(_postDescriptor);
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "fpytrend-emptywindow@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/fpy-trend?startUtc={EndUtc}&endUtc={StartUtc}&bucket=day", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("empty_window", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FpyTrend_BothSources_ReturnsResultPerSource()
    {
        var start = WindowStartEpoch;
        var post = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels = [Panel(1, 10, start + 60, 1), Panel(2, 10, start + 120, -1)],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var pre = new FakeAoiSource(_preDescriptor)
        {
            SeededPanels = [Panel(3, 20, start + 60, 1)],
            SeededMachines = [new Machine(20, 2, "PRE-20", "AOI")],
        };
        await using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IAoiSource>(post);
            s.AddSingleton<IAoiSource>(pre);
        }));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "fpytrend-both@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/fpy-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&granularity=panel&skipExclusion=raw",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var sources = root.GetProperty("sources");
        Assert.Equal(2, sources.GetArrayLength());

        var ids = sources.EnumerateArray()
            .Select(s => s.GetProperty("source").GetProperty("id").GetString()!)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, ids.Length);
        Assert.Equal("postreflow", ids[0]);
        Assert.Equal("prereflow", ids[1]);

        // Each source has at least one line with points.
        foreach (var s in sources.EnumerateArray())
        {
            Assert.True(s.GetProperty("buckets").GetArrayLength() >= 1);
            var lines = s.GetProperty("lines");
            Assert.True(lines.GetArrayLength() >= 1);
            var firstLine = lines[0];
            Assert.True(firstLine.GetProperty("points").GetArrayLength() >= 1);
        }
    }

    [Fact]
    public async Task FpyTrend_Csv_ReturnsRows()
    {
        var start = WindowStartEpoch;
        var post = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels = [Panel(1, 10, start + 60, 1), Panel(2, 10, start + 120, -1)],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "fpytrend-csv@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/fpy-trend/export.csv?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&granularity=panel&skipExclusion=raw",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("SourceId,SourceName,Granularity,SkipExclusion,MachineId,MachineName,", csv, StringComparison.Ordinal);
        Assert.Contains("postreflow", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FpyTrend_Pdf_ReturnsValidPdf()
    {
        var start = WindowStartEpoch;
        var post = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels = [Panel(1, 10, start + 60, 1), Panel(2, 10, start + 120, -1)],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "fpytrend-pdf@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/fpy-trend/export.pdf?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&granularity=panel&skipExclusion=raw&flavor=diagnostic",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 500, $"PDF too small: {bytes.Length} bytes.");
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public async Task FpyTrend_LineFilter_ResolvesPerSourceByMachineName()
    {
        var start = WindowStartEpoch;
        // Post-reflow carries line 2 (L2PSTAOI) and line 7 (L7PSTAOI).
        var post = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels = [Panel(1, 10, start + 60, 1), Panel(2, 11, start + 60, 1)],
            SeededMachines =
            [
                new Machine(10, 2, "L2PSTAOI", "AOI"),
                new Machine(11, 2, "L7PSTAOI", "AOI"),
            ],
        };
        // Pre-reflow carries ONLY line 7 (L7PREAOI) — no line 2 at all.
        var pre = new FakeAoiSource(_preDescriptor)
        {
            SeededPanels = [Panel(3, 20, start + 60, 1)],
            SeededMachines = [new Machine(20, 2, "L7PREAOI", "AOI")],
        };
        await using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IAoiSource>(post);
            s.AddSingleton<IAoiSource>(pre);
        }));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "fpytrend-line@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/fpy-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&granularity=panel&skipExclusion=raw&lines=2",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sources = doc.RootElement.GetProperty("sources");

        // Pre-reflow has no line-2 machine, so it must contribute NOTHING —
        // it must not fall back to returning every machine. Only post remains.
        Assert.Equal(1, sources.GetArrayLength());
        var only = sources[0];
        Assert.Equal("postreflow", only.GetProperty("source").GetProperty("id").GetString());

        // Within post-reflow, only the line-2 machine (L2PSTAOI, id 10) is kept;
        // the line-7 machine (id 11) is filtered out by the per-source resolve.
        var lines = only.GetProperty("lines");
        Assert.Equal(1, lines.GetArrayLength());
        Assert.Equal(10, lines[0].GetProperty("machineId").GetInt32());
        Assert.Equal("L2PSTAOI", lines[0].GetProperty("machineName").GetString());
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<string> IssueTokenAsync(HttpClient client, string email)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
            if (await users.FindByEmailAsync(email) is null)
            {
                var user = new NiewebUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = email.Split('@')[0],
                    CreatedUtc = DateTime.UtcNow,
                };
                var created = await users.CreateAsync(user, "correctpassword123");
                Assert.True(created.Succeeded);
            }
        }

        var login = new AuthEndpoints.LoginRequest { Email = email, Password = "correctpassword123" };
        using var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        Assert.NotNull(payload);
        return payload!.AccessToken;
    }

    private static PanelRow Panel(int id, int machineId, int date, int status) => new(
        PanelId: id,
        MachineId: machineId,
        LaneNumber: 1,
        PanelBarCode: $"BC-{id:D6}",
        PanelNumericDate: date,
        NbOfValidCards: 4,
        TestTime: 12.5,
        PanelStatus: status,
        AnomalyBr: 0,
        AnomalyAr: 0,
        HasBeenReviewed: false,
        NbOfTestedObject: 100,
        NbOfErrorObject: status is (-2) or (-1) ? 3 : 0,
        OperatorId: 42,
        ProductId: 500,
        RecipeId: 600);
}
