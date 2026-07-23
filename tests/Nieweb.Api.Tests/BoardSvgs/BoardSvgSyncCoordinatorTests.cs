using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Nieweb.Api.Audit;
using Nieweb.Api.BoardSvgs;
using Nieweb.Api.Tests.Fakes;
using Nieweb.DataSources;

using Xunit;

namespace Nieweb.Api.Tests.BoardSvgs;

/// <summary>
/// Unit tests for <see cref="BoardSvgSyncCoordinator"/>
/// (docs/phase-2.md §7.5 <c>TC4</c> Phase B). Uses
/// <see cref="FakeBoardSvgFileSystem"/> so no real disk is touched
/// and copies can be introspected byte-for-byte.
/// </summary>
public sealed class BoardSvgSyncCoordinatorTests
{
    private static readonly SourceDescriptor PostDescriptor = new("postreflow", "Post-Reflow", "5.0", Capabilities.PinLevel);
    private static readonly SourceDescriptor PreDescriptor = new("prereflow", "Pre-Reflow", "4.3.1", Capabilities.None);
    private const string CacheDir = @"C:\test-cache\board-svgs";
    private const string SourceAPath = @"\\aoi-a\svg";
    private const string SourceBPath = @"\\aoi-b\svg";

    private static BoardSvgSyncCoordinator BuildCoordinator(
        FakeBoardSvgSources sources,
        FakeBoardSvgFileSystem fs,
        FakeAuditLog audit,
        params IAoiSource[] aoi)
    {
        var options = Options.Create(new BoardSvgSyncOptions
        {
            CacheDirectory = CacheDir,
            IntervalSeconds = 3600,
            Enabled = true,
        });
        return new BoardSvgSyncCoordinator(
            sources,
            aoi,
            fs,
            audit,
            options,
            TimeProvider.System,
            NullLogger<BoardSvgSyncCoordinator>.Instance);
    }

    private static FakeAoiSource NewAoi(SourceDescriptor descriptor, params string[] productNames)
    {
        var products = productNames
            .Select((n, i) => new Product(i + 1, n, Revision: null, Description: null))
            .ToList();
        return new FakeAoiSource(descriptor)
        {
            SeededProducts = products,
        };
    }

    [Fact]
    public async Task SyncOnce_WithNoSourcesAndNoProducts_Succeeds()
    {
        var sources = new FakeBoardSvgSources();
        var fs = new FakeBoardSvgFileSystem();
        var audit = new FakeAuditLog();
        var coord = BuildCoordinator(sources, fs, audit);

        var result = await coord.SyncOnceAsync(CancellationToken.None);

        Assert.Empty(result.Sources);
        Assert.Empty(result.Products);
        Assert.Empty(audit.Entries);
        Assert.Empty(sources.RecordedSuccesses);
        Assert.Empty(sources.RecordedFailures);
        Assert.True(fs.DirectoryExists(CacheDir));
    }

    [Fact]
    public async Task SyncOnce_CopiesNewestFilePerProduct()
    {
        var sources = new FakeBoardSvgSources();
        var srcA = sources.Seed("AOI-A", SourceAPath);
        var srcB = sources.Seed("AOI-B", SourceBPath);

        var fs = new FakeBoardSvgFileSystem();
        // Source A has an OLDER copy of HA010522401_1st.
        fs.AddFile(SourceAPath, "HA010522401_1st.svg", "old"u8.ToArray(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        // Source B has a NEWER copy — should win.
        fs.AddFile(SourceBPath, "HA010522401_1st.svg", "new"u8.ToArray(), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        // Source A also has HA010522401_2nd — only A has it.
        fs.AddFile(SourceAPath, "HA010522401_2nd.svg", "second"u8.ToArray(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var audit = new FakeAuditLog();
        var aoi = NewAoi(PostDescriptor, "HA010522401_1st", "HA010522401_2nd");
        var coord = BuildCoordinator(sources, fs, audit, aoi);

        var result = await coord.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(2, result.Sources.Count);
        Assert.All(result.Sources, s => Assert.True(s.Reachable));

        // Products ordered alphabetically.
        Assert.Equal(2, result.Products.Count);
        var first = result.Products.Single(p => p.ProductName == "HA010522401_1st");
        Assert.True(first.Copied);
        Assert.Equal("AOI-B", first.SourceMachineName);
        Assert.Equal(3L, first.BytesCopied);

        var second = result.Products.Single(p => p.ProductName == "HA010522401_2nd");
        Assert.True(second.Copied);
        Assert.Equal("AOI-A", second.SourceMachineName);

        // Cache contents must match the newest source.
        var written1 = await fs.ReadAllBytesAsync(Path.Combine(CacheDir, "HA010522401_1st.svg"), CancellationToken.None);
        Assert.Equal("new"u8.ToArray(), written1);

        // Audit: two BoardSvgSynced events, zero BoardSvgSyncFailed.
        Assert.Equal(2, audit.Entries.Count(e => e.EventType == AuditEventTypes.BoardSvgSynced));
        Assert.DoesNotContain(audit.Entries, e => e.EventType == AuditEventTypes.BoardSvgSyncFailed);

        // Both reachable sources got a success ping.
        Assert.Equal(2, sources.RecordedSuccesses.Count);
        Assert.Contains(srcA.Id, sources.RecordedSuccesses);
        Assert.Contains(srcB.Id, sources.RecordedSuccesses);
    }

    [Fact]
    public async Task SyncOnce_SkipsProductsAlreadyCached()
    {
        var sources = new FakeBoardSvgSources();
        _ = sources.Seed("AOI-A", SourceAPath);

        var fs = new FakeBoardSvgFileSystem();
        fs.AddFile(SourceAPath, "HA010522401_1st.svg", "src"u8.ToArray(), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        // Cache already has a copy of the same product.
        fs.AddFile(CacheDir, "HA010522401_1st.svg", "cached"u8.ToArray(), new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var audit = new FakeAuditLog();
        var aoi = NewAoi(PostDescriptor, "HA010522401_1st");
        var coord = BuildCoordinator(sources, fs, audit, aoi);

        var initialWrites = fs.WriteCount;
        var result = await coord.SyncOnceAsync(CancellationToken.None);

        var product = Assert.Single(result.Products);
        Assert.True(product.AlreadyCached);
        Assert.False(product.Copied);
        Assert.Null(product.Error);

        // No new writes, no audit events for this product.
        Assert.Equal(initialWrites, fs.WriteCount);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task SyncOnce_UnreachableSource_RecordsFailureAndSkipsIt()
    {
        var sources = new FakeBoardSvgSources();
        var srcA = sources.Seed("AOI-A", SourceAPath);
        var srcB = sources.Seed("AOI-B", SourceBPath);

        var fs = new FakeBoardSvgFileSystem();
        // Source A unreachable; source B has the file.
        fs.MakeDirectoryUnreachable(SourceAPath);
        fs.AddFile(SourceBPath, "HA010522401_1st.svg", "ok"u8.ToArray(), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var audit = new FakeAuditLog();
        var aoi = NewAoi(PostDescriptor, "HA010522401_1st");
        var coord = BuildCoordinator(sources, fs, audit, aoi);

        var result = await coord.SyncOnceAsync(CancellationToken.None);

        var aOutcome = result.Sources.Single(s => s.MachineName == "AOI-A");
        Assert.False(aOutcome.Reachable);
        Assert.NotNull(aOutcome.Error);

        var bOutcome = result.Sources.Single(s => s.MachineName == "AOI-B");
        Assert.True(bOutcome.Reachable);
        Assert.Null(bOutcome.Error);

        // Product still copied (from the reachable source).
        var product = Assert.Single(result.Products);
        Assert.True(product.Copied);
        Assert.Equal("AOI-B", product.SourceMachineName);

        // Repo transitions: A got a failure, B got a success.
        Assert.Contains(srcA.Id, sources.RecordedFailures.Select(f => f.Id));
        Assert.Contains(srcB.Id, sources.RecordedSuccesses);
    }

    [Fact]
    public async Task SyncOnce_ProductWithNoMatchingFile_IsNotAnError()
    {
        var sources = new FakeBoardSvgSources();
        _ = sources.Seed("AOI-A", SourceAPath);

        var fs = new FakeBoardSvgFileSystem();
        // Source directory exists but has no files.
        fs.EnsureDirectoryExists(SourceAPath);

        var audit = new FakeAuditLog();
        var aoi = NewAoi(PostDescriptor, "HA010522401_1st");
        var coord = BuildCoordinator(sources, fs, audit, aoi);

        var result = await coord.SyncOnceAsync(CancellationToken.None);

        var product = Assert.Single(result.Products);
        Assert.False(product.Copied);
        Assert.False(product.AlreadyCached);
        Assert.Null(product.Error); // "not yet available" is not an error.
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task SyncOnce_SkipsDisabledSources()
    {
        var sources = new FakeBoardSvgSources();
        var srcA = sources.Seed("AOI-A", SourceAPath, isEnabled: false);
        _ = sources.Seed("AOI-B", SourceBPath);

        var fs = new FakeBoardSvgFileSystem();
        // Even though A has the file, it's disabled — should not be used.
        fs.AddFile(SourceAPath, "HA010522401_1st.svg", "a"u8.ToArray(), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile(SourceBPath, "HA010522401_1st.svg", "b"u8.ToArray(), new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        var audit = new FakeAuditLog();
        var aoi = NewAoi(PostDescriptor, "HA010522401_1st");
        var coord = BuildCoordinator(sources, fs, audit, aoi);

        var result = await coord.SyncOnceAsync(CancellationToken.None);

        var aOutcome = result.Sources.Single(s => s.MachineName == "AOI-A");
        Assert.False(aOutcome.Enabled);
        Assert.False(aOutcome.Reachable);

        var product = Assert.Single(result.Products);
        Assert.True(product.Copied);
        // Must have picked B (only enabled source), not the newer A file.
        Assert.Equal("AOI-B", product.SourceMachineName);

        var cached = await fs.ReadAllBytesAsync(Path.Combine(CacheDir, "HA010522401_1st.svg"), CancellationToken.None);
        Assert.Equal("b"u8.ToArray(), cached);

        // Disabled source must not get a success/failure ping.
        Assert.DoesNotContain(srcA.Id, sources.RecordedSuccesses);
        Assert.DoesNotContain(srcA.Id, sources.RecordedFailures.Select(f => f.Id));
    }

    [Fact]
    public async Task SyncOnce_UnionsProductsAcrossAoiSources()
    {
        var sources = new FakeBoardSvgSources();
        _ = sources.Seed("AOI-A", SourceAPath);

        var fs = new FakeBoardSvgFileSystem();
        fs.AddFile(SourceAPath, "ProdX.svg", "x"u8.ToArray(), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile(SourceAPath, "ProdY.svg", "y"u8.ToArray(), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var audit = new FakeAuditLog();
        var post = NewAoi(PostDescriptor, "ProdX");
        var pre = NewAoi(PreDescriptor, "ProdY", "ProdX"); // ProdX appears in both — should dedupe.
        var coord = BuildCoordinator(sources, fs, audit, post, pre);

        var result = await coord.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(2, result.Products.Count);
        Assert.Contains(result.Products, p => p.ProductName == "ProdX" && p.Copied);
        Assert.Contains(result.Products, p => p.ProductName == "ProdY" && p.Copied);
    }

    [Fact]
    public async Task SyncOnce_ProductNameWithInvalidChars_IsSkipped()
    {
        var sources = new FakeBoardSvgSources();
        _ = sources.Seed("AOI-A", SourceAPath);

        var fs = new FakeBoardSvgFileSystem();
        var audit = new FakeAuditLog();

        // Bad names must NEVER become filesystem operations — a
        // hostile product name like "..\..\evil" cannot escape the
        // cache directory.
        var aoi = NewAoi(PostDescriptor, "..\\..\\evil", "legit-product");
        fs.AddFile(SourceAPath, "legit-product.svg", "ok"u8.ToArray(), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));

        var coord = BuildCoordinator(sources, fs, audit, aoi);

        var result = await coord.SyncOnceAsync(CancellationToken.None);

        // Only the legit product should surface in outcomes.
        var product = Assert.Single(result.Products);
        Assert.Equal("legit-product", product.ProductName);
        Assert.True(product.Copied);
    }

    [Fact]
    public async Task SyncOnce_AuditPayload_ContainsProductAndSourceMetadata()
    {
        var sources = new FakeBoardSvgSources();
        _ = sources.Seed("AOI-A", SourceAPath);

        var fs = new FakeBoardSvgFileSystem();
        fs.AddFile(SourceAPath, "HA-1.svg", "abc"u8.ToArray(), new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        var audit = new FakeAuditLog();
        var aoi = NewAoi(PostDescriptor, "HA-1");
        var coord = BuildCoordinator(sources, fs, audit, aoi);

        _ = await coord.SyncOnceAsync(CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditEventTypes.BoardSvgSynced, entry.EventType);
        Assert.Equal(AuditTargetTypes.BoardSvg, entry.TargetType);
        Assert.Equal("HA-1", entry.TargetId);
    }
}
