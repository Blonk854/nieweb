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
/// HTTP tests for the skip-exclusion toggle exposed on the FPY endpoint
/// (<c>GET /api/reports/fpy-table</c>, newly added) and the DPMO endpoint
/// (<c>?skipExclusion=clean</c>).
/// </summary>
public sealed class SkipToggleEndpointTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public SkipToggleEndpointTests(NiewebApiFactory factory)
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
    private const long ObjectMissing = 1L;

    // A panel with one real good board and one X-OUT'd empty board.
    private static FakeAoiSource BuildMixedFake()
    {
        var tos = new List<TestedObjectRow>
        {
            To(1, 1, ObjectMissing, objId: 1), // card 1's single real defect
        };
        for (var i = 0; i < 50; i++)
        {
            tos.Add(To(1, 2, ObjectMissing, objId: 100 + i, repairButton: i == 0 ? "X-OUT" : null));
        }
        return new FakeAoiSource(_postDescriptor)
        {
            SeededPanels = [Panel(1, reviewed: true)],
            SeededCards =
            [
                Card(1, 1, cardStatus: 1, comp: 100),
                Card(1, 2, cardStatus: -2, comp: 100),
            ],
            SeededTestedObjects = tos,
        };
    }

    [Fact]
    public async Task Fpy_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/fpy-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Fpy_UnknownSource_Returns404()
    {
        var (authed, _) = await AuthedClientAsync("fpy-unknown@nieweb.test");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/fpy-table?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
    }

    [Fact]
    public async Task Fpy_Board_RawVsClean_ExcludesSkippedBoard()
    {
        var (authed, factory) = await AuthedClientAsync("fpy-clean@nieweb.test", BuildMixedFake());

        var raw = await GetAsync<FpyTableResult>(authed,
            $"/api/reports/fpy-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&granularity=board");
        var clean = await GetAsync<FpyTableResult>(authed,
            $"/api/reports/fpy-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&granularity=board&skipExclusion=clean");

        // Raw: two boards, one faulty → FPY 50%.
        Assert.Equal(FpyGranularity.Board, raw.Granularity);
        Assert.Equal(50d, raw.Overall.FpyAoiPercent);
        Assert.Equal(0L, raw.SkipExcludedRows);

        // Clean: the X-OUT board drops out → FPY 100%.
        Assert.Equal(SkipExclusion.Clean, clean.SkipExclusion);
        Assert.Equal(100d, clean.Overall.FpyAoiPercent);
        Assert.Equal(1L, clean.SkipExcludedRows);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Dpmo_Clean_ExcludesSkippedCards()
    {
        var (authed, factory) = await AuthedClientAsync("dpmo-clean@nieweb.test", BuildMixedFake());

        var clean = await GetAsync<DpmoTableResult>(authed,
            $"/api/reports/dpmo-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine&numerator=aoi&opportunity=components&skipExclusion=clean");

        // The empty board (50 phantom missings) drops out of both halves.
        Assert.Equal(SkipExclusion.Clean, clean.SkipExclusion);
        Assert.Equal(100L, clean.Overall.OpportunityCount);
        Assert.Equal(1L, clean.Overall.DefectBitCount);
        Assert.Equal(10_000d, clean.Overall.DpmoPpm);
        Assert.Equal(1L, clean.SkipExcludedCards);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Dpmo_BadSkipExclusion_Returns400()
    {
        var (authed, factory) = await AuthedClientAsync("dpmo-badskip@nieweb.test", BuildMixedFake());
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/dpmo-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine&skipExclusion=bogus", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Dpmo_SkipStatuses_ManualSkip_KeepsOnlySkippedBoard()
    {
        var (authed, factory) = await AuthedClientAsync("dpmo-status@nieweb.test", BuildMixedFake());

        var manualOnly = await GetAsync<DpmoTableResult>(authed,
            $"/api/reports/dpmo-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&groupBy=aoi-machine&numerator=aoi&opportunity=components&skipStatuses=ManualSkip");

        // Only the X-OUT empty board survives: 100 tests / 50 phantom
        // missings → 500 000 DPMO; the real board is filtered out.
        Assert.Equal(100L, manualOnly.Overall.OpportunityCount);
        Assert.Equal(50L, manualOnly.Overall.DefectBitCount);
        Assert.Equal(500_000d, manualOnly.Overall.DpmoPpm);
        Assert.Equal(1L, manualOnly.SkipExcludedCards);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Fpy_Board_SkipStatuses_None_MatchesClean()
    {
        var (authed, factory) = await AuthedClientAsync("fpy-status@nieweb.test", BuildMixedFake());

        var noneOnly = await GetAsync<FpyTableResult>(authed,
            $"/api/reports/fpy-table?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&granularity=board&skipStatuses=None");

        // None-only keeps just the real good board → FPY 100%, one dropped.
        Assert.Equal(1L, noneOnly.Overall.TotalRows);
        Assert.Equal(100d, noneOnly.Overall.FpyAoiPercent);
        Assert.Equal(1L, noneOnly.SkipExcludedRows);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // ---- helpers ----------------------------------------------------------

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using var response = await client.GetAsync(new Uri(uri, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<T>(_responseJson);
        Assert.NotNull(payload);
        return payload!;
    }

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
        if (await users.FindByEmailAsync(email) is not null)
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

    private static PanelRow Panel(int id, bool reviewed) => new(
        PanelId: id,
        MachineId: 10,
        LaneNumber: 1,
        PanelBarCode: $"BC-{id:D3}",
        PanelNumericDate: WindowStartEpoch + id,
        NbOfValidCards: 2,
        TestTime: 5.0,
        PanelStatus: 1,
        AnomalyBr: 0,
        AnomalyAr: 0,
        HasBeenReviewed: reviewed,
        NbOfTestedObject: 100,
        NbOfErrorObject: 0,
        OperatorId: null,
        ProductId: 500,
        RecipeId: 1);

    private static CardRow Card(int panelId, int cardId, int cardStatus, int comp) => new(
        PanelId: panelId,
        CardIdOnPanel: cardId,
        CardStatus: cardStatus,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: comp,
        NbOfErrorObject: 0,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: WindowStartEpoch + panelId,
        NbOfTestsOnComp: comp);

    private static TestedObjectRow To(int panel, int card, long errorTable, int objId, string? repairButton = null) => new(
        PanelId: panel,
        CardIdOnPanel: card,
        ObjectId: objId,
        ObjectTypeId: ComponentType,
        ErrorTable: errorTable,
        ErrorTableAr: errorTable,
        Status: errorTable == 0 ? 0 : 1,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: WindowStartEpoch + panel,
        Topology: null,
        PartNumberName: null,
        JedecName: null,
        RepairButtonComment: repairButton);
}
