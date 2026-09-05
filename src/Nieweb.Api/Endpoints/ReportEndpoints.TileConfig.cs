using System.Collections.Immutable;
using System.Text.Json;

using Nieweb.DataSources;
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
    /// Builds the <see cref="ParetoFilter"/> used by both saved-report
    /// Pareto export and <c>POST /api/reports/pareto/from-tile</c>.
    /// </summary>
    internal static (ParetoFilter? Filter, string? Error) TryBuildParetoTileFilter(
        DateRange window,
        IReadOnlyCollection<int>? machineIds,
        IReadOnlyCollection<int>? productIds,
        string? configJson)
    {
        var cfg = ParseParetoTileConfig(configJson);
        var axis = cfg.Axis is ParetoAxis.Shift or ParetoAxis.Day ? ParetoAxis.Defect : cfg.Axis;
        if (!TryParseTileFilters(configJson, out var filters, out var error))
        {
            return (null, error);
        }

        return (new ParetoFilter(
            Window: window,
            Axis: axis,
            Numerator: cfg.Numerator,
            Opportunity: cfg.Opportunity,
            Weight: cfg.Weight,
            TopN: cfg.TopN,
            IncludeOthersBucket: true,
            VitalFewThresholdPercent: cfg.VitalFewThresholdPercent,
            IncludeObsoleteBits: false,
            MachineIds: machineIds,
            ProductIds: productIds,
            DefectBits: null,
            Topologies: null,
            PartNumbers: null,
            JedecNames: null,
            SiteTimeZone: null,
            Shifts: null,
            Filters: filters), null);
    }

    /// <summary>
    /// Strict parser for a tile's <c>filters</c> array. Missing or empty
    /// arrays succeed with <c>filters = null</c>. Any malformed clause or
    /// validator failure returns an error; clauses are never dropped.
    /// </summary>
    internal static bool TryParseTileFilters(string? configJson, out FilterRequest? filters, out string? error)
    {
        filters = null;
        error = null;
        var root = TryParseObject(configJson);
        if (root is not { } obj || !obj.TryGetProperty("filters", out var filtersEl))
        {
            return true;
        }

        if (filtersEl.ValueKind != JsonValueKind.Array)
        {
            error = "filters must be an array";
            return false;
        }

        var clauses = ImmutableArray.CreateBuilder<FilterClause>();
        var index = 0;
        foreach (var el in filtersEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                error = $"filters[{index}]: clause must be an object";
                return false;
            }

            if (!el.TryGetProperty("field", out var fEl) || fEl.ValueKind != JsonValueKind.String)
            {
                error = $"filters[{index}]: missing field";
                return false;
            }

            if (!el.TryGetProperty("operator", out var oEl) || oEl.ValueKind != JsonValueKind.String)
            {
                error = $"filters[{index}]: missing operator";
                return false;
            }

            var fieldName = fEl.GetString();
            var opName = oEl.GetString();
            if (!Enum.TryParse<FilterField>(fieldName, ignoreCase: true, out var field) || !Enum.IsDefined(field))
            {
                error = $"filters[{index}]: unknown field '{fieldName}'";
                return false;
            }

            if (!Enum.TryParse<FilterOperator>(opName, ignoreCase: true, out var op) || !Enum.IsDefined(op))
            {
                error = $"filters[{index}]: unknown operator '{opName}'";
                return false;
            }

            var values = ImmutableArray.CreateBuilder<string>();
            if (el.TryGetProperty("values", out var vEl))
            {
                if (vEl.ValueKind != JsonValueKind.Array)
                {
                    error = $"filters[{index}]: values must be an array";
                    return false;
                }

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
                        default:
                            error = $"filters[{index}]: values must be strings or numbers";
                            return false;
                    }
                }
            }

            clauses.Add(new FilterClause(field, op, values.ToImmutable()));
            index++;
        }

        if (clauses.Count == 0)
        {
            return true;
        }

        var request = new FilterRequest(clauses.ToImmutable());
        var validation = FilterValidator.Validate(request);
        if (!validation.IsValid)
        {
            error = string.Join("; ", validation.Errors.Select(e => e.Key + ": " + e.Message));
            return false;
        }

        filters = request;
        return true;
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
