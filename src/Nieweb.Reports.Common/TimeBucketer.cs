using System.Collections.Immutable;
using System.Globalization;

namespace Nieweb.Reports.Common;

/// <summary>
/// Decomposes a UTC time window into an ordered list of
/// <see cref="TimeBucketRange"/> slices according to a
/// <see cref="TimeBucket"/> size and (for shift buckets) a
/// <see cref="ShiftDefinition"/>.
/// </summary>
/// <remarks>
/// <para>
/// All arithmetic is done in the caller's site time zone (via
/// <c>TimeZoneInfo</c>) so shift and calendar boundaries match wall
/// clocks on the shop floor. The AOI Superviseur database stores
/// <c>Panel_Numeric_Date</c> as a UTC <c>time_t</c>; report authors are
/// expected to pass UTC ranges in and let the bucketer handle the
/// zone shift.
/// </para>
/// <para>
/// The <c>Decompose</c> method is deterministic and side-effect free.
/// A window that ends mid-bucket produces a truncated final range
/// (<see cref="TimeBucketRange.EndUtcExclusive"/> = the requested
/// window end), which matches Vieweb's behaviour of showing partial
/// days at the edges of a report window.
/// </para>
/// </remarks>
public static class TimeBucketer
{
    /// <summary>Culture used for all bucket labels (invariant).</summary>
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    /// <summary>ISO 8601 calendar used for week numbering (Monday-based).</summary>
    private static readonly Calendar IsoCalendar = CultureInfo.InvariantCulture.Calendar;

    /// <summary>
    /// Decomposes <c>[fromUtc, toUtcExclusive)</c> into buckets of the
    /// given size. Wall-clock alignment uses
    /// <paramref name="siteTimeZone"/>.
    /// </summary>
    /// <param name="fromUtc">Inclusive lower bound of the window (UTC).</param>
    /// <param name="toUtcExclusive">Exclusive upper bound of the window (UTC).</param>
    /// <param name="bucket">Bucket size.</param>
    /// <param name="siteTimeZone">
    /// Time zone used for wall-clock alignment. Callers typically pass
    /// the site's local time zone (e.g. <c>TimeZoneInfo.Local</c> on
    /// the on-prem AOI server).
    /// </param>
    /// <param name="shifts">
    /// Required when <paramref name="bucket"/> is
    /// <see cref="TimeBucket.Shift"/>; ignored otherwise.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="toUtcExclusive"/> is not strictly after
    /// <paramref name="fromUtc"/>, or the bucket is <c>Shift</c> and
    /// <paramref name="shifts"/> is null.
    /// </exception>
    public static IReadOnlyList<TimeBucketRange> Decompose(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        TimeBucket bucket,
        TimeZoneInfo siteTimeZone,
        ShiftDefinition? shifts = null)
    {
        ArgumentNullException.ThrowIfNull(siteTimeZone);
        if (toUtcExclusive <= fromUtc)
        {
            throw new ArgumentException(
                "toUtcExclusive must be strictly greater than fromUtc.",
                nameof(toUtcExclusive));
        }
        if (bucket == TimeBucket.Shift && shifts is null)
        {
            throw new ArgumentException(
                "A ShiftDefinition is required when bucket is Shift.",
                nameof(shifts));
        }

        return bucket switch
        {
            TimeBucket.Hour1 => DecomposeHours(fromUtc, toUtcExclusive, siteTimeZone, hours: 1),
            TimeBucket.Hour3 => DecomposeHours(fromUtc, toUtcExclusive, siteTimeZone, hours: 3),
            TimeBucket.Hour6 => DecomposeHours(fromUtc, toUtcExclusive, siteTimeZone, hours: 6),
            TimeBucket.Hour12 => DecomposeHours(fromUtc, toUtcExclusive, siteTimeZone, hours: 12),
            TimeBucket.Day => DecomposeDay(fromUtc, toUtcExclusive, siteTimeZone),
            TimeBucket.Week => DecomposeWeek(fromUtc, toUtcExclusive, siteTimeZone),
            TimeBucket.Month => DecomposeMonth(fromUtc, toUtcExclusive, siteTimeZone),
            TimeBucket.Shift => DecomposeShift(fromUtc, toUtcExclusive, siteTimeZone, shifts!),
            _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Unknown TimeBucket value."),
        };
    }

    private static ImmutableArray<TimeBucketRange> DecomposeHours(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        TimeZoneInfo tz,
        int hours)
    {
        // Snap the start down to the previous h-hour boundary in local
        // wall clock (24 must be divisible by "hours" — 1/3/6/12 all
        // satisfy this — so alignment on midnight is always sound).
        var fromLocal = TimeZoneInfo.ConvertTime(fromUtc, tz);
        var alignedHour = (fromLocal.Hour / hours) * hours;
        var cursorLocal = new DateTimeOffset(
            fromLocal.Year, fromLocal.Month, fromLocal.Day,
            alignedHour, 0, 0, fromLocal.Offset);

        var buckets = ImmutableArray.CreateBuilder<TimeBucketRange>();
        while (cursorLocal < toUtcExclusive)
        {
            var nextLocal = cursorLocal.AddHours(hours);
            var startUtc = ClampLower(cursorLocal.ToUniversalTime(), fromUtc);
            var endUtc = ClampUpper(nextLocal.ToUniversalTime(), toUtcExclusive);
            if (endUtc > startUtc)
            {
                var label = cursorLocal.ToString("yyyy-MM-dd HH:mm", InvariantCulture);
                buckets.Add(new TimeBucketRange(
                    label, startUtc, endUtc,
                    hours switch
                    {
                        1 => TimeBucket.Hour1,
                        3 => TimeBucket.Hour3,
                        6 => TimeBucket.Hour6,
                        _ => TimeBucket.Hour12,
                    }));
            }
            cursorLocal = nextLocal;
        }
        return buckets.ToImmutable();
    }

    private static ImmutableArray<TimeBucketRange> DecomposeDay(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        TimeZoneInfo tz)
    {
        var fromLocal = TimeZoneInfo.ConvertTime(fromUtc, tz);
        var cursorLocal = new DateTimeOffset(
            fromLocal.Year, fromLocal.Month, fromLocal.Day,
            0, 0, 0, fromLocal.Offset);

        var buckets = ImmutableArray.CreateBuilder<TimeBucketRange>();
        while (cursorLocal < toUtcExclusive)
        {
            // Advance by one wall-clock day. A DST transition day will
            // therefore be 23 or 25 hours long — that is the physically
            // correct interpretation for shop-floor reporting.
            var nextLocal = cursorLocal.AddDays(1);
            var startUtc = ClampLower(cursorLocal.ToUniversalTime(), fromUtc);
            var endUtc = ClampUpper(nextLocal.ToUniversalTime(), toUtcExclusive);
            if (endUtc > startUtc)
            {
                var label = cursorLocal.ToString("yyyy-MM-dd", InvariantCulture);
                buckets.Add(new TimeBucketRange(label, startUtc, endUtc, TimeBucket.Day));
            }
            cursorLocal = nextLocal;
        }
        return buckets.ToImmutable();
    }

    private static ImmutableArray<TimeBucketRange> DecomposeWeek(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        TimeZoneInfo tz)
    {
        var fromLocal = TimeZoneInfo.ConvertTime(fromUtc, tz);
        // ISO 8601: weeks start on Monday. DayOfWeek.Sunday==0,
        // Monday==1, so map Sunday to 6 and subtract to reach Monday.
        var dow = (int)fromLocal.DayOfWeek;
        var daysToMonday = dow == 0 ? 6 : dow - 1;
        var cursorLocal = new DateTimeOffset(
            fromLocal.Year, fromLocal.Month, fromLocal.Day,
            0, 0, 0, fromLocal.Offset).AddDays(-daysToMonday);

        var buckets = ImmutableArray.CreateBuilder<TimeBucketRange>();
        while (cursorLocal < toUtcExclusive)
        {
            var nextLocal = cursorLocal.AddDays(7);
            var startUtc = ClampLower(cursorLocal.ToUniversalTime(), fromUtc);
            var endUtc = ClampUpper(nextLocal.ToUniversalTime(), toUtcExclusive);
            if (endUtc > startUtc)
            {
                var isoWeek = IsoCalendar.GetWeekOfYear(
                    cursorLocal.DateTime,
                    CalendarWeekRule.FirstFourDayWeek,
                    DayOfWeek.Monday);
                // Use ISO week-numbering-year (falls back one December
                // week into the next calendar year and vice versa),
                // which matches how Vieweb rendered week labels.
                var isoYear = IsoWeekYear(cursorLocal.DateTime);
                var label = string.Create(InvariantCulture, $"{isoYear:D4}-W{isoWeek:D2}");
                buckets.Add(new TimeBucketRange(label, startUtc, endUtc, TimeBucket.Week));
            }
            cursorLocal = nextLocal;
        }
        return buckets.ToImmutable();
    }

    private static ImmutableArray<TimeBucketRange> DecomposeMonth(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        TimeZoneInfo tz)
    {
        var fromLocal = TimeZoneInfo.ConvertTime(fromUtc, tz);
        var cursorLocal = new DateTimeOffset(
            fromLocal.Year, fromLocal.Month, 1,
            0, 0, 0, fromLocal.Offset);

        var buckets = ImmutableArray.CreateBuilder<TimeBucketRange>();
        while (cursorLocal < toUtcExclusive)
        {
            var nextLocal = cursorLocal.AddMonths(1);
            var startUtc = ClampLower(cursorLocal.ToUniversalTime(), fromUtc);
            var endUtc = ClampUpper(nextLocal.ToUniversalTime(), toUtcExclusive);
            if (endUtc > startUtc)
            {
                var label = cursorLocal.ToString("yyyy-MM", InvariantCulture);
                buckets.Add(new TimeBucketRange(label, startUtc, endUtc, TimeBucket.Month));
            }
            cursorLocal = nextLocal;
        }
        return buckets.ToImmutable();
    }

    private static ImmutableArray<TimeBucketRange> DecomposeShift(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        TimeZoneInfo tz,
        ShiftDefinition shifts)
    {
        // Strategy: walk day-by-day in local time and, for each day,
        // emit every shift segment that intersects the requested UTC
        // window. Shifts that cross midnight (last shift when the
        // schedule wraps) are emitted as a single [start, next-day-first]
        // range so the caller does not need to stitch anything back.
        var fromLocal = TimeZoneInfo.ConvertTime(fromUtc, tz);
        var cursorDayLocal = new DateTimeOffset(
            fromLocal.Year, fromLocal.Month, fromLocal.Day,
            0, 0, 0, fromLocal.Offset).AddDays(-1);

        var buckets = ImmutableArray.CreateBuilder<TimeBucketRange>();
        while (cursorDayLocal < toUtcExclusive)
        {
            for (var i = 0; i < shifts.Starts.Length; i++)
            {
                var startTime = shifts.Starts[i];
                var startLocal = new DateTimeOffset(
                    cursorDayLocal.Year, cursorDayLocal.Month, cursorDayLocal.Day,
                    startTime.Hour, startTime.Minute, startTime.Second,
                    cursorDayLocal.Offset);

                // Next boundary is either the next shift on the same day
                // or the first shift on the following day.
                DateTimeOffset endLocal;
                if (i + 1 < shifts.Starts.Length)
                {
                    var nextTime = shifts.Starts[i + 1];
                    endLocal = new DateTimeOffset(
                        cursorDayLocal.Year, cursorDayLocal.Month, cursorDayLocal.Day,
                        nextTime.Hour, nextTime.Minute, nextTime.Second,
                        cursorDayLocal.Offset);
                }
                else
                {
                    var nextTime = shifts.Starts[0];
                    var nextDay = cursorDayLocal.AddDays(1);
                    endLocal = new DateTimeOffset(
                        nextDay.Year, nextDay.Month, nextDay.Day,
                        nextTime.Hour, nextTime.Minute, nextTime.Second,
                        nextDay.Offset);
                }

                var startUtc = startLocal.ToUniversalTime();
                var endUtc = endLocal.ToUniversalTime();
                if (endUtc <= fromUtc || startUtc >= toUtcExclusive)
                {
                    continue; // this shift segment is outside the window
                }

                var clampedStart = ClampLower(startUtc, fromUtc);
                var clampedEnd = ClampUpper(endUtc, toUtcExclusive);
                if (clampedEnd <= clampedStart)
                {
                    continue;
                }

                var label = string.Create(
                    InvariantCulture,
                    $"{startLocal:yyyy-MM-dd} {shifts.Labels[i]}");
                buckets.Add(new TimeBucketRange(
                    label, clampedStart, clampedEnd, TimeBucket.Shift, ShiftIndex: i));
            }
            cursorDayLocal = cursorDayLocal.AddDays(1);
        }
        return buckets.ToImmutable();
    }

    private static DateTimeOffset ClampLower(DateTimeOffset value, DateTimeOffset floor)
        => value < floor ? floor : value;

    private static DateTimeOffset ClampUpper(DateTimeOffset value, DateTimeOffset ceiling)
        => value > ceiling ? ceiling : value;

    /// <summary>
    /// Returns the ISO 8601 week-numbering year for the given date
    /// (may be one off from the calendar year around New Year).
    /// </summary>
    private static int IsoWeekYear(DateTime date)
    {
        var day = IsoCalendar.GetDayOfWeek(date);
        // ISO rule: shift so that Thursday falls in the correct year.
        var thursday = date.AddDays(3 - ((int)day + 6) % 7);
        return thursday.Year;
    }
}
