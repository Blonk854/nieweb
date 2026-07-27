using System.Net;
using System.Net.Http.Headers;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Reports;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data;
using Nieweb.Data.Entities;
using Nieweb.DataSources;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for the multi-tile report export endpoints
/// <c>GET /api/reports/{id}/export.xlsx</c> and
/// <c>GET /api/reports/{id}/export.pdf</c> (docs/phase-2.md §7.6 <c>RC5</c>).
/// Seeds a saved report + tiles through <see cref="IReports"/> and
/// exercises the endpoints against a <see cref="FakeAoiSource"/> so
/// each tile actually runs its underlying report.
/// </summary>
public sealed class ReportExportEndpointTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    private static readonly SourceDescriptor _postDescriptor = new(
        "postreflow", "Post-reflow AOI", "5.0",
        Capabilities.PinLevel | Capabilities.IsLastInspectionFilter | Capabilities.BarcodeProductView);

    private const string StartUtc = "2026-01-01T00:00:00Z";
    private const string EndUtc = "2026-01-02T00:00:00Z";
    private const int WindowStartEpoch = 1767225600;
    private const int ComponentType = 32; // matches DpmoAndParetoEndpointsTests.
    private const long BitObjectMissing = 1L << 0;

    public ReportExportEndpointTests(NiewebApiFactory factory)
    {
        _factory = factory;
        EnsureDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NiewebDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    // -------------------- helpers --------------------

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
        };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
    }

    private async Task<(HttpClient Authed, WebApplicationFactory<Program>? OwnedFactory)> AuthedClientAsync(
        string email, FakeAoiSource? source)
    {
        WebApplicationFactory<Program>? owned = null;
        WebApplicationFactory<Program> factory = _factory;
        if (source is not null)
        {
            owned = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(source)));
            factory = owned;
        }
        using var login = factory.CreateClient();
        var token = await IssueTokenAsync(login, email);
        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (authed, owned);
    }

    private static async Task<int> SeedReportAsync(
        WebApplicationFactory<Program> factory,
        string title,
        params (string TileType, string? Title)[] tiles)
        => await SeedReportWithConfigsAsync(
            factory,
            title,
            [.. tiles.Select(t => (t.TileType, t.Title, "{}"))]);

    private static async Task<int> SeedReportWithConfigsAsync(
        WebApplicationFactory<Program> factory,
        string title,
        params (string TileType, string? Title, string ConfigJson)[] tiles)
    {
        using var scope = factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReports>();
        var report = await reports.CreateReportAsync(new CreateReportInput(
            Title: title,
            Description: "Seeded RC5/RC6 test report",
            ReportGroupId: null,
            OwnerUserId: null,
            OwnerDisplayName: "rc-tester",
            IsLocked: false,
            IsPinnedHome: false,
            RefreshFrequencySeconds: null,
            ChromeJson: null,
            DisplayOrder: 0));

        var order = 0;
        foreach (var (tileType, tileTitle, configJson) in tiles)
        {
            _ = await reports.AddEntityAsync(report.Id, new AddEntityInput(
                TileType: tileType,
                Title: tileTitle,
                DisplayOrder: order++,
                ConfigJson: configJson));
        }
        return report.Id;
    }

    private static TestedObjectRow Obj(int machineId, int date, long errorBits)
        => new(
            PanelId: 1,
            CardIdOnPanel: 1,
            ObjectId: date,
            ObjectTypeId: ComponentType,
            ErrorTable: errorBits,
            ErrorTableAr: errorBits,
            Status: errorBits == 0 ? 0 : 1,
            MachineId: machineId,
            ProductId: 500,
            PanelNumericDate: date,
            Topology: null,
            PartNumberName: null,
            JedecName: null);

    private static PanelRow Panel(int id, int machineId, int status)
        => new(
            PanelId: id,
            MachineId: machineId,
            LaneNumber: 1,
            PanelBarCode: $"BC-{id:D6}",
            PanelNumericDate: WindowStartEpoch + id,
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

    // -------------------- auth --------------------

    [Fact]
    public async Task Xlsx_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri("/api/reports/1/export.xlsx?sourceId=postreflow&startUtc=" + StartUtc + "&endUtc=" + EndUtc, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pdf_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            new Uri("/api/reports/1/export.pdf?sourceId=postreflow&startUtc=" + StartUtc + "&endUtc=" + EndUtc, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------- report id --------------------

    [Fact]
    public async Task Xlsx_UnknownReportId_Returns404()
    {
        var (authed, factory) = await AuthedClientAsync("rc5-xlsx-unknown@nieweb.test", new FakeAoiSource(_postDescriptor));
        using var response = await authed.GetAsync(
            new Uri("/api/reports/999999/export.xlsx?sourceId=postreflow&startUtc=" + StartUtc + "&endUtc=" + EndUtc, UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Pdf_UnknownReportId_Returns404()
    {
        var (authed, factory) = await AuthedClientAsync("rc5-pdf-unknown@nieweb.test", new FakeAoiSource(_postDescriptor));
        using var response = await authed.GetAsync(
            new Uri("/api/reports/999999/export.pdf?sourceId=postreflow&startUtc=" + StartUtc + "&endUtc=" + EndUtc, UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------- filter validation --------------------

    [Fact]
    public async Task Xlsx_MissingSourceId_Returns400()
    {
        var (authed, factory) = await AuthedClientAsync("rc5-xlsx-nosrc@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportAsync(factory!, "Empty");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Xlsx_UnknownSource_Returns404()
    {
        var (authed, factory) = await AuthedClientAsync("rc5-xlsx-badsrc@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportAsync(factory!, "Empty");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=nope&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Xlsx_InvalidWindow_Returns400()
    {
        var (authed, factory) = await AuthedClientAsync("rc5-xlsx-badwin@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportAsync(factory!, "Empty");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=postreflow&startUtc={EndUtc}&endUtc={StartUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------- happy paths --------------------

    [Fact]
    public async Task Xlsx_EmptyReport_ReturnsCoverOnly()
    {
        var (authed, factory) = await AuthedClientAsync("rc5-xlsx-empty@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportAsync(factory!, "Empty report");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? string.Empty, StringComparison.Ordinal);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);
        Assert.Single(wb.Worksheets);
        Assert.Equal("Cover", wb.Worksheets.First().Name);
        Assert.Equal("Empty report", wb.Worksheet("Cover").Cell("A1").GetString().Replace("Nieweb - ", string.Empty, StringComparison.Ordinal));

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Xlsx_WithPanelYieldAndParetoTiles_ProducesExpectedSheets()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, 10, status: 1), // good
                Panel(2, 10, status: 2), // faulty
                Panel(3, 11, status: 1),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 60, BitObjectMissing),
                Obj(10, WindowStartEpoch + 61, 0),
                Obj(11, WindowStartEpoch + 70, BitObjectMissing),
            ],
        };
        var (authed, factory) = await AuthedClientAsync("rc5-xlsx-happy@nieweb.test", fake);
        var reportId = await SeedReportAsync(
            factory!,
            "Two-tile report",
            ("panelYield", "Yield overview"),
            ("pareto", "Defect Pareto"));

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        var sheetNames = wb.Worksheets.Select(w => w.Name).ToList();
        Assert.Equal(3, sheetNames.Count);
        Assert.Equal("Cover", sheetNames[0]);
        Assert.StartsWith("01.", sheetNames[1], StringComparison.Ordinal);
        Assert.StartsWith("02.", sheetNames[2], StringComparison.Ordinal);
        Assert.Contains("Yield overview", sheetNames[1], StringComparison.Ordinal);
        Assert.Contains("Defect Pareto", sheetNames[2], StringComparison.Ordinal);

        // Cover lists both tiles + statuses.
        var cover = wb.Worksheet("Cover");
        Assert.Equal("rendered", cover.Cell(13, 4).GetString());
        Assert.Equal("rendered", cover.Cell(14, 4).GetString());

        // Panel-yield sheet has metric labels in column A starting at row 3.
        var yieldSheet = wb.Worksheets.ElementAt(1);
        Assert.Equal("Metric", yieldSheet.Cell("A3").GetString());
        Assert.Equal("Total Panels", yieldSheet.Cell("A4").GetString());

        // Pareto sheet echoes the axis + numerator.
        var paretoSheet = wb.Worksheets.ElementAt(2);
        Assert.Equal("Axis", paretoSheet.Cell("A3").GetString());
        Assert.Equal("Defect", paretoSheet.Cell("B3").GetString());
        Assert.Equal("Numerator", paretoSheet.Cell("A4").GetString());

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Xlsx_ParetoTile_HonorsPerTileConfig()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, 10, status: 1),
                Panel(2, 10, status: 2),
                Panel(3, 11, status: 1),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 60, BitObjectMissing),
                Obj(10, WindowStartEpoch + 61, 0),
                Obj(11, WindowStartEpoch + 70, BitObjectMissing),
            ],
        };
        var (authed, factory) = await AuthedClientAsync("rc5-xlsx-pareto-config@nieweb.test", fake);
        var reportId = await SeedReportWithConfigsAsync(
            factory!,
            "Configured Pareto report",
            ("pareto", "By machine", "{\"axis\":\"AoiMachine\",\"numerator\":\"Aoi\",\"weight\":\"Dpmo\"}"));

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        // The pareto tile's sheet must echo the per-tile config, not the
        // hardcoded default (axis=Defect / numerator=Real / weight=Count).
        // This proves ConfigJson now drives the export end to end.
        var paretoSheet = wb.Worksheets.ElementAt(1);
        Assert.Equal("AoiMachine", paretoSheet.Cell("B3").GetString());
        Assert.Equal("Aoi", paretoSheet.Cell("B4").GetString());
        Assert.Equal("Dpmo", paretoSheet.Cell("B5").GetString());

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Xlsx_UnknownTileType_RendersPlaceholderSheet()
    {
        var (authed, factory) = await AuthedClientAsync("rc5-xlsx-unknown-tile@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportAsync(factory!, "Unknown tile", ("frobnicator", "Legacy tile"));
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        var cover = wb.Worksheet("Cover");
        Assert.Equal("unsupported (skipped)", cover.Cell(13, 4).GetString());

        var sheet = wb.Worksheets.ElementAt(1);
        Assert.Contains("Unsupported tile type 'frobnicator'", sheet.Cell("A3").GetString(), StringComparison.Ordinal);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Pdf_HappyPath_ReturnsPdfBytes()
    {
        var fake = new FakeAoiSource(_postDescriptor)
        {
            SeededPanels =
            [
                Panel(1, 10, status: 1),
                Panel(2, 10, status: 2),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
            ],
            SeededTestedObjects =
            [
                Obj(10, WindowStartEpoch + 60, BitObjectMissing),
                Obj(10, WindowStartEpoch + 61, 0),
            ],
        };
        var (authed, factory) = await AuthedClientAsync("rc5-pdf-happy@nieweb.test", fake);
        var reportId = await SeedReportAsync(
            factory!,
            "Two-tile PDF report",
            ("panelYield", "Yield overview"),
            ("pareto", "Defect Pareto"));

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.pdf?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 500, "PDF should not be empty.");
        // Magic bytes: %PDF-
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Pdf_EmptyReport_StillReturnsCoverPdf()
    {
        var (authed, factory) = await AuthedClientAsync("rc5-pdf-empty@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportAsync(factory!, "Empty PDF report");
        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.pdf?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 500);
        Assert.Equal((byte)'%', bytes[0]);
        authed.Dispose();
        await factory!.DisposeAsync();
    }

    // -------------------- RC6: comment tile --------------------

    [Fact]
    public async Task Xlsx_CommentTile_WritesRawMarkdownToSheet()
    {
        const string markdown = "First paragraph.\n\nSecond paragraph with **bold** text.";
        var (authed, factory) = await AuthedClientAsync("rc6-xlsx-comment@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportWithConfigsAsync(
            factory!,
            "Report with comment",
            ("comment", "Release notes", $"{{\"markdown\":{JsonEncode(markdown)}}}"));

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        // Cover status is "rendered" (not "unsupported (skipped)").
        Assert.Equal("rendered", wb.Worksheet("Cover").Cell(13, 4).GetString());

        var sheet = wb.Worksheets.ElementAt(1);
        Assert.StartsWith("01.", sheet.Name, StringComparison.Ordinal);
        Assert.Contains("Release notes", sheet.Name, StringComparison.Ordinal);
        // Title in A1 mirrors the tile title; raw markdown lands in A3.
        Assert.Equal("Release notes", sheet.Cell("A1").GetString());
        Assert.Equal(markdown, sheet.Cell("A3").GetString());
        Assert.True(sheet.Cell("A3").Style.Alignment.WrapText);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Xlsx_CommentTile_EmptyMarkdown_RendersPlaceholder()
    {
        var (authed, factory) = await AuthedClientAsync("rc6-xlsx-comment-empty@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportWithConfigsAsync(
            factory!,
            "Report with empty comment",
            ("comment", "Blank", "{\"markdown\":\"\"}"));

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        // Empty comment is still "rendered" — just a placeholder body.
        Assert.Equal("rendered", wb.Worksheet("Cover").Cell(13, 4).GetString());
        var sheet = wb.Worksheets.ElementAt(1);
        Assert.Equal("(empty comment)", sheet.Cell("A3").GetString());

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Xlsx_CommentTile_MalformedConfig_RendersPlaceholder()
    {
        // ConfigJson missing the "markdown" property should fall
        // back to the empty-comment placeholder rather than crashing
        // the export or reporting "unsupported".
        var (authed, factory) = await AuthedClientAsync("rc6-xlsx-comment-bad@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportWithConfigsAsync(
            factory!,
            "Report with malformed comment",
            ("comment", "Bad", "{\"other\":\"value\"}"));

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.xlsx?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);

        Assert.Equal("rendered", wb.Worksheet("Cover").Cell(13, 4).GetString());
        Assert.Equal("(empty comment)", wb.Worksheets.ElementAt(1).Cell("A3").GetString());

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    [Fact]
    public async Task Pdf_CommentTile_ReturnsPdfBytes()
    {
        var (authed, factory) = await AuthedClientAsync("rc6-pdf-comment@nieweb.test", new FakeAoiSource(_postDescriptor));
        var reportId = await SeedReportWithConfigsAsync(
            factory!,
            "PDF with comment",
            ("comment", "Notes", "{\"markdown\":\"Hello world.\\n\\nSecond paragraph.\"}"));

        using var response = await authed.GetAsync(
            new Uri($"/api/reports/{reportId}/export.pdf?sourceId=postreflow&startUtc={StartUtc}&endUtc={EndUtc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 500);
        Assert.Equal((byte)'%', bytes[0]);

        authed.Dispose();
        await factory!.DisposeAsync();
    }

    private static string JsonEncode(string raw)
        => System.Text.Json.JsonSerializer.Serialize(raw);
}
