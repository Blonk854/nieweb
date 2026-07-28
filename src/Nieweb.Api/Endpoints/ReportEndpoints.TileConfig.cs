using System.Collections.Immutable;
using System.Text.Json;

using Nieweb.Filters;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Per-tile <c>ConfigJson</c> parsers for the multi-tile report export
/// path. These mirror the SPA contract in
/// <c>src/Nieweb.Web/src/components/reportConfig/tileConfig.ts</c>
/// field-for-field so a tile renders identically on screen and in the
/// exported PDF / CSV / XLSX (KPI parity).
/// </summary>
/// <remarks>
/// A tile's config carries ONLY the tile-specific analytic knobs. The
/// report-level filters (source, window, machine / product narrowing)
/// arrive via the export query string and are applied to every tile.
/// Every parser is total: malformed JSON, missing fields and unknown
/// enum values all fall back to the documented defaults.
/// </remarks>
public static partial class ReportEndpoints
{
    /// <summary>Tile-specific config for the <c>pareto</c> tile.</summary>
    internal readonly record struct ParetoTileConfig(
        ParetoAxis Axis,
        DpmoNumerator Numerator,
        DpmoOpportunity Opportunity,
        ParetoWeight Weight,
        int? TopN,
        double VitalFewThresholdPercent);

    /// <summary>
    /// Canonical default matching the SPA canvas <c>ParetoTile</c> and
    /// the stand-alone <c>/report/pareto</c> route ("DPMO real defects").
    /// </summary>
    internal static readonly ParetoTileConfig ParetoTileDefault = new(
        Axis: ParetoAxis.Defect,
        Numerator: DpmoNumerator.Real,
        Opportunity: DpmoOpportunity.Components,
        Weight: ParetoWeight.Count,
        TopN: 10,
        VitalFewThresholdPercent: 80.0);

    /// <summary>
    /// Parse a <c>pareto</c> tile's <c>ConfigJson</c>, substituting
    /// <see cref="ParetoTileDefault"/> per field.
    /// </summary>
    internal static ParetoTileConfig ParseParetoTileConfig(string? configJson)
    {
        var cfg = ParetoTileDefault;
        var root = TryParseObject(configJson);
        if (root is not { } obj)
        {
            return cfg;
        }

        return cfg with
        {
            Axis = ReadEnum(obj, "axis", cfg.Axis),
            Numerator = ReadEnum(obj, "numerator", cfg.Numerator),
            Opportunity = ReadEnum(obj, "opportunity", cfg.Opportunity),
            Weight = ReadEnum(obj, "weight", cfg.Weight),
            TopN = ReadTopN(obj, cfg.TopN),
            VitalFewThresholdPercent = ReadDouble(obj, "vitalFewThreshold", cfg.VitalFewThresholdPercent),
        };
    }

    /// <summary>
    /// Parse a <c>panelYield</c> tile's <c>OnlyLastInspection</c>
    /// override. Returns <c>null</c> when the tile does not set it, so
    /// the caller can inherit the report-level value.
    /// </summary>
    internal static bool? ParsePanelYieldOnlyLastInspection(string? configJson)
    {
        var root = TryParseObject(configJson);
        if (root is not { } obj)
        {
            return null;
        }

        if (obj.TryGetProperty("onlyLastInspection", out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(prop.GetString(), out var b) => b,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// Parses a per-tile Vieweb-style generic operator filter from a
    /// tile's <c>ConfigJson</c> <c>filters</c> array. Each element is
    /// <c>{ field, operator, values: [] }</c> using the canonical
    /// <see cref="FilterField"/> / <see cref="FilterOperator"/> enum
    /// names (case-insensitive). The whole request is validated with
    /// <see cref="FilterValidator"/>; a malformed or structurally
    /// invalid filter yields <c>null</c> (no filter) so one bad clause
    /// never blanks a report. Returns <c>null</c> when the tile carries
    /// no <c>filters</c> array.
    /// </summary>
    internal static FilterRequest? ParseTileFilters(string? configJson)
    {
        var root = TryParseObject(configJson);
        if (root is not { } obj
            || !obj.TryGetProperty("filters", out var filtersEl)
            || filtersEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var clauses = ImmutableArray.CreateBuilder<FilterClause>();
        foreach (var el in filtersEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object
                || !el.TryGetProperty("field", out var fEl) || fEl.ValueKind != JsonValueKind.String
                || !el.TryGetProperty("operator", out var oEl) || oEl.ValueKind != JsonValueKind.String
                || !Enum.TryParse<FilterField>(fEl.GetString(), ignoreCase: true, out var field) || !Enum.IsDefined(field)
                || !Enum.TryParse<FilterOperator>(oEl.GetString(), ignoreCase: true, out var op) || !Enum.IsDefined(op))
            {
                continue;
            }

            var values = ImmutableArray.CreateBuilder<string>();
            if (el.TryGetProperty("values", out var vEl) && vEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in vEl.EnumerateArray())
                {
                    switch (v.ValueKind)
                    {
                        case JsonValueKind.String:
                            values.Add(v.GetString()!);
                            break;
                        case JsonValueKind.Number:
                            values.Add(v.GetRawText());
                            break;
                    }
                }
            }

            clauses.Add(new FilterClause(field, op, values.ToImmutable()));
        }

        if (clauses.Count == 0)
        {
            return null;
        }

        var request = new FilterRequest(clauses.ToImmutable());
        return FilterValidator.Validate(request).IsValid ? request : null;
    }

    private static JsonElement? TryParseObject(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return doc.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
            // fall through — caller substitutes defaults
        }

        return null;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement obj, string name, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (obj.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String
            && Enum.TryParse<TEnum>(prop.GetString(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static int? ReadTopN(JsonElement obj, int? fallback)
    {
        if (!obj.TryGetProperty("topN", out var prop))
        {
            return fallback;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when prop.TryGetInt32(out var n) => n > 0 ? n : null,
            JsonValueKind.String when int.TryParse(prop.GetString(), out var n) => n > 0 ? n : null,
            _ => fallback,
        };
    }

    private static double ReadDouble(JsonElement obj, string name, double fallback)
    {
        if (obj.TryGetProperty(name, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.Number when prop.TryGetDouble(out var d) => d,
                JsonValueKind.String when double.TryParse(
                    prop.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var d) => d,
                _ => fallback,
            };
        }

        return fallback;
    }
}
