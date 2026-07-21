using Nieweb.Reports.Common.Defects;

using Xunit;

namespace Nieweb.Reports.Tests.Defects;

public class DefectBitDecoderCatalogueTests
{
    [Fact]
    public void All_HasEntryForEveryBitOneThroughTwentyFive_InOrder()
    {
        Assert.Equal(25, DefectBitDecoder.All.Length);
        for (var i = 0; i < 25; i++)
        {
            var info = DefectBitDecoder.All[i];
            Assert.Equal(i + 1, info.BitNumber);
            Assert.Equal((DefectBit)(i + 1), info.Bit);
            Assert.Equal(1L << i, info.Mask);
            Assert.False(string.IsNullOrEmpty(info.DisplayName));
            Assert.False(string.IsNullOrEmpty(info.Description));
        }
    }

    [Fact]
    public void ObsoleteBits_MatchTheVitDocumentedList()
    {
        // Per vit-aoi-database SKILL.md: bits 6, 12, 13, 14, 15, 16,
        // 17, 18 are documented as obsolete.
        var obsolete = DefectBitDecoder.All.Where(i => i.IsObsolete).Select(i => i.BitNumber).ToArray();
        Assert.Equal([6, 12, 13, 14, 15, 16, 17, 18], obsolete);
        foreach (var info in DefectBitDecoder.All)
        {
            if (info.IsObsolete)
            {
                Assert.False(string.IsNullOrEmpty(info.ObsolescenceNote));
            }
            else
            {
                Assert.Null(info.ObsolescenceNote);
            }
        }
    }

    [Fact]
    public void PinLevelBits_MatchesTheVitDocumentedSubset()
    {
        // Skill: "PIN.Error_Table / Error_Table_AR ... only bits 3, 4,
        // 25 (joint, bridge, lifted lead) plus the overhang bits are
        // typically populated." -> bits {3, 4, 21, 22, 25}.
        var pin = DefectBitDecoder.PinLevelBits.Select(i => i.BitNumber).OrderBy(n => n).ToArray();
        Assert.Equal([3, 4, 21, 22, 25], pin);
    }

    [Fact]
    public void ByBit_And_ByMask_AreBothPopulated_AndConsistent()
    {
        Assert.Equal(25, DefectBitDecoder.ByBit.Count);
        Assert.Equal(25, DefectBitDecoder.ByMask.Count);
        foreach (var info in DefectBitDecoder.All)
        {
            Assert.Same(info, DefectBitDecoder.ByBit[info.Bit]);
            Assert.Same(info, DefectBitDecoder.ByMask[info.Mask]);
        }
    }
}

public class DefectBitDecoderDecodeTests
{
    [Fact]
    public void Decode_Zero_YieldsNothing()
    {
        Assert.Empty(DefectBitDecoder.Decode(0));
    }

    [Fact]
    public void Decode_SingleBit_YieldsThatBit()
    {
        // Bit 3 = solder joint defect (mask = 4).
        var decoded = DefectBitDecoder.Decode(4).Single();
        Assert.Equal(DefectBit.SolderJointDefect, decoded.Bit);
    }

    [Fact]
    public void Decode_YieldsBitsInAscendingOrder()
    {
        // Bits 25 (16777216) + 3 (4) + 22 (2097152) set out of order
        // in the source integer — decoder must yield in bit-number
        // order (3, 22, 25) so Pareto charts render the same regardless
        // of underlying storage layout.
        var errorTable = (1L << 24) | (1L << 21) | (1L << 2);
        var bits = DefectBitDecoder.Decode(errorTable).Select(i => i.BitNumber).ToArray();
        Assert.Equal([3, 22, 25], bits);
    }

    [Fact]
    public void Decode_IgnoresUpperClassificationBitsAboveBit25()
    {
        // Error_Table_AR is BIGINT — upper 32 bits reserved for
        // classification. A stray upper bit must NOT surface as an
        // unknown defect.
        var lowerBit = 1L << 20;    // bit 21 (SideOverhang)
        var classificationBit = 1L << 40; // bit 41 (should be ignored)
        var errorTable = lowerBit | classificationBit;

        var decoded = DefectBitDecoder.Decode(errorTable).ToArray();
        Assert.Single(decoded);
        Assert.Equal(DefectBit.SideOverhang, decoded[0].Bit);
    }

    [Fact]
    public void HasBit_ReflectsBitmaskAndIgnoresUpperBits()
    {
        var errorTable = (1L << 2) | (1L << 40); // bit 3 set + upper stray
        Assert.True(DefectBitDecoder.HasBit(errorTable, DefectBit.SolderJointDefect));
        Assert.False(DefectBitDecoder.HasBit(errorTable, DefectBit.SolderBridgeDefect));
    }

    [Fact]
    public void HasBit_UnknownEnumValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DefectBitDecoder.HasBit(0, (DefectBit)999));
    }

    [Fact]
    public void CountBits_IsThePopulationCountOfLow25Bits()
    {
        Assert.Equal(0, DefectBitDecoder.CountBits(0));
        Assert.Equal(1, DefectBitDecoder.CountBits(1L << 24));
        Assert.Equal(3, DefectBitDecoder.CountBits((1L << 0) | (1L << 12) | (1L << 24)));

        // Upper bits do not inflate the count.
        Assert.Equal(1, DefectBitDecoder.CountBits((1L << 0) | (1L << 40)));
    }

    [Fact]
    public void IsComponentGood_RequiresZeroBits_AndZeroNotInspectedCause()
    {
        // Both zero -> good.
        Assert.True(DefectBitDecoder.IsComponentGood(errorTableAr: 0, notInspectedCause: 0));
        // Bit set -> not good.
        Assert.False(DefectBitDecoder.IsComponentGood(errorTableAr: 4, notInspectedCause: 0));
        // Not_Inspected_Cause != 0 -> not good even if bits are clear.
        Assert.False(DefectBitDecoder.IsComponentGood(errorTableAr: 0, notInspectedCause: 1));
        // Upper classification bit alone (no low bit) does not disqualify.
        Assert.True(DefectBitDecoder.IsComponentGood(errorTableAr: 1L << 40, notInspectedCause: 0));
    }
}

/// <summary>
/// Regression test for Vieweb bug <b>#11211</b> (wrong defect
/// displayed). Rebuilds a synthetic panel with several concurrent
/// defect bits set and asserts the decoder identifies each of them
/// by the correct <see cref="DefectBit"/> label. Any change to the
/// bit-to-name mapping table would break this test.
/// </summary>
public class DefectBitDecoderRegressionTests
{
    // A synthetic component with:
    //   - Object missing            (bit 1, mask 1)
    //   - Polarity error            (bit 2, mask 2)
    //   - Solder joint defect       (bit 3, mask 4)
    //   - Tilt error                (bit 20, mask 524288)
    //   - Foreign material          (bit 23, mask 4194304)
    //   - Lifted lead               (bit 25, mask 16777216)
    // AND a stray upper-32 classification bit (bit 40) to prove the
    // decoder ignores those. Combined value: 1|2|4|524288|4194304|16777216
    // plus 1L << 39.
    private const long SyntheticErrorTable
        = 1L | 2L | 4L | (1L << 19) | (1L << 22) | (1L << 24) | (1L << 39);

    [Fact]
    public void Bug11211_DecodeYieldsExactlyTheSixKnownDefects_InBitOrder()
    {
        var decoded = DefectBitDecoder.Decode(SyntheticErrorTable)
            .Select(info => info.Bit)
            .ToArray();

        Assert.Equal(
        [
            DefectBit.ObjectMissing,
            DefectBit.PolarityError,
            DefectBit.SolderJointDefect,
            DefectBit.TiltError,
            DefectBit.ForeignMaterialDetected,
            DefectBit.LiftedLead,
        ], decoded);
    }

    [Fact]
    public void Bug11211_CountBitsMatchesSixKnownDefects()
    {
        Assert.Equal(6, DefectBitDecoder.CountBits(SyntheticErrorTable));
    }

    [Fact]
    public void Bug11211_EachExpectedBit_IsIndividuallyHit()
    {
        Assert.True(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.ObjectMissing));
        Assert.True(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.PolarityError));
        Assert.True(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.SolderJointDefect));
        Assert.True(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.TiltError));
        Assert.True(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.ForeignMaterialDetected));
        Assert.True(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.LiftedLead));

        // And a couple of the un-set bits are correctly reported false —
        // the actual #11211 failure mode was reporting an adjacent bit
        // instead of the one that fired.
        Assert.False(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.SolderBridgeDefect));
        Assert.False(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.OcvError));
        Assert.False(DefectBitDecoder.HasBit(SyntheticErrorTable, DefectBit.ComponentPresentButShouldNotBe));
    }

    [Fact]
    public void Bug11211_DisplayNamesForEachBit_MatchCanonicalLabels()
    {
        // Guards against the specific bug variant where the correct
        // enum was returned but rendered with the wrong label.
        var decoded = DefectBitDecoder.Decode(SyntheticErrorTable)
            .ToDictionary(info => info.Bit, info => info.DisplayName);

        Assert.Equal("Object missing", decoded[DefectBit.ObjectMissing]);
        Assert.Equal("Polarity error", decoded[DefectBit.PolarityError]);
        Assert.Equal("Solder joint defect", decoded[DefectBit.SolderJointDefect]);
        Assert.Equal("Tilt error", decoded[DefectBit.TiltError]);
        Assert.Equal("Foreign material", decoded[DefectBit.ForeignMaterialDetected]);
        Assert.Equal("Lifted lead", decoded[DefectBit.LiftedLead]);
    }
}
