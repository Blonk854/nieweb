using System.Globalization;

namespace Nieweb.Reports;

/// <summary>
/// Shared first-party presentation rules for Pareto rows: which
/// ranking metric the bars plot, when cumulative / vital-few chrome
/// is shown, and how unavailable opportunity metrics are printed.
/// </summary>
public static class ParetoPresentation
{
    public static bool ShowCumulative(ParetoWeight weight) => weight is ParetoWeight.Count;

    public static bool ShowVitalFew(ParetoWeight weight) => ShowCumulative(weight);

    public static bool ShowOpportunityShare(ParetoAxis axis) => axis is not ParetoAxis.Defect;

    public static double BarMagnitude(ParetoWeight weight, ParetoRow row) =>
        ShowCumulative(weight) ? row.DefectCount : row.WeightedScore;

    public static string LeftAxisCaption(ParetoWeight weight) => weight switch
    {
        ParetoWeight.Dpmo => "DPMO",
        ParetoWeight.Ppm => "PPM",
        _ => "Defects",
    };

    public static string OpportunityCountCell(ParetoRow row, string unavailable = "") =>
        row.OpportunitiesApplicable
            ? row.OpportunityCount.ToString(CultureInfo.InvariantCulture)
            : unavailable;

    public static string OpportunityShareCell(ParetoAxis axis, ParetoRow row, string unavailable = "")
    {
        if (!row.OpportunitiesApplicable || !ShowOpportunityShare(axis))
        {
            return unavailable;
        }
        return row.OpportunitySharePercent.ToString("0.####", CultureInfo.InvariantCulture);
    }

    public static string DpmoCell(ParetoRow row, string unavailable = "") =>
        row.OpportunitiesApplicable
            ? row.DpmoPpm.ToString("0.####", CultureInfo.InvariantCulture)
            : unavailable;
}
