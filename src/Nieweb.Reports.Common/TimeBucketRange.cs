namespace Nieweb.Reports.Common;

/// <summary>
/// A single decomposed time-window slice. Emitted by
/// <see cref="TimeBucketer.Decompose"/> so report authors can iterate
/// buckets uniformly regardless of the underlying <see cref="TimeBucket"/>.
/// </summary>
/// <param name="Label">
/// Human-facing name for the bucket. Format depends on the bucket
/// kind: <c>"08:00"</c> for hour buckets, <c>"2026-07-21"</c> for day
/// buckets, <c>"2026-W30"</c> for ISO week buckets, <c>"2026-07"</c>
/// for month buckets, and the shift label
/// (e.g. <c>"2026-07-21 Shift 2"</c>) for shift buckets. Reports may
/// choose to display these verbatim on chart X-axes or re-format for
/// locale.
/// </param>
/// <param name="StartUtc">Inclusive lower bound in UTC.</param>
/// <param name="EndUtcExclusive">Exclusive upper bound in UTC.</param>
/// <param name="Bucket">The <see cref="TimeBucket"/> that produced this range.</param>
/// <param name="ShiftIndex">
/// Zero-based index into <see cref="ShiftDefinition.Starts"/> when
/// <see cref="Bucket"/> is <see cref="TimeBucket.Shift"/>; <c>null</c>
/// otherwise. Lets callers colour-code by shift without re-parsing the
/// label.
/// </param>
public sealed record TimeBucketRange(
    string Label,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtcExclusive,
    TimeBucket Bucket,
    int? ShiftIndex = null);
