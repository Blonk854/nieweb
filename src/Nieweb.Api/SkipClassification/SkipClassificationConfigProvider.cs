using System.Text.Json;

using Nieweb.Api.Parameters;
using Nieweb.Reports.Common.Skips;

namespace Nieweb.Api.SkipClassification;

/// <summary>
/// Resolves the site's <see cref="SkipClassificationConfig"/> from the
/// internal <c>AppParameters</c> table (keys <c>skip.*</c>). Report
/// endpoints inject this and pass the result into the DPMO / FPY / Skip
/// Summary filters so the skip toggle and status filter honour the
/// admin-tuned thresholds and repair-button map rather than the baked-in
/// <see cref="SkipClassificationConfig.Default"/>.
/// </summary>
public interface ISkipClassificationConfigProvider
{
    /// <summary>
    /// Builds the current config. Any missing / malformed parameter
    /// falls back per-field to <see cref="SkipClassificationConfig.Default"/>,
    /// so the reports never fail on a bad admin edit.
    /// </summary>
    Task<SkipClassificationConfig> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IAppParameters"/>-backed implementation. Reads the four
/// <c>skip.*</c> rows and validates each; out-of-range or unparseable
/// values fall back to the corresponding <see cref="SkipClassificationConfig.Default"/>
/// field so a fat-fingered admin edit degrades gracefully.
/// </summary>
public sealed class SkipClassificationConfigProvider : ISkipClassificationConfigProvider
{
    /// <summary>Missing-ratio threshold (decimal, 0-1).</summary>
    public const string MissingRatioThresholdKey = "skip.missing_ratio_threshold";

    /// <summary>Minimum component floor (int, &gt;= 1).</summary>
    public const string MinComponentFloorKey = "skip.min_component_floor";

    /// <summary>Absolute missing floor (int, &gt;= 1).</summary>
    public const string AbsoluteMissingFloorKey = "skip.absolute_missing_floor";

    /// <summary>Repair-button label -&gt; meaning map (JSON string).</summary>
    public const string RepairButtonMeaningsKey = "skip.repair_button_meanings";

    private readonly IAppParameters _parameters;

    /// <summary>Creates the provider over the given parameter store.</summary>
    public SkipClassificationConfigProvider(IAppParameters parameters)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    /// <inheritdoc />
    public async Task<SkipClassificationConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _parameters.ListAsync(cancellationToken).ConfigureAwait(false);
        var byKey = new Dictionary<string, AppParameterRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            byKey[row.Key] = row;
        }

        var fallback = SkipClassificationConfig.Default;

        var ratio = ReadRatio(byKey, fallback.MissingRatioThreshold);
        var minFloor = ReadFloor(byKey, MinComponentFloorKey, fallback.MinComponentFloor);
        var absFloor = ReadFloor(byKey, AbsoluteMissingFloorKey, fallback.AbsoluteMissingFloor);
        var buttonMap = ReadButtonMap(byKey) ?? fallback.RepairButtonMeanings;

        return new SkipClassificationConfig(
            RepairButtonMeanings: buttonMap,
            MissingRatioThreshold: ratio,
            MinComponentFloor: minFloor,
            AbsoluteMissingFloor: absFloor);
    }

    private static double ReadRatio(Dictionary<string, AppParameterRow> rows, double fallback)
    {
        if (!rows.TryGetValue(MissingRatioThresholdKey, out var row))
        {
            return fallback;
        }
        try
        {
            var value = (double)row.AsDecimal();
            return value is >= 0d and <= 1d ? value : fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    private static int ReadFloor(Dictionary<string, AppParameterRow> rows, string key, int fallback)
    {
        if (!rows.TryGetValue(key, out var row))
        {
            return fallback;
        }
        try
        {
            var value = row.AsInt();
            return value >= 1 ? value : fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    private static Dictionary<string, RepairButtonMeaning>? ReadButtonMap(
        Dictionary<string, AppParameterRow> rows)
    {
        if (!rows.TryGetValue(RepairButtonMeaningsKey, out var row) || string.IsNullOrWhiteSpace(row.Value))
        {
            return null;
        }

        Dictionary<string, string>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Value);
        }
        catch (JsonException)
        {
            return null;
        }
        if (raw is null)
        {
            return null;
        }

        var map = new Dictionary<string, RepairButtonMeaning>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, meaning) in raw)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }
            if (Enum.TryParse<RepairButtonMeaning>(meaning, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                map[label] = parsed;
            }
        }
        return map.Count > 0 ? map : null;
    }
}
