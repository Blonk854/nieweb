using Nieweb.Reports.Common;

using Xunit;

namespace Nieweb.Reports.Tests.Common;

/// <summary>
/// Behavioural tests for <see cref="TimeBucketer"/>. Every test runs
/// against a fixed UTC time zone offset (Europe/Paris = UTC+1 in
/// winter) so the boundary-alignment assertions are deterministic
/// regardless of the host machine's clock or locale.
/// </summary>
public sealed class TimeBucketerTests
{
    /// <summary>
    /// Europe/Paris — the primary AOI-line time zone Nieweb ships for.
    /// Using a real IANA zone (instead of a fixed offset) exercises the
    /// DST-aware code paths.
    /// </summary>
    private static readonly TimeZoneInfo Paris =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris");

    private static readonly TimeBucket[] ContiguousBuckets =
        { TimeBucket.Hour1, TimeBucket.Hour3, TimeBucket.Hour6, TimeBucket.Hour12, TimeBucket.Day };

    private static readonly TimeOnly[] DayNightStarts =
        { new(6, 0), new(18, 0) };
    private static readonly string[] DayNightLabels =
        { "Day", "Night" };

    private static DateTimeOffset ParisWinter(int y, int m, int d, int hh, int mm)
        => new(y, m, d, hh, mm, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Decompose_ThrowsWhenWindowIsInverted()
    {
        var start = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() =>
            TimeBucketer.Decompose(start, start, TimeBucket.Hour1, Paris));
        Assert.Throws<ArgumentException>(() =>
            TimeBucketer.Decompose(start, start.AddHours(-1), TimeBucket.Hour1, Paris));
    }

    [Fact]
    public void Decompose_ThrowsWhenShiftBucketMissesDefinition()
    {
        var from = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        Assert.Throws<ArgumentException>(() =>
            TimeBucketer.Decompose(from, to, TimeBucket.Shift, Paris));
    }

    [Fact]
    public void Hour1_ProducesOneBucketPerHourAlignedToWallClock()
    {
        // 06:15 Paris (winter, UTC+1) -> first bucket 06:00 local.
        var from = ParisWinter(2026, 1, 15, 6, 15).ToUniversalTime();
        var to = ParisWinter(2026, 1, 15, 9, 45).ToUniversalTime();

        var buckets = TimeBucketer.Decompose(from, to, TimeBucket.Hour1, Paris);

        // Expect buckets: 06:00-07:00, 07:00-08:00, 08:00-09:00, 09:00-10:00
        // The first is clipped at 06:15 (from) and last is clipped at 09:45 (to).
        Assert.Equal(4, buckets.Count);
        Assert.Equal(from, buckets[0].StartUtc);
        Assert.Equal("2026-01-15 06:00", buckets[0].Label);
        Assert.Equal("2026-01-15 09:00", buckets[^1].Label);
        Assert.Equal(to, buckets[^1].EndUtcExclusive);
        // Every bucket kind is Hour1.
        Assert.All(buckets, b => Assert.Equal(TimeBucket.Hour1, b.Bucket));
    }

    [Fact]
    public void Hour3_AlignsOnThreeHourBoundaries()
    {
        var from = ParisWinter(2026, 1, 15, 4, 0).ToUniversalTime();
        var to = ParisWinter(2026, 1, 15, 13, 0).ToUniversalTime();

        var buckets = TimeBucketer.Decompose(from, to, TimeBucket.Hour3, Paris);

        // Aligned starts local: 03:00, 06:00, 09:00, 12:00
        // First (03:00-06:00) clipped at 04:00 (from).
        Assert.Equal(4, buckets.Count);
        Assert.Equal("2026-01-15 03:00", buckets[0].Label);
        Assert.Equal(from, buckets[0].StartUtc);
        Assert.Equal("2026-01-15 12:00", buckets[^1].Label);
        Assert.Equal(to, buckets[^1].EndUtcExclusive);
    }

    [Fact]
    public void Day_ProducesOneBucketPerLocalDay()
    {
        var from = ParisWinter(2026, 1, 15, 10, 0).ToUniversalTime();
        var to = ParisWinter(2026, 1, 18, 4, 0).ToUniversalTime();

        var buckets = TimeBucketer.Decompose(from, to, TimeBucket.Day, Paris);

        Assert.Equal(4, buckets.Count);
        Assert.Equal("2026-01-15", buckets[0].Label);
        Assert.Equal("2026-01-16", buckets[1].Label);
        Assert.Equal("2026-01-17", buckets[2].Label);
        Assert.Equal("2026-01-18", buckets[3].Label);
        Assert.Equal(from, buckets[0].StartUtc);
        Assert.Equal(to, buckets[^1].EndUtcExclusive);
    }

    [Fact]
    public void Week_AlignsOnIsoMonday()
    {
        // 2026-01-15 is a Thursday; ISO week 3 of 2026 (Mon 2026-01-12).
        var from = ParisWinter(2026, 1, 15, 10, 0).ToUniversalTime();
        var to = ParisWinter(2026, 2, 2, 4, 0).ToUniversalTime();

        var buckets = TimeBucketer.Decompose(from, to, TimeBucket.Week, Paris);

        // Weeks: W03 (Jan 12), W04 (Jan 19), W05 (Jan 26), W06 (Feb 2).
        Assert.Equal(4, buckets.Count);
        Assert.Equal("2026-W03", buckets[0].Label);
        Assert.Equal("2026-W04", buckets[1].Label);
        Assert.Equal("2026-W05", buckets[2].Label);
        Assert.Equal("2026-W06", buckets[3].Label);
    }

    [Fact]
    public void Month_ProducesOneBucketPerCalendarMonth()
    {
        var from = ParisWinter(2026, 1, 20, 10, 0).ToUniversalTime();
        var to = new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.FromHours(2)).ToUniversalTime();

        var buckets = TimeBucketer.Decompose(from, to, TimeBucket.Month, Paris);

        Assert.Equal(4, buckets.Count);
        Assert.Equal("2026-01", buckets[0].Label);
        Assert.Equal("2026-02", buckets[1].Label);
        Assert.Equal("2026-03", buckets[2].Label);
        Assert.Equal("2026-04", buckets[3].Label);
    }

    [Fact]
    public void Shift_ThreeShiftDay_ProducesThreeSegmentsPerDay()
    {
        // Classic 3-shift schedule: 06:00, 14:00, 22:00 (wraps at 06:00 next day).
        var shifts = ShiftDefinition.FromStarts(new[]
        {
            new TimeOnly(6, 0),
            new TimeOnly(14, 0),
            new TimeOnly(22, 0),
        });

        // Single day 2026-01-15 in Paris.
        var from = ParisWinter(2026, 1, 15, 0, 0).ToUniversalTime();
        var to = ParisWinter(2026, 1, 16, 0, 0).ToUniversalTime();

        var buckets = TimeBucketer.Decompose(from, to, TimeBucket.Shift, Paris, shifts);

        // Expected windows (local):
        //   Shift 3 of previous day: 22:00 prev day -> 06:00 current -> partial
        //     (starts at 2026-01-14 22:00 -> clipped to 00:00; label uses start day 14 -> 15 handled below).
        //   Shift 1: 06:00 -> 14:00
        //   Shift 2: 14:00 -> 22:00
        //   Shift 3 of current day: 22:00 -> 06:00 next day -> clipped at 00:00.
        Assert.Equal(4, buckets.Count);
        Assert.Contains(buckets, b => b.Label == "2026-01-15 Shift 1" && b.ShiftIndex == 0);
        Assert.Contains(buckets, b => b.Label == "2026-01-15 Shift 2" && b.ShiftIndex == 1);
        // Two wrapping shift-3 segments: previous day clipped and current day clipped.
        Assert.Equal(2, buckets.Count(b => b.ShiftIndex == 2));
    }

    [Fact]
    public void Shift_SingleShiftSpansWholeDay()
    {
        // Continuous-run line: one shift starting at 00:00.
        var shifts = ShiftDefinition.FromStarts(new[] { new TimeOnly(0, 0) });

        var from = ParisWinter(2026, 1, 15, 0, 0).ToUniversalTime();
        var to = ParisWinter(2026, 1, 17, 0, 0).ToUniversalTime();

        var buckets = TimeBucketer.Decompose(from, to, TimeBucket.Shift, Paris, shifts);

        Assert.Equal(2, buckets.Count);
        Assert.Equal("2026-01-15 Shift 1", buckets[0].Label);
        Assert.Equal("2026-01-16 Shift 1", buckets[1].Label);
    }

    [Fact]
    public void Shift_WithCustomLabels_PreservesThem()
    {
        var shifts = ShiftDefinition.FromStarts(DayNightStarts, DayNightLabels);

        var from = ParisWinter(2026, 1, 15, 6, 0).ToUniversalTime();
        var to = ParisWinter(2026, 1, 16, 6, 0).ToUniversalTime();

        var buckets = TimeBucketer.Decompose(from, to, TimeBucket.Shift, Paris, shifts);

        Assert.Contains(buckets, b => b.Label == "2026-01-15 Day");
        Assert.Contains(buckets, b => b.Label == "2026-01-15 Night");
    }

    [Fact]
    public void Buckets_AreContiguousAndCoverTheEntireWindow()
    {
        var from = ParisWinter(2026, 1, 15, 6, 15).ToUniversalTime();
        var to = ParisWinter(2026, 1, 18, 22, 45).ToUniversalTime();

        foreach (var kind in ContiguousBuckets)
        {
            var buckets = TimeBucketer.Decompose(from, to, kind, Paris);
            Assert.NotEmpty(buckets);
            Assert.Equal(from, buckets[0].StartUtc);
            Assert.Equal(to, buckets[^1].EndUtcExclusive);
            for (var i = 1; i < buckets.Count; i++)
            {
                Assert.Equal(buckets[i - 1].EndUtcExclusive, buckets[i].StartUtc);
            }
        }
    }
}
