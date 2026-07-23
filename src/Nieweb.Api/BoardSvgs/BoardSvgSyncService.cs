using Microsoft.Extensions.Options;

namespace Nieweb.Api.BoardSvgs;

/// <summary>
/// Background poller that runs the board-SVG sync sweep every
/// <see cref="BoardSvgSyncOptions.IntervalSeconds"/> seconds
/// (docs/phase-2.md §7.5 <c>TC4</c> Phase B).
/// </summary>
/// <remarks>
/// <para>
/// The service is a singleton; it creates a scope per tick and
/// resolves <see cref="IBoardSvgSyncCoordinator"/> from the scope
/// so the coordinator's EF-backed dependencies get a fresh
/// <c>DbContext</c>.
/// </para>
/// <para>
/// When <see cref="BoardSvgSyncOptions.Enabled"/> is <c>false</c>
/// (typically in test hosts) the service returns immediately
/// without scheduling any work; the admin "sync now" endpoint is
/// unaffected.
/// </para>
/// <para>
/// Errors thrown by the coordinator would already be surfaced as
/// per-source / per-product entries in the result, so nothing bubbles
/// up here — but we still guard the tick with a broad catch so a
/// runtime bug can't crash the host.
/// </para>
/// </remarks>
public sealed partial class BoardSvgSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BoardSvgSyncOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BoardSvgSyncService> _logger;

    public BoardSvgSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<BoardSvgSyncOptions> options,
        TimeProvider time,
        ILogger<BoardSvgSyncService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            LogDisabled(_logger);
            return;
        }
        if (opts.IntervalSeconds <= 0)
        {
            LogInvalidInterval(_logger, opts.IntervalSeconds);
            return;
        }

        var interval = TimeSpan.FromSeconds(opts.IntervalSeconds);
        LogStarted(_logger, interval);

        using var timer = new PeriodicTimer(interval, _time);
        // Run once immediately so a fresh host doesn't wait a full
        // interval before the first sync sweep.
        await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Host stopping — expected.
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<IBoardSvgSyncCoordinator>();
            _ = await coordinator.SyncOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host stopping — swallow.
        }
#pragma warning disable CA1031 // Do not catch general exception types — the coordinator should never throw; if it does, log and keep the timer alive rather than crashing the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogTickFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 3520, Level = LogLevel.Information,
        Message = "BoardSvgSyncService started; interval={Interval}")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(EventId = 3521, Level = LogLevel.Information,
        Message = "BoardSvgSyncService disabled via Nieweb:BoardSvgSync:Enabled=false")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 3522, Level = LogLevel.Warning,
        Message = "BoardSvgSyncService invalid IntervalSeconds={IntervalSeconds}, refusing to start")]
    private static partial void LogInvalidInterval(ILogger logger, int intervalSeconds);

    [LoggerMessage(EventId = 3523, Level = LogLevel.Error,
        Message = "BoardSvgSyncService tick failed")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
