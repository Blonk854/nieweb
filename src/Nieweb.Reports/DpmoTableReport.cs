using Nieweb.DataSources;
using Nieweb.Reports.Common.Defects;

namespace Nieweb.Reports;

/// <summary>
/// DPMO (Defects Per Million Opportunities) table: rows per AOI
/// machine, defect bit, product, reference designator, part number,
/// or JEDEC. Implements Vieweb §3.1.6.5 verbatim ("DPMO tables can
/// show data by AOI, by defect, by package (Jedec), by part number,
/// by product, by Reference designator").
/// </summary>
/// <remarks>
/// <para>
/// The report streams <see cref="IAoiSource.StreamTestedObjectsAsync"/>
/// and folds each row into an <see cref="Accumulator"/> per group key.
/// Every bit-to-defect translation goes through the central
/// <see cref="DefectBitDecoder"/> so we cannot re-introduce Vieweb
/// bug <b>#11211</b> (wrong defect displayed) in this report.
/// </para>
/// <para>
/// Aggregation is count-first / divide-last: opportunity counts and
/// defect-bit counts are accumulated as <see cref="long"/> and only
/// the final <c>DPMO = 1e6 · defects / opportunities</c> ratio is
/// computed at emit time. This makes the report immune to the
/// weekly-vs-daily rounding divergence of Vieweb bug #12421.
/// </para>
/// <para>
/// Opportunity filtering ("DPMO defects components" vs "DPMO defects
/// paste" in Vieweb) is applied to the denominator BEFORE grouping —
/// tested-object rows that don't match
/// <see cref="DpmoTableFilter.Opportunity"/> contribute neither to
/// the numerator nor to the denominator. This keeps the "components
/// only" DPMO independent of paste-pad rows and vice-versa.
/// </para>
/// <para>
/// Machine / product name resolution happens after streaming, from a
/// single small <see cref="IAoiSource.ListMachinesAsync"/> /
/// <see cref="IAoiSource.ListProductsAsync"/> call — only when the
/// grouping axis actually needs it.
/// </para>
/// </remarks>
public sealed class DpmoTableReport : IReport<DpmoTableFilter, DpmoTableResult>
{
    /// <summary>Stable metadata for this report.</summary>
    public static readonly ReportDescriptor ReportDescriptor = new(
        Id: "dpmo-table",
        DisplayName: "DPMO table",
        Category: ReportCategory.Table,
        Description: "Defects Per Million Opportunities table per AOI / defect / product / reference designator / part number / JEDEC.");

    /// <summary>Stateless singleton; safe to share across all callers.</summary>
    public static readonly DpmoTableReport Instance = new();

    // OBJECT_TYPE.Object_Type_Id bit codes (vit-aoi-database skill).
    private const int ObjectTypeComponentBit = 0x00000001;
    private const int ObjectTypePastePadBit = 0x00000010;

    /// <inheritdoc />
    public ReportDescriptor Descriptor => ReportDescriptor;

    /// <inheritdoc />
    /// <remarks>The class-level remarks describe the aggregation contract.</remarks>
    public async Task<DpmoTableResult> RunAsync(
        IAoiSource source,
        DpmoTableFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(filter);

        var overall = new Accumulator();
        var perGroup = new Dictionary<GroupKey, Accumulator>();
        // Defect axis is special-cased: the denominator for every
        // defect row is the overall opportunity count in scope (a
        // component that didn't fire this bit is still an
        // opportunity for that bit). We therefore accumulate defect
        // counts per bit here and materialise the rows at emit time.
        var perDefectBit = filter.GroupBy == DpmoGroupBy.Defect
            ? new Dictionary<int, long>()
            : null;

        var query = new TestedObjectQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
        };

        await foreach (var obj in source.StreamTestedObjectsAsync(query, cancellationToken).ConfigureAwait(false))
        {
            var isOpportunity = IsOpportunity(obj.ObjectTypeId, filter.Opportunity);
            var defectBits = filter.Numerator switch
            {
                DpmoNumerator.Aoi => DefectBitDecoder.CountBits(obj.ErrorTable),
                DpmoNumerator.Real => DefectBitDecoder.CountBits(obj.ErrorTableAr),
                DpmoNumerator.Dummy => DefectBitDecoder.CountBits(obj.ErrorTable & ~obj.ErrorTableAr),
                _ => 0,
            };

            overall.Add(isOpportunity, defectBits);

            if (perDefectBit is not null)
            {
                // Per-bit defect tally; opportunity denominator is
                // applied later from `overall.OpportunityCount`.
                var errorField = filter.Numerator switch
                {
                    DpmoNumerator.Aoi => obj.ErrorTable,
                    DpmoNumerator.Real => obj.ErrorTableAr,
                    DpmoNumerator.Dummy => obj.ErrorTable & ~obj.ErrorTableAr,
                    _ => 0L,
                };
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
                // Row contributes nothing to either bucket; skip
                // grouping to avoid emitting empty rows for axes with
                // wide cardinality (e.g. ReferenceDesignator).
                continue;
            }

            AddToGroups(perGroup, filter, obj, isOpportunity, defectBits);
        }

        // Resolve display names only for axes that need them.
        var machineNames = filter.GroupBy == DpmoGroupBy.AoiMachine
            ? (await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(m => m.MachineId, m => (string?)m.MachineName)
            : null;
        var productNames = filter.GroupBy == DpmoGroupBy.Product
            ? (await source.ListProductsAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(p => p.ProductId, p => p.ProductName)
            : null;

        List<DpmoTableRow> rows;
        if (perDefectBit is not null)
        {
            var opportunityCount = overall.OpportunityCount;
            rows = perDefectBit
                .Select(kvp =>
                {
                    var info = DefectBitDecoder.All[kvp.Key - 1];
                    var kpi = new DpmoKpi(
                        TestedObjectCount: overall.TestedObjectCount,
                        OpportunityCount: opportunityCount,
                        DefectBitCount: kvp.Value,
                        DpmoPpm: opportunityCount == 0 ? 0d : 1_000_000d * kvp.Value / opportunityCount);
                    return new DpmoTableRow(
                        GroupKey: kvp.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        GroupName: info.DisplayName,
                        Kpi: kpi);
                })
                .OrderByDescending(r => r.Kpi.DpmoPpm)
                .ThenBy(r => r.GroupKey, StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            rows = perGroup
                .Select(kvp => BuildRow(kvp.Key, kvp.Value, filter.GroupBy, machineNames, productNames))
                // Vieweb reads the DPMO table worst-first — sort descending
                // by DpmoPpm and break ties by GroupKey for stable snapshots.
                .OrderByDescending(r => r.Kpi.DpmoPpm)
                .ThenBy(r => r.GroupKey, StringComparer.Ordinal)
                .ToList();
        }

        return new DpmoTableResult(
            Source: source.Descriptor,
            Window: filter.Window,
            GroupBy: filter.GroupBy,
            Numerator: filter.Numerator,
            Opportunity: filter.Opportunity,
            Overall: overall.ToKpi(),
            Rows: rows);
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
        DpmoTableFilter filter,
        TestedObjectRow obj,
        bool isOpportunity,
        int defectBits)
    {
        switch (filter.GroupBy)
        {
            case DpmoGroupBy.AoiMachine:
                Bump(perGroup, GroupKey.Int(obj.MachineId), isOpportunity, defectBits);
                break;

            case DpmoGroupBy.Product:
                Bump(perGroup, GroupKey.Int(obj.ProductId), isOpportunity, defectBits);
                break;

            case DpmoGroupBy.ReferenceDesignator:
                Bump(perGroup, GroupKey.String(obj.Topology), isOpportunity, defectBits);
                break;

            case DpmoGroupBy.PartNumber:
                Bump(perGroup, GroupKey.String(obj.PartNumberName), isOpportunity, defectBits);
                break;

            case DpmoGroupBy.Jedec:
                Bump(perGroup, GroupKey.String(obj.JedecName), isOpportunity, defectBits);
                break;

            case DpmoGroupBy.Defect:
                // Handled inline in RunAsync so the denominator can
                // be the overall opportunity count.
                throw new InvalidOperationException(
                    "DpmoGroupBy.Defect must be handled in RunAsync, not in AddToGroups.");

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(filter), filter.GroupBy, "Unknown DpmoGroupBy.");
        }
    }

    private static void Bump(
        Dictionary<GroupKey, Accumulator> perGroup,
        GroupKey key,
        bool isOpportunity,
        int defectBitsForThisRow)
    {
        if (!perGroup.TryGetValue(key, out var bucket))
        {
            bucket = new Accumulator();
            perGroup[key] = bucket;
        }
        bucket.Add(isOpportunity, defectBitsForThisRow);
    }

    private static DpmoTableRow BuildRow(
        GroupKey key,
        Accumulator bucket,
        DpmoGroupBy groupBy,
        Dictionary<int, string?>? machineNames,
        Dictionary<int, string?>? productNames)
    {
        string? name = null;
        string? keyString = key.ToDisplayKey();

        switch (groupBy)
        {
            case DpmoGroupBy.AoiMachine when machineNames is not null && key.IntValue is int mid:
                machineNames.TryGetValue(mid, out name);
                break;
            case DpmoGroupBy.Product when productNames is not null && key.IntValue is int pid:
                productNames.TryGetValue(pid, out name);
                break;
            case DpmoGroupBy.ReferenceDesignator:
            case DpmoGroupBy.PartNumber:
            case DpmoGroupBy.Jedec:
                // String axes: the group key is already the display
                // label; expose it as GroupName for parity with numeric
                // axes so the UI can render a single "name" column.
                name = key.StringValue;
                break;
            default:
                break;
        }

        return new DpmoTableRow(
            GroupKey: keyString,
            GroupName: name,
            Kpi: bucket.ToKpi());
    }

    /// <summary>
    /// Discriminated key over "numeric id" and "nullable string"
    /// group axes. Kept private to the report because callers only
    /// ever consume the stringified <see cref="DpmoTableRow.GroupKey"/>.
    /// </summary>
    private readonly record struct GroupKey(int? IntValue, string? StringValue)
    {
        public static GroupKey Int(int value) => new(value, null);
        public static GroupKey String(string? value) => new(null, value);

        public string? ToDisplayKey() => IntValue is int i
            ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : StringValue;
    }

    private sealed class Accumulator
    {
        private long _testedObjects;
        private long _opportunities;
        private long _defectBits;

        public long TestedObjectCount => _testedObjects;
        public long OpportunityCount => _opportunities;

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
