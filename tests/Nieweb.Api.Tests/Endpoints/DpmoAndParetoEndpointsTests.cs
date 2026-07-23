using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data.Entities;
using Nieweb.DataSources;
using Nieweb.Reports;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// End-to-end HTTP tests for the DPMO and Pareto endpoints exposed
/// on <c>GET /api/reports/dpmo-table</c> and
/// <c>GET /api/reports/pareto</c>. Same authentication, source
/// resolution, and window-validation contracts as the panel-yield
/// endpoints — the headline scenarios prove numeric parity through
/// the wire.
/// </summary>
public sealed class DpmoAndParetoEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public DpmoAndParetoEndpointsTests(NiewebApiFactory factory)
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

    private async Task<string> IssueTokenAsync(HttpClient client, string email)
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

    private static readonly SourceDescriptor _postDescriptor = new(
        "postreflow", "Post-reflow AOI", "5.0",
        Capabilities.PinLevel | Capabilities.IsLastInspectionFilter | Capabilities.BarcodeProductView);

    private const string StartUtc = "2026-01-01T00:00:00Z";
    private const string EndUtc = "2026-01-02T00:00:00Z";
    private const int WindowStartEpoch = 1767225600;

    private const int ComponentType = 0x01;

    private const long BitObjectMissing = 1L << 0; // bit 1
    private const long BitPolarityError = 1L << 1; // bit 2
    private const long BitSolderJoint = 1L << 2;   // bit 3

    // -------------------------------------------------------------------------
    // DPMO endpoint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dpmo_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/dpmo-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dpmo_UnknownSource_Returns404()
    {
        var (authed, _) = await AuthedClientAsync("dpmo-unknown@nieweb.test");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
    }

    [Fact]
    public async Task Dpmo_MissingGroupBy_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("dpmo-missing-groupby@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Dpmo_UnknownGroupBy_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("dpmo-bad-groupby@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=bogus", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Dpmo_InvalidWindow_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("dpmo-bad-window@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table?sourceId=postreflow&startUtc={EndUtc}&endUtc={StartUtc}&groupBy=aoi-machine", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    /// <summary>
    /// Machine 10: 4 tested objects (2 defects on one, 1 on another, 2 clean) → 3 defects / 4 opps → DPMO 750 000.
    /// Machine 11: 2 tested objects (1 defect on one, 1 clean)               → 1 defect  / 2 opps → DPMO 500 000.
    /// Rows sorted descending by DPMO.
    /// </summary>
    [Fact]
    public async Task Dpmo_GroupByAoiMachine_ReturnsExpectedRowsSortedDesc()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing | BitPolarityError),
                Obj(10, WindowStartEpoch + 61, ComponentType, 0, 0),
                Obj(10, WindowStartEpoch + 62, ComponentType, BitSolderJoint, BitSolderJoint),
                Obj(10, WindowStartEpoch + 63, ComponentType, 0, 0),
                Obj(11, WindowStartEpoch + 70, ComponentType, BitObjectMissing, BitObjectMissing),
                Obj(11, WindowStartEpoch + 71, ComponentType, 0, 0),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("dpmo-happy@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine&numerator=aoi", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DpmoTableResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal("postreflow", payload!.Source.Id);
        Assert.Equal(DpmoGroupBy.AoiMachine, payload.GroupBy);
        Assert.Equal(DpmoNumerator.Aoi, payload.Numerator);
        Assert.Equal(6L, payload.Overall.OpportunityCount);
        Assert.Equal(4L, payload.Overall.DefectBitCount);
        Assert.Equal(2, payload.Rows.Count);
        Assert.Equal("10", payload.Rows[0].GroupKey);
        Assert.Equal("AOI-10", payload.Rows[0].GroupName);
        Assert.Equal(750_000d, payload.Rows[0].Kpi.DpmoPpm);
        Assert.Equal("11", payload.Rows[1].GroupKey);
        Assert.Equal(500_000d, payload.Rows[1].Kpi.DpmoPpm);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Dpmo_GroupByDefect_KebabAliasWorks()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing | BitPolarityError),
                Obj(10, WindowStartEpoch + 61, ComponentType, BitObjectMissing, BitObjectMissing),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("dpmo-defect@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=defect&numerator=aoi", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DpmoTableResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(DpmoGroupBy.Defect, payload!.GroupBy);
        Assert.Equal(2, payload.Rows.Count);
        Assert.Equal("Object missing", payload.Rows[0].GroupName);
        Assert.Equal("Polarity error", payload.Rows[1].GroupName);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Pareto endpoint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Pareto_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/pareto?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=product", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pareto_UnknownSource_Returns404()
    {
        var (authed, _) = await AuthedClientAsync("pareto-unknown@nieweb.test");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}&axis=product", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
    }

    [Fact]
    public async Task Pareto_MissingAxis_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("pareto-missing-axis@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Pareto_DefectBitOutOfRange_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("pareto-bad-bit@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=part-number&defectBits=99", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Pareto_BadVitalFewThreshold_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("pareto-bad-threshold@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=product&vitalFewThreshold=150", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    /// <summary>
    /// Boss's canonical scenario over HTTP: Product A owns 100 opps /
    /// 10 defects, Product B owns 20 opps / 5 defects. Absolute-count
    /// Pareto MUST put A on top even though B has a higher DPMO.
    /// </summary>
    [Fact]
    public async Task Pareto_ProductAxis_VolumeWeighted_RanksHighVolumeContributorFirst()
    {
        var objects = new List<TestedObjectRow>(120);
        for (var i = 0; i < 100; i++)
        {
            var hasDefect = i < 10;
            objects.Add(Obj(10, WindowStartEpoch + 60 + i,
                objectId: 10_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 100));
        }
        for (var i = 0; i < 20; i++)
        {
            var hasDefect = i < 5;
            objects.Add(Obj(10, WindowStartEpoch + 60 + i,
                objectId: 20_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 200));
        }

        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(100, "Product A", null, null),
                new Product(200, "Product B", null, null),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("pareto-boss@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=product", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ParetoResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(ParetoAxis.Product, payload!.Axis);
        Assert.Equal(120L, payload.Overall.OpportunityCount);
        Assert.Equal(15L, payload.Overall.DefectBitCount);

        Assert.Equal(2, payload.Rows.Count);
        Assert.Equal("Product A", payload.Rows[0].GroupName);
        Assert.Equal(10L, payload.Rows[0].DefectCount);
        Assert.Equal("Product B", payload.Rows[1].GroupName);
        Assert.Equal(5L, payload.Rows[1].DefectCount);
        Assert.True(payload.Rows[1].DpmoPpm > payload.Rows[0].DpmoPpm,
            "B must have higher DPMO than A — otherwise the volume-weighting invariant is not being exercised.");

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Pareto_TopN_CollapsesOverflowIntoOthers()
    {
        var objects = new List<TestedObjectRow>();
        // Product defect counts: 10, 8, 6, 4, 2 (total 30). TopN=3 shows 10/8/6, Others=4+2=6.
        AddProduct(objects, productId: 1, defectiveCount: 10);
        AddProduct(objects, productId: 2, defectiveCount: 8);
        AddProduct(objects, productId: 3, defectiveCount: 6);
        AddProduct(objects, productId: 4, defectiveCount: 4);
        AddProduct(objects, productId: 5, defectiveCount: 2);

        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(1, "P1", null, null),
                new Product(2, "P2", null, null),
                new Product(3, "P3", null, null),
                new Product(4, "P4", null, null),
                new Product(5, "P5", null, null),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("pareto-topn@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=product&topN=3", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ParetoResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Rows.Count);
        Assert.NotNull(payload.OthersBucket);
        Assert.Equal(6L, payload.OthersBucket!.DefectCount);
        Assert.Equal(100d, payload.OthersBucket.CumulativePercent);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Pareto_DrillInByDefectBits_NarrowsRanking()
    {
        // PN-A: 5 "Object missing" + 1 "Polarity error"
        // PN-B: 2 "Object missing" + 4 "Polarity error"
        // Drill to defectBits=1 → PN-A ranks with 5 vs PN-B with 2.
        var objects = new List<TestedObjectRow>();
        for (var i = 0; i < 5; i++)
        {
            objects.Add(Obj(10, WindowStartEpoch + 60 + i, 40_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-A"));
        }
        objects.Add(Obj(10, WindowStartEpoch + 65, 40_100, ComponentType, BitPolarityError, BitPolarityError, partNumberName: "PN-A"));
        for (var i = 0; i < 2; i++)
        {
            objects.Add(Obj(10, WindowStartEpoch + 70 + i, 41_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-B"));
        }
        for (var i = 0; i < 4; i++)
        {
            objects.Add(Obj(10, WindowStartEpoch + 75 + i, 41_100 + i, ComponentType, BitPolarityError, BitPolarityError, partNumberName: "PN-B"));
        }

        var fake = new FakeAoiSource(_postDescriptor) { SeededTestedObjects = objects };
        var (authed, factory) = await AuthedClientAsync("pareto-drill@nieweb.test", fake);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=part-number&defectBits=1", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ParetoResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Rows.Count);
        Assert.Equal("PN-A", payload.Rows[0].GroupKey);
        Assert.Equal(5L, payload.Rows[0].DefectCount);
        Assert.Equal("PN-B", payload.Rows[1].GroupKey);
        Assert.Equal(2L, payload.Rows[1].DefectCount);
        // AppliedFilters echoes the drill so a UI can render a breadcrumb.
        Assert.Equal([1], payload.AppliedFilters.DefectBits);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<(HttpClient Authed, WebApplicationFactory<Program>? OwnedFactory)> AuthedClientAsync(
        string email, FakeAoiSource? source = null)
    {
        WebApplicationFactory<Program>? owned = null;
        WebApplicationFactory<Program> factory;
        if (source is not null)
        {
            owned = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(source)));
            factory = owned;
        }
        else
        {
            factory = _factory;
        }

        using var login = factory.CreateClient();
        var token = await IssueTokenAsync(login, email);
        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (authed, owned);
    }

    private static void AddProduct(List<TestedObjectRow> sink, int productId, int defectiveCount)
    {
        for (var i = 0; i < defectiveCount; i++)
        {
            sink.Add(Obj(
                machineId: 10,
                date: WindowStartEpoch + productId * 100 + i,
                objectId: productId * 1_000 + i,
                objectTypeId: ComponentType,
                errorTable: BitObjectMissing,
                errorTableAr: BitObjectMissing,
                productId: productId));
        }
    }

    private static TestedObjectRow Obj(
        int machineId,
        int date,
        int objectId,
        int objectTypeId,
        long errorTable,
        long errorTableAr,
        int productId = 500,
        string? topology = null,
        string? partNumberName = null,
        string? jedecName = null)
    {
        return new TestedObjectRow(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: objectId,
            ObjectTypeId: objectTypeId,
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: date,
            Topology: topology,
            PartNumberName: partNumberName,
            JedecName: jedecName);
    }

    // Wrapper matching the DPMO tests' Obj(...) helper (5-arg form used above).
    private static TestedObjectRow Obj(int machineId, int date, int objectTypeId, long errorTable, long errorTableAr)
        => Obj(machineId, date, objectId: date, objectTypeId: objectTypeId, errorTable: errorTable, errorTableAr: errorTableAr);

    // -------------------------------------------------------------------------
    // DPMO CSV export
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DpmoCsv_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DpmoCsv_UnknownSource_Returns404()
    {
        var (authed, _) = await AuthedClientAsync("dpmo-csv-unknown@nieweb.test");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.csv?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
    }

    [Fact]
    public async Task DpmoCsv_MissingGroupBy_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("dpmo-csv-missing-groupby@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task DpmoCsv_ReturnsUtf8CsvWithOverallAndPerMachineRows()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing | BitPolarityError),
                Obj(10, WindowStartEpoch + 61, ComponentType, 0, 0),
                Obj(10, WindowStartEpoch + 62, ComponentType, BitSolderJoint, BitSolderJoint),
                Obj(10, WindowStartEpoch + 63, ComponentType, 0, 0),
                Obj(11, WindowStartEpoch + 70, ComponentType, BitObjectMissing, BitObjectMissing),
                Obj(11, WindowStartEpoch + 71, ComponentType, 0, 0),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("dpmo-csv-happy@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine&numerator=aoi", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(
            "dpmo-postreflow-AoiMachine-20260101-20260102.csv",
            response.Content.Headers.ContentDisposition.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length); // header + OVERALL + 2 machine rows
        Assert.Equal(
            "SourceId,SourceName,WindowStartUtc,WindowEndUtc,GroupBy,Numerator,Opportunity,GroupKey,GroupName,TestedObjectCount,OpportunityCount,DefectBitCount,DpmoPpm",
            lines[0]);
        // OVERALL: 6 tested, 6 opps, 4 defect bits (2+1+1), 666666.6667
        Assert.StartsWith(
            "postreflow,Post-reflow AOI,2026-01-01T00:00:00Z,2026-01-02T00:00:00Z,AoiMachine,Aoi,All,OVERALL,Overall,6,6,4,",
            lines[1], StringComparison.Ordinal);
        // Machine 10 first (750000), then 11 (500000).
        Assert.Contains(",AoiMachine,Aoi,All,10,AOI-10,4,4,3,750000", lines[2], StringComparison.Ordinal);
        Assert.Contains(",AoiMachine,Aoi,All,11,AOI-11,2,2,1,500000", lines[3], StringComparison.Ordinal);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // DPMO XLSX export
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DpmoXlsx_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DpmoXlsx_ReturnsWorkbookWithSummaryAndRowsSheets()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing | BitPolarityError),
                Obj(10, WindowStartEpoch + 61, ComponentType, 0, 0),
                Obj(10, WindowStartEpoch + 62, ComponentType, BitSolderJoint, BitSolderJoint),
                Obj(11, WindowStartEpoch + 70, ComponentType, BitObjectMissing, BitObjectMissing),
                Obj(11, WindowStartEpoch + 71, ComponentType, 0, 0),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("dpmo-xlsx-happy@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine&numerator=aoi", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "dpmo-postreflow-AoiMachine-20260101-20260102.xlsx",
            response.Content.Headers.ContentDisposition?.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);

        var summary = workbook.Worksheet("Summary");
        Assert.Equal("Source Id", summary.Cell("A3").GetString());
        Assert.Equal("postreflow", summary.Cell("B3").GetString());
        Assert.Equal("AoiMachine", summary.Cell("B7").GetString());
        Assert.Equal("Aoi", summary.Cell("B8").GetString());
        // Overall metrics.
        Assert.Equal("Tested Objects", summary.Cell("A12").GetString());
        Assert.Equal(5, (int)summary.Cell("B12").GetDouble());
        Assert.Equal(5, (int)summary.Cell("B13").GetDouble());
        Assert.Equal(4, (int)summary.Cell("B14").GetDouble());

        var rows = workbook.Worksheet("Rows");
        Assert.Equal("GroupKey", rows.Cell(1, 1).GetString());
        // Row 2 = highest DPMO = machine 10.
        Assert.Equal("10", rows.Cell(2, 1).GetString());
        Assert.Equal("AOI-10", rows.Cell(2, 2).GetString());
        Assert.Equal(3, (int)rows.Cell(2, 3).GetDouble());
        Assert.Equal(3, (int)rows.Cell(2, 4).GetDouble());
        Assert.Equal(3, (int)rows.Cell(2, 5).GetDouble());
        Assert.Equal(1_000_000d, rows.Cell(2, 6).GetDouble(), 4);
        // Row 3 = machine 11.
        Assert.Equal("11", rows.Cell(3, 1).GetString());
        Assert.Equal(500_000d, rows.Cell(3, 6).GetDouble(), 4);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // -------------------------------------------------------------------------
    // Pareto CSV export
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParetoCsv_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/pareto/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=product", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ParetoCsv_UnknownSource_Returns404()
    {
        var (authed, _) = await AuthedClientAsync("pareto-csv-unknown@nieweb.test");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto/export.csv?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}&axis=product", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
    }

    [Fact]
    public async Task ParetoCsv_MissingAxis_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("pareto-csv-missing-axis@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    /// <summary>
    /// Boss's scenario expressed as CSV export: rank 1 MUST be Product A
    /// even though Product B has the higher DPMO. Volume-weighted Pareto
    /// invariant preserved end-to-end from HTTP to file bytes.
    /// </summary>
    [Fact]
    public async Task ParetoCsv_BossScenario_ProductARanksFirstAndOthersRowAbsent()
    {
        var objects = new List<TestedObjectRow>(120);
        for (var i = 0; i < 100; i++)
        {
            var hasDefect = i < 10;
            objects.Add(Obj(10, WindowStartEpoch + 60 + i,
                objectId: 10_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 100));
        }
        for (var i = 0; i < 20; i++)
        {
            var hasDefect = i < 5;
            objects.Add(Obj(10, WindowStartEpoch + 60 + i,
                objectId: 20_000 + i,
                objectTypeId: ComponentType,
                errorTable: hasDefect ? BitObjectMissing : 0,
                errorTableAr: hasDefect ? BitObjectMissing : 0,
                productId: 200));
        }

        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(100, "Product A", null, null),
                new Product(200, "Product B", null, null),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("pareto-csv-boss@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=product", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "pareto-postreflow-Product-20260101-20260102.csv",
            response.Content.Headers.ContentDisposition?.FileName);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // header + 2 product rows, no OTHERS
        Assert.Equal(
            "SourceId,SourceName,WindowStartUtc,WindowEndUtc,Axis,Numerator,Opportunity,Weight,Rank,GroupKey,GroupName,DefectCount,WeightedScore,OpportunityCount,OpportunitySharePercent,DpmoPpm,DefectSharePercent,CumulativePercent,IsVitalFew",
            lines[0]);
        // Rank 1 = Product A (higher DefectCount despite lower DPMO).
        Assert.Contains(",Product,Real,All,Count,1,100,Product A,10,10,100,", lines[1], StringComparison.Ordinal);
        // Rank 2 = Product B.
        Assert.Contains(",Product,Real,All,Count,2,200,Product B,5,5,20,", lines[2], StringComparison.Ordinal);
        Assert.DoesNotContain(",OTHERS,", csv, StringComparison.Ordinal);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task ParetoCsv_TopN_AppendsOthersRow()
    {
        var objects = new List<TestedObjectRow>();
        AddProduct(objects, productId: 1, defectiveCount: 10);
        AddProduct(objects, productId: 2, defectiveCount: 8);
        AddProduct(objects, productId: 3, defectiveCount: 6);
        AddProduct(objects, productId: 4, defectiveCount: 4);
        AddProduct(objects, productId: 5, defectiveCount: 2);

        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = objects,
            SeededProducts =
            [
                new Product(1, "P1", null, null),
                new Product(2, "P2", null, null),
                new Product(3, "P3", null, null),
                new Product(4, "P4", null, null),
                new Product(5, "P5", null, null),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("pareto-csv-topn@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=product&topN=3", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        // header + 3 rows + 1 OTHERS row = 5 lines.
        Assert.Equal(5, lines.Length);
        Assert.Contains(",OTHERS,", lines[4], StringComparison.Ordinal);
        // Others aggregates 4 + 2 = 6 defect bits.
        Assert.Contains(",6,6,", lines[4], StringComparison.Ordinal);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Pareto XLSX export
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParetoXlsx_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/pareto/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=product", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ParetoXlsx_ReturnsThreeSheetsWithAppliedFiltersEchoed()
    {
        var objects = new List<TestedObjectRow>();
        // PN-A dominates when we drill by defect bit 1.
        for (var i = 0; i < 5; i++)
        {
            objects.Add(Obj(10, WindowStartEpoch + 60 + i, 40_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-A"));
        }
        for (var i = 0; i < 2; i++)
        {
            objects.Add(Obj(10, WindowStartEpoch + 70 + i, 41_000 + i, ComponentType, BitObjectMissing, BitObjectMissing, partNumberName: "PN-B"));
        }

        var fake = new FakeAoiSource(_postDescriptor) { SeededTestedObjects = objects };
        var (authed, factory) = await AuthedClientAsync("pareto-xlsx-happy@nieweb.test", fake);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=part-number&defectBits=1", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "pareto-postreflow-PartNumber-20260101-20260102.xlsx",
            response.Content.Headers.ContentDisposition?.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);

        // Summary sheet.
        var summary = workbook.Worksheet("Summary");
        Assert.Equal("postreflow", summary.Cell("B3").GetString());
        Assert.Equal("PartNumber", summary.Cell("B7").GetString());
        Assert.Equal(7, (int)summary.Cell("B13").GetDouble()); // Tested Objects
        Assert.Equal(7, (int)summary.Cell("B15").GetDouble()); // Defect Bits (all defective)

        // Applied Filters sheet: DefectBits row must echo "1".
        var filters = workbook.Worksheet("Applied Filters");
        Assert.Equal("Filter", filters.Cell(1, 1).GetString());
        // Row order: Machine, Product, DefectBits, Topologies, PartNumbers, JedecNames.
        Assert.Equal("DefectBits", filters.Cell(4, 1).GetString());
        Assert.Equal("1", filters.Cell(4, 2).GetString());

        // Rows sheet: PN-A first (5), PN-B second (2).
        var rows = workbook.Worksheet("Rows");
        Assert.Equal("Rank", rows.Cell(1, 1).GetString());
        Assert.Equal("1", rows.Cell(2, 1).GetString());
        Assert.Equal("PN-A", rows.Cell(2, 2).GetString());
        Assert.Equal(5, (int)rows.Cell(2, 4).GetDouble());
        Assert.Equal("2", rows.Cell(3, 1).GetString());
        Assert.Equal("PN-B", rows.Cell(3, 2).GetString());
        Assert.Equal(2, (int)rows.Cell(3, 4).GetDouble());

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // #18915 regression: no 250-row cap on any DPMO export
    //
    // Vieweb 1.6 truncated exports above 250 rows/columns. Nieweb must
    // survive an arbitrarily wide DPMO table — we seed 300 distinct
    // reference designators (each with one defect) and verify that both
    // CSV and XLSX round-trip the full row set. See docs/phase-2.md §2.3
    // and §7.2 TR3.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a fake source that has one defective tested-object per
    /// distinct reference designator (<c>Ref-0001</c>..<c>Ref-{count}</c>)
    /// so a group-by-reference-designator DPMO table produces exactly
    /// <paramref name="count"/> rows.
    /// </summary>
    private static FakeAoiSource SeedWide(int count)
    {
        var objs = new List<TestedObjectRow>(count);
        for (var i = 1; i <= count; i++)
        {
            objs.Add(Obj(
                machineId: 10,
                date: WindowStartEpoch + i,
                objectId: i,
                objectTypeId: ComponentType,
                errorTable: BitObjectMissing,
                errorTableAr: BitObjectMissing,
                topology: $"Ref-{i:D4}"));
        }
        return new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = objs,
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };
    }

    [Fact]
    public async Task DpmoCsv_Regression18915_300RowsRoundTripCleanly()
    {
        const int rowCount = 300;
        var fake = SeedWide(rowCount);
        var (authed, factory) = await AuthedClientAsync("dpmo-csv-18915@nieweb.test", fake);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=reference-designator&numerator=aoi", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries);

        // Expected: 1 header + 1 OVERALL row + 300 group rows.
        Assert.Equal(1 + 1 + rowCount, lines.Length);

        // Every Ref-0001..Ref-0300 must appear exactly once (order is
        // by DPMO desc + GroupKey; every row has DPMO=1e6 so the tie
        // break falls to GroupKey ordinal). Assert set-equality to
        // stay agnostic of ordering.
        var refs = lines
            .Skip(2)
            .Select(l => l.Split(',', StringSplitOptions.None))
            .Select(f => f[7]) // GroupKey column
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(rowCount, refs.Count);
        for (var i = 1; i <= rowCount; i++)
        {
            Assert.Contains($"Ref-{i:D4}", refs);
        }

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task DpmoXlsx_Regression18915_300RowsRoundTripCleanly()
    {
        const int rowCount = 300;
        var fake = SeedWide(rowCount);
        var (authed, factory) = await AuthedClientAsync("dpmo-xlsx-18915@nieweb.test", fake);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=reference-designator&numerator=aoi", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);

        var rows = workbook.Worksheet("Rows");
        // Header on row 1, data rows 2..(rowCount+1).
        var lastRow = rows.LastRowUsed()?.RowNumber() ?? 0;
        Assert.Equal(rowCount + 1, lastRow);

        // Set-equality on the GroupKey column (column 1), same
        // reasoning as the CSV test.
        var refs = new HashSet<string>(StringComparer.Ordinal);
        for (var r = 2; r <= lastRow; r++)
        {
            refs.Add(rows.Cell(r, 1).GetString());
        }
        Assert.Equal(rowCount, refs.Count);
        for (var i = 1; i <= rowCount; i++)
        {
            Assert.Contains($"Ref-{i:D4}", refs);
        }

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // PDF export endpoints (TR3 — one smoke per report type)
    // -------------------------------------------------------------------------

    private const string PdfContentType = "application/pdf";

    private static void AssertLooksLikePdf(byte[] bytes)
    {
        Assert.True(bytes.Length > 500, $"PDF payload too small: {bytes.Length} bytes.");
        // "%PDF-" magic header.
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
        Assert.Equal((byte)'-', bytes[4]);
        // "%%EOF" trailer somewhere in the last 1 KB.
        var tail = System.Text.Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(bytes.Length, 1024));
        Assert.Contains("%%EOF", tail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DpmoPdf_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.pdf?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DpmoPdf_HappyPath_ReturnsValidPdf()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 60, ComponentType, BitObjectMissing | BitPolarityError, BitObjectMissing | BitPolarityError),
                Obj(10, WindowStartEpoch + 61, ComponentType, 0, 0),
                Obj(11, WindowStartEpoch + 70, ComponentType, BitObjectMissing, BitObjectMissing),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("dpmo-pdf-happy@nieweb.test", fake);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table/export.pdf?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine&numerator=aoi", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PdfContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "dpmo-postreflow-AoiMachine-20260101-20260102.pdf",
            response.Content.Headers.ContentDisposition?.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        AssertLooksLikePdf(bytes);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task ParetoPdf_HappyPath_ReturnsValidPdf()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 1, 1, ComponentType, BitObjectMissing, BitObjectMissing, topology: "R1"),
                Obj(10, WindowStartEpoch + 2, 2, ComponentType, BitPolarityError, BitPolarityError, topology: "R2"),
                Obj(10, WindowStartEpoch + 3, 3, ComponentType, 0, 0, topology: "R3"),
            ],
            SeededMachines = [new Machine(10, 2, "AOI-10", "AOI")],
        };

        var (authed, factory) = await AuthedClientAsync("pareto-pdf-happy@nieweb.test", fake);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/pareto/export.pdf?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=defect&numerator=aoi", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PdfContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "pareto-postreflow-Defect-20260101-20260102.pdf",
            response.Content.Headers.ContentDisposition?.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        AssertLooksLikePdf(bytes);

        authed.Dispose();
        await factory!.DisposeAsync();
    }
}
