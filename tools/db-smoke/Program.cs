using Nieweb.DataSources;
using Nieweb.DataSources.Sql;

// -----------------------------------------------------------------------------
// Smoke test for the Nieweb data-source layer.
//
// Loads credentials from ../../.env, opens a connection to each requested
// source using its read-only guarded adapter, and prints the machine list.
//
// Usage (from repo root):
//   dotnet run --project tools/db-smoke -- postreflow
//   dotnet run --project tools/db-smoke -- prereflow
//   dotnet run --project tools/db-smoke              # both
// -----------------------------------------------------------------------------

var wanted = args.Length == 0
    ? new[] { "postreflow", "prereflow" }
    : args.Select(a => a.ToLowerInvariant()).ToArray();

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var envPath = Path.Combine(repoRoot, ".env");
if (!File.Exists(envPath))
{
    Console.Error.WriteLine($"Missing {envPath}. Copy .env.example to .env and fill in the credentials.");
    return 1;
}

var env = LoadDotEnv(envPath);

var exitCode = 0;
foreach (var name in wanted)
{
    var prefix = name switch
    {
        "postreflow" => "AOI_POSTREFLOW_",
        "prereflow"  => "AOI_PREREFLOW_",
        _ => null,
    };
    if (prefix is null)
    {
        Console.Error.WriteLine($"Unknown source '{name}'. Use 'postreflow' or 'prereflow'.");
        exitCode = 2;
        continue;
    }

    try
    {
        var options = BuildOptions(env, prefix);
        SqlServerAoiSourceBase source = name == "postreflow"
            ? new HlyaoiSource(options)
            : new MeaoiSource(options);

        Console.WriteLine();
        Console.WriteLine($"=== {source.Descriptor.DisplayName} (schema {source.Descriptor.SchemaVersion}) ===");
        Console.WriteLine($"    Caps: {source.Descriptor.Caps}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var machines = await source.ListMachinesAsync(cts.Token);
        Console.WriteLine($"    {machines.Count} machines returned.");
        foreach (var m in machines)
        {
            Console.WriteLine($"      [{m.MachineId,3}] {m.MachineName,-24} type={m.MachineType} ({m.MachineTypeName})");
        }

        // --- PANELS smoke: 60 days ending at the source's most recent panel
        // (windows are sized per-source because HLYAOI post-reflow stopped
        // receiving new rows in Nov 2025, while MEAOI pre-reflow is live).
        var latest = await source.GetLatestPanelUtcAsync(cts.Token);
        if (latest is null)
        {
            Console.WriteLine();
            Console.WriteLine("    PANELS table is empty; skipping panel-query smoke.");
            continue;
        }
        var windowEnd = latest.Value.AddSeconds(1);   // inclusive of the latest row
        var windowStart = windowEnd - TimeSpan.FromDays(60);
        var panelQuery = new PanelQuery
        {
            Window = new DateRange(windowStart, windowEnd),
            PageSize = 10,
        };

        Console.WriteLine();
        Console.WriteLine($"    Latest panel: {latest:yyyy-MM-dd HH:mm:ss}Z");
        Console.WriteLine($"    Panels in [{windowStart:yyyy-MM-dd}, {windowEnd:yyyy-MM-dd}) (first {panelQuery.PageSize}):");
        var page = await source.QueryPanelsAsync(panelQuery, cts.Token);
        Console.WriteLine(
            $"    -> {page.Rows.Count} rows, HasMore={page.HasMore}, " +
            $"NextCursor={(page.NextCursor is PanelCursor c ? $"({c.LastPanelNumericDate}, {c.LastPanelId})" : "null")}.");
        foreach (var p in page.Rows.Take(5))
        {
            var ts = DateTimeOffset.FromUnixTimeSeconds(p.PanelNumericDate).UtcDateTime;
            Console.WriteLine(
                $"      [{p.PanelId,8}] {ts:yyyy-MM-dd HH:mm:ss}Z  M{p.MachineId,-2}  " +
                $"status={p.PanelStatus,2}  cards={p.NbOfValidCards,2}  " +
                $"barcode='{p.PanelBarCode}'");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"!!! {name} failed: {ex.GetType().Name}: {ex.Message}");
        exitCode = 3;
    }
}

return exitCode;

// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException($"Could not locate repo root from {start}.");
}

static Dictionary<string, string> LoadDotEnv(string path)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var raw in File.ReadAllLines(path))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var eq = line.IndexOf('=');
        if (eq <= 0) continue;
        var key = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1];
        }
        result[key] = value;
    }
    return result;
}

static AoiSourceOptions BuildOptions(Dictionary<string, string> env, string prefix)
{
    string Req(string key) =>
        env.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new InvalidOperationException($"Missing or empty {key} in .env");

    int OptInt(string key, int fallback) =>
        env.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

    return new AoiSourceOptions
    {
        Server = Req(prefix + "SERVER"),
        Database = Req(prefix + "DATABASE"),
        User = Req(prefix + "USER"),
        Password = Req(prefix + "PASSWORD"),
        ConnectTimeoutSeconds = OptInt("AOI_CONNECT_TIMEOUT", 15),
        QueryTimeoutSeconds = OptInt("AOI_QUERY_TIMEOUT", 30),
    };
}
