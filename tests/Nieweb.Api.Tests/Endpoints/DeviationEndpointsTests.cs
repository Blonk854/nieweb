using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Parameters;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data.Entities;
using Nieweb.DataSources;
using Nieweb.Reports;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// End-to-end HTTP tests for the Deviation-chart endpoint at
/// <c>GET /api/reports/deviation</c> (CR2 of docs/phase-2.md §7.3).
/// Same authentication / source-resolution contract as the other
/// report endpoints; verifies axis mapping, opportunity filtering,
/// tolerance auto-resolution from <see cref="IAppParameters"/>, and
/// out-of-tolerance counting through the wire.
/// </summary>
public sealed class DeviationEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public DeviationEndpointsTests(NiewebApiFactory factory)
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
    private const int PasteType = 0x10;

    // -------------------------------------------------------------------------

    [Fact]
    public async Task Deviation_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=delta-x", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deviation_UnknownSource_Returns404()
    {
        var (authed, _) = await AuthedClientAsync("dev-unknown@nieweb.test");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}&axis=delta-x", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
    }

    [Fact]
    public async Task Deviation_MissingAxis_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("dev-missing-axis@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Deviation_UnknownAxis_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("dev-bad-axis@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=bogus", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Deviation_BinCountOutOfRange_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("dev-bad-bins@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=delta-x&binCount=0", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Deviation_Components_ReturnsHistogramMeanAndStdDev()
    {
        // 5 component rows with delta-X = -2, -1, 0, 1, 2 µm → mean 0,
        // sample stddev ≈ 1.58114.
        var rows = new[] { -2.0, -1.0, 0.0, 1.0, 2.0 }
            .Select((v, i) => Obj(objectId: i, objectTypeId: ComponentType, dxUm: v))
            .ToArray();
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = rows,
        };
        var (authed, factory) = await AuthedClientAsync("dev-happy@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=delta-x&binCount=5", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DeviationResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(DeviationAxis.DeltaX, payload!.Axis);
        Assert.Equal(DpmoOpportunity.Components, payload.Opportunity);
        Assert.Equal(5L, payload.SampleCount);
        Assert.Equal(0.0, payload.Mean, precision: 10);
        Assert.Equal(Math.Sqrt(2.5), payload.StdDev, precision: 10);
        // Tolerance may or may not be present depending on whether an
        // AppParameter row exists for tolerance.component.itx (other
        // tests in this class seed it and the SQLite fixture is
        // shared). We assert only that the histogram is populated.
        Assert.Equal(5, payload.Bins.Count);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Deviation_Opportunity_Filters_Paste_Out()
    {
        // 3 components (dx=1,2,3) + 3 paste rows (dx=99,99,99). Component
        // run must ignore paste entirely → mean 2.
        var rows = new List<TestedObjectRow>();
        for (var i = 0; i < 3; i++)
        {
            rows.Add(Obj(objectId: i, objectTypeId: ComponentType, dxUm: i + 1));
        }
        for (var i = 0; i < 3; i++)
        {
            rows.Add(Obj(objectId: 100 + i, objectTypeId: PasteType, dxUm: 99.0));
        }
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = rows,
        };
        var (authed, factory) = await AuthedClientAsync("dev-opp@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=delta-x&opportunity=components", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DeviationResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(3L, payload!.SampleCount);
        Assert.Equal(2.0, payload.Mean, precision: 10);
    }

    [Fact]
    public async Task Deviation_ExplicitTolerance_CountsOutOfTolerance()
    {
        // Rows at -10, -5, 0, 5, 10. Explicit tolerance ±6 → 2 out.
        var rows = new[] { -10.0, -5.0, 0.0, 5.0, 10.0 }
            .Select((v, i) => Obj(objectId: i, objectTypeId: ComponentType, dxUm: v))
            .ToArray();
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = rows,
        };
        var (authed, factory) = await AuthedClientAsync("dev-explicit-tol@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=delta-x&lowerTolerance=-6&upperTolerance=6", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DeviationResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(-6.0, payload!.LowerTolerance);
        Assert.Equal(6.0, payload.UpperTolerance);
        Assert.Equal(2L, payload.OutOfToleranceCount);
    }

    private static readonly double[] _autoToleranceSample = [5.0, 15.0, 30.0];

    [Fact]
    public async Task Deviation_TolerancesResolvedFromAppParameters_WhenNoneSupplied()
    {
        // Seed tolerance.component.itx = 0.020 mm → symmetric ±10 µm envelope.
        // Then 3 rows at deltaX = 5, 15, 30 → 2 out of tolerance (15 and 30).
        var rows = _autoToleranceSample
            .Select((v, i) => Obj(objectId: i, objectTypeId: ComponentType, dxUm: v))
            .ToArray();
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = rows,
        };

        var (authed, factory) = await AuthedClientAsync("dev-auto-tol@nieweb.test", fake);
        // Seed the tolerance parameter via IAppParameters in the same
        // hosting process. Value 0.020 = 20 µm interval → ±10 µm.
        using (var scope = factory!.Services.CreateScope())
        {
            var parameters = scope.ServiceProvider.GetRequiredService<IAppParameters>();
            await parameters.EnsureSeededAsync();
            await parameters.UpsertAsync(
                key: "tolerance.component.itx",
                valueType: AppParameterValueTypes.Decimal,
                value: "0.020",
                description: null,
                cancellationToken: CancellationToken.None);
        }

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis=delta-x", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DeviationResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(-10.0, payload!.LowerTolerance);
        Assert.Equal(10.0, payload.UpperTolerance);
        Assert.Equal(2L, payload.OutOfToleranceCount);

        authed.Dispose();
        await factory.DisposeAsync();
    }

    private static readonly (string Slug, double ExpectedMean)[] _allAxes =
    [
        ("delta-x", 1.0),
        ("delta-y", 2.0),
        ("delta-theta", 3.0),
        ("delta-thickness", 4.0),
        ("delta-surface", 5.0),
    ];

    [Fact]
    public async Task Deviation_AllAxesAccepted()
    {
        var row = Obj(objectId: 1, objectTypeId: ComponentType,
            dxUm: 1.0, dyUm: 2.0, dthetaDeg: 3.0, dzUm: 4.0, dsRatio: 5.0);
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = new[] { row },
        };
        var (authed, factory) = await AuthedClientAsync("dev-all-axes@nieweb.test", fake);
        foreach (var (slug, expectedMean) in _allAxes)
        {
            using var response = await authed.GetAsync(
                new Uri($"/api/reports/deviation?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&axis={slug}&binCount=1", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<DeviationResult>(_responseJson);
            Assert.NotNull(payload);
            Assert.Equal(1L, payload!.SampleCount);
            Assert.Equal(expectedMean, payload.Mean, precision: 10);
        }
        authed.Dispose();
        await factory!.DisposeAsync();
    }

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

    private static TestedObjectRow Obj(
        int objectId,
        int objectTypeId,
        double? dxUm = null,
        double? dyUm = null,
        double? dthetaDeg = null,
        double? dzUm = null,
        double? dsRatio = null)
        => new(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: objectId,
            ObjectTypeId: objectTypeId,
            ErrorTable: 0,
            ErrorTableAr: 0,
            Status: 0,
            MachineId: 10,
            ProductId: 100,
            PanelNumericDate: WindowStartEpoch + 60 + objectId,
            Topology: "R" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PartNumberName: null,
            JedecName: null,
            DeltaXUm: dxUm,
            DeltaYUm: dyUm,
            DeltaThetaDeg: dthetaDeg,
            DeltaThicknessUm: dzUm,
            DeltaSurface: dsRatio);
}
