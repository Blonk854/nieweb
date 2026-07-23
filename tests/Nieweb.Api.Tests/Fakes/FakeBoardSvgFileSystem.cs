using System.Collections.Concurrent;

using Nieweb.Api.BoardSvgs;

namespace Nieweb.Api.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IBoardSvgFileSystem"/> used by the
/// <see cref="BoardSvgSyncCoordinator"/> unit tests. Simulates a
/// filesystem with an arbitrary set of "directories" (any string
/// prefix considered valid) and files.
/// </summary>
internal sealed class FakeBoardSvgFileSystem : IBoardSvgFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastWrite = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _existingDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unreachableDirs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Seed a file under <paramref name="directory"/> named <paramref name="fileName"/>.</summary>
    public void AddFile(string directory, string fileName, byte[] content, DateTime lastWriteUtc)
    {
        var dir = NormalizePath(directory);
        _ = _existingDirs.Add(dir);
        var full = NormalizePath(Combine(dir, fileName));
        _files[full] = content;
        _lastWrite[full] = lastWriteUtc;
    }

    /// <summary>Mark <paramref name="directory"/> as returning IOException on listing.</summary>
    public void MakeDirectoryUnreachable(string directory) => _unreachableDirs.Add(NormalizePath(directory));

    /// <summary>Total number of write calls this fake has served.</summary>
    public int WriteCount { get; private set; }

    public void EnsureDirectoryExists(string directory) => _existingDirs.Add(NormalizePath(directory));

    public bool DirectoryExists(string directory) => _existingDirs.Contains(NormalizePath(directory));

    public bool FileExists(string path) => _files.ContainsKey(NormalizePath(path));

    public BoardSvgFileInfo? GetFileInfo(string path)
    {
        var key = NormalizePath(path);
        if (!_files.TryGetValue(key, out var content))
        {
            return null;
        }
        return new BoardSvgFileInfo(
            FullPath: key,
            FileName: Path.GetFileName(key),
            LastWriteTimeUtc: _lastWrite[key],
            SizeBytes: content.LongLength);
    }

    public IReadOnlyList<BoardSvgFileInfo> ListSvgFiles(string directory)
    {
        var normalized = NormalizePath(directory);
        if (_unreachableDirs.Contains(normalized))
        {
            throw new IOException($"Fake: '{directory}' is unreachable");
        }
        if (!_existingDirs.Contains(normalized))
        {
            return Array.Empty<BoardSvgFileInfo>();
        }
        var prefix = NormalizeDir(normalized);
        var results = new List<BoardSvgFileInfo>();
        foreach (var kv in _files)
        {
            var path = kv.Key;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var fileName = path[prefix.Length..];
            // Non-recursive: skip anything with a further separator.
            if (fileName.Contains('/', StringComparison.Ordinal) || fileName.Contains('\\', StringComparison.Ordinal))
            {
                continue;
            }
            if (!fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            results.Add(new BoardSvgFileInfo(
                FullPath: path,
                FileName: fileName,
                LastWriteTimeUtc: _lastWrite[path],
                SizeBytes: kv.Value.LongLength));
        }
        return results;
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        var key = NormalizePath(path);
        if (!_files.TryGetValue(key, out var content))
        {
            throw new FileNotFoundException($"Fake: '{path}' not found");
        }
        return Task.FromResult(content);
    }

    public Task WriteAllBytesAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var key = NormalizePath(path);
        WriteCount++;
        _files[key] = content;
        _lastWrite[key] = DateTime.UtcNow;
        // Ensure the enclosing directory is registered.
        var dir = Path.GetDirectoryName(key);
        if (!string.IsNullOrEmpty(dir))
        {
            _ = _existingDirs.Add(dir);
        }
        return Task.CompletedTask;
    }

    private static string Combine(string directory, string fileName)
    {
        var prefix = NormalizeDir(directory);
        return prefix + fileName;
    }

    /// <summary>
    /// Canonicalize a path so callers using <see cref="Path.Combine(string, string)"/>
    /// (which uses <c>\</c> on Windows) hit the same dictionary entry as
    /// callers using literal forward slashes. UNC prefixes (<c>\\host\share</c>)
    /// are preserved as-is.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? path
            : path.Replace('\\', '/');
    }

    private static string NormalizeDir(string directory)
    {
        if (directory.EndsWith('/') || directory.EndsWith('\\'))
        {
            return directory;
        }
        // Detect UNC / backslash paths vs forward-slash.
        return directory.Contains('\\', StringComparison.Ordinal)
            ? directory + '\\'
            : directory + '/';
    }
}
