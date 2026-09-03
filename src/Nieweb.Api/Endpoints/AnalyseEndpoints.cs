using System.Globalization;

using Nieweb.DataSources;
using Nieweb.Reports;
using Nieweb.Reports.Common;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Minimal-API endpoint group for AOI-only Analyse dashboard contracts.
/// </summary>
public static class AnalyseEndpoints
{
    public static IEndpointRouteBuilder MapAnalyseEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/analyse")
            .WithTags("Analyse")
            .RequireAuthorization();

        group.MapGet("/contracts", GetContractsAsync)
            .WithName("AnalyseContracts");
        group.MapGet("/live-summary", GetLiveSummaryAsync)
            .WithName("AnalyseLiveSummary");
        group.MapGet("/line-performance-summary", GetLinePerformanceSummaryAsync)
            .WithName("AnalyseLinePerformanceSummary");
        group.MapGet("/product-summary", GetProductSummaryAsync)
            .WithName("AnalyseProductSummary");
        group.MapGet("/product-detail/{productId:int}", GetProductDetailAsync)
            .WithName("AnalyseProductDetail");

        return routes;
    }

    private static async Task<IResult> GetContractsAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var source = FindSource(sources, sourceId);
        if (source is null)
        {
            return Results.Problem(
                title: "Unknown source id.",
                detail: $"No AOI source is registered with id '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var parseWindow = TryParseWindow(startUtc, endUtc);
        if (parseWindow.Error is not null)
        {
            return parseWindow.Error;
        }
        if (parseWindow.Window is null)
        {
            return Results.Problem(
                title: "Invalid date window.",
                detail: "Could not resolve a valid analysis window.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var window = parseWindow.Window.Value;

        var filter = new AnalyseDashboardFilter(
            Window: window,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            OnlyLastInspection: onlyLastInspection ?? true);

        var result = await AnalyseDashboardContractsReport.Instance
            .RunAsync(source, filter, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetLiveSummaryAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var source = FindSource(sources, sourceId);
        if (source is null)
        {
            return Results.Problem(
                title: "Unknown source id.",
                detail: $"No AOI source is registered with id '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var parseWindow = TryParseWindow(startUtc, endUtc);
        if (parseWindow.Error is not null)
        {
            return parseWindow.Error;
        }
        if (parseWindow.Window is null)
        {
            return Results.Problem(
                title: "Invalid date window.",
                detail: "Could not resolve a valid analysis window.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var window = parseWindow.Window.Value;

        var filter = new AnalyseDashboardFilter(
            Window: window,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            OnlyLastInspection: onlyLastInspection ?? true);

        var result = await AnalyseLiveSummaryReport.Instance
            .RunAsync(source, filter, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetLinePerformanceSummaryAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var source = FindSource(sources, sourceId);
        if (source is null)
        {
            return Results.Problem(
                title: "Unknown source id.",
                detail: $"No AOI source is registered with id '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var parseWindow = TryParseWindow(startUtc, endUtc);
        if (parseWindow.Error is not null)
        {
            return parseWindow.Error;
        }
        if (parseWindow.Window is null)
        {
            return Results.Problem(
                title: "Invalid date window.",
                detail: "Could not resolve a valid analysis window.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var window = parseWindow.Window.Value;

        var filter = new AnalyseDashboardFilter(
            Window: window,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            OnlyLastInspection: onlyLastInspection ?? true);

        var result = await AnalyseLinePerformanceReport.Instance
            .RunAsync(source, filter, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetProductSummaryAsync(
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var source = FindSource(sources, sourceId);
        if (source is null)
        {
            return Results.Problem(
                title: "Unknown source id.",
                detail: $"No AOI source is registered with id '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var parseWindow = TryParseWindow(startUtc, endUtc);
        if (parseWindow.Error is not null)
        {
            return parseWindow.Error;
        }
        if (parseWindow.Window is null)
        {
            return Results.Problem(
                title: "Invalid date window.",
                detail: "Could not resolve a valid analysis window.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var window = parseWindow.Window.Value;

        var filter = new AnalyseDashboardFilter(
            Window: window,
            MachineIds: ParseIntList(machineIds),
            ProductIds: ParseIntList(productIds),
            OnlyLastInspection: onlyLastInspection ?? true);

        var result = await AnalyseProductSummaryReport.Instance
            .RunAsync(source, filter, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetProductDetailAsync(
        int productId,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        bool? onlyLastInspection,
        string? bucket,
        IEnumerable<IAoiSource> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var source = FindSource(sources, sourceId);
        if (source is null)
        {
            return Results.Problem(
                title: "Unknown source id.",
                detail: $"No AOI source is registered with id '{sourceId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var parseWindow = TryParseWindow(startUtc, endUtc);
        if (parseWindow.Error is not null)
        {
            return parseWindow.Error;
        }
        if (parseWindow.Window is null)
        {
            return Results.Problem(
                title: "Invalid date window.",
                detail: "Could not resolve a valid analysis window.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        var window = parseWindow.Window.Value;

        var parsedBucket = ParseBucket(bucket);
        if (parsedBucket.Error is not null)
        {
            return parsedBucket.Error;
        }

        var filter = new AnalyseProductDetailFilter(
            Window: window,
            ProductId: productId,
            Bucket: parsedBucket.Bucket,
            MachineIds: ParseIntList(machineIds),
            OnlyLastInspection: onlyLastInspection ?? true);

        var result = await AnalyseProductDetailReport.Instance
            .RunAsync(source, filter, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static IAoiSource? FindSource(IEnumerable<IAoiSource> sources, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return sources.OrderBy(s => s.Descriptor.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        return sources.FirstOrDefault(s =>
            string.Equals(s.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static (DateRange? Window, IResult? Error) TryParseWindow(string? startUtc, string? endUtc)
    {
        var now = DateTimeOffset.UtcNow;

        if (!TryParseDateTimeOffset(startUtc, out var start))
        {
            return (null, Results.Problem(
                title: "Invalid startUtc parameter.",
                detail: "Use an ISO-8601 timestamp, e.g. 2026-08-01T00:00:00Z.",
                statusCode: StatusCodes.Status400BadRequest));
        }
        if (!TryParseDateTimeOffset(endUtc, out var end))
        {
            end = now;
        }
        if (end is null)
        {
            end = now;
        }

        if (start is null)
        {
            start = end.Value.AddDays(-1);
        }

        if (start >= end)
        {
            return (null, Results.Problem(
                title: "Invalid date window.",
                detail: "startUtc must be strictly earlier than endUtc.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        return (new DateRange(start.Value, end.Value), null);
    }

    private static bool TryParseDateTimeOffset(string? raw, out DateTimeOffset? value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = null;
            return true;
        }

        if (DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static (TimeBucket Bucket, IResult? Error) ParseBucket(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (TimeBucket.Day, null);
        }

        if (Enum.TryParse<TimeBucket>(raw, ignoreCase: true, out var parsed)
            && parsed is TimeBucket.Day or TimeBucket.Week)
        {
            return (parsed, null);
        }

        return (TimeBucket.Day, Results.Problem(
            title: "Invalid bucket parameter.",
            detail: "Use bucket=Day or bucket=Week.",
            statusCode: StatusCodes.Status400BadRequest));
    }

    private static HashSet<int>? ParseIntList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var set = new HashSet<int>();
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                set.Add(value);
            }
        }

        return set.Count == 0 ? null : set;
    }
}
