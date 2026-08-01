namespace Nieweb.Reports;

/// <summary>
/// Mutable counter that translates a <c>Panel_Status</c> / <c>Card_Status</c>
/// enum value into the FPY count buckets and produces an immutable
/// <see cref="FpyKpi"/> carrying all three Vieweb flavours (AOI / Diagnostic /
/// After Repair).
/// </summary>
/// <remarks>
/// Shared by <see cref="FpyTableReport"/> and the per-line FPY trend report
/// so both reports compute the three FPY flavours identically. Aggregation is
/// count-first / divide-last: every row bumps one integer bucket and the three
/// percentages are only computed in <see cref="ToKpi"/>. This makes it
/// impossible to reproduce Vieweb bug #12421 (weekly FPY differing from the sum
/// of daily FPYs). Status classification follows the canonical enum
/// <c>{-2,-1,0,1,2,3}</c> from the <c>aoi-quality-metrics</c> skill.
/// </remarks>
internal sealed class FpyAccumulator
{
    private long _total;
    private long _notInspected;
    private long _faulty;
    private long _goodAoi;          // status = 1
    private long _goodDummyOnly;    // status = 2 (all defects dummy)
    private long _goodRepaired;     // status = 3

    /// <summary>Fold one panel / board status into the buckets.</summary>
    public void Add(int status)
    {
        _total++;
        switch (status)
        {
            case 1:
                _goodAoi++;
                break;
            case 2:
                _goodDummyOnly++;
                break;
            case 3:
                _goodRepaired++;
                break;
            case -1 or -2:
                _faulty++;
                break;
            case 0:
                _notInspected++;
                break;
            default:
                // Unknown status code — treat as not-inspected so
                // FPY numerators stay honest. See aoi-quality-metrics
                // skill: the canonical enum is {-2,-1,0,1,2,3}; hitting
                // this branch means the schema changed.
                _notInspected++;
                break;
        }
    }

    /// <summary>Compute the immutable KPI from the accumulated counts.</summary>
    public FpyKpi ToKpi()
    {
        var goodDiag = _goodAoi + _goodDummyOnly;
        var goodAr = goodDiag + _goodRepaired;
        var inspected = _total - _notInspected;

        var fpyAoi = inspected == 0 ? 0d : 100d * _goodAoi / inspected;
        var fpyDiag = inspected == 0 ? 0d : 100d * goodDiag / inspected;
        var fpyAr = inspected == 0 ? 0d : 100d * goodAr / inspected;

        return new FpyKpi(
            TotalRows: _total,
            InspectedCount: inspected,
            NotInspectedCount: _notInspected,
            FaultyCount: _faulty,
            GoodAoiCount: _goodAoi,
            GoodDiagnosticCount: goodDiag,
            GoodAfterRepairCount: goodAr,
            FpyAoiPercent: fpyAoi,
            FpyDiagnosticPercent: fpyDiag,
            FpyAfterRepairPercent: fpyAr);
    }
}

/// <summary>
/// Helpers for re-deriving a panel verdict from its surviving (non-skip)
/// boards when a skip filter drops some boards. Shared by the FPY reports so
/// panel-level "Clean" FPY behaves identically everywhere.
/// </summary>
internal static class FpyPanelStatus
{
    /// <summary>
    /// The effective status of a panel re-derived from its surviving
    /// (non-skip) boards: the panel is only as good as its worst board.
    /// Goodness order (best → worst): 1 (good AOI) &lt; 2 (good diagnostic)
    /// &lt; 3 (repaired) &lt; faulty. Status 0 (not inspected) is ignored
    /// unless it is all that is present, in which case the panel is
    /// not-inspected.
    /// </summary>
    public static int Effective(List<int> nonSkipStatuses)
    {
        var worstStatus = 0;
        var worstRank = -1;
        foreach (var status in nonSkipStatuses)
        {
            if (status == 0)
            {
                continue;
            }
            var rank = status switch { 1 => 0, 2 => 1, 3 => 2, _ => 3 }; // -1 / -2 / unknown = faulty
            if (rank > worstRank)
            {
                worstRank = rank;
                worstStatus = status;
            }
        }
        return worstStatus;
    }
}
