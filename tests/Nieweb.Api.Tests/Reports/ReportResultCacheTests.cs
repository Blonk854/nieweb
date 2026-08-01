using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Nieweb.Api.Reports;
using Nieweb.Api.Tests.Fakes;
using Nieweb.DataSources;
using Nieweb.Reports;

using Xunit;

namespace Nieweb.Api.Tests.Reports;

/// <summary>
/// Tests for <see cref="MemoryReportResultCache"/> — the short-TTL cache that
/// stops an export from re-running the AOI query the on-screen report just ran.
/// </summary>
public sealed class ReportResultCacheTests
{
    private static readonly SourceDescriptor _post = new(
        "postreflow", "Post-reflow AOI", "5.0", Capabilities.PinLevel);

    private static readonly SourceDescriptor _pre = new(
        "prereflow", "Pre-reflow AOI", "4.3.1", Capabilities.PastePrintMetrics);

    private static readonly DateRange _window = new(
        DateTimeOffset.Parse("2026-01-01T00:00:00Z", null),
        DateTimeOffset.Parse("2026-01-02T00:00:00Z", null));

    private static MemoryReportResultCache CreateCache(
        bool enabled = true, int ttlSeconds = 300, int maxEntries = 32) =>
        new(
            Options.Create(new ReportResultCacheOptions
            {
                Enabled = enabled,
                TtlSeconds = ttlSeconds,
                MaxEntries = maxEntries,
            }),
            NullLogger<MemoryReportResultCache>.Instance);

    private static PanelYieldFilter Filter(IReadOnlyCollection<int>? machineIds = null) =>
        new(Window: _window, MachineIds: machineIds, ProductIds: null, OnlyLastInspection: true);

    [Fact]
    public async Task GetOrRun_WithoutAStoredResult_RunsTheReport()
    {
        using var cache = CreateCache();
        var report = new CountingReport();
        var source = new FakeAoiSource(_post);

        var result = await cache.GetOrRunAsync(report, source, Filter(), CancellationToken.None);

        Assert.Equal(1, report.Runs);
        Assert.Equal(1, result.Value);
    }

    /// <summary>
    /// The headline behaviour: viewing a report then exporting it three ways
    /// used to cost four AOI passes. It now costs one.
    /// </summary>
    [Fact]
    public async Task StoreThenThreeExports_RunsTheReportExactlyOnce()
    {
        using var cache = CreateCache();
        var report = new CountingReport();
        var source = new FakeAoiSource(_post);
        var filter = Filter();

        // The on-screen report: always runs fresh, then populates the cache.
        var onScreen = await report.RunAsync(source, filter, CancellationToken.None);
        cache.Store(report, source, filter, onScreen);

        // CSV, XLSX, PDF.
        for (var i = 0; i < 3; i++)
        {
            var export = await cache.GetOrRunAsync(report, source, filter, CancellationToken.None);
            Assert.Equal(onScreen.Value, export.Value);
        }

        Assert.Equal(1, report.Runs);
    }

    [Fact]
    public async Task GetOrRun_WithADifferentFilter_MissesAndRunsAgain()
    {
        using var cache = CreateCache();
        var report = new CountingReport();
        var source = new FakeAoiSource(_post);

        await cache.GetOrRunAsync(report, source, Filter([1, 2]), CancellationToken.None);
        await cache.GetOrRunAsync(report, source, Filter([1, 3]), CancellationToken.None);

        Assert.Equal(2, report.Runs);
    }

    /// <summary>
    /// Machine ids collide across the pre- and post-reflow DBs, so the source
    /// id has to be part of the key or a Line-2 filter would serve pre-reflow
    /// numbers from a post-reflow entry.
    /// </summary>
    [Fact]
    public async Task GetOrRun_WithADifferentSource_MissesAndRunsAgain()
    {
        using var cache = CreateCache();
        var report = new CountingReport();
        var filter = Filter([2]);

        await cache.GetOrRunAsync(report, new FakeAoiSource(_post), filter, CancellationToken.None);
        await cache.GetOrRunAsync(report, new FakeAoiSource(_pre), filter, CancellationToken.None);

        Assert.Equal(2, report.Runs);
    }

    [Fact]
    public async Task Disabled_NeverServesAStoredResult()
    {
        using var cache = CreateCache(enabled: false);
        var report = new CountingReport();
        var source = new FakeAoiSource(_post);
        var filter = Filter();

        cache.Store(report, source, filter, new CountedResult(99));
        await cache.GetOrRunAsync(report, source, filter, CancellationToken.None);

        Assert.Equal(1, report.Runs);
    }

    [Fact]
    public async Task ZeroTtl_NeverServesAStoredResult()
    {
        using var cache = CreateCache(ttlSeconds: 0);
        var report = new CountingReport();
        var source = new FakeAoiSource(_post);
        var filter = Filter();

        cache.Store(report, source, filter, new CountedResult(99));
        await cache.GetOrRunAsync(report, source, filter, CancellationToken.None);

        Assert.Equal(1, report.Runs);
    }

    private sealed record CountedResult(int Value);

    /// <summary>
    /// Report stand-in that returns a new value on every run, so a cache hit
    /// is observable both by the run count and by the returned value.
    /// </summary>
    private sealed class CountingReport : IReport<PanelYieldFilter, CountedResult>
    {
        public int Runs { get; private set; }

        public ReportDescriptor Descriptor { get; } = new(
            Id: "counting-test-report",
            DisplayName: "Counting test report",
            Category: ReportCategory.Table);

        public Task<CountedResult> RunAsync(
            IAoiSource source, PanelYieldFilter input, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult(new CountedResult(Runs));
        }
    }
}
