namespace Nieweb.Reports.Common;

/// <summary>
/// Time bucket sizes for report time-window decomposition. Backs the
/// Vieweb "Draw type = By day / By shift" axis and generalises it to the
/// hour-based and calendar-based buckets Nieweb ships with — see
/// <c>docs/phase-2.md</c> §7.1 RI2.
/// </summary>
/// <remarks>
/// <para>
/// The enum values are ordered from finest to coarsest to keep switch
/// statements readable. Do not renumber them: analytics and saved-view
/// JSON payloads persist the string name (<see cref="Enum.ToString()"/>),
/// so the underlying int is only relevant to callers that materialise
/// the enum with <see cref="Enum.GetValues{T}()"/>.
/// </para>
/// <para>
/// <see cref="Shift"/> is the only bucket that depends on external
/// configuration (a <see cref="ShiftDefinition"/>). The other buckets
/// are self-describing: <see cref="Hour1"/> through <see cref="Hour12"/>
/// snap to wall-clock hour boundaries in the caller's time zone;
/// <see cref="Day"/> / <see cref="Week"/> / <see cref="Month"/> snap to
/// calendar boundaries (Monday-based ISO week).
/// </para>
/// </remarks>
public enum TimeBucket
{
    /// <summary>1-hour bucket (wall-clock).</summary>
    Hour1 = 0,

    /// <summary>3-hour bucket (00:00 / 03:00 / 06:00 / …).</summary>
    Hour3 = 1,

    /// <summary>6-hour bucket (00:00 / 06:00 / 12:00 / 18:00).</summary>
    Hour6 = 2,

    /// <summary>12-hour bucket (00:00 / 12:00).</summary>
    Hour12 = 3,

    /// <summary>
    /// Site-defined production shift — see <see cref="ShiftDefinition"/>.
    /// Requires a shift definition to be supplied to the bucketer.
    /// </summary>
    Shift = 4,

    /// <summary>Local calendar day (00:00 to 24:00 in the caller's time zone).</summary>
    Day = 5,

    /// <summary>ISO week (Monday 00:00 to next Monday 00:00).</summary>
    Week = 6,

    /// <summary>Calendar month (day 1 00:00 to first-of-next-month 00:00).</summary>
    Month = 7,
}
