namespace Nieweb.DataSources;

/// <summary>
/// Half-open UTC time window: [StartUtc, EndUtcExclusive). Every query
/// that hits the large Superviseur tables (PANELS, CARDS, TESTED_OBJECT,
/// PIN, PIN_MEASURE, *_HISTO) is required to carry one - this is enforced
/// at the base-adapter layer to satisfy the "never a bare SELECT" rule
/// documented in .github/copilot-instructions.md.
/// </summary>
public readonly record struct DateRange
{
    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset EndUtcExclusive { get; }

    public DateRange(DateTimeOffset startUtc, DateTimeOffset endUtcExclusive)
    {
        if (endUtcExclusive <= startUtc)
        {
            throw new ArgumentException(
                $"End ({endUtcExclusive:o}) must be strictly after start ({startUtc:o}).",
                nameof(endUtcExclusive));
        }

        StartUtc = startUtc.ToUniversalTime();
        EndUtcExclusive = endUtcExclusive.ToUniversalTime();
    }

    /// <summary>Corresponding ANSI time_t (seconds since 1970-01-01 UTC) for Panel_Numeric_Date etc.</summary>
    public long StartEpochSeconds => StartUtc.ToUnixTimeSeconds();

    /// <summary>Corresponding ANSI time_t (seconds since 1970-01-01 UTC) for Panel_Numeric_Date etc.</summary>
    public long EndEpochSecondsExclusive => EndUtcExclusive.ToUnixTimeSeconds();

    public TimeSpan Duration => EndUtcExclusive - StartUtc;
}
