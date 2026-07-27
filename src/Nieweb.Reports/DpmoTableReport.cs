using Nieweb.DataSources;
using Nieweb.Reports.Common.Defects;
using Nieweb.Reports.Common.Skips;

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
/// The report is a <b>two-pass</b> aggregation. Pass 1 streams
/// <see cref="IAoiSource.StreamCardsAsync"/> and sums the AOI's own
/// per-sub-panel inspection test counts
/// (<see cref="CardRow.NbOfTestsOnComp"/> /
/// <see cref="CardRow.NbOfTestsOnPads"/>) to form the DPMO / PPM
/// <i>opportunity denominator</i>. Pass 2 streams
/// <see cref="IAoiSource.StreamTestedObjectsAsync"/> and counts defect
/// bits for the <i>numerator</i>. Every bit-to-defect translation goes
/// through the central <see cref="DefectBitDecoder"/> so we cannot
/// re-introduce Vieweb bug <b>#11211</b> (wrong defect displayed).
/// </para>
/// <para>
/// The denominator MUST come from CARDS, never from a TESTED_OBJECT row
/// count: on the production Superviseur DB <c>TESTED_OBJECT</c> is
/// <b>defect-only</b> (one row per flagged defect), so counting its
/// rows collapses the opportunity count to ~the defect count and pins
/// DPMO near the 1e6 ceiling. Validated against the HLYAOI archive:
/// component DPMO ≈ 50.9, not the ≈957 000 a row-count denominator
/// yields.
/// </para>
/// <para>
/// Aggregation is count-first / divide-last: opportunity counts and
/// defect-bit counts are accumulated as <see cref="long"/> and only
/// the final <c>DPMO = 1e6 · defects / opportunities</c> ratio is
/// computed at emit time. This makes the report immune to the
/// weekly-vs-daily rounding divergence of Vieweb bug #12421.
/// </para>
/// <para>
/// Opportunity flavour ("DPMO defects components" vs "DPMO defects
/// paste" in Vieweb) selects both halves consistently: the denominator
/// uses <c>Nb_Of_Tests_On_Comp</c> vs <c>Nb_Of_Tests_On_Pads</c> and
/// the numerator counts defects only on objects of the matching type,
/// so the "components only" DPMO is independent of paste-pad rows and
/// vice-versa.
/// </para>
/// <para>
/// Only the card-derivable axes (AOI machine, product, defect,
/// overall) get a correct rate. Object-level axes (reference
/// designator, part number, JEDEC) cannot supply a per-group
/// opportunity count from a defect-only table, so they emit a
/// defect-COUNT ranking with the rate suppressed until per-placement
/// counts are wired from LIBRARY.
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

        // Classify boards up-front whenever a skip predicate is active so
        // dropped boards leave BOTH the opportunity denominator and the
        // defect numerator. Two composable predicates:
        //   * SkipExclusion.Clean drops any non-None (skipped) board.
        //   * SkipStatuses (when set) keeps only boards whose class is in
        //     the set (a positive narrowing filter, e.g. "ManualSkip only").
        // A board must satisfy BOTH to be counted. Raw + no status filter
        // leaves skipIndex null (unchanged fast path).
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

        bool KeepClass(SkipClass cls) =>
            (filter.SkipExclusion != SkipExclusion.Clean || cls == SkipClass.None)
            && (statusFilter is null || statusFilter.Contains(cls));

        // ---- Pass 1: opportunity denominator, streamed from CARDS. ----
        // Opportunities are inspection *test counts*
        // (CARDS.Nb_Of_Tests_On_Comp / _On_Pads), NEVER a
        // TESTED_OBJECT row count: production TESTED_OBJECT is
        // defect-only, so counting its rows collapses the denominator
        // to ~the defect count and pins DPMO near 1e6. Only the
        // card-derivable axes (AOI / product / defect / overall) get a
        // per-group denominator here; object-level axes fall back to a
        // defect-count ranking below.
        long opportunitiesOverall = 0;
        var opportunitiesByMachine = filter.GroupBy == DpmoGroupBy.AoiMachine
            ? new Dictionary<int, long>()
            : null;
        var opportunitiesByProduct = filter.GroupBy == DpmoGroupBy.Product
            ? new Dictionary<int, long>()
            : null;

        var cardQuery = new CardQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            if (skipIndex is not null && !KeepClass(skipIndex.Classify(card, config)))
            {
                skippedCards!.Add((card.PanelId, card.CardIdOnPanel));
                skipExcludedCards++;
                continue;
            }
            var opp = OpportunityFor(card, filter.Opportunity);
            opportunitiesOverall += opp;
            if (opportunitiesByMachine is not null)
            {
                opportunitiesByMachine.TryGetValue(card.MachineId, out var m);
                opportunitiesByMachine[card.MachineId] = m + opp;
            }
            if (opportunitiesByProduct is not null)
            {
                opportunitiesByProduct.TryGetValue(card.ProductId, out var p);
                opportunitiesByProduct[card.ProductId] = p + opp;
            }
        }

        // ---- Pass 2: defect-bit numerator, streamed from TESTED_OBJECT. ----
        long testedObjectsOverall = 0;
        long defectBitsOverall = 0;
        var defectBitsByGroup = new Dictionary<GroupKey, long>();
        var testedObjectsByGroup = new Dictionary<GroupKey, long>();
        var perDefectBit = filter.GroupBy == DpmoGroupBy.Defect
            ? new Dictionary<int, long>()
            : null;

        var objectQuery = new TestedObjectQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
        };
        await foreach (var obj in source.StreamTestedObjectsAsync(objectQuery, cancellationToken).ConfigureAwait(false))
        {
            // Clean mode: drop defects that live on a skipped board so the
            // numerator matches the denominator (which already dropped it).
            if (skippedCards is not null && skippedCards.Contains((obj.PanelId, obj.CardIdOnPanel)))
            {
                continue;
            }

            // The numerator honours the opportunity flavour: a
            // "components" DPMO counts component-object defects only,
            // a "paste" DPMO counts paste-pad defects only. This keeps
            // the numerator consistent with the card-derived
            // denominator (Nb_Of_Tests_On_Comp vs _On_Pads).
            if (!IsOpportunity(obj.ObjectTypeId, filter.Opportunity))
            {
                continue;
            }

            var errorField = filter.Numerator switch
            {
                DpmoNumerator.Aoi => obj.ErrorTable,
                DpmoNumerator.Real => obj.ErrorTableAr,
                DpmoNumerator.Dummy => obj.ErrorTable & ~obj.ErrorTableAr,
                _ => 0L,
            };
            var defectBits = DefectBitDecoder.CountBits(errorField);

            testedObjectsOverall++;
            defectBitsOverall += defectBits;

            if (perDefectBit is not null)
            {
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

            var key = GroupKeyFor(filter.GroupBy, obj);
            defectBitsByGroup.TryGetValue(key, out var d);
            defectBitsByGroup[key] = d + defectBits;
            testedObjectsByGroup.TryGetValue(key, out var t);
            testedObjectsByGroup[key] = t + 1;
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

        var overallKpi = new DpmoKpi(
            TestedObjectCount: testedObjectsOverall,
            OpportunityCount: opportunitiesOverall,
            DefectBitCount: defectBitsOverall,
            DpmoPpm: opportunitiesOverall == 0
                ? 0d
                : 1_000_000d * defectBitsOverall / opportunitiesOverall);

        var rows = filter.GroupBy switch
        {
            DpmoGroupBy.Defect => BuildDefectRows(
                perDefectBit!, testedObjectsOverall, opportunitiesOverall),
            DpmoGroupBy.AoiMachine => BuildBoardRows(
                opportunitiesByMachine!, defectBitsByGroup, testedObjectsByGroup,
                GroupKey.Int, machineNames),
            DpmoGroupBy.Product => BuildBoardRows(
                opportunitiesByProduct!, defectBitsByGroup, testedObjectsByGroup,
                GroupKey.Int, productNames),
            // Object-level axes (reference designator / part number /
            // JEDEC): no card-derived denominator exists on a
            // defect-only TESTED_OBJECT table, so emit a defect-count
            // ranking with the rate suppressed until per-placement
            // opportunity counts are wired from LIBRARY.
            _ => BuildCountOnlyRows(defectBitsByGroup, testedObjectsByGroup),
        };

        return new DpmoTableResult(
            Source: source.Descriptor,
            Window: filter.Window,
            GroupBy: filter.GroupBy,
            Numerator: filter.Numerator,
            Opportunity: filter.Opportunity,
            Overall: overallKpi,
            Rows: rows,
            SkipExclusion: filter.SkipExclusion,
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
    /// Card-level inspection opportunity count for the requested DPMO
    /// flavour — the canonical DPMO / PPM denominator. Paste
    /// opportunities are <c>null</c> on post-reflow sources (no paste
    /// stage), so they contribute 0.
    /// </summary>
    private static long OpportunityFor(CardRow card, DpmoOpportunity opportunity) => opportunity switch
    {
        DpmoOpportunity.Components => card.NbOfTestsOnComp,
        DpmoOpportunity.Paste => card.NbOfTestsOnPads ?? 0,
        DpmoOpportunity.All => card.NbOfTestsOnComp + (card.NbOfTestsOnPads ?? 0),
        _ => 0,
    };

    private static GroupKey GroupKeyFor(DpmoGroupBy groupBy, TestedObjectRow obj) => groupBy switch
    {
        DpmoGroupBy.AoiMachine => GroupKey.Int(obj.MachineId),
        DpmoGroupBy.Product => GroupKey.Int(obj.ProductId),
        DpmoGroupBy.ReferenceDesignator => GroupKey.String(obj.Topology),
        DpmoGroupBy.PartNumber => GroupKey.String(obj.PartNumberName),
        DpmoGroupBy.Jedec => GroupKey.String(obj.JedecName),
        DpmoGroupBy.Defect => throw new InvalidOperationException(
            "DpmoGroupBy.Defect is tallied inline in RunAsync, not grouped."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(groupBy), groupBy, "Unknown DpmoGroupBy."),
    };

    /// <summary>
    /// Defect axis: one row per set bit, denominator = the overall
    /// card-derived opportunity count (every inspected object is an
    /// opportunity for every bit). Sorted worst-first by DPMO.
    /// </summary>
    private static List<DpmoTableRow> BuildDefectRows(
        Dictionary<int, long> perDefectBit,
        long testedObjectsOverall,
        long opportunitiesOverall)
    {
        return perDefectBit
            .Select(kvp =>
            {
                var info = DefectBitDecoder.All[kvp.Key - 1];
                var kpi = new DpmoKpi(
                    TestedObjectCount: testedObjectsOverall,
                    OpportunityCount: opportunitiesOverall,
                    DefectBitCount: kvp.Value,
                    DpmoPpm: opportunitiesOverall == 0
                        ? 0d
                        : 1_000_000d * kvp.Value / opportunitiesOverall);
                return new DpmoTableRow(
                    GroupKey: kvp.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    GroupName: info.DisplayName,
                    Kpi: kpi);
            })
            .OrderByDescending(r => r.Kpi.DpmoPpm)
            .ThenBy(r => r.GroupKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Board-level axes (AOI machine / product): the row set is driven
    /// by the inspected card population, so every id that ran cards in
    /// the window gets a row — including zero-defect ids (DPMO 0) — and
    /// the denominator is that id's card-derived opportunity count.
    /// Sorted worst-first by DPMO.
    /// </summary>
    private static List<DpmoTableRow> BuildBoardRows(
        Dictionary<int, long> opportunitiesByKey,
        Dictionary<GroupKey, long> defectBitsByGroup,
        Dictionary<GroupKey, long> testedObjectsByGroup,
        Func<int, GroupKey> keyOf,
        Dictionary<int, string?>? names)
    {
        return opportunitiesByKey
            .Select(kvp =>
            {
                var key = keyOf(kvp.Key);
                var opportunities = kvp.Value;
                defectBitsByGroup.TryGetValue(key, out var defectBits);
                testedObjectsByGroup.TryGetValue(key, out var testedObjects);
                string? name = null;
                names?.TryGetValue(kvp.Key, out name);
                var kpi = new DpmoKpi(
                    TestedObjectCount: testedObjects,
                    OpportunityCount: opportunities,
                    DefectBitCount: defectBits,
                    DpmoPpm: opportunities == 0
                        ? 0d
                        : 1_000_000d * defectBits / opportunities);
                return new DpmoTableRow(
                    GroupKey: kvp.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    GroupName: name,
                    Kpi: kpi);
            })
            .OrderByDescending(r => r.Kpi.DpmoPpm)
            .ThenBy(r => r.GroupKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Object-level axes (reference designator / part number / JEDEC):
    /// a defect-only <c>TESTED_OBJECT</c> table cannot supply a
    /// per-group opportunity count, so we expose a defect-COUNT ranking
    /// with the rate suppressed (<c>OpportunityCount = 0</c>,
    /// <c>DpmoPpm = 0</c>) until per-placement counts are wired from
    /// LIBRARY. Sorted worst-first by defect count.
    /// </summary>
    private static List<DpmoTableRow> BuildCountOnlyRows(
        Dictionary<GroupKey, long> defectBitsByGroup,
        Dictionary<GroupKey, long> testedObjectsByGroup)
    {
        return defectBitsByGroup
            .Select(kvp =>
            {
                testedObjectsByGroup.TryGetValue(kvp.Key, out var testedObjects);
                var kpi = new DpmoKpi(
                    TestedObjectCount: testedObjects,
                    OpportunityCount: 0,
                    DefectBitCount: kvp.Value,
                    DpmoPpm: 0d);
                return new DpmoTableRow(
                    GroupKey: kvp.Key.ToDisplayKey(),
                    GroupName: kvp.Key.StringValue,
                    Kpi: kpi);
            })
            .OrderByDescending(r => r.Kpi.DefectBitCount)
            .ThenBy(r => r.GroupKey, StringComparer.Ordinal)
            .ToList();
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
}
