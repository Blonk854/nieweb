namespace Nieweb.Api.BoardSvgs;

/// <summary>
/// Thin filesystem abstraction used by
/// <see cref="IBoardSvgSyncCoordinator"/> so unit tests can drive
/// the sync logic without touching real UNC shares.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation (<see cref="DiskBoardSvgFileSystem"/>)
/// is a plain <see cref="File"/> / <see cref="Directory"/> wrapper.
/// Every method is expected to throw <see cref="IOException"/> on
/// I/O failures — the coordinator catches those and turns them into
/// per-source failure diagnostics.
/// </para>
/// </remarks>
public interface IBoardSvgFileSystem
{
    /// <summary>Creates <paramref name="directory"/> if missing.</summary>
    void EnsureDirectoryExists(string directory);

    /// <summary>Whether the directory exists and is accessible.</summary>
    bool DirectoryExists(string directory);

    /// <summary>Whether the file exists.</summary>
    bool FileExists(string path);

    /// <summary>
    /// Return metadata for <paramref name="path"/> without reading
    /// its contents, or <see langword="null"/> if the file does not
    /// exist. Used by <c>GET /api/board-svgs/{productName}</c> to
    /// compute the ETag / <c>Content-Length</c> before deciding
    /// whether to short-circuit with <c>304 Not Modified</c>.
    /// </summary>
    BoardSvgFileInfo? GetFileInfo(string path);

    /// <summary>
    /// Lists <c>.svg</c> files directly under
    /// <paramref name="directory"/> (non-recursive). Returns file
    /// name (with extension), full path, last-write timestamp, and
    /// size in bytes.
    /// </summary>
    IReadOnlyList<BoardSvgFileInfo> ListSvgFiles(string directory);

    /// <summary>Read a file's full contents.</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>Write a file (creates or overwrites).</summary>
    Task WriteAllBytesAsync(string path, byte[] content, CancellationToken cancellationToken);
}

/// <summary>Snapshot of an SVG file listed by
/// <see cref="IBoardSvgFileSystem.ListSvgFiles(string)"/>.</summary>
public sealed record BoardSvgFileInfo(
    string FullPath,
    string FileName,
    DateTime LastWriteTimeUtc,
    long SizeBytes);

/// <summary>
/// Default disk-backed <see cref="IBoardSvgFileSystem"/> — plain
/// <c>System.IO</c> calls, no fancy watchers. Registered as a
/// singleton in <c>Program.cs</c>.
/// </summary>
public sealed class DiskBoardSvgFileSystem : IBoardSvgFileSystem
{
    /// <inheritdoc />
    public void EnsureDirectoryExists(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
    }

    /// <inheritdoc />
    public bool DirectoryExists(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Directory.Exists(directory);
    }

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    /// <inheritdoc />
    public BoardSvgFileInfo? GetFileInfo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return null;
        }
        return new BoardSvgFileInfo(
            FullPath: info.FullName,
            FileName: info.Name,
            LastWriteTimeUtc: info.LastWriteTimeUtc,
            SizeBytes: info.Length);
    }

    /// <inheritdoc />
    public IReadOnlyList<BoardSvgFileInfo> ListSvgFiles(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<BoardSvgFileInfo>();
        }
        var results = new List<BoardSvgFileInfo>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.svg", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            results.Add(new BoardSvgFileInfo(
                FullPath: info.FullName,
                FileName: info.Name,
                LastWriteTimeUtc: info.LastWriteTimeUtc,
                SizeBytes: info.Length));
        }
        return results;
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytesAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteAllBytesAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        // Ensure the target directory exists so a fresh cache dir on
        // first run doesn't blow up with DirectoryNotFoundException.
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return File.WriteAllBytesAsync(path, content, cancellationToken);
    }
}
