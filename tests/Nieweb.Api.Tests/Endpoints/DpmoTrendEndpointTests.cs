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
/// Tests for <c>GET /api/reports/dpmo-trend</c> — the per-line DPMO trend
/// that runs across every registered source and returns one result per
/// source (namespaced, because machine ids collide across pre / post).
/// </summary>
public sealed class DpmoTrendEndpointTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public DpmoTrendEndpointTests(NiewebApiFactory factory)
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

    private const int ComponentType = 0x01;
    private const long BitObjectMissing = 1L << 0;
    private const long BitPolarityError = 1L << 1;

    [Fact]
    public async Task DpmoTrend_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri(
            $"/api/reports/dpmo-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DpmoTrend_NonDayWeekBucket_Returns400()
    {
        var post = new FakeAoiSource(_postDescriptor);
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-badbucket@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=hour-6", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// An empty / inverted window must carry the machine-readable
    /// <c>empty_window</c> code so the SPA can render "the date range is
    /// empty" instead of a bare "HTTP 400 Bad Request".
    /// </summary>
    [Fact]
    public async Task DpmoTrend_EndBeforeStart_Returns400WithEmptyWindowCode()
    {
        var post = new FakeAoiSource(_postDescriptor);
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-emptywindow@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend?startUtc={EndUtc}&endUtc={StartUtc}&bucket=day", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("empty_window", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DpmoTrend_UnknownOpportunity_Returns400()
    {
        var post = new FakeAoiSource(_postDescriptor);
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-badopp@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&opportunity=solder",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DpmoTrend_BothSources_ReturnsResultPerSourceWithAllThreeNumerators()
    {
        var start = WindowStartEpoch;
        var post = new FakeAoiSource(_postDescriptor)
        {
            SeededCards = [Card(10, start + 10, nbTestsOnComp: 100)],
            SeededTestedObjects =
            [
                // 2 AOI bits, 1 survives review -> Real 1, Dummy 1.
                Obj(10, start + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
        var pre = new FakeAoiSource(_preDescriptor)
        {
            SeededCards = [Card(20, start + 10, nbTestsOnComp: 50)],
            SeededTestedObjects =
            [
                Obj(20, start + 60, ComponentType, BitObjectMissing, BitObjectMissing),
            ],
            SeededMachines = [new Machine(20, 2, "PRE-20", "AOI")],
        };
        await using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IAoiSource>(post);
            s.AddSingleton<IAoiSource>(pre);
        }));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-both@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&opportunity=components&skipExclusion=raw",
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
        Assert.Equal("postreflow", ids[0]);
        Assert.Equal("prereflow", ids[1]);

        // Post-reflow line 10: 100 opportunities, 2 AOI bits -> 20 000 DPMO,
        // 1 real -> 10 000, 1 dummy -> 10 000. All three ship in one payload
        // so the client toggles without a refetch.
        var post10 = sources.EnumerateArray()
            .Single(s => s.GetProperty("source").GetProperty("id").GetString() == "postreflow")
            .GetProperty("lines")[0];
        var kpi = post10.GetProperty("points")[0].GetProperty("kpi");
        Assert.Equal(100, kpi.GetProperty("opportunityCount").GetInt64());
        Assert.Equal(2, kpi.GetProperty("defectsAoi").GetInt64());
        Assert.Equal(1, kpi.GetProperty("defectsReal").GetInt64());
        Assert.Equal(1, kpi.GetProperty("defectsDummy").GetInt64());
        Assert.Equal(20_000d, kpi.GetProperty("dpmoAoi").GetDouble());
        Assert.Equal(10_000d, kpi.GetProperty("dpmoReal").GetDouble());
        Assert.Equal(10_000d, kpi.GetProperty("dpmoDummy").GetDouble());
    }

    [Fact]
    public async Task DpmoTrend_Csv_ReturnsRows()
    {
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(SeededPost())));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-csv@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend/export.csv?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&skipExclusion=raw",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("Opportunities,DefectsAoi,DefectsReal,DefectsDummy,", csv, StringComparison.Ordinal);
        Assert.Contains("postreflow", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DpmoTrend_Xlsx_ReturnsWorkbook()
    {
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(SeededPost())));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-xlsx@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend/export.xlsx?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&skipExclusion=raw",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 500, $"XLSX too small: {bytes.Length} bytes.");
        // XLSX is a ZIP container: "PK\x03\x04".
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public async Task DpmoTrend_Pdf_ReturnsValidPdf()
    {
        await using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IAoiSource>(SeededPost())));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-pdf@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend/export.pdf?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&skipExclusion=raw&numerator=real",
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
    public async Task DpmoTrend_LineFilter_ResolvesPerSourceByMachineName()
    {
        var start = WindowStartEpoch;
        // Post-reflow carries line 2 (L2PSTAOI) and line 7 (L7PSTAOI).
        var post = new FakeAoiSource(_postDescriptor)
        {
            SeededCards =
            [
                Card(10, start + 10, nbTestsOnComp: 100),
                Card(11, start + 10, nbTestsOnComp: 100),
            ],
            SeededTestedObjects =
            [
                Obj(10, start + 60, ComponentType, BitObjectMissing, BitObjectMissing),
                Obj(11, start + 60, ComponentType, BitPolarityError, BitPolarityError),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "L2PSTAOI", "AOI"),
                new Machine(11, 2, "L7PSTAOI", "AOI"),
            ],
        };
        // Pre-reflow carries ONLY line 7 (L7PREAOI) — no line 2 at all.
        var pre = new FakeAoiSource(_preDescriptor)
        {
            SeededCards = [Card(20, start + 10, nbTestsOnComp: 50)],
            SeededTestedObjects = [Obj(20, start + 60, ComponentType, BitObjectMissing, BitObjectMissing)],
            SeededMachines = [new Machine(20, 2, "L7PREAOI", "AOI")],
        };
        await using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IAoiSource>(post);
            s.AddSingleton<IAoiSource>(pre);
        }));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-line@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&skipExclusion=raw&lines=2",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sources = doc.RootElement.GetProperty("sources");

        // Pre-reflow has no line-2 machine, so it must contribute NOTHING —
        // it must not fall back to returning every machine (an empty machine
        // list reads as "no filter" further down the stack). Only post remains.
        Assert.Equal(1, sources.GetArrayLength());
        var only = sources[0];
        Assert.Equal("postreflow", only.GetProperty("source").GetProperty("id").GetString());

        // Within post-reflow, only the line-2 machine (L2PSTAOI, id 10) is kept.
        var lines = only.GetProperty("lines");
        Assert.Equal(1, lines.GetArrayLength());
        Assert.Equal(10, lines[0].GetProperty("machineId").GetInt32());
        Assert.Equal("L2PSTAOI", lines[0].GetProperty("machineName").GetString());
    }

    [Fact]
    public async Task DpmoTrend_OfflineSource_IsOmittedNotFatal()
    {
        // A source whose catalogue lookup throws must be dropped from the
        // response, not surfaced as a 500 that blanks the whole page.
        var healthy = SeededPost();
        var broken = new FakeAoiSource(_preDescriptor)
        {
            ListMachinesThrows = new InvalidOperationException("simulated outage"),
        };
        await using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IAoiSource>(healthy);
            s.AddSingleton<IAoiSource>(broken);
        }));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "dpmotrend-offline@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(new Uri(
            $"/api/reports/dpmo-trend?startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&skipExclusion=raw",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sources = doc.RootElement.GetProperty("sources");
        Assert.Equal(1, sources.GetArrayLength());
        Assert.Equal("postreflow", sources[0].GetProperty("source").GetProperty("id").GetString());
    }

    // ---- helpers ----------------------------------------------------------

    private static FakeAoiSource SeededPost() => new(_postDescriptor)
    {
        SeededCards = [Card(10, WindowStartEpoch + 10, nbTestsOnComp: 100)],
        SeededTestedObjects =
        [
            Obj(10, WindowStartEpoch + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing),
        ],
        SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
    };

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

    private static TestedObjectRow Obj(
        int machineId, int date, int objectTypeId, long errorTable, long errorTableAr) => new(
        PanelId: 1,
        CardIdOnPanel: 1,
        ObjectId: date,
        ObjectTypeId: objectTypeId,
        ErrorTable: errorTable,
        ErrorTableAr: errorTableAr,
        Status: errorTable == 0 ? 0 : 1,
        MachineId: machineId,
        ProductId: 500,
        PanelNumericDate: date,
        Topology: null,
        PartNumberName: null,
        JedecName: null);

    private static CardRow Card(int machineId, int date, int nbTestsOnComp, int? nbTestsOnPads = null) => new(
        PanelId: 1,
        CardIdOnPanel: 1,
        CardStatus: 0,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: 0,
        NbOfErrorObject: 0,
        MachineId: machineId,
        ProductId: 500,
        PanelNumericDate: date,
        NbOfTestsOnComp: nbTestsOnComp,
        NbOfTestsOnPads: nbTestsOnPads);
}
