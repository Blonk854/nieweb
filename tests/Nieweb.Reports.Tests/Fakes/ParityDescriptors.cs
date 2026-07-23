using Nieweb.DataSources;

namespace Nieweb.Reports.Tests.Fakes;

/// <summary>
/// Canonical <see cref="SourceDescriptor"/> instances that mirror the
/// live Superviseur databases used by Nieweb — <c>HLYAOI2024</c> (post-
/// reflow, schema 5.0) and <c>MEAOI</c> (pre-reflow, schema 4.3.1).
/// Kept in one file so the T3 two-DB parity tests
/// (<see cref="Nieweb.Reports.Tests.Parity.TwoDbParityTests"/>) share
/// identical capability bitsets with the production
/// <c>HlyaoiSource</c> and <c>MeaoiSource</c>. If either production
/// descriptor gains or loses a <see cref="Capabilities"/> flag, mirror
/// the change here and re-run the parity suite.
/// </summary>
internal static class ParityDescriptors
{
    /// <summary>Post-reflow — <c>HLYMSSQL2 / HLYAOI2024</c>, schema 5.0.</summary>
    public static readonly SourceDescriptor PostReflow = new(
        Id: "postreflow",
        DisplayName: "Post-reflow AOI (HLYAOI2024)",
        SchemaVersion: "5.0",
        Caps:
            Capabilities.PinLevel |
            Capabilities.ReviewAudit |
            Capabilities.IsLastInspectionFilter |
            Capabilities.MachineEfficiencyTiming |
            Capabilities.PrecomputedCardDpmo |
            Capabilities.BarcodeProductView |
            Capabilities.RecipeVariants);

    /// <summary>Pre-reflow — <c>HLYMSSQL1 / MEAOI</c>, schema 4.3.1.</summary>
    public static readonly SourceDescriptor PreReflow = new(
        Id: "prereflow",
        DisplayName: "Pre-reflow AOI (MEAOI)",
        SchemaVersion: "4.3.1",
        Caps:
            Capabilities.PastePrintMetrics |
            Capabilities.FeederAnalytics);
}
