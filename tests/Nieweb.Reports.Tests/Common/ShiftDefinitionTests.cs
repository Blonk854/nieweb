using Nieweb.Reports.Common;

using Xunit;

namespace Nieweb.Reports.Tests.Common;

public sealed class ShiftDefinitionTests
{
    [Fact]
    public void FromStarts_SortsAndDedupes()
    {
        var d = ShiftDefinition.FromStarts(new[]
        {
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            new TimeOnly(14, 0),
            new TimeOnly(6, 0), // duplicate
        });

        Assert.Equal(3, d.Starts.Length);
        Assert.Equal(new TimeOnly(6, 0), d.Starts[0]);
        Assert.Equal(new TimeOnly(14, 0), d.Starts[1]);
        Assert.Equal(new TimeOnly(22, 0), d.Starts[2]);
        Assert.Equal("Shift 1", d.Labels[0]);
        Assert.Equal("Shift 3", d.Labels[2]);
    }

    private static readonly TimeOnly[] TwoShiftStarts =
        { new(18, 0), new(6, 0) };
    private static readonly string[] TwoShiftLabels =
        { "Night", "Day" };

    [Fact]
    public void FromStarts_WithLabels_KeepsLabelsAlignedAfterSort()
    {
        var d = ShiftDefinition.FromStarts(TwoShiftStarts, TwoShiftLabels);

        Assert.Equal(new TimeOnly(6, 0), d.Starts[0]);
        Assert.Equal("Day", d.Labels[0]);
        Assert.Equal("Night", d.Labels[1]);
    }

    [Fact]
    public void FromStarts_EmptyThrows()
    {
        Assert.Throws<ArgumentException>(() =>
            ShiftDefinition.FromStarts(Array.Empty<TimeOnly>()));
    }
}
