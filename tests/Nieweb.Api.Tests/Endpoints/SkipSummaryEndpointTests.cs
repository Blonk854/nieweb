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
using Nieweb.Reports.Common.Skips;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// End-to-end HTTP tests for <c>GET /api/reports/skip-summary</c>: the
/// standard auth / source-resolution / window-validation contract plus a
/// happy-path scenario proving the skip classification survives the wire.
/// </summary>
public sealed class SkipSummaryEndpointTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public SkipSummaryEndpointTests(NiewebApiFactory factory)
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

    [Fact]
    public async Task SkipSummary_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/skip-summary?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SkipSummary_UnknownSource_Returns404()
    {
        var (authed, _) = await AuthedClientAsync("skip-unknown@nieweb.test");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/skip-summary?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
    }

    [Fact]
    public async Task SkipSummary_InvalidWindow_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        var (authed, factory) = await AuthedClientAsync("skip-bad-window@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/skip-summary?sourceId=postreflow&startUtc={EndUtc}&endUtc={StartUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task SkipSummary_HappyPath_ReturnsClassifiedDistribution()
    {
        // Panel 1 (reviewed): a normal card + an X-OUT card.
        // Panel 2 (reviewed): a machine-skip card.
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, reviewed: true),
                Panel(2, reviewed: true),
            ],
            SeededCards =
            [
                Card(1, 1, components: 100),                                    // None
                Card(1, 2, components: 100),                                    // ManualSkip
                Card(2, 1, components: 100, anomalyAr: SkipClassifier.MachineSkipBit, cardStatus: 0), // MachineFlagged
            ],
            SeededTestedObjects =
            [
                To(1, 2, repairButton: "X-OUT", objId: 1),
            ],
        };

        var (authed, factory) = await AuthedClientAsync("skip-happy@nieweb.test", fake);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/skip-summary?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SkipSummaryResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal("postreflow", payload!.Source.Id);
        Assert.Equal(3L, payload.TotalCards);
        Assert.Equal(2L, payload.SkippedCards);
        Assert.Equal(4, payload.Classes.Count);

        Assert.Equal(1L, payload.Classes.Single(c => c.Class == SkipClass.None).CardCount);
        Assert.Equal(1L, payload.Classes.Single(c => c.Class == SkipClass.ManualSkip).CardCount);
        Assert.Equal(1L, payload.Classes.Single(c => c.Class == SkipClass.MachineFlagged).CardCount);
        Assert.Equal(0L, payload.Classes.Single(c => c.Class == SkipClass.HeuristicMissing).CardCount);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // ---- helpers ----------------------------------------------------------

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
        NbOfValidCards: 1,
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

    private static CardRow Card(int panelId, int cardId, int components, long anomalyAr = 0, int cardStatus = 1) => new(
        PanelId: panelId,
        CardIdOnPanel: cardId,
        CardStatus: cardStatus,
        AnomalyBr: 0,
        AnomalyAr: anomalyAr,
        NbOfTestedObject: components,
        NbOfErrorObject: 0,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: WindowStartEpoch + panelId);

    private static TestedObjectRow To(int panel, int card, long errorTable = 0, string? repairButton = null, int objId = 0) => new(
        PanelId: panel,
        CardIdOnPanel: card,
        ObjectId: objId,
        ObjectTypeId: 0x01,
        ErrorTable: errorTable,
        ErrorTableAr: 0,
        Status: errorTable == 0 ? 0 : 1,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: WindowStartEpoch + panel,
        Topology: null,
        PartNumberName: null,
        JedecName: null,
        RepairButtonComment: repairButton);
}
