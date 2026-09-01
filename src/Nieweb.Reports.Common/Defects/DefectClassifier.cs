using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;

namespace Nieweb.Reports.Common.Defects;

/// <summary>
/// Classifies defects from <c>Error_Table</c> / <c>Error_Table_AR</c>
/// and emits them in a caller-defined display order.
/// </summary>
public sealed class DefectClassifier
{
    private readonly FrozenDictionary<DefectBit, int> _rankByBit;

    public DefectClassifier(IEnumerable<DefectBit>? preferredOrder)
    {
        var rankByBit = new Dictionary<DefectBit, int>();
        if (preferredOrder is not null)
        {
            var rank = 0;
            foreach (var bit in preferredOrder)
            {
                if (!DefectBitDecoder.ByBit.ContainsKey(bit))
                {
                    continue;
                }
                if (rankByBit.ContainsKey(bit))
                {
                    continue;
                }
                rankByBit[bit] = rank++;
            }
        }
        _rankByBit = rankByBit.ToFrozenDictionary();
    }

    /// <summary>
    /// Builds a classifier from a JSON array of <see cref="DefectBit"/>
    /// enum names. Unknown values are ignored.
    /// </summary>
    public static DefectClassifier FromPreferredOrderJson(string? preferredOrderJson)
        => new(ParsePreferredOrderJson(preferredOrderJson));

    /// <summary>
    /// Parses a JSON array of enum names into a distinct, ordered list
    /// of known <see cref="DefectBit"/> values.
    /// </summary>
    public static ImmutableArray<DefectBit> ParsePreferredOrderJson(string? preferredOrderJson)
    {
        if (string.IsNullOrWhiteSpace(preferredOrderJson))
        {
            return [];
        }

        string[]? names;
        try
        {
            names = JsonSerializer.Deserialize<string[]>(preferredOrderJson);
        }
        catch (JsonException)
        {
            return [];
        }
        if (names is null || names.Length == 0)
        {
            return [];
        }

        var seen = new HashSet<DefectBit>();
        var parsed = new List<DefectBit>(names.Length);
        foreach (var name in names)
        {
            if (!Enum.TryParse<DefectBit>(name, ignoreCase: true, out var bit)
                || !DefectBitDecoder.ByBit.ContainsKey(bit)
                || !seen.Add(bit))
            {
                continue;
            }
            parsed.Add(bit);
        }

        return [.. parsed];
    }

    /// <summary>
    /// Decodes and orders the selected defect flavour.
    /// </summary>
    public ImmutableArray<DefectBitInfo> Classify(
        long errorTable,
        long errorTableAr,
        DefectClassFlavor flavor,
        bool includeObsolete = true)
    {
        var mask = DefectBitDecoder.Bits1To25Mask;
        var relevant = flavor switch
        {
            DefectClassFlavor.Aoi => errorTable & mask,
            DefectClassFlavor.Real => errorTableAr & mask,
            DefectClassFlavor.Dummy => (errorTable & mask) & ~(errorTableAr & mask),
            _ => throw new ArgumentOutOfRangeException(nameof(flavor), flavor, "Unknown defect class flavor."),
        };

        var decoded = DefectBitDecoder.Decode(relevant);
        if (!includeObsolete)
        {
            decoded = decoded.Where(info => !info.IsObsolete);
        }

        return
        [
            .. decoded
                .OrderBy(info => _rankByBit.TryGetValue(info.Bit, out var rank) ? rank : int.MaxValue)
                .ThenBy(info => info.BitNumber)
        ];
    }
}

/// <summary>
/// Which bitfield semantics to classify.
/// </summary>
public enum DefectClassFlavor
{
    /// <summary>Raw AOI bits from <c>Error_Table</c>.</summary>
    Aoi = 0,
    /// <summary>Post-review real defects from <c>Error_Table_AR</c>.</summary>
    Real = 1,
    /// <summary>Dummy / false-call bits present in AOI but absent after review.</summary>
    Dummy = 2,
}
