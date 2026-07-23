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
/// End-to-end HTTP tests for the Trend-chart endpoint at
/// <c>GET /api/reports/trend</c> (CR3 of docs/phase-2.md §7.3).
/// Same auth / source-resolution / bucket / metric-slug parsing
/// contract as the other report endpoints; verifies the CSV
/// <c>metrics=</c> parser, bucket validation, Cp/Cpk pre-conditions,
/// and the <c>bucket=shift</c> fallback to <see cref="Api.Shifts.IShifts"/>.
/// </summary>
public sealed class TrendEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public TrendEndpointsTests(NiewebApiFactory factory)
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
        Converters =
        {
            new JsonStringEnumConverter(),
            new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.KebabCaseLower),
        },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static readonly SourceDescriptor _postDescriptor = new(
        "postreflow", "Post-reflow AOI", "5.0",
        Capabilities.PinLevel | Capabilities.IsLastInspectionFilter | Capabilities.BarcodeProductView);

    private const string StartUtc = "2026-02-01T00:00:00Z";
    private const string EndUtc = "2026-02-02T00:00:00Z";
    private const int WindowStartEpoch = 1_769_904_000;

    private const int ComponentType = 0x01;

    // -------------------------------------------------------------------------

    [Fact]
    public async Task Trend_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/trend?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&metrics=panel-count", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Trend_UnknownSource_Returns404()
    {
        var (authed, _) = await AuthedClientAsync("trend-unknown@nieweb.test");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/trend?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&metrics=panel-count", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
    }

    [Fact]
    public async Task Trend_MissingMetrics_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("trend-missing-metrics@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/trend?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&bucket=day", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Trend_UnknownMetric_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("trend-bad-metric@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/trend?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&metrics=bogus", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Trend_UnknownBucket_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("trend-bad-bucket@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/trend?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&bucket=fortnight&metrics=panel-count", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Trend_ShiftBucket_Without_Config_Or_Query_Returns400()
    {
        // No shifts= query parameter and (by default) no configured
        // site shift cycle → 400.
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("trend-no-shifts@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/trend?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&bucket=shift&metrics=panel-count", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Trend_Cp_Without_DeviationAxis_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("trend-cp-no-axis@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/trend?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&metrics=cp", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Trend_HappyPath_Returns_Buckets_And_Series()
    {
        // Seed 3 objects (1 defective) in bucket 0 → FpyAoi is a panel
        // metric so we also seed 3 panels. Use panel-count + dpmo-real
        // for a two-series shape.
        var panels = new[]
        {
            Panel(1, WindowStartEpoch + 3600, status: 1),
            Panel(2, WindowStartEpoch + 3700, status: 1),
            Panel(3, WindowStartEpoch + 3800, status: -1),
        };
        var rows = new[]
        {
            Obj(1, WindowStartEpoch + 3600, ComponentType, errorTable: 0, errorTableAr: 0),
            Obj(2, WindowStartEpoch + 3700, ComponentType, errorTable: 0, errorTableAr: 0),
            Obj(3, WindowStartEpoch + 3800, ComponentType, errorTable: 0b1, errorTableAr: 0b1),
        };
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels = panels,
            SeededTestedObjects = rows,
        };
        var (authed, factory) = await AuthedClientAsync("trend-happy@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/trend?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&metrics=panel-count,dpmo-real&opportunity=components", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TrendResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Series.Count);
        Assert.Single(payload.Buckets);
        var bucket = payload.Buckets[0];
        Assert.Equal(3d, bucket.Values[TrendMetric.PanelCount]);
        // DpmoReal = 1e6 * 1 / 3 = 333_333.33
        Assert.NotNull(bucket.Values[TrendMetric.DpmoReal]);
        Assert.Equal(1_000_000d / 3d, bucket.Values[TrendMetric.DpmoReal]!.Value, precision: 6);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Trend_Cp_Cpk_HappyPath_With_Tolerances()
    {
        // 5 delta-X samples: -2,-1,0,1,2 → stddev = sqrt(2.5).
        // Tolerance ±3 → Cp = 6/(6*sqrt(2.5)).
        var rows = new[] { -2.0, -1.0, 0.0, 1.0, 2.0 }
            .Select((v, i) => Obj(i + 1, WindowStartEpoch + 3600 + i, ComponentType,
                errorTable: 0, errorTableAr: 0, dxUm: v))
            .ToArray();
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededTestedObjects = rows,
        };
        var (authed, factory) = await AuthedClientAsync("trend-cp-happy@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/trend?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&bucket=day&metrics=cp,cpk&opportunity=components&deviationAxis=delta-x&lowerTolerance=-3&upperTolerance=3", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TrendResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Single(payload!.Buckets);
        var expectedCp = 6d / (6d * Math.Sqrt(2.5));
        Assert.Equal(expectedCp, payload.Buckets[0].Values[TrendMetric.Cp]!.Value, precision: 10);
        Assert.Equal(expectedCp, payload.Buckets[0].Values[TrendMetric.Cpk]!.Value, precision: 10);

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

    private static PanelRow Panel(int panelId, int date, int status)
        => new(
            PanelId: panelId,
            MachineId: 10,
            LaneNumber: 1,
            PanelBarCode: "BC" + panelId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PanelNumericDate: date,
            NbOfValidCards: 4,
            TestTime: 12.0,
            PanelStatus: status,
            AnomalyBr: 0,
            AnomalyAr: 0,
            HasBeenReviewed: false,
            NbOfTestedObject: 100,
            NbOfErrorObject: status == -1 ? 1 : 0,
            OperatorId: null,
            ProductId: 100,
            RecipeId: 1000);

    private static TestedObjectRow Obj(
        int objectId,
        int date,
        int objectTypeId,
        long errorTable = 0,
        long errorTableAr = 0,
        double? dxUm = null)
        => new(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: objectId,
            ObjectTypeId: objectTypeId,
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: 10,
            ProductId: 100,
            PanelNumericDate: date,
            Topology: "R" + objectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PartNumberName: null,
            JedecName: null,
            DeltaXUm: dxUm);
}
