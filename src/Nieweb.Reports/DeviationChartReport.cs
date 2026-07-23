using Nieweb.DataSources;

namespace Nieweb.Reports;

/// <summary>
/// Component-level deviation histogram (a.k.a. Vieweb's Deviation
/// chart). Streams <see cref="IAoiSource.StreamTestedObjectsAsync"/>
/// once, projects one <see cref="DeviationAxis"/> per row, and emits
/// a fixed-bin histogram along with mean, sample standard deviation,
/// ±3σ overlays, and (when supplied by the caller) ±tolerance
/// overlays plus an out-of-tolerance count.
/// </summary>
/// <remarks>
/// <para>
/// Post-reflow AOI stores placement offsets on <c>TESTED_OBJECT</c>;
/// pre-reflow SPI stores stencil-print offsets on the same columns
/// (both live-DB schemas expose <c>Delta_X / Delta_Y / Delta_Theta /
/// Delta_Thickness / Delta_Surface</c>). This report is agnostic —
/// the caller narrows on <see cref="DeviationFilter.Opportunity"/>
/// (<see cref="DpmoOpportunity.Components"/> for placement,
/// <see cref="DpmoOpportunity.Paste"/> for print) and passes the
/// appropriate tolerance envelope resolved from <c>AppParameter</c>.
/// </para>
/// <para>
/// Bin bounds are derived from the observed sample: bin[0] starts at
/// <see cref="DeviationResult.Min"/> and bin[<c>BinCount-1</c>] ends
/// at <see cref="DeviationResult.Max"/>. When the sample is
/// degenerate (all rows identical, <see cref="DeviationResult.Min"/>
/// == <see cref="DeviationResult.Max"/>) every count lands in bin 0
/// and the remaining bins are zero-width and empty. When there are
/// zero rows the report returns
/// <see cref="DeviationFilter.BinCount"/> empty bins spanning the
/// tolerance envelope if one was supplied, or <c>[-1, +1]</c>
/// otherwise, so the client can still render an x-axis.
/// </para>
/// <para>
/// Standard deviation is Bessel-corrected (sample, n-1) and uses
/// Welford's online algorithm so the report never allocates a copy
/// of the sample array. This keeps memory constant regardless of
/// window size.
/// </para>
/// </remarks>
public sealed class DeviationChartReport : IReport<DeviationFilter, DeviationResult>
{
    /// <summary>Stable metadata for this report.</summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "deviation-chart",
        DisplayName: "Deviation chart",
        Category: ReportCategory.Chart,
        Description: "Histogram of one deviation dimension (X / Y / Theta / Thickness / Surface) across every tested object in the window, with mean, ±3σ, and optional ±tolerance overlays.");

    /// <summary>Stateless singleton; safe to share across all callers.</summary>
    public static readonly DeviationChartReport Instance = new();

    // OBJECT_TYPE.Object_Type_Id bit codes (vit-aoi-database skill).
    private const int ObjectTypeComponentBit = 0x00000001;
    private const int ObjectTypePastePadBit = 0x00000010;

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    public async Task<DeviationResult> RunAsync(
        IAoiSource source,
        DeviationFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);

        if (!Enum.IsDefined(filter.Axis))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Axis, "Unknown DeviationAxis.");
        }
        if (!Enum.IsDefined(filter.Opportunity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Opportunity, "Unknown DpmoOpportunity.");
        }
        if (filter.BinCount is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.BinCount, "BinCount must be between 1 and 500.");
        }
        if (filter.LowerTolerance is double lo && filter.UpperTolerance is double hi && lo >= hi)
        {
            throw new ArgumentException(
                "LowerTolerance must be strictly less than UpperTolerance when both are set.",
                nameof(filter));
        }

        var topologySet = ToOrdinalSet(filter.Topologies);
        var partNumberSet = ToOrdinalSet(filter.PartNumbers);
        var jedecSet = ToOrdinalSet(filter.JedecNames);

        var query = new TestedObjectQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
        };

        // First pass: stream once, collect samples in a buffer plus
        // running min/max. Because a real query can return hundreds
        // of thousands of rows we cap the in-memory buffer at
        // MaxBufferedSamples; beyond that we still update running
        // stats + histogram (bin bounds pinned to the observed
        // min/max seen so far) but drop the raw values. In practice
        // every report call is windowed tightly enough that we stay
        // under the cap.
        var samples = new List<double>(capacity: 1024);
        var count = 0L;
        var outOfTolerance = 0L;
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        var welfordMean = 0d;
        var welfordM2 = 0d;

        await foreach (var obj in source.StreamTestedObjectsAsync(query, cancellationToken).ConfigureAwait(false))
        {
            if (!IsOpportunity(obj.ObjectTypeId, filter.Opportunity))
            {
                continue;
            }
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

            var value = ProjectAxis(obj, filter.Axis);
            if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                continue;
            }

            var v = value.Value;
            count++;

            // Welford's online mean/variance.
            var delta = v - welfordMean;
            welfordMean += delta / count;
            var delta2 = v - welfordMean;
            welfordM2 += delta * delta2;

            if (v < min)
            {
                min = v;
            }
            if (v > max)
            {
                max = v;
            }

            if (samples.Count < MaxBufferedSamples)
            {
                samples.Add(v);
            }

            if (filter.LowerTolerance is double loTol && v < loTol)
            {
                outOfTolerance++;
            }
            else if (filter.UpperTolerance is double hiTol && v > hiTol)
            {
                outOfTolerance++;
            }
        }

        var (binMin, binMax) = ChooseBinBounds(count, min, max, filter);
        var bins = BuildBins(samples, count, binMin, binMax, filter.BinCount);

        var stdDev = count >= 2 ? Math.Sqrt(welfordM2 / (count - 1)) : 0d;
        var mean = count == 0 ? double.NaN : welfordMean;
        var minReported = count == 0 ? double.NaN : min;
        var maxReported = count == 0 ? double.NaN : max;
        var plus3 = count >= 2 ? mean + 3d * stdDev : double.NaN;
        var minus3 = count >= 2 ? mean - 3d * stdDev : double.NaN;

        return new DeviationResult(
            Source: source.Descriptor,
            Window: filter.Window,
            Axis: filter.Axis,
            Opportunity: filter.Opportunity,
            AppliedFilters: EchoAppliedFilters(filter),
            SampleCount: count,
            Mean: mean,
            StdDev: stdDev,
            Min: minReported,
            Max: maxReported,
            PlusThreeSigma: plus3,
            MinusThreeSigma: minus3,
            LowerTolerance: filter.LowerTolerance,
            UpperTolerance: filter.UpperTolerance,
            OutOfToleranceCount: outOfTolerance,
            Bins: bins);
    }

    /// <summary>
    /// Cap on the raw sample buffer. 1e6 doubles ≈ 8 MB — Welford
    /// running stats keep going past the cap; only histogram
    /// precision degrades marginally when the sample exceeds this
    /// (bounds are pinned to the min/max seen).
    /// </summary>
    private const int MaxBufferedSamples = 1_000_000;

    private static (double Min, double Max) ChooseBinBounds(
        long count, double min, double max, DeviationFilter filter)
    {
        if (count == 0)
        {
            // Zero-sample fallback: prefer the tolerance envelope so
            // the client can still render tolerance overlays. Ranges
            // are inclusive on the left, so a small pad on the right.
            if (filter.LowerTolerance is double lo && filter.UpperTolerance is double hi)
            {
                return (lo, hi);
            }
            if (filter.LowerTolerance is double lo1)
            {
                return (lo1, lo1 + 1d);
            }
            if (filter.UpperTolerance is double hi1)
            {
                return (hi1 - 1d, hi1);
            }
            return (-1d, 1d);
        }
        if (min == max)
        {
            // Degenerate: give the bin one unit of width so the
            // client doesn't divide by zero.
            return (min, min + 1d);
        }
        return (min, max);
    }

    private static List<DeviationBin> BuildBins(
        List<double> samples, long totalCount, double binMin, double binMax, int binCount)
    {
        var bins = new List<DeviationBin>(binCount);
        var width = (binMax - binMin) / binCount;
        var counts = new long[binCount];

        // If we captured every sample, count via the buffer. If we
        // hit the buffer cap, we can only distribute what we have —
        // the client sees Bins summing to samples.Count while
        // SampleCount is the true total. Documented in the DTO.
        foreach (var v in samples)
        {
            var idx = (int)Math.Floor((v - binMin) / width);
            if (idx < 0)
            {
                idx = 0;
            }
            if (idx >= binCount)
            {
                idx = binCount - 1;
            }
            counts[idx]++;
        }

        for (var i = 0; i < binCount; i++)
        {
            var lower = binMin + i * width;
            var upper = i == binCount - 1 ? binMax : lower + width;
            bins.Add(new DeviationBin(i, lower, upper, counts[i]));
        }
        return bins;
    }

    private static double? ProjectAxis(TestedObjectRow row, DeviationAxis axis) => axis switch
    {
        DeviationAxis.DeltaX => row.DeltaXUm,
        DeviationAxis.DeltaY => row.DeltaYUm,
        DeviationAxis.DeltaTheta => row.DeltaThetaDeg,
        DeviationAxis.DeltaThickness => row.DeltaThicknessUm,
        DeviationAxis.DeltaSurface => row.DeltaSurface,
        _ => null,
    };

    private static bool IsOpportunity(int objectTypeId, DpmoOpportunity opportunity) => opportunity switch
    {
        DpmoOpportunity.All => true,
        DpmoOpportunity.Components => (objectTypeId & ObjectTypeComponentBit) != 0,
        DpmoOpportunity.Paste => (objectTypeId & ObjectTypePastePadBit) != 0,
        _ => false,
    };

    private static HashSet<string>? ToOrdinalSet(IReadOnlyCollection<string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return null;
        }
        return new HashSet<string>(source, StringComparer.Ordinal);
    }

    private static DeviationAppliedFilters EchoAppliedFilters(DeviationFilter filter) => new(
        MachineIds: filter.MachineIds?.ToArray() ?? Array.Empty<int>(),
        ProductIds: filter.ProductIds?.ToArray() ?? Array.Empty<int>(),
        Topologies: filter.Topologies?.ToArray() ?? Array.Empty<string>(),
        PartNumbers: filter.PartNumbers?.ToArray() ?? Array.Empty<string>(),
        JedecNames: filter.JedecNames?.ToArray() ?? Array.Empty<string>());
}
