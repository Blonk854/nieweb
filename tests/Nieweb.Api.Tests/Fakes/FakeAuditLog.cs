using Nieweb.Api.Audit;

namespace Nieweb.Api.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IAuditLog"/> that records every write so
/// unit tests can assert the coordinator emitted the right events.
/// </summary>
internal sealed class FakeAuditLog : IAuditLog
{
    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries;

    public Task WriteAsync(
        string eventType,
        string targetType,
        string targetId,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        _entries.Add(new Entry(eventType, targetType, targetId, details));
        return Task.CompletedTask;
    }

    public Task WriteAsync(
        string eventType,
        string targetType,
        string targetId,
        int? actorUserId,
        string actorDisplayName,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        _entries.Add(new Entry(eventType, targetType, targetId, details));
        return Task.CompletedTask;
    }

    public sealed record Entry(string EventType, string TargetType, string TargetId, object? Details);
}

