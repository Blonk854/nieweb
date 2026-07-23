using Nieweb.Api.BoardSvgs;

namespace Nieweb.Api.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IBoardSvgSources"/> for coordinator tests.
/// Tracks calls to <see cref="RecordSyncSuccessAsync"/> /
/// <see cref="RecordSyncFailureAsync"/> so tests can assert the
/// coordinator wrote the right status transitions.
/// </summary>
internal sealed class FakeBoardSvgSources : IBoardSvgSources
{
    private readonly List<BoardSvgSourceRow> _rows = new();
    private int _nextId = 1;

    public IReadOnlyList<int> RecordedSuccesses => _successes;
    public IReadOnlyList<(int Id, string Error)> RecordedFailures => _failures;

    private readonly List<int> _successes = new();
    private readonly List<(int Id, string Error)> _failures = new();

    public BoardSvgSourceRow Seed(
        string machineName,
        string uncPath,
        bool isEnabled = true,
        DateTime? lastSyncedUtc = null,
        DateTime? lastSyncErrorUtc = null,
        string? lastSyncError = null)
    {
        var now = DateTime.UtcNow;
        var row = new BoardSvgSourceRow(
            _nextId++,
            machineName,
            uncPath,
            isEnabled,
            lastSyncedUtc,
            lastSyncErrorUtc,
            lastSyncError,
            now,
            now);
        _rows.Add(row);
        return row;
    }

    public Task<IReadOnlyList<BoardSvgSourceRow>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BoardSvgSourceRow>>(_rows.ToList());

    public Task<BoardSvgSourceRow?> GetAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.FirstOrDefault(r => r.Id == id));

    public Task<BoardSvgSourceRow> CreateAsync(string machineName, string uncPath, bool isEnabled, CancellationToken cancellationToken = default)
        => Task.FromResult(Seed(machineName, uncPath, isEnabled));

    public Task<BoardSvgSourceRow?> UpdateAsync(int id, string machineName, string uncPath, bool isEnabled, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task RecordSyncSuccessAsync(int id, CancellationToken cancellationToken = default)
    {
        _successes.Add(id);
        return Task.CompletedTask;
    }

    public Task RecordSyncFailureAsync(int id, string errorMessage, CancellationToken cancellationToken = default)
    {
        _failures.Add((id, errorMessage));
        return Task.CompletedTask;
    }
}
