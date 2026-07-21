using Nieweb.DataSources;
using Nieweb.Reports.Common.Defects;

namespace Nieweb.Reports;

/// <summary>
/// Volume-weighted Pareto chart of AOI defects. Bars = absolute
/// defect count so a low-rate / high-volume contributor correctly
/// outranks a high-rate / low-volume one — the ranking your line
/// engineer actually wants when deciding what to fix first. Each row
/// carries opportunity share, DPMO, and defect share as decorations so
/// the rate view stays visible without hijacking the sort order.
/// </summary>
/// <remarks>
/// <para>
/// The report streams
/// <see cref="IAoiSource.StreamTestedObjectsAsync"/> once, applying
/// DB-level filters (<see cref="ParetoFilter.MachineIds"/>,
/// <see cref="ParetoFilter.ProductIds"/>,
/// <see cref="ParetoFilter.RecipeIds"/>) as query parameters and the
/// in-memory narrowing filters
/// (<see cref="ParetoFilter.DefectBits"/>,
/// <see cref="ParetoFilter.Topologies"/>,
/// <see cref="ParetoFilter.PartNumbers"/>,
/// <see cref="ParetoFilter.JedecNames"/>) row-by-row.
/// </para>
/// <para>
/// Because the report is stateless, drill-down is expressed by the
/// client calling <see cref="RunAsync"/> again with an additional
/// narrowing filter. No server-side session is required.
/// </para>
/// <para>
/// All bit-to-defect translation goes through
/// <see cref="DefectBitDecoder"/> so this report cannot re-introduce
/// Vieweb bug #11211 (wrong defect displayed). Counts accumulate as
/// <see cref="long"/> and percentages are computed once at emit time
/// so weekly and daily totals cannot drift apart (Vieweb bug #12421).
/// </para>
/// </remarks>
public sealed class ParetoReport : IReport<ParetoFilter, ParetoResult>
{
    /// <summary>Stable metadata for this report.</summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "pareto-defects",
        DisplayName: "Volume-weighted defect Pareto",
        Category: ReportCategory.Chart,
        Description: "Ranks AOI defect contributors by absolute count with volume context (opportunity share, DPMO, cumulative %). Supports interactive drill-down across defect / product / machine / part / package / reference designator axes.");

    /// <summary>Stateless singleton; safe to share across all callers.</summary>
    public static readonly ParetoReport Instance = new();

    // OBJECT_TYPE.Object_Type_Id bit codes (vit-aoi-database skill).
    private const int ObjectTypeComponentBit = 0x00000001;
    private const int ObjectTypePastePadBit = 0x00000010;

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    public async Task<ParetoResult> RunAsync(
        IAoiSource source,
        ParetoFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Weight != ParetoWeight.Count)
        {
            throw new NotSupportedException(
                $"ParetoWeight.{filter.Weight} is not implemented yet. " +
                "Only ParetoWeight.Count (absolute defect count, boss-approved volume weighting) ships in TR3.");
        }
        if (filter.TopN is int n and <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), n, "TopN must be positive when set.");
        }
        if (filter.VitalFewThresholdPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter),
                filter.VitalFewThresholdPercent,
                "VitalFewThresholdPercent must be between 0 and 100.");
        }

        var overall = new Accumulator();
        var perGroup = new Dictionary<GroupKey, Accumulator>();
        // Defect axis: overall opportunity count is the denominator
        // for every defect row (a component that didn't fire this bit
        // is still an opportunity for it). Track counts per bit here.
        var perDefectBit = filter.Axis == ParetoAxis.Defect
            ? new Dictionary<int, long>()
            : null;

        // Fast-lookup sets for in-memory narrowing filters.
        var defectBitMask = BuildDefectBitMask(filter.DefectBits);
        var topologySet = ToOrdinalSet(filter.Topologies);
        var partNumberSet = ToOrdinalSet(filter.PartNumbers);
        var jedecSet = ToOrdinalSet(filter.JedecNames);

        var query = new TestedObjectQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
            RecipeIds = filter.RecipeIds,
        };

        await foreach (var obj in source.StreamTestedObjectsAsync(query, cancellationToken).ConfigureAwait(false))
        {
            // Row-level narrowing filters — cheap short-circuits.
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
            var errorField = filter.Numerator switch
            {
                DpmoNumerator.Aoi => obj.ErrorTable,
                DpmoNumerator.Real => obj.ErrorTableAr,
                DpmoNumerator.Dummy => obj.ErrorTable & ~obj.ErrorTableAr,
                _ => 0L,
            };
            // DefectBits filter: only rows carrying at least one of
            // the requested bits contribute. Note the filter narrows
            // to those bits ONLY when computing rows; opportunity
            // counting stays over the same objects because a "defect
            // present" filter answers "given this defect, who owns it?"
            if (defectBitMask != 0)
            {
                errorField &= defectBitMask;
            }

            var defectBits = DefectBitDecoder.CountBits(errorField);

            overall.Add(isOpportunity, defectBits);

            if (perDefectBit is not null)
            {
                // Group by defect bit: opportunity denominator is the
                // overall opportunity count, applied at emit time.
                foreach (var info in DefectBitDecoder.Decode(errorField))
                {
                    if (!filter.IncludeObsoleteBits && info.IsObsolete)
                    {
                        continue;
                    }
                    perDefectBit.TryGetValue(info.BitNumber, out var current);
                    perDefectBit[info.BitNumber] = current + 1;
                }
                continue;
            }

            if (!isOpportunity && defectBits == 0)
            {
                continue;
            }

            AddToGroups(perGroup, filter, obj, isOpportunity, defectBits);
        }

        // Resolve display names only for axes that need them.
        var machineNames = filter.Axis == ParetoAxis.AoiMachine
            ? (await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(m => m.MachineId, m => (string?)m.MachineName)
            : null;
        var productNames = filter.Axis == ParetoAxis.Product
            ? (await source.ListProductsAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(p => p.ProductId, p => p.ProductName)
            : null;

        var (visibleRows, othersBucket) = BuildRows(
            filter,
            overall,
            perGroup,
            perDefectBit,
            machineNames,
            productNames);

        return new ParetoResult(
            Source: source.Descriptor,
            Window: filter.Window,
            Axis: filter.Axis,
            Numerator: filter.Numerator,
            Opportunity: filter.Opportunity,
            Weight: filter.Weight,
            AppliedFilters: EchoAppliedFilters(filter),
            Overall: overall.ToKpi(),
            Rows: visibleRows,
            OthersBucket: othersBucket);
    }

    private static (IReadOnlyList<ParetoRow> Rows, ParetoRow? Others) BuildRows(
        ParetoFilter filter,
        Accumulator overall,
        Dictionary<GroupKey, Accumulator> perGroup,
        Dictionary<int, long>? perDefectBit,
        Dictionary<int, string?>? machineNames,
        Dictionary<int, string?>? productNames)
    {
        // Step 1: materialise every bucket as an "unranked" row.
        //   - Axis=Defect uses the defect-bit tally with an overall-scoped denominator.
        //   - Every other axis uses per-group accumulators.
        var totalOpportunities = overall.OpportunityCount;
        var totalDefects = overall.DefectBitCount;

        List<Unranked> unranked;
        if (perDefectBit is not null)
        {
            unranked = new List<Unranked>(perDefectBit.Count);
            foreach (var (bitNumber, count) in perDefectBit)
            {
                var info = DefectBitDecoder.All[bitNumber - 1];
                unranked.Add(new Unranked(
                    Key: bitNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Name: info.DisplayName,
                    DefectCount: count,
                    OpportunityCount: totalOpportunities));
            }
        }
        else
        {
            unranked = new List<Unranked>(perGroup.Count);
            foreach (var (key, bucket) in perGroup)
            {
                unranked.Add(new Unranked(
                    Key: key.ToDisplayKey(),
                    Name: ResolveName(key, filter.Axis, machineNames, productNames),
                    DefectCount: bucket.DefectBitCount,
                    OpportunityCount: bucket.OpportunityCount));
            }
        }

        // Step 2: sort descending by DefectCount (== WeightedScore
        // under ParetoWeight.Count), break ties on GroupKey for stable
        // snapshots.
        unranked.Sort((a, b) =>
        {
            var byScore = b.DefectCount.CompareTo(a.DefectCount);
            return byScore != 0 ? byScore : StringComparer.Ordinal.Compare(a.Key, b.Key);
        });

        // Step 3: apply TopN + Others.
        List<Unranked> visible;
        List<Unranked> overflow;
        if (filter.TopN is int topN && unranked.Count > topN)
        {
            visible = unranked.GetRange(0, topN);
            overflow = unranked.GetRange(topN, unranked.Count - topN);
        }
        else
        {
            visible = unranked;
            overflow = new List<Unranked>();
        }

        // Step 4: turn each row into a ParetoRow with cumulative
        // percent + vital-few flag. Compute cumulative BEFORE the
        // Others row so the Others share doesn't distort the
        // vital-few call.
        var rows = new List<ParetoRow>(visible.Count);
        var cumulativeDefects = 0L;
        var vitalFewFlipped = false;
        foreach (var u in visible)
        {
            cumulativeDefects += u.DefectCount;
            var cumulativePct = totalDefects == 0 ? 0d : 100d * cumulativeDefects / totalDefects;
            var defectSharePct = totalDefects == 0 ? 0d : 100d * u.DefectCount / totalDefects;
            var oppSharePct = totalOpportunities == 0 ? 0d : 100d * u.OpportunityCount / totalOpportunities;
            var dpmo = u.OpportunityCount == 0 ? 0d : 1_000_000d * u.DefectCount / u.OpportunityCount;

            // "Vital few" = every bar up to and INCLUDING the first
            // one whose cumulative % reaches or crosses the threshold.
            // Once we've flipped past the threshold on a previous
            // bar, subsequent bars are trivial-many.
            var isVitalFew = !vitalFewFlipped;
            if (!vitalFewFlipped && cumulativePct >= filter.VitalFewThresholdPercent)
            {
                vitalFewFlipped = true;
            }

            rows.Add(new ParetoRow(
                GroupKey: u.Key,
                GroupName: u.Name,
                DefectCount: u.DefectCount,
                WeightedScore: u.DefectCount,
                OpportunityCount: u.OpportunityCount,
                OpportunitySharePercent: oppSharePct,
                DpmoPpm: dpmo,
                DefectSharePercent: defectSharePct,
                CumulativePercent: cumulativePct,
                IsVitalFew: isVitalFew));
        }

        // Step 5: Others bucket.
        ParetoRow? others = null;
        if (overflow.Count > 0 && filter.IncludeOthersBucket)
        {
            var othersDefects = 0L;
            var othersOpps = 0L;
            foreach (var u in overflow)
            {
                othersDefects += u.DefectCount;
                othersOpps += u.OpportunityCount;
            }
            var othersDefectSharePct = totalDefects == 0 ? 0d : 100d * othersDefects / totalDefects;
            var othersOppSharePct = totalOpportunities == 0 ? 0d : 100d * othersOpps / totalOpportunities;
            var othersDpmo = othersOpps == 0 ? 0d : 1_000_000d * othersDefects / othersOpps;
            // CumulativePercent on Others is always 100 by
            // construction (visible + Others exhaust the sample).
            others = new ParetoRow(
                GroupKey: null,
                GroupName: "Others",
                DefectCount: othersDefects,
                WeightedScore: othersDefects,
                OpportunityCount: othersOpps,
                OpportunitySharePercent: othersOppSharePct,
                DpmoPpm: othersDpmo,
                DefectSharePercent: othersDefectSharePct,
                CumulativePercent: 100d,
                IsVitalFew: false);
        }

        return (rows, others);
    }

    private static string? ResolveName(
        GroupKey key,
        ParetoAxis axis,
        Dictionary<int, string?>? machineNames,
        Dictionary<int, string?>? productNames)
    {
        return axis switch
        {
            ParetoAxis.AoiMachine when machineNames is not null && key.IntValue is int mid
                => machineNames.TryGetValue(mid, out var name) ? name : null,
            ParetoAxis.Product when productNames is not null && key.IntValue is int pid
                => productNames.TryGetValue(pid, out var name) ? name : null,
            ParetoAxis.ReferenceDesignator
                or ParetoAxis.PartNumber
                or ParetoAxis.Jedec => key.StringValue,
            _ => null,
        };
    }

    private static bool IsOpportunity(int objectTypeId, DpmoOpportunity opportunity) => opportunity switch
    {
        DpmoOpportunity.All => true,
        DpmoOpportunity.Components => (objectTypeId & ObjectTypeComponentBit) != 0,
        DpmoOpportunity.Paste => (objectTypeId & ObjectTypePastePadBit) != 0,
        _ => false,
    };

    private static void AddToGroups(
        Dictionary<GroupKey, Accumulator> perGroup,
        ParetoFilter filter,
        TestedObjectRow obj,
        bool isOpportunity,
        int defectBits)
    {
        switch (filter.Axis)
        {
            case ParetoAxis.AoiMachine:
                Bump(perGroup, GroupKey.Int(obj.MachineId), isOpportunity, defectBits);
                break;
            case ParetoAxis.Product:
                Bump(perGroup, GroupKey.Int(obj.ProductId), isOpportunity, defectBits);
                break;
            case ParetoAxis.ReferenceDesignator:
                Bump(perGroup, GroupKey.String(obj.Topology), isOpportunity, defectBits);
                break;
            case ParetoAxis.PartNumber:
                Bump(perGroup, GroupKey.String(obj.PartNumberName), isOpportunity, defectBits);
                break;
            case ParetoAxis.Jedec:
                Bump(perGroup, GroupKey.String(obj.JedecName), isOpportunity, defectBits);
                break;
            case ParetoAxis.Defect:
                // Handled inline in RunAsync so the denominator can
                // be the overall opportunity count.
                throw new InvalidOperationException(
                    "ParetoAxis.Defect must be handled in RunAsync, not in AddToGroups.");
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(filter), filter.Axis, "Unknown ParetoAxis.");
        }
    }

    private static void Bump(
        Dictionary<GroupKey, Accumulator> perGroup,
        GroupKey key,
        bool isOpportunity,
        int defectBits)
    {
        if (!perGroup.TryGetValue(key, out var bucket))
        {
            bucket = new Accumulator();
            perGroup[key] = bucket;
        }
        bucket.Add(isOpportunity, defectBits);
    }

    private static long BuildDefectBitMask(IReadOnlyCollection<int>? bits)
    {
        if (bits is null || bits.Count == 0)
        {
            return 0L;
        }
        var mask = 0L;
        foreach (var bit in bits)
        {
            // DefectBitDecoder catalogues bits 1..25; anything else
            // is guaranteed to contribute zero to CountBits/Decode
            // (Bits1To25Mask strips the upper bits) so we reject the
            // caller's input early rather than silently discarding it.
            if (bit < 1 || bit > 25)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bits), bit, "Defect bit numbers must be in 1..25 (see DefectBitDecoder.All).");
            }
            mask |= 1L << (bit - 1);
        }
        return mask;
    }

    private static HashSet<string>? ToOrdinalSet(IReadOnlyCollection<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }
        return new HashSet<string>(values, StringComparer.Ordinal);
    }

    private static ParetoAppliedFilters EchoAppliedFilters(ParetoFilter filter)
    {
        // Materialise into stable list snapshots so the DTO carries
        // no live references back into the caller's collections.
        return new ParetoAppliedFilters(
            MachineIds: filter.MachineIds is null ? [] : [.. filter.MachineIds],
            ProductIds: filter.ProductIds is null ? [] : [.. filter.ProductIds],
            RecipeIds: filter.RecipeIds is null ? [] : [.. filter.RecipeIds],
            DefectBits: filter.DefectBits is null ? [] : [.. filter.DefectBits],
            Topologies: filter.Topologies is null ? [] : [.. filter.Topologies],
            PartNumbers: filter.PartNumbers is null ? [] : [.. filter.PartNumbers],
            JedecNames: filter.JedecNames is null ? [] : [.. filter.JedecNames]);
    }

    /// <summary>
    /// Discriminated key over "numeric id" and "nullable string"
    /// group axes. Kept private because callers only see the
    /// stringified <see cref="ParetoRow.GroupKey"/>.
    /// </summary>
    private readonly record struct GroupKey(int? IntValue, string? StringValue)
    {
        public static GroupKey Int(int value) => new(value, null);
        public static GroupKey String(string? value) => new(null, value);

        public string? ToDisplayKey() => IntValue is int i
            ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : StringValue;
    }

    private readonly record struct Unranked(
        string? Key,
        string? Name,
        long DefectCount,
        long OpportunityCount);

    private sealed class Accumulator
    {
        private long _testedObjects;
        private long _opportunities;
        private long _defectBits;

        public long TestedObjectCount => _testedObjects;
        public long OpportunityCount => _opportunities;
        public long DefectBitCount => _defectBits;

        public void Add(bool isOpportunity, int defectBits)
        {
            _testedObjects++;
            if (isOpportunity)
            {
                _opportunities++;
            }
            _defectBits += defectBits;
        }

        public DpmoKpi ToKpi()
        {
            var dpmo = _opportunities == 0 ? 0d : 1_000_000d * _defectBits / _opportunities;
            return new DpmoKpi(
                TestedObjectCount: _testedObjects,
                OpportunityCount: _opportunities,
                DefectBitCount: _defectBits,
                DpmoPpm: dpmo);
        }
    }
}
