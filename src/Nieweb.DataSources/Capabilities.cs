namespace Nieweb.DataSources;

/// <summary>
/// Optional features a data source may implement in addition to the
/// universal <see cref="IAoiSource"/> contract. Set only for capabilities
/// the source can actually satisfy; the UI uses this to enable / disable
/// widgets so users are never offered functionality that would 500.
/// </summary>
[Flags]
public enum Capabilities
{
    None = 0,

    /// <summary>Source has PIN and PIN_MEASURE tables (per-solder-joint data).</summary>
    PinLevel = 1 << 0,

    /// <summary>Source has the *_HISTO tables (review audit trail).</summary>
    ReviewAudit = 1 << 1,

    /// <summary>PANELS.IS_LAST_INSPECTION column available for de-duplication.</summary>
    IsLastInspectionFilter = 1 << 2,

    /// <summary>PANELS has CONVEYING_TIME_S / BUY_SELL_PANEL_TIME_S / WAITING_REVIEW_TIME_S.</summary>
    MachineEfficiencyTiming = 1 << 3,

    /// <summary>CARDS has pre-computed DPMO_*_DEFECT_NB buckets.</summary>
    PrecomputedCardDpmo = 1 << 4,

    /// <summary>PANELS + CARDS have PastePads_* / Stencil_D* (placement-AOI paste metrics).</summary>
    PastePrintMetrics = 1 << 5,

    /// <summary>FEEDER table is actively populated per machine (not stubs).</summary>
    FeederAnalytics = 1 << 6,

    /// <summary>Barcode_Product view + related lookups exist.</summary>
    BarcodeProductView = 1 << 7,

    /// <summary>RECIPE has VARIANT_NAME column.</summary>
    RecipeVariants = 1 << 8,
}
