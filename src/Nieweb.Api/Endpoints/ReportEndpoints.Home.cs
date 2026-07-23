using Microsoft.AspNetCore.Http.HttpResults;

using Nieweb.Api.Reports;

namespace Nieweb.Api.Endpoints;

/// <summary>
/// Home-page pinning surface (docs/phase-2.md §7.6 <c>RC4</c>).
/// Returns every report the owner has flagged
/// <c>IsPinnedHome=true</c> so the SPA can render a landing grid
/// for every signed-in user.
/// </summary>
/// <remarks>
/// Locked reports are intentionally included so users can discover
/// them and unlock on click; the SPA badges them accordingly. The
/// endpoint is auth-gated to any authenticated user (Reader+), not
/// Admin-only.
/// </remarks>
public static partial class ReportEndpoints
{
    /// <summary>Compact DTO returned by <c>GET /api/reports/home</c>.</summary>
    public sealed record HomeReportDto(
        int Id,
        string Title,
        string? Description,
        int? ReportGroupId,
        string? GroupName,
        string OwnerDisplayName,
        bool IsLocked,
        int? RefreshFrequencySeconds,
        int DisplayOrder,
        int EntityCount,
        DateTime LastModifiedUtc);

    private static async Task<Ok<IReadOnlyList<HomeReportDto>>> ListHomeReportsAsync(
        IReports reports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var rows = await reports.ListHomeReportsAsync(cancellationToken).ConfigureAwait(false);
        var dtos = rows.Select(r => new HomeReportDto(
            Id: r.Id,
            Title: r.Title,
            Description: r.Description,
            ReportGroupId: r.ReportGroupId,
            GroupName: r.GroupName,
            OwnerDisplayName: r.OwnerDisplayName,
            IsLocked: r.IsLocked,
            RefreshFrequencySeconds: r.RefreshFrequencySeconds,
            DisplayOrder: r.DisplayOrder,
            EntityCount: r.EntityCount,
            LastModifiedUtc: r.LastModifiedUtc)).ToList();
        return TypedResults.Ok<IReadOnlyList<HomeReportDto>>(dtos);
    }
}
