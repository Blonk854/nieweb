using System.Collections.Immutable;

namespace Nieweb.Reports.Common;

/// <summary>
/// A production shift schedule. Mirrors the Vieweb "Shift definition"
/// admin page (Vieweb §2.4.4): a list of shift start times ordered
/// around a 24-hour cycle. Nieweb persists shifts through Phase-2 §7.4
/// <c>PL1</c>; this record is the in-memory shape the bucketer consumes.
/// </summary>
/// <remarks>
/// <para>
/// The last entry always wraps around midnight — i.e. a three-shift day
/// with starts <c>[08:00, 16:00, 00:00]</c> defines shifts
/// <c>08:00–16:00</c>, <c>16:00–00:00</c>, <c>00:00–08:00</c>. The
/// bucketer never leaves a gap in a 24-hour window: gaps would silently
/// hide inspection panels.
/// </para>
/// <para>
/// Shift labels default to <c>"Shift 1"</c>, <c>"Shift 2"</c>, … but
/// admins can supply their own (matching Vieweb, which had free-form
/// shift names per Vieweb §2.4.4).
/// </para>
/// </remarks>
public sealed record ShiftDefinition
{
    /// <summary>
    /// Ordered shift start times as <see cref="TimeOnly"/> values (wall
    /// clock in the caller's site time zone). Must contain at least one
    /// entry and no duplicates.
    /// </summary>
    public ImmutableArray<TimeOnly> Starts { get; init; }

    /// <summary>
    /// Human-readable labels aligned with <see cref="Starts"/>. Same
    /// length as <see cref="Starts"/>.
    /// </summary>
    public ImmutableArray<string> Labels { get; init; }

    private ShiftDefinition(ImmutableArray<TimeOnly> starts, ImmutableArray<string> labels)
    {
        Starts = starts;
        Labels = labels;
    }

    /// <summary>
    /// Builds a shift definition from the given start times, generating
    /// default labels (<c>"Shift 1"</c>, <c>"Shift 2"</c>, …). Entries
    /// are normalised into ascending time-of-day order — this matches
    /// how Vieweb stored shift starts internally.
    /// </summary>
    public static ShiftDefinition FromStarts(IEnumerable<TimeOnly> starts)
    {
        ArgumentNullException.ThrowIfNull(starts);
        var sorted = starts.Distinct().OrderBy(t => t.Ticks).ToImmutableArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException("A shift definition needs at least one start time.", nameof(starts));
        }
        var labels = Enumerable.Range(1, sorted.Length)
            .Select(i => $"Shift {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}")
            .ToImmutableArray();
        return new ShiftDefinition(sorted, labels);
    }

    /// <summary>
    /// Builds a shift definition with explicit labels. <paramref name="starts"/>
    /// and <paramref name="labels"/> must have the same length; entries
    /// are re-sorted together by start time.
    /// </summary>
    public static ShiftDefinition FromStarts(
        IEnumerable<TimeOnly> starts,
        IEnumerable<string> labels)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(labels);
        var paired = starts.Zip(labels, (t, l) => (Time: t, Label: l))
            .DistinctBy(p => p.Time)
            .OrderBy(p => p.Time.Ticks)
            .ToArray();
        if (paired.Length == 0)
        {
            throw new ArgumentException("A shift definition needs at least one start time.", nameof(starts));
        }
        return new ShiftDefinition(
            paired.Select(p => p.Time).ToImmutableArray(),
            paired.Select(p => p.Label).ToImmutableArray());
    }
}
