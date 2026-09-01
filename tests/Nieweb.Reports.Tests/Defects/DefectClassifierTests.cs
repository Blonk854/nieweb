using Nieweb.Reports.Common.Defects;

using Xunit;

namespace Nieweb.Reports.Tests.Defects;

public sealed class DefectClassifierTests
{
    [Fact]
    public void ParsePreferredOrderJson_IgnoresUnknownAndDuplicates()
    {
        var parsed = DefectClassifier.ParsePreferredOrderJson(
            "[\"LiftedLead\",\"UnknownBit\",\"ObjectMissing\",\"liftedlead\"]");

        Assert.Equal([DefectBit.LiftedLead, DefectBit.ObjectMissing], parsed);
    }

    [Fact]
    public void Classify_Real_UsesErrorTableAr()
    {
        // AOI has bits {1, 3, 25}. Post-review kept {1, 25}.
        var errorTable = (1L << 0) | (1L << 2) | (1L << 24);
        var errorTableAr = (1L << 0) | (1L << 24);
        var classifier = new DefectClassifier(preferredOrder: null);

        var bits = classifier
            .Classify(errorTable, errorTableAr, DefectClassFlavor.Real)
            .Select(i => i.Bit)
            .ToArray();

        Assert.Equal([DefectBit.ObjectMissing, DefectBit.LiftedLead], bits);
    }

    [Fact]
    public void Classify_Dummy_UsesAoiMinusAr()
    {
        // AOI bits {1, 3, 25}; AR bits {1, 25} => dummy={3}.
        var errorTable = (1L << 0) | (1L << 2) | (1L << 24);
        var errorTableAr = (1L << 0) | (1L << 24);
        var classifier = new DefectClassifier(preferredOrder: null);

        var bits = classifier
            .Classify(errorTable, errorTableAr, DefectClassFlavor.Dummy)
            .Select(i => i.Bit)
            .ToArray();

        Assert.Equal([DefectBit.SolderJointDefect], bits);
    }

    [Fact]
    public void Classify_AppliesPreferredOrderBeforeBitNumber()
    {
        var errorTable = (1L << 0) | (1L << 24);
        var errorTableAr = errorTable;
        var classifier = DefectClassifier.FromPreferredOrderJson(
            "[\"LiftedLead\",\"ObjectMissing\"]");

        var bits = classifier
            .Classify(errorTable, errorTableAr, DefectClassFlavor.Real)
            .Select(i => i.Bit)
            .ToArray();

        Assert.Equal([DefectBit.LiftedLead, DefectBit.ObjectMissing], bits);
    }

    [Fact]
    public void Classify_CanDropObsoleteBits()
    {
        // Bits 1 and 6; bit 6 is obsolete.
        var errorTable = (1L << 0) | (1L << 5);
        var errorTableAr = errorTable;
        var classifier = new DefectClassifier(preferredOrder: null);

        var bits = classifier
            .Classify(errorTable, errorTableAr, DefectClassFlavor.Real, includeObsolete: false)
            .Select(i => i.Bit)
            .ToArray();

        Assert.Equal([DefectBit.ObjectMissing], bits);
    }
}
