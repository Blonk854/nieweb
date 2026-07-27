using Nieweb.Data.Entities;

namespace Nieweb.Api.Parameters;

/// <summary>
/// Canonical seed values for the <see cref="AppParameter"/> table.
/// Populated by <see cref="IAppParameters.EnsureSeededAsync"/> on every
/// host boot; entries whose <see cref="AppParameterDefault.Key"/> already
/// exists in the DB are left alone so admins can tune them safely.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are drawn verbatim from
/// <c>.github/skills/aoi-quality-metrics/SKILL.md</c> and Vieweb §2.4.2
/// (Application parameters). Tolerance intervals default to <c>0</c>
/// so that %Cp / GR&amp;R render a clear "not configured" result until
/// an admin sets a real value — this matches how Vieweb behaved when
/// its <c>ViewebParameters.properties</c> shipped without site-specific
/// tuning.
/// </para>
/// <para>
/// MSA thresholds (Acceptable / Out for Average, Std-Dev, 6σ, Cp,
/// GR&amp;R, EV, %EV on Deviation X / Y / Theta) are deliberately absent:
/// docs/phase-2.md §10 Q1 defers the MSA report until a dedicated
/// empty-panel Superviseur DB is commissioned. Those keys will be
/// appended here when the MSA slice is undeferred.
/// </para>
/// </remarks>
public static class AppParameterDefaults
{
    /// <summary>
    /// The full seed set. Order is not significant — the seeder inserts
    /// by set-difference on <see cref="AppParameterDefault.Key"/>.
    /// </summary>
    public static readonly IReadOnlyList<AppParameterDefault> All = new AppParameterDefault[]
    {
        // MSA constants (used by upcoming Cp / GR&R / EV / %EV reports).
        new(
            Key: "msa.gr_r",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "4.33",
            Description: "GR&R constant (defaultGR_R in Vieweb ViewebParameters). Do not change silently — expose in the admin UI so MSA-4-calibrated customers can override."),
        new(
            Key: "msa.confidence.coefficient",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "4.33",
            Description: "Confidence coefficient (k_conf) used in EV = k_conf × σ. Vieweb default."),
        new(
            Key: "msa.tolerance.ev",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "1.0",
            Description: "Tolerance EV parameter used in %EV = 100 × EV / Tolerance EV."),

        // Paste-pad tolerance intervals (Vieweb §2.4.2).
        new(
            Key: "tolerance.paste.itx",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "0",
            Description: "Paste pad tolerance interval X (mm). Set per site; 0 means 'not configured'."),
        new(
            Key: "tolerance.paste.ity",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "0",
            Description: "Paste pad tolerance interval Y (mm). Set per site."),
        new(
            Key: "tolerance.paste.its",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "0",
            Description: "Paste pad tolerance interval Surface (mm²). Set per site."),

        // Component tolerance intervals (Vieweb §2.4.2).
        new(
            Key: "tolerance.component.itx",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "0",
            Description: "Component tolerance interval X (mm). Set per site."),
        new(
            Key: "tolerance.component.ity",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "0",
            Description: "Component tolerance interval Y (mm). Set per site."),
        new(
            Key: "tolerance.component.its",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "0",
            Description: "Component tolerance interval Surface (mm²). Set per site."),

        // Batch scheduler master switch (Vieweb batchIsOn parity, per
        // docs/phase-2.md §5). The per-treatment IsEnabled flag lands
        // with F3 / AT2; both must be true for a run to fire.
        new(
            Key: "batch.enabled",
            ValueType: AppParameterValueTypes.Bool,
            Value: "false",
            Description: "Global master switch for automatic treatments. Parity with Vieweb batchIsOn."),

        // Skip-classification thresholds + repair-button map (see the
        // skip-classification domain). Consumed by DPMO / FPY / Skip
        // Summary when a skip toggle or status filter is active. Editable
        // via the dedicated Skip classification admin screen (which reads
        // and writes these very rows).
        new(
            Key: "skip.missing_ratio_threshold",
            ValueType: AppParameterValueTypes.Decimal,
            Value: "0.50",
            Description: "Empty-board heuristic: fraction of a card's components flagged 'missing' before it is classed HeuristicMissing (0-1)."),
        new(
            Key: "skip.min_component_floor",
            ValueType: AppParameterValueTypes.Int,
            Value: "8",
            Description: "Empty-board heuristic: minimum Number_Of_Component before the missing-ratio may fire (guards tiny cards)."),
        new(
            Key: "skip.absolute_missing_floor",
            ValueType: AppParameterValueTypes.Int,
            Value: "4",
            Description: "Empty-board heuristic: minimum absolute missing-component count before the missing-ratio may fire."),
        new(
            Key: "skip.repair_button_meanings",
            ValueType: AppParameterValueTypes.String,
            Value: "{\"X-OUT\":\"ManualSkip\"}",
            Description: "JSON map of repair-button label -> meaning (Normal|ManualSkip|FalseCall|ConfirmedRealMissing). Case-insensitive labels."),
    };
}

/// <summary>
/// Immutable seed row. Distinct from <see cref="AppParameter"/> because
/// the entity carries mutable timestamps + IsSystem, which the seeder
/// stamps at insert time.
/// </summary>
public sealed record AppParameterDefault(
    string Key,
    string ValueType,
    string Value,
    string Description);
