namespace Nieweb.Api.DataSources;

/// <summary>
/// Process-wide flag that turns on once an admin has mutated an
/// <see cref="Nieweb.Data.Entities.AoiSourceConfig"/> row. Cleared at
/// startup so a fresh process starts "clean".
/// </summary>
/// <remarks>
/// Live <c>IAoiSource</c> singletons are bound at boot time from DB
/// rows; row edits therefore do not affect the running process until
/// the API is restarted. The UI polls
/// <c>GET /api/admin/data-sources/restart-status</c> and surfaces a
/// yellow banner + "Restart API" button once
/// <see cref="IsPending"/> is <c>true</c>.
/// </remarks>
public interface IPendingRestartSignal
{
    /// <summary>True after the first mutation; cleared only on process restart.</summary>
    bool IsPending { get; }

    /// <summary>UTC timestamp of the first mutation that armed the signal.</summary>
    DateTime? SetUtc { get; }

    /// <summary>Optional freeform reason (e.g. "postreflow: password changed").</summary>
    string? Reason { get; }

    /// <summary>Arms the signal. Subsequent calls are no-ops (first-wins).</summary>
    void MarkPending(string reason, DateTime nowUtc);
}

/// <inheritdoc cref="IPendingRestartSignal"/>
public sealed class PendingRestartSignal : IPendingRestartSignal
{
    private readonly object _sync = new();
    private bool _pending;
    private DateTime? _setUtc;
    private string? _reason;

    /// <inheritdoc/>
    public bool IsPending
    {
        get
        {
            lock (_sync)
            {
                return _pending;
            }
        }
    }

    /// <inheritdoc/>
    public DateTime? SetUtc
    {
        get
        {
            lock (_sync)
            {
                return _setUtc;
            }
        }
    }

    /// <inheritdoc/>
    public string? Reason
    {
        get
        {
            lock (_sync)
            {
                return _reason;
            }
        }
    }

    /// <inheritdoc/>
    public void MarkPending(string reason, DateTime nowUtc)
    {
        lock (_sync)
        {
            if (_pending)
            {
                return;
            }
            _pending = true;
            _setUtc = nowUtc;
            _reason = reason;
        }
    }
}
