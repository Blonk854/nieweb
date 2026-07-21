using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Nieweb.Reports.Tests.Snapshots;

/// <summary>
/// Minimal snapshot testing helper. Serializes the actual value as
/// indented JSON, compares byte-for-byte to a file that lives next to
/// the test source (under a <c>Snapshots</c> subdirectory), and either
/// writes the file on first run / when <c>UPDATE_SNAPSHOTS=1</c> is
/// set, or writes an <c>.actual.json</c> diff sibling and fails the
/// test otherwise.
/// </summary>
/// <remarks>
/// We serialize with <see cref="JsonNamingPolicy.CamelCase"/> so
/// snapshots resemble the eventual REST payload from the /api/reports
/// endpoint (R3), which lets the same fixtures double as documentation
/// for front-end consumers.
/// </remarks>
internal static class SnapshotAssert
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Assert that <paramref name="actual"/> matches the snapshot
    /// named <paramref name="snapshotName"/>. Snapshots are stored at
    /// <c>&lt;test-file-dir&gt;/Snapshots/&lt;snapshotName&gt;.expected.json</c>.
    /// </summary>
    /// <param name="actual">Value to serialize and compare.</param>
    /// <param name="snapshotName">
    /// Bare snapshot name (no extension). Choose a slug that matches
    /// the test method name so failures are easy to trace.
    /// </param>
    /// <param name="testFilePath">
    /// Injected by the compiler via <see cref="CallerFilePathAttribute"/>;
    /// do not pass explicitly.
    /// </param>
    public static void Match(
        object actual,
        string snapshotName,
        [CallerFilePath] string testFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrEmpty(snapshotName);
        ArgumentException.ThrowIfNullOrEmpty(testFilePath);

        var testDir = Path.GetDirectoryName(testFilePath)
            ?? throw new InvalidOperationException($"Cannot resolve directory of '{testFilePath}'.");
        var snapshotDir = Path.Combine(testDir, "Snapshots");
        Directory.CreateDirectory(snapshotDir);
        var expectedPath = Path.Combine(snapshotDir, snapshotName + ".expected.json");
        var actualPath = Path.Combine(snapshotDir, snapshotName + ".actual.json");

        var actualJson = JsonSerializer.Serialize(actual, actual.GetType(), _serializerOptions);
        var updateRequested = string.Equals(
            Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS"),
            "1",
            StringComparison.Ordinal);

        if (updateRequested || !File.Exists(expectedPath))
        {
            File.WriteAllText(expectedPath, actualJson);
            // Clean any leftover diff from a previous failing run.
            if (File.Exists(actualPath))
            {
                File.Delete(actualPath);
            }
            return;
        }

        var expectedJson = File.ReadAllText(expectedPath);

        // Normalize line endings so a Windows checkout (CRLF via
        // .gitattributes eol=crlf on *.json, or a fresh clone with
        // core.autocrlf=true) does not spuriously diverge from a
        // Linux CI runner.
        if (Normalize(expectedJson) == Normalize(actualJson))
        {
            if (File.Exists(actualPath))
            {
                File.Delete(actualPath);
            }
            return;
        }

        File.WriteAllText(actualPath, actualJson);
        Assert.Fail(
            $"Snapshot mismatch for '{snapshotName}'.\n" +
            $"  expected: {expectedPath}\n" +
            $"  actual:   {actualPath}\n" +
            $"To accept the new output, set UPDATE_SNAPSHOTS=1 and rerun.");
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);
}
