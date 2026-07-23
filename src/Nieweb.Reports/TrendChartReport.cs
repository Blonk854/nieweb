using Nieweb.DataSources;
using Nieweb.Reports.Common;
using Nieweb.Reports.Common.Defects;

namespace Nieweb.Reports;

/// <summary>
/// Trend chart (CR3 in docs/phase-2.md §7.3): plots any subset of
/// <see cref="TrendMetric"/> values over the requested time bucket
/// (1h / 3h / 6h / 12h / shift / day / week / month). Reuses
/// <see cref="TimeBucketer"/> for bucket decomposition and the same
/// panel / card / tested-object streaming pattern as the FPY, DPMO,
/// and Pareto reports so its numeric output matches those reports
/// bucket-for-bucket over identical windows and filters.
/// </summary>
/// <remarks>
/// <para>
/// The report streams only the sources it needs:
/// <see cref="IAoiSource.StreamPanelsAsync"/> for FPY / PanelCount,
/// <see cref="IAoiSource.StreamCardsAsync"/> for BoardCount, and
/// <see cref="IAoiSource.StreamTestedObjectsAsync"/> for DPMO /
/// DefectCount / Cp / Cpk. Streams are pulled in sequence to keep
/// the pattern predictable — heavy time windows should be split by
/// the caller rather than parallelised at this layer, so the read-
/// only discipline against the AOI Superviseur DB
/// (cycle-time-sensitive) is preserved.
/// </para>
/// <para>
/// Every row is routed to at most one bucket via a
/// binary search over the pre-computed bucket start epochs (O(log n)
/// per row). Buckets outside the window boundary silently drop the
/// row — this mirrors <see cref="ParetoReport"/> and prevents
/// off-by-one contributions at window edges.
/// </para>
/// <para>
/// Standard deviation for Cp / Cpk uses Welford's online algorithm
/// (Bessel-corrected, sample stddev) so per-bucket memory stays
/// constant regardless of the sample size. A bucket with fewer than
/// two deviation samples emits <c>null</c> for both Cp and Cpk (the
/// chart draws a gap rather than a misleading 0). A bucket with zero
/// opportunities emits <c>null</c> for DPMO metrics for the same
/// reason; a bucket with zero inspected panels emits <c>null</c> for
/// FPY metrics.
/// </para>
/// </remarks>
public sealed class TrendChartReport : IReport<TrendFilter, TrendResult>
{
    /// <summary>Stable metadata for this report.</summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "trend-chart",
        DisplayName: "Trend chart",
        Category: ReportCategory.Chart,
        Description: "Trend chart over time buckets (Cp, Cpk, DPMO, FPY, panel / board counts).");

    /// <summary>Stateless singleton; safe to share across all callers.</summary>
    public static readonly TrendChartReport Instance = new();

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    public async Task<TrendResult> RunAsync(
        IAoiSource source,
        TrendFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);

        // ---- Validation ---------------------------------------------------
        if (!Enum.IsDefined(filter.Bucket))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Bucket, "Unknown TimeBucket value.");
        }
        if (!Enum.IsDefined(filter.Numerator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Numerator, "Unknown DpmoNumerator value.");
        }
        if (!Enum.IsDefined(filter.Opportunity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Opportunity, "Unknown DpmoOpportunity value.");
        }
        if (filter.Metrics is null || filter.Metrics.Count == 0)
        {
            throw new ArgumentException(
                "TrendFilter.Metrics must contain at least one metric.",
                nameof(filter));
        }
        foreach (var m in filter.Metrics)
        {
            if (!Enum.IsDefined(m))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(filter), m, "Unknown TrendMetric value.");
            }
        }
        if (filter.LowerTolerance is double lo && filter.UpperTolerance is double hi && lo >= hi)
        {
            throw new ArgumentException(
                "LowerTolerance must be strictly less than UpperTolerance when both are supplied.",
                nameof(filter));
        }

        // Deduplicate metrics while preserving insertion order so
        // series ordering is deterministic for snapshot tests.
        var seenMetrics = new HashSet<TrendMetric>();
        var metrics = new List<TrendMetric>(filter.Metrics.Count);
        foreach (var m in filter.Metrics)
        {
            if (seenMetrics.Add(m))
            {
                metrics.Add(m);
            }
        }

        var needsDeviationAxis =
            metrics.Contains(TrendMetric.Cp) || metrics.Contains(TrendMetric.Cpk);
        if (needsDeviationAxis)
        {
            if (filter.DeviationAxis is null)
            {
                throw new ArgumentException(
                    "TrendFilter.DeviationAxis is required when Cp or Cpk is requested.",
                    nameof(filter));
            }
            if (!Enum.IsDefined(filter.DeviationAxis.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(filter), filter.DeviationAxis, "Unknown DeviationAxis value.");
            }
            if (metrics.Contains(TrendMetric.Cp)
                && (filter.LowerTolerance is null || filter.UpperTolerance is null))
            {
                throw new ArgumentException(
                    "TrendMetric.Cp requires BOTH LowerTolerance and UpperTolerance.",
                    nameof(filter));
            }
            if (metrics.Contains(TrendMetric.Cpk)
                && filter.LowerTolerance is null && filter.UpperTolerance is null)
            {
                throw new ArgumentException(
                    "TrendMetric.Cpk requires at least one of LowerTolerance or UpperTolerance.",
                    nameof(filter));
            }
        }

        // ---- Bucket decomposition -----------------------------------------
        var timeZone = filter.SiteTimeZone ?? TimeZoneInfo.Utc;
        var buckets = TimeBucketer.Decompose(
            filter.Window.StartUtc,
            filter.Window.EndUtcExclusive,
            filter.Bucket,
            timeZone,
            filter.Shifts);
        var bucketStartEpochs = new long[buckets.Count];
        for (var i = 0; i < buckets.Count; i++)
        {
            bucketStartEpochs[i] = buckets[i].StartUtc.ToUnixTimeSeconds();
        }
        var accumulators = new BucketAccumulator[buckets.Count];
        for (var i = 0; i < accumulators.Length; i++)
        {
            accumulators[i] = new BucketAccumulator();
        }

        // ---- Metric categorisation ---------------------------------------
        var needsPanels = metrics.Any(IsPanelMetric);
        var needsCards = metrics.Contains(TrendMetric.BoardCount);
        var needsTestedObjects = metrics.Any(IsTestedObjectMetric);

        // Fast-lookup sets for in-memory narrowing filters (used only
        // on tested-object streaming since Topology / PartNumber /
        // Jedec live on that row).
        var topologySet = ToOrdinalSet(filter.Topologies);
        var partNumberSet = ToOrdinalSet(filter.PartNumbers);
        var jedecSet = ToOrdinalSet(filter.JedecNames);

        // ---- Panels ------------------------------------------------------
        if (needsPanels)
        {
            var panelQuery = new PanelQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
                OnlyLastInspection = filter.OnlyLastInspection,
            };
            await foreach (var panel in source.StreamPanelsAsync(panelQuery, cancellationToken).ConfigureAwait(false))
            {
                var idx = FindBucketIndex(bucketStartEpochs, panel.PanelNumericDate, buckets);
                if (idx < 0)
                {
                    continue;
                }
                accumulators[idx].AddPanel(panel.PanelStatus);
            }
        }

        // ---- Cards (boards) ---------------------------------------------
        if (needsCards)
        {
            var cardQuery = new CardQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
            };
            await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
            {
                var idx = FindBucketIndex(bucketStartEpochs, card.PanelNumericDate, buckets);
                if (idx < 0)
                {
                    continue;
                }
                accumulators[idx].AddCard();
            }
        }

        // ---- Tested objects ---------------------------------------------
        if (needsTestedObjects)
        {
            var testedObjectQuery = new TestedObjectQuery
            {
                Window = filter.Window,
                MachineIds = filter.MachineIds,
                ProductIds = filter.ProductIds,
            };
            var wantsDeviation = needsDeviationAxis;
            var axis = filter.DeviationAxis;
            await foreach (var obj in source.StreamTestedObjectsAsync(testedObjectQuery, cancellationToken).ConfigureAwait(false))
            {
                if (topologySet is not null && (obj.Topology is null || !topologySet.Contains(obj.Topology)))
                {
                    continue;
                }
                if (partNumberSet is not null && (obj.PartNumberName is null || !partNumberSet.Contains(obj.PartNumberName)))
                {
                    continue;
                }
                if (jedecSet is not null && (obj.JedecName is null || !jedecSet.Contains(obj.JedecName)))
                {
                    continue;
                }
                var isOpportunity = IsOpportunity(obj.ObjectTypeId, filter.Opportunity);
                if (!isOpportunity && !wantsDeviation)
                {
                    // Nothing to attribute to any tested-object metric.
                    continue;
                }

                var idx = FindBucketIndex(bucketStartEpochs, obj.PanelNumericDate, buckets);
                if (idx < 0)
                {
                    continue;
                }
                var acc = accumulators[idx];
                if (isOpportunity)
                {
                    var aoiBits = DefectBitDecoder.CountBits(obj.ErrorTable);
                    var realBits = DefectBitDecoder.CountBits(obj.ErrorTableAr);
                    var dummyBits = DefectBitDecoder.CountBits(obj.ErrorTable & ~obj.ErrorTableAr);
                    acc.AddOpportunity(aoiBits, realBits, dummyBits);
                }

                if (wantsDeviation)
                {
                    var v = ProjectAxis(obj, axis!.Value);
                    if (v is double d && !double.IsNaN(d) && !double.IsInfinity(d))
                    {
                        acc.AddDeviationSample(d);
                    }
                }
            }
        }

        // ---- Series metadata --------------------------------------------
        var series = new List<TrendSeries>(metrics.Count);
        foreach (var m in metrics)
        {
            series.Add(new TrendSeries(m, DisplayName(m), UnitFor(m)));
        }

        // ---- Build bucket points ----------------------------------------
        var points = new List<TrendBucketPoint>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            var acc = accumulators[i];
            var values = new Dictionary<TrendMetric, double?>(metrics.Count);
            foreach (var m in metrics)
            {
                values[m] = ComputeMetric(m, acc, filter);
            }
            points.Add(new TrendBucketPoint(
                Label: bucket.Label,
                StartUtc: bucket.StartUtc,
                EndUtcExclusive: bucket.EndUtcExclusive,
                ShiftIndex: bucket.ShiftIndex,
                Values: values));
        }

        return new TrendResult(
            Source: source.Descriptor,
            Window: filter.Window,
            Bucket: filter.Bucket,
            Numerator: filter.Numerator,
            Opportunity: filter.Opportunity,
            DeviationAxis: filter.DeviationAxis,
            LowerTolerance: filter.LowerTolerance,
            UpperTolerance: filter.UpperTolerance,
            AppliedFilters: EchoAppliedFilters(filter),
            Series: series,
            Buckets: points);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private const int ObjectTypeComponentBit = 0x01;
    private const int ObjectTypePastePadBit = 0x10;

    private static bool IsPanelMetric(TrendMetric m) => m
        is TrendMetric.FpyAoi
        or TrendMetric.FpyDiagnostic
        or TrendMetric.FpyAfterRepair
        or TrendMetric.PanelCount;

    private static bool IsTestedObjectMetric(TrendMetric m) => m
        is TrendMetric.DpmoAoi
        or TrendMetric.DpmoReal
        or TrendMetric.DpmoDummy
        or TrendMetric.DefectCount
        or TrendMetric.Cp
        or TrendMetric.Cpk;

    private static bool IsOpportunity(int objectTypeId, DpmoOpportunity opportunity) => opportunity switch
    {
        DpmoOpportunity.All => true,
        DpmoOpportunity.Components => (objectTypeId & ObjectTypeComponentBit) != 0,
        DpmoOpportunity.Paste => (objectTypeId & ObjectTypePastePadBit) != 0,
        _ => false,
    };

    private static double? ProjectAxis(TestedObjectRow obj, DeviationAxis axis) => axis switch
    {
        Reports.DeviationAxis.DeltaX => obj.DeltaXUm,
        Reports.DeviationAxis.DeltaY => obj.DeltaYUm,
        Reports.DeviationAxis.DeltaTheta => obj.DeltaThetaDeg,
        Reports.DeviationAxis.DeltaThickness => obj.DeltaThicknessUm,
        Reports.DeviationAxis.DeltaSurface => obj.DeltaSurface,
        _ => null,
    };

    private static HashSet<string>? ToOrdinalSet(IReadOnlyCollection<string>? source)
        => source is null || source.Count == 0
            ? null
            : new HashSet<string>(source, StringComparer.Ordinal);

    /// <summary>
    /// Binary search: returns the index of the bucket whose half-open
    /// [StartEpochSeconds, next-bucket start) contains
    /// <paramref name="panelNumericDate"/>, or a negative value when
    /// the timestamp is outside every bucket.
    /// </summary>
    private static int FindBucketIndex(
        long[] bucketStartEpochs,
        int panelNumericDate,
        IReadOnlyList<TimeBucketRange> buckets)
    {
        long panelEpoch = panelNumericDate;
        var idx = Array.BinarySearch(bucketStartEpochs, panelEpoch);
        if (idx < 0)
        {
            idx = ~idx - 1;
            if (idx < 0)
            {
                return -1;
            }
        }
        return panelEpoch < buckets[idx].EndUtcExclusive.ToUnixTimeSeconds() ? idx : -1;
    }

    private static string DisplayName(TrendMetric m) => m switch
    {
        TrendMetric.FpyAoi => "FPY (AOI)",
        TrendMetric.FpyDiagnostic => "FPY (Diagnostic)",
        TrendMetric.FpyAfterRepair => "FPY (After Repair)",
        TrendMetric.DpmoAoi => "DPMO (AOI)",
        TrendMetric.DpmoReal => "DPMO (Real)",
        TrendMetric.DpmoDummy => "DPMO (Dummy)",
        TrendMetric.PanelCount => "Panels",
        TrendMetric.BoardCount => "Boards",
        TrendMetric.DefectCount => "Defects",
        TrendMetric.Cp => "Cp",
        TrendMetric.Cpk => "Cpk",
        _ => m.ToString(),
    };

    private static string UnitFor(TrendMetric m) => m switch
    {
        TrendMetric.FpyAoi or TrendMetric.FpyDiagnostic or TrendMetric.FpyAfterRepair => "%",
        TrendMetric.DpmoAoi or TrendMetric.DpmoReal or TrendMetric.DpmoDummy => "ppm",
        TrendMetric.PanelCount or TrendMetric.BoardCount or TrendMetric.DefectCount => "count",
        _ => string.Empty,
    };

    private static double? ComputeMetric(TrendMetric m, BucketAccumulator acc, TrendFilter filter) => m switch
    {
        TrendMetric.FpyAoi => acc.PanelsInspected == 0
            ? null
            : 100d * acc.GoodAoi / acc.PanelsInspected,
        TrendMetric.FpyDiagnostic => acc.PanelsInspected == 0
            ? null
            : 100d * (acc.GoodAoi + acc.GoodDummyOnly) / acc.PanelsInspected,
        TrendMetric.FpyAfterRepair => acc.PanelsInspected == 0
            ? null
            : 100d * (acc.GoodAoi + acc.GoodDummyOnly + acc.GoodRepaired) / acc.PanelsInspected,
        TrendMetric.DpmoAoi => acc.Opportunities == 0
            ? null
            : 1_000_000d * acc.DefectsAoi / acc.Opportunities,
        TrendMetric.DpmoReal => acc.Opportunities == 0
            ? null
            : 1_000_000d * acc.DefectsReal / acc.Opportunities,
        TrendMetric.DpmoDummy => acc.Opportunities == 0
            ? null
            : 1_000_000d * acc.DefectsDummy / acc.Opportunities,
        TrendMetric.PanelCount => acc.PanelsTotal,
        TrendMetric.BoardCount => acc.CardsTotal,
        TrendMetric.DefectCount => filter.Numerator switch
        {
            DpmoNumerator.Aoi => acc.DefectsAoi,
            DpmoNumerator.Real => acc.DefectsReal,
            DpmoNumerator.Dummy => acc.DefectsDummy,
            _ => 0d,
        },
        TrendMetric.Cp => ComputeCp(acc, filter),
        TrendMetric.Cpk => ComputeCpk(acc, filter),
        _ => null,
    };

    private static double? ComputeCp(BucketAccumulator acc, TrendFilter filter)
    {
        if (acc.DeviationCount < 2 || filter.LowerTolerance is not double lo || filter.UpperTolerance is not double hi)
        {
            return null;
        }
        var stdDev = Math.Sqrt(acc.DeviationM2 / (acc.DeviationCount - 1));
        return stdDev <= 0d ? null : (hi - lo) / (6d * stdDev);
    }

    private static double? ComputeCpk(BucketAccumulator acc, TrendFilter filter)
    {
        if (acc.DeviationCount < 2)
        {
            return null;
        }
        var stdDev = Math.Sqrt(acc.DeviationM2 / (acc.DeviationCount - 1));
        if (stdDev <= 0d)
        {
            return null;
        }
        var mean = acc.DeviationMean;
        double? upper = filter.UpperTolerance is double hi
            ? (hi - mean) / (3d * stdDev)
            : null;
        double? lower = filter.LowerTolerance is double lo
            ? (mean - lo) / (3d * stdDev)
            : null;
        if (upper is null && lower is null)
        {
            return null;
        }
        if (upper is null)
        {
            return lower;
        }
        if (lower is null)
        {
            return upper;
        }
        return Math.Min(upper.Value, lower.Value);
    }

    private static TrendAppliedFilters EchoAppliedFilters(TrendFilter filter) => new(
        MachineIds: filter.MachineIds,
        ProductIds: filter.ProductIds,
        Topologies: filter.Topologies,
        PartNumbers: filter.PartNumbers,
        JedecNames: filter.JedecNames);

    /// <summary>
    /// Per-bucket rolling counts + Welford accumulator. One instance
    /// per bucket; mutated in place during streaming; never observed
    /// concurrently.
    /// </summary>
    private sealed class BucketAccumulator
    {
        // Panel counters
        public long PanelsTotal;
        public long PanelsInspected;
        public long GoodAoi;
        public long GoodDummyOnly;
        public long GoodRepaired;

        // Card (board) counter
        public long CardsTotal;

        // Opportunity + defect counters (tested object)
        public long Opportunities;
        public long DefectsAoi;
        public long DefectsReal;
        public long DefectsDummy;

        // Welford deviation accumulator
        public long DeviationCount;
        public double DeviationMean;
        public double DeviationM2;

        public void AddPanel(int status)
        {
            PanelsTotal++;
            switch (status)
            {
                case 1:
                    PanelsInspected++;
                    GoodAoi++;
                    break;
                case 2:
                    PanelsInspected++;
                    GoodDummyOnly++;
                    break;
                case 3:
                    PanelsInspected++;
                    GoodRepaired++;
                    break;
                case -1 or -2:
                    PanelsInspected++;
                    break;
                case 0:
                    // not inspected
                    break;
                default:
                    // Unknown status code — mirror the FpyTableReport
                    // policy: treat as not inspected so FPY numerators
                    // stay honest.
                    break;
            }
        }

        public void AddCard() => CardsTotal++;

        public void AddOpportunity(int aoiBits, int realBits, int dummyBits)
        {
            Opportunities++;
            DefectsAoi += aoiBits;
            DefectsReal += realBits;
            DefectsDummy += dummyBits;
        }

        public void AddDeviationSample(double value)
        {
            DeviationCount++;
            var delta = value - DeviationMean;
            DeviationMean += delta / DeviationCount;
            var delta2 = value - DeviationMean;
            DeviationM2 += delta * delta2;
        }
    }
}
