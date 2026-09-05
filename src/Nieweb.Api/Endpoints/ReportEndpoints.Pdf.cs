using System.Globalization;
using System.Security.Claims;
using Nieweb.Api.Reports;
using Nieweb.Api.SkipClassification;
using Nieweb.DataSources;
using Nieweb.Pdf;
using Nieweb.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// PDF export endpoints for the three shipped Nieweb reports
/// (panel-yield, DPMO table, Pareto). Every endpoint mirrors the
/// query-string contract of its CSV / XLSX sibling — we buffer the
/// PDF into a <see cref="MemoryStream"/> the same way ClosedXML does
/// because QuestPDF's output package is not forward-only either.
/// </summary>
/// <remarks>
/// TR3 close-out per docs/phase-2.md §7.2. Renderers live under
/// <c>src/Nieweb.Pdf/</c> and share the fixed corporate template
/// described in docs/phase-2.md §11.1.
/// </remarks>
public static partial class ReportEndpoints
{
    private const string PdfContentType = "application/pdf";

    /// <summary>
    /// Registers the three PDF-export endpoints on
    /// <paramref name="group"/>. Called from
    /// <see cref="MapReportEndpoints(IEndpointRouteBuilder)"/> so the
    /// PDF surface always ships with the JSON/CSV/XLSX one.
    /// </summary>
    private static void MapReportPdfEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/panel-yield/export.pdf", ExportPanelYieldPdfAsync)
             .WithName("ReportsPanelYieldExportPdf");

        group.MapGet("/dpmo-table/export.pdf", ExportDpmoTablePdfAsync)
             .WithName("ReportsDpmoTableExportPdf");

        group.MapGet("/fpy-table/export.pdf", ExportFpyTablePdfAsync)
             .WithName("ReportsFpyTableExportPdf");

        group.MapGet("/pareto/export.pdf", ExportParetoPdfAsync)
             .WithName("ReportsParetoExportPdf");

        group.MapGet("/fpy-trend/export.pdf", ExportFpyTrendPdfAsync)
             .WithName("ReportsFpyTrendExportPdf");

        group.MapGet("/dpmo-trend/export.pdf", ExportDpmoTrendPdfAsync)
             .WithName("ReportsDpmoTrendExportPdf");
    }

    private static async Task ExportDpmoTrendPdfAsync(
        HttpContext context,
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? opportunity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? excludeNogo,
        string? numerator,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        IReportResultCache resultCache,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (response, window, error) = await BuildDpmoTrendAsync(
            startUtc, endUtc, bucket, siteTimeZone, opportunity, skipExclusion, skipStatuses,
            lines, productIds, sourceIds, excludeNogo,
            sources, skipConfigProvider, resultCache, useCache: true,
            logger, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            await error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        // The view carries all three numerators, but a PDF is a flat artefact:
        // it has to commit to one. Real defects is the operational default.
        if (!TryParseEnumAlias<DpmoNumerator>(numerator, required: false, out var numeratorValue, out var numeratorError, defaultValue: DpmoNumerator.Real))
        {
            await ProblemFor("numerator", numeratorError!).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var displayTz = Nieweb.Pdf.NiewebPdfTimestamps.Resolve(siteTimeZone);
        var stem = string.Create(CultureInfo.InvariantCulture,
            $"dpmo-trend-{response!.Bucket}-{response.Opportunity}-{window.StartUtc:yyyyMMdd}-{window.EndUtcExclusive:yyyyMMdd}")
            .ToLowerInvariant();

        await WritePdfAsync(
            context,
            filenameStem: stem,
            render: stream => DpmoTrendPdfRenderer.Render(
                response!.Sources, response.Bucket, response.Opportunity, response.SkipExclusion,
                numeratorValue, ResolveDisplayName(context.User), stream, timeZone: displayTz),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExportFpyTrendPdfAsync(
        HttpContext context,
        string? startUtc,
        string? endUtc,
        string? bucket,
        string? siteTimeZone,
        string? granularity,
        string? skipExclusion,
        string? skipStatuses,
        string? lines,
        string? productIds,
        string? sourceIds,
        bool? onlyLastInspection,
        bool? excludeNogo,
        string? flavor,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        IReportResultCache resultCache,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (response, window, error) = await BuildFpyTrendAsync(
            startUtc, endUtc, bucket, siteTimeZone, granularity, skipExclusion, skipStatuses,
            lines, productIds, sourceIds, onlyLastInspection, excludeNogo,
            sources, skipConfigProvider, resultCache, useCache: true,
            logger, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            await error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        if (!TryParseEnumAlias<FpyFlavor>(flavor, required: false, out var flavorValue, out var flavorError, defaultValue: FpyFlavor.Diagnostic))
        {
            await ProblemFor("flavor", flavorError!).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var displayTz = Nieweb.Pdf.NiewebPdfTimestamps.Resolve(siteTimeZone);
        var stem = string.Create(CultureInfo.InvariantCulture,
            $"fpy-trend-{response!.Bucket}-{response.Granularity}-{window.StartUtc:yyyyMMdd}-{window.EndUtcExclusive:yyyyMMdd}")
            .ToLowerInvariant();

        await WritePdfAsync(
            context,
            filenameStem: stem,
            render: stream => FpyTrendPdfRenderer.Render(
                response!.Sources, response.Bucket, response.Granularity, response.SkipExclusion,
                flavorValue, ResolveDisplayName(context.User), stream, timeZone: displayTz),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExportPanelYieldPdfAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        string? tz,
        IEnumerable<IAoiSource> sources,
        IReportResultCache resultCache,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildPanelYieldRequest(
            sourceId, startUtc, endUtc, machineIds, productIds,
            onlyLastInspection, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var displayTz = Nieweb.Pdf.NiewebPdfTimestamps.Resolve(tz);
        LogRunning(logger, built.Source!.Descriptor.Id, built.Filter!.Window.StartUtc, built.Filter.Window.EndUtcExclusive);
        var result = await resultCache
            .GetOrRunAsync(PanelYieldByLineReport.Instance, built.Source, built.Filter, cancellationToken)
            .ConfigureAwait(false);

        await WritePdfAsync(
            context,
            filenameStem: string.Create(CultureInfo.InvariantCulture,
                $"panel-yield-{built.Source.Descriptor.Id}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}"),
            render: stream => PanelYieldPdfRenderer.Render(result, ResolveDisplayName(context.User), stream, timeZone: displayTz),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExportDpmoTablePdfAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? groupBy,
        string? numerator,
        string? opportunity,
        string? machineIds,
        string? productIds,
        bool? includeObsoleteBits,
        string? skipExclusion,
        string? skipStatuses,
        bool? excludeNogo,
        string? tz,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        IReportResultCache resultCache,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildDpmoRequest(
            sourceId, startUtc, endUtc, groupBy, numerator, opportunity,
            machineIds, productIds, includeObsoleteBits, skipExclusion, skipStatuses, excludeNogo, sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        LogRunningDpmo(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.GroupBy,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        var effectiveFilter = built.Filter! with
        {
            SkipConfig = await skipConfigProvider.GetAsync(cancellationToken).ConfigureAwait(false),
        };
        var result = await resultCache
            .GetOrRunAsync(DpmoTableReport.Instance, built.Source, effectiveFilter, cancellationToken)
            .ConfigureAwait(false);

        var dpmoTz = Nieweb.Pdf.NiewebPdfTimestamps.Resolve(tz);
        await WritePdfAsync(
            context,
            filenameStem: string.Create(CultureInfo.InvariantCulture,
                $"dpmo-{built.Source.Descriptor.Id}-{result.GroupBy}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}"),
            render: stream => DpmoTablePdfRenderer.Render(result, ResolveDisplayName(context.User), stream, timeZone: dpmoTz),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExportFpyTablePdfAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? granularity,
        string? groupBy,
        string? machineIds,
        string? productIds,
        bool? onlyLastInspection,
        string? skipExclusion,
        string? skipStatuses,
        bool? excludeNogo,
        string? tz,
        IEnumerable<IAoiSource> sources,
        ISkipClassificationConfigProvider skipConfigProvider,
        IReportResultCache resultCache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await BuildFpyResultAsync(
            context, sourceId, startUtc, endUtc, granularity, groupBy,
            machineIds, productIds, onlyLastInspection, skipExclusion, skipStatuses,
            excludeNogo, sources, skipConfigProvider, resultCache, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return;
        }

        var fpyTz = Nieweb.Pdf.NiewebPdfTimestamps.Resolve(tz);
        await WritePdfAsync(
            context,
            filenameStem: string.Create(CultureInfo.InvariantCulture,
                $"fpy-{result.Source.Id}-{result.Granularity}-{result.Window.StartUtc:yyyyMMdd}-{result.Window.EndUtcExclusive:yyyyMMdd}"),
            render: stream => FpyTablePdfRenderer.Render(result, ResolveDisplayName(context.User), stream, timeZone: fpyTz),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExportParetoPdfAsync(
        HttpContext context,
        string? sourceId,
        string? startUtc,
        string? endUtc,
        string? axis,
        string? numerator,
        string? opportunity,
        string? weight,
        int? topN,
        bool? includeOthers,
        double? vitalFewThreshold,
        bool? includeObsoleteBits,
        string? machineIds,
        string? productIds,
        string? defectBits,
        string? topologies,
        string? partNumbers,
        string? jedecNames,
        string? siteTimeZone,
        string? shifts,
        string? skipExclusion,
        string? skipStatuses,
        bool? excludeNogo,
        IEnumerable<IAoiSource> sources,
        IReportResultCache resultCache,
        ILogger<ReportsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var built = TryBuildParetoRequest(
            sourceId, startUtc, endUtc, axis, numerator, opportunity, weight,
            topN, includeOthers, vitalFewThreshold, includeObsoleteBits,
            machineIds, productIds,
            defectBits, topologies, partNumbers, jedecNames,
            siteTimeZone, shifts,
            skipExclusion, skipStatuses,
            excludeNogo,
            sources);
        if (built.Error is not null)
        {
            await built.Error.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        LogRunningPareto(
            logger,
            built.Source!.Descriptor.Id,
            built.Filter!.Axis,
            built.Filter.Numerator,
            built.Filter.Window.StartUtc,
            built.Filter.Window.EndUtcExclusive);

        ParetoResult result;
        try
        {
            result = await resultCache
                .GetOrRunAsync(ParetoReport.Instance, built.Source, built.Filter, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            await Results.Problem(
                title: "Invalid Pareto filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        catch (ArgumentException ex)
        {
            await Results.Problem(
                title: "Invalid Pareto filter: " + ex.Message,
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        await WritePdfAsync(
            context,
            filenameStem: string.Create(CultureInfo.InvariantCulture,
                $"pareto-{built.Source.Descriptor.Id}-{result.Axis}-{built.Filter.Window.StartUtc:yyyyMMdd}-{built.Filter.Window.EndUtcExclusive:yyyyMMdd}"),
            render: stream => ParetoPdfRenderer.Render(
                result,
                ResolveDisplayName(context.User),
                stream,
                timeZone: built.Filter.SiteTimeZone ?? Nieweb.Pdf.NiewebPdfTimestamps.Resolve(null),
                vitalFewThresholdPercent: result.VitalFewThresholdPercent),
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Buffers the PDF into a <see cref="MemoryStream"/>, then writes
    /// it to the response with the standard <c>attachment</c>
    /// Content-Disposition. Uses a 32 KiB initial capacity because
    /// typical single-report PDFs settle between 20-80 KB.
    /// </summary>
    private static async Task WritePdfAsync(
        HttpContext context,
        string filenameStem,
        Action<Stream> render,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(32 * 1024);
        render(buffer);
        buffer.Position = 0;

        var filename = filenameStem + ".pdf";
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = PdfContentType;
        context.Response.ContentLength = buffer.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";

        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the user-facing name printed in the PDF footer. Same
    /// precedence as <c>IAuditLog</c>'s actor resolution: the
    /// <c>name</c> claim (typically set by our OIDC provider), falling
    /// back to <see cref="ClaimTypes.Name"/>, then
    /// <see cref="System.Security.Principal.IIdentity.Name"/>, then
    /// the literal <c>"unknown"</c>.
    /// </summary>
    private static string ResolveDisplayName(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is null || !principal.Identity.IsAuthenticated)
        {
            return "unknown";
        }
        return principal.FindFirst("name")?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? principal.Identity.Name
            ?? "unknown";
    }
}
