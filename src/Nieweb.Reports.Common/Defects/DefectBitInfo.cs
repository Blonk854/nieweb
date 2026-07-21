namespace Nieweb.Reports.Common.Defects;

/// <summary>
/// Metadata for a single <see cref="DefectBit"/>: bit position, mask
/// value, canonical name, display name, longer description, and
/// obsolescence status. Materialised once in
/// <see cref="DefectBitDecoder"/>.
/// </summary>
/// <param name="Bit">Enum member.</param>
/// <param name="BitNumber">1-based bit position (1..25).</param>
/// <param name="Mask"><c>1L &lt;&lt; (BitNumber - 1)</c>. Stored as <see cref="long"/> because <c>Error_Table_AR</c> is BIGINT.</param>
/// <param name="Name">Machine-readable slug, matches the enum member name.</param>
/// <param name="DisplayName">Short human-readable label used in tables / Pareto charts.</param>
/// <param name="Description">Long-form explanation shown in tooltips / help.</param>
/// <param name="IsObsolete">
/// <c>true</c> when the VIT documentation flags this bit as obsolete
/// (still present in archived data but replaced by a newer construct).
/// </param>
/// <param name="ObsolescenceNote">
/// Non-empty when <see cref="IsObsolete"/> is <c>true</c>. Explains the
/// modern replacement (e.g. "use Not_Inspected_Cause = 2").
/// </param>
/// <param name="AppearsOnPin">
/// <c>true</c> when this bit is meaningful on <c>PIN.Error_Table</c> /
/// <c>PIN.Error_Table_AR</c> (per VIT: bits 3, 4, 25 plus the overhang
/// bits 21, 22). Consumers can use this to short-circuit pin-level
/// Pareto tables to the pin-meaningful subset.
/// </param>
public sealed record DefectBitInfo(
    DefectBit Bit,
    int BitNumber,
    long Mask,
    string Name,
    string DisplayName,
    string Description,
    bool IsObsolete,
    string? ObsolescenceNote,
    bool AppearsOnPin);
