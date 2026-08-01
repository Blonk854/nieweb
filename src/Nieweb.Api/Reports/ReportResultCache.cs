using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

using Nieweb.DataSources;
using Nieweb.Reports;

namespace Nieweb.Api.Reports;

/// <summary>
/// Short-lived cache of report results, sitting in front of
/// <see cref="IReport{TInput, TOutput}.RunAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Motivation: a user who views a report and then exports it to CSV,
/// XLSX and PDF used to trigger four independent full passes over the
/// AOI database. The Superviseur DBs sit on the SMT line's critical
/// path, so redundant passes are not just slow for the user, they cost
/// inspection cycle time.
/// </para>
/// <para>
/// The contract is deliberately asymmetric: <b>the on-screen report
/// always runs fresh</b> and calls <see cref="Store"/>, while the
/// export endpoints call <see cref="GetOrRunAsync"/>. That means
/// (a) clicking "Run" can never hand back stale numbers, and (b) an
/// export is guaranteed to match the figures the user was just
/// looking at.
/// </para>
/// <para>
/// Results are not user-scoped: for a given (report, source, filter)
/// every authenticated user sees identical numbers, and
/// <see cref="IAoiSource"/> registrations are process-wide. The cache
/// key therefore carries no user identity, and sharing an entry across
/// users leaks nothing.
/// </para>
/// <para>
/// Caching is best-effort. If a key cannot be computed (an input type
/// that will not serialize) the call degrades to a plain
/// <c>RunAsync</c> rather than failing the request.
/// </para>
/// </remarks>
public interface IReportResultCache
{
    /// <summary>
    /// Records the output of a report run that the caller has already
    /// performed, so a subsequent export can reuse it.
    /// </summary>
    void Store<TInput, TOutput>(
        IReport<TInput, TOutput> report,
        IAoiSource source,
        TInput input,
        TOutput output)
        where TInput : notnull
        where TOutput : notnull;

    /// <summary>
    /// Returns the cached result for this (report, source, input) if one
    /// is still live, otherwise runs the report and caches the output.
    /// </summary>
    Task<TOutput> GetOrRunAsync<TInput, TOutput>(
        IReport<TInput, TOutput> report,
        IAoiSource source,
        TInput input,
        CancellationToken cancellationToken)
        where TInput : notnull
        where TOutput : notnull;
}

/// <summary>
/// <see cref="IMemoryCache"/>-backed <see cref="IReportResultCache"/>.
/// Uses its own <see cref="MemoryCache"/> instance with an entry-count
/// limit so a run of wide report exports cannot squeeze other caches
/// (or the process) out of memory.
/// </summary>
public sealed partial class MemoryReportResultCache : IReportResultCache, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly ReportResultCacheOptions _options;
    private readonly ILogger<MemoryReportResultCache> _logger;

    /// <summary>
    /// Key serialization must be deterministic and total. Enums go out as
    /// numbers, and <see cref="TimeZoneInfo"/> - which has no usable
    /// round-trip shape - is reduced to its id by
    /// <see cref="TimeZoneInfoIdConverter"/>.
    /// </summary>
    private static readonly JsonSerializerOptions _keyJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new TimeZoneInfoIdConverter() },
        WriteIndented = false,
    };

    public MemoryReportResultCache(
        IOptions<ReportResultCacheOptions> options,
        ILogger<MemoryReportResultCache> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _options.MaxEntries,
        });
    }

    /// <inheritdoc />
    public void Store<TInput, TOutput>(
        IReport<TInput, TOutput> report,
        IAoiSource source,
        TInput input,
        TOutput output)
        where TInput : notnull
        where TOutput : notnull
    {
        if (!_options.Enabled || _options.TtlSeconds <= 0)
        {
            return;
        }
        var key = TryBuildKey(report, source, input);
        if (key is null)
        {
            return;
        }
        Set(key, output);
    }

    /// <inheritdoc />
    public async Task<TOutput> GetOrRunAsync<TInput, TOutput>(
        IReport<TInput, TOutput> report,
        IAoiSource source,
        TInput input,
        CancellationToken cancellationToken)
        where TInput : notnull
        where TOutput : notnull
    {
        ArgumentNullException.ThrowIfNull(report);

        var key = _options.Enabled && _options.TtlSeconds > 0
            ? TryBuildKey(report, source, input)
            : null;

        if (key is not null && _cache.TryGetValue(key, out var cached) && cached is TOutput hit)
        {
            LogCacheHit(_logger, report.Descriptor.Id, source.Descriptor.Id);
            return hit;
        }

        var result = await report.RunAsync(source, input, cancellationToken).ConfigureAwait(false);
        if (key is not null)
        {
            LogCacheMiss(_logger, report.Descriptor.Id, source.Descriptor.Id);
            Set(key, result);
        }
        return result;
    }

    private void Set(string key, object value) =>
        _cache.Set(key, value, new MemoryCacheEntryOptions
        {
            // Absolute, never sliding: repeated exports must not be able
            // to keep a stale result alive indefinitely.
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.TtlSeconds),
            Size = 1,
        });

    /// <summary>
    /// Builds a collision-resistant key from the report id, the source id
    /// and a canonical JSON rendering of the filter. Returns <c>null</c>
    /// when the filter will not serialize, which disables caching for
    /// that call rather than failing it.
    /// </summary>
    private string? TryBuildKey<TInput, TOutput>(
        IReport<TInput, TOutput> report,
        IAoiSource source,
        TInput input)
        where TInput : notnull
        where TOutput : notnull
    {
        try
        {
            var json = JsonSerializer.Serialize(input, _keyJsonOptions);
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{report.Descriptor.Id}|{source.Descriptor.Id}|{hash}");
        }
#pragma warning disable CA1031 // best-effort: any serialization failure just disables caching
        catch (Exception ex)
        {
            LogKeyFailure(_logger, report.Descriptor.Id, typeof(TInput).Name, ex);
            return null;
        }
#pragma warning restore CA1031
    }

    public void Dispose() => _cache.Dispose();

    [LoggerMessage(EventId = 3701, Level = LogLevel.Debug,
        Message = "Report result cache HIT for {ReportId} on {SourceId}; skipping the AOI query.")]
    private static partial void LogCacheHit(ILogger logger, string reportId, string sourceId);

    [LoggerMessage(EventId = 3702, Level = LogLevel.Debug,
        Message = "Report result cache MISS for {ReportId} on {SourceId}; ran the AOI query.")]
    private static partial void LogCacheMiss(ILogger logger, string reportId, string sourceId);

    [LoggerMessage(EventId = 3703, Level = LogLevel.Warning,
        Message = "Could not build a cache key for {ReportId} (input {InputType}); caching disabled for this call.")]
    private static partial void LogKeyFailure(ILogger logger, string reportId, string inputType, Exception exception);
}

/// <summary>
/// Serializes a <see cref="TimeZoneInfo"/> as its <see cref="TimeZoneInfo.Id"/>.
/// Only ever used for cache-key generation - the id is what actually
/// distinguishes two filters.
/// </summary>
internal sealed class TimeZoneInfoIdConverter : JsonConverter<TimeZoneInfo>
{
    public override TimeZoneInfo Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Cache keys are write-only.");

    public override void Write(
        Utf8JsonWriter writer, TimeZoneInfo value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value?.Id);
    }
}
