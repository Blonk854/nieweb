using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Parameters;
using Nieweb.Api.Startup;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data;
using Nieweb.Data.Entities;
using Nieweb.DataSources;

using Xunit;

namespace Nieweb.Api.Tests;

/// <summary>
/// HTTP integration tests for <see cref="AdminSkipClassificationEndpoints"/>:
/// admin-only GET / PUT of the skip-classification config, validation,
/// and proof that a threshold change flows into the Skip Summary report.
/// </summary>
public sealed class AdminSkipClassificationEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AdminSkipClassificationEndpointsTests(NiewebApiFactory factory)
    {
        _factory = factory;
        EnsureDatabaseAsync().GetAwaiter().GetResult();
    }

    private const string StartUtc = "2026-01-01T00:00:00Z";
    private const string EndUtc = "2026-01-02T00:00:00Z";
    private const int WindowStartEpoch = 1767225600;
    private const int ComponentType = 0x01;
    private const long ObjectMissing = 1L;

    private static readonly SourceDescriptor _postDescriptor = new(
        "postreflow", "Post-reflow AOI", "5.0",
        Capabilities.PinLevel | Capabilities.IsLastInspectionFilter | Capabilities.BarcodeProductView);

    // ---- tests ------------------------------------------------------------

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var res = await client.GetAsync(new Uri("/api/admin/skip-classification", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Get_AsReader_Returns403()
    {
        await SeedSystemAsync();
        _ = await CreateUserAsync("skip-reader@t.test", "correctpassword123", BootstrapAdmin.RoleReader);
        using var client = await LoggedInClientAsync("skip-reader@t.test", "correctpassword123");
        using var res = await client.GetAsync(new Uri("/api/admin/skip-classification", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Get_AsAdmin_ReturnsSeededDefaults()
    {
        await SeedSystemAsync();
        _ = await CreateUserAsync("skip-admin1@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("skip-admin1@t.test", "correctpassword123");

        var dto = await client.GetFromJsonAsync<ConfigDto>(
            new Uri("/api/admin/skip-classification", UriKind.Relative));

        Assert.NotNull(dto);
        Assert.Equal(0.50, dto!.MissingRatioThreshold);
        Assert.Equal(8, dto.MinComponentFloor);
        Assert.Equal(4, dto.AbsoluteMissingFloor);
        var xout = Assert.Single(dto.RepairButtonMeanings);
        Assert.Equal("X-OUT", xout.Label);
        Assert.Equal("ManualSkip", xout.Meaning);
    }

    [Fact]
    public async Task Put_AsAdmin_UpdatesAndReflects()
    {
        await SeedSystemAsync();
        _ = await CreateUserAsync("skip-admin2@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("skip-admin2@t.test", "correctpassword123");

        var update = new ConfigDto(
            MissingRatioThreshold: 0.7,
            MinComponentFloor: 10,
            AbsoluteMissingFloor: 5,
            RepairButtonMeanings:
            [
                new MeaningDto("X-OUT", "ManualSkip"),
                new MeaningDto("MY_MISSING", "ConfirmedRealMissing"),
            ]);

        using var put = await client.PutAsJsonAsync(
            new Uri("/api/admin/skip-classification", UriKind.Relative), update);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var reread = await client.GetFromJsonAsync<ConfigDto>(
            new Uri("/api/admin/skip-classification", UriKind.Relative));
        Assert.Equal(0.7, reread!.MissingRatioThreshold);
        Assert.Equal(10, reread.MinComponentFloor);
        Assert.Equal(5, reread.AbsoluteMissingFloor);
        Assert.Equal(2, reread.RepairButtonMeanings.Count);
        Assert.Contains(reread.RepairButtonMeanings, m => m.Label == "MY_MISSING" && m.Meaning == "ConfirmedRealMissing");
    }

    [Fact]
    public async Task Put_AsAdmin_InvalidRatio_Returns400()
    {
        await SeedSystemAsync();
        _ = await CreateUserAsync("skip-admin3@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("skip-admin3@t.test", "correctpassword123");

        var bad = new ConfigDto(2.0, 8, 4, [new MeaningDto("X-OUT", "ManualSkip")]);
        using var put = await client.PutAsJsonAsync(
            new Uri("/api/admin/skip-classification", UriKind.Relative), bad);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Put_AsAdmin_InvalidMeaning_Returns400()
    {
        await SeedSystemAsync();
        _ = await CreateUserAsync("skip-admin4@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);
        using var client = await LoggedInClientAsync("skip-admin4@t.test", "correctpassword123");

        var bad = new ConfigDto(0.5, 8, 4, [new MeaningDto("X-OUT", "NotAMeaning")]);
        using var put = await client.PutAsJsonAsync(
            new Uri("/api/admin/skip-classification", UriKind.Relative), bad);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task UpdatedThreshold_ChangesSkipSummaryClassification()
    {
        await SeedSystemAsync();
        _ = await CreateUserAsync("skip-report@t.test", "correctpassword123", BootstrapAdmin.RoleAdmin);

        // One reviewed card: 10 components, 5 "missing" objects, no X-OUT.
        // Under the default ratio (0.50) 5/10 fires HeuristicMissing; raise
        // the ratio above 0.50 and the same card classifies as None.
        var tos = new List<TestedObjectRow>();
        for (var i = 0; i < 5; i++)
        {
            tos.Add(To(1, 1, ObjectMissing, objId: 10 + i));
        }
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels = [Panel(1, reviewed: true)],
            SeededCards = [Card(1, 1, components: 10)],
            SeededTestedObjects = tos,
        };

        using var owned = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(fake)));
        using var client = await LoggedInClientAsync("skip-report@t.test", "correctpassword123", owned);

        // Default config: the card is HeuristicMissing → 1 skipped card.
        var before = await client.GetFromJsonAsync<SkipSummaryLite>(SkipSummaryUri());
        Assert.Equal(1, before!.SkippedCards);

        // Raise the missing-ratio threshold above the card's 0.50 ratio.
        var update = new ConfigDto(0.90, 8, 4, [new MeaningDto("X-OUT", "ManualSkip")]);
        using var put = await client.PutAsJsonAsync(
            new Uri("/api/admin/skip-classification", UriKind.Relative), update);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // Same card now classifies as None → 0 skipped cards.
        var after = await client.GetFromJsonAsync<SkipSummaryLite>(SkipSummaryUri());
        Assert.Equal(0, after!.SkippedCards);
    }

    private static Uri SkipSummaryUri() => new(
        $"/api/reports/skip-summary?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}",
        UriKind.Relative);

    // ---- helpers ----------------------------------------------------------

    private sealed record ConfigDto(
        double MissingRatioThreshold,
        int MinComponentFloor,
        int AbsoluteMissingFloor,
        List<MeaningDto> RepairButtonMeanings);

    private sealed record MeaningDto(string Label, string Meaning);

    private sealed record SkipSummaryLite(long SkippedCards);

    private async Task EnsureDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        await db.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<NiewebRole>>();
        foreach (var name in new[] { BootstrapAdmin.RoleReader, BootstrapAdmin.RoleAuthor, BootstrapAdmin.RoleAdmin })
        {
            if (!await roles.RoleExistsAsync(name))
            {
                _ = await roles.CreateAsync(new NiewebRole
                {
                    Name = name,
                    NormalizedName = name.ToUpperInvariant(),
                });
            }
        }
    }

    private async Task SeedSystemAsync()
    {
        using var scope = _factory.Services.CreateScope();
        // Reset to a clean default config first: the fixture DB is shared
        // across tests, so a prior PUT would otherwise leak into the
        // "defaults" assertions. Clearing then re-seeding restores the
        // canonical skip.* rows.
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        db.AppParameters.RemoveRange(db.AppParameters);
        await db.SaveChangesAsync();
        var svc = scope.ServiceProvider.GetRequiredService<IAppParameters>();
        await svc.EnsureSeededAsync();
    }

    private async Task<NiewebUser> CreateUserAsync(string email, string password, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<NiewebUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }
        var user = new NiewebUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
            CreatedUtc = DateTime.UtcNow,
        };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        if (roles.Length > 0)
        {
            Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        }
        return user;
    }

    private async Task<HttpClient> LoggedInClientAsync(
        string email, string password, WebApplicationFactory<Program>? factory = null)
    {
        var host = factory ?? _factory;
        using var anon = host.CreateClient();
        var login = new AuthEndpoints.LoginRequest { Email = email, Password = password };
        using var res = await anon.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), login);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var payload = await res.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

    // ---- fake-source builders ---------------------------------------------

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
        NbOfTestedObject: 10,
        NbOfErrorObject: 0,
        OperatorId: null,
        ProductId: 500,
        RecipeId: 1);

    private static CardRow Card(int panelId, int cardId, int components) => new(
        PanelId: panelId,
        CardIdOnPanel: cardId,
        CardStatus: 1,
        AnomalyBr: 0,
        AnomalyAr: 0,
        NbOfTestedObject: components,
        NbOfErrorObject: 0,
        MachineId: 10,
        ProductId: 500,
        PanelNumericDate: WindowStartEpoch + panelId);

    private static TestedObjectRow To(int panel, int card, long errorTable, int objId) => new(
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
        RepairButtonComment: null);
}
