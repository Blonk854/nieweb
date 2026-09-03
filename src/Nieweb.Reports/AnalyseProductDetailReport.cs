using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Common.Defects;

namespace Nieweb.Reports;

/// <summary>
/// ANA-04 product drilldown slice: per-bucket FPY and DPMO trend for one
/// product, plus overall KPI and top defect bits in the selected window.
/// </summary>
public sealed class AnalyseProductDetailReport : IReport<AnalyseProductDetailFilter, AnalyseProductDetailResult>
{
    public static readonly AnalyseProductDetailReport Instance = new();

    private const int ObjectTypeComponentBit = 0x00000001;

    private AnalyseProductDetailReport()
    {
    }

    public ReportDescriptor Descriptor { get; } = new(
        Id: "analyse-product-detail",
        DisplayName: "Analyse Product Detail",
        Category: ReportCategory.Chart,
        Description: "Per-product FPY and DPMO trend with top defect bits.");

    public async Task<AnalyseProductDetailResult> RunAsync(
        IAoiSource source,
        AnalyseProductDetailFilter input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(input);

        if (input.Bucket is not (TimeBucket.Day or TimeBucket.Week))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.Bucket,
                "Product detail supports only Day or Week buckets.");
        }

        var supportsLastInspection = source.Descriptor.Caps.HasFlag(Capabilities.IsLastInspectionFilter);
        var useInMemoryDedupe = input.OnlyLastInspection && !supportsLastInspection;
        HashSet<int>? keptPanelIds = null;
        if (useInMemoryDedupe)
        {
            keptPanelIds = await BuildLatestPanelIdsAsync(
                source,
                input.Window,
                input.MachineIds,
                input.ProductId,
                cancellationToken).ConfigureAwait(false);
        }

        var timeZone = input.SiteTimeZone ?? TimeZoneInfo.Utc;
        var bucketRanges = TimeBucketer.Decompose(
            input.Window.StartUtc,
            input.Window.EndUtcExclusive,
            input.Bucket,
            timeZone);
        var bucketStarts = bucketRanges.Select(b => b.StartUtc.ToUnixTimeSeconds()).ToArray();

        var buckets = bucketRanges
            .Select((b, i) => new AnalyseProductTrendBucket(i, b.Label, b.StartUtc, b.EndUtcExclusive))
            .ToList();

        var yieldOverall = new PanelAccumulator();
        var dpmoOverall = new DpmoAccumulator();
        var defectCounts = new Dictionary<int, long>();

        var bucketCells = new Dictionary<int, BucketCell>();
        BucketCell EnsureBucket(int index)
        {
            if (!bucketCells.TryGetValue(index, out var cell))
            {
                cell = new BucketCell();
                bucketCells[index] = cell;
            }

            return cell;
        }

        var panelQuery = new PanelQuery
        {
            Window = input.Window,
            MachineIds = input.MachineIds,
            ProductIds = new HashSet<int> { input.ProductId },
            OnlyLastInspection = input.OnlyLastInspection,
        };
        await foreach (var panel in source.StreamPanelsAsync(panelQuery, cancellationToken).ConfigureAwait(false))
        {
            if (keptPanelIds is not null && !keptPanelIds.Contains(panel.PanelId))
            {
                continue;
            }

            var bucketIndex = FindBucketIndex(bucketRanges, bucketStarts, panel.PanelNumericDate);
            if (bucketIndex < 0)
            {
                continue;
            }

            yieldOverall.Add(panel.PanelStatus);
            EnsureBucket(bucketIndex).Yield.Add(panel.PanelStatus);
        }

        var cardQuery = new CardQuery
        {
            Window = input.Window,
            MachineIds = input.MachineIds,
            ProductIds = new HashSet<int> { input.ProductId },
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            if (keptPanelIds is not null && !keptPanelIds.Contains((int)card.PanelId))
            {
                continue;
            }

            var bucketIndex = FindBucketIndex(bucketRanges, bucketStarts, card.PanelNumericDate);
            if (bucketIndex < 0)
            {
                continue;
            }

            dpmoOverall.AddOpportunity(card.NbOfTestsOnComp);
            EnsureBucket(bucketIndex).Dpmo.AddOpportunity(card.NbOfTestsOnComp);
        }

        var objectQuery = new TestedObjectQuery
        {
            Window = input.Window,
            MachineIds = input.MachineIds,
            ProductIds = new HashSet<int> { input.ProductId },
            DefectsOnly = true,
        };
        await foreach (var obj in source.StreamTestedObjectsAsync(objectQuery, cancellationToken).ConfigureAwait(false))
        {
            if (keptPanelIds is not null && !keptPanelIds.Contains((int)obj.PanelId))
            {
                continue;
            }
            if ((obj.ObjectTypeId & ObjectTypeComponentBit) == 0)
            {
                continue;
            }

            var bucketIndex = FindBucketIndex(bucketRanges, bucketStarts, obj.PanelNumericDate);
            if (bucketIndex < 0)
            {
                continue;
            }

            var bits = CountBits(obj.ErrorTable);
            dpmoOverall.AddDefects(bits);
            var cell = EnsureBucket(bucketIndex);
            cell.Dpmo.AddDefects(bits);
            cell.DefectBitCount += bits;

            foreach (var defect in DefectBitDecoder.Decode(obj.ErrorTable))
            {
                defectCounts.TryGetValue(defect.BitNumber, out var current);
                defectCounts[defect.BitNumber] = current + 1;
                cell.AddDefectBit(defect.BitNumber);
            }
        }

        var productName = (await source.ListProductsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => p.ProductId == input.ProductId)
            ?.ProductName;

        var trend = buckets
            .Select(bucket =>
            {
                if (!bucketCells.TryGetValue(bucket.Index, out var cell))
                {
                    cell = new BucketCell();
                }

                return new AnalyseProductTrendPoint(
                    BucketIndex: bucket.Index,
                    Label: bucket.Label,
                    Yield: cell.Yield.ToKpi(),
                    Dpmo: cell.Dpmo.ToKpi(),
                    DefectBitCount: cell.DefectBitCount,
                    TopDefectBits: cell.TopDefectBits());
            })
            .ToList();

        var topDefectBits = defectCounts
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .Take(8)
            .Select(kvp => new AnalyseProductDefectCount(kvp.Key, kvp.Value))
            .ToList();

        return new AnalyseProductDetailResult(
            Source: source.Descriptor,
            Filter: input,
            ProductId: input.ProductId,
            ProductName: productName,
            OverallYield: yieldOverall.ToKpi(),
            OverallDpmo: dpmoOverall.ToKpi(),
            Buckets: buckets,
            Trend: trend,
            TopDefectBits: topDefectBits,
            DedupeAppliedInMemory: useInMemoryDedupe,
            DedupeNote: useInMemoryDedupe
                ? "Source lacks IS_LAST_INSPECTION; dedupe is applied in memory by panel id."
                : null);
    }

    private static async Task<HashSet<int>> BuildLatestPanelIdsAsync(
        IAoiSource source,
        DateRange window,
        IReadOnlyCollection<int>? machineIds,
        int productId,
        CancellationToken cancellationToken)
    {
        var latestByBarcodeFace = new Dictionary<(string Barcode, int Face), PanelRow>();
        var query = new PanelQuery
        {
            Window = window,
            MachineIds = machineIds,
            ProductIds = new HashSet<int> { productId },
            OnlyLastInspection = false,
        };

        await foreach (var panel in source.StreamPanelsAsync(query, cancellationToken).ConfigureAwait(false))
        {
            var key = (panel.PanelBarCode, panel.FaceNumber ?? -1);
            if (!latestByBarcodeFace.TryGetValue(key, out var previous)
                || panel.PanelNumericDate > previous.PanelNumericDate
                || (panel.PanelNumericDate == previous.PanelNumericDate && panel.PanelId > previous.PanelId))
            {
                latestByBarcodeFace[key] = panel;
            }
        }

        return latestByBarcodeFace.Values.Select(p => p.PanelId).ToHashSet();
    }

    private static int FindBucketIndex(
        IReadOnlyList<TimeBucketRange> buckets,
        long[] bucketStartEpochs,
        int panelNumericDate)
    {
        long epoch = panelNumericDate;
        var idx = Array.BinarySearch(bucketStartEpochs, epoch);
        if (idx < 0)
        {
            idx = ~idx - 1;
            if (idx < 0)
            {
                return -1;
            }
        }

        return epoch < buckets[idx].EndUtcExclusive.ToUnixTimeSeconds() ? idx : -1;
    }

    private static long CountBits(long value)
    {
        var count = 0L;
        var remaining = unchecked((ulong)value);
        while (remaining != 0)
        {
            remaining &= remaining - 1;
            count++;
        }

        return count;
    }

    private sealed class BucketCell
    {
        public PanelAccumulator Yield { get; } = new();
        public DpmoAccumulator Dpmo { get; } = new();
        public long DefectBitCount { get; set; }
        private readonly Dictionary<int, long> _defectCounts = new();

        public void AddDefectBit(int bitNumber)
        {
            _defectCounts.TryGetValue(bitNumber, out var current);
            _defectCounts[bitNumber] = current + 1;
        }

        public List<AnalyseProductDefectCount> TopDefectBits() => _defectCounts
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .Take(3)
            .Select(kvp => new AnalyseProductDefectCount(kvp.Key, kvp.Value))
            .ToList();
    }

    private sealed class PanelAccumulator
    {
        private long _good;
        private long _faulty;
        private long _notInspected;

        public void Add(int panelStatus)
        {
            switch (panelStatus)
            {
                case 1 or 2 or 3:
                    _good++;
                    break;
                case -2 or -1:
                    _faulty++;
                    break;
                case 0:
                    _notInspected++;
                    break;
                default:
                    _notInspected++;
                    break;
            }
        }

        public PanelYieldKpi ToKpi()
        {
            var inspected = _good + _faulty;
            var total = inspected + _notInspected;
            var fpy = inspected == 0 ? 0d : 100d * _good / inspected;
            return new PanelYieldKpi(total, inspected, _good, _faulty, _notInspected, fpy);
        }
    }

    private sealed class DpmoAccumulator
    {
        private long _testedObjects;
        private long _opportunities;
        private long _defects;

        public void AddOpportunity(long opportunities) => _opportunities += opportunities;

        public void AddDefects(long defects)
        {
            _testedObjects++;
            _defects += defects;
        }

        public DpmoKpi ToKpi() => new(
            _testedObjects,
            _opportunities,
            _defects,
            _opportunities == 0 ? 0d : 1_000_000d * _defects / _opportunities);
    }
}

public sealed record AnalyseProductDetailFilter(
    DateRange Window,
    int ProductId,
    TimeBucket Bucket = TimeBucket.Day,
    TimeZoneInfo? SiteTimeZone = null,
    IReadOnlyCollection<int>? MachineIds = null,
    bool OnlyLastInspection = true);

public sealed record AnalyseProductTrendBucket(
    int Index,
    string Label,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtcExclusive);

public sealed record AnalyseProductTrendPoint(
    int BucketIndex,
    string Label,
    PanelYieldKpi Yield,
    DpmoKpi Dpmo,
    long DefectBitCount,
    IReadOnlyList<AnalyseProductDefectCount> TopDefectBits);

public sealed record AnalyseProductDetailResult(
    SourceDescriptor Source,
    AnalyseProductDetailFilter Filter,
    int ProductId,
    string? ProductName,
    PanelYieldKpi OverallYield,
    DpmoKpi OverallDpmo,
    IReadOnlyList<AnalyseProductTrendBucket> Buckets,
    IReadOnlyList<AnalyseProductTrendPoint> Trend,
    IReadOnlyList<AnalyseProductDefectCount> TopDefectBits,
    bool DedupeAppliedInMemory,
    string? DedupeNote);
