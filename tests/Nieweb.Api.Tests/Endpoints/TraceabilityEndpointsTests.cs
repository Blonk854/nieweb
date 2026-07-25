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
using Nieweb.Reports.Traceability;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// TC1 — endpoint contract tests for
/// <see cref="TraceabilityEndpoints"/>. Auth, source resolution,
/// 400/404 shapes, and pin-availability signalling are all exercised
/// against an in-memory <see cref="FakeAoiSource"/> (which is
/// itself an <see cref="IPinLevelSource"/> in the Api.Tests project).
/// </summary>
public sealed class TraceabilityEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public TraceabilityEndpointsTests(NiewebApiFactory factory)
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
        Assert.True(result.Succeeded);
    }

    private static readonly JsonSerializerOptions _responseJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // ---- Test data ---------------------------------------------------------
    private const int PanelId = 500;
    // 2026-06-05 12:00:00 UTC.
    private const int PanelEpoch = 1_780_660_800;

    private static readonly PanelRow SeededPanel = new(
        PanelId: PanelId,
        MachineId: 1,
        LaneNumber: 1,
        PanelBarCode: "BC-500",
        PanelNumericDate: PanelEpoch,
        NbOfValidCards: 1,
        TestTime: 5.0,
        PanelStatus: 0,
        AnomalyBr: 0,
        AnomalyAr: 0,
        HasBeenReviewed: false,
        NbOfTestedObject: 1,
        NbOfErrorObject: 0,
        OperatorId: null,
        ProductId: 1,
        RecipeId: 1);

    private static readonly CardRow SeededCard = new(
        PanelId: PanelId,
        CardIdOnPanel: 1,
        CardStatus: 0,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: 1,
        NbOfErrorObject: 0,
        MachineId: 1,
        ProductId: 1,
        PanelNumericDate: PanelEpoch);

    private static readonly TestedObjectRow SeededObject = new(
        PanelId: PanelId,
        CardIdOnPanel: 1,
        ObjectId: 77,
        ObjectTypeId: 1,
        ErrorTable: 0,
        ErrorTableAr: 0,
        Status: 0,
        MachineId: 1,
        ProductId: 1,
        PanelNumericDate: PanelEpoch,
        Topology: "R1",
        PartNumberName: "RES-10K",
        JedecName: "0603");

    private static readonly PinRow SeededPin = new(
        PinId: 1000,
        TestedObjectId: 77,
        ComponentSide: 0,
        PinIndexOnSide: 0,
        IpcPinNb: 1,
        ErrorTable: 0,
        ErrorTableAr: 0,
        ReviewSanction: 0);

    private static FakeAoiSource NewFake() => new(
        new SourceDescriptor("postreflow", "Post-reflow AOI", "5.0", Capabilities.PinLevel))
    {
        SeededPanels = [SeededPanel],
        SeededCards = [SeededCard],
        SeededTestedObjects = [SeededObject],
        SeededPins = [SeededPin],
    };

    private WebApplicationFactory<Program> WithFake(FakeAoiSource source)
        => _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IAoiSource>(source)));

    private async Task<HttpClient> AuthedClientAsync(WebApplicationFactory<Program> factory, string email)
    {
        using var anon = factory.CreateClient();
        var token = await IssueTokenAsync(anon, email);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ---- Auth / not-found guards ------------------------------------------

    [Fact]
    public async Task PanelById_WithoutToken_Returns401()
    {
        await using var factory = WithFake(NewFake());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/by-id/{PanelId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PanelById_UnknownSource_Returns404()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-unknown-src@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/nope/by-id/{PanelId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PanelById_UnknownPanel_Returns404()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-unknown-panel@nieweb.test");
        using var response = await client.GetAsync(
            new Uri("/api/traceability/panels/postreflow/by-id/999999", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Happy paths -------------------------------------------------------

    [Fact]
    public async Task PanelById_HappyPath_ReturnsPanelAndUtc()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-panel-id@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/by-id/{PanelId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TraceabilityPanel>(_responseJson);
        Assert.NotNull(body);
        Assert.Equal(PanelId, body!.Panel.PanelId);
        Assert.Equal(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc), body.PanelUtc);
    }

    [Fact]
    public async Task PanelByBarcode_HappyPath_ReturnsMostRecentInspection()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-panel-barcode@nieweb.test");
        using var response = await client.GetAsync(
            new Uri("/api/traceability/panels/postreflow/by-barcode?barcode=BC-500", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TraceabilityPanel>(_responseJson);
        Assert.NotNull(body);
        Assert.Equal("BC-500", body!.Panel.PanelBarCode);
    }

    [Fact]
    public async Task PanelByBarcode_MissingParam_Returns400()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-barcode-missing@nieweb.test");
        using var response = await client.GetAsync(
            new Uri("/api/traceability/panels/postreflow/by-barcode", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Subpanels_HappyPath_ReturnsPanelAndCards()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-subpanels@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/{PanelId}/subpanels", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubpanelsResponse>(_responseJson);
        Assert.NotNull(body);
        Assert.Equal(PanelId, body!.Panel.Panel.PanelId);
        Assert.Single(body.Cards);
    }

    [Fact]
    public async Task Objects_HappyPath_ReturnsBreadcrumbAndObjects()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-objects@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/{PanelId}/subpanels/1/objects", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TestedObjectsResponse>(_responseJson);
        Assert.NotNull(body);
        Assert.Single(body!.Objects);
        Assert.Equal(77, body.Objects[0].ObjectId);
    }

    [Fact]
    public async Task ObjectDetail_WithPinLevelSource_ReturnsPinsAvailableTrue()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-object-pins@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/{PanelId}/subpanels/1/objects/77", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TraceabilityTestedObject>(_responseJson);
        Assert.NotNull(body);
        Assert.True(body!.PinsAvailable);
        Assert.Single(body.Pins);
        Assert.Equal(1000L, body.Pins[0].PinId);
    }

    [Fact]
    public async Task ObjectDetail_UnknownObject_Returns404()
    {
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc1-object-unknown@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/{PanelId}/subpanels/1/objects/999", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ==================================================================
    // TC2 — cross-DB board trace by barcode.
    // ==================================================================

    private static FakeAoiSource NewPreReflowFake(string barcode = "BC-500")
    {
        var panel = SeededPanel with { PanelId = 900, PanelBarCode = barcode };
        var card = SeededCard with { PanelId = 900 };
        return new FakeAoiSource(
            new SourceDescriptor("prereflow", "Pre-reflow AOI", "4.3.1",
                Capabilities.PastePrintMetrics))
        {
            SeededPanels = [panel],
            SeededCards = [card],
        };
    }

    private WebApplicationFactory<Program> WithTwoFakes(FakeAoiSource post, FakeAoiSource pre)
        => _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IAoiSource>(post);
            s.AddSingleton<IAoiSource>(new PinlessAoiSource(pre));
        }));

    /// <summary>
    /// Wrapper that hides <see cref="IPinLevelSource"/> from the
    /// underlying fake so a TC2 test can prove
    /// <c>PinsAvailable = false</c> is emitted correctly for
    /// pre-reflow-shaped sources. The Api.Tests
    /// <see cref="FakeAoiSource"/> unconditionally implements
    /// <see cref="IPinLevelSource"/>, so we need this shim to model
    /// the v4.3.1 pre-reflow catalogue.
    /// </summary>
    private sealed class PinlessAoiSource : IAoiSource
    {
        private readonly IAoiSource _inner;
        public PinlessAoiSource(IAoiSource inner) { _inner = inner; }
        public SourceDescriptor Descriptor => _inner.Descriptor;
        public Task<DateTime?> GetLatestPanelUtcAsync(CancellationToken ct) => _inner.GetLatestPanelUtcAsync(ct);
        public Task<Page<PanelRow, PanelCursor>> QueryPanelsAsync(PanelQuery q, CancellationToken ct) => _inner.QueryPanelsAsync(q, ct);
        public Task<Page<CardRow, CardCursor>> QueryCardsAsync(CardQuery q, CancellationToken ct) => _inner.QueryCardsAsync(q, ct);
        public Task<Page<TestedObjectRow, TestedObjectCursor>> QueryTestedObjectsAsync(TestedObjectQuery q, CancellationToken ct) => _inner.QueryTestedObjectsAsync(q, ct);
        public IAsyncEnumerable<PanelRow> StreamPanelsAsync(PanelQuery q, CancellationToken ct) => _inner.StreamPanelsAsync(q, ct);
        public IAsyncEnumerable<CardRow> StreamCardsAsync(CardQuery q, CancellationToken ct) => _inner.StreamCardsAsync(q, ct);
        public IAsyncEnumerable<TestedObjectRow> StreamTestedObjectsAsync(TestedObjectQuery q, CancellationToken ct) => _inner.StreamTestedObjectsAsync(q, ct);
        public Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken ct) => _inner.ListMachinesAsync(ct);
        public Task<IReadOnlyList<ReviewOperator>> ListOperatorsAsync(CancellationToken ct) => _inner.ListOperatorsAsync(ct);
        public Task<IReadOnlyList<Product>> ListProductsAsync(CancellationToken ct) => _inner.ListProductsAsync(ct);
        public Task<IReadOnlyList<Recipe>> ListRecipesAsync(CancellationToken ct) => _inner.ListRecipesAsync(ct);
        public Task<PanelRow?> GetPanelByIdAsync(int panelId, CancellationToken ct) => _inner.GetPanelByIdAsync(panelId, ct);
        public Task<PanelRow?> GetPanelByBarcodeAsync(string barcode, CancellationToken ct) => _inner.GetPanelByBarcodeAsync(barcode, ct);
        public Task<IReadOnlyList<CardRow>> ListCardsForPanelAsync(long panelId, CancellationToken ct) => _inner.ListCardsForPanelAsync(panelId, ct);
        public Task<IReadOnlyList<TestedObjectRow>> ListTestedObjectsForSubpanelAsync(
            long panelId, int cardIdOnPanel, CancellationToken ct)
            => _inner.ListTestedObjectsForSubpanelAsync(panelId, cardIdOnPanel, ct);
    }

    [Fact]
    public async Task BoardByBarcode_WithoutToken_Returns401()
    {
        await using var factory = WithTwoFakes(NewFake(), NewPreReflowFake());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri("/api/traceability/boards/by-barcode?barcode=BC-500", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BoardByBarcode_MissingBarcode_Returns400()
    {
        await using var factory = WithTwoFakes(NewFake(), NewPreReflowFake());
        using var client = await AuthedClientAsync(factory, "tc2-missing@nieweb.test");
        using var response = await client.GetAsync(
            new Uri("/api/traceability/boards/by-barcode", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BoardByBarcode_OversizedBarcode_Returns400()
    {
        await using var factory = WithTwoFakes(NewFake(), NewPreReflowFake());
        using var client = await AuthedClientAsync(factory, "tc2-oversized@nieweb.test");
        var oversized = new string('X', 65);
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/boards/by-barcode?barcode={oversized}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BoardByBarcode_UnknownEverywhere_Returns404()
    {
        await using var factory = WithTwoFakes(NewFake(), NewPreReflowFake());
        using var client = await AuthedClientAsync(factory, "tc2-unknown@nieweb.test");
        using var response = await client.GetAsync(
            new Uri("/api/traceability/boards/by-barcode?barcode=NOPE-000", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BoardByBarcode_MatchesBothSources_ReturnsBothStagesPopulated()
    {
        await using var factory = WithTwoFakes(NewFake(), NewPreReflowFake());
        using var client = await AuthedClientAsync(factory, "tc2-both@nieweb.test");
        using var response = await client.GetAsync(
            new Uri("/api/traceability/boards/by-barcode?barcode=BC-500", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BoardTrace>(_responseJson);
        Assert.NotNull(body);
        Assert.Equal("BC-500", body!.Barcode);
        Assert.Equal(2, body.Stages.Count);

        var post = body.Stages.Single(s => s.SourceId == "postreflow");
        Assert.NotEmpty(post.Sides);
        Assert.Equal(PanelId, post.Sides[0].Panel.Panel.PanelId);
        Assert.True(post.PinsAvailable);
        Assert.Single(post.Sides[0].Cards);
        Assert.Null(post.Error);

        var pre = body.Stages.Single(s => s.SourceId == "prereflow");
        Assert.NotEmpty(pre.Sides);
        Assert.Equal(900, pre.Sides[0].Panel.Panel.PanelId);
        Assert.False(pre.PinsAvailable);
        Assert.Single(pre.Sides[0].Cards);
        Assert.Null(pre.Error);
    }

    [Fact]
    public async Task BoardByBarcode_MissingFromOneSource_ReturnsNullPanelForThatStage()
    {
        // Post-reflow has BC-500; pre-reflow only has BC-OTHER.
        await using var factory = WithTwoFakes(NewFake(), NewPreReflowFake("BC-OTHER"));
        using var client = await AuthedClientAsync(factory, "tc2-onemissing@nieweb.test");
        using var response = await client.GetAsync(
            new Uri("/api/traceability/boards/by-barcode?barcode=BC-500", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BoardTrace>(_responseJson);
        Assert.NotNull(body);
        Assert.Equal(2, body!.Stages.Count);

        var post = body.Stages.Single(s => s.SourceId == "postreflow");
        Assert.NotEmpty(post.Sides);
        Assert.Single(post.Sides[0].Cards);

        var pre = body.Stages.Single(s => s.SourceId == "prereflow");
        Assert.Empty(pre.Sides);
        Assert.Null(pre.Error);
    }

    // ==================================================================
    // TC5 Phase C — failed-objects-for-panel drill-down.
    // ==================================================================

    private static FakeAoiSource NewFakeWithFailures()
    {
        // Two subpanels: one clean (skipped by the DIM fallback), one
        // with a mix of pass / false-call / failure rows so the
        // endpoint has non-trivial output.
        var cleanCard = SeededCard with { CardIdOnPanel = 2, NbOfErrorObject = 0 };
        var failingCard = SeededCard with { NbOfErrorObject = 2 };
        var passing = SeededObject with { ObjectId = 77 };
        var falseCall = SeededObject with { ObjectId = 78, ErrorTable = 4, ErrorTableAr = 0 };
        var failedA = SeededObject with { ObjectId = 79, ErrorTable = 8, ErrorTableAr = 8 };
        var failedB = SeededObject with { ObjectId = 80, ErrorTable = 16, ErrorTableAr = 16 };
        return new FakeAoiSource(
            new SourceDescriptor("postreflow", "Post-reflow AOI", "5.0", Capabilities.PinLevel))
        {
            SeededPanels = [SeededPanel with { NbOfErrorObject = 2 }],
            SeededCards = [failingCard, cleanCard],
            SeededTestedObjects = [passing, falseCall, failedA, failedB],
        };
    }

    [Fact]
    public async Task FailedObjects_WithoutToken_Returns401()
    {
        await using var factory = WithFake(NewFakeWithFailures());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/{PanelId}/failed-objects", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FailedObjects_UnknownSource_Returns404()
    {
        await using var factory = WithFake(NewFakeWithFailures());
        using var client = await AuthedClientAsync(factory, "tc5c-unknown-src@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/nope/{PanelId}/failed-objects", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FailedObjects_UnknownPanel_Returns404()
    {
        await using var factory = WithFake(NewFakeWithFailures());
        using var client = await AuthedClientAsync(factory, "tc5c-unknown-panel@nieweb.test");
        using var response = await client.GetAsync(
            new Uri("/api/traceability/panels/postreflow/999999/failed-objects", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FailedObjects_HappyPath_ReturnsPanelAndFilteredObjects()
    {
        await using var factory = WithFake(NewFakeWithFailures());
        using var client = await AuthedClientAsync(factory, "tc5c-happy@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/{PanelId}/failed-objects", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<FailedObjectsResponse>(_responseJson);
        Assert.NotNull(body);
        Assert.Equal(PanelId, body!.Panel.Panel.PanelId);
        Assert.Equal(2, body.Objects.Count);
        // Both returned rows carry post-review defects (ErrorTableAr != 0).
        Assert.All(body.Objects, o => Assert.NotEqual(0, o.ErrorTableAr));
        Assert.Equal(79, body.Objects[0].ObjectId);
        Assert.Equal(80, body.Objects[1].ObjectId);
    }

    [Fact]
    public async Task FailedObjects_PanelWithoutFailures_ReturnsEmptyList()
    {
        // NewFake() seeds a panel + one clean card + one passing
        // tested-object. The endpoint must still 200 with an empty
        // list so the SPA can render "no failures" against the panel
        // breadcrumb.
        await using var factory = WithFake(NewFake());
        using var client = await AuthedClientAsync(factory, "tc5c-clean@nieweb.test");
        using var response = await client.GetAsync(
            new Uri($"/api/traceability/panels/postreflow/{PanelId}/failed-objects", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<FailedObjectsResponse>(_responseJson);
        Assert.NotNull(body);
        Assert.Equal(PanelId, body!.Panel.Panel.PanelId);
        Assert.Empty(body.Objects);
    }
}
