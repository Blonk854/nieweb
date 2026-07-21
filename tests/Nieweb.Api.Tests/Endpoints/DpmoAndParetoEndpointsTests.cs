using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

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
}
