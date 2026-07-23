using System.ComponentModel.DataAnnotations;

namespace Nieweb.Api.BoardSvgs;

/// <summary>
/// Configuration for the board-SVG sync worker
/// (docs/phase-2.md §7.5 <c>TC4</c> Phase B). Bound to the
/// <c>Nieweb:BoardSvgSync</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The worker polls every enabled <see cref="Nieweb.Data.Entities.BoardSvgSource"/>
/// on a fixed interval and pulls the newest matching <c>.svg</c>
/// file per product into <see cref="CacheDirectory"/>. All I/O is
/// read+write plain-file — never <c>robocopy /MIR</c> or anything
/// else that could delete on the source share (TC4 §7.5 Point 6).
/// </para>
/// <para>
/// Set <see cref="Enabled"/> to <c>false</c> in test / CI hosts so
/// the background service never runs; endpoints (<c>/sync</c>,
/// <c>/status</c>) still work.
/// </para>
/// </remarks>
public sealed class BoardSvgSyncOptions
{
    /// <summary>
    /// Configuration section name (<c>Nieweb:BoardSvgSync</c>).
    /// </summary>
    public const string SectionName = "Nieweb:BoardSvgSync";

    /// <summary>
    /// Directory where cached SVG files live, keyed by product name
    /// (see <see cref="Nieweb.Api.BoardSvgs.IBoardSvgSyncCoordinator"/>).
    /// Default: <c>./data/board-svgs</c>.
    /// </summary>
    [Required]
    public string CacheDirectory { get; set; } = "./data/board-svgs";

    /// <summary>
    /// Polling interval in seconds. Default 3600 (1 hour). Must be
    /// positive; smaller values reduce staleness but hammer the
    /// machine shares.
    /// </summary>
    [Range(1, 24 * 3600)]
    public int IntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// Master toggle for the background service. Set to <c>false</c>
    /// in test hosts so the ticker never fires; on-demand sync via
    /// <c>POST /api/admin/board-svgs/sync</c> still runs.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
