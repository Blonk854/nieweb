using Xunit;

namespace Nieweb.DataSources.Sql.Tests;

public sealed class DateRangeTests
{
    [Fact]
    public void Constructor_NormalizesToUtc()
    {
        var start = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.FromHours(-5));
        var end = new DateTimeOffset(2025, 1, 1, 11, 0, 0, TimeSpan.FromHours(-5));
        var range = new DateRange(start, end);
        Assert.Equal(TimeSpan.Zero, range.StartUtc.Offset);
        Assert.Equal(TimeSpan.Zero, range.EndUtcExclusive.Offset);
        Assert.Equal(TimeSpan.FromHours(1), range.Duration);
    }

    [Fact]
    public void Constructor_ThrowsWhenEndNotAfterStart()
    {
        var start = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new DateRange(start, start));
        Assert.Throws<ArgumentException>(() => new DateRange(start, start.AddSeconds(-1)));
    }

    [Fact]
    public void EpochSeconds_MatchUnixTimeOfBoundaries()
    {
        var start = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(1970, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var range = new DateRange(start, end);
        Assert.Equal(0L, range.StartEpochSeconds);
        Assert.Equal(3600L, range.EndEpochSecondsExclusive);
    }
}
