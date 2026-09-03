using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Identity;

using Nieweb.Api.Endpoints;
using Nieweb.Api.Tests.Fakes;
using Nieweb.Data.Entities;
using Nieweb.DataSources;

using Xunit;

namespace Nieweb.Api.Tests.Endpoints;

public sealed class AnalyseEndpointsTests : IClassFixture<NiewebApiFactory>
{
    private readonly NiewebApiFactory _factory;

    public AnalyseEndpointsTests(NiewebApiFactory factory)
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

    [Fact]
    public async Task Contracts_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/analyse/contracts", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Contracts_PreReflow_Source_ReportsUnsupportedFeatureToggles()
    {
        var pre = new FakeAoiSource(
            new SourceDescriptor(
                "prereflow",
                "Pre-reflow AOI",
                "4.3.1",
                Capabilities.PastePrintMetrics | Capabilities.FeederAnalytics));

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(pre)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-pre@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/contracts?sourceId=prereflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("prereflow", root.GetProperty("source").GetProperty("id").GetString());

        var dashboards = root.GetProperty("dashboards").EnumerateArray().ToArray();
        var live = dashboards.Single(d => d.GetProperty("dashboard").GetString() == "Live");
        var liveFeature = live.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("featureId").GetString() == "latest-inspection-filter");
        Assert.False(liveFeature.GetProperty("supported").GetBoolean());
        Assert.Equal("IsLastInspectionFilter", liveFeature.GetProperty("missingCapability").GetString());

        var line = dashboards.Single(d => d.GetProperty("dashboard").GetString() == "LinePerformance");
        var lineFeature = line.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("featureId").GetString() == "machine-efficiency-time-pie");
        Assert.False(lineFeature.GetProperty("supported").GetBoolean());
        Assert.Equal("MachineEfficiencyTiming", lineFeature.GetProperty("missingCapability").GetString());
    }

    [Fact]
    public async Task Contracts_PostReflow_Source_ReportsFeatureTogglesSupported()
    {
        var post = new FakeAoiSource(
            new SourceDescriptor(
                "postreflow",
                "Post-reflow AOI",
                "5.0",
                Capabilities.IsLastInspectionFilter | Capabilities.MachineEfficiencyTiming));

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-post@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/contracts?sourceId=postreflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var dashboards = doc.RootElement.GetProperty("dashboards").EnumerateArray().ToArray();

        var live = dashboards.Single(d => d.GetProperty("dashboard").GetString() == "Live");
        var liveFeature = live.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("featureId").GetString() == "latest-inspection-filter");
        Assert.True(liveFeature.GetProperty("supported").GetBoolean());

        var line = dashboards.Single(d => d.GetProperty("dashboard").GetString() == "LinePerformance");
        var lineFeature = line.GetProperty("features").EnumerateArray()
            .Single(f => f.GetProperty("featureId").GetString() == "machine-efficiency-time-pie");
        Assert.True(lineFeature.GetProperty("supported").GetBoolean());
    }

    [Fact]
    public async Task LiveSummary_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/analyse/live-summary", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LiveSummary_PreReflow_OnlyLastInspection_AppliesInMemoryDedupe()
    {
        var start = 1785542400;
        var pre = new FakeAoiSource(
            new SourceDescriptor(
                "prereflow",
                "Pre-reflow AOI",
                "4.3.1",
                Capabilities.PastePrintMetrics))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0),
                Panel(id: 3, machineId: 10, date: start + 15, status: 2, barcode: "BC-1", face: 1),
                Panel(id: 4, machineId: 10, date: start + 30, status: 0, barcode: "BC-2", face: 0),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(pre)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-live-pre@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/live-summary?sourceId=prereflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&onlyLastInspection=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("prereflow", root.GetProperty("source").GetProperty("id").GetString());
        Assert.True(root.GetProperty("dedupeAppliedInMemory").GetBoolean());

        var kpi = root.GetProperty("kpi");
        Assert.Equal(3, kpi.GetProperty("totalPanels").GetInt64());
        Assert.Equal(2, kpi.GetProperty("inspectedPanels").GetInt64());
        Assert.Equal(2, kpi.GetProperty("goodPanels").GetInt64());
        Assert.Equal(0, kpi.GetProperty("faultyPanels").GetInt64());
        Assert.Equal(1, kpi.GetProperty("notInspectedPanels").GetInt64());
    }

    [Fact]
    public async Task LiveSummary_PreReflow_RawMode_DoesNotDedupe()
    {
        var start = 1785542400;
        var pre = new FakeAoiSource(
            new SourceDescriptor(
                "prereflow",
                "Pre-reflow AOI",
                "4.3.1",
                Capabilities.PastePrintMetrics))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(pre)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-live-raw@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/live-summary?sourceId=prereflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&onlyLastInspection=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.False(root.GetProperty("dedupeAppliedInMemory").GetBoolean());
        var kpi = root.GetProperty("kpi");
        Assert.Equal(2, kpi.GetProperty("totalPanels").GetInt64());
        Assert.Equal(1, kpi.GetProperty("goodPanels").GetInt64());
        Assert.Equal(1, kpi.GetProperty("faultyPanels").GetInt64());
    }

    [Fact]
    public async Task LinePerformance_PreReflow_OnlyLastInspection_AppliesInMemoryDedupe()
    {
        var start = 1785542400;
        var pre = new FakeAoiSource(
            new SourceDescriptor(
                "prereflow",
                "Pre-reflow AOI",
                "4.3.1",
                Capabilities.PastePrintMetrics))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0),
                Panel(id: 3, machineId: 10, date: start + 15, status: 2, barcode: "BC-1", face: 1),
                Panel(id: 4, machineId: 11, date: start + 30, status: 0, barcode: "BC-2", face: 0),
            ],
            SeededCards =
            [
                Card(panelId: 2, machineId: 10, date: start + 20, nbTestsOnComp: 10),
                Card(panelId: 3, machineId: 10, date: start + 15, nbTestsOnComp: 10),
                Card(panelId: 4, machineId: 11, date: start + 30, nbTestsOnComp: 5),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 2, machineId: 10, date: start + 20, objectTypeId: 0x01, errorTable: 1, errorTableAr: 1),
                Obj(panelId: 3, machineId: 10, date: start + 15, objectTypeId: 0x01, errorTable: 2, errorTableAr: 2),
                Obj(panelId: 4, machineId: 11, date: start + 30, objectTypeId: 0x01, errorTable: 0, errorTableAr: 0),
            ],
            SeededMachines =
            [
                new Machine(10, 2, "AOI-10", "AOI"),
                new Machine(11, 2, "AOI-11", "AOI"),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(pre)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-line-pre@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/line-performance-summary?sourceId=prereflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&onlyLastInspection=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("dedupeAppliedInMemory").GetBoolean());
        Assert.Equal(3, root.GetProperty("overallYield").GetProperty("totalPanels").GetInt64());
        Assert.Equal(2, root.GetProperty("overallYield").GetProperty("inspectedPanels").GetInt64());
        Assert.Equal(25L, root.GetProperty("overallDpmo").GetProperty("opportunityCount").GetInt64());
        Assert.Equal(2L, root.GetProperty("overallDpmo").GetProperty("defectBitCount").GetInt64());
    }

    [Fact]
    public async Task LinePerformance_PostReflow_RawMode_DoesNotDedupe()
    {
        var start = 1785542400;
        var post = new FakeAoiSource(
            new SourceDescriptor(
                "postreflow",
                "Post-reflow AOI",
                "5.0",
                Capabilities.IsLastInspectionFilter))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0),
            ],
            SeededCards =
            [
                Card(panelId: 2, machineId: 10, date: start + 20, nbTestsOnComp: 10),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 2, machineId: 10, date: start + 20, objectTypeId: 0x01, errorTable: 3, errorTableAr: 3),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-line-post@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/line-performance-summary?sourceId=postreflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&onlyLastInspection=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.False(root.GetProperty("dedupeAppliedInMemory").GetBoolean());
        Assert.Equal(2, root.GetProperty("overallYield").GetProperty("totalPanels").GetInt64());
        Assert.Equal(1, root.GetProperty("overallYield").GetProperty("goodPanels").GetInt64());
        Assert.Equal(1, root.GetProperty("overallYield").GetProperty("faultyPanels").GetInt64());
    }

    [Fact]
    public async Task ProductSummary_PreReflow_OnlyLastInspection_AppliesInMemoryDedupe()
    {
        var start = 1785542400;
        var pre = new FakeAoiSource(
            new SourceDescriptor(
                "prereflow",
                "Pre-reflow AOI",
                "4.3.1",
                Capabilities.PastePrintMetrics))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 3, machineId: 11, date: start + 15, status: 2, barcode: "BC-2", face: 0, productId: 200),
            ],
            SeededCards =
            [
                Card(panelId: 2, machineId: 10, date: start + 20, nbTestsOnComp: 10, productId: 100),
                Card(panelId: 3, machineId: 11, date: start + 15, nbTestsOnComp: 5, productId: 200),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 2, machineId: 10, date: start + 20, objectTypeId: 0x01, errorTable: 3, errorTableAr: 3, productId: 100),
                Obj(panelId: 3, machineId: 11, date: start + 15, objectTypeId: 0x01, errorTable: 1, errorTableAr: 1, productId: 200),
            ],
            SeededProducts =
            [
                new Product(100, "Widget", null, null),
                new Product(200, "Gadget", null, null),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(pre)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-product-pre@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/product-summary?sourceId=prereflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&onlyLastInspection=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("dedupeAppliedInMemory").GetBoolean());
        Assert.Equal(2, root.GetProperty("products").GetArrayLength());
        Assert.Equal(15, root.GetProperty("overallDpmo").GetProperty("opportunityCount").GetInt64());
        Assert.Equal(3, root.GetProperty("overallDpmo").GetProperty("defectBitCount").GetInt64());
    }

    [Fact]
    public async Task ProductSummary_PostReflow_RawMode_DoesNotDedupe()
    {
        var start = 1785542400;
        var post = new FakeAoiSource(
            new SourceDescriptor(
                "postreflow",
                "Post-reflow AOI",
                "5.0",
                Capabilities.IsLastInspectionFilter))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0, productId: 100),
            ],
            SeededCards =
            [
                Card(panelId: 2, machineId: 10, date: start + 20, nbTestsOnComp: 10, productId: 100),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 2, machineId: 10, date: start + 20, objectTypeId: 0x01, errorTable: 3, errorTableAr: 3, productId: 100),
            ],
            SeededProducts =
            [
                new Product(100, "Widget", null, null),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-product-post@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/product-summary?sourceId=postreflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&onlyLastInspection=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.False(root.GetProperty("dedupeAppliedInMemory").GetBoolean());
        Assert.Equal(2, root.GetProperty("overallYield").GetProperty("totalPanels").GetInt64());
        Assert.Equal(1, root.GetProperty("overallYield").GetProperty("goodPanels").GetInt64());
        Assert.Equal(1, root.GetProperty("overallYield").GetProperty("faultyPanels").GetInt64());
    }

    [Fact]
    public async Task ProductDetail_PreReflow_OnlyLastInspection_AppliesInMemoryDedupe()
    {
        var start = 1785542400;
        var pre = new FakeAoiSource(
            new SourceDescriptor(
                "prereflow",
                "Pre-reflow AOI",
                "4.3.1",
                Capabilities.PastePrintMetrics))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 3, machineId: 11, date: start + 40, status: 2, barcode: "BC-2", face: 0, productId: 100),
            ],
            SeededCards =
            [
                Card(panelId: 1, machineId: 10, date: start + 10, nbTestsOnComp: 10, productId: 100),
                Card(panelId: 2, machineId: 10, date: start + 20, nbTestsOnComp: 10, productId: 100),
                Card(panelId: 3, machineId: 11, date: start + 40, nbTestsOnComp: 5, productId: 100),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 1, machineId: 10, date: start + 10, objectTypeId: 0x01, errorTable: 3, errorTableAr: 3, productId: 100),
                Obj(panelId: 2, machineId: 10, date: start + 20, objectTypeId: 0x01, errorTable: 3, errorTableAr: 3, productId: 100),
                Obj(panelId: 3, machineId: 11, date: start + 40, objectTypeId: 0x01, errorTable: 1, errorTableAr: 1, productId: 100),
            ],
            SeededProducts =
            [
                new Product(100, "Widget", null, null),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(pre)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-product-detail-pre@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/product-detail/100?sourceId=prereflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&onlyLastInspection=true&bucket=Day");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("dedupeAppliedInMemory").GetBoolean());
        Assert.Equal(100, root.GetProperty("productId").GetInt32());
        Assert.Equal("Widget", root.GetProperty("productName").GetString());
        Assert.Equal(2, root.GetProperty("overallYield").GetProperty("totalPanels").GetInt64());
        Assert.Equal(2, root.GetProperty("overallYield").GetProperty("goodPanels").GetInt64());
        Assert.Equal(15, root.GetProperty("overallDpmo").GetProperty("opportunityCount").GetInt64());
        Assert.Equal(3, root.GetProperty("overallDpmo").GetProperty("defectBitCount").GetInt64());
        Assert.Equal(1, root.GetProperty("buckets").GetArrayLength());
        Assert.Equal(1, root.GetProperty("trend").GetArrayLength());
        Assert.Equal(2, root.GetProperty("trend")[0].GetProperty("topDefectBits").GetArrayLength());
    }

    [Fact]
    public async Task ProductDetail_InvalidBucket_Returns400()
    {
        var post = new FakeAoiSource(
            new SourceDescriptor(
                "postreflow",
                "Post-reflow AOI",
                "5.0",
                Capabilities.IsLastInspectionFilter));

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(post)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-product-detail-badbucket@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/product-detail/100?sourceId=postreflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&bucket=Month");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PanelSummary_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/analyse/panel-summary", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PanelSummary_PreReflow_OnlyLastInspection_AppliesInMemoryDedupe()
    {
        var start = 1785542400;
        var pre = new FakeAoiSource(
            new SourceDescriptor(
                "prereflow",
                "Pre-reflow AOI",
                "4.3.1",
                Capabilities.PastePrintMetrics))
        {
            SeededPanels =
            [
                Panel(id: 1, machineId: 10, date: start + 10, status: -1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 2, machineId: 10, date: start + 20, status: 1, barcode: "BC-1", face: 0, productId: 100),
                Panel(id: 3, machineId: 11, date: start + 15, status: -1, barcode: "BC-2", face: 0, productId: 200),
            ],
            SeededCards =
            [
                Card(panelId: 2, machineId: 10, date: start + 20, nbTestsOnComp: 10, productId: 100),
                Card(panelId: 3, machineId: 11, date: start + 15, nbTestsOnComp: 5, productId: 200),
            ],
            SeededTestedObjects =
            [
                Obj(panelId: 2, machineId: 10, date: start + 20, objectTypeId: 0x01, errorTable: 3, errorTableAr: 3, productId: 100),
                Obj(panelId: 3, machineId: 11, date: start + 15, objectTypeId: 0x01, errorTable: 1, errorTableAr: 1, productId: 200),
            ],
            SeededProducts =
            [
                new Product(100, "Widget", null, null),
                new Product(200, "Gadget", null, null),
            ],
        };

        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IAoiSource>(pre)));

        using var client = factory.CreateClient();
        var token = await IssueTokenAsync(client, "analyse-panel-pre@nieweb.test");

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await authed.GetAsync(
            "/api/analyse/panel-summary?sourceId=prereflow&startUtc=2026-08-01T00:00:00Z&endUtc=2026-08-02T00:00:00Z&onlyLastInspection=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("dedupeAppliedInMemory").GetBoolean());
        Assert.Equal(2, root.GetProperty("totalPanels").GetInt32());
        Assert.Equal(2, root.GetProperty("panels").GetArrayLength());
        Assert.Equal(2, root.GetProperty("panels")[0].GetProperty("panelId").GetInt32());
        Assert.Equal(2, root.GetProperty("panels")[0].GetProperty("defectBitCount").GetInt64());
    }

    private static PanelRow Panel(int id, int machineId, int date, int status, string barcode, int face, int productId = 100) =>
        new(
            PanelId: id,
            MachineId: machineId,
            LaneNumber: 1,
            PanelBarCode: barcode,
            PanelNumericDate: date,
            NbOfValidCards: 4,
            TestTime: 9,
            PanelStatus: status,
            AnomalyBr: 0,
            AnomalyAr: 0,
            HasBeenReviewed: false,
            NbOfTestedObject: 120,
            NbOfErrorObject: status is -2 or -1 ? 3 : 0,
            OperatorId: 9,
                ProductId: productId,
            RecipeId: 200,
            FaceNumber: face);

            private static CardRow Card(long panelId, int machineId, int date, int nbTestsOnComp, int productId = 100) =>
        new(
            PanelId: panelId,
            CardIdOnPanel: 1,
            CardStatus: 0,
            AnomalyBr: 0,
            AnomalyAr: 0,
            NbOfTestedObject: 0,
            NbOfErrorObject: 0,
            MachineId: machineId,
                ProductId: productId,
            PanelNumericDate: date,
            NbOfTestsOnComp: nbTestsOnComp,
            NbOfTestsOnPads: null);

            private static TestedObjectRow Obj(long panelId, int machineId, int date, int objectTypeId, long errorTable, long errorTableAr, int productId = 100) =>
        new(
            PanelId: panelId,
            CardIdOnPanel: 1,
            ObjectId: date,
            ObjectTypeId: objectTypeId,
            ErrorTable: errorTable,
            ErrorTableAr: errorTableAr,
            Status: errorTable == 0 ? 0 : 1,
            MachineId: machineId,
            ProductId: productId,
            PanelNumericDate: date,
            Topology: null,
            PartNumberName: null,
            JedecName: null);

}
