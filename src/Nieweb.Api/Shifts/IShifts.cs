using Nieweb.Data.Entities;
using Nieweb.Reports.Common;

namespace Nieweb.Api.Shifts;

/// <summary>
/// Read/write access to the site-wide shift cycle (Vieweb §2.4.4 /
/// docs/phase-2.md §7.4 <c>PL1</c>). The service owns the persistent
/// <see cref="ShiftBreakpoint"/> rows in the internal DB and hands the
/// report layer an in-memory <see cref="ShiftDefinition"/> when
/// requested — that is the object CR1's Pareto / CR3 Trend / PC1
/// dashboard use to bucket panels by shift.
/// </summary>
public interface IShifts
{
    /// <summary>
    /// Returns every breakpoint ordered by <c>(Hour, Minute)</c>
    /// ascending — the same order shifts fire on the wall clock.
    /// </summary>
    Task<IReadOnlyList<ShiftBreakpointRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically replaces the entire cycle with <paramref name="entries"/>.
    /// Duplicate <c>(Hour, Minute)</c> pairs and out-of-range values are
    /// rejected with <see cref="ArgumentException"/>. Passing an empty
    /// list clears the cycle.
    /// </summary>
    Task<IReadOnlyList<ShiftBreakpointRow>> ReplaceAsync(
        IEnumerable<ShiftBreakpointInput> entries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience: returns a fully-built <see cref="ShiftDefinition"/>
    /// consumable by <c>TimeBucketer</c>, or <c>null</c> when the cycle
    /// is empty (i.e. shifts are not configured for this site).
    /// </summary>
    Task<ShiftDefinition?> BuildShiftDefinitionAsync(CancellationToken cancellationToken = default);
}

/// <summary>Row snapshot returned by list / replace.</summary>
public sealed record ShiftBreakpointRow(
    int Id,
    int Hour,
    int Minute,
    string? Label,
    int DisplayOrder,
    DateTime CreatedUtc,
    DateTime LastModifiedUtc);

/// <summary>Write input for <see cref="IShifts.ReplaceAsync"/>.</summary>
public sealed record ShiftBreakpointInput(int Hour, int Minute, string? Label);
