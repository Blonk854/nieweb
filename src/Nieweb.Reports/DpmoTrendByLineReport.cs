using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Common.Defects;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Reports;

/// <summary>
/// DPMO trend by line: one DPMO series per AOI machine, bucketed by day or
/// week over the requested window. Each point carries all three numerator
/// flavours (AOI / Real / Dummy) via <see cref="DpmoTrendKpi"/> so a client
/// can toggle between them without a refetch.
/// </summary>
/// <remarks>
/// <para>
/// This is <see cref="DpmoTableReport"/>'s two-pass aggregation re-keyed by
/// <c>(machineId, bucketIndex)</c>. Pass 1 streams
/// <see cref="IAoiSource.StreamCardsAsync"/> and sums the AOI's own
/// per-board inspection test counts to form the opportunity denominator.
/// Pass 2 streams <see cref="IAoiSource.StreamTestedObjectsAsync"/> and
/// counts defect bits for the numerator, with every bit-to-defect
/// translation routed through <see cref="DefectBitDecoder"/> so we cannot
/// re-introduce Vieweb bug <b>#11211</b> (wrong defect displayed).
/// </para>
/// <para>
/// The denominator MUST come from <c>CARDS</c>, never from a
/// <c>TESTED_OBJECT</c> row count. Counting rows collapses the opportunity
/// count to roughly the defect count and pins DPMO near the 1e6 ceiling —
/// validated against the HLYAOI archive, where component DPMO is ≈50.9 and
/// a row-count denominator yields ≈957 000.
/// </para>
/// <para>
/// Pass 2 sets <see cref="TestedObjectQuery.DefectsOnly"/>. Rows carrying no
/// defect bit popcount to zero in every flavour, so pruning them is
/// exact-parity for the numerator while collapsing the wire volume on the
/// pre-reflow v4.3.1 <c>TESTED_OBJECT</c> (which is not physically
/// defect-only). It is safe here precisely because the denominator does not
/// come from this stream.
/// </para>
/// <para>
/// Aggregation is count-first / divide-last: opportunity and defect-bit
/// counts accumulate as <see cref="long"/> and the DPMO ratio is computed
/// only at emit time, in <see cref="DpmoTrendKpi"/>. A week bucket therefore
/// equals the sum of its days (Vieweb bug #12421).
/// </para>
/// <para>
/// Unlike the DPMO table's reference-designator / part-number / JEDEC axes,
/// line × time is fully card-derivable, so <b>every</b> cell gets a correct
/// rate — nothing is suppressed.
/// </para>
/// </remarks>
public sealed class DpmoTrendByLineReport : IReport<DpmoTrendFilter, DpmoTrendResult>
{
    /// <summary>Stable metadata for this report.</summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "dpmo-trend",
        DisplayName: "DPMO Trend",
        Category: ReportCategory.Chart,
        Description: "Defects Per Million Opportunities over time (day / week) per AOI line.");

    /// <summary>Stateless singleton; safe to share across all callers.</summary>
    public static readonly DpmoTrendByLineReport Instance = new();

    // OBJECT_TYPE.Object_Type_Id bit codes (vit-aoi-database skill).
    private const int ObjectTypeComponentBit = 0x00000001;
    private const int ObjectTypePastePadBit = 0x00000010;

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    /// <remarks>The class-level remarks describe the aggregation contract.</remarks>
    public async Task<DpmoTrendResult> RunAsync(
        IAoiSource source,
        DpmoTrendFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Bucket is not (TimeBucket.Day or TimeBucket.Week))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Bucket,
                "DPMO trend supports only Day or Week buckets.");
        }

        var timeZone = filter.SiteTimeZone ?? TimeZoneInfo.Utc;
        var buckets = TimeBucketer.Decompose(
            filter.Window.StartUtc, filter.Window.EndUtcExclusive, filter.Bucket, timeZone);
        var bucketStartEpochs = buckets.Select(b => b.StartUtc.ToUnixTimeSeconds()).ToArray();

        // Cells keyed by (machine, bucket) for the chart points, and by
        // machine for the per-line window total. Every mutation is applied to
        // both, so a line total is always exactly the sum of its cells.
        var cells = new Dictionary<(int MachineId, int BucketIndex), Accumulator>();
        var lineOverall = new Dictionary<int, Accumulator>();

        // Resolves the (cell, line-total) pair a row belongs to, creating
        // either on first use. Null when the row falls outside every bucket.
        (Accumulator Cell, Accumulator Line)? Locate(int machineId, int panelNumericDate)
        {
            var bucketIndex = FindBucketIndex(buckets, bucketStartEpochs, panelNumericDate);
            if (bucketIndex < 0)
            {
                return null; // Row falls outside every bucket (edge of window).
            }
            var cellKey = (machineId, bucketIndex);
            if (!cells.TryGetValue(cellKey, out var cell))
            {
                cell = new Accumulator();
                cells[cellKey] = cell;
            }
            if (!lineOverall.TryGetValue(machineId, out var line))
            {
                line = new Accumulator();
                lineOverall[machineId] = line;
            }
            return (cell, line);
        }

        // Skip predicates: identical to DpmoTableReport so a Clean trend and
        // a Clean table cover the same board population. A dropped board must
        // leave BOTH passes, so pass 1 records its identity for pass 2.
        var config = filter.SkipConfig ?? SkipClassificationConfig.Default;
        var statusFilter = filter.SkipStatuses is { Count: > 0 }
            ? new HashSet<SkipClass>(filter.SkipStatuses)
            : null;
        var needsSkipIndex = filter.SkipExclusion == SkipExclusion.Clean || statusFilter is not null;
        var skipIndex = needsSkipIndex
            ? await SkipInputsIndex.BuildAsync(
                source, filter.Window, filter.MachineIds, filter.ProductIds,
                onlyLastInspection: true, config, cancellationToken).ConfigureAwait(false)
            : null;
        var skippedCards = skipIndex is null ? null : new HashSet<(long PanelId, int CardId)>();
        long skipExcludedCards = 0;

        bool KeepClass(SkipClass cls)
        {
            if (statusFilter is null)
            {
                return filter.SkipExclusion != SkipExclusion.Clean || cls == SkipClass.None;
            }
            return filter.SkipExclusion == SkipExclusion.Clean
                ? cls == SkipClass.None || statusFilter.Contains(cls)
                : statusFilter.Contains(cls);
        }

        var nogoProductIds = await NogoProducts.BuildAsync(
            source, filter.ExcludeNogo, cancellationToken).ConfigureAwait(false);

        // Paste opportunities only exist where the source records them. On a
        // post-reflow DB Nb_Of_Tests_On_Pads is absent, so an "All" trend
        // there is components-only rather than silently short a term.
        var hasPaste = source.Descriptor.Caps.HasFlag(Capabilities.PastePrintMetrics);

        // ---- Pass 1: opportunity denominator, streamed from CARDS. ----
        var cardQuery = new CardQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
            // Large pages: this report reads the whole window, so keyset
            // paging is pure overhead — minimise round trips.
            PageSize = 10_000,
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            if (nogoProductIds is not null && nogoProductIds.Contains(card.ProductId))
            {
                continue;
            }
            if (skipIndex is not null && !KeepClass(skipIndex.Classify(card, config)))
            {
                skippedCards!.Add((card.PanelId, card.CardIdOnPanel));
                skipExcludedCards++;
                continue;
            }
            var target = Locate(card.MachineId, card.PanelNumericDate);
            if (target is null)
            {
                continue;
            }
            var opportunities = OpportunityFor(card, filter.Opportunity, hasPaste);
            target.Value.Cell.AddOpportunities(opportunities);
            target.Value.Line.AddOpportunities(opportunities);
        }

        // ---- Pass 2: defect-bit numerator, streamed from TESTED_OBJECT. ----
        var objectQuery = new TestedObjectQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
            // Exact-parity pruning: a row with no defect bit adds zero to
            // every numerator. Safe because the denominator came from CARDS.
            DefectsOnly = true,
            PageSize = 10_000,
        };
        await foreach (var obj in source.StreamTestedObjectsAsync(objectQuery, cancellationToken).ConfigureAwait(false))
        {
            if (nogoProductIds is not null && nogoProductIds.Contains(obj.ProductId))
            {
                continue;
            }
            // Drop defects that live on a board the denominator already
            // dropped, so both halves of the ratio see the same population.
            if (skippedCards is not null && skippedCards.Contains((obj.PanelId, obj.CardIdOnPanel)))
            {
                continue;
            }
            // Honour the opportunity flavour: a components DPMO counts
            // component-object defects only, keeping the numerator consistent
            // with the Nb_Of_Tests_On_Comp denominator.
            if (!IsOpportunity(obj.ObjectTypeId, filter.Opportunity))
            {
                continue;
            }

            var target = Locate(obj.MachineId, obj.PanelNumericDate);
            if (target is null)
            {
                continue;
            }
            var aoi = DefectBitDecoder.CountBits(obj.ErrorTable);
            var real = DefectBitDecoder.CountBits(obj.ErrorTableAr);
            var dummy = DefectBitDecoder.CountBits(obj.ErrorTable & ~obj.ErrorTableAr);
            target.Value.Cell.AddDefects(aoi, real, dummy);
            target.Value.Line.AddDefects(aoi, real, dummy);
        }

        var machineNames = (await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.MachineId, m => (string?)m.MachineName);

        var bucketDtos = buckets
            .Select((b, i) => new DpmoTrendBucket(i, b.Label, b.StartUtc, b.EndUtcExclusive))
            .ToList();

        var lines = lineOverall.Keys
            .Select(machineId =>
            {
                var points = new List<DpmoTrendPoint>(buckets.Count);
                for (var i = 0; i < buckets.Count; i++)
                {
                    if (cells.TryGetValue((machineId, i), out var cell))
                    {
                        points.Add(new DpmoTrendPoint(i, cell.ToKpi()));
                    }
                }
                return new DpmoTrendLine(
                    MachineId: machineId,
                    MachineName: machineNames.TryGetValue(machineId, out var name) ? name : null,
                    Points: points,
                    Overall: lineOverall[machineId].ToKpi());
            })
            // Stable, human-friendly ordering: by name, then id for ties /
            // catalogue-missing machines.
            .OrderBy(l => l.MachineName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.MachineId)
            .ToList();

        return new DpmoTrendResult(
            Source: source.Descriptor,
            Window: filter.Window,
            Bucket: filter.Bucket,
            Opportunity: filter.Opportunity,
            SkipExclusion: filter.SkipExclusion,
            Buckets: bucketDtos,
            Lines: lines,
            SkipExcludedCards: skipExcludedCards);
    }

    private static bool IsOpportunity(int objectTypeId, DpmoOpportunity opportunity) => opportunity switch
    {
        DpmoOpportunity.All => true,
        DpmoOpportunity.Components => (objectTypeId & ObjectTypeComponentBit) != 0,
        DpmoOpportunity.Paste => (objectTypeId & ObjectTypePastePadBit) != 0,
        _ => false,
    };

    /// <summary>
    /// Card-level inspection opportunity count for the requested flavour —
    /// the canonical DPMO denominator. Paste tests only exist where the
    /// source advertises <see cref="Capabilities.PastePrintMetrics"/>.
    /// </summary>
    private static long OpportunityFor(CardRow card, DpmoOpportunity opportunity, bool hasPaste) => opportunity switch
    {
        DpmoOpportunity.Components => card.NbOfTestsOnComp,
        DpmoOpportunity.Paste => hasPaste ? card.NbOfTestsOnPads ?? 0 : 0,
        DpmoOpportunity.All => card.NbOfTestsOnComp + (hasPaste ? card.NbOfTestsOnPads ?? 0 : 0),
        _ => 0,
    };

    /// <summary>
    /// Binary search: index of the bucket whose half-open
    /// <c>[StartEpoch, next-start)</c> contains <paramref name="panelNumericDate"/>,
    /// or <c>-1</c> when the timestamp falls outside the last bucket's
    /// exclusive end.
    /// </summary>
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

    /// <summary>
    /// Mutable per-cell counter holding both halves of the DPMO ratio.
    /// Counts stay integral until <see cref="ToKpi"/> so the divide happens
    /// exactly once, at emit time.
    /// </summary>
    private sealed class Accumulator
    {
        private long _opportunities;
        private long _aoi;
        private long _real;
        private long _dummy;

        public void AddOpportunities(long opportunities) => _opportunities += opportunities;

        public void AddDefects(int aoi, int real, int dummy)
        {
            _aoi += aoi;
            _real += real;
            _dummy += dummy;
        }

        public DpmoTrendKpi ToKpi() => new(_opportunities, _aoi, _real, _dummy);
    }
}
