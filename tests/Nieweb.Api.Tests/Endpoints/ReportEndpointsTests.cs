using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Identity;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data.Entities;
using Nieweb.DataSources;
using Nieweb.Reports;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

public sealed class ReportEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public ReportEndpointsTests(NiewebApiFactory factory)
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

    // Window: 2026-01-01..2026-01-02 UTC.
    private const string StartUtc = "2026-01-01T00:00:00Z";
    private const string EndUtc = "2026-01-02T00:00:00Z";
    private const int WindowStartEpoch = 1767225600;

    [Fact]
    public async Task PanelYield_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/panel-yield?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PanelYield_UnknownSource_Returns404()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-unknown@nieweb.test");

        using var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield?sourceId=does-not-exist&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PanelYield_InvalidWindow_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(fake)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-badwindow@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // end <= start
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield?sourceId=postreflow&startUtc={EndUtc}&endUtc={StartUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PanelYield_MissingSourceId_Returns400()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-missing@nieweb.test");
        using var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield?startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PanelYield_SeededPanels_ReturnsExpectedKpis()
    {
        // 4 good (statuses 1,1,2,1), 1 faulty (-1), 1 not-inspected (0).
        // Inspected = 5, Good = 4 -> FPY = 80%.
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, 100, WindowStartEpoch + 60, 1),
                Panel(2, 100, WindowStartEpoch + 120, 1),
                Panel(3, 100, WindowStartEpoch + 180, 2),
                Panel(4, 200, WindowStartEpoch + 60, 1),
                Panel(5, 200, WindowStartEpoch + 120, -1),
                Panel(6, 200, WindowStartEpoch + 180, 0),
            ],
            SeededMachines =
            [
                new Machine(100, 2, "AOI-100", "AOI"),
                new Machine(200, 2, "AOI-200", "AOI"),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(fake)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-happy@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PanelYieldResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal("postreflow", payload!.Source.Id);
        Assert.Equal(6, payload.Overall.TotalPanels);
        Assert.Equal(5, payload.Overall.InspectedPanels);
        Assert.Equal(4, payload.Overall.GoodPanels);
        Assert.Equal(1, payload.Overall.FaultyPanels);
        Assert.Equal(1, payload.Overall.NotInspectedPanels);
        Assert.Equal(80d, payload.Overall.FpyPercent);
        Assert.Equal(2, payload.ByMachine.Count);
        Assert.Equal(100, payload.ByMachine[0].MachineId);
        Assert.Equal("AOI-100", payload.ByMachine[0].MachineName);
        Assert.Equal(100d, payload.ByMachine[0].Kpi.FpyPercent);
        Assert.Equal(200, payload.ByMachine[1].MachineId);
        Assert.Equal(50d, payload.ByMachine[1].Kpi.FpyPercent);
    }

    [Fact]
    public async Task PanelYield_MachineIdsFilterHonoured()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, 300, WindowStartEpoch + 60, 1),
                Panel(2, 300, WindowStartEpoch + 120, 2),
                Panel(3, 301, WindowStartEpoch + 60, -1),
                Panel(4, 301, WindowStartEpoch + 120, -2),
            ],
            SeededMachines =
            [
                new Machine(300, 2, "AOI-300", "AOI"),
                new Machine(301, 2, "AOI-301", "AOI"),
            ],
        };
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(fake)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-filter@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&machineIds=300", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PanelYieldResult>(_responseJson);
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Overall.TotalPanels);
        Assert.Equal(100d, payload.Overall.FpyPercent);
        var only = Assert.Single(payload.ByMachine);
        Assert.Equal(300, only.MachineId);
    }

    // -------------------------------------------------------------------------
    // CSV export endpoint (R4): /api/reports/panel-yield/export.csv
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PanelYieldCsv_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri($"/api/reports/panel-yield/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PanelYieldCsv_UnknownSource_Returns404()
    {
        using var client = _factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-csv-unknown@nieweb.test");

        using var authed = _factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield/export.csv?sourceId=does-not-exist&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PanelYieldCsv_InvalidWindow_Returns400()
    {
        var fake = new FakeAoiSource(_postDescriptor);
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(fake)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-csv-badwindow@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // end <= start
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield/export.csv?sourceId=postreflow&startUtc={EndUtc}&endUtc={StartUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PanelYieldCsv_SeededPanels_ReturnsUtf8CsvWithExpectedRows()
    {
        // Same seed as the JSON happy-path test so numeric parity is
        // trivially verifiable against ReportEndpointsTests above.
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, 100, WindowStartEpoch + 60, 1),
                Panel(2, 100, WindowStartEpoch + 120, 1),
                Panel(3, 100, WindowStartEpoch + 180, 2),
                Panel(4, 200, WindowStartEpoch + 60, 1),
                Panel(5, 200, WindowStartEpoch + 120, -1),
                Panel(6, 200, WindowStartEpoch + 180, 0),
            ],
            SeededMachines =
            [
                new Machine(100, 2, "AOI-100", "AOI"),
                new Machine(200, 2, "AOI-200", "AOI"),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(fake)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-csv-happy@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(
            "panel-yield-postreflow-20260101-20260102.csv",
            response.Content.Headers.ContentDisposition.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        // UTF-8 BOM so Excel-on-Windows autodetects encoding.
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);   // header + 2 machine rows
        Assert.Equal(
            "SourceId,SourceName,WindowStartUtc,WindowEndUtc,MachineId,MachineName,TotalPanels,InspectedPanels,GoodPanels,FaultyPanels,NotInspectedPanels,FpyPercent",
            lines[0]);
        Assert.Equal(
            "postreflow,Post-reflow AOI,2026-01-01T00:00:00Z,2026-01-02T00:00:00Z,100,AOI-100,3,3,3,0,0,100",
            lines[1]);
        Assert.Equal(
            "postreflow,Post-reflow AOI,2026-01-01T00:00:00Z,2026-01-02T00:00:00Z,200,AOI-200,3,2,1,1,1,50",
            lines[2]);
    }

    [Fact]
    public async Task PanelYieldCsv_EscapesFieldsContainingCommasAndQuotes()
    {
        // Machine name with a comma and an embedded double-quote must be
        // RFC-4180 escaped: wrapped in quotes and internal " doubled.
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, 400, WindowStartEpoch + 60, 1),
            ],
            SeededMachines =
            [
                new Machine(400, 2, "AOI-400, \"Alpha\"", "AOI"),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(fake)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-csv-escape@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        // The machine-name column must be quoted and the embedded " doubled.
        Assert.Contains(",\"AOI-400, \"\"Alpha\"\"\",", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PanelYieldCsv_MachineIdsFilterHonoured()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, 500, WindowStartEpoch + 60, 1),
                Panel(2, 500, WindowStartEpoch + 120, 1),
                Panel(3, 501, WindowStartEpoch + 60, -1),
            ],
            SeededMachines =
            [
                new Machine(500, 2, "AOI-500", "AOI"),
                new Machine(501, 2, "AOI-501", "AOI"),
            ],
        };
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(fake)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "yield-csv-filter@nieweb.test");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/panel-yield/export.csv?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}&machineIds=500", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csv = await response.Content.ReadAsStringAsync();
        // BOM (3 bytes) + header + a single machine row.
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains(",500,AOI-500,", lines[1], StringComparison.Ordinal);
    }

    private static PanelRow Panel(int id, int machineId, int date, int status) =>
        new(
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
