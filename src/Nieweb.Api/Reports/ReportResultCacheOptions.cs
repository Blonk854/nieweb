using System.ComponentModel.DataAnnotations;

namespace Nieweb.Api.Reports;

/// <summary>
/// Options for <see cref="IReportResultCache"/>, bound to configuration
/// section <c>Nieweb:Reports:ResultCache</c>.
/// </summary>
public sealed class ReportResultCacheOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Nieweb:Reports:ResultCache";

    /// <summary>
    /// Master switch. When <c>false</c> every lookup misses and every
    /// store is a no-op, so exports fall back to re-running the report.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a stored result stays usable, in seconds. Absolute (not
    /// sliding): a result can never be served for longer than this after
    /// the query that produced it, no matter how often it is exported.
    /// </summary>
    [Range(0, 3600)]
    public int TtlSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum number of cached results. Report outputs can be large
    /// (a wide DPMO table is megabytes), so the cache is deliberately
    /// small and evicts least-recently-used entries beyond this bound.
    /// </summary>
    [Range(1, 1024)]
    public int MaxEntries { get; set; } = 32;
}
