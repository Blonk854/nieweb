using Nieweb.DataSources;
using Nieweb.Filters;
using Nieweb.Reports.Common;
using Nieweb.Reports.Common.Defects;
using Nieweb.Reports.Common.Skips;
using Nieweb.Reports.Filters;

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
/// The report is a <b>two-pass</b> aggregation. Pass 1 streams
/// <see cref="IAoiSource.StreamCardsAsync"/> and sums the AOI's own
/// per-sub-panel test counts (<see cref="CardRow.NbOfTestsOnComp"/> /
/// <see cref="CardRow.NbOfTestsOnPads"/>) to form the DPMO / PPM
/// opportunity denominator — never a TESTED_OBJECT row count, since
/// production TESTED_OBJECT is defect-only. Pass 2 streams
/// <see cref="IAoiSource.StreamTestedObjectsAsync"/> for the defect
/// numerator, applying DB-level filters
/// (<see cref="ParetoFilter.MachineIds"/>,
/// <see cref="ParetoFilter.ProductIds"/>) as query parameters and the
/// in-memory narrowing filters
/// (<see cref="ParetoFilter.DefectBits"/>,
/// <see cref="ParetoFilter.Topologies"/>,
/// <see cref="ParetoFilter.PartNumbers"/>,
/// <see cref="ParetoFilter.JedecNames"/>) row-by-row.
/// </para>
/// <para>
/// Bars (absolute defect counts) are unaffected by the denominator, so
/// the volume-weighted ranking is always correct. The rate decorations
/// (opportunity share, DPMO) and the <see cref="ParetoWeight.Dpmo"/> /
/// <see cref="ParetoWeight.Ppm"/> weights use the card-derived
/// denominator on the card-derivable axes (AOI machine, product,
/// defect, day, shift); object-level axes (reference designator, part
/// number, JEDEC) have no card denominator on a defect-only table, so
/// their rate is suppressed (0) pending LIBRARY placement counts.
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
        if (!Enum.IsDefined(filter.Weight))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), filter.Weight, "Unknown ParetoWeight.");
        }
        if (filter.Axis == ParetoAxis.Shift && filter.Shifts is null)
        {
            throw new ArgumentException(
                "ParetoAxis.Shift requires ParetoFilter.Shifts to be set.",
                nameof(filter));
        }

        // Pre-decompose the window into buckets when the axis is
        // time-based. Buckets are contiguous inside the window, so a
        // binary search on StartEpochSeconds routes every incoming
        // row to at most one bucket in O(log n).
        var timeZone = filter.SiteTimeZone ?? TimeZoneInfo.Utc;
        var timeBuckets = filter.Axis switch
        {
            ParetoAxis.Day => TimeBucketer.Decompose(
                filter.Window.StartUtc,
                filter.Window.EndUtcExclusive,
                TimeBucket.Day,
                timeZone),
            ParetoAxis.Shift => TimeBucketer.Decompose(
                filter.Window.StartUtc,
                filter.Window.EndUtcExclusive,
                TimeBucket.Shift,
                timeZone,
                filter.Shifts),
            _ => null,
        };
        var bucketStartEpochs = timeBuckets is null
            ? null
            : timeBuckets.Select(b => b.StartUtc.ToUnixTimeSeconds()).ToArray();

        // ---- Pass 1: opportunity denominator, streamed from CARDS. ----
        // Opportunities are CARDS inspection test counts
        // (Nb_Of_Tests_On_Comp / _On_Pads), NEVER a TESTED_OBJECT row
        // count: production TESTED_OBJECT is defect-only, so counting
        // its rows collapses the denominator and pins DPMO near 1e6.
        // Only card-derivable axes (AOI / product / defect / day /
        // shift / overall) get a per-group denominator; object-level
        // axes fall back to a count-only ranking below.
        long opportunitiesOverall = 0;
        var opportunitiesByMachine = filter.Axis == ParetoAxis.AoiMachine
            ? new Dictionary<int, long>()
            : null;
        var opportunitiesByProduct = filter.Axis == ParetoAxis.Product
            ? new Dictionary<int, long>()
            : null;
        var opportunitiesByBucket = filter.Axis is ParetoAxis.Day or ParetoAxis.Shift
            ? new Dictionary<string, long>(StringComparer.Ordinal)
            : null;

        // Skip filtering (mirrors DpmoTableReport). Clean mode drops
        // skipped boards; a status filter narrows to specific skip
        // classes. Both must hold for a board to be kept. Raw + no status
        // filter leaves skipIndex null (unchanged fast path).
        var skipConfig = filter.SkipConfig ?? SkipClassificationConfig.Default;
        var skipStatusFilter = filter.SkipStatuses is { Count: > 0 }
            ? new HashSet<SkipClass>(filter.SkipStatuses)
            : null;
        var needsSkipIndex = filter.SkipExclusion == SkipExclusion.Clean || skipStatusFilter is not null;
        var skipIndex = needsSkipIndex
            ? await SkipInputsIndex.BuildAsync(
                source, filter.Window, filter.MachineIds, filter.ProductIds,
                onlyLastInspection: true, skipConfig, cancellationToken).ConfigureAwait(false)
            : null;
        var skippedCards = skipIndex is null ? null : new HashSet<(long PanelId, int CardId)>();
        long skipExcludedCards = 0;

        bool KeepClass(SkipClass cls)
        {
            // No status filter: Clean drops skipped (non-None) boards; Raw keeps all.
            if (skipStatusFilter is null)
            {
                return filter.SkipExclusion != SkipExclusion.Clean || cls == SkipClass.None;
            }
            // With a status filter set:
            //  - Clean: the selected classes are kept as exceptions alongside None.
            //  - Raw:   the selected classes act as a positive "show only these" filter.
            return filter.SkipExclusion == SkipExclusion.Clean
                ? cls == SkipClass.None || skipStatusFilter.Contains(cls)
                : skipStatusFilter.Contains(cls);
        }

        // NOGO exclusion: drop every product whose name contains "NOGO"
        // (case-insensitive) from both passes. NOGO coupons are known-
        // defect boards run at changeover and normally must not skew KPIs.
        var nogoProductIds = await NogoProducts.BuildAsync(
            source, filter.ExcludeNogo, cancellationToken).ConfigureAwait(false);

        var cardQuery = new CardQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
        };
        await foreach (var card in source.StreamCardsAsync(cardQuery, cancellationToken).ConfigureAwait(false))
        {
            if (nogoProductIds is not null && nogoProductIds.Contains(card.ProductId))
            {
                continue;
            }
            if (skipIndex is not null && !KeepClass(skipIndex.Classify(card, skipConfig)))
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
            if (opportunitiesByBucket is not null)
            {
                var bucket = FindBucket(timeBuckets!, bucketStartEpochs!, card.PanelNumericDate);
                if (bucket is not null)
                {
                    opportunitiesByBucket.TryGetValue(bucket.Label, out var b);
                    opportunitiesByBucket[bucket.Label] = b + opp;
                }
            }
        }

        // ---- Pass 2: defect numerator, streamed from TESTED_OBJECT. ----
        long testedObjectsOverall = 0;
        long defectsOverall = 0;
        var defectsByGroup = new Dictionary<GroupKey, long>();
        var perDefectBit = filter.Axis == ParetoAxis.Defect
            ? new Dictionary<int, long>()
            : null;

        // Fast-lookup sets for in-memory narrowing filters.
        var defectBitMask = BuildDefectBitMask(filter.DefectBits);
        var topologySet = ToOrdinalSet(filter.Topologies);
        var partNumberSet = ToOrdinalSet(filter.PartNumbers);
        var jedecSet = ToOrdinalSet(filter.JedecNames);

        // Generic Vieweb-style operator filter (applied in memory). Only
        // resolve the product / machine name maps when the request
        // actually references those fields — the string fields (topology /
        // part number / package) and defect need no lookup.
        var genericFilter = filter.Filters is { } fr && !fr.Clauses.IsDefaultOrEmpty ? fr : null;
        IReadOnlyDictionary<int, string?>? filterMachineNames = null;
        IReadOnlyDictionary<int, string?>? filterProductNames = null;
        if (genericFilter is not null)
        {
            var referencedFields = genericFilter.Clauses.Select(c => c.Field).ToHashSet();
            if (referencedFields.Contains(FilterField.AoiMachine))
            {
                filterMachineNames = (await source.ListMachinesAsync(cancellationToken).ConfigureAwait(false))
                    .ToDictionary(m => m.MachineId, m => (string?)m.MachineName);
            }
            if (referencedFields.Contains(FilterField.Product))
            {
                filterProductNames = (await source.ListProductsAsync(cancellationToken).ConfigureAwait(false))
                    .ToDictionary(p => p.ProductId, p => p.ProductName);
            }
        }

        var query = new TestedObjectQuery
        {
            Window = filter.Window,
            MachineIds = filter.MachineIds,
            ProductIds = filter.ProductIds,
        };

        await foreach (var obj in source.StreamTestedObjectsAsync(query, cancellationToken).ConfigureAwait(false))
        {
            // Drop NOGO-product defects (matches the card-pass exclusion).
            if (nogoProductIds is not null && nogoProductIds.Contains(obj.ProductId))
            {
                continue;
            }

            // Drop defects that live on a skipped board (Clean mode or a
            // status filter that excluded the board in Pass 1).
            if (skippedCards is not null && skippedCards.Contains((obj.PanelId, obj.CardIdOnPanel)))
            {
                continue;
            }

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

            // Generic operator filter — narrows on reference designator /
            // part number / package / product / AOI machine / defect using
            // the numerator-consistent defect bitfield.
            if (genericFilter is not null)
            {
                var numeratorField = filter.Numerator switch
                {
                    DpmoNumerator.Aoi => obj.ErrorTable,
                    DpmoNumerator.Real => obj.ErrorTableAr,
                    DpmoNumerator.Dummy => obj.ErrorTable & ~obj.ErrorTableAr,
                    _ => 0L,
                };
                var rowValues = ReportFilterRows.ForTestedObject(
                    obj, numeratorField, filter.IncludeObsoleteBits, filterMachineNames, filterProductNames);
                if (!FilterEvaluator.Matches(genericFilter, rowValues))
                {
                    continue;
                }
            }

            // The numerator honours the opportunity flavour so it stays
            // consistent with the card-derived denominator (a components
            // Pareto counts component defects only).
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
            // DefectBits filter: only rows carrying at least one of the
            // requested bits contribute.
            if (defectBitMask != 0)
            {
                errorField &= defectBitMask;
            }

            var defectBits = DefectBitDecoder.CountBits(errorField);

            testedObjectsOverall++;
            defectsOverall += defectBits;

            if (perDefectBit is not null)
            {
                // Group by defect bit: opportunity denominator is the
                // overall card-derived opportunity count, applied at
                // emit time.
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

            // Pareto ranks defect contributors — a zero-defect object
            // is no contributor, so only tally groups that carry a
            // defect (the denominator already came from the card pass).
            if (defectBits == 0)
            {
                continue;
            }

            var key = GroupKeyFor(filter.Axis, obj, timeBuckets, bucketStartEpochs);
            if (key is null)
            {
                // Row falls outside every decomposed time bucket.
                continue;
            }
            defectsByGroup.TryGetValue(key.Value, out var d);
            defectsByGroup[key.Value] = d + defectBits;
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

        var overallKpi = new DpmoKpi(
            TestedObjectCount: testedObjectsOverall,
            OpportunityCount: opportunitiesOverall,
            DefectBitCount: defectsOverall,
            DpmoPpm: opportunitiesOverall == 0
                ? 0d
                : 1_000_000d * defectsOverall / opportunitiesOverall);

        var (visibleRows, othersBucket) = BuildRows(
            filter,
            defectsOverall,
            opportunitiesOverall,
            defectsByGroup,
            perDefectBit,
            opportunitiesByMachine,
            opportunitiesByProduct,
            opportunitiesByBucket,
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
            Overall: overallKpi,
            Rows: visibleRows,
            OthersBucket: othersBucket,
            SkipExclusion: filter.SkipExclusion,
            SkipExcludedCards: skipExcludedCards);
    }

    private static (IReadOnlyList<ParetoRow> Rows, ParetoRow? Others) BuildRows(
        ParetoFilter filter,
        long totalDefects,
        long totalOpportunities,
        Dictionary<GroupKey, long> defectsByGroup,
        Dictionary<int, long>? perDefectBit,
        Dictionary<int, long>? opportunitiesByMachine,
        Dictionary<int, long>? opportunitiesByProduct,
        Dictionary<string, long>? opportunitiesByBucket,
        Dictionary<int, string?>? machineNames,
        Dictionary<int, string?>? productNames)
    {
        // Per-group opportunity denominator, resolved by axis from the
        // card pass. Object-level axes (reference designator / part /
        // JEDEC) have no card-derived denominator, so they report 0 and
        // the rate is suppressed.
        long OpportunityForGroup(GroupKey key) => filter.Axis switch
        {
            ParetoAxis.AoiMachine =>
                key.IntValue is int mid && opportunitiesByMachine!.TryGetValue(mid, out var m) ? m : 0,
            ParetoAxis.Product =>
                key.IntValue is int pid && opportunitiesByProduct!.TryGetValue(pid, out var p) ? p : 0,
            ParetoAxis.Day or ParetoAxis.Shift =>
                key.StringValue is string lbl && opportunitiesByBucket!.TryGetValue(lbl, out var b) ? b : 0,
            _ => 0,
        };

        // Step 1: materialise every bucket as an "unranked" row.
        //   - Axis=Defect uses the defect-bit tally with an overall-scoped denominator.
        //   - Every other axis uses per-group defect counts + card-derived opportunities.
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
            unranked = new List<Unranked>(defectsByGroup.Count);
            foreach (var (key, defects) in defectsByGroup)
            {
                unranked.Add(new Unranked(
                    Key: key.ToDisplayKey(),
                    Name: ResolveName(key, filter.Axis, machineNames, productNames),
                    DefectCount: defects,
                    OpportunityCount: OpportunityForGroup(key)));
            }
        }

        // Step 2: sort descending by WeightedScore for the active
        // weight. Under ParetoWeight.Count the score is DefectCount
        // (volume-weighted, boss default); under Dpmo / Ppm it is
        // 1e6 * defect / opportunity (rate-weighted). Ties break on
        // GroupKey for stable snapshots.
        unranked.Sort((a, b) =>
        {
            var byScore = ScoreFor(filter.Weight, b).CompareTo(ScoreFor(filter.Weight, a));
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
                WeightedScore: ScoreFor(filter.Weight, u),
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
                WeightedScore: ScoreFor(filter.Weight, new Unranked(null, null, othersDefects, othersOpps)),
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
                or ParetoAxis.Jedec
                or ParetoAxis.Day
                or ParetoAxis.Shift => key.StringValue,
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

    private static GroupKey? GroupKeyFor(
        ParetoAxis axis,
        TestedObjectRow obj,
        IReadOnlyList<TimeBucketRange>? timeBuckets,
        long[]? bucketStartEpochs)
    {
        switch (axis)
        {
            case ParetoAxis.AoiMachine:
                return GroupKey.Int(obj.MachineId);
            case ParetoAxis.Product:
                return GroupKey.Int(obj.ProductId);
            case ParetoAxis.ReferenceDesignator:
                return GroupKey.String(obj.Topology);
            case ParetoAxis.PartNumber:
                return GroupKey.String(obj.PartNumberName);
            case ParetoAxis.Jedec:
                return GroupKey.String(obj.JedecName);
            case ParetoAxis.Day:
            case ParetoAxis.Shift:
            {
                // Row is routed to at most one time bucket; a null
                // means it fell outside the decomposed window (should
                // not happen once the DB-level window filter holds).
                var bucket = FindBucket(timeBuckets!, bucketStartEpochs!, obj.PanelNumericDate);
                return bucket is null ? null : GroupKey.String(bucket.Label);
            }
            case ParetoAxis.Defect:
                throw new InvalidOperationException(
                    "ParetoAxis.Defect is tallied inline in RunAsync, not grouped.");
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(axis), axis, "Unknown ParetoAxis.");
        }
    }

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

    /// <summary>
    /// Binary search: returns the bucket whose half-open
    /// [StartEpochSeconds, next-bucket start) contains
    /// <paramref name="panelNumericDate"/>, or <c>null</c> when the
    /// timestamp falls outside the last bucket's exclusive end.
    /// </summary>
    private static TimeBucketRange? FindBucket(
        IReadOnlyList<TimeBucketRange> buckets,
        long[] bucketStartEpochs,
        int panelNumericDate)
    {
        long panelEpoch = panelNumericDate;
        var idx = Array.BinarySearch(bucketStartEpochs, panelEpoch);
        if (idx < 0)
        {
            // Array.BinarySearch returns bitwise complement of the
            // insertion point. Walk one step back to the bucket that
            // actually contains the panel timestamp.
            idx = ~idx - 1;
            if (idx < 0)
            {
                return null;
            }
        }
        var bucket = buckets[idx];
        return panelEpoch < bucket.EndUtcExclusive.ToUnixTimeSeconds() ? bucket : null;
    }

    /// <summary>
    /// Bar-height metric for the active <see cref="ParetoWeight"/>.
    /// <see cref="ParetoWeight.Count"/> returns the absolute defect
    /// count (volume weight); <see cref="ParetoWeight.Dpmo"/> and
    /// <see cref="ParetoWeight.Ppm"/> return
    /// <c>1e6 · defect count / opportunity count</c> (rate weight,
    /// zero when there are no opportunities).
    /// </summary>
    private static double ScoreFor(ParetoWeight weight, Unranked row) => weight switch
    {
        ParetoWeight.Count => row.DefectCount,
        ParetoWeight.Dpmo or ParetoWeight.Ppm
            => row.OpportunityCount == 0
                ? 0d
                : 1_000_000d * row.DefectCount / row.OpportunityCount,
        _ => row.DefectCount,
    };

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
}
